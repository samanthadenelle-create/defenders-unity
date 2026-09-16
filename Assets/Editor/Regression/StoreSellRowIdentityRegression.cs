// =============================================================================
// StoreSellRowIdentityRegression [store-sell-row] (WO-1584)
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// Pins the three defects the owner's 2026-09-07 Seeker frame showed on the vendor
// Store panel, SELL side (Logs/device/seeker-shots/Screenshot_20260907-075931.png):
// a white "*" where the item art belongs, a row painted with no label, and a detail
// column naming an item the list did not light.
//
//   1 [sell-rows-labelled]   Through the REAL PartyShopVM at the Market (Goods layout,
//                            SELL tab) over a seeded VillageInventory: every row carries
//                            a non-empty Id AND a non-empty Name that ends in the stack
//                            count, and every row has a detail payload. A row without a
//                            label is unreadable and paints as the blank plate the owner
//                            reported; the VM now refuses to emit one and this case is
//                            what keeps that true.
//
//   2 [material-art-key]     Every MATERIAL/GEM row carries the art keys the ONE material
//                            seam needs: detail.IconPath == the catalog's authored
//                            iconPath, and detail.IconCategory == the catalog's authored
//                            category. Then the seam itself is exercised -
//                            ItemIconCatalog.ForMaterial(id, iconPath, category) - across
//                            the WHOLE material catalog, not just the seeded rows.
//                            FAILURES are: an authored iconPath that does not load (a
//                            broken content path), or a row that carries no category at
//                            all. A material whose category the sheets do not cover is
//                            NOT a failure - it is reported by name as an ART ASK, because
//                            missing art is content work, not a code defect. The store
//                            View must call ForMaterial, never the potion keyword mapper:
//                            "Iron Scrap" / "HealthHerb" / "Oil Flask" all keyword-match
//                            potion rows, which is how a material came to wear a health
//                            bottle (F8-641) and, unmatched, the bare "*" glyph.
//
//   3 [selected-is-highlighted] SelectedId is the ONE truth. For EVERY row: Select(id)
//                            leaves SelectedId, SelectedItem, Selected (the detail) and
//                            IndexOfRow all naming the SAME id - so the lit row and the
//                            detail column can never disagree about WHICH item. Paired
//                            with a source lint that the View still highlights from
//                            _vm.SelectedId and still scrolls that row into view, since
//                            what actually broke was a lit row sitting OUTSIDE a
//                            1.5-row-tall viewport, not a wrong id.
//
//   4 [sell-detail-filled]   A goods SELL row reads out real facts, not a name over a
//                            stock sentence: a non-empty description, and spec lines for
//                            Category, In pack and Sells for. (Owner ruling 29 - the
//                            screen is filled, with context - applies to the detail column.)
//
// Markers: STORE_SELL_ROW_OK / STORE_SELL_ROW_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.StoreSellRowIdentityRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Village;
using DeNelle.Village.Crafting;
using DeNelle.Village.Hero;
using DeNelle.Village.Items;

namespace DeNelle.Editor.Regression
{
    public static class StoreSellRowIdentityRegression
    {
        private const string VendorId = "market";          // vendors.json: layout "goods"
        private const string ViewPath = "Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs";

