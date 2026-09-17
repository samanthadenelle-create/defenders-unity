// =============================================================================
// HudContextEvaluator — WO-541 Stage 2: the ONE HUD-context authority.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hud
//
// Consolidates the SAME inputs the two existing evaluators read — derived ONCE —
// and writes the Core HudContextModel:
//   • Combat  : a village wave is ACTIVE (WaveManager.Phase), OR a staged / in-place
//               fight is live (BattleLock.IsInBattle — ATB, Arena, BattleArena,
//               HeroCombatEngagement), OR an enemy is pursuing the hero, OR THE SCENE
//               ITSELF DECLARES COMBAT (HubScenes.SceneDeclaresCombat — WO-1436).
//
//               ⚠ THE OLD HEADER SENTENCE HERE WAS *"Scene ground alone (raid /
//               enemy-owned) does NOT flip Battle"*, AND IT SHIPPED THE WO-1436 P0.
//               It is REFINED, not deleted, because it is still right about the case
//               it was written for. The owner's 2026-07-05 "peaceful default" ruling
//               is about open enemy-owned GROUND — Village2 carries ownership:"Enemy",
//               and wandering onto hostile ground must stay calm until something
//               actually threatens the hero. That half stands: enemy-ownership STILL
//               does not flip Battle, and the PostureEvaluator still opens
//               hostile(prebattle) on pursuit/target there.
//
//               What the sentence got wrong is that it swept a RaidBase_* scene into
//               the same bucket. A raid is not ground you wander onto: it is reachable
//               only through BEGIN ASSAULT, with troops committed and a scored 180 s
//               clock, and [Flow:Raid] RAID START fires ~1 s after the load. Owner
//               felt-test 2026-09-06 (build 2026.09.06.358161): "in the raid, i had no
//               way to fight. No combast skills" — the context resolved to Overworld
//               for the whole assault, the posture to calm(explore), and the action bar
//               rendered the PEACEFUL dock. Measured off that same capture, the posture
//               flapped calm(explore) <-> hostile(*) SEVEN times in 49 s, tracking
//               transient pursuit pulses instead of the committed fight.
//
//               The narrow predicate (raids ONLY — not dungeons, not enemy ground)
//               lives in DeNelle.Core.HubScenes with the rest of the scene-family
//               naming, so the HUD, the Village and the seam oracle all read ONE
//               authority instead of three private copies (the WO-411/920 lesson).
//   • Town    : a non-combat HUB scene with the hero inside the town ring.
//   • Overworld : non-combat, outside the town ring (the merged overworld) / a non-hub scene.
//   • Modal   : a registered modal panel is open (PanelManager.AnyOpen) — overlays.
//
// PRECEDENCE (frozen rule, WO-541): Modal > Battle > Town > Overworld.
//
// NO VILLAGE<->HUD EDGE: this lives in DeNelle.Village and does NOT reference
// BattleHudVisibilityManager (DeNelle.HUD) — Village cannot reference HUD. It
// derives the identical combat signals from the UNDERLYING systems instead:
// WaveManager (own assembly, direct — no reflection), BattleLock / HubScenes /
// PanelManager (all DeNelle.Core). Town-vs-Overworld replicates
// VillageHudController.InVillage's radial model (hub scene + hero within the town
// ring) directly from the Village HeroLocomotion + the Heart-at-origin convention,
// rather than calling the HUD. So no new asmdef edge and no reflection is added.
//
// DARK: the existing BattleHudVisibilityManager / ApplyContext logic is UNTOUCHED
// (Stage 4 migrates the views). This only writes the new Core model — additive.
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.HudModel;
using DeNelle.Core.UI;
using DeNelle.Village;

namespace DeNelle.Village.Hud
{
    /// <summary>The single writer of <see cref="HudContextModel"/> (WO-541 Stage 2).</summary>
    internal sealed class HudContextEvaluator : HudProducer
    {
        // Mirrors VillageHudController's radial model (scene + town ring). The Heart of
        // Elarion sits at the world origin (canon §7); the town footprint reaches ~60u.
        private const string VillageSceneName = "Village2";
        private const float TownRadius = 60f;
        private const float TownRadiusHyst = 8f;

        private HeroLocomotion _hero;

        // Last pushed snapshot (change-gate so the model's [Flow:HUD] transition trace
        // only fires when an input actually changes).
        private HudContext _ctx = (HudContext)(-1);
        private bool _inVillage, _combat, _modal, _buildMode;
        private bool _pushedOnce;

        public HudContextEvaluator(IHudModel model, Transform _host) : base(model, 0.20f) { }

