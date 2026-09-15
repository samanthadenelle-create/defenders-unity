// =============================================================================
// TroopBreachOrder — the ONE explicit, player-chosen breach target (WO-1719).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Owner ruling 2026-09-14 (WO-1719 sec.1, verbatim): "tap the wall segment
// directly, and it overrides ... add a button for breach and select a wall
// segment" / "the most damanged [stays the fallback]" / "all together unless
// they have aggro".
//
// WHY A SECOND STATIC BESIDE TroopRally, AND NOT A FIELD ON IT.
// -----------------------------------------------------------------------------
// TroopRally is a POINT (Vector3?) with no target reference at all — that is the
// whole finding of WO-1717 sec.3a/b: tapping a wall drops a muster flag ON the
// masonry and nothing in the code reads it as "attack THIS panel". A breach order
// is a REFERENCE to one IDamageable, with a lifetime of its own (it survives the
// rally being moved or cancelled, and it dies when the panel does). Folding a
// reference into the point static would have made "rally cleared" silently mean
// "breach order cleared", which is the exact coupling ToggleRally's own comment
// warns about. Same SHAPE as TroopRally (static, nullable, one writer), separate
// state. This is NOT a second targeting system: TroopController.SharedBreachFocus
// stays the single place the warband's wall is resolved — it just asks here first.
//
// WRITERS: RaidDeployController only (Breach toggle -> tap sets it; toggling the
// mode off, retreating and scene teardown clear it), exactly as it owns TroopRally.
// WO-1746 adds SetStanceArmed to that same writer set - the Breach button arms the
// STANCE before any tap, and the exclusive-arm sites (rally / tile) disarm the mode
// flag while a standing order keeps StanceActive true on its own.
// READERS: TroopController.SharedBreachFocus, via RaidAssaultAi.SelectFocusBreach's
// explicit-override overload.
//
// ⚠ THE GETTER SELF-CLEARS, AND BOTH GUARDS ARE LOAD-BEARING.
//   * IsAlive == false  -> the ordered panel collapsed; the auto most-damaged rule
//     takes back over on the very next resolve (owner: it stays the fallback).
//   * UnityEngine fake-null -> a WallSegment destroyed with its scene leaves this
//     static holding a dead MonoBehaviour whose WorldPosition read (a transform
//     access) THROWS. Collapse() only sinks the ruin, so IsAlive alone never sees
//     this case; only scene teardown does, and teardown is exactly when a stale
//     static leaks into the next raid.
// Every transition bumps Version so a cached resolve can tell it is stale.
// =============================================================================

