# Tooling decisions

2026-09-13: Added Python/FastAPI/Pydantic/SQLite orchestration, configurable OpenAI
Responses and Ollama providers, and a local-work-order entry point.

The initial worker candidate is gpt-oss:20b. It is not a permanent model selection.
Native Ollama tool calling is used for worker actions. Structured JSON is used
for planning and review. Every action is validated by the host.

General command execution is disabled until an operator supplies an isolated
runner. A Git worktree alone is not an operating-system sandbox.

Execution uses the recorded committed baseline; current uncommitted game work
is not silently copied into a worker checkout.
