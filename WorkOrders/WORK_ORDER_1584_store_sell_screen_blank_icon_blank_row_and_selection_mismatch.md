# WORK ORDER 1584 - Store sell screen: a "*" where the item art should be, a blank first row, and the detail names an item the list does not highlight

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:44, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the owner's Seeker screenshot
**Silo / Lane:** Village/Hero party shop (the vendor SELL tab) - `Assets/_Modules/Village/Hero/PartyShopVM.cs`, `PartyShopPanelMvvm.cs`
**Type:** EXISTING system, DEFECT (visual + state)
**Priority:** P1 - the owner asked twice ("did you see screenshot regarding the store? not the realm store te regular sell screen. Look at seeker screenshots not the f8 ones")

## Evidence (the screenshot IS the data - memory `screenshots-are-primary-evidence`)

`Logs/device/seeker-shots/Screenshot_20260907-075931.png` (Seeker, build 2026.09.07.359076, 2670x1200), the
vendor Store panel, SELL side. Read off the frame:

1. **The item art is a `*` glyph.** The detail column shows a large white asterisk above "Iron Scrap x43 /
   From your pack." - the View's glyph fallback fired, so `ItemVM.IconPath` was null AND the catalog sprite
   lookup by `IconName` found nothing for a crafting material. (`PartyShopVM.cs:81-86` documents the three
   keys; the fallback is the View's.)
2. **A blank first row.** Above "Elarion Petal x3" sits an empty framed row - a row painted with no label
   (an empty ItemVM, a header row with no text, or a row whose label failed to bind).
3. **Selection mismatch.** The only labelled list row is "Elarion Petal x3", yet the detail column shows
   "Iron Scrap x43" and the button reads "Sell +2 Gold". Either the highlighted row is not the selected
   item, or the list is scrolled/clipped so the selected row (Iron Scrap) is off-frame while the blank row is
   its ghost.
4. **The panel is ~70% empty.** Two rows in a well that could hold eight; the detail column has no
   description, no category, no "what this is used for" line. The owner's standard for every screen is
   ruling 29 (fills the screen; size/font/style/context/images).

## What to do

- **Instrument first (CLAUDE.md s12):** `FlowTrace.Step("PartyShop", ...)` at row build (id, label,
  iconPath, iconName, resolved sprite yes/no), at selection change (selected id vs highlighted row), and at
  the glyph fallback (`Warn` naming the id whose art was not found). Run the headless capture for the sell
  tab (there is a UI capture harness - `RunCaptureHeadless`; find the panel's capture entry) and read the
  trace; do not fix from the frame alone.
- Fix the three defects the trace names: the art key for crafting materials (one producer - find where
  gear rows get their `iconPath` and give material rows the same seam; never a second icon resolver),
  the blank row (no row without a label - `Guard` it and skip with a `Fail` if the VM is empty), and the
  selected/highlighted disagreement (the VM's `SelectedId` is the ONE truth; the View highlights from it).
- Fill the detail column from the catalog: name, count, one-line description, category, sell value.
- Add/extend a regression under `Assets/Editor/Regression/` pinning: every sell row has a non-empty label;
  every material row resolves a sprite (or the fixture lists the missing keys as an ART ASK, named);
  highlighted row == selected id.

## Not to touch
- The Night Market / PackStore (`Assets/_Modules/Commerce`, `Assets/_Modules/Wallet`) - a different screen.
- `ManageScreenPanel` / Manage.

## Acceptance
- A headless capture of the SELL tab with >=3 sellable stacks shows real art on every row and in the detail,
  no blank row, the highlighted row is the one the detail names.
- Regression registered in `DataRegression.cs`, green; REGRESSION_OK n/n on a fresh log.
- Owner felt-test on the Seeker closes it.