        protected override void Poll()
        {
            // WO-1483 frame budget. FIRST line so every early-return path is still timed.
            // Accumulating 4-arg overload — no per-tick log; PerfReporter rolls it up 1/s.
            // Cadence is 0.20s (the HudProducer interval), so ~5 samples per roll-up.
            using var _perf = FlowTrace.Measure("Perf", "HudContextEvaluator.Poll", 4f, 1f);

            string scene = SceneManager.GetActiveScene().name;

            // WO-1436: the scene's OWN declaration. Constant for the whole time the player
            // stands in an assault, which is what stops the posture flapping with pursuit
            // pulses. Evaluated first so the trace below can name it as its own input.
            bool sceneCombat = HubScenes.SceneDeclaresCombat(scene);

            bool combat = sceneCombat
                          || IsWaveActive()
                          || BattleLock.IsInBattle()
                          // owner F8 2026-07-10 "actively chased should be battle HUD": an overworld rep
                          // pursuing/striking the hero flips to Battle too (refines the 2026-07-05 ruling
                          // that scene-ground alone stays prebattle). PursuitActive self-decays over
                          // PursuitTtl (~1.5s) = built-in hysteresis; distinct from staged BattleLock.
                          || DeNelle.Core.HudModel.PostureSignals.PursuitActive;

            bool inVillage = IsInTownRing(scene);
            bool modal = PanelManager.AnyOpen;
            // P4 (HUD_OBSIDIAN §3.3): the 4th space type. Read via the existing
            // BuildModeController seam (same assembly, read-only — Enter/Exit already
            // maintain IsActive + broadcast BuildModeChanged; no new seam needed).
            bool buildMode = IsBuildModeActive();

            // Precedence: Modal overlays everything; else BuildMode (an edit session owns
            // the screen and freezes waves — BuildModeController.Enter/FreezeWaves — so it
            // outranks a residual combat signal); else Battle; else Town; else Overworld.
            //
            // WO-1436: the chain itself now lives in DeNelle.Core.HudModel.HudContextResolver
            // so the seam oracle can assert it WITHOUT loading a scene or reaching into this
            // internal class. Behaviour is byte-identical — the ternary chain was hoisted, not
            // rewritten. Do NOT re-inline it here; a second copy is exactly how this rule would
            // drift away from the thing that tests it.
            HudContext ctx = HudContextResolver.Resolve(modal, buildMode, combat, inVillage);

            // Observability (mirrors BattleHudVisibilityManager.EvaluateMode's input trace).
            // sceneCombat is printed as its own input: WO-1436's whole diagnosis came from
            // reading this line and finding EVERY combat input False inside a live raid, so a
            // new input that did not appear here would be invisible to the next such read.
            FlowTrace.Throttle("HUD", "ctx-eval", 1f,
                $"context inputs: sceneCombat={sceneCombat} wave={IsWaveActive()} " +
                $"battleLock={BattleLock.IsInBattle()} " +
                $"pursuit={DeNelle.Core.HudModel.PostureSignals.PursuitActive} " +
                $"inVillage={inVillage} modal={modal} buildMode={buildMode} scene='{scene}' -> {ctx}");

            if (_pushedOnce && _combat && !combat)
                HudPostureReset.OnCombatEnded();

            if (_pushedOnce && ctx == _ctx && inVillage == _inVillage && combat == _combat &&
                modal == _modal && buildMode == _buildMode)
                return;

            _ctx = ctx; _inVillage = inVillage; _combat = combat; _modal = modal;
            _buildMode = buildMode; _pushedOnce = true;
            // HudContextModel.Set fires Changed only on a real Context change but ALWAYS
            // traces the state; a real change also emits the fleet-assertable
            // "[Flow:HudModel] context A->B" transition line (P4 contract).
            Model.Context.Set(ctx, inVillage, combat, modal, buildMode);
        }

        /// <summary>P4: true while a Build Mode edit session is live (BuildModeController.IsActive).</summary>
        private static bool IsBuildModeActive()
        {
            var bmc = BuildModeController.Instance;
            return bmc != null && bmc.IsActive;
        }

        // Countdown seconds at which a wave reads "imminent" (battle-worthy). Mirrors the
        // single-sourced WaveProducer.ImminentThreshold (HudModelProducers.cs) — that const is
        // `private` inside WaveProducer so it can't be referenced here without touching that file;
        // this evaluator is edit-scoped to itself, so the value is duplicated with THIS pointer.
        // Keep the two in lockstep. `internal` (not private) since 2026-08-10: BattleMusicManager
        // reads the SAME threshold so battle MUSIC obeys the same owner ruling as the HUD posture
        // (F8 seq 2251 — battle music through a whole 290s town countdown). Village-side authority
        // is THIS const; the Core-side WaveProducer copy stays private across the asmdef edge.
        internal const float ImminentThreshold = 5f;

