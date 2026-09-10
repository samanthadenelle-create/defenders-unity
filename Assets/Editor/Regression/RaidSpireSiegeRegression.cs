// =============================================================================
// RaidSpireSiegeRegression [raid-spire-siege]  Marker: RAID_SPIRE_SIEGE_OK / _FAIL
// -----------------------------------------------------------------------------
// WO-1617. Pins the ONE-DECIDER siege exemption for the raid spire.
//
// THE DEFECT THIS EXISTS TO PREVENT (proven from captured data, not inferred):
//   Builds/raidbase-bake.log, 2026-09-09 bake of raider_camp_small -
//     "[RaidBaseGenerator] 'spire art 'tower_siege_tower'' imported FLAT
//      (h=2.6m vs 4.4m wide) - applied the -90 X FBX-flat correction so it stands up."
//     "[RaidBaseGenerator] SPIRE 'tower_siege_tower' placed at centre: 1200 HP,
//      14.4m tall, art='Structures/Ballista'."
//   A Ballista IS 2.6 m tall and 4.4 m wide. PlaceSpire's flat-FBX heuristic read
//   correctly-authored art as a fallen building, tipped it onto its edge, then
//   ScaleToHeight magnified that wrong axis to monument height - a 2.6 m machine
//   rendered as a 14.4 m sculpture lying on its side, at the centre of the first
//   raid the player ever sees. PlaceTowerProp already had the exemption twenty
//   lines away and PlaceSpire did not consult it.
//
// WHY A SOURCE-TEXT ORACLE. RaidBaseGenerator/RaidBaseDresser live in
// DeNelle.EditorWallTools; this suite lives in DeNelle.EditorRegression, whose
// .asmdef does NOT reference it (read at source 2026-09-09), so the predicate
// cannot be CALLED from here. RaidBaseLayoutRegression (WO-1607's oracle) is
// source-text for the same reason. A behavioural case would need the lead to add
// "DeNelle.EditorWallTools" to Assets/Editor/Regression/DeNelle.EditorRegression.asmdef,
// which is not this lane's file - that option is recorded in the WO-1617 RESULT.
// So: this suite proves the WIRING (one predicate, both placers, no copied ids);
// it does NOT prove the baked height. The bake log line is the height proof.
//
// No bake, no PlayMode, no JSON writes.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class RaidSpireSiegeRegression
    {
        private const string GeneratorSrc = "Assets/Editor/WallTools/RaidBaseGenerator.cs";
        private const string DresserSrc = "Assets/Editor/WallTools/RaidBaseDresser.cs";

        /// <summary>The one predicate's declaration. Exactly one of these may exist.</summary>
        private const string PredicateDecl = "static bool IsAuthoredSiegeMachine(";

        /// <summary>The two authored siege ids. They may appear ONLY inside the predicate.</summary>
        private static readonly string[] SiegeIdLiterals = { "\"tower_siege_tower\"", "\"tower_catapult\"" };

        // Named so this file carries EXACTLY ONE open-brace and ONE close-brace character
        // literal. CLAUDE.md sec.1's quality gate counts braces naively over the whole file;
        // a body-extracting oracle written with bare '{' / '}' comparisons trips it with a
        // false BRACE MISMATCH. One const each keeps the naive count honest.
        private const char OpenBrace = '{';
        private const char CloseBrace = '}';

        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? "RAID_SPIRE_SIEGE_OK :: " : "RAID_SPIRE_SIEGE_FAIL :: ") + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                string gen = TryRead(GeneratorSrc);
                string dress = TryRead(DresserSrc);
                if (gen == null) failures.Add("cannot read " + GeneratorSrc);
                if (dress == null) failures.Add("cannot read " + DresserSrc);
                if (gen != null && dress != null)
                {
                    CaseSpireConsultsThePredicate(gen, failures, notes);
                    CaseTowerPropStillGuarded(gen, failures, notes);
                    CaseExactlyOnePredicate(gen, dress, failures, notes);
                    CaseMonumentFitIsTunable(gen, failures, notes);
                    CaseSaturationIsReported(gen, failures, notes);
                    CaseScaleFactorBoundsAreTunable(gen, failures, notes);
                    CaseDresserRoutesSpireThroughTheDecider(gen, dress, failures, notes);
                }
            }
            catch (Exception ex)
            {
                failures.Add("threw: " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures);
                return false;
            }
            reason = "raid-spire-siege " + string.Join("; ", notes);
            return true;
        }

        // -- cases -------------------------------------------------------------

        /// <summary>
        /// THE RED CASE. At HEAD, PlaceSpire called EnsureUpright + the monument clamp
        /// unconditionally and never mentioned IsAuthoredSiegeMachine, so this fails.
        /// Mutation that re-reds it: delete the `IsAuthoredSiegeMachine(catalogId)` line
        /// from PlaceSpire (or drop the `if (!authoredSiege)` guard on the clamp).
        /// </summary>
        private static void CaseSpireConsultsThePredicate(string gen, List<string> failures, List<string> notes)
        {
            string body = MethodBody(gen, "private static RaidSpire PlaceSpire(");
            if (body == null) { failures.Add("[spire] cannot locate PlaceSpire in " + GeneratorSrc); return; }

            if (body.IndexOf("IsAuthoredSiegeMachine", StringComparison.Ordinal) < 0)
                failures.Add("[spire] PlaceSpire does not consult IsAuthoredSiegeMachine - a low, wide siege " +
                             "machine will be tipped -90 X by EnsureUpright and magnified to monument height " +
                             "(WO-1617; Builds/raidbase-bake.log recorded exactly that at 2.6m -> 14.4m).");

            if (body.IndexOf("EnsureUpright", StringComparison.Ordinal) >= 0 &&
                body.IndexOf("authoredSiege", StringComparison.Ordinal) < 0)
                failures.Add("[spire] PlaceSpire still calls EnsureUpright without a siege guard.");

            if (body.IndexOf("ResolveSpireArtId", StringComparison.Ordinal) < 0)
                failures.Add("[spire] PlaceSpire does not resolve its art id through ResolveSpireArtId - the spire " +
                             "slot could again be handed siege art straight from centralBuilding.");

            notes.Add("PlaceSpire guarded");
        }

        /// <summary>The exemption PlaceTowerProp already had must not regress (WO-1607 lineage).</summary>
        private static void CaseTowerPropStillGuarded(string gen, List<string> failures, List<string> notes)
        {
            string body = MethodBody(gen, "private static GameObject PlaceTowerProp(");
            if (body == null) { failures.Add("[turret] cannot locate PlaceTowerProp in " + GeneratorSrc); return; }

            if (body.IndexOf("!IsAuthoredSiegeMachine(", StringComparison.Ordinal) < 0)
                failures.Add("[turret] PlaceTowerProp lost its siege guard around EnsureUpright.");

            notes.Add("PlaceTowerProp guarded");
        }

        /// <summary>
        /// ONE decider, one definition, and the two ids live ONLY inside it. A second copy is
        /// how the spire came to disagree with the turret in the first place, so a copy is the
        /// failure - not merely untidy (docs/ARCHITECTURE_PRINCIPLES.md, one owner per concern).
        /// </summary>
        private static void CaseExactlyOnePredicate(string gen, string dress, List<string> failures, List<string> notes)
        {
            int decls = Count(gen, PredicateDecl) + Count(dress, PredicateDecl);
            if (decls != 1)
                failures.Add("[one-decider] expected exactly ONE '" + PredicateDecl + "' across the generator + " +
                             "dresser, found " + decls + ". Do not fork the predicate - both placers and the " +
                             "dresser call the same one.");

            string predicate = MethodBody(gen, PredicateDecl);
            for (int i = 0; i < SiegeIdLiterals.Length; i++)
            {
                string lit = SiegeIdLiterals[i];
                int inGen = Count(gen, lit);
                int inPredicate = predicate != null ? Count(predicate, lit) : 0;
                if (inGen - inPredicate > 0)
                    failures.Add("[one-decider] the id literal " + lit + " appears " + (inGen - inPredicate) +
                                 " time(s) in " + GeneratorSrc + " OUTSIDE IsAuthoredSiegeMachine. Call the " +
                                 "predicate instead of restating the id.");
                if (Count(dress, lit) > 0)
                    failures.Add("[one-decider] the id literal " + lit + " appears in " + DresserSrc +
                                 " - the dresser must ask the predicate, never carry a copy of the ids.");
            }

            notes.Add("one predicate, ids not copied");
        }

        /// <summary>
        /// The monument fit survives as a TUNABLE with today's values as defaults. WO-1617
        /// changed WHO the fit applies to, never the numbers, and WO-1619 step 2 did not move
        /// them either - it removed the factor ceiling that was overriding them.
        ///
        /// The "must still land on 8.0 m" note this doc used to carry is RETIRED: 8.0 m was
        /// never the target, it was the SATURATION (Builds/wave2-bake, 2026-09-10, all three
        /// baked configs: target=14.40m appliedFactor=8.000 achieved=8.02m saturatedAt=UPPER).
        /// The height these tunables ask for is 14.40 m and it always was.
        /// </summary>
        private static void CaseMonumentFitIsTunable(string gen, List<string> failures, List<string> notes)
        {
            string[] tunables =
            {
                "SpireMonumentMultiplier = 1.6f",
                "SpireMonumentMinHeight = 8f",
                "SpireMonumentMaxHeight = 18f",
            };
            for (int i = 0; i < tunables.Length; i++)
                if (gen.IndexOf(tunables[i], StringComparison.Ordinal) < 0)
                    failures.Add("[tunable] missing '" + tunables[i] + "' - the monument fit must be a named " +
                                 "tunable carrying today's value, not a bare literal (WO-1617).");

            string body = MethodBody(gen, "private static RaidSpire PlaceSpire(");
            if (body != null && body.IndexOf("* 1.6f, 8f, 18f", StringComparison.Ordinal) >= 0)
                failures.Add("[tunable] PlaceSpire still hardcodes the 1.6f / 8f / 18f monument clamp.");

            notes.Add("monument fit tunable");
        }

        /// <summary>
        /// WO-1619 STEP 1. The monument fit is allowed to fail; it is NOT allowed to fail
        /// SILENTLY. ScaleToHeight has always returned the height it actually achieved, and
        /// nothing ever compared that to the target - so a spire landing at 8.0 m against a
        /// 14.4 m target logged identically to one that landed on its target (the 2026-09-09
        /// bake in Builds/raidbase-bake.log recorded exactly that, twice, with no warning).
        ///
        /// RED AT BASE e225ca57b: ScaleToHeight's body carried no Debug call of any kind and
        /// PlaceSpire called it with two arguments.
        /// MUTATION THAT RE-REDS IT: delete the LogWarning saturation branch from ScaleToHeight
        /// (case (a) fails), or drop the label argument at the PlaceSpire call site so the fit
        /// goes back to reporting nothing identifiable (case (b) fails).
        ///
        /// THE BOUNDS ARE PINNED NEXT DOOR, NOT HERE: CaseScaleFactorBoundsAreTunable is
        /// WO-1619 step 2's case and it landed with step 2's edit, exactly as this doc said it
        /// would. This case still owns only the REPORTING.
        /// </summary>
        private static void CaseSaturationIsReported(string gen, List<string> failures, List<string> notes)
        {
            string fit = MethodBody(gen, "private static float ScaleToHeight(");
            if (fit == null) { failures.Add("[saturation] cannot locate ScaleToHeight in " + GeneratorSrc); return; }

            if (fit.IndexOf("Debug.LogWarning", StringComparison.Ordinal) < 0)
                failures.Add("[saturation] ScaleToHeight has no Debug.LogWarning branch - a fit that " +
                             "saturates at a clamp bound would again be indistinguishable from a fit " +
                             "that reached its target (WO-1619).");

            if (fit.IndexOf("saturat", StringComparison.Ordinal) < 0 &&
                fit.IndexOf("SATURAT", StringComparison.Ordinal) < 0)
                failures.Add("[saturation] ScaleToHeight never names saturation - the log line must say " +
                             "which bound it hit, not merely print a height (WO-1619).");

            string body = MethodBody(gen, "private static RaidSpire PlaceSpire(");
            if (body == null) { failures.Add("[saturation] cannot locate PlaceSpire in " + GeneratorSrc); return; }

            if (body.IndexOf("ScaleToHeight(go, targetHeight,", StringComparison.Ordinal) < 0)
                failures.Add("[saturation] PlaceSpire no longer passes a label to ScaleToHeight - " +
                             "BuildAllRaidScenes bakes several configs per run, so an unlabelled fit line " +
                             "cannot be attributed to a scene (WO-1619).");

            notes.Add("fit saturation reported");
        }

        /// <summary>
        /// WO-1619 STEP 2. The scale-factor bounds inside ScaleToHeight are NAMED TUNABLES, and
        /// the upper one is NOT ALLOWED TO BE THE AUTHORITY ON MONUMENT HEIGHT.
        ///
        /// WHAT WENT WRONG (measured, not inferred - Builds/wave2-bake, 2026-09-10, printed
        /// identically for raider_camp_small, fortified_garrison and mage_enclave):
        ///   "rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m wantedFactor=14.366
        ///    appliedFactor=8.000 saturatedAt=UPPER achieved=8.02m (56% of target) - SATURATED"
        /// The monument clamp asked for 14.40 m on every raid the game ships and a bare 8f
        /// literal inside Mathf.Clamp handed back 8.02 m. SpireMonumentMinHeight /
        /// SpireMonumentMaxHeight own "how tall should this be"; the factor bound's job is
        /// refusing to magnify degenerate art, and it must not silently cap a monument.
        ///
        /// THE INVARIANT, and why it is a comparison rather than a hardcoded 24: the achieved
        /// height can never exceed SpireMonumentMaxHeight anyway, because the TARGET is clamped
        /// there before the fit runs. So the ceiling is non-binding for one-metre-class art
        /// exactly when it is at least SpireMonumentMaxHeight (18 / 1.002 = 17.96 is the largest
        /// factor today's measured art can need). Pinning that relation lets the lead retune the
        /// margin in one edit without touching this suite, and still reds the regression.
        ///
        /// RED AT BASE 406dbda07: ScaleToHeight read "Mathf.Clamp(wanted, 0.2f, 8f)" with both
        /// bounds inline and neither const existed.
        /// MUTATION THAT RE-REDS IT: re-inline either bound at the Mathf.Clamp call site, or set
        /// SpireFitFactorMax below SpireMonumentMaxHeight (e.g. back to 8f).
        /// </summary>
        private static void CaseScaleFactorBoundsAreTunable(string gen, List<string> failures, List<string> notes)
        {
            string[] decls =
            {
                "internal const float SpireFitFactorMin =",
                "internal const float SpireFitFactorMax =",
            };
            for (int i = 0; i < decls.Length; i++)
                if (gen.IndexOf(decls[i], StringComparison.Ordinal) < 0)
                    failures.Add("[fitbounds] missing '" + decls[i] + "' - ScaleToHeight's clamp bounds must be " +
                                 "named tunables declared beside SpireMonumentMultiplier, not bare literals " +
                                 "inside Mathf.Clamp (WO-1619 sec.3: a magic literal that survives a ticket " +
                                 "about a magic literal is the ticket failing).");

            string fit = MethodBody(gen, "private static float ScaleToHeight(");
            if (fit == null) { failures.Add("[fitbounds] cannot locate ScaleToHeight in " + GeneratorSrc); return; }

            if (fit.IndexOf("Mathf.Clamp(wanted, SpireFitFactorMin, SpireFitFactorMax)", StringComparison.Ordinal) < 0)
                failures.Add("[fitbounds] ScaleToHeight does not clamp through SpireFitFactorMin/SpireFitFactorMax - " +
                             "the fit path must read its bounds from the tunables (WO-1619).");

            if (fit.IndexOf(", 0.2f, 8f)", StringComparison.Ordinal) >= 0)
                failures.Add("[fitbounds] ScaleToHeight still carries the inline ', 0.2f, 8f)' clamp bounds - this " +
                             "is the literal that silently capped every raid spire at 8.02m against a 14.40m " +
                             "target (Builds/wave2-bake, 2026-09-10).");

            float ceiling = ConstFloat(gen, "SpireFitFactorMax");
            float monument = ConstFloat(gen, "SpireMonumentMaxHeight");
            if (ceiling <= 0f)
                failures.Add("[fitbounds] cannot parse SpireFitFactorMax's value out of " + GeneratorSrc);
            else if (monument <= 0f)
                failures.Add("[fitbounds] cannot parse SpireMonumentMaxHeight's value out of " + GeneratorSrc);
            else if (ceiling < monument)
                failures.Add("[fitbounds] SpireFitFactorMax (" + ceiling.ToString("0.###") + ") is below " +
                             "SpireMonumentMaxHeight (" + monument.ToString("0.###") + "), so the factor ceiling " +
                             "is once again the authority on monument height: one-metre-class art can no longer " +
                             "reach the tallest target the monument clamp is allowed to ask for. The measured " +
                             "spire art renders at 1.002m (Builds/wave2-bake, 2026-09-10) and needs a factor of " +
                             "17.96 at an 18m target (WO-1619 sec.3).");

            notes.Add("fit factor bounds tunable");
        }

        /// <summary>
        /// The spire slot's ART must come from the same decider the generator used, so the model
        /// the generator MEASURED and the model the dresser INSTANTIATES agree (ReplaceChildrenWith
        /// swaps the mesh but inherits the host's fitted localScale).
        /// </summary>
        private static void CaseDresserRoutesSpireThroughTheDecider(string gen, string dress,
                                                                   List<string> failures, List<string> notes)
        {
            if (gen.IndexOf("internal static string ResolveSpireArtId(", StringComparison.Ordinal) < 0)
                failures.Add("[dresser] RaidBaseGenerator.ResolveSpireArtId is missing or not reachable from the " +
                             "dresser (it must be internal, same assembly DeNelle.EditorWallTools).");

            string map = MethodBody(dress, "private static string MapCatalogArt(");
            if (map == null) { failures.Add("[dresser] cannot locate MapCatalogArt in " + DresserSrc); return; }

            if (map.IndexOf("RaidBaseGenerator.ResolveSpireArtId(", StringComparison.Ordinal) < 0)
                failures.Add("[dresser] MapCatalogArt does not route its id through " +
                             "RaidBaseGenerator.ResolveSpireArtId. Its ONLY caller is the spire slot, so a " +
                             "siege id reaching the token map hands the camp's centrepiece a Ballista - which " +
                             "is what shipped ('art=Structures/Ballista' in Builds/raidbase-bake.log).");

            int resolveAt = map.IndexOf("ResolveSpireArtId(", StringComparison.Ordinal);
            int ballistaAt = map.IndexOf("\"Ballista\"", StringComparison.Ordinal);
            if (resolveAt >= 0 && ballistaAt >= 0 && ballistaAt < resolveAt)
                failures.Add("[dresser] MapCatalogArt returns \"Ballista\" BEFORE it resolves the spire art id - " +
                             "the guard must run first or it guards nothing.");

            notes.Add("dresser routes through the decider");
        }

        // -- helpers -----------------------------------------------------------

        /// <summary>
        /// Brace-matched body of the method whose declaration contains <paramref name="signature"/>.
        /// Returns null when the signature is absent (the caller reports that as a failure rather
        /// than silently passing - a missing method must never read as a green case).
        /// </summary>
        private static string MethodBody(string src, string signature)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int at = src.IndexOf(signature, StringComparison.Ordinal);
            if (at < 0) return null;
            int open = src.IndexOf(OpenBrace, at);
            if (open < 0) return null;

            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                char c = src[i];
                if (c == OpenBrace) depth++;
                else if (c == CloseBrace)
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }

        /// <summary>
        /// Value of a `const float &lt;name&gt; = &lt;literal&gt;f;` declaration, read out of the source
        /// text. Returns 0f when the declaration is absent or unparseable, which every caller
        /// reports as a failure - a value this oracle cannot read must never pass as satisfied.
        /// Invariant-comparing two tunables beats hardcoding either of their values here
        /// (WO-1619: this suite exists because a hardcoded number decided a height).
        /// </summary>
        private static float ConstFloat(string src, string name)
        {
            if (string.IsNullOrEmpty(src)) return 0f;
            int at = src.IndexOf("const float " + name + " =", StringComparison.Ordinal);
            if (at < 0) return 0f;
            int eq = src.IndexOf('=', at);
            if (eq < 0) return 0f;
            int end = src.IndexOf(';', eq);
            if (end < 0) return 0f;

            string lit = src.Substring(eq + 1, end - eq - 1).Trim();
            if (lit.EndsWith("f", StringComparison.OrdinalIgnoreCase)) lit = lit.Substring(0, lit.Length - 1);
            return float.TryParse(lit, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        private static int Count(string src, string needle)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, at = 0;
            while ((at = src.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }
    }
}
