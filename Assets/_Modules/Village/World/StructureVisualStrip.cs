// =============================================================================
// StructureVisualStrip — the ONE owner of "a structure's visual was taken away".
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World
//
// THE DEFECT THIS EXISTS FOR (WO-1716, owner-confirmed in the Editor 2026-09-14).
//
//   `Main_Castle_Overworld.unity` carried a GameObject named `CastleBarracks`
//   (polyperfect `Military_Barracks`) whose MeshFilter AND MeshRenderer were BOTH
//   removed — completely invisible in play — while its MeshCollider stayed live and
//   an oversized carving NavMeshObstacle (Size 16.90712 x 5.08288 x 14.51318 on a
//   0.6/0.9/0.6-scaled object) punched a huge, wrongly-placed hole in the courtyard
//   navmesh. The owner deleted that one object live in the Editor and re-baked: the
//   oversized square vanished and every remaining structure carved normally. That
//   deletion PROVED the husk was the entire cause of the symptom.
//
//   The mechanism, read at source (NOT inferred):
//     1. `CastleHubBuilder.SkinHostUpright` destroys the host's own Renderer(s) and
//        MeshFilter(s) so a Tripo model can be skinned on in their place — and it
//        NEVER touched the host's own MeshCollider, which had been shaped by the
//        very mesh just destroyed. If the skin then failed (VisualFactory.Skin
//        returns null, which only LogWarning'd), or the visual landed elsewhere, the
//        host was left INVISIBLE BUT SOLID. The scene's ghost carries exactly that
//        signature: `m_RemovedComponents` = 2 (the filter + the renderer),
//        `m_AddedGameObjects: []` (no skin child was ever added).
//     2. `NavMeshBakeFinal.PrepareBakedTwinsForDynamicCarving` then resolves every
//        catalog `bakedTwins` name BY NAME and sizes a carving NavMeshObstacle from
//        that object's COLLIDER bounds. Handed an invisible husk, it faithfully sized
//        a barracks-sized carve off a barracks-sized collider that nothing rendered —
//        the 16.9 x 5.08 x 14.5 box, in a place the player sees nothing.
//
//   Neither step is wrong on its own. The husk is created by step 1 and weaponised by
//   step 2, and NOTHING in between asserts the invariant that closes it:
//
//     ⛔ A STRUCTURE MAY NOT BE INVISIBLE AND SOLID AT THE SAME TIME.
//        When the visual is gone and nothing replaced it, the geometry that described
//        it goes too. If a reason exists to keep the GameObject (a marker transform,
//        an NPC interact point, a persisted record), it keeps its transform and its
//        TRIGGERS, and loses its solid mesh collision and its navmesh carve.
//
//   ⚠ THE PAIR IS TWO CALLS, NOT ONE, AND THAT IS DELIBERATE. `SkinOptions.Structure`
//   sets `StripColliders = true` (VisualFactory.cs:53,112,368-370) — "remove the model's
//   own colliders (THE HOST OWNS ITS COLLIDER)". On a SUCCESSFUL re-skin the host's
//   existing collider IS the new building's body collision, so clearing it along with the
//   old renderer would let the player walk through every re-skinned structure and would
//   send NavMeshBakeFinal down its "NO collider — it never carved" branch: a navmesh
//   running straight THROUGH the building. That is worse than the husk, and that file's
//   own header says so. Hence: StripHostVisual (visual only) -> attempt the new visual ->
//   EnsureNoHusk (clears collision ONLY if nothing renders after all).
//
//   That invariant lives here, in ONE place, so the strip path and the regression
//   that guards it cannot drift apart (CLAUDE.md sec.5 / sec.15 duplicated-state rule).
//   It is in DeNelle.Village rather than DeNelle.Editor on purpose: DeNelle.Editor
//   references DeNelle.EditorRegression, so a helper living there could never be
//   exercised by the oracle that pins it.
//
// WHAT IS DELIBERATELY NOT TOUCHED
//   * BoxColliders — `Building.EnsureBlocker` (Building.cs:373-377) falls back to
//     `GetComponent<BoxCollider>()` and will re-Add one if it is missing. Destroying
//     a BoxCollider here would fight that seam every time Configure() runs.
//   * Trigger colliders — an NPC interact point is not "solid", and a structure that
//     still legitimately hosts a vendor keeps its doorway.
//   * Child GameObjects — callers that want a child's visual gone destroy the whole
//     child object, which leaves no husk by construction.
// =============================================================================
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

namespace DeNelle.Village.World
{
    /// <summary>
    /// Removes a structure host's own visual AND the geometry that described it, together,
    /// and owns the predicate that recognises the invisible-but-solid husk shape.
    /// </summary>
    public static class StructureVisualStrip
    {
        private const string FlowSys = "Hub";

        /// <summary>What one <see cref="StripHostVisual"/> call actually removed.</summary>
        public struct StripReport
        {
            public int Renderers;
            public int MeshFilters;
            public int SolidColliders;
            public int NavObstacles;

            public int Total => Renderers + MeshFilters + SolidColliders + NavObstacles;

            public override string ToString() =>
                $"renderers={Renderers} filters={MeshFilters} solidColliders={SolidColliders} navObstacles={NavObstacles}";
        }

