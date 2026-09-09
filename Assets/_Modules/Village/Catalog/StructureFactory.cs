// =============================================================================
// StructureFactory — the ONE creation path for catalog structures (WO-148).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// THE VISION (owner): catalog "buckets" of indexed CatalogEntry; the factory
// turns an entry (+ pose) into a live structure. There is exactly ONE creation
// method, called by THREE callers:
//   1. editor / bake-time builder  (the WO-136 castle rewrite authors via this)
//   2. runtime player placement    (TowerPlacementSystem, generalized)
//   3. persistence replay          (WO-149 load: recipe -> Create)
// Making structure-creation a repeatable process instead of bespoke geometry.
//
// RUNTIME-SAFE: this file has NO UnityEditor dependency, so the runtime + save
// paths use it directly. A thin DeNelle.Editor wrapper (StructureFactoryEditor)
// adds bake-time concerns (static flags, Undo) on top — it calls THIS, never
// duplicates the creation logic.
//
// CORE STAYS PURE: CatalogEntry/RepoProps (DeNelle.Core.Catalog) carry a STRING
// behaviorId, never a Village MonoBehaviour ref. The behaviorId -> component map
// is the switch in AttachBehavior() below — that switch IS the Core/Village
// boundary (no reflection, per CLAUDE.md §10).
// =============================================================================

using System.Collections.Generic;   // ReskinForLevel old-visual collection
using UnityEngine;
using UnityEngine.AI;          // NavMeshObstacle footprint self-report (invisible-blocker class)
using DeNelle.Core.Catalog;
using DeNelle.Core.Combat;     // DamageElement literals (WO-113 ArcaneTower default element)
using DeNelle.Core.Diagnostics; // FlowTrace / Guard — TGVRU instrumentation (§12)
using DeNelle.Cosmetics;        // CosmeticApplier — the village-category cosmetic seam (see AttachCosmeticSeam)

namespace DeNelle.Village
{
    /// <summary>
    /// Instantiates <see cref="CatalogEntry"/> defs into live village structures.
    /// Runtime-safe (no editor APIs); shared by builder, player placement, and
    /// save-replay. Every step null-guards and logs rather than throwing, so a
    /// missing prefab / unknown behaviorId degrades gracefully (pack-missing-safe).
    /// </summary>
    public static class StructureFactory
    {
        /// <summary>
        /// WO-764 (Y-height normalization, centralized) - the ONE global base ceiling (metres)
        /// every structure is fit-to-height against:
        /// <c>EffectiveHeight = YHeightVariable * repo.heightMul</c>. Every building uses the
        /// default 1.0 multiplier so the whole script-built town reads at ONE uniform height.
        /// THE CADENCE (owner ruling 2026-08-05, "all of the other structures stay within that
        /// cadence... relatively the same size... all scaled to the same point") is ONE FAMILY,
        /// NOT ONE NUMBER: 1.0 building base (4.0 m) / 1.2 TOWER ANCHOR (4.8 m, measured at 49.9%
        /// of a house diameter) / 0.75 siege engines (3.0 m) / 1.25 for the ONE landmark, the
        /// Cathedral of Magic / 0.35 decoration. The authority for the per-group rationale is the
        /// catalog's top-level <c>_heightCadence</c> key; RepoProps.heightMul carries the summary
        /// plus the two standing caveats (collector_farm's 1.4 is a BOUNDS compensation, not a
        /// cadence value; walls are deliberately unauthored for save compat). Change THIS ONE
        /// number and the entire town re-scales together (the owner-locked model). Was the WO-751
        /// per-item-absolute DefaultVisualHeight (also 4 m); the old absolute overrides became
        /// per-item <c>heightMul</c> multipliers.
        /// </summary>
        public const float YHeightVariable = 4f;

        /// <summary>
        /// WO-764 - the fit-to-HEIGHT target (metres) for <paramref name="entry"/>:
        /// <c>YHeightVariable * repo.heightMul</c> (multiplier default 1.0, guarded &gt; 0). Single
        /// source of truth so Create / ReskinForLevel / footprint-measure all fit to the SAME height
        /// (no size jump between placement, upgrade, and ghost/footprint). Only the multiplier changes
        /// WHICH height feeds the fit - the bounds+height scale math in VisualFactory.Fit is untouched.
        /// <paramref name="isOverride"/> is true when the item's multiplier != 1.0 (a deliberate class
        /// exception like a tower), false when it inherits the uniform base.
        /// </summary>
        private static float EffectiveVisualHeight(CatalogEntry entry, out bool isOverride)
        {
            float mult = entry != null && entry.repo != null ? entry.repo.heightMul : 1f;
            if (mult <= 0f) mult = 1f;   // guard a zero/unset/negative authored multiplier -> uniform base
            isOverride = !Mathf.Approximately(mult, 1f);
            return YHeightVariable * mult;
        }

