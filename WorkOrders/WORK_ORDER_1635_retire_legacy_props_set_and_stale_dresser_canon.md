# WO-1635 - Raid dressing: two prop authorities, and four docs that still say the dresser is dead

**Status:** PARTIALLY IMPLEMENTED - canon corrections landed 2026-09-10 (items 4 + 6: six stale props sites banner-corrected against Builds/wave3-bake7; RaidBaseGenerator.cs:35 was already live); items 1-3 + 5 (retire the props.set fallback, moving RaidBaseLayoutRegression.cs:154-155 in the same commit) still to implement (was: IMPLEMENTED - awaiting lead merge (lane PROPS-CANON 2026-09-10))
⛔ **SCOPE OF THAT FLIP — read before trusting it.** Lane PROPS-CANON delivered **acceptance #4 ONLY**
(the stale doc lines get dated correction banners) and confirmed **#6** (`RaidBaseGenerator.cs` untouched;
its `:35` header was **already** corrected at base sha `3da5e5360`). **Acceptance #1, #2, #3 and #5 — the
code/JSON half — are NOT done by this lane** and are still open at `3da5e5360`. See
`WORK_ORDER_1635_retire_legacy_props_set_and_stale_dresser_canon.RESULT.md` for the per-acceptance table
with the proving line for each. Do **not** close this ticket on the Status line alone.

**Minted:** 2026-09-10 (lane RAID-POLISH, main-line banner; bumped 1633 -> 1634 in the SAME edit)
**Silo / Lane:** World / Raid scenes - duplicated state + canon maintenance (CLAUDE.md section 15)
**Files owned:** `Assets/Editor/WallTools/RaidBaseDresser.cs` (`ScatterProps` fallback only),
`Assets/_Modules/Village/World/SceneConfigCatalog.cs` (`PropsDef`), the `props` blocks of
`Assets/Resources/Data/Canonical/scene-configs.json`, `Assets/Editor/Regression/RaidBaseLayoutRegression.cs`,
`WorkOrders/WORK_ORDER_1607_raid_bases_as_places.md`, `WorkOrders/WORK_ORDER_160{8,9}_*.md`,
`WorkOrders/WORK_ORDER_161{0,1}_*.md`
**Do NOT touch:** `RaidBaseGenerator.cs` (lane ARENA-WALL owns it; the stale header at `:35` is fixed
by that lane or by a follow-up, NOT here - see section 3).
**SEQUENCING:** touches `RaidBaseDresser.cs` and the JSON, same as **WO-1633** and **WO-1634**. Land
it last of the three.

---

## 0. Owner voice

> **"i have mentioned it in testing that it feels incomplete and not polished"**
> - owner, 2026-09-10 morning, about the raid arena / bases.

> **"similar strategy as we used in battle arena"**
> - owner, 2026-09-10, same morning, on how to dress the courtyards.

This ticket is not felt work. It exists because the **stale copies below are what made the felt work
nearly get built twice** - the exact duplicated-state failure CLAUDE.md sections 2, 5, 8 and 16 each
describe in their own words. WO-1607:65 (`COURTYARD (camp life + cover + a fight, not empty dirt)`)
is the spec the stale copies were hiding.

---

## 1. TWO authorities for the same prop set

`RaidBaseDresser.ScatterProps` reads props from **three** places in priority order:

| Priority | Source | Line |
|---|---|---|
| 1 | `def.raidDress.props` (`List<RaidDressPropDef>`, token + count + zone) | `:535-539` |
| 2 | `def.props.set` (`List<string>`, one instance each, always zone `Courtyard`) | `:540-552` |
| 3 | `DefaultProps(kit)` - hardcoded in C# | `:553`, body `:571-599` |

Priority 2 is the legacy `PropsDef` (`SceneConfigCatalog.cs:60-66`). Read on 2026-09-10, the live
data is:

