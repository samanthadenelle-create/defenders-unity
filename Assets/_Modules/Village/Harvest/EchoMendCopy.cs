// =============================================================================
// EchoMendCopy + EchoMendReport -- the PLAYER-FACING half of passive Echo mending
// (WO-1231).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// THE DEFECT THIS EXISTS TO CLOSE (owner felt-test 2026-08-26: "if its passive we
// should somehow let them know"): passive repair was CORRECT and COMPLETELY SILENT.
// A sweep of Assets/_Modules for player-facing strings about Echo mending returned
// ZERO -- every hit was a FlowTrace line, an editor [Tooltip] or a log message. So:
//   * the player was never told structures mend on their own,
//   * never told that OWNING MORE ECHOES MAKES IT FASTER (a monetisation-relevant
//     fact that was a total secret),
//   * never told that mending DEBITS WOOD AND IRON from their wallet, and
//   * never told when it STALLED because the wallet was short.
// The last two are the ones that matter: materials left with no cause shown, and
// repair stopped with no reason shown. Both halves were invisible.
//
// OWNER RULING 2026-08-26 (recorded in WORK_ORDER_1231): THE SPEND STAYS. Mending
// remains a Wood+Iron sink. The defect was invisibility, NOT the economy. Nothing in
// this file changes a rate, a cost or the count x level math -- it only SAYS what the
// existing system already does.
//
// ⛔ THIS IS COMMUNICATION ONLY. Do NOT let a picker chip, an assignment verb or a
// per-Echo repair lane back in on the strength of these strings existing: repair is
// PASSIVE and COUNT-DRIVEN (owner ruling WO-1108; EchoAssignments.cs refuses
// AssignRepair in as many words).
//
// WHY THE COPY LIVES IN ONE STATIC CLASS AND NOT IN THE VIEWS: two surfaces render it
// (the Echo card's PASSIVE MENDING block and the while-you-were-away summary) and a
// headless regression asserts it. Three copies of a sentence is how a "we told the
// player" claim goes stale in one of the three places (the CLAUDE.md duplicated-state
// failure that produced the stale WO block and the retired dependency table).
//
// COLOURBLIND LAW (owner is red/green colourblind): every state below is carried by a
// WORD -- "PAUSED", "waiting for materials", the named resource. The stall chip's
// frame is a SHAPE cue. Nothing here means anything by hue, so a greyscale capture
// reads identically.
//
// ASCII ONLY. A non-ASCII glyph renders as a tofu box on device -- so '-' and not an
// en dash, and no degree/bullet/arrow glyphs anywhere in these strings.
// =============================================================================
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// What passive Echo mending DID over one offline window: how much wall health it
    /// bought, what it SPENT buying it, and whether it stalled broke. A plain value
    /// carrier -- it never reads or writes GameState (the service banks and spends;
    /// this only reports what already happened), which is why the while-you-were-away
    /// summary can render it without touching a service.
    /// </summary>
    public sealed class EchoMendReport
    {
        /// <summary>
        /// The claim this report belongs to (<see cref="OfflineClaimWindow.Sequence"/>), or 0
        /// for a report no claim produced. The away summary CHECKS it: if Echo repair did not
        /// apply this claim's window (no GameState, or the service is not registered), the
        /// last report is an OLDER window's and re-printing its spend would be a worse lie
        /// than the silence this ticket removed.
        /// </summary>
        public int ClaimSequence;

        /// <summary>Number of structures brought back to full over the window.</summary>
        public int Repairs;

        /// <summary>Total structure-damage FRACTION mended (1.0 == one wall from
        /// destroyed-threshold to full). Rendered as a percentage, never as a raw fraction.</summary>
        public float HealthFraction;

        /// <summary>Wood debited by mending over the window.</summary>
        public int SpentWood;

        /// <summary>Iron debited by mending over the window.</summary>
        public int SpentIron;

        /// <summary>Stone debited by mending over the window (the CoreCost 'food' slot;
        /// WO-1163 renamed the axis, the field name is a live save key and did not move).</summary>
        public int SpentStone;

        /// <summary>Crystals debited by mending over the window. Normally 0 -- structure
        /// repair prices in wood/iron -- but PROD-014 is explicit that a crystals slot the
        /// UI calls "nothing" while charging it is worse than a wrong price, so it is carried.</summary>
        public int SpentCrystals;

        /// <summary>Player-facing name of the resource mending ran OUT of, or "" when it
        /// never stalled. A WORD, so the stall reads identically in greyscale.</summary>
        public string StalledResource = "";

        /// <summary>True when mending stopped over this window because the wallet was short.</summary>
        public bool Stalled => !string.IsNullOrEmpty(StalledResource);

        /// <summary>Total units debited across every slot.</summary>
        public int SpentTotal => SpentWood + SpentIron + SpentStone + SpentCrystals;

        /// <summary>
        /// True when this report has something the player needs to be told. NOTE the stall
        /// counts: "your walls did not mend because you are out of Wood" is actionable and
        /// is exactly the state that used to live only in a FlowTrace.
        /// </summary>
        public bool HasContent => Repairs > 0 || HealthFraction > 0f || SpentTotal > 0 || Stalled;

        /// <summary>An empty report -- mending did nothing worth saying.</summary>
        public static EchoMendReport None => new EchoMendReport();

        /// <summary>Fold one completed repair's spend into this report.</summary>
        public void AddSpend(DeNelle.Core.Catalog.ResourceCost cost)
        {
            SpentWood += Mathf.Max(0, cost.wood);
            SpentIron += Mathf.Max(0, cost.iron);
            SpentStone += Mathf.Max(0, cost.stone);
            SpentCrystals += Mathf.Max(0, cost.crystals);
        }
    }

    /// <summary>
    /// The ONE home for every player-facing sentence about passive Echo mending.
    /// Both surfaces (Echo card block, while-you-were-away summary) and the headless
    /// regression read these -- never a literal at a call site. ASCII only.
    /// </summary>
    public static class EchoMendCopy
    {
        // -- Echo card: the PASSIVE MENDING block ------------------------------

        /// <summary>Block heading on the Echo card.</summary>
        public const string Header = "PASSIVE MENDING";

        /// <summary>
        /// The explainer. States the effect in the PLAYER'S terms -- what it does and that
        /// more Echoes makes it faster -- and deliberately never prints the internal
        /// fraction knob (repairFractionPerHour) or the words "assignment"/"lane", because
        /// there is nothing to assign and implying otherwise sends the player hunting for
        /// a picker that WO-1108 retired.
        /// </summary>
        public const string Explainer =
            "Your Echoes mend the town's walls on their own. Every Echo you wake mends " +
            "faster - no assignment needed.";

        /// <summary>The spend disclosure. This is the sentence whose absence made materials
        /// look like they were vanishing.</summary>
        public const string SpendNote = "Mending uses Wood and Iron as it works.";

        /// <summary>What the rate line says when nobody is awake to mend (the honest zero --
        /// never a "0%" that reads as a broken system).</summary>
        public const string RateNone = "Mend rate now: none - wake an Echo to begin.";

        /// <summary>Prefix every rate line shares, so the regression can pin it without
        /// pinning a live number.</summary>
        public const string RatePrefix = "Mend rate now:";

        /// <summary>Prefix the stall chip shares in BOTH surfaces -- the actionable word.</summary>
        public const string StallPrefix = "PAUSED - waiting for materials";

        /// <summary>
        /// The live rate line, bound to <see cref="EchoBonusCalculator.RepairFractionsPerSecond"/>
        /// -- NEVER hardcoded, so a balance edit moves the sentence with it.
        /// </summary>
        public static string RateLine(float fractionsPerSecond)
        {
            if (fractionsPerSecond <= 0f) return RateNone;
            // fractions/sec -> fractions/hour -> PERCENT of wall health per hour. The x100 is
            // load-bearing: 0.35 fractions/h is 35%, and dropping it renders "+0.4%" for a
            // roster that is in fact mending a third of a wall an hour.
            return RatePrefix + " +" + Percent(fractionsPerSecond * 3600f * 100f) + "% wall health / hour";
        }

        /// <summary>
        /// The stall chip. Word + named resource, so it is legible in greyscale and
        /// ACTIONABLE ("go get Wood") rather than merely red.
        /// </summary>
        public static string StallChip(string resourceLabel)
        {
            return string.IsNullOrEmpty(resourceLabel)
                ? StallPrefix
                : StallPrefix + " (" + resourceLabel + ")";
        }

        // -- While-you-were-away summary --------------------------------------
        //  THE SPEND-ATTRIBUTION HOME. Owner-approved option: the offline-return summary,
        //  which is the moment the player is ALREADY reading a "here is what happened"
        //  report. ⛔ Explicitly NOT a toast per repair -- that would spam, and WO-1231
        //  rules it out by name.

        /// <summary>Prefix of the mended row -- pinned by the regression.</summary>
        public const string AwayMendedPrefix = "Echoes mended the walls";

        /// <summary>Prefix of the spend row -- pinned by the regression. This row is the
        /// answer to "where did my Wood go".</summary>
        public const string AwaySpentPrefix = "spent while mending";

        /// <summary>Prefix of the away stall row.</summary>
        public const string AwayStallPrefix = "Mending paused - ran out of";

        /// <summary>"Echoes mended the walls   +12% wall health".</summary>
        public static string AwayMendedLine(EchoMendReport r)
        {
            if (r == null) return "";
            return AwayMendedPrefix + "   +" + Percent(r.HealthFraction * 100f) + "% wall health";
        }

        /// <summary>"spent while mending   -120 Wood, -40 Iron" ("" when nothing was spent).</summary>
        public static string AwaySpentLine(EchoMendReport r)
        {
            if (r == null || r.SpentTotal <= 0) return "";
            var sb = new StringBuilder(AwaySpentPrefix).Append("   ");
            bool first = true;
            first = AppendSpend(sb, first, r.SpentWood, "Wood");
            first = AppendSpend(sb, first, r.SpentIron, "Iron");
            first = AppendSpend(sb, first, r.SpentStone, "Stone");
            AppendSpend(sb, first, r.SpentCrystals, "Crystals");
            return sb.ToString();
        }

        /// <summary>"Mending paused - ran out of Wood" ("" when it never stalled).</summary>
        public static string AwayStallLine(EchoMendReport r)
        {
            if (r == null || !r.Stalled) return "";
            return AwayStallPrefix + " " + r.StalledResource;
        }

        // -- helpers ----------------------------------------------------------

        private static bool AppendSpend(StringBuilder sb, bool first, int amount, string label)
        {
            if (amount <= 0) return first;
            if (!first) sb.Append(", ");
            sb.Append('-').Append(amount.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(label);
            return false;
        }

        /// <summary>
        /// Percent text with ONE decimal below 10 and none above. A slow roster mends a
        /// couple of percent an hour; rounding that to a flat "0%" would print a lie about
        /// a system that is in fact working, which is the exact failure mode this ticket
        /// exists to remove.
        /// </summary>
        public static string Percent(float percentValue)
        {
            float p = Mathf.Max(0f, percentValue);
            if (p >= 10f) return Mathf.RoundToInt(p).ToString(CultureInfo.InvariantCulture);
            if (p > 0f && p < 0.1f) return "<0.1";
            return p.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
