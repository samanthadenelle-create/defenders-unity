// =============================================================================
// WallBreachOrderRegression [wall-breach-order] — WO-1719.
// -----------------------------------------------------------------------------
// Pins the owner's 2026-09-14 breach ruling, verbatim: "tap the wall segment
// directly, and it overrides" / "the most damanged [stays the fallback]" /
// "all together unless they have aggro".
//
// WHAT IS ACTUALLY PROVEN HERE, and why in this shape:
//   1. FALLBACK INTACT  — with no explicit pick the rule is byte-for-byte the old
//      most-damaged / nearest-muster one. This is the case that would catch an
//      "override" implemented by restructuring the selection body.
//   2. OVERRIDE WINS    — a FULL-HP, FAR wall the player tapped beats a badly
//      damaged near one. That is the exact pairing WO-1717 sec.3d says defeats the
//      player today ("the moment ANY wall has taken >0.5 more damage ... that wall
//      wins permanently").
//   3. DEAD PICK FALLS BACK — the ordered panel collapsing returns the warband to
//      the automatic rule, which is this ticket's chosen "what clears an order".
//   4. ALL TOGETHER, EXCEPT AGGRO — a fixture of four Breach-phase troops and one
//      peelThreat troop: all four resolve bucket 2 == the ordered segment; the
//      aggro'd one resolves bucket 0 == its own foe, untouched.
//   5. THE RALLY MARCH RELEASES for an explicit order only (phase and rally are
//      independent axes - without this, case 4 fails for the whole warband any time
//      a rally is set).
//   6. WIRED, NOT JUST PURE — source-text proof that TroopController.SharedBreachFocus
//      and RaidDeployController actually consult TroopBreachOrder. Same pattern as
//      RaidAssaultAiRegression.Case_AllowNonObjectiveWiredIntoPickBucket: a pure rule
//      that nothing calls passes every case and ships nothing.
//
// RED PROOF: delete the `if (explicitFocus != null && explicitFocus.IsAlive) return
// explicitFocus;` early return in RaidAssaultAi.SelectFocusBreach and cases 2 and 4
// fail. Delete the TroopBreachOrder read in TroopController.SharedBreachFocus and
// case 6 fails while 1-5 still pass - which is precisely the failure mode case 6
// exists to catch.
//
// Marker: WALL_BREACH_ORDER_OK / WALL_BREACH_ORDER_FAIL.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core.Combat;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class WallBreachOrderRegression
    {
        private const string Tag = "[wall-breach-order]";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== WallBreachOrderRegression (WO-1719) ===\n");
            try
            {
                Case_NoOrder_AutoMostDamagedStillWins(failures, log);
                Case_ExplicitOrder_OverridesMostDamaged(failures, log);
                Case_CollapsedOrder_FallsBackToAuto(failures, log);
                Case_WarbandRetargetsTogether_AggroTroopKeepsItsFight(failures, log);
                Case_RallyMarch_ReleasedByExplicitOrderOnly(failures, log);
                Case_OverrideWiredIntoTheLivePath(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = log.ToString().TrimEnd() + "\nWALL_BREACH_ORDER_OK";
                return true;
            }

            reason = log.ToString() + string.Join("\n", failures) + "\nWALL_BREACH_ORDER_FAIL";
            return false;
        }

        // ── 1. The automatic rule is untouched when no order stands ──────────────
        private static void Case_NoOrder_AutoMostDamagedStillWins(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_NoOrder_AutoMostDamagedStillWins");
            var fullNear = new StubWall { HpValue = 100f, Pos = Vector3.zero, Label = "fullNear" };
            var hurtFar = new StubWall { HpValue = 40f, Pos = new Vector3(20f, 0f, 0f), Label = "hurtFar" };
            var list = new List<IDamageable> { fullNear, hurtFar };

            if (!ReferenceEquals(RaidAssaultAi.SelectFocusBreach(list, Vector3.zero), hurtFar))
                failures.Add(Tag + " the 2-arg fallback stopped picking the most-damaged wall");
            if (!ReferenceEquals(RaidAssaultAi.SelectFocusBreach(list, Vector3.zero, null), hurtFar))
                failures.Add(Tag + " a NULL explicit focus must fall through to the most-damaged rule");

            var a = new StubWall { HpValue = 100f, Pos = new Vector3(10f, 0f, 0f), Label = "farFull" };
            var b = new StubWall { HpValue = 100f, Pos = new Vector3(3f, 0f, 0f), Label = "nearFull" };
            if (!ReferenceEquals(RaidAssaultAi.SelectFocusBreach(
                    new List<IDamageable> { a, b }, Vector3.zero, null), b))
                failures.Add(Tag + " the nearest-muster tie-break was lost from the fallback");
            log.AppendLine("   fallback = most-damaged, ties by nearest muster: intact");
        }

        // ── 2. The player's tap beats the most-damaged computation outright ──────
        private static void Case_ExplicitOrder_OverridesMostDamaged(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_ExplicitOrder_OverridesMostDamaged");
            // The WO-1717 sec.3d pairing: the tapped panel is FULL HP and FAR; another wall
            // has already been chipped well past the 0.5 HP tie window. Today that chipped
            // wall wins permanently and the warband walks away from the player's tap.
            var tapped = new StubWall { HpValue = 100f, Pos = new Vector3(25f, 0f, 0f), Label = "tapped" };
            var chipped = new StubWall { HpValue = 12f, Pos = Vector3.zero, Label = "chipped" };
            var list = new List<IDamageable> { chipped, tapped };

            var pick = RaidAssaultAi.SelectFocusBreach(list, Vector3.zero, tapped);
            if (!ReferenceEquals(pick, tapped))
                failures.Add(Tag + " an explicit player breach order MUST beat the most-damaged pick " +
                             "(picked '" + LabelOf(pick) + "', expected 'tapped')");

            // And it wins even when it is not in the scanned candidate list at all - a cached
            // scene scan can trail the tap by a frame, and an invisible intermittent refusal
            // is worse than no feature.
            var notInList = RaidAssaultAi.SelectFocusBreach(
                new List<IDamageable> { chipped }, Vector3.zero, tapped);
            if (!ReferenceEquals(notInList, tapped))
                failures.Add(Tag + " the explicit pick must win even when the cached wall scan " +
                             "has not caught up with it yet");
            log.AppendLine("   explicit tap beats a 12hp wall at the muster: yes");
        }

        // ── 3. The ordered panel collapsing is what clears the order ─────────────
        private static void Case_CollapsedOrder_FallsBackToAuto(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_CollapsedOrder_FallsBackToAuto");
            var collapsed = new StubWall { HpValue = 0f, Pos = new Vector3(25f, 0f, 0f), Label = "collapsed" };
            var chipped = new StubWall { HpValue = 12f, Pos = Vector3.zero, Label = "chipped" };
            var fullFar = new StubWall { HpValue = 100f, Pos = new Vector3(40f, 0f, 0f), Label = "fullFar" };

            var pick = RaidAssaultAi.SelectFocusBreach(
                new List<IDamageable> { chipped, fullFar }, Vector3.zero, collapsed);
            if (!ReferenceEquals(pick, chipped))
                failures.Add(Tag + " a DEAD explicit target must fall back to the automatic " +
                             "most-damaged rule, not strand the warband (picked '" + LabelOf(pick) + "')");
            log.AppendLine("   dead order -> automatic rule resumes: yes");
        }

        // ── 4. All together, except aggro ────────────────────────────────────────
        private static void Case_WarbandRetargetsTogether_AggroTroopKeepsItsFight(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_WarbandRetargetsTogether_AggroTroopKeepsItsFight");

            var tapped = new StubWall { HpValue = 100f, Pos = new Vector3(25f, 0f, 0f), Label = "tapped" };
            var chipped = new StubWall { HpValue = 12f, Pos = Vector3.zero, Label = "chipped" };
            var scan = new List<IDamageable> { chipped, tapped };
            var defender = new StubUnit { Label = "defender" };

            // Four breach-phase troops scattered around the base (different musters, so a
            // per-troop nearest rule would give four different answers) plus one that is
            // being hit - the live peelThreat=True / phase=Peel state from the WO-1717
            // device capture.
            var musters = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 5f),
                new Vector3(-12f, 0f, 18f), new Vector3(8f, 0f, -22f),
            };

            for (int i = 0; i < musters.Length; i++)
            {
                var resolved = ResolveWinner(
                    scan, musters[i], explicitFocus: tapped,
                    peelThreat: false, hasUnit: false, unit: null, unitInAttackRange: false);
                if (!ReferenceEquals(resolved, tapped))
                    failures.Add(Tag + " breach-phase troop " + i + " (muster " + musters[i] +
                                 ") resolved '" + LabelOf(resolved) + "' - every phase=Breach troop " +
                                 "must retarget to the ordered segment in the same update");
            }

            // The aggro'd troop: peelThreat -> Peel -> bucket 0, its own foe. The ordered
            // wall is still resolved for it (same shared focus) and must still lose.
            var aggroWinner = ResolveWinner(
                scan, new Vector3(2f, 0f, 2f), explicitFocus: tapped,
                peelThreat: true, hasUnit: true, unit: defender, unitInAttackRange: true);
            if (!ReferenceEquals(aggroWinner, defender))
                failures.Add(Tag + " an aggro'd (peelThreat) troop must KEEP its current fight when a " +
                             "breach order lands - it resolved '" + LabelOf(aggroWinner) + "'");

            // Guard the inverse too: without aggro that same troop WOULD take the wall, so the
            // case above is proving the peel gate and not an accident of the fixture.
            var sameTroopNoAggro = ResolveWinner(
                scan, new Vector3(2f, 0f, 2f), explicitFocus: tapped,
                peelThreat: false, hasUnit: true, unit: defender, unitInAttackRange: false);
            if (!ReferenceEquals(sameTroopNoAggro, tapped))
                failures.Add(Tag + " fixture check: the same troop without aggro should take the " +
                             "ordered wall, so the aggro case proves the peel gate");
            log.AppendLine("   4/4 breach troops -> ordered panel; aggro'd troop -> its foe: yes");
        }

        /// <summary>
        /// The live resolve, reassembled from the SAME pure calls TroopController makes
        /// (TroopController.cs: SelectFocusBreach -> ResolvePhase -> PickBucket -> bucket
        /// switch). Kept in one place so a fixture troop cannot drift from the real order.
        /// </summary>
        private static IDamageable ResolveWinner(
            List<IDamageable> scan, Vector3 muster, IDamageable explicitFocus,
            bool peelThreat, bool hasUnit, IDamageable unit, bool unitInAttackRange)
        {
            IDamageable otherStruct = RaidAssaultAi.SelectFocusBreach(scan, muster, explicitFocus);

            // The rally march: a rally IS set and the troop has NOT arrived, which is the
            // normal mid-raid state. An explicit order releases it (see the 4-arg overload).
            bool hasExplicitOrder = explicitFocus != null && explicitFocus.IsAlive;
            if (RaidAssaultAi.RallyHoldsMarch(true, false, peelThreat, hasExplicitOrder))
                otherStruct = null;

            var phase = RaidAssaultAi.ResolvePhase(peelThreat, false, false);
            int bucket = RaidAssaultAi.PickBucket(
                phase, false, hasUnit, false, otherStruct != null, unitInAttackRange, false);
            switch (bucket)
            {
                case 0: return unit;
                case 2: return otherStruct;
                default: return null;
            }
        }

        // ── 5. The rally march releases for an explicit order, and only that ─────
        private static void Case_RallyMarch_ReleasedByExplicitOrderOnly(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_RallyMarch_ReleasedByExplicitOrderOnly");
            if (!RaidAssaultAi.RallyHoldsMarch(true, false, false, false))
                failures.Add(Tag + " with NO order the rally must still hold the march " +
                             "(owner 2026-09-12: no wall-ring chewing on the way to the flag)");
            if (RaidAssaultAi.RallyHoldsMarch(true, false, false, true))
                failures.Add(Tag + " an EXPLICIT breach order must release the rally march, or no " +
                             "rally-marching troop ever obeys the tap");
            if (RaidAssaultAi.RallyHoldsMarch(false, false, false, false))
                failures.Add(Tag + " no rally must not hold the march");
            if (RaidAssaultAi.RallyHoldsMarch(true, true, false, false))
                failures.Add(Tag + " arrival must still release the march");
            log.AppendLine("   implicit ring-farm still suppressed; explicit order released: yes");
        }

        // ── 6. The pure rule is actually on the live path ────────────────────────
        private static void Case_OverrideWiredIntoTheLivePath(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- Case_OverrideWiredIntoTheLivePath");

            string controller = Read("Assets/_Modules/Village/Troops/TroopController.cs");
            if (controller == null)
            {
                failures.Add(Tag + " TroopController.cs could not be read");
            }
            else
            {
                int start = controller.IndexOf("SharedBreachFocus(Vector3 muster)", StringComparison.Ordinal);
                if (start < 0)
                {
                    failures.Add(Tag + " TroopController.SharedBreachFocus not found - the shared " +
                                 "breach seam moved; re-point this case before trusting it");
                }
                else
                {
                    int end = controller.IndexOf("IsHostileStructure", start, StringComparison.Ordinal);
                    if (end < 0) end = Math.Min(controller.Length, start + 4000);
                    string body = controller.Substring(start, end - start);
                    if (body.IndexOf("TroopBreachOrder", StringComparison.Ordinal) < 0)
                        failures.Add(Tag + " SharedBreachFocus no longer consults TroopBreachOrder - the " +
                                     "player's explicit pick cannot reach the warband, however well the " +
                                     "pure rule above passes");
                }
            }

            string deploy = Read("Assets/_Modules/Village/Troops/RaidDeployController.cs");
            if (deploy == null)
            {
                failures.Add(Tag + " RaidDeployController.cs could not be read");
            }
            else
            {
                if (deploy.IndexOf("TroopBreachOrder.Set(", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " nothing in RaidDeployController SETS a breach order - the " +
                                 "Breach tap is gone");
                if (deploy.IndexOf("ToggleBreach", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the Breach toggle is gone from the raid HUD");
                if (deploy.IndexOf("TroopBreachOrder.Clear()", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " nothing clears the breach order - a stale static would point " +
                                 "the next raid's warband at a destroyed wall");
                // The default tap must stay Rally: HandleRallyTap has to survive, and the breach
                // branch has to be gated on the mode rather than replacing it.
                if (deploy.IndexOf("if (_breachMode) { HandleBreachTap(screenPoint); return; }",
                        StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the breach tap is no longer gated on _breachMode - a wall tap " +
                                 "with Breach OFF must still behave exactly as Rally");
                if (deploy.IndexOf("if (_rallyMode) { HandleRallyTap(screenPoint); return; }",
                        StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the default rally tap route changed - WO-1719 adds a mode, it " +
                                 "does not change the default world tap");
            }
            log.AppendLine("   SharedBreachFocus reads the order; the HUD sets/clears it; Rally untouched");
        }

        private static string Read(string relative)
        {
            try
            {
                string full = Path.Combine(Directory.GetCurrentDirectory(), relative);
                return File.Exists(full) ? File.ReadAllText(full) : null;
            }
            catch { return null; }
        }

        private static string LabelOf(IDamageable d)
        {
            var w = d as StubWall;
            if (w != null) return w.Label;
            var u = d as StubUnit;
            if (u != null) return u.Label;
            return "<null>";
        }

        private sealed class StubWall : IDamageable
        {
            public float HpValue;
            public Vector3 Pos;
            public string Label;
            public CombatFaction Faction { get { return CombatFaction.Hostile; } }
            public Vector3 WorldPosition { get { return Pos; } }
            public float Hp { get { return HpValue; } }
            public bool IsAlive { get { return HpValue > 0f; } }
            public void TakeDamage(float amount, DamageElement element) { }
            public void ApplyStatus(StatusEffect effect, float seconds) { }
        }

        private sealed class StubUnit : IDamageable
        {
            public string Label;
            public CombatFaction Faction { get { return CombatFaction.Hostile; } }
            public Vector3 WorldPosition { get { return Vector3.zero; } }
            public float Hp { get { return 50f; } }
            public bool IsAlive { get { return true; } }
            public void TakeDamage(float amount, DamageElement element) { }
            public void ApplyStatus(StatusEffect effect, float seconds) { }
        }
    }
}
