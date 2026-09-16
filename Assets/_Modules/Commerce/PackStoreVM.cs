// =============================================================================
// PackStoreVM — the pack-store's game-state seam (WO-744 MVVM migration).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Commerce   Namespace: DeNelle.Wallet
//
// Extracted from PackStore (the View) so the View stops naming GameStateService
// and the MarketplaceInteractor scene-resolve (FindFirstObjectOfType). The MONEY
// / ENTITLEMENT PATH is unchanged — ApplyPackContents / RecordOwned / IsOwned are
// moved VERBATIM (same crystals/food/coins top-up, same owned-SKU record, same
// self-reporting FlowTrace + Save). The async WalletService purchase orchestration
// stays in the View (it drives the status banner + card re-render); it now asks
// THIS VM to read ownership + apply contents instead of touching GameState itself.
//
// Also owns the store-close resolve: TryCloseViaInteractor (drives
// MarketplaceInteractor.CloseStore via reflection — the DeNelle.Wallet -> Village
// one-way asmdef guard) + the ReEnableDisabledHeroLocomotion fallback, so the soft-
// lock guard behaviour is preserved but the View no longer names FindFirstObjectOfType.
//
// GameState is resolved through an injected provider (CreateDefault -> GameStateService)
// so the VM is unit-testable with a plain GameState (ARCHITECTURE_PRINCIPLES §2c).
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Wallet
{
    /// <summary>
    /// The pack store's game-state ViewModel: ownership queries + the verbatim entitlement grant +
    /// the interactor-driven close resolve. Keeps the View free of GameStateService / scene finds.
    /// </summary>
    public sealed class PackStoreVM
    {
        private readonly Func<GameState> _stateProvider;

        /// <summary>Sole resolution site — binds the live GameState so the View never names the singleton.</summary>
        public static PackStoreVM CreateDefault() =>
            new PackStoreVM(() => GameStateService.Instance != null ? GameStateService.Instance.State : null);

        public PackStoreVM(Func<GameState> stateProvider)
        {
            _stateProvider = stateProvider;
        }

        private GameState State => _stateProvider != null ? _stateProvider() : null;

        // ── Ownership ─────────────────────────────────────────────────────────

        /// <summary>
        /// True when the pack SKU is already in the player's owned items.
        /// <para>⚠ Checks EVERY id the pack has ever shipped under, not just its current one
        /// (PackCatalog.OwnershipKeysFor). OwnedItemIds is a plain ordinal string list that is
        /// NEVER rewritten — a save made before a SKU was renamed still carries the OLD string, so
        /// a bare <c>Contains(pack.Sku)</c> would report a paid-for pack as unowned and re-offer it
        /// for sale. That is a double-charge, which is why this goes through the alias resolver.</para>
        /// </summary>
        public bool IsOwned(string sku)
        {
            var state = State;
            if (state == null || state.OwnedItemIds == null || string.IsNullOrEmpty(sku)) return false;
            if (state.OwnedItemIds.Contains(sku)) return true;
            foreach (var key in PackCatalog.OwnershipKeysFor(sku))
                if (!string.IsNullOrEmpty(key) && state.OwnedItemIds.Contains(key))
                {
                    FlowTrace.Step("Pack",
                        $"IsOwned('{sku}'): matched via RETIRED id '{key}' — pre-rename entitlement honoured.");
                    return true;
                }
            return false;
        }

        // ── Entitlement grant (money/reward path — moved VERBATIM from PackStore) ──

        /// <summary>
        /// Applies a purchased pack's contents to the live game state — the economy top-up lands in
        /// the resource wallet and the pack SKU plus its cosmetic SKUs are recorded as owned. Mirrors
        /// the React entitlement fulfilment (storeItems.ts purchaseGrantFor + grantItem). Behaviour is
        /// unchanged from PackStore.ApplyPackContents — same order, same self-reporting, same Save.
        /// </summary>
        public void ApplyPackContents(PackDef pack)
        {
            using var _ = FlowTrace.Enter("Store", $"ApplyPackContents '{pack?.Sku ?? "<null>"}'");
            var state = State;
            if (state == null)
            {
                // The payment already confirmed by the time we reach here — no GameState = the player
                // paid and the entitlement is LOST. Fail loudly (was a swallowed LogWarning).
                FlowTrace.Fail("Store",
                    $"ApplyPackContents: no GameStateService/State — pack '{pack?.Sku ?? "<null>"}' contents NOT applied. " +
                    "If this followed a confirmed payment, the player is CHARGED with NO entitlement.");
                return;
            }

            // Economy layer - route EVERY advertised currency through its canonical, persisted,
            // HUD-refreshing grant seam (ECON-01 fix). The old code wrote state.Resources DIRECTLY
            // and applied ONLY Crystals/Food/Coins, so Wood/Iron were silently dropped. Now:
            //   Wood/Iron/Food/Crystals     -> EconomyService.GrantSpendable(int,int,int,int)
            //   Coins (Gold)                -> EconomyService.AddCoins(int)
            // Each currency is passed to EXACTLY ONE seam, so nothing is double-granted.
            var econ = pack.Contents != null ? pack.Contents.Economy : null;
            int gWood = 0, gIron = 0, gFood = 0, gCrystals = 0, gCoins = 0;
            if (econ != null)
            {
                gWood     = Mathf.Max(0, econ.Wood);
                gIron     = Mathf.Max(0, econ.Iron);
                gFood     = Mathf.Max(0, econ.Stone);
                gCrystals = Mathf.Max(0, econ.Crystals);
                gCoins    = Mathf.Max(0, econ.Coins);

                // Wood/Iron/Food/Crystals land in a single GrantSpendable (mirrors Wood/Iron to the
                // persisted GameState ledger AND routes Food/Crystals through AddStone/AddCrystals ->
                // Save + ResourcesChanged); Coins(Gold) land via AddCoins.
                if (gWood > 0 || gIron > 0 || gFood > 0 || gCrystals > 0)
                    TryGrantResources(gWood, gFood, gIron, gCrystals, pack.Sku);
                if (gCoins > 0) TryGrantCoins(gCoins, pack.Sku);
            }

            // Ownership — the pack SKU + every cosmetic SKU it grants.
            // WO-1253: capture already-had BEFORE RecordOwned so a re-settle of the
            // permanent-builder SKU logs already-had and cannot grant a second crew.
            bool alreadyHadBuilder = PackCatalog.IsPermanentBuilderSku(pack.Sku) && IsOwned(pack.Sku);
            RecordOwned(state.OwnedItemIds, pack.Sku);
            int cosmeticCount = 0;
            if (pack.Contents != null && pack.Contents.Cosmetics != null)
                foreach (var sku in pack.Contents.Cosmetics)
                {
                    RecordOwned(state.OwnedItemIds, sku);
                    // ECON-02 fix: the wardrobe reads CosmeticOwnershipService ownership,
                    // NOT GameState.OwnedItemIds. Without this, a pack cosmetic is "owned" to the pack
                    // system yet CosmeticOwnershipService.Owns(sku)==false and Equip(sku) no-ops (split-
                    // brain, economy-meta FLAG #16). Grant ownership into the wardrobe store too via
                    // GrantAchievement (the outside-the-spend own-set writer). Write BOTH stores so the
                    // pack IsOwned check and the wardrobe agree; GrantAchievement is idempotent.
                    if (!string.IsNullOrEmpty(sku)) { TryGrantCosmeticOwnership(sku, pack.Sku); cosmeticCount++; }
                }

            // Convenience tokens land in GearInventory under "convenience:<kind>".
            // Redeemers (Lantern oil; WO-1246 ConvenienceRedeemer for instant-build /
            // instant-repair / harvest-auto-collect / xp-weekend) spend those keys.
            // Permanent-builder is SKU ownership, not a stacked token (WO-1253).

            // VERIFY the entitlement actually landed before persisting — the SKU must now be owned, or
            // the paid-for grant silently failed. This is the proof the entitlement took.
            int convenienceTokensLanded = 0;
            if (pack.Contents != null && pack.Contents.Convenience != null)
                foreach (var item in pack.Contents.Convenience)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Kind) || item.Count <= 0) continue;
                    // Permanent-builder concurrency is SKU ownership, not a stacked token.
                    // Skip the GearInventory increment on a re-settle so even the unread
                    // tray stays idempotent.
                    if (alreadyHadBuilder && PackCatalog.IsPermanentBuilderKind(item.Kind)) continue;
                    string key = "convenience:" + item.Kind.Trim().ToLowerInvariant();
                    if (state.GearInventory == null) state.GearInventory = new Dictionary<string, int>();
                    state.GearInventory.TryGetValue(key, out int prior);
                    state.GearInventory[key] = Mathf.Max(0, prior) + item.Count;
                    convenienceTokensLanded += item.Count;
                    FlowTrace.Step("Store", $"ApplyPackContents: convenience token '{key}' +{item.Count} -> {state.GearInventory[key]}.");
                }

            // WO-1388 - a 'temporary-builder' token must START its crew now, not on the next sweep.
            // Same reflection bridge as the permanent-builder hook; a miss is logged, never silent,
            // and the token is still in GearInventory for BuildTimerService's own sweep to spend.
            if (convenienceTokensLanded > 0) TryNotifyConvenienceTokensGranted(pack.Sku);

            if (PackCatalog.IsPermanentBuilderSku(pack.Sku))
            {
                FlowTrace.Step("Store", "player bought builder");
                FlowTrace.Step("Store", alreadyHadBuilder
                    ? "player bought builder already-had"
                    : "player bought builder applied");
                TryNotifyPermanentBuilderGrant(alreadyHadBuilder);
            }

            bool owned = state.OwnedItemIds != null && state.OwnedItemIds.Contains(pack.Sku);
            if (!owned)
                FlowTrace.Fail("Store",
                    $"ApplyPackContents: pack '{pack.Sku}' NOT recorded as owned after grant — entitlement did NOT take (player may be charged with nothing to show).");
            else
                FlowTrace.Step("Store", $"ApplyPackContents: pack '{pack.Sku}' recorded owned + economy applied.");

            // ECON-01/02 PROOF LINE - logs EXACTLY what this pack granted (every currency delta + the
            // cosmetic-ownership count) so a silent drop can never recur unseen. If any figure here reads
            // 0 for something the pack card advertised, the grant seam is broken and the trace shows it.
            FlowTrace.Step("Pack",
                $"granted '{pack.Sku}': wood={gWood} iron={gIron} food={gFood} " +
                $"crystals={gCrystals} coins={gCoins} cosmetics={cosmeticCount} " +
                "(each routed through its canonical persisted seam - GrantSpendable / AddCoins / GrantAchievement)");

            // Persist through the service so the save round-trips.
            FlowTrace.Try("Store", $"save after granting '{pack.Sku}'", () =>
            {
                var svc = GameStateService.Instance;
                if (svc != null) svc.Save();
            });
        }

        /// <summary>
        /// Applies a server-snapshotted promo pack without consulting <see cref="PackCatalog"/>.
        /// The SKU is still recorded as the durable entitlement key, but the contents delivered by
        /// the redeem response are the sole grant authority. This is intentionally a tiny adapter
        /// over the purchased-pack seam so promo grants cannot drift into a second economy path.
        /// </summary>
        public void ApplyPackContents(string sku, PackContents contents)
        {
            if (string.IsNullOrWhiteSpace(sku) || contents == null)
            {
                FlowTrace.Fail("Promo", "inline pack reward refused: missing SKU or contents; no catalog fallback attempted.");
                return;
            }

            ApplyPackContents(new PackDef
            {
                Sku = sku.Trim(),
                Name = sku.Trim(),
                StoreVisible = false,
                Contents = contents,
            });
        }

        /// <summary>Restores only durable ownership for an already-fulfilled server entitlement.
        /// Economy and convenience consumables are deliberately never replayed.</summary>
        public void RestoreFulfilledOwnership(PackDef pack)
        {
            var state = State;
            if (state == null || pack == null) return;
            if (state.OwnedItemIds == null) state.OwnedItemIds = new List<string>();
            bool alreadyHadBuilder = PackCatalog.IsPermanentBuilderSku(pack.Sku) &&
                                     state.OwnedItemIds != null &&
                                     PackCatalog.OwnsPermanentBuilder(state.OwnedItemIds);
            RecordOwned(state.OwnedItemIds, pack.Sku);
            if (pack.Contents != null && pack.Contents.Cosmetics != null)
                foreach (var sku in pack.Contents.Cosmetics)
                {
                    RecordOwned(state.OwnedItemIds, sku);
                    if (!string.IsNullOrEmpty(sku)) TryGrantCosmeticOwnership(sku, pack.Sku);
                }
            if (PackCatalog.IsPermanentBuilderSku(pack.Sku))
            {
                FlowTrace.Step("Store", alreadyHadBuilder
                    ? "player bought builder already-had"
                    : "player bought builder applied");
                TryNotifyPermanentBuilderGrant(alreadyHadBuilder);
            }
            FlowTrace.Try("Store", $"save restored fulfilled ownership '{pack.Sku}'", () =>
            {
                var svc = GameStateService.Instance;
                if (svc != null) svc.Save();
            });
        }

        private static void RecordOwned(List<string> owned, string sku)
        {
            if (owned == null || string.IsNullOrEmpty(sku)) return;
            if (!owned.Contains(sku)) owned.Add(sku);
        }

        // ---- Cross-asmdef grant seams (ECON-01 / ECON-02) -------------------
        // PackStoreVM lives in DeNelle.Commerce, which cannot reference DeNelle.Cosmetics
        // (Cosmetics -> Wallet already, so a back-reference would be circular) nor DeNelle.Village
        // (one-way asmdef guard). So the canonical, persisted grant services are reached by
        // AppDomain type-name reflection. Every miss Fails LOUDLY: by the time ApplyPackContents
        // runs the payment has ALREADY confirmed, so a silent grant failure = a lost, paid-for entitlement.

        /// <summary>Resolves a singleton service's live Instance by type name across loaded assemblies.</summary>
        private static object ResolveServiceInstance(string typeName, out Type type)
        {
            type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) break;
            }
            if (type == null) return null;
            return type.GetProperty("Instance",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)?.GetValue(null);
        }

        /// <summary>
        /// Cosmetic SKU -> CosmeticOwnershipService so the wardrobe can equip it (ECON-02).
        /// The wardrobe reads CosmeticOwnershipService.Owns(sku) as the SSOT for cosmetic
        /// ownership, and the pack-grant oracle asserts the same. GrantAchievement only registers SKUs
        /// that live in CosmeticCatalog (cosmetics.json) — pack cosmetic SKUs (e.g.
        /// "cosmetic.founders-vow.hero-outfit") are pack rewards, NOT shop-catalog items, so
        /// GrantAchievement no-ops on them and Owns(sku) stays false. So we ALSO call the
        /// catalog-independent MarkCosmeticOwned(string), which writes straight into the same owned-set
        /// CosmeticOwnershipService.Owns reads and persists it — guaranteeing Owns(sku)==true post-grant.
        /// Both writes are idempotent; the GameState.OwnedItemIds record (RecordOwned) is unchanged.
        /// </summary>
        private static void TryGrantCosmeticOwnership(string cosmeticSku, string packSku)
        {
            var svc = ResolveServiceInstance("DeNelle.Cosmetics.CosmeticOwnershipService", out var t);
            if (svc == null)
            {
                FlowTrace.Fail("Pack", $"grant cosmetic '{cosmeticSku}' (pack '{packSku}') FAILED: CosmeticOwnershipService missing - pack cosmetic will be UNEQUIPPABLE.");
                return;
            }

            // Keep the catalog-gated achievement write (harmless no-op for non-catalog pack SKUs; still
            // the right seam for any pack SKU that IS a catalog cosmetic).
            var achM = t.GetMethod("GrantAchievement", new[] { typeof(string) });
            if (achM != null)
            {
                try { achM.Invoke(svc, new object[] { cosmeticSku }); }
                catch (Exception ex) { FlowTrace.Fail("Pack", $"grant cosmetic '{cosmeticSku}' (pack '{packSku}') GrantAchievement THREW: {ex.GetType().Name}: {ex.Message}"); }
            }

            // The load-bearing write: register ownership catalog-independently so Owns(sku)==true.
            var markM = t.GetMethod("MarkCosmeticOwned", new[] { typeof(string) });
            if (markM == null)
            {
                FlowTrace.Fail("Pack", $"grant cosmetic '{cosmeticSku}' (pack '{packSku}') FAILED: MarkCosmeticOwned(string) not found - pack cosmetic will be UNEQUIPPABLE.");
                return;
            }
            try { markM.Invoke(svc, new object[] { cosmeticSku }); }
            catch (Exception ex) { FlowTrace.Fail("Pack", $"grant cosmetic '{cosmeticSku}' (pack '{packSku}') MarkCosmeticOwned THREW: {ex.GetType().Name}: {ex.Message} - pack cosmetic will be UNEQUIPPABLE."); }

            FlowTrace.Step("Pack", $"cosmetic '{cosmeticSku}' (pack '{packSku}') registered owned in CosmeticOwnershipService (Owns==true).");
        }

        /// <summary>
        /// Wood/Iron/Food/Crystals -> EconomyService.GrantSpendablePurchased(int wood,int food,int
        /// iron,int crystals) (ECON-01). Persists + raises ResourcesChanged.
        /// <para>WO-857 Phase F: this MUST resolve the <b>Purchased</b> seam, not the plain
        /// GrantSpendable. The plain one applies the town bank cap, and a pack advertising 5,000 food
        /// then delivered only the starter wallet's 1,920 headroom (caught by PackGrantRegression).
        /// An advertised quantity always arrives in full — see BankGrantKind.PurchasedOrPromised.</para>
        /// <para>The legacy name is kept as a LAST-RESORT fallback: if the purchased seam is ever
        /// renamed away, delivering a clamped amount is still better for the player than delivering
        /// nothing, but it is a FAIL-level event because the pack has under-delivered.</para>
        /// </summary>
        private static void TryGrantResources(int wood, int food, int iron, int crystals, string packSku)
        {
            var svc = ResolveServiceInstance("DeNelle.Village.EconomyService", out var t);
            if (svc == null)
            {
                FlowTrace.Fail("Pack", $"grant resources (W{wood}/F{food}/I{iron}/C{crystals}) for '{packSku}' FAILED: EconomyService missing - paid-for resources LOST.");
                return;
            }
            var sig = new[] { typeof(int), typeof(int), typeof(int), typeof(int) };
            var m = t.GetMethod("GrantSpendablePurchased", sig);
            if (m == null)
            {
                m = t.GetMethod("GrantSpendable", sig);
                if (m != null)
                    FlowTrace.Fail("Pack",
                        $"grant resources for '{packSku}': GrantSpendablePurchased(int,int,int,int) NOT FOUND - falling back to the " +
                        "CAPPED GrantSpendable. A paid pack may now UNDER-DELIVER its advertised amounts against the town bank cap.");
            }
            if (m == null)
            {
                FlowTrace.Fail("Pack", $"grant resources for '{packSku}' FAILED: no GrantSpendablePurchased/GrantSpendable(int,int,int,int) - paid-for resources LOST.");
                return;
            }
            try { m.Invoke(svc, new object[] { wood, food, iron, crystals }); }
            catch (Exception ex) { FlowTrace.Fail("Pack", $"grant resources for '{packSku}' THREW: {ex.GetType().Name}: {ex.Message} - paid-for resources LOST."); }
        }

        /// <summary>
        /// WO-1253 — tell BuildTimerService a permanent-builder entitlement landed so a pending
        /// job can pull into the new crew slot. Wallet cannot reference Village; same reflection
        /// bridge as the resource/cosmetic grants. A miss is logged, never silent: the SKU is
        /// already owned, so concurrency still derives on the next SlotCount read.
        /// </summary>
        private static void TryNotifyPermanentBuilderGrant(bool alreadyHad)
        {
            var svc = ResolveServiceInstance("DeNelle.Village.BuildTimerService", out var t);
            if (svc == null || t == null)
            {
                FlowTrace.Step("Store", alreadyHad
                    ? "player bought builder already-had (timer service not live)"
                    : "player bought builder applied (timer service not live; entitlement recorded)");
                return;
            }
            var m = t.GetMethod("OnPermanentBuilderEntitlement", new[] { typeof(bool) });
            if (m == null)
            {
                FlowTrace.Fail("Store",
                    "OnPermanentBuilderEntitlement(bool) not found - builder SKU is owned but the new crew was not pulled.");
                return;
            }
            try { m.Invoke(svc, new object[] { alreadyHad }); }
            catch (Exception ex)
            {
                FlowTrace.Fail("Store",
                    "OnPermanentBuilderEntitlement THREW: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// WO-1388 - tell BuildTimerService that convenience tokens landed so a 'temporary-builder'
        /// charge starts its window immediately (or is deferred behind a running one). Wallet cannot
        /// reference Village; same reflection bridge as <see cref="TryNotifyPermanentBuilderGrant"/>.
        /// A miss is a Step, not a Fail: the token is durable in GearInventory and the service's own
        /// sweep spends it on load, so nothing paid-for is lost - only the immediacy.
        /// </summary>
        private static void TryNotifyConvenienceTokensGranted(string packSku)
        {
            var svc = ResolveServiceInstance("DeNelle.Village.BuildTimerService", out var t);
            if (svc == null || t == null)
            {
                FlowTrace.Step("Store", $"convenience tokens for '{packSku}' landed; timer service not live - " +
                                        "BuildTimerService's sweep redeems them on load.");
                return;
            }
            var m = t.GetMethod("OnConvenienceTokensGranted", Type.EmptyTypes);
            if (m == null)
            {
                FlowTrace.Fail("Store", "OnConvenienceTokensGranted() not found on BuildTimerService - a " +
                                        "temporary-builder token will wait for the next sweep instead of starting now.");
                return;
            }
            try { m.Invoke(svc, null); }
            catch (Exception ex)
            {
                FlowTrace.Fail("Store", "OnConvenienceTokensGranted THREW: " + ex.GetType().Name + ": " + ex.Message +
                                        " - the token stays in GearInventory for the sweep.");
            }
        }

        /// <summary>Coins (Gold) -> EconomyService.AddCoins(int) (ECON-01). Persists + raises ResourcesChanged.</summary>
        private static void TryGrantCoins(int coins, string packSku)
        {
            var svc = ResolveServiceInstance("DeNelle.Village.EconomyService", out var t);
            if (svc == null)
            {
                FlowTrace.Fail("Pack", $"grant coins +{coins} for '{packSku}' FAILED: EconomyService missing - paid-for Gold LOST.");
                return;
            }
            var m = t.GetMethod("AddCoins", new[] { typeof(int) });
            if (m == null)
            {
                FlowTrace.Fail("Pack", $"grant coins +{coins} for '{packSku}' FAILED: AddCoins(int) not found - paid-for Gold LOST.");
                return;
            }
            try { m.Invoke(svc, new object[] { coins }); }
            catch (Exception ex) { FlowTrace.Fail("Pack", $"grant coins +{coins} for '{packSku}' THREW: {ex.GetType().Name}: {ex.Message} - paid-for Gold LOST."); }
        }

        // ── Store close resolve (interactor reflection + hero re-enable fallback) ──

        /// <summary>
        /// Closes the store the way MarketplaceInteractor does. Returns true when the interactor
        /// handled it (its private CloseStore re-enables HeroLocomotion + clears _storeOpen); returns
        /// false after running the ReEnableDisabledHeroLocomotion fallback, signalling the View to hide
        /// its own GameObject. Preserves the exact soft-lock-guard behaviour of PackStore.CloseStore.
        /// </summary>
        public bool CloseViaInteractor()
        {
            if (TryCloseViaInteractor()) return true;

            // Fallback: re-enable a disabled hero locomotion; the View hides itself.
            ReEnableDisabledHeroLocomotion();
            return false;
        }

        private bool TryCloseViaInteractor()
        {
            // Find MarketplaceInteractor by type name across loaded assemblies
            // (we can't reference the Village asmdef directly).
            Type interactorType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                interactorType = asm.GetType("DeNelle.Village.MarketplaceInteractor");
                if (interactorType != null) break;
            }
            if (interactorType == null) return false;

            var interactor = FindFirstObjectOfType(interactorType, true);
            if (interactor == null) return false;

            var closeMethod = interactorType.GetMethod(
                "CloseStore",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (closeMethod == null) return false;

            closeMethod.Invoke(interactor, null);
            return true;
        }

        private static UnityEngine.Object FindFirstObjectOfType(Type t, bool includeInactive)
        {
            var found = Resources.FindObjectsOfTypeAll(t);
            if (found == null) return null;
            foreach (var obj in found)
            {
                // Skip assets / prefabs not in a live scene.
                if (obj is Component comp && comp.gameObject.scene.IsValid())
                {
                    if (includeInactive || comp.gameObject.activeInHierarchy)
                        return obj;
                }
            }
            return null;
        }

        private void ReEnableDisabledHeroLocomotion()
        {
            Type locoType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                locoType = asm.GetType("DeNelle.Village.HeroLocomotion");
                if (locoType != null) break;
            }
            if (locoType == null) return;

            var found = Resources.FindObjectsOfTypeAll(locoType);
            if (found == null) return;
            foreach (var obj in found)
            {
                if (obj is Behaviour behaviour && behaviour.gameObject.scene.IsValid())
                    behaviour.enabled = true;
            }
        }
    }
}
