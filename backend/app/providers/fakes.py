import asyncio
import hashlib
import re
from collections import Counter
from collections.abc import AsyncGenerator
from typing import Final

from app.schemas import (
    ActionSpec,
    ExplainRequest,
    ExplainResponse,
    InterpretRequest,
    InterpretResponse,
    NarrateRequest,
    NarrateResponse,
    ParamValue,
    ServiceStatus,
    SttResponse,
    TipRequest,
    TipResponse,
)

# MPEG-1 Layer III, 128 kbit/s, 44.1 kHz, mono, zeroed side info: one 417-byte frame of silence.
_SILENT_FRAME: Final = bytes([0xFF, 0xFB, 0x90, 0xC0]) + bytes(413)
_MAX_FRAMES: Final = 400


async def _simulate_latency(delay_s: float) -> None:
    if delay_s > 0:
        await asyncio.sleep(delay_s)


# "Cal, should we cycle ...?", "Do you think we should jettison?": asking for the crew's view, never an order (interpret.md).
_RECOMMENDATION_LEAD: Final = re.compile(
    r"(?:^|[,:;]\s*)(?:should|shall) (?:we|i)\b"
    r"|\bdo you think we should\b|\bwhat do you think about\b|\bwould you recommend\b"
)


def _is_recommendation_request(text: str) -> bool:
    return text.endswith("?") and _RECOMMENDATION_LEAD.search(text) is not None


def _mentions(text: str, phrase: str) -> bool:
    return re.search(rf"(?<!\w){re.escape(phrase.lower())}(?!\w)", text) is not None


def _named_action(request: InterpretRequest, text: str) -> ActionSpec | None:
    for action in request.actions:
        if _mentions(text, action.id) or _mentions(text, action.id.replace("_", " ")):
            return action
    return None


def _recommendation(request: InterpretRequest, text: str, crew_member: str | None) -> InterpretResponse:
    """A `recommendation_request` the client can turn into a "go ahead" offer: the named action, and each enum
    parameter whose value the text names unambiguously."""
    action = _named_action(request, text)
    params: list[ParamValue] = []
    if action is not None:
        for param in action.params:
            named = [value for value in param.enum if _mentions(text, value)]
            if len(named) == 1:
                params.append(ParamValue(name=param.name, value=named[0]))
    fact_refs = [fact["id"] for fact in request.facts if "id" in fact and fact.get("label", "").lower() in text]
    view = " ".join(f"<FACT:{fact_id}>" for fact_id in fact_refs)
    closing = "Want us to go ahead?" if action is not None else "It's your call."
    return InterpretResponse(
        kind="recommendation_request",
        crew_member=crew_member,
        action_id=action.id if action is not None else None,
        params=params,
        fact_refs=fact_refs,
        dialogue_template=f"{view} {closing}".strip(),
    )


