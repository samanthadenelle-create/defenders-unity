// =============================================================================
// EnemyAssetTypeScreenRegression — WO-1097 ACCEPTANCE ITEM 4.
//
// The ticket's item 4 reads, verbatim:
//     "The doc example at `EnemyAssetLoader.cs:112-115` compiles and works as written."
//
// Items 3 and 5 are ALREADY pinned by ApexDragonSpawnRegression (GameObject-address
// resolution in WaveManager + the clear gate). This suite deliberately does NOT repeat
// either of them — a second copy of a live assertion is the duplicated-state failure
// CLAUDE.md §2/§5 records. It pins the OTHER half: the API's own documentation, and the
// type screen that makes the documented rule enforceable instead of advisory.
//
// WHY A SCREEN AND NOT A COMPILE ERROR: C# cannot express "UnityEngine.Object but not
// UnityEngine.Component", so `where T : Object` cannot be tightened. The runtime refusal
// IS the guard, and this suite is what proves it fires.
//
// EVIDENCE, NOT INFERENCE (§11B/§12): the refusal case does not lint for a FlowTrace call
// in the source — it installs a capturing ITraceSink, makes the real call, and reads the
// line the seam actually emitted. Source text says what the code contains; the captured
// line says what it DID.
//
// RED-FIRST (the two mutations that must turn this suite red):
//   1. Delete the `if (IsUnsatisfiableComponentRequest(typeof(T)))` block from
//      EnemyAssetLoader.Load<T> -> the DragonBoss request falls through to the resolver,
//      no marker is captured -> case 1 fails ("refusal not traced").
//   2. Restore the old XML doc example (the DragonBoss-typed call) -> case 4 fails.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>Pins WO-1097 acceptance item 4: the loader's documented example is the working
    /// shape, and a Component-typed request is refused loudly instead of failing forever.</summary>
    public static class EnemyAssetTypeScreenRegression
    {
        private const string LoaderRel = "_Modules/Core/Addressables/EnemyAssetLoader.cs";

        /// <summary>Captures FlowTrace output instead of writing it to the console, so the gate can
        /// assert on the exact line the seam emitted.</summary>
        private sealed class CapturingSink : ITraceSink
        {
            public readonly List<string> Lines = new List<string>();
            public void Info(string line)  { Lines.Add(line); }
            public void Warn(string line)  { Lines.Add(line); }
            public void Error(string line) { Lines.Add(line); }
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            // ---- CASE 1 + 2: behaviour, captured -----------------------------
            // A Component-typed request must be REFUSED (null) and TRACED (address + type named),
            // and it must never throw — it runs on the player path from wave callbacks.
            var sink = new CapturingSink();
            ITraceSink priorSink = FlowTrace.Sink;
            bool priorEnabled = FlowTrace.Enabled;
            DragonBoss componentResult = null;
            System.Exception gameObjectCallThrew = null;
            int markerLinesAfterGameObjectCall = 0;

            try
            {
                FlowTrace.Enabled = true;
                FlowTrace.Sink = sink;
                EnemyAssetLoader.ResetTypeScreenReports();   // deterministic regardless of what ran first

                try
                {
                    componentResult = EnemyAssetLoader.LoadEnemyAsset<DragonBoss>("Enemies/Boss_Dragon");
                }
                catch (System.Exception e)
                {
                    failures.Add("a Component-typed request THREW (" + e.GetType().Name +
                                 ") — the screen must refuse on the player path, never throw");
                }

                if (componentResult != null)
                    failures.Add("a Component-typed request was not refused: LoadEnemyAsset<DragonBoss>" +
                                 "(\"Enemies/Boss_Dragon\") returned non-null");

                string refusal = sink.Lines.Find(l => l != null && l.Contains(EnemyAssetLoader.TypeScreenMarker));
                if (refusal == null)
                {
                    failures.Add("the Component-typed refusal was NOT traced: no '" +
                                 EnemyAssetLoader.TypeScreenMarker + "' line was captured — a silent null " +
                                 "reads as 'not downloaded yet' and never self-heals (WO-1097)");
                }
                else
                {
                    if (!refusal.Contains("Enemies/Boss_Dragon"))
                        failures.Add("the refusal line does not name the ADDRESS");
                    if (!refusal.Contains(typeof(DragonBoss).FullName) && !refusal.Contains("DragonBoss"))
                        failures.Add("the refusal line does not name the REQUESTED TYPE");
                }

                // ---- CASE 3: the documented shape is NOT screened ------------
                // A GameObject-typed request must pass the screen untouched. We assert only that no
                // refusal fired — NOT that the asset resolves; resolution is item 5's domain and
                // depends on Addressables/AssetDatabase state in batchmode.
                int markerLinesBefore = sink.Lines.FindAll(
                    l => l != null && l.Contains(EnemyAssetLoader.TypeScreenMarker)).Count;
                try
                {
                    EnemyAssetLoader.LoadEnemyPrefab("Boss_Dragon");
                }
                catch (System.Exception e)
                {
                    gameObjectCallThrew = e;
                }
                markerLinesAfterGameObjectCall = sink.Lines.FindAll(
                    l => l != null && l.Contains(EnemyAssetLoader.TypeScreenMarker)).Count;

                if (gameObjectCallThrew != null)
                    failures.Add("the documented GameObject-typed call THREW (" +
                                 gameObjectCallThrew.GetType().Name + ")");
                if (markerLinesAfterGameObjectCall != markerLinesBefore)
                    failures.Add("the type screen fired on a GameObject-typed request — the screen is " +
                                 "over-broad and would refuse every legitimate prefab load");
            }
            finally
            {
                FlowTrace.Sink = priorSink;
                FlowTrace.Enabled = priorEnabled;
                EnemyAssetLoader.ResetTypeScreenReports();
            }

            // ---- CASE 3b: the predicate itself -------------------------------
            if (!EnemyAssetLoader.IsUnsatisfiableComponentRequest(typeof(DragonBoss)))
                failures.Add("the screen does not classify a MonoBehaviour as unsatisfiable");
            if (!EnemyAssetLoader.IsUnsatisfiableComponentRequest(typeof(Transform)))
                failures.Add("the screen does not classify a built-in Component as unsatisfiable");
            if (EnemyAssetLoader.IsUnsatisfiableComponentRequest(typeof(GameObject)))
                failures.Add("the screen wrongly rejects GameObject — the type Addressables publishes");
            if (EnemyAssetLoader.IsUnsatisfiableComponentRequest(typeof(RuntimeAnimatorController)))
                failures.Add("the screen wrongly rejects RuntimeAnimatorController");
            if (EnemyAssetLoader.IsUnsatisfiableComponentRequest(typeof(Texture)))
                failures.Add("the screen wrongly rejects Texture");

            // ---- CASE 4: the DOC example (acceptance item 4, literally) ------
            string path = Path.Combine(Application.dataPath, LoaderRel);
            string source = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (source.Length == 0)
            {
                failures.Add("Assets/" + LoaderRel + " is missing — the enemy seam has moved");
            }
            else
            {
                if (source.Contains("LoadEnemyAsset&lt;DragonBoss&gt;(\"Enemies/Boss_Dragon\")"))
                    failures.Add("the XML doc STILL prescribes the broken Component-typed call — " +
                                 "anyone following the docs writes WO-1097 again");
                if (!source.Contains("LoadEnemyAsset&lt;GameObject&gt;(\"Enemies/Boss_Dragon\")"))
                    failures.Add("the XML doc no longer shows the working GameObject-typed example");
                if (!source.Contains("GetComponentInChildren&lt;DragonBoss&gt;(true)"))
                    failures.Add("the XML doc no longer shows the prefab-to-component walk that makes " +
                                 "the example actually work");
                if (!source.Contains("IsUnsatisfiableComponentRequest(typeof(T))"))
                    failures.Add("EnemyAssetLoader.Load<T> no longer screens the requested type");
                if (!source.Contains("TypeScreenMarker"))
                    failures.Add("the type-screen marker const is gone — the pin and the producer would drift");
            }

            reason = failures.Count == 0
                ? "ENEMY_ASSET_TYPE_SCREEN_OK Component-typed request refused + traced (address and type " +
                  "named, no throw); GameObject-typed request unscreened; XML doc example is the working shape"
                : "ENEMY_ASSET_TYPE_SCREEN_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}
