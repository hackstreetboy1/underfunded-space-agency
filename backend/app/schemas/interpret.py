from typing import Literal

from pydantic import BaseModel, Field

from app.schemas.common import RequestId
from app.schemas.narrate import CrewPersona, StressLevel


class ActionParam(BaseModel):
    name: str
    type: Literal["string", "number", "integer", "boolean", "enum"]
    enum: list[str] = []


class ActionSpec(BaseModel):
    id: str
    description: str
    params: list[ActionParam] = []
    impact: Literal["low", "high", "irreversible"]


class ParamValue(BaseModel):
    name: str
    value: str


class PendingOffer(BaseModel):
    """A crew offer still open on a recent line: what "yes" or "do it" would accept (already validated by the game)."""

    speaker: str = Field(min_length=1, max_length=64)
    label: str = Field(max_length=160)
    action_id: str = Field(min_length=1, max_length=64)
    params: list[ParamValue] = []


class PendingClarification(BaseModel):
    """The crew's open clarifying question, so a reply like "the second one" can be resolved."""

    speaker: str = Field(min_length=1, max_length=64)
    question: str = Field(max_length=300)
    options: list[str] = Field(default_factory=list, max_length=4)


class InterpretPersona(CrewPersona):
    """How one crew member talks, so their reply sounds like them (GDD 5.7), and how stressed they are now."""

    id: str = Field(min_length=1, max_length=64)
    stress: StressLevel = "calm"


class InterpretRequest(BaseModel):
    request_id: RequestId
    player_text: str = Field(max_length=600)
    addressee: str | None
    mission_phase: str
    crew: list[dict[str, str]]
    actions: list[ActionSpec]
    facts: list[dict[str, str]]
    recent_transcript: list[str] = Field(default_factory=list, max_length=8)
    pending_offers: list[PendingOffer] = Field(default_factory=list, max_length=4)
    pending_clarification: PendingClarification | None = None
    # Additive (older clients omit them): each crew member's voice, and the gauges the crew can read right now (id and
    # label, never a value). The game reads every gauge a reply's `<FACT:id>` tokens name, then fills in the values.
    personas: list[InterpretPersona] = Field(default_factory=list, max_length=4)
    readable_facts: list[dict[str, str]] = Field(default_factory=list, max_length=96)


class InterpretResponse(BaseModel):
    kind: Literal["command", "question", "clarify", "recommendation_request", "chat", "unknown"]
    crew_member: str | None = None
    action_id: str | None = None
    params: list[ParamValue] = []
    fact_refs: list[str] = []
    dialogue_template: str = ""
    clarification_question: str | None = None
    clarification_options: list[str] = []
    requires_confirmation: bool = False
