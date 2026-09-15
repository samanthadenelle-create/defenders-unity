// =============================================================================
// RuntimeSpawnVisualRegression [runtime-spawn-visual]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// Pins the TWO owner-reported raid defects (2026-08-02) at their STRUCTURAL cause,
// so neither can silently come back the way it arrived:
//
//   DEFECT 1 - MAGENTA RAID TROOPS.
//     MagentaGuard was a SCENE-LOAD-ONLY guard: [RuntimeInitializeOnLoadMethod(
//     AfterSceneLoad)] + SceneManager.sceneLoaded, with a ONE-TIME
//     Object.FindObjectsByType<Renderer>() snapshot and no Update / no re-arm / no
//     per-object entry point. A raid troop is built MID-RAID (TroopDeployer ->
//     TroopFactory.Build -> VisualFactory.Skin), i.e. after every sceneLoaded has
//     fired, so the guard was structurally BLIND to it and the body stayed magenta.
//     The fix is a per-object sweep called from the shared skinning choke point.
//
//   DEFECT 2 - RAID TROOPS SLIDE / T-POSE.
//     TroopFactory built the body, the collider, the NavMeshAgent and the
//     TroopController - and never bound an AnimatorController. The two art packs in
//     troops.json failed that two different ways: the Knight FBX model prefab ships
//     an Animator with NO controller, and the Supercyan SC_* prefab variants ship
//     the vendor's StrafeMovement controller whose parameters (MoveVertical /
//     Grounded / MoveState / IsDead ...) contain none of the Speed / Attack / Hit /
//     Dead that TroopController actually writes. Either way every parameter write
//     was skipped and the agent slid a frozen rig.
//
// Cases:
//   1 [magenta-entrypoint] MagentaGuard exposes a PUBLIC per-object sweep that takes
//                          a GameObject, it is try/catch guarded (an uncaught throw
//                          out of the skinner halts the WebGL player), and the
//                          recovered-material cache is STATIC (one recovery per
//                          model, not one Material allocation per spawned troop).
//   2 [magenta-hooked]     The runtime spawn CHOKE POINT - VisualFactory.Skin's
//                          (Transform, GameObject, SkinOptions) overload, the one both
//                          overloads and every factory funnel through - actually calls
//                          it, before it hands the body back.
//   3 [magenta-shared]     Both seams share ONE recovery implementation (SweepRenderers),
//                          so the scene sweep and the spawn sweep cannot drift apart.
//   4 [body-animator]      GENERIC: every runtime character-body path (any file that
//                          skins a Heroes/ or Enemies/ model through VisualFactory)
//                          binds an AnimatorController. A body can never ship
//                          animator-less from a factory. New factories are picked up
//                          automatically - this case does not carry a hard-coded list.
//   5 [animator-order]     TroopFactory binds the animator BEFORE
//                          AddComponent<TroopController>(). AddComponent runs Awake
//                          synchronously and Awake caches which parameters exist, so
//                          binding after it re-creates the exact shipped defect while
//                          leaving all the fix code in place.
//   6 [troop-controllers]  DATA, not lint: every model in troops.json resolves to a
//                          real asset under Resources/Heroes, and every controller the
//                          troop fallback can land on exists AND declares the canonical
//                          AnimParams "Speed" float.
//
// Markers: RUNTIME_SPAWN_VISUAL_OK / RUNTIME_SPAWN_VISUAL_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.RuntimeSpawnVisualRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class RuntimeSpawnVisualRegression
    {
        private const string MagentaGuardSrc = "Assets/_Modules/Core/MagentaGuard.cs";
        private const string VisualFactorySrc = "Assets/_Modules/Village/VisualFactory.cs";
        private const string TroopFactorySrc = "Assets/_Modules/Village/Troops/TroopFactory.cs";
        private const string TroopControllerSrc = "Assets/_Modules/Village/Troops/TroopController.cs";

        private const string ModulesRoot = "Assets/_Modules";
        private const string HeroesRes = "Assets/Resources/Heroes";
        private const string TroopsJson = "Assets/Resources/Data/Canonical/troops.json";

        /// <summary>The one parameter the whole troop animation chain hangs on: it is the only
        /// per-frame write, and every controller this project builds that declares it also declares
        /// Attack / Hit / Dead. A controller without it cannot be driven by TroopController.</summary>
        private const string DriveParam = "Speed";

        // Literal curly braces are spelled with unicode escapes throughout this file so the repo's
        // brace-balance gate (CLAUDE.md section 1) - a naive open-vs-close character count over the
        // raw file - sees only real code braces. A regex that ends in a literal open brace, or a
        // char literal holding one, would otherwise trip that gate on every single run.
        private const string LBrace = "\u007B";
        private const char LBraceCh = '\u007B';
        private const char RBraceCh = '\u007D';

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("RUNTIME_SPAWN_VISUAL_OK - " + reason);
            else Debug.LogError("RUNTIME_SPAWN_VISUAL_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "magenta-entrypoint", () => Case1_MagentaEntryPoint(failures, notes));
                Case(failures, "magenta-hooked", () => Case2_ChokePointCallsIt(failures, notes));
                Case(failures, "magenta-shared", () => Case3_SharedRecovery(failures));
                Case(failures, "body-animator", () => Case4_EveryBodyPathBindsAnimator(failures, notes));
                Case(failures, "animator-order", () => Case5_AnimatorBoundBeforeController(failures));
                Case(failures, "troop-controllers", () => Case6_TroopControllerAssets(failures, notes));
                Case(failures, "troop-identity", () => Case7_SpawnIsAttributable(failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "RUNTIME SPAWN VISUAL OK - MagentaGuard exposes a guarded per-object sweep that the " +
                         "VisualFactory.Skin choke point calls (mid-raid bodies are no longer invisible to the " +
                         "scene-load-only guard), both seams share one recovery, every runtime character-body " +
                         "factory binds an AnimatorController, TroopFactory binds it before TroopController's " +
                         "Awake caches the parameter set, and every troops.json model plus every fallback " +
                         "controller resolves with a '" + DriveParam + "' parameter, and every troop spawn is " +
                         "ATTRIBUTABLE - the factory names the id and address before AddComponent, the animator " +
                         "verdict is reported from Configure (where the id exists) and siege is carved out" +
                         noteStr;
                return true;
            }
            reason = "runtime-spawn-visual FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1 - MagentaGuard has a guarded, cached, per-OBJECT entry point
        // =====================================================================
        private static void Case1_MagentaEntryPoint(List<string> failures, List<string> notes)
        {
            string src = ReadSource(MagentaGuardSrc, failures);
            if (src == null) return;
            string code = StripComments(src);

            // A PUBLIC STATIC method whose first parameter is a GameObject. Deliberately matched by
            // SHAPE, not by the name SweepGameObject: what must never regress is "there is a way to
            // sweep one object", not one particular identifier.
            var entry = Regex.Match(code,
                @"public\s+static\s+\w[\w<>\[\],\s\.]*\s+(?<name>\w+)\s*\(\s*GameObject\s+\w+");
            if (!entry.Success)
            {
                failures.Add("[magenta-entrypoint] MagentaGuard exposes NO public static method taking a " +
                             "GameObject - it is back to being a scene-load-only guard with a one-time " +
                             "FindObjectsByType snapshot, which is structurally blind to anything spawned " +
                             "mid-raid (the magenta-troop defect verbatim)");
                return;
            }
            string entryName = entry.Groups["name"].Value;
            notes.Add("per-object entry = MagentaGuard." + entryName);

            // The guard runs INSIDE the skinner. An uncaught exception there halts the WebGL player,
            // so the body must be try/catch wrapped end to end.
            string body = ExtractMethodBody(code, entry.Index);
            if (body == null)
            {
                failures.Add("[magenta-entrypoint] could not read the body of MagentaGuard." + entryName +
                             " - re-point this oracle deliberately rather than letting it silently pass");
                return;
            }
            if (body.IndexOf("try", StringComparison.Ordinal) < 0 ||
                body.IndexOf("catch", StringComparison.Ordinal) < 0)
                failures.Add("[magenta-entrypoint] MagentaGuard." + entryName + " is not try/catch wrapped - " +
                             "it is called from inside VisualFactory.Skin, and an uncaught exception on that " +
                             "path halts the WebGL player outright (a magenta body is cosmetic; a dead player " +
                             "is not)");

            // A missing gitignored art pack must degrade to a warning, never an error or a throw.
            if (!Regex.IsMatch(body, @"FlowTrace\.Warn") && !Regex.IsMatch(body, @"LogWarning"))
                failures.Add("[magenta-entrypoint] MagentaGuard." + entryName + " has no warning path - a " +
                             "gitignored art pack that was never imported (no resolvable URP/Lit shader) must " +
                             "warn and continue, not fail hard on a machine that simply lacks the pack");

            // STATIC cache: 8 troops off one tap share one broken source material. A per-call cache
            // would allocate a fresh Material per body - a leak plus an SRP-batching break.
            if (!Regex.IsMatch(code, @"static\s+readonly\s+Dictionary<\s*Material\s*,\s*Material\s*>"))
                failures.Add("[magenta-entrypoint] the recovered-material cache (Dictionary<Material,Material>) " +
                             "is not a STATIC field - a per-sweep local means every spawned troop allocates its " +
                             "own recovered Material for the SAME source, which leaks one material per troop " +
                             "and defeats SRP batching");

            // The ground-only, process-static name dedup must stay off the spawn path: it already
            // caused one silent miss (the colorless-floor RCA in MagentaGuard itself).
            var sweepRenderers = Regex.Match(code,
                @"static\s+void\s+SweepRenderers\s*\([^)]*\)\s*" + Regex.Escape(LBrace));
            if (sweepRenderers.Success)
            {
                string srBody = ExtractMethodBody(code, sweepRenderers.Index);
                if (srBody != null && srBody.IndexOf("_floorSeen", StringComparison.Ordinal) >= 0)
                    failures.Add("[magenta-entrypoint] the shared per-renderer sweep touches _floorSeen - that " +
                                 "dedup is process-static and ground-only, and gating a mutable-state check " +
                                 "behind it is exactly the silent miss the colorless-floor RCA documents in " +
                                 "this same file");
            }
        }

        // =====================================================================
        //  CASE 2 - the spawn CHOKE POINT actually calls it
        // =====================================================================
        private static void Case2_ChokePointCallsIt(List<string> failures, List<string> notes)
        {
            string src = ReadSource(VisualFactorySrc, failures);
            if (src == null) return;
            string code = StripComments(src);

            // The funnel: the (Transform, string, SkinOptions) overload resolves the prefab then calls
            // the (Transform, GameObject, SkinOptions) overload. Verify that delegation still holds -
            // if it is ever broken, hooking only one overload silently misses half the callers.
            var strOverload = Regex.Match(code,
                @"static\s+GameObject\s+Skin\s*\(\s*Transform\s+\w+\s*,\s*string\s+\w+\s*,\s*SkinOptions\s+\w+\s*\)\s*" + Regex.Escape(LBrace));
            if (!strOverload.Success)
            {
                failures.Add("[magenta-hooked] VisualFactory no longer declares Skin(Transform, string, " +
                             "SkinOptions) - the funnel this oracle relies on changed shape; re-verify which " +
                             "overload is the choke point before trusting the hook");
            }
            else
            {
                string body = ExtractMethodBody(code, strOverload.Index);
                if (body != null && !Regex.IsMatch(body, @"return\s+Skin\s*\("))
                    failures.Add("[magenta-hooked] the string overload of VisualFactory.Skin no longer delegates " +
                                 "to the prefab overload - the two paths have forked, so a hook on one of them " +
                                 "covers only some of the runtime factories");
            }

            var prefabOverload = Regex.Match(code,
                @"static\s+GameObject\s+Skin\s*\(\s*Transform\s+\w+\s*,\s*GameObject\s+\w+\s*,\s*SkinOptions\s+\w+\s*\)\s*" + Regex.Escape(LBrace));
            if (!prefabOverload.Success)
            {
                failures.Add("[magenta-hooked] VisualFactory no longer declares Skin(Transform, GameObject, " +
                             "SkinOptions) - this is the shared choke point every runtime body funnels through");
                return;
            }

            string skinBody = ExtractMethodBody(code, prefabOverload.Index);
            if (skinBody == null)
            {
                failures.Add("[magenta-hooked] could not read the body of VisualFactory.Skin(prefab) - re-point " +
                             "this oracle deliberately");
                return;
            }

            if (!Regex.IsMatch(skinBody, @"MagentaGuard\s*\.\s*\w+\s*\(\s*go\b"))
                failures.Add("[magenta-hooked] VisualFactory.Skin(prefab) does NOT run a MagentaGuard sweep on " +
                             "the body it just built - this is DEFECT 1 verbatim: MagentaGuard only sweeps on " +
                             "SceneManager.sceneLoaded, so a troop instantiated mid-raid is never inspected and " +
                             "renders magenta for the whole raid");

            // It must run on the FINAL renderer set - after the render-verify and after the wardrobe
            // dress can swap outfit renderers - and before the body is handed back.
            int guardAt = skinBody.IndexOf("MagentaGuard", StringComparison.Ordinal);
            int verifyAt = skinBody.IndexOf("VerifyRenders", StringComparison.Ordinal);
            if (guardAt >= 0 && verifyAt >= 0 && guardAt < verifyAt)
                failures.Add("[magenta-hooked] the MagentaGuard sweep runs BEFORE VerifyRenders in " +
                             "VisualFactory.Skin - it would inspect a body that is about to be destroyed as a " +
                             "miss, and would miss any renderer the later wardrobe dress enables");

            notes.Add("choke point hooked in VisualFactory.Skin(prefab)");
        }

        // =====================================================================
        //  CASE 3 - one recovery, two seams (no drift)
        // =====================================================================
        private static void Case3_SharedRecovery(List<string> failures)
        {
            string code = StripComments(ReadSource(MagentaGuardSrc, failures) ?? string.Empty);
            if (code.Length == 0) return;

            if (!Regex.IsMatch(code, @"static\s+void\s+SweepRenderers\s*\("))
            {
                failures.Add("[magenta-shared] MagentaGuard has no SweepRenderers - the per-renderer recovery " +
                             "was not extracted, so the scene sweep and the runtime spawn sweep are two " +
                             "copies of the same logic and will drift (the project already carries three " +
                             "copies of the IsBrokenShader predicate for exactly this reason)");
                return;
            }

            // The SCENE sweep must route through the shared implementation, not keep its own copy.
            var sweep = Regex.Match(code, @"static\s+void\s+Sweep\s*\(\s*string\s+\w+\s*\)\s*" + Regex.Escape(LBrace));
            if (!sweep.Success)
            {
                failures.Add("[magenta-shared] MagentaGuard.Sweep(string) is gone - the scene-load seam changed " +
                             "shape; re-verify that it still shares the recovery implementation");
                return;
            }
            string sweepBody = ExtractMethodBody(code, sweep.Index);
            if (sweepBody != null && sweepBody.IndexOf("SweepRenderers", StringComparison.Ordinal) < 0)
                failures.Add("[magenta-shared] the scene sweep does not call SweepRenderers - it has grown its " +
                             "own second copy of the recovery, which means a fix applied at the spawn seam will " +
                             "not reach the scene seam (or the reverse)");
        }

        // =====================================================================
        //  CASE 4 - GENERIC: no runtime character body ships animator-less
        // =====================================================================
        private static void Case4_EveryBodyPathBindsAnimator(List<string> failures, List<string> notes)
        {
            if (!Directory.Exists(ModulesRoot))
            {
                failures.Add("[body-animator] " + ModulesRoot + " not found - the module tree moved");
                return;
            }

            int checkedCount = 0;
            foreach (string path in Directory.GetFiles(ModulesRoot, "*.cs", SearchOption.AllDirectories))
            {
                string raw;
                try { raw = File.ReadAllText(path); }
                catch { continue; }

                string code = StripComments(raw);

                // VisualFactory itself DEFINES the skinner; it is not a body path.
                if (Regex.IsMatch(code, @"(static\s+)?class\s+VisualFactory\b")) continue;

                // A CHARACTER body path: it skins through VisualFactory and the model it asks for is a
                // rigged character (a Heroes/ or Enemies/ Resources path, or the hero prefab loader).
                // Structures / props / mine nodes are skinned the same way but have no rig, so they are
                // correctly excluded by this predicate rather than by a hard-coded ignore list.
                if (code.IndexOf("VisualFactory.Skin", StringComparison.Ordinal) < 0) continue;
                if (!Regex.IsMatch(code, @"""Heroes/|""Enemies/|LoadHeroPrefab")) continue;

                checkedCount++;

                bool binds = Regex.IsMatch(code, @"runtimeAnimatorController\s*=")
                          || Regex.IsMatch(code, @"\w*AnimatorFactory\s*\.\s*Apply\s*\(")
                          || Regex.IsMatch(code, @"Apply\w*Animator\s*\(");
                if (!binds)
                    failures.Add("[body-animator] " + path.Replace('\\', '/') + " skins a rigged character body " +
                                 "through VisualFactory but never binds an AnimatorController (no " +
                                 "runtimeAnimatorController assignment, no *AnimatorFactory.Apply, no " +
                                 "Apply*Animator call) - this is DEFECT 2 verbatim: the body spawns, the agent " +
                                 "moves the root, and the rig holds its bind pose because nothing ever gave it " +
                                 "a controller to play");
            }

            if (checkedCount == 0)
                failures.Add("[body-animator] found ZERO runtime character-body paths to check - the detection " +
                             "predicate stopped matching (VisualFactory.Skin + a Heroes//Enemies/ model), so " +
                             "this case is silently passing on nothing");
            else
                notes.Add("body paths checked = " + checkedCount);
        }

        // =====================================================================
        //  CASE 5 - the animator is bound BEFORE Awake caches the parameter set
        // =====================================================================
        private static void Case5_AnimatorBoundBeforeController(List<string> failures)
        {
            string factory = StripComments(ReadSource(TroopFactorySrc, failures) ?? string.Empty);
            if (factory.Length == 0) return;

            var animCall = Regex.Match(factory, @"Apply\w*Animator\s*\(");
            var addCtrl = Regex.Match(factory, @"AddComponent\s*<\s*TroopController\s*>\s*\(");

            if (!animCall.Success)
            {
                failures.Add("[animator-order] TroopFactory makes no Apply*Animator call - the troop body is " +
                             "built with a collider, a NavMeshAgent and a TroopController but no animator " +
                             "controller, which is the shipped defect");
                return;
            }
            if (!addCtrl.Success)
            {
                failures.Add("[animator-order] TroopFactory no longer adds a TroopController - the build recipe " +
                             "changed shape; re-verify the ordering constraint deliberately");
                return;
            }
            if (animCall.Index > addCtrl.Index)
                failures.Add("[animator-order] TroopFactory binds the animator AFTER " +
                             "AddComponent<TroopController>() - AddComponent runs Awake SYNCHRONOUSLY, and " +
                             "TroopController.Awake caches which parameters the bound controller declares. " +
                             "Binding afterwards leaves every cached flag false, so no parameter is ever " +
                             "written and the troop slides with all of the fix code still present");

            // The consumer half: Awake must still be the thing that caches the flags, or the ordering
            // constraint above is pinning something that no longer matters.
            string ctrl = StripComments(ReadSource(TroopControllerSrc, failures) ?? string.Empty);
            if (ctrl.Length > 0 && !Regex.IsMatch(ctrl, @"_animator\s*\.\s*parameters|_animator\.parameters"))
                failures.Add("[animator-order] TroopController no longer scans _animator.parameters - the " +
                             "ordering pin above is now guarding nothing; re-derive what the driver depends on");
        }

        // =====================================================================
        //  CASE 6 - DATA: models resolve, and the fallback controllers can be driven
        // =====================================================================
        private static void Case6_TroopControllerAssets(List<string> failures, List<string> notes)
        {
            if (!File.Exists(TroopsJson))
            {
                failures.Add("[troop-controllers] " + TroopsJson + " not found - the shipped player loads the " +
                             "Resources copy, so the roster cannot be verified");
                return;
            }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(TroopsJson)); }
            catch (Exception ex)
            {
                failures.Add("[troop-controllers] troops.json failed to parse (" + ex.GetType().Name + ": " +
                             ex.Message + ")");
                return;
            }

            var arr = root["troops"] as JArray;
            if (arr == null || arr.Count == 0)
            {
                failures.Add("[troop-controllers] troops.json has no non-empty 'troops' array");
                return;
            }

            var neededControllers = new SortedSet<string>(StringComparer.Ordinal);
            int rows = 0;
            foreach (var t in arr)
            {
                rows++;
                string id = (string)t["id"] ?? "<no-id>";
                string model = (string)t["model"];
                string role = ((string)t["role"] ?? "melee").Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(model))
                {
                    failures.Add("[troop-controllers] '" + id + "' has no 'model' - TroopFactory falls straight " +
                                 "through to the tinted-capsule placeholder, so this troop can never animate");
                    continue;
                }

                if (!TroopModelAssetExists(model))
                    failures.Add("[troop-controllers] '" + id + "' points at model '" + model + "' but no asset " +
                                 "resolves at Resources/" + ResolveTroopModelRel(model) +
                                 " (.prefab/.fbx) - VisualFactory.Skin returns null and the troop deploys as a capsule");

                // The fallback ladder TroopFactory walks: the model's own controller, then the shared
                // controller for its ROLE, then Knight. At least the role controller must be drivable.
                // WO-933 siege machines skip humanoid animator bind entirely — no role controller needed.
                if (role != "siege")
                    neededControllers.Add(role == "ranged" ? "Ranger" : "Knight");
            }

            neededControllers.Add("Knight");   // the last-resort fallback, always required

            foreach (string c in neededControllers)
            {
                string path = HeroesRes + "/" + c + ".controller";
                if (!File.Exists(path))
                {
                    failures.Add("[troop-controllers] the fallback controller '" + path + "' does not exist - a " +
                                 "troop whose model has no controller of its own has nothing to fall back on and " +
                                 "will slide with no walk/attack/death animation");
                    continue;
                }
                if (!ControllerDeclaresParam(path, DriveParam))
                    failures.Add("[troop-controllers] '" + path + "' does not declare a '" + DriveParam + "' " +
                                 "parameter - TroopController writes exactly Speed/Attack/Hit/Dead, so binding " +
                                 "this controller reproduces the Supercyan StrafeMovement failure: a controller " +
                                 "is attached, it looks wired, and not one parameter write reaches it");
            }

            notes.Add("troops=" + rows + ", fallback controllers=" + string.Join("/", new List<string>(neededControllers).ToArray()));
        }

        /// <summary>
        /// Resolves a troops.json model field the same way TroopFactory does:
        /// bare names → Heroes/&lt;name&gt;; slash paths → full Resources path
        /// (e.g. Structures/Catapult for WO-933 siege machines).
        /// </summary>
        private static string ResolveTroopModelRel(string model)
        {
            if (string.IsNullOrEmpty(model)) return "";
            if (model.IndexOf('/') >= 0 || model.IndexOf('\\') >= 0)
                return model.Replace('\\', '/');
            return "Heroes/" + model;
        }

        /// <summary>True if Resources carries a loadable asset for this troop model field.</summary>
        private static bool TroopModelAssetExists(string model)
        {
            string rel = ResolveTroopModelRel(model);
            if (string.IsNullOrEmpty(rel)) return false;

            // ⛔ STRUCTURE ART NO LONGER LIVES UNDER Resources.
            // A "Structures/..." key migrated to AssetRoots.StructureContent on 2026-08-18 (an
            // Addressable group served from the CDN); everything else is still Resources-resident.
            // Checking only Resources reported 'troop-catapult' as a missing model on a tree where
            // the asset is present and loadable — a false failure from testing where the file USED
            // to be. One prefix, one root; the runtime makes the same distinction via
            // StructureAssetLoader.
            const string structPrefix = "Structures/";
            string basePath = rel.StartsWith(structPrefix, System.StringComparison.OrdinalIgnoreCase)
                ? DeNelle.Core.AssetRoots.StructureContent + "/" + rel.Substring(structPrefix.Length)
                : "Assets/Resources/" + rel;

            foreach (string ext in new[] { ".prefab", ".fbx", ".FBX" })
                if (File.Exists(basePath + ext)) return true;
            return false;
        }

        /// <summary>True if Resources/Heroes carries a loadable asset for this model id.</summary>
        private static bool HeroAssetExists(string model) => TroopModelAssetExists(model);

        /// <summary>
        /// Reads the .controller YAML and reports whether it declares <paramref name="param"/> in its
        /// m_AnimatorParameters block. Deliberately reads the ASSET, not C# source: what broke here was
        /// a vendor controller whose parameter names simply did not match the driver, and only the
        /// asset can answer that.
        /// </summary>
        private static bool ControllerDeclaresParam(string path, string param)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { return false; }

            int start = text.IndexOf("m_AnimatorParameters:", StringComparison.Ordinal);
            if (start < 0) return false;
            int end = text.IndexOf("m_AnimatorLayers:", start, StringComparison.Ordinal);
            string block = end > start ? text.Substring(start, end - start) : text.Substring(start);
            return Regex.IsMatch(block, @"^\s*-\s*m_Name:\s*" + Regex.Escape(param) + @"\s*$", RegexOptions.Multiline);
        }

        // =====================================================================
        //  CASE 7 - IDENTITY: a troop spawn can always be attributed (WO-1748)
        // =====================================================================
        //
        // THE DEFECT THIS PINS (captured, not theorised). Eight device captures between
        // 2026-09-12 and 2026-09-15 (F8 seq 5057, 5062, 5066, 5109, 5118, 5123, 5128, 5258,
        // all scene RaidBase_IronBastion) carried the identical error:
        //
        //     [Flow:TroopVisual] id=: NO Animator anywhere under the troop root ...
        //
        // with the id EMPTY. Two separate instrument defects produced that, and neither was a
        // game defect:
        //
        //   (1) UNATTRIBUTABLE. TroopController.Awake reported the verdict, and Awake runs
        //       SYNCHRONOUSLY inside TroopFactory's AddComponent - one line BEFORE Configure()
        //       assigns _troopId. So the id was structurally empty on EVERY troop the game has
        //       ever spawned, and the WO's own inference from it ("the empty id narrows the
        //       spawn path") was false: the capture's stack named an ordinary
        //       RaidDeployController.DeployAll deploy.
        //   (2) FALSE ALARM ON SIEGE. TroopFactory skips ApplyTroopAnimator for role "siege"
        //       (a siege machine is a prop, not a rig), and troop-catapult's model address
        //       "Structures/Catapult" resolves to Assets/StructureContent/Synty/Catapult.prefab,
        //       which contains ZERO Animator components. So a catapult tripped a ship-blocking-
        //       looking Fail on every single deploy for a body that was working as authored.
        //
        // The pins below make both impossible to reintroduce silently. They are deliberately
        // ORDERING + LOCATION pins over source, plus a data pin on troops.json, because the
        // thing that broke was WHERE a line was emitted, not WHAT it measured.
        private static void Case7_SpawnIsAttributable(List<string> failures, List<string> notes)
        {
            string factoryRaw = ReadSource(TroopFactorySrc, failures);
            string ctrlRaw = ReadSource(TroopControllerSrc, failures);
            if (string.IsNullOrEmpty(factoryRaw) || string.IsNullOrEmpty(ctrlRaw)) return;

            // ---- A. The factory names the troop + the address BEFORE AddComponent --------
            string factory = StripComments(factoryRaw);
            var addCtrl = Regex.Match(factory, @"AddComponent\s*<\s*TroopController\s*>\s*\(");
            int identityAt = factory.IndexOf("AddComponent<TroopController> - address='",
                                             StringComparison.Ordinal);
            if (identityAt < 0)
                failures.Add("[troop-identity] TroopFactory emits no spawn IDENTITY line naming the resolved " +
                             "address at the AddComponent<TroopController> site. Without it the only line a " +
                             "failing troop produces is TroopController's verdict, which cannot name the art " +
                             "that resolved - that is exactly how F8 seq 5057..5258 stayed unattributable");
            else if (addCtrl.Success && identityAt > addCtrl.Index)
                failures.Add("[troop-identity] TroopFactory's spawn identity line is emitted AFTER " +
                             "AddComponent<TroopController>() - AddComponent runs Awake synchronously, so any " +
                             "failure Awake reports would still print with no identity above it");

            // ---- B. The missing-mesh branch reports through FlowTrace, not Debug ---------
            // Same reasoning already recorded for the off-mesh branch at the top of TroopFactory:
            // a Debug.LogWarning is INVISIBLE to the F8 break-capture harness, so the one line
            // that names WHICH model failed to load never reaches a capture.
            if (factory.IndexOf("had NO loadable mesh", StringComparison.Ordinal) < 0)
                failures.Add("[troop-identity] TroopFactory's missing-mesh fallback branch no longer carries its " +
                             "FlowTrace report - re-verify deliberately that the address that failed still " +
                             "reaches an F8 capture");
            if (Regex.IsMatch(factory, @"Debug\s*\.\s*LogWarning\s*\(\s*\$?""\[TroopFactory\] model"))
                failures.Add("[troop-identity] TroopFactory's missing-mesh fallback reverted to a bare " +
                             "Debug.LogWarning. The F8 harness captures errors and exceptions, NOT warnings, so " +
                             "the single most consequential spawn failure would again produce no evidence");

            // ---- C. The verdict is NOT reported from Awake -------------------------------
            string ctrl = StripComments(ctrlRaw);
            const string VerdictToken = "NO Animator anywhere";
            var awakeSig = Regex.Match(ctrl, @"(private|protected|public)?\s*void\s+Awake\s*\(\s*\)");
            if (!awakeSig.Success)
            {
                notes.Add("[troop-identity] TroopController declares no Awake - the location pin was skipped");
            }
            else
            {
                string awakeBody = ExtractMethodBody(ctrl, awakeSig.Index);
                if (awakeBody == null)
                    failures.Add("[troop-identity] TroopController.Awake's body does not brace-balance - the " +
                                 "location pin cannot be evaluated");
                else if (awakeBody.IndexOf(VerdictToken, StringComparison.Ordinal) >= 0)
                    failures.Add("[troop-identity] TroopController reports the animator verdict from Awake " +
                                 "again. Awake runs inside AddComponent, BEFORE Configure assigns _troopId, so " +
                                 "every line it prints carries an EMPTY id and no capture can be attributed to " +
                                 "a troop. Report it from Configure instead");
                // The caching MUST stay in Awake - that is the bind-order law Case 5 pins.
                else if (!Regex.IsMatch(awakeBody, @"_animator\s*\.\s*parameters"))
                    failures.Add("[troop-identity] moving the verdict out of Awake also moved the parameter " +
                                 "CACHING out. The caching is bind-order-critical and must stay in Awake");
            }

            // ---- D. ...and it IS reported from Configure, after the id is set ------------
            var configureSig = Regex.Match(ctrl, @"public\s+void\s+Configure\s*\(\s*TroopDef");
            if (!configureSig.Success)
            {
                failures.Add("[troop-identity] TroopController.Configure(TroopDef, ...) is gone - the seam that " +
                             "gives a troop its id changed shape; re-derive where the verdict must be reported");
            }
            else
            {
                string cfgBody = ExtractMethodBody(ctrl, configureSig.Index);
                if (cfgBody == null)
                {
                    failures.Add("[troop-identity] TroopController.Configure's body does not brace-balance - " +
                                 "the attribution pin cannot be evaluated");
                }
                else
                {
                    int idAt = cfgBody.IndexOf("_troopId", StringComparison.Ordinal);
                    int reportAt = cfgBody.IndexOf("ReportVisualVerdict", StringComparison.Ordinal);
                    if (reportAt < 0)
                        failures.Add("[troop-identity] Configure does not call ReportVisualVerdict - the animator " +
                                     "verdict is no longer emitted from the only place the troop has a name");
                    else if (idAt < 0 || idAt > reportAt)
                        failures.Add("[troop-identity] Configure reports the animator verdict BEFORE it assigns " +
                                     "_troopId - the line would carry an empty id, which is the original defect");
                }
            }

            // ---- E. Siege is carved out of the Fail, not lumped into it -----------------
            var verdictSig = Regex.Match(ctrl, @"private\s+void\s+ReportVisualVerdict\s*\(\s*\)");
            if (!verdictSig.Success)
            {
                failures.Add("[troop-identity] ReportVisualVerdict is gone - the siege carve-out and the " +
                             "attribution both lived there; re-derive both deliberately");
            }
            else
            {
                string vBody = ExtractMethodBody(ctrl, verdictSig.Index);
                if (vBody == null)
                {
                    failures.Add("[troop-identity] ReportVisualVerdict's body does not brace-balance");
                }
                else
                {
                    int siegeAt = vBody.IndexOf("SIEGE machine", StringComparison.Ordinal);
                    int failAt = vBody.IndexOf(VerdictToken, StringComparison.Ordinal);
                    if (siegeAt < 0)
                        failures.Add("[troop-identity] ReportVisualVerdict has no SIEGE branch. A siege machine " +
                                     "is animator-less BY DESIGN (TroopFactory skips the humanoid bind for role " +
                                     "'siege', and Structures/Catapult ships no Animator), so without the " +
                                     "carve-out every catapult deploy raises a false ship-blocking error");
                    else if (failAt >= 0 && siegeAt > failAt)
                        failures.Add("[troop-identity] the SIEGE branch no longer precedes the no-animator Fail - " +
                                     "a catapult would reach the Fail before the carve-out can return");
                }
            }

            // ---- F. DATA: nothing in troops.json can produce an empty id ----------------
            // Post-Configure, _troopId IS def.Id - so an empty id in the data is the one
            // remaining way an unattributable troop can exist.
            if (!File.Exists(TroopsJson))
            {
                failures.Add("[troop-identity] " + TroopsJson + " not found - the roster moved");
                return;
            }
            try
            {
                var root = JObject.Parse(File.ReadAllText(TroopsJson));
                var arr = root["troops"] as JArray;
                if (arr == null || arr.Count == 0)
                {
                    failures.Add("[troop-identity] troops.json declares no troops[] array - re-point this oracle");
                    return;
                }
                int siegeSeen = 0;
                foreach (var t in arr)
                {
                    string id = (string)t["id"];
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        failures.Add("[troop-identity] a troops.json entry has an EMPTY id. _troopId is def.Id, " +
                                     "so that troop is unattributable in every [Flow:*] line it ever emits");
                        continue;
                    }
                    if (string.Equals((string)t["role"], "siege", StringComparison.OrdinalIgnoreCase))
                        siegeSeen++;
                    if (string.IsNullOrWhiteSpace((string)t["model"]))
                        failures.Add("[troop-identity] troop '" + id + "' authors no model - it can only ever " +
                                     "spawn as an animator-less fallback body");
                }
                if (siegeSeen == 0)
                    notes.Add("[troop-identity] troops.json declares no role 'siege' entry - the carve-out " +
                              "pinned above currently covers nothing; confirm that is intended");
            }
            catch (Exception ex)
            {
                failures.Add("[troop-identity] troops.json did not parse: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private static string ReadSource(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("[source] " + path + " not found - the file moved without updating this oracle");
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("[source] could not read " + path + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Strips // and /* */ comments so a lint can never be satisfied by prose.</summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", " ");
        }

        /// <summary>
        /// The brace-balanced body of the method whose signature starts at <paramref name="from"/>.
        /// Comment-stripped input only. Returns null if the braces do not balance (which is itself a
        /// signal the caller should report rather than silently pass).
        /// </summary>
        private static string ExtractMethodBody(string code, int from)
        {
            int open = code.IndexOf(LBraceCh, from);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == LBraceCh) depth++;
                else if (code[i] == RBraceCh)
                {
                    depth--;
                    if (depth == 0) return code.Substring(open, i - open + 1);
                }
            }
            return null;
        }
    }
}
