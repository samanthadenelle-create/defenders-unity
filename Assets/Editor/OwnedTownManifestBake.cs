using System;
using System.IO;
using System.Reflection;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTownManifestBake
    {
        public static void VerifyIntegration()
        {
            FinalRaidRouteProof.Run();
            OwnedTownReconstructionProof.Run();
            Debug.Log("OWNED_LAYOUT_INTEGRATION_OK final raid route and town reconstruction use corrected tower collision and shipped manifest");
        }

        public static void Run()
        {
            VerifyScaledTowerContact();
            typeof(CatalogBootstrap).GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/RaidBase_IronBastion.unity", OpenSceneMode.Single);
            var property = new RaidCaptureCensus(scene).Settle();
            var manifest = new OwnedTownTemplateManifest { templateVersion = property.templateVersion };
            foreach (var record in property.structures)
            {
                if (!OwnedTownScenePose.TryResolve(scene, record.inheritedPose, out var target, out var reason)) throw new InvalidOperationException(reason);
                var spire = target.GetComponent<RaidSpire>();
                if (spire != null) typeof(RaidSpire).GetMethod("EnsureHittable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(spire, null);
                var tower = target.GetComponent<DefenseTower>();
                if (tower != null) typeof(DefenseTower).GetMethod("EnsureContactCollider", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tower, null);
                Physics.SyncTransforms();
                bool measured = false;
                var local = new Bounds(Vector3.zero, Vector3.zero);
                foreach (var collider in target.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                    var bounds = collider.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                        var point = target.InverseTransformPoint(bounds.center + Vector3.Scale(sign, bounds.extents));
                        if (!measured) { local = new Bounds(point, Vector3.zero); measured = true; }
                        else local.Encapsulate(point);
                    }
                }
                if (!measured) throw new InvalidOperationException("No collision footprint for " + target.name);
                manifest.entries.Add(new OwnedTownTemplateManifest.Entry {
                    structure = record.Clone(), movableTower = target.GetComponent<DefenseTower>() != null,
                    localBoundsCenter = local.center, localBoundsExtents = local.extents
                });
            }
            // WO-1767 — DERIVED, NEVER A LITERAL. This line read `!= 221` from 09-11 until
            // 2026-09-16. WO-1732 moved the raid scene onto the WO-1723 4.0 m partition (210 walls
            // -> 158 + 158 ruins, census 169), so re-running this bake as authored THREW on a scene
            // that was correct - CLAUDE.md §8's stale-count pattern living inside code. The entry
            // count is now compared against the census it was built FROM, and re-typing 169 here
            // would be the same bug with a newer number.
            if (property.structures.Count == 0)
                throw new InvalidOperationException(
                    "The final raid capture census is empty - refusing to write an empty owned-town manifest. " +
                    "Run DeNelle.Editor.OwnedTownChain.RebuildFromRaid.");
            if (manifest.entries.Count != property.structures.Count)
                throw new InvalidOperationException(
                    "Manifest census mismatch: " + manifest.entries.Count + " entry/entries built from " +
                    property.structures.Count + " censused structure(s). Every censused structure must produce " +
                    "exactly one manifest entry.");
            const string path = "Assets/Resources/OwnedTown/IronBastionTemplate.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(manifest, true));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Require(OwnedTownLayoutSnapshot.TryCreate(property, HeroClassOpt.Knight, out var snapshot, out var failure), failure);
            string before = Newtonsoft.Json.JsonConvert.SerializeObject(property);
            snapshot.structures[0].condition01 = 0f;
            Require(before == Newtonsoft.Json.JsonConvert.SerializeObject(property), "Snapshot aliases the town save.");
            var authority = new OwnedTownLayoutSnapshot(OwnedTownTemplateManifest.Load());
            var invalid = snapshot.Clone(); invalid.structures[0].inheritedPose.templateStructureId = "unknown-template-object";
            Require(!invalid.TryValidateAndCopy(authority, out _, out _), "Unknown structure accepted.");
            invalid = snapshot.Clone(); invalid.structures[1].inheritedPose = invalid.structures[0].inheritedPose.Clone();
            Require(!invalid.TryValidateAndCopy(authority, out _, out _), "Duplicate template mapping accepted.");
            invalid = snapshot.Clone(); invalid.structures[0].placement.itemId = "unknown-catalog-id";
            Require(!invalid.TryValidateAndCopy(authority, out _, out _), "Unknown catalog accepted.");
            int towerIndex = manifest.entries.FindIndex(e => e.movableTower);
            bool acceptedMove = false;
            foreach (float distance in new[] { 3f, -3f, 6f, -6f })
            {
                var proposed = snapshot.Clone(); proposed.structures[towerIndex].inheritedPose.x += distance;
                if (proposed.TryValidateAndCopy(authority, out _, out _)) { acceptedMove = true; break; }
            }
            Require(acceptedMove, "No ordinary tower relocation passed the shipped layout authority.");
            int otherTower = manifest.entries.FindIndex(towerIndex + 1, e => e.movableTower);
            var colliding = snapshot.Clone();
            var collisionPose = colliding.structures[towerIndex].inheritedPose;
            var otherPosition = OwnedTownTemplateManifest.WorldMatrix(colliding.structures[otherTower].inheritedPose).MultiplyPoint3x4(Vector3.zero);
            var parent = Matrix4x4.identity;
            for (int i = 0; i < 16; i++) parent[i] = collisionPose.parentFrame[i];
            var collisionLocal = parent.inverse.MultiplyPoint3x4(otherPosition);
            collisionPose.x = collisionLocal.x; collisionPose.y = collisionLocal.y; collisionPose.z = collisionLocal.z;
            Require(!colliding.TryValidateAndCopy(authority, out _, out _), "Overlapping standing towers accepted.");
            invalid = snapshot.Clone(); invalid.structures[towerIndex].inheritedPose.x += 1000f;
            Require(!invalid.TryValidateAndCopy(authority, out _, out _), "Out-of-bounds tower accepted.");
            invalid = snapshot.Clone(); invalid.structures[towerIndex].inheritedPose.sx *= 2f;
            Require(!invalid.TryValidateAndCopy(authority, out _, out _), "Resized inherited structure accepted.");
            invalid = snapshot.Clone(); invalid.templateVersion = "unknown-template";
            Require(!invalid.TryValidateAndCopy(authority, out _, out _), "Unknown template version accepted.");
            Debug.Log("OWNED_TOWN_MANIFEST_OK structures=" + manifest.entries.Count +
                " (DERIVED from the raid census, never a literal); shipped manifest round-trip; detached snapshot; bad identities, catalog, bounds, scale and version refused; scene not saved");
        }

        private static void Require(bool value, string reason)
        { if (!value) throw new InvalidOperationException(reason); }

        private static void VerifyScaledTowerContact()
        {
            var host = new GameObject("~ScaledTowerContactProof");
            try
            {
                host.transform.localScale = Vector3.one * .01f;
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.transform.SetParent(host.transform, false);
                visual.transform.localScale = new Vector3(100, 200, 100);
                UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                var tower = host.AddComponent<DefenseTower>();
                var legacy = host.AddComponent<CapsuleCollider>();
                legacy.height = 2f; legacy.radius = .5f; legacy.center = Vector3.up;
                Physics.SyncTransforms();
                Require(legacy.bounds.size.y < .03f, "Legacy fixture is not actually undersized.");
                var ensure = typeof(DefenseTower).GetMethod("EnsureContactCollider", BindingFlags.NonPublic | BindingFlags.Instance);
                ensure.Invoke(tower, null);
                Physics.SyncTransforms();
                Require(legacy.bounds.size.y >= 1.99f && legacy.bounds.size.x >= .99f,
                    "Scaled generated collider does not cover the visible tower.");
                Require(Vector3.Distance(legacy.ClosestPoint(new Vector3(.4f, 0, 0)), new Vector3(.4f, 0, 0)) < .001f,
                    "Contact query misses a point inside the tower.");
                float height = legacy.height, radius = legacy.radius;
                Vector3 center = legacy.center;
                ensure.Invoke(tower, null);
                Require(host.GetComponents<CapsuleCollider>().Length == 1 && legacy.height == height && legacy.radius == radius &&
                    legacy.center == center && host.transform.localScale == Vector3.one * .01f,
                    "Collider repair is not idempotent or changed the art scale.");
                UnityEngine.Object.DestroyImmediate(legacy);
                var authored = host.AddComponent<BoxCollider>(); authored.size = new Vector3(90, 190, 90);
                ensure.Invoke(tower, null);
                Require(host.GetComponents<CapsuleCollider>().Length == 0 && authored.size == new Vector3(90, 190, 90),
                    "Contact initialization replaced an authored solid collider.");
                Debug.Log("SCALED_TOWER_CONTACT_OK legacy repaired; real contact point; idempotent; art and authored collider preserved");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
