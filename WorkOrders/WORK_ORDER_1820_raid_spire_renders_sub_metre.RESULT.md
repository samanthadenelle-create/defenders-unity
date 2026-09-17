# WO-1820 RESULT — the RaidSpire rendered at 0.14 m

**Status:** IMPLEMENTED
**Date:** 2026-09-17
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*` + `Assets/Editor/Regression/RaidPostOrientationRegression.cs`
**Not done by this lane (by rule):** gate, commit, push. PO felt-verify + close (CLAUDE.md §13).

---

## The cause

`ReplaceChildrenWith` parents the clad with `SetParent(parent, false)`, so the clad keeps its OWN prefab
`localScale` **and** inherits the host's — the prefab's authored scale is applied **twice**. All four
hosts fitted correctly to 14.40 m (`SPIRE FIT config …`); the fit was never the bug.

```
camp   : 9.600          x (1.000 x 1.500)  = 14.40 m   correct
others : 0.010 x 14.366 x (0.010 x 100.2)  =  0.1439 m  (measured 0.14)
```

The error factor IS `prefabScaleBefore`. ⛔ **The camp was right by coincidence** — its art authors
`localScale 1.000` and squaring 1.000 is harmless; `tower_arcane_spire` authors 0.010. Reading the camp
as "the working case to copy" would have hidden the rule.

**Gameplay, not only visual:** `PlaceSpire` passes the achieved 14.40 into `RaidSpire.Configure`
(`RaidBaseGenerator.cs:1219`) and `EnsureHittable` (`RaidSpire.cs:200`) sizes the hero contact collider
from it — so three scenes had a 14.4 m hit box around a 14 cm object.

## The rule

> **The spire clad renders at `RaidSpire.VisualHeight` — the height the bake fitted and RECORDED.**

Already the collider's authority, so art and collider now agree by construction. Not re-derived:
`SpireMonumentMultiplier/Min/Max` are `internal` to `DeNelle.EditorWallTools`, which the regression
assembly cannot reference, so a formula in either oracle would have been a copy that drifts.

## Markers — fresh logs, judged by the word

| Run | Log | Marker |
|---|---|---|
| Compile (pre) | `Builds/wo1820-compile.log` | `COMPILE_GATE_OK` |
| BEFORE audit | `Builds/wo1820-audit-before.log` | `RAID_POST_AUDIT_FAIL: 3 defect(s)` — the 3 spires, `99 % off` |
| Rebake | `Builds/wo1820-rebake.log` | all four spires `fitH=14.4 cladH=14.4 delta=+0 (0.0 % off)`, `boundsY=[0..14.4]` |
| Chain | `Builds/wo1820-chain.log` | **`OWNED_TOWN_CHAIN_OK`** |
| AFTER audit (1) | `Builds/wo1820-audit-after.log` | `FAIL: 3` — **my seat pin, not the art** (see below) |
| AFTER audit (2) | `Builds/wo1820-audit-after2.log` | **`RAID_POST_AUDIT_OK 63`** |
| Gate RED proof | `Builds/wo1820-redproof.log` | `RAID_POST_ORIENTATION_FAIL **35** post(s) bad of 63` + a `the RAID OBJECTIVE renders` line |
| Compile (final) | `Builds/wo1820-compile4.log` | **`COMPILE_GATE_OK`** |
| Full suite | `Builds/wo1820-regression3.log` | **`REGRESSION_OK 564/564 suites`** |

**Suites the lead flagged, verified green on that log by their own lines:**
`[owned-town-template-identity] 169 census structure(s) in all three scenes` (unchanged) ·
`[raid-spire-siege] PlaceSpire guarded; PlaceTowerProp guarded` ·
`[raid-post-orientation] 63 corner post(s)/watchtower(s) across 4 baked scene(s)` ·
`[raid-keep-reach] scanned 5 baked raid scene(s)` · `[tower-wall-los] TOWER WALL LOS OK` ·
`[raid-wall-audit]` margins **garrison 2.99, IronBastion 1.99, mage_enclave 1.99, camp 2.00** — all > 0
and unchanged from WO-1817. `raid-wall-continuity` and `RaidPolishSavedProof` emit no tagged line on
this log; the suite reports **0 skipped**, so nothing registered was silently passed over, but I did not
read either file and cannot say more than that.

⚠ **TWO FALSE GATE SIGNALS SURVIVED TO BE EXPLAINED, BOTH WORTH KNOWING:**
- `Builds/wo1820-compile2.log` read `COMPILE_GATE_FAIL :: 1 check(s) failed` — a **WebGL-pass log
  flake**, not code. Unity interleaved a `-r:"C:/Program Files/…"` response-file fragment into a
  `Packages\com.solana.unity_sdk\…WebGLInput.cs … CS1069` line, so the gate's `Packages/` classifier
  could not match it and the advisory was promoted to a hard error. Proof it is not mine: the only
  files changed since the passing `wo1820-compile.log` were EDITOR assemblies, which are never
  compiled into a WebGL *player* script build; the glued line appears in that one log and in no other
  (`glued=0` in compile/compile3/compile4); and the re-run was clean. **The gate's classifier is
  defeated by an interleaved line — a real robustness gap in `CompileGate`, worth its own ticket.**
- `Builds/wo1820-regression.log` is a **void run**: `executeMethod class 'DataRegression' could not be
  found`. The entry point is `DeNelle.Editor.DataRegression.RunAll`, **not**
  `DeNelle.Editor.Regression.DataRegression.RunAll` — the class sits in namespace `DeNelle.Editor`
  (`DataRegression.cs:52`) while its siblings are in `.Regression`. Exit code 0, no marker: exactly the
  "judge by marker, never exit code" law (CLAUDE.md §8).

**Spire cladH per scene, after:** garrison **14.40**, IronBastion **14.40**, mage_enclave **14.40**,
camp **14.40** (was 0.14 / 0.14 / 0.14 / 14.40).

**RED proof is the load-bearing one.** WO-1817's proof flagged 31 and predates the spire branch; this
one flags **35 = 31 watchtowers + 4 spires**, which is what proves the spire branch actually executes.
Reverted and re-hashed byte-identical (`sha256 c5088c0d…f32f4`).

## The judgement call, stated plainly

AFTER audit run 1 red'd 3 spires at `minY 1.5 / 1.5 / 0.8` — **and that was MY PIN being wrong, not the
art.** The spire does not stand on the ground: `ReseatSpireOnKeepPlatform` deliberately lifts it onto
the KeepPlatform slab — *"1.5 m on the castle kits, 0.8 m on dungeon-stone"*
(`RaidBaseGenerator.cs:2184`), MEASURED off the slab — because WO-1749 proved a ground-seated spire ends
up INSIDE the slab, costing 1650 `PathPartial` and ZERO `PathComplete` on the owner's Seeker run. The
flagged lifts match the documented per-kit slab **to the centimetre**, and this bake's own
`OWNED_TOWN_SPIRE_RESEAT_OK` reads `base y 1.500 vs KeepPlatform top y 1.500, delta 0.0000m`.

So the spire is EXEMPT from the ground-seat pin in both oracles, with the reason written into both. The
platform-seat invariant is **not** re-implemented there: it already has an owner that measures it
properly, and a second copy would have been the duplicated state CLAUDE.md §2/§5/§16 describes — this
time with the copy being the wrong one. The spire's HEIGHT stays pinned.

## Bounce folded in (lead's combined gate, `REGRESSION_FAIL 563/564`)

`[A-missing-dependency]` hollow pass at `RaidPostOrientationRegression.cs:366`. Fixed, and the two
sibling hollow guards in the same method were fixed with it rather than left for the next lint pass.

⚠ **THE FIRST FIX DID NOT CLEAR THE LINT, AND THE REASON IS WORTH RECORDING.** Adding the
`faults.Add(...)` assertion left `REGRESSION_FAIL 563/564` still pointing at the same guards
(`Builds/wo1820-regression2.log`). The cause is not the logic — it is the accumulator's NAME.
`HollowPassScanner.AssertionCall` (`Assets/Editor/Regression/HollowPassScanner.cs:160-162`) recognises
an assertion only as `failures|fails|failure|errs|errors|problems|issues|f` + `.Add(`; this method's
parameter was named **`faults`**, so a real assertion was invisible to the lint. Renamed the parameter
to `failures` — **not** silenced with the `hollow-pass-ok` opt-out, which is for genuinely hollow sites
and would have been a lie here. `Judge`'s own `faults` local is untouched (its guards return the joined
string, a shape the scanner already clears). Sites fixed:
- a `Watchtower_*` with **no DefenseTower** → named failure (it also means an unarmed enemy turret shipped);
- one with an **empty CatalogId** → named failure;
- a spire whose recorded `VisualHeight` is below the `[Min(1f)]` floor → named failure (Configure never ran);
- `OptsFor(...).FitHeight <= 0` → named failure (malformed row).

## Files changed (this ticket)

| File | State |
|---|---|
| `Assets/_Modules/Village/World/Camps/RaidSpire.cs` | **M** — the ONE out-of-silo line: additive `public float VisualHeight => _visualHeight;` over an already-serialized field. No behaviour change. Approved by the lead. |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | **M** — `AuthoredHeightFor` (Watchtower→cadence, RaidSpire→VisualHeight, CornerPost→0) |
| `Assets/Editor/WallTools/RaidPostAudit.cs` | **M** — collects/judges/photographs `RaidSpire`; spire exempt from the ground-seat pin |
| `Assets/Editor/Regression/RaidPostOrientationRegression.cs` | **M** — spire pin, same exemption, hollow-guard fixes |
| 4 `RaidBase_*.unity` + 4 `NavMesh.asset` + `RaidGround/*.mat` + `OwnedTown_IronBastion.unity` + `ArenaPractice_IronBastion.unity` + `IronBastionTemplate.json` | **rebaked / re-derived** |

`python tools/gate_brace.py` clean, NUL 0 on every `.cs` touched.

## Not proven / open

1. **PO felt-verify on device is still required** — everything above is headless (§13).
2. **`CornerPost_*` still renders at clad-native size** (1.11 / 7.52 / **17.96** m) — WO-1817 §4, unruled.
3. **Whether 14.40 m is the right CREATIVE height** is the owner's call; this makes the render agree
   with what the generator, the collider and the component already said.
4. `RaidSpireSiegeRegression` and `RaidPolishSavedProof` were **not read** by this lane — both may pin
   baked spire state. They are in the full-suite run; watch them there.
