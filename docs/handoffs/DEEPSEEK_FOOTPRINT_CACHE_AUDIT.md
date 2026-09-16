# DeepSeek work order: placement-footprint cache audit

Context compaction: standalone // and XML-comment lines plus blank lines were omitted inside C# blocks to reduce reading cost. Executable lines are unchanged; range labels refer to original files, not compacted line numbers. Inline and block comments may remain. Full-file hashes refer to originals.
Perform a READ-ONLY source audit. You have no repository access: all supplied context is embedded below. Return one concise final audit, not a rewrite or implementation. The local CLI owns implementation, Unity execution and integration. This task can proceed independently of the storage sizing measurements.

## Bounded questions
1. Can an unavailable-art fallback become a persistent cached footprint after the art becomes resident? Trace result initialization, load failure, cache write, cache hit, and any visible invalidation. Give exact preconditions, source evidence and a deterministic proposed reproduction; do not call it a reproduced bug.
2. Does the key distinguish every input that changes the measured result (source address, fitting height, cap, rotation policy, manual orientation/scale and precision, authored fallback)? Separate mutable runtime scenarios from hypothetical editor changes. Distinguish value changes from an in-place mesh/material mutation and justify which need support.
3. Does a correctly refreshed measurement necessarily update an existing grid claim? Review supplied consumer windows. Do not equate visual replacement or cache invalidation with occupancy reconciliation. Identify exactly what remains unproven without full consumer lifecycle context.
4. Compare the transform sequence of MeasureUprightFootprintXZ with Create/OptsFor/Skin for nonzero manual Euler. Flag any double-application concern separately from caching, with an asymmetric fixture and nonzero rotation; identity storage rows cannot prove that case.
5. Recommend the smallest bounded repair direction and at most six meaningful tests. Consider never caching unsuccessful measurements versus explicit invalidation, avoiding per-frame Instantiate costs on repeated load misses. Do not invent an event/API, assume WhenSettled means successful residency, or introduce an unbounded callback registration loop.

## Constraints
- Preserve approved Tripo castle art, saved layout plus Cathedral of Learning, the flat KayKit storage orientation, all materials, and capture-to-owned-town behavior.
- No sizing target selection; no changes to heightMul/maxFootprint data, art, scenes, saves, grid policy, or Barracks movement guard.
- Existing placed/saved occupancy must not silently change under a cache repair. Treat migration/reconciliation as separately scoped if necessary.
- Unity 6000.4.8f1. No compile/run result is provided for this audit. All proposed tests must be UNRUN.
- Comments are historical hints, not runtime evidence. Hashes identify supplied current files only.
- Source excerpts are explicitly labelled. Consumer search windows are discovery evidence, NOT complete methods. If a missing dependency changes your verdict, name it and explain why; do not pretend you can open it.
- No binary art or full player lifecycle supplied. Do not claim exact dimensions, actual overlap, domain-reload behavior, or device failures from this packet.

## Deliverable (aim for 1200 words or fewer)
A severity-ranked table of findings with source/method evidence, preconditions, confidence and missing proof; one minimal repair recommendation; at most six independent regression scenarios; exact additional context needed, if any. Distinguish verified source behavior, inference and runtime evidence. No wholesale factory rewrite, no speculative code patch, no request to rerun this whole audit with another giant proposal.

## Context boundaries
Embedded: selected complete factory regions (creation, reskin/options, footprint measurement), complete VisualFactory, loader, catalog DTOs and grid; warmer state/lookup/completion/settlement excerpts; current direct consumer search windows. Other warmer request/warm-pass implementations, full consumer save/move lifecycle, Guard/FlowTrace internals and addressable binary assets are not embedded. Limit claims accordingly.

## Assets/_Modules/Village/Catalog/StructureFactory.cs — lines 1-372 of 1445; full-file SHA256 FD7F5D1944BF04DB5A2E0310C9D39DEC9C1012DA99C3A2210E3579CDD2CB0AC4
```csharp
using System.Collections.Generic;   // ReskinForLevel old-visual collection
using UnityEngine;
using UnityEngine.AI;          // NavMeshObstacle footprint self-report (invisible-blocker class)
using DeNelle.Core.Catalog;
using DeNelle.Core.Combat;     // DamageElement literals (WO-113 ArcaneTower default element)
using DeNelle.Core.Diagnostics; // FlowTrace / Guard — TGVRU instrumentation (§12)
using DeNelle.Cosmetics;        // CosmeticApplier — the village-category cosmetic seam (see AttachCosmeticSeam)
namespace DeNelle.Village
{
    public static class StructureFactory
    {
        public const float YHeightVariable = 4f;
        private static float EffectiveVisualHeight(CatalogEntry entry, out bool isOverride)
        {
            float mult = entry != null && entry.repo != null ? entry.repo.heightMul : 1f;
            if (mult <= 0f) mult = 1f;   // guard a zero/unset/negative authored multiplier -> uniform base
            isOverride = !Mathf.Approximately(mult, 1f);
            return YHeightVariable * mult;
        }
        public static GameObject Create(CatalogEntry entry, Pose pose, Transform parent)
        {
            using var _ = FlowTrace.Enter("Structure", $"Create id='{entry?.id ?? "<null>"}'");
            if (entry == null)
            {
                FlowTrace.Fail("Structure", "Create called with a null entry — skipped (returning null; caller falls back).");
                return null;
            }
            if (entry.kind == EntryKind.Composite)
            {
                FlowTrace.Step("Structure", $"'{entry.id}' is Composite — delegating to CreateGroup.");
                return CreateGroup(entry, pose, parent);
            }
            var root = new GameObject(string.IsNullOrEmpty(entry.displayName)
                ? $"Structure-{entry.id}" : entry.displayName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            FlowTrace.Step("Structure", $"'{entry.id}' root '{root.name}' created at {pose.position}.");
            if (!string.IsNullOrEmpty(entry.visualPrefabPath))
            {
                float targetHeight = EffectiveVisualHeight(entry, out bool heightOverride);
                bool pendingArtArmed = false;
                var opts = OptsFor(entry);   // fit-to-height + per-row rotation policy + trace id
                FlowTrace.Step("Structure", $"'{entry.id}' fit-to-height target={targetHeight:0.##}m " +
                    $"(source={(heightOverride ? "override" : "default")}), " +
                    $"preservePrefabRotation={opts.PreservePrefabRotation}.");
                GameObject visual = Guard.Try("Structure",
                    $"skin '{entry.id}' visual '{entry.visualPrefabPath}'",
                    () => VisualFactory.Skin(root.transform, entry.visualPrefabPath, opts),
                    fallback: null);
                if (visual == null)
                {
                    FlowTrace.Fail("Structure", $"'{entry.id}': visual '{entry.visualPrefabPath}' " +
                        "is not resident yet — retaining a visible pending-art proxy and arming one " +
                        "WhenSettled retry (the building, footprint and behaviour remain present).");
                    visual = BuildPendingArtProxy(root, entry);
                    pendingArtArmed = true;
                    GameObject capturedProxy = visual;
                    DeNelle.Core.StructureContentWarmer.WhenSettled(() =>
                        TryReplacePendingArt(root, entry, capturedProxy));
                }
                if (entry.orientation != null && entry.orientation.manual)
                {
                    Guard.Try("Structure", $"apply orientation offset/scale '{entry.id}'", () =>
                    {
                        bool moved = false;
                        Vector3 off = entry.orientation.Offset;
                        if (off.sqrMagnitude > 0.0001f)
                        {
                            visual.transform.localPosition += off;
                            moved = true;
                        }
                        if (entry.orientation.HasScale)
                        {
                            visual.transform.localScale = Vector3.Scale(
                                visual.transform.localScale, entry.orientation.EffectiveScale);
                            moved = true;
                        }
                        if (moved)
                            ReseatCorrectedBottom(visual, root.transform.position.y);
                    });
                }
                if (!string.IsNullOrEmpty(entry.visualTexturePath))
                {
                    var fixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                    fixer?.SetForcedTexture(entry.visualTexturePath);
                }
                {
                    var missFixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                    missFixer?.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
                }
                if (!string.IsNullOrEmpty(entry.visualTexturePath))
                    Guard.Try("Structure", $"force texture '{entry.id}'",
                        () => ApplyForcedTexture(visual, entry.visualTexturePath, entry.id));
                else
                    FlowTrace.Once("Structure", "no-texpath-" + entry.id,
                        $"'{entry.id}': NO repo visualTexturePath authored — the forced-albedo " +
                        $"rebind is SKIPPED BY DESIGN and '{entry.visualPrefabPath}' keeps its own " +
                        "embedded materials. A Tripo FBX with no texPath renders WHITE in a player " +
                        "build (its only Color map lives in a .fbm folder that does not ship).");
                if (!VerifyStructureRenders(root, entry.id))
                {
                    FlowTrace.Fail("Structure", $"'{entry.id}': skinned visual " +
                        $"'{entry.visualPrefabPath}' FAILED RENDER VERIFICATION (no enabled renderer " +
                        "with a mesh) — discarding that broken body, retaining a visible pending-art " +
                        "proxy and arming one WhenSettled retry (the building, footprint and behaviour " +
                        "remain present; nothing paid-for is destroyed).");
                    if (!pendingArtArmed)
                    {
                        if (visual != null) DestroyRoot(visual);
                        GameObject renderProxy = BuildPendingArtProxy(root, entry);
                        DeNelle.Core.StructureContentWarmer.WhenSettled(() =>
                            TryReplacePendingArt(root, entry, renderProxy));
                    }
                }
            }
            AttachCosmeticSeam(root, entry);
            AttachBehavior(root, entry);
            FlowTrace.Step("Structure", $"'{entry.id}' created OK -> '{root.name}'.");
            return root;
        }
        private static GameObject BuildPendingArtProxy(GameObject root, CatalogEntry entry)
        {
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.name = "StructureArtPending-" + (entry != null ? entry.id : "unknown");
            proxy.transform.SetParent(root.transform, false);
            float height = EffectiveVisualHeight(entry, out _);
            proxy.transform.localScale = new Vector3(height * 0.45f, height, height * 0.45f);
            proxy.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            var primitiveCollider = proxy.GetComponent<Collider>();
            if (primitiveCollider != null) Object.Destroy(primitiveCollider);
            var renderer = proxy.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.72f, 0.28f, 0.08f, 1f);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return proxy;
        }
        private static void TryReplacePendingArt(GameObject root, CatalogEntry entry, GameObject proxy)
        {
            if (root == null || entry == null || proxy == null) return;
            var visual = Guard.Try("Structure", $"late skin '{entry.id}' after residency settled",
                () => VisualFactory.Skin(root.transform, entry.visualPrefabPath, OptsFor(entry)),
                fallback: null);
            if (visual == null)
            {
                FlowTrace.Fail("Structure", $"'{entry.id}': residency settled but " +
                    $"'{entry.visualPrefabPath}' still did not resolve — retaining visible proxy; building not lost.");
                return;
            }
            if (entry.orientation != null && entry.orientation.manual)
            {
                bool moved = false;
                Vector3 off = entry.orientation.Offset;
                if (off.sqrMagnitude > 0.0001f) { visual.transform.localPosition += off; moved = true; }
                if (entry.orientation.HasScale)
                {
                    visual.transform.localScale = Vector3.Scale(visual.transform.localScale,
                        entry.orientation.EffectiveScale);
                    moved = true;
                }
                if (moved) ReseatCorrectedBottom(visual, root.transform.position.y);
            }
            if (!string.IsNullOrEmpty(entry.visualTexturePath))
            {
                var fixer = visual.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                fixer?.SetForcedTexture(entry.visualTexturePath);
                ApplyForcedTexture(visual, entry.visualTexturePath, entry.id);
            }
            visual.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true)
                ?.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
            if (!VerifyStructureRenders(visual, entry.id))
            {
                DestroyRoot(visual);
                FlowTrace.Fail("Structure", $"'{entry.id}': late visual failed render verification — " +
                    "retaining visible proxy; building not lost.");
                return;
            }
            Object.Destroy(proxy);
            CosmeticApplier.RefreshOn(root);
            FlowTrace.Step("Structure", $"'{entry.id}': residency retry replaced pending-art proxy " +
                $"with '{visual.name}' (building root and footprint never disappeared).");
        }
```

