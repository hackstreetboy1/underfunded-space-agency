import logging
import time
from collections.abc import AsyncIterator, Callable, Mapping
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI
from pydantic import ValidationError

from app.apod import ApodService
from app.cache import IdempotencyCache, TtsPhraseCache
from app.config import ENV_FILE, Settings
from app.deps import Providers, Services, build_apod, build_providers
from app.errors import install_error_handlers
from app.health_monitor import HealthMonitor, is_fake_provider
from app.limits import DEFAULT_LIMITS, BucketSpec, Endpoint, RateLimiter
from app.request_guard import MULTIPART_OVERHEAD_BYTES, RequestGuardMiddleware
from app.request_log import RequestLogMiddleware, configure_logging
from app.routers import ROUTERS
from app.routers.stt import MAX_AUDIO_BYTES

log = logging.getLogger("usa.backend")


def _provider_kind(provider: object) -> str:
    return "fake" if is_fake_provider(provider) else "real"


@asynccontextmanager
async def _lifespan(app: FastAPI) -> AsyncIterator[None]:
    services: Services = app.state.services
    providers = services.providers
    log.info(
        "backend starting language_model=%s speech_to_text=%s text_to_speech=%s",
        _provider_kind(providers.language_model),
        _provider_kind(providers.speech_to_text),
        _provider_kind(providers.text_to_speech),
    )
    # Connection warm-up: the health probes (metadata lookups, never generation) open the provider
    # TLS connections before the first real request and fill the /health cache.
    await services.health.current()
    yield
    services.cache.clear()
    log.info("backend stopped")


def load_settings(env_file: Path = ENV_FILE) -> Settings:
    """Reads `backend/.env`. A missing file is not an error (fakes are the default); an invalid
    value stops startup with the offending setting names only, never their values."""
    if not env_file.is_file():
        log.warning("backend/.env not found: using defaults (fake providers unless USE_FAKE_PROVIDERS=false)")
    try:
        return Settings(_env_file=env_file)
    except ValidationError as exc:
        names = sorted({str(error["loc"][0]).upper() for error in exc.errors() if error["loc"]})
        message = f"invalid backend settings: {', '.join(names) or 'unknown'} (see backend/.env.example)"
        log.error(message)
        raise SystemExit(message) from None


def create_app(
    settings: Settings | None = None,
    providers: Providers | None = None,
    limits: Mapping[Endpoint, BucketSpec] = DEFAULT_LIMITS,
    clock: Callable[[], float] = time.monotonic,
    apod: ApodService | None = None,
) -> FastAPI:
    """Builds the proxy. With no arguments it reads `backend/.env` (uvicorn `--factory` entry)."""
    configure_logging()
    resolved_settings = settings if settings is not None else load_settings()
    configure_logging(
        secrets=(
            resolved_settings.gemini_api_key.get_secret_value(),
            resolved_settings.elevenlabs_api_key.get_secret_value(),
            resolved_settings.nasa_api_key.get_secret_value(),
        )
    )
    resolved_providers = providers if providers is not None else build_providers(resolved_settings)

    app = FastAPI(title="Underfunded Space Agency proxy", version="0.1.0", lifespan=_lifespan)
    app.state.services = Services(
        settings=resolved_settings,
        providers=resolved_providers,
        cache=IdempotencyCache(clock=clock),
        phrases=TtsPhraseCache(resolved_settings.tts_cache_dir),
        limiter=RateLimiter(limits, clock=clock),
        health=HealthMonitor(
            resolved_providers.language_model,
            resolved_providers.speech_to_text,
            resolved_providers.text_to_speech,
            clock=clock,
        ),
        apod=apod if apod is not None else build_apod(resolved_settings),
    )
    install_error_handlers(app)
    # Added last runs first: the access log sees every guard rejection.
    app.add_middleware(
        RequestGuardMiddleware, body_limits={"/v1/stt": MAX_AUDIO_BYTES + MULTIPART_OVERHEAD_BYTES}
    )
    app.add_middleware(RequestLogMiddleware)
    for router in ROUTERS:
        app.include_router(router)
    return app
