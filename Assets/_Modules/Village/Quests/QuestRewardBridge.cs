// =============================================================================
// QuestRewardBridge — Village-side listener that DISPENSES story-quest rewards.
// -----------------------------------------------------------------------------
// QuestService (Core) raises RewardEarned(IReadOnlyList<QuestRewardLine>) when a
// stage's reward is earned, but Core cannot reference the wallet (EconomyService).
// This bridge closes that gap: it switches on each typed kind (WO-1202).
//
// XP resolves via XpEarnerRegistry.TryGet(HeroProgression.Id) — the sanctioned
// earner seam — never HeroProgression.Instance alone.
//
// Unknown kinds FAIL LOUD (FlowTrace.Fail) and skip that line only — never silent
// drop (WO-1163). kind "troop" is reserved/shape-only this pass.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Progression;
using DeNelle.Core.Quests;
using DeNelle.Village.Crafting;
using DeNelle.Village.Hero;
using UnityEngine;

namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    public sealed class QuestRewardBridge : MonoBehaviour
    {
        private static QuestRewardBridge _instance;
        private bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("QuestRewardBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<QuestRewardBridge>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            TrySubscribe();
        }

        private void OnEnable() => TrySubscribe();

        private void Update()
        {
            if (!_subscribed) TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            var svc = QuestService.Instance;
            if (svc == null) return;
            svc.RewardEarned += OnRewardEarned;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (QuestService.Instance != null) QuestService.Instance.RewardEarned -= OnRewardEarned;
            if (_instance == this) _instance = null;
        }

        private void OnRewardEarned(IReadOnlyList<QuestRewardLine> lines)
        {
            if (lines == null || lines.Count == 0) return;

            int wood = 0, iron = 0, food = 0, crystals = 0, magic = 0, xp = 0;
            var items = new List<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null) continue;
                string kind = line.NormalizedKind;
                switch (kind)
                {
                    case QuestRewardLine.KindXp:
                        xp += Mathf.Max(0, line.Amount);
                        break;
                    case QuestRewardLine.KindCrystals:
                        crystals += Mathf.Max(0, line.Amount);
                        break;
                    case QuestRewardLine.KindWood:
                        wood += Mathf.Max(0, line.Amount);
                        break;
                    case QuestRewardLine.KindIron:
                        iron += Mathf.Max(0, line.Amount);
                        break;
                    case QuestRewardLine.KindFood:
                        food += Mathf.Max(0, line.Amount);
                        break;
                    case QuestRewardLine.KindMagic:
                        magic += Mathf.Max(0, line.Amount);
                        break;
                    case QuestRewardLine.KindItem:
                        if (!string.IsNullOrEmpty(line.Id)) items.Add(line.Id);
                        else
                            FlowTrace.Fail("Quest", "reward kind 'item' with empty id — line skipped.");
                        break;
                    case QuestRewardLine.KindTroop:
                        // Shape reserved (WO-1201/1202). Not granted this pass.
                        FlowTrace.Warn("Quest",
                            "reward kind 'troop' id='" + (line.Id ?? "") +
                            "' is OUT OF SCOPE this pass — not granted.");
                        break;
                    default:
                        FlowTrace.Fail("Quest",
                            "unknown reward kind '" + (line.Kind ?? "") +
                            "' — line skipped (WO-1202 unknown-kind must never silent-drop).");
                        break;
                }
            }

            // Resources: one GrantSpendable for wood/iron/food/crystals (ECON-01).
            if (wood > 0 || iron > 0 || food > 0 || crystals > 0)
            {
                var econ = EconomyService.Instance;
                if (econ != null)
                {
                    // Prefer GrantSpendable when wood/iron present; Grant(crystals,food) for
                    // the legacy two-arg path when only those are set — GrantSpendable covers all.
                    econ.GrantSpendable(wood: wood, stone: food, iron: iron, crystals: crystals);
                    FlowTrace.Step("Economy",
                        $"Story quest granted resources wood={wood} iron={iron} food={food} crystals={crystals}");
                }
                else
                {
                    FlowTrace.Fail("Economy",
                        $"Story quest resources lost (wood={wood} iron={iron} food={food} crystals={crystals}) — EconomyService not ready");
                }
            }

            if (magic > 0)
            {
                var svc = DeNelle.Core.State.GameStateService.Instance;
                if (svc != null && svc.State != null)
                {
                    svc.State.Magic += magic;
                    svc.Save();
                    FlowTrace.Step("Economy", $"Story quest granted magic={magic}");
                }
                else
                {
                    FlowTrace.Fail("Economy", $"Story quest magic={magic} lost — GameStateService not ready");
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                string itemId = items[i];
                var inv = VillageInventory.Instance;
                if (inv != null)
                {
                    inv.Add(itemId, 1);
                    FlowTrace.Step("Economy", $"Story quest granted item '{itemId}' x1");
                }
                else
                {
                    FlowTrace.Fail("Economy", $"Story quest item '{itemId}' lost — VillageInventory not ready");
                }
            }

            if (xp > 0)
            {
                Guard.Try("Quest", "grant story-quest XP", () =>
                {
                    var earner = XpEarnerRegistry.TryGet(HeroProgression.Id);
                    if (earner == null)
                    {
                        FlowTrace.Fail("Quest",
                            $"Story quest XP={xp} lost — XpEarnerRegistry has no '{HeroProgression.Id}' earner yet");
                        return;
                    }
                    int levels = earner.AddXp(xp);
                    FlowTrace.Step("Economy",
                        $"Story quest granted xp={xp} (levelsGained={levels}) via XpEarnerRegistry");
                });
            }
        }
    }
}
