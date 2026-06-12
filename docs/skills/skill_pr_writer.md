# Skill: PR / MR Writer

## Objective
Push the feature branch, open a Pull Request against the integration branch, and merge it once CI passes. This skill executes only after the Reviewer audit returns all-Pass — it is the final automated gate before code lands in `develop`.

## Input Context
* **{{BRANCH}}**: Feature branch to open the PR from (output of `skill_sequential_commit`).
* **{{BASE_BRANCH}}**: Target integration branch (default: `develop`).
* **{{DAY_NUMBER}}**: Current day number.
* **{{AUDIT_REPORT}}**: The `Day_XX_Review_Report.md` produced by `skill_reviewer_auditor`. Must be all-Pass before this skill runs.
* **{{COMMIT_LOG}}**: The commit log table from `skill_sequential_commit`.
* **{{ARCHITECT_SPEC}}**: `docs/architecture/day_{{DAY_NUMBER}}_spec.md` — used for PR body context.

## Pre-condition Gate
```
IF any item in {{AUDIT_REPORT}} is "Fail" → HALT. Do not push. Return blockers to responsible agent.
IF {{COMMIT_LOG}} contains halted tasks → HALT. Do not push. Return list of unresolved tasks.
```

## Execution Directives

### Step 1 — Push Branch
```bash
git push origin {{BRANCH}}
```

### Step 2 — Generate PR Body
Produce a PR body with the following sections:

```markdown
## TL;DR
<One sentence: what this PR delivers and why.>

## Changes
### Architecture
<Summary of layer changes from Architect spec.>

### Implementation
<Key files added/modified and what they do.>

### Tests
<Test suite results — pass count, coverage %, frameworks used.>

### Ops / Infrastructure
<Dockerfile, CI, runbook changes if any.>

## Commit Trail
<Paste {{COMMIT_LOG}} table here.>

## Audit Sign-off
Reviewed by: Code Reviewer & Compliance Auditor
Result: All requirements Pass — cleared for merge.

## Known TODOs / Carry-Forwards
<List any items deferred to future days from current_state.md Carry-Forward Items.>
```

### Step 3 — Open PR
```bash
gh pr create \
  --base {{BASE_BRANCH}} \
  --head {{BRANCH}} \
  --title "feat(day-{{DAY_NUMBER}}): <deliverable title from Architect spec>" \
  --body-file /tmp/pr_day_{{DAY_NUMBER}}.md \
  --label "day-$(printf '%02d' {{DAY_NUMBER}})"
```

### Step 4 — Wait for CI and Merge
```bash
# Wait for CI checks to complete (poll every 30s, max 10 min)
gh pr checks {{BRANCH}} --watch --interval 30

# On CI green — squash merge and delete branch
gh pr merge {{BRANCH}} \
  --squash \
  --delete-branch \
  --subject "feat(day-{{DAY_NUMBER}}): <deliverable title> (#<PR_NUMBER>)"
```
- If CI fails: output the failing check name and logs. Do NOT merge. Escalate to DevOps agent.

### Step 5 — Confirm Merge
```bash
git checkout {{BASE_BRANCH}} && git pull origin {{BASE_BRANCH}}
git log --oneline -3
```
Output the final merged commit SHA and confirm the branch is deleted.

## Output Format
- `## PR URL` — link to the opened PR
- `## CI Status` — pass/fail per check
- `## Merge Result` — merged commit SHA or escalation note
- `## Branch Cleanup` — confirmation that feature branch is deleted
