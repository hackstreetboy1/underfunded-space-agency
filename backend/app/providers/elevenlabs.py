import logging
import math
import time
from collections.abc import AsyncGenerator, Sequence
from typing import Final, Protocol
from urllib.parse import quote

import httpx
from elevenlabs.client import AsyncElevenLabs
from elevenlabs.core.api_error import ApiError
from elevenlabs.core.request_options import RequestOptions

from app.config import Settings
from app.providers.base import ProviderError
from app.providers.probe import CachedProbe, Clock
from app.schemas import ServiceStatus, SttResponse

log = logging.getLogger("usa.backend")

TTS_OUTPUT_FORMAT: Final = "mp3_44100_64"

# The router's latency budget is the real limit; the client retries with the same request_id.
_HTTP_TIMEOUT_S: Final = 20.0
# Idle connections outlive the 30 s health probe, so the probes keep the TLS connection warm.
KEEPALIVE_EXPIRY_S: Final = 120.0
LOW_CONFIDENCE_WORD: Final = 0.6
_NO_RETRY: Final[RequestOptions] = {"max_retries": 0}
_RETRYABLE_CODES: Final = frozenset({408, 429, 500, 502, 503, 504})
_AUTH_CODES: Final = frozenset({401, 403})


class SttWord(Protocol):
    @property
    def type(self) -> object: ...

    @property
    def logprob(self) -> float | None: ...


def _retryable(code: int | None) -> bool:
    return code in _RETRYABLE_CODES


def _clamp_unit(value: float) -> float:
    return min(1.0, max(0.0, value))


def word_confidence(words: Sequence[SttWord], language_probability: float | None) -> float:
    """Mean per-word probability (`exp(logprob)`) over spoken words; spacing and audio events
    don't count. Falls back to the language-detection probability when no word has a logprob."""
    probabilities = [
        math.exp(word.logprob) for word in words if word.type == "word" and word.logprob is not None
    ]
    if probabilities:
        return _clamp_unit(sum(probabilities) / len(probabilities))
    return _clamp_unit(language_probability or 0.0)


def stt_response(result: object) -> SttResponse:
    """Maps a Scribe result (single or multichannel) to the STT response.

    `min_word_confidence` and `low_confidence_words` (below `LOW_CONFIDENCE_WORD`) let the client
    force a transcript review when one word, such as a thruster number, was misheard.
    """
    transcripts = getattr(result, "transcripts", None)
    chunk = transcripts[0] if transcripts else result
    text = getattr(chunk, "text", None)
    if not isinstance(text, str):
        raise ProviderError(retryable=False)
    words: Sequence[SttWord] = getattr(chunk, "words", None) or []
    confidence = word_confidence(words, getattr(chunk, "language_probability", None))
    scored = [
        (str(getattr(word, "text", "")).strip(), _clamp_unit(math.exp(word.logprob)))
        for word in words
        if word.type == "word" and word.logprob is not None
    ]
    return SttResponse(
        transcript=text.strip(),
        confidence=confidence,
        min_word_confidence=min((p for _, p in scored), default=confidence),
        low_confidence_words=[w for w, p in scored if p < LOW_CONFIDENCE_WORD],
    )


