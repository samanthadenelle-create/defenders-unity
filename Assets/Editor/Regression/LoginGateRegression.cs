// =============================================================================
// LoginGateRegression [login-gate] -- the boot login surface must NEVER be shown
// to a player who is already in, and must ALWAYS be shown to one who is not.
// -----------------------------------------------------------------------------
// THE DEFECT (owner-captured on a real Seeker, 2026-08-18, one launch):
//   20:21:38.592  [Flow:Wallet] SolanaWalletProvider RESUMED silently - CHKK...sfkC
//   20:21:38.601  [Flow:Wallet] Login connect bound save identity to wallet CHKK...sfkC
//   20:21:38.602  [Flow:Wallet] auto-resume SUCCEEDED - connected at boot, no player action
//   20:21:43.478  [Flow:Auth]   LoginPanelController.Build  <-- SIGN IN presented anyway
// The gate read ONE source, FirebaseAuthService.IsSignedIn, on a build whose data
// identity is the WALLET (LoginPanelController's identity law: "Firebase = ACCESS,
// wallet = DATA identity"). A wallet-only player is never Firebase-signed-in, so the
// gate re-prompted a connected, bound session. NOT a race: the decision ran ~5s AFTER
// the connect published - it was a WRONG SOURCE.
//
// What this oracle pins:
//   1. The decision seam LoginPanelController.ShouldContinueWithoutLogin exists and its
//      full truth table holds: PRESENT only when all three inputs are false. In
//      particular walletConnected alone => CONTINUE (the exact captured case), and
//      walletIdentityBound alone => CONTINUE (the returning player whose silent resume
//      has not landed yet - the race-proof half, which is why no delay was added).
//   2. FIRST RUN STILL PRESENTS: all inputs false => PRESENT.
//   3. GameStateService.HasAttestedWalletIdentity, the race-proof input, is honest at
//      runtime: guest key => false, wallet-shaped but UNATTESTED => false (a string may
//      not talk its way in), wallet-shaped AND attested on this device => true.
//   4. Source-scan of PresentOrContinue: it still samples BOTH wallet inputs and still
//      emits the decision trace with its inputs (INSTRUMENTATION_STANDARD 1.4b) -- a
//      later "tidy" that drops either one puts the SIGN IN wall back in front of the
//      owner with no line saying why.
//   5. WO-1249 PRODUCTION BOOT ROUTE (owner 2026-08-27: tester APK must behave like
//      production; no tester skip). The gate file has no tester-define branch, never
//      logs a wallet address, and the founding chokepoint still calls PresentOrContinue.
//      RED-first: the unpatched gate logged ConnectedWalletShortAddress in the decision
//      Step and titled the panel "SIGN IN"; both needles fail the pins below.
//
// LoginPanelController lives in DeNelle.Onboarding, which this asmdef does not
// reference; the seam is driven by reflection, exactly like FoundingReachabilityRegression.
//
// Marker: LOGIN_GATE_OK / LOGIN_GATE_FAIL. Expected: GREEN.
// Wire (DataRegression.RunAll): [login-gate].
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Editor.Regression
{
    public static class LoginGateRegression
    {
        // The per-device attestation slot (GameStateService.AttestedIdentityKey). Duplicated as a
        // literal on purpose: the field is private, and an oracle that read it through the code
        // under test could not notice the key being renamed out from under a live player.
        private const string AttestedPrefsKey = "dotr-cloud-identity-attested";

        // Base58, 44 chars - passes GameStateService.IsCloudIdentityShaped.
        private const string RealWallet = "BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV";
        private const string GuestKey =
            "guest-local-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- LOGIN GATE (already-in => CONTINUE, fresh => PRESENT) ---");

            // -- 1/2. The pure decision seam ----------------------------------
            var lpcType = FindType("DeNelle.Onboarding.LoginPanelController");
            if (lpcType == null)
            {
                failures.Add("[login-gate] LoginPanelController type not found (DeNelle.Onboarding not compiled?)");
                reason = Finish(failures, log);
                return failures.Count == 0;
            }

            var decide = lpcType.GetMethod("ShouldContinueWithoutLogin",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
            if (decide == null)
            {
                failures.Add("[login-gate] LoginPanelController.ShouldContinueWithoutLogin(bool,bool,bool) is gone -- " +
                             "the boot gate no longer has a decision seam anyone can test, which is how the " +
                             "2026-08-18 re-prompt shipped in the first place");
                reason = Finish(failures, log);
                return failures.Count == 0;
            }

            // walletConnected, walletIdentityBound, firebaseSignedIn -> expected CONTINUE?
            var table = new (bool wc, bool wb, bool fb, bool want, string what)[]
            {
                (false, false, false, false, "FIRST RUN (nothing connected, nothing bound, not signed in)"),
                (true,  false, false, true,  "wallet connected this boot (the owner's captured case)"),
                (false, true,  false, true,  "returning wallet player, silent resume not landed yet"),
                (false, false, true,  true,  "cached Firebase session (the original, still-valid case)"),
                (true,  true,  false, true,  "connected AND bound"),
                (true,  true,  true,  true,  "everything true"),
            };
            foreach (var row in table)
            {
                bool got;
                try { got = (bool)decide.Invoke(null, new object[] { row.wc, row.wb, row.fb }); }
                catch (Exception ex)
                {
                    failures.Add($"[login-gate] decision seam threw on '{row.what}': {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                string gotWord = got ? "CONTINUE" : "PRESENT";
                string wantWord = row.want ? "CONTINUE" : "PRESENT";
                log.AppendLine($"  ({row.wc},{row.wb},{row.fb}) -> {gotWord} (want {wantWord}) : {row.what}");
                if (got != row.want)
                {
                    failures.Add(row.want
                        ? $"[login-gate] the gate would PRESENT the SIGN IN wall to a player who is already in -- {row.what}"
                        : $"[login-gate] the gate would SKIP the login surface for {row.what} -- a genuine first run " +
                          "must still be able to connect a wallet or choose guest");
                }
            }

            // -- 3. The race-proof input is honest at runtime ------------------
            CheckAttestedIdentity(failures, log);

            // -- 4. The wiring + the decision trace ---------------------------
            CheckSource(failures, log);

            // -- 5. WO-1249: production boot route, no tester variant ---------
            CheckProductionBootRoute(failures, log);

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        /// <summary>
        /// GameStateService.HasAttestedWalletIdentity over a throwaway state: guest => false,
        /// wallet-shaped-but-unattested => false, wallet-shaped + attested => true. Snapshots and
        /// restores both the attestation slot and the live service instance.
        /// </summary>
        private static void CheckAttestedIdentity(List<string> failures, StringBuilder log)
        {
            var prop = typeof(GameStateService).GetProperty("HasAttestedWalletIdentity",
                BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                failures.Add("[login-gate] GameStateService.HasAttestedWalletIdentity is gone -- the login gate's " +
                             "race-proof input (a returning wallet player, before the silent resume lands) is unreadable");
                return;
            }

            bool hadAttested = PlayerPrefs.HasKey(AttestedPrefsKey);
            string priorAttested = hadAttested ? PlayerPrefs.GetString(AttestedPrefsKey, string.Empty) : string.Empty;
            GameStateService priorGss = GameStateService.Instance;
            GameObject gssGo = null;
            GameState throwaway = null;
            bool ownsThrowaway = false;
            try
            {
                gssGo = new GameObject("GSS (login-gate oracle)");
                var gss = gssGo.AddComponent<GameStateService>();

                // HOLLOW-PASS FIX 2026-08-18: this used to log "(skipped ...)" and RETURN when the
                // private field was not reflectable, so a rename of _state turned the entire
                // attestation oracle -- checks 3a/3b/3c below, the race-proof input the 2026-08-18
                // re-prompt hinged on -- into a silent GREEN. A suite that asserts nothing when its
                // fixture will not install is decoration (INSTRUMENTATION_STANDARD 1.4b).
                // The state is now INSTALLED, not skipped: the private field is the fast path, and
                // GameStateService.Awake already creates a fresh GameState when none is assigned,
                // so the PUBLIC State seam installs one even if _state is renamed. If neither
                // yields a state there is no fixture at all -- that is a FAILURE, never a pass.
                var stateField = typeof(GameStateService).GetField("_state",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (stateField != null)
                {
                    throwaway = ScriptableObject.CreateInstance<GameState>();
                    ownsThrowaway = true;
                    stateField.SetValue(gss, throwaway);
                }
                else
                {
                    throwaway = gss.State;
                    log.AppendLine("  (GameStateService._state not reflectable -- installed via the public State seam)");
                }

                if (throwaway == null)
                {
                    failures.Add("[login-gate] no GameState could be installed on a live GameStateService " +
                                 "(neither the private _state field nor the public State seam yielded one) -- " +
                                 "HasAttestedWalletIdentity is UNTESTED and the 2026-08-18 defect is unguarded");
                    return;
                }

                // Guest device-hash key: never a cloud identity, so never a reason to skip login.
                PlayerPrefs.DeleteKey(AttestedPrefsKey);
                throwaway.BoundWallet = GuestKey;
                bool guest = (bool)prop.GetValue(gss);
                log.AppendLine($"  guest-local key -> HasAttestedWalletIdentity={guest} (want false)");
                if (guest)
                    failures.Add("[login-gate] a guest-local save key reports an attested wallet identity -- the gate " +
                                 "would skip login for a player who has never connected a wallet");

                // Wallet-shaped but NOT attested on this device: still false (allowlist, not denylist).
                throwaway.BoundWallet = RealWallet;
                bool unattested = (bool)prop.GetValue(gss);
                log.AppendLine($"  wallet-shaped, UNATTESTED -> HasAttestedWalletIdentity={unattested} (want false)");
                if (unattested)
                    failures.Add("[login-gate] a wallet-SHAPED but unattested save key reports an attested identity -- " +
                                 "a copied save could skip the login surface and read as a connected wallet");

                // Attested on this device by a real signing wallet: the returning player.
                PlayerPrefs.SetString(AttestedPrefsKey, RealWallet);
                bool attested = (bool)prop.GetValue(gss);
                log.AppendLine($"  wallet-shaped, ATTESTED -> HasAttestedWalletIdentity={attested} (want true)");
                if (!attested)
                    failures.Add("[login-gate] an attested wallet-bound save does NOT report an attested identity -- " +
                                 "the returning player is re-prompted to sign in on every launch (the 2026-08-18 defect)");
            }
            catch (Exception ex)
            {
                failures.Add($"[login-gate] attestation oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                // Only destroy the state THIS oracle created; the public-seam fallback hands back
                // the service's own instance, which goes away with the GameObject above.
                if (ownsThrowaway && throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                var instField = typeof(GameStateService).GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (instField != null) instField.SetValue(null, priorGss);
                if (hadAttested) PlayerPrefs.SetString(AttestedPrefsKey, priorAttested);
                else PlayerPrefs.DeleteKey(AttestedPrefsKey);
                PlayerPrefs.Save();
            }
        }

        private static void CheckSource(List<string> failures, StringBuilder log)
        {
            string path = Path.Combine(Application.dataPath, "_Modules/Onboarding/LoginPanelController.cs");
            if (!File.Exists(path)) { failures.Add("[login-gate] LoginPanelController.cs not found"); return; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add($"[login-gate] LoginPanelController.cs unreadable ({ex.Message})"); return; }

            Require(text, "CurrencySkinResolver.IsWalletConnected", failures, log,
                "the gate no longer samples the LIVE wallet connection -- a wallet that connects at boot " +
                "would be shown the SIGN IN wall again");
            Require(text, "HasAttestedWalletIdentity", failures, log,
                "the gate no longer samples the PERSISTED wallet identity -- the boot resume race comes back, " +
                "and the only fixes left are timing hacks");
            Require(text, "login gate decision=", failures, log,
                "the gate's decision trace is gone -- the next wrong outcome is invisible in a device capture " +
                "(INSTRUMENTATION_STANDARD 1.4b: report the decision AND its inputs)");
        }

        /// <summary>
        /// WO-1249. The Unity login panel is the production one-time connect. A tester
        /// APK that skipped it could not validate production, so there is no tester
        /// variant to assert -- the same seam, the same copy, no address in the log.
        /// </summary>
        private static void CheckProductionBootRoute(List<string> failures, StringBuilder log)
        {
            log.AppendLine("--- LOGIN GATE production boot route (WO-1249; no tester variant) ---");

            string loginPath = Path.Combine(Application.dataPath, "_Modules/Onboarding/LoginPanelController.cs");
            if (!File.Exists(loginPath))
            {
                failures.Add("[login-gate] LoginPanelController.cs not found (production boot-route pin)");
                return;
            }
            string login;
            try { login = File.ReadAllText(loginPath); }
            catch (Exception ex)
            {
                failures.Add("[login-gate] LoginPanelController.cs unreadable for boot-route pin (" + ex.Message + ")");
                return;
            }

            // RED-first (WO-1138): the unpatched gate logged ConnectedWalletShortAddress in
            // the decision Step. A later "helpful" log of the pubkey puts a wallet address
            // back into Player.log -- forbidden (WO-1249 acceptance: never log a wallet
            // address; player id is enough, and here the booleans are enough).
            Forbid(login, "ConnectedWalletShortAddress", failures, log,
                "the login-gate decision logs a wallet address -- WO-1249 forbids rendering or logging one");

            Forbid(login, "#if TESTER_BUILD", failures, log,
                "the login gate branches on a tester define -- owner ruling 2026-08-27: tester APK must behave like production");
            Forbid(login, "IsTesterBuild", failures, log,
                "the login gate reads FeatureFlags.IsTesterBuild -- that is the tester skip the owner ruled out");

            // RED-first: the unpatched panel title was the email-era "SIGN IN" wall. The
            // production first-run surface is the one-time wallet connect, labelled as such.
            // WO-1857: both captions now resolve through LocalizedText, so these needles pin the
            // resolved KEYS. The keys' authored English still carries the same promise; the retired
            // literals are deliberately not reproduced in this comment, because a source-text needle
            // that matches its own comment is green while asserting nothing.
            Require(login, "\"onboarding.login_panel.title_wallet\"", failures, log,
                "the first-run panel title no longer resolves onboarding.login_panel.title_wallet -- a SIGN IN wall reads as a bug, not the one-time connect");
            Require(login, "\"onboarding.login_panel.intro_wallet\"", failures, log,
                "the first-run copy no longer resolves onboarding.login_panel.intro_wallet -- the owner reported the missing one-time-connect promise as a validate-wallet defect");

            string foundingPath = Path.Combine(Application.dataPath, "_Modules/Onboarding/FoundingChoiceController.cs");
            if (!File.Exists(foundingPath))
            {
                failures.Add("[login-gate] FoundingChoiceController.cs not found -- the production boot chokepoint is unreadable");
                return;
            }
            string founding;
            try { founding = File.ReadAllText(foundingPath); }
            catch (Exception ex)
            {
                failures.Add("[login-gate] FoundingChoiceController.cs unreadable (" + ex.Message + ")");
                return;
            }
            Require(founding, "LoginPanelController.PresentOrContinue", failures, log,
                "the founding chokepoint no longer routes through the login gate -- first-run players would skip the production connect, or a later caller would re-prompt returning players");
        }

        private static void Require(string text, string needle, List<string> failures, StringBuilder log, string why)
        {
            if (text.IndexOf(needle, StringComparison.Ordinal) < 0)
                failures.Add($"[login-gate] source no longer contains '{needle}' -- {why}");
            else
                log.AppendLine($"  source references '{needle}' OK");
        }

        private static void Forbid(string text, string needle, List<string> failures, StringBuilder log, string why)
        {
            if (text.IndexOf(needle, StringComparison.Ordinal) >= 0)
                failures.Add($"[login-gate] source contains '{needle}' -- {why}");
            else
                log.AppendLine($"  source does not contain '{needle}' OK");
        }

        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(full, false);
                if (t != null) return t;
            }
            return null;
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "LOGIN_GATE_OK");
                return "LOGIN GATE OK -- connected/bound/signed-in continue straight in, a fresh install still presents";
            }
            string reason = "login-gate: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "LOGIN_GATE_FAIL: " + reason);
            return reason;
        }
    }
}
