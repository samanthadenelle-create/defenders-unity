// =============================================================================
// PackGrantRegression [pack-grant] -- proves a purchased pack DELIVERS (ECON-01/02).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. PackStoreVM / PackCatalog / PackDef live in
// DeNelle.Wallet and CosmeticOwnershipService in DeNelle.Cosmetics -- neither is
// referenced by this asmdef, so this oracle drives them by AppDomain reflection
// (the same bridge PackStoreVM itself uses). It installs the REAL EconomyService +
// CosmeticOwnershipService singletons over a throwaway GameState, then calls the REAL
// PackStoreVM.ApplyPackContents(founders-vow PackDef) and asserts the ENTIRE
// advertised entitlement landed: Glimmer +1000, Crystals/Food/Coins by the packs.json
// amounts, and all 5 cosmetic SKUs return true from CosmeticOwnershipService.Owns.
//
// [temporary-builder-pack] (WO-1388, same suite, second case): applies the REAL
// 'builders-hour' PackDef against a throwaway GameState with the REAL EconomyService +
// BuildTimerService installed and asserts (1) the wood/iron/stone basket landed AND a
// temporary Builder crew window of RemoteTunables.EconomyPackTemporaryBuilderSecondsDefault
// (21600 s = 6 h) is RUNNING with SlotCount(Builder) +1; (2) a second purchase INSIDE the
// window is DEFERRED (ConvenienceRedeemer.DeferredTemporaryBuilderCount == 1, the running
// window is NOT extended, nothing burned); (3) after the clock is pushed past expiry, the
// service's own sweep starts the deferred charge as a fresh 6 h window. Proven RED first on
// the pre-WO tree (PackCatalog.Find('builders-hour') is null there).
//
// Marker: PACK_GRANT_OK / PACK_GRANT_FAIL. Expected: GREEN.
//
// Wire (DataRegression.RunAll):
//   if (!PackGrantRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[pack-grant] " + r);
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.Monetization;   // WO-1388 - ConvenienceRedeemer (the temporary-builder redeem pass)
using DeNelle.Core.Jobs;              // WO-1388 - ChannelId / ObsidianQueueState
using DeNelle.Core.Ops;               // WO-1388 - RemoteTunables.EconomyPackTemporaryBuilderSecondsDefault
using DeNelle.Core.State;

namespace DeNelle.Editor
{
    public static class PackGrantRegression
    {
        private const string SaveKey = "dotr-save";
        private const string CosmeticsKey = "dotr-cosmetics-v1";
        private const string PackSku = "founders-vow";

        // WO-1388 - the Builder's Hour pack and the convenience kind it carries.
        private const string TempPackSku = "builders-hour";
        private const string TempKind = "temporary-builder";
        private const string TempTag = "[temporary-builder-pack]";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- PACK GRANT (PackStoreVM.ApplyPackContents 'founders-vow' -> currency + cosmetic entitlement) ---");

            // Resolve the reflection-only types up front.
            Type vmType = FindType("DeNelle.Wallet.PackStoreVM");
            Type catType = FindType("DeNelle.Wallet.PackCatalog");
            Type glimType = FindType("DeNelle.Cosmetics.CosmeticOwnershipService");
            if (vmType == null || catType == null || glimType == null)
            {
                failures.Add($"pack types not loaded (PackStoreVM={vmType != null}, PackCatalog={catType != null}, CosmeticOwnershipService={glimType != null})");
                reason = Finish(failures, log);
                return failures.Count == 0;
            }

            // Expected amounts + SKUs from packs.json (the advertised contract).
            int expGlimmer = 0, expCrystals = 0, expFood = 0, expCoins = 0;
            var expSkus = new List<string>();
            if (!ReadExpected(out expGlimmer, out expCrystals, out expFood, out expCoins, expSkus, out string readErr))
            {
                failures.Add("packs.json read: " + readErr);
                reason = Finish(failures, log);
                return failures.Count == 0;
            }
            log.AppendLine($"  packs.json '{PackSku}': glimmer={expGlimmer} crystals={expCrystals} food={expFood} coins={expCoins} cosmetics={expSkus.Count}");

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;
            bool hadCos = PlayerPrefs.HasKey(CosmeticsKey);
            string rawCos = hadCos ? PlayerPrefs.GetString(CosmeticsKey, null) : null;
            PlayerPrefs.DeleteKey(CosmeticsKey);   // fresh cosmetic wallet

