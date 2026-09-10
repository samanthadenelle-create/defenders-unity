# WO-1599 RESULT - Command Center: pick a SKU from a dropdown instead of typing it

**Status:** IMPLEMENTED - b10038556 on HEAD 2026-09-09 (was READY); owner felt-test closes.
**Verified:** 2026-09-09, SILO 0A verify-and-flip pass, HEAD 184c8ff06, branch dev. Read-only:
no .cs edited, no deploy, no commit.

## 1. Landing sha

```
b10038556 feat(console): WO-1599 the promo mint's reward pack is a dropdown fed from the
          SKU catalog, with a 'type it' fallback
```

`git merge-base --is-ancestor b10038556 HEAD` = **ancestor**. Its diffstat:

```
WorkOrders/WORK_ORDER_1599_command_center_sku_dropdown_instead_of_typing.md |  36 ++++
api/admin/console.js                                                       |  98 ++++++-
test/admin.sku.dropdown.test.js                                            | 239 +++++++++++
3 files changed, 371 insertions(+), 2 deletions(-)
```

## 2. Proof at HEAD, file:line

- `api/admin/console.js:1035-1069` `skuFieldHtml(id, labelText, noneText)` - builds a `<select>`
  whose options come from `state.skus` only (`:1036-1037` `var d = state.skus; var rows = (d &&
  Array.isArray(d.packs)) ? d.packs : []`). Each option renders `name (sku)` (`:1046`), and the
  first option is the caller's none-text (`:1044`).
- `:1038` + `:1042-1043` - on `state.skusErr` the select renders **disabled** with the single option
  `"SKU catalog unreadable - type the sku below"`.
- `:1051-1055` - the failure is spelled out **in words**, not colour ("COULD NOT READ the SKU
  catalog (<err>), so the list is EMPTY and DISABLED - it is not saying there are no packs"). This
  is the colourblind rule (memory `owner-colorblind-delegate-visual-creative`).
- `:1061-1063` - the "Type it instead" toggle button, `aria-expanded`, only when the catalog read
  succeeded; `:1066-1067` the free-text `<input>` fallback, `hidden` unless the catalog is broken.
- `:1073+` `skuFieldValue(id)` - THE ONE READER; whichever input is visible is the answer, the
  hidden one having been cleared.
- `:1093` - the only call site: `skuFieldHtml('ppack', 'Reward pack sku (optional)', '- none -')`.
- `:1677` - the mint submit reads it: `rewardPackSku: skuFieldValue('ppack')`.
- `:1543` + `:1554` - the toggle handler, which also keeps the select disabled while
  `state.skusErr` is set.
- Catalog source unchanged: `:370` `getJson('/api/admin/stats?view=skus')`, `:383-385` sets
  `state.skus` / `state.skusErr` (WO-1532, one fetch, one list).
- Server-side validation untouched: `api/_lib/ops.js:308` `PACK_SKU_NOT_ASCII`, `:317` the
  "sku OR crystals, never both" refusal, `:431` / `:452` the INSERTs.

**"Every place" is ONE place.** `grep -n "tier1_pack_sku\|tier2_pack_sku\|reward_pack_sku"
api/admin/console.js` returns a single hit (`:1115`, a read-back display of `c.reward_pack_sku`).
The `tier1_pack_sku` / `tier2_pack_sku` fields named in the WO **do not exist in the console page**,
so `ppack` is the complete set of sku-typing sites. `grep -n 'id="[a-z-]*sku' api/admin/console.js`
returns nothing - no leftover bare sku text input.

## 3. Tests - RUN THIS SESSION, GREEN

`node --test test/admin.sku.dropdown.test.js`:

```
pass 9   fail 0   cancelled 0   skipped 0   duration_ms 103.7
```

The nine cases, verbatim from the run:

1. the sku field is a SELECT built from state.skus, not from a list in the page
2. NO catalog sku is hardcoded anywhere in the page
3. the old typed-only input is gone from the mint form
4. a free-text fallback still exists, so an unknown sku can never block a mint
5. RENDERED: the dropdown lists every catalog pack as "name (sku)", with a none option
6. RENDERED: a failed catalog read disables the select, says why, and opens the typed field
7. the mint submits whichever half is live, through one reader
8. the served page is still 7-bit ASCII from end to end
9. server-side sku validation is untouched - the dropdown is presentation only

Case 8 is the WO-1244 rule-6 ASCII acceptance; case 9 is the "dropdown is presentation" acceptance.

## 4. What the owner should felt-test

1. Open the Command Center, go to the promo-code mint form. Is **Reward pack sku (optional)** a
   dropdown listing every pack as `name (sku)`, with `- none -` first?
2. Mint a code with a pack reward **without typing anything** - pick from the list, submit, confirm
   the code lands with the right pack.
3. Tap **Type it instead**. Does the box appear, does the select go away, and does the typed value
   win on submit? (Only one of the two is ever live.)
4. Are the SKUs in the list the ones you expect, and is anything missing that is on the shelf?

## 5. What is NOT proven

- **Not proven against the LIVE deployment.** Everything above is HEAD source plus the local Node
  test. No request was made to the production console, and no phone-width render was captured.
  If the deployed console is behind HEAD, the owner will not see this yet - check the deploy before
  concluding it did not ship (memory `diagnose-the-build-under-test`,
  `four-vercel-projects-serve-this-game`).
- **`npm test` in full was NOT run** - only this one file, with `node --test`. Other suites in
  `test/` are unverified by this pass.
- **A real catalog outage was never induced.** Case 6 proves the rendered outage branch from a
  fixture, not from an actual failing `/api/admin/stats?view=skus`.
- **No proof that every DB pack reaches the catalog view.** If a pack is missing from the dropdown,
  the cause would be WO-1532's catalog view, not this ticket - the "type it" fallback exists exactly
  so that case can still be minted.
