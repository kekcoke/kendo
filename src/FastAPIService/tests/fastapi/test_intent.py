"""Unit tests for W4 User Intent Classification (M5.10).

Tests cover:
- IntentClassifier: mock classifier accuracy (>= 95%), confidence bounds
- classify endpoint: input validation, response shape
- Latency benchmark: p95 <= 80ms (measured with timed runs)
"""

from __future__ import annotations

import asyncio
import time
from typing import Any
from unittest.mock import patch

import pytest

from app.config import Settings
from app.rag.intent_classifier import IntentClassifier


def _make_settings(env: str = "test") -> Settings:
    """Create test settings with default test values."""
    return Settings(  # type: ignore[call-arg]
        env=env,
        embedding_model="BGE-large-en-v1.5",
        vector_read_dsn="",
        intent_classifier_model="gpt-4o-mini",
    )


def _make_classifier(settings: Settings | None = None, env: str | None = None) -> IntentClassifier:
    """Create an IntentClassifier for testing with default test settings."""
    if settings is None:
        if env:
            settings = _make_settings(env=env)
        else:
            settings = _make_settings()
    return IntentClassifier(settings)


# =============================================================================
# IntentClassifier - mock classifier accuracy
# =============================================================================

class TestMockClassifierAccuracy:
    """Tests for the deterministic mock classifier's accuracy.

    The mock classifier uses keyword matching. For production, the LLM
    classifier is validated separately (accuracy gate >= 95%).
    """

    TEST_CASES: list[tuple[str, str, str]] = [
        # events.ingest
        ("I want to create a new event for next Friday", "events.ingest", "create event"),
        ("Schedule a team meeting for 10 people", "events.ingest", "schedule meeting"),
        ("Plan a birthday party with 20 guests", "events.ingest", "plan party"),
        ("Add event to my calendar for tomorrow at 3pm", "events.ingest", "add event"),
        ("Organize a conference room booking", "events.ingest", "organize event"),
        ("Create a recurring weekly standup", "events.ingest", "create recurring"),

        # events.validate
        ("Please validate this event for scheduling conflicts", "events.validate", "validate event"),
        ("Check if my meeting conflicts with other events", "events.validate", "check conflicts"),
        ("Verify the event details are correct", "events.validate", "verify event"),
        ("Confirm the date and time are available", "events.validate", "confirm availability"),

        # support.create
        ("I need help with my account", "support.create", "account help"),
        ("Can someone support me with the event setup?", "support.create", "event support"),
        ("I am having an issue with the login", "support.create", "login issue"),
        ("I need help resetting my password", "support.create", "password help"),

        # assistant.ask
        ("What is the platform event capacity limit?", "assistant.ask", "platform question"),
        ("How to create a recurring event?", "assistant.ask", "how-to question"),
        ("Tell me about the available features", "assistant.ask", "features question"),
        ("Explain the difference between event types", "assistant.ask", "explain question"),
    ]

    @pytest.mark.parametrize("body,expected_route,label", TEST_CASES)
    def test_mock_classify_accuracy(self, body: str, expected_route: str, label: str) -> None:
        """Mock classifier correctly classifies intent for {label}."""
        classifier = _make_classifier()
        available_routes = ["events.ingest", "events.validate", "support.create", "assistant.ask"]

        result = classifier._mock_classify(body, available_routes)

        assert result["route"] == expected_route, (
            f"Failed for '{label}': expected {expected_route}, got {result['route']}"
        )
        assert 0.0 <= result["confidence"] <= 1.0, (
            f"Confidence {result['confidence']} out of [0, 1] range"
        )

    def test_mock_classify_fallback(self) -> None:
        """Ambiguous text returns fallback with low confidence."""
        classifier = _make_classifier()
        available_routes = ["events.ingest", "events.validate", "support.create", "assistant.ask"]

        result = classifier._mock_classify(
            "The weather is nice today", available_routes
        )

        assert result["route"] == "fallback"
        assert result["confidence"] <= 0.3

    def test_mock_classify_custom_routes(self) -> None:
        """Classifier works with arbitrary route lists."""
        classifier = _make_classifier()
        available_routes = ["custom.route1", "custom.route2"]

        result = classifier._mock_classify(
            "I need help with custom route", available_routes
        )

        assert result["route"] == "fallback"

    def test_confidence_bounds(self) -> None:
        """Confidence is always clamped to [0.0, 1.0]."""
        classifier = _make_classifier()
        available_routes = ["events.ingest", "events.validate", "support.create", "assistant.ask"]

        result = classifier._mock_classify(
            "create schedule plan organize add event make new", available_routes
        )

        assert 0.0 <= result["confidence"] <= 1.0

    def test_overall_accuracy_gate(self) -> None:
        """Mock classifier achieves >= 95% accuracy on the test set."""
        classifier = _make_classifier()
        available_routes = ["events.ingest", "events.validate", "support.create", "assistant.ask"]

        correct = 0
        total = len(self.TEST_CASES)

        for body, expected_route, _label in self.TEST_CASES:
            result = classifier._mock_classify(body, available_routes)
            if result["route"] == expected_route:
                correct += 1

        accuracy = correct / total
        assert accuracy >= 0.95, (
            f"Mock classifier accuracy: {accuracy:.2%} ({correct}/{total}) - expected >= 95%"
        )