## Assets/_Modules/Village/Catalog/StructureFactory.cs — lines 508-752 of 1445; full-file SHA256 FD7F5D1944BF04DB5A2E0310C9D39DEC9C1012DA99C3A2210E3579CDD2CB0AC4
```csharp
        public static string VisualPathForLevel(CatalogEntry entry, int level)
        {
            if (entry == null) return null;
            var ladder = entry.repo != null ? entry.repo.upgradeVisualPath : null;
            if (level >= 2 && ladder != null && ladder.Length >= level - 1
                && !string.IsNullOrEmpty(ladder[level - 2]))
                return ladder[level - 2];
            return entry.visualPrefabPath;
        }
        public static string TexturePathForLevel(CatalogEntry entry, int level)
        {
            if (entry == null) return null;
            var ladder = entry.repo != null ? entry.repo.upgradeTexturePath : null;
            if (level >= 2 && ladder != null && ladder.Length >= level - 1
                && !string.IsNullOrEmpty(ladder[level - 2]))
                return ladder[level - 2];
            return entry.visualTexturePath;
        }
        public static bool ReskinForLevel(GameObject root, CatalogEntry entry, int level)
        {
            if (root == null || entry == null) return false;
            bool towerLike = entry.id != null &&
                (entry.id.IndexOf("arcane", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.id.IndexOf("wizard", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.id.IndexOf("spire",  System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.id.IndexOf("mage",   System.StringComparison.OrdinalIgnoreCase) >= 0);
            ArcaneAura.EscalateTo(root, level, ensure: towerLike);   // idle aura grows with the tier
            var arcaneSpire = root.GetComponent<ArcaneTower>();
            if (arcaneSpire != null) arcaneSpire.SetVfxLevel(level);  // firing bursts grow with the tier
            string path = VisualPathForLevel(entry, level);
            if (string.IsNullOrEmpty(path) || path == entry.visualPrefabPath)
                return false;   // no authored tier model — legacy scale/tint applies
            string stem = path.Substring(path.LastIndexOf('/') + 1);
            for (int i = 0; i < root.transform.childCount; i++)
                if (root.transform.GetChild(i).name.StartsWith(stem)) return true;
            var old = new List<GameObject>();
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var c = root.transform.GetChild(i);
                if (c.GetComponentInChildren<Renderer>(true) != null) old.Add(c.gameObject);
            }
            GameObject visual = Guard.Try("Structure",
                $"reskin '{entry.id}' L{level} visual '{path}'",
                () => VisualFactory.Skin(root.transform, path, OptsForUpgradeLevel(entry, level)),
                fallback: null);
            if (visual == null)
            {
                FlowTrace.Fail("Structure", $"'{entry.id}': tier-{level} visual '{path}' failed to " +
                    "skin — keeping the previous visual (structure never blanks).");
                return false;
            }
            string texPath = TexturePathForLevel(entry, level);
            if (!string.IsNullOrEmpty(texPath))
            {
                var fixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                fixer?.SetForcedTexture(texPath);
            }
            {
                var missFixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                missFixer?.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
            }
            foreach (var g in old) Object.Destroy(g);
            CosmeticApplier.RefreshOn(root);
            FlowTrace.Step("Structure", $"'{entry.id}' reskinned to tier-{level} model '{stem}' " +
                $"(replaced {old.Count} old visual(s)); village cosmetic seam re-driven.");
            return true;
        }
        public static SkinOptions OptsForUpgradeLevel(CatalogEntry entry, int level)
        {
            var opts = OptsFor(entry, applyManualEuler: false);
            int index = level - 2;
            var ladder = entry != null && entry.repo != null
                ? entry.repo.upgradeOrientationEuler
                : null;
            if (index >= 0 && ladder != null && index < ladder.Length)
            {
                var euler = ladder[index];
                if (euler != null && euler.Length >= 3)
                {
                    var degrees = new Vector3(euler[0], euler[1], euler[2]);
                    if (degrees.sqrMagnitude > 0.0001f)
                        opts.LocalRotation = Quaternion.Euler(degrees);
                    FlowTrace.Step("Structure",
                        $"OptsForUpgradeLevel('{entry.id}', L{level}): tier Euler={degrees} (index {index}).");
                }
            }
            return opts;
        }
        public static SkinOptions OptsFor(CatalogEntry entry, bool applyManualEuler = true)
        {
            var o = SkinOptions.Structure(0f);   // clear FitLargest
            o.FitHeight = EffectiveVisualHeight(entry, out _);
            o.PreservePrefabRotation = entry != null && entry.repo != null && entry.repo.preservePrefabRotation;
            o.MaxFootprint = entry != null && entry.repo != null ? entry.repo.maxFootprint : 0f;
            if (applyManualEuler && entry != null && entry.orientation != null && entry.orientation.manual)
            {
                Vector3 e = entry.orientation.Euler;
                if (e.sqrMagnitude > 0.0001f)
                    o.LocalRotation = Quaternion.Euler(e);
            }
            o.TraceId = entry != null ? entry.id : null;
            string channel = o.LocalRotation.HasValue ? "MANUAL EULER (opts.LocalRotation, pre-fit)"
                           : o.PreservePrefabRotation ? "PRESERVE PREFAB ROTATION (repo opt-in row)"
                           : "IDENTITY RESET (DEF-232 default)";
            FlowTrace.Step("Structure",
                $"OptsFor('{o.TraceId ?? "<null>"}'): rotation channel = {channel}; " +
                $"catalogEuler={(entry?.orientation != null ? entry.orientation.Euler.ToString() : "<none>")} " +
                $"manual={(entry?.orientation != null && entry.orientation.manual)} " +
                $"preserve={o.PreservePrefabRotation} applyManualEuler={applyManualEuler} " +
                $"fitHeight={o.FitHeight:0.###} maxFootprint={o.MaxFootprint:0.###}.");
            return o;
        }
```

## Assets/_Modules/Village/Catalog/StructureFactory.cs — lines 908-1125 of 1445; full-file SHA256 FD7F5D1944BF04DB5A2E0310C9D39DEC9C1012DA99C3A2210E3579CDD2CB0AC4
```csharp
        private static void ReseatCorrectedBottom(GameObject visual, float groundY)
        {
            if (visual == null) return;
            Guard.Try("Structure", "reseat corrected bottom", () =>
            {
                if (!TryWorldBounds(visual, out Bounds b))
                {
                    FlowTrace.Warn("Structure", $"ReseatCorrectedBottom: no measurable bounds on '{visual.name}' — left at seat (may float/sink).");
                    return;
                }
                float dy = groundY - b.min.y;
                if (!Mathf.Approximately(dy, 0f))
                    visual.transform.position += new Vector3(0f, dy, 0f);
                if (!VisualFactory.IsSeatedOnGround(visual, groundY, out float bottomY))
                    FlowTrace.Warn("Structure",
                        $"ReseatCorrectedBottom('{visual.name}') LEFT IT OFF THE GROUND: bounds bottom " +
                        $"y={bottomY:F2} vs ground y={groundY:F2} (off by {bottomY - groundY:F2} m, " +
                        $"tolerance {VisualFactory.SeatEpsilonMetres:F2} m) — this structure floats/sinks.");
            });
        }
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> s_footprintXzCache =
            new System.Collections.Generic.Dictionary<string, Vector2>();
        public static float MeasureUprightFootprintMetres(CatalogEntry entry)
        {
            Vector2 xz = MeasureUprightFootprintXZ(entry);
            return Mathf.Max(xz.x, xz.y);
        }
        public static Vector2 MeasureUprightFootprintXZ(CatalogEntry entry)
        {
            float authored = entry != null && entry.repo != null && entry.repo.placement != null
                ? Mathf.Max(1f, entry.repo.placement.footprint) : 3f;
            Vector2 authoredV = new Vector2(authored, authored);
            if (entry == null || string.IsNullOrEmpty(entry.visualPrefabPath)) return authoredV;
            var o = entry.orientation;
            Vector3 es = o != null ? o.EffectiveScale : Vector3.one;
            string key = o != null && o.manual
                ? $"{entry.id}|{o.Euler.x:0.#},{o.Euler.y:0.#},{o.Euler.z:0.#}|{es.x:0.##},{es.y:0.##},{es.z:0.##}|xz"
                : entry.id + "|xz";
            if (s_footprintXzCache.TryGetValue(key, out Vector2 cached)) return cached;
            var probe = new GameObject("FootprintProbe");
            probe.hideFlags = HideFlags.HideAndDontSave;
            Vector2 result = authoredV;
            try
            {
                Guard.Try("Structure", $"measure upright footprint XZ '{entry.id}'", () =>
                {
                    var opts = OptsFor(entry);
                    var visual = VisualFactory.Skin(probe.transform, entry.visualPrefabPath, opts);
                    if (visual == null)
                    {
                        FlowTrace.Warn("Structure",
                            $"MeasureUprightFootprintXZ '{entry.id}': visual '{entry.visualPrefabPath}' failed to skin — using authored {authored:0.##}m square.");
                        return;
                    }
                    if (entry.orientation != null && entry.orientation.manual)
                    {
                        visual.transform.localRotation = Quaternion.Euler(entry.orientation.Euler) * visual.transform.localRotation;
                        visual.transform.localPosition += entry.orientation.Offset;
                        if (entry.orientation.HasScale)
                            visual.transform.localScale = Vector3.Scale(visual.transform.localScale, entry.orientation.EffectiveScale);
                    }
                    if (TryWorldBounds(visual, out Bounds b))
                    {
                        result = new Vector2(
                            Mathf.Max(0.1f, b.size.x),
                            Mathf.Max(0.1f, b.size.z));
                    }
                    else
                        FlowTrace.Warn("Structure",
                            $"MeasureUprightFootprintXZ '{entry.id}': no measurable bounds — using authored square.");
                });
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(probe);
                else                       Object.DestroyImmediate(probe);
            }
            s_footprintXzCache[key] = result;
            return result;
        }
        public static float MeasureClaimFootprintMetres(CatalogEntry entry)
        {
            Vector2 xz = MeasureClaimFootprintXZ(entry);
            return Mathf.Max(xz.x, xz.y);
        }
        public static Vector2 MeasureClaimFootprintXZ(CatalogEntry entry)
        {
            Vector2 measured = MeasureUprightFootprintXZ(entry);
            if (entry == null ||
                (entry.type != CatalogType.Wall && entry.type != CatalogType.Gate)) return measured;
            float authored = entry.repo != null && entry.repo.placement != null
                ? Mathf.Max(0.01f, entry.repo.placement.footprint)
                : Mathf.Max(measured.x, measured.y);
            string typeName = entry.type.ToString().ToUpperInvariant();
            FlowTrace.Once("Build", typeName.ToLowerInvariant() + "-claim-" + entry.id,
                $"{typeName} CLAIM '{entry.id}' (type={entry.type}): grid claim AUTHORED " +
                $"placement.footprint={authored:0.###}m (both axes → one-cell tile), mesh measures " +
                $"({measured.x:0.###} x {measured.y:0.###})m. " +
                "Mesh is NOT resized — claim only (WO-972 + WO-986 + WO-1153).");
            return new Vector2(authored, authored);
        }
        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null) return false;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                bounds = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
                return true;
            }
            var col = go.GetComponentInChildren<Collider>(true);
            if (col != null) { bounds = col.bounds; return true; }
            return false;
        }
```

