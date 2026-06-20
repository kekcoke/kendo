# syntax=docker/dockerfile:1
# Multi-stage build for Kendo FastAPI Service (Python 3.12)

# === Build stage ===
FROM python:3.12-slim AS builder

WORKDIR /app

# Install system build dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    gcc \
    libpq-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy dependency manifests
COPY pyproject.toml ./

# Install dependencies to a virtualenv
RUN python -m venv /venv && \
    /venv/bin/pip install --no-cache-dir -e .

# === Runtime stage ===
FROM python:3.12-slim AS runtime

WORKDIR /app

# Install runtime system dependencies only (psycopg2/pgvector support for future M5.3)
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpq-dev \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Copy virtualenv from builder
COPY --from=builder /venv /venv
ENV PATH="/venv/bin:$PATH"

# Copy application code
COPY . .

# Create non-root user
RUN groupadd -r fastapi && useradd -r -g fastapi -d /app -s /sbin/nologin fastapi && \
    chown -R fastapi:fastapi /app

USER fastapi

EXPOSE 8000

HEALTHCHECK --interval=10s --timeout=5s --retries=3 --start-period=10s \
    CMD curl -f http://localhost:8000/health/live || exit 1

CMD ["uvicorn", "app.main:create_app", "--factory", "--host", "0.0.0.0", "--port", "8000"]
