using DeNelle.Core.UI;

namespace DeNelle.Village.Crafting
{
    /// <summary>Localization keys for every player-facing string in the Jeweler return FTUE.</summary>
    public static class JewelerDiscoveryText
    {
        public const string KeyTitle = "jewelerFtue.title";
        public const string KeyBody = "jewelerFtue.body";
        public const string KeyOpenJeweler = "jewelerFtue.openJeweler";
        public const string KeyGuidance = "jewelerFtue.guidance";
        public const string KeyStakeChecking = "jewelerFtue.stakeChecking";
        public const string KeyStakeVerified = "jewelerFtue.stakeVerified";
        public const string KeyStakeVerifiedHighTier = "jewelerFtue.stakeVerifiedHighTier";
        public const string KeyStakeNotVerified = "jewelerFtue.stakeNotVerified";
        public const string KeyPolishTitle = "jewelerPolish.title";
        public const string KeyPolishBody = "jewelerPolish.body";
        public const string KeyBeginPolish = "jewelerPolish.begin";
        public const string KeyPolishStarted = "jewelerPolish.started";
        public const string KeyPolishFailed = "jewelerPolish.failed";
        public const string KeyNoStone = "jewelerPolish.noStone";
        public const string KeyRevealTitle = "jewelerPolish.revealTitle";
        public const string KeyRevealBody = "jewelerPolish.revealBody";
        public const string KeyKeepGem = "jewelerPolish.keep";

        public static readonly LocalizedText Title = new LocalizedText(KeyTitle);
        public static readonly LocalizedText Body = new LocalizedText(KeyBody);
        public static readonly LocalizedText OpenJeweler = new LocalizedText(KeyOpenJeweler);
        public static readonly LocalizedText Guidance = new LocalizedText(KeyGuidance);
        public static readonly LocalizedText StakeChecking = new LocalizedText(KeyStakeChecking);
        public static readonly LocalizedText StakeVerified = new LocalizedText(KeyStakeVerified);
        public static readonly LocalizedText StakeVerifiedHighTier = new LocalizedText(KeyStakeVerifiedHighTier);
        public static readonly LocalizedText StakeNotVerified = new LocalizedText(KeyStakeNotVerified);
        public static readonly LocalizedText PolishTitle = new LocalizedText(KeyPolishTitle);
        public static readonly LocalizedText<DurationArguments> PolishBody =
            new LocalizedText<DurationArguments>(KeyPolishBody);
        public static readonly LocalizedText<DurationArguments> BeginPolish =
            new LocalizedText<DurationArguments>(KeyBeginPolish);
        public static readonly LocalizedText PolishStarted = new LocalizedText(KeyPolishStarted);
        public static readonly LocalizedText PolishFailed = new LocalizedText(KeyPolishFailed);
        public static readonly LocalizedText NoStone = new LocalizedText(KeyNoStone);
        public static readonly LocalizedText RevealTitle = new LocalizedText(KeyRevealTitle);
        public static readonly LocalizedText<GemArguments> RevealBody =
            new LocalizedText<GemArguments>(KeyRevealBody);
        public static readonly LocalizedText KeepGem = new LocalizedText(KeyKeepGem);
    }

    public readonly struct DurationArguments
    {
        public DurationArguments(string duration) { Duration = duration ?? string.Empty; }
        public string Duration { get; }
    }

    public readonly struct GemArguments
    {
        public GemArguments(string gem) { Gem = gem ?? string.Empty; }
        public string Gem { get; }
    }
}
