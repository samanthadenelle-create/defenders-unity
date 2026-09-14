// =============================================================================
// HubStructureVisualInjector — runtime visual swap of baked castle-hub structures to
// lightweight Resources models (owner 2026-06-17), WITHOUT a scene rebuild.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// MainCastle_Hall bakes its 8 structures from polyperfect/Quaternius prefabs via
// CastleHubBuilder. As the owner authors LIGHTWEIGHT replacement models (tiny Tripo
// FBX, dropped into Assets/Resources/Structures/), this injector swaps them in at
// runtime — the project's no-scene-edit pattern (CampSystem / StoryCompanionInjector).
// On every hub load it finds each baked structure by name, hides its renderers
// (keeping the NPC interact point + colliders/logic), and skins the model in via
// VisualFactory.Skin, which BOUNDS-FITS the raw FBX to a target size, SEATS it on the
// ground, and URP-FIXES embedded Tripo materials (so it never renders magenta — which a
// CastleHubBuilder bake would, since it never fixes materials).
//
// TO REPLACE ANOTHER STRUCTURE: drop the model in Resources/Structures and add ONE row
// to Swaps below — { baked structure NAME, model path, height multiplier, yaw° }. WO-764:
// every landmark is fit-to-HEIGHT (StructureFactory.YHeightVariable × heightMul, uniform 1.0
// base) exactly like a player-built catalog structure — no more per-item hand-dialed sizeM. The
// baked names (from CastleHubBuilder) are:
//   Blacksmith_Weapons_Storefront · Lumbermill_Wood_Storefront · Windmill_Food_Storefront
//   EchoHollow_Pets_RoamingArea · Forge_Armor_Storefront · ArcaneTower_MagicUpgrades
//   Marketplace_Monetization
//
// Idempotent (a marker child guards re-swaps) + graceful (model missing → the baked
// visual is restored, nothing breaks).
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Buildings.Progression;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>Re-skins baked hub structures with lightweight Resources models at runtime.</summary>
    public static class HubStructureVisualInjector
    {
        /// <summary>One structure re-skin. Add a row per lightweight model authored.</summary>
        private struct Swap
        {
            public string bakedName;   // the structure's baked GameObject name (CastleHubBuilder)
            public string modelPath;   // Resources path of the lightweight model
            public float  heightMul;   // WO-764: Y-height multiplier vs StructureFactory.YHeightVariable
                                       // (4 m base). 0/unset -> 1.0 (uniform base); towers author 1.25.
            public float  yawDeg;      // Y rotation to correct a wrong-facing Tripo FBX (convention: 90)
            public float  pitchDeg;    // X rotation — when a model imports lying down (default 0)
            public float  rollDeg;     // Z rotation — rarely needed (default 0)
            public float  posY;        // vertical nudge after seat-on-ground (default 0; e.g. -0.6 to sink it)
            public float  posX;        // used only with setLocalPos
            public float  posZ;        // used only with setLocalPos
            public bool   setLocalPos; // true -> SET localPosition to (posX,posY,posZ), overriding the
                                       // seat (for a model that belongs at a specific spot); false -> posY is a nudge.
            public string texPath;     // OPTIONAL forced albedo, resolved through StructureAssetLoader
                                       // (WO-1327 - NOT Resources.Load: Assets/Resources/Structures was
                                       // deleted by the CDN migration, so a Resources-only load returns
                                       // null in every player build). Same key string as the addresses
                                       // in the Structure_Art group. Forced onto the model when its
                                       // embedded material didn't bind one (renders colorless). Default null.
            public float  scaleX;      // OPTIONAL explicit (non-uniform) local scale. When scaleX>0 it
            public float  scaleY;      // OVERRIDES the fit-to-height with (scaleX,scaleY,scaleZ) — for a
            public float  scaleZ;      // model the owner sized by hand. Default 0 -> use the height fit.
        }

        // ── THE SWAP TABLE — add a row per lightweight structure ──────────────────
        private static readonly Swap[] Swaps =
        {
            // CONVENTION (owner 2026-06-17): these are Tripo FBX exports — they import facing +X, so
            // ALL need yawDeg=90 to face the plaza, and their embedded materials are URP-fixed
            // automatically by SkinOptions.Structure (FixTripoMaterials). Keep new Tripo rows at yaw 90.
            // Trade convention (WO-1250): Blacksmith_Weapons_Storefront wears Structures/Forge
            // (catalog id "forge" = Weaponsmith); Forge_Armor_Storefront wears Structures/armorer
            // (catalog id "armorer" = Armorer). Store = Market.
            // WO-764: NO sizeM — every landmark is fit-to-HEIGHT (YHeightVariable × heightMul). All
            // buildings inherit the uniform 1.0 base (heightMul unset). THERE IS NO LONGER AN
            // EXCEPTION. The arcane tower / Cathedral of Magic sat at 1.25 as a deliberate
            // "landmark tier, the town's single apex" call in the 2026-08-05 cadence pass;
            // the owner overruled it on sight the next day ("why is the cathedral of magic so
            // large? Normalize"). It is a GameplayBuilding despite the tower-ish name, so it
            // belongs on the building base with everything else. The TOWER class is 1.2, the
            // owner-ruled archer anchor - do not resurrect a landmark tier from it.
            // yaw/pitch/roll/pos/scale hand-dials are orientation/placement,
            // untouched. (Jeweler scaleX CLEARED 2026-08-19 — owner upright (90,0,0) + uniform FitHeight;
            // post-Fit non-uniform scale was flipping it upside-down on new-town injector reload.)
            new Swap { bakedName = "EchoHollow_Pets_RoamingArea",   modelPath = "Structures/PetHouse2",    yawDeg = 0f,   pitchDeg = -90f, rollDeg = 270f },   // owner hand-dialed 2026-06-21
            new Swap { bakedName = "ArcaneTower_MagicUpgrades",     modelPath = "Structures/arcane tower", yawDeg = 0f,   pitchDeg = -90f, posY = -0.6f, texPath = "Structures/ArcaneTower_Albedo" },   // NORMALIZED 2026-08-06 (owner F8: "why is the cathedral of magic so large? Normalize"). heightMul was 1.25f here, hardcoded, which is what she was LOOKING AT - the hub scene injects its own swap, so the catalog row alone would not have moved it and the fix would have read as ineffective. The landmark-tier exception is retired; unset = the uniform 1.0 building base, matching the catalog row. DEF-arcane-white: texture moved OUT of the nested "arcane tower/" folder (its name collided with the sibling "arcane tower.fbx" so Resources.Load<Texture2D> returned null -> forced-texture no-op -> pure-white spire). Flat path resolved in Resources at the time - but the CDN migration then DELETED Assets/Resources/Structures, so Resources.Load<Texture2D> went back to returning null (device proof: "[HubStructureVisualInjector] texPath 'Structures/ArcaneTower_Albedo' not found for ArcaneTower_MagicUpgrades", logs/device/2026-08-20-*.log) and the Cathedral was white again. WO-1327 routes it through StructureAssetLoader; the address is registered in Structure_Art.
            // ═══ AXIS-BAKE, SECOND CHANNEL — retired here 2026-08-18 (this injector) ═══
            // Commit f995c4706 set bakeAxisConversion:1 on the Tripo structure FBXs, so the
            // upright correction now lives IN THE MESH. Earlier today the CATALOG channel
            // (structures-catalog.json entry.orientation) had its -90 X removed for the rows whose
            // model is baked. THIS TABLE IS A SECOND, INDEPENDENT CHANNEL: SkinStorefront/TryPlace
            // feed pitchDeg straight into opts.LocalRotation (VisualFactory.cs:229-232), which is
            // applied BEFORE Fit — so a baked model carrying a -90 here was rotated TWICE: it lay
            // down AND the fit-to-height then measured its SHORT axis, mis-scaling it. That is the
            // hovering, tapered-underside building in the device capture.
            //
            // THE RULE, per-model from .fbx.meta — NEVER blanket (GROK_BRIEF 2026-08-19):
            //   bakeAxisConversion: 1 -> imported root is (90,0,0), BUT VisualFactory.cs:236
            //     zeros that for non-PreservePrefabRotation rows. The upright MUST live on
            //     opts.LocalRotation = pitchDeg 90 (owner-dialed). pitchDeg 0 lays them on their faces.
            //   bakeAxisConversion: 0 -> the -90 is CORRECT and LOAD-BEARING, do not touch
            //                            (PetHouse2, arcane tower, store, lumbermill, farm, arena)
            // Jeweler: owner felt X=90 Y=0 Z=0 upright. Do NOT keep the old non-uniform
            // scaleX/Y/Z here — SkinStorefront applies that AFTER LocalRotation/Fit and it
            // re-tips the model upside-down on new-town load (bake without that scale looked
            // perfect). Clear scaleX so FitHeight 4 m matches Forge/armorer/barracks.
            new Swap { bakedName = "Blacksmith_Weapons_Storefront", modelPath = "Structures/Forge",        yawDeg = 180f, pitchDeg = 90f,  rollDeg = 0f },   // owner 2026-08-19: X=90 upright (Forge.fbx bakeAxisConversion:1); yaw kept
            new Swap { bakedName = "Forge_Armor_Storefront",        modelPath = "Structures/armorer",      yawDeg = 90f,  pitchDeg = 90f },  // same family; X=90 upright
            new Swap { bakedName = "Marketplace_Monetization",      modelPath = "Structures/store",        yawDeg = 90f,  pitchDeg = -90f }, // bake:0 — KEEP -90 (§6 negative control)
            new Swap { bakedName = "Jeweler_Gems_Storefront",       modelPath = "Structures/jeweler",      yawDeg = 0f,   pitchDeg = 90f,  rollDeg = 0f },   // owner: Rotation (90,0,0). PROVEN by render 2026-08-20 (Builds/StorefrontCaps): +90 is roof-up, -90 is upside down. The real upside-down cause was the post-Fit non-uniform scaleX, cleared in cd0d109b8 - never re-invert this sign to chase it.
            new Swap { bakedName = "Lumbermill_Wood_Storefront",    modelPath = "Structures/lumbermill",   yawDeg = 0f,   pitchDeg = -90f, posY = 1.5f }, // bake:0 — KEEP -90
            new Swap { bakedName = "Windmill_Food_Storefront",      modelPath = "Structures/farm",         yawDeg = 0f,   pitchDeg = -90f, rollDeg = 212f },   // bake:0 — KEEP -90
            // Castle barracks = the troop-TRAINING building (existing scene prefab "CastleBarracks");
            // visual swap only — its training function is already wired. Owner 2026-08-19: X=90.
            new Swap { bakedName = "CastleBarracks",                modelPath = "Structures/barracks",     yawDeg = 180f, pitchDeg = 90f, setLocalPos = true, posX = 38.3f, posY = 0f, posZ = 36f },
            // (ArenaMonument was deleted; the colosseum is a NEW placement at the arena herald
            //  spot (15,0,6) — see the Places table below, not a swap.)
        };

        private const string MarkerPrefix = "LightSkin_";   // child added on swap (idempotency guard)

        /// <summary>
        /// Hosts placed by <see cref="TryPlace"/> this session (e.g. the colosseum). The
        /// AutoPilot PROP-SEATING oracle walks this list and asserts each prop's visual
        /// base sits on the floor beneath it (owner F8 2026-07-02 "in ground not on ground").
        /// Dead entries are pruned by the reader; never cleared here (idempotent adds).
        /// </summary>
        public static readonly List<GameObject> RuntimePlacedProps = new List<GameObject>();

        /// <summary>A NEW model dropped at a world position (no baked structure to swap).</summary>
        // ── THE ONE BLANKET GROUND-Y (owner directive 2026-07-04, the One-Model way) ──
        // Category-wide rule for building/structure type: EVERY hub structure this injector
        // places has its bounds-base pinned to this single ground-Y, ONE time at placement —
        // no terrain sample, no navmesh, no raycast (all proven-brittle; the raycast floated
        // the Colosseum ~5m off overhead castle geometry). The merged Main_Castle_Overworld is
        // a KNOWN flat plane at y=0 (WorldMergeBuilder.LowerCastleToGround lowers the castle
        // floor coplanar with the terrain ring at y=0), which is ALSO the Tree of Life's proven
        // seat value — so the whole building category grounds off this ONE variable. If a future
        // scene isn't flat, change this in ONE place (or gate it per-scene here).
        private const float GroundY = 0f;

        private struct Place
        {
            public string  name;       // unique object name (idempotency guard)
            public string  modelPath;
            public Vector3 worldPos;
            public float   heightMul;  // WO-764: Y-height multiplier vs StructureFactory.YHeightVariable
                                       // (4 m base). 0/unset -> 1.0 (uniform base). Mirrors Swap.heightMul.
            public float   yawDeg;
            public float   pitchDeg;
            public float   rollDeg;    // Z rotation (owner hand-dial; default 0) — mirrors Swap.rollDeg
            public float   scaleX;     // OPTIONAL explicit non-uniform scale; scaleX>0 OVERRIDES the height
            public float   scaleY;     // fit with (scaleX,scaleY,scaleZ) — for a model the owner sized by
            public float   scaleZ;     // hand. Default 0 -> use the height fit. Mirrors Swap.scaleX/Y/Z.
        }

        // The old ArenaMonument was deleted; place the colosseum (arena.fbx) at the arena herald spot
        // (ArenaHeraldSpawner.HeraldOffset = 15,0,6). The herald already provides the "Enter Arena"
        // interaction within range, so co-locating the colosseum there makes it the arena ENTRANCE —
        // visual here, interaction from the herald. No new entry code needed.
        private static readonly Place[] Places =
        {
            // Owner 2026-07-03 ("coliseum and the jeweler are too large, scale both down 50%"):
            // halved the owner-hand-dialed scale (10.6/8.4/10.53 -> 5.3/4.2/5.265). The explicit
            // scale override in TryPlace runs AFTER VisualFactory's SeatOnGround, so a smaller scale
            // lifts the bounds base off the floor — SetBottomToGround (called in TryPlace) then pins
            // the bottom to the scripted GroundY (0) so the ring sits ON the ground, not floating.
            new Place { name = "Colosseum_ArenaEntrance", modelPath = "Structures/arena",
                        worldPos = new Vector3(-0.39f, 0f, 23.1f), heightMul = 1f,
                        yawDeg = 0f, pitchDeg = -90f, rollDeg = 90f,
                        scaleX = 5.3f, scaleY = 4.2f, scaleZ = 5.265f },   // WO-764: heightMul 1.0 uniform base, BUT the explicit scaleX below still supersedes the height fit (owner hand-dialed proportions) — clear scaleX to make it obey uniform height. 50% of owner hand-dialed 2026-06-21
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (HubScenes.IsHub(SceneManager.GetActiveScene().name)) ApplyAll();
        }

        // ⛔ P0 HANG FIX, 2026-08-20 — DO NOT PUT ApplyAll() BACK INLINE HERE.
        // The captured hang was three minutes of total process silence whose last line was
        //   [Flow:VisualFactory] -> Skin('Structures/barracks')
        // with THIS METHOD on the stack. The chain was
        //   Internal_SceneLoaded -> OnSceneLoaded -> ApplyAll -> TrySwap -> SkinStorefront
        //     -> VisualFactory.Skin -> StructureAssetLoader -> Addressables WaitForCompletion()
        // and WaitForCompletion is an uninterruptible spin that needs the ResourceManager to be
        // pumped from the PLAYER LOOP. Inside a nested engine callback the player loop cannot
        // re-enter, so the thread that would finish the operation is the thread waiting on it.
        // It was NOT a slow network (CDN pinged 2/2, 31.5 ms, while the device was hung).
        //
        // Two independent fixes, and this is the structural one: get OFF the engine callback.
        // StructureAssetLoader no longer blocks at all (see its header), so this deferral is
        // belt-and-braces — but it is the belt that survives someone re-introducing a blocking
        // load somewhere further down the chain, so it stays.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!HubScenes.IsHub(scene.name)) return;
            s_warmReapplies = 0;
            // WO-1327: the skinned visuals died with the previous scene, so no forced albedo
            // survives into this load. Clearing here is what lets a hub re-entry re-attempt a
            // texture that missed last time.
            s_texBound.Clear();
            s_texRetryArmed.Clear();
            DeNelle.Core.StructureContentWarmer.Defer(ApplyAll);
        }

        // Re-apply budget per hub load. Skins that were skipped because their art was not yet
        // resident get one more chance once the warmer settles; capped so a genuinely missing
        // address cannot spin ApplyAll forever.
        private const int MaxWarmReapplies = 3;
        private static int s_warmReapplies;
        private static bool s_warmReapplyArmed;

        // WO-1327 (Cathedral of Magic renders PURE WHITE on device). Baked names whose forced
        // albedo has actually been BOUND to a material this session. Separate from the
        // LightSkin_ marker on purpose: the marker means "the MODEL was skinned", and the two
        // can succeed independently. Before this, a texture miss was permanently unrecoverable
        // because SkinStorefront returns early on the marker — so the warm re-apply pass
        // (armed for exactly this class of miss) walked straight past the white building.
        private static readonly HashSet<string> s_texBound = new HashSet<string>();
        // Baked names that already own a one-shot WhenSettled texture retry, so a repeated
        // ApplyAll cannot stack rival callbacks on the same structure.
        private static readonly HashSet<string> s_texRetryArmed = new HashSet<string>();

        /// <summary>
        /// WO-724: re-surface the baked CastleBarracks LIVE when the unlock flips true
        /// mid-session (founding completes in-hub with no scene reload). The scene-load
        /// swap ran while locked and deactivated the building, so <see cref="FindByName"/>
        /// (active-only) can no longer see it - this scans INCLUDING inactive, reactivates
        /// it, then re-runs its swap to skin the lightweight model + fit its collider.
        /// Idempotent + gated: a no-op unless the barracks is genuinely unlocked now, the
        /// player has NOT built their own (placed wins), and the WO-834 blank-town gate is
        /// open. Called by <see cref="BarracksNpcInjector"/>'s 1 Hz poll and by
        /// StructureSingleton's resurface branch (which only runs when nothing is placed).
        /// <para>RETURNS true when the baked barracks is STANDING once this call returns
        /// (reactivated here, or already standing and re-skinned); false when a gate refused
        /// it or no CastleBarracks exists in this scene. StructureSingleton.ResurfaceBakedTwins
        /// counts its surfaced= tally off this answer, so a refused resurface is never reported
        /// as work done — the F8 seq=651 lesson (a tally that overclaims hides the real bug).
        /// Callers free to ignore it: BarracksNpcInjector's poll does.</para>
        /// </summary>
        public static bool EnsureBarracksSurfaced()
        {
            if (!BarracksUnlock.IsUnlocked) return false;
            // PLACED WINS — StructureSingleton.Enforce's own precedence (it tests
            // HasPlacedInstance FIRST, StructureSingleton.cs:262). A player-built barracks
            // owns the singleton, and MarkEverBuilt at the commit seam
            // (BuildModeController.cs:1842) is precisely what OPENS MayBakedTwinSurface — so
            // the gate ALONE lets the player's own build resurface the baked twin (owner F8
            // seq=651 "seems like two barracks": place a barracks -> the 1 Hz poll below
            // reactivates CastleBarracks and nothing re-enforces afterwards). IsPlayerBuilt
            // reads BaseLayout/PlacedStructure/live Building and EXCLUDES baked twins
            // (HasPlacedInstance:422-441), so this check can never self-latch off the twin
            // it is suppressing.
            if (StructureSingleton.IsPlayerBuilt("barracks") &&
                !AuthoredCastleStorefront.IsBoundAuthoredRoot(AuthoredCastleStorefront.Find("CastleBarracks", true), "barracks")) return false;
            // WO-834 blank-town gate: on a Build-Your-Own (migrated, never-built) save the
            // baked CastleBarracks may NOT surface at unlock — the player builds their own
            // from the palette (first is free, WO-812). Default-Town/legacy saves carry the
            // template grant ('barracks' in EverBuiltStructureIds), so this is a no-op for them.
            if (!StructureSingleton.MayBakedTwinSurface("barracks")) return false;

            Transform barracks = AuthoredCastleStorefront.Find("CastleBarracks", true);
            if (barracks == null) return false;   // not in this scene (pack not imported / not a hub)

            if (!barracks.gameObject.activeSelf)
            {
                barracks.gameObject.SetActive(true);
                DeNelle.Core.Diagnostics.FlowTrace.Step("Barracks",
                    "EnsureBarracksSurfaced - reactivated the baked CastleBarracks (unlock flipped true live).");
            }

            for (int i = 0; i < Swaps.Length; i++)
                if (Swaps[i].bakedName == "CastleBarracks") { TrySwap(Swaps[i]); break; }

            // Report the OBSERVED end state, not the intent: TrySwap runs its own gates and
            // the WO-673 migration standdown branch can deactivate the object again. Reading
            // activeSelf back is the only answer that cannot overclaim.
            bool stands = barracks.gameObject.activeSelf;
            // WO-950: a twin that legitimately STANDS gets its physics back (a prior
            // gate-suppression stripped colliders + nav obstacles). Restore re-asserts
            // the Ticket #10 setLocalPos rule, so the baked body collider stays down
            // while the visible skin + its fitted StructureCollider stand elsewhere.
            if (stands) RestoreBakedTwinPhysics(barracks.gameObject, "CastleBarracks");
            return stands;
        }

        private static void ApplyAll()
        {
            // Owner 2026-09-12: Courtyard_PlazaFallback (80x80 lime plane) OVERWROTE the
            // yard and the ring. Do not spawn it. Kill any leftover from a prior Play.
            // EnsureCourtyardPlazaVisual();
            RemoveRetiredCourtyardPlaza();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var authored in root.GetComponentsInChildren<AuthoredCastleStorefront>(true))
                    PrepareAuthoredStorefront(authored);
            for (int i = 0; i < Swaps.Length; i++) TrySwap(Swaps[i]);
            for (int i = 0; i < Places.Length; i++)
            {
                // WO-703 / BLANK-1 (owner ruling 2026-07-13 "should be completely flagged off
                // for now"): the colosseum placement is gated behind its own default-OFF flag.
                // Skipping the placement here means the model, the fitted StructureCollider,
                // and anything later parented to the host never exist — reversible via
                // PlayerPrefs "ff.colosseum" = 1 (FeatureFlags.Colosseum).
                if (Places[i].name == "Colosseum_ArenaEntrance" && !DeNelle.Core.FeatureFlags.Colosseum)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                        "standdown Colosseum_ArenaEntrance (ff.colosseum OFF — WO-703/BLANK-1 ruling: " +
                        "fresh start = tree + well + walls/gates only; flag ON to restore).");
                    continue;
                }
                TryPlace(Places[i]);
            }

            // P0 HANG FIX, 2026-08-20 (the graceful half). StructureAssetLoader now SKIPS rather
            // than waits when structure art is not yet resident, and SkinStorefront already
            // degrades correctly on that null — it re-enables the baked renderers and leaves the
            // baked twin standing, writing NO LightSkin_ marker. So a skip is recoverable, and the
            // recovery is armed HERE, after the passes, on the one signal that says a skip actually
            // happened: the loader issues an async Request per skip, so PendingRequests > 0 means
            // at least one structure went unskinned this pass. When those land, re-apply.
            // A slightly wrong-looking building for a second beats a dead game for three minutes.
            if (!s_warmReapplyArmed && s_warmReapplies < MaxWarmReapplies &&
                DeNelle.Core.StructureContentWarmer.PendingRequests > 0)
            {
                s_warmReapplyArmed = true;
                DeNelle.Core.StructureContentWarmer.WhenSettled(() =>
                {
                    s_warmReapplyArmed = false;
                    s_warmReapplies++;
                    if (!HubScenes.IsHub(SceneManager.GetActiveScene().name)) return;
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                        $"structure content settled ({DeNelle.Core.StructureContentWarmer.State}, " +
                        $"resident={DeNelle.Core.StructureContentWarmer.ResidentCount}) — " +
                        $"re-applying hub skins (pass {s_warmReapplies}/{MaxWarmReapplies}).");
                    ApplyAll();
                });
            }
        }

        // Place a NEW model at a world position (no baked structure to swap). Idempotent by name;
        // fit + seat + URP-fix Tripo materials via SkinOptions.Structure, with the orientation correction.
        private static void TryPlace(Place p)
        {
            if (FindByName(p.name) != null) return;   // already placed this scene
            var host = new GameObject(p.name);
            // DETERMINISTIC BASE-ON-GROUND SEAT (owner directive 2026-07-04, the WHOLE fix): the
            // recurring FLOATING-STRUCTURE class — Tree of Life before, the Colosseum now (floated
            // ~5m). ROOT CAUSE (captured [Flow:Hub] proof): the old fallback raycast cast against
            // ALL layers (~0) and took the FIRST hit, which in the merged Main_Castle_Overworld
            // caught OVERHEAD castle geometry at y≈5.23 instead of the ground at y=0 — and
            // non-deterministically (some sessions seated at 0, some floated).
            //
            // The world is SCRIPTED with a KNOWN flat ground: WorldMergeBuilder.LowerCastleToGround
            // lowers the castle floor coplanar with the terrain inner ring at y=0. So we DO NOT
            // sample terrain, DO NOT navmesh, DO NOT raycast (all proven-brittle) — the whole
            // building/structure CATEGORY reads the ONE blanket GroundY variable and pins the
            // bounds-base to it ONE TIME at placement. Same input -> same base on the ground, every
            // session, editor==build.
            Vector3 seated = new Vector3(p.worldPos.x, GroundY, p.worldPos.z);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                $"place '{p.name}': authored y={p.worldPos.y:0.###} -> GroundY={GroundY:0.###} " +
                $"(scripted category ground variable — no raycast/sample) — deterministic base-on-ground seat.");
            host.transform.position = seated;
            // WO-764: fit-to-HEIGHT (YHeightVariable × per-item multiplier), FitLargest cleared — the
            // SAME normalization player-built catalog structures use (StructureFactory.Create). No sizeM.
            float placeMult = p.heightMul > 0f ? p.heightMul : 1f;
            var opts = SkinOptions.Structure(0f);   // clears FitLargest (SeatOnGround + Tripo-fix retained)
            opts.FitHeight = StructureFactory.YHeightVariable * placeMult;
            opts.LocalRotation = Quaternion.Euler(p.pitchDeg, p.yawDeg, p.rollDeg);
            var vis = VisualFactory.Skin(host.transform, p.modelPath, opts);
            if (vis == null)
            {
                Debug.LogWarning("[HubStructureVisualInjector] place model '" + p.modelPath + "' not found for " + p.name + ".");
                Object.Destroy(host);
                return;
            }
            if (p.scaleX > 0f)   // explicit owner-dialed (non-uniform) scale overrides the fit-to-height
                vis.transform.localScale = new Vector3(p.scaleX, p.scaleY, p.scaleZ);
            // Owner 2026-07-04 exact rule: `if (object is a building) object.SetBottom = ground y=0`.
            // Capability check → set the bottom. One code path over all building-type objects, once at
            // placement, no raycast/sample. Runs AFTER any scale override (an explicit scale is applied
            // after VisualFactory's SeatOnGround, so it drifts the bottom — this re-pins it to ground).
            if (IsBuilding(p))
                SetBottomToGround(vis, GroundY);
            // Ticket #10 (RCA 2026-06-21): a TryPlace structure (e.g. the colosseum) has NO baked
            // collider at all — the inject path is visual-only. Fit one to the final visible mesh so
            // it's solid. Done AFTER scale so the box matches what the player sees.
            EnsureStructureCollider(host, vis);
            if (!RuntimePlacedProps.Contains(host)) RuntimePlacedProps.Add(host);   // PROP-SEATING oracle registry
            Debug.Log("[HubStructureVisualInjector] placed " + p.name + " (" + p.modelPath + ") at " + host.transform.position + ".");
        }

        private static void TrySwap(Swap s)
        {
            Transform target = FindByName(s.bakedName);
            if (target == null) return;                              // not in this scene
            // WO-673 L3 STANDDOWN (docs/WO673_ARCHITECTURE_REVIEW.md §3, the Barracks pattern
            // below; always-on since WO-682) — RECONCILED WITH LEVER 1 (owner 2026-07-24, WWCD):
            // the BAKE stands down ONLY when a BaseLayout RECORD will actually replace it (a
            // migrated save, or a player-built replacement); then it deactivates the whole baked
            // structure and BaseLayoutLoader replays the record instead (no double). A baked
            // storefront with NO replacing record STAYS — it PRE-STANDS visible + staffed on a
            // fresh hub (the gate no longer stands a store down merely for having a catalog row;
            // that hid all 8 on a blank save → empty grass under floating vendors). See
            // StanddownActiveForBaked's Lever-1 note.
            if (StrategicPlacementMigration.StanddownActiveForBaked(s.bakedName, out string migratedId))
            {
                target.gameObject.SetActive(false);
                SuppressBakedTwinPhysics(target.gameObject,
                    $"standdown, migrated -> BaseLayout '{migratedId}'");
                DeNelle.Core.Diagnostics.FlowTrace.Step("Placement",
                    $"standdown {s.bakedName} (migrated -> BaseLayout '{migratedId}').");
                return;
            }
            // WO-724 unlock rule (charter OPTION A): the baked Barracks surfaces only when
            // BarracksUnlock.IsUnlocked (ff.barracks ON - default OFF - AND founding-complete).
            // While locked, deactivate the baked structure ENTIRELY (not just re-skin renderers)
            // so the building, its tap-dialogue, and the drillmaster anchor all disappear; the NPC
            // injector then finds nothing and no-ops. ff.barracks OFF => permanently hidden
            // (regression); founding incomplete => hidden until the FTUE completes, at which point
            // BarracksNpcInjector's poll calls EnsureBarracksSurfaced() to reactivate + skin it live.
            //
            // PLACED WINS AT SCENE LOAD (owner ruling 2026-08-06, the barracks adoption):
            // once the player OWNS a barracks - built from the palette, or the shipped
            // CastleBarracks adopted into BaseLayout by
            // StrategicPlacementMigration.AdoptBakedBarracksIfNeeded - the raw bake must
            // NEVER stand again. It used to: TrySwap re-skinned it here, and only the
            // DEFERRED StructureSingleton.EnforceAll (one frame later, and up to 300 frames
            // if GameStateService is slow to appear) stood it down. In that window
            // BarracksNpcInjector.Inject (which runs on the sceneLoaded event, BEFORE
            // BaseLayoutLoader's Start() replay) anchored the drillmaster to the BAKE, and
            // when the sweep then deactivated that bake the drillmaster was left standing at
            // an invisible building with nothing to re-seat it - the owner's exact symptom
            // ("there's no person that stands with the barracks"). Asking the singleton
            // authority HERE, at load, is the same placed-wins rule EnsureBarracksSurfaced
            // already applies, just early enough that no double ever renders.
            // IsPlayerBuilt reads persisted BaseLayout / live PlacedStructure / live
            // non-twin Building and EXCLUDES baked twins, so it can never self-latch off the
            // very object it is suppressing; with no save service it answers false and this
            // is byte-for-byte the pre-existing behaviour.
            if (s.bakedName == "CastleBarracks" &&
                (!BarracksUnlock.IsUnlocked || (StructureSingleton.IsPlayerBuilt("barracks") &&
                    !AuthoredCastleStorefront.IsBoundAuthoredRoot(target, "barracks"))))
            {
                target.gameObject.SetActive(false);
                SuppressBakedTwinPhysics(target.gameObject,
                    "baked CastleBarracks stands down at scene load (locked / placed wins)");
                DeNelle.Core.Diagnostics.FlowTrace.Step("Barracks",
                    "TrySwap: baked 'CastleBarracks' stands DOWN at scene load - " +
                    (BarracksUnlock.IsUnlocked
                        ? "the player OWNS a barracks (placed wins; the owned one replays from BaseLayout)."
                        : $"locked (ff.barracks={DeNelle.Core.FeatureFlags.Barracks}, foundingComplete={BarracksUnlock.FoundingComplete}, WO-724)."));
                return;
            }
            SkinStorefront(s, target);
            AttachHubCollector(s, target);
        }

        // LEVER 1 (owner 2026-07-24, "stores pre-stand on a fresh hub", WWCD): re-surface a
        // baked storefront that STANDDOWN deactivated on a fresh save, so its vendor NPC
        // (seated by CastleVendorNpcInjector's baked-anchor fallback) does not stand at an
        // invisible lot. Re-activates the baked GameObject (found INCLUDING inactive) and
        // applies its lightweight skin, BYPASSING TrySwap's standdown gate — the caller has
        // already decided this storefront must pre-stand because no live/replayed Building
        // owns its id (so there is nothing to double-spawn). Idempotent: SkinStorefront
        // no-ops if the LightSkin_ marker child already exists.
        public static void ResurfaceStorefront(string bakedName)
        {
            if (string.IsNullOrEmpty(bakedName)) return;
            Transform target = AuthoredCastleStorefront.Find(bakedName, true);
            if (target == null) return;   // not in this scene bake
            if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
            // WO-950: a prior gate-suppression stripped this twin's colliders + nav
            // obstacles (phantom-footprint discipline); a resurfaced store must be SOLID
            // again. Restore re-asserts the Ticket #10 setLocalPos rule internally, so a
            // repositioned-visual row keeps its baked body colliders down.
            RestoreBakedTwinPhysics(target.gameObject, bakedName);

            // LightSkin_ marker present => SkinStorefront already ran (it early-returns on the
            // marker). Do NOT let that early-return leave the store hidden under a seated vendor:
            // a prior standdown pass or a re-load may have left the object inactive with the
            // lightweight visual's renderers disabled. SetActive(true) above re-activates it; here
            // we explicitly re-enable the skinned visual's renderers so the store is guaranteed
            // VISIBLE (Lever-1: baked stores pre-stand visible + staffed, owner 2026-07-24).
            var existingSkin = target.Find(MarkerPrefix + bakedName);
            if (existingSkin != null)
            {
                foreach (var r in existingSkin.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = true;
                for (int i = 0; i < Swaps.Length; i++)
                    if (Swaps[i].bakedName == bakedName) { AttachHubCollector(Swaps[i], target); break; }
                return;
            }

            for (int i = 0; i < Swaps.Length; i++)
                if (Swaps[i].bakedName == bakedName)
                {
                    SkinStorefront(Swaps[i], target);
                    AttachHubCollector(Swaps[i], target);
                    return;
                }
            // No Swap row (a storefront with no lightweight model): the re-activated baked
            // prefab renderers already make it visible — but a prior standdown may have left the
            // baked renderers disabled by a stale skin attempt; re-enable them to be safe.
            foreach (var r in target.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = true;
        }

        // =====================================================================
        //  WO-950 — the PHANTOM-FOOTPRINT discipline (owner F8 seq 2267, 2026-08-10:
        //  "feels like a building is here" at the suppressed baked barracks, ~(16,0,-4)).
        //  A suppressed baked twin must carry ZERO enabled non-trigger colliders and no
        //  live nav obstacle: SetActive(false) alone leaves every collider's enabled-FLAG
        //  true, so any later reactivation (or a path that hides renderers without
        //  deactivating, the :402 skin hide) stands an invisible wall the player walks
        //  into. This generalizes the Ticket #10 discipline SkinStorefront already
        //  applies on its setLocalPos path to EVERY suppression path — trigger colliders
        //  go down too, because on a suppressed twin the NPC interact point is NOT
        //  legitimately live (WO-950 item 4). Restore mirrors it on surfacing.
        // =====================================================================

        /// <summary>Disables every enabled collider (solid AND trigger — a suppressed
        /// twin's NPC point is not legitimately live) + every NavMeshObstacle under the
        /// twin. Idempotent and silent when nothing was enabled; logs one traced line
        /// (with a navmesh probe that splits collider-block from baked-navmesh-hole)
        /// when it actually disabled something.</summary>
        public static void SuppressBakedTwinPhysics(GameObject twin, string reason)
        {
            if (twin == null) return;
            int solid = 0, trig = 0, navs = 0;
            foreach (var c in twin.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || !c.enabled) continue;
                if (c.isTrigger) trig++; else solid++;
                c.enabled = false;
            }
            foreach (var o in twin.GetComponentsInChildren<NavMeshObstacle>(true))
                if (o != null && o.enabled) { o.enabled = false; navs++; }
            if (solid + trig + navs == 0) return;   // idempotent re-sweep - nothing to do, nothing to log

            // WO-950 2b mechanism split, one captured line: with zero enabled colliders,
            // any movement STILL blocking at this footprint is the BAKED NAVMESH (the
            // merged-world bake ran with the twin standing, so its hole persists at
            // runtime regardless of colliders) - a rebake concern, not a collider one.
            Vector3 pos = twin.transform.position;
            bool walkable = NavMesh.SamplePosition(pos, out _, 1.5f, NavMesh.AllAreas);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                $"suppressed physics on baked twin '{twin.name}' at ({pos.x:0.0},{pos.y:0.0},{pos.z:0.0}) - " +
                $"disabled {solid} solid + {trig} trigger collider(s), {navs} nav obstacle(s) ({reason}); " +
                $"navmesh-walkable-within-1.5m={walkable} (false + still-blocked-here = baked navmesh hole, WO-950).");
        }

        /// <summary>Re-enables the colliders + nav obstacles a suppression stripped, then
        /// RE-ASSERTS the Ticket #10 rule: on a setLocalPos skin row whose LightSkin marker
        /// stands, the baked NON-TRIGGER colliders stay disabled (the visible building and
        /// its fitted StructureCollider live elsewhere — re-enabling the baked body collider
        /// would resurrect the exact phantom wall the discipline removed).</summary>
        public static void RestoreBakedTwinPhysics(GameObject twin, string bakedName)
        {
            if (twin == null) return;
            int cols = 0, navs = 0;
            foreach (var c in twin.GetComponentsInChildren<Collider>(true))
                if (c != null && !c.enabled) { c.enabled = true; cols++; }
            foreach (var o in twin.GetComponentsInChildren<NavMeshObstacle>(true))
                if (o != null && !o.enabled) { o.enabled = true; navs++; }

            bool reassertedSkinRule = false;
            Transform marker = twin.transform.Find(MarkerPrefix + bakedName);
            if (marker != null && RowKeepsBakedCollidersDown(bakedName))
            {
                reassertedSkinRule = true;
                foreach (var c in twin.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || c.isTrigger) continue;                    // NPC points stay live
                    if (IsUnderTransform(c.transform, marker)) continue;       // the skinned visual's own colliders
                    if (IsUnderNamed(c.transform, "StructureCollider")) continue;   // the Ticket #10 fitted box
                    c.enabled = false;
                }
            }
            if (cols > 0 || navs > 0)
                DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                    $"restored physics on baked twin '{twin.name}' - re-enabled {cols} collider(s), {navs} nav " +
                    $"obstacle(s){(reassertedSkinRule ? "; setLocalPos skin present - baked solid colliders re-disabled per Ticket #10" : "")} (WO-950).");
        }

        /// <summary>True when the swap row for <paramref name="bakedName"/> repositions its
        /// visual (setLocalPos) — the rows whose baked body colliders must stay down.</summary>
        private static bool RowKeepsBakedCollidersDown(string bakedName)
        {
            for (int i = 0; i < Swaps.Length; i++)
                if (Swaps[i].bakedName == bakedName) return Swaps[i].setLocalPos;
            return false;
        }

        private static bool IsUnderTransform(Transform t, Transform root)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur == root) return true;
            return false;
        }

        private static bool IsUnderNamed(Transform t, string name)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur.name == name) return true;
            return false;
        }

        // =====================================================================
        //  WO-1327 — FORCED ALBEDO REBIND (the Cathedral of Magic white-building fix)
        // =====================================================================
        // ⛔ THE PROVING LINE, from the owner's device (logs/device/2026-08-20-town-freeze.log
        // and three sibling captures):
        //     08-20 09:04:34.325 W/Unity (25868): [HubStructureVisualInjector]
        //         texPath 'Structures/ArcaneTower_Albedo' not found for ArcaneTower_MagicUpgrades.
        // That was a Debug.LogWarning, so it never reached the errors-only break-log.jsonl the
        // device pulls — which is exactly why a session was spent looking for StructureFactory's
        // ApplyForcedTexture lines that CANNOT exist here: the Cathedral is a BAKED HUB TWIN
        // ("ArcaneTower_MagicUpgrades", CastleHubBuilder.cs:312), re-skinned by THIS injector.
        // It never routes through StructureFactory.Create at all.
        //
        // THE CAUSE: this used Resources.Load<Texture2D>("Structures/ArcaneTower_Albedo"), and
        // Assets/Resources/Structures WAS DELETED by the CDN migration (StructureAssetLoader's
        // header records it; the albedo now lives at Assets/StructureContent/ArcaneTower_Albedo.jpg
        // and is registered in the Structure_Art Addressables group as the address
        // "Structures/ArcaneTower_Albedo"). So the load returned null on device AND in a player
        // build, the rebind no-opped, and the Tripo FBX — whose only Color map lives in a .fbm
        // folder that does not survive a build — rendered PURE WHITE.
        //
        // THE FIX IS GENERAL, NOT PER-ID: every texPath row goes through StructureAssetLoader,
        // the same resident-first / Resources-fallback seam the MODEL paths already use. Same
        // key strings, so nothing in the swap table or the catalog changes.
        //
        // §12: the rebind now REPORTS ITSELF ON EVERY PATH — skipped (no texPath), missed
        // (address unresolved, with warmer state), bound (slot counts), and property-mismatched.
        // A rebind that says nothing when it does not run is the defect that cost the session.
        private static void ApplyForcedAlbedo(Swap s, Transform target, GameObject vis)
        {
            if (vis == null || target == null) return;

            if (string.IsNullOrEmpty(s.texPath))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Once("Hub", "tex-none-" + s.bakedName,
                    $"'{s.bakedName}': NO texPath authored — forced-albedo rebind SKIPPED BY DESIGN " +
                    $"(model '{s.modelPath}' keeps its own embedded materials). If this structure " +
                    "renders colorless, the swap row needs a texPath; this code did not fail.");
                return;
            }

            if (s_texBound.Contains(s.bakedName)) return;   // already bound this scene load

            // ⛔ NEVER Resources.Load here. Assets/Resources/Structures no longer exists, so a
            // Resources-only load silently returns null in every player build. The loader keeps a
            // Resources tier internally for anything that never migrated, so this is a superset.
            var tex = DeNelle.Core.StructureAssetLoader.LoadStructureAsset<Texture2D>(s.texPath);
            if (tex == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Hub",
                    $"'{s.bakedName}': forced albedo '{s.texPath}' did NOT resolve via " +
                    $"StructureAssetLoader (registered={DeNelle.Core.StructureContentWarmer.IsRegisteredAddress(s.texPath)}, " +
                    $"knownAbsent={DeNelle.Core.StructureContentWarmer.IsKnownAbsent(s.texPath)}, " +
                    $"warmerState={DeNelle.Core.StructureContentWarmer.State}, " +
                    $"resident={DeNelle.Core.StructureContentWarmer.ResidentCount}) — the model keeps " +
                    "its embedded materials and a Tripo FBX WILL RENDER WHITE. Arming one retry for " +
                    "when structure content settles.");
                ArmForcedAlbedoRetry(s);
                return;
            }

            // Bind every albedo-classified slot (URP _BaseMap, built-in _MainTex, Synty
            // _Albedo_Map). Hard-coding the first two is the same miss StructureFactory
            // already retired — and a throw here used to abort the rest of SkinStorefront
            // (Seeker 365875 21:17:44 stack: ApplyForcedAlbedo).
            int slots = 0, bound = 0, propless = 0;
            string proplessShader = null;
            Guard.Try("Hub", "forced-albedo bind " + s.bakedName, () =>
            {
                foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var mats = Application.isPlaying ? r.materials : r.sharedMaterials;
                    foreach (var m in mats)
                    {
                        if (m == null) continue;
                        slots++;
                        bool hit = false;
                        foreach (var property in m.GetTexturePropertyNames())
                        {
                            if (!DependencyClosureTrace.IsAlbedoSlot(property)) continue;
                            m.SetTexture(property, tex);
                            hit = true;
                        }
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                        if (m.HasProperty("_Color"))     m.SetColor("_Color", Color.white);
                        if (hit) bound++;
                        else
                        {
                            propless++;
                            if (proplessShader == null)
                                proplessShader = m.shader != null ? m.shader.name : "(null shader)";
                        }
                    }
                }
            });

            if (bound > 0)
            {
                s_texBound.Add(s.bakedName);
                s_texRetryArmed.Remove(s.bakedName);
                DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                    $"'{s.bakedName}': forced albedo '{s.texPath}' BOUND onto {bound}/{slots} " +
                    $"material slot(s) via StructureAssetLoader" +
                    (propless > 0
                        ? $" ({propless} slot(s) declared neither _BaseMap nor _MainTex; first shader " +
                          $"'{proplessShader}')."
                        : "."));
            }
            else
            {
                // WO-1707: a bare 'Synty/Generic_Basic' shader here (device captures seq5018/5022,
                // 2026-09-12) is the BAKED-TWIN PLACEHOLDER's own shader, not the real Tripo model's
                // — it means SkinStorefront's model swap had not yet landed on 'vis' when this ran,
                // i.e. the SAME not-resident-yet race as the tex==null branch above, just discovered
                // one step later (the texture resolved; the MODEL it needs to bind onto had not).
                // Proven by contrast against a same-day CONFIRMED-textured boot (seeker-365962-logcat,
                // 22:31:56.581): there this call instead read "served RESIDENT from the structure warm
                // cache" then "BOUND onto 2/2" — no propless branch at all — because the warm pass had
                // finished during Title/HeroSelect, ~8s before Main_Castle_Overworld even loaded, so
                // the real model was already swapped in by the time ApplyAll ran. In both white-town
                // boots this Fail fired at t=30.6s/39.9s, ~1s after the SAME scene load — no head
                // start. Previously this branch left the structure white FOREVER for that scene visit
                // (no retry armed, unlike the tex==null branch). Arm the same WhenSettled retry: if
                // the model has swapped in for real by the time content settles, the retried call
                // finds real texture-declaring slots and binds; if the mismatch is genuinely permanent
                // (not residency), the one-shot retry (s_texRetryArmed already dedupes) logs this same
                // Fail once more and stops — never a loop.
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Hub",
                    $"'{s.bakedName}': forced albedo '{s.texPath}' RESOLVED but bound onto ZERO of " +
                    $"{slots} material slot(s) — no material declares _BaseMap or _MainTex " +
                    $"(first shader '{proplessShader ?? "(no materials at all)"}'). The structure " +
                    "will render colorless. This is a SHADER PROPERTY mismatch OR the baked-twin " +
                    "placeholder was still standing in for the real model. Arming one retry for when " +
                    "structure content settles.");
                ArmForcedAlbedoRetry(s);
            }
        }

        /// <summary>
        /// WO-1327: one-shot re-attempt for a forced albedo that was not resident when the skin
        /// ran. Mirrors the model-side warm re-apply; capped by <see cref="s_texRetryArmed"/> so a
        /// genuinely absent address cannot arm rival callbacks every ApplyAll pass.
        /// </summary>
        private static void ArmForcedAlbedoRetry(Swap s)
        {
            if (s_texRetryArmed.Contains(s.bakedName)) return;
            s_texRetryArmed.Add(s.bakedName);
            DeNelle.Core.StructureContentWarmer.WhenSettled(() =>
            {
                if (s_texBound.Contains(s.bakedName)) return;
                var target = FindByName(s.bakedName);
                if (target == null) return;
                var vis = target.Find(MarkerPrefix + s.bakedName);
                if (vis == null) return;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                    $"'{s.bakedName}': structure content settled — RETRYING the forced albedo " +
                    $"'{s.texPath}'.");
                ApplyForcedAlbedo(s, target, vis.gameObject);
            });
        }

        /// <summary>Keep owner-authored meshes and poses; repair materials and bind existing capabilities only.</summary>
        public static void PrepareAuthoredStorefront(AuthoredCastleStorefront authored)
        {
            if (authored == null || !authored.PreserveAuthoredVisual) return;
            if (authored.RepairTripoMaterials)
            {
                var fixer = authored.GetComponent<TripoMaterialFixer>();
                if (fixer == null) fixer = authored.gameObject.AddComponent<TripoMaterialFixer>();
                if (authored.ForcedAlbedo != null) fixer.SetForcedSourceTexture(authored.ForcedAlbedo);
            }
            if (!Application.isPlaying || !authored.gameObject.activeInHierarchy) return;
            if (string.IsNullOrEmpty(authored.CanonicalId)) return; // Realm Store owns its direct panel door.
            var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(authored.CanonicalId);
            if (entry == null)
            {
                FlowTrace.Warn("Hub", "Authored storefront awaits catalog: " + authored.CanonicalId);
                return;
            }
            StructureFactory.AttachAuthoredCapabilities(authored.gameObject, entry);
        }

        // The lightweight-skin body of a swap (extracted from TrySwap so ResurfaceStorefront
        // can apply it without re-running the standdown/barracks gates). Idempotent by the
        // LightSkin_ marker child.
        private static void SkinStorefront(Swap s, Transform target)
        {
            var authored = target.GetComponent<AuthoredCastleStorefront>();
            if (authored != null && authored.PreserveAuthoredVisual)
            {
                PrepareAuthoredStorefront(authored);
                return;
            }
            string marker = MarkerPrefix + s.bakedName;
            var existing = target.Find(marker);
            if (existing != null)
            {
                // WO-1327: the MODEL is already skinned, but that says nothing about whether its
                // forced albedo ever bound. Retry the texture on the existing visual instead of
                // returning blind — this early-return is why the warm re-apply pass could never
                // recover a white Cathedral.
                if (!string.IsNullOrEmpty(s.texPath) && !s_texBound.Contains(s.bakedName))
                    ApplyForcedAlbedo(s, target, existing.gameObject);
                return;                                             // already swapped (idempotent)
            }

            // Hide the baked visual (renderers only — NPC point + colliders/logic stay live).
            var bakedRenderers = target.GetComponentsInChildren<Renderer>(true);
            foreach (var r in bakedRenderers)
                if (r != null) r.enabled = false;

            // Skin the lightweight model in: WO-764 fit-to-HEIGHT (YHeightVariable × per-item multiplier,
            // FitLargest cleared) + seat-on-ground + URP-fix Tripo materials — the SAME normalization
            // player-built catalog structures use. LocalRotation (yaw) is applied BEFORE fit/seat so the
            // fit measures it final-facing.
            float swapMult = s.heightMul > 0f ? s.heightMul : 1f;
            var opts = SkinOptions.Structure(0f);   // clears FitLargest (SeatOnGround + Tripo-fix retained)
            opts.FitHeight = StructureFactory.YHeightVariable * swapMult;
            opts.LocalRotation = Quaternion.Euler(s.pitchDeg, s.yawDeg, s.rollDeg);
            var vis = VisualFactory.Skin(target, s.modelPath, opts);
            if (vis == null)
            {
                // Model absent on this machine — restore the baked visual; nothing lost.
                foreach (var r in bakedRenderers)
                    if (r != null) r.enabled = true;
                Debug.LogWarning("[HubStructureVisualInjector] " + s.modelPath +
                                 " not found — kept the baked visual for " + s.bakedName + ".");
                return;
            }

            vis.name = marker;
            if (s.setLocalPos)
            {
                vis.transform.localPosition = new Vector3(s.posX, s.posY, s.posZ);
                // Ticket #10 (RCA 2026-06-21): when the visual is moved off the baked root (barracks),
                // the baked SOLID collider stays at the root — a phantom wall where nothing is visible,
                // and the visible building is walk-through. Disable the baked NON-TRIGGER colliders (keep
                // TRIGGER colliders — NPC interaction points rely on them) and fit a new one below.
                foreach (var c in target.GetComponentsInChildren<Collider>(true))
                    if (c != null && !c.isTrigger) c.enabled = false;
            }
            else if (s.posY != 0f)
            {
                var lp = vis.transform.localPosition;
                lp.y += s.posY;
                vis.transform.localPosition = lp;
            }
            if (s.scaleX > 0f)   // explicit (non-uniform) scale overrides the fit-to-height
                vis.transform.localScale = new Vector3(s.scaleX, s.scaleY, s.scaleZ);
            if (!s.setLocalPos)
                SetBottomToGround(vis, GroundY);
            // Escape hatch: force a texture when the model's embedded material didn't bind one
            // (renders colorless). The Tripo fixer reads the source material's _MainTex/_BaseMap;
            // a model whose FBX material lost that link (e.g. the arcane tower) needs it forced.
            ApplyForcedAlbedo(s, target, vis);
            // Ticket #10: when the visual was repositioned (setLocalPos), the baked collider is now
            // mislocated (disabled above) — fit a fresh one to the visible mesh so the building is solid
            // where it's seen. Only for setLocalPos: the 6 non-repositioned swaps keep their co-located
            // baked colliders (don't touch what works — no smuggled changes).
            if (s.setLocalPos)
                EnsureStructureCollider(target.gameObject, vis);

            // Owner 2026-07-15 "arcane towers should have an aura": the baked hub landmark
            // (ArcaneTower_MagicUpgrades) holds a persistent magic-circle aura. Idempotent;
            // colorblind-safe (reads by motion/luminance, not hue). Seated on the baked root so
            // it tracks the structure regardless of the swapped visual child.
            // Owner 2026-07-30 (WO-788, owner's explicit pick): the baked Cathedral of Magic hub
            // landmark shows the flat blue electro rune-circle ground loop ("Cathedral_Aura" ->
            // Magic circle electro loop) — NOT a shield dome; the prior "Aegis_Shield" holy dome was
            // the felt-test reject. Distinct from the combat Arcane Spire (Aura_HeartPulse) +
            // harvest nodes ("Poi_NodeAura" -- retagged 2026-08-06; this line used to name
            // "TreeofLifeAura_Aura" and was stale). Must match StructureFactory (diff gate).
            if (s.bakedName == "ArcaneTower_MagicUpgrades")
                ArcaneAura.Ensure(target.gameObject, "Cathedral_Aura");

            Debug.Log("[HubStructureVisualInjector] " + s.bakedName + " re-skinned to " + s.modelPath + ".");
        }

        // Fit a world-axis-aligned BoxCollider to the visible mesh so an injected structure is solid.
        // The collider lives on a child whose world rotation is identity and world scale is 1, so the
        // box maps 1:1 to world units regardless of the host/visual transform (rotation + non-uniform
        // scale on the Tripo visual would otherwise skew a collider placed directly on it). Ticket #10.
        private static void EnsureStructureCollider(GameObject host, GameObject vis)
        {
            if (host == null || vis == null) return;
            Bounds b = default; bool have = false;
            foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!have) { b = r.bounds; have = true; } else b.Encapsulate(r.bounds);
            }
            if (!have) return;   // no renderable mesh -> nothing to wall off

            var holder = new GameObject("StructureCollider");
            holder.transform.SetParent(host.transform, false);
            holder.transform.position = b.center;
            holder.transform.rotation = Quaternion.identity;
            // Neutralize inherited scale so BoxCollider.size is in world units.
            Vector3 pls = host.transform.lossyScale;
            holder.transform.localScale = new Vector3(
                Mathf.Abs(pls.x) > 1e-4f ? 1f / pls.x : 1f,
                Mathf.Abs(pls.y) > 1e-4f ? 1f / pls.y : 1f,
                Mathf.Abs(pls.z) > 1e-4f ? 1f / pls.z : 1f);
            var box = holder.AddComponent<BoxCollider>();
            box.size = b.size;
            // FlowTrace (not Debug.Log) so the headless break-log captures it — proof the structure is solid.
            DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                $"fitted BoxCollider on '{host.name}' size={b.size} center={b.center} (ticket #10 — now solid).");
        }

        // Capability check for the owner's rule `if (object is a building) SetBottom = ground`.
        // Everything the injector PLACES via the Places table is a building/structure — it is skinned
        // through SkinOptions.Structure + FitHeight and registered as a solid structure (EnsureStructure
        // collider). So the whole placed category carries the building capability; this is the single
        // predicate to narrow if a non-building prop is ever added to Places.
        private static bool IsBuilding(Place p) => true;

        // SetBottom: pin a placed visual's BOTTOM (combined renderer bounds.min.y — its lowest point)
        // to groundY, deterministically, ONE time. Generalized for ANY building-type object (Colosseum
        // and any future placed structure), not per-object. Runs after any explicit scale override
        // (which is applied after VisualFactory's SeatOnGround, so it drifts the bottom off ground).
        // No raycast, no sampling — the bottom is set to the scripted GroundY, so the base sits ON the
        // ground every session (editor==build). The Tree of Life grounds off the SAME value via
        // SeatOnGroundOnStart._groundY (default 0, SeatOnGroundOnStart.cs:40) — one ground for the category.
        // Owner 2026-09-12: do NOT spawn Courtyard_PlazaFallback. That 80x80 lime plane
        // rewrote the yard. The 8 injector LightSkins are the town; leave the baked ground.
        private static void RemoveRetiredCourtyardPlaza()
        {
            var go = GameObject.Find("Courtyard_PlazaFallback");
            if (go != null)
            {
                FlowTrace.Step("Hub", "destroyed leftover Courtyard_PlazaFallback (80x80 lime plane retired).");
                Object.Destroy(go);
            }
        }

        /// <summary>
        /// Hub farm/lumbermill are LightSkin swaps on baked twins. StructureFactory never
        /// runs for them on the founding load (BaseLayoutLoader saw an empty layout, then
        /// migration latched standdown for the NEXT hub load). Attach the same
        /// ResourceCollector + CollectorStackView a player-placed collector gets.
        /// </summary>
        private static void AttachHubCollector(Swap s, Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return;
            string buildingId = CollectorBuildingIdForBaked(s.bakedName);
            if (buildingId == null) return;

            var col = target.GetComponent<ResourceCollector>();
            if (col == null) col = target.gameObject.AddComponent<ResourceCollector>();
            col.Configure(buildingId);
            CollectorStackView.Attach(col);
            FlowTrace.Step("Harvest",
                $"hub '{s.bakedName}' ResourceCollector configured id={buildingId} " +
                "(same attach as StructureFactory.Create ResourceCollector).");
        }

        private static string CollectorBuildingIdForBaked(string bakedName)
        {
            if (bakedName == "Windmill_Food_Storefront") return ResourceBuildingProgression.FarmId;
            if (bakedName == "Lumbermill_Wood_Storefront") return ResourceBuildingProgression.LumbermillId;
            return null;
        }

        private static void SetBottomToGround(GameObject vis, float groundY)
        {
            if (vis == null) return;
            Bounds b = default; bool have = false;
            foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!have) { b = r.bounds; have = true; } else b.Encapsulate(r.bounds);
            }
            if (!have) return;
            var pos = vis.transform.position;
            pos.y += groundY - b.min.y;   // lift/lower so the visible BOTTOM lands exactly on groundY
            vis.transform.position = pos;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Hub",
                $"SetBottom '{vis.name}' bottom -> ground y={groundY:0.###} (was min.y={b.min.y:0.###}, " +
                $"delta={groundY - b.min.y:0.###}) — deterministic base-on-ground, no raycast.");
        }

        // Name match across the loaded scene(s). Runs once per hub load (not per frame).
        private static Transform FindByName(string name)
        {
            return AuthoredCastleStorefront.Find(name);
        }
    }
}
