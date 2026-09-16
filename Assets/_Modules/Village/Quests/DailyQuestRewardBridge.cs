// =============================================================================
// DailyQuestRewardBridge — WO-564. The SINGLE Village-side dispenser that pays
// out a completed daily quest's slot reward.
// -----------------------------------------------------------------------------
// DeNelle.Core (where DailyQuestService lives) cannot reference the Village
// wallet / currency services, so the grant happens here, on the other side of the
// DailyQuestService.QuestCompleted event (the same cross-assembly pattern as
// QuestRewardBridge / DailyQuestGateBridge).
//
// WHY A DEDICATED BRIDGE (WO-564): reward dispense was previously bolted onto
// DailyQuestTowerBridge, which previously omitted several live reward seams — it
// silently DROPPED the schema's rewardFood (exploration slot = 20 food) and
// rewardRandomItem (wildcard slot) — and (b) mixed an unrelated tower-placement
// hook into a "reward" responsibility. This bridge owns the WHOLE reward schema
// in ONE place; DailyQuestTowerBridge now does only its tower-placement tick.
// Exactly ONE listener subscribes to QuestCompleted, so the ClaimedAtUnix latch
// is never raced by two dispensers.
//
// DATA-DRIVEN: every amount comes from DailyQuestCatalog.RewardFor(slot) (the
// daily-quests.json `slots` block) — nothing is hardcoded per quest. The reward
// schema (DailyQuestSlotReward) is: rewardCrystals, rewardFood,
// rewardWisdom, rewardRandomItem.
//
// Grant routes (all the canonical earn sites):
//   crystals  -> GameStateService.AddCrystals (Resources.Crystals wallet)
//   food      -> GameStateService.AddStone      (Resources.Stone wallet)
//   wisdom    -> WisdomCurrencyService.Grant         (talent currency)
//   item      -> VillageInventory.Add  (persisted larder/gear store -> GameState.GearInventory)
//
// Self-bootstraps via RuntimeInitializeOnLoadMethod into a DontDestroyOnLoad
// object. Village -> Core only; all cross-calls are null-conditional. Each grant
// emits a [Flow:Economy] line (CLAUDE.md §12) so a headless run proves payout.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Quests;
using DeNelle.Core.State;
using DeNelle.Cosmetics;
using DeNelle.Village.Crafting;
using DeNelle.Village.Items;
using DeNelle.Village.Talents;
using UnityEngine;

namespace DeNelle.Village.Quests
{
    [DisallowMultipleComponent]
    public sealed class DailyQuestRewardBridge : MonoBehaviour
    {
        private static DailyQuestRewardBridge _instance;
        private bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("DailyQuestRewardBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<DailyQuestRewardBridge>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            Hook();
        }

        private void OnEnable() => Hook();

        private void Update()
        {
            // DailyQuestService self-bootstraps too; if it wasn't up yet at Awake,
            // keep retrying cheaply until the subscription lands.
            if (!_hooked) Hook();
        }

        private void OnDisable() => Unhook();

        private void OnDestroy()
        {
            Unhook();
            if (_instance == this) _instance = null;
        }

        private void Hook()
        {
            if (_hooked) return;
            var svc = DailyQuestService.Instance;
            if (svc == null) return;
            svc.QuestCompleted += HandleQuestCompleted;
            // WO-1521 — the CLAIM face re-enters THIS payer, deliberately with the SAME handler.
            // A second handler (or a second dispense method) would be a second claim path and a
            // second place for the latch to be wrong; HandleQuestCompleted's ClaimedAtUnix guard
            // and its _payingOut re-entrancy set already make a repeat call safe.
            svc.ClaimRequested += HandleQuestCompleted;
            _hooked = true;
            FlowTrace.Step("Economy", "DailyQuestRewardBridge subscribed to QuestCompleted + ClaimRequested");
        }

