# Day 25 Runbook — M5.6 Observability + Chaos + Runbook

> **Consolidated playbook:** This day adopted `ops/runbooks/fastapi_service.md` as the
> single source of truth for FastAPI incident response. See that file for:
> - Azure OpenAI outage recovery
> - pgvector read replica failover
> - FastAPI crash loop diagnosis
> - JWKS rotation procedure
> - Known CI Flakiness documentation
>
> **Day-specific context:**
> - Milestone: M5.6 (Observability + chaos + runbook)
> - Components delivered: rate limiter middleware, chaos test suite, runbook, CI integration
> - No new .NET services or endpoints — Python-only changes in `src/FastAPIService/`
> - Rate limiter uses in-memory TokenBucket (no Redis persistence — deferred for v2)
> - Chaos tests gated by `--chaos` marker; skip in local dev, run in CI docker-compose stack

## Rollback

```bash
# Revert M5.6 changes:
git revert --no-commit a11f12b   # Phase 5 state commit
git revert --no-commit 82b87c4   # Squash-merge PR #31 (rate limiter + chaos + runbook)
git commit -m "revert: Day 25 M5.6"
git push origin develop
```
