// =============================================================================
// ConsumableUseService - the "use a potion / eat food / pitch a tent kit" stub.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Items
//
// ISOLATION: composes existing PUBLIC APIs read-only/additively:
//   - DeNelle.Village.Crafting.VillageInventoryInstance (consume the item, public)
//   - DeNelle.Village.HeroHealth.Heal(amount)            (apply heal, public)
// It edits NO existing file. It is a static helper any caller (a future hotbar
// button, a debug key, the crafting panel) can call to spend a crafted consumable.
//
// v1 SCOPE (deliberately a stub - content + buff math is the deferred work):
//   * Potion  + Heal  -> VillageInventory.TryConsume(id, 1); HeroHealth.Heal(mag)
//   * Food    + Heal  -> same (in-fight heal); Buff effect is logged TODO
//   * Tent    + Rest  -> heal to full BETWEEN fights only (usableInFight=false);
//                        v1 applies a Heal(mag) and logs that the rest layer is
//                        deferred (no between-fight state machine wired yet).
//   * Mana / Buff / duration-over-time effects -> recognised + logged TODO; the
//     mana pool + timed-buff system are deferred (no hero mana field exists yet).
//
// GRACEFUL: returns false and no-ops if the feature is disabled, the consumable
// is unknown, the larder is short, or no hero is found. ASCII strings only.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Village.Crafting;
using DeNelle.Core.Diagnostics; // WO3: FlowTrace self-reporting for the percent/over-time potions
using DeNelle.Core.UI;          // 2026-08-05: ElarionUiKit.ShowToast — the ONE transient toast
                                // (BankOverflowToastPresenter precedent) for the empty-larder tell

namespace DeNelle.Village.Items
{
    public static class ConsumableUseService
    {
        // Owner directive (2026-07-24): ENFORCED, visually-told use-cooldown. Mirrors the
        // Q/W/E/R ability model - state lives HERE in the service (never the View), the Try
        // gate refuses while cooling, and HudModelProducers reads these to drive the belt
        // tile's radial sweep. RUNTIME-ONLY: absolute Time.time end-stamps keyed by
        // consumableId; never persisted, no save-schema bump (matches ability cooldowns).
        private static readonly Dictionary<string, float> _nextReadyAt = new Dictionary<string, float>();

        /// <summary>Seconds of use-cooldown left on <paramref name="id"/> (0 = ready). Read by the HUD.</summary>
        public static float CooldownRemaining(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            if (!_nextReadyAt.TryGetValue(id, out var readyAt)) return 0f;
            float left = readyAt - Time.time;
            return left > 0f ? left : 0f;
        }

        /// <summary>The full authored use-cooldown of <paramref name="id"/> (from consumables.json). 0 = spammable.</summary>
        public static float CooldownTotal(string id)
        {
            var def = ConsumableCatalog.Find(id);
            return def != null ? def.UseCooldown : 0f;
        }

        /// <summary>
        /// Attempt to use one of <paramref name="consumableId"/> from the village
        /// larder. <paramref name="inFight"/> gates fight-only vs rest-only items.
        /// Returns true if an item was consumed and its effect applied.
        /// </summary>
        public static bool TryUse(string consumableId, bool inFight)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName)
            { ElarionUiKit.ShowToast(LocalText.Get("ownedTown.practiceItems")); return false; }
            if (!ItemDropSystem.Enabled) return false;      // dark lane: inert when off
            if (string.IsNullOrEmpty(consumableId)) return false;

            var def = ConsumableCatalog.Find(consumableId);
            if (def == null)
            {
                Debug.LogWarning("[ConsumableUse] unknown consumable: " + consumableId);
                return false;
            }

            // Gate by context: tent kits are rest-only; some food may be fight-only.
            if (inFight && !def.UsableInFight)
            {
                Debug.Log("[ConsumableUse] " + consumableId + " cannot be used mid-fight (rest-only).");
                return false;
            }

            var inv = VillageInventory.Instance;
            if (inv == null) return false;
            if (inv.Get(consumableId) <= 0)
            {
                Debug.Log("[ConsumableUse] none in larder: " + consumableId);
                // Owner ruling 2026-08-05: an empty larder must SAY SO. Until now this branch only
                // wrote a Debug.Log, so tapping the potion belt at zero was completely silent to the
                // player. Route it through the ONE canonical transient toast (ElarionUiKit.ShowToast,
                // the BankOverflowToastPresenter precedent) — no new toast layer. The words carry the
                // meaning; never a colour-only cue (owner is red/green colourblind). ASCII only.
                // Card grown past the 480x76 default: the default holds ~2 lines / ~80 chars and
                // ToastCard's label overflows VERTICALLY (a third line draws outside the plate).
                ElarionUiKit.ShowToast(EmptyLarderLine(def), ElarionUiKit.ToastTone.Danger, 3.8f,
                                       sortingOrder: 720, cardWidth: 640f, cardHeight: 112f);
                return false;
            }

