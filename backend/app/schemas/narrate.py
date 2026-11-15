from typing import Literal

from pydantic import BaseModel, Field

from app.schemas.common import RequestId

StressLevel = Literal["calm", "tense", "critical"]


class CrewPersona(BaseModel):
    """How a crew member talks (GDD 5.7). Sent by Godot from `data/crew/crew.json`."""

    name: str = Field(min_length=1, max_length=60)
    role: str = Field(default="", max_length=60)
    personality: str = Field(default="", max_length=300)
    cadence: str = Field(default="", max_length=200)
    warmth: str = Field(default="", max_length=200)
    humor: str = Field(default="", max_length=200)
    stress_expression: str = Field(default="", max_length=200)
    sample_lines: list[str] = Field(default_factory=list, max_length=5)


class NarrateFact(BaseModel):
    """An approved fact: id and label plus a qualitative band, never a numeric value."""

    id: str = Field(min_length=1, max_length=64)
    label: str = Field(max_length=120)
    band: str | None = Field(default=None, max_length=40)


class NarrateRequest(BaseModel):
    request_id: RequestId
    crew_persona_id: str = Field(min_length=1, max_length=64)
    persona: CrewPersona | None = None
    stress: StressLevel = "calm"
    event: str = Field(min_length=1, max_length=200)
    facts: list[NarrateFact] = Field(default_factory=list, max_length=16)


class NarrateResponse(BaseModel):
    """`dialogue_template` carries `<FACT:id>` tokens that Godot resolves."""

    dialogue_template: str
    fact_refs: list[str] = []
