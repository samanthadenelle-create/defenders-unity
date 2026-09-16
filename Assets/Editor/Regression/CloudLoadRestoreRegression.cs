// =============================================================================
// CloudLoadRestoreRegression — WO-1447 + WO-1448: what a cloud LOAD restores,
// and when it is allowed to overwrite the local save.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core). Data + logic
// only — no scene loads, no network; runs in the existing batchmode harness.
//
// WHAT IS BEING PINNED, and why it needed a suite of its own:
//
//   WO-1447 — the cloud apply used to copy SEVEN fields by hand (bestWave /
//   resources / voidshards / aetherCrystals / stone / iron / wood) and never
//   call MigrateForImport or ApplyPersisted at all. The SERVER was never the
//   limiter: api/game/load.js returns the whole state document, so structures,
//   army, build queue, echoes, cosmetics and quest state were all present in the
//   payload and all dropped on the floor. A reinstall, or a sign-in on a second
//   device, restored the player's CURRENCIES ONTO A BLANK TOWN. The fix routes
//   the server row through the SAME migrate → validate → ApplyPersisted path the
//   local Load() uses; case A is the oracle for that.
//
//   WO-1448 — PersistenceBridge fires the cloud load on EVERY scene enter and
//   there was no recency comparison, so a player who spent resources, entered a
//   raid scene and came back before the server row caught up had the OLDER
//   server numbers written over the newer local ones and immediately persisted.
//   Case B is the oracle for that.
//
//   Identity — the payload CARRIES a boundWallet and ApplyPersisted installs it,
//   so a row naming a different owner would both overwrite the town and repoint
//   the account. Case C pins the fail-closed refusal. Never fail open here.
//
// ⛔ WHY THE ORACLE TARGETS ApplyBackendState AND NOT LoadFromBackend.
// LoadFromBackend is a UniTask that performs a real UnityWebRequest against
// https://defenders-of-the-realm-v2.vercel.app — it cannot run headless without
// a network and a live player row, and a suite that depended on either would be
// flaky evidence, not evidence. The DEFECT, however, was never in the transport:
// it was entirely in the apply. So the apply is a public seam
// (GameStateService.ApplyBackendState) that LoadFromBackend calls with exactly
// the three values it parsed off the response, and the seam is what is asserted.
// This is the same split CoreSaveRegression [H] uses for the local path.
//
// RED PROOF (reasoned, NOT observed — this lane could not run Unity):
//   Against the pre-WO-1447 body, case A fails on its FIRST assertion: the
//   seven-field copy list contains no baseLayout / army / obsidianQueue arm, so
//   a server row carrying a town restores none of it. Case B fails on the
//   resources assertion: the old block wrote `_state.Resources = server.Resources`
//   unconditionally, with no timestamp comparison anywhere in the method
//   (serverLastSeenMs was parsed and then only stored for display). Case C fails
//   open: the old block never read boundWallet at all, so a mismatched row was
//   applied without comment.
//
// Global state discipline: skips entirely when a live GameStateService.Instance
// exists (never commandeer a real session); swaps GameStateService.Provider to
// an in-memory provider and restores it; DestroyImmediate on every created
// object in finally.
//
// Markers: CLOUDLOAD_RESTORE_OK / CLOUDLOAD_RESTORE_FAIL (FAIL via
// Debug.LogError so it lands in break-log.jsonl). Entry:
// CloudLoadRestoreRegression.Run(out reason).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;

namespace DeNelle.Editor.Regression
{
    public static class CloudLoadRestoreRegression
    {
        /// <summary>A real base58 pubkey — a hyphenated fixture is retired on save by the
        /// 2026-08-02 identity work (see CoreSaveRegression [H]), so it could never stand in
        /// for a bound identity here either.</summary>
        private const string LocalWallet = "BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV";

        /// <summary>A DIFFERENT, equally valid pubkey — the identity-mismatch fixture.</summary>
        private const string StrangerWallet = "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("CLOUDLOAD_RESTORE_OK - " + reason);
            else Debug.LogError("CLOUDLOAD_RESTORE_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- CLOUD LOAD RESTORE (WO-1447 whole-row restore + WO-1448 recency gate + identity) ---");

            if (GameStateService.Instance != null)
            {
                // Same rule as CoreSaveRegression [H]: commandeering a live singleton would
                // clobber real session state. A skip is honest; a pass here would not be.
                reason = "SKIPPED - a live GameStateService.Instance already exists in this editor session";
                Debug.Log(log.ToString() + "  " + reason);
                return true;
            }

            var awake = typeof(GameStateService).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake == null)
            {
                reason = "cloud-load-restore: could not reflect GameStateService.Awake - the lifecycle seam moved; re-point this oracle";
                Debug.LogError(log.ToString() + "CLOUDLOAD_RESTORE_FAIL - " + reason);
                return false;
            }