            GameStateService priorGss = GameStateService.Instance;
            object priorEcon = GetInstance(typeof(EconomyService));
            object priorGlim = GetInstance(glimType);

            GameObject gssGo = null, econGo = null, glimGo = null;
            GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (pack-grant oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "PACK GRANT", "GameStateService state seam not reflectable (needs fleet)");
                }

                econGo = new GameObject("EconomyService (pack-grant oracle)");
                var econ = econGo.AddComponent<EconomyService>();
                SetInstance(typeof(EconomyService), econ);

                glimGo = new GameObject("CosmeticOwnershipService (pack-grant oracle)");
                var glim = glimGo.AddComponent(glimType);
                SetInstance(glimType, glim);

                var ownsM = glimType.GetMethod("Owns", new[] { typeof(string) });
                if (ownsM == null)
                { failures.Add("CosmeticOwnershipService.Owns not resolvable by reflection"); reason = Finish(failures, log); return false; }

                // Load the real founders-vow PackDef through the catalog.
                catType.GetMethod("Reload", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                var findM = catType.GetMethod("Find", new[] { typeof(string) });
                object pack = findM?.Invoke(null, new object[] { PackSku });
                if (pack == null)
                { failures.Add($"PackCatalog.Find('{PackSku}') returned null -- packs.json missing the founders-vow entry"); reason = Finish(failures, log); return false; }

                // Snapshot before.
                var resBefore = throwaway.Resources;
                int crystalsBefore = resBefore.Crystals, foodBefore = resBefore.Stone, coinsBefore = resBefore.Coins;

                // Build the VM against the live (throwaway) state and APPLY the pack.
                var vm = vmType.GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                if (vm == null) { failures.Add("PackStoreVM.CreateDefault() returned null"); reason = Finish(failures, log); return false; }
                var applyM = vmType.GetMethod("ApplyPackContents", new[] { pack.GetType() });
                if (applyM == null) { failures.Add("PackStoreVM.ApplyPackContents(PackDef) not resolvable by reflection"); reason = Finish(failures, log); return false; }
                applyM.Invoke(vm, new[] { pack });

                // Snapshot after + assert every advertised delta.
                var resAfter = throwaway.Resources;
                int crystalsDelta = resAfter.Crystals - crystalsBefore;
                int foodDelta = resAfter.Stone - foodBefore;
                int coinsDelta = resAfter.Coins - coinsBefore;
                log.AppendLine($"  granted deltas: crystals=+{crystalsDelta} food=+{foodDelta} coins=+{coinsDelta}");

                if (expGlimmer != 0) failures.Add("[pack-grant] retired currency key remains advertised");
                if (crystalsDelta != expCrystals) failures.Add($"[pack-grant] Crystals delta {crystalsDelta} != advertised {expCrystals}");
                if (foodDelta != expFood) failures.Add($"[pack-grant] Food delta {foodDelta} != advertised {expFood}");
                if (coinsDelta != expCoins) failures.Add($"[pack-grant] Coins delta {coinsDelta} != advertised {expCoins}");

                int ownedOk = 0;
                foreach (var sku in expSkus)
                {
                    bool owned = (bool)ownsM.Invoke(glim, new object[] { sku });
                    if (owned) ownedOk++;
                    else failures.Add($"[pack-grant] cosmetic SKU '{sku}' NOT owned after grant (CosmeticOwnershipService.Owns false -- unequippable, ECON-02)");
                }
                log.AppendLine($"  cosmetics owned {ownedOk}/{expSkus.Count}");
            }
            catch (Exception ex)
            {
                failures.Add($"pack-grant oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (econGo != null) UnityEngine.Object.DestroyImmediate(econGo);
                if (glimGo != null) UnityEngine.Object.DestroyImmediate(glimGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                SetInstance(typeof(EconomyService), priorEcon);
                SetInstance(glimType, priorGlim);
                SetGssInstance(priorGss);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave); else PlayerPrefs.DeleteKey(SaveKey);
                if (hadCos) PlayerPrefs.SetString(CosmeticsKey, rawCos); else PlayerPrefs.DeleteKey(CosmeticsKey);
                PlayerPrefs.Save();
            }

            // WO-1388 - second case, same suite (the suite count is pinned elsewhere; a new
            // suite would move it for no gain). Its fixture restores everything it touches.
            try { RunTemporaryBuilderPackCase(failures, log); }
            catch (Exception ex) { failures.Add(TempTag + " case threw outside its fixture: " + ex.GetType().Name + ": " + ex.Message); }

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        // =====================================================================
        //  [temporary-builder-pack] - WO-1388 Builder's Hour
        // =====================================================================

        /// <summary>
        /// The Builder's Hour contract, driven through the REAL grant path (PackStoreVM.ApplyPackContents ->
        /// GearInventory token -> BuildTimerService.OnConvenienceTokensGranted -> ConvenienceRedeemer.
        /// TryRedeemTemporaryBuilder -> BuildTimerService.TryGrantTemporaryBuilder(seconds, reclaimAfterExpiry:true)).
        /// <para>Named mutations this case catches:
        /// (a) KindTemporaryBuilder routed nowhere / OnConvenienceTokensGranted not invoked -> step 1 reads
        ///     tempActive=false; (b) the 24 h <c>temporaryBuilderSeconds</c> repurposed instead of the 6 h
        ///     <c>packTemporaryBuilderSeconds</c> -> step 1 reads remaining ~86400; (c) a second buy BURNED
        ///     (token consumed, no WriteDeferred) -> step 2 reads deferred=0; (d) a second buy STACKED
        ///     (window extended) -> step 2 reads remaining grew; (e) the sweep redeem pass missing -> step 3
        ///     reads deferred=1 and no window after expiry.</para>
        /// </summary>
        private static void RunTemporaryBuilderPackCase(List<string> failures, StringBuilder log)
        {
            log.AppendLine("--- TEMPORARY BUILDER PACK (WO-1388 'builders-hour' -> basket + 6 h Builder crew; a second buy inside the window defers, never burns) ---");

            Type vmType = FindType("DeNelle.Wallet.PackStoreVM");
            Type catType = FindType("DeNelle.Wallet.PackCatalog");
            if (vmType == null || catType == null)
            {
                failures.Add(TempTag + $" pack types not loaded (PackStoreVM={vmType != null}, PackCatalog={catType != null})");
                return;
            }

            if (!ReadTempPackExpected(out int expWood, out int expIron, out int expStone, out int expTokens, out string readErr))
            {
                // On the pre-WO tree this is the RED: packs.json has no 'builders-hour' row.
                failures.Add(TempTag + " packs.json read: " + readErr);
                return;
            }
            log.AppendLine($"  packs.json '{TempPackSku}': wood={expWood} iron={expIron} stone={expStone} {TempKind} tokens={expTokens}");
            if (expTokens != 1)
                failures.Add(TempTag + $" packs.json advertises {expTokens} '{TempKind}' charge(s); the Builder's Hour is exactly ONE crew for one window");
            if (expWood <= 0 || expIron <= 0 || expStone <= 0)
                failures.Add(TempTag + " packs.json basket is missing a lane (wood/iron/stone must each be > 0 - the WO's small basket)");

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;
            bool hadDeferred = PlayerPrefs.HasKey(ConvenienceRedeemer.PrefTemporaryBuilderDeferred);
            string rawDeferred = hadDeferred ? PlayerPrefs.GetString(ConvenienceRedeemer.PrefTemporaryBuilderDeferred, null) : null;
            PlayerPrefs.DeleteKey(ConvenienceRedeemer.PrefTemporaryBuilderDeferred);   // fresh: nothing owed

            GameStateService priorGss = GameStateService.Instance;
            object priorEcon = GetInstance(typeof(EconomyService));
            BuildTimerService priorQueue = BuildTimerService.Instance;
            double priorSkipMs = TimeSource.DevSkipMs;
            if (priorSkipMs != 0d)
                log.AppendLine($"  note: DevClock skip was {priorSkipMs:F0} ms before this case; it is reset to 0 on exit.");

            GameObject gssGo = null, econGo = null, svcGo = null;
            GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                throwaway.ObsidianQueue = ObsidianQueueState.Empty();
                gssGo = new GameObject("GSS (temporary-builder-pack oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    // NOT a skip: a case that green-passes on an unreachable seam asserts nothing.
                    failures.Add(TempTag + " [fixture] GameStateService state seam not reflectable - FAIL, not a skip");
                    return;
                }

                econGo = new GameObject("EconomyService (temporary-builder-pack oracle)");
                var econ = econGo.AddComponent<EconomyService>();
                SetInstance(typeof(EconomyService), econ);

                svcGo = new GameObject("BuildTimerService (temporary-builder-pack oracle)");
                var svc = svcGo.AddComponent<BuildTimerService>();
                if (!InstallQueueInstance(svc))
                {
                    failures.Add(TempTag + " [fixture] BuildTimerService.Instance is not reflectable - the redeem pass cannot find the queue. FAIL, not a skip");
                    return;
                }

                catType.GetMethod("Reload", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                var findM = catType.GetMethod("Find", new[] { typeof(string) });
                object pack = findM?.Invoke(null, new object[] { TempPackSku });
                if (pack == null)
                {
                    failures.Add(TempTag + $" PackCatalog.Find('{TempPackSku}') returned null - the catalog did not load the Builder's Hour row");
                    return;
                }

                var vm = vmType.GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                if (vm == null) { failures.Add(TempTag + " PackStoreVM.CreateDefault() returned null"); return; }
                var applyM = vmType.GetMethod("ApplyPackContents", new[] { pack.GetType() });
                if (applyM == null) { failures.Add(TempTag + " PackStoreVM.ApplyPackContents(PackDef) not resolvable by reflection"); return; }

                double expectedSeconds = RemoteTunables.EconomyPackTemporaryBuilderSecondsDefault;   // 21600 = 6 h
                double resolvedSeconds = ConvenienceRedeemer.PackTemporaryBuilderSeconds();
                log.AppendLine($"  duration: RemoteTunables default={expectedSeconds:F0}s, ConvenienceRedeemer.PackTemporaryBuilderSeconds()={resolvedSeconds:F0}s");
                if (Math.Abs(resolvedSeconds - expectedSeconds) > 0.5)
                    failures.Add(TempTag + $" PackTemporaryBuilderSeconds() resolves {resolvedSeconds:F0}s, not the shipping 6 h ({expectedSeconds:F0}s) - the knob and BuildTimerConfig.packTemporaryBuilderSeconds disagree, or a tunable row leaked into the oracle");

                int woodBefore = throwaway.Wood, ironBefore = throwaway.Iron, stoneBefore = throwaway.Resources.Stone;
                int slotsBefore = svc.SlotCount(ChannelId.Builder);
                if (svc.IsTemporaryBuilderActive)
                    failures.Add(TempTag + " [fixture] a fresh throwaway state already reports an active temporary builder");

                // ---- (1) FIRST PURCHASE: basket lands AND the crew starts NOW ---------------------
                applyM.Invoke(vm, new[] { pack });
                int woodDelta = throwaway.Wood - woodBefore;
                int ironDelta = throwaway.Iron - ironBefore;
                int stoneDelta = throwaway.Resources.Stone - stoneBefore;
                bool active1 = svc.IsTemporaryBuilderActive;
                double remaining1 = svc.TemporaryBuilderSecondsRemaining();
                int slots1 = svc.SlotCount(ChannelId.Builder);
                int tokens1 = ConvenienceRedeemer.Count(TempKind);
                int deferred1 = ConvenienceRedeemer.DeferredTemporaryBuilderCount;
                log.AppendLine($"  (1) first buy: wood=+{woodDelta} iron=+{ironDelta} stone=+{stoneDelta} tempActive={active1} remaining={remaining1:F0}s slots {slotsBefore}->{slots1} tokens={tokens1} deferred={deferred1}");

                if (woodDelta != expWood) failures.Add(TempTag + $" Wood delta {woodDelta} != advertised {expWood}");
                if (ironDelta != expIron) failures.Add(TempTag + $" Iron delta {ironDelta} != advertised {expIron}");
                if (stoneDelta != expStone) failures.Add(TempTag + $" Stone (Resources.Stone) delta {stoneDelta} != advertised {expStone}");
                if (!active1)
                    failures.Add(TempTag + " no temporary Builder window is running after the first buy - the '" + TempKind +
                                 "' token was NOT redeemed (mutation a: KindTemporaryBuilder routed nowhere / OnConvenienceTokensGranted not invoked)");
                if (remaining1 < expectedSeconds - 5d || remaining1 > expectedSeconds + 0.5d)
                    failures.Add(TempTag + $" window remaining {remaining1:F0}s is not ~{expectedSeconds:F0}s (mutation b: the 24 h temporaryBuilderSeconds was used, or the wrong knob)");
                if (slots1 != slotsBefore + 1)
                    failures.Add(TempTag + $" SlotCount(Builder) {slotsBefore}->{slots1}; expected exactly +1 crew while the window runs");
                if (tokens1 != 0)
                    failures.Add(TempTag + $" {tokens1} '{TempKind}' token(s) still in GearInventory after the crew started - a double-spend on the next sweep");
                if (deferred1 != 0)
                    failures.Add(TempTag + $" deferred={deferred1} after the FIRST buy; nothing was running, so nothing should have been deferred");

                // ---- (2) SECOND PURCHASE INSIDE THE WINDOW: DEFERRED, never burned, never stacked --
                applyM.Invoke(vm, new[] { pack });
                int deferred2 = ConvenienceRedeemer.DeferredTemporaryBuilderCount;
                int tokens2 = ConvenienceRedeemer.Count(TempKind);
                double remaining2 = svc.TemporaryBuilderSecondsRemaining();
                int slots2 = svc.SlotCount(ChannelId.Builder);
                int woodDelta2 = throwaway.Wood - woodBefore;
                log.AppendLine($"  (2) second buy inside window: deferred={deferred2} tokens={tokens2} remaining={remaining2:F0}s slots={slots2} wood total=+{woodDelta2}");

                if (deferred2 != 1)
                    failures.Add(TempTag + $" second buy inside the window: deferred={deferred2}, expected 1 (mutation c: the charge was BURNED - consumed without WriteDeferred - or never consumed)");
                if (tokens2 != 0)
                    failures.Add(TempTag + $" second buy left {tokens2} token(s) in GearInventory instead of moving them to the deferred queue");
                if (remaining2 > remaining1 + 0.5d)
                    failures.Add(TempTag + $" the running window GREW from {remaining1:F0}s to {remaining2:F0}s on the second buy (mutation d: stacked/extended instead of deferred)");
                if (slots2 != slots1)
                    failures.Add(TempTag + $" SlotCount(Builder) moved {slots1}->{slots2} on the second buy; a deferred charge adds no crew yet");
                if (woodDelta2 != 2 * expWood)
                    failures.Add(TempTag + $" second buy basket: wood total +{woodDelta2}, expected +{2 * expWood} (the basket is granted every purchase; only the crew defers)");

                // ---- (3) EXPIRE THE WINDOW, SWEEP: the deferred charge starts as a FRESH window ----
                TimeSource.AddDevSkipMs((expectedSeconds + 1d) * 1000d);
                bool activeAfterSkip = svc.IsTemporaryBuilderActive;
                var sweepM = typeof(BuildTimerService).GetMethod("SweepAllChannels", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sweepM == null)
                {
                    failures.Add(TempTag + " BuildTimerService.SweepAllChannels not reflectable - the deferred pickup is unprovable. FAIL, not a skip");
                    return;
                }
                if (activeAfterSkip)
                    failures.Add(TempTag + " the window still reads ACTIVE after the clock was pushed past its end - expiry is not wall-clock");
                sweepM.Invoke(svc, null);
                int deferred3 = ConvenienceRedeemer.DeferredTemporaryBuilderCount;
                bool active3 = svc.IsTemporaryBuilderActive;
                double remaining3 = svc.TemporaryBuilderSecondsRemaining();
                int slots3 = svc.SlotCount(ChannelId.Builder);
                log.AppendLine($"  (3) clock +{expectedSeconds + 1d:F0}s, sweep: activeBeforeSweep={activeAfterSkip} deferred={deferred3} tempActive={active3} remaining={remaining3:F0}s slots={slots3}");

                if (deferred3 != 0)
                    failures.Add(TempTag + $" deferred={deferred3} after the sweep past expiry - the queued charge did NOT start (mutation e: sweep redeem pass missing)");
                if (!active3)
                    failures.Add(TempTag + " no window running after the sweep - the deferred charge was LOST (the one thing this pack promises never happens)");
                if (remaining3 < expectedSeconds - 5d || remaining3 > expectedSeconds + 0.5d)
                    failures.Add(TempTag + $" the deferred window remaining {remaining3:F0}s is not a fresh ~{expectedSeconds:F0}s");
                if (slots3 != slotsBefore + 1)
                    failures.Add(TempTag + $" SlotCount(Builder)={slots3} after the deferred start; expected {slotsBefore + 1}");
            }
            catch (Exception ex)
            {
                failures.Add(TempTag + $" oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                TimeSource.ResetDevSkip();
                if (svcGo != null) UnityEngine.Object.DestroyImmediate(svcGo);
                if (econGo != null) UnityEngine.Object.DestroyImmediate(econGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                SetInstance(typeof(EconomyService), priorEcon);
                RestoreQueueInstance(priorQueue);
                SetGssInstance(priorGss);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave); else PlayerPrefs.DeleteKey(SaveKey);
                if (hadDeferred) PlayerPrefs.SetString(ConvenienceRedeemer.PrefTemporaryBuilderDeferred, rawDeferred);
                else PlayerPrefs.DeleteKey(ConvenienceRedeemer.PrefTemporaryBuilderDeferred);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The Builder's Hour row as ADVERTISED in packs.json: basket lanes + temporary-builder charge count.</summary>
        private static bool ReadTempPackExpected(out int wood, out int iron, out int stone, out int tokens, out string err)
        {
            wood = iron = stone = tokens = 0; err = null;
            string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/packs.json");
            if (string.IsNullOrEmpty(json)) { err = "packs.json not found/empty"; return false; }
            JObject root;
            try { root = JObject.Parse(json); } catch (Exception ex) { err = "parse error: " + ex.Message; return false; }
            var packs = root["packs"] as JArray;
            if (packs == null) { err = "no 'packs' array"; return false; }
            foreach (var tok in packs)
            {
                if (!(tok is JObject o) || o["sku"]?.ToString() != TempPackSku) continue;
                var econ = o["contents"]?["economy"] as JObject;
                if (econ != null)
                {
                    wood = econ["wood"]?.Value<int>() ?? 0;
                    iron = econ["iron"]?.Value<int>() ?? 0;
                    stone = econ["stone"]?.Value<int>() ?? 0;
                }
                var conv = o["contents"]?["convenience"] as JArray;
                if (conv != null)
                    foreach (var c in conv)
                    {
                        if (!(c is JObject item)) continue;
                        if (string.Equals(item["kind"]?.ToString(), TempKind, StringComparison.OrdinalIgnoreCase))
                            tokens += item["count"]?.Value<int>() ?? 0;
                    }
                return true;
            }
            err = $"packs.json has no '{TempPackSku}' entry";
            return false;
        }

        // ---- BuildTimerService.Instance install/restore (same shape as TrainingCostsTimeOnlyRegression) ----
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

        private static bool ReadExpected(out int glimmer, out int crystals, out int food, out int coins,
                                         List<string> skus, out string err)
        {
            glimmer = crystals = food = coins = 0; err = null;
            string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/packs.json");
            if (string.IsNullOrEmpty(json)) { err = "packs.json not found/empty"; return false; }
            JObject root;
            try { root = JObject.Parse(json); } catch (Exception ex) { err = "parse error: " + ex.Message; return false; }
            var packs = root["packs"] as JArray;
            if (packs == null) { err = "no 'packs' array"; return false; }
            foreach (var tok in packs)
            {
                if (!(tok is JObject o) || o["sku"]?.ToString() != PackSku) continue;
                var econ = o["contents"]?["economy"] as JObject;
                if (econ != null)
                {
                    glimmer = econ["glimmer"]?.Value<int>() ?? 0;
                    crystals = econ["crystals"]?.Value<int>() ?? 0;
                    food = econ["stone"]?.Value<int>() ?? 0;
                    coins = econ["coins"]?.Value<int>() ?? 0;
                }
                var cos = o["contents"]?["cosmetics"] as JArray;
                if (cos != null) foreach (var c in cos) { string s = c?.ToString(); if (!string.IsNullOrEmpty(s)) skus.Add(s); }
                return true;
            }
            err = $"packs.json has no '{PackSku}' entry";
            return false;
        }

        // ---- reflection helpers -------------------------------------------------
        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(full, false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool InstallState(GameStateService svc, GameState state)
        {
            var f = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(svc, state);
            return SetGssInstance(svc);
        }

        private static bool SetGssInstance(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }

        private static FieldInfo InstanceField(Type t)
        {
            var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                 ?? t.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) return f;
            foreach (var ff in t.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
                if (ff.Name.Contains("Instance") && ff.FieldType == t) return ff;
            return null;
        }

        private static object GetInstance(Type t) { var f = InstanceField(t); return f != null ? f.GetValue(null) : null; }
        private static void SetInstance(Type t, object val) { var f = InstanceField(t); if (f != null) f.SetValue(null, val); }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "PACK_GRANT_OK");
                return "PACK GRANT OK -- founders-vow granted Glimmer + Crystals/Food/Coins by the advertised amounts and all cosmetic SKUs are owned; " +
                       "[temporary-builder-pack] builders-hour granted its basket + a 6 h Builder crew, a second buy inside the window deferred (not burned, not stacked) and started after expiry";
            }
            string reason = "pack-grant: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "PACK_GRANT_FAIL: " + reason);
            return reason;
        }
    }
}
