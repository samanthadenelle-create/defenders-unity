# WO-1705 result — final raid capture, owned town, and AI practice

**Status: validation in progress; owner acceptance remains open.**

This review preserves the existing final-tier capture implementation and the owner's
original Tripo castle layout. It does not enable authored Barracks movement or replace
the Chapter One castle with the captured town. Release execution belongs to root's
current authorized release pass; this document does not claim deployment or device proof.

## Current implementation and acceptance mapping

| Owner outcome | Implementation | Executable coverage |
| --- | --- | --- |
| Only the highest raid, fully cleared for three stars, grants a town | `OwnedBaseProgression.TryCapture`, victory settlement, `RaidCaptureCensus` | `OwnedBaseStateRegression`, `OwnedTownScenePoseProof` |
| Inherit surviving condition and stable identities; no repeated supply grant | Frozen census, capture/supply receipts, pending-capture recovery, revision commits | `OwnedBaseStateRegression`, `OwnedTownScenePoseProof` |
| Repair with the one-time essential envelope and normal materials afterward | `OwnedTownRepairService`, supply-first durable repair commit | `OwnedTownRepairPaymentProof`, actual repair buttons in `OwnedTownMovePlayProof` |
| Design, save, and reenter without touching the story layout | Dedicated owned-town scene, layout snapshot/import, construction/move services | `OwnedTownReconstructionProof`, `OwnedTownConstructionRulesProof`, `OwnedTownMovePlayProof` |
| Resumable FTUE advances from successful gameplay operations | Saved milestones plus tutorial service signals after successful commits | `OwnedBaseStateRegression`, `OwnedTownReleaseContractProof`, live move/build/repair/reentry proof |
| AI practice uses a frozen versioned saved build; loss completes the lesson, abandonment does not | `OwnedTownPracticeSession`, real practice scene controller and combat policy | `OwnedBaseStateRegression`, live three-attacker win/save-retry/loss/abandon proof |

The AI match is local practice, with no wager, ranked award, or claim of server-verified
combat. Live player matchmaking and certification remain outside this change.

## Local cloud-boundary verification

Reviewed the existing dirty `api/game/save.js` and `api/game/load.js` changes for the
owned-town checkpoint. Save excludes canonical/PascalCase pending capture recovery
intents from the cloud delta; load removes those fields from its response without
mutating the stored record. Committed `ownedBase`, story `baseLayout`, and construction
queue remain separate persisted fields. These narrowly scoped changes are suitable
for root's Vercel preview checkpoint together with their tests.

Executed locally on Node v24.11.1:

```text
node --test test/game.save.owned-town.test.js test/game.save.reset-epoch.test.js test/game.load.test.js test/game.save.schema-version.test.js
```

**37 passed, 0 failed, 0 skipped**, exit 0. Evidence:
`Builds/night-owned-town-api-tests.log`. SQL/auth/audit handler tests use local stubs;
the mapping/schema tests call pure exported functions. No network/database writes or
deployment occurred. Existing reset epoch, schema-version, auth refusal, missing save,
and full-state load behavior remain covered by these focused runs.

This does not validate deployed Neon SQL execution or prove server-authoritative town
capture. Existing cloud blobs can still contain old recovery fields at rest because
ordinary saves shallow-merge deltas; response filtering prevents the supported fields
from returning to clients. No cleanup migration is required for this boundary change.
The current Unity backend import also explicitly clears remote pending capture intent.

## Reproduced failure and bounded correction

`OwnedTownPracticeController.ReturnToTown` previously restored `_entryHp` even when
startup never captured hero health. Missing pending-session startup can reach the return
button with `_entryHp == 0`; `HeroHealth.RestoreAfterPractice` clamps that value to one.
The real Play Mode baseline reproduced this in `Builds/night-practice-entry-baseline.log`:
`OWNED_TOWN_PRACTICE_ENTRY_FAIL ... Unstarted practice changed hero HP from 73 to 1.`
The run compiled without C# errors and exited with the expected failure.

`OwnedTownMovePlayProof.RunPracticeEntryFailure` isolates saves, enters a temporary
practice scene, executes the real missing-session startup and return callbacks, and
asserts that nontrivial hero health remains unchanged. It intentionally leaves ownership
absent so the real return router refuses navigation. This focused fixture measures health
ownership on failure, not successful town routing. The full Play proof covers that route.

The controller now records the actual hero reference when it captures entry health, then
restores only that same actor in the same practice scene. A failed entry has no captured
hero to restore. Restoration relinquishes the reference so repeated return requests cannot
overwrite later health. Capture, FTUE, rewards, saves, and town geometry are unchanged.

## Fresh validation run list

The focused post-fix Play run **passed** in `Builds/night-practice-entry-after.log`:
`OWNED_TOWN_PRACTICE_ENTRY_OK failed startup return preserves uncaptured hero health; main save unchanged`.
The remaining full-flow and release runs below remain **UNRUN for this change** until
root records their fresh outcomes.

1. Play Mode: `DeNelle.Editor.OwnedTownMovePlayProof.RunPracticeEntryFailure`.
   Baseline `OWNED_TOWN_PRACTICE_ENTRY_FAIL` and corrected
   `OWNED_TOWN_PRACTICE_ENTRY_OK` are both captured above. Driver exits Unity explicitly with code 0 or 1.
2. Editor: `DeNelle.Editor.OwnedTownScenePoseProof.Run` and
   `DeNelle.Editor.OwnedTownReconstructionProof.Run` (verify entry point names before invoking).
3. Editor: `DeNelle.Editor.OwnedTownReleaseContractProof.RunBatch`.
4. Play Mode: `DeNelle.Editor.OwnedTownMovePlayProof.Run`, requiring
   `OWNED_TOWN_MOVE_PLAY_OK` plus its real town and practice submarkers.
5. Full registered data regression and active/platform compile/build gates through root.
6. Owner test build: final raid three-star clear, single claim, restart during FTUE,
   repair, change design, save/reenter, finish AI practice, and revisit the original castle.

Historical September 11 Play logs and September 14 castle editor guards are useful
baselines, not proof for the current release tree. Current fresh logs and artifact hashes
must be appended by the executing root. Do not mark this work order owner-accepted based
on source review, editor assertions, or a successful build alone.

## Current full Play proof

Builds/night-owned-town-full-play.log reports OWNED_TOWN_MOVE_PLAY_OK, OWNED_TOWN_PRACTICE_PLAY_OK and OWNED_TOWN_FROZEN_CONSTRUCTION_PRACTICE_OK with no compiler errors and the main save unchanged. The actual Play fixture covers placement input, pending-selection handling, wall editing, atomic new construction, save retry, cancel/refund, tombstones, FTUE/reentry and independent owner construction during practice. Three real AI attackers move and fight; win/save-failure/retry, loss without normal death and abandon/return passed. Root opened owned-town-construction-live.png showing the scaffold/timer in the textured walled town. These are Editor Play proofs; release builds and owner device acceptance remain pending.
