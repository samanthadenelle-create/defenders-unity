# WORK ORDER 1792 — The Jeweler renders untextured in town for 9 of today's live players, on the build 10 of today's ids run (`2026.09.16.371701`)

**Status:** READY FOR LEAD REVIEW

> **Implementation lane hand-back, 2026-09-17 — code written, lane checks green, NO Unity run.**
> Per the lead's brief this lane HELD before any batchmode/CompileGate/DataRegression execution;
> the lead gates the combined tree once while other lanes are open. So `COMPILE_GATE_OK`,
> `REGRESSION_OK <n>/<n>` and `UI_CAPTURE_OK` (§4 AC bullet 3) are **NOT claimed here** — they are
> the lead's to produce, and the headless PNG read (AC bullet 1) still needs doing.
>
> **Answer to §3.1 — the question the WO left open.** It is option 1: `Glass` / `Icon` / `Rim` are
> **not building art at all** and were never meant to be textured. They are the three
> `SpriteRenderer` quads `InteractableSign.BuildQuad` creates (`InteractableSign.cs:199/201/206`) —
> the T-034 identity sign floating ~4.2 units above the interactable. The proving line's
> `material='Sprites-Default (URP)'` is `TripoMaterialFixer`'s own `srcName` (`src.name + " (URP)"`),
> so the source material was Unity's builtin SpriteRenderer default. Renderer names, count (3) and
> material all agree; nothing else in the tree produces that combination.
>
> **So it was TWO defects, not one, and the WO only knew about the first.** (a) a FALSE `error`-level
> line 68x/day on a healthy object — a SpriteRenderer supplies its texture at DRAW time from its
> `sprite`, never from a `_BaseMap` on its shared material, so `GetAlbedo` correctly finds nothing;
> and (b) a REAL, unreported visual defect — the rebuild **replaced** `Sprites-Default` with an
> opaque URP/Lit, stripping the sprite's alpha and shape, so the sign's gold bezel, dark-glass
> backdrop and type icon all rendered as **three plain white lit quads**. The three white things to
> look for in the AC-1 PNG are therefore those plates above the Jeweler (revealed within 10 units,
> billboarded), **not** the building body — the same capture shows 1 of 4 slots WITH an albedo bound,
> and this lane did not prove anything about the body's own look either way.
>
> **Fix:** one shared predicate `TripoMaterialFixer.IsSpriteOverlay`, called from BOTH the rebuild
> loop and the verify loop so the two skip sets cannot drift, plus a `FlowTrace.Step` naming each
> skipped renderer and the reason (AC bullet 2). No `.unity` edit, no art relink, no texture needed
> from a human. Scope is `SpriteRenderer` **only**: Particle/Line/Trail renderers are the same class
> of concern but no capture shows one under a fixer host, so widening would be a guess (§11B) — they
> keep today's behaviour and are instead instrumented with a `FlowTrace.Once` so the next capture
> settles it with data.
>
> **Files:** `Assets/_Modules/Core/TripoMaterialFixer.cs` (edited),
> `Assets/Editor/Regression/TripoSpriteOverlayRegression.cs` (new, 6 cases, marker
> `TRIPO_SPRITE_OVERLAY_OK`).
> **Registration:** the lead wired the suite in as `[tripo-sprite-overlay]`
> (`DataRegression.cs:445`), so it counts toward `REGRESSION_OK <n>/<n>`.
>
> **Follow-up landed 2026-09-17 — the gate caught a hollow pass in the suite itself, and it was a
> REAL defect in my test, not a false positive.** `HollowPassScanner` arm D
> (`D-vacuous-against-absent-fixture`) flagged `RunSourceCases`: every assertion in that method sat
> inside `if (fixer != null)` / `if (sign != null)`, so a reader of the method could see no floor.
> `ReadSource` *did* record a `[source-missing]` failure, so it would not literally have greened — but
> "it happens to fail via a side effect two calls away" is exactly the reasoning that let
> `RaidCooldownRegression` case 5 measure nothing while reporting green on 2026-08-21. Restructured to
> the shape the scanner documents as the sanctioned exoneration — producer records the miss,
> `if (fixer == null) return;` immediately — so all six cases now sit at method depth
> **unconditionally**, and an absent source is a loud failure rather than a silent skip. No opt-out
> token used, and the check is satisfied structurally rather than suppressed. Re-verified:
> `GATE_BRACE_SUMMARY bad=0 of 2`, raw 20/20 and 93/93, NUL 0.
>
> **Second follow-up 2026-09-17 — the `[sprite-not-rebuilt]` failure on the combined-tree run was a
> FALSE FAILURE FROM MY OWN TEST. The fix was working; the assertion was wrong.** The run's own output
> is the proof: it reported the sprite shader as changing *from*
> `Universal Render Pipeline/2D/Sprite-Unlit-Default` *to the identical string*. An unchanged shader
> name means the material was never swapped. The case tripped on its second clause,
> `spriteShaderAfter.StartsWith("Universal Render Pipeline/")` — and **in a URP project the builtin
> SpriteRenderer material is ALREADY on a URP shader**, so that prefix was true before the fixer ever
> ran. A family-prefix test cannot distinguish an untouched URP sprite material from one rebuilt to
> URP/Lit. This is the same "verified the wrong property" trap `VerifyAllRenderersUrp`'s own comment
> warns about ("THE SHADER IS NOT THE WHOLE ANSWER"), pointed at a false FAILURE instead of a false pass.
>
> Corrected: material **identity** (`GetInstanceID`) is now the primary signal — a rebuild always
> assigns a different Material instance — with the **exact** rebuild target `Universal Render
> Pipeline/Lit` (`TripoMaterialFixer.cs:262`) as the belt. Case 2 likewise asserts that exact string
> rather than the prefix. The failure text now prints the instance-identity fact alongside both
> shaders so a future failure here cannot mislead the same way. Dead `UrpPrefix` const removed rather
> than left for comments to reference.
>
> **Re-proved the coordinator's specific concern at source this turn:** `IsSpriteOverlay(r)` is called
> at `TripoMaterialFixer.cs:321`, inside `Run()` (255-460), and `continue`s there — **before** the only
> two mutation sites, `matsRef[i] = sharedMat` (:444) and `r.sharedMaterials = matsRef` (:447) — and
> again at `:580` in `VerifyAllRenderersUrp`. The rebuild loop does consult it; nothing touches a
> sprite overlay's material. Re-verified: `GATE_BRACE_SUMMARY bad=0 of 2`, raw 20/20 and 93/93, NUL 0.
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead from the 1791-1795 block; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** Structure art / material degrade — the Jeweler prefab + `TripoMaterialFixer` / `StructureAssetLoader`. NO `.unity` edits, no gameplay.
**Priority:** P1 — 9 distinct live player ids today, in the hub scene, on `2026.09.16.371701`.
**Lane disjointness:** file-disjoint from WO-1791 (analytics), WO-1793 (`api/admin/db.js`), WO-1794 (tutorial emitters), WO-1795 (`api/game/save.js`).

