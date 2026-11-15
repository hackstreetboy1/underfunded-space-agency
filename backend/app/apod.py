"""Astronomy Picture of the Day for the game's loading screens.

NASA's API is rate-limited, so the proxy asks it at most once per UTC calendar day, shared by every game launch: the
attempt is recorded before the call, whatever its outcome. The day's image (the standard `url`, never `hdurl`) is
downloaded once, capped at `MAX_IMAGE_BYTES`, and served from the cache, so the game never contacts NASA. When today's
picture cannot be used (a video day, an error, an oversized or unknown image) the newest cached image day is served
instead; with nothing cached there is no picture.
"""

import asyncio
import json
import logging
import os
import re
import uuid
from collections.abc import Callable
from dataclasses import asdict, dataclass
from datetime import UTC, date, datetime
from pathlib import Path
from typing import Final, Protocol
from urllib.parse import urlsplit

import httpx
from pydantic import BaseModel, ConfigDict, Field, ValidationError

from app.schemas.apod import APOD_DATE_PATTERN

log = logging.getLogger("usa.backend")

APOD_API_URL: Final = "https://api.nasa.gov/planetary/apod"
# The key travels in this header, never in a URL, so no request line or redirect can echo it.
API_KEY_HEADER: Final = "X-Api-Key"
MAX_IMAGE_BYTES: Final = 5 * 1024 * 1024
# Smaller than this is a logo or icon, not a photo worth a full-screen backdrop: treated like a video day.
MIN_IMAGE_BYTES: Final = 40 * 1024
MIN_IMAGE_LONG_SIDE_PX: Final = 800
MAX_EXPLANATION_CHARS: Final = 400
KEEP_DAYS: Final = 7
_METADATA_TIMEOUT_S: Final = 10.0
_IMAGE_TIMEOUT_S: Final = 20.0
_USER_AGENT: Final = "usa-proxy-apod/1.0"
_DATE: Final = re.compile(APOD_DATE_PATTERN)
_ATTEMPT_FILE: Final = "last_attempt.json"
# Formats Godot can decode from a buffer, by their leading bytes.
_IMAGE_SIGNATURES: Final = (
    (b"\xff\xd8\xff", "image/jpeg", "jpg"),
    (b"\x89PNG\r\n\x1a\n", "image/png", "png"),
)
_FAKE_IMAGE: Final = Path(__file__).resolve().parents[2] / "content" / "nasa" / "media" / "image_library" / "S66-24465.jpg"


class ApodUnavailable(Exception):
    """Today's picture cannot be used. `reason` is a short code for the log, never provider text."""

    def __init__(self, reason: str) -> None:
        super().__init__(reason)
        self.reason = reason


class ApodMetadata(BaseModel):
    """The fields of NASA's APOD record the proxy uses; anything else is ignored."""

    model_config = ConfigDict(extra="ignore")

    date: str = Field(pattern=APOD_DATE_PATTERN)
    title: str
    media_type: str
    url: str | None = None
    copyright: str | None = None
    explanation: str | None = None


@dataclass(frozen=True, slots=True)
class ApodEntry:
    """One cached image day (`<date>.json` beside `<date>.<ext>`)."""

    date: str
    title: str
    media_type: str
    url: str
    explanation: str
    copyright: str | None
    content_type: str
    image_file: str


@dataclass(frozen=True, slots=True)
class ApodAttempt:
    """The once-a-day NASA call: its UTC day, outcome code and, on success, the APOD date it cached."""

    utc_date: str
    outcome: str
    apod_date: str | None = None


@dataclass(frozen=True, slots=True)
class ApodImage:
    data: bytes
    content_type: str
    extension: str


class ApodSource(Protocol):
    async def fetch_metadata(self) -> ApodMetadata: ...

    async def fetch_image(self, url: str, max_bytes: int) -> ApodImage: ...


def sniff_image(data: bytes) -> tuple[str, str] | None:
    """`(content_type, extension)` for a JPEG or PNG, else None."""
    for signature, content_type, extension in _IMAGE_SIGNATURES:
        if data.startswith(signature):
            return content_type, extension
    return None


# JPEG start-of-frame markers (baseline, progressive, lossless, arithmetic); they carry the frame size.
_JPEG_SOF: Final = frozenset({0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF})


