import asyncio
import contextlib
import logging
import re
from collections.abc import AsyncGenerator, AsyncIterator
from typing import Final

from fastapi import APIRouter, Request, Response
from fastapi.responses import StreamingResponse

from app.cache import IdempotencyCache, TtsPhraseCache, fingerprint, phrase_key
from app.deps import CacheDep, PhraseCacheDep, ProvidersDep
from app.errors import ERROR_RESPONSES, ApiError
from app.limits import LATENCY_BUDGET_S
from app.providers.base import ProviderError
from app.request_log import bind_request_id
from app.routers._common import charge
from app.schemas import TtsRequest

log = logging.getLogger("usa.backend")

MEDIA_TYPE: Final = "audio/mpeg"
SCOPE: Final = "tts"
# First byte must arrive within the endpoint budget; the rest of the clip within this.
STREAM_TOTAL_S: Final = 20.0
# The voice id becomes a path segment of the provider URL, so it may never carry `/`, `.`, `?` or `#`.
VOICE_ID: Final = re.compile(r"\A[A-Za-z0-9_-]{1,64}\Z")

router = APIRouter(prefix="/v1", tags=["speech"], responses=ERROR_RESPONSES)


async def _first_chunk(stream: AsyncGenerator[bytes]) -> bytes:
    try:
        async with asyncio.timeout(LATENCY_BUDGET_S["tts"]):
            return await anext(stream)
    except StopAsyncIteration:
        raise ProviderError(retryable=True) from None


async def _relay(
    first: bytes,
    stream: AsyncGenerator[bytes],
    cache: IdempotencyCache,
    phrases: TtsPhraseCache,
    request_id: str,
    key: str | None,
) -> AsyncIterator[bytes]:
    """Forwards chunks as they arrive; only a complete clip is cached, so a broken stream is retried.

    Both caches are written before the generator ends, i.e. before the end of the chunked body. A
    failure after the first byte aborts the connection (the client sees a lost stream and retries);
    it is logged as one line, never a traceback.
    """
    chunks = [first]
    try:
        yield first
        async with asyncio.timeout(STREAM_TOTAL_S):
            async for chunk in stream:
                chunks.append(chunk)
                yield chunk
    except BaseException as exc:
        cache.fail(SCOPE, request_id, ProviderError(retryable=True))
        if isinstance(exc, Exception):
            log.warning("tts stream ended early type=%s", type(exc).__name__)
        raise
    finally:
        with contextlib.suppress(Exception):
            await stream.aclose()
    audio = b"".join(chunks)
    cache.complete(SCOPE, request_id, audio)
    if key is not None:
        await phrases.put(key, audio)


async def _await_duplicate(pending: asyncio.Future[object]) -> Response:
    """A retry that arrives while the first stream is still running gets the finished clip."""
    async with asyncio.timeout(LATENCY_BUDGET_S["tts"] + STREAM_TOTAL_S):
        audio = await asyncio.shield(pending)
    if not isinstance(audio, bytes):
        raise ProviderError(retryable=True)
    return Response(content=audio, media_type=MEDIA_TYPE)


@router.post(
    "/tts",
    response_class=Response,
    responses={200: {"content": {MEDIA_TYPE: {}}, "description": "MPEG audio, streamed"}},
)
async def tts(
    body: TtsRequest, request: Request, providers: ProvidersDep, cache: CacheDep, phrases: PhraseCacheDep
) -> Response:
    bind_request_id(request, body.request_id)
    if not VOICE_ID.match(body.voice_id):
        raise ApiError(422, "invalid_request", retryable=False)
    payload_fingerprint = fingerprint(body.model_dump_json())
    repeated = cache.lookup(SCOPE, body.request_id, payload_fingerprint)
    if isinstance(repeated, bytes):
        return Response(content=repeated, media_type=MEDIA_TYPE)
    pending = cache.pending(SCOPE, body.request_id, payload_fingerprint)
    if pending is not None:
        return await _await_duplicate(pending)

    text_to_speech = providers.text_to_speech
    key = phrase_key(body.voice_id, text_to_speech.model_id, body.text) if body.cacheable else None
    if key is not None:
        stored = await phrases.get(key)
        if stored is not None:
            cache.store(SCOPE, body.request_id, payload_fingerprint, stored)
            return Response(content=stored, media_type=MEDIA_TYPE)

    charge(request, "tts")
    cache.begin(SCOPE, body.request_id, payload_fingerprint)
    stream = text_to_speech.stream(body.voice_id, body.text)
    try:
        first = await _first_chunk(stream)
    except BaseException as exc:
        cache.fail(SCOPE, body.request_id, exc)
        with contextlib.suppress(Exception):
            await stream.aclose()
        raise
    return StreamingResponse(
        _relay(first, stream, cache, phrases, body.request_id, key), media_type=MEDIA_TYPE
    )
