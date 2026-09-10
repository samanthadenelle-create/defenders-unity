# WO-1634 - Raid camps: the authored prop sets miss what their own child WOs specified

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (lane RAID-POLISH, main-line banner; bumped 1633 -> 1634 in the SAME edit)
**Silo / Lane:** World / Raid scenes - DATA only
**Files owned:** the `raidDress.props` arrays of `Assets/Resources/Data/Canonical/scene-configs.json`
**Do NOT touch:** `RaidBaseGenerator.cs` (lane ARENA-WALL), garrison composition, spire HP, tower DPS,
`entranceCount`, `interiorWallLayers`, `wallTier`, `baseRadius`.
**SEQUENCING:** this ticket edits the same JSON rows as **WO-1633**, so the two are **NOT
file-disjoint**. WO-1633 lands first; the lead sequences them.
**Type:** EXISTING system. The dressing seam works (see WO-1633 section 1) - these are content rows
that were never authored.

---

## 0. Owner voice

> **"i have mentioned it in testing that it feels incomplete and not polished"**
> - owner, 2026-09-10 morning, about the raid arena / bases.

> **"similar strategy as we used in battle arena"**
> - owner, 2026-09-10, same morning, on how to dress the courtyards.

WO-1607:65, the program north star:

> `COURTYARD (camp life + cover + a fight, not empty dirt)`

---

## 1. The gaps, each against the child WO's own table

Authored rows read out of `Assets/Resources/Data/Canonical/scene-configs.json` on 2026-09-10; spec
rows re-read at source in the child WOs the same session.

### Easy - `raider_camp_small` (WO-1609 section 3.4)

| Spec row | Spec asks | Authored today | Verdict |
|---|---|---|---|
| Sleeping camp (`:103`) | tents, 3-4 | `building_tent_green` x4 Courtyard | MET |
| Loot pile (`:104`) | 8-10 total, colliders on | `barrel_large` 4 + `crate_large` 4 + `crates_stacked` 2 = 10 | count MET; colliders are WO-1633 |
| **Fire / cook (`:105`)** | **1 cluster** - `torch_lit.fbx`, or hexagon `haybale.fbx` / `trough.fbx` | **nothing** | **MISSING** |
| Racks (`:106`) | 2 - `weaponrack.fbx`, `bucket_arrows.fbx` | `weaponrack` x2 | MET |
| Rubble (`:107`) | 3 | `rubble_large` x3 | MET |

A scavenger camp with no cook fire is the single loudest missing "camp life" tell in the Easy frame -
the first camp the player ever raids (WO-1609 priority line).

### Hard - `fortified_garrison` (WO-1610 section 3.4)

| Spec row | Spec asks | Authored today | Verdict |
|---|---|---|---|
| Barracks (`:108`) | 1 - hexagon `building_barracks_red.fbx` | `barracks` x1 | present, but the token is not the spec'd red-hexagon file - see section 2 |
| Second hall (`:109`) | 1 - `building_home_A_red.fbx` or `building_workshop_red.fbx` | `House_Medieval_Medium` x1 | present, different kit - see section 2 |
| **Racks / ammo (`:110`)** | **4-6** - `weaponrack`, `bucket_arrows`, `cannonball_pallet` | `weaponrack` x3 | **UNDER by 1-3, and only one of the three token families** |
| Crates (`:111`) | 8, cover along the sides of the march | `barrel_large` 4 + `crate_large` 4 = 8 | count MET; "sides of the march" is WO-1633 |
| **Damage tell (`:112`)** | **4** - `wall_broken`, `rubble_large`, `sword_shield_broken` | `rubble_large` x3 | **UNDER by 1, and the "broken garrison" tells `wall_broken` / `sword_shield_broken` are absent entirely** |
| **Siege (`:113`)** | **2** - hexagon `building_tower_cannon_red.fbx` or catalog Ballista / Catapult | **nothing** | **MISSING** |

The camp is literally named **The Broken Garrison** and carries none of the two tokens that say
"broken". Note WO-1610:113's own caveat: *"Count against the 7 turrets if they shoot"* - the siege
pieces must be authored as **props** (visual emplacements), not as extra `DefenseTower`s, or they
change the tower DPS budget this ticket is forbidden to retune.

### Extreme - `mage_enclave` (WO-1611 section 4.4)

**Density is NOT a gap here.** WO-1611:96-98, verbatim: *"Courtyard - spare on purpose ... This is
not a settlement. If it is as busy as Easy, Extreme loses identity."* The gap is **which zone**:

| Spec row | Spec places it | Authored `zone` today | Verdict |
|---|---|---|---|
| Columns (`:100`) | colonnade along the march, 6-8 | `pillar_decorated` x6, `Courtyard` | MET |
| **Banners (`:101`)** | **"On columns"** - i.e. the courtyard colonnade | `banner_white` x4, **`Keep`** | **WRONG ZONE** |
| **Torches (`:102`)** | **"On outer inner-face"** - the courtyard perimeter | `torch_mounted` x6, **`Keep`** | **WRONG ZONE** |
| Rubble (`:103`) | 3, corners | `rubble_large` x3, `Courtyard` | MET |
| Chests (`:104`) | 2, decor or `BreakableContainer` | `chest_gold` x2, `Keep` | acceptable |

Ten of Extreme's 27 authored instances are inside the keep, which the player only reaches at the end.
The colonnade the whole camp is supposed to read as is 6 bare pillars. `ZoneOf` (`RaidBaseDresser.cs:602-610`)
resolves `Keep` only when `ctx.InnerLayers > 0`, which Extreme satisfies - so these are landing where
authored, not falling back. This is an authoring error, not a code defect.

---

## 2. One owner question (a default is picked; say the word to change it)

WO-1610:108-109 names **hexagon red** files (`building_barracks_red.fbx`, `building_home_A_red.fbx`)
for Hard's two yard buildings, but WO-1607 section 4's per-camp kit table assigns Hard **"Synty, not
hexagon-red"**. The two authored tokens today (`barracks`, `House_Medieval_Medium`) are neither.
Both readings are equally supported by canon.

**DEFAULT TAKEN (implement this unless she says otherwise): follow WO-1607 section 4 and keep Hard
Synty/neutral**, because 1607 is the program spine that 1610 hangs off and its table is the later,
kit-level ruling; keep the existing two tokens and add the missing rows in the same neutral register.

## 3. Acceptance

1. Easy gains a fire/cook cluster (WO-1609:105).
2. Hard gains a siege pair authored as **props**, racks brought to 4-6, and damage-tell brought to 4 including at least one `wall_broken` or `sword_shield_broken`.
3. Extreme's banners and torches move from `zone: "Keep"` to the courtyard colonnade / inner face; the courtyard census stays spare (WO-1611:96).
4. Every new token is verified present on disk **before** it is authored - resolve it through `RaidBaseDresser.LoadVisual`'s search order (`:115-172`); never guess a prefab name (`docs/polyperfect-asset-catalog.md`, the KayKit folders at `RaidBaseDresser.cs:40-49`).
5. The next bake shows `missing=0` for all three configs and a rising props count on the WO-1633 log line.
6. JSON edits are byte-safe: `scene-configs.json` is CRLF (352 LF == 352 CRLF at mint time); patch bytes and prove LF count == CRLF count after.

---

### OWNER RULING 2026-09-10

> **"Manage ARMY copy: BUILD BARRACKS, keep the cook fire"** - owner, verbatim, 2026-09-10 (morning).

**No code or data change is made by this block.** It is recorded so the decision is on the ticket
before anyone implements it.

**What the second clause settles: acceptance item 1 STANDS.** Easy (`raider_camp_small`) gains its
fire/cook cluster - the WO-1609:105 row this ticket's section 1 measured as **MISSING** (spec asks 1
cluster: `torch_lit.fbx`, or hexagon `haybale.fbx` / `trough.fbx`; authored today: nothing). The
ticket's own line at `:45` calls it *"the single loudest missing 'camp life' tell in the Easy frame"*,
and the owner has now said keep it. It is not to be dropped, deferred, or traded away against
WO-1633's collider work on the same JSON rows.

⛔ **THIS DOES NOT RULE ON SECTION 2, AND MUST NOT BE READ AS DOING SO.** The file's "one owner
question" is a different question entirely - **hexagon-red vs Synty/neutral** for Hard's two yard
buildings (WO-1610:108-109 names `building_barracks_red.fbx` / `building_home_A_red.fbx`; WO-1607
section 4's per-camp kit table assigns Hard "Synty, not hexagon-red"). The owner did not address it.
**Section 2's stated DEFAULT stands unchanged: follow WO-1607 section 4, keep Hard Synty/neutral.**

⚠ **Wording note, surfaced rather than smoothed over:** the ruling's "keep the cook fire" phrasing
reads as a response to a proposal to DROP the fire cluster. No such proposal was located in the
repo this session - a search of `WorkOrders/` and the root `*.md` set for "cook fire" / "cookfire"
returns exactly one hit, `:45` of this file. So the clause is recorded here as an affirmation of
acceptance item 1, which is the only reading the written record supports. If the owner meant
something narrower (a specific token, or a count above 1), that has not been proven from here and
one word from her settles it.

**Status deliberately untouched** (READY TO IMPLEMENT), and the WO-1633 sequencing note above still
holds - these two tickets edit the same JSON rows and are not file-disjoint.