using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// The single explicit breach target the player ordered by tapping a wall
    /// segment in Breach mode, or null when the warband is on the automatic
    /// most-damaged rule (<see cref="RaidAssaultAi.SelectFocusBreach"/>).
    /// </summary>
    public static class TroopBreachOrder
    {
        private static IDamageable _target;
        private static int _version;
        private static bool _stanceArmed;

        /// <summary>
        /// WO-1746 — TRUE while the warband is "breaching": the Breach button is armed, OR an
        /// explicit order still stands. Siege aside, this is the single flag that decides whether
        /// a troop hits a wall at full damage or at
        /// <see cref="RaidAssaultAi.ReluctantWallDamageMultiplier"/>.
        /// </summary>
        /// <remarks>
        /// ⭐ THE OR IS THE AUTO-CHAIN, AND IT IS THE WHOLE POINT OF THE RULING.
        /// Owner, WO-1738 (2026-09-15): *"One tap = 'we are breaching'; when the ordered wall
        /// falls the warband keeps opening walls ... until the player toggles Breach off or a
        /// hostile pulls aggro. No per-wall tap tax under the 180 s clock."*
        ///
        /// Read the two halves against <see cref="Target"/>'s self-clear, which this ticket KEEPS
        /// exactly as WO-1719 shipped it:
        ///   * the ordered panel COLLAPSES -> <see cref="DropInternal"/> nulls the target, so
        ///     <see cref="HasOrder"/> goes false - but <c>_stanceArmed</c> is UNTOUCHED, so the
        ///     stance holds, <see cref="RaidAssaultAi.SelectFocusBreach"/>'s automatic
        ///     most-damaged rule picks the next panel, and the warband keeps opening walls at
        ///     full damage with no second tap. THAT PATH IS THE AUTO-CHAIN; never disarm there.
        ///   * the player toggles Breach OFF / retreats / the raid tears down -> the public
        ///     <see cref="Clear"/> runs, which DOES disarm, and the warband goes reluctant again.
        ///
        /// The <c>|| HasOrder</c> half covers the exclusive-arm sites: arming Rally or a deploy
        /// tile flips the Breach MODE off while deliberately LEAVING a standing order
        /// (RaidDeployController's own WO-1719 comments). Without it, aiming a rally mid-breach
        /// would silently drop the warband to 10% against the very panel it is still ordered onto.
        /// </remarks>
        public static bool StanceActive { get { return _stanceArmed || HasOrder; } }

        /// <summary>
        /// Arm / disarm the Breach STANCE. Writer is RaidDeployController only, at exactly the
        /// points it already owns the Breach button's arm state.
        /// </summary>
        public static void SetStanceArmed(bool armed)
        {
            if (_stanceArmed == armed) return;
            _stanceArmed = armed;
            string state = armed ? "ARMED" : "disarmed";
            string effect = armed
                ? "every troop now opens walls at FULL structural damage and keeps chaining to the next panel when one falls"
                : "ordinary troops go RELUCTANT again (10% on walls) unless an explicit order still stands";
            FlowTrace.Step("RaidAI",
                "BREACH STANCE " + state + ": " + effect + ". standingOrder=" + HasOrder);
        }

        /// <summary>
        /// Bumped on every set / clear / self-clear. A resolver that caches a focus
        /// compares this to know its cache predates the current order.
        /// </summary>
        public static int Version { get { return _version; } }

        /// <summary>
        /// The ordered target, or null when none stands. Self-clears on a collapsed
        /// panel and on a destroyed Unity object (see the header).
        /// </summary>
        public static IDamageable Target
        {
            get
            {
                // Interface `==` is REFERENCE equality, so a destroyed MonoBehaviour does
                // NOT read null here - that is what IsDestroyedUnityObject is for.
                if (ReferenceEquals(_target, null)) return null;

                if (IsDestroyedUnityObject(_target))
                {
                    DropInternal("the ordered wall's GameObject was destroyed (scene teardown)");
                    return null;
                }

                if (!_target.IsAlive)
                {
                    DropInternal("the ordered wall collapsed - the automatic most-damaged rule takes back over");
                    return null;
                }
                return _target;
            }
        }

        /// <summary>True while an explicit, still-valid player breach order stands.</summary>
        public static bool HasOrder { get { return Target != null; } }

        /// <summary>
        /// Order the warband onto ONE wall segment. A null or already-dead target is
        /// refused rather than stored, so <see cref="HasOrder"/> can never read true
        /// for something the troops cannot hit.
        /// </summary>
        public static void Set(IDamageable wall)
        {
            if (ReferenceEquals(wall, null) || IsDestroyedUnityObject(wall) || !wall.IsAlive)
            {
                FlowTrace.Warn("RaidAI",
                    "breach order REFUSED: the tapped target was null or already destroyed. " +
                    "The warband stays on the automatic most-damaged pick.");
                return;
            }
            if (ReferenceEquals(_target, wall)) return;

            _target = wall;
            _version++;
            string name = NameOf(wall);
            string hp = wall.Hp.ToString("F0");
            FlowTrace.Step("RaidAI",
                "BREACH ORDER set by the player: focus='" + name + "' hp=" + hp +
                " v=" + _version + ". Every Breach-phase troop retargets to THIS panel on its " +
                "next resolve; an aggro'd (Peel) troop keeps its current fight.");
        }

        /// <summary>
        /// Drop the explicit order (troops fall back to the automatic most-damaged
        /// rule). Called on the Breach toggle going off, on retreat and on raid
        /// teardown — the same lifetime points as <see cref="TroopRally.Clear"/>.
        /// </summary>
        public static void Clear()
        {
            // WO-1746: the PUBLIC clear is the player's "stop breaching" - toggle-off, retreat and
            // teardown all arrive here - so it disarms the stance too. The private DropInternal
            // does NOT (see StanceActive): a panel collapsing is the auto-chain, not a cancel.
            // This runs BEFORE the early-out below on purpose, so a toggle-off with no standing
            // order still disarms a stance that was armed and never tapped.
            SetStanceArmed(false);
            if (ReferenceEquals(_target, null)) return;
            DropInternal("cleared by the player / raid teardown");
        }

        /// <summary>
        /// True when the target is a Unity object that has been destroyed. Uses
        /// Unity's overloaded <c>==</c> deliberately (that is the ONLY thing that sees
        /// a "fake null"), guarded by a <c>ReferenceEquals</c> so a pure-C# target -
        /// an EditMode stub - is never mistaken for a dead scene object.
        /// </summary>
        private static bool IsDestroyedUnityObject(IDamageable dmg)
        {
            var uo = dmg as UnityEngine.Object;
            if (ReferenceEquals(uo, null)) return false;
            return uo == null;
        }

        private static void DropInternal(string why)
        {
            string name = NameOf(_target);
            _target = null;
            // (name was read BEFORE the drop so the trace can say what was lost)
            _version++;
            FlowTrace.Step("RaidAI",
                "BREACH ORDER cleared (was '" + name + "'): " + why + ". v=" + _version);
        }

        private static string NameOf(IDamageable dmg)
        {
            var mb = dmg as UnityEngine.MonoBehaviour;
            if (ReferenceEquals(mb, null)) return "<non-scene target>";
            return mb == null ? "<destroyed>" : mb.name;
        }
    }
}
