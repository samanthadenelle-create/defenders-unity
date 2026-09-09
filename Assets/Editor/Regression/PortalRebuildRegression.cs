// =============================================================================
// PortalRebuildRegression [portal-rebuild]  - WO-869
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. SOURCE-LINT only - no scene, no play mode,
// so it runs in milliseconds inside the DataRegression batch.
//
// WHAT THIS PINS, AND WHY IT IS NOT OPTIONAL
// -----------------------------------------------------------------------------
// The owner's Seeker capture (docs/ui-review/2026-08-04-seeker/08-portal-magenta.png)
// showed the dungeon portal as a MAGENTA stick frame containing two SOLID BLUE BLOCKS.
// Three separate defects produced that one picture, and MagentaGuard - the guard that
// exists precisely to stop magenta shipping - caught NONE of them:
//
//   D1  BuildArch resolved its material with a RAW Shader.Find("Universal Render
//       Pipeline/Lit"). That returns NULL in a stripped player build; the code then
//       left `mat` null, MakeBox's `if (mat != null)` skipped the assignment, and the
//       cube kept Unity's DEFAULT material = MAGENTA under URP.
//
//   D2  MagentaGuard.Sweep is a ONE-TIME renderer snapshot on scene load, but the
//       portal is built SECONDS later (DungeonWorldPortalSpawner retries placement
//       until the world scene + a baked NavMesh exist). The guard was structurally
//       blind to it - the same class as the raid-troop miss.
//
//   D3  Worse: had the guard seen it, the arch is CreatePrimitive cubes, so
//       IsPrimitivePlaceholder would have matched and the sweep would have DISABLED
//       the renderers - turning "magenta portal" into "no portal at all".
//
//   D4  The blue blocks were never broken materials. They are the glow + halo quads
//       rendering OPAQUE, because the code set only `_Surface` / `_Blend` - which are
//       ShaderGUI properties that do nothing at runtime - and never wrote the real
//       state (_SrcBlend / _DstBlend / _ZWrite).
//
//   D5  PortalVFXController carried a LOCAL copy of the magenta shader predicate with
//       no `!isSupported` branch - i.e. blind to the ANDROID case, which is the exact
//       device this was captured on.
//
// Every case below fails against the PRE-WO-869 source and passes after it, so this is
// a real ratchet, not a snapshot of whatever the tree happens to say.
//
// Marker: PORTAL_REBUILD_OK / PORTAL_REBUILD_FAIL: <reason>
//
// Wire into DataRegression.RunAll (one line):
//   Guard.Try("Regression", "portal-rebuild suite", () => { if (!PortalRebuildRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[portal-rebuild] " + r); });
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class PortalRebuildRegression
    {
        private const string SpawnerRel = "_Modules/Village/World/DungeonWorldPortalSpawner.cs";
        private const string VfxCtrlRel = "_Modules/Village/Dungeon/PortalVFXController.cs";
        private const string GuardRel   = "_Modules/Core/MagentaGuard.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- PORTAL REBUILD (WO-869: material + guard + threshold) ---");

            string spawner = ReadOrFail(Path.Combine(Application.dataPath, SpawnerRel), "DungeonWorldPortalSpawner.cs", failures);
            string vfxCtrl = ReadOrFail(Path.Combine(Application.dataPath, VfxCtrlRel), "PortalVFXController.cs", failures);
            string guard   = ReadOrFail(Path.Combine(Application.dataPath, GuardRel),   "MagentaGuard.cs", failures);

            if (failures.Count > 0)
            {
                reason = "portal-rebuild: " + string.Join("; ", failures);
                Debug.LogError(log.ToString() + "PORTAL_REBUILD_FAIL: " + reason);
                return false;
            }

            // (a) D1 - the arch resolves URP/Lit through the ROBUST resolver, not a raw
            //     Shader.Find that returns null in a stripped build and silently yields the
            //     magenta default material.
            if (!Contains(spawner, "MagentaGuard.ResolveUrpLitShader"))
                failures.Add("DungeonWorldPortalSpawner does NOT call MagentaGuard.ResolveUrpLitShader - " +
                             "a raw Shader.Find returns null in a stripped player build and the arch ships MAGENTA (D1)");
            if (Contains(spawner, "Shader.Find(\"Sprites/Default\")"))
                failures.Add("DungeonWorldPortalSpawner still falls back to Sprites/Default for the arch - " +
                             "an unlit sprite shader on a 3D arch is not a recovery (D1)");
            log.AppendLine("  (a) arch material resolved via MagentaGuard.ResolveUrpLitShader (robust, build-safe)");

            // (b) D3 - the arch is registered as deliberate primitive art, so the sweep RECOVERS
            //     it instead of hiding it. Without this the guard's "best case" is an invisible
            //     dungeon entrance, which is strictly worse than the magenta it was hunting.
            if (!Contains(spawner, "MagentaGuard.ProtectPrimitiveArt"))
                failures.Add("DungeonWorldPortalSpawner does NOT call MagentaGuard.ProtectPrimitiveArt - " +
                             "the CreatePrimitive arch would be HIDDEN as a stray placeholder, making the " +
                             "dungeon entrance invisible (D3)");
            if (!Contains(guard, "ProtectPrimitiveArt") || !Contains(guard, "IsProtectedArt"))
                failures.Add("MagentaGuard is missing the ProtectPrimitiveArt / IsProtectedArt opt-out - " +
                             "deliberate primitive art has no way to avoid the stray-placeholder hide (D3)");
            if (!Contains(guard, "!IsProtectedArt(r)"))
                failures.Add("MagentaGuard's stray-primitive HIDE branch is not guarded by !IsProtectedArt(r) - " +
                             "registering art would have no effect (D3)");
            log.AppendLine("  (b) primitive-art opt-out present AND applied to the hide branch");

            // (c) D2 - the guard re-sweeps after a scene load so objects built by the
            //     "wait for the world, THEN place" builders are actually seen.
            if (!Contains(guard, "ScheduleDeferredSweeps") || !Contains(guard, "DeferredSweepDelays"))
                failures.Add("MagentaGuard has no deferred re-sweep - it remains a one-time scene-load " +
                             "snapshot and stays structurally blind to every late-built object (D2)");
            log.AppendLine("  (c) deferred re-sweep ladder present (late-built objects are reachable)");

            // (d) D4 - the runtime transparent materials write the state URP actually reads.
            //     _Surface/_Blend alone are ShaderGUI-only and leave the quad OPAQUE: that is
            //     literally the blue blocks in the capture.
            foreach (var prop in new[] { "_SrcBlend", "_DstBlend", "_ZWrite" })
                if (!Contains(vfxCtrl, prop))
                    failures.Add($"PortalVFXController never sets '{prop}' - its runtime 'additive' materials " +
                                 "render OPAQUE (the solid blue blocks in the owner capture) because _Surface/_Blend " +
                                 "are ShaderGUI-only properties that do nothing at runtime (D4)");
            if (!Contains(vfxCtrl, "ConfigureAdditive"))
                failures.Add("PortalVFXController has no single ConfigureAdditive helper - glow, halo and vortex " +
                             "each hand-roll their blend setup and will drift apart again (D4)");
            log.AppendLine("  (d) transparent/additive render state written for real (_SrcBlend/_DstBlend/_ZWrite)");

            // (e) D5 - no local copy of the magenta predicate. A name-only test is blind to the
            //     ANDROID case (a shader that fails to compile on-device keeps its name).
            if (!Contains(vfxCtrl, "MagentaGuard.IsBrokenShader"))
                failures.Add("PortalVFXController does not route through MagentaGuard.IsBrokenShader - " +
                             "a local name-only predicate is blind to the on-device !isSupported case, which " +
                             "is exactly the Seeker capture (D5)");
            if (Contains(vfxCtrl, "Hidden/InternalErrorShader"))
                failures.Add("PortalVFXController still hand-rolls an inline magenta shader test " +
                             "(Hidden/InternalErrorShader) - delete it and call the single authority (D5)");
            log.AppendLine("  (e) shader-broken test routed to the single authority, no local copy");

            // (f) The REBUILD itself - the arch is no longer two sticks and a bar. Assert the
            //     structural pieces that carry "this leads somewhere": a plinth to stand on,
            //     TWO pillar rings (depth), and a keystone (a peak, not a flat top). Named
            //     pieces, so this fails if someone quietly reverts to the flat frame.
            foreach (var piece in new[] { "Arch_Plinth", "Arch_Pillar", "Arch_Lintel", "Arch_Keystone" })
                if (!Contains(spawner, piece))
                    failures.Add($"DungeonWorldPortalSpawner no longer builds '{piece}' - the portal has " +
                                 "regressed toward the flat depth-less frame the owner rejected");
            if (!Contains(spawner, "ArchHalfDepth"))
                failures.Add("DungeonWorldPortalSpawner has no ArchHalfDepth - the two pillar rings (the DEPTH " +
                             "that makes the arch read as a way INTO somewhere) are gone");
            log.AppendLine("  (f) threshold arch structure intact: plinth + two pillar rings + lintels + keystone");

            // (g) WO-753 one-owner teardown - the held threshold aura loop must be Stop()'d with
            //     its portal, or a destroyed portal orphans a looping effect ("a VFX but no portal").
            if (Contains(spawner, "ThresholdVfx") && !Contains(spawner, "ThresholdVfx?.Stop("))
                failures.Add("DungeonWorldPortalSpawner holds a ThresholdVfx loop but never Stop()s it on " +
                             "teardown - a destroyed portal orphans its aura (WO-753)");
            log.AppendLine("  (g) WO-753: held threshold aura is torn down with its portal");

            // (h) The portal ring is DDOL, but its world objects belong only to the
            // overworld. RaidBase_* must not inherit every portal around its empty horizon.
            if (!Contains(spawner, "ApplyWorldOnlyVisibility") ||
                !Contains(spawner, "HubScenes.IsOverworld(sceneName)") ||
                !Contains(spawner, "_root.gameObject.SetActive(visible)"))
                failures.Add("DungeonWorldPortalSpawner does not hide its persistent portal ring outside " +
                             "the overworld - raid/dungeon cameras can see every map portal (D6)");
            log.AppendLine("  (h) DDOL portal ring is visible only in the overworld");

            if (failures.Count > 0)
            {
                reason = "portal-rebuild: " + string.Join("; ", failures);
                Debug.LogError(log.ToString() + "PORTAL_REBUILD_FAIL: " + reason);
                return false;
            }

            reason = "portal-rebuild OK (8 cases: robust shader resolve, primitive-art opt-out, " +
                     "deferred re-sweep, real additive state, single shader authority, arch structure, " +
                     "WO-753 teardown, world-only visibility)";
            Debug.Log(log.ToString() + "PORTAL_REBUILD_OK");
            return true;
        }

        private static bool Contains(string src, string needle)
            => !string.IsNullOrEmpty(src) && src.Contains(needle);

        private static string ReadOrFail(string path, string label, List<string> failures)
        {
            try
            {
                if (!File.Exists(path)) { failures.Add($"{label} not found at '{path}'"); return null; }
                return File.ReadAllText(path);
            }
            catch (System.Exception e)
            {
                failures.Add($"{label} could not be read: {e.Message}");
                return null;
            }
        }
    }
}
