// =============================================================================
// DefenseReportContractRegression — [defense-report] (WO-1026).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Registered ONCE in DataRegression.RunAll.
//
// WHAT THIS PINS — five things that are cheap to break and expensive to notice:
//
//  1. ROUND-TRIP. A fully-populated DefenseOutcomeRecord survives SaveSchema.JsonSettings
//     field-for-field. A report that silently loses its breaches on reload is worse than
//     no report: the player redesigns against a lie.
//
//  2. ⭐ THE MODEL-(c) PROOF. The SAME record with Attacker.Source = GhostSnapshot and a
//     non-empty SnapshotId round-trips IDENTICALLY. That is the assertion that ghost-PvP
//     is a SOURCE SWAP, not a schema change — the entire architectural claim of WO-1026,
//     made checkable today with no PvP built. The reserved LivePvp value round-trips too,
//     so the enum converter can never quietly drop it.
//
//  3. ⛔ THE STAKES GUARD. The WO-1026 loss ruling LANDED 2026-08-21 and WO-1139 built it,
//     so this case asserts the parts that survive any re-tuning: the production builder
//     stamps StakeRules.RuleId (a report always names the ruling that wrote it, so a
//     pre-WO-1139 record is never mis-read), it never emits crystals or magic, computing
//     the stakes never marks them APPLIED, and a HELD defence takes nothing. The
//     arithmetic itself is measured against an authored fixture in
//     SiegeLossStakesRegression -- on purpose NOT re-derived here from the same constants.
//
//  4. AC "A REDESIGN HAS A VISIBLE EFFECT", as DATA. Two layouts differing by one moved
//     structure MUST hash differently; the same layout in a different ORDER must hash the
//     SAME. Without the second half the hash would be a change-detector for save ordering,
//     not for the player's decisions.
//
//  5. THE RING BUFFER. Append MaxRetained+3 and the OLDEST are the ones dropped.
//
//  6. ⭐ THE LEGIBILITY HONESTY RULES (the WO-1026 follow-up). The owner's bar for that
//     layer is FELT -- "does losing feel like it was my fault, and do I know what to
//     change?" -- and a felt bar is not headlessly checkable. What IS checkable is the
//     thing that would silently destroy it: a hold time that LIES. So these cases pin
//     that an UNMEASURED hold time can never render as a duration, that pre-existing
//     damage disqualifies one, that a wall-less base is never reported as having a front
//     line, that the first breach is the EARLIEST, and -- enforced, not trusted -- that
//     no two plate marks share an ASCII glyph, i.e. nothing on the diagram is
//     distinguishable by COLOUR alone.
//
// ⚠ NOTE ON SAVE VERSIONING (deliberate deviation from the WO-1026 plan, recorded here so
//   a later reader does not "fix" it): the plan called for SaveSchema v39. The two WO-1026
//   fields are additive, nullable-on-the-wire and default-on-read, so they need NO bump --
//   the WO-771.9 / WO-808 precedent (barracksLevel / troopLevels / gearLevels) which
//   SaveSchema.cs documents in its own field comments. A schema bump on a LIVE published
//   game is an OWNER decision, and nothing here requires one. So this oracle asserts the
//   ADDITIVE CONTRACT (an older save with the fields absent loads clean) instead of
//   asserting a version number.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Defense;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Contract oracle for the WO-1026 defence report + its layout fingerprint.</summary>
    public static class DefenseReportContractRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            try
            {
                RoundTripCases(failures);
                GhostSourceSwapCase(failures);
                StakesInterimCase(failures);
                LayoutSensitivityCases(failures);
                AdditiveSaveContractCase(failures);
                NormalizeCase(failures);
                LegibilityCases(failures);
                LedgerRingBufferCases(failures, out bool skipped, out string skipWhy);
                if (skipped)
                {
                    // The GameStateService singleton/state seam moved -- genuinely unrunnable
                    // headless. NAMED SKIP, never a false FAIL (harness-integrity rule).
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "DEFENSE REPORT", "needs fleet -- " + skipWhy);
                }
            }
            catch (System.Exception ex)
            {
                failures.Add($"oracle threw: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count == 0)
            {
                reason = "DEFENSE REPORT OK -- record round-trips (incl. a GhostSnapshot-sourced one, " +
                         "so model (c) needs no schema change); stakes are self-describing @ " +
                         StakeRules.RuleId + " and a held defence takes nothing (crystals never); " +
                         "layout hash is move-sensitive + " +
                         "order-independent; ring buffer drops oldest; fields are additive default-on-read; " +
                         "hold time is never fabricated (unknown stays unknown, pre-damaged rows are " +
                         "disqualified); bands collapse honestly on a wall-less base; every plate mark has a " +
                         "unique glyph + a legend word (nothing reads by colour alone)";
                return true;
            }
            reason = $"DEFENSE REPORT FAIL x{failures.Count}: " + string.Join(" | ", failures);
            return false;
        }

        // ── 1. Round-trip ────────────────────────────────────────────────────────

        private static DefenseOutcomeRecord Populated(AttackerSource source, string snapshotId)
        {
            var r = DefenseOutcomeRecord.NewEmpty();
            r.Id = "fixed-report-id";
            r.StartedAtUnixMs = 1700000000000.0;
            r.EndedAtUnixMs = 1700000123000.0;
            r.Resolution = DefenseResolution.Live;
            r.Outcome = DefenseOutcome.Breached;
            r.WaveId = 7;
            r.DurationSeconds = 123.5f;

            r.Attacker.Source = source;
            r.Attacker.AttackerProfileId = "pve.warband.t1";
            r.Attacker.DisplayName = "Hollow Warband";
            r.Attacker.PowerRating = 42;
            r.Attacker.SnapshotId = snapshotId;
            r.Attacker.Units.Add(new AttackerUnitRecord { DefId = "hollow_one", Count = 9, Level = 3 });
            r.Attacker.Units.Add(new AttackerUnitRecord { DefId = "hollow_brute", Count = 2, Level = 4 });

            r.Defender.LayoutHash = "abc123def4567890";
            r.Defender.StructureCount = 17;
            r.Defender.WallCount = 24;
            r.Defender.TowerCount = 5;
            r.Defender.HeroPresent = true;
            r.Defender.Garrison.Add(new AttackerUnitRecord { DefId = "footman", Count = 3, Level = 2 });

            r.Breaches.Add(new BreachRecord
            {
                BreachedId = "NorthGate", DisplayName = "North Gate",
                WorldX = 12.5f, WorldY = 0f, WorldZ = -30.25f,
                AtSeconds = 41.5f, AttackerDefId = "hollow_brute",
            });
            r.Rows.Add(new StructureOutcome
            {
                DisplayName = "Lumbermill", DamageFraction = 0.4f, State = StructureState.Damaged,
                IsCollector = true, LootStolen = 120, HasCost = true,
                RepairWood = 80, RepairIron = 40, RepairStone = 0, RepairCrystals = 0,
            });

            // A POPULATED stakes basket, so the round-trip actually exercises every bucket's wire
            // key -- including "g" (COINS), which the owner's 2026-08-27 ruling added to the
            // lootable set. An additive key that is never populated is a key that is never proved.
            // Crystals/Magic stay 0: they are UNTOUCHABLE and no ruling may fill them.
            r.ResourcesLost = StakeRules.Empty();
            StakeRules.Add(r.ResourcesLost, DeNelle.Core.Economy.BankResource.Wood, 700);
            StakeRules.Add(r.ResourcesLost, DeNelle.Core.Economy.BankResource.Iron, 40);
            StakeRules.Add(r.ResourcesLost, DeNelle.Core.Economy.BankResource.Stone, 250);
            StakeRules.Add(r.ResourcesLost, DeNelle.Core.Economy.BankResource.Coins, 450);
            return r;
        }

        private static DefenseOutcomeRecord Cycle(DefenseOutcomeRecord r)
        {
            string json = JsonConvert.SerializeObject(r, SaveSchema.JsonSettings);
            return JsonConvert.DeserializeObject<DefenseOutcomeRecord>(json, SaveSchema.JsonSettings);
        }

        private static void Compare(DefenseOutcomeRecord a, DefenseOutcomeRecord b, string tag, List<string> f)
        {
            if (b == null) { f.Add($"{tag}: deserialised to NULL"); return; }
            if (a.Id != b.Id) f.Add($"{tag}: Id {a.Id} -> {b.Id}");
            if (a.RecordVersion != b.RecordVersion) f.Add($"{tag}: RecordVersion {a.RecordVersion} -> {b.RecordVersion}");
            if (a.StartedAtUnixMs != b.StartedAtUnixMs) f.Add($"{tag}: StartedAtUnixMs drifted");
            if (a.EndedAtUnixMs != b.EndedAtUnixMs) f.Add($"{tag}: EndedAtUnixMs drifted");
            if (a.Resolution != b.Resolution) f.Add($"{tag}: Resolution {a.Resolution} -> {b.Resolution}");
            if (a.Outcome != b.Outcome) f.Add($"{tag}: Outcome {a.Outcome} -> {b.Outcome}");
            if (a.WaveId != b.WaveId) f.Add($"{tag}: WaveId {a.WaveId} -> {b.WaveId}");
            if (!Mathf.Approximately(a.DurationSeconds, b.DurationSeconds)) f.Add($"{tag}: DurationSeconds drifted");

            if (a.Attacker.Source != b.Attacker.Source) f.Add($"{tag}: Attacker.Source {a.Attacker.Source} -> {b.Attacker.Source}");
            if (a.Attacker.AttackerProfileId != b.Attacker.AttackerProfileId) f.Add($"{tag}: Attacker.AttackerProfileId drifted");
            if (a.Attacker.DisplayName != b.Attacker.DisplayName) f.Add($"{tag}: Attacker.DisplayName drifted");
            if (a.Attacker.PowerRating != b.Attacker.PowerRating) f.Add($"{tag}: Attacker.PowerRating drifted");
            if (a.Attacker.SnapshotId != b.Attacker.SnapshotId)
                f.Add($"{tag}: Attacker.SnapshotId '{a.Attacker.SnapshotId}' -> '{b.Attacker.SnapshotId}' " +
                      "(the model-(c) key must survive the wire)");
            if (a.Attacker.Units.Count != b.Attacker.Units.Count) f.Add($"{tag}: Attacker.Units count drifted");
            else for (int i = 0; i < a.Attacker.Units.Count; i++)
                if (a.Attacker.Units[i].DefId != b.Attacker.Units[i].DefId
                    || a.Attacker.Units[i].Count != b.Attacker.Units[i].Count
                    || a.Attacker.Units[i].Level != b.Attacker.Units[i].Level)
                    f.Add($"{tag}: Attacker.Units[{i}] drifted");

            if (a.Defender.LayoutHash != b.Defender.LayoutHash) f.Add($"{tag}: Defender.LayoutHash drifted");
            if (a.Defender.StructureCount != b.Defender.StructureCount) f.Add($"{tag}: Defender.StructureCount drifted");
            if (a.Defender.WallCount != b.Defender.WallCount) f.Add($"{tag}: Defender.WallCount drifted");
            if (a.Defender.TowerCount != b.Defender.TowerCount) f.Add($"{tag}: Defender.TowerCount drifted");
            if (a.Defender.HeroPresent != b.Defender.HeroPresent) f.Add($"{tag}: Defender.HeroPresent drifted");
            if (a.Defender.Garrison.Count != b.Defender.Garrison.Count) f.Add($"{tag}: Defender.Garrison count drifted");

            if (a.Breaches.Count != b.Breaches.Count) { f.Add($"{tag}: Breaches count drifted"); }
            else for (int i = 0; i < a.Breaches.Count; i++)
            {
                var x = a.Breaches[i]; var y = b.Breaches[i];
                if (x.BreachedId != y.BreachedId || x.DisplayName != y.DisplayName
                    || !Mathf.Approximately(x.WorldX, y.WorldX) || !Mathf.Approximately(x.WorldZ, y.WorldZ)
                    || !Mathf.Approximately(x.AtSeconds, y.AtSeconds) || x.AttackerDefId != y.AttackerDefId)
                    f.Add($"{tag}: Breaches[{i}] drifted (the redesign signal must survive the wire)");
            }

            if (a.Rows.Count != b.Rows.Count) { f.Add($"{tag}: Rows count drifted"); }
            else for (int i = 0; i < a.Rows.Count; i++)
            {
                var x = a.Rows[i]; var y = b.Rows[i];
                if (x.DisplayName != y.DisplayName || x.Destroyed != y.Destroyed
                    || x.IsCollector != y.IsCollector || x.LootStolen != y.LootStolen
                    || x.HasCost != y.HasCost || x.RepairWood != y.RepairWood
                    || x.RepairIron != y.RepairIron || x.RepairStone != y.RepairStone
                    || x.RepairCrystals != y.RepairCrystals
                    || !Mathf.Approximately(x.DamageFraction, y.DamageFraction))
                    f.Add($"{tag}: Rows[{i}] drifted");
            }

            if (a.ResourcesLost.StakesRuleId != b.ResourcesLost.StakesRuleId) f.Add($"{tag}: ResourcesLost.StakesRuleId drifted");
            if (a.ResourcesLost.Wood != b.ResourcesLost.Wood || a.ResourcesLost.Iron != b.ResourcesLost.Iron
                || a.ResourcesLost.Stone != b.ResourcesLost.Stone || a.ResourcesLost.Coins != b.ResourcesLost.Coins
                || a.ResourcesLost.Crystals != b.ResourcesLost.Crystals
                || a.ResourcesLost.Magic != b.ResourcesLost.Magic) f.Add($"{tag}: ResourcesLost basket drifted");
            if (b.ResourcesLost.Wood == 0 && b.ResourcesLost.Iron == 0 && b.ResourcesLost.Stone == 0
                && b.ResourcesLost.Coins == 0)
                f.Add($"{tag}: the stakes basket came back ALL ZERO -- the fixture populates four " +
                      "buckets, so this comparison would be asserting nothing");
            if (a.Read != b.Read) f.Add($"{tag}: Read drifted");
        }

        private static void RoundTripCases(List<string> f)
        {
            var pve = Populated(AttackerSource.GeneratedPve, string.Empty);
            Compare(pve, Cycle(pve), "case1 pve round-trip", f);
        }

        // ── 2. ⭐ THE MODEL-(c) PROOF ────────────────────────────────────────────

        private static void GhostSourceSwapCase(List<string> f)
        {
            var ghost = Populated(AttackerSource.GhostSnapshot, "snap-9f2c-town-of-mirren");
            var back = Cycle(ghost);
            Compare(ghost, back, "case2 ghost round-trip", f);

            if (back != null && back.Attacker.Source != AttackerSource.GhostSnapshot)
                f.Add("case2 GhostSnapshot source did NOT survive -- model (c) would need a schema change");
            if (back != null && string.IsNullOrEmpty(back.Attacker.SnapshotId))
                f.Add("case2 SnapshotId came back EMPTY -- a replay button would have nothing to point at");

            // The RESERVED value must survive too, or a future PvP record silently degrades to PvE.
            var live = Populated(AttackerSource.LivePvp, "live-1");
            var liveBack = Cycle(live);
            if (liveBack == null || liveBack.Attacker.Source != AttackerSource.LivePvp)
                f.Add("case3 reserved LivePvp value was DROPPED by the enum converter");

            // And the interim-model record must NOT claim a snapshot: a GeneratedPve record with a
            // snapshot id is a contradiction that would mislead the (c) implementer.
            var pveAttacker = DeNelle.Village.DefenseReportBuilder.BuildPveAttacker(5);
            if (pveAttacker.Source != AttackerSource.GeneratedPve)
                f.Add($"case3 the PvE builder emitted {pveAttacker.Source}, not GeneratedPve");
            if (!string.IsNullOrEmpty(pveAttacker.SnapshotId))
                f.Add($"case3 the PvE builder stamped SnapshotId '{pveAttacker.SnapshotId}' -- " +
                      "model (a) has no snapshot; a non-empty id here is a contradiction");
        }

        // ── 3. ⛔ THE STAKES GUARD ───────────────────────────────────────────────
        //
        // The WO-1026 ruling LANDED (owner 2026-08-21) and WO-1139 implemented it, so this case
        // no longer asserts "zero". It asserts the two things that outlive any tuning of the
        // ruling: the ledger is SELF-DESCRIBING about which ruling wrote it, and a HELD defence
        // takes nothing. The arithmetic itself is measured against an authored fixture table in
        // SiegeLossStakesRegression -- deliberately not re-derived here from the same constants.

        private static void StakesInterimCase(List<string> f)
        {
            var settled = Populated(AttackerSource.GeneratedPve, string.Empty);
            settled.Outcome = DefenseOutcome.Overrun;
            var stakes = DeNelle.Village.DefenseReportBuilder.BuildStakes(settled);

            if (stakes == null) { f.Add("case4 BuildStakes returned null"); return; }

            if (stakes.StakesRuleId != StakeRules.RuleId)
                f.Add($"case4 StakesRuleId is '{stakes.StakesRuleId}', expected '{StakeRules.RuleId}' " +
                      "-- every report must stay self-describing about which ruling produced it, so a " +
                      "pre-WO-1139 record is never mis-read as 'they lost nothing that day'");

            if (stakes.Crystals != 0 || stakes.Magic != 0)
                f.Add($"case4 [HARD-RULE] THE BUILDER PRODUCED CRYSTALS/MAGIC (c{stakes.Crystals} m{stakes.Magic}) -- " +
                      "crystals are purchasable with real money and are NEVER stealable (owner ruling " +
                      "2026-08-21). This is a hard exemption, not a balance knob.");

            if (stakes.Applied)
                f.Add("case4 BuildStakes returned an ALREADY-APPLIED ledger -- computing the stakes must " +
                      "never charge anyone; only DefenseReportBuilder.ApplyStakes may set Applied.");

            // A CLEARED defence takes nothing, whatever the bank holds.
            var held = Populated(AttackerSource.GeneratedPve, string.Empty);
            held.Outcome = DefenseOutcome.Held;
            var heldStakes = DeNelle.Village.DefenseReportBuilder.BuildStakes(held);
            if (heldStakes == null || !heldStakes.IsEmpty)
                f.Add("case4 a HELD defence produced a non-empty stakes ledger -- the ruling governs a " +
                      "FAILED defence only");

            // A pre-WO-1139 record must keep its own rule id: the interim constant still exists and
            // Normalize still uses it. Deleting it would silently relabel history.
            if (StakesLedger.InterimRuleId == StakeRules.RuleId)
                f.Add("case4 the interim rule id was collapsed into the live one -- old reports would " +
                      "start claiming they were written under a ruling that did not exist yet");
        }

        // ── 4. LAYOUT SENSITIVITY (AC: a redesign has a visible effect) ──────────

        private static PlacedStructureData P(string id, int x, int z, int yaw, int lvl)
        {
            var p = new PlacedStructureData();
            p.itemId = id; p.cellX = x; p.cellZ = z; p.yawSteps = yaw; p.level = lvl;
            return p;
        }

        private static void LayoutSensitivityCases(List<string> f)
        {
            var baseline = new List<PlacedStructureData>
            {
                P("arrow_tower", 3, 4, 0, 2),
                P("wall", -2, 7, 1, 1),
                P("lumbermill", 0, 0, 0, 3),
            };
            string hBase = DeNelle.Village.DefenseReportBuilder.ComputeLayoutHash(baseline);

            // (a) MOVED structure -> DIFFERENT hash. This is the whole AC.
            var moved = new List<PlacedStructureData>
            {
                P("arrow_tower", 8, 4, 0, 2),   // the tower moved
                P("wall", -2, 7, 1, 1),
                P("lumbermill", 0, 0, 0, 3),
            };
            if (DeNelle.Village.DefenseReportBuilder.ComputeLayoutHash(moved) == hBase)
                f.Add("case5 moving a structure did NOT change the layout hash -- " +
                      "'a redesign has a visible effect' would be unprovable");

            // (b) SAME layout, DIFFERENT ORDER -> SAME hash. Without this the hash tracks
            //     save-list ordering, not the player's decisions.
            var reordered = new List<PlacedStructureData>
            {
                P("lumbermill", 0, 0, 0, 3),
                P("arrow_tower", 3, 4, 0, 2),
                P("wall", -2, 7, 1, 1),
            };
            if (DeNelle.Village.DefenseReportBuilder.ComputeLayoutHash(reordered) != hBase)
                f.Add("case5 re-ORDERING the same layout changed the hash -- the fingerprint must sort");

            // (c) UPGRADED in place -> different hash (level is part of the base's identity).
            var upgraded = new List<PlacedStructureData>
            {
                P("arrow_tower", 3, 4, 0, 5),
                P("wall", -2, 7, 1, 1),
                P("lumbermill", 0, 0, 0, 3),
            };
            if (DeNelle.Village.DefenseReportBuilder.ComputeLayoutHash(upgraded) == hBase)
                f.Add("case5 upgrading a structure did NOT change the layout hash");

            // (d) empty / null are stable and equal.
            if (DeNelle.Village.DefenseReportBuilder.ComputeLayoutHash(null) != LayoutFingerprint.Empty)
                f.Add("case5 null layout did not hash to LayoutFingerprint.Empty");
            if (DeNelle.Village.DefenseReportBuilder.ComputeLayoutHash(new List<PlacedStructureData>())
                != LayoutFingerprint.Empty)
                f.Add("case5 empty layout did not hash to LayoutFingerprint.Empty");

            // (e) the separator guard: "ab"+"c" must not collide with "a"+"bc".
            if (LayoutFingerprint.Compute(new[] { "ab", "c" })
                == LayoutFingerprint.Compute(new[] { "a", "bc" }))
                f.Add("case5 token concatenation collision -- the FNV separator is not doing its job");
        }

        // ── 5. THE ADDITIVE SAVE CONTRACT (in place of a version assertion) ──────

        private static void AdditiveSaveContractCase(List<string> f)
        {
            // A save blob written BEFORE WO-1026 has neither key. It must deserialise cleanly with
            // both absent -> the GameState initializers apply on read. That is what makes this an
            // additive change needing NO schema bump (and NO owner decision on a live game).
            const string legacy = "{\"gold\":10,\"onboarded\":true}";
            SaveSchema.PersistedState p = null;
            try { p = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(legacy, SaveSchema.JsonSettings); }
            catch (System.Exception ex) { f.Add($"case6 legacy blob threw: {ex.GetType().Name}: {ex.Message}"); return; }

            if (p == null) { f.Add("case6 legacy blob deserialised to null"); return; }
            if (p.DefenseReports != null)
                f.Add("case6 defenseReports was expected ABSENT (null) on a pre-WO-1026 blob");
            if (p.LastSiegeUnixMs.HasValue)
                f.Add("case6 lastSiegeUnixMs was expected ABSENT (null) on a pre-WO-1026 blob");

            // And a blob that DOES carry them round-trips.
            p.DefenseReports = new List<DefenseOutcomeRecord> { Populated(AttackerSource.GeneratedPve, string.Empty) };
            p.LastSiegeUnixMs = 1700000000000.0;
            string json = JsonConvert.SerializeObject(p, SaveSchema.JsonSettings);
            var back = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(json, SaveSchema.JsonSettings);
            if (back == null || back.DefenseReports == null || back.DefenseReports.Count != 1)
                f.Add("case6 defenseReports did NOT survive a PersistedState round-trip");
            else Compare(p.DefenseReports[0], back.DefenseReports[0], "case6 nested record", f);
            if (back == null || !back.LastSiegeUnixMs.HasValue || back.LastSiegeUnixMs.Value != 1700000000000.0)
                f.Add("case6 lastSiegeUnixMs did NOT survive a PersistedState round-trip");
        }

        // ── 6. Normalize tolerance (no reader ever meets a null sub-object) ──────

        private static void NormalizeCase(List<string> f)
        {
            // A partial/older wire with every sub-object missing must normalise, not throw.
            var bare = JsonConvert.DeserializeObject<DefenseOutcomeRecord>("{}", SaveSchema.JsonSettings);
            var n = DefenseOutcomeRecord.Normalize(bare);
            if (n == null) { f.Add("case7 Normalize returned null"); return; }
            if (n.Attacker == null || n.Attacker.Units == null) f.Add("case7 Attacker/Units left null");
            if (n.Defender == null || n.Defender.Garrison == null) f.Add("case7 Defender/Garrison left null");
            if (n.Breaches == null || n.Rows == null) f.Add("case7 Breaches/Rows left null");
            if (n.ResourcesLost == null || n.ResourcesLost.StakesRuleId != StakesLedger.InterimRuleId)
                f.Add("case7 ResourcesLost not defaulted to the interim ledger");
            if (string.IsNullOrEmpty(n.Id)) f.Add("case7 Id left empty (the panel selects by it)");
            if (n.RecordVersion != DefenseOutcomeRecord.CurrentRecordVersion)
                f.Add("case7 RecordVersion not defaulted");
        }

        // ── ⭐ LEGIBILITY: the fields that turn a loss list into a diagnosis ─────
        //
        // The owner's bar for this layer is FELT — "does losing feel like it was my fault,
        // and do I know what to change?" — and a felt bar is not headlessly checkable. What
        // IS checkable is the thing that would silently destroy it: a hold time that LIES.
        // These cases pin the honesty rules, not the feeling.

        private static void LegibilityCases(List<string> f)
        {
            // (a) The band classification. Core wins inside the ring, Front at/beyond the wall
            //     ring, Second between — using the report's OWN stored radii.
            const float coreR = 12f, frontR = 40f;
            if (DeNelle.Village.DefenseReportBuilder.ClassifyBand(5f, coreR, frontR) != DefenseBand.Core)
                f.Add("case10 a structure inside the core radius was not banded Core");
            if (DeNelle.Village.DefenseReportBuilder.ClassifyBand(25f, coreR, frontR) != DefenseBand.Second)
                f.Add("case10 a structure between the rings was not banded Second");
            if (DeNelle.Village.DefenseReportBuilder.ClassifyBand(60f, coreR, frontR) != DefenseBand.Front)
                f.Add("case10 a structure beyond the wall ring was not banded Front");

            // (b) ⛔ A BASE WITH NO WALLS HAS NO FRONT LINE. frontRadius 0 must NOT band
            //     everything Front — that would report a front line the player does not have,
            //     and the whole point of the grouping is to tell them they need one.
            if (DeNelle.Village.DefenseReportBuilder.ClassifyBand(100f, coreR, 0f) == DefenseBand.Front)
                f.Add("case10 a wall-less base reported a FRONT LINE -- with frontRadius 0 the Front " +
                      "band must collapse, or the report invents a defence the player never built");

            // (c) ⛔ THE LOAD-BEARING HONESTY RULE: an UNMEASURED hold time is -1 and must
            //     never read as a real duration. HasHoldTime is what every renderer gates on.
            var unknown = new StructureOutcome { HoldTimeSeconds = -1f, State = StructureState.Destroyed };
            if (unknown.HasHoldTime)
                f.Add("case11 a row with HoldTimeSeconds -1 reported HasHoldTime -- an unknown hold time " +
                      "would render as 'fell in 0s' and send the player to move the WRONG structure");

            // (d) Pre-existing damage disqualifies the hold time even when one was measured:
            //     that duration describes an EARLIER fight.
            var stale = new StructureOutcome { HoldTimeSeconds = 4f, State = StructureState.Destroyed, WasAlreadyDamaged = true };
            if (stale.HasHoldTime)
                f.Add("case11 a PRE-DAMAGED row reported a usable hold time -- its timing belongs to " +
                      "an earlier assault and must not be presented as this one's");
            var good = new StructureOutcome { HoldTimeSeconds = 4f, State = StructureState.Destroyed };
            if (!good.HasHoldTime)
                f.Add("case11 a genuinely measured hold time was rejected by HasHoldTime");

            // (d2) ⛔ THE SCORE DECLINES RATHER THAN GUESSING — the same discipline as the hold
            //      time. With no structure census the destroyed-fraction term is undefined, so a
            //      number would be the outcome enum wearing three inputs' clothes.
            var noCensus = DefenseOutcomeRecord.NewEmpty();
            noCensus.Outcome = DefenseOutcome.Breached;
            noCensus.Defender.StructureCount = 0;
            int declined = DeNelle.Village.DefenseReportBuilder.ComputeDefenseScore(noCensus);
            if (declined != DefenseOutcomeRecord.NotScored)
                f.Add($"case11 the defence score returned {declined} with NO structure census -- it must " +
                      "DECLINE (NotScored). A confident-looking number built from one input is exactly " +
                      "what the honesty rule forbids.");
            if (noCensus.HasDefenseScore)
                f.Add("case11 HasDefenseScore was true for a declined score");
            if (!string.IsNullOrEmpty(DeNelle.Village.DefenseReportBuilder.DefenseScoreWord(DefenseOutcomeRecord.NotScored)))
                f.Add("case11 DefenseScoreWord returned a word for a declined score -- the panel would " +
                      "print a grade for a score that does not exist");

            // (d3) With a census it scores, is bounded, and is MONOTONE in the outcome: a clean
            //      hold must never score below a breach, or the number is actively misleading.
            var held = DefenseOutcomeRecord.NewEmpty();
            held.Outcome = DefenseOutcome.Held; held.Defender.StructureCount = 20;
            var breached = DefenseOutcomeRecord.NewEmpty();
            breached.Outcome = DefenseOutcome.Breached; breached.Defender.StructureCount = 20;
            var overrun = DefenseOutcomeRecord.NewEmpty();
            overrun.Outcome = DefenseOutcome.Overrun; overrun.Defender.StructureCount = 20;
            int sh = DeNelle.Village.DefenseReportBuilder.ComputeDefenseScore(held);
            int sb = DeNelle.Village.DefenseReportBuilder.ComputeDefenseScore(breached);
            int so = DeNelle.Village.DefenseReportBuilder.ComputeDefenseScore(overrun);
            if (sh < 0 || sh > 100 || sb < 0 || sb > 100 || so < 0 || so > 100)
                f.Add($"case11 a score fell outside 0-100 (held {sh}, breached {sb}, overrun {so})");
            if (!(sh > sb && sb > so))
                f.Add($"case11 the score is not monotone in the outcome (held {sh}, breached {sb}, " +
                      $"overrun {so}) -- a clean hold must never score at or below a breach");

            // (d4) Destruction pushes it DOWN, and the floor holds.
            var wrecked = DefenseOutcomeRecord.NewEmpty();
            wrecked.Outcome = DefenseOutcome.Overrun;
            wrecked.Defender.StructureCount = 4;
            for (int i = 0; i < 4; i++)
                wrecked.Rows.Add(new StructureOutcome { State = StructureState.Destroyed });
            for (int i = 0; i < 8; i++)
                wrecked.Breaches.Add(new BreachRecord { AtSeconds = i });
            int sw = DeNelle.Village.DefenseReportBuilder.ComputeDefenseScore(wrecked);
            if (sw < 0 || sw > 100) f.Add($"case11 a total loss scored outside 0-100 ({sw})");
            if (sw >= so) f.Add($"case11 a totally destroyed base ({sw}) did not score below a bare overrun ({so})");

            // (e) The FIRST breach is the report's headline; every reader must agree on it.
            var r = Populated(AttackerSource.GeneratedPve, string.Empty);
            r.Breaches.Add(new BreachRecord { DisplayName = "South Gate", AtSeconds = 90f });
            var first = DeNelle.Village.DefenseReportBuilder.FirstBreach(r);
            if (first == null || first.DisplayName != "North Gate")
                f.Add("case12 FirstBreach did not return the EARLIEST breach (it is the headline of " +
                      "the whole report -- a later one would point the player at the wrong wall)");
            if (DeNelle.Village.DefenseReportBuilder.FirstBreach(DefenseOutcomeRecord.NewEmpty()) != null)
                f.Add("case12 FirstBreach invented a breach on a clean hold");

            // (f) The compass words the diagnosis is built from. North is +Z, matching the
            //     plate's north-up projection and HudMinimapWidget's north-up choice.
            if (DefenseMapPlate.Compass(0f, 10f) != "north") f.Add("case13 +Z did not read as north");
            if (DefenseMapPlate.Compass(10f, 0f) != "east") f.Add("case13 +X did not read as east");
            if (DefenseMapPlate.Compass(0f, -10f) != "south") f.Add("case13 -Z did not read as south");
            if (DefenseMapPlate.Compass(-10f, 0f) != "west") f.Add("case13 -X did not read as west");
            if (DefenseMapPlate.Compass(0f, 0f) != "centre") f.Add("case13 a zero offset did not read as centre");

            // (g) ⛔ COLOURBLIND LAW, enforced rather than trusted: every plate mark must have a
            //     distinct ASCII GLYPH, and the legend must spell each one out in words. If two
            //     marks ever share a glyph, the plate would be relying on colour to tell them
            //     apart — which the owner cannot see.
            var glyphs = new List<string>
            {
                RealmAtmosphereStyle.PinAscii(RealmPinShape.Circle),      // the Heart
                RealmAtmosphereStyle.PinAscii(RealmPinShape.TriangleUp),  // breach
                RealmAtmosphereStyle.PinAscii(RealmPinShape.Square),      // destroyed
                RealmAtmosphereStyle.PinAscii(RealmPinShape.Ring),        // damaged
            };
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (string.IsNullOrEmpty(glyphs[i])) { f.Add($"case14 plate glyph {i} is empty"); continue; }
                for (int j = i + 1; j < glyphs.Count; j++)
                    if (glyphs[i] == glyphs[j])
                        f.Add($"case14 plate marks {i} and {j} share the glyph '{glyphs[i]}' -- they would " +
                              "be distinguishable only by COLOUR, which the owner cannot see");
            }
            if (DefenseMapPlate.Legend == null || DefenseMapPlate.Legend.Length < glyphs.Count)
                f.Add("case14 the plate legend does not spell out every mark -- a glyph with no words " +
                      "is a symbol only the author understands");

            // (h) The text twin. The diagram must be DECORATION over facts already in words, so
            //     a report with a breach always describes it in sentences too.
            var described = DefenseMapPlate.DescribeMarks(r);
            if (described == null || described.Count == 0)
                f.Add("case15 DescribeMarks returned nothing for a report WITH a breach -- the plate " +
                      "would then be the only place that fact exists");
            var clean = DefenseOutcomeRecord.NewEmpty();
            var cleanDesc = DefenseMapPlate.DescribeMarks(clean);
            if (cleanDesc == null || cleanDesc.Count == 0)
                f.Add("case15 DescribeMarks said nothing about a CLEAN hold -- 'nothing got in' is a " +
                      "result the player needs stated, not an empty panel");

            // (i) The new legibility fields round-trip (they are the diagnosis; losing them on
            //     reload would quietly downgrade every stored report back to a flat list).
            r.Rows[0].HoldTimeSeconds = 41.5f;
            r.Rows[0].FirstHitAtSeconds = 10f;
            r.Rows[0].FellAtSeconds = 51.5f;
            r.Rows[0].Band = DefenseBand.Front;
            r.Rows[0].DistanceFromCore = 44f;
            r.Rows[0].WorldX = 12f;
            r.Rows[0].WorldZ = -8f;
            r.Rows[0].WasAlreadyDamaged = false;
            r.Defender.CoreRadius = coreR;
            r.Defender.FrontRadius = frontR;
            r.Path.Add(new AttackPathPoint { WorldX = 80f, WorldZ = 4f, AtSeconds = 0f, LiveCount = 12 });
            r.Path.Add(new AttackPathPoint { WorldX = 40f, WorldZ = 2f, AtSeconds = 2f, LiveCount = 11 });

            var back = Cycle(r);
            if (back == null) { f.Add("case16 legibility round-trip returned null"); return; }
            var a0 = r.Rows[0]; var b0 = back.Rows.Count > 0 ? back.Rows[0] : null;
            if (b0 == null) f.Add("case16 the loss row vanished on round-trip");
            else
            {
                if (!Mathf.Approximately(a0.HoldTimeSeconds, b0.HoldTimeSeconds)) f.Add("case16 HoldTimeSeconds drifted -- the hold time IS the diagnosis");
                if (!Mathf.Approximately(a0.FirstHitAtSeconds, b0.FirstHitAtSeconds)) f.Add("case16 FirstHitAtSeconds drifted");
                if (!Mathf.Approximately(a0.FellAtSeconds, b0.FellAtSeconds)) f.Add("case16 FellAtSeconds drifted");
                if (a0.Band != b0.Band) f.Add("case16 the defence Band band drifted");
                if (!Mathf.Approximately(a0.DistanceFromCore, b0.DistanceFromCore)) f.Add("case16 DistanceFromCore drifted");
                if (!Mathf.Approximately(a0.WorldX, b0.WorldX) || !Mathf.Approximately(a0.WorldZ, b0.WorldZ))
                    f.Add("case16 the loss pin position drifted");
                if (a0.WasAlreadyDamaged != b0.WasAlreadyDamaged) f.Add("case16 WasAlreadyDamaged drifted");
            }
            if (!Mathf.Approximately(back.Defender.CoreRadius, coreR)
                || !Mathf.Approximately(back.Defender.FrontRadius, frontR))
                f.Add("case16 the stored band radii drifted -- an old report would silently RE-BAND " +
                      "itself against a rebuilt town");
            if (back.Path.Count != r.Path.Count) f.Add("case16 the attack path drifted on round-trip");
            else if (!Mathf.Approximately(back.Path[0].WorldX, 80f) || back.Path[0].LiveCount != 12)
                f.Add("case16 an attack path sample drifted");
        }

        // ── 7. THE RING BUFFER (needs a headless GameState — editmode has no Awake) ──

        private static void LedgerRingBufferCases(List<string> f, out bool skipped, out string skipWhy)
        {
            skipped = false; skipWhy = null;

            var prior = GameStateService.Instance;
            string rawSave = HeadlessState.SnapshotSave(out bool hadSave);
            GameObject gssGo = null;
            GameState throwaway = null;
            bool installed = false;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GameStateService (defense-report-oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!HeadlessState.TryInstall(gss, throwaway, out skipWhy)) { skipped = true; return; }
                installed = true;

                DefenseReportLedger.Clear();

                // Append MaxRetained + 3; the OLDEST must be the ones dropped.
                int total = DefenseReportLedger.MaxRetained + 3;
                for (int i = 0; i < total; i++)
                {
                    var r = DefenseOutcomeRecord.NewEmpty();
                    r.Id = "r" + i;
                    r.WaveId = i;
                    DefenseReportLedger.Append(r);
                }

                var all = DefenseReportLedger.All();
                if (all.Count != DefenseReportLedger.MaxRetained)
                    f.Add($"case8 ring buffer holds {all.Count}, expected {DefenseReportLedger.MaxRetained}");
                if (DefenseReportLedger.TryGet("r0") != null)
                    f.Add("case8 the OLDEST report survived the trim (the buffer dropped the wrong end)");
                if (DefenseReportLedger.TryGet("r" + (total - 1)) == null)
                    f.Add("case8 the NEWEST report was dropped by the trim");

                // NewestFirst is the panel's order.
                var newest = DefenseReportLedger.NewestFirst();
                if (newest.Count > 0 && newest[0].Id != "r" + (total - 1))
                    f.Add($"case8 NewestFirst()[0] is {newest[0].Id}, expected r{total - 1}");

                // Unread / MarkRead.
                if (DefenseReportLedger.UnreadCount() != DefenseReportLedger.MaxRetained)
                    f.Add($"case9 UnreadCount is {DefenseReportLedger.UnreadCount()}, expected all unread");
                string markId = "r" + (total - 1);
                if (!DefenseReportLedger.MarkRead(markId))
                    f.Add("case9 MarkRead returned false for a retained, unread report");
                if (DefenseReportLedger.MarkRead(markId))
                    f.Add("case9 MarkRead returned true a SECOND time (it must no-op, not churn the save)");
                if (DefenseReportLedger.UnreadCount() != DefenseReportLedger.MaxRetained - 1)
                    f.Add($"case9 UnreadCount did not drop after MarkRead (is {DefenseReportLedger.UnreadCount()})");

                // Append(null) is refused, not thrown, and does not corrupt the buffer.
                int before = DefenseReportLedger.All().Count;
                if (DefenseReportLedger.Append(null))
                    f.Add("case9 Append(null) reported success");
                if (DefenseReportLedger.All().Count != before)
                    f.Add("case9 Append(null) changed the buffer");

                // ── ⭐ case10: SURVIVES A RESTART, through the REAL save/load ──────
                // The AC is "survives a session restart", which is strictly stronger than
                // "Append called Save()". A record can serialise perfectly and still be lost on
                // the way back: the load path runs PlayerPrefs -> HMAC integrity gate -> migrator
                // -> SaveSchema.Validate -> ApplyPersisted, and ANY of those four can reject a
                // payload and silently keep fresh state (GameStateService.Load returns false and
                // the town simply has no history). So this drives the real thing.
                //
                // A restart is simulated the only way it can be headlessly: install a SECOND,
                // FRESH GameState on the service -- exactly what a cold boot has -- and Load()
                // into it. Nothing is stubbed; it is the same Provider, the same signature check
                // and the same validator the device runs.
                DefenseReportLedger.Clear();
                var landmark = DefenseOutcomeRecord.NewEmpty();
                landmark.Id = "restart-proof";
                landmark.WaveId = 42;
                landmark.Outcome = DefenseOutcome.Breached;
                landmark.DefenseScore = 63;
                landmark.Defender.LayoutHash = "feedfacecafe0001";
                landmark.Defender.CoreRadius = 12f;
                landmark.Defender.FrontRadius = 40f;
                landmark.Rows.Add(new StructureOutcome
                {
                    DisplayName = "North Gate",
                    StructureId = "NorthGate",
                    StructureType = "Gate",
                    State = StructureState.Destroyed,
                    DamageFraction = 1f,
                    HoldTimeSeconds = 7.5f,
                    FirstHitAtSeconds = 12f,
                    FellAtSeconds = 19.5f,
                    Band = DefenseBand.Front,
                    BreachOrdinal = 1,
                });
                DefenseReportLedger.Append(landmark);   // Append persists through GameStateService.Save

                var rebooted = ScriptableObject.CreateInstance<GameState>();   // a COLD BOOT's state
                try
                {
                    if (!HeadlessState.TryInstall(gss, rebooted, out string reErr))
                    { f.Add("case10 could not install the rebooted state: " + reErr); }
                    else if (!gss.Load())
                    {
                        f.Add("case10 GameStateService.Load() returned FALSE after a Save that wrote a " +
                              "defence report -- the record did not survive the restart path (integrity " +
                              "gate / migrator / validator rejected it, or nothing was written).");
                    }
                    else
                    {
                        var back = DefenseReportLedger.TryGet("restart-proof");
                        if (back == null)
                        {
                            f.Add("case10 the report was GONE after a real save/load restart -- it " +
                                  "serialises but does not come back, which is the failure the AC names.");
                        }
                        else
                        {
                            if (back.WaveId != 42) f.Add("case10 WaveId lost across the restart");
                            if (back.Outcome != DefenseOutcome.Breached) f.Add("case10 Outcome lost across the restart");
                            if (back.DefenseScore != 63) f.Add("case10 DefenseScore lost across the restart");
                            if (back.Defender.LayoutHash != "feedfacecafe0001")
                                f.Add("case10 LayoutHash lost across the restart (the redesign signal)");
                            if (!Mathf.Approximately(back.Defender.FrontRadius, 40f))
                                f.Add("case10 the frozen band radii were lost across the restart");
                            if (back.Rows.Count != 1) f.Add("case10 the structure row was lost across the restart");
                            else
                            {
                                var row = back.Rows[0];
                                if (!Mathf.Approximately(row.HoldTimeSeconds, 7.5f))
                                    f.Add("case10 HOLD TIME did not survive the restart -- it is the diagnosis");
                                if (row.Band != DefenseBand.Front) f.Add("case10 the band did not survive the restart");
                                if (row.BreachOrdinal != 1) f.Add("case10 BreachOrdinal did not survive the restart");
                                if (row.State != StructureState.Destroyed) f.Add("case10 State did not survive the restart");
                                if (row.StructureType != "Gate") f.Add("case10 StructureType did not survive the restart");
                            }
                        }
                    }
                }
                finally
                {
                    if (rebooted != null) Object.DestroyImmediate(rebooted);
                }
            }
            catch (System.Exception ex)
            {
                f.Add($"case8/9 threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (gssGo != null) Object.DestroyImmediate(gssGo);
                if (throwaway != null) Object.DestroyImmediate(throwaway);
                if (installed) HeadlessState.TrySetInstance(prior);
                HeadlessState.RestoreSave(hadSave, rawSave);
            }
        }
    }
}
