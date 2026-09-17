// =============================================================================
// EndStateVM — the ONE view-model behind every end-state screen (WO-B, UI
// conformance audit 2026-07-02 §3.2): battle victory, battle defeat, hero death
// (non-hub), and wave-clear results all construct THIS and render through
// EndStateView. Pure data + adapter factories; the View binds it and never
// reads game state (MVVM strict, presentation-does-no-service).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.UI
//
// Icon resolution happens HERE (in the factories, the model side of the seam),
// sprite-first with null fallback per the RpgUiCatalog / ItemIconCatalog
// contract — a null Icon renders as a label-only slot plate, never blanks.
// The outcome EMBLEM deliberately does NOT reuse the fringed AI crown asset
// (Resources/RpgUi/crown/*): it uses the committed bronze RPG icon pack —
// icon_combat (crossed sword+axe) for wins, icon_shield for defeats.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;   // Guard / FlowTrace — §12: the banner never breaks the wave loop

namespace DeNelle.Village.UI
{
    /// <summary>Which end-state this screen represents (drives copy + trace tag).</summary>
    public enum EndStateKind
    {
        Victory,
        Defeat,
        HeroDeath,
        WaveResults,
    }

    /// <summary>One spoils row: icon (null = label-only plate) + label + amount.</summary>
    public sealed class SpoilRowVM
    {
        public Sprite Icon;
        public string Label;
        public string Amount;
        /// <summary>Rarity index for the kit slot plate frame (1 = common).</summary>
        public int Rarity = 1;

        /// <summary>
        /// This row is a SENTENCE + a cost, not a short noun + a short number — so it takes a
        /// whole band to itself at the full body width, and its label column is widened
        /// (EndStateView.SpoilBandPlan / BuildSpoilRow).
        ///
        /// OWNER F8 2026-09-02, defect #4: on a wave-clear panel the three resource rows read
        /// icon + "Wood" + "+180", and the structure-damage row crammed "Archer Tower - damaged
        /// 40%" and "Repair 40 wood, 12 iron" into a half-width cell of the same grid. BOTH
        /// halves ellipsised ("Archer Tow..." / "Repair..."), so it read as a BROKEN resource row
        /// rather than a different kind of row. The row declares its own shape here; the view
        /// never sniffs string lengths to guess it.
        ///
        /// TWO further contracts hang off this flag as of the 2026-09-02 re-capture, and both are
        /// load-bearing:
        ///   * COLUMN SPLIT — a wide row's plate is split in proportion to its two MEASURED
        ///     strings, not at a fixed fraction, and every wide row on one screen renders at one
        ///     shared font size (EndStateView.WideLabelShare / SolveWideRowFontPx). A fixed split
        ///     is what left "DESTROYED, looted ..." ellipsised beside a full-size "damaged".
        ///   * ICON — a wide row is NEVER handed the view's generic loot fallback (the chest,
        ///     RpgUiCatalog.IconInventory). It shows the icon its MODEL chose or none at all,
        ///     because a chest on a row reporting a LOSS states the opposite of the truth (owner
        ///     F8 2026-09-02 defect #3: the two damage rows drew a money bag). A wide row that
        ///     genuinely wants the chest — the combined spoils tail, "Plans Recovered" — sets
        ///     <see cref="Icon"/> explicitly.
        /// </summary>
        public bool Wide;
    }

    /// <summary>
    /// The end-state screen model. Construct via the static factories (they adapt
    /// each outcome's data + resolve icons); EndStateView renders it.
    /// </summary>
    public sealed class EndStateVM
    {
        public EndStateKind Kind;
        public string Title;
        public string Subtitle;

        /// <summary>Battle duration in seconds; &lt; 0 hides the time row.</summary>
        public float TimeSeconds = -1f;
        /// <summary>Earned stars 0..3; &lt; 0 hides the rating row.</summary>
        public int Stars = -1;
        /// <summary>Flawless win (perfect-tier); no signal is threaded yet — defaults false.</summary>
        public bool Perfect;

        /// <summary>Outcome emblem for the medallion socket (bronze icon pack, never the crown).</summary>
        public Sprite Emblem;

        /// <summary>
        /// WO-1374 - the optional PROGRESSION line a victory carries, e.g.
        /// "The Broken Garrison unlocked". Null on every screen that has nothing to announce.
        ///
        /// <para>It is its own field, and not a sentence pasted into <see cref="Subtitle"/> by
        /// the caller, so the sibling ladder lane owns WHAT unlocked while this file owns
        /// nothing about the ladder. Today <see cref="FromRaidVictory"/> also appends it to the
        /// subtitle so it is visible with no EndStateView change; a view that grows a dedicated
        /// band for it removes that append in the same edit.</para>
        /// </summary>
        public string UnlockLine;

        /// <summary>
        /// WO-1810 - troops this raid lost PERMANENTLY (killed + whatever the exit charged on the
        /// survivors). &lt; 0 = unknown/not applicable, and no screen states it.
        ///
        /// <para>Its own field, not only a sentence inside <see cref="Subtitle"/>, for the same
        /// reason <see cref="UnlockLine"/> is: a view that grows a dedicated band binds THIS and
        /// drops the subtitle append in the same edit. Carried by BOTH raid end states - a victory
        /// can now cost dead troops too ("any troop killed is dead"), and a win that quietly
        /// deleted three troops would be the same silence the non-victory screen was built to end.</para>
        /// </summary>
        public int TroopsLost = -1;

        public readonly List<SpoilRowVM> Spoils = new List<SpoilRowVM>();

        /// <summary>The ONE primary action (owner button law: an end-state has exactly one way out).</summary>
        public string PrimaryLabel = "Continue";
        /// <summary>Route tag for the FlowTrace line (e.g. "return-home", "respawn").</summary>
        public string PrimaryRoute = "close";
        /// <summary>Invoked exactly once (button tap OR auto-dismiss), then the view tears down.</summary>
        public Action Primary;
        // Optional durable-operation prerequisite. A refusal keeps the action available for retry.
        public Func<bool> PrimaryGate;

        /// <summary>&gt; 0 = fire Primary automatically after this many real seconds (softlock guard).</summary>
        public float AutoDismissSeconds;

        /// <summary>
        /// WO-1543 (owner ruling 2026-09-06: "Hold on touch, longer guard") - while true, any
        /// player interaction RE-ARMS <see cref="AutoDismissSeconds"/> instead of the countdown
        /// running blind to whether anyone is reading.
        ///
        /// <para>STOP - THE GUARD IS NOT REMOVED AND MUST NEVER BE. A player who walked away is still
        /// returned home; this flag only teaches the timer to tell a reading player from an absent
        /// one. RESTART, not cancel, was chosen deliberately: a cancel means one stray tap pins the
        /// screen open forever, which re-opens the very softlock the guard exists to prevent
        /// (WO-1543 section 3's own warning). Restart keeps the backstop alive while giving a
        /// reading player unlimited time.</para>
        ///
        /// <para>DEFAULTS FALSE, and that is load-bearing: <see cref="EndStateView"/> serves the
        /// arena, the dungeon, hero death, game over and the wave-clear banner as well as raids
        /// (each with its own dismiss value - <see cref="FromBattleDefeat"/> 2.5s,
        /// <see cref="FromHeroDeath"/> 6s, <see cref="FromGameOver"/> 0s,
        /// <see cref="FromOutpostVictory"/> 4s). Opting in per template is what stops this ruling
        /// from silently moving every other end state's timing (WO-1543 acceptance 4).</para>
        /// </summary>
        public bool HoldOnInteraction;

        public bool HoldWorld;

