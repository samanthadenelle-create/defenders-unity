using System;
using System.Collections.Generic;
using System.IO;

namespace DeNelle.Editor
{
    /// <summary>Release gate for the synchronous audio bootstrap black-screen class.</summary>
    public static class AudioStartupBoundedRegression
    {
        private static readonly string[] LoaderPaths =
        {
            "Assets/_Modules/Core/Addressables/AudioAssetLoader.cs",
            "Assets/_Modules/Core/Addressables/VfxAssetLoader.cs",
            "Assets/_Modules/Core/Addressables/HeroAssetLoader.cs",
            "Assets/_Modules/Core/Addressables/HeroTextureLoader.cs"
        };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            foreach (string path in LoaderPaths)
            {
                string source = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                string name = Path.GetFileName(path);
                if (source.Length == 0)
                {
                    failures.Add(name + " source missing");
                    continue;
                }

                // WO-1701 (2026-09-10): the anchor dropped its accessibility keyword - the method went public so
                // HeroContentPrewarmer can reuse it; the contract this lint guards (in-memory locator set, no
                // synchronous wait) is unchanged and still asserted on the body below.
                int method = source.IndexOf("static bool AddressableRegistered", StringComparison.Ordinal);
                int end = method < 0 ? -1 : source.IndexOf("\n        }", method, StringComparison.Ordinal);
                string body = method >= 0 && end > method ? source.Substring(method, end - method) : string.Empty;
                if (body.Length == 0) failures.Add(name + " AddressableRegistered body not found");
                if (body.IndexOf(".WaitForCompletion();", StringComparison.Ordinal) >= 0 ||
                    body.IndexOf("= Addressables.LoadResourceLocationsAsync", StringComparison.Ordinal) >= 0)
                    failures.Add(name + " synchronously waits for catalog initialization");
                if (body.IndexOf("Addressables.ResourceLocators", StringComparison.Ordinal) < 0)
                    failures.Add(name + " does not use the initialized in-memory locator set");

                int loadMethod = source.IndexOf("private static T Load", StringComparison.Ordinal);
                if (loadMethod < 0) loadMethod = source.IndexOf("public static Texture2D Load", StringComparison.Ordinal);
                int resources = loadMethod < 0 ? -1 : source.IndexOf("Resources.Load", loadMethod, StringComparison.Ordinal);
                int addressables = loadMethod < 0 ? -1 : source.IndexOf("Addressables.LoadAssetAsync", loadMethod, StringComparison.Ordinal);
                if (resources < 0 || addressables < 0 || resources > addressables)
                    failures.Add(name + " does not resolve resident Resources before synchronous Addressables");
            }

            if (failures.Count == 0)
            {
                reason = "STARTUP_ASSET_LOADERS_BOUNDED_OK - resident assets resolve first; catalog probes never block";
                return true;
            }
            reason = "AUDIO_STARTUP_BOUNDED_FAIL: " + string.Join(" | ", failures);
            return false;
        }
    }
}
