// =============================================================================
// SaveWipeBackupRegression [save-wipe-backup]              — WO-1688 RED pin #2
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
//
// WHAT IT PINS: the one-generation local backup that makes a New Game
// RECOVERABLE. WO-1688 §1.4 is the reason it exists — LocalSaveProvider is a
// SINGLE-SLOT store, every call site passes SaveSchema.PlayerPrefsKey, and on
// 2026-09-10 the reset's own Save() wrote len=3417 over the len=9457 body that
// had just been loaded. Nothing anywhere held a previous copy, so the owner's
// realm ceased to exist at 12:18:56.614.
//
// THIS SUITE IS BEHAVIOURAL WHERE IT CAN BE AND A LINT WHERE IT MUST BE, and
// the split is deliberate:
//   * BEHAVIOURAL half — the round trip runs against an IN-MEMORY ISaveProvider
//     installed into GameStateService.Provider and restored in a finally. It
//     therefore NEVER TOUCHES PlayerPrefs, which is the hazard that stops
//     ResetToNewGameFullClearRegression from driving the real reset: a gate that
//     wipes the save of whoever runs it is worse than the bug it pins.
//   * LINT half — the ORDERING ("the backup is written before the first
//     mutation") is a property of WHERE the call sits inside ResetToNewGame, and
//     the only way to assert that without executing the reset is to read the
//     method's source. Case 4 does exactly that, and says so.
//
// ⚠ RED-FIRST STATUS: at HEAD before WO-1688 every case fails at the first
// line — SaveBackupService does not exist and SaveSchema has no BackupKeySuffix,
// so the suite does not compile against the pre-fix tree. That is the strongest
// available RED for a file that introduces a type. It was NOT executed in the
// authoring lane (no Unity there); the lead's gate run is what proves it.
//
// Cases:
//   1 [slot-derivation]  BackupSlot == PlayerPrefsKey + BackupKeySuffix, differs
//                        from the live slot, and no second literal key is typed
//                        anywhere in the service's source.
//   2 [roundtrip]        A populated signed body copied into the backup slot
//                        comes back IDENTICAL, its EMBEDDED signature still
//                        validates, and it survives the NORMAL load path
//                        (TryExtractSigned -> SaveFile -> SaveMigrator ->
//                        SaveSchema.Validate) with its fields intact.
//   3 [one-generation]   A second capture overwrites the first and leaves
//                        exactly one backup body — a wipe-undo, not a history.
//   4 [ordering]         The capture call is the FIRST statement of
//                        ResetToNewGame, ahead of its own ENTER trace, the
//                        WO-1598 epoch bump and every field assignment.
//   5 [nothing-to-lose]  A capture over a blank live state SKIPS and leaves the
//                        existing backup intact. This is the incident replaying
//                        through the fix: after a wipe the live save IS blank
//                        and HasExistingSave() is false, so a second accidental
//                        START NEW would otherwise copy that blank over the only
//                        surviving copy of the realm.
//   6 [no-auto-restore]  The service never writes the LIVE slot, and nothing on
//                        the reset path reads the backup back. Restoring is a
//                        deliberate operator action (WO-1688 §2.2); an automatic
//                        one would fight the new game a player actually wanted.
//   7 [epoch-guard-intact] The WO-1598 reset-epoch guard is UNCHANGED. WO-1688
//                        §5 forbids weakening it to make restores work, so the
//                        four sites it lives at are pinned present here, in the
//                        same suite as the feature that might tempt someone.
//
// Markers: SAVE_WIPE_BACKUP_OK / SAVE_WIPE_BACKUP_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.SaveWipeBackupRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1688 RED pin #2 — the pre-reset backup exists, round-trips with its
    /// signature, keeps exactly one generation, and is written before the wipe.</summary>
    public static class SaveWipeBackupRegression
    {
        private const string ServiceSrc = "Assets/_Modules/Core/State/GameStateService.cs";
        private const string BackupSrc = "Assets/_Modules/Core/State/SaveBackupService.cs";

        /// <summary>
        /// A dictionary-backed <see cref="ISaveProvider"/>. Mirrors LocalSaveProvider's
        /// contract exactly — including that Delete drops the sibling signature key —
        /// so the round trip proves the same rules the device runs under, with the
        /// developer's own PlayerPrefs untouched.
        /// </summary>
        private sealed class MemoryProvider : ISaveProvider
        {
            public readonly Dictionary<string, string> Store = new Dictionary<string, string>();
            public bool Exists(string slot) => Store.ContainsKey(slot);
            public string Read(string slot) => Store.TryGetValue(slot, out var v) ? v : string.Empty;
            public void Write(string slot, string json) { Store[slot] = json; }
            public void Delete(string slot)
            {
                Store.Remove(slot);
                Store.Remove(slot + SaveSchema.SignatureKeySuffix);
            }
        }

        /// <summary>Standalone entry point (its own marker).</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("SAVE_WIPE_BACKUP_OK - " + reason);
            else Debug.LogError("SAVE_WIPE_BACKUP_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var previousProvider = GameStateService.Provider;
            try
            {
                Case(failures, "slot-derivation", () => Case1_SlotDerivation(failures));
                Case(failures, "roundtrip", () => Case2_RoundTrip(failures));
                Case(failures, "one-generation", () => Case3_OneGeneration(failures));
                Case(failures, "ordering", () => Case4_Ordering(failures));
                Case(failures, "nothing-to-lose", () => Case5_NothingToLose(failures));
                Case(failures, "no-auto-restore", () => Case6_NoAutoRestore(failures));
                Case(failures, "epoch-guard-intact", () => Case7_EpochGuardIntact(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // The provider is a static seam shared with everything else in this
                // batchmode session. Restoring it is not tidiness; leaving a memory
                // provider installed would silently blank the next suite's IO.
                GameStateService.Provider = previousProvider;
            }

            if (failures.Count == 0)
            {
                reason = "SAVE WIPE BACKUP OK - the backup slot is DERIVED from PlayerPrefsKey; a populated " +
                         "signed body round-trips byte-identical with a VALID embedded signature and survives " +
                         "the normal load path; a second capture leaves exactly ONE generation; the capture is " +
                         "the first statement of ResetToNewGame (ahead of its ENTER trace and the epoch bump); " +
                         "a blank live state SKIPS rather than overwriting a good backup; nothing auto-restores; " +
                         "and the WO-1598 epoch guard is untouched";
                return true;
            }
            reason = "save-wipe-backup FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ── CASE 1 — the name is derived, never typed twice ───────────────────
        private static void Case1_SlotDerivation(List<string> failures)
        {
            string expected = SaveSchema.PlayerPrefsKey + SaveSchema.BackupKeySuffix;
            if (SaveBackupService.BackupSlot != expected)
                failures.Add("[slot-derivation] BackupSlot is '" + SaveBackupService.BackupSlot +
                             "' but PlayerPrefsKey + BackupKeySuffix is '" + expected + "'. The backup " +
                             "slot must be composed from the live key so repointing the live key can never " +
                             "leave the wipe-undo writing where nothing reads.");
            if (SaveBackupService.BackupSlot == SaveSchema.PlayerPrefsKey)
                failures.Add("[slot-derivation] the backup slot IS the live slot - the 'backup' would " +
                             "overwrite the very save it is meant to preserve.");
            if (SaveBackupService.LiveSlot != SaveSchema.PlayerPrefsKey)
                failures.Add("[slot-derivation] LiveSlot has drifted from SaveSchema.PlayerPrefsKey.");

            var failures2 = new List<string>();
            string src = ReadSource(BackupSrc, failures2);
            failures.AddRange(failures2);
            if (src != null && StripComments(src).Contains("\"" + SaveSchema.PlayerPrefsKey))
                failures.Add("[slot-derivation] a literal save-key string is typed into " + BackupSrc +
                             ". WO-1688 §5: derive the name from the existing consts, never hardcode a " +
                             "second literal.");
        }

        // ── CASE 2 — the round trip, signature included ──────────────────────
        private static void Case2_RoundTrip(List<string> failures)
        {
            var mem = new MemoryProvider();
            string liveBody = MakePopulatedSaveJson(heroLevel: 7, onboarded: true);
            string stored = SaveSchema.EmbedSignature(liveBody);
            mem.Write(SaveBackupService.LiveSlot, stored);

            bool wrote = SaveBackupService.CaptureBeforeReset(true, "regression", mem);
            if (!wrote)
            {
                failures.Add("[roundtrip] CaptureBeforeReset reported no write over a populated live slot - " +
                             "there is no backup at all, which is the pre-WO-1688 world.");
                return;
            }

            string backup = mem.Read(SaveBackupService.BackupSlot);
            if (backup != stored)
            {
                failures.Add("[roundtrip] the backup body is NOT byte-identical to the live body (" +
                             (backup == null ? "null" : backup.Length.ToString()) + " vs " + stored.Length +
                             " chars). Anything but a verbatim copy risks laundering a broken body into one " +
                             "that merely LOOKS intact.");
                return;
            }

            // The NORMAL load path, step for step (GameStateService.Load).
            string json = SaveSchema.TryExtractSigned(backup, out bool sigPresent, out bool sigValid);
            if (!sigPresent)
                failures.Add("[roundtrip] the backup carries NO embedded signature - the copy dropped the " +
                             "LB-3 envelope, and a restore would be indistinguishable from a hand-edited blob.");
            else if (!sigValid)
                failures.Add("[roundtrip] the backup's embedded signature does NOT validate against its own " +
                             "payload, so the load path would reject the restore as tamper.");

            SaveSchema.SaveFile file = null;
            try { file = JsonConvert.DeserializeObject<SaveSchema.SaveFile>(json, SaveSchema.JsonSettings); }
            catch (Exception ex) { failures.Add("[roundtrip] the backup body does not parse: " + ex.Message); }
            if (file == null || file.State == null)
            {
                failures.Add("[roundtrip] the backup envelope has no state payload.");
                return;
            }

            var migration = SaveMigrator.MigrateForImport(file.State, file.StoreVersion);
            if (!migration.Ok)
            {
                failures.Add("[roundtrip] the backup was REJECTED by SaveMigrator: " + migration.Reason);
                return;
            }
            var validation = SaveSchema.Validate(migration.Data);
            if (!validation.Ok)
            {
                failures.Add("[roundtrip] the backup FAILED schema validation: " + validation.Message);
                return;
            }
            double level = validation.Data.HeroLevel.HasValue ? validation.Data.HeroLevel.Value : -1d;
            if (Math.Abs(level - 7d) > 0.001d)
                failures.Add("[roundtrip] the restored payload lost its content (heroLevel=" + level +
                             ", expected 7). A backup that survives validation but not its own fields is " +
                             "not a backup of anything.");
        }

        // ── CASE 3 — exactly one generation ─────────────────────────────────
        private static void Case3_OneGeneration(List<string> failures)
        {
            var mem = new MemoryProvider();
            string first = SaveSchema.EmbedSignature(MakePopulatedSaveJson(1, true));
            mem.Write(SaveBackupService.LiveSlot, first);
            SaveBackupService.CaptureBeforeReset(true, "regression-gen1", mem);

            string second = SaveSchema.EmbedSignature(MakePopulatedSaveJson(2, true));
            mem.Write(SaveBackupService.LiveSlot, second);
            SaveBackupService.CaptureBeforeReset(true, "regression-gen2", mem);

            if (mem.Read(SaveBackupService.BackupSlot) != second)
                failures.Add("[one-generation] the second capture did not overwrite the first - the backup " +
                             "slot no longer holds the most recent pre-reset body.");

            int bodies = 0;
            foreach (var key in mem.Store.Keys)
                if (key.StartsWith(SaveBackupService.BackupSlot, StringComparison.Ordinal) &&
                    !key.EndsWith(SaveSchema.SignatureKeySuffix, StringComparison.Ordinal))
                    bodies++;
            if (bodies != 1)
                failures.Add("[one-generation] found " + bodies + " backup bodies, expected exactly 1. " +
                             "WO-1688 §2.2 is explicit: this is a wipe-undo, not a save-history feature - " +
                             "a ladder of generations is a storage-growth and privacy surface nobody asked for.");
        }

        // ── CASE 4 — written BEFORE the first mutation (source lint) ─────────
        private static void Case4_Ordering(List<string> failures)
        {
            string raw = ReadSource(ServiceSrc, failures);
            if (raw == null) return;
            string src = StripComments(raw);

            int method = src.IndexOf("public void ResetToNewGame()", StringComparison.Ordinal);
            if (method < 0)
            {
                failures.Add("[ordering] ResetToNewGame() was not found in " + ServiceSrc +
                             " - the method shape changed and this oracle is proving nothing.");
                return;
            }
            string body = ExtractBraceBody(src, method);
            if (body == null)
            {
                failures.Add("[ordering] could not extract ResetToNewGame's body (unbalanced braces).");
                return;
            }

            int capture = body.IndexOf("SaveBackupService.CaptureBeforeReset", StringComparison.Ordinal);
            if (capture < 0)
            {
                failures.Add("[ordering] ResetToNewGame never takes a backup. THIS IS THE 2026-09-10 " +
                             "CONDITION VERBATIM: the reset's own Save() overwrites the single 'dotr-save' " +
                             "slot and the prior signed body ceases to exist on the device.");
                return;
            }

            // Everything the copy must precede, in the order the incident log shows them.
            var mustFollow = new (string token, string why)[]
            {
                ("ResetToNewGame: ENTER", "the reset's own ENTER trace - the log ordering IS the proof"),
                ("priorResetEpoch", "the WO-1598 epoch read/bump"),
                ("s.Pets = new List<PetData>()", "the first persisted-field assignment"),
                ("Save()", "the write that overwrites the live slot"),
            };
            foreach (var (token, why) in mustFollow)
            {
                int at = body.IndexOf(token, StringComparison.Ordinal);
                if (at >= 0 && at < capture)
                    failures.Add("[ordering] the backup call sits AFTER " + why + " ('" + token + "'). " +
                                 "A backup taken after a mutation is a backup of the wipe.");
            }

            // And it must be FIRST outright: nothing but the opening brace before it.
            string prefix = body.Substring(1, Math.Max(0, capture - 1)).Trim();
            if (prefix.Contains(";") && !prefix.Contains("progressToLose"))
                failures.Add("[ordering] statements execute before the backup call: '" +
                             prefix.Substring(0, Math.Min(120, prefix.Length)) +
                             "...'. Only the has-anything-to-lose predicate may precede it; anything else " +
                             "is a line someone can later move a mutation into.");
        }

        // ── CASE 5 — the incident replaying through the fix ─────────────────
        private static void Case5_NothingToLose(List<string> failures)
        {
            var mem = new MemoryProvider();
            string good = SaveSchema.EmbedSignature(MakePopulatedSaveJson(9, true));
            mem.Write(SaveBackupService.LiveSlot, good);
            SaveBackupService.CaptureBeforeReset(true, "regression-good", mem);

            // Now the post-wipe world: the live slot holds a blank town and the title's
            // HasExistingSave() is false, so START NEW is frictionless by design.
            string blank = SaveSchema.EmbedSignature(MakePopulatedSaveJson(1, false));
            mem.Write(SaveBackupService.LiveSlot, blank);
            bool wrote = SaveBackupService.CaptureBeforeReset(false, "regression-blank", mem);

            if (wrote)
                failures.Add("[nothing-to-lose] a capture over a blank live state still wrote. That is the " +
                             "incident finishing itself: the second accidental START NEW copies the blank " +
                             "town over the only surviving copy of the realm.");
            if (mem.Read(SaveBackupService.BackupSlot) != good)
                failures.Add("[nothing-to-lose] the good backup was destroyed by a capture that had nothing " +
                             "worth saving. The existing generation must be left INTACT when the live state " +
                             "is blank.");
        }

        // ── CASE 6 — nothing restores itself ────────────────────────────────
        private static void Case6_NoAutoRestore(List<string> failures)
        {
            string backupSrc = ReadSource(BackupSrc, failures);
            if (backupSrc != null)
            {
                string s = StripComments(backupSrc);
                if (Regex.IsMatch(s, @"Write\s*\(\s*LiveSlot"))
                    failures.Add("[no-auto-restore] SaveBackupService writes the LIVE slot. It is a backup " +
                                 "taker and a reader; the moment it can write the live save it can silently " +
                                 "undo a New Game the player genuinely wanted.");
            }
            string serviceSrc = ReadSource(ServiceSrc, failures);
            if (serviceSrc != null && StripComments(serviceSrc).Contains("TryReadBackup"))
                failures.Add("[no-auto-restore] the reset path reads the backup back. Restoring is a " +
                             "deliberate operator action (WO-1688 §2.2), never something the client decides.");
        }

        // ── CASE 7 — the guard this feature must NOT weaken ─────────────────
        private static void Case7_EpochGuardIntact(List<string> failures)
        {
            string src = ReadSource(ServiceSrc, failures);
            if (src == null) return;
            var required = new (string token, string why)[]
            {
                ("SAVE_RESET_STALE", "the 409 the push side answers"),
                ("THIS DEVICE DOES NOT SELF-HEAL", "the in-code statement of the documented gap"),
                ("ResetEpoch", "the WO-1598 field itself"),
                ("serverEpoch", "the apply-side comparison"),
            };
            foreach (var (token, why) in required)
                if (!src.Contains(token))
                    failures.Add("[epoch-guard-intact] '" + token + "' (" + why + ") is GONE from " +
                                 ServiceSrc + ". WO-1688 §5 forbids weakening the WO-1598 reset-epoch guard " +
                                 "to make restores work - it is protecting a real failure mode, and the " +
                                 "restore path is a SERVER-side re-bless, not a client-side hole.");
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static string MakePopulatedSaveJson(double heroLevel, bool onboarded)
        {
            var file = new SaveSchema.SaveFile
            {
                Format = SaveSchema.FileFormat,
                StoreVersion = SaveSchema.CurrentVersion,
                ExportedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Wallet = null,
                State = new SaveSchema.PersistedState { HeroLevel = heroLevel, Onboarded = onboarded },
            };
            return JsonConvert.SerializeObject(file, SaveSchema.JsonSettings);
        }

        /// <summary>Brace-matched body starting at the first '{' at or after <paramref name="from"/>.</summary>
        private static string ExtractBraceBody(string src, int from)
        {
            int open = src.IndexOf('{', from);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }

        private static string ReadSource(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("[source] " + path + " not found - the file moved without updating this oracle");
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("[source] could not read " + path + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", " ");
        }
    }
}
