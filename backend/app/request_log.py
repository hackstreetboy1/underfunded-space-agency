import logging
import re
import time
import uuid
from collections.abc import Iterable
from typing import Final

from starlette.requests import Request
from starlette.types import ASGIApp, Message, Receive, Scope, Send

from app.errors import error_response

log = logging.getLogger("usa.backend")

_STATE_KEY: Final = "request_id"
_UNSAFE: Final = re.compile(r"[^A-Za-z0-9_.:-]")
_MAX_ID_LENGTH: Final = 64


# `key=...` style query parameters and credential headers, as they appear in SDK and HTTP debug logs.
_SECRET_QUERY: Final = re.compile(
    r"(?i)\b((?:api[_-]?)?key|access_token|token|signature|sig|xi-api-key|x-goog-api-key)=([^&\s\"'#]+)"
)
_SECRET_HEADER: Final = re.compile(
    r"(?i)\b(authorization|proxy-authorization|xi-api-key|x-goog-api-key|x-api-key)(\"?'?\s*[:,]\s*\"?'?)"
    r"(?:bearer\s+|basic\s+)?([^\s\"',}]+)"
)
_REDACTED: Final = "[redacted]"
# Third-party loggers that print request URLs or headers at INFO/DEBUG.
_CHATTY_LOGGERS: Final = ("httpx", "httpcore", "google_genai", "google.genai", "elevenlabs", "urllib3")
_HANDLER_LOGGERS: Final = ("", "usa.backend", "uvicorn", "uvicorn.error", "uvicorn.access")


class RedactingFilter(logging.Filter):
    """Scrubs credentials from every record before a handler formats it.

    Covers `key=`-style query parameters, credential headers and the exact configured key values,
    in the message, its arguments and any traceback text.
    """

    def __init__(self) -> None:
        super().__init__()
        self._secrets: set[str] = set()

    def add_secret(self, value: str) -> None:
        if len(value.strip()) >= 8:
            self._secrets.add(value.strip())

    def redact(self, text: str) -> str:
        for secret in self._secrets:
            text = text.replace(secret, _REDACTED)
        text = _SECRET_QUERY.sub(lambda m: f"{m.group(1)}={_REDACTED}", text)
        return _SECRET_HEADER.sub(lambda m: f"{m.group(1)}{m.group(2)}{_REDACTED}", text)

    def _redact_value(self, value: object) -> object:
        return self.redact(value) if isinstance(value, str) else value

    def filter(self, record: logging.LogRecord) -> bool:
        # Arguments are redacted in place first, because some formatters (uvicorn's access log)
        # unpack `record.args` themselves; only a secret split across message and arguments
        # needs the record collapsed into one pre-formatted message.
        if isinstance(record.args, tuple):
            record.args = tuple(self._redact_value(arg) for arg in record.args)
        elif isinstance(record.args, dict):
            record.args = {key: self._redact_value(arg) for key, arg in record.args.items()}
        try:
            message = record.getMessage()
        except Exception:
            message = str(record.msg)
        redacted = self.redact(message)
        if redacted != message:
            record.msg = redacted
            record.args = None
        if record.exc_info and not record.exc_text:
            record.exc_text = logging.Formatter().formatException(record.exc_info)
        if record.exc_text:
            record.exc_text = self.redact(record.exc_text)
        if record.stack_info:
            record.stack_info = self.redact(record.stack_info)
        return True


REDACTION: Final = RedactingFilter()


def _install_redaction(logger: logging.Logger) -> None:
    for handler in logger.handlers:
        if REDACTION not in handler.filters:
            handler.addFilter(REDACTION)


def configure_logging(level: int = logging.INFO, secrets: Iterable[str] = ()) -> None:
    """Attach one stream handler to the backend logger and redact credentials on every handler
    that exists (ours, uvicorn's and the root's). Safe to call repeatedly."""
    for secret in secrets:
        REDACTION.add_secret(secret)
    if not log.handlers:
        handler = logging.StreamHandler()
        handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(name)s %(message)s"))
        log.addHandler(handler)
        log.setLevel(level)
    for name in _CHATTY_LOGGERS:
        logging.getLogger(name).setLevel(logging.WARNING)
    for name in _HANDLER_LOGGERS:
        _install_redaction(logging.getLogger(name))


def bind_request_id(request: Request, request_id: str) -> None:
    """Expose the client's `request_id` to the access log line for this request."""
    request.state.request_id = _UNSAFE.sub("_", request_id)[:_MAX_ID_LENGTH]


class RequestLogMiddleware:
    """Logs one line per request (id, path, status, duration) and turns crashes into opaque 500s.

    Bodies, headers, query strings and exception messages are never logged.
    """

    def __init__(self, app: ASGIApp) -> None:
        self._app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self._app(scope, receive, send)
            return

        state: dict[str, object] = scope.setdefault("state", {})
        started = time.perf_counter()
        status = 500
        response_started = False

        async def send_wrapper(message: Message) -> None:
            nonlocal status, response_started
            if message["type"] == "http.response.start":
                status = int(message["status"])
                response_started = True
            await send(message)

        try:
            await self._app(scope, receive, send_wrapper)
        except Exception as exc:
            log.error("unhandled error type=%s path=%s", type(exc).__name__, scope.get("path", ""))
            if response_started:
                # Mid-stream: returning without finishing the body makes the server drop the connection,
                # which the client reads as a lost stream. Re-raising would only add a traceback.
                return
            status = 500
            await error_response(500, "internal_error", retryable=False)(scope, receive, send)
        finally:
            request_id = state.get(_STATE_KEY) or f"srv-{uuid.uuid4().hex[:12]}"
            duration_ms = (time.perf_counter() - started) * 1000.0
            log.info(
                "request id=%s path=%s status=%d duration_ms=%.1f",
                request_id,
                scope.get("path", ""),
                status,
                duration_ms,
            )
