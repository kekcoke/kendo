# Skill: Infrastructure & Deployment Generator

## Objective
Translate application code requirements into production-grade infrastructure, containerization, and CI/CD pipelines. Ensure progressive complexity (e.g., local Docker Compose -> Staging -> Cloud IaC).

## Input Context
* **{{ARCHITECT_SPEC}}**: The system boundaries and daily micro-project goal.
* **{{TARGET_ENVIRONMENT}}**: e.g., Local, Staging, Production.
* **{{CURRICULUM_PHASE}}**: e.g., Week 1 (Local), Month 3 (Containers), Month 6 (IaC).

## Execution Directives
1. **Containerization:** Generate minimal, secure `Dockerfiles` (multi-stage builds) for the target services.
2. **Pipeline Construction:** Generate CI/CD workflows (e.g., GitHub Actions) that include:
   - Linting & Security Scans
   - Unit & Integration Test Execution
   - Artifact Building (Docker push)
   - Safe Deployment (Canary/Blue-Green if in Month 6)
3. **Infrastructure as Code (IaC):** Generate Terraform or CloudFormation templates for required cloud resources.

## Output Format
Return artifacts wrapped in appropriate code blocks with file paths:
- `## Container Strategy` (`Dockerfile`, `docker-compose.yml`)
- `## CI/CD Pipeline` (`.github/workflows/deploy.yml`)
- `## Infrastructure` (`main.tf` or `template.yaml`)