## Assets/_Modules/Village/VisualFactory.cs — lines 1-678 of 678; full-file SHA256 4D12FB5C418EE90BACC3FEF03303103A58A14C862BF12545FC02331801BB47E6
```csharp
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;
namespace DeNelle.Village
{
    public struct SkinOptions
    {
        public float FitHeight;          // >0 → scale so world-bounds HEIGHT = this
        public float FitLargest;         // >0 → scale so LARGEST world-bounds dim = this (wins over FitHeight)
        public float MaxFootprint;
        public bool  SeatOnGround;       // shift so the bounds base sits at the host's y
        public bool  StripColliders;     // remove the model's own colliders (the host owns its collider)
        public bool  FixTripoMaterials;  // attach DeNelle.Core.TripoMaterialFixer (Tripo→URP) via reflection
        public Quaternion? LocalRotation;
        public bool SeatFlat;
        public bool PreservePrefabRotation;
        public string TraceId;
        public static SkinOptions Enemy(float height) =>
            new SkinOptions { FitHeight = height, StripColliders = true };
        public static SkinOptions Structure(float largest) =>
            new SkinOptions { FitLargest = largest, SeatOnGround = true, FixTripoMaterials = true };
        public static SkinOptions Prop(float largest) =>
            new SkinOptions { FitLargest = largest, SeatOnGround = true };
    }
    public static class VisualFactory
    {
        private const int DefaultMissLogCap = 3;
        private static int MissLogCap => Mathf.Max(1,
            DeNelle.Core.Ops.RemoteTunables.Int(DeNelle.Core.Ops.RemoteTunables.KeyVisualsMissLogCap));
        private static readonly Dictionary<string, int> s_missLogCounts = new Dictionary<string, int>();
        private static void ReportResolveMiss(string resourcesPath)
        {
            s_missLogCounts.TryGetValue(resourcesPath, out int n);
            n++;
            s_missLogCounts[resourcesPath] = n;
            string cause = DeNelle.Core.StructureContentWarmer.LastFailureCause(resourcesPath);
            int attempts = DeNelle.Core.StructureContentWarmer.AttemptsFor(resourcesPath);
            string detail =
                $"model not found via Addressables OR Resources: '{resourcesPath}' — returning null " +
                "(caller falls back). UNDERLYING FETCH CAUSE: " +
                (cause ?? "none recorded — no async fetch has FAILED for this address, so the bytes were " +
                          "either never requested or are still in flight") +
                $" [fetchAttempts={attempts}/{DeNelle.Core.StructureContentWarmer.MaxRequestAttempts}, " +
                $"resolveAttempts={n}, warmerState={DeNelle.Core.StructureContentWarmer.State}, " +
                $"resident={DeNelle.Core.StructureContentWarmer.ResidentCount}, " +
                $"pending={DeNelle.Core.StructureContentWarmer.PendingRequests}, " +
                $"lastTransportUrl={DeNelle.Core.StructureContentWarmer.LastRequestUrl ?? "(none)"}]";
            if (n <= MissLogCap)
            {
                FlowTrace.Fail("VisualFactory", detail);
                return;
            }
            if (n == MissLogCap + 1)
            {
                FlowTrace.Fail("VisualFactory",
                    $"RESOLVE-LOG CAP: '{resourcesPath}' has now missed {n} times. Further misses for this " +
                    "address are THROTTLED to roughly one line every 10s for the rest of the launch — they " +
                    "are NOT suppressed and the address is NOT abandoned here (the fetch retry budget in " +
                    "StructureContentWarmer owns that decision). " + detail);
                return;
            }
            FlowTrace.Throttle("VisualFactory", "miss-" + resourcesPath, 10f,
                $"(throttled, miss #{n}) " + detail);
        }
        public static GameObject Skin(Transform host, string resourcesPath, SkinOptions opts)
        {
            using var _ = DeNelle.Core.Ops.RemoteTunables.Int(
                              DeNelle.Core.Ops.RemoteTunables.KeyTraceAssetVerbosity)
                          >= DeNelle.Core.Ops.RemoteTunables.VerbosityVerbose
                ? FlowTrace.Enter("VisualFactory", $"Skin('{resourcesPath}')")
                : default;
            GameObject prefab = null;
            FlowTrace.Try("VisualFactory", $"resolve '{resourcesPath}'",
                () => prefab = DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(resourcesPath));
            if (prefab == null)
            {
                ReportResolveMiss(resourcesPath);
                return null;
            }
            FlowTrace.Step("VisualFactory", $"resolved Resources model '{resourcesPath}' -> '{prefab.name}'.");
            return Skin(host, prefab, opts);
        }
        public static GameObject Skin(Transform host, GameObject prefab, SkinOptions opts)
        {
            if (prefab == null)
            {
                FlowTrace.Fail("VisualFactory", "Skin called with a null prefab — returning null (caller falls back).");
                return null;
            }
            using var _ = FlowTrace.Enter("VisualFactory", $"Skin(prefab='{prefab.name}')");
            GameObject go = null;
            FlowTrace.Try("VisualFactory", $"Instantiate '{prefab.name}'",
                () => go = Object.Instantiate(prefab, host));
            if (go == null)
            {
                FlowTrace.Fail("VisualFactory",
                    $"Instantiate returned null for prefab '{prefab.name}' — returning null (caller falls back).");
                return null;
            }
            go.transform.localPosition = Vector3.zero;
            string who = string.IsNullOrEmpty(opts.TraceId)
                ? $"'{prefab.name}'"
                : $"'{prefab.name}' (entry='{opts.TraceId}')";
            void TraceXform(string stage)
            {
                var t = go.transform;
                string measured = "bounds=<none>";
                if (TryBounds(go, out Bounds tb))
                {
                    float widest = Mathf.Max(tb.size.x, tb.size.z);
                    float aspect = widest > 0.0001f ? tb.size.y / widest : 0f;
                    measured = $"bounds size=({tb.size.x:0.###}w x {tb.size.y:0.###}h x {tb.size.z:0.###}d) " +
                               $"aspect={aspect:0.###} minY={tb.min.y:0.###}";
                }
                FlowTrace.Step("Xform", $"{who} after {stage}: " +
                    $"euler={t.localEulerAngles} pos={t.localPosition} scale={t.localScale} {measured}");
            }
            TraceXform("instantiate (prefab-native pose)");
            if (opts.LocalRotation.HasValue)
                go.transform.localRotation = opts.LocalRotation.Value;
            else if (!opts.PreservePrefabRotation)
                go.transform.localRotation = Quaternion.identity;
            TraceXform(opts.LocalRotation.HasValue ? "opts.LocalRotation"
                     : opts.PreservePrefabRotation ? "prefab rotation PRESERVED (WO-928, opt-in row)"
                     : "LocalRotation identity (DEF-232 default)");
            if (opts.SeatFlat)
            {
                FlowTrace.Try("VisualFactory", "seat flat (bounds-derived)", () => SeatFlat(go));
                TraceXform("SeatFlat");
            }
            if (opts.StripColliders)
                FlowTrace.Try("VisualFactory", "strip colliders",
                    () => { foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c); });
            if (opts.FixTripoMaterials)
                FlowTrace.Try("VisualFactory", "add Tripo material fixer", () => TryAddTripoFixer(go));
            FlowTrace.Try("VisualFactory", "fit + seat", () =>
            {
                if (opts.FitLargest > 0f)     Fit(go, opts.FitLargest, largest: true);
                else if (opts.FitHeight > 0f) Fit(go, opts.FitHeight,  largest: false);
                if (opts.MaxFootprint > 0f) CapFootprint(go, opts.MaxFootprint);
                if (opts.SeatOnGround)
                    SeatOnGround(go, host != null ? host.position : go.transform.position);
            });
            TraceXform("Fit+SeatOnGround");
            if (!VerifyRenders(go, prefab.name))
            {
                Object.Destroy(go);
                return null;
            }
            FlowTrace.Try("VisualFactory", "wardrobe default-dress",
                () => { if (BlinkWardrobe.IsDressable(go)) BlinkWardrobe.DressInStarter(go); });
            FlowTrace.Try("VisualFactory", "material trace", () =>
            {
                var renderer = go.GetComponentInChildren<Renderer>();
                if (renderer == null || renderer.sharedMaterial == null)
                    FlowTrace.Warn("EnemyVisual", $"Material on {prefab.name}: NO renderer/material (would render blank/fallback)");
                else
                    FlowTrace.Step("EnemyVisual", $"Material on {prefab.name}: {renderer.sharedMaterial.name}");
            });
            FlowTrace.Try("VisualFactory", "magenta sweep (runtime spawn seam)",
                () => DeNelle.Core.MagentaGuard.SweepGameObject(go, "VisualFactory.Skin"));
            return go;
        }
        private static bool VerifyRenders(GameObject go, string what)
        {
            if (go == null)
            {
                FlowTrace.Fail("VisualFactory", $"VerifyRenders: skinned '{what}' instance is null.");
                return false;
            }
            var rends = go.GetComponentsInChildren<Renderer>(true);
            int total = 0, enabled = 0, withMesh = 0;
            foreach (var r in rends)
            {
                if (r == null) continue;
                total++;
                bool on = r.enabled && r.gameObject.activeInHierarchy;
                bool hasMesh = false;
                if (r is SkinnedMeshRenderer smr) hasMesh = smr.sharedMesh != null;
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    hasMesh = mf != null && mf.sharedMesh != null;
                }
                if (on) enabled++;
                if (on && hasMesh) withMesh++;
            }
            bool boundsOk = TryBounds(go, out Bounds b) && b.size.sqrMagnitude > 1e-8f;
            bool renders = enabled > 0 && withMesh > 0 && boundsOk;
            FlowTrace.Step("VisualFactory",
                $"skinned '{what}' on '{go.name}': renderers={total} enabled={enabled} withMesh={withMesh} " +
                $"boundsSize={(boundsOk ? b.size.ToString("F2") : "<degenerate>")} => renders={renders}");
            if (!renders)
            {
                FlowTrace.Fail("VisualFactory",
                    $"VerifyRenders FAILED for skinned '{what}' on '{go.name}': renderers={total} enabled={enabled} " +
                    $"withMesh={withMesh} boundsOk={boundsOk} — treating as a MISS (destroy + return null; caller falls back).");
                return false;
            }
            return true;
        }
        private static void Fit(GameObject go, float target, bool largest)
        {
            if (!TryBounds(go, out Bounds b))
            {
                FlowTrace.Warn("VisualFactory",
                    $"Fit('{go?.name}'): NO measurable renderer bounds — NOT fitted, scale left at " +
                    $"{(go != null ? go.transform.localScale.ToString("F3") : "<null>")}.");
                return;
            }
            float measure = largest ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : b.size.y;
            if (measure < 0.0001f)
            {
                FlowTrace.Warn("VisualFactory",
                    $"Fit('{go?.name}'): measured axis is degenerate ({measure:0.#####} m) — NOT fitted.");
                return;
            }
            float k = target / measure;
            FlowTrace.Step("VisualFactory",
                $"Fit('{go.name}'): mode={(largest ? "largest" : "height")} measured={measure:0.###}m " +
                $"of bounds ({b.size.x:0.###} x {b.size.y:0.###} x {b.size.z:0.###}) " +
                $"target={target:0.###}m -> scale x{k:0.####} (from {go.transform.localScale.x:0.####}).");
            go.transform.localScale *= k;
        }
        public const float SeatEpsilonMetres = 0.05f;
        public static bool IsSeatedOnGround(GameObject go, float groundY, out float bottomY,
                                            float epsilon = SeatEpsilonMetres)
        {
            bottomY = float.NaN;
            if (go == null || !TryBounds(go, out Bounds b)) return false;
            bottomY = b.min.y;
            return Mathf.Abs(bottomY - groundY) <= epsilon;
        }
        private static void CapFootprint(GameObject go, float maxMetres)
        {
            if (maxMetres <= 0f || !TryBounds(go, out Bounds b)) return;
            float widest = Mathf.Max(b.size.x, b.size.z);
            if (widest < 0.0001f || widest <= maxMetres) return;
            float k = maxMetres / widest;
            go.transform.localScale *= k;
            FlowTrace.Step("VisualFactory",
                $"footprint cap: widest {widest:0.##}m > {maxMetres:0.##}m — scaled x{k:0.###} uniformly " +
                "(height follows; this row's fit-time pose is flat, so fit-to-height alone over-scales it).");
        }
        private static void SeatOnGround(GameObject go, Vector3 basePos)
        {
            if (!TryBounds(go, out Bounds b))
            {
                FlowTrace.Warn("VisualFactory",
                    $"SeatOnGround('{go?.name}'): NO measurable renderer bounds — NOT seated, left at " +
                    $"{(go != null ? go.transform.position.ToString("F2") : "<null>")} (ground y={basePos.y:F2}). " +
                    "The body may float or sink; check the model has an enabled renderer with a mesh.");
                return;
            }
            Vector3 delta = new Vector3(basePos.x - b.center.x,
                                        basePos.y - b.min.y,
                                        basePos.z - b.center.z);
            go.transform.position += delta;
            if (!IsSeatedOnGround(go, basePos.y, out float bottomY))
            {
                FlowTrace.Warn("VisualFactory",
                    $"SeatOnGround('{go.name}') LEFT IT OFF THE GROUND: bounds bottom y={bottomY:F2} vs " +
                    $"ground y={basePos.y:F2} (off by {bottomY - basePos.y:F2} m, tolerance " +
                    $"{SeatEpsilonMetres:F2} m). NOTE: the Xform line's localPosition is the PIVOT, " +
                    "not the bottom — this line is the one that decides whether it floats.");
            }
        }
        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            bounds = default;
            if (rends.Length == 0) return false;
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
            return true;
        }
        private static void SeatFlat(GameObject go)
        {
            if (go == null || !TryBounds(go, out Bounds b)) return;
            Vector3 sz = b.size;
            int shortest = (sz.x <= sz.y && sz.x <= sz.z) ? 0 : (sz.y <= sz.z ? 1 : 2);
            if (shortest == 1)
            {
                FlowTrace.Step("VisualFactory",
                    $"SeatFlat('{go.name}'): already flat (Y narrowest, size {sz:F2}) — no rotation.");
                return;
            }
            Vector3 shortAxis = shortest == 0 ? Vector3.right : Vector3.forward;
            Quaternion delta = Quaternion.FromToRotation(shortAxis, Vector3.up);
            go.transform.rotation = delta * go.transform.rotation;
            FlowTrace.Step("VisualFactory",
                $"SeatFlat('{go.name}'): narrowest axis was {(shortest == 0 ? "X" : "Z")} " +
                $"(size {sz:F2}) → stood it to +Y so the flat face rests down.");
        }
        private static void TryAddTripoFixer(GameObject go)
        {
            var fixer = go.GetComponent<DeNelle.Core.TripoMaterialFixer>();
            if (fixer == null) fixer = go.AddComponent<DeNelle.Core.TripoMaterialFixer>();
            fixer.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
        }
    }
}
```

