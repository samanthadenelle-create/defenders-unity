// =============================================================================
// RaidBaseDresser - KayKit / Synty / StructureContent dress for RaidBase_* bakes.
// -----------------------------------------------------------------------------
// WO-1608. Called by RaidBaseGenerator AFTER rings, turrets, spire and staging
// exist. Never hand-edits a .unity. Missing gitignored packs LogWarning and skip
// that piece (TGVRU). Combat stats stay on DefenseTower / WallSegment colliders.
//
// Art load order (the Resources/Structures miss is why towers shipped as cylinders):
//   1. explicit Assets/ path
//   2. KayKit token (Dungeon Remastered + Hexagon + dungeon twin)
//   3. Synty castle / battleground prefab by filename
//   4. Assets/StructureContent (tracked - always on a fresh clone)
//   5. Resources.Load of the catalog visualPrefabPath (legacy, usually empty)
// =============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class RaidBaseDresser
    {
        public const string Sys = "RaidBase";
        public const float MinGateWidth = 3.5f;

        // -- WO-1633 courtyard cover rings ------------------------------------
        // Owner 2026-09-10, verbatim: "i have mentioned it in testing that it feels
        // incomplete and not polished" / "similar strategy as we used in battle arena".
        // The band these knobs describe replaces the single annulus the old PropSlot
        // courtyard branch used (Lerp(inner+4, radius-6, 0.45..0.75)), which put every
        // courtyard prop of every camp on ONE circle with no jitter - WO-1633 §2.2.

        /// <summary>Clear air kept around the spire so props never crowd the objective.</summary>
        private const float CourtyardSpirePad = 9f;

        /// <summary>Clear air kept outside an inner keep ring before the courtyard band starts.</summary>
        private const float CourtyardInnerPad = 3.5f;

        /// <summary>Clear air kept inside the wall line, so props do not fight the wall-band turrets.</summary>
        private const float CourtyardWallPad = 5.5f;

        /// <summary>Target metres between concentric cover rings inside the courtyard band.</summary>
        private const float CourtyardRingSpacing = 7f;

        /// <summary>Ceiling on concentric courtyard rings - three reads as a place, more reads as a car park.</summary>
        private const int CourtyardMaxRings = 3;

        /// <summary>Base cluster spread in metres; grows with the entry's instance count.</summary>
        private const float ClusterSpreadBase = 1.8f;

        /// <summary>Scale roll for props. Deliberately tighter than the arena's 0.9-1.6 - a crate
        /// at 1.6x reads as a bug, where a boulder does not.</summary>
        private const float PropScaleMin = 0.92f;
        private const float PropScaleMax = 1.12f;

        /// <summary>Metres of clear air kept around each placed turret so props never bury one.</summary>
        private const float TurretClearPad = 3.5f;

        /// <summary>Metres of clear air kept around the staging marker (WO-1520).</summary>
        private const float StagingClearPad = 10f;

        /// <summary>
        /// The staging marker's object name. MIRRORS <c>RaidBaseGenerator.StagingPointName</c>
        /// (`RaidBaseGenerator.cs:189`), which is private - this dresser reads the marker OUT OF
        /// THE BUILT TREE rather than recomputing a radius, because staging is NOT on the south
        /// axis for every camp: Builds/wave2-bake2 records both `fortified_garrison` and
        /// `mage_enclave` moved to the SOUTH-WEST diagonal.
        /// </summary>
        private const string StagingMarkerName = "RaidStagingPoint";

        /// <summary>
        /// Where props may NOT go, all of it measured off the tree the generator just built
        /// rather than off hardcoded radii. WO-1633 acceptance 3 + 4.
        /// </summary>
        private struct PropKeepout
        {
            /// <summary>Half-width of the gate -> spire assault corridor (the x ~ 0 lane).</summary>
            public float LaneHalf;
            public float SpireClear;
            public List<Vector3> Turrets;
            public Vector3 Staging;
            public bool HasStaging;
        }

        public struct LayoutContext
        {
            public float Radius;
            public float Innermost;
            public bool TwoGates;
            public int InnerLayers;
            public float GateWidth;
            public float SegmentWidth;
        }

        private static readonly string[] KayFolders =
        {
            "Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)",
            "Assets/Models/KayKit/dungeon/fbx(unity)",
            "Assets/Models/KayKit/dungeon",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/red",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/neutral",
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props",
            "Assets/Models/KayKit/medieval",
            "Assets/Models/KayKit/medieval/walls",
        };

        private static readonly string[] KayExts = { ".fbx", ".gltf", ".prefab" };

        private static readonly string[] SyntyFolders =
        {
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Buildings",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/SiegeEngines",
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners",
        };

        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static int _placed;
        private static int _missing;

        public static void Dress(SceneConfigDef def, Transform root, LayoutContext ctx)
        {
            if (def == null || root == null) return;
            _placed = 0;
            _missing = 0;
            Warned.Clear();

            var dress = def.raidDress;
            string kit = dress != null && !string.IsNullOrEmpty(dress.kit) ? dress.kit : KitFor(def.id);

            var approach = EnsureZone(root, "Zone_Approach");
            var gatehouse = EnsureZone(root, "Zone_Gatehouse");
            var courtyard = EnsureZone(root, "Zone_Courtyard");
            Transform choke = ctx.InnerLayers > 0 ? EnsureZone(root, "Zone_Choke") : null;
            Transform keep = ctx.InnerLayers > 0 ? EnsureZone(root, "Zone_Keep") : null;

            string gateTok = dress != null && !string.IsNullOrEmpty(dress.gate) ? dress.gate : DefaultGate(kit);
            string wallTok = dress != null && !string.IsNullOrEmpty(dress.wallModule) ? dress.wallModule : DefaultWall(kit);
            string floorTok = dress != null && !string.IsNullOrEmpty(dress.floor) ? dress.floor : DefaultFloor(kit);
            string towerTok = dress != null && !string.IsNullOrEmpty(dress.towersVisual) ? dress.towersVisual : DefaultTower(kit);

            DressAtmosphere(kit);
            HideWallRenderers(root);
            CladRing(root, wallTok, ctx.Radius, ctx.GateWidth, ctx.TwoGates, kit);
            if (ctx.InnerLayers > 0)
                CladRing(root, InnerWall(kit, wallTok), ctx.Innermost, Mathf.Max(MinGateWidth, ctx.GateWidth * 0.85f), false, kit, northGate: true);

            PlaceGatehouse(gatehouse, gateTok, kit, new Vector3(0f, 0f, -ctx.Radius), 0f, ctx.GateWidth, "south");
            if (ctx.TwoGates)
                PlaceGatehouse(gatehouse, gateTok, kit, new Vector3(0f, 0f, ctx.Radius), 180f, ctx.GateWidth, "north");

            TileApproachRoad(approach, floorTok, ctx.Radius, ctx.GateWidth);
            TileCourtyardRing(courtyard, floorTok, ctx);
            DressGateMouth(approach, gatehouse, kit, ctx);

            ReskinCombatArt(root, towerTok, def);
            // WO-1633: the keepout is gathered AFTER the generator has placed turrets, the spire
            // and the staging marker (RaidBaseGenerator.cs:403 / :401 / :422 all run before :424
            // calls Dress), so every exclusion below is read out of the built tree.
            ScatterProps(def, kit, approach, gatehouse, courtyard, choke, keep, ctx, BuildKeepout(root, ctx));
            PlaceGarrisonSlots(root, def, ctx);
            if (ctx.InnerLayers > 0)
                RaiseKeep(keep != null ? keep : root, kit, ctx);

            FlowTrace.Step(Sys,
                $"dressed '{def.id}' kit={kit} placed={_placed} missing={_missing} " +
                $"gateW={ctx.GateWidth:F2} zones=Approach,Gatehouse,Courtyard" +
                (ctx.InnerLayers > 0 ? ",Choke,Keep" : ""));
        }

        public static GameObject LoadVisual(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;

            if (token.StartsWith("Assets/") || token.StartsWith("assets/"))
            {
                var direct = AssetDatabase.LoadAssetAtPath<GameObject>(token);
                if (direct != null) return direct;
            }

            string file = token.Replace('\\', '/');
            int slash = file.LastIndexOf('/');
            if (slash >= 0) file = file.Substring(slash + 1);
            if (file.StartsWith("Structures/")) file = file.Substring("Structures/".Length);

            for (int i = 0; i < KayFolders.Length; i++)
            {
                for (int e = 0; e < KayExts.Length; e++)
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{KayFolders[i]}/{file}{KayExts[e]}");
                    if (go != null) return go;
                    string noExt = System.IO.Path.GetFileNameWithoutExtension(file);
                    go = AssetDatabase.LoadAssetAtPath<GameObject>($"{KayFolders[i]}/{noExt}{KayExts[e]}");
                    if (go != null) return go;
                }
            }

            for (int i = 0; i < SyntyFolders.Length; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{SyntyFolders[i]}/{file}.prefab");
                if (go != null) return go;
                string noExt = System.IO.Path.GetFileNameWithoutExtension(file);
                go = AssetDatabase.LoadAssetAtPath<GameObject>($"{SyntyFolders[i]}/{noExt}.prefab");
                if (go != null) return go;
            }

            string sc = AssetRoots.StructureContent + "/";
            string[] scTry =
            {
                sc + file,
                sc + file + ".prefab",
                sc + file + ".fbx",
                sc + System.IO.Path.GetFileNameWithoutExtension(file) + ".prefab",
                sc + System.IO.Path.GetFileNameWithoutExtension(file) + ".fbx",
                sc + "Tower_Medieval_Wood.prefab",
            };
            for (int i = 0; i < scTry.Length - 1; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(scTry[i]);
                if (go != null) return go;
            }

            var res = Resources.Load<GameObject>(token);
            if (res != null) return res;
            if (token.StartsWith("Structures/"))
                res = Resources.Load<GameObject>(token);
            return res;
        }

        public static GameObject InstantiateVisual(GameObject model, Transform parent, string name,
                                                   Vector3 pos, Quaternion rot, bool stripColliders)
        {
            if (model == null) return null;
            GameObject inst = null;
            Guard.Try(Sys, "instantiate " + name, () =>
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                if (inst == null) inst = Object.Instantiate(model, parent);
                inst.name = name;
                inst.transform.SetParent(parent, false);
                inst.transform.position = pos;
                inst.transform.rotation = rot;
                if (stripColliders) StripColliders(inst);
                SeatOnGround(inst);
                _placed++;
            });
            return inst;
        }

        // -- kit defaults -----------------------------------------------------

        private static string KitFor(string id)
        {
            if (id == "fortified_garrison") return "synty-castle";
            if (id == "mage_enclave") return "dungeon-stone";
            return "hexagon-green";
        }

        private static string DefaultGate(string kit)
        {
            if (kit == "synty-castle") return "SM_Bld_Castle_Wall_Gate_01";
            if (kit == "dungeon-stone") return "wall_straight_gate";
            return "wall_straight_gate";
        }

        private static string DefaultWall(string kit)
        {
            if (kit == "synty-castle") return "SM_Bld_Castle_Wall_01";
            if (kit == "dungeon-stone") return "wall_cracked";
            return "barrier";
        }

        private static string InnerWall(string kit, string outer)
        {
            if (kit == "dungeon-stone") return "wall";
            return outer;
        }

        private static string DefaultFloor(string kit)
        {
            if (kit == "hexagon-green") return "floor_dirt_large";
            return "floor_tile_large";
        }

        private static string DefaultTower(string kit)
        {
            if (kit == "synty-castle") return "SM_Bld_Castle_Wall_Tower_M_01";
            if (kit == "dungeon-stone") return "Tower_Castle_Square";
            return "building_watchtower_green";
        }

        // -- zones ------------------------------------------------------------

        private static Transform EnsureZone(Transform root, string name)
        {
            var existing = root.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        // -- walls / gate -----------------------------------------------------

        private static void DressAtmosphere(string kit)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            if (kit == "dungeon-stone")
            {
                RenderSettings.fogColor = new Color(0.14f, 0.13f, 0.16f);
                RenderSettings.fogStartDistance = 18f;
                RenderSettings.fogEndDistance = 88f;
                RenderSettings.ambientLight = new Color(0.22f, 0.20f, 0.24f);
            }
            else if (kit == "synty-castle")
            {
                RenderSettings.fogColor = new Color(0.58f, 0.55f, 0.50f);
                RenderSettings.fogStartDistance = 28f;
                RenderSettings.fogEndDistance = 115f;
                RenderSettings.ambientLight = new Color(0.38f, 0.36f, 0.33f);
            }
            else
            {
                RenderSettings.fogColor = new Color(0.66f, 0.58f, 0.42f);
                RenderSettings.fogStartDistance = 22f;
                RenderSettings.fogEndDistance = 95f;
                RenderSettings.ambientLight = new Color(0.42f, 0.36f, 0.26f);
            }
        }

        private static void HideWallRenderers(Transform root)
        {
            var walls = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < walls.Length; i++)
            {
                if (walls[i] == null || walls[i].name == null) continue;
                if (!walls[i].name.StartsWith("Wall_")) continue;
                var rends = walls[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                    if (rends[r] != null) rends[r].enabled = false;
            }
        }

        private static void CladRing(Transform root, string token, float radius, float gateWidth,
                                     bool twoGates, string kit, bool northGate = false)
        {
            var model = LoadVisual(token);
            if (model == null)
            {
                WarnMissing(token);
                return;
            }
            float piece = MeasureLongest(model);
            if (piece < 1.2f) piece = kit == "synty-castle" ? 5f : 4f;
            float run = radius * 2f;
            int n = Mathf.Max(2, Mathf.CeilToInt(run / piece));
            float step = run / n;
            var parent = EnsureZone(root, "Zone_Clad");

            for (int s = 0; s < 4; s++)
            {
                bool gated = northGate ? (s == 2) : ((s == 0) || (twoGates && s == 2));
                var rot = Quaternion.Euler(0f, 90f * s, 0f);
                var mid = rot * new Vector3(0f, 0f, -radius);
                var along = rot * Vector3.right;
                for (int i = 0; i < n; i++)
                {
                    float t = -run * 0.5f + (i + 0.5f) * step;
                    if (gated && Mathf.Abs(t) < gateWidth * 0.5f) continue;
                    var pos = mid + along * t;
                    var go = InstantiateVisual(model, parent, $"Clad_{s}_{i}", pos,
                                               rot, stripColliders: true);
                    if (go != null) FitPieceAlong(go, step * 0.98f, piece);
                }
            }
        }

        private static void PlaceGatehouse(Transform zone, string token, string kit, Vector3 pos,
                                           float yaw, float width, string side)
        {
            var model = LoadVisual(token);
            if (model == null)
            {
                model = LoadVisual("Gate_Medieval_Medium");
                if (model == null) WarnMissing(token);
            }
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var gate = InstantiateVisual(model, zone, "Gatehouse_" + side, pos, rot, stripColliders: false);
            if (gate != null)
            {
                int structure = LayerMask.NameToLayer("Structure");
                if (structure >= 0) gate.layer = structure;
            }

            string flankTok = kit == "synty-castle" ? "SM_Bld_Castle_Wall_Tower_S_01"
                : kit == "dungeon-stone" ? "wall_pillar" : "building_watchtower_green";
            var flank = LoadVisual(flankTok);
            if (flank == null) flank = LoadVisual("Tower_Wooden_Watchtower");
            float offset = Mathf.Max(3.2f, width * 0.55f);
            var right = rot * Vector3.right;
            if (flank != null)
            {
                InstantiateVisual(flank, zone, "GateFlank_" + side + "_L", pos - right * offset, rot, true);
                InstantiateVisual(flank, zone, "GateFlank_" + side + "_R", pos + right * offset, rot, true);
            }

            FlowTrace.Step(Sys, $"GATE {side} width={width:F2}m art={(model != null ? model.name : "MISSING")}");
        }

        /// <summary>
        /// Fill the empty plaza beside the gate (owner 2026-09-09). Spikes / crates
        /// sit on BOTH sides, outside and just inside, and never in the walkable
        /// slit (|x| &lt; MinGateWidth/2).
        /// </summary>
        private static void DressGateMouth(Transform approach, Transform gatehouse, string kit,
                                           LayoutContext ctx)
        {
            string spike = kit == "synty-castle" ? "SM_Prop_Spike_Fortification_01"
                         : kit == "dungeon-stone" ? "rubble_large" : "rubble_large";
            string crate = "crate_large";
            PlaceGateFlanks(approach, spike, crate, ctx, south: true);
            if (ctx.TwoGates) PlaceGateFlanks(gatehouse, spike, crate, ctx, south: false);
        }

        private static void PlaceGateFlanks(Transform zone, string spikeTok, string crateTok,
                                            LayoutContext ctx, bool south)
        {
            float sign = south ? -1f : 1f;
            float r = ctx.Radius;
            float flank = Mathf.Max(4.5f, ctx.GateWidth * 0.5f + 2.4f);
            float[] xs = { -flank, flank };
            float[] zs = { r * sign + 3.2f * sign, r * sign - 3.8f * sign };
            var spike = LoadVisual(spikeTok);
            var crate = LoadVisual(crateTok);
            int n = 0;
            for (int i = 0; i < xs.Length; i++)
            {
                for (int j = 0; j < zs.Length; j++)
                {
                    var pos = new Vector3(xs[i], 0f, zs[j]);
                    if (Mathf.Abs(pos.x) < MinGateWidth * 0.5f) continue;
                    var model = (i + j) % 2 == 0 ? spike : crate;
                    if (model == null) model = spike ?? crate;
                    if (model == null) continue;
                    InstantiateVisual(model, zone, "GateFlankProp", pos,
                                      Quaternion.Euler(0f, 90f * i, 0f), true);
                    n++;
                }
            }
            FlowTrace.Step(Sys, "gate mouth " + (south ? "south" : "north") + " props=" + n);
        }

        // -- floor ------------------------------------------------------------

        private static void TileApproachRoad(Transform zone, string token, float radius, float gateWidth)
        {
            var model = LoadVisual(token);
            if (model == null) { WarnMissing(token); return; }
            float span = MeasureLongest(model);
            if (span < 1f) span = 4f;
            float y = 0.04f;
            // Cover the south FACE, not a 12 m ribbon. Owner Seeker 2026-09-09 09:54
            // (proof/seeker-gate-empty-20260909.png): the gate sat in a grey hole
            // because the road was narrower than the opening + flanks.
            float half = Mathf.Max(14f, gateWidth * 0.5f + 8f);
            for (float z = -(radius + 10f); z <= -(radius - 4f) + 0.01f; z += span)
            {
                for (float x = -half; x <= half + 0.01f; x += span)
                    InstantiateVisual(model, zone, "Floor", new Vector3(x, y, z), Quaternion.identity, true);
            }
        }

        private static void TileCourtyardRing(Transform zone, string token, LayoutContext ctx)
        {
            var model = LoadVisual(token);
            if (model == null) { WarnMissing(token); return; }
            float span = MeasureLongest(model);
            if (span < 2.2f) span = 2.2f;
            float outer = ctx.Radius - 1.1f;
            // A ring of tiles left a brown hole in the middle (owner 2026-09-09
            // raid frame). Fill the disk so the pad is a camp floor, not a plane.
            float expected = ((2f * outer) / span) + 1f;
            if (expected * expected > 420f)
                span = (2f * outer) / (Mathf.Sqrt(420f) - 1f);
            float y = 0.04f;
            float outerSq = outer * outer;
            for (float x = -outer; x <= outer + 0.01f; x += span)
            {
                for (float z = -outer; z <= outer + 0.01f; z += span)
                {
                    if (x * x + z * z > outerSq) continue;
                    var pos = new Vector3(x, y, z);
                    InstantiateVisual(model, zone, "Floor", pos, Quaternion.identity, true);
                }
            }
        }

        // -- combat art reskin ------------------------------------------------

        private static void ReskinCombatArt(Transform root, string towerTok, SceneConfigDef def)
        {
            var towerModel = LoadVisual(towerTok);
            if (towerModel == null) towerModel = LoadVisual("Tower_Wooden_Watchtower");
            if (towerModel == null) towerModel = LoadVisual("Tower_Medieval_Wood");

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.name == null) continue;
                if (t.name.StartsWith("Watchtower_") || t.name.StartsWith("CornerPost_"))
                    ReplaceChildrenWith(t.gameObject, towerModel, t.name.StartsWith("Watchtower_"));
                if (t.name == RaidSpireName())
                {
                    string spireTok = def != null ? def.centralBuilding : null;
                    var spireModel = LoadVisual(MapCatalogArt(spireTok));
                    if (spireModel == null) spireModel = LoadVisual("ArcaneSpire_1");
                    ReplaceChildrenWith(t.gameObject, spireModel, keepComponents: true);
                }
            }
        }

        private static string RaidSpireName()
        {
            return "RaidSpire";
        }

        /// <summary>
        /// Map a structures-catalog id to the art token for the SPIRE SLOT.
        ///
        /// ⚠ THIS HAS EXACTLY ONE CALLER AND IT IS THE SPIRE (ReskinCombatArt, above). WO-1617's
        /// premise that the siege branches served "the turret slots" was checked and is FALSE -
        /// the turrets go through RaidBaseGenerator.PlaceTowerProp, which never comes here. So a
        /// `siege -> Ballista` row could only ever hand siege art to the camp's architectural
        /// centrepiece, which is precisely the defect: the Easy camp rendered a Ballista as its
        /// spire ('Structures/Ballista' in Builds/raidbase-bake.log).
        ///
        /// The fix routes the id through the ONE decider that owns "is this a siege machine?" -
        /// RaidBaseGenerator.ResolveSpireArtId - BEFORE the token mapping. That keeps the model
        /// the generator MEASURED for its height fit and the model this dresser INSTANTIATES in
        /// agreement (ReplaceChildrenWith inherits the host's fitted localScale), and it means
        /// the two ids live in one place instead of being restated here.
        ///
        /// The siege/catapult rows below are consequently UNREACHABLE for today's only caller.
        /// They are kept, not deleted, so that a future turret-side caller gets a correct answer
        /// rather than falling through to the raw id - but they are no longer the spire's answer.
        /// </summary>
        private static string MapCatalogArt(string catalogId)
        {
            if (string.IsNullOrEmpty(catalogId)) return "ArcaneSpire_1";
            catalogId = RaidBaseGenerator.ResolveSpireArtId(catalogId);

            // CATALOG FIRST (WO-1619, 2026-09-10). The substring table below answered exactly
            // four ids; anything else fell through to the RAW id, which LoadVisual could not
            // resolve, so the caller's null-fallback quietly reskinned the spire back to
            // ArcaneSpire_1 - the dresser undoing the model the generator had just measured and
            // fitted. That is what happened to the owner's ruled Forsaken Camp art
            // ('tower_ruined_watchtower'). The catalog already answers this question for the
            // generator (PlaceSpire reads entry.visualPrefabPath); asking it here too means the
            // two halves cannot disagree and a new spire id needs NO edit in this file.
            //
            // Behaviour for every id that was already live is UNCHANGED, by construction:
            // tower_arcane_spire authors "Structures/ArcaneSpire_1" and tower_ground_archer
            // authors "Structures/Tower_Wooden_Watchtower" - the same two tokens the branches
            // below return - and the siege/catapult ids never reach here because
            // ResolveSpireArtId substitutes them one line above. LoadVisual strips the
            // "Structures/" prefix itself.
            string catalogPath = RaidBaseGenerator.CatalogArtPath(catalogId);
            if (!string.IsNullOrEmpty(catalogPath)) return catalogPath;

            if (catalogId.IndexOf("siege", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Ballista";
            if (catalogId.IndexOf("catapult", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Catapult";
            if (catalogId.IndexOf("arcane", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "ArcaneSpire_1";
            if (catalogId.IndexOf("archer", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Tower_Wooden_Watchtower";
            return catalogId;
        }

        private static void ReplaceChildrenWith(GameObject host, GameObject model, bool keepComponents)
        {
            if (host == null || model == null) return;
            var doomed = new List<GameObject>();
            for (int i = 0; i < host.transform.childCount; i++)
            {
                var c = host.transform.GetChild(i);
                if (c != null) doomed.Add(c.gameObject);
            }
            for (int i = 0; i < doomed.Count; i++) Object.DestroyImmediate(doomed[i]);

            var vis = InstantiateVisual(model, host.transform, "Visual", host.transform.position,
                                        host.transform.rotation, stripColliders: !keepComponents);
            if (vis == null) return;
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            SeatOnGround(host);
        }

        // -- props ------------------------------------------------------------

        private static void ScatterProps(SceneConfigDef def, string kit, Transform approach,
                                         Transform gatehouse, Transform courtyard, Transform choke,
                                         Transform keep, LayoutContext ctx, PropKeepout keepout)
        {
            var list = new List<RaidDressPropDef>();
            if (def.raidDress != null && def.raidDress.props != null)
            {
                for (int i = 0; i < def.raidDress.props.Count; i++)
                    if (def.raidDress.props[i] != null) list.Add(def.raidDress.props[i]);
            }
            // WO-1635 acceptance #1 + #2 (2026-09-10): `raidDress.props` in scene-configs.json is
            // the ONE authority for raid props. The two legacy readers that used to sit here are
            // RETIRED, not merely disabled:
            //   * the `props.set` block (a List<string>, one instance each, ALWAYS zone Courtyard)
            //   * a kit-keyed emergency set hardcoded in C# (deleted; see the note where it stood,
            //     immediately above ZoneOf)
            // Both were silent third copies of authored content. Each fired only when the authored
            // array came back empty, so a config mid-edit dressed itself from DRIFTED content and
            // read as authored in the bake log - the same duplicated-state trap CLAUDE.md sections
            // 2, 5, 8 and 16 each describe. An unauthored raid config must now be LOUD and empty;
            // RaidBaseLayoutRegression.CaseSinglePropAuthority reds on it, and CaseOnePropReader
            // reds if either legacy reader is ever restored here.
            if (list.Count == 0)
            {
                FlowTrace.Warn(Sys, "props '" + def.id + "' kit=" + kit +
                               " authors NO raidDress.props - this courtyard dresses EMPTY. " +
                               "scene-configs.json is the only prop authority (WO-1635).");
            }

            int seed = StableHash(def.id);
            // Seeded, so a re-bake reproduces the identical courtyard (the arena's contract,
            // ProceduralSiegeArenaBuilder.cs:89 "Deterministic jitter so a rebuild reproduces
            // the same venue layout").
            var rng = new System.Random(seed);

            // The courtyard BAND: everything between the inner boundary (keep ring, or a clear
            // ring around the spire when there is no keep) and the wall line, minus the pads.
            float inner = ctx.InnerLayers > 0
                ? ctx.Innermost + CourtyardInnerPad
                : CourtyardSpirePad;
            float outer = Mathf.Max(inner + 4f, ctx.Radius - CourtyardWallPad);
            int rings = CoverRingPlacer.RingCount(inner, outer, CourtyardRingSpacing, CourtyardMaxRings);

            int placedProps = 0;
            int courtyardProps = 0;
            var tokens = new List<string>();

            // Cluster anchors must be spread over the COURTYARD entries, not over the whole prop
            // list. Indexing by the full list leaves whole quadrants bare - on the authored rows
            // that put every Extreme cluster on the east side and left Easy empty from 145 to 323
            // degrees, which is the same "empty dirt" this ticket exists to remove.
            int courtyardCount = 0;
            for (int i = 0; i < list.Count; i++)
                if (ZoneOf(list[i].zone, approach, gatehouse, courtyard, choke, keep) == courtyard)
                    courtyardCount++;
            int courtyardIndex = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                int n = Mathf.Clamp(p.count, 1, 16);
                var zone = ZoneOf(p.zone, approach, gatehouse, courtyard, choke, keep);
                var model = LoadVisual(p.token);
                if (model == null)
                {
                    // Keep the courtyard cursor moving even when the art is missing, so a missing
                    // pack does not bunch every surviving cluster into one arc.
                    if (zone == courtyard) courtyardIndex++;
                    WarnMissing(p.token);
                    continue;
                }

                int got;
                if (zone == courtyard)
                {
                    got = PlaceCourtyardCluster(model, zone, p, n, rng, inner, outer, rings,
                                                courtyardIndex, courtyardCount, keepout);
                    courtyardIndex++;
                    courtyardProps += got;
                }
                else
                {
                    got = PlaceZoneProps(model, zone, p, n, ctx, seed, i, keepout);
                }

                placedProps += got;
                if (got > 0) tokens.Add(p.token + "x" + got + (p.cover ? "*" : string.Empty));
            }

            // WO-1633 §2.4: the aggregate `dressed ... placed=N` line counts clad panels, floor
            // tiles, the gatehouse and the gate mouth too, so no bake log has ever stated a PROPS
            // count. This is that line. '*' marks a cover-class prop (colliders kept).
            Debug.Log($"[RaidBaseDresser] props '{def.id}': {placedProps} placed " +
                      $"(set={string.Join(", ", tokens)})");
            FlowTrace.Step(Sys,
                $"props '{def.id}' total={placedProps} courtyard={courtyardProps} " +
                $"rings={rings} band={inner:F1}-{outer:F1}m laneHalf={keepout.LaneHalf:F1}m " +
                $"turretsAvoided={(keepout.Turrets != null ? keepout.Turrets.Count : 0)}");
        }

        /// <summary>
        /// Gather every no-go region from the tree the generator just built. Nothing here is a
        /// hardcoded radius: turrets come from their <see cref="DefenseTower"/> components and
        /// staging from the marker's own transform, because both move per config.
        /// </summary>
        private static PropKeepout BuildKeepout(Transform root, LayoutContext ctx)
        {
            var k = new PropKeepout
            {
                // The gate mouths and the march they feed are all on the x ~ 0 axis (south gate at
                // -Radius, north gate at +Radius, inner keep gate north), so ONE corridor test
                // covers every lane. Width tracks the gate the player walks through, and clears
                // the WO-1609:110 (>= 4 m) / WO-1610:114 (>= 5 m) floors by a margin.
                LaneHalf = Mathf.Max(2.5f, ctx.GateWidth * 0.5f) + 1.0f,
                SpireClear = CourtyardSpirePad * 0.66f,
                Turrets = new List<Vector3>(),
                Staging = Vector3.zero,
                HasStaging = false,
            };

            Guard.Try(Sys, "keepout scan", () =>
            {
                var towers = root.GetComponentsInChildren<DefenseTower>(true);
                for (int i = 0; i < towers.Length; i++)
                    if (towers[i] != null) k.Turrets.Add(towers[i].transform.position);

                var staging = root.Find(StagingMarkerName);
                if (staging != null)
                {
                    k.Staging = staging.position;
                    k.HasStaging = true;
                }
            });

            return k;
        }

        /// <summary>True when a candidate slot is clear of the assault lane, the spire, every
        /// turret footprint and the staging pocket.</summary>
        private static bool AcceptPropSlot(Vector3 pos, PropKeepout k)
        {
            if (Mathf.Abs(pos.x) < k.LaneHalf) return false;
            if (new Vector2(pos.x, pos.z).magnitude < k.SpireClear) return false;

            if (k.Turrets != null)
            {
                for (int i = 0; i < k.Turrets.Count; i++)
                {
                    float dx = k.Turrets[i].x - pos.x;
                    float dz = k.Turrets[i].z - pos.z;
                    if (dx * dx + dz * dz < TurretClearPad * TurretClearPad) return false;
                }
            }

            if (k.HasStaging)
            {
                float sx = k.Staging.x - pos.x;
                float sz = k.Staging.z - pos.z;
                if (sx * sx + sz * sz < StagingClearPad * StagingClearPad) return false;
            }

            return true;
        }

        /// <summary>
        /// A cluster anchor that lands in the assault corridor is PUSHED to the corridor edge,
        /// not dropped. WO-1610:111 wants cover "along the SIDES of the south->north march, not
        /// on it", and WO-1610:46 wants the courtyard to be "a fight, not a runway" - dropping
        /// the cluster would satisfy the first and break the second.
        /// </summary>
        private static Vector3 PushOutOfLane(Vector3 pos, PropKeepout k)
        {
            if (Mathf.Abs(pos.x) >= k.LaneHalf) return pos;
            float side = pos.x >= 0f ? 1f : -1f;
            pos.x = side * (k.LaneHalf + 0.6f);
            return pos;
        }

        /// <summary>
        /// Place one authored entry as a CLUSTER on one of the concentric courtyard cover rings,
        /// using the arena's jitter + scale vocabulary through <see cref="CoverRingPlacer"/>.
        /// </summary>
        private static int PlaceCourtyardCluster(GameObject model, Transform zone, RaidDressPropDef p,
                                                 int n, System.Random rng, float inner, float outer,
                                                 int rings, int entryIndex, int entryCount,
                                                 PropKeepout keepout)
        {
            float ringR = CoverRingPlacer.BandRadius(inner, outer, entryIndex % rings, rings);
            float anchorAng = CoverRingPlacer.ClusterAnchorAngle(rng, entryIndex, entryCount, 0.42f);
            var anchor = PushOutOfLane(CoverRingPlacer.PolarPoint(ringR, anchorAng), keepout);

            float spread = ClusterSpreadBase + 0.35f * n;
            int placed = 0;

            for (int k = 0; k < n; k++)
            {
                var slot = CoverRingPlacer.Jittered(rng, anchor, spread, PropScaleMin, PropScaleMax);
                slot.Position = PushOutOfLane(slot.Position, keepout);

                // Re-roll a few times before giving a slot up, so a cluster near a turret thins
                // rather than vanishes.
                for (int attempt = 0; attempt < 5 && !AcceptPropSlot(slot.Position, keepout); attempt++)
                {
                    slot = CoverRingPlacer.Jittered(rng, anchor, spread * 1.4f, PropScaleMin, PropScaleMax);
                    slot.Position = PushOutOfLane(slot.Position, keepout);
                }
                if (!AcceptPropSlot(slot.Position, keepout)) continue;

                var go = InstantiateVisual(model, zone, "Prop_" + p.token, slot.Position,
                                           slot.Rotation, stripColliders: !p.cover);
                if (go == null) continue;

                // Scale AFTER instantiate (which already seated it), then re-seat: a scaled prop
                // that is not re-seated floats or sinks by its own bounds delta.
                go.transform.localScale *= slot.Scale;
                SeatOnGround(go);
                if (p.cover) EnsureCoverCollider(go);
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// Approach / Gatehouse / Choke / Keep keep their authored, deliberate placement
        /// (<see cref="PropSlot"/>) - a gatehouse flank prop is SUPPOSED to sit beside the gate,
        /// so the courtyard's lane push must not apply here. The staging pocket is still honoured,
        /// because Approach props are the only ones that reach out towards it.
        /// </summary>
        private static int PlaceZoneProps(GameObject model, Transform zone, RaidDressPropDef p, int n,
                                          LayoutContext ctx, int seed, int entryIndex, PropKeepout keepout)
        {
            int placed = 0;
            for (int k = 0; k < n; k++)
            {
                Vector3 pos = PropSlot(p.zone, ctx, seed + entryIndex * 17 + k * 31, k, n);
                if (IsSouthLane(pos, ctx)) pos.x += 6f * ((k & 1) == 0 ? 1f : -1f);

                if (keepout.HasStaging)
                {
                    float sx = keepout.Staging.x - pos.x;
                    float sz = keepout.Staging.z - pos.z;
                    if (sx * sx + sz * sz < StagingClearPad * StagingClearPad) continue;
                }

                var go = InstantiateVisual(model, zone, "Prop_" + p.token, pos,
                                           Quaternion.Euler(0f, (seed + k * 40) % 360, 0f),
                                           stripColliders: !p.cover);
                if (go == null) continue;
                if (p.cover) EnsureCoverCollider(go);
                placed++;
            }
            return placed;
        }

        /// <summary>
        /// A cover prop has to actually stop a body and an arrow. WO-1607 §6 "Cover stacks":
        /// "Troops can stand behind something ... with colliders"; WO-1609:104 "Colliders on";
        /// WO-1610:112 "collider stays". Keeping the prefab's own colliders is enough when it
        /// HAS any - many KayKit FBX do not, so fit an axis-aligned box to the renderer bounds.
        /// The box is a touch generous on a yawed mesh (an AABB of a rotated bound), which is the
        /// right way to err for cover.
        /// </summary>
        private static void EnsureCoverCollider(GameObject go)
        {
            if (go == null) return;
            var existing = go.GetComponentsInChildren<Collider>(true);
            if (existing != null && existing.Length > 0) return;

            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;

            Guard.Try(Sys, "cover collider " + go.name, () =>
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                var box = go.AddComponent<BoxCollider>();
                var lossy = go.transform.lossyScale;
                float sx = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
                float sy = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
                float sz = Mathf.Max(0.0001f, Mathf.Abs(lossy.z));

                box.center = go.transform.InverseTransformPoint(b.center);
                box.size = new Vector3(b.size.x / sx, b.size.y / sy, b.size.z / sz);
            });
        }

        // WO-1635 (2026-09-10): the kit-keyed emergency prop set that used to live here is DELETED.
        // It was reached only when a config authored no props at all, and it had already drifted
        // from the shipped JSON (its hexagon-green branch still handed out a banner/weaponrack set
        // that Easy's authored 10-entry array had moved past). A fallback that silently substitutes
        // stale content for authored content is not a safety net - it hides the empty row it was
        // meant to cover. ScatterProps now FlowTrace.Warns and dresses nothing; the empty row is
        // caught in the suite, not papered over at bake time. Do not reintroduce a C# prop set:
        // RaidBaseLayoutRegression.CaseOnePropReader reds on the identifier.

        private static Transform ZoneOf(string zone, Transform approach, Transform gatehouse,
                                        Transform courtyard, Transform choke, Transform keep)
        {
            if (string.Equals(zone, "Approach", System.StringComparison.OrdinalIgnoreCase)) return approach;
            if (string.Equals(zone, "Gatehouse", System.StringComparison.OrdinalIgnoreCase)) return gatehouse;
            if (string.Equals(zone, "Choke", System.StringComparison.OrdinalIgnoreCase) && choke != null) return choke;
            if (string.Equals(zone, "Keep", System.StringComparison.OrdinalIgnoreCase) && keep != null) return keep;
            return courtyard;
        }

        private static Vector3 PropSlot(string zone, LayoutContext ctx, int seed, int k, int n)
        {
            float ang = ((seed % 360) + k * (360f / Mathf.Max(1, n))) * Mathf.Deg2Rad;
            float r;
            if (string.Equals(zone, "Approach", System.StringComparison.OrdinalIgnoreCase))
                r = ctx.Radius + 4f + (k % 3);
            else if (string.Equals(zone, "Gatehouse", System.StringComparison.OrdinalIgnoreCase))
            {
                float flank = Mathf.Max(5f, ctx.GateWidth * 0.5f + 2.2f);
                return new Vector3((k % 2 == 0 ? -1f : 1f) * flank, 0f, -ctx.Radius + 2.5f);
            }
            else if (string.Equals(zone, "Keep", System.StringComparison.OrdinalIgnoreCase) && ctx.InnerLayers > 0)
                r = Mathf.Max(5f, ctx.Innermost * 0.55f);
            else if (string.Equals(zone, "Choke", System.StringComparison.OrdinalIgnoreCase) && ctx.InnerLayers > 0)
                r = Mathf.Max(6f, ctx.Innermost * 0.85f);
            else
                r = Mathf.Lerp(ctx.InnerLayers > 0 ? ctx.Innermost + 4f : 8f, ctx.Radius - 6f, 0.45f + (k % 3) * 0.15f);
            return new Vector3(Mathf.Sin(ang) * r, 0f, Mathf.Cos(ang) * r);
        }

        private static bool IsSouthLane(Vector3 pos, LayoutContext ctx)
        {
            return Mathf.Abs(pos.x) < 4f && pos.z < -4f;
        }

        // -- garrison slots ---------------------------------------------------

        private static void PlaceGarrisonSlots(Transform root, SceneConfigDef def, LayoutContext ctx)
        {
            int n = 8;
            if (def.garrison != null && def.garrison.composition != null)
            {
                n = 0;
                for (int i = 0; i < def.garrison.composition.Count; i++)
                    if (def.garrison.composition[i] != null)
                        n += Mathf.Max(0, def.garrison.composition[i].count);
            }
            n = Mathf.Clamp(n, 4, 28);

            int idx = 0;
            PlaceSlot(root, "GarrisonSlot_Gate_S_0", new Vector3(-3.2f, 0f, -ctx.Radius + 3f));
            PlaceSlot(root, "GarrisonSlot_Gate_S_1", new Vector3(3.2f, 0f, -ctx.Radius + 3f));
            idx = 2;
            if (ctx.TwoGates)
            {
                PlaceSlot(root, "GarrisonSlot_Gate_N_0", new Vector3(0f, 0f, ctx.Radius - 3f));
                idx = 3;
            }
            int keepSlots = ctx.InnerLayers > 0 ? Mathf.Min(4, n / 4) : 0;
            int yard = n - idx - keepSlots;
            for (int i = 0; i < yard; i++, idx++)
            {
                float ang = (i / (float)Mathf.Max(1, yard)) * Mathf.PI * 2f + 0.4f;
                float r = Mathf.Max(8f, ctx.Radius * 0.55f);
                var pos = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (IsSouthLane(pos, ctx)) pos.x += 5f;
                PlaceSlot(root, "GarrisonSlot_Yard_" + i, pos);
            }
            for (int i = 0; i < keepSlots; i++, idx++)
            {
                float ang = (i / (float)Mathf.Max(1, keepSlots)) * Mathf.PI * 2f;
                float r = Mathf.Max(4f, ctx.Innermost * 0.4f);
                PlaceSlot(root, "GarrisonSlot_Keep_" + i, new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r));
            }
        }

        private static void PlaceSlot(Transform root, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
        }

        // -- keep platform ----------------------------------------------------

        private static void RaiseKeep(Transform keep, string kit, LayoutContext ctx)
        {
            float half = Mathf.Max(6f, ctx.Innermost * 0.55f);
            float height = kit == "dungeon-stone" ? 0.8f : 1.5f;
            var plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plat.name = "KeepPlatform";
            plat.transform.SetParent(keep, false);
            plat.transform.localScale = new Vector3(half * 2f, height, half * 2f);
            plat.transform.position = new Vector3(0f, height * 0.5f, 0f);
            ApplyUrp(plat, kit == "dungeon-stone"
                ? new Color(0.28f, 0.26f, 0.32f)
                : new Color(0.38f, 0.34f, 0.30f));
            MagentaGuard.ProtectPrimitiveArt(plat, "RaidBaseDresser.KeepPlatform");

            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "KeepRamp";
            ramp.transform.SetParent(keep, false);
            ramp.transform.localScale = new Vector3(4.2f, 0.35f, half * 0.9f);
            ramp.transform.position = new Vector3(0f, height * 0.35f, -half - 1.5f);
            ramp.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
            ApplyUrp(ramp, new Color(0.32f, 0.30f, 0.28f));
            MagentaGuard.ProtectPrimitiveArt(ramp, "RaidBaseDresser.KeepRamp");

            if (kit == "dungeon-stone")
            {
                var ceil = LoadVisual("ceiling_tile");
                if (ceil != null)
                {
                    float span = MeasureLongest(ceil);
                    if (span < 1f) span = 4f;
                    float y = 4.2f;
                    for (float x = -half + span * 0.5f; x <= half; x += span)
                    for (float z = -half + span * 0.5f; z <= half; z += span)
                    {
                        var go = InstantiateVisual(ceil, keep, "KeepCeiling", new Vector3(x, y, z),
                                                   Quaternion.identity, true);
                        if (go != null) StripColliders(go);
                    }
                }
                var col = LoadVisual("pillar_decorated");
                if (col == null) col = LoadVisual("pillar");
                if (col != null)
                {
                    float d = half * 0.7f;
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(-d, 0f, -d), Quaternion.identity, true);
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(d, 0f, -d), Quaternion.identity, true);
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(-d, 0f, d), Quaternion.identity, true);
                    InstantiateVisual(col, keep, "KeepCol", new Vector3(d, 0f, d), Quaternion.identity, true);
                }
            }
        }

        // -- helpers ----------------------------------------------------------

        private static void ApplyUrp(GameObject go, Color c)
        {
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) return;
            var m = MagentaGuard.BuildUrpLitMaterial(c);
            if (m != null) r.sharedMaterial = m;
        }

        private static void StripColliders(GameObject go)
        {
            if (go == null) return;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) Object.DestroyImmediate(cols[i]);
        }

        private static void SeatOnGround(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            go.transform.position += new Vector3(0f, -b.min.y, 0f);
        }

        private static float MeasureLongest(GameObject model)
        {
            var tmp = Object.Instantiate(model);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            float w = 4f;
            var rends = tmp.GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                w = Mathf.Max(b.size.x, b.size.z);
            }
            Object.DestroyImmediate(tmp);
            return w;
        }

        private static void FitPieceAlong(GameObject go, float target, float native)
        {
            if (go == null || native < 0.2f) return;
            float f = Mathf.Clamp(target / native, 0.6f, 1.4f);
            go.transform.localScale = go.transform.localScale * f;
        }

        private static void WarnMissing(string token)
        {
            _missing++;
            if (Warned.Add(token))
            {
                Debug.LogWarning($"[RaidBase] art '{token}' not found (KayKit/Synty gitignored or StructureContent miss) - skipped.");
                FlowTrace.Warn(Sys, "missing token=" + token);
            }
        }

        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 17;
            unchecked
            {
                int h = 23;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h & 0x7fffffff;
            }
        }
    }
}
