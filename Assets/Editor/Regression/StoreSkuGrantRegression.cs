// =============================================================================
// StoreSkuGrantRegression [store-sku-grant] -- WO-1246.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
// Marker: STORE_SKU_GRANT_OK / STORE_SKU_GRANT_FAIL.
//
// THE RULE: a SKU cannot be VISIBLE while the thing it sells does nothing.
// Visibility is PackCatalog.IsOnBrowsableShelf -- the SAME helper PackStore uses
// to build cards -- so this oracle and the shelf cannot disagree.
//
// WO-1138: an unreadable catalog is a FAIL, never a quiet green. Dual-copy
// mismatch is a FAIL. An empty packs array is a FAIL. A live-grant section that
// cannot install GameStateService is a [PARTIAL-SKIP] of THAT section only; the
// data cases have already asserted.
//
// WHAT THIS DOES NOT DO: merge clones, reprice dominated rungs, or author the
// WO-1253 permanent-builder SKU. Those are other tickets.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Monetization;
using DeNelle.Wallet;

namespace DeNelle.Editor.Regression
{
    /// <summary>Pins that every browsable SKU has a working grant path (WO-1246).</summary>
    public static class StoreSkuGrantRegression
    {
        private const string PacksRel = "Data/Canonical/packs.json";
        private const string MonthlyRel = "Data/Canonical/battle_monthly.json";
        private const string SaveKey = "dotr-save";
        private const string CosmeticsKey = "dotr-cosmetics-v1";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("STORE_SKU_GRANT_OK - " + reason);
            else Debug.LogError("STORE_SKU_GRANT_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== StoreSkuGrantRegression [store-sku-grant] (WO-1246: visible SKU must grant) ===");

            try
            {
                CaseDualCopy(PacksRel, failures, log);
                CaseDualCopy(MonthlyRel, failures, log);

                JObject packsRoot = ReadJson(PacksRel, failures, log);
                if (packsRoot == null)
                {
                    reason = Finish(failures, notes, log);
                    return failures.Count == 0;
                }

                var packs = packsRoot["packs"] as JArray;
                if (packs == null || packs.Count == 0)
                {
                    failures.Add("[catalog] packs.json has no packs[] -- an unreadable/empty catalog is a FAIL, not a skip (WO-1138).");
                    reason = Finish(failures, notes, log);
                    return false;
                }

                CaseShelfHasDeliverableGrant(packs, failures, log);
                CaseNoVisibleVaporConvenience(packs, failures, log);
                CaseMonthlyCardsGrantable(failures, log);
                CaseLiveGrant(packs, failures, notes, log);
                CaseRedeemerConsumes(notes, failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[store-sku-grant] THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            reason = Finish(failures, notes, log);
            return failures.Count == 0;
        }

        // =====================================================================
        //  Dual-copy / load. Unreadable = FAIL (WO-1138).
        // =====================================================================
        private static void CaseDualCopy(string rel, List<string> failures, StringBuilder log)
        {
            string res = Application.dataPath + "/Resources/" + rel;
            string sa = Application.dataPath + "/StreamingAssets/" + rel;
            if (!File.Exists(res) || !File.Exists(sa))
            {
                failures.Add("[dual-copy] " + rel + " missing " +
                             (File.Exists(res) ? "" : "Resources ") +
                             (File.Exists(sa) ? "" : "StreamingAssets") +
                             " -- CanonicalJson silently changes what a shipped build loads.");
                return;
            }
            byte[] a = File.ReadAllBytes(res), b = File.ReadAllBytes(sa);
            bool equal = a.Length == b.Length;
            if (equal)
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { equal = false; break; }
            if (!equal)
                failures.Add("[dual-copy] " + rel + " Resources/StreamingAssets DIVERGED (" +
                             a.Length + " vs " + b.Length + " bytes).");
            else
                log.AppendLine("  [dual-copy] " + rel + " byte-identical (" + a.Length + " bytes)");
        }

        private static JObject ReadJson(string rel, List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(rel);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[catalog] CanonicalJson.Read('" + rel + "') returned empty -- unreadable catalog is a FAIL (WO-1138).");
                return null;
            }
            try
            {
                var root = JObject.Parse(json);
                if (root == null)
                {
                    failures.Add("[catalog] " + rel + " parsed to null.");
                    return null;
                }
                log.AppendLine("  [catalog] " + rel + " parsed (" + json.Length + " chars)");
                return root;
            }
            catch (Exception ex)
            {
                failures.Add("[catalog] " + rel + " PARSE FAIL: " + ex.Message + " -- unreadable catalog is a FAIL (WO-1138).");
                return null;
            }
        }

