using UnityEngine;

namespace DeNelle.Core
{
    /// <summary>
    /// Central demo/web feature gate. THE DEMO LAW: a reachable feature must either WORK or be
    /// HIDDEN — a broken-but-visible feature is worse than an absent one, especially on the WEB
    /// build (the grant-demo target). Features default to their *proven* state; anything not
    /// verified end-to-end ships OFF and is flipped ON only once it's confirmed ("unflag when proven").
    ///
    /// Each entry point checks the matching flag before spawning/binding/opening. To test a gated
    /// feature without a rebuild, set PlayerPrefs "ff.&lt;name&gt;" to 1 (on) or 0 (off); -1/absent
    /// uses the default below.
    ///
    /// Status set by the 2026-06-16 demo-readiness audit:
    ///   RAID  = ON  — the core loop is closed: RaidVictoryController now subscribes to
    ///           RaidGarrisonSpawner.OnCleared and runs victory -> CLAIM (RaidClaimService +
    ///           SceneOwnership flip player-owned) -> NEXT COMPANION (AddToParty) -> RETURN
    ///           (victory banner + GoCastle, with an auto-return safety timer), so a cleared
    ///           raid no longer soft-locks. The full WO-431 star-scoring/reward SCREEN and the
    ///           WO-441 Phase-C auto-harvest outpost are follow-ups layered on this spine.
    ///   ARENA = ON  — full loop verified (enter→fight→win/lose→reward→return); SKR wallet is an
    ///           intentional client-side MVP stub. Demo-ready.
    /// </summary>
    public static class FeatureFlags
    {
        public static bool Raid  => Get("raid",  defaultOn: true);

        /// <summary>V1 DESCOPE (2026-06-26): the isolated battle Arena is CUT from the V1 build —
        /// V1 is the farm-&gt;build-&gt;level-&gt;raid loop (base = Village2, raid = walk-to EnemyOutpost),
        /// so the Arena entry/reward path ships OFF. Default flipped to false (was true). The Arena
        /// code is gated, NOT deleted — re-enable for V2 via PlayerPrefs "ff.arena" = 1.</summary>
        public static bool Arena => Get("arena", defaultOn: false);

        /// <summary>PIVOT (owner 2026-06-22): SINGLE-HERO combat. When ON, the ATB battle party is
        /// JUST the hero — no pets/companions are surfaced as combatants (see
        /// <see cref="DeNelle.BattleATB.BattleController"/> BuildParty). Companions were a net negative
        /// (they didn't heal/engage), so the direction is one hero + a wider skill tree (heal + ranged).
        /// Default ON. Flag-gated so the hero+pets party is reversible: PlayerPrefs "ff.singlehero" = 0.</summary>
        public static bool SingleHero => Get("singlehero", defaultOn: true);

        // EYES-SWEEP 2026-07-06: ff.herotalents REMOVED (was the OWNER 2026-07-04 consolidation
        // shim). A stale PlayerPrefs "ff.herotalents"=1 re-armed the dead legacy HeroTalents route
        // and the capture fleet rendered panel_HeroTalents fully black. The consolidation is now
        // unconditional: every talents entry point routes to PanelId.HeroSkillTree.

        /// <summary>PIVOT (owner 2026-06-22): Blink armor is JUNKED. When OFF (default), HeroArmorVisual
        /// is inert — no addressable armored-body swap, no rig bone-mapping (which spammed
        /// "ShareBaseSkeleton FAILED" in the F8 logs), and the owner dislikes the look. The hero keeps
        /// its base body; armor/cosmetics move to the Tripo self-rigged direction. Flip ON to restore the
        /// Blink armor swap: PlayerPrefs "ff.blinkarmor" = 1.</summary>
        public static bool BlinkArmor => Get("blinkarmor", defaultOn: false);

        /// <summary>PIVOT (owner 2026-06-22): lock to ONE polished hero (Knight) — do it well, then
        /// fold in the other classes. RETIRED as the default 2026-08-05: this now defaults OFF, so
        /// the playable roster is Knight/Ranger/Mage (see DeNelle.Core.State.PlayableHeroes) and
        /// GameStateService.ChooseHero no longer coerces a Ranger/Mage pick to Knight. Set
        /// PlayerPrefs "ff.knightonly" = 1 to restore the solo-Knight V1 pivot.</summary>
        // OWNER RULING 2026-08-05: Mage + Ranger are UNLOCKED. WO-861 Phase 0 (e1ae9403) built
        // the single-source-of-truth registry (DeNelle.Core.State.PlayableHeroes) precisely so
        // this one flag widens EVERY consumer at once - the select screen's lock state,
        // GameStateService.ChooseHero's coercion, and VendorStockResolver's shelf roster - with
        // no three-site edit and no drift. Phase 0 deliberately shipped with the flag still ON
        // (its own header says "TODAY'S BEHAVIOUR IS UNCHANGED"), so the plumbing landed and the
        // unlock did not. This is that unlock. The flag-OFF roster is Knight/Ranger/Mage;
        // CLERIC STAYS OUT deliberately (PlayableHeroes.cs:20-26) - it has no authored kit.
        // Set PlayerPrefs "ff.knightonly" = 1 to restore the solo-Knight V1 pivot.
        public static bool KnightOnly => Get("knightonly", defaultOn: false);

        /// <summary>PIVOT (owner 2026-06-22): base-building / CoC base-defense is GATED OFF — NOT on the
        /// V1 critical path. Polish the solo-hero OFFENSE loop first; revisit base-building only if the
        /// polished loop shows it would add value. This gates: convert-on-clear (WO-475 "cleared outpost
        /// -> your base" — base CREATION), the troop auto-defense + watch/continue raid-on-base event, and
        /// any build-a-base UI. V1 outpost reward = skill points / gear (NOT a base). Existing barracks /
        /// WaveManager / towers / GarrisonController stay dormant behind this. Flip ON when V2 is greenlit:
        /// PlayerPrefs "ff.basebuilding" = 1. See docs/COMBAT_PIVOT_NORTHSTAR.md.</summary>
        public static bool BaseBuilding => Get("basebuilding", defaultOn: false);

        /// <summary>WO-612 (owner 2026-07-06): CoC-style construction timers on structure placement —
        /// wires the existing WO-172 BuildTimerService into BuildModeController.Place (15 s base,
        /// 2 free slots, offline-fair). Timer is pacing, never a wall: no free slot = instant build.
        /// Growth path = option-3 "free income" (rewarded-ad skip, no real player cost) — those hooks
        /// exist in the service but stay unsurfaced. Default ON; PlayerPrefs "ff.buildtimers" = 0 to
        /// restore instant builds.</summary>
        public static bool BuildTimers => Get("buildtimers", defaultOn: true);

        /// <summary>WO-449 — when ON, the raid loop IS the continuous distance-gated WALK: the raid
        /// target is a live EnemyOutpost spawned in the merged world (~70m out a gate), the hero walks
        /// to it on one continuous NavMesh, combat triggers on approach (Enemy hero-aggro), and clearing
        /// it claims the base + grants the next companion IN PLACE — there is NO DEPLOY screen and NO
        /// teleport (the hero never leaves the open world). When OFF, the legacy
        /// RaidSelectionScreen -> RaidDeployScreen -> SceneRouter.GoRaid teleport path is restored
        /// verbatim (the raid icon opens the selection screen; RaidOutpostSystem does not spawn the
        /// walk-to outpost). Default ON. PlayerPrefs "ff.raidwalk".</summary>
        // WO-771 LOCKED (2026-07-26): raid loop = Teleport/Deploy, NOT walk-to. raidwalk OFF makes the
        // RaidSelectionScreen deploy loop the default from the raid icon (RaidEntryBridge routes to
        // PingNearestRaidOutpost only when this is ON). ff.raidwalk=1 restores the old walk-to path.
        public static bool RaidContinuousWalk => Get("raidwalk", defaultOn: false);

        /// <summary>TEST ONLY (owner ask 2026-08-16). Opens the raid selection grid even when the
        /// army is not full. The full-army gate (RaidSelectionScreen: deployable + queued >= cap,
        /// cap 10) is CORRECT product behaviour and remains the default — but it means roughly ten
        /// training jobs before the raid grid opens at all, which makes the raid pillar impossible
        /// to felt-test in one sitting. Default OFF, so shipping behaviour is untouched; the bypass
        /// logs a FlowTrace.Warn every time so a bypassed gate can never read as a passed one in a
        /// capture. PlayerPrefs "ff.raidtest".</summary>
        public static bool RaidTestBypassArmyGate => Get("raidtest", defaultOn: false);

        /// <summary>When ON (default), only the family REP/leader roams the overworld; the full
        /// family (leader + followers) spawns in the BattleArena on engage from the recipe carried
        /// by RepEngageWatcher.Init. Owner 2026-07-10: perf — bounded roaming agents (the overworld
        /// followers were redundant; the arena rebuilds the family regardless). OFF = legacy
        /// full-family roam. PlayerPrefs "ff.overworldleaderonlyroam".</summary>
        public static bool OverworldLeaderOnlyRoam => Get("overworldleaderonlyroam", defaultOn: true);

        /// <summary>When OFF, the "Travel to &lt;outpost&gt;" confirm-to-cross prompt on garrison /
        /// raid-outpost seams (<see cref="DeNelle.Village.World.SceneTransitionTrigger"/> whose target is a
        /// <c>Garrison_*</c> / <c>Outpost_*</c> / <c>RaidBase_*</c> scene) is SUPPRESSED — the player can NOT
        /// fast-travel to an outpost area; reaching it must be earned by walking (the WO-453 distance-gated
        /// region vision). The castle hub transitions are NOT outpost destinations and are never
        /// gated by this flag. Default OFF (owner 2026-06-19: "i dont want that as a fast travel option, at
        /// least not yet"). Flip ON via PlayerPrefs "ff.outposttravel" = 1 to restore the travel prompt.</summary>
        public static bool OutpostTravel => Get("outposttravel", defaultOn: false);

        /// <summary>When ON, our decorative CHROME (gilt inner-rim / bottom rule / header shadow+rule /
        /// niche backings + per-panel solid fills + glows) does NOT render, so the Blink "Obsidian" panel
        /// sprite + functional content (text/rows/grid/buttons) show clean. Content/structure and the
        /// world-occluding backdrops are never hidden. Default OFF (current look). PlayerPrefs
        /// "ff.blinkchrome". Gated in ElarionUiKit + per-panel (memory ui-chrome-composition-and-blink-flag).</summary>
        public static bool BlinkChrome => Get("blinkchrome", defaultOn: false);

        /// <summary>WO-443 — when ON, the WebGL build streams its diagnostic logs (FlowTrace +
        /// Unity errors/exceptions) to the backend remote-trace sink (<see cref="DeNelle.Core.Diagnostics.WebTrace"/>)
        /// so a real web player's issue can be triaged from the DB. Default OFF (don't spam the DB).
        /// PlayerPrefs "ff.webtrace". Can also be flipped ON for ONE session via the WebGL URL
        /// query-param <c>?trace=1</c> (see <see cref="ApplyUrlActivationOnce"/>) so support can turn it
        /// on without a rebuild. The sink itself is a clean no-op on standalone/editor and stays dormant
        /// until a backend endpoint is configured.</summary>
        public static bool WebTrace => Get("webtrace", defaultOn: true);

        /// <summary>When ON, tapping an upgradable building opens the code-built MVVM
        /// <c>BuildingUpgradePanelMvvm</c> — the Warcraft-3-style ENHANCEMENT perk grid
        /// (tap a tile to unlock a tier/perk; owner redo 2026-07-02). Presentation only —
        /// the unlock math (BuildingUpgradeService / ResourceBuildingState) is unchanged.
        /// Default ON. PlayerPrefs "ff.buildingupgradepanel". The legacy UIDocument twin was
        /// DELETED 2026-07-02 (audit §3.1); OFF is now a kill-switch (no panel spawns).</summary>
        public static bool BuildingUpgradePanel => Get("buildingupgradepanel", defaultOn: true);

        /// <summary>When ON, opening a weapon/armor shop opens the native code-built MVVM
        /// <c>PartyShopPanelMvvm</c> (party-member selector + tap-to-filter + unified single-tap
        /// buy/equip/sell + real item images + stat/buff deltas). Presentation + transaction routing
        /// through the proven IEconomy / IInventoryStore / IEquipTarget seams; the catalog + equip
        /// math is unchanged. Default ON. PlayerPrefs "ff.partyshop".
        /// <para>⚠ THIS FLAG IS NOW A KILL-SWITCH, NOT A CHOOSER (corrected 2026-09-06, WO-1430).
        /// The legacy <c>ShopPanel</c> twin was DELETED, so OFF means NO gear shop spawns at all —
        /// <c>PartyShopPanelMvvmBootstrap</c> is the only spawner and it returns early when OFF, and
        /// <c>PanelId.PartyShop</c> then has no registrant. Same shape as
        /// <see cref="BuildingUpgradePanel"/> after its UIDocument twin was deleted.</para>
        /// <para>⛔ THE OLD TEXT HERE WAS FALSE AND IS RETIRED. It read "Default OFF ... CmdOpenShop
        /// routes to PanelRouter→PartyShop only when ON (legacy ShopPanel path when OFF)". BOTH
        /// halves were wrong at the time of writing: the flag has always read
        /// <c>defaultOn: true</c> on the very next line, and <c>DialogueCommandSink</c>'s "OpenShop"
        /// verb routes to <c>PanelId.PartyShop</c> UNCONDITIONALLY (there is no flag branch, and
        /// <c>DialogueService</c>'s shop route likewise). That stale sentence is what made ShopPanel
        /// look reachable, which is how a doorless panel survived in the tree until
        /// <c>PanelDoorRegression</c> asked. A hand-written claim about what a flag branch does is
        /// duplicated state; read the routing site, not this comment.</para></summary>
        public static bool PartyShop => Get("partyshop", defaultOn: true);

        /// <summary>WO-455 / WO-557 — dialogue runs through OUR code-built system (DeNelle.Core.Dialogue:
        /// data-driven nodes + DialogueRunner + MVVM DialogueView styled via ElarionUiKit). Lifecycle WE
        /// control = no "No node" race, no Stop()-teardown NRE. Default ON (WO-557): YarnSpinner is FULLY
        /// REMOVED — there is no longer a legacy path to fall back to, so the custom sink/View MUST register.
        /// PlayerPrefs "ff.customdialogue".</summary>
        public static bool CustomDialogue => Get("customdialogue", defaultOn: true);

        /// <summary>WO-482 — when ON, the overworld touch-encounter loop is live: wandering enemy "rep" mobs
        /// in the open world that aggro/chase (chase-music sting, wide leash, ~+5% player speed) and, on
        /// engage, transition into an ISOLATED real-time battle arena (the generic <c>BattleArena</c>) where the
        /// single hero (Knight) fights the full Tripo orc family in an OPEN kite arena, then returns to where you
        /// were. Separate from ATB (its own system). Default OFF until the vertical is felt-verified
        /// ("unflag when proven"). PlayerPrefs "ff.overworldencounter". Spec: WORK_ORDER_482. See
        /// docs/COMBAT_PIVOT_NORTHSTAR.md + memory overworld-encounter-isolated-battle.</summary>
        public static bool OverworldEncounter => Get("overworldencounter", defaultOn: true);  // owner REVERSAL 2026-07-30 (F8 seq511 "missing enemies in the world") of the WO-771 2026-07-26 lock-OFF — wandering reps + engage->BattleArena are live again alongside the Teleport/Deploy raid loop; ff.overworldencounter=0 restores the quiet world.

        /// <summary>Ambient overworld roamers (RegionMobSpawner's live wandering-mob population that
        /// tops up around the player as they walk regions). Default OFF per owner 2026-07-26 (WWCD +
        /// "no idea how I got here"): regions stay peaceful until the player picks a fight, so the
        /// surprise overworld combat can't happen out of the box. When OFF the spawner does nothing
        /// (top-level enable gate only — all internals intact). Reversible: PlayerPrefs
        /// "ff.regionroam" = 1 restores the roaming population.</summary>
        public static bool RegionRoam => Get("regionroam", defaultOn: true);   // owner REVERSAL 2026-07-30 F8 seq511 ("missing enemies in the world") — regions live again; ff.regionroam=0 restores the peaceful preview

