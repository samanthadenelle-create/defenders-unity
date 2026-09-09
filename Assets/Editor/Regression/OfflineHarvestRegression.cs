// =============================================================================
// OfflineHarvestRegression — headless oracle for the WO-115 offline-accrual clock:
// cap clamp, backwards-clock (anti-tamper) monotonic guard, and advance-even-on-zero
// (the anti-double-grant contract).
// -----------------------------------------------------------------------------
// Drives the REAL OfflineHarvestService.ClaimAccrual() against a live GameStateService
// with a CONTROLLED GameState.LastHarvestClaimMs, asserting from data (no sources are
// present in a headless editor, so every haul is 0 — exactly what isolates the CLOCK
// logic from the accrual sources):
//   1. FRESH SAVE (LastHarvestClaimMs<=0) → seeds the clock to now, banks nothing (None),
//      and advances the clock forward (no giant retroactive first-claim).
//   2. LONG ABSENCE (20h with a 10h cap) → result.WasCapped == true, AwaySeconds >= cap,
//      Total == 0 (no sources), and the clock advanced to ~now (window never re-claimable).
//   3. BACKWARDS CLOCK (last set to the FUTURE) → elapsed clamps to 0 (no throw, no
//      negative haul) and the clock is re-stamped to now (monotonic anti-tamper guard).
//
// SAFETY: snapshots the raw PlayerPrefs save blob + restores it (svc.Load()) in a finally,
// and DestroyImmediate's the throwaway service, so the real save/state is untouched.
// Mirrors MonetizationCovenantRegression: public static bool Run(out string reason).
// =============================================================================
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class OfflineHarvestRegression
    {
        private const string SaveKey = "dotr-save";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            // Snapshot the persisted save so nothing we mutate here survives the oracle.
            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;

            // HEADLESS STATE INSTALL — editmode batchmode NEVER runs GameStateService.Awake
            // (Awake fires only in play mode / on ExecuteAlways), so a bare
            // AddComponent<GameStateService>() leaves Instance + State null — the exact cause of
            // the historic false-FAIL "no GameStateService/State available". Mirror
            // CoreSaveContractRegression: construct a THROWAWAY GameState SO and install it as the
            // active state for the duration by setting the private static _instance + the
            // [SerializeField] _state via reflection, restoring the prior live service in finally.
            GameStateService priorInstance = GameStateService.Instance;
            GameObject svcGo = null, gssGo = null;
            GameState throwaway = null;
            bool installed = false;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();   // fresh defaults; all collections init'd → Save()-safe
                gssGo = new GameObject("GameStateService (harvest-oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!TryInstallHeadlessState(gss, throwaway, out string installErr))
                {
                    // The GameStateService singleton/state seam moved — genuinely unrunnable
                    // headless. NAMED SKIP (return true), never a false FAIL (harness-integrity).
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "OFFLINE HARVEST", "needs fleet -- " + installErr);
                }
                installed = true;
                var state = gss.State;   // the throwaway — never null now
                if (state == null)
                {
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "OFFLINE HARVEST", "needs fleet -- throwaway state did not install");
                }

                svcGo = new GameObject("OfflineHarvestService (oracle)");
                var svc = svcGo.AddComponent<OfflineHarvestService>();   // ClaimAccrual reads GameStateService.Instance directly (no Awake needed)
                svc.OfflineCapHours = 10f;   // deterministic cap for case 2

                double capSeconds = 10.0 * 3600.0;

                // --- Case 1: fresh save (clock 0) seeds forward, banks nothing ---
                state.LastHarvestClaimMs = 0;
                double before1 = state.LastHarvestClaimMs;
                var r1 = svc.ClaimAccrual();
                if (r1 == null) failures.Add("case1 fresh save returned null result");
                else if (r1.Total != 0) failures.Add($"case1 fresh save banked {r1.Total} (should seed clock + bank nothing)");
                if (state.LastHarvestClaimMs <= before1)
                    failures.Add($"case1 fresh save did NOT advance the clock (still {state.LastHarvestClaimMs})");

                // --- Case 2: 20h absence, 10h cap → WasCapped, Total 0, clock advanced ---
                double now2 = DeNelle.Village.TimeSource.NowUnixMs();
                double away20hMs = 20.0 * 3600.0 * 1000.0;
                state.LastHarvestClaimMs = now2 - away20hMs;
                double before2 = state.LastHarvestClaimMs;
                var r2 = svc.ClaimAccrual();
                if (r2 == null) { failures.Add("case2 returned null result"); }
                else
                {
                    if (!r2.WasCapped) failures.Add($"case2 20h absence vs 10h cap: WasCapped should be true (AwaySeconds={r2.AwaySeconds:0})");
                    if (r2.AwaySeconds < capSeconds) failures.Add($"case2 AwaySeconds {r2.AwaySeconds:0} < cap {capSeconds:0} (elapsed mis-measured)");
                    if (r2.Total != 0) failures.Add($"case2 banked {r2.Total} with NO sources present (phantom haul)");
                }
                if (state.LastHarvestClaimMs <= before2)
                    failures.Add("case2 did not advance the claim clock (window would be re-claimable → double-grant)");

                // --- Case 3: backwards clock (future) → clamp to 0, no throw, re-stamp now ---
                double now3 = DeNelle.Village.TimeSource.NowUnixMs();
                double future = now3 + 3600.0 * 1000.0;   // 1h in the future
                state.LastHarvestClaimMs = future;
                OfflineHarvestResult r3 = null;
                try { r3 = svc.ClaimAccrual(); }
                catch (System.Exception ex) { failures.Add($"case3 backwards clock THREW: {ex.GetType().Name}: {ex.Message}"); }
                if (r3 != null)
                {
                    if (r3.Total != 0) failures.Add($"case3 backwards clock banked {r3.Total} (negative-delta not clamped)");
                    if (r3.WasCapped) failures.Add("case3 backwards clock reported WasCapped (elapsed should clamp to 0, not cap)");
                }
                if (state.LastHarvestClaimMs > future)
                    failures.Add($"case3 clock left in the future ({state.LastHarvestClaimMs}) — monotonic guard did not re-stamp to now");

                // =====================================================================
                //  Case 4 [one-row-per-resource] (WO-1434) -- THE MOVED PIN. READ THIS.
                // ---------------------------------------------------------------------
                //  WHAT THIS CASE USED TO BE, and why moving it was required rather than
                //  optional. WO-1392 pinned the exact string
                //      "Storage nearly full - 414 wood will wait"
                //  and pinned WelcomeBackPopup for `PredictCollectWaits(_result)` +
                //  `AddCollectWaitRows(body, ref y)`. Those three pins together REQUIRED the
                //  screen to keep drawing a separate warning line under each waiting row --
                //  which is precisely the duplication the owner reported on 2026-09-06
                //  ("this screen too. Way too much here"): six lines for three facts, each
                //  integer printed twice. A pin that requires the old copy is a pin that
                //  forbids the fix, so it is MOVED, deliberately, in the same edit as the copy.
                //
                //  ⭐ WHAT SURVIVES, because it was the real assertion and it is still true:
                //  the screen must predict the bank's behaviour BEFORE the tap, from a PURE
                //  function taking a headroom reader, in rail order, with no false alarm at
                //  unlimited headroom, in ASCII. Every one of those is re-asserted below
                //  against OfflineHarvestService.BuildReturnRows.
                //  ⛔ WHAT DOES NOT SURVIVE: the requirement that the prediction be drawn as
                //  its OWN line. It is now the right-hand column of the row it describes.
                //
                //  RED PROOF (measured, not asserted): against the pre-WO-1434 tree this case
                //  does not compile -- BuildReturnRows / ReturnRowLabel / ReturnRowDestiny /
                //  OfflineHarvestResult.SiloPending did not exist. Against the old BEHAVIOUR,
                //  the sub-case marked [no-gain-without-headroom] is the one that fails on
                //  today's build: the old popup drew "+" + line.Pending for every row
                //  unconditionally (WelcomeBackPopup:280, `"+" + line.Pending`), so a row with
                //  zero headroom rendered "WOOD WAITING +10609" -- a gain sign on an amount
                //  that banked nothing. The owner tapped COLLECT on 42,782 of those and banked
                //  0 ([Flow:Eco] Grant +W0 +I0, device 2026-09-06 12:51:25).
                // =====================================================================
                var r4 = new OfflineHarvestResult();
                r4.PendingCollectors.Add(new OfflineHarvestResult.OfflineCollectorLine { Resource = "Wood", Pending = 672, Collectors = 1 });
                r4.PendingCollectors.Add(new OfflineHarvestResult.OfflineCollectorLine { Resource = "Iron", Pending = 403, Collectors = 1 });
                r4.PendingCollectors.Add(new OfflineHarvestResult.OfflineCollectorLine { Resource = "Stone", Pending = 874, Collectors = 1 });
                r4.PendingCollectorTotal = 1949;
                r4.PendingCollectorCount = 3;

                System.Func<DeNelle.Village.Buildings.Progression.HarvestResource, int> headroom4 = res =>
                {
                    switch (res)
                    {
                        case DeNelle.Village.Buildings.Progression.HarvestResource.Wood: return 258;
                        case DeNelle.Village.Buildings.Progression.HarvestResource.Iron: return 5000;
                        case DeNelle.Village.Buildings.Progression.HarvestResource.Food: return 0;
                        default: return int.MaxValue;
                    }
                };

                var rows4 = OfflineHarvestService.BuildReturnRows(r4, headroom4);
                if (rows4 == null || rows4.Count != 3)
                    failures.Add($"case4 [one-row-per-resource] expected 3 rows (wood/iron/stone), got {(rows4 == null ? -1 : rows4.Count)}");
                else
                {
                    // Rail order, and the same arithmetic the retired PredictCollectWaits pinned:
                    // wood 672 against headroom 258 waits 414; stone 874 against 0 waits all 874.
                    if (rows4[0].Word != "Wood" || rows4[0].Pending != 672 || rows4[0].Banks != 258 || rows4[0].Waits != 414)
                        failures.Add($"case4 [one-row-per-resource] row 0 = {rows4[0].Word} pending {rows4[0].Pending} " +
                                     $"banks {rows4[0].Banks} waits {rows4[0].Waits}; expected Wood/672/258/414");
                    if (rows4[1].Word != "Iron" || rows4[1].Waits != 0 || rows4[1].Banks != 403)
                        failures.Add($"case4 [one-row-per-resource] row 1 = {rows4[1].Word} banks {rows4[1].Banks} " +
                                     $"waits {rows4[1].Waits}; expected Iron banking all 403");
                    if (rows4[2].Word != "Stone" || rows4[2].Banks != 0 || rows4[2].Waits != 874)
                        failures.Add($"case4 [one-row-per-resource] row 2 = {rows4[2].Word} banks {rows4[2].Banks} " +
                                     $"waits {rows4[2].Waits}; expected Stone/0/874 (Stone is the Food slot, rail order Wood/Iron/Stone)");

                    // --- [no-gain-without-headroom] -- THE ACCEPTANCE ASSERTION -------
                    // No row may present an uncollectable amount as a gain. The "+" is the
                    // gain sign on this screen; a row that banks nothing may not wear one.
                    if (OfflineHarvestService.ReturnRowLabel(rows4[0]) != "WOOD +258")
                        failures.Add($"case4 [no-gain-without-headroom] the partial-room row reads " +
                                     $"'{OfflineHarvestService.ReturnRowLabel(rows4[0])}', expected 'WOOD +258'. THE PLUS-NUMBER " +
                                     "MUST BE THE COLLECTABLE AMOUNT, NEVER THE PENDING ONE - '+672' is a promise the tap " +
                                     "cannot keep, and that substitution IS WO-1434 D1 at smaller scale");
                    if (OfflineHarvestService.ReturnRowDestiny(rows4[0]) != "414 MORE WAITS")
                        failures.Add($"case4 [one-row-per-resource] the destiny column reads " +
                                     $"'{OfflineHarvestService.ReturnRowDestiny(rows4[0])}', expected '414 MORE WAITS'");

                    foreach (var r in rows4)
                    {
                        string label = OfflineHarvestService.ReturnRowLabel(r);
                        string destiny = OfflineHarvestService.ReturnRowDestiny(r);
                        if (r.NothingBanks && label.IndexOf('+') >= 0)
                            failures.Add($"case4 [no-gain-without-headroom] '{label}' presents {r.Pending} {r.Word} as a GAIN " +
                                         "while zero of it banks - this is the defect the owner tapped COLLECT on (42,782 promised, 0 delivered)");
                        if (!r.NothingBanks && r.Banks > 0 && label.IndexOf("+" + r.Banks, System.StringComparison.Ordinal) < 0)
                            failures.Add($"case4 [no-gain-without-headroom] '{label}' does not headline the COLLECTABLE " +
                                         $"{r.Banks} {r.Word}");
                        if (r.Banks > 0 && r.Waits > 0 && label.IndexOf("+" + r.Pending, System.StringComparison.Ordinal) >= 0)
                            failures.Add($"case4 [no-gain-without-headroom] '{label}' headlines the PENDING {r.Pending} with a " +
                                         $"gain sign while only {r.Banks} banks - WO-1434 D1 verbatim: 'the headline number is " +
                                         "the PENDING amount, not the COLLECTABLE amount'");
                        // D2 - no integer may appear twice across the one row.
                        if (r.Banks > 0 && r.Waits > 0 &&
                            destiny.IndexOf(r.Banks.ToString(), System.StringComparison.Ordinal) >= 0)
                            failures.Add($"case4 [one-row-per-resource] the destiny '{destiny}' restates the label's own " +
                                         $"integer {r.Banks} - printing one number twice is the owner's 'way too much here'");
                        // The destiny column must never call a retained amount lost -- both live
                        // producers keep what the cap refuses (WO-1392 collectors, WO-1434 silo).
                        if (destiny.IndexOf("lost", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            destiny.IndexOf("gone", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            failures.Add($"case4 [no-gain-without-headroom] destiny '{destiny}' says the units are lost; " +
                                         "nothing on either producer burns");
                        foreach (var ch in label) if (ch > 126) { failures.Add($"case4 row label '{label}' is not ASCII"); break; }
                        foreach (var ch in destiny) if (ch > 126) { failures.Add($"case4 row destiny '{destiny}' is not ASCII"); break; }
                    }
                    string footer4 = OfflineHarvestService.ReturnFooterLine(rows4);
                    if (string.IsNullOrEmpty(footer4) || footer4.IndexOf("nothing is lost", System.StringComparison.Ordinal) < 0)
                        failures.Add($"case4 [one-row-per-resource] the footer does not say plainly that nothing is lost: '{footer4}'");
                }

                // No false alarm at unlimited headroom (carried over from the retired case).
                var open4 = OfflineHarvestService.BuildReturnRows(r4, res => int.MaxValue);
                if (open4 == null || open4.Count != 3) failures.Add("case4 unlimited headroom dropped rows that still exist");
                else foreach (var r in open4)
                    if (r.Waits != 0 || OfflineHarvestService.ReturnRowDestiny(r) != "COLLECT NOW")
                        failures.Add($"case4 with unlimited headroom {r.Word} still predicts a wait (false alarm): " +
                                     $"'{OfflineHarvestService.ReturnRowDestiny(r)}'");

                // =====================================================================
                //  Case 5 [every-producer-rendered] (WO-1434) -- THE ONE THAT WOULD HAVE
                //  CAUGHT D3. The device's own modal aggregate named FIVE rows while the
                //  return screen drew THREE, and nothing anywhere compared the two counts.
                //  The two missing ones were the Echo silo (28,800 wood + 28,800 iron,
                //  device 2026-09-06 12:51:25) -- WO-1434 sec.3 attributed them to the
                //  offline-harvest grant, but that claim accrued total=0 and Grant() never
                //  ran. The rows are EchoService.DumpSilos's.
                //  RED BY: removing the SiloPending term from BuildReturnRows, or the
                //  HasSiloNews term from OfflineHarvestResult.HasSummaryContent.
                // =====================================================================
                var r5 = new OfflineHarvestResult();
                r5.PendingCollectors.Add(new OfflineHarvestResult.OfflineCollectorLine { Resource = "Wood", Pending = 10656, Collectors = 1 });
                r5.PendingCollectors.Add(new OfflineHarvestResult.OfflineCollectorLine { Resource = "Iron", Pending = 6393, Collectors = 1 });
                r5.PendingCollectors.Add(new OfflineHarvestResult.OfflineCollectorLine { Resource = "Stone", Pending = 25870, Collectors = 1 });
                r5.PendingCollectorTotal = 42919;
                r5.PendingCollectorCount = 3;
                r5.SiloPending.Add(new OfflineHarvestResult.OfflineSiloLine { Resource = "Wood", Pending = 28800 });
                r5.SiloPending.Add(new OfflineHarvestResult.OfflineSiloLine { Resource = "Iron", Pending = 28800 });
                r5.SiloTotal = 57600;
                r5.SiloAtCap = true;

                var rows5 = OfflineHarvestService.BuildReturnRows(r5, res => 0);   // every bank full, as she found it
                int producerResources = 3;   // wood, iron, stone -- the silo adds units, not resources, here
                if (rows5 == null || rows5.Count != producerResources)
                    failures.Add($"case5 [every-producer-rendered] {(rows5 == null ? -1 : rows5.Count)} rows for " +
                                 $"{producerResources} resources with a non-zero pending - a producer is being counted and not drawn");
                else
                {
                    int drawn = 0;
                    foreach (var r in rows5) drawn += r.Pending;
                    if (drawn != 42919 + 57600)
                        failures.Add($"case5 [every-producer-rendered] the rows account for {drawn} units but the " +
                                     $"producers hold {42919 + 57600} - {42919 + 57600 - drawn} units exist with no row (WO-1430 species)");
                    if (rows5[0].FromSilo != 28800 || rows5[0].FromCollectors != 10656)
                        failures.Add($"case5 [every-producer-rendered] the Wood row does not carry BOTH producers " +
                                     $"(collectors {rows5[0].FromCollectors}, silo {rows5[0].FromSilo})");
                }
                if (!r5.HasSiloNews || !r5.HasSummaryContent)
                    failures.Add("case5 [every-producer-rendered] a full Echo silo does not open the reveal gate - " +
                                 "a town whose nodes are idle and whose collectors are empty gets no screen at all");
                var siloOnly = new OfflineHarvestResult();
                siloOnly.SiloPending.Add(new OfflineHarvestResult.OfflineSiloLine { Resource = "Wood", Pending = 400 });
                siloOnly.SiloTotal = 400;
                if (!siloOnly.HasSummaryContent)
                    failures.Add("case5 [every-producer-rendered] a silo-ONLY window still reports no summary content");
                string stalled5 = OfflineHarvestService.SiloStalledLine(r5);
                if (string.IsNullOrEmpty(stalled5))
                    failures.Add("case5 a silo at cap does not say IN WORDS that the Echoes have stopped gathering " +
                                 "(`FOUNDATIONAL_RULINGS.md` section 7 - a stopped faucet is told, never signalled by colour)");
                else foreach (var ch in stalled5) if (ch > 126) { failures.Add("case5 the silo-stalled line is not ASCII"); break; }
                r5.SiloAtCap = false;
                if (!string.IsNullOrEmpty(OfflineHarvestService.SiloStalledLine(r5)))
                    failures.Add("case5 a silo BELOW cap still claims the Echoes have stopped gathering (false alarm)");

                // --- Source pins: the screen must actually seat the one producer ------
                // Moved with the copy (see case 4's header): the retired pins named
                // PredictCollectWaits(_result) / AddCollectWaitRows, both of which required the
                // duplicated second list.
                string popupPath = System.IO.Path.Combine(Application.dataPath, "_Modules/Village/Harvest/UI/WelcomeBackPopup.cs");
                string popupSrc = System.IO.File.Exists(popupPath) ? System.IO.File.ReadAllText(popupPath) : null;
                if (popupSrc == null)
                    failures.Add("case4 could not read WelcomeBackPopup.cs");
                else
                {
                    if (popupSrc.IndexOf("BuildReturnRows(_result)", System.StringComparison.Ordinal) < 0)
                        failures.Add("case4 [one-row-per-resource] WelcomeBackPopup does not build its rows from " +
                                     "OfflineHarvestService.BuildReturnRows - the screen has a second producer again");
                    if (popupSrc.IndexOf("ReturnRowDestiny(r)", System.StringComparison.Ordinal) < 0)
                        failures.Add("case4 [one-row-per-resource] the popup seats no destiny column - the amount is " +
                                     "shown without what becomes of it, which is the defect");
                    if (popupSrc.IndexOf("AddDestinyFooter(body, ref y", System.StringComparison.Ordinal) < 0)
                        failures.Add("case5 [every-producer-rendered] the popup seats no table footer - the " +
                                     "'nothing is lost' sentence and the silo-stalled sentence have no home");
                    // Quote-prefixed on purpose: the retired sentence is QUOTED in this file's own
                    // WO-1434 comment block (it is the evidence), and a bare substring test would
                    // fail on the comment that explains the fix.
                    if (popupSrc.IndexOf("\"Storage nearly full", System.StringComparison.Ordinal) >= 0)
                        failures.Add("case4 [one-row-per-resource] the RETIRED per-row warning line " +
                                     "('Storage nearly full - N <res> will wait') is back - it repeats the row's own " +
                                     "integer and is what the owner called 'way too much here' (2026-09-06)");
                }

                // =====================================================================
                //  Case 6 [new-game-claims-nothing] (WO-1414) -- THE FRESH-SAVE FIXTURE.
                // ---------------------------------------------------------------------
                //  OWNER FELT-TEST, 2026-09-05 09:57 and again 2026-09-06: START NEW, and the
                //  welcome-back popup opened on a brand-new town reading "YOUR REALM WORKED FOR
                //  8h 22m" with WOOD +11520 / IRON +6912 / STONE +15000. 8h22m was the wall time
                //  since the PREVIOUS save's session. A second New Game reported 1h 56m.
                //
                //  THE TWO PERSISTED INVARIANTS, asserted here against the SAME defaults a new
                //  save is built from, so a seat that reintroduces either mutation goes red in
                //  editmode instead of on the owner's device:
                //    1. LastHarvestClaimMs == 0. GameStateService.ResetToNewGame:1304 sets it, and
                //       the coordinator's fresh-clock arm (OfflineClaimCoordinator.cs:281-293)
                //       then yields a ZERO window and fans out to NOBODY -- which is exactly the
                //       "first window is 0 s and no popup" the ticket asks for. Case 1 above
                //       already proves the arm; this case proves the VALUE it keys off.
                //    2. EverBuiltStructureIds is EMPTY. ResetToNewGame:1354 clears it. A surviving
                //       ledger is what paid the phantom farm/lumbermill HELD ticks on a town that
                //       has neither building (ResourceBuildingHarvester.cs:257).
                //
                //  AND THE GATE ITSELF: a result carrying nothing must not open the screen. That
                //  is the term the popup and OnClaimCompleted now share
                //  (OfflineHarvestResult.HasSummaryContent), so one assertion covers both.
                //  RED BY: restoring a non-zero LastHarvestClaimMs default, seeding the ledger on
                //  a blank founding, or adding a term to HasSummaryContent that is true at zero.
                // =====================================================================
                var fresh = new GameState();
                if (fresh.LastHarvestClaimMs != 0)
                    failures.Add($"case6 [new-game-claims-nothing] a fresh GameState carries LastHarvestClaimMs=" +
                                 $"{fresh.LastHarvestClaimMs:0} -- the coordinator's fresh-clock arm keys off <= 0, so a " +
                                 "non-zero default hands a brand-new town the PREVIOUS save's away window (owner device " +
                                 "2026-09-05: 'YOUR REALM WORKED FOR 8h 22m' on START NEW)");
                if (fresh.EverBuiltStructureIds != null && fresh.EverBuiltStructureIds.Count != 0)
                    failures.Add($"case6 [new-game-claims-nothing] a fresh GameState carries " +
                                 $"{fresh.EverBuiltStructureIds.Count} ever-built id(s) -- the harvest tick pays a " +
                                 "building the new town does not have and HOLDS the units forever");
                if (fresh.HasEverBuilt("farm") || fresh.HasEverBuilt("lumbermill"))
                    failures.Add("case6 [new-game-claims-nothing] a fresh GameState already claims farm/lumbermill were " +
                                 "ever built -- these are the exact two ids the owner's device logged HELD every 10s");

                var nothing = new OfflineHarvestResult { AwaySeconds = 0.0 };
                if (nothing.HasSummaryContent)
                    failures.Add("case6 [new-game-claims-nothing] an EMPTY away result still opens the welcome-back " +
                                 "reveal gate -- a brand-new town would be shown a report with nothing in it");
                if (nothing.Total != 0)
                    failures.Add($"case6 [new-game-claims-nothing] an empty away result reports Total={nothing.Total}");

                // Case 7 [thirty-minute-reveal-floor]. Accrual/grant happens before this
                // presentation decision; short gaps stay stored normally without interrupting play.
                var shortGap = new OfflineHarvestResult { AwaySeconds = 29.0 * 60.0, Wood = 100 };
                var threshold = new OfflineHarvestResult { AwaySeconds = 30.0 * 60.0, Wood = 100 };
                if (OfflineHarvestService.ShouldRevealAwaySummary(shortGap))
                    failures.Add("case7 [thirty-minute-reveal-floor] a 29-minute gap opens WHILE YOU WERE AWAY; " +
                                 "short-gap resources should be stored normally with no modal");
                if (!OfflineHarvestService.ShouldRevealAwaySummary(threshold))
                    failures.Add("case7 [thirty-minute-reveal-floor] a content-bearing 30-minute gap does not " +
                                 "open the away summary");
                if (OfflineHarvestService.MinAwaySummarySeconds != 1800.0)
                    failures.Add("case7 [thirty-minute-reveal-floor] threshold is " +
                                 OfflineHarvestService.MinAwaySummarySeconds + "s, expected exactly 1800s");
            }
            catch (System.Exception ex)
            {
                failures.Add($"oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (svcGo != null) Object.DestroyImmediate(svcGo);
                if (gssGo != null) Object.DestroyImmediate(gssGo);
                if (throwaway != null) Object.DestroyImmediate(throwaway);

                // Restore the live service the batch's later oracles read. DestroyImmediate
                // above may have nulled the static via OnDestroy, so set it back explicitly.
                if (installed) TrySetInstanceStatic(priorInstance);

                // Restore the persisted save blob (svc.Save() wrote to it during the run).
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave);
                else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }

            if (failures.Count == 0)
            {
                reason = "OFFLINE HARVEST OK — fresh-seed + 10h cap clamp + backwards-clock guard hold; " +
                         "clock always advances (no re-claimable window); a NEW GAME carries a zero away clock " +
                         "and an empty ever-built ledger and claims nothing (WO-1414)";
                return true;
            }
            reason = $"OFFLINE HARVEST FAIL x{failures.Count}: " + string.Join(" | ", failures);
            return false;
        }

        // =====================================================================
        //  Headless state-install helpers (editmode has no Awake)
        // =====================================================================

        /// <summary>
        /// Installs <paramref name="state"/> as the active state on <paramref name="svc"/> and
        /// promotes <paramref name="svc"/> to the live singleton, by reflection over the private
        /// <c>_state</c> field + the <c>_instance</c> static — the same seam Awake sets, which does
        /// NOT run on AddComponent in editmode batchmode. Returns false (with a named reason) if
        /// either seam was renamed/removed, so the caller NAMED-SKIPs instead of false-failing.
        /// </summary>
        private static bool TryInstallHeadlessState(GameStateService svc, GameState state, out string err)
        {
            err = null;
            var stateField = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null)
            { err = "GameStateService._state field not found by reflection (state seam renamed/removed)"; return false; }
            stateField.SetValue(svc, state);
            if (!TrySetInstanceStatic(svc))
            { err = "GameStateService._instance static not found by reflection (singleton seam renamed/removed)"; return false; }
            return true;
        }

        /// <summary>Sets the private static <c>GameStateService._instance</c> (null allowed, to restore).
        /// Returns false only if the field seam is gone.</summary>
        private static bool TrySetInstanceStatic(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }
    }
}
