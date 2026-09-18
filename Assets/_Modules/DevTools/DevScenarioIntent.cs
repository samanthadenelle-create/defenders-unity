// =============================================================================
// DevScenarioIntent — WO-1775. Reads Android launch-intent EXTRAS (namespaced
// "dotr.*") on the QA_SCENARIO_BUILD variant and drives a deterministic
// scenario through the REAL seams — no reflection. DevTools already
// references Core/Village/HUD/Wallet (the documented tooling exception,
// DevPanelController.cs:26-34), so this file calls HeroProgression.AddXp,
// GameStateService, DevSkipKit and SceneRouter directly.
//
// GATE: this whole file compiles ONLY under QA_SCENARIO_BUILD — a define
// stamped ALONGSIDE TESTER_BUILD by overnight-apk-build.ps1 -Scenario, never
// alone and never on a store/Play/Firebase-tester artifact (see
// DeviceScenarioKitRegression). It is a SEPARATE define from TESTER_BUILD on
// purpose (WO-1775 §1.5): this kit MINTS value (resources, hero levels), and
// the owner ruling behind AdminOverlay's WO-1512 two-guard split forbids that
// reaching the APK Firebase testers install.
//
// TWO-PHASE DISPATCH (§1.6.3):
//   PRE-HUB  (GameState only, before the first BeginLoop, applied at Title):
//            newgame, onboarded, wave, resources, buildings, troops, ff.*, tun.*
//   POST-HUB (scene-bound objects, applied once the hub scene + hero exist):
//            level, raid, town
// A build launched with NO scenario extras behaves EXACTLY like a plain
// TESTER_BUILD boot: one FlowTrace.Once saying nothing was requested, no
// state written (acceptance #8).
//
// ⛔ NO BARE RESET/WIPE VERB IS EXPOSED. The only extras this file EVER reads
// are the named keys below — there is no "dotr.reset"/"dotr.wipe"/
// "dotr.deleteall" key, and "newgame" only accepts a value that parses to a
// real HeroClass (knight|ranger|mage|cleric); anything else is refused with a
// FlowTrace.Fail, never silently coerced to a reset. `newgame` is additionally
// fail-closed on PlayerPrefs.HasKey(SaveSchema.PlayerPrefsKey) — a scripted
// reset can never destroy an existing save (§1.6.1).
//
// Camera profile (camera=) is DELIBERATELY NOT implemented — WO-1775 §8 Q2 is
// still open and this file must not guess at an unproven knob.
// =============================================================================
#if QA_SCENARIO_BUILD
using System;
using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.DevTools
{
    internal static class DevScenarioIntent
    {
        private const string Tag = "DevScenario";
        private const string ExtraPrefix = "dotr.";
        private const string FfTunInfix = "ff.tun.";
        private const string FfInfix = "ff.";

        // A parked request never expires silently (§1.6.3) — after this many scene loads
        // with a phase still unmet, that phase Warns by name and the retry for it stops.
        private const int MaxSceneLoadAttempts = 30;

        private sealed class Request
        {
            public string NewGame;
            public bool HasOnboarded; public bool Onboarded;
            public bool HasWave; public int Wave;
            public bool HasLevel; public int Level;
            public bool HasTroops; public int Troops;
            public string Raid;
            public string Town;
            public string Resources;
            public string Buildings;
            public readonly Dictionary<string, int> Ff = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Tun = new Dictionary<string, int>();
            public bool Any;
        }

        private static Request s_parked;
        private static bool s_preHubDone;
        private static bool s_postHubDone;
        private static bool s_subscribed;
        private static int s_sceneLoadAttempts;
        private static bool s_waveLateWarned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_parked != null) return; // idempotent — fires once per process by design
            s_parked = ParseIntent();
            if (!s_parked.Any)
            {
                FlowTrace.Once(Tag, "no-scenario", "[Flow:DevScenario] no scenario requested — normal boot.");
                return;
            }
            FlowTrace.Step(Tag, "[Flow:DevScenario] scenario parsed, applying pre-hub phase.");
            if (!s_subscribed) { SceneManager.sceneLoaded += OnSceneLoaded; s_subscribed = true; }
            TryApplyPreHub();
            TryApplyPostHub();
            MaybeUnsubscribe();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            s_sceneLoadAttempts++;
            TryApplyPreHub();
            TryApplyPostHub();
            MaybeUnsubscribe();
            if (s_sceneLoadAttempts >= MaxSceneLoadAttempts && (!s_preHubDone || !s_postHubDone))
            {
                if (!s_preHubDone)
                    FlowTrace.Warn(Tag, "[Flow:DevScenario] pre-hub phase never found a live GameStateService " +
                        "after " + s_sceneLoadAttempts + " scene load(s) — giving up rather than retrying forever.");
                if (!s_postHubDone)
                    FlowTrace.Warn(Tag, "[Flow:DevScenario] post-hub phase never found a live HeroProgression " +
                        "after " + s_sceneLoadAttempts + " scene load(s) — giving up rather than retrying forever.");
                MaybeUnsubscribe(force: true);
            }
        }

        private static void MaybeUnsubscribe(bool force = false)
        {
            if (!s_subscribed) return;
            if (force || (s_preHubDone && s_postHubDone))
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                s_subscribed = false;
            }
        }

        // ---------------------------------------------------------------
        //  Parse — read-only, no state writes here.
        // ---------------------------------------------------------------
        private static Request ParseIntent()
        {
            var req = new Request();
            Guard.Try(Tag, "read launch intent extras", () =>
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                if (intent == null) return;

                bool Has(string key) => intent.Call<bool>("hasExtra", ExtraPrefix + key);
                string ReadString(string key) => intent.Call<string>("getStringExtra", ExtraPrefix + key);
                int ReadInt(string key) => intent.Call<int>("getIntExtra", ExtraPrefix + key, 0);

                req.NewGame = ReadString("newgame");
                if (Has("onboarded")) { req.HasOnboarded = true; req.Onboarded = ReadInt("onboarded") != 0; }
                if (Has("level"))     { req.HasLevel = true; req.Level = ReadInt("level"); }
                if (Has("wave"))      { req.HasWave = true; req.Wave = ReadInt("wave"); }
                if (Has("troops"))    { req.HasTroops = true; req.Troops = ReadInt("troops"); }
                req.Raid       = ReadString("raid");
                req.Town       = ReadString("town");
                req.Resources  = ReadString("resources");
                req.Buildings  = ReadString("buildings");

                // ff.<key> / ff.tun.<key> — the key NAME is caller-chosen, so these two ride
                // the extras Bundle's own keySet rather than a fixed name list.
                using var extras = intent.Call<AndroidJavaObject>("getExtras");
                if (extras != null)
                {
                    using var keySet = extras.Call<AndroidJavaObject>("keySet");
                    using var iterator = keySet.Call<AndroidJavaObject>("iterator");
                    while (iterator.Call<bool>("hasNext"))
                    {
                        using var keyObj = iterator.Call<AndroidJavaObject>("next");
                        string full = keyObj?.Call<string>("toString");
                        if (string.IsNullOrEmpty(full) || !full.StartsWith(ExtraPrefix, StringComparison.Ordinal))
                            continue;
                        string rest = full.Substring(ExtraPrefix.Length);
                        if (rest.StartsWith(FfTunInfix, StringComparison.Ordinal))
                            req.Tun[rest.Substring(FfTunInfix.Length)] = intent.Call<int>("getIntExtra", full, 0);
                        else if (rest.StartsWith(FfInfix, StringComparison.Ordinal))
                            req.Ff[rest.Substring(FfInfix.Length)] = intent.Call<int>("getIntExtra", full, 0);
                    }
                }
#endif
            });

            req.Any = !string.IsNullOrEmpty(req.NewGame) || req.HasOnboarded || req.HasLevel || req.HasWave ||
                      req.HasTroops || !string.IsNullOrEmpty(req.Raid) || !string.IsNullOrEmpty(req.Town) ||
                      !string.IsNullOrEmpty(req.Resources) || !string.IsNullOrEmpty(req.Buildings) ||
                      req.Ff.Count > 0 || req.Tun.Count > 0;
            return req;
        }

        // ---------------------------------------------------------------
        //  PRE-HUB — GameState only, before the first BeginLoop.
        // ---------------------------------------------------------------
        private static void TryApplyPreHub()
        {
            if (s_preHubDone || s_parked == null) return;
            var svc = GameStateService.Instance;
            if (svc == null) return; // not alive yet at this scene load — retried on the next one

            s_preHubDone = true;
            var req = s_parked;

            if (!string.IsNullOrEmpty(req.NewGame)) ApplyNewGame(svc, req.NewGame);
            if (req.HasOnboarded) ApplyOnboarded(svc, req.Onboarded);
            if (req.HasWave) ApplyWave(svc, req.Wave);
            if (!string.IsNullOrEmpty(req.Resources)) ApplyResources(req.Resources);
            if (!string.IsNullOrEmpty(req.Buildings)) ApplyBuildings(req.Buildings);
            if (req.HasTroops) ApplyTroops(svc, req.Troops);
            foreach (var kv in req.Ff) ApplyFlag(kv.Key, kv.Value);
            foreach (var kv in req.Tun) ApplyTunable(kv.Key, kv.Value);
        }

        // ---------------------------------------------------------------
        //  POST-HUB — scene-bound objects, once the hub + hero exist.
        // ---------------------------------------------------------------
        private static void TryApplyPostHub()
        {
            if (s_postHubDone || s_parked == null) return;
            var hero = UnityEngine.Object.FindAnyObjectByType<HeroProgression>();
            if (hero == null) return; // hub not loaded yet — retried on the next scene load

            // §1.4 trap: if the hub is loading BEFORE GameStateService ever went live pre-hub,
            // a `wave=` write here would land after WaveManager has already resolved its resume
            // seed — a silent no-op. Warn by name instead of applying quietly; still apply the
            // rest of the pre-hub set (better late than never for everything except wave).
            if (!s_preHubDone)
            {
                if (s_parked.HasWave && !s_waveLateWarned)
                {
                    s_waveLateWarned = true;
                    FlowTrace.Warn(Tag, "[Flow:DevScenario] wave=" + s_parked.Wave + " arrived AFTER the hub " +
                        "loaded with no GameStateService seen pre-hub — WaveManager.ResolveStartWave's resume " +
                        "seed is already resolved by now, so writing BestWave here would be a silent no-op " +
                        "(WO-1775 §1.4). Skipped, not applied.");
                }
                TryApplyPreHub();
                if (!s_preHubDone) return;
            }

            s_postHubDone = true;
            var req = s_parked;
            if (req.HasLevel) ApplyLevel(hero, req.Level);
            if (!string.IsNullOrEmpty(req.Town)) ApplyTown(req.Town);
            if (!string.IsNullOrEmpty(req.Raid)) ApplyRaid(req.Raid);
        }

        // ---------------------------------------------------------------
        //  Appliers — each one Guard.Try'd and FlowTrace'd.
        // ---------------------------------------------------------------
        private static void ApplyNewGame(GameStateService svc, string classToken)
        {
            if (UnityEngine.PlayerPrefs.HasKey(SaveSchema.PlayerPrefsKey))
            {
                FlowTrace.Fail(Tag, "[Flow:DevScenario] REFUSED newgame='" + classToken + "' — a save already " +
                    "exists at PlayerPrefs '" + SaveSchema.PlayerPrefsKey + "'. This kit never wipes an " +
                    "existing save (WO-1775 §1.6.1).");
                return;
            }
            if (!Enum.TryParse(classToken, true, out HeroClass cls) || !Enum.IsDefined(typeof(HeroClass), cls))
            {
                FlowTrace.Fail(Tag, "[Flow:DevScenario] REFUSED newgame='" + classToken + "' — not a recognised " +
                    "hero class (knight|ranger|mage|cleric). No bare reset/wipe verb is exposed by this kit.");
                return;
            }
            Guard.Try(Tag, "newgame -> ResetToNewGame + ChooseHero", () =>
            {
                svc.ResetToNewGame();
                svc.ChooseHero(cls);
                FlowTrace.Step(Tag, "[Flow:DevScenario] newgame -> ResetToNewGame() + ChooseHero(" + cls +
                    ") on a save-free target.");
            });
        }

        private static void ApplyOnboarded(GameStateService svc, bool value)
        {
            Guard.Try(Tag, "onboarded", () =>
            {
                if (svc.State == null) { FlowTrace.Warn(Tag, "[Flow:DevScenario] onboarded= requested but State is null."); return; }
                svc.State.Onboarded = value;
                svc.Save();
                FlowTrace.Step(Tag, "[Flow:DevScenario] onboarded -> " + value + " (saved).");
            });
        }

        private static void ApplyWave(GameStateService svc, int wave)
        {
            Guard.Try(Tag, "wave", () =>
            {
                if (svc.State == null) return;
                int target = Mathf.Max(0, wave - 1);
                svc.RecordRun(target);
                FlowTrace.Step(Tag, "[Flow:DevScenario] wave -> RecordRun(" + target + ") so BestWave seeds " +
                    "start wave " + wave + " (WO-1775 §1.4 — must land before the first BeginLoop).");
            });
        }

        private static void ApplyResources(string value)
        {
            Guard.Try(Tag, "resources", () =>
            {
                var eco = DeNelle.Village.EconomyService.Instance;
                if (eco == null)
                {
                    FlowTrace.Warn(Tag, "[Flow:DevScenario] resources= requested but EconomyService is not alive yet.");
                    return;
                }
                int wood = 0, food = 0, iron = 0, crystals = 0;
                bool max = string.Equals(value, "max", StringComparison.OrdinalIgnoreCase);
                if (max) { wood = food = iron = crystals = 50000; }
                else
                {
                    foreach (var pair in value.Split(','))
                    {
                        var kv = pair.Split(':');
                        if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int amount)) continue;
                        switch (kv[0].Trim().ToLowerInvariant())
                        {
                            case "wood": wood = amount; break;
                            case "food": food = amount; break;
                            case "iron": iron = amount; break;
                            case "crystals": crystals = amount; break;
                        }
                    }
                }
                eco.GrantSpendableUncapped(wood, food, iron, crystals);
                if (max) eco.AddCoins(50000);
                FlowTrace.Step(Tag, "[Flow:DevScenario] resources -> wood=" + wood + " food=" + food +
                    " iron=" + iron + " crystals=" + crystals + (max ? " (+50000 coins)" : ""));
            });
        }

        private static void ApplyBuildings(string value)
        {
            if (!string.Equals(value, "max", StringComparison.OrdinalIgnoreCase))
            {
                FlowTrace.Warn(Tag, "[Flow:DevScenario] buildings='" + value + "' not recognised (only 'max' is wired) — skipped.");
                return;
            }
            Guard.Try(Tag, "buildings=max", () =>
            {
                string result = DevSkipKit.PrepCastlePower();
                FlowTrace.Step(Tag, "[Flow:DevScenario] buildings=max -> " + result);
            });
        }

        private static void ApplyTroops(GameStateService svc, int n)
        {
            Guard.Try(Tag, "troops", () =>
            {
                int before = svc.State?.Army?.Owned != null ? svc.State.Army.Owned.Count : 0;
                int after = DevSkipKit.TrainExactly(svc, n);
                svc.Save();
                if (before > n)
                    FlowTrace.Warn(Tag, "[Flow:DevScenario] troops=" + n + " requested but the army already " +
                        "held " + before + " — this kit has no disband verb, left at " + after + ".");
                else if (after < n)
                    FlowTrace.Warn(Tag, "[Flow:DevScenario] troops=" + n + " could not be reached — army holds " +
                        after + " (slots or roster exhausted).");
                FlowTrace.Step(Tag, "[Flow:DevScenario] troops -> TrainExactly(" + n + "), army now " + after + ".");
            });
        }

        private static void ApplyTown(string value)
        {
            if (!string.Equals(value, "granted", StringComparison.OrdinalIgnoreCase))
            {
                FlowTrace.Warn(Tag, "[Flow:DevScenario] town='" + value + "' not recognised (only 'granted' is wired) — skipped.");
                return;
            }
            Guard.Try(Tag, "town=granted", () =>
            {
                string result = DevSkipKit.GrantCapturedTownAndEnter();
                FlowTrace.Step(Tag, "[Flow:DevScenario] town=granted -> " + result);
            });
        }

        private static void ApplyRaid(string sceneId)
        {
            Guard.Try(Tag, "raid", () =>
            {
                SceneRouter.GoRaid(sceneId);
                FlowTrace.Step(Tag, "[Flow:DevScenario] raid -> SceneRouter.GoRaid(" + sceneId + ")");
            });
        }

        private static void ApplyLevel(HeroProgression hero, int target)
        {
            Guard.Try(Tag, "level", () =>
            {
                target = Mathf.Max(1, target);
                int guard = 0;
                while (hero.Level < target && guard++ < 500)
                    hero.AddXp(hero.XpToNext + 1f);

                int wisdom = 0;
                var wis = DeNelle.Village.Talents.WisdomCurrencyService.Instance;
                if (wis != null) wisdom = wis.Wisdom;

                FlowTrace.Step("Hero", "DevScenario set hero -> Lv." + hero.Level + " (target " + target +
                    "), Wisdom " + wisdom);
            });
        }

        private static void ApplyFlag(string key, int value)
        {
            if (string.IsNullOrEmpty(key)) return;
            UnityEngine.PlayerPrefs.SetInt("ff." + key, value);
            UnityEngine.PlayerPrefs.Save();
            FlowTrace.Step(Tag, "[Flow:DevScenario] ff." + key + " -> " + value);
        }

        private static void ApplyTunable(string key, int value)
        {
            if (string.IsNullOrEmpty(key)) return;
            UnityEngine.PlayerPrefs.SetInt(RemoteTunables.LocalPrefix + key, value);
            UnityEngine.PlayerPrefs.Save();
            FlowTrace.Step(Tag, "[Flow:DevScenario] " + RemoteTunables.LocalPrefix + key + " -> " + value);
        }
    }
}
#endif