        /// <summary>
        /// Create one structure from <paramref name="entry"/> at <paramref name="pose"/>.
        /// Resolves the visual (via <see cref="VisualFactory"/>), attaches the
        /// behaviour component named by <c>entry.repo.behaviorId</c>, and parents
        /// it under <paramref name="parent"/>. Returns the root GameObject, or null
        /// when the entry is unusable (logged).
        /// </summary>
        public static GameObject Create(CatalogEntry entry, Pose pose, Transform parent)
        {
            using var _ = FlowTrace.Enter("Structure", $"Create id='{entry?.id ?? "<null>"}'");

            if (entry == null)
            {
                // U: was Debug.LogWarning — roll up via FlowTrace so a null-entry create self-reports.
                FlowTrace.Fail("Structure", "Create called with a null entry — skipped (returning null; caller falls back).");
                return null;
            }

            // Composites delegate to CreateGroup so a single id can build a bundle.
            if (entry.kind == EntryKind.Composite)
            {
                FlowTrace.Step("Structure", $"'{entry.id}' is Composite — delegating to CreateGroup.");
                return CreateGroup(entry, pose, parent);
            }

            // Root host — owns the world transform; the visual is skinned under it.
            var root = new GameObject(string.IsNullOrEmpty(entry.displayName)
                ? $"Structure-{entry.id}" : entry.displayName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            FlowTrace.Step("Structure", $"'{entry.id}' root '{root.name}' created at {pose.position}.");

            // LOOK — skin the polyperfect/Resources visual under the root.
            // DEF-208 + WO-751 (Y-height normalization): EVERY structure fits to HEIGHT now.
            // A tall structure (tower) must fit to HEIGHT, not to its largest bounds dim
            // (fit-to-largest scaled a tower so its tallest axis = footprint ~2.5 m -> a
            // squashed/wrong-scaled tower). WO-764: every structure fits to YHeightVariable *
            // repo.heightMul - buildings inherit the uniform 1.0 base while a class that must read
            // taller or shorter authors its multiplier off the 2026-08-05 cadence (tower 1.2 /
            // siege 0.75 / landmark 1.25 / decoration 0.35), so a tall structure stands tall while
            // the town stays one family.
            if (!string.IsNullOrEmpty(entry.visualPrefabPath))
            {
                float targetHeight = EffectiveVisualHeight(entry, out bool heightOverride);
                // WO-1142: true once the pending-art proxy + its ONE WhenSettled retry are armed,
                // so the render-verify degrade below never arms a rival second subscription.
                bool pendingArtArmed = false;

                // WO-928: build the opts through the ONE shared helper instead of assembling them
                // inline. The inline copy here had drifted from ReskinForLevel's OptsFor - it carried
                // the height and nothing else - so any per-row skin policy added to OptsFor would have
                // reached tier models and MISSED the base visual, i.e. an L1 tower and its L3 tower
                // would obey different rules. That is precisely the class of split this defect is.
                var opts = OptsFor(entry);   // fit-to-height + per-row rotation policy + trace id
                FlowTrace.Step("Structure", $"'{entry.id}' fit-to-height target={targetHeight:0.##}m " +
                    $"(source={(heightOverride ? "override" : "default")}), " +
                    $"preservePrefabRotation={opts.PreservePrefabRotation}.");

                // G: Guard the Skin — a throwing VisualFactory (bad prefab path / Addressables
                // hiccup) logs + rolls up instead of aborting the whole create. null => meshless.
                GameObject visual = Guard.Try("Structure",
                    $"skin '{entry.id}' visual '{entry.visualPrefabPath}'",
                    () => VisualFactory.Skin(root.transform, entry.visualPrefabPath, opts),
                    fallback: null);

                if (visual == null)
                {
                    // WO-1142: an Addressables miss here usually means "not resident THIS FRAME".
                    // Returning null made BaseLayoutLoader drop a paid building, its footprint and
                    // its behaviour for the entire session. Keep a loud, renderer-bearing proxy so
                    // the gameplay root survives, then replace only that proxy after the request
                    // VisualFactory/StructureAssetLoader just issued has settled.
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
                    // Euler is applied PRE-fit via OptsFor → LocalRotation (GROK_BRIEF 2026-08-19).
                    // Re-multiplying it here would tip twice. Only offset + non-uniform scale remain
                    // post-Skin; reseat when those move the bounds base off the root y.
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

                // WO-719 (arcane spire renders WHITE): route the forced albedo THROUGH the Tripo
                // fixer VisualFactory.Skin just added, BEFORE its next-frame Start. The fixer bakes
                // the texture into its single-pass URP/Lit rebuild and ASSIGNS it to the renderer, so
                // it STICKS in the built player (MagentaGuard durability) and is RACE-FREE. This
                // replaces the old post-skin ApplyForcedTexture below as the primary path: that
                // mutated per-instance materials in the SAME frame, which the fixer's rebuild then
                // replaced a frame later (and it only carried a source map, never overrode a broken/
                // white extracted one) - the proven reason the colored albedo never reached the spire.
                if (!string.IsNullOrEmpty(entry.visualTexturePath))
                {
                    var fixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                    fixer?.SetForcedTexture(entry.visualTexturePath);
                }

                // WHITE-STRUCTURE FALLBACK (ballista fix 2026-07-19): heroes/pets pass a species tint
                // to their fixer, but structures never did — so a model whose albedo didn't survive
                // the build (gitignored .fbm, e.g. Structures/Ballista / WizardTower_1) rebuilt to
                // SOLID WHITE. Register a neutral stone MISS-tint on the fixer VisualFactory.Skin just
                // added: it is applied ONLY to slots that resolve no texture at all, so a textured
                // structure is byte-unchanged and a textureless one degrades to flat stone (never
                // bright white). Set before the fixer's next-frame Start (same as SetForcedTexture).
                {
                    var missFixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                    missFixer?.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
                }

                // WO-707 texPath escape hatch (retained as a belt-and-suspenders secondary; the
                // WO-719 fixer route above is the durable primary). Force a Resources texture onto
                // the skinned materials when the model's embedded material lost its map link
                // (renders colorless — the arcane tower FBX). G: guarded — a missing texture logs +
                // leaves the untinted mesh, never aborts the create.
                if (!string.IsNullOrEmpty(entry.visualTexturePath))
                    Guard.Try("Structure", $"force texture '{entry.id}'",
                        () => ApplyForcedTexture(visual, entry.visualTexturePath, entry.id));
                else
                    // WO-1327 §12: a rebind that never RUNS used to log NOTHING — neither its
                    // success line nor its failure line — so "the forced texture is broken" and
                    // "there is no forced texture authored" were indistinguishable in a capture.
                    // That ambiguity cost a full session on the Cathedral of Magic. Say it.
                    FlowTrace.Once("Structure", "no-texpath-" + entry.id,
                        $"'{entry.id}': NO repo visualTexturePath authored — the forced-albedo " +
                        $"rebind is SKIPPED BY DESIGN and '{entry.visualPrefabPath}' keeps its own " +
                        "embedded materials. A Tripo FBX with no texPath renders WHITE in a player " +
                        "build (its only Color map lives in a .fbm folder that does not ship).");

                // V + R: PROVE the skinned structure can render (>=1 enabled renderer with a
                // sharedMesh) — the grey-foundation / floating-untextured class self-reports here.
                // A NavMeshObstacle/collider footprint is logged so an "invisible blocker"
                // announces its size.
                //
                // WO-1142 residual: this used to Fail + DestroyRoot + return null, which made it the
                // LAST path in the game that destroyed a PAID building (BaseLayoutLoader then printed
                // "structure NOT built (one building lost…)"). A render-BROKEN visual must not be
                // worth less than a not-yet-RESIDENT one, so it now degrades to the SAME pending-art
                // proxy + WhenSettled retry the residency miss above uses — one mechanism, not two.
                // The wording below is deliberately distinct from the residency Fail so the two
                // causes stay tellable apart in a log.
                if (!VerifyStructureRenders(root, entry.id))
                {
                    FlowTrace.Fail("Structure", $"'{entry.id}': skinned visual " +
                        $"'{entry.visualPrefabPath}' FAILED RENDER VERIFICATION (no enabled renderer " +
                        "with a mesh) — discarding that broken body, retaining a visible pending-art " +
                        "proxy and arming one WhenSettled retry (the building, footprint and behaviour " +
                        "remain present; nothing paid-for is destroyed).");
                    // When the residency miss above already installed a proxy and armed the ONE
                    // retry, keep them: arming a second WhenSettled would be a rival authority.
                    if (!pendingArtArmed)
                    {
                        if (visual != null) DestroyRoot(visual);
                        GameObject renderProxy = BuildPendingArtProxy(root, entry);
                        DeNelle.Core.StructureContentWarmer.WhenSettled(() =>
                            TryReplacePendingArt(root, entry, renderProxy));
                    }
                }
            }

            // COSMETICS — install the village-category cosmetic seam on the finished body.
            AttachCosmeticSeam(root, entry);

            // BEHAVIOR — resolve the Core string id to a real Village component.
            AttachBehavior(root, entry);

            FlowTrace.Step("Structure", $"'{entry.id}' created OK -> '{root.name}'.");
            return root;
        }

        /// <summary>
        /// Visible fail-loud body for the short interval between save replay and Addressables
        /// residency. It intentionally owns no gameplay collider: BaseLayoutLoader installs the
        /// catalog-derived collider/obstacle on the root after Create returns.
        /// </summary>
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

        /// <summary>One-shot residency callback for the Create/save-replay caller.</summary>
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

        // =====================================================================
        //  Village-category cosmetics seam
        // =====================================================================
        /// <summary>
        /// Installs the ONE <see cref="CosmeticApplier"/> on a finished structure root, bound to the
        /// <c>village</c> cosmetic category.
        /// <para>⛔ ONE APPEARANCE OWNER. This method does NOT build, replace, or own the structure's
        /// body — <see cref="VisualFactory.Skin"/> above already did that, and stays the sole body
        /// owner. The applier only RE-DECORATES the renderers it finds under this root (material swap /
        /// mesh override / preview tint) and re-drives itself when the player's equipped cosmetic
        /// changes. It is deliberately attached to the ROOT, not to the skinned child: the child is
        /// destroyed and rebuilt by <see cref="ReskinForLevel"/> on every tier upgrade, so an applier
        /// living there would be destroyed with it. Root-hosted, it survives the reskin and is
        /// re-driven by the RefreshOn call at the end of that method.</para>
        /// <para>Four of the twelve shipped cosmetics are village-category and had NO consumer at all
        /// before this seam: equipping one changed nothing anywhere in the game. Note that
        /// <c>village</c> cosmetics carry several distinct <c>appliesTo</c> members
        /// (building-palette / wall-tier-2 / heart-lantern / banners / emblem) and only the first two
        /// name a placed structure. heart-lantern (the Heart of Elarion), banners and emblem have
        /// their OWN body owners elsewhere and are deliberately NOT claimed here — binding them to
        /// building-palette would make an unrelated cosmetic silently repaint every building.</para>
        /// <para>With no cosmetic art staged in the tree, an equipped village cosmetic lands on the
        /// applier's preview-tint fallback and logs a Warn naming the exact <c>Resources/...</c> path
        /// that would replace it. That is INTENDED — it is the authoring to-do surfacing itself, not
        /// a defect to patch by inventing art.</para>
        /// </summary>
        private static void AttachCosmeticSeam(GameObject root, CatalogEntry entry)
        {
            if (root == null || entry == null) return;

            string appliesTo = VillageCosmeticMemberFor(entry.id);
            if (string.IsNullOrEmpty(appliesTo))
            {
                FlowTrace.Once("Cosmetics", $"structure-no-cosmetic-member-{entry.id}",
                    $"'{entry.id}' maps to no village cosmetic member — no applier installed (by design).");
                return;
            }

            // G: a throwing applier install must never abort a structure create — the building is
            // gameplay, the cosmetic is decoration. Guard logs via FlowTrace.Fail and moves on.
            Guard.Try("Cosmetics", $"attach village cosmetic seam to '{entry.id}'", () =>
            {
                var applier = CosmeticApplier.Attach(root, "village", appliesTo);
                FlowTrace.Step("Cosmetics",
                    $"'{entry.id}' -> village cosmetic seam installed (appliesTo='{appliesTo}', " +
                    $"equipped='{applier?.EquippedCosmeticId ?? "<none>"}', " +
                    $"decoratedRenderers={applier?.DecoratedRendererCount ?? 0}, " +
                    $"previewTint={applier?.UsingPreviewTint ?? false}).");
            });
        }

        /// <summary>
        /// The <c>village</c>-category cosmetic MEMBER (CosmeticDef.AppliesTo) a placed structure id
        /// belongs to, or null when the structure wears no village cosmetic.
        /// <para>Members are read off the shipped cosmetics catalog: walls are their own member
        /// (<c>wall-tier-2</c>); every other trade/production/defence building is part of the
        /// <c>building-palette</c>. Ids are LIVE SAVE KEYS — this matches on them, never renames them.</para>
        /// </summary>
        /// <remarks>PUBLIC so CosmeticApplyRegression's [village] rule asserts against the factory's
        /// OWN mapping rather than a copy of it — a duplicated table is the drift CLAUDE.md §2/§5/§16
        /// catalogues.</remarks>
        public static string VillageCosmeticMemberFor(string structureId)
        {
            if (string.IsNullOrEmpty(structureId)) return null;
            string id = structureId.ToLowerInvariant();

            // Walls carry their own cosmetic member in cosmetics.json ("village-walls-stoneweave").
            if (id.StartsWith("wall_") || id.StartsWith("gate_")) return "wall-tier-2";

            // Pure decoration/terrain ids are not part of the building palette — a palette reskin
            // repainting a fountain or a rock reads as a bug, not a cosmetic.
            if (id.StartsWith("deco_") || id.StartsWith("fountain_") || id.StartsWith("rock_") ||
                id.StartsWith("tree_")) return null;

            // Everything else the player PLACES is a building: the building-palette member.
            return "building-palette";
        }

        /// <summary>
        /// WO-707 — force a Resources texture onto every material of the skinned visual.
        /// Ported from HubStructureVisualInjector.TrySwap's texPath escape hatch (the
        /// owner-dialed swap table): a Tripo FBX whose embedded material lost its
        /// _MainTex/_BaseMap link renders colorless; this rebinds the authored texture.
        /// Play mode uses instance materials (safe to retint a one-off building — same
        /// as the proven swap path); edit mode uses sharedMaterials to avoid the
        /// edit-time material-instantiation leak.
        /// </summary>
        private static void ApplyForcedTexture(GameObject visual, string texPath, string id)
        {
            if (visual == null || string.IsNullOrEmpty(texPath)) return;
            // Addressables-first via StructureAssetLoader — upgradeTexturePath keys live in the same
            // "Structures/..." address space as the prefabs, so they migrate together or the tier
            // reskin would keep a Resources dependency alive and defeat the whole migration.
            var tex = DeNelle.Core.StructureAssetLoader.LoadStructureAsset<Texture2D>(texPath);
            if (tex == null)
            {
                FlowTrace.Warn("Structure",
                    // WO-1327: wording corrected. This said "not found in Resources", which is a
                    // lie since the CDN migration deleted Assets/Resources/Structures — the load
                    // is resident-cache-first via StructureAssetLoader and Resources is only a
                    // residual tier. A reader who believed the old line went looking in a folder
                    // that does not exist.
                    $"'{id}': visualTexturePath '{texPath}' did NOT resolve via StructureAssetLoader " +
                    $"(registered={DeNelle.Core.StructureContentWarmer.IsRegisteredAddress(texPath)}, " +
                    $"warmerState={DeNelle.Core.StructureContentWarmer.State}) — leaving materials " +
                    "as-is; a Tripo FBX will render WHITE.");
                return;
            }
            int touched = 0, bound = 0, propless = 0;
            string proplessShader = null;
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = Application.isPlaying ? r.materials : r.sharedMaterials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    // WO-1327: URP/Lit declares _BaseMap, the built-in path declares _MainTex.
                    // Setting only one is SILENTLY REJECTED by the other shader family (the same
                    // mismatch class fixed in UVscroll.cs), and "touched" used to count a material
                    // that took NEITHER — so the success line could report N materials while
                    // binding zero textures. Count what actually bound.
                    bool hit = false;
                    if (m.HasProperty("_BaseMap"))   { m.SetTexture("_BaseMap", tex); hit = true; }
                    if (m.HasProperty("_MainTex"))   { m.SetTexture("_MainTex", tex); hit = true; }
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                    if (m.HasProperty("_Color"))     m.SetColor("_Color", Color.white);
                    touched++;
                    if (hit) bound++;
                    else
                    {
                        propless++;
                        if (proplessShader == null)
                            proplessShader = m.shader != null ? m.shader.name : "(null shader)";
                    }
                }
            }
            if (bound > 0)
                FlowTrace.Step("Structure",
                    $"'{id}': forced texture '{texPath}' onto {bound}/{touched} material(s) " +
                    $"(WO-707 texPath port)" +
                    (propless > 0
                        ? $"; {propless} slot(s) declared neither _BaseMap nor _MainTex " +
                          $"(first shader '{proplessShader}')."
                        : "."));
            else
                FlowTrace.Fail("Structure",
                    $"'{id}': forced texture '{texPath}' RESOLVED but bound onto ZERO of {touched} " +
                    $"material slot(s) — none declares _BaseMap or _MainTex (first shader " +
                    $"'{proplessShader ?? "(no materials at all)"}'). The structure will render " +
                    "colorless. This is a SHADER PROPERTY mismatch, not a missing asset.");
        }

