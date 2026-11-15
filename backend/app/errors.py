import logging
from collections.abc import Mapping
from typing import Any, Final

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.providers.base import ProviderError
from app.schemas import ErrorResponse

log = logging.getLogger("usa.backend")

ERROR_RESPONSES: Final[dict[int | str, dict[str, Any]]] = {
    status: {"model": ErrorResponse} for status in (409, 413, 415, 422, 429, 501, 502, 504)
}

_HTTP_CODES: Final[Mapping[int, str]] = {
    404: "not_found",
    405: "method_not_allowed",
    413: "payload_too_large",
}


class ApiError(Exception):
    def __init__(
        self,
        status_code: int,
        error_code: str,
        *,
        retryable: bool,
        headers: Mapping[str, str] | None = None,
    ) -> None:
        super().__init__(error_code)
        self.status_code = status_code
        self.error_code = error_code
        self.retryable = retryable
        self.headers = dict(headers or {})


def error_response(
    status_code: int, error_code: str, *, retryable: bool, headers: Mapping[str, str] | None = None
) -> JSONResponse:
    body = ErrorResponse(error_code=error_code, retryable=retryable)
    return JSONResponse(body.model_dump(), status_code=status_code, headers=dict(headers or {}))


async def _api_error(_: Request, exc: Exception) -> JSONResponse:
    assert isinstance(exc, ApiError)
    return error_response(exc.status_code, exc.error_code, retryable=exc.retryable, headers=exc.headers)


async def _validation_error(_: Request, exc: Exception) -> JSONResponse:
    return error_response(422, "invalid_request", retryable=False)


async def _http_error(_: Request, exc: Exception) -> JSONResponse:
    assert isinstance(exc, StarletteHTTPException)
    code = _HTTP_CODES.get(exc.status_code, "http_error")
    return error_response(exc.status_code, code, retryable=False, headers=exc.headers)


async def _not_implemented(_: Request, exc: Exception) -> JSONResponse:
    return error_response(501, "not_implemented", retryable=False)


async def _provider_error(_: Request, exc: Exception) -> JSONResponse:
    assert isinstance(exc, ProviderError)
    log.warning("provider error type=%s retryable=%s", type(exc).__name__, exc.retryable)
    return error_response(502, "provider_error", retryable=exc.retryable)


async def _provider_timeout(_: Request, exc: Exception) -> JSONResponse:
    log.warning("provider timeout")
    return error_response(504, "provider_timeout", retryable=True)


def install_error_handlers(app: FastAPI) -> None:
    """Map every failure to `{error_code, retryable}`; provider and validation text never leaves."""
    app.add_exception_handler(ApiError, _api_error)
    app.add_exception_handler(RequestValidationError, _validation_error)
    app.add_exception_handler(StarletteHTTPException, _http_error)
    app.add_exception_handler(NotImplementedError, _not_implemented)
    app.add_exception_handler(ProviderError, _provider_error)
    app.add_exception_handler(TimeoutError, _provider_timeout)
