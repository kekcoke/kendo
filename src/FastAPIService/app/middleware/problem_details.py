"""RFC 7807 Problem Details exception handler.

Catches unhandled exceptions in route handlers and returns
`application/problem+json` responses matching the platform standard.
"""

from __future__ import annotations

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse


def add_problem_details_handler(app: FastAPI) -> None:
    """Register the RFC 7807 exception handler on the FastAPI app."""

    @app.exception_handler(Exception)
    async def _problem_details_handler(request: Request, exc: Exception) -> JSONResponse:
        if isinstance(exc, HTTPException):
            detail = exc.detail if isinstance(exc.detail, str) else "An error occurred"
            status_code = exc.status_code
        else:
            detail = "An internal error occurred"
            status_code = 500

        return JSONResponse(
            status_code=status_code,
            content={
                "type": "about:blank",
                "title": _status_title(status_code),
                "status": status_code,
                "detail": detail,
                "trace_id": "",  # Populated by OpenTelemetry middleware (M5.5+)
            },
            headers={"Content-Type": "application/problem+json"},
        )

    @app.exception_handler(ValueError)
    async def _value_error_handler(request: Request, exc: ValueError) -> JSONResponse:
        return JSONResponse(
            status_code=400,
            content={
                "type": "about:blank",
                "title": "Bad Request",
                "status": 400,
                "detail": str(exc),
            },
            headers={"Content-Type": "application/problem+json"},
        )


def _status_title(status_code: int) -> str:
    titles = {
        400: "Bad Request",
        401: "Unauthorized",
        403: "Forbidden",
        404: "Not Found",
        422: "Unprocessable Entity",
        429: "Too Many Requests",
        500: "Internal Server Error",
        503: "Service Unavailable",
        504: "Gateway Timeout",
    }
    return titles.get(status_code, "Unknown Error")
