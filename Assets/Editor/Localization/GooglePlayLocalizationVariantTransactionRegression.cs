using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Localization
{
    /// <summary>Focused destructive-then-restored proof for the Play localization transaction.</summary>
    public static class GooglePlayLocalizationVariantTransactionRegression
    {
        public static void RunFocused()
        {
            var failures = new List<string>();
            string[] paths = TransactionPaths();
            var original = paths.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

            try
            {
                bool prepared = GooglePlayLocalizationVariant.PrepareForAddressables(
                    new[] { GooglePlayLocalizationVariant.PlayDefine });
                if (!prepared) failures.Add("GOOGLE_PLAY did not prepare a localization variant");
                GooglePlayLocalizationVariant.AssertPrepared();

                bool reused = GooglePlayLocalizationVariant.PrepareForAddressables(
                    new[] { GooglePlayLocalizationVariant.PlayDefine });
                if (!reused) failures.Add("active transaction was not reused idempotently");
                GooglePlayLocalizationVariant.AssertPrepared();
            }
            catch (Exception exception)
            {
                failures.Add(exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                try { GooglePlayLocalizationVariant.Restore("focused transaction regression"); }
                catch (Exception exception)
                {
                    failures.Add("restore threw " + exception.GetType().Name + ": " + exception.Message);
                }
            }

            AssertBytes(paths, original, failures, "Play restore");
            if (File.Exists(GooglePlayLocalizationVariant.LedgerPath))
                failures.Add("transaction ledger survived a successful restore");
            if (Directory.Exists(GooglePlayLocalizationVariant.BackupRoot))
                failures.Add("transaction backup directory survived a successful restore");

            try
            {
                if (GooglePlayLocalizationVariant.PrepareForAddressables(Array.Empty<string>()))
                    failures.Add("non-Play defines prepared a Play localization variant");
            }
            catch (Exception exception)
            {
                failures.Add("non-Play no-op threw " + exception.GetType().Name + ": " + exception.Message);
            }
            AssertBytes(paths, original, failures, "non-Play no-op");

            if (failures.Count == 0)
            {
                Debug.Log("PLAY_LOCALIZATION_VARIANT_TRANSACTION_OK - 20 canonical locale files and 7 GameStrings assets transformed, asserted, reused, and restored byte-for-byte");
                return;
            }

            foreach (string failure in failures.Distinct())
                Debug.LogError("[PlayLocalizationVariantRegression] " + failure);
            Debug.LogError("PLAY_LOCALIZATION_VARIANT_TRANSACTION_FAIL");
            EditorApplication.Exit(1);
        }

        private static string[] TransactionPaths()
        {
            JObject policy = JObject.Parse(File.ReadAllText(GooglePlayLocalizationVariant.PolicyPath));
            string[] required = policy["requiredLocales"]?.Values<string>().ToArray() ?? Array.Empty<string>();
            string[] enabled = policy["enabledTableLocales"]?.Values<string>().ToArray() ?? Array.Empty<string>();
            var paths = new List<string>();
            foreach (string locale in required)
            {
                paths.Add("Assets/Resources/Data/Canonical/" + locale + ".json");
                paths.Add("Assets/StreamingAssets/Data/Canonical/" + locale + ".json");
            }
            paths.Add("Assets/Localization/Tables/GameStrings Shared Data.asset");
            paths.AddRange(enabled.Select(locale => "Assets/Localization/Tables/GameStrings_" + locale + ".asset"));
            return paths.ToArray();
        }

        private static void AssertBytes(
            IEnumerable<string> paths,
            IReadOnlyDictionary<string, byte[]> original,
            ICollection<string> failures,
            string phase)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in paths)
            {
                if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(original[path]))
                    failures.Add(phase + " changed tracked bytes: " + path);
            }
        }
    }
}
