# WO-1877 — Armorer shop: each armor is a 2D plate (art + title + stats), not a generic shield

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/cg-wave1876.log`) + `REGRESSION_OK 588/588` (`Builds/r-wave1876c.log`). Family plates retarget existing ItemIcons (no byte-duplicate PNGs). FIXED after tester APK.

**Owner, verbatim (2026-09-18, on that frame):** "this is the armor screen" / "no images for any item, and thats all armer is a 2D image and stats and a title" / "since we never change physical armor there is no reason we cannot create better versions of similar armor since they are all in code only"

## Proof (opened PNG, this session)

`Logs/device/armorer-now.png`: title **Armorer**, Thrain Mage Lv 18, list **Leafcloak of Elarion** / **Aegis of Elarion** as name bars with **no row art**. Preview is a **gold shield glyph**, title Aegis of Elarion, "Legendary gear. Suited to any class.", Defense 0.28 (+0.25), HP line clipped. Purchase 17.3k Gold.

That glyph is not a miss — the shop **throws the catalog art away**:

```
PartyShopPanelMvvm.ResolveItemSprite (:1833-1836)
if (role == PartyShopVM.IconRoleArmor)
    return RpgUiCatalog.Get(..., IconShield);
if (role == PartyShopVM.IconRoleWeapon)
    return RpgUiCatalog.Get(..., IconSword);
```

Comment at `:1830-1832` says large previews reject "old catalog cards" and keep category glyphs; item identity is "the adjacent name/spec." The owner just reversed that for armor.

## Ruling (this ticket)

1. **Armor never changes the body.** No mesh-swap (canon: `docs/MASTER_CATALOG/village-hero.md` "Single Knight Grom, no mesh-swap"; `GearVisualApplier.AttachArmorVisual` is a cube tint, not a body). Shop presentation is **2D image + title + stats**. Do not 3D-preview armor.
2. Because the body does not change, **similar armor may share a silhouette**. Better / higher-rarity versions are new 2D plates of that family, not new hero meshes.
3. Every armor row and the preview well show **that piece's 2D plate**. The generic shield is last-resort only when `iconPath` loads nothing.
4. Stats (defense, HP, delta vs equipped) and the title stay. HP must not clip off the well (visible on the live frame).

## What already exists (do not regenerate blindly)

`Assets/Resources/ItemIcons/` already has class-set plates: `armor_knight_{common,uncommon,rare,epic,legendary}.png`, ranger same, mage uncommon→legendary. `armor.json` already authors `iconPath` (`ItemIcons/<id>`). The shop never loads them for `IconRoleArmor`.

Catalog ids in `Assets/StreamingAssets/Data/Canonical/armor.json` with **no** matching PNG under ItemIcons (read this session): `armor_cloth`, `armor_leather`, `armor_chain`, `armor_plate`, `aegis_plate`, `armor_mage_common`. Family those off the nearest existing plate (cloth/leather off ranger/mage hooded; chain/plate/aegis off knight). Blink leftover ids stay out of the Armorer door unless they already sell.

## Fix

1. **Show authored 2D art.** `ResolveItemSprite`: armor (and, same one-liner, weapons) must prefer `Resources.Load<Sprite>(iconPath)` / `ItemIconCatalog` before the role glyph. Glyph remains the never-blank last resort. Trace an ART MISS when iconPath is set and the sprite is null.
2. **List rows** get a thumb of the same sprite, not name-only bars.
3. **Author the missing plates** at the catalog `iconPath` (transparent or dark-card, no baked white, no baked title text — the UI already prints the name). Similar armor = one silhouette, stepped materials/ornament by rarity. Owner colorblind: distinguish by shape/ornament, not hue alone.
4. **Preview layout:** 2D plate fills the well; title + stats under it; HP/delta fully visible. Truncated vendor footer ("come back after you le") is in scope if the same pass wraps copy; do not rewrite shop IA.

Do **not** mesh-swap the hero. Do **not** turn armor preview into a 3D turntable. Do **not** hand-edit scenes.

## Files

- `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs` (`ResolveItemSprite` `:1833-1836`; list-row bind if it skips the sprite).
- `Assets/Resources/ItemIcons/` — missing plates named by `armor.json` `iconPath`.
- Dual-copy catalog only if a path string changes (`StreamingAssets` + `Resources` Canonical `armor.json`). Prefer matching existing `iconPath` so JSON does not move.
- Regression: `Assets/Editor/Regression/` (extend party-shop preview suite or add `[armorer-plates]`): for `IconRoleArmor` with a loadable `iconPath`, `ResolveItemSprite` is **not** `IconShield`; revert the `:1833-1834` early-return → RED.

## Acceptance

- `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- Device/editor frame of **Armorer** with Aegis (or Leafcloak) selected: **2D armor plate** in the well and on the row, title + full stats, not the gold shield. Lead **opens the PNG** before IMPLEMENTED (`visual-ticket-never-fixed-without-an-opened-frame`).
- Owner felt-verify closes.

## Not in scope

- WO-1876 owned-town HUD.
- Weapon 3D models on the body (weapons do change the hand). Weapon **shop glyph** may ride the same `ResolveItemSprite` one-liner; new weapon art is a follow-up unless the owner expands.
- Mesh-swap, Blink armor leftover as a shop rewrite, Night Market SKR packs.
