from pydantic import BaseModel, Field

from app.schemas.common import RequestId


class EncyclopediaExcerpt(BaseModel):
    id: str = Field(min_length=1, max_length=64)
    title: str = Field(max_length=120)
    text: str = Field(max_length=2000)


class ExplainRequest(BaseModel):
    request_id: RequestId
    question: str = Field(min_length=1, max_length=600)
    excerpts: list[EncyclopediaExcerpt] = Field(default_factory=list, max_length=6)


class ExplainResponse(BaseModel):
    """`sources` lists excerpt ids the answer drew on. Answers explain concepts, never solutions."""

    answer: str
    sources: list[str] = []