def image_dimensions(data: bytes) -> tuple[int, int] | None:
    """`(width, height)` from a PNG IHDR or a JPEG start-of-frame header, else None."""
    if data.startswith(_IMAGE_SIGNATURES[1][0]):
        if len(data) < 24 or data[12:16] != b"IHDR":
            return None
        return int.from_bytes(data[16:20], "big"), int.from_bytes(data[20:24], "big")
    if not data.startswith(b"\xff\xd8"):
        return None
    i = 2
    while i + 4 <= len(data):
        if data[i] != 0xFF:
            i += 1
            continue
        marker = data[i + 1]
        if marker == 0xFF:
            i += 1
            continue
        if marker in (0xD8, 0x01) or 0xD0 <= marker <= 0xD7:
            i += 2
            continue
        if marker in (0xD9, 0xDA):
            return None
        length = int.from_bytes(data[i + 2 : i + 4], "big")
        if marker in _JPEG_SOF:
            if i + 9 > len(data):
                return None
            return int.from_bytes(data[i + 7 : i + 9], "big"), int.from_bytes(data[i + 5 : i + 7], "big")
        i += 2 + length
    return None


def is_backdrop_photo(data: bytes) -> bool:
    """Big enough to be a photo: at least `MIN_IMAGE_BYTES` and `MIN_IMAGE_LONG_SIDE_PX` on its long side."""
    if len(data) < MIN_IMAGE_BYTES:
        return False
    size = image_dimensions(data)
    return size is not None and max(size) >= MIN_IMAGE_LONG_SIDE_PX


def short_explanation(text: str | None, limit: int = MAX_EXPLANATION_CHARS) -> str:
    """The first sentences that fit in `limit` characters, else a word-boundary cut with an ellipsis."""
    flat = " ".join((text or "").split())
    if len(flat) <= limit:
        return flat
    head = flat[:limit]
    sentence_end = max(head.rfind(". "), head.rfind("! "), head.rfind("? "))
    if sentence_end >= limit // 3:
        return head[: sentence_end + 1]
    return head.rsplit(" ", 1)[0].rstrip(",;:") + "…"


def clean_copyright(value: str | None) -> str | None:
    """APOD copyright fields carry stray newlines and padding; an empty one means public domain."""
    flat = " ".join((value or "").split())
    return flat or None


def utc_today() -> date:
    return datetime.now(UTC).date()


# Sources --------------------------------------------------------------------------------------------------------------


class NasaApodSource:
    """The real API. Counts every request it sends (`calls`) so a test or a live check can prove the budget."""

    def __init__(self, api_key: str, transport: httpx.AsyncBaseTransport | None = None) -> None:
        self._api_key = api_key
        self._transport = transport
        self.metadata_calls = 0
        self.image_calls = 0

    async def fetch_metadata(self) -> ApodMetadata:
        self.metadata_calls += 1
        log.info("apod: NASA metadata request (call %d this process)", self.metadata_calls)
        async with httpx.AsyncClient(
            transport=self._transport,
            timeout=_METADATA_TIMEOUT_S,
            follow_redirects=False,
            headers={"User-Agent": _USER_AGENT},
        ) as client:
            try:
                response = await client.get(APOD_API_URL, headers={API_KEY_HEADER: self._api_key})
            except httpx.HTTPError as exc:
                raise ApodUnavailable(f"transport_{type(exc).__name__}") from None
        log.info("apod: NASA metadata status=%d", response.status_code)
        if response.status_code != 200:
            raise ApodUnavailable(f"http_{response.status_code}")
        try:
            return ApodMetadata.model_validate(response.json())
        except (ValueError, ValidationError):
            raise ApodUnavailable("bad_metadata") from None

    async def fetch_image(self, url: str, max_bytes: int) -> ApodImage:
        parts = urlsplit(url)
        host = (parts.hostname or "").lower()
        # Only NASA's own image hosts; the key header is never attached here.
        if parts.scheme not in ("https", "http") or not (host == "nasa.gov" or host.endswith(".nasa.gov")):
            raise ApodUnavailable("image_host_not_allowed")
        self.image_calls += 1
        log.info("apod: image download (call %d this process)", self.image_calls)
        async with httpx.AsyncClient(
            transport=self._transport,
            timeout=_IMAGE_TIMEOUT_S,
            follow_redirects=True,
            max_redirects=3,
            headers={"User-Agent": _USER_AGENT},
        ) as client:
            try:
                async with client.stream("GET", url) as response:
                    if response.status_code != 200:
                        raise ApodUnavailable(f"image_http_{response.status_code}")
                    declared = response.headers.get("content-length")
                    if declared is not None and declared.isdigit() and int(declared) > max_bytes:
                        raise ApodUnavailable("image_too_large")
                    chunks: list[bytes] = []
                    size = 0
                    async for chunk in response.aiter_bytes():
                        size += len(chunk)
                        if size > max_bytes:
                            raise ApodUnavailable("image_too_large")
                        chunks.append(chunk)
            except httpx.HTTPError as exc:
                raise ApodUnavailable(f"image_transport_{type(exc).__name__}") from None
        return _as_image(b"".join(chunks))


