// =============================================================================
// PanelRouter — a tiny, reflection-free registry that lets any assembly OPEN a
// named gameplay panel by id without referencing the panel's type (DEF-213).
// -----------------------------------------------------------------------------
// THE PROBLEM (DEF-213): building interactions were generic. BuildingInteractable
// (DeNelle.Village) needs to open panels that live in DeNelle.HUD
// (HeroTalentPanel, PetSkillTreePanel, CosmeticShopPanel) — but Village must NOT
// reference HUD (CLAUDE.md §5). The old code bridged that gap with
// System.Reflection (FindAnyObjectByType(typeName).Toggle()), which (a) was fragile,
// (b) used Toggle() so a 2nd interaction CLOSED the panel, and (c) had no way to
// map a SPECIFIC building to its ONE correct panel — every building funnelled
// through the same handful of reflection calls.
//
// THE FIX: both DeNelle.Village and DeNelle.HUD already reference DeNelle.Core.
// Each panel registers a plain delegate ("open me") here under a stable
// PanelId. A caller in ANY assembly opens the right panel with one call —
// PanelRouter.Open(PanelId.HeroTalents) — with NO reflection and NO cross-asmdef
// type reference. The registered open action is the panel's own Open()/Show(),
// which already routes through PanelManager (DEF-212), so the one-panel-at-a-time
// rule still holds.
//
// Pure static state, reset on domain reload like PanelManager. No scene object.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// Stable identifiers for the routable gameplay panels. A building (or any
    /// other interactable) names the panel it opens by id; the panel registers
    /// its open action against the same id. Adding a panel = add one enum value
    /// + one Register call in that panel.
    /// </summary>
    public enum PanelId
    {
        /// <summary>RETIRED (eyes-sweep 2026-07-06) — the legacy Hero Talents panel was deleted
        /// (2026-07-03 S6) and its last flag-gated route removed; nothing registers or opens this id.
        /// The Arcane Tower / talents route is <see cref="HeroSkillTree"/>. Value kept so
        /// default(PanelId) stays a defined member; do NOT register new panels against it.</summary>
        HeroTalents = 0,
        /// <summary>Village crafting bench — the Workshop.</summary>
        Crafting = 1,
        /// <summary>Resource-building upgrades — Farm / Lumbermill / Forge / Armorer / Mine.</summary>
        BuildingUpgrade = 2,
        /// <summary>Cosmetic shop — the Marketplace / Realm Store.</summary>
        CosmeticShop = 3,
        // RETIRED (2026-07-08): PetSkillTree = 4 removed — the pet SKILL-TREE stack was deleted (dead
        // content; pets are harvest/companion-only per docs/COMBAT_PIVOT_NORTHSTAR.md). Value 4 is left
        // unused so the following members keep their numeric values; nothing registers/opens a pet tree.
        /// <summary>Party weapon/armor shop — the native code-built MVVM gear store (PartyShopPanelMvvm).</summary>
        PartyShop = 5,
        /// <summary>Quest / rumor board — the real story board (RumorBoardPanel, WO-304/436).
        /// Registered by DialogueCommandBridge so the HUD context button can open it.</summary>
        RumorBoard = 6,
        /// <summary>Knight skill tree — the code-built MVVM HeroSkillTreePanelMvvm (replaces the
        /// empty-in-builds UIDocument HeroTalentPanel). Opened from the inventory's "Skills" tab.</summary>
        HeroSkillTree = 7,
        /// <summary>Ability loadout chooser — HeroLoadoutPanelMvvm (W/E/R equip). Opened from the
        /// skill-tree panel's "Equip" button.</summary>
        HeroLoadout = 8,
        /// <summary>Consumable crafting / alchemy bench — CraftingPanelMvvm (combine dropped
        /// ingredients into potions/bombs). SEPARATE from the gear Workshop (PanelId.Crafting).</summary>
        ConsumableCrafting = 9,
        /// <summary>Jeweler jewelry crafting bench — JewelerPanelMvvm (combine a base ring/amulet +
        /// gems into a higher-tier accessory, WO-553). SEPARATE from the Apothecary + Forge lanes.</summary>
        JewelerCrafting = 10,
        /// <summary>Character / Gear Preview paper-doll — EquipmentPanel (central 3D hero + the five
        /// equipment slot plates). Opened by tapping the hero portrait in the inventory.</summary>
        EquipmentPanel = 11,
        /// <summary>Game Guide / tutorial codex — GameGuidePanel (tabbed help reference: tabs left,
        /// body right). Opt-in, opened from the Settings panel's "Game Guide" button (WO-588).</summary>
        GameGuide = 12,
        /// <summary>Realm Store — the SKR/SOL/USDC five-pack monetization store (PackStore,
        /// DeNelle.Wallet). Host-free: PackStoreBootstrap registers this opener at boot and
        /// find-or-spawns the store on first open (merchant "Realm Store" option + ?realmstore=1
        /// demo URL). Separate from the cosmetic wardrobe (PanelId.CosmeticShop).</summary>
        RealmStore = 13,
        /// <summary>Hero inventory (bag). Registered scene-independently by
        /// HeroInventoryController's boot hook (owner 07-06 "Clicking bag doesnt do anything":
        /// the old event chain's ONLY listener, HeroEquipHud, is scene-whitelisted and never
        /// spawns in Main_Castle_Overworld — 0 subscribers, log-proven. The kit Bag button now
        /// routes here, reflection-free, no scene whitelist to keep in sync).</summary>
        Inventory = 14,
        /// <summary>Realm Map — the WO-826 full-screen parchment overworld (Elarion + the five
        /// fog-shrouded regions from dual-copy realm-map.json). Registered scene-independently
        /// by RealmMapPanel (spawned by RealmMapPanelBootstrap); opened by the Journey deck's
        /// "Realm Map" card (WO-1396, 2026-09-05 - the ONE public door; the HUD kit Map button and
        /// the flag-gated Bag route are both retired) and the DevPanel "Open Realm Map" entry.
        /// Travel stays a WORDED stub until the WO-827 discovery/travel ledger.</summary>
        RealmMap = 15,
        /// <summary>WO-911 — the unified MANAGE / QUEUES screen: one tabbed door onto all three
        /// production lines (Builder / Train / Research) with per-item Finish Now, cancel-with-
        /// 100%-refund, bump-up and the Echo-gated extra-slot purchase, plus the affordability
        /// browser absorbed from WO-905. Registered scene-independently by ManageScreenPanel
        /// (spawned by ManageScreenBootstrap). This is what the bar's re-pointed "Upgrade" face
        /// opens (owner ruling Q10+Q13, 2026-08-06); it SUPERSEDES the old ObsidianQueueHud modal
        /// and the undiscoverable Builders-chip double-tap.</summary>
        Manage = 16,
        /// <summary>The developer console (DevPanelController). Registered ONLY by
        /// <c>DeNelle.DevTools</c>, which is compiled out of release builds — so in a store APK
        /// nothing registers this id and <see cref="IsRegistered"/> returns false, which is what lets
        /// the Settings entry hide itself rather than offering a dead button.
        ///
        /// Owner ruling 2026-08-08: "remove the dev flag on the left side, and let's hide the dev
        /// panel ... let's stick it under settings." The on-screen DEV chips are gone
        /// (ff.devresourcetool now defaults OFF everywhere); this id is the replacement door, so
        /// access survives without anything sitting in shot. Append-only: values are load-bearing.</summary>
        DevPanel = 17,
        /// <summary>WO-1026 — the DEFENCE REPORT: the re-openable record of attacks on the
        /// player's own town (who came, where they broke through, what broke, what it cost).
        /// Registered scene-independently by DefenseReportPanel (spawned by
        /// DefenseReportPanelBootstrap).
        ///
        /// DELIBERATELY **NOT** an action-bar face: CLAUDE.md §7 caps the calm(town) bar at
        /// SIX visible faces and spends paragraphs on why. The panel ships REGISTERED and
        /// openable via PanelRouter.Open + the DevPanel while the owner picks the town door
        /// (a badge on the Heart interaction and a Manage-screen tab are the two candidates).
        /// Append-only: values are load-bearing.</summary>
        DefenseReport = 18,
        /// <summary>Calendar-month Battle Pass track. Append-only route.</summary>
        BattlePass = 19,
        /// <summary>Non-expiring pool-model Monthly Ledger. Append-only route.</summary>
        MonthlyLedger = 20,
        /// <summary>WO-1073 - the BENEFACTORS OF THE REALM wall: the single GLOBAL honour
        /// roll of $500 Founders, identical in every kingdom, read from the public
        /// GET /api/patronage/benefactors seam.
        ///
        /// Registered scene-independently by BenefactorsWallPanel (spawned by
        /// BenefactorsWallPanelBootstrap). Its ONE door in the world is the Founders
        /// Monument standing near the Heart (DeNelle.Village.FoundersMonument) - owner
        /// ruling 2026-08-27(c): "walking up to the monument and reading the names is the
        /// moment; a menu item is not". Deliberately NOT an action-bar face; CLAUDE.md
        /// section 7 caps the calm(town) bar and spends paragraphs on why.
        /// Append-only: values are load-bearing.</summary>
        Benefactors = 21,
        /// <summary>WO-1286 mobile-first card launcher for realm services and status surfaces.</summary>
        RealmDeck = 22,
        /// <summary>WO-1286 mobile-first card launcher for hero inventory, equipment and skills.</summary>
        HeroDeck = 23,
        /// <summary>WO-1286 mobile-first card launcher for quests, map, raids and seasons.</summary>
        JourneyDeck = 24,
        /// <summary>WO-1399 - the HELP menu (HelpMenu, DeNelle.HUD: Report a Bug / Controls /
        /// Reset Hero and Echoes / Credits, plus dev-only rows). Registered scene-independently by
        /// HelpMenu.Awake (spawned by HelpMenuBootstrap). Its ONE player door is the "Help" row
        /// inside Settings (SettingsController.OnHelpClicked) - the gear dock's "Settings" row
        /// used to open THIS screen instead of Settings, and Help was reachable nowhere else.
        /// Append-only: values are load-bearing.</summary>
        Help = 25,
        /// <summary>WO-2003 / WO-2017 - the HEART OF ELARION surface (HeartPanel, DeNelle.Village):
        /// the realm-progression spine, where the player reads their HEART LEVEL, sees what the
        /// next level opens (derived from building-tiers.json, never typed) and raises it.
        ///
        /// ⛔ THE DEFECT THIS ID EXISTS FOR (owner 2026-09-06: "wire the heart"): the sole writer of
        /// the gate is VillageTierService.TryUpgrade, whose ONLY caller was
        /// BuildingUpgradeVM.Select(VillageTierRowId) - reachable ONLY from the VillageGated action
        /// band in BuildingUpgradePanelMvvm.cs:1322-1338, i.e. ONLY while the player happened to be
        /// looking at a building whose next tier was gated. The control that gates nearly all
        /// content had NO DIRECT ROUTE. Registered scene-independently by HeartPanel (spawned by
        /// HeartPanelBootstrap); its doors are the Manage header HEART face and every village-gated
        /// building/research CTA, which already read "UPGRADE THE HEART".
        /// Append-only: values are load-bearing.</summary>
        Heart = 26,
        /// <summary>WO-1432 - the one-time HONEST FEEDBACK thank-you (HonestFeedbackPanel,
        /// DeNelle.Village.Feedback): a short feedback box that pays 1000 wood / 1000 stone /
        /// 1000 iron as <c>BankGrantKind.PurchasedOrPromised</c> once, on a response in which
        /// our OWN backend says it stored the player's words. The store link rides the same
        /// panel as a second, visually secondary, UNREWARDED button.
        ///
        /// ⛔ NOT a menu entry and NOT an action-bar face. The offer decides its own moment
        /// (HonestFeedbackService.IsEligible: a positive beat landed, session time past the
        /// JSON-authored threshold, onboarding done, no other modal open) and shows itself
        /// exactly once per save. Its doors are HonestFeedbackService.TryOpenOffer (D1) and
        /// HonestFeedbackPanelBootstrap (D2).
        ///
        /// ⛔ NOTHING ON THIS ROUTE MAY CLAIM A REVIEW WAS LEFT OR VERIFIED. No app store
        /// tells a client either fact, on any platform, deliberately - so the grant hangs on
        /// a feedback surface this project owns, never on a store flow returning.
        /// Append-only: values are load-bearing.</summary>
        HonestFeedback = 27,
        /// <summary>WO-1870 - the full in-game CIRCLE screen (CircleScreenPanel, DeNelle.HUD):
        /// create or join a Circle by code, the member roster with the leader/officer verbs,
        /// the Circle leaderboard, the read-only vault and the ballots. It is also where a
        /// player without a Remnant name claims one, which is the first thing such a player
        /// sees on it (owner ruling 2026-09-18).
        ///
        /// Its doors are CircleScreenPanelBootstrap (D2) and the always-visible button inside
        /// Circle Chat (D1). It is deliberately NOT a gear-drawer row: AddDockTab lays the
        /// drawer out as a 2x3 grid and all six cells are occupied, so a seventh would paint
        /// outside the panel and move the cell WO-1465's clearance geometry is written around.
        /// Append-only: values are load-bearing.</summary>
        Circle = 28,
        /// <summary>WO-1874 - the Ceremony of Vigil plate (VigilCeremonyPanel, DeNelle.HUD):
        /// the short roots-to-canopy sequence and the epoch plate after a Circle ballot
        /// settles. Auto-plays once per install per epoch on the next hub entry; replay
        /// is the Ballots tab "Watch the last vigil" face.
        ///
        /// Doors: VigilCeremonyPanelBootstrap (D2) and CircleScreenPanel naming
        /// VigilCeremonyPanel / PanelId.CeremonyOfVigil (D1). Never a raid blocker.
        /// Append-only: values are load-bearing.</summary>
        CeremonyOfVigil = 29,
    }

    /// <summary>
    /// Static, reflection-free panel-open registry. Panels call
    /// <see cref="Register(PanelId, Action)"/> when they come alive; interactables
    /// call <see cref="Open(PanelId)"/> to open the one correct panel. Routing the
    /// open through the panel's own method means the modal arbiter (PanelManager)
    /// still governs visibility — opening one panel closes any other.
    /// </summary>
    public static class PanelRouter
    {
        private static readonly Dictionary<PanelId, Action> _openers =
            new Dictionary<PanelId, Action>();

        // DEF-186: optional context-aware openers. A panel that can FOCUS on a
        // specific subject (e.g. the BuildingUpgrade panel scrolling to / highlighting
        // the exact building the player interacted with) registers an Action<string>
        // here in ADDITION to its plain Action above. Callers that know the subject id
        // (BuildingInteractable knows which building was tapped) route through
        // Open(id, context); callers that don't keep using Open(id). Kept as a SEPARATE
        // map so the reflection-free plain-Action contract above is untouched.
        private static readonly Dictionary<PanelId, Action<string>> _contextOpeners =
            new Dictionary<PanelId, Action<string>>();

        // Vendor-shop simplification (owner F8 2026-07-10): some panels open in a MODE as
        // well as on a subject — the Party Shop opens LOCKED to buy OR sell (one list, one
        // action, no competing top tabs). A panel registers a two-arg opener here (subject +
        // mode) in ADDITION to the plain + single-string openers above; callers that carry a
        // mode route through Open(id, arg0, arg1), everyone else keeps the existing paths.
        // Kept as a SEPARATE map so the reflection-free contracts above are untouched.
        private static readonly Dictionary<PanelId, Action<string, string>> _contextOpeners2 =
            new Dictionary<PanelId, Action<string, string>>();

        /// <summary>
        /// Register (or replace) the open action for <paramref name="id"/>. Panels
        /// call this in Awake/OnEnable. Null actions are ignored. Idempotent: a
        /// re-spawned panel simply overwrites the previous opener for its id.
        /// </summary>
        public static void Register(PanelId id, Action open)
        {
            if (open == null) return;
            _openers[id] = open;
        }

        /// <summary>
        /// Remove the open action for <paramref name="id"/> if it is exactly
        /// <paramref name="open"/> (so a destroyed panel doesn't clobber a freshly
        /// spawned replacement that already re-registered). Null-safe.
        /// </summary>
        public static void Unregister(PanelId id, Action open)
        {
            if (open == null) return;
            if (_openers.TryGetValue(id, out var current) && current == open)
                _openers.Remove(id);
        }

        /// <summary>
        /// Register (or replace) a CONTEXT-aware open action for <paramref name="id"/>
        /// (DEF-186). The string arg is a subject id — e.g. the building id the player
        /// interacted with — letting the panel focus on / highlight that subject. A
        /// panel typically registers BOTH this and the plain <see cref="Register(PanelId, Action)"/>
        /// (the plain one is the "open with no particular focus" fallback). Idempotent.
        /// </summary>
        public static void Register(PanelId id, Action<string> openWithContext)
        {
            if (openWithContext == null) return;
            _contextOpeners[id] = openWithContext;
        }

        /// <summary>
        /// Remove the context-aware open action for <paramref name="id"/> if it is
        /// exactly <paramref name="openWithContext"/> (mirrors the plain Unregister).
        /// </summary>
        public static void Unregister(PanelId id, Action<string> openWithContext)
        {
            if (openWithContext == null) return;
            if (_contextOpeners.TryGetValue(id, out var current) && current == openWithContext)
                _contextOpeners.Remove(id);
        }

        /// <summary>
        /// Register (or replace) a SUBJECT+MODE open action for <paramref name="id"/>
        /// (owner F8 2026-07-10). The first arg is the subject id (e.g. the vendor); the
        /// second is a mode (e.g. "buy"/"sell") that opens the panel locked to one flow.
        /// A panel registering this typically ALSO registers the plain + single-string
        /// openers (they stay the "no mode" fallbacks). Idempotent.
        /// </summary>
        public static void Register(PanelId id, Action<string, string> openWithContextMode)
        {
            if (openWithContextMode == null) return;
            _contextOpeners2[id] = openWithContextMode;
        }

        /// <summary>
        /// Remove the subject+mode open action for <paramref name="id"/> if it is exactly
        /// <paramref name="openWithContextMode"/> (mirrors the other Unregisters).
        /// </summary>
        public static void Unregister(PanelId id, Action<string, string> openWithContextMode)
        {
            if (openWithContextMode == null) return;
            if (_contextOpeners2.TryGetValue(id, out var current) && current == openWithContextMode)
                _contextOpeners2.Remove(id);
        }

        /// <summary>WO-T1 (Tutorial V2) — raised after a panel opened AND verified visible
        /// (both the plain and context Open paths route through <see cref="VerifyOpenedVisible"/>).
        /// TutorialSignals maps this to "panel.opened:&lt;PanelId&gt;". Additive; never throws
        /// back into the opener (subscriber exceptions are guarded).</summary>
        public static event Action<PanelId> PanelOpened;

        // Raise PanelOpened without letting a subscriber fault the open path.
        private static void RaisePanelOpened(PanelId id)
        {
            try { PanelOpened?.Invoke(id); }
            catch (Exception ex)
            {
                FlowTrace.Fail("UI", "PanelRouter.PanelOpened subscriber threw for '" + id + "': " + ex.Message);
            }
        }

        /// <summary>True when a panel is registered for <paramref name="id"/>.</summary>
        public static bool IsRegistered(PanelId id) =>
            _openers.ContainsKey(id) || _contextOpeners.ContainsKey(id);

        /// <summary>
        /// Open the panel registered for <paramref name="id"/>. Returns false (and
        /// does nothing) when no panel is registered — the caller can then show a
        /// "coming soon" message instead of silently doing nothing. Exceptions from
        /// the panel's open action are swallowed (logged) so a bad panel can't break
        /// the interaction.
        /// </summary>
        public static bool Open(PanelId id)
        {
            if (!_openers.TryGetValue(id, out var open) || open == null)
                return false;

            // GUARD the open (WO-465): a throwing opener never returns a false "true". FlowTrace.Fail
            // on a throw so the route self-reports instead of swallowing into a Debug.LogWarning.
            bool ran = Guard.Try("UI", "PanelRouter.Open '" + id + "'", () => open.Invoke());
            if (!ran)
            {
                FlowTrace.Fail("UI", "PanelRouter: opening '" + id + "' threw — panel did NOT open.");
                return false;
            }

            // VISIBILITY VERIFY: the opener ran without throwing, but "didn't throw" != "rendered".
            // The registered open routes through PanelManager (the modal arbiter), so a successful
            // open MUST leave a panel recorded open. If nothing is open afterwards the panel failed
            // to become visible (the WO-465 invisible-scrim class) — Fail-loud and return false so the
            // caller can show a fallback instead of believing a blank panel opened.
            return VerifyOpenedVisible(id);
        }

        // Shared post-open visibility verify (WO-465). Returns true when a panel is actually recorded
        // open by the modal arbiter; FlowTrace.Fail + false when the open silently produced nothing.
        private static bool VerifyOpenedVisible(PanelId id)
        {
            bool anyOpen = PanelManager.AnyOpen;
            if (!anyOpen)
            {
                // WO-437 battle-lock refusal is a CONTRACT, not the WO-465 invisible-scrim
                // class: PanelManager.NotifyOpened rejects a non-battle-allowed panel while
                // BattleLock.IsInBattle(), so "nothing open" here is the intended refusal
                // ("no shopping while being killed", WO-599). Fail loud ONLY when nothing
                // opened AND no battle-lock explains it (fleet 9000/9200 false-flagged the
                // refusal as scrim on RumorBoard/BuildingUpgrade, 2026-07-06).
                if (DeNelle.Core.Combat.BattleLock.IsInBattle())
                {
                    FlowTrace.Warn("UI",
                        "PanelRouter: '" + id + "' open REFUSED by battle-lock (WO-437 contract, in-battle) — not a scrim failure.");
                    return false;
                }
                FlowTrace.Fail("UI",
                    "PanelRouter: '" + id + "' open action ran but NO panel is recorded open afterwards " +
                    "— panel failed to become visible (WO-465 invisible-scrim class).");
                return false;
            }
            FlowTrace.Step("UI",
                "PanelRouter: '" + id + "' opened and verified visible (open panel='" + PanelManager.OpenPanelName + "').");
            RaisePanelOpened(id);   // WO-T1 — tutorial "panel.opened:<id>" signal source
            return true;
        }

        /// <summary>
        /// Open the panel registered for <paramref name="id"/>, focusing it on
        /// <paramref name="context"/> (a subject id — DEF-186). Prefers the context-aware
        /// opener if the panel registered one; otherwise falls back to the plain
        /// <see cref="Open(PanelId)"/> (so a panel that ignores context still opens).
        /// Returns false only when NEITHER opener is registered. Exceptions are
        /// swallowed (logged) exactly like the plain Open.
        /// </summary>
        public static bool Open(PanelId id, string context)
        {
            if (_contextOpeners.TryGetValue(id, out var openCtx) && openCtx != null)
            {
                // GUARD the context open (WO-465) — a throwing opener self-reports via FlowTrace.Fail
                // and returns false rather than swallowing into a Debug.LogWarning + claiming nothing.
                bool ran = Guard.Try("UI", "PanelRouter.Open(ctx) '" + id + "'", () => openCtx.Invoke(context));
                if (!ran)
                {
                    FlowTrace.Fail("UI", "PanelRouter: context-opening '" + id + "' threw — panel did NOT open.");
                    return false;
                }
                // VISIBILITY VERIFY: same as the plain Open — "didn't throw" != "rendered".
                return VerifyOpenedVisible(id);
            }
            // No context-aware opener — fall back to the plain open (ignores context).
            return Open(id);
        }

        /// <summary>
        /// Open the panel registered for <paramref name="id"/> with BOTH a subject
        /// <paramref name="context"/> and a <paramref name="mode"/> (owner F8 2026-07-10 —
        /// e.g. the Party Shop opened locked to "buy"/"sell"). Prefers the subject+mode opener;
        /// if none is registered, falls back to the single-string <see cref="Open(PanelId, string)"/>
        /// (mode dropped), then to the plain <see cref="Open(PanelId)"/>. Returns false only when
        /// NO opener at all is registered. Exceptions are swallowed (logged) like the other Opens.
        /// </summary>
        public static bool Open(PanelId id, string context, string mode)
        {
            if (_contextOpeners2.TryGetValue(id, out var openCtx2) && openCtx2 != null)
            {
                bool ran = Guard.Try("UI", "PanelRouter.Open(ctx,mode) '" + id + "'", () => openCtx2.Invoke(context, mode));
                if (!ran)
                {
                    FlowTrace.Fail("UI", "PanelRouter: context+mode-opening '" + id + "' threw — panel did NOT open.");
                    return false;
                }
                return VerifyOpenedVisible(id);
            }
            // No subject+mode opener — fall back to the single-string open (mode dropped).
            return Open(id, context);
        }
    }
}