        /// <summary>The Resources visual path a structure shows at <paramref name="level"/>:
        /// repo.upgradeVisualPath[level-2] when authored (L2 = [0], L3 = [1] — the RepoProps
        /// contract), else the base visualPrefabPath. Data was write-only until the owner F8
        /// 2026-07-06 "on tower upgrade it's just making bigger, need to replace with new
        /// structure" — this + ReskinForLevel make the ladder real.</summary>
        public static string VisualPathForLevel(CatalogEntry entry, int level)
        {
            if (entry == null) return null;
            var ladder = entry.repo != null ? entry.repo.upgradeVisualPath : null;
            if (level >= 2 && ladder != null && ladder.Length >= level - 1
                && !string.IsNullOrEmpty(ladder[level - 2]))
                return ladder[level - 2];
            return entry.visualPrefabPath;
        }

        /// <summary>The FORCED albedo (Resources path) a structure wears at <paramref name="level"/>:
        /// repo.upgradeTexturePath[level-2] when authored (L2 = [0], L3 = [1] - same contract as
        /// upgradeVisualPath), else the base visualTexturePath (which itself may be null). WO-719
        /// upgrade-tier fix: an upgraded spire (ArcaneSpire_2/_3) is a Tripo FBX whose only Color map
        /// is buried in its .fbm folder — it does NOT survive a player build (renders WHITE, exactly
        /// like L1 did before its fix). ReskinForLevel routes this flat Resources albedo through the
        /// FRESH TripoMaterialFixer the tier reskin adds, so the upgraded model keeps its colour.</summary>
        public static string TexturePathForLevel(CatalogEntry entry, int level)
        {
            if (entry == null) return null;
            var ladder = entry.repo != null ? entry.repo.upgradeTexturePath : null;
            if (level >= 2 && ladder != null && ladder.Length >= level - 1
                && !string.IsNullOrEmpty(ladder[level - 2]))
                return ladder[level - 2];
            return entry.visualTexturePath;
        }

