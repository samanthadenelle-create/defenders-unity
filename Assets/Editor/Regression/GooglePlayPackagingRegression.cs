using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace DeNelle.Editor
{
    /// <summary>WO-1255 source oracle for the fail-closed Play AAB packaging chain.</summary>
    public static class GooglePlayPackagingRegression
    {
        /// <summary>Focused batchmode entry point; does not build an AAB.</summary>
        public static void RunFocused()
        {
            if (Run(out string focusedReason))
            {
                UnityEngine.Debug.Log(focusedReason);
                UnityEngine.Debug.Log("PLAY_PACKAGING_REGRESSION_OK");
                return;
            }

            UnityEngine.Debug.LogError(focusedReason);
            UnityEngine.Debug.LogError("PLAY_PACKAGING_REGRESSION_FAIL");
            UnityEditor.EditorApplication.Exit(1);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string build = Read("Assets/Editor/AndroidBuild.cs", failures);
            string gate = Read("Assets/Editor/Regression/GooglePlayPackagingGate.cs", failures);
            string scanner = Read("tools/android/assert-google-play-aab-clean.ps1", failures);
            string piController = Read("Assets/_Modules/Core/Platform/PiSignInController.cs", failures);
            string loginBridge = Read("Assets/_Modules/Core/Platform/LoginSurfacePlatform.cs", failures);
            string loginVm = Read("Assets/_Modules/Onboarding/LoginViewModel.cs", failures);

            Require(build, "BuildGooglePlayAab", "Play AAB entry point missing", failures);
            Require(build, "GooglePlayPackagingGate.AssertSourceIsolation()", "Play build no longer runs Gate 0", failures);
            Require(build, "BuildAndroidArtifact(isGooglePlay: true)", "Play entry point does not select the Play artifact", failures);
            Require(build, "EditorUserBuildSettings.buildAppBundle = isGooglePlay", "AAB/APK mode is not asserted per artifact", failures);
            Require(build, "? \"GOOGLE_PLAY\" : \"DAPP_STORE\"", "immutable channel stamps missing", failures);
            Require(build, "string forbidden = isGooglePlay ? \"DAPP_STORE\" : \"GOOGLE_PLAY\"",
                    "artifact define composition no longer removes the opposite channel", failures);
            Require(build, "!isGooglePlay || !string.Equals(value, \"SOLANA_SDK\"",
                    "Play artifact define composition no longer strips SOLANA_SDK", failures);
            Require(build, ".Append(wanted)", "artifact define composition no longer supplies its channel stamp", failures);
            Require(build, "GooglePlayPackagingGate.AssertBuiltArtifact(artifactPath)", "successful AAB bypasses post-build inspection", failures);
            Require(build, "PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation",
                    "Android build does not remove the landscape-only orientation restriction", failures);
            Require(build, "PlayerSettings.allowedAutorotateToPortrait = true",
                    "Android build does not permit portrait on large/foldable displays", failures);
            Require(build, "PlayerSettings.allowedAutorotateToPortraitUpsideDown = true",
                    "Android build does not permit reverse portrait on large/foldable displays", failures);
            Require(build, "PlayerSettings.allowedAutorotateToLandscapeRight = true",
                    "Android build does not permit landscape-right", failures);
            Require(build, "PlayerSettings.allowedAutorotateToLandscapeLeft = true",
                    "Android build does not permit landscape-left", failures);

            // Google Sign-In 1.0.4 originally shipped a 4 KB-aligned prebuilt native library.
            // Keep the Maven coordinate/API stable, but pin the maintainer's 16 KB rebuild by
            // digest in BOTH the source m2 repository and EDM's generated local repository.
            // If dependency resolution restores the old AAR, this turns red before submission.
            const string alignedGoogleSignInSha256 =
                "BDB08061F4371042B2E55859F914543FB01CA452F506B234E01C13CB55A885A0";
            RequireSha256(
                "Assets/GoogleSignIn/Editor/m2repository/com/google/signin/google-signin-support/1.0.4/google-signin-support-1.0.4.srcaar",
                alignedGoogleSignInSha256, "source Google Sign-In support archive is not the 16 KB-aligned rebuild", failures);
            RequireSha256(
                "Assets/GeneratedLocalRepo/GoogleSignIn/Editor/m2repository/com/google/signin/google-signin-support/1.0.4/google-signin-support-1.0.4.aar",
                alignedGoogleSignInSha256, "resolved Google Sign-In support archive is not the 16 KB-aligned rebuild", failures);

            int gateAt = build.IndexOf("GooglePlayPackagingGate.AssertSourceIsolation()", StringComparison.Ordinal);
            int buildAt = build.IndexOf("BuildAndroidArtifact(isGooglePlay: true)", StringComparison.Ordinal);
            if (gateAt < 0 || buildAt < 0 || gateAt > buildAt)
                failures.Add("Play source gate does not execute before the player build");

            Require(gate, "DeNelle.Village directly references DeNelle.Wallet", "known assembly-graph blocker is no longer diagnosed", failures);
            Require(gate, "MobileWalletAdapter.androidlib is an unconditional Android plugin", "MWA plugin blocker is no longer diagnosed", failures);
            Require(gate, "Solana SDK is not embedded", "embedded-package source gate is missing", failures);
            Require(gate, "Embedded Solana SDK runtime assembly has no !GOOGLE_PLAY constraint", "SDK runtime constraint gate is missing", failures);
            Require(gate, "Embedded Solana managed plugin is unconditional", "managed-plugin constraint gate is missing", failures);
            Require(gate, "Vendored UniTask must remain available", "UniTask availability gate is missing", failures);
            Require(gate, "Android PlayerSettings persist artifact symbol",
                    "persistent Android artifact-symbol rejection is missing", failures);
            Require(gate, "PLAY_ARTIFACT_DIRTY", "artifact rejection marker missing", failures);
            Require(gate, "ScanStream(stream, entry.FullName, tokens, readable, hits)", "in-build audit no longer scans every AAB payload", failures);
            Require(gate, "SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3", "live SKR mint is absent from forbidden material", failures);
            Require(gate, "stake.solanamobile", "staking marketing is absent from forbidden material", failures);
            Require(gate, "\"crypto\"", "generic crypto token is absent from forbidden material", failures);
            Require(gate, "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
                    "live USDC mint is absent from forbidden material", failures);
            Require(gate, "FalsePositiveAllowlist",
                    "documented false-positive allowlist is gone; the next FP will be fixed by weakening a token", failures);
            Require(gate, "cryptograph",
                    "System.Security.Cryptography allowlist entry is gone", failures);
            Require(gate, "ShortTokensRequiringTextContext",
                    "short-token evidence rule is gone; skr/usdc/web3 are either hollow or flaky without it", failures);
            Reject(gate, "OpaqueExecutableTokens",
                    "a weakened opaque token tier has been re-introduced (WO-1364 blind spot)", failures);
            Require(gate, "\"sign in with pi\"", "Pi authentication CTA is absent from forbidden material", failures);
            Require(gate, "\"api.minepi.com\"", "Pi authentication backend is absent from forbidden material", failures);
            Require(gate, "defenders/mwa/", "in-build scanner does not target the actual MWA package path", failures);
            Require(gate, "phantom wallet", "in-build scanner does not target Phantom wallet branding", failures);
            Require(gate, "app.phantom", "in-build scanner does not target Phantom app identifiers", failures);
            Reject(gate, "\"mwa/\"", "in-build scanner still uses ambiguous mwa/ token", failures);
            Reject(gate, "\"phantom\"", "in-build scanner still uses ambiguous phantom token", failures);
            Require(scanner, "PLAY_ARTIFACT_CLEAN_OK", "standalone artifact scanner success marker missing", failures);
            Require(scanner, "Test-StreamToken", "standalone scanner no longer inspects binary payloads", failures);
            Require(scanner, "Find-StreamTokens", "standalone scanner no longer sweeps the whole vocabulary per payload", failures);
            Require(scanner, "GetEncoding(28591)", "standalone scanner decodes payloads as ASCII again, which folds every high byte to a printable '?'", failures);
            // WO-1364: the scanner no longer keeps its own copy of the token policy - the
            // two copies had already drifted once. It parses the compiled gate's arrays and
            // fails CLOSED if it cannot. These pins moved from "the scanner spells out token
            // X" to "the scanner reads the one place token X is defined, and refuses to run
            // otherwise"; the per-token coverage is pinned on the gate above and in the
            // vocabulary mutations below.
            Require(scanner, "Get-GateTokenArray", "standalone scanner no longer derives its policy from the compiled gate", failures);
            Require(scanner, "Assets/Editor/Regression/GooglePlayPackagingGate.cs", "standalone scanner no longer points at the single source of truth", failures);
            Require(scanner, "'ForbiddenTokens'", "standalone scanner does not read the forbidden vocabulary", failures);
            Require(scanner, "'ShortTokensRequiringTextContext'", "standalone scanner does not read the short-token evidence rule", failures);
            Require(scanner, "'FalsePositiveAllowlist'", "standalone scanner does not read the documented false-positive allowlist", failures);
            Require(scanner, "refusing to scan with an empty policy", "standalone scanner no longer fails closed on a missing policy source", failures);
            Require(scanner, "parsed empty from", "standalone scanner no longer fails closed on an empty token array", failures);
            Require(scanner, "$tokens = $forbiddenTokens", "standalone scanner no longer polices every entry with the whole vocabulary", failures);
            Require(scanner, "Test-PrintableRun", "standalone scanner lost the binary short-token corroboration rule", failures);
            Require(scanner, "Test-AllowlistedOccurrence", "standalone scanner lost allowlist-aware matching", failures);
            Reject(scanner, "$userFacingTokens = @(", "standalone scanner has re-inlined a second copy of the token policy", failures);
            Reject(scanner, "$opaqueTokens = @(", "standalone scanner has re-inlined a weakened opaque token tier", failures);
            Require(scanner, "BUNDLE-METADATA/com.unity/dependencies.pb", "standalone scanner no longer classifies dependency provenance separately", failures);
            Reject(scanner, "'mwa/'", "standalone scanner still uses ambiguous mwa/ token", failures);
            Reject(scanner, "'phantom'", "standalone scanner still uses ambiguous phantom token", failures);

            Require(piController, "#if GOOGLE_PLAY", "Pi runtime has no Play compile-time exclusion", failures);
            Require(piController, "public static string SignedInUid => null;",
                    "Play Pi stub no longer preserves the shared read-only identity seam", failures);
            int piGuard = piController.IndexOf("#if GOOGLE_PLAY", StringComparison.Ordinal);
            int piElse = piController.IndexOf("#else", piGuard, StringComparison.Ordinal);
            int piRuntime = piController.IndexOf("public sealed class PiSignInController : MonoBehaviour", StringComparison.Ordinal);
            if (piGuard < 0 || piElse < 0 || piRuntime < piElse)
                failures.Add("Pi runtime controller is not confined to the non-Play branch");

            Require(loginBridge, "GooglePlayIdentityBridge.EnsureSignedInAsync()",
                    "Play login still does not call the real Google identity bridge", failures);
            Require(loginBridge, "GameStateService.IsGooglePlayIdentity(playerId)",
                    "Play login no longer verifies the bound play-* identity", failures);
            Require(loginVm, "#if !GOOGLE_PLAY", "Play VM can re-bind Google identity through the wallet API", failures);

            // -- WO-1364: vocabulary mutation pins, RE-POINTED and made stricter ------------
            // These cases used to pin the BLIND SPOT. Until 2026-09-04 they failed if the
            // opaque tier rejected crypto/web3 (:112-114 of the old file) or the Solana
            // package identity (:115-116), so making the gate stricter turned the suite red.
            // That tier split is what let Builds/ui-reskin-final-google-play-aab-v2.log:38188
            // emit PLAY_ARTIFACT_CLEAN_OK on an AAB carrying solana x74, SKR x35, Jupiter x12
            // and the USDC mint: every C# string literal lands in global-metadata.dat, which
            // was routed to the weakened list. The pin is not deleted - it is inverted. One
            // vocabulary now polices BOTH entry classes, and short tokens are disambiguated
            // in binaries by evidence quality (a printable run), never by dropping the token.
            string[] readableMutation = GooglePlayPackagingGate.TokensForEntry(
                "base/assets/Data/Canonical/mutation.json");
            string[] opaqueMutation = GooglePlayPackagingGate.TokensForEntry(
                "base/assets/bin/Data/Managed/Metadata/global-metadata.dat");
            string[] mustPolice =
            {
                "crypto", "web3", "solana", "jupiter", "usdc", "blockchain", "skr",
                "Solana.Unity.", "connect wallet", "stake.solanamobile",
                "SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3",
                "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v"
            };
            foreach (string required in mustPolice)
            {
                if (!Array.Exists(readableMutation, token => token == required))
                    failures.Add($"readable-content mutation no longer rejects {required}");
                if (!Array.Exists(opaqueMutation, token => token == required))
                    failures.Add($"opaque mutation no longer rejects {required} - this is the WO-1364 blind spot returning");
            }

            // Evidence-quality rule: a 3-4 character token collides with random bytes in a
            // 500 MB artifact, so in a BINARY entry it must sit in a printable run and have
            // boundaries on both sides. In readable text it needs none of that. Losing either
            // half turns the gate hollow (misses SKR) or flaky (fires on noise).
            if (!GooglePlayPackagingGate.IsShortToken("skr") ||
                GooglePlayPackagingGate.IsShortToken("solana"))
                failures.Add("short-token classification no longer distinguishes collision-prone tokens");
            if (GooglePlayPackagingGate.MatchesTokenInPayload(
                    "skr", "skr", readableEntry: false))
                failures.Add("binary short-token rule falsely rejects random bytes spelling skr");
            if (!GooglePlayPackagingGate.MatchesTokenInPayload(
                    "Balance: {0} SKR", "skr", readableEntry: false))
                failures.Add("binary short-token rule no longer rejects a real SKR string literal");
            if (!GooglePlayPackagingGate.MatchesTokenInPayload(
                    "{\"skr\": 3}", "skr", readableEntry: true))
                failures.Add("readable short-token rule no longer rejects an authored skr field");
            if (!GooglePlayPackagingGate.MatchesTokenInPayload(
                    "Powered with SKR - stake natively", "skr", readableEntry: false))
                failures.Add("binary short-token rule no longer rejects SKR marketing copy");

            // Documented false positives are suppressed BY NAME, with a reason in the gate.
            // The token itself stays live - suppressing it wholesale is what caused this WO.
            if (GooglePlayPackagingGate.MatchesTokenForAudit(
                    "System.Security.Cryptography.Aes", "crypto"))
                failures.Add("BCL cryptography allowlist entry is missing; crypto cannot survive in the opaque tier");
            if (GooglePlayPackagingGate.MatchesTokenForAudit("javax.crypto.Cipher", "crypto"))
                failures.Add("JCE package allowlist entry is missing");
            if (GooglePlayPackagingGate.MatchesTokenForAudit("libcrypto.so", "crypto"))
                failures.Add("OpenSSL native library allowlist entry is missing");
            if (!GooglePlayPackagingGate.MatchesTokenForAudit("Nothing crypto ships here", "crypto"))
                failures.Add("allowlist has swallowed standalone crypto material");
            // Measured non-allowlisted occurrences from the 2026-09-04 AAB. Each one is
            // third-party or engine material; losing any of these entries makes the gate
            // cry wolf, and the historical response to a crying gate was to delete the token.
            foreach (string thirdParty in new[]
                     {
                         "psa_crypto_init", "NEON_AArch64_crypto.cs", "ebc_cryptofthecount",
                         "crypto.getRandomValues(new Uint8Array(1))", "return crypto&&crypto.getRandomValues",
                         "case \"CryptoKey\":", "FingerprintManager$CryptoObject", "Landroid/crypto/hpke/Hpke;",
                         "com.google.crypto.tink.AesGcmKey", "com.google.firebase.auth.api.crypto.%s",
                         "Exception encountered during crypto setup:", "firebear_main_key_id_for_storage_crypto",
                         "Mono.Security.Cryptography.CryptoConvert", "keyRings/%s/cryptoKeys/%s"
                     })
                if (GooglePlayPackagingGate.MatchesTokenForAudit(thirdParty, "crypto"))
                    failures.Add($"documented third-party crypto false positive is no longer allowlisted: {thirdParty}");
            // ...and the string we DO author still trips the gate.
            if (!GooglePlayPackagingGate.MatchesTokenForAudit(
                    "No wallet is connected and no live crypto is used.", "crypto"))
                failures.Add("allowlist now swallows our own authored crypto copy");

            // JAR digest listings: base64 SHA digests are printable runs of arbitrary
            // characters, so short tokens cannot be judged there (MEASURED: MANIFEST.MF
            // carries the digest ...I89IGK+USDc=, which matched usdc on both boundaries).
            // Long tokens are still enforced there so a forbidden FILE NAME is still caught.
            if (!GooglePlayPackagingGate.IsSignatureDigestEntry("META-INF/MANIFEST.MF") ||
                !GooglePlayPackagingGate.IsSignatureDigestEntry("META-INF/ANDROIDD.SF"))
                failures.Add("JAR signature digest listings are no longer classified");
            if (GooglePlayPackagingGate.IsSignatureDigestEntry(
                    "base/assets/bin/Data/Managed/Metadata/global-metadata.dat"))
                failures.Add("digest-listing exemption has leaked onto the IL2CPP metadata blob");
            Require(scanner, "isDigestListing", "standalone scanner lost the JAR digest-listing rule", failures);
            Require(gate, "IsSignatureDigestEntry", "gate lost the JAR digest-listing rule", failures);
            if (!GooglePlayPackagingGate.MatchesTokenForAudit(
                    "pay with EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
                    "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v"))
                failures.Add("USDC mint is no longer matched");
            if (!GooglePlayPackagingGate.ShouldSkipProvenanceEntry(
                    "BUNDLE-METADATA/com.unity/dependencies.pb"))
                failures.Add("dependency provenance receipt is no longer classified separately");
            if (GooglePlayPackagingGate.MatchesTokenForAudit(
                    "Disconnect Wallet pressed", "connect wallet"))
                failures.Add("word-boundary mutation falsely rejects Disconnect Wallet");
            if (!GooglePlayPackagingGate.MatchesTokenForAudit(
                    "Tap Connect Wallet now", "connect wallet"))
                failures.Add("word-boundary mutation no longer rejects standalone Connect Wallet");
            Require(scanner, "Test-TokenInText", "standalone scanner lost boundary-aware token matching", failures);

            // -- WO-1282 Lane D: the force-include door -------------------------------------
            // Source isolation being green proved NOTHING about the artifact on 2026-08-30:
            // Resources/ and StreamingAssets/ are packed by construction, so no .asmdef
            // constraint reaches them. These assertions pin the mechanism that does, INCLUDING
            // its restore discipline — a quarantine with no restore silently strips the Solana
            // rail from the next Seeker APK, which is a WORSE regression than the one it fixes.
            string content = Read("Assets/Editor/GooglePlayContentExclusion.cs", failures);
            Require(content, "Assets/Resources/SolanaUnitySDK",
                    "Play content exclusion no longer quarantines the force-included Solana SDK Resources", failures);
            Require(content, "Assets/StreamingAssets/Data/Canonical/wallets.json",
                    "Play content exclusion no longer quarantines the StreamingAssets wallet registry", failures);
            Require(content, "Assets/Resources/Data/Canonical/wallets.json",
                    "Play content exclusion no longer quarantines the Resources wallet registry mirror", failures);
            Require(content, "Assets/Resources/Data/Canonical/stake-rewards.json",
                    "Play content exclusion no longer quarantines generated stake-reward source data", failures);
            Require(content, "root[\"_nightMarketNote\"] = \"Google Play store presentation.\"",
                    "Play-neutral canon-strings rewrite lost its exact note allowlist", failures);
            Require(content, "root[\"storeBuyWalletRequiredCta\"] = \"Continue\"",
                    "Play-neutral wallet CTA rewrite lost its exact field pin", failures);
            string localizationVariant = Read("Assets/Editor/Localization/GooglePlayLocalizationVariant.cs", failures);
            string localizationPolicy = Read("Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json", failures);
            Require(localizationVariant, "PLAY_LOCALIZATION_VARIANT_OK",
                    "Play localization variant has no prepared-state marker", failures);
            Require(localizationVariant, "shared.RemoveKey(sharedEntry.Id)",
                    "Play localization variant no longer removes stripped shared-table ids", failures);
            Require(localizationPolicy, "\"heroSelect.subtitle\"",
                    "Play localization policy lost its visible hero-title replacement", failures);
            Require(content, "GooglePlayLocalizationVariant.AssertPrepared()",
                    "BuildPlayer content hook no longer requires the pre-Addressables localization variant", failures);
            Require(content, "pricing?.Property(\"usdc\")?.Remove()",
                    "Play-neutral pack rewrite no longer removes only wallet price rails", failures);
            Require(content, "pricing?.Property(\"sol\")?.Remove()",
                    "Play-neutral pack rewrite no longer removes SOL pricing", failures);
            Require(content, "pricing?.Property(\"skr\")?.Remove()",
                    "Play-neutral pack rewrite no longer removes SKR pricing", failures);
            Require(content, "RewriteLedgerPath", "Play-neutral rewrite has no crash-safe ledger", failures);
            Require(content, "File.WriteAllBytes(backup, File.ReadAllBytes(path))",
                    "Play-neutral rewrite no longer backs up original bytes", failures);
            Require(content, "ValidateNeutralMirrorEquality(\"rewrite\")",
                    "Play-neutral rewrite no longer validates mirror equality after mutation", failures);
            Require(content, "ValidateNeutralMirrorEquality(\"restore\")",
                    "Play-neutral rewrite no longer validates mirror equality after restoration", failures);
            Require(content, "Assets/_Modules/Onboarding/UI/TitleScreen.uxml",
                    "Play-neutral rewrite no longer covers TitleScreen.uxml", failures);
            Require(content, "Assets/_Modules/Onboarding/UI/HeroSelectScreen.uxml",
                    "Play-neutral rewrite no longer covers HeroSelectScreen.uxml", failures);
            Require(content, "uxml.Replace(\"Connect Wallet\", \"Continue with Google\")",
                    "Play-neutral UXML rewrite lost its exact text mutation", failures);
            Require(content, "PLAY_NEUTRAL_BYTE_RESTORE_MISMATCH",
                    "Play-neutral UXML/catalog transaction no longer verifies byte restore", failures);

            string currencySkin = Read("Assets/_Modules/Core/Platform/CurrencySkin.cs", failures);
            string showcase = Read("Assets/_Modules/Core/UI/SkrShowcasePanel.cs", failures);
            string stakePanel = Read("Assets/_Modules/Core/UI/StakeRewardsPanel.cs", failures);
            Require(currencySkin, "#if GOOGLE_PLAY", "SKR currency fallback is not Play-neutral at compile time", failures);
            Require(currencySkin, "storeCtaVerb: \"Continue\"", "Play currency fallback exposes a crypto spend CTA", failures);
            Require(showcase, "ConnectActionLabel = \"Continue with Google\"",
                    "Play showcase action is not compile-time neutral", failures);
            Require(stakePanel, "StakeNativeLine = \"Store rewards are unavailable in this edition.\"",
                    "Play staking surface still compiles its native-staking URL", failures);
            Require(content, "IPostprocessBuildWithReport",
                    "Play content exclusion has no post-build restore", failures);
            Require(content, "InitializeOnLoadMethod",
                    "Play content exclusion has no domain-load repair for an interrupted build", failures);
            Require(content, "PLAY_CONTENT_REPAIRED",
                    "interrupted-build repair no longer announces itself", failures);
            Require(build, "GooglePlayContentExclusion.EnsureTreeIsWhole()",
                    "AndroidBuild no longer sweeps a leftover quarantine before the content build", failures);

            int sweepAt = build.IndexOf("GooglePlayContentExclusion.EnsureTreeIsWhole()", StringComparison.Ordinal);
            int contentAt = build.IndexOf("AddressablesContentBuild.EnsureBuilt", StringComparison.Ordinal);
            if (sweepAt < 0 || contentAt < 0 || sweepAt > contentAt)
                failures.Add("quarantine sweep does not run before the Addressables content build; a Seeker " +
                             "APK could be baked from a wallet-less tree");

            int localizationAt = build.IndexOf(
                "GooglePlayLocalizationVariant.PrepareForAddressables(options.extraScriptingDefines)",
                StringComparison.Ordinal);
            int localizationRestoreAt = build.IndexOf(
                "GooglePlayLocalizationVariant.Restore(\"Android build finally\")",
                StringComparison.Ordinal);
            if (localizationAt < 0 || localizationAt > contentAt)
                failures.Add("Play localization variant does not run before Addressables content is built");
            if (localizationRestoreAt < contentAt)
                failures.Add("Play localization variant is not restored after the Android build transaction");

            string stamp = Read("Assets/_Modules/Core/Payments/ArtifactVariantStamp.cs", failures);
            Require(stamp, "RuntimeInitializeOnLoadMethod",
                    "artifact variant is no longer stamped into the device log at startup", failures);
            Require(stamp, "SEEKER BUILD IS MISSING THE WALLET PAYLOAD",
                    "a wallet-less Seeker build no longer announces itself on first launch", failures);

            // This is an audit observation, not a red regression: today Gate 0 is expected to
            // block. Once the assembly split/plugin exclusion lands, the same regression stays
            // green and the build may proceed to physical artifact proof.
            int blockerCount = GooglePlayPackagingGate.InspectSourceIsolation().Count;
            if (blockerCount == 0)
                reason = "PLAY_PACKAGING_GATE_OK - source isolation ready; AAB still requires physical scanner proof";
            else
                reason = $"PLAY_PACKAGING_GATE_OK - fail-closed with {blockerCount} named Gate-0 blocker(s); no non-compliant AAB can be built";

            if (failures.Count == 0) return true;
            reason = "PLAY_PACKAGING_GATE_FAIL: " + string.Join(" | ", failures);
            return false;
        }

        private static void RequireSha256(string path, string expected, string message, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("missing " + path);
                return;
            }

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add(message + ": " + path + " SHA-256 " + actual +
                                 " != pinned 16 KB rebuild " + expected);
            }
        }

        private static string Read(string path, List<string> failures)
        {
            if (File.Exists(path)) return File.ReadAllText(path);
            failures.Add("missing " + path);
            return string.Empty;
        }

        private static void Require(string text, string token, string message, List<string> failures)
        {
            if (!text.Contains(token)) failures.Add(message);
        }

        private static void Reject(string text, string token, string message, List<string> failures)
        {
            if (text.Contains(token)) failures.Add(message);
        }
    }
}
