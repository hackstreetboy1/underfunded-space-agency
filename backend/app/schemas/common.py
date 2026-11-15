from typing import Annotated, Final, Literal

from pydantic import BaseModel, StringConstraints

ServiceStatus = Literal["ok", "degraded", "down", "unconfigured"]
# "fake": every provider is a local stand-in (canned replies, test audio);
# "live": at least one real provider is wired.
ProviderMode = Literal["fake", "live"]

REQUEST_ID_MAX_LENGTH: Final = 128
# Same charset the access log keeps, so a client id is logged verbatim and never needs escaping.
REQUEST_ID_PATTERN: Final = r"^[A-Za-z0-9_.:-]+$"

RequestId = Annotated[
    str, StringConstraints(min_length=1, max_length=REQUEST_ID_MAX_LENGTH, pattern=REQUEST_ID_PATTERN)
]


class ErrorResponse(BaseModel):
    error_code: str
    retryable: bool
