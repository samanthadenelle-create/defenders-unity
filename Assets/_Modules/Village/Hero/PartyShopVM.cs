// =============================================================================
// PartyShopVM - the PARTY weapon/armor shop's pure ViewModel (docs/STORE_EQUIP_SPEC.md).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hero
//
// THE COMPLETE store-to-spec ViewModel that replaced the broken ShopVM/ShopPanel
// experience (two sell bars, no party selection, blank icons). ⛔ That pair was DELETED
// on 2026-09-06 (WO-1430 Group A) once PanelDoorRegression proved no production file
// opened it, so this is now the ONLY gear shop - there is no legacy twin to fall back to
// and FeatureFlags.PartyShop is a kill-switch, not a chooser. It composes the
// PROVEN MVVM seams already in the repo - IEconomy (wallet/spend), IInventoryStore
// (owned set + gear defs + fit-by-class), and IEquipTarget (per-member loadout +
// equip/unequip) - into ONE unified shop:
//
//   1. PARTY SELECTOR. Holds the party (hero + companions) as IEquipTarget members
//      (id/name/class/portrait). SelectMember(i) re-filters. Default = the active hero.
//   2. TAP -> FILTER. The row list = gear the SELECTED member can equip: weapons by
//      `job` (WeaponFitsClass); armor by `ArmorFitsClass` (weight/class); both by
//      `req.level <= member level`; the store TYPE (weapon shop / armor shop) is the
//      VendorStockContract gate over the catalog.
//   3. ONE action per row, SINGLE TAP. BUY (spend + add to inventory + auto-equip to
//      the selected member), or EQUIP (already owned), or SELL (in the SAME screen -
//      a Sell tab; selling owned gear credits coins so the player can afford new gear
//      WITHOUT leaving). EXACTLY one buy button per row; NO duplicate buy/sell bars.
//   4. REAL ITEM IMAGE. Each row carries the iconPath (the rendered sprite key); the
//      View resolves the actual sprite (fallback to a category glyph if null).
//   5. DETAILS + BUFFS. Each row carries a stat line + the delta vs the member's
//      currently-equipped piece ("+X% def", "+Y reach", "= equipped") so the player
//      makes an informed decision.
//
// PURE: NO UnityEngine UI types (no GameObject/Image/Sprite/RectTransform/Color).
// Icons are carried as KEYS on ItemVM (IconRole/IconName) + a parallel detail map; the
// View resolves the real sprite. Math uses System.Math, never UnityEngine.Mathf, so the
// VM is unit-testable without a scene (ARCHITECTURE_PRINCIPLES.md ?2 / ?2c; ui-mvvm-binding-seam).
//
// Implements DeNelle.Core.UI.Mvvm.IPanelViewModel: the View binds it, re-renders on
// Changed, routes input back as commands, and never reads game state.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Core.Catalog;       // ShoppableCraftable (the craftable row payload from the resolver)
using DeNelle.Village.Crafting;   // VillageInventory (for the buy -> add path through the store seam)

namespace DeNelle.Village.Hero
{
    /// <summary>The two shop screens: BUY (catalog the member can equip) + SELL (owned gear, for coins).</summary>
    public enum PartyShopTab { Buy, Sell }

    /// <summary>
    /// The category selector for the shop list (the "dropdown selections" - owner 2026-06-23).
    /// A gear shop combines weapons + armor into one flat list; with the catalog holding hundreds
    /// of weapons this drowns out armor, so the player needs a category narrow. ALL shows the
    /// armor-then-weapons combined list (armor/weapons-first, STORE_EQUIP_SPEC); WEAPONS / ARMOR
    /// narrow to one kind. Applies to BOTH the BUY and SELL lists.
    /// </summary>
    public enum PartyShopCategory { All, Weapons, OffHand, Armor }

    /// <summary>
    /// The finer weapon/armor TYPE sub-filter (WO-501 owner point 1 - narrow ON TOP of the
    /// hero-fit + category list). Read PURELY from data already on the def (no schema change):
    /// OneHand -> WeaponDef.IsOneHandedMain, TwoHand -> IsTwoHanded, Shield -> IsOffHandItem,
    /// Light/Heavy -> ArmorDef.weight. Any = no narrow. Applies to BOTH the BUY and SELL lists.
    /// </summary>
    public enum PartyShopType { Any, OneHand, TwoHand, Shield, Light, Heavy }

    /// <summary>
    /// Detail payload for one shop row - the stat line + the "why it's better" delta vs the
    /// selected member's currently-equipped piece, plus the icon KEYS the View resolves to art.
    /// Carried by the VM so the View renders the row purely from data (no state-pull).
    /// </summary>
    public readonly struct PartyShopDetail
    {
        /// <summary>Stat line, e.g. "+18% dmg   reach 2.5m" or "+12% def   +30 hp".</summary>
        public readonly string Stats;
        /// <summary>Delta vs the member's equipped piece, e.g. "+6% dmg vs equipped" / "= equipped" / "".</summary>
        public readonly string Delta;
        /// <summary>One-line description (rarity + class fit + flavour).</summary>
        public readonly string Description;
        /// <summary>iconPath (the rendered item sprite key), or null - the View falls back to a category glyph.</summary>
        public readonly string IconPath;
        /// <summary>Coarse icon role for the View's glyph fallback ("weapon" / "armor").</summary>
        public readonly string IconRole;
        /// <summary>Item id (the View resolves the catalog sprite from this when IconPath is null).</summary>
        public readonly string IconName;
        /// <summary>
        /// WO-1584 - the row's AUTHORED art category (materials.json `category`: metal / herb /
        /// crystal / cloth / ...), or null for rows that are not materials. This is the missing
        /// third argument of the ONE material-art seam the repo already owns,
        /// <c>ItemIconCatalog.ForMaterial(id, iconPath, category)</c> (added by F8-641 precisely so a
        /// material never borrows a potion's art). The store View could not call it because the VM
        /// carried no category, so every material fell through
        /// <c>ForConsumable</c> to the ROLE glyph - the white "*" over "Iron Scrap x43" on the
        /// owner's 2026-09-07 Seeker frame. The VM produces the key; the View resolves it. Adding a
        /// second resolver in the View would be the banned fix.
        /// </summary>
        public readonly string IconCategory;
        /// <summary>
        /// The row's AUTHORED ASCII glyph (materials.json / consumables `glyph`), or null. Used by
        /// the View ONLY when no sprite resolves - an authored "=" for Iron Scrap says more than the
        /// generic role "*", and it is data, not a View guess.
        /// </summary>
        public readonly string Glyph;

        public PartyShopDetail(string stats, string delta, string description,
                               string iconPath, string iconRole, string iconName,
                               string iconCategory = null, string glyph = null)
        {
            Stats = stats;
            Delta = delta;
            Description = description;
            IconPath = iconPath;
            IconRole = iconRole;
            IconName = iconName;
            IconCategory = iconCategory;
            Glyph = glyph;
        }
    }

    /// <summary>
    /// One readable SPEC line for the selected item's details pane (WO: "make weapons matter" -
    /// read out the stat + the +/- delta vs equipped before buying). The View renders, per line:
    /// "<Label> <Value> (<Delta>)" with <Delta> tinted by <DeltaSign> (green up / red down / dim same).
    /// Value is already formatted; Delta is "" when nothing comparable is equipped (raw stat, no tint).
    /// </summary>
    public readonly struct PartyShopSpec
    {
        /// <summary>Stat label, e.g. "Damage", "Defense", "HP", "Reach".</summary>
        public readonly string Label;
        /// <summary>Formatted absolute value, e.g. "25", "0.14", "2.5m".</summary>
        public readonly string Value;
        /// <summary>Formatted +/- delta vs the equipped piece, e.g. "+5", "-0.02"; "" when no comparison.</summary>
        public readonly string Delta;
        /// <summary>-1 worse / 0 same / +1 better; the View maps this to red / dim / green. 0 when no delta.</summary>
        public readonly int DeltaSign;

        public PartyShopSpec(string label, string value, string delta, int deltaSign)
        {
            Label = label;
            Value = value;
            Delta = delta;
            DeltaSign = deltaSign;
        }
    }

    /// <summary>One party member chip - id/name/class for the selector, portrait key for the View.</summary>
    public readonly struct PartyMemberVM
    {
        public readonly string Name;
        public readonly string Class;        // knight/mage/ranger/cleric (lowercase)
        public readonly bool Selected;
        /// <summary>Portrait icon role (always "portrait"); the View maps Class -> a portrait/crest sprite.</summary>
        public readonly string IconRole;

        public PartyMemberVM(string name, string cls, bool selected)
        {
            Name = name;
            Class = cls ?? "";
            Selected = selected;
            IconRole = "portrait";
        }
    }

    /// <summary>
    /// The 3D preview MODEL descriptor for a row (WO-501 preview pane), projected by the VM so the
    /// View resolves the prefab WITHOUT naming GearCatalog (strict-MVVM). IsGear=false routes the
    /// preview straight to the 2D icon/glyph (non-gear rows, or a missing/def-less id); when IsGear
    /// is true the View loads <see cref="PrefabPath"/> via Addressables when <see cref="Addressable"/>
    /// else Resources. An empty PrefabPath degrades to the 2D sprite (most gear has no model today).
    /// </summary>
    public readonly struct PartyShopPreviewModel
    {
        public readonly bool IsGear;
        public readonly string PrefabPath;
        public readonly bool Addressable;

        public PartyShopPreviewModel(bool isGear, string prefabPath, bool addressable)
        {
            IsGear = isGear;
            PrefabPath = prefabPath;
            Addressable = addressable;
        }
    }

    public sealed class PartyShopVM : IPanelViewModel, IDisposable
    {
        // -- Icon role keys (ItemVM.IconRole) - the View maps these to the real sprite source --
        public const string IconRoleWeapon    = "weapon";
        public const string IconRoleArmor     = "armor";
        public const string IconRoleCraftable = "craftable";
        // WO-598 goods/jeweler bands (the View resolves iconPath first, then a glyph fallback).
        public const string IconRolePotion    = "potion";
        public const string IconRoleMaterial  = "material";
        public const string IconRoleAccessory = "accessory";
        public const string IconRoleGem       = "gem";

        private readonly string _vendorContext;
        private readonly string _displayName;
        private readonly IEconomy _economy;
        private readonly IInventoryStore _store;
        private readonly IReadOnlyList<IEquipTarget> _members;   // hero + companions (never null)
        private readonly IReadOnlyList<int> _memberLevels;       // parallel to _members (member level for req gate)
        private readonly Action _onClose;

        private readonly Action<ResourceSnapshot> _ecoHandler;
        private readonly List<Action> _unsubscribers = new List<Action>();
        private bool _disposed;

        private int _selectedMember;                 // index into _members (default = active hero = 0)
        private PartyShopTab _tab = PartyShopTab.Buy;
        private PartyShopCategory _category = PartyShopCategory.All;   // the category "dropdown" selection
        private PartyShopType _type = PartyShopType.Any;               // the finer weapon/armor TYPE sub-filter (WO-501)

        // The per-row action keyed by item id (armed on rebuild), plus the detail payload per id.
        private readonly Dictionary<string, Action> _rowActions = new Dictionary<string, Action>();
        private readonly Dictionary<string, PartyShopDetail> _rowDetails = new Dictionary<string, PartyShopDetail>();
        // WO-1584: the goods/jeweler SELL facts the detail column reads back (BuildSpecs) - the
        // coins this stack pays and how many are held. Armed on the same rebuild as the row.
        private readonly Dictionary<string, int> _rowSellCoins = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _rowOwned = new Dictionary<string, int>();

