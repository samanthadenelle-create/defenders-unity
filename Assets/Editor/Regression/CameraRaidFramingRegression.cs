using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Core.Combat;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1765 — the camera must never FRAME A WALL, a raid must never go yaw-silent again, and the
    /// raid over-the-shoulder seat must stay inside the raid's own geometry.
    /// <para>
    /// ⛔ WHY THIS IS A SEPARATE SUITE FROM <see cref="CameraWallOcclusionRegression"/>. That suite
    /// pins DISTANCE behaviour (the WO-1734/1751/1753 occlusion contract) and it must stay green
    /// untouched — every one of those three commits works on the boom, and the owner's report
    /// ("still rotating too much with the walls") is about ROTATION. Mixing a yaw oracle into a
    /// distance oracle is how a future seat "fixes" one by weakening the other.
    /// </para>
    /// <para>
    /// ⛔ AND WHY THE CASES CALL CODE INSTEAD OF GREPPING IT. This file's sibling has already
    /// shipped TWO source-text lint misfires in one day — once pinning the defect in place, once
    /// reporting a correct fix as broken (see its <c>ExtractMethodBody</c> remarks). A
    /// <c>Contains()</c> cannot tell a hostile mob from a hostile wall, cannot tell layer 7 from
    /// layer 8, and cannot do arithmetic. So every behavioural claim below DRIVES the real statics:
    /// <see cref="SmartMobileCamera.IsFramingSubject"/>,
    /// <see cref="SmartMobileCamera.ComputeDefaultEnemyScanMask"/>,
    /// <see cref="SmartMobileCamera.ShouldEmitYawEvidence"/>,
    /// <see cref="SmartMobileCamera.ShouldPullIn"/> and
    /// <see cref="SmartMobileCamera.AllowedCameraDistance"/>. Only the §12 instrumentation-permanence
    /// checks are source-text, because "a trace still exists" is the one claim that IS a string.
    /// </para>
    /// <para>
    /// Editor-only, batchmode-runnable, NO PlayMode: nothing here instantiates a
    /// <see cref="SmartMobileCamera"/> (that would run Awake/OnEnable for real and fight the editor
    /// scene's cameras). The framing-subject cases use plain C# doubles of the two shapes that
    /// matter, which is exactly why <c>IsFramingSubject</c> takes an interface and not a Component.
    /// </para>
    /// </summary>
    public static class CameraRaidFramingRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            CheckFramingSubjectRule(failures);
            CheckTownIsUntouched(failures);
            CheckEnemyScanMask(failures);
            CheckRaidProfile(failures);
            CheckYawEvidenceGate(failures);
            CheckOpenAirRaidTargetsGetTheRaidSeat(failures);
            CheckYawInstrumentPermanence(failures);

            reason = failures.Count == 0
                ? "CAMERA_RAID_FRAMING_OK walls are not framing subjects; the raid seat is over-the-shoulder and yaw evidence reaches a raid"
                : "CAMERA_RAID_FRAMING_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }

        // ── (a) THE C1 ADMISSION RULE ────────────────────────────────────────────────────────
        //
        // THE DEFECT: `ScanForEnemies` admitted anything with a live Hostile IDamageable, and
        // `WallSegment.Faction` is Hostile in an enemy-owned scene BY DESIGN
        // (WallSegment.cs:288-289 — without it a raid wall is indestructible). A captured device
        // session names 115 distinct `Wall_*` segments admitted as hostile structures through the
        // very same physics-sweep shape. The look-at was then dragged toward the NEAREST of them,
        // and the nearest of 115 candidates switches constantly along a wall line — each switch
        // flipping a lateral look-at arm whose screen-yaw gain is 1/boom.
        //
        // The rule is a SECOND INTERFACE test, which is the only thing that separates the two
        // populations: the dual implementers (WallSegment / Gate / DefenseTower / RaidSpire) are
        // all scenery, while the mobile hostiles (EnemyDamageable, DragonBoss) implement
        // IDamageable and NOT IDamageableStructure.
        private static void CheckFramingSubjectRule(List<string> failures)
        {
            var mob = new MobileHostileDouble();
            if (!SmartMobileCamera.IsFramingSubject(mob))
                failures.Add("a live HOSTILE MOBILE (IDamageable only - the EnemyDamageable / DragonBoss "
                    + "shape) was REJECTED as a framing subject; the auto-framing would then never "
                    + "frame a fight at all, which is the opposite defect");

            var wall = new HostileStructureDouble();
            if (SmartMobileCamera.IsFramingSubject(wall))
                failures.Add("a live HOSTILE STRUCTURE (IDamageable + IDamageableStructure - the "
                    + "WallSegment / Gate / DefenseTower / RaidSpire shape) was ADMITTED as a framing "
                    + "subject. That is WO-1765 C1: the camera look-at is dragged toward the nearest "
                    + "raid wall, and the nearest of 115+ panels switches as the hero walks a wall "
                    + "line, swinging the view - the owner's 'the camera rotates with the walls'");

            var deadMob = new MobileHostileDouble { Alive = false };
            if (SmartMobileCamera.IsFramingSubject(deadMob))
                failures.Add("a DEAD hostile was admitted as a framing subject - the camera would "
                    + "hold its frame on a corpse");

            var friendlyMob = new MobileHostileDouble { Side = CombatFaction.Friendly };
            if (SmartMobileCamera.IsFramingSubject(friendlyMob))
                failures.Add("a FRIENDLY body was admitted as a framing subject - the camera would "
                    + "frame the player's own troops as enemies");

            if (SmartMobileCamera.IsFramingSubject(null))
                failures.Add("a null candidate was admitted as a framing subject");
        }

        // ── (a2) THE SCOPE OF THE C1 CORRECTION — RAID ONLY, BY RULING ───────────────────────
        //
        // ⛔ THIS CASE EXISTS BECAUSE PINNING THE RULE WITHOUT PINNING ITS SCOPE WOULD STAY GREEN
        // WHILE THE TOWN MOVED. Lead ruling 2026-09-16: "the TOWN must not change felt behaviour in
        // this build." Both halves of the C1 correction — the narrowed scan mask and the structure
        // filter — are scoped by the ONE predicate AppliesRaidScanNarrowing, because in town they are
        // not cosmetic: a baked town camera's _enemyMask is m_Bits 256 = layer 8 "Structure" ONLY, so
        // structures are the only thing its scan can see. Narrow the mask there and the combat zoom
        // starts firing on mobs for the first time; filter structures out and the town's combat zoom
        // and auto-framing stop firing at all. Either way the owner feels a change in the hub she did
        // not ask for. So the rule lands where the defect is reported and where the mask is ours (the
        // runtime-attached raid camera), and this case is what stops a later "tidy-up" globalising it.
        private static void CheckTownIsUntouched(List<string> failures)
        {
            if (!SmartMobileCamera.AppliesRaidScanNarrowing("RaidBase_IronBastion"))
                failures.Add("the raid does NOT get the narrowed scan mask / structure filter, so the "
                    + "C1 correction does not apply in the one scene kind WO-1765 was opened on");
            // ⛔ THE HUB IS RESOLVED, NEVER TYPED, AND EVERY CANDIDATE IS CHECKED.
            // hub-scene-literal (HubSceneLiteralRegression) caught a hardcoded hub name here on
            // 2026-09-16 and it was right to: a typed-in hub goes stale SILENTLY, and a stale gate
            // reports OK while watching a scene the player never loads — the way UICaptureMode,
            // TowerRespawnRegression and FloorDeepDiag were all pinned to the retired hub at once.
            // ⚠ AND HERE IT ITERATES CastleCandidates RATHER THAN READING SceneRouter.Castle, which is
            // the OPPOSITE choice from DungeonCameraFeelRegression's — deliberately. `Castle` resolves
            // only the branch ff.MergedWorld happens to be flagged into, so it would prove this for one
            // hub and leave the other unguarded. The claim here is a NEGATIVE that must hold for BOTH
            // ("no hub, in either configuration, gets the raid narrowing"), and index [1]
            // (the legacy MainCastle_Hall, still on disk per CLAUDE.md §7) must satisfy it just as
            // much as [0]. Iterating is therefore strictly stronger and carries no false failure.
            foreach (string hub in DeNelle.Core.SceneRouter.CastleCandidates)
                if (SmartMobileCamera.AppliesRaidScanNarrowing(hub))
                    failures.Add("the home hub '" + hub + "' now gets the narrowed scan mask / structure "
                        + "filter. The lead ruled it must not: the baked hub camera's mask is "
                        + "Structure-ONLY (m_Bits 256), so narrowing it starts firing the combat zoom on "
                        + "mobs for the first time and filtering structures out retires the hub's combat "
                        + "zoom entirely - a felt change in the hub, in a build that is not taking one");
            // Village2 is the raid TARGET town, not a hub, and SceneRouter owns its name as a const -
            // so it is resolved the same way rather than re-typed.
            if (SmartMobileCamera.AppliesRaidScanNarrowing(DeNelle.Core.SceneRouter.Village))
                failures.Add("'" + DeNelle.Core.SceneRouter.Village + "' now gets the narrowed scan mask "
                    + "/ structure filter - it bakes the same Structure-only mask as the hub "
                    + "(Village2.unity:3387-3389)");
            if (SmartMobileCamera.AppliesRaidScanNarrowing("Dungeon_HealersCottage"))
                failures.Add("a dungeon now gets the raid scan narrowing - the dungeon profile switches "
                    + "framing off anyway, so this is scope creep with no benefit and an untested "
                    + "change to WO-920's camera");
            if (SmartMobileCamera.AppliesRaidScanNarrowing(null)
                || SmartMobileCamera.AppliesRaidScanNarrowing(string.Empty))
                failures.Add("an empty scene name resolved to 'apply the raid scan narrowing'");

            // The scope predicate and the profile predicate must agree, or the mask can be narrowed in
            // a scene that does not get the raid seat (or the reverse) and nobody would notice.
            var agreementScenes = new List<string>
                { "RaidBase_IronBastion", DeNelle.Core.SceneRouter.Village, "Dungeon_HealersCottage" };
            agreementScenes.AddRange(DeNelle.Core.SceneRouter.CastleCandidates);   // hub resolved, not typed
            foreach (string scene in agreementScenes)
                if (SmartMobileCamera.AppliesRaidScanNarrowing(scene)
                    != SmartMobileCamera.ResolvesToRaidCameraProfile(scene))
                    failures.Add("for scene '" + scene + "' the scan-narrowing scope and the raid camera "
                        + "profile disagree - the mask would be narrowed in a scene that does not get "
                        + "the raid seat, or vice versa, and the two must be the same set");

            // The narrowing must be REVERSIBLE, or a camera that survives raid -> town keeps it for the
            // rest of the session and the town changes anyway, one scene transition later.
            string cam = ReadCameraSource();
            if (cam.Length > 0)
            {
                if (!cam.Contains("_villageEnemyMask               = _enemyMask;")
                    && !cam.Contains("_villageEnemyMask = _enemyMask;"))
                    failures.Add("the town's baked enemy-scan mask is no longer snapshotted, so the raid "
                        + "narrowing cannot be undone");
                if (!cam.Contains("_enemyMask   = _villageEnemyMask;")
                    && !cam.Contains("_enemyMask = _villageEnemyMask;"))
                    failures.Add("the town's baked enemy-scan mask is never restored - a camera that "
                        + "survives a raid->town transition would carry the narrowed mask into the hub "
                        + "and change town framing for the rest of the session");
                // And it must not be resolved unconditionally on Awake again, which is where it lived
                // before the ruling. ApplyRaidSeat is the only legitimate caller.
                int calls = System.Text.RegularExpressions.Regex.Matches(
                    cam, @"(?<!private void )ResolveEnemyScanMask\(\)\s*;").Count;
                if (calls != 1)
                    failures.Add("ResolveEnemyScanMask() is called " + calls + " time(s); exactly ONE "
                        + "call site is allowed and it is ApplyRaidSeat. It used to be called from "
                        + "Awake for every scene, which is the town change the lead ruled out");
            }
        }

        // ── (b) THE SCAN MASK, AND THE 256 TRAP ──────────────────────────────────────────────
        //
        // ⛔ READ THIS BEFORE "RESTORING" m_Bits 256 ANYWHERE. The baked town cameras serialize
        // `_enemyMask m_Bits: 256` (Main_Castle_Overworld.unity:3034-3036,
        // Village2.unity:3387-3389). 256 is 1<<8, and ProjectSettings/TagManager.asset declares
        // layer 8 as "Structure"; the Enemy layer is SEVEN. So that baked value is not a narrowed
        // enemy mask at all - it is a STRUCTURE-ONLY mask, i.e. a town camera whose combat zoom and
        // auto-framing could only ever have been driven by masonry. WO-1765's brief asked for the
        // raid camera to "mirror m_Bits 256"; mirroring the NUMBER would have shipped that bug into
        // the raid, so the INTENT (narrow to the enemy layer) was implemented by NAME instead and
        // the discrepancy is recorded in the WO. This case is what stops the number coming back.
        private static void CheckEnemyScanMask(List<string> failures)
        {
            int enemy = LayerMask.NameToLayer("Enemy");
            int structure = LayerMask.NameToLayer("Structure");
            if (enemy < 0)
                failures.Add("project layer 'Enemy' does not exist - the camera's combat scan cannot "
                    + "be narrowed to it (ProjectSettings/TagManager.asset)");
            if (structure < 0)
                failures.Add("project layer 'Structure' does not exist - every raid wall panel lives "
                    + "there (RaidBaseGenerator.cs:1999-2000)");
            if (enemy >= 0 && structure >= 0 && enemy == structure)
                failures.Add("'Enemy' and 'Structure' resolve to the SAME layer index - the scan mask "
                    + "can no longer separate a mob from a wall");
            if (enemy >= 0 && (1 << enemy) == 256)
                failures.Add("the Enemy layer is now bit 256 - the WO-1765 note about the baked "
                    + "m_Bits: 256 town cameras is stale and must be re-read, not trusted");

            int mask = SmartMobileCamera.ComputeDefaultEnemyScanMask();
            RequireLayerInMask(failures, mask, "Enemy",
                "EnemyFactory.cs:51-52 puts every spawned hostile body on this layer");
            RequireLayerInMask(failures, mask, "Default",
                "DragonBoss sets no layer at all, so a boss may sit on Default; excluding it would "
                + "silently blind the framing to the one fight that needs it");
            RequireLayerNotInMask(failures, mask, "Structure",
                "a raid base bakes 158 Wall_* colliders here and the scan buffer is 32 slots - with "
                + "them in the mask the sweep can be full of masonry before it sees a mob");
            RequireLayerNotInMask(failures, mask, "Building",
                "town buildings are not combat-framing subjects");
            RequireLayerNotInMask(failures, mask, "Tower",
                "towers are not combat-framing subjects");
            RequireLayerNotInMask(failures, mask, "UI",
                "UI colliders must never enter a world sweep");
            RequireLayerNotInMask(failures, mask, "Water",
                "water must never enter the combat sweep");

            // The raid camera is runtime-attached and the town cameras are baked, but there is only
            // ONE computation for both - that is the point of the method. A per-scene branch would
            // be the duplicated state CLAUDE.md §5 forbids, and it is what let town and raid drift.
            if (SmartMobileCamera.ComputeDefaultEnemyScanMask() != mask)
                failures.Add("ComputeDefaultEnemyScanMask is not deterministic - the raid mask and the "
                    + "town mask must be the SAME resolved value, from the same one authority");
        }

        // ── (c) THE RAID OVER-THE-SHOULDER PROFILE ───────────────────────────────────────────
        //
        // Owner ruling 2026-09-16: "its the clear win when the camera spins about as if possessed
        // and people leave the game." The lead's direction: over-the-shoulder for raid scenes.
        //
        // ⛔ THE BOOM IS NOT A TASTE SETTING - IT IS SIZED AGAINST THE PULL-IN GATE. The gate fires
        // when (boom - gateDistance) < _occluderPullInDistance (0.6 m), where the gate distance is a
        // SPHERECAST hit distance from the hero's chest, i.e. (surface distance - _collisionRadius
        // 0.35). So for a wall whose surface is D metres from the pivot the gate fires for
        // D in (boom - 0.25, boom + 0.35], and a wall beyond boom + 0.35 is not hit at all. The
        // arithmetic below is run against the real statics, so a future seat that lengthens the raid
        // boom past the raid's own partition FAILS here instead of shipping the collapse.
        private static void CheckRaidProfile(List<string> failures)
        {
            const float pullIn = 0.6f;          // _occluderPullInDistance, authored
            const float skin = 0.2f;            // _collisionSkin, authored
            const float radius = 0.35f;         // _collisionRadius, authored
            const float floor = 1.2f;           // _minCollisionDistance, authored
            const float conservativePartition = 3.0f;   // the lead's stated raid wall partition
            const float heroBodyRadius = 0.4f;  // HeroLocomotion.cs:989, _agent.radius

            const float townBoom = 4.501f;      // |(0, 2.6-2.5, -4.5)|, the shipped town seat
            float boom = SmartMobileCamera.RaidCam.Boom;

            if (boom >= townBoom - 0.5f)
                failures.Add("the raid boom is " + boom.ToString("0.###") + " m - not meaningfully "
                    + "shorter than the town seat (" + townBoom.ToString("0.###") + " m). The whole "
                    + "ruling is a SHORTER over-the-shoulder seat: at the town boom the seat lands "
                    + "outside a 3 m raid space, the far wall is faded, and the next occluder out "
                    + "collapses the boom toward the 1.2 m floor where a small lateral offset is an "
                    + "enormous screen rotation");
            if (boom <= floor)
                failures.Add("the raid boom (" + boom.ToString("0.###") + " m) is at or inside the "
                    + "_minCollisionDistance floor (" + floor.ToString("0.#") + " m) - the seat would "
                    + "start in the hero's back and the pull-in would have nowhere to go");
            if (SmartMobileCamera.RaidCam.ShoulderOffset <= 0.05f)
                failures.Add("the raid seat has no lateral SHOULDER offset, so it is merely a close "
                    + "camera and not an over-the-shoulder one (the hero's own body then hides the "
                    + "aim line ahead of her)");

            // THE LOAD-BEARING CASE. Hero pressed against one face of a `conservativePartition`
            // space; her body radius holds her centre off it, so the FAR face sits at
            // (partition - bodyRadius) from the pivot. The camera looks back across the space, so
            // that far face is the occluder nearest the SEAT - the one WO-1753's gate judges.
            float farFaceDist = conservativePartition - heroBodyRadius;
            float gateDist = farFaceDist - radius;                 // a spherecast hit distance
            bool firesInPartition = gateDist <= boom
                && SmartMobileCamera.ShouldPullIn(boom, gateDist, pullIn);
            if (firesInPartition)
            {
                // Firing is tolerable; COLLAPSING is not. The seat must stay bounded by the
                // occluder, never driven to the emergency floor, or the yaw gain (1/boom) explodes.
                float seat = SmartMobileCamera.AllowedCameraDistance(boom, gateDist, skin, floor);
                if (seat <= floor + 0.001f)
                    failures.Add("in a " + conservativePartition.ToString("0.#") + " m raid space with "
                        + "the hero against a wall, the pull-in collapses the raid seat to the "
                        + floor.ToString("0.#") + " m emergency floor (boom " + boom.ToString("0.###")
                        + " -> " + seat.ToString("0.###") + "). At that seat every lateral look-at "
                        + "offset becomes an enormous screen rotation - the exact felt defect");
                if (seat < boom * 0.75f)
                    failures.Add("the worst-case raid pull-in is " + seat.ToString("0.###") + " m "
                        + "against a " + boom.ToString("0.###") + " m boom - more than a quarter of "
                        + "the seat, so the yaw gain jumps whenever the hero touches a wall");
            }

            // -- The DOCUMENTED aperture, not just the brief's conservative one. WO-1723's RESULT
            // (:230, :271) records the raid wall module / gate opening at ~3.9 m. Hero against one
            // face: the far face sits at 3.9 - 0.4 = 3.5 m, gate distance 3.15 - INSIDE the firing
            // band for this boom, so the backstop fires here and what it does is the whole question.
            const float documentedAperture = 3.9f;
            float farGate = documentedAperture - heroBodyRadius - radius;
            if (farGate <= boom && SmartMobileCamera.ShouldPullIn(boom, farGate, pullIn))
            {
                float seat39 = SmartMobileCamera.AllowedCameraDistance(boom, farGate, skin, floor);
                if (seat39 <= floor + 0.001f)
                    failures.Add("in the documented ~" + documentedAperture.ToString("0.#") + " m raid "
                        + "aperture the pull-in collapses the seat to the " + floor.ToString("0.#")
                        + " m emergency floor (boom " + boom.ToString("0.###") + " -> "
                        + seat39.ToString("0.###") + "). At that seat every lateral look-at offset is "
                        + "an enormous screen rotation - the felt defect, in the most common geometry "
                        + "in the whole raid");
                if (seat39 < boom * 0.75f)
                    failures.Add("the pull-in in the documented raid aperture is "
                        + seat39.ToString("0.###") + " m against a " + boom.ToString("0.###")
                        + " m boom - more than a quarter of the seat, so the yaw gain jumps every "
                        + "time the hero walks a wall line");
            }

            // A clear line of sight must never be a pull-in, at the raid boom either.
            if (SmartMobileCamera.ShouldPullIn(boom, SmartMobileCamera.NoOccluderGateDistance, pullIn))
                failures.Add("a clear line of sight fired the pull-in at the raid boom");

            // The profile's felt switches, each a single constant so the lead can flip one.
            if (SmartMobileCamera.RaidCam.FramingEnabled)
                failures.Add("auto-framing is ON in the raid profile. Yaw gain is 1/boom: a 0.2 bias "
                    + "toward a hostile 10 m out is a 2.0 m lateral arm, which at the ~2.3 m raid "
                    + "boom is ~41 degrees of view yaw versus ~24 at the 4.5 m town boom - "
                    + "shortening the boom makes framing MORE violent, so the two ship together");
            if (!SmartMobileCamera.RaidCam.CollisionEnabled)
                failures.Add("occlusion is OFF in the raid profile - the ruling is that occluders "
                    + "FADE (WO-385) and the seat holds; switching collision off instead lets the "
                    + "camera body sit inside raid masonry");
            if (SmartMobileCamera.RaidCam.CombatZoomOut > 0.001f
                || SmartMobileCamera.RaidCam.CombatFovBoost > 0.001f)
                failures.Add("the raid profile still pumps the seat/FOV on enemy proximity - at a "
                    + "~2.3 m boom that pump is violent, and it breaks the partition arithmetic the "
                    + "boom was sized against");
            // ── The seat values are the DUNGEON's, by reference, not a re-typed copy ─────────
            //
            // The owner has already approved that seat (WO-920), and the lazy recenter below came
            // from her filing THIS SAME complaint underground (WO-958, F8 seq 2289 "its auto
            // rotating"). If a future edit re-types them into the raid profile, the two drift and
            // the raid silently keeps a seat she never signed off. Read through the profile that
            // owns them, so the assertion cannot be satisfied by a copy.
            if (Math.Abs(SmartMobileCamera.RaidCam.CameraHeight
                    - DeNelle.Core.World.DungeonCameraProfile.CameraHeight) > 0.0001f
                || Math.Abs(SmartMobileCamera.RaidCam.CameraDistance
                    - DeNelle.Core.World.DungeonCameraProfile.CameraDistance) > 0.0001f
                || Math.Abs(SmartMobileCamera.RaidCam.LookAtHeight
                    - DeNelle.Core.World.DungeonCameraProfile.LookAtHeight) > 0.0001f)
                failures.Add("the raid seat no longer tracks the owner-approved dungeon seat "
                    + "(DungeonCameraProfile CameraHeight/CameraDistance/LookAtHeight) - either it was "
                    + "re-typed as a copy, which drifts, or it was re-tuned without a ruling");

            // ⛔ THE ROOM-TOPOLOGY BLOCKS MUST STAY DUNGEON-ONLY. This is why the raid got a THIRD
            // branch instead of a widened IsDungeon gate: an open arena has no rooms and no ceiling,
            // so a shared gate would install a ceiling clamp, a room-aware seat damp and a facing
            // look-ahead whose inertness could only be argued, never proven. Gated on
            // _dungeonProfileActive they are inert BY CONSTRUCTION - pin that, or the argument
            // quietly becomes load-bearing again.
            // The INVERSE pin, and the only shape that is this suite's business: none of the three
            // may be re-gated to include the raid. (That they remain WIRED for dungeons is
            // DungeonCameraTightRoomRegression.cs:96-99's job — asserting it here too would be the
            // second hand-maintained copy CLAUDE.md §5 forbids.)
            string cam = ReadCameraSource();
            if (cam.Length > 0)
            {
                foreach (var leak in new[]
                {
                    "_raidProfileActive) zoomOffset = DungeonRoomSeat",
                    "_raidProfileActive && DungeonCam.FacingLookAhead",
                    "_raidProfileActive) && DungeonCam.FacingLookAhead",
                    "_raidProfileActive || _dungeonProfileActive) && DungeonCam.FacingLookAhead",
                    "_dungeonProfileActive || _raidProfileActive) && DungeonCam.FacingLookAhead",
                })
                    if (cam.Contains(leak))
                        failures.Add("a dungeon ROOM-TOPOLOGY feature was extended to raids ('" + leak
                            + "'). An open raid arena publishes no rooms and has no ceiling; the raid "
                            + "got its own profile branch precisely so these stay inert by "
                            + "construction rather than by an argument nobody can prove on a device");
            }

            // The look-at lever arm the raid profile deletes outright: with LeadDistance 0 AND
            // framing off, _leadPoint IS the hero's chest, so the ONLY remaining yaw source in a
            // raid is _panYaw (player drag + the lazy recenter). That is the whole claim of the fix,
            // so it is pinned as arithmetic, not prose.
            if (SmartMobileCamera.RaidCam.LeadDistance > 0.001f)
                failures.Add("the raid profile still leads the look-at by "
                    + SmartMobileCamera.RaidCam.LeadDistance.ToString("0.##") + " m. The lead term is "
                    + "UNSCALED by speed, so a hero scrubbing along a wall at 0.15 m/s swings the "
                    + "full lever (WO-1765 C2); with lead 0 and framing off the look-at IS the hero's "
                    + "chest and the only yaw source left is _panYaw");

            if (SmartMobileCamera.RaidCam.FacingRecenterMaxSpeed > 100f)
                failures.Add("the raid facing-recenter still swings at "
                    + SmartMobileCamera.RaidCam.FacingRecenterMaxSpeed.ToString("0")
                    + " deg/s - the town value (220) is WO-1765 candidate C3, the swing that fires "
                    + "every time the hero stops against a wall");
            if (!SmartMobileCamera.RaidCam.FacingRecenterEnabled)
                failures.Add("the raid facing-recenter is switched OFF - the seat would stay "
                    + "world-locked behind a wall through a turn, which is the WO-385 defect");
            // Town band is [-10, 35] (the _panPitchMin/_panPitchMax initialisers in
            // SmartMobileCamera.cs). "Slightly lower pitch" = a lower ceiling: positive pitch
            // RAISES the seat, so 35 lets the player crane back into a near top-down view.
            if (SmartMobileCamera.RaidCam.PanPitchMax >= 35f)
                failures.Add("the raid pitch ceiling is not lower than the town 35 degrees, so the "
                    + "player can still crane the raid seat up into a top-down view");
            if (SmartMobileCamera.RaidCam.PanPitchMin > SmartMobileCamera.RaidCam.PanPitchMax)
                failures.Add("the raid pitch band is inverted");
        }

        // ── (d) THE EVIDENCE GATE ────────────────────────────────────────────────────────────
        //
        // THE DEFECT WO-1765 OPENED WITH: the only trace that prints yaw was gated on the DUNGEON
        // profile, and RaidBase* is a different scene kind - so a 32.8 MB device logcat of a raid
        // session held ZERO camera yaw evidence while the owner was reporting that the camera
        // rotates. This case is what stops a raid going silent again.
        private static void CheckYawEvidenceGate(List<string> failures)
        {
            if (!SmartMobileCamera.ShouldEmitYawEvidence("RaidBase_IronBastion"))
                failures.Add("a RAID emits no camera yaw evidence - that silence IS the defect "
                    + "WO-1765 was opened on; a raid capture would again contain no [Flow:Camera] "
                    + "heartbeat and the next yaw ticket would start from zero data");
            if (!SmartMobileCamera.ShouldEmitYawEvidence("Dungeon_HealersCottage"))
                failures.Add("a DUNGEON no longer emits camera yaw evidence - WO-958's heartbeat was "
                    + "weakened while extending it to raids");
            // Hub resolved, never typed, and BOTH ff.MergedWorld candidates checked - the claim is a
            // NEGATIVE that must hold for either hub (hub-scene-literal; see CheckTownIsUntouched).
            foreach (string hub in DeNelle.Core.SceneRouter.CastleCandidates)
                if (SmartMobileCamera.ShouldEmitYawEvidence(hub))
                    failures.Add("the home hub '" + hub + "' now emits the per-second camera heartbeat - "
                        + "that is a frame-path firehose in the scene the player spends the most time in, "
                        + "and it evicts the boot window out of the device logcat ring "
                        + "(memory logcat-ring-buffer-destroys-evidence)");
            if (SmartMobileCamera.ShouldEmitYawEvidence(null)
                || SmartMobileCamera.ShouldEmitYawEvidence(string.Empty))
                failures.Add("an empty scene name resolved to 'emit yaw evidence'");

            if (!SmartMobileCamera.ResolvesToRaidCameraProfile("RaidBase_IronBastion"))
                failures.Add("RaidBase_IronBastion does not resolve to the raid camera profile, so "
                    + "the over-the-shoulder seat never applies in the scene it was written for");
            foreach (string hub in DeNelle.Core.SceneRouter.CastleCandidates)
                if (SmartMobileCamera.ResolvesToRaidCameraProfile(hub))
                    failures.Add("the home hub '" + hub + "' resolves to the raid camera profile - the hub "
                        + "would ship a ~3.3 m over-the-shoulder seat with framing and the movement lead "
                        + "switched off");
            if (SmartMobileCamera.ResolvesToRaidCameraProfile("Dungeon_HealersCottage"))
                failures.Add("a dungeon resolves to the RAID profile, so the WO-920 locked dungeon "
                    + "seat is dead");
        }

        // ── (d2) WO-1770 — THE OPEN-AIR RAID TARGETS GET THE SAME SEAT ───────────────────────
        //
        // THE DEFECT WO-1770 OPENED WITH: WO-1765's three predicates all read HubScenes.IsRaid, which
        // matches `RaidBase*` ONLY. `Garrison_*` and `Outpost1-2` are open-air assault targets baked
        // outside the raid pipeline (HubScenes.cs:141-152), so every one of them kept the TOWN seat and
        // the 220 deg/s recenter whip - the exact felt defect 1765 fixed, surviving in a scene family
        // its gate never reached. Five Garrison_* scenes plus Outpost1/Outpost2 are in the build list.
        //
        // These are POSITIVE pins only. The ticket's "what NOT to touch" forbids widening the negative
        // scope pins in CheckTownIsUntouched / CheckYawEvidenceGate, and nothing here does: no hub, no
        // Village2 and no Dungeon_* name is added to any set. The one negative below is the opposite of
        // a widening - it holds the dungeon that merely ENDS in "Outpost" out of the new term.
        private static void CheckOpenAirRaidTargetsGetTheRaidSeat(List<string> failures)
        {
            // One representative per family. Named literally because these are not hub scenes (the
            // hub-scene-literal lint's subject) and no const owns them: GarrisonRecipe derives the
            // name from authored data at build time (GarrisonRecipe.cs:116), so there is nothing to
            // resolve from - but each name below is in ProjectSettings/EditorBuildSettings.asset.
            string[] openAir = { "Garrison_troll_outpost", "Garrison_frost_keep", "Garrison_hill_fort",
                                 "Garrison_ruined_keep", "Garrison_village2_stronghold",
                                 "Outpost1", "Outpost2" };

            foreach (string scene in openAir)
            {
                if (!SmartMobileCamera.ResolvesToOpenAirRaidTarget(scene))
                    failures.Add("'" + scene + "' is not recognised as an open-air raid target, so the "
                        + "WO-1770 term does not reach it and it keeps the town seat");
                if (!SmartMobileCamera.ResolvesToRaidCameraProfile(scene))
                    failures.Add("'" + scene + "' does not resolve to the raid camera profile - an "
                        + "open-air assault would ship the ~4.5 m town seat and the 220 deg/s recenter "
                        + "whip, which is the WO-1770 defect verbatim");
                if (!SmartMobileCamera.AppliesRaidScanNarrowing(scene))
                    failures.Add("'" + scene + "' takes the raid seat but not the narrowed scan mask / "
                        + "structure filter, so its walls would still be framing subjects");
                if (!SmartMobileCamera.ShouldEmitYawEvidence(scene))
                    failures.Add("'" + scene + "' takes the raid seat with NO [Flow:Camera] yaw "
                        + "heartbeat - CLAUDE.md §12: the next yaw ticket in this scene family would "
                        + "start from zero data, which is the silence WO-1765 was opened on");

                // Same agreement invariant CheckTownIsUntouched asserts for the raid/town/dungeon set:
                // the seat and the narrowing must be the SAME set of scenes or the mask moves in a
                // scene that never took the seat.
                if (SmartMobileCamera.AppliesRaidScanNarrowing(scene)
                    != SmartMobileCamera.ResolvesToRaidCameraProfile(scene))
                    failures.Add("for scene '" + scene + "' the scan-narrowing scope and the raid "
                        + "camera profile disagree - the two must be the same set");
            }

            // KayKitChallengeOutpost is a DUNGEON that happens to end in "Outpost" (HubScenes.cs:174-177
            // orders Classify for exactly this reason). It must keep the WO-920 locked dungeon seat.
            // Resolved from the const, never typed.
            if (SmartMobileCamera.ResolvesToOpenAirRaidTarget(DeNelle.Core.HubScenes.OutpostSceneName)
                || SmartMobileCamera.ResolvesToRaidCameraProfile(DeNelle.Core.HubScenes.OutpostSceneName))
                failures.Add("'" + DeNelle.Core.HubScenes.OutpostSceneName + "' is a DUNGEON but now "
                    + "resolves to the open-air raid seat - the WO-920 locked dungeon camera is dead in "
                    + "the starter outpost");

            if (SmartMobileCamera.ResolvesToOpenAirRaidTarget(null)
                || SmartMobileCamera.ResolvesToOpenAirRaidTarget(string.Empty))
                failures.Add("an empty scene name resolved to 'open-air raid target'");
        }

        // ── (e) §12: THE YAW INSTRUMENT IS PERMANENT ─────────────────────────────────────────
        //
        // The ONE place a source-text pin is the right tool: "does this trace still exist" is
        // literally a string question. CLAUDE.md §12 - instrumentation is never stripped, only
        // ever flagged off. Without these, the next seat can delete the only measurement of the
        // owner's actual word and the following yaw ticket starts from theory again.
        /// <summary>The camera's source text, or empty. One reader, so the path is written once.</summary>
        private static string ReadCameraSource()
        {
            string path = Path.Combine("Assets", "_Modules", "Village", "Hero", "SmartMobileCamera.cs");
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static void CheckYawInstrumentPermanence(List<string> failures)
        {
            string source = ReadCameraSource();
            if (source.Length == 0)
            {
                failures.Add("SmartMobileCamera.cs could not be read - the yaw instrument was not checked");
                return;
            }

            if (!source.Contains("viewYawRate="))
                failures.Add("the viewYawRate measurement was stripped from the camera heartbeat "
                    + "(CLAUDE.md sec.12 - instrumentation is permanent). It is the ONLY measurement "
                    + "of the owner's own word ('rotating'); panYaw cannot substitute, because the "
                    + "framing scan, the movement lead and the pull-in all rotate the view with "
                    + "panYaw held still");
            if (!source.Contains("YAW SPIKE"))
                failures.Add("the yaw spike edge Warn was stripped (CLAUDE.md sec.12) - a busy device "
                    + "log then has no line that survives to name the worst rotation moment");
            if (!source.Contains("framingTarget='"))
                failures.Add("the framingTarget field was stripped from the camera heartbeat - naming "
                    + "a Wall_* there is the C1 verdict in one word, and nothing else in a capture "
                    + "distinguishes 'framed a wall' from 'framed a mob'");
            if (!source.Contains("structsRejected="))
                failures.Add("the structsRejected counter was stripped - with framing switched off in "
                    + "the raid profile it is the ONLY way a capture shows the C1 admission rule "
                    + "actually ran");
            // The sibling 1765 ticket's §6 field list. `step=` is the one that makes the spin a
            // NUMBER: steps summing past ~90 deg with the player's thumb off the screen IS the
            // possessed rotation, measured, and `yawSrc=input` throughout REFUTES the recenter and
            // re-points the ticket at the pull-in. Falsifiable both ways — the point of §12.
            if (!source.Contains("step=") || !source.Contains("recenterSpeed="))
                failures.Add("the recenter STEP (degrees actually applied) and/or recenterSpeed were "
                    + "stripped from the camera trace - a capture can then say the camera rotated but "
                    + "not by how much, nor whether the recenter or the player did it");
            if (!source.Contains("heroYaw="))
                failures.Add("heroYaw was stripped - the recenter chases the hero's FACING, so without "
                    + "it a recenter step cannot be checked against the angle it was closing");
            if (!source.Contains("_lastRecenterStep"))
                failures.Add("the recenter step is no longer recorded at the point it is applied");
            if (!source.Contains("leadLateral="))
                failures.Add("the leadLateral lever arm was stripped - without it a yaw rate cannot "
                    + "be attributed to a look-at offset rather than to the seat");
            if (!source.Contains("pullingIn="))
                failures.Add("the pull-in verdict was stripped from the heartbeat - separating "
                    + "rotation from pull-in is the whole question WO-1765 asks");
            // The frame path must stay on the accumulating 4-arg Measure overload (CLAUDE.md §12).
            if (!source.Contains("\"Perf\", \"SmartMobileCamera.LateUpdate\", 4f, 1f"))
                failures.Add("the 4-arg frame-budget Measure scope on LateUpdate was removed or "
                    + "changed shape - the added yaw work would then be unmeasurable, and a 3-arg "
                    + "Measure on a frame path floods the log");
        }

        // ── doubles ──────────────────────────────────────────────────────────────────────────
        //
        // Plain C# objects, no GameObject, no Component: IsFramingSubject takes an INTERFACE
        // precisely so the rule can be driven without standing up a camera or a scene.

        /// <summary>The EnemyDamageable / DragonBoss shape: IDamageable and NOT IDamageableStructure.</summary>
        private sealed class MobileHostileDouble : IDamageable
        {
            public bool Alive = true;
            public CombatFaction Side = CombatFaction.Hostile;

            public CombatFaction Faction => Side;
            public Vector3 WorldPosition => Vector3.zero;
            public float Hp => Alive ? 100f : 0f;
            public bool IsAlive => Alive;
            public void TakeDamage(float amount, DamageElement element) { }
            public void ApplyStatus(StatusEffect effect, float seconds) { }
        }

        /// <summary>
        /// The WallSegment / Gate / DefenseTower / RaidSpire shape: BOTH interfaces on ONE class,
        /// exactly as those four declare them (WallSegment.cs:58). Hostile and alive, because that
        /// is what a standing panel in an enemy-owned raid base reports.
        /// </summary>
        private sealed class HostileStructureDouble : IDamageable, IDamageableStructure
        {
            public CombatFaction Faction => CombatFaction.Hostile;
            public Vector3 WorldPosition => Vector3.zero;
            public float Hp => 100f;
            public bool IsAlive => true;
            public void TakeDamage(float amount, DamageElement element) { }
            public void ApplyStatus(StatusEffect effect, float seconds) { }
            public void ApplyContactDamage(float amount) { }
        }

        private static void RequireLayerInMask(List<string> failures, int mask, string layerName, string why)
        {
            int idx = LayerMask.NameToLayer(layerName);
            if (idx < 0)
            {
                failures.Add("project layer '" + layerName + "' does not exist - the camera scan mask "
                    + "cannot contain it (" + why + ")");
                return;
            }
            if ((mask & (1 << idx)) == 0)
                failures.Add("camera enemy-scan mask omits layer " + idx + ":" + layerName + " - " + why);
        }

        private static void RequireLayerNotInMask(List<string> failures, int mask, string layerName, string why)
        {
            int idx = LayerMask.NameToLayer(layerName);
            if (idx < 0) return;   // a layer the project does not declare cannot be in the mask
            if ((mask & (1 << idx)) != 0)
                failures.Add("camera enemy-scan mask INCLUDES layer " + idx + ":" + layerName
                    + " - " + why);
        }
    }
}
