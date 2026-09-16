// =============================================================================
// HudModelProducers — WO-541 Stage 2: the per-model producers (DeNelle.Village).
// -----------------------------------------------------------------------------
// Each producer READS one or more live gameplay systems and WRITES exactly one
// Core HUD model (DeNelle.Core.HudModel). DARK / ADDITIVE — nothing reads the
// models yet (Stage 3), so these change zero runtime behaviour. Producers
// subscribe to a source event where a clean, stable one exists and otherwise
// poll (throttled by HudProducer); every write is change-gated so a model's
// Changed event + [Flow:HUD] trace only fire when a value actually changes.
//
// SOURCE MAP (each member verified against the real API — see the WO result):
//   HeroVitals  <- HeroHealth.Instance (Hp/MaxHp, OnHealthChanged),
//                  HeroAbilities (Mana/MaxMana/HeroClass),
//                  HeroProgression.Instance (Xp/XpToNext/Level, OnXpChanged)
//   Party       <- the hero (HeroHealth/HeroAbilities) + StoryCompanion.Active
//                  (the same roster PartyHudBridge feeds VillageHudController from)
//   Economy     <- EconomyService.Instance (Coins=Gold, Wood, Iron, Food, Crystals, OnChanged)
//   Wave        <- WaveManager.Instance (Phase, CurrentWaveId, CountdownRemaining, LiveEnemies)
//   Target      <- HeroTargetIndicator.CurrentTarget -> Enemy + EnemyBrain.Role
//   TargetCycle <- Enemy scan (FindObjectsByType) sorted by distance to the hero
//   Abilities   <- HeroLoadoutAccess.Current + AbilityCatalog + HeroAbilities cooldowns
//   World       <- HeartController (Hp, max=100) + Tower count (stubs noted below)
//   Momentum    <- BattleStarRating + a battle clock started when context = Battle
//   Echo        <- EchoService.Instance (EchoCount, MaxEchoes, Silo, FillFraction, Changed)
//
// Assembly law: writes Core models (Village -> Core, legal). No HUD reference.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Combat;
using DeNelle.Core.HudModel;
using DeNelle.Core.UI;         // ConceptIconResolver — resolvable concept key for ability slot icons (F1)
using DeNelle.Village;
using DeNelle.Village.Arena;   // BattleStarRating
using DeNelle.Village.Crafting;
using DeNelle.Village.Items;   // ConsumableUseService — enforced potion use-cooldown readers
using DeNelle.Village.Talents; // HeroTalentModifiers — ManaCostOf mult for ability face costs
using DeNelle.Core.HUD;
using CoreWavePhase = DeNelle.Core.HudModel.WavePhase;

namespace DeNelle.Village.Hud
{
    // ── Hero class resolution (WO-967) ────────────────────────────────────────

    /// <summary>
    /// THE one place the HUD asks "what class is the hero?" — WO-967.
    ///
    /// PRECEDENCE (deliberately the SAME shape GearLoadout.CurrentJob was fixed to under
    /// F8 seq-642, GearLoadout.cs:1251-1307 — one spelling of this question in the tree,
    /// not four):
    ///   1. the hero's LIVE HeroAbilities class;
    ///   2. the PERSISTED GameState.HeroClass — the SAME source HeroBodySwapper.ResolveHeroClass
    ///      trusts to build the BODY, so the bar can never disagree with the body;
    ///   3. a producer's own last-resolved class (presentation memory only — see below);
    ///   4. AbilityCatalog.DefaultClass, and ONLY with a FlowTrace.Warn.
    ///
    /// WHY STEP 2 EXISTS (F8 seq 2312, PROVEN from source, WO-967). A composed dungeon hero
    /// carries NO HeroAbilities: DungeonBaker.PopulateForPlay attaches only HeroLocomotion +
    /// HeroBodySwapper (DungeonBaker.cs:1168-1187), and HeroControlEnsurer.EnsureHeroCombatComponents
    /// provisions nine components and never that one — while HeroControlEnsurer.Ensure's IsVillageScene
    /// gate fails every dg_* scene. So FindAnyObjectByType&lt;HeroAbilities&gt;() returned null in every
    /// dungeon and THREE hand-written "knight" string literals in this file (the old :87, :139, :392)
    /// asserted the Knight kit. The owner, playing a MAGE, got Sword Heroic / Shield Charge /
    /// Warden's Grace / Radiant Strike on her bar in Dungeon_HealersCottage, verbatim:
    /// "in dungeon i have the knights action bar loading". The body and animator were correctly Mage
    /// in the same capture ([Flow:HeroLoco] ... avatar=MageAvatar | controller=Mage) — only the HUD
    /// was inventing a class.
    ///
    /// THIS IS THE SECOND TIME THIS EXACT DEFECT SHIPPED. GearLoadout.CurrentJob had it under
    /// F8 seq-642: it armed a KNIGHT body with a Mage staff and cloth robes AND CORRUPTED A SAVE
    /// SLOT THE PLAYER NEVER PLAYED (every persisted equip written under the wrong "-mage" key).
    /// It was fixed there with exactly this persisted-class step; this reader never got it.
    ///
    /// WHY THE CACHE IS STEP 3, NOT STEP 2 (a deliberate, documented refinement of WO-967 §4,
    /// which listed it second): the cache is a PRESENTATION field — a producer's memory of what it
    /// last resolved. Per the architecture law, presentation never owns game state, so the state
    /// layer must out-rank presentation memory. In every real flow the two agree (the cache was
    /// itself seeded from the state), so this is behaviour-identical today; it only differs when
    /// they DISAGREE, and there the persisted state is the truth by definition (it is what the
    /// BODY was built from). The cache remains as the last memory before the loud default.
    ///
    /// EVERY STEP SELF-REPORTS ITS SOURCE. Before WO-967 this whole layer was silent: a repo-wide
    /// grep of the owner's live Player.log + break-log.jsonl for the Knight skill names, "Thrain",
    /// "HeroAbilities" and "CombatArc" returned ZERO hits, which is why only her eyes could catch
    /// this. Per CLAUDE.md §12 these traces are PERMANENT — flag them off when the system is proven,
    /// never strip them.
    ///
    /// PUBLIC on purpose: DeNelle.Editor.Regression.HudHeroClassFallbackRegression pins the
    /// precedence behaviourally (the producers themselves are internal).
    /// </summary>
    public static class HudHeroClassResolver
    {
        /// <summary>Provenance label: the class came off a live HeroAbilities component.</summary>
        public const string SourceLive = "HeroAbilities(live)";
        /// <summary>Provenance label: the class came off the persisted GameState.HeroClass.</summary>
        public const string SourcePersisted = "GameState(persisted)";
        /// <summary>Provenance label: the class came off the producer's own last-resolved value.</summary>
        public const string SourceCache = "hud-cache";
        /// <summary>Provenance label: NOTHING answered — AbilityCatalog.DefaultClass was assumed.</summary>
        public const string SourceDefault = "catalog-default";

        /// <summary>
        /// The hero's class id for HUD display. See the type doc for the precedence and why.
        /// Never returns null or empty; never returns a hardcoded class literal.
        /// </summary>
        public static string Resolve(HeroAbilities abilities, string cached = null)
        {
            return Resolve(abilities, cached, out _);
        }

