# Skill: System Design

## Objective
Translate parsed requirements into a concrete architectural design — directory structure, component boundaries, data contracts, and API schemas — scoped strictly to the current deliverable.

## Input Context
* **{{PARSER_OUTPUT}}**: The structured requirements extracted by the Requirements Parser skill.
* **{{CURRICULUM_PHASE}}**: Current position in the 180-day arc (governs complexity ceiling).

## Execution Directives
1. **Design Directory Structure:** Generate a complete monorepo/project tree that fulfills the requirements.
2. **Define Component Boundaries:** Identify which service (User Service, Background Worker, Web App) is affected and specify its interface contracts.
3. **Specify Data Contracts & API Schemas:** Produce explicit request/response shapes, database schemas, or message formats as required.
4. **Establish Configuration Management:** Define how environment variables and external config are handled.

## Output Format
Return a structured markdown document:
- `## Directory Scaffolding` (ASCII tree)
- `## Component Boundaries & Interfaces`
- `## Data Contracts / API Schemas`
- `## Configuration Strategy`
