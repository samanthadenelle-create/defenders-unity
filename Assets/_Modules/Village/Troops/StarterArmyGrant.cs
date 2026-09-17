// =============================================================================
// StarterArmyGrant - THE FIRST ARMY IS FREE (WO-1374, north-star map section 2).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Owner, verbatim (docs/PROGRAM_RAID_ECONOMY_2026-09-04.md section 2):
//   "A player starts with 200 gold but needs 1,650 to participate in the thing
//    you're trying to teach them. That's basically putting a nightclub behind a
//    velvet rope and handing the player twelve cents."
//   "On Barracks completion, grant 3 free Footmen (a starter raid squad)."
//   "The first raid must happen within MINUTES of unlocking Barracks, not hours."
//   (S) "One raid teaches the entire economy."
//
// -----------------------------------------------------------------------------
// WO-1803 (2026-09-16) - THREE BECAME TEN, AND THE SQUAD BECAME A COMPOSITION.
// -----------------------------------------------------------------------------
// Owner, verbatim: "Instead of giving them three troops, because that's shit, let's
// give them ten troops. Let's give them five footmen and five archers."
//
// So the grant is no longer "N x one troop id". It is an AUTHORED COMPOSITION whose
// TOTAL still rides raid.starterArmySize (default now 10) and whose SPLIT is derived
// from that total - even, remainder to Footmen (SplitComposition). Deriving the split
// rather than authoring two knobs means the owner retunes ONE number from the DB and
// can never land a "ten troops" squad that is secretly ten archers.
//
// Ten is not an arbitrary ten: ArmyStorage.DefaultMaxArmySize is 10 and Footman and
// Archer both cost 1 slot (troops.json, read at source 2026-09-16), so the free squad
// fills a fresh town's housing EXACTLY. ResolveCount clamps to that const, not to a
// literal, so the knob ceiling and the housing cap cannot drift apart.
// (!) KNOWN AND ACCEPTED CONSEQUENCE: a fresh save is 10/10 after the grant, so
// ArmyStorage.CanTrain refuses every troop until housing grows (the barracks-tier
// armyCapBonus perk). That is the CoC shape - the camp is full, upgrade it - and the
// post-raid FTUE beat that completes on troop.job_queued can still complete through
// BarracksService.UpgradeTroop, which housing does not gate.
//
// -----------------------------------------------------------------------------
// (S) WHY THIS IS SAFE TO BUILD WHILE WO-1374 IS BLOCKED.
// -----------------------------------------------------------------------------
// The work order is blocked on a fork the owner has not called: WO-1372 rules that
// troops cost TIME, the map prices three starters at 1,650 GOLD. This grant is
// CORRECT UNDER BOTH READINGS. Under the gold model it removes the 1,650-gold wall
// that stands between a new player and the loop the game is trying to teach; under
// the time model it is simply a fast start. Nothing here reads, spends or asserts
// a gold price, so neither ruling can make it wrong.
//
// -----------------------------------------------------------------------------
// WHY A POLLING BRIDGE AND NOT A HOOK ON THE BUILD JOB.
// -----------------------------------------------------------------------------
// "The player has a Barracks" is reachable by at least four different routes: the
// timed Builder job completing, the offline-fair sweep resolving it on launch, the
// strategic-placement migration granting a founding barracks, and a baked twin
// resurfacing after a WO-753 destruction. Hooking BuildTimerService.JobCompleted
// would cover the first and miss the rest, and a player whose barracks arrived by
// any other road would be handed the velvet rope after all.
//
// So this asks the SAME question every other raid surface asks -
// StructureSingleton.IsBuilt("barracks") - on the SAME 0.5 s edge-triggered
// cadence RaidCapabilityHudBridge uses, and grants on the rising edge. One
// predicate, one answer, no second definition of "has a Barracks" to drift.
//
// -----------------------------------------------------------------------------
// IDEMPOTENT ACROSS RELAUNCHES, WITHOUT A SAVE SCHEMA BUMP.
// -----------------------------------------------------------------------------
// The grant latches on GameState.MarkEverAcquired(GrantLedgerKey) - the monotonic
// v-whatever string ledger that already exists for "has this save ever held X",
// and whose Mark returns TRUE only on the first add. That is exactly an
// idempotency primitive, it is already persisted, migrated and round-tripped, and
// reusing it means this feature needs NO new field, NO version bump and NO
// migrator. It is the same "no new state" reasoning WO-1357 recorded when it
// discriminated NoBarracks from BarracksLost off EverBuiltStructureIds.
//
// The key is namespaced "grant." so it can never collide with a real item id -
// VillageInventory.HasEverAcquired reads the same list for item discovery, and a
// bare id there would make a phantom item look discovered.
//
// (!) A DESTROYED-AND-REBUILT BARRACKS DOES NOT RE-GRANT, and that is deliberate:
// the ledger is monotonic, so the squad is once per save. A second free squad
// would make demolishing a barracks a troop faucet.
//
// ASCII only. FlowTrace tag "Raid". Never stripped (CLAUDE.md section 12).
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// Grants the free starter squad the first time this save has a Barracks, and
    /// emits funnel step 1 at the same edge. Self-installing, DontDestroyOnLoad,
    /// idempotent forever.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StarterArmyGrant : MonoBehaviour
    {
        /// <summary>The StructureSingleton id of the raid building. Same literal every other
        /// raid surface uses - not a second name for the same thing.</summary>
        public const string BarracksItemId = "barracks";

        /// <summary>
        /// The MELEE half of the authored starter composition. TroopDef id, read at source in
        /// <c>Assets/Resources/Data/Canonical/troops.json:6</c> (2026-09-16) - slots 1,
        /// unlockBarracksTier 1, so it is fieldable the moment the Barracks exists.
        /// </summary>
        public const string StarterTroopId = "troop-footman";

        /// <summary>
        /// The RANGED half (WO-1803). TroopDef id, read at source in
        /// <c>Assets/Resources/Data/Canonical/troops.json:29</c> (2026-09-16) - slots 1,
        /// unlockBarracksTier 1, the other day-one unit. A starter squad of ten identical
        /// melee bodies teaches nothing about composition; five and five does.
        /// </summary>
        public const string ArcherTroopId = "troop-archer";

        /// <summary>
        /// How many. WO-1803, owner verbatim 2026-09-16: <i>"Instead of giving them three troops,
        /// because that's shit, let's give them ten troops. Let's give them five footmen and five
        /// archers."</i>
        /// <para>Ships as a tunable so the owner can re-size the starter squad by feel without a
        /// rebuild - <c>RemoteTunables.KeyRaidStarterArmySize</c>, default 10. The knob governs the
        /// TOTAL; the split between the two ids is derived (see
        /// <see cref="SplitComposition"/>), so retuning the number can never produce a squad of
        /// one unit type by accident.</para>
        /// <para>(!) TEN IS EXACTLY THE FRESH-SAVE HOUSING CAP
        /// (<see cref="ArmyStorage.DefaultMaxArmySize"/> = 10, read at source
        /// <c>Assets/_Modules/Core/State/ArmyStorage.cs:43</c>), which is why
        /// <see cref="ResolveCount"/> clamps to that const rather than to a literal 10. The two
        /// numbers are the same number and are not allowed to drift.</para>
        /// </summary>
        public const int DefaultStarterCount = 10;

        /// <summary>
        /// The idempotency key in GameState's monotonic acquired-ledger. Namespaced so it
        /// can never be mistaken for a real item id by the inventory's discovery reads.
        /// </summary>
        public const string GrantLedgerKey = "grant.starter-army";

        /// <summary>
        /// WO-1803 - the authored split. The knob governs the TOTAL; this derives the two halves,
        /// EVEN, with the REMAINDER TO FOOTMEN (the owner's "five footmen and five archers" at the
        /// shipping 10, and a melee-leaning squad at any odd number - a front line the archers can
        /// stand behind is the readable default). Pure, static and public so the oracle can pin
        /// 10 -> 5/5 and 3 -> 2/1 without a tunable override seam.
        /// </summary>
        public static void SplitComposition(int total, out int footmen, out int archers)
        {
            if (total <= 0) { footmen = 0; archers = 0; return; }
            footmen = (total + 1) / 2;   // remainder to melee
            archers = total - footmen;
        }

        /// <summary>
        /// The FTUE line, built from the ACTUAL composition granted rather than a hardcoded
        /// "3 Footmen": the squad size is a tunable and the split is derived from it, so a toast
        /// that says three while the player received ten - or that says Footmen while half the
        /// squad draws a bow - is the kind of small lie that costs trust in every other number the
        /// game prints. Ends with the direction the map wrote ("Journey -> Raids"), which is also
        /// the direction the Game Guide now gives - one destination, said the same way everywhere.
        /// ASCII only (mobile font-atlas law).
        /// </summary>
        public static string GrantToastFor(int footmen, int archers)
        {
            string body;
            if (footmen > 0 && archers > 0)
                body = footmen + " " + (footmen == 1 ? "Footman" : "Footmen") +
                       " and " + archers + " " + (archers == 1 ? "Archer" : "Archers");
            else if (footmen > 0)
                body = footmen + " " + (footmen == 1 ? "Footman" : "Footmen");
            else
                body = archers + " " + (archers == 1 ? "Archer" : "Archers");
            return "Your first squad is ready - " + body + ", free. Open Journey, then Raids.";
        }

        private const float PollInterval = 0.5f;

        private float _timer;      // 0 on spawn -> the first Update asks immediately
        private bool _resolved;    // once granted (or found already granted) we stop polling

        /// <summary>
        /// WO-1794 - what this seam reports for funnel step 1's `granted` flag: TRUE, always.
        ///
        /// <para>THE OWNER-FACING FACT the flag exists to carry (WO-1794 section 2, production
        /// rows 2026-09-16): raid_funnel_barracks_unlocked landed in the SAME SECOND as
        /// founding_path_selected for every id that reached founding, because the founding
        /// template hands the player a Barracks. The step is grant-fired for every founding
        /// player, and that is what the row must say.</para>
        ///
        /// <para>(!) A CONSTANT, AND DELIBERATELY NOT A DERIVED ONE. A "did we watch it appear"
        /// discriminator was written and then REJECTED, because it was wrong on exactly the rows
        /// this ticket is about: the founding-choice screen leaves a readable GameState with no
        /// Barracks on screen for the tens of seconds the player spends reading it, so every
        /// founding player would have been observed "without a barracks" first and reported as
        /// having EARNED one - the inverse of the measured truth. <c>StructureSingleton.IsBuilt</c>
        /// also answers from live scene objects and baked twins
        /// (<c>StructureSingleton.cs:131-145</c>), not from the save alone, so the observation is
        /// not even stable across a scene load. An honest constant with a named blind spot beats a
        /// derived flag that is confidently backwards.</para>
        ///
        /// <para>The blind spot, said out loud: a player who genuinely builds their first Barracks
        /// with no founding grant is also reported as granted. Step 1 latches per INSTALL, so in
        /// practice the emitted row is the founding one; when that changes, this const is the one
        /// place to revisit.</para>
        /// </summary>
        private const bool BarracksIsGrantFired = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("StarterArmyGrant");
            DontDestroyOnLoad(go);
            go.AddComponent<StarterArmyGrant>();
        }

        private void Update()
        {
            if (_resolved) { enabled = false; return; }

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = PollInterval;

            var gs = GameStateService.Instance;
            var st = gs != null ? gs.State : null;
            if (st == null) return;      // pre-boot / headless with no save: nothing to grant onto

            if (!StructureSingleton.IsBuilt(BarracksItemId)) return;

            // ── The rising edge. Everything below runs at most once per process. ──
            _resolved = true;
            enabled = false;

            // Funnel step 1 fires on the EDGE, not on the grant, so a returning player who
            // already had the squad still registers as having reached the step. RaidFunnel
            // latches it per install, so this is a no-op after the first time.
            //
            // WO-1794 - and the row now SAYS it is grant-fired, so "8 players unlocked a
            // barracks" can no longer be read as eight player actions. See
            // BarracksIsGrantFired for why this is a const and not a derived observation.
            Guard.Try("Funnel", "barracks unlocked",
                () => DeNelle.Core.Analytics.RaidFunnel.BarracksUnlocked("StarterArmyGrant", BarracksIsGrantFired));

            TryGrant(gs, st);
        }

        /// <summary>
        /// The grant itself. Public + state-taking so an oracle can drive it against a
        /// fixture GameState with no scene - and so the "second call grants nothing"
        /// property is provable rather than asserted in a comment.
        /// </summary>
        /// <returns>How many troops were granted (0 when this save already had its squad).</returns>
        public static int TryGrant(GameStateService service, GameState state)
        {
            if (state == null)
            {
                FlowTrace.Warn("Raid", "starter army: no GameState - nothing to grant onto.");
                return 0;
            }

            // MarkEverAcquired returns TRUE only when the key was NEWLY added, so this
            // single call is BOTH the check and the latch. Splitting it into a Has/Mark
            // pair would open a window in which two callers both read false.
            if (!state.MarkEverAcquired(GrantLedgerKey))
            {
                FlowTrace.Step("Raid",
                    "starter army: already granted on this save ('" + GrantLedgerKey +
                    "' is in the acquired ledger) - granting nothing. A rebuilt Barracks " +
                    "never re-issues the free squad.");
                return 0;
            }

            int want = ResolveCount();
            int wantFootmen, wantArchers;
            SplitComposition(want, out wantFootmen, out wantArchers);

            if (state.Army == null) state.Army = new ArmyStorage();

            // ── WO-1803 THE HOUSING CHECK, AND IT IS LOUD ON PURPOSE ──
            // GrantTrainedTroop -> ArmyStorage.GrantTrained is UNCONDITIONAL (the trained unit
            // must land even when the cap has since filled - CoC parity), so the squad is NEVER
            // silently truncated. What CAN happen is that the ten land in a town whose housing
            // holds fewer, which would leave the player over cap and unable to train at all with
            // nothing on screen to say why. That is a FAIL line naming the cap, not a shrug.
            int housingCap = 0;
            Guard.Try("Raid", "read army cap", () => { housingCap = state.Army.MaxArmySize; });
            if (housingCap > 0 && want > housingCap)
                FlowTrace.Fail("Raid",
                    "starter army: the knob asks for " + want + " troops but this save's army " +
                    "housing cap is " + housingCap + " (ArmyStorage.MaxArmySize = base " +
                    ArmyStorage.DefaultMaxArmySize + " + perk bonus). NOTHING IS TRUNCATED - the " +
                    "squad lands in full - but the roster starts OVER CAP and every train refuses " +
                    "until housing grows. Lower raid.starterArmySize or raise the housing.");

            int granted = 0;
            int grantedFootmen = 0;
            int grantedArchers = 0;

            // Footmen first, then Archers - the remainder rule already put the odd body in the
            // melee half, so the order here only decides roster listing, never the counts.
            // A SHORT melee half means the roster owner itself is broken, so the ranged half is
            // skipped: it would fail identically, and a second copy of the same FAIL line is noise
            // on top of the one that already named the cause.
            bool meleeOk = GrantRun(state, StarterTroopId, wantFootmen, ref granted, ref grantedFootmen);
            if (meleeOk)
                GrantRun(state, ArcherTroopId, wantArchers, ref granted, ref grantedArchers);

            FlowTrace.Step("Raid",
                "STARTER ARMY GRANTED: " + granted + "/" + want + " free on first Barracks - " +
                "composition " + grantedFootmen + "/" + wantFootmen + " x '" + StarterTroopId +
                "' + " + grantedArchers + "/" + wantArchers + " x '" + ArcherTroopId +
                "' (WO-1803, owner 2026-09-16 'five footmen and five archers'; WO-1374 map " +
                "section 2 - removes the 1,650-gold wall in front of the loop the game is trying " +
                "to teach). Housing cap " + housingCap + ", roster is now " +
                (state.Army != null && state.Army.Owned != null ? state.Army.Owned.Count : 0) + ".");

            if (service != null)
                Guard.Try("Raid", "persist starter army", () => service.Save());

            if (granted > 0)
                Guard.Try("Raid", "starter army toast",
                    () => ElarionUiKit.ShowToast(GrantToastFor(grantedFootmen, grantedArchers),
                                                 ElarionUiKit.ToastTone.Confirm));

            return granted;
        }

        /// <summary>
        /// Grants <paramref name="want"/> bodies of one troop id through the ONE roster owner
        /// (<c>BarracksProgression.GrantTrainedTroop</c>), so funnel step 2 ("army trained") fires
        /// from the same place a paid train fires it and the two cannot disagree. Returns FALSE the
        /// moment the roster owner reports an empty roster - the squad is SHORT and the trace says
        /// so, because the ledger key is already latched and this will never retry.
        /// </summary>
        private static bool GrantRun(GameState state, string troopId, int want,
                                     ref int granted, ref int grantedOfId)
        {
            for (int i = 0; i < want; i++)
            {
                // WO-1794 - granted: TRUE. This whole class IS the grant; nothing here is a
                // player action, and funnel step 2 must not read as one.
                int roster = BarracksProgression.GrantTrainedTroop(state, troopId, "starter-army", granted: true);
                if (roster <= 0)
                {
                    FlowTrace.Fail("Raid",
                        "starter army: GrantTrainedTroop('" + troopId + "') reported an empty " +
                        "roster on grant " + (i + 1) + " of " + want + " - the free squad is " +
                        "SHORT. The ledger key is already latched, so this will not retry.");
                    return false;
                }
                granted++;
                grantedOfId++;
            }
            return true;
        }

        /// <summary>
        /// The squad size, off the tunable rail, clamped to a sane band. 0 disables the
        /// grant outright (and the ledger still latches, so it stays disabled for that
        /// save rather than firing later if the knob moves - a grant that appears
        /// retroactively would be worse than one that never happened).
        /// </summary>
        public static int ResolveCount()
        {
            int raw = DeNelle.Core.Ops.RemoteTunables.Int(
                DeNelle.Core.Ops.RemoteTunables.KeyRaidStarterArmySize);
            // (!) THE CEILING IS THE FRESH-SAVE HOUSING CAP, READ OFF THE CONST (WO-1803).
            // Not a literal 10 that happens to agree with it today: a knob above the cap would
            // seat the whole free squad over housing and close training on a brand-new save, and
            // a hand-copied ceiling here is exactly the duplicated state CLAUDE.md 2/5/8/16 each
            // describe. One number, one home.
            int ceiling = ArmyStorage.DefaultMaxArmySize;
            int clamped = Mathf.Clamp(raw, 0, ceiling);
            if (clamped != raw)
                FlowTrace.Warn("Raid",
                    "starter army size knob resolved to " + raw + ", outside 0.." + ceiling +
                    " (the fresh-save army housing cap) - CLAMPED to " + clamped + ".");
            return clamped;
        }

        /// <summary>
        /// The shipping composition at the current knob value, for copy and for the oracle.
        /// </summary>
        public static void ResolveComposition(out int footmen, out int archers)
        {
            SplitComposition(ResolveCount(), out footmen, out archers);
        }
    }
}
