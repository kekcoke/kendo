"""Corpus index for W6 Document Q&A / Onboarding Assistant (M5.12).

Scans the project corpus (docs/architecture/, ops/runbooks/, templates/skills/,
changelog/, .ai/orchestration.md), chunks documents with overlap, embeds them
using the existing BGE embedding client, and persists to disk.

Design decisions:
- Local vector store (in-memory dict + numpy + pickle) — no external network
  dependency, no circuit breaker needed. Persisted to /var/kendo/assistant_index/.
- Atomic rebuild: build new index in temp dir, swap atomically on success.
- Chunking: 500 chars with 50-char overlap for continuity.
- Corpus paths are resolved relative to the project root (KENDO_ROOT or pwd).
"""

from __future__ import annotations

import json
import os
import pickle
import shutil
import tempfile
from pathlib import Path
from typing import Any

import numpy as np

from app.config import Settings
from app.rag.embeddings_client import BaseEmbeddingClient, create_embedding_client

# Default corpus root — resolved relative to project root
DEFAULT_CORPUS_ROOT = os.path.abspath(
    os.environ.get("KENDO_ROOT", os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
)

# Index persistence path
DEFAULT_INDEX_DIR = os.environ.get(
    "KENDO_ASSISTANT_INDEX",
    "/var/kendo/assistant_index/",
)

# Chunking parameters
CHUNK_SIZE = 500
CHUNK_OVERLAP = 50

# Corpus glob patterns (relative to corpus root)
CORPUS_PATTERNS: list[str] = [
    "docs/architecture/day_*.md",
    "ops/runbooks/*.md",
    "templates/skills/*.md",
    "changelog/*.md",
    ".ai/orchestration.md",
]

# Excluded patterns
EXCLUDED_PATTERNS: list[str] = [
    ".ai/current_state.md",
    ".ai/entrypoint.md",
    "templates/agents/",
]


class AssistantIndex:
    """Corpus index for the Document Q&A assistant.

    Scans project corpus files, chunks them, embeds chunks via the configured
    embedding client, and stores them for similarity search.

    The index is an in-memory dict:
      {
        "chunks": [
          {"id": str, "text": str, "source": str, "line_start": int, "line_end": int},
        ],
        "embeddings": np.ndarray of shape (n_chunks, embedding_dim)
      }
    """

    def __init__(
        self,
        settings: Settings,
        corpus_root: str | None = None,
        index_dir: str | None = None,
    ) -> None:
        self.settings = settings
        self.embedding_client: BaseEmbeddingClient = create_embedding_client(settings)
        self.corpus_root = corpus_root or DEFAULT_CORPUS_ROOT
        self.index_dir = index_dir or DEFAULT_INDEX_DIR

        # In-memory index state
        self._chunks: list[dict[str, Any]] = []
        self._embeddings: np.ndarray | None = None
        self._ready = False

    @property
    def is_ready(self) -> bool:
        """Check if the index is loaded and queryable."""
        return self._ready and len(self._chunks) > 0 and self._embeddings is not None

    @property
    def chunk_count(self) -> int:
        """Number of indexed chunks."""
        return len(self._chunks)

    async def load_or_build(self) -> None:
        """Load existing index from disk, or build from scratch if missing."""
        index_path = os.path.join(self.index_dir, "index.pkl")
        if os.path.exists(index_path):
            try:
                await self._load(index_path)
                return
            except Exception:
                pass  # Fall through to rebuild

        await self.rebuild()

    async def rebuild(self) -> None:
        """Scan corpus, chunk, embed, and persist atomically.

        Builds the index in a temp directory, then atomically swaps it
        into the target index_dir. This prevents partial/corrupt indexes.
        """
        # 1. Scan and chunk corpus
        chunks = self._scan_and_chunk()

        if not chunks:
            self._chunks = []
            self._embeddings = None
            self._ready = False
            return

        # 2. Embed all chunks
        texts = [c["text"] for c in chunks]
        embeddings = await self.embedding_client.embed_documents(texts)
        embedding_array = np.array(embeddings, dtype=np.float32)

        # 3. Persist atomically
        os.makedirs(self.index_dir, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=self.index_dir) as tmpdir:
            tmp_path = os.path.join(tmpdir, "index.pkl")
            with open(tmp_path, "wb") as f:
                pickle.dump({
                    "chunks": chunks,
                    "embeddings": embedding_array,
                }, f)

            # Atomic swap
            final_path = os.path.join(self.index_dir, "index.pkl")
            shutil.move(tmp_path, final_path)

        # 4. Update in-memory state
        self._chunks = chunks
        self._embeddings = embedding_array
        self._ready = True

    async def search(
        self,
        query_text: str,
        top_k: int = 5,
    ) -> list[dict[str, Any]]:
        """Search the index for chunks similar to the query.

        Args:
            query_text: Natural language query string.
            top_k: Number of results to return (default 5).

        Returns:
            List of dicts with id, text, source, line_start, line_end, score.
        """
        if not self.is_ready:
            return []

        query_embedding = await self.embedding_client.embed_query(query_text)
        query_array = np.array(query_embedding, dtype=np.float32)

        # Cosine similarity
        scores = np.dot(self._embeddings, query_array) / (
            np.linalg.norm(self._embeddings, axis=1) * np.linalg.norm(query_array) + 1e-10
        )

        top_indices = np.argsort(scores)[-top_k:][::-1]

        results = []
        for idx in top_indices:
            if scores[idx] > 0.1:  # Relevance floor
                chunk = self._chunks[idx]
                results.append({
                    **chunk,
                    "score": float(scores[idx]),
                })

        return results

    def _scan_and_chunk(self) -> list[dict[str, Any]]:
        """Scan corpus files and split into chunks with line ranges."""
        chunks: list[dict[str, Any]] = []
        chunk_id = 0

        for pattern in CORPUS_PATTERNS:
            # Resolve glob relative to corpus_root
            full_pattern = os.path.join(self.corpus_root, pattern)
            matched_files = sorted(Path(self.corpus_root).glob(pattern))

            for file_path in matched_files:
                rel_path = str(file_path.relative_to(self.corpus_root))

                # Check exclusions
                if self._is_excluded(rel_path):
                    continue

                try:
                    content = file_path.read_text(encoding="utf-8")
                except (OSError, UnicodeDecodeError):
                    continue

                lines = content.split("\n")
                file_chunks = self._chunk_text(content, rel_path, lines)

                for chunk_text, line_start, line_end in file_chunks:
                    chunks.append({
                        "id": f"chunk-{chunk_id}",
                        "text": chunk_text,
                        "source": rel_path,
                        "line_start": line_start,
                        "line_end": line_end,
                    })
                    chunk_id += 1

        return chunks

    def _chunk_text(
        self,
        text: str,
        source: str,
        lines: list[str],
    ) -> list[tuple[str, int, int]]:
        """Split text into chunks of CHUNK_SIZE with CHUNK_OVERLAP.

        Returns list of (chunk_text, line_start, line_end) tuples.
        Line ranges are approximate — computed from character position.
        """
        if len(text) <= CHUNK_SIZE:
            return [(text, 1, len(lines))]

        chunks: list[tuple[str, int, int]] = []
        start = 0

        while start < len(text):
            end = min(start + CHUNK_SIZE, len(text))

            # Try to break at a newline for clean splits
            if end < len(text):
                newline_pos = text.rfind("\n", start, end)
                if newline_pos > start + CHUNK_SIZE // 2:
                    end = newline_pos + 1

            chunk_text = text[start:end]

            # Approximate line ranges
            line_start = text[:start].count("\n") + 1
            line_end = text[:end].count("\n") + 1

            chunks.append((chunk_text, line_start, line_end))

            # Advance with overlap
            next_start = end - CHUNK_OVERLAP
            if next_start <= start:
                next_start = end
            start = next_start

            # Safety — prevent infinite loop on tiny files
            if start >= len(text):
                break

        return chunks

    @staticmethod
    def _is_excluded(rel_path: str) -> bool:
        """Check if a relative path matches any exclusion pattern."""
        for pattern in EXCLUDED_PATTERNS:
            if pattern.endswith("/"):
                # Directory exclusion
                if rel_path.startswith(pattern):
                    return True
            elif rel_path == pattern:
                return True
        return False

    async def _load(self, index_path: str) -> None:
        """Load a persisted index from disk."""
        with open(index_path, "rb") as f:
            data = pickle.load(f)

        self._chunks = data["chunks"]
        self._embeddings = data["embeddings"]
        self._ready = True
