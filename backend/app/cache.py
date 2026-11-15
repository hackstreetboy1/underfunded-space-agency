import asyncio
import hashlib
import logging
import os
import re
import time
import uuid
from collections import OrderedDict
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Final, TypeVar, cast

from app.errors import ApiError

log = logging.getLogger("usa.backend")

T = TypeVar("T")

IDEMPOTENCY_TTL_S: Final = 600.0

Clock = Callable[[], float]


@dataclass(frozen=True, slots=True)
class _Entry:
    fingerprint: str
    expires_at: float
    value: object
    size: int


@dataclass(frozen=True, slots=True)
class _InFlight:
    fingerprint: str
    started_at: float
    result: asyncio.Future[object]


# A stored JSON response is a few hundred bytes; audio is counted at its real size.
_NOMINAL_ENTRY_BYTES: Final = 1024


def _size_of(value: object) -> int:
    return len(value) if isinstance(value, (bytes, bytearray)) else _NOMINAL_ENTRY_BYTES


def _consume_outcome(future: asyncio.Future[object]) -> None:
    # A failure nobody waited for must not log "exception was never retrieved".
    if not future.cancelled():
        future.exception()


def fingerprint(payload: bytes | str) -> str:
    data = payload.encode("utf-8") if isinstance(payload, str) else payload
    return hashlib.sha256(data).hexdigest()


class IdempotencyCache:
    """In-memory `request_id -> response` store so client retries never run a provider twice.

    Keys are scoped per endpoint. A reused `request_id` with a different payload is a conflict
    (409), not a cache hit. A duplicate that arrives while the first is still running shares its
    outcome (value or error) instead of starting a second provider call. Failures are not stored,
    so a retry after an error calls the provider again. Memory is bounded by entry count and bytes.
    """

    def __init__(
        self,
        ttl_s: float = IDEMPOTENCY_TTL_S,
        max_entries: int = 512,
        max_bytes: int = 64 * 1024 * 1024,
        clock: Clock = time.monotonic,
    ) -> None:
        self._ttl_s = ttl_s
        self._max_entries = max_entries
        self._max_bytes = max_bytes
        self._clock = clock
        self._entries: OrderedDict[tuple[str, str], _Entry] = OrderedDict()
        self._bytes = 0
        self._in_flight: dict[tuple[str, str], _InFlight] = {}

    def __len__(self) -> int:
        return len(self._entries)

    @property
    def stored_bytes(self) -> int:
        return self._bytes

    @property
    def in_flight(self) -> int:
        return len(self._in_flight)

    async def get_or_compute(
        self,
        scope: str,
        request_id: str,
        payload_fingerprint: str,
        compute: Callable[[], Awaitable[T]],
    ) -> T:
        stored = self.lookup(scope, request_id, payload_fingerprint)
        if stored is not None:
            return cast(T, stored)
        pending = self.pending(scope, request_id, payload_fingerprint)
        if pending is not None:
            return cast(T, await asyncio.shield(pending))
        self.begin(scope, request_id, payload_fingerprint)
        try:
            value = await compute()
        except BaseException as exc:
            self.fail(scope, request_id, exc)
            raise
        self.complete(scope, request_id, value)
        return value

    def lookup(self, scope: str, request_id: str, payload_fingerprint: str) -> object | None:
        """The stored value for a repeated `request_id`. Raises 409 on a payload mismatch."""
        self._purge()
        entry = self._entries.get((scope, request_id))
        if entry is None:
            return None
        if entry.fingerprint != payload_fingerprint:
            raise ApiError(409, "request_id_conflict", retryable=False)
        return entry.value

    def pending(self, scope: str, request_id: str, payload_fingerprint: str) -> asyncio.Future[object] | None:
        """The outcome of a running computation for this `request_id`, if one is in flight.

        Await it through `asyncio.shield`, so a waiter that gives up never cancels the owner.
        """
        running = self._in_flight.get((scope, request_id))
        if running is None:
            return None
        if running.fingerprint != payload_fingerprint:
            raise ApiError(409, "request_id_conflict", retryable=False)
        return running.result

    def begin(self, scope: str, request_id: str, payload_fingerprint: str) -> None:
        """Marks a computation as running; end it with exactly one `complete` or `fail`."""
        result: asyncio.Future[object] = asyncio.get_running_loop().create_future()
        result.add_done_callback(_consume_outcome)
        self._in_flight[(scope, request_id)] = _InFlight(payload_fingerprint, self._clock(), result)

    def complete(self, scope: str, request_id: str, value: object) -> None:
        running = self._in_flight.pop((scope, request_id), None)
        if running is None:
            return
        self._store(scope, request_id, running.fingerprint, value)
        if not running.result.done():
            running.result.set_result(value)

    def fail(self, scope: str, request_id: str, error: BaseException) -> None:
        running = self._in_flight.pop((scope, request_id), None)
        if running is None or running.result.done():
            return
        if isinstance(error, Exception):
            running.result.set_exception(error)
        else:
            # Cancellation or shutdown of the owner: waiters get a retryable failure, not a cancellation.
            running.result.set_exception(TimeoutError())

    def store(self, scope: str, request_id: str, payload_fingerprint: str, value: object) -> None:
        self._store(scope, request_id, payload_fingerprint, value)

    def clear(self) -> None:
        self._entries.clear()
        self._bytes = 0
        for running in self._in_flight.values():
            if not running.result.done():
                running.result.set_exception(TimeoutError())
        self._in_flight.clear()

    def _store(self, scope: str, request_id: str, payload_fingerprint: str, value: object) -> None:
        key = (scope, request_id)
        size = _size_of(value)
        if size > self._max_bytes:
            return
        previous = self._entries.pop(key, None)
        if previous is not None:
            self._bytes -= previous.size
        self._entries[key] = _Entry(payload_fingerprint, self._clock() + self._ttl_s, value, size)
        self._bytes += size
        while len(self._entries) > self._max_entries or self._bytes > self._max_bytes:
            _, evicted = self._entries.popitem(last=False)
            self._bytes -= evicted.size

    def _purge(self) -> None:
        now = self._clock()
        expired = [key for key, entry in self._entries.items() if entry.expires_at <= now]
        for key in expired:
            self._bytes -= self._entries.pop(key).size
        # An owner that never reported back (its stream was never iterated) must not block the id forever.
        stale = [key for key, running in self._in_flight.items() if now - running.started_at > self._ttl_s]
        for key in stale:
            self.fail(*key, TimeoutError())


