"""FastAPI application factory for Kendo AI/Vector Service."""

from __future__ import annotations

from contextlib import asynccontextmanager
from typing import AsyncGenerator

from fastapi import FastAPI

from app.config import Settings


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """Application lifespan — startup and shutdown hooks."""
    settings = app.state.settings

    # Initialize OpenTelemetry tracing
    from app.observability.tracing import init_tracing
    init_tracing(settings)

    # Initialize circuit breakers and store in app state
    from app.resilience.circuit_breaker import create_pgvector_breaker, create_openai_breaker
    from app.db import set_pgvector_breaker

    pgv_breaker = create_pgvector_breaker(
        fail_max=settings.breaker_pgvector_fail_max,
        reset_timeout=settings.breaker_pgvector_reset_timeout,
    )
    oai_breaker = create_openai_breaker(
        fail_max=settings.breaker_openai_fail_max,
        reset_timeout=settings.breaker_openai_reset_timeout,
    )

    app.state.resilience = {
        "pgvector_breaker": pgv_breaker,
        "openai_breaker": oai_breaker,
    }

    # Wire pgvector breaker into db module
    set_pgvector_breaker(pgv_breaker)

    # Initialize pgvector pool if DSN is configured
    if settings.vector_read_dsn:
        from app.db import create_pool
        await create_pool(settings.vector_read_dsn)

    yield

    # Teardown
    if settings.vector_read_dsn:
        from app.db import close_pool
        await close_pool()


def create_app(settings: Settings | None = None) -> FastAPI:
    """Create and configure the FastAPI application."""
    if settings is None:
        settings = Settings()  # type: ignore[call-arg]

    app = FastAPI(
        title=settings.service_name,
        version="0.1.0",
        lifespan=lifespan,
        contact={"name": "Kendo Platform"},
    )

    app.state.settings = settings

    # --- Middleware ---
    # Registered in order: auth -> trace propagation -> problem details
    # Auth middleware: health endpoints are exempted via EXEMPT_PATHS
    from app.middleware.auth import JWTAuthMiddleware
    from app.middleware.trace_propagation import TracePropagationMiddleware
    from app.middleware.problem_details import add_problem_details_handler

    app.add_middleware(
        JWTAuthMiddleware,
        jwks_url=settings.jwt_jwks_url,
        audience=settings.jwt_audience,
        issuer=settings.jwt_issuer,
    )
    app.add_middleware(TracePropagationMiddleware)
    add_problem_details_handler(app)

    # --- Routes ---
    # Health routes are registered first so they bypass all middleware
    from app.routes.health import router as health_router

    app.include_router(health_router)

    # RAG routes — JWT-protected by middleware
    from app.api.v1.rag import router as rag_router

    app.include_router(rag_router)

    return app
