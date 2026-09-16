// SCAFFOLD — CLI must build-verify under Unity Test Framework
// =============================================================================
// DevGrantSpendableTests (EditMode) — dev-grant wallet-convergence regression.
// -----------------------------------------------------------------------------
// THE BUG THIS GUARDS: the DEV "Load up (full base)" grant could leave the owner
// with no SPENDABLE Wood/Iron because those resources live in TWO stores that do
// not sync —
//   * EconomyService's in-session pool (Wood/Iron) — read by the gear shop + the HUD
//     resource bar (HeartHudBridge -> EconomyService.Snapshot).
//   * GameState.Wood / GameState.Iron — read + spent by the building-upgrade flow
//     (ResourceBuildingProgression.ResourceLedger).
// A plain EconomyService.Grant filled only the in-session pool, so dev-granted
// Wood/Iron was unspendable in the upgrade flow; writing GameState directly filled
// only the upgrade wallet, so the shop + HUD saw nothing.
//
// EconomyService.GrantSpendableUncapped is the explicit dev-grant seam both dev panels use
// (the ordinary GrantSpendable path is correctly town-bank-capped). The dev seam MUST
// land Wood/Iron in BOTH wallets. These tests assert exactly that — they fail if
// the grant stops reaching either spendable store.
//
// EditMode never calls Awake(), so neither singleton is auto-wired. We spawn an
// EconomyService component directly (serialized defaults Wood 200 / Iron 80) and a
// GameStateService with an injected GameState + _loadOnAwake disabled, the same
// pattern Core's TestSupport uses. PlayerPrefs writes from Save() are cleared in
// teardown.
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Core.State;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class DevGrantSpendableTests
    {
        private EconomyService _econ;
        private GameStateService _service;
        private GameState _state;

        [SetUp]
        public void SetUp()
        {
            ResetEconomySingleton();
            ResetGameStateSingleton();

            // GameStateService with an injected fresh state + auto-load disabled.
            var gsGo = new GameObject("GameStateService (test)");
            _service = gsGo.AddComponent<GameStateService>();
            _state = ScriptableObject.CreateInstance<GameState>();
            SetPrivateField(_service, "_state", _state);
            SetPrivateField(_service, "_loadOnAwake", false);
            // EditMode skips Awake, so set the public singleton handle by reflection
            // so EconomyService.GrantSpendableUncapped resolves GameStateService.Instance.
            SetGameStateInstance(_service);

            // EconomyService component (serialized defaults: Wood 200, Iron 80).
            var ecoGo = new GameObject("EconomyService (test)");
            _econ = ecoGo.AddComponent<EconomyService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_econ != null) Object.DestroyImmediate(_econ.gameObject);
            if (_service != null) Object.DestroyImmediate(_service.gameObject);
            if (_state != null) Object.DestroyImmediate(_state);
            _econ = null; _service = null; _state = null;
            ResetEconomySingleton();
            ResetGameStateSingleton();
            PlayerPrefs.DeleteKey(SaveSchema.PlayerPrefsKey);
            PlayerPrefs.Save();
        }

        // ── The contract: dev grant lands Wood/Iron in BOTH spendable wallets ──

        [Test]
        public void grant_spendable_increases_economy_pool_the_shop_spends_from()
        {
            int wood0 = _econ.Wood;
            int iron0 = _econ.Iron;

            _econ.GrantSpendableUncapped(wood: 50000, iron: 50000);

            Assert.That(_econ.Wood, Is.EqualTo(wood0 + 50000),
                "in-session Wood (gear shop + HUD bar wallet) must grow by the grant");
            Assert.That(_econ.Iron, Is.EqualTo(iron0 + 50000),
                "in-session Iron must grow by the grant");
        }

        [Test]
        public void grant_spendable_increases_gamestate_wallet_the_upgrade_flow_spends_from()
        {
            int wood0 = _state.Wood;
            int iron0 = _state.Iron;

            _econ.GrantSpendableUncapped(wood: 50000, iron: 50000);

            Assert.That(_state.Wood, Is.EqualTo(wood0 + 50000),
                "GameState.Wood (the building-upgrade / ResourceLedger wallet) must grow by the grant");
            Assert.That(_state.Iron, Is.EqualTo(iron0 + 50000),
                "GameState.Iron must grow by the grant");
        }

        [Test]
        public void granted_wood_iron_is_affordable_in_the_economy_after_grant()
        {
            // Before: a 50k Wood/Iron cost is unaffordable from the 200/80 starter.
            var bigCost = new ResourceCost(wood: 50000, iron: 50000);
            Assert.That(_econ.CanAfford(bigCost), Is.False, "precondition: starter can't afford 50k/50k");

            _econ.GrantSpendableUncapped(wood: 50000, iron: 50000);

            Assert.That(_econ.CanAfford(bigCost), Is.True,
                "after the dev grant the economy must report the 50k/50k cost affordable (shop can spend)");
        }

        [Test]
        public void crystals_and_food_route_through_the_single_gamestate_wallet()
        {
            int food0 = _state.Resources.Stone;
            int crystals0 = _state.Resources.Crystals;

            _econ.GrantSpendableUncapped(stone: 25000, crystals: 25000);

            Assert.That(_state.Resources.Stone, Is.EqualTo(food0 + 25000),
                "Food is GameState-backed; the dev grant must land it there");
            Assert.That(_state.Resources.Crystals, Is.EqualTo(crystals0 + 25000),
                "Crystals are GameState-backed; the dev grant must land them there");
            // EconomyService reads Food/Crystals THROUGH GameState, so its view agrees.
            Assert.That(_econ.Stone, Is.EqualTo(food0 + 25000), "EconomyService.Food mirrors GameState");
            Assert.That(_econ.Crystals, Is.EqualTo(crystals0 + 25000), "EconomyService.Crystals mirrors GameState");
        }

        // ── WO-842 — the captured F8 asymmetry, as a test ─────────────────────
        // [Flow:Upgrade] "TryUpgrade FALSE (needed W800/F500/C0, have W985646/F988524/...)":
        // riches granted GAMESTATE-SIDE (dev tool / save load writing state.Wood directly)
        // were invisible to EconomyService's old in-session pool, so CanAfford/TrySpend
        // refused an 800-wood cost against a 985k wallet. Post-unification the GameState
        // wallet IS the economy wallet: afford says yes, the spend lands, views agree.
        [Test]
        public void gamestate_side_riches_are_spendable_through_the_economy_wo842()
        {
            _state.Wood = 985646;                       // GameState-side grant ONLY (no EconomyService call)
            var bal = _state.Resources; bal.Stone = 988524; _state.Resources = bal;

            var cost = new ResourceCost(wood: 800, stone: 500);
            Assert.That(_econ.Wood, Is.EqualTo(985646),
                "EconomyService.Wood must read through GameState.Wood (single wallet)");
            Assert.That(_econ.CanAfford(cost), Is.True,
                "the captured refusal: CanAfford(W800/F500) must be TRUE against GameState W985646/F988524");
            Assert.That(_econ.TrySpend(cost), Is.True,
                "TrySpend must succeed against the same wallet CanAfford read");
            Assert.That(_state.Wood, Is.EqualTo(984846),
                "the debit must land on GameState.Wood (the ledger the upgrade flow spends)");
            Assert.That(_econ.Wood, Is.EqualTo(_state.Wood),
                "post-spend the economy view and GameState must agree (one store)");
            Assert.That(_state.Resources.Stone, Is.EqualTo(988024),
                "the food slot debits the same single GameState wallet");
        }

        [Test]
        public void full_base_grant_lands_all_four_in_their_spendable_stores()
        {
            int wood0 = _econ.Wood, iron0 = _econ.Iron;
            int gsWood0 = _state.Wood, gsIron0 = _state.Iron;
            int food0 = _state.Resources.Stone, crystals0 = _state.Resources.Crystals;

            // The exact full-base load both dev buttons fire.
            _econ.GrantSpendableUncapped(wood: 50000, stone: 25000, iron: 50000, crystals: 25000);

            // Shop / HUD wallet.
            Assert.That(_econ.Wood, Is.EqualTo(wood0 + 50000));
            Assert.That(_econ.Iron, Is.EqualTo(iron0 + 50000));
            // Upgrade-flow wallet.
            Assert.That(_state.Wood, Is.EqualTo(gsWood0 + 50000));
            Assert.That(_state.Iron, Is.EqualTo(gsIron0 + 50000));
            // Shared GameState harvestables.
            Assert.That(_state.Resources.Stone, Is.EqualTo(food0 + 25000));
            Assert.That(_state.Resources.Crystals, Is.EqualTo(crystals0 + 25000));
        }

        // ── Reflection helpers (mirror Core TestSupport) ──────────────────────

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"expected private field '{name}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        private static void SetGameStateInstance(GameStateService service)
        {
            // GameStateService.Instance is a public static auto-property backed by a
            // private static field; in EditMode Awake never runs to assign it.
            var prop = typeof(GameStateService).GetProperty(
                "Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.CanWrite) { prop.SetValue(null, service); return; }
            // Auto-property backing field fallback.
            var backing = typeof(GameStateService).GetField(
                "<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            if (backing != null) { backing.SetValue(null, service); return; }
            // Explicit field fallback (matches Core TestSupport's "_instance").
            var f = typeof(GameStateService).GetField(
                "_instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(f, Is.Not.Null, "could not locate GameStateService singleton handle to set");
            f.SetValue(null, service);
        }

        private static void ResetGameStateSingleton()
        {
            var prop = typeof(GameStateService).GetProperty(
                "Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.CanWrite) { prop.SetValue(null, null); return; }
            var backing = typeof(GameStateService).GetField(
                "<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            if (backing != null) { backing.SetValue(null, null); return; }
            var f = typeof(GameStateService).GetField(
                "_instance", BindingFlags.Static | BindingFlags.NonPublic);
            f?.SetValue(null, null);
        }

        private static void ResetEconomySingleton()
        {
            var prop = typeof(EconomyService).GetProperty(
                "Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.CanWrite) { prop.SetValue(null, null); return; }
            var backing = typeof(EconomyService).GetField(
                "<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            backing?.SetValue(null, null);
        }
    }
}
