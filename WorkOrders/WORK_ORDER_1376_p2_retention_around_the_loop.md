# WORK ORDER 1376 - P2: build retention around the loop

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:57, build 2026.09.08.361259). PRIOR STATUS: FIXED (navigation slice) - on Firebase App Distribution as build 2026.09.05.356468 (08:2x); awaiting owner felt-test (JOURNEY shows five cards). The remaining slices (ladder, dungeon rewards, troops-in-defence) are listed below as later work. The NAVIGATION slice landed + gated (COMPILE_GATE_OK, REGRESSION_OK 383/383, UI_CAPTURE_OK: Journey deck = five cards - Quests, Raids, Dungeons, Realm Map, Season - at 114 ref px on the derived-row grid); the dungeon-status gate is PROVEN OPEN (`/api/dungeon-status` HTTP 200, 5 open / 1 sealed, dispatcher pass 07:20). Remaining in this WO for later: the weekly ladder, dungeon rewards, and troops-in-wave-defence (owner: "Not P0" - split into its own WO on her word). Was: READY TO IMPLEMENT - sequenced AFTER WO-1375 (now FIXED).
**Silo / Lane:** Retention / Journey navigation + weekly ladder + dungeons + troop defence
**Type:** NEW BEHAVIOUR on existing systems, owner-ruled
**Minted:** 2026-09-04 (CLI)

> ⭐ **THE SPEC IS `docs/PROGRAM_RAID_ECONOMY_2026-09-04.md` — THE NORTH STAR MAP.**
> Owner ruled 2026-09-04: *"these findings take presedence"* / *"this is the north star map"*.
> ⛔ **This ticket deliberately does NOT restate the numbers.** Read them there. A reward table
> copied into a work order is the duplicated-state defect this repo has paid for four times
> (CLAUDE.md §2 WO numbers, §5 the assembly table, §16 the R2 verify, WO-1137's fallback catalog).

## THE DELIVERABLE — the map's §10 "P2" list

Weekly **Realm Threat** ladder · Journey **Dungeons** card · first dungeon accessible · **Season Pass
navigation** · **Realm Map navigation** · dungeon rewards · troops participate in wave defence.

## ⭐ JOURNEY BECOMES FIVE CARDS

Quests · Raids · Dungeons · Realm Map · Season. *"Two cards makes Journey look unfinished."*
The deck was designed for four and shipped with two — `PlayerDeckWorkspace.cs:588-624`.

## ⛔ TWO GATES STAND IN THE WAY, BOTH REAL

1. **All six dungeons are FAIL-CLOSED** behind a live `/api/dungeon-status` row
   (`DungeonStatusCatalog.cs:20-48`) — no network, stale cache or bad payload all resolve to Sealed.
   ⚠ **Whether that endpoint currently serves `open` rows is NOT PROVEN.** Verify it or change the
   gate; do not assume.
2. **Season Pass and Realm Map navigation is ENFORCED ABSENT** by
   `PublicNavigationRetirementRegression`. Re-point it.

## ⚠ TROOPS IN WAVE DEFENCE IS THE LARGEST ITEM HERE
Map §9: **ASSIGN DEFENDERS** before a wave, so the army serves offence AND defence and stops being a
raid tax. ⚠ The owner marked it *"Not P0, though."* It is the tail of this ticket, not its head —
**consider splitting it out** rather than letting it block the navigation wins.

## EVERY NUMBER IS A TUNABLE. BINDING.

Standing rule (owner, 2026-09-02): **a balance value is a TUNABLE, default answer YES.** Register
the map's values as DEFAULTS on the existing rail — `Core/Ops/RemoteTunables.cs` Registry ·
`RemoteTunablesService.cs` · `TUNABLE_KEYS` in `api/_lib/tunables.js` · the Command Center Balance
tab — **all four in the SAME commit**; `[tunable-defaults]` names any two that disagree.
⛔ Do not build a second rail. No row / no network / no parse ⇒ today's behaviour exactly.
⭐ She is setting the reward curve for the main loop BY FEEL. Every value must reach her device in
~40 seconds, not a 10-minute APK round trip.

## ⛔ REGRESSION COVERAGE IS NOT OPTIONAL

Owner, 2026-09-04: *"shouldnt these items all have regression cases? so that stuff cant break
working features"*. **Every behaviour this ticket adds ships with an oracle, PROVEN RED FIRST**
against the real failing input, then green. A test that has never failed proves nothing (WO-1138).
Register each suite in `DataRegression` — an unregistered oracle never runs (the WO-973 failure).

## WHAT NOT TO TOUCH
- ⛔ Do not build PvP (map §9). *"That's a whole dragon."*

---
## RCA re-verified 2026-09-04 (QA read-only pass)
**Verdict:** NEW-FEATURE
**Evidence:**
- The sequencing gate is released: `WorkOrders/WORK_ORDER_1375_p1_raid_progression.md:3` reads `**Status:** FIXED - in build 2026.09.05.355872, installed on the Seeker 2026-09-04 22:22` (RESULT file present). WO-1374 is FIXED in the same build.
- The spec exists: `docs/PROGRAM_RAID_ECONOMY_2026-09-04.md:295` "### P2 - build retention around it (**WO-1376**)"; `:37` says WO-1374/1375/1376 execute it and point at it rather than restating.
- Gate 1 is real: `Assets/_Modules/Core/World/DungeonStatusCatalog.cs:2` "THE SAFETY DIRECTION IS FAIL-CLOSED", `:19-23` explains Sealed-on-anything-bad. `api/dungeon-status.js` exists; whether it currently serves `open` rows is NOT proven from this seat (no request made).
- Gate 2 is real: `Assets/Editor/Regression/PublicNavigationRetirementRegression.cs` exists.
- Journey deck: `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:588-624` builds exactly two cards, `Title = "Quests"` (`:591`) and `Title = "Raids"` (`:610`); last touched `3921a487e` / `e63494ed8` 2026-09-03 (WO-1357). No Dungeons / Realm Map / Season card exists.
- Tunables rail (`Core/Ops/RemoteTunables.cs` Registry, `api/_lib/tunables.js TUNABLE_KEYS`) exists; no `1376` / ladder keys registered.
**What changed since the RCA:** WO-1375 landed, so "sequenced AFTER" no longer blocks; nothing of this ticket's scope is built.
**Ready for a lane?** yes for the navigation half (five Journey cards, re-point the retirement oracle, dungeon gate); no for troops-in-wave-defence, which the owner marked "Not P0" and the WO itself suggests splitting. It is unbuilt scope - route as a spec (s13), not an RCA. Files a lane would touch: `HUD/PlayerDeckWorkspace.cs`, `Core/World/DungeonStatusCatalog.cs`, `Editor/Regression/PublicNavigationRetirementRegression.cs`, `Core/Ops/RemoteTunables.cs` + `RemoteTunablesService.cs` + `api/_lib/tunables.js`, Command Center Balance tab, new oracles.
**Pins/rulings needed:** (1) prove or change the `/api/dungeon-status` gate before building on it; (2) owner: split troops-in-wave-defence into its own WO?; (3) the map's P2 numbers are the defaults - none copied here, by design.