class ElevenLabsAccount:
    """One client and one cached health probe shared by speech-to-text and text-to-speech."""

    def __init__(self, settings: Settings, client: AsyncElevenLabs | None = None, clock: Clock = time.monotonic) -> None:
        self.client = client or AsyncElevenLabs(
            api_key=settings.elevenlabs_api_key.get_secret_value(),
            timeout=_HTTP_TIMEOUT_S,
            httpx_client=httpx.AsyncClient(
                timeout=_HTTP_TIMEOUT_S,
                follow_redirects=True,
                limits=httpx.Limits(keepalive_expiry=KEEPALIVE_EXPIRY_S),
            ),
        )
        self._probe = CachedProbe(self._check, clock=clock)

    async def health(self) -> ServiceStatus:
        return await self._probe.status()

    async def _check(self) -> ServiceStatus:
        """Subscription lookup (no characters spent); `degraded` once the character quota is used up."""
        try:
            subscription = await self.client.user.subscription.get(request_options=_NO_RETRY)
        except ApiError as exc:
            # A key scoped without `user_read` still authenticated; only a rejected key is misconfigured.
            if exc.status_code in _AUTH_CODES and _missing_permission(exc):
                return "ok"
            log.warning("elevenlabs probe failed code=%s", exc.status_code)
            if exc.status_code in _AUTH_CODES:
                return "unconfigured"
            return "degraded" if exc.status_code == 429 else "down"
        if subscription.character_limit > 0 and subscription.character_count >= subscription.character_limit:
            return "degraded"
        return "ok"


def _missing_permission(exc: ApiError) -> bool:
    body = exc.body if isinstance(exc.body, dict) else {}
    detail = body.get("detail")
    return isinstance(detail, dict) and detail.get("status") == "missing_permissions"


class ElevenLabsSpeechToText:
    """Scribe batch transcription of one push-to-talk utterance."""

    def __init__(self, account: ElevenLabsAccount, model: str) -> None:
        self._account = account
        self._model = model

    async def transcribe(self, audio_wav: bytes) -> SttResponse:
        try:
            result = await self._account.client.speech_to_text.convert(
                model_id=self._model,
                file=("utterance.wav", audio_wav, "audio/wav"),
                tag_audio_events=False,
                timestamps_granularity="word",
                request_options=_NO_RETRY,
            )
        except ApiError as exc:
            log.warning("elevenlabs stt failed code=%s", exc.status_code)
            raise ProviderError(retryable=_retryable(exc.status_code)) from exc
        except httpx.TimeoutException as exc:
            raise TimeoutError from exc
        except httpx.TransportError as exc:
            raise ProviderError(retryable=True) from exc
        except TimeoutError:
            raise
        except Exception as exc:
            # An unparsable body or another transport is a provider failure, never a 500.
            log.warning("elevenlabs stt failed type=%s", type(exc).__name__)
            raise ProviderError(retryable=True) from exc
        try:
            return stt_response(result)
        except ProviderError:
            raise
        except Exception as exc:
            log.warning("elevenlabs stt returned an unreadable result type=%s", type(exc).__name__)
            raise ProviderError(retryable=False) from exc

    async def health(self) -> ServiceStatus:
        return await self._account.health()


class ElevenLabsTextToSpeech:
    """Streams MP3 (`mp3_44100_64`) from the configured low-latency model."""

    def __init__(self, account: ElevenLabsAccount, model: str) -> None:
        self._account = account
        self._model = model

    @property
    def model_id(self) -> str:
        return self._model

    async def stream(self, voice_id: str, text: str) -> AsyncGenerator[bytes]:
        try:
            # The SDK puts the id into the URL path unescaped; quoting keeps it one segment.
            async for chunk in self._account.client.text_to_speech.stream(
                quote(voice_id, safe=""),
                text=text,
                model_id=self._model,
                output_format=TTS_OUTPUT_FORMAT,
                request_options=_NO_RETRY,
            ):
                if chunk:
                    yield chunk
        except ApiError as exc:
            log.warning("elevenlabs tts failed code=%s", exc.status_code)
            raise ProviderError(retryable=_retryable(exc.status_code)) from exc
        except httpx.TimeoutException as exc:
            raise TimeoutError from exc
        except httpx.TransportError as exc:
            raise ProviderError(retryable=True) from exc
        except TimeoutError:
            raise
        except Exception as exc:
            log.warning("elevenlabs tts failed type=%s", type(exc).__name__)
            raise ProviderError(retryable=True) from exc

    async def synthesize(self, voice_id: str, text: str) -> bytes:
        return b"".join([chunk async for chunk in self.stream(voice_id, text)])

    async def health(self) -> ServiceStatus:
        return await self._account.health()