## Assets/_Modules/Core/Addressables/StructureAssetLoader.cs — lines 1-238 of 238; full-file SHA256 F182741F1828BD48507CE1CB4F3B8C84180B9A3D4FF12592973FBD5DEA65B93D
```csharp
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;
namespace DeNelle.Core
{
    public static class StructureAssetLoader
    {
        public const string System = "StructureAssets";
        public const string StructureAddrPrefix = "Structures/";
        private static readonly HashSet<string> s_reportedMisses = new HashSet<string>();
        public static GameObject LoadStructurePrefab(string key) => Load<GameObject>(key);
        public static T LoadStructureAsset<T>(string key) where T : Object => Load<T>(key);
        private static T Load<T>(string address) where T : Object
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            T result = null;
            if (StructureContentWarmer.TryGet(address, out result) && result != null)
            {
                FlowTrace.Once(System, "addr-hit-" + address,
                    $"'{address}' served RESIDENT from the structure warm cache " +
                    "(out of the force-included Resources payload, and off the blocking path).");
                DependencyClosureTrace.Verify(System, address, result, viaFallback: false);
                return result;
            }
#if UNITY_EDITOR
            result = StructureEditorSyncResolver.Resolve<T>(address);
            if (result != null)
            {
                DependencyClosureTrace.Verify(System, address, result, viaFallback: false);
                return result;
            }
#endif
            Guard.Try(System, $"Resources.Load {address} ({typeof(T).Name})", () =>
            {
                result = Resources.Load<T>(address);
            });
            if (result != null)
            {
                DependencyClosureTrace.Verify(System, address, result, viaFallback: true);
                return result;
            }
            bool knownAbsent = StructureContentWarmer.IsKnownAbsent(address);
            float waited = StructureContentWarmer.SecondsWaiting(address);
            if (!knownAbsent) StructureContentWarmer.Request(address);
            if (knownAbsent || waited > StructureContentWarmer.MissEscalateSeconds)
            {
                if (s_reportedMisses.Add(address))
                {
                    FlowTrace.Fail(System,
                        $"structure asset '{address}' ({typeof(T).Name}) still unresolved after " +
                        $"{waited:F1}s via the resident cache OR Resources — the caller is keeping its " +
                        $"baked/previous visual (warmerState={StructureContentWarmer.State}, " +
                        $"knownAbsent={knownAbsent}, resident={StructureContentWarmer.ResidentCount}, " +
                        $"pending={StructureContentWarmer.PendingRequests}). Check repo.visualPrefabPath " +
                        "against the assets on disk, and that the grouper registered this exact address. " +
                        $"UNDERLYING FETCH CAUSE: {StructureContentWarmer.LastFailureCause(address) ?? "none recorded — no async fetch has failed for this address"}. " +
                        $"attempts={StructureContentWarmer.AttemptsFor(address)}/{StructureContentWarmer.MaxRequestAttempts}, " +
                        $"lastTransportUrl={StructureContentWarmer.LastRequestUrl ?? "(none)"}. " +
                        "NOTE: this is a VISUAL defect. The game did not stall — that is deliberate.");
                }
            }
            else
            {
                FlowTrace.Throttle(System, "skip-" + address, 1f,
                    $"'{address}' ({typeof(T).Name}) is not resident yet — SKIPPING this frame after " +
                    $"{waited:F1}s and requesting it asynchronously (warmerState={StructureContentWarmer.State}, " +
                    $"pending={StructureContentWarmer.PendingRequests}). The caller keeps its current visual. " +
                    "This deliberately does NOT wait: waiting here is what deadlocked the game on 2026-08-20.");
            }
            return null;
        }
    }
}
```

