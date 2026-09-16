// =============================================================================
// MineNodeVisual — gives every harvestable MineNode a DISTINCT, readable look so a
// player can tell at a glance what a node yields (the owner's "we need an image for
// the nodes still" gap: nodes were rendering as a bare tinted Cube / a primitive
// sphere — indistinguishable placeholders).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// ONE visual source of truth for ALL MineNodes, however they were placed:
//   • OuterWorldBuilder bakes a tinted Cube + MineNode  → this hides the Cube and
//     builds the real silhouette over it.
//   • RareCrystalSpawner builds a crystal bloom at runtime → reuses this too.
//   • Build-mode placed nodes (later)                    → same path.
//
// MineNode attaches this in Awake (auto), so there is NO scene edit and NO prefab to
// wire — it just works on the existing scene + on anything spawned at runtime.
//
// ART-FORWARD, FALLBACK-SAFE (CLAUDE.md §4):
//   1. First it TRIES to load a real low-poly prop from Resources via the project's
//      proven VisualFactory.Skin (the same loader towers/enemies use). Drop a matching
//      prefab at the per-resource Resources path below and the node auto-upgrades to it.
//   2. If no prop is present (fresh clone, pack not imported), it builds a DISTINCT
//      procedural low-poly silhouette from primitives per resource — a stacked log for
//      Wood, a jagged ore boulder for Iron, a grain mound for Food, a faceted crystal
//      cluster for AetherCrystal — tinted + (crystal) emissive. Readable, not a single
//      bright primitive. LogWarning on a missing prop, never an error.
//
// VISUAL-ONLY: it never touches MineNode's collider / radius / harvest / banking. The
// node's interaction is a distance check (MineNode.InteractRadius), independent of any
// mesh — so swapping the look can't break harvesting.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>Builds a distinct, readable visual for a <see cref="MineNode"/> per
    /// <see cref="MineResource"/>. Tries a real Resources prop first, then falls back to
    /// a procedural low-poly silhouette. Pure cosmetic — never touches harvest logic.</summary>
    [DisallowMultipleComponent]
    public sealed class MineNodeVisual : MonoBehaviour
    {
        [Tooltip("Which resource look to build. Set by MineNode when it attaches this.")]
        public MineResource Resource = MineResource.Iron;

        [Tooltip("Optional: try to load a real low-poly prop from Resources first " +
                 "(auto-upgrades the node when matching art is imported). Falls back to a " +
                 "procedural silhouette if absent.")]
        public bool TryResourcesProp = true;

        private GameObject _visual;

        // NOTE (owner F8 2026-07-04): the harvest props are Tripo exports whose import pivot leaves
        // them at a wrong identity pose. This used to be corrected with a hand-authored OffsetForge
        // euler (offsets.json), but that guessed rotation laid the model on its side. Orientation is
        // now derived from the model's own bounds at placement (SkinOptions.SeatFlat in Build) — the
        // flattest face rests down for every prop, no per-prop magic euler. See VisualFactory.SeatFlat.

        // Per-resource Resources prop path tried FIRST (drop a model here to upgrade the look).
        // Owner 2026-06-16 art drop: lightweight per-type models at Resources/Harvest/<type>
        // (~33-156KB each, WebGL-friendly). When absent the procedural fallback renders.
        private static string PropPath(MineResource res) => res switch
        {
            MineResource.Wood          => "Harvest/wood",
            MineResource.Iron          => "Harvest/iron",
            // WO-1163/PROD-016: enum frozen, DISPLAY moves. Same route as HarvestSite - the two must
            // never disagree about which model a node wears.
            MineResource.Stone          => "Harvest/stone",
            MineResource.AetherCrystal => "Harvest/crystals",
            _                          => null,
        };

        // Readable tints per resource (used by the procedural fallback + as the prop's
        // accent). Lumber=bark brown, Iron=cold steel-grey, Food=wheat gold, Crystal=aether.
        private static Color Tint(MineResource res) => res switch
        {
            MineResource.Wood          => new Color(0.42f, 0.28f, 0.15f),
            MineResource.Iron          => new Color(0.52f, 0.54f, 0.58f),
            MineResource.Stone          => new Color(0.85f, 0.70f, 0.28f),
            MineResource.AetherCrystal => new Color(0.35f, 0.78f, 1.00f),
            _                          => Color.gray,
        };

        private void Start()
        {
            // Pull the FINAL resource from the sibling MineNode at Start: callers like
            // RareCrystalSpawner set MineNode.Resource AFTER AddComponent (post-Awake), and
            // CrystalMineNode may flip it to AetherCrystal in its own Awake. Reading here (a
            // frame after all Awakes) guarantees the look matches the node's real resource.
            var node = GetComponent<MineNode>();
            if (node != null) Resource = node.Resource;
            Build();
        }

        /// <summary>Builds (or rebuilds) the node visual for the current Resource. Safe to
        /// call again after changing Resource — it tears the old visual down first.</summary>
        public void Build()
        {
            // Owner F8 2026-06-30 ("nodes are floating, the y is not set on raycast"): snap the node
            // HOST to the real ground BEFORE seating. SeatOnGround only re-bases the visual to the
            // host's y (it does NOT raycast), and OuterWorldBuilder places nodes at a fixed y that
            // ignores the terrain heightmap dip — so without this the prop seats at the wrong height.
            SnapHostToGround();

            if (_visual != null) { Destroy(_visual); _visual = null; }

            // Permanently REMOVE any placeholder mesh sitting on the node's OWN GameObject
            // (the tinted Cube OuterWorldBuilder bakes — including the bright cyan
            // AetherCrystal cube — or a primitive sphere). We DESTROY the MeshRenderer +
            // MeshFilter rather than only disabling them: NodeDiscoverySystem captures every
            // child Renderer via GetComponentsInChildren<Renderer>(true) (which INCLUDES
            // disabled renderers) and its ApplyDim() sets r.enabled = true on reveal — that
            // resurrected the disabled host cube as a bright unlit cyan box poking through the
            // real seated prop. Destroying the components removes them from that capture for
            // good. The host transform + any Collider (SettlementPlacer's raycast + future
            // triggers) are left untouched, so harvest interaction is unaffected.
            var hostRenderer = GetComponent<MeshRenderer>();
            if (hostRenderer != null) Destroy(hostRenderer);
            var hostFilter = GetComponent<MeshFilter>();
            if (hostFilter != null) Destroy(hostFilter);
            FlowTrace.Step("HarvestNode",
                "MineNodeVisual(" + Resource + "): removed host placeholder cube renderer+filter " +
                "- only the seated Harvest prop renders (no cyan cube; NodeDiscovery cannot re-enable it).");

            _visual = new GameObject("NodeVisual");
            _visual.transform.SetParent(transform, false);
            _visual.transform.localPosition = Vector3.zero;

            // 1) Try a real low-poly prop (auto-upgrade when art lands).
            if (TryResourcesProp)
            {
                string path = PropPath(Resource);
                if (!string.IsNullOrEmpty(path) && Resources.Load<GameObject>(path) != null)
                {
                    // Owner F8 2026-07-04 ("iron mine not sitting with flat side down"): the harvest
                    // props are Tripo exports whose import pivot leaves them at a wrong identity pose.
                    // The old offsets.json euler (id "iron"/"wood"/… = 0,37,-129) was a hand-authored
                    // GUESS that laid the model on its side. Replace it with SeatFlat — VisualFactory
                    // derives the upright pose from the model's own bounds (narrowest axis → +Y, §4)
                    // so the flat face rests down for every harvest prop, no per-prop magic euler.
                    FlowTrace.Step("Harvest",
                        "MineNodeVisual(" + Resource + "): seating prop '" + path +
                        "' FLAT via bounds-derived orientation (SeatFlat) — no hand-authored euler.");

                    // FixTripoMaterials so a Tripo/AccuRIG export renders in URP (no magenta).
                    // SeatFlat + Fit + Seat run inside Skin — prop stood up, sized, seated on the node.
                    // Owner F8 2026-07-08 "tree toation" (RCA, Player.log 15609-15840): SkinOptions.
                    // LocalRotation is an ASSIGNMENT, not a compose — a bare yaw WIPES the FBX's native
                    // up-axis correction and tips the prop; SeatFlat's narrowest-axis→+Y heuristic then
                    // ratifies the LYING pose. The fix: the prop AUTHORS its full pose and opts OUT of
                    // SeatFlat so the heuristic can't clobber it (§4: an authored/manual orientation is
                    // canon, never overwritten by the auto pass).
                    // Owner F8 2026-07-08 (scope update): WOOD, IRON, and CRYSTALS all share ONE
                    // authored upright pose — the euler (-90,0,90). (Supersedes the earlier F8-6 wood
                    // value (270,90,0).) FOOD keeps the auto SeatFlat pass.
                    // Owner F8 2026-08-30 ("a node that didn't have the flat surface face down"):
                    // FOOD JOINS THE AUTHORED-POSE LIST. This is NOT a new orientation guess — it is
                    // the SAME already-owner-verified pose applied to the SAME geometry, proven by
                    // asset identity:
                    //   • WO-1163/PROD-016 (commit 0082dcc99, 2026-08-25) re-pointed Food's model
                    //     from "Harvest/food" to "Harvest/stone" in a ONE-LINE path change, and left
                    //     this authoredPose list untouched.
                    //   • Assets/Resources/Harvest/stone.fbx is BYTE-IDENTICAL to crystals.fbx
                    //     (md5 233dcdffb2a60359d9f4137315223c91 for both); their .meta files differ
                    //     ONLY in the guid — same bakeAxisConversion:0, useFileUnits:1, globalScale:1.
                    //     They even share the same Tripo source texture (a8bcc942-...).
                    //   • Identical geometry through an identical import needs an identical upright
                    //     pose. Crystals' upright pose is owner-verified (F8 2026-07-08) as
                    //     Euler(-90,0,90) with SeatFlat OFF. Therefore stone's is too.
                    //   • SeatFlat can never reach it: the pose maps the model's LOCAL +Z to world +Y,
                    //     i.e. this Tripo export is Z-up and its long axis is its up axis. SeatFlat's
                    //     contract is the OPPOSITE — it drives the NARROWEST bounds axis to +Y — so on
                    //     an elongated cluster it either reports "already flat (Y narrowest)" and
                    //     applies nothing, or stands a short axis up; either way the long axis stays
                    //     horizontal and the prop lies on its side. That is the reported symptom.
                    // Wood/Iron/AetherCrystal are untouched by this change: they already evaluated
                    // true and still do, so their SkinOptions are identical before and after.
                    bool authoredPose = Resource == MineResource.Wood
                        || Resource == MineResource.Iron
                        || Resource == MineResource.Stone
                        || Resource == MineResource.AetherCrystal;
                    var skinned = VisualFactory.Skin(_visual.transform, path,
                        new SkinOptions
                        {
                            FitLargest = 2.0f,
                            SeatOnGround = true,
                            FixTripoMaterials = true,
                            SeatFlat = !authoredPose,
                            LocalRotation = authoredPose
                                ? Quaternion.Euler(-90f, 0f, 90f) : (Quaternion?)null,
                        });
                    // PERMANENT ORIENTATION TRACE (§12). The next "node is lying down" report must be
                    // ONE capture, not another hunt: name the resource, the model path, the pose ROUTE
                    // taken, and the FINAL WORLD rotation the player actually sees. VerifyNodeRenders
                    // in ClaimableCamp cannot host this - it runs synchronously after SetActive(true),
                    // i.e. BEFORE this MineNodeVisual.Start() has built anything, so it can only ever
                    // see the pre-build state. This is the only site that knows the resolved pose.
                    FlowTrace.Step("HarvestNode",
                        "MineNodeVisual POSE: resource=" + Resource + " model='" + path +
                        "' route=" + (authoredPose ? "AUTHORED Euler(-90,0,90) [SeatFlat OFF]"
                                                   : "SeatFlat bounds-heuristic [no authored euler]") +
                        " skinned=" + (skinned != null) +
                        " worldEuler=" + (skinned != null
                            ? skinned.transform.rotation.eulerAngles.ToString("F1") : "n/a") +
                        " hostEuler=" + transform.rotation.eulerAngles.ToString("F1"));
                    if (skinned != null) return;
                }
            }

            // 2) Procedural low-poly silhouette fallback (distinct per resource).
            BuildProcedural(_visual.transform, Resource);
        }

        // Cast straight down to the ground (terrain) and set the host y to the hit, so the seated
        // prop sits ON the surface instead of floating at OuterWorldBuilder's fixed placement y.
        // Our own colliders are disabled for the cast so the ray can't hit the node itself.
        private void SnapHostToGround()
        {
            Guard.Try("HarvestNode", "snap node to ground", () =>
            {
                var ownCols = GetComponentsInChildren<Collider>(true);
                foreach (var c in ownCols) if (c != null) c.enabled = false;

                Vector3 p = transform.position;
                bool hit = Physics.Raycast(new Vector3(p.x, p.y + 60f, p.z), Vector3.down,
                    out RaycastHit h, 300f, ~0, QueryTriggerInteraction.Ignore);

                foreach (var c in ownCols) if (c != null) c.enabled = true;

                if (hit)
                {
                    transform.position = new Vector3(p.x, h.point.y, p.z);
                    FlowTrace.Step("HarvestNode",
                        $"MineNodeVisual({Resource}): snapped host to ground y={h.point.y:F2} (was {p.y:F2}, hit '{h.collider.name}').");
                }
                else
                {
                    FlowTrace.Warn("HarvestNode",
                        $"MineNodeVisual({Resource}): NO ground under node at {p} (down-ray miss) — left at y={p.y:F2}. Likely the same navmesh/terrain gap as the hero fall.");
                }
            });
        }

        // ── Procedural silhouettes ───────────────────────────────────────────────────
        private static void BuildProcedural(Transform parent, MineResource res)
        {
            switch (res)
            {
                case MineResource.Wood:          BuildLog(parent);     break;
                case MineResource.Iron:          BuildOre(parent);     break;
                case MineResource.Stone:          BuildGrain(parent);   break;
                case MineResource.AetherCrystal: BuildCrystal(parent); break;
                default:                         BuildOre(parent);     break;
            }
        }

        // Lumber: a short stacked log pile — two horizontal logs + a low stump, bark brown.
        private static void BuildLog(Transform parent)
        {
            Color bark = Tint(MineResource.Wood);
            Color end  = new Color(0.62f, 0.46f, 0.28f); // pale cut-end

            // Stump base.
            var stump = Cyl(parent, "Stump", new Vector3(0f, 0.35f, 0f),
                Quaternion.identity, new Vector3(0.9f, 0.35f, 0.9f), bark);
            // Two crossed logs lying on top (rotated on their side via Z-euler).
            var logA = Cyl(parent, "LogA", new Vector3(-0.18f, 0.95f, 0f),
                Quaternion.Euler(0f, 0f, 90f), new Vector3(0.32f, 0.95f, 0.32f), bark);
            var logB = Cyl(parent, "LogB", new Vector3(0.20f, 0.95f, 0.10f),
                Quaternion.Euler(0f, 28f, 90f), new Vector3(0.30f, 0.85f, 0.30f), bark);
            // A bright cut-end so it reads as cut timber, not a rock.
            Cube(parent, "CutEnd", new Vector3(-0.92f, 0.95f, 0f),
                Quaternion.identity, new Vector3(0.06f, 0.34f, 0.34f), end);
            _ = stump; _ = logA; _ = logB;
        }

        // Iron: a jagged ore boulder — a faceted rock cluster, cold steel-grey, with a
        // couple of brighter "ore vein" nuggets so it reads as a mineable deposit.
        private static void BuildOre(Transform parent)
        {
            Color rock = Tint(MineResource.Iron);
            Color vein = new Color(0.78f, 0.66f, 0.42f); // rusty ore glint

            Cube(parent, "Boulder", new Vector3(0f, 0.55f, 0f),
                Quaternion.Euler(18f, 22f, 12f), new Vector3(1.25f, 1.05f, 1.15f), rock);
            Cube(parent, "Chunk1", new Vector3(0.55f, 0.40f, 0.45f),
                Quaternion.Euler(34f, 12f, 28f), new Vector3(0.7f, 0.6f, 0.65f), rock);
            Cube(parent, "Chunk2", new Vector3(-0.55f, 0.35f, -0.35f),
                Quaternion.Euler(20f, 48f, 16f), new Vector3(0.6f, 0.55f, 0.6f), rock);
            // Ore veins (small bright nuggets embedded in the rock).
            Cube(parent, "Vein1", new Vector3(0.18f, 0.85f, 0.42f),
                Quaternion.Euler(30f, 20f, 15f), new Vector3(0.22f, 0.22f, 0.22f), vein);
            Cube(parent, "Vein2", new Vector3(-0.25f, 0.62f, 0.5f),
                Quaternion.Euler(10f, 40f, 25f), new Vector3(0.18f, 0.18f, 0.18f), vein);
        }

        // Food: a wheat/grain mound — a rounded golden heap with a couple of sheaf spikes.
        private static void BuildGrain(Transform parent)
        {
            Color grain = Tint(MineResource.Stone);
            Color sheaf = new Color(0.92f, 0.80f, 0.40f);

            // Low wide mound (squashed sphere reads as a heap, not a ball).
            Sphere(parent, "Mound", new Vector3(0f, 0.45f, 0f),
                new Vector3(1.3f, 0.7f, 1.3f), grain);
            // A few upright sheaf spikes so it isn't mistaken for a stone.
            Cyl(parent, "Sheaf1", new Vector3(0.25f, 0.95f, 0.1f),
                Quaternion.Euler(0f, 0f, 8f), new Vector3(0.10f, 0.55f, 0.10f), sheaf);
            Cyl(parent, "Sheaf2", new Vector3(-0.2f, 0.9f, -0.15f),
                Quaternion.Euler(0f, 0f, -10f), new Vector3(0.10f, 0.50f, 0.10f), sheaf);
            Cyl(parent, "Sheaf3", new Vector3(0.05f, 1.0f, 0.28f),
                Quaternion.Euler(6f, 0f, 2f), new Vector3(0.09f, 0.6f, 0.09f), sheaf);
        }

        // Crystal/Magic: a faceted aether crystal cluster — angled shards of different
        // heights, emissive, with the project's CrystalVisual driver layered on so it
        // slowly spins + pulses (the owner's "slowly spinning and pulsing" crystal).
        private static void BuildCrystal(Transform parent)
        {
            Color aether = Tint(MineResource.AetherCrystal);

            // Central tall shard + flanking smaller shards, all faceted (cube on point).
            CrystalShard(parent, "ShardMain", new Vector3(0f, 0.9f, 0f),
                Quaternion.Euler(8f, 20f, 6f), new Vector3(0.55f, 1.7f, 0.55f), aether);
            CrystalShard(parent, "ShardL", new Vector3(-0.45f, 0.6f, 0.15f),
                Quaternion.Euler(-14f, 35f, -18f), new Vector3(0.38f, 1.0f, 0.38f), aether);
            CrystalShard(parent, "ShardR", new Vector3(0.42f, 0.55f, -0.2f),
                Quaternion.Euler(16f, -25f, 22f), new Vector3(0.34f, 0.9f, 0.34f), aether);
            CrystalShard(parent, "ShardFront", new Vector3(0.1f, 0.5f, 0.45f),
                Quaternion.Euler(24f, 10f, 10f), new Vector3(0.3f, 0.75f, 0.3f), aether);

            // Layer the cosmetic spin + colour-pulse driver onto the cluster (reuses the
            // project's CrystalVisual — no new behaviour). Reflection-free: same assembly.
            if (parent.GetComponent<CrystalVisual>() == null)
                parent.gameObject.AddComponent<CrystalVisual>();
        }

        // ── Primitive helpers (flat URP-lit, collider stripped — visual only) ─────────
        private static GameObject Cube(Transform parent, string name, Vector3 pos,
            Quaternion rot, Vector3 scale, Color colour)
            => Prim(PrimitiveType.Cube, parent, name, pos, rot, scale, colour, false);

        private static GameObject Cyl(Transform parent, string name, Vector3 pos,
            Quaternion rot, Vector3 scale, Color colour)
            => Prim(PrimitiveType.Cylinder, parent, name, pos, rot, scale, colour, false);

        private static GameObject Sphere(Transform parent, string name, Vector3 pos,
            Vector3 scale, Color colour)
            => Prim(PrimitiveType.Sphere, parent, name, pos, Quaternion.identity, scale, colour, false);

        // A crystal shard: a cube tipped on point (45° lean) reads as a faceted gem, with
        // emission so it glows like the aether crystals elsewhere.
        private static GameObject CrystalShard(Transform parent, string name, Vector3 pos,
            Quaternion rot, Vector3 scale, Color colour)
            => Prim(PrimitiveType.Cube, parent, name, pos, rot, scale, colour, true);

        private static GameObject Prim(PrimitiveType type, Transform parent, string name,
            Vector3 pos, Quaternion rot, Vector3 scale, Color colour, bool emissive)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // visual only — MineNode owns interaction.
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            ApplyFlat(go.GetComponent<Renderer>(), colour, emissive);
            return go;
        }

        private static void ApplyFlat(Renderer renderer, Color colour, bool emissive)
        {
            if (renderer == null) return;
            Shader s = Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                       ?? Shader.Find("Standard");
            if (s == null) return;
            var mat = new Material(s);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     colour);
            if (emissive && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", colour * 0.8f);
            }
            renderer.sharedMaterial = mat;
        }
    }
}
