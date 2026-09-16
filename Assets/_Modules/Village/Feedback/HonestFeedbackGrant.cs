// =============================================================================
// HonestFeedbackGrant (WO-1432) - THE ONE GRANT SEAM for the honest-feedback
// thank-you. There is exactly one, and WO-1432 section 3 forbids a second.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Feedback
//
// 1000 wood + 1000 stone + 1000 iron, once per save, paid ONLY after our own
// backend has confirmed in a response that it stored the player's feedback.
//
// -----------------------------------------------------------------------------
// ⛔ THERE IS NO `Stone` BALANCE. STONE **IS** `Resources.Stone`.
// -----------------------------------------------------------------------------
// GameState.cs:59-71 records the retirement: `public int Stone = 20;` lived there
// and was DELETED by WO-1212 because there were TWO Stone balances and the one
// the player sees, spends and is granted into is the legacy Food slot (WO-1163
// reused it for the Stone vocabulary). A grant that writes a field named Stone
// writes into a balance no HUD reads.
//
// ⚠ AND THE WORK ORDER'S OWN TABLE IS IMPRECISE ABOUT THE OTHER TWO. WO-1432
// section 2a says wood -> `Resources.Wood` and iron -> `Resources.Iron`. Those
// members DO NOT EXIST: `ResourceBalance` (NestedTypes.cs:41-49) carries only
// Crystals / Food / Coins. Wood and Iron are TOP-LEVEL GameState scalars
// (GameState.cs:76-79), which is why StartingBudget's own comment says so
// verbatim. Nothing here is broken by that, because this file never touches a
// field directly - it goes through the economy seam, which already knows:
//   EconomyService.GrantInternal writes gsw.State.Wood / gsw.State.Iron and
//   routes Food through GameStateService.AddStone (EconomyService.cs:416-490).
// The nit is recorded rather than silently worked around (CLAUDE.md sec.11B).
//
// -----------------------------------------------------------------------------
// WHY `PurchasedOrPromised` AND NOT `EarnedIncome`
// -----------------------------------------------------------------------------
// TownBankCapacity.cs law 5: "A quantity the player PAID FOR or was PROMISED AN
// EXACT NUMBER OF ... NEVER CLAMPED - an advertised quantity always arrives in
// full." A screen that says 1000 and delivers 340 because a silo was near its cap
// is the exact failure that enum exists to prevent. IsClampable (:296) already
// exempts the kind; this file only has to pass the right one, which it does via
// EconomyService.GrantSpendablePurchased.
//
// CONSEQUENCE, AND IT IS CORRECT: this grant can put the player OVER the storage
// cap. FOUNDATIONAL_RULINGS.md section 7 makes that a legitimate state - "Credit
// the full purchased amount, above the cap", "No overflow wallet, no escrow, no
// held value anywhere" - with ONE obligation attached: the player must be TOLD,
// IN WORDS, that earned income into that resource pauses until they spend back
// under. HonestFeedbackPanel.OverCapLineFor is that sentence, and it never uses
// the word "lost", because nothing is.
//
// -----------------------------------------------------------------------------
// THE ONE-TIME FLAG, AND WHY IT IS SET AFTER THE MONEY, NOT BEFORE
// -----------------------------------------------------------------------------
// HonestFeedbackKeys.GrantClaimedKey rides the existing SeenTutorials map (no
// save-schema bump - the reasoning is in that file's header). It is written
// AFTER GrantSpendablePurchased returns, so a grant that could not resolve the
// economy leaves the flag clear and the player can still be paid later. Setting
// it first would burn the offer on a failure the player never saw.
//
// Instrumentation: FlowTrace tag "HonestFeedback". Permanent (CLAUDE.md sec.12) -
// the second-claim no-op line is asserted by HonestFeedbackClaimOnceRegression,
// so deleting it is a RED build, not a tidy-up.
//
// ASCII only.
// =============================================================================

using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.State;

