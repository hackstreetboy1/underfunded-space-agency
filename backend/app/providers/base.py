from collections.abc import AsyncGenerator
from typing import Protocol, runtime_checkable

from app.schemas import (
    ExplainRequest,
    ExplainResponse,
    InterpretRequest,
    InterpretResponse,
    NarrateRequest,
    NarrateResponse,
    ServiceStatus,
    SttResponse,
    TipRequest,
    TipResponse,
)


class ProviderError(Exception):
    """A provider call failed. Details stay server-side; clients only see `retryable`."""

    def __init__(self, *, retryable: bool) -> None:
        super().__init__("provider call failed")
        self.retryable = retryable


@runtime_checkable
class LanguageModel(Protocol):
    """Classifies and phrases only. Every result is validated by Godot before it can act."""

    async def interpret(self, request: InterpretRequest, system_prompt: str) -> InterpretResponse: ...

    async def narrate(self, request: NarrateRequest, system_prompt: str) -> NarrateResponse: ...

    async def tip(self, request: TipRequest, system_prompt: str) -> TipResponse: ...

    async def explain(self, request: ExplainRequest, system_prompt: str) -> ExplainResponse: ...

    async def health(self) -> ServiceStatus: ...


@runtime_checkable
class SpeechToText(Protocol):
    async def transcribe(self, audio_wav: bytes) -> SttResponse: ...

    async def health(self) -> ServiceStatus: ...


@runtime_checkable
class TextToSpeech(Protocol):
    @property
    def model_id(self) -> str:
        """Part of the phrase-cache key, so a model change never replays old audio."""
        ...

    def stream(self, voice_id: str, text: str) -> AsyncGenerator[bytes]:
        """Yields MPEG audio (`audio/mpeg`) chunks as the provider produces them."""
        ...

    async def synthesize(self, voice_id: str, text: str) -> bytes:
        """The whole MPEG clip."""
        ...

    async def health(self) -> ServiceStatus: ...
