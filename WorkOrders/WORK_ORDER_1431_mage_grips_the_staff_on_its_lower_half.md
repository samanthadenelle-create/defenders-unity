# WO-1431: the mage grips the staff on its LOWER half, not its upper half

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane HERO-GRIP); owner felt-test on device closes
**Silo:** Hero visuals - weapon attachment offsets
**Owner (2026-09-06, verbatim):** *"the staff needs reversed, right now they grasp it 75% on the lower half instead of
on the upper half of the staff"* and, on priority: *"it's a very low priority. We don't need to fix it right now, but I
just wanted it tracked so that it's on the radar that when things are slow, we have them fix that."*

**Pick this up when a lane is free and nothing player-blocking is waiting.** It is cosmetic; nothing is unreachable and
no system is broken.

---

## 1. The defect
The mage holds the staff about 75% down its LOWER half. A staff is gripped on the upper half, near the head. Purely
visual; combat, reach and damage are unaffected.

## 2. Where to look - a head start, NOT a diagnosis
⚠ CLAUDE.md section 12 still applies: this is a lead from a five-minute read, not a proven cause. Prove it before editing.

- Weapon props are seated on the humanoid `RightHand` bone - `HeroBodySwapper.cs:988` (*"sword/staff/mace on RightHand;
  bow skipped in applier"*), with `GetBoneTransform(RightHand/LeftHand)` mapping to the `CC_Base_*` grips (`:731`).
- Per-item placement is authored in **`AttachmentOffsetRegistry`**
  (`Assets/_Modules/Village/Hero/AttachmentOffsetRegistry.cs:39`), backed by
  `Assets/Resources/OffsetForge/offsets.json`.
- **THE LIKELY CAUSE:** that file authors **26** offsets, and a scan of its keys shows shields, several swords, towers
  and resource props - **no staff entry**. A staff with no authored offset falls back to a default meant for a
  one-handed sword, whose grip sits near the pommel. On a long two-handed prop the same offset lands three-quarters of
  the way down the shaft. **VERIFY THIS** - list the keys and confirm no staff/wand/mage entry exists before assuming.
- `HeroBodySwapper.cs:1015-1026` records the intended direction already: *"mapping to the CC_Base grip joints is the
  documented next step; the owner is refining that approach"* and *"FOLLOW-UP: map gear to the body's grip joints via
  AttachmentOffsetRegistry."* So the registry is the sanctioned seam - do not invent a second one.

## 3. The fix

> ### ⚠ SECTIONS 2 AND 3 BELOW ARE SUPERSEDED (2026-09-09, lane HERO-GRIP). Read this box first.
> Section 2's lead ("THE LIKELY CAUSE: no staff entry in `offsets.json`, so a staff falls back to a
> one-handed-sword default") and section 3's instruction ("**Data, not code.**") were a five-minute
> read, and section 2 said so in its own words. They were **verified and superseded** by the RCA in
> `docs/READY_RCA_2026-09-09.md` § "WO-1431", which is source- and device-proven.
>
> **The half that held up:** `offsets.json` really does hold **26** rows and **none of them is a
> staff** (re-counted at source 2026-09-09; the ids are shield_A, ShieldWithItemLogic, Knight,
> crystals, iron, wood, food, sword_A/D/F/G, enemy_outpost, arcane tower, three Tower_Wooden_
> Watchtower tiers, bow_A_withString, jeweler, Forge, barracks, armorer, three Ballista tiers,
> RealmStore, ShopAndCrafting).
>
> **The half that was wrong:** there is no "sword default" fallback. The live non-native melee attach
> path called `EquipmentController.SeatHiltLowerHalf` for **every** melee prop — the bladed rule was
> the *only* rule, applied unconditionally. Meanwhile `WeaponOrientHelper.TryDeriveStaffGripY`
> already computed the owner's ruled answer (device trace, `tripo_staff_a` on Hero (Blaise): a
> 1.2639 m staff, `fraction=0.75 -> gripY=0.7204`) and **the result was discarded** — it was reachable
> only from `TraceMeasuredSeat`, a read-only prediction. The measurement path and the live path were
> two different programs.
>
> **So authoring a JSON row is the wrong fix**, and the ledger rules it out: it would paper a
> per-asset hand-dial over a live/measure split, and it would have to be re-dialled for every staff
> mesh — the exact per-asset tuning `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` exists to abolish. Worse, an
> authored row moves the staff **out of the derived tier**, so the sanctioned derivation would then be
> permanently unreachable for it.
>
> **What shipped instead:** the split is joined. `EquipmentController.SeatMeleeGripPoint` is one
> dispatcher — a Staff that passes the precedence ladder takes the derived 0.75 grip; every other
> melee family keeps the hilt-lower-half rule byte-for-byte; an authored offset row or a
> substantiated `manual: true` still outranks the derivation. See
> `WORK_ORDER_1431_mage_grips_the_staff_on_its_lower_half.RESULT.md`.

⚠ `offsets.json` lives under `Assets/Resources/`. Check whether a `StreamingAssets` twin exists; if it does, both
copies stay byte-identical and the edit is byte-mode with the LF count proven.

## 4. Scope
**In:** the staff's attachment offset, and any other weapon that visibly shares the defect - check the mace and the bow
while you are in there, and report rather than silently fixing.
**Out:** staff damage, reach, the swing animation, and the WO-1429 primary-attack fallback. Different tickets.

## 5. Acceptance
- [ ] A capture or screenshot of the mage holding the staff, before and after. **This is a visual defect, so the
      screenshot IS the evidence** - a green compile proves nothing here.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` if any `.cs` is touched; JSON parity proven if a twin exists.
- [ ] Owner felt-test closes it.

## 6. Worth noting
If the cause is a missing entry falling back to a sword default, then **every unauthored weapon has the same latent
defect** and only the ones tried so far have been noticed. Report how many weapon ids exist against the 26 authored
offsets - that ratio is the real finding, and it is the same shape as the seam defects found on 2026-09-06.
