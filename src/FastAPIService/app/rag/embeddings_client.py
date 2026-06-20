"""Embedding model client — local (BGE) vs cloud (Azure OpenAI).

Seam swap via app/llm/embeddings.py, selected at startup
based on KENDO_ENV or embedding_model setting.
"""

from __future__ import annotations

from typing import Any

from app.config import Settings


class BaseEmbeddingClient:
    """Base class for embedding clients."""

    async def embed_query(self, text: str) -> list[float]:
        raise NotImplementedError

    async def embed_documents(self, texts: list[str]) -> list[list[float]]:
        raise NotImplementedError


class BGEQueryEmbeddingClient(BaseEmbeddingClient):
    """Local embedding client using BGE-large-en-v1.5 via sentence-transformers."""

    def __init__(self, model_name: str = "BAAI/bge-large-en-v1.5", device: str = "cpu") -> None:
        self.model_name = model_name
        self.device = device
        self._model: Any = None

    async def _lazy_load(self) -> None:
        if self._model is None:
            from sentence_transformers import SentenceTransformer
            self._model = SentenceTransformer(self.model_name, device=self.device)

    async def embed_query(self, text: str) -> list[float]:
        await self._lazy_load()
        embedding = self._model.encode(text, normalize_embeddings=True)
        return embedding.tolist()

    async def embed_documents(self, texts: list[str]) -> list[list[float]]:
        await self._lazy_load()
        embeddings = self._model.encode(texts, normalize_embeddings=True)
        return [e.tolist() for e in embeddings]


def create_embedding_client(settings: Settings) -> BaseEmbeddingClient:
    """Factory function — creates the appropriate embedding client.

    For local dev/CI: BGE-large-en-v1.5 (1024 dimensions).
    For cloud: Azure OpenAI text-embedding-3-small (1536 dimensions).
    """
    if settings.env == "development" or settings.embedding_model.startswith("BGE"):
        return BGEQueryEmbeddingClient()
    else:
        # Cloud: use Azure OpenAI embedding (deferred implementation)
        # from app.llm.azure_embeddings import AzureEmbeddingClient
        # return AzureEmbeddingClient(
        #     endpoint=settings.llm_endpoint,
        #     api_key=settings.llm_api_key,
        #     deployment=settings.llm_deployment_name,
        # )
        # Fallthrough to BGE for now
        return BGEQueryEmbeddingClient()
