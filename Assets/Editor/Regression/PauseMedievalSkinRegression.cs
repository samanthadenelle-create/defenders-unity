#if UNITY_EDITOR
using System;
using System.IO;

namespace DeNelle.Editor.Regression
{
    /// <summary>Locks the approved compact Pause composition and shared skin seam.</summary>
    public static class PauseMedievalSkinRegression
    {
        public static bool Run(out string result)
        {
            try
            {
                string pause = File.ReadAllText("Assets/_Modules/Settings/PauseController.cs");
                Require(pause, "MedievalUiSkin.ApplyShell(_modal.chrome, compact: true)");
                Require(pause, "AspectRatioFitter.AspectMode.HeightControlsWidth");
                // WO-1857: the three pause faces now resolve through LocalizedText, so these pin the
                // resolved KEYS. The middle face reuses the Settings screen's own settings.title row
                // rather than minting a synonym. The retired English captions are deliberately not
                // reproduced here - one of them still appears in a comment inside PauseController.cs,
                // so a source-text needle on it would pass off that comment and assert nothing.
                Require(pause, "\"settings.pause.resume\"");
                Require(pause, "\"settings.title\"");
                Require(pause, "\"settings.pause.quit_to_title\"");
                Require(pause, "MedievalUiSkin.ApplyButton(resume, primary: true)");
                Require(pause, "WorldHold.AcquirePlayerOwned(WorldHold.ReasonPauseMenu,");
                // WO-1369: the probe argument is REQUIRED, and it must be a liveness test on the
                // controller - never a duration test (that is the WO-1353 regression).
                Require(pause, "() => this != null && isActiveAndEnabled");
                Forbid(pause, "BuildButtonColumn(body");

                string skin = File.ReadAllText("Assets/_Modules/Core/UI/MedievalUiSkin.cs");
                Require(skin, "Image.Type.Sliced");
                Require(skin, "buttons/button-disabled-empty");
                Require(skin, "buttons/close-ornate");
                Require(skin, "bool authoredLabel");
                Require(skin, "GetComponentsInChildren<TMP_Text>(true)");
                Require(skin, "label.gameObject.SetActive(false)");

                string buttons = File.ReadAllText("Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs");
                Require(buttons, "CanonicalizeButtonLabels");
                Require(buttons, "GetComponentsInChildren<TMP_Text>(true)");
                Require(buttons, "candidate.gameObject.SetActive(false)");

                result = "Pause uses the compact medieval shell, the baked Close label is the sole authority, three approved actions, and authoritative WorldHold.";
                return true;
            }
            catch (Exception ex)
            {
                result = ex.Message;
                return false;
            }
        }

        private static void Require(string source, string token)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Pause reskin contract missing: " + token);
        }

        private static void Forbid(string source, string token)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Legacy Pause layout returned: " + token);
        }
    }
}
#endif
