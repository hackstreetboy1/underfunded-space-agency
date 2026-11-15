from fastapi import APIRouter, Request

from app.cache import fingerprint
from app.deps import CacheDep, ProvidersDep
from app.errors import ERROR_RESPONSES
from app.prompts import load_prompt
from app.routers._common import run_idempotent
from app.schemas import NarrateRequest, NarrateResponse

router = APIRouter(prefix="/v1", tags=["ai"], responses=ERROR_RESPONSES)


@router.post("/narrate", response_model=NarrateResponse)
async def narrate(
    body: NarrateRequest, request: Request, providers: ProvidersDep, cache: CacheDep
) -> NarrateResponse:
    return await run_idempotent(
        request,
        cache,
        "narrate",
        body.request_id,
        fingerprint(body.model_dump_json()),
        lambda: providers.language_model.narrate(body, load_prompt("narrate")),
    )
