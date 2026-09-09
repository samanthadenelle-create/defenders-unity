using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// Layered runtime skin for the approved black-iron / antique-gold UI kit.
    /// It changes presentation only; callers retain their commands and live data.
    /// </summary>
    public static class MedievalUiSkin
    {
        private const string Root = "UI/ElarionMedieval/";

        public static void ApplyShell(ElarionUiKit.PanelChrome chrome, bool compact = false)
        {
            if (chrome == null) return;
            var root = chrome.root != null ? chrome.root.GetComponent<Image>() : null;
            var frame = Resources.Load<Sprite>(Root +
                (compact ? "frames/content-panel" : "frames/modal-frame-16x9"));
            if (root != null && frame != null)
            {
                root.sprite = frame;
                root.type = Image.Type.Sliced;
                root.fillCenter = true;
                root.color = Color.white;
            }

            // Legacy chrome paints an opaque raw-black inner rectangle over the layered
            // frame. The approved shell owns its textured center, so reveal it.
            var legacyFill = chrome.content != null ? chrome.content.GetComponent<Image>() : null;
            if (legacyFill != null)
                legacyFill.color = new Color(1f, 1f, 1f, 0f);

            if (chrome.layout != null && chrome.layout.medallion != null)
                chrome.layout.medallion.gameObject.SetActive(false);

            if (chrome.title != null)
            {
                chrome.title.text = (chrome.title.text ?? string.Empty).ToUpperInvariant();
                chrome.title.color = ElarionUi.Gold;
                chrome.title.fontStyle |= FontStyles.Bold;
                chrome.title.characterSpacing = 3f;
                ElarionUiKit.EnsureFont(chrome.title, ElarionUiKit.FontRole.Title);
            }

            ApplyClose(chrome.close);
        }

        public static void ApplyButton(Button button, bool primary = false)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            var gold = Resources.Load<Sprite>(Root + "buttons/button-normal-empty");
            var pressed = Resources.Load<Sprite>(Root + "buttons/button-pressed-empty");
            var disabled = Resources.Load<Sprite>(Root + "buttons/button-disabled-empty");
            // Every enabled action belongs to the same black-iron / antique-gold family.
            // The previous expression deliberately assigned the DISABLED face to every
            // non-primary button, which is why secondary choices rendered as legacy silver
            // even while fully interactive. Disabled art is state-only, never normal chrome.
            var normal = gold;
            if (image != null && normal != null)
            {
                image.sprite = normal;
                // The supplied 2048x512 button plate carries very deep importer borders.
                // On compact landscape actions those borders exceed the rendered height and
                // Unity collapses the entire nine-slice to an invisible centre, leaving a
                // floating label. Preserve the authored silhouette by scaling the complete
                // plate; its native 4:1 aspect is already the action family's target ratio.
                image.type = Image.Type.Simple;
                image.fillCenter = true;
                image.color = primary ? new Color(1.08f, 1.03f, 0.88f, 1f) : Color.white;
                button.targetGraphic = image;
                button.transition = Selectable.Transition.SpriteSwap;
                var states = button.spriteState;
                states.highlightedSprite = pressed != null ? pressed : normal;
                states.selectedSprite = pressed != null ? pressed : normal;
                states.pressedSprite = pressed != null ? pressed : normal;
                states.disabledSprite = disabled != null ? disabled : normal;
                button.spriteState = states;
            }

            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = (label.text ?? string.Empty).ToUpperInvariant();
                label.color = ElarionUi.Parchment;
                label.fontStyle |= FontStyles.Bold;
                label.characterSpacing = 2f;
                label.fontSize = 44f;
                ElarionUiKit.EnsureFont(label, ElarionUiKit.FontRole.Title);
                // Button geometry varies from compact row actions to full modal CTAs. Keep the
                // authored 44px ceiling, but fit toward the project's legibility floor instead
                // of truncating valid runtime copy such as "Build new defense".
                ElarionUiKit.FitSingleLine(label, 30f, 44f);
            }
        }

        public static void ApplyClose(Button button)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            var sprite = Resources.Load<Sprite>(Root + "buttons/close-ornate");
            // The ornate plate bakes the English word CLOSE into the pixels. It is
            // valid only for English; every other locale keeps the normal button
            // background and renders the shared CommonText label through TMP.
            bool authoredLabel = image != null && sprite != null &&
                                 string.Equals(LocalText.LanguageCode, "en", System.StringComparison.OrdinalIgnoreCase);
            if (authoredLabel)
            {
                image.sprite = sprite;
                // The ornate source has deep borders; at the shared short Close footprint a
                // nine-slice can collapse to an invisible centre. Preserve the complete plate.
                image.type = Image.Type.Simple;
                image.fillCenter = true;
                image.color = Color.white;
                // Close previously retained the legacy SpriteSwap state. Hover/select then
                // replaced the valid ornate face with a blank pack sprite. A Close has one
                // silhouette in every state; interaction feedback comes from tint, never from
                // swapping to unrelated art.
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, .94f, .78f, 1f);
                colors.selectedColor = colors.highlightedColor;
                colors.pressedColor = new Color(.78f, .68f, .52f, 1f);
                colors.disabledColor = new Color(.48f, .48f, .48f, .72f);
                colors.colorMultiplier = 1f;
                colors.fadeDuration = .08f;
                button.colors = colors;
                button.targetGraphic = image;
            }
            var labels = button.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                if (label == null) continue;
                // close-ornate.png already contains the word CLOSE. A live TMP copy over
                // that baked label produces the doubled/offset word seen on shared modals.
                // Keep runtime text only as the visual fallback if the plate is missing.
                if (authoredLabel)
                {
                    label.gameObject.SetActive(false);
                    continue;
                }
                label.gameObject.SetActive(true);
                label.text = CommonText.Close.Resolve();
                label.color = ElarionUi.Parchment;
                label.fontStyle |= FontStyles.Bold;
                label.characterSpacing = 2f;
                label.fontSize = 44f;
                ElarionUiKit.EnsureFont(label, ElarionUiKit.FontRole.Title);
                ElarionUiKit.FitSingleLine(label, 30f, 44f);
            }
        }
    }
}