_HEX_KEY: Final = re.compile(r"\A[0-9a-f]{64}\Z")


def phrase_key(voice_id: str, model_id: str, text: str) -> str:
    """`sha256(voice_id + model + text)` (SCRIPTING 14.4)."""
    return fingerprint(voice_id + model_id + text)


class TtsPhraseCache:
    """On-disk MP3 cache for fixed phrases (acknowledgements, greetings) flagged `cacheable`.

    One file per phrase, written atomically. A read or write failure only costs a provider call.
    """

    def __init__(self, directory: Path) -> None:
        self._directory = directory

    @property
    def directory(self) -> Path:
        return self._directory

    def path_for(self, key: str) -> Path:
        if not _HEX_KEY.match(key):
            raise ValueError("phrase cache key must be a sha256 hex digest")
        return self._directory / f"{key}.mp3"

    async def get(self, key: str) -> bytes | None:
        return await asyncio.to_thread(self._read, self.path_for(key))

    async def put(self, key: str, audio: bytes) -> None:
        if audio:
            await asyncio.to_thread(self._write, self.path_for(key), audio)

    @staticmethod
    def _read(path: Path) -> bytes | None:
        try:
            data = path.read_bytes()
        except FileNotFoundError:
            return None
        except OSError as exc:
            log.warning("tts cache read failed type=%s", type(exc).__name__)
            return None
        return data or None

    @staticmethod
    def _write(path: Path, audio: bytes) -> None:
        # Short temp name: the full digest plus a uuid passed Windows' 260-character path limit in deep
        # temp folders, and the write then failed silently (the clip was never cached).
        temp = path.with_name(f"{uuid.uuid4().hex[:12]}.tmp")
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            temp.write_bytes(audio)
            os.replace(temp, path)
        except OSError as exc:
            log.warning("tts cache write failed type=%s", type(exc).__name__)
            temp.unlink(missing_ok=True)
