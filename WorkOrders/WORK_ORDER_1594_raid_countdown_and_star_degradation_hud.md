# WO-1594 — Raid countdown clock + stars that start lit and go dark

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane RAID)  
**Minted:** 2026-09-07 — program WO-1592  
**Priority:** P0 felt — “onscreen clock counting down starting with 3 stars; lose third then second as milestones pass”  
**Lane:** Raid HUD / scoring presentation  
**Files (expected):** `RaidHudController.cs`, `RaidScoring.cs` (presentation + projected stars), regression `RaidScoringRegression.cs`

---

## 1. Problem

Today the raid has a **180s clock** and **0–3 stars** computed from clear / boss / under-time (`RaidScoring`). The HUD shows timer + star diamonds, but the **felt story** the owner wants is CoC-adjacent:

1. Fight opens with **three stars lit**.  
2. A **countdown** is always on screen.  
3. As **milestones fail**, stars **extinguish** in order (3rd, then 2nd) — pressure you can read without math.

That is a **presentation + milestone contract**, not necessarily a new loot formula. Loot can keep using final settled stars; the live HUD must narrate the loss.

---

## 2. Creative proposal — “Honor clock”

### 2.1 Always-on readout (top raid band — keep `HudLayoutBands.RaidReadoutBand`)

| Element | Spec |
|---|---|
| **Countdown** | `M:SS` large, fills/shrinks bar; pulse under 30s (already partly there) |
| **Three stars** | Start **all lit** at raid engage (when clock truly starts — after WO-1520 engagement, not spawn) |
| **Destruction %** | Keep as secondary (shape + number, not hue-only) |

### 2.2 How stars go out (proposed milestones — owner may retune)

Think **time bands + failure floors**, not opaque formulas:

| Star | Starts | Extinguishes when… | Player read |
|---|---|---|---|
| ★★★ (third) | Lit at engage | Elapsed > **T3** (propose **90s**) **OR** hero dead (already caps at 2 — WO-1526) | “Speed honor lost” |
| ★★ (second) | Lit at engage | Elapsed > **T2** (propose **150s**) **OR** destruction% still below **D2** (propose **50%**) at T2 | “Raid going long / unfinished” |
| ★ (first) | Lit at engage | Only lost if raid ends with near-zero destruction (existing settle) — **never snuff mid-fight for time alone** | “You still get something if you cracked the camp” |

**At settle:** final stars = min(projected HUD stars, existing `ComputeStars` rules) so loot cannot exceed what the live HUD promised, and cannot invent stars the scorer forbids.

### 2.3 Juice (cheap, high feel)

- When a star dies: short **scale pop + dim** + one SFX (`SfxId` existing or soft UI click) + FlowTrace `star-lost reason=…`.  
- Optional toast once: `"3-star window closed"` / `"2-star window closed"` — ASCII, dismissible, not modal.  
- Colorblind: stars are **shape fill** (lit vs hollow), never red/green alone.

---

## 3. Implementation notes (grounded)

- Clock already lives on `RaidScoring` (`DefaultClockSeconds = 180`). Prefer **countdown display** of remaining, not only elapsed.  
- `RaidHudController` already paints diamonds + `n/3` — change **projection** to start full and snuff on milestones.  
- Wire star-loss to **engagement start** if WO-1520 has landed (clock must not eat stars during staging).  
- Do **not** change loot tables in this WO except to clamp to HUD honesty.

---

## 4. Owner rulings — ANSWERED 2026-09-07

**Q1.** Owner, verbatim: *"1594 you determine what is realistic and fair"* → **T3 = 90 s, T2 = 150 s,
D2 = 50 %** (locked). Half the clock to keep the third star; the last 30 s before timeout for the
second, and only if the camp is still under half razed; the first star is never snuffed mid-fight for
time alone.

**Q2.** Owner, verbatim: *"1594 q2 yes"* → hero death snuffs ★★★ immediately. **YES.**

*(Both rulings are quoted from commit `5c3c82de2` on `grok/raid-1593-1595`, which recorded them in
this file and its RESULT. That commit is NOT an ancestor of HEAD — the code was re-implemented onto
HEAD rather than merged. See the RESULT for what differs.)*

---

## 5. Acceptance

1. At engagement, HUD shows **3/3** lit stars + countdown from 3:00 (or authored).  
2. Crossing T3 snuffs the third star with visible feedback; crossing T2 snuffs the second if D2 unmet (or per Q1).  
3. Staging (pre-engagement) does **not** advance the honor clock.  
4. End screen stars ≤ what the HUD showed in the last second of the fight (no surprise demotion unexplained).  
5. Regression: pure function tests for milestone → projected stars; `COMPILE_GATE_OK`.  
6. Open PNGs of HUD at 0s / post-T3 / post-T2.

## 6. Not in scope

KayKit art (1593), AI roles (1595), army caps, garrison HP retune.

---

## 7. Owner ruling 2026-09-09 - THE MILESTONES ARE TUNABLES

**Owner, verbatim choice:** ***"Tunables with those defaults"***

The WO-1594 RESULT flagged T3 / T2 / D2 as compiled constants and asked for a ruling rather than
promoting them unilaterally (RESULT section 8, third bullet). She ruled: they go on the
`RemoteTunables` rail, **at the values already ruled in section 4** - 90 s, 150 s, 50 %.

| Milestone | Key | Kind | Shipping default | Was |
|---|---|---|---|---|
| T3 - the third star snuffs | `raid.honorThirdStarSeconds` | int seconds | `90` | `const float HonorThirdStarSeconds = 90f` |
| T2 - the second star may snuff | `raid.honorSecondStarSeconds` | int seconds | `150` | `const float HonorSecondStarSeconds = 150f` |
| D2 - destruction needed to keep the second | `raid.honorSecondStarMinDestructionPct` | int PERCENT | `50` | `const float HonorSecondStarMinDestruction = 0.50f` |

- **The defaults ARE today's behaviour, byte for byte.** No row, no network, no parse, no registry
  entry all resolve to 90 / 150 / 50, so this change is behaviour-neutral on its own.
- **D2 is an integer PERCENT because the rail carries no floats.** `RaidScoring` clamps `0..100` and
  divides by `100`; `Pct` is in the key name so a console reader cannot mistake `50` for a fraction.
- **Every read goes through `RemoteTunables.SpecFor(key)` BEFORE `Int(key)`** - the same shape
  `RaidDeployController.StagingCeilingSeconds` uses for `raid.stagingCeilingSeconds` (WO-1095). That
  guard is load-bearing, not defensive: `Int` answers `0` for an unregistered key, and `T3 = 0`
  would put the third honor star out on the FIRST FRAME of every raid **and**, through `Finalize`'s
  `min(settle, honor)` clamp, silently cap every raid in the game at two stars.
- **These are not only presentation.** Lowering T3 lowers what raids PAY. That is stated on the doc
  row so the owner is not surprised by it at 2am.

**Acceptance for this ruling (in addition to section 5):**

7. All six sources move in ONE change: `RemoteTunables` registry + defaults, the defaults oracle's
   `ExpectedKnobCount` and `ExpectedDefaults`, `api/_lib/tunables.js`, the manifest cards, the
   regenerated `api/_lib/tunable-manifest.generated.json`, and `docs/PROD022_TUNABLE_FLAGS.md`.
8. `node tools/gen-tunable-manifest.mjs` prints `TUNABLE_MANIFEST_GEN_OK` and
   `node --test test/tunables-manifest.test.js` is green.
9. `RaidWatchdogHonorRegression` asserts against the SHIPPING DEFAULT consts, never the live
   properties - an oracle that read the property it tests would measure the thing against itself.