        /// <summary>WO-473 / PIVOT (owner 2026-06-22): SINGLE-HERO V1 onboarding has NO pet step. When ON
        /// (default), the intro flow skips the PetSelect screen entirely — after the hero pick (Title in-flow
        /// pick OR HeroSelect confirm) the player routes STRAIGHT to the castle (MainCastle_Hall). Hero-pick
        /// persistence (<c>svc.ChooseHero</c>) is preserved; PetSelect persists nothing since the 2026-06-13
        /// pet-acquisition rework (real bonding moved to the in-town Echo Hollow), so bypassing it loses
        /// nothing. The PetSelect scene/controller/router const stay INTACT — flip OFF to restore the
        /// Title/HeroSelect -> PetSelect -> Castle step: PlayerPrefs "ff.bypasspetselect" = 0.</summary>
        public static bool BypassPetSelect => Get("bypasspetselect", defaultOn: true);

        /// <summary>WO-467 runtime variant (owner 2026-06-23, "world seam still broken" x3): when ON,
        /// <c>RuntimeRegionGate</c> self-bootstraps on a hub scene and BUILDS crossing infrastructure
        /// from the <c>region-gates.json</c> recipe AT RUNTIME — a walkable approach deck welded to the
        /// source navmesh (runtime <c>NavMeshSurface</c> re-bake, NO editor bake), a deck-seated
        /// <c>SceneTransitionTrigger</c> masked-warp for the hero, a GUID-keyed <c>HeroLinkCrossing</c> entry/dest
        /// pair, gate-funnel choke panels, and a narrow cross-scene
        /// <c>NavMeshLink</c> for AI. No scene hand-edit, no stale baked coord. Default OFF (2026-07-04):
        /// SUPERSEDED by merged-world (ff.mergedworld ON) — the seam infrastructure is now DEAD CODE pending
        /// removal. Flip to PlayerPrefs "ff.runtimeworldseam" = 1 only to test legacy two-scene seam during
        /// due-diligence unwiring. Spec: WORK_ORDER_467 §"Runtime auto-seam".</summary>
        public static bool RuntimeWorldSeam => Get("runtimeworldseam", defaultOn: false);

        /// <summary>WO-491 — when ON (default), low-HP orcs drive the <c>Injured</c> wounded-stance
        /// locomotion (ActorAnimator.SetInjured from Enemy.DriveAnimator below the HP cutoff). This is
        /// the slide-fix-adjacent locomotion polish; the slide fix itself (Speed param + walk state) is
        /// in the rebuilt controller and needs no flag. Default ON; PlayerPrefs "ff.enemyinjured" = 0
        /// to disable the wounded swap (the orc keeps the healthy locomotion at all HP).</summary>
        public static bool EnemyInjuredStance => Get("enemyinjured", defaultOn: true);

        /// <summary>WO-493 #5 / WO-497 — when ON (default), the HERO reads a wounded look + feel below the
        /// low-HP cutoff (~30%): ActorAnimator.SetInjured drives the Injured locomotion swap on the hero
        /// body, a screen-edge red vignette pulses (HeroInjuredVignette), an optional heartbeat cue plays,
        /// and the hero moves slightly slower (HeroHealth.MoveSpeedMultiplier). Restored on heal above the
        /// cutoff. Mirrors the EnemyInjuredStance polish on the enemy half. Default ON; PlayerPrefs
        /// "ff.heroinjured" = 0 to disable (the hero keeps the healthy stance/speed at all HP).</summary>
        public static bool HeroInjuredStance => Get("heroinjured", defaultOn: true);

        /// <summary>WO-491 — when ON (default), an enemy's RANGED/cast attack is ROOTED + telegraphed:
        /// the NavMeshAgent stops for the cast window (the caster commits, does not slide while casting)
        /// and a WindUp -> Cast animation + audio charge cue play so the strike is readable/dodgeable.
        /// When OFF the legacy instant ranged hit (no root, no telegraph) is restored. Default ON;
        /// PlayerPrefs "ff.enemyrootedcast" = 0 to disable.</summary>
        public static bool EnemyRootedCast => Get("enemyrootedcast", defaultOn: true);

        /// <summary>WO (enemy structure awareness) — DATA-PROVEN fix (headless [Flow:EnemyAggro] run:
        /// wave / overworld-rep enemies spawn with NO EnemyBrain, so their ONLY structure-targeting was
        /// <c>Enemy.ProbeForStructure</c>'s forward-only SphereCast, which missed ~99.7% — they march to
        /// the Heart / roam past defences instead of attacking them). When ON (default),
        /// <see cref="DeNelle.Village.Enemy"/> ALSO runs a short all-direction sweep that lets a brain-less
        /// enemy lock + attack a nearby live structure (a side tower/wall, or the Heart tree) it would
        /// otherwise walk straight past. HERO-PRIMARY is preserved: the sweep is suppressed while the hero
        /// is within aggro range (the verified hero-chase path wins) and it never targets the hero. When
        /// OFF, the exact legacy forward-only probe runs (fully reversible, no rebuild). PlayerPrefs
        /// "ff.enemystructureaware".</summary>
        public static bool EnemyStructureAwareness => Get("enemystructureaware", defaultOn: true);

        /// <summary>ENEMY WEAPONS-IN-HANDS (owner F8 2026-07-04: "enemies spamming weapons in all sorts of
        /// odd ways — maybe we not add a weapon unless we perfect one"). Gates <c>EnemyFactory.AttachEnemyWeapon</c>
        /// (the berserker's held axe seat on CC_Base_R_Hand). When OFF (default), enemies spawn WEAPONLESS —
        /// the grip/prop attach is SKIPPED until the Offset Forge grip is perfected on a single weapon. The
        /// attach code path + NormalizeEnemyProp + AttachmentOffsetRegistry grip are INTACT and reversible:
        /// disabling, not deleting. Flip ON to re-enable held weapons once the grip is dialed in:
        /// PlayerPrefs "ff.enemyweapons" = 1.</summary>
        public static bool EnemyWeapons => Get("enemyweapons", defaultOn: false);

        /// <summary>WO-498 — when ON, the new 9-zone mobile battle HUD (<see cref="DeNelle.Village.Arena.BattleHud9Zone"/>)
        /// spawns alongside <see cref="DeNelle.Village.Arena.BattleArenaHud"/> when a battle stages: a 3x3
        /// tic-tac-toe layout (TL Knight HP+resources, TC enemy family role overview, TR timer+pause,
        /// ML current-target portrait, MR quick-focus, BL joystick (mobile), BC basic attack pill,
        /// BR ability arc with radial cooldown rings). Dark semi-transparent premium-fantasy panels
        /// wired to HeroHealth / HeroAbilities+AbilityCatalog / HeroTargetIndicator. Default OFF — this is
        /// the BONES (WO-498 overnight) that the owner finesses look/feel on tomorrow; togglable without a
        /// rebuild. PlayerPrefs "ff.battlehud9zone" = 1 to preview.
        /// APPLIED 2026-06-23 (owner: "the new 9-slice HUD should be applied"): default flipped ON
        /// so the 9-zone bones spawn on every BattleArena fight via BattleArenaHud.Create ->
        /// BattleHud9Zone.Create. The owner finesses look/feel on top (WO-507). To revert to the
        /// legacy overlay only: PlayerPrefs "ff.battlehud9zone" = 0.
        /// V1 DESCOPE 2026-06-26: the 9-zone HUD belongs to the CUT BattleArena loop, so it ships
        /// OFF for V1. Default flipped to false; PlayerPrefs "ff.battlehud9zone" = 1 to preview (V2).</summary>
        public static bool BattleHud9Zone => Get("battlehud9zone", defaultOn: true);   // PREVIEW 2026-06-26 (HUD): ON so the 9-zone spawns in-arena for the owner to felt-judge. REVERT to false (V2) after the decision.

        /// <summary>WO-579 — the village wave loop AUTO-ARMS its prepare-phase countdown on home-hub
        /// entry (MainCastle_Hall) so a "next wave in MM:SS" timer ticks top-left and the wave AUTO-starts
        /// at zero (towers + hero auto-defend in-hub). The HUD "Start Wave" button is then a manual EARLY
        /// OVERRIDE (skip the remaining countdown), not the only kickoff. Default ON (owner felt-test
        /// 2026-06-28: "start wave was an OVERRIDE, otherwise should AUTO attack"). PlayerPrefs
        /// "ff.waveautostart" = 0 to revert to the old defend-gated (button-only) start.</summary>
        public static bool WaveAutoStart => Get("waveautostart", defaultOn: true);

        /// <summary>WO-579 — DEPRECATED breach→ATB handoff. When an enemy reached the Heart ring the
        /// village wave used to PAUSE and load the flat/static ATBBattle scene (memory
        /// atb-flat-vs-overworld: ATB is the deprecated side-path). Owner felt-test 2026-06-28: the
        /// village wave must resolve IN-HUB (towers + hero auto-defend; enemies contact-damage the Heart;
        /// Heart at 0 = defeat) and must NOT launch ATBBattle. Default OFF → no scene swap, so placed
        /// towers + the wave counter never reset across the (removed) round-trip. The ATB system itself is
        /// untouched (dungeons / sandbox still use SceneRouter.GoBattle). PlayerPrefs "ff.wavebreachtoatb"
        /// = 1 to restore the legacy breach→ATB route.</summary>
        public static bool WaveBreachToAtb => Get("wavebreachtoatb", defaultOn: false);

        /// <summary>WO-591 / owner directive 2026-06-29 ("we should retire ATB"): when ON (default), the
        /// two remaining DUNGEON encounter entry points (<see cref="DeNelle.Dungeons.DungeonStubEncounter"/>
        /// and <see cref="DeNelle.Dungeons.EncounterTrigger"/>) route the fight into the REAL-TIME isolated
        /// <see cref="DeNelle.Village.Arena.BattleArena"/> (BeginEncounter — the verified open-kite combat
        /// stack the overworld + arena already use), instead of loading the FLAT/static ATBBattle scene via
        /// <c>SceneRouter.GoBattle</c>. Canon: the dungeon is a SKIN of the BattleArena; ATB is the weaker
        /// system (static enemy defs, never reads the talent tree/loadout) and is being retired. The arena
        /// stages additively + warps the hero in/out IN the dungeon scene (no scene round-trip), so victory
        /// lands the hero back where the fight started. Default ON. This is a REVERSIBLE RETIRE, not removal:
        /// the ATBBattle scene + BattleController are untouched — set PlayerPrefs "ff.dungeonrealtime" = 0 to
        /// restore the legacy ATB GoBattle path verbatim.</summary>
        public static bool DungeonRealtimeBattle => Get("dungeonrealtime", defaultOn: true);

        /// <summary>Global runtime kill-switch for ALL dev keyboard hotkeys (DevPanel F1, DebugCanvas
        /// F12, AdminOverlay Ctrl+Shift+A, the test spawners J/K/L, the tower dev harness B/J/K/N/U,
        /// the jukebox J open, etc.). Default OFF — so every dev hotkey is DEAD everywhere (editor AND
        /// build) unless a developer explicitly opts in by setting PlayerPrefs "ff.devhotkeys" = 1.
        /// This is the single gate the dev hotkeys check at the top of their key-read; it replaces the
        /// old <c>#if UNITY_EDITOR</c> wraps that left the keys live in the editor (where the owner
        /// tests). Movement (WASD/arrows), weapon skills/spells, F8 capture and F9 are NOT dev hotkeys
        /// and are unaffected by this flag.</summary>
        public static bool DevHotkeys => Get("devhotkeys", defaultOn: false);

        /// <summary>DEV RESOURCE TOOL (owner 2026-07-16: "on this APK can you add a resource devtool ...
        /// since its only local"). Gates the on-screen touch grant overlay
        /// <see cref="DeNelle.Village.Dev.ResourceDevTool"/> (per-currency +100/+1k/+10k/+1M buttons for
        /// Gold/Wood/Iron/Food/Crystals). WHY A FLAG AND NOT Debug.isDebugBuild: the tester APK is a
        /// RELEASE build — <c>AndroidBuild.BuildSeekerApk</c> uses <c>BuildOptions.None</c>, so
        /// Debug.isDebugBuild is FALSE and the whole DeNelle.DevTools assembly is stripped on the APK.
        /// This flag is what surfaces the tool on that local build. The tool ALSO always shows in the
        /// Editor and in any Development build regardless of this flag. Default ON so it appears on the
        /// local tester APK the owner ships now ("since its only local"). SECURITY: it grants unlimited
        /// resources — SET PlayerPrefs "ff.devresourcetool" = 0 (or flip this default to false) BEFORE
        /// ANY PUBLIC / STORE RELEASE build.
        /// STORE-HARDENING (Path A, S2): default is now <see cref="IsDevBuild"/> — ON in Editor/Development
        /// (owner keeps the tool while developing) but OFF in a RELEASE/store APK, so no unlimited-resource
        /// exploit ships publicly. PlayerPrefs "ff.devresourcetool" = 1 still re-enables it on any build.</summary>
        // OWNER RULING 2026-08-07 ("remove the flag and dev button ... for better screenshots"):
        // default OFF everywhere, including dev builds. Was defaultOn: IsDevBuild, which put the
        // chip in shot on every editor/dev capture. Opt in with ff.devresourcetool=1.
        // OWNER RULING 2026-08-07 (second pass, "enable the dev tab and rebuild the exe"): default
        // ON for DESKTOP ONLY. This does not reverse the screenshot ruling above - that ruling was
        // about chips appearing in DEVICE captures, and Android/iOS stay OFF, so the Seeker shots
        // and any store APK are unaffected. Desktop is where she iterates and where F8 already
        // works from the keyboard, so the DEV chip earns its space there.
        // OWNER RULING 2026-08-08 (third pass): "let's go ahead and remove the dev flag on the left
        // side, and let's hide the dev panel ... let's stick it under settings."
        // Default OFF EVERYWHERE, desktop included.
        //
        // ⚠ THIS IS NOT A REVERSAL OF THE 08-07 SECOND PASS, and the difference is the whole point.
        // That pass turned it ON for desktop because turning it OFF had taken away her only way IN;
        // the chip was paying for itself as an access route. This pass removes the chip AND ADDS THE
        // DOOR: DevPanel is reachable from Settings (SettingsController), so access survives without
        // anything sitting in shot. Flipping the flag alone would have re-created the exact problem
        // the second pass existed to fix - which is why the two changes ship together, not apart.
        //
        // ONE SWITCH, still: OwnerDevToolsOverlay gates on this same flag (:88), so both DEV chips
        // go together. F8 capture is untouched.
        // Still overridable either way: ff.devresourcetool = 0 hides it, = 1 forces it on device.
        public static bool DevResourceTool => Get("devresourcetool", defaultOn: false);

