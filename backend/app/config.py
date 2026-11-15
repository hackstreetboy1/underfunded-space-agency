from pathlib import Path
from typing import Final, Literal

from pydantic import Field, SecretStr, ValidationInfo, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

BACKEND_DIR: Final = Path(__file__).resolve().parents[1]
ENV_FILE: Final = BACKEND_DIR / ".env"
TTS_CACHE_DIR: Final = BACKEND_DIR / "cache" / "tts"
APOD_CACHE_DIR: Final = BACKEND_DIR / "cache" / "apod"
LOOPBACK_HOSTS: Final = frozenset({"127.0.0.1", "localhost", "::1"})

# Used when the matching `.env` entry is empty. Recorded in backend/README.md.
DEFAULT_GEMINI_MODEL: Final = "gemini-3.5-flash-lite"
# Tried in order when the primary is overloaded or slow; other generations draw on separate capacity.
DEFAULT_GEMINI_MODEL_FALLBACKS: Final = "gemini-3.1-flash-lite,gemini-2.5-flash-lite"
NO_FALLBACKS: Final = "none"
DEFAULT_ELEVENLABS_TTS_MODEL: Final = "eleven_flash_v2_5"
DEFAULT_ELEVENLABS_STT_MODEL: Final = "scribe_v3"

ThinkingLevel = Literal["minimal", "low", "medium", "high"]

# Models that accept thinking level "minimal"; every other model gets "low" unless configured.
_MINIMAL_THINKING_MODELS: Final = frozenset(
    {"gemini-3.6-flash", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-3-flash-preview"}
)
# Gemini 2.5 models take a thinking token budget instead of a level; they are sent a budget of 0.
_BUDGET_THINKING_PREFIX: Final = "gemini-2.5-"


class Settings(BaseSettings):
    """Proxy configuration read from `backend/.env`.

    Secrets are `SecretStr` so they are masked in repr and never serialised by accident.
    Read them only with `get_secret_value()` at the provider boundary.
    """

    model_config = SettingsConfigDict(
        env_file=ENV_FILE,
        env_file_encoding="utf-8",
        env_ignore_empty=True,
        extra="ignore",
        frozen=True,
    )

    gemini_api_key: SecretStr = SecretStr("")
    gemini_model: str = DEFAULT_GEMINI_MODEL
    # Comma list (`GEMINI_MODEL_FALLBACKS`); `none` turns the fallback chain off.
    gemini_model_fallbacks: str = DEFAULT_GEMINI_MODEL_FALLBACKS
    gemini_thinking_level: ThinkingLevel | None = None
    elevenlabs_api_key: SecretStr = SecretStr("")
    elevenlabs_tts_model: str = DEFAULT_ELEVENLABS_TTS_MODEL
    elevenlabs_stt_model: str = DEFAULT_ELEVENLABS_STT_MODEL
    # Astronomy Picture of the Day (`/v1/apod`): at most one call per UTC day, see `app/apod.py`.
    nasa_api_key: SecretStr = SecretStr("")
    proxy_host: str = "127.0.0.1"
    proxy_port: int = 8787
    use_fake_providers: bool = True
    # Test seam: every fake generation call waits this long first, so the 504 budget path is reachable.
    # Applied only with `use_fake_providers`; a real-provider deployment can never be slowed by it.
    fake_provider_delay_ms: int = Field(default=0, ge=0, le=60_000)
    tts_cache_dir: Path = TTS_CACHE_DIR
    apod_cache_dir: Path = APOD_CACHE_DIR

    @field_validator("proxy_host")
    @classmethod
    def _loopback_only(cls, value: str) -> str:
        if value not in LOOPBACK_HOSTS:
            raise ValueError("proxy_host must be a loopback address")
        return value

    @field_validator("gemini_model", "elevenlabs_tts_model", "elevenlabs_stt_model", mode="before")
    @classmethod
    def _blank_model_uses_default(cls, value: object, info: ValidationInfo) -> object:
        if not isinstance(value, str):
            return value
        return value.strip() or _MODEL_DEFAULTS[info.field_name or ""]

    @property
    def has_gemini_key(self) -> bool:
        return bool(self.gemini_api_key.get_secret_value().strip())

    @property
    def has_elevenlabs_key(self) -> bool:
        return bool(self.elevenlabs_api_key.get_secret_value().strip())

    @property
    def has_nasa_key(self) -> bool:
        return bool(self.nasa_api_key.get_secret_value().strip())

    @property
    def gemini_models(self) -> tuple[str, ...]:
        """The primary model, then each fallback in order, without duplicates."""
        names = [self.gemini_model]
        if self.gemini_model_fallbacks.strip().lower() != NO_FALLBACKS:
            names += [name.strip() for name in self.gemini_model_fallbacks.split(",")]
        return tuple(dict.fromkeys(name for name in names if name))

    @property
    def resolved_thinking_level(self) -> ThinkingLevel | None:
        return self.thinking_level_for(self.gemini_model)

    def thinking_level_for(self, model: str) -> ThinkingLevel | None:
        """None for a model that takes a thinking budget instead (sent as 0)."""
        if model.startswith(_BUDGET_THINKING_PREFIX):
            return None
        if self.gemini_thinking_level is not None:
            return self.gemini_thinking_level
        return "minimal" if model in _MINIMAL_THINKING_MODELS else "low"


_MODEL_DEFAULTS: Final[dict[str, str]] = {
    "gemini_model": DEFAULT_GEMINI_MODEL,
    "elevenlabs_tts_model": DEFAULT_ELEVENLABS_TTS_MODEL,
    "elevenlabs_stt_model": DEFAULT_ELEVENLABS_STT_MODEL,
}
