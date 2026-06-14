# Kendo Platform — Meta Dockerfile
# Per-service Dockerfiles are at src/<service>/Dockerfile.
# This file serves as a reference for the overall platform image
# used in CI/CD and production environments.

FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL org.kendo.platform="multi-service-scaffold"
LABEL org.kendo.milestone="M2.6"

# curl is required for Docker health checks (installed in per-service Dockerfiles)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
