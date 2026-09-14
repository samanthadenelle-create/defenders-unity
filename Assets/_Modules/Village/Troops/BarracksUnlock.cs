// =============================================================================
// BarracksUnlock - the ONE source of truth for the WO-724 Barracks unlock rule.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// CHARTER OPTION A (WO-723 charter + WO-724): the EXISTING baked Barracks
// (CastleBarracks) surfaces and its drillmaster + train UI unlock on
// FOUNDING-COMPLETE - NOT on a buildable-barracks / ff.basebuilding path
// (basebuilding stays OFF per the charter; a "buildable barracks" is out of scope).
//
// The founding-complete signal = GameState.Onboarded (set true by
// GameStateService.FinishOnboarding at the FTUE hand-off; the SAME gate the FTUE
// peace window + SylasStewardInjector.ArcIncomplete key on - no new flag).
//
// ⚠ CORRECTED 2026-08-20 (PROD-013). This block used to say "the feature flag ff.barracks stays
// DEFAULT OFF in code (owner 2026-07-10 V1 hide); testers opt in via PlayerPrefs". THAT IS STALE —
// the flag was flipped ON by WO-771 (2026-07-26, the raid deploy loop needs the barracks-gated
// roster in normal play). READ IT OFF THE CODE, NOT OFF THIS COMMENT:
// FeatureFlags.Barracks => Get("barracks", defaultOn: true) — Assets/_Modules/Core/FeatureFlags.cs.
// Set PlayerPrefs "ff.barracks" = 0 to HIDE the barracks again; the opt-in direction is reversed
// from what this comment used to claim.
//
// EVERY runtime surface that decides whether the Barracks exists this session reads
// THIS predicate - the building visual (HubStructureVisualInjector), the drillmaster
// NPC (BarracksNpcInjector), and the train-UI verb (TroopDialogueCommands) - so the
// unlock rule lives in ONE place and can never drift between them.
// =============================================================================

using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>
    /// Single source of truth for the WO-724 Barracks unlock (charter OPTION A):
    /// the Barracks surfaces + the drillmaster/train UI unlock when the feature flag
    /// is ON AND founding is complete. Read by every barracks runtime surface.
    /// </summary>
    public static class BarracksUnlock
    {
        /// <summary>
        /// The founding-complete signal. TWO ways a town can be founded, and BOTH count:
        /// <list type="bullet">
        /// <item><b>Interactive FTUE</b> — <see cref="GameState.Onboarded"/>, set by
        /// <c>GameStateService.FinishOnboarding</c>. The SAME gate the FTUE peace window
        /// keys on. Unchanged; this is still the only signal a Build-Your-Own start has.</item>
        /// <item><b>Premade / Default Town</b> — the persisted explicit selection
        /// <c>founding.default_town_selected</c>, read through
        /// <see cref="StrategicPlacementMigration.HasExplicitDefaultTownSelection"/>
        /// (written ONLY by <c>FoundingChoiceController.OnDefaultTown</c>; Build-Your-Own
        /// writes nothing). WO-1710 symptom 1 / WO-1711 AC#3, owner 2026-09-14: the premade
        /// castle ships with a Barracks already placed, so the army door must be open on it
        /// with no FTUE lap and no remove/re-add ritual.</item>
        /// </list>
        /// <para>⛔ THIS PREDICATE MUST STAY PURELY PERSISTED-STATE. It may NEVER ask
        /// whether a barracks OBJECT exists. <c>HubStructureVisualInjector</c> does
        /// <c>SetActive(false)</c> on <c>CastleBarracks</c> while this reads false, and both
        /// <c>AuthoredCastleStorefront.Find</c> and <c>FindObjectsByType</c> default-exclude
        /// inactive objects — so an existence-based unlock LATCHES OFF on the first locked
        /// load and can never recover. False when no save state is live.</para>
        /// </summary>
        public static bool FoundingComplete
        {
            get
            {
                var svc = GameStateService.Instance;
                var state = svc != null ? svc.State : null;
                if (state == null) return false;
                if (state.Onboarded) return true;
                if (StrategicPlacementMigration.HasExplicitDefaultTownSelection(state))
                {
                    FlowTrace.Once("Barracks", "founding-complete-via-premade",
                        "founding-complete via the PREMADE path: 'founding.default_town_selected' is persisted while " +
                        "Onboarded is still false. The premade castle already carries a Barracks, so the army/train " +
                        "door opens now (WO-1710 sym.1 / WO-1711 AC#3). The interactive FTUE path is untouched.");
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The Barracks is surfaced/interactable when the feature flag is ON AND founding is
        /// complete (either founding path — see <see cref="FoundingComplete"/>).
        /// ff.basebuilding is NOT required.
        /// <para>⚠ THE FLAG DEFAULTS <b>ON</b>, not off. Read it at source:
        /// <c>FeatureFlags.Barracks =&gt; Get("barracks", defaultOn: true)</c>
        /// (<c>Assets/_Modules/Core/FeatureFlags.cs:1151</c>, re-read 2026-09-14) — WO-771
        /// flipped it on 2026-07-26 because the raid deploy loop needs the barracks-gated
        /// roster in normal play. This summary said "default OFF; testers set PlayerPrefs
        /// 'ff.barracks' = 1" until 2026-09-14, contradicting the corrected header 40 lines
        /// above it in the same file. The opt-in direction is REVERSED from that claim: set
        /// PlayerPrefs "ff.barracks" = 0 to HIDE the barracks again.</para>
        /// </summary>
        public static bool IsUnlocked => FeatureFlags.Barracks && FoundingComplete;
    }
}
