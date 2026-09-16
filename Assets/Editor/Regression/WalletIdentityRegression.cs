// =============================================================================
// WalletIdentityRegression [wallet-identity]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core; the Wallet and
// Onboarding assemblies are deliberately NOT referenced - see "WHY SOURCE LINTS").
//
// Pins the three P0s the pre-APK identity audit (2026-08-02) found on the front
// door the owner is about to hand to outside testers:
//
//   P0-1  ANDROID LOGIN HARD-SOFTLOCK. LoginPanelController.SetBusy(true) disabled
//         "Play as Guest" along with everything else, and every await on that
//         surface was unbounded. A tester whose wallet app never answered - or who
//         had no wallet app at all - sat on "Opening your wallet..." with EVERY
//         control dead and no back button. The escape hatch was disabled by the
//         very state it exists to escape.
//   P0-2  THE STUB WALLET MINTED A CONSTANT-SEEDED, REAL-LOOKING ADDRESS.
//         StubWalletProvider used `new System.Random(0xDEFEED)` and emitted a plain
//         44-char base58 string. Every device produced the SAME first address, and
//         the cloud-identity test was a DENYLIST ("not guest-local-*"), so that
//         address read as a real player. One SOLANA_SDK-less Android build would
//         have pointed every tester at ONE player_data row.
//   P0-3  EMAIL SIGN-IN BOUND THE FIREBASE UID AS THE SAVE KEY. That contradicts
//         the owner ruling (Firebase = ACCESS, wallet = DATA) AND a Firebase UID
//         cannot pass api/_lib/wallet-auth.js's base58 WALLET_RE, so those saves could never
//         authenticate once backend auth flips to Enforced.
//
// Cases:
//   1 [shape-allowlist]  GameStateService.IsCloudIdentityShaped is a real ALLOWLIST,
//                        executed live against genuine strings: a real pubkey passes;
//                        a stub address, a Firebase UID, a guest key, a debug string,
//                        empty and over/under-length all FAIL.
//   2 [server-contract]  That allowlist is byte-for-byte the rule the SERVER applies -
//                        the charset and the 32..44 bounds are read out of
//                        the server's OWN regex literal, so a divergence
//                        between client and server is a FAIL here, not a 401 on a
//                        tester's phone. (api/ is READ, never written.)
//   3 [stub-unmistakable] StubWalletProvider mints marker + base58 (the marker's '-'
//                        is not a base58 glyph, so a stub address fails Case 1 BY
//                        CONSTRUCTION), is seeded PER INSTANCE, and reports
//                        CanSignMessages => false.
//   4 [real-wallet-gate] WalletService.IsRealSigningWallet is a POSITIVE attestation
//                        (connected AND not the stub AND can actually sign), the
//                        attested BindWallet overload is what the wallet path calls,
//                        and IsRealWalletConnected requires shape AND attestation AND
//                        currency - never the old denylist.
//   5 [wallet-only-identity] LoginViewModel has NO non-wallet identity path at all
//                        (WO-837-B, owner ruling 2026-08-21 - dApp Store only), and the
//                        wallet connect path is BindPlayer's only call site.
//   6 [guest-escape]     No code path disables the escape hatch: SetBusy does not
//                        touch _guest, OnPlayAsGuest is not gated on _busy, and every
//                        await on the login surface is bounded by a Timeout.
//   7 [bug-attribution]  A bug submitted from Settings carries an identity AND stack
//                        frames: BugReportVM sends playerId = the bound save key, and
//                        BreakCaptureHarness's report tail no longer discards the
//                        `stack` argument (it used to send the exception MESSAGE only).
//
// WHY SOURCE LINTS: DeNelle.EditorRegression does not reference DeNelle.Wallet or
// DeNelle.Onboarding, and adding those references to make three asserts prettier
// would couple the whole regression suite to the payment stack. The things pinned
// there are DELETED TERMS and DELETED GUARDS ("_guest is absent from SetBusy",
// "the stub seed is not a constant") - exactly the class of regression a lint
// catches precisely. Comments are stripped before every lint, so prose can never
// satisfy one. Case 1 - the assertion that actually matters, "a stub can never key
// a cloud save" - is LIVE, not a lint.
//
// Markers: WALLET_IDENTITY_OK / WALLET_IDENTITY_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.WalletIdentityRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Editor.Regression
{
    public static class WalletIdentityRegression
    {
        // ── Sources under lint ────────────────────────────────────────────────
        private const string StubSrc      = "Assets/_Modules/Wallet/StubWalletProvider.cs";
        private const string WalletSrc    = "Assets/_Modules/Wallet/WalletService.cs";
        private const string SkinSrc      = "Assets/_Modules/Wallet/WalletSkinBootstrap.cs";
        private const string StateSrc     = "Assets/_Modules/Core/State/GameStateService.cs";
        private const string SignerSrc    = "Assets/_Modules/Core/Backend/BackendRequestSigner.cs";
        private const string LoginVmSrc   = "Assets/_Modules/Onboarding/LoginViewModel.cs";
        private const string LoginViewSrc = "Assets/_Modules/Onboarding/LoginPanelController.cs";
        private const string BugVmSrc     = "Assets/_Modules/HUD/BugReportVM.cs";
        private const string CaptureSrc   = "Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs";

        /// <summary>
        /// The SERVER's identity rules, READ (never written) so client and server cannot
        /// drift apart silently. api/ is owned by another lane; this suite only observes it.
        /// The rules live in _lib/wallet-auth.js as of 2026-08-02 (they were inline in
        /// api/auth/nonce.js before that lane extracted them), so both are searched and the
        /// FIRST that yields a wallet regex wins.
        /// </summary>
        private static readonly string[] ServerAuthSrcs =
        {
            "api/_lib/wallet-auth.js",
            "api/auth/nonce.js",
        };

        /// <summary>
        /// The marker every stub address carries. Duplicated here ON PURPOSE rather than
        /// referenced: this suite must fail if StubWalletProvider stops emitting it, and a
        /// compile-time reference would simply follow the constant wherever it was moved.
        /// Case 3 lints that the provider still uses this exact literal.
        /// </summary>
        private const string StubMarker = "stub-wallet-";

        /// <summary>A REAL, well-formed Solana pubkey shape (44 base58 chars) - the one
        /// input that MUST be accepted, so Case 1 cannot pass by rejecting everything.</summary>
        private const string RealPubkey = "7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU";

        /// <summary>A genuine 28-char Firebase UID shape - the P0-3 offender.</summary>
        private const string FirebaseUid = "kJ3nR7pQwXcYb2Zt8VmL0aHdEg1F";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("WALLET_IDENTITY_OK - " + reason);
            else Debug.LogError("WALLET_IDENTITY_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "shape-allowlist",   () => Case1_ShapeAllowlist(failures, notes));
                Case(failures, "server-contract",   () => Case2_ServerContract(failures, notes));
                Case(failures, "stub-unmistakable", () => Case3_StubUnmistakable(failures, notes));
                Case(failures, "real-wallet-gate",  () => Case4_RealWalletGate(failures));
                Case(failures, "wallet-only-identity", () => Case5_WalletIsTheOnlyIdentity(failures));
                Case(failures, "guest-escape",      () => Case6_GuestEscape(failures, notes));
                Case(failures, "bug-attribution",   () => Case7_BugAttribution(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "WALLET IDENTITY OK - a stub address can never pass the cloud-identity " +
                         "allowlist, that allowlist matches the server's own wallet regex, only a " +
                         "provider-attested real signing wallet keys a cloud save, the wallet is the " +
                         "ONLY identity path on the login surface, the guest escape hatch is never disabled and every " +
                         "login await is bounded, and a Settings bug report carries both an identity " +
                         "and stack frames" + noteStr;
                return true;
            }
            reason = "wallet-identity FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1 - the allowlist, executed LIVE
        // =====================================================================
        // This is the assertion the whole audit rests on: "a stub address can never
        // pass the real-wallet predicate". It runs the SHIPPING predicate against real
        // strings - no lint, no mirror implementation.
        private static void Case1_ShapeAllowlist(List<string> failures, List<string> notes)
        {
            // MUST pass - otherwise the allowlist locks real players out of their saves.
            if (!GameStateService.IsCloudIdentityShaped(RealPubkey))
                failures.Add("[shape-allowlist] a well-formed 44-char base58 pubkey was REJECTED - " +
                             "the allowlist is too tight and would lock real wallets out of cloud save");

            // MUST fail - each of these is a live P0 if it gets through.
            RejectOrFail(failures, StubMarker + "9xQeWvG8mNfR3kTaLbYcHdJpZs2Uvi4o",
                         "a devnet STUB address (P0-2: two devices sharing one save row)");
            RejectOrFail(failures, FirebaseUid,
                         "a Firebase UID (P0-3: a save key the server's nonce check 401s)");
            RejectOrFail(failures, "guest-local-4f9c1a2b3d4e5f60718293a4b5c6d7e8",
                         "a guest/device key (local-only by design)");
            RejectOrFail(failures, "0xTESTWALLET", "a debug bind string");
            RejectOrFail(failures, null, "a null id");
            RejectOrFail(failures, "", "an empty id");
            RejectOrFail(failures, new string('A', 31), "a 31-char id (under the server's 32 floor)");
            RejectOrFail(failures, new string('A', 45), "a 45-char id (over the server's 44 ceiling)");
            // The four base58-excluded glyphs, each swapped into an otherwise valid key.
            foreach (char bad in new[] { '0', 'O', 'I', 'l' })
                RejectOrFail(failures, bad + RealPubkey.Substring(1),
                             "an id containing the non-base58 glyph '" + bad + "'");

            // The stub marker's hyphen is what makes P0-2 structural rather than a policy
            // the next author can "clean up". Prove the glyph itself is disqualifying.
            if (GameStateService.IsCloudIdentityShaped(RealPubkey.Substring(0, 20) + "-" +
                                                      RealPubkey.Substring(21)))
                failures.Add("[shape-allowlist] a '-' no longer disqualifies an id - the stub marker's " +
                             "structural safety is GONE and a stub address could key a cloud save");
            else
                notes.Add("stub safety is structural ('-' is not base58), not a denylist entry");
        }

        private static void RejectOrFail(List<string> failures, string id, string what)
        {
            if (GameStateService.IsCloudIdentityShaped(id))
                failures.Add("[shape-allowlist] ACCEPTED " + what + " as a cloud save key: '" +
                             Describe(id) + "'");
        }

        // =====================================================================
        //  CASE 2 - client rule == server rule
        // =====================================================================
        private static void Case2_ServerContract(List<string> failures, List<string> notes)
        {
            string js = null, from = null;
            Match m = Match.Empty;
            foreach (string path in ServerAuthSrcs)
            {
                string text = ReadOptional(path);
                if (text == null) continue;
                js = text; from = path;
                // Pull the wallet regex literal straight out of the server. If the backend
                // lane tightens or loosens it, this fails HERE instead of on a tester's phone.
                m = Regex.Match(text, @"/\^\[(?<set>[^\]]+)\]\{(?<lo>\d+),(?<hi>\d+)\}\$/");
                if (m.Success) break;
            }

            if (js == null)
            {
                notes.Add("no api/ auth source present in this checkout (" +
                          string.Join(", ", ServerAuthSrcs) + ") - the server-contract case could not " +
                          "run. It is NOT silently passing; it simply had nothing to read.");
                return;
            }
            if (!m.Success)
            {
                failures.Add("[server-contract] found " + from + " but no wallet regex literal in any of " +
                             string.Join(", ", ServerAuthSrcs) + " - either the server stopped validating " +
                             "the wallet shape, or the rule moved again and this oracle is now blind to a " +
                             "real client/server divergence");
                return;
            }

            string set = m.Groups["set"].Value;
            int lo = int.Parse(m.Groups["lo"].Value);
            int hi = int.Parse(m.Groups["hi"].Value);

            if (set != "1-9A-HJ-NP-Za-km-z")
                failures.Add("[server-contract] the server charset changed to '" + set +
                             "' - GameStateService.IsCloudIdentityShaped still enforces base58 " +
                             "(1-9A-HJ-NP-Za-km-z) and the two now disagree");

            // Prove the BOUNDS agree by executing the client predicate at each edge.
            if (GameStateService.IsCloudIdentityShaped(new string('A', lo - 1)))
                failures.Add("[server-contract] the client accepts " + (lo - 1) +
                             " chars but the server floor is " + lo + " - such a save would 401");
            if (!GameStateService.IsCloudIdentityShaped(new string('A', lo)))
                failures.Add("[server-contract] the client rejects " + lo +
                             " chars, which the server accepts - the client is stricter than the contract");
            if (!GameStateService.IsCloudIdentityShaped(new string('A', hi)))
                failures.Add("[server-contract] the client rejects " + hi +
                             " chars, which the server accepts - a legitimate wallet would be denied cloud save");
            if (GameStateService.IsCloudIdentityShaped(new string('A', hi + 1)))
                failures.Add("[server-contract] the client accepts " + (hi + 1) +
                             " chars but the server ceiling is " + hi + " - such a save would 401");

            // GUEST RAIL (added server-side by the api/ lane, 2026-08-02): the backend now
            // accepts ^guest-local-[0-9a-f]{64}$ as a deliberately second-class identity.
            // That shape is generated CLIENT-side by EnsureAccount, so if the client's
            // derivation ever changes (a different prefix, an uppercase hex digest, a
            // truncated hash) every guest silently loses the only rail they have. Pin the
            // agreement from the server's own literal.
            Match g = Regex.Match(js, @"/\^guest-local-\[0-9a-f\]\{(?<n>\d+)\}\$/");
            if (g.Success)
            {
                int hexLen = int.Parse(g.Groups["n"].Value);
                string clientPrefix = ReadClientGuestPrefix(failures);
                if (clientPrefix != null && clientPrefix != "guest-local-")
                    failures.Add("[server-contract] the client's guest prefix is '" + clientPrefix +
                                 "' but the server's guest rail matches 'guest-local-' - every guest " +
                                 "would be rejected as a bad player id");
                if (hexLen != 64)
                    failures.Add("[server-contract] the server's guest rail expects a " + hexLen +
                                 "-char hex digest; the client mints a SHA-256 (64) - the shapes disagree");
                notes.Add("guest rail contract confirmed (guest-local- + " + hexLen + " hex)");
            }
            else
            {
                notes.Add("no guest-rail regex found server-side - the guest rail may not be deployed yet");
            }

            notes.Add("server contract read live from " + from + " (" + lo + ".." + hi + " base58)");
        }

        /// <summary>The literal GuestWalletPrefix the client mints its device key with, or
        /// null when it cannot be located (recorded as a failure by the caller's reader).</summary>
        private static string ReadClientGuestPrefix(List<string> failures)
        {
            string src = ReadSource(StateSrc, failures);
            if (src == null) return null;
            Match m = Regex.Match(StripComments(src),
                                  @"GuestWalletPrefix\s*=\s*""(?<p>[^""]*)""");
            return m.Success ? m.Groups["p"].Value : null;
        }

        // =====================================================================
        //  CASE 3 - a stub identity is unmistakably a stub
        // =====================================================================
        private static void Case3_StubUnmistakable(List<string> failures, List<string> notes)
        {
            string src = ReadSource(StubSrc, failures);
            if (src == null) return;
            string code = StripComments(src);

            if (code.IndexOf("\"" + StubMarker + "\"", StringComparison.Ordinal) < 0)
                failures.Add("[stub-unmistakable] " + StubSrc + " no longer defines the \"" + StubMarker +
                             "\" marker - a stub address is minted as plain base58 again and is " +
                             "indistinguishable from a real wallet (P0-2 regressed)");

            if (!Regex.IsMatch(code, @"FakeAddressMarker\s*\+\s*RandomBase58"))
                failures.Add("[stub-unmistakable] GenerateDevnetAddress no longer prefixes the marker - " +
                             "the address is real-looking again (P0-2 regressed)");

            // The constant seed is the second half of P0-2: identical addresses ACROSS DEVICES.
            if (Regex.IsMatch(code, @"new\s+System\.Random\s*\(\s*0[xX][0-9A-Fa-f]+\s*\)") ||
                Regex.IsMatch(code, @"new\s+System\.Random\s*\(\s*\d+\s*\)"))
                failures.Add("[stub-unmistakable] the stub RNG is CONSTANT-seeded again - every device " +
                             "mints the same fake wallet, so logs, analytics and bug attribution all " +
                             "collapse onto one identity (P0-2 regressed)");

            if (!Regex.IsMatch(code, @"new\s+System\.Random\s*\(\s*Guid\.NewGuid\(\)\.GetHashCode\(\)\s*\)"))
                failures.Add("[stub-unmistakable] the stub RNG is no longer seeded per instance - " +
                             "uniqueness per device is not guaranteed");

            if (!Regex.IsMatch(code, @"CanSignMessages\s*=>\s*false"))
                failures.Add("[stub-unmistakable] StubWalletProvider.CanSignMessages is no longer a hard " +
                             "false - the stub could now satisfy the real-wallet attestation and key a " +
                             "cloud save, and could be treated as a signer on the real-value path");

            notes.Add("stub marker + per-instance seed + CanSignMessages=false all present");
        }

        // =====================================================================
        //  CASE 4 - only an attested real signing wallet keys a cloud save
        // =====================================================================
        private static void Case4_RealWalletGate(List<string> failures)
        {
            string wallet = StripComments(ReadSource(WalletSrc, failures) ?? string.Empty);
            string skin   = StripComments(ReadSource(SkinSrc, failures) ?? string.Empty);
            string state  = StripComments(ReadSource(StateSrc, failures) ?? string.Empty);

            // POSITIVE attestation: connected AND not the stub AND actually able to sign.
            if (!Regex.IsMatch(wallet, @"IsRealSigningWallet\s*=>[\s\S]{0,200}?IsConnected"))
                failures.Add("[real-wallet-gate] WalletService.IsRealSigningWallet no longer requires " +
                             "IsConnected");
            if (!Regex.IsMatch(wallet, @"IsRealSigningWallet\s*=>[\s\S]{0,200}?!\s*\(\s*_provider\s+is\s+StubWalletProvider\s*\)"))
                failures.Add("[real-wallet-gate] WalletService.IsRealSigningWallet no longer excludes " +
                             "StubWalletProvider - the devnet stub can be attested as a real wallet (P0-2)");
            if (!Regex.IsMatch(wallet, @"IsRealSigningWallet\s*=>[\s\S]{0,200}?_provider\.CanSignMessages"))
                failures.Add("[real-wallet-gate] WalletService.IsRealSigningWallet no longer requires " +
                             "CanSignMessages - a non-signing provider (or a DECORATOR wrapping the stub, " +
                             "which the type test alone would miss) could be attested");

            // The wallet login path must attest; nothing else may.
            if (!Regex.IsMatch(skin, @"BindWallet\s*\(\s*account\.Address\s*,\s*attested\s*\)"))
                failures.Add("[real-wallet-gate] WalletSkinBootstrap's login path no longer calls the " +
                             "ATTESTED BindWallet overload - a real wallet connect would leave the save " +
                             "local-only forever");
            if (!Regex.IsMatch(skin, @"attested\s*=\s*_wallet\.IsRealSigningWallet"))
                failures.Add("[real-wallet-gate] WalletSkinBootstrap derives its attestation from " +
                             "something other than IsRealSigningWallet");

            // The gate itself: shape AND attestation AND currency. Never the old denylist.
            if (!Regex.IsMatch(state, @"bool\s+IsRealWalletConnected\s*\([\s\S]{0,900}?IsCloudIdentityShaped"))
                failures.Add("[real-wallet-gate] IsRealWalletConnected no longer applies the shape allowlist");
            if (!Regex.IsMatch(state, @"bool\s+IsRealWalletConnected\s*\([\s\S]{0,900}?AttestedCloudIdentity"))
                failures.Add("[real-wallet-gate] IsRealWalletConnected no longer requires a provider " +
                             "attestation - it is back to trusting the SHAPE of a string, which is exactly " +
                             "how a stub address became a cloud identity (P0-2)");
            if (Regex.IsMatch(state, @"IsRealWalletConnected[\s\S]{0,400}?StartsWith\s*\(\s*GuestWalletPrefix"))
                failures.Add("[real-wallet-gate] IsRealWalletConnected is a DENYLIST again " +
                             "(\"not guest-local-*\") - anything that is not a guest key would cloud-sync");

            // The attested overload must refuse a mis-shaped address rather than trust the caller.
            if (!Regex.IsMatch(state, @"if\s*\(\s*attestedRealWallet\s*\)[\s\S]{0,400}?IsCloudIdentityShaped"))
                failures.Add("[real-wallet-gate] BindWallet's attested overload no longer re-checks the " +
                             "shape - a buggy provider could attest an arbitrary string");

            // Signature verification must stay strictly enforced (no guest shortcut).
            //
            // ⛔ RE-POINTED 2026-08-27, NOT RELAXED. The guarantee did not move an inch; the
            // FILE it lives in did. WO-1211 (dd73e4569) block-commented
            // GameStateService.TryAttachAuthHeaders as retired history - and StripComments
            // erases a /* ... */ block wholesale, so this case went red against an UNCHANGED
            // invariant while the dead method still sat in the file. The live authority is now
            // BackendRequestSigner.TryAttachAsync, whose guard is byte-for-byte the one that
            // used to be here (guest rail first - which PREDATES WO-1211 and was present and
            // passing at dd73e4569^ - then fail closed on no signer).
            //
            // Pinned in TWO places on purpose. The guard alone is not the invariant: a caller
            // that stopped consulting it, or ignored its false, would re-open exactly the hole
            // this case exists to close. So we assert the guard AND that the save rail aborts on it.
            string signerSrc = StripComments(ReadSource(SignerSrc, failures) ?? string.Empty);

            // Google Play has a separate, server-verified session rail. Remove only that
            // compile-time branch before proving the legacy wallet rail still fails closed;
            // then pin the exception independently so it cannot become a general bypass.
            string walletSignerSrc = Regex.Replace(
                signerSrc,
                @"#if\s+GOOGLE_PLAY[\s\S]*?#endif",
                string.Empty);
            if (!Regex.IsMatch(signerSrc,
                    @"#if\s+GOOGLE_PLAY[\s\S]{0,500}?IsGooglePlayIdentity\s*\(\s*playerId\s*\)" +
                    @"[\s\S]{0,300}?TryAttachCachedSession\s*\(\s*req\s*,\s*playerId\s*\)" +
                    @"[\s\S]{0,120}?return\s+true[\s\S]{0,500}?return\s+false[\s\S]{0,120}?#endif"))
                failures.Add("[real-wallet-gate] GOOGLE_PLAY identity exception is no longer tightly " +
                             "scoped to a verified cached session and fail-closed missing-session path");

            // ⚠ ANCHORED to the GUEST RAIL on purpose. MintSessionAsync carries a
            // character-identical `signer == null || !signer.CanSign` guard, so an
            // unanchored match would have gone on passing with TryAttachAsync's own guard
            // DELETED - proven by mutation before this was tightened. The guest rail exists
            // only in TryAttachAsync, so requiring guest-rail -> X-Guest-Id -> fail-closed IN
            // THAT ORDER pins both the guard AND the fact that the guest shortcut is the only
            // thing permitted to precede it.
            if (!Regex.IsMatch(walletSignerSrc,
                    @"IsGuestIdentity\s*\(\s*playerId\s*\)[\s\S]{0,200}?X-Guest-Id[\s\S]{0,300}?" +
                    @"signer\s*==\s*null\s*\|\|\s*!\s*signer\.CanSign" +
                    // ⚠ TEMPERED: the window may not contain a `return true`. A plain lazy
                    // [\s\S]{0,N}? happily skips PAST a fail-OPEN body to the next unrelated
                    // `return false` further down the method - which is how a guard rewritten
                    // to `if (no signer) return true;` was still passing. Proven by mutation.
                    @"(?:(?!return\s+true)[\s\S]){0,400}?return\s+false"))
                failures.Add("[real-wallet-gate] BackendRequestSigner.TryAttachAsync no longer FAILS CLOSED " +
                             "when there is no real signer - wallet-signature verification has been weakened");

            // The `if (` immediately before the `!` is load-bearing: it is what makes
            // `if (someEscapeHatch && !await ...)` fail this assertion instead of satisfying it.
            //
            // ⚠ THE ABORT IS NO LONGER SPELLED `return false` (WO-1587). SendCurrentSnapshot
            // returns a CATEGORISED SaveAttemptResult, because a bare bool erased the difference
            // between "no session" and "the server refused the payload" - which is how six 400s
            // were reported to the owner as an identity problem. THE INVARIANT IS UNCHANGED and
            // is still pinned structurally; only the token that spells the abort has moved.
            // `SaveAttemptCategory.AuthAbsent` IS the fail-closed refusal: its call sites re-queue
            // the marker and never send.
            //
            // ⚠ AND THE TEMPERING IS TIGHTENED, NOT RELAXED. The old window forbade an
            // intervening `return true` because a plain lazy match happily skipped PAST a
            // fail-OPEN body to an unrelated `return false` further down (proven by mutation).
            // The same hole now has three mouths, so all three are shut inside the window:
            //   * `return true`                 - the original fail-open
            //   * `SaveAttemptResult.Success`   - its successor spelling
            //   * `SaveAttemptCategory.Ok`      - the same thing written long-hand
            // `SendWebRequest` is forbidden in the window too, which is what proves the abort is
            // lexically BEFORE the request is ever sent: a guard that "aborts" after the POST has
            // gone out is not a guard.
            const string AuthAbortWindow =
                @"(?:(?!return\s+true)(?!SaveAttemptResult\.Success)(?!SaveAttemptCategory\.Ok)(?!SendWebRequest)[\s\S]){0,600}?";
            const string AuthAbortReturn =
                @"(?:return\s+false|return\s+new\s+SaveAttemptResult\s*\(\s*SaveAttemptCategory\.AuthAbsent)";
            if (!Regex.IsMatch(state, @"if\s*\(\s*!\s*await\s+[\w\.]*BackendRequestSigner\.TryAttachAsync\s*\(" +
                                      AuthAbortWindow + AuthAbortReturn))
                failures.Add("[real-wallet-gate] the cloud SAVE rail no longer ABORTS on a false from " +
                             "BackendRequestSigner.TryAttachAsync - a save could go out unauthed on the " +
                             "real rail even though the shared authority refused to vouch for it. The " +
                             "abort must sit inside the `if (!await ...TryAttachAsync(...))` guard, " +
                             "before any SendWebRequest, and read `return false` or " +
                             "`return new SaveAttemptResult(SaveAttemptCategory.AuthAbsent, ...)`");
        }

        // =====================================================================
        //  CASE 5 - the WALLET is the only identity path that exists
        // =====================================================================
        // WAS: "Firebase is ACCESS, never the save key" - it allowed the email/Google
        // paths to exist and only policed that they bound nothing (it REQUIRED the
        // NoteAccessGranted trace to still be there). Owner ruling 2026-08-21 removed
        // those paths outright ("we are only in the dApp Store, which is all wallet
        // authentication based"), so the check is now the stronger one: the login VM
        // must contain NO non-wallet identity path at all. P0-3 (a Firebase UID used as
        // the playerId) is unreachable by construction once nothing can produce one.
        private static void Case5_WalletIsTheOnlyIdentity(List<string> failures)
        {
            string src = ReadSource(LoginVmSrc, failures);
            if (src == null) return;
            string code = StripComments(src);

            foreach (var banned in new[] { "FirebaseAuthService", "SignUpAsync",
                                           "SendPasswordResetEmailAsync", "SignInWithGoogleCredentialAsync",
                                           "GoogleSignIn" })
            {
                if (code.IndexOf(banned, StringComparison.Ordinal) >= 0)
                    failures.Add("[wallet-only-identity] " + LoginVmSrc + " references '" + banned +
                                 "' - a non-wallet login path is back. The wallet is the sole identity " +
                                 "(WO-837-B); email/Google/Firebase were removed as player-facing paths.");
            }

            // BindPlayer must survive for the WALLET path (a wallet success must not continue unbound).
            if (!Regex.IsMatch(code, @"ConnectWalletAsync\([\s\S]{0,400}?BindPlayer\s*\("))
                failures.Add("[wallet-only-identity] the WALLET path no longer binds the connected address - " +
                             "a wallet sign-in would leave the save keyed to the guest id");

            // ...and BindPlayer must be reachable from NOWHERE else, so no future caller can
            // slip a non-wallet id into the save key.
            int binds = Regex.Matches(code, @"BindPlayer\s*\(").Count;
            if (binds > 2)   // the declaration + the one ConnectWalletAsync call site
                failures.Add("[wallet-only-identity] BindPlayer has more than one call site in " + LoginVmSrc +
                             " - only the wallet connect path may bind a save key (P0-3 guard)");
        }

        // =====================================================================
        //  CASE 6 - the escape hatch, and no unbounded await
        // =====================================================================
        private static void Case6_GuestEscape(List<string> failures, List<string> notes)
        {
            string src = ReadSource(LoginViewSrc, failures);
            if (src == null) return;
            string code = StripComments(src);

            // (a) SetBusy must not touch the guest button.
            Match setBusy = Regex.Match(code, @"void\s+SetBusy\s*\(\s*bool\s+busy\s*\)\s*\{(?<body>[\s\S]{0,900}?)\n\s*\}");
            if (!setBusy.Success)
                failures.Add("[guest-escape] could not locate SetBusy in " + LoginViewSrc +
                             " - this oracle can no longer see whether the escape hatch is disabled");
            else if (setBusy.Groups["body"].Value.IndexOf("_guest", StringComparison.Ordinal) >= 0)
                failures.Add("[guest-escape] SetBusy disables _guest again - a hung wallet/sign-in " +
                             "handshake leaves the tester on the login screen with EVERY control dead " +
                             "and no back button (P0-1 regressed)");

            // (b) The handler must not refuse the tap while busy.
            Match guestHandler = Regex.Match(code, @"void\s+OnPlayAsGuest\s*\(\s*\)\s*\{(?<body>[\s\S]{0,500}?)\n\s*\}");
            if (!guestHandler.Success)
                failures.Add("[guest-escape] could not locate OnPlayAsGuest in " + LoginViewSrc);
            else if (Regex.IsMatch(guestHandler.Groups["body"].Value, @"if\s*\([^)]*_busy"))
                failures.Add("[guest-escape] OnPlayAsGuest is gated on _busy again - the pending await is " +
                             "exactly the state the player is trying to escape, so the tap is swallowed " +
                             "(P0-1 regressed)");

            // (c) EVERY await on this surface is time-bounded. An unbounded one is the
            //     softlock, whether or not guest still works.
            int awaits = Regex.Matches(code, @"\bawait\b").Count;
            int bounded = Regex.Matches(code, @"\.Timeout\s*\(").Count;
            if (bounded < 1)
                failures.Add("[guest-escape] no Timeout(...) remains in " + LoginViewSrc +
                             " - the login surface can hang forever with no player-facing message (P0-1)");
            foreach (var handler in new[] { "OnConnectWallet", "OnSignIn", "OnCreateAccount",
                                            "OnGoogleSignIn", "OnForgotPassword", "PresentOrContinue" })
            {
                Match h = Regex.Match(code, @"\b" + handler + @"\s*\([^)]*\)\s*\{(?<body>[\s\S]{0,2000}?)\n\s{8}\}");
                if (!h.Success) continue;   // handler removed/renamed - not this case's business
                string body = h.Groups["body"].Value;
                if (body.IndexOf("await", StringComparison.Ordinal) < 0) continue;
                if (body.IndexOf(".Timeout(", StringComparison.Ordinal) < 0 &&
                    body.IndexOf("Bounded(", StringComparison.Ordinal) < 0)
                    failures.Add("[guest-escape] " + handler + " awaits WITHOUT a ceiling - on a bad " +
                                 "connection it never resolves and the surface stays busy with no honest " +
                                 "message (P0-1)");
            }

            // (d) The timeout must SAY something to the player, not just log.
            if (!Regex.IsMatch(code, @"catch\s*\(\s*TimeoutException[\s\S]{0,600}?SetStatus\s*\(") &&
                !Regex.IsMatch(code, @"catch\s*\(\s*TimeoutException[\s\S]{0,600}?AuthOutcome\.Fail\s*\("))
                failures.Add("[guest-escape] a timeout no longer produces a player-facing message - " +
                             "a silent recovery reads to the tester as the same freeze");

            notes.Add("login surface: " + awaits + " awaits, " + bounded + " explicit ceilings, guest never disabled");
        }

        // =====================================================================
        //  CASE 7 - a Settings bug report is attributable AND actionable
        // =====================================================================
        private static void Case7_BugAttribution(List<string> failures)
        {
            string bug = StripComments(ReadSource(BugVmSrc, failures) ?? string.Empty);
            string cap = StripComments(ReadSource(CaptureSrc, failures) ?? string.Empty);

            // IDENTITY: the report must carry the bound save key, and must not be blocked by it.
            // The source writes the JSON key ESCAPED - sb.Append(",\"playerId\":") - so the
            // text in the file is  playerId\":  and never  "playerId" .
            if (bug.IndexOf("playerId\\\":", StringComparison.Ordinal) < 0)
                failures.Add("[bug-attribution] BugReportVM no longer sends a playerId - a tester's bug " +
                             "lands in the db with no way to tell WHO hit it");
            if (!Regex.IsMatch(bug, @"PlayerIdKey\s*\(\)[\s\S]{0,600}?BoundWallet"))
                failures.Add("[bug-attribution] the report's identity is no longer the bound save key " +
                             "(GameState.BoundWallet) - it can no longer be joined to the player's save row");
            if (!Regex.IsMatch(bug, @"Guard\.Try\s*\(\s*""BugReport""\s*,\s*""player id key"""))
                failures.Add("[bug-attribution] reading the identity is no longer Guarded - a null state " +
                             "would throw and lose the whole report");

            // FAILURE MUST NOT BE SILENT.
            if (!Regex.IsMatch(bug, @"LastError\s*=") || !Regex.IsMatch(bug, @"Stage\.Failed"))
                failures.Add("[bug-attribution] a failed submit no longer surfaces an error state - the " +
                             "player would believe a lost report was sent");
            if (!Regex.IsMatch(bug, @"SaveLocalFallback\s*\("))
                failures.Add("[bug-attribution] the offline fallback copy is gone - a report that fails " +
                             "to POST is now lost outright");
            if (!Regex.IsMatch(bug, @"req\.timeout\s*=\s*\d+"))
                failures.Add("[bug-attribution] the bug-report POST has no timeout - a stalled submit " +
                             "hangs the form the same way the login surface used to hang");

            // STACK TRACE: the owner asked for it explicitly; the tail used to DISCARD it.
            if (!Regex.IsMatch(cap, @"static\s+string\s+FirstFrames\s*\(\s*string\s+stack\s*\)"))
                failures.Add("[bug-attribution] BreakCaptureHarness.FirstFrames is gone - the report tail " +
                             "is back to sending the exception MESSAGE with no call frames, which is the " +
                             "unactionable report the owner asked to stop receiving");
            if (!Regex.IsMatch(cap, @"OnTailLog[\s\S]{0,1600}?FirstFrames\s*\(\s*stack\s*\)"))
                failures.Add("[bug-attribution] OnTailLog discards its `stack` argument again - a bug " +
                             "report carries no stack trace");
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        /// <summary>Trace-safe rendering of an id under test - never paint a full key.</summary>
        private static string Describe(string id)
        {
            if (id == null) return "<null>";
            if (id.Length <= 12) return id;
            return id.Substring(0, 6) + "..." + id.Substring(id.Length - 4) + " (len=" + id.Length + ")";
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

        /// <summary>Reads a file that may legitimately be absent (the api/ lane may be split
        /// out of this checkout). Returns null instead of recording a failure.</summary>
        private static string ReadOptional(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        /// <summary>Strips // and /* */ comments so a lint can never be satisfied by prose.
        /// Every one of these files documents the defect it fixes in its own header, so
        /// linting raw text would pass on the comments alone.</summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", " ");
        }
    }
}