        /// <summary>MOBILE FLAG BUTTON (owner felt-tests on Android and CANNOT press F8 - no keyboard).
        /// Gates the on-screen tap-to-capture chip <see cref="DeNelle.Core.Dev.FlagCaptureButton"/>, the
        /// mobile equivalent of the F8 key: one tap fires the SAME
        /// <see cref="DeNelle.Core.Diagnostics.BreakCaptureHarness"/> "flagged" capture (break-log.jsonl
        /// entry + clean-frame PNG + the recent Flow/Guard/exception trace tail) the F8 key fires.
        /// WHY A FLAG AND NOT Debug.isDebugBuild: the tester APK is a RELEASE build
        /// (<c>AndroidBuild.BuildSeekerApk</c> uses <c>BuildOptions.None</c>), so Debug.isDebugBuild is
        /// FALSE there - same reasoning as <see cref="DevResourceTool"/>. The button ALSO always shows in
        /// the Editor and any Development build regardless of this flag. Default ON so the owner can flag
        /// bugs on-device now ("owner is never the bug detector", CLAUDE.md 14). HIDE for a public/store
        /// release: set PlayerPrefs "ff.flagbutton" = 0 (or flip this default to false).
        /// STORE-HARDENING (Path A, S2): default is now <see cref="IsDevBuild"/> — ON in Editor/Development
        /// (owner keeps the on-device flag chip while developing) but OFF in a RELEASE/store APK. PlayerPrefs
        /// "ff.flagbutton" = 1 still re-enables it on any build.</summary>
        // OWNER RULING 2026-08-07: default OFF everywhere. The F8 KEY still captures - only the
        // on-screen chip is hidden. Opt in with ff.flagbutton=1 (needed on a touch device with
        // no keyboard). Was defaultOn: IsDevBuild.
        // ⭐ OWNER RULING 2026-08-24 - default ON for TESTER builds, still OFF for the store.
        // The 2026-08-07 "default OFF everywhere" ruling is NOT reversed; it is made reachable. That
        // ruling protected two things - no dev chip in a store APK, and no chip in device SCREENSHOTS -
        // and both survive: the store build has no TESTER_BUILD define, and the AdminOverlay toggle
        // (Settings -> DevTools -> "FLAG chip") turns it off for clean captures.
        //
        // ⛔ WHAT THE OLD DEFAULT ACTUALLY COST, found 2026-08-24: on a touch device the owner had NO
        // capture trigger at all. No F8 key; the 5-tap corner gesture retired; the F10 menu retired;
        // the dev panel's "Feature flags" group holding ZERO rows since ff.strategicplacement was
        // removed; and Android PlayerPrefs needing root to set by hand. The flag was the documented
        // opt-in for "a touch device with no keyboard" and it was unreachable FROM a touch device.
        public static bool FlagButton => Get("flagbutton", defaultOn: IsDevBuild || IsTesterBuild);

        /// <summary>HUB AMBIENT DEPTH (owner 2026-06-23, overnight first-pass) -- when ON (default), the
        /// <see cref="DeNelle.Village.HubAmbientVfxInjector"/> attaches tasteful looping ambient VFX to the
        /// home hub (MainCastle_Hall) at runtime, WITHOUT hand-editing the scene: a soft glowing aura around
        /// the Tree of Life (Heart of Elarion) at plaza centre, plus a small warm flame/ember accent at the
        /// top of each of the 4 castle corner towers. Self-built procedural ParticleSystems rendered with the
        /// committed URP-safe material helper (no gitignored VFX-pack dependency, clean-clone safe). This is
        /// the BONES -- exact effect/scale/colour are tunables in the injector the owner finesses by eye
        /// tomorrow. Default ON so the draft is visible; PlayerPrefs "ff.hubambientvfx" = 0 to disable.</summary>
        public static bool HubAmbientVfx => Get("hubambientvfx", defaultOn: true);

        /// <summary>WO-512 — when ON, the battle SOFT LOCK-ON is live: auto-lock the nearest enemy on
        /// engaging a battle (BattleArena.StageRoutine), tap the HUD Lock toggle to release to free-look,
        /// and switch via the roster/cycle. The single lock owner is <see cref="DeNelle.Village.HeroTargetIndicator"/>
        /// (the HUD routes through it; no duplicate aim writes). Slices 0-1 add NO camera motion and NO
        /// hero-facing change — they are the safe foundation (flag-gated; OFF == today's exact behavior).
        /// Camera framing + face/strafe (slices 2-3) are layered behind this same flag later. Default OFF
        /// until felt-proven (mobile-nausea is the top risk). PlayerPrefs "ff.lockon". Spec: WORK_ORDER_512.</summary>
        public static bool LockOn => Get("lockon", defaultOn: false);

        /// <summary>WO-509 (overnight BONES) — when ON, a diegetic WATER MOAT ring is built around the
        /// MainCastle_Hall perimeter with 4 WIDE DRAWBRIDGE decks at the cardinal gates (N/E/S/W), so the
        /// "you cannot go past here" castle edge READS as deliberate (water = natural impassable boundary)
        /// and the exits read as intentional WIDE crossings, not dead-ends. The bridges are also the
        /// defensive CHOKEPOINTS (single-lane crossings towers/troops cover; raisable to seal a lane is a
        /// future hook) and ARE the WO-509 four RegionGates (only the south is wired today). FIRST-PASS:
        /// visual moat + visible bridge decks only (no footprint shrink, no navmesh re-bake, no functional
        /// N/E/W crossings yet — those are the editor/architect follow per docs/CASTLE_MOAT_DESIGN_NOTE).
        /// Default ON so the owner sees the bones; PlayerPrefs "ff.castlemoat" = 0 to hide. Tunables live in
        /// <see cref="DeNelle.Village.World.CastleMoatBuilder"/>.</summary>
        public static bool CastleMoat => Get("castlemoat", defaultOn: true);

        /// <summary>WO-608 (owner 2026-07-04) — when ON, the game uses the ONE merged scene
        /// <c>Main_Castle_Overworld</c> (castle + full outer world welded into a single continuous
        /// navmesh, built by <see cref="DeNelle.Editor.WorldMergeBuilder"/>) instead of the legacy
        /// two-scene additive model. When ON the seam is GONE, so the runtime SKIPS any additive
        /// world load and SKIPS masked warp / RegionGate crossing (<see cref="DeNelle.Village.RuntimeRegionGate"/>) on the
        /// merged scene — the descent is a plain walk down the 4 bridge ramps on one navmesh. The
        /// additive path + RegionGate primitive stay intact for other hubs / dungeon / outpost / arena
        /// (disable-not-delete). Default OFF until the merged scene is baked + owner felt-verified
        /// (ten-year-old test: descend the ramp, seam invisible). PlayerPrefs "ff.mergedworld" = 1 to
        /// preview once the bake lands.</summary>
        public static bool MergedWorld => Get("mergedworld", defaultOn: true);   // TEST-BUILD 07-04: ON for owner merged-world felt-walk; merge lane commit HELD until verified + felt-approved (set final default per owner verdict)

        /// <summary>WO-602 (fleet-proven P0: <c>HOME_RETURN_FAIL :: gate=&lt;none&gt;</c> x4) — when ON,
        /// <see cref="DeNelle.Village.World.HomeReturnPortalInjector"/> authors four "Enter Elarion"
        /// return portals (SceneTransitionTrigger targeting the hub) at the moat-bridge outer ends,
        /// so a player who leaves the castle always has a discoverable, tap-to-confirm way back to
        /// the courtyard. Runtime-authored (no scene edit, no rebake). PlayerPrefs
        /// "ff.homereturnportal" = 0 to hide/disable.</summary>
        public static bool HomeReturnPortal => Get("homereturnportal", defaultOn: true);

        /// <summary>GATE TRAVERSAL (owner on-device 2026-07-15: "on approaching a door, should navigate to
        /// the other side of the door") — when ON, <see cref="DeNelle.Village.World.GateTraversalInjector"/>
        /// authors a paired inner/outer warp at each of the 4 castle gates (N/S/E/W) on the merged overworld,
        /// so a hero walking OUT through a gate opening is carried just past the archway (facing outward), and
        /// walking IN is carried just inside (facing inward). Runtime-authored (no scene edit, no rebake),
        /// NavMesh-seated, and kept INSIDE the HomeReturnPortal outer ring (r~72) so the two never fight.
        /// Default ON. PlayerPrefs "ff.gatetraversal" = 0 to disable.</summary>
        // WO-1295: the Synty perimeter has separate baked navmesh regions on either side of its
        // arch. The old default-OFF assumption strands the input-driven hero at the opening.
        // GateTraversalInjector now crosses only the short wall thickness (not the retired 14m
        // eject), while a NavMeshLink on the same seam carries pathfinding enemies and troops.
        public static bool GateTraversal => Get("gatetraversal", defaultOn: true);

        /// <summary>The editor-baked south CastleBridgeSeam deck (CastleHubBuilder.AddCastleBridgeSeam).
        /// Default OFF (2026-06-29): the editor deck stacked a 2nd navmesh deck on top of the runtime
        /// RuntimeRegionGate south deck, splitting the south navmesh -> spawn->gate PathPartial. South now
        /// uses the runtime-only crossing like W/N/E. Set "ff.castleeditorbridgeseam"=1 to restore.</summary>
        public static bool CastleEditorBridgeSeam => Get("castleeditorbridgeseam", defaultOn: false);

        /// <summary>The runtime gate-finding BEACON pillar (RuntimeRegionGate.BuildGateBeacon) — the tall
        /// white emissive cube + point light at each of the 4 crossings. Owner 2026-06-29: "the white thing"
        /// that doesn't belong; the gates should be VERY SIMPLE AND FLAT. Default OFF so no beacon pillars
        /// render — the crossing still works (deck + trigger). Set "ff.gatebeacon"=1 to restore findability
        /// pillars.</summary>
        public static bool GateBeacon => Get("gatebeacon", defaultOn: false);

        /// <summary>OUTPOST ENTRANCES (owner 2026-06-28: "create a few caves in the bake, we just don't
        /// wire them and flag them on till ready"). When ON, the walk-in CAVE MOUTHS placed in the
        /// merged world by <see cref="DeNelle.Editor.CavePortalBuilder"/> become live OUTPOST
        /// entrances — a deck-seated <c>SceneTransitionTrigger</c> warps the hero into the (future)
        /// loading-zone → outpost RESOLVER. That resolver/loading-zone DOES NOT EXIST YET, so this ships
        /// OFF: the bake places the cave GEOMETRY only and leaves the entrance behavior INERT (no trigger /
        /// no destination) until the resolver slice lands. Canon: outposts/dungeons are entered by a
        /// placeable warp gate (cave skin = outpost) → loading zone → resolver. Default OFF. Flip ON once
        /// the resolver is wired: PlayerPrefs "ff.outpostcaves" = 1.</summary>
        public static bool OutpostCaves => Get("outpostcaves", defaultOn: true);

        /// <summary>DUNGEON ENTRANCES (owner 2026-06-28, same theory as <see cref="OutpostCaves"/>): when
        /// ON, the KayKit-skinned DUNGEON PORTAL points placed in the merged world by
        /// <see cref="DeNelle.Editor.CavePortalBuilder"/> become live dungeon entrances routed
        /// into the (future) loading-zone → dungeon resolver. Same unbuilt-resolver caveat: the bake places
        /// the portal GEOMETRY only and leaves the behavior INERT until the resolver lands. Kept SEPARATE
        /// from <see cref="OutpostCaves"/> so cave-outposts and portal-dungeons can be enabled
        /// independently (cave skin = outpost, portal skin = dungeon).
        /// DEFAULT ON since 2026-07-13 (owner: "can we fold in a simple dungeon — it's already
        /// been tested many times… the portal take to dungeons"): the animated DungeonPortal
        /// entrances arm for the two REAL dungeon scenes only (HealersCottage + FolksGranary —
        /// both bootstraps' lists cover exactly the built scenes). The old placeholder-pill
        /// concern is accepted for this milestone; portal ANIMATION polish is a flagged
        /// follow-up (owner: "animation needs looked at but all works").
        /// Kill switch: PlayerPrefs "ff.dungeonportals" = 0.</summary>
        public static bool DungeonPortals => Get("dungeonportals", defaultOn: true);

        /// <summary>WO-1114 DUNGEON DOOR STATUS. Default ON. When ON, the remotely-flippable
        /// door state (<c>DeNelle.Core.World.DungeonStatusCatalog</c> / <c>DungeonStatusService</c>)
        /// is fetched at boot and a non-<c>open</c> dungeon reads as a SEALED DOOR in the world —
        /// authored prose at the portal, no entry, no scene load, no error styling.
        /// <para>
        /// ⛔ KILL SWITCH: PlayerPrefs "ff.dungeonstatus" = 0 forces EVERY door OPEN with no
        /// rebuild, and suppresses the fetch entirely. That is the escape hatch if a bad payload
        /// ever locks content — every other failure path in the system already resolves toward
        /// OPEN by design, so this flag is the last line, not the first.
        /// </para>
        /// ⛔ Do NOT add "dungeonstatus" to the URL-activatable allow-list. That list is
        /// deliberately restricted to read-only presentation flags; a URL-flippable CONTENT gate
        /// is a security regression.
        /// <para>
        /// This is the SINGLE authority for the key. <c>DungeonStatusService</c> reads it through
        /// here (its 2026-08-21 inlined copy was deleted in the same edit that added this line) —
        /// never re-inline a second reader of "ff.dungeonstatus".
        /// </para></summary>
        public static bool DungeonStatus => Get("dungeonstatus", defaultOn: true);

        /// <summary>WO-1243 OPERATOR KILL SWITCHES. Default ON. When ON,
        /// <c>DeNelle.Core.Ops.MaintenanceService</c> polls /api/maintenance and a sealed area
        /// refuses entry with a rolling banner naming it.
        /// <para>
        /// !! THIS FLAG DISABLES THE COURTESY GATE ONLY, AND THAT IS THE POINT OF SAYING SO HERE.
        /// The seal itself is enforced SERVER SIDE (api/_lib/maintenance.js, called from
        /// purchases/quote.js, game/save.js and leaderboard/submit.js) precisely because a client
        /// check cannot bind someone running a modified client. Turning this off stops the banner
        /// and the client refusal; it does NOT reopen a sealed area's server-side seam, and it is
        /// not a way to bypass one.
        /// </para>
        /// <para>
        /// DO NOT: KILL SWITCH: PlayerPrefs "ff.maintenance" = 0 suppresses the poll entirely and leaves
        /// every area open on the client. It exists for the case where the endpoint itself
        /// misbehaves - the system already fails OPEN on every other path (owner ruling 2026-08-27),
        /// so this is the last line, not the first.
        /// </para>
        /// DO NOT: Do NOT add "maintenance" to the URL-activatable allow-list. A URL-flippable
        /// containment gate is a security regression.</summary>
        public static bool Maintenance => Get("maintenance", defaultOn: true);

        /// <summary>WORLD FEEL (owner felt-test 2026-07-01: "world feels empty / very flat / not polished").
        /// When ON (default), <c>DeNelle.Village.World.WorldFeelInjector</c> applies the world-aesthetics
        /// pass at runtime on the outdoor scenes (MainCastle_Hall / Main_Castle_Overworld / Village2), WITHOUT
        /// hand-editing any scene: (1) camera clearFlags forced to Skybox — the hub camera ships
        /// SolidColor near-black (MainCastle_Hall.unity m_ClearFlags:2, bg 0.16/0.17/0.19), which IS the
        /// black-void sky in every screenshot; (2) a dusk "hold the last light" procedural skybox +
        /// warm trilight ambient + low warm sun + soft haze fog; (3) a subtle global URP post volume
        /// (bloom for torch/aura pop, gentle vignette, slight warm grade); (4) drifting ambient motes
        /// around the camera in the open world. Every knob is a tunable const in the injector.
        /// Default ON so the owner feels the draft; PlayerPrefs "ff.worldfeel" = 0 restores the
        /// exact prior look (no rebuild).</summary>
        public static bool WorldFeel => Get("worldfeel", defaultOn: true);

        /// <summary>SURVIVAL RULE (owner 2026-06-29): Health AND Mana do NOT auto-restore after combat.
        /// When ON (default), the post-combat "return heal" (BattleArena.ReturnHomeWithFade) is SKIPPED —
        /// in the field the hero keeps the HP/MP it ended the fight with and relies on crafted potions.
        /// Full passive recovery happens ONLY at a SAFE ZONE (Castle/Town/Base — see
        /// <see cref="DeNelle.Village.SafeZoneRecovery"/> + <see cref="DeNelle.Core.HubScenes.IsHub"/>), which
        /// ALWAYS fully heals regardless of this flag (that is the design, not the auto-heal this gates).
        /// Reversible: PlayerPrefs "ff.noautoheal" = 0 restores the post-combat auto-heal-to-full.</summary>
        public static bool NoAutoHeal => Get("noautoheal", defaultOn: true);