        // =====================================================================
        //  [shelf] every browsable row grants something THIS build can deliver
        // =====================================================================
        private static void CaseShelfHasDeliverableGrant(JArray packs, List<string> failures, StringBuilder log)
        {
            int shelf = 0;
            PackCatalog.Reload();
            foreach (var tok in packs)
            {
                if (!(tok is JObject p)) continue;
                string sku = p["sku"]?.Value<string>();
                if (string.IsNullOrEmpty(sku)) continue;
                var def = PackCatalog.Find(sku);
                if (def == null || !PackCatalog.IsOnBrowsableShelf(def)) continue;
                shelf++;

                bool economy = HasPositiveEconomy(p["contents"]?["economy"] as JObject);
                bool cosmetics = (p["contents"]?["cosmetics"] as JArray)?.Count > 0;
                bool redeemableConv = false;
                var conv = p["contents"]?["convenience"] as JArray;
                if (conv != null)
                    foreach (var item in conv)
                    {
                        string kind = (item as JObject)?["kind"]?.Value<string>();
                        if (!string.IsNullOrEmpty(kind) && PackCatalog.IsRedeemableConvenience(kind))
                            redeemableConv = true;
                    }

                if (!economy && !cosmetics && !redeemableConv)
                    failures.Add("[shelf] '" + sku + "' is on the browsable shelf and grants NOTHING this build can deliver " +
                                 "(empty economy, no cosmetics, no redeemable convenience). A visible SKU cannot sell vapor (WO-1246).");
            }
            if (shelf == 0)
                failures.Add("[shelf] zero browsable packs -- either the catalog is empty or IsOnBrowsableShelf hid the whole store.");
            log.AppendLine("  [shelf] " + shelf + " browsable SKU(s) each grant at least one deliverable");
        }

        private static void CaseNoVisibleVaporConvenience(JArray packs, List<string> failures, StringBuilder log)
        {
            int checkedConv = 0;
            foreach (var tok in packs)
            {
                if (!(tok is JObject p)) continue;
                string sku = p["sku"]?.Value<string>();
                var def = PackCatalog.Find(sku);
                if (def == null || !PackCatalog.IsOnBrowsableShelf(def)) continue;
                var conv = p["contents"]?["convenience"] as JArray;
                if (conv == null) continue;
                foreach (var item in conv)
                {
                    string kind = (item as JObject)?["kind"]?.Value<string>();
                    if (string.IsNullOrEmpty(kind)) continue;
                    checkedConv++;
                    if (!PackCatalog.IsRedeemableConvenience(kind))
                        failures.Add("[no-vapor] shelf pack '" + sku + "' advertises convenience '" + kind +
                                     "' which NOTHING spends (IsRedeemableConvenience==false). Ship the redeemer, then the line.");
                }
            }
            log.AppendLine("  [no-vapor] " + checkedConv + " advertised convenience kind(s) on the shelf, all redeemable");
        }

        private static void CaseMonthlyCardsGrantable(List<string> failures, StringBuilder log)
        {
            JObject root = ReadJson(MonthlyRel, failures, log);
            if (root == null) return;
            var cards = root["monthlyCards"] as JArray;
            if (cards == null || cards.Count == 0)
            {
                failures.Add("[monthly] battle_monthly.json has no monthlyCards[] -- unreadable/empty is a FAIL (WO-1138).");
                return;
            }
            int ok = 0;
            foreach (var tok in cards)
            {
                if (!(tok is JObject card)) continue;
                string sku = card["sku"]?.Value<string>();
                if (string.IsNullOrEmpty(sku))
                {
                    failures.Add("[monthly] a monthly card has no sku.");
                    continue;
                }
                var table = card["dailyTable"] as JArray;
                if (table == null || table.Count == 0)
                {
                    failures.Add("[monthly] '" + sku + "' has an empty dailyTable -- the card sells 0 claims.");
                    continue;
                }
                bool anyGrant = false;
                foreach (var day in table)
                {
                    var grant = (day as JObject)?["grant"] as JObject;
                    if (grant == null) continue;
                    string kind = grant["kind"]?.Value<string>();
                    if (string.IsNullOrEmpty(kind)) continue;
                    if (string.Equals(kind, "convenience_token", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kind, "convenience", StringComparison.OrdinalIgnoreCase))
                    {
                        string ck = grant["convenience"]?["kind"]?.Value<string>();
                        if (!string.IsNullOrEmpty(ck) && !PackCatalog.IsRedeemableConvenience(ck))
                            failures.Add("[monthly] '" + sku + "' drip convenience '" + ck + "' has no redeemer.");
                        else anyGrant = true;
                    }
                    else
                    {
                        anyGrant = true;
                    }
                }
                if (!anyGrant)
                    failures.Add("[monthly] '" + sku + "' has no deliverable drip -- a visible card cannot sell nothing.");
                else ok++;
            }
            log.AppendLine("  [monthly] " + ok + "/" + cards.Count + " card(s) have a deliverable drip");
        }

