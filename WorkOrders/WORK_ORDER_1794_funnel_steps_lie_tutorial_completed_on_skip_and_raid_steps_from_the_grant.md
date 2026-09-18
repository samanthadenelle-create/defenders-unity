# WORK ORDER 1794 — Two of the funnel numbers the owner reads are untrue: `tutorial_completed` fires on SKIP-ALL, and raid steps 1-2 fire from the starter grant

**Status:** DONE - committed 1ed77ffd2, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead from the 1791-1795 block; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** funnel emitters — `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs` (the `tutorial_completed` call site) and `Assets/_Modules/Village/Troops/StarterArmyGrant.cs` + `Assets/_Modules/Core/Analytics/RaidFunnel.cs` (step semantics). Analytics only — **no gameplay behaviour may change.**
**Priority:** P1 — these are the two numbers in today's metrics that would make the owner believe the FTUE and the raid on-ramp are working when they are not.
**Lane disjointness:** file-disjoint from WO-1791 (`EventTracker.cs`), 1792 (art), 1793 (`api/admin/db.js`), 1795 (`api/game/save.js`).

---

## 1. DEFECT A — `tutorial_completed` counts skip-outs as completions

Production rows, 2026-09-16, ordered per player id:

```
guest-local-0821c1bb 16:28:20 tutorial_started      {"mode":"new","flowId":"ftue_v2"}
guest-local-0821c1bb 16:29:10 tutorial_skipped_all  {"index":0,"seconds":10.35,"fromStep":"founding_greet"}
guest-local-0821c1bb 16:29:10 tutorial_completed    {"skips":0,"totalSeconds":10.35}

guest-local-f6035f25 20:08:55 tutorial_started
guest-local-f6035f25 20:09:39 tutorial_skipped_all  {"index":0,"seconds":28.48,"fromStep":"founding_greet"}
guest-local-f6035f25 20:09:39 tutorial_completed    {"skips":0,"totalSeconds":28.48}

2ZurmHauf4fD…       21:02:54 tutorial_started
2ZurmHauf4fD…       21:03:26 tutorial_skipped_all  {"index":0,"seconds":11.16,"fromStep":"founding_greet"}
2ZurmHauf4fD…       21:03:26 tutorial_completed    {"skips":0,"totalSeconds":11.16}
```

`tutorial_completed` lands **in the same second** as `tutorial_skipped_all`, with
**`skips: 0`** and `totalSeconds` in the 8-33 s range — a tutorial nobody could have played.

**The day's counts are therefore not what they look like:** `tutorial_started 14`,
`tutorial_completed 8`, `tutorial_skipped_all 6`. Every `tutorial_skipped_all` row today has a
matching same-second `tutorial_completed`, so **at least 6 of the 8 "completions" are skip-outs**
and genuine completion is **2** — and even those two (`unverified` at 14:03:28, `totalSeconds 97.8`,
and 17:11:31, `totalSeconds 80.5`) sit in the shared bucket of WO-1791.

### The mechanism, read at source

`TutorialFlow.cs:1695-1700` emits `tutorial_skipped_all { fromStep, index, seconds }` inside the
skip-all handler, which then **ends the flow**; `TutorialFlow.cs:1820-1824` emits
`tutorial_completed { totalSeconds, skips = _skips }` in the **flow-finished** path, unconditionally
and with no knowledge of how the flow ended. So it is a **fall-through, not a deliberate dual-purpose
event** — and `_skips` is `0` because skipping *all* never increments the per-step skip counter. Both
facts are visible in the rows above (`skips:0`, same second).

Fix: the finish path must know how it was reached. Preferred shape, smallest diff:
`tutorial_completed { totalSeconds, skips, outcome: "completed" | "skipped_all" }`, so the existing
event keeps its name (a dashboard already sums it) and the truth is one field away. Do NOT emit a
bare `tutorial_completed` after a skip-all. **Do not change what the skip button does to the game**
— including the one-shot `MarkTutorialSeen` sweep at `:1688-1694`, which is deliberate.

## 2. DEFECT B — raid funnel steps 1 and 2 are not player actions

`RaidFunnel` (`Assets/_Modules/Core/Analytics/RaidFunnel.cs:79-94`) defines six steps, latched once
per install in PlayerPrefs. Today's rows, per player, with their own `source` property:

```
2ZurmHauf4fD… 21:02:54 raid_funnel_barracks_unlocked {"source":"StarterArmyGrant"}
2ZurmHauf4fD… 21:02:54 founding_path_selected        {"path":"starter_settlement"}
2ZurmHauf4fD… 21:02:54 raid_funnel_army_trained      {"source":"starter-army","troopId":"troop-footman"}
2ZurmHauf4fD… 21:02:54 tutorial_started
```

All four in the SAME SECOND, for all four ids that reached founding today
(`2ZurmHauf4fD`, `3xLP7Uyx`, `DKYiurDc`, and `unverified` twice). Both raid steps carry
`source: "StarterArmyGrant"` / `"starter-army"` — they are emitted by
`StarterArmyGrant.cs:144-147` and `BarracksProgression.cs:365` as a side effect of the founding
grant, not by a player unlocking a barracks or training a troop.

So the metrics line `raid_funnel_barracks_unlocked 8 / raid_funnel_army_trained 8` reads as
"8 players built a barracks and trained an army" and **means** "8 grants fired". The honest funnel
for today is:

```
founding_path_selected        8 events / 4 ids
raid_funnel_first_raid_attempted   0        <-- ZERO. Not one of today's players launched a raid.
```

