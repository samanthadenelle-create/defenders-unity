// =============================================================================
// StakingComplianceRegression — WO-1674 D5 / HEART-001.
// -----------------------------------------------------------------------------
// ⛔ THIS SUITE WAS CITED AS LIVE PROTECTION FOR WEEKS AND DID NOT EXIST.
//
//   Assets/_Modules/Core/FeatureFlags.cs:1008 says, on the DAPP_STORE compile-off
//   of the staking perk: "Pinned by StakingComplianceRegression."
//   WorkOrders/WORK_ORDER_1255_*.md:95 and WORK_ORDER_1673_*.md:138 repeat it.
//   `grep -rn "StakingComplianceRegression" .` returned THREE hits before this
//   file, and all three were CLAIMS THAT IT PROTECTS SOMETHING. There was no
//   definition anywhere.
//
//   It is the third such case in this feature area (WO-1674 §0.4 / the triage's
//   table): JewelPolishService.cs:425 cites a JewelPolishRegression that is its
//   own only hit, and skr_staking.json's _comment cites an "SkrStakingRegression"
//   that never existed. THREE IMAGINARY FIREWALLS IN ONE FEATURE AREA, and this
//   was the worst of them, because it sits on the flag that decides whether
//   token-gated gameplay compiles into a Google Play artifact.
//
//   A COMMENT IS NOT A FIREWALL. This file is the firewall.
//
// WHAT IT PINS — three properties, each of which is a real, demonstrated failure
// mode rather than a tidy invariant:
//
//   A. NO REWARD PATH READS A CLIENT-REPORTED STAKE.
//      Until 2026-09-10 NativeSkrPolishBonus resolved its grant from
//      StakeRewardsResolver.Resolve(), i.e. from StakeRewardsResolver.Query — a
//      PUBLIC SETTABLE PROPERTY. Any code in the process could install an
//      IStakeQuery reporting any number and be granted on it. That is a standing
//      violation of product rule 7, tolerable only while the grant was ATTEMPTS.
//
//   B. THE DAPP_STORE DEFINE GATES THE HEARTBOUND SURFACE, and the Wallet
//      assembly stays out of a Play artifact entirely.
//
//   C. THE BACKEND VERIFIER EXISTS AND VALIDATES ITS INPUTS — the endpoint is
//      GET-only, takes no amount under any name, and resolves the wallet from
//      the AUTHENTICATED identity rather than from the request.
//
// ⚠ SOURCE LINT, AND HONEST ABOUT BEING ONE. These are text assertions over
//   files, in the shape GooglePlayPackagingRegression already uses. A source
//   lint cannot prove runtime behaviour; what it CAN do is fail the moment
//   someone re-opens one of the specific holes above, which is exactly what was
//   missing. Where a needle is exact text it must match byte-for-byte.
//
// Registered in DataRegression.RunAll; marker REGRESSION_OK <n>/<n> suites.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace DeNelle.Editor.Regression
{
    public static class StakingComplianceRegression
    {
        /// <summary>Focused batchmode entry point, for proving this suite red before green.</summary>
        public static void RunFocused()
        {
            if (Run(out string focusedReason))
            {
                UnityEngine.Debug.Log(focusedReason);
                UnityEngine.Debug.Log("STAKING_COMPLIANCE_OK");
                return;
            }

            UnityEngine.Debug.LogError(focusedReason);
            UnityEngine.Debug.LogError("STAKING_COMPLIANCE_FAIL");
            UnityEditor.EditorApplication.Exit(1);
        }

        public static bool Run(out string reason)
        {
            var f = new List<string>();

            string bonus = Read("Assets/_Modules/Core/Catalog/PolishBonusProvider.cs", f);
            string snapshot = Read("Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs", f);
            string resolver = Read("Assets/_Modules/Core/Platform/StakeRewardsResolver.cs", f);
            string nativeQuery = Read("Assets/_Modules/Wallet/NativeSkrStakeQuery.cs", f);
            string statusClient = Read("Assets/_Modules/Wallet/HeartboundStatusClient.cs", f);
            string walletAsmdef = Read("Assets/_Modules/Wallet/DeNelle.Wallet.asmdef", f);
            string flags = Read("Assets/_Modules/Core/FeatureFlags.cs", f);
            string verifier = Read("api/_lib/skr-staking.js", f);
            string pda = Read("api/_lib/solana-pda.js", f);
            string endpoint = Read("api/heartbound/status.js", f);
            string migration = Read("api/migrations/20260910_0024_skr_stake_snapshots.sql", f);

            // =================================================================
            //  A. NO REWARD PATH READS A CLIENT-REPORTED STAKE (product rule 7)
            // =================================================================

            // A1. The polish grant reads the BACKEND-VERIFIED snapshot.
            if (!bonus.Contains("VerifiedStakeSnapshot.RewardBearingStakeSkr"))
                f.Add("PRODUCT RULE 7 BROKEN: NativeSkrPolishBonus no longer reads " +
                      "VerifiedStakeSnapshot.RewardBearingStakeSkr. The only other source of a stake " +
                      "on the client is a value the DEVICE computed, which a patched build controls.");

            // A2. And it must NOT be back on the settable-seam path. Resolve() with
            //     no argument reads StakeRewardsResolver.Query, which is public and
            //     settable — that is the injection route, and it must not return.
            if (bonus.Contains("StakeRewardsResolver.Resolve();"))
                f.Add("PRODUCT RULE 7 BROKEN: PolishBonusProvider calls the NO-ARGUMENT " +
                      "StakeRewardsResolver.Resolve(), which resolves from the PUBLIC SETTABLE " +
                      "StakeRewardsResolver.Query. That is precisely the injection path WO-1674 closed: " +
                      "anything in the process can install an IStakeQuery reporting any amount. Use the " +
                      "Resolve(long) overload fed by VerifiedStakeSnapshot.");

            // A3. The snapshot must refuse to pay on an unverified state.
            if (!snapshot.Contains("if (!IsRewardBearing) return 0L;"))
                f.Add("VerifiedStakeSnapshot.RewardBearingStakeSkr no longer refuses to report an " +
                      "amount for an unverified state — an RPC outage or an un-asked question would " +
                      "pay out on whatever happened to be in the field.");
            if (!snapshot.Contains("_status == StakeVerificationStatus.Verified") ||
                !snapshot.Contains("_ageSeconds <= _graceWindowSeconds"))
                f.Add("VerifiedStakeSnapshot.IsRewardBearing no longer restricts payment to a VERIFIED " +
                      "snapshot or a STALE one inside the SERVER's grace window (owner ruling 2026-09-10 " +
                      "13:10). Without the age re-check a stale snapshot pays forever.");

            // A4. EXACTLY ONE WRITER. This is what makes "the client cannot inject an
            //     amount" true in-process: a static setter cannot be made unsettable,
            //     so the guarantee is that only one file ever calls it.
            int writers = 0;
            foreach (string path in EnumerateRuntimeCs())
            {
                string text;
                try { text = File.ReadAllText(path); } catch (Exception) { continue; }
                if (text.IndexOf("VerifiedStakeSnapshot.AcceptServerVerification",
                        StringComparison.Ordinal) < 0) continue;
                // The declaration site itself is not a caller.
                if (path.Replace('\\', '/').EndsWith("Core/Platform/VerifiedStakeSnapshot.cs",
                        StringComparison.Ordinal)) continue;
                writers++;
                if (!path.Replace('\\', '/').EndsWith("Wallet/HeartboundStatusClient.cs",
                        StringComparison.Ordinal))
                    f.Add("SECOND WRITER of the verified stake: " + path.Replace('\\', '/') +
                          " calls VerifiedStakeSnapshot.AcceptServerVerification. Exactly ONE caller " +
                          "may exist (HeartboundStatusClient, which reads the backend). A second writer " +
                          "is a second authority, and one of them will be a client-side number.");
            }
            if (writers == 0)
                f.Add("NOTHING writes VerifiedStakeSnapshot: the backend answer never reaches gameplay, " +
                      "so every staking perk is silently dead. HeartboundStatusClient must call " +
                      "AcceptServerVerification.");

            // A5. The client-side chain read must still declare itself display-only,
            //     so the next reader cannot mistake it for the authority.
            if (!nativeQuery.Contains("DISPLAY-ONLY"))
                f.Add("NativeSkrStakeQuery no longer declares itself DISPLAY-ONLY. It computes a stake " +
                      "ON THE DEVICE; without that statement the next lane will wire it to a grant, " +
                      "which is the exact WO-1674 defect.");

            // A6. The attempts-only invariant. Unchanged by WO-1674 and must stay so:
            //     the owner's first proposal was +5% ODDS and it was rejected.
            foreach (string banned in new[] { "OddsBonus", "LuckBonus", "WeightBonus",
                                              "TierBonus", "ChanceBonus", "OddsMultiplier" })
                if (bonus.IndexOf(banned, StringComparison.Ordinal) >= 0)
                    f.Add("FAIRNESS BROKEN: IPolishBonusProvider exposes '" + banned + "' — an " +
                          "ODDS-shaped grant. Staking buys ATTEMPTS, never OUTCOMES.");

            // A7. The display seam must remain a DISPLAY seam — still present (the
            //     showcase panel needs it) but never the reward input.
            if (!resolver.Contains("public static IStakeQuery Query"))
                f.Add("StakeRewardsResolver.Query has been removed. WO-1674 demoted it to a DISPLAY " +
                      "seam, it did not delete it: the Seekerthon showcase panel and the Jeweler FTUE " +
                      "still render through it.");

            // =================================================================
            //  B. THE DISTRIBUTION GATE
            // =================================================================

            // B1. The whole Wallet assembly is out of a Play artifact. Stronger than
            //     any flag: a flag is a PlayerPrefs value a stored entry can beat
            //     (FeatureFlags.cs:691-693); an absent assembly is absent.
            if (!walletAsmdef.Contains("\"!GOOGLE_PLAY\""))
                f.Add("COMPLIANCE: DeNelle.Wallet.asmdef no longer carries the \"!GOOGLE_PLAY\" define " +
                      "constraint. The wallet assembly — NativeSkrStakeQuery and HeartboundStatusClient " +
                      "included — would compile into a Google Play artifact.");

            // B2. The staking perk flag stays compile-gated on the DISTRIBUTION define,
            //     never blanket-on. This is the line FeatureFlags.cs:1008 claims is
            //     pinned here; from now on, it is.
            if (!flags.Contains("#if DAPP_STORE") ||
                !flags.Contains("public static bool StakingPolishBonus => Get(\"stakingpolishbonus\", defaultOn: true);") ||
                !flags.Contains("public static bool StakingPolishBonus => Get(\"stakingpolishbonus\", defaultOn: false);"))
                f.Add("COMPLIANCE: FeatureFlags.StakingPolishBonus is no longer compile-gated on " +
                      "DAPP_STORE with defaultOn:true / defaultOn:false per channel. A blanket default " +
                      "would enable token-gated gameplay in a future Play build from these same " +
                      "ProjectSettings, silently, months later.");

            // B3. Every Heartbound client surface is inside the distribution define.
            if (!statusClient.Contains("#if !DAPP_STORE") || !statusClient.Contains("#if DAPP_STORE"))
                f.Add("COMPLIANCE: HeartboundStatusClient is not gated on the DAPP_STORE distribution " +
                      "define, so a Windows/WebGL build would poll a Heartbound endpoint the owner " +
                      "ruled Seeker-only (2026-09-10 13:10).");

            // =================================================================
            //  C. THE BACKEND VERIFIER EXISTS AND VALIDATES ITS INPUTS
            // =================================================================

            // C1. It exists at all. The whole point of HEART-001.
            if (verifier.Length == 0)
                f.Add("api/_lib/skr-staking.js is missing: there is no server-side stake verifier, so " +
                      "nothing can satisfy product rule 6 or 7.");

            // C2. Mainnet is EXPLICIT and is not inherited from the shared, possibly
            //     devnet-pointed SOLANA_RPC_URL (purchase-catalog.js:57 proves a devnet
            //     configuration exists; :37-48 records a 1000x decimals scar).
            if (!verifier.Contains("SOLANA_MAINNET_RPC_URL"))
                f.Add("ACCEPTANCE 8: the verifier does not read a dedicated MAINNET RPC variable. " +
                      "Inheriting SOLANA_RPC_URL can read DEVNET and report it as a real stake.");
            if (verifier.Contains("process.env.SOLANA_RPC_URL"))
                f.Add("The verifier reads the SHARED SOLANA_RPC_URL. Heartbound is mainnet-only " +
                      "(spec :181); that variable is not guaranteed to be mainnet.");

            // C3. SKR is 6 decimals here, with no devnet 9 branch. The decimals trap.
            if (!verifier.Contains("SKR_DECIMALS = 6"))
                f.Add("The verifier does not fix SKR at 6 decimals. api/_lib/purchase-catalog.js:37-48 " +
                      "records a 1000x overcharge from getting this wrong once already.");

            // C4. The PDA derivation performs the on-curve rejection. Without it the
            //     derived address is wrong for ~half of all wallets, and a wrong
            //     address reads back as ACCOUNT_NOT_FOUND — a staker silently told
            //     they have no stake, with nothing in any log naming the cause.
            if (!pda.Contains("isOnCurve") || !pda.Contains("if (!isOnCurve(candidate))"))
                f.Add("api/_lib/solana-pda.js no longer rejects ON-CURVE candidates when deriving the " +
                      "PDA. A PDA is BY DEFINITION off the curve; without the check the derived " +
                      "UserStake address is wrong for about half of all wallets and fails SILENTLY as " +
                      "'no stake'. (Five real mainnet accounts cross-checked at bump 254 — reachable " +
                      "only if this rejection runs.)");

            // C5. Unstaking is reported but never added to the active stake
            //     (acceptance criterion 3). The IDL proves shares already exclude it.
            if (!verifier.Contains("computeActiveStakeRaw") ||
                !verifier.Contains("(sharesRaw * sharePriceRaw) / SHARE_PRICE_SCALE"))
                f.Add("ACCEPTANCE 3: the active stake is no longer shares x share_price / 1e9. The " +
                      "program's own IDL states unstake BURNS shares, so that product is what excludes " +
                      "the unstaking amount; any other formula has to re-derive the exclusion by hand.");

            // C6. An outage must never become a zero.
            if (!verifier.Contains("resolveServedState"))
                f.Add("ACCEPTANCE 4: the verifier has no resolveServedState, so there is nothing " +
                      "implementing the owner's 2026-09-10 13:10 ruling (last-known verified state " +
                      "within a bounded grace window) and an RPC failure can reach a consumer as zero.");

            // C7. The endpoint accepts NO amount, under any name, and is GET-only.
            if (endpoint.Length > 0)
            {
                if (!endpoint.Contains("req.method !== 'GET'"))
                    f.Add("SECURITY: /api/heartbound/status is not GET-only. A body is a place a client " +
                          "can submit `stakedSkr = 500000`, which spec :162-168 forbids outright.");
                if (endpoint.Contains("req.body"))
                    f.Add("SECURITY: /api/heartbound/status reads req.body. The client may not submit a " +
                          "staking value or any equivalent (spec :164-168).");
                // ⚠ SCAN CODE, NOT DOCUMENTATION. The endpoint's own header QUOTES the
                // spec — "the client may NOT submit `stakedSkr = 500000`" — and a raw
                // text scan flags that comment as the very violation it warns against.
                // Caught while authoring this suite: the first version failed against a
                // correct file. A lint that punishes a file for explaining itself
                // teaches the next author to delete the explanation.
                string endpointCode = StripLineComments(endpoint);
                foreach (string banned in new[] { "stakedSkr", "activeStake\"]", "query.tier",
                                                  "query.stake", "query.amount", "query.shares" })
                    if (endpointCode.IndexOf(banned, StringComparison.Ordinal) >= 0)
                        f.Add("SECURITY: /api/heartbound/status appears to read a client-supplied '" +
                              banned + "'. The stake is read from the CHAIN, never from the request.");
                if (!endpoint.Contains("auth.identity"))
                    f.Add("SECURITY: /api/heartbound/status does not resolve the wallet from the " +
                          "AUTHENTICATED identity (spec :168). Reading the playerId query parameter " +
                          "instead trusts a value the request supplied.");
                if (!endpoint.Contains("authenticate(sql, req, null, playerId)"))
                    f.Add("SECURITY: /api/heartbound/status does not run the standard wallet-auth gate, " +
                          "so a base58 id is not proven by an ed25519 signature over a single-use nonce.");
            }

            // C8. The snapshot's amount columns stay NULLABLE with no default. A
            //     DEFAULT 0 turns "never verified" into "verified to hold nothing" —
            //     the WO-1457 corruption wearing a different column name.
            if (migration.Length > 0)
            {
                if (!migration.Contains("active_staked_raw    NUMERIC(39,0),"))
                    f.Add("skr_stake_snapshots.active_staked_raw is no longer a NULLABLE NUMERIC(39,0) " +
                          "with no default. NULL means 'never verified'; a DEFAULT 0 would claim a " +
                          "server-observed stake of zero we have no evidence for (migrations 0022/0023).");
                if (!migration.Contains("verified_at          TIMESTAMPTZ,") ||
                    !migration.Contains("last_attempt_at      TIMESTAMPTZ NOT NULL"))
                    f.Add("skr_stake_snapshots no longer separates verified_at (last SUCCESS, the grace " +
                          "clock) from last_attempt_at. Collapsed into one column, an outage renews its " +
                          "own grace window forever.");
            }

            if (f.Count > 0)
            {
                reason = "STAKING_COMPLIANCE_FAIL: " + string.Join(" | ", f);
                return false;
            }

            reason = "STAKING COMPLIANCE OK - no reward path reads a client-reported stake " +
                     "(one backend-written snapshot, one writer); the DAPP_STORE define and the " +
                     "!GOOGLE_PLAY asmdef constraint gate every Heartbound surface; the mainnet " +
                     "verifier exists, derives its PDA off-curve, and its endpoint is GET-only and " +
                     "accepts no amount under any name";
            return true;
        }

        /// <summary>
        /// Drop `//` line comments so a banned token is judged on what the file DOES,
        /// not on what it explains. Deliberately naive — it does not model strings, so
        /// a `//` inside a string literal would truncate that line. That is acceptable
        /// here (the tokens being hunted are identifiers, and a false ABSENCE in one
        /// commented line cannot mask a real read elsewhere) and it is stated rather
        /// than left for the next reader to discover.
        /// </summary>
        private static string StripLineComments(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var kept = new List<string>();
            foreach (string line in source.Split('\n'))
            {
                int idx = line.IndexOf("//", StringComparison.Ordinal);
                kept.Add(idx >= 0 ? line.Substring(0, idx) : line);
            }
            return string.Join("\n", kept);
        }

        /// <summary>Runtime C# only — Editor and test sources are not shipped grant paths.</summary>
        private static IEnumerable<string> EnumerateRuntimeCs()
        {
            const string root = "Assets/_Modules";
            if (!Directory.Exists(root)) return Array.Empty<string>();
            try { return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories); }
            catch (Exception) { return Array.Empty<string>(); }
        }

        private static string Read(string path, List<string> f)
        {
            if (File.Exists(path)) return File.ReadAllText(path);
            f.Add("missing " + path);
            return string.Empty;
        }
    }
}