        /// <summary>Combat-feel polish layer (2026-07-02 arena feel pass) — when ON (default), the
        /// presentation-side feel additions run: recorded sword-clash / enemy-death SFX VARIANT pools
        /// (GameSfx / EnemyCombatAudio pick a random authored take per hit instead of one repeated
        /// clip), and the arena stage RETHEME (stone-biome fights swap the forest-clearing green
        /// lawn + toy-tree ring for the biome's stone ground + rock-only silhouette so the floor
        /// matches the colosseum backdrop — owner F8 "this looks awful" visual-vocabulary clash).
        /// Pure presentation: no damage, timing or AI change. OFF restores the previous look/sound
        /// exactly. PlayerPrefs "ff.combatfeel" = 0 to disable.</summary>
        public static bool CombatFeel => Get("combatfeel", defaultOn: true);

        /// <summary>WO-T1 (Tutorial V2) — when ON, the SOLE first-run tutorial is the data-driven
        /// founding arc: <c>tutorial-steps.json</c> walked by <c>DeNelle.Village.TutorialFlow</c>,
        /// spoken only by the pet-Echo <c>{guide}</c> token (the ice-wolf / founding Echo —
        /// TutorialGuideIdentityInstaller). Completion via <c>TutorialSignals</c>.
        /// ONE-GUIDE LOCK (WO-971 / owner 2026-08-10): while this flag is ON, legacy
        /// <c>OnboardingFlow</c> coach-marks and auto-hosted CompanionMeeting Yarn STAND DOWN
        /// so two tutorials cannot fight. The deleted <c>TutorialDirector</c> / Sylas tut_* arc
        /// stay dead (TutorialGuideIdentityRegression). PlayerPrefs "ff.tutorialv2" = 0 only for
        /// emergency legacy coach-mark re-arm (not a second narrator).</summary>
        public static bool TutorialV2 => Get("tutorialv2", defaultOn: true);

        /// <summary>Hero package pipeline (owner ruling 2026-07-03: the PALADIN is the new Knight body) —
        /// when ON (default), the runtime hero pipeline loads the published PALADIN hero package for the
        /// Knight: <c>Resources/Heroes/KnightPackage.prefab</c> (a variant of Knight_Hero.fbx that BINDS
        /// <c>KnightPackage.controller</c>, the full posture-tree controller) instead of the legacy Tripo
        /// armored Knight. The package prefab carries its own Animator + controller + baked sword/shield/
        /// helmet, and runs at a single 1.0 cadence authority (not the legacy 0.5 global anim speed).
        /// OFF restores the legacy Tripo Knight (legacy slug "Knight", 0.5 anim speed, +15 forward yaw)
        /// exactly. A failed package load also degrades to the legacy Knight so the hero is never bodyless.
        /// PlayerPrefs "ff.heropackage" = 0 to force the legacy Tripo Knight.</summary>
        public static bool HeroPackage => Get("heropackage", defaultOn: true);

        /// <summary>KnightV3 hero body (owner "try this" 2026-07-03) — when ON (default), the Knight loads
        /// the owner's NEW <c>Resources/Heroes/KnightV3.fbx</c> body: a Character-Creator / AccuRIG export
        /// (CC_Base_* skeleton auto-mapped to a STANDARD Unity humanoid, one embedded 'Material_Pbr'
        /// diffuse, embedded WALK + custom DANCE clips). It retargets the shared Knight animations via the
        /// proven <c>Knight.controller</c> (locomotion + injured + cast states), keeps its OWN embedded
        /// texture (RetargetMaterialsToUrp), and falls back to a flat color only on null-albedo slots.
        /// Checked BEFORE ff.heropackage, so V3 supersedes the Paladin package for the Knight. A failed V3
        /// load degrades to the legacy Tripo Knight (never bodyless). PlayerPrefs "ff.knightv3" = 0 to
        /// restore the Paladin package / legacy Knight.</summary>
        public static bool KnightV3 => Get("knightv3", defaultOn: true);

        /// <summary>STUDIO-MOCAP KNIGHT LOCOMOTION (owner 2026-07-04) — when ON, the KnightV3 body binds
        /// the studio-mocap locomotion twin <c>Resources/Heroes/KnightMocap.controller</c> instead of
        /// <c>Knight.controller</c>. KnightMocap is IDENTICAL to the Knight controller EXCEPT its
        /// Locomotion 1-D blend tree Idle/Walk/Run sources are the professional sword+shield studio-mocap
        /// clips (idle_ready / walkforward01 / runforward_218667 from
        /// Assets/Action/Knight/Motion/studio-mocap-sword-and-shield-moves/) — HUMANOID on the SAME CC_Base
        /// rig, so the retarget is ~1:1 (no lossy cross-rig Mixamo look, the "off" walk the owner flagged).
        /// Cast/Attack/Hit/Death/Block/Victory/Injured/UpperBody are unchanged. Default OFF until the owner
        /// felt-approves — the existing Knight.controller is byte-untouched when OFF (a missing KnightMocap
        /// controller also degrades to Knight). idle/walk/run FORWARD only this pass; strafe/turn/combat are
        /// Phase 2. PlayerPrefs "ff.mocaploco" = 1 to preview.</summary>
        public static bool MocapLocomotion => Get("mocaploco", defaultOn: true);   // 2026-07-04: owner felt-approved ("player feels ok") — ON. Studio-mocap idle/walk/run FORWARD; strafe/turn/shield-carry = Phase 2.

        /// <summary>WO-478 — DEPRECATED geometry grip inference for NATIVE melee props (Blink
        /// grip-at-origin prefabs such as sword_A). When ON, native melee weapons discard the
        /// authored pivot and run NormalizeInto + SeatHiltLowerHalf + ComputeMeleeGripRotation
        /// (the pre-WO-478 path superseding stale WO-435). Default OFF → native melee trusts
        /// SeatNative (authored grip-at-origin + scale + per-archetype nudge only). PlayerPrefs
        /// "ff.weapongripinfer" = 1 to restore the legacy inference path.</summary>
        public static bool WeaponGripInfer => Get("weapongripinfer", defaultOn: false);

        /// <summary>SEEKERTHON stake-rewards DEMO surface — when ON, <see cref="DeNelle.Core.Platform.StakeRewardsDemoBootstrap"/>
        /// seeds a real-looking active native SKR stake (~1M, a Genesis holder) into
        /// <see cref="DeNelle.Core.Platform.StakeRewardsResolver"/> and auto-opens the read-only
        /// <see cref="DeNelle.Core.UI.StakeRewardsPanel"/>, so the video shows "active stake -> in-game
        /// rewards, automatically, we never hold your SKR" WITHOUT any live wallet. Purely presentation +
        /// a mock READ — it custodies nothing, mutates no game state, and mints nothing (canon
        /// skr-separate-ingame-currency-real-token-readonly). Default OFF so production NEVER shows it.
        /// Forceable ON for ONE WebGL session via the page URL <c>?stakedemo=1</c> (allow-listed in
        /// <see cref="ApplyUrlActivationOnce"/>) — no rebuild, no prod-default change. Off-web:
        /// PlayerPrefs "ff.stakedemo" = 1 (or the Defenders/Debug editor menu).</summary>
        public static bool StakeDemo => Get("stakedemo", defaultOn: false);

        /// <summary>"POWERED WITH SKR" GRANT PREVIEW (owner 2026-07-04) — when ON, the aspirational SKR
        /// integration story is presentable for the grant recording: a "Powered with SKR" badge appears
        /// on the Title screen and opens <see cref="DeNelle.Core.UI.SkrShowcasePanel"/> — the branding +
        /// the honest value-prop (real token / non-custodial / server-verified / cosmetic-only), clearly
        /// stamped "PREVIEW · TESTNET — NOT LIVE". Every action is a NO-OP or opens the read-only
        /// StakeRewardsPanel; NOTHING calls a wallet, signs, or moves funds (the pay rail stays the devnet
        /// StubWalletProvider). Default OFF so normal players never see the aspirational flow. The
        /// grant-recording build flips it ON: the Defenders/Debug menu, PlayerPrefs "ff.skrpreview" = 1,
        /// or the WebGL URL <c>?skrpreview=1</c> (allow-listed in <see cref="ApplyUrlActivationOnce"/> —
        /// read-only presentation, so a crafted link can at most show an info panel).</summary>
        // Owner 2026-07-06: demo/grant recording — badge + showcase (the panel is self-labeling:
        // PREVIEW · TESTNET stamp, no wallet call).
        // STORE-HARDENING (Path A, C4): default flipped to OFF so the honest ZERO-CRYPTO store build
        // ships NO "Powered with SKR"/crypto marketing surface (no misleading badge). The grant-recording
        // build flips it back ON via the Defenders/Debug menu, PlayerPrefs "ff.skrpreview" = 1, or the
        // allow-listed ?skrpreview=1 URL — all preserved.
        public static bool SkrPreview => Get("skrpreview", defaultOn: false);

        /// <summary>STORE-HARDENING (Path A, M1) — gates the pack-store PURCHASE rail (the "Buy" CTA in
        /// <see cref="DeNelle.Wallet.PackStore"/> BuildPackCard + the <c>Purchase</c> entry itself). The
        /// pack cards ALWAYS render cosmetically (name / tagline / USD reference / contents); this flag
        /// only controls whether a buy button is offered. Default is <c>false</c> — OFF on EVERY build
        /// (Editor, Development and RELEASE alike) as of the 2026-08-08 store-submission re-gate, so the
        /// honest ZERO-CRYPTO build ships NO dead "Buy" button routed to the stub wallet. Reversible per
        /// DEVICE: PlayerPrefs "ff.realmstorepurchase" = 1 re-enables the rail on any build. NOT
        /// URL-activatable (monetization surface — excluded from the allow-list).</summary>
        // HISTORY 2026-08-07 (WO-911 Q9): the default was flipped ON. The reasoning was that we were
        // DEVNET-ONLY with the owner as the only registered tester, so there was no player to hand a
        // failing button to, while every broke-case Finish-Now route dead-ended on "Coming soon".
        //
        // RE-GATED **OFF** 2026-08-08 (owner decision, store-submission build — WO-915).
        // The Q9 premise expires the moment the build reaches a public store: a STORE REVIEWER will tap
        // Buy, and the tap cannot settle. Both blockers below are still true at source, so the button
        // could only ever dead-end — which is itself a rejection risk:
        //   * SolanaWalletProvider.SendPayment HARD-BLOCKS WalletNetwork.Mainnet (defence in depth
        //     that survives this flag - a mainnet build still cannot take money), and
        //   * WalletEndpoints.SkrMintDevnet is "" so even a DEVNET **SKR** transfer cannot resolve
        //     a mint. SKR is the default currency, so an SKR buy fails today; USDC/SOL is the only
        //     rail with a chance of completing.
        // Third reason this default matters off-Android: StubWalletProvider.SendPayment FAKE-SUCCEEDS
        // (fabricated signature, mock balance debited) and PackStore then grants the full pack for zero
        // payment. WalletService auto-selects that stub on desktop/WebGL/no-SDK builds, so an ON flag is
        // a free-entitlement hole there, not merely a dead button.
        //
        // ⛔ DO NOT TURN THIS BACK ON until ALL THREE are true (not any one of them):
        //   1. A LIVE, resolvable mint for the default rail — WalletEndpoints.SkrMintDevnet (and its
        //      mainnet equivalent) is non-empty and the transfer resolves it, and
        //   2. The mainnet block in SolanaWalletProvider.SendPayment is deliberately lifted with a
        //      real, tested, settling payment path behind it.
        //   3. **THE FREE-GRANT HOLE IS CLOSED** — Assets/_Modules/Wallet/StubWalletProvider.cs carries
        //      NO #if UNITY_EDITOR / DEVELOPMENT_BUILD guard, so it compiles into EVERY shipped build and
        //      WalletService auto-selects it on release desktop/WebGL (and on Android without SOLANA_SDK).
        //      Either build-guard that file out of release players, OR make the payment path REFUSE when
        //      the resolved provider is the stub. This is the one that costs money, not just a dead
        //      button: turning the flag on before it is closed ships FREE PACKS. See WO-931.
        //      >>> SATISFIED 2026-08-10 (WO-931, option b - runtime refusal at the payment seam).
        //      WalletService.Pay AND WalletService.PayFlat now refuse BEFORE _provider.SendPayment:
        //      a stub-typed provider is refused outright (before any connection-state check), and a
        //      connected provider failing IsRealSigningWallet (e.g. the dev-only DevWalletProbe, which
        //      delegates SendPayment to an inner stub) is refused too. The refusal is unconditional -
        //      NOT #if-guarded - so it holds in Editor, development and release alike; it is loud
        //      (FlowTrace.Fail) and returns PaymentResult.Failure, so PackStore's Ok-gated grant +
        //      purchase_completed event can never fire from a stub "payment". Locked in by the
        //      [wallet-provider] regression, section 8 (runtime Pay/PayFlat cases + source pin).
        //      Preconditions 1 and 2 above remain OPEN - this note does NOT license flipping the flag.
        // NOTE FOR WHOEVER FLIPS IT: Get() reads PlayerPrefs FIRST, so a STORED value BEATS this
        // default. A device that ever set "ff.realmstorepurchase" = 1 keeps the Buy rail until that key
        // is cleared/zeroed. Changing this default protects FRESH INSTALLS (every store reviewer and
        // every real player); it does not retroactively re-gate an existing device.
        // ── LOCAL PURCHASE-RAIL TEST BUILD (2026-08-17) ─────────────────────────────────────
        // ⛔ THIS DEFAULT STAYS false FOR EVERY SHIPPED BUILD. The owner asked to exercise the
        // full purchase flow on her own Seeker; the answer was a LOCAL build, side-loaded, NOT
        // distributed — not a change to what the dApp Store serves. Enable with the define
        // STORE_RAIL_LOCAL_TEST, which is set ONLY on a hand-built local APK and MUST NEVER be
        // added to ProjectSettings' Android define list.
        //
        // ⚠ WHY THIS IS NOT SAFE TO SHIP, and the reason is NOT the flag — it is the NETWORK.
        // WalletService.DefaultNetwork is Devnet, and devnet SOL/USDC/SKR are FREE TEST TOKENS.
        // The WO-931 seam refusal does NOT block here (it only refuses the stub provider or a
        // wallet that cannot sign — a real connected Seeker wallet passes), and
        // SolanaWalletProvider.SendPayment only hard-blocks MAINNET. So on a devnet build with
        // this flag on, the purchase chain COMPLETES: worthless tokens move, real pack contents
        // are granted. Ship that publicly and every download is free packs with a genuine
        // purchase_completed event behind it — indistinguishable in the data from real revenue.
        //
        // The game is PUBLISHED. "Only the owner has it today" is a race, not a guarantee.
        //
        // TO GO LIVE FOR REAL, this flag is the LAST step, not the first:
        //   1. mainnet decision + lift SolanaWalletProvider.SendPayment's mainnet block (:429)
        //   2. switch WalletService.DefaultNetwork off Devnet
        //   3. verify a real signed transaction settles on-chain
        //   4. THEN this default, with the WO-931 seam refusal left exactly as it is.
        // Flipping this one first only ever produces a Buy button in front of free goods.
        //
        // ── ALL FOUR TAKEN, IN ORDER — GO LIVE (WO-1159, owner explicit 2026-08-23) ────────
        // The default below is now TRUE. Every step above was executed in sequence, and the
        // order is the whole reason this is not the "Buy button in front of free goods" case
        // the block warns about:
        //   1. Mainnet decision made by the owner explicitly, and the unconditional mainnet
        //      refusal in SolanaWalletProvider.SendPayment's non-canary branch is REPLACED by
        //      the ruled condition (SKR rail + a positive server-quoted amount). See that file
        //      for why it deliberately carries NO SKU allowlist.
        //   2. WalletService.DefaultNetwork moved off Devnet (6802e2292).
        //   3. Real signed transactions SETTLED on mainnet - the 1-SKR canaries, success
        //      recorded by the owner. The rail is proven, not theorised.
        //   4. This default, with the WO-931 seam refusal untouched and still unconditional.
        //
        // ⛔ THE DEVNET FREE-PACKS HAZARD ABOVE IS CLOSED BY STEP 2, NOT BY THIS FLAG. The
        // warning was "on a devnet build with this flag on, worthless tokens move and real
        // pack contents are granted". DefaultNetwork is Mainnet now, so the tokens are real.
        // If anyone ever moves DefaultNetwork back to Devnet, this default must come back to
        // false in the SAME edit - those two values are only safe as a matched pair.
        //
        // TREASURY: CLOSED. The revenue vault's Squads threshold is 2-of-3, timeLock 0 -
        // re-verified on chain 2026-08-24 (tools/treasury-verify.mjs --multisig). No treasury
        // precondition remains on this flag.
        // ⚠ This comment said 1-of-1 until 2026-08-24 and was STALE - the owner had already
        // raised it. Because raising it changes no code, nothing forced this line to update.
        //
        // NOTE (unchanged and still true): Get() reads PlayerPrefs FIRST. A device that ever
        // stored "ff.realmstorepurchase" = 0 keeps the Buy rail CLOSED until that key is
        // cleared. This default governs fresh installs.
#if STORE_RAIL_LOCAL_TEST || MAINNET_CANARY_TEST
        // An explicit owner-test build must win over a stale production PlayerPrefs value.
        // The symbol is command-line-only and is never present in a distributed build.
        public static bool RealmStorePurchase => true;
#else
        public static bool RealmStorePurchase => Get("realmstorepurchase", defaultOn: true);
#endif

