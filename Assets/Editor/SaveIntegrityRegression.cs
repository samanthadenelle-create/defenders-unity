// =============================================================================
// SaveIntegrityRegression — LB-3 save-integrity hard gate (editor-only, no PlayMode).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor
//
// WHY THIS EXISTS (LB-3 / launch-blocker):
//   The local save was plaintext, unsigned PlayerPrefs JSON — a player could edit
//   the blob (resources.*, ownedItemIds, …) and relaunch to load it as truth.
//   The fix writes a keyed HMAC-SHA256 ALONGSIDE the payload and verifies it on
//   load; a mismatch is REJECTED and the game falls back to fresh defaults. This
//   gate proves that contract holds, so a regression that weakens or removes the
//   signature is caught headlessly before it ships.
//
//   It exercises the SAME integrity primitive GameStateService.Load() uses
//   (SaveSchema.ComputeSignature / VerifySignature over the serialized SaveFile
//   JSON) — pure editor, no PlayMode, no PlayerPrefs, no live SO. That is exactly
//   the load DECISION: valid-sig => accept, tampered-payload => reject (fresh).
//
// ENTRY POINT:
//   public static bool Run(out string reason)
//   Wire into RegressionSuite.RunAll (see that file) as the "save-integrity" case.
// =============================================================================

using DeNelle.Core.State;
using Newtonsoft.Json;

namespace DeNelle.Editor
{
    /// <summary>
    /// Editor-only LB-3 gate: a validly-signed save verifies (load accepts); a save
    /// whose payload was tampered (a resource value bumped) fails verification (load
    /// rejects and keeps fresh state). No PlayMode.
    /// </summary>
    public static class SaveIntegrityRegression
    {
        /// <summary>
        /// Runs the integrity round-trip + tamper-rejection assertions.
        /// Returns true when both hold; on false, <paramref name="reason"/> names
        /// the failing assertion.
        /// </summary>
        public static bool Run(out string reason)
        {
            // 1. Build a valid save EXACTLY as GameStateService.Save() serializes it.
            var file = new SaveSchema.SaveFile
            {
                Format = SaveSchema.FileFormat,
                StoreVersion = SaveSchema.CurrentVersion,
                ExportedAt = "2026-06-28T00:00:00.000Z",
                Wallet = "guest-local-regression",
                State = new SaveSchema.PersistedState
                {
                    Resources = new ResourceBalance { Crystals = 100, Stone = 50, Coins = 25 },
                    BestWave = 7,
                    OwnedItemIds = new System.Collections.Generic.List<string> { "blink_armor_basic1" },
                },
            };

            string json = JsonConvert.SerializeObject(file, SaveSchema.JsonSettings);

            // 2. Sign it, then assert the signature verifies → LOAD WOULD SUCCEED.
            string sig = SaveSchema.ComputeSignature(json);
            if (!SaveSchema.VerifySignature(json, sig))
            {
                reason = "valid save did NOT verify against its own signature (load would wrongly reject a legit save).";
                return false;
            }

            // 3. TAMPER: bump a resource value in the serialized blob, keep the old
            //    sig — assert verification now FAILS → LOAD WOULD REJECT (fresh).
            //    (Mirrors a player editing PlayerPrefs to inflate crystals.)
            string tampered = json.Replace("\"crystals\":100", "\"crystals\":999999");
            if (tampered == json)
            {
                reason = "test harness could not locate the crystals field to tamper (serialized shape changed?).";
                return false;
            }
            if (SaveSchema.VerifySignature(tampered, sig))
            {
                reason = "TAMPERED save verified against the original signature — integrity gate is NOT rejecting edits (LB-3 hole).";
                return false;
            }

            // 4. Sanity: a fresh signature over the tampered blob obviously matches
            //    itself (the HMAC is deterministic) — the protection is that the
            //    attacker cannot PRODUCE that signature without the embedded key,
            //    and a payload edit without re-signing is what we just rejected.
            string reSig = SaveSchema.ComputeSignature(tampered);
            if (!SaveSchema.VerifySignature(tampered, reSig))
            {
                reason = "deterministic HMAC failed self-consistency (ComputeSignature/VerifySignature disagree).";
                return false;
            }

            reason = "valid save verified; tampered save rejected; HMAC self-consistent.";
            return true;
        }
    }
}
