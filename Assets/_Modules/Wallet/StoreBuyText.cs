// =============================================================================
// StoreBuyText — localized copy for the Store's purchase gate.
// =============================================================================

using DeNelle.Core.UI;

namespace DeNelle.Wallet
{
    /// <summary>
    /// Typed localization catalog for every normal purchase-gate state. English
    /// copy lives only in GameStrings; callers resolve through the global locale.
    /// </summary>
    public static class StoreBuyText
    {
        public const string KeyClosed = "storeBuyClosed";
        public const string KeyRailNotReady = "storeBuyRailNotReady";
        public const string KeyWalletRequired = "storeBuyWalletRequired";
        public const string KeyWalletRequiredCrypto = "storeBuyWalletRequiredCrypto";
        public const string KeyWalletRequiredCta = "storeBuyWalletRequiredCta";
        public const string KeyComingSoon = "storeBuyComingSoon";
        public const string KeyShelfClosed = "storeShelfClosed";

        public static readonly LocalizedText Closed = new LocalizedText(KeyClosed);
        public static readonly LocalizedText RailNotReady = new LocalizedText(KeyRailNotReady);
        public static readonly LocalizedText<WalletThresholdArguments> WalletRequired =
            new LocalizedText<WalletThresholdArguments>(KeyWalletRequired);
        public static readonly LocalizedText WalletRequiredCrypto =
            new LocalizedText(KeyWalletRequiredCrypto);
        public static readonly LocalizedText WalletRequiredCta = new LocalizedText(KeyWalletRequiredCta);
        public static readonly LocalizedText ComingSoon = new LocalizedText(KeyComingSoon);
        public static readonly LocalizedText ShelfClosed = new LocalizedText(KeyShelfClosed);

        public static readonly string[] AllKeys =
        {
            KeyClosed,
            KeyRailNotReady,
            KeyWalletRequired,
            KeyWalletRequiredCrypto,
            KeyWalletRequiredCta,
            KeyComingSoon,
            KeyShelfClosed,
        };

        public static string WalletRequiredFor(string threshold) =>
            WalletRequired.Resolve(new WalletThresholdArguments(threshold));
    }

    public readonly struct WalletThresholdArguments
    {
        public WalletThresholdArguments(string threshold)
        {
            Threshold = threshold ?? string.Empty;
        }

        public string Threshold { get; }
    }
}
