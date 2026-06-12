# Kendo Orchestration Loop — Master Rules
> **Canonical reference. Agents, skills, and the Entrypoint all defer to this document.**  
> Do NOT interpret this file as a prompt. It is a specification.  
> Last updated: 2026-06-12

---

## 1. Variable Schema

| Variable | Type | Resolved By | Description |
|---|---|---|---|
| `{{DAY_NUMBER}}` | integer | Phase 0 / `current_state` | Current curriculum day (1-indexed) |
| `{{SLUG}}` | string | Phase 1 (Architect) | kebab-case deliverable summary (e.g. `infra-foundation`) |
| `{{TASK_LIST}}` | ordered list | Phase 1 spec `## Implementation Plan` | Ordered commit units for Developer+QA |
| `{{TEST_COMMAND}}` | string | DevOps config | Full test suite command (e.g. `dotnet test /p:CollectCoverage=true`) |
| `{{LINT_COMMAND}}` | string | DevOps config | Linter command (e.g. `dotnet format --verify-no-changes`) |
| `{{BRANCH_BASE}}` | string | `current_state` | Branch to fork from (default: `develop`) |
| `{{PARSER_OUTPUT}}` | file ref | Phase 1 output | `docs/architecture/day_{{DAY_NUMBER}}_spec.md` |
| `{{CODER_OUTPUT}}` | file ref | Phase 2 output | Commit log table + annotated integration points |
| `{{phase_plan}}` | string | Entrypoint trigger | Platform roadmap phase in scope (`01`, `02`, `03`) |

---

## 2. Prerequisites

Verify before every session. Failure blocks Phase 4b only — all prior phases can still run.

- [ ] `git remote get-url origin` returns a valid GitHub URL
- [ ] `gh auth status` returns authenticated

If either check fails: surface the issue and document it in `current_state.md § Carry-Forward Items` before proceeding past Phase 1.

---

## 3. Phase Definitions

### Phase 0 — State Initialization
**Agent:** Orchestrator (no delegation)  
**Trigger:** Start of every new day session  
**Inputs:** `.ai/current_state.md`, `docs/platform_roadmap.md §{{phase_plan}}`

**Actions (in order):**
1. Read `current_state.md` in full. Load: `## Completed Days`, `## Active Dependency Map`, `## Carry-Forward Items`.
2. Check `incomplete_tasks` — if non-empty → **halt**. Surface each blocker. Do not advance to Phase 1.
3. Confirm the current day's task does not conflict with any dependency in `## Active Dependency Map`.
4. Resolve `{{DAY_NUMBER}}`, `{{BRANCH_BASE}}`, and `{{phase_plan}}` for this session.
5. Update `current_state.md`: set `current_phase: 0`.

**Gate to Phase 0b / Phase 1 — all must be true:**
- [ ] `incomplete_tasks` is empty
- [ ] No dependency conflicts detected
- [ ] `{{DAY_NUMBER}}`, `{{BRANCH_BASE}}`, `{{phase_plan}}` resolved

---

### Phase 0b — Branch Bootstrap *(first session only)*
**Condition:** Run ONLY if `git log --oneline 2>/dev/null | head -1` returns empty (no commits yet).  
**After first session:** permanently skipped.

```bash
# 1. Commit orchestration scaffolding to main
git checkout -b main
git add .ai/ docs/ ops/ scripts/ .gitignore README.md
git commit -m "chore: initialize repo scaffolding and orchestration templates"

# 2. Cut develop from main
git checkout -b develop
git push origin main develop
```

---

### Phase 1 — Architecture & Contract Design
**Agent:** `docs/agents/agent_platform_architect.md`  
**Skills:** `skill_requirements_parser`, `skill_system_design`  
**Inputs:** Today's curriculum article, `docs/platform_roadmap.md §{{phase_plan}}`, `current_state.md`  
**Output:** `docs/architecture/day_{{DAY_NUMBER}}_spec.md`  
**Sets:** `{{PARSER_OUTPUT}}`, `{{TASK_LIST}}`, `{{SLUG}}`

