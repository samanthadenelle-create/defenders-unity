using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace DeNelle.Localization
{
    /// <summary>Installs the package adapter before scene code can request player copy.</summary>
    internal static class LocalizationBootstrap
    {
        private static UnityLocalizationProvider _provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _provider?.Dispose();
            _provider = null;
            LocalText.InstallProvider(null);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            if (_provider != null)
                return;

            InstallExplicitSelector();

            _provider = new UnityLocalizationProvider();
            LocalText.InstallProvider(_provider);
            _provider.Initialize();
            FlowTrace.Step("Localization", "Installed Unity Localization provider; English fallback remains available during async startup.");
        }

        private static void InstallExplicitSelector()
        {
            List<IStartupLocaleSelector> selectors = LocalizationSettings.StartupLocaleSelectors;
            for (int i = 0; i < selectors.Count; i++)
            {
                if (selectors[i] is ExplicitLocaleSelector)
                    return;
            }

            selectors.Insert(0, new ExplicitLocaleSelector());
        }
    }
}
