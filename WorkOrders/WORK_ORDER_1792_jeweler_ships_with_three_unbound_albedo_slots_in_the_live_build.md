# WORK ORDER 1792 — The Jeweler renders untextured in town for 9 of today's live players, on the build 10 of today's ids run (`2026.09.16.371701`)

**Status:** READY TO IMPLEMENT
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
