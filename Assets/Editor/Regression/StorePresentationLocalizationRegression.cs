using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Wallet;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1605: the reviewed Store presentation cohort is live through LocalText.</summary>
    public static class StorePresentationLocalizationRegression
    {
        private static readonly string[] ExpectedKeys =
        {
            "storeBandGap",
            "storeBandGapSub",
            "storeBandBasket",
            "storeSpotlightEmpty",
            "storeLedgerHeading",
            "storeCardOwned",
            "storeCardGap",
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("STORE_PRESENTATION_LOCALIZATION_OK - " + reason);
            else Debug.LogError("STORE_PRESENTATION_LOCALIZATION_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                var expected = new HashSet<string>(ExpectedKeys, StringComparer.Ordinal);
                var actual = new HashSet<string>(StorePresentationText.AllKeys, StringComparer.Ordinal);
                if (!expected.SetEquals(actual) || StorePresentationText.AllKeys.Length != ExpectedKeys.Length)
                    failures.Add("StorePresentationText.AllKeys must contain the seven reviewed keys exactly once");

                RequireKey(StorePresentationText.BandGap.Key, StorePresentationText.KeyBandGap, failures);
                RequireKey(StorePresentationText.BandGapSub.Key, StorePresentationText.KeyBandGapSub, failures);
                RequireKey(StorePresentationText.BandBasket.Key, StorePresentationText.KeyBandBasket, failures);
                RequireKey(StorePresentationText.SpotlightEmpty.Key, StorePresentationText.KeySpotlightEmpty, failures);
                RequireKey(StorePresentationText.LedgerHeading.Key, StorePresentationText.KeyLedgerHeading, failures);
                RequireKey(StorePresentationText.CardOwned.Key, StorePresentationText.KeyCardOwned, failures);
                RequireKey(StorePresentationText.CardGap.Key, StorePresentationText.KeyCardGap, failures);

                string root = Directory.GetParent(Application.dataPath).FullName;
                string source = File.ReadAllText(Path.Combine(root, "Assets/_Modules/Wallet/PackStore.cs"));
                RequireCount(source, "StorePresentationText.BandGap.Resolve()", 1, failures);
                RequireCount(source, "StorePresentationText.BandGapSub.Resolve()", 1, failures);
                RequireCount(source, "StorePresentationText.BandBasket.Resolve()", 1, failures);
                RequireCount(source, "StorePresentationText.SpotlightEmpty.Resolve()", 1, failures);
                RequireCount(source, "StorePresentationText.LedgerHeading.Resolve()", 1, failures);
                RequireCount(source, "StorePresentationText.CardOwned.Resolve()", 2, failures);
                RequireCount(source, "StorePresentationText.CardGap.Resolve()", 1, failures);

                foreach (string key in ExpectedKeys)
                {
                    if (source.IndexOf("StoreStrings.Get(StoreStrings.Key" + Suffix(key) + ")", StringComparison.Ordinal) >= 0)
                        failures.Add(key + " still resolves through legacy StoreStrings");
                }

                // Product-sensitive rows remain deliberately outside this safe cutover.
                Require(source, "StoreStrings.Get(StoreStrings.KeyBandFree)", failures);
                Require(source, "StoreStrings.Get(StoreStrings.KeyBandPatronage)", failures);
                Require(source, "StoreStrings.Get(StoreStrings.KeyBandBasketSub)", failures);
                Require(source, "StoreStrings.Get(StoreStrings.KeyCardAnchor)", failures);
                Require(source, "StoreStrings.Format(StoreStrings.KeyBalanceAfter", failures);
            }
            catch (Exception ex)
            {
                failures.Add(ex.GetType().Name + ": " + ex.Message);
            }

            reason = failures.Count == 0
                ? "seven safe Store presentation keys resolve through StorePresentationText/LocalText; held product-sensitive rows remain legacy"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static void RequireKey(string actual, string expected, ICollection<string> failures)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                failures.Add("localized wrapper points at " + actual + " instead of " + expected);
        }

        private static void Require(string source, string needle, ICollection<string> failures)
        {
            if (source.IndexOf(needle, StringComparison.Ordinal) < 0)
                failures.Add("held legacy call disappeared: " + needle);
        }

        private static void RequireCount(string source, string needle, int expected, ICollection<string> failures)
        {
            int count = 0;
            for (int at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
            if (count != expected)
                failures.Add(needle + " occurs " + count + " time(s), expected " + expected);
        }

        private static string Suffix(string key) => char.ToUpperInvariant(key[5]) + key.Substring(6);
    }
}