        /// <summary>
        /// As <see cref="Resolve(HeroAbilities,string)"/>, and reports WHICH source answered
        /// (one of the Source* constants) so a trace can name the provenance, not just the class.
        /// </summary>
        public static string Resolve(HeroAbilities abilities, string cached, out string source)
        {
            // Unity's implicit bool covers both a plain null and a destroyed-but-non-null component.
            string live = abilities ? abilities.HeroClass : null;
            return ResolveFrom(live, PersistedPlayerJob(), cached, out source);
        }

        /// <summary>
        /// The pure precedence — no Unity objects, no singletons, so the regression can prove the
        /// ORDER headlessly (a live GameStateService cannot be stood up in a batch run without
        /// touching the player's real save). Every runtime caller reaches this through
        /// <see cref="Resolve(HeroAbilities,string,out string)"/>.
        /// </summary>
        public static string ResolveFrom(string liveClass, string persistedJob, string cached, out string source)
        {
            if (!string.IsNullOrEmpty(liveClass)) { source = SourceLive; return liveClass; }

            if (!string.IsNullOrEmpty(persistedJob))
            {
                source = SourcePersisted;
                // Not a Warn: this is the CORRECT answer for any hero built without a HeroAbilities
                // component (every composed dungeon). Once-per-key so a run logs it exactly once
                // instead of at the producer's 5x/sec poll rate.
                DeNelle.Core.Diagnostics.FlowTrace.Once("HudModel", "class-from-gamestate-" + persistedJob,
                    "HUD hero class: no live HeroAbilities (composed dungeon hero) - resolved '" + persistedJob +
                    "' from the PERSISTED GameState.HeroClass, NOT a hardcoded class. Hero identity: '" +
                    DeNelle.Core.State.HeroCanonNames.ForJob(persistedJob) + "' (en.json hero." + persistedJob + ".name).");
                return persistedJob;
            }

            if (!string.IsNullOrEmpty(cached))
            {
                source = SourceCache;
                DeNelle.Core.Diagnostics.FlowTrace.Once("HudModel", "class-from-hudcache-" + cached,
                    "HUD hero class: no live HeroAbilities AND no persisted GameState.HeroClass - falling " +
                    "back to this producer's LAST-RESOLVED class '" + cached + "'. That is presentation " +
                    "memory, not state: it is right only while the class has not changed. Fix the SOURCE " +
                    "(persist the hero class) rather than relying on this line.");
                return cached;
            }

            source = SourceDefault;
            DeNelle.Core.Diagnostics.FlowTrace.Warn("HudModel",
                "HUD hero class: NO HeroAbilities, NO persisted GameState.HeroClass and no cached class - " +
                "falling back to AbilityCatalog.DefaultClass ('" + AbilityCatalog.DefaultClass + "'). The " +
                "ability bar, the nameplate AND the party card all key off that class, so if the hero is not " +
                "a " + AbilityCatalog.DefaultClass + " every one of those surfaces is LYING to the player. " +
                "A silent wrong-class default is exactly what put a Knight bar on a Mage (WO-967) and what " +
                "corrupted a save slot in GearLoadout (F8 seq-642). Fix the SOURCE - do not treat this line " +
                "as normal.");
            return AbilityCatalog.DefaultClass;
        }

        /// <summary>
        /// The lowercase job key for the PERSISTED player class, or null when no class has been
        /// chosen / no save service is up. Byte-identical to GearLoadout.PersistedPlayerJob and to
        /// HeroBodySwapper.ResolveHeroClass's source, mapped through DeNelle.Core.State.PlayableHeroes.JobKey
        /// — the same key weapons.json `job`, the armor weight-class lookup and the per-class
        /// PlayerPrefs slots use, so the bar, the body and the gear can never key off different strings.
        ///
        /// FULLY QUALIFIED ON PURPOSE (including the extension method, called statically): importing
        /// DeNelle.Core.State here would shadow DeNelle.Village names used throughout this file.
        /// </summary>
        private static string PersistedPlayerJob()
        {
            var svc = DeNelle.Core.State.GameStateService.Instance;
            if (svc == null || svc.State == null) return null;
            var opt = DeNelle.Core.State.HeroClassOptExtensions.ToNullable(svc.State.HeroClass);
            return opt.HasValue ? DeNelle.Core.State.PlayableHeroes.JobKey(opt.Value) : null;
        }
    }

    // ── HeroVitals ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <see cref="HeroVitalsModel"/> from HeroHealth + HeroAbilities + HeroProgression
    /// + WisdomCurrencyService (P4: wisdom through the model — retires the HUD's reflection
    /// pull of the service, VillageHudController.cs:1486-1497).
    /// </summary>
    internal sealed class HeroVitalsProducer : HudProducer
    {
        private HeroAbilities _abilities;
        private HeroHealth _health;
        private HeroProgression _prog;
        // last-pushed snapshot for change-gating
        private int _hp = int.MinValue, _maxHp, _mana, _maxMana, _xp, _xpToNext, _level, _wisdom;
        private string _classId;
        // WO-997 §3b: exact mana snapshot — the ints quantized a small pool to whole-point
        // steps, so sub-point regen never re-pushed the model and the bar looked frozen.
        // Change-gated with an epsilon so a 0.2-mana regen tick per 0.20s poll flows.
        private float _manaExact = float.MinValue, _maxManaExact = float.MinValue;
        private string _resourceName = "";
        private const float ManaEpsilon = 0.05f;

        public HeroVitalsProducer(IHudModel m) : base(m, 0.20f) { }

