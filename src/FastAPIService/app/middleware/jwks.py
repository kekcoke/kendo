"""JWKS client for RS256 token validation.

Fetches the Gateway's JWKS endpoint once, caches for 1 hour,
and refreshes on cache-miss (unknown kid).
"""

from __future__ import annotations

import json
import time
from typing import Any

import httpx
from jwt import PyJWKClient


class CachedJWKSClient:
    """JWKS client with 1-hour TTL and refresh-on-miss."""

    def __init__(self, jwks_url: str, timeout: int = 10) -> None:
        self._jwks_url = jwks_url
        self._timeout = timeout
        self._cache: dict[str, Any] = {}  # kid -> signing key
        self._fetched_at: float = 0.0
        self._ttl: float = 3600.0  # 1 hour
        self._client = PyJWKClient(jwks_url, cache_keys=True)

    async def get_signing_key(self, kid: str) -> str | None:
        """Fetch the signing key for the given kid.

        Returns the PEM-encoded key as a string, or None if not found.
        """
        now = time.time()

        # Refresh if TTL expired
        if now - self._fetched_at > self._ttl:
            await self._fetch_jwks()

        # Check cache
        if kid in self._cache:
            return self._cache[kid]

        # Cache miss: force refresh
        await self._fetch_jwks()
        return self._cache.get(kid)

    async def _fetch_jwks(self) -> None:
        """Fetch JWKS from the Gateway and populate the cache."""
        try:
            async with httpx.AsyncClient(timeout=self._timeout) as client:
                response = await client.get(self._jwks_url)
                response.raise_for_status()
                jwks = response.json()
        except Exception:
            # If fetch fails, keep existing cache
            return

        now = time.time()
        self._fetched_at = now

        for key_data in jwks.get("keys", []):
            kid = key_data.get("kid")
            if kid:
                # Store the raw key data for later signing key derivation
                self._cache[kid] = key_data

    async def get_signing_key_from_data(self, kid: str) -> str | None:
        """Get the PEM signing key from cached JWKS data."""
        key_data = await self.get_signing_key(kid)
        if key_data is None:
            return None

        try:
            # Use PyJWKClient to construct the key
            signing_key = self._client.get_signing_key_from_jwt(
                json.dumps({"kid": kid, "alg": "RS256"})
            )
            return signing_key.key
        except Exception:
            return None
