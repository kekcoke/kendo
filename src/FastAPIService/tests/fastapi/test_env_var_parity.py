"""Test: .env.example ↔ docker-compose.yml environment variable parity.

Ensures that:
1. All docker-compose environment vars have a matching .env.example entry
   (case-insensitive, handling PascalCase<->UPPER_SNAKE_CASE normalisation).
2. Core services (gateway, userservice, worker) have required connection
   string variables in docker-compose.yml.
3. All code-consumed variables (from config.py and .NET Shared libs) are
   documented in .env.example with a default value.
4. No unexpected vars in docker-compose environment blocks (drift detection).
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest
import yaml


# ── Paths ─────────────────────────────────────────────────────────────────
PROJECT_ROOT = Path(__file__).resolve().parents[4]
DOT_ENV_EXAMPLE = PROJECT_ROOT / ".env.example"
DOCKER_COMPOSE = PROJECT_ROOT / "docker-compose.yml"


# ── Normalisation ─────────────────────────────────────────────────────────

def normalise_var_name(name: str) -> str:
    """Convert any variable name to a canonical form for comparison.

    Canonical form: all-caps, no underscores within segments, segments
    separated by double underscore.

    Strategy: split on double underscore (the .NET config hierarchy
    separator), uppercase each segment, strip internal underscores.
    This correctly maps both PascalCase and UPPER_SNAKE_CASE variants
    of the same .NET config key to the same canonical string.

    Examples:
        Kendo__Jwt__Issuer             → KENDO__JWT__ISSUER
        KENDO__JWT__ISSUER             → KENDO__JWT__ISSUER
        PrivateKeyPath                 → PRIVATEKEYPATH
        Redis__ConnectionString        → REDIS__CONNECTIONSTRING
        FASTAPI__ENV                   → FASTAPI__ENV
        ASPNETCORE_URLS                → ASPNETCOREURLS
        ConnectionStrings__DefaultConnection → CONNECTIONSTRINGS__DEFAULTCONNECTION
        Kendo__FastApi__BaseUrl        → KENDO__FASTAPI__BASEURL
        KENDO__FASTAPI__BASE_URL       → KENDO__FASTAPI__BASEURL
    """
    segments = name.split("__")
    return "__".join(seg.upper().replace("_", "") for seg in segments)


def normalise_set(names: set[str]) -> set[str]:
    """Return a set of normalised variable names."""
    return {normalise_var_name(n) for n in names}


def test_normalise_var_name() -> None:
    """Verify normalisation works for expected patterns."""
    assert normalise_var_name("Kendo__Jwt__Issuer") == "KENDO__JWT__ISSUER"
    assert normalise_var_name("KENDO__JWT__ISSUER") == "KENDO__JWT__ISSUER"
    assert normalise_var_name("PrivateKeyPath") == "PRIVATEKEYPATH"
    assert normalise_var_name("Redis__ConnectionString") == "REDIS__CONNECTIONSTRING"
    assert normalise_var_name("FASTAPI__ENV") == "FASTAPI__ENV"
    assert normalise_var_name("ASPNETCORE_URLS") == "ASPNETCOREURLS"
    assert normalise_var_name("ConnectionStrings__DefaultConnection") == "CONNECTIONSTRINGS__DEFAULTCONNECTION"
    assert normalise_var_name("KENDO__JWT__PRIVATE_KEY_PATH") == "KENDO__JWT__PRIVATEKEYPATH"
    assert normalise_var_name("KENDO__FASTAPI__CIRCUIT_BREAKER_BREAK_SECONDS") == "KENDO__FASTAPI__CIRCUITBREAKERBREAKSECONDS"
    assert normalise_var_name("kendo__fastapi__circuit_breaker_break_seconds") == "KENDO__FASTAPI__CIRCUITBREAKERBREAKSECONDS"
    assert normalise_var_name("Kendo__FastApi__BaseUrl") == "KENDO__FASTAPI__BASEURL"


# ── Parsers ───────────────────────────────────────────────────────────────

def parse_dotenv_example(path: Path) -> dict[str, str]:
    """Return {VAR_NAME: value} from .env.example (non-comment lines only)."""
    result: dict[str, str] = {}
    pattern = re.compile(r"^(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?P<val>.*)$")
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        m = pattern.match(line)
        if m:
            result[m.group("var")] = m.group("val")
    return result


def parse_docker_compose_env(path: Path) -> dict[str, list[str]]:
    """Return {service_name: [var1, var2, ...]} from docker-compose.yml."""
    with open(path) as f:
        data = yaml.safe_load(f)

    result: dict[str, list[str]] = {}
    for svc_name, svc_def in data.get("services", {}).items():
        env_list = svc_def.get("environment", [])
        vars_in_service: list[str] = []
        for entry in env_list:
            if isinstance(entry, str) and "=" in entry:
                var_name = entry.split("=", 1)[0].strip()
                if var_name:
                    vars_in_service.append(var_name)
        if vars_in_service:
            result[svc_name] = vars_in_service
    return result


# ── KNOWLEDGE BASE ────────────────────────────────────────────────────────

# Vars expected in docker-compose but NOT in .env.example (infrastructure-only)
COMPOSE_ONLY_VARS = {
    "ASPNETCORE_URLS",
    "POSTGRES_USER",
    "POSTGRES_PASSWORD",
    "POSTGRES_DB",
}

# Vars expected in .env.example but NOT in docker-compose (local-dev defaults)
DOTENV_ONLY_VARS = {
    # .NET local dev (docker-compose uses DOTNET_ENVIRONMENT)
    "ASPNETCORE_ENVIRONMENT",
    # FastAPI config.py defaults — sensible for Docker, documented for local override
    "FASTAPI__RELOAD",
    "FASTAPI__BREAKER_PGVECTOR_FAIL_MAX",
    "FASTAPI__BREAKER_PGVECTOR_RESET_TIMEOUT",
    "FASTAPI__BREAKER_OPENAI_FAIL_MAX",
    "FASTAPI__BREAKER_OPENAI_RESET_TIMEOUT",
    "FASTAPI__RETRY_MAX_ATTEMPTS",
    "FASTAPI__RETRY_MIN_WAIT",
    "FASTAPI__RETRY_MAX_WAIT",
    "FASTAPI__RETRY_MULTIPLIER",
    "FASTAPI__OTEL_SERVICE_NAME",
    "FASTAPI__OTEL_EXPORTER_OTLP_ENDPOINT",
    "FASTAPI__INTENT_CLASSIFIER_MODEL",
    "FASTAPI__USER_SERVICE_BASE_URL",
    "FASTAPI__USER_SERVICE_TIMEOUT",
}

# Core required vars per service (must be present in docker-compose)
CORE_REQUIRED_VARS: dict[str, list[str]] = {
    "gateway": ["Redis__ConnectionString"],
    "userservice": [
        "ConnectionStrings__DefaultConnection",
        "Redis__ConnectionString",
    ],
    "worker": [
        "ConnectionStrings__DefaultConnection",
        "Redis__ConnectionString",
    ],
}

# All vars consumed by code that must appear in .env.example
CODE_CONSUMED_VARS = {
    # FastAPI config.py
    "FASTAPI__ENV",
    "FASTAPI__SERVICE_NAME",
    "FASTAPI__JWT__JWKS_URL",
    "FASTAPI__JWT__AUDIENCE",
    "FASTAPI__JWT__ISSUER",
    "FASTAPI__HOST",
    "FASTAPI__PORT",
    "FASTAPI__LOG_LEVEL",
    "FASTAPI__VECTOR__READ_DSN",
    "FASTAPI__LLM__ENDPOINT",
    "FASTAPI__LLM__API_KEY",
    "FASTAPI__LLM__DEPLOYMENT_NAME",
    "FASTAPI__LLM__API_VERSION",
    "FASTAPI__EMBEDDING_MODEL",
    "FASTAPI__EMBEDDING_DIMENSION",
    "FASTAPI__RATE_LIMIT__TOKENS_PER_WINDOW",
    "FASTAPI__RATE_LIMIT__WINDOW_SECONDS",
    "FASTAPI__RATE_LIMIT__BUCKET_CAPACITY",
    "FASTAPI__BREAKER_PGVECTOR_FAIL_MAX",
    "FASTAPI__BREAKER_PGVECTOR_RESET_TIMEOUT",
    "FASTAPI__BREAKER_OPENAI_FAIL_MAX",
    "FASTAPI__BREAKER_OPENAI_RESET_TIMEOUT",
    "FASTAPI__RETRY_MAX_ATTEMPTS",
    "FASTAPI__RETRY_MIN_WAIT",
    "FASTAPI__RETRY_MAX_WAIT",
    "FASTAPI__RETRY_MULTIPLIER",
    "FASTAPI__OTEL_SERVICE_NAME",
    "FASTAPI__OTEL_EXPORTER_OTLP_ENDPOINT",
    "FASTAPI__INTENT_CLASSIFIER_MODEL",
    "FASTAPI__USER_SERVICE_BASE_URL",
    "FASTAPI__USER_SERVICE_TIMEOUT",
    # .NET Shared lib / UserService
    "ConnectionStrings__DefaultConnection",
    "Redis__ConnectionString",
    "Rebus__ConnectionString",
    "Rebus__AiWorkers",
    "Rebus__AiParallelism",
    "Rebus__AiDlqDepthThreshold",
    "Rebus__DlqConsumerEnabled",
    "KENDO__JWT__PRIVATE_KEY_PATH",
    "KENDO__JWT__ISSUER",
    "KENDO__JWT__AUDIENCE",
    "KENDO__JWT__ROTATION_DAYS",
    "KENDO__JWT__TOKEN_LIFETIME_MINUTES",
    "KENDO__JWT__SERVICE_TOKEN_LIFETIME_SECONDS",
    "KENDO__FASTAPI__BASE_URL",
    "KENDO__FASTAPI__TIMEOUT_SECONDS",
    "KENDO__FASTAPI__CIRCUIT_BREAKER_FAILURES",
    "KENDO__FASTAPI__CIRCUIT_BREAKER_BREAK_SECONDS",
    "KENDO__EVENT_EMBEDDING__DIM",
    "KENDO__EVENT_EMBEDDING__MODEL",
}


# ── Fixtures ──────────────────────────────────────────────────────────────

@pytest.fixture(scope="module")
def dotenv_vars() -> dict[str, str]:
    assert DOT_ENV_EXAMPLE.exists(), f".env.example not found at {DOT_ENV_EXAMPLE}"
    return parse_dotenv_example(DOT_ENV_EXAMPLE)


@pytest.fixture(scope="module")
def compose_env() -> dict[str, list[str]]:
    assert DOCKER_COMPOSE.exists(), f"docker-compose.yml not found at {DOCKER_COMPOSE}"
    return parse_docker_compose_env(DOCKER_COMPOSE)


# ── Tests ─────────────────────────────────────────────────────────────────


class TestEnvVarParity:
    """Validate environment variable parity between .env.example and
    docker-compose.yml."""

    def test_dotenv_example_exists(self):
        assert DOT_ENV_EXAMPLE.exists(), ".env.example is missing"

    def test_docker_compose_exists(self):
        assert DOCKER_COMPOSE.exists(), "docker-compose.yml is missing"

    def test_normalisation(self):
        """Verify the normalisation helper works correctly."""
        test_normalise_var_name()

    # ── Compose vars → .env.example (forward direction) ────────────────

    @pytest.mark.parametrize(
        "service",
        [pytest.param(svc, id=f"service={svc}")
         for svc in parse_docker_compose_env(DOCKER_COMPOSE)],
    )
    def test_compose_vars_have_dotenv_entry(
        self,
        service: str,
        dotenv_vars: dict[str, str],
        compose_env: dict[str, list[str]],
    ):
        """Every docker-compose environment var (excluding COMPOSE_ONLY_VARS)
        must have a corresponding entry in .env.example."""
        assert service in compose_env
        dotenv_norm = normalise_set(set(dotenv_vars.keys()))

        unmapped: list[str] = []
        for var in compose_env[service]:
            if var in COMPOSE_ONLY_VARS:
                continue
            if normalise_var_name(var) not in dotenv_norm:
                unmapped.append(var)

        assert not unmapped, (
            f"Service '{service}' has {len(unmapped)} env var(s) in "
            f"docker-compose.yml with no matching entry in .env.example "
            f"(checked via normalised name):\n  " + "\n  ".join(unmapped)
        )

    # ── Core required vars ──────────────────────────────────────────────

    @pytest.mark.parametrize(
        "service,required_vars",
        [pytest.param(svc, vars_, id=f"core_vars={svc}")
         for svc, vars_ in CORE_REQUIRED_VARS.items()],
    )
    def test_core_required_vars(
        self,
        service: str,
        required_vars: list[str],
        compose_env: dict[str, list[str]],
    ):
        """Core services must have connection string vars in
        docker-compose.yml."""
        assert service in compose_env, (
            f"Service '{service}' has no environment block in docker-compose.yml"
        )
        compose_norm = normalise_set(set(compose_env[service]))
        missing = [v for v in required_vars
                   if normalise_var_name(v) not in compose_norm]
        assert not missing, (
            f"Core service '{service}' is missing required vars "
            f"(normalised comparison): {missing}"
        )

    # ── Code-consumed vars documented in .env.example ───────────────────

    @pytest.mark.parametrize("var_name", sorted(CODE_CONSUMED_VARS))
    def test_code_consumed_vars_in_dotenv_example(
        self,
        var_name: str,
        dotenv_vars: dict[str, str],
    ):
        """Every var consumed by code must be documented in .env.example."""
        dotenv_norm = normalise_set(set(dotenv_vars.keys()))
        assert normalise_var_name(var_name) in dotenv_norm, (
            f"Code-consumed var '{var_name}' is missing from .env.example"
        )

    # ── No orphaned compose vars (drift detection) ─────────────────────

    def test_no_orphaned_compose_vars(
        self,
        dotenv_vars: dict[str, str],
        compose_env: dict[str, list[str]],
    ):
        """Flag docker-compose env vars not in .env.example nor COMPOSE_ONLY."""
        all_compose: set[str] = set()
        for vars_list in compose_env.values():
            all_compose.update(vars_list)

        dotenv_norm = normalise_set(set(dotenv_vars.keys()))

        orphaned: list[str] = []
        for var in sorted(all_compose):
            if var in COMPOSE_ONLY_VARS:
                continue
            if normalise_var_name(var) in dotenv_norm:
                continue
            orphaned.append(var)

        assert not orphaned, (
            f"{len(orphaned)} compose env var(s) not found in .env.example "
            f"or COMPOSE_ONLY:\n  " + "\n  ".join(orphaned)
        )
