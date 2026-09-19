# WO-1877 RESULT — CLAIM (not lead-verified)

**Status claimed:** `IMPLEMENTED PENDING LEAD GATE`  
**Seat:** grok-4.5 lane worker  
**Date:** 2026-09-19  
**Commit:** none (explicit: do not commit)

## Claim

Armorer shop now prefers each armor's authored 2D `iconPath` plate for preview + list thumbs; the IconShield short-circuit is gone; armor never enters the 3D preview rig; missing family plates are on disk at the catalog paths; HP/spec band and footer wrap were widened so Defense+HP and the level-up sentence fit.

## What changed

### Code
- `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs`
  - `ResolveItemSprite`: removed the early `IconRoleArmor → IconShield` / `IconRoleWeapon → IconSword` returns. Loads `Resources.Load<Sprite>(iconPath)` first; role glyphs are last resort only; ART MISS warn when `iconPath` is set and load returns null.
  - `BuildPreviewModelOrFallback`: armor always takes `armor-2d-plate` → `ShowSpriteFallback` (no mesh-swap / 3D turntable).
  - `CreateRow`: left `RowThumb` from the same `ResolveItemSprite`; name shifts right of the thumb.
  - Preview layout: plate square `0.50–0.98`; name/desc under it; specs band raised to `0.02–0.29` so HP+delta stay visible.
  - Footer note: height 96px, overflow (was Truncate mid-word on Seeker).
- `Assets/_Modules/Village/Hero/PartyShopVM.cs`
  - Buy/sell armor+weapon `ItemVM` rows now carry `iconPath` (detail already had it).

### Art (family off nearest existing ItemIcons — no JSON path change, no 3D)
| Catalog `iconPath` | Familied from |
|---|---|
| `ItemIcons/armor_cloth` | `armor_ranger_common` |
| `ItemIcons/armor_leather` | `armor_ranger_uncommon` |
| `ItemIcons/armor_chain` | `armor_knight_rare` |
| `ItemIcons/armor_plate` | `armor_knight_epic` |
| `ItemIcons/aegis_plate` | `armor_knight_legendary` |
| `ItemIcons/armor_mage_common` | `armor_mage_uncommon` |

New PNG + `.meta` (unique GUIDs) under `Assets/Resources/ItemIcons/`. Class-set plates (`armor_knight_*`, `armor_ranger_*`, `armor_mage_{uncommon…legendary}`) were already present — Leafcloak = `armor_ranger_legendary`.

### Regression
- New `Assets/Editor/Regression/ArmorerShopPlatesRegression.cs` `[armorer-plates]`
  - no IconShield-before-iconPath short-circuit
  - armor-2d-plate branch present
  - RowThumb present
  - six family plates on disk
  - authored paths for `aegis_plate` / `armor_ranger_legendary` on disk and not IconShield (sprite import may be pending until Editor reimports)
- Wired in `DataRegression.cs` as `[armorer-plates]`.

## Proof this seat ran
- `python tools/gate_brace.py` on the four edited `.cs` → `GATE_BRACE_SUMMARY bad=0 of 4`
- NUL scan clean on those four files
- Disk presence of all six family PNG+meta verified

## NOT proven by this seat (lead gate)
- Unity `COMPILE_GATE_OK` — explicitly not run (order)
- `REGRESSION_OK` / `ARMORER_PLATES_OK` on a fresh Unity log — not run
- Device/editor PNG of Armorer with Aegis/Leafcloak showing the 2D plate — lead must open a fresh frame before IMPLEMENTED (`visual-ticket-never-fixed-without-an-opened-frame`)
- Owner felt-verify

## Not touched
- No commit, no APK, no `BOARD.html`
- No scene edits, no mesh-swap, no armor.json path rewrites
- WO-1876 owned-town HUD out of scope
