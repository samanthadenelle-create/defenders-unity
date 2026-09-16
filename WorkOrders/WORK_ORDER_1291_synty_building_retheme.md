# WO-1291 ? Preserve original castle storefronts and validate the owner-authored layout

> **OWNER CORRECTION 2026-09-13: original Tripo castle storefronts are authoritative.**
> Owner: "Yes—preserve the original Tripo storefronts" and "the synty ones was done by a really bad CLI model that just did without asking".
> The earlier approval claims below do not authorize replacing the castle storefronts.
> Preserve the working capture-to-owned-player-town feature. Validate the owner's uncommitted
> castle scene and corrected buildings; do not regenerate the old template over that work.
> Validation/assistance artifacts: `Builds/castle-validation-20260913/`.
> Follow-up owner decision: preserve the saved 13-object selection and layout, adding the original
> Arcane Tower (Cathedral of Learning). Verify colors and Tripo material fixes. The nine original
> storefront address bindings were restored through Unity: `RESTORE_ORIGINAL_STOREFRONTS_OK changed=9`.
> Saved repair passed `OWNER_CASTLE_LAYOUT_APPLY_OK preserved=13 cathedral=1 saved=True`;
> original scene and prefab backups are retained in the validation folder. The actual empty-scene
> castle builder and two injector passes passed `OWNER_CASTLE_RUNTIME_OK` (editor proof only).
> Focused final integration checks passed after orphan-group collider removal, including
> barracks provenance/state replacement, the active store, and captured-town reconstruction:
> `OWNER_CASTLE_FINAL_CHECKS_OK` in `castle-validation-20260913-final-checks2.log`.
> Current results and remaining limits are recorded in the validation folder's
> `VALIDATION_REPORT.md`. This is an applied local repair, not a shipped/device-accepted fix.
> The initial missing-legacy-host probe described the pre-repair scene and is superseded by
> semantic-marker resolution and the fresh focused evidence above.

**Status:** IN PROGRESS ? original Tripo restoration applied and focused checks passed; final night regression and test builds pending (owner correction 2026-09-13).
**Minted:** 2026-09-01 (CLI, banner bumped 1289 -> 1293 in the same edit)
**Branch:** `feat/synty-art-retheme`   **Lane:** 3 of 4 (Synty art re-theme)
**Historical scope, superseded for castle storefronts:** the September 1 re-theme text below is retained as history only. The September 13 owner correction above is the active requirement.
Lane 3 covers catalog + storefronts; lane 4 (WO-1292) covers environment/props.

---

## HISTORICAL STATE (2026-09-01; not the current implementation)

`Assets/Resources/Data/Canonical/structures-catalog.json` — **28 entries, 27 carrying art**, every
`visualPrefabPath` under `Structures/*`, served through **Addressables / the R2 CDN**:

```
tower_ground_archer  Structures/Tower_Wooden_Watchtower     wall_wood    Structures/Wall_Medieval_Wood
tower_ballista       Structures/Ballista_L1                 wall_stone   Structures/Wall_Medieval_Stone
tower_siege_tower    Structures/Ballista                    gate_stone   Structures/Gate_Medieval_Medium
tower_catapult       Structures/Catapult                    mine_crystal Structures/CrystalMine
healing_caravan      Structures/HealingCaravan              deco_torch   Structures/Torche_Wall
pet-house            Structures/PetHouse2                   workshop     Structures/ShopAndCrafting
```

Hand-placed storefronts baked into `Main_Castle_Overworld.unity`: `Blacksmith_Weapons_Storefront`,
`Forge_Armor_Storefront`, `Windmill_Food_Storefront`, `Lumbermill_Wood_Storefront`,
`Jeweler_Gems_Storefront`, `Marketplace_Monetization`, `ArcaneTower_MagicUpgrades`, `CastleBarracks`.

## THE REPLACEMENT ART

`Assets/Synty/PolygonFantasyKingdom/Prefabs/` (URP-native — see WO-1290 for the shader proof):

- **`Buildings/Presets/` — 26 `*_Optimized` prefabs**: Blacksmith, Church A/B, Tavern, Stables,
  Windmill, Tower, Hut x2, Shelter x2, Outhouse, Houses 01-10, Archway x2. Near-1:1 onto our storefronts.
- **`Buildings/House/` — 241 modular pieces** for anything a preset does not cover.
- **`SiegeEngines/` — 15 prefabs**: real art for `tower_ballista` / `tower_catapult` / `tower_siege_tower`,
  which currently point at polyperfect stand-ins.
- **`Castle/` — 348 prefabs** (WO-1290) for `wall_*` / `gate_stone`, so the catalog wall entries finally
  match the perimeter.
