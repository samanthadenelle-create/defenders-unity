// =============================================================================
// VfxPickOverrideRegression [vfx-pick-override]  -- WO-1348
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// -----------------------------------------------------------------------------
// WHAT THIS SUITE IS FOR, IN ONE SENTENCE
// -----------------------------------------------------------------------------
// It makes it IMPOSSIBLE for a database row to change what an OFFLINE player sees,
// and impossible for the Command Center to offer a VFX pick this build cannot
// actually render.
//
// Both failures are SILENT, which is why prose was never going to be enough:
//
//   * "No row, no network, no parse => today's behaviour, byte for byte" is asserted
//     in capitals in RemoteTunables.cs and again in VfxPickOverrides.cs. PROSE DOES
//     NOT GO RED. If a future edit makes the override layer answer something other
//     than the build-time pick on an empty table, nothing crashes - the game just
//     quietly stops being the build that shipped.
//
//   * A picker offering an unshipped prefab reproduces CLAUDE.md section 16's exact
//     failure in a new place: the build installs, launches, plays, and renders
//     NOTHING NEW, with no error on screen. That has already cost this project
//     THREE separate incidents. The only defence is that every option, by
//     construction, names a key this build's catalog already resolves - and that is
//     a claim a suite can check and a comment cannot.
//
// -----------------------------------------------------------------------------
// WHAT IT PINS
//   1. THE INVARIANT - an empty / malformed / readOk=false table leaves every
//      catalog key resolving to EXACTLY the row it resolves to with no override
//      layer at all. Asserted over the WHOLE catalog, not a sample.
//   2. Every offered option resolves to a real catalog row with a real prefab.
//   3. An id the pool does not carry FALLS BACK to the build-time pick.
//   4. A valid pick actually re-points the prefab (the feature works at all).
//   5. KEY CREATION - a key with no build-time row gains one from a pick.
//   6. A loop/one-shot mismatch is REFUSED, not papered over.
//   7. Every realm.vfx.* knob is registered Int with default 0, and 0 is the only
//      value that can mean "the build-time pick".
//   8. The two generated option-pool copies are byte-identical (console vs build).
//
// ⛔ If case 1 goes RED the correct response is NEVER to relax the assert. It is the
// acceptance criterion the work order states twice, and the owner's offline players
// are the ones who pay for it.
//
// ASCII only. Never throws - a suite that dies takes the whole gate with it.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Core.Ops;
using DeNelle.Core.Vfx;
using DeNelle.Village;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class VfxPickOverrideRegression
    {
        private const string CatalogPath = "Assets/Resources/VFX/HovlVfxCatalog.asset";
        private const string ApiOptionsPath = "api/_lib/vfx-pick-options.generated.json";
        private const string ResOptionsPath = "Assets/Resources/VFX/vfx-pick-options.generated.json";
        private const string OverridesSrc = "Assets/_Modules/Core/VFX/VfxPickOverrides.cs";

        /// <summary>
        /// The key WO-1348 must be able to CREATE: it is deliberately absent from
        /// Assets/Editor/VfxManualPicks.json (NightStoreAuraSelectionRegression pins that its
        /// row stays deleted, because the VFX Caster invented it in the owner's name), so it
        /// renders NOTHING today. If a pick cannot give it a prefab, half the ticket is lost.
        /// </summary>
        private const string CreationKey = "atfootprintoftree_Impact";

        /// <summary>
        /// The catalog key three cases drive the override machinery through. It is a FIXTURE, not a
        /// convenience: the owner tagged it in Assets/Editor/VfxManualPicks.json and the generator
        /// bakes it into the catalog, so its absence is a real defect in one of those two - never a
        /// reason for a case to stand down having asserted nothing.
        /// </summary>
        private const string FixtureKey = "EliteDeath_Impact";

        /// <summary>
        /// The one sentence every missing-fixture arm says. ⛔ A guard that returns on an absent
        /// fixture MUST fail rather than pass quietly: a suite that greens because its inputs
        /// vanished is worse than no suite, because it reports coverage it does not have.
        /// </summary>
        private static string MissingFixture(string caseTag, string key)
        {
            return "[" + caseTag + "] the fixture key '" + key + "' has no row, or no prefab, in " +
                   CatalogPath + ". It IS in the owner's Assets/Editor/VfxManualPicks.json, so either " +
                   "the catalog is stale (run Defenders/VFX/Generate Hovl VFX Catalog) or the pack " +
                   "holding its prefab is not imported. This case asserted NOTHING - it is reported as " +
                   "a FAILURE rather than a pass, because a green on absent inputs is a lie about " +
                   "coverage.";
        }

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("VFX_PICK_OVERRIDE_OK - " + reason);
            else Debug.LogError("VFX_PICK_OVERRIDE_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            int catalogRows = 0, optionCount = 0;

            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<HovlVfxCatalog>(CatalogPath);
                if (catalog == null)
                {
                    reason = "[catalog] " + CatalogPath + " did not load. Every case below reads it, " +
                             "so the suite cannot answer. Run Defenders/VFX/Generate Hovl VFX Catalog.";
                    return false;
                }
                catalog.BuildLookup();
                catalogRows = catalog.Rows == null ? 0 : catalog.Rows.Length;

                var options = LoadOptions(failures);
                optionCount = options.Count;

                Case(failures, "registry-shape", () => Case1_RegistryShape(failures));
                Case(failures, "no-row", () => Case2_NoRowInvariant(failures, catalog));
                Case(failures, "options-shipped", () => Case3_EveryOptionIsShipped(failures, catalog, options));
                Case(failures, "unknown-id", () => Case4_UnknownIdFallsBack(failures, catalog));
                Case(failures, "pick-applies", () => Case5_PickApplies(failures, catalog, options));
                Case(failures, "key-creation", () => Case6_KeyCreation(failures, catalog, options));
                Case(failures, "loop-mismatch", () => Case7_LoopMismatchRefused(failures, catalog, options));
                Case(failures, "pool-copies", () => Case8_PoolCopiesAreOneFile(failures));
                Case(failures, "instrumented", () => Case9_TheSeamIsInstrumented(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // Never leave a mutated table or a standing snapshot behind for the next suite.
                RemoteTunables.Clear();
                VfxPickOverrides.ResetForTests();
            }

            if (failures.Count == 0)
            {
                reason = "VFX PICK OVERRIDE OK - " + catalogRows + " catalog row(s) and " + optionCount +
                         " shipped option(s). An EMPTY / MALFORMED / readOk=false tunables table leaves " +
                         "EVERY catalog key resolving to exactly the prefab this build baked from " +
                         "Assets/Editor/VfxManualPicks.json, which stays the default and the record. " +
                         "Every option the Command Center can offer resolves to a real catalog row with " +
                         "a real prefab, so a pick can never name art that was not shipped. An id the " +
                         "pool does not carry falls back and traces the reason; a loop/one-shot mismatch " +
                         "is refused rather than handed to a call site that cannot stop it; a valid pick " +
                         "re-points the prefab; and a key with NO build-time row ('" + CreationKey +
                         "') gains one from a pick, which is the ticket's key-CREATION criterion. Both " +
                         "generated option-pool copies are byte-identical.";
                return true;
            }

            reason = string.Join(" | ", failures);
            return false;
        }

        // =====================================================================
        //  CASES
        // =====================================================================

        /// <summary>Every realm.vfx.* knob is an Int shipping at 0 - the ONLY "no override" value.</summary>
        private static void Case1_RegistryShape(List<string> failures)
        {
            int seen = 0;
            foreach (var spec in RemoteTunables.Registry)
            {
                if (spec == null || spec.Key == null ||
                    !spec.Key.StartsWith(VfxPickOverrides.TunablePrefix, StringComparison.Ordinal)) continue;
                seen++;

                if (spec.Kind != TunableKind.Int)
                    failures.Add("[registry-shape] '" + spec.Key + "' is " + spec.Kind + ", not Int. The " +
                                 "value is an OPTION ID; a bool cannot carry one.");
                if (spec.Default != VfxPickOverrides.BuildDefaultId)
                    failures.Add("[registry-shape] THE INVARIANT IS BROKEN: '" + spec.Key + "' ships at " +
                                 spec.Default + ", not " + VfxPickOverrides.BuildDefaultId + ". Only 0 means " +
                                 "'use the build-time pick', so any other default makes an EMPTY table change " +
                                 "what an offline player sees - the one thing RemoteTunables.cs forbids in capitals.");

                string vfxKey = spec.Key.Substring(VfxPickOverrides.TunablePrefix.Length);
                if (string.IsNullOrEmpty(vfxKey))
                    failures.Add("[registry-shape] '" + spec.Key + "' carries no VFX key after the prefix.");
            }

            if (seen < 4)
                failures.Add("[registry-shape] expected at least the FOUR WO-1348 pick keys in " +
                             "RemoteTunables.Registry, found " + seen + ". The four are the tags the owner " +
                             "could not correct on 2026-09-03 without a rebuild.");
        }

        /// <summary>
        /// THE ACCEPTANCE CRITERION. With no row / a malformed payload / readOk=false, EVERY key in
        /// the catalog must resolve to EXACTLY the row the raw Rows array holds for it.
        /// </summary>
        private static void Case2_NoRowInvariant(List<string> failures, HovlVfxCatalog catalog)
        {
            RemoteTunables.Clear();
            VfxPickOverrides.ResetForTests();
            VfxPickOverrides.Snapshot("regression:no-row");
            AssertBuildTimePicks(failures, catalog, "no row (table cleared - offline, 404, or empty table)");

            // A body that does not parse at all.
            RemoteTunables.ApplyPayload("{not json at all", RemoteTunables.ProvenanceRemote);
            VfxPickOverrides.Snapshot("regression:malformed");
            AssertBuildTimePicks(failures, catalog, "malformed payload");

            // The server answering honestly that it could not read the table.
            RemoteTunables.ApplyPayload("{\"version\":1,\"readOk\":false,\"reason\":\"db down\",\"values\":{}}",
                RemoteTunables.ProvenanceRemote);
            VfxPickOverrides.Snapshot("regression:readOk-false");
            AssertBuildTimePicks(failures, catalog, "readOk=false");

            // A well-formed payload that simply carries no VFX rows.
            RemoteTunables.ApplyPayload("{\"version\":1,\"readOk\":true,\"values\":{}}",
                RemoteTunables.ProvenanceRemote);
            VfxPickOverrides.Snapshot("regression:empty-values");
            AssertBuildTimePicks(failures, catalog, "empty values map");

            // And an EXPLICIT 0, which must be identical to no row at all.
            ApplyPicks("{\"" + VfxPickOverrides.TunablePrefix + "EliteDeath_Impact\":\"0\"}");
            VfxPickOverrides.Snapshot("regression:explicit-zero");
            AssertBuildTimePicks(failures, catalog, "an explicit 0 row");

            RemoteTunables.Clear();
            VfxPickOverrides.ResetForTests();
        }

        /// <summary>Every offered option names a catalog key that carries a real prefab.</summary>
        private static void Case3_EveryOptionIsShipped(List<string> failures, HovlVfxCatalog catalog,
                                                       List<OptionRow> options)
        {
            if (options.Count == 0)
            {
                failures.Add("[options-shipped] the option pool is EMPTY (" + ResOptionsPath + "). The " +
                             "Command Center would offer nothing. Run: node tools/gen-vfx-pick-options.mjs");
                return;
            }

            int missing = 0, offered = 0, heldBack = 0;
            foreach (var opt in options)
            {
                // Held back and NOT offered: a retired key left her tag file, and an unresolved key
                // is still tagged but has no row this build can render. Both keep their ids reserved
                // so a future key can never inherit one and silently re-point a row she already set.
                if (opt.retired || opt.unresolved)
                {
                    heldBack++;
                    // ASSERT THE ABSENCE rather than trusting the flag: an entry marked unresolved
                    // that the catalog CAN resolve means the generator's parse has drifted from the
                    // asset format, and the picker would be short a pick she is entitled to.
                    if (opt.unresolved && TryGetRawRow(catalog, opt.key, out var back) && back.Prefab != null)
                        failures.Add("[options-shipped] option id " + opt.id + " ('" + opt.key + "') is " +
                                     "marked unresolved, but " + CatalogPath + " resolves it perfectly " +
                                     "well. The generator's catalog parse has drifted from the asset " +
                                     "format and is withholding a pick she should be offered. Re-run: " +
                                     "node tools/gen-vfx-pick-options.mjs");
                    continue;
                }

                offered++;
                if (!TryGetRawRow(catalog, opt.key, out var row) || row.Prefab == null)
                {
                    missing++;
                    if (missing <= 8)
                        failures.Add("[options-shipped] option id " + opt.id + " names catalog key '" +
                                     opt.key + "', which has NO row or NO prefab in " + CatalogPath +
                                     ". Offering it would let a pick point at art this build cannot " +
                                     "resolve - CLAUDE.md section 16, three incidents. The generator is " +
                                     "supposed to mark it unresolved: node tools/gen-vfx-pick-options.mjs");
                }
            }
            if (missing > 8)
                failures.Add("[options-shipped] ... and " + (missing - 8) + " more unresolvable option(s).");

            if (offered == 0)
                failures.Add("[options-shipped] the pool holds " + options.Count + " entr(ies) but NOT ONE " +
                             "is offerable (" + heldBack + " held back as retired/unresolved). The picker " +
                             "would be empty, which reads as a broken feature rather than a missing pack.");
        }

        /// <summary>An id the pool does not carry must fall back to the build-time pick.</summary>
        private static void Case4_UnknownIdFallsBack(List<string> failures, HovlVfxCatalog catalog)
        {
            const string key = FixtureKey;
            if (!TryGetRawRow(catalog, key, out var baseRow))
            {
                failures.Add(MissingFixture("unknown-id", key));
                return;
            }

            ApplyPicks("{\"" + VfxPickOverrides.TunablePrefix + key + "\":\"999999\"}");
            VfxPickOverrides.Snapshot("regression:unknown-id");

            if (!catalog.TryGet(key, out var got) || got.Prefab != baseRow.Prefab)
                failures.Add("[unknown-id] a row naming option 999999 (which no build carries) did NOT " +
                             "fall back to the build-time pick for '" + key + "'. An unresolvable pick " +
                             "must render the shipped effect and say why in the trace - never nothing.");

            RemoteTunables.Clear();
            VfxPickOverrides.ResetForTests();
        }

        /// <summary>A valid pick actually re-points the prefab. Without this, nothing else matters.</summary>
        private static void Case5_PickApplies(List<string> failures, HovlVfxCatalog catalog,
                                              List<OptionRow> options)
        {
            const string key = FixtureKey;
            if (!TryGetRawRow(catalog, key, out var baseRow) || baseRow.Prefab == null)
            {
                failures.Add(MissingFixture("pick-applies", key));
                return;
            }

            var alt = FindOption(options, catalog, key, baseRow.IsLoop, differentPrefabFrom: baseRow.Prefab);
            if (alt == null)
            {
                failures.Add("[pick-applies] no option in the pool differs from '" + key + "'s own prefab " +
                             "with matching loop-ness, so the feature cannot be exercised at all.");
                return;
            }

            ApplyPicks("{\"" + VfxPickOverrides.TunablePrefix + key + "\":\"" + alt.id + "\"}");
            VfxPickOverrides.Snapshot("regression:pick-applies");

            if (!catalog.TryGet(key, out var got))
                failures.Add("[pick-applies] '" + key + "' stopped resolving at all under a valid pick.");
            else if (got.Prefab == baseRow.Prefab)
                failures.Add("[pick-applies] option id " + alt.id + " ('" + alt.key + "') did NOT re-point '" +
                             key + "' - it still renders the build-time prefab. The whole feature is inert.");
            else if (got.Key != key)
                failures.Add("[pick-applies] the overridden row came back keyed '" + got.Key + "' instead of '" +
                             key + "' - a caller keying its pool by row.Key would pool it under the wrong name.");

            RemoteTunables.Clear();
            VfxPickOverrides.ResetForTests();
        }

        /// <summary>KEY CREATION - a key with no build-time row gains one from a pick.</summary>
        private static void Case6_KeyCreation(List<string> failures, HovlVfxCatalog catalog,
                                              List<OptionRow> options)
        {
            RemoteTunables.Clear();
            VfxPickOverrides.ResetForTests();
            VfxPickOverrides.Snapshot("regression:creation-baseline");

            // ASSERT THE ABSENCE. '<CreationKey>' having no build-time row is not a missing fixture -
            // it is the STATE UNDER TEST, and NightStoreAuraSelectionRegression pins that its row
            // stays deleted (the VFX Caster invented it in her name and she removed it). So the
            // baseline is an assertion in its own right, not a precondition to stand down on.
            if (catalog.TryGet(CreationKey, out _))
            {
                failures.Add("[key-creation] '" + CreationKey + "' now HAS a build-time row in " +
                             CatalogPath + ". NightStoreAuraSelectionRegression pins that row as " +
                             "DELETED - the VFX Caster invented the key in the owner's name and she " +
                             "removed it - so its return is either that defect recurring or a real " +
                             "re-tag that needs her ruling. Either way the CREATION path can no longer " +
                             "be proven from a genuinely empty slot. Ask her; do not edit the tag file " +
                             "to match this suite, and do not relax this into a skip.");
                return;
            }

            var alt = FindOption(options, catalog, CreationKey, wantLoop: false, differentPrefabFrom: null);
            if (alt == null)
            {
                failures.Add("[key-creation] no one-shot option available to create '" + CreationKey + "'.");
                return;
            }

            ApplyPicks("{\"" + VfxPickOverrides.TunablePrefix + CreationKey + "\":\"" + alt.id + "\"}");
            VfxPickOverrides.Snapshot("regression:creation");

            if (!catalog.TryGet(CreationKey, out var made) || made.Prefab == null)
                failures.Add("[key-creation] a pick did NOT create '" + CreationKey + "'. WO-1348 states " +
                             "this outright: a design that can only override an EXISTING key cannot give " +
                             "her the tag she still needs, and half the motivation is lost.");
            else if (made.Key != CreationKey)
                failures.Add("[key-creation] the created row came back keyed '" + made.Key + "'.");

            RemoteTunables.Clear();
            VfxPickOverrides.ResetForTests();
        }

        /// <summary>A loop/one-shot mismatch is refused, not silently honoured.</summary>
        private static void Case7_LoopMismatchRefused(List<string> failures, HovlVfxCatalog catalog,
                                                      List<OptionRow> options)
        {
            const string key = FixtureKey;                       // a ONESHOT slot
            if (!TryGetRawRow(catalog, key, out var baseRow) || baseRow.Prefab == null)
            {
                failures.Add(MissingFixture("loop-mismatch", key));
                return;
            }

            // ASSERT THE ABSENCE rather than standing down on it. '<FixtureKey>' being a ONESHOT is
            // not an incidental property of the fixture - it is canon: NightStoreAuraSelectionRegression
            // records her tag as isLoop=false with the reason "a death burst must not hold a loop slot -
            // that is correct, not a bug to fix". If it ever becomes a loop, the thing to do is say so
            // loudly, not to quietly stop testing the mismatch path.
            if (baseRow.IsLoop)
            {
                failures.Add("[loop-mismatch] the fixture key '" + key + "' is now a LOOP in " +
                             CatalogPath + ". It is canon that a death burst is a ONESHOT (her tag is " +
                             "isLoop=false), so this is either a real re-tag that needs her ruling or a " +
                             "generator defect - and either way this case can no longer prove that a " +
                             "loop option is refused for a oneshot slot. Do not relax this into a skip.");
                return;
            }

            var loopOpt = FindOption(options, catalog, key, wantLoop: true, differentPrefabFrom: baseRow.Prefab);
            if (loopOpt == null)
            {
                // Also an assertion, not a stand-down: the owner has tagged many looping '*_Aura'
                // keys, so a pool carrying NOT ONE resolvable loop means the pool is broken (or the
                // generator dropped every loop), which is exactly what this suite exists to catch.
                failures.Add("[loop-mismatch] the shipped option pool carries NO resolvable LOOP option " +
                             "at all (" + options.Count + " option(s) read from " + ResOptionsPath + "). " +
                             "The owner has tagged many looping '*_Aura' keys, so an empty loop set means " +
                             "the pool or the generator is broken, and the loop/oneshot refusal path " +
                             "cannot be exercised. Regenerate: node tools/gen-vfx-pick-options.mjs");
                return;
            }

            ApplyPicks("{\"" + VfxPickOverrides.TunablePrefix + key + "\":\"" + loopOpt.id + "\"}");
            VfxPickOverrides.Snapshot("regression:loop-mismatch");

            if (!catalog.TryGet(key, out var got) || got.Prefab != baseRow.Prefab)
                failures.Add("[loop-mismatch] a LOOP option (id " + loopOpt.id + ", '" + loopOpt.key + "') was " +
                             "honoured for the ONESHOT key '" + key + "'. The call site would receive an " +
                             "effect it never returns to the pool - a leak that reads as 'random vfx stuck " +
                             "around', which the owner has already flagged once.");

            RemoteTunables.Clear();
            VfxPickOverrides.ResetForTests();
        }

        /// <summary>The console copy and the build copy of the pool are one file.</summary>
        private static void Case8_PoolCopiesAreOneFile(List<string> failures)
        {
            if (!File.Exists(ApiOptionsPath) || !File.Exists(ResOptionsPath))
            {
                failures.Add("[pool-copies] one of the two generated pools is missing (" + ApiOptionsPath +
                             " / " + ResOptionsPath + "). Run: node tools/gen-vfx-pick-options.mjs");
                return;
            }
            byte[] a = File.ReadAllBytes(ApiOptionsPath);
            byte[] b = File.ReadAllBytes(ResOptionsPath);
            if (a.Length != b.Length)
            {
                failures.Add("[pool-copies] the console copy is " + a.Length + " bytes and the build copy is " +
                             b.Length + ". The Command Center would offer picks the build resolves differently.");
                return;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                failures.Add("[pool-copies] the two copies diverge at byte " + i + ". Run: node " +
                             "tools/gen-vfx-pick-options.mjs");
                return;
            }
            foreach (byte by in b)
            {
                if (by != 0) continue;
                failures.Add("[pool-copies] " + ResOptionsPath + " carries a NUL byte.");
                return;
            }
        }

        /// <summary>
        /// The seam still SAYS which source a pick came from. CLAUDE.md section 12 forbids stripping
        /// instrumentation, and the work order names this exact field: without it, "the override did
        /// not work" and "the override worked and the art is subtle" are indistinguishable.
        /// </summary>
        private static void Case9_TheSeamIsInstrumented(List<string> failures)
        {
            if (!File.Exists(OverridesSrc))
            {
                failures.Add("[instrumented] " + OverridesSrc + " is missing.");
                return;
            }
            string src = File.ReadAllText(OverridesSrc);
            if (src.IndexOf("FlowTrace.Warn", StringComparison.Ordinal) < 0)
                failures.Add("[instrumented] " + OverridesSrc + " no longer emits FlowTrace.Warn. A fallback " +
                             "that stops warning is a SILENT fallback - CLAUDE.md section 12, never strip.");
            // The per-play decline lines are THROTTLED rather than Warn'd, on purpose: Resolve runs on
            // every play of the key, and a Warn on a hot path floods the device logcat ring and evicts
            // the boot window (memory: logcat-ring-buffer-destroys-evidence). Losing the throttle would
            // trade one silent failure for another.
            if (src.IndexOf("FlowTrace.Throttle", StringComparison.Ordinal) < 0)
                failures.Add("[instrumented] " + OverridesSrc + " no longer THROTTLES its per-play decline " +
                             "line. An unthrottled Warn on this path evicts the boot window out of the " +
                             "logcat ring, which destroys the evidence the trace exists to capture.");
            if (src.IndexOf("source=", StringComparison.Ordinal) < 0)
                failures.Add("[instrumented] " + OverridesSrc + " no longer prints the SOURCE of a pick. " +
                             "Without it a capture cannot tell a failed override from a subtle one.");
            if (src.IndexOf("FallbackReason", StringComparison.Ordinal) < 0)
                failures.Add("[instrumented] " + OverridesSrc + " no longer carries a fallback REASON.");
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        /// <summary>Assert every catalog key still resolves to the row the Rows array holds for it.</summary>
        private static void AssertBuildTimePicks(List<string> failures, HovlVfxCatalog catalog, string situation)
        {
            var rows = catalog.Rows;
            if (rows == null)
            {
                failures.Add("[no-row] " + CatalogPath + " loaded but its Rows array is NULL, so the " +
                             "byte-for-byte invariant cannot be checked against anything under '" +
                             situation + "'. That is a broken catalog asset, not a reason to pass.");
                return;
            }
            int broken = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (string.IsNullOrEmpty(rows[i].Key)) continue;

                // ⚠ COMPARE AGAINST THE LAST-WINS RESOLUTION, NOT rows[i]. The catalog is generated
                // by MERGING a curated map with the manual overlay (HovlVfxCatalogGenerator), and
                // BuildLookup resolves a duplicated key to the LAST row. Comparing an earlier
                // duplicate against TryGet would red on a key the build has always resolved
                // correctly - a suite failing on a non-defect is worse than no suite, because the
                // next seat learns to ignore it.
                if (!TryGetRawRow(catalog, rows[i].Key, out var expected)) continue;
                if (!catalog.TryGet(expected.Key, out var got) || got.Prefab != expected.Prefab ||
                    got.IsLoop != expected.IsLoop)
                {
                    broken++;
                    if (broken <= 5)
                        failures.Add("[no-row] THE INVARIANT IS BROKEN under '" + situation + "': key '" +
                                     expected.Key + "' resolved to '" +
                                     (got.Prefab == null ? "null" : got.Prefab.name) + "' instead of the " +
                                     "build-time '" + (expected.Prefab == null ? "null" : expected.Prefab.name) +
                                     "'. An offline player must see EXACTLY the build that shipped.");
                }
            }
            if (broken > 5)
                failures.Add("[no-row] ... and " + (broken - 5) + " more key(s) broken under '" + situation + "'.");
        }

        /// <summary>The RAW row from the Rows array - the build-time pick, with no override layer.</summary>
        private static bool TryGetRawRow(HovlVfxCatalog catalog, string key, out HovlVfxCatalog.Row row)
        {
            row = default;
            var rows = catalog.Rows;
            if (rows == null || string.IsNullOrEmpty(key)) return false;
            bool found = false;
            for (int i = 0; i < rows.Length; i++)
            {
                if (!string.Equals(rows[i].Key, key, StringComparison.Ordinal)) continue;
                row = rows[i];      // last row wins on duplicate keys, matching BuildLookup
                found = true;
            }
            return found;
        }

        /// <summary>An option that is resolvable, matches the requested loop-ness and is not the key itself.</summary>
        private static OptionRow FindOption(List<OptionRow> options, HovlVfxCatalog catalog, string forKey,
                                            bool wantLoop, GameObject differentPrefabFrom)
        {
            foreach (var opt in options)
            {
                if (opt.retired || opt.unresolved) continue;   // never offered, so never used as a fixture
                if (string.Equals(opt.key, forKey, StringComparison.Ordinal)) continue;
                if (!TryGetRawRow(catalog, opt.key, out var row) || row.Prefab == null) continue;
                if (row.IsLoop != wantLoop) continue;
                if (differentPrefabFrom != null && row.Prefab == differentPrefabFrom) continue;
                return opt;
            }
            return null;
        }

        /// <summary>Push a values map onto the tunables table as a well-formed remote payload.</summary>
        private static void ApplyPicks(string valuesJson)
        {
            RemoteTunables.ApplyPayload(
                "{\"version\":1,\"readOk\":true,\"values\":" + valuesJson + "}",
                RemoteTunables.ProvenanceRemote);
        }

        /// <summary>Minimal mirror of one generated option row - read from disk, never re-typed.</summary>
        private sealed class OptionRow
        {
            public int id;
            public string key;
            public bool retired;
            public bool unresolved;
        }

        /// <summary>Parse the BUILD's copy of the pool. A missing file is a failure, never an empty pass.</summary>
        private static List<OptionRow> LoadOptions(List<string> failures)
        {
            var list = new List<OptionRow>();
            try
            {
                if (!File.Exists(ResOptionsPath))
                {
                    failures.Add("[options] " + ResOptionsPath + " is missing - the build would offer and " +
                                 "resolve NOTHING. Run: node tools/gen-vfx-pick-options.mjs");
                    return list;
                }
                var doc = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ResOptionsPath));
                var arr = doc["options"] as Newtonsoft.Json.Linq.JArray;
                if (arr == null)
                {
                    failures.Add("[options] " + ResOptionsPath + " has no options array.");
                    return list;
                }
                foreach (var tok in arr)
                {
                    list.Add(new OptionRow
                    {
                        id = tok.Value<int>("id"),
                        key = tok.Value<string>("key"),
                        retired = tok.Value<bool?>("retired") == true,
                        unresolved = tok.Value<bool?>("unresolved") == true,
                    });
                }
            }
            catch (Exception ex)
            {
                failures.Add("[options] could not read " + ResOptionsPath + ": " + ex.Message);
            }
            return list;
        }

        /// <summary>Run one case without letting a throw take the suite (and the gate) down.</summary>
        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }
    }
}
