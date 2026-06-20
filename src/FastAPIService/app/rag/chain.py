"""LangChain RAG pipeline — RetrievalQA chain with pgvector retriever.

Uses the pgvector cosine similarity search via asyncpg,
then passes retrieved contexts to the LLM for answer generation.
"""

from __future__ import annotations

import json
from typing import Any, AsyncGenerator
from uuid import uuid4

from app.config import Settings
from app.rag.embeddings_client import create_embedding_client
from app.rag.prompts import get_prompt_template


class KendoRAGChain:
    """RAG chain that retrieves from pgvector and generates answers.

    This is a lightweight implementation that follows the pre-authored
    fastapi_rag_service_spec.md data contracts but avoids heavy LangChain
    dependency chains that may not be available in all environments.
    """

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.embedding_client = create_embedding_client(settings)
        self._prompt_version = "v1"

    async def query(
        self,
        query_text: str,
        top_k: int = 5,
        filters: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        """Execute a synchronous RAG query.

        1. Embed the query text
        2. Search pgvector for similar embeddings
        3. Return retrieved contexts (answer generation via LLM is future)
        """
        # 1. Embed query
        query_embedding = await self.embedding_client.embed_query(query_text)

        # 2. Search pgvector for similar embeddings
        from app.repositories.embeddings import similarity_search

        contexts = await similarity_search(query_embedding, top_k=top_k, filters=filters)

        # 3. Build the RAG response with contexts
        # LLM answer generation will be wired in when Azure OpenAI is configured
        trace_id = str(uuid4())

        return {
            "answer": self._build_answer(query_text, contexts),
            "contexts": contexts,
            "trace_id": trace_id,
        }

    async def stream(
        self,
        query_text: str,
        top_k: int = 5,
        filters: dict[str, Any] | None = None,
    ) -> AsyncGenerator[str, None]:
        """Stream RAG response as SSE events.

        Yields JSON chunks for each step of the pipeline.
        """
        yield json.dumps({"type": "start", "trace_id": str(uuid4())}) + "\n\n"

        query_embedding = await self.embedding_client.embed_query(query_text)
        yield json.dumps({"type": "embedding", "dimensions": len(query_embedding)}) + "\n\n"

        from app.repositories.embeddings import similarity_search

        contexts = await similarity_search(query_embedding, top_k=top_k, filters=filters)
        yield json.dumps({"type": "contexts", "count": len(contexts)}) + "\n\n"

        answer = self._build_answer(query_text, contexts)
        for chunk in [answer[i : i + 50] for i in range(0, len(answer), 50)]:
            yield json.dumps({"type": "token", "text": chunk}) + "\n\n"

        yield json.dumps({"type": "done"}) + "\n\n"

    def _build_answer(self, query: str, contexts: list[dict[str, Any]]) -> str:
        """Build an answer string from retrieved contexts.

        In future milestones, this will call Azure OpenAI / local LLM.
        For M5.2, returns a summary of found contexts.
        """
        if not contexts:
            return "No relevant context found for your query."

        prompt = get_prompt_template(self._prompt_version)
        context_str = "\n\n".join(
            f"[{c['source']}] (score: {c['score']:.3f}): {c['text'][:200]}"
            for c in contexts
        )
        return prompt.format(context=context_str, query=query)