        // The seeded pack: a metal scrap with NO authored icon (the owner's "*" row), a petal
        // WITH authored icon art, and a legacy PascalCase herb - three different art paths.
        private static readonly (string id, int qty)[] Pack =
        {
            ("IronScrap",         43),
            ("ing_elarion_petal",  3),
            ("HealthHerb",         7),
            ("ing_moonbloom",      2),
        };

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("STORE_SELL_ROW_OK - " + reason);
            else Debug.LogError("STORE_SELL_ROW_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            var artAsks = new List<string>();
            GameObject larder = null;
            VillageInventory previous = VillageInventory.Instance;

            try
            {
                MaterialCatalog.Reload();

                larder = SeedLarder(failures);
                if (larder != null)
                {
                    var vm = OpenSellShop();
                    try
                    {
                        CaseRowsLabelled(vm, failures, notes);
                        CaseMaterialArtKeys(vm, failures, notes, artAsks);
                        CaseSelectedIsHighlighted(vm, failures, notes);
                        CaseDetailFilled(vm, failures, notes);
                    }
                    finally { vm.Dispose(); }
                }

                CaseCatalogArtSweep(failures, notes, artAsks);
                CaseViewSelectionSeam(failures, notes);
            }
            catch (Exception e)
            {
                failures.Add("[store-sell-row] threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                RestoreLarder(larder, previous);
            }

            if (artAsks.Count > 0)
                notes.Add("ART ASK (" + artAsks.Count + " material id(s) resolve NO sprite and fall back to their " +
                          "authored glyph - content work, not a code defect): " + string.Join(", ", artAsks));

            reason = failures.Count == 0
                ? string.Join(" | ", notes)
                : string.Join(" | ", failures);
            return failures.Count == 0;
        }

        // -- Case 1 -------------------------------------------------------------
        private static void CaseRowsLabelled(PartyShopVM vm, List<string> failures, List<string> notes)
        {
            var items = vm.Items;
            if (items == null || items.Count < 3)
            {
                failures.Add("[sell-rows-labelled] the Market SELL shelf built " +
                             (items == null ? "<null>" : items.Count.ToString()) +
                             " row(s) from a pack holding " + Pack.Length +
                             " sellable stacks - the shelf, not the labels, is the first problem");
                return;
            }

            int checkedRows = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (string.IsNullOrEmpty(it.Id))
                {
                    failures.Add("[sell-rows-labelled] SELL row " + i + " has an EMPTY id - it can be painted but " +
                                 "never selected or sold");
                    continue;
                }
                if (string.IsNullOrEmpty(it.Name))
                {
                    failures.Add("[sell-rows-labelled] SELL row " + i + " (id '" + it.Id + "') has an EMPTY label - " +
                                 "this is the blank framed row on the owner's 2026-09-07 Seeker frame");
                    continue;
                }
                if (!Regex.IsMatch(it.Name, @"\sx\d+$"))
                    failures.Add("[sell-rows-labelled] SELL row '" + it.Id + "' label \"" + it.Name +
                                 "\" does not end in its stack count - the player cannot see how many they hold");
                if (vm.DetailFor(it.Id) == null)
                    failures.Add("[sell-rows-labelled] SELL row '" + it.Id + "' has NO detail payload, so the detail " +
                                 "column blanks the moment it is selected");
                checkedRows++;
            }
            notes.Add("[sell-rows-labelled] " + checkedRows + " Market SELL row(s), every one labelled + counted");
        }

        // -- Case 2 -------------------------------------------------------------
        private static void CaseMaterialArtKeys(PartyShopVM vm, List<string> failures, List<string> notes,
                                                List<string> artAsks)
        {
            int material = 0;
            foreach (var it in vm.Items)
            {
                var def = MaterialCatalog.Find(it.Id);
                if (def == null) continue;                 // a consumable row - Case 2b sweeps materials
                var detail = vm.DetailFor(it.Id);
                if (detail == null) continue;              // Case 1 owns the missing-detail failure

                material++;
                var d = detail.Value;

                if (!string.Equals(d.IconPath ?? "", def.IconPath ?? "", StringComparison.Ordinal))
                    failures.Add("[material-art-key] SELL row '" + it.Id + "' carries iconPath '" +
                                 (d.IconPath ?? "<null>") + "' but materials.json authors '" +
                                 (def.IconPath ?? "<null>") + "'");

                if (string.IsNullOrEmpty(d.IconCategory))
                    failures.Add("[material-art-key] SELL row '" + it.Id + "' carries NO icon category, so the View " +
                                 "cannot call ItemIconCatalog.ForMaterial and falls through to the role glyph - " +
                                 "this is the white '*' over 'Iron Scrap x43'");
                else if (!string.Equals(d.IconCategory, def.Category, StringComparison.Ordinal))
                    failures.Add("[material-art-key] SELL row '" + it.Id + "' carries category '" + d.IconCategory +
                                 "' but materials.json authors '" + (def.Category ?? "<null>") + "'");

                if (string.IsNullOrEmpty(d.Glyph) && !string.IsNullOrEmpty(def.Glyph))
                    failures.Add("[material-art-key] SELL row '" + it.Id + "' drops the authored glyph '" + def.Glyph +
                                 "' - an art miss then paints the coarse role glyph instead of the row's own");
            }

            if (material == 0)
                failures.Add("[material-art-key] the seeded pack produced NO material rows at all - the fixture, or " +
                             "the Market's sell band, stopped reaching materials.json");
            else
                notes.Add("[material-art-key] " + material + " material SELL row(s) carry authored iconPath + category");
        }