        // =====================================================================
        //  [live-grant] drive ApplyPackContents for every shelf SKU
        // =====================================================================
        private static void CaseLiveGrant(JArray packs, List<string> failures, List<string> notes, StringBuilder log)
        {
            Type vmType = FindType("DeNelle.Wallet.PackStoreVM");
            Type catType = FindType("DeNelle.Wallet.PackCatalog");
            Type glimType = FindType("DeNelle.Cosmetics.CosmeticOwnershipService");
            if (vmType == null || catType == null || glimType == null)
            {
                failures.Add("[live-grant] pack types not loaded (PackStoreVM=" + (vmType != null) +
                             ", PackCatalog=" + (catType != null) + ", CosmeticOwnershipService=" + (glimType != null) + ")");
                return;
            }

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;
            bool hadCos = PlayerPrefs.HasKey(CosmeticsKey);
            string rawCos = hadCos ? PlayerPrefs.GetString(CosmeticsKey, null) : null;
            PlayerPrefs.DeleteKey(CosmeticsKey);

            GameStateService priorGss = GameStateService.Instance;
            object priorEcon = GetInstance(typeof(EconomyService));
            object priorGlim = GetInstance(glimType);
            GameObject gssGo = null, econGo = null, glimGo = null;
            GameState throwaway = null;
            int granted = 0;

            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (store-sku-grant oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    notes.Add(RegressionOutcome.PartialSkip("live-grant ApplyPackContents",
                        "GameStateService state seam not reflectable (needs fleet)"));
                    return;
                }

                econGo = new GameObject("EconomyService (store-sku-grant oracle)");
                var econ = econGo.AddComponent<EconomyService>();
                SetInstance(typeof(EconomyService), econ);
                glimGo = new GameObject("CosmeticOwnershipService (store-sku-grant oracle)");
                var glim = glimGo.AddComponent(glimType);
                SetInstance(glimType, glim);

                catType.GetMethod("Reload", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                var findM = catType.GetMethod("Find", new[] { typeof(string) });
                var vm = vmType.GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                if (vm == null || findM == null)
                {
                    failures.Add("[live-grant] PackStoreVM.CreateDefault or PackCatalog.Find unresolved.");
                    return;
                }

                foreach (var tok in packs)
                {
                    if (!(tok is JObject p)) continue;
                    string sku = p["sku"]?.Value<string>();
                    if (string.IsNullOrEmpty(sku)) continue;
                    object pack = findM.Invoke(null, new object[] { sku });
                    if (pack == null) continue;
                    var def = pack as PackDef;
                    if (def == null || !PackCatalog.IsOnBrowsableShelf(def)) continue;

                    // Wood/Iron are GameState scalars, not ResourceBalance fields (NestedTypes.cs).
                    int woodB = throwaway.Wood, ironB = throwaway.Iron;
                    int foodB = throwaway.Resources.Stone, cryB = throwaway.Resources.Crystals;
                    int coinsB = throwaway.Resources.Coins;

                    var applyM = vmType.GetMethod("ApplyPackContents", new[] { pack.GetType() });
                    if (applyM == null)
                    {
                        failures.Add("[live-grant] ApplyPackContents(PackDef) missing.");
                        return;
                    }
                    applyM.Invoke(vm, new[] { pack });

                    if (throwaway.OwnedItemIds == null || !throwaway.OwnedItemIds.Contains(sku))
                        failures.Add("[live-grant] '" + sku + "' not in OwnedItemIds after ApplyPackContents -- charged with no entitlement.");

                    int expWood = def.Contents?.Economy?.Wood ?? 0;
                    int expIron = def.Contents?.Economy?.Iron ?? 0;
                    int expFood = def.Contents?.Economy?.Stone ?? 0;
                    int expCry = def.Contents?.Economy?.Crystals ?? 0;
                    int expCoins = def.Contents?.Economy?.Coins ?? 0;
                    if (throwaway.Wood - woodB < expWood)
                        failures.Add("[live-grant] '" + sku + "' wood delta short (got " +
                                     (throwaway.Wood - woodB) + ", advertised " + expWood + ")");
                    if (throwaway.Iron - ironB < expIron)
                        failures.Add("[live-grant] '" + sku + "' iron delta short.");
                    if (throwaway.Resources.Stone - foodB < expFood)
                        failures.Add("[live-grant] '" + sku + "' stone delta short.");
                    if (throwaway.Resources.Crystals - cryB < expCry)
                        failures.Add("[live-grant] '" + sku + "' crystals delta short.");
                    if (throwaway.Resources.Coins - coinsB < expCoins)
                        failures.Add("[live-grant] '" + sku + "' coins delta short.");

                    if (def.Contents?.Convenience != null)
                        foreach (var item in def.Contents.Convenience)
                        {
                            if (item == null || item.Count <= 0 || string.IsNullOrEmpty(item.Kind)) continue;
                            if (PackCatalog.IsPermanentBuilderKind(item.Kind)) continue;
                            string key = "convenience:" + item.Kind.Trim().ToLowerInvariant();
                            int have = 0;
                            if (throwaway.GearInventory != null)
                                throwaway.GearInventory.TryGetValue(key, out have);
                            if (have < item.Count)
                                failures.Add("[live-grant] '" + sku + "' convenience '" + item.Kind +
                                             "' count " + have + " < advertised " + item.Count + ".");
                        }

                    granted++;
                }
                log.AppendLine("  [live-grant] ApplyPackContents drove " + granted + " shelf SKU(s)");
            }
            catch (Exception ex)
            {
                failures.Add("[live-grant] threw: " + ex.GetType().Name + ": " + ex.Message);
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
        }

        // =====================================================================
        //  [redeemer] instant-build token is actually spent
        // =====================================================================
        private static void CaseRedeemerConsumes(List<string> notes, List<string> failures, StringBuilder log)
        {
            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;
            GameStateService priorGss = GameStateService.Instance;
            GameObject gssGo = null;
            GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                throwaway.GearInventory = new Dictionary<string, int>
                {
                    { "convenience:instant-build", 2 },
                    { "convenience:xp-weekend", 1 },
                };
                gssGo = new GameObject("GSS (store-sku-grant redeemer)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    notes.Add(RegressionOutcome.PartialSkip("redeemer consume",
                        "GameStateService state seam not reflectable (needs fleet)"));
                    return;
                }

