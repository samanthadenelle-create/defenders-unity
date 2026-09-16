using System;
using System.Collections.Generic;
using System.Reflection;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor
{
    // One editor launch for the focused integration checks. Individual failure markers
    // are authoritative even when a legacy standalone entry catches its exception.
    public static class OwnerCastleValidation
    {
        public static void RunCatalogChecks()
        {
            try
            {
                if (!BuildEconomyRegression.Run(out string economy)) throw new InvalidOperationException(economy);
                if (!VfxAuraDifferentiationRegression.Run(out string aura)) throw new InvalidOperationException(aura);
                if (!DeNelle.Editor.Regression.JsonMirrorLiteralRegression.Run(out string mirrors)) throw new InvalidOperationException(mirrors);
                Debug.Log("OWNER_CASTLE_CATALOG_OK generated fallback parity and Cathedral naming");
            }
            catch (Exception error) { Debug.LogError("OWNER_CASTLE_CATALOG_FAIL " + error); }
        }

        public static void RunCatalogAndCompileChecks()
        {
            RunCatalogChecks();
            CompileGate.Run();
        }

        public static void RunFinalChecks()
        {
            var observed = new HashSet<string>();
            var failures = new List<string>();
            string[] required = { "RESTORE_ORIGINAL_STOREFRONTS_OK", "STRUCTURE_COMPLETION_SAFETY_OK",
                "OWNER_CASTLE_RUNTIME_OK", "OWNED_TOWN_POSE_OK", "OWNED_TOWN_RECONSTRUCTION_OK" };
            void Capture(string message, string stack, LogType type)
            {
                foreach (string marker in required)
                    if (message.StartsWith(marker, StringComparison.Ordinal)) observed.Add(marker);
                if (message.StartsWith("OWNER_CASTLE_RUNTIME_FAIL", StringComparison.Ordinal) ||
                    message.StartsWith("STRUCTURE_COMPLETION_SAFETY_FAIL", StringComparison.Ordinal) ||
                    message.StartsWith("RESTORE_ORIGINAL_STOREFRONTS_FAIL", StringComparison.Ordinal)) failures.Add(message);
            }
            Application.logMessageReceived += Capture;
            try
            {
                SyntyStructureRetheme.RestoreOriginalStorefronts();
                SyntyStructureCompletionRegression.RunAll();
                OwnerCastleRuntimeProof.Run();
                typeof(CatalogBootstrap).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                if (!AuthoredBarracksProvenanceRegression.Run(out string provenance)) throw new InvalidOperationException(provenance);
                Debug.Log("AUTHORED_BARRACKS_OK " + provenance);
                if (!DeNelle.Editor.Regression.RealmStorefrontRegression.Run(out string store)) throw new InvalidOperationException(store);
                OwnedTownScenePoseProof.Run();
                OwnedTownReconstructionProof.Run();
                foreach (string marker in required)
                    if (!observed.Contains(marker)) failures.Add("Missing check marker: " + marker);
            }
            catch (Exception error) { failures.Add(error.GetBaseException().ToString()); }
            finally { Application.logMessageReceived -= Capture; }
            if (failures.Count != 0) Debug.LogError("OWNER_CASTLE_FINAL_CHECKS_FAIL " + string.Join("; ", failures));
            else Debug.Log("OWNER_CASTLE_FINAL_CHECKS_OK original assets, scene/builder/injector, barracks reload/state replacement, store, and captured-town reconstruction");
        }
    }
}
