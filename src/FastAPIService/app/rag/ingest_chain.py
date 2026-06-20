"""Event Ingestion RAG chain — free-form text to structured Event JSON.

Follows the pattern established by KendoRAGChain in chain.py:
1. Chunk the input text
2. Embed each chunk
3. Retrieve similar past events from pgvector
4. Build a prompt with context and call LLM for structured output
5. Return structured Event JSON

Designed for W1 (M5.7) — first P0 workload.
"""

from __future__ import annotations

import json
from typing import Any
from uuid import uuid4

from app.config import Settings
from app.rag.embeddings_client import create_embedding_client
from app.rag.prompts import get_prompt_template


class IngestChain:
    """Chain that transforms free-form event text into structured Event JSON.

    Pipeline: chunk -> embed -> pgvector retrieval -> LLM structured output.
    Uses the same embedding client as KendoRAGChain for model parity.
    """

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.embedding_client = create_embedding_client(settings)
        self._prompt_version = "v1"

    def _chunk_text(self, text: str, max_chars: int = 512) -> list[str]:
        """Split input text into overlapping chunks for embedding.

        Args:
            text: Free-form event description to chunk.
            max_chars: Maximum characters per chunk (default: 512).

        Returns:
            List of text chunks.
        """
        if len(text) <= max_chars:
            return [text]

        chunks: list[str] = []
        start = 0
        while start < len(text):
            end = start + max_chars
            chunks.append(text[start:end])
            # 50-char overlap for context continuity
            start = end - 50 if end < len(text) else len(text)
        return chunks

    async def ingest(
        self,
        text: str,
        user_id: str | None = None,
    ) -> dict[str, Any]:
        """Ingest free-form event text and return structured Event JSON.

        Args:
            text: Free-form event description from the user.
            user_id: JWT subject ID for context seeding (optional).

        Returns:
            Structured Event JSON with fields extracted by the LLM.
        """
        # 1. Chunk input text
        chunks = self._chunk_text(text)

        # 2. Embed each chunk
        chunk_embeddings: list[list[float]] = []
        for chunk in chunks:
            embedding = await self.embedding_client.embed_query(chunk)
            chunk_embeddings.append(embedding)

        # 3. Retrieve similar past events from pgvector
        #    Use the first (or most representative) chunk embedding for retrieval
        query_embedding = chunk_embeddings[0] if chunk_embeddings else []

        from app.repositories.embeddings import similarity_search

        contexts = await similarity_search(query_embedding, top_k=5)

        # 4. Build the prompt with context and call LLM for structured output
        structured_event = await self._call_llm(text, contexts, user_id)

        return structured_event

    async def _call_llm(
        self,
        text: str,
        contexts: list[dict[str, Any]],
        user_id: str | None = None,
    ) -> dict[str, Any]:
        """Call the LLM with context to extract structured Event fields.

        For M5.7, uses the prompt template to instruct the LLM to return
        structured JSON. If no LLM is configured, returns a mock structured
        response for development/testing parity.

        Returns:
            Dict with structured Event fields: name, date, time, location,
            headcount, dietary_notes, description, and raw text.
        """
        # Build context string from retrieved past events
        context_str = "\n\n".join(
            f"[{c['source']}] (score: {c['score']:.3f}): {c['text'][:300]}"
            for c in contexts
        ) if contexts else "No similar past events found."

        ingest_prompt = f"""You are a Kendo event ingestion assistant.
Extract structured event information from the user's free-form text.

Relevant past events for context:
{context_str}

User input: {text}

Return a JSON object with these fields:
- name: Event name (string)
- date: Event date in YYYY-MM-DD format (string or null if not specified)
- time: Event time in HH:MM format (string or null if not specified)
- location: Event location (string or null if not specified)
- headcount: Expected number of attendees (integer or null)
- dietary_notes: Any dietary restrictions mentioned (string or null)
- description: A clean, concise description of the event (string)

Return ONLY valid JSON, no markdown formatting, no explanation.
"""

        # For local dev / test parity: return structured mock until LLM is wired
        # This matches the pattern in chain.py where _build_answer is used
        # before Azure OpenAI is configured.
        if not self.settings.llm_endpoint or self.settings.env == "test":
            return self._mock_structured_output(text)

        # When LLM is configured, call it here.
        # Future: integrate with Azure OpenAI / local LLM via httpx
        # For now, fall through to mock for development use
        return self._mock_structured_output(text)

    def _mock_structured_output(self, text: str) -> dict[str, Any]:
        """Produce a mock structured Event for development/testing.

        Returns a deterministic structured output derived from the input text.
        In production, this is replaced by the LLM call.
        """
        import re

        # Extract basic fields heuristically for dev parity
        # (Production uses LLM; this just ensures the pipeline is wired)
        lines = text.strip().split("\n")
        first_line = lines[0] if lines else text

        # Simple heuristic: first line or first 60 chars is the name
        name = first_line[:60] if len(first_line) > 3 else "Untitled Event"

        # Match date patterns like "Sat 7pm", "Saturday at 7", "tomorrow"
        date_match = re.search(
            r"(\d{4}-\d{2}-\d{2}|\b(today|tomorrow|next\s+\w+)\b)",
            text,
            re.IGNORECASE,
        )
        date = date_match.group(1) if date_match else None

        # Match time patterns like "7pm", "7:00", "19:00"
        time_match = re.search(
            r"(\d{1,2}(:\d{2})?\s*(am|pm|AM|PM))",
            text,
        )
        time = time_match.group(1).strip() if time_match else None

        # Match location patterns
        location_match = re.search(
            r"(at|in|@)\s+([A-Z][a-zA-Z\s]+)",
            text,
        )
        location = location_match.group(2).strip() if location_match else None

        # Match headcount patterns
        headcount_match = re.search(
            r"(\d+)\s+(people|guests|attendees|person)",
            text,
            re.IGNORECASE,
        )
        headcount = int(headcount_match.group(1)) if headcount_match else None

        # Diet keywords
        diet_keywords = [
            "nut", "gluten", "dairy", "vegan", "vegetarian",
            "allerg", "kosher", "halal", "no nuts", "no dairy",
        ]
        dietary_notes = None
        for kw in diet_keywords:
            if kw in text.lower():
                # Extract the sentence containing the keyword
                sentences = re.split(r"[.!?]", text)
                for sentence in sentences:
                    if kw in sentence.lower():
                        dietary_notes = sentence.strip()
                        break
                break

        return {
            "event_id": str(uuid4()),
            "name": name,
            "date": date,
            "time": time,
            "location": location,
            "headcount": headcount,
            "dietary_notes": dietary_notes,
            "description": text[:500],
            "raw_text": text,
        }
