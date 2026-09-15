// =============================================================================
// PlayGateChunkSeamRegression [play-gate-chunk-seam]
// -----------------------------------------------------------------------------
// Assembly: the editor regression assembly (editor-only).
// Markers: PLAY_GATE_CHUNK_SEAM_OK (Debug.Log) / PLAY_GATE_CHUNK_SEAM_FAIL
// (LogError). Contract mirrors every other oracle here:
//   public static bool Run(out string reason)  -- and it NEVER throws.
//
// WO-1754, from the WO-1740 RCA of chain run 2026-09-15 14:14:58.
//
// WHAT WENT WRONG. GooglePlayPackagingGate.ScanStream reads an AAB entry in
// 64 KiB chunks. Both the allowlist window (IsAllowlistedOccurrence searches
// hit +/- phrase length) and the printable-run rule read context on BOTH SIDES
// of a hit -- and a chunk edge is a hard edge on both. In that build the token
// crypto sat at offset 1,900,536 and its suppressing phrase cryptography ran to
// 1,900,547, while chunk 29 ended at 1,900,544: the phrase was cut three bytes
// short, the hit was recorded LIVE, and a 455 MB artifact was REJECTED on a BCL
// type name (system.security.cryptography.hmacsha256) that every IL2CPP build
// on earth contains. Whole-file, that token had ZERO live occurrences.
//
// WHY IT NEEDED A REGRESSION AND NOT JUST A FIX. Bundle content is hashed, so
// every content build moves every offset. The defect therefore presents as
// FLAKINESS -- the same source tree passing and failing on consecutive runs --
// which is the failure mode least likely to be believed and most likely to be
// explained away. Nothing in the repo could reproduce it on demand. This suite
// can: it synthesises the straddle instead of waiting for one.
//
// THREE VECTORS, AND WHY A TAIL-ONLY BOUND IS NOT ENOUGH:
//   (a) TAIL       -- a hit near the chunk end loses its RIGHT context.
//   (b) HEAD       -- the retained bytes are re-scanned at the front of the NEXT
//                     chunk, where a hit whose phrase starts to its LEFT has that
//                     start cut off and fires, even though the previous chunk
//                     judged it correctly. A right bound alone leaves this open.
//   (c) WORD END   -- the matcher treated the end of the TEXT as a word end, and
//                     a chunk end is not a word end, so a short token ending
//                     exactly at a chunk tail passed the trailing-boundary test
//                     no matter what the next byte was.
//
// EVERY OFFSET BELOW IS DERIVED FROM THE IMPLEMENTATION -- ScanChunkSize and
// ScanRetainBytes -- never hard-coded. A hand-written 65536 or 264 in a test is
// the same duplicated state CLAUDE.md sec.2/sec.5/sec.16 each describe: it would
// go stale the moment the chunk or the retention changed, and would then be
// testing a seam that is no longer where it says it is.
//
// THE RED FIXTURE IS THE POINT OF THIS FILE. LegacyScanStream below rebuilds the
// PRE-WO-1754 loop (judge each chunk whole, carry one margin forward) and the
// suite FAILS if that fixture does NOT report the false positives. Without it,
// the cases could drift off the seam and pass for the wrong reason -- a green
// suite proving only that the synthetic streams no longer straddle anything.
//
// IT ALSO PINS THAT THE FIX DID NOT WEAKEN THE GATE. Cases D/E/F place REAL
// leaks at the same seam and require them to fire. A seam fix that silences the
// seam is not a fix; it is a hole with a comment on it.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pins that the Google Play artifact scanner judges every byte of an entry exactly
    /// once, with full context, regardless of where the chunk boundaries fall.
    /// </summary>
    public static class PlayGateChunkSeamRegression
    {
        private const string Tag = "[play-gate-chunk-seam]";

        // A representative binary entry: global-metadata.dat is where every C# string
        // literal and every type name in an IL2CPP player lands, and it is the entry that
        // carried all four of the 2026-09-15 offenders.
        private const string BinaryEntry = "base/assets/bin/Data/Managed/Metadata/global-metadata.dat";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                string[] vocab = GooglePlayPackagingGate.TokensForEntry(BinaryEntry);
                int chunk = GooglePlayPackagingGate.ScanChunkSize;
                int retain = GooglePlayPackagingGate.ScanRetainBytes(vocab);
                int legacyRetain = LegacyRetainBytes(vocab);
                int length = (2 * chunk) + 4096;   // >= 2 chunks, deliberately NOT a multiple

                // -- CASE A: vector (a), the MEASURED defect. ------------------------------
                // system.security.cryptography.hmacsha256 with the token crypto ending just
                // inside chunk 0 and the suppressing phrase cryptograph running past its end.
                const string Bcl = "system.security.cryptography.hmacsha256";
                int cryptoInBcl = Bcl.IndexOf("crypto", StringComparison.Ordinal);
                byte[] caseA = Synth(length, chunk - cryptoInBcl - 8, Bcl);

                // -- CASE B: vector (b), the head re-scan. ---------------------------------
                // libcrypto.so with the phrase starting BEFORE the retained window and the
                // token landing EXACTLY at the window start, so chunk 0 sees the whole phrase
                // and chunk 1 sees the token at index 0 with its phrase start cut away.
                //
                // ⛔ THE ALIGNMENT IS EXACT ON PURPOSE, AND IT WAS MEASURED. One byte further
                // in and the truncated view still shows the b of lib before the token, which
                // the LEADING-boundary rule rejects on its own - so the case would pass for a
                // reason that has nothing to do with the seam, and the red fixture below would
                // go quiet without anything being fixed. Landing the token at index 0 is what
                // makes the pre-WO-1754 scanner take the hit_index-is-zero short circuit and
                // report the false positive. Built at BOTH the current and the legacy
                // retention: the first is the case the shipped scanner must survive, the
                // second is the one the red fixture must still fire on.
                const string Ssl = "libcrypto.so.1.1";
                int cryptoInSsl = Ssl.IndexOf("crypto", StringComparison.Ordinal);
                byte[] caseBNow = Synth(length, chunk - retain - cryptoInSsl, Ssl);
                byte[] caseBLegacy = Synth(length, chunk - legacyRetain - cryptoInSsl, Ssl);

                // -- CASE C: vector (c), the false word end. -------------------------------
                // web3 ending EXACTLY at the chunk edge with a letter immediately after it.
                // The real string is web3x, which the trailing-boundary rule must reject.
                string web3Letter = new string('-', 20) + "web3x" + new string('-', 20);
                byte[] caseC = Synth(length, chunk - 24, web3Letter);

                RequireClean(caseA, vocab, "crypto", failures,
                    "CASE A (tail): the BCL type name system.security.cryptography.hmacsha256 still " +
                    "reads as a live crypto leak when its suppressing phrase straddles a chunk end. " +
                    "This is the exact false positive that rejected the 2026-09-15 AAB.");
                RequireClean(caseBNow, vocab, "crypto", failures,
                    "CASE B (head, current retention): libcrypto reads as a live crypto leak when the " +
                    "retained bytes are re-judged at the head of the next chunk without their left " +
                    "context. A right-hand bound alone does not close this.");
                RequireClean(caseBLegacy, vocab, "crypto", failures,
                    "CASE B (head, legacy retention offset): same vector at the pre-WO-1754 retention " +
                    "distance; it must be clean too, or the fix only moved the seam.");
                RequireClean(caseC, vocab, "web3", failures,
                    "CASE C (word end): the short token web3 was accepted at a chunk tail although the " +
                    "very next byte is a letter. A chunk end is not a word end.");

                // -- THE RED FIXTURE. -----------------------------------------------------
                // The pre-WO-1754 loop MUST report these, or the cases are no longer on the
                // seam and their passes above mean nothing.
                RequireLegacyDirty(caseA, vocab, "crypto", failures,
                    "CASE A no longer straddles a chunk boundary, so its pass proves nothing");
                RequireLegacyDirty(caseBLegacy, vocab, "crypto", failures,
                    "CASE B no longer exercises the head re-scan, so its pass proves nothing");
                RequireLegacyDirty(caseC, vocab, "web3", failures,
                    "CASE C no longer lands on a chunk tail, so its pass proves nothing");

                // -- CASES D/E/F: the fix must not have silenced the seam. -----------------
                // D: a live solana in the deferred tail of a stream whose length is an EXACT
                //    multiple of the chunk -- the case with no next chunk to inherit it.
                //    globalgamemanagers.assets.split0 is exactly 1,048,576 bytes, sixteen
                //    chunks with no remainder, in the very artifact this was measured on.
                byte[] caseD = Synth(2 * chunk, (2 * chunk) - 26, new string('-', 10) + "solana" + new string('-', 10));
                RequireDirty(caseD, vocab, "solana", failures,
                    "CASE D: a real solana literal in the final deferred tail of an exact-multiple " +
                    "entry was never judged at all. Deferring a hit with no next chunk to judge it " +
                    "turns a leak into silence, which is worse than the false positive this fixes.");

                // E: a live solana straddling the chunk edge itself.
                byte[] caseE = Synth(length, chunk - 3, new string('-', 10) + "solana" + new string('-', 10));
                RequireDirty(caseE, vocab, "solana", failures,
                    "CASE E: a real solana literal straddling a chunk boundary is no longer reported");

                // F: the short-token trailing rule still fires when the boundary is REAL.
                string web3Clean = new string('-', 20) + "web3-" + new string('-', 20);
                byte[] caseF = Synth(length, chunk - 24, web3Clean);
                RequireDirty(caseF, vocab, "web3", failures,
                    "CASE F: a real web3 literal ending at a chunk tail with a genuine word boundary " +
                    "is no longer reported. Vector (c) must reject an unknown neighbour, not every one.");

                // -- CASE G: the WO-1754 exact-identifier allowlist, scope proven both ways. -
                CheckExactIdentifierScope(failures);
            }
            catch (Exception ex)
            {
                failures.Add("threw: " + ex.GetType().Name + " " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = "PLAY_GATE_CHUNK_SEAM_FAIL: " + string.Join(" | ", failures.ToArray());
                Debug.LogError(Tag + " " + reason);
                return false;
            }

            reason = "PLAY_GATE_CHUNK_SEAM_OK: seam closed on all three vectors, real leaks at the " +
                     "seam still fire, owner-ruled identifiers suppressed by whole identity only";
            Debug.Log(Tag + " " + reason);
            return true;
        }

        /// <summary>
        /// The owner-ruled residuals must be suppressed, and ONLY as whole identifiers. The
        /// negative half is the one that matters: a blunt phrase entry reading SolanaWallet
        /// would also suppress SolanaWalletAdapterWebGL, a real crypto surface the gate's own
        /// UniTask comment says must keep firing -- and the walletadapter token cannot be
        /// relied on as a backstop there, because it needs a LEADING word boundary and the
        /// character before WalletAdapter in that identifier is the a of Solana.
        /// </summary>
        private static void CheckExactIdentifierScope(List<string> failures)
        {
            // NUL-packed name-table entries: this is the shape IL2CPP actually writes.
            Suppressed("\0SolanaWallet\0", "solana", false, failures,
                "SkinAuthMode.SolanaWallet (WO-1377, owner-ruled: may not be renamed) still rejects the artifact");
            Suppressed("\0SolanaDappStore\0", "solana", false, failures,
                "PaymentChannel.SolanaDappStore (WO-1377, owner-ruled) still rejects the artifact");
            Suppressed("\0dotr-arena-skr-balance\0", "skr", false, failures,
                "the live arena save key (WO-1366 s4, renaming it reseeds every balance) still rejects the artifact");
            // The shipped canonical value the identifier is name-bound to.
            Suppressed("{ \"authMode\": \"SolanaWallet\" }", "solana", true, failures,
                "skin.json authMode SolanaWallet still rejects the artifact");
            // The receipt form, and only the receipt form.
            Suppressed("Dependencies:[\"com.solana.unity_sdk@1.2.9\"]", "solana", false, failures,
                "the Unity package-version receipt still rejects the artifact");

            Fires("\0SolanaWalletAdapterWebGL\0", "solana", false, failures,
                "SolanaWalletAdapterWebGL is suppressed by the SolanaWallet ruling -- the allowlist has " +
                "been widened from an identifier into a prefix and a real wallet surface can now ship");
            Fires("\0SolanaDappStoreProvider\0", "solana", false, failures,
                "an identifier merely PREFIXED with a ruled one is suppressed");
            Fires("\0dotr-arena-skr-balance-v2\0", "skr", false, failures,
                "a key merely prefixed with the ruled save key is suppressed");
            Fires("packages/com.solana.unity_sdk/runtime/plugins/solanawalletadapterwebgl/x.cs", "solana", false, failures,
                "the package-version receipt entry also suppresses a real SDK source PATH");
            Fires("\0SolanaMobileStakeClient\0", "solana", false, failures,
                "an unruled solana identifier is suppressed");
        }

        private static void Suppressed(string text, string token, bool readable, List<string> failures, string message)
        {
            if (GooglePlayPackagingGate.MatchesTokenInPayload(text, token, readable))
                failures.Add("CASE G: " + message);
        }

        private static void Fires(string text, string token, bool readable, List<string> failures, string message)
        {
            if (!GooglePlayPackagingGate.MatchesTokenInPayload(text, token, readable))
                failures.Add("CASE G: " + message);
        }

        // ---------------------------------------------------------------------------
        // Fixtures
        // ---------------------------------------------------------------------------

        /// <summary>
        /// A synthetic entry: NUL bulk with one Latin-1 segment written at an absolute
        /// offset. NUL bulk on purpose -- it cannot satisfy the printable-run rule, so the
        /// only evidence in the stream is the segment the case is about.
        /// </summary>
        private static byte[] Synth(int length, int offset, string segment)
        {
            var bytes = new byte[length];
            for (int i = 0; i < segment.Length; i++)
            {
                int at = offset + i;
                if (at >= 0 && at < length) bytes[at] = (byte)segment[i];
            }
            return bytes;
        }

        private static List<string> ScanSynthetic(byte[] payload, string[] tokens)
        {
            var hits = new List<string>();
            using (var stream = new MemoryStream(payload, writable: false))
                GooglePlayPackagingGate.ScanStream(stream, BinaryEntry, tokens, readableEntry: false, hits: hits);
            return hits;
        }

        private static bool Reported(List<string> hits, string token)
        {
            string needle = " token:" + token;
            foreach (string hit in hits)
                if (hit != null && hit.EndsWith(needle, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void RequireClean(byte[] payload, string[] tokens, string token, List<string> failures, string message)
        {
            if (Reported(ScanSynthetic(payload, tokens), token)) failures.Add(message);
        }

        private static void RequireDirty(byte[] payload, string[] tokens, string token, List<string> failures, string message)
        {
            if (!Reported(ScanSynthetic(payload, tokens), token)) failures.Add(message);
        }

        private static void RequireLegacyDirty(byte[] payload, string[] tokens, string token, List<string> failures, string message)
        {
            var hits = new List<string>();
            using (var stream = new MemoryStream(payload, writable: false))
                LegacyScanStream(stream, tokens, hits);
            if (!Reported(hits, token))
                failures.Add("RED FIXTURE: " + message + " -- the pre-WO-1754 scanner does not fire on it, " +
                             "so this case no longer reproduces the defect and its clean result is meaningless");
        }

        /// <summary>
        /// The PRE-WO-1754 loop, verbatim in shape: read a chunk, judge the whole buffered
        /// text, carry ONE margin forward. It exists to prove the cases above actually sit on
        /// the seam. It is a FIXTURE, not a second policy: it reads the same vocabulary and
        /// calls the same matcher with a whole-text window, which is exactly what the old
        /// code did.
        /// </summary>
        private static void LegacyScanStream(Stream stream, string[] tokens, List<string> hits)
        {
            int chunkSize = GooglePlayPackagingGate.ScanChunkSize;
            int overlap = LegacyRetainBytes(tokens);
            var buffer = new byte[chunkSize + overlap];
            int retained = 0;

            while (true)
            {
                int read = stream.Read(buffer, retained, chunkSize);
                if (read <= 0) break;
                int count = retained + read;

                var chars = new char[count];
                for (int i = 0; i < count; i++) chars[i] = (char)buffer[i];
                string asciiText = new string(chars);
                string utf16Text = Encoding.Unicode.GetString(buffer, 0, count - (count % 2));

                foreach (string token in tokens)
                {
                    if (GooglePlayPackagingGate.MatchesTokenInPayload(asciiText, token, readableEntry: false) ||
                        GooglePlayPackagingGate.MatchesTokenInPayload(utf16Text, token, readableEntry: false))
                        hits.Add("content:" + BinaryEntry + " token:" + token);
                }

                retained = Math.Min(overlap, count);
                Buffer.BlockCopy(buffer, count - retained, buffer, 0, retained);
            }
        }

        private static int LegacyRetainBytes(string[] tokens)
        {
            int overlap = 0;
            foreach (string t in tokens)
            {
                int bytes = Encoding.Unicode.GetByteCount(t);
                if (bytes > overlap) overlap = bytes;
            }
            overlap += 4 * GooglePlayPackagingGate.MinPrintableRunForShortTokens + 128;
            if ((overlap & 1) != 0) overlap++;
            return overlap;
        }
    }
}
