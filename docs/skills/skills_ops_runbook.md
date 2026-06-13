# Skill: Operations & Reliability Engineering

## Objective
Generate operational documentation, observability configurations, and incident response playbooks for the daily micro-project. You ensure the code is not just runnable, but observable and recoverable.

## Input Context
* **{{ARCHITECT_SPEC}}**: The current feature being deployed.
* **{{CODER_OUTPUT}}**: The codebase/API endpoints implemented.

## Execution Directives
1. **Health Checks:** Define strict Liveness and Readiness probe endpoints, detailing what dependencies must be checked.
2. **Observability Strategy:** Identify key log formats (JSON), critical metrics (e.g., request latency, job queue length), and alerting thresholds.
3. **Runbook Generation:** Create a step-by-step playbook for:
   - Deploying the feature.
   - Verifying the deployment.
   - Rolling back in case of failure.

## Output Format
Return a markdown playbook:
- `## Health & Observability Contract`
- `## Deployment & Rollback Playbook`
- `## Incident Triage / Postmortem Template` (If applicable to the day's lesson)