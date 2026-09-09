// =============================================================================
// StorePresentationText - localized copy for safe Night Market presentation.
// =============================================================================

using DeNelle.Core.UI;

namespace DeNelle.Wallet
{
    /// <summary>
    /// Typed localization catalog for Store presentation rows that make no
    /// provider, pricing, scarcity, or patronage claim.
    /// </summary>
    public static class StorePresentationText
    {
        public const string KeyBandGap = "storeBandGap";
        public const string KeyBandGapSub = "storeBandGapSub";
        public const string KeyBandBasket = "storeBandBasket";
        public const string KeySpotlightEmpty = "storeSpotlightEmpty";
        public const string KeyLedgerHeading = "storeLedgerHeading";
        public const string KeyCardOwned = "storeCardOwned";
        public const string KeyCardGap = "storeCardGap";

        public static readonly LocalizedText BandGap = new LocalizedText(KeyBandGap);
        public static readonly LocalizedText BandGapSub = new LocalizedText(KeyBandGapSub);
        public static readonly LocalizedText BandBasket = new LocalizedText(KeyBandBasket);
        public static readonly LocalizedText SpotlightEmpty = new LocalizedText(KeySpotlightEmpty);
        public static readonly LocalizedText LedgerHeading = new LocalizedText(KeyLedgerHeading);
        public static readonly LocalizedText CardOwned = new LocalizedText(KeyCardOwned);
        public static readonly LocalizedText CardGap = new LocalizedText(KeyCardGap);

        public static readonly string[] AllKeys =
        {
            KeyBandGap,
            KeyBandGapSub,
            KeyBandBasket,
            KeySpotlightEmpty,
            KeyLedgerHeading,
            KeyCardOwned,
            KeyCardGap,
        };
    }
}
