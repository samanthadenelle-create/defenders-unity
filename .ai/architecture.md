# Orchestration architecture

The local orchestration service is in `tools/hybrid-orchestrator`.
Deterministic tasks use code. Approved work orders use a local worker. Unplanned
development uses a configurable reasoning provider followed by the worker.
Edits occur in detached Git worktrees at a recorded commit. The service returns
patches for review and never merges or publishes them automatically.

This file describes development tooling, not the game's architecture. Existing
project design and source documents remain the authority for gameplay.
