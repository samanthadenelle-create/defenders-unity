# WORK ORDER 1599 - Command Center: pick a SKU from a dropdown instead of typing it

**Status:** IMPLEMENTED - b10038556 on HEAD 2026-09-09 (was READY); owner felt-test closes. See `WORK_ORDER_1599_command_center_sku_dropdown_instead_of_typing.RESULT.md`. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the owner's ask
**Silo / Lane:** api/admin/console.js (the console page), api/_lib/sku-catalog.js + `GET /api/admin/stats?view=skus` (the catalog the dropdown reads, WO-1532), api/_lib/ops.js (the ops that take a sku), test/
**Type:** EXISTING system, USABILITY
**Priority:** P2 (owner-facing; she mints codes and packs by hand)

## Owner, verbatim (2026-09-07 12:5x)

> "Would it be possible to add in the command center a drop-down for the SKUs that allowed me to just
> select the SKU from a drop-down list instead of having to manually type it in?"

## What to build

- Every place the console asks the operator to TYPE a sku (the promo-code mint form's
  `reward_pack_sku` / `tier1_pack_sku` / `tier2_pack_sku`, any grant-pack op, anything else that posts a
  sku to `api/_lib/ops.js`) becomes a `<select>` fed from the SKU catalog the page already fetches
  (`state.skus` from `/api/admin/stats?view=skus`, WO-1532) - ONE source, never a second list typed
  into the page. Each option reads `name (sku)`; the first option is "- none -" where the field is
  optional. A free-text fallback stays available behind a small "type it" toggle for a sku the catalog
  does not know (a brand-new DB pack), so the page can never block a legitimate mint.
- The catalog fetch failing (`state.skusErr`) renders the select disabled with the error in words
  (the owner is red/green colourblind - words, not colour) and the free-text field enabled.
- Keep the page 7-bit ASCII end to end (WO-1244 rule 6) and keep every op's server-side validation as
  it is - the dropdown is presentation; `ops.js` still refuses an unknown sku.
- Tests: extend `test/admin.skus.view.test.js` (or beside it) with a DOM-free assertion that the
  console source builds the select from `state.skus` and carries the fallback; and that no sku
  literal list exists in the page.

## Not to touch
- The pack catalog data (`packs.json`), the store client, the promo redeem endpoint.

## Acceptance
- Owner opens the console, the sku fields are dropdowns listing every catalog sku by name, minting a
  code with a pack reward needs no typing; a catalog outage degrades to the typed field with a
  visible reason. npm test green.