        private readonly List<ItemVM> _items = new List<ItemVM>();
        private readonly List<(string id, GearKind kind)> _currentStock = new List<(string id, GearKind kind)>();
        // The TYPE chips that have >0 candidate rows, captured during Rebuild ahead of the TYPE narrow.
        private readonly List<PartyShopType> _availableTypes = new List<PartyShopType>();

        // The store TYPE this vendor sells (weapon shop -> Weapon, armor shop -> Armor), from the contract.
        private readonly GearKind _storeKinds;

        /// <summary>
        /// DI-in-Open factory (UI_MVVM_MIGRATION_PLAN §1 step 5): resolves the economy + owned-store
        /// handles ITSELF (EconomyService.Instance + VillageInventory.Instance) so the View names
        /// neither singleton. The party members/levels stay View-supplied (they wrap live scene
        /// GameObjects the pure VM can't hold). The store is returned via <paramref name="store"/> so
        /// the View keeps its handle to dispose. Header default ("Party Shop" for a no-context open)
        /// composes here, matching the old View-side computation.
        /// </summary>
        public static PartyShopVM CreateDefault(string vendorContext, string displayName,
                                                IReadOnlyList<IEquipTarget> members,
                                                IReadOnlyList<int> memberLevels,
                                                Action onClose, PartyShopTab? lockedTab,
                                                out InventoryStore store)
        {
            store = new InventoryStore(VillageInventory.Instance, members);
            string headerName = !string.IsNullOrEmpty(displayName)
                ? displayName
                : (string.IsNullOrEmpty(vendorContext) ? "Party Shop" : null);
            return new PartyShopVM(vendorContext, EconomyService.Instance, store, members, memberLevels,
                headerName, onClose, lockedTab);
        }

        public PartyShopVM(string vendorContext,
                           IEconomy economy,
                           IInventoryStore store,
                           IReadOnlyList<IEquipTarget> members,
                           IReadOnlyList<int> memberLevels,
                           string displayName = null,
                           Action onClose = null,
                           PartyShopTab? lockedTab = null)
        {
            _vendorContext = vendorContext ?? "";
            _displayName = displayName;
            _economy = economy;
            _store = store;
            _members = members ?? Array.Empty<IEquipTarget>();
            _memberLevels = memberLevels ?? Array.Empty<int>();
            _onClose = onClose;

            // WO-598: the vendor's declared stock query (vendors.json) decides the shelf.
            // Layout drives which list this shop builds (gear / goods / jeweler) and the
            // View's per-trade chrome (Market shows NO equip tabs / paper-doll — flag_03).
            Layout = VendorStockResolver.LayoutFor(_vendorContext);
            EmptyLine = VendorStockResolver.EmptyLineFor(_vendorContext);

            _storeKinds = VendorStockContract.AllowedFor(_vendorContext.ToLowerInvariant());
            if (Layout == VendorLayout.Gear)
            {
                // A GEAR shop only deals weapons/armor. If the contract resolved to nothing
                // useful, fall back to BOTH gear kinds so a mis-tagged vendor still shows
                // wearable gear rather than an empty screen. (WO-598: this fallback no longer
                // applies to goods/jeweler layouts — it was the "Market sells swords" root.)
                _storeKinds &= (GearKind.Weapon | GearKind.Armor);
                if (_storeKinds == GearKind.None) _storeKinds = GearKind.Weapon | GearKind.Armor;
            }

            if (_economy != null)
            {
                _ecoHandler = _ => Raise();
                _economy.OnChanged += _ecoHandler;
            }
            if (_store != null)
            {
                Action h = OnModelChanged;
                _store.Changed += h;
                _unsubscribers.Add(() => _store.Changed -= h);
            }
            foreach (var m in _members)
            {
                if (m == null) continue;
                var mm = m;
                Action h = OnModelChanged;
                mm.EquipChanged += h;
                _unsubscribers.Add(() => mm.EquipChanged -= h);
            }

            // Owner F8 2026-07-10: when the NPC's dialogue opened the shop in a SINGLE mode
            // (Buy OR Sell as separate choices), preset the tab and LOCK it — the View hides
            // the top BUY/SELL strip, leaving one list + one bottom action. A null lockedTab
            // keeps the legacy both-tabs behaviour (SetTab still free to switch).
            if (lockedTab.HasValue)
            {
                _tab = lockedTab.Value;
                TabsLocked = true;
            }

            Rebuild();
        }

        // -- IPanelViewModel ---------------------------------------------------

        public event Action Changed;

