# WORK ORDER 1095 — the raid stranding watchdog fired again: 225 s in a 180 s raid that never finalized

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane RAID)
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1095 → 1096 in the same edit)
**Silo:** Raid
**Severity:** P1 — a felt softlock; only the last-resort watchdog got the player out
**Source:** F8 capture seq=4967 (Editor, scene `RaidBase_raider_camp_small`, `t=2073.54`)

---

## The capture

```
[Flow:Raid] RAID STRANDING WATCHDOG FIRED (last-resort arm) - 225s in a raid scene with a 180s clock
that NEVER finalized, so neither the objective, the clock expiry nor Retreat ended this session
(WO-1526: hero death is no longer an exit - the army fights on and the raid ends by objective,
Retreat or the clock). Routing home anyway. This arm firing means the OnTimeExpired subscriber is
missing or RaidScoring never installed - fix THAT (WO-1437).
```

`RaidDeployController.<StrandingWatchdog>d__31` at `RaidDeployController.cs:398`.

The watchdog did its job — the player was routed home. But it is the **last resort**, and its own text
says so: reaching it means all three real exits failed.

## ⚠ This is NOT simply "WO-1437 regressed" — do not open it as that until the tree is settled

**WO-1437 is CLOSED**, owner felt-test PASS 2026-09-07 (validated `2026-09-07T14:03:08`, build
`2026.09.07.359076`), fix landed in `5bc5025f5`. So the shipped code passed on a device two days ago.

**But this capture is from the EDITOR, against a working tree with six uncommitted raid files:**

```
Assets/_Modules/Village/Troops/RaidDeployController.cs      | 26 +++---
Assets/_Modules/Village/Troops/RaidHudController.cs         | 10 ++-
Assets/_Modules/Village/Troops/RaidScoring.cs               | 12 ++--
Assets/_Modules/Village/Troops/TroopController.cs           | 29 ++++---
Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs  | 84 ++++++++++++++--
Assets/_Modules/Village/World/Camps/RaidVictoryController.cs| 28 +++-----
```

— plus `RaidScoring`/`RaidDeployController` are the exact classes that own the clock and the
subscription. **The running Editor was compiled against in-flight work, not against what shipped.**

## What the dirty diff does and does not explain

- `RaidVictoryController` carries an **owner ruling dated today**: garrison wipe now ALSO wins, not
  only razing the spire (*"a dead camp must settle, not wait for the empty spire to be farmed"*).
  That **widens** the win conditions, so it cannot explain a raid that never ends. Ruled out.
- The `RaidDeployController` hunks I read are **HUD/cosmetic** — status-text removal, deploy-bar
  transparency, spent tiles leaving the bar. They do not touch the subscription at `:238` / `:256-257`.
- `RaidScoring.cs` (12 lines) and `RaidGarrisonSpawner.cs` (84 lines) were **not** read line-by-line
  and are the remaining candidates. **I did not prove which change, if any, is responsible.**

## What would settle it — cheapest first

1. **Did `RaidScoring` exist at all in that session?** The watchdog names two distinct causes —
   *subscriber missing* vs *RaidScoring never installed* — and they need different fixes. The
   instrumentation at `RaidDeployController.cs:212-230` already distinguishes them ("subscriber IS
   installed (bound before the build)"); read that line from the session's log rather than guessing.
2. **Re-run the same raid on a clean tree** (`git stash` the six files, or run the shipped Seeker
   build). If it settles normally, this is an in-flight-work regression and belongs to whoever owns
   that raid lane — not a re-open of WO-1437.
3. If it strands on clean code too, **then** re-open WO-1437 as a regression, with this capture as
   the evidence, and pin it — a P0 that passed a felt-test and came back within 48 h needs a
   behavioural regression case, not a third manual fix.

## ⚠ ADDENDUM 2026-09-09 — the attribution above is SETTLED, and the "Do NOT" below is narrowed

This body was written before the RCA. `docs/READY_RCA_2026-09-09.md` ("WO-1095") then **proved the
cause from capture**, and it is neither of the two the original text was choosing between:

> The watchdog uses total raid-scene age while `RaidScoring` deliberately excludes staging time.
> The failing run entered the raid at 17:15:42.851Z and fired 224.977 s later, exactly its 180+45 s
> bound, while the screenshot still showed 2:46 on the real clock (only 14 s engaged). This is a
> **clock-domain mismatch**, not merely an absent subscriber.

So this is **not** a regression of `5bc5025f5` and **not** one of the six dirty files. It is a
cross-change invariant break: WO-1520 (`d6511b8e52`) made the clock engagement-gated; the watchdog
(`5bc5025f5b`) still measured scene age. Both changes were correct on their own.

**The "Do NOT" below still binds as written — nothing about it was softened.** The 225 s bound is
UNCHANGED, the `Fail` severity is UNCHANGED, and the arm was not deleted. What changed is the
*interval the bound is applied to*. The fix is the measurement, not the net.

## Do NOT

- Do not "fix" the watchdog, raise its 225 s bound, or downgrade its severity. It is the net that
  caught this and its `Fail` severity is correct — unlike a benign handled path, **every firing of
  this arm is a real defect.**
- Do not edit the six dirty files to chase this: another seat owns them mid-flight. Coordinate
  first (§11 multi-session reconciliation).

## Acceptance criteria

- [ ] The session log line naming *subscriber missing* vs *RaidScoring never installed* is quoted in
      the RESULT.
- [ ] The same raid completes by objective, clock expiry and Retreat — all three exits, each proven
      once — with the stranding arm never firing.
- [ ] Attribution stated plainly: in-flight work, or a genuine regression of `5bc5025f5`.
- [ ] If a regression: a behavioural regression case is added so the third occurrence is caught by CI
      rather than by the owner.
- [ ] Owner felt-verifies a raid on device and closes.

## Unproven, recorded honestly

- Which of the six dirty files, if any, caused this. Two were not read line-by-line.
- Whether the Editor session even had the same scene bake as the device — `RaidBase_*.unity` and
  their NavMesh assets are also dirty in this tree.
- Whether the player tried Retreat. The capture says Retreat did not end the session, but a button
  that was never pressed and a button that was pressed and did nothing produce the same line here.
