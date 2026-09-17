// =============================================================================
// BattlePlansRevealVM -- the pure ViewModel behind BattlePlansReveal (WO-1804).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHY THIS FILE EXISTS, stated plainly: the first cut of the reveal read
// GameStateService straight out of the View, and `UiMvvmConformanceRegression` FAILED
// it as a NEW state-reading View (BattlePlansReveal.cs -> 112,121,125,186). It was
// right to. THE LAW (docs/ARCHITECTURE_PRINCIPLES.md §1/§2/§2c; UI_MVVM_BINDING_MAP.md;
// the gold standard BuildingUpgradeVM + BuildingUpgradePanelMvvm): all state and logic
// live in an IPanelViewModel, and the View is a dumb skin that binds it, re-renders on
// Changed, and routes taps as COMMANDS.
//
// ⛔ AND IT IS NOT FIXED BY GAMING THE LINT. That oracle's routing check is FILE-LEVEL
// and says so in its own header: a View that merely NAMES IPanelViewModel drops out of
// the offender set even if it still calls a banned symbol. Adding the token and leaving
// the reads in place would have turned the gate green while the defect stayed -- the
// exact "the word and the door disagree" shape this ticket already fixed once for the
// CTA face. So every state read MOVED. `GameStateService` does not appear in
// BattlePlansReveal.cs at all any more; it passes on the BANNED-SYMBOL half of the rule,
// not just the routing half.
//
// ⛔ NOR BY THE ALLOW-LIST. SpirePlansCelebration.cs is allow-listed as a "one-shot
// cinematic flow controller ... reads only its own seen-flag", and this screen is built
// in that same cold-open idiom -- so copying the exemption was available and would have
// been DISHONEST: this reveal also reads the LIVE army roster to compose its middle beat,
// which is exactly the live game data that exemption promises is absent. An allow-list
// entry whose stated reason is false is worse than a violation, because the next reader
// believes it.
//
// PURE C#: no UnityEngine UI type crosses this seam (no GameObject, Image, Sprite or
// RectTransform), so it is unit-testable without a scene and it can never be mistaken for
// a View by the conformance oracle. The Echo PORTRAIT is exposed as a NAME (string); the
// View is the only side that turns it into a Sprite.
//
// WHAT IT OWNS: the once-ever seen flag (read + mark), the live army count, whether a
// Barracks stands, where the player is standing, the composed beats, the CTA decision AND
// the CTA's command. WHAT IT DOES NOT OWN: pixels, fades, the arbiter handshake.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village
{
    /// <summary>
    /// Pure ViewModel for the WO-1804 plans reveal. Composed once per showing by
    /// <see cref="CreateDefault"/>; the View binds it and never reads game state.
    /// </summary>
    public sealed class BattlePlansRevealVM : IPanelViewModel, IDisposable
    {
        // -- IPanelViewModel --------------------------------------------------
        /// <summary>The plans' own name ("Enemy Battle Plans" / "Bastion Plans").</summary>
        public string Title { get; private set; }

        /// <summary>Raised when bound data changes. Composed ONCE per showing and never
        /// mutated afterwards, so this never fires today -- the contract is honoured (the
        /// View subscribes and would re-render) without inventing a refresh loop that this
        /// one-shot moment does not have.</summary>
        public event Action Changed;

        /// <summary>The universal dismiss command. Presentation-only: the plans are already
        /// HELD and the unlock already stands before this VM exists, so closing costs the
        /// player nothing. The View owns the actual teardown; this is the seam.</summary>
        public void Close() => CloseRequested?.Invoke();

        /// <summary>Raised by <see cref="Close"/> so the View can tear its overlay down
        /// without this VM ever touching a GameObject.</summary>
        public event Action CloseRequested;

        public void Dispose()
        {
            Changed = null;
            CloseRequested = null;
        }

        // -- bound data -------------------------------------------------------
        /// <summary>Which plans kind this showing is for.</summary>
        public BattlePlansKind Kind { get; private set; }

        /// <summary>The target camp's player-facing name, read from the scene-config row.</summary>
        public string CampName { get; private set; }

        /// <summary>The beats, one line at a time, already composed.</summary>
        public IReadOnlyList<BattlePlans.Beat> Beats { get; private set; }

        /// <summary>The speaking Echo's bare name, or empty for an unattributed beat.</summary>
        public string SpeakerName { get; private set; }

        /// <summary>The speaking Echo's portrait NAME. A string, never a Sprite: this VM must
        /// not name a UnityEngine type (IPanelViewModel's own contract). The View loads it.</summary>
        public string PortraitName { get; private set; }

        /// <summary>The ONE call-to-action's face label.</summary>
        public string CtaLabel { get; private set; }

        /// <summary>Which action the CTA performs (for the View's trace only -- the View never
        /// branches on it; it calls <see cref="InvokeCta"/>).</summary>
        public BattlePlansCta Cta { get; private set; }

        /// <summary>Deployable troop BODIES at compose time, or -1 for UNKNOWN. Exposed for the
        /// trace and the regression, not for the View to re-word.</summary>
        public int DeployableBodies { get; private set; }

        /// <summary>Whether a Barracks stood at compose time (trace + regression).</summary>
        public bool BarracksBuilt { get; private set; }

        /// <summary>Whether the reveal is playing in a hub (trace + regression).</summary>
        public bool InHub { get; private set; }

        /// <summary>The spoils line as projected, or null when the projection resolved nothing.</summary>
        public string SpoilsLine { get; private set; }

        // =====================================================================
        //  COMPOSE
        // =====================================================================

        /// <summary>
        /// Read every input ONCE and compose the whole screen. The house factory name, so the
        /// conformance oracle's <c>.CreateDefault(</c> routing token is satisfied by a real
        /// factory rather than by a decorative mention.
        /// </summary>
        public static BattlePlansRevealVM CreateDefault(BattlePlansKind kind, string activeScene)
        {
            var vm = new BattlePlansRevealVM
            {
                Kind = kind,
                Title = BattlePlans.TitleFor(kind),
                InHub = HubScenes.IsHub(activeScene),
            };

            vm.CampName = BattlePlans.CampDisplayName(kind);
            vm.SpoilsLine = BattlePlans.SpoilsSentence(kind);
            vm.DeployableBodies = ReadDeployableBodies();
            vm.BarracksBuilt = StructureSingleton.IsBuilt(BattlePlans.BarracksItemId);

            // WHERE the player stands is a CTA input: build mode is a town verb, so a
            // no-Barracks reveal in the boss room offers the way home instead of a dead button.
            vm.Cta = BattlePlans.ResolveCta(vm.BarracksBuilt, vm.InHub);
            vm.CtaLabel = BattlePlans.CtaLabel(vm.Cta, vm.CampName);

            string army = BattlePlans.ArmySentence(vm.DeployableBodies);
            vm.Beats = BattlePlans.BuildBeats(kind, vm.CampName, vm.SpoilsLine, army);

            vm.ResolveSpeaker();

            FlowTrace.Step(BattlePlans.Sys,
                "reveal VM composed kind=" + kind + " camp='" + vm.CampName + "' spoils=" +
                (string.IsNullOrEmpty(vm.SpoilsLine) ? "<none>" : "'" + vm.SpoilsLine + "'") +
                " deployableBodies=" + vm.DeployableBodies + " barracksBuilt=" + vm.BarracksBuilt +
                " inHub=" + vm.InHub + " cta=" + vm.Cta + " beats=" + vm.Beats.Count +
                " speaker='" + (string.IsNullOrEmpty(vm.SpeakerName) ? "(unattributed)" : vm.SpeakerName) +
                "' -- WO-1804 (all state read HERE; the View reads none)");
            return vm;
        }

        /// <summary>
        /// Deployable troop BODIES, live off ArmyStorage. Same shape as
        /// <c>RaidSelectionScreen.CountDeployableTroops</c> so the reveal and the raid grid can
        /// never disagree about the roster. Returns -1 (UNKNOWN) with no state, so the army
        /// sentence is dropped rather than guessed.
        /// </summary>
        private static int ReadDeployableBodies()
        {
            var st = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (st == null || st.Army == null) return -1;
            int n = 0;
            foreach (var t in st.Army.GetDeployable())
                if (t != null && !string.IsNullOrEmpty(t.TroopDefId)) n++;
            return n;
        }

        /// <summary>
        /// The speaker, read from the roster catalog -- NEVER a name literal. Echo #1 is the
        /// founding Echo; the roster's DisplayName is authored as "&lt;name&gt;, the
        /// &lt;element&gt; Echo", so the bare name is everything before the first comma. An empty
        /// roster leaves the beat unattributed (it still reads) with a Warn.
        /// </summary>
        private void ResolveSpeaker()
        {
            var entry = EchoRosterCatalog.ByCount(1);
            if (entry == null || string.IsNullOrEmpty(entry.DisplayName))
            {
                FlowTrace.Warn(BattlePlans.Sys,
                    "reveal VM: EchoRosterCatalog.ByCount(1) gave no display name -- the " +
                    "call-to-arms beat plays unattributed (never a hardcoded name).");
                SpeakerName = string.Empty;
                PortraitName = string.Empty;
                return;
            }
            int comma = entry.DisplayName.IndexOf(',');
            SpeakerName = comma > 0 ? entry.DisplayName.Substring(0, comma).Trim()
                                    : entry.DisplayName.Trim();
            PortraitName = entry.PortraitName ?? string.Empty;
        }

        // =====================================================================
        //  ONCE-EVER  (the persisted flag lives here, not in the View)
        // =====================================================================

        /// <summary>True once this kind's persisted once-ever flag is set (the screen played).
        /// Static because the SCHEDULER asks before any VM or View exists.</summary>
        public static bool HasBeenSeen(BattlePlansKind kind)
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null || state.SeenTutorials == null) return false;
            return state.SeenTutorials.TryGetValue(BattlePlans.RevealSeenKeyFor(kind), out bool seen) && seen;
        }

        /// <summary>
        /// Persist "this kind's reveal has played". Called by the View at the moment the screen is
        /// ON SCREEN, so an explicit Skip can never make it replay later.
        /// <para>The key rides the EXISTING GameState.SeenTutorials map via MarkTutorialSeen
        /// (idempotent + Save()), which is why there is NO SaveSchema field and NO version bump --
        /// the same reason the castle plans did not bump either.</para>
        /// </summary>
        public void MarkSeen()
        {
            string key = BattlePlans.RevealSeenKeyFor(Kind);
            var svc = GameStateService.Instance;
            if (svc == null)
            {
                FlowTrace.Warn(BattlePlans.Sys,
                    "reveal seen flag DROPPED kind=" + Kind + ": no GameStateService -- the moment " +
                    "played but could replay. (Presentation only; nothing gameplay-bearing.)");
                return;
            }
            svc.MarkTutorialSeen(key);
            FlowTrace.Step(BattlePlans.Sys,
                "reveal seen flag persisted: '" + key + "' (once ever, SeenTutorials store)");
        }

        // =====================================================================
        //  THE CTA COMMAND -- the View raises it; this performs it.
        // =====================================================================

        /// <summary>
        /// Perform the one call-to-action. The View calls this AFTER tearing its overlay down
        /// (ManageScreenPanel.OpenPlacementFor Close()s first for the same reason: a modal still
        /// standing over build mode eats the placement drag).
        ///
        /// <para>⛔ AND IT NEVER LOADS A SCENE. In a dungeon the raid grid is unreachable and a
        /// raid load would bypass DungeonExitInteractable.ExecuteLeave ->
        /// DungeonController.ExitToVillage, which BANKS the run's crafting scatter -- so the
        /// command sets BattlePlans' two latches and the dungeon's own exit confirm carries the
        /// player home. Grep this file for GoRaid: there is none, and case 8's source lint fails
        /// the gate if one appears.</para>
        /// </summary>
        public void InvokeCta()
        {
            if (Cta == BattlePlansCta.ReturnToCastle)
            {
                BattlePlans.RequestDungeonExit("plans reveal RETURN TO THE CASTLE kind=" + Kind);
                FlowTrace.Step(BattlePlans.Sys,
                    "reveal CTA HANDED OFF kind=" + Kind + ": no Barracks and not in a hub, so " +
                    "latch 1 (leave) only. The dungeon's own exit raises its Continue/Cancel " +
                    "confirm and still banks the run; the WO-1802 helper chain takes over in town. " +
                    "No raid grid was requested, because there is nothing to raid from yet. -- WO-1804");
                return;
            }

            if (Cta == BattlePlansCta.BuildBarracks)
            {
                var controller = BuildModeController.Instance ?? BuildModeController.EnsureExists();
                bool ok = controller != null &&
                          controller.EnterBuildModeForStructure(BattlePlans.BarracksItemId);
                if (ok)
                    FlowTrace.Step(BattlePlans.Sys,
                        "reveal CTA HANDED OFF kind=" + Kind + ": build mode entered with '" +
                        BattlePlans.BarracksItemId + "' selected for placement -- WO-1804");
                else
                    FlowTrace.Fail(BattlePlans.Sys,
                        "reveal CTA FAILED kind=" + Kind + ": EnterBuildModeForStructure('" +
                        BattlePlans.BarracksItemId + "') refused (controller=" +
                        (controller != null ? "present" : "null") + "). The plans are still HELD; " +
                        "the player can reach the Barracks from the bar. Never a silent dead button.");
                return;
            }

            // OpenRaidGrid -- and WHERE the player stands decides the route.
            string campId = BattlePlans.CampIdFor(Kind);
            if (InHub)
            {
                Hero.RaidSelectionScreen.Open();
                FlowTrace.Step(BattlePlans.Sys,
                    "reveal CTA HANDED OFF kind=" + Kind + " in a hub: raid grid opened directly " +
                    "(camp '" + campId + "'). NOTE: RaidSelectionScreen.Open() takes no argument -- " +
                    "there is no preselect API and none was added, so the grid opens and the camp is " +
                    "its own row. -- WO-1804");
                return;
            }

            BattlePlans.RequestRaidGridOnHubArrival(campId);
            BattlePlans.RequestDungeonExit("plans reveal CTA kind=" + Kind + " outside a hub");
            FlowTrace.Step(BattlePlans.Sys,
                "reveal CTA HANDED OFF kind=" + Kind + " OUTSIDE a hub: routed through the " +
                "dungeon's OWN banked exit. Latch 1 = leave (claimed by DungeonExitInteractable, " +
                "which still raises its Continue/Cancel confirm and still runs ExecuteLeave -> " +
                "ExitToVillage, so the run banks); latch 2 = open the grid for '" + campId +
                "' on the first hub frame. -- WO-1804");
        }

        /// <summary>Fire <see cref="Changed"/>. Unused today (the VM is composed once and never
        /// mutated) and kept so a future live-refreshing beat has the seam the contract promises,
        /// rather than the next author re-inventing it.</summary>
        private void Raise() => Changed?.Invoke();
    }
}
