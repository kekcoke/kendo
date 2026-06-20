#!/usr/bin/env python3
"""Regenerate baseline metric snapshots for the W8 eval suite.

Usage:
    python -m tests.fastapi.eval.regenerate_baseline

This script runs the full eval suite against the current datasets and
persists metric scores as baseline snapshots in tests/fastapi/eval/baseline/.
"""

from __future__ import annotations

import json
from pathlib import Path

from deepeval.metrics import AnswerRelevancyMetric, FaithfulnessMetric, HallucinationMetric

from .conftest import BASELINE_DIR, DATASET_DIR, load_dataset

try:
    from ragas.metrics import context_precision, context_recall
    RAGAS_AVAILABLE = True
except ImportError:
    RAGAS_AVAILABLE = False


def _discover_datasets() -> list[Path]:
    if not DATASET_DIR.exists():
        return []
    return sorted(DATASET_DIR.glob("*.json"))


def regenerate_baselines() -> None:
    datasets = _discover_datasets()
    if not datasets:
        print("WARNING: No datasets found.")
        return

    metrics = {
        "faithfulness": FaithfulnessMetric(threshold=0.7, model="gpt-4o-mini"),
        "answer_relevancy": AnswerRelevancyMetric(threshold=0.7, model="gpt-4o-mini"),
        "hallucination": HallucinationMetric(threshold=0.3, model="gpt-4o-mini"),
    }

    BASELINE_DIR.mkdir(parents=True, exist_ok=True)

    for dataset_path in datasets:
        dataset_name = dataset_path.stem
        test_cases = load_dataset(dataset_path)
        if not test_cases:
            print("WARNING: Dataset '{}' is empty — skipping".format(dataset_name))
            continue

        print("Evaluating dataset: {} ({} test cases)".format(dataset_name, len(test_cases)))
        baseline: dict[str, float] = {}

        for metric_name, metric in metrics.items():
            scores: list[float] = []
            for tc in test_cases:
                metric.measure(tc)
                scores.append(metric.score)
            avg_score = sum(scores) / len(scores) if scores else 0.0
            baseline[metric_name] = round(avg_score, 4)
            print("   {}: {:.4f}".format(metric_name, baseline[metric_name]))

        if RAGAS_AVAILABLE:
            inputs = [tc.input for tc in test_cases]
            responses = [tc.actual_output for tc in test_cases]
            contexts = [tc.retrieval_context for tc in test_cases]
            references = [tc.expected_output for tc in test_cases]
            try:
                precision_scores = context_precision.score(inputs, responses, contexts, references)
                baseline["context_precision"] = round(
                    sum(precision_scores) / len(precision_scores) if precision_scores else 0.0, 4
                )
                print("   context_precision: {:.4f}".format(baseline["context_precision"]))
            except Exception as e:
                print("   context_precision: SKIPPED ({})".format(e))
            try:
                recall_scores = context_recall.score(inputs, responses, contexts, references)
                baseline["context_recall"] = round(
                    sum(recall_scores) / len(recall_scores) if recall_scores else 0.0, 4
                )
                print("   context_recall: {:.4f}".format(baseline["context_recall"]))
            except Exception as e:
                print("   context_recall: SKIPPED ({})".format(e))

        baseline_path = BASELINE_DIR / "{}.json".format(dataset_name)
        with open(baseline_path, "w") as f:
            json.dump(baseline, f, indent=2)
        print("   Baseline saved to {}".format(baseline_path))

    print("Baseline regeneration complete.")


if __name__ == "__main__":
    regenerate_baselines()