## Assets/_Modules/Core/Catalog/CatalogEntry.cs — lines 1-225 of 225; full-file SHA256 7AFDD449576A08360B0CA75CB7F4FDADA3C3199BCD1558015448EAE0BC17E14B
```csharp
using UnityEngine;
namespace DeNelle.Core.Catalog
{
    [System.Serializable]
    public sealed class CellPlacement
    {
        public string  cellEntryId;
        public Vector3 offset;
        public float   yRotation;   // 90° steps
        public CellPlacement() { }
        public CellPlacement(string cellEntryId, Vector3 offset, float yRotation)
        {
            this.cellEntryId = cellEntryId;
            this.offset = offset;
            this.yRotation = yRotation;
        }
    }
    [System.Serializable]
    public sealed class CatalogEntry
    {
        public string      id;
        public string      displayName;
        public string      description;
        public CatalogType type;
        public EntryKind   kind = EntryKind.Cell;
        public string      role;
        public string[]    manageFilters;
        public string      manageArtKey;
        public int         displayOrder;
        public string      visualPrefabPath;
        public string      visualTexturePath;
        public RepoProps   repo = new RepoProps();
        public CellPlacement[] composite = null;
        public OrientationFix orientation = null;
    }
    [System.Serializable]
    public sealed class OrientationFix
    {
        public bool    corrected;
        public bool    manual;        // true = human-verified (Inspector) → applied; false = advisory
        public float[] euler;         // [x,y,z] degrees
        public float[] offset;        // [x,y,z] metres
        public float   scale = 1f;    // UNIFORM scale multiplier (legacy / back-compat).
        public float[] scaleAxis;     // [x,y,z] per-axis multipliers; null/short → (1,1,1)
        public string  note;
        public Vector3 Euler  => euler  != null && euler.Length  == 3 ? new Vector3(euler[0],  euler[1],  euler[2])  : Vector3.zero;
        public Vector3 Offset => offset != null && offset.Length == 3 ? new Vector3(offset[0], offset[1], offset[2]) : Vector3.zero;
        public Vector3 ScaleAxis => scaleAxis != null && scaleAxis.Length == 3
            ? new Vector3(scaleAxis[0], scaleAxis[1], scaleAxis[2])
            : Vector3.one;
        public Vector3 EffectiveScale
        {
            get
            {
                float s = scale > 0f ? scale : 1f;
                Vector3 a = ScaleAxis;
                return new Vector3(
                    Mathf.Max(0.0001f, s * a.x),
                    Mathf.Max(0.0001f, s * a.y),
                    Mathf.Max(0.0001f, s * a.z));
            }
        }
        public bool HasScale
        {
            get
            {
                Vector3 e = EffectiveScale;
                return !Mathf.Approximately(e.x, 1f)
                    || !Mathf.Approximately(e.y, 1f)
                    || !Mathf.Approximately(e.z, 1f);
            }
        }
    }
}
```

## Assets/_Modules/Core/Catalog/RepoProps.cs — lines 1-511 of 511; full-file SHA256 C75CCDCE0BFA06CA44698B82AF5FFDEEA67EC771F0EE8BE19697B0C0B1B5CD21
```csharp
using DeNelle.Core.Combat;
namespace DeNelle.Core.Catalog
{
    [System.Serializable]
    public struct ResourceCost
    {
        public int wood;
        [UnityEngine.Serialization.FormerlySerializedAs("food")]
        [Newtonsoft.Json.JsonProperty("food")] public int stone;
        public int iron;
        public int crystals;
        public bool IsZero => wood == 0 && stone == 0 && iron == 0 && crystals == 0;
    }
    [System.Serializable]
    public struct RepairCrystalRate
    {
        public float perWood;
        [UnityEngine.Serialization.FormerlySerializedAs("perFood")]
        [Newtonsoft.Json.JsonProperty("perFood")] public float perStone;
        public float perIron;
        public bool IsZero => perWood <= 0f && perStone <= 0f && perIron <= 0f;
    }
    [System.Serializable]
    public sealed class RepoProps
    {
        public NavSurfaceKind navSurface = NavSurfaceKind.None;
        public int buildCost = 0;
        public ResourceCost cost = new ResourceCost();
        public RepairCrystalRate repairCrystalsPer = new RepairCrystalRate();
        public const int MaxStructureLevel = 6;
        public int maxLevel = 1;
        public ResourceCost[] upgradeCost = null;
        public string[] upgradeVisualPath = null;
        public float[][] upgradeOrientationEuler = null;
        public string[] upgradeTexturePath = null;
        public int wallTierBase = 0;
        public string behaviorId = null;
        public string collectorBuildingId = null;
        public string[] satisfiedByStructureIds = null;
        public int restoreGoldCost = 0;
        public bool singleton = false;
        public string[] bakedTwins = null;
        public string npcModel = null;
        public int storageCapacity = 0;
        public string storageResource = null;
        public bool IsStorageContainer => storageCapacity > 0;
        public int capacity = 0;
        public PlacementRules placement = new PlacementRules();
        public float heightMul = 1.0f;
        public float maxFootprint = 0f;
        public float visualHeight = 0f;
        public bool preservePrefabRotation = false;
        public float         range     = 0f;
        public float         damage    = 0f;
        public float         fireRate  = 0f;     // shots per second
        public bool          canHitAir = false;  // ground = false · wall-walk = true
        public bool          airOnly   = false;
        public DamageElement element   = DamageElement.None;
        public string projectileStyle = null;
        public float aoeRadius      = 0f;   // splash radius (m) around the impact; 0 = use component default
        public float slowSeconds    = 0f;   // Slow debuff duration (s) on every blast victim; 0 = no slow
        public float splashFraction = 0f;   // 0-1 fraction of damage to non-primary victims; 0 = use default
    }
}
```

## Assets/_Modules/Core/Catalog/PlacementRules.cs — lines 1-34 of 34; full-file SHA256 E1B9201F2FFE214A3E3A487B0D57253DB70DB69F3D8679DBB5CECC1D1B12FDCB
```csharp
namespace DeNelle.Core.Catalog
{
    [System.Serializable]
    public sealed class PlacementRules
    {
        public PlacementSurface mustSitOn = PlacementSurface.AnyTerrain;
        public bool  noOverlap = true;
        public float footprint = 3f;
        public float minDistanceFromGate = 0f;
        public bool  requiresSupport = false;
        public bool  checkAffordable = true;
        public string ownedGate = null;
    }
}
```

## Assets/_Modules/Village/BuildMode/PlacementGrid.cs — lines 1-361 of 361; full-file SHA256 291B8C44F4B58308CDBB95559AB8032618D0B3E703A16619C05967A864497CE2
```csharp
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;
namespace DeNelle.Village
{
    public sealed class PlacementGrid : MonoBehaviour
    {
        public static PlacementGrid Instance { get; private set; }
        [Header("Grid")]
        [Tooltip("Cell edge in metres — 3 m matches polyperfect 3×3 modular walls.")]
        public float cellSize = 3f;
        [Tooltip("Cells across X — 90 m / 3 m = 30 (±45, reaches the E/W walls + corner towers).")]
        public int gridWidth = 30;
        [Tooltip("Cells across Z — 40 (south edge fixed at -45; extends NORTH to +75, ~31 m past the north wall).")]
        public int gridHeight = 40;
        [Tooltip("World-space XZ of the grid's (0,0) cell-corner. Default centres the grid on the origin.")]
        public Vector3 origin = Vector3.zero;
        [Tooltip("Cells of border to block from placement. 0 = edge-allow (perimeter walls reach the boundary).")]
        public int edgeMargin = 0;
        private string[,] _occupied;
        private GameObject _overlay;
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            EnsureGrid();
            const float SouthEdgeZ = -45f;   // = -(original 30 cells)*3 m/2 ; the walled base's south edge
            if (origin == Vector3.zero)
                origin = new Vector3(-gridWidth * cellSize * 0.5f, 0f, SouthEdgeZ);
        }
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
        private void EnsureGrid()
        {
            if (_occupied == null ||
                _occupied.GetLength(0) != gridWidth ||
                _occupied.GetLength(1) != gridHeight)
            {
                _occupied = new string[Mathf.Max(1, gridWidth), Mathf.Max(1, gridHeight)];
            }
        }
        public Vector2Int WorldToCell(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt((worldPos.x - origin.x) / cellSize);
            int z = Mathf.FloorToInt((worldPos.z - origin.z) / cellSize);
            return new Vector2Int(x, z);
        }
        public Vector3 CellToWorld(Vector2Int cell)
        {
            float x = origin.x + (cell.x + 0.5f) * cellSize;
            float z = origin.z + (cell.y + 0.5f) * cellSize;
            return new Vector3(x, origin.y, z);
        }
        public Vector3 SnapToGrid(Vector3 worldPos)
        {
            var cell = WorldToCell(worldPos);
            var snapped = CellToWorld(cell);
            snapped.y = worldPos.y;   // keep the surface height from the placement ray
            return snapped;
        }
        public bool CanPlace(Vector2Int cell, Vector2Int footprint)
        {
            EnsureGrid();
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            int m = Mathf.Max(0, edgeMargin);
            int minX = m, minZ = m, maxX = gridWidth - m, maxZ = gridHeight - m;
            for (int dx = 0; dx < fw; dx++)
            {
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < minX || z < minZ || x >= maxX || z >= maxZ) return false;
                    if (!string.IsNullOrEmpty(_occupied[x, z])) return false;
                }
            }
            return true;
        }
        public string OccupantAt(Vector2Int cell)
        {
            EnsureGrid();
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridWidth || cell.y >= gridHeight) return null;
            string id = _occupied[cell.x, cell.y];
            return string.IsNullOrEmpty(id) ? null : id;
        }
        public bool InBounds(Vector2Int cell, Vector2Int footprint)
        {
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            int m = Mathf.Max(0, edgeMargin);
            int minX = m, minZ = m, maxX = gridWidth - m, maxZ = gridHeight - m;
            for (int dx = 0; dx < fw; dx++)
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < minX || z < minZ || x >= maxX || z >= maxZ) return false;
                }
            return true;
        }
        public void Occupy(Vector2Int cell, Vector2Int footprint, string structureId)
        {
            FlowTrace.Step("Grid", $"Occupy cell=({cell.x},{cell.y}) footprint=({footprint.x}x{footprint.y}) id='{structureId ?? "<null>"}'");
            EnsureGrid();
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            for (int dx = 0; dx < fw; dx++)
            {
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < 0 || z < 0 || x >= gridWidth || z >= gridHeight) continue;
                    _occupied[x, z] = structureId;
                }
            }
        }
        public void Free(Vector2Int cell, Vector2Int footprint)
        {
            EnsureGrid();
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            for (int dx = 0; dx < fw; dx++)
            {
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < 0 || z < 0 || x >= gridWidth || z >= gridHeight) continue;
                    _occupied[x, z] = null;
                }
            }
        }
        public void ClearAll()
        {
            FlowTrace.Step("Grid", $"ClearAll — wiping {gridWidth}x{gridHeight} occupancy");
            _occupied = new string[Mathf.Max(1, gridWidth), Mathf.Max(1, gridHeight)];
        }
        public int MetresToCells(float metres)
            => Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.01f, metres) / cellSize));
        public Vector2Int FootprintCells(float footprintMetres)
        {
            int cells = MetresToCells(footprintMetres);
            return new Vector2Int(cells, cells);
        }
        public Vector2Int FootprintCells(Vector2 footprintMetres)
            => new Vector2Int(MetresToCells(footprintMetres.x), MetresToCells(footprintMetres.y));
        public Vector2Int FootprintCells(float footprintMetres, float yawDegrees)
        {
            float yawMod = Mathf.Abs(Mathf.DeltaAngle(0f, yawDegrees)) % 90f;
            bool cardinal = yawMod < 0.01f || yawMod > 89.99f;
            if (cardinal) return FootprintCells(footprintMetres);
            float rad = yawDegrees * Mathf.Deg2Rad;
            float inflate = Mathf.Abs(Mathf.Sin(rad)) + Mathf.Abs(Mathf.Cos(rad));
            return FootprintCells(footprintMetres * inflate);
        }
        public Vector2Int FootprintCells(Vector2 footprintMetres, float yawDegrees)
        {
            float w = Mathf.Max(0.01f, footprintMetres.x);
            float d = Mathf.Max(0.01f, footprintMetres.y);
            float yawMod = Mathf.Abs(Mathf.DeltaAngle(0f, yawDegrees)) % 90f;
            bool cardinal = yawMod < 0.01f || yawMod > 89.99f;
            if (cardinal)
            {
                bool quarterTurn = Mathf.RoundToInt(Mathf.Abs(Mathf.DeltaAngle(0f, yawDegrees)) / 90f) % 2 == 1;
                return FootprintCells(quarterTurn ? new Vector2(d, w) : new Vector2(w, d));
            }
            float rad = yawDegrees * Mathf.Deg2Rad;
            float absC = Mathf.Abs(Mathf.Cos(rad));
            float absS = Mathf.Abs(Mathf.Sin(rad));
            float worldW = absC * w + absS * d;
            float worldD = absS * w + absC * d;
            return FootprintCells(new Vector2(worldW, worldD));
        }
        public void SetGridVisible(bool visible)
        {
            if (visible)
            {
                if (_overlay == null) _overlay = BuildOverlay();
                if (_overlay != null) _overlay.SetActive(true);
            }
            else if (_overlay != null)
            {
                _overlay.SetActive(false);
            }
        }
        private GameObject BuildOverlay()
        {
            var go = new GameObject("PlacementGridOverlay");
            go.transform.SetParent(gameObject.activeInHierarchy ? transform : transform.parent, false);
            float y  = origin.y + 0.05f;
            float x0 = origin.x,                         x1 = origin.x + gridWidth  * cellSize;
            float z0 = origin.z,                         z1 = origin.z + gridHeight * cellSize;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace    = true;
            lr.loop             = true;
            lr.positionCount    = 4;
            lr.SetPositions(new[]
            {
                new Vector3(x0, y, z0), new Vector3(x1, y, z0),
                new Vector3(x1, y, z1), new Vector3(x0, y, z1),
            });
            lr.widthMultiplier   = 0.35f;
            lr.numCornerVertices = 2;
            lr.alignment         = LineAlignment.TransformZ;   // ribbon lies flat on the ground
            lr.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows     = false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.25f, 0.7f, 1f, 0.9f));
            lr.sharedMaterial = mat;
            return go;
        }
    }
}
```

