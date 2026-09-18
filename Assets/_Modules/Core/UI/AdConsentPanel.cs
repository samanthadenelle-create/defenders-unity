// =============================================================================
// AdConsentPanel — the ad-privacy prompt, and the door back to it from Settings.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core (Core/UI). Code-built uGUI on the Obsidian kit — no UXML
// (UXML does not render in builds, CLAUDE.md §8).
//
// SHOWN TO EVERYONE, NOT JUST THE EEA. Geo-gating a consent prompt means shipping
// a region guess, and a wrong guess fails silently in the direction that matters:
// a European player quietly never asked. Asking everyone costs one screen once and
// cannot be wrong about who you are.
//
// PLAIN LANGUAGE, AND AN HONEST "NO". Both buttons are real choices and the copy
// says what each does. Declining does NOT switch ads off — it switches them to
// NON-PERSONALISED, which is the truth, and hiding that would make the "no" feel
// like a trick when ads still appear.
//
// NEVER MEANING BY COLOUR ALONE (the owner is red/green colourblind) — every state
// is worded.
//
// ⚠ THE OLD "ASCII-only strings: non-ASCII renders as tofu in TMP" LINE IS RETIRED
// (WO-1857, 2026-09-18). This panel's copy now resolves through LocalizedText keys
// (ads.consent.*), so it renders Cyrillic, Arabic and CJK whenever the player's locale
// asks for them — an ASCII rule here would have been a rule this file can no longer
// keep. Whether the shipped TMP font atlas COVERS those ranges is a separate, and as
// of this change UNPROVEN, question: it needs a Unity capture, not a comment. It is
// recorded as the open risk in Logs/debug/scratch/sweep-core-summary.md.
//
// ⚠ TECHNICAL IMPLEMENTATION, NOT LEGAL ADVICE. The wording here is written to be
// clear and honest; whether it satisfies a particular regime is the owner's call
// with counsel. What this file guarantees is that the answer is captured before
// anything initialises, and that it can always be changed.
// =============================================================================

using System;
using UnityEngine;
using TMPro;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Monetization;

namespace DeNelle.Core.UI
{
    /// <summary>The ad-privacy prompt. One at a time.</summary>
    public sealed class AdConsentPanel : MonoBehaviour
    {
        private static AdConsentPanel s_active;

        private ElarionUiKit.ObsidianModal _modal;
        private PanelHandle _panelHandle;
        private Action _onDecided;

        /// <summary>
        /// Show the prompt. No-op when one is already up. <paramref name="onDecided"/> fires once
        /// the player answers (or immediately if they already had) — the mediation layer waits on
        /// it so init can never run ahead of the answer.
        /// </summary>
        public static void Show(Action onDecided = null)
        {
            if (s_active != null) return;

            var go = new GameObject("AdConsentPanel");
            var p = go.AddComponent<AdConsentPanel>();   // Awake registers the arbiter handle
            p._onDecided = onDecided;
            DontDestroyOnLoad(go);
            p.Build();

            // The arbiter can REJECT (battle-lock). Honour it: report through the callback so a
            // waiting caller is never left hanging on a panel that was torn down before it drew.
            if (!PanelManager.NotifyOpened(p._panelHandle))
            {
                FlowTrace.Warn("AdConsent",
                    "consent prompt REJECTED by the modal arbiter (battle-lock) - not shown; " +
                    "the caller is notified so nothing waits on an answer that cannot come.");
                onDecided?.Invoke();
                return;
            }

            s_active = p;
        }

        private void Awake()
        {
            // A top-band modal with no PanelManager handle leaves AnyOpen FALSE while it covers
            // the screen - the world interact button stays live underneath and back has nothing to
            // close. Same class as the Echo FTUE cascade.
            _panelHandle = PanelManager.Register("Ad Privacy", CloseWithoutChoice, IsShowing);
        }

        private bool IsShowing() => _modal != null && _modal.canvas != null && _modal.canvas.activeInHierarchy;

        private void Build()
        {
            _modal = ElarionUiKit.BuildObsidianModal(
                "AdConsentUI", new LocalizedText("ads.consent.title").Resolve(),
                new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.82f),
                onClose: null, sortingOrder: 31020);   // no close X: this is a question, not a notice
            MedievalUiSkin.ApplyShell(_modal.chrome, compact: true);

            var content = _modal.chrome.content.transform;

            ElarionUiKit.Label(content,
                new LocalizedText("ads.consent.body").Resolve(),
                0.38f, 0.82f, ElarionUi.Parchment, 32, TextAlignmentOptions.TopLeft,
                0.06f, 0.94f);

            var yes = ElarionUiKit.Button(content, new LocalizedText("ads.consent.accept").Resolve(),
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.06f, 0.20f), new Vector2(0.48f, 0.35f), () => Answer(true));

            var no = ElarionUiKit.Button(content, new LocalizedText("ads.consent.decline").Resolve(),
                ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.52f, 0.20f), new Vector2(0.94f, 0.35f), () => Answer(false));
            MedievalUiSkin.ApplyButton(yes, primary: true);
            MedievalUiSkin.ApplyButton(no, primary: false);
        }

        private void Answer(bool granted)
        {
            AdConsentService.SetGdpr(granted);
            FlowTrace.Step("AdConsent", $"player answered the consent prompt: {(granted ? "YES" : "NO")}");
            Finish();
        }

        /// <summary>
        /// The arbiter closed us without an answer (battle-lock swap). Do NOT record a choice —
        /// silence is not consent, and writing Denied here would make "was never asked" and
        /// "said no" indistinguishable for ever. The caller is still released so nothing hangs.
        /// </summary>
        private void CloseWithoutChoice()
        {
            FlowTrace.Warn("AdConsent",
                "consent prompt closed with NO answer recorded (arbiter swap). State stays Unknown, " +
                "so the prompt will be asked again rather than assuming a refusal.");
            Finish();
        }

        private void Finish()
        {
            var cb = _onDecided;
            _onDecided = null;
            PanelManager.NotifyClosed(_panelHandle);
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
            if (s_active == this) s_active = null;
            Destroy(gameObject);
            Guard.Try("AdConsent", "notify consent decided", () => cb?.Invoke());
        }

        private void OnDestroy()
        {
            if (s_active == this) s_active = null;
        }
    }
}
