"""Unit tests for pybreaker circuit breaker integration."""

from __future__ import annotations

import pybreaker
import pytest

from app.resilience.circuit_breaker import (
    CircuitBreakerState,
    create_pgvector_breaker,
    create_openai_breaker,
    get_breaker_state,
)


class TestCircuitBreakerCreation:
    """Tests for circuit breaker factory functions."""

    def test_create_pgvector_breaker(self) -> None:
        """Create pgvector breaker with default config."""
        breaker = create_pgvector_breaker(fail_max=3, reset_timeout=30)
        assert breaker.fail_max == 3
        assert breaker.reset_timeout == 30
        assert breaker.name == "pgvector"

    def test_create_openai_breaker(self) -> None:
        """Create Azure OpenAI breaker with default config."""
        breaker = create_openai_breaker(fail_max=3, reset_timeout=30)
        assert breaker.fail_max == 3
        assert breaker.reset_timeout == 30
        assert breaker.name == "azure_openai"

    def test_breakers_independent(self) -> None:
        """Two breakers are independent instances."""
        pgv = create_pgvector_breaker(3, 30)
        oai = create_openai_breaker(5, 60)
        assert pgv is not oai
        assert pgv.fail_max != oai.fail_max
        assert pgv.reset_timeout != oai.reset_timeout


class TestCircuitBreakerStateTransitions:
    """Tests for circuit breaker state transitions."""

    def test_initial_state_closed(self) -> None:
        """Breaker starts in CLOSED state."""
        breaker = create_pgvector_breaker(3, 30)
        state = get_breaker_state(breaker)
        assert state == CircuitBreakerState.CLOSED

    def test_opens_after_n_failures(self) -> None:
        """Breaker transitions to OPEN after fail_max consecutive failures."""
        breaker = create_pgvector_breaker(fail_max=3, reset_timeout=30)

        for _ in range(3):
            try:
                breaker.call(lambda: (_ for _ in ()).throw(Exception("DB error")))
            except (Exception, pybreaker.CircuitBreakerError):
                pass

        state = get_breaker_state(breaker)
        assert state == CircuitBreakerState.OPEN

    def test_closed_before_fail_max(self) -> None:
        """Breaker stays CLOSED before reaching fail_max."""
        breaker = create_pgvector_breaker(fail_max=3, reset_timeout=30)

        for _ in range(2):
            try:
                breaker.call(lambda: (_ for _ in ()).throw(Exception("DB error")))
            except Exception:
                pass

        state = get_breaker_state(breaker)
        assert state == CircuitBreakerState.CLOSED

    def test_success_resets_failure_count(self) -> None:
        """A successful call resets the failure count."""
        breaker = create_pgvector_breaker(fail_max=3, reset_timeout=30)

        # 2 failures
        for _ in range(2):
            try:
                breaker.call(lambda: (_ for _ in ()).throw(Exception("DB error")))
            except Exception:
                pass

        # 1 success — resets counter
        breaker.call(lambda: "ok")

        # 2 more failures — should NOT open because counter was reset
        for _ in range(2):
            try:
                breaker.call(lambda: (_ for _ in ()).throw(Exception("DB error")))
            except Exception:
                pass

        state = get_breaker_state(breaker)
        assert state == CircuitBreakerState.CLOSED, (
            "Success should reset failure count, requiring 3 more failures to open"
        )

    def test_breaker_state_none_breaker(self) -> None:
        """get_breaker_state returns CLOSED when breaker is None."""
        state = get_breaker_state(None)
        assert state == CircuitBreakerState.CLOSED

    def test_openai_breaker_opens_independently(self) -> None:
        """OpenAI breaker opens independently from pgvector breaker."""
        pgv = create_pgvector_breaker(3, 30)
        oai = create_openai_breaker(3, 30)

        # Trip only the pgvector breaker
        for _ in range(3):
            try:
                pgv.call(lambda: (_ for _ in ()).throw(Exception("DB error")))
            except (Exception, pybreaker.CircuitBreakerError):
                pass

        assert get_breaker_state(pgv) == CircuitBreakerState.OPEN
        assert get_breaker_state(oai) == CircuitBreakerState.CLOSED, (
            "OpenAI breaker should remain CLOSED when pgvector breaker trips"
        )