        /// <summary>⭐ LIVE since 2026-08-24 — gates the WHOLE rewarded-ad timer-skip path: the "Ad"
        /// CTA on every queue row (ManageScreenPanel + ObsidianQueueHud) and
        /// <c>BuildTimerService.CanWatchAdToSkip</c> / <c>WatchAdToSkip</c>. <b>DEFAULT ON</b>
        /// (owner: "Flip it on", the morning the ironSource Ads account was approved).
        ///
        /// <para>⚠ THIS SUMMARY USED TO SAY THE OPPOSITE, AND THAT IS WHY IT IS REWRITTEN HERE RATHER
        /// THAN CORRECTED FURTHER DOWN. It read "RELEASE BLOCKER GATE … DEFAULT OFF, and it must STAY
        /// OFF" and "there is NO ad SDK in this project". The flip note was added below the
        /// <c>#if MONETIZATION_LOCAL_TEST</c>, so the FIRST thing a reader (or an IDE tooltip) saw was
        /// still the release-blocker text — a categorical "no SDK, keep it off" sitting on top of a
        /// shipped, ruled ON flag. A correction the reader reaches second is not a correction.</para>
        ///
        /// <para>BOTH ORIGINAL PREREQUISITES ARE MET, verified at source before the flip:
        /// <b>(1) a real SDK</b> — <c>com.unity.services.levelplay@9.5.1</c> in manifest + lock, the
        /// adapter compiling in its own LEVELPLAY_PRESENT-constrained assembly, app key configured,
        /// three placements mapped to real unit ids, rewards granted ONLY from the earned callback.
        /// <b>(2) WO-912 server-anchored window</b> — the stamp is <c>TimeSource.NowUnixMs()</c>, not
        /// <c>DateTime.UtcNow</c>. ⚠ A stale "KNOWN LIMIT … DEVICE clock" comment still sits above
        /// that code in BuildTimerService; the fix shipped and the warning was left. Do not act on it.</para>
        ///
        /// <para>⛔ THE ORIGINAL REASONING STILL GOVERNS ANY FUTURE CHANGE, so it is kept below rather
        /// than deleted: granting on "we showed it" is fraud against the network, and a device-clock
        /// window is FABRICATED IMPRESSIONS against a live ad account — which is what networks ban
        /// for. There is now a live approved account to lose, so those guards matter MORE than when
        /// they were written, not less.</para>
        ///
        /// HISTORICAL — the 2026-08-07 blocker text, true when written, false since 2026-08-24:
        /// <see cref="DeNelle.Village.RewardedAdManager"/>.ShowAdInternal is a stub and
        /// <c>IsAdReady</c> is a plain 480s stopwatch, NOT a fill check. Shipping the CTA would hand out
        /// unlimited free timer skips with no ad shown and no revenue earned. WO-911 widened the CTA from
        /// the Builder channel to ALL THREE channels (Builder/Train/Research), which made the blast radius
        /// bigger, so the gate lands here rather than by reverting that work. When OFF the button is
        /// ABSENT (not greyed) — every build site checks this flag before constructing the control.
        ///
        /// HARD PREREQUISITES before this flag is EVER switched on (both, not either):
        ///   1. A REAL rewarded-ad SDK is integrated and the reward is granted ONLY from that SDK's
        ///      genuine completion callback (OnUserEarnedReward). Granting on "we showed it" is fraud
        ///      against the network, so the stub deliberately refuses instead of rewarding.
        ///   2. WO-912 — SERVER-SIDE ad-window validation. Today the rolling-window start is stamped from
        ///      the DEVICE clock (<c>DateTime.UtcNow</c> in BuildTimerService.RecordAdSkipUsed /
        ///      RollWindowIfNeeded, and that file's own KNOWN LIMIT note says so). Moving the device clock
        ///      forward past the window grants a fresh allowance. With a real SDK behind it that is not
        ///      merely free skips — it is FABRICATED IMPRESSIONS against the ad account, which is exactly
        ///      what ad networks ban accounts for. The window must be stamped/validated server-side (the
        ///      save already round-trips) BEFORE any real ad rail goes live.
        ///
        /// Everything the gate protects is INTACT, not removed: the rolling-window ledger, adSkipSeconds,
        /// adSkipsPerWindow, the cap logic and the UI rows all stay. This is a gate, not a deletion.
        /// NOT URL-activatable (monetization surface — deliberately excluded from s_urlActivatableFlags).
        /// Local testing only: PlayerPrefs "ff.rewardedadskip" = 1.</summary>
#if MONETIZATION_LOCAL_TEST
        // Owner-device sideload only. AndroidBuild production paths never define this symbol.
        // It exists so the physical-device LevelPlay matrix can run without weakening the public
        // default or making monetization URL-activatable. It deliberately overrides a stale
        // PlayerPrefs value written by an earlier production-gated APK on the same test device.
        public static bool RewardedAdSkip => true;
#else
        /// ⭐ FLIPPED ON 2026-08-24 (owner: <i>"Flip it on"</i>), the morning the ironSource Ads
        /// account was APPROVED — which was the last missing piece and the only one that was never
        /// in this repo's hands.
        ///
        /// <para>BOTH HARD PREREQUISITES ABOVE ARE SATISFIED, verified at source before the flip:
        /// <list type="number">
        /// <item>A REAL SDK — <c>com.unity.services.levelplay@9.5.1</c> in manifest + lock, the
        /// adapter compiling in its own <c>LEVELPLAY_PRESENT</c>-constrained assembly, app key
        /// configured, three placements mapped to real unit ids, and rewards granted ONLY from the
        /// earned callback.</item>
        /// <item>WO-912 SERVER-ANCHORED AD WINDOW — the window stamp is
        /// <c>TimeSource.NowUnixMs()</c>, not <c>DateTime.UtcNow</c>, so rolling the device clock
        /// forward can no longer mint a fresh allowance. ⚠ A stale "KNOWN LIMIT — the window start
        /// is a DEVICE clock" comment still sits above that code in BuildTimerService; the fix
        /// landed and the warning was left behind. Do not act on it.</item>
        /// </list></para>
        ///
        /// <para>⛔ WHY THE PREREQUISITES MATTERED, restated so nobody relaxes them later: granting
        /// on "we showed it" is FRAUD against the network, and a device-clock window is FABRICATED
        /// IMPRESSIONS against a live ad account — which is what networks ban accounts for. The
        /// account that was just approved is the thing those two guards protect.</para>
        ///
        /// <para>⚠ OPEN, AND IT NOW HAS TWO DOORS: ads pay out in GOLD, gold IS
        /// <c>Resources.Coins</c>, and WO-1163 makes troop training pure gold — against a covenant
        /// that says "never combat power". WO-1165 §1 already raises this for PURCHASES; ads are the
        /// second path into the same collision. Harmless today because coins are inert; it becomes
        /// real the moment WO-1163 lands. Owner ruling owed there, not here.</para>
        public static bool RewardedAdSkip => Get("rewardedadskip", defaultOn: true);
#endif

        // RETIRED 2026-09-05 (WO-1396): the MapTab flag (PlayerPrefs "ff.maptab", default OFF) is DELETED.
        // It gated a Bag-side Realm Map door (InventoryUIBuilder.OpenRealmMap) that shipped OFF by
        // default and was never offered, so the map had a dormant second door and no live one.
        // The Realm Map's ONE public door is now the Journey deck card (PlayerDeckWorkspace, case
        // PlayerDeckKind.Journey -> PanelId.RealmMap); PublicNavigationRetirementRegression pins
        // that this flag and the Bag route stay gone. The WO-911 Q10+Q13 ruling that took Map OFF
        // the bottom bar STANDS - ActionBarButtonId.Map stays dormant at ordinal 4 (never renumber).

        /// <summary>
        /// WO-1050 — the player's REDUCED-MOTION preference. Turn it ON and every decorative
        /// animation is not merely paused, it is <b>never built</b>: The Night Market's four motion
        /// moments (the spotlight aurora drift, the 400 ms light crossfade on selection, the CTA
        /// specular sweep, the patronage sheen) fall back to their flat lights.
        ///
        /// <para><b>Default OFF — i.e. motion is on by default; flipping this ON reduces it.</b> The
        /// flag name states the PREFERENCE, not the animation, so "on" always means "the player asked
        /// for less". PlayerPrefs "ff.reducedmotion" = 1.</para>
        ///
        /// <para>⛔ THE ACCEPTANCE TEST IS THAT THIS FLAG CHANGES NOTHING BUT THE MOTION. Rolling
        /// colour never carries meaning — band identity lives in the 3 px mark, the text eyebrow and
        /// the step in greyscale value; every card state carries a WORD. With this ON the store must
        /// still be completely readable, because nothing was ever encoded in movement. If turning
        /// this on loses information, the information was in the wrong place.</para>
        ///
        /// <para>It is written as a first-class preference rather than a store-local field because
        /// the Dungeons surfaces already carry per-component <c>SetReducedMotion</c> hooks
        /// (Checkpoint, CraftingPedestal, IngredientPickup) with no shared switch to drive them —
        /// this is the switch they should eventually read, so the preference is asked ONCE.</para>
        /// </summary>
        public static bool ReducedMotion => Get("reducedmotion", defaultOn: false);

        /// <summary>
        /// WO-1026 — the PvE SIEGE loop: scheduled attacks on the player's own town, plus the
        /// persisted, re-openable Defence Report they produce.
        ///
        /// <para><b>Default ON.</b> It was OFF for a DESIGN reason, not a code one: what a failed
        /// defence COST the player was unruled, so the loop would have felt-tested as an attack
        /// with no stake -- hollow, and correctly reported as "the feature is bad" when the truth
        /// was "the stake is missing". That gate is closed.</para>
        ///
        /// <para><b>THE LIVE STAKES RULING is the owner's of 2026-08-27: BANK THEFT REPLACES
        /// COLLECTOR LOOTING.</b> A siege bills ONCE per attack and takes exactly three things:
        /// structural damage, a repair bill, and a bounded percentage of the UNPROTECTED bank --
        /// under a PROTECTED FLOOR and a PER-ATTACK CAP. LOOTABLE: wood, iron, stone (the balance
        /// internally named Food) and coins. UNTOUCHABLE, absolutely: crystals, SKR, purchased
        /// goods, equipped gear. <c>DeNelle.Core.Defense.StakeRules</c> holds the arithmetic,
        /// <c>SiegeStakesBalance</c> holds the (OWNER-PENDING) numbers, and
        /// <c>DefenseReportBuilder.ApplyStakes</c> is the single debit -- pinned by
        /// SiegeLossStakesRegression and SiegeUntouchableRegression.</para>
        ///
        /// <para>The superseded WO-1139 position ("collector looting only, no bank theft",
        /// 2026-08-22) is recorded in StakeRules' header so nobody mistakes its prose for the
        /// ruling. Collector looting is REMOVED, so no double-bill is expressible.</para>
        ///
        /// <para>⚠ The flag's DEFAULT was flipped to ON by an edit-only pass and has not been
        /// gate-verified here — the CLI seat owns that proof.</para>
        ///
        /// <para>⛔ If this ever goes back to OFF, say WHY in this comment. "Default OFF" with no
        /// stated blocker is how a finished loop stays dark for a month.</para>
        ///
        /// <para>When OFF, <see cref="DeNelle.Village.SiegeScheduler"/> arms nothing and calls no
        /// WaveManager entry point, so the build's behaviour is byte-identical to before WO-1026.
        /// It still logs one line saying it is off — a silent no-op is indistinguishable from a
        /// broken scheduler, and "the base is never attacked" is the exact bug class this WO
        /// exists to close. Flip via PlayerPrefs "ff.siege" (0 = dark, 1 = on).</para>
        ///
        /// <para>This is PvE. It is NOT PvP: nothing here snapshots, exports or replays another
        /// player's base (that is WO-730, separately unbuilt). The report's
        /// <c>AttackerIdentity.Source</c> field is the seam a ghost-PvP source would plug into
        /// later — a source swap, not a second system.</para>
        /// NOT URL-activatable (it changes game state — deliberately excluded from s_urlActivatableFlags).
        /// </summary>
        public static bool Siege => Get("siege", defaultOn: true);

        /// <summary>
        /// WO-828 — the corner minimap plate (<c>HudMinimapWidget</c>) in the calm postures.
        ///
        /// <para>Default ON. Unlike the retired Bag MapTab flag (deleted 2026-09-05, WO-1396; the
        /// Realm Map is now reached from the Journey deck) - which shipped OFF because realm travel
        /// is a WO-827 stub and a visible tab would promise a journey the game cannot take - the minimap
        /// promises nothing it cannot deliver: it reads the hero, the seam objective and the live
        /// threats that ALREADY drive <c>HudCompassWidget</c>, so it is correct the moment it is
        /// drawn. It adds no camera and no RenderTexture (WO-828's cost rule), so there is no
        /// performance reason to ship it dark either.</para>
        ///
        /// <para>OFF hides the plate entirely (the widget is never built, so it costs nothing at
        /// all rather than being an invisible ticking widget). Flip without a rebuild via
        /// PlayerPrefs "ff.minimap" = 0. Visibility per posture stays owned by hud-areas.json —
        /// this flag is the master switch, NOT a posture rule.</para>
        /// </summary>
        public static bool Minimap => Get("minimap", defaultOn: false);

