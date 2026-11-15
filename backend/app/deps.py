import logging
from dataclasses import dataclass
from typing import Annotated, cast

from fastapi import Depends, Request

from app.apod import ApodService, DiskApodStore, FakeApodSource, MemoryApodStore, NasaApodSource
from app.cache import IdempotencyCache, TtsPhraseCache
from app.config import Settings
from app.health_monitor import HealthMonitor
from app.limits import RateLimiter
from app.providers.base import LanguageModel, SpeechToText, TextToSpeech
from app.providers.elevenlabs import ElevenLabsAccount, ElevenLabsSpeechToText, ElevenLabsTextToSpeech
from app.providers.fakes import FakeLanguageModel, FakeSpeechToText, FakeTextToSpeech
from app.providers.gemini import GeminiLanguageModel

log = logging.getLogger("usa.backend")


@dataclass(frozen=True, slots=True)
class Providers:
    language_model: LanguageModel
    speech_to_text: SpeechToText
    text_to_speech: TextToSpeech


@dataclass(frozen=True, slots=True)
class Services:
    settings: Settings
    providers: Providers
    cache: IdempotencyCache
    phrases: TtsPhraseCache
    limiter: RateLimiter
    health: HealthMonitor
    apod: ApodService


def build_providers(settings: Settings) -> Providers:
    """Fakes when `use_fake_providers` is set or a key is missing.

    A fake that stands in for a missing key reports `unconfigured` on /health, so the
    client still sees the misconfiguration instead of a healthy service.
    """
    if settings.use_fake_providers:
        delay_s = settings.fake_provider_delay_ms / 1000.0
        if delay_s > 0:
            log.warning("fake providers delay every generation call by %d ms", settings.fake_provider_delay_ms)
        return Providers(
            FakeLanguageModel(delay_s=delay_s), FakeSpeechToText(delay_s=delay_s), FakeTextToSpeech(delay_s=delay_s)
        )

    language_model: LanguageModel
    if settings.has_gemini_key:
        language_model = GeminiLanguageModel(settings)
    else:
        log.warning("gemini key missing; using fake language model")
        language_model = FakeLanguageModel(status="unconfigured")

    speech_to_text: SpeechToText
    text_to_speech: TextToSpeech
    if settings.has_elevenlabs_key:
        account = ElevenLabsAccount(settings)
        speech_to_text = ElevenLabsSpeechToText(account, settings.elevenlabs_stt_model)
        text_to_speech = ElevenLabsTextToSpeech(account, settings.elevenlabs_tts_model)
    else:
        log.warning("elevenlabs key missing; using fake speech providers")
        speech_to_text = FakeSpeechToText(status="unconfigured")
        text_to_speech = FakeTextToSpeech(status="unconfigured")

    return Providers(language_model, speech_to_text, text_to_speech)


def build_apod(settings: Settings) -> ApodService:
    """The offline stand-in (kept in memory, never in `backend/cache/apod`) for fake providers or a missing key."""
    if settings.use_fake_providers:
        return ApodService(FakeApodSource(), MemoryApodStore())
    if not settings.has_nasa_key:
        log.warning("nasa key missing; loading screens use the offline stand-in picture")
        return ApodService(FakeApodSource(), MemoryApodStore())
    return ApodService(NasaApodSource(settings.nasa_api_key.get_secret_value()), DiskApodStore(settings.apod_cache_dir))


def get_services(request: Request) -> Services:
    return cast(Services, request.app.state.services)


def get_providers(request: Request) -> Providers:
    return get_services(request).providers


def get_cache(request: Request) -> IdempotencyCache:
    return get_services(request).cache


def get_health_monitor(request: Request) -> HealthMonitor:
    return get_services(request).health


def get_phrase_cache(request: Request) -> TtsPhraseCache:
    return get_services(request).phrases


def get_apod(request: Request) -> ApodService:
    return get_services(request).apod


ProvidersDep = Annotated[Providers, Depends(get_providers)]
CacheDep = Annotated[IdempotencyCache, Depends(get_cache)]
HealthDep = Annotated[HealthMonitor, Depends(get_health_monitor)]
PhraseCacheDep = Annotated[TtsPhraseCache, Depends(get_phrase_cache)]
ApodDep = Annotated[ApodService, Depends(get_apod)]
