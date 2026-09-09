// =============================================================================
// ChestInteractionText — localized copy for the dungeon loot-chest interaction.
// =============================================================================

using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// Stable localization keys for the loot chest's open and combat-blocked states.
    /// The blocked sentence is shared by the interaction prompt and refusal toast so
    /// the player receives one consistent explanation.
    /// </summary>
    public static class ChestInteractionText
    {
        public const string KeyOpen = "interaction.chest.open";
        public const string KeyBlockedByEnemies = "interaction.chest.blockedByEnemies";

        public static readonly LocalizedText Open = new LocalizedText(KeyOpen);
        public static readonly LocalizedText BlockedByEnemies = new LocalizedText(KeyBlockedByEnemies);
    }
}
