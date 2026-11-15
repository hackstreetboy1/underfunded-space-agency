from fastapi import APIRouter, Request

from app.cache import fingerprint
from app.deps import CacheDep, ProvidersDep
from app.errors import ERROR_RESPONSES
from app.prompts import load_prompt
from app.routers._common import run_idempotent
from app.schemas import TipRequest, TipResponse

router = APIRouter(prefix="/v1", tags=["ai"], responses=ERROR_RESPONSES)


@router.post("/tip", response_model=TipResponse)
async def tip(body: TipRequest, request: Request, providers: ProvidersDep, cache: CacheDep) -> TipResponse:
    return await run_idempotent(
        request,
        cache,
        "tip",
        body.request_id,
        fingerprint(body.model_dump_json()),
        lambda: providers.language_model.tip(body, load_prompt("tip")),
    )
