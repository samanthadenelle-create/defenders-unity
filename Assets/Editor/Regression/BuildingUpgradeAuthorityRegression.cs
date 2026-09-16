// =============================================================================
// BuildingUpgradeAuthorityRegression [upgrade-authority] -- proves a city upgrade
// writes the ONE authoritative store (GameState.BuildingTiers), not the legacy
// per-building PlayerPrefs pool.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Core).
// Installs a throwaway GameStateService, funds the GameState wallet, then drives the
// REAL BuildingUpgradeService.TryUpgrade("forge", tier) and asserts:
//   * GameState.BuildingTiers["forge"] advanced to the target tier (the SSOT), AND
//   * the legacy PlayerPrefs key "dotr.resbuilding.level.forge" is UNCHANGED (the
//     divergent per-building store the city-tier authority must not touch).
//
// Marker: UPGRADE_AUTHORITY_OK / UPGRADE_AUTHORITY_FAIL. Expected: GREEN.
//
// Wire (DataRegression.RunAll):
//   if (!BuildingUpgradeAuthorityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[upgrade-authority] " + r);
// =============================================================================
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Village.Buildings.Progression;
using DeNelle.Core.State;

namespace DeNelle.Editor
{
    public static class BuildingUpgradeAuthorityRegression
    {
        private const string SaveKey = "dotr-save";
        private const string LegacyPrefKey = "dotr.resbuilding.level.forge";
        private const string TimerFlagKey = "ff.buildtimers";
        private const string BuildingId = "forge";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- BUILDING UPGRADE AUTHORITY (TryUpgrade -> GameState.BuildingTiers, legacy PlayerPrefs untouched) ---");

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;
            int priorTimerFlag = PlayerPrefs.GetInt(TimerFlagKey, -1);
            bool hadLegacy = PlayerPrefs.HasKey(LegacyPrefKey);
            int legacyBefore = PlayerPrefs.GetInt(LegacyPrefKey, int.MinValue);

            GameStateService priorGss = GameStateService.Instance;
            GameObject gssGo = null;
            GameState throwaway = null;
            try
            {
                PlayerPrefs.SetInt(TimerFlagKey, 0);   // deterministic instant-apply (no build-timer path)

                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (upgrade-authority oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "UPGRADE AUTHORITY", "GameStateService state seam not reflectable (needs fleet)");
                }

                // Fund the GameState wallet generously (Wood/Iron top-level; Food/Crystals in Resources),
                // and lift any village-tier gate so the only variable under test is the write target.
                throwaway.Wood = 9_000_000;
                throwaway.Iron = 9_000_000;
                var bal = throwaway.Resources; bal.Stone = 9_000_000; bal.Crystals = 9_000_000;
                bal.Coins = 9_000_000; throwaway.Resources = bal;
                throwaway.VillageTier = 99;

                int current = ModifierService.TierOf(BuildingId);
                int target = current + 1;
                if (BuildingTierCatalog.TierOf(BuildingId, target) == null)
                {
                    failures.Add($"[upgrade-authority] building-tiers.json has no tier {target} for '{BuildingId}' -- the city-tier authority cannot advance it (catalog gap)");
                    reason = Finish(failures, log);
                    return failures.Count == 0;
                }

                bool ok = BuildingUpgradeService.TryUpgrade(BuildingId, target);
                if (!ok)
                    failures.Add($"[upgrade-authority] TryUpgrade('{BuildingId}', {target}) returned false at a funded wallet + lifted gate (the city upgrade did not execute)");

                int tierNow = throwaway.BuildingTiers != null && throwaway.BuildingTiers.TryGetValue(BuildingId, out var t) ? t : -1;
                log.AppendLine($"  BuildingTiers['{BuildingId}'] {current} -> {tierNow} (target {target})");
                if (tierNow != target)
                    failures.Add($"[upgrade-authority] GameState.BuildingTiers['{BuildingId}']={tierNow}, expected {target} -- the authoritative tier store did NOT advance");

                // The legacy per-building PlayerPrefs store must be untouched by the city-tier path.
                bool legacyNow = PlayerPrefs.HasKey(LegacyPrefKey);
                int legacyAfter = PlayerPrefs.GetInt(LegacyPrefKey, int.MinValue);
                log.AppendLine($"  legacy '{LegacyPrefKey}': existed={hadLegacy}->{legacyNow}, value={legacyBefore}->{legacyAfter}");
                if (legacyNow != hadLegacy || legacyAfter != legacyBefore)
                    failures.Add($"[upgrade-authority] the legacy '{LegacyPrefKey}' PlayerPrefs changed (existed {hadLegacy}->{legacyNow}, value {legacyBefore}->{legacyAfter}) -- a city upgrade must write BuildingTiers ONLY, not the divergent per-building pool");
            }
            catch (System.Exception ex)
            {
                failures.Add($"upgrade-authority oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (gssGo != null) Object.DestroyImmediate(gssGo);
                if (throwaway != null) Object.DestroyImmediate(throwaway);
                SetGssInstance(priorGss);
                if (priorTimerFlag == -1) PlayerPrefs.DeleteKey(TimerFlagKey); else PlayerPrefs.SetInt(TimerFlagKey, priorTimerFlag);
                // Restore the legacy pref exactly (the oracle never intends to write it; guard anyway).
                if (hadLegacy) PlayerPrefs.SetInt(LegacyPrefKey, legacyBefore); else PlayerPrefs.DeleteKey(LegacyPrefKey);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave); else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        private static bool InstallState(GameStateService svc, GameState state)
        {
            var f = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(svc, state);
            return SetGssInstance(svc);
        }

        private static bool SetGssInstance(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "UPGRADE_AUTHORITY_OK");
                return "UPGRADE AUTHORITY OK -- TryUpgrade advanced GameState.BuildingTiers and left the legacy per-building PlayerPrefs untouched";
            }
            string reason = "upgrade-authority: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "UPGRADE_AUTHORITY_FAIL: " + reason);
            return reason;
        }
    }
}
