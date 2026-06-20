"""Chaos test fixtures — FastAPI app harness with injected failures.

Chaos tests simulate infrastructure failures by injecting faults via
environment variables. In CI, these tests are gated by the `--chaos` marker
and run against the full docker-compose stack.
"""

from __future__ import annotations

from typing import Any, AsyncGenerator

import pytest
from fastapi import FastAPI

from app.config import Settings
from app.main import create_app


@pytest.fixture(scope="session")
def chaos_settings() -> Settings:
    """Settings with chaos injection enabled."""
    return Settings(  # type: ignore[call-arg]
        env="test",
        chaos_enabled=True,
        vector_read_dsn="postgresql://fastapi_ro:fastapi_ro_pw@localhost:5432/kendo_users",
        jwt_jwks_url="http://localhost:5000/.well-known/jwks.json",
    )


@pytest.fixture(scope="function")
async def chaos_app(chaos_settings: Settings) -> AsyncGenerator[FastAPI, None]:
    """Create a FastAPI app with chaos injection for testing.

    In CI, this fixture is replaced by a full-stack conftest that
    runs actual docker-compose operations.
    """
    app = create_app(settings=chaos_settings)
    yield app


def pytest_collection_modifyitems(items: list[Any]) -> None:
    """Skip chaos tests unless --chaos flag is passed."""
    import pytest

    if not items:
        return

    # Check if --chaos was passed
    config = items[0].config
    if not config.getoption("--chaos", default=False):
        skip_chaos = pytest.mark.skip(reason="use --chaos flag to run chaos tests")
        for item in items:
            if "chaos" in item.keywords:
                item.add_marker(skip_chaos)


def pytest_addoption(parser: Any) -> None:
    """Register --chaos flag."""
    parser.addoption(
        "--chaos",
        action="store_true",
        default=False,
        help="Run chaos/infrastructure-failure tests",
    )