        // -- Case 2b: the seam itself, across the whole catalog -----------------
        private static void CaseCatalogArtSweep(List<string> failures, List<string> notes, List<string> artAsks)
        {
            var all = MaterialCatalog.All;
            if (all == null || all.Count == 0)
            {
                failures.Add("[material-art-key] materials.json deserialized to 0 MaterialDef objects");
                return;
            }

            int resolved = 0, authored = 0;
            foreach (var m in all)
            {
                if (m == null || string.IsNullOrEmpty(m.Id)) continue;

                if (string.IsNullOrEmpty(m.Category))
                {
                    failures.Add("[material-art-key] material '" + m.Id + "' authors NO category, so the ONE material " +
                                 "art seam has nothing to resolve from and the row can only ever show a glyph");
                    continue;
                }

                if (!string.IsNullOrEmpty(m.IconPath))
                {
                    authored++;
                    if (Resources.Load<Sprite>(m.IconPath) == null)
                    {
                        failures.Add("[material-art-key] material '" + m.Id + "' authors iconPath '" + m.IconPath +
                                     "' but nothing loads there - a broken content path, not missing art");
                        continue;
                    }
                }

                var sprite = ItemIconCatalog.ForMaterial(m.Id, m.IconPath, m.Category);
                if (sprite != null) resolved++;
                else artAsks.Add(m.Id + " (category '" + m.Category + "')");
            }

            notes.Add("[material-art-key] ForMaterial resolves art for " + resolved + "/" + all.Count +
                      " material(s); " + authored + " carry authored icons; " + artAsks.Count + " ART ASK");
        }

        // -- Case 3 -------------------------------------------------------------
        private static void CaseSelectedIsHighlighted(PartyShopVM vm, List<string> failures, List<string> notes)
        {
            int probed = 0;
            var ids = new List<string>();
            foreach (var it in vm.Items) if (!string.IsNullOrEmpty(it.Id)) ids.Add(it.Id);

            foreach (var id in ids)
            {
                vm.Select(id);
                probed++;

                if (!string.Equals(vm.SelectedId, id, StringComparison.Ordinal))
                {
                    failures.Add("[selected-is-highlighted] Select('" + id + "') left SelectedId '" +
                                 (vm.SelectedId ?? "<null>") + "'");
                    continue;
                }
                if (vm.IndexOfRow(vm.SelectedId) < 0)
                    failures.Add("[selected-is-highlighted] SelectedId '" + id + "' is not in the built row list, so " +
                                 "no row can carry the highlight the detail column is describing");

                var item = vm.SelectedItem;
                if (!item.HasValue || !string.Equals(item.Value.Id, id, StringComparison.Ordinal))
                    failures.Add("[selected-is-highlighted] SelectedItem for '" + id + "' resolved '" +
                                 (item.HasValue ? item.Value.Id : "<null>") + "'");

                var detail = vm.Selected;
                if (!detail.HasValue || !string.Equals(detail.Value.IconName, id, StringComparison.Ordinal))
                    failures.Add("[selected-is-highlighted] the detail payload for '" + id + "' names '" +
                                 (detail.HasValue ? detail.Value.IconName : "<null>") +
                                 "' - the detail column and the lit row would describe DIFFERENT items");
            }
            notes.Add("[selected-is-highlighted] " + probed + " row(s): SelectedId, SelectedItem, Selected and " +
                      "IndexOfRow all agree");
        }