        private void Unhook()
        {
            if (!_hooked) return;
            var svc = DailyQuestService.Instance;
            if (svc != null)
            {
                svc.QuestCompleted -= HandleQuestCompleted;
                svc.ClaimRequested -= HandleQuestCompleted;
            }
            _hooked = false;
        }

        // ── Reward dispense ────────────────────────────────────────────────────

        // WO-978 — RE-ENTRANCY GUARD, the thing the old latch-first was really buying.
        // Holds the quests whose payout is IN FLIGHT right now, so a re-fire during the
        // grants cannot double-pay. It replaces the latch as the double-grant defence,
        // which is what frees the latch to move AFTER the grants (see HandleQuestCompleted).
        private readonly HashSet<string> _payingOut = new HashSet<string>();

        private void HandleQuestCompleted(DailyQuestInstance q)
        {
            if (q == null || q.ClaimedAtUnix != 0) return; // already paid out
            var reward = DailyQuestCatalog.RewardFor(q.Slot);
            if (reward == null)
            {
                FlowTrace.Warn("Economy", $"DailyQuest '{q.TemplateId}' complete but no reward row for slot '{q.Slot}'");
                return;
            }

            // ── WO-978 / same defect shape as WO-977: THE LATCH NO LONGER RUNS FIRST ──
            // It used to read:
            //     q.ClaimedAtUnix = ...UtcNow...;   // "latch FIRST so a re-fire cannot double-grant"
            // placed ABOVE every grant. The double-grant it prevented is real, but the price was
            // worse than the bug: any grant below that no-oped (no service, no GameState, a full
            // town bank) left the quest PERMANENTLY MARKED CLAIMED having paid nothing, with no
            // way for the player to ever collect it. A re-entrancy set buys the same protection
            // without spending the player's reward, so the latch now lands only once a payout is
            // confirmed — and if NOTHING was credited we leave the quest claimable and say so.
            string key = (q.TemplateId ?? "?") + "|" + q.Slot;
            if (!_payingOut.Add(key))
            {
                FlowTrace.Warn("Economy", $"DailyQuest '{q.TemplateId}' re-entered while its payout was still in flight — " +
                                          "ignoring the duplicate event (no double-grant).");
                return;
            }

            int paidAxes = 0, requestedAxes = 0;
            try
            {
                PayOut(q, reward, ref paidAxes, ref requestedAxes);
            }
            finally
            {
                _payingOut.Remove(key);
            }

            if (requestedAxes == 0 || paidAxes > 0)
            {
                // Something landed (or the row asks for nothing at all) — latch it so the
                // credited part can never be paid twice.
                q.ClaimedAtUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                FlowTrace.Step("Economy", $"DailyQuest '{q.TemplateId}' CLAIMED (latched) after {paidAxes}/{requestedAxes} " +
                                          "reward axes were confirmed credited.");
            }
            else
            {
                // ⚠ This message used to promise the player "does not lose the reward". That was
                // FALSE and is exactly the hollow-assertion class WO-978 exists to kill — verified:
                // DailyQuestService.Report skips any quest whose Completed is already true, so
                // QuestCompleted fires EXACTLY ONCE per quest and can never re-invoke this bridge;
                // nothing else calls PayOut; and EnsureToday rolls a fresh set at midnight. So an
                // unlatched, zero-credit quest is unreachable — the reward IS lost today.
                // Not latching is still the right call (it costs nothing and leaves the door open
                // for a reclaim path), but the trace must not claim a retry that does not exist.
                FlowTrace.Fail("Economy", $"DailyQuest '{q.TemplateId}' paid NOTHING — {requestedAxes} reward axis/axes were " +
                                          "requested and 0 landed (town bank at cap is the usual cause). ClaimedAtUnix is NOT " +
                                          "latched, but there is NO reclaim path today: QuestCompleted fires once per quest and " +
                                          "the day's set is rolled at midnight, so THE REWARD IS LOST. What should happen at cap " +
                                          "is the open owner question in WO-978 section 6.");
            }
        }

