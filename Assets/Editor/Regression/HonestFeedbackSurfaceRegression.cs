#if UNITY_EDITOR
using System;
using System.IO;

namespace DeNelle.Editor.Regression
{
    /// <summary>Locks the repaired layout, permanent Settings door, and localization seam.</summary>
    public static class HonestFeedbackSurfaceRegression
    {
        public static bool Run(out string result)
        {
            try
            {
                string panel = File.ReadAllText("Assets/_Modules/Village/Feedback/HonestFeedbackPanel.cs");
                string bootstrap = File.ReadAllText("Assets/_Modules/Village/Feedback/HonestFeedbackPanelBootstrap.cs");
                string settings = File.ReadAllText("Assets/_Modules/Settings/SettingsController.cs");
                string service = File.ReadAllText("Assets/_Modules/Village/Feedback/HonestFeedbackService.cs");
                string local = File.ReadAllText("Assets/_Modules/Core/UI/LocalText.cs");
                string streamEnglish = File.ReadAllText("Assets/StreamingAssets/Data/Canonical/en.json");
                string resourceEnglish = File.ReadAllText("Assets/Resources/Data/Canonical/en.json");

                Require(panel, "ModalArchetype.Browse");
                Require(panel, "var body = _modal.chrome.content.transform");
                Require(panel, "new Vector2(0.08f, 0.49f), new Vector2(0.58f, 0.65f)");
                Require(panel, "new Vector2(0.63f, 0.60f), new Vector2(0.93f, 0.80f)");
                Require(panel, "new Vector2(0.63f, 0.33f), new Vector2(0.93f, 0.53f)");
                Require(panel, "HonestFeedbackText.Body");
                Require(panel, "HonestFeedbackText.NetworkFailed");
                Require(panel, "TooShort.Resolve(");
                Require(panel, "OverCapacity.Resolve(");

                Forbid(bootstrap, "if (HonestFeedbackGrant.HasClaimed() || HonestFeedbackGrant.HasBeenOffered())");
                Require(bootstrap, "panelGo.AddComponent<HonestFeedbackPanel>()");
                Require(bootstrap, "gateGo.AddComponent<HonestFeedbackService>()");

                Require(settings, "HonestFeedbackText.SettingsButton");
                Require(settings, "HonestFeedbackText.SettingsButton.Resolve()");
                Require(settings, "PanelRouter.Open(PanelId.HonestFeedback)");
                Require(service, "language = DeNelle.Core.UI.LocalText.LanguageCode");
                Require(service, "systemLanguage = Application.systemLanguage.ToString()");
                Require(local, "Data/Canonical/");
                Require(local, "public const string KeyTitle = \"feedback.title\"");
                Require(local, "LocalizedText<MinimumCharactersArguments> TooShort");
                Require(local, "LocalizedText<CapacityOverflowArguments> OverCapacity");
                Require(streamEnglish, "\"feedback.settingsButton\": \"Send Feedback\"");
                if (!string.Equals(streamEnglish, resourceEnglish, StringComparison.Ordinal))
                    throw new InvalidOperationException("StreamingAssets and Resources English tables drifted.");

                result = "Honest Feedback uses separated two-column geometry, remains manually reachable after dismissal, centralizes player copy under feedback.* keys, and submits device language metadata.";
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
                throw new InvalidOperationException("Honest Feedback contract missing: " + token);
        }

        private static void Forbid(string source, string token)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Honest Feedback retired shape returned: " + token);
        }
    }
}
#endif
