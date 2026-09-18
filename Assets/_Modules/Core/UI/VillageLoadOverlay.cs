// =============================================================================
// VillageLoadOverlay — a code-built full-screen loading screen for the village.
// -----------------------------------------------------------------------------
// PROBLEM (owner): loading Village2 is a 3-6s SYNCHRONOUS hitch that shows the
// player a black screen with no feedback — a lumpy first minute. SceneRouter.
// GoVillage used SceneManager.LoadScene (a blocking call), so nothing could render
// during the load.
//
// FIX: an async load (LoadSceneAsync) with this overlay rendered ON TOP while it
// runs — "Loading Elarion…", an animated spinner, a progress bar, and a rotating
// lore tidbit so the wait feels intentional and on-brand. Hidden + destroyed the
// moment the new scene is active.
//
// WO-C restyle (2026-07-03): the legacy uGUI Text (LegacyRuntime.ttf) labels are
// now TMP through ElarionUiKit.EnsureFont (the kit's proven font chain), the
// unicode ring spinner is a rotating gold diamond Image (NO unicode glyphs in
// TMP — ASCII rule), and the progress strip is THE kit bar
// (ElarionUiKit.BuildObsidianBar, Loading kind) — it renders the Blink loading
// bar art when the mirrored pack is present and degrades to the procedural
// track+fill otherwise. Dark obsidian backdrop, gold title, parchment lore.
//
// BUILD-SAFETY: built entirely in CODE with uGUI (Canvas / Image / TMP) — NOT
// UI Toolkit and NOT UXML. This sidesteps BOTH project landmines:
//   • "UXML/UIDocuments don't render in player builds" (CLAUDE.md §8), and
//   • the UIDocument PanelSettings-null regression (a UIDocument needs a
//     PanelSettings asset; a code-built uGUI Canvas needs nothing).
// A DontDestroyOnLoad canvas at a very high sortingOrder so it paints over the old
// AND the loading scene. Styled from the shared ElarionUi palette so it matches
// the rest of the game. Self-contained: one static Show()/HideAndDestroy() pair
// driven by SceneRouter.LoadVillageWithLoader.
// =============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// A self-contained, code-built (uGUI + TMP, no UXML / no PanelSettings)
    /// full-screen loading overlay shown during the async village scene load.
    /// Created via <see cref="Show"/>; driven + torn down by <see cref="SceneRouter"/>.
    /// </summary>
    public sealed class VillageLoadOverlay : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _spinner;
        private ElarionUiKit.BarHandle _progress;
        private TextMeshProUGUI _loreLabel;

        // Single-modal arbiter handle. The load overlay is a full-screen SYSTEM overlay that must
        // never be rejected or force-closed by the battle-lock, so it registers battle-allowed.
        // Opening it closes any lingering gameplay panel before the scene swaps.
        private PanelHandle _panelHandle;

        private float _spinSpeed = 220f;     // deg / sec
        private float _loreTimer;
        private int _loreIndex;
        private const float LoreRotateSeconds = 3.2f;

        // Rotating lore tidbits — short, on-brand, set the tone during the wait.
        // ⛔ RETIRED CANON MUST NOT RE-ENTER THIS ARRAY (2026-09-06). The line
        // "Hold the last light." shipped here for months after the 2026-07-24
        // Pets→Echoes rebrand retired it; the canonical tagline is
        // "Echoes of a Forgotten Civilization" (canon-strings.json "tagline").
        // GlossaryRegression case [ui-source-copy] now lints this array's
        // literals, so a re-add fails the suite instead of reaching a player.
        private static readonly string[] Lore =
        {
            "Elarion holds because we hold the line.",
            "The Echoes rest in the Hollow, waiting to be called.",
            "Every tower is a promise the gate will not fall.",
            "The enemy comes by night. We rebuild by day.",
            "Stone remembers. So do we.",
        };

        /// <summary>
        /// Builds + shows the loading overlay on a fresh DontDestroyOnLoad canvas and
        /// returns it. Call <see cref="SetProgress"/> while the scene loads and
        /// <see cref="HideAndDestroy"/> when it is active.
        /// </summary>
        public static VillageLoadOverlay Show()
        {
            var go = new GameObject("VillageLoadOverlay");
            DontDestroyOnLoad(go);
            var overlay = go.AddComponent<VillageLoadOverlay>();
            overlay.Build();
            return overlay;
        }

        private void Build()
        {
            // ── Canvas (screen-space overlay, very high sort so it's always on top) ──
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;   // above HUD / fade / everything
            gameObject.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var scaler = GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;
            _group.blocksRaycasts = true;   // eat input under the loader

            // ── Full-screen background (menu bg texture if present, else stone fill) ─
            var bg = NewImage("Backdrop", transform);
            Stretch(bg.rectTransform);
            var menuTex = ElarionUi.MenuBackground;
            if (menuTex != null)
            {
                bg.sprite = Sprite.Create(menuTex,
                    new Rect(0, 0, menuTex.width, menuTex.height), new Vector2(0.5f, 0.5f));
                bg.type = Image.Type.Simple;
                bg.preserveAspect = false;
                bg.color = Color.white;
            }
            else
            {
                bg.color = ElarionUi.PanelStoneDark;   // deep obsidian
            }

            // A dark obsidian scrim so text reads over any busy bg.
            var scrim = NewImage("Scrim", transform);
            Stretch(scrim.rectTransform);
            scrim.color = new Color(0.02f, 0.015f, 0.025f, 0.60f);

            // ── Title (gold, TMP) ─────────────────────────────────────────────────
            var title = NewText("Title", transform,
                new LocalizedText("loading.village.title").Resolve(), 64, bold: true);
            title.color = ElarionUi.Gilt;
            title.alignment = TextAlignmentOptions.Center;
            var tRt = title.rectTransform;
            tRt.anchorMin = new Vector2(0.5f, 0.5f);
            tRt.anchorMax = new Vector2(0.5f, 0.5f);
            tRt.pivot = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = new Vector2(0, 160);
            tRt.sizeDelta = new Vector2(900, 120);

            // ── Spinner — a rotating gold DIAMOND Image (no unicode glyph in TMP) ─
            var spinGo = new GameObject("Spinner", typeof(RectTransform), typeof(Image));
            spinGo.transform.SetParent(transform, false);
            var spinImg = spinGo.GetComponent<Image>();
            spinImg.sprite = ElarionUiKit.SolidSprite;
            spinImg.color = ElarionUi.Gold;
            spinImg.raycastTarget = false;
            _spinner = spinGo.GetComponent<RectTransform>();
            _spinner.anchorMin = new Vector2(0.5f, 0.5f);
            _spinner.anchorMax = new Vector2(0.5f, 0.5f);
            _spinner.pivot = new Vector2(0.5f, 0.5f);
            _spinner.anchoredPosition = new Vector2(0, 20);
            _spinner.sizeDelta = new Vector2(72, 72);
            _spinner.localRotation = Quaternion.Euler(0f, 0f, 45f);   // square -> diamond

            // A dark inner square so the diamond reads as a gold RING, not a slab.
            var spinInner = NewImage("SpinnerInner", spinGo.transform);
            spinInner.color = new Color(0.02f, 0.015f, 0.025f, 1f);
            var siRt = spinInner.rectTransform;
            siRt.anchorMin = Vector2.zero; siRt.anchorMax = Vector2.one;
            siRt.offsetMin = new Vector2(10f, 10f);
            siRt.offsetMax = new Vector2(-10f, -10f);
            spinInner.raycastTarget = false;

            // ── Progress bar — THE kit bar (Blink loading art when mirrored) ──────
            var barHost = new GameObject("ProgressBar", typeof(RectTransform));
            barHost.transform.SetParent(transform, false);
            var bRt = (RectTransform)barHost.transform;
            bRt.anchorMin = new Vector2(0.5f, 0.5f);
            bRt.anchorMax = new Vector2(0.5f, 0.5f);
            bRt.pivot = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = new Vector2(0, -120);
            bRt.sizeDelta = new Vector2(640, 34);
            _progress = ElarionUiKit.BuildObsidianBar(barHost.transform,
                ElarionUiKit.ObsidianBarKind.Loading, Vector2.zero, Vector2.one);
            _progress.SetImmediate(0.05f, 1f);

            // ── Rotating lore tidbit (parchment, TMP) ─────────────────────────────
            _loreIndex = Random.Range(0, Lore.Length);
            var lore = NewText("Lore", transform, Lore[_loreIndex], 30, bold: false);
            lore.fontStyle = FontStyles.Italic;
            lore.color = ElarionUi.ParchmentDim;
            lore.alignment = TextAlignmentOptions.Center;
            lore.textWrappingMode = TextWrappingModes.Normal;
            _loreLabel = lore;
            var lRt = lore.rectTransform;
            lRt.anchorMin = new Vector2(0.5f, 0.5f);
            lRt.anchorMax = new Vector2(0.5f, 0.5f);
            lRt.pivot = new Vector2(0.5f, 0.5f);
            lRt.anchoredPosition = new Vector2(0, -210);
            lRt.sizeDelta = new Vector2(820, 100);

            // Register with the single-modal arbiter (battle-allowed — a loading screen must never
            // be blocked). isOpen tracks the live overlay; the close delegate tears it down.
            if (_panelHandle == null)
                _panelHandle = PanelManager.RegisterBattleAllowed("VillageLoad", HideAndDestroy, () => this != null);
            PanelManager.NotifyOpened(_panelHandle);
        }

        private void Update()
        {
            // Spin the diamond (unscaled — a paused timescale during load must not freeze it).
            if (_spinner != null)
                _spinner.Rotate(0f, 0f, -_spinSpeed * Time.unscaledDeltaTime);

            // Rotate the lore tidbit on a timer.
            _loreTimer += Time.unscaledDeltaTime;
            if (_loreTimer >= LoreRotateSeconds && _loreLabel != null)
            {
                _loreTimer = 0f;
                _loreIndex = (_loreIndex + 1) % Lore.Length;
                _loreLabel.text = Lore[_loreIndex];
            }
        }

        /// <summary>Sets the progress bar fill (0..1). Clamped + given a small floor so it always reads as "moving".</summary>
        public void SetProgress(float t)
        {
            if (_progress != null)
                _progress.SetImmediate(Mathf.Clamp(t, 0.05f, 1f), 1f);
        }

        /// <summary>Hides + destroys the overlay (call once the new scene is active).</summary>
        public void HideAndDestroy()
        {
            // Release the arbiter slot as the overlay tears down (no-op if already released).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            if (this != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Safety net — don't leak the arbiter slot if destroyed by any other path.
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
        }

        // ── uGUI builder helpers ──────────────────────────────────────────────

        private static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Image>();
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string text,
            int size, bool bold)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);   // the kit's proven TMP font chain (assign BEFORE .text)
            t.text = text;
            t.fontSize = size;
            if (bold) t.fontStyle = FontStyles.Bold;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
