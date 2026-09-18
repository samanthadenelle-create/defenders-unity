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

            // WO-1861: pseudolocalization QA decorator. NO-OP unless PlayerPrefs
            // "ff.pseudoloc" == 1, and the whole type is compiled out of any build that is
            // neither the editor nor a QA_SCENARIO_BUILD. It wraps the provider just
            // installed above, which is why it is called HERE and not from the type itself:
            // this is the one place the real inner provider is in hand.
            // ⚠ ORDERING: this method is AfterAssembliesLoaded and DevScenarioIntent (which
            // WRITES ff.pseudoloc from a launch extra) is AfterSceneLoad, so on the FIRST
            // launch that passes dotr.ff.pseudoloc=1 the pref does not exist yet here.
            // DevScenarioIntent re-calls InstallIfEnabled after its ff loop for exactly that
            // case; the call is idempotent.
#if UNITY_EDITOR || QA_SCENARIO_BUILD
            PseudolocTextProvider.InstallIfEnabled(_provider);
#endif
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
