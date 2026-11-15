import math
import time
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from typing import Final, Literal

from app.errors import ApiError

Endpoint = Literal["stt", "interpret", "narrate", "tip", "explain", "tts"]
Clock = Callable[[], float]


@dataclass(frozen=True, slots=True)
class BucketSpec:
    capacity: float
    refill_per_s: float


DEFAULT_LIMITS: Final[Mapping[Endpoint, BucketSpec]] = {
    "stt": BucketSpec(capacity=10, refill_per_s=1.0),
    "interpret": BucketSpec(capacity=10, refill_per_s=1.0),
    "narrate": BucketSpec(capacity=20, refill_per_s=2.0),
    "tip": BucketSpec(capacity=5, refill_per_s=0.2),
    "explain": BucketSpec(capacity=5, refill_per_s=0.2),
    "tts": BucketSpec(capacity=20, refill_per_s=2.0),
}

# Server-side budgets; Godot's per-request timeouts must be longer (README "Client timeouts").
LATENCY_BUDGET_S: Final[Mapping[Endpoint, float]] = {
    "stt": 2.5,
    # Room for the Gemini fallback chain: 2.5 s + 2.5 s + the rest (`providers/gemini.py`).
    "interpret": 7.0,
    "narrate": 6.0,
    "tip": 6.0,
    "explain": 4.0,
    "tts": 2.0,
}


class TokenBucket:
    def __init__(self, spec: BucketSpec, clock: Clock = time.monotonic) -> None:
        self._spec = spec
        self._clock = clock
        self._tokens = spec.capacity
        self._updated = clock()

    def try_acquire(self) -> float:
        """Takes one token. Returns 0 on success, otherwise the seconds until one is available."""
        now = self._clock()
        elapsed = max(0.0, now - self._updated)
        self._tokens = min(self._spec.capacity, self._tokens + elapsed * self._spec.refill_per_s)
        self._updated = now
        if self._tokens >= 1.0:
            self._tokens -= 1.0
            return 0.0
        if self._spec.refill_per_s <= 0:
            return math.inf
        return (1.0 - self._tokens) / self._spec.refill_per_s


class RateLimiter:
    """Per-endpoint token buckets. Only requests that reach a provider are charged (`routers/_common.charge`):
    invalid requests, idempotent replays and phrase-cache hits are free."""

    def __init__(self, specs: Mapping[Endpoint, BucketSpec] = DEFAULT_LIMITS, clock: Clock = time.monotonic) -> None:
        merged = {**DEFAULT_LIMITS, **specs}
        self._buckets: dict[Endpoint, TokenBucket] = {
            endpoint: TokenBucket(spec, clock) for endpoint, spec in merged.items()
        }

    def check(self, endpoint: Endpoint) -> None:
        wait_s = self._buckets[endpoint].try_acquire()
        if wait_s > 0:
            retry_after = "60" if math.isinf(wait_s) else str(max(1, math.ceil(wait_s)))
            raise ApiError(429, "rate_limited", retryable=True, headers={"Retry-After": retry_after})