class FakeApodSource:
    """Offline stand-in: a bundled, already approved archive photo dated today. Never touches the network."""

    def __init__(self, image_path: Path = _FAKE_IMAGE, today: Callable[[], date] = utc_today) -> None:
        self._image_path = image_path
        self._today = today

    async def fetch_metadata(self) -> ApodMetadata:
        return ApodMetadata(
            date=self._today().isoformat(),
            title="Practice sky",
            media_type="image",
            url="https://apod.nasa.gov/apod/image/practice.jpg",
            copyright=None,
            explanation="A stand-in picture served by the practice proxy, so loading screens work offline.",
        )

    async def fetch_image(self, url: str, max_bytes: int) -> ApodImage:
        try:
            data = await asyncio.to_thread(self._image_path.read_bytes)
        except OSError:
            raise ApodUnavailable("fake_image_missing") from None
        if len(data) > max_bytes:
            raise ApodUnavailable("image_too_large")
        return _as_image(data)


def _as_image(data: bytes) -> ApodImage:
    sniffed = sniff_image(data)
    if sniffed is None:
        raise ApodUnavailable("image_format_unsupported")
    return ApodImage(data, *sniffed)


# Stores ---------------------------------------------------------------------------------------------------------------


class ApodStore(Protocol):
    def last_attempt(self) -> ApodAttempt | None: ...

    def record_attempt(self, attempt: ApodAttempt) -> None: ...

    def save(self, entry: ApodEntry, image: bytes) -> None: ...

    def entries(self) -> list[ApodEntry]: ...

    def image(self, entry: ApodEntry) -> bytes | None: ...


class MemoryApodStore:
    """Fake-provider mode: nothing is written to `backend/cache/apod`, so a stand-in can never be served as real."""

    def __init__(self) -> None:
        self._attempt: ApodAttempt | None = None
        self._entries: dict[str, tuple[ApodEntry, bytes]] = {}

    def last_attempt(self) -> ApodAttempt | None:
        return self._attempt

    def record_attempt(self, attempt: ApodAttempt) -> None:
        self._attempt = attempt

    def save(self, entry: ApodEntry, image: bytes) -> None:
        self._entries[entry.date] = (entry, image)

    def entries(self) -> list[ApodEntry]:
        return [entry for entry, _ in self._entries.values()]

    def image(self, entry: ApodEntry) -> bytes | None:
        stored = self._entries.get(entry.date)
        return stored[1] if stored is not None else None


class DiskApodStore:
    """`<dir>/<date>.json` + `<dir>/<date>.<ext>` per image day, and `last_attempt.json`; writes are atomic.

    Only the newest `KEEP_DAYS` image days are kept. A read or write failure is logged by type and costs only the
    cached copy, never the once-a-day budget (the attempt marker is written first).
    """

    def __init__(self, directory: Path, keep_days: int = KEEP_DAYS) -> None:
        self._directory = directory
        self._keep_days = keep_days

    @property
    def directory(self) -> Path:
        return self._directory

    def last_attempt(self) -> ApodAttempt | None:
        try:
            raw = json.loads((self._directory / _ATTEMPT_FILE).read_text(encoding="utf-8"))
            attempt = ApodAttempt(raw["utc_date"], raw["outcome"], raw.get("apod_date"))
        except (OSError, ValueError, TypeError, KeyError):
            return None
        return attempt if isinstance(attempt.utc_date, str) and _DATE.match(attempt.utc_date) else None

    def record_attempt(self, attempt: ApodAttempt) -> None:
        record = asdict(attempt) | {"at": datetime.now(UTC).replace(microsecond=0).isoformat()}
        self._write(_ATTEMPT_FILE, json.dumps(record).encode("utf-8"))

    def save(self, entry: ApodEntry, image: bytes) -> None:
        if not _DATE.match(entry.date):
            return
        self._write(entry.image_file, image)
        self._write(f"{entry.date}.json", json.dumps(asdict(entry), ensure_ascii=False, indent=2).encode("utf-8"))
        self._prune()

    def entries(self) -> list[ApodEntry]:
        found: list[ApodEntry] = []
        try:
            paths = sorted(self._directory.glob("????-??-??.json"))
        except OSError:
            return found
        for path in paths:
            try:
                entry = ApodEntry(**json.loads(path.read_text(encoding="utf-8")))
            except (OSError, ValueError, TypeError):
                continue
            if _DATE.match(entry.date) and (self._directory / entry.image_file).is_file():
                found.append(entry)
        return found

    def image(self, entry: ApodEntry) -> bytes | None:
        try:
            return (self._directory / entry.image_file).read_bytes()
        except OSError as exc:
            log.warning("apod cache read failed type=%s", type(exc).__name__)
            return None

    def _write(self, name: str, data: bytes) -> None:
        target = self._directory / name
        temp = target.with_name(f"{uuid.uuid4().hex[:12]}.tmp")
        try:
            self._directory.mkdir(parents=True, exist_ok=True)
            temp.write_bytes(data)
            os.replace(temp, target)
        except OSError as exc:
            log.warning("apod cache write failed type=%s", type(exc).__name__)
            temp.unlink(missing_ok=True)

    def _prune(self) -> None:
        for stale in sorted(self.entries(), key=lambda e: e.date)[: -self._keep_days]:
            for name in (stale.image_file, f"{stale.date}.json"):
                try:
                    (self._directory / name).unlink(missing_ok=True)
                except OSError:
                    pass


