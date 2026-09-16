// regression-registry: standalone
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Buildings.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Actual collector callbacks with unique keys; does not reset any owner save/prefs.</summary>
    public static class CollectorOwnershipPlayProof
    {
        internal const string Arm = "CollectorOwnershipPlayProof.Armed";
        internal const string MainSave = "CollectorOwnershipPlayProof.MainSave";
        internal static ISaveProvider Memory;

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Requires closed Play session.");
            SessionState.SetString(MainSave, new LocalSaveProvider().Read(SaveSchema.PlayerPrefsKey) ?? "");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Arm, true);
            EditorApplication.EnterPlaymode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Isolate()
        {
            if (!SessionState.GetBool(Arm, false)) return;
            Memory = new MemoryProvider();
            GameStateService.Provider = Memory;
            GameStateService.SuppressCloudForIsolatedProof = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartDriver()
        {
            if (!SessionState.GetBool(Arm, false)) return;
            SessionState.SetBool(Arm, false);
            new GameObject("CollectorOwnershipPlayProof").AddComponent<CollectorOwnershipPlayDriver>();
        }

        sealed class MemoryProvider : ISaveProvider
        {
            readonly Dictionary<string, string> data = new Dictionary<string, string>();
            public bool Exists(string slot) => data.ContainsKey(slot);
            public string Read(string slot) => data.TryGetValue(slot, out var value) ? value : "";
            public void Write(string slot, string value) => data[slot] = value;
            public void Delete(string slot) => data.Remove(slot);
        }
    }

    public sealed class CollectorOwnershipPlayDriver : MonoBehaviour
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<GameObject> owned = new List<GameObject>();
        readonly List<string> ids = new List<string>();
        readonly Dictionary<string, string> originalKeys = new Dictionary<string, string>();
        string knownIds;
        bool knownIdsExisted, initialized;

        IEnumerator Start()
        {
            Exception failure = null;
            var test = Exercise();
            while (true)
            {
                bool more;
                try { more = test.MoveNext(); }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return test.Current;
            }
            try { Cleanup(); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            if (failure != null) Debug.LogError("COLLECTOR_OWNERSHIP_PLAY_FAIL " + failure);
            else Debug.Log("COLLECTOR_OWNERSHIP_PLAY_OK real callbacks: ledger refusal, fallback/placed handoff, displaced rekey, idempotent authored capability, latest-pending revival, ownership removal; owner keys preserved; fixed-ID retry scheduling not exercised");
            EditorApplication.Exit(failure == null ? 0 : 1);
        }

        IEnumerator Exercise()
        {
            yield return null;
            Require(GameStateService.Provider == CollectorOwnershipPlayProof.Memory && CollectorOwnershipPlayProof.Memory != null,
                "Collector proof did not isolate the save provider before startup.");
            var service = GameStateService.Instance;
            Require(service != null && service.State != null, "Save state unavailable.");
            Require(service.State.EverBuiltStructureIds.Count == 0, "Fixture must begin without ownership; will not clear existing ownership.");
            knownIdsExisted = PlayerPrefs.HasKey(GameStateService.CollectorKnownIdsPrefKey);
            knownIds = PlayerPrefs.GetString(GameStateService.CollectorKnownIdsPrefKey, "");
            foreach (string id in new[] { "farm", "lumbermill", "forge" })
                foreach (string prefix in GameStateService.CollectorPrefPrefixes)
                    originalKeys[prefix + id] = ReadKey(prefix, id);
            initialized = true;
            string a = "ownership-proof-a-" + Guid.NewGuid().ToString("N");
            string b = "ownership-proof-b-" + Guid.NewGuid().ToString("N");
            ids.Add(a); ids.Add(b);
            var host = GameObject.Find("ResourceCollectorHost");
            Require(host != null, "Real collector bootstrap host missing.");
            Ensure(a);
            Require(ResourceCollectorRegistry.Get(a) == null && host.transform.Find("Collector_" + a) == null,
                "Missing ownership created a phantom collector.");
            service.State.MarkEverBuilt("collector_" + a);
            Ensure(a);
            var fallback = ResourceCollectorRegistry.Get(a);
            Require(fallback != null && fallback.transform.IsChildOf(host.transform), "Owned missing collector did not receive fallback.");
            owned.Add(fallback.gameObject);
            yield return null; // Run real Start/away stamp for the configured unique id.
            fallback.Accrue(10);
            Require(Near(fallback.PendingAmount, 10) && Near(ReadPending(a), 10), "Fallback did not accrue/persist exactly ten.");

            // The stored owner state is newer than this fallback's in-memory ten. A handoff
            // must not let OnDisable write the stale ten over the arriving owner's twenty.
            PlayerPrefs.SetFloat(GameStateService.CollectorPendingPrefPrefix + a, 20f);
            var placedHost = Own(new GameObject("CollectorOwnershipPlaced"));
            var entry = new CatalogEntry { id = "collector_" + a, type = CatalogType.Collector,
                repo = new RepoProps { behaviorId = "ResourceCollector", collectorBuildingId = a } };
            StructureFactory.AttachAuthoredCapabilities(placedHost, entry);
            var placed = placedHost.GetComponent<ResourceCollector>();
            Require(placed != null && ResourceCollectorRegistry.Get(a) == placed && !fallback.gameObject.activeSelf,
                "Placed capability did not take ownership and park its fallback.");
            Require(Near(placed.PendingAmount, 20) && Near(ReadPending(a), 20), "Fallback disable overwrote newer pending during handoff.");
            Require(!HarvestSourceRegistry.Active.ContainsReference(fallback) && HarvestSourceRegistry.Active.ContainsReference(placed),
                "Harvest source registry retained both ownership writers.");

            // A second injector pass must keep the existing component's volatile state. This
            // deliberate divergence makes an accidental repeated Configure/LoadState observable.
            typeof(ResourceCollector).GetField("_pending", Private).SetValue(placed, 23d);
            StructureFactory.AttachAuthoredCapabilities(placedHost, entry);
            Require(placedHost.GetComponents<ResourceCollector>().Length == 1 && placedHost.GetComponent<ResourceCollector>() == placed &&
                Near(placed.PendingAmount, 23) && Near(ReadPending(a), 20), "Repeated capability attach reset or duplicated the live collector.");
            placed.Accrue(1);
            Require(Near(ReadPending(a), 24), "Placed collector did not persist its authoritative pending.");
            placedHost.SetActive(false); // Real OnDisable saves and unregisters the current owner.
            Require(ResourceCollectorRegistry.Get(a) == null, "Outgoing owner failed to unregister.");
            Ensure(a);
            Require(ResourceCollectorRegistry.Get(a) == fallback && fallback.gameObject.activeSelf && Near(fallback.PendingAmount, 24),
                "Fallback revival did not reload the latest placed state.");

            // The same ordering as AddComponent(default id) then Configure(real id), but both
            // ids are unique, so the test cannot write the owner's farm progression keys.
            var incomingHost = Own(new GameObject("CollectorOwnershipRekey")); incomingHost.SetActive(false);
            var incoming = incomingHost.AddComponent<ResourceCollector>(); incoming.Configure(a);
            incomingHost.SetActive(true);
            Require(ResourceCollectorRegistry.Get(a) == incoming, "Actual OnEnable did not establish displaced-owner precondition.");
            incoming.Configure(b);
            Require(ResourceCollectorRegistry.Get(a) == fallback && ResourceCollectorRegistry.Get(b) == incoming,
                "Configure orphaned the displaced owner while re-keying.");
            Require(HarvestSourceRegistry.Active.ContainsReference(fallback) && HarvestSourceRegistry.Active.ContainsReference(incoming),
                "Re-key lost a live harvest source.");
            incomingHost.SetActive(false);
            Require(ResourceCollectorRegistry.Get(a) == fallback && ResourceCollectorRegistry.Get(b) == null,
                "Disabling re-keyed collector removed the wrong registry slot.");

            // Model the readiness gate with a changed isolated state ledger, then invoke its
            // real bootstrap decision. Fixed three-ID event scheduling is a separate test.
            PlayerPrefs.SetFloat(GameStateService.CollectorPendingPrefPrefix + a, 31f);
            service.State.EverBuiltStructureIds.Remove("collector_" + a);
            Ensure(a);
            Require(!fallback.gameObject.activeSelf && ResourceCollectorRegistry.Get(a) == null && Near(ReadPending(a), 31),
                "Removed ownership retained a fallback or overwrote the replacement state's pending.");
            Ensure(a);
            Require(ResourceCollectorRegistry.Get(a) == null && Near(ReadPending(a), 31), "Repeated refusal recreated an unowned collector.");
            service.State.MarkEverBuilt("collector_" + a);
            Ensure(a);
            Require(ResourceCollectorRegistry.Get(a) == fallback && Near(fallback.PendingAmount, 31),
                "Restored ownership did not rehydrate the current pending state.");
            yield return null;
        }

        GameObject Own(GameObject value) { owned.Add(value); return value; }
        static void Ensure(string id)
        {
            var method = typeof(ResourceCollectorBootstrap).GetMethod("EnsureFallbackCollector", BindingFlags.Static | BindingFlags.NonPublic);
            Require(method != null, "Actual fallback decision entry point unavailable.");
            method.Invoke(null, new object[] { id });
        }
        static float ReadPending(string id) => PlayerPrefs.GetFloat(GameStateService.CollectorPendingPrefPrefix + id, -1f);
        static bool Near(double a, double b) => !double.IsNaN(a) && !double.IsInfinity(a) && Math.Abs(a - b) < .001;
        static string ReadKey(string prefix, string id)
        {
            string key = prefix + id;
            if (!PlayerPrefs.HasKey(key)) return null;
            return prefix == GameStateService.CollectorLastAccrualPrefPrefix ? PlayerPrefs.GetString(key) :
                PlayerPrefs.GetFloat(key).ToString("R", CultureInfo.InvariantCulture);
        }
        void Cleanup()
        {
            if (!initialized) return;
            // A refused/throwing factory can leave our child before returning a collector.
            // Resolve only exact GUID names allocated by this fixture, never arbitrary roots.
            var host = GameObject.Find("ResourceCollectorHost");
            if (host != null)
                foreach (string id in ids)
                {
                    var child = host.transform.Find("Collector_" + id);
                    if (child != null && !owned.Contains(child.gameObject)) owned.Add(child.gameObject);
                }
            // Disable first, so actual OnDisable writes finish before deleting only our unique keys.
            foreach (var go in owned) if (go != null) go.SetActive(false);
            foreach (var go in owned) if (go != null) Destroy(go);
            foreach (string id in ids)
                foreach (string prefix in GameStateService.CollectorPrefPrefixes) PlayerPrefs.DeleteKey(prefix + id);
            if (knownIdsExisted) PlayerPrefs.SetString(GameStateService.CollectorKnownIdsPrefKey, knownIds);
            else PlayerPrefs.DeleteKey(GameStateService.CollectorKnownIdsPrefKey);
            PlayerPrefs.Save();
            foreach (string id in new[] { "farm", "lumbermill", "forge" })
                foreach (string prefix in GameStateService.CollectorPrefPrefixes)
                    Require(originalKeys[prefix + id] == ReadKey(prefix, id), "Fixture touched owner collector key " + prefix + id);
            Require(PlayerPrefs.HasKey(GameStateService.CollectorKnownIdsPrefKey) == knownIdsExisted &&
                PlayerPrefs.GetString(GameStateService.CollectorKnownIdsPrefKey, "") == knownIds, "Collector index was not restored exactly.");
            Require((new LocalSaveProvider().Read(SaveSchema.PlayerPrefsKey) ?? "") == SessionState.GetString(CollectorOwnershipPlayProof.MainSave, ""),
                "Main owner save changed during collector proof.");
        }
        static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    }

    static class CollectorOwnershipFixtureList
    {
        internal static bool ContainsReference(this IReadOnlyList<DeNelle.Core.World.IHarvestSource> list, ResourceCollector source)
        { for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], source)) return true; return false; }
    }
}
