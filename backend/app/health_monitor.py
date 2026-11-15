import asyncio
import time
from collections.abc import Awaitable, Callable
from typing import Final

from app.providers.base import LanguageModel, SpeechToText, TextToSpeech
from app.providers.fakes import FakeLanguageModel, FakeSpeechToText, FakeTextToSpeech
from app.schemas import HealthResponse, ProviderMode, ServiceStatus

HEALTH_TTL_S: Final = 30.0
# Real probes are network round trips; providers cache them for 30 s and startup warms them.
PROBE_TIMEOUT_S: Final = 2.0

_SEVERITY: Final[dict[ServiceStatus, int]] = {"ok": 0, "degraded": 1, "unconfigured": 2, "down": 3}

Clock = Callable[[], float]


async def _probe(check: Callable[[], Awaitable[ServiceStatus]]) -> ServiceStatus:
    try:
        async with asyncio.timeout(PROBE_TIMEOUT_S):
            return await check()
    except Exception:
        return "down"


def _worst(*statuses: ServiceStatus) -> ServiceStatus:
    return max(statuses, key=_SEVERITY.__getitem__)


def is_fake_provider(provider: object) -> bool:
    return isinstance(provider, (FakeLanguageModel, FakeSpeechToText, FakeTextToSpeech))


def provider_mode(*providers: object) -> ProviderMode:
    """`fake` only when every provider is a stand-in.

    A fake standing in for a missing key still reports `unconfigured` in its own status.
    """
    return "fake" if all(is_fake_provider(provider) for provider in providers) else "live"


class HealthMonitor:
    """Probes providers at most once per `ttl_s`; concurrent callers share one probe."""

    def __init__(
        self,
        language_model: LanguageModel,
        speech_to_text: SpeechToText,
        text_to_speech: TextToSpeech,
        ttl_s: float = HEALTH_TTL_S,
        clock: Clock = time.monotonic,
    ) -> None:
        self._language_model = language_model
        self._speech_to_text = speech_to_text
        self._text_to_speech = text_to_speech
        self._ttl_s = ttl_s
        self._clock = clock
        self._mode = provider_mode(language_model, speech_to_text, text_to_speech)
        self._lock = asyncio.Lock()
        self._cached: HealthResponse | None = None
        self._expires_at = 0.0

    async def current(self) -> HealthResponse:
        async with self._lock:
            if self._cached is not None and self._clock() < self._expires_at:
                return self._cached
            gemini, stt, tts = await asyncio.gather(
                _probe(self._language_model.health),
                _probe(self._speech_to_text.health),
                _probe(self._text_to_speech.health),
            )
            self._cached = HealthResponse(
                backend="ok", gemini=gemini, elevenlabs=_worst(stt, tts), mode=self._mode
            )
            self._expires_at = self._clock() + self._ttl_s
            return self._cached
