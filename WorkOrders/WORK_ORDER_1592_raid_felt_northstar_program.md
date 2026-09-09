> **2026-09-09 — MAPS HALF DECOMPOSED.** Owner asked for raid-base design beyond fence + tower,
> KayKit dungeon welcome. Layout program is WO-1607–1611 (answers 1593 Q1 from disk).
> 1594 (star HUD) and 1595 (AI) stay the other two legs of this north star. Do not rebalance
> caps here.

# WO-1592 — Raid felt north star: maps, AI, and a living star clock

**Status:** SPEC — program spine; maps via WO-1607–1611; HUD 1594; AI 1595  
**Minted:** 2026-09-07 (CLI / Grok seat) — banner bumped 1592 → 1592 in the same edit  
**Priority:** P0 felt — owner: maps look bad (simple walls, towers like pillars); AI just runs and attacks; wants countdown + 3★ that degrade; better KayKit landscapes  
**Depends on / respects:** `docs/RAID_BALANCE_AUDIT_2026-09-06.md` (do not rebalance caps until staging + targeting hold); WO-1520 staging/clock-on-engagement; existing `RaidScoring` / `RaidHudController` spine  
**Lane:** Raid / World / Troops — file-disjoint from Manage Verify

---

## 0. Owner voice (binding intent)

> Maps seem pretty bad — very simple walls; towers look like a pillar.  
> The AI all just seem to run and attack.  
> There should maybe be an onscreen clock counting down starting with 3 stars and as milestones pass you lose the third star then the second.  
> Better landscape and scenes — could be entire KayKit.  
> Be creative and make raiding better.

**Creative authority stays hers** on final art picks, camp names, and which KayKit kits ship. These WOs propose a concrete menu she can accept, trim, or redirect.

---

## 1. What “better raiding” means (player-felt)

A raid should feel like **assaulting a place**, not a flat grey yard with sticks:

| Beat | Feel |
|---|---|
| Approach | Staging outside range (WO-1520) — breathe, deploy, then fight |
| Read the base | Walls have thickness and corners; towers have platforms, banners, roofs — **not cylinders** |
| Fight | **Breach → push spire (capture)**; peel to stay alive under aggro; **deploy + march as a formation** (Front ahead, DPS+ranged behind, healers safe) — never linger farming the wall ring (WO-1595) |
| Pressure | A **countdown** you always see; **three lit stars** that **go out** as time or damage milestones fail |
| Victory | Stars still decide loot; the HUD told the truth the whole fight |

---

## 2. Child tickets (do not implement this file alone)

| WO | Title | Owns |
|---|---|---|
| **1593** | KayKit raid bases (original art pass) | **Implement through 1607–1611** — do not double-bake |
| **1607–1611** | Raid bases as places | Named zones, gatehouse, KayKit dungeon/hexagon dress, three camp layouts |
| **1594** | Live countdown + star degradation HUD | `RaidHudController` + scoring presentation |
| **1595** | Raid AI beyond rush (attackers + defenders) | `TroopController` / garrison brain / role tables |

**Sequence:** 1593 can art-pass in parallel with 1595. **1594** should land early — it makes every other retest readable. Balance numbers stay parked per the audit until Easy-camp acceptance holds.

---

## 3. Non-goals (explicit)

- Raising army slot caps to paper over map/AI defects (audit Option B rejected).  
- Deterministic CoC sim (V2).  
- Hand-editing `RaidBase_*.unity` (CLAUDE.md §3 — builders / injectors only).  
- Closing Manage Verify tickets (owner match loop).

---

## 4. Acceptance for the PROGRAM

1. Owner can name Easy camp as “a place” in one screenshot (walls + towers + ground read).  
2. During a raid, clock + 3★ are always visible; losing a star is felt without opening a menu.  
3. A 10-slot starter army shows **role diversity** in targeting (not one blob chewing one wall).  
4. Easy-camp checklist in `RAID_BALANCE_AUDIT` §4 still applies before any DPS/HP retune.

## 5. Paste for CLI

```text
Implement child WOs 1593 / 1594 / 1595 under WORK_ORDER_1592_raid_felt_northstar_program.md.
Do not rebalance garrison HP until staging + targeting + star HUD are felt-green on Easy camp.
```
