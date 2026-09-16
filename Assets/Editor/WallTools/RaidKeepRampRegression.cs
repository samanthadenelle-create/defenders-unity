using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class RaidKeepRampRegression
    {
        public static void Run()
        {
            var failures = new List<string>();
            var notes = new StringBuilder();
            foreach (var entry in new[] { ("fortified_garrison", "synty-castle", 22.05f), ("mage_enclave", "dungeon-stone", 24.3f) })
            {
                var root = new GameObject("RaidKeepRampRegression");
                try
                {
                    var keep = new GameObject("Zone_Keep");
                    keep.transform.SetParent(root.transform, false);
                    var producer = typeof(RaidBaseDresser).GetMethod("RaiseKeep", BindingFlags.Static | BindingFlags.NonPublic);
                    if (producer == null) throw new MissingMethodException("RaiseKeep");
                    producer.Invoke(null, new object[] { keep.transform, entry.Item2, new RaidBaseDresser.LayoutContext { Innermost = entry.Item3 } });
                    CheckKeepRamp(root.transform, entry.Item1, 1, failures, notes);
                }
                catch (Exception ex) { failures.Add(entry.Item1 + " " + ex); }
                finally { Object.DestroyImmediate(root); }
            }
            CheckFloor(failures, notes);
            if (failures.Count == 0) Debug.Log("RAID_KEEP_RAMP_OK\n" + notes);
            else Debug.LogError("RAID_KEEP_RAMP_FAIL " + failures.Count + "\n" + notes + string.Join("\n", failures));
        }

        private static void CheckFloor(List<string> failures, StringBuilder notes)
        {
            foreach (string token in new[] { "floor_dirt_large", "floor_tile_large" })
            {
                var root = new GameObject("RaidFloorFixture");
                try
                {
                    var method = typeof(RaidBaseDresser).GetMethod("TileCourtyardRing", BindingFlags.Static | BindingFlags.NonPublic);
                    method.Invoke(null, new object[] { root.transform, token, new RaidBaseDresser.LayoutContext { Radius = 49f } });
                    if (token == "floor_dirt_large")
                    {
                        if (root.transform.childCount != 0) failures.Add("camp flat dirt overlay hides owned detailed ground");
                        continue;
                    }
                    var rows = new Dictionary<int, List<Bounds>>();
                    foreach (Transform tile in root.transform)
                    {
                        var renderers = tile.GetComponentsInChildren<Renderer>();
                        if (renderers.Length == 0) continue;
                        Bounds bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        int row = Mathf.RoundToInt(tile.position.x * 1000f);
                        if (!rows.TryGetValue(row, out var cells)) rows[row] = cells = new List<Bounds>();
                        cells.Add(bounds);
                    }
                    int checkedPairs = 0; float maxGap = 0f, maxOverlap = 0f;
                    foreach (var row in rows.Values)
                    {
                        row.Sort((a, b) => a.center.z.CompareTo(b.center.z));
                        for (int j = 1; j < row.Count; j++)
                        {
                            float gap = row[j].min.z - row[j - 1].max.z;
                            maxGap = Mathf.Max(maxGap, gap); maxOverlap = Mathf.Max(maxOverlap, -gap); checkedPairs++;
                        }
                    }
                    notes.AppendLine($"STONE_FLOOR actualPairs={checkedPairs} maxGap={maxGap:F3} maxOverlap={maxOverlap:F3} tiles={root.transform.childCount}");
                    if (checkedPairs < 100 || maxGap > 0.01f || maxOverlap > 0.025f || root.transform.childCount > 420)
                        failures.Add("stone floor actual footprints do not join within existing tile budget");
                }
                catch (Exception ex) { failures.Add(token + " " + ex); }
                finally { Object.DestroyImmediate(root); }
            }
        }

        private static void CheckKeepRamp(Transform root, string id, int layers, List<string> failures, StringBuilder notes)
        {
            if (layers == 0) return;
            Transform platform = root.Find("Zone_Keep/KeepPlatform");
            Transform ramp = root.Find("Zone_Keep/KeepRamp");
            if (platform == null || ramp == null) { failures.Add(id + " missing keep platform/ramp"); return; }
            var slab = platform.GetComponent<BoxCollider>();
            var slope = ramp.GetComponent<BoxCollider>();
            if (slab == null || slope == null) { failures.Add(id + " missing actual keep colliders"); return; }
            Physics.SyncTransforms();
            Bounds bounds = slab.bounds;
            float x = bounds.center.x, z = bounds.min.z - 0.02f;
            var ray = new Ray(new Vector3(x, bounds.max.y + 10f, z), Vector3.down);
            if (!slope.Raycast(ray, out RaycastHit landing, 20f))
                failures.Add(id + " keep ramp does not reach platform landing edge");
            else
            {
                float rise = bounds.max.y - landing.point.y;
                notes.AppendLine($"{id} KEEP_RAMP landing={landing.point:F3} platformTop={bounds.max.y:F3} remainingStep={rise:F3}");
                if (Mathf.Abs(rise) > 0.10f) failures.Add(id + " keep ramp landing discontinuity " + rise.ToString("F3") + "m exceeds 0.10m");
            }
            Vector3 foot = ramp.TransformPoint(new Vector3(0f, 0.5f, -0.5f));
            Vector3 top = ramp.TransformPoint(new Vector3(0f, 0.5f, 0.5f));
            notes.AppendLine($"{id} KEEP_RAMP actualTopFace foot={foot:F3} top={top:F3} width={ramp.lossyScale.x:F3} thickness={ramp.lossyScale.y:F3}");
            if (Mathf.Abs(foot.y) > 0.10f || top.y <= foot.y || top.z < bounds.min.z)
                failures.Add(id + " keep ramp actual top-face endpoints do not connect ground to platform");
            if (Mathf.Abs(ramp.lossyScale.x - 4.2f) > 0.001f || Mathf.Abs(ramp.lossyScale.y - 0.35f) > 0.001f)
                failures.Add(id + " keep ramp authored width/thickness changed");
        }

    }
}