## Assets/_Modules/Core/Addressables/StructureContentWarmer.cs — lines 119-372 of 1591; full-file SHA256 6180005000AD5A86EAEAE06D62FEAF350890BB4A0052F89967CAEC4E25A534D4
```csharp
    public static class StructureContentWarmer
    {
        public const string System = "StructureAssets";
        public const string AddressPrefix = "Structures/";
        public const float WarmDeadlineSeconds = 45f;
        public const float MissEscalateSeconds = 10f;
        private static readonly Dictionary<string, Object> s_resident = new Dictionary<string, Object>();
        private static readonly List<AsyncOperationHandle> s_retained = new List<AsyncOperationHandle>();
        private static readonly HashSet<string> s_inFlight = new HashSet<string>();
        private static readonly Queue<string> s_piQueue = new Queue<string>();
        private static readonly HashSet<string> s_deadAddresses = new HashSet<string>();
        private static readonly Dictionary<string, string> s_failureCause = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> s_attempts = new Dictionary<string, int>();
        public const int DefaultMaxRequestAttempts = 3;
        public const int DefaultPiRequestTimeoutSeconds = 20;
        public static int MaxRequestAttempts =>
            Mathf.Max(1, RemoteTunables.Int(RemoteTunables.KeyAssetsMaxRequestAttempts));
        public static int PiRequestTimeoutSeconds =>
            Mathf.Max(1, RemoteTunables.Int(RemoteTunables.KeyPiRequestTimeoutSeconds));
        private static int ConcurrencyCap =>
            Mathf.Max(0, RemoteTunables.Int(RemoteTunables.KeyAssetsMaxConcurrentRequests));
        private static bool RemoteStructureArtDisabled =>
            WebGLPiPlatform.IsPiBrowserEnvironment &&
            RemoteTunables.Bool(RemoteTunables.KeyPiDisableRemoteStructureArt);
        private static bool PiEagerWarm =>
            RemoteTunables.Bool(RemoteTunables.KeyPiEagerStructureWarm);
        private static bool PiAwaitInitBeforeFirstLoad =>
            RemoteTunables.Bool(RemoteTunables.KeyPiAwaitInitBeforeFirstLoad);
        private static void TraceStep(int minVerbosity, string message)
        {
            if (RemoteTunables.Int(RemoteTunables.KeyTraceAssetVerbosity) < minVerbosity) return;
            FlowTrace.Step(System, message);
        }
        private static int s_activeRequests;
        private static bool s_piPrewarmStarted;
        private static bool s_piPrewarmDone;
        private static int s_webRequests;
        private static string s_lastRequestUrl;
        private static UnityEngine.Networking.UnityWebRequest s_lastRequest;
        private static readonly Dictionary<string, float> s_firstMissAt = new Dictionary<string, float>();
        private static readonly List<Action> s_settleCallbacks = new List<Action>();
        private static readonly List<Action> s_deferred = new List<Action>();
        private static readonly HashSet<string> s_registeredKeys = new HashSet<string>();
        private static Host s_host;
        private static bool s_warmStarted;
        private static int s_discovered;
        private static string s_warmPhase = "not-started";
        public static StructureContentState State { get; private set; } = StructureContentState.Cold;
        public static bool IsSettled => State == StructureContentState.Warm || State == StructureContentState.Degraded;
        public static int PendingRequests => s_inFlight.Count;
        public static int ResidentCount => s_resident.Count;
        public static int RetainedHandleCount => s_retained.Count;
        public static int DiscoveredAddressCount => s_discovered;
        public static bool IsRegisteredAddress(string address) =>
            !string.IsNullOrEmpty(address) && s_registeredKeys.Contains(address);
        public static bool TryGet<T>(string address, out T asset) where T : Object
        {
            asset = null;
            if (string.IsNullOrWhiteSpace(address)) return false;
            if (s_resident.TryGetValue(Key(typeof(T), address), out var typed) && typed != null)
            {
                asset = typed as T;
                if (asset != null) return true;
            }
            if (s_resident.TryGetValue(Key(typeof(Object), address), out var loose) && loose is T hit)
            {
                asset = hit;
                return true;
            }
            return false;
        }
```

## Assets/_Modules/Core/Addressables/StructureContentWarmer.cs — lines 603-736 of 1591; full-file SHA256 6180005000AD5A86EAEAE06D62FEAF350890BB4A0052F89967CAEC4E25A534D4
```csharp
        private static void OnRequestCompleted(string address, AsyncOperationHandle<Object> handle, bool queued)
        {
            s_inFlight.Remove(address);
            if (!TryReadStatus(handle, out var completedStatus))
            {
                s_failureCause[address] = "the AsyncOperationHandle was already INVALID when its own " +
                    "Completed callback ran — the operation had been released/recycled by another " +
                    "owner, so no status, result or exception could be read off it. " +
                    "| classified=HANDLE-INVALID";
                FlowTrace.Fail(System,
                    $"async load of '{address}' completed on an INVALID handle after " +
                    $"{SecondsWaiting(address):F1}s — nothing can be read off it, so the asset is " +
                    "treated as NOT arrived. The address keeps its retry budget " +
                    $"({AttemptsFor(address)}/{MaxRequestAttempts}) and is NOT retired, because an " +
                    "invalid handle proves nothing about the bytes. No Release is attempted: " +
                    "releasing an already-released handle is what creates the next invalid one.");
                MaybeNotifySettled();
                if (queued) ReleaseSlotAndPump();
                return;
            }
            if (completedStatus == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                s_resident[Key(typeof(Object), address)] = handle.Result;
                s_retained.Add(handle);          // ⛔ retained for the process; see header (B)
                float waited = SecondsWaiting(address);
                TraceStep(RemoteTunables.VerbosityNormal,
                    $"'{address}' arrived ASYNC after {waited:F1}s and is now RESIDENT " +
                    $"(retained={s_retained.Count}) — the next skin attempt will use it. " +
                    "It is never released, so the dungeon->town cycle cannot evict it.");
            }
            else
            {
                string cause = DescribeFailure(handle);
                s_failureCause[address] = cause;
                int attempts = AttemptsFor(address);
                bool budgetSpent = attempts >= MaxRequestAttempts;
                if (budgetSpent) s_deadAddresses.Add(address);
                FlowTrace.Fail(System,
                    $"async load of '{address}' FAILED ({completedStatus}) after {SecondsWaiting(address):F1}s " +
                    $"on attempt {attempts}/{MaxRequestAttempts}. CAUSE: {cause}. " +
                    (budgetSpent
                        ? "Retry budget SPENT — retiring this address for the rest of the launch; the " +
                          "caller keeps its baked twin / pending-art proxy."
                        : "Retry budget remains — the next skin attempt may re-request it.") +
                    " NOTE: this is a visual defect only; the game did NOT stall, which is the whole " +
                    "point of this path.");
                Guard.Try(System, $"release failed handle '{address}'", () => Addressables.Release(handle));
            }
            MaybeNotifySettled();
            if (queued) ReleaseSlotAndPump();
        }
        public static void WhenSettled(Action onSettled)
        {
            if (onSettled == null) return;
            EnsureHost();
            s_settleCallbacks.Add(onSettled);
        }
        public static void Defer(Action work)
        {
            if (work == null) return;
            EnsureHost();
            if (s_host == null)
            {
                Guard.Try(System, "run deferred work inline (no host)", work);
                return;
            }
            s_deferred.Add(work);
        }
        private static void MaybeNotifySettled()
        {
            if (s_settleCallbacks.Count == 0) return;
            if (!IsSettled) return;
            if (s_inFlight.Count > 0) return;
            if (s_piPrewarmStarted && !s_piPrewarmDone) return;
            var due = s_settleCallbacks.ToArray();
            s_settleCallbacks.Clear();
            foreach (var cb in due)
                Guard.Try(System, "settle callback", () => cb());
        }
```

