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
        private Vector3[] _fullScales;
        private Coroutine[] _animations;
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
            if (!HarvestResourceNames.TryParse(resourceWord, out harvest) || _catalog == null ||
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
                    $"parsed={HarvestResourceNames.TryParse(resourceWord, out HarvestResource _)} " +
                    $"catalog={(_catalog == null ? "NULL" : "ok")} " +
                    $"prop={(_catalog != null && _catalog.TryGet(harvest, out var probe) && probe.Prop != null ? "ok" : "MISSING")}.");
                BuildFallback();
                return;
            }

            // Seat fill on the owner's preserved pallet without changing its body.
            // Measure before adding any stack or fallback UI.
            if (!TryBoundsIn(transform, transform, out var deck))
            {
                FlowTrace.Warn("Storage", "Cannot measure authored pallet for " + _placed.itemId);
                BuildFallback();
                return;
            }
            var root = new GameObject("StorageFillStack").transform;
            root.SetParent(transform, false);
            _props = new GameObject[VisibleProps[4]];
            _homes = new Vector3[_props.Length];
            _fullScales = new Vector3[_props.Length];
            _animations = new Coroutine[_props.Length];
            for (int i = 0; i < _props.Length; i++)
            {
                var prop = Instantiate(row.Prop, root);
                prop.name = $"StorageProp_{i:D2}";
                prop.transform.localPosition = Vector3.zero;
                prop.transform.localRotation = Quaternion.identity;
                prop.transform.localScale = Vector3.one;
                if (!TryBoundsIn(prop.transform, root, out var propBounds) ||
                    !TryDeckSeat(deck, propBounds, i, out var home, out float scale))
                {
                    FlowTrace.Warn("Storage", "Invalid fill mesh bounds for " + _placed.itemId);
                    Destroy(root.gameObject);
                    _props = null;
                    BuildFallback();
                    return;
                }
                prop.transform.localPosition = home;
                prop.transform.localScale = Vector3.one * scale;
                foreach (var c in prop.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                _props[i] = prop;
                _homes[i] = home;
                _fullScales[i] = prop.transform.localScale;
                prop.SetActive(false);
            }
        }

        // Transform renderer-local bound corners instead of inverse-transforming a
        // world AABB, which inflates the deck whenever its placed yaw is nonzero.
        private static bool TryBoundsIn(Transform subject, Transform frame, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var renderer in subject.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
                var local = renderer.localBounds;
                var matrix = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }

        /// <summary>Four supported props per layer, with two centred across the top layer.</summary>
        public static bool TryDeckSeat(Bounds deck, Bounds prop, int index, out Vector3 position, out float scale)
        {
            position = Vector3.zero; scale = 1f;
            if (index < 0 || index >= 14 || deck.size.x <= 0 || deck.size.z <= 0 ||
                prop.size.x <= 0 || prop.size.y <= 0 || prop.size.z <= 0) return false;
            // Ten percent of the deck remains clear around the compact stack.
            scale = Mathf.Min(deck.size.x * .9f / (2f * prop.size.x), deck.size.z * .9f / (2f * prop.size.z));
            if (!float.IsFinite(scale) || scale <= 0) return false;
            Vector3 size = prop.size * scale;
            int layer = index / 4;
            float x = (index % 2 == 0 ? -.5f : .5f) * size.x;
            float z = layer == 3 ? 0f : (index % 4 < 2 ? -.5f : .5f) * size.z;
            // Contact is measured from mesh bottom, not its possibly off-centre pivot.
            position = new Vector3(deck.center.x + x, deck.max.y + layer * size.y, deck.center.z + z)
                - new Vector3(prop.center.x, prop.min.y, prop.center.z) * scale;
            return true;
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
                if (_props[i].activeSelf == on && _animations[i] == null) continue;
                if (immediate || upgraded || old < 0) SetShown(i, on);
                else
                {
                    if (_animations[i] != null) StopCoroutine(_animations[i]);
                    _animations[i] = StartCoroutine(Animate(i, on));
                }
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

        private IEnumerator Animate(int index, bool show)
        {
            var prop = _props[index];
            if (prop == null) yield break;
            Vector3 full = _fullScales[index];
            if (show) { prop.SetActive(true); prop.transform.localScale = full * .6f; prop.transform.localPosition = _homes[index] + Vector3.up * .2f; }
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < .15f)
            {
                float t = (Time.unscaledTime - start) / .15f;
                prop.transform.localScale = Vector3.Lerp(show ? full * .6f : full, show ? full : full * .6f, t);
                prop.transform.localPosition = Vector3.Lerp(show ? _homes[index] + Vector3.up * .2f : _homes[index], show ? _homes[index] : _homes[index] - Vector3.up * .1f, t);
                yield return null;
            }
            prop.transform.localScale = full;
            prop.transform.localPosition = _homes[index];
            if (!show) prop.SetActive(false);
            _animations[index] = null;
        }

        private void SetShown(int i, bool shown)
        {
            if (_animations[i] != null) { StopCoroutine(_animations[i]); _animations[i] = null; }
            _props[i].transform.localScale = _fullScales[i];
            _props[i].transform.localPosition = _homes[i];
            _props[i].SetActive(shown);
        }

        private static bool TryResource(string word, out BankResource resource)
            => TownBankCapacity.TryParseResource(word, out resource) && resource <= BankResource.Stone;
    }
}
