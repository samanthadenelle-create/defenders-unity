# WORK ORDER 1771 — The TOWN hub flaps the point-blank pull-in ~78 times a minute

**Status:** BLOCKED - ask the owner whether it is felt before any work

**Minted:** 2026-09-16 by the lead from WO-1765 §17.3

**Silo:** camera

---

## Symptom / Evidence

The largest burst of `OCCLUDER PULL-IN ENTERED` in the whole 3.16 M-line capture is **78 in the minute 13:22**, and that minute is **`Main_Castle_Overworld`** — the hub, not the raid (`:3022710`, after `LoadSceneWithFade name='Main_Castle_Overworld'` at `:3002017`). Other pre-raid bursts: 33 at 13:10, 47 at 13:17, 18 at 13:20. With `_collisionApproachSpeed` 40 in and `_collisionReturnSpeed` 8 out, that is a continuous boom sawtooth in the scene the player spends the most time in.

**Evidence:** `logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt:3022710` — measured from the owner's own Seeker run, APK 2026.09.16.371701. Nothing in the capture says it is felt.

---

## Files to edit

Not measured

---

## What NOT to touch

- No code edits until the owner confirms this is a felt issue. SmartMobileCamera.cs WO-1765 instrumentation is permanent.
- CLAUDE.md §3 bake rules apply if a scene edit is needed.

---

## Acceptance criteria

1. Owner confirmation: the boom sawtooth in the town hub is felt as an issue worth fixing.
2. Root cause named from the same heartbeat fields as WO-1765's yaw instrument (boom, distanceFrac, pullingIn, approach/return speeds).
3. Fix validated with a fresh capture showing pull-in frequency lowered.

---

## Source

WO-1765 RESULT §9B.6a and WO §17.3
