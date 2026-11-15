from fastapi import APIRouter, Request

from app.cache import fingerprint
from app.deps import CacheDep, ProvidersDep
from app.errors import ERROR_RESPONSES
from app.prompts import load_prompt
from app.routers._common import run_idempotent
from app.schemas import InterpretRequest, InterpretResponse

router = APIRouter(prefix="/v1", tags=["ai"], responses=ERROR_RESPONSES)


@router.post("/interpret", response_model=InterpretResponse)
async def interpret(
    body: InterpretRequest, request: Request, providers: ProvidersDep, cache: CacheDep
) -> InterpretResponse:
    return await run_idempotent(
        request,
        cache,
        "interpret",
        body.request_id,
        fingerprint(body.model_dump_json()),
        lambda: providers.language_model.interpret(body, load_prompt("interpret")),
    )
