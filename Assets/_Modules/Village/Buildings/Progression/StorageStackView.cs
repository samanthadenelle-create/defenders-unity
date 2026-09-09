using System.Collections;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.UI;
using UnityEngine;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>WO-903: diegetic five-tier bank fill for lumberyard/foundry/silo pallets.</summary>
    [DisallowMultipleComponent]
    public sealed class StorageStackView : MonoBehaviour
    {
        public const int TierCount = 5;
        private static readonly int[] VisibleProps = { 0, 2, 5, 9, 14 };
        private const float Deadband = 0.02f;
        private const float PollSeconds = 0.25f;

        private PlacedStructure _placed;
        private BankResource _resource;
        private CollectorStackPropCatalog _catalog;
        private GameObject[] _props;
        private Vector3[] _homes;
        private ElarionUiKit.BarHandle _bar;
        private Transform _barRoot;
        private Transform _camera;
        private int _tier = -1;
        private int _level = -1;
        private float _nextPoll;

        public static StorageStackView Attach(PlacedStructure placed)
        {
            if (placed == null || string.IsNullOrEmpty(placed.itemId)) return null;
            var entry = CatalogRegistry.Get(placed.itemId);
            // [one-reader]: capacity math (storageCapacity / IsStorageContainer) has exactly one
            // owner. Ask TownBankCapacity, never the raw repo seam -- two readers is how the pallet
            // reads FULL while the bank still accepts.
            if (entry == null || !TownBankCapacity.IsStorageContainer(entry.repo)) return null;
            if (!TryResource(entry.repo.storageResource, out _)) return null;
            var existing = placed.GetComponent<StorageStackView>();
            var view = existing != null ? existing : placed.gameObject.AddComponent<StorageStackView>();
            view._placed = placed;
            return view;
        }

        private void Start()
        {
            if (_placed == null) _placed = GetComponent<PlacedStructure>();
            var entry = _placed != null ? CatalogRegistry.Get(_placed.itemId) : null;
            if (entry == null || entry.repo == null || !TryResource(entry.repo.storageResource, out _resource))
            {
                enabled = false;
                return;
            }
            _camera = Camera.main != null ? Camera.main.transform : null;
            _catalog = Resources.Load<CollectorStackPropCatalog>(CollectorStackPropCatalog.ResourcesPath);
            Build(entry.repo.storageResource);
            Refresh(true);
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollSeconds;
                Refresh(false);
            }
            // Billboard the BAR root, not transform.root -- transform.root is the placed
            // structure itself, so the old form spun the whole building to face the camera.
            if (_barRoot != null && _camera != null)
                _barRoot.rotation = Quaternion.LookRotation(_barRoot.position - _camera.position);
        }

        private void Build(string resourceWord)
        {
            HarvestResource harvest;
            if (!System.Enum.TryParse(resourceWord, true, out harvest) || _catalog == null ||
                !_catalog.TryGet(harvest, out var row) || row.Prop == null)
            {
                // CLAUDE.md 12: this degradation was SILENT. The abstract bar and the diegetic
                // pallet look nothing alike, so a player seeing the bar is looking at a different
                // feature - and with the props served from the CDN (16), an unpushed or missing
                // prop is exactly how that happens. Name WHICH of the four conditions fired, or
                // the next session gets "it shows a bar sometimes" and no way to tell why.
                FlowTrace.Warn("Storage",
                    $"'{(_placed != null ? _placed.itemId : "?")}' fell back to the abstract fill " +
                    $"bar instead of the pallet stack: resourceWord='{resourceWord}' " +
                    $"parsed={System.Enum.TryParse(resourceWord, true, out HarvestResource _)} " +
                    $"catalog={(_catalog == null ? "NULL" : "ok")} " +
                    $"prop={(_catalog != null && _catalog.TryGet(harvest, out var probe) && probe.Prop != null ? "ok" : "MISSING")}.");
                BuildFallback();
                return;
            }

            Vector3 slot = row.SlotSize.sqrMagnitude > 0.001f ? row.SlotSize : new Vector3(1.2f, 1f, 0.8f);
            float scale = row.PropScale > 0f ? row.PropScale : 1f;
            // The old anchor was local zero, exactly the centre of GenericContainer's
            // raised deck. Every visible sack/ingot therefore occupied the building
            // instead of reading as its pallet. Seat the stack wholly beyond the
            // authored structure footprint, on the approach (-Z) edge.
            float footprint = _placed != null && CatalogRegistry.Get(_placed.itemId)?.repo?.placement != null
                ? CatalogRegistry.Get(_placed.itemId).repo.placement.footprint
                : 0f;
            var root = new GameObject("StorageFillStack").transform;
            root.SetParent(transform, false);
            root.localPosition = StackAnchor(footprint, slot.z * scale);
            _props = new GameObject[VisibleProps[4]];
            _homes = new Vector3[_props.Length];
            for (int i = 0; i < _props.Length; i++)
            {
                int col = i % 4;
                int layer = i / 4;
                // The final two props deliberately spill beyond the tidy frame silhouette.
                float spill = i >= 12 ? (i == 12 ? -0.62f : 0.62f) : 0f;
                Vector3 home = new Vector3((col - 1.5f) * slot.x / 4f + spill,
                    layer * slot.y / 4f, (layer % 2 == 0 ? 0.12f : -0.12f) * slot.z);
                var prop = Instantiate(row.Prop, root);
                prop.name = $"StorageProp_{i:D2}";
                prop.transform.localPosition = home;
                prop.transform.localScale = Vector3.one * scale;
                foreach (var c in prop.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                prop.SetActive(false);
                _props[i] = prop;
                _homes[i] = home;
            }
        }

        /// <summary>
        /// The ABSTRACT fallback tell, shown only when the diegetic pallet props are unavailable
        /// (see the FlowTrace.Warn above for which of the four conditions fired).
        ///
        /// [UI-OBSIDIAN]: this is world-space, but it is still a styled CONTENT widget, so it goes
        /// through the kit rather than hand-rolling its own Image plates. ElarionUiKit.BuildObsidianBar
        /// is parent-relative (anchorMin/anchorMax under any RectTransform), so a world-space canvas
        /// is a perfectly ordinary parent for it -- there is no world-space-only primitive to reach
        /// for and none is needed. Kind = Stat (the neutral, un-ornate bar): a storage container's
        /// fill is not a vital, and the owner is RED/GREEN COLOURBLIND, so the meaning must come
        /// from the bar's LENGTH and never from its hue. The bar is NOT interactive (raycast off,
        /// no Button), so MinTouchPx does not apply to it.
        /// </summary>
        private void BuildFallback()
        {
            var go = new GameObject("StorageFillBar");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            _barRoot = go.transform;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1.4f, 0.22f);
            rt.localScale = Vector3.one;
            _bar = ElarionUiKit.BuildObsidianBar(go.transform, ElarionUiKit.ObsidianBarKind.Stat,
                Vector2.zero, Vector2.one, withValue: false, framed: false);
            if (_bar != null) _bar.SetImmediate(0f, 1f);
        }

        private void Refresh(bool immediate)
        {
            if (_placed == null) return;
            string key = TownBankCapacity.InstanceKeyOf(_placed.itemId, _placed.gridCell.x, _placed.gridCell.y);
            if (!TownBankCapacity.TryGetSlot(_resource, key, out var slot)) return;
            int next = ResolveTier(slot.Contents, slot.Capacity, _tier);
            bool upgraded = _level >= 0 && _placed.level != _level;
            _level = _placed.level;
            if (next == _tier) return;
            int old = _tier;
            _tier = next;
            // Kit fill contract (ElarionUiKit BarHandle): the ONLY width mutation is
            // fillAmount = cur/max -- never anchors, never sizeDelta.
            if (_bar != null) _bar.SetValue(slot.Fill01, 1f);
            if (_props == null) return;
            int visible = VisibleProps[next];
            for (int i = 0; i < _props.Length; i++)
            {
                bool on = i < visible;
                if (_props[i].activeSelf == on) continue;
                if (immediate || upgraded || old < 0) SetShown(i, on);
                else StartCoroutine(Animate(i, on));
            }
        }

        public static int ResolveTier(int current, int max, int previous)
        {
            if (current <= 0 || max <= 0) return 0;
            if (current >= max) return 4;
            float fill = current / (float)max;
            int raw = fill < .375f ? 1 : fill < .625f ? 2 : 3;
            if (previous < 1 || previous > 3 || raw == previous) return raw;
            float boundary = raw > previous ? (raw == 2 ? .375f : .625f) : (previous == 2 ? .375f : .625f);
            if (raw > previous && fill < boundary + Deadband) return previous;
            if (raw < previous && fill > boundary - Deadband) return previous;
            return raw;
        }

        /// <summary>Local pallet seat, fully outside the structure's authored footprint.</summary>
        public static Vector3 StackAnchor(float footprintMetres, float propDepthMetres)
        {
            float halfStructure = Mathf.Max(1f, footprintMetres) * 0.5f;
            float halfStack = Mathf.Max(0.1f, propDepthMetres) * 0.5f;
            return new Vector3(0f, 0.05f, -(halfStructure + halfStack + 0.15f));
        }

        private IEnumerator Animate(int index, bool show)
        {
            var prop = _props[index];
            if (prop == null) yield break;
            Vector3 full = prop.transform.localScale;
            if (show) { prop.SetActive(true); prop.transform.localScale = full * .6f; prop.transform.localPosition = _homes[index] + Vector3.up * .2f; }
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < .15f)
            {
                float t = (Time.unscaledTime - start) / .15f;
                prop.transform.localScale = Vector3.Lerp(show ? full * .6f : full, show ? full : full * .6f, t);
                prop.transform.localPosition = Vector3.Lerp(show ? _homes[index] + Vector3.up * .2f : _homes[index], show ? _homes[index] : _homes[index] - Vector3.up * .1f, t);
                yield return null;
            }
            if (!show) prop.SetActive(false);
            else { prop.transform.localScale = full; prop.transform.localPosition = _homes[index]; }
        }

        private void SetShown(int i, bool shown)
        {
            _props[i].SetActive(shown);
            if (shown) _props[i].transform.localPosition = _homes[i];
        }

        private static bool TryResource(string word, out BankResource resource)
            => System.Enum.TryParse(word, true, out resource) && resource <= BankResource.Food;
    }
}
