from typing import Final

from pydantic import BaseModel, Field

APOD_DATE_PATTERN: Final = r"^\d{4}-\d{2}-\d{2}$"


class ApodResponse(BaseModel):
    """`GET /v1/apod`: the newest cached Astronomy Picture of the Day that is an image.

    `url` is NASA's standard-resolution image URL, for reference only: the game loads the cached copy from
    `image_path` on this proxy and never contacts NASA. `copyright` is the holder's name when the image is not
    public domain. `is_fallback` is true when today's picture could not be used (video day, error) and an
    earlier cached day is served instead.
    """

    date: str = Field(pattern=APOD_DATE_PATTERN)
    title: str
    media_type: str
    url: str
    image_path: str
    explanation: str = ""
    copyright: str | None = None
    is_fallback: bool = False
