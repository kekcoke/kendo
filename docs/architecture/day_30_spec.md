# Architecture Spec — Day 29 — W8: Evaluation & Regression Gate

> **Milestone:** M5.14 — W8 Evaluation & Regression Gate (P0)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Infrastructure spec (Case C — no pre-authored spec; adopts `fastapi_rag_service_spec.md §W8` by reference)  
> **Depends on:** M5.1–M5.6 foundation (all live on `develop`), no workload datasets required (starts empty)

---

## Milestone Scope

- **What:** Implement the W8 Evaluation & Regression Gate — a `pytest` suite using **DeepEval** (faithfulness, answer relevancy, hallucination) and **Ragas** (context precision/recall) that runs in CI on every PR touching `src/FastAPIService/`. Starts with an **empty dataset** (a 0% baseline). Build fails on any metric regression > 5% vs. the `main` baseline.
- **Maps to:** `fastapi_rag_service_spec.md §W8 — Evaluation & Regression Gate for LLM Outputs` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - Individual workload eval datasets (W1 dataset wired on Day 30 during W1 merge)
  - Nightly `develop` runs (added after first dataset lands)
  - W2–W7 workload implementation (handled in subsequent days)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Test | `tests/fastapi/eval/` | New — eval test directory |
| Test | `tests/fastapi/eval/conftest.py` | New — eval fixtures, metric configuration |
| Test | `tests/fastapi/eval/datasets/` | New — golden dataset directory (starts empty, `.gitkeep`) |
| Test | `tests/fastapi/eval/baseline/` | New — baseline metric snapshots (starts empty) |
| Test | `tests/fastapi/eval/test_regression.py` | New — regression test suite with DeepEval + Ragas metrics |
| App | `pyproject.toml` | Modify — add `deepeval`, `ragas` dependencies |
| CI | `.github/workflows/ci.yml` | Modify — add `eval-gate` job |
| Ops | `ops/runbooks/day_29_runbook.md` | New — W8 eval gate runbook |

---

## Implementation Plan (Commit Units)

### Unit 1 — Eval framework scaffold + empty dataset

**Files:**
- `src/FastAPIService/tests/fastapi/eval/__init__.py` — empty
- `src/FastAPIService/tests/fastapi/eval/conftest.py` — eval settings, DeepEval/Ragas metric factory fixtures, dataset loader stub
- `src/FastAPIService/tests/fastapi/eval/datasets/.gitkeep` — empty dataset dir
- `src/FastAPIService/tests/fastapi/eval/baseline/.gitkeep` — empty baseline dir
- `src/FastAPIService/tests/fastapi/eval/test_regression.py` — regression test skeleton with metric placeholders, parametrized over datasets
- `src/FastAPIService/pyproject.toml` — add `deepeval`, `ragas`

**Gate command:** `pytest tests/fastapi/eval/ -v` — must exit 0 (empty dataset, all metrics report "no data" gracefully).

**Commit message:**
```
feat(eval): add W8 eval gate scaffold — DeepEval + Ragas, empty dataset

Creates tests/fastapi/eval/ with:
- conftest.py: eval settings, metric factory (faithfulness, answer_relevancy,
  context_precision, context_recall)
- test_regression.py: regression test suite with parametrized dataset loading
- datasets/: golden dataset directory (empty, .gitkeep)
- baseline/: baseline metric snapshots (empty, .gitkeep)
- pyproject.toml: add deepeval>=1.0.0, ragas>=0.2.0

Empty dataset = 0% baseline. Datasets added by each workload on merge.
Gate command: pytest tests/fastapi/eval/ -v

Day 29 — M5.14 Unit 1 of 3 | Milestone: M5.14 — W8 Evaluation & Regression Gate
Lint: clean
```

### Unit 2 — CI eval-gate job

**Files:**
- `.github/workflows/ci.yml` — add `eval-gate` job:
  - Trigger: `paths: ['src/FastAPIService/**', 'tests/fastapi/eval/**']`
  - Steps: setup Python, install deps, `pytest tests/fastapi/eval/ --junitxml=eval-report.xml`
  - Upload artifact: `eval-report.xml`
  - Fails build on > 5% regression vs baseline (DeepEval metric thresholds in test config)

**Gate command:** `cat .github/workflows/ci.yml | grep -A30 "eval-gate"` — CI job definition present.

**Commit message:**
```
ci(eval): add eval-gate CI job — regression gate for FastAPI changes

New eval-gate job runs pytest tests/fastapi/eval/ on every PR touching
src/FastAPIService/. Fails build on >5% metric regression. Results uploaded
as CI artifact.

Day 29 — M5.14 Unit 2 of 3 | Milestone: M5.14 — W8 Evaluation & Regression Gate
```

### Unit 3 — Baseline management + runbook

**Files:**
- `src/FastAPIService/tests/fastapi/eval/regenerate_baseline.py` — script to re-run eval suite and store metric snapshots as baseline
- `ops/runbooks/day_29_runbook.md` — deployment, verification, adding datasets, regenerating baselines
- `docs/architecture/day_29_review_report.md` — Phase 4b review artifact

**Gate command:** `python -m pytest tests/fastapi/eval/ -v --co` — quick-check passes, runbook readable.

**Commit message:**
```
chore(eval): add baseline management script + W8 runbook

- regenerate_baseline.py: re-runs eval suite, persists metric snapshots
- ops/runbooks/day_29_runbook.md: deployment, CI gate verification,
  adding workload datasets, baseline regeneration
- day_29_review_report.md: Phase 4b gate check

Day 29 — M5.14 Unit 3 of 3 | Milestone: M5.14 — W8 Evaluation & Regression Gate
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W8 acceptance gate`:

| # | Criterion | How Verified |
|---|-----------|-------------|
| 1 | `pytest tests/fastapi/eval/` runs in CI on every PR touching `src/FastAPIService/` | CI job exists, trigger paths correct |
| 2 | Build fails on any metric regression > 5% vs. `main` baseline | DeepEval metric thresholds in test assertions |
| 3 | Eval dataset is versioned in git (regressions reproducible) | `tests/fastapi/eval/datasets/` git-tracked |
| 4 | Suite completes in ≤ 10 minutes in CI | Empty dataset = < 1m; realistic limit with `pytest --co` quick-check |
| 5 | Results uploaded as CI artifact | `actions/upload-artifact` on `eval-report.xml` |
| 6 | Baseline snapshots storable and regenerable | `regenerate_baseline.py` script provided |

---

## Resilience Mandate

The eval gate itself has no direct resilience requirements (it's a CI-only tool). However:

- **Graceful degradation:** Empty dataset = all metrics report "no data" without erroring
- **Independent from production:** Eval suite runs standalone, no FastAPI app instance required
- **Fail-safe:** DeepEval `assert_test()` raises `AssertionError` on metric failure, which pytest converts to a test failure → CI build failure

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W8` — design-spec contract
- M5.1–M5.6 foundation (FastAPI scaffold, pgvector, LangChain, resilience) — all live on `develop`
- No workload datasets required — starts empty per spec