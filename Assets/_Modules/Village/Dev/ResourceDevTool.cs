// =============================================================================
// ResourceDevTool - a TOUCH, on-screen DEV grant overlay for the LOCAL tester APK.
// -----------------------------------------------------------------------------
// WHY THIS EXISTS (owner 2026-07-16: "on this APK can you add a resource devtool
//   ... since its only local"): the owner + testers need to grant resources while
//   felt-testing the Android tester APK. The existing dev surfaces do NOT reach it:
//     - DevPanel / AutoPilot / DevWalletProbe live in the DeNelle.DevTools assembly,
//       whose asmdef is `UNITY_EDITOR || DEVELOPMENT_BUILD`-constrained, and the
//       tester APK is a RELEASE build (AndroidBuild.BuildSeekerApk uses
//       BuildOptions.None) - so that whole assembly is STRIPPED from the APK.
//     - OwnerDevToolsOverlay ships in release but gates on Pi owner sign-in, which
//       does not fire on a native APK, so it stays dark there.
//   This tool is a RELEASE-SAFE, touch-driven sibling that DOES ship in the APK.
//
// ASSEMBLY: DeNelle.Village - deliberately. Village ships in every release build
//   (core gameplay), can build uGUI (BuildHudController et al.), references DeNelle.Core
//   (FeatureFlags / FlowTrace), and is the SAME assembly as EconomyService, so the
//   grant calls are DIRECT + null-safe (no reflection, unlike OwnerDevToolsOverlay
//   which lives in DeNelle.HUD and cannot reference Village).
//
// GRANT API (the real economy path, not a hack):
//   EconomyService.GrantSpendable(int wood=0, int stone=0, int iron=0, int crystals=0)
//     - lands Wood/Iron in BOTH wallets (in-session pool + GameState) and routes
//       Stone/Crystals through GameState (persist + ResourcesChanged), and
//   EconomyService.AddCoins(int) - GOLD via GameState.Resources.Coins (persist + event).
//   These are the exact methods OwnerDevToolsOverlay.GiveResources already relies on.
//
// DEV GATE (ShouldShow): Application.isEditor || Debug.isDebugBuild ||
//   FeatureFlags.DevResourceTool. Editor + any Development build always show it; a
//   RELEASE build (incl. the local tester APK, which is release-signed) shows it only
//   when the flag is ON. The flag DEFAULTS ON so it appears on the local tester APK
//   now; flip PlayerPrefs "ff.devresourcetool" = 0 (or default it OFF) before a PUBLIC
//   store release so real players never see it.
//
// SELF-BOOTSTRAP: a RuntimeInitializeOnLoadMethod (AfterSceneLoad) spawns ONE
//   DontDestroyOnLoad host + its own ScreenSpaceOverlay canvas - NO scene is edited
//   (mirrors OwnerDevToolsOverlay / EconomyService.Bootstrap). Fully guarded.
//
// UI: grey/white, ASCII-only, colorblind-safe (every control is TEXT-labelled - the
//   owner is red/green colorblind, so meaning is never carried by hue). A tiny "DEV"
//   chip on the left edge (easy to miss in normal play) opens/closes the grant panel.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core;                 // FeatureFlags
using DeNelle.Core.Diagnostics;     // FlowTrace