                ConvenienceRedeemer.ClearTimedWindowsForTests();
                if (!ConvenienceRedeemer.TrySkipBuildTimer())
                    failures.Add("[redeemer] TrySkipBuildTimer returned false with 2 instant-build charges in GearInventory.");
                if (ConvenienceRedeemer.Count(ConvenienceRedeemer.KindInstantBuild) != 1)
                    failures.Add("[redeemer] instant-build count after one consume was " +
                                 ConvenienceRedeemer.Count(ConvenienceRedeemer.KindInstantBuild) + ", expected 1.");

                float mult = ConvenienceRedeemer.XpMultiplier();
                if (mult < 1.99f)
                    failures.Add("[redeemer] XpMultiplier was " + mult + " with an xp-weekend charge in inventory; expected 2.");
                if (!ConvenienceRedeemer.IsXpWeekendActive)
                    failures.Add("[redeemer] xp-weekend window did not start after XpMultiplier consumed the token.");
                ConvenienceRedeemer.ClearTimedWindowsForTests();
                log.AppendLine("  [redeemer] instant-build consume + xp-weekend window start both landed");
            }
            catch (Exception ex)
            {
                failures.Add("[redeemer] threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                ConvenienceRedeemer.ClearTimedWindowsForTests();
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                SetGssInstance(priorGss);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave); else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }
        }

        private static bool HasPositiveEconomy(JObject econ)
        {
            if (econ == null) return false;
            foreach (var kv in econ)
                if (kv.Value != null && kv.Value.Type == JTokenType.Integer && kv.Value.Value<long>() > 0)
                    return true;
            return false;
        }

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

        private static string Finish(List<string> failures, List<string> notes, StringBuilder log)
        {
            foreach (var n in notes) log.AppendLine("  " + n);
            if (failures.Count == 0)
            {
                string extra = notes.Count > 0 ? " " + string.Join(" ", notes) : "";
                Debug.Log("STORE_SKU_GRANT_OK\n" + log + extra);
                return "STORE SKU GRANT OK - every browsable SKU has a deliverable grant path; unreadable catalog fails closed" + extra;
            }
            string reason = "store-sku-grant: " + failures.Count + " failure(s): " + string.Join(" | ", failures);
            Debug.LogError("STORE_SKU_GRANT_FAIL: " + reason + "\n" + log);
            return reason;
        }
    }
}