            // Cooldown gate: refuse while this consumable is still cooling (mirrors
            // HeroAbilities.TryCast's ExtraCooldownRemaining>0 refusal). Only gates authored
            // consumables (useCooldown > 0); spammable ones fall straight through.
            if (def.UseCooldown > 0f && CooldownRemaining(consumableId) > 0f)
            {
                Debug.Log("[ConsumableUse] " + consumableId + " still cooling ("
                    + CooldownRemaining(consumableId).ToString("0.0") + "s left).");
                return false;
            }

            // Consume FIRST (only spend on a real apply path).
            if (!inv.TryConsume(consumableId, 1)) return false;

            ApplyEffect(def);

            // Start the cooldown ONLY on a successful use (runtime-only; not persisted).
            if (def.UseCooldown > 0f)
                _nextReadyAt[consumableId] = Time.time + def.UseCooldown;

            return true;
        }

        // ── Empty-larder copy (owner ruling 2026-08-05) ──────────────────────────
        // The line must name the potion TYPE and send the player somewhere that ACTUALLY
        // EXISTS. A message that routes to a place the player cannot reach is worse than
        // no message, so both destinations below were re-verified at source:
        //
        //   BUY -> REAL. vendors.json id "market" / displayName "Market Stalls" is the only
        //          vendor whose `categories` include "consumable", and both potions carry a
        //          `price` in consumables.json (minor-heal-potion 8, cons_mana_draught 12).
        //          Its storefront is BAKED into the hub (Marketplace_Monetization +
        //          NPC_Marketplace_Interactable in Main_Castle_Overworld.unity) and the
        //          Lever-1 ruling keeps baked stores PRE-STANDING on a fresh save, so the
        //          player can always walk to it. This is the route we can promise.
        //
        //   CRAFT -> CONDITIONAL, and it is NOT a standing destination today. Recipes exist
        //          (consumable-recipes.json: craft-minor-heal-potion, craft-survival-mana-potion)
        //          but PanelId.ConsumableCrafting has exactly TWO openers: a Building of
        //          BuildingType.ApothecaryWorkbench, and the "apothecary" NPC dialogue, whose
        //          vendor anchor waits on a live Building with that same id. On a strategic-
        //          placement save CraftingStationInjector.Inject stands down unconditionally
        //          (StrategicPlacementMigration.StanddownActiveForStation returns StanddownActive),
        //          structures-catalog.json has NO "apothecary" row so the palette can never
        //          build one, and no apothecary is baked in the hub scene. Net: usually there
        //          is no brewer to send anyone to.
        //          So the craft clause is emitted ONLY when an alchemy bench is genuinely
        //          standing in the scene — checked, never assumed. The alternative (hardcoding
        //          "Craft one at the Apothecary") is the exact defect this rewrite corrects.
        private const string CraftPlace = "Apothecary";
        private const string BuyPlace = "Market Stalls";

        /// <summary>
        /// True when a consumable-crafting bench is ACTUALLY standing in the loaded scene —
        /// i.e. a live Building whose Type is ApothecaryWorkbench, the one thing that opens
        /// PanelId.ConsumableCrafting. Scans the Building collection rather than trusting a
        /// catalog/marker, because the station is injected at runtime and stands down on most
        /// saves. Cheap enough here: this runs once per EMPTY-slot tap, never in a loop.
        ///
        /// ⭐ MADE PUBLIC BY WO-1235. The owner's ruling #3 is a HARD PRECONDITION on the mana
        /// recipe scroll - verbatim "Never teach a verb the player cannot immediately perform" -
        /// so ManaRecipeScrollService has to ask the same question this method already answers.
        /// It calls THIS one rather than growing a second scan: two copies of "is there a brewer"
        /// is how the empty-larder line and the scroll gate would come to disagree, and the whole
        /// point of the precondition is that they must not.
        /// ⚠ The caller there scans at 1 Hz, not per tap - keep this allocation-light.
        /// </summary>
        public static bool AlchemyBenchIsStanding()
        {
            var buildings = Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
            if (buildings == null) return false;
            for (int i = 0; i < buildings.Length; i++)
            {
                var b = buildings[i];
                if (b != null && b.IsAlive && b.Type == BuildingType.ApothecaryWorkbench) return true;
            }
            return false;
        }

        /// <summary>ASCII, colour-free "you have none" line naming the item and every remedy that
        /// is REACHABLE right now. The words carry the whole meaning — no colour-only cue (the
        /// owner is red/green colourblind), and no destination is named unless it exists.</summary>
        private static string EmptyLarderLine(ConsumableDef def)
        {
            string name = def != null && !string.IsNullOrEmpty(def.DisplayName)
                ? def.DisplayName
                : (def != null && !string.IsNullOrEmpty(def.Id) ? def.Id : "that item");
            // Cheap ASCII plural: "Minor Healing Draught" -> "Draughts"; anything already
            // ending in 's' ("Traveler's Rations") is left alone.
            if (!name.EndsWith("s") && !name.EndsWith("S")) name += "s";

            if (AlchemyBenchIsStanding())
                return "Out of " + name + ". Brew more at the " + CraftPlace
                     + ", or buy them at the " + BuyPlace + ".";

            // No brewer standing: name the ONE real route, and say plainly why crafting is not
            // on the table instead of pointing at a building that is not in the world.
            return "Out of " + name + ". Buy more at the " + BuyPlace
                 + " in town - there is no " + CraftPlace + " standing to brew them yet.";
        }

        private static void ApplyEffect(ConsumableDef def)
        {
            switch (def.Effect)
            {
                case ConsumableEffect.Heal:
                    ApplyHeal(def);
                    break;

                case ConsumableEffect.Rest:
                    // Tent kit: v1 applies the heal magnitude; the proper "rest
                    // between fights -> heal party to full + clear debuffs" layer
                    // is DEFERRED (no between-fight state machine yet).
                    ApplyHeal(def);
                    Debug.Log("[ConsumableUse] tent rest applied (between-fight rest layer DEFERRED).");
                    break;

                case ConsumableEffect.Mana:
                    ApplyMana(def);
                    break;

                case ConsumableEffect.Buff:
                    // DEFERRED: timed-buff system not wired in this lane.
                    Debug.Log("[ConsumableUse] timed buff DEFERRED (no buff system wired): " + def.Id);
                    break;

                default:
                    Debug.Log("[ConsumableUse] no effect handler for: " + def.Id);
                    break;
            }
        }

        /// <summary>
        /// Heal the active hero. WO3: when <c>magnitudePct &gt; 0</c> the heal is a PERCENT of
        /// the hero's effective max HP (gear+talent), so "30%" scales with the build; otherwise
        /// the flat <c>magnitude</c> path is preserved. Finds the first HeroHealth in the scene.
        /// </summary>
        private static void ApplyHeal(ConsumableDef def)
        {
            var hero = Object.FindAnyObjectByType<HeroHealth>();
            if (hero == null)
            {
                Debug.Log("[ConsumableUse] no hero found to heal.");
                return;
            }

            float amount;
            if (def.MagnitudePct > 0f)
            {
                amount = def.MagnitudePct / 100f * hero.MaxHp;
                FlowTrace.Step("ConsumableUse", $"heal {def.Id}: {def.MagnitudePct}% of maxHp ({hero.MaxHp:0.0}) = {amount:0.0}.");
            }
            else
            {
                amount = def.Magnitude;
                FlowTrace.Step("ConsumableUse", $"heal {def.Id}: flat {amount:0.0}.");
            }

            if (amount <= 0f) return;
            hero?.Heal(amount);

            // Owner 2026-07-24: drinking a heal potion now plays the SAME full-prefab Hovl heal read
            // the Knight's Warden's Grace uses, so a heal is visibly a heal either way. Heal_Cast is a
            // self-lifetiming radiant burst (a oneshot prefab — no leak from this static service), fired
            // at the hero's chest through the ONE VFXManager pool (PlayKey). A missing key throttled-
            // no-ops (no throw), so this is ship-safe even before the catalog row exists.
            if (hero != null)
                VFXManager.PlayKey("Heal_Cast", hero.transform.position + Vector3.up * 1.2f,
                    Quaternion.identity, hero.transform);
        }

        /// <summary>
        /// WO3 (Mana Draught): restore mana GRADUALLY via HeroAbilities — <c>magnitudePct</c>
        /// percent of max mana spread over <c>duration</c> seconds (owner spec: +3%/s till 30%).
        /// Data-driven; the code only interprets. No-ops null-safely if no mana pool is present.
        /// </summary>
        private static void ApplyMana(ConsumableDef def)
        {
            if (def.MagnitudePct <= 0f)
            {
                Debug.Log("[ConsumableUse] mana potion has no magnitudePct; nothing to restore: " + def.Id);
                return;
            }

            var hero = Object.FindAnyObjectByType<HeroAbilities>();
            if (hero == null)
            {
                Debug.Log("[ConsumableUse] no hero mana pool found (HeroAbilities) for: " + def.Id);
                return;
            }

            float seconds = def.Duration > 0f ? def.Duration : 10f;
            hero?.RestoreManaOverTime(def.MagnitudePct, seconds);
            FlowTrace.Step("ConsumableUse", $"mana {def.Id}: +{def.MagnitudePct}% over {seconds}s (over-time drip).");
        }
    }
}
