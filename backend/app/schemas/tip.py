from typing import Literal

from pydantic import BaseModel, Field

from app.schemas.common import RequestId

TipStage = Literal["nudge", "investigate", "connect", "recommend", "direct"]


class HintCandidate(BaseModel):
    """Authored hint text from the mission's hint tree; the fallback if rephrasing fails."""

    tip_id: str = Field(min_length=1, max_length=64)
    stage: TipStage
    text: str = Field(min_length=1, max_length=400)
    fact_refs: list[str] = []
    allowed_action_refs: list[str] = []


class KnownFact(BaseModel):
    id: str = Field(min_length=1, max_length=64)
    label: str = Field(max_length=120)


class TipRequest(BaseModel):
    request_id: RequestId
    stage: TipStage
    candidates: list[HintCandidate] = Field(min_length=1, max_length=8)
    known_facts: list[KnownFact] = Field(default_factory=list, max_length=32)


class TipResponse(BaseModel):
    tip_id: str
    stage: TipStage
    text: str
    fact_refs: list[str] = []
    allowed_action_refs: list[str] = []
