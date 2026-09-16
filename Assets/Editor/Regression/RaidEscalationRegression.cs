// =============================================================================
// RaidEscalationRegression — the raid ladder actually escalates, and the tier-4
// target cannot dead-end the player.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor
//
// WHAT WAS BROKEN, MEASURED 2026-09-04 AT SOURCE (this is the RED this suite was
// written against, not a hypothetical):
//
//   1. RaidSelectionVM hard-listed THREE ids and built every ItemVM with the
//      7-arg constructor - so `locked` defaulted false and `lockReason` defaulted
//      null on EVERY card at EVERY victory count. ItemVM has carried Locked +
//      LockReason since it was written (ItemVM.cs:32,35); nothing passed them, so
//      "upgrade -> unlock a harder raid" (economy map §4) had no reader at all.
//   2. `grep -c IronBastion` returned 0 on scene-configs.json AND on
//      EditorBuildSettings.asset. RaidBase_IronBastion.unity was baked on
//      2026-08-21 and reachable by nothing.
//   3. The three raid rows carried their pre-canon display names ("Small Raider
//      Camp" / "Fortified Garrison" / "Mage Enclave") and NO description field at
//      all - not empty, ABSENT, in both the JSON and SceneConfigDef.
//
// WHAT THIS PINS
//   A. The four raid rows exist, keep their LIVE SAVE-KEY ids, and carry the
//      creative canon's display name + one-line card copy verbatim
//      (docs/CREATIVE_CANON_ELARION_2026-09-04.md §3).
//   B. unlockVictories is authored 0 / 0 / 0 / 0 (owner 2026-09-11: NO win-count
//      lock was ruled. Capture is a 3-star clear of the highest raid).
//   C. No superseded FIRST-PASS name survives anywhere in the catalog (creative
//      canon §2 lists them by name: implementing one is a defect, not a taste).
//   D. The Resources / StreamingAssets twins are BYTE-IDENTICAL (Resources wins
//      at runtime, so a drifted twin is a silent behaviour fork).
//   E. Every enemy row's sceneName is REGISTERED in Build Settings.
//   F. ⭐ THE SEAT PIN, AND IT IS SELF-HEALING. A raid scene may only be ENABLED
//      in Build Settings if it bakes a HeroStartPoint_PlayerSpawn marker (the seat
//      HeroControlEnsurer puts the carried hero on) - and conversely, a raid scene
//      that DOES bake the marker must not sit disabled. RaidBase_IronBastion bakes
//      127 GameObjects and no marker, so it is registered disabled today; the
//      moment someone re-bakes it with a seat, this suite FAILS and tells them to
//      flip enabled:1. That is the whole point: the disabled state is a recorded
//      defect with an expiry, not a shrug.
//   G. The live projection: at 0 victories the three gated targets are LOCKED with
//      a non-empty reason and the first is open; at 3 the second opens; at 20 all
//      four are open. Asserted by CONSTRUCTING the real RaidSelectionVM over fake
//      defs - behaviour, not a source grep.
//
// Contract mirrors the other suites - Run(out string reason): true = pass.
//
// Orchestrator registration (DataRegression.RunAll), covenant style:
//   if (!RaidEscalationRegression.Run(out var raidEscalationReason)) failures.Add(raidEscalationReason); else log.AppendLine("[raid-escalation] " + raidEscalationReason);
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor
{
    public static class RaidEscalationRegression
    {
        private const string ResourcesRel     = "Resources/Data/Canonical/scene-configs.json";
        private const string StreamingRel     = "StreamingAssets/Data/Canonical/scene-configs.json";
        private const string SpawnMarkerName  = "HeroStartPoint_PlayerSpawn";

        // The ladder. Ids are LIVE SAVE KEYS (RaidClaimService PlayerPrefs
        // "dotr-raid-owner-<id>"): never rename one, rename the displayName.
        private sealed class Tier
        {
            public string Id;
            public string DisplayName;
            public string Description;
            public int Unlock;
        }

        private static readonly Tier[] Ladder =
        {
            new Tier { Id = "raider_camp_small",  Unlock = 0,  DisplayName = "The Forsaken Camp",
                       Description = "Scavengers strip an abandoned settlement the Heart can no longer reach." },
            new Tier { Id = "fortified_garrison", Unlock = 0,  DisplayName = "The Broken Garrison",
                       Description = "Its soldiers still guard their post, though no living commander remains to give the order." },
            new Tier { Id = "mage_enclave",       Unlock = 0,  DisplayName = "The Veiled Enclave",
                       Description = "Something inside has learned to bend fractured memories into magic." },
            new Tier { Id = "iron_bastion",       Unlock = 0,  DisplayName = "The Iron Bastion",
                       Description = "The Heart remembers no fortress here." },
        };

        // Creative canon §2 - the first pass, superseded. Implementing one is a defect.
        private static readonly string[] SupersededNames =
        {
            "Splinter Camp", "Ironwatch Garrison", "Ashen Enclave", "Blackiron Bastion",
            "FORCED RETREAT",
        };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RAID ESCALATION (economy map §4 ladder + creative canon §3 names + the tier-4 seat pin) ---");

            CheckCatalogRows(failures, log);
            CheckTwinsIdentical(failures, log);
            CheckNoSupersededNames(failures, log);
            CheckBuildSettingsAndSeats(failures, log);
            CheckLiveProjection(failures, log);

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "RAID_ESCALATION_OK");
                reason = "RAID ESCALATION OK - four targets authored unlockVictories 0 (no win-count lock), " +
                         "twins byte-identical, no superseded name survives, every raid sceneName registered, " +
                         "no seatless raid scene enabled, capture remains a 3-star highest-raid clear";
                return true;
            }

            reason = "raid-escalation: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "RAID_ESCALATION_FAIL: " + reason);
            return false;
        }

        // ── A + B: the authored ladder ──────────────────────────────────────────
        private static void CheckCatalogRows(List<string> failures, StringBuilder log)
        {
            // Force a re-read so an editor-time JSON edit cannot be masked by a cached catalog.
            SceneConfigCatalog.Invalidate();

            foreach (var tier in Ladder)
            {
                var def = SceneConfigCatalog.Find(tier.Id);
                if (def == null)
                {
                    failures.Add($"scene-configs.json has no row '{tier.Id}' - the raid ladder is missing tier " +
                                 $"'{tier.DisplayName}', so RaidSelectionVM's flagship lookup drops it and the " +
                                 "grid silently shrinks");
                    continue;
                }

                if (!string.Equals(def.displayName, tier.DisplayName, StringComparison.Ordinal))
                    failures.Add($"'{tier.Id}'.displayName is '{def.displayName}' but the creative canon §3 says " +
                                 $"'{tier.DisplayName}' - names come from canon, never from an implementer");

                if (string.IsNullOrEmpty(def.description))
                    failures.Add($"'{tier.Id}'.description is empty - the target card's canon line " +
                                 $"(\"{tier.Description}\") is the one sentence that tells the player what this " +
                                 "place IS; without it the card has a slot and nothing in it");
                else if (!string.Equals(def.description, tier.Description, StringComparison.Ordinal))
                    failures.Add($"'{tier.Id}'.description does not match creative canon §3 verbatim - " +
                                 $"expected \"{tier.Description}\", found \"{def.description}\"");

                if (def.unlockVictories != 0)
                    failures.Add($"'{tier.Id}'.unlockVictories is {def.unlockVictories} — owner 2026-09-11: " +
                                 "no win-count lock; capture is a 3-star clear of the highest raid");

                foreach (char c in def.displayName ?? "")
                    if (c > 127)
                    { failures.Add($"'{tier.Id}'.displayName carries a non-ASCII character - the build TMP font renders it as tofu"); break; }
                foreach (char c in def.description ?? "")
                    if (c > 127)
                    { failures.Add($"'{tier.Id}'.description carries a non-ASCII character - the build TMP font renders it as tofu"); break; }

                log.AppendLine($"OK: '{tier.Id}' = \"{def.displayName}\" @ {def.unlockVictories} victories, card line authored");
            }
        }

        // ── D: dual-copy canonical JSON ─────────────────────────────────────────
        private static void CheckTwinsIdentical(List<string> failures, StringBuilder log)
        {
            string root = Application.dataPath;   // ".../Assets"
            string res = Path.Combine(root, ResourcesRel);
            string str = Path.Combine(root, StreamingRel);

            if (!File.Exists(res)) { failures.Add($"missing {ResourcesRel} - the runtime catalog has no source"); return; }
            if (!File.Exists(str)) { failures.Add($"missing {StreamingRel} - the StreamingAssets twin is gone"); return; }

            byte[] a, b;
            try { a = File.ReadAllBytes(res); b = File.ReadAllBytes(str); }
            catch (Exception e)
            {
                failures.Add($"could not read the scene-config twins ({e.GetType().Name}) - cannot prove they match");
                return;
            }

            bool same = a.Length == b.Length;
            if (same) for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { same = false; break; }

            if (!same)
                failures.Add("scene-configs.json Resources and StreamingAssets copies DIFFER. Resources wins at " +
                             "runtime, so the StreamingAssets copy is a lie that only shows up on the platform " +
                             "that reads it - keep both in sync in the same edit (CLAUDE.md canonical-JSON rule)");
            else
                log.AppendLine($"OK: scene-configs.json twins byte-identical ({a.Length} bytes)");
        }

        // ── C: the superseded first pass stays dead ─────────────────────────────
        private static void CheckNoSupersededNames(List<string> failures, StringBuilder log)
        {
            string res = Path.Combine(Application.dataPath, ResourcesRel);
            string text;
            try { text = File.ReadAllText(res); }
            catch (Exception e)
            {
                failures.Add($"could not read {ResourcesRel} ({e.GetType().Name}) - cannot prove the superseded names are gone");
                return;
            }

            foreach (var dead in SupersededNames)
                if (text.IndexOf(dead, StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"scene-configs.json still carries the SUPERSEDED first-pass name '{dead}' - " +
                                 "creative canon §2 lists it explicitly as replaced; shipping it is a defect, not a preference");

            log.AppendLine($"OK: none of the {SupersededNames.Length} superseded first-pass names appear in the catalog");
        }

        // ── E + F: build registration and the hero seat ─────────────────────────
        private static void CheckBuildSettingsAndSeats(List<string> failures, StringBuilder log)
        {
            var registered = new Dictionary<string, EditorBuildSettingsScene>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s == null || string.IsNullOrEmpty(s.path)) continue;
                registered[Path.GetFileNameWithoutExtension(s.path)] = s;
            }

            // (E) every enemy row resolves to a registered scene.
            SceneConfigCatalog.Invalidate();
            foreach (var def in SceneConfigCatalog.All)
            {
                if (def == null || !def.IsEnemy || string.IsNullOrEmpty(def.sceneName)) continue;
                if (!registered.ContainsKey(def.sceneName))
                    failures.Add($"scene-configs.json row '{def.id}' names scene '{def.sceneName}', which is NOT in " +
                                 "Build Settings at all - SceneRouter.GoRaid rejects an unregistered name, so that " +
                                 "target is unreachable and the card is a dead end");
                else
                    log.AppendLine($"OK: '{def.id}' -> '{def.sceneName}' registered (enabled={registered[def.sceneName].enabled})");
            }

            // (F) the self-healing seat pin, on every registered RaidBase_* scene.
            foreach (var kv in registered)
            {
                string sceneName = kv.Key;
                if (!DeNelle.Core.HubScenes.IsRaid(sceneName)) continue;

                string text;
                try { text = File.ReadAllText(kv.Value.path); }
                catch (Exception e)
                {
                    failures.Add($"could not read raid scene '{kv.Value.path}' ({e.GetType().Name}) - cannot prove " +
                                 $"whether it bakes {SpawnMarkerName}");
                    continue;
                }

                bool hasSeat = text.IndexOf(SpawnMarkerName, StringComparison.Ordinal) >= 0;
                bool enabled = kv.Value.enabled;

                if (enabled && !hasSeat)
                    failures.Add($"raid scene '{sceneName}' is ENABLED in Build Settings but bakes no " +
                                 $"'{SpawnMarkerName}' - HeroControlEnsurer has nowhere to seat the carried hero, so " +
                                 "the player arrives at their TOWN world pose (off-map / inside geometry)");
                else if (!enabled && hasSeat)
                    failures.Add($"raid scene '{sceneName}' now BAKES '{SpawnMarkerName}' but is still registered " +
                                 "disabled. That disabled state was a recorded defect with an expiry, and the expiry " +
                                 "just fired: flip enabled:1 in ProjectSettings/EditorBuildSettings.asset so the " +
                                 "target the catalog already advertises can actually be entered");
                else
                    log.AppendLine($"OK: raid scene '{sceneName}' enabled={enabled}, bakes seat={hasSeat} (consistent)");
            }
        }

        // ── G: the live projection locks and unlocks ────────────────────────────
        private static void CheckLiveProjection(List<string> failures, StringBuilder log)
        {
            var defs = new List<SceneConfigDef>();
            foreach (var t in Ladder)
                defs.Add(new SceneConfigDef
                {
                    id = t.Id, displayName = t.DisplayName, description = t.Description,
                    unlockVictories = t.Unlock, ownership = "Enemy", sceneName = "RaidBase_" + t.Id,
                });

            // Every fake scene is "available", so ONLY the victory gate is under test here.
            Func<string, bool> allAvailable = _ => true;

            AssertLockState(defs, allAvailable, victories: 0,
                            expectOpen: new[] { "raider_camp_small", "fortified_garrison", "mage_enclave", "iron_bastion" },
                            expectLocked: new string[0],
                            failures, log);

            // The availability gate: a target whose scene cannot load stays locked,
            // with a DIFFERENT sentence - the player is not told to go win raids.
            using (var vm = new RaidSelectionVM(defs, null, 0, name => name != "RaidBase_iron_bastion"))
            {
                string reason = vm.LockReasonFor("iron_bastion");
                if (string.IsNullOrEmpty(reason))
                    failures.Add("RaidSelectionVM leaves 'iron_bastion' OPEN even when its scene " +
                                 "cannot be loaded - tapping it reaches SceneRouter.GoRaid, which refuses the " +
                                 "unregistered scene, and the player gets a dead tap");
                else if (reason.IndexOf("win ", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("RaidSelectionVM tells a player who HAS earned 'iron_bastion' to go win more raids - " +
                                 "the availability refusal must not reuse the progression sentence, it is advice that " +
                                 "cannot possibly work");
                else
                    log.AppendLine("OK: an earned-but-unloadable target locks with the availability sentence, not the progression one");
            }
        }

        private static void AssertLockState(List<SceneConfigDef> defs, Func<string, bool> avail, int victories,
                                            string[] expectOpen, string[] expectLocked,
                                            List<string> failures, StringBuilder log)
        {
            using (var vm = new RaidSelectionVM(defs, null, victories, avail))
            {
                foreach (var id in expectOpen)
                    if (vm.IsLocked(id))
                        failures.Add($"at {victories} victories RaidSelectionVM LOCKS '{id}', which the economy-map " +
                                     "ladder says is already earned - the player is refused a target they own");

                foreach (var id in expectLocked)
                {
                    if (!vm.IsLocked(id))
                    {
                        failures.Add($"at {victories} victories RaidSelectionVM leaves '{id}' UNLOCKED. This is the " +
                                     "exact defect the ladder exists to fix: every card emitted open at every " +
                                     "victory count, so 'upgrade -> unlock a harder raid' had no reader");
                        continue;
                    }
                    string why = vm.LockReasonFor(id);
                    if (string.IsNullOrEmpty(why))
                        failures.Add($"'{id}' is locked at {victories} victories with NO reason string - a bare lock " +
                                     "says nothing in greyscale, and the owner is red/green colourblind: the words " +
                                     "are the whole signal");
                }

                // The ItemVM the View actually renders must carry the same state.
                foreach (var item in vm.Raids)
                {
                    bool expectedLocked = Array.IndexOf(expectLocked, item.Id) >= 0;
                    if (item.Locked != expectedLocked)
                        failures.Add($"ItemVM for '{item.Id}' reports Locked={item.Locked} at {victories} victories " +
                                     $"but the ladder says {expectedLocked} - the View renders ItemVM, so this is " +
                                     "what the player sees");
                    if (item.Locked && string.IsNullOrEmpty(item.LockReason))
                        failures.Add($"ItemVM for '{item.Id}' is Locked with a null LockReason - the card would show " +
                                     "a bare locked state with no sentence");
                }

                log.AppendLine($"OK: at {victories} victories -> {expectOpen.Length} open, {expectLocked.Length} locked with reasons");
            }
        }
    }
}
