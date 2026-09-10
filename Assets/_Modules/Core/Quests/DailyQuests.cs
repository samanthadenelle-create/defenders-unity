// =============================================================================
// DailyQuests — three-slot daily-quest system ported from
// docs/daily-quests-spec.md (React agent worktree).
// -----------------------------------------------------------------------------
// One file holds:
//   • DailyQuestTemplate / DailyQuestCatalogData — the JSON shape of
//     daily-quests.json under StreamingAssets/Data/Canonical/.
//   • DailyQuestCatalog — static loader (mirrors PetCatalog).
//   • DailyQuestInstance — one rolled quest at runtime with progress.
//   • DailyQuestSet — today's three quests + reroll count + date stamp.
//   • DailyQuestService — singleton that rolls today's set at first access,
//     reads/writes the set from PlayerPrefs (PER-DAY scope — missing a day
//     just rolls fresh), and exposes Report(eventId, amount) so gameplay code
//     can tick progress without naming the manager.
//
// SCOPE: Week-1 skeleton. UI panel + reward dispensing + per-event hooks land
// in follow-up tasks. The service is wired and survives scene loads so the
// HUD bridge can subscribe today.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Core.Quests
{
    // ── JSON DTOs ────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class DailyQuestTemplate
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("slot")] public string Slot;
        [JsonProperty("target")] public int Target;
        [JsonProperty("label")] public string Label;
        [JsonProperty("weight")] public float Weight = 1f;
        [JsonProperty("requiresHero")] public string RequiresHero;
        [JsonProperty("requiresFeature")] public string RequiresFeature;
        // DEF-223: when true this template is force-selected for its slot on a
        // brand-new player's first day (until they complete it once) so the
        // tutorial-aligned "Build 4 defensive towers" quest is always present.
        [JsonProperty("day1Guaranteed")] public bool Day1Guaranteed;
    }

    [Serializable]
    public sealed class DailyQuestSlotReward
    {
        [JsonProperty("slot")] public string Slot;
        [JsonProperty("rewardCrystals")] public int RewardCrystals;
        [JsonProperty("rewardFood")] public int RewardFood;
        [JsonProperty("rewardWisdom")] public int RewardWisdom;
        [JsonProperty("rewardRandomItem")] public bool RewardRandomItem;
    }

    [Serializable]
    public sealed class DailyQuestCatalogData
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("slotCount")] public int SlotCount = 3;
        [JsonProperty("rerollsFreePerDay")] public int RerollsFreePerDay = 1;
        [JsonProperty("rerollCostCrystals")] public int RerollCostCrystals = 50;
        [JsonProperty("rerollsMaxPerDay")] public int RerollsMaxPerDay = 3;
        [JsonProperty("slots")] public List<DailyQuestSlotReward> Slots = new List<DailyQuestSlotReward>();
        [JsonProperty("templates")] public List<DailyQuestTemplate> Templates = new List<DailyQuestTemplate>();
    }

    // ── Loader ───────────────────────────────────────────────────────────────

    /// <summary>Static surface over StreamingAssets/Data/Canonical/daily-quests.json.</summary>
    public static class DailyQuestCatalog
    {
        private const string StreamingRelativePath = "Data/Canonical/daily-quests.json";

        private static DailyQuestCatalogData _data;

        public static IReadOnlyList<DailyQuestTemplate> Templates
        { get { EnsureLoaded(); return _data.Templates; } }

        public static IReadOnlyList<DailyQuestSlotReward> Slots
        { get { EnsureLoaded(); return _data.Slots; } }

        public static int RerollsFreePerDay  { get { EnsureLoaded(); return _data.RerollsFreePerDay; } }
        public static int RerollCostCrystals { get { EnsureLoaded(); return _data.RerollCostCrystals; } }
        public static int RerollsMaxPerDay   { get { EnsureLoaded(); return _data.RerollsMaxPerDay; } }

        public static DailyQuestSlotReward RewardFor(string slot)
        {
            EnsureLoaded();
            foreach (var s in _data.Slots) if (s.Slot == slot) return s;
            return null;
        }

        public static DailyQuestTemplate FindTemplate(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            foreach (var t in _data.Templates) if (t.Id == id) return t;
            return null;
        }

        /// <summary>
        /// WO-810 follow-up (2026-08-02): the ONE display-label resolution site for a rolled
        /// daily quest — "{target}" substituted with the instance's Target; a null/empty
        /// Label falls back to TemplateId, then Slot. Pure (never touches the catalog data,
        /// so it is EditMode-testable with no StreamingAssets). Both consumers route here
        /// (DailyQuestVM + RumorBoardLiveBackend — the rumor board previously skipped
        /// substitution and showed raw "{target}" titles). The save payload keeps the RAW
        /// template Label (MakeInstance) — never persist a substituted string.
        /// </summary>
        public static string ResolveLabel(DailyQuestInstance q)
        {
            if (q == null) return "";
            if (string.IsNullOrEmpty(q.Label)) return q.TemplateId ?? q.Slot;
            return q.Label.Replace("{target}", q.Target.ToString());
        }

        public static void Reload() { _data = null; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            // WebGL-safe: CanonicalJson reads the Resources dual-copy first
            // (works in a browser build) and falls back to StreamingAssets on
            // desktop. Raw File.ReadAllText would throw in WebGL → empty panel.
            DeNelle.Core.Diagnostics.FlowTrace.Step("DailyQuest", "EnsureLoaded — reading daily-quests.json.");
            try
            {
                string text = CanonicalJson.Read(StreamingRelativePath);
                if (!string.IsNullOrEmpty(text))
                {
                    var parsed = JsonConvert.DeserializeObject<DailyQuestCatalogData>(text);
                    if (parsed != null && parsed.Templates != null && parsed.Templates.Count > 0)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Step("DailyQuest", $"loaded {parsed.Templates.Count} template(s), {parsed.Slots?.Count ?? 0} slot reward(s).");
                        _data = parsed; return;
                    }
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("DailyQuest", "daily-quests.json parsed EMPTY (0 templates — mapping break) -> empty catalog.");
                    Debug.LogError("[DailyQuestCatalog] daily-quests.json parsed empty.");
                }
                else
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("DailyQuest", $"daily-quests.json not found/empty ({StreamingRelativePath}) -> empty catalog.");
                    Debug.LogError($"[DailyQuestCatalog] daily-quests.json not found ({StreamingRelativePath}).");
                }
            }
            catch (Exception ex)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("DailyQuest", $"read/parse daily-quests.json threw {ex.GetType().Name}: {ex.Message} -> empty catalog.");
                Debug.LogError($"[DailyQuestCatalog] Failed to read daily-quests.json: {ex.Message}");
            }
            _data = new DailyQuestCatalogData();
        }
    }

    // ── Runtime models ───────────────────────────────────────────────────────

    [Serializable]
    public sealed class DailyQuestInstance
    {
        public string Id;            // unique per slot per day
        public string TemplateId;
        public string Slot;
        public int Target;
        public int Progress;
        public bool Completed;
        public long ClaimedAtUnix;   // 0 = not yet claimed
        public string Label;         // RAW template label — "{target}" is NOT substituted here
                                     // (save-serialized as authored; DailyQuestCatalog.ResolveLabel
                                     // is the display-time substitution site — WO-810 follow-up)

        public float ProgressFraction => Target > 0 ? Mathf.Clamp01((float)Progress / Target) : 0f;
    }

    [Serializable]
    public sealed class DailyQuestSet
    {
        public string Date;          // YYYY-MM-DD (local)
        public int RerollsUsed;
        public List<DailyQuestInstance> Quests = new List<DailyQuestInstance>();
    }

    // ── Service ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Singleton service that holds today's three quests, persists them to
    /// PlayerPrefs, and exposes Report() so combat / exploration code can tick
    /// progress without naming this class directly.
    ///
    /// Reset rule: at the first access of a new local-date the prior day's
    /// set is replaced — no streak guilt, no FOMO, matching the spec.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DailyQuestService : MonoBehaviour
    {
        private const string PrefKey = "dotr-daily-quests-v1";
        // Sticky flag: set the first time the Day-1 build-towers quest completes,
        // so the guaranteed-quest override stops forcing it on subsequent days.
        private const string Day1DonePrefKey = "dotr-daily-quests-day1-done-v1";

        public static DailyQuestService Instance { get; private set; }
        public event Action SetChanged;
        /// <summary>
        /// Fired once per quest the moment it transitions to Completed. A reward
        /// bridge in the Village assembly listens for this to dispense the slot's
        /// crystal / wisdom reward (Core cannot reference the Village wallet, so
        /// the grant happens on the other side of the event — DEF-223).
        /// </summary>
        public event Action<DailyQuestInstance> QuestCompleted;

        /// <summary>
        /// WO-1521 — THE CLAIM VERB'S SEAM. Fired when the player presses CLAIM on a daily that
        /// is <see cref="IsClaimable"/>: completed, but never latched because its payout credited
        /// NOTHING (a full town bank is the usual cause, DailyQuestRewardBridge:168).
        ///
        /// ⛔ THIS IS NOT A SECOND CLAIM PATH. It re-enters the SAME payer — the bridge listens
        /// with the SAME handler it uses for <see cref="QuestCompleted"/>, so the re-entrancy set
        /// and the ClaimedAtUnix latch are the ones that already exist. Before this seam the
        /// "N ready to claim" counter (JourneyDeckSubtitleVM) was the only surface that could see
        /// such a quest, and there was NO verb anywhere in the game to act on it: the owner's
        /// "quests say one quest to claim but no idea how or what to do" was literally true.
        /// </summary>
        public event Action<DailyQuestInstance> ClaimRequested;

        private static readonly List<DailyQuestInstance> EmptyQuests = new List<DailyQuestInstance>();

        private DailyQuestSet _today;
        private System.Random _rng;

        public DailyQuestSet Today
        {
            get
            {
                EnsureToday();
                return _today;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("DailyQuestService");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<DailyQuestService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _rng = new System.Random();
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Increments progress for every quest whose template id starts with
        /// <paramref name="eventId"/>. Lets gameplay code post once per event
        /// — "combat.clear-waves" matches all combat clear-wave templates.
        /// </summary>
        public void Report(string eventId, int amount = 1)
        {
            if (string.IsNullOrEmpty(eventId) || amount <= 0) return;
            EnsureToday();
            bool changed = false;
            // Collect newly-completed quests and fire QuestCompleted AFTER the
            // loop + save, so a reward handler sees a consistent persisted set.
            List<DailyQuestInstance> justCompleted = null;
            foreach (var q in _today.Quests)
            {
                if (q == null || q.Completed) continue;
                if (q.TemplateId == eventId || q.TemplateId.StartsWith(eventId + "."))
                {
                    q.Progress = Mathf.Min(q.Target, q.Progress + amount);
                    if (q.Progress >= q.Target)
                    {
                        q.Completed = true;
                        (justCompleted ??= new List<DailyQuestInstance>()).Add(q);
                    }
                    changed = true;
                }
            }
            // ⛔ MAKE THE NEGATIVE OBSERVABLE (quest audit 2026-08-21, CLAUDE.md §12).
            // Three daily slots were dead for months and NOTHING said so: a report that
            // matched no active template looked exactly like a report that was never sent.
            // The proof of a broken bridge is the ABSENCE of a tick, and an absence you
            // cannot see is not evidence. Now a run states which it was, so the next
            // breakage is one capture away instead of one audit away.
            if (!changed)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("DailyQuest",
                    $"Report('{eventId}', {amount}) matched NO active daily quest. Either today's " +
                    "roll does not include that template (ordinary), or its bridge is reporting an " +
                    "id no template uses (a defect - see the quest audit).");
            }
            else
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("DailyQuest",
                    $"Report('{eventId}', {amount}) advanced " +
                    $"{(justCompleted?.Count ?? 0)} completion(s) this call.");
            }

            if (changed) { Save(); SetChanged?.Invoke(); }
            if (justCompleted != null)
            {
                foreach (var q in justCompleted)
                {
                    // Latch the Day-1 build-towers completion so the guaranteed
                    // override stops on later days.
                    if (q.TemplateId == Day1QuestTemplateId)
                    { PlayerPrefs.SetInt(Day1DonePrefKey, 1); PlayerPrefs.Save(); }
                    QuestCompleted?.Invoke(q);
                }

                // ⛔ WO-1521 — PERSIST THE LATCH THE HANDLERS JUST SET. The Save() above runs
                // BEFORE QuestCompleted fires, and DailyQuestRewardBridge writes ClaimedAtUnix
                // from inside that handler — so before this line the latch lived in MEMORY ONLY
                // and a quest paid as the last act of a session reloaded as Completed with
                // ClaimedAtUnix == 0. That is CLAIMABLE. It used to only make the "N ready to
                // claim" counter lie; the moment WO-1521 gives that state a CLAIM door it
                // becomes a DOUBLE GRANT. RequestClaim already saves after its payout; this is
                // the same rule on the completion path.
                Save();
            }
        }

        /// <summary>Template id of the tutorial-aligned Day-1 guaranteed quest.</summary>
        public const string Day1QuestTemplateId = "combat.build-towers";

        // ── WO-1521: the ONE authority for "ready to claim" ──────────────────
        // Every surface that says a number about claimable dailies reads THESE, so the
        // counter and the rumor board can never again disagree. The old counter kept its
        // own copy of the predicate (JourneyDeckSubtitleVM's inline
        // `q.Completed && q.ClaimedAtUnix == 0` loop) — a fact written twice, which is this
        // repo's dominant failure mode (CLAUDE.md sec.2/sec.5/sec.16).

        /// <summary>Today's rolled quests. Never null; empty rather than null on a bad roll.</summary>
        public IReadOnlyList<DailyQuestInstance> TodayQuests
        {
            get
            {
                var set = Today;
                return set != null && set.Quests != null ? (IReadOnlyList<DailyQuestInstance>)set.Quests : EmptyQuests;
            }
        }

        /// <summary>THE predicate. Completed work whose reward has never been latched.</summary>
        public static bool IsClaimable(DailyQuestInstance q) =>
            q != null && q.Completed && q.ClaimedAtUnix == 0;

        /// <summary>How many of today's quests are claimable right now. THE count.</summary>
        public int ClaimableCount
        {
            get
            {
                int n = 0;
                var quests = TodayQuests;
                for (int i = 0; i < quests.Count; i++) if (IsClaimable(quests[i])) n++;
                return n;
            }
        }

        /// <summary>Today's quest with this instance id (the per-day `Id`, not the template id).</summary>
        public DailyQuestInstance Find(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            var quests = TodayQuests;
            for (int i = 0; i < quests.Count; i++)
                if (quests[i] != null && quests[i].Id == questId) return quests[i];
            return null;
        }

        /// <summary>
        /// The player pressed CLAIM. Re-enters the ONE payer through <see cref="ClaimRequested"/>
        /// and reports whether the reward actually landed — judged by the LATCH the payer sets,
        /// never by the fact that an event was raised (a raised event proves a call, not a
        /// credit; CLAUDE.md sec.11B). Returns false when there was nothing to claim, when no
        /// payer is listening, or when the payout credited nothing again — the caller shows the
        /// player WHY rather than pretending.
        /// </summary>
        public bool RequestClaim(string questId)
        {
            var q = Find(questId);
            if (!IsClaimable(q))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("DailyQuest",
                    $"RequestClaim('{questId}') found no claimable quest (already claimed, or not today's).");
                return false;
            }

            if (ClaimRequested == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("DailyQuest",
                    $"RequestClaim('{questId}') has NO listener — the Village reward bridge is not hooked, " +
                    "so the CLAIM face would be a door onto nothing. Reward NOT paid.");
                return false;
            }

            ClaimRequested.Invoke(q);

            bool paid = q.ClaimedAtUnix != 0;
            if (paid)
            {
                Save();
                SetChanged?.Invoke();
                DeNelle.Core.Diagnostics.FlowTrace.Step("DailyQuest",
                    $"RequestClaim('{questId}') PAID and latched.");
            }
            else
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("DailyQuest",
                    $"RequestClaim('{questId}') credited NOTHING — the quest stays claimable so the " +
                    "player can retry once the town bank has room (WO-978 section 6).");
            }
            return paid;
        }

        /// <summary>
        /// Spends a re-roll on the given slot (free up to RerollsFreePerDay,
        /// then RerollCostCrystals each, capped at RerollsMaxPerDay). Returns
        /// the new quest, or null if the re-roll was denied.
        /// </summary>
        public DailyQuestInstance Reroll(string slot, Func<int, bool> spendCrystals = null)
        {
            EnsureToday();
            if (_today.RerollsUsed >= DailyQuestCatalog.RerollsMaxPerDay) return null;

            bool free = _today.RerollsUsed < DailyQuestCatalog.RerollsFreePerDay;
            if (!free && spendCrystals != null && !spendCrystals(DailyQuestCatalog.RerollCostCrystals))
                return null;

            int idx = _today.Quests.FindIndex(q => q != null && q.Slot == slot);
            if (idx < 0) return null;
            var roll = RollOne(slot, exclude: _today.Quests[idx]?.TemplateId);
            if (roll == null) return null;
            _today.Quests[idx] = roll;
            _today.RerollsUsed++;
            Save();
            SetChanged?.Invoke();
            return roll;
        }

        /// <summary>Forces a fresh roll (used by AdminOverlay / dev tools).</summary>
        public void ForceRollToday()
        {
            _today = RollSet(LocalDateString());
            Save();
            SetChanged?.Invoke();
        }

        // ── Internals ────────────────────────────────────────────────────────

        private void EnsureToday()
        {
            string today = LocalDateString();
            if (_today != null && _today.Date == today) return;
            if (TryLoad(out var loaded) && loaded != null && loaded.Date == today)
            { _today = loaded; return; }
            _today = RollSet(today);
            Save();
            SetChanged?.Invoke();
        }

        private DailyQuestSet RollSet(string date)
        {
            var set = new DailyQuestSet { Date = date, RerollsUsed = 0 };
            foreach (var slot in new[] { "combat", "exploration", "wildcard" })
            {
                var q = RollOne(slot, exclude: null);
                if (q != null) set.Quests.Add(q);
            }
            return set;
        }

        private DailyQuestInstance RollOne(string slot, string exclude)
        {
            var pool = new List<DailyQuestTemplate>();
            float totalWeight = 0f;
            foreach (var t in DailyQuestCatalog.Templates)
            {
                if (t == null || t.Slot != slot) continue;
                if (exclude != null && t.Id == exclude) continue;
                // Skip templates whose required feature isn't shipped (week-7).
                if (!string.IsNullOrEmpty(t.RequiresFeature) && !FeatureShipped(t.RequiresFeature)) continue;
                // WO-1430 Group B: the SIBLING gate, half-built until 2026-09-09. `requiresHero`
                // sat beside `requiresFeature` with NO reader anywhere, so the day someone
                // authored a hero requirement it would have been silently ignored - the WO-1038
                // shape (authored content, no code, no error). It is read HERE, on the same line
                // of reasoning as its sibling, and it warns rather than shrugging.
                if (!HeroRequirementMet(t.RequiresHero, CurrentHeroClass())) continue;
                // DEF-223: a Day-1-guaranteed template (build-towers) is force-
                // selected for its slot for a player who has never completed it,
                // making the tutorial-aligned quest deterministic on day one. It
                // is otherwise excluded from the random pool so it doesn't keep
                // re-appearing after completion.
                if (t.Day1Guaranteed)
                {
                    if (!Day1QuestDone && exclude != t.Id)
                        return MakeInstance(t);
                    continue;
                }
                pool.Add(t);
                totalWeight += Mathf.Max(0.01f, t.Weight);
            }
            if (pool.Count == 0)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("DailyQuest", $"RollOne('{slot}') found NO eligible template (empty pool) -> slot stays empty.");
                return null;
            }

            float pick = (float)_rng.NextDouble() * totalWeight;
            DailyQuestTemplate chosen = pool[0];
            foreach (var t in pool)
            {
                pick -= Mathf.Max(0.01f, t.Weight);
                if (pick <= 0f) { chosen = t; break; }
            }

            return MakeInstance(chosen);
        }

        private static DailyQuestInstance MakeInstance(DailyQuestTemplate t) => new DailyQuestInstance
        {
            Id = t.Id + "@" + LocalDateString(),
            TemplateId = t.Id,
            Slot = t.Slot,
            Target = t.Target,
            Progress = 0,
            Completed = false,
            ClaimedAtUnix = 0,
            Label = t.Label,
        };

        /// <summary>True once the Day-1 build-towers quest has ever been completed.</summary>
        public static bool Day1QuestDone => PlayerPrefs.GetInt(Day1DonePrefKey, 0) == 1;

        /// <summary>
        /// Can this player actually DO the thing a template asks for?
        ///
        /// <para>Historically this asked only "did this build ship the feature", and the
        /// answer was an unconditional true for everything. WO-1374 adds the second, and
        /// more useful, sense: <b>is the door open on THIS save right now</b>. A daily
        /// quest the player cannot possibly complete is worse than no daily quest - it
        /// occupies one of three slots for a whole day and teaches the player that the
        /// quest board lies.</para>
        ///
        /// <para>⛔ "raids" READS THE ONE RAID PREDICATE, IT DOES NOT RE-DERIVE IT.
        /// <c>PostureSignals.RaidCapable</c> is the single answer to "can this player
        /// raid" - the action-bar face, the Journey card and the selection screen all read
        /// it, and WO-1357 is explicit that a second barracks check on a new surface is
        /// the defect, not the fix. It defaults TRUE (the never-false-block precedent), so
        /// a headless roll or a pre-publish frame keeps the template eligible rather than
        /// silently shrinking the pool.</para>
        /// </summary>
        private static bool FeatureShipped(string feature) => feature switch
        {
            // FLAG-6: these were stale-gated false but their systems are now
            // shipped — harvesting (MineNode + OfflineHarvestService + WorkerManager),
            // tower-build (BuildModeController), cosmetic-shop (CosmeticCatalog +
            // CosmeticOwnershipService + CosmeticShopPanel), hero-talents
            // (HeroTalentCatalog + WisdomCurrencyService + HeroSkillTreePanelMvvm -
            // this line used to name TalentTreePanel, which was never wired to any
            // button and was DELETED on 2026-09-06, WO-1430 Group A).
            "harvesting"    => true,
            "tower-build"   => true,
            "cosmetic-shop" => true,
            "hero-talents"  => true,
            // WO-1374 — the ONLY per-save gate in this table. The two combat.raid.*
            // templates carry it, because before this a player with no Barracks could be
            // handed "clear 1 enemy outpost" as a daily and then find no way to attempt it.
            "raids"         => DeNelle.Core.HudModel.PostureSignals.RaidCapable,
            _ => true,
        };

        // =====================================================================
        // WO-1430 GROUP B -- `requiresHero`, the half-built sibling gate.
        // ---------------------------------------------------------------------
        // ⚠ NO SPEC DEFINES THIS FIELD. Measured 2026-09-09: `git log -S"requiresHero"`
        // over this file returns ONE commit (1a64930d7, "Daily quests, second dungeon,
        // six canonical data tables") which introduced it with no reader; the only doc
        // that mentions it is WORK_ORDER_558 (CLOSED 2026-08-26), whose §A says the
        // authored pack carries *"No requiresHero (single-Knight north star)"*.
        // daily-quests.json authors it on ZERO rows today.
        //
        // THE INTERPRETATION CHOSEN, STATED SO IT CAN BE OVERRULED: a save holds exactly
        // ONE hero (`SaveSchema.PersistedState.heroClass`, surfaced live as
        // `GameStateService.Instance.State.HeroClass`); there is no owned-hero roster in
        // the tree (searched for one, found none). So "requires hero X" can only mean
        // "the player's chosen class IS X". If the owner means something else - a party
        // member, an unlocked companion - `HeroRequirementMet` is the ONE function to
        // change, and nothing else moves.
        //
        // FAILS CLOSED AND LOUD: no class chosen yet -> not met; an unrecognised name ->
        // FlowTrace.Warn + not met. Never silently eligible: handing a player a daily
        // that names a hero they are not is the "quest board lies" failure FeatureShipped's
        // own header describes.
        // =====================================================================

        /// <summary>
        /// PURE predicate over the authored <c>requiresHero</c> string. No service lookup, so
        /// it is directly pinnable (AuthoredFieldGateRegression). Blank/absent = no requirement.
        /// </summary>
        public static bool HeroRequirementMet(string requiresHero, DeNelle.Core.State.HeroClassOpt current)
        {
            if (string.IsNullOrWhiteSpace(requiresHero)) return true;
            string wanted = requiresHero.Trim();
            // Enum.TryParse accepts the UNDERLYING NUMBER ("3") as well as the name, and a
            // numeric daily-quests.json row is authored nonsense, not a hero. Reject it here
            // so it takes the loud path below rather than silently resolving to Ranger.
            bool named = !char.IsDigit(wanted[0]) && wanted[0] != '-' && wanted[0] != '+';
            if (!named ||
                !Enum.TryParse<DeNelle.Core.State.HeroClassOpt>(wanted, true, out var required) ||
                required == DeNelle.Core.State.HeroClassOpt.None)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("DailyQuest",
                    $"template requiresHero='{requiresHero}' names no known hero class " +
                    "(Mage/Knight/Ranger/Cleric) -> template SKIPPED. Correct the authored value; " +
                    "an unreadable requirement must never silently pass as 'no requirement'.");
                return false;
            }
            if (current == DeNelle.Core.State.HeroClassOpt.None) return false;
            return required == current;
        }

        /// <summary>The live chosen hero class, or None when no save/class exists yet.</summary>
        private static DeNelle.Core.State.HeroClassOpt CurrentHeroClass()
        {
            var svc = DeNelle.Core.State.GameStateService.Instance;
            var st = svc != null ? svc.State : null;
            return st != null ? st.HeroClass : DeNelle.Core.State.HeroClassOpt.None;
        }

        private static string LocalDateString() => DateTime.Now.ToString("yyyy-MM-dd");

        private bool TryLoad(out DailyQuestSet set)
        {
            set = null;
            if (!PlayerPrefs.HasKey(PrefKey)) return false;
            try { set = JsonConvert.DeserializeObject<DailyQuestSet>(PlayerPrefs.GetString(PrefKey)); }
            catch (Exception ex)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("DailyQuest", $"TryLoad from PlayerPrefs threw {ex.GetType().Name}: {ex.Message} -> fresh roll.");
                Debug.LogWarning("[DailyQuestService] load failed: " + ex.Message);
            }
            return set != null;
        }

        private void Save()
        {
            try
            {
                PlayerPrefs.SetString(PrefKey, JsonConvert.SerializeObject(_today));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("DailyQuest", $"Save to PlayerPrefs threw {ex.GetType().Name}: {ex.Message} (progress not persisted this tick).");
                Debug.LogWarning("[DailyQuestService] save failed: " + ex.Message);
            }
        }
    }
}
