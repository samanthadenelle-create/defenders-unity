# WORK ORDER 1770 — `Garrison_*` / `Outpost1-2` raids still run the TOWN camera and the 220 deg/s whip

**Status:** READY FOR LEAD REVIEW

**Implemented:** 2026-09-17 — one shared term, `SmartMobileCamera.ResolvesToOpenAirRaidTarget`
(`HubScenes.IsEnemyOutpost && !HubScenes.IsDungeon`), OR'd into `ResolvesToRaidCameraProfile`,
`AppliesRaidScanNarrowing` and `ShouldEmitYawEvidence`. `HubScenes.IsRaid` is UNTOUCHED, so the HUD
combat-cluster gate and `RaidDeployController` are unchanged. New positive pins in
`CameraRaidFramingRegression.CheckOpenAirRaidTargetsGetTheRaidSeat`; no negative scope pin widened.
Awaiting the lead's combined-tree gate, then the owner felt-test (AC3).

**Minted:** 2026-09-16 by the lead from WO-1765 §17.2

**Silo:** camera

---

## Symptom / Evidence

`HubScenes.IsRaid` matches **only** `RaidBase*`. `IsDungeon`'s own remark says `Garrison_*` / `Outpost1-2` are *"deliberately NOT dungeons: they are open-air raid targets that keep the outdoor camera and a real sky."* So the over-the-shoulder profile, the lazy recenter and the raid scan scoping all miss them: a Garrison raid gets the town seat and the village whip.

Not widened in WO-1765 because `IsRaid` is **shared** with the HUD combat-cluster gate and RaidDeployController's self-install, so broadening it reaches well outside a camera ticket. Recorded so the owner's next "still rotating" report from a Garrison is not read as a 1765 regression.

---

## Files to edit

- `Assets/_Modules/Village/Hero/SmartMobileCamera.cs` — `ResolvesToRaidCameraProfile` method and `AppliesRaidScanNarrowing` predicate (one added term each)

---

## What NOT to touch

- The HUD combat-cluster gate in other modules; RaidDeployController; CLAUDE.md §3 bake rules apply.
- WO-1765's raid profile and scope narrowing logic in SmartMobileCamera; do not widen the gate in regression checks.

---

## Acceptance criteria

1. `ResolvesToRaidCameraProfile` and `AppliesRaidScanNarrowing` return true for `Garrison_*` and `Outpost1-2` scene names, alongside `RaidBase*`.
2. A new HubScenes predicate is added if needed (e.g., `IsOpenAirRaidTarget`), or the term is added directly.
3. Owner felt-test: Garrison raid, camera behaves like Iron Bastion raid (over-the-shoulder, lazy recenter, no wall framing). PO closes.

---

## Source

WO-1765 RESULT §13 item 4b and WO §17.2
