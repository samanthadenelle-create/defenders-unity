# WORK ORDER 1707 - White / untextured town in the seq5020 + seq5023 softlock screenshots

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-14 by the CLI lead from F8 triage
**Source of truth:** `docs/F8_TRIAGE_2026-09-14.md` sections 1 (cluster C, `:19`) and 4 (`:103-116`).
**Class (CLAUDE.md section 12):** **built-but-invisible** - geometry renders, surfaces are colourless.

---

## 1. Objective

Find, with captured data, why walls and buildings render as **flat white untextured blocks** in the
device boots behind F8 seq 5020 and seq 5023, then fix that one dead step. Do not fix anything else.

## 2. Evidence (each line opened this session)

- **seq5023** - `logs/f8-inbox/capture-device-20260911-223819-seq5023.md`, device utc
  `2026-09-12T02:23:48.7890780Z`, kind `possible_softlock`, scene `Main_Castle_Overworld`.
- **seq5020** - `logs/f8-inbox/capture-device-20260911-223817-seq5020.md`, device utc
  `2026-09-12T00:41:49.8987720Z`, same kind, same scene.
- Screenshots, **opened by this lane**:
  - `logs/f8-inbox/device/SM02G4061955851/break_02_possible_softlock.png` (mtime 2026-09-11 21:23:49) -
    Thrain **Lv 1**, **200** gold. Walls, keep and every building in frame are **flat white/unshaded**;
    the world tree, NPCs and grass are correctly textured. Floating yellow cubes present; both centre
    HUD banners are empty of text.
  - `logs/f8-inbox/device/SM02G4061955851/break_03_possible_softlock.png` (mtime 2026-09-11 19:41:50) -
    Thrain **Lv 2**, **221** gold, "Wave 2 / Next wave in 8m 11s" - **the world is live, so the
    "softlock" is a stationary player, not a freeze**. The near wall section and the left building are
    **pure white**; the wall behind them and the house to the right are correctly textured. So the
    defect is **per-object, not a global shader/lighting failure**.
- **Mapping caveat, verbatim from both capture files:** the list is "CANDIDATES by kind and recency, not
  a proven 1:1 match to this entry"; 5020->`break_03` / 5023->`break_02` is the doc's UTC-5 rule (`:6-9`,
  `:103-105`), not the harness's.
- **Counter-evidence, do not discard:** `break_01_possible_softlock.png` (->seq5012) is a different save
  rendering fully coloured (doc `:116`), and the 22:31 boot that logged the same Addressables INIT
  failure rendered **textured** walls (doc `:126-132`).

## 3. Three candidate mechanisms - the trace must SPLIT them, not the reviewer

1. **Albedo bound to zero slots** (triage cluster B, doc `:18`, `:120-125`) - emitters
   `Assets/_Modules/Village/Catalog/StructureFactory.cs:497` and
   `Assets/_Modules/Village/HubStructureVisualInjector.cs:728`. Both read `M` in `git status --short`
   run by this lane - **uncommitted**. **UNPROVEN that the device APK predates the fix**: builds here are
   cut from the working tree, not HEAD. Read the build's version/commit stamp off the device first.
2. **Pending-art proxies** - `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:1096-1104`
   says in its own text: *"not one structure address was ever requested and the whole town rendered as
   pending-art proxies."* That is literally this symptom.
3. **Missing R2 bundles** (CLAUDE.md section 16) - a build whose bundles were never pushed shows
   "placeholder buildings, no error on screen"; detected only by `RemoteProviderException` / `404`.

## 4. INSTRUMENT FIRST - read these before any edit

**Evidence gap, measured:** `ls logs/debug/ | grep -i logcat` returns exactly one file,
`seeker-365962-logcat.txt` (the 22:31 flag boot). **There is no logcat for the 19:41 or 21:23 boots** -
those two screenshots have no trace. Closing that gap is step one. On a white-town boot, read in order:
1. `[Flow:StructureAssets]` (`StructureContentWarmer.System = "StructureAssets"`, `:123`) - did the
   locator enumeration run, and were structure addresses requested at all?
2. `[Flow:Structure]` from `StructureFactory` and `[Flow:Hub]` from `HubStructureVisualInjector` - the
   "RESOLVED but bound onto ZERO of N material slot(s)" line.
3. Any `RemoteProviderException` / `HTTP/1.1 404` on the R2 host (CLAUDE.md section 16).

## 5. Acceptance criteria

1. A **fresh captured boot** reproducing the white-town state, with the `[Flow:*]` lines above quoted
   verbatim in the RESULT, naming which of the three mechanisms fired. No edit before this exists.
2. The dead step named in 1 is fixed, and a **post-fix screenshot of the same scene** is attached
   showing the same structures textured. (Screenshots are primary evidence for visual defects.)
3. A regression that fails on the proven mechanism (e.g. an albedo-slot bind assertion, or a
   structure-address-requested count) - not a source-text lint.
4. If mechanism 1 fires, this ticket also carries the visual proof `docs/WORKLOG_2026-09-11.md:18`
   records as still owed for cluster B.

## 6. What NOT to touch

- **Not the softlock detector.** WO-1237 (`softlock detector fires on afk`) is CLOSED 2026-08-26 (doc
  `:19`); both screenshots show a live world and a stationary player. Detector tuning = separate ticket.
- Do not ack seq 5020 / 5023 until this ticket is filed against them (doc `:94-95`).
- No scene hand-edits, no R2 push from this lane.
- Not the grass shimmer or the frozen `+4` indicator - doc `:79-80` marks both UNPROVEN; WO-1218 closed
  the shimmer as owner-PASS.
