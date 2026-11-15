from typing import Annotated, Final

from fastapi import APIRouter, Query, Response

from app.deps import ApodDep
from app.errors import ApiError
from app.schemas import ApodResponse
from app.schemas.apod import APOD_DATE_PATTERN
from app.schemas.common import ErrorResponse

IMAGE_PATH: Final = "/v1/apod/image"
# The day's picture changes at most once a day; an hour keeps a long session from re-reading the file.
_IMAGE_CACHE_CONTROL: Final = "private, max-age=3600"

router = APIRouter(prefix="/v1", tags=["apod"], responses={404: {"model": ErrorResponse}})


def _unavailable() -> ApiError:
    return ApiError(404, "apod_unavailable", retryable=False)


@router.get("/apod", response_model=ApodResponse)
async def apod(service: ApodDep) -> ApodResponse:
    """Today's Astronomy Picture of the Day, else the newest cached image day; 404 `apod_unavailable` when none."""
    current = await service.current()
    if current is None:
        raise _unavailable()
    entry, is_fallback = current
    return ApodResponse(
        date=entry.date,
        title=entry.title,
        media_type=entry.media_type,
        url=entry.url,
        image_path=f"{IMAGE_PATH}?date={entry.date}",
        explanation=entry.explanation,
        copyright=entry.copyright,
        is_fallback=is_fallback,
    )


@router.get(IMAGE_PATH.removeprefix("/v1"), response_class=Response)
async def apod_image(
    service: ApodDep, date: Annotated[str | None, Query(pattern=APOD_DATE_PATTERN)] = None
) -> Response:
    """The cached image bytes (JPEG or PNG) of `date`, or of the newest cached day. Never contacts NASA."""
    image = await service.image(date)
    if image is None:
        raise _unavailable()
    data, content_type = image
    return Response(content=data, media_type=content_type, headers={"Cache-Control": _IMAGE_CACHE_CONTROL})
