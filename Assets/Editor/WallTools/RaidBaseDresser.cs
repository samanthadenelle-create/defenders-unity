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
            ScatterProps(def, kit, approach, gatehouse, courtyard, choke, keep, ctx);
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
                                         Transform keep, LayoutContext ctx)
        {
            var list = new List<RaidDressPropDef>();
            if (def.raidDress != null && def.raidDress.props != null)
            {
                for (int i = 0; i < def.raidDress.props.Count; i++)
                    if (def.raidDress.props[i] != null) list.Add(def.raidDress.props[i]);
            }
            if (list.Count == 0 && def.props != null && def.props.set != null)
            {
                for (int i = 0; i < def.props.set.Count; i++)
                {
                    if (string.IsNullOrEmpty(def.props.set[i])) continue;
                    list.Add(new RaidDressPropDef
                    {
                        token = def.props.set[i],
                        count = 1,
                        zone = "Courtyard"
                    });
                }
            }
            if (list.Count == 0) list.AddRange(DefaultProps(kit));

            int seed = StableHash(def.id);
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                int n = Mathf.Clamp(p.count, 1, 16);
                var zone = ZoneOf(p.zone, approach, gatehouse, courtyard, choke, keep);
                var model = LoadVisual(p.token);
                if (model == null) { WarnMissing(p.token); continue; }
                for (int k = 0; k < n; k++)
                {
                    Vector3 pos = PropSlot(p.zone, ctx, seed + i * 17 + k * 31, k, n);
                    if (IsSouthLane(pos, ctx)) pos.x += 6f * ((k & 1) == 0 ? 1f : -1f);
                    InstantiateVisual(model, zone, "Prop_" + p.token, pos, Quaternion.Euler(0f, (seed + k * 40) % 360, 0f), true);
                }
            }
        }

        private static List<RaidDressPropDef> DefaultProps(string kit)
        {
            var list = new List<RaidDressPropDef>();
            if (kit == "hexagon-green")
            {
                list.Add(new RaidDressPropDef { token = "building_tent_green", count = 4, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "barrel_large", count = 4, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "crate_large", count = 4, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "banner_green", count = 2, zone = "Approach" });
                list.Add(new RaidDressPropDef { token = "rubble_large", count = 3, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "weaponrack", count = 2, zone = "Courtyard" });
            }
            else if (kit == "synty-castle")
            {
                list.Add(new RaidDressPropDef { token = "barracks", count = 1, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "SM_Prop_Spike_Fortification_01", count = 3, zone = "Approach" });
                list.Add(new RaidDressPropDef { token = "barrel_large", count = 4, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "crate_large", count = 4, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "weaponrack", count = 3, zone = "Courtyard" });
            }
            else
            {
                list.Add(new RaidDressPropDef { token = "pillar_decorated", count = 6, zone = "Courtyard" });
                list.Add(new RaidDressPropDef { token = "banner_white", count = 4, zone = "Keep" });
                list.Add(new RaidDressPropDef { token = "torch_mounted", count = 6, zone = "Keep" });
                list.Add(new RaidDressPropDef { token = "chest_gold", count = 2, zone = "Keep" });
                list.Add(new RaidDressPropDef { token = "rubble_large", count = 3, zone = "Courtyard" });
            }
            return list;
        }

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
