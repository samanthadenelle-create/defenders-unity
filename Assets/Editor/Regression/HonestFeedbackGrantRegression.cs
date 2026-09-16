// =============================================================================
// HonestFeedbackGrantRegression [honest-feedback-grant] -- WO-1432 acceptance 1:
// the thank-you DELIVERS 1000/1000/1000 even with the bank near its ceiling.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression, which references DeNelle.Village directly,
// so this drives the REAL types with no reflection except for the two private
// singleton seams every fixture in this folder pokes (GameStateService._state /
// ._instance and EconomyService's Instance backing field) -- the PackGrantRegression
// pattern, verbatim.
//
// -----------------------------------------------------------------------------
// WHY A "DELTA == 1000" ASSERTION ALONE WOULD BE A HOLLOW GREEN
// -----------------------------------------------------------------------------
// INSTRUMENTATION_STANDARD sec.1.4b: before you write an assertion, ask what broken
// state would make it print something different. Against a fresh wallet and a
// 2,000-unit ceiling, "the purchased grant delivered 1000" passes whether or not the
// cap is respected, because 1000 fits. The oracle would be measuring nothing.
//
// So this suite runs TWO grants against the SAME deliberately near-cap fixture:
//
//   CONTROL  an EarnedIncome grant of the same basket, which MUST be clamped to
//            less than 1000 on every axis. If it is not, the fixture is not
//            actually near the cap and the real case proves nothing -- so the
//            control failing is reported as a BROKEN FIXTURE, not as a pass.
//   SUBJECT  HonestFeedbackGrant.TryApply, which MUST deliver EXACTLY 1000 on all
//            three axes from the identical starting balances.
//
// The pair is the proof: same wallet, same amount, one clamps and one does not,
// and the only difference is BankGrantKind. That is TownBankCapacity law 5 being
// exercised rather than assumed.
//
// -----------------------------------------------------------------------------
// WHAT IS MEASURED, AND WHY IT IS NOT `Resources.Wood`
// -----------------------------------------------------------------------------
// The deltas are read off GameState.Wood, GameState.Iron and
// GameState.Resources.Stone -- the fields the economy seam actually writes
// (EconomyService.GrantInternal writes State.Wood/State.Iron and routes Food
// through GameStateService.AddStone).
//
// ⚠ WO-1432 section 2a's table says wood -> `Resources.Wood` and iron ->
// `Resources.Iron`. THOSE MEMBERS DO NOT EXIST: ResourceBalance (NestedTypes.cs:41)
// carries only Crystals / Food / Coins, and Wood/Iron are top-level GameState
// scalars. An oracle that measured `Resources.Wood` would read delta 0 forever.
// The WO is right about the important half -- there is no Stone balance and the
// player-facing Stone IS Resources.Stone (GameState.cs:59-71, WO-1212/WO-1163).
//
// Marker: HONEST_FEEDBACK_GRANT_OK / HONEST_FEEDBACK_GRANT_FAIL. Expected: GREEN.
//
// REVERT RECIPE (RED): in HonestFeedbackGrant.TryApply, swap
// GrantSpendablePurchased for GrantSpendable. The subject grant then clamps to the
// same ~100 units the control did and all three delta assertions fire at once --
// which is precisely the "a screen said 1000 and the wallet got 340" failure the
// PurchasedOrPromised kind exists to prevent.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "honest-feedback-grant suite", () => { if (!HonestFeedbackGrantRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[honest-feedback-grant] " + r); });
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Feedback;

namespace DeNelle.Editor
{
    /// <summary>Proves the WO-1432 thank-you lands in full against a near-cap bank.</summary>
    public static class HonestFeedbackGrantRegression
    {
        private const string Tag = "[honest-feedback-grant]";
        private const string SaveKey = "dotr-save";

        /// <summary>How far below the ceiling the fixture parks each balance. Small enough
        /// that a 1000 basket cannot fit, large enough that the control still moves.</summary>
        private const int Headroom = 100;

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- HONEST FEEDBACK GRANT (WO-1432: 1000 wood / 1000 stone(Food) / 1000 iron, " +
                           "PurchasedOrPromised, against a bank parked " + Headroom + " units under its ceiling) ---");

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;

            GameStateService priorGss = GameStateService.Instance;
            object priorEcon = HonestFeedbackFixture.GetInstance(typeof(EconomyService));

            GameObject gssGo = null, econGo = null;
            GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (honest-feedback-grant oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!HonestFeedbackFixture.InstallState(gss, throwaway))
                {
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "HONEST FEEDBACK GRANT", "GameStateService state seam not reflectable (needs fleet)");
                }

