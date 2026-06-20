"""Regression test suite — W8 Evaluation & Regression Gate.

Runs DeepEval metrics (faithfulness, answer relevancy, hallucination)
and Ragas metrics (context precision, context recall) against every
dataset in tests/fastapi/eval/datasets/.

Each test case asserts that metric scores do not regress more than 5%
against the stored baseline. The suite is parametrized over datasets
so a single dataset failure fails that dataset's tests only.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest
from deepeval.test_case import LLMTestCase

from .conftest import BASELINE_DIR, DATASET_DIR, load_dataset

# ---------------------------------------------------------------------------
# Ragas metrics
# ---------------------------------------------------------------------------

try:
    from ragas.metrics import context_precision, context_recall

    RAGAS_AVAILABLE = True
except ImportError:
    RAGAS_AVAILABLE = False

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _discover_datasets() -> list[Path]:
    """Discover all JSON dataset files in the datasets directory."""
    if not DATASET_DIR.exists():
        return []
    return sorted(DATASET_DIR.glob("*.json"))


def _load_baseline(dataset_name: str) -> dict[str, float] | None:
    """Load baseline metrics for a dataset, or None if no baseline exists."""
    baseline_path = BASELINE_DIR / f"{dataset_name}.json"
    if not baseline_path.exists():
        return None
    with open(baseline_path) as f:
        return json.load(f)


def _get_regression_threshold() -> float:
    """Return the max allowed regression as a fraction (default 0.05 = 5%)."""
    return 0.05


def _parametrize_datasets() -> list[tuple[str, list[LLMTestCase], dict[str, float] | None]]:
    """Parametrize over available datasets.

    Returns [(dataset_name, test_cases, baseline)] for each dataset found.
    """
    datasets = _discover_datasets()
    if not datasets:
        return [("empty", [], None)]

    params: list[tuple[str, list[LLMTestCase], dict[str, float] | None]] = []
    for dataset_path in datasets:
        name = dataset_path.stem
        test_cases = load_dataset(dataset_path)
        baseline = _load_baseline(name)
        params.append((name, test_cases, baseline))
    return params


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestEvalRegression:
    """Regression test suite — one dataset per parametrized invocation."""

    @pytest.mark.parametrize(
        "dataset_name,test_cases,baseline",
        _parametrize_datasets(),
        ids=lambda p: p[0] if isinstance(p, tuple) and len(p) >= 1 else str(p),
    )
    def test_faithfulness(
        self,
        dataset_name: str,
        test_cases: list[LLMTestCase],
        baseline: dict[str, float] | None,
        metric_factory: dict[str, Any],
    ) -> None:
        """Evaluate faithfulness metric — scores must not regress > 5% vs baseline."""
        if not test_cases:
            pytest.skip(f"Dataset '{dataset_name}' is empty — no test cases to evaluate")

        metric = metric_factory["faithfulness"]
        _run_metric_and_check(metric, "faithfulness", test_cases, dataset_name, baseline)

    @pytest.mark.parametrize(
        "dataset_name,test_cases,baseline",
        _parametrize_datasets(),
        ids=lambda p: p[0] if isinstance(p, tuple) and len(p) >= 1 else str(p),
    )
    def test_answer_relevancy(
        self,
        dataset_name: str,
        test_cases: list[LLMTestCase],
        baseline: dict[str, float] | None,
        metric_factory: dict[str, Any],
    ) -> None:
        """Evaluate answer relevancy metric."""
        if not test_cases:
            pytest.skip(f"Dataset '{dataset_name}' is empty — no test cases to evaluate")

        metric = metric_factory["answer_relevancy"]
        _run_metric_and_check(metric, "answer_relevancy", test_cases, dataset_name, baseline)

    @pytest.mark.parametrize(
        "dataset_name,test_cases,baseline",
        _parametrize_datasets(),
        ids=lambda p: p[0] if isinstance(p, tuple) and len(p) >= 1 else str(p),
    )
    def test_hallucination(
        self,
        dataset_name: str,
        test_cases: list[LLMTestCase],
        baseline: dict[str, float] | None,
        metric_factory: dict[str, Any],
    ) -> None:
        """Evaluate hallucination metric — lower is better (< threshold)."""
        if not test_cases:
            pytest.skip(f"Dataset '{dataset_name}' is empty — no test cases to evaluate")

        metric = metric_factory["hallucination"]
        _run_metric_and_check(metric, "hallucination", test_cases, dataset_name, baseline)

    @pytest.mark.parametrize(
        "dataset_name,test_cases,baseline",
        _parametrize_datasets(),
        ids=lambda p: p[0] if isinstance(p, tuple) and len(p) >= 1 else str(p),
    )
    @pytest.mark.skipif(not RAGAS_AVAILABLE, reason="ragas not installed")
    def test_context_precision(
        self,
        dataset_name: str,
        test_cases: list[LLMTestCase],
        baseline: dict[str, float] | None,
    ) -> None:
        """Evaluate context precision metric (ragas)."""
        if not test_cases:
            pytest.skip(f"Dataset '{dataset_name}' is empty — no test cases to evaluate")
        _run_ragas_metric_and_check(
            context_precision, "context_precision", test_cases, dataset_name, baseline
        )

    @pytest.mark.parametrize(
        "dataset_name,test_cases,baseline",
        _parametrize_datasets(),
        ids=lambda p: p[0] if isinstance(p, tuple) and len(p) >= 1 else str(p),
    )
    @pytest.mark.skipif(not RAGAS_AVAILABLE, reason="ragas not installed")
    def test_context_recall(
        self,
        dataset_name: str,
        test_cases: list[LLMTestCase],
        baseline: dict[str, float] | None,
    ) -> None:
        """Evaluate context recall metric (ragas)."""
        if not test_cases:
            pytest.skip(f"Dataset '{dataset_name}' is empty — no test cases to evaluate")
        _run_ragas_metric_and_check(
            context_recall, "context_recall", test_cases, dataset_name, baseline
        )


# ---------------------------------------------------------------------------
# Metric runner helpers
# ---------------------------------------------------------------------------


def _run_metric_and_check(
    metric: Any,
    metric_name: str,
    test_cases: list[LLMTestCase],
    dataset_name: str,
    baseline: dict[str, float] | None,
) -> None:
    """Run a DeepEval metric and assert against baseline threshold."""
    scores: list[float] = []
    for tc in test_cases:
        metric.measure(tc)
        scores.append(metric.score)

    avg_score = sum(scores) / len(scores) if scores else 0.0

    if baseline and metric_name in baseline:
        baseline_score = baseline[metric_name]
        regression = baseline_score - avg_score
        threshold = _get_regression_threshold()

        assert regression <= threshold, (
            f"Dataset '{dataset_name}': {metric_name} regressed "
            f"from {baseline_score:.3f} to {avg_score:.3f} "
            f"({regression:.1%} regression, threshold {threshold:.1%})"
        )
    else:
        print(
            f"ℹ️  No baseline for '{dataset_name}' / {metric_name} — "
            f"score {avg_score:.3f} (will become baseline on next regenerate)"
        )


def _run_ragas_metric_and_check(
    metric_fn: Any,
    metric_name: str,
    test_cases: list[LLMTestCase],
    dataset_name: str,
    baseline: dict[str, float] | None,
) -> None:
    """Run a Ragas metric and assert against baseline threshold."""
    inputs = [tc.input for tc in test_cases]
    responses = [tc.actual_output for tc in test_cases]
    contexts = [tc.retrieval_context for tc in test_cases]
    references = [tc.expected_output for tc in test_cases]

    scores_list = metric_fn.score(inputs, responses, contexts, references)
    avg_score = (
        sum(scores_list) / len(scores_list) if scores_list and len(scores_list) > 0 else 0.0
    )

    if baseline and metric_name in baseline:
        baseline_score = baseline[metric_name]
        regression = baseline_score - avg_score
        threshold = _get_regression_threshold()

        assert regression <= threshold, (
            f"Dataset '{dataset_name}': {metric_name} regressed "
            f"from {baseline_score:.3f} to {avg_score:.3f} "
            f"({regression:.1%} regression, threshold {threshold:.1%})"
        )
    else:
        print(
            f"ℹ️  No baseline for '{dataset_name}' / {metric_name} — "
            f"score {avg_score:.3f}"
        )</RAW_37596>
    }
  },
  {
    "name": "read_file",
    "kwargs": {
      "file_path": "src/FastAPIService/pyproject.toml"
    }
  }
]