from fastapi import APIRouter, Request

from app.cache import fingerprint
from app.deps import CacheDep, ProvidersDep
from app.errors import ERROR_RESPONSES
from app.prompts import load_prompt
from app.routers._common import run_idempotent
from app.schemas import ExplainRequest, ExplainResponse

router = APIRouter(prefix="/v1", tags=["ai"], responses=ERROR_RESPONSES)


@router.post("/explain", response_model=ExplainResponse)
async def explain(
    body: ExplainRequest, request: Request, providers: ProvidersDep, cache: CacheDep
) -> ExplainResponse:
    return await run_idempotent(
        request,
        cache,
        "explain",
        body.request_id,
        fingerprint(body.model_dump_json()),
        lambda: providers.language_model.explain(body, load_prompt("explain")),
    )