                econGo = new GameObject("EconomyService (honest-feedback-grant oracle)");
                var econ = econGo.AddComponent<EconomyService>();
                HonestFeedbackFixture.SetInstance(typeof(EconomyService), econ);

                // ── the ceilings the fixture will park just under ──────────────
                int maxWood = TownBankCapacity.MaxOf(BankResource.Wood);
                int maxIron = TownBankCapacity.MaxOf(BankResource.Iron);
                int maxFood = TownBankCapacity.MaxOf(BankResource.Stone);
                log.AppendLine($"  ceilings: wood={maxWood} iron={maxIron} stone(Food)={maxFood}");

                // A MISSING FIXTURE FAILS AND NAMES ITSELF -- it never silently passes. If a
                // ceiling is unreadable or smaller than the headroom, the near-cap premise does
                // not hold and no claim can be made from this run.
                if (maxWood == int.MaxValue || maxIron == int.MaxValue || maxFood == int.MaxValue ||
                    maxWood <= Headroom || maxIron <= Headroom || maxFood <= Headroom)
                {
                    failures.Add(Tag + " [near-cap-fixture] one or more ceilings are uncapped or below the " +
                                 Headroom + "-unit headroom (wood=" + maxWood + " iron=" + maxIron +
                                 " stone=" + maxFood + "), so the bank cannot be parked near its cap and " +
                                 "neither case below would be measuring anything. FAIL, not a skip");
                    reason = Finish(failures, log);
                    return false;
                }

                // =============================================================
                //  CONTROL -- the fixture must actually BITE.
                // =============================================================
                ParkNearCap(throwaway, maxWood, maxIron, maxFood);
                var ctlBefore = Snapshot(throwaway);
                var ctlApplied = econ.Grant(new ResourceCost(
                    HonestFeedbackGrant.GrantWood, HonestFeedbackGrant.GrantStone, HonestFeedbackGrant.GrantIron));
                var ctlAfter = Snapshot(throwaway);
                int ctlWood = ctlAfter.wood - ctlBefore.wood;
                int ctlFood = ctlAfter.food - ctlBefore.food;
                int ctlIron = ctlAfter.iron - ctlBefore.iron;
                log.AppendLine($"  CONTROL (EarnedIncome) measured deltas: wood=+{ctlWood} stone(Food)=+{ctlFood} " +
                               $"iron=+{ctlIron} (seam reported W{ctlApplied.Wood}/F{ctlApplied.Stone}/I{ctlApplied.Iron})");

                if (ctlWood >= HonestFeedbackGrant.GrantWood ||
                    ctlFood >= HonestFeedbackGrant.GrantStone ||
                    ctlIron >= HonestFeedbackGrant.GrantIron)
                {
                    failures.Add(Tag + " [near-cap-fixture] the CONTROL EarnedIncome grant was NOT clamped " +
                                 "(wood +" + ctlWood + ", stone +" + ctlFood + ", iron +" + ctlIron + " against a " +
                                 Headroom + "-unit headroom). The fixture is not near the cap, so the subject " +
                                 "case below would pass whether or not PurchasedOrPromised is exempt. This is a " +
                                 "BROKEN ORACLE, not a passing grant");
                }

                // =============================================================
                //  SUBJECT -- the real seam, from the identical starting balances.
                // =============================================================
                ParkNearCap(throwaway, maxWood, maxIron, maxFood);
                ClearClaimFlag(throwaway);

                var before = Snapshot(throwaway);
                var outcome = HonestFeedbackGrant.TryApply(out var applied);
                var after = Snapshot(throwaway);

                int dWood = after.wood - before.wood;
                int dFood = after.food - before.food;
                int dIron = after.iron - before.iron;
                log.AppendLine($"  SUBJECT (TryApply) outcome={outcome} measured deltas: wood=+{dWood} " +
                               $"stone(Food)=+{dFood} iron=+{dIron} (seam reported W{applied.Wood}/F{applied.Stone}/I{applied.Iron})");

                if (outcome != ThankYouGrantOutcome.Applied)
                    failures.Add(Tag + " [thank-you-applies] TryApply returned " + outcome +
                                 " on a clean save with a live GameState and EconomyService -- expected Applied");