class FakeLanguageModel:
    """Deterministic stand-in for Gemini. `calls` counts invocations per method.

    `delay_s` delays every generation call (not `health`), to exercise the proxy's latency budgets.
    """

    def __init__(self, status: ServiceStatus = "ok", delay_s: float = 0.0) -> None:
        self._status: ServiceStatus = status
        self._delay_s = delay_s
        self.calls: Counter[str] = Counter()

    async def interpret(self, request: InterpretRequest, system_prompt: str) -> InterpretResponse:
        self.calls["interpret"] += 1
        await _simulate_latency(self._delay_s)
        text = request.player_text.strip().lower()
        crew_ids = [member["id"] for member in request.crew if "id" in member]
        crew_member = request.addressee if request.addressee in crew_ids else next(iter(crew_ids), None)
        if not text:
            return InterpretResponse(kind="unknown", dialogue_template="Say again?")
        if _is_recommendation_request(text):
            return _recommendation(request, text, crew_member)
        for action in request.actions:
            if action.id.lower() in text or action.id.replace("_", " ").lower() in text:
                return InterpretResponse(
                    kind="command",
                    crew_member=crew_member,
                    action_id=action.id,
                    dialogue_template=f"Copy, {action.description}.",
                    requires_confirmation=action.impact != "low",
                )
        if text.endswith("?"):
            fact_refs = [
                fact["id"]
                for fact in request.facts
                if "id" in fact and fact.get("label", "").lower() in text
            ]
            template = " ".join(f"<FACT:{fact_id}>" for fact_id in fact_refs) or "I can't verify that."
            return InterpretResponse(
                kind="question", crew_member=crew_member, fact_refs=fact_refs, dialogue_template=template
            )
        return InterpretResponse(
            kind="clarify",
            crew_member=crew_member,
            clarification_question="Say again, which action?",
            clarification_options=[action.id for action in request.actions[:3]],
        )

    async def narrate(self, request: NarrateRequest, system_prompt: str) -> NarrateResponse:
        self.calls["narrate"] += 1
        await _simulate_latency(self._delay_s)
        fact_refs = [fact.id for fact in request.facts]
        tokens = " ".join(f"<FACT:{fact_id}>" for fact_id in fact_refs)
        return NarrateResponse(dialogue_template=f"{request.event}. {tokens}".strip(), fact_refs=fact_refs)

    async def tip(self, request: TipRequest, system_prompt: str) -> TipResponse:
        self.calls["tip"] += 1
        await _simulate_latency(self._delay_s)
        chosen = next(
            (candidate for candidate in request.candidates if candidate.stage == request.stage),
            request.candidates[0],
        )
        return TipResponse(
            tip_id=chosen.tip_id,
            stage=request.stage,
            text=chosen.text,
            fact_refs=chosen.fact_refs,
            allowed_action_refs=chosen.allowed_action_refs,
        )

    async def explain(self, request: ExplainRequest, system_prompt: str) -> ExplainResponse:
        self.calls["explain"] += 1
        await _simulate_latency(self._delay_s)
        if not request.excerpts:
            return ExplainResponse(answer="I don't have a reference for that.", sources=[])
        first = request.excerpts[0]
        return ExplainResponse(answer=f"{first.title}: {first.text[:280]}", sources=[first.id])

    async def health(self) -> ServiceStatus:
        return self._status


class FakeSpeechToText:
    def __init__(self, status: ServiceStatus = "ok", delay_s: float = 0.0) -> None:
        self._status: ServiceStatus = status
        self._delay_s = delay_s
        self.calls: Counter[str] = Counter()

    async def transcribe(self, audio_wav: bytes) -> SttResponse:
        self.calls["transcribe"] += 1
        await _simulate_latency(self._delay_s)
        digest = hashlib.sha256(audio_wav).hexdigest()[:8]
        return SttResponse(transcript=f"fake transcript {digest}", confidence=0.9, min_word_confidence=0.9)

    async def health(self) -> ServiceStatus:
        return self._status


class FakeTextToSpeech:
    """Silent MP3 sized by text length, streamed one frame per chunk. `delay_s` holds back the first chunk."""

    def __init__(self, status: ServiceStatus = "ok", model_id: str = "fake", delay_s: float = 0.0) -> None:
        self._status: ServiceStatus = status
        self._model_id = model_id
        self._delay_s = delay_s
        self.calls: Counter[str] = Counter()

    @property
    def model_id(self) -> str:
        return self._model_id

    async def stream(self, voice_id: str, text: str) -> AsyncGenerator[bytes]:
        self.calls["synthesize"] += 1
        await _simulate_latency(self._delay_s)
        for _ in range(min(_MAX_FRAMES, 4 + len(text) // 4)):
            yield _SILENT_FRAME

    async def synthesize(self, voice_id: str, text: str) -> bytes:
        return b"".join([chunk async for chunk in self.stream(voice_id, text)])

    async def health(self) -> ServiceStatus:
        return self._status
