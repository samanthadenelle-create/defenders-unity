// =============================================================================
// DefenseReportBuilder — the ADAPTER between the live town and the persisted report.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village   (WO-1026)
//
// Everything that translates LIVE Village objects into Core.Defense DATA lives here,
// and only here:
//   WaveDamageReport.Entry[]  -> StructureOutcome[]
//   GameState.BaseLayout      -> DefenderSnapshot + LayoutHash
//   a wave ordinal            -> AttackerIdentity        (model (a))
//   the town bank standing     -> StakesLedger            (THE RULED LOSS, BuildStakes/ApplyStakes)
//
// ⛔ THIS IS THE ONLY FILE THAT WRITES AttackerSource.GeneratedPve.
//    That is the "do not hardcode that the attacker is generated" ruling made concrete:
//    every READER branches on the record's Source field (or on nothing), so the day a
//    ghost-snapshot producer exists it writes GhostSnapshot here and NOTHING downstream
//    changes. SiegeSpawnAuthorityRegression fails the gate if a second file writes it.
//
// NOTHING IN HERE RE-AGGREGATES DAMAGE. WaveDamageReport.Collect() already enumerates
// every damaged/destroyed player structure worst-first and priced, Guard-wrapped, capped
// and trace-truncated. We serialise its output. Re-scanning would be a second aggregator
// that drifts from the one the wave-clear banner shows — the player would then read two
// different accounts of the same attack.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Defense;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>Live-town → persisted-report adapters. Pure translation; no scanning of its own.</summary>
    public static class DefenseReportBuilder
    {
        // =====================================================================
        //  Rows — a 1:1 adapt of the EXISTING aggregate
        // =====================================================================

        /// <summary>
        /// Adapts <see cref="WaveDamageReport.Entry"/> rows into <see cref="StructureOutcome"/>s,
        /// appending into <paramref name="into"/>. Guard.TryEach per row, so one malformed entry is
        /// skipped and logged rather than losing the whole report.
        /// </summary>
        public static void AdaptRows(List<WaveDamageReport.Entry> entries, List<StructureOutcome> into)
        {
            if (into == null) return;
            if (entries == null || entries.Count == 0) return;

            var built = Guard.TryEach("Siege", "adapt damage entry", entries, e =>
            {
                if (e == null) return;
                into.Add(new StructureOutcome
                {
                    DisplayName = string.IsNullOrEmpty(e.Name) ? "Structure" : e.Name,
                    DamageFraction = Mathf.Clamp01(e.DamageFraction),
                    // ONE source of truth for the row's condition — StructureOutcome.Destroyed
                    // is derived from this, never stored beside it, so they cannot disagree.
                    // (StructureState.Held is unreachable here by design: WaveDamageReport only
                    // emits rows for structures that were actually damaged. See that enum's doc.)
                    State = e.Destroyed ? StructureState.Destroyed : StructureState.Damaged,
                    IsCollector = e.IsCollector,
                    LootStolen = Mathf.Max(0, e.LootStolen),
                    // Cost is OMITTED, never faked — HasCost carries that, exactly as the
                    // WaveDamageReport contract already does for the live banner.
                    HasCost = e.HasCost,
                    RepairWood = e.HasCost ? Mathf.Max(0, e.RepairCost.wood) : 0,
                    RepairIron = e.HasCost ? Mathf.Max(0, e.RepairCost.iron) : 0,
                    RepairStone = e.HasCost ? Mathf.Max(0, e.RepairCost.stone) : 0,
                    RepairCrystals = e.HasCost ? Mathf.Max(0, e.RepairCost.crystals) : 0,
                });
            });

            if (built.failed > 0)
                FlowTrace.Warn("Siege", $"adapt losses: {built.failed} of {entries.Count} rows skipped (see Guard lines).");
        }

        // =====================================================================
        //  ⭐ LEGIBILITY — the merge that turns a loss LIST into a DIAGNOSIS
        // =====================================================================

        /// <summary>
        /// Stamps position, defence BAND and HOLD TIME onto rows that already exist.
        ///
        /// <para>The owner's bar for this feature is felt, not structural:
        /// <i>"Does losing feel like it was my fault, and do I know what to change?"</i>
        /// A row that says "Wall B destroyed" fails that bar. The same row saying
        /// "your east wall fell in 4s, front line" passes it, because the player can act on it.
        /// That is the entire job of this method.</para>
        ///
        /// <para>⛔ MERGE ONLY. It adds no row, drops no row and re-prices nothing —
        /// <c>WaveDamageReport</c> remains the single authority on WHAT was damaged. Rows the
        /// watch never saw keep <c>HoldTimeSeconds = -1</c> (UNKNOWN), which the panel renders as an
        /// honest blank. It never renders as "fell in 0s": a fabricated hold time would send the
        /// player to move the wrong structure, which is worse than telling them nothing.</para>
        /// </summary>
        public static void StampLegibility(DefenseOutcomeRecord record, StructureVitalsWatch vitals)
        {
            if (record == null) return;

            Guard.Try("Siege", "stamp report legibility", () =>
            {
                float assault = Mathf.Max(0f, record.DurationSeconds);
                float coreR = record.Defender.CoreRadius;
                float frontR = record.Defender.FrontRadius;

                int timed = 0, unknown = 0;
                for (int i = 0; i < record.Rows.Count; i++)
                {
                    var l = record.Rows[i];
                    if (l == null) continue;

                    l.HoldTimeSeconds = -1f;
                    l.FirstHitAtSeconds = -1f;
                    l.FellAtSeconds = -1f;

                    if (vitals == null) { unknown++; l.Band = DefenseBand.Second; continue; }

                    var t = vitals.Resolve(l.DisplayName, assault);
                    if (!t.Found) { unknown++; l.Band = DefenseBand.Second; continue; }

                    l.WorldX = t.Position.x;
                    l.WorldZ = t.Position.z;
                    l.DistanceFromCore = t.DistanceFromCore;
                    l.Band = ClassifyBand(t.DistanceFromCore, coreR, frontR);
                    l.WasAlreadyDamaged = t.WasAlreadyDamaged;
                    l.FirstHitAtSeconds = t.FirstHitAtSeconds;
                    l.FellAtSeconds = t.FellAtSeconds;
                    l.HoldTimeSeconds = t.HoldTimeSeconds;

                    l.StructureId = t.StructureId;
                    l.StructureType = t.StructureType;
                    l.BreachOrdinal = ResolveBreachOrdinal(record, t.StructureId);

                    if (l.HasHoldTime) timed++; else unknown++;
                }

                // FROZEN here, after every input above is settled. Never recomputed on read.
                record.DefenseScore = ComputeDefenseScore(record);

                FlowTrace.Step("Siege",
                    $"legibility: {timed} row(s) carry a hold time, {unknown} unknown; " +
                    $"bands coreR={coreR:F1}m frontR={frontR:F1}m; path={record.Path.Count} samples; " +
                    $"first breach={(FirstBreach(record) != null ? FirstBreach(record).DisplayName : "none")}.");
            });
        }

        /// <summary>
        /// Which band a distance falls in. Both radii come from the record, so an old report
        /// keeps the classification it was WRITTEN with — recomputing on read would let a
        /// rebuilt town silently rewrite history.
        /// <para>A base with no walls has <c>frontRadius = 0</c>; rather than banding every
        /// structure Front (which would be meaningless), the Front band collapses and rows read
        /// Second/Core. The report then honestly shows a base with no front line.</para>
        /// </summary>
        /// <summary>
        /// Which breach (1-based) happened AT this structure, or 0 if they never crossed here.
        /// Correlated by the scene-instance key both sides already carry.
        /// <para>This separates "the wall they came through" from "a wall that took splash
        /// damage" — identical rows in a flat list, opposite instructions to the player.</para>
        /// </summary>
        public static int ResolveBreachOrdinal(DefenseOutcomeRecord record, string structureId)
        {
            if (record == null || string.IsNullOrEmpty(structureId)) return 0;
            for (int i = 0; i < record.Breaches.Count; i++)
            {
                var b = record.Breaches[i];
                if (b != null && b.BreachedId == structureId) return i + 1;   // breaches are time-ordered
            }
            return 0;
        }

        // =====================================================================
        //  DefenseScore — a LABEL, derived, and honest enough to decline
        // =====================================================================

        // ⚠ PRESENTATION WEIGHTING, NOT GAME BALANCE. Nothing gameplay-facing reads the score:
        // no reward, no matchmaking, no stake, no gate. It exists to put one number on a screen.
        // Keeping it inert is what stops a display weighting quietly becoming an economy rule --
        // the failure the WO-947 / stockpile-cap rulings were expensive to settle.
        private const int ScoreHeld = 100;
        private const int ScoreBreached = 75;
        private const int ScoreOverrun = 40;
        private const int BreachPenaltyEach = 5;
        private const int BreachPenaltyCap = 20;
        private const int DestructionPenaltyMax = 35;

        /// <summary>
        /// 0-100, or <see cref="DefenseOutcomeRecord.NotScored"/> (-1) when the inputs are too
        /// thin to say anything honest.
        ///
        /// <para><b>Derivation:</b>
        /// start from the OUTCOME (Held 100 / Breached 75 / Overrun 40);
        /// subtract 5 per recorded breach, capped at 20;
        /// subtract up to 35 scaled by the FRACTION of the base destroyed
        /// (destroyed rows / StructureCount); clamp 0..100.</para>
        ///
        /// <para><b>⛔ WHEN IT DECLINES:</b> when <c>Defender.StructureCount &lt;= 0</c> — i.e.
        /// there is no census of the base that was defended. Without it the destruction term is
        /// undefined, and the remaining number would be the outcome enum wearing three inputs'
        /// clothes: a confident-looking 75 that actually means "it was breached, and we know
        /// nothing else". Same discipline as an unmeasured hold time printing nothing. The panel
        /// then shows no score rather than a fake one.</para>
        /// </summary>
        public static int ComputeDefenseScore(DefenseOutcomeRecord record)
        {
            if (record == null) return DefenseOutcomeRecord.NotScored;

            int census = record.Defender != null ? record.Defender.StructureCount : 0;
            if (census <= 0)
            {
                FlowTrace.Step("Siege",
                    "defense score DECLINED -- no structure census on the defender snapshot, so the " +
                    "destroyed-fraction term is undefined. Showing no score beats showing a confident one.");
                return DefenseOutcomeRecord.NotScored;
            }

            int score;
            switch (record.Outcome)
            {
                case DefenseOutcome.Overrun: score = ScoreOverrun; break;
                case DefenseOutcome.Breached: score = ScoreBreached; break;
                default: score = ScoreHeld; break;
            }

            score -= Mathf.Min(BreachPenaltyCap, BreachPenaltyEach * record.Breaches.Count);

            int destroyed = 0;
            for (int i = 0; i < record.Rows.Count; i++)
                if (record.Rows[i] != null && record.Rows[i].Destroyed) destroyed++;
            score -= Mathf.RoundToInt(DestructionPenaltyMax * Mathf.Clamp01((float)destroyed / census));

            return Mathf.Clamp(score, 0, 100);
        }

        /// <summary>
        /// The score as a WORD. The number alone is a grade; the word is what makes it readable
        /// at a glance and keeps the plate/list honest under greyscale (nothing here is a colour).
        /// Returns empty when the score declined, so the panel prints nothing.
        /// </summary>
        public static string DefenseScoreWord(int score)
        {
            if (score < 0) return string.Empty;
            if (score >= 90) return "Clean hold";
            if (score >= 70) return "Solid";
            if (score >= 45) return "Shaky";
            return "Bad";
        }

        public static DefenseBand ClassifyBand(float distance, float coreRadius, float frontRadius)
        {
            if (coreRadius > 0f && distance <= coreRadius) return DefenseBand.Core;
            if (frontRadius > 0f && distance >= frontRadius) return DefenseBand.Front;
            return DefenseBand.Second;
        }

        /// <summary>
        /// THE FIRST place they got through, or null if nothing crossed. Breaches are appended
        /// in time order by the observer, so this is Breaches[0] — but it is a NAMED helper
        /// because "the first breach" is the report's headline and every reader must agree on
        /// what it is rather than each indexing the list by hand.
        /// </summary>
        public static BreachRecord FirstBreach(DefenseOutcomeRecord record)
        {
            if (record == null || record.Breaches == null || record.Breaches.Count == 0) return null;
            return record.Breaches[0];
        }

        // =====================================================================
        //  Defender snapshot — what the base looked like at attack time
        // =====================================================================

        /// <summary>
        /// Captures the base as it stands RIGHT NOW. Called before the first spawn: this is the
        /// layout the player is about to be judged on.
        ///
        /// StructureCount + LayoutHash come from the PERSISTED BaseLayout (that is what a model-(c)
        /// snapshot would also be built from, so the hash means the same thing on both sides).
        /// WallCount/TowerCount come from the LIVE SCENE components, deliberately: there is no
        /// structure-ROLE field in the catalog yet (see memory `structure-role-enum-and-format-
        /// normalization` — it is queued, not landed), and guessing a role from an itemId substring
        /// would silently mis-count the day someone renames an id. When the role enum lands, move
        /// these two counts onto it and delete the scene scan.
        /// </summary>
        public static DefenderSnapshot CaptureDefender()
        {
            var snap = new DefenderSnapshot
            {
                LayoutHash = LayoutFingerprint.Empty,
                Garrison = new List<AttackerUnitRecord>(),   // EMPTY today — the WO-430-F seam
            };

            Guard.Try("Siege", "capture defender snapshot", () =>
            {
                var svc = GameStateService.Instance;
                var state = svc != null ? svc.State : null;
                var layout = state != null ? state.BaseLayout : null;

                snap.StructureCount = layout != null ? layout.Count : 0;
                snap.LayoutHash = ComputeLayoutHash(layout);

                snap.WallCount = Object.FindObjectsByType<WallSegment>(FindObjectsSortMode.None).Length;
                snap.TowerCount = Object.FindObjectsByType<Tower>(FindObjectsSortMode.None).Length;
                snap.HeroPresent = Object.FindFirstObjectByType<HeroLocomotion>() != null;
            });

            return snap;
        }

        /// <summary>
        /// The layout fingerprint: one normalised token per placed structure, hashed
        /// order-independently by <see cref="LayoutFingerprint"/>.
        ///
        /// The token carries itemId + cell + yaw + level, so MOVING a structure changes the hash
        /// (which is what makes "a redesign has a visible effect" a data assertion) while merely
        /// re-ordering the save list does not. Public + static so the contract oracle can prove
        /// both halves with no scene.
        /// </summary>
        public static string ComputeLayoutHash(IReadOnlyList<PlacedStructureData> layout)
        {
            if (layout == null || layout.Count == 0) return LayoutFingerprint.Empty;
            var tokens = new List<string>(layout.Count);
            for (int i = 0; i < layout.Count; i++)
            {
                var p = layout[i];
                if (string.IsNullOrEmpty(p.itemId)) continue;
                tokens.Add($"{p.itemId}@{p.cellX},{p.cellZ}:{p.yawSteps}:{p.level}");
            }
            return LayoutFingerprint.Compute(tokens);
        }

        // =====================================================================
        //  Attacker identity — model (a). THE ONLY WRITER OF GeneratedPve.
        // =====================================================================

        /// <summary>Tier bands for the PvE warband's name + id. Cosmetic only — nothing branches
        /// on them, and the panel renders the STRING, never re-derives a name from the source.</summary>
        private static readonly string[] TierNames = { "Hollow Warband", "Hollow Host", "Hollow Legion" };

        /// <summary>
        /// Builds the (a)-model attacker for a given wave ordinal. Units + PowerRating are filled
        /// in at <see cref="SiegeSession.Close"/> from what was ACTUALLY fielded (WO-1113 drips a
        /// roster in slices, so it cannot be known up front).
        /// <para>SnapshotId is EMPTY under (a) and must stay empty — a non-empty snapshot id on a
        /// GeneratedPve record would be a contradiction, and the contract oracle says so.</para>
        /// </summary>
        public static AttackerIdentity BuildPveAttacker(int waveId)
        {
            int tier = waveId >= 20 ? 2 : (waveId >= 10 ? 1 : 0);
            return new AttackerIdentity
            {
                Source = AttackerSource.GeneratedPve,   // ⛔ THE ONLY WRITE OF THIS VALUE IN THE REPO
                AttackerProfileId = $"pve.warband.t{tier + 1}",
                DisplayName = TierNames[tier],
                PowerRating = 0,            // settled at Close from the fielded roster
                SnapshotId = string.Empty,  // EMPTY under (a) — the model-(c) seam, unused today
                Units = new List<AttackerUnitRecord>(),
            };
        }

        // =====================================================================
        //  *** THE SEAM -- the loss consequence (WO-1026, owner ruling 2026-08-27) ***
        // =====================================================================
        //
        //  THE RULING: BANK THEFT REPLACES COLLECTOR LOOTING. A SIEGE BILLS ONCE PER ATTACK.
        //      A siege takes exactly three things: structural damage, a repair bill, and theft of
        //      a PERCENTAGE of UNPROTECTED bank resources under a PROTECTED FLOOR and a
        //      PER-ATTACK CAP.
        //          LOOTABLE      Wood, Iron, Stone (the balance NAMED Food), Coins
        //          UNTOUCHABLE   Crystals, SKR, purchased goods, equipped gear
        //
        //  ! ONE BILL, BY CONSTRUCTION -- NOT BY CARE. Collector looting was REMOVED in the same
        //    ruling (ResourceCollector.OnSiegeDestroyed no longer takes anything from its pending),
        //    so the double-charge the superseded WO-1139 ruling feared is closed BY REMOVAL. There
        //    is exactly ONE theft in the game and it is the debit below. If anyone re-adds a second
        //    pool, this one must be removed in the same change.
        //
        //  * ONE NUMBER, BY IDENTITY RATHER THAN BY AGREEMENT. BuildStakes computes the ledger;
        //    ApplyStakes spends EXACTLY THOSE BUCKETS. The figure the player READS on the report
        //    and the figure the wallet LOST are the same object -- there is no second computation
        //    available to drift. A report that lies about a loss is worse than no report.
        //
        //  ! CRYSTALS ARE NEVER TAKEN. StakeRules.IsLootable refuses the bucket, the debit basket
        //    below never sets a crystal field, and ApplyStakes carries a backstop that zeroes a
        //    crystal bucket if anything else ever writes one. Three independent refusals, because
        //    a player cannot tell a harvested crystal from a purchased one and a crystal on a loss
        //    screen is a refund request on a live published title.

        /// <summary>
        /// WHAT THE ATTACK WILL TAKE -- computed once, from the bank's standing at settle time.
        ///
        /// <para>Reads the CURRENT balance and the CAPACITY of each lootable bucket
        /// (<c>TownBankCapacity</c> is the one authority on both) and hands them to
        /// <see cref="StakeRules.Build"/>, which applies the protected floor, the steal fraction
        /// for the outcome, and the per-attack cap. A HELD defence takes nothing.</para>
        ///
        /// <para><b>It takes nothing yet.</b> This method is a READ plus arithmetic;
        /// <see cref="ApplyStakes"/> performs the single debit of exactly these numbers.</para>
        ///
        /// <para>Guard-wrapped: if the scan throws, the fallback is an ALL-ZERO ledger. Failing
        /// towards "nothing was taken" is the only safe direction -- an exception must never be
        /// able to invent a loss on a live published game.</para>
        /// </summary>
        public static StakesLedger BuildStakes(DefenseOutcomeRecord settled)
        {
            var ledger = Guard.Try<StakesLedger>("Siege", "compute bank-theft stakes", () =>
            {
                var outcome = settled != null ? settled.Outcome : DefenseOutcome.Held;

                if (outcome == DefenseOutcome.Held)
                {
                    FlowTrace.Step("Siege",
                        "stakes: the defence HELD -- nothing is taken. That is structural, not a knob: " +
                        "if holding still cost resources the report would have nothing riding on it.");
                    return StakeRules.Empty();
                }

                var standings = new List<BankStanding>();
                foreach (var resource in LootableBuckets)
                {
                    if (!StakeRules.IsLootable(resource)) continue;   // belt and braces: the gate is StakeRules'
                    standings.Add(new BankStanding
                    {
                        Resource = resource,
                        Banked = TownBankCapacity.CurrentOf(resource),
                        Capacity = TownBankCapacity.IsCapped(resource)
                            ? TownBankCapacity.MaxOf(resource)
                            : StakeRules.UncappedCapacity,
                    });
                }

                var built = StakeRules.Build(outcome, standings);

                FlowTrace.Step("Siege",
                    $"stakes COMPUTED rule={built.StakesRuleId} outcome={outcome} " +
                    $"-W{built.Wood} -I{built.Iron} -S{built.Stone} -G{built.Coins} " +
                    "(stone is the balance named Food; gold is Coins). CRYSTALS/PREMIUM RAIL/PURCHASED GOODS/" +
                    "EQUIPPED GEAR ARE UNTOUCHABLE and have no expression here.");

                return built;
            }, fallback: null);

            if (ledger != null) return ledger;

            FlowTrace.Warn("Siege",
                "stakes computation FAILED -- filing an all-zero ledger. A throw must never be " +
                "able to invent a loss, so the failure direction is 'nothing was taken'.");
            return StakeRules.Empty();
        }

        /// <summary>
        /// The buckets a siege may read. Authored here as the DEBIT BASKET so the crystal exemption
        /// is visible at the call site as well as inside <see cref="StakeRules.IsLootable"/> --
        /// two independent refusals, neither relying on the other being remembered.
        /// </summary>
        private static readonly BankResource[] LootableBuckets =
        {
            BankResource.Wood,
            BankResource.Iron,
            BankResource.Stone,    // "Stone" player-facing -- live save/wire key, never renamed
            BankResource.Coins,   // "Gold" player-facing
        };

        /// <summary>
        /// THE SINGLE DEBIT, AT THE SINGLE SEAM. Called ONCE, from <c>SiegeScheduler.Settle</c>,
        /// between <c>SiegeSession.Close</c> and <c>DefenseReportLedger.Append</c>.
        ///
        /// <para>Spends exactly the buckets <see cref="BuildStakes"/> computed, through the
        /// EXISTING economy path (<c>EconomyService.TrySpend</c> -- the same atomic wallet writer
        /// every shop and build cost uses). There is no second economy writer, and no bespoke
        /// subtraction anywhere in the siege lane.</para>
        ///
        /// <para><b>The ledger is re-clamped to what the wallet could actually pay before the
        /// debit</b>, so the number the player READS is the number that ACTUALLY left the wallet
        /// even if the balance moved between compute and settle. Report and reality cannot diverge.</para>
        ///
        /// <para><b>Idempotent.</b> <see cref="StakesLedger.Applied"/> latches on the first call and
        /// every later call refuses -- that latch IS the "a siege bills ONCE per attack" guarantee,
        /// so a re-filed or re-opened report can never bill twice.</para>
        ///
        /// <para>! Nothing here downgrades a building, destroys permanent progress, or touches
        /// stars / cleared-camp state / gear / items / SKR. There is no code path in this file that
        /// could, and SiegeUntouchableRegression fails the gate if one is ever written.</para>
        /// </summary>
        /// <returns>True when the record carries a real, non-zero loss that was debited.</returns>
        public static bool ApplyStakes(DefenseOutcomeRecord settled)
        {
            if (settled == null) return false;
            if (settled.ResourcesLost == null)
                settled.ResourcesLost = StakeRules.Empty();

            var ledger = settled.ResourcesLost;

            if (ledger.Applied)
            {
                FlowTrace.Warn("Siege",
                    $"ApplyStakes called AGAIN on report {settled.Id} -- already settled " +
                    $"(-W{ledger.Wood} -I{ledger.Iron} -S{ledger.Stone} -G{ledger.Coins}). " +
                    "Refusing: a siege bills ONCE per attack.");
                return false;
            }

            // ! THE UNTOUCHABLE BACKSTOP. StakeRules.Add cannot write these buckets, so a non-zero
            //   one here means something else wrote the ledger (a bad migration, a hand-edited save,
            //   a future caller). Zero it BEFORE the debit basket is built, so an impossible value
            //   can never become a real charge.
            if (ledger.Crystals != 0 || ledger.Magic != 0)
            {
                FlowTrace.Fail("Siege",
                    $"stakes ledger carried crystals={ledger.Crystals} magic={ledger.Magic} -- NEITHER IS " +
                    "EVER TAKEABLE (owner ruling: crystals/premium rail/purchased goods/equipped gear are untouchable " +
                    "absolutely). Zeroed before the debit.");
                ledger.Crystals = 0;
                ledger.Magic = 0;
            }

            ledger.StakesRuleId = StakeRules.RuleId;

            if (ledger.Wood <= 0 && ledger.Iron <= 0 && ledger.Stone <= 0 && ledger.Coins <= 0)
            {
                FlowTrace.Step("Siege",
                    $"stakes: nothing was taken (outcome={settled.Outcome}) -- the defence held, or every " +
                    "balance sat at or under its protected floor. The floor is what stops the mechanic " +
                    "kicking a player who is already down.");
                return false;
            }

            // Re-clamp to what the wallet holds RIGHT NOW, so the report cannot claim more than left.
            ledger.Wood = ClampToBalance(ledger.Wood, BankResource.Wood);
            ledger.Iron = ClampToBalance(ledger.Iron, BankResource.Iron);
            ledger.Stone = ClampToBalance(ledger.Stone, BankResource.Stone);
            ledger.Coins = ClampToBalance(ledger.Coins, BankResource.Coins);

            if (ledger.Wood <= 0 && ledger.Iron <= 0 && ledger.Stone <= 0 && ledger.Coins <= 0)
            {
                FlowTrace.Warn("Siege",
                    "stakes: every bucket clamped to nothing against the live wallet -- the balance moved " +
                    "between compute and settle. Filing a zero loss rather than a figure the player " +
                    "cannot see in their own wallet.");
                ledger.Applied = true;   // settled, and it cost nothing -- never re-bill it
                return false;
            }

            var economy = EconomyService.Instance;
            if (economy == null)
            {
                // No silent failure (CLAUDE.md section 12): a report that CLAIMS a loss the wallet
                // never took is the divergence this whole design exists to prevent. Zero the ledger
                // so the report tells the truth, and say loudly why.
                FlowTrace.Fail("Siege",
                    $"stakes: NO EconomyService -- the debit could not run for report {settled.Id}. " +
                    "Zeroing the ledger so the report cannot claim a loss the wallet never took.");
                ledger.Wood = ledger.Iron = ledger.Stone = ledger.Coins = 0;
                ledger.Applied = true;
                return false;
            }

            // *** THE ONE DEBIT. Crystals are deliberately absent from the basket -- not zero, ABSENT.
            var basket = new DeNelle.Village.ResourceCost(
                wood: ledger.Wood,
                stone: ledger.Stone,      // "Stone"
                iron: ledger.Iron,
                crystals: 0,            // UNTOUCHABLE, absolutely
                coins: ledger.Coins);   // "Gold"

            bool spent = Guard.Try("Siege", "debit the siege stakes", () => economy.TrySpend(basket), fallback: false);

            if (!spent)
            {
                FlowTrace.Fail("Siege",
                    $"stakes: TrySpend REFUSED the basket for report {settled.Id} " +
                    $"(-W{ledger.Wood} -I{ledger.Iron} -S{ledger.Stone} -G{ledger.Coins}). Zeroing the " +
                    "ledger: the report must never claim a loss the wallet did not take.");
                ledger.Wood = ledger.Iron = ledger.Stone = ledger.Coins = 0;
                ledger.Applied = true;
                return false;
            }

            ledger.Applied = true;

            FlowTrace.Step("Siege",
                $"stakes DEBITED rule={ledger.StakesRuleId} -W{ledger.Wood} -I{ledger.Iron} " +
                $"-S{ledger.Stone} -G{ledger.Coins} (ONE bill for this siege; the floor and the cap held; " +
                "crystals/premium rail/purchased goods/equipped gear untouched).");

            return true;
        }

        /// <summary>Clamps a ledger bucket to the wallet's live balance. Never negative.</summary>
        private static int ClampToBalance(int amount, BankResource resource)
        {
            if (amount <= 0) return 0;
            int held = TownBankCapacity.CurrentOf(resource);
            if (held <= 0) return 0;
            return amount < held ? amount : held;
        }
    }
}
