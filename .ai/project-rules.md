# Orchestrator project rules

- Never overwrite or reset unrelated working changes.
- Never modify paths outside the operator scope and the work order's narrower scope.
- Never change objectives, acceptance criteria or requirements to make a failed task pass.
- Never perform merges, pushes, publishing, deployments, payments, credential changes,
  external communications or database migrations through worker tools.
- Stop at the configured attempt and paid-call limits.
- Preserve the diff before removing each temporary worktree.
- Surface missing credentials, unavailable runners, and insufficient validation honestly.
