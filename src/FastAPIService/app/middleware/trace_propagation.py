"""Trace propagation middleware — extracts traceparent from Gateway requests.

Implements W3C Trace Context propagation so that FastAPI spans link back
to the originating Gateway span. The `traceparent` header is set by the
Gateway's existing Polly pipeline when making HTTP requests to FastAPI.
"""

from __future__ import annotations

from typing import Any

from opentelemetry import trace
from opentelemetry.propagate import extract
from opentelemetry.trace import SpanKind
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.types import ASGIApp


class TracePropagationMiddleware(BaseHTTPMiddleware):
    """Middleware that extracts traceparent headers and creates child spans.

    This middleware:
    1. Extracts the `traceparent` header from the incoming Gateway request
    2. Creates a child span linked to the Gateway's trace
    3. Populates span attributes from request context
    4. Ensures trace correlation appears in all downstream log lines
    """

    def __init__(self, app: ASGIApp) -> None:
        super().__init__(app)

    async def dispatch(self, request: Any, call_next: Any) -> Any:
        tracer = trace.get_tracer(__name__)

        # Extract trace context from headers (W3C Trace Context)
        ctx = extract(request.headers)
        span_name = f"{request.method} {request.url.path}"

        with tracer.start_as_current_span(
            span_name,
            context=ctx,
            kind=SpanKind.SERVER,
            attributes={
                "http.method": request.method,
                "http.url": str(request.url),
                "http.path": request.url.path,
            },
        ) as span:
            response = await call_next(request)
            span.set_attribute("http.status_code", response.status_code)

        return response
