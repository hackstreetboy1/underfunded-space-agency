import asyncio
from collections.abc import Awaitable, Callable
from typing import TypeVar

from fastapi import Request

from app.cache import IdempotencyCache
from app.deps import get_services
from app.limits import LATENCY_BUDGET_S, Endpoint
from app.request_log import bind_request_id

T = TypeVar("T")


def charge(request: Request, endpoint: Endpoint) -> None:
    """Takes one rate-limit token; raises 429 `rate_limited` when the bucket is empty.

    Call it only right before a provider call, so validation failures and cached replays cost nothing.
    """
    get_services(request).limiter.check(endpoint)


async def run_idempotent(
    request: Request,
    cache: IdempotencyCache,
    endpoint: Endpoint,
    request_id: str,
    payload_fingerprint: str,
    call: Callable[[], Awaitable[T]],
) -> T:
    """Runs `call` once per `request_id` within the endpoint's latency budget.

    The rate limit is charged on a cache miss only; a replay of a stored response is free.
    """
    bind_request_id(request, request_id)

    async def bounded() -> T:
        charge(request, endpoint)
        async with asyncio.timeout(LATENCY_BUDGET_S[endpoint]):
            return await call()

    return await cache.get_or_compute(endpoint, request_id, payload_fingerprint, bounded)
