// =============================================================================
// RealmStorePlacer — bakes the Realm Store storefront into the hub.
// -----------------------------------------------------------------------------
// PROD-003, owner ruling 2026-08-18: placement option (a) — the south plaza,
// ACROSS the plaza from Coppin rather than beside him, so the two read as
// different establishments in one market rather than two stalls of the same one.
//
// ⛔ PLACEMENT IS CONSTRAINED BY A LESSON ALREADY PAID FOR. Coppin's market
// (Marketplace_Monetization) sits at (0, 0, 32) — due north. "Across the plaza"
// therefore points at (0, 0, -32), which is EXACTLY where the Jeweler used to be
// and was removed from, because it BLOCKED THE SOUTH DOOR (CastleHubBuilder's own
// comment: "Jeweler (Gems) REMOVED from the fixed ring — it was blocking the
// south door. Do NOT re-add a fixed Jeweler here."). The south gate reads from
// the recipe at x ~= -4.37, z ~= -40.6, so the doorway corridor runs up the
// middle of that face.
// The store therefore sits at (12, 0, -32): still the south plaza, still facing
// Coppin across the open centre, but offset ~16 units east of the gate corridor
// and ~14 from the nearest existing structure (Lumbermill at 22,0,-22). Putting
// the game's only storefront where it blocks the main entrance would be a
// self-inflicted wound of the same shape PROD-003 exists to avoid.
//
// ⛔ NOT ADDED TO structures-catalog.json, AND THAT IS THE POINT. A catalog row
// would put it in the build palette, making it sellable / movable / damageable —
// each of which is a way for the player to take their own store offline. It is
// baked furniture, like the Heart, and it is not an IDamageableStructure.
//
// Idempotent: re-running replaces the existing instance rather than stacking a
// second storefront in the scene.
//
// -----------------------------------------------------------------------------
// SCALE IS DERIVED, NOT TYPED (ARCHITECTURE_PRINCIPLES section 4).
// The first version of this script instantiated the FBX RAW. Measured, the model
// is about 1.0 x 0.6 x 1.2 m, standing next to neighbours fitted to 4 m - a
// waist-high shed where the ticket (section 3.4) asks for the one building a
// player can find without being told. The fix is NOT a hand-typed multiplier: the
// storefront is now skinned through VisualFactory.Skin - the SAME seam every
// catalog structure flows through - with FitHeight = StructureFactory.YHeightVariable
// * HeightMul, so it inherits the town's height cadence and re-scales with it when
// that one number moves. HeightMul stays at the uniform 1.0 building base on
// purpose: the cadence names the Cathedral of Magic as the ONE landmark, and
// promoting a second one is a creative call for the owner, not something a fix
// should smuggle in. If she wants it to read taller, this constant is the knob.
//
// KNOWN TRADE-OFF, stated rather than hidden: VisualFactory.Skin uses
// Object.Instantiate, so the baked storefront is a PLAIN hierarchy, not a prefab
// instance linked back to RealmStore.fbx. Editing the FBX therefore no longer
// updates the scene by itself — you re-run this placer, which is the workflow the
// header already mandates and what the [realm-storefront] oracle checks. The
// alternative was to keep the prefab link and hand-write the fit maths here, i.e.
// a second copy of the scaling law that would drift from the town's. One re-run is
// cheaper than one more source of truth.
//
// -----------------------------------------------------------------------------
// THE COLLIDER IS RECOMPUTED EVERY RUN, AND THAT IS THE POINT.
// Commit f995c4706 set bakeAxisConversion on RealmStore.fbx, re-orienting the mesh
// at IMPORT, and did not re-run this placer - so the saved scene kept a collider
// (size 1.034 x 0.620 x 1.195, centre z 0.4999) describing a mesh shape that no
// longer exists. A producer script cannot see a stale artifact it did not write.
// So: the box is measured from the CURRENT renderer bounds on every run, with the
// root temporarily unrotated so world axes equal local axes (the old code assigned
// a world-space AABB of a YAWED object straight into a local-space box, which is
// inflated by the yaw even when the mesh is right), and the saved result is READ
// BACK and asserted before this script claims success.
//
// -----------------------------------------------------------------------------
// DURABILITY COUPLING - READ THIS BEFORE REBUILDING THE HUB.
// This storefront is placed by THIS standalone script at scene root. CastleHubBuilder
// does NOT create it and never will, so the documented "new empty scene + rebuild the
// hub" workflow SILENTLY DROPS THE GAME'S ONLY STOREFRONT. Restructuring the hub
// builder to own it is structural work and is deliberately out of scope here; the
// loss is made LOUD instead, by RealmStorefrontRegression [realm-storefront], which
// pins the object in the SAVED scene and goes red the moment a bake drops it.
// >>> AFTER ANY Defenders > Scenes > Build CastleHub_MainKeep, RE-RUN
// >>> DeNelle.Editor.RealmStorePlacer.Run AND THEN RE-BAKE THE NAVMESH. <<<
// =============================================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    /// <summary>Bakes the PROD-003 Realm Store storefront + its vendor into the hub scene.</summary>
    public static class RealmStorePlacer
    {
        private const string HubScene   = "Assets/Scenes/Main_Castle_Overworld.unity";
        private const string ObjectName = "RealmStore_Storefront";
        private const string OkMarker   = "REALM_STORE_PLACED_OK";

        /// <summary>
        /// FALLBACK ONLY — the owner-ruled (a) position, used when Offset Forge has no authored
        /// placement for this storefront.
        /// <para>
        /// ⛔ THE AUTHORED VALUE WINS. Owner principle, applied consistently all session: a value
        /// the owner tunes must live in DATA, not in code. Hardcoding the transform here would make
        /// every nudge a code edit and a round trip through me — the same mistake as the hardcoded
        /// asset paths that became AssetRoots. Offset Forge already stores pos/rot/scale by id and
        /// is the tool she tunes with; this reads what it authored.
        /// </para>
        /// </summary>
        private static readonly Vector3 FallbackPlacement = new Vector3(12f, 0f, -32f);

        /// <summary>Offset Forge key. Matches the asset name, which is how that file is keyed.</summary>
        private const string OffsetKey = "RealmStore";

        /// <summary>
        /// THE OWNER-RULED ORIENTATION CORRECTION for RealmStore.fbx, applied to the SKINNED CHILD as
        /// <see cref="DeNelle.Village.SkinOptions.LocalRotation"/> — i.e. BEFORE Fit + SeatOnGround.
        ///
        /// <para>⛔ OWNER 2026-08-19 (GROK_BRIEF_STRUCTURE_ORIENTATION): <c>rot 90,0,0</c> —
        /// pos 0,0,0 / Y = ground / flagged fixed. Calibrated against FBX vertex extents: Z runs
        /// [0,H] (true vertical); after bakeAxisConversion Unity negates Z so imported up is local
        /// −Z; <c>Euler(90,0,0)</c> puts that at world up (right way up). <c>Euler(−90,0,0)</c> is
        /// AABB-identical but UPSIDE-DOWN. The 2026-08-18 <c>Euler(0,180,90)</c> left local +X at
        /// world up — still on its side (device build 331367). Do not revert to (0,180,90).</para>
        ///
        /// <para>WHY THE CHILD AND NOT THE ROOT: the root's rotation is COMPUTED every run
        /// (LookRotation toward the plaza centre). LocalRotation is UPSTREAM of Fit so height
        /// measures the upright axis (~4.00 m) and the footprint shrinks (~2.9 m across).</para>
        ///
        /// <para>WHY NOT Offset Forge <c>rot</c>: inert for this id (placer reads <c>pos</c> only);
        /// <c>TripoAxisBake</c> would zero a flagged rot. Facing yaw stays on the root LookRotation.</para>
        /// </summary>
        private static readonly Quaternion AuthoredCorrection = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>
        /// The storefront's place in the town HEIGHT CADENCE, expressed the way every catalog row
        /// expresses it: a multiplier on <c>StructureFactory.YHeightVariable</c> (RepoProps.heightMul).
        /// 1.0 = the uniform building base (4 m). It is a MULTIPLIER, not a size, precisely so this
        /// building re-scales with the town when that one number moves - a typed metre value here
        /// would drift the day the cadence changes and nobody would know until a screenshot.
        /// </summary>
        private const float HeightMul = 1.0f;

        /// <summary>
        /// The fit-to-height target in metres, DERIVED from the shared cadence. Public so the
        /// [realm-storefront] oracle can assert the SAVED scene against the producer's own current
        /// answer instead of against a number copied into a second file (a copied number is how the
        /// collider went stale in the first place).
        /// </summary>
        public static float FitHeightMeters =>
            DeNelle.Village.StructureFactory.YHeightVariable * HeightMul;

        /// <summary>
        /// The placement this script WOULD use right now: the Offset Forge authored position when one
        /// exists, otherwise the owner-ruled fallback. Public for the same reason as
        /// <see cref="FitHeightMeters"/> - the oracle compares the artifact to the producer, so an
        /// owner nudge in Offset Forge that was never re-baked shows up as a red suite rather than as
        /// a building standing somewhere nobody authored.
        /// </summary>
        public static Vector3 ResolvePlacement()
            => TryReadAuthoredPosition(out var authored) ? authored : FallbackPlacement;

        [MenuItem("Defenders/World/Place the Realm Store storefront")]
        public static void RunMenu() => Run();

        /// <summary>
        /// Reads a non-zero authored position for this storefront out of Offset Forge.
        /// <para>
        /// A ZERO pos is treated as "not authored" rather than "place it at the world origin".
        /// Every row in that file defaults to pos (0,0,0), so honouring a zero literally would drop
        /// the storefront on top of the Heart the moment anyone touched its rotation without
        /// intending to move it — a tuning tool must not be able to teleport a building by
        /// omission.
        /// </para>
        /// </summary>
        private static bool TryReadAuthoredPosition(out Vector3 pos)
        {
            pos = default;
            const string path = "Assets/OffsetForge/offsets.json";
            if (!System.IO.File.Exists(path)) return false;

            try
            {
                string json = System.IO.File.ReadAllText(path);
                // Locate this key's block, then the pos object inside it. Text-scanned rather than
                // deserialized so a schema addition to offsets.json cannot break the bake.
                int at = json.IndexOf($"\"id\": \"{OffsetKey}\"", System.StringComparison.Ordinal);
                if (at < 0) return false;

                int posAt = json.IndexOf("\"pos\"", at, System.StringComparison.Ordinal);
                if (posAt < 0) return false;

                var m = System.Text.RegularExpressions.Regex.Match(
                    json.Substring(posAt, System.Math.Min(220, json.Length - posAt)),
                    "\"x\"\\s*:\\s*(-?[0-9.eE+]+).*?\"y\"\\s*:\\s*(-?[0-9.eE+]+).*?\"z\"\\s*:\\s*(-?[0-9.eE+]+)",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                if (!m.Success) return false;

                var v = new Vector3(
                    float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));

                if (v.sqrMagnitude < 0.0001f) return false;   // unauthored zero — see summary
                pos = v;
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RealmStore] could not read the authored offset ({ex.Message}) — " +
                                 "falling back to the owner-ruled default.");
                return false;
            }
        }

        /// <summary>
        /// (Re)computes the storefront's BoxCollider from the CURRENT renderer bounds. Returns false
        /// when there is nothing to measure.
        /// <para>
        /// ⛔ TWO BUGS ARE FIXED HERE AND THEY ARE DIFFERENT BUGS.
        /// (1) The old code only added a collider "if none existed", so it could describe a shape the
        /// model no longer has and never repair it. This always recomputes: the collider is DERIVED
        /// state, and derived state that is written once is stale state.
        /// (2) <c>Renderer.bounds</c> is a WORLD-space AABB. Assigning it into a LOCAL-space
        /// BoxCollider on a YAWED root stores a box inflated by the yaw. Measuring with the root
        /// temporarily unrotated makes world axes equal local axes, so the numbers written are the
        /// numbers meant.
        /// </para>
        /// </summary>
        private static bool ApplyCollider(GameObject root)
        {
            if (root == null) return false;

            var rends = root.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;

            var t = root.transform;
            Quaternion keptRotation = t.rotation;
            t.rotation = Quaternion.identity;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            t.rotation = keptRotation;

            if (b.size.sqrMagnitude < 1e-6f) return false;

            // The fit scale lives on the SKINNED CHILD, so the root is unit-scaled and world sizes
            // convert to local ones 1:1. Say so out loud rather than assuming it: if a future change
            // scales the root, this line is the one that explains the wrong-sized box.
            Vector3 s = t.lossyScale;
            if (Mathf.Abs(s.x - 1f) > 0.001f || Mathf.Abs(s.y - 1f) > 0.001f || Mathf.Abs(s.z - 1f) > 0.001f)
            {
                Debug.LogWarning($"[RealmStore] the storefront ROOT is scaled {s:F3}, not 1 — the collider " +
                                 "size below is a world measurement written into a local-space box and will " +
                                 "be wrong by that factor. Keep the fit scale on the skinned child.");
            }

            var box = root.GetComponent<BoxCollider>();
            if (box == null) box = root.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = b.center - t.position;   // identity rotation + unit scale => world delta == local
            box.size   = b.size;

            Debug.Log($"[RealmStore] collider RECOMPUTED from live bounds: size={box.size:F3} centre={box.center:F3} " +
                      $"(measured with the root unrotated; {rends.Length} renderer(s)).");
            return true;
        }

        public static void Run()
        {
            string modelPath = DeNelle.Core.AssetRoots.StructureContent + "/RealmStore.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[RealmStore] model not found at '{modelPath}' — nothing placed. " +
                               "The art is owner-purchased and git-tracked; if it is missing the " +
                               "migration or a checkout has gone wrong.");
                return;
            }

            EditorSceneManager.OpenScene(HubScene, OpenSceneMode.Single);
            var scene = SceneManager.GetActiveScene();

            var ownerStore = DeNelle.Village.AuthoredCastleStorefront.Find(ObjectName, true);
            if (ownerStore != null && ownerStore.GetComponent<DeNelle.Village.AuthoredCastleStorefront>() != null)
            {
                if (ownerStore.GetComponent<DeNelle.Village.RealmStoreVendor>() == null)
                    throw new System.InvalidOperationException("Authored Realm Store is missing its door; run OwnerCastleLayoutRepair.");
                Debug.Log("REALM_STORE_PLACED_OK preserved owner-authored storefront and pose");
                return;
            }

            // Idempotent: drop any previous instance first, so re-running never stacks storefronts.
            foreach (var existingRoot in scene.GetRootGameObjects())
            {
                foreach (var t in existingRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name == ObjectName)
                    {
                        Debug.Log("[RealmStore] removing the previous storefront instance (idempotent re-run).");
                        Object.DestroyImmediate(t.gameObject);
                        break;
                    }
                }
            }

            // Read the AUTHORED placement first; fall back to the owner-ruled default.
            Vector3 placement = FallbackPlacement;
            bool authored = TryReadAuthoredPosition(out var authoredPos);
            if (authored)
            {
                placement = authoredPos;
                Debug.Log($"[RealmStore] using the AUTHORED position from Offset Forge: {placement}");
            }
            else
            {
                Debug.Log($"[RealmStore] no authored position for '{OffsetKey}' — using the owner-ruled " +
                          $"default {placement}. Tune it in Offset Forge and re-run to make it stick.");
            }

            // ROOT HOST + SKINNED VISUAL — the same shape StructureFactory.Create gives every catalog
            // structure. The root owns the world pose, the collider and the vendor door; the model is
            // skinned UNDERNEATH it by the one shared seam. Reusing VisualFactory.Skin rather than
            // instantiating the FBX raw is what supplies the fit-to-height (see the SCALE block in the
            // header) and the Tripo->URP material fixer, and it means this storefront obeys the same
            // scaling law as its neighbours instead of a rule written only here.
            var root = new GameObject(ObjectName);
            // Face the plaza centre so the shopfront looks at the player walking in from it,
            // rather than presenting its back to the town.
            root.transform.SetPositionAndRotation(
                placement,
                Quaternion.LookRotation(new Vector3(0f, 0f, 0f) - placement, Vector3.up));

            var opts = DeNelle.Village.SkinOptions.Structure(0f); // clears FitLargest; keeps SeatOnGround + Tripo fix
            opts.FitHeight = FitHeightMeters;                     // DERIVED from the town cadence
            opts.TraceId   = OffsetKey;                           // stamps the Xform value-trace lines
            opts.LocalRotation = AuthoredCorrection;              // owner-ruled upright + facing (see the field)
            // NOTE: PreservePrefabRotation is deliberately LEFT FALSE, and that is still right AFTER
            // the 2026-08-18 correction below. The prefab-native pose captured on this asset is a 90
            // X-PITCH; the pose it actually needs is a 90 Z-ROLL. Those are different axes, so
            // reinstating the discarded native pose would have laid the storefront down a DIFFERENT
            // way rather than standing it up — the native pose is itself wrong, and the DEF-232
            // identity reset is not the defect here. The correction is AUTHORED, via LocalRotation.

            var visual = DeNelle.Village.VisualFactory.Skin(root.transform, model, opts);
            if (visual == null)
            {
                Debug.LogError("[RealmStore] VisualFactory.Skin returned NULL for the storefront model — " +
                               "nothing was placed and the empty root has been removed. The skin seam logs " +
                               "the reason ([Flow:VisualFactory]); a render-verify miss means the FBX loaded " +
                               "but draws nothing.");
                Object.DestroyImmediate(root);
                return;
            }

            // A collider so the player cannot walk through the building. NOT an
            // IDamageableStructure and NOT a catalog row — it blocks, it does not take damage.
            if (!ApplyCollider(root))
            {
                Debug.LogError("[RealmStore] could not measure renderer bounds for the collider — " +
                               "the storefront would be walk-through. Nothing saved.");
                Object.DestroyImmediate(root);
                return;
            }

            // The door. PackStoreBootstrap already registers PanelId.RealmStore at boot, so this
            // component only has to call it.
            if (root.GetComponent<DeNelle.Village.RealmStoreVendor>() == null)
                root.AddComponent<DeNelle.Village.RealmStoreVendor>();
            if (root.GetComponent<DeNelle.Village.RealmStoreBeacon>() == null)
                root.AddComponent<DeNelle.Village.RealmStoreBeacon>();

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            // ── VERIFY THE ARTIFACT, NOT THE INTENT ──────────────────────────────────────────
            // INSTRUMENTATION_STANDARD 1.4b: assert MEASURED values. "Present in the scene" is the
            // weakest possible claim and is exactly the claim that was true the whole time the
            // collider was 90 degrees out of alignment. Re-find the object in the SAVED scene and
            // read its real numbers back, so this script cannot print its OK marker over a shape it
            // did not actually produce.
            GameObject saved = null;
            foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (t.name == ObjectName) { saved = t.gameObject; break; }
                if (saved != null) break;
            }

            if (saved == null)
            {
                Debug.LogError("[RealmStore] storefront is NOT in the saved scene — placement failed.");
                return;
            }

            var savedBox = saved.GetComponent<BoxCollider>();
            if (savedBox == null)
            {
                Debug.LogError("[RealmStore] the saved storefront has NO BoxCollider — the player would " +
                               "walk straight through the game's only storefront.");
                return;
            }

            float target = FitHeightMeters;
            float measuredH = savedBox.size.y;
            float measuredBase = savedBox.center.y - savedBox.size.y * 0.5f;

            // Tolerances are deliberately generous (a building is not a ruler) but NOT unfalsifiable:
            // the pre-fix artifact measured 0.62 m tall against a 4 m target and would fail this by
            // a factor of six, which is the whole reason the check is written as a number.
            if (Mathf.Abs(measuredH - target) > target * 0.10f)
            {
                Debug.LogError($"[RealmStore] MEASURED HEIGHT {measuredH:F2} m != the derived fit target " +
                               $"{target:F2} m. The fit-to-height did not take, so the storefront does not " +
                               "stand in the town's height cadence. Not claiming success.");
                return;
            }
            if (Mathf.Abs(measuredBase) > 0.35f)
            {
                Debug.LogError($"[RealmStore] the collider base sits {measuredBase:F2} m off the root's y — " +
                               "the model is not seated on the ground (it would float or sink).");
                return;
            }

            Debug.Log($"[RealmStore] placed at {placement} ({(authored ? "AUTHORED in Offset Forge" : "owner-ruled default")}) " +
                      "facing the plaza centre, with a collider and RealmStoreVendor. " +
                      "NOT in the build catalog, NOT damageable.");
            Debug.Log($"[RealmStore] MEASURED from the saved scene: collider size={savedBox.size:F3} " +
                      $"centre={savedBox.center:F3} (fit target {target:F2} m, base offset {measuredBase:F2} m), " +
                      $"pos={saved.transform.position:F2}.");
            Debug.Log($"{OkMarker} at {placement} height {measuredH:F2}m");
        }
    }
}