        protected override void Poll()
        {
            // HP-desync ticket 2026-07-02: FOLLOW HeroHealth.Instance, don't cache-forever. The old
            // "resolve only when null" left this producer bound to a stale-but-alive hero body when a
            // scene spawned a fresh hero (Instance moved) — the HUD then showed the untouched body's
            // pool (93/155) while the real body took the hits (92/100 in the capture). One authoritative
            // source: the CURRENT HeroHealth.Instance, and HeroAbilities from that SAME body.
            var inst = HeroHealth.Instance;
            if (inst != null && !ReferenceEquals(_health, inst)) { _health = inst; _abilities = null; }
            else if (_health == null || !_health) _health = inst;
            if ((_abilities == null || !_abilities) && _health != null)
                _abilities = _health.GetComponent<HeroAbilities>();
            if (_abilities == null || !_abilities) _abilities = Object.FindAnyObjectByType<HeroAbilities>();
            if (_prog == null || !_prog) _prog = HeroProgression.Instance;

            // No hero resolved yet -> leave the model at default (don't push zeros over a stale-but-valid value).
            if (_health == null && _abilities == null && _prog == null) return;

            int hp      = _health != null ? Mathf.CeilToInt(Mathf.Max(0f, _health.Hp)) : _hp;
            int maxHp   = _health != null ? Mathf.CeilToInt(Mathf.Max(1f, _health.MaxHp)) : _maxHp;
            // WO-997 §3b: keep the ints for display text, but carry the EXACT floats too so
            // the bar fill can show sub-point regen/burn (the ints alone step in whole points).
            float manaExact    = _abilities != null ? _abilities.Mana : _manaExact;
            float maxManaExact = _abilities != null ? _abilities.MaxMana : _maxManaExact;
            int mana    = _abilities != null ? Mathf.RoundToInt(_abilities.Mana) : _mana;
            int maxMana = _abilities != null ? Mathf.RoundToInt(_abilities.MaxMana) : _maxMana;
            int xp      = _prog != null ? Mathf.RoundToInt(_prog.Xp) : _xp;
            int xpToNext= _prog != null ? Mathf.RoundToInt(_prog.XpToNext) : _xpToNext;
            int level   = _prog != null ? _prog.Level : _level;
            // WO-967: ask the state layer, never assert a class. `_classId` is this producer's
            // sticky last-resolved value and is now the THIRD step, below the persisted state.
            string cls  = HudHeroClassResolver.Resolve(_abilities, _classId);
            // P4: unspent Wisdom straight off the service (Village -> Village, no reflection).
            // Singleton is Bootstrap-created; keep the last value while it is not up yet.
            var wis = DeNelle.Village.Talents.WisdomCurrencyService.Instance;
            int wisdom = wis != null ? wis.Wisdom : Mathf.Max(0, _wisdom);

            // WO-999: class resource identity (Mana / Vigor / Focus) for the bar label.
            string resName = _abilities != null ? (_abilities.ResourceDisplayName ?? "") : _resourceName;

            // WO-997 §3b: the mana gate is the FLOAT epsilon (0.05), not the int equality —
            // a 5 Hz poll of a 1.0/s regen moves ~0.2 mana per poll, so the model now pushes
            // every poll during regen instead of once per whole point.
            if (hp == _hp && maxHp == _maxHp &&
                Mathf.Abs(manaExact - _manaExact) < ManaEpsilon &&
                Mathf.Abs(maxManaExact - _maxManaExact) < ManaEpsilon &&
                xp == _xp && xpToNext == _xpToNext && level == _level && cls == _classId &&
                wisdom == _wisdom && resName == _resourceName) return;

            _hp = hp; _maxHp = maxHp; _mana = mana; _maxMana = maxMana;
            _manaExact = manaExact; _maxManaExact = maxManaExact;
            _xp = xp; _xpToNext = xpToNext; _level = level; _classId = cls; _wisdom = wisdom;
            _resourceName = resName;
            // Sanitize the never-resolved sentinel (float.MinValue) to the model's own
            // "not provided" sentinel (-1) so readers cleanly fall back to the ints.
            Model.HeroVitals.Set(hp, maxHp, mana, maxMana, xp, xpToNext, level, cls, wisdom,
                                 manaExact < 0f ? -1f : manaExact,
                                 maxManaExact < 0f ? -1f : maxManaExact,
                                 resName);
        }
    }

    // ── Party ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <see cref="PartyModel"/>: slot 0 = the hero, slots 1..3 = the live
    /// StoryCompanion roster (the same source PartyHudBridge feeds the HUD from).
    /// Companions are hidden when ff.singlehero is ON (matches PartyHudBridge).
    /// </summary>
    internal sealed class PartyProducer : HudProducer
    {
        private HeroHealth _health;
        private HeroAbilities _abilities;
        private string _signature;

        public PartyProducer(IHudModel m) : base(m, 0.40f) { }

