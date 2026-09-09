// =============================================================================
// HudKitCommandBridge — Village-side handler registration for the HUD kit's
// Core command sink (HUD_OBSIDIAN_ARCHITECTURE_2026-07-03 A4 — P23 HUDKIT).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hud
//
// The kit (DeNelle.HUD) fires DeNelle.Core.HUD.HudCommands; this bridge is the
// Village side that plugs the real gameplay handlers in, RE-REGISTERED ON EVERY
// SCENE LOAD so a handler can never go stale on a destroyed body (the exact
// failure mode that killed the talk button's reflection push — see
// PostureSignals header). Handlers resolve their scene objects LAZILY AT FIRE
// TIME, so registration order vs. hero spawn order cannot break them.
//
// Registered here:
//   attack      -> the class's ranged basic (DERIVED via HeroAbilities.
//                  TryGetRangedPrimary, never a class-name table) when it can be
//                  paid for, and ALWAYS the free melee sweep otherwise — a
//                  refused cast falls THROUGH instead of returning (WO-1429).
//   cycleSelect -> HeroTargetIndicator.EngageLock on the Enemy whose instance
//                  id matches the TargetRecord.Id (mirrors the retired
//                  BattleHud9Zone.SelectCycleRow WO-512 routing).
//   flee        -> registered by BattleArenaHud (battle-scoped, not here).
//   potion      -> ConsumableUseService.TryUse(minor-heal-potion, inFight).
//   manaPotion  -> ConsumableUseService.TryUse(cons_mana_draught, inFight).
//   assignable  -> AssignableSkillBar slot -> HeroAbilities.TryCastExtra.
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.HUD;
using DeNelle.Village.Items;