        /// <summary>
        /// Swap the skinned visual to the per-tier model for <paramref name="level"/>.
        /// Returns TRUE only when a real per-tier model (different from the base path) is
        /// now worn — the caller then SKIPS the legacy StructureTierVisual scale-step (the
        /// model IS the progression). New visual is skinned BEFORE the old is destroyed, so
        /// a bad path keeps the old look (never a blank structure). No-op-true when the
        /// tier model is already worn (idempotent across re-loads).
        /// </summary>
        public static bool ReskinForLevel(GameObject root, CatalogEntry entry, int level)
        {
            if (root == null || entry == null) return false;

            // TOWER-VFX TIER ESCALATION (owner felt-test 2026-07-17: "more/better VFX at higher tower
            // levels"). Drive the idle aura + firing bursts off the NEW level so an upgraded tower's
            // VFX visibly escalate. Done FIRST — before the tier-model early-returns below — so the
            // escalation fires on EVERY upgrade, even when a structure has no authored tier model
            // (the level still changed). Runs on the live BuildMode upgrade AND on save/reload
            // (BaseLayoutLoader), which both funnel through this one path. Null-safe / colorblind-safe
            // (§7: size/motion, not hue). We only READ the level here; the build-mode upgrade path
            // (BuildModeController) is untouched.
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

            // Already wearing it? (Skin instantiates '<prefab>(Clone)' under the root.)
            string stem = path.Substring(path.LastIndexOf('/') + 1);
            for (int i = 0; i < root.transform.childCount; i++)
                if (root.transform.GetChild(i).name.StartsWith(stem)) return true;

            // Collect the current visual children BEFORE adding the new one (renderer-bearing
            // direct children; leaves non-visual children like the WO-612 BuildCountdown alone).
            var old = new List<GameObject>();
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var c = root.transform.GetChild(i);
                if (c.GetComponentInChildren<Renderer>(true) != null) old.Add(c.gameObject);
            }

            GameObject visual = Guard.Try("Structure",
                $"reskin '{entry.id}' L{level} visual '{path}'",
                // Base Euler remains excluded; only an explicitly-authored tier Euler may apply.
                () => VisualFactory.Skin(root.transform, path, OptsForUpgradeLevel(entry, level)),
                fallback: null);
            if (visual == null)
            {
                FlowTrace.Fail("Structure", $"'{entry.id}': tier-{level} visual '{path}' failed to " +
                    "skin — keeping the previous visual (structure never blanks).");
                return false;
            }

            // WO-719 (upgrade-tier albedo): the tier reskin's VisualFactory.Skin just added a FRESH
            // TripoMaterialFixer to this new model with NO forced texture — so an upgraded Tripo spire
            // (ArcaneSpire_2/_3), whose only Color map is buried in its .fbm folder (does NOT survive a
            // player build), would render WHITE exactly like L1 did before its fix. Route the per-tier
            // flat Resources albedo THROUGH that fixer BEFORE its next-frame Start (identical to the L1
            // path in Create) so the forced map is baked into the fixer's single-pass URP/Lit rebuild
            // and STICKS in the build. Covers BOTH the live upgrade (BuildModeController) AND save/
            // reload (BaseLayoutLoader) — both funnel through this one reskin path.
            string texPath = TexturePathForLevel(entry, level);
            if (!string.IsNullOrEmpty(texPath))
            {
                var fixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                fixer?.SetForcedTexture(texPath);
            }

            // WHITE-STRUCTURE FALLBACK (ballista fix 2026-07-19): mirror Create — a tier model whose
            // albedo didn't survive the build would otherwise reskin to SOLID WHITE. Register the same
            // neutral stone MISS-tint (texture-miss-only, so textured tiers are byte-unchanged).
            {
                var missFixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                missFixer?.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
            }

            // Base orientation remains isolated from upgrade art. A measured tier correction is
            // authored independently in repo.upgradeOrientationEuler and has already been applied
            // by OptsForUpgradeLevel before the model is fitted.

            foreach (var g in old) Object.Destroy(g);

            // COSMETICS re-drive: the renderer set the root's CosmeticApplier decorated a moment ago
            // was just DESTROYED and replaced by the tier model. Without this the upgraded building
            // silently reverts to its bare tier look while the player still has a cosmetic equipped.
            // RefreshOn is a no-op on a root with no applier, and the applier re-collects renderers
            // itself — no second visual path, no re-skin from here.
            CosmeticApplier.RefreshOn(root);

            FlowTrace.Step("Structure", $"'{entry.id}' reskinned to tier-{level} model '{stem}' " +
                $"(replaced {old.Count} old visual(s)); village cosmetic seam re-driven.");
            return true;
        }

        /// <summary>Production skin options for an authored upgrade model.</summary>
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

        /// <summary>
        /// WO-764 skin options for an entry - fit-to-HEIGHT always:
        /// YHeightVariable * repo.heightMul (uniform base × the per-item multiplier). Shared by
        /// Create + ReskinForLevel + MeasureUprightFootprintMetres so a tier reskin and the
        /// placement ghost fit to the SAME height AND wear the SAME rotation policy as the base
        /// skin (no size jump on upgrade, no ghost/placed disagreement).
        /// <para>
        /// WO-928 - THE ROTATION POLICY IS READ HERE, PER ROW, AND NOWHERE ELSE. VisualFactory
        /// normally flattens an instantiated model's root to identity (DEF-232), which is correct
        /// for the structure class as a whole: most Tripo building FBXs instantiate at euler
        /// (270,0,0) and that 270 is exactly what the reset exists to CANCEL. A HANDFUL of rows are
        /// the opposite case - the model's own 270 IS its upright correction, and flattening it
        /// ships the structure on its side AND then lets VisualFactory.Fit measure the SHORT axis
        /// to reach the height target, so it is oversized as well (orientation and "the footprint is
        /// huge" are ONE defect). Those rows author <c>repo.preservePrefabRotation: true</c>.
        /// </para>
        /// <para>
        /// SETTING IT ON <c>SkinOptions.Structure</c> INSTEAD WAS TRIED AND REVERTED ON 2026-08-08
        /// (it laid the whole town down - the ⛔ block on that factory has the captured trace). The
        /// row is the correct granularity because the correction is a property of one PIECE OF ART,
        /// not of the structure class. Default false =&gt; every row that does not author the key is
        /// byte-identical in behaviour to before this landed.
        /// </para>
        /// <para>
        /// PUBLIC as of WO-928 so the WYSIWYG paths can stop re-deriving it. <c>GhostPreview</c>
        /// (BuildMode/GhostPreview.cs) still hand-rolls FitHeight from
        /// <c>YHeightVariable * repo.heightMul</c> under a comment insisting it must match Create
        /// "EXACTLY" - a promise a second copy cannot keep, and it does NOT carry the rotation policy,
        /// so the ghost of a preserve-row renders lying down while the placed structure stands. That
        /// caller should become <c>opts = StructureFactory.OptsFor(entry)</c>; it is a one-line change
        /// and it retires the third copy of this formula.
        /// </para>
        /// </summary>
        /// <param name="applyManualEuler">
        /// True for Create / ghost (euler → LocalRotation BEFORE Fit). False for
        /// <see cref="ReskinForLevel"/> — tier models rely on prefab-native orientation;
        /// re-applying the base euler tips upright L2/L3 models (F8-2 2026-07-07).
        /// </param>
        public static SkinOptions OptsFor(CatalogEntry entry, bool applyManualEuler = true)
        {
            var o = SkinOptions.Structure(0f);   // clear FitLargest
            o.FitHeight = EffectiveVisualHeight(entry, out _);

            // Per-row rotation policy (WO-928). Null-guarded the same way EffectiveVisualHeight
            // guards repo: a sparse / missing repo means "no opt-in", i.e. the known-good default.
            o.PreservePrefabRotation = entry != null && entry.repo != null && entry.repo.preservePrefabRotation;

            // FOOTPRINT CAP (owner F8 2026-08-20). One line, and it is deliberately HERE rather
            // than at each call site: Create, ReskinForLevel, the placement GHOST and
            // MeasureUprightFootprintXZ all route through OptsFor, so the grid claim shrinks with
            // the visual and the ghost can never disagree with the placed structure.
            o.MaxFootprint = entry != null && entry.repo != null ? entry.repo.maxFootprint : 0f;

            // GROK_BRIEF 2026-08-19 / owner upright: a manual catalog euler MUST feed
            // SkinOptions.LocalRotation so VisualFactory applies it BEFORE Fit. Post-fit
            // euler measured the lying-down short axis (~6.3 m storefronts). Pre-fit yields 4.00 m.
            if (applyManualEuler && entry != null && entry.orientation != null && entry.orientation.manual)
            {
                Vector3 e = entry.orientation.Euler;
                if (e.sqrMagnitude > 0.0001f)
                    o.LocalRotation = Quaternion.Euler(e);
            }

            // Instrumentation (§12): stamp the ROW id into every Xform line VisualFactory emits, so
            // the preserve-vs-identity branch is attributable to a row by grep. The model name alone
            // cannot do it - GenericContainer is the model for lumberyard AND foundry AND silo.
            o.TraceId = entry != null ? entry.id : null;

            // WO-1157 (§12, owner F8 2026-08-24 "the ballista builds on its side"): the DECISION is
            // logged here, at the one place it is made, rather than being reconstructed downstream
            // from the Xform lines. The three orientation channels (manual euler / preserve / the
            // identity reset) are mutually exclusive and only ONE of them runs for a given row —
            // so the row must SAY which, or every future orientation triage starts by re-deriving
            // it from the catalog by hand. That re-derivation is what produced two wrong theories
            // on this same defect. PERMANENT instrumentation; flag it off, never strip it.
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

        // V (render-verify) + footprint self-report. Mirrors HeroArmorVisual.VerifyArmorRendersNow:
        // the created structure MUST carry >=1 ENABLED Renderer with a non-null sharedMesh, else it
        // reads as the grey-foundation / invisible-untextured class the owner flagged. Traces the exact
        // counts so a capture splits "no renderer" vs "renderer but no mesh" vs "all disabled" with zero
        // guessing. ALSO logs the footprint of any NavMeshObstacle/Collider so an INVISIBLE BLOCKER
        // (collider with no visible mesh, or a too-large obstacle) self-reports its size. Returns false
        // => caller rolls back (destroy + null) so callers fall back rather than seat a broken structure.
        private static bool VerifyStructureRenders(GameObject root, string id)
        {
            if (root == null)
            {
                FlowTrace.Fail("Structure", $"VerifyStructureRenders: root for '{id}' is null.");
                return false;
            }

            int total = 0, enabledRend = 0, withMesh = 0;
            Guard.Try("Structure", $"render-verify '{id}'", () =>
            {
                var rends = root.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends)
                {
                    if (r == null) continue;
                    total++;
                    if (r.enabled) enabledRend++;
                    // sharedMesh lives on MeshRenderer's sibling MeshFilter, or on a
                    // SkinnedMeshRenderer directly — check both so any renderer kind counts.
                    if (RendererHasMesh(r)) withMesh++;
                }
            });

            // Footprint self-report (invisible-blocker class). Any blocking volume is logged so a
            // collider/obstacle that occupies space WITHOUT a visible mesh announces itself.
            Guard.Try("Structure", $"footprint-probe '{id}'", () =>
            {
                var obstacle = root.GetComponentInChildren<NavMeshObstacle>(true);
                var col = root.GetComponentInChildren<Collider>(true);
                if (obstacle != null)
                    FlowTrace.Step("Structure",
                        $"'{id}' carries NavMeshObstacle (carving={obstacle.carving}, size={obstacle.size}, center={obstacle.center}) — blocking footprint.");
                if (col != null)
                {
                    Bounds cb = col.bounds;
                    FlowTrace.Step("Structure",
                        $"'{id}' carries Collider '{col.GetType().Name}' bounds size={cb.size} center={cb.center} — physical footprint.");
                    if (withMesh == 0)
                        FlowTrace.Warn("Structure",
                            $"'{id}' has a Collider/obstacle but NO visible mesh — this is the INVISIBLE-BLOCKER shape (footprint {cb.size}).");
                }
            });

            bool renders = enabledRend > 0 && withMesh > 0;
            FlowTrace.Step("Structure",
                $"VerifyStructureRenders '{id}' on '{root.name}': renderers total={total} enabled={enabledRend} withMesh={withMesh} => renders={renders}.");

            if (!renders)
            {
                FlowTrace.Fail("Structure",
                    $"VerifyStructureRenders FAILED '{id}' on '{root.name}': renders={renders} " +
                    $"(total={total}, enabled={enabledRend}, withMesh={withMesh}) — grey/invisible/untextured structure; " +
                    "destroying + returning null so the caller falls back (no silent broken structure).");
                return false;
            }
            return true;
        }

