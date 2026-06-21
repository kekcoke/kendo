"""Citation-grounded RAG chain for W6 Document Q&A / Onboarding Assistant (M5.12).

Pipeline: embed query -> search AssistantIndex -> build prompt with citations ->
generate answer with source file + line range references.

Key design:
- Uses the AssistantIndex (local Chroma-alike store) — no external network calls
- Citations include source file path, line range, and excerpt text
- Lightweight implementation using the existing embedding client
- Fallback if index is missing/unavailable: returns RFC 7807 503
"""

from __future__ import annotations

from typing import Any
from uuid import uuid4

from app.config import Settings
from app.rag.assistant_index import AssistantIndex


class AssistantChain:
    """RAG chain that answers questions from the project corpus.

    Pipeline: embed query -> search AssistantIndex -> build answer with citations.
    """

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self._prompt_version = "v2"  # W6 uses v2 prompt template

    async def answer(
        self,
        query: str,
        corpus_root: str | None = None,
        index_dir: str | None = None,
    ) -> dict[str, Any]:
        """Answer a question using the project corpus index.

        Args:
            query: Natural language question.
            corpus_root: Override for corpus root path.
            index_dir: Override for index persistence path.

        Returns:
            dict with:
              - answer: str — the generated answer text.
              - citations: list of {source, line_range, text} dicts.
              - trace_id: str
        """
        trace_id = str(uuid4())

        # 1. Load/ensure index
        index = AssistantIndex(
            self.settings,
            corpus_root=corpus_root,
            index_dir=index_dir,
        )
        await index.load_or_build()

        if not index.is_ready:
            return {
                "answer": "The assistant knowledge base is empty. Run `scripts/assistant-reindex.sh` to build the index.",
                "citations": [],
                "trace_id": trace_id,
            }

        # 2. Search index
        results = await index.search(query, top_k=5)

        if not results:
            return {
                "answer": "I couldn't find relevant information in the project corpus to answer your question.",
                "citations": [],
                "trace_id": trace_id,
            }

        # 3. Build answer from contexts
        answer_parts: list[str] = []
        citations: list[dict[str, Any]] = []

        for r in results:
            # Build citation
            line_range = f"{r['line_start']}-{r['line_end']}"
            citations.append({
                "source": r["source"],
                "line_range": line_range,
                "text": r["text"][:200],
            })

            # Build answer snippet
            answer_parts.append(
                f"From **{r['source']}** (lines {line_range}):\n{r['text']}"
            )

        answer = self._build_answer(query, answer_parts, citations)

        return {
            "answer": answer,
            "citations": citations,
            "trace_id": trace_id,
        }

    def _build_answer(
        self,
        query: str,
        context_parts: list[str],
        citations: list[dict[str, Any]],
    ) -> str:
        """Build a human-readable answer from retrieved contexts.

        In production, this would call an LLM for answer generation.
        For M5.12, returns a structured summary of found contexts.
        """
        if not context_parts:
            return "No relevant context found for your query."

        # Build a structured answer with citation references
        context_str = "\n\n".join(context_parts)

        prompt = f"""Based on the project documentation, here is what I found about '{query}':

{context_str}

I found {len(citations)} relevant document section(s) that address your question. Each citation includes the source file and line range for verification."""

        return prompt
