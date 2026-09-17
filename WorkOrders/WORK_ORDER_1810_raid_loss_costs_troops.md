# WORK ORDER 1810 — losing a raid must COST troops (killed = dead, fail = whole warband, retreat = 60% of survivors)

**Status:** IMPLEMENTED
**Result:** `WorkOrders/WORK_ORDER_1810_raid_loss_costs_troops.RESULT.md`
**Silo:** raid-end reconcile / army storage / non-victory end-state VM (+ two remote-tunable rows)
**Owner ruling:** 2026-09-16 ~20:05, verbatim
**Ticket opened by:** CLI lane (WO-1810, number pre-assigned by the lead; banner already bumped)

---

## 1. THE OWNER RULING (verbatim, binding)

> "there is no cost to losing a raid" / "troops return a few injured which has no cost or time" /
> "loss should lose troops and then rebuild" / "maybe lose 100 on fail, lose 60% on retreat" /
> "but any troop killed is dead so 60% of whats left"

Read as canon:

1. **Any troop killed during a raid is DEAD, permanently.** No wounded-and-recover return for the killed,
   on ANY exit — victory included ("any troop killed is dead").
2. **FAILED raid** (clock expiry, warband wiped, watchdog, hero-death settlement — any non-victory that is
   **not** the player's own Retreat): the player loses **100%** of the deployed warband. Everything that was
   still alive is lost too.
3. **RETREAT** (the player's own Retreat button): **60% of the SURVIVORS** are lost (the killed are already
   gone); 40% come home **healthy**.
4. **VICTORY:** survivors come home untouched; the killed stay dead.
5. The player **rebuilds by training at the barracks** — there is no free recovery timer on a raid loss.
6. The non-victory result screen must **say how many troops were lost**, with a one-line reason.

---

## 2. WHAT THE BUILD DID BEFORE THIS TICKET — PROVEN AT SOURCE, NOT INFERRED

Captured evidence, build **372984**, 2026-09-16 19:59:01 local, `logs/device/logcat-2000-tower-inverted.txt`:

```
[Flow:Raid] raid clock expired at 180.0s (destruction 12%)
[Flow:Raid] raid-end reconcile - deployed 10, survivors 7, wounded 3 (stars 0, recovery 1200s).
[Flow:Army] TroopRecoveryService: recovery advanced; 3 still wounded.
army status -> NOT READY (deployable 7 + queued 0 / cap 10, required 10)
```
…and one minute later the Manage/army screen reads **"Army is full. 10/10 slots used"**
(`logs/device/seeker-now.png`). Killed troops became **wounded**, healed **free** in 20 minutes, and kept
occupying army slots the whole time.

The code that produced exactly that, read at source this session:

| Fact | Source |
|---|---|
| The reconcile + its trace line | `Assets/_Modules/Village/Troops/RaidDeployController.cs:2095-2168` (`ReconcileRaidEnd(int starsEarned)`, trace at `:2162-2165`) |
| The mutation: deployed-but-not-survivor → **MarkWounded, never deleted** | `Assets/_Modules/Core/State/ArmyStorage.cs:280-301` (`ReconcileAfterRaid`), `:258-269` (`MarkWounded`) |
| Recovery is a free TIMER (5 / 20 / 45 min by camp difficulty) | `RaidDeployController.cs:2041-2062` (`RecoveryRegularSeconds` … `RecoveryForDifficulty`) |
| The free heal actually runs, online + offline | `Assets/_Modules/Village/Troops/TroopRecoveryService.cs:94-127` → `ArmyStorage.AdvanceRecovery` (`ArmyStorage.cs:354+`) |
| **VICTORY did the same thing** — killed troops became wounded | `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:821-838` (`ReconcileArmy` → `deploy.ReconcileRaidEnd(stars)`) |
| The screen said "wounded", because that was true | `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:575-586` |

**Retreat vs timeout — the real signal (they are NOT the same exit):**
`OnRetreatPressed` (`RaidDeployController.cs:1614-1634`) calls `DoRetreat(EndStateVM.RetreatReason)`;
`OnRaidTimeExpired` (`:669-676`) calls `DoRetreat(EndStateVM.TimeoutReason)` — and the timeout toast
*"your warband retreats"* (`:671`) is flavour, not a retreat. `ForceExitHome` (`:652-665`) and
`HeroHealth.cs:1613-1614` both settle at 0 stars with no reason at all. So the exit reason string is the
only honest discriminator, and everything that is not `RetreatReason` and not the victory path is a FAIL.

---

## 3. WHAT THIS TICKET IMPLEMENTS

### 3.1 The pure policy — `RaidCasualtyPolicy` (new)
`Assets/_Modules/Village/Troops/RaidCasualtyPolicy.cs`. No scene, no save, no catalog, no `Random`:

```
killed       = deployed - survivors          (always dead, every outcome)
pct          = Victory 0 | Retreat retreatPct | Failed/Undeclared failPct
lostByPolicy = (survivors * pct + 50) / 100          // integer NEAREST, no floats
               if (survivors > 0 && pct > 0 && lost < 1) lost = 1      // never a free loss
               lost = min(lost, survivors)
returned     = survivors - lostByPolicy
```

**THE ROUNDING, STATED:** integer **nearest** (`+50 /100`) with a **floor of one** whenever a non-empty
remainder is taxed at all. Chosen over `ceil` deliberately: `ceil(2 * 0.6) = 2` would take **100%** of two
survivors and `ceil(7 * 0.6) = 5` is **71%**, neither of which is "60%". The floor of one is the ruling's
"at least one survivor of a non-empty remainder is lost". Table at 60%: `1→1, 2→1, 3→2, 5→3, 7→4, 0→0`;
`pct 0 → 0`; `pct 100 → survivors`.

**WHICH survivors are lost is DETERMINISTIC, never random:** rookies first — sort by `VeterancyRank`
ascending, then by `Id` ordinal. A veteran rank is earned (`GrantVeterancy`, 3-star clears only), so the
policy must never spend it on a coin flip, and an oracle must be able to assert the same answer twice.

### 3.2 The mutation — `ArmyStorage.RemoveOwned` (new)
Killed ∪ lost-by-policy are **removed from the roster** through one new `ArmyStorage` method, so the
deletion rides the existing serialized `GameState.Army.Owned` list and the existing `Save()` calls at every
raid exit. **No `SaveSchema` bump:** `Owned` is a `List<PlayerTroop>` on the wire and removing entries
changes no field and no shape (`Assets/_Modules/Core/State/SaveSchema.cs:41` — `CurrentVersion = 41`, whose
own v22 note is "army roster persistence … owned troops + cap + wounded/recovery/veterancy"). Nothing else
in the tree holds a `PlayerTroop.Id`: the WO-934 loadout bank stores **def-id + count rows**
(`ArmyLoadoutBank.cs:52-68`), not ids, and `TroopController.OwnedTroopId` is per-raid runtime state.
`grep '\.Owned\.Remove'` over `Assets/` returned **nothing** before this ticket — this is the first troop
deletion path in the game, and it is treated as the seam.

### 3.3 The wiring — `ReconcileRaidEnd`
The outcome is **declared** by the exit, on a tri-state field that defaults to `Undeclared`:
`DoRetreat` declares `Retreat`/`Failed` off its reason, `ForceExitHome` declares `Failed`,
`RaidVictoryController.HandleVictory` declares `Victory` at its `_handled` latch (**early, not at
`ReconcileArmy`** — a hero death in the window between the win and the reconcile would otherwise wipe a WON
warband), and `HeroHealth` is **not touched**: it settles at 0 stars with no declaration, and `Undeclared`
resolves to **Failed** per the ruling while emitting a `Warn` so a capture shows it.

Order inside the reconcile is unchanged where it matters: ids off the deploy ledger → policy → remove →
then the **existing** `army.ReconcileAfterRaid(deployed, survivors, recovery)` call is KEPT as a backstop
(it now finds nothing, because the fallen are gone; anything that somehow escaped removal is still wounded
rather than silently healthy). That keeps `RaidCooldownRegression.cs:313-318`'s `ResolveRecoverySeconds(`
pin satisfied by **live, honest code** rather than a dead call, and the `wounded N` half of the old trace
now reports what `MarkWounded` actually touched instead of `deployed - survivors`.

**Wounded/recovery is therefore NO LONGER a raid outcome.** `ArmyStorage.MarkWounded` /
`TickRecovery` / `AdvanceRecovery` / `TroopRecoveryService` all stay (they are the backstop above and are
pinned by `ArmyRecoveryRegression`); what changes is that a raid stops producing wounded troops.

New trace, verbatim as specified:
`raid-end casualties: outcome=<fail|retreat|victory> deployed= killed= survivors= lostByPolicy= returned=`

### 3.4 The screen — `EndStateVM.FromRaidRetreat`
Trailing optional `int troopsLost = -1` (every positional caller keeps its behaviour). The wounded sentence
is **replaced, not joined** — the subtitle can already carry four facts and `EndStateView` compresses every
band past that (`EndStateVM.cs:564-568`, `EndStateBodyFitRegression`). New copy:
`"N troops lost - a failed assault loses the whole warband."` / `"N troops lost - the cost of covering the
retreat."` / `"Every troop came home."` Literal strings, exactly like every other line in this factory, so
**no locale key was added** and `LocalizationBuilder.BuildAll` does not need to run for this ticket.

### 3.5 The two rates — REMOTE TUNABLE ROWS (chosen over a canonical JSON)
`raid.lossPctFail` default **100**, `raid.lossPctRetreat` default **60**. The rail was chosen because
**there is no canonical raid-balance JSON to put them in** (`ls Assets/Resources/Data/Canonical | grep -i
raid` → nothing), every other raid balance number already lives on the rail (PROD022 rows #19-#31, #43-#51,
#57-#64), and the owner's own words are provisional — *"maybe lose 100 … 60%"* — which is the definition of
a number that will move. Closest precedent followed field for field: **#43 `raid.lootRepeatClearPct`** (a
percent, `0..100` clamp at the consumer, a default that is the OWNER'S RULING rather than today's
behaviour). All six registration places moved in this change (PROD022_TUNABLE_FLAGS.md:412-419).

⚠ **Both defaults are a DEPARTURE from "the default is today's behaviour", stated not hidden:** today a
raid loses **0%** of its troops permanently, which is the defect. A row of `0` on both keys restores the old
no-permanent-loss behaviour exactly (the killed would still be removed — that half is the ruling's
"any troop killed is dead" and is deliberately NOT on the rail).

### 3.6 Regression
`Assets/Editor/Regression/RaidCasualtyRegression.cs` (marker `RAID_CASUALTY_OK` / `_FAIL`), registered in
`DataRegression.RunAll`: the pure policy across fail/retreat/victory + every rounding edge case, the
`ArmyStorage` mutation end-to-end on a fake army (deterministic rookies-first selection, veterans kept,
non-deployed troops untouched), and a source-lint that `ReconcileRaidEnd(int starsEarned)` still calls the
policy **and** the removal — so a future seat cannot silently revert to wound-only.

---

## 4. RULED 2026-09-16 ~21:20 — A FAILED RAID PAYS NO LOOT

The question in §4b below is **answered**: a FAILED raid (timeout or wipe — any non-victory that is not a
player retreat) pays **nothing**; a player **RETREAT** keeps the partial loot scaled by damage done, exactly
as today; victory unchanged. Implemented in `SettlePartialLoot` by branching on the **same**
`RaidExitOutcome` the exits already declare (`paysLoot = _exitOutcome == RaidExitOutcome.Retreat`), with the
trace `raid-end loot: outcome=fail paid=0 (owner ruling 2026-09-16)`. The score is still **finalized** on a
fail — stars / razed % / clock are what the screen reports and the `Finalized` latch is what stops a double
payment; only the grant is refused. The screen adds one sentence, **"No spoils - the warband was lost."**,
on the fail exit only.

⚠ **`raid.lootFailPct` (PROD022 #21, default 18) is NOT deleted and must not be.** It still prices
`RaidScoring.LootFor` and still pays a **0-star RETREAT**; the fail branch simply never consults it.

⚠ **Consequence, recorded not hidden:** the hero-death settlement declares no outcome by design, so it
resolves to fail and now pays nothing. That reverses WO-1110's **unruled** default *"death pays what retreat
pays"* — reversed by a ruling, which outranks it.

## 4b. THE QUESTION AS IT WAS ASKED (kept for the record)

**The partial "RETREAT LOOT" on a TIMEOUT.** The captured 19:59 exit still paid
`486w / 297i / 11f / 558g / 2c` on a raid that razed 12% and then lost the whole warband under this ruling.
That payout was **not** ruled on and is untouched here (`RaidDeployController.SettlePartialLoot` /
`LogRetreatCredit:1959-1978`, priced by `raid.lootFailPct` = 18%). **Should a FAILED raid that now costs
100% of the warband still pay partial loot at all — and if so, at 18%?** A `0` on `raid.lossPctFail` or a
change to `raid.lootFailPct` are both one console row away either way.

---

## 5. WHAT THIS TICKET DELIBERATELY DOES NOT TOUCH

`DefenseTower.cs`, `Assets/Editor/WallTools/*`, `CastleHubBuilder.cs`, any VFX prefab, any `.unity` scene,
`HeroHealth.cs` (its `ReconcileRaidEnd(0)` already lands on the Failed default — but its trace at
`HeroHealth.cs:1625-1626` still says *"the fallen are wounded"*, which is now **false copy in another
lane's file** and is named here for the lead rather than edited).