        /// <summary>True when <paramref name="r"/> resolves a non-null mesh (MeshFilter sibling for a
        /// MeshRenderer, sharedMesh for a SkinnedMeshRenderer). The grey-foundation symptom is a
        /// renderer present but no mesh bound.</summary>
        private static bool RendererHasMesh(Renderer r)
        {
            if (r == null) return false;
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh != null;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null;
        }

        /// <summary>Destroy a half-built root on a render-broken create (play vs. edit safe). Control-flow
        /// safety — runs regardless of the FlowTrace toggle so a broken structure is never left in-scene.</summary>
        private static void DestroyRoot(GameObject root)
        {
            if (root == null) return;
            if (Application.isPlaying) Object.Destroy(root);
            else                       Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Create a composite (a pre-snapped bundle of cells — e.g. a castle as a
        /// group of wall/tower/gate entries). Each member is placed relative to
        /// <paramref name="pose"/> by its <see cref="CellPlacement"/> offset+rotation,
        /// reusing <see cref="Create"/> per member. Missing member ids are skipped
        /// (logged). Returns the group root.
        /// </summary>
        public static GameObject CreateGroup(CatalogEntry composite, Pose pose, Transform parent)
        {
            using var _ = FlowTrace.Enter("Structure", $"CreateGroup id='{composite?.id ?? "<null>"}'");

            if (composite == null)
            {
                // U: was Debug.LogWarning — roll up.
                FlowTrace.Fail("Structure", "CreateGroup called with a null entry — skipped (returning null).");
                return null;
            }

            var groupRoot = new GameObject(string.IsNullOrEmpty(composite.displayName)
                ? $"Group-{composite.id}" : composite.displayName);
            groupRoot.transform.SetParent(parent, false);
            groupRoot.transform.SetPositionAndRotation(pose.position, pose.rotation);

            if (composite.composite == null || composite.composite.Length == 0)
            {
                // U: was Debug.LogWarning — Warn (empty composite is recoverable, returns the bare root).
                FlowTrace.Warn("Structure", $"composite '{composite.id}' has no cell placements — empty group returned.");
                return groupRoot;
            }

            // G: TryEach the member loop — ONE bad member (throwing Create / bad pose) logs + is
            // SKIPPED, never aborts the whole group (a castle must not vanish because one wall threw).
            int built = 0;
            var result = Guard.TryEach("Structure", $"build member of '{composite.id}'",
                composite.composite, cell =>
            {
                if (cell == null || string.IsNullOrEmpty(cell.cellEntryId)) return;

                var member = CatalogRegistry.Get(cell.cellEntryId);
                if (member == null)
                {
                    // U: was Debug.LogWarning — Warn + skip this member, keep building the rest.
                    FlowTrace.Warn("Structure", $"composite '{composite.id}': member " +
                        $"'{cell.cellEntryId}' not in registry — skipped.");
                    return;
                }

                // Member pose is the cell offset/rotation expressed in the group's
                // local space, composed onto the group's world pose.
                Vector3 worldPos = groupRoot.transform.TransformPoint(cell.offset);
                Quaternion worldRot = groupRoot.transform.rotation *
                                      Quaternion.Euler(0f, cell.yRotation, 0f);

                if (Create(member, new Pose(worldPos, worldRot), groupRoot.transform) != null)
                    built++;
                else
                    FlowTrace.Warn("Structure",
                        $"composite '{composite.id}': member '{cell.cellEntryId}' Create returned null (render-broken/missing) — skipped.");
            });

            FlowTrace.Step("Structure", $"composite '{composite.id}' built {built}/" +
                $"{composite.composite.Length} member(s) (loop built={result.built}, failed={result.failed}).");
            return groupRoot;
        }

        // ── corrected-bounds seat + footprint (fixes float + tight-to-wall) ───
        // The placement pipeline (ghost + footprint + seat) must measure the UPRIGHT,
        // OrientationFix-applied bounds, not the raw lying-down prefab. These helpers
        // make that the single source of truth used by Create (seat) and the loader/
        // validity (footprint) so all three agree on the same corrected geometry.

        /// <summary>
        /// Re-seat a skinned visual so its CURRENT (post-correction) world-bounds base
        /// sits at <paramref name="groundY"/>. Called AFTER the OrientationFix re-rotates
        /// the mesh, undoing the float/sink VisualFactory.SeatOnGround introduced when it
        /// seated the raw (un-corrected) bounds. XZ is left as VisualFactory centred it.
        /// </summary>
        private static void ReseatCorrectedBottom(GameObject visual, float groundY)
        {
            if (visual == null) return;
            // G: guard the bounds op — a degenerate/NaN mesh bound logs + leaves the seat unchanged
            // (the float self-reports via the bounds-miss path) rather than throwing mid-create.
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

                // VERIFY THE RESEAT LANDED (§12, 2026-08-20 portal triage). This helper runs AFTER
                // the per-row orientation offset/scale has moved the mesh, i.e. it is the LAST thing
                // that decides whether a placed structure's bottom touches the plaza. It used to
                // shift and return with no proof, so a stale/degenerate bounds measurement produced
                // a silent floater and the only evidence left was the [Flow:Xform] line — whose
                // pos= is the PIVOT, not the bottom, and is therefore routinely misread as a float
                // when a centre-pivoted 4 m building correctly reports local y = +2.00.
                // VisualFactory.IsSeatedOnGround is the SHARED definition of "seated" (same epsilon
                // as the runtime seat and as StructureSeatRegression) — never re-derive it here.
                if (!VisualFactory.IsSeatedOnGround(visual, groundY, out float bottomY))
                    FlowTrace.Warn("Structure",
                        $"ReseatCorrectedBottom('{visual.name}') LEFT IT OFF THE GROUND: bounds bottom " +
                        $"y={bottomY:F2} vs ground y={groundY:F2} (off by {bottomY - groundY:F2} m, " +
                        $"tolerance {VisualFactory.SeatEpsilonMetres:F2} m) — this structure floats/sinks.");
            });
        }

