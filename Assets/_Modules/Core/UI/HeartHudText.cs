namespace DeNelle.Core.UI
{
    /// <summary>Localization authority for the Heart objective and Heartfire HUD cluster.</summary>
    public static class HeartHudText
    {
        public const string KeyTitle = "hud.heart.title";
        public const string KeyDefend = "hud.heart.objective.defend";
        public const string KeyPrepareWave = "hud.heart.objective.prepareWave";
        public const string KeyBuildBarracks = "hud.heart.objective.buildBarracks";
        public const string KeyTrainOne = "hud.heart.objective.trainOne";
        public const string KeyTrainOther = "hud.heart.objective.trainOther";
        // WO-1641: the SAME shortfall, AFTER the first raid. Raids are already unlocked by then,
        // so the pair above would claim a lock the player has already opened; these say what the
        // shortfall actually means - the full-cap party the door asks for from that point on.
        public const string KeyTrainNextRaidOne = "hud.heart.objective.trainNextRaidOne";
        public const string KeyTrainNextRaidOther = "hud.heart.objective.trainNextRaidOther";
        public const string KeyHeartfirePlate = "hud.heart.heartfire.plate";
        public const string KeyHeartfireNextMinutes = "hud.heart.heartfire.nextMinutes";
        public const string KeyHeartfireNextHoursMinutes = "hud.heart.heartfire.nextHoursMinutes";
        public const string KeyHeartfireCombined = "hud.heart.heartfire.combined";

        public static readonly LocalizedText Title = new LocalizedText(KeyTitle);
        public static readonly LocalizedText Defend = new LocalizedText(KeyDefend);
        public static readonly LocalizedText PrepareWave = new LocalizedText(KeyPrepareWave);
        public static readonly LocalizedText BuildBarracks = new LocalizedText(KeyBuildBarracks);
        public static readonly LocalizedText<HeartTroopsArguments> TrainOne =
            new LocalizedText<HeartTroopsArguments>(KeyTrainOne);
        public static readonly LocalizedText<HeartTroopsArguments> TrainOther =
            new LocalizedText<HeartTroopsArguments>(KeyTrainOther);
        public static readonly LocalizedText<HeartTroopsArguments> TrainNextRaidOne =
            new LocalizedText<HeartTroopsArguments>(KeyTrainNextRaidOne);
        public static readonly LocalizedText<HeartTroopsArguments> TrainNextRaidOther =
            new LocalizedText<HeartTroopsArguments>(KeyTrainNextRaidOther);
        public static readonly LocalizedText<HeartfireCountArguments> HeartfirePlate =
            new LocalizedText<HeartfireCountArguments>(KeyHeartfirePlate);
        public static readonly LocalizedText<HeartfireMinutesArguments> HeartfireNextMinutes =
            new LocalizedText<HeartfireMinutesArguments>(KeyHeartfireNextMinutes);
        public static readonly LocalizedText<HeartfireHoursMinutesArguments> HeartfireNextHoursMinutes =
            new LocalizedText<HeartfireHoursMinutesArguments>(KeyHeartfireNextHoursMinutes);
        public static readonly LocalizedText<HeartfireCombinedArguments> HeartfireCombined =
            new LocalizedText<HeartfireCombinedArguments>(KeyHeartfireCombined);
    }

    public readonly struct HeartTroopsArguments
    {
        public HeartTroopsArguments(int troops) { Troops = troops; }
        public int Troops { get; }
    }

    public readonly struct HeartfireCountArguments
    {
        public HeartfireCountArguments(int charges, int maxCharges)
        {
            Charges = charges;
            MaxCharges = maxCharges;
        }
        public int Charges { get; }
        public int MaxCharges { get; }
    }

    public readonly struct HeartfireMinutesArguments
    {
        public HeartfireMinutesArguments(long minutes) { Minutes = minutes; }
        public long Minutes { get; }
    }

    public readonly struct HeartfireHoursMinutesArguments
    {
        public HeartfireHoursMinutesArguments(long hours, long minutes)
        {
            Hours = hours;
            Minutes = minutes;
        }
        public long Hours { get; }
        public long Minutes { get; }
    }

    public readonly struct HeartfireCombinedArguments
    {
        public HeartfireCombinedArguments(string plate, string rekindle)
        {
            Plate = plate ?? string.Empty;
            Rekindle = rekindle ?? string.Empty;
        }
        public string Plate { get; }
        public string Rekindle { get; }
    }
}
