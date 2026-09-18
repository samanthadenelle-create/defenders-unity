// =============================================================================
// FoundingReachabilityRegression [founding-reach] -- proves the founding choice is
// actually REACHABLE on a fresh save and correctly suppressed for a returning player.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. FoundingChoiceController lives in
// DeNelle.Onboarding (not referenced by this asmdef), so ShouldOffer is driven by
// reflection over a throwaway GameState:
//   * fresh state (Onboarded=false, empty BaseLayout) -> ShouldOffer == true, AND
//   * post-onboard state (Onboarded=true)            -> ShouldOffer == false.
// Plus a source-scan that the reachability wiring is present: the HeroSelect bypass
// path AND PetSelect both reference PresentOrContinue (the founding entry point).
//
// Marker: FOUNDING_REACH_OK / FOUNDING_REACH_FAIL. Expected: GREEN.
//
// Wire (DataRegression.RunAll):
//   if (!FoundingReachabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[founding-reach] " + r);
// =============================================================================
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Editor
{
    public static class FoundingReachabilityRegression
    {
        private const string SaveKey = "dotr-save";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- FOUNDING REACHABILITY (ShouldOffer fresh=true / post-onboard=false + PresentOrContinue wiring) ---");

            var fccType = FindType("DeNelle.Onboarding.FoundingChoiceController");
            if (fccType == null)
            {
                failures.Add("FoundingChoiceController type not found (DeNelle.Onboarding not compiled?)");
                reason = Finish(failures, log);
                return failures.Count == 0;
            }
            var shouldOffer = fccType.GetProperty("ShouldOffer", BindingFlags.Public | BindingFlags.Static);
            if (shouldOffer == null)
            {
                failures.Add("FoundingChoiceController.ShouldOffer static property not found (reachability seam renamed)");
                reason = Finish(failures, log);
                return failures.Count == 0;
            }
            var decidedLatch = fccType.GetField("_decidedThisSession", BindingFlags.NonPublic | BindingFlags.Static);

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;
            object priorLatch = decidedLatch != null ? decidedLatch.GetValue(null) : null;
            GameStateService priorGss = GameStateService.Instance;
            GameObject gssGo = null;
            GameState throwaway = null;
            try
            {
                // Clear the once-per-session latch so ShouldOffer reflects state, not a prior present.
                if (decidedLatch != null) decidedLatch.SetValue(null, false);

                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (founding-reach oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "FOUNDING REACH", "GameStateService state seam not reflectable (needs fleet)");
                }

                // FRESH: not onboarded, nothing founded -> offer the founding choice.
                throwaway.Onboarded = false;
                if (throwaway.BaseLayout != null) throwaway.BaseLayout.Clear();
                if (decidedLatch != null) decidedLatch.SetValue(null, false);
                bool fresh = (bool)shouldOffer.GetValue(null);
                log.AppendLine($"  fresh state -> ShouldOffer={fresh} (want true)");
                if (!fresh)
                    failures.Add("[founding-reach] ShouldOffer is FALSE on a fresh save (Onboarded=false, empty BaseLayout) -- the founding choice is unreachable for a new player");

                // POST-ONBOARD: returning player -> suppress.
                throwaway.Onboarded = true;
                if (decidedLatch != null) decidedLatch.SetValue(null, false);
                bool post = (bool)shouldOffer.GetValue(null);
                log.AppendLine($"  post-onboard state -> ShouldOffer={post} (want false)");
                if (post)
                    failures.Add("[founding-reach] ShouldOffer is TRUE after Onboarded=true -- a returning player would be re-offered the founding choice");
            }
            catch (Exception ex)
            {
                failures.Add($"founding-reach oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                SetGssInstance(priorGss);
                if (decidedLatch != null && priorLatch != null) decidedLatch.SetValue(null, priorLatch);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave); else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }

            // Source-scan: the reachability wiring calls PresentOrContinue.
            CheckReferences("Onboarding/HeroSelectController.cs", "HeroSelect bypass", failures, log);
            CheckReferences("Onboarding/PetSelectController.cs", "PetSelect", failures, log);
            CheckRecommendedDefault(failures, log);

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        private static void CheckRecommendedDefault(List<string> failures, StringBuilder log)
        {
            string flagsPath = Path.Combine(Application.dataPath, "_Modules/Core/FeatureFlags.cs");
            string choicePath = Path.Combine(Application.dataPath, "_Modules/Onboarding/FoundingChoiceController.cs");
            string flags = File.Exists(flagsPath) ? File.ReadAllText(flagsPath) : "";
            string choice = File.Exists(choicePath) ? File.ReadAllText(choicePath) : "";
            if (!flags.Contains("Get(\"defaulttown\", defaultOn: true)"))
                failures.Add("[founding-reach] Default Town is not default-on -- fresh players still fall into blank founding");
            // WO-1857: both CTA captions now resolve through LocalizedText, so these pin the
            // resolved KEY alongside the handler - the caption still has to say the right thing and
            // still has to route to the right place. The retired English captions are deliberately
            // not reproduced here; a needle matching its own comment asserts nothing.
            if (!choice.Contains("new LocalizedText(\"onboarding.founding_choice.ready_settlement\").Resolve()")
                || !choice.Contains("OnDefaultTown"))
                failures.Add("[founding-reach] recommended starter-settlement CTA is absent");
            if (!choice.Contains("new LocalizedText(\"onboarding.founding_choice.empty_realm\").Resolve()")
                || !choice.Contains("OnBuildYourOwn"))
                failures.Add("[founding-reach] blank-canvas secondary path is no longer exposed");
            if (!choice.Contains("BodyFillHorizontalOverscan") ||
                !choice.Contains("new Vector2(-BodyFillHorizontalOverscan, 0f)") ||
                !choice.Contains("new Vector2(1f + BodyFillHorizontalOverscan, 1f)"))
                failures.Add("[founding-reach] founding modal body fill no longer overscans beneath both frame shoulders; the previous screen can bleed through at the sides");
            string completionPath = Path.Combine(Application.dataPath, "_Modules/Village/BuildMode/StarterSettlementCompletion.cs");
            string layoutRes = Path.Combine(Application.dataPath, "Resources/Data/Canonical/starter-settlement-layout.json");
            string layoutStream = Path.Combine(Application.dataPath, "StreamingAssets/Data/Canonical/starter-settlement-layout.json");
            string completion = File.Exists(completionPath) ? File.ReadAllText(completionPath) : "";
            if (!completion.Contains("CanonicalJson.Read(LayoutRelativePath)") ||
                !completion.Contains("CatalogRegistry.Get(item.id)"))
                failures.Add("[founding-reach] ready-settlement layout no longer reads canonical ids and resolves their current catalog art");
            if (completion.Contains("private static readonly Entry[] Template"))
                failures.Add("[founding-reach] starter transforms returned to a hardcoded C# table instead of canonical data");
            if (!File.Exists(layoutRes) || !File.Exists(layoutStream))
                failures.Add("[founding-reach] starter-settlement-layout.json dual copies are missing");
            else if (!StructuralComparisons.StructuralEqualityComparer.Equals(
                         File.ReadAllBytes(layoutRes), File.ReadAllBytes(layoutStream)))
                failures.Add("[founding-reach] starter-settlement-layout.json Resources/StreamingAssets copies diverged");
            string structuresRes = Path.Combine(Application.dataPath, "Resources/Data/Canonical/structures-catalog.json");
            string structures = File.Exists(structuresRes) ? File.ReadAllText(structuresRes) : "";
            // ⭐ OWNER RULING 2026-09-02, verbatim: "one thing i hate is the changes to the archer
            // towers. can you bring my wooden towers i created in tripo?"
            //
            // THIS ASSERTION WAS INVERTED ON THAT RULING. It previously required the catalog to
            // contain Tower_Castle_Round / _Castle_Square / _Medieval_Big and called that family
            // "owner-approved" on the strength of a 2026-09-01 note in structures-catalog.json
            // claiming she had REJECTED the wooden watchtower look. She asked for the exact
            // opposite the next day, so that note was either mis-recorded or reversed - either way
            // a recorded art ruling in a data file is NOT automatically current, and this oracle
            // was enforcing the superseded half.
            //
            // The three Tower_Castle_* prefabs are POLYPERFECT pack assets. Hers are
            // Assets/StructureContent/Tower_Wooden_Watchtower{,_L2,_L3}.fbx - each carries a
            // sibling .fbx.tripo-extracted marker proving Tripo authorship.
            //
            // ⚠ AND NOTE WHY THE SWAP WAS INVISIBLE: a Synty re-wrap under
            // Assets/StructureContent/Synty/ reused her filenames verbatim while actually wrapping
            // SM_Bld_Castle_Wall_Tower_S/M/L_01 - stone castle wall towers wearing her asset's name -
            // which duplicated the Addressables address so resolution could pick either. That is
            // repaired (the Synty entries re-addressed to Structures/Synty_Tower_Castle_Wall_*), and
            // WO-1305 Part B covers the other 27 addresses with the same shape.
            if (!structures.Contains("Structures/Tower_Wooden_Watchtower") ||
                !structures.Contains("Structures/Tower_Wooden_Watchtower_L2") ||
                !structures.Contains("Structures/Tower_Wooden_Watchtower_L3"))
                failures.Add("[founding-reach] Archer Tower no longer uses the owner's Tripo wooden watchtower ladder (ruling 2026-09-02) - L1/L2/L3 must be Structures/Tower_Wooden_Watchtower{,_L2,_L3}");
            // Falsifiable in the other direction too: a silent revert to the Polyperfect stone
            // family must FAIL rather than pass by omission.
            if (structures.Contains("Structures/Tower_Castle_Round") ||
                structures.Contains("Structures/Tower_Castle_Square") ||
                structures.Contains("Structures/Tower_Medieval_Big"))
                failures.Add("[founding-reach] the Polyperfect stone tower family is back in the structures catalog - the owner reverted the Archer Tower to her Tripo wooden towers on 2026-09-02");
            log.AppendLine("  recommended starter settlement default + scratch secondary path checked");
        }

        private static void CheckReferences(string rel, string label, List<string> failures, StringBuilder log)
        {
            string path = Path.Combine(Application.dataPath, "_Modules", rel);
            if (!File.Exists(path)) { failures.Add($"[founding-reach] {label} source '{rel}' not found"); return; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add($"[founding-reach] {label} source unreadable ({ex.Message})"); return; }
            if (text.IndexOf("PresentOrContinue", StringComparison.Ordinal) < 0)
                failures.Add($"[founding-reach] {label} ('{rel}') does NOT reference PresentOrContinue -- the founding entry point is not wired into this route");
            else
                log.AppendLine($"  {label} references PresentOrContinue OK");
        }

        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(full, false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool InstallState(GameStateService svc, GameState state)
        {
            var f = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(svc, state);
            return SetGssInstance(svc);
        }

        private static bool SetGssInstance(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "FOUNDING_REACH_OK");
                return "FOUNDING REACH OK -- ShouldOffer true on a fresh save, false post-onboard, and HeroSelect+PetSelect wire PresentOrContinue";
            }
            string reason = "founding-reach: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "FOUNDING_REACH_FAIL: " + reason);
            return reason;
        }
    }
}
