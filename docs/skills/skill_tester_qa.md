# Skill: Quality Assurance & Testing

## Objective
Design a comprehensive testing strategy and validation matrix based on project requirements. Define how to prove the system works — including under failure — before or alongside implementation.

## Input Context
* **{{PARSER_OUTPUT}}**: The structured requirements extracted by the Requirements Parser skill.
* **{{CODER_OUTPUT}}** *(optional in serial mode)*: Specific implementation details for targeted test generation.

## Execution Directives
1. **Define Automated Test Suites:** Specify unit, integration, and end-to-end tests required by the tech stack, including frameworks and target coverage thresholds.
2. **Create Functional Scenarios:** Map out step-by-step workflows to verify core features and expected outcomes.
3. **Identify Edge Cases & Failure States:** Define how the system must behave when dependencies fail, bad data is submitted, or invariants are violated.

## Output Format
Return a structured markdown document:
- `## Automated Testing Strategy` (frameworks, coverage targets)
- `## End-to-End Functional Demos` (step-by-step expected outcomes)
- `## Failure State Validations`
