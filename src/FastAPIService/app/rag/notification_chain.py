"""Notification summarization chain — W7 (M5.13).

Generates personalized event notification summaries with per-tenant tone control.
Uses SSE streaming with prompt-version tracking for audit trail.

Follows the same pattern as IngestChain and KendoRAGChain.
"""

from __future__ import annotations

import json
from typing import Any, AsyncGenerator
from uuid import uuid4

from app.config import Settings
from app.rag.prompts import get_prompt_template


# Registered tone profiles — maps tone slug to prompt instruction suffix
TONE_PROFILES: dict[str, str] = {
    "professional": (
        "Write in a professional, formal tone. Use complete sentences and "
        "avoid casual language. Address the recipient respectfully."
    ),
    "friendly": (
        "Write in a warm, friendly tone. Use conversational language and "
        "a welcoming tone. Make the recipient feel personally addressed."
    ),
    "urgent": (
        "Write in an urgent, direct tone. Prioritize key information and "
        "action items. Be concise and clear about what needs attention."
    ),
}

NOTIFICATION_PROMPT_TEMPLATE: str = """You are a Kendo notification assistant.
Generate a personalized event notification for the user based on the event details below.

Event details:
{event_context}

User preferences:
{user_context}

Tone profile: {tone}
{tone_instruction}

Instructions:
- Generate a concise notification body (2-3 paragraphs max).
- Include the event name, date/time, and key details.
- Address the user by name if available.
- Do not fabricate information not present in the event details.
- End with a clear next-step or call to action.

Notification body:"""

NOTIFICATION_PROMPT_VERSION = "w7-notification-v1"


class NotificationSummarizationChain:
    """Chain that generates personalized event notification summaries.

    Pipeline: build context -> format prompt -> stream LLM output -> yield chunks.
    Supports per-tenant tone control and prompt-version tracking for audit.
    """

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self._prompt_version = NOTIFICATION_PROMPT_VERSION

    @property
    def prompt_version(self) -> str:
        """Return the current prompt version for audit tracking."""
        return self._prompt_version

    async def summarize(
        self,
        event_context: dict[str, Any],
        user_context: dict[str, Any] | None = None,
        tone: str = "friendly",
    ) -> AsyncGenerator[dict[str, Any], None]:
        """Stream a personalized notification summary via SSE chunks.

        Args:
            event_context: Event details dict (name, date, time, location, description, etc.).
            user_context: Optional user profile dict (name, preferences, etc.).
            tone: Tone profile key — one of "professional", "friendly", "urgent".

        Yields:
            Dict with keys suitable for SSE serialization:
            - {"text": "...", "token_count": N} for chunk events
            - Final "done" event with full metadata
            - "error" event on failure
        """
        tone = tone.lower() if tone else "friendly"
        if tone not in TONE_PROFILES:
            tone = "friendly"

        tone_instruction = TONE_PROFILES[tone]

        # Format event context as structured text
        event_lines = []
        for key, value in event_context.items():
            if value is not None:
                event_lines.append(f"{key}: {value}")
        event_text = "\n".join(event_lines) if event_lines else "No event details provided."

        # Format user context
        user_text = ""
        if user_context:
            user_lines = []
            for key, value in user_context.items():
                if value is not None:
                    user_lines.append(f"{key}: {value}")
            user_text = "\n".join(user_lines) if user_lines else "No user preferences."

        # Build the prompt
        prompt = NOTIFICATION_PROMPT_TEMPLATE.format(
            event_context=event_text,
            user_context=user_text if user_text else "No user preferences available.",
            tone=tone,
            tone_instruction=tone_instruction,
        )

        trace_id = str(uuid4())

        # In production, this would call the LLM and stream tokens.
        # For the initial implementation, generate a mock streaming response
        # that demonstrates the SSE contract without requiring Azure OpenAI.
        mock_body = self._generate_mock_notification(event_context, user_context, tone)

        # Stream the response in chunks (simulated)
        words = mock_body.split(" ")
        for i, word in enumerate(words):
            chunk_text = word + (" " if i < len(words) - 1 else "")
            yield {
                "text": chunk_text,
                "token_count": i + 1,
            }

        # Final done event
        yield {
            "notification_body": mock_body,
            "prompt_version": self._prompt_version,
            "total_tokens": len(words),
            "trace_id": trace_id,
            "_done": True,
        }

    def _generate_mock_notification(
        self,
        event_context: dict[str, Any],
        user_context: dict[str, Any] | None = None,
        tone: str = "friendly",
    ) -> str:
        """Generate a mock notification body for testing.

        In production, this is replaced by LLM streaming.
        The mock output demonstrates the expected response shape.
        """
        event_name = event_context.get("name", "your event")
        event_date = event_context.get("date", "upcoming date")
        event_time = event_context.get("time", "scheduled time")
        user_name = (user_context or {}).get("name", "there")

        if tone == "professional":
            body = (
                f"Dear {user_name},\n\n"
                f"This is a notification regarding '{event_name}', "
                f"scheduled for {event_date} at {event_time}. "
                f"We are pleased to confirm the arrangements and look forward to your participation. "
                f"Please review the details and contact support if any changes are required.\n\n"
                f"Best regards,\nKendo Platform"
            )
        elif tone == "urgent":
            body = (
                f"URGENT: {user_name},\n\n"
                f"Your event '{event_name}' on {event_date} at {event_time} requires immediate attention. "
                f"Please confirm your availability and review the latest updates. "
                f"Action may be required to avoid schedule conflicts.\n\n"
                f"Respond as soon as possible.\nKendo Platform"
            )
        else:
            body = (
                f"Hey {user_name}! 🌟\n\n"
                f"Just a quick heads-up about '{event_name}' — "
                f"it's coming up on {event_date} at {event_time}. "
                f"We're super excited and hope you are too! "
                f"Everything is set and ready to go.\n\n"
                f"Can't wait to see you there! 🎉\n"
                f"Best,\nThe Kendo Team"
            )

        return body

    async def summarize_blocking(
        self,
        event_context: dict[str, Any],
        user_context: dict[str, Any] | None = None,
        tone: str = "friendly",
    ) -> dict[str, Any]:
        """Non-streaming version — collects all chunks and returns the final result.

        Useful for testing and for callers that don't support SSE.
        """
        notification_body = ""
        final_result: dict[str, Any] = {}

        async for chunk in self.summarize(event_context, user_context, tone):
            if chunk.get("_done"):
                final_result = chunk
            else:
                notification_body += chunk.get("text", "")

        if final_result:
            final_result["notification_body"] = notification_body
            return final_result

        return {
            "notification_body": notification_body,
            "prompt_version": self._prompt_version,
            "total_tokens": 0,
            "trace_id": str(uuid4()),
        }