Across the whole 7-day window there were only **3** `first_raid_attempted` events, on 09-15 (2, one
id) and 09-11 (1, one id).

Fix: **keep the six wire names** (WO-1374 owns them) and add `source` to the funnel's own
documented contract so a query can separate granted from earned — e.g. an explicit
`granted: true` property on a grant-sourced step, and a note in `RaidFunnel.cs` that steps 1-2 are
grant-fired for every founding player. Steps 3-6 are already genuine player actions
(`SceneRouter.cs:626`, `RaidVictoryController.cs:447`) and must not be touched.

## 3. ACCEPTANCE CRITERIA

- [ ] A headless FTUE run that presses SKIP-ALL emits `tutorial_skipped_all` and **no bare
      `tutorial_completed`** (or a `tutorial_completed` carrying `outcome:"skipped"`).
- [ ] A headless FTUE run that plays to the end emits `tutorial_completed` with the real `skips`.
- [ ] Every grant-fired `raid_funnel_*` row carries the flag that separates it from an earned one;
      a regression pins the six wire names unchanged.
- [ ] `RaidFunnel.PrefKeys` snapshot/restore still works (the file's own note at `:120-127` — a
      suite must not latch a step on the build machine).
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs. No gameplay change in the diff.

## 4. WHAT NOT TO TOUCH

The six `RaidFunnel` wire names and their order. The skip button's gameplay effect. The PlayerPrefs
latch keys (renaming one re-fires a step for every existing install).

---

## 5. IMPLEMENTATION (lane hand-back 2026-09-17, awaiting lead gate)

**Defect A.** `FinishFlow()` became `FinishFlow(string outcome)`
(`Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs:1929`); the mandatory chain's end passes
`OutcomeCompleted` (`:843`) and `SkipAll` passes `OutcomeSkippedAll` (`:1808`) while still reusing the
one finisher. `tutorial_completed` now carries `outcome` ("completed" | "skipped_all") plus a
`completed` boolean for dashboards that filter more cheaply on a bool. No `stepsSeen` field was added,
deliberately: `SkipAll` sets `_index` to the step count before finishing, so such a field would have
invented a second false number on this very event. Gameplay untouched — the `MarkTutorialSeen` sweep,
the grants and the Onboarded hand-off are byte-identical.

**Defect B.** `RaidFunnel.BarracksUnlocked(source, bool granted)` and
`RaidFunnel.ArmyTrained(troopId, rosterCount, source, bool granted)` now emit a `granted` property.
The flag is an EXPLICIT argument, never inferred from `source` — otherwise a future grant path only has
to invent a spelling to report itself as an earned action. `StarterArmyGrant` passes
`granted: true` for the starter squad; `BarracksProgression.GrantTrainedTroop` forwards a
`granted = false` default because its default path is the paid, timed Train job. Step 1 reports
`granted = true` from a documented const (`StarterArmyGrant.BarracksIsGrantFired`) — this WO §2's own
ruling that the step is grant-fired for every founding player. A "did we watch the barracks appear"
discriminator was written first and REJECTED: the founding-choice screen leaves a readable `GameState`
with no Barracks for the tens of seconds the player spends on it, so every founding player would have
been reported as having EARNED one — the inverse of the measured rows — and
`StructureSingleton.IsBuilt` answers from live scene objects and baked twins
(`StructureSingleton.cs:131-145`), not from the save alone. The remaining blind spot (a genuinely
player-built first Barracks also reads as granted) is written at source. Wire names, order and
PlayerPrefs latch keys untouched.

⚠ **WIRE VALUE:** the skip outcome ships as **`"skipped_all"`** (§1's preferred shape), not §3's
`"skipped"` — write dashboard filters against `outcome = 'skipped_all'`, or the boolean
`completed = false`.

**Oracles.** `TutorialCompletionPublisherRegression` Case 7 `[skip-is-not-completion]` (no
parameterless finisher may exist or be called, the row carries `outcome` + `completed`, `SkipAll`
finishes as `skipped_all`, the chain's end as `completed`) and `RaidFunnelRegression` section (E) plus a
LITERAL pin of the six wire names in (A) — the previous (A) only compared them against their own
consts, so a const rename passed while orphaning every collected row.

**Wire verified, not assumed:** `api/events/track.js:337-342` parses `properties` whole into JSONB with
no key allowlist and no truncation, so `outcome`, `completed` and `granted` do reach Neon. Both oracles
are REGISTERED in `DataRegression.RunAll` (`DataRegression.cs:1245` tutorial-completion-publisher,
`:1617` raid-funnel), so Case 7 and section (E) are inside `REGRESSION_OK` rather than being unregistered.

**Not done here, flagged for the lead** (out of this WO's declared emitter silo — needs a follow-up
ticket or a go-ahead): `api/admin/stats.js:695` still counts every `tutorial_completed` row into
`completed_players`, which is the "Finished it" tile at `site/admin.html:328`, and `stats.js:226`
`MILESTONE_EVENTS` counts the same event as "progressing". Both keep reading high until they filter on
`completed = true` / `outcome = 'completed'`. Historic rows carry no outcome at all and cannot be
back-filled.

**Lane collision to reconcile (lead, not this lane):** `TutorialFlow.cs` is NOT file-disjoint as §7
assumed — the WO-1788 lane's scene-scoped `MarkTutorialSeen` sweep is in the same working file, and it
changed the very sweep this WO §1 says not to touch. The two tickets must be reconciled and the commit
split by hunk.