## Consumer discovery windows (not complete methods)
```text
Assets/_Modules\Core\Catalog\RepoProps.cs-381-        /// </list></para>
Assets/_Modules\Core\Catalog\RepoProps.cs-382-        /// <para>NOT A CADENCE VALUE - <c>collector_farm</c> = 1.4. This multiplier fits BOUNDS, so
Assets/_Modules\Core\Catalog\RepoProps.cs-383-        /// a spindly silhouette reads SMALLER than a boxy one at the same number; the farm's windmill
Assets/_Modules\Core\Catalog\RepoProps.cs-384-        /// blades inflate its Y bounds and 1.4 is the owner felt-report compensation that puts its
Assets/_Modules\Core\Catalog\RepoProps.cs-385-        /// BODY back on the 4 m line. Never "normalize" it to 1.0. The same caveat applies to any
Assets/_Modules\Core\Catalog\RepoProps.cs-386-        /// cross-row comparison: equal heightMul does NOT mean equal apparent size.</para>
Assets/_Modules\Core\Catalog\RepoProps.cs-387-        /// <para>HEIGHT AND FOOTPRINT ARE ONE NUMBER, by design. The fit is a UNIFORM scale, so this
Assets/_Modules\Core\Catalog\RepoProps.cs-388-        /// multiplier moves the base footprint by the same factor, and
Assets/_Modules\Core\Catalog\RepoProps.cs:389:        /// StructureFactory.MeasureUprightFootprintMetres measures the real estate off the
Assets/_Modules\Core\Catalog\RepoProps.cs-390-        /// height-fitted model, never off the authored placement.footprint (that is only the
Assets/_Modules\Core\Catalog\RepoProps.cs-391-        /// prefab-missing fallback). There is no width dial and none is needed. Corollary for
Assets/_Modules\Core\Catalog\RepoProps.cs-392-        /// SAVE COMPAT: the grid claim is ceil(measured / 3 m), so RAISING a multiplier can grow a
Assets/_Modules\Core\Catalog\RepoProps.cs-393-        /// claim and make an existing saved town reload with OVERLAPPING claims - always state the
Assets/_Modules\Core\Catalog\RepoProps.cs-394-        /// before/after cell claim when you change one. (Lowering only shrinks a claim, which is
Assets/_Modules\Core\Catalog\RepoProps.cs-395-        /// overlap-safe, but for WALLS a narrower segment opens pathable GAPS in already-placed
Assets/_Modules\Core\Catalog\RepoProps.cs-396-        /// runs, which is why wall_wood/wall_stone/gate_stone were deliberately left at 1.0.)</para>
Assets/_Modules\Core\Catalog\RepoProps.cs-397-        /// JSON deserializes "heightMul" straight in. SUPERSEDES the
Assets/_Modules\Core\Catalog\RepoProps.cs-398-        /// deprecated absolute <see cref="visualHeight"/> below.
Assets/_Modules\Core\Catalog\RepoProps.cs-399-        /// </summary>
Assets/_Modules\Core\Catalog\RepoProps.cs-400-        public float heightMul = 1.0f;
Assets/_Modules\Core\Catalog\RepoProps.cs-401-
--
Assets/_Modules\Core\Catalog\RepoProps.cs-422-        /// larger than anything else"; the number behind that felt-report is 3.5x.</para>
Assets/_Modules\Core\Catalog\RepoProps.cs-423-        /// <para>NO OTHER ROW CAN BE FIXED BY DIALING HEIGHT INSTEAD. Both directions on
Assets/_Modules\Core\Catalog\RepoProps.cs-424-        /// <see cref="heightMul"/> are wrong here: lowering it shrinks the BUILDING as well as the
Assets/_Modules\Core\Catalog\RepoProps.cs-425-        /// footprint (that is literally the "shrunk farm" the owner already rejected, commit
Assets/_Modules\Core\Catalog\RepoProps.cs-426-        /// 31b41d19), and raising it makes the footprint worse. Height and footprint were ONE
Assets/_Modules\Core\Catalog\RepoProps.cs-427-        /// number by design; this is the deliberate, opt-in second number for the case where that
Assets/_Modules\Core\Catalog\RepoProps.cs-428-        /// design has no answer.</para>
Assets/_Modules\Core\Catalog\RepoProps.cs-429-        /// <para>SAVE COMPAT, same rule as <see cref="heightMul"/>: BuildModeController claims
Assets/_Modules\Core\Catalog\RepoProps.cs:430:        /// <c>ceil(measured / 3 m)</c> cells from StructureFactory.MeasureUprightFootprintXZ, which
Assets/_Modules\Core\Catalog\RepoProps.cs-431-        /// measures the fitted model, so arming this key SHRINKS a claim. Shrinking is overlap-safe
Assets/_Modules\Core\Catalog\RepoProps.cs-432-        /// (a saved town reloads with a smaller claim, never an overlapping one) — collector_farm
Assets/_Modules\Core\Catalog\RepoProps.cs-433-        /// goes 5x5 cells -&gt; 2x2. RAISING an already-armed cap can grow a claim; state the
Assets/_Modules\Core\Catalog\RepoProps.cs-434-        /// before/after cell claim when you change one, exactly as for heightMul. Never arm this on
Assets/_Modules\Core\Catalog\RepoProps.cs-435-        /// a WALL row: a narrower segment opens pathable GAPS in already-placed runs.</para>
Assets/_Modules\Core\Catalog\RepoProps.cs-436-        /// <para>Read by <c>StructureFactory.OptsFor</c> into <c>SkinOptions.MaxFootprint</c>, so it
Assets/_Modules\Core\Catalog\RepoProps.cs-437-        /// reaches Create, ReskinForLevel, the placement GHOST and MeasureUprightFootprintXZ through
Assets/_Modules\Core\Catalog\RepoProps.cs-438-        /// the one shared options builder — the ghost cannot disagree with the placed structure.
Assets/_Modules\Core\Catalog\RepoProps.cs-439-        /// JSON deserializes "maxFootprint" straight in.</para>
Assets/_Modules\Core\Catalog\RepoProps.cs-440-        /// </summary>
Assets/_Modules\Core\Catalog\RepoProps.cs-441-        public float maxFootprint = 0f;
Assets/_Modules\Core\Catalog\RepoProps.cs-442-
--
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-658-            FlowTrace.Once("Build", "desc-unauthored-" + e.id,
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-659-                $"description UNAUTHORED id={e.id} type={e.type} -- author CatalogEntry.description " +
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-660-                "in structures-catalog.json (BOTH canonical copies). No fallback prose is painted.");
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-661-            return string.Empty;
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-662-        }
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-663-
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-664-        /// <summary>
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-665-        /// WO-972 follow-through — the panel states the CLAIM, derived from the same authority
Assets/_Modules\Village\BuildMode\StructureCardVM.cs:666:        /// placement claims with (StructureFactory.MeasureClaimFootprintMetres), NOT a second
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-667-        /// measure of its own. WO-972 decoupled a Wall's grid claim from its fitted mesh
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-668-        /// (BuildModeController.IsValidPlacement + BaseLayoutLoader both moved to the claim
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-669-        /// metric), but this label was still reading the raw mesh measure — so a wall whose
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-670-        /// 3.03 m body ceils to 2 cells would have told the player "2x2 cells" while placement
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-671-        /// claimed 1x1. Identical output for every non-Wall row (the claim metric IS the
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-672-        /// measured metric there); only a Wall's label changes, and it changes to the truth.
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-673-        /// "Derive, don't hand-author" (ARCHITECTURE_PRINCIPLES §4): one claim authority,
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-674-        /// read by everyone who reports it.
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-675-        /// </summary>
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-676-        private static string FootprintFor(CatalogEntry e)
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-677-        {
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-678-            var grid = PlacementGrid.Instance;
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-679-            if (grid != null && e != null)
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-680-            {
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-681-                // WO-986: report non-square claim cells (same authority as placement).
Assets/_Modules\Village\BuildMode\StructureCardVM.cs:682:                Vector2 xz = StructureFactory.MeasureClaimFootprintXZ(e);
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-683-                if (xz.x > 0f && xz.y > 0f)
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-684-                {
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-685-                    Vector2Int f = grid.FootprintCells(xz);
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-686-                    int fx = Mathf.Max(1, f.x);
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-687-                    int fy = Mathf.Max(1, f.y);
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-688-                    return fx + "x" + fy + " cells";
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-689-                }
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-690-                FlowTrace.Once("Build", "footprint-label-unmeasured-" + (e.id ?? "<null>"),
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-691-                    $"FOOTPRINT LABEL '{e.id}': claim XZ returned ({xz.x:0.###},{xz.y:0.###})m (non-positive), " +
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-692-                    "so the info panel is showing the 1x1 DEFAULT, not a measured claim.");
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-693-            }
Assets/_Modules\Village\BuildMode\StructureCardVM.cs-694-            else
--
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-94-                if (item == null || string.IsNullOrWhiteSpace(item.id))
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-95-                { failed++; FlowTrace.Fail("Founding", $"starter layout row {i} has no id"); continue; }
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-96-                if (Count(state.BaseLayout, item.id) > OccurrenceBefore(template, i, item.id))
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-97-                { existing++; continue; }
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-98-
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-99-                CatalogEntry catalog = CatalogRegistry.Get(item.id);
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-100-                if (catalog == null) { failed++; FlowTrace.Fail("Founding", $"starter id missing: {item.id}"); continue; }
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-101-                Vector2Int footprint = grid.FootprintCells(
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs:102:                    StructureFactory.MeasureClaimFootprintXZ(catalog), item.yawQuarterTurns * 90f);
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-103-                if (!ResolveFreeCell(grid, item.Cell, footprint, out Vector2Int cell))
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-104-                { failed++; FlowTrace.Fail("Founding", $"no starter seat for {item.id} near {item.Cell}"); continue; }
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-105-
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-106-                var record = new PlacedStructureData(item.id, cell.x, cell.y, item.yawQuarterTurns, 1);
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-107-                state.BaseLayout.Add(record);
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-108-                state.MarkEverBuilt(item.id);
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-109-                if (loader == null || loader.Spawn(record, grid) == null)
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-110-                {
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-111-                    state.BaseLayout.RemoveAt(state.BaseLayout.Count - 1);
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-112-                    failed++;
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-113-                    FlowTrace.Fail("Founding", $"starter spawn failed: {item.id} at {cell}");
Assets/_Modules\Village\BuildMode\StarterSettlementCompletion.cs-114-                    continue;
--
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1765-            // cover it (PlacementGrid.FootprintCells yaw overload; ×1 at cardinals —
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1766-            // byte-identical to the legacy claim). Under-claiming at 45° was the
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1767-            // placement-lies bug the architecture review vetoed (G-F).
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1768-            // WO-972 — the claim metric, not the raw mesh measure. Identical for every row
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1769-            // EXCEPT a Wall, whose claim comes off the authored placement.footprint so a
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1770-            // 3.03 m palisade on a 3.00 m cell stays a ONE-CELL tile instead of squaring up
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1771-            // into the 2x2 block that rejected the neighbouring cell (F8 seq 2327).
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1772-            // WO-986: CoC non-square claim (x,z) + yaw AABB — not max-axis square.
Assets/_Modules\Village\BuildMode\BuildModeController.cs:1773:            footprint = _grid.FootprintCells(StructureFactory.MeasureClaimFootprintXZ(entry), ArmedYawDegrees);
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1774-            _lastRejectDetail = null;   // per-evaluation; only an Occupied gate below sets it
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1775-
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1776-            // SURFACE ROLE (data-driven, PlacementRules.mustSitOn) — a WallWalk defense MUST seat
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1777-            // on a wall TOP (defensive posture); everything else is a flat-ground placement. The
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1778-            // rules live on the catalog row (entry.repo.placement); null = the legacy Ground path.
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1779-            var rules = entry != null && entry.repo != null ? entry.repo.placement : null;
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1780-            bool needsWallWalk = rules != null && rules.mustSitOn == PlacementSurface.WallWalk;
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1781-            // Find a WallSegment under the cursor (the placement ray hits ~all layers, so it CAN
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1782-            // hit the wall collider). The structure seats on the wall's walk-top, NOT the hit point.
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1783-            WallSegment supportingWall = hit.collider != null
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1784-                ? hit.collider.GetComponentInParent<WallSegment>() : null;
Assets/_Modules\Village\BuildMode\BuildModeController.cs-1785-
--
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-525-            //     promised and a later placement could overlap the diagonal.
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-526-            // WO-972 — the CLAIM metric (identical to the measured mesh for every row except a
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-527-            // Wall, which claims off its authored placement.footprint). This MUST be the same
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-528-            // call BuildModeController.IsValidPlacement claims with, or a reload would Occupy a
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-529-            // different cell set than placement promised and the run would re-break on load.
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-530-            // The BLOCKER is unaffected in practice: AddFootprintBlocker sizes the box as
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-531-            // Clamp(rendered * 0.85, cellSize, claim), which is 3x3 m for a wall at either claim.
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-532-            // WO-986: same non-square claim as BuildModeController (save replay must match place).
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs:533:            Vector2 claimXz = StructureFactory.MeasureClaimFootprintXZ(entry);
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-534-            Vector2Int blockerFootprint = grid.FootprintCells(claimXz);
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-535-            Vector2Int footprint = grid.FootprintCells(claimXz, yawDeg);
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-536-
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-537-            // A structure that WILL MOVE must NOT carve the navmesh: the carving NavMeshObstacle on
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-538-            // the same root as the caravan's NavMeshAgent carved the mesh out from under its own
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-539-            // agent — isOnNavMesh false forever, follow dead on arrival (2026-08-15 review finding
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-540-            // #1). It KEEPS the box collider (tap-select + contact-damage collider-of-record) and
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-541-            // the visual-collider strip; only the static carve is skipped.
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-542-            //
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-543-            // ⛔ WO-1424 — THE TEST IS "WILL IT MOVE?", NOT "DOES THE COMPONENT EXIST?". The
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-544-            // caravan's follow is now split by context (owner ruling 2026-09-06: "it slow follows
Assets/_Modules\Village\BuildMode\BaseLayoutLoader.cs-545-            // as a combat attack item as defensive item it stationary"), so HealingCaravanMobility
--
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-128-            // These factory behaviors have durable condition replay. Other catalog families need their own adapter.
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-129-            if (!SupportsConstruction(entry) ||
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-130-                record.placement.level > BuildModeController.MaxLevelFor(entry))
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-131-            { reason = "This structure has no supported town construction/condition adapter."; return false; }
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-132-            var p = record.placement;
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-133-            if (p.wallMounted || Mathf.Abs(p.worldY) > .25f || Mathf.Abs(p.yawOffset) > 45f ||
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-134-                p.cellX < 0 || p.cellX >= 36 || p.cellZ < 0 || p.cellZ >= 36)
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-135-            { reason = "New construction is outside the supported ground grid."; return false; }
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs:136:            var size = StructureFactory.MeasureClaimFootprintXZ(entry);
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-137-            float angle = (p.yawSteps * 90f + p.yawOffset) * Mathf.Deg2Rad;
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-138-            float c = Mathf.Abs(Mathf.Cos(angle)), s = Mathf.Abs(Mathf.Sin(angle));
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-139-            float width = Mathf.Ceil((size.x * c + size.y * s) / 3f) * 3f;
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-140-            float depth = Mathf.Ceil((size.x * s + size.y * c) / 3f) * 3f;
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-141-            var center = new Vector3(-45f + (p.cellX + .5f) * 3f, p.worldY + 4f, -45f + (p.cellZ + .5f) * 3f);
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-142-            bounds = new Bounds(center, new Vector3(width, 8f, depth));
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-143-            float farX = Mathf.Abs(center.x) + width * .5f, farZ = Mathf.Abs(center.z) + depth * .5f;
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-144-            if (new Vector2(farX, farZ).magnitude > 54f)
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-145-            { reason = "The construction footprint extends outside the town plot."; return false; }
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-146-            return true;
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-147-        }
Assets/_Modules\Village\World\Camps\OwnedTownLayoutSnapshot.cs-148-    }
--
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-41-                !OwnedTownLayoutSnapshot.TryCreate(proposed, service.State.HeroClass, out _, out reason)) return false;
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-42-            var entry = CatalogRegistry.Get(placement.itemId);
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-43-            bool free = BuildModeController.FreeBuildAvailable(entry);
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-44-            // Quote before adding the preview body to the live tower census.
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-45-            BuildModeController.InvalidateTowerCount();
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-46-            var price = BuildModeController.EffectiveCostFor(entry);
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-47-            var grace = BuildModeController.GraceReasonFor(!service.State.HasEverBuilt(placement.itemId), !service.State.Onboarded,
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-48-                DeNelle.Core.Economy.TownBankCapacity.IsStorageContainer(entry.repo));
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs:49:            var footprint = grid.FootprintCells(StructureFactory.MeasureClaimFootprintXZ(entry), placement.yawSteps * 90f + placement.yawOffset);
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-50-            if (!grid.CanPlace(new Vector2Int(placement.cellX, placement.cellZ), footprint))
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-51-            { reason = "That construction grid position is occupied."; return false; }
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-52-            var position = grid.CellToWorld(new Vector2Int(placement.cellX, placement.cellZ));
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-53-            if (!UnityEngine.AI.NavMesh.SamplePosition(position, out var seat, 1.5f, UnityEngine.AI.NavMesh.AllAreas) ||
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-54-                Mathf.Abs(seat.position.y - position.y) > .25f || !OwnedTownNavigation.Validate(scene, out reason))
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-55-            { reason = "Choose level, walkable ground inside your town."; return false; }
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-56-            var target = BaseLayoutLoader.SpawnForLayout(placement, grid, grid.transform.parent);
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-57-            if (target == null) { reason = "The structure could not be created; nothing was charged."; return false; }
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-58-            target.gameObject.AddComponent<OwnedTownPlacedIdentity>().InstanceId = id;
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-59-            var tower = target.GetComponent<DefenseTower>();
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-60-            if (tower != null) tower.enabled = false; // preview must not fight before payment
Assets/_Modules\Village\World\Camps\OwnedTownConstructionService.cs-61-            var host = new GameObject("OwnedTownBuildValidation");
--
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-21-//
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-22-// LANDMINES AVOIDED (per the architecture plan):
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-23-//   * Does NOT touch PlacementGrid.Instance or GameState.BaseLayout — those are
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-24-//     the VILLAGE-scoped global singletons; routing camp pieces through them would
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-25-//     corrupt the village layout. We do LOCAL cell math against the outpost root.
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-26-//   * Does NOT call BaseLayoutLoader.Rebuild() (the destroy-all/respawn-all
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-27-//     pop-on-at-once bug). We own a per-piece Create loop.
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-28-//   * Footprint colliders measure the UPRIGHT mesh via
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs:29://     StructureFactory.MeasureUprightFootprintMetres (no hand-rolled bounds).
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-30-//   * cellSize matches PlacementGrid (3 m) so a camp piece reads at village scale.
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-31-//
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-32-// PlacedStructureData (DeNelle.Core) is the ONLY Core type persisted; Village->Core
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-33-// is allowed. ASCII-only strings. LogWarning, never error (pack-missing-safe).
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-34-// =============================================================================
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-35-
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-36-using System.Collections;
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-37-using System.Collections.Generic;
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-38-using UnityEngine;
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-39-using UnityEngine.AI;
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-40-using DeNelle.Core.Catalog;
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-41-using DeNelle.Core.State;
--
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-314-            // V — VERIFY the piece RENDERS. This is the owner's invisible-blocker class:
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-315-            // a carving NavMeshObstacle on a grey/unrendered foundation reads as a wall the
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-316-            // hero can't cross with NOTHING visible. Prove >=1 enabled renderer with a mesh
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-317-            // BEFORE we carve, so an unrendered carve self-reports LOUDLY (footprint logged below).
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-318-            bool renders = VerifyPieceRenders(go, rec.itemId, worldPos);
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-319-
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-320-            // Carve the navmesh per piece (no full rebake), measuring the UPRIGHT
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-321-            // footprint so the blocker matches the placed mesh.
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs:322:            float footprintM = StructureFactory.MeasureUprightFootprintMetres(entry);
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-323-            AddFootprintBlocker(go, footprintM, rec.itemId, worldPos, renders);
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-324-            return true;
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-325-        }
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-326-
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-327-        // V — does this realized piece actually RENDER? A foundation/fort piece with a
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-328-        // carving NavMeshObstacle but no visible mesh is the owner's "invisible SW blocker /
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-329-        // grey foundation" bug: the hero is blocked by nothing on screen. Returns true when
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-330-        // >=1 ENABLED Renderer carries geometry. Traces the exact counts so a capture splits
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-331-        // "no renderer" vs "renderer disabled" vs "no mesh/material".
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-332-        private static bool VerifyPieceRenders(GameObject go, string itemId, Vector3 worldPos)
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-333-        {
Assets/_Modules\Village\World\Camps\OutpostFoundationGenerator.cs-334-            if (go == null) return false;
```
