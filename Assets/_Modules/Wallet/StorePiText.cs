// =============================================================================
// StorePiText - localized copy for the Pi Store skin.
// =============================================================================

using DeNelle.Core.UI;

namespace DeNelle.Wallet
{
    /// <summary>Typed localization catalog for every Pi-specific Store state.</summary>
    public static class StorePiText
    {
        public const string KeyHeaderNotice = "storePiHeaderNotice";
        public const string KeyPriceAtCheckout = "storePiPriceAtCheckout";
        public const string KeyNotOnSale = "storePiNotOnSale";
        public const string KeyRailUnavailable = "storePiRailUnavailable";
        public const string KeyWalletGate = "storePiWalletGate";
        public const string KeyShelfEmpty = "storePiShelfEmpty";

        public static readonly LocalizedText HeaderNotice = new LocalizedText(KeyHeaderNotice);
        public static readonly LocalizedText PriceAtCheckout = new LocalizedText(KeyPriceAtCheckout);
        public static readonly LocalizedText NotOnSale = new LocalizedText(KeyNotOnSale);
        public static readonly LocalizedText RailUnavailable = new LocalizedText(KeyRailUnavailable);
        public static readonly LocalizedText WalletGate = new LocalizedText(KeyWalletGate);
        public static readonly LocalizedText ShelfEmpty = new LocalizedText(KeyShelfEmpty);

        public static readonly string[] AllKeys =
        {
            KeyHeaderNotice,
            KeyPriceAtCheckout,
            KeyNotOnSale,
            KeyRailUnavailable,
            KeyWalletGate,
            KeyShelfEmpty,
        };
    }
}