- `raider_camp_small` - `"props": { "set": [], "count": 0 }` and a full `raidDress.props`
- `mage_enclave` - `"props": { "set": [], "count": 0 }` and a full `raidDress.props`
- **`fortified_garrison` - `"props": { "set": ["barracks"], "count": 1 }` AND `"barracks"` again as the first entry of `raidDress.props`** - the same prop authored twice, in two schemas
- `player_outpost`, `iron_bastion` - `{ "set": [], "count": 0 }`, no `raidDress` at all

The duplicate is inert **today** only because priority 1 wins and short-circuits priority 2. Delete
or empty `raidDress.props` on Hard and the legacy row silently takes over with a different zone and a
different count. That is a trap, not a fallback.

**Also dead-but-armed:** `DefaultProps(kit)` (`:571-599`) is a third copy of the same content, in C#,
that drifted from the JSON the moment WO-1609/1610/1611 authored the rows - e.g. it gives
hexagon-green a `banner_green`/`weaponrack` set that no longer matches Easy's authored 10-entry array.

## 2. Four documents that say the opposite of the code

| Doc | What it says | What is true (proven 2026-09-10) |
|---|---|---|
| `WORK_ORDER_1607_raid_bases_as_places.md:47` | *"`props` is authored empty and unread. Every raid row has `"props": { "set": [], "count": 0 }`"* | `raidDress.props` is authored on all three raid rows (27 / 25 / 27 instances) and read at `RaidBaseDresser.cs:535-539` |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs:35` | *"Still dead and DELIBERATELY not faked: `props` (no prop dresser for raid bases yet)"* | `RaidBaseDresser.Dress` is called at `RaidBaseGenerator.cs:424` |
| `WORK_ORDER_1609_*.md:3` header | *"251 dress pieces"* | `Builds/wave2-bake2`: `dressed 'raider_camp_small' ... placed=314` |
| `WORK_ORDER_1610_*.md:3` / `WORK_ORDER_1611_*.md:3` headers | *"531 dress pieces"* / *"668 dress pieces"* | `Builds/wave2-bake2`: `placed=489` / `placed=590` |

The two count pairs are hand-copied numbers tracking a live bake - they were right for the bake they
were written against and wrong for the next one. Per CLAUDE.md section 15, a dated WO header is
**frozen**: it gets a `SUPERSEDED`/correction banner naming the fresh evidence, it does **not** get
its body rewritten with a newer number that will rot the same way.

## 3. Scope boundary on `RaidBaseGenerator.cs:35`

That header line is stale, but the file is held by lane **ARENA-WALL** this morning. **Do not open it
from this ticket.** Either hand the one-line header correction to that lane in its own commit, or
mint a follow-up once the ring work lands. Report which was done.

## 4. Acceptance

1. Exactly ONE authority for raid props. Recommended: `raidDress.props` is it; the `props.set`
   fallback at `:540-552` is removed, and `PropsDef` either stops being read for raid configs or is
   documented in-code as non-raid-only. `fortified_garrison`'s duplicate `"barracks"` in `props.set`
   goes.
2. `DefaultProps(kit)` (`:571-599`) is either deleted or reduced to a labelled TGVRU emergency set
   with an in-code note that the JSON is the authority - it must never again be a silent third copy
   of authored content.
3. A regression case reds if any raid config authors props in BOTH schemas, or if a raid config's
   `raidDress.props` is empty. `RaidBaseLayoutRegression` is the suite (it already owns
   `CaseEasyDress`, `:90-108`).
4. The four stale doc lines in section 2 carry dated corrections citing `Builds/wave2-bake2` and
   `RaidBaseDresser.cs:535-539`. Frozen headers get banners, not rewrites (CLAUDE.md section 15).
5. JSON edits byte-safe: `scene-configs.json` is CRLF (352 LF == 352 CRLF at mint time); prove
   LF count == CRLF count after.
6. `RaidBaseGenerator.cs` untouched by this ticket.
