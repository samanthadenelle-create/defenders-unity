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
        /// The monument fit survives as a TUNABLE with today's values as defaults. The ticket
        /// changes WHO the fit applies to, never the numbers - the arcane-spire bake must still
        /// land on 8.0 m (Builds/raidbase-bake.log).
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
