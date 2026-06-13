# Skill: Sequential Commit

## Objective
Gate every code commit behind a passing lint and test run. Produce a verifiable, task-by-task commit trail on a feature branch — no broken commits ever enter the branch history.

## Input Context
* **{{DAY_NUMBER}}**: Current day (used for branch naming and commit scoping).
* **{{TASK_LIST}}**: Ordered list of implementation tasks from the Architect's spec (e.g., "Add /health route", "Add /disk endpoint", "Wire Docker healthcheck").
* **{{TEST_COMMAND}}**: Command to run the full test suite (e.g., `npm test`, `pytest`).
* **{{LINT_COMMAND}}**: Command to run the linter (e.g., `npm run lint`, `ruff check .`).
* **{{BRANCH_BASE}}**: Branch to fork from (default: `develop`).

## Execution Directives

### Step 1 — Branch Checkout
```bash
BRANCH="feature/day-$(printf '%02d' {{DAY_NUMBER}})-{{SLUG}}"
git checkout {{BRANCH_BASE}}
git pull origin {{BRANCH_BASE}}
git checkout -b "$BRANCH"
```
- `{{SLUG}}` is a kebab-case summary of the day's deliverable (e.g., `infra-foundation`).
- If the branch already exists, check it out and rebase onto `{{BRANCH_BASE}}` before adding new commits.

### Step 2 — Per-Task Commit Loop
For **each task** in `{{TASK_LIST}}`, in order:

1. **Implement** the task (scoped change only — do not bundle multiple tasks).
2. **Run lint:**
   ```bash
   {{LINT_COMMAND}}
   ```
   - If lint **fails**: stop. Output the lint errors. Do NOT commit. Request fix before continuing.
3. **Run tests:**
   ```bash
   {{TEST_COMMAND}}
   ```
   - If tests **fail** or coverage drops below threshold: stop. Output the failure. Do NOT commit. Request fix before continuing.
4. **Commit** (only on double-green):
   ```bash
   git add -A
   git commit -m "<type>(<scope>): <task description>

   Day {{DAY_NUMBER}} — task N of M
   Coverage: <reported %>
   Lint: clean"
   ```

### Step 3 — Emit Commit Log
After all tasks complete, output a `## Commit Log` table:

| Task | Commit SHA | Lint | Tests | Coverage |
|---|---|---|---|---|
| <task description> | `<short sha>` | ✅ | ✅ | <n>% |

Any halted task must appear as:

| <task description> | — | ❌ / ✅ | ❌ | — |

## Output Format
- `## Branch` — branch name created/used
- `## Commit Log` — table as above
- `## Halted Tasks` — list of tasks that did not produce a commit, with error summary
- `## Next Step` — "All tasks committed. Ready for PR." or "N task(s) halted. Fix required before PR."
