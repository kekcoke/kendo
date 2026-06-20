"""OpenTelemetry tracing initialization and access.

Provides init_tracing() called from lifespan startup,
and get_tracer() for creating spans in middleware and handlers.
"""

from __future__ import annotations

import logging

from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor, ConsoleSpanExporter

from app.config import Settings

logger = logging.getLogger(__name__)

_tracer: trace.Tracer | None = None


def init_tracing(settings: Settings) -> trace.Tracer:
    """Initialize the OpenTelemetry TracerProvider.

    Configures:
    - Service name from settings.otel_service_name
    - OTLP exporter if otel_exporter_otlp_endpoint is set
    - Console exporter fallback for local dev / CI
    - Batch span processor

    Args:
        settings: Application settings with otel_* fields.

    Returns:
        The configured Tracer instance.
    """
    global _tracer

    resource = Resource.create({
        "service.name": settings.otel_service_name,
        "service.version": "0.1.0",
        "deployment.environment": settings.env,
    })

    provider = TracerProvider(resource=resource)

    if settings.otel_exporter_otlp_endpoint:
        otlp_exporter = OTLPSpanExporter(
            endpoint=f"{settings.otel_exporter_otlp_endpoint}/v1/traces",
        )
        provider.add_span_processor(BatchSpanProcessor(otlp_exporter))
        logger.info("OpenTelemetry OTLP exporter configured: %s", settings.otel_exporter_otlp_endpoint)
    else:
        console_exporter = ConsoleSpanExporter()
        provider.add_span_processor(BatchSpanProcessor(console_exporter))
        logger.info("OpenTelemetry console exporter configured (no OTLP endpoint)")

    trace.set_tracer_provider(provider)

    _tracer = provider.get_tracer(settings.otel_service_name)
    logger.info("OpenTelemetry initialized: service=%s", settings.otel_service_name)
    return _tracer


def get_tracer() -> trace.Tracer:
    """Get the configured Tracer instance.

    Raises:
        RuntimeError: If init_tracing() has not been called yet.
    """
    if _tracer is None:
        raise RuntimeError("OpenTelemetry not initialized. Call init_tracing() first.")
    return _tracer


def get_current_trace_id() -> str:
    """Get the current trace ID as a 16-character hex string.

    Returns empty string if no active span exists.
    """
    span = trace.get_current_span()
    span_context = span.get_span_context()
    if span_context.is_valid:
        # Return last 16 hex chars of the 32-char trace ID
        return format(span_context.trace_id, "032x")[16:]
    return ""
