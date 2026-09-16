// =============================================================================
// ResourceAuthorityRegression - WO-1212. ONE balance the player can see, spend
// and be granted into. Never two.
// -----------------------------------------------------------------------------
// THE DEFECT THIS PINS: `GameState.Stone` and `GameState.Resources.Stone` were BOTH
// persisted, BOTH guarded and BOTH time-derived-reconciled, but only Food was ever
// displayed (as "Stone") or spent. A grant routed to the other one vanished with no
// error, no log and no red test - on a build that takes real money.
//
// The assertions below are deliberately the GENERAL SHAPE, not a one-off "stone is
// gone" string match: arm (D) derives what is LIVE from EconomyService.cs - the one
// reader/spender - so ANY future balance wired into the server's clamp path without a
// spendable home fails here, whatever it is called.
//
// NO HOLLOW PASS: a missing file throws into the catch and is reported as a failure.
// This suite never returns green on an absent dependency.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1212: exactly one authority per player-facing balance.</summary>
    public static class ResourceAuthorityRegression
    {
        private const string GameStatePath  = "Assets/_Modules/Core/State/GameState.cs";
        private const string ServicePath    = "Assets/_Modules/Core/State/GameStateService.cs";
        private const string EconomyPath    = "Assets/_Modules/Village/EconomyService.cs";
        private const string SchemaPath     = "Assets/_Modules/Core/State/SaveSchema.cs";
        private const string ServerSavePath = "api/game/save.js";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("RESOURCE_AUTHORITY_OK - " + reason);
            else Debug.LogError("RESOURCE_AUTHORITY_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var keys = new List<string>();
            try
            {
                string gameState = File.ReadAllText(GameStatePath);
                string service   = File.ReadAllText(ServicePath);
                string economy   = File.ReadAllText(EconomyPath);
                string server    = File.ReadAllText(ServerSavePath);

                // (A) No second Stone FIELD on the state object.
                if (Regex.IsMatch(StripComments(gameState), @"public\s+int\s+Stone\b"))
                    failures.Add("GameState still declares a `Stone` field - the retired second Stone " +
                                 "balance is back, and a grant to it is invisible to the player");

                // (B) No runtime authority for it anywhere in the service.
                string svcCode = StripComments(service);
                // Match the complete receiver, not the trailing 's' in Resources.Stone.
                const string retiredAccess = @"\b(?:_state|s)\s*\.\s*Stone\b";
                if (Regex.IsMatch(svcCode, retiredAccess))
                    failures.Add("GameStateService still reads/writes the retired direct Stone balance");
                if (!Regex.IsMatch("_state.Stone = 1; s . Stone = 2;", retiredAccess) ||
                    Regex.IsMatch("_state.Resources.Stone = resources.Stone; s.Resources.Stone = 2;", retiredAccess))
                    failures.Add("Retired-balance classifier cannot distinguish direct state from Resources authority");

                // Runtime Stone is legitimate; only its legacy Food wire slot may
                // be emitted. Assert the payload, not a local variable spelling.
                var delta = new DeNelle.Core.State.GameStateService.SyncDeltaPayload { Stone = 37 };
                var wire = Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(
                    delta, DeNelle.Core.State.SaveSchema.JsonSettings));
                if ((int?)wire["Food"] != 37 || wire["Stone"] != null || wire["stone"] != null)
                    failures.Add("sync delta must emit one Stone amount under the legacy Food key, never a second stone balance");

                // (C) The client switch and the server list must name the SAME keys.
                string clientRead  = Method(service, "ReadTimeDerivedBalance");
                string clientWrite = Method(service, "WriteTimeDerivedBalance");
                List<string> readKeys   = CaseKeys(clientRead);
                List<string> writeKeys  = CaseKeys(clientWrite);
                List<string> serverKeys = ServerTimeDerivedKeys(server);
                keys.AddRange(readKeys);

                if (readKeys.Count == 0)
                    failures.Add("ReadTimeDerivedBalance has no case arms at all - the oracle has nothing to judge");
                if (!readKeys.SequenceEqual(writeKeys))
                    failures.Add("ReadTimeDerivedBalance [" + string.Join(",", readKeys.ToArray()) + "] and " +
                                 "WriteTimeDerivedBalance [" + string.Join(",", writeKeys.ToArray()) + "] disagree");
                if (serverKeys.Count == 0)
                    failures.Add("could not parse TIME_DERIVED_BALANCES out of " + ServerSavePath);
                else if (!readKeys.SequenceEqual(serverKeys))
                    failures.Add("client time-derived keys [" + string.Join(",", readKeys.ToArray()) + "] do not match " +
                                 "api/game/save.js TIME_DERIVED_BALANCES [" + string.Join(",", serverKeys.ToArray()) + "] - " +
                                 "one side would clamp a balance the other cannot map");

                // (D) THE GENERAL SHAPE: every clamp-able balance must have a SPENDABLE home.
                //     "Live" is not a hand-kept list here - it is whatever EconomyService, the one
                //     reader/spender, actually reads off the state object.
                string econCode = StripComments(economy);
                var targets = CaseTargets(clientRead);
                // NO HOLLOW PASS: if the arms stop being simple `return <field>;` the loop below
                // would iterate nothing and this whole check would pass by saying nothing at all.
                if (targets.Count != readKeys.Count)
                    failures.Add("ReadTimeDerivedBalance has " + readKeys.Count + " case key(s) but only " +
                                 targets.Count + " parseable `return <field>;` arm(s) - the live-slot check " +
                                 "cannot see them all, so it must not report green");
                foreach (var arm in targets)
                {
                    string expr = arm.Value.Replace("_state.", "state.").Trim();
                    if (!econCode.Contains(expr))
                        failures.Add("time-derived balance '" + arm.Key + "' resolves to `" + expr + "`, which " +
                                     "EconomyService never reads - a server clamp (or a grant) on it would move a " +
                                     "number no player can see or spend. Point it at a live slot or drop the key.");
                }

                // (E) The inbound legacy alias + the loud discard must both survive.
                if (!service.Contains("aliased.Stone") || !service.Contains("aliasedCloud.Stone"))
                    failures.Add("the inbound `stone` -> live-slot alias was removed - an older sender's value " +
                                 "would now be dropped on the floor silently");
                if (Regex.Matches(service, "DISCARDED retired").Count < 2)
                    failures.Add("the retired-balance discard is no longer traced on both the local and cloud " +
                                 "load paths - a discard nobody can see is a silent loss");

                // (F) The WIRE field must survive so an old save can still be READ (and then
                //     discarded). A deleted field cannot be read-migrated or aliased.
                if (!File.ReadAllText(SchemaPath).Contains("[JsonProperty(\"stone\")]"))
                    failures.Add("SaveSchema no longer declares the `stone` wire field - an existing save's " +
                                 "value can no longer be read, so it can be neither aliased nor knowingly discarded");
            }
            catch (Exception ex)
            {
                failures.Add("oracle threw " + ex.GetType().Name + ": " + ex.Message);
            }

            reason = failures.Count == 0
                ? "one authority per balance; time-derived keys [" + string.Join(",", keys.ToArray()) +
                  "] agree client/server and every one resolves to a slot EconomyService spends; " +
                  "legacy `stone` aliases inbound and its stored value is discarded aloud"
                : string.Join(" | ", failures.ToArray());
            return failures.Count == 0;
        }

        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(source, @"//[^\r\n]*", "");
        }

        private static string Method(string source, string name)
        {
            var decl = Regex.Match(source,
                @"\b(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?[\w<>\?\[\]]+\s+" +
                Regex.Escape(name) + @"\s*\(");
            if (!decl.Success) throw new InvalidOperationException("method not found: " + name);
            int open = source.IndexOf('{', decl.Index);
            if (open < 0) throw new InvalidOperationException("method body not found: " + name);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            throw new InvalidOperationException("unterminated method: " + name);
        }

        private static List<string> CaseKeys(string body)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(StripComments(body), "case\\s+\"([a-z]+)\"\\s*:"))
                list.Add(m.Groups[1].Value);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>case key -> the expression the READ arm returns (its backing field).</summary>
        private static List<KeyValuePair<string, string>> CaseTargets(string body)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (Match m in Regex.Matches(StripComments(body), "case\\s+\"([a-z]+)\"\\s*:\\s*return\\s+([^;]+);"))
                list.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
            return list;
        }

        private static List<string> ServerTimeDerivedKeys(string js)
        {
            var list = new List<string>();
            var m = Regex.Match(js, @"const\s+TIME_DERIVED_BALANCES\s*=\s*\[([^\]]*)\]");
            if (!m.Success) return list;
            foreach (Match k in Regex.Matches(m.Groups[1].Value, @"'([a-z]+)'"))
                list.Add(k.Groups[1].Value);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