        /// <summary>Owner ruling 2026-07-08 (refines the 2026-07-06 "countdown counts as battle"): a
        /// wave COUNTDOWN reads as Battle ONLY when the wave is IMMINENT (final ~<see cref="ImminentThreshold"/>s),
        /// NOT for the whole long empty between-wave gap. So an ACTIVE wave is always battle; a countdown
        /// with more than the threshold remaining releases the HUD to its non-battle context (Town/Overworld),
        /// and only the last few seconds re-arm the "wave incoming" tension. This keeps the imminent window
        /// single-sourced to the same threshold WaveProducer already uses.</summary>
        private static bool IsWaveActive()
        {
            var wm = WaveManager.Instance;
            if (wm == null) return false;

            if (wm.Phase == DeNelle.Village.WavePhase.Active)
                return true;

            if (wm.Phase == DeNelle.Village.WavePhase.Countdown)
            {
                // ⛔ WO-1736 — A PARKED COUNTDOWN IS NOT AN IMMINENT ONE. THIS IS THE
                //    PROVEN HOLDER, named from a captured device log (see below).
                //
                //    ENDLESS MODE parks the loop in phase Countdown with _countdownRemaining
                //    HELD AT 0 and _awaitingPlayerStart set, waiting for the player's DEFEND
                //    press (WaveManager.TryArmEndlessWave, and the comment at WaveManager.cs:518-525
                //    that states the design). The bare test below is `0.0f <= 5f` => TRUE, so a
                //    wave that is not counting down at all read as PERMANENTLY IMMINENT, pinned
                //    `combat` true, and left HudContext at Battle - the combatDock instead of the
                //    peacefulDock - until the player started the next wave.
                //
                //    ⭐ CAPTURED, NOT INFERRED (owner's Seeker SM02G4061955851, 2026-09-17,
                //    endless wave 21, Main_Castle_Overworld). Wave 20 cleared at 13:05:21
                //    ("EnterCountdown(waveId=21) phaseBefore=Active" + "awaiting player start").
                //    From 13:05:21 to 13:39:51 - 34m30s, 186 consecutive throttled samples -
                //    the trace below read, every single time:
                //        [Flow:HUD] countdown IMMINENT (0.0s <= 5s) -> counts as Battle
                //    alongside
                //        [Flow:HUD] context inputs: sceneCombat=False wave=True
                //        battleLock=False pursuit=False ... -> Battle
                //    battleLock AND pursuit AND sceneCombat were ALL FALSE for the whole window,
                //    so this input was the SOLE holder. The field was empty and the drain had
                //    reported "COMPLETE - all 13 held enemy(s) released" 20s before the clear.
                //
                //    ⚠ THIS CORRECTS WO-1736 sec.2.3 AND sec.2.4, WHICH RANKED THIS OUT. That RCA
                //    read "IsWaveActive() returns true when wm.Phase == WavePhase.Active" and
                //    concluded that external player Sminer's visible START WAVE button (pushed
                //    only for Countdown / Idle / Complete) excluded both wave-sourced inputs. The
                //    first branch is only HALF this method: the Countdown branch below returns
                //    true as well, so phase==Countdown produces the START WAVE BUTTON *and* the
                //    combat dock AT THE SAME TIME. That is Sminer's screenshot exactly, and the
                //    "sometimes a re-login fixes it" asymmetry too - a fresh manager in Idle
                //    reads false, one restored into an awaiting-start Countdown re-latches.
                //
                //    ⛔ DO NOT "FIX" THIS BY RAISING OR REMOVING THE THRESHOLD. The imminent
                //    window is an owner ruling (2026-07-08) and a real 4.9s countdown must still
                //    read as Battle. The defect is that the test cannot tell "5 seconds left"
                //    from "no countdown is running", and IsAwaitingPlayerStart is the authority
                //    that can - it exists already as a public read-only seam for exactly this
                //    kind of consumer (WaveManager.cs:570-575).
                bool parked = wm.IsAwaitingPlayerStart;
                bool imminent = !parked && wm.CountdownRemaining <= ImminentThreshold;
                FlowTrace.Throttle("HUD", "countdown-posture", 1f,
                    parked
                        ? $"countdown PARKED awaiting the player's DEFEND press ({wm.CountdownRemaining:0.0}s, endless mode) -> gated OUT of Battle (HUD releases; WO-1736)"
                        : imminent
                            ? $"countdown IMMINENT ({wm.CountdownRemaining:0.0}s <= {ImminentThreshold}s) -> counts as Battle"
                            : $"countdown long-gap ({wm.CountdownRemaining:0.0}s > {ImminentThreshold}s) -> gated OUT of Battle (HUD releases)");
                return imminent;
            }

            return false;
        }

        /// <summary>
        /// Replicates VillageHudController.InVillage: a HUB scene (HubScenes) with the
        /// hero inside the town ring around the Heart-at-origin. No hero resolved yet =>
        /// treated as in-ring (don't flip to Overworld before the hero spawns).
        /// </summary>
        private bool IsInTownRing(string scene)
        {
            if (HubScenes.IsOwnedTown(scene)) return true;
            if (!HubScenes.IsHub(scene)) return false;

            if (_hero == null || !_hero) _hero = Object.FindAnyObjectByType<HeroLocomotion>();
            if (_hero == null) return true; // hero not spawned -> default to town

            Vector3 p = _hero.transform.position;
            float distSqr = p.x * p.x + p.z * p.z;   // horizontal distance to origin (Heart)
            // Hysteresis: once inside, allow drifting slightly past before flipping out.
            float edge = _inVillage ? TownRadius + TownRadiusHyst : TownRadius;
            return distSqr <= edge * edge;
        }
    }
}