namespace DeNelle.Village.Hud
{
    /// <summary>Registers the Village handlers behind the HUD kit's commands (see header).</summary>
    public static class HudKitCommandBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            RegisterAll();
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            RegisterAll();
            if (HubScenes.IsHub(s.name))
                HudPostureReset.OnHubLoaded(s.name);
        }

        private static void RegisterAll()
        {
            // Lazy-resolving closures: the live component is found at FIRE time
            // (follows body swaps / respawns; never a cached destroyed instance).
            HudCommands.RegisterAttack(() =>
            {
                var health = Object.FindAnyObjectByType<HeroHealth>();
                if (health != null && health.IsBlocking)
                {
                    FlowTrace.Step("HudKit", "attack gated while Block is held");
                    return;
                }
                // ── ⭐ WO-1429 — YOU PRESSED ATTACK, SO YOU ATTACK ────────────────────────────
                //
                // THE DEFECT THIS REPLACES, from a CAPTURE and not a theory
                // (logs/device/freeze-20260904-095249.log:544639, a real Seeker session):
                //     [Flow:HudKit] command 'attack' fired
                //     [Flow:HeroMana] cast REFUSED slot=Q 'Fireball': cd=0.47s Mana 21.08/24.00 cost=3.00
                //     [Flow:HudKit] primary command -> class Q for mage gated
                // ...and NOTHING follows. The tap produced NO VERB. The code here used to be a
                // hardcoded per-class table — `heroClass == "mage" || heroClass == "ranger"` ->
                // TryCast(Q) -> `return`, BEFORE the melee swing could ever be reached — so every
                // refusal was a dead button. Note the numbers: that refusal is a COOLDOWN refusal at
                // near-full mana. HeroAbilities.TryCast:813 refuses on `cd > 0 || _mana < cost` and
                // BOTH exit the same `return false`, so this was never an out-of-mana edge case: the
                // button died in every cooldown gap, several times a minute, all game.
                //
                // THE RULE, and it needs no class name: **a refused primary — for ANY reason,
                // cooldown or cost — falls THROUGH to the free melee sweep.** No thresholds, no mana
                // check, no hysteresis (a 0.47s cooldown gap must not lock the hero to the staff
                // until mana climbs to 50% — that would be strictly worse than the defect).
                //
                // WHY THE TABLE IS DELETED RATHER THAN EXTENDED: HeroAbilities.TryGetRangedPrimary
                // is "the SINGLE decision seam" (its own doc comment) and is DERIVED from the
                // authored def's effect shape + RangedPrimaryReachFactor, not from a class id.
                // Measured against Assets/StreamingAssets/Data/Canonical/abilities.json this session:
                // mage.q Fireball (effect=strike, range=14) -> true, ranger.q Quick Shot
                // (effect=strike, range=15) -> true, knight.q Sword Heroic (effect=dash) -> false —
                // the EXACT set the string table hardcoded, now derived, and it generalises to any
                // future class for free. PlayerAttackController and HeroTargetIndicator gate on the
                // same call, so input and targeting still cannot disagree.
                //
                // THE SWEEP IS FREE BY CONSTRUCTION (owner ruling, WO-1429 §7: "No swing Staff
                // should have no cost only casting magic should"): PlayerAttackController spends no
                // resource anywhere — its only pool contact is a GRANT, the ranger's on-hit
                // RestoreMana at PlayerAttackController.cs:816-820, and that one is gated OFF for a
                // class with a ranged basic. Free is the only cost that keeps "the hero always has a
                // verb" true at every instant.
                //
                // MOBILE ONLY: PlayerAttackController.Update:328 already melees unconditionally for
                // every class on keyboard/gamepad/mouse. That path is untouched — which is why the
                // owner only ever felt this on the Seeker.
                var atk = Object.FindAnyObjectByType<PlayerAttackController>();
                var abilities = Object.FindAnyObjectByType<HeroAbilities>();

                AbilityDef qDef = null;
                bool hasRangedPrimary = abilities != null &&
                                        abilities.TryGetRangedPrimary(atk != null ? atk.AttackRange : 0f, out qDef);

                if (hasRangedPrimary)
                {
                    if (abilities.TryCast(AbilitySlot.Q))
                    {
                        FlowTrace.Step("HudKit", "primary command -> class Q '" +
                                                   (qDef != null ? qDef.Id : "(none)") + "' FIRED");
                        return;
                    }
                    // §12: name the fall-through explicitly. HeroAbilities already logged WHICH gate
                    // refused (cd/cost/wind-up) one line above this in any capture, so the pair reads
                    // "refused, and here is the verb the player got instead" in a single look.
                    FlowTrace.Step("HudKit", "primary command -> class Q '" +
                                               (qDef != null ? qDef.Id : "(none)") +
                                               "' REFUSED - falling through to the free melee sweep (WO-1429)");
                }

                if (atk == null) { FlowTrace.Warn("HudKit", "attack fired but no PlayerAttackController in scene"); return; }
                bool swung = atk.TriggerBasicAttack();
                FlowTrace.Step("HudKit", "primary command -> melee sweep " + (swung ? "SWUNG" : "gated") +
                                           (hasRangedPrimary ? " (fallback after a refused class Q)"
                                                             : " (class has no ranged primary)"));
            });

            HudCommands.RegisterBlock(held =>
            {
                var abilities = Object.FindAnyObjectByType<HeroAbilities>();
                if (abilities != null && string.Equals(abilities.HeroClass, "mage",
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    // A Mage has no physical shield: pointer-down casts the authored
                    // Arcane Shell; pointer-up is only meaningful to held Knight Block.
                    if (held)
                    {
                        bool fired = abilities.TryCast(AbilitySlot.W);
                        FlowTrace.Step("HudKit", "mage protection -> Arcane Shell " +
                                                   (fired ? "FIRED" : "gated"));
                    }
                    return;
                }
                var health = Object.FindAnyObjectByType<HeroHealth>();
                if (health == null)
                {
                    FlowTrace.Warn("HudKit", "block fired but no HeroHealth in scene");
                    return;
                }
                health.SetBlocking(held);
            });

            HudCommands.RegisterCycleSelect(id =>
            {
                if (string.IsNullOrEmpty(id)) return;
                var indicator = Object.FindAnyObjectByType<HeroTargetIndicator>();
                if (indicator == null) { FlowTrace.Warn("HudKit", "cycleSelect fired but no HeroTargetIndicator"); return; }
                int wanted;
                if (!int.TryParse(id, out wanted)) return;
                var enemies = Object.FindObjectsByType<Enemy>();
                for (int i = 0; i < enemies.Length; i++)
                {
                    var en = enemies[i];
                    if (en == null || en.IsDead || en.GetInstanceID() != wanted) continue;
                    var dmg = en.GetComponent<IDamageable>();
                    if (dmg == null) dmg = en.GetComponentInParent<IDamageable>();
                    if (dmg != null)
                    {
                        indicator.EngageLock(dmg);
                        FlowTrace.Step("HudKit", "cycleSelect -> lock engaged on " + en.name);
                    }
                    return;
                }
                FlowTrace.Warn("HudKit", "cycleSelect: enemy id " + id + " not found (died/despawned)");
            });

            HudCommands.RegisterPotion(() =>
            {
                bool ok = ConsumableUseService.TryUse(HudCommands.HpPotionId, inFight: true);
                FlowTrace.Step("HudKit", "potion command -> TryUse(" + HudCommands.HpPotionId + ") " + (ok ? "OK" : "no-op"));
            });

            HudCommands.RegisterManaPotion(() =>
            {
                bool ok = ConsumableUseService.TryUse(HudCommands.ManaPotionId, inFight: true);
                FlowTrace.Step("HudKit", "manaPotion command -> TryUse(" + HudCommands.ManaPotionId + ") " + (ok ? "OK" : "no-op"));
            });

            HudCommands.RegisterAssignableCast(slot =>
            {
                var bar = AssignableSkillBarAccess.Current;
                var abilities = Object.FindAnyObjectByType<HeroAbilities>();
                if (bar == null || abilities == null)
                {
                    FlowTrace.Warn("HudKit", "assignableCast slot=" + slot + " but no bar/abilities");
                    return;
                }
                string id = bar.AbilityIdForSlot(slot);
                if (string.IsNullOrEmpty(id)) return;
                bool fired = abilities.TryCastExtra(id);
                FlowTrace.Step("HudKit", "assignableCast slot=" + slot + " id=" + id + " -> " + (fired ? "FIRED" : "gated"));
            });

            FlowTrace.Step("HudKit", "command bridge registered (attack, block, cycleSelect, potions, assignable) for scene '" +
                           SceneManager.GetActiveScene().name + "'");
        }
    }
}
