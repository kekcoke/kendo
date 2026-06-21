"""Document Q&A / Onboarding Assistant endpoint — W6 (M5.12).

POST /v1/assistant/ask — answers questions from the project corpus
(docs/architecture/, ops/runbooks/, templates/skills/, changelog/,
.ai/orchestration.md). Returns cited answers with source file + line ranges.

FastAPI is internal-only — no external access. Uses a local Chroma-alike
vector store with no external network dependency.

Design:
- Citation accuracy >= 95% on held-out QA set
- Answer faithfulness >= 0.9
- Latency p95 <= 4s (local store, no network calls)
- Missing/corrupt index -> RFC 7807 503 with reindex guidance
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Request

from app.rag.assistant_chain import AssistantChain

router = APIRouter(prefix="/v1/assistant", tags=["assistant"])


def _get_assistant_chain(request: Request) -> AssistantChain:
    """Get or create the assistant chain from app state."""
    if not hasattr(request.app.state, "assistant_chain"):
        settings: Any = request.app.state.settings
        request.app.state.assistant_chain = AssistantChain(settings)
    return request.app.state.assistant_chain


@router.post("/ask")
async def ask_assistant(
    request: Request,
    body: dict[str, Any],
) -> dict[str, Any]:
    """Ask the Document Q&A / Onboarding Assistant a question (W6).

    Request body:
    ```json
    {"query": "string (required)"}
    ```

    Returns:
    ```json
    {
      "answer": "string",
      "citations": [
        {
          "source": "docs/architecture/day_22_spec.md",
          "line_range": "142-148",
          "text": "snippet"
        }
      ],
      "trace_id": "string"
    }
    ```
    """
    query = body.get("query", "").strip() if body else ""
    if not query:
        raise HTTPException(
            status_code=400,
            detail="'query' field is required and cannot be empty",
        )

    chain = _get_assistant_chain(request)
    result = await chain.answer(query)

    return result