        /// <summary>
        /// Owner ruling 2026-08-20 — gates the founding "Default Town" (prebuilt ring) option.
        /// Owner, verbatim: <i>"we are going to flag the start with prebuilt as it still has issues,
        /// so flag that off to unblock"</i>.
        ///
        /// <para>Default ON. The player-facing contract is two valid starts: a movable prebuilt
        /// town or the blank Build Your Own template. The placed-structure path now applies the
        /// catalog's manual storefront correction before fitting, including the jeweler's
        /// render-proven +90 degree pitch.</para>
        ///
        /// <para>Nothing is deleted. "Default Town" works by setting
        /// <c>GameState.StrategicPlacementMigrated = false</c> so the Castle-load migration writer
        /// converts the baked ring into movable records — that whole path is intact and flipping
        /// PlayerPrefs "ff.defaulttown" = 1 restores the choice without a rebuild, which is exactly
        /// what it will need for its own re-test once the issues are fixed.</para>
        ///
        /// <para>The suppression is FlowTrace'd at the decision site, so a capture shows WHY the
        /// screen is missing rather than reading as a vanished screen.</para>
        /// </summary>
        /// ⭐ FLIPPED OFF 2026-08-23 (owner: <i>"im thinking we want to remove the default option and
        /// just have them place all buildings till we get this all ironed out"</i>).
        ///
        /// <para>⚠ NOTE FOR THE RECORD, because it is the second time: the 2026-08-20 ruling
        /// quoted above ALREADY said to flag this off — and the default was left ON. The flag was
        /// added and never flipped, so the option kept shipping and the owner had to rule the same
        /// thing twice. A ruling recorded but not applied is indistinguishable from no ruling.</para>
        ///
        /// <para>⛔ AND THE 2026-08-23 REASON IS SHARPER THAN THE 08-20 ONE. WO-1163 is re-authoring
        /// the economy underneath this: the Farm becomes a QUARRY, the Silo RETIRES entirely, the
        /// Forge is now the Weaponsmith and iron moves to a dedicated Iron Mine. A prebuilt ring
        /// hands every new player a town made of buildings that are being renamed, repriced and
        /// removed — it would bake the stale world in at founding, on the one screen a new player
        /// cannot avoid.</para>
        ///
        /// <para>Nothing is deleted and the path stays intact (see above). PlayerPrefs
        /// "ff.defaulttown" = 1 restores the choice with no rebuild, which is what its own re-test
        /// needs once WO-1163 lands and the ring can be re-authored against the new vocabulary.</para>
        public static bool FoundingDefaultTown => Get("defaulttown", defaultOn: true);

        /// <summary>
        /// WO-1042 (owner ruling 2026-08-16) — gates the STAKING bonus hook on the Jeweler's polish
        /// economy. What it grants is ATTEMPTS ONLY: +1 free re-roll per week for a native SKR staker,
        /// and +1 to the per-stone roll cap at 10k+ SKR. It NEVER touches a probability — a staker's
        /// roll is exactly as likely as a free player's, which is the fairness property the whole
        /// polish design rests on (see IPolishBonusProvider).
        /// <para>
        /// ⚠ DEFAULT OFF ON PURPOSE, AND THIS IS A COMPLIANCE DEFAULT, NOT A READINESS ONE. Apple and
        /// Google both restrict gating gameplay functionality on token holdings and have been actively
        /// enforcing; a Play-store build must therefore ship with the seam returning ZERO for everyone.
        /// A Seeker / dApp-store build can flip it on. Reading the flag HERE — once, inside
        /// PolishBonuses — is what keeps a platform check out of every call site.
        /// </para>
        /// Flip via PlayerPrefs "ff.stakingpolishbonus" = 1.
        /// </summary>
        // ── 2026-08-17: ON BY DEFAULT **ONLY** ON A dApp-STORE BUILD ────────────────────────
        // The owner asked to turn the staking bonus on now that Echoes of Elarion is published on
        // the Solana dApp Store. The comment above is explicit that a Seeker / dApp-store build MAY
        // flip it on — but ALSO that the OFF default is a COMPLIANCE default: Apple and Google both
        // restrict gating gameplay on token holdings and have been actively enforcing.
        //
        // ⛔ SO THIS IS NOT `defaultOn: true`. A blanket flip would make it true for a future
        // Play-store build from these same ProjectSettings, which is precisely what the compliance
        // note forbids — and it would do so silently, months later, in a build nobody re-read this
        // comment for. The compliance property has to survive a build target we have not made yet.
        //
        // ⚠ AND IT IS NOT GATED ON `SOLANA_SDK` EITHER, though that was the tempting shortcut:
        // SOLANA_SDK is set per-PLATFORM (Android) in ProjectSettings, so a Play-store Android
        // build would carry it too. It marks "this build can talk to a wallet", NOT "this build is
        // distributed somewhere that permits token-gated gameplay". Those are different questions
        // and conflating them is how a compliance default rots into a violation.
        //
        // `DAPP_STORE` is a DISTRIBUTION define: it says where the binary is going, which is the
        // only thing that actually answers the compliance question. It must be REMOVED from the
        // Android define list before any Play-store build. Pinned by StakingComplianceRegression.
        //
        // What it grants stays ATTEMPTS-ONLY (+1 weekly re-roll, +1 roll cap at 10k+ SKR) and never
        // touches a probability — a staker's roll is exactly as likely as a free player's. That
        // fairness property is the whole reason this is defensible at all; do not "improve" it into
        // better odds.
#if DAPP_STORE
        public static bool StakingPolishBonus => Get("stakingpolishbonus", defaultOn: true);
#else
        public static bool StakingPolishBonus => Get("stakingpolishbonus", defaultOn: false);
#endif

        /// <summary>WO-1679 / HEART-006 — the passive benefit ladder's distribution gate.
        /// Read in EXACTLY ONE PLACE: <c>HeartboundBonuses.Active</c>
        /// (Assets/_Modules/Core/Catalog/HeartboundBenefits.cs), which returns the zero
        /// provider whenever this is off, so no call site anywhere carries a distribution
        /// check.</summary>
        /// <remarks>
        /// ⚠ A SEPARATE FLAG FROM <see cref="StakingPolishBonus"/>, ON PURPOSE, even though the
        /// owner's Q-LADDER ruling (2026-09-10 13:12) MERGED the two ladders. The ladder is one;
        /// the GRANT CLASSES are two, and they carry different risk. StakingPolishBonus grants
        /// extra ATTEMPTS at an existing table — a fairness-neutral perk. This one grants
        /// RECURRING ECONOMIC ACCELERATION, bounded by a ceiling that is a design guardrail and
        /// not an anti-cheat control (Q-CLIENTECON).
        ///
        /// ⛔ BUT BE PRECISE ABOUT WHAT THE SEPARATION ACTUALLY BUYS — IT IS ONE-DIRECTIONAL, NOT
        /// TWO. Since the merge, NativeSkrPolishBonus reads its grant AMOUNTS through
        /// HeartboundBonuses, which reads THIS flag. So:
        ///   • turning StakingPolishBonus OFF kills the polish perk and leaves the economy — ✅;
        ///   • turning HeartboundPassives OFF kills the economy AND the polish perk — it is the
        ///     upstream switch, not an independent one.
        /// That is a consequence of there being one ladder, and it is the correct shape: the
        /// perk's amounts come from the tier, so a build that may not read a tier cannot grant
        /// them either. What the second flag buys is the ability to kill the perk WITHOUT killing
        /// the economy, which is the direction that actually gets used. Do not describe these as
        /// two independent kill switches; they are a switch and a sub-switch.
        ///
        /// ⛔ NOT `defaultOn: true` OUTSIDE DAPP_STORE, for the reason spelled out at length above
        /// StakingPolishBonus: the compliance property has to survive a build target we have not
        /// made yet, and `DAPP_STORE` is the DISTRIBUTION define — the only one that answers
        /// "is this binary going somewhere that permits gating gameplay on a holding". Heartbound
        /// is Seeker-only by owner ruling (2026-09-10 13:10, Q3: "Seeker only; Play never shows it").
        ///
        /// ⚠ ON BY DEFAULT ON THE dApp-store artifact is SAFE BEFORE THE FEATURE IS WIRED: with no
        /// backend answer accepted, every reader sees the zero provider — tier 0, no bonus, no
        /// attempts, no events. There is no default that pays.
        /// </remarks>
#if DAPP_STORE
        public static bool HeartboundPassives => Get("heartboundpassives", defaultOn: true);
#else
        public static bool HeartboundPassives => Get("heartboundpassives", defaultOn: false);
#endif

        /// <summary>WO-991 (owner ruling 2026-08-15) — KILL SWITCH for the Healing Caravan's mobile
        /// shell (HealingCaravanMobility: slow follow-the-hero crawl + glass HP + the status chip).
        /// Default ON: the shell SHIPPED 2026-08-15 and this flag exists so a felt-test regression
        /// can flatten the caravan back to the static HealingFountain-only behaviour WITHOUT a
        /// rebuild (PlayerPrefs "ff.caravanmobile" = 0). The flag gates ONLY the mobility/chip
        /// attach in StructureFactory — HealingFountain's out-of-battle Heart heal is untouched
        /// either way. The heal-FIELD unlock (later WO-991 slice) will gate separately.</summary>
        public static bool HealingCaravanMobile => Get("caravanmobile", defaultOn: true);

        /// <summary>WO-611 — when ON, the owner-designed COMBAT HUD renders inside the HudKit: a
        /// VIRTUAL D-PAD (cross/plus, steel body + gold chevrons + centre hub) for movement instead of
        /// the 4-round-button cluster; an oblong stadium ATTACK PILL (gold-trimmed, energy-sword) at the
        /// bottom-right thumb anchor; the Q/W/E/R abilities as round gold MEDALLIONS arcing up-left around
        /// the pill with a SOFT under-glow cooldown (not a hard clock sweep); the hot-swap action bar HOUSED
        /// in an obsidian panel with a gold-trim inner ring; the HP/MP bars recessed in an inset WELL inside
        /// the vitals plate; and an animated LOCK CROSSHAIR badge on the target frame (hud/crosshair_1|2|3:
        /// unlocked -> acquiring -> locked, bound to TargetModel.HasTarget/Locked). On the posture flip to
        /// hostile(prebattle|activebattle) it also calls PanelManager.CloseAll() so every other screen closes
        /// and ONLY the combat HUD renders. Default OFF — the shipping HUD is BYTE-IDENTICAL when OFF (every
        /// combat-HUD widget is flag-gated at its build site). PlayerPrefs "ff.combathud611" = 0 reverts.
        /// DEFAULT ON (owner 2026-07-08, F8-25): the v8 combat HUD is the approved design — after the
        /// FULL RESET wiped her PlayerPrefs override, the legacy battle HUD (old arrow pad / legacy
        /// right-thumb layout) resurfaced because this still defaulted OFF. Approved = default.</summary>
        public static bool CombatHud611 => Get("combathud611", defaultOn: true);

        /// <summary>UI MVVM migration (WO-744, landmine 1) — when ON, the ATB combat HUD
        /// (<see cref="DeNelle.BattleATB.BattleHudUgui"/>) binds a read-only snapshot
        /// <c>BattleHudVM</c> for its Skills/Item submenus (the catalog resolves —
        /// Defs.HERO_ABILITIES / ITEM_DEFS -> active hero class + usable abilities/items —
        /// move into the VM) instead of resolving them off its own held BattleState. The
        /// per-frame VISUAL ATB feel-sim (_visualAtb / TickVisualAtb) and the OnAction
        /// callback contract are UNTOUCHED on both paths. Default OFF: with the flag off the
        /// HUD behaves BYTE-IDENTICALLY to today (no VM path) so the owner can A/B the ATB
        /// feel. PlayerPrefs "ff.battlehudvm" = 1 to bind the snapshot VM.</summary>
        public static bool BattleHudVm => Get("battlehudvm", defaultOn: false);

        /// <summary>2026-07-07 sheathed-pose fallback (owner A/B): when a weapon has NO explicit
        /// "&lt;mesh&gt;@sheathed" registry entry, its DRAWN offset falls back onto the built-in back
        /// pose. OFF (default) = the fallback nudges POSITION ONLY — frame-safe, since the drawn
        /// rotation was authored in the HAND frame and composing it onto the chest-socket sheathe
        /// rotation is a frame mismatch (e.g. sword_A's drawn euler (117,-61,-111) swings the back
        /// carry arbitrarily). ON = the 0492d7dc behavior (position + rotation compose) as the
        /// BACKUP if position-only doesn't carry the town fix. Explicit @sheathed entries are
        /// identical under both. PlayerPrefs "ff.sheathdrawnrot" = 0 to flip back to pos-only.
        /// ⛔ THE DEFAULT IS BACK TO **OFF** (2026-08-20) — and the line that used to sit here is the
        /// reason it has to be written this loudly. It read: "2026-07-07 owner A/B: default ON —
        /// pos-only preserved the exact plank pose she flagged (felt-verdict from flag_00 10:25);
        /// trying the full compose next." That is an EXPERIMENT'S default, left switched on after
        /// the experiment ended — and the pose it was being A/B'd against, the diagonal BACK carry,
        /// was itself retired six weeks later by the owner's 2026-08-20 hip ruling. So the flag went
        /// on defending a carry that no longer exists.
        ///
        /// WHAT IT COST, measured on the live Knight (Builds/KnightGearProof/, play-mode capture):
        ///   [Flow:Equip]  sheathed long axis ... tiltFromVertical=0deg longAxisDotUp=-1   <- ruled
        ///   [Flow:Offset] sheathed FALLBACK (drawn 'sword_A' on back pose):
        ///                 pos=(0.01,0.03,-0.01) rot=(117.00,-2.00,110.00)                <- undone
        /// ComputeSheathRotation had just DERIVED the exact pose the owner asked for, and this flag
        /// composed sword_A's DRAWN euler — authored in the HAND bone's frame — straight on top of
        /// it. Measured result: the sheathed sword sat **81 deg off vertical, TIP UP**. That is the
        /// sword-across-the-waist in her screenshot, and no amount of reading ComputeSheathRotation
        /// would ever have shown it, because that method is right.
        ///
        /// The paragraph above this one already SAID pos-only is the frame-safe answer. The default
        /// simply disagreed with the documentation. Now it does not.
        ///
        /// THE FLAG ITSELF STAYS (CLAUDE.md sec 12 — never strip a seam): PlayerPrefs
        /// "ff.sheathdrawnrot" = 1 restores the compose for an A/B, and the toggle is still in the
        /// OwnerDevToolsOverlay list. Only the DEFAULT moved.</summary>
        public static bool SheathedDrawnRotFallback => Get("sheathdrawnrot", defaultOn: false);

        /// <summary>PET COMBAT (owner 2026-07-08) — gates whether deployed pets FIGHT. When OFF (default),
        /// pets are HARVEST / COMPANION only per docs/COMBAT_PIVOT_NORTHSTAR.md ("no pets in battle"; pets =
        /// autonomous harvesters in V1): a Defend pet's hunt/target/attack loop (<see cref="DeNelle.Pets.Pet"/>
        /// Update/Attack/OverlapSphere scan) NO-OPs — the pet acquires no target and deals no damage, and it
        /// earns no combat XP. (The XP half is no longer a FLAG concern at all: WO-993 retired PetProgression
        /// outright, so no pet registers as an IXpEarner in either flag state. HeroProgression is the only
        /// earner left. This flag still gates the FIGHT half in Pet.Update and PetHarvester.)
        /// The pet stays alive as a companion and <see cref="DeNelle.Pets.PetHarvester"/> keeps gathering
        /// (harvest no longer yields to a fight that can't happen). When ON, the full pet combat behaviour is
        /// restored. Default OFF — reversible: PlayerPrefs "ff.petcombat" = 1.</summary>
        public static bool PetCombat => Get("petcombat", defaultOn: false);

        /// <summary>Owner directive 2026-07-10: the ENTIRE Barracks feature is hidden for V1 —
        /// structure (building/mesh), the drillmaster NPC, and all barracks dialogue/training.
        /// When OFF (default): the baked CastleBarracks is hidden at runtime, BarracksNpcInjector
        /// no-ops, and the barracks dialogue/training entry points are unreachable. Disable-not-
        /// delete; flip ON for V2 via PlayerPrefs "ff.barracks" = 1.</summary>
        // WO-771 V1 (2026-07-26): the raid deploy loop pulls troops from the barracks-gated roster, so the
        // barracks must be reachable in normal play. Roster/training (TroopTrainingPanel/BarracksUnlock/
        // GameState.Army) already exists — flip ON. ff.barracks=0 to hide it again if it needs more polish.
        public static bool Barracks => Get("barracks", defaultOn: true);