namespace DeNelle.Village.Feedback
{
    /// <summary>What a call to <see cref="HonestFeedbackGrant.TryApply"/> did.</summary>
    public enum ThankYouGrantOutcome
    {
        /// <summary>The basket landed. The one-time flag is now set.</summary>
        Applied = 0,

        /// <summary>The flag was already set - a traced NO-OP. Nothing moved.
        /// This is the repeat-claim guard doing its job, not an error.</summary>
        AlreadyClaimed = 1,

        /// <summary>No live GameState - nothing could be written, and the flag is NOT
        /// burned, so the player can still be paid on a later attempt.</summary>
        NoGameState = 2,

        /// <summary>No EconomyService in the scene - same fail-open rule as above.</summary>
        NoEconomy = 3,
    }

    /// <summary>
    /// The single seam that pays the WO-1432 thank-you. Both the network success path
    /// (HonestFeedbackService) and both regression oracles call THIS method - there is
    /// no second entry point, by design.
    /// </summary>
    public static class HonestFeedbackGrant
    {
        /// <summary>FlowTrace category for the whole WO-1432 lane.</summary>
        public const string Sys = "HonestFeedback";

        // ── The owner's number, stated verbatim in WO-1432's source quote ─────────
        // Deliberately NOT authored in honest-feedback.json: WO-1432 section 5 requires
        // an oracle that asserts each delta is EXACTLY 1000, and an oracle reading the
        // same JSON the code read would agree with whatever was in it. See
        // HonestFeedbackTuning's header.

        /// <summary>Wood granted. Owner, verbatim: "1000 of wood stone and Iron".</summary>
        public const int GrantWood = 1000;

        /// <summary>STONE. Written to Resources.Stone - read the file header before
        /// "correcting" this to a Stone field; there is not one.</summary>
        public const int GrantStone = 1000;

        /// <summary>Iron granted.</summary>
        public const int GrantIron = 1000;

        /// <summary>The exact phrase the second-claim no-op logs. Asserted verbatim by
        /// HonestFeedbackClaimOnceRegression, so it is a contract, not a message.</summary>
        public const string AlreadyClaimedTrace =
            "TryApply refused: the thank-you was already claimed on this save - NO-OP, nothing granted";

