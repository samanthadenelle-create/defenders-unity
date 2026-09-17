// =============================================================================
// TutorialHighlightRegistry — stable-id lookup of spotlightable targets (WO-T2).
// -----------------------------------------------------------------------------
// HUD/panels REGISTER their affordances by id as they build ("hud.build_button",
// "hud.wave_button"); world systems register Transforms or lazy RESOLVERS
// ("world.guide", "world.gate_direction"). The tutorial (UiSpotlight /
// TutorialFlow) resolves ids here — replacing TutorialHudOverlay.Highlight's
// UIDocument name-reach (spec §2.2). Reusable beyond the tutorial: any coach
// mark / attention cue can resolve the same ids.
//
// NOT tutorial-gated: registration is a dictionary write; nothing renders until
// something resolves an id. Destroyed targets resolve to null (Unity fake-null
// checked) so a scene reload can never hand out a stale RectTransform.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeNelle.Core.UI
{
    /// <summary>A resolved highlight target: either a uGUI rect or a world anchor.</summary>
    public readonly struct HighlightTarget
    {
        public readonly RectTransform Rect;     // uGUI target (screen-space)
        public readonly Transform World;        // world anchor (project via camera)
        public bool IsValid => Rect != null || World != null;
        public HighlightTarget(RectTransform rect) { Rect = rect; World = null; }
        public HighlightTarget(Transform world) { Rect = null; World = world; }
    }

    /// <summary>
    /// Id → target registry the tutorial spotlight resolves through. Owners
    /// register eagerly (UI, at build time) or lazily (world resolvers).
    /// </summary>
    public static class TutorialHighlightRegistry
    {
        private static readonly Dictionary<string, RectTransform> _rects =
            new Dictionary<string, RectTransform>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Transform> _worlds =
            new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Func<HighlightTarget>> _resolvers =
            new Dictionary<string, Func<HighlightTarget>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised when a target registers — a spotlight waiting on a late-built
        /// UI element can re-resolve without polling.</summary>
        public static event Action<string> TargetRegistered;

        /// <summary>Register a uGUI target by id (HUD/panels call this as they build). Idempotent.</summary>
        public static void Register(string id, RectTransform target)
        {
            if (string.IsNullOrEmpty(id) || target == null) return;
            _rects[id] = target;
            TargetRegistered?.Invoke(id);
        }

        /// <summary>Register a world-space anchor by id. Idempotent.</summary>
        public static void RegisterWorld(string id, Transform anchor)
        {
            if (string.IsNullOrEmpty(id) || anchor == null) return;
            _worlds[id] = anchor;
            TargetRegistered?.Invoke(id);
        }

        /// <summary>Register a LAZY resolver (for targets that move/spawn late, e.g.
        /// "world.gate_direction" = the nearest gate at resolve time). Explicit
        /// registrations win over resolvers for the same id.</summary>
        public static void RegisterResolver(string id, Func<HighlightTarget> resolver)
        {
            if (string.IsNullOrEmpty(id) || resolver == null) return;
            _resolvers[id] = resolver;
            TargetRegistered?.Invoke(id);
        }

        /// <summary>Remove a registration (any kind) for <paramref name="id"/>.</summary>
        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _rects.Remove(id);
            _worlds.Remove(id);
            _resolvers.Remove(id);
        }

        /// <summary>Resolve an id to a live target. Invalid (never registered, destroyed,
        /// or resolver returned nothing) ⇒ IsValid == false — callers degrade gracefully.</summary>
        public static HighlightTarget Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return default;

            if (_rects.TryGetValue(id, out var rt))
            {
                if (rt != null) return new HighlightTarget(rt);
                _rects.Remove(id);   // destroyed — drop the stale entry
            }
            if (_worlds.TryGetValue(id, out var w))
            {
                if (w != null) return new HighlightTarget(w);
                _worlds.Remove(id);
            }
            if (_resolvers.TryGetValue(id, out var fn) && fn != null)
            {
                try { return fn(); }
                catch (Exception ex)
                {
                    Diagnostics.FlowTrace.Warn("Tutorial", $"highlight resolver '{id}' threw: {ex.Message}");
                }
            }
            return default;
        }

        /// <summary>True when the id is registered (eager or resolver). Used by the
        /// DataRegression invariant "every highlight id is a known registry key" via
        /// the KnownIds snapshot below.</summary>
        public static bool IsRegistered(string id) =>
            !string.IsNullOrEmpty(id) &&
            (_rects.ContainsKey(id) || _worlds.ContainsKey(id) || _resolvers.ContainsKey(id));

        /// <summary>The ids the shipped game is expected to register (build-time contract
        /// for data validation — runtime registration is scene-dependent).</summary>
        public static readonly string[] KnownIds =
        {
            "hud.build_button",     // VillageHudController TownActions BUILD icon
            "hud.wave_button",      // VillageHudController Start Wave CTA
            "world.guide",          // TutorialWorldAnchors resolver (WO-1012 P2: the pet-Echo GUIDE body / steward stand-in / Heart / town anchor)
            "world.gate_direction", // TutorialWorldAnchors resolver (nearest gate to hero)
            "build.tab_town",       // WO-702 — BuildPaletteUI Town category tab (registers when the palette builds)
            "build.tab_defenses",   // WO-702 — BuildPaletteUI Defenses category tab (founding_defense beat)
            "build.card.lumberyard",// WO-746 BM-3 — the Lumberyard card (registered per Render as build.card.<entryId>); kept as the accepted wood-id equivalent
            // F8 seq 632 root cause 3 (2026-08-02): a step must be able to point at the CARD, not
            // just the Build button. BuildPaletteUI registers "build.card.<entryId>" for every card
            // it renders, so these two resolve the moment the Town palette builds; they are listed
            // here because DataRegression validates every authored highlight against this contract.
            "build.card.pet-house",           // founding_hollow — the Echo Hollow card among ~10 Town cards
            "build.card.collector_lumbermill",// founding_stores — the Lumbermill card (the collector that actually harvests)
            "hud.pets",             // FTUE-04 — the persistent "Pets" pet-box button (EchoUnlockFeedback EchoPetBoxButton); resolved lazily below
            "hud.builders_chip",    // WO-1012 P3 — the Builders/queue status chip (HudKitController.BuildQueueStatusChip) the TIMERS beat spotlights ("Work takes time, Keeper. Watch the ledger.")
            // WO-1340 — the two hops of the SPEND-A-TALENT-POINT route, owner-confirmed
            // 2026-09-03 on build 2026.09.03.353742: "the path to the skills tree is fixed,
            // it's Hero then Skills". Bar HERO face -> the SKILLS card on the Hero deck.
            "hud.hero_button",      // HudKitController bar face (ActionBarButtonId.Bag, labelled "Hero") -> PanelId.HeroDeck
            "deck.card.skills",     // PlayerDeckWorkspace "DeckCard_Skills" -> PanelId.HeroSkillTree; resolved lazily below
            // WO-1389 - the post-first-raid HOW beat walks the REAL Manage > Troops screen. All four
            // are registered EAGERLY by ManageScreenPanel as it builds the Troops workspace (rail
            // row per troop id, the two CTA faces on the selected-troop card, the OPEN QUEUE face on
            // the TRAINING NOW band); Register is idempotent and the panel re-registers on every
            // Render, so a rebuilt row resolves again on the next spotlight frame.
            "manage.troop_row.troop-footman",   // ManageScreenPanel.BuildTroopRailRow ("manage.troop_row.<troopId>")
            "manage.troop_cta_train",           // ManageScreenPanel.BuildTroopCard TRAIN 1 <NAME> face
            "manage.troop_cta_upgrade",         // ManageScreenPanel.BuildTroopCard UPGRADE TO L<n> face
            "manage.open_queue",                // ManageScreenPanel.AddTroopTrainingNowBand OPEN QUEUE face
            // WO-1802 - the two hops of the RAID DOOR route (owner 2026-09-16: "make the raid
            // door obvious after founding"). The dock JOURNEY face -> the Raids card on the
            // Journey deck. Exactly the WO-1340 hud.hero_button -> deck.card.skills shape, and
            // for the same reason: the route is FIXED (the deck is the only public door to the
            // raid grid since WO-1421 took the bar's Map face away), so a spotlight can walk it.
            //
            // ⚠ WHY THESE ARE THE FIRST JOURNEY/RAIDS IDS TO EXIST. ctx_raid_first and
            // ctx_heartfire both had to carry their route IN WORDS ONLY and said so in their
            // notes ("there is no Journey/Raids id in TutorialHighlightRegistry.KnownIds and
            // DataRegression reds an invented one"). That was true and is now fixed rather than
            // worked around - the words STAY in the copy as well, because the owner is red/green
            // colourblind and a spotlight is a visual-only affordance.
            "hud.journey_button",   // HudKitController dock JOURNEY face (calm slot 3 / outside slot 1) -> PanelId.JourneyDeck
            "deck.card.raids",      // PlayerDeckWorkspace "DeckCard_Raids" -> RaidEntryGate.RequestOpen; resolved lazily below
        };

        // FTUE-04: the founding_echo tutorial step spotlights the Pets button, but that
        // button (EchoUnlockFeedback.BuildPetBoxButton -> GameObject "EchoPetBoxButton")
        // lives on a self-contained overlay canvas that never called Register. Wire a LAZY
        // resolver here (in-lane) so the spotlight resolves the LIVE button at show time --
        // no dependency from the Village button-build code back into Core. Idempotent; a
        // missing/inactive button resolves to invalid (spotlight degrades gracefully).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBuiltInResolvers()
        {
            RegisterResolver("hud.pets", () =>
            {
                var go = GameObject.Find("EchoPetBoxButton");
                if (go == null) return default;
                return go.transform is RectTransform rt ? new HighlightTarget(rt) : default;
            });

            // WO-1340 — the SKILLS card on the Hero deck (PlayerDeckWorkspace.BuildCard names
            // every card "DeckCard_<Title>"). A LAZY resolver for the same reason hud.pets is
            // one: the deck's cards are built per RenderPage, only once the player opens the
            // Hero panel, so there is no build-time rect to Register and no eager hook that
            // would not be dead most of the session.
            //
            // ⚠ RESOLVES THE CARD'S OWN RECT, DELIBERATELY — NOT A LABEL INSIDE IT. Every
            // label on that panel is currently drawn TWICE by two different owners in two
            // fonts with two different wordings (WO-1341, another lane's fix), so label
            // geometry there is ambiguous and a label-anchored spotlight would sit on
            // whichever producer happened to win. The card rect is unambiguous and is also
            // the actual touch target. This adds NO third label producer.
            RegisterResolver("deck.card.skills", () =>
            {
                var go = GameObject.Find("DeckCard_Skills");
                if (go == null) return default;
                return go.transform is RectTransform rt ? new HighlightTarget(rt) : default;
            });

            // WO-1802 — the RAIDS card on the Journey deck. A LAZY resolver for the identical
            // reason deck.card.skills is one: PlayerDeckWorkspace builds its cards per
            // RenderPage, only once the player opens the deck, so there is no build-time rect to
            // Register and an eager hook would be dead for most of a session.
            //
            // ⚠ RESOLVES THE CARD'S OWN RECT, NOT A LABEL INSIDE IT — the WO-1341 rule the
            // skills resolver above records, and here it matters twice over: the Raids card
            // draws TWO different captions depending on lock state (the live purpose line, or
            // the remedy line under a "[ LOCKED ]" badge) plus, from this ticket, a
            // "[ RAID READY ]" badge. The card rect is the one unambiguous geometry and is also
            // the real touch target.
            //
            // Degrades to nothing if the deck is closed, which is correct: the spotlight's first
            // hop is the dock face, and this hop only fires on panel.opened:JourneyDeck.
            RegisterResolver("deck.card.raids", () =>
            {
                var go = GameObject.Find("DeckCard_Raids");
                if (go == null) return default;
                return go.transform is RectTransform rt ? new HighlightTarget(rt) : default;
            });
        }
    }
}
