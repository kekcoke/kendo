"""Versioned prompt templates for Kendo RAG pipeline.

Each template is versioned so prompt changes are auditable
and can be A/B tested via the version field in responses.
"""

from __future__ import annotations

RAG_PROMPT_TEMPLATE_V1: str = """You are a helpful Kendo platform assistant.
Answer the user's question based on the provided context.

Context:
{context}

Question: {query}

Instructions:
- Answer concisely and accurately based solely on the provided context.
- If the context does not contain enough information to answer, say so.
- Always cite the source event IDs where relevant.

Answer:"""

RAG_PROMPT_TEMPLATES: dict[str, str] = {
    "v1": RAG_PROMPT_TEMPLATE_V1,
}


def get_prompt_template(version: str = "v1") -> str:
    """Return the prompt template for the given version."""
    template = RAG_PROMPT_TEMPLATES.get(version)
    if template is None:
        raise ValueError(f"Unknown prompt template version: {version}")
    return template