        /// <summary>
        /// Dispenses every axis of the slot reward, counting how many were REQUESTED and how
        /// many actually CREDITED. WO-978: not one of these APIs returns a credited amount
        /// (<c>AddCrystals</c>/<c>AddStone</c>/<c>EconomyService.Grant</c> are all <c>void</c>;
        /// each grant is measured as a
        /// BEFORE/AFTER delta on the wallet it targets — a measured quantity, never the catalog
        /// number we asked for. Every null service now names its consequence rather than
        /// no-oping silently, copying the idiom of this file's honest sibling
        /// <see cref="GrantRandomItem"/>.
        /// </summary>
        private void PayOut(DailyQuestInstance q, DailyQuestSlotReward reward,
                            ref int paidAxes, ref int requestedAxes)
        {
            var gs = GameStateService.Instance;
            var state = gs != null ? gs.State : null;

            // Crystals -> the canonical resource wallet (the same store the build
            // menu reads/spends). AddCrystals clamps, persists, raises ResourcesChanged.
            if (reward.RewardCrystals > 0)
            {
                requestedAxes++;
                if (state == null)
                {
                    FlowTrace.Fail("Economy", $"DailyQuest '{q.TemplateId}' crystals LOST — no GameState loaded; " +
                                              $"{reward.RewardCrystals} crystals were never credited.");
                }
                else
                {
                    int before = state.Resources.Crystals;
                    gs.AddCrystals(reward.RewardCrystals);
                    int credited = state.Resources.Crystals - before;
                    if (credited > 0) paidAxes++;
                    Report(q, "crystals", credited, reward.RewardCrystals, state.Resources.Crystals);
                }
            }

            // Food -> the food wallet (Resources.Stone).
            // WO-857 Phase F: a daily-quest payout is EARNED income, the same category as the story
            // quest rewards QuestRewardBridge already routes through EconomyService.Grant, so it is
            // subject to the town bank cap (clamp-and-warn, owner ruling WO-901 §5). It used to call
            // GameStateService.AddStone directly, which is BELOW the cap seam - an unclamped back door
            // that would have let dailies alone push food past a full bank with no warn. AddStone stays
            // as the no-EconomyService fallback only (EditMode / headless boots).
            // WO-978: this is THE axis the cap actually bites — so it is the one that most needed to
            // stop printing the catalog number.
            if (reward.RewardStone > 0)
            {
                requestedAxes++;
                var eco = EconomyService.Instance;
                if (eco != null)
                {
                    int before = eco.Stone;
                    eco.Grant(stone: reward.RewardStone);
                    int credited = eco.Stone - before;
                    if (credited > 0) paidAxes++;
                    Report(q, "food", credited, reward.RewardStone, eco.Stone);
                }
                else if (state != null)
                {
                    int before = state.Resources.Stone;
                    gs.AddStone(reward.RewardStone);
                    int credited = state.Resources.Stone - before;
                    if (credited > 0) paidAxes++;
                    Report(q, "food (no-EconomyService fallback)", credited, reward.RewardStone, state.Resources.Stone);
                }
                else
                {
                    FlowTrace.Fail("Economy", $"DailyQuest '{q.TemplateId}' food LOST — no EconomyService and no GameState; " +
                                              $"{reward.RewardStone} food was never credited.");
                }
            }

            // Wisdom (WO-763, owner 2026-07-25): dailies NO LONGER pay Wisdom — Wisdom is
            // a LEVEL-UP reward only so new skills/magic feel EARNED, not handed out. The
            // daily's value is PRESERVED by redirecting the RewardWisdom amount into
            // crystals (the canonical wallet) rather than dropping it, so dailies stay
            // worth claiming without cheapening the skill economy.
            if (reward.RewardWisdom > 0)
            {
                requestedAxes++;
                if (state == null)
                {
                    FlowTrace.Fail("Economy", $"DailyQuest '{q.TemplateId}' wisdom->crystals redirect LOST — no GameState; " +
                                              $"{reward.RewardWisdom} crystals were never credited.");
                }
                else
                {
                    int before = state.Resources.Crystals;
                    gs.AddCrystals(reward.RewardWisdom);
                    int credited = state.Resources.Crystals - before;
                    if (credited > 0) paidAxes++;
                    Report(q, "wisdom->crystals (WO-763)", credited, reward.RewardWisdom, state.Resources.Crystals);
                }
            }

            // Random item -> roll a consumable from the catalog and grant it into the
            // persisted larder (VillageInventory.Add -> GameState.GearInventory). The
            // pool is the data-driven ConsumableCatalog, not a hardcoded id.
            if (reward.RewardRandomItem)
            {
                requestedAxes++;
                if (GrantRandomItem(q)) paidAxes++;
            }
        }

