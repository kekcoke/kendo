"""Application settings via pydantic-settings."""

from __future__ import annotations

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Configuration for the FastAPI service.

    Loaded from environment variables and .env file.
    All variables are prefixed with FASTAPI__ for consistency.
    """

    model_config = SettingsConfigDict(
        env_prefix="FASTAPI__",
        env_file=".env",
        env_file_encoding="utf-8",
        case_sensitive=False,
    )

    # --- General ---
    env: str = "development"
    service_name: str = "kendo-fastapi"

    # --- JWT ---
    jwt_jwks_url: str = "http://gateway:5000/.well-known/jwks.json"
    jwt_audience: str = "kendo.api"
    jwt_issuer: str = "https://gateway.local/.well-known/jwks.json"

    # --- Server ---
    host: str = "0.0.0.0"
    port: int = 8000
    reload: bool = True
    log_level: str = "info"

    # --- pgvector (read-only, fastapi_ro role) ---
    vector_read_dsn: str = ""

    # --- LLM / Azure OpenAI ---
    llm_endpoint: str = ""
    llm_api_key: str = ""
    llm_deployment_name: str = ""
    llm_api_version: str = "2024-02-15-preview"

    # --- Embedding model ---
    embedding_model: str = "BGE-large-en-v1.5"
    embedding_dimension: int = 1024

    # --- Resilience (pybreaker) ---
    breaker_pgvector_fail_max: int = 3
    breaker_pgvector_reset_timeout: int = 30  # seconds
    breaker_openai_fail_max: int = 3
    breaker_openai_reset_timeout: int = 30  # seconds

    # --- Resilience (tenacity) ---
    retry_max_attempts: int = 3
    retry_min_wait: float = 1.0
    retry_max_wait: float = 30.0
    retry_multiplier: float = 2.0

    # --- OpenTelemetry ---
    otel_service_name: str = "kendo-fastapi"
    otel_exporter_otlp_endpoint: str = ""  # empty = console exporter fallback
