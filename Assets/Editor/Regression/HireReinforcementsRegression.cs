// =============================================================================
// HireReinforcementsRegression - WO-1372 Lane D: GOLD HIRES MERCENARIES.
// Marker: HIRE_REINFORCEMENTS_OK / HIRE_REINFORCEMENTS_FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Register in DataRegression.RunAll.
// Contract mirrors the other Run(out reason) oracles (ManageTroopsTrainDoorRegression,
// ObsidianQueueRegression, BuildEconomyRegression).
//
// THE ARROW BEING PROVEN (docs/PROGRAM_RAID_ECONOMY_2026-09-04.md):
//     Get richer -> Train (faster).
// Owner ruling, verbatim: "gold buys hire mercenaries instead of waiting on time."
// Creative canon (docs/CREATIVE_CANON_ELARION_2026-09-04.md §6, lines 181-194):
// mercenaries are the SAME UNIT - no upkeep, no contracts, no expiry, no roster -
// and the button reads HIRE REINFORCEMENTS, never "Skip Training".
//
// THE DEFECT THIS SUITE IS SHAPED AROUND, measured at source on 2026-09-04:
//   * `mercenar` appeared NOWHERE under Assets/ (grep, zero hits).
//   * The speed-up already existed, was already channel-generic, and already
//     reached Train jobs. The defect was the CURRENCY, at exactly two sites:
//     BuildTimerService.TryInstantFinish tested `state.Resources.Crystals < price`
//     and spent `svc.AddCrystals(-price)` for EVERY channel, so a training job
//     could only ever be rushed with crystals.
//   * ObsidianQueueHud priced its rush row through the Builder-ONLY overload
//     `InstantFinishPrice(structureId)`, so a training job priced at 0 and the
//     CTA was hidden entirely - its own comment conceded it.
//
// ⛔ THE ONE-MECHANISM RULE. Case 5 exists to stop a future seat "adding gold
// support" by writing a SECOND instant-finish that spends coins somewhere else.
// The currency branch lives in BuildTimerService or it does not exist.
//
// Cases:
//   1  CURRENCY MAP  - FinishPaysGold is TrainTroop and ONLY TrainTroop.
//   2  PRICE CURVE   - the gold price is real, floored, monotonic, and rides the
//                      SAME shared curve (no second pricing philosophy).
//   3  LIVE SPEND    - 0 crystals + ample gold: a Train job finishes NOW, Coins
//                      falls by EXACTLY the price, Crystals is UNCHANGED, and the
//                      troop lands in the roster. A Builder job still pays crystals.
//   4  REFUSALS      - the gold shortfall has its OWN prefix and does not route to
//                      the crystal store.
//   5  SHAPE         - no second coins-spending instant-finish outside
//                      BuildTimerService; the HUD uses the channel overload; the
//                      canon verb is present and "Skip Training" is nowhere.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class HireReinforcementsRegression
    {
        private const string ServicePath = "Assets/_Modules/Village/Buildings/BuildTimerService.cs";
        private const string HudPath     = "Assets/_Modules/Village/BuildMode/ObsidianQueueHud.cs";
        // WO-1512: the work-queue surface's PRICING moved off the View and onto its ViewModel
        // (presentation never touches the objects, ARCHITECTURE_PRINCIPLES §2). Case 5b's subject
        // is "whatever prices the work-queue surface", so it follows the seam to the VM and keeps
        // the negative Builder-only check on the View as well - a re-inlined price there is still
        // the defect this case exists to catch.
        private const string QueueVmPath = "Assets/_Modules/Village/BuildMode/ObsidianQueueVM.cs";
        private const string VmPath      = "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== HireReinforcementsRegression: gold hires mercenaries (WO-1372 Lane D) ===");

            try
            {
                CheckCurrencyMap(failures, log);
                CheckPriceCurve(failures, log);
                RunLiveSpend(failures, log);
                CheckShape(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add($"HireReinforcementsRegression threw: {ex.GetType().Name}: {ex.Message}");
            }

            return Verdict(failures, log, out reason);
        }

        // ── CASE 1: the currency map ──────────────────────────────────────────
        private static void CheckCurrencyMap(List<string> failures, StringBuilder log)
        {
            if (!BuildTimerService.FinishPaysGold(JobKind.TrainTroop))
                failures.Add("[case 1] BuildTimerService.FinishPaysGold(JobKind.TrainTroop) is FALSE - the owner's " +
                             "ruling is that gold hires mercenaries instead of waiting on time. With this false the " +
                             "training rush is charged in crystals again, which is the pre-WO-1372 defect exactly.");

            foreach (JobKind kind in Enum.GetValues(typeof(JobKind)))
            {
                if (kind == JobKind.TrainTroop) continue;
                if (BuildTimerService.FinishPaysGold(kind))
                    failures.Add($"[case 1] FinishPaysGold({kind}) is TRUE. ⛔ NO NEW CURRENCY and no currency " +
                                 "creep: gold buys TROOP TIME only. Every other channel keeps crystals, which is " +
                                 "what funds the Cathedral ladder (BuildTimerConfig 'ONE WALLET, DELIBERATELY').");
            }
            log.AppendLine("  case 1 - currency map checked across " +
                           Enum.GetValues(typeof(JobKind)).Length + " JobKind value(s)");
        }

        // ── CASE 2: the gold price curve ──────────────────────────────────────
        private static void CheckPriceCurve(List<string> failures, StringBuilder log)
        {
            var cfg = BuildTimerConfig.CreateDefault();
            if (cfg == null)
            {
                failures.Add("[case 2] BuildTimerConfig.CreateDefault() returned null - the gold price cannot be " +
                             "priced at all. FAIL, not a skip.");
                return;
            }

            int floorPrice = cfg.HireReinforcementsPrice(1.0);   // ~1 second left
            int tenMinutes = cfg.HireReinforcementsPrice(600.0); // the longest authored train time
            int hour       = cfg.HireReinforcementsPrice(3600.0);
            log.AppendLine($"  case 2 - gold price 1s={floorPrice}  10m={tenMinutes}  1h={hour} " +
                           $"(perMinute={cfg.hireReinforcementsGoldPerMinute}, min={cfg.hireReinforcementsMinGold})");

            if (cfg.hireReinforcementsGoldPerMinute > 0)
            {
                if (floorPrice < cfg.hireReinforcementsMinGold)
                    failures.Add($"[case 2] HireReinforcementsPrice(1s) = {floorPrice}, below the authored floor of " +
                                 $"{cfg.hireReinforcementsMinGold} - a nearly-trained unit would hire for nothing.");
                if (tenMinutes < floorPrice || hour < tenMinutes)
                    failures.Add($"[case 2] the gold price is NOT monotonic in remaining time " +
                                 $"({floorPrice} -> {tenMinutes} -> {hour}). Paying to skip MORE time must never " +
                                 "cost less, or the player is taught to wait before hiring.");
                if (tenMinutes <= 0)
                    failures.Add("[case 2] a 10-minute training job hires for 0 gold - a free rush is not a sink, " +
                                 "and 0 also HIDES the CTA (every list builds its button only when price > 0).");
            }
            else if (hour != 0)
            {
                failures.Add("[case 2] hireReinforcementsGoldPerMinute is 0 (sink disabled) but the price is not 0 - " +
                             "the disable switch does not disable.");
            }

            // ONE CURVE, not two philosophies: with the two knob pairs equal the gold price must
            // reproduce the crystal price exactly. If someone forks the maths this fails.
            int perMin = cfg.instantFinishCrystalsPerMinute, min = cfg.instantFinishMinCrystals;
            int goldPerMin = cfg.hireReinforcementsGoldPerMinute, goldMin = cfg.hireReinforcementsMinGold;
            try
            {
                cfg.hireReinforcementsGoldPerMinute = perMin;
                cfg.hireReinforcementsMinGold = min;
                for (double sec = 30.0; sec <= 7200.0; sec *= 4.0)
                {
                    int a = cfg.InstantFinishPrice(sec), b = cfg.HireReinforcementsPrice(sec);
                    if (a != b)
                    {
                        failures.Add($"[case 2] with IDENTICAL knobs the gold price ({b}) differs from the crystal " +
                                     $"price ({a}) at {sec}s remaining. There must be ONE skip curve - a forked " +
                                     "curve is the duplicated state CLAUDE.md keeps paying for.");
                        break;
                    }
                }
            }
            finally
            {
                cfg.hireReinforcementsGoldPerMinute = goldPerMin;
                cfg.hireReinforcementsMinGold = goldMin;
            }
        }

        // ── CASE 3 + 4: the LIVE spend, against real services ─────────────────
        private static void RunLiveSpend(List<string> failures, StringBuilder log)
        {
            string priorSave = PlayerPrefs.GetString(SaveSchema.PlayerPrefsKey, null);
            var priorGss = GameStateService.Instance;
            var priorQueue = BuildTimerService.Instance;

            GameObject gssGo = null, svcGo = null;
            GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (hire-reinforcements oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    // NOT A SKIP: a suite that green-passes on an unreachable seam asserts nothing,
                    // most eagerly on the day the seam breaks.
                    failures.Add("[case 3] GameStateService state seam is not reflectable, so the LIVE gold spend " +
                                 "could not run. This is a FAIL, not a skip.");
                    return;
                }

                svcGo = new GameObject("BuildTimerService (hire-reinforcements oracle)");
                var svc = svcGo.AddComponent<BuildTimerService>();
                if (!InstallQueueInstance(svc))
                {
                    failures.Add("[case 3] BuildTimerService.Instance backing field is not reflectable - the oracle " +
                                 "cannot install the queue singleton. FAIL, not a skip.");
                    return;
                }

                // The completion effects are registered by a [RuntimeInitializeOnLoadMethod], which
                // does NOT run in edit mode. Invoke it directly so the roster grant is genuinely
                // exercised rather than assumed. Registration is idempotent by its own comment.
                InvokeRuntimeInit(typeof(BarracksService), "RegisterEffects");

                throwaway.Onboarded = true;
                throwaway.BarracksLevel = 3;
                throwaway.Wood = 100000;
                throwaway.Iron = 100000;
                throwaway.ObsidianQueue = ObsidianQueueState.Empty();
                var bal = throwaway.Resources;
                bal.Stone = 100000;
                bal.Crystals = 0;          // ⚠ THE ACCEPTANCE FIXTURE: broke on crystals ...
                bal.Coins = 100000;        // ... and rich in gold.
                throwaway.Resources = bal;

                // A REAL training job on the REAL line, enqueued through the engine (not hand-built),
                // so the kind, the channel and the id grammar are the shipping ones.
                string troopId = "troop-footman";
                string jobId = BarracksService.TrainPrefix + troopId + ":oracle01";
                var enqueued = svc.Enqueue(JobKind.TrainTroop, ChannelId.Train, jobId, 600d);
                if (enqueued == null)
                {
                    failures.Add("[case 3] could not enqueue a JobKind.TrainTroop job on ChannelId.Train - " +
                                 "the fixture cannot be built, so the spend is unproven.");
                    return;
                }

                int price = svc.InstantFinishPrice(ChannelId.Train, jobId);
                bool paysGold = svc.FinishPaysGold(ChannelId.Train, jobId);
                int rosterBefore = throwaway.Army != null ? throwaway.Army.CountOfDef(troopId) : 0;
                int coinsBefore = throwaway.Resources.Coins;
                int crystalsBefore = throwaway.Resources.Crystals;
                log.AppendLine($"  case 3 fixture - price={price} paysGold={paysGold} coins={coinsBefore} " +
                               $"crystals={crystalsBefore} roster('{troopId}')={rosterBefore}");

                if (!paysGold)
                    failures.Add("[case 3] the live Train job does NOT report paysGold - the price above was quoted " +
                                 "in crystals, which the player does not have and should not need.");
                if (price <= 0)
                    failures.Add("[case 3] a running Train job priced at 0, so no HIRE REINFORCEMENTS CTA can be " +
                                 "built by any list (every one gates on price > 0). This is the exact shape of the " +
                                 "pre-WO-1372 defect, in which training showed no finish CTA at all.");

                bool ok = svc.TryInstantFinish(ChannelId.Train, jobId, out string failure);
                int coinsAfter = throwaway.Resources.Coins;
                int crystalsAfter = throwaway.Resources.Crystals;
                int rosterAfter = throwaway.Army != null ? throwaway.Army.CountOfDef(troopId) : 0;
                log.AppendLine($"  case 3 result - ok={ok} reason=\"{failure}\" coins={coinsAfter} " +
                               $"crystals={crystalsAfter} roster={rosterAfter} " +
                               $"trainDepth={svc.QueueDepth(ChannelId.Train)}");

                if (!ok)
                {
                    failures.Add($"[case 3] with 0 crystals and {coinsBefore} gold, hiring reinforcements on a real " +
                                 $"training job FAILED: \"{failure}\". The owner's arrow (Get richer -> Train faster) " +
                                 "is broken.");
                }
                else
                {
                    if (coinsBefore - coinsAfter != price)
                        failures.Add($"[case 3] Coins moved by {coinsBefore - coinsAfter}, expected exactly {price}. " +
                                     "The face quoted one number and the wallet paid another.");
                    if (crystalsAfter != crystalsBefore)
                        failures.Add($"[case 3] Crystals moved ({crystalsBefore} -> {crystalsAfter}) on a GOLD hire. " +
                                     "Training must never touch the crystal wallet.");
                    if (svc.QueueDepth(ChannelId.Train) != 0)
                        failures.Add("[case 3] the gold was spent but the training job is STILL on the Train line - " +
                                     "the player paid for nothing.");
                    if (rosterAfter <= rosterBefore)
                        failures.Add($"[case 3] the job completed but the roster did not grow ({rosterBefore} -> " +
                                     $"{rosterAfter}) for '{troopId}'. Mercenaries are the SAME UNIT (creative canon " +
                                     "§6) - a hire that lands no troop is a purchase of nothing.");
                }

                // ── CASE 3b: a BUILDER job still spends CRYSTALS, and being broke on them refuses.
                var wallet = throwaway.Resources;
                wallet.Crystals = 0;
                throwaway.Resources = wallet;
                string buildId = "hire-oracle-structure";
                if (svc.Enqueue(JobKind.Build, ChannelId.Builder, buildId, 600d) == null)
                {
                    failures.Add("[case 3b] could not enqueue a Builder job - the crystal side is unproven.");
                }
                else
                {
                    int builderPrice = svc.InstantFinishPrice(ChannelId.Builder, buildId);
                    if (svc.FinishPaysGold(ChannelId.Builder, buildId))
                        failures.Add("[case 3b] a BUILD job reports paysGold - gold has leaked out of the training " +
                                     "channel and every builder timer just became purchasable with raid income.");
                    int goldBefore = throwaway.Resources.Coins;
                    bool builderOk = svc.TryInstantFinish(ChannelId.Builder, buildId, out string builderFailure);
                    log.AppendLine($"  case 3b - builderPrice={builderPrice} ok={builderOk} " +
                                   $"reason=\"{builderFailure}\" goldDelta={throwaway.Resources.Coins - goldBefore}");

                    if (builderOk)
                        failures.Add("[case 3b] a Builder job finished with ZERO crystals held. The crystal wall is " +
                                     "gone, which defunds every crystal ladder in the game.");
                    if (throwaway.Resources.Coins != goldBefore)
                        failures.Add($"[case 3b] the refused Builder finish moved GOLD by " +
                                     $"{throwaway.Resources.Coins - goldBefore}. A builder timer must never be " +
                                     "chargeable to the army wallet.");

                    // ── CASE 4: the refusals are DISTINGUISHABLE without parsing prose.
                    if (builderFailure == null ||
                        !builderFailure.StartsWith(BuildTimerService.InsufficientCrystalsPrefix, StringComparison.Ordinal))
                        failures.Add($"[case 4] the broke Builder refusal (\"{builderFailure}\") does not carry " +
                                     "InsufficientCrystalsPrefix - the UI can no longer route the player to the " +
                                     "crystal store.");
                }

                // Broke on GOLD, on a training job: its own prefix, never the crystal one (which
                // would route the player to a store that does not sell gold - gold is EARNED).
                var poor = throwaway.Resources;
                poor.Coins = 0;
                poor.Crystals = 100000;
                throwaway.Resources = poor;
                string jobId2 = BarracksService.TrainPrefix + troopId + ":oracle02";
                if (svc.Enqueue(JobKind.TrainTroop, ChannelId.Train, jobId2, 600d) == null)
                {
                    failures.Add("[case 4] could not enqueue the second training job - the gold refusal is unproven.");
                }
                else
                {
                    bool hired = svc.TryInstantFinish(ChannelId.Train, jobId2, out string goldFailure);
                    log.AppendLine($"  case 4 - broke-on-gold ok={hired} reason=\"{goldFailure}\" " +
                                   $"crystals={throwaway.Resources.Crystals}");
                    if (hired)
                        failures.Add("[case 4] reinforcements were hired with ZERO gold. The gold check is not " +
                                     "reached, or it read the crystal wallet (which is full in this fixture).");
                    if (throwaway.Resources.Crystals != 100000)
                        failures.Add("[case 4] a broke-on-GOLD training hire spent CRYSTALS instead - the player was " +
                                     "charged the wrong currency, which is the whole defect.");
                    if (goldFailure == null ||
                        !goldFailure.StartsWith(BuildTimerService.InsufficientGoldPrefix, StringComparison.Ordinal))
                        failures.Add($"[case 4] the broke-on-gold refusal (\"{goldFailure}\") does not carry " +
                                     "InsufficientGoldPrefix, so a caller cannot tell it from a crystal shortfall.");
                    if (goldFailure != null &&
                        goldFailure.StartsWith(BuildTimerService.InsufficientCrystalsPrefix, StringComparison.Ordinal))
                        failures.Add("[case 4] the gold shortfall reuses the CRYSTAL prefix, which routes the player " +
                                     "to the crystal store to solve a gold problem.");
                }
            }
            finally
            {
                if (svcGo != null) UnityEngine.Object.DestroyImmediate(svcGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                RestoreQueueInstance(priorQueue);
                RestoreGssInstance(priorGss);
                // The live spend calls GameStateService.Save(), which writes the editor save slot.
                // Put the developer's save back exactly as it was.
                if (priorSave != null) PlayerPrefs.SetString(SaveSchema.PlayerPrefsKey, priorSave);
                else PlayerPrefs.DeleteKey(SaveSchema.PlayerPrefsKey);
                PlayerPrefs.Save();
            }
        }

        // ── CASE 5: shape - one mechanism, one spender, the canon words ───────
        private static void CheckShape(List<string> failures, StringBuilder log)
        {
            // 5a. ⛔ NO SECOND COINS-SPENDING INSTANT-FINISH. Any file that BOTH debits coins AND
            // talks about finishing/hiring a job is a fork of the one mechanism. BuildTimerService
            // is the single sanctioned site; this oracle is excluded because it NAMES the tokens.
            string root = Directory.GetCurrentDirectory();
            string modules = Path.Combine(root, "Assets", Path.Combine("_Modules", ""));
            var offenders = new List<string>();
            string[] spendTokens = { "AddCoins(-", "Coins -=", "wallet.Coins - ", "r.Coins - " };
            string[] finishTokens = { "InstantFinish", "HireReinforcements", "CompleteAnyJob" };

            if (!Directory.Exists(modules))
            {
                failures.Add("[case 5] Assets/_Modules not found from the working directory - the shape scan could " +
                             "not run. FAIL, not a skip (the repo root is machine-dependent, CLAUDE.md §0).");
            }
            else
            {
                foreach (string file in Directory.GetFiles(modules, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.EndsWith("BuildTimerService.cs", StringComparison.OrdinalIgnoreCase)) continue;
                    string src;
                    try { src = File.ReadAllText(file); }
                    catch (Exception ex) { failures.Add($"[case 5] could not read {file}: {ex.Message}"); continue; }
                    string code = StripLineComments(src);

                    bool spends = false;
                    foreach (string t in spendTokens)
                        if (code.IndexOf(t, StringComparison.Ordinal) >= 0) { spends = true; break; }
                    if (!spends) continue;

                    foreach (string t in finishTokens)
                        if (code.IndexOf(t, StringComparison.Ordinal) >= 0)
                        {
                            offenders.Add(file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar) +
                                          " (spends coins AND mentions " + t + ")");
                            break;
                        }
                }
            }
            if (offenders.Count > 0)
                failures.Add("[case 5] ⛔ A SECOND COINS-SPENDING INSTANT-FINISH EXISTS outside BuildTimerService: " +
                             string.Join(" | ", offenders) + ". WO-1372's whole point is REUSING the one speed-up " +
                             "mechanism; a parallel implementation is the defect the lane exists to avoid. Put the " +
                             "currency branch in BuildTimerService.TryInstantFinish and call it.");
            else
                log.AppendLine("  case 5a OK - BuildTimerService is the only coins-spending finish site under _Modules");

            // 5b. the HUD must price through the CHANNEL overload. The Builder-only one cannot
            // resolve a Train job, which is why training had no CTA in this HUD at all.
            // The negative half stays on the VIEW: a price re-inlined there is the §2 breach AND
            // the Builder-only bug at once.
            string hud = ReadSource(HudPath, failures);
            if (hud != null)
            {
                string hudCode = StripLineComments(hud);
                if (hudCode.IndexOf("InstantFinishPrice(", StringComparison.Ordinal) >= 0)
                    failures.Add("[case 5b] ObsidianQueueHud prices a job itself again. WO-1512 moved the quote to " +
                                 "ObsidianQueueVM.OfferFor; a View that re-quotes can (and did) reach for the " +
                                 "Builder-only overload, which prices every Train/Research job at 0 and makes the " +
                                 "finish CTA silently disappear from the training queue.");
            }

            string queueVm = ReadSource(QueueVmPath, failures);
            if (queueVm != null)
            {
                string code = StripLineComments(queueVm);
                if (code.IndexOf("InstantFinishPrice(job.StructureId)", StringComparison.Ordinal) >= 0)
                    failures.Add("[case 5b] ObsidianQueueVM is back on the Builder-only " +
                                 "InstantFinishPrice(structureId). A Train/Research job prices at 0 there, and every " +
                                 "list gates its button on price > 0 - so the finish CTA silently disappears from " +
                                 "the training queue.");
                if (code.IndexOf("InstantFinishPrice(channel,", StringComparison.Ordinal) < 0 &&
                    code.IndexOf("InstantFinishPrice(job.ChannelId,", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5b] ObsidianQueueVM does not call the CHANNEL overload of " +
                                 "InstantFinishPrice - training jobs cannot be priced from this surface.");
                if (code.IndexOf("HireReinforcementsVerb", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5b] ObsidianQueueVM does not quote " +
                                 "BuildTimerService.HireReinforcementsVerb - a retyped face drifts from canon.");
            }

            // 5c. the VM composes the verb, and the currency comes from the ONE authority.
            string vm = ReadSource(VmPath, failures);
            if (vm != null)
            {
                string code = StripLineComments(vm);
                if (code.IndexOf("BuildTimerService.FinishPaysGold", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5c] ManageScreenVM does not ask BuildTimerService.FinishPaysGold - the row " +
                                 "would print a currency it inferred locally, which can disagree with the debit.");
                // ⚠ THE VERB PIN IS RETIRED FOR THE QUEUE ROW, BY OWNER RULING, ON A MEASUREMENT.
                // WO-1443 panel 8: the queue row's CTA verb was instrumented and reported
                //   'HIRE REINFORCEMENTS' needs 598px and its box gives 236px at the font floor
                // - two and a half times over. No slot on that row can seat it at any size this
                // project considers legible, so it ellipsised to "HIRE REIN...". The owner's mockup
                // draws ONE gold SPEED UP on every tab and prices it on the line beneath, and she
                // has ruled the mockup absolute. The row now reads SPEED UP on all three channels.
                //
                // ⛔ WHAT THIS CASE ACTUALLY DEFENDS IS UNTOUCHED AND IS ASSERTED ABOVE AND BELOW:
                // the CURRENCY is still the service's decision (FinishPaysGold, checked above) and
                // the row still SAYS which currency in words - "349 gold" on a training job,
                // "33 crystals" elsewhere - via FinishCostText. Nothing about what the player spends
                // has changed; only a verb that could not render has gone.
                // Pinning a word that cannot fit its box is pinning a defect, so the pin moves to
                // the cost line - the thing that carries the meaning and CAN be shown.
                if (code.IndexOf("FinishCostText", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5c] ManageScreenVM no longer composes FinishCostText. With the " +
                                 "HIRE REINFORCEMENTS verb retired (it measured 598px into a 236px slot), the " +
                                 "COST LINE is the only thing telling a player the training rush is priced in " +
                                 "GOLD rather than crystals - losing it hides the currency entirely.");
                if (code.IndexOf("DescribeFinishCost(price, balance, paysGold)", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5c] the queue row's cost line no longer carries the service's paysGold " +
                                 "verdict into its words - it would print a currency it inferred locally, which " +
                                 "is the same defect the FinishPaysGold check above exists to stop.");
            }

            // 5d. the retired words, anywhere under Assets. Creative canon §6: flavour wins.
            string service = ReadSource(ServicePath, failures);
            foreach (var pair in new[]
                     {
                         new KeyValuePair<string, string>(HudPath, hud),
                         new KeyValuePair<string, string>(VmPath, vm),
                         new KeyValuePair<string, string>(ServicePath, service),
                     })
            {
                if (pair.Value == null) continue;
                // CODE, not prose: these files DISCUSS the retired phrase in their comments (that is
                // how the next seat learns why it is retired). Only a live string may fail this.
                if (StripLineComments(pair.Value).IndexOf("Skip Training", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"[case 5d] \"Skip Training\" appears in {pair.Key}. Creative canon §6 rules the " +
                                 "button reads HIRE REINFORCEMENTS - the flavour IS the feature, because the " +
                                 "mechanics are deliberately the cheap half.");
            }

            // 5e. the service still branches the WALLET, not just the price. A price-only change
            // would quote gold and charge crystals.
            if (service != null)
            {
                string code = StripLineComments(service);
                if (code.IndexOf("state.Resources.Coins", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5e] BuildTimerService never reads state.Resources.Coins - the gold wallet is " +
                                 "not tested, so a broke player hires for free or is refused against the wrong purse.");
                if (code.IndexOf("InsufficientGoldPrefix", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5e] BuildTimerService.InsufficientGoldPrefix is gone - the gold refusal is " +
                                 "indistinguishable from a crystal one.");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static bool InstallState(GameStateService svc, GameState state)
        {
            var f = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(svc, state);
            var i = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (i == null) return false;
            i.SetValue(null, svc);
            return true;
        }

        private static void RestoreGssInstance(GameStateService prior)
        {
            var i = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (i != null) i.SetValue(null, prior);
        }

        private static bool InstallQueueInstance(BuildTimerService svc)
        {
            var t = typeof(BuildTimerService);
            var prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.GetSetMethod(true) != null)
            {
                prop.GetSetMethod(true).Invoke(null, new object[] { svc });
                return ReferenceEquals(BuildTimerService.Instance, svc);
            }
            var f = t.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return ReferenceEquals(BuildTimerService.Instance, svc);
        }

        private static void RestoreQueueInstance(BuildTimerService prior)
        {
            var t = typeof(BuildTimerService);
            var prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.GetSetMethod(true) != null)
            {
                prop.GetSetMethod(true).Invoke(null, new object[] { prior });
                return;
            }
            var f = t.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) f.SetValue(null, prior);
        }

        /// <summary>Invokes a private [RuntimeInitializeOnLoadMethod] that edit mode never fires.
        /// Silent absence is NOT tolerated: a missing hook is reported by the case that needed it.</summary>
        private static void InvokeRuntimeInit(Type type, string method)
        {
            var m = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
            if (m != null) m.Invoke(null, null);
        }

        /// <summary>Drops whole-line // and /// comments so a source oracle matches CODE, not prose.</summary>
        private static string StripLineComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            foreach (string line in src.Split('\n'))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Repo-relative read. The repo ROOT is machine-dependent (CLAUDE.md §0), so it is
        /// resolved at runtime from the working directory and never hardcoded.</summary>
        private static string ReadSource(string relativePath, List<string> failures)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(),
                                       relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                failures.Add($"source file missing: {relativePath}");
                return null;
            }
            return File.ReadAllText(full);
        }

        private static bool Verdict(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = "HIRE REINFORCEMENTS OK - gold (and only gold) finishes a TrainTroop job on the ONE " +
                         "instant-finish mechanism: Coins falls by exactly the quoted price, Crystals is untouched, " +
                         "the troop lands in the roster, a Builder job still pays crystals, and the two shortfalls " +
                         "carry distinct prefixes";
                Debug.Log("HIRE_REINFORCEMENTS_OK\n" + log);
                return true;
            }
            reason = $"HIRE REINFORCEMENTS: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"HIRE_REINFORCEMENTS_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}