        /// <summary>
        /// Remove the host's OWN visual — every Renderer and MeshFilter on the host GameObject —
        /// and NOTHING else. Child objects are the caller's business.
        /// <para/>
        /// ⛔ THIS IS HALF AN OPERATION AND MUST NEVER BE CALLED ALONE. Pair it with
        /// <see cref="EnsureNoHusk"/> once the replacement visual is (or is not) in place. It is
        /// split from the collision clear ON PURPOSE, for a reason read at source and not guessed:
        /// <c>SkinOptions.Structure</c> sets <c>StripColliders = true</c>
        /// (<c>VisualFactory.cs:53,112,368-370</c>) — "remove the model's own colliders (THE HOST
        /// OWNS ITS COLLIDER)". So on a SUCCESSFUL re-skin the host's existing collider is the
        /// building's body collision, and clearing it unconditionally here would let the player walk
        /// through every re-skinned structure and make NavMeshBakeFinal take its "NO collider — it
        /// never carved" branch, i.e. a navmesh running straight through the building. That outcome
        /// is worse than the husk, and NavMeshBakeFinal's own header says so.
        /// </summary>
        public static StripReport StripHostVisual(GameObject host, string reason)
        {
            var report = new StripReport();
            if (host == null) return report;

            foreach (var r in host.GetComponents<Renderer>())
                if (r != null) { DestroyComponent(r); report.Renderers++; }

            foreach (var mf in host.GetComponents<MeshFilter>())
                if (mf != null) { DestroyComponent(mf); report.MeshFilters++; }

            if (report.Total > 0)
                FlowTrace.Step(FlowSys,
                    $"StripHostVisual('{host.name}') removed {report} ({reason}) - " +
                    "call EnsureNoHusk once the replacement visual is resolved (WO-1716).");

            return report;
        }

        /// <summary>
        /// THE CLOSING HALF. If — and only if — <paramref name="host"/> is now an invisible-but-solid
        /// husk, take its solid MeshColliders and its NavMeshObstacles away too, so nothing invisible
        /// is ever left blocking or carving. A no-op (returns false, logs nothing) whenever anything
        /// under the host renders, which is the normal successful-skin case.
        /// <para/>
        /// Triggers and BoxColliders are never touched: a trigger is an NPC interact point, not solid
        /// geometry, and a BoxCollider is the <c>Building._blocker</c> seam that
        /// <c>Building.EnsureBlocker</c> (Building.cs:373-377) re-adds anyway.
        /// </summary>
        /// <returns>TRUE when a husk was found and cleared.</returns>
        public static bool EnsureNoHusk(GameObject host, string reason)
        {
            if (host == null) return false;
            if (!IsInvisibleNavBlockingHusk(host, out string detail)) return false;

            var report = new StripReport();

            foreach (var mc in host.GetComponents<MeshCollider>())
            {
                if (mc == null || mc.isTrigger) continue;
                DestroyComponent(mc);
                report.SolidColliders++;
            }

            // A carve with no visible source is exactly the WO-1716 ghost: the owner spent a session
            // hunting a hole whose owner nothing rendered.
            foreach (var obs in host.GetComponents<NavMeshObstacle>())
                if (obs != null) { DestroyComponent(obs); report.NavObstacles++; }

            FlowTrace.Warn(FlowSys,
                $"EnsureNoHusk('{host.name}') found an invisible-but-solid husk ({detail}) and cleared " +
                $"{report} ({reason}) - WO-1716.");

            // No silent failures: if it is STILL a husk, the remaining blocker is something this
            // helper deliberately does not touch, and a human needs to see that line.
            if (IsInvisibleNavBlockingHusk(host, out string stillDetail))
                FlowTrace.Fail(FlowSys,
                    $"EnsureNoHusk('{host.name}') could NOT clear the husk: {stillDetail} ({reason}). " +
                    "Destroy the GameObject instead - WO-1716.");

            return true;
        }

        /// <summary>
        /// TRUE when <paramref name="go"/> is the WO-1716 ghost shape: nothing anywhere under it
        /// renders, yet it still blocks — an enabled solid (non-trigger) collider, or an enabled
        /// carving NavMeshObstacle. This is the invariant the removal path must never produce and
        /// the hub scene must never contain.
        /// </summary>
        public static bool IsInvisibleNavBlockingHusk(GameObject go, out string detail)
        {
            detail = null;
            if (go == null) return false;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null) return false;    // something renders - not a husk, whatever else it carries

            int solid = 0, carving = 0;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null && c.enabled && !c.isTrigger) solid++;
            foreach (var o in go.GetComponentsInChildren<NavMeshObstacle>(true))
                if (o != null && o.enabled && o.carving) carving++;

            if (solid == 0 && carving == 0) return false;

            detail = $"'{go.name}' renders nothing but keeps {solid} enabled solid collider(s) " +
                     $"and {carving} enabled carving NavMeshObstacle(s)";
            return true;
        }

        /// <summary>Edit mode needs DestroyImmediate; play mode must not use it.</summary>
        private static void DestroyComponent(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) Object.Destroy(component);
            else Object.DestroyImmediate(component);
        }
    }
}