        /// <summary>WO-703 / ticket BLANK-1 (owner ruling 2026-07-13 "should be completely flagged
        /// off for now"): the Colosseum / arena-entrance structure visual in the home hub. When OFF
        /// (default), <see cref="DeNelle.Village.HubStructureVisualInjector"/> skips the
        /// Colosseum_ArenaEntrance placement entirely — the structure model, its fitted
        /// StructureCollider, and anything parented to that host (attached emitters included) never
        /// spawn. Disable-not-delete; reversible via PlayerPrefs "ff.colosseum" = 1. The arena's
        /// interaction path (ArenaHeraldSpawner) stays independently gated by "ff.arena".</summary>
        public static bool Colosseum => Get("colosseum", defaultOn: false);

        /// <summary>WO-1073 - the FOUNDERS MONUMENT stand-in near the Heart of Elarion, and
        /// with it the ONLY world door onto the Benefactors of the Realm wall
        /// (<see cref="DeNelle.Core.UI.PanelId"/>.Benefactors).
        ///
        /// DEFAULT ON, and that is the point: owner ruling 2026-08-27 leaves the $500 tier
        /// switched OFF until the stand-in actually RENDERS (WO-1073 section 3.2 - "a threshold
        /// whose cosmetic cannot render is not authored yet"), so a default-OFF flag here would
        /// hold the tier closed forever.
        ///
        /// It exists as a flag anyway because the monument is new furniture in a hub whose
        /// blank-start look is owner-ruled (WO-703/BLANK-1 turned the Colosseum off for exactly
        /// that reason). If the owner felt-tests it and wants it gone, PlayerPrefs
        /// "ff.foundersmonument" = 0 removes the model, its collider and its interaction in one
        /// word - and takes the wall's door with it, which is why it is not off by default.</summary>
        /// <summary>
        /// The Benefactors wall's world door (WO-1073).
        ///
        /// ⭐ DEFAULT FLIPPED ON -> OFF, 2026-08-27, on device evidence. It shipped
        /// default-ON because the reasoning was "a default-OFF flag would hold the $500
        /// tier closed forever" - and that premise turned out to be false, because the
        /// stand-in DOES NOT RENDER. Its Addressables address
        /// (BenefactorsCatalog.StandInMonumentAssetKey) does not exist yet: the real
        /// monument is a custom FBX the owner is authoring with an artist. So on the
        /// owner's first tester build it fell back to grey primitive cubes she did not
        /// recognise - reported as "some type of new pillar" - and logged an ERROR on
        /// every hub load (F8 seq 3613-3616), burying her real findings in noise while
        /// she was trying to validate 191 items.
        ///
        /// ⭐ FLIP THIS BACK TO true THE MOMENT THAT ADDRESS RESOLVES. Nothing else needs
        /// to change - not the injector, not the door, not the panel, not the server. The
        /// $500 tier is waiting on real art, not on this flag.
        ///
        /// ⛔ Do NOT instead silence the loader's error logging to quieten this. That
        /// logging is shared with every other structure and is how a genuinely missing
        /// piece of art gets found (section 16: a missing bundle fails SILENTLY, and this
        /// log is one of the few things that does not). Turning off a feature whose art
        /// does not exist is the honest fix; muting the detector is not.
        /// </summary>
        public static bool FoundersMonument => Get("foundersmonument", defaultOn: false);

        /// <summary>The build palette's walls category tab. When OFF,
        /// <see cref="DeNelle.Village.BuildPaletteUI"/> renders only the Town / Defense
        /// quick-tabs and <c>BuildPaletteVM.ConfigureGroup</c> filters Wall rows out of the
        /// Structures grouping. Wall catalog rows stay loadable either way (saves/replay
        /// untouched). PlayerPrefs "ff.wallstab" = 0/1 overrides.</summary>
        // Owner D8 resolution 2026-08-09 (WO-1010 §7 D21 addendum) — defaultOn flipped
        // false -> true: Walls RETURNS as the "Castle Structures" display category on the
        // build screen's right-edge quick-tab stack (display rename only; the BuildType.Walls
        // key and build-categories.json keys are unchanged). This SUPERSEDES the 2026-07-13
        // ruling that parked the tab behind settlement building.
        // WARNING — PlayerPrefs-first trap: Get() reads the persisted "ff.wallstab" BEFORE
        // this default, so a machine where ff.wallstab=0 was ever written will NOT see this
        // flip until the pref is cleared or set to 1. A felt-test seat that still shows two
        // tabs is that machine, not a regression of this line.
        public static bool WallsTab => Get("wallstab", defaultOn: true);

        /// <summary>WO-VFX-POI (owner is red/green colorblind — callouts read by MOTION / SHAPE /
        /// LUMINANCE / VERTICALITY, never hue): when ON, <see cref="DeNelle.Village.PoiCalloutSystem"/>
        /// self-bootstraps and drives point-of-interest callouts off <see cref="DeNelle.Village.PoiRegistry"/>
        /// — a small looping ground AURA on near-field harvest nodes (mine reserves / harvest sites /
        /// active collectors) that appears within ~28m and hands off to the interact prompt on arrival
        /// (capped to the nearest ~6 to respect the VFX loop budget), plus a tall looping PILLAR/beacon on
        /// far-field landmarks (enemy fortress outposts) visible from range until cleared. Presentation
        /// only — no gameplay/economy change; null-safe (no-ops until the "Poi_*" catalog keys exist).
        /// Default OFF (dark-ship until the catalog rows are generated + owner felt-verifies). PlayerPrefs
        /// "ff.poicallouts" = 0 to disable.</summary>
        // Owner asked to felt-test the node auras + fortress beacon (2026-07-10) — flipped ON for
        // build 2; the catalog Poi_* keys are authored. Set ff.poicallouts=0 to turn off if too busy.
        public static bool PoiCallouts => Get("poicallouts", defaultOn: true);

        // WO-682 (owner ruling 2026-07-12 "have that ff removed and set to lock in build"):
        // ff.strategicplacement is REMOVED — strategic building placement (WO-673) is
        // ALWAYS ON in every build. All former call sites are the unconditional TRUE path.

        /// <summary>DUNGEON CAMERA — FIRST-PERSON, now an OPT-IN A/B (WO-920, owner 2026-08-07:
        /// "stationary camera view for in dungeons"). <b>DEFAULT FLIPPED ON->OFF.</b>
        /// <para>HISTORY, because the reversal only makes sense with it: FPV was made default on 2026-07-26
        /// as a WORKAROUND — an architect chose first-person traversal INSTEAD OF raising the ~2.8u ceiling,
        /// since the old top-down iso rig floated at/above the roofline and the room could not be seen.
        /// WO-919 has since removed the premise: composed rooms are now 4 m walls WITH a ceiling slab
        /// (RoomForgeCanon.WallHeight/CeilingThickness) and are relit dark. With the room properly enclosed,
        /// an over-the-shoulder camera seated under the ceiling works, and the owner wants the calm framing
        /// rather than a free-look that drifts. <see cref="DeNelle.Dungeons.DungeonCameraRig"/>.ResolveMode
        /// therefore now returns OverShoulder by default.</para>
        /// <para>OFF (default) = locked over-the-shoulder: no free-look, obstacle-avoidance off, seat from
        /// DeNelle.Core.World.DungeonCameraProfile, and NO combat reframe (the fight uses the same calm seat).
        /// ON = the full FPV, preserved intact and NOT deleted: independent yaw+pitch look layer (right-half
        /// touch-drag / mouse-delta, pitch-clamped ±70, decoupled from the movement heading), hero body
        /// renderers hidden (ShadowsOnly), and arena fights temporarily forcing over-the-shoulder via
        /// <c>SetCombatFraming</c>. Set PlayerPrefs "ff.dungeonfpv" = 1 to opt back into first-person.</para>
        /// <para>SCOPE: this flag only reaches the TWO hand-built dungeon scenes that actually carry a
        /// DungeonCameraRig (Dungeon_HealersCottage, Dungeon_FolksGranary). The composed dg_* dungeons and
        /// KayKitChallengeOutpost bake no camera at all and run DeNelle.Village.SmartMobileCamera instead —
        /// their locked seat is ApplyDungeonProfileIfNeeded there, not this flag.</para></summary>
        public static bool DungeonFpv => Get("dungeonfpv", defaultOn: false);

        /// <summary>DUNGEON CAMERA — LEGACY TOP-DOWN ISO escape hatch (owner 2026-07-17). When ON, the
        /// dungeon rig restores the pre-2026-07-17 fixed top-down isometric framing (pitch ~52, height-capped
        /// for the ~4u ceiling) instead of the new over-the-shoulder default. Kept so the old look is one
        /// PlayerPref away for an A/B. <see cref="DungeonFpv"/> wins over this if both are set. Default OFF.
        /// PlayerPrefs "ff.dungeoniso" = 1 to restore the top-down rig.</summary>
        public static bool DungeonCameraIso => Get("dungeoniso", defaultOn: false);

        /// <summary>HUB NATURAL DRESSING (owner 2026-08-02: "can we add more trees and natural items in
        /// world? Feels very empty and boring."). When ON (default), the
        /// <see cref="DeNelle.Village.World.HubFoliageInjector"/> scatters trees / rocks / bushes around the
        /// castle hub at RUNTIME -- deterministically (fixed seed), capped at a mobile instance budget,
        /// colliders stripped, and WITHOUT ever hand-editing Main_Castle_Overworld.unity (CLAUDE.md SS3)
        /// or invalidating the baked navmesh. Props come from the git-tracked Resources nature set
        /// (Arena/Tree_*, Arena/Rock_*, Hedges/Fence_Shrub); a missing prop warns and is skipped
        /// (CLAUDE.md SS4). Keep-out zones protect the Heart plaza, every existing structure and the
        /// walked gate routes. Default ON so the owner SEES the draft; PlayerPrefs "ff.hubfoliage" = 0
        /// turns it off with NO rebuild (per-tier const toggles live in the injector).</summary>
        public static bool HubFoliage => Get("hubfoliage", defaultOn: true);

        /// <summary>THE HOLLOW ROADS (owner 2026-08-16: "place a portal to simple tunnel system that
        /// will drop into the new biomes"). Gates the whole hub -> portal -> tunnel -> four-biome-drop
        /// spoke: the derived Hollow Roads portal row in <c>DungeonWorldPortalSpawner</c> and the four
        /// biome drops <c>HollowRoadsDropInjector</c> seats inside the tunnel scene. (Named in
        /// &lt;c&gt; rather than &lt;see cref&gt; on purpose — both types live in DeNelle.Village, which
        /// Core does not reference, so a cref to them cannot resolve from this assembly.)
        /// <para>
        /// DEFAULT ON, and the reasoning is worth keeping because the obvious precedent points the
        /// other way. The Bag's Map tab shipped OFF (the retired ff.maptab, deleted 2026-09-05) because
        /// realm travel is a WO-827 STUB and the areas genuinely do not connect - the map itself is
        /// now reached read-only from the Journey deck. This is NOT that case: the four destinations
        /// (Goldfields / Stoneback / Mirewood / Ashwood) are REAL walkable ground in the merged
        /// overworld today -- painted by ExteriorTerrainBuilder as four directional terrain biomes,
        /// covered by the single baked navmesh, classified by ZoneManager and already roamed by
        /// OverworldEncounterSpawner. A drop lands somewhere the player could have WALKED. What is
        /// missing is CONTENT in those regions, not the regions.
        /// </para>
        /// <para>
        /// It is also self-hiding before the bake: the tunnel scene <c>dg_hollow_roads</c> is only
        /// registered once the graph is composed, and the portal's def is injected behind an
        /// <c>Application.CanStreamedLevelBeLoaded</c> test -- so on an un-baked tree nothing is
        /// placed at all rather than a door to a missing scene. PlayerPrefs "ff.biomeroads" = 0
        /// removes the spoke with no rebuild.
        /// </para></summary>
        public static bool BiomeRoads => Get("biomeroads", defaultOn: true);

        /// <summary>HERO GAIT FORENSICS — the per-frame gait/camera recorder
        /// <c>HeroGaitForensics</c> (DeNelle.Village; named in &lt;c&gt; because Core does not
        /// reference Village). WHY IT EXISTS: owner F8 2026-07-12 ("look at the data for walking and
        /// running, hip bones and smart camera") and again WO-965 ("Mage faces northwest when running
        /// north") — it is the instrument that made both captures decidable, so per CLAUDE.md §12 it
        /// STAYS IN THE CODE, permanently. It is FLAGGED OFF, never stripped.
        /// <para>
        /// WHY IT IS OFF: the recorder self-bootstraps from a
        /// <c>RuntimeInitializeOnLoadMethod</c>, lives in a SHIPPING assembly with no <c>#if</c>
        /// guard and no define constraint (unlike DeNelle.DevTools, which is stripped from a release
        /// player), and its LateUpdate does a 20-field boxed <c>string.Format</c> +
        /// <c>StreamWriter.WriteLine</c> into <c>persistentDataPath/gait-forensics.csv</c> plus a
        /// <c>GetCurrentAnimatorClipInfo</c> allocation EVERY FRAME. Until 2026-08-16 it read a raw
        /// PlayerPrefs key that DEFAULTED ON and was declared in NO flag table — so it ran in the
        /// release Seeker APK, ~1,200 boxed structs/sec at 60fps and an unbounded file on the
        /// player's device, with no dev menu, no UI and no URL able to turn it off. Declaring it here
        /// is what gives it an off-switch and an owner-facing toggle.
        /// </para>
        /// <para>
        /// TURNING IT BACK ON for an investigation: the Defenders/Debug menu, or PlayerPrefs
        /// "ff.gaitforensics" = 1 on the device. NOT URL-activatable (it writes a file).
        /// </para></summary>
        public static bool GaitForensics => Get("gaitforensics", defaultOn: false);

        /// <summary>JUPITER SWAP PANEL (WO-43) — gates <c>JupiterSwapBootstrap</c> (DeNelle.Web3),
        /// the <c>RuntimeInitializeOnLoadMethod</c> that auto-spawns the crypto swap-panel host in
        /// Title / HeroSelect / PetSelect and any <c>Dungeon_*</c> scene. DEFAULT OFF, same idiom and
        /// same reason as <see cref="SkrPreview"/> and <see cref="RealmStorePurchase"/>.
        /// <para>
        /// WHY OFF (store-hardening, Path A): those two flags were deliberately flipped OFF so the
        /// honest ZERO-CRYPTO store build ships NO crypto marketing or purchase surface. This
        /// bootstrap was the hole in that decision — it was gated by NOTHING, shipped
        /// unconditionally (its UXML lives under Assets/_Modules/Web3/Resources/), and so put a swap
        /// CTA host into the very build the ruling stripped crypto out of. Secondary reason it must
        /// not ship un-gated: it is a UXML panel, and UXML does not render in player builds
        /// (CLAUDE.md §8), so on device it would most likely draw blank — a broken surface rather
        /// than a working one.
        /// </para>
        /// <para>
        /// NOT DELETED ON PURPOSE: whether the swap CTA should exist at all is an OWNER ruling, so
        /// the flag keeps it reversible in one value — PlayerPrefs "ff.jupiterswap" = 1, or the
        /// Defenders/Debug menu. NOT URL-activatable (monetization surface — excluded from the
        /// allow-list, same as RealmStorePurchase).
        /// </para></summary>
        /// <para>
        /// WO-1377 (2026-09-09): compiled out entirely under GOOGLE_PLAY. The property NAME
        /// <c>JupiterSwap</c> and the key literal <c>"jupiterswap"</c> both land in IL2CPP's
        /// global-metadata.dat as shipped identifiers, and its only runtime caller
        /// (<c>JupiterSwapBootstrap</c>) lives in DeNelle.Web3, which is already excluded on
        /// Play by <c>DeNelle.Web3.asmdef:17</c> <c>"!GOOGLE_PLAY"</c>. So on Play the flag
        /// gates nothing and only contributes a token. ⛔ NOT deleted — the dApp lane still
        /// reads it, and <c>ShippedSurfaceGateRegression</c> Case 3 FAILS if the declaration
        /// disappears from this file's source.
        /// </para>
#if !GOOGLE_PLAY
        public static bool JupiterSwap => Get("jupiterswap", defaultOn: false);
#endif