**Required spec sections:**
- `## Layer Changes` — which services/components are touched
- `## Data Contracts` — API schemas, message schemas, DB migrations
- `## Implementation Plan (Commit Units)` — ordered list; each unit has: files, gate command, commit message
- `## Success Checklist` — explicit pass/fail criteria
- `## Resilience Mandate` — circuit breaker config, fallback, async boundaries (where applicable)

**Gate to Phase 2 — all must be true:**
- [ ] `docs/architecture/day_{{DAY_NUMBER}}_spec.md` exists
- [ ] File contains `## Success Checklist` with ≥ 1 item
- [ ] File contains `## Implementation Plan (Commit Units)` with ≥ 1 unit
- [ ] All circuit breaker / fallback mechanisms declared (or N/A explicitly stated)
- [ ] Data contracts and API schemas defined

---

### Phase 2 — Implementation + Validation (Developer + QA paired)
**Agents:** `docs/agents/agent_developer.md` + `docs/agents/agent_qa_engineer.md`  
**Skills:** `skill_implementation_coder`, `skill_sequential_commit`, `skill_tester_qa`  
**Inputs:** `{{PARSER_OUTPUT}}`, `{{TASK_LIST}}`, `{{BRANCH_BASE}}`

> ⚠️ **QA is paired within Phase 2, not a separate phase.** For each commit unit: Developer implements → QA writes/updates tests → gate runs → commit. Tests must exist at commit time.

**Branch first:**
```bash
BRANCH="feature/day-$(printf '%02d' {{DAY_NUMBER}})-{{SLUG}}"
git checkout {{BRANCH_BASE}} && git pull origin {{BRANCH_BASE}}
git checkout -b "$BRANCH"
```

**Per commit unit (iterate — do NOT batch):**
1. **Developer** implements all files listed for the unit (parallel writes within a unit are fine).
2. **QA** writes or updates tests for that unit.
3. **Commit gate:** run the gate command from the spec. Must exit 0. Any failure → halt. Output error. Do NOT commit. Fix before continuing.
4. **Commit** on green:
   ```
   git add -A
   git commit -m "<type>(<scope>): <message from spec>
   
   Day {{DAY_NUMBER}} — unit N of M
   Coverage: <reported %>
   Lint: clean"
   ```

**Sets:** `{{CODER_OUTPUT}}` — `## Commit Log` table + annotated integration points

**Gate to Phase 4 — all must be true:**
- [ ] `## Commit Log` has zero halted units
- [ ] Every unit row: Lint ✅, Tests ✅
- [ ] Feature branch exists on `origin` and is ahead of `{{BRANCH_BASE}}`
- [ ] Every `## Success Checklist` item from Phase 1 spec maps to ≥ 1 test

**Halt condition:** Any halted unit → Developer+QA resolve before Phase 4 starts.

---

### Phase 4 — Delivery & Operations (DevOps)
**Agent:** `docs/agents/agent_devops_sre.md`  
**Skills:** `skill_cicd_infrastructure`, `skill_ops_runbook`  
**Inputs:** `{{PARSER_OUTPUT}}`, `{{CODER_OUTPUT}}`  
**Outputs:**
- `ops/Dockerfile` (updated)
- `.github/workflows/ci.yml` (updated)
- `ops/runbooks/day_{{DAY_NUMBER}}_runbook.md`

**Gate to Phase 4b — all must be true:**
- [ ] CI pipeline passes on the feature branch
- [ ] Health check endpoint validated by pipeline
- [ ] Rollback plan documented in runbook

---

### Phase 4b — Review & Merge
**Agent:** `docs/agents/agent_reviewer.md`  
**Skills:** `skill_reviewer_auditor`, `skill_pr_writer`  
**Inputs:** `{{PARSER_OUTPUT}}`, `{{CODER_OUTPUT}}`, QA test output, ops artifacts  
**Output:** `docs/architecture/day_{{DAY_NUMBER}}_review_report.md` + PR URL + merged SHA

#### Verdict: PASS
1. `skill_pr_writer`: push branch → open PR against `develop` → wait for CI → squash-merge → delete branch.
2. Proceed to Phase 5.

