from pydantic import BaseModel, Field

from app.schemas.common import RequestId


class SttResponse(BaseModel):
    """`confidence` is the mean word probability; the client forces a transcript review when
    `min_word_confidence` < 0.6. `low_confidence_words` lists those words in spoken order."""

    transcript: str
    confidence: float = Field(ge=0.0, le=1.0)
    min_word_confidence: float = Field(ge=0.0, le=1.0)
    low_confidence_words: list[str] = []


class TtsRequest(BaseModel):
    """`cacheable` marks a fixed phrase (acknowledgement, greeting) for the on-disk phrase cache."""

    request_id: RequestId
    voice_id: str = Field(min_length=1, max_length=64)
    text: str = Field(min_length=1, max_length=1000)
    cacheable: bool = False
