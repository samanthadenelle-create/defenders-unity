// =============================================================================
// RepairTapGuardRegression [repair-tap-guard]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers: REPAIR_TAP_GUARD_OK / REPAIR_TAP_GUARD_FAIL.
//
// WHAT THIS PINS — the three WO-1708 defects, all on the hub repair input path.
//
// [pause-hides-repair-all]  CRITERION 3, the one this suite proves OUTRIGHT.
//   The owner's screenshots (flag_20260912-033141_00/_01.png) show "PAIR ALL" and
//   "Wood 15  Iron 7" painted across the PAUSE menu. That is NOT a sorting-order
//   bug and raising the card's order would have made it worse:
//     * PauseController.cs:195 builds its modal at sortingOrder 31500 and
//       ElarionUiKit.cs:967 sets overrideSorting = true, so 31500 genuinely draws
//       OVER HubRepairAffordance's 905 (HubRepairAffordance.cs:61);
//     * ElarionUiKit.cs:125 paints the modal scrim at alpha 0.85 — so 15% of
//       whatever sits underneath reads straight through it.
//   The only fix is for the card not to be drawn at all while a modal is up. The
//   seam is PanelManager (DeNelle.Core.UI), the single modal arbiter that
//   PauseController registers with (RegisterBattleAllowed("Pause") at
//   PauseController.cs:247, NotifyOpened at :293) and that MobileInteractButton
//   already reads for the same prompt-suppression reason (PanelManager.cs:19).
//   This case opens that seam and asserts the affordance's own public
//   DiagnosticState reports HiddenPanelOpen.
//
// [hit-chain-names-the-refusal]  CRITERION 2, INSTRUMENTATION ONLY.
//   ⚠ READ THIS BEFORE "FIXING" THE WRAP. The ticket's UNPROVEN item 3 guessed the
//   cause was "a component on a parent the wrap does not walk (GetComponent where
//   GetComponentInParent is needed)". THAT GUESS IS FALSE, and it was disproven by
//   reading, not by inference: RepairTarget.TryWrap ALREADY uses
//   GetComponentInParent<WallSegment>/<Gate>/<Building> (RepairTarget.cs:85-97).
//   The wrap walks parents correctly. Two further source facts, measured:
//     * grep -c WallSegment Assets/Editor/WallTools/SyntyCastlePerimeterBuilder.cs = 0
//     * grep -c WallSegment Assets/Editor/CastleWallsFromRecipe.cs = 0
//   Those two files are what mint `Battlement_{i}` (SyntyCastlePerimeterBuilder.cs:242)
//   and `Wall_*_DoorJamb_*` (CastleWallsFromRecipe.cs:233-234, put on structureLayer
//   at :237-238). They add NO WallSegment anywhere. Meanwhile WallRepairController's
//   _selectableMask defaults to ~0 (WallRepairController.cs:122) — EVERY layer. So the
//   logged `Wall_4` / `Battlement_1` / `Wall_DoorJamb_R` refusals are most consistent
//   with non-repairable Synty perimeter DECOR sitting on a raycast mask that accepts
//   everything, and `Hero (Blaise)` is RepairTarget's deliberate Player-tag reject
//   (RepairTarget.cs:82-84) — i.e. the wrap is behaving correctly in every logged case.
//   THAT REMAINS UNPROVEN BY CAPTURE and no behavioural fix was made on it. What
//   shipped is the trace that settles it in one read: this case pins that
//   DescribeHitChain reports the component set per level, so a capture can say WHICH
//   check refused instead of only that one did.
//
// [pointer-guard-exists]  CRITERION 1, a SOURCE LINT, and it is honest about being one.
//   Whether the guard actually suppresses a real button press can only be proven by a
//   capture on a device (grep the log for the Throttle key `tap-over-ui`). What a
//   headless suite CAN pin is that the guard has not been deleted again: the file had
//   ZERO EventSystem references before WO-1708, which is exactly how it shipped.
//
// OUT OF SCOPE, deliberately: whether Synty perimeter decor SHOULD be repairable (a
// design ruling for the owner — give those objects WallSegment, or take them off the
// selectable mask), and any change to RepairTarget's documented WallSegment/Gate/
// Building scope (WO-1708 §4 forbids widening it).
//
// -----------------------------------------------------------------------------
// REVERT RECIPES (per case) — what to delete to put the tree back the way it was.
//
//   [pause-hides-repair-all]  Delete the PanelManager.AnyOpen gate at the top of
//                             HubRepairAffordance.Refresh, its OnEnable/OnDisable/
//                             OnPanelStateChanged trio, the Vis.HiddenPanelOpen enum
//                             member and its Announce case; delete this case.
//   [hit-chain-names-the-refusal]
//                             Delete WallRepairController.DescribeHitChain and restore
//                             the old one-line tap-not-repairable message; delete this case.
//   [pointer-guard-exists]    Delete WallRepairController.PointerIsOverUi, its call in
//                             HandleTap and the UnityEngine.EventSystems using; delete this case.
//   WHOLE SUITE               Delete this file and its DataRegression.cs registration line.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1708: the hub repair card hides under a modal, and a refused tap says why.</summary>
    public static class RepairTapGuardRegression
    {
        private const string ControllerSrc =
            "Assets/_Modules/Village/Walls/WallRepairController.cs";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("REPAIR_TAP_GUARD_OK - " + reason);
            else Debug.LogError("REPAIR_TAP_GUARD_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            Case(failures, "pause-hides-repair-all", () => CasePauseHides(failures, notes));
            Case(failures, "hit-chain-names-the-refusal", () => CaseHitChain(failures, notes));
            Case(failures, "pointer-guard-exists", () => CasePointerGuard(failures, notes));

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count > 0)
            {
                reason = "repair-tap-guard FAIL x" + failures.Count + ": "
                       + string.Join(" | ", failures) + noteStr;
                return false;
            }

            reason = "REPAIR TAP GUARD OK (WO-1708) - the hub Repair-All card reports "
                   + "HiddenPanelOpen while PanelManager holds a modal, so it can no longer "
                   + "read through the pause scrim (alpha 0.85 over a 905 canvas under a "
                   + "31500 overrideSorting modal); a refused world tap now dumps the hit "
                   + "object's parent chain WITH the components each level carries, so a "
                   + "capture can name which check refused; and WallRepairController still "
                   + "carries the EventSystem pointer guard (it had ZERO EventSystem "
                   + "references before this WO)" + noteStr;
            return true;
        }

        // =====================================================================
        //  [pause-hides-repair-all] - CRITERION 3
        // =====================================================================

        private static void CasePauseHides(List<string> failures, List<string> notes)
        {
            // PanelManager is STATIC state shared with every other suite in the batch.
            // The handle is released in a finally so a failure here cannot leave a ghost
            // modal open and redden an unrelated suite downstream.
            GameObject host = null;
            PanelHandle handle = null;
            bool testPanelOpen = false;
            try
            {
                host = new GameObject("WO1708_HubRepairAffordanceProbe");

                // PRE-SEED THE BACKEND ON OUR OWN HOST, AND THE REASON IS LEAK CONTAINMENT.
                // The modal-CLOSED half of this case walks the full Refresh() path, which
                // reaches HubRepairAffordance.EnsureRepair(). With no controller in the scene
                // that method does `new GameObject("WallRepair_HubEngine")` — an object our
                // `finally` would NOT own, which would then survive into [repair-probe] and
                // [repair-hud-contract], both of which resolve a controller by
                // FindFirstObjectByType. Seeding one on `host` makes EnsureRepair find THIS
                // one, so the backend dies with the host.
                // Safe without Awake (edit-mode AddComponent does not run it): every
                // collection RepairAllCost touches is `readonly ... = new List<>()` at its
                // declaration (WallRepairController.cs:169-178), not built in Awake.
                host.AddComponent<WallRepairController>();

                var affordance = host.AddComponent<HubRepairAffordance>();

                // NOTE on why this case asserts DiagnosticState and NOT canvas activity:
                // AddComponent in EDIT MODE does not run Awake, so the affordance's private
                // _canvas is null and SetVisible is a no-op. DiagnosticState is the public,
                // already-shipped diagnostic seam (HubRepairAffordance.DiagnosticState) and
                // it is what the F8 capture reads too, so pinning it pins the same fact the
                // owner's screenshot is about.
                var refresh = typeof(HubRepairAffordance).GetMethod("Refresh",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (refresh == null)
                {
                    failures.Add("pause-hides-repair-all: HubRepairAffordance.Refresh not found - "
                               + "the suite cannot drive the visibility decision");
                    return;
                }

                handle = PanelManager.Register("WO1708TestPanel", () => { }, () => testPanelOpen);
                testPanelOpen = true;
                PanelManager.NotifyOpened(handle);
                if (!PanelManager.AnyOpen)
                {
                    failures.Add("pause-hides-repair-all: PanelManager.NotifyOpened did not set "
                               + "AnyOpen - the seam this fix hangs on is not behaving, so the "
                               + "case proves nothing either way");
                    return;
                }

                refresh.Invoke(affordance, null);
                string state = affordance.DiagnosticState;
                if (state != "HiddenPanelOpen")
                {
                    failures.Add("pause-hides-repair-all: with a modal OPEN the affordance reported "
                               + "DiagnosticState='" + state + "', expected 'HiddenPanelOpen'. The "
                               + "Repair-All card would read through the pause scrim again "
                               + "(ElarionUiKit.cs:125 paints it at alpha 0.85)");
                    return;
                }

                notes.Add("modal-open -> DiagnosticState=HiddenPanelOpen");

                // The gate must be a GATE, not a permanent hide: closing the modal has to let
                // the normal damage/affordability decision run again. In edit mode there are no
                // damaged structures, so the expected state is HiddenNothingDamaged - what
                // matters is that it is NO LONGER HiddenPanelOpen.
                testPanelOpen = false;
                PanelManager.NotifyClosed(handle);
                handle = null;
                refresh.Invoke(affordance, null);
                string after = affordance.DiagnosticState;
                if (after == "HiddenPanelOpen")
                {
                    failures.Add("pause-hides-repair-all: the affordance stayed in HiddenPanelOpen "
                               + "AFTER the modal closed - the gate latched instead of gating, so "
                               + "the card would never come back");
                    return;
                }
                notes.Add("modal-closed -> DiagnosticState=" + after);
            }
            catch (Exception e)
            {
                failures.Add("pause-hides-repair-all threw: " + e.Message);
            }
            finally
            {
                if (handle != null) { try { PanelManager.NotifyClosed(handle); } catch { } }
                if (host != null) UnityEngine.Object.DestroyImmediate(host);

                // BELT, not braces. If the pre-seed above is ever removed, or an early
                // `return` lands between the two, EnsureRepair's self-installed engine would
                // outlive this case and pollute every suite that runs after it. Sweep by the
                // exact name EnsureRepair mints so nothing else can be caught by it.
                try
                {
                    var strays = UnityEngine.Object.FindObjectsByType<WallRepairController>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None);
                    for (int i = 0; i < strays.Length; i++)
                    {
                        if (strays[i] == null) continue;
                        if (strays[i].gameObject.name == "WallRepair_HubEngine")
                            UnityEngine.Object.DestroyImmediate(strays[i].gameObject);
                    }
                }
                catch { }
            }
        }

        // =====================================================================
        //  [hit-chain-names-the-refusal] - CRITERION 2 (instrumentation only)
        // =====================================================================

        private static void CaseHitChain(List<string> failures, List<string> notes)
        {
            GameObject parent = null;
            try
            {
                // The fixture is built to LOOK like the real logged failures: a `Wall_4`-shaped
                // object carrying a collider and nothing else, under a plain parent. If the wrap
                // were the defect this shape would be a false negative; it is here to prove the
                // TRACE reports enough to tell the two apart.
                parent = new GameObject("WO1708_CastlePerimeter");
                var child = new GameObject("Wall_4");
                child.transform.SetParent(parent.transform, false);
                child.AddComponent<BoxCollider>();

                string chain = WallRepairController.DescribeHitChain(child.transform, 3);
                if (string.IsNullOrEmpty(chain))
                {
                    failures.Add("hit-chain: DescribeHitChain returned empty for a live transform");
                    return;
                }
                if (chain.IndexOf("Wall_4", StringComparison.Ordinal) < 0)
                {
                    failures.Add("hit-chain: the chain does not name the HIT object ('Wall_4'): " + chain);
                    return;
                }
                if (chain.IndexOf("WO1708_CastlePerimeter", StringComparison.Ordinal) < 0)
                {
                    failures.Add("hit-chain: the chain does not walk to the PARENT - the whole point "
                               + "is to show what the ancestors carry, since that is where "
                               + "GetComponentInParent looks: " + chain);
                    return;
                }
                if (chain.IndexOf("BoxCollider", StringComparison.Ordinal) < 0)
                {
                    failures.Add("hit-chain: the chain does not report the COMPONENTS on a level "
                               + "(expected BoxCollider): " + chain);
                    return;
                }
                if (chain.IndexOf("WallSegment", StringComparison.Ordinal) >= 0)
                {
                    failures.Add("hit-chain: the chain claims a WallSegment on a fixture that has "
                               + "none - the dump is inventing components: " + chain);
                    return;
                }
                if (chain.IndexOf("Transform", StringComparison.Ordinal) >= 0)
                {
                    failures.Add("hit-chain: Transform is listed - it is on every level and carries "
                               + "no signal, so it is deliberately omitted: " + chain);
                    return;
                }
                notes.Add("chain='" + chain + "'");

                // A null hit must not throw - the emitter calls this on a path that already
                // handles a null collider.
                string nullChain = WallRepairController.DescribeHitChain(null, 3);
                if (string.IsNullOrEmpty(nullChain))
                    failures.Add("hit-chain: DescribeHitChain(null) returned empty instead of a marker");
            }
            catch (Exception e)
            {
                failures.Add("hit-chain threw: " + e.Message);
            }
            finally
            {
                if (parent != null) UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        // =====================================================================
        //  [pointer-guard-exists] - CRITERION 1 (source lint; capture is the real proof)
        // =====================================================================

        private static void CasePointerGuard(List<string> failures, List<string> notes)
        {
            try
            {
                if (!File.Exists(ControllerSrc))
                {
                    failures.Add("pointer-guard: " + ControllerSrc + " not found");
                    return;
                }
                string src = File.ReadAllText(ControllerSrc);

                if (src.IndexOf("UnityEngine.EventSystems", StringComparison.Ordinal) < 0)
                {
                    failures.Add("pointer-guard: WallRepairController no longer references "
                               + "UnityEngine.EventSystems. That is the exact state it shipped in "
                               + "(measured: grep -c EventSystem returned 0), in which every "
                               + "on-screen button press ALSO raycast the world behind it");
                    return;
                }
                if (src.IndexOf("IsPointerOverGameObject(t.fingerId)", StringComparison.Ordinal) < 0)
                {
                    failures.Add("pointer-guard: the TOUCH branch does not use the fingerId overload. "
                               + "Unity's EventSystem reference: the parameterless form answers for "
                               + "pointerId -1 (the left MOUSE button), so on the Seeker it would "
                               + "return false for every real finger - i.e. no guard at all on the "
                               + "one platform that produced this ticket");
                    return;
                }
                if (src.IndexOf("PointerIsOverUi()", StringComparison.Ordinal) < 0)
                {
                    failures.Add("pointer-guard: HandleTap no longer calls PointerIsOverUi()");
                    return;
                }
                if (src.IndexOf("tap-over-ui", StringComparison.Ordinal) < 0)
                {
                    failures.Add("pointer-guard: the FlowTrace.Throttle key 'tap-over-ui' is gone - "
                               + "without it a capture cannot prove the guard fired, and CLAUDE.md "
                               + "section 12 forbids a silent skip");
                    return;
                }
                notes.Add("guard present; capture proof = grep the device log for 'tap-over-ui'");
            }
            catch (Exception e)
            {
                failures.Add("pointer-guard threw: " + e.Message);
            }
        }

        // =====================================================================
        //  Harness
        // =====================================================================

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception e) { failures.Add(name + " threw: " + e.Message); }
        }
    }
}