        // -- Case 3b: the View half of the same law (source lint) ---------------
        private static void CaseViewSelectionSeam(List<string> failures, List<string> notes)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), ViewPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                failures.Add("[selected-is-highlighted] " + ViewPath + " not found - the View half cannot be pinned");
                return;
            }
            string body = File.ReadAllText(path);

            if (!Regex.IsMatch(body, @"_rowPlates\[i\]\.id\s*==\s*sel"))
                failures.Add("[selected-is-highlighted] PartyShopPanelMvvm no longer highlights the row whose id equals " +
                             "the VM's SelectedId - the View must never keep a second idea of what is selected");
            if (!Regex.IsMatch(body, @"ScrollSelectedIntoView\(\);"))
                failures.Add("[selected-is-highlighted] PartyShopPanelMvvm no longer scrolls the SELECTED row into view. " +
                             "The 2026-09-07 defect was a lit row sitting OUTSIDE the viewport, so the detail column " +
                             "named an item no visible row was lit for");
            if (!Regex.IsMatch(body, @"ReseatColumns\(\);"))
                failures.Add("[selected-is-highlighted] PartyShopPanelMvvm no longer re-seats its columns to the height " +
                             "actually free above them - the fixed 0.36-0.525 band could not hold two rows");
            if (!Regex.IsMatch(body, @"ItemIconCatalog\.ForMaterial\("))
                failures.Add("[material-art-key] PartyShopPanelMvvm no longer routes material/gem art through " +
                             "ItemIconCatalog.ForMaterial - the potion keyword mapper is not the material seam");

            notes.Add("[selected-is-highlighted] View pins present: highlight-from-SelectedId, scroll-into-view, " +
                      "adaptive columns, ForMaterial routing");
        }

        // -- Case 4 -------------------------------------------------------------
        private static void CaseDetailFilled(PartyShopVM vm, List<string> failures, List<string> notes)
        {
            int filled = 0;
            foreach (var it in vm.Items)
            {
                if (MaterialCatalog.Find(it.Id) == null) continue;
                var detail = vm.DetailFor(it.Id);
                if (detail == null) continue;

                if (string.IsNullOrEmpty(detail.Value.Description))
                    failures.Add("[sell-detail-filled] SELL row '" + it.Id + "' has no description line");

                var specs = vm.SpecsFor(it.Id);
                bool hasCategory = false, hasOwned = false, hasValue = false;
                if (specs != null)
                    foreach (var s in specs)
                    {
                        if (s.Label == "Category")  hasCategory = true;
                        if (s.Label == "In pack")   hasOwned = true;
                        if (s.Label == "Sells for") hasValue = true;
                    }

                if (!hasCategory) failures.Add("[sell-detail-filled] SELL row '" + it.Id + "' reads out no Category line");
                if (!hasOwned)    failures.Add("[sell-detail-filled] SELL row '" + it.Id + "' reads out no In-pack count");
                if (!hasValue)    failures.Add("[sell-detail-filled] SELL row '" + it.Id + "' reads out no Sells-for value");
                filled++;
            }
            notes.Add("[sell-detail-filled] " + filled + " material SELL row(s) read out category + count + sell value");
        }

        // -- Fixture ------------------------------------------------------------

        // The goods SELL shelf reads VillageInventory.Instance directly (it is the player's pack,
        // not the gear store), and edit-mode never runs Awake - so the singleton is seated here by
        // reflection over its backing field and put back in RestoreLarder. Nothing else in the
        // suite touches it.
        private static GameObject SeedLarder(List<string> failures)
        {
            var go = new GameObject("[WO1584 Larder]");
            go.hideFlags = HideFlags.HideAndDontSave;
            var inv = go.AddComponent<VillageInventory>();

            if (!TrySetInstance(inv))
            {
                failures.Add("[store-sell-row] could not seat a test VillageInventory - the SELL shelf reads the " +
                             "singleton directly, so the fixture cannot run");
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }

            foreach (var (id, qty) in Pack) inv.Add(id, qty);
            return go;
        }

        private static void RestoreLarder(GameObject go, VillageInventory previous)
        {
            TrySetInstance(previous);
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        private static bool TrySetInstance(VillageInventory value)
        {
            var prop = typeof(VillageInventory).GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var setter = prop != null ? prop.GetSetMethod(nonPublic: true) : null;
            if (setter != null) { setter.Invoke(null, new object[] { value }); return true; }

            var field = typeof(VillageInventory).GetField("<Instance>k__BackingField",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field != null) { field.SetValue(null, value); return true; }
            return false;
        }

        private static PartyShopVM OpenSellShop()
        {
            var members = new IEquipTarget[] { new FakeEquipTarget("knight") };
            return new PartyShopVM(VendorId, new FakeEconomy(), new FakeStore(),
                                   members, new[] { 1 },
                                   displayName: "Market Stalls", onClose: null,
                                   lockedTab: PartyShopTab.Sell);
        }

        private sealed class FakeEconomy : IEconomy
        {
            public int Coins => 999999;
            public int Wood => 999999;
            public int Iron => 999999;
            public int Stone => 999999;
            public int Crystals => 999999;
            public bool CanAfford(ResourceCost cost) => true;
            public bool TrySpend(ResourceCost cost) => true;
            public ResourceCost Grant(ResourceCost amount) => default(ResourceCost);
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
            public IReadOnlyList<(WeaponDef def, int qty)> OwnedWeapons() => Array.Empty<(WeaponDef, int)>();
            public IReadOnlyList<(ArmorDef def, int qty)> OwnedArmor() => Array.Empty<(ArmorDef, int)>();
            public IReadOnlyList<(string id, int qty)> OwnedConsumables() => Array.Empty<(string, int)>();
            public bool WeaponFitsClass(WeaponDef w, string job) => GearCatalog.WeaponFitsClass(w, job);
            public bool ArmorFitsClass(ArmorDef a, string job) => GearCatalog.ArmorFitsClass(a, job);
            public bool TryRemove(string id, int n) => false;
        }

        private sealed class FakeEquipTarget : IEquipTarget
        {
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
            public void EquipWeaponById(string id) { }
            public void EquipArmorById(string id) { }
            public void UnequipWeapon() { }
            public void UnequipArmor() { }
            public void EquipOffHandById(string id) { }
            public void UnequipOffHand() { }
            public void EquipAccessoryById(string id) { }
            public void UnequipAccessory(string slot) { }
        }
    }
}