# Service --------------------------------------------------------------------------------------------------------------


class ApodService:
    """Today's picture, else the newest cached image day, else None. Concurrent requests share one refresh."""

    def __init__(self, source: ApodSource, store: ApodStore, today: Callable[[], date] = utc_today) -> None:
        self._source = source
        self._store = store
        self._today = today
        self._lock = asyncio.Lock()
        # Usability per cached image file, so an entry cached before the photo rule is judged once, never re-fetched.
        self._usable: dict[str, bool] = {}

    @property
    def source(self) -> ApodSource:
        return self._source

    async def current(self) -> tuple[ApodEntry, bool] | None:
        """The entry to show and whether it is a fallback (not today's picture)."""
        async with self._lock:
            utc_day = self._today().isoformat()
            await self._refresh(utc_day)
            newest = self._newest()
            if newest is None:
                return None
            attempt = self._store.last_attempt()
            todays = attempt is not None and attempt.utc_date == utc_day and attempt.apod_date == newest.date
            return newest, not todays

    def _newest(self) -> ApodEntry | None:
        entries = self._usable_entries()
        return max(entries, key=lambda e: e.date) if entries else None

    def _usable_entries(self) -> list[ApodEntry]:
        usable = []
        for entry in self._store.entries():
            key = f"{entry.date}/{entry.image_file}"
            if key not in self._usable:
                data = self._store.image(entry)
                self._usable[key] = data is not None and is_backdrop_photo(data)
                if not self._usable[key]:
                    log.info("apod: cached %s is not a backdrop photo; skipped", entry.date)
            if self._usable[key]:
                usable.append(entry)
        return usable

    async def image(self, day: str | None) -> tuple[bytes, str] | None:
        """The image of `day` (one served by `current`), or of the newest cached day."""
        entry = self._find(day) if day is not None else self._newest()
        if entry is None:
            return None
        data = await asyncio.to_thread(self._store.image, entry)
        return (data, entry.content_type) if data else None

    def _find(self, day: str) -> ApodEntry | None:
        return next((e for e in self._usable_entries() if e.date == day), None)

    async def _refresh(self, utc_day: str) -> None:
        attempt = self._store.last_attempt()
        if attempt is not None and attempt.utc_date == utc_day:
            return
        # Recorded before the call: a crash, timeout or error still spends today's single call.
        self._store.record_attempt(ApodAttempt(utc_day, "started"))
        try:
            metadata = await self._source.fetch_metadata()
            if metadata.media_type != "image" or not metadata.url:
                raise ApodUnavailable(f"media_type_{re.sub(r'[^a-z]', '', metadata.media_type.lower())[:16]}")
            if not any(e.date == metadata.date for e in self._store.entries()):
                image = await self._source.fetch_image(metadata.url, MAX_IMAGE_BYTES)
                if not is_backdrop_photo(image.data):
                    raise ApodUnavailable("image_not_a_photo")
                entry = ApodEntry(
                    date=metadata.date,
                    title=" ".join(metadata.title.split()),
                    media_type=metadata.media_type,
                    url=metadata.url,
                    explanation=short_explanation(metadata.explanation),
                    copyright=clean_copyright(metadata.copyright),
                    content_type=image.content_type,
                    image_file=f"{metadata.date}.{image.extension}",
                )
                await asyncio.to_thread(self._store.save, entry, image.data)
        except ApodUnavailable as exc:
            log.warning("apod: today's picture unavailable reason=%s; serving the newest cached day", exc.reason)
            self._store.record_attempt(ApodAttempt(utc_day, exc.reason))
            return
        self._store.record_attempt(ApodAttempt(utc_day, "ok", metadata.date))
        log.info("apod: cached %s", metadata.date)
