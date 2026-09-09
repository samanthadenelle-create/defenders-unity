namespace DeNelle.Core.UI
{
    /// <summary>Localization keys for combat HUD actions whose state changes their meaning.</summary>
    public static class CombatHudText
    {
        public const string KeyFleeAction = "hud.combat.flee.action";
        public const string KeyFleeConfirm = "hud.combat.flee.confirm";

        public static readonly LocalizedText FleeAction = new LocalizedText(KeyFleeAction);
        public static readonly LocalizedText FleeConfirm = new LocalizedText(KeyFleeConfirm);

        public static string ResolveFlee(bool confirmationArmed) =>
            confirmationArmed ? FleeConfirm.Resolve() : FleeAction.Resolve();
    }
}
