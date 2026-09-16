// =============================================================================
// ArmorStoreLockedWindowRegression [armor-store-window] (WO-960)
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// Pins the owner's 2026-08-10 armor-store ruling ("we need more armor, only 3
// options in store ... display as greyed out with lvl and only show ones in the
// next 5 levels"):
//
//   1 [window-data]        lockedPreviewLevels is DATA on the armorer row, > 0,
//                          present in BOTH vendors.json copies (byte-identical
//                          modulo line endings), and VendorRegistry parses it to
//                          the same value (loader/schema-drift catch).
//   2 [window-visible-set] For a knight at levels 1/3/4/6/8/10, the armorer's
//                          Resolve output EQUALS an independent oracle built here
//                          straight from armor.json via GearCatalog: visible set
//                          == unlocked (req <= N) UNION locked-within-(N, N+W],
//                          class-appropriate + non-excluded only, bucketed by
//                          req.level, defense DESC, id ordinal ASC, perLevelCap
//                          per bucket, levels ascending. Every locked ware must
//                          read "Requires Lv <req>"; anything deeper than N+W
//                          must be ABSENT. The WO acceptance verbatim.
//   3 [locked-never-purchasable] Through the REAL PartyShopVM with fake economy/
//                          store/member seams: a locked card is Locked + not
//                          Affordable, tapping it (Act) spends NOTHING and equips
//                          NOTHING, and the status explains the unlock in words
//                          ("unlocks at Lv N") - word+shape, never tint alone.
//
// Markers: ARMOR_WINDOW_OK / ARMOR_WINDOW_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.ArmorStoreLockedWindowRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor.Regression
{
    public static class ArmorStoreLockedWindowRegression
    {
        private const string VendorsRes = "Assets/Resources/Data/Canonical/vendors.json";
        private const string VendorsSA = "Assets/StreamingAssets/Data/Canonical/vendors.json";
        private const string VendorId = "armorer";
        private const string Job = "knight";

        // Pinned roster: the suite must not depend on ff.knightonly / PlayableHeroes drift.
        private static readonly string[] Roster = { "knight", "ranger", "mage" };
        private static readonly int[] Levels = { 1, 3, 4, 6, 8, 10 };

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("ARMOR_WINDOW_OK - " + reason);
            else Debug.LogError("ARMOR_WINDOW_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                GearCatalog.Reload();
                VendorRegistry.Reload();
                Case1_WindowData(failures);
                Case2_WindowVisibleSet(failures, notes);
                Case3_LockedNeverPurchasable(failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add($"[armor-store-window] unexpected {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count > 0) { reason = string.Join(" | ", failures); return false; }
            reason = "armorer locked-preview window verified (" + string.Join("; ", notes) + ")";
            return true;
        }

        // =====================================================================
        //  CASE 1 - the knob is DATA, in both copies, and the loader reads it
        // =====================================================================
        private static void Case1_WindowData(List<string> failures)
        {
            if (!File.Exists(VendorsRes)) { failures.Add("[window-data] missing " + VendorsRes); return; }
            if (!File.Exists(VendorsSA)) { failures.Add("[window-data] missing " + VendorsSA); return; }

            string res = File.ReadAllText(VendorsRes);
            string sa = File.ReadAllText(VendorsSA);
            if (res.Replace("\r\n", "\n") != sa.Replace("\r\n", "\n"))
                failures.Add("[window-data] vendors.json DRIFT between Resources and StreamingAssets - " +
                             "editor and device would disagree about the armor shelf");

            JObject root;
            try { root = JObject.Parse(res); }
            catch (Exception ex)
            {
                failures.Add($"[window-data] vendors.json failed to parse ({ex.GetType().Name}: {ex.Message})");
                return;
            }

            JObject row = null;
            if (root["vendors"] is JArray arr)
                foreach (var v in arr)
                    if (string.Equals((string)v["id"], VendorId, StringComparison.OrdinalIgnoreCase))
                    { row = (JObject)v; break; }
            if (row == null) { failures.Add("[window-data] vendors.json has no '" + VendorId + "' row"); return; }

            if (row["lockedPreviewLevels"] == null)
            {
                failures.Add("[window-data] '" + VendorId + "' has no 'lockedPreviewLevels' in vendors.json - " +
                             "the WO-960 window can no longer be retuned as data");
                return;
            }
            int jsonWindow = (int)row["lockedPreviewLevels"];
            if (jsonWindow <= 0)
                failures.Add($"[window-data] '{VendorId}'.lockedPreviewLevels = {jsonWindow} - a non-positive " +
                             "window means the owner's greyed ladder is OFF");

            var def = VendorRegistry.Find(VendorId);
            if (def == null)
                failures.Add("[window-data] VendorRegistry.Find('" + VendorId + "') returned null despite the JSON row");
            else if (def.LockedPreviewLevels != jsonWindow)
                failures.Add($"[window-data] loader drift: JSON lockedPreviewLevels={jsonWindow} but " +
                             $"VendorRegistry parsed {def.LockedPreviewLevels}");
        }

        // =====================================================================
        //  CASE 2 - visible set == unlocked UNION locked-within-(N, N+W]
        // =====================================================================
        private static void Case2_WindowVisibleSet(List<string> failures, List<string> notes)
        {
            var vendor = VendorRegistry.Find(VendorId);
            if (vendor == null || vendor.LockedPreviewLevels <= 0) return;   // Case 1 reports the real problem
            int window = vendor.LockedPreviewLevels;

            int lockedTotal = 0;
            foreach (int level in Levels)
            {
                var wares = VendorStockResolver.Resolve(VendorId, Job, level, Roster);
                if (wares == null)
                {
                    failures.Add($"[window-visible-set] Resolve('{VendorId}', {Job}, {level}) returned null");
                    continue;
                }

                // The independent oracle - built straight from the catalog, deliberately NOT
                // calling the resolver's private helpers, so it can DISAGREE rather than
                // inherit a bug. Mirrors the documented EmitCapped sort.
                var expected = Oracle(vendor, level, window);

                var actual = new List<(string id, bool eligible)>();
                foreach (var ware in wares)
                {
                    if (ware.Kind != VendorWareKind.Armor) continue;
                    actual.Add((ware.Id, ware.Eligible));

                    var a = GearCatalog.FindArmor(ware.Id);
                    int req = a != null && a.req != null ? a.req.level : 1;
                    if (!ware.Eligible)
                    {
                        lockedTotal++;
                        if (req <= level || req > level + window)
                            failures.Add($"[window-visible-set] Lv{level}: locked '{ware.Id}' req {req} is OUTSIDE " +
                                         $"the ({level}, {level + window}] window");
                        string want = "Requires Lv " + req;
                        if (!string.Equals(ware.LockReason, want, StringComparison.Ordinal))
                            failures.Add($"[window-visible-set] Lv{level}: locked '{ware.Id}' reason is " +
                                         $"'{ware.LockReason}', expected '{want}' - the card's 'Lv N' hint and the " +
                                         "tap explanation both derive from it");
                    }
                    else if (req > level)
                    {
                        failures.Add($"[window-visible-set] Lv{level}: '{ware.Id}' req {req} shipped ELIGIBLE " +
                                     "above the shopper's level - the purchase gate is open on a locked item");
                    }
                }

                if (Join(actual) != Join(expected))
                    failures.Add($"[window-visible-set] Lv{level}: shelf is [{Join(actual)}] but the oracle says " +
                                 $"[{Join(expected)}] - visible set must be unlocked UNION locked-within-" +
                                 $"({level}, {level + window}], capped {vendor.PerLevelCap}/level, defense DESC, " +
                                 "id ordinal ASC");
            }

            // The ruling exists to SHOW the ladder: if no tested level produces a single
            // locked preview row, the window is dead data and the store is back to 3 rows.
            if (lockedTotal == 0)
                failures.Add("[window-visible-set] no level in " + string.Join(",", Levels) + " produced a locked " +
                             "preview row - the greyed ladder the owner asked for is not rendering from this data");
            notes.Add($"locked preview rows across levels = {lockedTotal}");
        }

        private static List<(string id, bool eligible)> Oracle(VendorDef vendor, int level, int window)
        {
            var candidates = new List<(ArmorDef def, int req, bool eligible)>();
            foreach (var a in GearCatalog.AllArmors())
            {
                if (a == null || string.IsNullOrEmpty(a.id)) continue;
                if (!VendorStockResolver.ArmorRosterObtainable(a, Roster)) continue;
                if (IsExcluded(a.id, vendor.ExcludeIdPrefixes)) continue;
                if (!GearCatalog.ArmorFitsClass(a, Job)) continue;   // class-appropriate ladder only
                int req = a.req != null ? a.req.level : 1;
                if (vendor.MaxReqLevel > 0 && req > vendor.MaxReqLevel) continue;
                if (req <= level) candidates.Add((a, req, true));                       // unlocked slice
                else if (req <= level + window) candidates.Add((a, req, false));       // locked preview
                // deeper than level + window: hidden
            }

            candidates.Sort((x, y) =>
            {
                int byLevel = x.req.CompareTo(y.req);
                if (byLevel != 0) return byLevel;
                int byPower = y.def.defense.CompareTo(x.def.defense);
                if (byPower != 0) return byPower;
                return string.CompareOrdinal(x.def.id, y.def.id);
            });

            var expected = new List<(string, bool)>();
            var perLevel = new Dictionary<int, int>();
            foreach (var c in candidates)
            {
                perLevel.TryGetValue(c.req, out int n);
                if (vendor.PerLevelCap > 0 && n >= vendor.PerLevelCap) continue;
                perLevel[c.req] = n + 1;
                expected.Add((c.def.id, c.eligible));
            }
            return expected;
        }

        private static bool IsExcluded(string id, IReadOnlyList<string> prefixes)
        {
            if (prefixes == null) return false;
            foreach (var p in prefixes)
                if (!string.IsNullOrEmpty(p) && id.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string Join(List<(string id, bool eligible)> rows)
        {
            var parts = new List<string>(rows.Count);
            foreach (var r in rows) parts.Add(r.id + (r.eligible ? "" : "(locked)"));
            return string.Join(",", parts);
        }

        // =====================================================================
        //  CASE 3 - a locked card can never be purchased (real VM, fake seams)
        // =====================================================================
        private static void Case3_LockedNeverPurchasable(List<string> failures, List<string> notes)
        {
            var economy = new FakeEconomy();
            var store = new FakeStore();
            var member = new FakeEquipTarget(Job);
            var vm = new PartyShopVM(VendorId, economy, store,
                new IEquipTarget[] { member }, new[] { 1 });
            try
            {
                ItemVM? locked = null;
                foreach (var item in vm.Items)
                    if (item.Locked) { locked = item; break; }
                if (locked == null)
                {
                    failures.Add("[locked-never-purchasable] the armorer VM shows NO locked row for a Lv1 knight - " +
                                 "the preview window is not reaching the shop screen");
                    return;
                }

                var card = locked.Value;
                if (card.Affordable)
                    failures.Add($"[locked-never-purchasable] locked '{card.Id}' is flagged Affordable - the View " +
                                 "would tint its price as buyable");
                if (string.IsNullOrEmpty(card.LockReason))
                    failures.Add($"[locked-never-purchasable] locked '{card.Id}' carries no LockReason - the card " +
                                 "has no 'Lv N' text and grey tint alone is banned (colourblind owner)");

                vm.Act(card.Id);   // the tap - the ONLY purchase seam a row exposes

                if (economy.SpendCalls > 0)
                    failures.Add($"[locked-never-purchasable] tapping locked '{card.Id}' called TrySpend " +
                                 $"{economy.SpendCalls}x - a locked card just took money");
                if (member.EquipCalls > 0)
                    failures.Add($"[locked-never-purchasable] tapping locked '{card.Id}' equipped gear " +
                                 $"{member.EquipCalls}x - a locked card must be fully inert");
                string status = vm.Status ?? "";
                bool explained =
                    status.IndexOf("unlocks at Lv ", StringComparison.Ordinal) >= 0 ||
                    (!string.IsNullOrEmpty(card.LockReason) &&
                     status.IndexOf(card.LockReason, StringComparison.Ordinal) >= 0);
                if (!explained)
                    failures.Add($"[locked-never-purchasable] tapping locked '{card.Id}' explained nothing - " +
                                 $"status was '{status}', expected the unlock level in words");

                notes.Add($"locked tap on '{card.Id}' -> '{status}', 0 spends");
            }
            finally
            {
                vm.Dispose();
            }
        }

        // -- fakes: minimal, honest implementations of the VM's three seams -------

        private sealed class FakeEconomy : IEconomy
        {
            public int SpendCalls;
            public int Coins => 999999;
            public int Wood => 999999;
            public int Iron => 999999;
            public int Stone => 999999;
            public int Crystals => 999999;
            public bool CanAfford(ResourceCost cost) => true;
            public bool TrySpend(ResourceCost cost) { SpendCalls++; return true; }
            // Fixed-balance fake: it banks NOTHING, so the applied basket is empty, not the request.
            public ResourceCost Grant(ResourceCost amount) { return default(ResourceCost); }
            public event Action<ResourceSnapshot> OnChanged { add { } remove { } }
        }

        private sealed class FakeStore : IInventoryStore
        {
            private static readonly Dictionary<string, int> Empty = new Dictionary<string, int>();
            public event Action Changed { add { } remove { } }
            public IReadOnlyDictionary<string, int> OwnedCounts => Empty;
            public int OwnedQuantity(string id) => 0;
            public WeaponDef FindWeapon(string id) => GearCatalog.FindWeapon(id);
            public ArmorDef FindArmor(string id) => GearCatalog.FindArmor(id);
            public AccessoryDef FindAccessory(string id) => GearCatalog.FindAccessory(id);
            public IReadOnlyList<AccessoryDef> AccessoriesForSlot(string slot, int level) =>
                Array.Empty<AccessoryDef>();
            public IReadOnlyList<(WeaponDef def, int qty)> OwnedWeapons() =>
                Array.Empty<(WeaponDef, int)>();
            public IReadOnlyList<(ArmorDef def, int qty)> OwnedArmor() =>
                Array.Empty<(ArmorDef, int)>();
            public IReadOnlyList<(string id, int qty)> OwnedConsumables() =>
                Array.Empty<(string, int)>();
            public bool WeaponFitsClass(WeaponDef w, string job) => GearCatalog.WeaponFitsClass(w, job);
            public bool ArmorFitsClass(ArmorDef a, string job) => GearCatalog.ArmorFitsClass(a, job);
            public bool TryRemove(string id, int n) => false;
        }

        private sealed class FakeEquipTarget : IEquipTarget
        {
            public int EquipCalls;
            private readonly string _class;
            public FakeEquipTarget(string cls) { _class = cls; }
            public string TargetName => "TestKnight";
            public string TargetClass => _class;
            public int TargetLevel => 1;
            public string EquippedWeaponName => null;
            public string EquippedArmorName => null;
            public WeaponDef EquippedWeapon => null;
            public ArmorDef EquippedArmor => null;
            public WeaponDef EquippedOffHand => null;
            public AccessoryDef EquippedRing => null;
            public AccessoryDef EquippedAmulet => null;
            public float WeaponMult => 1f;
            public float ArmorDefense => 0f;
            public float CurrentHealth => 0f;
            public float MaxHealth => 0f;
            public float CurrentMana => 0f;
            public float MaxMana => 0f;
            public event Action EquipChanged { add { } remove { } }
            public void EquipWeaponById(string id) => EquipCalls++;
            public void EquipArmorById(string id) => EquipCalls++;
            public void UnequipWeapon() { }
            public void UnequipArmor() { }
            public void EquipOffHandById(string id) => EquipCalls++;
            public void UnequipOffHand() { }
            public void EquipAccessoryById(string id) => EquipCalls++;
            public void UnequipAccessory(string slot) { }
        }
    }
}
