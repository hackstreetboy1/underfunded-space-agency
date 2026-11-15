import ipaddress
import logging
from collections.abc import Mapping
from typing import Final
from urllib.parse import urlsplit

from fastapi import HTTPException
from starlette.datastructures import Headers
from starlette.types import ASGIApp, Message, Receive, Scope, Send

from app.errors import error_response

log = logging.getLogger("usa.backend")

LOOPBACK_NAMES: Final = frozenset({"localhost", "127.0.0.1", "::1"})
MAX_JSON_BODY_BYTES: Final = 256 * 1024
# `/v1/stt` carries the WAV plus the multipart framing and the request_id field.
MULTIPART_OVERHEAD_BYTES: Final = 64 * 1024


def is_loopback_host(host: str) -> bool:
    name = host.strip().lower().removeprefix("[").removesuffix("]")
    if name in LOOPBACK_NAMES:
        return True
    try:
        return ipaddress.ip_address(name).is_loopback
    except ValueError:
        return False


def _host_without_port(authority: str) -> str:
    if authority.startswith("["):
        return authority.split("]", 1)[0] + "]"
    return authority.rsplit(":", 1)[0] if authority.count(":") == 1 else authority


class BodyTooLargeError(HTTPException):
    def __init__(self) -> None:
        super().__init__(status_code=413)


class RequestGuardMiddleware:
    """Keeps the proxy a local service even if it is started with a wider bind by mistake.

    - The peer address, the `Host` header and any `Origin` header must be loopback. The Host check
      stops DNS rebinding; the Origin check stops a web page in the player's browser from spending
      provider quota with a simple cross-site POST (the Godot client sends no Origin).
    - Request bodies are capped before they are parsed: a declared `Content-Length` over the cap is
      rejected at once, and a streamed body is cut off as soon as it passes the cap (413).
    """

    def __init__(self, app: ASGIApp, body_limits: Mapping[str, int] | None = None) -> None:
        self._app = app
        self._body_limits = dict(body_limits or {})

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self._app(scope, receive, send)
            return

        headers = Headers(scope=scope)
        if not self._is_local(scope, headers):
            log.warning("rejected non-local request path=%s", scope.get("path", ""))
            await error_response(403, "forbidden", retryable=False)(scope, receive, send)
            return

        limit = self._body_limits.get(scope.get("path", ""), MAX_JSON_BODY_BYTES)
        declared = headers.get("content-length")
        if declared is not None and (not declared.isdigit() or int(declared) > limit):
            await error_response(413, "payload_too_large", retryable=False)(scope, receive, send)
            return

        received = 0

        async def limited_receive() -> Message:
            nonlocal received
            message = await receive()
            if message["type"] == "http.request":
                received += len(message.get("body", b""))
                if received > limit:
                    raise BodyTooLargeError()
            return message

        await self._app(scope, limited_receive, send)

    @staticmethod
    def _is_local(scope: Scope, headers: Headers) -> bool:
        client = scope.get("client")
        if client is not None and not is_loopback_host(str(client[0])):
            return False
        host = headers.get("host")
        if host is None or not is_loopback_host(_host_without_port(host)):
            return False
        origin = headers.get("origin")
        if origin is None:
            return True
        parts = urlsplit(origin)
        return parts.scheme in ("http", "https") and is_loopback_host(parts.hostname or "")
