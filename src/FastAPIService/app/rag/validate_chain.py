"""Event Validation reasoning chain — W2 (M5.8).

Multi-step reasoning chain for detecting scheduling conflicts:
1. Parse event date/time, headcount, location from structured Event JSON
2. Retrieve user's recent events from pgvector
3. Run conflict detection: date overlap, headcount plausibility, recurring collision
4. Generate suggestions when conflicts found
5. Return ValidationResult with reasoning trace
"""

from __future__ import annotations

import json
from datetime import datetime
from typing import Any

from app.config import Settings
from app.rag.embeddings_client import create_embedding_client
from app.rag.prompts import get_prompt_template


class ValidateChain:
    """Chain that validates candidate events for scheduling conflicts.

    Pipeline: parse event -> embed -> pgvector retrieval -> conflict detection -> suggestions.
    Uses the same embedding client as IngestChain for model parity.
    """

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.embedding_client = create_embedding_client(settings)
        self._prompt_version = "v1"

    async def validate(
        self,
        event: dict[str, Any],
        user_id: str,
    ) -> dict[str, Any]:
        """Validate a candidate event and return conflict detection results.

        Args:
            event: Structured Event JSON from W1 ingestion.
            user_id: JWT subject ID for context seeding.

        Returns:
            ValidationResult dict with:
              - ok: bool (no conflicts found)
              - conflicts: list[str]
              - suggestions: list[str]
              - reasoning_trace: list[dict] with step and result
        """
        trace: list[dict[str, str]] = []

        # 1. Parse candidate event fields
        event_name = event.get("name", "Untitled Event")
        event_date = event.get("date")
        event_time = event.get("time")
        event_location = event.get("location")
        event_headcount = event.get("headcount")

        # 2. Embed the event for pgvector retrieval
        embed_text = f"{event_name} {event_date or ''} {event_time or ''} {event_location or ''} {event_headcount or ''}"
        embedding = await self.embedding_client.embed_query(embed_text)

        # 3. Retrieve user's recent events from pgvector
        from app.repositories.embeddings import similarity_search

        recent_events = await similarity_search(embedding, top_k=10)

        conflicts: list[str] = []
        suggestions: list[str] = []

        # 4. Run conflict detection tools
        # Tool 1: Date/time overlap check
        date_result = self._check_date_overlap(
            event_date, event_time, event_name, recent_events
        )
        trace.append({"step": "check_date_overlap", "result": date_result})
        if date_result != "no conflict":
            conflicts.append(date_result)
            suggestions.append(f"Consider rescheduling '{event_name}' to a different date/time slot")

        # Tool 2: Headcount plausibility check
        headcount_result = self._check_headcount_plausibility(
            event_headcount, event_location, recent_events
        )
        trace.append({"step": "check_headcount_plausibility", "result": headcount_result})
        if headcount_result != "headcount is reasonable":
            conflicts.append(headcount_result)
            suggestions.append(f"Consider a larger venue for '{event_name}' or reducing headcount")

        # Tool 3: Recurring event collision check
        recurring_result = self._check_recurring_collision(
            event_date, event_time, event_name, recent_events
        )
        trace.append({"step": "check_recurring_collision", "result": recurring_result})
        if recurring_result != "no recurring collision":
            conflicts.append(recurring_result)
            suggestions.append("Check if this conflicts with a recurring event schedule")

        # 5. Generate fallback suggestions if no tool-specific suggestion exists
        if not suggestions:
            suggestions.append("No conflicts detected — event can be scheduled.")

        return {
            "ok": len(conflicts) == 0,
            "conflicts": conflicts,
            "suggestions": suggestions,
            "reasoning_trace": trace,
        }

    def _check_date_overlap(
        self,
        event_date: str | None,
        event_time: str | None,
        event_name: str,
        recent_events: list[dict[str, Any]],
    ) -> str:
        """Check if candidate event's date/time overlaps with existing events."""
        if not event_date and not event_time:
            return "no conflict (no date/time specified)"

        for past_event in recent_events:
            past_text = past_event.get("text", "").lower()
            if event_date and event_date.lower() in past_text:
                return f"Date conflict: '{event_name}' shares a date with a past event"
            if event_time and event_time.lower() in past_text:
                return f"Time conflict: '{event_name}' shares a time slot with a past event"

        return "no conflict"

    def _check_headcount_plausibility(
        self,
        headcount: int | None,
        location: str | None,
        recent_events: list[dict[str, Any]],
    ) -> str:
        """Check if headcount is plausible given location and past events."""
        if headcount is None:
            return "headcount is reasonable (not specified)"

        # Check if location was used in past events with similar headcounts
        if location:
            for past_event in recent_events:
                past_text = past_event.get("text", "").lower()
                if location.lower() in past_text:
                    # Rough heuristic: past events at this location with similar scale
                    return "headcount is reasonable"

        # If headcount is very large without location, flag it
        if headcount > 200:
            return f"Headcount {headcount} may be too large — no venue specified"

        return "headcount is reasonable"

    def _check_recurring_collision(
        self,
        event_date: str | None,
        event_time: str | None,
        event_name: str,
        recent_events: list[dict[str, Any]],
    ) -> str:
        """Check if candidate event collides with recurring event patterns."""
        if not event_date and not event_time:
            return "no recurring collision"

        for past_event in recent_events:
            past_text = past_event.get("text", "").lower()
            # Check for recurring patterns like "every Tuesday", "weekly", "daily"
            recurring_indicators = ["every", "weekly", "daily", "monthly", "bi-weekly"]
            has_recurring = any(indicator in past_text for indicator in recurring_indicators)

            if has_recurring:
                if event_date and event_date.lower() in past_text:
                    return f"Recurring collision: '{event_name}' overlaps with a recurring event"
                if event_time and event_time.lower() in past_text:
                    return f"Recurring collision: '{event_name}' time slot overlaps with a recurring event"

        return "no recurring collision"

    def _mock_validation_result(
        self,
        event: dict[str, Any],
        user_id: str,
    ) -> dict[str, Any]:
        """Produce a mock validation result for development/testing.

        For M5.8 dev parity, provides deterministic validation output.
        In production, replaced by the full langgraph chain.
        """
        # Simple mock: determine if conflict exists based on event properties
        name = event.get("name", "").lower()
        headcount = event.get("headcount")

        conflicts: list[str] = []
        trace: list[dict[str, str]] = []

        if "birthday" in name or "party" in name:
            conflicts.append("Potential date conflict with existing events")
            trace.append({"step": "check_date_overlap", "result": "date overlap detected"})
        else:
            trace.append({"step": "check_date_overlap", "result": "no conflict"})

        if headcount and headcount > 100:
            conflicts.append(f"Headcount {headcount} may exceed venue capacity")
            trace.append({"step": "check_headcount_plausibility", "result": "headcount too large"})
        else:
            trace.append({"step": "check_headcount_plausibility", "result": "headcount is reasonable"})

        trace.append({"step": "check_recurring_collision", "result": "no recurring collision"})

        return {
            "ok": len(conflicts) == 0,
            "conflicts": conflicts,
            "suggestions": ["Consider a different date/time" if conflicts else "No conflicts detected"],
            "reasoning_trace": trace,
        }
