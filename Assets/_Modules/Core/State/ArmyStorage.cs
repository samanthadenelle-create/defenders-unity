// =============================================================================
// ArmyStorage — the persisted army manager (WO-453 Step 2 / 3).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.State
//
// The player's PERSISTED ARMY STATE — the roster of owned troops, the army cap,
// and the train / wound-recover / veterancy logic over them. Held by GameState
// (GameState.Army), the SAME ownership shape BaseLayout / ArenaDefense use: a
// plain saveable class on the state object, NOT a separate singleton/PlayerPrefs.
// Round-trips through SaveSchema (v22) — additive at the END so older saves load
// with an empty cap-10 army.
//
// LOSS MODEL = WOUNDED-RECOVERY, NO PERMADEATH (owner-decided): MarkWounded never
// removes a troop — it flags it + starts a recovery countdown; TickRecovery clears
// the flag when it elapses. The roster is stable; a downed troop just sits out.
//
// ASSEMBLY NOTE: lives in DeNelle.Core (NOT Village) because GameState.Army is a
// Core field and Core may not reference Village (CS0234 circular) — identical to
// PlacedDefenderData's rationale. The TroopDef catalog + the resource wallet that
// training needs both live in Village, so the catalog-dependent methods take small
// SEAM DELEGATES (a slot resolver + an affordability/spend callback) the Village
// caller wires to TroopCatalog / EconomyService. The pure army logic
// (cap / wounded / recovery / veterancy) needs no seam and runs Core-side.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.State
{
    /// <summary>
    /// The persisted army: the owned-troop roster + cap + train/wound/veterancy
    /// logic. Held by <see cref="GameState"/> (mirrors BaseLayout/ArenaDefense
    /// ownership). Catalog-dependent methods (slots, cost) take seam delegates so
    /// this Core type never references the Village TroopCatalog / EconomyService.
    /// </summary>
    [Serializable]
    public sealed class ArmyStorage
    {
        /// <summary>Default army population cap (expandable later via a barracks tier).</summary>
        public const int DefaultMaxArmySize = 10;

        /// <summary>The owned-troop roster (saved). Never null after construction.</summary>
        [JsonProperty("owned")] public List<PlayerTroop> Owned = new List<PlayerTroop>();

        /// <summary>Last army cap value we emitted a [Flow:Perk] line for (change-only logging).</summary>
        private static int s_lastLoggedCap = -1;

        /// <summary>
        /// Army population cap — total troop slots. DYNAMIC: base 10
        /// (<see cref="DefaultMaxArmySize"/>) + the SUMMED <c>armyCapBonus</c> from the
        /// active perk contract (the "Barracks: more troops" capstone). Null-safe — with no
        /// modifier it stays the base 10. Derived, so NOT serialized (the old stored
        /// "maxArmySize" key on legacy saves is harmlessly ignored on load).
        /// </summary>
        [JsonIgnore] public int MaxArmySize
        {
            get
            {
                int bonus = 0;
                var mods = ModifierService.Active;               // never null (Compute() returns fresh)
                if (mods != null && mods.ArmyCapBonus > 0) bonus = mods.ArmyCapBonus;
                int cap = DefaultMaxArmySize + bonus;
                if (cap != s_lastLoggedCap)
                {
                    s_lastLoggedCap = cap;
                    FlowTrace.Step("Perk", "army cap -> " + cap);
                }
                return cap;
            }
        }

        /// <summary>
        /// Monotonic id counter for minting stable <see cref="PlayerTroop.Id"/> values
        /// ("troop-{n}") — persisted so ids stay unique + deterministic across saves
        /// (no Date.now / random). Only ever increments.
        /// </summary>
        [JsonProperty("nextId")] public int NextId = 1;

        /// <summary>
        /// WO-779 — the persisted RECOVERY CLOCK anchor: the unix-ms wall-clock at which
        /// <see cref="AdvanceRecovery"/> last advanced the wounded countdowns. Mirrors
        /// <c>GameState.LastHarvestClaimMs</c> (the offline-accrual clock) so wounded troops
        /// heal FORWARD across app-closes off the SAME clock the Obsidian work queue reads
        /// (TimeSource.NowUnixMs) — reused, never forked. Additive at the END: an older save
        /// with no key loads 0, and the first <see cref="AdvanceRecovery"/> SEEDS it to now
        /// (crediting nothing that launch) so a pre-anchor save never banks a giant
        /// retroactive heal. Serialized straight to JSON with the rest of GameState.Army.
        /// </summary>
        [JsonProperty("lastRecoveryTickMs")] public double LastRecoveryTickMs;

        // ── v38 — Named army loadout presets (WO-934) ─────────────────────────
        /// <summary>
        /// Fixed bank of named composition presets the player can save/load and
        /// muster into the Train queue (WO-897 + WO-934). Always length
        /// <see cref="ArmyLoadoutBank.SlotCount"/> after <see cref="EnsureLoadouts"/>.
        /// Additive: absent on older saves → null → seeded empty slots on first access.
        /// </summary>
        [JsonProperty("loadouts")] public List<ArmyLoadoutSlot> Loadouts;

        /// <summary>Which loadout slot is active in the Armies UI (0..SlotCount-1).</summary>
        [JsonProperty("activeLoadout")] public int ActiveLoadoutIndex;

        /// <summary>
        /// Ensures <see cref="Loadouts"/> has exactly <see cref="ArmyLoadoutBank.SlotCount"/>
        /// slots with default names. Safe to call every open; never shrinks authored rows.
        /// </summary>
        public void EnsureLoadouts()
        {
            if (Loadouts == null) Loadouts = new List<ArmyLoadoutSlot>();
            while (Loadouts.Count < ArmyLoadoutBank.SlotCount)
            {
                int i = Loadouts.Count;
                Loadouts.Add(new ArmyLoadoutSlot
                {
                    Name = ArmyLoadoutBank.DefaultName(i),
                    Rows = new List<ArmyLoadoutRow>(),
                });
            }
            if (ActiveLoadoutIndex < 0) ActiveLoadoutIndex = 0;
            if (ActiveLoadoutIndex >= ArmyLoadoutBank.SlotCount)
                ActiveLoadoutIndex = ArmyLoadoutBank.SlotCount - 1;
        }

        /// <summary>Loadout slot at index, or null if out of range (after Ensure).</summary>
        public ArmyLoadoutSlot GetLoadout(int index)
        {
            EnsureLoadouts();
            if (index < 0 || index >= Loadouts.Count) return null;
            return Loadouts[index];
        }

        // ── v42 — THE RESERVE (WO-1811, owner ruling 2026-09-16) ──────────────
        //
        // Owner, verbatim: "If you change the you want 3 archers and 2 healers and rest
        // footman, you need to remove ones from active army, and either return them to
        // gold or to a staged ready troop". The "staged ready troop" is this list; it is
        // called the RESERVE in every player-facing string (the word "staged" is banned
        // from the army screen's copy - it is the vocabulary the owner could not place).
        //
        // ⛔ IT IS A SEPARATE LIST ON PURPOSE, AND THAT IS THE WHOLE DESIGN. A reserve
        // FLAG on PlayerTroop would have forced a change to SlotsUsed / GetDeployable /
        // CountOfDef - the cap arithmetic every train and raid gate reads (BarracksService
        // .cs:385, ArmyReadiness.cs) - so a bug in it would show up as a wrong army cap.
        // Keeping reserved troops OUT of Owned means the cap math is untouched, and
        // "not counted against the raid cap" is true by construction rather than by a
        // filter somebody has to remember to add.
        //
        // The two moves below are the ONLY writers, and both go through the existing
        // mutation seams: RemoveOwned (WO-1810, the first deletion path in the game) on
        // the way out, and a plain Owned.Add on the way back. Nothing here mints or
        // destroys a troop - a reserved troop keeps its Id, its veterancy and its wound.

        /// <summary>
        /// Trained troops the player has set aside. They are NOT part of the active army:
        /// they hold no cap slot, they never deploy, and no raid gate counts them.
        /// Additive on the nested Army JSON (v42) - absent on an older save reads as null
        /// and is seeded empty by <see cref="EnsureReserve"/>, which is exactly the prior
        /// behaviour (no reserve at all).
        /// </summary>
        [JsonProperty("reserve")] public List<PlayerTroop> Reserve;

        /// <summary>Null-safe accessor/seed for <see cref="Reserve"/>. Safe every read.</summary>
        public List<PlayerTroop> EnsureReserve()
        {
            if (Reserve == null) Reserve = new List<PlayerTroop>();
            return Reserve;
        }

        /// <summary>Reserved troops of one def id (0 when none).</summary>
        public int ReserveCountOf(string troopDefId)
        {
            if (Reserve == null || string.IsNullOrEmpty(troopDefId)) return 0;
            int n = 0;
            for (int i = 0; i < Reserve.Count; i++)
                if (Reserve[i] != null && Reserve[i].TroopDefId == troopDefId) n++;
            return n;
        }

        /// <summary>
        /// Moves ONE troop of <paramref name="troopDefId"/> out of the active army and into the
        /// reserve, freeing its cap slot. Prefers a HEALTHY troop so a move never silently parks
        /// the one that was about to recover. Returns false (and changes nothing) when the player
        /// owns none. The caller persists.
        /// </summary>
        public bool MoveToReserve(string troopDefId)
        {
            if (Owned == null || string.IsNullOrEmpty(troopDefId)) return false;

            // ⛔ A WOUNDED TROOP IS NEVER MOVED, and that is a data rule, not a preference:
            // AdvanceRecovery iterates Owned ONLY (see TickRecovery below), so a wounded body
            // parked in the reserve would stop healing forever and nothing on screen would say
            // why. Only a HEALTHY troop with a real id can be set aside; a player who wants the
            // wounded one gone dismisses it instead.
            PlayerTroop pick = null;
            foreach (var t in Owned)
            {
                if (t == null || t.Wounded || t.TroopDefId != troopDefId) continue;
                if (string.IsNullOrEmpty(t.Id)) continue;   // RemoveOwned keys on Id - a blank id
                pick = t;                                   // would leave it in BOTH lists
                break;
            }
            if (pick == null) return false;

            EnsureReserve().Add(pick);
            RemoveOwned(new[] { pick.Id });     // the WO-1810 seam, not a second deletion path
            return true;
        }

        /// <summary>
        /// Moves ONE reserved troop of <paramref name="troopDefId"/> back into the active army.
        /// Returns false when there is none reserved; the CAP CHECK IS THE CALLER'S - this method
        /// is the move, not the rule (the army screen asks <see cref="SlotsRemaining"/> first).
        /// </summary>
        public bool RecallFromReserve(string troopDefId)
        {
            if (Reserve == null || string.IsNullOrEmpty(troopDefId)) return false;
            for (int i = 0; i < Reserve.Count; i++)
            {
                var t = Reserve[i];
                if (t == null || t.TroopDefId != troopDefId) continue;
                Reserve.RemoveAt(i);
                if (Owned == null) Owned = new List<PlayerTroop>();
                Owned.Add(t);
                return true;
            }
            return false;
        }

        // ── Capacity ─────────────────────────────────────────────────────────

        /// <summary>
        /// Total army slots occupied by the current roster. Each troop's slot cost is
        /// resolved via <paramref name="slotOf"/> (TroopDef.Slots, Village-side) — a
        /// missing/unknown def counts as 1 slot (a safe floor). Footman/Archer = 1.
        /// </summary>
        public int SlotsUsed(Func<string, int> slotOf)
        {
            if (Owned == null) return 0;
            int total = 0;
            foreach (var t in Owned)
            {
                if (t == null) continue;
                int s = slotOf != null ? slotOf(t.TroopDefId) : 1;
                total += s > 0 ? s : 1;
            }
            return total;
        }

        /// <summary>Free army slots = <see cref="MaxArmySize"/> − used. Never negative.</summary>
        public int SlotsRemaining(Func<string, int> slotOf)
        {
            int rem = MaxArmySize - SlotsUsed(slotOf);
            return rem < 0 ? 0 : rem;
        }

        /// <summary>
        /// How many roster members (incl. wounded) carry <paramref name="troopDefId"/>.
        /// Used for per-type ownership caps (WO-933 siege maxOwned) — wounded still count.
        /// </summary>
        public int CountOfDef(string troopDefId)
        {
            if (string.IsNullOrEmpty(troopDefId) || Owned == null) return 0;
            int n = 0;
            foreach (var t in Owned)
                if (t != null && string.Equals(t.TroopDefId, troopDefId, StringComparison.Ordinal))
                    n++;
            return n;
        }

        /// <summary>
        /// True when <paramref name="troopId"/> is a known def (slot &gt; 0) AND the
        /// remaining army slots cover that def's slot cost. Pure capacity check — does
        /// NOT consider resource cost (that is <see cref="TrainNow"/>'s affordability seam).
        /// Optional <paramref name="maxOwnedOf"/>: when it returns &gt; 0 for the def, also
        /// refuses once <see cref="CountOfDef"/> is at the cap (WO-933). In-flight train
        /// jobs are NOT visible here — BarracksService.EnqueueTraining counts those.
        /// </summary>
        public bool CanTrain(string troopId, Func<string, int> slotOf, Func<string, int> maxOwnedOf = null)
        {
            if (string.IsNullOrEmpty(troopId)) return false;
            int cost = slotOf != null ? slotOf(troopId) : 0;
            if (cost <= 0) return false;                 // unknown def → not trainable
            if (SlotsRemaining(slotOf) < cost) return false;
            if (maxOwnedOf != null)
            {
                int max = maxOwnedOf(troopId);
                if (max > 0 && CountOfDef(troopId) >= max) return false;
            }
            return true;
        }

        /// <summary>
        /// STEP-stub training (owner's "training stub — resource cost only"): if there
        /// is army room AND the player can afford the troop's resource cost, mints a
        /// fresh <see cref="PlayerTroop"/> (rank 0, not wounded), appends it, and
        /// returns it. Returns null when capacity OR affordability fails — no mutation
        /// on failure. The real timed queue + offline accrual is a LATER increment.
        /// </summary>
        /// <param name="troopId">The TroopDef id to train (e.g. "troop-footman").</param>
        /// <param name="slotOf">Resolves a def id → its army slot cost (TroopDef.Slots).</param>
        /// <param name="tryAfford">
        /// Affordability+spend seam (Village wires this to EconomyService.TrySpend of the
        /// def's CostWood/Iron/Food): returns true AND has deducted the cost, or false +
        /// no spend. Pass a callback that always returns true for a free/dev train.
        /// </param>
        public PlayerTroop TrainNow(string troopId, Func<string, int> slotOf, Func<string, bool> tryAfford)
        {
            if (!CanTrain(troopId, slotOf)) return null;
            // Spend AFTER the capacity check, but only commit the troop if the spend took.
            if (tryAfford != null && !tryAfford(troopId)) return null;
            var troop = new PlayerTroop(MintId(), troopId);
            if (Owned == null) Owned = new List<PlayerTroop>();
            Owned.Add(troop);
            return troop;
        }

        /// <summary>
        /// WO-771.9 — grants a freshly-TRAINED troop into the roster UNCONDITIONALLY (no
        /// capacity/afford check). This is the completion effect of a timed
        /// <see cref="DeNelle.Core.Jobs.JobKind.TrainTroop"/> job: the resource cost was charged
        /// + the army-cap checked at ENQUEUE time, so on completion the paid troop must land even
        /// if the cap has since filled (CoC parity — the barracks holds the trained unit). Mints a
        /// stable id, appends a rank-0 healthy <see cref="PlayerTroop"/>, and returns it. Null id →
        /// no-op (returns null).
        /// </summary>
        public PlayerTroop GrantTrained(string troopDefId)
        {
            if (string.IsNullOrEmpty(troopDefId)) return null;
            if (Owned == null) Owned = new List<PlayerTroop>();
            var troop = new PlayerTroop(MintId(), troopDefId);
            Owned.Add(troop);
            return troop;
        }

        // ── Deployment ───────────────────────────────────────────────────────

        /// <summary>The deployable troops — healthy (not wounded) members of the roster.</summary>
        public IEnumerable<PlayerTroop> GetDeployable()
        {
            if (Owned == null) yield break;
            foreach (var t in Owned)
                if (t != null && t.IsDeployable) yield return t;
        }

        // ── Loss / recovery ───────────────────────────────────────────────────
        //
        // ⚠ CORRECTED 2026-09-16 (WO-1810). This heading read "wounded-recovery model
        // — NEVER deletes", and the whole family below is still written that way,
        // because until this ticket that WAS the model: a troop killed on a raid came
        // home wounded and healed for free on a timer. The owner ruled that out —
        // "any troop killed is dead" — so the RAID path now REMOVES the fallen
        // (see RemoveOwned below and DeNelle.Village.RaidCasualtyPolicy).
        //
        // MarkWounded / TickRecovery / AdvanceRecovery are KEPT and are not dead code:
        // they are the backstop the raid reconcile still runs after the removal (a
        // deployed body that somehow escaped removal is wounded rather than silently
        // healthy), and they are pinned by ArmyRecoveryRegression. What changed is that
        // a raid no longer PRODUCES wounded troops.

        /// <summary>
        /// Marks <paramref name="t"/> wounded and starts its recovery countdown. Clamps the
        /// recovery seconds to &gt;= 0; a non-positive value recovers it immediately.
        ///
        /// <para>⚠ NO LONGER THE RAID-LOSS PATH (WO-1810). A troop killed on a raid is now
        /// REMOVED from the roster (<see cref="RemoveOwned"/>); this is the backstop for a
        /// deployed body that was not removed, and the seam any future non-lethal downed
        /// state would use.</para>
        /// </summary>
        public void MarkWounded(PlayerTroop t, float recoverySeconds)
        {
            if (t == null) return;
            if (recoverySeconds <= 0f)
            {
                t.Wounded = false;
                t.RecoveryRemaining = 0f;
                return;
            }
            t.Wounded = true;
            t.RecoveryRemaining = recoverySeconds;
        }

        /// <summary>
        /// The raid-EXIT reconcile (WO-453 Step 4): given the ids of every troop that was
        /// DEPLOYED into the raid and the ids of the SURVIVORS (still alive at retreat), marks
        /// every deployed-but-not-survivor troop wounded (recovery countdown) and leaves the
        /// survivors untouched.
        ///
        /// <para>⚠ WO-1810 — THIS IS NO LONGER THE WHOLE RAID SETTLEMENT, AND IT NO LONGER
        /// NORMALLY DOES ANYTHING. <c>RaidDeployController.ReconcileRaidEnd</c> now removes the
        /// killed (and, on a fail/retreat, the policy's share of the survivors) through
        /// <see cref="RemoveOwned"/> BEFORE calling this, so by the time this runs there is
        /// typically no deployed-but-not-survivor troop left in <see cref="Owned"/> to wound. It
        /// is kept as the BACKSTOP: a deployed body that escaped removal is wounded rather than
        /// silently healthy. Its own behaviour is unchanged and stays pinned by
        /// ArmyRecoveryRegression.</para>
        ///
        /// <para>Both id sets are null-safe (a null set reads as empty); a non-positive
        /// <paramref name="recoverySeconds"/> recovers a downed troop immediately (per
        /// <see cref="MarkWounded"/>). Lookup is by <see cref="PlayerTroop.Id"/>.</para>
        /// </summary>
        public void ReconcileAfterRaid(IEnumerable<string> deployedIds, IEnumerable<string> survivorIds, float recoverySeconds)
        {
            if (Owned == null || deployedIds == null) return;

            var survivors = new HashSet<string>(StringComparer.Ordinal);
            if (survivorIds != null)
                foreach (var s in survivorIds)
                    if (!string.IsNullOrEmpty(s)) survivors.Add(s);

            var deployed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in deployedIds)
                if (!string.IsNullOrEmpty(d)) deployed.Add(d);

            // Index the roster by id once so a big army doesn't go O(n*m).
            foreach (var t in Owned)
            {
                if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                if (!deployed.Contains(t.Id)) continue;        // not sent on this raid
                if (survivors.Contains(t.Id)) continue;        // came home — untouched
                MarkWounded(t, recoverySeconds);               // deployed + fell → wounded
            }
        }

        /// <summary>
        /// WO-1810 — <b>THE FIRST TROOP DELETION PATH IN THE GAME.</b> Removes every roster entry
        /// whose <see cref="PlayerTroop.Id"/> appears in <paramref name="ids"/> and returns how
        /// many were actually removed.
        ///
        /// <para>OWNER RULING 2026-09-16, verbatim: <i>"loss should lose troops and then rebuild"</i>
        /// / <i>"any troop killed is dead so 60% of whats left"</i>. Until this method existed the
        /// only raid-loss outcome was <see cref="MarkWounded"/> — a free 5-45 minute timer — so a
        /// lost raid cost the player nothing and the army came back whole. The player now rebuilds
        /// by TRAINING at the barracks, which is the cost the loop was missing.</para>
        ///
        /// <para>⛔ TREAT THIS AS THE SEAM, NOT AS ONE OF SEVERAL. <c>grep '\.Owned\.Remove'</c> over
        /// <c>Assets/</c> returned NOTHING before this ticket, so nothing in the tree was written to
        /// survive a troop disappearing. Two things were checked at source rather than assumed:
        /// the WO-934 loadout bank stores <b>def-id + count rows</b> (<c>ArmyLoadoutBank.cs:52-68</c>),
        /// never instance ids, so a removal cannot orphan a preset; and
        /// <c>TroopController.OwnedTroopId</c> is per-raid runtime state on a body that is destroyed
        /// with the scene. Any FUTURE system that keys on a troop id must be pruned HERE.</para>
        ///
        /// <para>NO SAVE-SCHEMA BUMP: <see cref="Owned"/> is a serialized <c>List&lt;PlayerTroop&gt;</c>
        /// (<c>SaveSchema.CurrentVersion</c> v22 note: "owned troops + cap + wounded/recovery/
        /// veterancy"). Removing entries changes no field and no shape, so an older or newer save
        /// reads identically — there is nothing for a migrator to do. The CALLER persists (every raid
        /// exit already calls <c>GameStateService.Save()</c>).</para>
        ///
        /// <para>Null-safe and idempotent: a null/empty id set, a null roster and an id that is
        /// already gone are all no-ops, so a duplicated raid-exit call cannot remove twice.</para>
        /// </summary>
        public int RemoveOwned(IEnumerable<string> ids)
        {
            if (Owned == null || ids == null) return 0;

            var doomed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) doomed.Add(id);
            if (doomed.Count == 0) return 0;

            int before = Owned.Count;
            Owned.RemoveAll(t => t != null && !string.IsNullOrEmpty(t.Id) && doomed.Contains(t.Id));
            return before - Owned.Count;
        }

        /// <summary>
        /// Advances recovery on every wounded troop by <paramref name="dt"/> seconds;
        /// a troop whose countdown reaches 0 recovers (Wounded cleared). Call once per
        /// tick (real-time or simulated). No-op when nothing is wounded. Returns the
        /// number of troops that recovered on THIS call.
        ///
        /// PURE STEP (dt-based, no clock) so it is headlessly unit-testable with a
        /// simulated delta — the wall-clock resolver is <see cref="AdvanceRecovery"/>.
        /// NEVER adds/removes a troop and NEVER touches <see cref="PlayerTroop.Id"/> or
        /// <see cref="PlayerTroop.VeterancyRank"/> — a recovered troop is the SAME
        /// instance with Wounded cleared, so the roster / OwnedTroopId / veterancy
        /// accounting is untouched (no double-resurrect) and it is idempotent once healed
        /// (a healed troop is skipped by the <c>!t.Wounded</c> guard).
        /// </summary>
        public int TickRecovery(float dt)
        {
            if (Owned == null || dt <= 0f) return 0;
            int recovered = 0;
            foreach (var t in Owned)
            {
                if (t == null || !t.Wounded) continue;
                t.RecoveryRemaining -= dt;
                if (t.RecoveryRemaining <= 0f)
                {
                    t.RecoveryRemaining = 0f;
                    t.Wounded = false;
                    recovered++;
                }
            }
            return recovered;
        }

        /// <summary>
        /// WO-779 — the wall-clock RECOVERY RESOLVER: the live + offline advance hook the
        /// zero-caller <see cref="TickRecovery"/> was missing. Computes the elapsed seconds
        /// since the last advance from the persisted <see cref="LastRecoveryTickMs"/> anchor
        /// (fed <paramref name="nowMs"/> = TimeSource.NowUnixMs by the Village tick — the
        /// SAME clock the Obsidian work queue uses, reused not forked) and ticks every
        /// wounded troop by that delta. Call on LOAD (credits the offline gap) AND on a
        /// lightweight live cadence (~1/sec). Returns the number of troops that recovered.
        ///
        /// Null/empty-army safe (<see cref="TickRecovery"/> no-ops). Monotonic + retroactive-
        /// safe:
        ///   • fresh anchor (&lt;= 0) → SEED to now, tick NOTHING (a pre-anchor save can't
        ///     bank a giant first-load heal — mirrors OfflineHarvestService's fresh-clock seed);
        ///   • a backwards/zero clock delta → advance the anchor but tick nothing (never
        ///     re-heal on a rewound clock).
        /// The anchor is part of GameState.Army, so every Save() that persists a wound (e.g.
        /// RaidDeployController.DoRetreat) persists the fresh anchor ATOMICALLY with it — the
        /// away-gap on reload is measured from the wound's own save point (no over-heal).
        /// </summary>
        public int AdvanceRecovery(double nowMs)
        {
            // Fresh anchor → seed to now, credit nothing this call.
            if (LastRecoveryTickMs <= 0.0)
            {
                LastRecoveryTickMs = nowMs;
                return 0;
            }

            double elapsedSec = (nowMs - LastRecoveryTickMs) / 1000.0;
            LastRecoveryTickMs = nowMs;          // always advance the anchor (even on a 0/backwards delta)
            if (elapsedSec <= 0.0) return 0;      // no time passed / clock ran backwards → no heal

            int recovered = TickRecovery((float)elapsedSec);
            if (recovered > 0)
                FlowTrace.Step("Army", $"recovery advanced {elapsedSec:0}s -> {recovered} troop(s) healed.");
            return recovered;
        }

        // ── Veterancy ─────────────────────────────────────────────────────────

        /// <summary>
        /// Grants one veterancy rank to <paramref name="t"/> (called on a survived
        /// 3-star raid), capped at <see cref="PlayerTroop.MaxVeterancyRank"/>. Idempotent
        /// at the cap.
        /// </summary>
        public void AddVeterancy(PlayerTroop t)
        {
            if (t == null) return;
            if (t.VeterancyRank < PlayerTroop.MaxVeterancyRank)
                t.VeterancyRank++;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        /// <summary>Mints the next stable troop id ("troop-{n}") and advances the counter.</summary>
        private string MintId()
        {
            if (NextId < 1) NextId = 1;
            string id = "troop-" + NextId.ToString();
            NextId++;
            return id;
        }
    }
}
