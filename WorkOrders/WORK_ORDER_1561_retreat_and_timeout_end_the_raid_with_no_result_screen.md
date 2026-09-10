# WO-1561: retreat and clock-expiry end the raid with NO result screen — the outcome is computed, banked, and discarded unread

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Priority:** **P0** — and it is the exit a new player is most likely to hit.
**Silo:** `Assets/_Modules/Village/Troops/RaidDeployController.cs` (exit paths only) +
`Assets/_Modules/Village/UI/EndState/EndStateVM.cs`. **NOT the raid's layout, art or lifecycle.**
**Parent:** WO-1534 §A3. **Source:** read-only review 2026-09-06 (CLI seat), re-read at source.
**LANDS AFTER** the wave-two gate — `RaidDeployController.cs` currently carries uncommitted WO-1462 /
WO-1520 edits in the shared tree.
**Minted** from the banner (`CLI_LANES_WO_NUMBERS.md`, renumbered to the banner's hundred-and-second-pass reconciliation, 2026-09-06 22:12).

---

## 1. EVIDENCE

`RaidDeployController.DoRetreat()` (`:752-763`) — the whole method:

```
SettlePartialLoot("retreat");
ReconcileRaidEnd(0);          // a retreat / clock-expiry exit is never a 3-star clear
TroopRally.Clear();
GameStateService.Instance?.Save();
SetStatus("Retreating to the castle...");
SceneRouter.GoCastle();
```

**No `EndStateVM`. No `EndStateView.Show`.** Measured:
`grep -c "EndStateVM\.\|EndStateView.Show" RaidDeployController.cs` returns **1**, and that single hit is
a **comment** at `:290`. The timeout exit funnels into the same method — `OnRaidTimeExpired()` (`:401`)
sets a status string and calls `DoRetreat()`.

⛔ **AND NOTHING PICKS IT UP IN TOWN EITHER. This was checked, because "deferred to town" would have
downgraded the finding.** `RaidResult` is the settled outcome (`RaidScoring.Finalize`, `:916-939`), and
**every reader of it is raid-scene-side**: `RaidDeployController.cs:800, :808`, `RaidScoring.cs`,
`RaidVictoryController.cs:259`. Grepping `RaidResult` across `Assets/_Modules` returns no town-side
consumer. The "welcome-back" surfaces (`ResourceCollectorService`, `HarvestOverflowModal`) are
offline-harvest popups and never mention a raid.

**So the player is teleported into town with no screen**, having earned real loot (`SettlePartialLoot`)
and possibly a star — `RaidScoring.cs:455` grants 1 star at `destructionPct >= 0.5f` — and is told
**none of it**: not razed %, not stars, not spoils, not which troops came home wounded. A **won** raid
gets the full treatment at `RaidVictoryController.cs:753-766`.

⚠ **Why P0 (memory `retention-is-the-business-problem`):** the most likely raid a new player *finishes*
is one they lose or abandon. That is the one the game says nothing about.

**Not covered by WO-1437** (`raid_never_ends_softlock`): that ticket proves the three exits *fire*; its §4
asks only whether the session terminates. **Nothing in it asks an exit to REPORT.** WO-1526 changes *when*
hero death settles, not *what* a non-victory exit shows.

⛔ **There is no post-raid result PNG anywhere in the repo.** This ticket is proven from source only, which
is why acceptance 6 requires a fresh capture.

## 2. WHAT TO DO

1. Every non-victory exit — retreat and timeout — shows a result screen naming **razed %**, **stars
   earned**, **spoils actually banked**, and **troops lost / wounded**.
2. **REUSE `EndStateVM` / `EndStateView`. NO NEW SCREEN.** Model the call on
   `RaidVictoryController.cs:753-766`, which already composes the victory one. `EndStateVM` already has
   the template shape you need (see `:388` and `:416`).
3. ⛔ **Report what was BANKED, never what was promised.** WO-1461 records the live case: the deploy card
   quoted `~1800 wood` and **25** arrived, because the bank was full (`[Flow:Bank] BANK FULL [Grant]
   Wood: requested 450, banked 25, LOST 425`). Read the settled values off `SettlePartialLoot` /
   `RaidResult` — never off the deploy estimate. If loot was lost to a full bank, **say so**, the way the
   victory screen's *"Some of the reward could not be paid out"* line does
   (`RaidVictoryController.cs:782-784`).
4. Add `FlowTrace` on the new path (CLAUDE.md §12). **Never strip existing FlowTrace** (§12 ruling
   2026-08-09) — instrumentation is permanent.
5. **Accommodate WO-1526**, which will stop hero death ending the raid and cap it at 2 stars. Do not
   re-decide hero-death settlement; just do not build something that blocks it.

## 3. THE SOFTLOCK TRAP — read before wiring the dismiss

`EndStateView` fires its primary route on a timer when `AutoDismissSeconds > 0`
(`:989-990` -> `:2628-2632`), and **the file contains no `StopCoroutine`** (`:2771`). WO-1543 is deciding
that timer's behaviour for the **victory** screen and is **blocked on an owner ruling**.

**For this ticket: give the retreat screen an anti-softlock guard, because a player stranded after a
retreat is strictly worse than one who reads a screen too briefly.** Follow whatever WO-1543 settles
afterwards rather than inventing a second rule here. `EndStateVM.cs:379` shows the deliberate opt-out
(`AutoDismissSeconds = 0f`, *"Retry must be chosen"*) if that turns out to be right.

## 4. ACCEPTANCE

1. Retreat shows a result screen. Clock-expiry shows a result screen.
2. Both name razed %, stars, spoils **banked**, and troops lost/wounded.
3. Both reuse `EndStateVM` / `EndStateView`. No new screen class is added.
4. Loot lost to a full bank is stated in words, not silently dropped.
5. A regression oracle under `Assets/Editor/Regression/` **FAILS** if a non-victory exit routes to town
   without an end state. ⛔ **Prove it RED before it is green** and record both runs in the RESULT
   (memory `prove-the-success-path-not-just-the-refusal`).
6. A **fresh** capture of the retreat result screen is attached to the RESULT. No such frame exists today.
7. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on **fresh** logs — judged by the marker, never the exit
   code (CLAUDE.md §8).

## 5. WHAT NOT TO TOUCH

- Raid layout, art, backdrop, overlaps, the magenta flag, the "make it pop" pass — **WO-1462 / 1463 /
  1464 / 1519**, in flight on these files.
- The staging area and when the clock starts — **WO-1520** (P0), same file.
- Hero-death settlement — **WO-1526**.
- Loot amounts, the Raid Cache, repeat multipliers — **WO-1461**. This ticket adds a REPORT, never a number.
- Any second "when may you raid" gate — **WO-1379** / `HeartfireRegression` PIN F reds the file.
- The victory screen's auto-return duration — **WO-1543**, blocked on a ruling.