        /// <summary>
        /// Build the entry's visual OFF-SCREEN, apply its OrientationFix, and measure the
        /// resulting UPRIGHT XZ footprint (the larger of width/depth, in metres). Used by
        /// the placement/loader path so the footprint matches the corrected mesh the ghost
        /// shows — a lying-down prefab would otherwise report a long, wrong footprint and
        /// the tower couldn't sit tight to a wall. Returns the entry's authored
        /// repo.placement.footprint as a fallback when the visual can't be measured.
        /// The temp object is destroyed before return (no scene side-effects).
        /// </summary>
        // Cache upright XZ per entry id — ghost loop calls every frame while arming.
        // Key folds orientation + scale so a live re-orient invalidates.
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> s_footprintXzCache =
            new System.Collections.Generic.Dictionary<string, Vector2>();

        /// <summary>
        /// Scalar max(width,depth) — legacy callers / regressions. Prefer
        /// <see cref="MeasureUprightFootprintXZ"/> for CoC non-square claims (WO-986).
        /// </summary>
        public static float MeasureUprightFootprintMetres(CatalogEntry entry)
        {
            Vector2 xz = MeasureUprightFootprintXZ(entry);
            return Mathf.Max(xz.x, xz.y);
        }

        /// <summary>
        /// WO-986: upright mesh claim in metres as (size.x, size.z) — NOT collapsed to max
        /// and squared. Thin structures keep a thin axis so they pack CoC-style.
        /// </summary>
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
                        // World AABB after orientation — CoC claim axes (WO-986).
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

        /// <summary>
        /// WO-972 (owner F8 seq 2327, verbatim: "cannot build walls beside each other").
        /// The metric the GRID CLAIM is computed from — which is NOT always the measured
        /// mesh. For a <see cref="CatalogType.Wall"/> row the claim comes from the AUTHORED
        /// repo.placement.footprint; every other row keeps the measured upright mesh
        /// (<see cref="MeasureUprightFootprintMetres"/>), byte-identical to before.
        ///
        /// WHY WALLS ARE DECOUPLED — proven by the seq-2327 capture, not inferred:
        ///   wall_wood's fitted body measures just over the 3.00 m cell across (its
        ///   MeshCollider dumped size=(3.03, 3.73, 1.42)), so Ceil(m / cellSize) claimed
        ///   TWO cells — and PlacementGrid.FootprintCells SQUARES that claim, so a 1.42 m
        ///   THIN palisade claimed a 2x2 block. The captured reject:
        ///     [Flow:Build] REJECT Occupied cell=(17,16) fp=(2x2) gate=CellGrid
        ///                  occupantCell=(17,17) occupant='wall_wood'
        ///   — she was starting a corner one row over and the neighbour's phantom cells
        ///   owned it. The run she DID land sat on a SIX-metre pitch (Occupy 12_17 /
        ///   14_17 / 16_17; collider centres x = -7.50 / -1.50 / 4.50), i.e. a ~3 m hole
        ///   between every 3.03 m segment. A wall is a ONE-CELL tile (CoC model); its body
        ///   may overhang its cell slightly, and that overhang is exactly what makes a run
        ///   read as continuous instead of a dashed line.
        ///
        /// THE FIX IS CLAIM-SIDE ONLY — THE MESH IS NOT RESIZED. The height-cadence
        /// carve-out for walls (structures-catalog.json `_heightCadence` and the per-row
        /// `_heightNote`: narrowing a wall opens PATHABLE GAPS in already-saved runs and
        /// shrinks its NavMeshObstacle with it) is untouched. So is the obstacle itself:
        /// BaseLayoutLoader.AddFootprintBlocker sizes the box as
        /// Clamp(rendered * 0.85, cellSize, claim), which resolves to 3x3 m at BOTH the old
        /// 2x2 claim and the new 1x1 (captured: "kept root footprint box 3x3m (h=4)") — the
        /// carve is byte-identical. And nothing MOVES: PlacementGrid.CellToWorld seats a
        /// structure on its ORIGIN CELL centre, independent of footprint, so every
        /// already-saved wall replays at the exact same world position and merely claims
        /// fewer (never more) cells — a shrinking claim can never invalidate a saved layout.
        /// </summary>
        /// <summary>Legacy scalar claim (max axis). Prefer <see cref="MeasureClaimFootprintXZ"/>.</summary>
        public static float MeasureClaimFootprintMetres(CatalogEntry entry)
        {
            Vector2 xz = MeasureClaimFootprintXZ(entry);
            return Mathf.Max(xz.x, xz.y);
        }

        /// <summary>
        /// WO-986 / WO-972 / WO-1153 claim metric as non-square metres (x, z).
        /// Walls AND GATES: both axes use authored placement.footprint (one-cell tile, CoC wall runs).
        /// Everything else: measured upright mesh XZ (no squaring of max).
        /// Shrinking a prior square claim on load is safe; never expands phantom cells.
        ///
        /// WO-1153 — WHY THE GATE JOINED THE WALL CARVE-OUT (measured 2026-08-23, not inferred):
        /// Gate_Medieval_Medium's native bounds are 15.75 x 10.52 x 5.22; gate_stone authors
        /// heightMul 1.0 and no maxFootprint, so fit-to-height (YHeightVariable = 4 m) lands its
        /// X at 5.99 m — against PlacementGrid.cellSize = 3 m that CEILS to TWO cells, where its
        /// authored footprint 2.8 says ONE. A gate over-claimed a full extra cell, so it could not
        /// be seated flush in the wall run it is supposed to interrupt.
        ///
        /// ⛔ DELIBERATELY NOT GENERALISED to "any row with an authored placement.footprint".
        /// ALL 28 catalog rows author one, so that would be a repo-wide claim remap — and WO-972's
        /// safety argument above holds ONLY for a SHRINKING claim ("a shrinking claim can never
        /// invalidate a saved layout"). A row whose authored number EXCEEDS its measured mesh would
        /// GROW its claim and can break an already-saved town on replay. Wall + Gate only; adding a
        /// third type means proving authored &lt;= measured for every row of that type first.
        /// </summary>
        public static Vector2 MeasureClaimFootprintXZ(CatalogEntry entry)
        {
            Vector2 measured = MeasureUprightFootprintXZ(entry);
            if (entry == null ||
                (entry.type != CatalogType.Wall && entry.type != CatalogType.Gate)) return measured;

            float authored = entry.repo != null && entry.repo.placement != null
                ? Mathf.Max(0.01f, entry.repo.placement.footprint)
                : Mathf.Max(measured.x, measured.y);

            // WO-1153: name the TYPE that took the carve-out. The line said "WALL CLAIM"
            // unconditionally, so a gate on the authored path logged as a wall — a trace that
            // misreports which branch ran is worse than no trace (CLAUDE.md §12).
            string typeName = entry.type.ToString().ToUpperInvariant();
            FlowTrace.Once("Build", typeName.ToLowerInvariant() + "-claim-" + entry.id,
                $"{typeName} CLAIM '{entry.id}' (type={entry.type}): grid claim AUTHORED " +
                $"placement.footprint={authored:0.###}m (both axes → one-cell tile), mesh measures " +
                $"({measured.x:0.###} x {measured.y:0.###})m. " +
                "Mesh is NOT resized — claim only (WO-972 + WO-986 + WO-1153).");
            return new Vector2(authored, authored);
        }