                if (dWood != HonestFeedbackGrant.GrantWood)
                    failures.Add(Tag + " [wood-exactly-1000] MEASURED GameState.Wood delta " + dWood +
                                 " != " + HonestFeedbackGrant.GrantWood + ". A PurchasedOrPromised grant is " +
                                 "NEVER clamped (TownBankCapacity law 5) -- the screen promised an exact number");
                if (dFood != HonestFeedbackGrant.GrantStone)
                    failures.Add(Tag + " [stone-exactly-1000] MEASURED GameState.Resources.Stone delta " + dFood +
                                 " != " + HonestFeedbackGrant.GrantStone + ". Stone IS Resources.Stone " +
                                 "(GameState.cs:59-71) -- there is no Stone balance to write instead");
                if (dIron != HonestFeedbackGrant.GrantIron)
                    failures.Add(Tag + " [iron-exactly-1000] MEASURED GameState.Iron delta " + dIron +
                                 " != " + HonestFeedbackGrant.GrantIron + ". A PurchasedOrPromised grant is " +
                                 "NEVER clamped (TownBankCapacity law 5)");

                // The seam's own reported basket must agree with the wallet. They can only differ
                // if a grant path reports what it was ASKED for rather than what it APPLIED --
                // the ECON-SWEEP 2026-08-16 defect-2 class, and the reason the panel reads it.
                if (applied.Wood != dWood || applied.Stone != dFood || applied.Iron != dIron)
                    failures.Add(Tag + " [reported-matches-measured] the economy seam reported W" + applied.Wood +
                                 "/F" + applied.Stone + "/I" + applied.Iron + " but the wallet MOVED by W" + dWood +
                                 "/F" + dFood + "/I" + dIron + ". A caller showing the player the reported number " +
                                 "would be naming resources they did not receive");

                // Over-cap is the EXPECTED end state here and is legitimate
                // (FOUNDATIONAL_RULINGS.md section 7). Recorded, never asserted against.
                log.AppendLine($"  post-grant over-cap units: wood={HonestFeedbackGrant.OverCapUnits(BankResource.Wood)} " +
                               $"stone(Food)={HonestFeedbackGrant.OverCapUnits(BankResource.Stone)} " +
                               $"iron={HonestFeedbackGrant.OverCapUnits(BankResource.Iron)} " +
                               "(above the cap is a legitimate paid state -- nothing is lost)");
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " oracle threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (econGo != null) UnityEngine.Object.DestroyImmediate(econGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                HonestFeedbackFixture.SetInstance(typeof(EconomyService), priorEcon);
                HonestFeedbackFixture.SetGssInstance(priorGss);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave); else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        // =====================================================================
        //  Fixture helpers
        // =====================================================================

        private struct Balances { public int wood, food, iron; }

        private static Balances Snapshot(GameState s)
            => new Balances { wood = s.Wood, food = s.Resources.Stone, iron = s.Iron };

        private static void ParkNearCap(GameState s, int maxWood, int maxIron, int maxFood)
        {
            s.Wood = maxWood - Headroom;
            s.Iron = maxIron - Headroom;
            var r = s.Resources;
            r.Stone = maxFood - Headroom;
            s.Resources = r;
        }

        private static void ClearClaimFlag(GameState s)
        {
            if (s.SeenTutorials == null) return;
            s.SeenTutorials[HonestFeedbackKeys.GrantClaimedKey] = false;
            s.SeenTutorials[HonestFeedbackKeys.OfferedKey] = false;
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "HONEST_FEEDBACK_GRANT_OK");
                return "HONEST FEEDBACK GRANT OK -- against a bank parked " + Headroom + " units under its " +
                       "ceiling, an EarnedIncome control grant CLAMPED while HonestFeedbackGrant.TryApply " +
                       "delivered exactly 1000 wood / 1000 stone(Resources.Stone) / 1000 iron, and the seam's " +
                       "reported basket matched the measured wallet movement on all three axes";
            }
            string reason = "honest-feedback-grant: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "HONEST_FEEDBACK_GRANT_FAIL: " + reason);
            return reason;
        }
    }

    /// <summary>
    /// The private-singleton pokes both WO-1432 oracles need. Shared so the two suites cannot
    /// drift apart -- the duplicated-state failure CLAUDE.md sections 2 and 5 keep paying for.
    /// </summary>
    internal static class HonestFeedbackFixture
    {
        internal static bool InstallState(GameStateService svc, GameState state)
        {
            var f = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(svc, state);
            return SetGssInstance(svc);
        }

        internal static bool SetGssInstance(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }

        internal static FieldInfo InstanceField(Type t)
        {
            var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                 ?? t.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) return f;
            foreach (var ff in t.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
                if (ff.Name.Contains("Instance") && ff.FieldType == t) return ff;
            return null;
        }

        internal static object GetInstance(Type t) { var f = InstanceField(t); return f != null ? f.GetValue(null) : null; }

        internal static void SetInstance(Type t, object val) { var f = InstanceField(t); if (f != null) f.SetValue(null, val); }
    }
}
