// =============================================================================
// DungeonFpvRegression [dungeon-fpv] — locks the DUNGEON CAMERA contract.
// -----------------------------------------------------------------------------
// RENAMED IN INTENT 2026-08-07 (WO-920), same suite id. It used to lock "FPV is the
// default". WO-920 REVERSED that default, so what it locks now is:
//   * the LOCKED OVER-THE-SHOULDER explore camera is the default, on BOTH pipelines;
//   * FPV is still fully WIRED, just opt-in — the free-look was not deleted;
//   * the shared seat still fits under the WO-919 ceiling and inside a room.
//
// ⚠ THE THING THIS SUITE GOT WRONG BEFORE, AND NOW COVERS. It only ever read the
// DeNelle.Dungeons rig. Verified at source 2026-08-07, that rig exists in exactly TWO
// scenes (Dungeon_HealersCottage, Dungeon_FolksGranary). The COMPOSED dungeons
// (Assets/Scenes/DungeonCompose/dg_*.unity) and KayKitChallengeOutpost bake NO camera and
// NO rig — their camera is the runtime "GameplayCamera (ensured)" + SmartMobileCamera from
// HeroControlEnsurer L283-295. So a suite that read only DungeonCameraRig would have gone
// green while every dungeon the owner actually plays kept the old bouncing village camera
// and a #314D79 blue clear. Cases (6)-(8) close that hole.
//
// The five original wiring seams (1)-(5) are kept, with (1) inverted:
//   1. ff.dungeonfpv DEFAULTS OFF, and the rig's ResolveMode falls through to OverShoulder.
//   2. DungeonCameraRig still CARRIES the FPV look-accumulator (yaw+pitch) with a pitch
//      clamp, the body-hide (ShadowsOnly), and SetCombatFraming(bool) — opt-in, not deleted.
//   3. DungeonHero feeds the shared VirtualJoystick into movement AND gates tap-to-move OFF
//      while FPV is active (a tap is "look", not "walk").
//   4. DungeonController still wires SetCombatFraming on battle STAGED / ENDED.
//   5. BattleArena raises OnBattleStaged (fired in BeginEncounter) — the start signal.
// And the WO-920 additions:
//   6. The cross-assembly SEAT MIRROR is honest and the seat physically fits.
//   7. Pipeline B (SmartMobileCamera) actually applies a dungeon profile that kills the
//      four bounce sources.
//   8. The runtime camera clears to the dungeon colour, scoped to dungeon scenes.
//
// FEEL (motion sickness / control) is OWNER felt-verify — this only guards wiring + numbers.
// Source-lint (edit-mode, no PlayMode). Wired into DataRegression.RunAll. Never throws.
// =============================================================================
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class DungeonFpvRegression
    {
        public static bool Run(out string reason)
        {
            var fails = new List<string>();

            string flags   = ReadOrFail("_Modules/Core/FeatureFlags.cs", fails);
            string hubs    = ReadOrFail("_Modules/Core/HubScenes.cs", fails);
            string profile = ReadOrFail("_Modules/Core/World/DungeonCameraProfile.cs", fails);
            string rig     = ReadOrFail("_Modules/Dungeons/DungeonCameraRig.cs", fails);
            string canon   = ReadOrFail("_Modules/Dungeons/RoomForge/RoomForgeCanon.cs", fails);
            string hero    = ReadOrFail("_Modules/Dungeons/DungeonHero.cs", fails);
            string ctrl    = ReadOrFail("_Modules/Dungeons/DungeonController.cs", fails);
            string arena   = ReadOrFail("_Modules/Village/Arena/BattleArena.cs", fails);
            string smc     = ReadOrFail("_Modules/Village/Hero/SmartMobileCamera.cs", fails);
            string ensurer = ReadOrFail("_Modules/Village/Hero/HeroControlEnsurer.cs", fails);
            if (fails.Count > 0) { return Verdict(fails, out reason); }

            // ── (1) ff.dungeonfpv now defaults OFF; OTS is the fall-through default ──
            if (!Regex.IsMatch(flags, @"DungeonFpv\s*=>\s*Get\(""dungeonfpv"",\s*defaultOn:\s*false\)"))
                fails.Add("WO-920 REVERSED the FPV default: FeatureFlags.DungeonFpv must be " +
                          "Get(\"dungeonfpv\", defaultOn: false) so the locked over-the-shoulder rig ships");
            if (!Regex.IsMatch(flags, @"DungeonCameraIso\s*=>\s*Get\(""dungeoniso"",\s*defaultOn:\s*false\)"))
                fails.Add("FeatureFlags.DungeonCameraIso must stay defaultOn:false (WO-920 §3 Phase D)");
            // ResolveMode must FALL THROUGH to OverShoulder once both opt-in flags are off
            // (written as the final arm of the fpv?:iso?:ots ternary).
            if (!Regex.IsMatch(rig, @"ResolveMode[\s\S]{0,600}:\s*CamMode\.OverShoulder"))
                fails.Add("DungeonCameraRig.ResolveMode no longer falls through to CamMode.OverShoulder " +
                          "when both opt-in flags are off");

            // ── (2) FPV is OPT-IN, not deleted — every seam must still be present ──
            if (!rig.Contains("_lookYaw") || !rig.Contains("_lookPitch"))
                fails.Add("DungeonCameraRig lost the FPV look accumulator (_lookYaw/_lookPitch) — " +
                          "WO-920 makes FPV opt-in, it does NOT remove it");
            if (!Regex.IsMatch(rig, @"Mathf\.Clamp\(\s*_lookPitch") || !rig.Contains("_fpvPitchClamp"))
                fails.Add("DungeonCameraRig does not pitch-clamp the FPV look (_fpvPitchClamp)");
            if (!rig.Contains("SampleLookDelta"))
                fails.Add("DungeonCameraRig has no independent look sampler (SampleLookDelta — right-half drag / mouse delta)");
            if (!Regex.IsMatch(rig, @"void\s+LateUpdate"))
                fails.Add("DungeonCameraRig drives no LateUpdate look layer (heading-decoupled free-look)");
            if (!rig.Contains("HideHeroBody") || !rig.Contains("ShadowsOnly"))
                fails.Add("DungeonCameraRig does not hide the hero body on FPV bind (HideHeroBody + ShadowsOnly)");
            if (!Regex.IsMatch(rig, @"public\s+void\s+SetCombatFraming\s*\(\s*bool"))
                fails.Add("DungeonCameraRig has no public SetCombatFraming(bool) combat-framing override");
            // The look layer must stay gated on _fpvActive, or the default OTS would free-look.
            // (Written without a literal open-brace on purpose — CLAUDE.md §1's brace gate counts
            // braces inside string literals and comments too, so a single escaped open-brace in a
            // regex here would fail the gate with no real imbalance. Match the guard variable instead.)
            if (!Regex.IsMatch(rig, @"void\s+LateUpdate[\s\S]{0,200}!_fpvActive"))
                fails.Add("DungeonCameraRig.LateUpdate no longer early-outs on !_fpvActive — the " +
                          "default over-the-shoulder mode would free-look from mouse/drag noise");

            // ── (2b) WO-920 locked-OTS behaviour on the rig ──
            if (!Regex.IsMatch(rig, @"_otsAvoidObstacles\s*=\s*false"))
                fails.Add("DungeonCameraRig._otsAvoidObstacles must default FALSE (WO-920 §3 Phase A.3 — " +
                          "wall pull-in/out in a tight room is the bounce)");
            // Policy B1: no combat reframe when the traversal mode is already the locked OTS seat.
            if (!Regex.IsMatch(rig, @"SetCombatFraming[\s\S]{0,900}_mode\s*==\s*CamMode\.OverShoulder[\s\S]{0,800}return\s*;"))
                fails.Add("DungeonCameraRig.SetCombatFraming does not no-op when the mode is already " +
                          "OverShoulder (WO-920 policy B1 — otherwise every fight re-seats the rig twice)");
            // The Bind seat must come from the shared profile, never re-typed literals.
            if (!rig.Contains("DungeonCameraProfile.CameraHeight")
                || !rig.Contains("DungeonCameraProfile.VerticalArmLength")
                || !rig.Contains("DungeonCameraProfile.CameraDistance"))
                fails.Add("DungeonCameraRig.Bind no longer sources its seat from DungeonCameraProfile — " +
                          "a re-typed seat is how the two dungeon camera pipelines drift apart");

            // ── (3) DungeonHero — joystick + tap-to-move FPV gate ──
            if (!hero.Contains("SampleJoystickMove") || !hero.Contains("VirtualJoystick.Move"))
                fails.Add("DungeonHero does not feed the shared VirtualJoystick into movement (SampleJoystickMove)");
            if (!Regex.IsMatch(hero, @"if\s*\(\s*!FeatureFlags\.DungeonFpv\s*\)\s*\n\s*TrySampleTap"))
                fails.Add("DungeonHero does not gate tap-to-move OFF in FPV (expected 'if (!FeatureFlags.DungeonFpv) TrySampleTap()')");

            // ── (4) DungeonController — combat-camera switch still wired (used when FPV/iso opted in) ──
            if (!ctrl.Contains("OnBattleStaged += OnRealtimeBattleStaged"))
                fails.Add("DungeonController never subscribes BattleArena.OnBattleStaged (no combat-camera switch on fight start)");
            if (!ctrl.Contains("OnBattleStaged -= OnRealtimeBattleStaged"))
                fails.Add("DungeonController never unsubscribes BattleArena.OnBattleStaged (event leak)");
            if (!Regex.IsMatch(ctrl, @"OnRealtimeBattleStaged[\s\S]{0,300}SetCombatFraming\(true\)"))
                fails.Add("DungeonController does not SetCombatFraming(true) on battle staged");
            if (!Regex.IsMatch(ctrl, @"OnRealtimeBattleEnded[\s\S]{0,300}SetCombatFraming\(false\)"))
                fails.Add("DungeonController does not SetCombatFraming(false) on battle ended");

            // ── (5) BattleArena — the battle-STARTED signal ──
            if (!Regex.IsMatch(arena, @"event\s+Action<EncounterParams>\s+OnBattleStaged"))
                fails.Add("BattleArena has no OnBattleStaged event (the battle-started signal)");
            if (!arena.Contains("OnBattleStaged?.Invoke("))
                fails.Add("BattleArena never fires OnBattleStaged in BeginEncounter");

            // ── (6) THE CROSS-ASSEMBLY SEAT MIRROR, and does the seat physically fit ──
            // RoomForgeCanon lives in DeNelle.Dungeons; DeNelle.Core and DeNelle.Village cannot
            // reference it, so DungeonCameraProfile MIRRORS its two load-bearing numbers with a
            // citation. RoomForgeCanon's own header (L13-17) warns that a copied oracle is not an
            // oracle — this case is what keeps the copy honest, by reading BOTH files as text.
            float wallHeight     = ConstFloat(canon,   @"const\s+float\s+WallHeight", fails, "RoomForgeCanon.WallHeight");
            float cell           = ConstFloat(canon,   @"const\s+float\s+Cell",       fails, "RoomForgeCanon.Cell");
            float ceilingRef     = ConstFloat(profile, @"const\s+float\s+CeilingHeightRef", fails, "DungeonCameraProfile.CeilingHeightRef");
            float cellRef        = ConstFloat(profile, @"const\s+float\s+CellSizeRef",      fails, "DungeonCameraProfile.CellSizeRef");
            float camHeight      = ConstFloat(profile, @"const\s+float\s+CameraHeight",     fails, "DungeonCameraProfile.CameraHeight");
            float camDistance    = ConstFloat(profile, @"const\s+float\s+CameraDistance",   fails, "DungeonCameraProfile.CameraDistance");
            float lookAtHeight   = ConstFloat(profile, @"const\s+float\s+LookAtHeight",     fails, "DungeonCameraProfile.LookAtHeight");
            float verticalArm    = ConstFloat(profile, @"const\s+float\s+VerticalArmLength", fails, "DungeonCameraProfile.VerticalArmLength");

            if (!Mathf.Approximately(ceilingRef, wallHeight))
                fails.Add($"MIRROR DRIFT: DungeonCameraProfile.CeilingHeightRef={ceilingRef} but " +
                          $"RoomForgeCanon.WallHeight={wallHeight}. Core cannot reference DeNelle.Dungeons, " +
                          "so the mirror must be updated by hand when the canon changes");
            if (!Mathf.Approximately(cellRef, cell))
                fails.Add($"MIRROR DRIFT: DungeonCameraProfile.CellSizeRef={cellRef} but RoomForgeCanon.Cell={cell}");
            // The real acceptance criterion: the camera must sit UNDER the WO-919 ceiling slab.
            if (camHeight + verticalArm >= wallHeight)
                fails.Add($"dungeon camera would clip the ceiling: CameraHeight {camHeight} + " +
                          $"VerticalArmLength {verticalArm} = {camHeight + verticalArm} >= WallHeight {wallHeight}");
            // Looking UP means framing the ceiling instead of the corridor floor.
            if (lookAtHeight >= camHeight)
                fails.Add($"dungeon camera would tilt UP into the ceiling: LookAtHeight {lookAtHeight} " +
                          $">= CameraHeight {camHeight}");
            // A seat deeper than half a room is inside the wall behind the hero for most of a corridor.
            if (camDistance >= cell * 0.5f)
                fails.Add($"dungeon camera seat is too deep for a room: CameraDistance {camDistance} " +
                          $">= half of RoomForgeCanon.Cell ({cell * 0.5f})");

            // ── (7) PIPELINE B — SmartMobileCamera's locked dungeon profile ──
            // This is the camera in every composed dg_* dungeon and in KayKitChallengeOutpost.
            // ⛔ WO-1772: THIS PIN TESTS THE SEAM, NOT THE SPELLING. It used to require the literal
            // `ApplyDungeonProfileIfNeeded`, which made the lint block its own subject's rename —
            // WO-1765 R1 (`ApplyDungeonProfileIfNeeded` → `ApplySceneCameraProfileIfNeeded`) was
            // abandoned for exactly that reason. A lint that pins an identifier's characters is
            // duplicated state (CLAUDE.md §5); what this suite actually cares about is that a
            // per-scene camera-profile applier EXISTS and is keyed off HubScenes.IsDungeon. So the
            // applier is resolved from its DECLARATION and the IsDungeon check is anchored to that
            // declaration's body — any name may be chosen, no behaviour may be dropped.
            var applier = Regex.Match(smc, @"private\s+void\s+(Apply\w*ProfileIfNeeded)\s*\(");
            if (!applier.Success)
                fails.Add("SmartMobileCamera has no `private void Apply*ProfileIfNeeded(` applier — the " +
                          "composed dungeons and KayKitChallengeOutpost bake NO camera and NO " +
                          "DungeonCameraRig, so this component IS their dungeon camera " +
                          "(HeroControlEnsurer L283-295)");
            else if (!Regex.IsMatch(smc.Substring(applier.Index),
                                    @"^[\s\S]{0,4000}HubScenes\.IsDungeon"))
                fails.Add($"SmartMobileCamera's {applier.Groups[1].Value} body is not keyed off " +
                          "HubScenes.IsDungeon — the dungeon profile must never alter the " +
                          "overworld/hub/raid camera");
            if (!Regex.IsMatch(smc, @"_followOffset\s*=\s*new\s+Vector3\([\s\S]{0,200}DungeonCameraProfile\.CameraHeight"))
                fails.Add("SmartMobileCamera's dungeon seat is not sourced from DungeonCameraProfile.CameraHeight");
            if (!smc.Contains("DungeonCameraProfile.CameraDistance") || !smc.Contains("DungeonCameraProfile.LookAtHeight"))
                fails.Add("SmartMobileCamera's dungeon seat does not use DungeonCameraProfile.CameraDistance/LookAtHeight");
            // The four bounce sources must all be switched off in the dungeon branch. Matched by
            // regex, not literal text, so re-aligning the assignment block cannot fail the suite.
            foreach (var off in new[] { @"_collisionEnabled\s*=\s*false", @"_framingEnabled\s*=\s*false",
                                        @"_combatZoomOut\s*=\s*0f", @"_combatFovBoost\s*=\s*0f",
                                        @"_leadDistance\s*=\s*0f" })
                if (!Regex.IsMatch(smc, off))
                    fails.Add($"SmartMobileCamera's dungeon profile does not set '{off}' — WO-920 " +
                              "requires the wall-collision thrash, framing yank, combat pump and " +
                              "movement-lead sway all OFF underground");
            // Leaving the dungeon must restore the village camera, or the town goes dark and tight.
            // ⛔ WO-1772: reversibility is a BEHAVIOUR, so pin the behaviour. This used to require the
            // literal `_dungeonProfileActive`, one of the two identifiers WO-1765 R1 could not rename.
            // The rename-proof anchor is the ENUM: whatever the flag is called, it must be derived from
            // CameraSceneProfile.Dungeon, and the town seat must be assigned back from its snapshot.
            string dgFlag = DungeonActiveFlagName(smc);
            if (dgFlag == null)
                fails.Add("SmartMobileCamera has no dungeon-active flag derived from " +
                          "CameraSceneProfile.Dungeon (expected `<flag> = <want> == CameraSceneProfile.Dungeon;`) — " +
                          "without it nothing tells the room-topology and heartbeat gates they are underground");
            if (!Regex.IsMatch(smc, @"_followOffset\s*=\s*_villageFollowOffset\s*;"))
                fails.Add("SmartMobileCamera's dungeon profile is not reversible (the town seat is never " +
                          "assigned back from _villageFollowOffset) — a camera surviving back into town " +
                          "would keep the dungeon seat");

            // ── (8) The runtime camera must CLEAR to the dungeon colour ──
            // WO-919 nulled RenderSettings.skybox; with a null skybox CameraClearFlags.Skybox falls
            // back to backgroundColor, whose Unity default is #314D79 BLUE. The runtime camera set
            // neither field, so enclosed dungeons still cleared to daylight blue.
            if (!Regex.IsMatch(ensurer, @"HubScenes\.IsDungeon[\s\S]{0,600}clearFlags\s*=\s*CameraClearFlags\.SolidColor"))
                fails.Add("HeroControlEnsurer does not set clearFlags=SolidColor on the runtime dungeon " +
                          "camera — with RenderSettings.skybox null (WO-919), Skybox clear falls back to " +
                          "backgroundColor's #314D79 BLUE default");
            if (!ensurer.Contains("DungeonCameraProfile.ClearColor"))
                fails.Add("HeroControlEnsurer does not use DungeonCameraProfile.ClearColor for the dungeon " +
                          "camera background (a re-typed hex is how this drifts from DungeonSceneBuilder L2067)");
            if (!Regex.IsMatch(profile, @"ClearColor\s*=\s*\(Color\)new\s+Color32\(\s*0x07,\s*0x07,\s*0x09"))
                fails.Add("DungeonCameraProfile.ClearColor is no longer #070709 — the value proven by the " +
                          "hand-built dungeon (DungeonSceneBuilder L2067). Change it deliberately or not at all");

            // ── (8b) HubScenes.IsDungeon must still cover all three naming families ──
            if (!Regex.IsMatch(hubs, @"public\s+static\s+bool\s+IsDungeon"))
                fails.Add("HubScenes.IsDungeon is gone — it is the single runtime dungeon-scene authority");
            foreach (var family in new[] { "\"dg_\"", "\"Dungeon\"", "KayKitChallengeOutpost" })
                if (!hubs.Contains(family))
                    fails.Add($"HubScenes.IsDungeon no longer covers the {family} scene family — composed " +
                              "dungeons (dg_*), hand-built (Dungeon*) and the outpost must ALL match");

            return Verdict(fails, out reason);
        }

        /// <summary>
        /// Reads a <c>const float NAME = &lt;value&gt;f;</c> out of a source file. This is how the
        /// cross-assembly mirror stays honest: DeNelle.Core cannot reference DeNelle.Dungeons, so the
        /// only way to compare the two numbers is to read both files as text. Returns NaN and records
        /// a fail if the declaration cannot be found (a rename must not silently pass).
        /// </summary>
        private static float ConstFloat(string source, string declPattern, List<string> fails, string label)
        {
            var m = Regex.Match(source, declPattern + @"\s*=\s*(-?[0-9]*\.?[0-9]+)\s*f?\s*;");
            if (!m.Success)
            {
                fails.Add($"could not read {label} — re-point this oracle (pattern: {declPattern})");
                return float.NaN;
            }
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// WO-1772 — resolves the name of SmartMobileCamera's dungeon-active flag from the ONE thing a
        /// rename cannot move: the <c>CameraSceneProfile.Dungeon</c> enum member it is derived from.
        /// <para>
        /// ⛔ THIS EXISTS SO NO LINT EVER AGAIN PINS AN IDENTIFIER'S CHARACTERS. Two suites required the
        /// literal <c>_dungeonProfileActive</c> / <c>ApplyDungeonProfileIfNeeded</c>, which is why
        /// WO-1765 R1 shipped without its rename and why the heartbeat call site was written as two
        /// identical branches instead of one <c>||</c> — the lint pinned the very dungeon-only gate the
        /// ruling widened. A pinned spelling is duplicated state (CLAUDE.md §5): it tracks live code by
        /// hand and fails the same way the stale WO-number block did.
        /// </para>
        /// Returns <c>null</c> when the derivation is gone, so the caller fails LOUDLY — a resolver that
        /// silently returned a default would make every gate below it vacuous (CLAUDE.md §12: no silent
        /// failures). ONE copy, deliberately: DungeonCameraTightRoomRegression lints the same source file
        /// and CALLS this — a second private regex in that suite would be the duplicate §5 forbids.
        /// </summary>
        internal static string DungeonActiveFlagName(string smcSource)
        {
            var m = Regex.Match(smcSource, @"(\w+)\s*=\s*\w+\s*==\s*CameraSceneProfile\.Dungeon\s*;");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string ReadOrFail(string rel, List<string> fails)
        {
            string p = Path.Combine(Application.dataPath, rel);
            if (!File.Exists(p)) { fails.Add("source not found: " + rel + " — re-point this oracle"); return string.Empty; }
            return File.ReadAllText(p);
        }

        private static bool Verdict(List<string> fails, out string reason)
        {
            if (fails.Count == 0)
            {
                Debug.Log("DUNGEON_FPV_OK");
                reason = "DUNGEON CAMERA OK — ff.dungeonfpv default OFF (locked OTS ships, FPV opt-in and " +
                         "still fully wired), rig seat + AvoidObstacles-off + B1 no-op combat framing, " +
                         "seat mirror honest vs RoomForgeCanon and fits under the ceiling, SmartMobileCamera " +
                         "dungeon profile kills all four bounce sources and is reversible, runtime camera " +
                         "clears to #070709 scoped by HubScenes.IsDungeon (FEEL = owner felt-verify)";
                return true;
            }
            reason = "dungeon-fpv: " + string.Join("; ", fails);
            Debug.LogError("DUNGEON_FPV_FAIL: " + reason);
            return false;
        }
    }
}
