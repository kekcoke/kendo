"""Eval test fixtures — DeepEval metric configuration and dataset loading.

Provides:
- eval_settings: fixture for eval configuration
- metric_factory: creates DeepEval metric instances
- load_eval_dataset: loads golden dataset from tests/fastapi/eval/datasets/
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

from deepeval.metrics import AnswerRelevancyMetric, FaithfulnessMetric, HallucinationMetric
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
        "regression_threshold": 0.05,  # 5% max regression vs baseline
        "datasets_dir": str(DATASET_DIR),
        "baselines_dir": str(BASELINE_DIR),
    }


# ---------------------------------------------------------------------------
# Metric factory
# ---------------------------------------------------------------------------


@pytest.fixture(scope="session")
def metric_factory() -> dict[str, Any]:
    """Create DeepEval metric instances for the W8 eval suite.

    Returns a dict of metric_name -> metric_instance so tests can
    reference them by name. Metrics wrap deepeval's built-in scorers.
    """
    return {
        "faithfulness": FaithfulnessMetric(threshold=0.7, model="gpt-4o-mini"),
        "answer_relevancy": AnswerRelevancyMetric(threshold=0.7, model="gpt-4o-mini"),
        "hallucination": HallucinationMetric(threshold=0.3, model="gpt-4o-mini"),
    }


# ---------------------------------------------------------------------------
# Dataset loading
# ---------------------------------------------------------------------------


def discover_datasets() -> list[Path]:
    """Discover all JSON dataset files in the datasets directory.

    Each dataset file is a JSON array of test cases with fields:
      - input: str (the user query or event text)
      - actual_output: str (the LLM response)
      - expected_output: str (the golden / ideal response)
      - retrieval_context: list[str] (contexts retrieved by the RAG pipeline)
    """
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
