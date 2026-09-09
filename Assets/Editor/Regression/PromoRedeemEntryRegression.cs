// =============================================================================
// PromoRedeemEntryRegression [promo-redeem-entry] — the promo-code DOOR.
// Marker: PROMO_REDEEM_ENTRY_OK / PROMO_REDEEM_ENTRY_FAIL. Expected: GREEN.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. SOURCE-LINT + localization-data oracle (no PlayMode),
// so it slots straight into the headless DataRegression.RunAll batch gate.
//
// WHAT IT PINS (the promo-redeem door WO)
// The whole promo stack — the identity-gated api/promo/redeem endpoint, PromoCodeService,
// the local dedup set, the reward grant — shipped FULLY BUILT with no way for a player to
// reach it. The one UI that existed (PromoCodeUI) needs a UIDocument, and UXML does not
// render in player builds (CLAUDE.md §8), so it could never have been the door. This suite
// keeps the new door open and keeps it honest:
//
//   1. ROUTING    — RedeemCodePanel drives PromoCodeService and speaks NO HTTP itself.
//                   A second client would duplicate the identity-proof + error taxonomy,
//                   and the copy that drifts is the one that silently burns codes.
//   2. NO LEAK    — the code string never reaches a log/trace/analytics call in either the
//                   panel or the service. F8 captures get shared; a live promo code in a
//                   capture is spendable by whoever reads it.
//   3. DISTINCT   — every documented server error (INVALID_CODE / ALREADY_REDEEMED /
//                   EXPIRED / PLAYER_LIMIT_REACHED) plus the offline and identity cases maps
//                   to its OWN non-empty localized sentence. A bare "invalid code" on a redeem
//                   screen reads as a scam: the player cannot tell a typo from a spent code
//                   from an outage on our side.
//   4. LOCALIZED  — PromoStrings forwards to LocalText, and its English rows exist,
//                   byte-identical, in both localizable source copies.
//   5. UNGATED    — the store entry is NOT gated on FeatureFlags.RealmStorePurchase. That
//                   flag gates BUYING; redeeming spends no money and must work while
//                   purchases are disabled. Proven structurally: the button is built in
//                   PackStore.EnsureBuilt, and that method never names the flag.
//   6. ONE GRANT  — the reward lands through the pack seam (EconomyService.
//                   GrantSpendablePurchased / AddCoins — BankGrantKind.PurchasedOrPromised,
//                   never clamped by the town bank cap, WO-857 Phase F) and NOT by writing
//                   state.Resources directly, which is what it used to do.
//   7. DEAD PANEL — PromoCodeUI still carries its "do not wire this" header, so the next
//                   reader does not re-point the store at the UIDocument panel.
//
// Wire (DataRegression.RunAll):
//   if (!PromoRedeemEntryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[promo-redeem-entry] " + r);
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class PromoRedeemEntryRegression
    {
        private const string PanelPath   = "Assets/_Modules/Wallet/RedeemCodePanel.cs";
        // WO-1512: the redeem VERB moved off the View onto its ViewModel (presentation never
        // touches the objects, ARCHITECTURE_PRINCIPLES §2). The ROUTING half of case 1 follows the
        // seam here; the PRESENTATION half stays on the panel, and a new check pins that the panel
        // does NOT reach the service directly again.
        private const string VmPath      = "Assets/_Modules/Wallet/RedeemCodeVM.cs";
        private const string StorePath   = "Assets/_Modules/Wallet/PackStore.cs";
        private const string ServicePath = "Assets/_Modules/Core/Promo/PromoCodeService.cs";
        private const string StringsPath = "Assets/_Modules/Core/Promo/PromoStrings.cs";
        private const string DeadUiPath  = "Assets/_Modules/Core/Promo/PromoCodeUI.cs";
        private const string EnglishResources       = "Assets/Resources/Data/Canonical/en.json";
        private const string EnglishStreamingAssets = "Assets/StreamingAssets/Data/Canonical/en.json";

        // The localization keys the failure taxonomy hangs on. Every one must exist, be non-empty,
        // be ASCII, and be DIFFERENT from all the others.
        private static readonly string[] FailureKeys =
        {
            "promo.redeem.error.empty", "promo.redeem.error.invalid",
            "promo.redeem.error.alreadyUsed", "promo.redeem.error.expired",
            "promo.redeem.error.playerLimit", "promo.redeem.error.offline",
            "promo.redeem.error.identity", "promo.redeem.error.signIn",
            "promo.redeem.error.unknown",
        };

        // The chrome/success copy the screen cannot render without.
        private static readonly string[] ChromeKeys =
        {
            "promo.redeem.entry", "promo.redeem.title", "promo.redeem.blurb",
            "promo.redeem.placeholder", "promo.redeem.action", "promo.redeem.hint",
            "promo.redeem.busy", "promo.redeem.success", "promo.redeem.successNoReward",
            "promo.redeem.rewardCrystals", "promo.redeem.rewardCoins", "promo.redeem.rewardPack",
        };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- PROMO REDEEM ENTRY (the promo system has a door, and the door is honest) ---");

            try
            {
                CheckPanelRoutesThroughService(failures, log);
                CheckNoCodeEverLogged(failures, log);
                CheckErrorTaxonomyIsDistinct(failures, log);
                CheckStringsUseLocalizationAuthority(failures, log);
                CheckLocalizableStringsDualCopy(failures, log);
                CheckEntryIsNotPurchaseGated(failures, log);
                CheckRewardUsesThePackSeam(failures, log);
                CheckDeadUiStaysLabelled(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add($"promo-redeem-entry oracle threw: {ex.GetType().Name}: {ex.Message}");
            }

            return Finish(failures, log, out reason);
        }

        // ── 1. the panel is a VIEW over the service, never a second client ────
        private static void CheckPanelRoutesThroughService(List<string> failures, StringBuilder log)
        {
            string panel = ReadRepoFile(PanelPath);
            if (panel == null)
            {
                failures.Add($"{PanelPath} missing — the promo system has NO player entry point (that absence IS the defect this suite exists for)");
                return;
            }

            // CODE only. The header of that file explains at length why it must NOT use a
            // UnityWebRequest or a UIDocument — prose naming the banned thing is the opposite of
            // doing it, and a scan that cannot tell them apart punishes the documentation.
            panel = StripLineComments(panel);

            // WO-1512: the panel must NOT resolve the service any more — it binds RedeemCodeVM.
            // This is the §2 pin: a View that transacts against a currency-minting backend is the
            // breach, and re-inlining PromoCodeService here is how it comes back.
            if (panel.IndexOf("PromoCodeService", StringComparison.Ordinal) >= 0)
                failures.Add("RedeemCodePanel names PromoCodeService in CODE — WO-1512 moved the redeem verb to " +
                             "RedeemCodeVM. A View that resolves the promo service transacts against a live " +
                             "currency-minting backend from the presentation layer (ARCHITECTURE_PRINCIPLES §2).");
            if (panel.IndexOf("_vm", StringComparison.Ordinal) < 0 ||
                panel.IndexOf("RedeemCodeVM", StringComparison.Ordinal) < 0)
                failures.Add("RedeemCodePanel does not bind RedeemCodeVM — the door is not connected to the system it opens");

            foreach (var banned in new[] { "UnityWebRequest", "UploadHandlerRaw", "DownloadHandlerBuffer", "api/promo" })
                if (panel.IndexOf(banned, StringComparison.Ordinal) >= 0)
                    failures.Add($"RedeemCodePanel names '{banned}' — it is speaking to the backend directly. " +
                                 "A second client duplicates the identity proof and the error taxonomy, and the copy " +
                                 "that drifts silently burns real codes. Route through PromoCodeService.");

            // The routing half now lives on the VM. Same assertions, followed to where the work went.
            string vm = ReadRepoFile(VmPath);
            if (vm == null)
            {
                failures.Add($"{VmPath} missing — RedeemCodePanel's ViewModel is gone, so nothing calls PromoCodeService");
            }
            else
            {
                vm = StripLineComments(vm);
                if (vm.IndexOf("PromoCodeService", StringComparison.Ordinal) < 0 ||
                    vm.IndexOf("RedeemAsync", StringComparison.Ordinal) < 0)
                    failures.Add("RedeemCodeVM does not call PromoCodeService.RedeemAsync — the door is not connected to the system it opens");

                foreach (var banned in new[] { "UnityWebRequest", "UploadHandlerRaw", "DownloadHandlerBuffer", "api/promo" })
                    if (vm.IndexOf(banned, StringComparison.Ordinal) >= 0)
                        failures.Add($"RedeemCodeVM names '{banned}' — it is speaking to the backend directly instead of " +
                                     "routing through PromoCodeService, which is the one client that owns the identity " +
                                     "proof and the error taxonomy.");

                // The endpoint stores/compares uppercase: a lowercase entry must not read as "no such code".
                if (vm.IndexOf("ToUpperInvariant", StringComparison.Ordinal) < 0)
                    failures.Add("RedeemCodeVM never uppercases the entry — the endpoint compares uppercase, so a player " +
                                 "typing their real code in lower case would be told it does not exist");

                // Subscribe AND unsubscribe: a detached VM that still handles callbacks writes into dead UI.
                if (vm.IndexOf("OnRedeemed", StringComparison.Ordinal) < 0 ||
                    vm.IndexOf("OnRedeemFailed", StringComparison.Ordinal) < 0)
                    failures.Add("RedeemCodeVM does not subscribe to OnRedeemed/OnRedeemFailed — the player would get no answer at all");
                if (vm.IndexOf("-= RaiseRedeemed", StringComparison.Ordinal) < 0 ||
                    vm.IndexOf("-= RaiseFailed", StringComparison.Ordinal) < 0)
                    failures.Add("RedeemCodeVM never unsubscribes from the service events — a closed panel keeps handling redeem callbacks");
            }

            // A UIDocument here would repeat the exact mistake that left PromoCodeUI unreachable.
            if (panel.IndexOf("UIDocument", StringComparison.Ordinal) >= 0)
                failures.Add("RedeemCodePanel references UIDocument — UXML does not render in player builds (CLAUDE.md §8). " +
                             "This screen must stay code-built uGUI on the Obsidian kit.");

            if (panel.IndexOf("ElarionUiKit", StringComparison.Ordinal) < 0)
                failures.Add("RedeemCodePanel does not route through ElarionUiKit — it is hand-rolling chrome instead of using the house frame");

            // The panel still has to WIRE the two outcomes to its own status line, and still has to
            // let go on Close — the assertions kept their meaning, they just changed subject from
            // the service's events to the VM's.
            if (panel.IndexOf("Redeemed += HandleRedeemed", StringComparison.Ordinal) < 0 ||
                panel.IndexOf("Failed   += HandleFailed", StringComparison.Ordinal) < 0)
                failures.Add("RedeemCodePanel does not bind the VM's Redeemed/Failed outcomes — the player would get no answer at all");
            if (panel.IndexOf("_vm.Detach()", StringComparison.Ordinal) < 0)
                failures.Add("RedeemCodePanel never detaches its VM on close — a closed panel keeps handling redeem callbacks");

            // A successful redeem can open the Welcome Letter over this panel. Closing that letter
            // must reveal a receipt, not another live Redeem button (which also overlaps the longer
            // success copy on Seeker). Reopening the panel later restores entry for another code.
            string redeemed = ExtractMethodBody(panel, "private void HandleRedeemed(PromoReward reward)");
            string opened = ExtractMethodBody(panel, "public void Open()");
            string visibility = ExtractMethodBody(panel, "private void SetEntryVisible(bool visible)");
            if (redeemed == null || redeemed.IndexOf("SetEntryVisible(false)", StringComparison.Ordinal) < 0)
                failures.Add("RedeemCodePanel leaves its input/Redeem button visible after success; closing the Welcome Letter returns the player to an overlapping, reusable redeem form");
            if (opened == null || opened.IndexOf("SetEntryVisible(true)", StringComparison.Ordinal) < 0)
                failures.Add("RedeemCodePanel does not restore its entry controls on Open; hiding them after one success would permanently remove the door for other promo codes");
            if (visibility == null ||
                visibility.IndexOf("_input.gameObject.SetActive(visible)", StringComparison.Ordinal) < 0 ||
                visibility.IndexOf("_submit.gameObject.SetActive(visible)", StringComparison.Ordinal) < 0)
                failures.Add("RedeemCodePanel's success-state toggle does not control both the input and Redeem button");

            log.AppendLine("  panel: drives PromoCodeService, no direct HTTP, no UIDocument, subscribes + unsubscribes; success leaves receipt + Close only");
        }

        // ── 2. ⛔ the code string never reaches a log / trace / analytics call ─
        private static void CheckNoCodeEverLogged(List<string> failures, StringBuilder log)
        {
            foreach (var path in new[] { ServicePath, PanelPath })
            {
                string src = ReadRepoFile(path);
                if (src == null) { failures.Add($"{path} unreadable — the no-leak rule cannot be verified"); continue; }

                int scanned = 0;
                foreach (var statement in LoggingStatements(StripLineComments(src)))
                {
                    scanned++;
                    // Case-sensitive \bcode\b: 'errorCode' / 'responseCode' / 'canonKey' do not match,
                    // and neither does '_code'. What DOES match is the raw entered code.
                    if (Regex.IsMatch(statement, @"\bcode\b"))
                        failures.Add($"{Path.GetFileName(path)} logs the code string: \"{Trim(statement)}\". " +
                                     "F8 captures get shared and a live promo code inside one is spendable by anyone " +
                                     "who reads it. Trace the OUTCOME (redeemed / expired / already-used / refused / " +
                                     "offline), never the input.");
                }
                log.AppendLine($"  {Path.GetFileName(path)}: {scanned} log/trace/analytics statement(s) scanned, none carries the code");
            }
        }

        // ── 3. one distinct sentence per documented cause ─────────────────────
        private static void CheckErrorTaxonomyIsDistinct(List<string> failures, StringBuilder log)
        {
            string svc = ReadRepoFile(ServicePath);
            if (svc == null) { failures.Add($"{ServicePath} unreadable — the error mapping cannot be verified"); return; }

            var documented = new[] { "INVALID_CODE", "ALREADY_REDEEMED", "EXPIRED", "PLAYER_LIMIT_REACHED" };
            var mapped = new Dictionary<string, string>();
            foreach (var err in documented)
            {
                var m = Regex.Match(svc, "\"" + err + "\"\\s*=>\\s*PromoStrings\\.(\\w+)");
                if (!m.Success)
                {
                    failures.Add($"PromoCodeService does not map the documented backend error '{err}' to its own localization key — " +
                                 "that failure would fall through to the generic sentence and the player could not tell " +
                                 "what actually happened to their code");
                    continue;
                }
                mapped[err] = m.Groups[1].Value;
            }

            var seenKeys = new Dictionary<string, string>();   // localization key -> first error that claimed it
            foreach (var kv in mapped)
            {
                if (kv.Value == "KeyErrUnknown")
                    failures.Add($"'{kv.Key}' is mapped to the catch-all KeyErrUnknown — a documented cause must say what it is");
                if (seenKeys.TryGetValue(kv.Value, out var firstOwner))
                    failures.Add($"'{kv.Key}' and '{firstOwner}' share the localization key '{kv.Value}' — two different causes reading as one sentence");
                else
                    seenKeys[kv.Value] = kv.Key;
            }

            // The two non-server cases the spec calls out by name.
            if (svc.IndexOf("KeyErrOffline", StringComparison.Ordinal) < 0)
                failures.Add("PromoCodeService never uses KeyErrOffline — an unreachable server would be reported as something else, " +
                             "and the player would not be told their code was NOT used");
            if (svc.IndexOf("KeyErrIdentity", StringComparison.Ordinal) < 0)
                failures.Add("PromoCodeService never uses KeyErrIdentity — a refused identity proof would be indistinguishable from a bad code");

            // No sentence may be typed inline: the copy lives behind LocalText (§7).
            if (Regex.IsMatch(svc, @"OnRedeemFailed\?\.Invoke\(\s*""") || Regex.IsMatch(svc, @"OnRedeemFailed\?\.Invoke\(\s*\$"""))
                failures.Add("PromoCodeService raises OnRedeemFailed with a hardcoded sentence — player copy belongs behind LocalText (CLAUDE.md §7)");

            log.AppendLine($"  error taxonomy: {mapped.Count}/4 documented errors mapped to distinct localization keys, plus offline + identity");
        }

        // ── 4. one localization authority + byte-identical English sources ───
        private static void CheckStringsUseLocalizationAuthority(List<string> failures, StringBuilder log)
        {
            string source = ReadRepoFile(StringsPath);
            if (source == null)
            {
                failures.Add($"{StringsPath} unreadable — the promo localization seam cannot be verified");
                return;
            }

            string code = StripLineComments(source);
            if (code.IndexOf("LocalText.Get", StringComparison.Ordinal) < 0 ||
                code.IndexOf("LocalText.Format", StringComparison.Ordinal) < 0)
                failures.Add("PromoStrings no longer forwards Get + Format through LocalText — promo copy has split from the runtime localization authority");

            if (code.IndexOf("CanonicalJson.Read", StringComparison.Ordinal) >= 0 ||
                code.IndexOf("JsonConvert.DeserializeObject", StringComparison.Ordinal) >= 0 ||
                code.IndexOf("CanonRelativePath", StringComparison.Ordinal) >= 0)
                failures.Add("PromoStrings still owns a direct canonical JSON reader/cache — it must remain a thin LocalText compatibility catalog");

            log.AppendLine("  localization seam: PromoStrings Get + Format forward to LocalText; no private JSON reader remains");
        }

        private static void CheckLocalizableStringsDualCopy(List<string> failures, StringBuilder log)
        {
            string res = ReadRepoFile(EnglishResources);
            string sa  = ReadRepoFile(EnglishStreamingAssets);
            if (res == null || sa == null)
            {
                failures.Add("an en.json copy is unreadable — the redeem screen's words cannot be verified");
                return;
            }

            var values = new Dictionary<string, string>();
            foreach (var key in Concat(FailureKeys, ChromeKeys))
            {
                string vRes = ExtractStringValue(res, key);
                string vSa  = ExtractStringValue(sa, key);

                if (vRes == null)
                {
                    failures.Add($"en.json key '{key}' missing from the Resources copy — the screen would render the " +
                                 $"literal '[[missing:{key}]]' where a sentence belongs");
                    continue;
                }
                if (vSa == null)
                {
                    failures.Add($"en.json key '{key}' missing from the StreamingAssets copy — LocalText falls back to it, " +
                                 "so a build that ships without Resources would lose this sentence");
                    continue;
                }
                if (!string.Equals(vRes, vSa, StringComparison.Ordinal))
                    failures.Add($"en.json '{key}' DIFFERS between the Resources and StreamingAssets copies " +
                                 "— the same screen would read differently depending on which file loaded");

                if (string.IsNullOrWhiteSpace(vRes))
                    failures.Add($"en.json '{key}' is empty — a blank line on a redeem screen is a silent failure");

                foreach (char c in vRes)
                    if (c > 126)
                    {
                        failures.Add($"en.json '{key}' contains a non-ASCII character — TMP renders it as tofu on device");
                        break;
                    }

                values[key] = vRes;
            }

            // Distinctness applies to the FAILURE sentences: the whole point is that the player can
            // tell the causes apart.
            foreach (var a in FailureKeys)
                foreach (var b in FailureKeys)
                {
                    if (string.CompareOrdinal(a, b) >= 0) continue;
                    if (values.TryGetValue(a, out var va) && values.TryGetValue(b, out var vb) &&
                        string.Equals(va, vb, StringComparison.Ordinal))
                        failures.Add($"en.json '{a}' and '{b}' are the SAME sentence — two different failures the player " +
                                     "cannot tell apart is the vague-refusal defect this suite exists to prevent");
                }

            // Success must not silently claim a reward the grant path did not deliver.
            if (values.TryGetValue("promo.redeem.success", out var success) && success.IndexOf("{0}", StringComparison.Ordinal) < 0)
                failures.Add("en.json 'promo.redeem.success' lost its {0} placeholder — the reward amounts would never reach the player");

            log.AppendLine($"  localization data: {values.Count} redeem keys present in BOTH en.json copies, ASCII, failure sentences all distinct");
        }

        // ── 5. the entry is NOT gated on the purchase flag ────────────────────
        private static void CheckEntryIsNotPurchaseGated(List<string> failures, StringBuilder log)
        {
            string panel = ReadRepoFile(PanelPath);
            if (panel != null) panel = StripLineComments(panel);   // its header explains the rule; that is not a use
            if (panel != null && panel.IndexOf("RealmStorePurchase", StringComparison.Ordinal) >= 0)
                failures.Add("RedeemCodePanel names FeatureFlags.RealmStorePurchase — that flag gates BUYING. Redeeming spends " +
                             "no money and must work while purchases are disabled.");

            string store = ReadRepoFile(StorePath);
            if (store == null) { failures.Add($"{StorePath} unreadable — the store entry cannot be verified"); return; }

            if (store.IndexOf("RedeemCodePanel", StringComparison.Ordinal) < 0 ||
                store.IndexOf("PromoStrings.KeyEntry", StringComparison.Ordinal) < 0)
            {
                failures.Add("PackStore has no Redeem-a-Code entry (no RedeemCodePanel / PromoStrings.KeyEntry) — the promo " +
                             "system is unreachable again, which is the exact state this WO closed");
                return;
            }

            // Comments are STRIPPED first: this file's own comment explains at the build site that the
            // entry is deliberately ungated, and naming the flag in prose must not read as gating on it.
            string ensureBuilt = ExtractMethodBody(StripLineComments(store), "private void EnsureBuilt()");
            if (ensureBuilt == null)
            {
                failures.Add("PackStore.EnsureBuilt not found — cannot prove the redeem entry is built outside the purchase gate");
                return;
            }
            if (ensureBuilt.IndexOf("PromoStrings.KeyEntry", StringComparison.Ordinal) < 0)
                failures.Add("the Redeem-a-Code button is not built in PackStore.EnsureBuilt — it has moved into a conditional " +
                             "path (a pack card / a purchase branch) where a flag or an empty catalogue can swallow it");
            if (ensureBuilt.IndexOf("RealmStorePurchase", StringComparison.Ordinal) >= 0)
                failures.Add("PackStore.EnsureBuilt now tests RealmStorePurchase — the redeem entry lives in that method and " +
                             "must not be reachable only when purchases are enabled");

            log.AppendLine("  entry: built unconditionally in PackStore.EnsureBuilt; neither it nor the panel names RealmStorePurchase");
        }

        // ── 6. one grant path, and it is the pack's ───────────────────────────
        private static void CheckRewardUsesThePackSeam(List<string> failures, StringBuilder log)
        {
            string svc = ReadRepoFile(ServicePath);
            if (svc == null) { failures.Add($"{ServicePath} unreadable — the grant seam cannot be verified"); return; }

            if (svc.IndexOf("GrantSpendablePurchased", StringComparison.Ordinal) < 0)
                failures.Add("PromoCodeService does not grant through EconomyService.GrantSpendablePurchased — a promo reward " +
                             "would be clamped by the town bank cap and under-deliver what the code promised (WO-857 Phase F)");
            if (svc.IndexOf("AddCoins", StringComparison.Ordinal) < 0)
                failures.Add("PromoCodeService does not grant coins through EconomyService.AddCoins — the coin half of a reward is lost");
            if (Regex.IsMatch(svc, @"state\.Resources\s*="))
                failures.Add("PromoCodeService writes state.Resources directly — that is a SECOND grant path, bypassing the " +
                             "persisted, cap-aware, HUD-refreshing seam every purchased grant uses");

            log.AppendLine("  grant: routes to EconomyService.GrantSpendablePurchased + AddCoins (the pack seam); no direct Resources write");
        }

        // ── 7. the dead UIDocument panel keeps its warning ────────────────────
        private static void CheckDeadUiStaysLabelled(List<string> failures, StringBuilder log)
        {
            string dead = ReadRepoFile(DeadUiPath);
            if (dead == null) { log.AppendLine("  (PromoCodeUI.cs absent — nothing to mislabel)"); return; }

            if (dead.IndexOf("RedeemCodePanel", StringComparison.Ordinal) < 0)
                failures.Add("PromoCodeUI.cs no longer points at RedeemCodePanel — the next reader will wire the UIDocument " +
                             "panel that cannot render in a player build (CLAUDE.md §8)");
            else
                log.AppendLine("  PromoCodeUI.cs still carries its do-not-wire header pointing at RedeemCodePanel");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static IEnumerable<string> Concat(string[] a, string[] b)
        {
            foreach (var s in a) yield return s;
            foreach (var s in b) yield return s;
        }

        /// <summary>Drops // comments so prose about the code string is not mistaken for a log of it.</summary>
        private static string StripLineComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            foreach (var line in src.Split('\n'))
            {
                int i = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(i >= 0 ? line.Substring(0, i) : line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Every statement that reaches a log sink (Debug / FlowTrace / analytics).</summary>
        private static IEnumerable<string> LoggingStatements(string strippedSource)
        {
            foreach (var raw in strippedSource.Split(';'))
            {
                if (raw.IndexOf("Debug.Log", StringComparison.Ordinal) >= 0 ||
                    raw.IndexOf("FlowTrace.", StringComparison.Ordinal) >= 0 ||
                    raw.IndexOf("EventTracker.Track", StringComparison.Ordinal) >= 0)
                    yield return raw;
            }
        }

        private static string Trim(string s)
        {
            s = Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();
            return s.Length <= 160 ? s : s.Substring(0, 157) + "...";
        }

        /// <summary>Pulls a flat JSON string value by key, or null when the key is absent.</summary>
        private static string ExtractStringValue(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Declared as a PAIR so this file's own brace count stays balanced (CLAUDE.md §1 runs a naive
        // open-vs-close count, so an odd number of brace char literals reads to it as a mismatch).
        private const char OpenBrace  = '{';
        private const char CloseBrace = '}';

        /// <summary>Returns the brace-matched body of the method whose signature line contains <paramref name="signature"/>.</summary>
        private static string ExtractMethodBody(string src, string signature)
        {
            int i = src.IndexOf(signature, StringComparison.Ordinal);
            if (i < 0) return null;
            int open = src.IndexOf(OpenBrace, i);
            if (open < 0) return null;
            int depth = 0;
            for (int j = open; j < src.Length; j++)
            {
                if (src[j] == OpenBrace) depth++;
                else if (src[j] == CloseBrace)
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, j - open + 1);
                }
            }
            return null;
        }

        /// <summary>Repo-relative read. The repo ROOT is machine-dependent (CLAUDE.md §0), so it is
        /// resolved at runtime from Application.dataPath and never hardcoded.</summary>
        private static string ReadRepoFile(string repoRelativePath)
        {
            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                string full = Path.Combine(root, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(full) ? File.ReadAllText(full) : null;
            }
            catch { return null; }
        }

        private static bool Finish(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "PROMO_REDEEM_ENTRY_OK");
                reason = "PROMO REDEEM ENTRY OK -- the Realm Store carries an ungated Redeem-a-Code door, it drives " +
                         "PromoCodeService (no second HTTP client), every documented failure has its own localized sentence in " +
                         "both copies, the code string is never logged, and the reward lands on the uncapped pack grant seam";
                return true;
            }
            reason = "promo-redeem-entry: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "PROMO_REDEEM_ENTRY_FAIL: " + reason);
            return false;
        }
    }
}