        // ── WO-969: the PENDING-TRANSITION HAND-BACK (owner F8 seq 2315) ──────────
        // A screen is presentation. A PENDING STATE TRANSITION (the arena's masked home
        // return, a respawn) is NOT — and until now it was owned by <see cref="Primary"/>,
        // i.e. by a GameObject that PanelManager, a replacing Show() or a scene load may
        // destroy at any moment. When that happened the transition was simply dropped and
        // the player was stranded until BattleArena's 45s watchdog rescued her (PROVEN BY
        // CAPTURE: opening Pause over the victory summary -> PanelManager.NotifyOpened ->
        // EndStateView.CloseFromArbiter -> Destroy -> STRANDING WATCHDOG FIRED).
        //
        // Abandoned is the hand-back: the view REPORTS its own destruction to whoever
        // owns the transition, instead of taking it to the grave. It is NOT "Continue" —
        // a displaced end-state still must never silently fire the player's CHOICE. It is
        // the owner being told "your screen is gone; the transition is yours again."
        // Fired exactly once, and only while <see cref="Primary"/> never ran.

        /// <summary>Invoked exactly once if this end-state is destroyed WITHOUT its
        /// <see cref="Primary"/> ever running (arbiter swap-out, replacing Show, scene load,
        /// or any other destroy). Hands a pending state transition back to its real owner.
        /// Null = nothing load-bearing was delegated to this screen.</summary>
        public Action Abandoned;

        private bool _handedBack;

        /// <summary>
        /// WO-969 - run the hand-back exactly once. Lives on the MODEL, not the view, for two
        /// reasons: (1) a pending state transition outliving the screen is precisely the point, so
        /// its latch must not live in the thing being destroyed; (2) it makes the contract provable
        /// HEADLESSLY (EndStateTransitionHandoffRegression) with no canvas, no coroutines and no
        /// edit-mode Destroy.
        ///
        /// Contract, and every clause of it is load-bearing:
        ///   * Runs only while <see cref="Primary"/> NEVER ran - <see cref="Primary"/> is nulled by
        ///     the view the instant it fires, so a normal Continue makes this a permanent no-op.
        ///   * NEVER invokes <see cref="Primary"/>. A displaced end-state must not silently make the
        ///     player's CHOICE; it only tells the transition's owner that the screen is gone.
        ///   * Fires at most once no matter how many destroy paths call it.
        /// </summary>
        /// <returns>True if a hand-back actually ran on this call.</returns>
        public bool HandBackPendingTransition(string reason)
        {
            if (_handedBack) return false;
            if (Primary == null) return false;        // the transition already ran (or none was pending)
            var handBack = Abandoned;
            Abandoned = null;
            _handedBack = true;
            if (handBack == null) return false;       // nothing load-bearing was delegated

            FlowTrace.Warn("EndState",
                "'" + Title + "' - HANDING THE PENDING TRANSITION BACK to its owner (" + reason + "). " +
                "It is completing NOW, independently of the screen's lifetime: a state change was " +
                "never a view's to lose. (WO-969; BattleArena's stranding watchdog stays armed as the " +
                "last-resort net and should now never fire.)");
            Guard.Try("EndState", "abandoned-transition hand-back", () => handBack.Invoke());
            return true;
        }

        /// <summary>True once <see cref="HandBackPendingTransition"/> has latched (test/diagnostic).</summary>
        public bool HandedBack => _handedBack;

        /// <summary>Compact banner mode (wave-clear): small top panel, no scrim/backdrop, non-blocking.</summary>
        public bool Compact;

        // ── WO-672 Slice E: banner CTA (the ONE case the compact CTA seat returns) ──
        // Distinct from Primary on purpose: on a compact banner, tap-anywhere and
        // auto-dismiss BOTH funnel Primary — if Primary were "Repair All", a stray
        // tap or the timer would silently spend crystals. The CTA is the explicit
        // button-only action; firing it also dismisses the banner.

        /// <summary>Compact-banner CTA label (e.g. "Repair All - 40 wood, 12 iron"); null/empty = no CTA.</summary>
        public string CtaLabel;
        /// <summary>False renders the CTA disabled but still showing its cost (informative, not dead).</summary>
        public bool CtaEnabled = true;
        /// <summary>Route tag for the CTA FlowTrace line (e.g. "repair-all").</summary>
        public string CtaRoute = "cta";
        /// <summary>Invoked exactly once on CTA tap; the banner then dismisses via Primary.</summary>
        public Action Cta;

        // ── Factories (the adapters — one per outcome) ────────────────────────

        /// <summary>
        /// Battle-arena WIN summary (replaces BattleArenaHud.ShowVictorySummary).
        /// <paramref name="onContinue"/> = the deferred home-return; the view latches it once.
        /// </summary>
        /// <param name="primaryRoute">FlowTrace route TAG only — never behaviour. Leave null to
        /// derive it from where the win happened (see <see cref="DefaultVictoryRoute"/>).</param>
        public static EndStateVM FromBattleVictory(int stars, float durationSeconds,
            int xp, int wisdom, int wood, int iron, string gearName,
            Action onContinue, float autoTimeoutSeconds = 20f, bool perfect = false,
            string primaryRoute = null,
            // WO-969: the SAME return, wired as the hand-back. The arena's masked home return is
            // the only route out of a won arena, so it must survive this screen being destroyed.
            // Callers that pass nothing keep the old (view-owned) behaviour.
            Action onAbandon = null,
            // WO-1104 (owner felt-test 2026-08-16): GOLD banked during the fight - granted per
            // kill by Enemy.Die but never REPORTED, so coin income was invisible here - plus the
            // KILL COUNT, so a five-body win reads as a bigger fight than a one-body win instead
            // of just a bigger unattributable number. Both default to 0: every existing caller
            // (and the handoff regression's positional calls) keeps its exact behaviour.
            int gold = 0, int kills = 0)
        {
            string felled = kills > 1 ? kills + " foes felled. "
                          : kills == 1 ? "1 foe felled. "
                          : "";
            var vm = new EndStateVM
            {
                Kind = EndStateKind.Victory,
                Title = "Victory!",
                Subtitle = felled + (perfect ? "Flawless! The realm is safer because of you!"
                                             : "The realm is safer because of you!"),
                Stars = Mathf.Clamp(stars, 0, 3),
                Perfect = perfect,
                TimeSeconds = Mathf.Max(0f, durationSeconds),
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconCombat),
                PrimaryLabel = "Continue",
                PrimaryRoute = primaryRoute ?? DefaultVictoryRoute(),
                Primary = onContinue,
                Abandoned = onAbandon,
                AutoDismissSeconds = Mathf.Max(1f, autoTimeoutSeconds),
            };

