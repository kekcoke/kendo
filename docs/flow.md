[Unstructured Event Request] (e.g., Markdown/Text)
                            │
                            ▼
┌────────────────────────────────────────────────────────────────────────┐
│ .NET 10 Web API (Semantic Kernel C# Engine)                            │
│                                                                        │
│  1. Ingestion: Chunks text & generates embeddings via Azure OpenAI     │
│  2. Vector DB: Stores & retrieves event context from Qdrant/pgvector  │
│  3. Agent: Uses Chat Completion to output structured Event JSON        │
└───────────────────────────┬────────────────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Observability & Evaluation Gate                                        │
│  - OpenTelemetry logs traces to console/dashboard                      │
│  - xUnit test suite simulating DeepEval/PromptFlow checks              │
└────────────────────────────────────────────────────────────────────────┘


The 10-Hour Sprint Breakdown (C# Focus)Hours 1–3: The Core Engine SetupSpin up a minimal .NET 10 Web API.  Pull in the Microsoft.SemanticKernel NuGet package.Set up a local vector database container (like Qdrant or pgvector) using Docker.Hours 4–6: The RAG & Data Extraction PipelineWrite a service that takes an unstructured text description of an event, uses Semantic Kernel to chunk it, generates embeddings, and saves it to your vector database.Create a Semantic Kernel Plugin/Agent that queries the vector database, pulls the context, and uses an LLM (via an OpenAI or Azure OpenAI API key) to output a cleanly formatted JSON payload representing the scheduled event.Hours 7–8: Tracing & Mock EvaluationTurn on OpenTelemetry logging within your .NET 10 app. Semantic Kernel has built-in support for this, allowing you to trace prompt tokens and execution steps directly in your console or a light dashboard.Write an xUnit test that passes a few test inputs and asserts that the LLM output matches expected criteria (simulating the "AI quality gates" mentioned in the JD).  Hours 9–10: Documentation & Resume UpdateDraft a clean README.md and your own "AI-Native SDLC Playbook" detailing how you used GitHub Copilot/Claude Code to quickly generate the boilerplate for the Semantic Kernel implementation.  