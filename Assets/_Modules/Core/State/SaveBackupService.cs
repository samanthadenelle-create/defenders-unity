// =============================================================================
// SaveBackupService — the ONE-GENERATION wipe-undo (WO-1688).
// -----------------------------------------------------------------------------
// WHY THIS EXISTS, stated in the incident's own words (WO-1688 §1): on
// 2026-09-10 a single unintended touch on the title's START NEW button
// permanently destroyed the owner's realm. The captured device log shows the
// whole loss inside three milliseconds and with no way back:
//
//   12:18:56.608 [Flow:Save] ResetToNewGame: ENTER ...
//   12:18:56.614 [Flow:Save] wrote signed save via LocalSaveProvider (len=3417).
//
// ...where the save that had just been LOADED was len=9457. `LocalSaveProvider`
// is a SINGLE-SLOT store (Exists/Read/Write/Delete all take one slot and every
// call site passes SaveSchema.PlayerPrefsKey), so the moment the reset's own
// Save() wrote len=3417 over "dotr-save" the prior signed body did not exist
// anywhere on the device. WO-1688 §2.1 closes the accidental PRESS; this file
// closes the "and nothing to fall back on" half.
//
// THE CONTRACT — deliberately small:
//   * ONE generation. A new backup overwrites the old. This is a wipe-undo, not
//     a save-history feature (WO-1688 §2.2), so there is no ladder of slots to
//     reason about, expire or migrate.
//   * Written through the SAME ISaveProvider seam as the live slot, so the
//     round-trip and the signature rules are identical by construction and a
//     future cloud provider carries the backup with it. No parallel storage
//     path — that is the WO's explicit instruction.
//   * The stored value is copied VERBATIM. Since the LB-3 atomic envelope
//     (SaveSchema.EmbedSignature) the HMAC lives in the FRONT of the stored
//     string — "<64-hex-sig>\n<json>" — so copying the bytes preserves a
//     signature that still validates against the copied payload. Re-signing
//     here would be strictly worse: it would launder a body whose signature had
//     ALREADY been broken into one that looks intact.
//     The LEGACY sibling key (slot + SaveSchema.SignatureKeySuffix) is copied
//     too when present, because LocalSaveProvider.Delete still maintains it and
//     a save written before the envelope landed can still be sitting on a real
//     device.
//   * The slot name is DERIVED (SaveSchema.PlayerPrefsKey + BackupKeySuffix),
//     never a second literal. See the comment on BackupKeySuffix.
//   * RESTORING IS NOT AUTOMATIC. This file exposes a READ
//     (TryReadBackup) and never writes the live slot. An auto-restore would
//     fight the new game a player may genuinely have wanted (WO-1688 §2.2), and
//     the deliberate operator restore is written up in the WO's RESULT.
//
// EVERY PATH TRACES. A backup nobody can prove exists at 3am is not a backup
// (WO-1688 §2.2), so the skip, the absent-save case, the IO failure and the
// success each emit their own [Flow:Save] line, and the success line names both
// byte lengths and both slots.
// =============================================================================

