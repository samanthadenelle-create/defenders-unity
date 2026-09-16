// =============================================================================
// RealmStorefrontRegression [realm-storefront] -- pins the PROD-003 Realm Store
// storefront IN THE SAVED HUB SCENE, not in the code that places it.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Marker: REALM_STOREFRONT_OK /
// REALM_STOREFRONT_FAIL.
//
// WHY IT READS THE .unity FILE AS TEXT AND NOT THE LIVE SCENE.
// The storefront is BAKED. PROD-003 section 3.6 asks for the oracle to be pinned
// "against the ARTIFACT, not just the code", because of the lesson already paid
// for on 2026-08-17 (WO-1049 section 5b): a gate that runs the PRODUCER cannot see
// a STALE BAKE. That is not a hypothetical here -- it is the live defect this
// suite was written alongside. Commit f995c4706 set bakeAxisConversion on
// RealmStore.fbx, re-orienting the mesh at import, and nobody re-ran the placer,
// so the saved scene kept a collider (1.034 x 0.620 x 1.195) describing a mesh
// shape that no longer exists. Every code-level check passed the whole time.
// Reading the saved YAML is the only way to assert what SHIPPED.
// It is also side-effect free: opening the hub scene mid-RunAll would disturb
// every suite after it, and this needs no scene loaded at all.
//
// WHAT IT ASSERTS, and every one of these is a MEASURED value
// (INSTRUMENTATION_STANDARD 1.4b -- an oracle that cannot report failure is a bug):
//
//   1. EXISTS, EXACTLY ONCE. One GameObject named RealmStore_Storefront in the
//      saved hub. Two = the placer stacked; zero = a hub rebuild dropped the
//      game's only storefront, which is the durability hole PROD-003 leaves open
//      (CastleHubBuilder does not own this object -- see its header).
//   2. STANDS WHERE THE PRODUCER SAYS. Its saved position is compared against
//      RealmStorePlacer.ResolvePlacement() -- the producer's OWN current answer,
//      resolved by reflection -- not against a number copied into this file. An
//      owner nudge in Offset Forge that was never re-baked therefore shows up as a
//      red suite instead of as a building standing somewhere nobody authored.
//   3. IS THE RIGHT SIZE. Its collider height is compared against
//      RealmStorePlacer.FitHeightMeters (the town height cadence,
//      StructureFactory.YHeightVariable x heightMul). The pre-fix artifact stood
//      0.62 m against a 4 m target and fails this by a factor of six. A suite that
//      cannot go red is worse than no suite, so this is the case that proves it can.
//   4. SITS ON THE GROUND. The collider base is at the root's y, so the building
//      neither floats nor sinks.
//   5. HAS ITS DOOR. A RealmStoreVendor component is on it, else the storefront is
//      scenery.
//   6. IS NOT DAMAGEABLE. Every script on it is resolved to a real System.Type and
//      checked against IDamageableStructure -- section 2 of the ticket, verbatim:
//      "It must not be a IDamageableStructure at all". A raid must not be able to
//      take revenue offline.
//   7. IS NOT IN THE BUILD CATALOG. structures-catalog.json and
//      build-categories.json, BOTH dual copies (Resources + StreamingAssets), carry
//      no id for it. Section 5: "Do NOT add the storefront to build-categories.json
//      -- that is the bug this WO exists to prevent." A catalog row is what would
//      make it sellable / movable / placeable.
//      The scan compares WHOLE STRING VALUES, normalised -- never a substring --
//      because both catalogs contain authoring prose that legitimately says "Realm
//      Store", and a naive grep would fail on the note explaining why the row must
//      not exist.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "realm-storefront suite", () => { if (!DeNelle.Editor.Regression.RealmStorefrontRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[realm-storefront] " + r); });
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Core.Combat;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>Artifact oracle for the PROD-003 baked storefront. See the file header.</summary>
    public static class RealmStorefrontRegression
    {
        private const string ScenePath  = "Assets/Scenes/Main_Castle_Overworld.unity";
        private const string ObjectName = "RealmStore_Storefront";
        private const string VendorType = "DeNelle.Village.RealmStoreVendor";
        private const string PlacerType = "DeNelle.Editor.RealmStorePlacer";

        /// <summary>The four catalog artifacts that must NOT carry a row for this building.</summary>
        private static readonly string[] CatalogPaths =
        {
            "Assets/Resources/Data/Canonical/structures-catalog.json",
            "Assets/StreamingAssets/Data/Canonical/structures-catalog.json",
            "Assets/Resources/Data/Canonical/build-categories.json",
            "Assets/StreamingAssets/Data/Canonical/build-categories.json",
        };

        // Position tolerance in metres. Tight: the position is written by the producer and read
        // back verbatim, so anything past a rounding wobble means the bake and the producer
        // disagree -- which is exactly the event this suite exists to name.
        private const float PosToleranceM = 0.05f;

        // Height tolerance as a FRACTION of the fit target. Generous enough that a model with a
        // flagpole is not a failure, far too tight for the 6x miss the pre-fix artifact carried.
        private const float HeightTolerance = 0.10f;

        // How far the collider base may sit from the root's y before the building reads as
        // floating or sunk.
        private const float SeatToleranceM = 0.35f;

        // Open the SAVED artifact in an isolated preview scene. The old root is retained
        // inactive for recovery; it cannot serve as evidence for the new, active store door.
        private static bool RunAuthoredArtifact(string workspace, out string reason)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var roots = scene.GetRootGameObjects();
                var markers = roots.SelectMany(r => r.GetComponentsInChildren<DeNelle.Village.AuthoredCastleStorefront>(true))
                    .Where(m => m.LegacyName == ObjectName).ToArray();
                if (markers.Length != 1) throw new InvalidOperationException("Expected one authored store identity");
                var store = markers[0];
                var doors = roots.SelectMany(r => r.GetComponentsInChildren<DeNelle.Village.RealmStoreVendor>(true))
                    .Where(v => v.enabled && v.gameObject.activeInHierarchy).ToArray();
                if (doors.Length != 1 || doors[0].gameObject != store.gameObject)
                    throw new InvalidOperationException("Authored store does not own the only active door");
                if (!string.IsNullOrEmpty(store.CanonicalId) ||
                    store.GetComponents<Component>().Any(c => c is IDamageableStructure) ||
                    store.GetComponent<DeNelle.Village.PlacedStructure>() != null)
                    throw new InvalidOperationException("Realm Store became damageable or player-placeable");
                string source = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(store.gameObject);
                if (UnityEditor.AssetDatabase.AssetPathToGUID(source) != "54f795e6004947e488b96d86a60f0ab3")
                    throw new InvalidOperationException("Authored Realm Store source changed");
                var recipe = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab");
                var expected = recipe != null ? recipe.GetComponentsInChildren<DeNelle.Village.AuthoredCastleStorefront>(true)
                    .SingleOrDefault(m => m.LegacyName == ObjectName) : null;
                if (expected == null || store.transform.localPosition != expected.transform.localPosition ||
                    store.transform.localRotation != expected.transform.localRotation || store.transform.localScale != expected.transform.localScale)
                    throw new InvalidOperationException("Saved store and owner recipe disagree");
                if (!store.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger))
                    throw new InvalidOperationException("Authored store has no solid collider");
                var renderers = store.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0 || renderers.Any(r => !r.enabled || r.sharedMaterials.Any(m => m == null ||
                    m.shader.name != "Universal Render Pipeline/Lit" || !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == null)))
                    throw new InvalidOperationException("Authored store lost its visible textured art");
                foreach (string path in CatalogPaths)
                    if (FindStorefrontIdValue(File.ReadAllText(Path.Combine(workspace, path))) != null)
                        throw new InvalidOperationException("Realm Store entered build catalog: " + path);
                reason = "authored saved store/recipe agree; original art, one active door, textured, not damageable or placeable";
                Debug.Log("REALM_STOREFRONT_OK " + reason);
                return true;
            }
            catch (Exception error) { reason = "REALM_STOREFRONT_FAIL " + error.Message; return false; }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static bool Run(out string reason)
        {
            var log = new StringBuilder();
            log.AppendLine("--- REALM STOREFRONT (PROD-003: baked, correctly scaled, not damageable, not in the catalog) ---");

            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                string scenePath = Path.Combine(root, ScenePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(scenePath))
                {
                    return Fail(out reason, log, "the hub scene is not on disk at " + ScenePath +
                                " -- there is no artifact to pin.");
                }

                string yaml = File.ReadAllText(scenePath);
                if (yaml.Contains("legacyName: RealmStore_Storefront"))
                    return RunAuthoredArtifact(root, out reason);
                var docs = ParseDocs(yaml);
                if (docs.Count == 0)
                {
                    return Fail(out reason, log, "parsed 0 YAML documents out of " + ScenePath +
                                " -- the scene file is not in the expected text form, so this suite " +
                                "would silently assert nothing. Treating that as RED on purpose.");
                }
                log.AppendLine($"  artifact: {ScenePath} parsed, {docs.Count} YAML documents");

                // ---- 1. EXISTS, EXACTLY ONCE ------------------------------------
                var hits = new List<Doc>();
                foreach (var d in docs)
                {
                    if (d.ClassId != 1) continue;                       // GameObject
                    if (ReadScalar(d.Body, "m_Name") != ObjectName) continue;
                    hits.Add(d);
                }

                if (hits.Count == 0)
                {
                    // Name the most likely cause rather than only the symptom: a hub rebuild is the
                    // documented way this object disappears, and it disappears without any error.
                    bool legacyShape = yaml.IndexOf("value: " + ObjectName, StringComparison.Ordinal) >= 0;
                    string extra = legacyShape
                        ? " A PrefabInstance modification naming '" + ObjectName + "' IS present, so the " +
                          "scene carries the OLD raw-FBX placement shape: the bake predates the root-host " +
                          "placer (fit-to-height + recomputed collider) and is STALE. Re-run " +
                          PlacerType + ".Run, then re-bake the navmesh."
                        : " Nothing in the scene mentions it at all. CastleHubBuilder does NOT create this " +
                          "object (see its header) -- a 'new empty scene + rebuild the hub' pass drops the " +
                          "game's only storefront silently. Re-run " + PlacerType + ".Run, then re-bake.";
                    return Fail(out reason, log,
                        "NO GameObject named '" + ObjectName + "' in the saved hub scene -- the game's only " +
                        "monetization storefront is not in the build." + extra);
                }
                if (hits.Count > 1)
                {
                    return Fail(out reason, log,
                        hits.Count + " GameObjects named '" + ObjectName + "' in the saved scene -- the placer " +
                        "stacked storefronts instead of replacing the previous one. Two doors to one panel is " +
                        "fine; two buildings intersecting each other is not.");
                }

                Doc store = hits[0];
                if (ReadScalar(store.Body, "m_IsActive") != "1")
                    return Fail(out reason, log, "Legacy storefront is inactive and no authored replacement was found");
                var components = ComponentsOf(docs, store.FileId);
                log.AppendLine($"  found '{ObjectName}' (fileID {store.FileId}) with {components.Count} component document(s)");

                // ---- 2. STANDS WHERE THE PRODUCER SAYS --------------------------
                Doc transform = FindByClass(components, 4);
                if (transform == null)
                {
                    return Fail(out reason, log, "the storefront GameObject has no Transform document -- " +
                                "the scene file is malformed.");
                }
                if (!TryReadVector(transform.Body, "m_LocalPosition", out Vector3 savedPos))
                {
                    return Fail(out reason, log, "could not read m_LocalPosition off the storefront's Transform.");
                }

                Vector3 expectedPos;
                bool haveExpectedPos = TryProducerPlacement(out expectedPos, out string posDetail);
                if (!haveExpectedPos)
                {
                    // Do NOT fall back to a literal. A number typed here is a second source of truth
                    // and would go stale exactly the way the collider did.
                    return Fail(out reason, log,
                        "could not resolve the producer's placement via " + PlacerType + ".ResolvePlacement() (" +
                        posDetail + "). Without it this suite could only compare the artifact to a hardcoded " +
                        "number, which is the drift it exists to catch. RED rather than green-by-omission.");
                }

                float posErr = Vector3.Distance(savedPos, expectedPos);
                if (posErr > PosToleranceM)
                {
                    return Fail(out reason, log,
                        $"the storefront stands at {savedPos} in the SAVED scene but {PlacerType} would place it " +
                        $"at {expectedPos} ({posErr:F2} m apart). The bake and the producer disagree -- most " +
                        "likely an Offset Forge nudge that was never re-baked. Re-run the placer, then re-bake " +
                        "the navmesh (moving a building invalidates it).");
                }
                log.AppendLine($"  position: saved {savedPos} == producer {expectedPos} (delta {posErr:F3} m)");

                // ---- 3 + 4. RIGHT SIZE, SEATED ON THE GROUND --------------------
                Doc box = FindByClass(components, 65);   // BoxCollider
                if (box == null)
                {
                    return Fail(out reason, log,
                        "the storefront has NO BoxCollider in the saved scene -- the player walks straight " +
                        "through the game's only storefront.");
                }
                if (!TryReadVector(box.Body, "m_Size", out Vector3 size) ||
                    !TryReadVector(box.Body, "m_Center", out Vector3 centre))
                {
                    return Fail(out reason, log, "could not read m_Size / m_Center off the storefront's BoxCollider.");
                }

                float target;
                if (!TryProducerFitHeight(out target, out string hDetail))
                {
                    return Fail(out reason, log,
                        "could not resolve " + PlacerType + ".FitHeightMeters (" + hDetail + "). The height " +
                        "target must come from the producer's cadence, never from a literal here.");
                }

                if (size.x <= 0.01f || size.y <= 0.01f || size.z <= 0.01f)
                {
                    return Fail(out reason, log, $"the storefront collider is degenerate ({size}) -- it blocks nothing.");
                }
                if (Mathf.Abs(size.y - target) > target * HeightTolerance)
                {
                    return Fail(out reason, log,
                        $"the storefront's saved collider is {size.y:F2} m tall against a derived fit target of " +
                        $"{target:F2} m (full size {size}). It does not stand in the town's height cadence -- a " +
                        $"{size.y:F2} m shopfront beside 4 m neighbours is not the building PROD-003 section 3.4 " +
                        "asks a player to find without being told. If the FBX was re-imported (an axis bake, a " +
                        "scale change) the SAVED SCENE IS STALE: re-run " + PlacerType + ".Run, then re-bake the " +
                        "navmesh.");
                }

                float baseY = centre.y - size.y * 0.5f;
                if (Mathf.Abs(baseY) > SeatToleranceM)
                {
                    return Fail(out reason, log,
                        $"the storefront's collider base sits {baseY:F2} m off its root y (centre {centre}, size " +
                        $"{size}) -- the model is not seated on the ground, so it floats or sinks. Re-run the placer.");
                }
                log.AppendLine($"  collider: size {size} (height {size.y:F2} m vs target {target:F2} m), " +
                               $"centre {centre}, base offset {baseY:F2} m");

                // ---- 5 + 6. THE DOOR IS THERE, AND NOTHING ON IT IS DAMAGEABLE --
                bool vendorFound = false;
                var scriptNames = new List<string>();
                foreach (var c in components)
                {
                    if (c.ClassId != 114) continue;                     // MonoBehaviour
                    string ident = ReadScalar(c.Body, "m_EditorClassIdentifier");
                    if (string.IsNullOrEmpty(ident)) continue;

                    // "Assembly::Namespace.Type" -> "Namespace.Type"
                    int sep = ident.IndexOf("::", StringComparison.Ordinal);
                    string typeName = sep >= 0 ? ident.Substring(sep + 2) : ident;
                    scriptNames.Add(typeName);

                    if (typeName == VendorType) vendorFound = true;

                    Type t = FindType(typeName);
                    if (t == null) continue;   // a type we cannot resolve is reported below, not silently trusted
                    if (typeof(IDamageableStructure).IsAssignableFrom(t))
                    {
                        return Fail(out reason, log,
                            "the storefront carries '" + typeName + "', which implements IDamageableStructure. " +
                            "PROD-003 section 2 is explicit that it must not be one AT ALL: anything that " +
                            "participates in the damage system participates in its bugs, and a raid that reaches " +
                            "the store would take the game's only revenue surface OFFLINE. Do not answer this " +
                            "with a huge HP pool or an immunity flag -- remove the component.");
                    }
                }

                if (!vendorFound)
                {
                    return Fail(out reason, log,
                        "no " + VendorType + " on the storefront (scripts found: " +
                        (scriptNames.Count == 0 ? "none" : string.Join(", ", scriptNames.ToArray())) +
                        ") -- the building stands there as scenery and one interact opens nothing. " +
                        "PanelId.RealmStore is registered at boot by PackStoreBootstrap; this component is the door.");
                }

                // The type itself, independent of what the bake happened to serialise -- so someone
                // making the vendor damageable in CODE fails here even before a re-bake.
                Type vendor = FindType(VendorType);
                if (vendor == null)
                {
                    return Fail(out reason, log,
                        VendorType + " does not resolve to a loaded type -- the baked scene references a script " +
                        "that no longer exists, so the storefront's door is dead in the build.");
                }
                if (typeof(IDamageableStructure).IsAssignableFrom(vendor))
                {
                    return Fail(out reason, log,
                        VendorType + " now implements IDamageableStructure. See PROD-003 section 2 -- the " +
                        "storefront must not be damageable at all.");
                }
                log.AppendLine($"  components: {scriptNames.Count} script(s) [{string.Join(", ", scriptNames.ToArray())}]; " +
                               "none implement IDamageableStructure");

                // ---- 7. NOT IN THE BUILD CATALOG (both dual copies) -------------
                foreach (var rel in CatalogPaths)
                {
                    string full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        return Fail(out reason, log,
                            "catalog artifact missing: " + rel + " -- cannot prove the storefront is absent from " +
                            "a file that is not there.");
                    }

                    string offender = FindStorefrontIdValue(File.ReadAllText(full));
                    if (offender != null)
                    {
                        return Fail(out reason, log,
                            "'" + offender + "' appears as a value in " + rel + ". A catalog row is EXACTLY the " +
                            "failure PROD-003 exists to prevent: it puts the storefront in the build palette, " +
                            "which makes it sellable (the player deletes their own store), movable (buried behind " +
                            "walls), damageable, and absent for a brand-new player. Remove the row -- do not " +
                            "special-case it in the palette.");
                    }
                }
                log.AppendLine($"  catalogs: no storefront id in any of the {CatalogPaths.Length} artifacts " +
                               "(both Resources and StreamingAssets copies of structures-catalog + build-categories)");

                reason = $"REALM STOREFRONT OK -- baked once at {savedPos}, {size.y:F2} m tall (target {target:F2} m), " +
                         $"seated (base {baseY:F2} m), door present, not damageable, absent from all " +
                         $"{CatalogPaths.Length} catalog artifacts";
                Debug.Log(log.ToString() + "REALM_STOREFRONT_OK");
                return true;
            }
            catch (Exception ex)
            {
                return Fail(out reason, log, "exception during the suite -- " + ex.GetBaseException().Message);
            }
        }

        // ── verdict helper ───────────────────────────────────────────────────
        private static bool Fail(out string reason, StringBuilder log, string why)
        {
            reason = "realm-storefront: " + why;
            Debug.LogError(log.ToString() + "REALM_STOREFRONT_FAIL: " + reason);
            return false;
        }

        // ── the producer, by reflection ──────────────────────────────────────
        // DeNelle.EditorRegression does not reference the DeNelle.Editor assembly (same technique as
        // HubFoliageRegression / DungeonDressingRegression). Reflection here is the asmdef boundary
        // being respected, not routed around.
        private static bool TryProducerPlacement(out Vector3 pos, out string detail)
        {
            pos = default;
            Type t = FindType(PlacerType);
            if (t == null) { detail = PlacerType + " type not found"; return false; }

            var m = t.GetMethod("ResolvePlacement", BindingFlags.Public | BindingFlags.Static,
                                null, Type.EmptyTypes, null);
            if (m == null) { detail = "no public static ResolvePlacement()"; return false; }
            if (m.ReturnType != typeof(Vector3)) { detail = "ResolvePlacement() does not return Vector3"; return false; }

            pos = (Vector3)m.Invoke(null, null);
            detail = "resolved " + pos;
            return true;
        }

        private static bool TryProducerFitHeight(out float height, out string detail)
        {
            height = 0f;
            Type t = FindType(PlacerType);
            if (t == null) { detail = PlacerType + " type not found"; return false; }

            var p = t.GetProperty("FitHeightMeters", BindingFlags.Public | BindingFlags.Static);
            if (p == null) { detail = "no public static FitHeightMeters"; return false; }

            height = Convert.ToSingle(p.GetValue(null));
            if (height <= 0.01f) { detail = "FitHeightMeters resolved to " + height; return false; }
            detail = "resolved " + height;
            return true;
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

        // ── catalog scan ─────────────────────────────────────────────────────
        // Compares WHOLE quoted string values, normalised (lowercased, separators dropped), against
        // the id forms this building could plausibly be authored under. Never a substring search:
        // both catalogs contain long authoring notes that legitimately mention the Realm Store, and
        // a substring scan would go red on the very prose explaining why the row must not exist.
        private static readonly Regex QuotedValue = new Regex("\"([^\"\\\\]{1,40})\"", RegexOptions.Compiled);

        private static string FindStorefrontIdValue(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            foreach (Match m in QuotedValue.Matches(json))
            {
                string raw = m.Groups[1].Value;
                var sb = new StringBuilder(raw.Length);
                foreach (char ch in raw)
                {
                    if (ch == '_' || ch == '-' || ch == ' ' || ch == '.') continue;
                    sb.Append(char.ToLowerInvariant(ch));
                }
                if (sb.ToString() == "realmstore") return raw;
            }
            return null;
        }

        // ── minimal Unity-YAML reader ────────────────────────────────────────
        private sealed class Doc
        {
            public int    ClassId;   // the !u!<n> tag: 1 GameObject, 4 Transform, 65 BoxCollider, 114 MonoBehaviour
            public long   FileId;    // the &<n> anchor
            public string Body;
        }

        private static readonly Regex DocHeader = new Regex(
            @"^--- !u!(\d+) &(\d+)", RegexOptions.Multiline | RegexOptions.Compiled);

        private static List<Doc> ParseDocs(string yaml)
        {
            var docs = new List<Doc>();
            var headers = DocHeader.Matches(yaml);
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                int bodyStart = h.Index + h.Length;
                int bodyEnd = (i + 1 < headers.Count) ? headers[i + 1].Index : yaml.Length;
                docs.Add(new Doc
                {
                    ClassId = int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture),
                    FileId  = long.Parse(h.Groups[2].Value, CultureInfo.InvariantCulture),
                    Body    = yaml.Substring(bodyStart, bodyEnd - bodyStart),
                });
            }
            return docs;
        }

        /// <summary>Every document that declares <c>m_GameObject: {fileID: goId}</c>.</summary>
        private static List<Doc> ComponentsOf(List<Doc> docs, long goId)
        {
            string needle = "m_GameObject: {fileID: " + goId.ToString(CultureInfo.InvariantCulture) + "}";
            var found = new List<Doc>();
            foreach (var d in docs)
                if (d.Body.IndexOf(needle, StringComparison.Ordinal) >= 0) found.Add(d);
            return found;
        }

        private static Doc FindByClass(List<Doc> docs, int classId)
        {
            foreach (var d in docs) if (d.ClassId == classId) return d;
            return null;
        }

        /// <summary>Reads a scalar mapping value (<c>key: value</c>) from a document body.</summary>
        private static string ReadScalar(string body, string key)
        {
            var m = Regex.Match(body, @"^\s*" + Regex.Escape(key) + @":[ \t]*(.*)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>Reads a <c>key: {x: n, y: n, z: n}</c> flow mapping.</summary>
        private static bool TryReadVector(string body, string key, out Vector3 v)
        {
            v = default;
            var m = Regex.Match(
                body,
                @"^\s*" + Regex.Escape(key) +
                @":\s*\{x:\s*(-?[0-9.eE+\-]+),\s*y:\s*(-?[0-9.eE+\-]+),\s*z:\s*(-?[0-9.eE+\-]+)\}",
                RegexOptions.Multiline);
            if (!m.Success) return false;

            v = new Vector3(
                float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            return true;
        }
    }
}
