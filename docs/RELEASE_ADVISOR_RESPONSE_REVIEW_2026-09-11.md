# Lead evaluation of the external release-blocker response

Source: C:/Users/Elden/OneDrive/Desktop/review.md, modified 2026-09-11 10:55:02. Read in full. Advice informs implementation; it does not change the owner's objective or establish completion by assertion.

## Decisions

| Topic | Disposition | Implementation constraint |
|---|---|---|
| Q1: persisted pending victory | Accept with corrections | Persist the immutable capture payload and receipt before effects. Preserve capture and its isolated supplies in the same owned revision, as they already are. Do not introduce a separate SupplyGranted stage unless there is an actual independent effect. Recovery guarantees begin only after the journal write succeeds. |
| Q2: stable IDs and layout adapter | Accept with corrections | Bake a stable templateStructureId once into each authored object; preserve it across hierarchy edits. Store a separate owned instance ID tied to capture receipt plus template ID. Capture-time receipt/sequence IDs alone cannot locate objects in a shipped template. Preserve a defined pose coordinate frame and template version; stable IDs alone do not make local poses survive reparenting. |
| Q3: pristine and ruined capture | Reject the proposed pristine mutation and refusal policy | Assigning damage that did not occur is inventing damage, regardless of wording. Pristine captures should use a real integrity inspection followed by design. Ruined captures must have a bounded, one-time allowance sufficient to restore/rebuild a defense usable for design. Retain actual ruins; do not silently deny earned ownership because assistance was mispriced. |
| Q4: terrain bake plus runtime obstacles | Accept direction, amend proof claims | Match obstacles to actual colliders, wait for carving updates, and verify routes/spawns for the relevant agent settings. Do not invent a custom flood-fill where Unity's path queries suffice. Three categories are useful, but neither their fixed count nor a few endpoints proves all layouts playable. |
| Q5: explicit practice mode | Accept no-wallet separation; reject abandon-as-completion | Reuse the arena lifecycle and one snapshot importer. An explicit enum/mode flag can be safe if it selects a no-wallet path before any economic call; a second class is not inherently safer. Win/loss can complete the lesson. Abandon returns safely and remains retryable without awarding completion merely for exiting. |

## Material corrections

1. A single composed state write is not inherently worse than a journal. Its safety depends on the persistence backend and effect boundaries. The journal is appropriate for recoverable intent, but every effect still needs idempotency. Leaving loot and counters outside the journal does not establish duplicate-reward safety for those effects.
2. The claim "kill between victory and capture commit -> record survives" is only true after a successful durable pending-record write. A failure to record intent must stop dependent effects and expose retry; there is no recovery guarantee for a record never written.
3. Nullable additive fields do not universally require a schema-version bump, and schema bumps are not universally irreversible. Follow this repository's actual migration and old-client compatibility policy. Existing owned fields are additive under schema 41; inspect that contract before changing it.
4. The story castle must remain a separate property. Share the editor/import interfaces, not the owned town's data as the story castle's replacement authority.
5. The existing BuildModeController does not already expose the complete small layout-context interface described in the response; direct BaseLayout reads/writes remain. Adapting those sites is real implementation work.
6. Granting a wreck is still an asset grant. A repair allowance should be bounded and receipted, but there is no reason to fabricate a new wreck when the census already retains actual destroyed structures.
7. Run desktop Play, automated interruption tests and visual checks before final expensive packaging. Device verification is essential for device behavior, but not every end-to-end observation requires the owner's phone. Sub-frame settlement boundaries require controlled fault injection; a person cannot reliably force-quit at each exact write boundary.
8. Play content exclusion needs build configuration, dependency/content evidence and packaged-artifact checks. A string scan alone is insufficient. An internal-track upload is an external distribution action and is not authorized by this advice.
9. Neither "most of the work" nor "a week" is an evidence-based estimate from the inspected implementation. The remaining integrations are substantial. The absence of complete-journey proof is not proof of failure, but it cannot establish readiness; several integrations are also demonstrably unwritten.
10. Advice cannot prohibit discovering additional defects or mandate new work orders for every finding. Keep the agreed scope, record new evidence, and resolve release blockers within it.

## Execution order

Stable template identities and the layout-context boundary first; then recoverable capture settlement preserving atomic ownership/supplies; then guaranteed pristine/ruined onboarding paths; dynamic navigation and shared snapshot practice integration; desktop journey/interruption/visual checks; current full gates and signed artifacts; device acceptance. No store publication is implied.

## Independent UI work completed during the review

The first six-frame town-panel capture passed the existing automated checks, but image inspection showed Return to castle outside the design panel. Put the two movement controls on one row; the initial long labels then failed the glyph check and were shortened to Move west / Move east. release-owned-town-panel-final.log passed OWNED_TOWN_PANEL_CAPTURE_OK for six frames with zero compiler errors. The final 2670x1200 image was reopened for inspection. This is isolated panel evidence, not live town entry or release completion.