        /// <summary>World-space renderer bounds of <paramref name="go"/> (renderer-first, collider fallback).</summary>
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

        // ── behaviorId -> component bridge (the Core/Village boundary) ─────────
        // A plain switch, NOT reflection. Adding a new behaviour = a new case here.
        private static void AttachBehavior(GameObject root, CatalogEntry entry)
        {
            string behaviorId = entry.repo != null ? entry.repo.behaviorId : null;
            if (string.IsNullOrEmpty(behaviorId)) return;   // decoration / no behaviour

            // G: guard the whole attach — a throwing AddComponent/Configure logs + leaves the
            // already-rendered structure standing (visual is verified before this), never throws
            // out of Create and never blanks a built structure.
            Guard.Try("Structure", $"attach behavior '{behaviorId}' to '{entry.id}'", () =>
            AttachBehaviorImpl(root, entry, behaviorId));
        }

        private static void AttachBehaviorImpl(GameObject root, CatalogEntry entry, string behaviorId)
        {
            switch (behaviorId)
            {
                case "DefenseTower":
                {
                    var t = root.AddComponent<DefenseTower>();
                    var r = entry.repo;
                    t.Range     = r.range;
                    t.Damage    = r.damage;
                    t.FireRate  = r.fireRate;
                    t.CanHitAir = r.canHitAir;
                    // ANTI-AIR SPECIALIST (owner 2026-07-08): the Ballista's airOnly flag makes it
                    // acquire ONLY flying targets. Implies CanHitAir so it reaches the cruising dragon.
                    t.AirOnly   = r.airOnly;
                    if (r.airOnly) t.CanHitAir = true;
                    t.Element   = r.element;
                    // TOWER IDENTITY (owner 2026-07-08): optional per-entry projectile
                    // VISUAL style ("pellet"|"bolt"|"spell"; null = pellet). Data-driven —
                    // the component resolves + instruments the string itself.
                    t.ProjectileStyle = r.projectileStyle;
                    // CATALOG IDENTITY (WO-870): the entries[].id this tower was built from.
                    // Presentation only - DefenseTower uses it solely to look up owner-tagged
                    // per-tower projectile VFX keys (the Archer and the Ballista are otherwise
                    // indistinguishable: both bolt / None / ground). Never a gameplay input.
                    t.CatalogId = entry.id;
                    break;
                }

                // ArcaneTower — WO-113: the slow-firing AoE MAGIC tower. Same copy-
                // stats-off-RepoProps pattern as DefenseTower, plus the optional AoE
                // fields (aoeRadius / slowSeconds / splashFraction). Each optional field
                // only overrides the component's serialized default when authored > 0,
                // so a sparse row still gets a sensible blast + slow.
                case "ArcaneTower":
                {
                    var a = root.AddComponent<ArcaneTower>();
                    var r = entry.repo;
                    a.Range     = r.range;
                    a.Damage    = r.damage;
                    a.FireRate  = r.fireRate;
                    a.CanHitAir = r.canHitAir;
                    a.Element   = r.element != DamageElement.None ? r.element : DamageElement.Aether;
                    if (r.aoeRadius      > 0f) a.AoeRadius            = r.aoeRadius;
                    if (r.slowSeconds    > 0f) a.SlowSeconds          = r.slowSeconds;
                    if (r.splashFraction > 0f) a.SplashDamageFraction = r.splashFraction;
                    break;
                }

                // WallSegment is authored with id/index/length by the builder, not
                // from RepoProps stats — attach it bare; the caller configures it.
                case "WallSegment":
                    root.AddComponent<WallSegment>();
                    break;

                // Gate — the cardinal force-field gate. Attach bare (same pattern as
                // WallSegment): Awake null-guards its collider/renderer + builds the
                // MPB, so a player-/save-placed gate is a live IDamageableStructure
                // (takes damage, collapses below 25%). The WallLayout builder calls
                // Configure() to size the opening; a free-placed gate keeps defaults.
                case "Gate":
                    root.AddComponent<Gate>();
                    // ⛔ THE FIX (quest audit 2026-08-21) — the 12 explore.visit-gate.* daily
                    // templates tick from GateProximityOpener.OnHeroEntered, and a gate built
                    // HERE never had one. The opener was attached only by VillageController,
                    // whose guid is in no scene or prefab, so on a player-built town the whole
                    // exploration slot could never advance. Attaching it at the one place gates
                    // are actually created is what makes the templates reachable at all.
                    // [RequireComponent(typeof(Gate))] is satisfied by the line above; the
                    // opener self-builds its trigger relay and no-ops without a hero.
                    root.AddComponent<GateProximityOpener>();
                    break;

                // ResourceCollector — CoC-style typed town collector (Farm / Lumbermill /
                // Forge). A collector places EXACTLY like any other structure (same grid /
                // ghost / persist path) — only the attached behaviour differs. Configure it
                // with the ResourceBuildingProgression id from the row (collectorBuildingId),
                // falling back to the entry id when the row omits it.
                case "ResourceCollector":
                {
                    var col = root.AddComponent<DeNelle.Village.Buildings.Progression.ResourceCollector>();
                    var r = entry.repo;
                    string buildingId = !string.IsNullOrEmpty(r != null ? r.collectorBuildingId : null)
                        ? r.collectorBuildingId : entry.id;
                    col.Configure(buildingId);

                    // WO-900 Part A - attach the DIEGETIC FILL VIEW. CollectorStackView is a
                    // complete, 437-line CoC "I am full" tell (pooled prop pile / world-space fill
                    // bar, amber near-full band at 85%, redundant "N/20" readout, the "!" bang, the
                    // glint VFX, the bob, and the one-time "<Building> is full" toast) that has had
                    // ZERO CALLERS since it was written - so a collector filling up showed the
                    // player absolutely nothing and the wallet number just stopped moving. This is
                    // the wiring, not a rebuild. Attach self-skips the origin-parked DDOL logical
                    // fallback hosts (CollectorStackView.cs:100-102), so only real placed collectors
                    // get decorated, and it is null-safe. Presentation stays a SEPARATE component
                    // injected with the model - the gameplay object never builds UI.
                    DeNelle.Village.Buildings.Progression.CollectorStackView.Attach(col);
                    break;
                }

                // CrystalMine - passive Crystal generator. WO-856: it banks crystals on
                // EVERY cleared wave from LEVEL 1, on the curve buildings.json authors
                // ("crystal-mine".crystalsPerWave = [1, 2, 4], indexed by level - 1). The
                // level is READ off the PlacedStructure this root carries (the persisted
                // per-instance level), never owned by the component; upgrades go through
                // the BuildMode Upgrade verb charging mine_crystal's repo.upgradeCost.
                // (The pre-WO-856 comment here claimed "+1/wave at L3" - that gate was
                // unreachable and the mine had never paid out.) Self-resolves the wave +
                // economy in Start and builds its own placeholder visual when no prefab
                // is assigned, so a placed mine is a real, upgradeable gameplay object.
                case "CrystalMine":
                    root.AddComponent<CrystalMine>();
                    break;

                // HealingFountain — Healing Caravan. A SUPPORT structure that heals
                // the Heart of Elarion out of battle only (rate scales L1=1.0/L2=2.0/L3=3.5
                // HP/s). Self-resolves Heart + WaveManager in Start; Configure reads the
                // level ceiling from RepoProps. Gated behind the arcane-tower research perk
                // 'arcane-wellspring' at the build-palette layer.
                case "HealingFountain":
                {
                    var f = root.AddComponent<HealingFountain>();
                    f.Configure(entry);
                    // WO-991: the caravan is a glass support unit. WO-1424 SPLIT its movement by
                    // context (owner ruling 2026-09-06: "it slow follows as a combat attack item
                    // as defensive item it stationary") — the component now slow-follows only in
                    // an enemy-owned scene and stands still in the player's town. The component is
                    // attached the same way in both cases; it latches the mode itself in Awake.
                    //
                    // ⛔ ff.caravanmobile IS NOT A "MAKE IT STATIC" SWITCH — DO NOT REACH FOR IT.
                    // This component is the SOLE attach site for CaravanHealField (the 7m heal
                    // aura + the Safe Zone range ring), CaravanStatusChip and the FloatingHealthBar
                    // glass HP. Turning the flag OFF amputates all of those, not just the follow —
                    // which is exactly what the Warn below is admitting. The flag stays a genuine
                    // kill switch for the WHOLE WO-991 shell; the stationary/defensive behaviour is
                    // reached through the split inside the component, never through this flag.
                    if (entry != null && entry.id == "healing_caravan")
                    {
                        if (DeNelle.Core.FeatureFlags.HealingCaravanMobile)
                        {
                            var mob = root.AddComponent<HealingCaravanMobility>();
                            mob.Configure(entry);
                        }
                        else
                        {
                            DeNelle.Core.Diagnostics.FlowTrace.Warn("Caravan",
                                "ff.caravanmobile OFF -> healing_caravan built STATIC (no follow, no glass HP, no chip)");
                        }
                    }
                    break;
                }

                // *** RETAINED ON PURPOSE - DO NOT DELETE AS DEAD CODE (WO-990, 2026-08-14). ***
                // The catalog row that used to point here ('tower_healer') was RETIRED from
                // structures-catalog.json at v20 by OWNER RULING ("i do not know what the town
                // healer is" -> "retire"). She did not recognise it because it had NEVER been
                // buildable: it appeared in NO build category, so no palette could ever offer it.
                // This case is therefore CURRENTLY UNREFERENCED BY ANY CATALOG ROW, and that is
                // an expected, recorded state - not rot. It is kept because it is the REFERENCE
                // IMPLEMENTATION of the WO-891 support-FIELD pattern described below (stats plus
                // two tags), the worked example every future field structure is meant to be copied
                // from, together with the commented SlowFieldTower sibling. Its known future
                // consumer is WO-991 - the MOBILE Healing Caravan with an unlockable heal FIELD,
                // which is the design successor of the retired row. Any new row that sets
                // behaviorId "HealerTower" reaches this code unchanged.
                //
                // HealerTower - WO-891. The FIRST instance of the general support/offensive
                // FIELD pattern, and the proof of its thesis: a new structure is stats plus
                // TWO TAGS. It copies range / fireRate / magnitude off entry.repo exactly the
                // way DefenseTower's case above does, then hands SupportFieldStructure an
                // element tag (presentation) and an effect tag (gameplay).
                //
                // NOT a clone of HealingFountain. That one is a bespoke singleton whose whole
                // job is topping the HEART up out of battle (its own upgrade UI, its own coin
                // ladder, its own out-of-battle gate). This heals UNITS in a radius on a tick,
                // in or out of battle, and carries no UI of its own.
                //
                // THE PATTERN PROOF - a second variant costs exactly these three lines and NO
                // new VFX code, because SupportFieldStructure's element table already resolves
                // all four wheel elements and every one of those VFXTypes is already
                // catalogued:
                //     case "SlowFieldTower":
                //     {
                //         var s = root.AddComponent<SupportFieldStructure>();
                //         s.Configure(entry, SupportFieldStructure.FieldElement.Ice,
                //                            SupportFieldStructure.FieldEffect.Heal);
                //         break;
                //     }
                // (An Ice SLOW would additionally need one arm in ResolveTick's effect switch
                // - that is gameplay, not VFX, and WO-891's claim is about the VFX half. Said
                // plainly rather than glossed.)
                case "HealerTower":
                {
                    var h = root.AddComponent<SupportFieldStructure>();
                    h.Configure(entry,
                                SupportFieldStructure.FieldElement.Holy,
                                SupportFieldStructure.FieldEffect.Heal);
                    break;
                }

                // GameplayBuilding — Phase 2: the village's economy/upgrade buildings
                // (pet-house / workshop / market / mill / lumbermill / forge / arcane-
                // tower) as first-class droppable catalog entries. Mirrors the hard-coded
                // VillageSceneBuilder.Buildings[] inject (which stays as the parity
                // fallback): attach + Configure a Building from the catalog row, add the
                // proximity-prompt BuildingInteractable, and register the building with
                // the scene's VillageController so it shows in the roster / wave damage
                // accounting exactly like a baked one.
                //
                // The catalog 'id' MUST equal Building.Id verbatim (pet flow / gear /
                // talents key on it) — BuildingType is derived from the id so a Type
                // enum collision (Market & Lumbermill both ordinal 5) stays benign:
                // identity is by id, the enum only steers the default panel route.
                case "GameplayBuilding":
                {
                    var b = root.AddComponent<Building>();
                    b.Configure(BuildingTypeForId(entry.id), entry.id,
                                string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName);

                    // Proximity F-prompt / mobile interact button (RequireComponent(Building)).
                    root.AddComponent<BuildingInteractable>();

                    // Register with the live scene controller so the placed building joins
                    // the roster (null-safe: a headless / controller-less scene just skips it).
                    var controller = Object.FindAnyObjectByType<VillageController>();
                    if (controller != null) controller.RegisterBuilding(b);

                    // Owner 2026-07-15 "arcane towers should have an aura": the arcane-tower
                    // landmark (this GameplayBuilding path replays it from BaseLayout) holds a
                    // persistent magic-circle aura. Idempotent; colorblind-safe (motion, not hue).
                    // Owner 2026-07-30 (WO-788, owner's explicit pick): the Cathedral of Magic (id
                    // "arcane-tower") shows the flat blue electro rune-circle ground loop
                    // ("Cathedral_Aura" -> Magic circle electro loop) — NOT a shield dome; the prior
                    // "Aegis_Shield" holy dome was the felt-test reject. Distinct from the combat
                    // Arcane Spire's "Aura_HeartPulse" + nodes' "Poi_NodeAura" (retagged
                    // 2026-08-06; this line used to name "TreeofLifeAura_Aura" and was stale).
                    if (string.Equals(entry.id, "arcane-tower", System.StringComparison.OrdinalIgnoreCase))
                        ArcaneAura.Ensure(root, "Cathedral_Aura");
                    break;
                }

                default:
                    // U: was Debug.LogWarning — Warn (the structure still renders; it just has no
                    // gameplay behaviour, which a capture should surface, not silently swallow).
                    FlowTrace.Warn("Structure", $"'{entry.id}': unknown behaviorId " +
                        $"'{behaviorId}' — no behaviour attached (visual-only structure).");
                    break;
            }
        }

