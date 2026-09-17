// =============================================================================
// DungeonWallLosRegression [dungeon-wall-los] -- WO-1837.
// Marker: DUNGEON_WALL_LOS_OK / DUNGEON_WALL_LOS_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Dungeons, so it drives the
// REAL seam rather than a copy of it).
//
// WHAT THIS PINS AND WHY IT EXISTS
// WO-1829 fixed "the hero targets through a closed DOOR" by putting the door leaf on
// the "Structure" layer, and DungeonDoorShapeRegression pins THAT. Days later the
// owner hit the same defect on a WALL: a solid socket seal plugging a dead-end
// doorway in dg_ember_deep, with the hero locked onto an Orc Raider straight through
// it. One fix, one oracle, one prefab -- and the class of defect walked to the next
// piece of geometry, because what was pinned was the door, not the RULE.
//
// So this suite pins the RULE, on the WALL geometry, BEHAVIOURALLY:
//   Case 1 -- DungeonBakerChecks.SealSocket's seal wall (the exact WO-1837 piece)
//             ends up on "Structure", and a Physics.Linecast masked to Structure --
//             the same call HeroTargetIndicator.HasLoS makes -- actually HITS it.
//             A layer-equality assertion alone would not catch a seal that lost its
//             collider; this drives the physics.
//   Case 2 -- the same linecast through a DefaultDungeonRoomsBuilder-shaped room
//             wall (Wall_N) is blocked once the shared owner has been applied, and
//             is NOT blocked while it sits on Default. The negative half is the
//             point: it proves the linecast is genuinely layer-gated, so Case 1
//             cannot pass for an unrelated reason.
//   Case 3 -- the name discriminator keeps HORIZONTAL geometry off Structure.
//             Floor_Lower / Ceiling / Step_00 / RampCollider must be rejected. This
//             is not pedantry: SmartMobileCamera's occluder mask includes Structure
//             (SmartMobileCamera.cs:1323), so a floor on that layer would make the
//             camera fade the ground the hero is standing on.
//   Case 4 -- ComposedDungeonHost.Install's load-time sweep corrects a STALE BAKE.
//             Builds a DungeonCompose_-shaped root holding a Default-layer seal wall,
//             runs the sweep, and asserts the wall moved. This is the half that
//             reaches a build the owner already has: the generators write to disk, so
//             without a load-time net the fix is inert on every already-baked dungeon.
//   Case 5 -- a render-only wall (no collider) is NOT moved. LoS and collision must
//             agree; an optical blocker you can walk through is its own bug.
//   Case 6 -- INFORMATIONAL BAKE AUDIT. Walks Assets/Dungeon/Rooms/*.prefab and
//             counts collider-bearing wall children still on Default. It REPORTS
//             rather than fails, because Case 4's load-time sweep is what makes the
//             game correct and the on-disk staleness is a bake-freshness matter, not
//             a code defect. It exists so the stale count is visible in the gate log
//             instead of being quietly forgotten.
//
// ⚠ DELIBERATELY NOT A SOURCE-TEXT LINT. CLAUDE.md sec.7 calls out "a source-text
// lint as the dock's coverage" as false coverage, and the same applies here: a grep
// for NameToLayer("Structure") would have passed on WO-1829's fix while the wall
// beside the door was still wide open. Every case below drives real objects.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-wall-los suite", () => { if (!DeNelle.Editor.Regression.DungeonWallLosRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-wall-los] " + r); });
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Dungeons.RoomForge;

namespace DeNelle.Editor.Regression
{
    public static class DungeonWallLosRegression
    {
        /// <summary>Where DefaultDungeonRoomsBuilder writes its baked room prefabs.</summary>
        private const string RoomsFolder = "Assets/Dungeon/Rooms";