        /// <summary>
        /// WO-978 — the single reporting shape for a daily-quest payout axis:
        /// <c>credited/requested</c> plus the resulting total (mirroring EconomyService.cs:416).
        /// A shortfall is a Warn naming both numbers and the likely cause, never a Step.
        /// </summary>
        private static void Report(DailyQuestInstance q, string axis, int credited, int requested, int total)
        {
            if (credited >= requested)
                FlowTrace.Step("Economy", $"DailyQuest '{q.TemplateId}' credited {credited}/{requested} {axis} -> total {total}");
            else
                FlowTrace.Warn("Economy", $"DailyQuest '{q.TemplateId}' SHORT on {axis}: credited {credited} of {requested} " +
                                          $"requested -> total {total}. Daily payouts are EarnedIncome, which the town bank cap " +
                                          "clamps — the player completed the quest and did not receive the full reward (WO-978).");
        }

        /// <summary>
        /// Rolls and grants the wildcard consumable. This method was ALREADY the honest
        /// sibling in this file — it Warns on every null instead of no-oping — so WO-978
        /// changed only its signature: it now RETURNS whether the item actually landed, so
        /// the caller's claimed-latch can depend on a confirmed credit rather than on the
        /// call having been attempted.
        /// </summary>
        private bool GrantRandomItem(DailyQuestInstance q)
        {
            var pool = ConsumableCatalog.All;
            if (pool == null || pool.Count == 0)
            {
                FlowTrace.Warn("Economy", $"DailyQuest '{q.TemplateId}' random-item reward skipped — empty consumable catalog");
                return false;
            }

            // Deterministic-enough roll; no need for a seeded RNG for a cosmetic drop.
            var def = pool[Random.Range(0, pool.Count)];
            if (def == null || string.IsNullOrEmpty(def.Id))
            {
                FlowTrace.Warn("Economy", $"DailyQuest '{q.TemplateId}' random-item reward skipped — null catalog entry");
                return false;
            }

            var inv = VillageInventory.Instance;
            if (inv == null)
            {
                FlowTrace.Warn("Economy", $"DailyQuest '{q.TemplateId}' random-item '{def.Id}' lost — VillageInventory not ready");
                return false;
            }

            // VillageInventory.Add is void, but Get(id) is an observable count — measure it
            // rather than asserting the call worked (WO-978).
            int before = inv.Get(def.Id);
            inv.Add(def.Id, 1);
            int credited = inv.Get(def.Id) - before;
            if (credited > 0)
            {
                FlowTrace.Step("Economy", $"DailyQuest '{q.TemplateId}' credited random item '{def.Id}' x{credited} -> now holding {inv.Get(def.Id)}");
                return true;
            }

            FlowTrace.Warn("Economy", $"DailyQuest '{q.TemplateId}' random item '{def.Id}' did NOT land — " +
                                      $"inventory count unchanged at {inv.Get(def.Id)}; the player was not given the wildcard reward.");
            return false;
        }
    }
}
