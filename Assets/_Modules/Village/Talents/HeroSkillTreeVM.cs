// =============================================================================
// HeroSkillTreeVM — the Knight skill-tree panel's PURE ViewModel (MVVM slice).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Talents
//
// ALL skill-tree STATE + LOGIC lives here, view-agnostic. Mirrors BuildingUpgradeVM:
//   * implements DeNelle.Core.UI.Mvvm.IPanelViewModel (Title / Changed / Close / Dispose)
//   * NO UnityEngine UI types (no GameObject/Image/Sprite/RectTransform/Color); the
//     View resolves all presentation. The VM is unit-testable without a scene
//     (ARCHITECTURE_PRINCIPLES §2 / §2c).
//   * the View binds it, re-renders on Changed, and routes user input back as
//     commands; the View NEVER reads game state (ui-mvvm-binding-seam rule).
//
// V1 = solo Knight (combat-pivot north star). The tree is the "knight" HeroTalentTreeDef
// (HeroTalentCatalog), laid out column-per-branch (Ranged / Heal-Sustain / Control),
// tier rows top-down. Owned/CanUnlock/LockReason come from WisdomCurrencyService +
// HeroTalentCatalog.CanUnlock. Unlock(nodeId) spends through WisdomCurrencyService.Unlock.
//
// NODE KIND (parallel data slice): HeroTalentNodeDef carries an optional `kind`
// (Skill | Stat) + `abilityId`. A Skill-kind node grants an equippable ability the
// loadout panel can slot; a Stat node is a passive stat bump. We read both off the
// def reflectively-free (the fields exist in the combined tree at gate time).
//
// WO-896 (2026-08-05) — PROGRESSION TRACKS. The View no longer draws an authored
// x/y node GRAPH; it draws TRACKS (a track = one ordered line of nodes connected by a
// progression line). This VM owns the track derivation, because a track is DATA, not
// presentation: it is a chain decomposition of the prerequisite graph over the VISIBLE
// nodes (Hidden already filtered), so hiding a node re-forms the tracks with no view
// change at all. Each node in a track carries a resolved SkillNodeState so the View
// only has to skin it (owned / planned / next / available / inert / locked).
//
// WO-910 (open, owner ruling pending) — DEAD NODES MUST NOT READ AS "NEXT". 31 of the
// ranger/mage nodes are player-reachable talents with no implemented consumer: the
// player can spend Wisdom and receive nothing. Presenting one of those as the shining
// "next step" would be the panel lying. TalentEffectLiveness (below) answers "does this
// node's effect actually do anything at runtime?"; a reachable node that answers NO
// resolves to SkillNodeState.Inert instead of Next, and says so in the detail strip.
// This is a PRESENTATION honesty fix only — it does not hide, block or re-price any
// node, so it stands whichever way the owner rules on hiding.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village.Talents
{
    /// <summary>Whether a skill-tree node grants an equippable ability or a passive stat.</summary>
    public enum SkillNodeKind { Skill, Stat }

    /// <summary>
    /// One skill-tree node's view-agnostic payload: identity + placement (branch column,
    /// tier row), prerequisite ids (for the View's parent->child lines), unlock state
    /// (Owned / CanUnlock / LockReason), Wisdom cost, kind, and whether the granted ability
    /// is currently equipped in a loadout slot. A readonly struct (no per-row allocation).
    /// </summary>
    public readonly struct SkillNodeVM
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int Tier;            // 1..4 (parsed from "tier1".."tier4"); 0 for shared pool nodes
        public readonly int Column;          // 0-based grid column == Slot-1 (v2: slot 1..5)
        public readonly string Branch;       // legacy branch label (unused by the v2 slot-grid view)
        public readonly IReadOnlyList<string> Prereqs;
        public readonly SkillNodeKind Kind;
        public readonly string AbilityId;    // non-empty for Skill-kind nodes; "" for Stat
        public readonly string IconPath;     // Resources sprite path (Talents/<hero>/<hero>_NN), may be ""
        public readonly bool IsCapstone;     // tier-4 capstone (distinct frame)
        public readonly bool IsShared;       // a Shared Universal pool node
        public readonly bool Owned;
        public readonly bool CanUnlock;
        public readonly string LockReason;   // why it's locked (shown instead of bare "LOCKED")
        public readonly int WisdomCost;
        public readonly bool IsEquipped;
        /// <summary>One-based quick-swap slot for an equipped active; 0 for passive/unassigned.</summary>
        public readonly int EquippedSlot;
        public readonly bool IsPending;      // staged in the current plan (not yet committed)
        public readonly float X;             // node-graph canvas position (0..1; -1 = unset/auto)
        public readonly float Y;             // 0=top, 1=bottom
        /// <summary>WO-896/WO-910: false when this node's effect has NO implemented runtime
        /// consumer — buying it grants nothing today. Such a node is never presented as the
        /// track's "next" step; the View marks it and the detail strip says so out loud.</summary>
        public readonly bool EffectLive;

        public SkillNodeVM(string id, string name, int tier, int column, string branch,
                           IReadOnlyList<string> prereqs, SkillNodeKind kind, string abilityId,
                           string iconPath, bool isCapstone, bool isShared,
                           bool owned, bool canUnlock, string lockReason, int wisdomCost, bool isEquipped,
                           int equippedSlot,
                           bool isPending, float x, float y, bool effectLive = true)
        {
            Id = id;
            Name = name;
            Tier = tier;
            Column = column;
            Branch = branch;
            Prereqs = prereqs ?? Array.Empty<string>();
            Kind = kind;
            AbilityId = abilityId ?? "";
            IconPath = iconPath ?? "";
            IsCapstone = isCapstone;
            IsShared = isShared;
            Owned = owned;
            CanUnlock = canUnlock;
            LockReason = lockReason ?? "";
            WisdomCost = wisdomCost;
            IsEquipped = isEquipped;
            EquippedSlot = equippedSlot;
            IsPending = isPending;
            X = x;
            Y = y;
            EffectLive = effectLive;
        }
    }

    /// <summary>
    /// How one node reads on the progression line. The View skins these WITHOUT relying on
    /// hue (the owner is red/green colourblind): fill, plate size, badge SHAPE, label prefix
    /// and connector weight carry every distinction — see the state matrix in
    /// HeroSkillTreePanelMvvm's header.
    /// </summary>
    public enum SkillNodeState
    {
        /// <summary>Already unlocked and paid for.</summary>
        Owned,
        /// <summary>Staged in the current plan, not yet committed.</summary>
        Planned,
        /// <summary>The ONE frontier step of an ordered track: reachable, affordable, and its
        /// effect has a real runtime consumer. At most one per ordered track.</summary>
        Next,
        /// <summary>Reachable + affordable + live, but not the track's frontier (a pool node,
        /// or a second live option on the same rung).</summary>
        Available,
        /// <summary>Reachable + affordable, but the effect has NO implemented consumer (WO-910):
        /// buying it grants nothing today. Never rendered as the "next" step.</summary>
        Inert,
        /// <summary>Not reachable yet (prerequisite / cost / capstone rule). LockReason says why.</summary>
        Locked
    }

    /// <summary>One node's seat on a progression track: the node, its resolved state, and
    /// whether the node to its LEFT on the track is its actual prerequisite (a real progression
    /// link) or merely the previous item on an unordered shelf.</summary>
    public readonly struct SkillTrackNodeVM
    {
        public readonly SkillNodeVM Node;
        public readonly SkillNodeState State;
        public readonly bool LinksToPrev;

        public SkillTrackNodeVM(SkillNodeVM node, SkillNodeState state, bool linksToPrev)
        {
            Node = node;
            State = state;
            LinksToPrev = linksToPrev;
        }
    }

    /// <summary>
    /// One PROGRESSION TRACK — an ordered line of nodes the View draws left-to-right with a
    /// connecting line (WO-896). An ORDERED track is a prerequisite chain (earlier -> later);
    /// an UNORDERED track is a free-pick pool (the Universal shelf), which the View draws with
    /// a dotted rail + a "no order" note so the line never implies a sequence that isn't real.
    /// </summary>
    public sealed class SkillTrackVM
    {
        /// <summary>Small caps tag above the title ("WAR PATH", "UNIVERSAL", ...). ASCII only.</summary>
        public string Kind { get; }
        /// <summary>The track's name — the root node's name for a chain. ASCII only.</summary>
        public string Title { get; }
        /// <summary>Optional qualifier ("after Thunderbolt", "no order - pick any",
        /// "prerequisite is hidden"). ASCII only, may be "".</summary>
        public string Note { get; }
        /// <summary>True when the line means "unlock order"; false for a free-pick pool.</summary>
        public bool Ordered { get; }
        public IReadOnlyList<SkillTrackNodeVM> Nodes { get; }

        public SkillTrackVM(string kind, string title, string note, bool ordered,
                            IReadOnlyList<SkillTrackNodeVM> nodes)
        {
            Kind = kind ?? "";
            Title = title ?? "";
            Note = note ?? "";
            Ordered = ordered;
            Nodes = nodes ?? Array.Empty<SkillTrackNodeVM>();
        }
    }

    /// <summary>
    /// WO-910 answer to "would buying this node actually DO anything?" — asked at RUNTIME, from
    /// the node's own data, so the progression line can never advertise a dead node as the next
    /// step to spend Wisdom on.
    ///
    /// THREE RULES, cheapest-proof first:
    ///   1. NOTE FLAG (the data's own confession) — an effect note carrying "V2" / "stub" /
    ///      "no consumer" / "hidden until" / "not wired" is the same belt check the EditMode
    ///      gate (TalentStrategyRegression G3) uses. Authored by whoever wrote the stub.
    ///   2. EFFECT KEY — the effect type (plus the modifyAbility stat discriminator) must be a
    ///      key some system actually reads. See the mirror note on ConsumedEffectKeys.
    ///   3. ABILITY PROOF — an unlockAbility / kind=skill node must name an ability that
    ///      resolves in AbilityCatalog, or the loadout has nothing to equip. This one is a
    ///      PROOF, not a list: it re-checks itself against the shipped catalog every run.
    ///
    /// FAILURE DIRECTION IS DELIBERATE: if a consumer is wired and rule 2's mirror is not
    /// updated, the node reads "no effect yet" — an under-claim the player can still buy. The
    /// panel never over-claims. (The reverse — a stale mirror promising an effect that does not
    /// exist — is the exact WO-910 defect and is what this class exists to prevent.)
    /// </summary>
    public static class TalentEffectLiveness
    {
        // Rule 1 — lowercase substrings that mark an authored-but-not-wired effect. These are
        // the tokens actually used in hero-talents.json today ("(V2)", "(NEW ability - stub)",
        // "NO rider consumer for a slow yet", "hidden until a crit mechanic exists").
        private static readonly string[] NotWiredNoteTokens =
        {
            "v2", "v-later", "stub", "no consumer", "no rider consumer",
            "not wired", "not implemented", "hidden until"
        };

        // Rule 2 — MIRROR of TalentConsumerRegistry.Implemented in
        // Assets/Editor/Regression/TalentStrategyRegression.cs. That registry is the AUTHORITY
        // (it carries the file+member citation the gate enforces); it lives in the Editor
        // assembly, which runtime code cannot reference, so the keys are mirrored here.
        // UPDATE RULE: wiring a consumer means adding the key in BOTH places in the same commit.
        // Drift is safe in one direction only (see the class summary) — never delete a key here
        // to make a node look alive.
        private static readonly HashSet<string> ConsumedEffectKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "damagereduction", "defense", "allstatspct", "maxhppct", "blockchance",
            "damagebonus", "cdreduction", "unlockability", "modifyability:heal",
            "reflect", "laststand", "invuln", "revive", "proc", "healthregen", "manaregen",
            "harvestrate", "collectorcap", "repaircost", "buildtime", "salvage", "wavereward",
            "towerdamage", "towerrange", "structuretoughness", "structuretoughnesswave",
            "towerattackspeed", "modifyability:poison", "modifyability:"
        };

        /// <summary>True when this node's effect is read by a real runtime system.
        /// <paramref name="why"/> carries the DIAGNOSTIC reason when it is NOT.
        ///
        /// ⛔ <paramref name="why"/> IS NOT PLAYER COPY. WO-1522 (device frame
        /// Logs/device/screens/owner-screen-20260906-202355.png): this out-string used to be
        /// concatenated straight into the learn dialog, so the player read
        /// "NO EFFECT YET - not implemented yet (data note: 'v2'). C" - an AUTHORING NOTE, quoting
        /// the token an author typed into hero-talents.json, truncated mid-sentence. The doc line
        /// here previously claimed it was "safe to show a player"; it never was. It names the data
        /// file's private vocabulary ("v2", "stub", "no consumer") and grows with it.
        /// Route it to FlowTrace only. The one player-facing sentence for a dead node is composed
        /// by the VM (see <c>DeadNodePlayerLine</c>) and says the WORD "COMING".</summary>
        public static bool HasRuntimeConsumer(HeroTalentNodeDef n, out string why)
        {
            why = "";
            if (n == null) { why = "no node"; return false; }

            var e = n.Effect;
            bool grantsAbility = !string.IsNullOrEmpty(n.AbilityId)
                                 || (e != null && string.Equals(e.Type, "unlockAbility", StringComparison.OrdinalIgnoreCase));

            if (e == null || string.IsNullOrEmpty(e.Type))
            {
                if (!grantsAbility) { why = "no effect is authored on this talent yet"; return false; }
            }

            // Rule 1 — the data's own not-yet-wired confession.
            if (e != null && NoteFlagsNotWired(e.Note, out string token))
            {
                why = "not implemented yet (data note: '" + token + "')";
                return false;
            }

            // Rule 2 — the effect key has to be one somebody reads.
            if (e != null && !string.IsNullOrEmpty(e.Type))
            {
                string key = EffectKey(e);
                if (!ConsumedEffectKeys.Contains(key))
                {
                    why = "nothing reads the '" + e.Type + "' effect yet";
                    return false;
                }
            }

            // Rule 3 — an ability-granting node must name an ability that EXISTS.
            if (grantsAbility)
            {
                string ids = !string.IsNullOrEmpty(n.AbilityId) ? n.AbilityId
                           : (e != null ? e.Ability : null);
                if (string.IsNullOrEmpty(ids))
                {
                    why = "it unlocks an ability but names none";
                    return false;
                }
                foreach (var raw in ids.Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    if (AbilityCatalog.FindById(id) != null) continue;
                    // A MISS is only meaningful while the catalog itself is up. If
                    // abilities.json failed to load, every id would miss and the whole tree
                    // would grey out on a data fault — so say so in the trace and DON'T
                    // slander the talents (fail the check open, never blank the screen).
                    if (!AbilityCatalogAlive())
                    {
                        FlowTrace.Warn("SkillTree", "AbilityCatalog looks EMPTY - cannot verify ability '" +
                                                    id + "'; treating talents as live rather than dead");
                        return true;
                    }
                    why = "the ability '" + id + "' does not exist yet";
                    return false;
                }
            }

            return true;
        }

        /// <summary>The registry key for an effect: the type, lowercased, with the modifyAbility
        /// stat discriminator appended ("modifyability:heal"). An UNSET stat is itself a
        /// discriminator ("modifyability:" = the taunt-burn rider), never a wildcard.</summary>
        private static string EffectKey(HeroTalentEffectDef e)
        {
            if (e == null || string.IsNullOrEmpty(e.Type)) return "";
            string type = e.Type.Trim().ToLowerInvariant();
            if (type != "modifyability") return type;
            return "modifyability:" + (e.Stat ?? "").Trim().ToLowerInvariant();
        }

        /// <summary>True when abilities.json actually loaded (any class has a Q/W/E/R loadout).
        /// Guards rule 3 against a data-load fault reading as "every talent is dead".</summary>
        private static bool AbilityCatalogAlive()
        {
            var knight = AbilityCatalog.GetLoadout("knight");
            if (knight != null && knight.Count > 0) return true;
            var mage = AbilityCatalog.GetLoadout("mage");
            return mage != null && mage.Count > 0;
        }

        private static bool NoteFlagsNotWired(string note, out string token)
        {
            token = "";
            if (string.IsNullOrEmpty(note)) return false;
            string lower = note.ToLowerInvariant();
            foreach (var t in NotWiredNoteTokens)
            {
                if (lower.IndexOf(t, StringComparison.Ordinal) >= 0) { token = t; return true; }
            }
            return false;
        }
    }

    /// <summary>
    /// Pure ViewModel for the LIVE hero's skill tree (see the ctor: the slug is resolved from
    /// GameState.HeroClass, never hardcoded). Exposes <see cref="Nodes"/> (one
    /// <see cref="SkillNodeVM"/> per authored node) + the wallet header (RemainingWisdom,
    /// RemainingSkillPoints) + the column/branch labels. Raises <see cref="Changed"/>
    /// after each unlock and on any WisdomCurrencyService change.
    /// </summary>
    public sealed class HeroSkillTreeVM : IPanelViewModel, IDisposable
    {
        // Hero slug = the LIVE class, resolved per-construction (fix 2026-08-16).
        // HISTORY / CORRECTION: this used to be `public const string HeroSlug = "knight"` with the
        // note "solo Knight north star — swap to a ctor arg when multi-hero lands". Multi-hero HAS
        // landed: HeroTalentModifiers.ForEachUnlocked reads the LIVE class and calls
        // HeroTalentCatalog.GetTree(slug), so a Ranger player browsed KNIGHT nodes, spent Wisdom on
        // them, and gained NOTHING (the applier never looked at "knight." ids). The constant is now
        // only the last-resort FALLBACK for "no GameState / no class chosen yet".
        private const string FallbackSlug = "knight";

        private readonly string _heroSlug;
        private readonly Action _onClose;
        private readonly Action _wisdomHandler;
        private bool _disposed;

        private readonly List<SkillNodeVM> _nodes = new List<SkillNodeVM>();
        private readonly List<SkillNodeVM> _shared = new List<SkillNodeVM>();
        // Ordered, de-duped branch column labels (index == SkillNodeVM.Column).
        private readonly List<string> _branches = new List<string>();
        // WO-896: the progression TRACKS the View draws (derived from the two lists above).
        private readonly List<SkillTrackVM> _tracks = new List<SkillTrackVM>();
        // Plan→CONFIRM: nodes staged this session but not yet committed/spent.
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);

        // Single-screen folds (owner 2026-06-28): the currently SELECTED node (drives the
        // detail/description panel) + a mirror of the player's QUICK-SWAP bar (slots 1..3)
        // so a player can read a perk AND assign an owned skill without a second screen.
        private string _selectedId = "";
        private readonly List<LoadoutSlotVM> _quickSlots = new List<LoadoutSlotVM>(AssignableSkillBar.SlotCount);
        private Action _barHandler;
        private AssignableSkillBar _barSub;

        public HeroSkillTreeVM(Action onClose, string heroSlug = null)
        {
            _heroSlug = string.IsNullOrWhiteSpace(heroSlug)
                ? ResolveLiveHeroSlug()
                : heroSlug.Trim().ToLowerInvariant();
            _onClose = onClose;

            RefundStrandedTrees();

            var svc = WisdomCurrencyService.Instance;
            if (svc != null)
            {
                _wisdomHandler = Raise;
                svc.Changed += _wisdomHandler;
            }
            SubscribeBar();

            Rebuild();
        }

        /// <summary>
        /// The LIVE hero class slug (the same source HeroTalentModifiers folds its stats from) —
        /// GameState.HeroClass via <c>HeroTalentClassReader.Slug()</c>, falling back to
        /// <see cref="FallbackSlug"/> only when no class has been chosen. NEVER a constant: a
        /// constant here is exactly the bug this method exists to make impossible.
        /// </summary>
        private static string ResolveLiveHeroSlug()
        {
            string slug = Guard.Try("SkillTree", "resolve live hero slug",
                () => HeroTalentClassReader.Slug(), FallbackSlug);
            if (string.IsNullOrWhiteSpace(slug)) slug = FallbackSlug;
            slug = slug.Trim().ToLowerInvariant();
            FlowTrace.Step("SkillTree", "hero slug resolved LIVE = '" + slug + "'");
            return slug;
        }

        /// <summary>
        /// One-shot honesty pass: Wisdom banked on a tree the player can no longer SEE is
        /// unreachable and unrespeccable (the respec button only ever targets the visible tree).
        /// That state is reachable for anyone who spent points while the panel was hardcoded to
        /// "knight". Any unlocked node belonging to a hero tree other than the live one is refunded
        /// at FULL cost, FREE (no crystal charge — the player never chose to make this trade).
        /// </summary>
        private void RefundStrandedTrees()
        {
            Guard.Try("SkillTree", "refund stranded talent trees", () =>
            {
                var svc = WisdomCurrencyService.Instance;
                if (svc == null) return;
                var owned = svc.Unlocked;
                if (owned == null || owned.Count == 0) return;

                foreach (var tree in HeroTalentCatalog.AllTrees)
                {
                    string slug = tree?.HeroSlug;
                    if (string.IsNullOrWhiteSpace(slug)) continue;
                    slug = slug.Trim().ToLowerInvariant();
                    if (slug == _heroSlug) continue;

                    string prefix = slug + ".";
                    bool stranded = false;
                    foreach (var id in owned)
                    {
                        if (id != null && id.StartsWith(prefix, StringComparison.Ordinal))
                        { stranded = true; break; }
                    }
                    if (!stranded) continue;

                    FlowTrace.Warn("SkillTree",
                        "stranded talents on '" + slug + "' while playing '" + _heroSlug +
                        "' - refunding that tree in full, free of charge.");
                    svc.RespecHero(slug);
                    RespecStatus = "Talents spent on another class were refunded.";
                }
            });
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        public string Title { get; private set; } = HudStrings.Get(HudStrings.KeyHeroSkills);

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var svc = WisdomCurrencyService.Instance;
            if (svc != null && _wisdomHandler != null) svc.Changed -= _wisdomHandler;
            UnsubscribeBar();
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>Every hero-tree node (slot column + tier row carried on each). Never null.</summary>
        public IReadOnlyList<SkillNodeVM> Nodes => _nodes;

        /// <summary>The 8 Shared Universal pool nodes (v2 strip). Never null.</summary>
        public IReadOnlyList<SkillNodeVM> Shared => _shared;

        /// <summary>Branch column display names, left-to-right (index == node.Column). Never null.</summary>
        public IReadOnlyList<string> Branches => _branches;

        /// <summary>WO-896: the progression TRACKS the panel draws — each an ordered line of
        /// nodes with a resolved state per seat. Hero chains first (reading order), then the
        /// unordered Universal pool. Never null.</summary>
        public IReadOnlyList<SkillTrackVM> Tracks => _tracks;

        /// <summary>Current Wisdom balance (the talent-unlock currency).</summary>
        public int RemainingWisdom
        {
            get { var svc = WisdomCurrencyService.Instance; return svc != null ? svc.Wisdom : 0; }
        }

        /// <summary>The next guaranteed Wisdom grant. HeroProgression grants Wisdom at every level.</summary>
        public int NextWisdomLevel
        {
            get
            {
                var progression = HeroProgression.Instance;
                return Math.Max(2, (progression != null ? progression.Level : 1) + 1);
            }
        }

        /// <summary>Unspent hero skill points (display companion to Wisdom).</summary>
        public int RemainingSkillPoints
        {
            get
            {
                var sk = DeNelle.Core.Progression.SkillSystem.Instance;
                return sk != null ? sk.AvailablePoints : 0;
            }
        }

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Unlock a node IMMEDIATELY (legacy path): spends Wisdom + validates via the service.
        /// The node-graph View uses the plan→CONFIRM flow (Stage/Commit) instead.</summary>
        public void Unlock(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            var svc = WisdomCurrencyService.Instance;
            if (svc != null) svc.Unlock(nodeId);
            _pending.Remove(nodeId);
            Rebuild();
            Raise();
        }

        // ── Plan → CONFIRM flow (node-graph) ─────────────────────────────────────

        /// <summary>Total Wisdom the current staged plan would spend.</summary>
        public int PendingCost
        {
            get
            {
                int sum = 0;
                foreach (var id in _pending)
                {
                    var n = HeroTalentCatalog.FindNode(id);
                    if (n != null) sum += n.Cost;
                }
                return sum;
            }
        }

        /// <summary>Count of nodes staged in the current plan.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>True when there is a non-empty, affordable plan to commit.</summary>
        public bool CanCommit
        {
            get
            {
                var svc = WisdomCurrencyService.Instance;
                int wisdom = svc != null ? svc.Wisdom : 0;
                return _pending.Count > 0 && PendingCost <= wisdom;
            }
        }

        /// <summary>Read-only: true when the selected owned active skill is already in a Loadout slot.</summary>
        public bool SelectedAlreadyOnBar => AssignableSkillBarAccess.SlotOf(SelectedAssignAbilityId) >= 0;
        /// <summary>Stage a node into the plan if reachable + affordable within the remaining budget
        /// (treats already-staged nodes as tentatively owned so a chain can be planned in one pass).</summary>
        public void Stage(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || _pending.Contains(nodeId)) return;
            var svc = WisdomCurrencyService.Instance;
            if (svc == null) return;
            var node = HeroTalentCatalog.FindNode(nodeId);
            if (node == null) return;

            var owned = BuildUnlockedSet(svc);
            if (owned.Contains(nodeId)) return;                 // already owned
            var effective = Effective(owned);                   // owned ∪ pending
            int budget = svc.Wisdom - PendingCost;              // Wisdom left after the staged plan
            if (!HeroTalentCatalog.CanUnlock(nodeId, budget, effective)) return;
            if (node.Cost > budget) return;

            _pending.Add(nodeId);
            Rebuild();
            Raise();
        }

        /// <summary>Remove a node from the plan, and cascade-drop any staged node that depended on it.</summary>
        public void Unstage(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || !_pending.Remove(nodeId)) return;
            PrunePending();
            Rebuild();
            Raise();
        }

        /// <summary>Commit the staged plan: unlock every pending node (tier order) via the service, then clear.</summary>
        public void Commit()
        {
            var svc = WisdomCurrencyService.Instance;
            if (svc == null || _pending.Count == 0) return;

            // Dependency-safe order: shared (no prereqs) + by tier ascending, so a parent
            // is always committed before its child.
            var ordered = new List<string>(_pending);
            ordered.Sort((a, b) => TierOf(a).CompareTo(TierOf(b)));
            FlowTrace.Step("SkillTree", "commit plan: " + ordered.Count + " node(s), -" + PendingCost + " wisdom");
            foreach (var id in ordered) svc.Unlock(id);   // each re-validates prereq+cost+capstone

            _pending.Clear();
            Rebuild();
            Raise();
        }

        /// <summary>Discard the staged plan without spending.</summary>
        public void CancelPlan()
        {
            if (_pending.Count == 0) return;
            _pending.Clear();
            Rebuild();
            Raise();
        }

        // ── Respec (refund spent Wisdom for a Crystal cost) ──────────────────────
        // Mirrors the respec path of the legacy TalentTreePanel (a UI-Toolkit screen that was
        // never wired to any button and was DELETED on 2026-09-06, WO-1430 - do not go looking
        // for TalentTreePanel.OnRespecClicked) so the LIVE MVVM panel
        // surfaces the same in-game respec (owner F8: "no respec option"). Spends
        // HeroTalentCatalog.RespecCostCrystals via EconomyService (Crystals-only), then
        // WisdomCurrencyService.RespecHero wipes this hero's unlocked nodes + refunds the
        // Wisdom. Refreshes the panel so freed nodes + the refunded balance show at once.

        /// <summary>The Crystal cost of a full respec (HeroTalentCatalog.RespecCostCrystals).</summary>
        public int RespecCost => HeroTalentCatalog.RespecCostCrystals;

        /// <summary>True when a respec can be afforded right now (enough Crystals in the wallet).</summary>
        public bool CanRespec
        {
            get
            {
                var econ = EconomyService.Instance;
                return econ != null && econ.CanAfford(ResourceCost.CrystalsOnly(RespecCost));
            }
        }

        /// <summary>Last respec action result (shown on the panel's status line).</summary>
        public string RespecStatus { get; private set; } = "";

        /// <summary>
        /// Respec this hero: pay the Crystal cost, then wipe + refund the hero's talents.
        /// Discards any staged plan first (a respec invalidates it). No-op (with a status
        /// hint) when the wallet can't cover the cost. Refreshes the panel on success.
        /// </summary>
        public void Respec()
        {
            if (string.IsNullOrEmpty(_heroSlug)) return;

            var econ = EconomyService.Instance;
            var cost = ResourceCost.CrystalsOnly(RespecCost);
            if (econ == null || !econ.TrySpend(cost))
            {
                RespecStatus = "Need " + RespecCost + " Crystals to respec.";
                FlowTrace.Warn("SkillTree", "respec REJECTED — need " + RespecCost + " crystals");
                Raise();
                return;
            }

            _pending.Clear();
            WisdomCurrencyService.Instance?.RespecHero(_heroSlug);
            RespecStatus = "Respec complete - talents refunded.";
            FlowTrace.Step("SkillTree", "respec '" + _heroSlug + "' done (-" + RespecCost + " crystals)");
            Rebuild();
            Raise();
        }

        // owned ∪ pending — the "tentatively owned" set used for staging validation + node state.
        private HashSet<string> Effective(HashSet<string> owned)
        {
            var set = new HashSet<string>(owned, StringComparer.Ordinal);
            foreach (var id in _pending) set.Add(id);
            return set;
        }

        // Drop staged nodes whose prerequisites are no longer satisfied by owned ∪ remaining-pending
        // (iterate to a fixpoint so a whole staged chain collapses when its root is unstaged).
        private void PrunePending()
        {
            var svc = WisdomCurrencyService.Instance;
            if (svc == null) { _pending.Clear(); return; }
            var owned = BuildUnlockedSet(svc);
            bool changed = true;
            while (changed)
            {
                changed = false;
                var effective = Effective(owned);
                foreach (var id in new List<string>(_pending))
                {
                    var n = HeroTalentCatalog.FindNode(id);
                    if (n == null) { _pending.Remove(id); changed = true; continue; }
                    if (n.Prerequisites == null) continue;
                    foreach (var pr in n.Prerequisites)
                    {
                        if (string.IsNullOrEmpty(pr)) continue;
                        if (!effective.Contains(pr)) { _pending.Remove(id); changed = true; break; }
                    }
                }
            }
        }

        private static int TierOf(string nodeId)
        {
            var n = HeroTalentCatalog.FindNode(nodeId);
            return n != null ? TierIndex(n.Tier) : 0;   // shared (tier "shared") -> 1 via default
        }

        // ── Build the node rows (no Unity types) ─────────────────────────────────

        private void Rebuild()
        {
            _nodes.Clear();
            _shared.Clear();
            _branches.Clear();

            var tree = HeroTalentCatalog.GetTree(_heroSlug);
            // WO-896 F8 overcrowding: the Obsidian demo chrome is "TALENT TREE", not a
            // packed "Grom (Knight) Skills" header. Hero name stays on the detail strip.
            Title = HudStrings.Get(HudStrings.KeyHeroSkills);

            var svc = WisdomCurrencyService.Instance;
            int wisdom = svc != null ? svc.Wisdom : 0;
            var owned = BuildUnlockedSet(svc);
            _pending.RemoveWhere(owned.Contains);     // drop anything committed/owned externally
            var effective = Effective(owned);          // owned ∪ pending = tentatively owned
            int budget = wisdom - PendingCost;          // Wisdom left after the staged plan

            // ── Hero tree: v2 slot-grid (column == slot-1, row == tier 1..4). ───────
            if (tree != null && tree.Nodes != null)
            {
                foreach (var n in tree.Nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                    if (n.Hidden) continue;   // WO-910: the wire-or-hide law's hide half (see below)
                    // Column from the explicit v2 slot (1..5); fall back to legacy branch column.
                    int col = n.Slot > 0 ? n.Slot - 1 : LegacyColumn(n);
                    _nodes.Add(BuildNode(n, col, isShared: false, budget, owned, effective));
                }
            }

            // ── Shared Universal pool (8 free-standing nodes any hero may draw). ────
            var shared = HeroTalentCatalog.SharedNodes;
            if (shared != null)
            {
                for (int i = 0; i < shared.Count; i++)
                {
                    var n = shared[i];
                    if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                    if (n.Hidden) continue;   // WO-910: same hide rule as the hero tree above
                    int col = n.Slot > 0 ? n.Slot - 1 : i;
                    _shared.Add(BuildNode(n, col, isShared: true, budget, owned, effective));
                }
            }

            // WO-910 — WHY the two `n.Hidden` skips above exist, and what they do NOT do.
            // HeroTalentNodeDef.Hidden shipped on 2026-07-11 with a comment claiming "the View
            // skips it", but NO code ever read it: a node marked "hidden": true in
            // hero-talents.json silenced the EditMode no-dead-nodes gate while staying fully
            // clickable in this panel. The owner's wire-or-hide law only has two legal moves if
            // BOTH work, so the reader lives here now — this VM is the one place the tree's node
            // list is projected for the View, so one skip per collection covers every surface.
            //
            // DELIBERATELY NOT DONE HERE: hiding a node does NOT rewrite the prerequisite graph.
            // A visible node whose only prerequisite is hidden keeps that prerequisite and reads
            // "Requires <hidden node>" forever (LockReasonFor below), i.e. it is stranded. Auto-
            // repointing prerequisites past a hidden node would be a silent DESIGN change to the
            // tree's shape, which is the owner's call — so the honest failure mode (a visibly
            // unreachable node) is preserved instead. Verify downstream reachability BEFORE
            // marking anything hidden; see WORK_ORDER_910_ranger_mage_talent_consumers.md.
            // No node currently sets hidden, so today this is a no-op on live behaviour.

            RebuildTracks();
            BuildQuickSlots();
        }

        // ── WO-896: PROGRESSION TRACKS (the data behind the connected line) ──────
        // A track is a CHAIN in the prerequisite graph over the VISIBLE nodes, walked
        // left-to-right = earlier-to-later. Chain decomposition, in one pass over the nodes
        // sorted by (tier, column, id): a node extends the chain whose TAIL is one of its
        // prerequisites; otherwise it starts a new track. A tier-1 root therefore opens a
        // track and each following tier extends it, while a second child of the same parent
        // (e.g. Venombrand off Thunderbolt) opens its own short track labelled "after <parent>".
        //
        // WHY IN THE VM: hiding a node (the WO-910 ruling the owner still owes) changes only
        // _nodes/_shared; the tracks then RE-FORM from whatever is left, and a survivor whose
        // only prerequisite was hidden opens its own track carrying an explicit
        // "prerequisite is hidden" note instead of silently looking like a fresh start.

        private void RebuildTracks()
        {
            _tracks.Clear();
            Guard.Try("SkillTree", "build progression tracks", () =>
            {
                BuildHeroTracks();
                BuildSharedTrack();
            });
            LogTrackCensus();
        }

        private void BuildHeroTracks()
        {
            var byId = new Dictionary<string, SkillNodeVM>(StringComparer.Ordinal);
            foreach (var n in _nodes)
                if (!string.IsNullOrEmpty(n.Id)) byId[n.Id] = n;

            var ordered = new List<SkillNodeVM>(_nodes);
            ordered.Sort(CompareForTrack);

            var chains = new List<List<SkillNodeVM>>();
            var links = new List<List<bool>>();
            var notes = new List<string>();
            var tails = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var n in ordered)
            {
                if (string.IsNullOrEmpty(n.Id)) continue;

                string tailParent = null, visibleParent = null;
                bool hasPrereq = false;
                if (n.Prereqs != null)
                {
                    foreach (var pr in n.Prereqs)
                    {
                        if (string.IsNullOrEmpty(pr)) continue;
                        hasPrereq = true;
                        if (!byId.ContainsKey(pr)) continue;          // prerequisite is hidden
                        if (visibleParent == null) visibleParent = pr;
                        if (tails.ContainsKey(pr)) { tailParent = pr; break; }
                    }
                }

                if (tailParent != null && tails.TryGetValue(tailParent, out int chain))
                {
                    tails.Remove(tailParent);
                    chains[chain].Add(n);
                    links[chain].Add(true);
                    tails[n.Id] = chain;
                    continue;
                }

                chains.Add(new List<SkillNodeVM> { n });
                links.Add(new List<bool> { false });
                notes.Add(visibleParent != null ? "after " + NameOf(visibleParent, byId)
                        : hasPrereq ? "prerequisite is hidden"
                        : "");
                tails[n.Id] = chains.Count - 1;
            }

            for (int i = 0; i < chains.Count; i++)
            {
                var root = chains[i][0];
                _tracks.Add(new SkillTrackVM(
                    BranchTagOf(root.Id),
                    string.IsNullOrEmpty(root.Name) ? root.Id : root.Name,
                    notes[i],
                    ordered: true,
                    nodes: ResolveStates(chains[i], links[i], ordered: true)));
            }
        }

        // The Universal pool is NOT a chain: every node is free-standing, so a solid line
        // between them would invent an unlock order that does not exist. It ships as ONE
        // unordered track (the View draws a dotted rail + the "no order" note).
        private void BuildSharedTrack()
        {
            if (_shared.Count == 0) return;
            var seats = new List<SkillNodeVM>(_shared);
            var links = new List<bool>(seats.Count);
            for (int i = 0; i < seats.Count; i++) links.Add(false);
            _tracks.Add(new SkillTrackVM("UNIVERSAL", "Any class", "no order - pick any",
                ordered: false, nodes: ResolveStates(seats, links, ordered: false)));
        }

        // Resolve each seat's state. On an ORDERED track exactly ONE node may be Next: the
        // first reachable node whose effect actually does something (WO-910). A reachable node
        // with no consumer resolves Inert and therefore never takes the Next seat, so the
        // panel's focus can never point at a talent that grants nothing.
        private static IReadOnlyList<SkillTrackNodeVM> ResolveStates(
            List<SkillNodeVM> seats, List<bool> links, bool ordered)
        {
            var outSeats = new List<SkillTrackNodeVM>(seats.Count);
            bool nextTaken = false;
            for (int i = 0; i < seats.Count; i++)
            {
                var n = seats[i];
                SkillNodeState state;
                if (n.Owned) state = SkillNodeState.Owned;
                else if (n.IsPending) state = SkillNodeState.Planned;
                else if (!n.CanUnlock) state = SkillNodeState.Locked;
                else if (!n.EffectLive) state = SkillNodeState.Inert;
                else if (ordered && !nextTaken) { state = SkillNodeState.Next; nextTaken = true; }
                else state = SkillNodeState.Available;
                outSeats.Add(new SkillTrackNodeVM(n, state, i > 0 && links[i]));
            }
            return outSeats;
        }

        // Deterministic reading order: tier row first, then the authored slot column, then id.
        private static int CompareForTrack(SkillNodeVM a, SkillNodeVM b)
        {
            int c = a.Tier.CompareTo(b.Tier);
            if (c != 0) return c;
            c = a.Column.CompareTo(b.Column);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static string NameOf(string id, Dictionary<string, SkillNodeVM> byId)
        {
            if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n.Name))
                return n.Name;
            return id ?? "";
        }

        // WO-676 strategic branch, straight off the def ("war" | "steward" | "bulwark";
        // absent = war). NOT SkillNodeVM.Branch — that field carries the legacy v1 a/b/c
        // column mapping ("Ranged"/"Heal-Sustain"/"Control"), which v2 nodes do not set.
        private static string BranchTagOf(string nodeId)
        {
            var def = HeroTalentCatalog.FindNode(nodeId);
            string b = def != null ? (ReadStringField(def, "Branch") ?? "") : "";
            switch (b.Trim().ToLowerInvariant())
            {
                case "steward": return "STEWARD PATH";
                case "bulwark": return "BULWARK PATH";
                default: return "WAR PATH";
            }
        }

        // §12 instrumentation: one line per CHANGE of the census (never per Raise), so the F8
        // break-log shows how many nodes resolved to each state and how many are inert.
        private string _lastCensus;
        private void LogTrackCensus()
        {
            int owned = 0, planned = 0, next = 0, avail = 0, inert = 0, locked = 0, nodes = 0;
            foreach (var t in _tracks)
            {
                if (t == null || t.Nodes == null) continue;
                foreach (var s in t.Nodes)
                {
                    nodes++;
                    switch (s.State)
                    {
                        case SkillNodeState.Owned: owned++; break;
                        case SkillNodeState.Planned: planned++; break;
                        case SkillNodeState.Next: next++; break;
                        case SkillNodeState.Available: avail++; break;
                        case SkillNodeState.Inert: inert++; break;
                        default: locked++; break;
                    }
                }
            }
            string census = "tracks=" + _tracks.Count + " nodes=" + nodes + " owned=" + owned
                          + " planned=" + planned + " next=" + next + " available=" + avail
                          + " inert=" + inert + " locked=" + locked;
            if (census == _lastCensus) return;
            _lastCensus = census;
            FlowTrace.Step("SkillTree", "progression line rebuilt: " + census);
            if (nodes == 0)
                FlowTrace.Warn("SkillTree", "progression line has NO nodes - hero '" + _heroSlug +
                                            "' has no visible talents (catalog empty or all hidden?)");
        }

        private SkillNodeVM BuildNode(HeroTalentNodeDef n, int col, bool isShared, int budget,
                                      HashSet<string> owned, HashSet<string> effective)
        {
            bool isOwned = owned.Contains(n.Id);
            bool isPending = _pending.Contains(n.Id);
            // "CanUnlock" for the View = can be STAGED now: not owned/pending, reachable
            // (prereqs in owned∪pending), capstone-legal, affordable within the remaining budget.
            bool canStage = !isOwned && !isPending
                            && HeroTalentCatalog.CanUnlock(n.Id, budget, effective)
                            && n.Cost <= budget;
            string reason = (isOwned || isPending) ? "" : LockReasonFor(n, budget, effective);

            SkillNodeKind kind = KindOf(n);
            string abilityId = AbilityIdOf(n);
            bool equipped = kind == SkillNodeKind.Skill
                            && !string.IsNullOrEmpty(abilityId)
                            && AssignableSkillBarAccess.IsAssigned(abilityId);
            int equippedSlot = equipped ? AssignableSkillBarAccess.SlotOf(abilityId) + 1 : 0;

            int tier = isShared ? 0 : TierIndex(n.Tier);

            // WO-910: does buying this node actually DO anything at runtime? A "no" never hides
            // or blocks the node (that ruling is the owner's) — it only stops the View selling
            // it as the next step, and is logged ONCE per node id so the gap is visible in F8.
            bool effectLive = TalentEffectLiveness.HasRuntimeConsumer(n, out string deadWhy);
            if (!effectLive)
                FlowTrace.Once("SkillTree", "deadnode:" + n.Id,
                    "node '" + n.Id + "' (" + (n.Name ?? "?") + ") has NO runtime consumer - " + deadWhy +
                    " (WO-910); it will never be shown as the next step");

            return new SkillNodeVM(
                n.Id,
                string.IsNullOrEmpty(n.Name) ? n.Id : n.Name,
                tier,
                col,
                isShared ? "Shared" : BranchLabel(BranchKey(n)),
                n.Prerequisites != null ? new List<string>(n.Prerequisites) : null,
                kind,
                abilityId,
                n.IconPath,
                !isShared && tier >= 4,   // capstone
                isShared,
                isOwned,
                canStage,
                reason,
                n.Cost,
                equipped,
                equippedSlot,
                isPending,
                n.X,
                n.Y,
                effectLive);
        }

        // Legacy fallback when a node has no v2 slot: derive a column from its branch.
        private int LegacyColumn(HeroTalentNodeDef n)
        {
            string branch = BranchKey(n);
            int idx = _branches.IndexOf(BranchLabel(branch));
            if (idx >= 0) return idx;
            _branches.Add(BranchLabel(branch));
            return _branches.Count - 1;
        }

        // ── Lock-reason (the specific "why", not bare LOCKED) ─────────────────────

        private static string LockReasonFor(HeroTalentNodeDef n, int wisdom, HashSet<string> unlocked)
        {
            if (n == null) return "Locked";
            // v2 capstone exclusivity — once one Tier-4 is taken, the others read this
            // (dominant reason so the panel dims every other capstone clearly). A hero
            // respec clears the unlocked set and re-frees the choice.
            if (HeroTalentCatalog.IsCapstone(n) && HeroTalentCatalog.AnotherCapstoneUnlocked(n.Id, unlocked))
                return "One capstone per hero";
            // Prereq gate first (the structural blocker).
            if (n.Prerequisites != null)
            {
                foreach (var pr in n.Prerequisites)
                {
                    if (string.IsNullOrEmpty(pr)) continue;
                    if (unlocked == null || !unlocked.Contains(pr))
                    {
                        var prNode = HeroTalentCatalog.FindNode(pr);
                        string prName = prNode != null && !string.IsNullOrEmpty(prNode.Name) ? prNode.Name : pr;
                        return "Requires " + prName;
                    }
                }
            }
            // Then affordability.
            if (wisdom < n.Cost)
                return "Needs " + n.Cost + " Wisdom (have " + wisdom + ")";
            return "Locked";
        }

        // ── Node kind / ability id readers (data slice fields; safe if absent) ────
        // The parallel data slice adds `kind` ("skill"|"stat") + `abilityId` to
        // HeroTalentNodeDef. Until both ship in the combined tree these readers degrade
        // gracefully: a node with no abilityId is treated as a Stat node.

        private static SkillNodeKind KindOf(HeroTalentNodeDef n)
        {
            if (n == null) return SkillNodeKind.Stat;
            string k = ReadStringField(n, "Kind");
            if (!string.IsNullOrEmpty(k))
                return k.Trim().ToLowerInvariant() == "skill" ? SkillNodeKind.Skill : SkillNodeKind.Stat;
            // No explicit kind — a node that names an ability is a Skill, else a Stat.
            return string.IsNullOrEmpty(AbilityIdOf(n)) ? SkillNodeKind.Stat : SkillNodeKind.Skill;
        }

        private static string AbilityIdOf(HeroTalentNodeDef n)
        {
            if (n == null) return "";
            return ReadStringField(n, "AbilityId") ?? "";
        }

        // Field/property read tolerant of the data slice landing slightly differently
        // (field vs property, present or not) — no NRE, no hard compile coupling.
        private static string ReadStringField(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var f = t.GetField(name);
            if (f != null) return f.GetValue(obj) as string;
            var p = t.GetProperty(name);
            if (p != null) return p.GetValue(obj, null) as string;
            return null;
        }

        // ── Branch derivation (column-per-branch) ────────────────────────────────
        // The catalog node carries an optional `branch` ("ranged"/"heal"/"control").
        // Until that ships we fall back to the node's Column letter so the panel still
        // lays out column-per-branch (a/b/c -> Ranged/Heal-Sustain/Control).

        private static string BranchKey(HeroTalentNodeDef n)
        {
            string b = ReadStringField(n, "Branch");
            if (!string.IsNullOrEmpty(b)) return b.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(n?.Column) ? "a" : n.Column.Trim().ToLowerInvariant();
        }

        private static string BranchLabel(string key)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "ranged": case "a": return "Ranged";
                case "heal": case "heal-sustain": case "sustain": case "b": return "Heal-Sustain";
                case "control": case "c": return "Control";
                default: return char.ToUpper(key[0]) + (key.Length > 1 ? key.Substring(1) : "");
            }
        }

        private static int TierIndex(string tier)
        {
            switch ((tier ?? "tier1").Trim().ToLowerInvariant())
            {
                case "tier4": return 4;
                case "tier3": return 3;
                case "tier2": return 2;
                default: return 1;
            }
        }

        private static HashSet<string> BuildUnlockedSet(WisdomCurrencyService svc)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (svc != null && svc.Unlocked != null)
                foreach (var id in svc.Unlocked)
                    if (!string.IsNullOrEmpty(id)) set.Add(id);
            return set;
        }

        // ── Selection + detail panel (read what a perk does BEFORE confirming) ───
        // The View calls Select(id) on every node tap. Selection drives the detail
        // strip (name + description). For an ACTIONABLE node we also fold the plan
        // toggle in here (stage/unstage), so one tap both reads AND plans it; a locked
        // node just updates the detail so the player can read why it's gated.

        /// <summary>
        /// Select a node for the spend popup (name / desc / cost). Does NOT stage or spend —
        /// owner 2026-08-15: the tree is graph-only; spend happens only from the popup Confirm.
        /// </summary>
        public void Select(string nodeId)
        {
            _selectedId = nodeId ?? "";
            FlowTrace.Step("SkillTree", "select node " + _selectedId + " (popup, no stage)");
            Raise();
        }

        /// <summary>Dismiss the spend popup without spending.</summary>
        public void ClearSelection()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            FlowTrace.Step("SkillTree", "clear selection (popup dismiss)");
            _selectedId = "";
            Raise();
        }

        /// <summary>True when the selected node can be bought with current Wisdom right now.</summary>
        public bool CanSpendSelected
        {
            get
            {
                var n = HeroTalentCatalog.FindNode(_selectedId);
                if (n == null) return false;
                var svc = WisdomCurrencyService.Instance;
                var owned = BuildUnlockedSet(svc);
                if (owned.Contains(n.Id)) return false;
                int wisdom = svc != null ? svc.Wisdom : 0;
                return HeroTalentCatalog.CanUnlock(n.Id, wisdom, owned) && n.Cost <= wisdom;
            }
        }

        /// <summary>Wisdom cost of the selected node (0 when none).</summary>
        public int SelectedWisdomCost
        {
            get
            {
                var n = HeroTalentCatalog.FindNode(_selectedId);
                return n != null ? n.Cost : 0;
            }
        }

        /// <summary>
        /// Popup prompt line. Buyable: "Spend N Wisdom for &lt;name&gt;?".
        /// Owned / locked: the honest state line (no fake spend affordance).
        /// </summary>
        public string SelectedSpendPrompt
        {
            get
            {
                if (!HasSelection) return "";
                if (CanSpendSelected)
                    return "Spend " + SelectedWisdomCost + " Wisdom for " + SelectedNodeName + "?";
                return SelectedNodeStateLine;
            }
        }

        /// <summary>Popup Confirm: spend Wisdom on the selected node immediately. Active skills stay
        /// selected so the same popup advances to its explicit quick-swap assignment step; passives
        /// dismiss because they are always active and have no slot action.</summary>
        public void SpendSelected()
        {
            if (!CanSpendSelected)
            {
                FlowTrace.Warn("SkillTree", "SpendSelected rejected for '" + _selectedId + "'");
                return;
            }
            string id = _selectedId;
            FlowTrace.Step("SkillTree", "popup spend '" + id + "' cost=" + SelectedWisdomCost);
            Unlock(id);
            _selectedId = "";
            Raise();
        }

        /// <summary>True when a real node is selected (the detail strip has content).</summary>
        public bool HasSelection => HeroTalentCatalog.FindNode(_selectedId) != null;

        /// <summary>Id of the selected node ("" when none). The View uses this for the
        /// focus plate so the selected seat always matches the detail strip.</summary>
        public string SelectedNodeId => _selectedId ?? "";

        /// <summary>Display name of the selected node, or "" when none.</summary>
        public string SelectedNodeName
        {
            get { var n = HeroTalentCatalog.FindNode(_selectedId); return n == null ? "" : (string.IsNullOrEmpty(n.Name) ? n.Id : n.Name); }
        }

        /// <summary>What the selected perk does — the node's authored description, with a
        /// graceful fallback to the unlocked ability / effect summary when none is authored.</summary>
        public string SelectedNodeDescription
        {
            get
            {
                var n = HeroTalentCatalog.FindNode(_selectedId);
                if (n == null) return "";
                return string.IsNullOrEmpty(n.Description) ? DescribeFallback(n) : n.Description;
            }
        }

        /// <summary>Owned / planned / cost / lock-reason line for the selected node.</summary>
        public string SelectedNodeStateLine
        {
            get
            {
                var n = HeroTalentCatalog.FindNode(_selectedId);
                if (n == null) return "";
                var svc = WisdomCurrencyService.Instance;
                var owned = BuildUnlockedSet(svc);
                // WO-910 — say it OUT LOUD before the player spends: a talent with no
                // implemented consumer grants nothing. This line replaces the usual
                // cost/lock line for an unowned dead node, so the panel can never imply
                // otherwise; an owned one still explains itself below.
                if (!owned.Contains(n.Id) && !TalentEffectLiveness.HasRuntimeConsumer(n, out string deadWhy))
                    return DeadNodePlayerLine(n, deadWhy);
                if (owned.Contains(n.Id))
                {
                    // Owner 2026-08-15: loadout left this screen; owned just explains itself.
                    if (SelectedIsAssignable)
                        return "Owned - Active skill";
                    return "Owned - Passive - always active";
                }
                if (_pending.Contains(n.Id)) return "Planned  -  -" + n.Cost + " Wisdom";
                int budget = (svc != null ? svc.Wisdom : 0) - PendingCost;
                var effective = Effective(owned);
                if (HeroTalentCatalog.CanUnlock(n.Id, budget, effective) && n.Cost <= budget)
                    return "Costs " + n.Cost + " Wisdom";
                return LockReasonFor(n, budget, effective);
            }
        }

        /// <summary>The ability id the selected node grants IF it is an OWNED, assignable skill — else "".
        /// Non-empty means the quick-swap row can drop this skill into a slot 1..3.</summary>
        public string SelectedAssignAbilityId
        {
            get
            {
                var n = HeroTalentCatalog.FindNode(_selectedId);
                if (n == null) return "";
                string abilityId = AbilityIdOf(n);
                if (string.IsNullOrEmpty(abilityId)) return "";
                if (!BuildUnlockedSet(WisdomCurrencyService.Instance).Contains(n.Id)) return ""; // owned only
                return AbilityCatalog.FindById(abilityId) != null ? abilityId : "";
            }
        }

        /// <summary>True when the selected node is an owned skill that can be assigned to the quick-swap bar.</summary>
        public bool SelectedIsAssignable => !string.IsNullOrEmpty(SelectedAssignAbilityId);

        /// <summary>One-based current slot for the selected active, or 0 when not assigned.</summary>
        public int SelectedAssignedSlot => SelectedIsAssignable
            ? AssignableSkillBarAccess.SlotOf(SelectedAssignAbilityId) + 1 : 0;

        /// <summary>WO-1522 - the ONE player-facing sentence for a talent with no runtime
        /// consumer. It is COMPOSED here, from nothing but the node's own cost, so no authoring
        /// vocabulary can reach the screen: the diagnostic reason is emitted to FlowTrace instead.
        ///
        /// The word is COMING, stated in words (never a hue): the owner is red/green colourblind,
        /// so "this is not buyable value yet" has to survive greyscale. It is deliberately a
        /// SHORT, whole sentence - the learn dialog's state band truncates silently (FitBlock
        /// overflow Truncate), which is how the old string lost its tail mid-word.</summary>
        private static string DeadNodePlayerLine(HeroTalentNodeDef n, string diagnosticWhy)
        {
            string id = n != null ? n.Id : "";
            if (!string.Equals(id, s_lastDeadLogged, StringComparison.Ordinal))
            {
                s_lastDeadLogged = id;
                // The authoring note lives HERE and only here - a trace line, never a label.
                FlowTrace.Step("SkillTree", "dead node '" + id + "' shown as COMING; reason (dev only): " +
                                            (string.IsNullOrEmpty(diagnosticWhy) ? "unstated" : diagnosticWhy));
            }
            int cost = n != null ? n.Cost : 0;
            return "COMING - no effect in game yet. Costs " + cost + " Wisdom.";
        }

        private static string s_lastDeadLogged = "";

        // Best-available text when a node carries no authored description string.
        private static string DescribeFallback(HeroTalentNodeDef n)
        {
            if (n == null) return "";
            string ability = AbilityIdOf(n);
            if (!string.IsNullOrEmpty(ability))
            {
                var def = AbilityCatalog.FindById(ability);
                if (def != null)
                    return (string.IsNullOrEmpty(def.Name) ? ability : def.Name)
                         + (string.IsNullOrEmpty(def.Effect) ? "" : " - " + def.Effect);
                return "Unlocks ability: " + ability;
            }
            if (n.Effect != null && !string.IsNullOrEmpty(n.Effect.Type))
                return n.Effect.Type + (n.Effect.Value != 0f ? " " + n.Effect.Value : "");
            return "Passive talent.";
        }

        // ── Quick-swap bar (folds in the loadout screen) ─────────────────────────

        /// <summary>The player's quick-swap slots 1..3 (mirror of AssignableSkillBar). Never null.</summary>
        public IReadOnlyList<LoadoutSlotVM> QuickSlots => _quickSlots;

        /// <summary>Read-only quick-swap hint naming Loadout as the one assignment owner.</summary>
        // WO-1857: ONE authored sentence with a POSITIONAL hole - {0} is the loadout screen's
        // canon name (HudStrings.KeyHeroLoadout). The retired shape concatenated the destination
        // name into an English frame, so a French device read an English sentence with one French
        // word inside it (owner device capture). The word itself was also mistranslated as
        // "equipment"; that correction lives in the heroLoadout row, not here.
        public string QuickSwapStatus => LocalText.Format(
            "village.talents.skill_tree.quick_swap_hint",
            HudStrings.Get(HudStrings.KeyHeroLoadout));

        /// <summary>WO-1522 - THE CLASS'S LOCKED BASIC, NAMED. Canon (CLAUDE.md sec.7, WO-1105 R5):
        /// slot Q is the class's LOCKED basic; only W/E/R are loadout-swappable. The rail showed
        /// three swappable seats and never said so, so the player could not tell whether the bar
        /// was three abilities or four, nor which one they can never change.
        ///
        /// Q is <c>AbilityCatalog.GetLoadout(slug)[0]</c> - that method walks q,w,e,r IN ORDER, so
        /// index 0 IS Q by construction; it is not an assumption about authoring order.
        /// SlotKey is the literal "Q" (never a number - the three swappable seats own 1..3).
        /// Empty AbilityId means the class has no authored Q and the View draws nothing.</summary>
        public LoadoutSlotVM LockedBasic
        {
            get
            {
                var loadout = AbilityCatalog.GetLoadout(_heroSlug);
                if (loadout == null || loadout.Count == 0) return new LoadoutSlotVM(-1, "Q", "", "");
                var q = loadout[0];
                if (q == null || string.IsNullOrEmpty(q.Id)) return new LoadoutSlotVM(-1, "Q", "", "");
                return new LoadoutSlotVM(-1, "Q", q.Id, string.IsNullOrEmpty(q.Name) ? q.Id : q.Name);
            }
        }

        private void BuildQuickSlots()
        {
            _quickSlots.Clear();
            var bar = AssignableSkillBarAccess.Current;
            for (int i = 0; i < AssignableSkillBar.SlotCount; i++)
            {
                string id = bar != null ? bar.AbilityIdForSlot(i) : null;
                string name = "";
                if (!string.IsNullOrEmpty(id))
                {
                    var def = AbilityCatalog.FindById(id);
                    name = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : id;
                }
                _quickSlots.Add(new LoadoutSlotVM(i, (i + 1).ToString(), id ?? "", name));
            }
        }

        private void SubscribeBar()
        {
            var bar = AssignableSkillBarAccess.Current;
            if (bar == null) return;
            _barHandler = OnBarChanged;
            bar.Changed += _barHandler;
            _barSub = bar;
        }

        private void UnsubscribeBar()
        {
            if (_barSub != null && _barHandler != null) _barSub.Changed -= _barHandler;
            _barSub = null;
            _barHandler = null;
        }

        private void OnBarChanged()
        {
            if (_disposed) return;
            Rebuild();
            Raise();
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}
