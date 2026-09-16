# Review of the advisor packet response

Source: Desktop/review.md, modified 2026-09-11 11:12:29, 37,814 bytes. Reviewed against the live worktree, not assumed to describe current HEAD. This document is guidance and evidence, not release approval.

## Overall decision

Accept the five areas of work. Correct the implementation details below before using the response as a specification. It usefully recognizes the existing persistence oracle, separates template identity from ownership identity, rejects invented capture damage, and keeps practice outside the wager economy. It is not yet an executable recovery design.

## Already further along than the shared snapshot

- Stable GUID template IDs are now baked into both saved scenes for all 221 structures. Separate owned instance IDs already exist; do not rename the persisted instanceId field. Sibling reorder, duplicate-ID rejection, changed-parent-frame rejection, and actual capture/pose round-trip passed focused Unity checks. IDs survive rename; parent coordinate changes require migration. Source geometry was not regenerated.
- Movable tower footprints now use runtime carving, with collider-derived local bounds. Five-scene bake and final raid route checks passed. These do not establish live carving correctness after a player edit.
- Pristine inspection and first-repair selection are now implemented. If no tower stands, capture funding and the repair button select the same cheapest damaged tower. Otherwise they select the cheapest damaged structure. Focused Unity proof passed at 11:13:38 in Builds/release-restoration-edges.log (zero compiler errors), including actual census total destruction, funded tower repair, unchanged other ruins, and pristine state inspection. No broader Play/device acceptance is implied.

## Corrections required

1. **Pending victory needs an actual restart consumer.** A data field and three save wrappers do not make HandleVictory run on boot. Record the immutable settled payload, validate its internal receipt/template/structure identities, define boot recovery after GameStateService load, and consume that payload without needing the destroyed raid scene or census object. A kill before the first successful durable record does not preserve the earned victory; replaying a raid is not recovery of that victory. Current census TryCommit returns validation success for an existing town, not the refusal described in the matrix.

2. **Do not close settlement safety by disclaiming its remaining effects.** A capture-only journal can be a useful intermediate implementation, but ordinary loot, counters, cooldowns, quest events and other settlement effects still need an explicit retry/recovery policy for release. Per-day crystal/stone stamps do not establish idempotency for every effect. Prefer typed, bounded payload data to unvalidated censusJson; reject unknown stage values rather than flooring them. Schema changes must follow actual compatibility requirements: SaveMigrator explicitly documents optional additions without migration steps, so a nullable field does not universally require v42.

3. **Keep identity resolution read-only.** TryResolve must not silently mutate caller records or write saves. A legacy upgrade should resolve and validate the entire detached property, then make one explicit revisioned commit. Missing objects must preserve the original save. A baked GUID is independent of later names. Finding a reparented object does not make its old local coordinates valid; the current parentFrame guard prevents silent displacement.

4. **The layout adapter needs failure and identity semantics.** Four void methods keyed by itemId/cell are insufficient for inherited structures whose placeholder grid cells can coincide. Use stable instance IDs and explicit failure results. TryChangeInheritedPose only moves existing structures; it cannot implement add, sell or upgrade. Audit seeding, singleton/free-build checks, charges/refunds, live spawn/destroy, layout mutations and persistence together. Story isolation is not automatic from swapping three call sites. Do not expose build actions before their full operation can succeed or roll back coherently.

5. **Tower funding must be guaranteed by the selected repair.** Merely allowing a second repair does not help after the only allowance bought a wall. The same selector must drive the capture allowance and first repair. Validate required tower content before shipping or starting the raid; do not discover a template problem after an earned victory and treat refusing ownership as the normal solution. Pristine inspection advances restoration without inventing damage or spending supplies.

6. **Carving is asynchronous.** A zero stationary delay is not an immediate NavMesh update, and Physics.SyncTransforms does not prove that carving has finished. Unity documents a frame delay: https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/AboutObstacles.html . Test after the navigation update with actual agent filters and meaningful endpoints, and prove live agent traversal as well as complete paths. A path to an objective's blocked collider center is not a valid approach test. A single spawn/objective pair per agent type does not establish access to every required gameplay interaction.

7. **The proposed rollback is rejected by our own revision guard.** TryAcceptRevision refuses an older revision, so recommitting the previous property after a failed move is not a three-line rollback. Validate a reversible live preview before durable commit, restore that preview on failure, and prevent simultaneous edits during validation. If a durable corrective operation is required, it must be a new revision with explicit milestone semantics. Reconstruction applies saved poses; it does not automatically detect that a previous transform assignment failed.

8. **A snapshot copier is not a scene importer.** Practice needs a validated detached contract plus actual spawning/reconstruction, condition, faction, hero/loadout and AI setup in an isolated match scene. Loading the owned town and setting a boolean alone does not establish this. Audit all wallet/progression/outpost settlement hooks and UI, not just four calls in ArenaMode. Win/loss may complete the lesson after durable save; abandon and interruption must not. Abandon already occurs after reentry, so absence of a reentry revision is not what prevents completion.

9. **Validate the desktop journey before freezing candidate artifacts.** Compile, focused contracts and UI captures are necessary, but the release order must include desktop Play integration before final APK/AAB candidate construction, followed by artifact and device validation. The response places the desktop journey after signing; any fixes there would invalidate the candidate.

## Useful next advisor request

Please solve one bounded seam: specify the exact pending-victory restart state machine using the current save provider and raid settlement code. Show the boot consumer, immutable payload validation, write-failure and kill recovery at each boundary, and how ordinary rewards/counters avoid duplication or loss. Do not assume HandleVictory reruns on boot, do not call replaying the whole raid recovery, and do not re-propose IDs or tower baking already completed. Provide concrete method-level changes and failure-injection acceptance cases; keep all unproven effects explicit.

The owner does not need to approve routine component naming, a redundant instanceId rename, or the already authorized tower lesson. Agent filters and practice ruleset selection are implementation questions to resolve from the project.
