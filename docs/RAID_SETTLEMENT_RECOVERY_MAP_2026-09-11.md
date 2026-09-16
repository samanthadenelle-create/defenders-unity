# Remaining raid settlement recovery boundaries

Source inspection after the live practice integration; this is an implementation map, not a claim of crash-safe settlement.

`RaidVictoryController.HandleVictory` currently gates town capture before its handled latch. After that latch, settlement still crosses the stores below. Retrying the entire method is unsafe.

| Effect | Current writer and store | Required replay boundary |
| --- | --- | --- |
| Town ownership and repair allowance | `RaidCaptureCensus` → `PendingTownCapture` → signed `GameStateService` save | Implemented: immutable capture intent; startup consumer; ownership, allowance and intent removal in one successful write. |
| Permanent camp claim | `RaidClaimService.MarkClaimed`, `dotr-raid-owner-<id>` PlayerPrefs | Freeze whether this was a new claim before mutation. Reapplying the flag is idempotent, but querying it again cannot reconstruct whether a companion was earned. |
| Cooldown | `RaidCooldownService.BeginAfterClear`, signed `GameState.RaidCooldowns` | Freeze server-anchored start time, duration and anchored flag. Recalling BeginAfterClear on recovery would extend the cooldown. |
| Companion unlock | `RaidVictoryController.UnlockNextCompanion` → roster/party services | Freeze the chosen companion before claim mutation. Recover that exact grant, not “choose next” again. Publish scene/presentation updates after durable state succeeds. |
| Resources | `GrantLoot` → `EconomyService.Grant`; fallback only covers Food and Crystals | Receipt and credited balances must persist together. Preserve authoritative storage clamps and all five requested/credited axes; missing-component fallback loss fixed and verified by RaidLootAuthorityProof; durable receipt remains unimplemented. |
| Overflow cache | `RaidClaimService.RetainOverflow`, `dotr-raid-cache-<resource>` PlayerPrefs | This operation is explicitly additive and not idempotent. Freeze the split and retain each receipt once; a replay must not add the overflow again. |
| Daily crystal stamp | `RaidClaimService.MarkCrystalsPaid`, `dotr-raid-crystalday-<id>` PlayerPrefs | Freeze the earned UTC day. Stamp only after its corresponding resource receipt succeeds; recovery must not stamp a different day. |
| Rough stone and grade | `DungeonRunPayout.GrantRoughStone`; day cap in `RaidScoring` | Item, grade, acquisition flags and receipt must share a recoverable operation; freeze earned day and gate decision. A separate day stamp does not prove the item was saved. |
| Army casualties/veterancy | `RaidDeployController.ReconcileRaidEnd` → army state | Capture the deployed/surviving roster before scene teardown. Boot recovery cannot inspect destroyed troop bodies or the old controller. |
| Victory count/backfill | `RecordVictory` → signed `RaidVictories` and `RaidVictoriesBackfilled` | Persist the increment with its receipt; preserve the one-time legacy claim backfill. |
| Daily quest progress | `DailyQuestService.Report`, JSON at `dotr-daily-quests-v1` | Progress plus receipt need a single stored result. Capture the quest-set date and affected quest IDs: rerolls/day rollover must not redirect an old raid to new quests. **This service uses local calendar date, not UTC.** |
| Season pass XP | `ArenaOutcomeRelay.Publish` → `BattlePassService.OnRaidResult`, separate `bp.*` PlayerPrefs | Freeze season, award inputs and first-clear decision. XP and receipt require one recoverable record; the first-clear flag alone does not deduplicate ordinary XP. Google Play may intentionally have no Wallet handler. |
| Analytics and presentation | `RaidFunnel`, victory sound/screen, transient events | Separate from authoritative settlement. They must not decide whether rewards landed or block recovery of earned state. |

## Constraints for the next implementation

- Record one validated, typed victory intent before any irreversible settlement write. It must contain enough data to finish without the old scene, scorer, spawner or deployed-body ledger.
- A journal stage saved after an unprotected additive effect still has a crash gap. Each store needs an idempotent receipt boundary or a frozen absolute after-image applied while conflicting mutations are blocked.
- Do not use “retry HandleVictory” as the startup consumer. Do not re-roll rewards, select another companion, recompute first-clear status, extend cooldowns, or use recovery-day stamps.
- Do not claim all effects are committed from void `Save`, guarded/swallowed exceptions, a day stamp, or absence of a registered optional progression handler.
- Existing player writes and cloud state must not be rolled back to a stale whole-save snapshot. Keep the pending intent local-only; handle explicit reset epochs consistently with `PendingTownCapture`.
- Acceptance must inject failure before and after each durable boundary and reconstruct the service from saved data. Prove no duplicated resources/cache/items/counters/XP, no lost earned grant, unchanged unrelated saves, and retryable UI until required effects are durably resolved.

The current practice proofs do not exercise actual raid victory settlement. The new Windows owner test build is a gameplay handoff, not evidence of these crash guarantees.

## 2026-09-11 14:13 ? missing economy authority payout fixed

GrantLoot now requires loaded GameState and restores the normal EconomyService when missing. Removed the partial two-resource fallback. The actual private raid grant is invoked against an isolated memory-backed wallet: absent component, five-axis credit, idempotent authority reuse, near-cap material credit and uncapped premium/coin credit, and exact result basket/shortfall flag all verified. Terminal PASS Builds/raid-loot-authority.log / RAID_LOOT_AUTHORITY_OK / zero compiler errors. Registered this proof in DataRegression. This fixes the dropped-axis defect only; no cross-store crash recovery claim.
