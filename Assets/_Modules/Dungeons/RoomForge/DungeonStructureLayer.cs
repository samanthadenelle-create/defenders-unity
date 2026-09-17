// =============================================================================
// DungeonStructureLayer — THE single owner of "this dungeon collider belongs on
// the Structure physics layer". WO-1837.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Dungeons (runtime). Lives here, NOT in DeNelle.Editor, so the
// runtime load path, the editor room/stair builders (DeNelle.Editor references
// DeNelle.Dungeons) and the oracle (DeNelle.EditorRegression references it too)
// all call ONE implementation. UnityEngine-only — no UnityEditor — exactly like
// DungeonBakerChecks next door, and for the same reason.
//
// ⛔ WHY THIS FILE EXISTS — THE DEFECT CLASS THAT HAS NOW RECURRED TWICE
// HeroTargetIndicator.HasLoS (Assets/_Modules/Village/Hero/HeroTargetIndicator.cs
// :1543-1563) masks its blocker Physics.Linecast to the "Structure" layer ONLY.
// A solid collider that is not on that layer is INVISIBLE to the cast, so the
// hero freely auto-acquires, locks and attacks an enemy straight through it —
// with no error, no warning and nothing on screen. The only detector is the
// owner's eyes, which is precisely what CLAUDE.md §14 exists to never rely on.
//   • WO-1829 found it on the composed dungeon DOOR (CommonDungeonDoor's leaf sat
//     on Default). Fixed by copying WallSegment.cs:646-647 into that one file.
//   • WO-1837 found it again on a WALL. Copying the same two lines a third time
//     is how it comes back a fourth. Hence ONE owner, called from every site.
//
// PROVING DATA (read from disk 2026-09-17, not inferred — CLAUDE.md §11B):
//   ProjectSettings/TagManager.asset layers[] → index 8 == "Structure".
//   Assets/Dungeon/Rooms/*.prefab → every Wall_N / Wall_E / Wall_W / Wall_S_L /
//   Wall_S_R / Wall_N_L / Wall_N_R GameObject carries `m_Layer: 0` (Default)
//   while holding a BoxCollider from GameObject.CreatePrimitive. 22 such walls
//   across the first four room prefabs alone.
//   Assets/Scenes/DungeonCompose/dg_ember_deep.unity (binary) contains the
//   strings SEALED_WALL x11 plus the seal children Seal_s_lower_w,
//   Seal_s_upper_e, Seal_s_door_01 — i.e. the owner's screenshot geometry.
//
// ⚠ VERTICAL GEOMETRY ONLY, AND THAT RESTRAINT IS LOAD-BEARING.
// "Structure" is NOT a private channel for the LoS cast. SmartMobileCamera's
// occluder mask includes it (SmartMobileCamera.cs:1323 — "raid + town wall panels
// live here"), and the town camera's `_enemyMask m_Bits: 256` IS layer 8 alone
// (SmartMobileCamera.cs:456, :2017). So moving a dungeon FLOOR, RAMP, STEP or
// CEILING onto Structure would hand the camera a horizontal occluder and start it
// fading/pulling in on the ground the hero is standing on. Walls and socket seals
// are unambiguous and are all this sweep claims. Floors blocking LoS across stair
// levels is a real question and a DESIGN RULING, not something to guess at here.
//
// Instrumented per CLAUDE.md §12: one SUMMARY line per sweep, never one line per
// wall — a room can carry 158 wall colliders and a per-object trace would flood
// the device logcat ring and evict the boot window (memory
// `logcat-ring-buffer-destroys-evidence`).
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Dungeons.RoomForge
{
    /// <summary>
    /// Assigns sight-blocking dungeon geometry to the "Structure" physics layer so
    /// <c>HeroTargetIndicator.HasLoS</c>'s Structure-masked linecast can see it.
    /// </summary>
    public static class DungeonStructureLayer
    {
        private const string Sys = "DungeonStructureLayer";

        /// <summary>The physics layer every solid, sight-blocking structure sits on.</summary>
        public const string LayerName = "Structure";

        /// <summary>
        /// Layer index, or -1 when the project declares no such layer. NEVER cached across
        /// calls: TagManager.asset is editable, and a stale -1 would silently disable the
        /// whole gate for the rest of the session.
        /// </summary>
        public static int Resolve() => LayerMask.NameToLayer(LayerName);

        /// <summary>
        /// Put one GameObject on the Structure layer. Returns true when it moved.
        /// GUARD: NameToLayer returns -1 when the layer is absent — only ever assign a real
        /// layer, so a misconfigured project is left untouched rather than shoved onto
        /// layer 0. Mirrors WallSegment.cs:646-647.
        /// </summary>
        public static bool Apply(GameObject go, string why)
        {
            if (go == null) return false;
            int layer = Resolve();
            if (layer < 0)
            {
                FlowTrace.Once(Sys, "structure-layer-absent",
                    $"'{LayerName}' layer is not declared in this project — '{go.name}' stays on " +
                    $"layer {go.layer} and will NOT block hero line-of-sight ({why}).");
                return false;
            }
            if (go.layer == layer) return false;
            go.layer = layer;
            return true;
        }

        /// <summary>
        /// True when <paramref name="name"/> names vertical, sight-blocking dungeon geometry.
        /// <para>
        /// Deliberately a NAME test over a curated vocabulary rather than "anything with a
        /// collider": the alternative sweeps floors, ramps, stair treads and dressing props
        /// onto a layer the camera reads as an occluder (see the file header).
        /// </para>
        /// <para>
        /// Matched: <c>Wall_*</c> (DefaultDungeonRoomsBuilder / DefaultStairConnectorRoomsBuilder
        /// BuildSolidWall), <c>Seal_*</c> (DungeonBakerChecks.SealSocket — the WO-1837 geometry),
        /// and any name carrying "wall" (the KayKit dungeon-kit chunk meshes).
        /// </para>
        /// <para>
        /// Excluded even when they carry "wall": floors, ceilings, ramps, steps/stairs and
        /// DOORS. Doors are excluded because <see cref="CommonDungeonDoor"/> already owns its
        /// leaf's layer AND toggles the blocker collider on open/close — two owners of one
        /// door is the bug this file is trying not to become.
        /// </para>
        /// </summary>
        public static bool IsWallName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();

            // ⛔ Seal_* WINS BEFORE THE EXCLUSIONS, AND THAT ORDER IS THE WHOLE BUG.
            // SealSocket names its slab Seal_<RoomSocket.id>, and socket ids are routinely
            // DOOR-named — RoomSocket.id's own tooltip says "e.g. north_door_01", and
            // dg_ember_deep.unity literally carries `Seal_s_door_01`. With the "door" exclusion
            // running first, IsWallName("Seal_s_door_01") returned FALSE and the load-time sweep
            // skipped the very wall the owner screenshotted. SealSocket returns early for secret
            // and vertical sockets, so anything that reaches CreatePrimitive and gets a Seal_
            // name is unambiguously a solid vertical slab.
            if (n.StartsWith("seal_")) return true;

            if (n.Contains("floor") || n.Contains("ceil") || n.Contains("ramp") ||
                n.Contains("step") || n.Contains("stair") || n.Contains("door") ||
                n.Contains("torch") || n.Contains("dressing") || n.Contains("chest"))
                return false;

            return n.StartsWith("wall") || n.StartsWith("seal_") || n.Contains("wall");
        }

        /// <summary>
        /// True when <paramref name="go"/> is solid enough to justify blocking sight: it holds
        /// at least one enabled, non-trigger collider. A render-only decorative wall is not a
        /// physical obstruction and must not become an optical one either — keeping LoS and
        /// collision agreeing is what stops "I can walk through it but not shoot through it".
        /// </summary>
        public static bool IsSolid(GameObject go)
        {
            if (go == null) return false;
            var cols = go.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c != null && c.enabled && !c.isTrigger) return true;
            }
            return false;
        }

        /// <summary>
        /// Sweep <paramref name="root"/>'s whole subtree and move every solid wall/seal onto the
        /// Structure layer. Returns how many objects MOVED (0 = already correct, or nothing
        /// matched). Idempotent, so it is safe to call on every scene load.
        /// <para>
        /// ⚠ THIS IS THE BAKE-INDEPENDENT HALF AND IT IS THE HALF THAT REACHES THE OWNER'S
        /// DEVICE. The generators below this fix bake prefabs and scenes to DISK; correcting
        /// them changes nothing already baked until every dungeon is re-baked. This sweep runs
        /// at load, so an existing stale bake is corrected in memory on the frame it loads.
        /// </para>
        /// </summary>
        public static int ApplyToWalls(Transform root, string why)
        {
            if (root == null) return 0;

            int layer = Resolve();
            if (layer < 0)
            {
                FlowTrace.Warn(Sys, $"'{LayerName}' layer is not declared — the dungeon wall sweep is a " +
                                    $"NO-OP and the hero will target through walls ({why}).");
                return 0;
            }

            int matched = 0, moved = 0, alreadyOk = 0, skippedHollow = 0;
            string sample = null;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                var go = t.gameObject;
                if (!IsWallName(go.name)) continue;
                matched++;
                if (!IsSolid(go)) { skippedHollow++; continue; }
                if (go.layer == layer) { alreadyOk++; continue; }
                if (sample == null) sample = go.name;
                go.layer = layer;
                moved++;
            }

            // ONE summary line — never one per wall (header: logcat ring buffer).
            FlowTrace.Step(Sys, $"wall LoS sweep on '{root.name}' ({why}): matched={matched} " +
                                $"moved={moved} alreadyOnStructure={alreadyOk} " +
                                $"skippedRenderOnly={skippedHollow} layer={layer} " +
                                $"firstMoved='{sample ?? "none"}'.");

            if (moved > 0)
                FlowTrace.Warn(Sys, $"{moved} wall/seal collider(s) under '{root.name}' were on the WRONG " +
                                    $"layer at load and were corrected in memory. That is a STALE BAKE: the " +
                                    $"generators now assign Structure, so re-bake the dungeon prefabs/scenes " +
                                    $"to make the on-disk artifact match (WO-1837).");

            return moved;
        }

        /// <summary>
        /// Same sweep across every root of one loaded scene — for the LEGACY hand-built pipeline
        /// (DungeonController), which owns a whole scene rather than a single compose root.
        /// <para>
        /// Audited 2026-09-17: Assets/Scenes/Dungeon_Demo.unity carries 6 <c>Wall_*</c>
        /// GameObjects and ALL 6 read <c>m_Layer: 0</c>, so the legacy scenes have the identical
        /// hole. Returns the total moved.
        /// </para>
        /// </summary>
        public static int ApplyToScene(UnityEngine.SceneManagement.Scene scene, string why)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int moved = 0;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                moved += ApplyToWalls(roots[i].transform, why);
            }
            return moved;
        }
    }
}