---

## 1. THE PROVING LINES (production Neon, read-only, 2026-09-16 UTC)

`playtest_break` rows whose message begins `[Flow:TripoMatFix]`: **68 hits across 9 distinct
player ids**, every one in `Main_Castle_Overworld`. Grouped by the object named in the message:

```
 51 hits / 9 players -> 'Jeweler'
 17 hits / 9 players -> (the VERIFY summary line, which names no object)
```

The three verbatim messages, each 14 hits / 6 ids:

```
[Flow:TripoMatFix]   NO ALBEDO on 'Jeweler' renderer 'Glass' slot 0: material='Sprites-Default (URP)' shader='Universal Render Pipeline/Lit' tint=(1.0…
[Flow:TripoMatFix]   NO ALBEDO on 'Jeweler' renderer 'Icon'  slot 0: material='Sprites-Default (URP)' shader='Universal Render Pipeline/Lit' tint=(1.00…
[Flow:TripoMatFix]   NO ALBEDO on 'Jeweler' renderer 'Rim'   slot 0: material='Sprites-Default (URP)' shader='Universal Render Pipeline/Lit' tint=(1.00…
```

and the summary, 17 hits / 9 ids:

```
[Flow:TripoMatFix]   Jeweler: VERIFY UNTEXTURED — all 4 slot(s) on a URP shader, but 3 slot(s) have NO albedo bound and no miss/fallback tint.
```

