using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTownReleaseContractProof
    {
        public static void RunLocalized()
        {
            // The importer lives in the outer editor assembly; avoid a circular
            // assembly reference from this regression assembly.
            MethodInfo import = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("DeNelle.Editor.LocalizationBuilder", false);
                if (type != null) { import = type.GetMethod("BuildAll", BindingFlags.Static | BindingFlags.Public); break; }
            }
            if (import == null) throw new Exception("Localization importer is unavailable.");
            import.Invoke(null, null);
            RunBatch();
            DeNelle.Editor.Regression.LocalizationRegressionRunner.RunAll();
        }

        public static void RunBatch()
        {
            if (!ScenePostureSeamRegression.Run(out string sceneReport))
                throw new Exception(sceneReport);
            var failures = new List<string>();
            var report = new StringBuilder();
            var tutorialCheck = typeof(DataRegression).GetMethod("CheckTutorialSteps",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (tutorialCheck == null) throw new Exception("Release tutorial contract check is missing.");
            tutorialCheck.Invoke(null, new object[] { failures, report });
            if (failures.Count != 0) throw new Exception(string.Join("\n", failures));
            Debug.Log("OWNED_TOWN_RELEASE_CONTRACT_OK " + sceneReport + "\n" + report);
        }
    }
}