        protected override void Poll()
        {
            // HP-desync ticket 2026-07-02: follow HeroHealth.Instance (see HeroVitalsProducer.Poll) —
            // slot 0 must show the SAME hero body the game is damaging, and its mana must come from
            // the HeroAbilities on that same body (stale-cached abilities = the frozen-at-full MP frame).
            var inst = HeroHealth.Instance;
            if (inst != null && !ReferenceEquals(_health, inst)) { _health = inst; _abilities = null; }
            else if (_health == null || !_health) _health = inst;
            if ((_abilities == null || !_abilities) && _health != null)
                _abilities = _health.GetComponent<HeroAbilities>();
            if (_abilities == null || !_abilities) _abilities = Object.FindAnyObjectByType<HeroAbilities>();

            var members = new List<PartyMemberRecord>(4);

            // Slot 0 — the controlled hero.
            if (_health != null || _abilities != null)
            {
                int hp = _health != null ? Mathf.CeilToInt(Mathf.Max(0f, _health.Hp)) : 0;
                int maxHp = _health != null ? Mathf.CeilToInt(Mathf.Max(1f, _health.MaxHp)) : 1;
                int mana = _abilities != null ? Mathf.RoundToInt(_abilities.Mana) : 0;
                int maxMana = _abilities != null ? Mathf.RoundToInt(_abilities.MaxMana) : 0;
                // WO-967: was a hardcoded "knight" literal — the party card slot 0 named the wrong
                // class (and therefore the wrong portrait/kit) for every hero without a live
                // HeroAbilities. Same resolver as the vitals + ability producers.
                string cls = HudHeroClassResolver.Resolve(_abilities);
                bool alive = _health == null || _health.IsAlive;
                members.Add(new PartyMemberRecord("Hero", cls, hp, maxHp, mana, maxMana, cls, alive, true));
            }

            // Slots 1..3 — story companions (hidden in single-hero mode).
            if (!FeatureFlags.SingleHero)
            {
                var roster = StoryCompanion.Active;
                int added = 0;
                for (int i = 0; i < roster.Count && added < 3; i++)
                {
                    var c = roster[i];
                    if (c == null || c.gameObject == null || !c.gameObject.activeInHierarchy) continue;
                    int max = c.MaxHp > 0f ? Mathf.CeilToInt(c.MaxHp) : 100;
                    int cur = Mathf.Clamp(Mathf.CeilToInt(c.Hp), 0, max);
                    string name = string.IsNullOrEmpty(c.DisplayName) ? "Companion" : c.DisplayName.Split(',')[0].Trim();
                    members.Add(new PartyMemberRecord(name, "companion", cur, max, 0, 0, name, cur > 0, true));
                    added++;
                }
            }

            // Change-gate on a cheap signature (count + each member's name/hp/maxHp).
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                sb.Append(m.Name).Append(':').Append(m.Hp).Append('/').Append(m.MaxHp).Append('|');
            }
            string sig = sb.ToString();
            if (sig == _signature) return;
            _signature = sig;
            Model.Party.SetMembers(members);
        }
    }

    // ── Economy ───────────────────────────────────────────────────────────────

    /// <summary>Fills <see cref="EconomyModel"/> from EconomyService (event + poll fallback).</summary>
    internal sealed class EconomyProducer : HudProducer
    {
        private EconomyService _econ;
        private bool _bound;
        private int _gold = int.MinValue, _wood, _iron, _food, _crystals;

        public EconomyProducer(IHudModel m) : base(m, 0.30f) { }

        protected override void Poll()
        {
            var econ = EconomyService.Instance;
            if (!ReferenceEquals(econ, _econ))
            {
                Unbind();
                _econ = econ;
                if (_econ != null) { _econ.OnChanged += OnEconChanged; _bound = true; }
            }
            Push();
        }

        private void OnEconChanged(ResourceSnapshot _) => Push();

        private void Push()
        {
            if (_econ == null) return;
            int gold = _econ.Coins, wood = _econ.Wood, iron = _econ.Iron, food = _econ.Stone, crystals = _econ.Crystals;
            if (gold == _gold && wood == _wood && iron == _iron && food == _food && crystals == _crystals) return;
            _gold = gold; _wood = wood; _iron = iron; _food = food; _crystals = crystals;
            Model.Economy.Set(gold, wood, iron, food, crystals);
        }

        private void Unbind() { if (_bound && _econ != null) _econ.OnChanged -= OnEconChanged; _bound = false; }
        public override void Dispose() => Unbind();
    }

    // ── Wave ──────────────────────────────────────────────────────────────────

    /// <summary>Fills <see cref="WaveModel"/> from WaveManager (Phase maps to the Core WavePhase).</summary>
    internal sealed class WaveProducer : HudProducer
    {
        private const float ImminentThreshold = 5f; // countdown seconds at which a wave reads "imminent"

        private CoreWavePhase _phase = (CoreWavePhase)(-1);
        private int _number = -1, _live = -1, _total = -1;
        private float _countdown = -1f;
        private bool _imminent;

        public WaveProducer(IHudModel m) : base(m, 0.20f) { }

        protected override void Poll()
        {
            var wm = WaveManager.Instance;
            if (wm == null) return; // no wave loop in this scene -> leave model at default

            CoreWavePhase phase = MapPhase(wm.Phase);
            int number = Mathf.Max(0, wm.CurrentWaveId);
            float countdown = wm.CountdownRemaining;

            int live = 0, total = 0;
            var enemies = wm.LiveEnemies;
            if (enemies != null)
            {
                total = enemies.Count;
                for (int i = 0; i < enemies.Count; i++)
                    if (enemies[i] != null && enemies[i].IsAlive) live++;
            }

            bool imminent = phase == CoreWavePhase.Countdown && countdown <= ImminentThreshold;

            // Change-gate (countdown bucketed to whole seconds so the timer doesn't churn every poll).
            int cdBucket = Mathf.CeilToInt(countdown);
            int lastCd = Mathf.CeilToInt(_countdown);
            if (phase == _phase && number == _number && live == _live && total == _total &&
                cdBucket == lastCd && imminent == _imminent) return;

            _phase = phase; _number = number; _live = live; _total = total;
            _countdown = countdown; _imminent = imminent;

            // LookoutStatus / Max / ClearBanner have no clean WaveManager source (the
            // schedule total + banner copy are not exposed) — left as neutral stubs.
            Model.Wave.Set(phase, number, 0, countdown, imminent, null, live, total, null);
        }

        private static CoreWavePhase MapPhase(DeNelle.Village.WavePhase p)
        {
            switch (p)
            {
                case DeNelle.Village.WavePhase.Countdown: return CoreWavePhase.Countdown;
                case DeNelle.Village.WavePhase.Active:    return CoreWavePhase.Active;
                case DeNelle.Village.WavePhase.Breached:  return CoreWavePhase.Breached;
                case DeNelle.Village.WavePhase.Complete:  return CoreWavePhase.Cleared;
                case DeNelle.Village.WavePhase.Defeated:  return CoreWavePhase.Defeated;
                default:                                  return CoreWavePhase.Idle;
            }
        }
    }

    // ── Target ────────────────────────────────────────────────────────────────

    /// <summary>Fills <see cref="TargetModel"/> from HeroTargetIndicator's locked target.</summary>
    internal sealed class TargetProducer : HudProducer
    {
        private HeroTargetIndicator _indicator;
        private bool _hadTarget;
        private string _sig;

        public TargetProducer(IHudModel m) : base(m, 0.15f) { }

        protected override void Poll()
        {
            if (_indicator == null || !_indicator) _indicator = Object.FindAnyObjectByType<HeroTargetIndicator>();

            IDamageable cur = _indicator != null ? _indicator.CurrentTarget : null;
            var curMb = cur as MonoBehaviour;
            var en = curMb != null ? curMb.GetComponentInParent<Enemy>() : null;

            if (cur == null || !cur.IsAlive || en == null)
            {
                if (_hadTarget) { _hadTarget = false; _sig = null; Model.Target.Clear(); }
                return;
            }

            var role = RoleOf(en);
            string name = FriendlyName(en, role);
            int hp = Mathf.CeilToInt(Mathf.Max(0f, en.Hp));
            int maxHp = Mathf.CeilToInt(Mathf.Max(1f, en.MaxHp));
            float frac = en.HpFraction;
            // WO-1232 (owner ruling 2026-08-26) — THE NUMBER IS GONE. What the frame shows beside
            // the name is the enemy's AUTHORED classification WORD and nothing else:
            //   boss:true      -> "BOSS"
            //   role:"elite"   -> "ELITE"
            //   anything else  -> "" (NOTHING is rendered - silence is the default, not a blank
            //                         label the player has to interpret)
            // The old "Lv N" was round(def.Hp / 25) - HP wearing a level costume, which is how a
            // wave enemy read "Lv 68" beside a Lv 5 hero. Owner: "HP / 25 is not a level system.
            // Dressing it up as one just produces very confident nonsense."
            //
            // APEX IS DELIBERATELY NOT EMITTED. waves.json authors an apexBoss, but until a tier is
            // authored on purpose a third badge would re-invent the precision this ruling removed.
            //
            // The "!"/"!!" threat prefix that used to be pasted onto the name here is REMOVED with
            // the RiskyDelta/LethalDelta banding that drove it - it graded a fake level against the
            // hero's real one. Its replacement (a Combat Rating from HP/damage/cadence/armour/
            // abilities/role, shown Low/Even/High/Deadly) is a SEPARATE, UNBUILT spec: do not stub
            // it here. A WORD, never a tint - the owner is red/green colourblind. ASCII only.
            string badge = EnemyBadge.For(en);
            bool locked = _indicator != null && _indicator.LockEngaged;

            string sig = $"{name}|{badge}|{hp}/{maxHp}|{role}|{locked}";
            if (sig == _sig) return;
            _sig = sig;
            _hadTarget = true;

            // CLAUDE.md S12 - PERMANENT instrumentation. It now names the source of the BADGE (the
            // authored EnemyDef flags), and still prints the retired HP-derived magnitude beside it
            // so a silent re-appearance of a numeric level on this frame is one log read rather than
            // a felt-test. The line is sig-gated above, so it fires on target change, never a frame.
            DeNelle.Core.Diagnostics.FlowTrace.Step("HudTarget",
                $"target classification resolved: badge='{badge}' from Enemy.Tier={en.Tier} " +
                $"(authored EnemyDef boss/role, def='{en.EnemyDefId}') - NO numeric level is displayed " +
                $"(WO-1232); for reference the retired HP-derived magnitude was {en.Level} " +
                $"at runtime maxHp={maxHp}.");
            Model.Target.Set(true, name, badge, hp, maxHp, frac, ToHudRole(role), locked);
        }

    }

    // ── TargetCycle ───────────────────────────────────────────────────────────

    /// <summary>Fills <see cref="TargetCycleModel"/> from the live enemies, distance-sorted to the hero.</summary>
    internal sealed class TargetCycleProducer : HudProducer
    {
        private HeroAbilities _hero;
        private string _sig;

        public TargetCycleProducer(IHudModel m) : base(m, 0.30f) { }

        protected override void Poll()
        {
            if (_hero == null || !_hero) _hero = Object.FindAnyObjectByType<HeroAbilities>();
            Vector3 me = _hero != null ? _hero.transform.position : Vector3.zero;

            var enemies = Object.FindObjectsByType<Enemy>();
            System.Array.Sort(enemies, (a, b) =>
            {
                if (a == null) return 1; if (b == null) return -1;
                return (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude);
            });

            var list = new List<TargetRecord>(enemies.Length);
            for (int i = 0; i < enemies.Length; i++)
            {
                var en = enemies[i];
                if (en == null || en.IsDead) continue;
                var role = RoleOf(en);
                list.Add(new TargetRecord(en.GetInstanceID().ToString(), FriendlyName(en, role),
                                          en.HpFraction, ToHudRole(role), en.IsAlive));
            }

            // Change-gate on id+hp signature (HpFraction bucketed to avoid per-poll churn).
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
                sb.Append(list[i].Id).Append(':').Append(Mathf.RoundToInt(list[i].HpFraction * 20f)).Append('|');
            string sig = sb.ToString();
            if (sig == _sig) return;
            _sig = sig;
            Model.TargetCycle.SetTargets(list);
        }
    }

    // ── AbilityLoadout ────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <see cref="AbilityLoadoutModel"/> (4 slots Q/W/E/R). Q = the class basic
    /// attack (AbilityCatalog.Find); W/E/R = the equipped skill-tree ability via
    /// HeroLoadoutAccess + AbilityCatalog.FindById. Cooldowns from HeroAbilities.
    /// Mirrors BattleHud9Zone.ResolveSlotDef.
    /// </summary>
    internal sealed class AbilityLoadoutProducer : HudProducer
    {
        private HeroAbilities _abilities;
        private string _sig;
        // WO-1019: the class this producer last BOUND the bar to, so the trace can name the
        // TRANSITION (knight -> mage) and not just the destination. A hero switch that failed to
        // rebind is only visible as an old->new class with UNCHANGED ids.
        private string _boundClass;

        public AbilityLoadoutProducer(IHudModel m) : base(m, 0.20f) { }

        protected override void Poll()
        {
            if (_abilities == null || !_abilities) _abilities = Object.FindAnyObjectByType<HeroAbilities>();

            // WO-967 — THE REPORTED BUG lived on this line as a hardcoded "knight" literal. A
            // composed dungeon hero carries no HeroAbilities, so `_abilities` is null in every
            // dungeon and the bar asserted the Knight kit at a Mage. Ask the state layer instead;
            // `source` names WHICH source answered so the capture can prove it next time.
            string cls = HudHeroClassResolver.Resolve(_abilities, null, out string clsSource);
            var slots = new List<AbilitySlotRecord>(4);
            var sb = new System.Text.StringBuilder();
            List<string> unmapped = null;   // F8-33: equipped slots whose concept resolved NO real art

            for (int i = 0; i < 4; i++)
            {
                var slot = (AbilitySlot)i;
                string key = slot.ToString();
                AbilityDef def = ResolveSlotDef(slot, cls, _abilities);
                bool equipped = def != null;
                float total = equipped ? def.Cooldown : 0f;
                float remaining = _abilities != null ? _abilities.CooldownRemaining(slot) : 0f;
                string name = equipped && !string.IsNullOrEmpty(def.Name) ? def.Name : key;
                // F1 (ABILITY_ICON_AUDIT_2026-07-05): do NOT forward def.Icon — that is the decorative
                // glyph ("✦"), which no concept-icons.json key matches, so every slot resolved null and
                // rendered blank (0/11). Store a RESOLVABLE concept key (abilityId, then effect) so
                // UiStyle.Icon(IconKey) downstream draws real RpgUi art; ResolveKey==null (nothing maps)
                // falls through to def.Effect/def.Id so the SetIcon default-sprite backstop still fills it.
                string resolvedKey = equipped ? ConceptIconResolver.ResolveKey(def.Id, def.Effect) : null;
                string icon = equipped ? (resolvedKey ?? def.Effect ?? def.Id) : null;
                // WO-1695 follow-up, owner 2026-09-10: retire the July word placeholder.
                // Knight Q now retains its authored charge_knight concept art, just like
                // every other mapped ability; the dock's generic text-face support stays.
                // F8-33 (owner: right-side ability icons hard-coded/placeholder): a slot whose
                // concept did NOT resolve real art renders the SetIcon default backstop — that
                // fallback must never be silent. Collected here, warned once below on a loadout
                // signature change (Poll runs 5x/s — warning per poll would spam the log).
                if (equipped && resolvedKey == null)
                {
                    if (unmapped == null) unmapped = new List<string>(2);
                    unmapped.Add($"{key}='{def.Id ?? def.Name}' (effect='{def.Effect}')");
                }
                string accent = equipped ? def.Color : null;
                // WO-999: cost pip + affordability — mirror ManaCostOf (Cathedral mult for mage).
                float manaCost = 0f;
                if (equipped)
                {
                    manaCost = def.ManaCost;
                    if (_abilities != null)
                        manaCost *= HeroTalentModifiers.MageManaCostMultiplier(_abilities.HeroClass);
                }
                float curMana = _abilities != null ? _abilities.Mana : 0f;
                bool affordable = !equipped || manaCost <= 0f || curMana + 0.001f >= manaCost;

                // ⭐ WO-1105 REVISION (owner 2026-08-16, verbatim: "change the bow and arrow attack
                // to the action bar and leave the attack as the dagger attack"). The bow is an
                // ACTION-BAR ABILITY, not the primary attack — and this is the slot it already
                // lives in, because `ranger.q` (Quick Shot, 15 m) IS the class's locked Q def. The
                // authored VERB rides the slot record so the medallion can wear the word the owner
                // asked for ("It should be the word shoot") alongside its bow icon, which the
                // concept map already binds (ranger.q -> spellicons/Hunter12) with no C# choice.
                // DATA, never a per-class table: only abilities that author a `verb` show one, so
                // the Knight's medallions are untouched and a class added tomorrow needs no edit.
                string verb = equipped && !string.IsNullOrEmpty(def.Verb) ? def.Verb : "";
                slots.Add(new AbilitySlotRecord(key, key, name, "", icon, accent, equipped, remaining, total,
                                                manaCost, affordable, verb));
                sb.Append(key).Append('=').Append(equipped ? name : "-")
                  .Append(':').Append(Mathf.CeilToInt(remaining)).Append('/').Append(Mathf.RoundToInt(total))
                  .Append('@').Append(manaCost.ToString("0.#")).Append(affordable ? "" : "!").Append('|');
            }

            // Include live mana bucket so unaffordable faces flip when regen crosses a threshold.
            float manaBucket = _abilities != null ? Mathf.Floor(_abilities.Mana * 2f) * 0.5f : -1f;
            string sig = sb.ToString() + "|m=" + manaBucket.ToString("0.0");
            if (sig == _sig) return;
            _sig = sig;
            // WO-967: the right-hand ability bar used to emit NOTHING — a repo-wide grep of the
            // owner's live logs for the Knight skill names, "Thrain", "HeroAbilities" and
            // "CombatArc" returned ZERO hits, so a Knight bar on a Mage was catchable only by her
            // eyes. This fires on a loadout-signature CHANGE only (never per poll, Poll runs 5x/s)
            // and names the class, WHERE the class came from, and the ability ids it produced —
            // so "class='knight' source=... " on a Mage names the defect on sight next time.
            //
            // WO-1019 EXTENDS THAT SAME LINE (one trace vocabulary, not a competing second one):
            // it now names the BAR (qwer vs hotswap) and the class TRANSITION. The owner's
            // "he inherits the hotswap from previous character" was a bar that did not REBIND on a
            // hero switch, and a destination-only line cannot show that — "class='mage' (was
            // 'knight') ids=[Sword Heroic,...]" names the defect on sight, where "class='mage'
            // ids=[...]" alone does not.
            string wasClause = (!string.IsNullOrEmpty(_boundClass) && _boundClass != cls)
                ? " (was '" + _boundClass + "')" : "";
            _boundClass = cls;
            DeNelle.Core.Diagnostics.FlowTrace.Step("HudModel",
                "ability bar bound: bar=qwer class='" + cls + "'" + wasClause + " source=" + clsSource +
                " hero='" + DeNelle.Core.State.HeroCanonNames.ForJob(cls) + "' ids=[" +
                string.Join(",", slots.ConvertAll(s => s.Name).ToArray()) + "] sig=" + sig);
            // F8-33: name every ability that fell back to placeholder art — once per loadout
            // change, never per poll. No silent placeholder (CLAUDE.md §12 "no silent failures").
            if (unmapped != null)
                for (int i = 0; i < unmapped.Count; i++)
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("HudModel",
                        "ability icon unmapped — placeholder shown for " + unmapped[i] +
                        "; add a concept-icons.json entry to bind real art");
            Model.Abilities.SetSlots(slots);
        }

        private static AbilityDef ResolveSlotDef(AbilitySlot slot, string cls, HeroAbilities abilities)
        {
            // SINGLE SOURCE OF TRUTH (E-medallion-vs-cast fix): resolve the slot through the
            // SAME path the cast uses — HeroAbilities.ResolvedDef -> the hero's OWN HeroLoadout
            // + _heroClass — so the ICON shown is always the ability actually CAST. The old path
            // re-derived class (a hardcoded "knight") + loadout via HeroLoadoutAccess.Current, a
            // DIFFERENT lookup that could disagree with the cast: for E it returned null/empty and
            // fell back to the class stock def (Knight "Defender's Call" taunt / shield icon) while
            // the cast resolved the equipped HEAL. Routing both through the hero collapses the fork
            // (class + loadout come from the one component) so the equipped heal's icon renders.
            // Fall back to the class catalog ONLY when no hero exists at all.
            if (abilities != null)
            {
                var def = abilities.ResolvedDef(slot);
                if (def != null) return def;
            }
            if (slot == AbilitySlot.Q) return AbilityCatalog.Find(cls, slot);
            var lo = HeroLoadoutAccess.Current;
            string id = lo != null ? lo.AbilityIdForSlot(slot) : null;
            // F8 2026-07-11 "where are the defaults for the action rails": empty loadout
            // slots fall back to the CLASS KIT (Bash/Charge/Radiant) — the legacy bridge
            // (HeroAbilitiesHudBridge.ResolveSlotDef:340) always did this; the v8 producer
            // dropped the line, leaving W/E/R blank in normal play.
            var eq = string.IsNullOrEmpty(id) ? null : AbilityCatalog.FindById(id);
            return eq ?? AbilityCatalog.Find(cls, slot);
        }
    }

    // ── WorldMetrics ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <see cref="WorldMetricsModel"/> from the Heart (Hp, max=100) + a live
    /// tower count. Population / passive-XP / forgetting / wards / minimap have no
    /// clean single source yet and are left as neutral stubs (noted in the WO result).
    /// </summary>
    internal sealed class WorldMetricsProducer : HudProducer
    {
        private const float HeartMaxHp = 100f;   // HeartController.Hp is 0..100 (HeartHudBridge.HeartMaxHp)

        private HeartController _heart;
        private int _heartHp = int.MinValue, _towers;

        public WorldMetricsProducer(IHudModel m) : base(m, 0.50f) { }

        protected override void Poll()
        {
            if (_heart == null || !_heart) _heart = Object.FindAnyObjectByType<HeartController>();
            if (_heart == null)
            {
                // NO HEART IN THIS SCENE -> publish an explicit EMPTY, do NOT early-return.
                // The old bare `return` left WorldMetricsModel holding the HUB's last pushed
                // Hp across a scene change, so anything that rendered the Heart bar outside
                // the village drew a plausible FILLED bar (owner felt-test, Seeker: full
                // "Heart of Elarion" bar inside Dungeon_HealersCottage) and the
                // "[Flow:HUD] heart <hp>/<max>" line never re-fired to show it. Belt-and-
                // braces behind the HUD's scene gate: any future leak now reads EMPTY and
                // is visible in the trace. Published ONCE (the _heartHp latch keeps the
                // 0.5s poll quiet afterwards); SetMetrics does the throttled trace itself.
                if (_heartHp == 0 && _towers == 0) return;
                _heartHp = 0; _towers = 0;
                Model.World.SetMetrics(0, 0, 0f, 0, 0, 0, 0f, 0, 0, 0, 0, null);
                return;
            }

            int heartHp = Mathf.CeilToInt(Mathf.Max(0f, _heart.Hp));
            int maxHp = Mathf.CeilToInt(HeartMaxHp);
            float pct = HeartMaxHp > 0f ? Mathf.Clamp01(_heart.Hp / HeartMaxHp) : 0f;

            var towersArr = Object.FindObjectsByType<Tower>();
            int towers = towersArr != null ? towersArr.Length : 0;

            if (heartHp == _heartHp && towers == _towers) return;
            _heartHp = heartHp; _towers = towers;

            // Stubs: TowersMax=0, Population=0, PassiveXpPerMin=0, PassiveTowerCount=towers,
            // ForgettingLevel=0, Wards 0/0 — no clean producer source for these yet.
            Model.World.SetMetrics(heartHp, maxHp, pct, towers, 0, 0, 0f, towers, 0, 0, 0, null);
        }
    }

    // ── Momentum ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <see cref="MomentumModel"/> from BattleStarRating + a battle clock started
    /// when the HudContext becomes Battle. Combo / KillStreak have no clean producer
    /// source yet (the HUD's SetComboCount is pushed by combat events not exposed as a
    /// pollable counter) and are left at 0 (noted in the WO result).
    /// </summary>
    internal sealed class MomentumProducer : HudProducer
    {
        private float _battleStart = -1f;
        private int _stars = int.MinValue, _elapsedBucket = -1;

        public MomentumProducer(IHudModel m) : base(m, 0.25f) { }

        protected override void Poll()
        {
            bool inBattle = Model.Context != null && Model.Context.CombatActive;
            if (!inBattle)
            {
                if (_battleStart >= 0f)
                {
                    _battleStart = -1f; _stars = 0; _elapsedBucket = 0;
                    Model.Momentum.Set(0, 0, 0, 0f, 0f);
                }
                return;
            }

            if (_battleStart < 0f) _battleStart = Time.time;
            float elapsed = Time.time - _battleStart;
            int stars = BattleStarRating.StarsForDuration(elapsed);

            // Seconds remaining before the next star threshold drops (keep-star countdown).
            float nextDrop = elapsed <= BattleStarRating.ThreeStarSeconds ? BattleStarRating.ThreeStarSeconds
                           : elapsed <= BattleStarRating.TwoStarSeconds ? BattleStarRating.TwoStarSeconds
                           : -1f;
            float keepStar = nextDrop < 0f ? 0f : Mathf.Max(0f, nextDrop - elapsed);

            int bucket = Mathf.FloorToInt(elapsed);
            if (stars == _stars && bucket == _elapsedBucket) return;
            _stars = stars; _elapsedBucket = bucket;
            Model.Momentum.Set(0, 0, stars, elapsed, keepStar);
        }
    }

    // ── Echo ──────────────────────────────────────────────────────────────────

    /// <summary>Fills <see cref="EchoModel"/> from EchoService (event + poll fallback).</summary>
    internal sealed class EchoProducer : HudProducer
    {
        private EchoService _echo;
        private bool _bound;
        private int _count = int.MinValue, _max;
        private float _silo = -1f, _fill = -1f;

        public EchoProducer(IHudModel m) : base(m, 0.40f) { }

        protected override void Poll()
        {
            var echo = EchoService.Instance;
            if (!ReferenceEquals(echo, _echo))
            {
                Unbind();
                _echo = echo;
                if (_echo != null) { _echo.Changed += Push; _bound = true; }
            }
            Push();
        }

        private void Push()
        {
            if (_echo == null) return;
            int count = _echo.EchoCount, max = _echo.MaxEchoes;
            float silo = (float)_echo.Silo, fill = _echo.FillFraction;
            if (count == _count && max == _max &&
                Mathf.Approximately(silo, _silo) && Mathf.Approximately(fill, _fill)) return;
            _count = count; _max = max; _silo = silo; _fill = fill;
            Model.Echo.Set(count, max, silo, fill);
        }

        private void Unbind() { if (_bound && _echo != null) _echo.Changed -= Push; _bound = false; }
        public override void Dispose() => Unbind();
    }

    // ── Cast (wind-up telegraph FALLBACK bar) ─────────────────────────────────

    /// <summary>
    /// Fills <see cref="CastModel"/> from the cast wind-up seams (P4, HUD_OBSIDIAN
    /// §3.4): subscribes Enemy.CastStarted/CastEnded AND (2026-08-16) the hero's
    /// HeroAbilities.CastWindupStarted/Ended (push) and interpolates Progress01
    /// on its own fast poll, change-gated to 2% buckets so the model never fires on an
    /// unchanged value. One cast bar (V1): the LATEST cast wins; an ended earlier cast is
    /// ignored once superseded. SELF-EXPIRES on (start + windUp) or a dead/destroyed
    /// caster — a caster destroyed mid-cast kills its coroutine before CastEnded fires.
    ///
    /// OWNER RULING 2026-08-16: the Spells Pack Casting_* loop on the caster is the
    /// wind-up telegraph INSTEAD of this bar. When <see cref="CastingTelegraphVfx"/>
    /// reports a LIVE spawned telegraph for the caster, the bar is suppressed for that
    /// cast; when the VFX did not spawn (missing mirror / CastingTelegraphVfx.
    /// UseVfxTelegraph=false) the bar shows exactly as before — the player always
    /// sees wind-up feedback. Flipping UseVfxTelegraph=false restores the bar-only
    /// behaviour with no other change.
    /// </summary>
    internal sealed class CastProducer : HudProducer
    {
        private Component _caster;     // Enemy OR HeroAbilities
        private Enemy _casterEnemy;    // non-null when the caster is an enemy (dead-check)
        private string _casterName;
        private string _ability;
        private float _start = -1f, _duration = 1f;
        private int _lastBucket = -1;
        private bool _visible;

        public CastProducer(IHudModel m) : base(m, 0.10f)
        {
            Enemy.CastStarted += OnEnemyCastStarted;
            Enemy.CastEnded += OnEnemyCastEnded;
            HeroAbilities.CastWindupStarted += OnHeroCastStarted;
            HeroAbilities.CastWindupEnded += OnHeroCastEnded;
        }

        private void OnEnemyCastStarted(Enemy caster, string ability, float windUpSeconds)
        {
            if (caster == null) return;
            if (Suppressed(caster, ability)) return;
            Begin(caster, caster, FriendlyName(caster, RoleOf(caster)), ability, windUpSeconds);
        }

        // 2026-08-16: hero wind-ups join the same single bar, FALLBACK-ONLY — the
        // Casting_* VFX on the hero is the primary telegraph; the bar shows a hero
        // cast only when that VFX failed to spawn (never a silent no-telegraph).
        private void OnHeroCastStarted(HeroAbilities caster, string ability, float windUpSeconds)
        {
            if (caster == null) return;
            if (Suppressed(caster, ability)) return;
            DeNelle.Core.Diagnostics.FlowTrace.Step("HUD", $"cast bar FALLBACK for hero wind-up '{ability}' (no Casting_* VFX spawned)");
            Begin(caster, null, "You", ability, windUpSeconds);
        }

        // Owner ruling 2026-08-16: a LIVE spawned Casting_* telegraph replaces the bar.
        private bool Suppressed(Component caster, string ability)
        {
            if (!CastingTelegraphVfx.IsTelegraphed(caster)) return false;
            DeNelle.Core.Diagnostics.FlowTrace.Step("HUD", $"cast bar SUPPRESSED (Casting_* wind-up telegraph live) caster={caster.name} ability='{ability}'");
            return true;
        }

        private void Begin(Component caster, Enemy enemy, string casterName, string ability, float windUpSeconds)
        {
            _caster = caster;
            _casterEnemy = enemy;
            _casterName = casterName;
            _ability = ability;
            _duration = Mathf.Max(0.05f, windUpSeconds);
            _start = Time.time;
            _lastBucket = -1;          // force the first push of the new cast
            Push(0f);
        }

        private void OnEnemyCastEnded(Enemy caster)
        {
            // Only the cast we are tracking may clear the bar (a superseded cast's
            // end event must not kill the newer cast's bar).
            if (!ReferenceEquals(caster, _caster)) return;
            Hide();
        }

        private void OnHeroCastEnded(HeroAbilities caster)
        {
            if (!ReferenceEquals(caster, _caster)) return;
            Hide();
        }

        protected override void Poll()
        {
            if (_start < 0f) return;   // no live cast
            // Self-expiry: destroyed/dead casters end the coroutine without CastEnded.
            if (_caster == null || !_caster || (_casterEnemy != null && _casterEnemy.IsDead)) { Hide(); return; }
            float t = Mathf.Clamp01((Time.time - _start) / _duration);
            if (t >= 1f) { Hide(); return; }
            Push(t);
        }

        // Change gate: 2% progress buckets — a poll with an unchanged bucket writes nothing.
        private void Push(float t01)
        {
            int bucket = Mathf.RoundToInt(t01 * 50f);
            if (_visible && bucket == _lastBucket) return;
            _visible = true;
            _lastBucket = bucket;
            Model.Cast.Set(_casterName, _ability, t01);
        }

        private void Hide()
        {
            _start = -1f;
            _caster = null;
            _casterEnemy = null;
            if (!_visible) return;     // change gate: never clear an already-clear model
            _visible = false;
            _lastBucket = -1;
            Model.Cast.Clear();
        }

        public override void Dispose()
        {
            Enemy.CastStarted -= OnEnemyCastStarted;
            Enemy.CastEnded -= OnEnemyCastEnded;
            HeroAbilities.CastWindupStarted -= OnHeroCastStarted;
            HeroAbilities.CastWindupEnded -= OnHeroCastEnded;
        }
    }

    // ── AssignableLoadout (WO-609) ────────────────────────────────────────────

    /// <summary>
    /// Fills <see cref="AssignableLoadoutModel"/> from the hero's
    /// <see cref="AssignableSkillBar"/> (4 hotswap slots) + extra cooldowns.
    /// </summary>
    internal sealed class AssignableLoadoutProducer : HudProducer
    {
        private HeroAbilities _abilities;
        private string _sig;
        // WO-1019: same pair of fields as AbilityLoadoutProducer, for the same reason.
        private string _boundClass;

        public AssignableLoadoutProducer(IHudModel m) : base(m, 0.20f) { }

        protected override void Poll()
        {
            if (_abilities == null || !_abilities) _abilities = Object.FindAnyObjectByType<HeroAbilities>();
            var bar = AssignableSkillBarAccess.Current;
            // WO-1019: THE bar the owner reported ("he inherits the hotswap from previous
            // character") and it was emitting NOTHING — WO-967 instrumented only the Q/W/E/R rail,
            // so the hot-swap rail's contents were invisible in every capture and the defect was
            // catchable only by her eyes. Same resolver, same trace vocabulary as that rail.
            string cls = HudHeroClassResolver.Resolve(_abilities, _boundClass, out string clsSource);

            var slots = new List<AbilitySlotRecord>(AssignableSkillBar.SlotCount);
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < AssignableSkillBar.SlotCount; i++)
            {
                string id = bar != null ? bar.AbilityIdForSlot(i) : null;
                bool equipped = !string.IsNullOrEmpty(id);
                AbilityDef def = equipped ? AbilityCatalog.FindById(id) : null;
                float total = def != null ? def.Cooldown : 0f;
                float remaining = equipped && _abilities != null ? _abilities.ExtraCooldownRemaining(id) : 0f;
                string name = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : (equipped ? id : "—");
                // F1 (ABILITY_ICON_AUDIT_2026-07-05): store a RESOLVABLE concept key (abilityId, then
                // effect) — not the decorative glyph def.Icon — so the hot-swap slot draws real art.
                string icon = def != null
                    ? (ConceptIconResolver.ResolveKey(def.Id, def.Effect) ?? def.Effect ?? def.Id)
                    : null;
                string accent = def != null ? def.Color : null;
                string key = "A" + i;

                slots.Add(new AbilitySlotRecord(key, key, name, "", icon, accent, equipped, remaining, total));
                sb.Append(key).Append('=').Append(equipped ? name : "-")
                  .Append(':').Append(Mathf.CeilToInt(remaining)).Append('/').Append(Mathf.RoundToInt(total)).Append('|');
            }

            string sig = sb.ToString();
            if (sig == _sig && string.Equals(_boundClass, cls, System.StringComparison.Ordinal)) return;
            _sig = sig;
            string wasClause = (!string.IsNullOrEmpty(_boundClass) && _boundClass != cls)
                ? " (was '" + _boundClass + "')" : "";
            _boundClass = cls;
            // Change-gated (Poll runs 5x/s), never per poll — identical cadence to the qwer rail.
            DeNelle.Core.Diagnostics.FlowTrace.Step("HudModel",
                "ability bar bound: bar=hotswap class='" + cls + "'" + wasClause + " source=" + clsSource +
                " hero='" + DeNelle.Core.State.HeroCanonNames.ForJob(cls) + "' ids=[" +
                string.Join(",", slots.ConvertAll(s => s.Name).ToArray()) + "] sig=" + sig);
            Model.Assignable.SetSlots(slots);
        }
    }

    // ── ConsumableHotbar (WO-609) ─────────────────────────────────────────────

    /// <summary>Pushes battle potion counts from the village larder.</summary>
    internal sealed class ConsumableHotbarProducer : HudProducer
    {
        private int _hp = int.MinValue, _mana = int.MinValue;
        // Cadence-smoothing choice: the base poll is tightened to 0.10s (was 0.50s) so the
        // radial cooldown sweep STEPS smoothly (~10 fps) instead of jumping in half-second
        // increments. Both the count push and the cooldown push are still change-gated, so
        // the tighter tick costs two dictionary reads and adds NO model churn while idle.
        private float _hpCd = -1f, _manaCd = -1f;

        public ConsumableHotbarProducer(IHudModel m) : base(m, 0.10f) { }

        protected override void Poll()
        {
            var inv = VillageInventory.Instance;
            int hp = inv != null ? inv.Get(HudCommands.HpPotionId) : 0;
            int mana = inv != null ? inv.Get(HudCommands.ManaPotionId) : 0;
            if (hp != _hp || mana != _mana)
            {
                _hp = hp;
                _mana = mana;
                Model.Consumables.Set(hp, mana);
            }

            // Enforced use-cooldown (owner directive): state lives in the SERVICE; the producer
            // only reads remaining/total and pushes it to the model for the belt tile's sweep.
            float hpCd = ConsumableUseService.CooldownRemaining(HudCommands.HpPotionId);
            float manaCd = ConsumableUseService.CooldownRemaining(HudCommands.ManaPotionId);
            if (hpCd != _hpCd || manaCd != _manaCd)
            {
                _hpCd = hpCd;
                _manaCd = manaCd;
                Model.Consumables.SetCooldown(
                    hpCd, ConsumableUseService.CooldownTotal(HudCommands.HpPotionId),
                    manaCd, ConsumableUseService.CooldownTotal(HudCommands.ManaPotionId));
            }
        }
    }

    // ── StatusEffects (WO-609 Phase 2) ────────────────────────────────────────

    /// <summary>Fills player + locked-target status rows from combat status trackers.</summary>
    internal sealed class StatusEffectsProducer : HudProducer
    {
        private const int MaxIcons = 6;
        private static readonly List<ActiveStatusSnapshot> Scratch = new List<ActiveStatusSnapshot>(8);
        private static readonly List<StatusIconRecord> Icons = new List<StatusIconRecord>(8);

        private HeroTargetIndicator _indicator;
        private string _playerSig;
        private string _targetSig;

        public StatusEffectsProducer(IHudModel m) : base(m, 0.20f) { }

        protected override void Poll()
        {
            PollPlayer();
            PollTarget();
        }

        private void PollPlayer()
        {
            Scratch.Clear();
            Icons.Clear();
            var status = HeroCombatStatus.Current;
            status?.CollectActive(Scratch, MaxIcons);
            for (int i = 0; i < Scratch.Count; i++)
                Icons.Add(ToRecord(Scratch[i]));

            string sig = BuildSig(Icons);
            if (sig == _playerSig) return;
            _playerSig = sig;
            Model.PlayerStatus.SetIcons(Icons);
        }

        private void PollTarget()
        {
            if (_indicator == null || !_indicator) _indicator = Object.FindAnyObjectByType<HeroTargetIndicator>();

            Scratch.Clear();
            Icons.Clear();
            IDamageable cur = _indicator != null ? _indicator.CurrentTarget : null;
            var curMb = cur as MonoBehaviour;
            var dmg = curMb != null ? curMb.GetComponentInParent<EnemyDamageable>() : null;

            if (dmg != null && cur != null && cur.IsAlive)
                dmg.CollectActive(Scratch, MaxIcons);

            for (int i = 0; i < Scratch.Count; i++)
                Icons.Add(ToRecord(Scratch[i]));

            string sig = BuildSig(Icons);
            if (sig == _targetSig) return;
            _targetSig = sig;
            Model.TargetStatus.SetIcons(Icons);
        }

        private static StatusIconRecord ToRecord(ActiveStatusSnapshot s)
            => new StatusIconRecord(s.Id, s.Label, s.Id, s.IsBuff, s.RemainingSeconds, s.TotalSeconds);

        private static string BuildSig(IReadOnlyList<StatusIconRecord> icons)
        {
            if (icons == null || icons.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < icons.Count; i++)
            {
                var ic = icons[i];
                sb.Append(ic.Id).Append(':').Append(Mathf.CeilToInt(ic.RemainingSeconds)).Append('|');
            }
            return sb.ToString();
        }
    }
}