**The build is named.** (⚠ It is the build **10 of today's 17 attributable ids** run; 7 run
`2026.09.07.359722`. Which binary the dApp Store currently serves was NOT read in this lane.)
Joining each offending id to its own
`session_start.appVersion` for the same day:

| player id (prefix) | hits | appVersion |
|---|---|---|
| `CHKKFkPGz8VZ…` | 8 | `2026.09.16.371701` |
| `CMV5WumRnsvF…` | 4 | `2026.09.16.371701` |
| `guest-local-4b2fc333` | 4 | `2026.09.16.371701` |
| `guest-local-92be2871` | 4 | `2026.09.16.371701` |
| `guest-local-f6035f25` | 4 | `2026.09.16.371701` |
| `DKYiurDc3Liz…` | 4 | `2026.09.16.371701` |
| `3xLP7UyxD6vK…` | 4 | (no `session_start` under this id — see WO-1791 §3) |
| `2ZurmHauf4fD…` | 4 | (same) |
| `unverified` | 32 | four versions mixed (WO-1791 §1) |

This is the build-version fact WO-1774 has been blocked on for its own object
(`WORK_ORDER_1774_untextured_slab_in_town_courtyard_tester_video.md:3` = `NEEDS DATA`,
"BUILD UNDER TEST IS UNKNOWN"): today's players run `2026.09.16.371701` and
`2026.09.07.359722`. **That does not close 1774** — 1774's object is a player-built WALL, this
ticket's object is the Jeweler — but the version question is answered for both.

## 2. WHAT THE DATA ALREADY RULES OUT

- **It is not a missing R2 push (CLAUDE.md §16).** The only bundle failures today read
  `UnityWebRequest result : ConnectionError : Cannot connect to destination host` — a device with no
  network at 10:30:15-10:30:29 UTC — not `HTTP/1.1 404 Not Found`. Do not chase a push.
- **It is not a shader assignment failure.** The instrument says all 4 slots ARE on
  `Universal Render Pipeline/Lit`; three of them have **no albedo texture bound** and, worse, **no
  miss/fallback tint**, so the degrade path that is supposed to make a missing texture visible as a
  deliberate tint did not run either.
- `material='Sprites-Default (URP)'` on a building renderer is itself the tell: three renderers
  (`Glass`, `Icon`, `Rim`) are carrying a sprite material, not a structure material.

## 3. WHAT TO DO

1. Open the Jeweler prefab and establish which of `Glass` / `Icon` / `Rim` are meant to be textured
   at all. An `Icon`/`Rim` renderer may legitimately be a tinted overlay — in which case the defect
   is that `TripoMaterialFixer` has no rule for it and reports a FALSE error 68 times a day, and the
   fix is the rule, not a texture.
2. For the renderers that ARE meant to be textured: bind the albedo, or make the fallback tint fire
   so the player sees an intentional colour instead of white.
3. Either way the instrument must stop firing `error`-level on a healthy object — an error that is
   expected is an error nobody reads (CLAUDE.md §12).

## 4. ACCEPTANCE CRITERIA

- [ ] A headless capture of `Main_Castle_Overworld` with the Jeweler present emits ZERO
      `[Flow:TripoMatFix] NO ALBEDO on 'Jeweler'` lines. Open the PNG; the Jeweler is not white.
- [ ] If any renderer is legitimately untextured, the instrument classifies it as such by NAME, with
      the reason written at the call site.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` + `UI_CAPTURE_OK` on fresh logs.

## 5. WHAT NOT TO TOUCH

`.unity` scene files. WO-1774's wall path. The raid-scene `[Flow:RaidArt]` boundary-ring lines
(156 hits today, single unattributed id, raid scenes) — those belong to WO-1747 / WO-1758.
