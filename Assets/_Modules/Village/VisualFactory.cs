// =============================================================================
// VisualFactory — one runtime "skinner" for any gameplay object: enemy, animal,
// tower, structure, prop.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The same five steps were copy-pasted across the codebase whenever something
// needed a real mesh at runtime — load a model, instantiate it under a host,
// scale it to fit, seat it on the ground, fix its materials for URP, strip its
// stray colliders. This consolidates them behind ONE call:
//
//     VisualFactory.Skin(host, "Enemies/Skeleton_Warrior", SkinOptions.Enemy(1.9f));
//     VisualFactory.Skin(host, "Structures/Tower",          SkinOptions.Structure(17f));
//
// It is deliberately VISUAL-ONLY: it does not touch gameplay. Living things layer
// their animator on top afterwards (EnemyAnimatorFactory) — a static wall simply
// never asks for one. That keeps the skinner universal without pretending a tower
// is animated.
//
// Runtime/Resources-based (the editor scene baker has its own AssetDatabase path +
// the same fit/seat helpers; the two asmdefs can't share without a reference, so
// this mirrors that logic for the runtime side rather than forcing a dependency).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>How <see cref="VisualFactory"/> should dress a model. Use the
    /// <see cref="Enemy"/> / <see cref="Structure"/> / <see cref="Prop"/> presets,
    /// or set fields directly.</summary>
    public struct SkinOptions
    {
        public float FitHeight;          // >0 → scale so world-bounds HEIGHT = this
        public float FitLargest;         // >0 → scale so LARGEST world-bounds dim = this (wins over FitHeight)

        // FOOTPRINT CAP (owner F8 2026-08-20 "farm seems to be much larger than anything else").
        // >0 => after the height fit, if the widest HORIZONTAL world-bounds extent (max of x,z)
        // exceeds this many metres, scale DOWN uniformly so it equals it. A CAP, never an
        // enlargement, never non-uniform.
        //
        // WHY IT IS NEEDED: fit-to-HEIGHT is a SINGLE-AXIS promise executed as a UNIFORM scale, so
        // it silently drags the other two axes with it. A model whose FIT-TIME pose is flat blows
        // up: Structures/farm measures 0.977 x 0.391 x 1.000 m once its (-90,0,0) euler is applied
        // PRE-fit, so a 5.6 m height target divides by 0.391 and drags the 1.000 m plan axis to
        // 14.34 m — measured, against a 2.8–5.8 m family. Neither direction on heightMul fixes
        // that, because heightMul IS the thing being multiplied.
        // DEFAULT 0 = disabled = byte-identical behaviour for every existing caller.
        public float MaxFootprint;
        public bool  SeatOnGround;       // shift so the bounds base sits at the host's y
        public bool  StripColliders;     // remove the model's own colliders (the host owns its collider)
        public bool  FixTripoMaterials;  // attach DeNelle.Core.TripoMaterialFixer (Tripo→URP) via reflection

        // DEF-232: a fixed local orientation correction APPLIED BEFORE Fit / SeatOnGround.
        // Hero/companion bodies import facing +X and need a -90° yaw to face the root's +Z.
        // Callers used to apply that yaw AFTER Skin returned — but SeatOnGround had already
        // centred the (off-pivot) bounds over the root while the body was still at identity,
        // so the post-Skin rotation swung the off-centre mesh sideways (camera "stays to her
        // right", body "pivots in place" instead of translating). Set the yaw HERE instead:
        // the body is rotated FIRST, then fit + seated/centred in its FINAL orientation, so
        // the visible mesh sits dead-centre over the hero root regardless of import pivot.
        public Quaternion? LocalRotation;

        // FLAT-SEAT (owner F8 2026-07-04 "iron mine not sitting with flat side down"): derive a
        // natural resting orientation from the model's own geometry — rotate so its NARROWEST
        // world-bounds axis points to world +Y (CLAUDE.md §4: narrowest axis → up/down, so the
        // largest/flattest face rests on the ground). Applied AFTER LocalRotation and BEFORE
        // Fit/SeatOnGround, so the model is measured + seated in its final upright pose. Replaces
        // the guessed magic-euler tips (e.g. z:-129 / z:-90) that laid the harvest props on their
        // side. Robust to import pivot — measured live per instance, no per-prop hand-tuning.
        public bool SeatFlat;

        // WO-928: KEEP the prefab's own authored rotation instead of flattening it to identity.
        // The default below (identity when no LocalRotation is supplied) is DEF-232's and stays —
        // but it silently DESTROYS an orientation correction that was deliberately baked into a
        // prefab, which is how the L3 Archer Tower shipped lying on its side.
        //
        // Captured 2026-08-08, owner felt-test:
        //   after instantiate (prefab-native pose): euler=(270.00, 0.00, 0.00)   <- the bake is THERE
        //   after LocalRotation identity:           euler=(0.00, 0.00, 0.00)     <- and gone
        //   after Fit+SeatOnGround:                 scale=(8.34, 8.34, 8.34)
        //   skinned ... boundsSize=(4.91, 4.80, 8.34)
        // The second-order damage is worse than the wrong pose: once the model is flat, Fit measures
        // the WRONG AXIS to reach its height target, so the tower scales 8.34x instead of L1's 4.74x
        // and sprawls 8.34 m across a 3x3 m nav/physics blocker. Orientation and "the footprint is
        // huge" are ONE defect, not two.
        //
        // This is opt-in, not a default flip, precisely because DEF-232's identity is load-bearing for
        // hero/companion bodies (they import facing +X and are corrected via LocalRotation). It is also
        // NOT a per-FACTORY opt-in — see the ⛔ block on SkinOptions.Structure for the 2026-08-08 revert
        // that proved "structures" is far too wide a set. The ONE correct granularity is PER CATALOG
        // ENTRY: StructureFactory.OptsFor reads it off `entry.repo.preservePrefabRotation`, so a single
        // row whose art carries its correction in the asset opts in and every other row is byte-unchanged.
        // ARCHITECTURE_PRINCIPLES law 4: an authored/manual correction is canon and is NEVER
        // overwritten by an automatic pass. Flattening it to identity was exactly that overwrite.
        public bool PreservePrefabRotation;

        // WO-928 instrumentation: an OPTIONAL caller-supplied identity (the catalog entry id) stamped
        // into every Xform trace line this skin emits. `prefab.name` alone cannot answer "which ROW put
        // a sideways model on my screen" — several rows share one model (GenericContainer serves
        // lumberyard/foundry/silo; House_Medieval_Medium serves armorer AND collector_forge), and the
        // rotation POLICY is now per-row, so the row is the only thing that identifies the decision.
        // The owner should never have to felt-test a second sideways structure: with this, the
        // preserve-vs-identity branch is `grep "entry='<id>'"` away. Null/empty = unstamped (unchanged
        // line shape for every non-catalog caller: enemies, troops, props, hero bodies).
        public string TraceId;

        /// <summary>An enemy/creature: fit to height, strip its colliders (the root carries the trigger capsule).</summary>
        public static SkinOptions Enemy(float height) =>
            new SkinOptions { FitHeight = height, StripColliders = true };

        /// <summary>A tower/building: fit to largest dimension, seat on ground, URP-fix Tripo materials.
        /// DELIBERATELY leaves <see cref="PreservePrefabRotation"/> FALSE — the identity reset is the
        /// known-good default for the structure class as a whole. The rotation policy is a PER-ROW
        /// decision now and is applied by StructureFactory.OptsFor from
        /// <c>entry.repo.preservePrefabRotation</c>, never from this factory. Read the ⛔ block.</summary>
        // ⛔ PreservePrefabRotation was set TRUE **HERE** on 2026-08-08 and REVERTED the same day.
        //    DO NOT SET IT HERE AGAIN. It is set per catalog row, in StructureFactory.OptsFor.
        //
        // It fixed the L3 Archer Tower, whose baked -90 is the only thing standing it up. It also
        // reached EVERY structure, because StructureFactory.OptsFor builds from this factory - and
        // most Tripo building prefabs also instantiate at euler (270, 0, 0), where that same 270 is
        // precisely what DEF-232's identity reset exists to CANCEL. Preserving it laid the whole
        // town on its side.
        //
        // Captured (owner felt-test, returning to town from a dungeon):
        //     after instantiate (prefab-native pose): euler=(270.00, 0.00, 0.00)
        //     prefab rotation PRESERVED (WO-928):     euler=(270.00, 0.00, 0.00)   x10 structures
        //
        // It only surfaced on RE-ENTRY because the first town load seats buildings from the
        // bake/injector path; coming back re-creates them through BaseLayoutLoader ->
        // StructureFactory.Create -> here. A first-load-only test would have missed it entirely.
        //
        // WHY A BLANKET FLIP CANNOT WORK, stated as mechanism rather than as a caution: THIRTEEN
        // catalog rows already carry a manual orientation of exactly (-90, 0, 0) — tower_wall_wizard,
        // pet-house, workshop, market, forge, jeweler, arcane-tower, collector_farm,
        // collector_lumbermill, lumberyard, foundry, silo, barracks. Those rows are stood up by
        // StructureFactory.Create APPLYING that -90 on top of an identity-reset root. Preserve the
        // native 270 as well and the two COMPOSE to 180 — upside down, not merely sideways. Any row
        // with a non-zero manual orientation is therefore permanently ineligible for this flag.
        //
        // THE LESSON, and it is the reason this comment is long: an opt-in is only as narrow as the
        // thing you opt in. "Structures" is not a narrow set. The correct scope is PER CATALOG
        // ENTRY - the wooden watchtower ladder specifically - not per factory. See WO-928 defect A.
        public static SkinOptions Structure(float largest) =>
            new SkinOptions { FitLargest = largest, SeatOnGround = true, FixTripoMaterials = true };

        /// <summary>A small prop: fit to largest dimension, seat on ground.</summary>
        public static SkinOptions Prop(float largest) =>
            new SkinOptions { FitLargest = largest, SeatOnGround = true };
    }

    public static class VisualFactory
    {
        // PROD-022 Lane B — BOUND THE MISS-LOG STORM, WITHOUT EVER GOING SILENT.
        // The Fail below fires on EVERY Skin attempt, and a hub re-apply plus save replay can drive
        // several attempts per address per second. A Pi Browser session's final seconds were nothing
        // but the same four addresses cycling -> Skin / not found / <- Skin, which buries every other
        // line in the capture and costs bandwidth on a device that is already the suspect.
        // The shape here is deliberately ESCALATE-THEN-THROTTLE, never suppress:
        //   attempts 1..MissLogCap   -> full Fail, with the underlying network cause
        //   attempt  MissLogCap + 1  -> one Fail saying the cap is reached and what happens next
        //   thereafter               -> a throttled Fail-equivalent, ~1 per 10s per address
        // CLAUDE.md §12 is binding: instrumentation is permanent and a failure never becomes silent.
        //
        // PROD-022 (owner ruling 2026-09-02) — the cap is now REMOTELY TUNABLE, defaulting to the
        // 3 it has always been. A build with no `visuals.missLogCap` row behaves exactly as before;
        // the value can be moved from the database without a 30-minute WebGL rebuild. The registry
        // and the owner-facing list live in DeNelle.Core.Ops.RemoteTunables /
        // docs/PROD022_TUNABLE_FLAGS.md. Clamped to at least 1: a cap of zero would skip straight
        // to the throttle and lose the first, most informative Fail of every address.
        private const int DefaultMissLogCap = 3;
        private static int MissLogCap => Mathf.Max(1,
            DeNelle.Core.Ops.RemoteTunables.Int(DeNelle.Core.Ops.RemoteTunables.KeyVisualsMissLogCap));

        private static readonly Dictionary<string, int> s_missLogCounts = new Dictionary<string, int>();

        /// <summary>
        /// The one place a resolve-miss is reported. Escalates for the first few attempts, announces its
        /// own cap, then throttles — and always carries the UNDERLYING fetch cause when the warmer has
        /// one, so the reader is told WHY the bytes never arrived rather than merely that they did not.
        /// </summary>
        private static void ReportResolveMiss(string resourcesPath)
        {
            s_missLogCounts.TryGetValue(resourcesPath, out int n);
            n++;
            s_missLogCounts[resourcesPath] = n;

            // Cross-module read, null-conditional per CLAUDE.md §10. Non-structure addresses (enemies,
            // props, hero bodies) simply have no warmer record and report "none".
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

        /// <summary>Loads <paramref name="resourcesPath"/> from Resources and skins it
        /// under <paramref name="host"/>. Returns null (caller falls back) if absent.</summary>
        public static GameObject Skin(Transform host, string resourcesPath, SkinOptions opts)
        {
            // PROD-022 — the `-> Skin(...)` / `<- Skin(...)` pair is the single loudest thing in a
            // Pi Browser capture: a hub re-apply drives several attempts per address per second and
            // the observed final seconds were nothing but this pair cycling. It is NARRATION, so it
            // is dimmable by `trace.assetVerbosity` — default 2, which is today's behaviour, every
            // scope printed. At a lower level the scope is `default(FlowScope)`, whose Dispose is a
            // documented no-op (FlowTrace.cs:322 — `_active` is false), so nothing changes but the
            // volume. ⛔ Warn and Fail below are NEVER gated: CLAUDE.md §12 is binding and a failure
            // that stops being logged is the exact bug this instrumentation exists to prevent.
            using var _ = DeNelle.Core.Ops.RemoteTunables.Int(
                              DeNelle.Core.Ops.RemoteTunables.KeyTraceAssetVerbosity)
                          >= DeNelle.Core.Ops.RemoteTunables.VerbosityVerbose
                ? FlowTrace.Enter("VisualFactory", $"Skin('{resourcesPath}')")
                : default;

            // ⛔ RESOLVES THROUGH StructureAssetLoader, NOT Resources.Load.
            // This is the SINGLE point every structure visual flows through — StructureFactory.Create,
            // the tier-upgrade reskin and the build-preview probe all land here. Structure art moved
            // OUT of Resources into a remote Addressable group (2026-08-18), so a bare Resources.Load
            // here returns null for EVERY building: the town renders empty while every gate that does
            // not instantiate still passes. The seam is Addressables-first with a Resources fallback,
            // so this line is correct both before and after the migration.
            GameObject prefab = null;
            FlowTrace.Try("VisualFactory", $"resolve '{resourcesPath}'",
                () => prefab = DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(resourcesPath));

            if (prefab == null)
            {
                // §12: a missing model is a hard miss the caller falls back on — promote from a
                // swallowed Debug.LogWarning to FlowTrace.Fail so it rolls up to the break-log
                // (error severity) and a headless capture pinpoints the unresolved address.
                // ⚠ Post-CDN this line NAMES THE ADDRESS that failed, which is the whole benefit of
                // resolving by address rather than by path: a remote miss says exactly which asset
                // and which key, instead of leaving a silently empty spot in the world.
                // PROD-022 Lane B: routed through ReportResolveMiss so the line NAMES THE CAUSE
                // (UnityWebRequest result / HTTP status / timeout-vs-protocol, from the warmer) and so
                // the same address repeating cannot flood a session's final seconds. The wording
                // "model not found via Addressables OR Resources: '<addr>'" is preserved verbatim
                // because existing triage docs, greps and the WO's acceptance criterion match on it.
                ReportResolveMiss(resourcesPath);
                return null;
            }
            FlowTrace.Step("VisualFactory", $"resolved Resources model '{resourcesPath}' -> '{prefab.name}'.");
            return Skin(host, prefab, opts);
        }

        /// <summary>Instantiates <paramref name="prefab"/> under <paramref name="host"/>
        /// and applies the skin options.</summary>
        public static GameObject Skin(Transform host, GameObject prefab, SkinOptions opts)
        {
            if (prefab == null)
            {
                FlowTrace.Fail("VisualFactory", "Skin called with a null prefab — returning null (caller falls back).");
                return null;
            }

            using var _ = FlowTrace.Enter("VisualFactory", $"Skin(prefab='{prefab.name}')");

            // Guard the Instantiate: a broken/aborted prefab clone returns null rather than NRE'ing
            // every caller. Treated as a miss (Fail + null) so callers fall back, never get half-built.
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

            // XFORM VALUE-TRACE (owner 2026-07-08: "i want to see everything that happens to it
            // from selecting fbx to placement" — the euler ping-pong RCA): one line per mutation
            // stage with the ACTUAL local euler/pos/scale, so a single placement prints the whole
            // transform journey. Companion census: docs/STRUCTURE_TRANSFORM_CENSUS (agent).
            //
            // WO-928: the line is now stamped with the CALLER'S ENTRY ID as well as the model name.
            // The model name alone is ambiguous by construction — GenericContainer is the model for
            // lumberyard AND foundry AND silo, House_Medieval_Medium for armorer AND collector_forge —
            // and since the preserve-vs-identity branch below is decided PER ROW, the row is the only
            // thing that identifies which decision ran. A future sideways structure is now one grep
            // ("entry='<id>'" or "prefab rotation PRESERVED") away instead of one felt-test away.
            string who = string.IsNullOrEmpty(opts.TraceId)
                ? $"'{prefab.name}'"
                : $"'{prefab.name}' (entry='{opts.TraceId}')";
            // WO-1157 (owner F8 2026-08-24, "the ballista builds on its side"): the line now also
            // carries the MEASURED WORLD BOUNDS and the upright aspect at that stage. euler alone
            // cannot answer "is it standing" — a euler of (0,0,0) is upright for one model and flat
            // for the next, which is exactly how two orientation theories were argued from the same
            // trace and both were wrong. size + aspect are the numbers that decide it, so they are
            // printed beside the pose at EVERY mutation stage rather than derived afterwards.
            // aspect = height / max(width, depth): >1 tall-and-narrow, <1 flat-and-wide.
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

            // DEF-232: apply the caller's orientation BEFORE Fit/SeatOnGround so the body is
            // measured + centred in its FINAL facing. A post-Skin rotation (the old pattern)
            // swung the off-pivot bounds sideways. Default is identity (unchanged for callers
            // that don't pass LocalRotation, e.g. enemies/structures).
            // WO-928: an explicit LocalRotation always wins. Otherwise we only FORCE identity when the
            // caller has not asked us to preserve the prefab's authored pose — flattening a baked
            // correction is what laid the L3 Archer Tower on its side and then let Fit measure the
            // wrong axis (see SkinOptions.PreservePrefabRotation for the captured trace).
            if (opts.LocalRotation.HasValue)
                go.transform.localRotation = opts.LocalRotation.Value;
            else if (!opts.PreservePrefabRotation)
                go.transform.localRotation = Quaternion.identity;

            // The stage string IS the decision record: three mutually exclusive branches, each naming
            // WHY the pose is what it is, on a line that now also carries the entry id (see `who`).
            // "prefab rotation PRESERVED (WO-928, opt-in row)" is the only branch that can leave a
            // native Tripo 270 standing, so grepping it lists exactly the rows running the opt-in.
            TraceXform(opts.LocalRotation.HasValue ? "opts.LocalRotation"
                     : opts.PreservePrefabRotation ? "prefab rotation PRESERVED (WO-928, opt-in row)"
                     : "LocalRotation identity (DEF-232 default)");

            // FLAT-SEAT: derive the natural resting orientation from geometry (narrowest world-bounds
            // axis → +Y, §4) so the model sits flat side down. Runs BEFORE Fit/SeatOnGround so the
            // upright bounds are what gets fit + seated. Replaces guessed magic-euler tips.
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

                // AFTER the fit and BEFORE the seat: the cap is a ceiling on the fit's result, not
                // a second competing fit, and it changes height as well as width (uniform), so it
                // must land before the bounds base is dropped to the host.
                if (opts.MaxFootprint > 0f) CapFootprint(go, opts.MaxFootprint);

                // SeatOnGround centres the (now correctly-oriented) bounds over the host's x/z and
                // drops the bounds-base to the host's y — so the visible mesh sits dead-centre over
                // the hero ROOT, the transform the camera follows and HeroLocomotion drives.
                if (opts.SeatOnGround)
                    SeatOnGround(go, host != null ? host.position : go.transform.position);
            });
            TraceXform("Fit+SeatOnGround");

            // RENDER-VERIFY (owner directive 2026-06-19: "anything that renders can be broken — check
            // render==true and roll back the error"). This is the #1 shared choke point — every
            // enemy/troop/structure/prop/animal/companion body skins through here. A prefab that loads
            // but renders nothing (no enabled renderer, missing mesh, degenerate bounds) reads as a
            // grey/empty body to the player. PROVE it can render before handing it back; a broken build
            // logs Fail (rolls up to break-log) and is destroyed + treated as a MISS (return null) so
            // the caller falls back — we never hand back a render-broken-but-non-null body silently.
            if (!VerifyRenders(go, prefab.name))
            {
                Object.Destroy(go);
                return null;
            }

            // RIG-LEVEL DRESSABLE capability (BlinkWardrobe, owner architecture 2026-06-20): a body that
            // ships outfit-set renderers self-dresses to its default outfit HERE — beside the rig, the one
            // shared path every character skins through — so EVERY dressable humanoid (hero / companion /
            // arena fighter / future human-skinned enemy) starts CLOTHED, never in underwear. Non-dressable
            // bodies (skeletons / animals / structures) ship no outfit renderers → IsDressable=false → skip.
            // The data-driven per-character wardrobe + cosmetic-store feed land on this seam (WO-456).
            FlowTrace.Try("VisualFactory", "wardrobe default-dress",
                () => { if (BlinkWardrobe.IsDressable(go)) BlinkWardrobe.DressInStarter(go); });

            // WO-436 Step 1 (§12): surface the ACTUAL material name on the skinned body so a headless
            // capture PROVES Failure A (URP material not applied → the raw FBX surface renders as Unity's
            // solid unlit-green fallback) instead of guessing. Null-guarded: no renderer/material → Warn
            // (never a silent blank). sharedMaterial (not .material) — no per-instance material leak.
            FlowTrace.Try("VisualFactory", "material trace", () =>
            {
                var renderer = go.GetComponentInChildren<Renderer>();
                if (renderer == null || renderer.sharedMaterial == null)
                    FlowTrace.Warn("EnemyVisual", $"Material on {prefab.name}: NO renderer/material (would render blank/fallback)");
                else
                    FlowTrace.Step("EnemyVisual", $"Material on {prefab.name}: {renderer.sharedMaterial.name}");
            });

            // MAGENTA RECOVERY AT THE SPAWN SEAM (owner defect 2026-08-02: "raid troops are magenta").
            // PROVEN CAUSE (not a theory): MagentaGuard.Init is [RuntimeInitializeOnLoadMethod(
            // AfterSceneLoad)] + SceneManager.sceneLoaded, and its Sweep takes a ONE-TIME
            // Object.FindObjectsByType<Renderer>() SNAPSHOT. It has no Update and had no per-object
            // entry point. A raid troop is built MID-RAID (TroopDeployer.SpawnFromArmy ->
            // TroopFactory.Build -> here), i.e. after every sceneLoaded has already fired, so the
            // guard was structurally BLIND to it and the body stayed magenta forever.
            //
            // THIS overload is the choke point: the (Transform, string, SkinOptions) overload above
            // resolves the prefab then calls straight into this one (:95), and every runtime factory
            // — TroopFactory, EnemyFactory, StructureFactory, GhostPreview, HubStructureVisualInjector,
            // MineNodeVisual, HarvestSite, the station injectors, BuildPreviewModal,
            // StoryCompanionInjector — enters through one of those two. One hook covers them all.
            //
            // Placed AFTER VerifyRenders + the wardrobe dress so it sees the FINAL renderer set
            // (BlinkWardrobe toggles outfit renderers), and it is the last thing before the body is
            // handed back. SweepGameObject never throws and warns-not-errors on a missing art pack.
            FlowTrace.Try("VisualFactory", "magenta sweep (runtime spawn seam)",
                () => DeNelle.Core.MagentaGuard.SweepGameObject(go, "VisualFactory.Skin"));

            return go;
        }

        // RENDER-VERIFY: the instantiated body MUST carry >=1 ENABLED Renderer (SkinnedMeshRenderer or
        // MeshRenderer) with a non-null shared mesh AND non-degenerate world bounds. Traces the exact
        // counts so a headless capture splits "no enabled renderer" vs "missing mesh" vs "degenerate
        // bounds" with zero guessing. Returns false => caller (Skin) treats the build as a miss.
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

        // ── Geometry helpers ─────────────────────────────────────────────────
        /// <summary>Uniformly scales so the world-bounds HEIGHT (or largest dimension)
        /// equals <paramref name="target"/> — robust to arbitrary import scale.</summary>
        private static void Fit(GameObject go, float target, bool largest)
        {
            if (!TryBounds(go, out Bounds b))
            {
                // W (WO-1157): a silent return here left the model at its authored scale with
                // nothing saying the fit never ran. Never silent.
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

            // WO-1157 (§12): the fit is where a mis-ORIENTED model becomes a mis-SCALED one as well,
            // because fit-to-height divides by whatever axis happens to be vertical AT THIS MOMENT.
            // Printing the measured axis, the whole bounds and the resulting factor makes that
            // coupling readable in one line: a lying-down model shows a small `measure` and a wildly
            // large factor, which is the signature the archer-tower thread had to re-derive by hand.
            float k = target / measure;
            FlowTrace.Step("VisualFactory",
                $"Fit('{go.name}'): mode={(largest ? "largest" : "height")} measured={measure:0.###}m " +
                $"of bounds ({b.size.x:0.###} x {b.size.y:0.###} x {b.size.z:0.###}) " +
                $"target={target:0.###}m -> scale x{k:0.####} (from {go.transform.localScale.x:0.####}).");

            go.transform.localScale *= k;
        }

        // ── Seat verification (§12: the next float NAMES ITSELF) ─────────────
        //
        // ⚠ READ THE TRACE IN WORLD SPACE, NOT IN THE Xform LINE'S localPosition.
        // The "[Flow:Xform] ... after Fit+SeatOnGround: pos=(0.00, 2.00, 3.13)" line prints
        // transform.localPosition — the position of the model's PIVOT relative to its host, NOT
        // the height of its bottom above the ground. A model whose pivot sits at the centre of a
        // 4 m body seats CORRECTLY at local y = +2.00: that is exactly the lift needed to put the
        // bounds BOTTOM on the host's y. Reading that 2.00 as "floating 2 m" is a misdiagnosis
        // that has cost a session already (2026-08-20 portal triage). The only number that can
        // decide the question is bounds.min.y vs the ground plane — which is what this pair of
        // helpers measures and prints, so nobody has to re-derive the pivot maths from a log.
        //
        // Epsilon: a seat is "on the ground" when its bounds bottom is within this of the ground
        // plane. Renderer bounds are a loose world AABB and a fitted 4 m building carries a few
        // mm of float error, so a hard == would cry wolf on every correct seat. 5 cm is well under
        // anything a player can perceive as a gap and well over the numeric noise.
        public const float SeatEpsilonMetres = 0.05f;

        /// <summary>
        /// TRUE when <paramref name="go"/>'s world-bounds BOTTOM rests within
        /// <see cref="SeatEpsilonMetres"/> of <paramref name="groundY"/>. <paramref name="bottomY"/>
        /// receives the measured bottom (NaN when the object has no measurable bounds — which is
        /// itself a fail, since an unmeasurable object cannot have been seated).
        /// <para>PUBLIC on purpose: this is the one definition of "seated", shared by the runtime
        /// seat below and by <c>StructureSeatRegression</c>, so the gate cannot drift from the game.</para>
        /// </summary>
        public static bool IsSeatedOnGround(GameObject go, float groundY, out float bottomY,
                                            float epsilon = SeatEpsilonMetres)
        {
            bottomY = float.NaN;
            if (go == null || !TryBounds(go, out Bounds b)) return false;
            bottomY = b.min.y;
            return Mathf.Abs(bottomY - groundY) <= epsilon;
        }

        /// <summary>Shifts the object so its bounds base sits at <paramref name="basePos"/>.y
        /// (centred on basePos.x/z).</summary>
        /// <summary>
        /// Uniformly scales DOWN (never up) so the widest horizontal world-bounds extent (max of
        /// x,z) is at most <paramref name="maxMetres"/>. Runs AFTER <see cref="Fit"/>, so it is a
        /// ceiling on that fit rather than a second competing fit; proportions are preserved.
        /// </summary>
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
            // W: an unmeasurable body used to return here in SILENCE, leaving the model wherever
            // Fit left it — the exact "it floats and nothing said so" class §12 exists to kill.
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

            // VERIFY THE SEAT ACTUALLY LANDED (§12). The shift above is correct by construction
            // *given the bounds it measured*; the failure mode is that the measurement was stale or
            // degenerate (skinned-mesh bounds before the first pose, a renderer that reports an
            // empty AABB). Re-measuring after the move is the only thing that proves the bottom is
            // on the plane — and it makes the offending object print its OWN name and its OWN
            // offending Y, so the next occurrence is one grep away instead of one felt-test away.
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

        /// <summary>Levels a prop to rest on its flattest face: rotates (world space) so the
        /// NARROWEST world-bounds axis points to world +Y (CLAUDE.md §4). This is the runtime
        /// twin of CatalogOrientationBaker's bake-time keep-vertical heuristic — a geometry
        /// derivation, not a hand-authored euler. A model already flat (Y narrowest) is left
        /// untouched. Measured from live world bounds so it is robust to any import pivot.</summary>
        private static void SeatFlat(GameObject go)
        {
            if (go == null || !TryBounds(go, out Bounds b)) return;
            Vector3 sz = b.size;

            // Index of the SHORTEST world-bounds axis (0=X, 1=Y, 2=Z). That axis should be vertical
            // so the two longer axes span the ground-resting face.
            int shortest = (sz.x <= sz.y && sz.x <= sz.z) ? 0 : (sz.y <= sz.z ? 1 : 2);
            if (shortest == 1)
            {
                FlowTrace.Step("VisualFactory",
                    $"SeatFlat('{go.name}'): already flat (Y narrowest, size {sz:F2}) — no rotation.");
                return;
            }

            // Bring the current shortest WORLD axis onto world +Y with the shortest-arc rotation,
            // pre-multiplied so it applies in world space (parent may be rotated).
            Vector3 shortAxis = shortest == 0 ? Vector3.right : Vector3.forward;
            Quaternion delta = Quaternion.FromToRotation(shortAxis, Vector3.up);
            go.transform.rotation = delta * go.transform.rotation;

            FlowTrace.Step("VisualFactory",
                $"SeatFlat('{go.name}'): narrowest axis was {(shortest == 0 ? "X" : "Z")} " +
                $"(size {sz:F2}) → stood it to +Y so the flat face rests down.");
        }

        // ── Tripo material fix ──────────────────────────────────────────────
        // WO-1511: TripoMaterialFixer lives in DeNelle.Core, which DeNelle.Village.asmdef
        // already references — so the type is directly nameable and the cached
        // Type.GetType lookup (plus its "type missing" null path) is gone. Behaviour is
        // identical: add the component once, never twice.
        private static void TryAddTripoFixer(GameObject go)
        {
            var fixer = go.GetComponent<DeNelle.Core.TripoMaterialFixer>();
            if (fixer == null) fixer = go.AddComponent<DeNelle.Core.TripoMaterialFixer>();
            // Same stone miss-tint StructureFactory sets. Hub LightSkins go through THIS
            // path, not StructureFactory, so a remaining texture miss degrades to stone
            // instead of bright white (Default Town 365875).
            fixer.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
        }
    }
}
