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
