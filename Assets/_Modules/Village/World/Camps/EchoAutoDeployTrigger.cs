// =============================================================================
// EchoAutoDeployTrigger + EchoWorldPresence + EchoPresenceWatcher
// THE SINGLE OWNER OF WHEN THE ECHO'S WORLD BODY APPEARS AND VANISHES (WO-1108 B).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// OWNER RULE, verbatim (2026-08-16): "The only thing that should happen for the pet
// or the echo is it takes you to the gate, gives you your dialogue, then it
// disappears. The only time it reappears is after your battle."
//
// So there are exactly THREE appearance transitions in the whole game, and all three
// go through EchoWorldPresence below:
//   1. ESCORT SUMMON  - the FTUE guide's body wakes for beat 1/8 and leads beat 2/8
//                       ("Follow {guide} to the gate"). Called by
//                       TutorialFlow.ApplyStarterPetGrant.
//   2. VANISH         - the moment beat 2/8 completes. Fired at the EXISTING
//                       PetHeroLeash.ClearLeadTarget point in TutorialFlow, so
//                       ARRIVAL AND VANISH ARE THE SAME EVENT and cannot disagree.
//   3. REAPPEAR       - exactly ONCE, when the first battle RESOLVES. Guarded by the
//                       same once-per-session static idiom this file already used.
//                       WO-1696: it is also gated on WHERE the hero is standing. The
//                       battle arena is a masked 7km WARP inside the same scene, and on
//                       a WIN the battle lock drops seconds before the return warp, so
//                       an ungated reappearance seated the companion ON THE ARENA STAGE
//                       and left it there for the rest of the session (owner capture
//                       2026-09-10: "wolf is in the battle areena"). The transition is
//                       DEFERRED, never duplicated -- there is still exactly one.
//
// ---------------------------------------------------------------------------
// WHY WO-360's OUTPOST SUMMON IS RETIRED HERE (the two-seam reconcile)
// ---------------------------------------------------------------------------
// This file used to be a SECOND, independent appearance owner: entering an enemy
// outpost's combat radius summoned the Echo (once per session, static guard) and its
// header stated "The Echo PERSISTS - it is never despawned here". That is the direct
// opposite of the rule above on BOTH halves:
//   * it appeared BEFORE/AS the battle started, not after it resolved, and
//   * it never went away again.
// Two owners would have shown the Echo twice by two different rules. The WO forbade
// adding a third seam, so the outpost trigger is RE-POINTED rather than duplicated:
// it no longer summons anything. It now only marks that the player has walked into
// the fight (a trace), and the appearance itself waits for the battle to resolve.
// The WO-360 presentation (golden flourish + the mini-tutorial toast) is NOT lost --
// it MOVED to the reappearance, which is where the player now actually meets the
// Echo again.
//
// The battle-resolve edge is read from BattleLock (DeNelle.Core.Combat) - the ONE
// assembly-neutral "is a battle running" predicate, which both ATBCombatManager and
// ArenaMode register into. Watching the true->false edge means this seam does not
// care WHICH battle system resolved, and no battle owner needs a new callback.
//
// REUSE (no reinvented wheels):
//   * PetDeployer.SummonAt(pos, Defend) - the ONE pet summon path (WO-360).
//   * PetDeployer.DespawnAllEchoBodies  - the WO-1108 despawn verb (the mirror of
//     SpawnPet; before this WO no despawn path existed anywhere in the pet stack).
//   * VFXManager.Play(Juice_LevelUp, pos) - best-effort golden flourish. Null-safe.
//   * EchoTutorialUI.Show(name) - the code-built bottom-left mini-tutorial toast.
//   * EnsurePetDeployer() - the same self-heal DialogueCommandSink / TutorialFlow use.
//
// Isolation/safety: lives in DeNelle.Village; references DeNelle.Pets via the
// Village asmdef. Every cross-call null-guarded + Guard/try-caught. ASCII-only
// strings. Per CLAUDE.md sec.12 every transition self-reports through FlowTrace so a
// single capture can prove WHICH beat fired -- never strip these lines.
// =============================================================================

