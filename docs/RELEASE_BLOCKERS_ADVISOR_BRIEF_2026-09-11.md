# Release blockers: request for architectural advice

## Objective

Ship validated Android APK and Google Play AAB for this complete journey: real Iron Bastion victory -> separate damaged personal town -> resumable repair/design/save/reentry lessons -> AI practice using the same versioned build contract. Preserve existing saves and the Chapter One castle. Practice must have no wager or ranked payout. Do not publish stores or redesign the approved raid layout.

The owner is cost constrained. Recommend the smallest complete implementation using existing systems. Please give decisions and their tradeoffs, not a new framework, broad cleanup, or a longer test checklist.

## What exists and what is not proven

The final raid layout is owner-approved. Navigation bake, saved route, and isolated HUD captures have passed. A full regression run passed 508/508 before the latest capture/town changes; those later changes have focused compile and state checks only.

There are additive owned-town save records, revision validation, save-failure rollback, a precombat census, final-victory capture calls, a separate scene template, condition/pose reconstruction, repair and tower-move services, a first UI panel, and contextual tutorial steps. Reconstruction checks covered 210 walls, 10 towers and one spire. These are implementation pieces, not proof of the playable journey.

The final victory still returns to the castle. Town entry/hero seating, the new UI, destroyed-structure behavior, and the complete restart journey have not been verified in Play. No new APK/AAB has been built. The current arena does not consume the new snapshot contract.

## 1. Durable victory settlement

Capture commits an OwnedBase revision through GameStateService. An in-memory receipt and settled census support retry while the victory controller survives. A failed capture prevents its Return action from discarding that in-memory record. Existing raid loot, claims and victory counts still use their separate save calls.

Gap: there is no durable pending-victory record containing the capture census. Killing the app before capture commits can lose the recovery information. The entire victory/capture/reward operation is not one proven restart-safe transaction.

Question: should the existing persisted envelope gain a pending victory record with a receipt and completion stages, or should all settlement changes be composed and written once? What is the minimum design that prevents lost ownership and duplicate rewards across termination at each boundary?

## 2. One authoritative editable build

Story build mode writes GameState.BaseLayout. The owned town uses a separate OwnedBase.structures list. Its inherited geometry includes exact local poses and sibling-index addresses into a dated scene template; ordinary grid coordinates cannot preserve the fitted walls. The owned-scene loader is isolated, but the existing BuildModeController has not been adapted to this ownership context.

Gap: source sibling addresses are fragile across future hierarchy changes. The new design service moves towers independently of the existing build-mode persistence path. ArenaBuildSnapshot is a validated DTO contract without a live importer for this town.

Question: what small layout-context adapter should let the existing editor, reconstruction and arena consume one build revision while preserving story saves and inherited geometry? Should inherited objects receive baked stable IDs now, with a template migration policy, instead of depending on sibling positions?

## 3. Every legitimate capture must have a playable lesson path

Current assistance covers the cheapest damaged structure. Design requires a standing tower. Milestones and supplies persist in the owned record; contextual tutorial hints use the existing interpreter.

Gaps: a garrison-only win can leave no damage to repair. A destructive win can leave no standing tower to move. Repairing the cheapest wall then does not guarantee the design lesson is possible. The current first controls offer west/east moves rather than the full existing build editor. Selection feedback, live reentry, and repeat town access remain unverified. No practice outcome is connected yet.

Question: what explicit branch should handle pristine capture and total destruction without inventing damage, granting unlimited supplies, or awarding a lesson merely because a panel was dismissed? Which minimum repair/rebuild guarantee makes every captured town eligible for a meaningful design choice?

## 4. Navigation must follow actual destruction and edits

Raid walls now use carving obstacles instead of permanent baked wall holes. The saved route passes, but live intact-wall -> combat breach traversal is not proven. Towers still leave their baked footprints when repositioned by the new design service. A NavMesh destination check and collider overlap check do not prove the whole town or arena remains connected.

Question: should movable/destroyable defenses all carve a fixed terrain NavMesh, with static baking limited to permanent terrain/buildings? Which runtime path checks must gate a saved design or practice start, without requiring a full runtime bake after every edit?

## 5. Practice mode and release acceptance

The existing ArenaMode.TryStartRaid always calls the wager debit path and constructs opponents through its existing generator. A zero wager would still execute the wrong economic path. The shared build snapshot currently has no gameplay adapter. Hero placement, completion on win or loss, return to the same town, and no balance changes need implementation and proof.

Question: should practice be an explicit mode in the existing arena lifecycle, using one snapshot importer and an entirely separate no-wallet settlement branch? Specify the minimum boundaries and interruption/retry behavior; do not add matchmaking or ranked features.

After these seams work, the candidate still needs current full gates, Play journey/visual checks, signed APK/AAB builds, content/version/signing checks, and Play-specific crypto exclusion. Device acceptance remains unproven. These are required delivery work, not evidence that the Android build toolchain is currently broken.

## Advice requested

Please return: (1) recommended decisions for the five questions; (2) the shortest dependency order; (3) changes you would avoid; (4) the few end-to-end observations that would establish readiness. Do not infer completion from isolated green tests.
