namespace DeNelle.Core.Services
{
    /// <summary>
    /// Release gate for the clan/chat feature. WO-1265's server, moderation, two-wallet,
    /// and operator-readiness acceptance is complete (WO-1848's two-wallet integration
    /// gate is green) — WO-1851 (clan WO-8) flips this to true and opens the player door.
    /// </summary>
    public static class ClanFeatureGate
    {
        public const bool PlayerFacingEnabled = true;
    }
}

