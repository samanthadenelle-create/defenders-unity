using System.Collections;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Promo;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Onboarding
{
    /// <summary>WO-1264 launch thank-you letter, shown once after FIRSTWATCH succeeds.</summary>
    public sealed class FirstWatchWelcomeLetter : MonoBehaviour
    {
        private const string SeenKey = "eoa.first-watch-thanks.2026-08";
        private const string CampaignCode = "FIRSTWATCH";
        private static FirstWatchWelcomeLetter _instance;
        private ElarionUiKit.ObsidianModal _modal;
        private PanelHandle _panelHandle;
        private PromoCodeService _promo;
        private bool _open;
        private bool _queued;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isBatchMode || PlayerPrefs.GetInt(SeenKey, 0) == 1) return;
            if (_instance != null) return;
            var go = new GameObject("[FirstWatchWelcomeLetter]");
            DontDestroyOnLoad(go);
            go.AddComponent<FirstWatchWelcomeLetter>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _panelHandle = PanelManager.Register("First Watch Letter", Close, () => _open);
        }

        private void Update()
        {
            if (_promo != null) return;
            _promo = PromoCodeService.Instance;
            if (_promo != null) _promo.OnCodeRedeemed += HandleCodeRedeemed;
        }

        private void HandleCodeRedeemed(string code, PromoReward reward)
        {
            if (_queued || PlayerPrefs.GetInt(SeenKey, 0) == 1 ||
                !string.Equals(code, CampaignCode, System.StringComparison.OrdinalIgnoreCase)) return;
            _queued = true;
            StartCoroutine(WaitForRedeemPanelToClose());
        }

        private IEnumerator WaitForRedeemPanelToClose()
        {
            while (PanelManager.AnyOpen) yield return null;
            Show();
        }

        private void Show()
        {
            _modal = ElarionUiKit.BuildObsidianModal(
                "FirstWatchWelcomeLetterUI",
                new LocalizedText("onboarding.first_watch.welcome_title").Resolve(),
                new Vector2(0.20f, 0.16f), new Vector2(0.80f, 0.84f),
                Close, sortingOrder: 31010);
            if (_modal == null || _modal.canvas == null || _modal.chrome == null)
            {
                FlowTrace.Fail("FirstWatch", "welcome letter could not build; it will retry next session");
                Destroy(gameObject);
                return;
            }

            _open = true;
            if (!PanelManager.NotifyOpened(_panelHandle))
            {
                _open = false;
                Destroy(_modal.canvas);
                _modal = null;
                StartCoroutine(WaitForRedeemPanelToClose());
                return;
            }

            var content = _modal.chrome.content.transform;
            var letterTexture = Resources.Load<Texture2D>("UI/Onboarding/welcome-letter-complete-v1");
            if (letterTexture == null)
            {
                FlowTrace.Fail("FirstWatch", "welcome letter art missing; refusing blank modal");
                Close();
                return;
            }

            var artGo = new GameObject("WelcomeLetterArt", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
            artGo.transform.SetParent(content, false);
            var artRect = (RectTransform)artGo.transform;
            artRect.anchorMin = new Vector2(0.04f, 0.22f);
            artRect.anchorMax = new Vector2(0.96f, 0.98f);
            artRect.offsetMin = Vector2.zero;
            artRect.offsetMax = Vector2.zero;
            var art = artGo.GetComponent<RawImage>();
            art.texture = letterTexture;
            art.color = Color.white;
            art.raycastTarget = false;
            var fitter = artGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)letterTexture.width / letterTexture.height;

            // WO-1857: the dismiss face reuses the game-wide common.close
            // (COMMON_KEYS_REGISTRY names this exact call site).
            ElarionUiKit.BuildObsidianButton(content, CommonText.Close.Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1,
                ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.28f, 0.02f), new Vector2(0.72f, 0.20f), Close);

            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            FlowTrace.Step("FirstWatch", "welcome letter shown (campaign code withheld)");
        }

        private void OnDestroy()
        {
            if (_promo != null) _promo.OnCodeRedeemed -= HandleCodeRedeemed;
            if (_instance == this) _instance = null;
        }

        private void Close()
        {
            _open = false;
            PanelManager.NotifyClosed(_panelHandle);
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
            _modal = null;
            Destroy(gameObject);
        }
    }
}
