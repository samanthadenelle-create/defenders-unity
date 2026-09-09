// Repeatable localization smoke capture. This is deliberately separate from the large
// UICaptureLaunch suite: one locale failure must be attributable to one stable pair of frames.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DeNelle.Core.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace DeNelle.Editor.Localization
{
    /// <summary>
    /// Captures Settings and the calm-town HUD at the Seeker surface for every locale intended
    /// for the current beta build. The entry point is synchronous so -batchmode -quit cannot
    /// leave before the PNGs and summary have been written.
    /// </summary>
    public static class LocaleSmokeCapture
    {
        private const string TableCollection = "GameStrings";
        private const string EnglishCode = "en";
        private const string OutputDirectory = "Builds/localization-smoke";
        private const int Width = 2670;
        private const int Height = 1200;

        // Deliberately explicit. A stray Locale asset must never silently expand a release gate.
        private static readonly string[] BuildLocales = { "en", "es", "pt-BR", "de", "fr", "ru" };
        private static readonly Regex RichTextTag = new Regex("<[^>]*>", RegexOptions.Compiled);

        [Serializable]
        private sealed class Summary
        {
            public int schemaVersion = 1;
            public int width = Width;
            public int height = Height;
            public string tableCollection = TableCollection;
            public string[] expectedLocales = BuildLocales;
            public List<LocaleResult> locales = new List<LocaleResult>();
            public int screenshotsExpected;
            public int screenshotsWritten;
            public int failures;
            public bool passed;
        }

        [Serializable]
        private sealed class LocaleResult
        {
            public string locale;
            public bool localeRegistered;
            public bool selectedTableLoaded;
            public bool englishTableLoaded;
            public List<SurfaceResult> surfaces = new List<SurfaceResult>();
            public List<string> englishFallbackKeys = new List<string>();
            public List<string> missingMarkers = new List<string>();
            public List<string> missingGlyphs = new List<string>();
            public List<string> errors = new List<string>();
            public bool passed;
        }

        [Serializable]
        private sealed class SurfaceResult
        {
            public string surface;
            public string screenshot;
            public int visibleLabels;
            public bool captured;
        }

        [MenuItem("Defenders/Localization/Capture Locale Smoke Matrix")]
        public static void Run()
        {
            Directory.CreateDirectory(OutputDirectory);
            var summary = new Summary { screenshotsExpected = BuildLocales.Length * 3 };
            Locale priorLocale = null;

            try
            {
                LocalizationSettings initialization =
                    LocalizationSettings.InitializationOperation.WaitForCompletion();
                if (initialization == null)
                    throw new InvalidOperationException("Unity Localization initialization returned null.");

                AsyncOperationHandle<Locale> priorOperation = LocalizationSettings.SelectedLocaleAsync;
                priorLocale = priorOperation.IsDone ? priorOperation.Result : priorOperation.WaitForCompletion();

                foreach (string code in BuildLocales)
                    summary.locales.Add(CaptureLocale(code));
            }
            catch (Exception exception)
            {
                Debug.LogError("[LocaleSmoke] run-level failure: " + exception);
                summary.locales.Add(new LocaleResult
                {
                    locale = "<run>",
                    errors = new List<string> { exception.GetType().Name + ": " + exception.Message },
                    passed = false
                });
            }
            finally
            {
                LocalText.InstallProvider(null);
                if (priorLocale != null)
                    LocalizationSettings.SelectedLocale = priorLocale;
                ElarionUiKit.ClearSurfaceOverride();
            }

            foreach (LocaleResult locale in summary.locales)
            {
                summary.screenshotsWritten += locale.surfaces.Count(surface => surface.captured);
                if (!locale.passed) summary.failures++;
            }
            summary.passed = summary.failures == 0 &&
                             summary.screenshotsWritten == summary.screenshotsExpected;

            string summaryPath = Path.Combine(OutputDirectory, "summary.json").Replace('\\', '/');
            File.WriteAllText(summaryPath, JsonUtility.ToJson(summary, true) + Environment.NewLine);

            if (summary.passed)
            {
                Debug.Log("LOCALIZATION_SMOKE_OK locales=" + BuildLocales.Length +
                          " screenshots=" + summary.screenshotsWritten + "/" + summary.screenshotsExpected +
                          " summary=" + Path.GetFullPath(summaryPath));
                return;
            }

            string message = "LOCALIZATION_SMOKE_FAIL localeFailures=" + summary.failures +
                             " screenshots=" + summary.screenshotsWritten + "/" + summary.screenshotsExpected +
                             " summary=" + Path.GetFullPath(summaryPath);
            Debug.LogError(message);
            throw new BuildFailedException(message);
        }

        private static LocaleResult CaptureLocale(string code)
        {
            var result = new LocaleResult { locale = code };
            TableProvider provider = null;

            try
            {
                Locale locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(code));
                result.localeRegistered = locale != null;
                if (locale == null)
                {
                    result.errors.Add("Locale is not registered in LocalizationSettings.AvailableLocales.");
                    return Finish(result, provider);
                }

                Locale english = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(EnglishCode));
                if (english == null)
                {
                    result.errors.Add("Required English locale is not registered.");
                    return Finish(result, provider);
                }

                LocalizationSettings.SelectedLocale = locale;
                StringTable selected = LoadTable(locale, result, "selected");
                StringTable englishTable = string.Equals(code, EnglishCode, StringComparison.OrdinalIgnoreCase)
                    ? selected
                    : LoadTable(english, result, "English");
                result.selectedTableLoaded = TableMatches(selected, code);
                result.englishTableLoaded = TableMatches(englishTable, EnglishCode);
                if (selected != null && !result.selectedTableLoaded)
                    result.errors.Add("Selected table resolved as '" + selected.LocaleIdentifier.Code +
                                      "' instead of '" + code + "' (automatic fallback is not a locale pass).");
                if (englishTable != null && !result.englishTableLoaded)
                    result.errors.Add("English fallback table resolved as '" + englishTable.LocaleIdentifier.Code + "'.");
                if (selected == null || englishTable == null)
                    return Finish(result, provider);
                if (!result.selectedTableLoaded || !result.englishTableLoaded)
                    return Finish(result, provider);

                provider = new TableProvider(code, selected, englishTable);
                LocalText.InstallProvider(provider);
                ElarionUiKit.SetSurfaceOverride(Width, Height);

                result.surfaces.AddRange(CaptureSettings(code, result));
                result.surfaces.Add(CaptureHud(code, result));
            }
            catch (Exception exception)
            {
                result.errors.Add(exception.GetType().Name + ": " + exception.Message);
                Debug.LogError("[LocaleSmoke] " + code + " threw: " + exception);
            }
            finally
            {
                ElarionUiKit.ClearSurfaceOverride();
            }

            return Finish(result, provider);
        }

        private static LocaleResult Finish(LocaleResult result, TableProvider provider)
        {
            if (provider != null)
                result.englishFallbackKeys.AddRange(provider.EnglishFallbackKeys.OrderBy(key => key, StringComparer.Ordinal));
            result.missingMarkers.Sort(StringComparer.Ordinal);
            result.missingGlyphs.Sort(StringComparer.Ordinal);
            result.errors.Sort(StringComparer.Ordinal);

            // A translated smoke surface using English fallback is incomplete even when it is readable.
            if (!string.Equals(result.locale, EnglishCode, StringComparison.OrdinalIgnoreCase) &&
                result.englishFallbackKeys.Count > 0)
                result.errors.Add("Selected table fell back to English for visible UI keys.");

            result.passed = result.localeRegistered && result.selectedTableLoaded && result.englishTableLoaded &&
                            result.surfaces.Count == 3 && result.surfaces.All(surface => surface.captured) &&
                            result.missingMarkers.Count == 0 && result.missingGlyphs.Count == 0 &&
                            result.errors.Count == 0;
            Debug.Log((result.passed ? "[LocaleSmoke] PASS " : "[LocaleSmoke] FAIL ") + result.locale +
                      " frames=" + result.surfaces.Count(surface => surface.captured) + "/3" +
                      " labels=" + result.surfaces.Sum(surface => surface.visibleLabels) +
                      " fallbackKeys=" + result.englishFallbackKeys.Count +
                      " missingMarkers=" + result.missingMarkers.Count +
                      " missingGlyphs=" + result.missingGlyphs.Count +
                      " errors=" + result.errors.Count);
            return result;
        }

        private static StringTable LoadTable(Locale locale, LocaleResult result, string label)
        {
            try
            {
                return LocalizationSettings.StringDatabase
                    .GetTableAsync(TableCollection, locale).WaitForCompletion();
            }
            catch (Exception exception)
            {
                result.errors.Add(label + " table load failed: " + exception.Message);
                return null;
            }
        }

        private static bool TableMatches(StringTable table, string code)
        {
            return table != null && string.Equals(table.LocaleIdentifier.Code, code,
                StringComparison.OrdinalIgnoreCase);
        }

        private static List<SurfaceResult> CaptureSettings(string code, LocaleResult localeResult)
        {
            var surfaces = new List<SurfaceResult>();
            GameObject eventSystem = null;
            GameObject host = null;
            GameObject canvasObject = null;
            Component controller = null;
            try
            {
                EnsureEventSystem(ref eventSystem);
                Type type = ResolveType("DeNelle.Settings.SettingsController");
                if (type == null) throw new InvalidOperationException("SettingsController type was not found.");
                host = new GameObject("~LocaleSmokeSettings");
                controller = host.AddComponent(type);
                Invoke(controller, "EnsureBuilt");
                object modal = GetField(controller, "_modal");
                canvasObject = modal == null ? null : ReadField(modal, "canvas") as GameObject;
                if (canvasObject == null) throw new InvalidOperationException("Settings canvas was not built.");
                canvasObject.SetActive(true);
                ScrollRect scroll = canvasObject.GetComponentInChildren<ScrollRect>(true);
                if (scroll == null)
                    throw new InvalidOperationException("Settings ScrollRect was not built.");

                scroll.verticalNormalizedPosition = 1f;
                surfaces.Add(RenderSurface(code, "settings", canvasObject, localeResult));

                // The language controls live below the first viewport. A dedicated,
                // deterministic scroll capture proves the selector/Beta copy instead
                // of calling a top-of-list frame the complete Settings surface.
                // At the Seeker landscape reference size this centers the Language
                // caption and both locale controls, with neighboring sections visible
                // so overlaps at either edge remain obvious in the proof frame.
                scroll.verticalNormalizedPosition = 0.67f;
                surfaces.Add(RenderSurface(code, "settings_language", canvasObject, localeResult));
                return surfaces;
            }
            finally
            {
                if (controller != null) SetField(controller, "_modal", null);
                Destroy(canvasObject);
                Destroy(host);
                Destroy(eventSystem);
            }
        }

        private static SurfaceResult CaptureHud(string code, LocaleResult localeResult)
        {
            GameObject eventSystem = null;
            GameObject ownerObject = null;
            GameObject hudObject = null;
            object hudModel = null;
            Type coreServices = null;
            try
            {
                EnsureEventSystem(ref eventSystem);
                Type ownerType = ResolveType("DeNelle.HUD.VillageHudController");
                Type kitType = ResolveType("DeNelle.HUD.Kit.HudKitController");
                Type postureType = ResolveType("DeNelle.HUD.Kit.HudPosture");
                coreServices = ResolveType("DeNelle.Core.CoreServices");
                Type hudModelType = ResolveType("DeNelle.Core.HudModel.HudModel");
                if (ownerType == null || kitType == null || postureType == null ||
                    coreServices == null || hudModelType == null)
                    throw new InvalidOperationException("Adaptive HUD fixture types were not found.");

                hudModel = Activator.CreateInstance(hudModelType);
                coreServices.GetMethod("RegisterHudModel", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, new[] { hudModel });

                ownerObject = new GameObject("~LocaleSmokeHudOwner");
                Component owner = ownerObject.AddComponent(ownerType);
                Component kit = kitType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, new object[] { owner }) as Component;
                if (kit == null) throw new InvalidOperationException("HudKitController.Create returned null.");
                hudObject = kit.gameObject;

                object models = GetField(kit, "_models");
                object wave = models?.GetType().GetProperty("Wave")?.GetValue(models);
                MethodInfo setWave = wave?.GetType().GetMethod("Set", BindingFlags.Public | BindingFlags.Instance);
                Type wavePhase = wave?.GetType().GetProperty("Phase")?.PropertyType;
                object vitals = models?.GetType().GetProperty("HeroVitals")?.GetValue(models);
                vitals?.GetType().GetMethod("Set")?.Invoke(vitals,
                    new object[] { 92, 120, 38, 60, 240, 500, 2, "knight", 0, 38f, 60f, "Focus" });
                object economy = models?.GetType().GetProperty("Economy")?.GetValue(models);
                economy?.GetType().GetMethod("Set")?.Invoke(economy, new object[] { 227, 480, 320, 260, 18 });
                object world = models?.GetType().GetProperty("World")?.GetValue(models);
                world?.GetType().GetMethod("SetMetrics")?.Invoke(world,
                    new object[] { 840, 1000, 0.84f, 4, 12, 6, 3.5f, 2, 0, 2, 4, string.Empty });

                if (setWave != null && wavePhase != null)
                    setWave.Invoke(wave, new object[]
                    {
                        Enum.Parse(wavePhase, "Countdown"), 2, 20, 849f, true, "", 18, 18, ""
                    });
                kitType.GetMethod("SetStartWaveAvailable", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(kit, new object[] { true });
                MethodInfo apply = kitType.GetMethod("ApplyPosture", BindingFlags.NonPublic | BindingFlags.Instance);
                if (apply == null) throw new InvalidOperationException("HUD ApplyPosture was not found.");
                apply.Invoke(kit, new[] { Enum.Parse(postureType, "CalmTown") });

                foreach (Transform transform in hudObject.GetComponentsInChildren<Transform>(true))
                    if (transform.name == "Widget_heartStatus") transform.gameObject.SetActive(true);

                var dockLabels = GetField(kit, "_peacefulDockLabels") as TMP_Text[];
                string[] dockKeys =
                {
                    HudStrings.KeyNavBuild, HudStrings.KeyNavTalk, HudStrings.KeyNavHero,
                    HudStrings.KeyNavJourney, HudStrings.KeyNavManage,
                };
                if (dockLabels == null || dockLabels.Length != dockKeys.Length)
                {
                    localeResult.errors.Add("hud: localized calm-dock label handles were not built.");
                }
                else
                {
                    var actual = new string[dockKeys.Length];
                    for (int i = 0; i < dockKeys.Length; i++)
                    {
                        actual[i] = dockLabels[i] != null ? dockLabels[i].text : string.Empty;
                        string expected = HudStrings.Get(dockKeys[i]);
                        if (!string.Equals(actual[i], expected, StringComparison.Ordinal))
                            localeResult.errors.Add("hud: dock label " + i + " is '" + actual[i] +
                                                    "', expected locale key " + dockKeys[i] + " = '" + expected + "'.");
                    }
                    if (!string.Equals(code, EnglishCode, StringComparison.OrdinalIgnoreCase) &&
                        actual.SequenceEqual(new[] { "BUILD", "TALK", "HERO", "JOURNEY", "MANAGE" }))
                        localeResult.errors.Add("hud: translated locale retained every English calm-dock caption.");
                }

                var collector = GetField(kit, "_collectorsChipLabel") as TMP_Text;
                string collectorExpected = HudStrings.Get(HudStrings.KeyCollectorsTitle);
                if (collector == null || !string.Equals(collector.text, collectorExpected, StringComparison.Ordinal))
                    localeResult.errors.Add("hud: Collectors startup seed did not resolve hudCollectorsTitle.");

                object heartPlateObject = GetField(kit, "_heartPlate");
                if (!(heartPlateObject is ElarionUiKit.PartyNameplateHandle heartPlate) ||
                    heartPlate.NameLabel == null ||
                    !string.Equals(heartPlate.NameLabel.text, HeartHudText.Title.Resolve(), StringComparison.Ordinal))
                    localeResult.errors.Add("hud: Heart title did not resolve hud.heart.title.");

                var heartObjective = GetField(kit, "_heartObjectiveLabel") as TMP_Text;
                var armySnapshot = DeNelle.Core.HudModel.HudActionBarModel.Shared.ArmySnapshot;
                string objectiveExpected = DeNelle.Core.HudModel.HeartObjectiveCopy.Resolve(
                    false, DeNelle.Core.HudModel.PostureSignals.RaidCapable,
                    DeNelle.Core.HudModel.PostureSignals.RaidLock, armySnapshot, out _);
                if (heartObjective == null ||
                    !string.Equals(heartObjective.text, objectiveExpected, StringComparison.Ordinal))
                    localeResult.errors.Add("hud: Heart objective did not resolve the selected locale/state.");

                var heartfireLabel = GetField(kit, "_heartfireLabel") as TMP_Text;
                var rekindleLabel = GetField(kit, "_heartfireRekindleLabel") as TMP_Text;
                int paintedLit = Convert.ToInt32(GetField(kit, "_heartfireLitPainted"));
                int paintedMax = Convert.ToInt32(GetField(kit, "_heartfireMaxPainted"));
                long paintedSeconds = Convert.ToInt64(GetField(kit, "_heartfireSecondsPainted"));
                string heartfireExpected = DeNelle.Core.State.HeartfireCharges.PlateLabel(paintedLit, paintedMax);
                string rekindleExpected = DeNelle.Core.State.HeartfireCharges.PlateRekindle(
                    paintedLit, paintedMax, paintedSeconds);
                if (heartfireLabel == null ||
                    !string.Equals(heartfireLabel.text, heartfireExpected, StringComparison.Ordinal))
                    localeResult.errors.Add("hud: Heartfire count row did not resolve the selected locale.");
                if (rekindleLabel == null ||
                    !string.Equals(rekindleLabel.text, rekindleExpected, StringComparison.Ordinal))
                    localeResult.errors.Add("hud: Heartfire timer row did not resolve the selected locale.");

                var fleeLabel = GetField(kit, "_fleeLabel") as TMP_Text;
                if (fleeLabel == null ||
                    !string.Equals(fleeLabel.text, CombatHudText.ResolveFlee(false), StringComparison.Ordinal))
                    localeResult.errors.Add("hud: initial Flee action did not resolve the selected locale.");
                MethodInfo fleeTap = kitType.GetMethod("OnFleeTapped", BindingFlags.NonPublic | BindingFlags.Instance);
                fleeTap?.Invoke(kit, null);
                if (fleeLabel == null ||
                    !string.Equals(fleeLabel.text, CombatHudText.ResolveFlee(true), StringComparison.Ordinal))
                    localeResult.errors.Add("hud: armed Flee confirmation did not resolve the selected locale.");
                kitType.GetMethod("RefreshLocalizedHudCopy", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(kit, null);
                if (fleeLabel == null ||
                    !string.Equals(fleeLabel.text, CombatHudText.ResolveFlee(true), StringComparison.Ordinal))
                    localeResult.errors.Add("hud: locale refresh lost the armed Flee meaning.");
                return RenderSurface(code, "hud", hudObject, localeResult);
            }
            finally
            {
                Destroy(hudObject);
                Destroy(ownerObject);
                Destroy(eventSystem);
                if (coreServices != null && hudModel != null)
                    coreServices.GetMethod("UnregisterHudModel", BindingFlags.Public | BindingFlags.Static)
                        ?.Invoke(null, new[] { hudModel });
                PanelManager.CloseAll();
            }
        }

        private static SurfaceResult RenderSurface(
            string code, string surface, GameObject canvasObject, LocaleResult localeResult)
        {
            string relativePath = (OutputDirectory + "/" + code + "_" + surface + "_" +
                                   Width + "x" + Height + ".png").Replace('\\', '/');
            var result = new SurfaceResult { surface = surface, screenshot = relativePath };
            Canvas canvas = canvasObject == null ? null : canvasObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                localeResult.errors.Add(surface + ": root Canvas was not found.");
                return result;
            }

            RenderMode priorMode = canvas.renderMode;
            Camera priorCamera = canvas.worldCamera;
            float priorPlane = canvas.planeDistance;
            RenderTexture priorActive = RenderTexture.active;
            GameObject cameraObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            try
            {
                cameraObject = new GameObject("~LocaleSmokeCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 1000f;
                camera.cullingMask = ~0;
                renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                renderTexture.Create();
                camera.targetTexture = renderTexture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                ApplyScreenSpaceScale(canvas);

                for (int pass = 0; pass < 2; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    RectTransform root = canvasObject.GetComponent<RectTransform>();
                    if (root != null) LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                    foreach (TMP_Text label in canvasObject.GetComponentsInChildren<TMP_Text>(true))
                        if (label != null) label.ForceMeshUpdate();
                    Canvas.ForceUpdateCanvases();
                }

                InspectVisibleText(code, surface, canvasObject, localeResult, result);
                var request = new RenderPipeline.StandardRequest { destination = renderTexture };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) camera.SubmitRenderRequest(request);
                else camera.Render();

                RenderTexture.active = renderTexture;
                texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                texture.Apply(false);
                if (IsBlank(texture))
                {
                    localeResult.errors.Add(surface + ": render was blank and was not written.");
                    return result;
                }

                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    localeResult.errors.Add(surface + ": PNG encoder returned no bytes.");
                    return result;
                }
                File.WriteAllBytes(relativePath, png);
                result.captured = true;
                return result;
            }
            catch (Exception exception)
            {
                localeResult.errors.Add(surface + " capture failed: " + exception.Message);
                return result;
            }
            finally
            {
                RenderTexture.active = priorActive;
                canvas.renderMode = priorMode;
                canvas.worldCamera = priorCamera;
                canvas.planeDistance = priorPlane;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                Destroy(cameraObject);
            }
        }

        private static void InspectVisibleText(
            string code, string surface, GameObject root, LocaleResult localeResult, SurfaceResult surfaceResult)
        {
            var markers = new HashSet<string>(StringComparer.Ordinal);
            var glyphs = new HashSet<string>(StringComparer.Ordinal);
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label == null || !label.gameObject.activeInHierarchy || !label.enabled ||
                    string.IsNullOrEmpty(label.text)) continue;
                surfaceResult.visibleLabels++;
                string plain = RichTextTag.Replace(label.text, string.Empty);
                if (plain.IndexOf("[[missing:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    plain.IndexOf("No translation found for", StringComparison.OrdinalIgnoreCase) >= 0)
                    markers.Add(code + "/" + surface + "/" + label.gameObject.name + ": " + plain);

                TMP_FontAsset font = label.font;
                if (font == null)
                {
                    glyphs.Add(code + "/" + surface + "/" + label.gameObject.name + ": <no font>");
                    continue;
                }
                foreach (char character in plain)
                {
                    if (char.IsWhiteSpace(character) || char.IsControl(character)) continue;
                    if (char.IsSurrogate(character) || !font.HasCharacter(character, true, false))
                        glyphs.Add(code + "/" + surface + "/U+" + ((int)character).ToString("X4"));
                }
            }
            localeResult.missingMarkers.AddRange(markers.Where(value => !localeResult.missingMarkers.Contains(value)));
            localeResult.missingGlyphs.AddRange(glyphs.Where(value => !localeResult.missingGlyphs.Contains(value)));
        }

        private static bool IsBlank(Texture2D texture)
        {
            var buckets = new HashSet<int>();
            int colored = 0;
            int samples = 0;
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i += 13)
            {
                Color32 pixel = pixels[i];
                int bucket = (pixel.r >> 4) << 8 | (pixel.g >> 4) << 4 | (pixel.b >> 4);
                buckets.Add(bucket);
                if (pixel.r > 12 || pixel.g > 12 || pixel.b > 12) colored++;
                samples++;
            }
            return buckets.Count < 3 || samples == 0 || colored / (float)samples < 0.002f;
        }

        private static void ApplyScreenSpaceScale(Canvas canvas)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) return;
            Vector2 reference = scaler.referenceResolution;
            float refWidth = reference.x > 1f ? reference.x : 1080f;
            float refHeight = reference.y > 1f ? reference.y : 1920f;
            float logWidth = Mathf.Log(Width / refWidth, 2f);
            float logHeight = Mathf.Log(Height / refHeight, 2f);
            canvas.scaleFactor = Mathf.Pow(2f,
                Mathf.Lerp(logWidth, logHeight, Mathf.Clamp01(scaler.matchWidthOrHeight)));
            canvas.referencePixelsPerUnit = scaler.referencePixelsPerUnit > 0f
                ? scaler.referencePixelsPerUnit : 100f;
        }

        private static void EnsureEventSystem(ref GameObject temporary)
        {
            if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            temporary = new GameObject("~LocaleSmokeEventSystem");
            temporary.AddComponent<UnityEngine.EventSystems.EventSystem>();
        }

        private static Type ResolveType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target?.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null) throw new MissingMethodException(target?.GetType().FullName, method);
            info.Invoke(target, null);
        }

        private static object GetField(object target, string name) => ReadField(target, name);

        private static object ReadField(object target, string name)
        {
            return target?.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target?.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value != null) UnityEngine.Object.DestroyImmediate(value);
        }

        private sealed class TableProvider : ILocalTextProvider
        {
            private readonly StringTable _selected;
            private readonly StringTable _english;
            private readonly List<LocaleOption> _locales;

            public TableProvider(string code, StringTable selected, StringTable english)
            {
                CurrentLocaleCode = code;
                _selected = selected;
                _english = english;
                // Match the production provider: every currently enabled non-English
                // locale is an early-access translation and must carry the Beta label.
                _locales = BuildLocales.Select(locale => new LocaleOption(
                    locale,
                    locale,
                    !string.Equals(locale, EnglishCode, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            public HashSet<string> EnglishFallbackKeys { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public bool IsReady => true;
            public bool UsesSystemLocale => false;
            public string CurrentLocaleCode { get; }
            public IReadOnlyList<LocaleOption> AvailableLocales => _locales;
            public event Action Changed { add { } remove { } }

            public bool TryResolve(string key, object[] args, out string value)
            {
                if (Resolve(_selected, key, args, out value)) return true;
                if (!ReferenceEquals(_selected, _english) && Resolve(_english, key, args, out value))
                {
                    EnglishFallbackKeys.Add(key);
                    return true;
                }
                return false;
            }

            public bool TrySelectLocale(string code) => false;
            public void UseSystemLocale() { }

            private static bool Resolve(StringTable table, string key, object[] args, out string value)
            {
                value = null;
                StringTableEntry entry = table?.GetEntry(key);
                if (entry == null || string.IsNullOrEmpty(entry.Value)) return false;
                try
                {
                    value = args == null || args.Length == 0
                        ? entry.GetLocalizedString()
                        : entry.GetLocalizedString(args);
                    return !string.IsNullOrEmpty(value);
                }
                catch
                {
                    value = null;
                    return false;
                }
            }
        }
    }
}
