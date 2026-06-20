"""Eval test fixtures — DeepEval metric configuration and dataset loading.

Provides:
- eval_settings: fixture for eval configuration
- metrics_available: bool fixture — True if OPENAI_API_KEY is set
- metric_factory: creates DeepEval metric instances (or None if unavailable)
- load_eval_dataset: loads golden dataset from tests/fastapi/eval/datasets/
"""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

import pytest

from deepeval.test_case import LLMTestCase

# ---------------------------------------------------------------------------
# Eval settings
# ---------------------------------------------------------------------------

EVAL_DIR = Path(__file__).parent
DATASET_DIR = EVAL_DIR / "datasets"
BASELINE_DIR = EVAL_DIR / "baseline"


@pytest.fixture(scope="session")
def eval_settings() -> dict[str, Any]:
    """Shared eval configuration."""
    return {
        "regression_threshold": 0.05,
        "datasets_dir": str(DATASET_DIR),
        "baselines_dir": str(BASELINE_DIR),
    }


# ---------------------------------------------------------------------------
# Metric availability
# ---------------------------------------------------------------------------


@pytest.fixture(scope="session")
def metrics_available() -> bool:
    """Check if LLM evaluation is available (API key configured).

    DeepEval metrics require OPENAI_API_KEY. When unavailable, tests
    skip gracefully rather than hard-failing.
    """
    return bool(os.environ.get("OPENAI_API_KEY"))


# ---------------------------------------------------------------------------
# Metric factory
# ---------------------------------------------------------------------------


@pytest.fixture(scope="session")
def metric_factory(metrics_available: bool) -> dict[str, Any]:
    """Create DeepEval metric instances for the W8 eval suite.

    If OPENAI_API_KEY is not configured, returns an empty dict so
    consuming tests can skip gracefully.
    """
    if not metrics_available:
        return {}

    from deepeval.metrics import AnswerRelevancyMetric, FaithfulnessMetric, HallucinationMetric

    return {
        "faithfulness": FaithfulnessMetric(threshold=0.7, model="gpt-4o-mini"),
        "answer_relevancy": AnswerRelevancyMetric(threshold=0.7, model="gpt-4o-mini"),
        "hallucination": HallucinationMetric(threshold=0.3, model="gpt-4o-mini"),
    }


# ---------------------------------------------------------------------------
# Dataset loading
# ---------------------------------------------------------------------------


def discover_datasets() -> list[Path]:
    """Discover all JSON dataset files in the datasets directory."""
    if not DATASET_DIR.exists():
        return []
    return sorted(DATASET_DIR.glob("*.json"))


def load_dataset(dataset_path: Path) -> list[LLMTestCase]:
    """Load a JSON dataset file into DeepEval LLMTestCase objects."""
    with open(dataset_path) as f:
        raw_cases = json.load(f)

    test_cases = []
    for case in raw_cases:
        test_cases.append(
            LLMTestCase(
                input=case.get("input", ""),
                actual_output=case.get("actual_output", ""),
                expected_output=case.get("expected_output", ""),
                retrieval_context=case.get("retrieval_context", []),
            )
        )
    return test_cases


@pytest.fixture(scope="session")
def dataset_registry() -> dict[str, list[LLMTestCase]]:
    """Load all available datasets into a registry dict.

    Returns {dataset_name: [LLMTestCase, ...]}. Empty dict when no
    datasets exist (initial state).
    """
    registry: dict[str, list[LLMTestCase]] = {}
    for dataset_path in discover_datasets():
        name = dataset_path.stem
        registry[name] = load_dataset(dataset_path)
    return registry