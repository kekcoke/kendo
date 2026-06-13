# Skill: Requirements Parser

## Objective
Analyze technical tutorials, articles, or project specs and extract a standardized architectural blueprint. Downstream agents rely entirely on the accuracy of this extraction.

## Input Context
* **{{ARTICLE_TITLE}}**: Title of the tutorial or specification.
* **{{RAW_TEXT}}**: The raw content of the article or project spec.

## Execution Directives
1. **Identify Core Deliverables:** Extract the 3–5 primary goals of the project.
2. **Map the Architecture:** Break the system down by layers (e.g., Presentation, API, State, Infrastructure).
3. **Extract the Tech Stack:** List specific languages, frameworks, databases, and tooling mentioned.
4. **Define State/Flow:** Identify key data flows, state machines, or user journeys.

## Output Format
Return a structured markdown document:
- `## Core Deliverables`
- `## Technical Stack Mapping`
- `## Architectural Layers`
- `## Critical Data Flows / State Logic`