- **`Props/Banners/` — 43** for faction/ownership dressing.

## THE WORK

1. **Author a mapping table** — one tracked file, catalog id -> Synty prefab. Do NOT scatter path
   literals across builders (the duplicated-state failure class that produced the stale WO block,
   the hardcoded repo root, and the retired asmdef table — CLAUDE.md §2/§0/§5).
2. **Copy the referenced prefabs into the tracked Addressable/Resources path.** `Assets/Synty/` is
   gitignored (461MB, same policy as polyperfect) — only what the game references gets committed.
3. **Re-point the 27 `visualPrefabPath` values.** **Never rename a catalog `id`** — they are live save
   keys (memory `structure-role-enum-and-format-normalization`).
4. **Normalize by Y-height, not raw scale** — `repo.visualHeight` fit-to-height (DEF-208 / WO-751), and
   respect the `_heightCadence` note in the catalog. **Walls are deliberately excluded from narrowing**
   (`MASTER_CATALOG.md:86-87` — narrowing opens pathable gaps in saved wall runs).
5. **Re-theme the hand-placed storefronts** in the hub scene via the builder, never by hand-editing
   `.unity` (CLAUDE.md §3).
6. **Colliders + layer**: BoxCollider, `Structure` layer, so `IDamageableStructure` / tower LoS / nav
   carving keep working.

## SHIP GATE — THIS LANE CANNOT SHIP WITHOUT AN R2 PUSH (CLAUDE.md §16)

Every `Structures/*` swap rebuilds the Addressable content, and **bundle names are content-hashed —
every content build needs ITS OWN push. A push from a previous build can never cover it.** A missing
push fails SILENTLY: the APK installs, launches, plays, and shows placeholder buildings with no error
on screen. This has already happened three times (2026-08-18 / -19 / -20).

- [ ] Run **`tools\r2-ship.ps1`** — the ONE sanctioned path. Do not re-inline the push or the verify.
- [ ] Judge by **`R2_PUSH_OK` + `R2_PARITY_OK` on a FRESH log**, never the exit code.
- [ ] Never `adb install` a hand-built APK — that bypasses the whole gate.

## ACCEPTANCE CRITERIA

- [ ] All 27 catalog entries resolve to a Synty prefab; zero null loads (assert, do not eyeball).
- [ ] No catalog `id` changed. Save-compat proven by loading a pre-change save.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` + `UI_CAPTURE_OK` + **`R2_PARITY_OK`** on FRESH logs.
- [ ] `RunCaptureHeadless` screenshots of the town from the standard angles, opened and looked at.
- [ ] Build-mode placement still seats every structure on the ground at the right height.

## PROGRESS NOTES

- **2026-09-01 (edit-only agent, ungated — lead gates + commits):** owner-approved re-picks landed
  in `Assets/Editor/SyntyStructureRetheme.cs`: armorer -> House_05 (Forge keeps Blacksmith),
  barracks -> House_07, lumbermill -> Shelter_02, arcane tower -> Church_01_A (ArcaneSpire_1 keeps
  Tower_01, now unique; safe vs the A3 tower-aspect floor because the arcane-tower row is type
  Resource / heightMul 1, read at source). Watermill_Medieval is now a COMPOSED wrapper (House_08 +
  Castle/SM_Bld_Waterwheel_01) via new minimal composition support; the wheel mount is a
  **PLACEHOLDER** (computed bounds-based wall hang, +X face, 0.10 m embed, 0.05 m ground clearance)
  pending screenshot verify. The three unmapped addresses are closed: GenericContainer ->
  KayKit Pallet_Wood_Covered_A.fbx (owner ruling: wood pallet), CrystalMine + IronMine ->
  KayKit building_mine_green.fbx (IronMine differentiation deferred to WO-1292). Expect
  swapped 30 -> 33, unmapped 3 -> 0 on the next run. Also added: a purge pass — the group held
  68 entries over 38 addresses (every swapped address duplicated: old source entry + wrapper,
  because CreateOrMoveEntry cannot remove the old entry); the run now removes superseded/dangling
  entries for addresses it re-pointed, group-only, assets untouched. NOTE for gate time: the
  reported dangling GUID 33233eb1... is the group's own m_GUID (its identity field), not an entry —
  nothing to purge there. Re-run `SyntyStructureRetheme.Run` to apply; needs the full gate ladder +
  r2-ship per SHIP GATE above.

## DO NOT TOUCH

- `Assets/Generated/Terrain/**` (WO-1289). Castle perimeter geometry (WO-1290).
- Catalog `id` strings, `RepoProps.MaxStructureLevel` (the SINGLE level ceiling — never re-hardcode one).
- The cost baskets — regular structures are wood+iron only, magical are crystal-based (WO-947).