        /// <summary>True once the thank-you has actually been paid on this save.
        /// False when no GameState is live (an unknown state never claims the player was paid).</summary>
        public static bool HasClaimed()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.SeenTutorials == null) return false;
            return state.SeenTutorials.TryGetValue(HonestFeedbackKeys.GrantClaimedKey, out bool claimed) && claimed;
        }

        /// <summary>True once the offer panel has been shown on this save.</summary>
        public static bool HasBeenOffered()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.SeenTutorials == null) return false;
            return state.SeenTutorials.TryGetValue(HonestFeedbackKeys.OfferedKey, out bool shown) && shown;
        }

        /// <summary>Record that the offer panel has been shown. Idempotent (MarkTutorialSeen is).</summary>
        public static void MarkOffered()
        {
            var svc = GameStateService.Instance;
            if (svc == null)
            {
                FlowTrace.Warn(Sys, "MarkOffered called with no GameStateService - the offer will be " +
                                    "re-shown next session because nothing could be persisted.");
                return;
            }
            svc.MarkTutorialSeen(HonestFeedbackKeys.OfferedKey);
            FlowTrace.Step(Sys, "offer recorded as shown (" + HonestFeedbackKeys.OfferedKey + ").");
        }

        /// <summary>
        /// ⭐ THE GRANT. Pays 1000 wood / 1000 stone / 1000 iron as
        /// <see cref="BankGrantKind.PurchasedOrPromised"/> - never clamped - then sets the
        /// one-time flag.
        /// <para><paramref name="applied"/> returns the basket the economy seam says ACTUALLY
        /// landed, not the basket that was requested. A caller showing the player a number MUST
        /// read it (the ECON-SWEEP 2026-08-16 defect-2 rule); this method also compares the two
        /// itself and raises an UNTHROTTLED Warn on any shortfall, because a purchased grant that
        /// under-delivers is the one thing TownBankCapacity law 5 exists to make impossible.</para>
        /// <para>Idempotent: a second call is a traced no-op that moves nothing.</para>
        /// </summary>
        public static ThankYouGrantOutcome TryApply(out ResourceCost applied)
        {
            applied = new ResourceCost(0, 0, 0, 0, 0);

            // ── the repeat-claim guard: one flag, one seam (WO-1432 section 3) ─────
            if (HasClaimed())
            {
                FlowTrace.Step(Sys, AlreadyClaimedTrace + " (key=" + HonestFeedbackKeys.GrantClaimedKey + ").");
                return ThankYouGrantOutcome.AlreadyClaimed;
            }

            var gs = GameStateService.Instance;
            if (gs == null || gs.State == null)
            {
                FlowTrace.Fail(Sys, "TryApply could not resolve a live GameState - the thank-you was NOT " +
                                    "granted and the one-time flag is deliberately left CLEAR, so the player " +
                                    "can still be paid on a later attempt.");
                return ThankYouGrantOutcome.NoGameState;
            }

            var econ = EconomyService.Instance;
            if (econ == null)
            {
                FlowTrace.Fail(Sys, "TryApply could not resolve EconomyService - the thank-you was NOT granted " +
                                    "and the one-time flag is deliberately left CLEAR.");
                return ThankYouGrantOutcome.NoEconomy;
            }

            // Signature order is (wood, food, iron, crystals) - EconomyService.cs:558.
            // food IS stone. PurchasedOrPromised, so the town bank cap does not apply.
            applied = econ.GrantSpendablePurchased(wood: GrantWood, stone: GrantStone, iron: GrantIron);

            // A line that can embarrass us: it prints what LANDED, per axis, against what was
            // promised. If law 5 is ever broken the shortfall is in the log before it is in a
            // support ticket. (INSTRUMENTATION_STANDARD sec.1.4b - assert outcomes, not intent.)
            if (applied.Wood != GrantWood || applied.Stone != GrantStone || applied.Iron != GrantIron)
            {
                FlowTrace.Warn(Sys,
                    $"PROMISED-QUANTITY SHORTFALL: promised W{GrantWood}/S{GrantStone}/I{GrantIron} but the " +
                    $"economy seam applied W{applied.Wood}/S{applied.Stone}/I{applied.Iron}. A " +
                    $"{nameof(BankGrantKind.PurchasedOrPromised)} grant must never clamp " +
                    "(TownBankCapacity law 5) - the screen said a number and the wallet did not get it.");
            }

            // Flag AFTER the money. See the header.
            gs.MarkTutorialSeen(HonestFeedbackKeys.GrantClaimedKey);

            FlowTrace.Step(Sys,
                $"thank-you APPLIED as {nameof(BankGrantKind.PurchasedOrPromised)}: " +
                $"applied W{applied.Wood} Stone{applied.Stone} I{applied.Iron} -> wallet now " +
                $"Wood={gs.State.Wood} Stone={gs.State.Resources.Stone} Iron={gs.State.Iron}; " +
                $"one-time flag set ({HonestFeedbackKeys.GrantClaimedKey}).");

            return ThankYouGrantOutcome.Applied;
        }

        /// <summary>
        /// Units by which <paramref name="r"/> now sits ABOVE its storage ceiling, or 0 when it
        /// does not. Presentation-only: FOUNDATIONAL_RULINGS.md section 7 obliges the screen to
        /// SAY, in words, when a resource is above capacity and the earned faucet has paused.
        /// Reads TownBankCapacity - the one reader - and never re-derives capacity arithmetic.
        /// </summary>
        public static int OverCapUnits(BankResource r)
        {
            if (!TownBankCapacity.IsCapped(r)) return 0;
            int max = TownBankCapacity.MaxOf(r);
            if (max == int.MaxValue) return 0;
            int current = TownBankCapacity.CurrentOf(r);
            return current > max ? current - max : 0;
        }
    }
}
