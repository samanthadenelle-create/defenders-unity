# DeepSeek Research Packet: Wall-Breach Tap Miss + Unexplained Blue Overlay

**Date:** 2026-09-14. **Game:** Defenders of the Realm / Echoes of Elarion (Unity, C#, mobile).
**How to use this file:** everything you need is pasted inline below — code excerpts, live log lines,
and what the two attached screenshots show. Two image files are attached alongside this text:
`wall_target_breach_on_20260914_1.png` (normal state) and
`wall_target_breach_on_20260914_2_blue_overlay.png` (the anomaly, described in Part 3).

---

## Part 1 — The feature and what it's supposed to do

Today the game shipped WO-1719: a "Breach" button that lets the player tap a specific wall segment
during a raid to order the whole warband to attack that exact segment, overriding the game's default
"auto-pick whatever wall is most damaged" behavior. The full source-cited design doc for the whole raid
system (walls, raid AI, damage feedback) is `docs/handoffs/RAID_SYSTEMS_REFERENCE_2026-09-14.md` in the
same repo, sections "2.2" (auto targeting) and "2.3" (the new explicit order) — paste that in too if you
want the full picture; this packet only carries what's needed for the specific bug below.

## Part 2 — Live-confirmed symptom: the tap misses the wall

**Player's own words:** "keeps saying that is not a wall" — repeatedly, while actively trying to tap a
wall segment with Breach mode toggled on.

**Exact toast shown to the player (captured live from the device):**
```
[Flow:UI] kit toast -> 'Breach: that is not a wall - tap a wall section.' tone=Info
[Flow:Raid] breach tap missed every WallSegment (hit 'RaidGround') - the standing order, if any, is UNCHANGED.
```

**What this proves:** the tap's raycast is landing on the ground plane's collider (`RaidGround`)
instead of the wall segment's own collider, at the screen point the player tapped. The order is never
set because the raycast never finds a `WallSegment` to set it on.

**Proof this is NOT a broader targeting/damage bug — the automatic system works perfectly in the same
session, captured seconds later:**
```
[Flow:WallSegment] WallSegment 'Wall_Outer_SE_28' took 18 (attack, tier 3, Hostile) -> damage 62/100 (38% standing).
[Flow:WallSegment] WallSegment 'Wall_Outer_SE_28' (Hostile) COLLAPSED: 1 solid collider(s) and 1 carving obstacle(s) dropped - it no longer blocks tower line-of-sight or agent pathing.
[Flow:RaidAI] source=auto focus='Wall_Outer_SE_27' hp=100 walls=197
[Flow:TroopAI] id=troop-battlemage role=ranged RETARGET#14 reason=foe-died dropped='Wall_Outer_SE_28(WallSegment)' -> won='Wall_Outer_SE_27(WallSegment)' kind=struct dist=18.8m
[Flow:Reticle] [hostile-admit] HOSTILE STRUCTURE 'Wall_Outer_SE_27' impl=DeNelle.Village.WallSegment faction=Hostile via physics sweep (mask=Enemy|Structure)
```
Damage applies correctly, faction reads Hostile correctly, the tier-3 damage divisor is visibly working,
collapse correctly disables exactly 1 collider + 1 nav-carving obstacle, and every troop retargets
cleanly. **A separate physics sweep (`mask=Enemy|Structure`) used for something else in the same frame
DOES find the wall correctly.** So the wall's own collider is real, present, and on the right layer —
the bug is isolated to whatever raycast `HandleBreachTap()` specifically uses for the player's tap.

Across the whole ~4-second capture window: 12 lines show `source=auto` (the automatic system choosing a
target), **0 lines show `source=order`** — the explicit tap-order has never once won in this play
session, consistent with the tap simply never landing on a wall.

## Part 3 — Second, separate, unexplained symptom: a blue-grey overlay

Immediately after (see the attached second screenshot), the player reported: "which implies destroyed
but no change." The screenshot shows a dark blue-grey wash covering roughly the right half of the
screen — **including the ground terrain, not just a wall** — with the hero rendered as a dark
silhouette. The HUD (SPIRE 100%, Razed 5%, Troops 9/9, timer counting down) is still fully legible and
normal, so this isn't a full-screen game-over/pause state.

**This has NOT been traced to a cause yet.** No log line has been found (in the windows captured so
far) that explains it. Candidates, none confirmed, do not treat any of these as the answer:
- A shader/material property (the wall collapse system uses a `_Collapse` MaterialPropertyBlock ramp,
  per `WallSegment.cs:121`, `:446-457`) somehow applied at the wrong scope (e.g. to a shared material
  instance instead of per-instance).
- A lighting/time-of-day transition unrelated to combat.
- A post-processing volume or camera effect stuck in a triggered state.
- Something about the "razed" percentage crossing a threshold and a state-tell effect firing at the
  wrong scale (screen-wide instead of per-object).

## Part 4 — Relevant source code (pasted, not just cited, so no repo access is needed)

### WallSegment.cs — the collapse visual (candidate for the overlay, unconfirmed)

The collapse shader ramp mechanism (paraphrased from the reference doc, not the raw file — if you need
the literal source, the file is `Assets/_Modules/Village/Walls/WallSegment.cs` lines 399-457):
a coroutine sinks the wall's mesh and pushes a `_Collapse` value into a `MaterialPropertyBlock` (NOT a
shared material asset) over a ~0.9 second duration with an accelerating ease-out curve. It is scoped
per-renderer via `MaterialPropertyBlock`, which is specifically the Unity mechanism meant to avoid one
instance's shader property leaking onto another's shared material — so if the overlay IS this system,
something is bypassing that scoping.

