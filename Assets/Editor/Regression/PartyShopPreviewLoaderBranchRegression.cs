// =============================================================================
// PartyShopPreviewLoaderBranchRegression — WO-1096 (2026-09-09).
// -----------------------------------------------------------------------------
// PINS the shop preview's loader-branch decision to THE ROW, never to the id prefix.
//
// THE DEFECT THIS EXISTS FOR: PartyShopVM.WeaponLoadsViaAddressable applied the
// retired ARMOR kill-switch (FeatureFlags.BlinkArmor, junked 2026-06-22) to any WEAPON
// id beginning "blink_", and returned BEFORE reading the row's own `loadVia`. So
// blink_shield1h_03 — loadVia="addressable", prefabPath "gear/weapon/Shield1h_03", an
// address that EXISTS in the local Gear group — was routed to the Resources/structure
// loader and rendered a fallback (F8 capture seq=4968, lastTransportUrl=(none): the
// Addressables request was never issued). The owner ratified all 65 blink_ WEAPON rows
// as shelf content (2026-08-14); only Blink ARMOR stays excluded
// (Assets/Resources/Data/Canonical/vendors.json:42 `_excludeIdPrefixesNote`).
//
// WHAT IT ASSERTS (all with ff.blinkarmor forced to a known value, then restored):
//   1. blink_shield1h_03 — the CAPTURED row — resolves ADDRESSABLE with the flag OFF.
//   2. EVERY blink_ weapon row whose loadVia=="addressable" resolves ADDRESSABLE with
//      the flag OFF (the whole ratified shelf, not just the one that got captured).
//   3. A Blink ARMOR row (loadVia=="addressable") stays EXCLUDED with the flag OFF —
//      the armor kill-switch still governs armor.
//   4. That same armor row flips to ADDRESSABLE with the flag ON — proving case 3 is
//      the FLAG doing the work, not a hardcoded exclusion.
//   5. A synthetic weapon row with neither loadVia nor a "gear/" prefabPath resolves
//      NON-addressable — the fallback branch still exists.
//
// Menu / batch: DeNelle.Editor.PartyShopPreviewLoaderBranchRegression.Run
// Marker: SHOP_PREVIEW_LOADER_BRANCH_OK <n>/<n>  |  SHOP_PREVIEW_LOADER_BRANCH_FAIL
// =============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor
{
    public static class PartyShopPreviewLoaderBranchRegression
    {
        private const string MarkerOk   = "SHOP_PREVIEW_LOADER_BRANCH_OK";
        private const string MarkerFail = "SHOP_PREVIEW_LOADER_BRANCH_FAIL";

        private const string FlagKey       = "ff.blinkarmor";
        private const string CapturedId    = "blink_shield1h_03";
        private const string BlinkArmorId  = "blink_armor_centurion";

        [MenuItem("Defenders/Regression/Party Shop Preview Loader Branch (WO-1096)")]
        public static void Run()
        {
            Run(out _);
        }

        /// <summary>Registered-suite entry point (DataRegression.RunAll).</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            int total = 0;
            int ok = 0;

            // Preserve whatever the machine had; -1 == "absent, use the code default".
            int prior = PlayerPrefs.GetInt(FlagKey, -1);

            try
            {
                PlayerPrefs.SetInt(FlagKey, 0);   // Blink ARMOR junked — the shipping default.

                // ── Case 1: the captured row ─────────────────────────────────────────
                total++;
                var captured = GearCatalog.FindWeapon(CapturedId);
                if (captured == null)
                {
                    failures.Add($"case1: weapon row '{CapturedId}' not found in the catalog");
                }
                else if (!string.Equals(captured.loadVia, "addressable", System.StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"case1: '{CapturedId}' no longer declares loadVia=addressable " +
                                 $"(got '{captured.loadVia}') — the WO-1096 fixture moved; re-point this case");
                }
                else if (!PartyShopVM.WeaponLoadsViaAddressable(captured))
                {
                    failures.Add($"case1: '{CapturedId}' (loadVia=addressable, prefabPath='{captured.prefabPath}') " +
                                 "resolved NON-addressable with ff.blinkarmor OFF — the retired armor flag is " +
                                 "vetoing a ratified Blink WEAPON again (WO-1096)");
                }
                else ok++;

                // ── Case 2: the whole ratified Blink weapon shelf ────────────────────
                total++;
                var offenders = new List<string>();
                int blinkAddressableRows = 0;
                var weapons = GearCatalog.AllWeapons();
                if (weapons == null || weapons.Count == 0)
                {
                    failures.Add("case2: weapon catalog empty — cannot prove the shelf");
                }
                else
                {
                    for (int i = 0; i < weapons.Count; i++)
                    {
                        var w = weapons[i];
                        if (w?.id == null) continue;
                        if (!w.id.StartsWith("blink_", System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(w.loadVia, "addressable", System.StringComparison.OrdinalIgnoreCase)) continue;
                        blinkAddressableRows++;
                        if (!PartyShopVM.WeaponLoadsViaAddressable(w)) offenders.Add(w.id);
                    }

                    if (blinkAddressableRows == 0)
                        failures.Add("case2: no blink_ weapon row declares loadVia=addressable — the ratified " +
                                     "shelf (vendors.json:42) has vanished from weapons.json");
                    else if (offenders.Count > 0)
                    {
                        // Built as a plain local: a nested literal inside an interpolation hole
                        // desyncs CompileGate.BraceBalanced's character scanner (CompileGate.cs:896).
                        string offenderList = string.Join(", ", offenders.ToArray());
                        failures.Add($"case2: {offenders.Count}/{blinkAddressableRows} blink_ weapon rows with " +
                                     $"loadVia=addressable resolved NON-addressable: {offenderList}");
                    }
                    else ok++;
                }

                // ── Case 3: Blink ARMOR stays excluded with the flag OFF ─────────────
                total++;
                var armor = GearCatalog.FindArmor(BlinkArmorId);
                if (armor == null)
                {
                    failures.Add($"case3: armor row '{BlinkArmorId}' not found in the catalog");
                }
                else if (!string.Equals(armor.loadVia, "addressable", System.StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"case3: '{BlinkArmorId}' no longer declares loadVia=addressable " +
                                 $"(got '{armor.loadVia}') — this case can no longer tell the flag from the row");
                }
                else if (PartyShopVM.ArmorLoadsViaAddressable(armor))
                {
                    failures.Add($"case3: Blink ARMOR '{BlinkArmorId}' resolved ADDRESSABLE with ff.blinkarmor OFF — " +
                                 "the junked-armor exclusion (pivot 2026-06-22) has been removed");
                }
                else ok++;

                // ── Case 4: the same armor row flips ON with the flag ON ─────────────
                total++;
                PlayerPrefs.SetInt(FlagKey, 1);
                if (armor == null)
                {
                    failures.Add($"case4: armor row '{BlinkArmorId}' not found in the catalog");
                }
                else if (!PartyShopVM.ArmorLoadsViaAddressable(armor))
                {
                    failures.Add($"case4: Blink ARMOR '{BlinkArmorId}' stayed NON-addressable with ff.blinkarmor ON — " +
                                 "case 3 is a hardcoded exclusion, not the flag");
                }
                else ok++;
                PlayerPrefs.SetInt(FlagKey, 0);

                // ── Case 5: the non-addressable branch still exists ──────────────────
                total++;
                var plain = new WeaponDef { id = "wo1096_synthetic_resources_row", loadVia = null, prefabPath = "Heroes/Props/Weapons/sword_A" };
                if (PartyShopVM.WeaponLoadsViaAddressable(plain))
                    failures.Add("case5: a row with no loadVia and a non-gear/ prefabPath resolved ADDRESSABLE — " +
                                 "the Resources branch is unreachable");
                else ok++;
            }
            finally
            {
                if (prior == -1) PlayerPrefs.DeleteKey(FlagKey);
                else PlayerPrefs.SetInt(FlagKey, prior);
                PlayerPrefs.Save();
            }

            if (failures.Count > 0)
            {
                reason = $"{MarkerFail} {ok}/{total} — " + string.Join(" | ", failures.ToArray());
                Debug.LogError($"[{MarkerFail}] {reason}");
                return false;
            }

            reason = $"{MarkerOk} {ok}/{total} — shop preview loader branch decided by the ROW; " +
                     "Blink weapons addressable, Blink armor flag-gated.";
            Debug.Log($"[{MarkerOk}] {reason}");
            return true;
        }
    }
}