        public static bool Run(out string reason)
        {
            var log = new StringBuilder();
            log.AppendLine("--- DUNGEON WALL LoS (walls + socket seals block the Structure-masked linecast) ---");
            var failures = new List<string>();
            var scratch = new List<GameObject>();

            int structureLayer = DungeonStructureLayer.Resolve();
            int structureMask = structureLayer >= 0 ? (1 << structureLayer) : 0;

            try
            {
                if (structureLayer < 0)
                {
                    // Not a silent skip: the whole LoS gate is off in this project and that is a
                    // finding, not a pass.
                    failures.Add($"the project declares no '{DungeonStructureLayer.LayerName}' layer — " +
                                 "HeroTargetIndicator.HasLoS has nothing to mask to and NOTHING blocks " +
                                 "hero targeting anywhere in the game");
                }
                else
                {
                    log.AppendLine($"  '{DungeonStructureLayer.LayerName}' resolves to layer {structureLayer} " +
                                   $"(mask {structureMask}).");

                    // ── Case 1: the real SealSocket seam, driven end to end ──────────
                    var socketGo = NewScratch(scratch, "~WallLos_Socket");
                    // NEVER the origin. Batchmode runs this suite in whatever scene is open; in town
                    // the Heart of Elarion sits at (0,0,0) with 158 Wall_* colliders already on
                    // Structure, so a Structure-masked cast there would report "blocked" on
                    // unrelated geometry and this case would pass for the wrong reason — the exact
                    // false pass the negative half of Case 2 exists to rule out.
                    socketGo.transform.position = new Vector3(-900f, 0f, -900f);
                    socketGo.transform.rotation = Quaternion.identity;
                    var socket = socketGo.AddComponent<RoomSocket>();
                    // ⛔ A DOOR-NAMED SOCKET ID ON PURPOSE — this is the regression, not decoration.
                    // RoomSocket.id's tooltip says "e.g. north_door_01", and dg_ember_deep.unity
                    // carries exactly `Seal_s_door_01`. The first cut of the name discriminator ran
                    // its "door" exclusion BEFORE the Seal_ test, so IsWallName("Seal_s_door_01")
                    // was false and the load-time sweep skipped the owner's actual wall. Using the
                    // real name from the owner's dungeon means that ordering bug cannot come back.
                    socket.id = "s_door_01";
                    socket.type = RoomSocketType.Door;
                    socket.isSecret = false;
                    socket.halfWidth = RoomForgeCanon.DoorGap * 0.5f;
                    socket.commonDoor = false;

                    bool spawned = DungeonBakerChecks.SealSocket(socket);
                    if (!spawned)
                        failures.Add("SealSocket built NO geometry for a normal unmated door socket — " +
                                     "the WO-1837 seal wall is the piece under test");
                    if (socket.matedTo != "SEALED_WALL")
                        failures.Add($"SealSocket left matedTo='{socket.matedTo}', expected SEALED_WALL");

                    Transform seal = socketGo.transform.Find($"Seal_{socket.id}");
                    if (seal == null)
                    {
                        failures.Add($"no Seal_{socket.id} child after SealSocket — cannot test the seal's LoS");
                    }
                    else
                    {
                        if (seal.gameObject.layer != structureLayer)
                            failures.Add($"THE WO-1837 DEFECT: socket seal '{seal.name}' is on layer " +
                                         $"{seal.gameObject.layer} " +
                                         $"('{LayerMask.LayerToName(seal.gameObject.layer)}'), expected " +
                                         $"Structure {structureLayer}. HeroTargetIndicator.HasLoS masks its " +
                                         "blocker linecast to Structure, so the hero locks and attacks " +
                                         "through this wall.");

                        if (seal.GetComponent<Collider>() == null)
                            failures.Add($"socket seal '{seal.name}' carries no collider — a layer " +
                                         "assignment on a collider-less object blocks nothing");

                        // BEHAVIOURAL: the same call HasLoS makes, across the seal.
                        bool blocked = CastAcross(seal, structureMask, out string castNote);
                        if (!blocked)
                            failures.Add($"a Structure-masked Physics.Linecast passed THROUGH the socket seal " +
                                         $"({castNote}) — this is exactly what lets the hero target an enemy " +
                                         "in the next room through solid wall");
                        log.AppendLine($"  case1 seal='{seal.name}' layer={seal.gameObject.layer} " +
                                       $"blocked={blocked} {castNote}");
                    }

                    // ── Case 2: a room wall, both polarities ─────────────────────────
                    // Parked 500 m from Case 1's seal ON PURPOSE. Both pieces are live colliders in
                    // the same physics scene, and Case 1's seal IS on Structure — sharing the origin
                    // would let this case's Structure-masked cast hit the SEAL and report the
                    // negative half as "blocked on Default", failing a correct build.
                    var wall = NewScratchPrimitive(scratch, "Wall_N");
                    wall.transform.position = new Vector3(500f, RoomForgeCanon.WallHeight * 0.5f, 500f);
                    wall.transform.localScale = new Vector3(6f, RoomForgeCanon.WallHeight, 0.4f);
                    wall.layer = 0;

                    // NEGATIVE half — proves the cast really is layer-gated, so Case 1's pass
                    // cannot be an artefact of the cast hitting anything at all.
                    bool blockedOnDefault = CastAcross(wall.transform, structureMask, out _);
                    if (blockedOnDefault)
                        failures.Add("a Structure-masked linecast reported a hit on a wall sitting on " +
                                     "Default — the mask is not doing what this suite assumes, so none of " +
                                     "its other conclusions hold");

                    if (!DungeonStructureLayer.IsWallName("Wall_N"))
                        failures.Add("IsWallName rejected 'Wall_N' — the room builders' own wall naming " +
                                     "must be recognised or the sweep covers nothing");
                    DungeonStructureLayer.Apply(wall, "regression probe");

                    if (wall.layer != structureLayer)
                        failures.Add($"Apply left 'Wall_N' on layer {wall.layer}, expected {structureLayer}");
                    bool blockedOnStructure = CastAcross(wall.transform, structureMask, out string wallNote);
                    if (!blockedOnStructure)
                        failures.Add($"a Structure-masked linecast passed through a Wall_N on the Structure " +
                                     $"layer ({wallNote})");
                    log.AppendLine($"  case2 Wall_N blockedOnDefault={blockedOnDefault} " +
                                   $"blockedOnStructure={blockedOnStructure} {wallNote}");

                    // ── Case 3: horizontal geometry stays OFF Structure ──────────────
                    // SmartMobileCamera.cs:1323 reads Structure as a camera occluder; a floor
                    // here would fade the ground under the hero.
                    string[] mustReject = { "Floor_Lower", "Floor_Upper_W", "Ceiling", "Step_00",
                                            "RampCollider", "Dressing_barrel_large_6",
                                            "Dressing_torch_mounted_0", "Chest_barrel" };
                    foreach (string n in mustReject)
                        if (DungeonStructureLayer.IsWallName(n))
                            failures.Add($"IsWallName ACCEPTED '{n}' — horizontal/prop geometry on Structure " +
                                         "makes SmartMobileCamera treat it as an occluder and fade the " +
                                         "ground or props the hero stands among");

                    // The door is excluded on purpose: CommonDungeonDoor owns its leaf's layer and
                    // toggles the blocker on open/close. Two owners of one door is the bug.
                    if (DungeonStructureLayer.IsWallName("Door_Leaf"))
                        failures.Add("IsWallName ACCEPTED 'Door_Leaf' — doors are owned by " +
                                     "CommonDungeonDoor (WO-1829), not by this sweep");

                    // Every Seal_* name here is a REAL string read out of dg_ember_deep.unity.
                    // "Seal_s_door_01" is the one the exclusion list used to swallow — keep it.
                    string[] mustAccept = { "Wall_N", "Wall_S_L", "Wall_E_Mid", "Seal_s_lower_w",
                                            "Seal_s_upper_e", "Seal_s_door_01", "Seal_north_door_01",
                                            "wall_corner_inner" };
                    foreach (string n in mustAccept)
                        if (!DungeonStructureLayer.IsWallName(n))
                            failures.Add($"IsWallName REJECTED '{n}' — that is live wall/seal geometry " +
                                         "(baked room prefabs + dg_ember_deep) and it would stay invisible " +
                                         "to the hero's LoS cast");
                    log.AppendLine($"  case3 discriminator: {mustAccept.Length} accepted shapes, " +
                                   $"{mustReject.Length + 1} rejected shapes checked.");

                    // ── Case 4: the load-time sweep repairs a stale bake ─────────────
                    var composeRoot = NewScratch(scratch, "DungeonCompose_wo1837_probe");
                    var staleSeal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    staleSeal.name = "Seal_s_stale_bake";
                    staleSeal.transform.SetParent(composeRoot.transform, false);
                    staleSeal.layer = 0;
                    var staleWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    staleWall.name = "Wall_W";
                    staleWall.transform.SetParent(composeRoot.transform, false);
                    staleWall.layer = 0;
                    var staleFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    staleFloor.name = "Floor_Lower";
                    staleFloor.transform.SetParent(composeRoot.transform, false);
                    staleFloor.layer = 0;

                    int moved = DungeonStructureLayer.ApplyToWalls(composeRoot.transform, "regression probe");
                    if (moved != 2)
                        failures.Add($"the load-time sweep moved {moved} object(s), expected exactly 2 " +
                                     "(Seal_s_stale_bake + Wall_W, never Floor_Lower)");
                    if (staleSeal.layer != structureLayer)
                        failures.Add("the load-time sweep left a stale-bake socket seal on Default — every " +
                                     "already-baked dungeon would still target through walls");
                    if (staleWall.layer != structureLayer)
                        failures.Add("the load-time sweep left a stale-bake Wall_W on Default");
                    if (staleFloor.layer == structureLayer)
                        failures.Add("the load-time sweep moved Floor_Lower onto Structure — the camera " +
                                     "occluder mask reads that layer and would fade the floor");

                    // Idempotent: a second pass must be a no-op, so the net is not a second owner.
                    int again = DungeonStructureLayer.ApplyToWalls(composeRoot.transform, "regression probe (2nd)");
                    if (again != 0)
                        failures.Add($"the sweep is NOT idempotent — a second pass moved {again} object(s)");
                    log.AppendLine($"  case4 stale-bake sweep moved={moved} (expected 2), second pass={again} " +
                                   "(expected 0).");

                    // ── Case 5: render-only wall is left alone ───────────────────────
                    var ghost = NewScratchPrimitive(scratch, "Wall_Ghost_Decor");
                    var ghostCol = ghost.GetComponent<Collider>();
                    if (ghostCol != null) UnityEngine.Object.DestroyImmediate(ghostCol);
                    ghost.layer = 0;
                    if (DungeonStructureLayer.IsSolid(ghost))
                        failures.Add("IsSolid reported a collider-less wall as solid");
                    int ghostMoved = DungeonStructureLayer.ApplyToWalls(ghost.transform, "regression probe");
                    if (ghostMoved != 0 || ghost.layer == structureLayer)
                        failures.Add("the sweep moved a RENDER-ONLY wall onto Structure — LoS would then " +
                                     "block on something the hero can walk straight through");
                    log.AppendLine($"  case5 render-only wall moved={ghostMoved} (expected 0).");
                }

                // ── Case 6: informational bake-freshness audit ───────────────────────
                log.AppendLine(AuditBakedRooms(structureLayer));
            }
            catch (System.Exception ex)
            {
                failures.Add($"THREW {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                for (int i = 0; i < scratch.Count; i++)
                    if (scratch[i] != null) UnityEngine.Object.DestroyImmediate(scratch[i]);
            }

            if (failures.Count > 0)
            {
                reason = "dungeon-wall-los: " + string.Join(" | ", failures);
                Debug.LogError(log.ToString() + "DUNGEON_WALL_LOS_FAIL: " + reason);
                return false;
            }

            reason = "dungeon walls + socket seals block the Structure-masked linecast: SealSocket's seal " +
                     "and Wall_* land on Structure and stop a real Physics.Linecast, horizontal geometry " +
                     "and render-only walls are excluded, and the load-time sweep repairs a stale bake " +
                     "idempotently.";
            Debug.Log(log.ToString() + "DUNGEON_WALL_LOS_OK");
            return true;
        }

        /// <summary>
        /// Fires the same shape of call <see cref="UnityEngine.Physics.Linecast"/> that
        /// HeroTargetIndicator.HasLoS makes — masked, triggers ignored — straight through
        /// <paramref name="piece"/> along its thinnest local axis, and reports whether it was
        /// blocked. Endpoints are derived from the piece's own renderer bounds so nothing here
        /// re-types a canon dimension.
        /// </summary>
        private static bool CastAcross(Transform piece, int mask, out string note)
        {
            note = "no bounds";
            if (piece == null || mask == 0) return false;

            var rdr = piece.GetComponentInChildren<Renderer>(true);
            if (rdr == null) return false;
            Bounds b = rdr.bounds;

            // Cross the THIN axis: that is the direction a hero on one side looks through.
            Vector3 dir = b.size.x <= b.size.z ? Vector3.right : Vector3.forward;
            float span = Mathf.Max(b.size.x, b.size.z) + 4f;
            Vector3 mid = b.center;
            Vector3 a = mid - dir * span * 0.5f;
            Vector3 c = mid + dir * span * 0.5f;

            // EDIT MODE: nothing has stepped the physics scene, so collider poses can still be
            // one edit behind the transforms this suite just wrote. Without this the casts can
            // miss a correctly-placed wall and the suite fails a good build.
            Physics.SyncTransforms();

            bool hit = Physics.Linecast(a, c, out RaycastHit info, mask, QueryTriggerInteraction.Ignore);
            if (!hit)
            {
                note = $"cast {a} -> {c} hit NOTHING";
                return false;
            }

            // The hit must be THIS piece. Whatever scene batchmode has open contributes its own
            // Structure colliders, and "something blocked the cast" is not the claim — "this wall
            // blocked the cast" is. Without this the suite can go green on a town wall.
            bool mine = info.collider != null && info.collider.transform.IsChildOf(piece);
            note = mine
                ? $"cast {a} -> {c} hit '{info.collider.name}' (the piece under test)"
                : $"cast {a} -> {c} hit '{(info.collider != null ? info.collider.name : "null")}' " +
                  "which is NOT the piece under test — foreign Structure geometry in the open scene";
            return mine;
        }

        /// <summary>
        /// Reads the baked room prefabs' YAML and counts wall GameObjects still on Default.
        /// INFORMATIONAL: the load-time sweep (case 4) is what makes the game correct, so a
        /// stale bake is a freshness note, not a code failure. Printed so the count is visible
        /// in the gate log rather than quietly forgotten.
        /// </summary>
        private static string AuditBakedRooms(int structureLayer)
        {
            if (!Directory.Exists(RoomsFolder))
                return $"  case6 bake audit: '{RoomsFolder}' not present — nothing to audit.";

            string[] files = Directory.GetFiles(RoomsFolder, "*.prefab", SearchOption.TopDirectoryOnly);
            var goBlock = new Regex(@"---\s*!u!1\s*&\d+\s*\r?\nGameObject:(.*?)(?=\r?\n---\s|\z)",
                                    RegexOptions.Singleline | RegexOptions.Compiled);
            var nameRx = new Regex(@"m_Name:\s*(.*)", RegexOptions.Compiled);
            var layerRx = new Regex(@"m_Layer:\s*(\d+)", RegexOptions.Compiled);

            int walls = 0, stale = 0, onStructure = 0;
            var staleFiles = new List<string>();

            foreach (string f in files)
            {
                string text;
                try { text = File.ReadAllText(f); }
                catch { continue; }
                bool fileStale = false;
                foreach (Match m in goBlock.Matches(text))
                {
                    string block = m.Groups[1].Value;
                    Match nm = nameRx.Match(block);
                    Match ly = layerRx.Match(block);
                    if (!nm.Success || !ly.Success) continue;
                    if (!DungeonStructureLayer.IsWallName(nm.Groups[1].Value.Trim())) continue;
                    walls++;
                    if (int.TryParse(ly.Groups[1].Value, out int layer) && layer == structureLayer) onStructure++;
                    else { stale++; fileStale = true; }
                }
                if (fileStale) staleFiles.Add(Path.GetFileNameWithoutExtension(f));
            }

            if (stale == 0)
                return $"  case6 bake audit: {files.Length} room prefab(s), {walls} wall GameObject(s), " +
                       "ALL on Structure — the bake is fresh.";

            string sample = string.Join(", ", staleFiles.GetRange(0, Mathf.Min(6, staleFiles.Count)));
            Debug.LogWarning($"[dungeon-wall-los] STALE BAKE (informational, WO-1837): {stale} of {walls} wall " +
                             $"GameObject(s) across {staleFiles.Count} baked room prefab(s) are still on " +
                             $"layer 0 instead of Structure ({structureLayer}). The load-time sweep " +
                             "(ComposedDungeonHost.Install / DungeonController.Start) corrects these in " +
                             "memory, so the game is correct; re-run 'Defenders/Dungeon/Build Default Room " +
                             $"Prefabs' to make the on-disk artifact match. Prefabs: {sample}.");

            return $"  case6 bake audit: {walls} wall GameObject(s) in {files.Length} room prefab(s) — " +
                   $"{onStructure} on Structure, {stale} STALE on Default (repaired at load; re-bake to " +
                   $"clear). Prefabs: {sample}.";
        }

        private static GameObject NewScratch(List<GameObject> bag, string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            bag.Add(go);
            return go;
        }

        private static GameObject NewScratchPrimitive(List<GameObject> bag, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.hideFlags = HideFlags.HideAndDontSave;
            bag.Add(go);
            return go;
        }
    }
}