# =============================================================================
# IntentClassifier - classify method
# =============================================================================

class TestClassifyMethod:
    """Tests for the classify method that wraps _llm_classify."""

    @pytest.mark.asyncio
    async def test_classify_returns_expected_shape(self) -> None:
        """classify() returns dict with route and confidence."""
        classifier = _make_classifier()
        result = await classifier.classify(
            body="Create a new event for next week",
            user_id="user-abc",
            available_routes=["events.ingest", "events.validate", "support.create", "assistant.ask"],
        )

        assert "route" in result
        assert "confidence" in result
        assert isinstance(result["route"], str)
        assert isinstance(result["confidence"], float)
        assert 0.0 <= result["confidence"] <= 1.0

    @pytest.mark.asyncio
    async def test_classify_correct_routing(self) -> None:
        """classify() correctly routes known intents."""
        classifier = _make_classifier()
        routes = ["events.ingest", "events.validate", "support.create", "assistant.ask"]

        test_cases = [
            ("I need help with the platform", "support.create"),
            ("Schedule a team event for 15 people", "events.ingest"),
            ("Validate the event for date conflicts", "events.validate"),
            ("How to set up a new user account?", "assistant.ask"),
        ]

        for body, expected_route in test_cases:
            result = await classifier.classify(
                body=body,
                user_id="user-abc",
                available_routes=routes,
            )
            assert result["route"] == expected_route, (
                f"Expected {expected_route}, got {result['route']} for: {body}"
            )

    @pytest.mark.asyncio
    async def test_classify_with_user_id(self) -> None:
        """user_id is accepted without error."""
        classifier = _make_classifier()
        result = await classifier.classify(
            body="Create an event",
            user_id="user-xyz-789",
            available_routes=["events.ingest", "events.validate"],
        )

        assert result["route"] == "events.ingest"
        assert 0.0 <= result["confidence"] <= 1.0

    @pytest.mark.asyncio
    async def test_classify_empty_body(self) -> None:
        """Empty body should still return a fallback route (no crash)."""
        classifier = _make_classifier()
        result = await classifier.classify(
            body="",
            user_id=None,
            available_routes=["events.ingest", "events.validate", "support.create", "assistant.ask"],
        )

        assert result["route"] == "fallback"
        assert result["confidence"] <= 0.3


# =============================================================================
# IntentClassifier - LLM path (mock)
# =============================================================================

class TestLLMClassify:
    """Tests for the LLM classification path (mocked)."""

    @pytest.mark.asyncio
    async def test_llm_classify_falls_back_in_test_env(self) -> None:
        """In test env, _llm_classify should fall back to mock."""
        classifier = _make_classifier(env="test")
        result = await classifier._llm_classify(
            "Create an event",
            ["events.ingest", "events.validate"],
        )

        assert result["route"] == "events.ingest"

    @pytest.mark.asyncio
    async def test_llm_classify_with_configured_llm(self) -> None:
        """With LLM configured, should attempt real call then fall back on failure."""
        settings = _make_settings(env="production")
        settings.llm_endpoint = "http://localhost:9999/v1"
        settings.llm_api_key = "test-key"

        classifier = _make_classifier(settings)

        result = await classifier._llm_classify(
            "Create an event",
            ["events.ingest", "events.validate"],
        )

        assert result["route"] in ["events.ingest", "fallback"]
        assert 0.0 <= result["confidence"] <= 1.0

    @pytest.mark.asyncio
    async def test_llm_classify_mocked_response(self) -> None:
        """Verify the LLM response parsing pipeline with a mocked client."""
        classifier = _make_classifier(env="production")

        with patch.object(classifier, "_call_llm", return_value={
            "route": "events.ingest",
            "confidence": 0.95,
        }):
            classifier.settings.llm_endpoint = "http://mocked:8000"
            classifier.settings.llm_api_key = "mocked-key"

            result = await classifier._llm_classify(
                "I want to schedule a meeting",
                ["events.ingest", "events.validate"],
            )

            assert result["route"] == "events.ingest"
            assert result["confidence"] == 0.95


# =============================================================================
# IntentClassifier - prompt building
# =============================================================================

