# WO-2018 — Manage category-first navigation, detail space, and Army frames

**Status:** IMPLEMENTED — AWAITING OWNER DEVICE MATCH

**Date:** 2026-09-08

**Priority:** P1

## Owner observations

- BUILD initially shows too much; it should start with categories.
- BUILD card text/status hierarchy overlaps and feels visually off.
- Building detail content is correct but does not use the available space well.
- ARMY portraits should be contained by the visible frames.
- Regression scripts must move with the behavior.

## Scope

- Open BUILD on one clean row of ECONOMY / DEFENSE / CRAFT / STORAGE cards from the existing category authority.
- Selecting a category replaces those cards with only that category's buildings.
- BACK from a building category returns to the category root; BACK from the root closes Manage.
- Keep QUEUE visible on both levels and remove the ALL/filter-chip row from the opening screen.
- Strengthen tile name/footer contrast, compact/inset the status medallion, and render state words on a dark rounded pill.
- Increase detail art presence and place the primary action in the lower well when content allows.
- Give Army tiles an explicit portrait-containment contract and clip portrait/frame to one square seat.
- Update behavioral, conformance, build-door, and capture regressions for the new navigation/layout.

## Acceptance

- BUILD root is exactly the ordered `BuildFilter.Membership` set in one row, with no count/status banner competing with the category name.
- Category selection produces the authoritative `BuildInventoryModel.Tiles(category)` inventory.
- BACK returns category inventory to the category root without closing Manage.
- Army portrait pixels remain inside the square frame seat and state text does not cross that seat.
- Detail action uses the lower portion of the card without colliding with composed facts.
- Fresh compile, full data regression, Manage flow capture, and eyes-on PNG inspection are recorded.
- Ticket remains outside DONE until the owner accepts a device screenshot under ruling 29.