#### Verdict: FAIL — Full Phase 1 Restart
1. Reviewer enumerates blockers, each mapped to a responsible agent with specific action items.
2. **Discard current-day artifacts** (spec, commit log, runbook).
3. Update `current_state.md`:
   - Set `current_phase: 1`
   - Append each blocker to `incomplete_tasks`
   - Mark all Phase Outputs for the day ❌
4. **Restart from Phase 1.** No new day begins until a PASS verdict is issued.

---

### Phase 5 — State Update & Changelog
**Agent:** Orchestrator  
**Inputs:** All upstream outputs + merged PR data

**Required writes (all mandatory):**
1. **Append** new row to `## Completed Days`: Day, title, key outputs, notes, status (✅/⏳/❌).
2. **Append** new rows to `## Active Dependency Map` for new foundational resources. Update "Consumed By" for existing resources with new consumers.
3. **Replace** `## Active Infrastructure Snapshot` with current state of all services, DBs, queues, pipelines.
4. **Update** `## Carry-Forward Items`: remove resolved items, append new unresolved decisions/homework.
5. **Append** permanent tech choices / API contract freezes to `## Architectural Decisions Log`.
6. **Replace** `## Last Session Summary` with today's date, day number, and 3-bullet hand-off note.
7. **Changelog:** write or append to `changelog/YYYY-MM-DD.md`:
   ```markdown
   ## Day {{DAY_NUMBER}} — {{DELIVERABLE_TITLE}}
   **MR:** [feat(day-{{DAY_NUMBER}}): {{TITLE}}]({{PR_URL}}) · merged `{{SHA}}`

   ### Changed
   - <bullet per shipped item>

   ### Deferred
   - <bullet per new carry-forward, or "None">

   ### Fixed
   - <bullet per resolved carry-forward, or "None">
   ```
8. **Validate:** run `./scripts/validate_state.sh {{DAY_NUMBER}}` → must exit 0.
9. Update `current_state.md`: increment `current_day`, set `current_phase: 0`, clear `incomplete_tasks`.

> **Rule:** Never truncate or rewrite history. Append only — except `## Last Session Summary` and `## Active Infrastructure Snapshot`, which are full replacements.

---

## 4. Gate Flow

```
Phase 0 ──gate──▶ [Phase 0b] ──▶ Phase 1 ──gate──▶ Phase 2 ──gate──▶ Phase 4 ──gate──▶ Phase 4b
                                                                                              │
                         ◀──── FAIL: discard day artifacts · append incomplete_tasks · restart Phase 1 ────┘
                                                                                              │
                                                                                         PASS ▼
                                                                                        Phase 5
```

Each `──gate──▶` is a hard stop. Gates are verified by the Orchestrator before invoking the next agent.  
**No phase may be skipped.** Phase 0b is the only conditional phase.

---

## 5. Day Sequencing Rule

> **Serial days only.** Day N+1 MUST NOT begin until Day N's Phase 4b issues a PASS verdict and the feature branch is squash-merged into `develop`.

Enforced at Phase 0:
- `current_phase: 0` AND `incomplete_tasks: []` → safe to proceed to Day N+1.
- Any other state → surface blockers, do not increment `current_day`.

---

## 6. Agent Handoff Protocol

1. Each agent receives inputs as **file references** (e.g. `@docs/architecture/day_01_spec.md`).
2. Each agent emits outputs as **named artifacts** declared in its `## Output Constraints`.
3. The Orchestrator resolves all `{{VARIABLE}}` tokens before passing context to agents.
4. **Agents do not communicate directly.** All routing passes through the Orchestrator.

---

## 7. Artifact Registry

| Phase | Artifact | Path |
|---|---|---|
| 1 | Architecture Spec | `docs/architecture/day_{{DAY_NUMBER}}_spec.md` |
| 2 | Commit Log + Branch | Inline in Developer output + `origin/feature/day-{{DAY_NUMBER}}-{{SLUG}}` |
| 4 | Ops Bundle | `ops/Dockerfile`, `.github/workflows/ci.yml`, `ops/runbooks/day_{{DAY_NUMBER}}_runbook.md` |
| 4b | Review Report | `docs/architecture/day_{{DAY_NUMBER}}_review_report.md` |
| 4b | Merged PR | `develop` branch squash commit |
| 5 | Changelog Entry | `changelog/YYYY-MM-DD.md` |
