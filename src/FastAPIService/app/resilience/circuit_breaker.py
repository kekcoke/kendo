"""Circuit breaker per external dependency — pybreaker integration.

Provides independent circuit breakers for pgvector and Azure OpenAI
so that one dependency's failure does not affect the other.
"""

from __future__ import annotations

import enum
import logging

import pybreaker

logger = logging.getLogger(__name__)


class CircuitBreakerState(str, enum.Enum):
    """Circuit breaker state enum for observability."""
    CLOSED = "closed"
    OPEN = "open"
    HALF_OPEN = "half_open"


class CircuitBreakerListener(pybreaker.CircuitBreakerListener):
    """Logs state transitions for observability and chaos test assertions."""

    def state_change(self, breaker: pybreaker.CircuitBreaker, old_state: pybreaker.CircuitBreakerState, new_state: pybreaker.CircuitBreakerState) -> None:
        state_names = {
            pybreaker.STATE_CLOSED: "closed",
            pybreaker.STATE_OPEN: "open",
            pybreaker.STATE_HALF_OPEN: "half_open",
        }
        old = state_names.get(old_state, "unknown")
        new = state_names.get(new_state, "unknown")
        logger.warning("Circuit breaker %s: %s → %s", breaker.name, old, new)


def _map_state(state: object) -> CircuitBreakerState:
    """Map pybreaker internal state object to our enum."""
    state_name = type(state).__name__
    if state_name == "CircuitClosedState":
        return CircuitBreakerState.CLOSED
    elif state_name == "CircuitOpenState":
        return CircuitBreakerState.OPEN
    elif state_name == "CircuitHalfOpenState":
        return CircuitBreakerState.HALF_OPEN
    return CircuitBreakerState.CLOSED


def create_pgvector_breaker(fail_max: int = 3, reset_timeout: int = 30) -> pybreaker.CircuitBreaker:
    """Create circuit breaker for pgvector database operations.

    Args:
        fail_max: Consecutive failures before opening (default: 3).
        reset_timeout: Seconds before transitioning to half-open (default: 30).

    Returns:
        pybreaker.CircuitBreaker instance named 'pgvector'.
    """
    listener = CircuitBreakerListener()
    breaker = pybreaker.CircuitBreaker(
        fail_max=fail_max,
        reset_timeout=reset_timeout,
        listeners=[listener],
    )
    breaker.name = "pgvector"
    logger.info("Created pgvector circuit breaker: fail_max=%d, reset_timeout=%ds", fail_max, reset_timeout)
    return breaker


def create_openai_breaker(fail_max: int = 3, reset_timeout: int = 30) -> pybreaker.CircuitBreaker:
    """Create circuit breaker for Azure OpenAI LLM calls.

    Args:
        fail_max: Consecutive failures before opening (default: 3).
        reset_timeout: Seconds before transitioning to half-open (default: 30).

    Returns:
        pybreaker.CircuitBreaker instance named 'azure_openai'.
    """
    listener = CircuitBreakerListener()
    breaker = pybreaker.CircuitBreaker(
        fail_max=fail_max,
        reset_timeout=reset_timeout,
        listeners=[listener],
    )
    breaker.name = "azure_openai"
    logger.info("Created Azure OpenAI circuit breaker: fail_max=%d, reset_timeout=%ds", fail_max, reset_timeout)
    return breaker


def get_breaker_state(breaker: pybreaker.CircuitBreaker | None) -> CircuitBreakerState:
    """Get the current state of a circuit breaker.

    Returns CLOSED if breaker is None (no breaker configured).
    """
    if breaker is None:
        return CircuitBreakerState.CLOSED
    return _map_state(breaker.state)
