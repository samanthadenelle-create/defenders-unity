# WORK ORDER 1803 — The starter army becomes TEN: five Footmen and five Archers

**Status:** IMPLEMENTED

**Owner ruling 2026-09-16 (verbatim):** *"Instead of giving them three troops, because that's shit,
let's give them ten troops. Let's give them five footmen and five archers."*

**Sibling lane:** WO-1802 (make the raid door obvious after founding) deliberately hardcodes **no**
troop count and points here for the grant. This lane owns the grant, the composition and the
`raid.starterArmySize` default.

---

## 1. Where the three came from, and what is actually wrong with it

WO-1374 (north-star map §2) put a free squad behind the first Barracks to delete the 1,650-gold wall
in front of the raid loop:

> *"A player starts with 200 gold but needs 1,650 to participate in the thing you're trying to teach
> them. That's basically putting a nightclub behind a velvet rope and handing the player twelve
> cents."*

It shipped at **3 x `troop-footman`**, because three was what 1,650 gold used to buy. Two things are
wrong with that, and both are measurable rather than felt:

1. **Three cannot take the first camp.** `raider_camp_small` ("The Forsaken Camp") garrisons **9**
   defenders — summed from `garrison.composition` in
   `Assets/Resources/Data/Canonical/scene-configs.json`, read at source 2026-09-16 via
   `RaidSelectionVM.GarrisonCount`. With 3 deployable the raid grid prints
   **`Outmatched - Army 9 advised`** on the only camp the FTUE points at, and BEGIN ASSAULT asks the
   player to confirm marching into a fight the game itself has just called uneven. The free squad was
   sized against a retired gold price, not against the fight it has to win.
2. **Ten identical melee bodies teach nothing.** The two day-one units
   (`unlockBarracksTier: 1` in `troops.json`) are Footman and Archer. A starter squad that contains
   only one of them never shows the player that composition exists.

## 2. What this lane changes

### (a) The grant becomes an authored COMPOSITION, with the total still on the knob

`Assets/_Modules/Village/Troops/StarterArmyGrant.cs`:

- `StarterTroopId` = `troop-footman` (unchanged) and a new **`ArcherTroopId` = `troop-archer`**. Both
  read at source in `Assets/Resources/Data/Canonical/troops.json` (`:6` / `:29`, 2026-09-16):
  `slots: 1`, `unlockBarracksTier: 1`.
- `DefaultStarterCount` **3 -> 10**.
- **`SplitComposition(total, out footmen, out archers)`** — pure, static, public. Even split,
  **remainder to Footmen**. 10 -> 5/5; 3 -> 2/1; 1 -> 1/0; 0 and negatives -> 0/0.
- ⛔ **ONE KNOB, NOT TWO, AND THAT IS THE DESIGN.** The owner retunes `raid.starterArmySize` (the
  **total**) from the DB; the split is *derived* from it. Authoring the two halves as separate knobs
  would let a DB row land "ten troops" that are secretly ten Archers — i.e. it would make the exact
  thing the ruling rejects reachable from a surface with no review.
- `GrantRun(...)` grants each half through the **one** roster owner
  (`BarracksProgression.GrantTrainedTroop`), so funnel step 2 (`army trained`) still fires from the
  same place a paid train fires it. A short melee half skips the ranged half: it would fail
  identically and a second copy of the same `FlowTrace.Fail` is noise on top of the line that already
  named the cause.
- `GrantToastFor(int footmen, int archers)` replaces the one-arg form. The FTUE toast now reads
  **"Your first squad is ready - 5 Footmen and 5 Archers, free. Open Journey, then Raids."** It names
  both units with their real counts, drops a half that is zero, and stays 7-bit ASCII (mobile
  font-atlas law). A toast that said "10 Footmen" while half the squad drew a bow is the small lie
  that costs trust in every other number the game prints.

### (b) The ten FIT — proven, not asserted

- `ArmyStorage.DefaultMaxArmySize` is **10** (`Assets/_Modules/Core/State/ArmyStorage.cs:43`, read at
  source), and Footman and Archer each cost **1 slot**. Ten bodies = ten slots = **exactly** the
  fresh-save housing cap. **No housing change was needed** and none was made.
- `ResolveCount()` now clamps to `ArmyStorage.DefaultMaxArmySize` **instead of a literal 10**. The
  knob ceiling and the housing cap are the same number and are not allowed to drift — a hand-copied
  ceiling there is the duplicated state CLAUDE.md §2/§5/§8/§16 each describe in their own words.
