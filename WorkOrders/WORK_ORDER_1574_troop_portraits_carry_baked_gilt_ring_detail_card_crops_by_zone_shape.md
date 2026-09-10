# WO-1574: Troop portraits carry baked gilt ring; detail card crops by zone shape

**Status:** CLOSED 2026-09-10 - owner ruling: the 09-06 medallions are the final art; the detail-card crop (`ManageWorkspacePanel.cs` `artFrac` / `SquarePortrait`) stays as the shipped shape and its pin `[detail-art-crops-the-ring]` stays; the WO's rectangular-painting premise is retired. PRIOR STATUS: BLOCKED - owner art not delivered: all nine troop PNGs measured 2026-09-10 as 1254x1254 gilt medallions on transparency (all four edges + corners alpha 0, gold at the disc rim), so removing the crop would put the ring back on the detail card and turn the `[detail-art-crops-the-ring]` pin red.
**Silo:** Art + UI wiring - `Assets/Resources/RpgUi/troop/` + detail card panel.
**Source:** Manage pass-three lane handback 2026-09-07. Minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1574 -> 1575 in the same edit).

---

### OWNER RULING 2026-09-10 (morning)

> **"The 09-06 drop WAS the delivery - the medallions are final; the detail-card crop workaround stays
> and the ticket closes"** — the owner's answer, given via AskUserQuestion ~03:40 on 2026-09-10, to
> the question raised as item 9 of `docs/HANDOVER_2026-09-10_overnight.md` ("was the 09-06 drop meant
> to be that delivery (it re-exported the medallions)?").

**⛔ THIS RETIRES THE TICKET'S PREMISE.** WO-1574 was written on the assumption that nine
*rectangular paintings* were coming and that the detail card's crop was a temporary workaround
holding the line until they arrived. That assumption is now false: the **1254x1254 gilt medallions on
transparency** under `Assets/Resources/RpgUi/troop/troop-*.png` are the **final art**. Read every
section below as history, not as work owed. **There is no art delivery pending and this ticket is
NOT blocked** — it is closed.

**What stays, verified at source 2026-09-10:**

| Thing | Where | Verdict |
|---|---|---|
| the detail card's art fraction | `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs:1559` — `float artFrac = Mathf.Min(0.40f, Mathf.Max(0.28f, (cardH * 0.90f) / Mathf.Max(1f, cardW)));` | **STAYS** — it is the shipped shape, not a workaround |
| the full-height art zone it feeds | `ManageWorkspacePanel.cs:1561` — `new Vector2(0.015f, 0.02f), new Vector2(0.015f + artFrac, 0.98f)` | **STAYS** |
| the envelope-crop portrait call | `SquarePortrait(...)`, `ManageWorkspacePanel.cs:507`, `:1298` (rationale at `:473`, `:1271`, `:1297`, `:1530`, `:1542`) | **STAYS** |
| the regression pin | `[detail-art-crops-the-ring]`, `Assets/Editor/Regression/ManageMockupConformanceRegression.cs:1184` and `:1193` | **STAYS — do not relax or delete it.** It is now pinning the intended shape rather than guarding a temporary one |

⚠ **The `:1167` line number this file's Status carried until today was WRONG.** The
`[detail-art-crops-the-ring]` failures are raised at `ManageMockupConformanceRegression.cs:1184` and
`:1193` (both opened 2026-09-10); `:1167` sits inside the unrelated `[research-tree-rows-take-the-band]`
block. Corrected here so the next seat greps the right lines. `Assets/_Modules/Core/Manage/` is also
the real home of `ManageWorkspacePanel.cs` (`find Assets -name ManageWorkspacePanel.cs`, 2026-09-10).

Struck in the handover in the same pass: `docs/HANDOVER_2026-09-10_overnight.md` item 9 now carries
`RULED: medallions are final, closed`.

---

## 1. EVIDENCE (re-read at source 2026-09-07)

⚠ **THE PATH IN THE ORIGINAL DRAFT WAS WRONG AND IT IS LOAD-BEARING (corrected 2026-09-10).** This
section, §2 and FILES TO EDIT all named `Assets/Resources/Portraits/Troops/`. That directory **does
not exist** (`find Assets -ipath "*Portrait*" -maxdepth 5 -type d`, run 2026-09-10, returns
`Assets/Resources/Portraits/Buildings` and no `Troops`). The real folder is
`Assets/Resources/RpgUi/troop/`, and the folder name is a contract, not a convenience:
`ManageScreenVM.cs:4610` builds the portrait key as `"RpgUi/troop/" + c.IconId`, and
`ManagePortraitCoverageRegression.cs:263` fails on any rename under that folder. Art dropped at the
drafted path would load **nothing**.

- `Assets/Resources/RpgUi/troop/troop-*.png` are 1254x1254 medallions with a baked gilt
  ring as the outer frame.
- The Manage redesign mockup requires a rectangular troop portrait (the painting only, no ring).
- The detail card zone is currently non-square (cropped) as a workaround to hide the ring by
  envelope crop.
- The mockup authors rectangular framing; the current crop is a placeholder waiting for art.

## 2. FIX

**OWNER ACTION (STILL OUTSTANDING as of 2026-09-10 - see §5):** deliver nine rectangular troop
paintings (remove the gilt ring, paint-only format) **into `Assets/Resources/RpgUi/troop/`, keeping
the existing filenames exactly**, for the following troop ids from `troops.json`:
- troop-archer
- troop-battlemage
- troop-catapult
- troop-echo-legionnaire
- troop-field-cleric
- troop-footman
- troop-outrider
- troop-shieldguard
- troop-spearman

**CLI ACTION (when art lands - NOT YET, the art has not landed):** remove the zone-shape crop
workaround from the detail card panel and restore normal rectangular framing. **This edit is
coupled** - see §5 for the two files that must move in the same commit.

## 3. WHAT NOT TO TOUCH

Troop data model, stat displays, other portrait assets (hero, enemies, NPCs).

## 4. ACCEPTANCE

⚠ **THE FIRST THREE BOXES WERE PRE-TICKED ON A `READY TO IMPLEMENT` TICKET AND ALL THREE WERE
FALSE.** They are un-ticked here because each one was measured false on 2026-09-10 (§5). A ticked box
that nobody executed is worse than an empty one: the next seat reads it, believes the art landed, and
either removes the crop (shipping the ring) or closes the ticket on fiction.

- [ ] Nine rectangular troop paintings authored (no gilt ring, painting-only).
      **FALSE 2026-09-10** - all nine decode as 1254x1254 RGBA with every edge and corner at alpha 0.
- [ ] Crop workaround removed from detail card zone shape.
      **FALSE 2026-09-10** - the non-square `artFrac` is live at `ManageWorkspacePanel.cs:1532` and is
      actively pinned by `ManageMockupConformanceRegression.cs:1167-1179`.
- [ ] Detail card displays rectangular troop portrait without letterboxing.
      **UNPROVEN** - no capture was taken this session; it cannot be true while the art is a medallion.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log (gate lane).

## FILES TO EDIT

- `Assets/Resources/RpgUi/troop/troop-*.png` (nine art files - **owner**, path corrected 2026-09-10)
- `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs` (`BuildSelection`, the `artFrac` /
  `SelPortrait` zone at `:1532-1539`)
- `Assets/Editor/Regression/ManageMockupConformanceRegression.cs` (`:1167-1179`, the
  `[detail-art-crops-the-ring]` pin - must move in the SAME commit, see §5)

---

## 5. LANE FINDINGS 2026-09-10 (lane PORTRAITS, edit-only, no Unity run)

### 5.1 The art has NOT landed - measured, not assumed

All nine PNGs were decoded byte-for-byte this session (pure-Python zlib + PNG unfilter; no Unity, no
image library). Every one is still the medallion:

| file | size | corner RGBA (all 4) | mid-edge alpha (N/S/W/E) | fully-opaque pixel fraction |
|---|---|---|---|---|
| troop-archer.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.552 |
| troop-battlemage.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.556 |
| troop-catapult.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.582 |
| troop-echo-legionnaire.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.551 |
| troop-field-cleric.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.570 |
| troop-footman.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.546 |
| troop-outrider.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.562 |
| troop-shieldguard.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.549 |
| troop-spearman.png | 1254x1254 RGBA8 | (0,0,0,0) | 0/0/0/0 | 0.565 |

A rectangular painting would read alpha 255 at every edge and an opaque fraction near 1.0. These read
0 at every edge - the art is still a disc on transparency.

**The gilt ring itself is directly visible in the pixels.** Horizontal scan of `troop-outrider.png` at
y=627 (the vertical centre), sampling every 40px: x=0 and x=40 are `(0,0,0,0)`; the picture begins at
**x=80 `(221,149,43,253)`** and **x=120 `(235,180,80,253)`** - saturated gold - and ends symmetrically
at **x=1160 `(193,131,37,253)`** and x=1200, with x=1240 back to `(0,0,0,0)`. Gold at both rims of an
otherwise dark painting is the gilt ring, still baked in.

**History:** `git log --oneline -- Assets/Resources/RpgUi/troop/` returns three commits, the newest
`32659c0f6` (2026-09-06, "the Manage screens rebuilt against the owner's mockup"), which touched all
nine files. That is the day BEFORE this WO was minted (2026-09-07), so nothing has been delivered
against this ticket.

### 5.2 Why the CLI action could not be run early

`ManageMockupConformanceRegression.cs:1167-1179` (case `[detail-art-crops-the-ring]`) **fails the
suite** if the square `artFrac` is restored or if the portrait zone stops spanning the card's full
height. Its failure text says why in its own words: `SquarePortrait` envelope-fits
(`AspectRatioFitter.EnvelopeParent` inside a `RectMask2D`), so a **square sprite in a square zone is a
1:1 fit that crops nothing** and the medallion survives, while the same sprite in the grid's 2.3:1
tile is scaled to cover and the ring is cropped away. The zone's aspect is the entire mechanism.
Removing the crop today therefore does two bad things at once: it puts the ring back on the detail
card (the defect the owner captured), and it reds a live suite. No `.cs` was touched.

### 5.3 The plan for when the art lands (so it is not rediscovered)

1. Confirm the drop with the same decode: every edge alpha 255, opaque fraction ~1.0, and record the
   delivered aspect ratio (it decides step 3).
2. `ManageWorkspacePanel.cs:1532` - replace the crop-shaped `artFrac` with a zone authored to the
   mockup's rectangular block, and rewrite the long comment block at `:1491-1531`, which is now a
   history of the workaround and would become a lie the moment the workaround goes.
3. **Decide `SquarePortrait` vs `preserveAspect` - this is the acceptance risk.** Acceptance #3 says
   "without letterboxing". If the delivered aspect matches the authored zone, either path is clean. If
   it does not, `preserveAspect` letterboxes (and fails #3) while envelope-crop silently eats the edges
   of the new painting. Match the zone to the delivered aspect and this question disappears; that is
   the reason step 1 records it.
4. `ManageMockupConformanceRegression.cs:1167-1179` - re-point the pin **in the same commit**. Leaving
   it is a red suite; deleting it without a replacement loses the only guard on this shape. The
   replacement should pin the NEW invariant (a rectangular zone matched to rectangular art).
5. Fresh `ManageFlow_ARMY_*` UI capture, opened and looked at - the ring is a visual defect and only a
   screenshot closes it.

### 5.4 Not proven / carried forward

- **The in-code "checkerboard baked into RGB" note is now questionable, and was NOT acted on.**
  `ManageWorkspacePanel.cs:1491-1497` records that on 2026-09-06 `troop-outrider` corner pixels read
  alpha 0 with RGB varying 252/253/250/247/248. This session the same file's four corners read
  `(0,0,0,0)` - RGB zero, not 252ish. Only corner and mid-edge pixels were sampled, so "the
  checkerboard is gone everywhere" is **not proven** and no comment was edited. Whoever does §5.3 step
  2 should sample the full transparent region before rewriting that note.
- No Unity was run, no gate, no commit. Nothing was captured this session.
- **Owner question:** the files at HEAD still carry the gilt ring - please deliver the nine
  rectangular paintings into `Assets/Resources/RpgUi/troop/` under the existing filenames, and say
  whether the 2026-09-06 drop was meant to be that delivery (it re-exported the medallions).
