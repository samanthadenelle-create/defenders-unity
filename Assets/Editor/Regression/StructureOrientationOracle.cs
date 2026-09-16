// =============================================================================
// StructureOrientationOracle (PROD-008) — the gate that can SEE a lying-down
// building, because nothing else in this project can.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Markers: STRUCTURE_ORIENTATION_OK /
// STRUCTURE_ORIENTATION_FAIL.  Editor-only asset reads. No scene, no PlayMode.
//
// =============================================================================
// WHY THIS EXISTS
// =============================================================================
// Every orientation defect this project has shipped went out COMPILE-GREEN and
// REGRESSION-GREEN. Commit f995c4706 said so about itself: "sits correctly in the
// town is a felt claim". On 2026-08-18 the owner found TEN structure models
// double-corrected — on a LIVE store build, by eye — because the axis conversion
// had been baked into the MESH while the legacy -90 compensations stayed live in
// the catalog. Two independent channels each applied a correction only one of
// them should have, and no automated check could see it.
//
// The whole point of this file is that ORIENTATION IS MEASURABLE. It is not a
// felt claim. A structure that is lying down has a WRONG NUMBER attached to it,
// and the catalog already declares what that number should be.
//
// =============================================================================
// THE MECHANISM, READ AT SOURCE (this is the load-bearing part — do not trust
// the summary, the file:line citations are here so the next seat can re-derive it)
// =============================================================================
// VisualFactory.Skin (Assets/_Modules/Village/VisualFactory.cs) runs, in order:
//   1. Instantiate the prefab under the host, localPosition = zero.
//   2. localRotation = identity, UNLESS opts.PreservePrefabRotation (DEF-232 /
//      WO-928). Note this touches the CLONE ROOT ONLY — a rotation authored on a
//      CHILD of the prefab survives both branches untouched.
//   3. Fit(): `measure = bounds.size.y; localScale *= target / measure;`
//      (VisualFactory.Fit, the `largest:false` arm — StructureFactory.OptsFor
//      clears FitLargest and sets FitHeight, so structures ALWAYS fit to HEIGHT).
//   4. SeatOnGround(): translation only, never affects size.
//
// StructureFactory.Create (Assets/_Modules/Village/Catalog/StructureFactory.cs)
// then applies the catalog's own correction — AFTER Skin has returned:
//      visual.transform.localRotation = Quaternion.Euler(entry.orientation.Euler)
//                                     * visual.transform.localRotation;
//      ... EffectiveScale ... ReseatCorrectedBottom(...)   // translation only
//
// THE CONSEQUENCE, AND IT IS THE WHOLE ORACLE:
//   Fit measures the model in its PRE-ORIENTATION pose. So for any row whose
//   authored correction does NOT tip the vertical axis, the FINAL world height is
//   EXACTLY YHeightVariable * heightMul — a number the catalog itself declares.
//   Nothing is thresholded, nothing is guessed. A model that is lying down at fit
//   time has its DEPTH fitted to that target instead of its height, so the number
//   comes out wrong and says by how much. That is the same mis-measured fit the
//   tower_ground_archer row's own `note` walks through (9.25x where 4.80x was due).
//
// =============================================================================
// ⛔ THE DESIGN CONSTRAINT THAT MAKES THE OBVIOUS VERSION WRONG
// =============================================================================
// The obvious oracle is "height / max(width, depth) > 1.2" applied to everything —
// WoodenWatchtowerBuilder's UprightAspectMin (WoodenWatchtowerBuilder.cs:271-277,
// measured 1.70-1.92 upright vs 0.52-0.59 lying down on the three towers).
//
// THAT VERSION FALSE-POSITIVES ON HONEST BUILDINGS AND MUST NOT SHIP.
// House_Medieval_Medium fits to 4.0 m tall by 5.562 m across (the catalog's own
// `_heightCadence` states the 5.562 figure) = aspect 0.72, i.e. squarely inside
// the "lying down" band while perfectly upright. A gate that reds correct
// buildings gets itself disabled, and a disabled gate is worse than no gate.
//
// So the aspect band is used ONLY where tall-and-narrow is a property of the
// CLASS, and the class is read off the catalog's own data (CatalogType.Tower plus
// the 1.2 tower cadence anchor), never off a hardcoded name list.
//
// =============================================================================
// WHAT THIS ORACLE ASSERTS
// =============================================================================
// A1  CHANNEL COLLISION (every row + every tier).
//     A source model whose ModelImporter has bakeAxisConversion == true is MEANT to
//     carry the Z-up -> Y-up correction IN THE MESH (TripoAxisBake.cs: "the two
//     halves must flip together or the model ends up upside down"), and such a model
//     may not also receive a vertical-axis rotation from the catalog channel.
//     FAILS when:
//       (a) the row's orientation.manual euler tips world-up by more than 1 deg
//           AND the model MEASURABLY STANDS UP AT IDENTITY (NativelyUpright: height
//           dominance AND MeshTaper must AGREE), or
//       (b) repo.preservePrefabRotation is true AND the prefab's own ROOT carries
//           a tilt (preserving an identity root is a no-op and is not flagged —
//           asserting on the flag alone would be a claim we did not measure).
//     This is the check that would have caught 2026-08-18 before the store build.
//
//     ⛔ (a) WAS DATA-ONLY UNTIL 2026-08-23 AND THAT MADE IT LIE. The flag says the
//     bake was REQUESTED; it does not say the mesh came back standing. On this
//     project's Tripo shop family (workshop / forge / armorer / jeweler / barracks)
//     the flag is set, the catalog also authors euler=[90,0,0], and the buildings
//     RENDER CORRECTLY IN GAME with both applied — owner-confirmed for the jeweler.
//     The data-only form reported five DOUBLE-CORRECTED failures whose stated remedy
//     ("zero the euler") would have tipped five correct buildings onto their sides.
//     The counter-experiment is recorded: clearing the flag on four of them and
//     force-reimporting turned all four UPSIDE DOWN at the shipped pitch (jeweler
//     taper 0.79 -> 1.27). Renders on disk, before and after:
//     docs/ui-evidence/structure-orientation-2026-08-23/.
//     An oracle whose remedy breaks a correct town is worse than no oracle, so the
//     accusation is now EARNED BY MEASUREMENT and unproven cases are LOGGED, not
//     failed. Cost of that choice, stated rather than hidden: a genuine double
//     correction on a model that does NOT stand at identity is not caught by A1.
//
// A2  HEIGHT FIDELITY (MEASURED, threshold-free target, the primary assert).
//     Replays the pipeline above and asserts the final world bounds height equals
//     StructureFactory.OptsFor(entry).FitHeight within +/-0.05 m. The expected
//     value is taken from the REAL production helper, not re-derived here — a
//     second copy of `YHeightVariable * heightMul` is how a gate and the game come
//     to disagree while both report success.
//       - base visual: the row's manual orientation is applied first (as Create does).
//       - tier models (repo.upgradeVisualPath): orientation is deliberately NOT
//         applied — ReskinForLevel says so in as many words ("Tier models rely on
//         their prefab-native orientation"), so a tier model must stand on its own.
//
// A3  TOWER ASPECT BAND (scoped, 1.2, from WoodenWatchtowerBuilder's measurement).
//     final height / max(width, depth) >= 1.2, for tower-CLASS rows only.
//
// =============================================================================
// ⚠ WHAT THIS ORACLE DOES **NOT** COVER — stated, never special-cased
// =============================================================================
// (1) RealmStore IS NOT A CATALOG ROW. structures-catalog.json holds 28 entries
//     and none of them is the Realm Store storefront (PROD-003 placed it from
//     Assets/Editor/RealmStorePlacer.cs). A catalog-driven oracle therefore cannot
//     see it, and RealmStore.fbx carries bakeAxisConversion: 1 — exactly the state
//     that double-corrects. It is NOT special-cased in here: a special case in an
//     oracle is a lie about its coverage. Covering it needs either a catalog row
//     or a placer-side oracle, and that is a separate ticket.
//
// (2) A2 IS NOT ASSERTED ON A BASE VISUAL WHOSE CATALOG ORIENTATION TIPS THE
//     VERTICAL AXIS (rows still on [-90,0,0]: pet-house, market,
//     arcane-tower, collector_farm, collector_lumbermill). lumberyard/foundry/silo
//     are KayKit Pallet_Wood_Covered_A at identity (owner 2026-09-12: rot X=0 pos Y=0).
//     NOT because they are known-good — because for those rows the pipeline
//     PROVABLY fits a different axis than the one they end up standing on (see the
//     mechanism block above), so `YHeightVariable * heightMul` is not their
//     expected height and the catalog does not declare what is. Their whole
//     automated coverage is A1. The excluded count is printed in the OK line so
//     this can never read as full coverage. If those models are ever axis-baked,
//     A1 arms on them the same day and A2 follows automatically once the euler
//     zeroes out.
//
// (3) A3 IS NOT APPLIED TO THE SIEGE-ENGINE GROUP (tower_siege_tower,
//     tower_catapult, heightMul 0.75). Their `_heightNote` authors them as
//     machines that deliberately sit UNDER the house line; they are squat by
//     design and the 1.2 band would red them. Scoped by heightMul, not by name.
//
// (4) A row whose type is Tower but whose art is a shared/placeholder mesh is
//     still measured — placeholders ship, and a placeholder lying down looks
//     exactly as broken to a player as the real model would.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Village;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    /// <summary>PROD-008 — orientation/height oracle over structures-catalog.json.</summary>
    public static class StructureOrientationOracle
    {
        private const string CatalogRelPath = "Data/Canonical/structures-catalog.json";

        /// <summary>
        /// Height-fidelity tolerance in metres. NOT a tuned band: the fit is a single
        /// float multiply (localScale *= target / measuredY), so a correctly-oriented
        /// model reproduces the target to float precision. 0.05 m absorbs bounds
        /// round-tripping only. Widening this to make something pass defeats the file.
        /// </summary>
        private const float HeightToleranceM = 0.05f;

        /// <summary>
        /// Degrees of vertical-axis tilt that count as "this channel rotates the model".
        /// 1 degree is a numeric-equality epsilon, not a judgement band — every authored
        /// correction in the catalog is either exactly 0 or exactly -90.
        /// </summary>
        private const float TiltEpsilonDeg = 1.0f;

        /// <summary>
        /// Upright aspect floor for TOWER-CLASS rows only. MEASURED, not chosen:
        /// WoodenWatchtowerBuilder.cs:271-277 records 1.70-1.92 upright and 0.52-0.59
        /// lying down across the three wooden-watchtower models, so 1.2 separates the
        /// two states with a wide margin and cannot be satisfied by a tower on its side.
        /// Same constant, same rationale, second consumer — see the ⛔ block on scope.
        /// </summary>
        private const float UprightAspectMin = 1.2f;

        /// <summary>
        /// The tower cadence anchor. A row is TOWER-CLASS for A3 when
        /// type == CatalogType.Tower AND heightMul is at least this — which selects the
        /// 1.2 anchor group from the catalog's own `_heightCadence` and leaves the 0.75
        /// siege engines out. Data-derived; never a list of ids.
        /// </summary>
        private const float TowerCadenceMinHeightMul = 1.2f;

        [System.Serializable]
        private sealed class StructuresFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        /// <summary>One measurable model: a row's base visual, or one rung of its ladder.</summary>
        private sealed class Subject
        {
            public CatalogEntry Entry;
            public string Address;
            public int    Tier;            // 0 = base visual, 2..N = repo.upgradeVisualPath rung
            public bool   AppliesOrientation;   // tiers never do (ReskinForLevel)
            public string Label => Tier == 0 ? Entry.id : Entry.id + " L" + Tier;
        }

        [MenuItem("Defenders/Build/Audit Structure Orientation (PROD-008)")]
        public static void RunMenu()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason); else Debug.LogError(reason);
        }

        /// <summary>Headless batch entry — prints the marker and nothing else to scrape.</summary>
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason); else Debug.LogError(reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();

            // ---- 1. the catalog, parsed exactly as CatalogBootstrap parses it ----------
            var entries = ParseCatalog(failures);
            if (entries == null) return Verdict(failures, log, 0, 0, 0, 0, out reason);

            // ---- 2. the address -> asset map, read off the Addressables SETTINGS -------
            // Read from the settings object rather than from the runtime seam: in the
            // editor the Addressables play-mode provider can serve straight from the
            // AssetDatabase, so "it loaded" in a gate proves nothing about the built
            // catalog. The settings are the authored truth, and they are what ships.
            var addressToPath = BuildAddressMap();
            if (addressToPath == null)
            {
                failures.Add("no AddressableAssetSettings object — structure art lives in the remote " +
                             "Structure_Art group (Assets/StructureContent), so with no settings NOTHING " +
                             "resolves and every building in the game is invisible. Orientation cannot be " +
                             "measured because there is no geometry to measure.");
                return Verdict(failures, log, 0, 0, 0, 0, out reason);
            }

            // ---- 3. the subjects: every base visual + every authored tier rung ---------
            var subjects = new List<Subject>();
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (!string.IsNullOrEmpty(e.visualPrefabPath))
                    subjects.Add(new Subject { Entry = e, Address = e.visualPrefabPath, Tier = 0, AppliesOrientation = true });

                var ladder = e.repo != null ? e.repo.upgradeVisualPath : null;
                if (ladder == null) continue;
                for (int i = 0; i < ladder.Length; i++)
                {
                    string p = ladder[i];
                    if (string.IsNullOrEmpty(p)) continue;          // authored-empty rung (healing_caravan)
                    if (p == e.visualPrefabPath) continue;           // same model as base; already a subject
                    subjects.Add(new Subject { Entry = e, Address = p, Tier = i + 2, AppliesOrientation = false });
                }
            }

            if (subjects.Count == 0)
            {
                // Not a stand-down: 28 authored rows with no measurable subject means the
                // catalog lost every visualPrefabPath, which is a total art outage.
                failures.Add($"structures-catalog.json parsed {entries.Count} entries but produced ZERO " +
                             "measurable visuals (no visualPrefabPath anywhere) — every building in the " +
                             "game would render nothing.");
                return Verdict(failures, log, 0, 0, 0, 0, out reason);
            }

            int measured = 0, a1Checked = 0, heightAsserted = 0, aspectAsserted = 0, tipExcluded = 0;
            var tipExcludedLabels = new List<string>();

            foreach (var s in subjects)
            {
                // ---- resolve the address the way the shipped catalog will --------------
                if (!addressToPath.TryGetValue(s.Address, out string assetPath) || string.IsNullOrEmpty(assetPath))
                {
                    failures.Add($"'{s.Label}': address '{s.Address}' is NOT registered in any Addressable " +
                                 "group and Assets/Resources/Structures is gone — StructureAssetLoader " +
                                 "resolves it via neither path, so this structure renders NOTHING.");
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    failures.Add($"'{s.Label}': address '{s.Address}' points at '{assetPath}', which loads no " +
                                 "GameObject (dangling entry — the group keeps the GUID after the asset moves).");
                    continue;
                }

                // ---- A1: CHANNEL COLLISION (data only) ---------------------------------
                a1Checked++;
                bool meshBaked = AnyModelAxisBaked(assetPath, out string bakedModelPath);

                float catalogTilt = 0f;
                bool catalogManual = s.AppliesOrientation && s.Entry.orientation != null && s.Entry.orientation.manual;
                if (catalogManual)
                    catalogTilt = TiltDegrees(Quaternion.Euler(s.Entry.orientation.Euler));

                bool preserve = s.Entry.repo != null && s.Entry.repo.preservePrefabRotation;
                float prefabRootTilt = TiltDegrees(prefab.transform.localRotation);

                if (meshBaked && catalogTilt > TiltEpsilonDeg)
                {
                    // ⛔ MEASURED, NOT INFERRED (2026-08-23). The flag alone is NOT proof that the
                    // mesh arrives standing up, and on this project's Tripo family it PROVABLY is
                    // not: workshop/forge/armorer/jeweler/barracks all carry bakeAxisConversion=1
                    // AND orientation.euler=[90,0,0], which read here as five double-corrections -
                    // yet every one of them RENDERS CORRECTLY in game with both applied, which the
                    // owner confirmed for the jeweler after a four-hour investigation.
                    //
                    // The renders are on disk; open them before doubting this:
                    //   docs/ui-evidence/structure-orientation-2026-08-23/<stem>__pitch0.png   (lying down)
                    //   docs/ui-evidence/structure-orientation-2026-08-23/<stem>__pitch90.png  (upright)
                    // And the counter-experiment was run: clearing bakeAxisConversion on the four
                    // and force-reimporting turned all four UPSIDE DOWN at the shipped pitch
                    // (jeweler taper 0.79 -> 1.27, roof into the ground). The flag is NOT inert and
                    // the euler is NOT redundant - they are two halves of one correct pose. The
                    // metas were restored byte-identical.
                    //
                    // So the accusation now has to be EARNED by a measurement: the model must
                    // already STAND UP on its own at identity before a catalog tilt can be called a
                    // second correction. Two independent signals must AGREE (dominance AND taper),
                    // because on 2026-08-22 each one lied alone.
                    bool nativeStands = NativelyUpright(prefab, out float nativeTaper, out Vector3 nativeSize);
                    string ev = $"native identity bounds {nativeSize.x:0.00} x {nativeSize.y:0.00} x " +
                                $"{nativeSize.z:0.00}, taper {nativeTaper:0.00}";

                    if (nativeStands)
                        failures.Add($"'{s.Label}' DOUBLE-CORRECTED: model '{bakedModelPath}' has " +
                                     $"bakeAxisConversion=true (the Z-up->Y-up fix is in the MESH - and it " +
                                     $"MEASURABLY LANDED: {ev}, i.e. the model already stands at identity) " +
                                     $"AND the catalog row still authors orientation.euler=" +
                                     $"{Fmt(s.Entry.orientation.Euler)}, which tips world-up by " +
                                     $"{catalogTilt:0.0} deg on top of it. Both corrections apply — this is " +
                                     "the 2026-08-18 defect exactly. Zero the euler (keep manual:true so no " +
                                     "auto-baker re-tips the row) or clear bakeAxisConversion; never both.");
                    else
                        log.Append($"\n  [A1 measured-clear] '{s.Label}': bakeAxisConversion=true on " +
                                   $"'{bakedModelPath}' AND euler={Fmt(s.Entry.orientation.Euler)} " +
                                   $"({catalogTilt:0.0} deg), but the model does NOT stand at identity " +
                                   $"({ev}) - the bake did not deliver a Y-up mesh here, so the catalog " +
                                   "tilt is the ONLY correction in play, not a second one. Render-proven " +
                                   "2026-08-23 (docs/ui-evidence/structure-orientation-2026-08-23/).");
                }

                if (meshBaked && preserve && prefabRootTilt > TiltEpsilonDeg)
                    failures.Add($"'{s.Label}' DOUBLE-CORRECTED: model '{bakedModelPath}' has " +
                                 $"bakeAxisConversion=true AND repo.preservePrefabRotation=true over a prefab " +
                                 $"root already tilted {prefabRootTilt:0.0} deg — the baked mesh and the " +
                                 "preserved pose compose.");

                // ---- measure: replay the shipped pipeline ------------------------------
                if (!TryMeasure(prefab, s, out Vector3 size, out float target, out string measureNote))
                {
                    failures.Add($"'{s.Label}' ({assetPath}): {measureNote}");
                    continue;
                }
                measured++;

                float h = size.y;
                float maxWD = Mathf.Max(size.x, size.z);
                float aspect = maxWD > 0.0001f ? h / maxWD : 0f;

                // ---- A2: HEIGHT FIDELITY ----------------------------------------------
                bool tipsVertical = catalogManual && catalogTilt > TiltEpsilonDeg;
                if (tipsVertical)
                {
                    // Named, counted exclusion — see coverage note (2) in the header.
                    tipExcluded++;
                    tipExcludedLabels.Add($"{s.Label}(tilt {catalogTilt:0.0}deg, measured h={h:0.00}m)");
                }
                else
                {
                    heightAsserted++;
                    float delta = h - target;
                    if (Mathf.Abs(delta) > HeightToleranceM)
                        failures.Add($"'{s.Label}' HEIGHT FIDELITY: measured world height {h:0.00} m, " +
                                     $"expected {target:0.00} m (StructureFactory.OptsFor -> YHeightVariable * " +
                                     $"heightMul), off by {delta:+0.00;-0.00} m. The fit is one multiply, so it " +
                                     "cannot miss unless it measured a DIFFERENT AXIS than the one the model " +
                                     $"finally stands on — i.e. this model is not upright at fit time. " +
                                     $"Measured bounds {size.x:0.00}x{h:0.00}x{size.z:0.00} m, aspect " +
                                     $"{aspect:0.00}; asset '{assetPath}'; prefab-root tilt {prefabRootTilt:0.0} " +
                                     $"deg; preservePrefabRotation={preserve}; mesh axis-baked={meshBaked}.");
                }

                // ---- A3: TOWER ASPECT BAND (scoped) -----------------------------------
                float heightMul = s.Entry.repo != null ? s.Entry.repo.heightMul : 1f;
                bool towerClass = s.Entry.type == CatalogType.Tower && heightMul >= TowerCadenceMinHeightMul;
                if (towerClass)
                {
                    aspectAsserted++;
                    if (aspect < UprightAspectMin)
                        failures.Add($"'{s.Label}' NOT STANDING: upright aspect {aspect:0.00} " +
                                     $"(height {h:0.00} m / max(width {size.x:0.00}, depth {size.z:0.00}) = " +
                                     $"{maxWD:0.00} m) is below the measured {UprightAspectMin:0.0} floor. " +
                                     "WoodenWatchtowerBuilder measured this class at 1.70-1.92 upright and " +
                                     "0.52-0.59 lying down; a tower cannot read below 1.2 while standing. " +
                                     "THERE ARE EXACTLY TWO WAYS TO BE HERE and the fix differs: (a) the model " +
                                     "is mis-oriented — check the height line above, a lying-down model misses " +
                                     "its height target too; or (b) the ROW is mis-classed — it is typed Tower " +
                                     $"on the {TowerCadenceMinHeightMul:0.0} tower cadence anchor while its art " +
                                     "is a wide machine rather than a tower silhouette, in which case the row's " +
                                     "heightMul belongs in the siege-engine group (0.75) like tower_catapult, " +
                                     "and that is an owner ruling, NOT a reason to widen this floor. " +
                                     $"Asset '{assetPath}'; prefab-root tilt {prefabRootTilt:0.0} deg; " +
                                     $"catalog euler tilt {catalogTilt:0.0} deg; mesh axis-baked={meshBaked}.");
                }
            }

            log.AppendLine($"subjects={subjects.Count} (base visuals + authored upgrade rungs), measured={measured}");
            log.AppendLine($"A1 channel-collision checked on {a1Checked} model(s)");
            log.AppendLine($"A2 height fidelity asserted on {heightAsserted} model(s) at +/-{HeightToleranceM:0.00} m");
            log.AppendLine($"A3 tower aspect asserted on {aspectAsserted} tower-class model(s) at >= {UprightAspectMin:0.0}");
            if (tipExcluded > 0)
                log.AppendLine($"A2 NOT ASSERTED on {tipExcluded} base visual(s) whose catalog orientation tips the " +
                               "vertical axis (coverage note 2 — the fit provably measures a different axis than " +
                               "the one they stand on, so the catalog declares no expected height for them; their " +
                               "coverage is A1 only): " + string.Join(", ", tipExcludedLabels.ToArray()));
            log.AppendLine("NOT COVERED: RealmStore — it is not a catalog row (28 entries, no store/realm id), " +
                           "so no catalog-driven oracle can see it, and RealmStore.fbx is axis-baked.");

            return Verdict(failures, log, measured, heightAsserted, aspectAsserted, tipExcluded, out reason);
        }

        // =====================================================================
        //  MEASUREMENT — replays VisualFactory.Skin + StructureFactory.Create
        // =====================================================================
        /// <summary>
        /// Instantiates <paramref name="prefab"/> and reproduces, step for step, what the
        /// shipped pipeline does to it, then returns the final WORLD bounds size.
        ///
        /// It deliberately does NOT call VisualFactory.Skin: that path goes through
        /// StructureAssetLoader -> Addressables, whose editor behaviour depends on the
        /// play-mode script, and a gate that can silently resolve nothing is the hollow
        /// pass this file exists to prevent. The TARGET HEIGHT and the ROTATION POLICY are
        /// still taken from the real production helper (StructureFactory.OptsFor) so the
        /// formula is never re-typed here — only the three lines of transform arithmetic
        /// are mirrored, and they are cited inline.
        ///
        /// Bounds are gathered from ACTIVE renderers only, because that is exactly what
        /// VisualFactory.TryBounds does (GetComponentsInChildren&lt;Renderer&gt;() with no
        /// includeInactive) and therefore exactly what Fit divided into.
        /// </summary>
        private static bool TryMeasure(GameObject prefab, Subject s, out Vector3 size, out float target, out string note)
        {
            size = Vector3.zero;
            target = 0f;
            note = null;

            SkinOptions opts;
            try { opts = StructureFactory.OptsFor(s.Entry); }
            catch (Exception ex)
            {
                note = "StructureFactory.OptsFor threw: " + ex.Message;
                return false;
            }
            target = opts.FitHeight;
            if (target <= 0f)
            {
                note = $"OptsFor produced a non-positive fit height ({target:0.###}) — nothing to assert against.";
                return false;
            }

            var host = new GameObject("PROD008_OrientationProbe");
            host.hideFlags = HideFlags.HideAndDontSave;
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject go = null;
            try
            {
                go = Object.Instantiate(prefab, host.transform);
                go.transform.localPosition = Vector3.zero;

                // VisualFactory.Skin step 2 — clone-root policy. LocalRotation is never set
                // by OptsFor (structures), so this is the identity-vs-preserve branch only.
                if (!opts.PreservePrefabRotation) go.transform.localRotation = Quaternion.identity;

                // VisualFactory.Fit, `largest:false` arm: localScale *= target / bounds.size.y
                if (!TryActiveWorldBounds(go, out Bounds pre))
                {
                    note = "no ACTIVE renderers with measurable bounds — VisualFactory.VerifyRenders would " +
                           "destroy this instance and the structure would fall back to nothing.";
                    return false;
                }
                if (pre.size.y < 0.0001f)
                {
                    note = $"degenerate pre-fit Y extent ({pre.size.y:0.#####} m) — Fit would early-return and " +
                           "leave the model at import scale.";
                    return false;
                }
                go.transform.localScale *= target / pre.size.y;

                // StructureFactory.Create — the catalog correction, applied AFTER the fit.
                // ReskinForLevel does NOT do this for tier models ("Tier models rely on their
                // prefab-native orientation"), so tiers are measured without it.
                if (s.AppliesOrientation && s.Entry.orientation != null && s.Entry.orientation.manual)
                {
                    go.transform.localRotation = Quaternion.Euler(s.Entry.orientation.Euler) * go.transform.localRotation;
                    if (s.Entry.orientation.HasScale)
                        go.transform.localScale = Vector3.Scale(go.transform.localScale, s.Entry.orientation.EffectiveScale);
                }

                if (!TryActiveWorldBounds(go, out Bounds post))
                {
                    note = "bounds became unmeasurable after the orientation correction.";
                    return false;
                }
                size = post.size;
                return true;
            }
            catch (Exception ex)
            {
                note = "measurement threw: " + ex.Message;
                return false;
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>Mirror of VisualFactory.TryBounds: ACTIVE renderers only, world AABB.</summary>
        private static bool TryActiveWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
            return true;
        }

        // =====================================================================
        //  THE OTHER CHANNEL — the importer flag
        // =====================================================================
        /// <summary>
        /// True when the asset at <paramref name="assetPath"/>, or any model it depends on,
        /// imports with ModelImporter.bakeAxisConversion — i.e. the Z-up -> Y-up correction
        /// lives in the MESH DATA and no consumer may apply it a second time
        /// (TripoAxisBake.cs: "the two halves must flip together").
        /// </summary>
        /// <summary>
        /// Does this model STAND UP on its own, with no catalog correction at all? Instantiated at
        /// identity and judged on TWO signals that must AGREE:
        ///   * height dominance - world Y is the longest axis (within 5%), and
        ///   * MeshTaper.Ratio  - narrow on top, i.e. a roof rather than a plinth in the air.
        ///
        /// ⛔ WHY BOTH, AND WHY DISAGREEMENT MEANS "NO": on 2026-08-22 each signal lied ALONE - an
        /// AABB cannot tell +90 from -90, and the taper scores a wide low COMPOUND (the barracks,
        /// 0.95 tall vs 1.00 wide at identity, taper 0.82) as if its roof were down. Requiring
        /// agreement makes the DEFAULT "not proven", which is the safe default for an assert whose
        /// remedy is to edit a building that may currently be correct on screen. A green gate over
        /// a broken town is strictly worse than a red gate over a correct one - and so is a red
        /// gate that talks a seat into breaking one.
        /// </summary>
        private static bool NativelyUpright(GameObject prefab, out float taper, out Vector3 size)
        {
            taper = 1f;
            size = Vector3.zero;
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Object.Instantiate(prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (!TryActiveWorldBounds(go, out Bounds b)) return false;

                size = b.size;
                taper = MeshTaper.Ratio(go.transform, b);
                bool tall = b.size.y >= Mathf.Max(b.size.x, b.size.z) * 0.95f;
                return tall && taper < MeshTaper.UprightBelow;
            }
            catch (Exception)
            {
                // An unmeasurable model is NOT evidence of a double correction.
                return false;
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        private static bool AnyModelAxisBaked(string assetPath, out string bakedModelPath)
        {
            bakedModelPath = null;
            if (string.IsNullOrEmpty(assetPath)) return false;

            if (AssetImporter.GetAtPath(assetPath) is ModelImporter self)
            {
                if (self.bakeAxisConversion) { bakedModelPath = assetPath; return true; }
                return false;
            }

            // A prefab: the mesh comes from whichever model asset(s) it depends on.
            var deps = AssetDatabase.GetDependencies(assetPath, true);
            if (deps == null) return false;
            foreach (var d in deps)
            {
                if (string.Equals(d, assetPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetImporter.GetAtPath(d) is ModelImporter mi && mi.bakeAxisConversion)
                { bakedModelPath = d; return true; }
            }
            return false;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        /// <summary>Degrees by which <paramref name="q"/> moves world-up off world-up.</summary>
        private static float TiltDegrees(Quaternion q) => Vector3.Angle(q * Vector3.up, Vector3.up);

        private static string Fmt(Vector3 v) => $"[{v.x:0.#},{v.y:0.#},{v.z:0.#}]";

        /// <summary>Every authored Addressable address -> the asset path its GUID resolves to.</summary>
        private static Dictionary<string, string> BuildAddressMap()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return null;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.address)) continue;
                    map[entry.address] = AssetDatabase.GUIDToAssetPath(entry.guid);
                }
            }
            return map;
        }

        private static List<CatalogEntry> ParseCatalog(List<string> failures)
        {
            string json = CanonicalJson.Read(CatalogRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add($"{CatalogRelPath} unreadable (CanonicalJson.Read returned empty) — the oracle has " +
                             "no rows to measure and cannot assert anything.");
                return null;
            }

            StructuresFile file;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = { new StringEnumConverter() },
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                file = JsonConvert.DeserializeObject<StructuresFile>(json, settings);
            }
            catch (Exception ex)
            {
                failures.Add($"structures-catalog.json failed to parse: {ex.Message}");
                return null;
            }

            if (file == null || file.Entries == null || file.Entries.Count == 0)
            {
                failures.Add("structures-catalog.json deserialized to 0 CatalogEntry objects (mapping break or empty 'entries')");
                return null;
            }
            return file.Entries;
        }

        private static bool Verdict(List<string> failures, StringBuilder log,
                                    int measured, int heightAsserted, int aspectAsserted, int tipExcluded,
                                    out string reason)
        {
            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("STRUCTURE_ORIENTATION_FAIL: ").Append(failures.Count).Append(" issue(s):");
                foreach (var f in failures) sb.Append("\n  - ").Append(f);
                if (log.Length > 0) sb.Append("\n").Append(log);
                reason = sb.ToString();
                return false;
            }

            var ok = new StringBuilder("STRUCTURE_ORIENTATION_OK — ");
            ok.Append(measured).Append(" structure model(s) measured through the shipped fit pipeline; ")
              .Append(heightAsserted).Append(" held height fidelity to within ").Append(HeightToleranceM.ToString("0.00"))
              .Append(" m of the catalog's declared YHeightVariable * heightMul, and ")
              .Append(aspectAsserted).Append(" tower-class model(s) stood above the ")
              .Append(UprightAspectMin.ToString("0.0")).Append(" upright-aspect floor. ")
              .Append(tipExcluded).Append(" base visual(s) are OUTSIDE the height assert by construction ")
              .Append("(their catalog orientation tips the vertical axis after the fit) and RealmStore is ")
              .Append("outside the oracle entirely (not a catalog row) — see the header coverage notes.\n");
            ok.Append(log);
            reason = ok.ToString();
            return true;
        }
    }
}
