# WO-1530: measure the enemy level-scaling formula BEFORE any balance pass

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT - owner direction 2026-09-06 20:33. **DO THIS FIRST** of the balance work.)
**Silo:** Village/Enemies scaling + `RaidGarrisonSpawner` level assignment.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1530 -> 1531 in the same edit). From her review of
`docs/RAID_BALANCE_AUDIT_2026-09-06.md`.

## 1. EVIDENCE

Owner, verbatim:

> "make enemy scaling the very next measurement, because that is the biggest unknown capable of making all the
> napkin DPS comparisons lie"

The one measured data point says the lie is real: at camp level 3 the hero took **15 per hit against a listed
10**. Whatever the formula is, the authored number is not what the player meets, so every damage and
time-to-kill figure in the balance audit is computed against values that do not occur in play.

This is the whole reason the ticket exists: it is not a bug report, it is the measurement that has to precede
the other tickets. WO-1526, WO-1527, WO-1528 and the Hard/Extreme tuning all rest on it.

## 2. FIX SHAPE

- Read the scaling formula AT SOURCE and write it down. Do not infer it from observed damage.
- Add a PERMANENT `FlowTrace` line at defender spawn naming, per defender: base health/damage -> scaled
  health/damage, and the level that produced them. Never stripped (CLAUDE.md sec.12).
- Capture ONE raid and read the lines.
- Record the formula in `docs/RAID_BALANCE_AUDIT_2026-09-06.md` so the audit's other numbers can be recomputed
  against reality.

## 3. WHAT NOT TO DO
- **Do not change any scaling number in this ticket.** It measures. A tuning change made in the same pass
  makes it impossible to tell what the formula was.
- Do not proceed with the Hard/Extreme balance pass until this lands.

## 4. ACCEPTANCE
- [ ] The formula quoted from source, with file:line.
- [ ] The spawn trace lands and a captured raid's lines are pasted, showing base -> scaled for each defender.
- [ ] The 15-vs-10 discrepancy is EXPLAINED by the measured formula, or recorded as still unexplained - either
      is an acceptable outcome; an unproven explanation is not.
- [ ] The formula written into the audit doc.
- [ ] `REGRESSION_OK n/n` on a fresh log.

---

## 5. THE FORMULA, READ AT SOURCE — 2026-09-06 (edit-only lane, no Unity)

Every raid/garrison defender is built by the SAME shared helper class:
`Assets/_Modules/Village/World/Camps/GarrisonStatBlocks.cs`. Nothing else in the raid path
touches HP or contact damage. `Enemy.SetBaseStats` / `Enemy.ApplyDifficulty` (the dynamic-difficulty
pass) are called **only by `WaveManager`** (`WaveManager.cs:2239`, `:2468`) — the garrison path
never reaches them, so there is no hidden fifth multiplier.

*(All file:line below re-read at source AFTER this pass's edits landed, so they are the numbers in
the tree as committed — not the pre-edit numbers.)*

### 5.1 The chain, verbatim from source

**Stage 1 — the built stat block.** `GarrisonStatBlocks.BuildTypedDef` (`GarrisonStatBlocks.cs:116`)
returns a HARDCODED per-id block through `BuildGenericDef` (`:183`), which folds a global constant:

```csharp
// GarrisonStatBlocks.cs:38
public const float GlobalDifficultyMult = 1.2f;

// GarrisonStatBlocks.cs:194, :196
Hp            = hp  * GlobalDifficultyMult,
ContactDamage = dmg * GlobalDifficultyMult,
```

**Stage 2 — the level fold.** `GarrisonStatBlocks.ApplyLevelScale` (`GarrisonStatBlocks.cs:94-111`):

```csharp
if (def == null || level <= 1) return;
int over = level - 1;
float hpScale  = 1f + 0.08f * over;
float earlyDamageLevels = Mathf.Min(over, 10);
float lateDamageLevels  = Mathf.Min(Mathf.Max(0, over - 10), 10);
float dmgScale = 1f + 0.04f * earlyDamageLevels + 0.02f * lateDamageLevels;
def.Hp            *= hpScale;
def.ContactDamage *= dmgScale;
def.Height         = def.Height * (1f + 0.012f * over);
def.XpReward      += over * 3;
```

So: **HP +8% per level over 1, uncapped. Damage +4% per level for levels 2-11, then +2% per level
for levels 12-21, then FLAT** (`dmgScale` maxes at 1.60 and never grows again).

**Stage 3 — the per-config difficulty fold.** `RaidGarrisonSpawner.FoldDifficulty`
(`RaidGarrisonSpawner.cs:421-426`, the two multiplies at `:424-425`) multiplies HP and contact damage
by the config's `difficultyMultiplier`. **Stage 4 (boss only)** — `RaidGarrisonSpawner.cs:291-293`
multiplies by `bossHpMult` / `bossDamageMult` (each floored at 1).

**The level itself** is not the camp's authored level alone —
`RaidGarrisonSpawner.cs:166`:

```csharp
int enemyLevel = Mathf.Max(g.baseEnemyLevel, playerLevel + g.levelOffset);
```

A high-level player raising `playerLevel` raises every defender's level. "Camp level 3" is the
FLOOR, not necessarily the level the formula used on the night the 15 was measured.

### 5.2 Worked example — Orc Berserker at level 3, difficulty 1.0 (the Forsaken Camp)

Config values read at source from `Assets/StreamingAssets/Data/Canonical/scene-configs.json`,
config id **`raider_camp_small`** ("The Forsaken Camp"): `baseEnemyLevel` 3, `levelOffset` 0,
`difficultyMultiplier` 1.0, composition 7 x `orc-berserker` + 2 x `orc-shaman`.
Level 3 therefore holds **only while the hero is level 3 or below** (`enemyLevel =
max(3, playerLevel + 0)`).

