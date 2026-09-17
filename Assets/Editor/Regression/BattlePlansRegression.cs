// =============================================================================
// BattlePlansRegression [battle-plans] -- WO-1804 guardrails for the TWO plans drops
// that introduce raiding: the wave-2 "Enemy Battle Plans" and the dungeon-boss
// "Bastion Plans".
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
//
// A SIBLING suite, not an extension of CastlePlansUnlockRegression, for the same reason
// the runtime is sibling files: that suite owns the WO-1013 spire contract and reds on
// its own four guardrails. Bolting a second feature's cases into it would mean one red
// cannot tell you which feature broke -- and the file already carries a note about what
// ONE duplicated rule across two suites cost (its crystals-inclusive case).
//
// THE EIGHT CASES:
//   1. THRESHOLD FROM THE CONST -- the camp drop's wave gate is read from
//      BattlePlans.RequiredWavesSurvived, never a literal, and it sits BELOW the spire
//      drop's own threshold (raids are introduced one wave earlier). Both numbers are
//      read from their consts, so a re-tune of either moves one token and this case
//      still holds.
//   2. CAMP DROP TRUTH TABLE -- scheduled at >= the const while unheld; NEVER twice;
//      NEVER while a prop stands; NEVER during a live wave; and SKIPPED FOREVER on a
//      save that has already raided.
//   3. BASTION DROP TRUTH TABLE -- boss down AND the authored dungeon AND unheld AND no
//      prop. Wrong dungeon spawns nothing.
//   4. NEVER RE-LOCK -- ShouldGateBastion refuses the gate on ANY prior contact
//      (claimed or inside a clear cooldown cycle), so a live save that already cleared
//      or captured the Bastion is never re-locked behind a dungeon.
//   5. CTA BRANCHES ON THE BARRACKS -- no Barracks => BuildBarracks; Barracks =>
//      OpenRaidGrid. Pure function, both labels non-empty and ASCII.
//   6. COPY FROM THE PROJECTION -- the beats name the camp's displayName READ FROM
//      scene-configs.json and, when the payout projection resolves, carry
//      RaidSelectionVM's own spoils grammar. The army sentence branches three ways
//      (unknown -> silence, zero -> "raise a Barracks", N -> the live count).
//      ⛔ NOT ONE PAYOUT DIGIT IS WRITTEN IN THIS FILE.
//   7. THE DATA + THE LOCK -- the iron_bastion row authors the plans gate and the ruled
//      dungeon; the other three flagship camps author NONE of it and keep
//      unlockVictories 0; the Resources/StreamingAssets twins are byte-identical; and
//      RaidSelectionVM.ResolveLock turns the gate into the real lock sentence (the one
//      OnCardTapped reads), open when the gate opens.
//   8. THE BOSS-ROOM REVEAL AND ITS BANKED ROUTE (owner ruling 2026-09-16, "keep the
//      reveal in the boss room") -- the Bastion reveal is schedulable in its own dungeon
//      and the camp reveal is not; the two cross-scene latches round-trip and clear
//      exactly once; and a SOURCE LINT proves the moment's files (View, ViewModel AND
//      pickup) call no GoRaid( and load no scene, that the ViewModel is what sets the two
//      latches, that the View only ROUTES the command, and that the dungeon's own exit is
//      the latch's consumer and still raises its Continue/Cancel confirm. A truth table
//      cannot prove those negatives; the lint can.
//
// Marker: BATTLE_PLANS_OK / BATTLE_PLANS_FAIL. Expected: GREEN.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "battle-plans suite", () => { if (!BattlePlansRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[battle-plans] " + r); });
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Village;
using DeNelle.Village.Hero;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class BattlePlansRegression
    {
        private const string CanonPath = "Assets/Resources/Data/Canonical/scene-configs.json";
        private const string StreamPath = "Assets/StreamingAssets/Data/Canonical/scene-configs.json";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- BATTLE PLANS (WO-1804: wave-2 camp plans + dungeon-boss bastion plans) ---");

            Case1_Threshold(failures, log);
            Case2_CampTruthTable(failures, log);
            Case3_BastionTruthTable(failures, log);
            Case4_NeverRelock(failures, log);
            Case5_CtaBranch(failures, log);
            Case6_CopyFromProjection(failures, log);
            Case7_DataAndLock(failures, log);
            Case8_BossRoomRevealAndBankedRoute(failures, log);

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        // =====================================================================
        //  1 -- the threshold is a const, and it is BELOW the spire's
        // =====================================================================
        private static void Case1_Threshold(List<string> failures, StringBuilder log)
        {
            int camp = BattlePlans.RequiredWavesSurvived;
            int spire = CastleDefensePlansService.RequiredWavesSurvived;
            log.AppendLine("  thresholds: camp plans @ " + camp + " waves, spire plans @ " + spire + " waves");

            if (camp <= 0)
                failures.Add("BattlePlans.RequiredWavesSurvived is " + camp +
                             " - a non-positive wave gate would drop the plans before the first wave.");
            if (camp >= spire)
                failures.Add("the camp plans threshold (" + camp + ") is not BELOW the spire plans " +
                             "threshold (" + spire + "). The owner's design is that raiding is " +
                             "introduced one wave BEFORE the spire reward lands, so the player meets " +
                             "the offence verb first. Re-tune one of the two consts, never a literal.");
        }

        // =====================================================================
        //  2 -- the camp drop truth table
        // =====================================================================
        private static void Case2_CampTruthTable(List<string> failures, StringBuilder log)
        {
            int need = BattlePlans.RequiredWavesSurvived;

            Expect(failures, "spawns at exactly the threshold while unheld",
                true, BattlePlans.ShouldSpawnCampDrop(need, false, false, false, false));
            Expect(failures, "spawns above the threshold (persists across later waves until taken)",
                true, BattlePlans.ShouldSpawnCampDrop(need + 3, false, false, false, false));
            Expect(failures, "does NOT spawn below the threshold",
                false, BattlePlans.ShouldSpawnCampDrop(need - 1, false, false, false, false));
            Expect(failures, "does NOT spawn once the plans are HELD (one-shot, forever)",
                false, BattlePlans.ShouldSpawnCampDrop(need + 9, true, false, false, false));
            Expect(failures, "does NOT spawn a SECOND prop while one stands",
                false, BattlePlans.ShouldSpawnCampDrop(need, false, true, false, false));
            Expect(failures, "does NOT spawn DURING A LIVE WAVE",
                false, BattlePlans.ShouldSpawnCampDrop(need, false, false, true, false));
            Expect(failures, "is SKIPPED for a save that has already raided",
                false, BattlePlans.ShouldSpawnCampDrop(need + 5, false, false, false, true));

            log.AppendLine("  camp truth table: 7 shapes pinned against the const (need >= " + need + ")");
        }

        // =====================================================================
        //  3 -- the bastion drop truth table
        // =====================================================================
        private static void Case3_BastionTruthTable(List<string> failures, StringBuilder log)
        {
            Expect(failures, "bastion plans drop when the boss falls in the authored dungeon",
                true, BattlePlans.ShouldSpawnBastionDrop(true, true, false, false));
            Expect(failures, "no drop before the boss falls",
                false, BattlePlans.ShouldSpawnBastionDrop(false, true, false, false));
            Expect(failures, "no drop from a boss in ANOTHER dungeon",
                false, BattlePlans.ShouldSpawnBastionDrop(true, false, false, false));
            Expect(failures, "no second drop once the bastion plans are HELD",
                false, BattlePlans.ShouldSpawnBastionDrop(true, true, true, false));
            Expect(failures, "no second prop while one stands",
                false, BattlePlans.ShouldSpawnBastionDrop(true, true, false, true));

            // The two kinds must never share a persisted flag or a reveal key, or collecting one
            // would silently consume the other.
            if (BattlePlans.PlansIdFor(BattlePlansKind.EnemyCamp) ==
                BattlePlans.PlansIdFor(BattlePlansKind.Bastion))
                failures.Add("both plans kinds resolve to the SAME ProgressionUnlocks id - one " +
                             "pickup would consume the other.");
            if (BattlePlans.RevealSeenKeyFor(BattlePlansKind.EnemyCamp) ==
                BattlePlans.RevealSeenKeyFor(BattlePlansKind.Bastion))
                failures.Add("both plans kinds resolve to the SAME reveal seen key - one reveal " +
                             "would suppress the other.");
            if (BattlePlansPickup.SignalFor(BattlePlansKind.EnemyCamp) ==
                BattlePlansPickup.SignalFor(BattlePlansKind.Bastion))
                failures.Add("both plans kinds raise the SAME tutorial signal - WO-1802's helper " +
                             "chain could not tell the two moments apart.");

            // THE HAND-OFF, pinned by name so the sibling lane's step id cannot drift from ours.
            const string wantBattle = "plans.revealed:battle";
            const string wantBastion = "plans.revealed:bastion";
            if (BattlePlansPickup.SignalFor(BattlePlansKind.EnemyCamp) != wantBattle)
                failures.Add("the camp plans hand-off signal is '" +
                             BattlePlansPickup.SignalFor(BattlePlansKind.EnemyCamp) +
                             "', not '" + wantBattle + "' - WO-1802's helper chain awaits that id.");
            if (BattlePlansPickup.SignalFor(BattlePlansKind.Bastion) != wantBastion)
                failures.Add("the bastion plans hand-off signal is '" +
                             BattlePlansPickup.SignalFor(BattlePlansKind.Bastion) +
                             "', not '" + wantBastion + "'.");

            log.AppendLine("  bastion truth table: 5 shapes + distinct flags/keys/signals; hand-off ids = '" +
                           wantBattle + "' / '" + wantBastion + "'");
        }

        // =====================================================================
        //  4 -- NEVER RE-LOCK a bastion the player has already been to
        // =====================================================================
        private static void Case4_NeverRelock(List<string> failures, StringBuilder log)
        {
            Expect(failures, "a fresh save with no plans and no contact IS gated",
                true, BattlePlans.ShouldGateBastion(false, false, false));
            Expect(failures, "holding the plans opens it",
                false, BattlePlans.ShouldGateBastion(true, false, false));
            Expect(failures, "a save that already CLAIMED (cleared/captured) it is NEVER re-locked",
                false, BattlePlans.ShouldGateBastion(false, true, false));
            Expect(failures, "a save inside a clear cooldown cycle is NEVER re-locked",
                false, BattlePlans.ShouldGateBastion(false, false, true));
            Expect(failures, "plans + prior contact is still open",
                false, BattlePlans.ShouldGateBastion(true, true, true));

            // The sentence must name the remedy, never be a bare "Locked" (the ResolveLock law).
            string s = BattlePlans.BastionLockSentence("The Ember Deep");
            if (string.IsNullOrEmpty(s) || s.IndexOf(BattlePlans.BastionLockPrefix, StringComparison.Ordinal) != 0)
                failures.Add("the plans lock sentence does not start with BastionLockPrefix: '" + s + "'");
            if (s.IndexOf("The Ember Deep", StringComparison.Ordinal) < 0)
                failures.Add("the plans lock sentence does not name WHERE to find them: '" + s + "'");
            string blank = BattlePlans.BastionLockSentence(null);
            if (string.IsNullOrEmpty(blank) || blank.Length <= BattlePlans.BastionLockPrefix.Length)
                failures.Add("an un-named dungeon produces a truncated lock sentence: '" + blank + "'");
            if (!IsAscii(s) || !IsAscii(blank))
                failures.Add("the plans lock sentence is not ASCII (device tofu risk).");

            log.AppendLine("  never-relock: 5 shapes pinned; lock sentence = '" + s + "'");
        }

        // =====================================================================
        //  5 -- the CTA branches on the Barracks, and only on that
        // =====================================================================
        private static void Case5_CtaBranch(List<string> failures, StringBuilder log)
        {
            if (BattlePlans.ResolveCta(false, inHub: true) != BattlePlansCta.BuildBarracks)
                failures.Add("with NO Barracks in a hub the CTA is not BuildBarracks - a player with " +
                             "no barracks would be handed a raid grid that bounces them.");
            if (BattlePlans.ResolveCta(true, inHub: true) != BattlePlansCta.OpenRaidGrid)
                failures.Add("with a Barracks standing the CTA is not OpenRaidGrid.");
            if (BattlePlans.ResolveCta(true, inHub: false) != BattlePlansCta.OpenRaidGrid)
                failures.Add("with a Barracks standing OUTSIDE a hub (the boss room) the CTA is not " +
                             "OpenRaidGrid - the Bastion beat's whole point is the raid door.");
            // THE WO-1542 GUARD: "BUILD YOUR BARRACKS" in a boss room is a word whose door cannot
            // open (build mode is a town verb). Outside a hub it must become the way home.
            if (BattlePlans.ResolveCta(false, inHub: false) != BattlePlansCta.ReturnToCastle)
                failures.Add("with NO Barracks OUTSIDE a hub the CTA is not ReturnToCastle - a " +
                             "BUILD YOUR BARRACKS face in the boss room is a label whose door " +
                             "cannot open, which is the WO-1542 defect.");
            string home = BattlePlans.CtaLabel(BattlePlansCta.ReturnToCastle, null);
            if (string.IsNullOrEmpty(home) || !IsAscii(home) ||
                home.IndexOf("BARRACKS", StringComparison.Ordinal) >= 0)
                failures.Add("the ReturnToCastle label is empty, non-ASCII, or still mentions the " +
                             "Barracks: '" + home + "'");

            string build = BattlePlans.CtaLabel(BattlePlansCta.BuildBarracks, "The Forsaken Camp");
            string raid = BattlePlans.CtaLabel(BattlePlansCta.OpenRaidGrid, "The Forsaken Camp");
            if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(raid) || build == raid)
                failures.Add("the two CTA labels are empty or identical ('" + build + "' / '" + raid + "').");
            if (raid.IndexOf("THE FORSAKEN CAMP", StringComparison.Ordinal) < 0)
                failures.Add("the raid CTA label does not name the camp: '" + raid + "'");
            if (!IsAscii(build) || !IsAscii(raid))
                failures.Add("a CTA label is not ASCII (device tofu risk).");
            // A null camp name must still produce a usable face, never "RAID ".
            string fallback = BattlePlans.CtaLabel(BattlePlansCta.OpenRaidGrid, null);
            if (string.IsNullOrEmpty(fallback) || fallback.Trim().EndsWith("RAID", StringComparison.Ordinal))
                failures.Add("an un-named camp produces a dangling CTA label: '" + fallback + "'");

            log.AppendLine("  cta: hub/no-barracks -> '" + build + "'; barracks -> '" + raid +
                           "'; dungeon/no-barracks -> '" + home + "'");
        }

        // =====================================================================
        //  6 -- the copy is COMPOSED, never literal
        // =====================================================================
        private static void Case6_CopyFromProjection(List<string> failures, StringBuilder log)
        {
            // The camp name is READ from scene-configs.json, and this suite never spells it.
            // Invalidate first, the RaidEscalationRegression / RaidSceneCoverageRegression
            // precedent: these cases read the REAL rows, so the loader must not serve a cache
            // built before this run touched the JSON.
            SceneConfigCatalog.Invalidate();
            var campDef = SceneConfigCatalog.Find(BattlePlans.CampConfigId);
            if (campDef == null || string.IsNullOrEmpty(campDef.displayName))
            {
                failures.Add("scene-configs.json has no displayName for '" + BattlePlans.CampConfigId +
                             "' - the reveal cannot name where the raid is.");
                return;
            }
            string campName = campDef.displayName;

            // The spoils line comes from the SETTLE's own formula through the row's projection.
            string spoils = RaidSelectionVM.FormatSpoils(RaidSelectionVM.EstimateSpoils(campDef));
            if (!string.IsNullOrEmpty(spoils) &&
                spoils.IndexOf(RaidSelectionVM.SpoilsPrefix, StringComparison.Ordinal) != 0)
                failures.Add("the spoils projection no longer uses RaidSelectionVM.SpoilsPrefix ('" +
                             spoils + "') - the reveal and the raid card would word the payout " +
                             "differently.");

            // THE ARMY SENTENCE BRANCHES THREE WAYS.
            if (BattlePlans.ArmySentence(-1) != null)
                failures.Add("an UNKNOWN army count still produces a sentence - a headless or " +
                             "pre-state frame must never print a roster it cannot prove.");
            string zero = BattlePlans.ArmySentence(0);
            if (string.IsNullOrEmpty(zero))
                failures.Add("a ZERO army produces no sentence - the beat would silently promise " +
                             "nothing before a BUILD YOUR BARRACKS CTA.");
            else if (zero.IndexOf("Barracks", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("the zero-army sentence does not point at the Barracks: '" + zero + "'");
            string one = BattlePlans.ArmySentence(1);
            string many = BattlePlans.ArmySentence(4);
            if (string.IsNullOrEmpty(one) || string.IsNullOrEmpty(many) || one == many)
                failures.Add("the army sentence does not vary with the live count ('" + one +
                             "' / '" + many + "').");
            if (one.IndexOf(" soldiers", StringComparison.Ordinal) >= 0)
                failures.Add("a one-soldier army is pluralised: '" + one + "'");

            foreach (var kind in new[] { BattlePlansKind.EnemyCamp, BattlePlansKind.Bastion })
            {
                string name = BattlePlans.CampDisplayName(kind);
                var beats = BattlePlans.BuildBeats(kind, name, spoils, many);
                if (beats == null || beats.Count < 3)
                {
                    failures.Add("kind=" + kind + " composes " +
                                 (beats == null ? "no" : beats.Count.ToString()) + " beats; expected 3.");
                    continue;
                }
                var all = new StringBuilder();
                bool anySpeaker = false;
                for (int i = 0; i < beats.Count; i++)
                {
                    if (string.IsNullOrEmpty(beats[i].Text))
                        failures.Add("kind=" + kind + " beat " + (i + 1) + " is empty.");
                    if (beats[i].HoldSeconds <= 0f)
                        failures.Add("kind=" + kind + " beat " + (i + 1) + " holds for " +
                                     beats[i].HoldSeconds + "s - a non-positive hold flashes past.");
                    if (!IsAscii(beats[i].Text))
                        failures.Add("kind=" + kind + " beat " + (i + 1) + " is not ASCII: '" +
                                     beats[i].Text + "'");
                    anySpeaker |= beats[i].Speaker;
                    all.Append(beats[i].Text).Append(' ');
                }
                string body = all.ToString();
                if (body.IndexOf(name, StringComparison.Ordinal) < 0)
                    failures.Add("kind=" + kind + " never names the target ('" + name +
                                 "') - the reveal must say WHERE it is.");
                if (!string.IsNullOrEmpty(spoils) && kind == BattlePlansKind.EnemyCamp &&
                    body.IndexOf(RaidSelectionVM.SpoilsPrefix, StringComparison.Ordinal) < 0)
                    failures.Add("kind=" + kind + " never says WHAT IT PAYS even though the " +
                                 "projection resolved '" + spoils + "'.");
                if (body.IndexOf(many, StringComparison.Ordinal) < 0)
                    failures.Add("kind=" + kind + " never says the army is ready.");
                if (!anySpeaker)
                    failures.Add("kind=" + kind + " has no Echo-spoken beat - the call to arms is " +
                                 "unattributed by construction.");
            }

            log.AppendLine("  copy: camp='" + campName + "' spoils=" +
                           (string.IsNullOrEmpty(spoils) ? "<projection empty>" : "'" + spoils + "'") +
                           " (no payout digit is written in this suite); army sentence branches 3 ways");
        }

        // =====================================================================
        //  7 -- the DATA and the REAL lock
        // =====================================================================
        private static void Case7_DataAndLock(List<string> failures, StringBuilder log)
        {
            // -- twins byte-identical (the canon rule for every Canonical/StreamingAssets pair) --
            if (!File.Exists(CanonPath) || !File.Exists(StreamPath))
            {
                failures.Add("scene-configs.json is missing from " +
                             (File.Exists(CanonPath) ? StreamPath : CanonPath));
            }
            else
            {
                byte[] a = File.ReadAllBytes(CanonPath), b = File.ReadAllBytes(StreamPath);
                bool same = a.Length == b.Length;
                for (int i = 0; same && i < a.Length; i++) same = a[i] == b[i];
                if (!same)
                    failures.Add("scene-configs.json Resources and StreamingAssets copies are NOT " +
                                 "byte-identical (" + a.Length + " vs " + b.Length + " bytes) - the " +
                                 "build reads Resources and only one of them carries the plans gate.");
                else
                    log.AppendLine("  twins byte-identical (" + a.Length + " bytes)");
            }

            // -- the bastion row authors the gate and the ruled dungeon --
            SceneConfigCatalog.Invalidate();
            var bastion = SceneConfigCatalog.Find(BattlePlans.BastionConfigId);
            if (bastion == null)
            {
                failures.Add("scene-configs.json has no '" + BattlePlans.BastionConfigId + "' row.");
                return;
            }
            if (bastion.unlockedByPlans != BattlePlans.BastionPlansId)
                failures.Add("the '" + BattlePlans.BastionConfigId + "' row authors unlockedByPlans='" +
                             bastion.unlockedByPlans + "', not BattlePlans.BastionPlansId ('" +
                             BattlePlans.BastionPlansId + "') - the gate and the pickup's flag would " +
                             "be different strings and the plans would unlock nothing.");
            if (bastion.plansDungeonId != BattlePlansService.DefaultBastionPlansDungeonId)
                failures.Add("the '" + BattlePlans.BastionConfigId + "' row authors plansDungeonId='" +
                             bastion.plansDungeonId + "'. OWNER RULING 2026-09-16: \"Make it the one " +
                             "that's the ember deep\" -> '" +
                             BattlePlansService.DefaultBastionPlansDungeonId + "'.");
            if (string.IsNullOrEmpty(bastion.plansDungeonName))
                failures.Add("the '" + BattlePlans.BastionConfigId + "' row authors no " +
                             "plansDungeonName - the locked card would name a raw scene id.");
            log.AppendLine("  iron_bastion row: unlockedByPlans='" + bastion.unlockedByPlans +
                           "' plansDungeonId='" + bastion.plansDungeonId +
                           "' plansDungeonName='" + bastion.plansDungeonName + "'");

            // -- the other three flagship camps are UNTOUCHED --
            foreach (var id in new[] { "raider_camp_small", "fortified_garrison", "mage_enclave" })
            {
                var d = SceneConfigCatalog.Find(id);
                if (d == null) { failures.Add("scene-configs.json has no '" + id + "' row."); continue; }
                if (!string.IsNullOrEmpty(d.unlockedByPlans))
                    failures.Add("'" + id + "' now authors unlockedByPlans='" + d.unlockedByPlans +
                                 "'. WO-1804 gates the Iron Bastion ONLY; the other three camps stay " +
                                 "open (the WO-1705 ruling of 2026-09-11 left them at unlockVictories 0).");
                if (d.unlockVictories != 0)
                    failures.Add("'" + id + "' authors unlockVictories=" + d.unlockVictories +
                                 ", not 0 - WO-1804 changes no win-count rung.");
            }

            // -- the REAL lock: ResolveLock turns the gate into the sentence OnCardTapped reads --
            var savedGate = RaidSelectionVM.PlansGateProvider;
            var savedNamer = RaidSelectionVM.PlansDungeonNameProvider;
            try
            {
                var defs = new List<SceneConfigDef>
                {
                    new SceneConfigDef
                    {
                        id = "plans_gated_probe", displayName = "Probe Bastion",
                        sceneName = "RaidBase_probe", ownership = "Enemy", unlockVictories = 0,
                        unlockedByPlans = "probe_plans", plansDungeonName = "The Probe Deep",
                        rewardMultiplier = 1f,
                    },
                    new SceneConfigDef
                    {
                        id = "ungated_probe", displayName = "Probe Camp",
                        sceneName = "RaidBase_probe2", ownership = "Enemy", unlockVictories = 0,
                        rewardMultiplier = 1f,
                    },
                };

                RaidSelectionVM.PlansDungeonNameProvider = null;   // fall back to the authored row

                RaidSelectionVM.PlansGateProvider = (id, plansId) =>
                {
                    // PIN THE SECOND ARGUMENT: the AUTHORED plans id must reach the host, or the
                    // data's `unlockedByPlans` string is decorative and the real flag name lives
                    // in code (the dead-copy shape). A wrong/blank id here reds the case.
                    if (plansId != "probe_plans")
                        failures.Add("the plans gate was handed plansId='" + plansId + "' for camp '" +
                                     id + "' - ResolveLock must pass the row's AUTHORED " +
                                     "unlockedByPlans value, or the host has to hardcode the flag name.");
                    return true;
                };
                using (var vm = new RaidSelectionVM(defs, null, 0, _ => true))
                {
                    string gatedReason = vm.LockReasonFor("plans_gated_probe");
                    if (string.IsNullOrEmpty(gatedReason))
                        failures.Add("a plans-gated camp is OPEN in RaidSelectionVM - the gate is " +
                                     "display-only, which is the WO-1542 defect (a locked word under " +
                                     "a lit door).");
                    else if (gatedReason.IndexOf(BattlePlans.BastionLockPrefix, StringComparison.Ordinal) != 0)
                        failures.Add("a plans-gated camp is locked with the wrong sentence: '" +
                                     gatedReason + "'");
                    else if (gatedReason.IndexOf("The Probe Deep", StringComparison.Ordinal) < 0)
                        failures.Add("the plans lock sentence does not read the authored " +
                                     "plansDungeonName: '" + gatedReason + "'");
                    if (!string.IsNullOrEmpty(vm.LockReasonFor("ungated_probe")))
                        failures.Add("a row that authors NO unlockedByPlans was locked by the plans " +
                                     "gate ('" + vm.LockReasonFor("ungated_probe") + "') - every " +
                                     "existing camp must be untouched.");
                }

                RaidSelectionVM.PlansGateProvider = (_, __) => false;    // plans held / prior contact
                using (var vm = new RaidSelectionVM(defs, null, 0, _ => true))
                {
                    string open = vm.LockReasonFor("plans_gated_probe");
                    if (!string.IsNullOrEmpty(open))
                        failures.Add("a camp whose plans gate is OPEN is still locked: '" + open + "'");
                }

                RaidSelectionVM.PlansGateProvider = null;          // unwired must FAIL CLOSED
                using (var vm = new RaidSelectionVM(defs, null, 0, _ => true))
                {
                    if (string.IsNullOrEmpty(vm.LockReasonFor("plans_gated_probe")))
                        failures.Add("with the plans gate UNWIRED a gated camp reads OPEN - a frame " +
                                     "that cannot prove the plans are held must not hand over the " +
                                     "target (the VictoryCountProvider polarity).");
                }

                RaidSelectionVM.PlansGateProvider = (_, __) => throw new InvalidOperationException("probe");
                using (var vm = new RaidSelectionVM(defs, null, 0, _ => true))
                {
                    if (string.IsNullOrEmpty(vm.LockReasonFor("plans_gated_probe")))
                        failures.Add("a THROWING plans gate reads OPEN - the probe must fail closed " +
                                     "and say so, never swallow (section 12).");
                }

                log.AppendLine("  lock: gated -> the plans sentence; open -> null; unwired + throwing -> " +
                               "fail CLOSED; an un-authored row is untouched");
            }
            finally
            {
                RaidSelectionVM.PlansGateProvider = savedGate;
                RaidSelectionVM.PlansDungeonNameProvider = savedNamer;
            }
        }

        // =====================================================================
        //  8 -- THE REVEAL LIVES IN THE BOSS ROOM, AND ITS ROUTE IS THE BANKED EXIT
        // =====================================================================
        //  OWNER RULING 2026-09-16, verbatim: "keep the reveal in the boss room".
        //
        //  Two halves, and the second matters most: the CTA must reach the raid grid WITHOUT
        //  loading a scene from the dungeon. DungeonExitInteractable.ExecuteLeave is the only
        //  path that banks the run's crafting scatter (via DungeonController.ExitToVillage), so
        //  a SceneRouter.GoRaid from the boss room would silently bin everything the player
        //  carried down. This case pins the scheduling predicate, the two latches, and - by
        //  SOURCE LINT - the absence of any raid-route call in the files that own the moment.
        //  The lint is deliberate: a truth table cannot prove a NEGATIVE about which method a UI
        //  button calls, and "just open it directly" is exactly the well-meaning edit that would
        //  re-introduce this.
        private static void Case8_BossRoomRevealAndBankedRoute(List<string> failures, StringBuilder log)
        {
            string dungeon = BattlePlansService.DefaultBastionPlansDungeonId;

            // -- WHERE each reveal may play --
            Expect(failures, "the BASTION reveal plays IN ITS OWN DUNGEON (the boss room)",
                true, BattlePlansService.RevealPlayableIn(
                    BattlePlansKind.Bastion, dungeon, isHub: false, isPlansDungeon: true));
            // ⛔ THE HUB IS RESOLVED, NEVER TYPED, AND EVERY CANDIDATE IS CHECKED.
            // hub-scene-literal (HubSceneLiteralRegression) caught a hardcoded hub name on this very
            // line on 2026-09-16 and it was right to: a typed-in hub goes stale SILENTLY, and a
            // stale gate reports OK while watching a scene the player never loads - the way
            // UICaptureMode, TowerRespawnRegression and FloorDeepDiag were all pinned to the retired
            // hub at once.
            // ⚠ AND IT ITERATES CastleCandidates RATHER THAN READING SceneRouter.Castle, the same
            // choice WO-1765 made in CameraRaidFramingRegression and for the same reason: `Castle`
            // resolves only the branch ff.MergedWorld happens to be flagged into, so it would prove
            // this for one hub and leave the other unguarded. The claim here must hold for BOTH
            // ("a save that walked out of the dungeon still gets its reveal at home, in either
            // configuration"), and index [1] - the legacy MainCastle_Hall, still on disk per
            // CLAUDE.md sec.7 - must satisfy it just as much as [0]. Iterating is strictly stronger
            // and carries no false failure.
            foreach (string hub in DeNelle.Core.SceneRouter.CastleCandidates)
                Expect(failures, "the BASTION reveal also plays in hub '" + hub +
                    "' (a save that walked out before seeing it)",
                    true, BattlePlansService.RevealPlayableIn(
                        BattlePlansKind.Bastion, hub, isHub: true, isPlansDungeon: false));
            Expect(failures, "the CAMP reveal NEVER plays in a dungeon - it is a town beat",
                false, BattlePlansService.RevealPlayableIn(
                    BattlePlansKind.EnemyCamp, dungeon, isHub: false, isPlansDungeon: true));
            Expect(failures, "no reveal plays in a raid scene",
                false, BattlePlansService.RevealPlayableIn(
                    BattlePlansKind.Bastion, "RaidBase_IronBastion", isHub: false, isPlansDungeon: false));

            // -- THE TWO LATCHES: written, observable, consumed exactly once --
            BattlePlans.ClearPendingRequests();
            try
            {
                if (BattlePlans.HasPendingDungeonExit || BattlePlans.HasPendingRaidGrid)
                    failures.Add("the plans latches are not clean after ClearPendingRequests.");

                BattlePlans.RequestDungeonExit("regression");
                BattlePlans.RequestRaidGridOnHubArrival(BattlePlans.BastionConfigId);
                if (!BattlePlans.HasPendingDungeonExit)
                    failures.Add("RequestDungeonExit did not latch - the CTA in the boss room would " +
                                 "leave the player standing there.");
                if (!BattlePlans.HasPendingRaidGrid)
                    failures.Add("RequestRaidGridOnHubArrival did not latch - the player would be " +
                                 "carried home and shown nothing.");

                if (BattlePlans.ConsumeDungeonExitRequest() != "regression")
                    failures.Add("the dungeon-exit latch did not round-trip its reason.");
                if (BattlePlans.HasPendingDungeonExit)
                    failures.Add("the dungeon-exit latch was not CLEARED by Consume - every exit in " +
                                 "the dungeon would raise its own confirm.");
                if (BattlePlans.ConsumeDungeonExitRequest() != null)
                    failures.Add("a second Consume of the dungeon-exit latch returned a request.");

                if (BattlePlans.ConsumeRaidGridRequest() != BattlePlans.BastionConfigId)
                    failures.Add("the raid-grid latch did not round-trip its camp id.");
                if (BattlePlans.HasPendingRaidGrid)
                    failures.Add("the raid-grid latch was not CLEARED by Consume - the grid would " +
                                 "re-open on every later hub entry.");
            }
            finally { BattlePlans.ClearPendingRequests(); }

            // -- SOURCE LINT: no raid route may be reachable from the moment's own files --
            const string revealPath = "Assets/_Modules/Village/Progression/BattlePlansReveal.cs";
            const string vmPath = "Assets/_Modules/Village/Progression/BattlePlansRevealVM.cs";
            string[] owned = { revealPath, vmPath, "Assets/_Modules/Village/Progression/BattlePlansPickup.cs" };
            foreach (var path in owned)
            {
                if (!File.Exists(path)) { failures.Add("source lint: missing " + path); continue; }
                string code = File.ReadAllText(path);
                // ⛔ THE TRAILING "(" IS LOAD-BEARING: MATCH A CALL, NOT THE WORD.
                //    The bare token `GoRaid` appears in BattlePlansReveal.cs AND in
                //    BattlePlansRevealVM.cs -- in the comments that explain why it must never be
                //    called, and INSIDE RUNTIME FlowTrace STRINGS. A word-match lint therefore
                //    fails on the very file whose correctness it is asserting, and (worse) it would
                //    pressure the next author to DELETE the explanation to make the gate green.
                //    Comment-stripping would not have saved it either, because one occurrence is in
                //    a string literal - and a hand-rolled comment/string scanner is precisely the
                //    trap CLAUDE.md section 1 records for CompileGate.BraceBalanced.
                //    A call always carries its parenthesis; prose never does. Do NOT "tighten" this
                //    back to the bare token.
                if (code.IndexOf("GoRaid(", StringComparison.Ordinal) >= 0)
                    failures.Add("source lint: " + path + " CALLS GoRaid(. A raid load from the boss " +
                                 "room bypasses DungeonExitInteractable.ExecuteLeave -> " +
                                 "DungeonController.ExitToVillage and silently bins the run's " +
                                 "crafting scatter. The CTA must latch and let the dungeon's own " +
                                 "exit carry the player home.");
                if (code.IndexOf("LoadSceneWithFade(", StringComparison.Ordinal) >= 0 ||
                    code.IndexOf("SceneManager.LoadScene(", StringComparison.Ordinal) >= 0)
                    failures.Add("source lint: " + path + " loads a scene directly - the same " +
                                 "banking bypass by another name.");
            }

            // SOMETHING must ask to leave, or the boss-room CTA is a dead button.
            //
            // ⚠ THIS ASSERTS THE VIEWMODEL, NOT THE VIEW, AND THAT MOVED ON 2026-09-16.
            // UiMvvmConformanceRegression failed the reveal as a NEW state-reading View, so every
            // state read AND the CTA command moved into BattlePlansRevealVM; the View now only
            // routes the tap (vm.InvokeCta). Had this lint kept pointing at the View it would have
            // gone red on a correct MVVM split - a gate punishing the fix for the gate next to it.
            // The pair of lints is therefore deliberate: MVVM owns WHERE the logic lives, this owns
            // WHAT it is allowed to do.
            string vm = File.Exists(vmPath) ? File.ReadAllText(vmPath) : "";
            if (vm.IndexOf("RequestDungeonExit", StringComparison.Ordinal) < 0)
                failures.Add("source lint: the reveal VM never calls RequestDungeonExit - its " +
                             "boss-room CTA cannot get the player home.");
            if (vm.IndexOf("RequestRaidGridOnHubArrival", StringComparison.Ordinal) < 0)
                failures.Add("source lint: the reveal VM never calls RequestRaidGridOnHubArrival - " +
                             "the raid door is lost across the scene load.");

            // And the View must stay a dumb skin: it ROUTES the command, it does not perform it.
            string reveal = File.Exists(revealPath) ? File.ReadAllText(revealPath) : "";
            if (reveal.IndexOf("InvokeCta()", StringComparison.Ordinal) < 0)
                failures.Add("source lint: the reveal View no longer routes its CTA through the " +
                             "ViewModel - the tap is wired to nothing, or the doors were re-inlined " +
                             "into the View (which UiMvvmConformanceRegression forbids).");

            // And the dungeon exit MUST be the consumer, so the banked path is the one that runs.
            const string exitPath = "Assets/_Modules/Dungeons/DungeonExitInteractable.cs";
            if (!File.Exists(exitPath))
                failures.Add("source lint: missing " + exitPath);
            else
            {
                string exit = File.ReadAllText(exitPath);
                if (exit.IndexOf("ConsumeDungeonExitRequest", StringComparison.Ordinal) < 0)
                    failures.Add("source lint: DungeonExitInteractable never consumes the exit latch - " +
                                 "the boss-room CTA would latch a request nothing honours, and the " +
                                 "player would be stuck in the dungeon.");
                if (exit.IndexOf("RequestExitConfirm();", StringComparison.Ordinal) < 0)
                    failures.Add("source lint: the claimed exit request no longer raises the ordinary " +
                                 "Continue/Cancel confirm - a request must never skip the player's " +
                                 "own confirm, and must never skip ExecuteLeave's banking.");
            }

            log.AppendLine("  boss-room reveal: scheduled in '" + dungeon + "' for the Bastion kind " +
                           "(camp kind town-only); CTA route = latch -> the dungeon's banked exit " +
                           "confirm -> grid on the first hub frame; source lint proves no GoRaid / " +
                           "no direct scene load in the moment's files");
        }

        // =====================================================================
        //  helpers
        // =====================================================================
        private static void Expect(List<string> failures, string what, bool want, bool got)
        {
            if (want != got)
                failures.Add(what + ": expected " + want + ", got " + got);
        }

        private static bool IsAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            for (int i = 0; i < s.Length; i++) if (s[i] > 126) return false;
            return true;
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "BATTLE_PLANS_OK");
                return "BATTLE PLANS OK -- camp plans scheduled off the const at the wave-clear beat " +
                       "(one-shot, never mid-wave, skipped once raided); bastion plans off the authored " +
                       "dungeon's boss; the Bastion gate never re-locks a camp the player has been to; " +
                       "the CTA branches on the Barracks; every word is composed from the projection " +
                       "and the live roster";
            }
            string reason = "battle-plans: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "BATTLE_PLANS_FAIL: " + reason);
            return reason;
        }
    }
}
