fix(camera): WO-1765 - raid scenes get the over-the-shoulder profile; the framing scan never frames a wall; yaw evidence reaches raids

Owner ruling 2026-09-16: "its the clear win when the camera spins about as if possessed and people leave the game" - over-the-shoulder for raids, retention priority. Two RCAs merged: raids ran the TOWN camera (the dungeon LockedOTS profile was gated on IsDungeon, so RaidBase_* kept the village recenter whip WO-958 already ruled against), and the framing scan admitted raid WallSegments as hostile subjects (83 distinct walls in the owner's capture; boom pinned 5.75-6.94 m; combat blend never released). The pull-in collapse theory is refuted on magnitude (worst ratio 0.883, floor never reached) - WO-1753 stands.

Shipped: a third raid branch in the one profile seam reading the dungeon numbers (boom 3.28 m, shoulder 0.6, recenter 70 deg/s, framing/lead off, collision on), IsFramingSubject + a name-resolved scan mask scoped to raid scenes ONLY (town behaviour unchanged, pinned by CheckTownIsUntouched over SceneRouter.CastleCandidates), and the throttled yaw instrument (viewYawRate, yawSrc, framingTarget, boom, pullingIn; spike Warn). Four follow-ups minted (WO-1769..1772).

Gated: COMPILE_GATE_OK (2026-09-16 15:15) + REGRESSION_OK 550/550 suites (2026-09-16 15:31); [camera-raid-framing] and [hub-scene-literal] green.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