using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Pets;
using UnityEngine;
using UnityEngine.AI;

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// A SphereCollider trigger that marks the first time the Player enters an enemy
    /// outpost's combat radius. WO-1108: it NO LONGER SUMMONS the Echo -- appearance is
    /// owned solely by <see cref="EchoWorldPresence"/>, which brings the Echo back only
    /// after the battle RESOLVES. Idempotent per session. Attach via <see cref="Attach"/>
    /// from <see cref="EnemyOutpost"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EchoAutoDeployTrigger : MonoBehaviour
    {
        // Fires at most once per session (multiple outposts / re-entries are no-ops).
        // Kept as the WO-360 static-guard idiom; only what it guards changed.
        private static bool s_outpostEnteredThisSession;

        private float _triggerRadius = 12f;
        private bool _fired;

        /// <summary>
        /// Adds an EchoAutoDeployTrigger to <paramref name="host"/> with a trigger
        /// sphere of <paramref name="radius"/>. Called by EnemyOutpost.Start().
        /// </summary>
        public static EchoAutoDeployTrigger Attach(GameObject host, float radius)
        {
            if (host == null) return null;
            var trig = host.GetComponent<EchoAutoDeployTrigger>();
            if (trig == null) trig = host.AddComponent<EchoAutoDeployTrigger>();
            trig._triggerRadius = Mathf.Max(2f, radius);
            trig.EnsureTriggerCollider();
            return trig;
        }

        private void EnsureTriggerCollider()
        {
            // A dedicated child trigger so we don't disturb any solid colliders on
            // the outpost root (garrison/fort pieces have their own).
            var existing = GetComponent<SphereCollider>();
            SphereCollider col = existing;
            if (col == null) col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = _triggerRadius;
            col.center = Vector3.zero;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryMarkBattleEntered(other);
        }

        // Belt-and-braces: if the player is already standing inside the radius when
        // the trigger spawns, OnTriggerEnter won't fire — poll a couple of frames.
        private void Start()
        {
            if (!s_outpostEnteredThisSession && !_fired)
                Invoke(nameof(PollForPlayer), 0.25f);
        }

        private void PollForPlayer()
        {
            if (_fired || s_outpostEnteredThisSession) return;
            var player = GameObject.FindWithTag("Player");
            if (player != null &&
                Vector3.Distance(player.transform.position, transform.position) <= _triggerRadius)
            {
                Fire(player.transform.position);
            }
        }

        private void TryMarkBattleEntered(Collider other)
        {
            if (_fired || s_outpostEnteredThisSession || other == null) return;
            // The hero is tagged "Player" (CLAUDE.md §7); accept the player or a
            // child collider of the player root.
            if (!other.CompareTag("Player"))
            {
                var root = other.transform.root;
                if (root == null || !root.CompareTag("Player")) return;
            }
            Fire(other.transform.position);
        }

        // WO-1108: THIS NO LONGER SUMMONS. Walking into the outpost is the START of a
        // battle, and the owner's rule is that the Echo reappears only AFTER one. All
        // this does now is record the beat so a capture can prove the ordering
        // (entered-fight -> battle resolved -> REAPPEAR) instead of leaving the
        // reappearance looking like it came from nowhere.
        private void Fire(Vector3 atPosition)
        {
            if (_fired || s_outpostEnteredThisSession) return;
            _fired = true;
            s_outpostEnteredThisSession = true;

            FlowTrace.Step("Echo",
                $"outpost combat radius ENTERED at {atPosition} -- the WO-360 summon here is RETIRED " +
                "(WO-1108: one appearance owner, EchoWorldPresence). The Echo does NOT appear for the " +
                "fight; it returns once the battle RESOLVES, with the flourish + toast that used to " +
                $"play here. awaitingReappear={EchoWorldPresence.AwaitingBattleReappear}, " +
                $"alreadyReappeared={EchoWorldPresence.ReappearedThisSession}.");
        }

        /// <summary>The player's name for their Echo, or "Echo". Shared with
        /// <see cref="EchoWorldPresence"/> so there is ONE spelling of the name lookup.</summary>
        internal static string ResolveEchoName()
        {
            var state = GameStateService.Instance?.State;
            if (state != null && !string.IsNullOrWhiteSpace(state.PetName))
                return state.PetName.Trim();
            return "Echo";
        }

        // Self-heal a PetDeployer in the world scene if none exists (mirrors
        // DialogueCommandBridge.EnsurePetDeployer): the OuterWorld may ship without
        // one. Heart/origin centre, project "Enemy" layer mask, save-bond ranks.
        // internal: EchoWorldPresence reuses it rather than inventing a fifth spelling.
        internal static PetDeployer EnsurePetDeployer()
        {
            var deployer = FindAnyObjectByType<PetDeployer>();
            if (deployer != null) return deployer;

            var go = new GameObject("PetDeployer");
            deployer = go.AddComponent<PetDeployer>();

            Vector3 heartPos = Vector3.zero;
            var heart = FindAnyObjectByType<HeartController>();
            if (heart != null) heartPos = heart.transform.position;
            deployer.SetHeartPosition(heartPos);

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            deployer.SetEnemyMask(enemyLayer >= 0 ? (1 << enemyLayer) : ~0);

            var svc = GameStateService.Instance;
            if (svc != null && svc.State != null && svc.State.PetBonds != null)
            {
                var b = svc.State.PetBonds;
                int aether = b.Count > 0 ? b[0] : 0;
                int flame  = b.Count > 1 ? b[1] : 0;
                int ice    = b.Count > 2 ? b[2] : 0;
                deployer.SetBondRanks(aether, flame, ice);
            }

            Debug.Log($"[EchoAutoDeployTrigger] Self-healed a PetDeployer (heart={heartPos}).");
            return deployer;
        }
    }

    // =========================================================================
    //  EchoWorldPresence — THE SINGLE APPEARANCE OWNER (WO-1108 Lane B)
    // =========================================================================
    /// <summary>
    /// Owns the Echo's world body lifecycle: escort summon -> vanish on arrival ->
    /// exactly ONE reappearance after the first battle resolves. Every transition is
    /// FlowTrace'd so one capture proves which beat fired (CLAUDE.md sec.12).
    /// <para/>
    /// State is SESSION-scoped statics, matching the WO-360 idiom this file already
    /// used (<c>s_outpostEnteredThisSession</c>): the live body carries across scene loads,
    /// so a per-scene guard would re-fire the beat every time the hub reloads.
    /// </summary>
    public static class EchoWorldPresence
    {
        private static bool s_escortSummoned;
        private static bool s_despawnedAfterEscort;
        private static bool s_reappearedThisSession;

        /// <summary>True once the escort body has been summoned this session.</summary>
        public static bool EscortSummoned => s_escortSummoned;

        /// <summary>True once the escort beat ended and the body was removed.</summary>
        public static bool DespawnedAfterEscort => s_despawnedAfterEscort;

        /// <summary>True once the post-battle reappearance has happened (it happens once).</summary>
        public static bool ReappearedThisSession => s_reappearedThisSession;

        /// <summary>True while the Echo is off-stage waiting for a battle to resolve.</summary>
        public static bool AwaitingBattleReappear => s_despawnedAfterEscort && !s_reappearedThisSession;

        /// <summary>How many Echo/pet bodies are in the world right now (scene-counted).</summary>
        public static int LiveBodyCount => PetDeployer.LiveBodyCount;

        /// <summary>
        /// TRANSITION 1 — the escort body. Called by TutorialFlow's starter-pet grant so
        /// the "Follow {guide} to the gate" beat has something to follow. Routed through
        /// this owner (rather than TutorialFlow calling PetDeployer directly) so ALL THREE
        /// transitions are visible in one place and in one trace stream.
        /// </summary>
        public static bool SummonEscortBody(Vector3 at, string reason)
        {
            var deployer = EchoAutoDeployTrigger.EnsurePetDeployer();
            if (deployer == null)
            {
                FlowTrace.Fail("Echo",
                    $"echo ESCORT SUMMON FAILED ({reason}): no PetDeployer could be found or built, so the " +
                    "guide has NO BODY and 'Follow {guide} to the gate' points at nothing.");
                return false;
            }

            Vector3 safeAt = ResolveSafeEscortSpawn(at);
            Pet body = Guard.Try("Echo", "summon the escort body", () => deployer.SummonAt(safeAt), null);
            if (body == null)
            {
                FlowTrace.Warn("Echo",
                    $"echo ESCORT SUMMON produced no body ({reason}) at {safeAt} -- PetDeployer.SummonAt returned " +
                    "null (no pet owned/chosen, or the catalog is empty). The escort beat will fall through " +
                    "to the steward stand-in.");
                return false;
            }

            s_escortSummoned = true;
            string echoName = EchoAutoDeployTrigger.ResolveEchoName();
            if (!string.IsNullOrEmpty(echoName)) body.name = echoName;
            FlowTrace.Step("Echo",
                $"echo APPEAR (escort): body '{body.name}' summoned at {safeAt} ({reason}). bodies={LiveBodyCount}. " +
                "It leads the gate beat and then VANISHES at that beat's completion (WO-1108).");
            return true;
        }

        internal const float EscortHeroSeparation = 3.25f;

        /// <summary>Keep the founding guide visibly separate from the hero while remaining on
        /// the town navmesh. The requested guide anchor is still authoritative; this only adds a
        /// small deterministic staging offset when that anchor coincides with the hero spawn.</summary>
        private static Vector3 ResolveSafeEscortSpawn(Vector3 requested)
        {
            GameObject hero = null;
            Guard.Try("Echo", "resolve hero for escort separation", () => hero = GameObject.FindGameObjectWithTag("Player"));
            if (hero == null) return requested;

            Vector3 heroPos = hero.transform.position;
            Vector3 flatDelta = requested - heroPos;
            flatDelta.y = 0f;
            if (flatDelta.sqrMagnitude >= EscortHeroSeparation * EscortHeroSeparation)
                return requested;

            Vector3 forward = hero.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();

            // Try in front, then either side, then behind. All candidates remain only 3.25m
            // from the opening anchor—well inside the town rather than on the enemy ring.
            Vector3[] directions = { forward, Vector3.Cross(Vector3.up, forward),
                                     Vector3.Cross(forward, Vector3.up), -forward };
            foreach (Vector3 direction in directions)
            {
                Vector3 candidate = heroPos + direction * EscortHeroSeparation;
                candidate.y = requested.y;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                Vector3 separation = hit.position - heroPos;
                separation.y = 0f;
                if (separation.sqrMagnitude >= 2.75f * 2.75f)
                {
                    FlowTrace.Step("Echo", $"escort staging moved from overlapping anchor {requested} to navmesh-safe {hit.position}; hero={heroPos}.");
                    return hit.position;
                }
            }

            FlowTrace.Warn("Echo", $"escort staging found no separated navmesh point near {requested}; using the authoritative guide anchor.");
            return requested;
        }

        /// <summary>
        /// TRANSITION 2 — the vanish. Fired from TutorialFlow at the EXISTING
        /// PetHeroLeash.ClearLeadTarget point, so arrival and vanish are the same event
        /// and cannot disagree. Idempotent: a second call only sweeps strays.
        /// </summary>
        public static void NotifyEscortComplete(string reason)
        {
            int removed = 0;
            Guard.Try("Echo", "despawn the echo after the escort",
                () => removed = PetDeployer.DespawnAllEchoBodies(reason));

            if (s_despawnedAfterEscort)
            {
                FlowTrace.Step("Echo",
                    $"echo VANISH re-asserted ({reason}) -- already despawned this session; swept {removed} " +
                    $"stray body/bodies. bodies={LiveBodyCount}.");
                return;
            }

            s_despawnedAfterEscort = true;
            FlowTrace.Step("Echo",
                $"echo VANISH: the escort beat is over ({reason}); removed {removed} world body/bodies, " +
                $"bodies={LiveBodyCount}. From here the Echo reappears EXACTLY ONCE, when the first battle " +
                "resolves (owner ruling WO-1108).");

            if (removed == 0 && s_escortSummoned)
                FlowTrace.Warn("Echo",
                    "echo VANISH found NO body to remove even though an escort body was summoned this " +
                    "session -- something destroyed or re-parented it outside PetDeployer, so the despawn " +
                    "verb had nothing to tear down. Route every pet spawn through PetDeployer.SummonAt.");
        }

        /// <summary>
        /// TRANSITION 3 — the one reappearance. Called by <see cref="EchoPresenceWatcher"/>
        /// on the battle-resolve edge. Returns true only on the single firing.
        /// </summary>
        public static bool TryReappearAfterBattle(string reason)
        {
            if (s_reappearedThisSession)
            {
                FlowTrace.Step("Echo",
                    $"echo REAPPEAR suppressed ({reason}) -- it already returned this session. The " +
                    "once-per-session static guard is the whole point: the Echo comes back after the " +
                    "FIRST battle, not after every battle.");
                return false;
            }
            if (!s_despawnedAfterEscort)
            {
                FlowTrace.Step("Echo",
                    $"echo REAPPEAR skipped ({reason}) -- the escort vanish has not happened this session, " +
                    "so there is nothing off-stage to bring back (nothing to do, not an error).");
                return false;
            }

            bool inBattle = false;
            Guard.Try("Echo", "reappear battle-gate", () => inBattle = DeNelle.Core.Combat.BattleLock.IsInBattle());
            if (inBattle)
            {
                FlowTrace.Warn("Echo",
                    $"echo REAPPEAR refused ({reason}) -- BattleLock still reports a battle in progress. " +
                    "The Echo returns AFTER the fight, never during it.");
                return false;
            }

            // WO-1696 -- THE OFF-STAGE GATE. The arena is a MASKED WARP, not a scene: the hero is
            // teleported 7km to ArenaCentre (5000,0,5000) inside Main_Castle_Overworld and warped
            // home again on resolve. BattleLock drops the moment BattleArena.Resolve runs, which on
            // a WIN is several seconds BEFORE the return warp (kill stream + celebration + fade).
            // EchoPresenceWatcher polls that edge every 0.5s, so on a first-battle WIN in the arena
            // the reappearance fired while the hero still stood on the stage and
            // ResolveReturnPosition seated the companion beside him -- AT 5000. Captured proof
            // (Builds/device-frames/2026-09-10_1520_logcat.txt): resolve :714168 -> REAPPEAR at
            // (4997.29, 0.08, 5002.89) :714180 -> return warp home only at :715040. The Echo was
            // BORN on the stage and stayed there; every later arena entry warps the hero back to
            // the wolf. (On a LOSS the order inverts -- warp :731866, resolve :731911 -- which is
            // why the same session ALSO shows healthy town returns and why an ordering assumption
            // is the wrong fix.)
            //
            // So the gate is POSITIONAL, not ordering-based: while the hero stands inside the
            // staged arena, defer. BattleArena.IsArenaPosition (Arena/BattleArena.cs:102) is a pure
            // public static predicate in this same assembly -- reading it touches none of the
            // battle-lock code. The guard is deliberately NOT consumed (same contract as the
            // null-summon branch below), and EchoPresenceWatcher retries every poll until the hero
            // is home, so the one reappearance still happens -- in town, where it belongs. The
            // once-per-session rule is untouched.
            if (IsHeroOnArenaStage(out Vector3 stagePos))
            {
                FlowTrace.Warn("Echo",
                    $"echo REAPPEAR DEFERRED ({reason}) -- the hero is still on the arena stage at {stagePos} " +
                    "(BattleArena.IsArenaPosition). Seating the companion here would strand it 7km from town, " +
                    "which is exactly what the 2026-09-10 capture shows. The once-per-session guard is NOT " +
                    "consumed; EchoPresenceWatcher retries once the hero is home.");
                return false;
            }

            var deployer = EchoAutoDeployTrigger.EnsurePetDeployer();
            if (deployer == null)
            {
                FlowTrace.Fail("Echo",
                    $"echo REAPPEAR FAILED ({reason}): no PetDeployer could be found or built. The Echo is " +
                    "off-stage with no way back -- the player never sees it again this session.");
                return false;
            }

            Vector3 at = ResolveReturnPosition();
            Pet echo = Guard.Try("Echo", "summon the echo after the battle",
                () => deployer.SummonAt(at, PetMode.Defend), null);
            if (echo == null)
            {
                FlowTrace.Fail("Echo",
                    $"echo REAPPEAR FAILED ({reason}): PetDeployer.SummonAt returned null at {at} (no pet " +
                    "owned/chosen, or the catalog is empty). The guard is NOT consumed, so a later battle " +
                    "resolve can still bring it back.");
                return false;
            }

            s_reappearedThisSession = true;
            string echoName = EchoAutoDeployTrigger.ResolveEchoName();
            if (!string.IsNullOrEmpty(echoName)) echo.name = echoName;

            // WO-1380 -- THE VOICE. The Echo that just came back from the expedition is the
            // Guide the player chose, and this is the beat where it remembers the place it was
            // taken to. Additive: the appearance lifecycle above is untouched, no second body
            // is summoned, and a Guide with nothing to say simply says nothing.
            SpeakGuideMemory(EchoGuideService.LastExpeditionTargetId, "post-battle return");

            // The WO-360 presentation, MOVED here from the outpost entry (see the file
            // header): this is where the player actually meets the Echo again.
            PlaySummonFlourish(echo.transform.position);
            Guard.Try("Echo", "echo reappear toast", () => EchoTutorialUI.Show(echoName));

            FlowTrace.Step("Echo",
                $"echo REAPPEAR: '{echo.name}' returned at {at} after the battle ({reason}). " +
                $"bodies={LiveBodyCount}. This fires ONCE per session and never again.");
            return true;
        }

        /// <summary>
        /// WO-1380 -- THE ECHO GUIDE'S VOICE, and the ONLY place it speaks.
        /// <para/>
        /// The Echo does not fight. It REMEMBERS: it recognises a fragment of the world that
        /// existed before the Realm fell (creative canon 2026-09-04 sec.7). This method adds no
        /// body, no lifecycle and no second spawner -- the appearance owner is still the three
        /// transitions above. It reads the player's chosen Guide and the authored line for the
        /// target, traces it, and surfaces it through the shared toast.
        /// <para/>
        /// SCOPE FENCE (owner ruling): narrative ONLY. Nothing here grants a stat, a yield or a
        /// combat effect, and EchoGuideMemoryRegression fails if that ever changes.
        /// <para/>
        /// Returns the line that was spoken, or null when there is nothing authored for this
        /// pairing (warned by the catalog -- never a silent blank).
        /// </summary>
        public static string SpeakGuideMemory(string targetIdOrSceneName, string reason)
        {
            if (string.IsNullOrEmpty(targetIdOrSceneName))
            {
                FlowTrace.Step("Echo",
                    "guide memory skipped (" + reason + "): no expedition target recorded this session, " +
                    "so the Echo has no place to remember. Nothing to do, not an error.");
                return null;
            }

            var guide = EchoGuideService.SelectedGuide;
            string line = EchoGuideService.MemoryLineFor(targetIdOrSceneName);
            if (string.IsNullOrEmpty(line))
            {
                FlowTrace.Warn("Echo",
                    "guide memory MISSING (" + reason + ") for guide=" +
                    (guide != null ? guide.Id : "(none)") + " target=" + targetIdOrSceneName +
                    ". A Guide standing at a target with nothing to say is the failure WO-1380 " +
                    "rules against -- all 24 lines must ship.");
                return null;
            }

            string speaker = guide != null && !string.IsNullOrEmpty(guide.DisplayName)
                ? guide.DisplayName : "Your Echo";
            FlowTrace.Step("Echo",
                "guide memory (" + reason + "): " + speaker + " at " + targetIdOrSceneName + " -- \"" +
                line + "\"");

            Guard.Try("Echo", "show the guide memory toast", () =>
                DeNelle.Core.UI.ElarionUiKit.ShowToast(speaker + ": " + line,
                    DeNelle.Core.UI.ElarionUiKit.ToastTone.Info, 5.0f, 720, 640f, 132f));
            return line;
        }

        /// <summary>
        /// Clears the session statics. The lifecycle oracle drives the three transitions
        /// in one process, so it needs a defined starting point; nothing in gameplay calls
        /// this (a session IS the scope).
        /// </summary>
        public static void ResetSessionState()
        {
            s_escortSummoned = false;
            s_despawnedAfterEscort = false;
            s_reappearedThisSession = false;
        }

        private const float ReturnBehindMetres = 2.4f;
        private const float ReturnSideMetres = 1.4f;

        /// <summary>Pure companion seat used by the post-battle return and its regression.
        /// The Echo belongs beside/behind the hero, never at the hero's own origin.</summary>
        public static Vector3 ReturnSeat(Vector3 heroPosition, Vector3 heroForward, Vector3 heroRight)
        {
            Vector3 forward = Vector3.ProjectOnPlane(heroForward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(heroRight, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            if (right.sqrMagnitude < 0.5f) right = Vector3.right;
            return heroPosition - forward * ReturnBehindMetres + right * ReturnSideMetres;
        }

        /// <summary>
        /// WO-1696 -- true while the "Player"-tagged hero stands inside the staged battle arena.
        /// <para/>
        /// The ONE authority on "is this position the arena" is
        /// <c>DeNelle.Village.Arena.BattleArena.IsArenaPosition</c> (Arena/BattleArena.cs:102) -- a
        /// pure public static predicate over a world position. It is READ here and never
        /// re-implemented: the 7km offset and the radius live in that one file, and a second copy of
        /// either is the duplicated-state failure CLAUDE.md sec.2/5/8/16 each describe. Reading a
        /// predicate is not touching the arena's battle-lock code (WO-1694).
        /// <para/>
        /// No hero found = NOT on the stage: the town/Heart fallbacks inside
        /// <see cref="ResolveReturnPosition"/> already handle that case and they never produce an
        /// arena seat, so failing open here cannot strand the Echo.
        /// </summary>
        private static bool IsHeroOnArenaStage(out Vector3 heroPos)
        {
            heroPos = Vector3.zero;
            GameObject hero = null;
            Guard.Try("Echo", "resolve hero for the arena-stage gate",
                () => hero = GameObject.FindWithTag("Player"));
            if (hero == null) return false;

            // `pos` is a plain local on purpose: an `out` parameter may NOT be captured by a lambda
            // (CS1628), and Guard.Try takes one.
            Vector3 pos = hero.transform.position;
            heroPos = pos;
            bool onStage = false;
            Guard.Try("Echo", "arena-stage predicate",
                () => onStage = DeNelle.Village.Arena.BattleArena.IsArenaPosition(pos));
            return onStage;
        }

        // The hero is tagged "Player" (CLAUDE.md sec.7). Seat the companion off the
        // hero's capsule, then snap that desired seat to nearby walkable ground.
        private static Vector3 ResolveReturnPosition()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                Vector3 desired = ReturnSeat(player.transform.position,
                    player.transform.forward, player.transform.right);
                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    return hit.position;
                FlowTrace.Warn("Echo",
                    $"echo REAPPEAR companion seat {desired} was not on a NavMesh within 3m -- using " +
                    "the offset seat without snapping; it remains clear of the hero origin.");
                return desired;
            }

            var heart = Object.FindAnyObjectByType<HeartController>();
            if (heart != null)
            {
                FlowTrace.Warn("Echo",
                    "echo REAPPEAR could not find a 'Player'-tagged hero -- returning the Echo at the Heart " +
                    "instead. It should return to the player's side.");
                return heart.transform.position;
            }

            FlowTrace.Warn("Echo",
                "echo REAPPEAR found neither a 'Player'-tagged hero nor a Heart -- returning the Echo at the " +
                "world origin.");
            return Vector3.zero;
        }

        // Best-effort GOLDEN flourish. The project has no dedicated "pet summon" VFX --
        // reuse the celebratory level-up burst, which reads gold (WO-360 pick, kept).
        private static void PlaySummonFlourish(Vector3 pos)
        {
            try { VFXManager.Play(VFXType.Juice_LevelUp, pos); }
            catch { /* VFX is cosmetic -- never let it break the reappearance */ }
        }
    }

    // =========================================================================
    //  EchoPresenceWatcher — the battle-resolve edge detector
    // =========================================================================
    /// <summary>
    /// Polls <c>BattleLock.IsInBattle()</c> and calls
    /// <see cref="EchoWorldPresence.TryReappearAfterBattle"/> on the true-&gt;false edge.
    /// Code-built + DontDestroyOnLoad (mirrors PetHarvestBootstrap): no scene edit, no
    /// new callback on any battle owner, and it does not care which battle system ran.
    /// </summary>
    public sealed class EchoPresenceWatcher : MonoBehaviour
    {
        private const float PollSeconds = 0.5f;

        private static EchoPresenceWatcher _instance;
        private bool _wasInBattle;
        private float _timer;

        // WO-1696 -- set when a resolve edge could not seat the Echo yet (the hero was still on the
        // arena stage). While it is set, every poll retries, so the ONE reappearance lands the
        // instant the return warp puts the hero back in town. It is a RETRY of the same single
        // transition, not a new one: TryReappearAfterBattle still owns the once-per-session guard
        // and clears this the moment it fires.
        private bool _reappearPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("EchoPresenceWatcher");
            _instance = go.AddComponent<EchoPresenceWatcher>();
            Object.DontDestroyOnLoad(go);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = PollSeconds;

            bool inBattle = false;
            Guard.Try("Echo", "battle-resolve probe",
                () => inBattle = DeNelle.Core.Combat.BattleLock.IsInBattle());

            if (inBattle == _wasInBattle)
            {
                // WO-1696 -- the deferred landing. No edge this poll, but a resolve already happened
                // while the hero stood on the arena stage; keep retrying (out of battle only) until
                // the return warp puts him in town. Cheap: one bool, and it clears on the first
                // success or as soon as the reappearance is no longer owed.
                if (!_reappearPending || inBattle) return;
                if (!EchoWorldPresence.AwaitingBattleReappear)
                {
                    _reappearPending = false;
                    return;
                }
                if (EchoWorldPresence.TryReappearAfterBattle("arena return (deferred landing)"))
                {
                    _reappearPending = false;
                    FlowTrace.Step("Echo",
                        "deferred reappearance LANDED -- the Echo was owed a return from a battle that " +
                        "resolved while the hero was still on the arena stage, and it has now come back in " +
                        "town. One transition, retried; the once-per-session guard fired exactly once.");
                }
                return;
            }
            _wasInBattle = inBattle;

            if (inBattle)
            {
                FlowTrace.Step("Echo",
                    $"battle STARTED (BattleLock) -- awaitingReappear={EchoWorldPresence.AwaitingBattleReappear}, " +
                    $"alreadyReappeared={EchoWorldPresence.ReappearedThisSession}. The Echo returns when this resolves.");
                return;
            }

            FlowTrace.Step("Echo", "battle RESOLVED (BattleLock true->false) -- evaluating the one reappearance.");
            bool returned = EchoWorldPresence.TryReappearAfterBattle("first battle resolved");
            // WO-1696: owed but not seated (the arena-stage gate deferred it) -> retry each poll.
            _reappearPending = !returned && EchoWorldPresence.AwaitingBattleReappear;
            if (_reappearPending)
                FlowTrace.Step("Echo",
                    "reappearance still OWED after this resolve -- retrying every poll until the hero is off " +
                    "the arena stage. (If the Echo had simply been seated here it would have landed 7km away " +
                    "at ArenaCentre; see the WO-1696 note on the gate.)");
        }
    }
}