        /// <summary>
        /// Maps a catalog building id to its <see cref="BuildingType"/>, matching the
        /// hard-coded VillageSceneBuilder.Buildings[] Type assignments so a catalog-placed
        /// building routes its interaction panel the same as a baked one. Identity is by
        /// id (Building.Id), not by this enum — Market and Lumbermill share ordinal 5 in
        /// the Buildings[] table, which is benign (the enum only picks the default panel;
        /// BuildingInteractable re-resolves the actual route from the id first). Unknown
        /// ids fall back to CrystalMine (ordinal 0 / Upgrade panel), never throwing.
        /// </summary>
        private static BuildingType BuildingTypeForId(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "pet-house":    return BuildingType.PetHouse;
                case "arcane-tower": return BuildingType.ArcaneTower;
                case "workshop":     return BuildingType.Workshop;   // labelled "Forge" in-world
                case "farm":
                case "mill":         return BuildingType.Farm;
                case "market":       return BuildingType.Lumbermill; // matches Buildings[] Type=5
                case "lumbermill":   return BuildingType.Lumbermill;
                case "forge":        return BuildingType.Forge;      // weapons
                case "armorer":
                case "blacksmith":   return BuildingType.Armorer;    // armor vendor
                case "jeweler":      return BuildingType.Workshop;   // Sable the Jeweler (crafting/upgrade station; Yarn route resolves by name -> TalkToJeweler)
                // WO-707 storage containers (lumberyard/foundry/silo): no Storage-ish
                // BuildingType exists, so they take the generic CrystalMine ordinal —
                // its default Upgrade panel is the right route for a capacity-upgradeable
                // container. Explicit cases so the mapping is a decision, not a fallthrough.
                case "lumberyard":
                case "foundry":
                case "silo":         return BuildingType.CrystalMine;
                // WO-812: the placeable Barracks. No Barracks-ish BuildingType exists, so it
                // takes the generic CrystalMine ordinal (default Upgrade panel on building tap);
                // the TRAIN door is the drillmaster NPC (BarracksNpcInjector anchors to this
                // placed instance) -> DialogueService.PlayStructure("barracks"). Explicit case
                // so the mapping is a decision, not a fallthrough.
                case "barracks":     return BuildingType.CrystalMine;
                default:             return BuildingType.CrystalMine;
            }
        }
    }
}