            var priorProvider = GameStateService.Provider;
            var created = new List<GameObject>();
            try
            {
                GameStateService.Provider = new InMemorySaveProvider();

                var go = new GameObject("CloudLoadRestoreOracle_Svc");
                created.Add(go);
                var svc = go.AddComponent<GameStateService>();
                awake.Invoke(svc, null);   // Load() over an empty provider = blank founding
                if (svc.State == null)
                {
                    failures.Add("service has no State after Awake - the oracle cannot run");
                }
                else
                {
                    CheckWholeRowRestore(svc, failures, log);   // A — WO-1447
                    CheckRecencyGate(svc, failures, log);       // B — WO-1448
                    CheckIdentityFailClosed(svc, failures, log);// C — never fail open on identity
                }
            }
            catch (Exception ex)
            {
                failures.Add($"suite threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                for (int i = 0; i < created.Count; i++)
                    if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
                GameStateService.Provider = priorProvider;
            }

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "CLOUDLOAD_RESTORE_OK");
                reason = "CLOUD LOAD RESTORE OK - a newer server row restores the WHOLE town (structures + army + queued job), " +
                         "an older row changes nothing, and a mismatched identity is refused";
                return true;
            }
            reason = "cloud-load-restore: " + string.Join("; ", failures.ToArray());
            Debug.LogError(log.ToString() + "CLOUDLOAD_RESTORE_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  A — WO-1447: a NEWER server row restores the whole state, not 7 fields
        // =====================================================================
        private static void CheckWholeRowRestore(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[A] WO-1447 - a newer server row restores structures + army + the build queue");

            var st = svc.State;
            st.BoundWallet = LocalWallet;
            st.BaseLayout = new List<PlacedStructureData>();
            st.Army = new ArmyStorage();
            st.ObsidianQueue = ObsidianQueueState.Empty();
            svc.Save();   // stamps LastLocalSaveUnixMs = now

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var server = BuildTownRow();

            // The row is one minute NEWER than the local save - the reinstall / second-device
            // shape, where the server is the only place the town still exists.
            var outcome = svc.ApplyBackendState(server, SaveSchema.CurrentVersion, now + 60000d);
            if (outcome != GameStateService.BackendApplyOutcome.Applied)
            {
                failures.Add($"[A] a server row NEWER than the local save was not applied (outcome={outcome}) - " +
                             "a reinstall would still land on a blank town");
                return;
            }

            // ⭐ THE THREE ARMS THE OLD SEVEN-FIELD COPY LIST COULD NOT REACH.
            if (st.BaseLayout == null || st.BaseLayout.Count != 1 || st.BaseLayout[0].itemId != "market")
                failures.Add($"[A] cloud load did not restore baseLayout (got {(st.BaseLayout == null ? "null" : st.BaseLayout.Count.ToString())} record(s)) - " +
                             "WO-1447: the whole town is in the payload and must survive the apply");
            if (st.Army == null || st.Army.Owned == null || st.Army.Owned.Count != 1
                || st.Army.Owned[0].TroopDefId != "troop-footman")
                failures.Add($"[A] cloud load did not restore the army roster (got {(st.Army == null || st.Army.Owned == null ? "null" : st.Army.Owned.Count.ToString())} troop(s))");
            if (st.ObsidianQueue == null
                || st.ObsidianQueue.Channel(ChannelId.Builder).Count != 1)
                failures.Add($"[A] cloud load did not restore the queued Builder job (got {(st.ObsidianQueue == null ? "null queue" : st.ObsidianQueue.Channel(ChannelId.Builder).Count + " job(s)")}) - " +
                             "in-flight timed work must survive a device change");

            // ...and the currencies still flow, through the SAME path rather than a copy list.
            if (st.Resources.Crystals != 777 || st.Resources.Stone != 42)
                failures.Add($"[A] cloud load lost the row's resources (crystals={st.Resources.Crystals}, food={st.Resources.Stone}; expected 777/42) - " +
                             "the currency fields must ride ApplyPersisted like every other field, not a bespoke block");
            if (st.BestWave != 19)
                failures.Add($"[A] cloud load lost bestWave (got {st.BestWave}, expected 19)");

            // Identity is DEVICE-owned: a row with no wallet must never blank the live one.
            if (st.BoundWallet != LocalWallet)
                failures.Add($"[A] the apply changed BoundWallet to '{st.BoundWallet}' - identity is device-owned, never payload-owned");

            log.AppendLine("  newer row applied: baseLayout + army + Builder job + currencies all restored, identity untouched");
        }

        // =====================================================================
        //  B — WO-1448: an OLDER server row must change nothing at all
        // =====================================================================
        private static void CheckRecencyGate(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[B] WO-1448 - an older server row must not overwrite newer local resources");

            var st = svc.State;
            st.BoundWallet = LocalWallet;
            var spent = st.Resources;
            spent.Crystals = 5;      // the player just SPENT down to 5
            spent.Stone = 3;
            st.Resources = spent;
            st.BaseLayout = new List<PlacedStructureData>
            {
                new PlacedStructureData("barracks", 1, 2, 0, 1),
            };
            svc.Save();   // local save is now the freshest thing that exists

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var stale = BuildTownRow();   // carries crystals 777 / food 42 / a "market"

            // One minute OLDER than the local save: the exact scene-enter race - spend,
            // enter a raid, come back before the server row catches up.
            var outcome = svc.ApplyBackendState(stale, SaveSchema.CurrentVersion, now - 60000d);
            if (outcome != GameStateService.BackendApplyOutcome.SkippedStaleServer)
                failures.Add($"[B] an OLDER server row was not skipped (outcome={outcome}) - every scene enter would " +
                             "hand the player back resources they already spent");

            if (st.Resources.Crystals != 5 || st.Resources.Stone != 3)
                failures.Add($"[B] a stale server row OVERWROTE local resources (crystals={st.Resources.Crystals}, food={st.Resources.Stone}; " +
                             "expected the local 5/3) - WO-1448: newer wins, and nothing is applied when local is newer");
            if (st.BaseLayout == null || st.BaseLayout.Count != 1 || st.BaseLayout[0].itemId != "barracks")
                failures.Add("[B] a stale server row overwrote the local town layout - a skip must apply NOTHING, not 'everything but resources'");

            // An UNDATED row (older backend) against a device that HAS saved is also a skip:
            // unknown is not the same as newer, and guessing here is what mints currency.
            var undated = svc.ApplyBackendState(BuildTownRow(), SaveSchema.CurrentVersion, null);
            if (undated != GameStateService.BackendApplyOutcome.SkippedStaleServer)
                failures.Add($"[B] an UNDATED server row was applied over a dated local save (outcome={undated}) - " +
                             "an unknown server timestamp must never beat a known local one");
            if (st.Resources.Crystals != 5)
                failures.Add($"[B] an undated server row overwrote local resources (crystals={st.Resources.Crystals}, expected 5)");

            log.AppendLine("  older row and undated row both skipped: local resources + local town untouched");
        }

        // =====================================================================
        //  C — identity: a row naming a different wallet is REFUSED, fail closed
        // =====================================================================
        private static void CheckIdentityFailClosed(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[C] identity - a row bound to a different wallet is refused (never fail open)");

            var st = svc.State;
            st.BoundWallet = LocalWallet;
            var mine = st.Resources;
            mine.Crystals = 11;
            st.Resources = mine;
            st.BaseLayout = new List<PlacedStructureData>
            {
                new PlacedStructureData("barracks", 1, 2, 0, 1),
            };
            svc.Save();

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var strangersRow = BuildTownRow();
            strangersRow.BoundWallet = StrangerWallet;

            // NEWER than local on purpose: recency must not be able to launder a bad identity.
            var outcome = svc.ApplyBackendState(strangersRow, SaveSchema.CurrentVersion, now + 60000d);
            if (outcome != GameStateService.BackendApplyOutcome.RejectedIdentity)
                failures.Add($"[C] a row bound to a DIFFERENT wallet was not rejected (outcome={outcome}) - " +
                             "applying it would both overwrite this player's town and repoint their account");
            if (st.BoundWallet != LocalWallet)
                failures.Add($"[C] the rejected row still changed BoundWallet (now '{st.BoundWallet}')");
            if (st.Resources.Crystals != 11)
                failures.Add($"[C] the rejected row still wrote resources (crystals={st.Resources.Crystals}, expected 11)");
            if (st.BaseLayout == null || st.BaseLayout.Count != 1 || st.BaseLayout[0].itemId != "barracks")
                failures.Add("[C] the rejected row still overwrote the local town layout");

            log.AppendLine("  mismatched identity refused with nothing applied, even though the row was newer");
        }

        // =====================================================================
        //  Fixture — a server row carrying a REAL town, not just currencies
        // =====================================================================
        private static SaveSchema.PersistedState BuildTownRow()
        {
            var army = new ArmyStorage();
            army.Owned = new List<PlayerTroop> { new PlayerTroop("troop-1", "troop-footman") };

            var queue = ObsidianQueueState.Empty();
            queue.Channel(ChannelId.Builder).ActiveJobs.Add(new BuildJobData
            {
                StructureId = "market-1",
                JobType = 0,
                Kind = 0,
                Channel = 0,
                StartMs = 1000d,
                DurationMs = 60000d,
            });

            var resources = new ResourceBalance { Crystals = 777, Stone = 42, Coins = 9 };

            return new SaveSchema.PersistedState
            {
                BestWave = 19,
                Resources = resources,
                Wood = 120,
                Iron = 60,
                BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("market", 4, 5, 1, 1),
                },
                Army = army,
                ObsidianQueue = queue,
            };
        }

        /// <summary>In-memory save IO — the suite must never touch the machine's PlayerPrefs.</summary>
        private sealed class InMemorySaveProvider : ISaveProvider
        {
            private readonly Dictionary<string, string> _store = new Dictionary<string, string>();
            public bool Exists(string slot) => _store.ContainsKey(slot);
            public string Read(string slot) => _store.TryGetValue(slot, out var v) ? v : string.Empty;
            public void Write(string slot, string json) => _store[slot] = json;
            public void Delete(string slot) => _store.Remove(slot);
        }
    }
}
