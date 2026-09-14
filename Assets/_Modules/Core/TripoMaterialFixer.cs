// =============================================================================
// TripoMaterialFixer — runtime fix for FBX meshes that import with legacy
// Phong/Standard materials URP can't render.
// -----------------------------------------------------------------------------
// Owner question 2026-05-20: "why do colors not show on models?"
//
// Root cause: every Tripo AI-generated FBX (Wizard, Knight, Ranger, fairy,
// fox, castle ballast tower) ships with FbxSurfacePhong materials. Unity 6
// URP can't render Phong shaders — the mesh appears as a transparent pink
// ghost or a magenta error.
//
// NOTE (2026-07-23): the apex dragon is deliberately NOT in that list. The old
// dragon that used to render through this fixer was a 3DHaupt Blender export
// (CC-BY-NC — a commercial-ship blocker), NOT a Tripo asset; it has been
// REMOVED and REPLACED by the separately-licensed Asset-Store dragon at
// Assets/Dragon (product 71047), whose materials ship URP-ready and need no
// runtime Phong→URP rebuild. Do not re-add "dragon" here.
//
// This component walks every Renderer in its hierarchy on Awake and rebuilds
// each material under "Universal Render Pipeline/Lit", carrying the texture
// across (preferring _MainTex, then _BaseMap). The optional fallbackTextureName
// loads from Resources/<name> when the source material has no texture bound
// (Tripo's .fbm-folder textures sometimes don't auto-link on import).
//
// Drop this MonoBehaviour onto any GameObject whose FBX renders wrong —
// works for castle arch, pets, hero meshes, anything.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core
{
    [DisallowMultipleComponent]
    public sealed class TripoMaterialFixer : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // P0-2 (PERF AUDIT 2026-06-28, WO-568): shared-material CACHE.
        // The old code allocated an UNSHARED `new Material(lit)` for EVERY renderer
        // slot of EVERY enemy on EVERY spawn. With continuously re-topped + arena families
        // re-staged, two identically-skinned orcs never shared a material -> SRP batching
        // could never coalesce them, and native material memory churned (rebuilt mats are
        // never Destroy()ed on death, they accumulate until Resources.UnloadUnusedAssets).
        //
        // Fix: build the URP/Lit material ONCE per distinguishing tuple (shader +
        // base map + normal map + emission map + base color + emission color +
        // emissive flag + smoothness + metallic) and reuse the SAME shared Material
        // instance for every slot that resolves to the same tuple. All `orc-warrior`
        // bodies now share one material -> batching restored, churn eliminated.
        //
        // STATIC + long-lived deliberately: it must survive respawns so the 2nd..Nth
        // orc is a cache HIT, not a re-alloc. Materials are intentionally never freed
        // (one per unique look, a tiny bounded set), which is the whole point -- no
        // per-instance churn. Only truly-identical looks share; any slot whose tuple
        // differs (unique texture/tint/emission) gets its own cached entry, so the
        // visual result is identical to the old per-instance build, byte for byte.
        // -------------------------------------------------------------------------
        private readonly struct MatKey : System.IEquatable<MatKey>
        {
            public readonly int Shader;
            public readonly int BaseMap;
            public readonly int Normal;
            public readonly int EmissionMap;
            public readonly Color BaseColor;
            public readonly Color EmissionColor;
            public readonly bool Emissive;
            public readonly float Smoothness;
            public readonly float Metallic;

            public MatKey(int shader, int baseMap, int normal, int emissionMap,
                          Color baseColor, Color emissionColor, bool emissive,
                          float smoothness, float metallic)
            {
                Shader = shader; BaseMap = baseMap; Normal = normal; EmissionMap = emissionMap;
                BaseColor = baseColor; EmissionColor = emissionColor; Emissive = emissive;
                Smoothness = smoothness; Metallic = metallic;
            }

            public bool Equals(MatKey o) =>
                Shader == o.Shader && BaseMap == o.BaseMap && Normal == o.Normal &&
                EmissionMap == o.EmissionMap && Emissive == o.Emissive &&
                Smoothness == o.Smoothness && Metallic == o.Metallic &&
                BaseColor == o.BaseColor && EmissionColor == o.EmissionColor;

            public override bool Equals(object obj) => obj is MatKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Shader;
                    h = h * 397 ^ BaseMap;
                    h = h * 397 ^ Normal;
                    h = h * 397 ^ EmissionMap;
                    h = h * 397 ^ (Emissive ? 1 : 0);
                    h = h * 397 ^ Smoothness.GetHashCode();
                    h = h * 397 ^ Metallic.GetHashCode();
                    h = h * 397 ^ BaseColor.GetHashCode();
                    h = h * 397 ^ EmissionColor.GetHashCode();
                    return h;
                }
            }
        }

        private static readonly System.Collections.Generic.Dictionary<MatKey, Material> s_matCache =
            new System.Collections.Generic.Dictionary<MatKey, Material>();
        // Cumulative proof counters (headless): a high hit:new ratio = the win landed.
        private static int s_cacheHits;
        private static int s_cacheNew;

        private static int Id(Texture t) => t != null ? t.GetInstanceID() : 0;

        [SerializeField] private string _fallbackTextureName;
        [SerializeField] private string _forcedTextureName;   // WO-719: UNCONDITIONAL albedo override (see SetForcedTexture)
        private Texture _forcedSourceTexture;
        // WHITE-STREAK FIX (fleet triage 2026-07-18): default was Color.white, so a texture
        // MISS (missing basecolor/albedo atlas) with the tint path active rendered the whole
        // body SOLID WHITE — the recurring "white line/streak" in the party stack. Default to
        // a neutral MID-GREY instead, so a miss degrades to an UNLIT GREY body (the intended
        // degrade per HeroTextureLoader) rather than broken pure-white. Explicit SetFallbackTint
        // callers pass their own colour and are unaffected.
        [SerializeField] private Color _fallbackTint = new Color(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private bool _hasFallbackTint;
        // WHITE-STRUCTURE FALLBACK (ballista fix 2026-07-19): a TEXTURE-MISS-ONLY tint. Unlike
        // _fallbackTint (applied UNCONDITIONALLY when set — it multiplies onto real textures too),
        // this is applied ONLY when a slot resolves NO texture at all (no source map, no fallback,
        // no forced), replacing the would-be SOLID WHITE with a neutral tint. Textured slots are
        // byte-unchanged. Structures (StructureFactory) set this so a model whose albedo didn't
        // survive the build (gitignored .fbm, e.g. Structures/Ballista / WizardTower_1) degrades to
        // flat stone instead of bright untextured white — the "white ballista" symptom.
        [SerializeField] private Color _missTint = new Color(0.60f, 0.58f, 0.54f, 1f);
        [SerializeField] private bool _hasMissTint;
        // FLAT COAT (owner ruling WO-1326, 2026-09-02: "flat coat"). Opt-in, off by default.
        // When set, EVERY slot this fixer rebuilds resolves its base map to NOTHING — source
        // _MainTex/_BaseMap, the fallback texture and the forced texture are all discarded — so
        // the registered tint alone paints the body. This is the ONE owner of "this body is
        // deliberately untextured"; call sites must not re-express it by nulling their own
        // texture argument, or the choice scatters across the callers again.
        // Non-destructive elsewhere: no caller that omits SuppressBaseMap() changes by one byte.
        [SerializeField] private bool _suppressBaseMap;
        [SerializeField] private float _smoothness = 0.15f;
        [SerializeField] private float _metallic = 0f;
        [SerializeField] private bool _forceRebuild;
        private bool _ran;

        /// <summary>
        /// Rebuild EVERY material (even ones already on a URP shader) as a plain
        /// URP/Lit carrying the source's basecolor. Use when the auto-extracted
        /// Tripo materials are URP but render wrong (e.g. a vertex-colour shader
        /// painting a rainbow patchwork) — a plain URP/Lit with the same _BaseMap
        /// shows the real texture instead.
        /// </summary>
        public void ForceRebuildAll() => _forceRebuild = true;

        /// <param name="optional">True when a missing fallback texture is an EXPECTED state
        /// (owner F8 2026-07-02: the pet basecolor PNGs were purged for size in 2774fb50; the
        /// pets' real look comes from their extracted .fbm materials). Downgrades the miss
        /// from Warn/Fail to Step so it never lands in the break-log.</param>
        public void SetFallbackTexture(string resourcesPath, bool optional = false)
        {
            _fallbackTextureName = resourcesPath;
            _fallbackOptional = optional;
        }

        /// <summary>
        /// WO-719 (arcane spire renders WHITE): FORCE a Resources texture as the _BaseMap on
        /// EVERY rebuilt material, UNCONDITIONALLY - overriding whatever the source material
        /// carries (unlike SetFallbackTexture, which only fills in when the source has no map).
        /// The arcane-tower Tripo FBX ('Structures/arcane tower') ships an extracted material
        /// whose bound map renders white; forcing the authored albedo here bakes it into the
        /// fixer's SINGLE-PASS URP/Lit rebuild that is ASSIGNED to the renderer, so it STICKS in
        /// the built player (the MagentaGuard "fresh material assigned to renderer" durability)
        /// and is RACE-FREE - no post-skin material mutation for the next-frame rebuild to stomp.
        /// Opt-in: only callers that set this are affected; default (null) = no change.
        /// </summary>
        public void SetForcedTexture(string resourcesPath) => _forcedTextureName = resourcesPath;

        /// <summary>
        /// Pins an albedo already referenced by the instantiated model. This avoids relying on
        /// shader-property aliases while rebuilding materials in a player build (where an otherwise
        /// valid model dependency could be rebuilt as a flat white/tint-only material).
        /// </summary>
        public void SetForcedSourceTexture(Texture texture) => _forcedSourceTexture = texture;

        /// <summary>
        /// WO-1326 (owner ruling 2026-09-02, verbatim: <c>"flat coat"</c>): rebuild every slot on
        /// this model with NO base map at all, so the registered tint is the whole albedo and the
        /// body reads as one flat, evenly-shaded surface with no painted markings.
        ///
        /// WHY THIS EXISTS AS A SEAM RATHER THAN AS A DELETED ASSET: the measured defect was that
        /// the SAME tree produced two different-looking wolves because the authored coat map
        /// reached two payloads and not the third (WO-1326 §2). The look was being decided by an
        /// accident of packaging. Suppressing the map HERE makes the flat look the deliberate,
        /// identical outcome on every build target, without touching, deleting or re-authoring one
        /// texture — the normal map is still preserved, so the mesh keeps its shading relief.
        ///
        /// Opt-in and one-way: only callers that ask for it are affected.
        /// </summary>
        public void SuppressBaseMap() => _suppressBaseMap = true;

        /// <summary>
        /// Forces a solid fallback colour on every material rebuilt by this
        /// fixer. Use when the Tripo FBX's embedded textures don't extract
        /// (the player build sees no _MainTex / _BaseMap on the source) and
        /// the mesh would otherwise render solid white. Owner direction
        /// 2026-05-20: pets / heroes show white in the player despite the
        /// fixer — wire each model's species tint as a safety net.
        /// </summary>
        public void SetFallbackTint(Color tint)
        {
            _fallbackTint = tint;
            _hasFallbackTint = true;
        }

        /// <summary>
        /// Ballista fix 2026-07-19: register a TEXTURE-MISS-ONLY tint. Applied by the rebuild ONLY
        /// when a slot resolves no texture at all (no source map, no fallback, no forced) — replacing
        /// the default white so a textureless model degrades to a neutral tint instead of SOLID WHITE.
        /// Non-destructive for textured slots (they keep their map + colour, byte-for-byte). Used by
        /// StructureFactory: structures carry no species tint, so a model whose albedo didn't survive
        /// the build (gitignored .fbm) rebuilt to bright white — this rescues that case only.
        /// </summary>
        public void SetMissTint(Color tint)
        {
            _missTint = tint;
            _hasMissTint = true;
        }

        private bool _fallbackOptional;
        private bool _hasEmissionOverride;
        private Color _emissionOverride = Color.black;
        private const float EmissionOverrideIntensity = 0.30f; // owner: "very minimal"

        /// <summary>
        /// Owner 2026-05-25: the pets' "aura / light beams" was bright emission
        /// preserved from their source materials. Replace it with a MINIMAL,
        /// affinity-coloured glow instead (fire red / ice white / aether violet).
        /// When set, this overrides any source emission on every rebuilt material.
        /// </summary>
        public void SetEmissionOverride(Color color)
        {
            _emissionOverride = color;
            _hasEmissionOverride = true;
        }

        // Start (not Awake): callers like PetDeployer add this component and
        // THEN set the fallback texture name + tint on the next line. Awake
        // fires synchronously inside AddComponent, so the setters would land
        // too late and Run() would build URP materials with no diffuse and
        // no tint — the symptom Samantha hit 2026-05-24 (pets invisible,
        // labels only). Start defers Run() to the next frame so the setter
        // calls have already landed.
        private void Start() => Run();

        private void Run()
        {
            if (_ran) return;
            _ran = true;

            using var _t = FlowTrace.Enter("TripoMatFix", $"Run on '{gameObject.name}'");

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                // V/T: the fixer failing to find its target shader = magenta/error everywhere it
                // is attached (heroes/pets/buildings). This is a HARD fail, not a warn — roll it
                // up to the break-log so a run self-reports the entire fixer was a no-op.
                FlowTrace.Fail("TripoMatFix",
                    $"URP/Lit shader NOT FOUND — TripoMaterialFixer on '{gameObject.name}' cannot rebuild any " +
                    "material; every mesh it covers stays on its (likely Phong/error) source shader = magenta/pink. " +
                    "URP pipeline asset missing or shader stripped from the build.");
                return;
            }

            Texture2D fallbackTex = null;
            if (!string.IsNullOrEmpty(_fallbackTextureName))
                // WO-545: Addressables-first/Resources-fallback seam (was Resources.Load). Generic
                // over the path — hero atlases ("Heroes/Textures/*") resolve from the migrated
                // bundle; enemy atlases ("Enemies/OrcTex/*", not migrated) fall back to Resources.
                fallbackTex = HeroTextureLoader.Load(_fallbackTextureName, _fallbackOptional);
            if (!string.IsNullOrEmpty(_fallbackTextureName) && fallbackTex == null)
            {
                if (_fallbackOptional)
                    // Expected miss (e.g. pet basecolor PNGs purged for size, 2774fb50) —
                    // the source materials / tint carry the look. Step, never break-log noise.
                    FlowTrace.Step("TripoMatFix",
                        $"'{gameObject.name}': optional fallback texture '{_fallbackTextureName}' absent (by design) — " +
                        "source materials / tint carry the look.");
                else
                    FlowTrace.Warn("TripoMatFix",
                        $"'{gameObject.name}': fallback texture '{_fallbackTextureName}' did not load from Resources — " +
                        "rebuilt materials will fall back to tint/source only.");
            }
            // WO-719: the UNCONDITIONAL forced albedo (arcane spire). Loaded once, applied to every
            // slot below regardless of the source's own map. A miss is a hard Fail (break-log) because
            // a set-but-unresolved force = the spire stays white (the exact symptom being fixed).
            Texture forcedTex = _forcedSourceTexture;
            if (!string.IsNullOrEmpty(_forcedTextureName))
            {
                forcedTex = HeroTextureLoader.Load(_forcedTextureName, false);
                if (forcedTex == null)
                    FlowTrace.Fail("TripoMatFix",
                        $"'{gameObject.name}': FORCED texture '{_forcedTextureName}' did NOT resolve (Addressables/Resources) - " +
                        "materials keep their (white) source map; the forced-albedo override is a no-op this run.");
                else
                    FlowTrace.Step("TripoMatFix",
                        $"'{gameObject.name}': FORCED albedo '{_forcedTextureName}' loaded - overriding every slot's _BaseMap (WO-719).");
            }

            FlowTrace.Step("TripoMatFix",
                $"{gameObject.name}: fallbackPath='{_fallbackTextureName}', loaded={fallbackTex != null}, forced={forcedTex != null}, tintActive={_hasFallbackTint}, flatCoat={_suppressBaseMap}");

            int renderers = 0, slotsRebuilt = 0;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                renderers++;
                // G: guard the per-material loop so ONE bad source material logs + is skipped,
                // never aborting the rebuild of the rest of this renderer's slots (Guard.TryEach
                // LogErrors the bad index via [Flow:TripoMatFix] -> break-log, then carries on).
                var rr = r;
                var matsRef = mats;
                Guard.TryEach("TripoMatFix", $"rebuild slots on '{rr.name}'", System.Linq.Enumerable.Range(0, matsRef.Length), i =>
                {
                    var src = matsRef[i];
                    // WO-34 (2026-05-25): ALWAYS rebuild — do NOT skip already-URP
                    // materials. The Tripo importer extracts materials AS URP, but
                    // those extracted URP mats render washed-out/grey (buildings
                    // were grey in ~90% of runs because their BAKED fixer had
                    // _forceRebuild=false and skipped them). Rebuilding every
                    // material as a clean URP/Lit from its real basecolor + maps is
                    // what makes colour reliable. Normal + emission are preserved
                    // below, so this is non-destructive for materials that already
                    // rendered correctly (no regression on working models).

                    Texture tex = null;
                    Color col = Color.white;
                    if (src != null)
                    {
                        // Synty / PolygonFantasyKingdom graphs bind colour on _Albedo_Map, not
                        // _MainTex/_BaseMap. Asking only those two names is how a Default Town
                        // LightSkin rebuilt to URP/Lit with NO map and rendered flat white
                        // (Seeker 365875, 2026-09-11 21:17:44 [Flow:TripoMatFix] NO ALBEDO).
                        tex = DependencyClosureTrace.GetAlbedo(src);
                        if (src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");
                        else if (src.HasProperty("_Color")) col = src.color;
                    }
                    // Tripo Phong materials sometimes export _Color as
                    // transparent black (0,0,0,0) — that'd render the rebuilt
                    // URP material as invisible. Treat near-zero alpha or
                    // near-black as "no useful source colour" and default to
                    // white so the texture or fallback tint controls the look.
                    if (col.a < 0.05f || (col.r + col.g + col.b) < 0.05f)
                        col = Color.white;
                    if (tex == null && fallbackTex != null) tex = fallbackTex;
                    // WO-719: forced albedo WINS over source + fallback (the extracted arcane-tower
                    // source map renders white). Baked into this shared rebuild -> sticks in the build.
                    if (forcedTex != null) tex = forcedTex;
                    // WO-1326 FLAT COAT: last word on the base map, after source/fallback/forced,
                    // so no earlier resolution can sneak a map back in. tex == null here means the
                    // tint below IS the albedo — the identical result the WebGL payload produced
                    // by accident, now produced on purpose on all three targets.
                    if (_suppressBaseMap) tex = null;
                    // Owner 2026-05-20 ("still grey"): the fallback tint was
                    // only applied when tex == null, but Tripo's source
                    // material often has a _MainTex reference pointing at a
                    // broken/embedded texture URP renders as white. Apply the
                    // tint whenever it's been set — when a real texture also
                    // resolves the tint just multiplies (mild colour push).
                    if (_hasFallbackTint) col = _fallbackTint;

                    // WHITE-STRUCTURE FALLBACK (ballista fix 2026-07-19): after ALL texture resolution,
                    // a slot that STILL has no map (tex == null) would rebuild as a solid WHITE URP/Lit
                    // (col defaulted to white above). For structures — which set no unconditional species
                    // tint — that is the "white ballista": a model whose albedo didn't survive the build
                    // (gitignored .fbm / untextured embedded material). Degrade the would-be white to the
                    // registered neutral stone MISS-tint. Textured slots (tex != null) never reach here,
                    // so they are byte-unchanged; an explicit _fallbackTint still wins when both are set.
                    // ⚠ `!_suppressBaseMap`: the miss-tint rescues an ACCIDENTAL texture miss. A
                    // flat coat is a DELIBERATE one, so its registered tint must not be replaced
                    // by the neutral stone colour — that would silently repaint the very look the
                    // owner chose. Nothing ships both today; the guard states the precedence anyway.
                    if (tex == null && _hasMissTint && !_suppressBaseMap) col = _missTint;

                    // Preserve the normal map always (non-destructive).
                    Texture nrm = (src != null && src.HasProperty("_BumpMap")) ? src.GetTexture("_BumpMap") : null;

                    // Resolve the FINAL emission inputs (override for pets, else the
                    // source's own emission for buildings) BEFORE keying the cache, so
                    // the tuple fully determines the built material.
                    Texture emMap = null;
                    Color emColor = Color.black;
                    bool emissive = false;
                    if (_hasEmissionOverride)
                    {
                        emColor = _emissionOverride * EmissionOverrideIntensity;
                        emissive = true;
                    }
                    else if (src != null)
                    {
                        Texture em = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : null;
                        Color emc = src.HasProperty("_EmissionColor") ? src.GetColor("_EmissionColor") : Color.black;
                        if (em != null || emc.maxColorComponent > 0.01f)
                        {
                            emMap = em; emColor = emc; emissive = true;
                        }
                    }

                    // P0-2: one SHARED material per identical look. Two orcs with the same
                    // texture/tint/maps now reference the SAME Material instance -> SRP
                    // batching coalesces them + native-material churn stops on respawn.
                    string srcName = (src != null && src.name != null ? src.name : "Tripo") + " (URP)";
                    Material sharedMat = GetOrCreateSharedMaterial(
                        lit, tex, nrm, emMap, col, emColor, emissive, _smoothness, _metallic, srcName);
                    matsRef[i] = sharedMat;
                    slotsRebuilt++;
                });
                r.sharedMaterials = matsRef;
            }

            FlowTrace.Step("TripoMatFix",
                $"{gameObject.name}: rebuilt {slotsRebuilt} slot(s) across {renderers} renderer(s). " +
                $"matCache: {s_cacheHits} hit / {s_cacheNew} new, size={s_matCache.Count} (P0-2 shared-material win).");

            // V: post-rebuild VERIFY — every renderer this fixer covers must now be on a URP/Lit
            // shader (the result of `new Material(lit)`), NOT a Hidden/InternalError/Standard/legacy
            // shader (the exact magenta/pink/grey symptom). If ANY slot is still on a non-URP/error
            // shader, the rebuild did not take for it — FlowTrace.Fail so the run self-reports
            // instead of leaving the owner to spot magenta on a model.
            VerifyAllRenderersUrp();
        }

        // P0-2: get the ONE shared URP/Lit material for this exact look, building + caching
        // it on first sight. Identical-look slots (same maps/tint/emission/finish) all get the
        // same instance -> SRP batching + no per-spawn alloc. The built material is identical to
        // the old per-instance `new Material(lit)` result (same property writes, same order).
        private static Material GetOrCreateSharedMaterial(
            Shader lit, Texture tex, Texture nrm, Texture emMap,
            Color col, Color emColor, bool emissive, float smoothness, float metallic, string name)
        {
            var key = new MatKey(lit != null ? lit.GetInstanceID() : 0,
                                 Id(tex), Id(nrm), Id(emMap),
                                 col, emColor, emissive, smoothness, metallic);

            // A previously-cached material may have been destroyed by an explicit
            // Resources.UnloadUnusedAssets sweep — Unity's overloaded == catches that;
            // rebuild on a dead entry so we never assign a null/destroyed material.
            if (s_matCache.TryGetValue(key, out var cached) && cached != null)
            {
                s_cacheHits++;
                return cached;
            }

            var m = new Material(lit);
            m.name = name;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", col);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            if (nrm != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", nrm);
                m.EnableKeyword("_NORMALMAP");
            }
            if (emissive)
            {
                if (emMap != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emMap);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emColor);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
            // SEE-THROUGH JOINTS FIX (2026-07-02, same pipeline as the hero): Tripo self-rigged
            // bodies (orc family etc.) are OPEN SHELLS of separate parts; URP/Lit's default
            // back-face cull (_Cull=2) turns bend-joint shell separations (shoulders/knees/
            // elbows) into see-through holes. Render double-sided — the DEF-6 precedent
            // (HeroBodySwapper.RetargetMaterialsToUrp) — so shell interiors show instead of
            // holes. Uniform across the cache (not part of MatKey) — every rebuilt material
            // gets the same value, so cache identity is unaffected.
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // 0 = Off (double-sided)

            s_matCache[key] = m;
            s_cacheNew++;
            return m;
        }

        // V (TGVRU): assert every renderer slot under this fixer ended on a URP shader after Run().
        // A slot still on Hidden/InternalErrorShader (magenta), Standard/Legacy (grey/pink under URP),
        // or a null shader means the rebuild silently failed for it — roll it up to the break-log.
        // Pure read-only inspection; always runs (control-flow safety, not behind a render check).
        private void VerifyAllRenderersUrp()
        {
            int checkedSlots = 0, broken = 0, untextured = 0;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    checkedSlots++;
                    string sn = (m != null && m.shader != null) ? m.shader.name : null;
                    bool isUrp = !string.IsNullOrEmpty(sn) &&
                                 (sn.StartsWith("Universal Render Pipeline/") || sn.StartsWith("URP/"));
                    bool isError = !string.IsNullOrEmpty(sn) &&
                                   (sn.Contains("InternalErrorShader") || sn.Contains("Hidden/"));
                    // Android white/magenta slab: a shader that fails to COMPILE on-device keeps a valid
                    // (even URP) name but renders magenta/white with isSupported==false — flag it too.
                    bool unsupported = m != null && m.shader != null && !m.shader.isSupported;
                    if (isUrp && !isError && !unsupported)
                    {
                        // THE SHADER IS NOT THE WHOLE ANSWER. This verify used to stop here and
                        // print VERIFY OK - which a URP/Lit material with NO ALBEDO BOUND passes
                        // cleanly while rendering as a flat dark/untextured mesh. That is the same
                        // false green as EnvTreeFix and the castle pink floor: the fix verified the
                        // wrong property and reported success.
                        //
                        // Report what is ACTUALLY BOUND, so a run produces DATA instead of an
                        // argument: material name, albedo texture name (or none), and the tint.
                        Texture albedo = DependencyClosureTrace.GetAlbedo(m);
                        Color tint = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                                   : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
                        string matName = string.IsNullOrEmpty(m.name) ? "<unnamed>" : m.name;

                        if (albedo == null)
                        {
                            untextured++;
                            // WO-1707: a Warn here never reaches break-log.jsonl / the F8 device
                            // bridge (both are error-level-only, per BreakCaptureHarness), so this
                            // line — the one place a general (non-arcane, non-forced-texture)
                            // structure's white render would be diagnosed — was invisible to every
                            // capture pulled for the seq5020/5023 white-town boots. No line here
                            // named the wall/house cause because THIS line never shipped as a Fail.
                            // Promote the literal-white case (no tint pushed off white either) so the
                            // next boot that reproduces it actually lands in the device's break-log.
                            // A non-white tint stays a Warn — that fallback is a deliberate degrade,
                            // not the bug.
                            bool rendersPureWhite = tint.r > 0.95f && tint.g > 0.95f && tint.b > 0.95f;
                            string msg = $"NO ALBEDO on '{gameObject.name}' renderer '{r.name}' slot {i}: material='{matName}' " +
                                $"shader='{sn}' tint=({tint.r:0.00},{tint.g:0.00},{tint.b:0.00}) - the URP rebuild took " +
                                "but bound NO base map, so this mesh renders as flat tint. A shader-only VERIFY calls this OK.";
                            if (rendersPureWhite)
                                FlowTrace.Fail("TripoMatFix", msg + " Tint is WHITE, not a designed miss-tint degrade — " +
                                    "this is the flat-white-structure symptom (WO-1707).");
                            else
                                FlowTrace.Warn("TripoMatFix", msg);
                        }
                        else
                        {
                            FlowTrace.Step("TripoMatFix",
                                $"albedo BOUND on '{gameObject.name}' renderer '{r.name}' slot {i}: material='{matName}' " +
                                $"tex='{albedo.name}' tint=({tint.r:0.00},{tint.g:0.00},{tint.b:0.00}).");
                        }
                        continue;
                    }

                    broken++;
                    FlowTrace.Fail("TripoMatFix",
                        $"VERIFY FAILED on '{gameObject.name}' renderer '{r.name}' slot {i}: shader='{sn ?? "<null>"}' " +
                        $"(supported={(m != null && m.shader != null ? m.shader.isSupported.ToString() : "n/a")}) " +
                        "is NOT a usable URP shader after rebuild (magenta/pink/grey/white risk) — the URP/Lit rebuild did " +
                        "not take for this slot. Mesh will render as the error/legacy/unsupported fallback.");
                }
            }
            if (broken == 0 && untextured > 0 && !_hasFallbackTint && !_hasMissTint && !_suppressBaseMap)
            {
                FlowTrace.Fail("TripoMatFix",
                    $"{gameObject.name}: VERIFY UNTEXTURED — all {checkedSlots} slot(s) on a URP shader, " +
                    $"but {untextured} slot(s) have NO albedo bound and no miss/fallback tint. " +
                    "That is the Default Town white LightSkin (shader-only VERIFY used to call this OK).");
            }
            else if (broken == 0)
                FlowTrace.Step("TripoMatFix",
                    $"{gameObject.name}: VERIFY OK — all {checkedSlots} slot(s) on a URP shader " +
                    $"(no magenta/error); {untextured} slot(s) with NO albedo bound.");
        }
    }
}