namespace DeNelle.Village.Dev
{
    /// <summary>
    /// Release-safe, touch-driven resource grant overlay for the local tester APK.
    /// Self-bootstraps behind a dev gate; grants through the real EconomyService API.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResourceDevTool : MonoBehaviour
    {
        public static ResourceDevTool Instance { get; private set; }

        private GameObject _canvasGo;
        private GameObject _panelGo;
        private Text _readout;

        // Grant tiers offered per currency row.
        private static readonly (string label, int amount)[] Tiers =
        {
            ("+100",   100),
            ("+1k",    1000),
            ("+10k",   10000),
            ("+1M",    1000000),
        };

        // ------------------------------------------------------------------
        // DEV GATE
        // ------------------------------------------------------------------
        private static bool ShouldShow()
        {
            // OWNER RULING 2026-08-07: "remove the flag and dev button ... for better screenshots."
            //
            // The DEV chip used to appear in the editor and in ANY development build
            // unconditionally, so it sat in shot alongside the FLAG chip. The flag is now the
            // SOLE gate on every platform - set ff.devresourcetool=1 to bring it back when you
            // actually want to grant resources (e.g. staging a town for captures).
            //
            // NOTE the flag is PlayerPrefs-backed and STICKY: a machine where it was switched on
            // earlier keeps it on until cleared. A clean profile is the only honest check.
            return FeatureFlags.DevResourceTool;
        }

        // ------------------------------------------------------------------
        // SELF-BOOTSTRAP (mirrors OwnerDevToolsOverlay / EconomyService.Bootstrap)
        // ------------------------------------------------------------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            if (!ShouldShow())
            {
                FlowTrace.Once("DevTool", "resdevtool-gate-blocked",
                    "ResourceDevTool gate BLOCKED (release build, ff.devresourcetool OFF) - not spawned.");
                return;
            }
            try
            {
                var go = new GameObject("[ResourceDevTool]");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<ResourceDevTool>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ResourceDevTool] Bootstrap failed (non-fatal): " + e.Message);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            try
            {
                BuildOverlay();
                FlowTrace.Step("DevTool", "ResourceDevTool bootstrapped (grant overlay ready).");
            }
            catch (Exception e)
            {
                FlowTrace.Warn("DevTool", $"BuildOverlay threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // GRANTS - the real EconomyService API (same assembly; null-conditional)
        // ------------------------------------------------------------------
        private enum Currency { Gold, Wood, Iron, Stone, Crystals }

        private void Grant(Currency currency, int amount)
        {
            var eco = EconomyService.Instance;
            if (eco == null) { SetReadout("EconomyService not alive yet - try again in a moment."); return; }

            switch (currency)
            {
                // WO-857 Phase F: a DEV grant is test setup, not player income — it bypasses the town
                // bank cap (GrantSpendableUncapped) so a +1M tier still lands and the storage-full
                // toast stays a real-economy signal instead of dev-tool noise.
                case Currency.Gold:     eco.AddCoins(amount);                         break; // GameState.Resources.Coins (uncapped by design)
                case Currency.Wood:     eco.GrantSpendableUncapped(wood: amount);     break; // both wallets
                case Currency.Iron:     eco.GrantSpendableUncapped(iron: amount);     break; // both wallets
                case Currency.Stone:     eco.GrantSpendableUncapped(stone: amount);     break; // GameState-backed
                case Currency.Crystals: eco.GrantSpendableUncapped(crystals: amount); break; // GameState-backed (uncapped by design)
            }

            FlowTrace.Step("DevTool", $"granted +{amount} {currency}.");
            RefreshReadout();
        }

        // ------------------------------------------------------------------
        // UI
        // ------------------------------------------------------------------
        private void BuildOverlay()
        {
            _canvasGo = new GameObject("ResourceDevCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5200;   // above gameplay HUD, below OwnerDev(5500)/DevPanel(9000) modals

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // --- tiny always-visible toggle chip (left edge, centered - easy to miss) ---
            var toggle = MakeButton(_canvasGo.transform, "DEV", TogglePanel);
            var trt = toggle.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 0.5f);
            trt.pivot = new Vector2(0f, 0.5f);
            trt.anchoredPosition = new Vector2(6f, 0f);
            trt.sizeDelta = new Vector2(72f, 40f);
            toggle.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.28f, 0.60f); // low-alpha grey

            BuildPanel();
            if (_panelGo != null) _panelGo.SetActive(false);
        }

        private void BuildPanel()
        {
            _panelGo = new GameObject("ResourceDevPanel", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            _panelGo.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.07f, 0.96f); // black panel
            var prt = _panelGo.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.anchoredPosition = new Vector2(84f, 0f);   // opens just right of the DEV chip
            prt.sizeDelta = new Vector2(520f, 10f);        // width fixed; height auto via fitter

            var vlg = _panelGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            var fitter = _panelGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Title.
            AddRowText("RESOURCE DEV TOOL (local build)", 18, new Color(0.95f, 0.95f, 0.95f, 1f));

            // Live readout of current wallet totals.
            _readout = AddRowText("...", 15, new Color(0.80f, 0.85f, 0.80f, 1f));

            // One row per currency.
            AddCurrencyRow("Gold",     Currency.Gold);
            AddCurrencyRow("Wood",     Currency.Wood);
            AddCurrencyRow("Iron",     Currency.Iron);
            AddCurrencyRow("Stone",     Currency.Stone);
            AddCurrencyRow("Crystals", Currency.Crystals);

            // Close row.
            var close = MakeButton(_panelGo.transform, "Close", TogglePanel);
            AddLayout(close.gameObject, 44f);

            RefreshReadout();
        }

        /// <summary>A currency row: a fixed-width name label + the four grant-tier buttons.</summary>
        private void AddCurrencyRow(string name, Currency currency)
        {
            var rowGo = new GameObject("Row_" + name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(_panelGo.transform, false);
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            AddLayout(rowGo, 48f);

            // Name label (fixed width so all tier buttons line up).
            var lblTxt = MakeText(rowGo.transform, name, 16, TextAnchor.MiddleLeft, Color.white);
            var lblLe = lblTxt.gameObject.AddComponent<LayoutElement>();
            lblLe.minWidth = 96f;
            lblLe.preferredWidth = 96f;
            lblLe.flexibleWidth = 0f;

            foreach (var (label, amount) in Tiers)
            {
                int amt = amount;                 // capture per iteration
                var btn = MakeButton(rowGo.transform, label, () => RunGrant(currency, amt));
            }
        }

        private void RunGrant(Currency currency, int amount)
        {
            try
            {
                Grant(currency, amount);
            }
            catch (Exception e)
            {
                FlowTrace.Warn("DevTool", $"grant +{amount} {currency} FAILED: {e.GetType().Name}: {e.Message}");
                SetReadout($"grant {currency} FAILED: {e.Message}");
            }
        }

        private void TogglePanel()
        {
            if (_panelGo == null) return;
            bool show = !_panelGo.activeSelf;
            _panelGo.SetActive(show);
            if (show) RefreshReadout();
            FlowTrace.Step("DevTool", $"panel {(show ? "OPENED" : "closed")}.");
        }

        private void RefreshReadout()
        {
            var eco = EconomyService.Instance;
            if (eco == null) { SetReadout("EconomyService not alive yet."); return; }
            SetReadout($"Gold {eco.Coins}  Wood {eco.Wood}  Iron {eco.Iron}  Stone {eco.Stone}  Crystals {eco.Crystals}");
        }

        private void SetReadout(string s)
        {
            if (_readout != null) _readout.text = s;
        }

        // ------------------------------------------------------------------
        // UI HELPERS (grey/white, ASCII, colorblind-safe - mirror OwnerDevToolsOverlay)
        // ------------------------------------------------------------------
        private Text AddRowText(string content, int size, Color color)
        {
            var t = MakeText(_panelGo.transform, content, size, TextAnchor.MiddleLeft, color);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            AddLayout(t.gameObject, 30f);
            return t;
        }

        private static void AddLayout(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        private static Button MakeButton(Transform parent, string label, Action onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.20f, 0.22f, 0.28f, 0.98f); // grey face
            var btn = go.GetComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var txt = MakeText(go.transform, label, 16, TextAnchor.MiddleCenter, Color.white);
            var lrt = txt.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4f, 2f); lrt.offsetMax = new Vector2(-4f, -2f);
            txt.raycastTarget = false;
            return btn;
        }

        private static Text MakeText(Transform parent, string content, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            return t;
        }
    }
}