        public string Title => ResolveTitle();

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_economy != null && _ecoHandler != null) _economy.OnChanged -= _ecoHandler;
            foreach (var u in _unsubscribers) u?.Invoke();
            _unsubscribers.Clear();
            Changed = null;
        }

        // -- Read-only data the View renders -------------------------------------

        public PartyShopTab Tab => _tab;

        /// <summary>Owner F8 2026-07-10: true when the shop was opened locked to a SINGLE mode
        /// (Buy OR Sell, chosen in the NPC dialogue). The View hides the top BUY/SELL tab strip
        /// and <see cref="SetTab"/> is inert — one list, one bottom action per flow.</summary>
        public bool TabsLocked { get; private set; }

        /// <summary>The active category "dropdown" selection (All / Weapons / Armor). The View
        /// highlights the matching selector chip and rebuilds the list when it changes.</summary>
        public PartyShopCategory Category => _category;

        /// <summary>The active finer weapon/armor TYPE sub-filter (WO-501). The View highlights the
        /// matching chip and rebuilds the list when it changes.</summary>
        public PartyShopType Type => _type;

        /// <summary>The weapon/armor TYPES the hero-fit + category list offers BEFORE the TYPE narrow
        /// (so the View only shows chips with &gt;0 candidate rows - never a dead chip). Captured during
        /// Rebuild, ahead of the TYPE predicate, so toggling a chip never strips the other chips.</summary>
        public IReadOnlyList<PartyShopType> AvailableTypes => _availableTypes;

        /// <summary>WO-598: this vendor's declared shelf presentation (gear / goods / jeweler).
        /// The View binds per-trade chrome off this — the Market never shows equip context.</summary>
        public VendorLayout Layout { get; }

        /// <summary>WO-598: the AUTHORED empty-shelf line (never null/empty). The View renders
        /// this instead of a raw "No wares in stock." when a query resolves 0 items.</summary>
        public string EmptyLine { get; }

        /// <summary>
        /// WO-860 B4 / WEAPONS_DEEP_DIVE §3(e): the vendor's AUTHORED "come back after you level
        /// up for new stock" line, or NULL meaning "render no footer". Unlike <see cref="EmptyLine"/>
        /// this is NOT constant for the shop — it is recomputed every <see cref="Rebuild"/> because
        /// it describes the CURRENT shelf. Exactly three states, and the View must keep them
        /// distinguishable:
        ///   1. shelf FULL (the cap dropped nothing)      -> FooterLine == null, no footer row.
        ///   2. shelf THINNED by perLevelCap, rows &gt; 0 -> FooterLine == the authored line.
        ///   3. shelf EMPTY                               -> FooterLine == null; EmptyLine owns it.
        /// Null is also the answer for a vendor that authors no footerLine, and for the SELL tab.
        /// </summary>
        public string FooterLine { get; private set; }

        /// <summary>Whether this vendor offers BOTH gear kinds (so the category selector is useful).
        /// A weapon-only or armor-only vendor pins the category and hides the selector; non-gear
        /// layouts (goods/jeweler, WO-598) never show the weapons/armor selector at all.</summary>
        public bool CategorySelectorVisible =>
            Layout == VendorLayout.Gear &&
            (_storeKinds & GearKind.Weapon) != 0;

        /// <summary>The party-member chips (one per member; the selected one flagged). Never null.</summary>
        public IReadOnlyList<PartyMemberVM> Party
        {
            get
            {
                var list = new List<PartyMemberVM>(_members.Count);
                for (int i = 0; i < _members.Count; i++)
                {
                    var m = _members[i];
                    string name = m == null || string.IsNullOrEmpty(m.TargetName) ? "Hero" : m.TargetName;
                    string cls = m != null ? (m.TargetClass ?? "") : "";
                    list.Add(new PartyMemberVM(name, cls, i == _selectedMember));
                }
                return list;
            }
        }

        public int SelectedMemberIndex => _selectedMember;

        /// <summary>"Name - Class (Lv N)" for the selected member (the panel's sub-header).</summary>
        public string MemberLabel
        {
            get
            {
                var m = SelectedMember;
                if (m == null) return "No hero";
                string name = string.IsNullOrEmpty(m.TargetName) ? "Hero" : m.TargetName;
                string cls = string.IsNullOrEmpty(m.TargetClass) ? "" : Cap(m.TargetClass);
                string lv = " (Lv " + SelectedLevel + ")";
                return string.IsNullOrEmpty(cls) ? name + lv : name + " - " + cls + lv;
            }
        }

        /// <summary>The active tab's rows (affordability + owned/equipped already computed). Never null.</summary>
        public IReadOnlyList<ItemVM> Items => _items;

        public string SelectedId { get; private set; }

        /// <summary>The selected row's detail payload, or null when nothing is selected.</summary>
        public PartyShopDetail? Selected =>
            SelectedId != null && _rowDetails.TryGetValue(SelectedId, out var d) ? d : (PartyShopDetail?)null;

        /// <summary>
        /// The selected item's readable SPEC lines (label + absolute value + signed delta vs the
        /// selected member's equipped piece), for the details pane. Empty when nothing is selected
        /// or the row isn't gear (craftable). Reuses the SAME equipped-comparison the delta line
        /// uses; on the SELL tab (or when nothing comparable is equipped) the delta column is blank.
        /// </summary>
        public IReadOnlyList<PartyShopSpec> SelectedSpecs => BuildSpecs(SelectedId);

        /// <summary>Per-id spec lines (View can render the details for any row). Never null.</summary>
        public IReadOnlyList<PartyShopSpec> SpecsFor(string id) => BuildSpecs(id);

        /// <summary>Detail payload for any row id (View renders the per-row stats/delta from this). Null when absent.</summary>
        public PartyShopDetail? DetailFor(string id) =>
            id != null && _rowDetails.TryGetValue(id, out var d) ? d : (PartyShopDetail?)null;

        /// <summary>
        /// The 3D preview MODEL descriptor for a row id (WO-501 preview pane). Resolves the gear def's
        /// prefabPath + addressable flag HERE (banned symbols are legit inside a VM) so the View drives
        /// the rig WITHOUT naming GearCatalog. Non-gear rows (or a missing/def-less id) return IsGear=false
        /// -> the View shows the 2D icon/glyph. Verbatim logic moved from PartyShopPanelMvvm.
        /// </summary>
        public PartyShopPreviewModel PreviewModelFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return new PartyShopPreviewModel(false, null, false);
            string role = _rowDetails.TryGetValue(id, out var d) ? d.IconRole : null;
            if (role != IconRoleWeapon && role != IconRoleArmor)
                return new PartyShopPreviewModel(false, null, false);   // goods/jeweler/craftable -> 2D

            if (role == IconRoleArmor)
            {
                var a = GearCatalog.FindArmor(id);
                bool armorAddr = ArmorLoadsViaAddressable(a);
                // The branch + its reason are built as PLAIN LOCALS, never as nested literals inside
                // an interpolation hole. CompileGate.BraceBalanced (Assets/Editor/CompileGate.cs:896)
                // is a character scanner with no interpolation model: a `"` inside a `$"..."` hole
                // ENDS the string for its state machine, so the following braces get counted as code
                // and the gate goes RED on a file the compiler accepts. Keep interpolation holes to
                // bare identifiers here.
                string armorBranch = armorAddr ? "ADDRESSABLE" : "RESOURCES/fallback";
                string armorWhy;
                if (a == null) armorWhy = "no armor def";
                else if (armorAddr) armorWhy = "the ROW declares loadVia=addressable or a gear/ path";
                else if (!DeNelle.Core.FeatureFlags.BlinkArmor && a.id != null &&
                         a.id.StartsWith("blink_", StringComparison.OrdinalIgnoreCase))
                    armorWhy = "junked Blink ARMOR (ff.blinkarmor OFF)";
                else armorWhy = "the row declares neither loadVia=addressable nor a gear/ path";
                string armorLoadVia = a?.loadVia;
                string armorPrefab = a?.prefabPath;
                bool blinkArmorFlag = DeNelle.Core.FeatureFlags.BlinkArmor;
                DeNelle.Core.Diagnostics.FlowTrace.Step("PartyShop",
                    $"preview loader branch: id='{id}' role=armor -> {armorBranch} " +
                    $"(loadVia='{armorLoadVia}' prefabPath='{armorPrefab}' ff.blinkarmor={blinkArmorFlag}) " +
                    $"— reason: {armorWhy}");
                return new PartyShopPreviewModel(true, a?.prefabPath, armorAddr);
            }
            var w = GearCatalog.FindWeapon(id);
            bool weaponAddr = WeaponLoadsViaAddressable(w);
            string weaponBranch = weaponAddr ? "ADDRESSABLE" : "RESOURCES/fallback";
            string weaponWhy;
            if (w == null) weaponWhy = "no weapon def";
            else if (weaponAddr) weaponWhy = "the ROW itself declares loadVia=addressable or a gear/ path";
            else weaponWhy = "the row declares neither loadVia=addressable nor a gear/ path";
            string weaponLoadVia = w?.loadVia;
            string weaponPrefab = w?.prefabPath;
            DeNelle.Core.Diagnostics.FlowTrace.Step("PartyShop",
                $"preview loader branch: id='{id}' role=weapon -> {weaponBranch} " +
                $"(loadVia='{weaponLoadVia}' prefabPath='{weaponPrefab}') " +
                $"— reason: {weaponWhy}");
            return new PartyShopPreviewModel(true, w?.prefabPath, weaponAddr);
        }

        // Mirror of EquipmentController.LoadsViaAddressable (replicated, NOT forked) — MOVED here from
        // PartyShopPanelMvvm so the def read leaves the View. Addressable when loadVia=="addressable"
        // or prefabPath starts "gear/".
        //
        // ⛔ WO-1096 (2026-09-09): the `blink_` PREFIX VETO IS DELETED FROM THE WEAPON PATH, and it must
        // never come back. It applied the ARMOR kill-switch (ff.blinkarmor, junked 2026-06-22) to any
        // WEAPON id starting "blink_", and it returned BEFORE the row's own `loadVia` was read — so
        // `blink_shield1h_03` (loadVia="addressable", prefabPath "gear/weapon/Shield1h_03", an address
        // that EXISTS in the local Gear group) was routed to the Resources/structure loader and rendered
        // the fallback (F8 capture seq=4968: "model not found via Addressables OR Resources:
        // 'gear/weapon/Shield1h_03'", lastTransportUrl=(none) — the Addressables request was never
        // issued). The veto also FORKED this from EquipmentController.LoadsViaAddressable
        // (EquipmentController.cs:1100-1108), which has no such prefix test — so the shop previewed one
        // loader and the equip path used another for the same row.
        //
        // CONTENT RULING it violated: the owner ratified all 65 blink_ WEAPON rows as shelf content
        // (2026-08-14) and only Blink ARMOR stays excluded — see the `_excludeIdPrefixesNote` in
        // Assets/Resources/Data/Canonical/vendors.json:42, pinned by ForgeShelfClassKindRegression case 3.
        // THE ROW DECIDES, NOT THE ID PREFIX. The armor flag governs armor only (below).
        // Pinned by Assets/Editor/Regression/PartyShopPreviewLoaderBranchRegression.cs.
        /// <summary>TRUE when the shop preview must load this WEAPON row through Addressables.
        /// Decided by the ROW (loadVia / "gear/" prefabPath) — never by its id prefix.
        /// Public so the regression can pin the branch without constructing a whole VM.</summary>
        public static bool WeaponLoadsViaAddressable(WeaponDef def)
        {
            if (def == null) return false;
            if (!string.IsNullOrEmpty(def.loadVia) &&
                def.loadVia.Equals("addressable", StringComparison.OrdinalIgnoreCase)) return true;
            return !string.IsNullOrEmpty(def.prefabPath) &&
                   def.prefabPath.StartsWith("gear/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>TRUE when the shop preview must load this ARMOR row through Addressables.
        /// The `blink_` veto is CORRECT here and stays: Blink ARMOR is junked behind ff.blinkarmor
        /// (pivot 2026-06-22, HeroArmorVisual.cs:105) — the flag governs ARMOR, and only armor.
        /// Public so the regression can pin the branch without constructing a whole VM.</summary>
        public static bool ArmorLoadsViaAddressable(ArmorDef def)
        {
            if (def == null) return false;
            if (!DeNelle.Core.FeatureFlags.BlinkArmor && def.id != null &&
                def.id.StartsWith("blink_", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(def.loadVia) &&
                def.loadVia.Equals("addressable", StringComparison.OrdinalIgnoreCase)) return true;
            return !string.IsNullOrEmpty(def.prefabPath) &&
                   def.prefabPath.StartsWith("gear/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The selected row's ItemVM (price/affordable/owned/equipped), or null. Pure lookup
        /// into the built _items by SelectedId - the View binds the preview price/buttons off this.</summary>
        public ItemVM? SelectedItem
        {
            get
            {
                if (SelectedId == null) return null;
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].Id == SelectedId) return _items[i];
                return null;
            }
        }

        /// <summary>The large, readable price line for the selected row (WO-501 owner point 3). On the
        /// SELL tab it reads "+N Gold" (the refund); on BUY it reads "N Gold", "Owned" (held), or "Free".</summary>
        public string SelectedPriceText
        {
            get
            {
                var it = SelectedItem;
                if (it == null) return "";
                var item = it.Value;
                // WO-697: price numbers through the ONE kit formatter (compact >= 10k).
                if (_tab == PartyShopTab.Sell)
                    return "+" + (item.Price > 0
                        ? DeNelle.Core.UI.ElarionUi.CompactNumber(item.Price) + " Gold" : "Free");
                // Locked (wrong class / above level): the price line reads the requirement, not a price.
                if (item.Locked) return string.IsNullOrEmpty(item.LockReason) ? "Locked" : item.LockReason;
                if (item.Equipped || item.Price <= 0) return "Owned";
                return DeNelle.Core.UI.ElarionUi.CompactNumber(item.Price) + " Gold";
            }
        }

        public string Status { get; private set; }

        /// <summary>Live wallet readout (the View rebuilds its "Gold: ..." line from these).</summary>
        public int Coins    => _economy?.Coins ?? 0;
        public int Wood     => _economy?.Wood ?? 0;
        public int Iron     => _economy?.Iron ?? 0;
        public int Food     => _economy?.Food ?? 0;
        public int Crystals => _economy?.Crystals ?? 0;

        /// <summary>The store's ACTUAL built stock (id + category) - for an AutoPilot bot assertion.</summary>
        public IReadOnlyList<(string id, GearKind kind)> CurrentStock => _currentStock;

        public string VendorContext => _vendorContext;

        // -- Commands ------------------------------------------------------------

        /// <summary>Select a party member by index -> re-filter the list FOR that member.</summary>
        public void SelectMember(int index)
        {
            if (index < 0 || index >= _members.Count) return;
            if (index == _selectedMember) return;
            _selectedMember = index;
            SelectedId = null;
            Rebuild();
            Raise();
        }

        /// <summary>Switch BUY <-> SELL (both live on the same screen - no leaving to sell).</summary>
        public void SetTab(PartyShopTab tab)
        {
            if (TabsLocked) return;   // single-mode shop (owner F8 2026-07-10): tab is fixed
            if (tab == _tab) return;
            _tab = tab;
            SelectedId = null;
            Rebuild();
            Raise();
        }

        /// <summary>Set the category "dropdown" (All / Weapons / Armor), then rebuild the list.</summary>
        public void SetCategory(PartyShopCategory category)
        {
            if (category == _category) return;
            _category = category;
            _type = PartyShopType.Any;   // a category switch resets the finer TYPE narrow (chip set changes)
            SelectedId = null;
            Rebuild();
            Raise();
        }

        /// <summary>Set the finer weapon/armor TYPE sub-filter (WO-501), then rebuild the list.</summary>
        public void SetType(PartyShopType type)
        {
            if (type == _type) return;
            _type = type;
            SelectedId = null;
            Rebuild();
            Raise();
        }

        /// <summary>Inspect a row (holds it as selected so the View can show its details).</summary>
        public void Select(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            SelectedId = id;
            // WO-1584 §12: SelectedId is the ONE truth for both the detail column and the row
            // highlight. Logging it here means a capture that shows the detail naming one item
            // while another row is lit can be settled from the trace instead of re-theorized.
            DeNelle.Core.Diagnostics.FlowTrace.Step("PartyShop",
                $"SELECT id='{id}' tab={_tab} rows={_items.Count} rowIndex={IndexOfRow(id)}");
            Raise();
        }

        /// <summary>Index of a row id in the built list, or -1. Used by the selection trace and by
        /// the View to scroll the SELECTED row into view (WO-1584).</summary>
        public int IndexOfRow(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// The single-tap action for a row: BUY (if not owned) / EQUIP (if owned) on the BUY tab,
        /// or SELL on the SELL tab. Selecting the row first arms it; this fires its armed action.
        /// </summary>
        public void Act(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            SelectedId = id;
            if (_rowActions.TryGetValue(id, out var act) && act != null) act();
            else Status = "Nothing to do for that item.";
            Raise();
        }

        /// <summary>WO-501 EQUIP button: equip the SELECTED owned item to the selected member through
        /// the IEquipTarget seam (GearLoadout auto-routes shields to off-hand + enforces hand rules).
        /// A no-op (with status) when nothing is selected, the item isn't owned, or it is already worn.</summary>
        public void EquipSelected()
        {
            string id = SelectedId;
            if (string.IsNullOrEmpty(id)) { Status = "Select an item to equip."; Raise(); return; }
            if (_store == null || _store.OwnedQuantity(id) <= 0)
            {
                Status = "You must buy it before you can equip it.";
                Raise();
                return;
            }
            var w = GearCatalog.FindWeapon(id);
            if (w != null) { EquipWeapon(w); Raise(); return; }
            var a = GearCatalog.FindArmor(id);
            if (a != null) { EquipArmor(a); Raise(); return; }
            Status = "That item can't be equipped.";
            Raise();
        }

        // ── WO-808 Option A: the Improve verb (Forge/Armorer reforge) ─────────────

        /// <summary>True when the SELECTED row is owned gear (the Improve button shows at all).</summary>
        public bool ImproveVisible
        {
            get
            {
                string id = SelectedId;
                if (string.IsNullOrEmpty(id) || _store == null || _store.OwnedQuantity(id) <= 0) return false;
                return GearCatalog.FindWeapon(id) != null || GearCatalog.FindArmor(id) != null;
            }
        }

        /// <summary>True when the selected owned gear can be improved RIGHT NOW (not maxed + affordable).</summary>
        public bool ImproveEnabled
        {
            get
            {
                string id = SelectedId;
                string rarity = RarityOf(id);
                return ImproveVisible && rarity != null && GearProgression.CanImprove(id, rarity, out _);
            }
        }

        /// <summary>Button label — "Improve Lv N" while a next level exists, "Max Level" at cap.</summary>
        public string ImproveLabel
        {
            get
            {
                string id = SelectedId;
                string rarity = RarityOf(id);
                if (string.IsNullOrEmpty(id) || rarity == null) return "Improve";
                int lvl = GearLevel(id);
                return GearProgression.HasNextLevel(rarity, lvl) ? "Improve Lv " + (lvl + 1) : "Max Level";
            }
        }

        /// <summary>
        /// WO-808 Improve: reforge the SELECTED owned piece one level in place (instant,
        /// ResourceLedger-charged; the instance and its equip state are untouched — only
        /// its power climbs). Status carries the result or the honest block reason.
        /// </summary>
        public void ImproveSelected()
        {
            string id = SelectedId;
            if (string.IsNullOrEmpty(id)) { Status = "Select an item to improve."; Raise(); return; }
            if (_store == null || _store.OwnedQuantity(id) <= 0)
            {
                Status = "You must own it before you can improve it.";
                Raise(); return;
            }
            string rarity = RarityOf(id);
            if (rarity == null) { Status = "That item can't be improved."; Raise(); return; }

            if (!GearProgression.CanImprove(id, rarity, out string reason))
            {
                Status = reason;
                Raise(); return;
            }

            var w = GearCatalog.FindWeapon(id);
            var a = GearCatalog.FindArmor(id);
            string name = w != null ? Display(w.name, w.id) : a != null ? Display(a.name, a.id) : id;

            int newLevel = GearProgression.Improve(id, rarity);
            Status = newLevel > 0
                ? name + " improved to Lv " + newLevel + "."
                : "Improve failed.";
            Rebuild();
            Raise();
        }

        private static string RarityOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var w = GearCatalog.FindWeapon(id);
            if (w != null) return w.rarity;
            var a = GearCatalog.FindArmor(id);
            return a != null ? a.rarity : null;
        }

        /// <summary>Owner ruling 07-06 (3-state shop Equip button): UNEQUIP the selected,
        /// currently-worn item from the selected member through the SAME IEquipTarget seam
        /// <see cref="EquipSelected"/> uses. Routes by the slot that actually wears the id —
        /// main-hand, off-hand (shield), or armor — so a shield unequips from the off-hand,
        /// never a dead UnequipWeapon call. No-op with a status line when nothing is selected
        /// or the selected item isn't worn by the selected member.</summary>
        public void UnequipSelected()
        {
            string id = SelectedId;
            if (string.IsNullOrEmpty(id)) { Status = "Select an item to unequip."; Raise(); return; }
            var m = SelectedMember;
            if (m == null) { Status = "No hero selected."; Raise(); return; }

            bool main = m.EquippedWeapon != null && string.Equals(m.EquippedWeapon.id, id, StringComparison.OrdinalIgnoreCase);
            bool off  = m.EquippedOffHand != null && string.Equals(m.EquippedOffHand.id, id, StringComparison.OrdinalIgnoreCase);
            bool body = m.EquippedArmor != null && string.Equals(m.EquippedArmor.id, id, StringComparison.OrdinalIgnoreCase);
            if (!main && !off && !body) { Status = "That item isn't equipped."; Raise(); return; }

            var w = GearCatalog.FindWeapon(id);
            var a = GearCatalog.FindArmor(id);
            string name = w != null ? Display(w.name, w.id) : a != null ? Display(a.name, a.id) : id;

            if (main) m.UnequipWeapon();
            else if (off) m.UnequipOffHand();
            else m.UnequipArmor();

            Status = "Unequipped " + name + " from " + MemberName() + ".";
            Rebuild();
            Raise();
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }

        private void OnModelChanged()
        {
            if (_disposed) return;
            Rebuild();
            Changed?.Invoke();
        }

        // -- Selected-member helpers ----------------------------------------------

        private IEquipTarget SelectedMember =>
            (_members.Count > 0 && _selectedMember >= 0 && _selectedMember < _members.Count)
                ? _members[_selectedMember] : null;

        private int SelectedLevel =>
            (_selectedMember >= 0 && _selectedMember < _memberLevels.Count) ? _memberLevels[_selectedMember] : 1;

        private string SelectedJob => SelectedMember != null ? SelectedMember.TargetClass : null;

        // -- List build (dispatch) ------------------------------------------------

        private void Rebuild()
        {
            _items.Clear();
            _rowActions.Clear();
            _rowDetails.Clear();
            _rowSellCoins.Clear();
            _rowOwned.Clear();
            _currentStock.Clear();
            _availableTypes.Clear();
            // WO-860 B4: the footer describes THIS shelf, so it starts cleared every rebuild.
            // Only BuildBuyGear (the only layout the perLevelCap thins) ever sets it — SELL and
            // the goods/jeweler shelves therefore never carry a stale footer from a prior tab.
            FooterLine = null;

            if (_store == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("PartyShop", "no inventory store bound - list empty.");
                return;
            }

            if (_tab == PartyShopTab.Buy) BuildBuy();
            else BuildSell();
        }

        // -- BUY dispatch (WO-598): the vendor's declared layout picks the shelf ------
        private void BuildBuy()
        {
            if (Layout == VendorLayout.Goods)   { BuildBuyGoods();   return; }
            if (Layout == VendorLayout.Jeweler) { BuildBuyJeweler(); return; }
            BuildBuyGear();
        }

        // -- BUY (gear) - the catalog gear the SELECTED member can equip, gated by store type ------
        private void BuildBuyGear()
        {
            var member = SelectedMember;
            string job = SelectedJob;
            int level = SelectedLevel;

            // ?12: guard the catalog read so a parse/IO failure logs + is skipped instead of throwing.
            DeNelle.Core.Diagnostics.Guard.Try("PartyShop", "reload gear catalog", () => GearCatalog.Reload());

            // RECONCILED FILTER (WO-598): ask the ONE VendorStockResolver what this vendor stocks
            // for the selected member. The resolver folds the vendor's declared stock QUERY
            // (vendors.json categories) + the ROSTER gate (an item NO currently-playable class can
            // use — Mage wands under ff.knightonly — is EXCLUDED, never a locked row; the flag_08
            // fix) + class/level eligibility into one list. SHOW-ALL survives for legitimate
            // aspiration: level-gated (and roster-obtainable wrong-class) items come back locked
            // with a LockReason ("Requires Lv X"), so progression still shows.
            // WO-860 B4: the out-flag is the ONLY honest source for "the cap thinned this shelf" —
            // the VM cannot re-derive it, because by the time it sees the list the dropped rows
            // are already gone. See FooterLine for the three states this feeds.
            var shoppable = VendorStockResolver.Resolve(_vendorContext, job, level, null, out bool thinnedByCap);

            // Category "dropdown": ALL shows the combined list ARMOR-FIRST then weapons (armor/
            // weapons-first, STORE_EQUIP_SPEC); WEAPONS / ARMOR narrow to one kind. Craftables
            // always pass (a crafting vendor's only stock). Reorder so armor leads in ALL.
            var ordered = new List<VendorWare>(shoppable.Count);
            if (_category == PartyShopCategory.All || _category == PartyShopCategory.Armor)
                foreach (var e in shoppable) if (e.Kind == VendorWareKind.Armor) ordered.Add(e);
            if (_category != PartyShopCategory.Armor)
                foreach (var e in shoppable)
                {
                    if (e.Kind != VendorWareKind.Weapon) continue;
                    var def = GearCatalog.FindWeapon(e.Id);
                    bool offHand = def != null && def.IsOffHandItem;
                    if (_category == PartyShopCategory.Weapons && offHand) continue;
                    if (_category == PartyShopCategory.OffHand && !offHand) continue;
                    ordered.Add(e);
                }
            foreach (var e in shoppable) if (e.Kind == VendorWareKind.Craftable) ordered.Add(e);

            // ?12 INSTRUMENT (affordability live-RCA, no logic change): ONCE per rebuild, capture the
            // exact context the affordability check sees - whether the economy/state are bound and what
            // gold it reads - so the owner's next store-open NAMES "coins read 0 / state null" vs "cost wrong".
            DeNelle.Core.Diagnostics.FlowTrace.Step("Store",
                "affordability ctx: economyNull=" + (_economy == null)
                + " stateNull=" + (DeNelle.Core.State.GameStateService.Instance?.State == null)
                + " coins=" + (_economy?.Coins ?? -1));
            // Sample only the first few buyable rows (cheap; avoids per-frame/long-list spam).
            int _affordSamples = 0;

            foreach (var entry in ordered)
            {
                switch (entry.Kind)
                {
                    case VendorWareKind.Weapon:
                    {
                        var w = GearCatalog.FindWeapon(entry.Id);
                        if (w == null) continue;
                        if (!WeaponPassesType(w)) continue;   // WO-501 TYPE narrow (registers availability)
                        bool owned = _store.OwnedQuantity(w.id) > 0;
                        var equippedDef = w.IsOffHandItem ? member?.EquippedOffHand : member?.EquippedWeapon;
                        bool equipped = equippedDef != null &&
                                        string.Equals(equippedDef.id, w.id, StringComparison.OrdinalIgnoreCase);
                        var cost = GearCatalog.GetBuyCost(w);
                        bool affordable = owned || _economy == null || _economy.CanAfford(cost);

                        if (_affordSamples < 3)
                        {
                            _affordSamples++;
                            DeNelle.Core.Diagnostics.FlowTrace.Step("Store",
                                "afford row '" + w.id + "' cost=" + DescribeCost(cost)
                                + " coins=" + (_economy?.Coins ?? -1)
                                + " canAfford=" + (_economy?.CanAfford(cost) ?? true)
                                + " owned=" + owned);
                        }

                        AddBuyWeaponRow(w, cost, owned, equipped, affordable, entry.Eligible, entry.LockReason);
                        _currentStock.Add((w.id, GearKind.Weapon));
                        break;
                    }
                    case VendorWareKind.Armor:
                    {
                        var a = GearCatalog.FindArmor(entry.Id);
                        if (a == null) continue;
                        if (!ArmorPassesType(a)) continue;   // WO-501 TYPE narrow (registers availability)
                        bool owned = _store.OwnedQuantity(a.id) > 0;
                        bool equipped = member != null && member.EquippedArmor != null &&
                                        string.Equals(member.EquippedArmor.id, a.id, StringComparison.OrdinalIgnoreCase);
                        var cost = GearCatalog.GetBuyCost(a);
                        bool affordable = owned || _economy == null || _economy.CanAfford(cost);

                        if (_affordSamples < 3)
                        {
                            _affordSamples++;
                            DeNelle.Core.Diagnostics.FlowTrace.Step("Store",
                                "afford row '" + a.id + "' cost=" + DescribeCost(cost)
                                + " coins=" + (_economy?.Coins ?? -1)
                                + " canAfford=" + (_economy?.CanAfford(cost) ?? true)
                                + " owned=" + owned);
                        }

                        AddBuyArmorRow(a, cost, owned, equipped, affordable, entry.Eligible, entry.LockReason);
                        _currentStock.Add((a.id, GearKind.Armor));
                        break;
                    }
                    case VendorWareKind.Craftable:
                    {
                        AddBuyCraftableRow(entry.Craftable);
                        _currentStock.Add((entry.Id, GearKind.Craftable));
                        break;
                    }
                }
            }

            // WO-598: an empty shelf reads the vendor's AUTHORED line, never a raw fallback.
            Status = _items.Count == 0
                ? EmptyLine
                : "Tap a row to BUY (auto-equips) or EQUIP what you own.";

            // WO-860 B4 / WEAPONS_DEEP_DIVE §3(e) — the dead-copy wire. A Knight at the Forge sees
            // 2 rows because perLevelCap=2 thinned the shelf; without this he gets no explanation
            // and the shop reads as broken. Gated on BOTH conditions so the three states stay
            // distinguishable: rows>0 (an empty shelf is EmptyLine's case, never the footer's) AND
            // thinnedByCap (a full shelf has nothing to promise). _items.Count is used rather than
            // shoppable.Count because the WO-501 TYPE narrow can drop rows after the resolve.
            FooterLine = (_items.Count > 0 && thinnedByCap)
                ? VendorStockResolver.FooterLineFor(_vendorContext)
                : null;

            // ?12: no silent blank - record WHY the BUY list is empty (data vs filtered).
            if (_items.Count == 0)
            {
                bool catalogEmpty = GearCatalog.AllWeapons().Count == 0 && GearCatalog.AllArmors().Count == 0;
                if (catalogEmpty)
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("PartyShop",
                        $"BUY EMPTY for '{_vendorContext}': gear catalog loaded NO weapons/armor.");
                else
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("PartyShop",
                        $"BUY EMPTY for '{_vendorContext}' member job='{job}' lvl={level} " +
                        $"(store kinds {_storeKinds}) - every catalog item filtered out by class/level.");
            }
        }

        private void AddBuyWeaponRow(WeaponDef w, ResourceCost cost, bool owned, bool equipped, bool affordable,
                                     bool eligible, string lockReason)
        {
            string id = w.id;
            string name = string.IsNullOrEmpty(w.name) ? w.id : w.name;

            _rowDetails[id] = new PartyShopDetail(
                WeaponStats(w), DeltaVsEquippedWeapon(w), DescribeGear(w.job, w.rarity),
                w.iconPath, IconRoleWeapon, id);

            // SHOW-ALL / LOCK (owner 2026-07-01): an item the selected member can't use yet (wrong class
            // or above level) is SHOWN for progression but NOT purchasable/equippable — the row is greyed
            // with the reason. Tapping it only reports the requirement (never spends/equips).
            if (!eligible)
            {
                string reason = string.IsNullOrEmpty(lockReason) ? "Locked" : lockReason;
                _rowActions[id] = () => { Status = LockedTapLine(name, reason); };
                _items.Add(new ItemVM(id, name, IconRoleWeapon, id, cost.Coins, "gold",
                    affordable: false, w.rarity, equipped: false, locked: true, lockReason: reason));
                return;
            }

            // Single-tap action: EQUIP if already owned, else BUY (which auto-equips on success).
            if (owned) _rowActions[id] = () => EquipWeapon(w);
            else _rowActions[id] = () => BuyWeapon(w);

            // Price column shows the buy cost, or 0 (Owned) when held. Equipped/Owned flags drive the chip.
            // WO-808: owned rows carry the instance's gear level (View shows a "Lv N" chip when > 1).
            _items.Add(new ItemVM(id, name, IconRoleWeapon, id, owned ? 0 : cost.Coins, "gold",
                affordable, w.rarity, equipped: equipped, locked: false,
                level: owned ? GearLevel(id) : 1));
        }

        private void AddBuyArmorRow(ArmorDef a, ResourceCost cost, bool owned, bool equipped, bool affordable,
                                    bool eligible, string lockReason)
        {
            string id = a.id;
            string name = string.IsNullOrEmpty(a.name) ? a.id : a.name;

            _rowDetails[id] = new PartyShopDetail(
                ArmorStats(a), DeltaVsEquippedArmor(a), DescribeGear(a.job, a.rarity),
                a.iconPath, IconRoleArmor, id);

            // SHOW-ALL / LOCK (see AddBuyWeaponRow): shown for progression, not purchasable/equippable.
            if (!eligible)
            {
                string reason = string.IsNullOrEmpty(lockReason) ? "Locked" : lockReason;
                _rowActions[id] = () => { Status = LockedTapLine(name, reason); };
                _items.Add(new ItemVM(id, name, IconRoleArmor, id, cost.Coins, "gold",
                    affordable: false, a.rarity, equipped: false, locked: true, lockReason: reason));
                return;
            }

            if (owned) _rowActions[id] = () => EquipArmor(a);
            else _rowActions[id] = () => BuyArmor(a);

            _items.Add(new ItemVM(id, name, IconRoleArmor, id, owned ? 0 : cost.Coins, "gold",
                affordable, a.rarity, equipped: equipped, locked: false,
                level: owned ? GearLevel(id) : 1));
        }

        /// <summary>
        /// WO-960 ruling 4: a tap on a LOCKED card explains the unlock in plain words.
        /// Level locks read "unlocks at Lv N" (owner-verbatim phrasing); any other lock
        /// (e.g. "Class: Ranger" — a hard never, not a later) keeps the reason as-is.
        /// The LockReason string itself stays "Requires Lv N" because the View's card
        /// hint shortener and the disabled buy-button label both key on that prefix.
        /// </summary>
        private static string LockedTapLine(string name, string reason)
        {
            const string prefix = "Requires Lv ";
            return reason != null && reason.StartsWith(prefix, StringComparison.Ordinal)
                ? name + " unlocks at Lv " + reason.Substring(prefix.Length) + "."
                : name + " - " + reason + ".";
        }

        // -- BUY - a CRAFTABLE recipe row (crafting-as-shoppable). Surfaced when the vendor's
        // contract allows GearKind.Craftable. The recipe is crafted at the forge/pedestal (not
        // bought for gold), so the row's action explains where - it never spends or equips. --
        private void AddBuyCraftableRow(ShoppableCraftable c)
        {
            if (string.IsNullOrEmpty(c.Id)) return;
            string id = c.Id;
            string name = string.IsNullOrEmpty(c.DisplayName) ? c.Id : c.DisplayName;

            _rowDetails[id] = new PartyShopDetail(
                string.IsNullOrEmpty(c.ResultGlyph) ? "Craftable" : c.ResultGlyph + "  Craftable",
                "", c.Description ?? "", null, IconRoleCraftable, id);

            _rowActions[id] = () => { Status = "Craft " + name + " at the crafting station."; };

            // Price 0 (crafted, not purchased); affordable=true so the row is never greyed out.
            _items.Add(new ItemVM(id, name, IconRoleCraftable, id, 0, "craft",
                true, null, equipped: false, locked: false));
        }

        // =====================================================================
        //  WO-598 GOODS shelf (Market): consumables + crafting materials. A flat
        //  purchase list - NO equip context, NO weapons/armor. Rows come from the
        //  vendor's resolved query; the VM only maps them to ItemVM rows.
        // =====================================================================
        private void BuildBuyGoods()
        {
            foreach (var ware in VendorStockResolver.Resolve(_vendorContext, SelectedJob, SelectedLevel))
            {
                switch (ware.Kind)
                {
                    case VendorWareKind.Consumable: AddBuyConsumableRow(ware.Id); break;
                    case VendorWareKind.Material:   AddBuyMaterialRow(ware.Id, IconRoleMaterial, GearKind.Material); break;
                    // Any other band on a goods vendor is a data mistake; the resolver
                    // already traces the query, so just skip (never render wrong-trade rows).
                }
            }

            Status = _items.Count == 0
                ? EmptyLine
                : "Tap a row to view it, then Purchase.";
        }

        // =====================================================================
        //  WO-598 JEWELER shelf: rings + amulets (accessories.json) + gems (the
        //  crystal material band). Never weapons/armor (flag_11 fix). Accessories
        //  are bought here and equipped from the Character screen (EquipVM slots).
        // =====================================================================
        private void BuildBuyJeweler()
        {
            foreach (var ware in VendorStockResolver.Resolve(_vendorContext, SelectedJob, SelectedLevel))
            {
                switch (ware.Kind)
                {
                    case VendorWareKind.Ring:
                    case VendorWareKind.Amulet:
                        AddBuyAccessoryRow(ware.Id, ware.Eligible, ware.LockReason);
                        break;
                    case VendorWareKind.Gem:
                        AddBuyMaterialRow(ware.Id, IconRoleGem, GearKind.Material);
                        break;
                }
            }

            Status = _items.Count == 0
                ? EmptyLine
                : "Tap a row to view it, then Purchase. Equip jewelry from the Character screen.";
        }

        // -- WO-598 goods row builders (data from the catalogs; zero shelf lists here) --

        private void AddBuyConsumableRow(string id)
        {
            var def = DeNelle.Village.Items.ConsumableCatalog.Find(id);
            if (def == null) return;
            string name = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName;
            int price = VendorStockResolver.PriceFor(def);
            var cost = new ResourceCost(coins: price);
            bool affordable = _economy == null || _economy.CanAfford(cost);
            int owned = VillageInventory.Instance != null ? VillageInventory.Instance.Get(id) : 0;

            string stats = Cap(def.KindRaw ?? "consumable") +
                           (string.IsNullOrEmpty(def.EffectRaw) ? "" : "   " + def.EffectRaw) +
                           (owned > 0 ? "   (owned " + owned + ")" : "");
            _rowDetails[id] = new PartyShopDetail(stats, "", DescribeConsumable(def),
                def.IconPath, IconRolePotion, id, iconCategory: null, glyph: def.Glyph);
            _rowActions[id] = () => BuyGoods(id, name, cost);
            _items.Add(new ItemVM(id, name, IconRolePotion, id, price, "gold", affordable,
                                  iconPath: def.IconPath));
            _currentStock.Add((id, GearKind.Potion));
        }

        private void AddBuyMaterialRow(string id, string iconRole, GearKind stockKind)
        {
            var def = DeNelle.Village.Items.MaterialCatalog.Find(id);
            if (def == null) return;
            string name = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName;
            int price = VendorStockResolver.PriceFor(def);
            var cost = new ResourceCost(coins: price);
            bool affordable = _economy == null || _economy.CanAfford(cost);
            int owned = VillageInventory.Instance != null ? VillageInventory.Instance.Get(id) : 0;

            string stats = Cap(def.Category ?? "material") + (owned > 0 ? "   (owned " + owned + ")" : "");
            string desc = iconRole == IconRoleGem
                ? "A cut stone for the jeweler's bench - fuel for ring and amulet work."
                : DescribeMaterial(def);
            // WO-1584: the BUY shelf carries the SAME art keys as the SELL shelf. The Market's
            // buy tab glyphed for exactly the same reason the sell tab did.
            _rowDetails[id] = new PartyShopDetail(stats, "", desc, def.IconPath, iconRole, id,
                                                  iconCategory: def.Category, glyph: def.Glyph);
            _rowActions[id] = () => BuyGoods(id, name, cost);
            _items.Add(new ItemVM(id, name, iconRole, id, price, "gold", affordable,
                                  iconPath: def.IconPath));
            DeNelle.Core.Diagnostics.FlowTrace.Step("PartyShop",
                $"BUY material row id='{id}' label='{name}' role='{iconRole}' " +
                $"iconPath='{(string.IsNullOrEmpty(def.IconPath) ? "<none>" : def.IconPath)}' " +
                $"category='{(string.IsNullOrEmpty(def.Category) ? "<none>" : def.Category)}'");
            _currentStock.Add((id, stockKind));
        }

        private void AddBuyAccessoryRow(string id, bool eligible, string lockReason)
        {
            var ac = GearCatalog.FindAccessory(id);
            if (ac == null) return;
            string name = string.IsNullOrEmpty(ac.name) ? ac.id : ac.name;
            var cost = GearCatalog.GetBuyCost(ac);
            bool owned = _store != null && _store.OwnedQuantity(id) > 0;
            bool affordable = owned || _economy == null || _economy.CanAfford(cost);

            _rowDetails[id] = new PartyShopDetail(AccessoryStats(ac), "",
                DescribeGear(ac.job, ac.rarity), ac.iconPath, IconRoleAccessory, id);

            if (!eligible)
            {
                string reason = string.IsNullOrEmpty(lockReason) ? "Locked" : lockReason;
                _rowActions[id] = () => { Status = name + " - " + reason + "."; };
                _items.Add(new ItemVM(id, name, IconRoleAccessory, id, cost.Coins, "gold",
                    affordable: false, ac.rarity, equipped: false, locked: true, lockReason: reason));
            }
            else
            {
                _rowActions[id] = () => BuyAccessory(ac);
                _items.Add(new ItemVM(id, name, IconRoleAccessory, id, owned ? 0 : cost.Coins, "gold",
                    affordable, ac.rarity));
            }
            _currentStock.Add((id, GearKind.Accessory));
        }

        private void BuyGoods(string id, string name, ResourceCost cost)
        {
            if (_economy == null) { Status = "Economy unavailable."; return; }
            if (!_economy.TrySpend(cost))
            {
                Status = "Not enough gold for " + name + " - needs " + CostString(cost) + ".";
                return;
            }
            VillageInventory.Instance?.Add(id, 1);
            PushHud();
            Status = "Purchased " + name + "!";
            Rebuild();
        }

        private void BuyAccessory(AccessoryDef ac)
        {
            if (ac == null) return;
            if (_economy == null) { Status = "Economy unavailable."; return; }
            var cost = GearCatalog.GetBuyCost(ac);
            if (!_economy.TrySpend(cost))
            {
                Status = "Not enough gold for " + Display(ac.name, ac.id) + " - needs " + CostString(cost) + ".";
                return;
            }
            VillageInventory.Instance?.Add(ac.id, 1);
            PushHud();
            Status = "Purchased " + Display(ac.name, ac.id) + "! Equip it from the Character screen.";
            Rebuild();
        }

        private static string AccessoryStats(AccessoryDef ac)
        {
            if (ac == null) return "Accessory";
            var bits = new List<string>();
            int dmg = RoundToInt(Max(0f, ac.damageMult) * 100f);
            int def = RoundToInt(Max(0f, ac.defense) * 100f);
            if (dmg > 0) bits.Add("+" + dmg + "% dmg");
            if (def > 0) bits.Add("+" + def + "% def");
            if (ac.hpBonus > 0) bits.Add("+" + ac.hpBonus + " hp");
            return bits.Count > 0 ? string.Join("   ", bits) : "Accessory";
        }

        private static string DescribeConsumable(DeNelle.Village.Items.ConsumableDef def)
        {
            if (def == null) return "";
            switch (def.Effect)
            {
                case DeNelle.Village.Items.ConsumableEffect.Heal: return "Restores health in a pinch.";
                case DeNelle.Village.Items.ConsumableEffect.Mana: return "Restores mana in a pinch.";
                case DeNelle.Village.Items.ConsumableEffect.Rest: return "A camp kit - full rest out of combat.";
                case DeNelle.Village.Items.ConsumableEffect.Buff: return "A combat tonic - temporary edge.";
                default: return "A consumable good.";
            }
        }

        // -- SELL dispatch (WO-598): each trade buys back its OWN bands ------------
        private void BuildSell()
        {
            if (Layout == VendorLayout.Goods)   { BuildSellGoods();   return; }
            if (Layout == VendorLayout.Jeweler) { BuildSellJeweler(); return; }
            BuildSellGear();
        }

        // -- WO-598 SELL (goods): owned consumables + non-gem materials, 50% refund --
        private void BuildSellGoods()
        {
            var inv = VillageInventory.Instance;
            if (inv == null) { Status = "No inventory."; return; }

            foreach (var kv in inv.Counts)
            {
                if (kv.Value <= 0) continue;
                string id = kv.Key;
                var cDef = DeNelle.Village.Items.ConsumableCatalog.Find(id);
                var mDef = cDef == null ? DeNelle.Village.Items.MaterialCatalog.Find(id) : null;
                if (cDef == null && (mDef == null || VendorStockResolver.IsGem(mDef))) continue;

                int price = cDef != null ? VendorStockResolver.PriceFor(cDef) : VendorStockResolver.PriceFor(mDef);
                AddSellGoodsRow(id,
                    cDef != null ? cDef.DisplayName : mDef.DisplayName,
                    cDef != null ? cDef.IconPath : mDef.IconPath,
                    cDef != null ? IconRolePotion : IconRoleMaterial,
                    kv.Value, price,
                    // WO-1584: the art CATEGORY + authored GLYPH travel with the row. A consumable
                    // has no material category (the View keeps the potion keyword mapper for it);
                    // a material's category IS the seam argument ForMaterial needs.
                    iconCategory: cDef != null ? null : mDef.Category,
                    glyph: cDef != null ? cDef.Glyph : mDef.Glyph,
                    description: cDef != null ? DescribeConsumable(cDef) : DescribeMaterial(mDef));
            }

            Status = _items.Count == 0
                ? "Nothing in your pack this stall would buy."
                : "Tap an item to SELL it for coins.";
        }

        // -- WO-598 SELL (jeweler): owned accessories + gems, 50% refund ------------
        private void BuildSellJeweler()
        {
            var inv = VillageInventory.Instance;
            if (inv == null) { Status = "No inventory."; return; }

            foreach (var kv in inv.Counts)
            {
                if (kv.Value <= 0) continue;
                string id = kv.Key;
                var ac = GearCatalog.FindAccessory(id);
                if (ac != null)
                {
                    var refund = ScaleCost(GearCatalog.GetBuyCost(ac), 0.50f);
                    string name = (string.IsNullOrEmpty(ac.name) ? ac.id : ac.name) + " x" + kv.Value;
                    _rowDetails[id] = new PartyShopDetail(AccessoryStats(ac), "",
                        DescribeGear(ac.job, ac.rarity), ac.iconPath, IconRoleAccessory, id);
                    string idCopy = id; var refundCopy = refund;
                    _rowActions[id] = () => SellGoods(idCopy, refundCopy);
                    _items.Add(new ItemVM(id, name, IconRoleAccessory, id, refund.Coins, "gold", true, ac.rarity));
                    continue;
                }
                var mDef = DeNelle.Village.Items.MaterialCatalog.Find(id);
                if (mDef == null || !VendorStockResolver.IsGem(mDef)) continue;
                AddSellGoodsRow(id, mDef.DisplayName, mDef.IconPath, IconRoleGem,
                    kv.Value, VendorStockResolver.PriceFor(mDef),
                    iconCategory: mDef.Category, glyph: mDef.Glyph,
                    description: DescribeMaterial(mDef));
            }

            Status = _items.Count == 0
                ? "No jewelry or cut stones in your pack to sell."
                : "Tap an item to SELL it for coins.";
        }

        // WO-1584. ONE producer for a goods/jeweler sell row: label, art keys (iconPath + the
        // authored CATEGORY + the authored GLYPH), the detail copy and the sell value all leave
        // here together, so the View has everything it needs and never re-derives any of it.
        //
        // ⛔ NO ROW WITHOUT A LABEL. A row whose display name AND id are both empty cannot be read,
        // cannot be tapped meaningfully and paints as the blank plate the owner reported - so it is
        // FAILED and SKIPPED here (§12: never a silent blank), not shipped to the View to paint.
        private void AddSellGoodsRow(string id, string displayName, string iconPath, string iconRole,
                                     int owned, int buyPrice,
                                     string iconCategory = null, string glyph = null,
                                     string description = null)
        {
            string label = string.IsNullOrEmpty(displayName) ? id : displayName;
            if (string.IsNullOrEmpty(label))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("PartyShop",
                    $"SELL row SKIPPED: an inventory entry (role='{iconRole}', owned={owned}) has NO " +
                    "display name AND no id - it would paint as an unreadable blank plate.");
                return;
            }

            string name = label + " x" + owned;
            var refund = new ResourceCost(coins: System.Math.Max(1, RoundToInt(buyPrice * 0.5f)));

            string stats = string.IsNullOrEmpty(iconCategory)
                ? "Owned " + owned
                : Cap(iconCategory) + "   Owned " + owned;
            string desc = string.IsNullOrEmpty(description) ? "From your pack." : description;

            _rowDetails[id] = new PartyShopDetail(stats, "", desc, iconPath, iconRole, id,
                                                  iconCategory, glyph);
            _rowSellCoins[id] = refund.Coins;
            _rowOwned[id] = owned;

            string idCopy = id; var refundCopy = refund;
            _rowActions[id] = () => SellGoods(idCopy, refundCopy);
            // The row VM carries the SAME art key as the detail - one producer, two consumers.
            _items.Add(new ItemVM(id, name, iconRole, id, refund.Coins, "gold", true,
                                  iconPath: iconPath));

            DeNelle.Core.Diagnostics.FlowTrace.Step("PartyShop",
                $"SELL row id='{id}' label='{name}' role='{iconRole}' " +
                $"iconPath='{(string.IsNullOrEmpty(iconPath) ? "<none>" : iconPath)}' " +
                $"category='{(string.IsNullOrEmpty(iconCategory) ? "<none>" : iconCategory)}' " +
                $"sell={refund.Coins}g owned={owned}");
        }

        /// <summary>
        /// WO-1584 - the one-line "what is this" for a crafting material. Prefers the AUTHORED
        /// flavour (materials.json `flavor`, WO-1042 - narrative copy is data, never a call site),
        /// and otherwise says what the CATEGORY is for. Same shape as
        /// <see cref="DescribeConsumable"/>, which has always worked this way.
        /// </summary>
        private static string DescribeMaterial(DeNelle.Village.Items.MaterialDef def)
        {
            if (def == null) return "";
            if (!string.IsNullOrEmpty(def.Flavor)) return def.Flavor;
            switch ((def.Category ?? "").ToLowerInvariant())
            {
                case "herb":
                case "petal":
                case "fungus":  return "A gathered ingredient for the workshop's brews.";
                case "crystal":
                case "gem":     return "A cut stone - fuel for jewelry and enchanting work.";
                case "metal":
                case "ore":
                case "ingot":   return "Salvaged metal the forge can work back into gear.";
                case "stone":   return "Worked stone for the heavier bench recipes.";
                case "cloth":   return "Scrap fabric - bindings, wrappings and padding.";
                case "wood":    return "Seasoned timber for hafts, shafts and frames.";
                case "liquid":  return "A bottled reagent for the brewing bench.";
                case "bone":    return "Bone stock for the coarser reagents.";
                case "dust":
                case "resin":   return "A fine reagent the workshop measures by the pinch.";
                default:        return "A crafting ingredient for the workshop's recipes.";
            }
        }

        private void SellGoods(string id, ResourceCost refund)
        {
            var inv = VillageInventory.Instance;
            if (inv == null || inv.Get(id) <= 0) { Status = "You don't own that."; return; }
            if (!inv.TryConsume(id, 1)) { Status = "Couldn't sell that."; return; }
            _economy?.Grant(refund);
            PushHud();
            Status = "Sold for +" + CostString(refund) + ".";
            SelectedId = null;
            Rebuild();
        }

        // -- SELL (gear) - owned gear (any kind the shop type accepts), credits coins, same screen --
        private void BuildSellGear()
        {
            var member = SelectedMember;

            // Armor leads in the SELL list too (armor/weapons-first), narrowed by the category dropdown.
            foreach (var (a, qty) in _store.OwnedArmor())
            {
                if (a == null || (_storeKinds & GearKind.Armor) == 0) continue;
                // WO-578: OwnedArmor now UNIONs auto-equipped gear (display "owned"), but only LEDGER
                // gear (a real VillageInventory count) is sellable — skip equipped-only pieces so the
                // SELL list never shows a phantom row that SellGear would reject as "you don't own that".
                if (_store.OwnedQuantity(a.id) <= 0) continue;
                if (_category == PartyShopCategory.Weapons || _category == PartyShopCategory.OffHand) continue;
                if (!ArmorPassesType(a)) continue;   // WO-501 TYPE narrow (registers availability)
                bool equipped = member != null && member.EquippedArmor != null &&
                                string.Equals(member.EquippedArmor.id, a.id, StringComparison.OrdinalIgnoreCase);
                var refund = ScaleCost(GearCatalog.GetBuyCost(a), 0.50f);
                string id = a.id;
                string name = (string.IsNullOrEmpty(a.name) ? a.id : a.name) + " x" + qty;

                _rowDetails[id] = new PartyShopDetail(
                    ArmorStats(a), "", DescribeGear(a.job, a.rarity), a.iconPath, IconRoleArmor, id);
                _rowActions[id] = () => SellGear(a.id, refund);
                _items.Add(new ItemVM(id, name, IconRoleArmor, id, refund.Coins, "gold", true, a.rarity, equipped: equipped));
            }

            foreach (var (w, qty) in _store.OwnedWeapons())
            {
                if (w == null || (_storeKinds & GearKind.Weapon) == 0) continue;
                // WO-578: SELL only LEDGER gear (see the armor loop above) — skip equipped-only pieces.
                if (_store.OwnedQuantity(w.id) <= 0) continue;
                if (_category == PartyShopCategory.Armor) continue;
                if (_category == PartyShopCategory.Weapons && w.IsOffHandItem) continue;
                if (_category == PartyShopCategory.OffHand && !w.IsOffHandItem) continue;
                if (!WeaponPassesType(w)) continue;   // WO-501 TYPE narrow (registers availability)
                var equippedDef = w.IsOffHandItem ? member?.EquippedOffHand : member?.EquippedWeapon;
                bool equipped = equippedDef != null &&
                                string.Equals(equippedDef.id, w.id, StringComparison.OrdinalIgnoreCase);
                var refund = ScaleCost(GearCatalog.GetBuyCost(w), 0.50f);
                string id = w.id;
                string name = (string.IsNullOrEmpty(w.name) ? w.id : w.name) + " x" + qty;

                _rowDetails[id] = new PartyShopDetail(
                    WeaponStats(w), "", DescribeGear(w.job, w.rarity), w.iconPath, IconRoleWeapon, id);
                _rowActions[id] = () => SellGear(w.id, refund);
                _items.Add(new ItemVM(id, name, IconRoleWeapon, id, refund.Coins, "gold", true, w.rarity, equipped: equipped));
            }

            Status = _items.Count == 0
                ? "You own no gear to sell here."
                : "Tap an item to SELL it for coins - buy without leaving.";
        }

        // -- Finer weapon/armor TYPE narrow (WO-501, read PURELY from def flags) ------
        // Capture which TYPE a candidate row belongs to (so the View shows only live chips),
        // then return whether it passes the active _type narrow. Called per candidate ahead of
        // adding the row; a weapon that is neither 1h/2h/shield (shouldn't happen) registers
        // nothing and passes only when _type==Any.

        private bool WeaponPassesType(WeaponDef w)
        {
            if (w == null) return false;
            PartyShopType t = w.IsOffHandItem ? PartyShopType.Shield
                            : w.IsTwoHanded   ? PartyShopType.TwoHand
                            : w.IsOneHandedMain ? PartyShopType.OneHand
                            : PartyShopType.Any;
            if (t != PartyShopType.Any && !_availableTypes.Contains(t)) _availableTypes.Add(t);
            // An ARMOR type narrow (Light/Heavy) drops all weapons; a weapon type passes only its own kind.
            if (_type == PartyShopType.Light || _type == PartyShopType.Heavy) return false;
            return _type == PartyShopType.Any || _type == t;
        }

        private bool ArmorPassesType(ArmorDef a)
        {
            if (a == null) return false;
            string wt = (a.weight ?? "").Trim().ToLowerInvariant();
            PartyShopType t = wt == "light" ? PartyShopType.Light
                            : wt == "heavy" ? PartyShopType.Heavy
                            : PartyShopType.Any;
            if (t != PartyShopType.Any && !_availableTypes.Contains(t)) _availableTypes.Add(t);
            // An armor TYPE narrow (Light/Heavy) never hides weapon-only chip selections and vice versa:
            // when a WEAPON type is active, armor rows are dropped; when an ARMOR type is active, weapons drop.
            if (_type == PartyShopType.OneHand || _type == PartyShopType.TwoHand || _type == PartyShopType.Shield)
                return false;
            return _type == PartyShopType.Any || _type == t;
        }

        // -- Transactions ---------------------------------------------------------

        private void BuyWeapon(WeaponDef w)
        {
            if (w == null) return;
            if (_economy == null) { Status = "Economy unavailable."; return; }
            var cost = GearCatalog.GetBuyCost(w);
            if (!_economy.TrySpend(cost))
            {
                Status = "Not enough gold for " + Display(w.name, w.id) + " - needs " + CostString(cost) + ".";
                return;
            }
            VillageInventory.Instance?.Add(w.id, 1);
            PushHud();
            Status = "Bought " + Display(w.name, w.id) + ". Tap again to equip; your current loadout was kept.";
            Rebuild();
        }

        private void BuyArmor(ArmorDef a)
        {
            if (a == null) return;
            if (_economy == null) { Status = "Economy unavailable."; return; }
            var cost = GearCatalog.GetBuyCost(a);
            if (!_economy.TrySpend(cost))
            {
                Status = "Not enough gold for " + Display(a.name, a.id) + " - needs " + CostString(cost) + ".";
                return;
            }
            VillageInventory.Instance?.Add(a.id, 1);
            PushHud();
            Status = "Bought " + Display(a.name, a.id) + ". Tap again to equip; your current loadout was kept.";
            Rebuild();
        }

        private void EquipWeapon(WeaponDef w)
        {
            var member = SelectedMember;
            if (member == null) { Status = "No hero selected to equip."; return; }
            if (w == null) return;

            // WO-1214 Ruling 2 - ask the ONE authority BEFORE claiming success. GearLoadout's
            // equip seam now fails closed on class + level + the armed-hero invariant, and a
            // vendor screen that printed "Equipped X" over a refused equip would be exactly the
            // silent failure this ticket exists to end.
            if (!GearCatalog.CanEquipWeaponNow(w, member.EquippedWeapon, member.TargetClass,
                                               member.TargetLevel, out string refusal, out _))
            {
                Status = refusal;
                Rebuild();
                return;
            }

            if (w.IsOffHandItem) member.EquipOffHandById(w.id);
            else member.EquipWeaponById(w.id);
            Status = "Equipped " + Display(w.name, w.id) + " to " + MemberName() + ".";
            Rebuild();
        }

        private void EquipArmor(ArmorDef a)
        {
            var member = SelectedMember;
            if (member == null) { Status = "No hero selected to equip."; return; }
            if (a == null) return;

            // WO-1214 Ruling 2 - see EquipWeapon: never report an equip the seam refused.
            if (!GearCatalog.CanEquipArmorNow(a, member.TargetClass, member.TargetLevel,
                                              out string refusal, out _))
            {
                Status = refusal;
                Rebuild();
                return;
            }

            member.EquipArmorById(a.id);
            Status = "Equipped " + Display(a.name, a.id) + " to " + MemberName() + ".";
            Rebuild();
        }

        private void SellGear(string id, ResourceCost refund)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_store == null || _store.OwnedQuantity(id) <= 0) { Status = "You don't own that."; return; }
            if (!_store.TryRemove(id, 1)) { Status = "Couldn't sell that."; return; }
            _economy?.Grant(refund);
            PushHud();
            Status = "Sold for +" + CostString(refund) + ".";
            SelectedId = null;
            Rebuild();
        }

        private void PushHud()
        {
            if (_economy == null) return;
            DeNelle.Core.CoreServices.Hud?.SetResources(_economy.Wood, _economy.Iron, _economy.Food, _economy.Crystals);
        }

        // -- Title ----------------------------------------------------------------
        //
        // ⛔ This was a COPY of the legacy ShopVM.ResolveTitle, and its own comment said so
        // ("mirrors ShopVM.ResolveTitle"). Both copies invented the header from
        // substrings of the vendor context, which is how the weapon shop and the armour
        // shop wore each other's names. There is now exactly ONE implementation
        // (VendorStockResolver) and this VM calls it; it resolves the word from the catalog
        // row that claims the vendor's ROLE (StructureRoles), the single naming authority.
        // (ShopVM itself was DELETED on 2026-09-06 with its doorless ShopPanel - WO-1430.)

        private string ResolveTitle() =>
            VendorStockResolver.TitleFor(_vendorContext, _displayName);

        // -- Stat / delta / cost formatting (pure; System.Math) --------------------

        private string MemberName()
        {
            var m = SelectedMember;
            return m == null || string.IsNullOrEmpty(m.TargetName) ? "this hero" : m.TargetName;
        }

        private static string Display(string name, string id) => string.IsNullOrEmpty(name) ? id : name;

        private static string WeaponStats(WeaponDef w)
        {
            if (w == null) return "";
            int dmgPct = RoundToInt((Max(0.1f, w.damageMult) - 1f) * 100f);
            string s = "+" + dmgPct + "% dmg";
            if (w.reach > 0f) s += "   reach " + Fmt1(w.reach) + "m";
            if (!string.IsNullOrEmpty(w.damageType)) s += "   " + w.damageType;
            if (!string.IsNullOrEmpty(w.hand)) s += "   " + w.hand;
            return s;
        }

        private static string ArmorStats(ArmorDef a)
        {
            if (a == null) return "";
            int defPct = RoundToInt(Clamp(a.defense, 0f, GearLoadout.MaxArmorDefense) * 100f);
            string s = "+" + defPct + "% def";
            if (a.hpBonus > 0f) s += "   +" + Fmt1(a.hpBonus) + " hp";
            if (!string.IsNullOrEmpty(a.weight)) s += "   " + a.weight;
            return s;
        }

        private string DeltaVsEquippedWeapon(WeaponDef w)
        {
            var m = SelectedMember;
            if (w == null || m == null || m.EquippedWeapon == null) return "";
            int cur = RoundToInt((Max(0.1f, m.EquippedWeapon.damageMult) - 1f) * 100f);
            int nw  = RoundToInt((Max(0.1f, w.damageMult) - 1f) * 100f);
            int d = nw - cur;
            return d == 0 ? "= equipped" : (d > 0 ? "+" + d + "% dmg vs equipped" : d + "% dmg vs equipped");
        }

        private string DeltaVsEquippedArmor(ArmorDef a)
        {
            var m = SelectedMember;
            if (a == null || m == null || m.EquippedArmor == null) return "";
            // CAP: GearLoadout.MaxArmorDefense on BOTH sides of the delta - a display-only
            // literal here is exactly how the shown number drifted from the applied one.
            int cur = RoundToInt(Clamp(m.EquippedArmor.defense, 0f, GearLoadout.MaxArmorDefense) * 100f);
            int nw  = RoundToInt(Clamp(a.defense, 0f, GearLoadout.MaxArmorDefense) * 100f);
            int d = nw - cur;
            return d == 0 ? "= equipped" : (d > 0 ? "+" + d + "% def vs equipped" : d + "% def vs equipped");
        }

        // -- Readable per-stat SPEC lines + per-stat delta vs the equipped piece (WO: weapons matter) --
        // Reads the RAW stat fields the def exposes (WeaponDef.damageMult/reach, ArmorDef.defense/hpBonus)
        // and, when a comparable piece is equipped on the selected member (BUY tab), the signed delta.
        // Delta is suppressed on SELL (you'd be comparing the item to itself/your own loadout) and when
        // nothing comparable is equipped - then the raw stat shows with no tint.
        private IReadOnlyList<PartyShopSpec> BuildSpecs(string id)
        {
            var list = new List<PartyShopSpec>();
            if (string.IsNullOrEmpty(id)) return list;
            bool compare = _tab == PartyShopTab.Buy;
            var m = SelectedMember;

            var w = GearCatalog.FindWeapon(id);
            if (w != null)
            {
                // Damage: a derived whole number from the multiplier (the "damage" the player
                // reads). WO-808: BOTH sides resolve through the gear-level ladder so an owned
                // improved piece (and the equipped comparison) show their LIVE power.
                float dmg = DamageOutputPct(GearStatResolver.EffectiveDamageMult(w, GearLevel(w.id)));
                bool cmp = compare && m != null && m.EquippedWeapon != null
                           && !string.Equals(m.EquippedWeapon.id, w.id, StringComparison.OrdinalIgnoreCase);
                float curDmg = cmp
                    ? DamageOutputPct(GearStatResolver.EffectiveDamageMult(m.EquippedWeapon, GearLevel(m.EquippedWeapon.id)))
                    : 0f;
                list.Add(MakeSpec("Damage output", Fmt0(dmg) + "%", cmp, dmg - curDmg, 0.05f, "%"));

                if (w.reach > 0f || (cmp && m.EquippedWeapon.reach > 0f))
                {
                    float curReach = cmp ? m.EquippedWeapon.reach : 0f;
                    list.Add(MakeSpec("Reach", Fmt1(w.reach) + "m", cmp, w.reach - curReach, 0.05f, "m"));
                }

                string hand = w.IsTwoHanded ? "Two-handed - shield removed when equipped"
                    : w.IsOffHandItem ? "Off-hand shield" : "One-handed";
                list.Add(new PartyShopSpec("Hands", hand, "", 0));

                var element = DeNelle.Core.Combat.ElementalDamageResolver.ParseElement(w.element);
                if (element != DeNelle.Core.Combat.DamageElement.None)
                {
                    string e = element.ToString();
                    list.Add(new PartyShopSpec("Affinity", e, "", 0));
                    list.Add(new PartyShopSpec("Matchup", "Strong +25% / resisted -25%", "", 0));
                }

                string effectKind = !string.IsNullOrEmpty(w.effectKind) ? w.effectKind : w.ammoEffect;
                if (!string.IsNullOrEmpty(effectKind))
                {
                    float effectSeconds = w.effectDurationSeconds > 0f ? w.effectDurationSeconds : w.ammoSeconds;
                    string chance = w.effectChance > 0f ? Fmt0(w.effectChance * 100f) + "% " : "";
                    string duration = effectSeconds > 0f ? " for " + Fmt1(effectSeconds) + " sec" : "";
                    string stacks = w.effectMaxStacks > 0 ? " - max " + w.effectMaxStacks : "";
                    list.Add(new PartyShopSpec("Special", chance + Cap(effectKind) + duration + stacks, "", 0));
                }

                string style = w.IsTwoHanded ? "Heavy" : element == DeNelle.Core.Combat.DamageElement.Flame
                    ? "Burn" : element == DeNelle.Core.Combat.DamageElement.Ice ? "Control" : "Reliable";
                bool hotSwapReady = _store != null && _store.OwnedQuantity(w.id) > 0;
                list.Add(new PartyShopSpec("Hot swap",
                    hotSwapReady ? "READY - " + style : "Available after purchase - " + style, "", 0));
                list.Add(new PartyShopSpec("Purchase", "Keeps current gear and assignments", "", 0));

                // WO-808: owned + improvable -> the before->after preview the owner specced
                // ("Lcurrent -> Lnext, damage before -> after"). Green (+1) — an Improve is
                // always an upgrade; the ladder never authors a downgrade.
                int lvl = GearLevel(w.id);
                if (_store != null && _store.OwnedQuantity(w.id) > 0 && GearProgression.HasNextLevel(w.rarity, lvl))
                {
                    float nextDmg = DamageOutputPct(GearStatResolver.EffectiveDamageMult(w, lvl + 1));
                    list.Add(new PartyShopSpec("Improve", "Lv " + lvl + " -> " + (lvl + 1),
                        FmtDelta(nextDmg - dmg, "% output"), 1));
                }

                // WO-814 (owner ruling 2026-08-24, batch 2 ruling 11 §3): the max-level ability is
                // shown from Level 1 - "Lv 5: <ability>" - so the goal is visible the whole way up
                // rather than a surprise at the end. Deliberately OUTSIDE the ownership gate above:
                // the promise is a property of the RARITY, so it reads the same on a shop row as on
                // an owned one. Sign 0 (no colour-carried meaning). Once the piece is at max the
                // earned ability replaces the locked line. Both are silent while the band's
                // weaponAbilities are unauthored - see GearStatResolver.LockedAbilityLine.
                var earned = GearStatResolver.AbilityFor(w, lvl);
                if (earned != null && !string.IsNullOrEmpty(earned.DisplayName))
                {
                    list.Add(new PartyShopSpec("Ability", earned.DisplayName, "", 0));
                    if (!string.IsNullOrEmpty(earned.Description))
                        list.Add(new PartyShopSpec("", earned.Description, "", 0));
                }
                string locked = GearStatResolver.LockedAbilityLine(w.rarity, lvl);
                if (!string.IsNullOrEmpty(locked))
                    list.Add(new PartyShopSpec("Ability", locked, "", 0));
                return list;
            }

            var a = GearCatalog.FindArmor(id);
            if (a != null)
            {
                bool cmp = compare && m != null && m.EquippedArmor != null
                           && !string.Equals(m.EquippedArmor.id, a.id, StringComparison.OrdinalIgnoreCase);
                float curDef = cmp
                    ? GearStatResolver.EffectiveDefense(m.EquippedArmor, GearLevel(m.EquippedArmor.id))
                    : 0f;
                float def = GearStatResolver.EffectiveDefense(a, GearLevel(a.id));
                list.Add(MakeSpec("Defense", Fmt2(def), cmp, def - curDef, 0.005f));

                if (a.hpBonus > 0f || (cmp && m.EquippedArmor.hpBonus > 0f))
                {
                    float curHp = cmp ? m.EquippedArmor.hpBonus : 0f;
                    list.Add(MakeSpec("HP", Fmt0(a.hpBonus), cmp, a.hpBonus - curHp, 0.5f));
                }

                int lvl = GearLevel(a.id);
                if (_store != null && _store.OwnedQuantity(a.id) > 0 && GearProgression.HasNextLevel(a.rarity, lvl))
                {
                    float nextDef = GearStatResolver.EffectiveDefense(a, lvl + 1);
                    list.Add(new PartyShopSpec("Improve", "Lv " + lvl + " -> " + (lvl + 1),
                        FmtDelta(nextDef - def, " def"), 1));
                }
                return list;
            }

            // WO-1584 - NON-GEAR rows used to return an EMPTY list here, which is why the owner's
            // 2026-09-07 Seeker frame showed a detail column holding only a name and "From your
            // pack." for a stack worth 2 gold. A material/consumable has real facts to read out:
            // what it is, how many are held, and what the counter pays for one. Ruling 29 (the
            // screen is filled, with context) applies to the detail column too.
            if (_rowDetails.TryGetValue(id, out var goods))
            {
                if (!string.IsNullOrEmpty(goods.IconCategory))
                    list.Add(new PartyShopSpec("Category", Cap(goods.IconCategory), "", 0));
                else if (goods.IconRole == IconRolePotion)
                    list.Add(new PartyShopSpec("Category", "Consumable", "", 0));

                if (_rowOwned.TryGetValue(id, out int held) && held > 0)
                    list.Add(new PartyShopSpec("In pack", held.ToString(), "", 0));

                if (_tab == PartyShopTab.Sell && _rowSellCoins.TryGetValue(id, out int coins))
                    list.Add(new PartyShopSpec("Sells for", coins + " Gold", "", 0));
            }
            return list;   // craftable rows still carry no stat block
        }

        /// <summary>WO-808: the owned instance's gear level (1 baseline). VM-side state read —
        /// the MVVM law keeps GameStateService out of Views; this is the sanctioned seam.</summary>
        private static int GearLevel(string id) =>
            GearProgression.GearLevelOf(
                DeNelle.Core.State.GameStateService.Instance != null
                    ? DeNelle.Core.State.GameStateService.Instance.State : null, id);

        // Build one spec line: format the signed delta + classify the sign (with a small epsilon so a
        // float wobble doesn't read as a change). suffix is appended to the delta number (e.g. "m").
        private static PartyShopSpec MakeSpec(string label, string value, bool compare, float delta, float eps, string suffix = "")
        {
            if (!compare) return new PartyShopSpec(label, value, "", 0);
            int sign = delta > eps ? 1 : (delta < -eps ? -1 : 0);
            string ds = FmtDelta(delta, suffix);
            return new PartyShopSpec(label, value, ds, sign);
        }

        // Truthful applied weapon-output factor. The former nominal-base-20 number looked absolute
        // but was not the selected hero's damage authority. A percentage is exact for every class.
        private static float DamageOutputPct(float mult) => Max(0f, Max(0.1f, mult) * 100f);

        private static string FmtDelta(float v, string suffix)
        {
            // Round to the value's display precision so "(+5)"/"(-0.02)" matches the shown value.
            string sign = v >= 0f ? "+" : "-";
            float a = v < 0f ? -v : v;
            string num = a >= 10f ? Fmt0(a) : (a >= 1f ? Fmt1(a) : Fmt2(a));
            return sign + num + suffix;
        }

        private static string DescribeGear(string job, string rarity)
        {
            string r = string.IsNullOrEmpty(rarity) ? "" : char.ToUpper(rarity[0]) + (rarity.Length > 1 ? rarity.Substring(1) : "");
            // WO-1241: `job` may now be a comma-separated class LIST ("knight,cleric"), so the
            // raw field can no longer be pasted into a sentence. GearCatalog.JobLabel is the ONE
            // formatter all three DescribeGear copies share.
            string j = GearCatalog.JobLabel(job);
            return (string.IsNullOrEmpty(r) ? "" : r + " gear. ") + "Suited to " + j + ".";
        }

        private ResourceCost ScaleCost(ResourceCost c, float f) =>
            new ResourceCost(
                RoundToInt(c.Wood * f), RoundToInt(c.Food * f), RoundToInt(c.Iron * f),
                RoundToInt(c.Crystals * f), RoundToInt(c.Coins * f));

        private static string CostString(ResourceCost c)
        {
            // WO-697: cost numbers through the ONE kit formatter (compact >= 10k).
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[] { ("gold", "Gold", c.Coins), ("wood", "Wood", c.Wood), ("iron", "Iron", c.Iron), ("stone", "Stone", c.Food), ("crystal", "Crystals", c.Crystals) });
            return parts.Count == 0 ? "Free" : DeNelle.Core.UI.CostFormat.Words(parts);
        }

        // ?12 INSTRUMENT helper: dump EVERY field of a ResourceCost (not just non-zero like CostString),
        // so the affordability trace proves whether the cost is in the right currency (coins) or wrongly
        // carries wood/iron/food/crystals, and whether the amount is sane vs huge.
        private static string DescribeCost(ResourceCost c) =>
            "{coins=" + c.Coins + " wood=" + c.Wood + " iron=" + c.Iron
            + " food=" + c.Food + " crystals=" + c.Crystals + "}";

        private static string Cap(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // -- Pure math (System.Math - keeps the VM Unity-UI-free) ------------------
        private static int RoundToInt(float f) => (int)Math.Floor(f + 0.5f);
        private static float Max(float a, float b) => a > b ? a : b;
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static string Fmt0(float v) => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        private static string Fmt1(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        private static string Fmt2(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
