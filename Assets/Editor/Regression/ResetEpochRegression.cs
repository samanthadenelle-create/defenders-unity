// =============================================================================
// ResetEpochRegression — WO-1598: a NEW GAME must be able to DECLARE itself to
// the cloud, and a cloud row from before that New Game must never come back.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core). Data + logic
// only — no scene loads, no network; runs in the existing batchmode harness.
//
// THE DEFECT THIS PINS (analytics_events, read 2026-09-07 by the minting lane):
//   api/game/save.js runs a sanity guard that rejects a save whose balances drop
//   implausibly or whose bestWave goes backwards. A NEW GAME does exactly that,
//   honestly: the owner's 2026-09-07 reset posted 36 crystals against a stored
//   901 and was refused ELEVEN times between 00:39Z and 10:26Z
//   (`implausible_drop crystals 901 -> 36`), with `rollback:bestWave` alongside
//   it — 177 such rows across 7 ids in 14 days. Every reject leaves the cloud row
//   holding the OLD town, and the next cloud load hands it straight back over the
//   new one: a silent duplication of everything she had just reset.
//
//   The fix is a monotonic `resetEpoch` the client DECLARES. This suite owns the
//   CLIENT half of it; the server half (bypass-once on a newer epoch,
//   SAVE_RESET_STALE on an older one) is tested under test/ in the api lane.
//
// THE SIX CASES, and why each is the one that would have caught the bug:
//   A [wire]   BuildSaveBody stamps an integer `resetEpoch` TOP-LEVEL, always,
//              beside schemaVersion. The guard reads the BODY, not the nested
//              state document — a field that only rode the state would never
//              reach it. "Always" matters as much as "present": an
//              intermittently-declared epoch cannot be compared on every write.
//   B [reset]  ResetToNewGame RAISES the epoch strictly above its prior value and
//              never zeroes it. This is the only field a New Game must not clear —
//              a reset that reset its own counter could not be told from the reset
//              before it, which is what makes a bare "this is a reset" flag
//              forgeable and replayable by an old device.
//   C [stale]  ApplyBackendState REFUSES a row whose epoch is older than the local
//              one — *even though its timestamp is newer*. That combination is not
//              hypothetical, it is the exact shape of the bug: a guard-rejected
//              save still advances updated_at while keeping the old balances, so
//              the row is timestamp-NEWER and content-OLDER, and the WO-1448
//              recency gate alone reads it as a winner.
//   D [equal]  An EQUAL epoch still applies through the normal recency gate, so
//              this guard changes nothing for the ordinary sync of a player who has
//              never reset (both sides sit at 0). A guard that also broke the
//              common path would be a worse defect than the one it fixes.
//   E [adopt]  An APPLIED row's newer epoch is adopted locally. The server keeps
//              resetEpoch in its own column and strips it from the state blob, so
//              ApplyPersisted can never install it — without the adoption a
//              REINSTALL restores the cloud town at local epoch 0 and is then
//              refused SAVE_RESET_STALE on every save it makes, forever.
//   F [drain]  409 SAVE_RESET_STALE is its OWN category and is NON-RETRYABLE.
//              The refusal is deterministic — identical on every retry until a
//              cloud load moves the device forward — so in the generic http-4xx
//              bucket the drain would retain its markers and re-post on every
//              scene enter for the life of the install. Classification needs BOTH
//              the 409 and the code (409 is this backend's general conflict
//              answer), the line names why=reset-stale, and the drain arm deletes
//              the queue instead of re-serializing it.
//
// RED PROOF (reasoned, NOT observed — this lane may not run Unity):
//   Against the pre-WO-1598 tree, A fails on its first assertion (BuildSaveBody
//   wrote only playerId + schemaVersion; there is no resetEpoch key to find),
//   B fails to compile-as-written (GameState.ResetEpoch did not exist), and C
//   fails on its outcome assertion: ApplyBackendState had no epoch parameter and
//   no RejectedResetEpoch outcome, so a newer-timestamped stale row returned
//   Applied and overwrote the new town's resources — the observed defect.
//
// Global state discipline (CloudLoadRestoreRegression's contract, deliberately
// mirrored rather than reinvented): skips entirely when a live
// GameStateService.Instance exists (never commandeer a real session); swaps
// GameStateService.Provider to an in-memory provider and restores it;
// DestroyImmediate on every created object in finally.
//
// ⚠ ResetToNewGame writes real PlayerPrefs (ClearEquipPrefs / ClearProgressionPrefs
// / ClearHarvestPrefs only DELETE keys) and raises NewGameStarted. Case B calls it
// on the same throwaway service the other cases use — the deletions are of this
// machine's own stale equip/talent/harvest keys, which is what a New Game does
// anyway, and no key is written.
//
// Markers: RESET_EPOCH_OK / RESET_EPOCH_FAIL (FAIL via Debug.LogError so it lands
// in break-log.jsonl). Entry: ResetEpochRegression.Run(out reason).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Editor.Regression
{
    public static class ResetEpochRegression
    {
        /// <summary>A real base58 pubkey — a hyphenated fixture is retired on save by the
        /// 2026-08-02 identity work, so it could never stand in for a bound identity here.</summary>
        private const string LocalWallet = "BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("RESET_EPOCH_OK - " + reason);
            else Debug.LogError("RESET_EPOCH_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RESET EPOCH (WO-1598 a New Game declares itself; a pre-reset cloud row is refused) ---");

            if (GameStateService.Instance != null)
            {
                // Same rule as CloudLoadRestoreRegression: commandeering a live singleton would
                // clobber real session state. A skip is honest; a pass here would not be.
                reason = "SKIPPED - a live GameStateService.Instance already exists in this editor session";
                Debug.Log(log.ToString() + "  " + reason);
                return true;
            }

            var awake = typeof(GameStateService).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake == null)
            {
                reason = "reset-epoch: could not reflect GameStateService.Awake - the lifecycle seam moved; re-point this oracle";
                Debug.LogError(log.ToString() + "RESET_EPOCH_FAIL - " + reason);
                return false;
            }

            var priorProvider = GameStateService.Provider;
            var created = new List<GameObject>();
            try
            {
                GameStateService.Provider = new InMemorySaveProvider();

                var go = new GameObject("ResetEpochOracle_Svc");
                created.Add(go);
                var svc = go.AddComponent<GameStateService>();
                awake.Invoke(svc, null);   // Load() over an empty provider = blank founding
                if (svc.State == null)
                {
                    failures.Add("service has no State after Awake - the oracle cannot run");
                }
                else
                {
                    CheckWireCarriesEpoch(svc, failures, log);      // A
                    CheckResetBumpsEpoch(svc, failures, log);       // B
                    CheckStaleEpochRefused(svc, failures, log);     // C
                    CheckEqualEpochApplies(svc, failures, log);     // D
                    CheckAppliedRowAdoptsEpoch(svc, failures, log); // E
                    CheckStaleRefusalIsItsOwnCause(failures, log);  // F
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
                Debug.Log(log.ToString() + "RESET_EPOCH_OK");
                reason = "RESET EPOCH OK - the save body declares an integer resetEpoch, ResetToNewGame raises it, " +
                         "a backend row from BEFORE that reset is refused even when its timestamp is newer, an " +
                         "equal or absent epoch still applies through the normal recency gate, and an applied " +
                         "row's newer epoch is adopted so a reinstall can still save; and a 409 SAVE_RESET_STALE " +
                         "is its own non-retryable cause whose markers the drain DROPS";
                return true;
            }
            reason = "reset-epoch: " + string.Join("; ", failures.ToArray());
            Debug.LogError(log.ToString() + "RESET_EPOCH_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  A — the WIRE: the POST body declares an integer resetEpoch, always
        // =====================================================================
        private static void CheckWireCarriesEpoch(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[A] wire - BuildSaveBody stamps a top-level integer resetEpoch beside schemaVersion");

            var st = svc.State;
            st.BoundWallet = LocalWallet;
            st.ResetEpoch = 4242;

            byte[] body = GameStateService.BuildSaveBody(SnapshotOf(svc), LocalWallet);
            if (body == null || body.Length == 0)
            {
                failures.Add("[A] BuildSaveBody produced no bytes");
                return;
            }

            JObject jo;
            try { jo = JObject.Parse(Encoding.UTF8.GetString(body)); }
            catch (Exception ex)
            {
                failures.Add($"[A] the save body is not parseable JSON: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            JToken epoch = jo["resetEpoch"];
            if (epoch == null)
            {
                failures.Add("[A] the save body carries NO top-level 'resetEpoch' key - api/game/save.js reads the " +
                             "BODY, so a new game can never declare itself and its honest balance DROP is rejected " +
                             "as implausible (the owner's 901 -> 36, eleven times on 2026-09-07)");
                return;
            }
            if (epoch.Type != JTokenType.Integer)
            {
                failures.Add($"[A] 'resetEpoch' is a {epoch.Type} token, not an Integer - the server contract is a " +
                             "monotonic INTEGER and a float/string would not compare");
                return;
            }
            long declared = epoch.Value<long>();
            if (declared != 4242L)
                failures.Add($"[A] the body declares resetEpoch {declared} but the state holds {st.ResetEpoch} - " +
                             "the wire must carry the LIVE epoch, never a default");

            log.AppendLine($"  body declares resetEpoch={declared} (Integer token)");

            // ...and it is declared UNCONDITIONALLY. A save that only names the epoch after a
            // reset leaves the server unable to tell an old client from a silent one.
            st.ResetEpoch = 0;
            var zeroJo = JObject.Parse(Encoding.UTF8.GetString(GameStateService.BuildSaveBody(SnapshotOf(svc), LocalWallet)));
            if (zeroJo["resetEpoch"] == null)
                failures.Add("[A] a never-reset save (epoch 0) OMITS resetEpoch - the field must be present on every " +
                             "write so the server can compare it every time, not only after a New Game");
            else if (zeroJo["resetEpoch"].Value<long>() != 0L)
                failures.Add($"[A] a never-reset save declared resetEpoch={zeroJo["resetEpoch"].Value<long>()}, expected 0");

            // The WO-1587 companion field must survive alongside it - the two ride the same
            // stamp block and a careless edit to one has already broken the other once.
            if (jo["schemaVersion"] == null)
                failures.Add("[A] the body lost its schemaVersion key (WO-1587) - the resetEpoch stamp must sit BESIDE it, not replace it");
        }

        // =====================================================================
        //  B — the RESET: ResetToNewGame raises the epoch, and never clears it
        // =====================================================================
        private static void CheckResetBumpsEpoch(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[B] reset - ResetToNewGame raises the epoch strictly above its prior value");

            var st = svc.State;
            st.ResetEpoch = 7;
            int prior = st.ResetEpoch;

            svc.ResetToNewGame();

            int after = svc.State.ResetEpoch;
            if (after <= prior)
                failures.Add($"[B] ResetToNewGame left the epoch at {after} (prior {prior}) - a New Game that does not " +
                             "RAISE the epoch cannot declare itself, so its balance drop is rejected exactly as before " +
                             "the fix; and an epoch that RESET could never be told from the reset before it");
            if (after == 0)
                failures.Add("[B] ResetToNewGame ZEROED the epoch - this is the one field a reset must carry forward; " +
                             "wiping it makes every reset indistinguishable and lets an old device replay its row");

            // Twice in a row still climbs — the monotonic property is what makes the server's
            // "newer than stored" test meaningful for a player who resets repeatedly.
            int once = after;
            svc.ResetToNewGame();
            int twice = svc.State.ResetEpoch;
            if (twice <= once)
                failures.Add($"[B] a SECOND ResetToNewGame did not raise the epoch again ({once} -> {twice}) - two " +
                             "resets in the same second must still produce two distinct, increasing epochs");

            log.AppendLine($"  epoch climbed {prior} -> {once} -> {twice} across two resets");
        }

        // =====================================================================
        //  C — the LOAD: a row from BEFORE the reset is refused, newer or not
        // =====================================================================
        private static void CheckStaleEpochRefused(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[C] load - a backend row with an OLDER epoch is refused even when its timestamp is newer");

            var st = svc.State;
            st.BoundWallet = LocalWallet;
            st.ResetEpoch = 9000;            // this device started a new game
            var fresh = st.Resources;
            fresh.Crystals = 36;             // ...and the new town has the owner's 36 crystals
            fresh.Stone = 12;
            st.Resources = fresh;
            st.BestWave = 0;
            svc.Save();

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var oldTown = BuildOldTownRow(epoch: 8999d);

            // ⭐ THE EXACT SHAPE OF THE BUG: the server's sanity guard rejected the new save's
            // fields but STILL WROTE the row, so updated_at is a minute NEWER while the balances
            // are the old town's 901. Recency alone says "apply". The epoch says "refuse".
            var outcome = svc.ApplyBackendState(oldTown, SaveSchema.CurrentVersion, now + 60000d, 8999d);
            if (outcome != GameStateService.BackendApplyOutcome.RejectedResetEpoch)
                failures.Add($"[C] a row from BEFORE this device's New Game was not refused (outcome={outcome}) - " +
                             "the old town's 901 crystals come back over the reset on the next scene enter, which is " +
                             "the reported defect");
            if (st.Resources.Crystals != 36 || st.Resources.Stone != 12)
                failures.Add($"[C] the refused row still wrote resources (crystals={st.Resources.Crystals}, " +
                             $"food={st.Resources.Stone}; expected the new town's 36/12)");
            if (st.BestWave != 0)
                failures.Add($"[C] the refused row still wrote bestWave (got {st.BestWave}, expected the reset's 0)");
            if (st.ResetEpoch != 9000)
                failures.Add($"[C] the refused row moved the local epoch to {st.ResetEpoch} (expected 9000) - a refusal " +
                             "must change nothing at all, least of all the field it was judged on");

            // The epoch also travels INSIDE the state payload, for a backend that does not yet
            // return the column top-level. Same verdict, sourced from the payload.
            var payloadOnly = BuildOldTownRow(epoch: 8999d);
            var fromPayload = svc.ApplyBackendState(payloadOnly, SaveSchema.CurrentVersion, now + 60000d, null);
            if (fromPayload != GameStateService.BackendApplyOutcome.RejectedResetEpoch)
                failures.Add($"[C] with no top-level epoch, the row's own payload epoch was ignored (outcome={fromPayload}) - " +
                             "an older backend that does not return the column must not become a bypass");

            log.AppendLine("  older-epoch row refused twice (top-level and payload-sourced); the new town untouched");
        }

        // =====================================================================
        //  D — the COMMON PATH: an EQUAL epoch still applies. No regression.
        // =====================================================================
        private static void CheckEqualEpochApplies(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[D] load - an EQUAL epoch changes nothing about the ordinary newer-row apply");

            var st = svc.State;
            st.BoundWallet = LocalWallet;
            st.ResetEpoch = 0;               // a player who has never reset - the common case
            var spent = st.Resources;
            spent.Crystals = 1;
            st.Resources = spent;
            svc.Save();

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var row = BuildOldTownRow(epoch: 0d);

            var outcome = svc.ApplyBackendState(row, SaveSchema.CurrentVersion, now + 60000d, 0d);
            if (outcome != GameStateService.BackendApplyOutcome.Applied)
                failures.Add($"[D] a NEWER row at an EQUAL epoch was not applied (outcome={outcome}) - the guard must be " +
                             "invisible to every player who has never started a new game, or it is a worse defect than " +
                             "the one it fixes (a reinstall would land on a blank town again, WO-1447)");
            else if (st.Resources.Crystals != 901)
                failures.Add($"[D] the applied row did not restore its resources (crystals={st.Resources.Crystals}, expected 901)");

            // And an ABSENT epoch on both sides (old backend + never-reset device) behaves the
            // same way: absent reads as 0, 0 is not less than 0, the row applies.
            var absent = BuildOldTownRow(epoch: null);
            var spent2 = st.Resources;
            spent2.Crystals = 2;
            st.Resources = spent2;
            svc.Save();
            var absentOutcome = svc.ApplyBackendState(absent, SaveSchema.CurrentVersion,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000d, null);
            if (absentOutcome != GameStateService.BackendApplyOutcome.Applied)
                failures.Add($"[D] a row with NO epoch at all was not applied (outcome={absentOutcome}) - every row " +
                             "written before WO-1598 shipped carries none, and refusing them would strand every " +
                             "existing player's cloud save");

            log.AppendLine("  equal-epoch and absent-epoch rows both applied normally");
        }

        // =====================================================================
        //  E — the REINSTALL: an applied row's NEWER epoch is ADOPTED locally
        // =====================================================================
        // ⛔ THE ONE THAT IS NOT OBVIOUS, so it is spelled out. On the server the epoch is
        // TRANSPORT, not game state: api/game/save.js's RESERVED_KEYS strips `resetEpoch` out
        // of the JSONB blob and keeps it in its own player_data.reset_epoch column, and
        // api/game/load.js returns it TOP-LEVEL. So a loaded row's `data` carries NO epoch and
        // ApplyPersisted has nothing to install. If the apply did not adopt the top-level value,
        // a reinstalled device would restore the cloud town at local epoch 0 and then declare 0
        // against a stored 9000 on every save - refused 409 SAVE_RESET_STALE forever, having done
        // nothing wrong. This case is the pin on that adoption.
        private static void CheckAppliedRowAdoptsEpoch(GameStateService svc, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[E] reinstall - an applied row's newer epoch is adopted, so the device can save again");

            var st = svc.State;
            st.BoundWallet = LocalWallet;
            st.ResetEpoch = 0;              // a fresh install: this device has never reset
            svc.Save();

            // The row the player reset on ANOTHER device: newer timestamp, epoch 9000, and -
            // exactly as the live backend behaves - NO epoch inside the state payload.
            var row = BuildOldTownRow(epoch: null);
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var outcome = svc.ApplyBackendState(row, SaveSchema.CurrentVersion, now + 60000d, 9000d);
            if (outcome != GameStateService.BackendApplyOutcome.Applied)
            {
                failures.Add($"[E] a newer row with a NEWER epoch was not applied (outcome={outcome}) - a reinstall " +
                             "must still restore the cloud town; the guard refuses OLDER rows only");
                return;
            }
            if (st.ResetEpoch != 9000)
                failures.Add($"[E] the applied row's epoch was not adopted (local epoch is {st.ResetEpoch}, expected 9000) - " +
                             "the server keeps resetEpoch in its own column and strips it from the state blob, so " +
                             "ApplyPersisted cannot install it; without the adoption every save from this device " +
                             "declares 0 against a stored 9000 and is refused SAVE_RESET_STALE forever");

            // And the adoption is MONOTONIC - it may never walk the local epoch backwards.
            st.ResetEpoch = 12000;
            svc.Save();
            svc.ApplyBackendState(BuildOldTownRow(epoch: null), SaveSchema.CurrentVersion,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000d, 12000d);
            if (st.ResetEpoch < 12000)
                failures.Add($"[E] the apply LOWERED the local epoch to {st.ResetEpoch} (was 12000) - the epoch is " +
                             "monotonic on this device and an apply may only ever raise it");

            log.AppendLine($"  epoch adopted from the applied row: local now {st.ResetEpoch}");
        }

        // =====================================================================
        //  F — the DRAIN: 409 SAVE_RESET_STALE is its own, NON-RETRYABLE cause
        // =====================================================================
        // The server's answer when this device is BEHIND is deterministic: the same bytes are
        // refused identically forever, until a cloud load moves the device forward. Left in the
        // generic http-4xx bucket the drain would retain its markers and re-post on every scene
        // enter for the life of the install - a permanent silent retry loop against a server that
        // has already given its final answer. So the category exists, it names itself, and the
        // drain drops the markers. This case pins the classification and the words; the DROP
        // itself is a source lint below, because the drain path needs a network to execute.
        private static void CheckStaleRefusalIsItsOwnCause(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[F] drain - 409 SAVE_RESET_STALE classifies as its own non-retryable category");

            const string StaleBody = "{\"ok\":false,\"code\":\"SAVE_RESET_STALE\",\"ref\":\"a1b2c3d4\"}";

            var classified = GameStateService.ClassifyHttp(409L, StaleBody);
            if (classified != GameStateService.SaveAttemptCategory.ResetStale)
                failures.Add($"[F] a 409 SAVE_RESET_STALE classified as {classified}, not ResetStale - in the generic " +
                             "4xx bucket the drain re-queues its markers and retries an answer that can never change");

            // The STATUS alone must not be enough: 409 is this backend's general conflict answer
            // (api/purchases/fulfill.js uses it too), so a 409 carrying some other code is an
            // ordinary refusal and must stay retryable.
            var otherConflict = GameStateService.ClassifyHttp(409L, "{\"ok\":false,\"code\":\"PROMO_ALREADY_CLAIMED\"}");
            if (otherConflict == GameStateService.SaveAttemptCategory.ResetStale)
                failures.Add("[F] any 409 is being read as a reset refusal - the CODE in the body identifies the " +
                             "verdict, not the status, and mis-reading it would silently DROP a retryable marker");

            // ...and the code on some other status is not a refusal either.
            var wrongStatus = GameStateService.ClassifyHttp(400L, StaleBody);
            if (wrongStatus == GameStateService.SaveAttemptCategory.ResetStale)
                failures.Add("[F] the SAVE_RESET_STALE string was honoured on a 400 - both the status and the code " +
                             "are required, or a body that merely mentions the string becomes a marker-dropper");

            // The cause travels with the failure, in its own words, and never blames the wallet.
            string line = GameStateService.DescribeSaveFailure(new GameStateService.SaveAttemptResult(
                GameStateService.SaveAttemptCategory.ResetStale, 409L, StaleBody));
            if (line.IndexOf("why=reset-stale", StringComparison.Ordinal) < 0)
                failures.Add($"[F] the ResetStale failure line does not name its own why= token: {line}");
            if (line.IndexOf("Flow:Wallet", StringComparison.Ordinal) >= 0)
                failures.Add("[F] the ResetStale line sends the reader to [Flow:Wallet] - the wallet rail is not " +
                             "implicated in an epoch verdict (the WO-1587 mis-attribution, repeated)");

            // SOURCE LINT: the drain must DELETE the queue on this category, never re-serialize it.
            // Asserted on the source because DrainOfflineQueue needs a live network to reach.
            const string StatePath = "Assets/_Modules/Core/State/GameStateService.cs";
            if (!System.IO.File.Exists(StatePath))
            {
                failures.Add($"[F] {StatePath} not found - re-point this oracle");
                return;
            }
            string src = System.IO.File.ReadAllText(StatePath);

            // ⚠ ANCHOR ON THE ARM'S OWN WARN LITERAL, NOT ON THE CATEGORY NAME. There are TWO
            // ResetStale arms and they do different jobs: the ORDINARY sync path (inside
            // SyncToBackend's try, so 16-space indent) must skip EnqueueOffline, and the DRAIN
            // (FlushOfflineQueue, 12-space indent) must DeleteKey an existing queue. A bare
            // IndexOf on "attempt.Category == SaveAttemptCategory.ResetStale" finds the SYNC arm
            // first - it is ~860 lines earlier in the file - and then asserts the drain's contract
            // against it, which reds a correct tree. That is exactly what this oracle did on
            // Builds/reg-wave10.log: the code had the DeleteKey the whole time, at :3626, and the
            // lint was reading the wrong arm. Both arms are pinned separately below, by the
            // unique log literal each one owns.
            const string DrainArmAnchor = "offline queue drain REFUSED as STALE - {mine.Count}";
            int drainAt = src.IndexOf(DrainArmAnchor, StringComparison.Ordinal);
            if (drainAt < 0)
            {
                failures.Add("[F] the drain has NO ResetStale arm ('" + DrainArmAnchor + "' is gone or re-worded) - " +
                             "a stale refusal falls through to the generic re-queue and the queue can never empty again");
                return;
            }

            // The arm's body: from its `else if` header back-searched from the Warn, forward to
            // the next 12-space `else` (the generic re-queue branch that follows it).
            int drainArmStart = src.LastIndexOf("else if (attempt.Category == SaveAttemptCategory.ResetStale)",
                                                drainAt, StringComparison.Ordinal);
            if (drainArmStart < 0)
            {
                failures.Add("[F] the drain's STALE warning is no longer inside an `else if (attempt.Category == " +
                             "SaveAttemptCategory.ResetStale)` arm - re-point this oracle");
                return;
            }
            int drainArmEnd = src.IndexOf("\n            else", drainAt, StringComparison.Ordinal);
            string drainArm = drainArmEnd > drainArmStart
                ? src.Substring(drainArmStart, drainArmEnd - drainArmStart)
                : src.Substring(drainArmStart);

            if (drainArm.IndexOf("PlayerPrefs.DeleteKey(SyncQueueKey)", StringComparison.Ordinal) < 0)
                failures.Add("[F] the ResetStale DRAIN arm does not DeleteKey(SyncQueueKey) - the markers survive a " +
                             "refusal that will never succeed, which is the retry loop this category exists to stop");
            if (drainArm.IndexOf("PlayerPrefs.SetString(SyncQueueKey", StringComparison.Ordinal) >= 0)
                failures.Add("[F] the ResetStale DRAIN arm RE-QUEUES the markers - non-retryable means dropped, not retained");
            // The coordinator's contract: the Warn names BOTH epochs. Ours is named directly;
            // the server's `stored` arrives inside the body head that DescribeSaveFailure carries.
            if (drainArm.IndexOf("resetEpoch=", StringComparison.Ordinal) < 0)
                failures.Add("[F] the drain's STALE warning does not name the local resetEpoch - a refusal nobody can " +
                             "attribute to an epoch is the silent failure CLAUDE.md s12 forbids");
            if (drainArm.IndexOf("DescribeSaveFailure(attempt)", StringComparison.Ordinal) < 0)
                failures.Add("[F] the drain's STALE warning does not carry DescribeSaveFailure(attempt) - the server's " +
                             "own `stored` epoch travels in that body head, and without it only half the comparison is logged");

            // ── the OTHER arm: the ordinary sync path must not enqueue a marker at all ──
            const string SyncArmAnchor = "cloud sync SKIPPED the offline queue";
            int syncAt = src.IndexOf(SyncArmAnchor, StringComparison.Ordinal);
            if (syncAt < 0)
            {
                failures.Add("[F] the ORDINARY sync path has no ResetStale arm ('" + SyncArmAnchor + "') - a stale " +
                             "refusal there falls through to EnqueueOffline, so every sync enqueues a marker the very " +
                             "next drain drops: a bounded loop, but one that never stops producing it");
            }
            else
            {
                int syncArmStart = src.LastIndexOf("else if (attempt.Category == SaveAttemptCategory.ResetStale)",
                                                   syncAt, StringComparison.Ordinal);
                int syncArmEnd = src.IndexOf("\n                else", syncAt, StringComparison.Ordinal);
                string syncArm = syncArmStart >= 0 && syncArmEnd > syncArmStart
                    ? src.Substring(syncArmStart, syncArmEnd - syncArmStart)
                    : string.Empty;
                if (syncArm.Length > 0 && syncArm.IndexOf("EnqueueOffline", StringComparison.Ordinal) >= 0)
                    failures.Add("[F] the ordinary sync path's ResetStale arm still calls EnqueueOffline - the whole " +
                                 "point of the category is that this refusal produces no marker");
                if (syncArmStart >= 0 && syncArmStart == drainArmStart)
                    failures.Add("[F] the sync and drain ResetStale arms resolved to the SAME source position - this " +
                                 "oracle is reading one arm twice and proving nothing about the other");
            }

            log.AppendLine("  409+code classifies as ResetStale, names why=reset-stale, the DRAIN arm deletes the queue " +
                           "and names the epoch, and the SYNC arm queues nothing");
        }

        // =====================================================================
        //  Fixture — the OLD town the server kept holding: 901 crystals, wave 40
        // =====================================================================
        private static SaveSchema.PersistedState BuildOldTownRow(double? epoch)
        {
            return new SaveSchema.PersistedState
            {
                BestWave = 40,
                Resources = new ResourceBalance { Crystals = 901, Stone = 500, Coins = 300 },
                Wood = 15,
                Iron = 5,
                ResetEpoch = epoch,
            };
        }

        /// <summary>The service's own snapshot, via the same public seam the sync path uses.</summary>
        private static SaveSchema.PersistedState SnapshotOf(GameStateService svc)
        {
            return svc.Snapshot();
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
