import struct
from typing import Annotated, Final

from fastapi import APIRouter, File, Form, Request, UploadFile

from app.cache import fingerprint
from app.deps import CacheDep, ProvidersDep
from app.errors import ERROR_RESPONSES, ApiError
from app.routers._common import run_idempotent
from app.schemas import RequestId, SttResponse

# The client records at most 15 s of 16 kHz mono PCM16 (about 480 KB); both caps leave headroom.
MAX_AUDIO_BYTES: Final = 4 * 1024 * 1024
MAX_AUDIO_SECONDS: Final = 30.0

router = APIRouter(prefix="/v1", tags=["speech"], responses=ERROR_RESPONSES)


def wav_duration_s(data: bytes) -> float | None:
    """Seconds of audio in a RIFF/WAVE clip, or None when it isn't a readable WAV.

    Walks the chunks for `fmt ` (byte rate) and `data`; a data size larger than the bytes present
    (streaming writers put 0xFFFFFFFF there) counts only the bytes actually sent.
    """
    if len(data) < 12 or data[0:4] != b"RIFF" or data[8:12] != b"WAVE":
        return None
    byte_rate = 0
    offset = 12
    while offset + 8 <= len(data):
        chunk_id = data[offset : offset + 4]
        (size,) = struct.unpack_from("<I", data, offset + 4)
        body = offset + 8
        if chunk_id == b"fmt " and size >= 16 and body + 16 <= len(data):
            (byte_rate,) = struct.unpack_from("<I", data, body + 8)
        elif chunk_id == b"data":
            if byte_rate <= 0:
                return None
            return min(size, len(data) - body) / byte_rate
        offset = body + size + (size & 1)
    return None


@router.post("/stt", response_model=SttResponse)
async def stt(
    request: Request,
    providers: ProvidersDep,
    cache: CacheDep,
    audio: Annotated[UploadFile, File(description="WAV recording")],
    request_id: Annotated[RequestId, Form()],
) -> SttResponse:
    data = await audio.read(MAX_AUDIO_BYTES + 1)
    if len(data) > MAX_AUDIO_BYTES:
        raise ApiError(413, "payload_too_large", retryable=False)
    duration_s = wav_duration_s(data)
    if duration_s is None:
        raise ApiError(415, "unsupported_media_type", retryable=False)
    if duration_s > MAX_AUDIO_SECONDS:
        raise ApiError(413, "payload_too_large", retryable=False)
    return await run_idempotent(
        request,
        cache,
        "stt",
        request_id,
        fingerprint(data),
        lambda: providers.speech_to_text.transcribe(data),
    )