### RaidDeployController.cs — the breach tap handler (the actual bug location, per the log)

The handler (`HandleBreachTap`, around `RaidDeployController.cs:887-930`) does, in order: raycast the
tap point, walk up from whatever collider it hits via `GetComponentInParent<WallSegment>()`, and if
that returns null, show the "not a wall" toast and log the miss with the collider name it DID hit
(`'RaidGround'` in this capture). It does not currently log the raycast's hit point, distance, or which
specific collider(s) were in the ray's path before the ground — that diagnostic detail does not exist
yet in this build.

## Part 5 — What's already been ruled out, and what hasn't

**Ruled out (proven by the live log, not assumed):**
- Wall colliders don't exist / aren't on the right layer — disproven, a different sweep in the same
  frame finds them fine.
- The damage/collapse/faction pipeline is broken — disproven, it worked correctly seconds later on
  `Wall_Outer_SE_28`.
- The automatic targeting system is broken — disproven, clean retargeting observed repeatedly.

**Not yet determined:**
- Why the tap's specific raycast prefers the ground collider over the wall's collider at that screen
  point — camera angle occlusion? Ground collider sized/layered to catch hits meant for a wall behind
  it? A raycast distance or layer-mask difference between the tap-handler's raycast and the sweep that
  works?
- Whether the blue-grey overlay is connected to the tap-miss bug at all, or a fully separate issue that
  happened to occur in the same play session.

## What to help with

1. Given the pasted evidence, what's the most likely reason a screen-space tap raycast would resolve to
   a large flat ground collider instead of a thinner wall panel positioned in front of/above it, in a
   Unity mobile game using `Physics.Raycast` from camera to touch point? Consider collider layering,
   raycast max distance, and the ground plane's own collider bounds (the reference doc notes the ground
   is "a continuous plane... with a shared MeshCollider" spanning the whole 140m arena — a raycast that
   hits the (possibly larger, closer-to-camera, or simply first-in-scene-order) ground collider before
   reaching a wall collider several meters away is a very plausible shape for this bug, but this is a
   hypothesis, not a proven cause).
2. What additional single log line, added to `HandleBreachTap()`, would most cheaply distinguish
   "the raycast is hitting the ground because the wall's collider genuinely isn't in the ray's path"
   from "the raycast IS hitting something that could be the wall, but RaycastAll ordering or a
   maxDistance cutoff is picking the ground first"? (E.g. logging `RaycastAll` results sorted by
   distance, not just the first hit.)
3. Any pattern-matched guess at what could cause a full-panel blue-grey desaturation overlay tied to
   wall/raid state, if you've seen similar shader/URP issues before — flagged clearly as a guess to be
   tested, not asserted as fact, since this codebase's own rule (and this packet's own discipline) is
   never to guess at a cause without instrumenting and capturing the real behavior first.