        /// <summary>SECURITY (store-hardening Path A): TRUE in the Editor or any Development build,
        /// FALSE in a release/store build (BuildOptions.None → Debug.isDebugBuild is false). Dev-only
        /// tooling uses this as its <c>defaultOn</c> so the owner keeps the tool while developing but it
        /// STRIPS OFF (defaults to hidden) in a public/store APK. The PlayerPrefs "ff.&lt;name&gt;"
        /// override still wins, so a developer can flip the tool back on on any build.</summary>
        private static bool IsDevBuild => Application.isEditor || Debug.isDebugBuild;

        /// <summary>
        /// TRUE only in a build compiled with the <c>TESTER_BUILD</c> scripting define — the APK that
        /// goes to Firebase App Distribution, never the one that goes to the Solana dApp Store.
        /// <para>
        /// ⛔ WHY THIS EXISTS (owner ruling 2026-08-24: <i>"if it's a dev build then we can just leave
        /// the flag on because it's only going to the tester. It's not going to the Solana store"</i>).
        /// Until today THE TWO WERE THE SAME ARTIFACT. <c>AndroidBuild.BuildSeekerApk</c> produces a
        /// <c>BuildOptions.None</c> RELEASE apk for both destinations, so <see cref="Debug.isDebugBuild"/>
        /// is FALSE on the tester build too — which is exactly why owner-facing tooling kept
        /// disappearing on the very device it was built for. There was no way for code to tell the two
        /// apart, so every tool had to choose between shipping publicly or being unreachable.
        /// </para>
        /// <para>
        /// ⭐ THE DEFAULT DIRECTION IS DELIBERATE AND MUST NOT BE INVERTED: the define is OPT-IN, so its
        /// ABSENCE means store-safe. A store build cannot ship dev tooling by forgetting a flag — only
        /// by explicitly adding one. Fail-safe, not fail-open.
        /// </para>
        /// </summary>
#if TESTER_BUILD
        private static bool IsTesterBuild => true;
#else
        private static bool IsTesterBuild => false;
#endif

        /// <summary>
        /// TRUE on the editor and on a DESKTOP standalone player; FALSE on Android/iOS.
        /// Owner-facing dev tooling defaults ON here and OFF on device, which threads the needle
        /// between two of her standing asks: fast iteration on the EXE, and clean device
        /// screenshots with no chips in shot. It also keeps the tooling out of any store APK by
        /// construction rather than by remembering to flip a flag.
        /// </summary>
        private static bool IsDesktop =>
            Application.isEditor
            || Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.OSXPlayer
            || Application.platform == RuntimePlatform.LinuxPlayer;

        /// <summary>WO-1331 — arms the REMOTE CANONICAL CATALOG SEAM (CanonicalJson.Source).
        /// <para>
        /// ⛔ DEFAULT OFF, AND OFF MEANS ABSENT, NOT IDLE. With this flag unset,
        /// <see cref="DeNelle.Core.RemoteCatalogService"/>.Install() returns BEFORE assigning
        /// CanonicalJson.Source and Bootstrap() returns BEFORE starting any fetch, so a default
        /// build does not merely behave like today — it runs the identical code path, with
        /// CanonicalJson.Source still holding the LocalJsonCatalogSource from its own field
        /// initializer. Nothing is constructed, nothing is polled.
        /// </para>
        /// <para>
        /// When ON, the five allowlisted canonical catalogs (RemoteCatalogOverrides.Allowlist)
        /// may be served from the database instead of the copy compiled into the player — which
        /// is the only thing that makes "data-driven" mean "tunable without a rebuild"
        /// (docs/reference/TUNABLE_LEVER_INVENTORY.md §2). Every failure — unreachable, 404,
        /// malformed, truncated, empty, denied path — falls through to the compiled copy.
        /// </para>
        /// PlayerPrefs "ff.catalogremote" = 1. The remote twin of this arm is the tunables-rail
        /// knob "catalog.remoteEnabled", read only once it is registered.</summary>
        public static bool RemoteCatalogs => Get("catalogremote", defaultOn: false);

        /// <summary>Per-feature resolve: PlayerPrefs override ("ff.&lt;name&gt;" = 0/1) wins, else the default.</summary>
        private static bool Get(string name, bool defaultOn)
        {
            int pref = PlayerPrefs.GetInt("ff." + name, -1);
            if (pref == 0) return false;
            if (pref == 1) return true;
            return defaultOn;
        }

        // ── WO-443 — WebGL one-session URL activation (?trace=1) ──────────────────
        private static bool s_urlActivationChecked;

        // SECURITY (audit B-URLFLAG): a URL query can ONLY activate flags on this explicit
        // allow-list. Anything else (gameplay / economy / monetization flags) is rejected so
        // a crafted link can never flip game state. Today the only URL-activatable flag is the
        // diagnostic web-trace toggle. Map: URL query key → PlayerPrefs flag key.
        private static readonly System.Collections.Generic.Dictionary<string, string> s_urlActivatableFlags =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "trace", "ff.webtrace" },      // diagnostic-only; safe to flip per session
                // Seekerthon stake-rewards DEMO. Safe to allow-list despite the "no monetization flags"
                // rule because the surface is READ-ONLY PRESENTATION: it opens an informational panel
                // over a MOCK stake and mutates NO game/economy state, so a crafted ?stakedemo=1 link
                // can at most show a cosmetic panel — it can never flip real state. Default OFF => prod
                // is unaffected unless the demo link is used.
                { "stakedemo", "ff.stakedemo" },
                // "Powered with SKR" grant PREVIEW. Same rationale as stakedemo: READ-ONLY
                // presentation (branding + a labeled testnet preview) — no wallet call, no state
                // mutation — so a crafted ?skrpreview=1 link can at most show an info panel. Default
                // OFF => prod is unaffected unless the grant-recording link is used.
                { "skrpreview", "ff.skrpreview" },
            };

        /// <summary>
        /// WO-443 — reads <see cref="Application.absoluteURL"/> on WebGL and, if it carries
        /// <c>?trace=1</c> (or <c>&amp;trace=1</c>), turns the <see cref="WebTrace"/> flag ON for THIS
        /// session only (writes PlayerPrefs "ff.webtrace"=1) so support can activate web tracing for a
        /// single player without a rebuild. Idempotent (runs its parse once) and safe to call on every
        /// platform — on editor/standalone <c>absoluteURL</c> is empty so it is a no-op. Never throws.
        /// </summary>
        public static void ApplyUrlActivationOnce()
        {
            if (s_urlActivationChecked) return;
            s_urlActivationChecked = true;
            try
            {
                string url = Application.absoluteURL;
                if (string.IsNullOrEmpty(url)) return;

                int q = url.IndexOf('?');
                if (q < 0) return;
                string query = url.Substring(q + 1);

                foreach (var pair in query.Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    string key = (eq < 0 ? pair : pair.Substring(0, eq)).Trim();
                    string val = (eq < 0 ? "" : pair.Substring(eq + 1)).Trim();

                    // Allow-list gate (B-URLFLAG): only diagnostic flags here may be set via URL.
                    if (!s_urlActivatableFlags.TryGetValue(key, out string prefKey))
                    {
                        if (!string.IsNullOrEmpty(key))
                            Debug.LogWarning($"[FeatureFlags] URL flag '{key}' is NOT URL-activatable — rejected.");
                        continue;
                    }

                    if (val == "1" || val.Equals("true", System.StringComparison.OrdinalIgnoreCase))
                    {
                        PlayerPrefs.SetInt(prefKey, 1);
                        PlayerPrefs.Save();
                        Debug.Log($"[FeatureFlags] ?{key}=1 detected — '{prefKey}' activated for this session.");
                        // continue (not return) so MULTIPLE allow-listed flags in one URL all activate
                        // (e.g. ?trace=1&stakedemo=1). Each is independently allow-list-gated above.
                        continue;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[FeatureFlags] URL trace-activation parse skipped: " + ex.Message);
            }
        }

#if UNITY_EDITOR
        // ── Editor flag toggles (Defenders > Debug) — no registry editing, no Play Mode needed.
        // Flip, then re-open the panel to see it. Checkmark shows the current resolved state.
        private const string BlinkChromeMenu = "Defenders/Debug/Blink Chrome (hide our UI dressing)";

        [UnityEditor.MenuItem(BlinkChromeMenu, priority = 200)]
        private static void ToggleBlinkChrome()
        {
            bool on = !BlinkChrome;                       // resolved value, then invert
            PlayerPrefs.SetInt("ff.blinkchrome", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.blinkchrome = " + (on ? "ON (Blink panels show clean)" : "OFF (our chrome)"));
        }

        [UnityEditor.MenuItem(BlinkChromeMenu, validate = true)]
        private static bool ToggleBlinkChromeValidate()
        {
            UnityEditor.Menu.SetChecked(BlinkChromeMenu, BlinkChrome);
            return true;
        }

        // WO-482 — flip the overworld-encounter battle loop on/off from the menu (no PlayerPrefs
        // fiddling). ON => orc reps spawn in the merged world; engage one -> the isolated BattleArena.
        private const string OverworldEncounterMenu = "Defenders/Debug/Overworld Encounter (WO-482 battle loop)";

        [UnityEditor.MenuItem(OverworldEncounterMenu, priority = 201)]
        private static void ToggleOverworldEncounter()
        {
            bool on = !OverworldEncounter;
            PlayerPrefs.SetInt("ff.overworldencounter", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.overworldencounter = " + (on ? "ON (orc reps spawn in merged world; engage -> BattleArena)" : "OFF"));
        }

        [UnityEditor.MenuItem(OverworldEncounterMenu, validate = true)]
        private static bool ToggleOverworldEncounterValidate()
        {
            UnityEditor.Menu.SetChecked(OverworldEncounterMenu, OverworldEncounter);
            return true;
        }

        // WO-512 — flip the battle soft lock-on on/off from the menu (no PlayerPrefs fiddling).
        // ON => auto-lock nearest enemy on engage + HUD lock toggle routes through HeroTargetIndicator.
        private const string LockOnMenu = "Defenders/Debug/Lock-On (WO-512 battle soft lock)";

        [UnityEditor.MenuItem(LockOnMenu, priority = 202)]
        private static void ToggleLockOn()
        {
            bool on = !LockOn;
            PlayerPrefs.SetInt("ff.lockon", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.lockon = " + (on ? "ON (auto-lock nearest enemy on engage; HUD lock routes through HeroTargetIndicator)" : "OFF"));
        }

        [UnityEditor.MenuItem(LockOnMenu, validate = true)]
        private static bool ToggleLockOnValidate()
        {
            UnityEditor.Menu.SetChecked(LockOnMenu, LockOn);
            return true;
        }

        // Seekerthon — flip the stake-rewards demo surface from the menu (no PlayerPrefs fiddling).
        // ON => a seeded ~1M SKR stake opens the read-only StakeRewardsPanel on play.
        private const string StakeDemoMenu = "Defenders/Debug/Stake Rewards Demo (Seekerthon SKR)";

        [UnityEditor.MenuItem(StakeDemoMenu, priority = 203)]
        private static void ToggleStakeDemo()
        {
            bool on = !StakeDemo;
            PlayerPrefs.SetInt("ff.stakedemo", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.stakedemo = " + (on ? "ON (seeded ~1M SKR stake opens StakeRewardsPanel on play)" : "OFF"));
        }

        [UnityEditor.MenuItem(StakeDemoMenu, validate = true)]
        private static bool ToggleStakeDemoValidate()
        {
            UnityEditor.Menu.SetChecked(StakeDemoMenu, StakeDemo);
            return true;
        }

        // "Powered with SKR" grant PREVIEW — flip the aspirational SKR story on/off from the menu.
        // ON => a "Powered with SKR" badge shows on the Title screen and opens the SkrShowcasePanel.
        private const string SkrPreviewMenu = "Defenders/Debug/Powered with SKR (grant preview)";

        [UnityEditor.MenuItem(SkrPreviewMenu, priority = 204)]
        private static void ToggleSkrPreview()
        {
            bool on = !SkrPreview;
            PlayerPrefs.SetInt("ff.skrpreview", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.skrpreview = " + (on ? "ON (Title 'Powered with SKR' badge -> SkrShowcasePanel; no wallet call)" : "OFF"));
        }

        [UnityEditor.MenuItem(SkrPreviewMenu, validate = true)]
        private static bool ToggleSkrPreviewValidate()
        {
            UnityEditor.Menu.SetChecked(SkrPreviewMenu, SkrPreview);
            return true;
        }

        // WO-611 — flip the owner-designed combat HUD on/off from the menu (no PlayerPrefs fiddling).
        // ON => virtual d-pad + attack pill + Q/W/E/R medallion arc + housed action bar + HP/MP inset
        // + lock crosshair badge; hostile posture closes all other screens (PanelManager.CloseAll).
        private const string CombatHud611Menu = "Defenders/Debug/Combat HUD (WO-611 owner-designed)";

        [UnityEditor.MenuItem(CombatHud611Menu, priority = 205)]
        private static void ToggleCombatHud611()
        {
            bool on = !CombatHud611;
            PlayerPrefs.SetInt("ff.combathud611", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.combathud611 = " + (on
                ? "ON (owner-designed combat HUD: d-pad/pill/medallion-arc/housed-bar/HP-MP-inset/lock-crosshair; hostile -> CloseAll)"
                : "OFF (shipping HUD unchanged)"));
        }

        [UnityEditor.MenuItem(CombatHud611Menu, validate = true)]
        private static bool ToggleCombatHud611Validate()
        {
            UnityEditor.Menu.SetChecked(CombatHud611Menu, CombatHud611);
            return true;
        }

        // Hero gait forensics — the per-frame CSV recorder. OFF by default (it writes a file every
        // frame in a shipping assembly); this menu is the owner-facing way to arm it for a capture.
        private const string GaitForensicsMenu = "Defenders/Debug/Hero Gait Forensics (per-frame CSV)";

        [UnityEditor.MenuItem(GaitForensicsMenu, priority = 206)]
        private static void ToggleGaitForensics()
        {
            bool on = !GaitForensics;
            PlayerPrefs.SetInt("ff.gaitforensics", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.gaitforensics = " + (on
                ? "ON (HeroGaitForensics: per-frame gait-forensics.csv + [Flow:GaitF] change lines)"
                : "OFF (no recorder, no csv - the shipping default)"));
        }

        [UnityEditor.MenuItem(GaitForensicsMenu, validate = true)]
        private static bool ToggleGaitForensicsValidate()
        {
            UnityEditor.Menu.SetChecked(GaitForensicsMenu, GaitForensics);
            return true;
        }

        // Jupiter swap panel — the WO-43 crypto swap CTA host. OFF by default (store-hardening
        // Path A: the shipping build carries no crypto surface).
        // WO-1377: nested inside the enclosing #if UNITY_EDITOR because the property it toggles
        // no longer exists under GOOGLE_PLAY. An editor compile carrying -ExtraScriptingDefines
        // GOOGLE_PLAY would otherwise fail here.
#if !GOOGLE_PLAY
        private const string JupiterSwapMenu = "Defenders/Debug/Jupiter Swap Panel (crypto CTA)";

        [UnityEditor.MenuItem(JupiterSwapMenu, priority = 207)]
        private static void ToggleJupiterSwap()
        {
            bool on = !JupiterSwap;
            PlayerPrefs.SetInt("ff.jupiterswap", on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FeatureFlags] ff.jupiterswap = " + (on
                ? "ON (JupiterSwapBootstrap spawns the swap-panel host in Title/HeroSelect/PetSelect/Dungeon_*)"
                : "OFF (no swap host spawns - the zero-crypto store default)"));
        }

        [UnityEditor.MenuItem(JupiterSwapMenu, validate = true)]
        private static bool ToggleJupiterSwapValidate()
        {
            UnityEditor.Menu.SetChecked(JupiterSwapMenu, JupiterSwap);
            return true;
        }
#endif  // !GOOGLE_PLAY  (WO-1377 — the Jupiter debug menu follows its property)
#endif
    }
}