| Stage | Source | HP | Damage / hit |
|---|---|---|---|
| authored literal | `GarrisonStatBlocks.cs:122` | 260 | 13 |
| x `GlobalDifficultyMult` 1.2 | `:194`, `:196` | **312.0** | **15.6** |
| x level 3 (`over`=2: hp 1.16, dmg 1.08) | `:98-104` | **361.9** | **16.85** |
| x `difficultyMultiplier` 1.0 | `RaidGarrisonSpawner.cs:424-425` | **361.9** | **16.85** |

Height 2.4 -> 2.458; XP 30 -> 36.

### 5.3 THE 15-vs-10 DISCREPANCY — the audit and the spawner read DIFFERENT sources

**This is the finding.** The audit's table (`docs/RAID_BALANCE_AUDIT_2026-09-06.md:187`) lists the
Orc Berserker at **117 HP / 10 damage**. Those are the `enemies.json` values verbatim
(`Assets/StreamingAssets/Data/Canonical/enemies.json`, `"id": "orc-berserker"`, `"hp": 117`,
`"contactDamage": 10`). **The raid spawn path never reads that row.** `BuildTypedDef` hardcodes
`260f / 13f` for `orc-berserker` (`GarrisonStatBlocks.cs:122`); only `orc-raider` reads the shared
roster (`GarrisonStatBlocks.cs:125-135`, `WildlandsRoster.BaseDef`). So every damage and
time-to-kill figure in the audit's A.3 table is computed against numbers that are not in the game.

`CombatAtbRegression` Check H already names this exact two-table divergence for `orc-raider` and
tags it `[FAIL-BY-DESIGN]` (`CombatAtbRegression.cs:750-793`) — it is a known, unclosed split.

**Leading hypothesis for the observed 15, NOT PROVEN:** 13 x 1.2 = **15.6**, which floors/rounds to
15 on a HUD. That value needs NO level scaling at all, which would mean the level formula was not
what produced the number. A competing path is level scaling plus hero mitigation
(`Enemy.cs:2187` applies `mitigated`, not raw `ContactDamage`). **The trace below decides it; do not
tune anything until it has been read.** No number was changed in this pass.

### 5.4 Comment-vs-code mismatches found while reading (report only, nothing changed)

1. `GarrisonStatBlocks.cs:93` says level scale adds "~5% contact damage"; the code (`:104`) is
   `0.04f` (4%) for the first ten levels and `0.02f` (2%) after.
2. `GarrisonStatBlocks.cs:36` documents `1.0 = current live feel`; the constant (`:38`) is `1.2f`.
3. `BuildTypedDef(string id, int level)` (`:116`) **ignores its `level` parameter entirely** — the
   scaling is done afterwards by `ApplyLevelScale`. The `case "troll"` arm (`:121`) additionally
   hardcodes `BuildTrollDef(2)`, i.e. threat 2, so a troll's threat scale never varies.
4. **`ogre` has no `case` arm** in the `BuildTypedDef` switch (`:118-147`), yet the Broken Garrison
   composition asks for 2 of them. It falls to `default` — a `Debug.LogWarning` plus a generic
   220 HP / 11 damage brute (`:144-146`). The audit's "2 Ogres" are not ogres in any authored sense.

### 5.5 The permanent trace (landed this pass)

One `FlowTrace.Step("EnemyScale", ...)` lives in
`GarrisonStatBlocks.TraceSpawnScale` (`GarrisonStatBlocks.cs:166-181`, the `Step` at `:170`). It is
called immediately before `EnemyFactory.Build` at three sites, after every fold that site applies:

- `RaidGarrisonSpawner.cs:295` `SpawnBoss` (after difficulty + boss multipliers)
- `RaidGarrisonSpawner.cs:355` `SpawnGuard` (after difficulty)
- `GarrisonController.cs:307` additive-camp guard (no further fold; `lvl` == `final`)

Line shape (**illustrative** — the numbers are §5.2's worked example, not a captured line; the
config id is the real one, `raider_camp_small`):

```
[Flow:EnemyScale] raid-guard[3] config='raider_camp_small' difficultyx1.00 id='orc-berserker'
  name='Orc Berserker' lv=3 | hp built=312 -> lvl=361.9 -> final=361.9
  | dmg built=15.6 -> lvl=16.85 -> final=16.85 | built already includes GlobalDifficultyMult x1.2
```

`built` is deliberately NOT divided back down to the authored literal — the line reports what was
measured, never a reconstruction.

**Source case that the trace exists:** `CombatAtbRegression.CheckEnemyScaleTracePresent` (Check H3)
asserts the one `FlowTrace.Step("EnemyScale"` in `GarrisonStatBlocks.cs` and the three
`TraceSpawnScale(` call sites, from source text — the spawn paths are MonoBehaviours that cannot be
invoked headless. Removal now fails the suite (CLAUDE.md §12: flag it off, never strip it).

### 5.6 Acceptance still OPEN after this pass

This was an edit-only lane (no Unity, no gates). Still owed by the CLI seat:
capture one raid and paste the `[Flow:EnemyScale]` lines; confirm or refute §5.3's hypothesis from
those lines; write the confirmed formula into `docs/RAID_BALANCE_AUDIT_2026-09-06.md` (and correct
its A.3 stat table, which is sourced from `enemies.json` rather than the spawn path);
`REGRESSION_OK n/n` on a fresh log.