            if (xp > 0)
                vm.Spoils.Add(new SpoilRowVM
                {
                    Icon = RpgUiCatalog.Get(RpgUiCatalog.RoleBadge, RpgUiCatalog.BadgeLevel),
                    // WO-697: reward numbers render through the ONE kit formatter
                    // (ElarionUi.CompactNumber) — never verbatim six-digit strings.
                    Label = "Experience", Amount = "+" + ElarionUi.CompactNumber(xp),
                });
            if (gold > 0)
                vm.Spoils.Add(new SpoilRowVM
                {
                    // Icon left null on purpose (the Iron-row lesson above): EndStateView
                    // resolves the CONCEPT icon from the label - "gold" -> currency/currency_gold
                    // in concept-icons.json - which is a real PNG with alpha.
                    Label = "Gold", Amount = "+" + ElarionUi.CompactNumber(gold),
                });
            if (wisdom > 0)
                vm.Spoils.Add(new SpoilRowVM
                {
                    Icon = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconTree),
                    Label = "Wisdom", Amount = "+" + ElarionUi.CompactNumber(wisdom),
                });
            if (wood > 0)
                vm.Spoils.Add(new SpoilRowVM
                {
                    // Item-catalog first (sprite-first, null-safe); no wood sheet art today
                    // -> renders as a label-only plate until the art lands.
                    Icon = ItemIconCatalog.ForConsumable("mat_wood", "Wood"),
                    Label = "Wood", Amount = "+" + ElarionUi.CompactNumber(wood),
                });
            if (iron > 0)
                vm.Spoils.Add(new SpoilRowVM
                {
                    // NO ItemIconCatalog lookup here (owner F8 2026-08-05: "it's only some
                    // triangles" / the Iron row painted an opaque cream BOX). ForConsumable
                    // ("iron_ore_ingot","Iron Ore") keyword-matched into a JPEG sprite sheet —
                    // a .jpg carries NO alpha, so the sub-sprite rendered as a solid cream plate
                    // instead of an icon. Leave Icon null exactly like the raid loot rows below:
                    // EndStateView.BuildSpoilRow then resolves the CONCEPT icon from the label
                    // ("iron" -> currency/currency_iron, concept-icons.json:201-204), which is a
                    // real PNG with alpha — the same path "Wood" already renders through.
                    Label = "Iron", Amount = "+" + ElarionUi.CompactNumber(iron),
                });
            if (!string.IsNullOrEmpty(gearName))
                vm.Spoils.Add(new SpoilRowVM
                {
                    // Gear drops arrive as a DISPLAY NAME only (BattleArena.TryGrantArenaGear)
                    // — no id to resolve through GearCatalog/ItemIconCatalog, so the bronze
                    // sword icon stands in for "a piece of gear".
                    Icon = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconSword),
                    Label = gearName, Amount = "Equipped",
                    Rarity = 2,
                });
            return vm;
        }

        /// <summary>
        /// The honest default for an arena win's <see cref="PrimaryRoute"/> TAG.
        ///
        /// F8 2026-08-05, triage cost: PrimaryRoute is documented on the field above as a
        /// FlowTrace tag and nothing else — yet <see cref="FromBattleVictory"/> hard-coded
        /// "return-home" for EVERY arena win, dungeons included. A dungeon win performs no
        /// return home and no scene load at all: BattleArena warps the hero back to the
        /// engagement spot IN THE SAME SCENE and DungeonController.SettleEncounter logs "hero
        /// resumes in place". The trace therefore asserted a route that path was never designed
        /// to take, and triage went hunting a scene load that does not exist. Make the tag say
        /// what actually happens.
        ///
        /// STRICTLY PRESENTATIONAL: this only picks the string that lands in the FlowTrace line.
        /// Primary / onContinue / the deferred return are untouched — the win behaves identically
        /// either way. Scene-name convention matches AudioService.cs:971.
        /// </summary>
        private static string DefaultVictoryRoute()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty;
            return scene.StartsWith("Dungeon", StringComparison.Ordinal)
                ? "resume-in-place"
                : "return-home";
        }

        /// <summary>Battle-arena LOSS sting (replaces BattleArenaHud.ShowLossPanel). The
        /// controller returns the hero home immediately, so this auto-dismisses fast.</summary>
        public static EndStateVM FromBattleDefeat()
        {
            return new EndStateVM
            {
                Kind = EndStateKind.Defeat,
                Title = "Defeat",
                Subtitle = "Fall back and regroup, hero.",
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconShield),
                PrimaryLabel = "Continue",
                PrimaryRoute = "close",
                AutoDismissSeconds = 2.5f,
            };
        }

        /// <summary>
        /// HERO DEATH in a NON-hub scene (closes the audit MISSING: HeroHealth silently
        /// respawns/evacuates outside hubs with no defeat sting). The respawn/evac is
        /// already automatic (HeroHealth.HandleDeath) — "Rise again" simply dismisses
        /// the sting; the existing respawn IS the route. Never pauses time (a pause
        /// would freeze the respawn coroutine this screen narrates).
        /// </summary>
        public static EndStateVM FromHeroDeath(bool enemyOwnedScene)
        {
            return new EndStateVM
            {
                Kind = EndStateKind.HeroDeath,
                Title = "YOU HAVE FALLEN",
                Subtitle = enemyOwnedScene
                    ? "The raid is lost. You retreat to the castle to fight another day."
                    : "The dark takes you, but Elarion still needs its defender.",
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconShield),
                PrimaryLabel = "Rise again",
                PrimaryRoute = "respawn",
                AutoDismissSeconds = 6f,
            };
        }

        /// <summary>
        /// HUB game-over (GameOverScreen routes here — audit §2e: its bespoke
        /// EventSystem-free manual hit-test overlay is retired). Heart-fell and
        /// hero-fell share this shape; the caller supplies the copy (the DEF-141 /
        /// WO-235 locked canon strings live in GameOverScreen). ONE way out (owner
        /// button law): Try Again — the old Leave-to-Title second exit is dropped
        /// because the template exposes exactly one primary action. NEVER
        /// auto-dismisses: an auto-fired Retry would reload the scene without
        /// player intent (the game is paused under this screen; the view's tween
        /// runs on unscaled time, so the pause is safe).
        /// </summary>
        public static EndStateVM FromGameOver(bool heartFell, string title, string body,
                                              Action onRetry)
        {
            return new EndStateVM
            {
                Kind = heartFell ? EndStateKind.Defeat : EndStateKind.HeroDeath,
                Title = title,
                Subtitle = body,
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconShield),
                PrimaryLabel = "Try Again",
                PrimaryRoute = "retry",
                Primary = onRetry,
                AutoDismissSeconds = 0f,   // deliberate: no softlock-guard here — Retry must be chosen
            };
        }

        /// <summary>
        /// RAID victory (baked RaidBase_* teleport scenes — replaces
        /// RaidVictoryController's bespoke "VICTORY" banner). The base is claimed and,
        /// on a new claim, the next companion joins; the ONE way out is
        /// <paramref name="onReturn"/> (SceneRouter.GoCastle) — the anti-soft-lock
        /// auto-return is the template's AutoDismissSeconds (fires the same route).
        /// </summary>
        public static EndStateVM FromRaidVictory(string joinedCompanionName,
            Action onReturn, float autoReturnSeconds = 20f,
            int stars = -1, int destructionPercent = -1, float elapsedSeconds = -1f,
            ResourceCost credited = default(ResourceCost), string unlockLine = null,
            // WO-1810 - troops lost for good on a WON raid. Trailing + optional, so every existing
            // positional caller keeps its exact behaviour; < 0 = unknown and the line is omitted.
            int troopsLost = -1)
        {
            // WO-771.6: the LOCKED-V1 scoring/loot now rides the raid victory screen —
            // stars (0-3), the %-destruction of the base, the clear time, and the loot
            // breakdown. All fields are opt-in (a caller with no scorer passes the old
            // three args and the screen renders exactly as before).
            string body = !string.IsNullOrEmpty(joinedCompanionName)
                ? "The base is CLAIMED - it is yours now.\n" + joinedCompanionName + " joins your party."
                : "The base is CLAIMED - it is yours now.";
            if (destructionPercent >= 0)
                body += "\n" + destructionPercent + "% razed.";

            // WO-1810 - A WIN CAN NOW COST DEAD TROOPS, SO THE WIN SAYS SO. Owner ruling
            // 2026-09-16: "any troop killed is dead" - that applies to victory as much as to a
            // retreat, and a screen that congratulates the player while three troops are quietly
            // gone from the barracks is the same silence the non-victory screen was built to end.
            // ONLY when lost > 0: a clean sweep must not be handed a "0 troops lost" consolation
            // line, and this screen already carries stars, razed %, up to five spoils rows and an
            // unlock line inside the same compressing band budget.
            if (troopsLost > 0)
                body += "\n" + troopsLost + (troopsLost == 1 ? " troop lost" : " troops lost") +
                        " taking the base.";

            var vm = new EndStateVM
            {
                Kind = EndStateKind.Victory,
                Title = "Victory!",
                Subtitle = body,
                TroopsLost = troopsLost,
                Stars = stars >= 0 ? Mathf.Clamp(stars, 0, 3) : -1,
                TimeSeconds = elapsedSeconds >= 0f ? elapsedSeconds : -1f,
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconCombat),
                PrimaryLabel = "Return to Castle",
                PrimaryRoute = "return-home",
                Primary = onReturn,
                AutoDismissSeconds = Mathf.Max(2f, autoReturnSeconds),
                // WO-1543 - the RAID end states are the two that opt in. This screen carries the
                // star result, up to five spoils rows, a companion-join line and, at a capped
                // bank, "Some of the reward could not be paid out" - the one message a player
                // must not miss, on the screen that used to leave by itself.
                HoldOnInteraction = true,
            };

            // Loot breakdown (null Icon -> BuildSpoilRow resolves the concept icon from
            // the label, then a generic fallback - a reward row never blanks).
            //
            // WO-1374 - THIS SCREEN USED TO NAME A CURRENCY THAT DOES NOT EXIST, AND TO HIDE
            // MOST OF THE PAYOUT. The retired shape took exactly two ints (lootCrystals,
            // lootFood) and emitted two rows: "Crystals" and "Stone". Two defects in three
            // lines, and both are the visible half of the raid loop:
            //   * "Stone" IS NOT A CURRENCY. It was retired as a balance - GameState.cs:59
            //     records the removal of `public int Stone = 20;` in-code (WO-1212,
            //     2026-08-26). The row was rendering the FOOD amount under a dead name.
            //   * WOOD, IRON AND GOLD WERE NEVER SHOWN AT ALL. Raids pay all five currencies
            //     (PROGRAM_RAID_ECONOMY_2026-09-04 section 1: 1,800 wood / 1,100 iron /
            //     3,000 food / 2,200 gold / 20-30 crystals for a perfect Camp I), so a
            //     three-star clear told the player about two fifths of what it paid, one
            //     fifth of it by the wrong name. "Raid -> get richer" cannot be felt through
            //     a screen that does not report getting richer.
            // The parameter is now the whole CREDITED basket - the measured delta the wallet
            // actually took, never the requested amount (the WO-978 contract the caller keeps).
            // One row per NON-ZERO currency, ordered as section 1's table orders them.
            AddSpoil(vm, "Wood", credited.Wood);
            AddSpoil(vm, "Iron", credited.Iron);
            AddSpoil(vm, FoodSpoilLabel, credited.Stone);
            AddSpoil(vm, "Gold", credited.Coins);
            AddSpoil(vm, "Crystals", credited.Crystals);

            // The UNLOCK LINE (optional). Carried as its own field rather than smuggled into
            // the body text so the sibling ladder lane can hand this factory "The Broken
            // Garrison unlocked" - the CREATIVE_CANON_ELARION_2026-09-04 section 3 name, never
            // its superseded "Ironwatch Garrison" first pass - without this file changing again.
            // Appended to the subtitle here so it is VISIBLE today with no EndStateView change;
            // whoever adds a dedicated band in the view drops this append in the same edit, so
            // the line can never render twice.
            if (!string.IsNullOrEmpty(unlockLine))
            {
                vm.UnlockLine = unlockLine;
                vm.Subtitle = string.IsNullOrEmpty(vm.Subtitle) ? unlockLine : vm.Subtitle + "\n" + unlockLine;
            }
            return vm;
        }

        // =====================================================================
        //  WO-1561 - THE NON-VICTORY RAID EXIT. Retreat and clock-expiry.
        // =====================================================================

        /// <summary>Title on the screen a player sees when they call the assault off themselves.</summary>
        public const string RetreatTitle = "Retreat";
        /// <summary>Title on the screen a player sees when the raid clock runs out.</summary>
        public const string TimeoutTitle = "Time!";
        /// <summary>The exit label the retreat path passes; also the trace tag.</summary>
        public const string RetreatReason = "retreat";
        /// <summary>The exit label the clock-expiry path passes; also the trace tag.</summary>
        public const string TimeoutReason = "timeout";
        /// <summary>Stated in words when the town bank could not take everything the raid earned.</summary>
        public const string RewardShortSentence = "Some of the reward could not be paid out.";

        /// <summary>
        /// WO-1526 — the raid HUD's status line the instant the hero falls, while the raid keeps
        /// running. It lives HERE, on the view model, and not inside
        /// <c>RaidDeployController</c>'s view code, because the acceptance criterion is that the
        /// sentence is composed by the VM (the same reason <see cref="TimeoutReason"/> and
        /// <see cref="RewardShortSentence"/> are consts here rather than literals at their call
        /// sites): presentation copy has exactly one owner, so the words a player reads and the
        /// words an oracle asserts can never drift apart.
        /// </summary>
        public const string HeroDownArmyFightsOn = "HERO DOWN - your army fights on";

        /// <summary>
        /// WO-1561 - THE RESULT SCREEN A LOSING OR ABANDONED RAID NEVER HAD.
        ///
        /// <para><b>THE DEFECT THIS CLOSES.</b> <c>RaidDeployController.DoRetreat</c> settled the
        /// score, paid the partial loot, reconciled the army and then called
        /// <c>SceneRouter.GoCastle()</c> - with no screen at all. The clock-expiry exit funnels
        /// into the same method. So a player who retreated, or simply ran out the clock, was
        /// teleported into town having earned real loot and possibly a star
        /// (<c>RaidScoring.cs</c> grants 1 at <c>destructionPct &gt;= 0.5f</c>) and was told NONE
        /// of it. Nothing picked it up in town either: every reader of <c>RaidResult</c> is
        /// raid-scene-side. The outcome was computed, banked, and discarded unread - and it is the
        /// exit a new player is most likely to reach (memory retention-is-the-business-problem).</para>
        ///
        /// <para>STOP - <b>EVERY NUMBER HERE IS WHAT WAS BANKED, NEVER WHAT WAS PROMISED.</b> The
        /// caller measures the wallet either side of the grant and hands the DELTA, exactly as
        /// <c>RaidVictoryController.GrantLoot</c> does (the WO-978 contract). WO-1461 records the
        /// live case this protects: the deploy card quoted ~1,800 wood and 25 arrived, because the
        /// bank was full. When the wallet took less than the raid awarded,
        /// <paramref name="rewardShort"/> puts <see cref="RewardShortSentence"/> on the screen in
        /// WORDS - never a colour, never silence (the owner is red/green colourblind).</para>
        ///
        /// <para>NO NEW SCREEN: this is the same <see cref="EndStateView"/> template the victory
        /// takes, so the two exits cannot drift apart in shape or timing. It carries
        /// <see cref="HoldOnInteraction"/> for the same reason the victory does (WO-1543), and it
        /// keeps a guard rather than <c>AutoDismissSeconds = 0f</c>: a player stranded after a
        /// retreat is strictly worse than one who reads a screen too briefly.</para>
        ///
        /// <para>ACCOMMODATES WO-1526 (hero death capped at 2 stars) without deciding it - the
        /// stars and razed % are passed IN from the settled <c>RaidResult</c>, so whatever that
        /// lane settles is what this screen states.</para>
        /// </summary>
        /// <param name="reason"><see cref="RetreatReason"/> or <see cref="TimeoutReason"/>.</param>
        /// <param name="onReturn">The route home (the caller owns it; this screen only reports).</param>
        /// <param name="autoReturnSeconds">Anti-softlock guard, re-armed by interaction.</param>
        /// <param name="stars">Settled stars 0..3; &lt; 0 hides the rating row.</param>
        /// <param name="destructionPercent">Settled razed %, 0..100; &lt; 0 omits the sentence.</param>
        /// <param name="elapsedSeconds">Settled clock; &lt; 0 hides the time row.</param>
        /// <param name="credited">The MEASURED wallet delta - never the requested loot.</param>
        /// <param name="rewardShort">True when the wallet took less than the raid awarded.</param>
        /// <param name="troopsDeployed">Bodies committed to this raid (&lt; 0 = unknown, line omitted).</param>
        /// <param name="troopsSurvived">Bodies that walked off the field (&lt; 0 = unknown).</param>
        /// <param name="troopsLost">
        /// WO-1810 — troops this raid lost PERMANENTLY: the killed plus the share of the survivors
        /// the exit charged (<c>RaidCasualtyPolicy</c>). &lt; 0 = unknown, and the sentence then
        /// falls back to <paramref name="troopsDeployed"/> minus <paramref name="troopsSurvived"/>
        /// — the killed, who are dead either way — rather than printing a number it cannot prove.
        /// </param>
        public static EndStateVM FromRaidRetreat(string reason, Action onReturn,
            float autoReturnSeconds = 30f, int stars = -1, int destructionPercent = -1,
            float elapsedSeconds = -1f, ResourceCost credited = default(ResourceCost),
            bool rewardShort = false, int troopsDeployed = -1, int troopsSurvived = -1,
            int troopsLost = -1)
        {
            bool timedOut = string.Equals(reason, TimeoutReason, StringComparison.OrdinalIgnoreCase);

            // The lead sentence names WHICH exit, because the two feel different and a player who
            // ran out of clock did not choose to leave. Voice: plain, never scolding.
            // KEPT SHORT DELIBERATELY. This subtitle can carry FOUR facts (the lead, the razed %,
            // the wounded count and the bank-short caveat), and EndStateView compresses every band
            // when the stack outgrows the well - the "body rows COMPRESSED to fit" line
            // EndStateBodyFitRegression exists because of. A lead sentence that wraps would push
            // the stack to five rendered lines on a phone for no added meaning.
            string body = timedOut
                ? "The clock ran out - your warband falls back."
                : "You called the assault off.";
            if (destructionPercent >= 0)
                body += "\n" + destructionPercent + "% razed.";

            // TROOPS LOST, IN WORDS, WITH THE REASON FOLDED IN (WO-1810).
            //
            // ⚠ THIS LINE USED TO SAY "N troops return wounded", AND IT WAS TRUE UNTIL THIS TICKET.
            // RaidDeployController marked every deployed body that did not survive as WOUNDED with a
            // difficulty-scaled recovery, so "wounded" was the honest word. Owner ruling 2026-09-16:
            // "there is no cost to losing a raid" / "any troop killed is dead so 60% of whats left" -
            // the fallen are now REMOVED from the roster, and a non-victory exit also takes a share of
            // the survivors. So the word is "lost", and the sentence carries WHY, because a player who
            // has just lost real troops must not have to work out what charged them.
            //
            // ONE SENTENCE, REPLACING the old one rather than joining it: this subtitle can already
            // carry four facts (the lead, the razed %, this line and the bank-short caveat) and
            // EndStateView uniform-compresses every band past that (see the note above / the
            // EndStateBodyFitRegression contract). A fifth line would crush the whole stack.
            int lost = troopsLost;
            if (lost < 0 && troopsDeployed >= 0 && troopsSurvived >= 0)
                lost = troopsDeployed - troopsSurvived;      // the killed: dead under either model
            // lost < 0 = nothing measurable, so the line is OMITTED: a raid that never reconciled
            // must not print a zero it cannot prove (the same contract the -1 sentinels carry at
            // the caller).
            if (lost >= 0)
            {
                if (lost == 0)
                {
                    body += "\nEvery troop came home.";
                }
                else
                {
                    string noun = lost == 1 ? " troop lost" : " troops lost";
                    body += "\n" + lost + noun + (timedOut
                        ? " - the assault failed and the warband did not make it back."
                        : " - the cost of covering the retreat.");
                }
            }

            // OWNER RULING 2026-09-16 ~21:20 - A FAILED RAID PAYS NO LOOT, AND THE SCREEN SAYS SO
            // IN WORDS. With nothing credited the spoils grid is empty (AddSpoil suppresses zeros),
            // and an empty grid alone reads as "the screen is broken" rather than "you lost the
            // haul". One sentence, and only on the FAIL exit: a RETREAT that simply razed nothing
            // banked nothing for a different reason and must not be told the warband was lost.
            // It occupies the slot rewardShort would have used - a fail credits nothing, so the
            // bank-short caveat can never fire on the same screen, and the four-fact budget holds.
            if (timedOut && credited.IsZero)
                body += "\nNo spoils - the warband was lost.";

            if (rewardShort) body += "\n" + RewardShortSentence;

            var vm = new EndStateVM
            {
                // Defeat, not Victory: the emblem and trace tag must not congratulate a fall-back.
                Kind = EndStateKind.Defeat,
                Title = timedOut ? TimeoutTitle : RetreatTitle,
                Subtitle = body,
                Stars = stars >= 0 ? Mathf.Clamp(stars, 0, 3) : -1,
                TimeSeconds = elapsedSeconds >= 0f ? elapsedSeconds : -1f,
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconShield),
                PrimaryLabel = "Return to Castle",
                PrimaryRoute = "return-home",
                Primary = onReturn,
                AutoDismissSeconds = Mathf.Max(2f, autoReturnSeconds),
                HoldOnInteraction = true,
                // WO-1810 - the same count the sentence above states, bindable by a view that
                // grows a band for it (see the field's own note).
                TroopsLost = lost,
            };

            // SPOILS - the same five rows, the same order, the same AddSpoil suppression the
            // victory screen uses. A retreat that banked nothing draws no rows and advertises
            // nothing, which is the WO-978 honesty contract, not an oversight.
            AddSpoil(vm, "Wood", credited.Wood);
            AddSpoil(vm, "Iron", credited.Iron);
            AddSpoil(vm, FoodSpoilLabel, credited.Stone);
            AddSpoil(vm, "Gold", credited.Coins);
            AddSpoil(vm, "Crystals", credited.Crystals);

            FlowTrace.Step("EndState",
                "RAID NON-VICTORY RESULT composed (reason=" + (reason ?? "(null)") + "): stars=" + stars +
                " razed=" + destructionPercent + "% spoilRows=" + vm.Spoils.Count +
                " deployed=" + troopsDeployed + " survived=" + troopsSurvived +
                " lost=" + troopsLost + " short=" + rewardShort +
                ". Before WO-1561 this exit showed NO screen at all; before WO-1810 it reported the " +
                "fallen as WOUNDED, which is no longer what happens to them.");
            return vm;
        }

        /// <summary>
        /// The player-facing word for the FOOD balance on a spoils row.
        ///
        /// <para>WO-1374 sets this to "Food": PROGRAM_RAID_ECONOMY_2026-09-04 section 3 enumerates
        /// the five currencies as Wood / Iron / Food / Gold / Crystals, and that document is
        /// declared NORTH STAR and takes precedence over earlier rulings.</para>
        ///
        /// <para>IT CONTRADICTS THE LIVE HUD, AND THAT IS RECORDED HERE RATHER THAN BURIED,
        /// because it is an OWNER call and not an engineering one. Three surfaces label this same
        /// balance "Stone" today, all re-read at source 2026-09-04:
        /// <list type="bullet">
        ///   <item><description><c>HudKitController.cs:2190</c> - the town resource rail:
        ///     <c>names = { "Wood", "Iron", "Stone", "Crystals" }</c> against
        ///     <c>kinds = { Wood, Iron, Food, Crystal }</c>.</description></item>
        ///   <item><description><c>BuildWalletRow.cs:46</c> - the build-mode wallet tags.</description></item>
        ///   <item><description><c>DailyQuestHud.cs:407</c> - the daily-quest reward row.</description></item>
        /// </list>
        /// The retired line here said "Stone" for exactly that reason (WO-1163), so this row was
        /// CONSISTENT with the wallet even though the underlying <c>GameState.Stone</c> BALANCE
        /// was retired (GameState.cs:59, WO-1212). Two different things were retired at two
        /// different times: the balance, and - per canon section 7 - never the word.</para>
        ///
        /// <para>SO THE RISK IS REAL: until the three surfaces above move too, a raid pays
        /// "+3,000 Food" and the number the player then watches rise is labelled "Stone". The
        /// word is isolated in this ONE constant precisely so the owner's ruling is a one-word
        /// edit here plus three elsewhere, and so nobody has to re-derive the conflict.</para>
        /// </summary>
        // 2026-09-16 owner ruling ("we completely removed food"): the spoils row reads Stone, the
        // resource the number then watches rise. The internal field name is kept so the WO-1374
        // conflict note above stays findable; only the player-facing word changed.
        private const string FoodSpoilLabel = "Stone";

        /// <summary>
        /// Adds one spoils row when <paramref name="amount"/> is positive. Zero and negative are
        /// skipped: a raid that credited no iron must show no iron row, and a NEGATIVE would mean
        /// the measured wallet delta went backwards during the grant - which is a defect
        /// elsewhere, not a reward, so it is reported rather than drawn as "+-40".
        /// </summary>
        private static void AddSpoil(EndStateVM vm, string label, int amount)
        {
            if (vm == null) return;
            if (amount < 0)
            {
                FlowTrace.Warn("EndState",
                    "raid spoils: '" + label + "' credited a NEGATIVE delta (" + amount + ") - the wallet " +
                    "moved backwards across the grant. No row is drawn; the grant path is the defect, " +
                    "not this screen.");
                return;
            }
            if (amount == 0) return;
            vm.Spoils.Add(new SpoilRowVM
            {
                Label = label, Amount = "+" + ElarionUi.CompactNumber(amount),
            });
        }

        /// <summary>
        /// OUTPOST victory in the continuous-walk OuterWorld (replaces
        /// OutpostVictoryController's bespoke toast). Compact + non-blocking (no scrim):
        /// the hero KEEPS WALKING — there is no return/teleport (WO-449). The one action
        /// is a plain dismiss; AutoDismissSeconds clears it so it never lingers.
        /// </summary>
        public static EndStateVM FromOutpostVictory(string joinedCompanionName,
            bool newClaim, float autoDismissSeconds = 4f)
        {
            string body = !string.IsNullOrEmpty(joinedCompanionName)
                ? "The outpost is yours.\n" + joinedCompanionName + " joins your party."
                : (newClaim ? "The outpost is yours." : "Outpost already claimed.");

            return new EndStateVM
            {
                Kind = EndStateKind.Victory,
                Title = "Outpost Claimed",
                Subtitle = body,
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconCombat),
                // F8-43: no CTA on a compact banner — it auto-dismisses in seconds, so a
                // Continue button is a redundant control. Exit = auto-dismiss + tap-anywhere.
                PrimaryLabel = null,
                PrimaryRoute = "dismiss",
                AutoDismissSeconds = Mathf.Max(1f, autoDismissSeconds),
                Compact = true,
            };
        }

        /// <summary>Wave-clear RESULTS banner (replaces WaveCelebrationManager's IMGUI
        /// toast / prefab text): compact, non-blocking, auto-dismissing.
        ///
        /// OWNER FELT-TEST 2026-08-08 ("I'm not seeing rewards after waves. Shouldn't we have
        /// a rewards banner?"): this banner now leads with THE EARN BEAT — the resources the
        /// wave actually banked, read from <see cref="WaveManager.TryGetPayoutFor"/> and never
        /// re-derived. Wave rewards were paid all along (WaveManager.AwardWaveResources /
        /// AwardWaveCrystals) with no surface at all: every OnWaveCleared listener in the tree
        /// is persistence / quests / dialogue / tutorial / audio / pose, and the one
        /// presentation attempt (WaveManager.ShowRewardToast) reflected for
        /// "ShowBanner(string)" / "ShowToast(string)", neither of which VillageHudController
        /// declares — so it resolved null and no-opped silently, every wave, forever.
        ///
        /// Reward rows come FIRST (the beat the player is owed), then the damage report, both
        /// inside the ONE hard row budget (<see cref="CompactMaxSpoilRows"/>) this banner can
        /// seat without EndStateView.BuildBody having to compress every band.
        ///
        /// F8-45 (owner 2026-07-11): carries the DAMAGE REPORT — one spoils row per
        /// damaged/destroyed structure (worst-first; <see cref="WaveDamageReport.MaxRows"/>
        /// caps what is collected, the row budget caps what is SHOWN and the subtitle states
        /// the shortfall),
        /// with the IN-KIND MATERIALS repair cost (owner ruling 2026-07-11: damage
        /// fraction x the row's own catalog build cost; destroyed rows read "Rebuild"
        /// at the full build cost; crystals are never charged) where a
        /// WallRepairController exists to price it, and the production hit for damaged
        /// collectors (accrual scales with HP). State is carried by TEXT
        /// ("damaged 40%" / "DESTROYED" / a leading "+" on every earned amount), never color
        /// alone (colorblind law). A wave that paid nothing AND took no damage keeps today's
        /// 4s row-less banner, unchanged.</summary>
        public static EndStateVM FromWaveClear(int waveNumber)
        {
            var vm = new EndStateVM
            {
                Kind = EndStateKind.WaveResults,
                Title = $"Wave {waveNumber} Cleared",
                Subtitle = WaveCelebrationManager.Significance01(waveNumber) >= 1f
                    ? "A decisive defense. Review what changed before the next assault."
                    : "The realm holds. Review the result, then prepare the next defense.",
                Emblem = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconCombat),
                // F8-43: no CTA on a compact banner — it auto-dismisses in seconds, so a
                // Continue button is a redundant control. Exit = auto-dismiss + tap-anywhere.
                PrimaryLabel = $"Prepare for Wave {waveNumber + 1}",
                PrimaryRoute = "prepare-next-wave",
                AutoDismissSeconds = WaveCelebrationManager.Significance01(waveNumber) >= 1f ? 8f : 5f,
                Compact = false,
                HoldWorld = true,
            };

            // Damage report (model-side aggregation; this factory is the MVVM adapter).
            // COLLECTED FIRST, BUILT SECOND: the row budget below has to know how many damage
            // entries exist before it decides how many reward rows it may spend.
            var damage = Guard.Try<List<WaveDamageReport.Entry>>(
                "EndState", "wave " + waveNumber + " damage report",
                () => WaveDamageReport.Collect(), null);
            int damageAvailable = damage != null ? damage.Count : 0;

            // ── THE EARN BEAT (owner felt-test 2026-08-08: "I'm not seeing rewards after
            //    waves. Shouldn't we have a rewards banner?") ─────────────────────────────
            // Rewards were ALWAYS being paid (WaveManager.AwardWaveResources /
            // AwardWaveCrystals) and NOTHING rendered them. These rows read the integers
            // WaveManager BANKED (WaveManager.TryGetPayoutFor — keyed on this wave's id so a
            // previous wave's spoils can never leak in); the payout is a random roll, so a
            // re-derivation here would print numbers the wallet never received.
            // Guarded (§12): a malformed payout must never take the wave loop down with it.
            int rewardRows = 0;
            Guard.Try("EndState", "wave " + waveNumber + " reward rows",
                () => { rewardRows = AppendWavePayoutRows(vm, waveNumber, damageAvailable); });

            if (RewardedProgression.TryGetWaveUnlockFor(waveNumber, out string unlockedName))
            {
                vm.Spoils.Add(new SpoilRowVM
                {
                    Icon = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconInventory),
                    Label = "Plans Recovered",
                    Amount = unlockedName,
                    Rarity = 3,
                    // The amount is a BUILDING NAME, not a number — same wide grammar as the
                    // damage rows (owner F8 2026-09-02).
                    Wide = true,
                });
                rewardRows++;
            }

            // Damage rows fill whatever the reward rows left of the compact budget. When any
            // damage exists the reward side is capped at MaxRows-1, so this is always >= 1.
            int damageBudget = vm.Compact
                ? Mathf.Max(0, CompactMaxSpoilRows - rewardRows)
                : damageAvailable;
            int damageRows = Mathf.Min(damageAvailable, damageBudget);
            for (int i = 0; i < damageRows; i++)
            {
                var e = damage[i];
                if (e == null) continue;
                int pct = Mathf.Clamp(Mathf.RoundToInt(e.DamageFraction * 100f), 1, 100);
                string state;
                if (e.Destroyed)
                    state = e.IsCollector && e.LootStolen > 0
                        // WO-697: currency counts through the ONE kit formatter.
                        ? $"DESTROYED, looted {ElarionUi.CompactNumber(e.LootStolen)}"
                        : "DESTROYED";
                else
                    state = e.IsCollector
                        ? $"damaged {pct}%, production -{pct}%"  // economy hit = HP-scaled accrual
                        : $"damaged {pct}%";
                vm.Spoils.Add(new SpoilRowVM
                {
                    Icon = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconShield),
                    // Plain hyphen (not em-dash) in PLAYER-FACING copy: the build font has a
                    // tofu precedent (EndStateView.BuildStarRow) and canon strings use " - ".
                    Label = $"{e.Name} - {state}",
                    // In-kind materials cost only where the live controller priced it
                    // (no controller — omit rather than fake). Destroyed rows read
                    // "Rebuild" (full build cost); damaged rows read "Repair"
                    // (owner ruling 2026-07-11 — crystals never appear here).
                    Amount = e.HasCost && !WallRepairController.MaterialsZero(e.RepairCost)
                        ? (e.Destroyed ? "Rebuild " : "Repair ") +
                          WallRepairController.DescribeMaterials(e.RepairCost)
                        : string.Empty,
                    // A damage line is a sentence + a materials cost, never a resource row —
                    // owner F8 2026-09-02. See SpoilRowVM.Wide.
                    Wide = true,
                });
            }

            // The subtitle is the ONE place the banner's overall verdict is stated, and it is
            // now keyed on DAMAGE ROWS, not on Spoils.Count — a reward-only banner previously
            // would have inherited "it took damage" simply because it had rows.
            // Never colour-only (colourblind law): the verdict is the words themselves.
            if (damageRows > 0)
            {
                vm.Subtitle = "The realm holds - but it took damage.";
                vm.AutoDismissSeconds = 8f;   // a report needs reading time
            }
            else if (rewardRows > 0)
            {
                vm.Subtitle = "The realm holds. Spoils claimed.";
                vm.AutoDismissSeconds = 6f;
            }

            // TRUNCATION IS STATED, NEVER SILENT. WaveDamageReport hands back up to MaxRows (8)
            // entries; the compact banner can legibly seat 4 rows TOTAL (see the budget note on
            // CompactMaxSpoilRows). Dropping the tail without saying so would read as "those
            // buildings are fine". An explicit '\n' segment is used because EndStateView's
            // SubtitleLines measures each segment independently — the panel solve therefore
            // BUDGETS this second line instead of discovering it after layout.
            if (damageAvailable > damageRows)
                vm.Subtitle += "\nShowing " + damageRows + " of " + damageAvailable + " damaged structures.";

            if (damageRows > 0)
            {
                // WO-672 Slice E (owner 2026-07-11: "i saw a damage report but could not
                // repair"): the CTA seat returns for THIS one case — "Repair All" wired to
                // WallRepairController.RepairAll (the one wallet/repair authority; this
                // factory is the model-side adapter, same seam as the icon resolution
                // above). Priced in IN-KIND MATERIALS summed per resource (owner ruling
                // 2026-07-11 — crystals never charged). Unaffordable renders
                // disabled-with-cost (informative, not dead). Copy uses plain hyphens
                // only: the build font has a tofu precedent (see the Label note above),
                // so no ◈/— glyphs in player copy.
                var repair = UnityEngine.Object.FindFirstObjectByType<WallRepairController>();
                var cost = repair != null
                    ? repair.RepairAllCost() : default(DeNelle.Core.Catalog.ResourceCost);
                if (repair != null && !WallRepairController.MaterialsZero(cost))
                {
                    vm.CtaLabel = "Repair All - " + WallRepairController.DescribeMaterials(cost);
                    vm.CtaEnabled = repair.CanAffordMaterials(cost);
                    vm.CtaRoute = "repair-all";
                    // RepairAll raises FeedbackShown (the existing HUD toast) with the
                    // repaired-summary; the banner itself dismisses after firing.
                    vm.Cta = () => { if (repair != null) repair.RepairAll(); };
                    // A decision now sits on the banner — extend the reading window.
                    vm.AutoDismissSeconds = 10f;
                }
            }
            return vm;
        }

        // ── THE COMPACT BANNER'S ROW BUDGET (derived, not picked) ─────────────────────
        //
        // ⚠ THE ARITHMETIC BELOW IS FROZEN AT ITS 2026-08-08 CONSTANTS. Two of them moved on
        // 2026-09-02 (owner F8 "spacing tight and ..."): EndStateView.BandGapPx 8 -> 18, and a
        // new 16px SpoilsLeadGapPx band sits between the copy and the spoils grid. Re-solving
        // the L=2 worst case with those: 64 + 120 + 16 + 64R + 18(R+2) <= 504 -> R <= 3.6, so a
        // strict re-derivation would read THREE. It is deliberately NOT lowered here, because
        // this ceiling only ever binds a COMPACT banner (see the `vm.Compact ?` tests at the two
        // call sites) and the wave-clear screen this WO is about runs as the FULL modal, whose
        // panel SOLVES to its content instead of clamping. Read this as the shape of the
        // reasoning, not as live numbers — and re-derive from EndStateView at source before
        // trusting any figure in it.
        //
        // Canon issue #28 is real and it fires on THIS template: EndStateView.BuildBody
        // uniform-compresses every band when the content is taller than the body well, and
        // logs "body rows COMPRESSED to fit". A wave banner that quietly crushed itself would
        // be a worse defect than the missing rewards it is fixing, so the ceiling is computed
        // here rather than discovered on device.
        //
        // THE ARITHMETIC (all constants read at source, this session):
        //   Compact banner frame  = y 0.56 .. 0.86, grows DOWN, capped at y1-0.08 = 0.78
        //                           of screen height          (EndStateView.Show / the
        //                           compact extension block)
        //   Body well             = 0.075 .. 0.745 of the panel = 0.670 of it
        //                           (ElarionUiKit ZonesFor FrameCore z.body.y = 0.075, top
        //                           clamped to 0.745 by EndStateView's compact branch — with
        //                           the dead close band reclaimed, see EndStateView)
        //   Post-scale canvas H   = 965 ref px on BOTH the owner's 2670x1200 desktop and a
        //                           2400x1080 Seeker (CanvasScaler match 0.5 -> the same
        //                           1.24 / 1.12 scale factors land within 1px of each other)
        //   => body well AT THE CLAMP = 0.670 x 0.78 x 965 = ~504 ref px
        //
        //   Band costs (EndStateView): Emblem 64, Subtitle 60 PER WRAPPED LINE, Row 64,
        //   plus an 8px gap BETWEEN bands.
        //   Wave banner BANDS = emblem(1) + subtitle(1 band, L wrapped lines) + R rows,
        //   so n = R + 2 bands and (n-1) = R + 1 gaps:
        //       need = 64 + 60L + 64R + 8(R + 1)
        //   Solving need <= 504:
        //       L=1 -> R <= 5.3     L=2 -> R <= 4.4
        //   So FOUR rows is the largest count that survives BOTH a one-line and a two-line
        //   subtitle (worst case L=2, R=4: 64 + 120 + 256 + 40 = 480 <= 504, 24px spare).
        //
        // WHAT THIS REPLACED: WaveDamageReport hands back up to MaxRows = 8 entries and the
        // old code rendered every one. Eight rows demand 64 + 60 + 512 + 72 = 708 px into a
        // well that could not exceed 504 -> scale 0.71, i.e. the F8-35 class the extension
        // block itself was written to avoid. The cap is therefore a FIX to the pre-existing
        // damage path as well as the budget for the new reward rows, and the shortfall is
        // stated in the subtitle instead of silently dropped.
        /// <summary>Hard ceiling on spoils ROWS for the compact wave-clear banner. See the
        /// derivation above — four rows is what the banner can seat at its growth clamp
        /// without BuildBody having to compress every band below its own content size.</summary>
        private const int CompactMaxSpoilRows = 4;

        /// <summary>
        /// Appends the wave's EARNED-RESOURCE rows and returns how many it added.
        ///
        /// SOURCE OF TRUTH: <see cref="WaveManager.TryGetPayoutFor"/> — the integers the wave
        /// loop actually banked, keyed on <paramref name="waveNumber"/>. Returns 0 when this
        /// wave paid nothing (the staggered WO-361 intervals mean many waves legitimately
        /// don't), and the banner then reads exactly as it does today.
        ///
        /// ROW SHAPE: one row per resource while they fit — short label left, short "+N"
        /// right, which is the column split BuildSpoilRow is built for (label gets ~0.62 of
        /// the plate, the amount ~0.34). When the payout has MORE lines than the budget
        /// allows, the TAIL folds into a single combined row rather than being dropped: no
        /// earned resource ever goes unmentioned, and no cell ever carries a string long
        /// enough to be shrunk past the font floor.
        ///
        /// Icons are deliberately left NULL: EndStateView.BuildSpoilRow then resolves the
        /// CONCEPT icon from the row's own label ("wood"/"iron"/"food"/"crystals" are all
        /// keyed in concept-icons.json) — the same data-decides path FromBattleVictory's Iron
        /// row and FromRaidVictory's loot rows use, and the reason those rows render real
        /// currency PNGs instead of a placeholder square.
        /// </summary>
        private static int AppendWavePayoutRows(EndStateVM vm, int waveNumber, int damageAvailable)
        {
            if (vm == null) return 0;
            if (!WaveManager.TryGetPayoutFor(waveNumber, out var pay))
            {
                FlowTrace.Step("EndState",
                    $"wave {waveNumber} clear banner: no payout recorded for this wave " +
                    "(staggered intervals - nothing was due), reward rows omitted.");
                return 0;
            }

            var lines = new List<KeyValuePair<string, int>>(4);
            if (pay.Wood     > 0) lines.Add(new KeyValuePair<string, int>("Wood",     pay.Wood));
            if (pay.Iron     > 0) lines.Add(new KeyValuePair<string, int>("Iron",     pay.Iron));
            if (pay.Stone     > 0) lines.Add(new KeyValuePair<string, int>(FoodSpoilLabel, pay.Stone));
            if (pay.Crystals > 0) lines.Add(new KeyValuePair<string, int>("Crystals", pay.Crystals));
            if (lines.Count == 0) return 0;

            // THE SPLIT: when the wave also took damage, the damage list keeps at least one
            // row, so the reward side may claim at most CompactMaxSpoilRows-1. A clean wave
            // gives the whole budget to the spoils.
            int budget = Mathf.Max(1, damageAvailable > 0 ? CompactMaxSpoilRows - 1 : CompactMaxSpoilRows);

            if (lines.Count <= budget)
            {
                foreach (var l in lines) vm.Spoils.Add(ResourceRow(l.Key, l.Value));
                FlowTrace.Step("EndState",
                    $"wave {waveNumber} clear banner: {lines.Count} reward row(s) from the BANKED payout " +
                    $"(wood={pay.Wood} iron={pay.Iron} food={pay.Stone} crystals={pay.Crystals}), " +
                    $"budget={budget} of {CompactMaxSpoilRows}.");
                return lines.Count;
            }

            // Overflow (reachable: a live ServerConfig event pays crystals EVERY wave, so a
            // wave divisible by 2/3/4 with damage hits four resource lines against a budget of
            // three). Emit the first budget-1 rows individually, then ONE combined tail row.
            for (int i = 0; i < budget - 1; i++)
                vm.Spoils.Add(ResourceRow(lines[i].Key, lines[i].Value));

            var tailLabel = new System.Text.StringBuilder();
            var tailAmount = new System.Text.StringBuilder();
            for (int i = budget - 1; i < lines.Count; i++)
            {
                if (tailLabel.Length > 0) { tailLabel.Append(" + "); tailAmount.Append(", "); }
                tailLabel.Append(lines[i].Key);
                tailAmount.Append('+').Append(ElarionUi.CompactNumber(lines[i].Value));
            }
            vm.Spoils.Add(new SpoilRowVM
            {
                // The combined label ("Food + Crystals") resolves no single concept, and the chest
                // is the right stand-in for "mixed spoils" — but it must now be asked for HERE.
                // OWNER F8 2026-09-02 defect #3: EndStateView's generic kit fallback IS the chest,
                // and it was painting a MONEY BAG on structure-DAMAGE rows, which state the
                // opposite of loot. That fallback is no longer offered to a Wide row (see
                // EndStateView.ResolveRowIcon), so this row — a Wide row that genuinely wants it —
                // names it explicitly instead of inheriting it. Model-side, like every other icon
                // decision in this file.
                Icon = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconInventory),
                Label = tailLabel.ToString(),
                Amount = tailAmount.ToString(),
                // "Food + Crystals" / "+80, +12" is a COMBINED line - two resources' worth of
                // string in one cell. It takes a full band like the other wide rows.
                Wide = true,
            });
            FlowTrace.Step("EndState",
                $"wave {waveNumber} clear banner: {lines.Count} paid resources folded into {budget} row(s) " +
                $"(budget {budget} of {CompactMaxSpoilRows}, damage entries={damageAvailable}) - the tail is " +
                "COMBINED, never dropped.");
            return budget;
        }

        /// <summary>One earned-resource row. ASCII only, and the "+" prefix (not colour) is
        /// what marks it as a gain — the damage rows on the same banner carry no "+".</summary>
        private static SpoilRowVM ResourceRow(string label, int amount)
        {
            return new SpoilRowVM
            {
                // WO-697: every reward number renders through the ONE kit formatter.
                Label = label, Amount = "+" + ElarionUi.CompactNumber(amount),
            };
        }
    }
}
