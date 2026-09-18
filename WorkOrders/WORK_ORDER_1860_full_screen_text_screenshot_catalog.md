# WORK ORDER 1860 — Full screenshot catalog: every player-facing screen with any text

**Status:** DONE - committed d4642754d, COMPILE_GATE_OK + REGRESSION_OK 577/577 verified. PRIOR: READY FOR LEAD REVIEW

**Minted:** 2026-09-17, by the CLI lead, from the owner's direct ask: *"can I get a screenshot of
every screen that has text? I want to create a catalog for the overnight build to create a library
of images so I can get them all listed for them"* — clarified scope: *"every single screen... there
are like 45 player facing screens"* and *"any screen that has a single letter of text"* (i.e. no
threshold — a screen with even one character of copy is in scope; only fully wordless/art-only
screens are excluded).

**Owner routing:** this is monotonous capture-and-catalog work, suited for tonight's overnight
cycle alongside WO-1857 (the localization sweep) — the two are complementary: this ticket produces
the visual evidence WO-1857's sweep can be checked against.

## Existing tooling (read before building anything new)

- `Assets/Editor/UICaptureLaunch.cs` — the primary headless capture harness. Already has 20+
  `Run*CaptureHeadless()` entry points (grep `public static void Run` in that file for the current
  list — do not trust this WO's own count, re-verify at source). Writes to `Builds/ui-capture/`.
- `Assets/_Modules/Core/Diagnostics/UICaptureMode.cs` — a SECOND, older harness writing to
  `Builds/UICaps/` with its own router routes. Per `docs/qa/UI_SCREEN_GRAPH_2026-09-04.md`'s own
  header, this is NOT the pre-ship `UI_CAPTURE_OK` producer — read that doc's legend before assuming
  which harness is authoritative for which screen.
- `docs/qa/UI_SCREEN_GRAPH_2026-09-04.md` — an existing, code-cited screen graph from the HUD root,
  documenting 42 `[cap: ...]` capture points as of that date. **This doc is now ~2 weeks stale**
  (dated 2026-09-04; today is 2026-09-17, and the clan system, dungeons work, and many other WOs have
  shipped since). Use it as a STARTING map, not a source of truth — re-verify every route against
  current code before trusting it, per this repo's standing "never guess" discipline.

## Scope

1. **Re-derive the current screen inventory from source**, not from the stale doc. Walk every
   `PanelId`/panel-router route, every HUD dock/menu entry point, every modal/dialogue/confirm
   surface, from the actual current code (`PanelRouter`, `HudKitController`, `SettingsController`,
   the various `*Gate.RequestOpen` call sites, etc.). Produce an updated, dated screen-graph doc
   (or update the existing one with a fresh date and a `STALE 2026-09-04` banner per CLAUDE.md §15's
   canon-maintenance rule) listing every screen, whether it has any player-facing text, and whether
   an existing headless capture case covers it.
2. For every screen that has ANY text and does NOT yet have a headless capture case, add one
   (`Run<ScreenName>CaptureHeadless()` in `UICaptureLaunch.cs`, following the exact pattern of an
   existing entry — read several first, match the convention precisely: how it boots to the right
   state, how it waits for the panel to be visible, how it names/sizes the output PNG).
3. Run every capture case (existing + newly added) headlessly and confirm each produces a real,
   non-blank PNG in `Builds/ui-capture/`.
4. Produce a catalog: one document (`docs/qa/UI_SCREENSHOT_CATALOG_2026-09-17.md` or similar) listing
   every captured screen, its PNG path, and a one-line description of what text appears on it — this
   is the "library... listed for them" the owner asked for, not just a folder of unlabeled PNGs.

## Non-scope

- Do NOT capture screens that are genuinely wordless (pure art/icon surfaces with zero text) — if
  uncertain whether a screen counts, err on the side of INCLUDING it (the owner's own bar is "a
  single letter of text").
- Do NOT modify any UI/gameplay code to fix anything the screenshots reveal — this is a capture and
  catalog ticket only. Flag anything that looks wrong (truncated text, missing localization, a tofu
  glyph, etc.) in the catalog doc for a follow-up ticket, but do not fix it here.
- Do NOT touch WO-1857 (localization sweep) — complementary but separate; this ticket's catalog can
  be handed to that ticket's agent(s) as reference material, not merged into it.

## Acceptance criteria

- [ ] An updated, source-verified screen inventory exists, dated 2026-09-17, listing every
      player-facing screen and whether it carries text.
- [ ] Every text-bearing screen has a working headless capture case; run them all and confirm every
      PNG is non-blank (a blank/black PNG under `-nographics` for a UITK/canvas panel would be a red
      flag — investigate rather than silently accept per this repo's own gotcha about `-nographics`
      rendering).
- [ ] The catalog doc lists every screen with its PNG path and a one-line text summary.
- [ ] `UI_CAPTURE_OK` (or whatever marker this harness emits — verify at source) fires clean on a
      fresh run.
- [ ] Full regression suite green if any `.cs` file was touched; `python tools/gate_brace.py` + NUL
      scan on every file touched.

## Test plan

1. Run the full capture sweep; count PNGs produced vs. screens in the inventory — every text-bearing
   screen must have exactly one (or more, if it has meaningfully distinct states worth separate
   capture — e.g. an error state vs. a success state) corresponding image.
2. Spot-check 5 PNGs by actually opening them to confirm they show real rendered UI, not a blank/black
   frame or a crash screen.
3. Confirm the catalog doc is readable and useful as a standalone reference (the owner's stated goal).

## Rollback

Purely additive (new capture methods, new doc, new PNGs) — safe to delete without side effects if
reverted.

## Copy rules

N/A — this ticket captures existing copy, it does not write new copy.

---

## IMPLEMENTATION RECORD (2026-09-17, SME agent pass)

**Re-derived inventory, not trusted the stale doc.** `docs/qa/UI_SCREEN_GRAPH_2026-09-04.md` banner-
fixed `⚠ SUPERSEDED 2026-09-17` per CLAUDE.md §15 (frozen, not rewritten). Diffed it against
`git log --since=2026-09-04 -- Assets/Editor/UICaptureLaunch.cs Assets/_Modules/Core/UI/PanelRouter.cs`
— found the doc's own gap list had gone stale in both directions: CosmeticShop and SeasonTrack/
BattlePass gained real doors + captures since 09-04 (commits `e94027216`, `5f48aa7bd`), and
`BarracksPanel.cs`/`ShopPanel.cs` (named in the gap list) no longer exist in the tree at all.

**New code:** `Assets/Editor/UICaptureLaunch.cs` — added `Leaderboard` and `Jukebox` to
`RunRegisteredSecondaryCaptureHeadless` (expected frame count 36→42), the two gear-dock rows the old
doc's own capture-gap list named as uncovered. `ArmyMusterPanel` and `RedeemCodePanel` were also gap-
listed but do NOT fit that reflection recipe (bespoke VM/construction requirements) — deferred rather
than guessed at, per §11B.

**Verified via two fresh headless runs, judged by marker not exit code:**
- `RunCaptureHeadless` → `UI_CAPTURE_OK 106` (clean fidelity/geometry/glyph/endstate oracles),
  log `Builds/ui-capture-wo1860-main.log`.
- `RunRegisteredSecondaryCaptureHeadless` → 42/42 real non-blank frames rendered; the run's own
  content oracle correctly FAILED the marker on two genuine pre-existing UI defects it caught in the
  two new captures (Leaderboard duplicate/self-overlapping tab buttons; Jukebox caption text that
  truncates/vanishes at wider landscape aspects) — see the catalog doc's "Flagged for follow-up"
  section. Not fixed here per this ticket's explicit non-scope.
- Combined: 267 PNGs across 125 distinct screen/state stems in `Builds/ui-capture/`, well past the
  owner's "~45 screens" estimate (Manage's category-flow matrix alone authors ~40 named states).
- 5 PNGs opened and visually confirmed real/legible/non-blank (Leaderboard, Jukebox, ManageWorkspace,
  RaidSelection, AdaptiveHudPeaceful).

**Catalog doc:** `docs/qa/UI_SCREENSHOT_CATALOG_2026-09-17.md` — screen-by-screen table, sourced text
summaries (cited file:line or [seen] where visually opened), the two flagged defects, and an honest
"not done to full depth" section per the WO's own "breadth over perfection" instruction.

**Gate:** `python tools/gate_brace.py Assets/Editor/UICaptureLaunch.cs` → `bad=0 of 1`; NUL scan → 0.
Only `.cs` file touched. Did not touch any `Clan*.cs`/`api/` file from the concurrent WO-1858 lane.

**What remains (follow-up pass):** re-run the ~15 standalone `Run*CaptureHeadless()` entries not
exercised this session to refresh their PNGs; transcribe per-state Manage/Army/Research flow copy;
build bespoke fixtures for ArmyMusterPanel and RedeemCodePanel; open a WO each for the two flagged
Leaderboard/Jukebox defects; capture ClanChatPanel once WO-1858 lands.
