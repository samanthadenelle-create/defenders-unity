using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Catalog;
using DeNelle.Core.Social;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    /// <summary>
    /// Runtime consumer for WO-1276 public snapshots.  This is intentionally a projection-only
    /// overlay: it owns no GameState reference and exposes no build, combat, collector, inventory,
    /// economy or progression command.  Closing simply reveals the still-live leaderboard.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TownShowcaseVisitPanel : MonoBehaviour
    {
        private const int AmbientCount = 12;
        private ElarionUiKit.ObsidianModal _modal;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _status;
        private RectTransform _map;
        private RectTransform _ambientHost;
        private readonly List<RectTransform> _ambientMarkers = new List<RectTransform>();
        private TownShowcaseClient _client;
        private TownVisitNavigation _navigation;
        private Action _onReturn;
        private int _loadGeneration;
        private string _activeSnapshotId;
        private PanelHandle _panelHandle;

        public bool IsOpen => _modal != null && _modal.canvas != null && _modal.canvas.activeSelf;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Town Showcase", Close, () => IsOpen);
        }

        private void OnDestroy()
        {
            ++_loadGeneration;
            if (IsOpen) PanelManager.NotifyClosed(_panelHandle);
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        public void Open(IReadOnlyList<TopTownVisitEntry> top, string showcaseId, int leaderboardRow,
            float leaderboardScrollPosition, Action onReturn)
        {
            int index = Find(top, showcaseId);
            if (index < 0 || !TownShowcaseIds.IsShowcaseId(showcaseId)) return;
            EnsureBuilt();

            // ⛔ SHOW FIRST, ANNOUNCE LAST — THE PROBE MUST BE ANSWERABLE WHEN THE VERIFY RUNS.
            // WO-1301 (sibling sweep): NotifyOpened runs its WO-465 visibility verify SYNCHRONOUSLY
            // and invokes this panel's probe, `() => IsOpen`, which reads
            // `_modal.canvas.activeSelf`. EnsureBuilt deliberately ENDS with SetActive(false), and
            // Close also leaves the canvas inactive — so announcing before the SetActive(true)
            // below made the probe false BY CONSTRUCTION on every open (first open: just
            // deactivated by EnsureBuilt; every later open: still deactivated by Close), firing a
            // FlowTrace.Fail / F8 error capture on a working panel. Activating first lets the probe
            // answer truthfully. The arbiter is untouched.
            if (_modal == null || _modal.canvas == null)
            {
                // THE GENUINE GHOST: the build failed, so there is nothing on screen. Announce
                // anyway so the verify runs and REPORTS it — that is the case the check exists for
                // — then clear the arbiter slot.
                PanelManager.NotifyOpened(_panelHandle);
                Close();
                return;
            }

            _modal.canvas.SetActive(true);
            // A refusal (battle-lock) invokes this panel's Close on its way out, which deactivates
            // the canvas again and clears the arbiter slot — nothing is left visible.
            if (!PanelManager.NotifyOpened(_panelHandle)) return;
            _onReturn = onReturn;
            _navigation = new TownVisitNavigation(top, index, leaderboardRow, leaderboardScrollPosition);
            Load(top[index]).Forget();
        }

        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;
            _client = new TownShowcaseClient();
            _modal = ElarionUiKit.BuildObsidianModal("TownShowcaseVisit",
                new LocalizedText("hud.town_showcase.title").Resolve(),
                new Vector2(.04f, .04f), new Vector2(.96f, .96f), Close,
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "castle");
            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? (Transform)_modal.chrome.layout.body : _modal.chrome.content.transform;

            _title = Text(body, new LocalizedText("hud.town_showcase.loading").Resolve(),
                24, ElarionUi.Gilt, FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(.03f, .90f), new Vector2(.70f, .99f));
            _status = Text(body, new LocalizedText("hud.town_showcase.status_public").Resolve(),
                14, ElarionUi.ParchmentDim,
                FontStyles.Italic, TextAlignmentOptions.Left, new Vector2(.03f, .83f), new Vector2(.97f, .90f));

            var mapGo = new GameObject("ReadOnlyTownMap", typeof(RectTransform), typeof(Image));
            mapGo.transform.SetParent(body, false);
            _map = mapGo.GetComponent<RectTransform>();
            _map.anchorMin = new Vector2(.03f, .19f); _map.anchorMax = new Vector2(.97f, .82f);
            _map.offsetMin = Vector2.zero; _map.offsetMax = Vector2.zero;
            mapGo.GetComponent<Image>().color = new Color(.025f, .06f, .08f, .94f);

            var ambientGo = new GameObject("AmbientPresentationOnly", typeof(RectTransform));
            ambientGo.transform.SetParent(_map, false);
            _ambientHost = ambientGo.GetComponent<RectTransform>();
            _ambientHost.anchorMin = Vector2.zero; _ambientHost.anchorMax = Vector2.one;
            _ambientHost.offsetMin = Vector2.zero; _ambientHost.offsetMax = Vector2.zero;

            ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("hud.town_showcase.previous").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(.03f, .03f), new Vector2(.27f, .16f), Previous);
            ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("hud.town_showcase.return_leaderboard").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(.30f, .03f), new Vector2(.70f, .16f), Close);
            ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("common.next").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(.73f, .03f), new Vector2(.97f, .16f), Next);
            _modal.canvas.SetActive(false);
        }

        private async UniTaskVoid Load(TopTownVisitEntry entry)
        {
            if (entry == null || !entry.CanVisit) return;
            int generation = ++_loadGeneration;
            _activeSnapshotId = null;
            ClearMap();
            _title.text = "#" + entry.Rank + "  " + (string.IsNullOrEmpty(entry.Username)
                ? new LocalizedText("hud.town_showcase.anonymous_defender").Resolve()
                : entry.Username);
            _status.text = new LocalizedText("hud.town_showcase.loading_snapshot").Resolve();
            var result = await _client.FetchSnapshotAsync(entry.ShowcaseId, Application.version);
            if (generation != _loadGeneration || !IsOpen) return;
            if (!result.IsReady)
            {
                _status.text = result.Message ?? new LocalizedText("hud.town_showcase.unavailable").Resolve();
                return;
            }

            var projection = new ReadOnlyTownShowcaseView();
            if (!projection.Reconstruct(result.Snapshot, RegistryCatalog.Instance))
            {
                _status.text = new LocalizedText("hud.town_showcase.unsafe_snapshot").Resolve();
                return;
            }
            int missing = RenderStructures(projection.Structures);
            BuildAmbient(AmbientCount);
            _activeSnapshotId = result.Snapshot.SnapshotId;
            // WO-1857: ONE complete sentence per shape, never five concatenated fragments - the
            // optional placeholder clause is a SECOND key rather than a hole spliced mid-sentence,
            // because a locale cannot reorder around an empty string.
            _status.text = missing > 0
                ? LocalText.Format("hud.town_showcase.snapshot_line_with_placeholders",
                    result.Snapshot.SnapshotVersion, projection.Structures.Count, missing)
                : LocalText.Format("hud.town_showcase.snapshot_line",
                    result.Snapshot.SnapshotVersion, projection.Structures.Count);
        }

        private int RenderStructures(IReadOnlyList<ReadOnlyTownStructure> structures)
        {
            int missing = 0;
            for (int i = 0; i < structures.Count; i++)
            {
                var item = structures[i];
                if (item.IsFallback) missing++;
                var go = new GameObject(item.IsFallback ? "MissingSkuPlaceholder" : "Structure", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_map, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = Project(item.Position);
                rt.sizeDelta = new Vector2(30f, 30f);
                rt.localRotation = Quaternion.Euler(0f, 0f, -item.Rotation.eulerAngles.y);
                go.GetComponent<Image>().color = item.IsFallback
                    ? new Color(.85f, .22f, .25f, .95f)
                    : new Color(.40f, .73f, .95f, .95f);
                var label = Text(rt, item.IsFallback ? "?" : Mathf.Clamp(item.DisplayLevel, 1, 99).ToString(),
                    11, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
                label.gameObject.name = item.IsFallback ? item.FallbackLabel : item.RequestedItemId;
            }
            return missing;
        }

        private void BuildAmbient(int count)
        {
            count = Mathf.Clamp(count, 0, TownShowcaseAmbient.MaxAmbientEntities);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("AmbientPatrol_" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_ambientHost, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.sizeDelta = new Vector2(9f, 9f);
                go.GetComponent<Image>().color = new Color(.76f, .52f, 1f, .9f);
                _ambientMarkers.Add(rt);
            }
        }

        private void Update()
        {
            if (!IsOpen || string.IsNullOrEmpty(_activeSnapshotId) || _ambientMarkers.Count == 0) return;
            var points = TownShowcaseAmbient.Sample(_activeSnapshotId, _ambientMarkers.Count, Time.unscaledTime);
            Vector2 size = _ambientHost.rect.size;
            float scale = Mathf.Max(10f, Mathf.Min(size.x, size.y) / 18f);
            for (int i = 0; i < _ambientMarkers.Count; i++)
                _ambientMarkers[i].anchoredPosition = new Vector2(points[i].x, points[i].z) * scale;
        }

        private void Previous() { var entry = _navigation?.Previous(); if (entry != null) Load(entry).Forget(); }
        private void Next() { var entry = _navigation?.Next(); if (entry != null) Load(entry).Forget(); }

        private void Close()
        {
            ++_loadGeneration;
            _activeSnapshotId = null;
            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(false);
            PanelManager.NotifyClosed(_panelHandle);
            var callback = _onReturn;
            _onReturn = null;
            callback?.Invoke();
        }

        private void ClearMap()
        {
            _ambientMarkers.Clear();
            if (_map == null) return;
            for (int i = _map.childCount - 1; i >= 0; i--) Destroy(_map.GetChild(i).gameObject);
            // ClearMap removes the old ambient host too; recreate it so animation never retains stale entities.
            var go = new GameObject("AmbientPresentationOnly", typeof(RectTransform));
            go.transform.SetParent(_map, false);
            _ambientHost = go.GetComponent<RectTransform>();
            _ambientHost.anchorMin = Vector2.zero; _ambientHost.anchorMax = Vector2.one;
            _ambientHost.offsetMin = Vector2.zero; _ambientHost.offsetMax = Vector2.zero;
        }

        private static int Find(IReadOnlyList<TopTownVisitEntry> top, string id)
        {
            if (top == null) return -1;
            for (int i = 0; i < top.Count; i++)
                if (top[i] != null && string.Equals(top[i].ShowcaseId, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static Vector2 Project(Vector3 world) => new Vector2(
            Mathf.Clamp01(.5f + world.x / 80f), Mathf.Clamp01(.5f + world.z / 80f));

        private static TextMeshProUGUI Text(Transform parent, string value, float size, Color color,
            FontStyles style, TextAlignmentOptions alignment, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style;
            text.alignment = alignment; text.raycastTarget = false;
            ElarionUiKit.EnsureFont(text);
            return text;
        }

        private sealed class RegistryCatalog : IReadOnlyTownCatalog
        {
            public static readonly RegistryCatalog Instance = new RegistryCatalog();
            public bool ContainsStructure(string itemId) => CatalogRegistry.Get(itemId) != null;
        }
    }
}