using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.State
{
    /// <summary>
    /// One-generation local backup of the signed save, taken immediately before
    /// <see cref="GameStateService.ResetToNewGame"/> mutates anything. Read-only
    /// afterwards: nothing here ever writes the live save slot.
    /// </summary>
    public static class SaveBackupService
    {
        /// <summary>The live save slot (the one a reset overwrites).</summary>
        public static string LiveSlot => SaveSchema.PlayerPrefsKey;

        /// <summary>The one-generation backup slot, derived from the live slot.</summary>
        public static string BackupSlot => SaveSchema.PlayerPrefsKey + SaveSchema.BackupKeySuffix;

        /// <summary>The legacy sibling signature key for a given slot.</summary>
        public static string SignatureSlot(string slot) => slot + SaveSchema.SignatureKeySuffix;

        /// <summary>
        /// Copy the currently-stored signed save into the backup slot. Call this
        /// BEFORE the first mutation of a reset.
        /// <para><paramref name="hasProgressToLose"/> is the caller's answer to "is
        /// there anything here worth keeping". It is NOT a convenience: after a wipe
        /// the live save is a blank town, and a second START NEW press would
        /// otherwise copy that blank over the good backup — the same gesture that
        /// caused the incident, now destroying the undo as well. False therefore
        /// SKIPS the write and LEAVES the existing backup intact.</para>
        /// </summary>
        /// <param name="hasProgressToLose">False when the live state is a fresh/blank
        /// town (nothing to lose) — the existing backup is preserved untouched.</param>
        /// <param name="reason">Short caller tag for the trace (e.g. "ResetToNewGame").</param>
        /// <param name="provider">IO seam; defaults to <see cref="GameStateService.Provider"/>.</param>
        /// <returns>True only when a backup body was actually written this call.</returns>
        public static bool CaptureBeforeReset(bool hasProgressToLose, string reason,
                                              ISaveProvider provider = null)
        {
            var p = provider ?? GameStateService.Provider;
            string why = string.IsNullOrEmpty(reason) ? "unspecified" : reason;

            if (p == null)
            {
                FlowTrace.Fail("Save",
                    "SaveBackup: no ISaveProvider available — the pre-reset backup was NOT taken. " +
                    "The wipe still proceeds (a failed backup must never softlock New Game), but " +
                    "this reset is UNDO-LESS and this line is the only warning of it.");
                return false;
            }

            if (!hasProgressToLose)
            {
                FlowTrace.Step("Save",
                    "SaveBackup: SKIPPED for " + why + " — the live state has nothing to lose " +
                    "(no hero chosen and onboarding not complete). The EXISTING backup in '" +
                    BackupSlot + "' is left intact on purpose: overwriting a good realm with a " +
                    "blank one is exactly how a second accidental START NEW would finish the job " +
                    "the first one started (WO-1688).");
                return false;
            }

            if (!p.Exists(LiveSlot))
            {
                FlowTrace.Step("Save",
                    "SaveBackup: nothing stored under '" + LiveSlot + "' — no backup taken for " +
                    why + ". This is the genuine fresh-install shape, not a failure.");
                return false;
            }

            bool wrote = false;
            Guard.Try("Save", "SaveBackup.CaptureBeforeReset (pre-reset copy)", () =>
            {
                string stored = p.Read(LiveSlot);
                if (string.IsNullOrEmpty(stored))
                {
                    FlowTrace.Warn("Save",
                        "SaveBackup: '" + LiveSlot + "' exists but its value is EMPTY — nothing " +
                        "worth copying, so the existing backup is left untouched.");
                    return;
                }

                // The LEGACY sibling signature, handled FIRST so the backup body is the
                // last write and a tear can never leave a body paired with a stale sig.
                string liveSig = SignatureSlot(LiveSlot);
                string backupSig = SignatureSlot(BackupSlot);
                p.Delete(backupSig);                       // drop any sibling from the previous generation
                bool siblingCopied = false;
                if (p.Exists(liveSig))
                {
                    string sig = p.Read(liveSig);
                    if (!string.IsNullOrEmpty(sig)) { p.Write(backupSig, sig); siblingCopied = true; }
                }

                // VERBATIM. The LB-3 envelope keeps the HMAC in the front of this string,
                // so the copy carries a signature that validates against the copied body.
                p.Write(BackupSlot, stored);
                wrote = true;

                // The ORDERING PROOF the WO asks for: this line is emitted before
                // ResetToNewGame's own "ENTER" line, because the call site is the first
                // statement in that method. A log where these two appear the other way
                // round is a REGRESSION, not a formatting quirk.
                SaveSchema.TryExtractSigned(stored, out bool sigPresent, out bool sigValid);
                string sigState;
                if (!sigPresent) sigState = "legacy unsigned body";
                else if (sigValid) sigState = "embedded-sig VALID";
                else sigState = "embedded-sig PRESENT BUT INVALID (copied as-is, never re-signed)";
                string siblingState = siblingCopied ? "legacy sibling sig copied" : "no legacy sibling sig";
                FlowTrace.Step("Save",
                    "SaveBackup: WROTE " + stored.Length + " bytes '" + LiveSlot + "' -> '" +
                    BackupSlot + "' BEFORE " + why + " mutated anything (" + sigState + "; " +
                    siblingState + "). ONE generation only — this overwrote the previous backup.");
            });

            if (!wrote)
                FlowTrace.Warn("Save",
                    "SaveBackup: no backup body was written for " + why + " — the reset proceeds " +
                    "UNDO-LESS. Read the [Flow:Save] line above for which branch it took.");
            return wrote;
        }

        /// <summary>True when a one-generation backup body is present.</summary>
        public static bool HasBackup(ISaveProvider provider = null)
        {
            var p = provider ?? GameStateService.Provider;
            return p != null && p.Exists(BackupSlot) && !string.IsNullOrEmpty(p.Read(BackupSlot));
        }

        /// <summary>
        /// Read the backup body back out, exactly as it was stored. READ ONLY — this
        /// never writes the live slot, because restoring is a deliberate operator
        /// action, not something the client decides for the player (WO-1688 §2.2).
        /// The returned string is the raw stored value: feed it through the SAME load
        /// path the live slot uses (TryExtractSigned -> SaveFile -> SaveMigrator ->
        /// SaveSchema.Validate) to restore it.
        /// </summary>
        public static bool TryReadBackup(out string stored, ISaveProvider provider = null)
        {
            stored = null;
            var p = provider ?? GameStateService.Provider;
            if (p == null || !p.Exists(BackupSlot)) return false;
            string body = p.Read(BackupSlot);
            if (string.IsNullOrEmpty(body)) return false;
            stored = body;
            return true;
        }
    }
}