- **NOTHING IS EVER TRUNCATED.** `ArmyStorage.GrantTrained` is unconditional by design (CoC parity —
  the barracks holds a unit that was paid for even if the cap has since filled), so the squad always
  lands in full. What the grant now does is read `state.Army.MaxArmySize` and, if the knob asks for
  more than the save can house, emit **`FlowTrace.Fail` naming the cap** — never a silent clip and
  never a shrug.
- **The tray can field all ten.** `ArmyReadiness.Compute(army, slots, 0)` over the granted roster
  reports `DeployableSlots = 10`, `CapSlots = 10`, `Ready = true`. There is **no separate deploy-tray
  ceiling** on the player side: `RaidDeployVM.Rebuild` groups the roster by `TroopDefId` and carries
  the count as the card's price, so ten troops draw **two** cards (footman x5, archer x5), and
  `Fielded` is `Readiness.DeployableSlots`. (`liveCombatantCap = 36` in
  `Village/World/Camps/RaidGarrisonSpawner.cs:61` is the **defender** concurrency budget, not a player
  cap.)
- **The door's copy still reads right.** Camp I garrisons 9; with 10 deployable,
  `RaidSelectionVM.ArmyWarnWord` returns **null** — the `Outmatched - Army 9 advised` word is gone
  from the first camp, which is the point. It still fires on Camp II (garrison 15) and above, and the
  regression pins both halves: no word at 10, and a word still at 3, so a pass at 10 cannot be a
  compare that has gone inert.

### (c) Existing saves DO NOT re-grant

The one-shot latch is unchanged: `GameState.MarkEverAcquired("grant.starter-army")`, the monotonic
acquired-ledger key, returns true only on the first add and **is both the check and the latch**. So:

- a save that already took the **old 3-troop** grant receives **nothing** — no top-up to ten;
- a demolished-and-rebuilt Barracks receives nothing (a second squad would make demolish a troop
  faucet);
- no save-schema bump, no migrator, no new field.

⛔ **A top-up was NOT implemented and must not be added casually.** "Give the existing 3-troop saves
seven more" is a *retroactive* grant, and a grant that appears after the fact is worse than one that
never happened — it lands with no FTUE beat around it, and the same reasoning already written into
`ResolveCount` (a knob moved later must not fire a grant retroactively) applies. If the owner wants
those saves topped up, that is a ruling and a separate ticket with its own ledger key.

### (d) The default is registered in the brief's six places — and a SEVENTH the brief missed

