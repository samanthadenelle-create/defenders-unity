// =============================================================================
// WalletConnectFailureAttributionRegression — WO-1420 + WO-1441 + WO-1583
// -----------------------------------------------------------------------------
// Two defects in one seam, both of which reported the WRONG CAUSE and so cost a
// triage its opening move. This oracle pins the corrections.
//
// WO-1420: WalletService.Connect wrapped the provider in UniTask.Timeout and then
// treated EVERY TimeoutException as "our 30s deadline expired". On device capture
// seq 4683 the connect failed in 0.4 s because the WALLET REFUSED — five handlers
// installed, one of which answered and closed the association endpoint 47 ms in —
// and the trace, the F8 capture and the player-facing LastConnectError all said
// "no wallet app installed, or the handshake was never answered". Only measured
// elapsed time can tell our deadline from the provider's refusal shape.
//
// WO-1441: BackendRequestSigner.WarmUpSessionAsync traced "first authenticated
// action will mint", which stopped being true at the WO-1157 fail-bounce. Nothing
// minted a session for a wallet holder who auto-resumed, so every cloud save was
// refused fail-closed with why=missing for the whole session.
//
// ⚠ WHY THIS IS A SOURCE ORACLE, NOT A PLAY-MODE TEST. Reproducing (b) needs a
// provider that throws TimeoutException from inside a real MWA association on a
// physical Seeker; there is no seam to inject one (ConnectTimeoutSeconds is a
// const and Connect builds its own Timeout). The structural facts below are what
// can actually be pinned from here, and they are the facts that regressed. The
// behavioural proof is the device felt-test named in the WO.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1420/WO-1441: a connect failure and a missing session must each name their real cause.</summary>
    public static class WalletConnectFailureAttributionRegression
    {
        private const string WalletServicePath = "Assets/_Modules/Wallet/WalletService.cs";
        private const string AssociationPath   = "Assets/_Modules/Wallet/TargetedLocalAssociationScenario.cs";
        private const string SignerPath        = "Assets/_Modules/Core/Backend/BackendRequestSigner.cs";
        private const string BootstrapPath     = "Assets/_Modules/Wallet/WalletSkinBootstrap.cs";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("WALLET_CONNECT_ATTRIBUTION_OK - " + reason);
            else Debug.LogError("WALLET_CONNECT_ATTRIBUTION_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                string service     = Strip(File.ReadAllText(WalletServicePath));
                string association = Strip(File.ReadAllText(AssociationPath));
                string signer      = Strip(File.ReadAllText(SignerPath));
                string bootstrap   = Strip(File.ReadAllText(BootstrapPath));

                // ── WO-1420 (1): the catch MEASURES and BRANCHES ────────────────────────
                // A catch that cannot distinguish the two causes will always report one of
                // them wrongly, so the elapsed measurement is the whole fix.
                if (!service.Contains("Time.realtimeSinceStartup"))
                    failures.Add("WalletService.Connect no longer measures elapsed connect time - a provider " +
                                 "refusal will be reported as our 30s timeout again (WO-1420)");
                if (!Regex.IsMatch(service, @"catch\s*\(\s*TimeoutException\s+\w+\s*\)"))
                    failures.Add("the TimeoutException catch dropped its exception binding - the provider's own " +
                                 "message can no longer be surfaced (WO-1420)");
                if (!service.Contains("Connect REFUSED by the wallet after"))
                    failures.Add("the REFUSED attribution is gone - every TimeoutException reads as our deadline again");
                if (!service.Contains("Connect TIMED OUT after"))
                    failures.Add("the genuine-deadline attribution is gone - a real 30s hang would be mislabelled a refusal");
                if (!service.Contains("Your wallet refused the connection."))
                    failures.Add("LastConnectError has no refusal copy - the player is told to wait for a wallet " +
                                 "that already answered (WO-1420)");
                // The branch must be on measured time, not on the exception type or a flag: those
                // are identical for both causes, which is how this shipped wrong the first time.
                if (!Regex.IsMatch(service, @"elapsed\s*>=\s*ConnectTimeoutSeconds"))
                    failures.Add("the timeout/refusal branch is no longer decided by measured elapsed vs the " +
                                 "deadline - nothing else can tell the two causes apart (WO-1420)");

                // ── WO-1420 (2): the association close is correlated in ONE line ────────
                if (!association.Contains("LastAssociationCloseUtc"))
                    failures.Add("the association close is no longer recorded - a triage must correlate two " +
                                 "threads by timestamp again (WO-1420 item 2)");
                if (!service.Contains("LastAssociationCloseUtc"))
                    failures.Add("WalletService no longer reads the association close - the cause is not named " +
                                 "in the failure line (WO-1420 item 2)");
                // Must sit OUTSIDE the class's #if SOLANA_SDK member guard: the READER compiles on
                // every target, so guarding the property breaks editor and desktop builds.
                // ⚠ ANCHOR AT THE CLASS, NOT AT THE FILE. The first "#if SOLANA_SDK" in this file is
                // the one around the SDK `using` block near the top, long before the class - so a
                // file-wide IndexOf reports EVERY member as guarded and fails the correct code.
                int classAt = association.IndexOf("class TargetedLocalAssociationScenario", StringComparison.Ordinal);
                int guardAt = classAt >= 0
                    ? association.IndexOf("#if SOLANA_SDK", classAt, StringComparison.Ordinal)
                    : -1;
                int propAt  = association.IndexOf("LastAssociationCloseUtc", StringComparison.Ordinal);
                if (guardAt >= 0 && propAt > guardAt)
                    failures.Add("LastAssociationCloseUtc moved inside #if SOLANA_SDK - editor and desktop " +
                                 "builds will not compile WalletService");

                // ── WO-1441: something must actually MINT the session ───────────────────
                if (!bootstrap.Contains("MintSessionForExplicitConnectAsync"))
                    failures.Add("no connect-time mint is wired - a wallet holder who never buys a pack has " +
                                 "no session and every cloud save refuses fail-closed (WO-1441)");

                // ── WO-1583 (owner ruling 2026-09-07): ...BUT NEVER AT BOOT ────────────
                // Owner, verbatim: "everytime i play now im forced to authenticate ... I would think
                // the authentication would only be needed for purchases (and codes)". WO-1441 wired
                // the mint onto BOTH connect paths, so auto-resume raised a wallet SignMessage sheet
                // on every launch. The mint stays (the line above); what changed is WHO may reach it.
                // ⚠ THE TWO PINS ABOVE AND BELOW ARE IN TENSION ON PURPOSE - a mint must exist and
                // must NOT be reachable from boot. Satisfying either alone is a shipped defect.
                if (!bootstrap.Contains("explicitConnect"))
                    failures.Add("the boot-vs-tap distinction is gone from WalletSkinBootstrap - auto-resume " +
                                 "and the player's Connect tap share one body, so boot signs again on every " +
                                 "launch (WO-1583)");
                if (!bootstrap.Contains("TryResumeSessionWithoutSigningAsync"))
                    failures.Add("boot no longer takes the signature-free resume - it must reuse or renew a " +
                                 "session, never mint one (WO-1583)");
                if (!signer.Contains("boot never signs (ruling 2026-09-07)"))
                    failures.Add("the boot trace no longer says IN WORDS why no wallet sheet was shown - the " +
                                 "owner's next device log cannot prove the ruling is in effect (WO-1583)");
                if (signer.Contains("first authenticated action will mint"))
                    failures.Add("the retired claim that any authed call mints is back in BackendRequestSigner - " +
                                 "cloud SAVE passes allowMint:false and cannot mint (WO-1441)");

                // ⛔ THE 15-MINUTE CLIFF. A mint alone is not a fix: the server TTL is 900s and the
                // save route may not raise SignMessage, so without a signature-free renewal `why`
                // flips missing -> expired a quarter hour in and cloud save dies again mid-session.
                if (!signer.Contains("TryRenewSessionAsync"))
                    failures.Add("the signature-free session renewal is gone - cloud save will die 15 minutes " +
                                 "after the handshake and nothing will re-mint it (WO-1441)");
                if (!Regex.IsMatch(signer, "why\\s*==\\s*\"expired\""))
                    failures.Add("renewal is no longer triggered by an expired session - the renewal exists but " +
                                 "is never reached (WO-1441)");

                // The caller token must survive async, or every trace names this file instead of
                // the real caller - which is exactly what hid this bug's origin.
                if (!signer.Contains("MethodNameFromGeneratedType"))
                    failures.Add("FirstExternalFrame no longer unwraps async state machines - caller= will read " +
                                 "<TryAttachSession>d__N.MoveNext again instead of the real caller (WO-1441)");
            }
            catch (Exception ex) { failures.Add("oracle threw " + ex.GetType().Name + ": " + ex.Message); }

            reason = failures.Count == 0
                ? "connect failures name refusal vs deadline from measured time, the association close is " +
                  "correlated in one line, and an explicit connect mints the backend session"
                : string.Join(" | ", failures.ToArray());
            return failures.Count == 0;
        }

        /// <summary>
        /// Removes comments so a lint reads code, not prose.
        /// <para>
        /// ⛔ LINE COMMENTS FIRST — see the long note on BackendSaveAuthRegression.Strip. Block-first
        /// lets a <c>//</c> comment containing a star-slash sequence open a false block comment that
        /// runs to the next real terminator; on 2026-09-06 that deleted 73% of the file under test
        /// and made this very suite report the renewal as MISSING when it was present and wired.
        /// </para>
        /// </summary>
        private static string Strip(string source)
        {
            source = Regex.Replace(source, @"//[^\r\n]*", "");
            return Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        }
    }
}