class TestPromptBuilding:
    """Tests for the classification prompt builder."""

    def test_build_prompt_includes_routes(self) -> None:
        """Prompt should list all available routes."""
        classifier = _make_classifier()
        routes = ["events.ingest", "events.validate", "support.create"]
        prompt = classifier._build_classification_prompt(routes)

        for route in routes:
            assert route in prompt, f"Route {route} should be in prompt"

    def test_build_prompt_includes_format(self) -> None:
        """Prompt should mention JSON output format."""
        classifier = _make_classifier()
        prompt = classifier._build_classification_prompt(["events.ingest"])

        assert "JSON" in prompt or "json" in prompt
        assert "route" in prompt
        assert "confidence" in prompt


# =============================================================================
# Latency benchmark: p95 <= 80ms
# =============================================================================

class TestLatencyBenchmark:
    """Benchmark tests for classification latency.

    Measured using the mock classifier (no real LLM call).
    p95 should be well under 80ms since the mock classifier is purely
    local keyword matching with no network I/O.
    """

    @pytest.mark.parametrize("num_runs", [100])
    def test_mock_classifier_latency_p95(self, num_runs: int) -> None:
        """Mock classifier p95 latency <= 80ms."""
        classifier = _make_classifier()
        available_routes = ["events.ingest", "events.validate", "support.create", "assistant.ask"]

        body = "I want to create a new event for next Friday at 3pm for 20 people"

        latencies: list[float] = []
        for _ in range(num_runs):
            start = time.perf_counter()
            classifier._mock_classify(body, available_routes)
            elapsed = (time.perf_counter() - start) * 1000
            latencies.append(elapsed)

        latencies.sort()
        p95_idx = int(num_runs * 0.95)
        p95_latency = latencies[p95_idx]

        print(f"\n[p95 latency] {p95_latency:.3f}ms (target: <= 80ms)")
        print(f"[min/avg/max] {min(latencies):.3f} / {sum(latencies)/num_runs:.3f} / {max(latencies):.3f} ms")

        assert p95_latency <= 80.0, (
            f"p95 latency {p95_latency:.3f}ms exceeds 80ms threshold"
        )

    @pytest.mark.parametrize("num_runs", [50])
    def test_end_to_end_latency_p95(self, num_runs: int) -> None:
        """End-to-end classify() p95 latency <= 80ms (no LLM call)."""
        classifier = _make_classifier()

        async def run_classify() -> float:
            start = time.perf_counter()
            await classifier.classify(
                body="Schedule a meeting for next Tuesday",
                user_id="user-test",
                available_routes=["events.ingest", "events.validate"],
            )
            return (time.perf_counter() - start) * 1000

        async def run_all() -> list[float]:
            results = await asyncio.gather(*[run_classify() for _ in range(num_runs)])
            return list(results)

        latencies = asyncio.run(run_all())
        latencies.sort()
        p95_idx = int(num_runs * 0.95)
        p95_latency = latencies[p95_idx]

        print(f"\n[end-to-end p95 latency] {p95_latency:.3f}ms (target: <= 80ms)")

        assert p95_latency <= 80.0, (
            f"End-to-end p95 latency {p95_latency:.3f}ms exceeds 80ms threshold"
        )


# =============================================================================
# Full pathway: endpoint contract test (no HTTP)
# =============================================================================

class TestEndpointContract:
    """Tests the endpoint's contract shape using direct classifier calls."""

    @pytest.mark.asyncio
    async def test_full_classification_pipeline(self) -> None:
        """Full pipeline returns expected contract shape."""
        classifier = _make_classifier()
        routes = ["events.ingest", "events.validate", "support.create", "assistant.ask"]

        test_inputs: list[dict[str, Any]] = [
            {
                "body": "Create a new event for 50 people at the convention center",
                "user_id": "user-001",
                "available_routes": routes,
                "expected_route": "events.ingest",
            },
            {
                "body": "Please check if this event conflicts with my schedule",
                "user_id": "user-002",
                "available_routes": routes,
                "expected_route": "events.validate",
            },
            {
                "body": "I need help with my account settings",
                "user_id": "user-003",
                "available_routes": routes,
                "expected_route": "support.create",
            },
            {
                "body": "Can you explain how to invite guests to an event?",
                "user_id": "user-004",
                "available_routes": routes,
                "expected_route": "assistant.ask",
            },
        ]

        for case in test_inputs:
            result = await classifier.classify(
                body=case["body"],
                user_id=case["user_id"],
                available_routes=case["available_routes"],
            )

            assert result["route"] == case["expected_route"], (
                f"Expected route {case['expected_route']}, got {result['route']} "
                f"for body: {case['body']}"
            )
            assert isinstance(result["confidence"], float)
            assert 0.0 <= result["confidence"] <= 1.0