| # | Place | Change |
|---|---|---|
| 1 | `Assets/_Modules/Core/Ops/RemoteTunables.cs` | `RaidStarterArmySizeDefault` 3 -> **10**; key doc + `TunableSpec` prose rewritten (total-not-composition, the 5/5 derivation, the `0..DefaultMaxArmySize` clamp, the owner's verdict on three) |
| 2 | `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | `ExpectedDefaults` row `raid.starterArmySize` 3 -> **10** |
| 3 | `docs/PROD022_TUNABLE_FLAGS.md` | row **32** rewritten (default `10`, the split, the once-per-save/no-re-grant note, the clamp-to-the-housing-const, the 10/10 housing consequence) |
| 4 | `api/_lib/tunables.js` | `TUNABLE_KEYS` comment for the key rewritten (the row carries the **total**; the client derives the split) |
| 5 | `api/_lib/tunable-manifest.js` | `label`/`what`/`risk` rewritten in the Command Center's plain voice — says five-and-five, says it splits whatever number you enter, says why 10 is the ceiling |
| 6 | `api/_lib/tunable-manifest.generated.json` | regenerated: `node tools/gen-tunable-manifest.mjs` -> `TUNABLE_MANIFEST_GEN_OK knobs=71` |
| **7** | `Assets/Editor/Regression/RaidLootCurrencyRegression.cs` | ⚠ **A SEVENTH SITE THE BRIEF DID NOT NAME.** `MapStarterArmySize` 3 -> **10** (`:74`), consumed at `:186` by `AssertDefault(KeyRaidStarterArmySize, MapStarterArmySize)`. **That suite goes RED at the first gate** if this literal stays at 3. Found only by grepping the C# CONST/KEY identifiers, not the lowercase key string. |

## 3. Instrumentation (§12 — permanent, never stripped)

The grant's `FlowTrace.Step` now carries the **composition and the housing check**, not just a total:

```
STARTER ARMY GRANTED: 10/10 free on first Barracks - composition 5/5 x 'troop-footman'
+ 5/5 x 'troop-archer' (WO-1803, owner 2026-09-16 'five footmen and five archers'; WO-1374
map section 2 ...). Housing cap 10, roster is now 10.
```

Plus: `FlowTrace.Fail` when `want > MaxArmySize` (names the cap, says nothing was truncated, says what
to do); `FlowTrace.Fail` per-id when the roster owner reports an empty roster; `FlowTrace.Warn` when
the knob clamps, now naming the ceiling as *"the fresh-save army housing cap"* rather than a bare
`0..10`.

## 4. Regression — `StarterArmyGrantRegression` (`STARTER_ARMY_OK` / `_FAIL`)

Already registered in `DataRegression.RunAll` (`DataRegression.cs:1586`) — **no registration line was
added and `DataRegression.cs` was not touched.** Cases added/changed:

- **A2 — composition BY TROOP ID.** Counts `troop-footman` and `troop-archer` separately against the
  literals 5 and 5. A total-only count would pass a squad of ten Footmen, which is precisely what the
  ruling rejects.
- **A2b — both ids are real, 1-slot, tier-1 defs** (`TroopCatalog.Find`). A typo'd id still lands in
  the roster (the grant is unconditional) and then draws a tinted capsule with no stats — a silent
  failure of the exact class §12 forbids.
- **A4 — it fits.** `DefaultStarterCount <= ArmyStorage.DefaultMaxArmySize`, the granted roster
  occupies exactly 10 slots, and the source lint requires `ResolveCount` to clamp to the **const**,
  not a literal.
- **D1 — the split rule.** 7 values incl. 10/3/1/0/-4: exact halves, `f + a == max(total,0)` (no body
  silently lost at any knob value), and the remainder never lands in the ranged half.
- **E1/E2 — the ten can march.** Real `ArmyReadiness.Compute` over the granted roster
  (`DeployableSlots == 10`, `Ready`), then real `RaidSelectionVM.ArmyWarnWord` over the real Camp I
  def: **null at 10** and **non-null at 3**, plus `garrison <= 10`.
- **C2 — the toast** re-pointed to the two-arg form across 5 compositions (5/5, 2/1, 1/1, 1/0, 0/1),
  including the negative halves: a toast must not promise a unit the player did not receive.
- **B1/B2 — no re-grant** (unchanged, and now the guard for existing 3-troop saves): a second call and
  a save already carrying the ledger key both grant zero.

## 5. Known and accepted consequence — a fresh save is 10/10

After the grant, `SlotsRemaining == 0`, so `ArmyStorage.CanTrain` refuses **every** troop until the
barracks-tier `armyCapBonus` perk raises the cap. This is deliberate and it is the CoC shape: *the
camp is full, upgrade it.* Recorded rather than papered over.

It does **not** strand the FTUE: the post-raid beat `ctx_post_raid`
(`tutorial-steps.json`) completes on `troop.job_queued`, which `BarracksService` raises from **both**
`EnqueueTraining` *and* `UpgradeTroop` — and a troop upgrade costs time, not housing. The beat is also
contextual, `pausePressure: false`, one-shot, with a 240 s release, so it can never gate. The
`{army.used}`/`{army.cap}` tokens in that beat's copy will read "10 of 10", which is true.

⚠ **Not verified by this lane:** whether "10 of 10, 0 missing" *reads* well in that beat's sentence is
a felt question and the tutorial files belong to another lane — flagged for the owner, not changed
here.

## 6. Files this lane touched

```
Assets/_Modules/Village/Troops/StarterArmyGrant.cs
Assets/_Modules/Core/Ops/RemoteTunables.cs
Assets/Editor/Regression/StarterArmyGrantRegression.cs
Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs
Assets/Editor/Regression/RaidLootCurrencyRegression.cs
docs/PROD022_TUNABLE_FLAGS.md
api/_lib/tunables.js
api/_lib/tunable-manifest.js
api/_lib/tunable-manifest.generated.json
WorkOrders/WORK_ORDER_1803_starter_army_ten_five_footmen_five_archers.md
WorkOrders/WORK_ORDER_1803_starter_army_ten_five_footmen_five_archers.RESULT.md
```

**Not touched, per brief:** `PackStore.cs`, `StorePackCard.cs`, `PurchaseQuoteService.cs`,
`BuildStructureInfoPanel.cs`, `TutorialFlow.cs`, `PlayerDeckWorkspace.cs`,
`RaidSelectionScreen.cs`/`RaidSelectionVM.cs` (read only), `HudKitController.cs`,
`RaidDeployController.cs`, `TroopController.cs`, any `.unity`, `DataRegression.cs`,
`CLI_LANES_WO_NUMBERS.md`. No Unity gate, no commit.

---

**See `WORK_ORDER_1803_starter_army_ten_five_footmen_five_archers.RESULT.md` for the proofs.**
