import asyncio
import time
from collections.abc import Awaitable, Callable
from typing import Final

from app.schemas import ServiceStatus

PROBE_TTL_S: Final = 30.0

Clock = Callable[[], float]


class CachedProbe:
    """Runs a cheap status check at most once per `ttl_s`; concurrent callers share one call.

    A failing check reports `down` and is cached like any other result, so a dead service is
    not hammered by the 5 s client heartbeat.
    """

    def __init__(
        self,
        check: Callable[[], Awaitable[ServiceStatus]],
        ttl_s: float = PROBE_TTL_S,
        clock: Clock = time.monotonic,
    ) -> None:
        self._check = check
        self._ttl_s = ttl_s
        self._clock = clock
        self._lock = asyncio.Lock()
        self._status: ServiceStatus | None = None
        self._expires_at = 0.0

    async def status(self) -> ServiceStatus:
        async with self._lock:
            if self._status is not None and self._clock() < self._expires_at:
                return self._status
            try:
                status = await self._check()
            except Exception:
                status = "down"
            self._status = status
            self._expires_at = self._clock() + self._ttl_s
            return status
