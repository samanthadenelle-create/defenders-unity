using DeNelle.Core.State;
using DeNelle.Core.UI;

namespace DeNelle.Settings
{
    /// <summary>Stable localization keys for player-readable copy owned by Settings.</summary>
    internal static class SettingsText
    {
        public static readonly LocalizedText Title = new LocalizedText("settings.title");

        public static readonly LocalizedText AudioSection = new LocalizedText("settings.section.audio");
        public static readonly LocalizedText MasterVolume = new LocalizedText("settings.audio.master");
        public static readonly LocalizedText MusicVolume = new LocalizedText("settings.audio.music");
        public static readonly LocalizedText SfxVolume = new LocalizedText("settings.audio.sfx");
        public static readonly LocalizedText MuteAll = new LocalizedText("settings.audio.muteAll");
        public static readonly LocalizedText MixerUnavailable = new LocalizedText("settings.audio.mixerUnavailable");

        public static readonly LocalizedText GameplaySection = new LocalizedText("settings.section.gameplay");
        public static readonly LocalizedText DifficultyEasy = new LocalizedText("settings.difficulty.easy");
        public static readonly LocalizedText DifficultyNormal = new LocalizedText("settings.difficulty.normal");
        public static readonly LocalizedText DifficultyHard = new LocalizedText("settings.difficulty.hard");
        public static readonly LocalizedText DifficultyEasyBlurb = new LocalizedText("settings.difficulty.easyBlurb");
        public static readonly LocalizedText DifficultyNormalBlurb = new LocalizedText("settings.difficulty.normalBlurb");
        public static readonly LocalizedText DifficultyHardBlurb = new LocalizedText("settings.difficulty.hardBlurb");

        public static readonly LocalizedText GraphicsSection = new LocalizedText("settings.section.graphics");
        public static readonly LocalizedText QualityLow = new LocalizedText("settings.quality.low");
        public static readonly LocalizedText QualityHigh = new LocalizedText("settings.quality.high");
        public static readonly LocalizedText QualityDesktop = new LocalizedText("settings.quality.desktop");

        public static readonly LocalizedText ComfortSection = new LocalizedText("settings.section.comfort");
        public static readonly LocalizedText ScreenShake = new LocalizedText("settings.comfort.screenShake");
        public static readonly LocalizedText ToggleOn = new LocalizedText("settings.toggle.on");
        public static readonly LocalizedText ToggleOff = new LocalizedText("settings.toggle.off");

        public static readonly LocalizedText LanguageSection = new LocalizedText("settings.section.language");
        public static readonly LocalizedText DeviceLanguage = new LocalizedText("settings.language.systemDefault");
        public static readonly LocalizedText<LanguageArguments> ChooseLanguage =
            new LocalizedText<LanguageArguments>("settings.language.choose");
        public static readonly LocalizedText<LanguageArguments> ChooseBetaLanguage =
            new LocalizedText<LanguageArguments>("settings.language.chooseBeta");

        public static readonly LocalizedText WalletSection = new LocalizedText("settings.section.wallet");
        public static readonly LocalizedText ConnectWallet = new LocalizedText("settings.wallet.connect");
        public static readonly LocalizedText DisconnectWallet = new LocalizedText("settings.wallet.disconnect");
        public static readonly LocalizedText<WalletAddressArguments> DisconnectAddress =
            new LocalizedText<WalletAddressArguments>("settings.wallet.disconnectAddress");

        public static readonly LocalizedText HelpSection = new LocalizedText("settings.section.help");
        public static readonly LocalizedText GameGuide = new LocalizedText("settings.help.gameGuide");
        public static readonly LocalizedText Help = new LocalizedText("settings.help.help");
        public static readonly LocalizedText ResetDefaults = new LocalizedText("settings.help.resetDefaults");

        public static readonly LocalizedText TownSection = new LocalizedText("settings.section.town");
        public static readonly LocalizedText DefenceReports = new LocalizedText("settings.town.defenceReports");
        public static readonly LocalizedText<UnreadReportsArguments> DefenceReportsUnread =
            new LocalizedText<UnreadReportsArguments>("settings.town.defenceReportsUnread");

        public static readonly LocalizedText LegalSection = new LocalizedText("settings.section.legal");
        public static readonly LocalizedText PrivacyPolicy = new LocalizedText("settings.legal.privacyPolicy");
        public static readonly LocalizedText TermsOfService = new LocalizedText("settings.legal.termsOfService");

        public static readonly LocalizedText AdPrivacySection = new LocalizedText("settings.section.adPrivacy");
        public static readonly LocalizedText AdPrivacyChoices = new LocalizedText("settings.adPrivacy.choices");
        public static readonly LocalizedText DoNotSellOn = new LocalizedText("settings.adPrivacy.doNotSellOn");
        public static readonly LocalizedText DoNotSellOff = new LocalizedText("settings.adPrivacy.doNotSellOff");

        public static readonly LocalizedText OfflineSection = new LocalizedText("settings.section.offline");
        public static readonly LocalizedText OfflineReady = new LocalizedText("settings.offline.ready");
        public static readonly LocalizedText PlayOffline = new LocalizedText("settings.offline.play");

        public static readonly LocalizedText DeveloperSection = new LocalizedText("settings.section.developer");
        public static readonly LocalizedText DeveloperPanel = new LocalizedText("settings.developer.panel");

        public static readonly LocalizedText<PercentArguments> Percent =
            new LocalizedText<PercentArguments>("settings.value.percent");

        public static string QualityLabel(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.SeekerLow: return QualityLow.Resolve();
                case QualityTier.Desktop: return QualityDesktop.Resolve();
                default: return QualityHigh.Resolve();
            }
        }

        public static string DifficultyLabel(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return DifficultyEasy.Resolve();
                case Difficulty.Hard: return DifficultyHard.Resolve();
                default: return DifficultyNormal.Resolve();
            }
        }

        public static string DifficultyBlurb(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return DifficultyEasyBlurb.Resolve();
                case Difficulty.Hard: return DifficultyHardBlurb.Resolve();
                default: return DifficultyNormalBlurb.Resolve();
            }
        }
    }

    internal readonly struct WalletAddressArguments
    {
        public WalletAddressArguments(string address) { Address = address ?? string.Empty; }
        public string Address { get; }
    }

    internal readonly struct UnreadReportsArguments
    {
        public UnreadReportsArguments(int unreadCount) { UnreadCount = unreadCount; }
        public int UnreadCount { get; }
    }

    internal readonly struct PercentArguments
    {
        public PercentArguments(int percent) { Percent = percent; }
        public int Percent { get; }
    }

    internal readonly struct LanguageArguments
    {
        public LanguageArguments(string language) { Language = language ?? string.Empty; }
        public string Language { get; }
    }
}
