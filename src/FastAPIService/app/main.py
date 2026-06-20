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
    # Registered in order: auth -> problem details -> rate limit (future)
    # Auth middleware: health endpoints are exempted via EXEMPT_PATHS
    from app.middleware.auth import JWTAuthMiddleware
    from app.middleware.problem_details import add_problem_details_handler

    app.add_middleware(
        JWTAuthMiddleware,
        jwks_url=settings.jwt_jwks_url,
        audience=settings.jwt_audience,
        issuer=settings.jwt_issuer,
    )
    add_problem_details_handler(app)

    # --- Routes ---
    # Health routes are registered first so they bypass all middleware
    from app.routes.health import router as health_router

    app.include_router(health_router)

    return app
