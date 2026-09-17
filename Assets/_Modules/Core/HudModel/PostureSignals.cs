// =============================================================================
// PostureSignals — Core-visible battle-arc / interaction signals for the HUD kit.
// (HUD_OBSIDIAN_ARCHITECTURE_2026-07-03 amendments A4.4-A4.7 — P23 HUDKIT.)
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.HudModel
//
// THE PROBLEM THIS SOLVES: the posture arc (calm -> hostile(prebattle |
// activebattle | postbattle)) needs Village-side facts (enemy pursuit/aggro,
// the end-state screen, NPC-in-range talk availability) but DeNelle.HUD may
// reference DeNelle.Core ONLY (CLAUDE.md §5). Village pushes the facts here;
// the HUD-side PostureEvaluator reads them. Pure data — no UnityEngine.UI.
//
// PURSUIT is PULSE-BASED (self-decaying): Village re-reports an active pursuit
// every poll tick (RegionMobSpawner aggro loop); a pursuit that stops being
// reported expires after PursuitTtl seconds. This makes the engagement window
// robust against missed "clear" paths (death/despawn/scene-unload) — the same
// reasoning as the leash system's own decay.
//
// TALK availability ALSO lives here (root cause of the "talk button never
// appears" §0 felt failure): TalkHudBridge used a ONE-SHOT reflection hook onto
// a PER-SCENE VillageHudController — after the first scene swap the cached
// MethodInfo targeted a destroyed instance and the bridge never re-hooked
// (MaxResolveAttempts exhausted), so availability was never pushed again.
// A Core static cannot go stale.
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.HudModel
{
    /// <summary>Village-pushed facts the HUD posture arc derives from (A4.4-A4.7).</summary>
    public static class PostureSignals
    {
        /// <summary>Seconds a reported pursuit stays live without being re-reported.</summary>
        public const float PursuitTtl = 1.5f;

        // ── Pursuit / engagement window (A4.5) ───────────────────────────────

        // Small fixed ring of (key, lastReport, owner) triples — no allocation per pulse.
        private const int MaxTracked = 12;
        private static readonly int[]    _pursuitKeys   = new int[MaxTracked];
        private static readonly float[]  _pursuitAt     = new float[MaxTracked];
        private static readonly string[] _pursuitOwners = new string[MaxTracked];
        private static int _pursuitCount;

        /// <summary>Owner tag stored for a stamp that named none. See <see cref="DescribePursuits"/>.</summary>
        public const string UnnamedPursuitOwner = "unnamed";

        // =====================================================================
        //  WO-1603 — NAME THE PULSER. THE RING NOW CARRIES ITS OWNER.
        // ---------------------------------------------------------------------
        //  The ring used to record (key, lastReport) only, and the key is an
        //  INSTANCE ID — a number that identifies nothing once the body is gone.
        //  That is why F8 seq 4701/4702 could say the battle-lock was HELD by
        //  PursuitBattleProbe.Probe and could not say WHO was stamping the pulse
        //  the probe reads. Three producers call ReportPursuit —
        //  Enemy.DriveNav (two DIFFERENT branches, see below),
        //  OverworldEncounterSpawner's rep chase and RegionMobSpawner's aggro
        //  loop — and the captured message narrowed it to none of them.
        //
        //  ⚠ THE OWNER IS RECORDED, NOT LOGGED PER STAMP. ReportPursuit runs
        //  EVERY aggro tick per chaser; a FlowTrace line at each stamp would be a
        //  per-frame firehose that evicts the boot window out of the device
        //  logcat ring and destroys the very evidence it was added to collect
        //  (CLAUDE.md §12; memory `logcat-ring-buffer-destroys-evidence`). The
        //  existing trace stays EDGE-ONLY — first add of a key — and now carries
        //  the owner; DescribePursuits() renders the whole ring on demand, and is
        //  called only at battle end and on a quiescence FAIL.
        // =====================================================================

        /// <summary>
        /// Report that the enemy identified by <paramref name="key"/> (instance id) is
        /// actively pursuing the hero RIGHT NOW. Call every aggro tick — the report
        /// self-expires after <see cref="PursuitTtl"/> so no explicit clear is needed.
        /// </summary>
        /// <param name="owner">
        /// WHO is stamping, as a short stable tag ("Enemy.DriveNav/aggro"). Rendered by
        /// <see cref="DescribePursuits"/> so a stuck battle-lock names its pulser instead of
        /// naming the probe that merely reads it. Null keeps the old anonymous behaviour.
        /// </param>
        public static void ReportPursuit(int key, string owner)
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _pursuitCount; i++)
            {
                if (_pursuitKeys[i] != key) continue;
                _pursuitAt[i] = now;
                // A key can change hands (an id reused by a pooled body): keep the LATEST
                // honest tag rather than the one that opened the entry.
                if (!string.IsNullOrEmpty(owner)) _pursuitOwners[i] = owner;
                return;
            }
            Prune(now);
            if (_pursuitCount >= MaxTracked) return;   // ring full — window is already open
            _pursuitKeys[_pursuitCount]   = key;
            _pursuitAt[_pursuitCount]     = now;
            _pursuitOwners[_pursuitCount] = string.IsNullOrEmpty(owner) ? UnnamedPursuitOwner : owner;
            _pursuitCount++;
            FlowTrace.Step("HudKit",
                $"pursuit reported (key={key}, owner={_pursuitOwners[_pursuitCount - 1]}, live={_pursuitCount})");
        }

        /// <summary>Anonymous overload — kept so no caller breaks; prefer the tagged one.</summary>
        public static void ReportPursuit(int key) => ReportPursuit(key, null);

        /// <summary>Number of live (un-expired) pursuit pulses. Prunes as it reads.</summary>
        public static int PursuitCount
        {
            get { Prune(Time.unscaledTime); return _pursuitCount; }
        }

        /// <summary>
        /// Every live pulse as "key=&lt;id&gt; owner='&lt;tag&gt;' age=&lt;s&gt;", or "none".
        /// The AGE is the half that tells a LIVE chase (age ≈ 0, re-stamped this frame) apart
        /// from a latched pulse riding out its TTL (age approaching <see cref="PursuitTtl"/>) —
        /// the two shapes the quiescence gate's own message asks the reader to distinguish.
        /// Allocates: call it at battle end / on a failure, never per frame.
        /// </summary>
        public static string DescribePursuits()
        {
            float now = Time.unscaledTime;
            Prune(now);
            if (_pursuitCount == 0) return "none";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _pursuitCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("key=").Append(_pursuitKeys[i])
                  .Append(" owner='").Append(_pursuitOwners[i] ?? UnnamedPursuitOwner)
                  .Append("' age=").Append((now - _pursuitAt[i]).ToString("F2")).Append('s');
            }
            return sb.ToString();
        }

        /// <summary>True while at least one un-expired pursuit report is live —
        /// the enemy half of the A4.5 engagement window.</summary>
        public static bool PursuitActive
        {
            get
            {
                float now = Time.unscaledTime;
                Prune(now);
                return _pursuitCount > 0;
            }
        }

        private static void Prune(float now)
        {
            for (int i = _pursuitCount - 1; i >= 0; i--)
            {
                if (now - _pursuitAt[i] <= PursuitTtl) continue;
                _pursuitCount--;
                _pursuitKeys[i]   = _pursuitKeys[_pursuitCount];
                _pursuitAt[i]     = _pursuitAt[_pursuitCount];
                _pursuitOwners[i] = _pursuitOwners[_pursuitCount];
            }
        }

        /// <summary>Drop one enemy's pursuit pulse (death / despawn). Other live pursuers
        /// keep <see cref="PursuitActive"/> true so combat HUD stays up until the last
        /// threat is gone.</summary>
        public static void RevokePursuit(int key)
        {
            for (int i = _pursuitCount - 1; i >= 0; i--)
            {
                if (_pursuitKeys[i] != key) continue;
                string revokedOwner = _pursuitOwners[i] ?? UnnamedPursuitOwner;
                _pursuitCount--;
                _pursuitKeys[i]   = _pursuitKeys[_pursuitCount];
                _pursuitAt[i]     = _pursuitAt[_pursuitCount];
                _pursuitOwners[i] = _pursuitOwners[_pursuitCount];
                FlowTrace.Step("HudKit",
                    $"pursuit revoked (key={key}, owner={revokedOwner}, live={_pursuitCount})");
                return;
            }
        }

        /// <summary>Clears all pursuit pulses (hub return / combat end — peaceful HUD).</summary>
        public static void ClearPursuits()
        {
            if (_pursuitCount == 0) return;
            // Name what is being dropped: the ONE line that says which owners a battle-end
            // clear actually took down, so a holder that comes back is visibly a RE-STAMP
            // rather than something this clear missed.
            string dropped = DescribePursuits();
            _pursuitCount = 0;
            for (int i = 0; i < MaxTracked; i++) _pursuitOwners[i] = null;
            FlowTrace.Step("HudKit", $"pursuit cleared (posture -> peaceful); dropped: {dropped}");
        }

        // ── Postbattle / end-state (A4.6 — the decision node owns the screen) ─

        /// <summary>True while the shared end-state template (Victory/Defeat) is on
        /// screen — hostile(postbattle): the HUD kit stands down.</summary>
        public static bool EndStateVisible { get; private set; }

        /// <summary>Producer-only (EndStateView Show/teardown).</summary>
        public static void SetEndState(bool visible)
        {
            if (EndStateVisible == visible) return;
            EndStateVisible = visible;
            FlowTrace.Step("HudKit", "end-state " + (visible ? "SHOWN (posture -> hostile(postbattle))" : "dismissed"));
        }

        // ── Talk availability (§0 root-cause fix — see file header) ──────────

        /// <summary>True while a talkable NPC is in range of the hero (TalkHudBridge pushes).</summary>
        public static bool TalkAvailable { get; private set; }

        /// <summary>Raised when <see cref="TalkAvailable"/> changes value.</summary>
        public static event Action TalkChanged;

        /// <summary>Producer-only (Village TalkHudBridge).</summary>
        public static void SetTalkAvailable(bool available)
        {
            if (TalkAvailable == available) return;
            TalkAvailable = available;
            FlowTrace.Step("HudKit", "talk available -> " + available);
            TalkChanged?.Invoke();
        }

        // ── Raid capability (WO-835 — the SetTalkAvailable mirror pattern) ───
        // Village-side RaidCapabilityHudBridge publishes "the player CAN raid":
        // FeatureFlags.Raid AND barracks built. ⚠ WO-1008 (2026-08-16) DELETED the
        // old third clause ">=1 deployable troop": an empty army used to HIDE the
        // face, and the owner reported "I do not see a way to start a raid" with a
        // Barracks standing. Troop count now picks a DIM REASON on a VISIBLE face
        // (HudActionBarModel.RaidDimReason), alongside RaidEntryGate.ArmyStatus.Ready
        // (the WO-820 full-army DIM gate). The only hide reasons left are "no
        // barracks" and "flag off".

        /// <summary>True while the player can raid at all (Village-published).
        /// Defaults TRUE so headless / pre-publish scenes never hide the raid
        /// door (the RaidEntryGate.ArmyStatus never-false-block precedent).</summary>
        public static bool RaidCapable { get; private set; } = true;

        // ── WO-1357: WHY the raid door is shut ───────────────────────────────
        // Owner ruling 2026-09-03, verbatim: "Raid button under journey should fail
        // gracefully, it works great if there is a barracks but should show locked if
        // doesnt have one yet or its destroyed".
        //
        // ⛔ THIS ADDS A REASON, NOT A SECOND RULE. RaidCapable is still THE one
        // predicate and its boundary is UNCHANGED by WO-1357 — the bar face and the
        // Journey card now read the SAME bool, and the reason below only supplies the
        // words. Never write a second barracks check on a surface: two checks drift,
        // and the drift IS the defect this ticket closes (the Journey card carried
        // `Available = () => true` while the bar honoured RaidCapable).

        /// <summary>WHY <see cref="RaidCapable"/> is false. <see cref="RaidLockReason.None"/> when open.</summary>
        public enum RaidLockReason
        {
            /// <summary>Not locked — the raid door is open.</summary>
            None = 0,
            /// <summary>FeatureFlags.Raid is off in this build.</summary>
            FlagOff = 1,
            /// <summary>No Barracks has ever stood on this save — build the first one.</summary>
            NoBarracks = 2,
            /// <summary>A Barracks stood on this save and is gone (destroyed = lost, WO-753) — rebuild it.</summary>
            BarracksLost = 3,
        }

        /// <summary>Latest published lock reason (Village-published, WO-1357).</summary>
        public static RaidLockReason RaidLock { get; private set; } = RaidLockReason.None;

        /// <summary>
        /// The ONE owner of the player-facing lock copy, so the Journey card, any future
        /// surface and the regression oracle all read identical words.
        /// ASCII-only (mobile font-atlas law) and it always says WHAT TO DO, never just
        /// "Locked" — the owner is red/green colourblind, so the tell has to be words and
        /// the words have to be actionable. "No Barracks" and "lost your Barracks" are
        /// DIFFERENT player situations with different remedies; never collapse them.
        /// </summary>
        public static string RaidLockCopy(RaidLockReason reason)
        {
            switch (reason)
            {
                case RaidLockReason.FlagOff: return "Raids are turned off in this build";
                case RaidLockReason.NoBarracks: return "Build a Barracks to raid";
                case RaidLockReason.BarracksLost: return "Rebuild your lost Barracks to raid";
                default: return null;
            }
        }

        /// <summary>Raised when <see cref="RaidCapable"/> OR <see cref="RaidLock"/> changes.</summary>
        public static event Action RaidCapableChanged;

        // ── WO-1379: HEARTFIRE rides the SAME rail, beside RaidCapable ───────
        // Canon: docs/CREATIVE_CANON_ELARION_2026-09-04.md section 4. "Raid Orders" is
        // dead - the player is the ruler and nobody issues them orders. Heartfire is
        // the Heart's ability to sustain an expedition beyond its own reach.
        //
        // ⛔ HEARTFIRE IS A CHARGE, NOT A CURRENCY. These three numbers are a DISPLAY
        // MIRROR of DeNelle.Village.World.Camps.HeartfireService and nothing else: no
        // wallet row, no ResourceType member, no storage cap, no vendor. Nothing may
        // ever write a Heartfire value from a purchase, a reward or an economy path.
        //
        // WHY IT LIVES HERE rather than in a new rail: DeNelle.HUD may reference
        // DeNelle.Core ONLY (CLAUDE.md §5), the Village service is the producer, and a
        // Core static cannot go stale across a scene swap - the exact reasoning that put
        // TalkAvailable here after the one-shot reflection hook rotted. A second rail
        // would be a second thing to keep in step, which is the duplicated-state failure
        // §2/§5/§16 keep warning about.

        /// <summary>Heartfire charges lit right now (Village-published). Named "Lit", not
        /// "Charges", so it can never be confused with the TYPE
        /// DeNelle.Core.State.HeartfireCharges that owns the arithmetic. Defaults to the
        /// ceiling so a headless or pre-publish scene never renders an empty Heart and
        /// never implies a gate nobody has evaluated - the RaidCapable never-false-block
        /// precedent, same direction.</summary>
        public static int HeartfireLit { get; private set; } = HeartfireMaxDefault;

        /// <summary>The pool ceiling as the producer last published it.</summary>
        public static int HeartfireMax { get; private set; } = HeartfireMaxDefault;

        /// <summary>Seconds until the next charge lights; 0 while the pool is full.</summary>
        public static double HeartfireSecondsToNext { get; private set; }

        /// <summary>
        /// The pre-publish ceiling. ⛔ NOT a second authoring of the balance: the live
        /// number is DeNelle.Core.State.HeartfireCharges.MaxCharges (tunable
        /// raid.heartfireMaxCharges). This is only what the display shows in the frames
        /// before Village has published anything at all.
        /// </summary>
        public const int HeartfireMaxDefault = 3;

        /// <summary>Raised when any published Heartfire value changes.</summary>
        public static event Action HeartfireChanged;

        /// <summary>
        /// Producer-only (DeNelle.Village.World.Camps.HeartfireService). Fires on a change
        /// to the COUNTDOWN as well as the count, because the rekindle line under the
        /// flames repaints from it - an early-return on the count alone would strand a
        /// frozen timer on screen, which is the same defect SetRaidCapable's reason-only
        /// change was written to avoid.
        /// </summary>
        /// <summary>
        /// WO-1802 - HAS the Village producer published Heartfire at all, ever this process.
        /// FALSE until the first <see cref="SetHeartfire"/> call, whatever that call decides.
        ///
        /// ⛔ IT IS SET BEFORE THE CHANGE CHECK BELOW, DELIBERATELY, AND THAT IS THE WHOLE
        /// POINT. <see cref="HeartfireLit"/> defaults to the ceiling so no pre-publish frame
        /// implies a gate nobody evaluated - correct for a DISPLAY, and exactly backwards for
        /// an always-on affordance, which would then light up on the title screen and in every
        /// headless frame. A witness placed after the early-return would be worse than none: a
        /// producer publishing 3/3 with 0s to next matches the default, returns early, and the
        /// witness would stay FALSE forever on precisely the full-Heart save the badge is for.
        ///
        /// Read by <see cref="RaidDoorReadiness"/>. A consumer that wants the ordinary display
        /// polarity must keep reading the values directly and must NOT gate on this.
        /// </summary>
        public static bool HeartfirePublished { get; private set; }

        public static void SetHeartfire(int charges, int max, double secondsToNext)
        {
            // The producer has spoken. Recorded first - see HeartfirePublished's own note for
            // why an early-return below must never be able to swallow this.
            HeartfirePublished = true;
            if (max < 1) max = 1;
            if (charges < 0) charges = 0;
            if (charges > max) charges = max;
            if (double.IsNaN(secondsToNext) || secondsToNext < 0d) secondsToNext = 0d;

            bool countMoved = HeartfireLit != charges || HeartfireMax != max;
            // Whole-second granularity: the producer is polled, and repainting a text
            // countdown more often than it can visibly change is pure garbage.
            bool clockMoved = (long)HeartfireSecondsToNext != (long)secondsToNext;
            if (!countMoved && !clockMoved) return;

            HeartfireLit = charges;
            HeartfireMax = max;
            HeartfireSecondsToNext = secondsToNext;

            if (countMoved)
                FlowTrace.Step("HudKit", "heartfire -> " + charges + "/" + max +
                               " (next in " + secondsToNext.ToString("F0") + "s)");
            HeartfireChanged?.Invoke();
        }

        /// <summary>
        /// Producer-only (Village RaidCapabilityHudBridge). The event fires on a
        /// REASON-only change too (NoBarracks -> BarracksLost never flips the bool, but the
        /// card copy must repaint) — an early-return on the bool alone would strand the
        /// wrong sentence on screen.
        /// </summary>
        public static void SetRaidCapable(bool capable, RaidLockReason reason = RaidLockReason.None)
        {
            if (capable) reason = RaidLockReason.None;   // an open door has no reason to give
            if (RaidCapable == capable && RaidLock == reason) return;
            RaidCapable = capable;
            RaidLock = reason;
            FlowTrace.Step("HudKit", "raid capable -> " + capable +
                           (capable ? "" : " (locked: " + reason + " -> \"" + RaidLockCopy(reason) + "\")"));
            RaidCapableChanged?.Invoke();
        }

        // -- WO-1389: ARMY FILL rides the SAME rail, beside RaidCapable ---------
        // The Journey deck's Raids card shows "Army 3 / 10" as its subtitle until the army
        // is full (WO-1389 pressure point 6). DeNelle.HUD may reference Core ONLY, and the
        // slot arithmetic needs the Village TroopCatalog (a siege unit is 4 slots), so the
        // Village BuildTimerService.PublishArmyStatus - the ONE relay of ArmyReadiness.Compute
        // to the Core seams (RaidEntryGate), QueueChanged edges + the 1 s heartbeat - PUBLISHES
        // the two numbers here and the card just reads them - the SetRaidCapable mirror pattern.
        // NOT RaidCapabilityHudBridge: RaidsDiscoverabilityRegression D5 forbids ArmyReadiness
        // there by design (readiness decides DIM and REFUSAL, never visibility).
        // Defaults (0, 0) = "not published" - the card then keeps its ordinary purpose
        // line, so headless / pre-publish never shows a fake count.

        /// <summary>Roster slots in use (incl. wounded), Village-published. 0 until published.</summary>
        public static int ArmyFillUsed { get; private set; }
        /// <summary>Army slot cap, Village-published. 0 until published (= "unknown", never "full").</summary>
        public static int ArmyFillCap { get; private set; }
        /// <summary>Raised when either army-fill number changes.</summary>
        public static event Action ArmyFillChanged;

        /// <summary>Producer-only (Village BuildTimerService.PublishArmyStatus). Change-only publish.</summary>
        public static void SetArmyFill(int used, int cap)
        {
            if (used < 0) used = 0;
            if (cap < 0) cap = 0;
            if (ArmyFillUsed == used && ArmyFillCap == cap) return;
            ArmyFillUsed = used;
            ArmyFillCap = cap;
            FlowTrace.Step("HudKit", "army fill -> " + used + " / " + cap +
                           (cap > 0 && used >= cap ? " (full)" : ""));
            ArmyFillChanged?.Invoke();
        }

        // -- WO-1404: RAID CAMPS OPEN on the Journey deck -----------------------
        // Village owns the raid catalog and garrison arithmetic; HUD may reference Core only.
        // Publish the already-projected count here beside army fill rather than making the
        // Journey card reach across the assembly boundary or duplicate the camp predicate.
        public static int RaidOpenCampCount { get; private set; }
        public static event Action RaidOpenCampCountChanged;

        /// <summary>Producer-only (Village BuildTimerService.PublishArmyStatus). Change-only.</summary>
        public static void SetRaidOpenCampCount(int count)
        {
            if (count < 0) count = 0;
            if (RaidOpenCampCount == count) return;
            RaidOpenCampCount = count;
            FlowTrace.Step("HudKit", "raid camps open -> " + count);
            RaidOpenCampCountChanged?.Invoke();
        }

        // -- WO-1541: THE NEXT CAMP, NAMED. ONE PRODUCER, TWO READERS -----------
        // Owner ruling 2026-09-06 ("named camp + door", WO-1534 section D ruling 1): the raid
        // authority publishes ONE "next camp" fact and every surface READS it.
        //
        // ⛔ WHY THIS PROPERTY EXISTS AT ALL, and it is not "the Journey deck needed a name".
        // The deck reads RaidOpenCampCount above and keeps doing so - its copy is deliberately
        // NOT being redesigned (WO-1541 section 4). The defect was on the OTHER surface:
        // ManageScreenVM.BuildTroopArmySummary CONSTRUCTED ITS OWN RaidSelectionVM and walked the
        // raid catalog a SECOND time to decide which camp is next. Two independent derivations of
        // one fact is the duplicated-state class this repo keeps paying for
        // (PlayerDeckWorkspace.cs:719-723: "a second check would drift from the first, and the
        // drift is the actual defect"). RaidOpenCampCount could not absorb that walk because it
        // publishes only a COUNT and never a NAME - so the authority gains the fact instead of
        // the consumer keeping its own copy.
        //
        // ⚠ NOT AN ASSEMBLY FIX. ManageScreenVM lives in DeNelle.Village and may construct a
        // RaidSelectionVM legally; it broke the ONE-PRODUCER rule, not the boundary. Do not
        // "restore" the walk on the grounds that it compiled.
        //
        // Null / 0 is the "not published" sentinel, exactly as ArmyFillCap == 0 is - so a
        // headless or pre-publish frame shows no camp clause rather than a fabricated one.

        /// <summary>Display name of the next raidable camp. Null until published.</summary>
        public static string RaidNextCampName { get; private set; }
        /// <summary>Garrison the next camp fields. 0 until published (= "unknown", never "empty").</summary>
        public static int RaidNextCampGarrison { get; private set; }
        /// <summary>Raised when either next-camp field changes.</summary>
        public static event Action RaidNextCampChanged;

        // -- WO-1802: THE FIRST RAID'S REWARD, PROJECTED ONCE ------------------
        // Owner direction 2026-09-16, verbatim: "we need to create a desire to raid so we need to
        // announce early what the rewards are."
        //
        // The Journey deck's Raids card must be able to say what the first raid PAYS. It cannot
        // work the figure out for itself: DeNelle.HUD may reference DeNelle.Core ONLY (CLAUDE.md
        // section 5), and the projection is RaidScoring.EstimateSpoils - Village. So the Village
        // relay that already publishes the next camp publishes this too, on the same cadence, and
        // the card just READS it. Exactly the SetRaidNextCamp reasoning, whose own note explains
        // why the alternative (a consumer deriving the fact a second time) is the defect.
        //
        // (!) IT IS THE FORMATTED CLAUSE, NOT A NUMBER, AND THAT IS DELIBERATE. The "~" range-feel
        // grammar and the rounding are owned by RaidSelectionVM.FormatSpoils (owner ruling WO-1402:
        // "a range or estimate, never exact"). Publishing an int would force the card to re-format
        // it and the two surfaces would eventually print different-looking golds for one figure.
        //
        // (!) NULL IS THE "NOT PUBLISHED" SENTINEL, exactly as ArmyFillCap == 0 and
        // RaidNextCampName == null already are - so a headless frame, a capture and any
        // pre-publish frame show the plain badge with no reward clause rather than a fabricated
        // one. Fail-closed, the same polarity RaidDoorReadiness uses.

        /// <summary>
        /// The first raid's reward clause as the raid card would phrase it (e.g. "~2200 gold"),
        /// Village-published. NULL until published, and null means "say nothing".
        /// </summary>
        public static string RaidFirstSpoilsGold { get; private set; }

        /// <summary>Raised when <see cref="RaidFirstSpoilsGold"/> changes.</summary>
        public static event Action RaidFirstSpoilsChanged;

        /// <summary>Producer-only (Village BuildTimerService.PublishArmyStatus). Change-only.</summary>
        public static void SetRaidFirstSpoilsGold(string goldClause)
        {
            if (string.IsNullOrWhiteSpace(goldClause)) goldClause = null;
            if (string.Equals(RaidFirstSpoilsGold, goldClause, StringComparison.Ordinal)) return;
            RaidFirstSpoilsGold = goldClause;
            FlowTrace.Step("HudKit", "raid first-spoils clause -> " +
                (goldClause == null ? "(none published)" : "\"" + goldClause + "\""));
            RaidFirstSpoilsChanged?.Invoke();
        }

        /// <summary>Producer-only (Village BuildTimerService.PublishArmyStatus). Change-only.</summary>
        public static void SetRaidNextCamp(string campName, int garrison)
        {
            if (string.IsNullOrWhiteSpace(campName)) campName = null;
            if (garrison < 0) garrison = 0;
            if (string.Equals(RaidNextCampName, campName, StringComparison.Ordinal) &&
                RaidNextCampGarrison == garrison) return;
            RaidNextCampName = campName;
            RaidNextCampGarrison = garrison;
            FlowTrace.Step("HudKit", "raid next camp -> " +
                (campName == null ? "(none published)" : campName + " fields " + garrison));
            RaidNextCampChanged?.Invoke();
        }
    }
}
