// =============================================================================
// RemoteTunables - PROD-022, the database-backed knobs the Pi crash loop is
// bisected with. THE STATE AND THE PARSE. Transport lives in
// RemoteTunablesService.cs, exactly the way MaintenanceCatalog / MaintenanceService
// are split, and for exactly the same reason: this half stays headlessly drivable
// by a regression oracle with no network and no PlayMode.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Ops
//
// -----------------------------------------------------------------------------
// WHY THIS EXISTS AT ALL. Owner ruling 2026-09-02, verbatim:
//     "make the testing as robust as possible with as many solutions as
//      possible... all we really have to do is just flip a flag and possibly
//      redeploy"
// A WebGL rebuild costs about thirty minutes. PROD-022 is a P0 crash loop that
// reproduces on the owner's iPhone inside Pi Browser and NOWHERE ELSE - desktop
// Chrome ran the identical build for 62 minutes. So every candidate mitigation
// ships in ONE build, each behind its OWN independent knob, all defaulting to
// today's behaviour. The bisect is then flag flips against the database, at
// seconds per hypothesis instead of half an hour.
//
// -----------------------------------------------------------------------------
// ⛔ THE INVARIANT THAT OUTRANKS EVERYTHING ELSE IN THIS FILE:
//     NO ROW, NO NETWORK, NO PARSE, NO SERVER  =>  TODAY'S BEHAVIOUR, EXACTLY.
// -----------------------------------------------------------------------------
// Every default in Registry below is the value that is hardcoded in the shipping
// code TODAY. A player who cannot reach the API, whose fetch times out, who gets
// a 404, or who receives malformed JSON resolves EVERY knob to that default. The
// remote read is an OVERRIDE and never a dependency. This is the same fail-to-the-
// safe-ground-state shape as MaintenanceCatalog, and it is asserted rather than
// asserted-in-a-comment: RemoteTunablesService never blocks, never awaits at a
// call site, and every parse goes through Guard.
//
// ⚠ ONE HONEST EXCEPTION, STATED RATHER THAN HIDDEN (WO-1327): the two vfx.* knobs
// are BUG FIXES, so their defaults are the CORRECTED values, not the broken ones.
// An empty table gives you this build's fixed VFX collision and light budget, not
// the art pack's perfectly-elastic fireballs and 25 concurrent point lights. The
// invariant still holds in the form that matters - NO ROW => EXACTLY WHAT THIS
// BUILD HARDCODES - and the previous behaviour is one flip away
// (vfx.particleBouncePct=100, vfx.maxParticleLights=25) if the owner, who owns
// every VFX call, judges the new feel wrong.
//
// -----------------------------------------------------------------------------
// PRECEDENCE, and it composes with FeatureFlags rather than fighting it:
//     LOCAL PlayerPrefs "ff.tun.<key>"   (most specific - a human at the device)
//         beats REMOTE payload           (the owner at the database)
//             beats DEFAULT              (what this build hardcodes = today)
// FeatureFlags.Get already resolves PlayerPrefs-over-default for the ff.* family;
// this file inserts the remote layer BETWEEN those two and leaves ff.* untouched.
// The prefix is "ff.tun." and NOT plain "ff." on purpose - a tunable key and a
// FeatureFlags name must never be able to collide in one PlayerPrefs namespace.
//
// -----------------------------------------------------------------------------
// THE OWNER-FACING LIST IS docs/PROD022_TUNABLE_FLAGS.md. The Registry array
// below is the MACHINE-READABLE source of truth (key, kind, default, what ON
// does, which hypothesis it tests) and the doc is written from it. If you change
// one, change the other in the same commit - CLAUDE.md section 15.
//
// -----------------------------------------------------------------------------
// NO SILENT ANYTHING (CLAUDE.md section 12). Every resolve is traced ONCE per key
// with its value AND its provenance, and the whole configuration is printed on one
// line at service boot and again on every accepted payload - so a felt-test
// capture always says which configuration produced it. A session whose config
// cannot be reconstructed afterwards is a wasted session.
//
// ASCII only. FlowTrace tag "Tunables". Never strip it.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Diagnostics;
using Newtonsoft.Json;

namespace DeNelle.Core.Ops
{
    /// <summary>What a knob's value means on the wire.</summary>
    public enum TunableKind
    {
        /// <summary>0 / 1. Read with <see cref="RemoteTunables.Bool"/>.</summary>
        Bool = 0,
        /// <summary>A whole number. Read with <see cref="RemoteTunables.Int"/>.</summary>
        Int = 1,
    }

    /// <summary>
    /// One knob's contract. Immutable, authored in <see cref="RemoteTunables.Registry"/>,
    /// and printed verbatim into the trace so a reader never has to open this file to
    /// know what a flag does.
    /// </summary>
    public sealed class TunableSpec
    {
        /// <summary>Wire key. Lower camel, dotted, ASCII. Matches the client_tunables PK.</summary>
        public readonly string Key;

        /// <summary>Bool or Int.</summary>
        public readonly TunableKind Kind;

        /// <summary>THE SHIPPING VALUE. Bools are 0/1. This is today's behaviour, always.</summary>
        public readonly int Default;

        /// <summary>What turning it on (or raising it) actually does, in one sentence.</summary>
        public readonly string WhatOnDoes;

        /// <summary>Which PROD-022 hypothesis flipping it tests.</summary>
        public readonly string Hypothesis;

        public TunableSpec(string key, TunableKind kind, int def, string whatOnDoes, string hypothesis)
        {
            Key = key;
            Kind = kind;
            Default = def;
            WhatOnDoes = whatOnDoes;
            Hypothesis = hypothesis;
        }
    }

    /// <summary>
    /// Static, transport-free knob table. Always answers, never throws, and answers
    /// the shipping default for every question it cannot answer from data.
    /// </summary>
    public static class RemoteTunables
    {
        /// <summary>FlowTrace system tag for the whole tunables lane.</summary>
        public const string Sys = "Tunables";

        /// <summary>Payload schema version this build was written against.</summary>
        public const int PayloadVersion = 1;

        /// <summary>PlayerPrefs prefix for a LOCAL override. Deliberately not plain "ff.".</summary>
        public const string LocalPrefix = "ff.tun.";

        // Provenance literals. Also the values the oracle asserts, and the words that
        // appear in every trace line - "default" vs "remote" must never need inferring.
        public const string ProvenanceDefault = "default";
        public const string ProvenanceRemote = "remote";
        public const string ProvenanceLocal = "local-playerprefs";
        public const string ProvenanceCache = "remote-cached";

        // =====================================================================
        //  THE KEYS. One const per knob so no call site ever types a string.
        // =====================================================================

        /// <summary>Bool. Pi Browser runs the full desktop warm pass instead of on-demand.</summary>
        public const string KeyPiEagerStructureWarm = "pi.eagerStructureWarm";

        /// <summary>Bool. Pi awaits Addressables init + harvests keys before the first on-demand load.</summary>
        public const string KeyPiAwaitInitBeforeFirstLoad = "pi.awaitInitBeforeFirstLoad";

        /// <summary>Bool. Pi issues NO remote structure-art requests at all. The big hammer.</summary>
        public const string KeyPiDisableRemoteStructureArt = "pi.disableRemoteStructureArt";

        /// <summary>Int. Ceiling on concurrent residency requests. 0 = today (no explicit cap).</summary>
        public const string KeyAssetsMaxConcurrentRequests = "assets.maxConcurrentRequests";

        /// <summary>Int. The Pi Addressables per-request timeout, seconds.</summary>
        public const string KeyPiRequestTimeoutSeconds = "pi.requestTimeoutSeconds";

        /// <summary>Int. Async fetch attempts allowed per address per launch.</summary>
        public const string KeyAssetsMaxRequestAttempts = "assets.maxRequestAttempts";

        /// <summary>Int. VisualFactory resolve-miss escalate-then-throttle cap.</summary>
        public const string KeyVisualsMissLogCap = "visuals.missLogCap";

        /// <summary>Int. Verbosity of the [Flow:StructureAssets] / [Flow:VisualFactory] families.</summary>
        public const string KeyTraceAssetVerbosity = "trace.assetVerbosity";

        /// <summary>
        /// Int, PERCENT. How much of the damage a "drainshot" ability actually deals comes
        /// back to the caster as healing. 100 = today (heal == damage dealt).
        /// <para>
        /// ⭐ THIS ONE IS NOT A PROD-022 KNOB - it is a BALANCE knob, and it is here because
        /// the owner ruled that balance must move without a rebuild too (2026-09-02, verbatim:
        /// "be smart, dont make it need a code change, make it tweakable from a db call"). The
        /// rail is reused end to end rather than a second configuration mechanism being built
        /// - see the "no second bespoke mechanism" note in docs/PROD022_TUNABLE_FLAGS.md.
        /// </para>
        /// <para>
        /// The domain is EVERY drainshot ability, because HeroAbilities.HealFromDrain is the
        /// single owner of the drain heal - mage.siphon (the WO-1306 cost-1 base grant),
        /// mage.drain (the mage's stock E) and ranger.healing-shot all pass through it. It is
        /// therefore named combat.* and NOT mage.*: a mage-only knob would need a per-ability
        /// branch inside that one owner, which is the second mechanism this rule forbids.
        /// </para>
        /// </summary>
        /// <remarks>
        /// ⛔ 60, NOT 100, AND THAT IS A DELIBERATE DEPARTURE FROM THIS FILE'S OWN RULE.
        /// Do NOT "correct" it back. Owner ruling 2026-09-02, verbatim: <i>"keep drain at
        /// 60% for now"</i>, with the design intent she gave in the same breath: <i>"drain
        /// should help stave off not run the show"</i>.
        /// <para>
        /// Every other Default in the Registry below is the value the shipping code
        /// hardcoded, so that an empty table reproduces today's behaviour byte for byte.
        /// This one is a RULED BALANCE VALUE, so an empty table gives the drain the OWNER
        /// chose rather than the 100 percent WO-1306 shipped. Her ruling outranks the
        /// convention - the convention exists to stop a default DRIFTING silently, and a
        /// value she stated out loud is the opposite of drift. The invariant that still
        /// binds unchanged: no row, no network, no parse => EXACTLY WHAT THIS BUILD
        /// HARDCODES, with the remote read an override and never a dependency. The WO-861
        /// identity "heal == damage dealt" is now reachable by setting the row to 100.
        /// (Same shape as the two vfx.* knobs' exception, recorded in this file's header.)
        /// </para>
        /// </remarks>
        public const int DrainReturnPctDefault = 60;

        /// <summary>Int percent. Share of damage DEALT that a drainshot returns as healing.</summary>
        public const string KeyCombatDrainReturnPct = "combat.drainReturnPct";

        // ---------------------------------------------------------------------
        //  WO-1330 - THE OVER-TIME BALANCE RAIL. Three knobs, not six.
        //
        //  The ticket named three levers - tick MAGNITUDE, tick INTERVAL and
        //  DURATION - and required them tunable rather than hardcoded. They are
        //  registered ONCE and SHARED by every over-time effect of EITHER sign,
        //  because "how often does an over-time effect pulse" is the same concept
        //  whether the pulse hurts or mends. Per-ability duplicates were explicitly
        //  rejected by the work order ("Prefer ONE shared knob over per-ability
        //  duplicates where it is genuinely the same concept") and would also have
        //  had to be re-registered for every future DoT anyone authors.
        //
        //  ⭐ ALL THREE DEFAULTS ARE TODAY'S BEHAVIOUR, and this is checkable rather
        //  than asserted: 1000 ms is exactly the "const float tick = 1f" that both
        //  HeroAbilities.BurnDoT and HeroAbilities.PoisonDoT hardcoded before this
        //  work, and 100 percent is identity on the other two. An empty table, a
        //  404, a malformed row and an offline player therefore all reproduce the
        //  shipped mage.poison and knight.emberbrand-throw numbers exactly.
        //
        //  The consumer is DeNelle.Core.Combat.OverTimeTuning, which owns the
        //  clamps and is the ONLY reader - see that file for why each clamp exists.
        // ---------------------------------------------------------------------

        /// <summary>Milliseconds between over-time pulses. 1000 = today.</summary>
        public const int OverTimeTickMsDefault = 1000;

        /// <summary>Percent scale on every over-time pulse's magnitude. 100 = today.</summary>
        public const int OverTimeMagnitudePctDefault = 100;

        /// <summary>Percent scale on every over-time effect's duration. 100 = today.</summary>
        public const int OverTimeDurationPctDefault = 100;

        /// <summary>Int, MILLISECONDS between over-time pulses (both signs).</summary>
        public const string KeyCombatOverTimeTickMs = "combat.overTimeTickMs";

        /// <summary>Int, PERCENT scale on over-time pulse magnitude (both signs).</summary>
        public const string KeyCombatOverTimeMagnitudePct = "combat.overTimeMagnitudePct";

        /// <summary>Int, PERCENT scale on over-time effect duration (both signs).</summary>
        public const string KeyCombatOverTimeDurationPct = "combat.overTimeDurationPct";

        /// <summary>
        /// Int, PERCENT. Restitution allowed on a WORLD-COLLIDING particle inside any VFX host
        /// the pooled spawner checks out. 0 = THIS BUILD'S DEFAULT: a particle that hits scene
        /// geometry stops there and terminates. 100 = leave the art pack's authored collision
        /// completely alone.
        /// <para>
        /// ⭐ NOT a PROD-022 knob and NOT a balance knob - a FEEL/PERF knob (WO-1327). It exists
        /// because the offending numbers live in a PREFAB inside a GITIGNORED art pack
        /// (<c>Assets/Spells Pack/</c>), so a hand-edit to that prefab is unreviewable,
        /// uncommittable, and erased by the next re-import. The clamp therefore lives at the ONE
        /// spawn owner (<c>VFXManager</c>) and rides this rail, exactly as the 2026-09-02 standing
        /// rule requires of a feel value.
        /// </para>
        /// <para>
        /// The clamp only ever TIGHTENS: bounce is lowered toward the cap, dampen and lifetime-loss
        /// are raised toward its complement. It can never make an effect bouncier than its author
        /// made it, so setting 100 is a true "do nothing".
        /// </para>
        /// </summary>
        public const int VfxParticleBouncePctDefault = 0;

        /// <summary>Int percent. Restitution ceiling for world-colliding VFX particles.</summary>
        public const string KeyVfxParticleBouncePct = "vfx.particleBouncePct";

        /// <summary>
        /// Int COUNT. Ceiling on the total concurrent real-time point lights ONE spawned VFX host
        /// may drive through its ParticleSystem LightsModules, summed across every emitter on the
        /// host. 4 = THIS BUILD'S DEFAULT. 0 turns particle lights off outright; a number at or
        /// above a host's authored total leaves that host untouched.
        /// <para>
        /// ⭐ Also WO-1327, and for the same reason: <c>Spell_Fire_9</c> drives 20 lights from its
        /// <c>Fireballs</c> emitter plus 5 from its <c>Explosion</c> sub-emitter - TWENTY-FIVE
        /// real-time point lights per cast, on a phone - and the dial is baked into a gitignored
        /// prefab. The budget is spent EVENLY across the host's enabled modules and each module's
        /// <c>ratio</c> is scaled down with it, so the lights stay spread across the effect instead
        /// of all sticking to the first few particles.
        /// </para>
        /// <para>⛔ This never deletes a light PROTOTYPE. The prototype is what the module clones
        /// from; removing it breaks the effect instead of tuning it.</para>
        /// </summary>
        public const int VfxMaxParticleLightsDefault = 4;

        /// <summary>Int count. Concurrent particle-driven real-time lights allowed per VFX host.</summary>
        public const string KeyVfxMaxParticleLights = "vfx.maxParticleLights";

        // ---------------------------------------------------------------------
        //  WO-1374 - THE RAID REWARD TABLE + THE PERFORMANCE LADDER.
        //  Spec: docs/PROGRAM_RAID_ECONOMY_2026-09-04.md sections 1 and 12.7.
        //
        //  (S) EVERY NUMBER IN THAT DOCUMENT IS A TUNABLE. Section 12.7 says so in
        //  capitals, and it is the standing 2026-09-02 rule applied to the one
        //  curve the owner is going to set BY FEEL: how much a raid pays. A wrong
        //  reward number is a thirty-minute rebuild away from being right, or it
        //  is forty seconds away. These knobs are the difference.
        //
        //  (!) THESE DEFAULTS ARE NOT "TODAY'S BEHAVIOUR", AND THAT IS DELIBERATE
        //  AND RULED. Today a raid pays ZERO wood and ZERO iron - that is the
        //  defect WO-1374 exists to close, and the map states the target values
        //  outright (1,800 wood / 1,100 iron at a perfect 3-star, 100% Camp I
        //  run). So the shipping default is the OWNER'S NUMBER, exactly as
        //  combat.drainReturnPct ships at her 60 rather than the code's old 100.
        //  The invariant that still binds unchanged: no row, no network, no parse
        //  => EXACTLY WHAT THIS BUILD HARDCODES, with the remote read an override
        //  and never a dependency. Setting both bases to 0 restores the old
        //  food-and-crystals-only payout precisely.
        //
        //  (!) GOLD IS HERE NOW. THE FORK IS CLOSED (commit 281902df0): troops COST
        //  GOLD, ALSO take time, and a SECOND gold spend hires mercenaries to skip
        //  the clock. So the map's gold column - sized at 125-140% of a per-camp
        //  DESIGNED army-replacement cost - is the live spec, and the missing arrow
        //  the map names explicitly ("you currently have Gold -> troops but not
        //  troops -> raids -> gold") is the thing these four knobs build. Any
        //  comment left in this repo still calling that fork open is STALE.
        //
        //  (!) GOLD IS FOUR KNOBS, NOT ONE, AND IT IS NOT MULTIPLIED BY THE CAMP'S
        //  rewardMultiplier. The map gives a DESIGNED gold target per camp
        //  (2,200 / 3,100 / 4,500 / 6,500) rather than a single base times a
        //  difficulty multiplier - x1.5 on 2,200 is 3,300, not her 3,100, and x2.2
        //  is 4,840, not her 4,500. Encoding the escalation in the knob VALUES is
        //  the only arrangement in which every published number is payable exactly;
        //  applying the multiplier on top would make all four defaults wrong.
        //
        //  The consumer is DeNelle.Village.RaidLootTunables, which owns the clamps
        //  and is the ONLY reader.
        // ---------------------------------------------------------------------

        /// <summary>Wood paid by a perfect (3-star, 100% razed) Camp I raid. Map section 1.</summary>
        public const int RaidLootWoodBaseDefault = 1800;

        /// <summary>Iron paid by a perfect (3-star, 100% razed) Camp I raid. Map section 1.</summary>
        public const int RaidLootIronBaseDefault = 1100;

        /// <summary>Percent of the base paid by a FAILED attack. Map's ladder says 15-20.</summary>
        public const int RaidLootFailPctDefault = 18;

        /// <summary>Percent of the base paid at 1 star.</summary>
        public const int RaidLootOneStarPctDefault = 50;

        /// <summary>Percent of the base paid at 2 stars.</summary>
        public const int RaidLootTwoStarPctDefault = 75;

        /// <summary>Percent of the base paid at 3 stars.</summary>
        public const int RaidLootThreeStarPctDefault = 100;

        /// <summary>Percent of the base paid at 3 stars AND 100% destruction.</summary>
        public const int RaidLootPerfectPctDefault = 110;

        /// <summary>
        /// GOLD paid by a perfect run on a Camp I-tier base (map section 1: 2,200, sized at
        /// 125-140% of that camp's designed 1,650-gold army replacement cost). ALSO the
        /// fallback for any raid config id the per-camp table below does not name.
        /// </summary>
        public const int RaidLootCoinsBaseCamp1Default = 2200;

        /// <summary>GOLD at a perfect run on Camp II (map: 3,100 against a 2,300 army).</summary>
        public const int RaidLootCoinsBaseCamp2Default = 3100;

        /// <summary>GOLD at a perfect run on Camp III (map: 4,500 against a 3,300 army).</summary>
        public const int RaidLootCoinsBaseCamp3Default = 4500;

        /// <summary>GOLD at a perfect run on the Iron Bastion (map: 6,500 against a 4,800 army).</summary>
        public const int RaidLootCoinsBaseBastionDefault = 6500;

        /// <summary>
        /// CRYSTALS at 100% destruction, before the per-star bonus. 20 + 3x2 = 26 at a
        /// perfect clear, inside the map's 20-30 band and DOWN from the old 25 + 3x10 = 55.
        /// The map: <i>"Crystals are timer compression. If raids dump huge amounts of
        /// crystals, you accidentally accelerate the already-too-short progression curve."</i>
        /// This is the one number in the reward table that DECREASES.
        /// </summary>
        public const int RaidLootCrystalsBaseDefault = 20;

        /// <summary>Extra crystals per earned star. 2, down from 10.</summary>
        public const int RaidLootCrystalsPerStarDefault = 2;

        /// <summary>
        /// WO-1461. PERCENT of the ordinary loot a REPEAT clear pays - one inside the camp's
        /// still-running cooldown cycle. 60 is the owner's ruling of 2026-09-06 20:33, verbatim:
        /// <i>"100% first clear after cooldown, 60% repeat clear during the same cycle, then
        /// reset to 100% when the camp's cooldown expires."</i>
        ///
        /// <para>⛔ A FOURTH DELIBERATE DEPARTURE from "the default is today's behaviour",
        /// alongside the two vfx.* fixes, the ruled drain rate and the two raid bases. Today
        /// this build pays 25 - <c>RaidClaimService.RepeatClearLootMultiplier</c> was a compiled
        /// <c>const 0.25f</c> - and that is the defect WO-1461 exists to close. The invariant
        /// that still binds: no row, no network, no parse => exactly what this build hardcodes,
        /// and 25 is one flag flip away.</para>
        /// </summary>
        public const int RaidLootRepeatClearPctDefault = 60;

        /// <summary>
        /// WO-1461. Per-resource ceiling on the RAID CACHE - the temporary hold that catches
        /// raid loot the town bank has no room for, so a win above the cap is never burned.
        /// Owner ruling 2026-09-06 20:33: <i>"Never destroy raid loot because storage is full.
        /// Put overflow into a temporary Raid Cache with a modest cap."</i>
        ///
        /// <para>⚠ 1800 IS NOT HER NUMBER - she said "a modest cap" and named none. It is
        /// exactly one perfect Camp I wood haul (<see cref="RaidLootWoodBaseDefault"/>), so the
        /// cache holds at most ONE raid's worth of any one resource and cannot quietly become a
        /// second, larger bank. Registered as a knob so her number lands without a rebuild.</para>
        /// </summary>
        public const int RaidCacheCapPerResourceDefault = 1800;

        /// <summary>Int. Wood a perfect Camp I raid pays before the difficulty multiplier.</summary>
        public const string KeyRaidLootWoodBase = "raid.lootWoodBase";

        /// <summary>Int. Iron a perfect Camp I raid pays before the difficulty multiplier.</summary>
        public const string KeyRaidLootIronBase = "raid.lootIronBase";

        /// <summary>Int PERCENT of base paid by a failed attack.</summary>
        public const string KeyRaidLootFailPct = "raid.lootFailPct";

        /// <summary>Int PERCENT of base paid at 1 star.</summary>
        public const string KeyRaidLootOneStarPct = "raid.lootOneStarPct";

        /// <summary>Int PERCENT of base paid at 2 stars.</summary>
        public const string KeyRaidLootTwoStarPct = "raid.lootTwoStarPct";

        /// <summary>Int PERCENT of base paid at 3 stars.</summary>
        public const string KeyRaidLootThreeStarPct = "raid.lootThreeStarPct";

        /// <summary>Int PERCENT of base paid at 3 stars with 100% destruction.</summary>
        public const string KeyRaidLootPerfectPct = "raid.lootPerfectPct";

        /// <summary>Int GOLD a perfect Camp I raid pays. Also the fallback for an unknown camp.</summary>
        public const string KeyRaidLootCoinsBaseCamp1 = "raid.lootCoinsBaseCamp1";

        /// <summary>Int GOLD a perfect Camp II raid pays.</summary>
        public const string KeyRaidLootCoinsBaseCamp2 = "raid.lootCoinsBaseCamp2";

        /// <summary>Int GOLD a perfect Camp III raid pays.</summary>
        public const string KeyRaidLootCoinsBaseCamp3 = "raid.lootCoinsBaseCamp3";

        /// <summary>Int GOLD a perfect Iron Bastion raid pays.</summary>
        public const string KeyRaidLootCoinsBaseBastion = "raid.lootCoinsBaseBastion";

        /// <summary>Int CRYSTALS at 100% destruction, before the per-star bonus.</summary>
        public const string KeyRaidLootCrystalsBase = "raid.lootCrystalsBase";

        /// <summary>Int extra CRYSTALS per earned star.</summary>
        public const string KeyRaidLootCrystalsPerStar = "raid.lootCrystalsPerStar";

        /// <summary>Int PERCENT of ordinary loot a REPEAT clear pays inside the camp's cooldown
        /// cycle. Consumer: <c>RaidClaimService.RepeatClearPct</c>, clamped 0..100 there.</summary>
        public const string KeyRaidLootRepeatClearPct = "raid.lootRepeatClearPct";

        /// <summary>Int per-resource ceiling on the RAID CACHE. Consumer:
        /// <c>RaidClaimService.CacheCapPerResource</c>, clamped 0..1000000 there.</summary>
        public const string KeyRaidCacheCapPerResource = "raid.cacheCapPerResource";

        // ---------------------------------------------------------------------
        //  WO-1763 - PER-CAMP RAID DIFFICULTY. Eight knobs: a PERCENT on each
        //  camp's authored difficultyMultiplier, and a REPLACEMENT for each
        //  camp's authored levelOffset.
        //
        //  WHY THEY EXIST: the owner nearly 3-starred the Iron Bastion with a
        //  level-4 hero, and the Bastion is a field-for-field clone of Camp III
        //  on every difficulty axis in scene-configs.json - read the live values
        //  off that file, never off a number written here. Before this ticket the
        //  ONLY way to make a camp harder was to edit that JSON and ship a build,
        //  which is a thirty-minute rebuild per opinion about feel. Her standing
        //  ruling since 2026-09-02 is that a balance number is a row.
        //
        //  THE DEFAULTS ARE TODAY'S BEHAVIOUR, EXACTLY AND DELIBERATELY. 100 is
        //  identity on the multiplier (the consumer short-circuits at 100 and
        //  returns the authored float untouched, so there is not even a float
        //  round-trip), and the offset keys ship at a NEGATIVE SENTINEL meaning
        //  "use the value scene-configs.json authored". No row, no network, no
        //  parse => the raid that shipped, byte for byte.
        //
        //  THE OFFSET IS REPLACE, NOT ADD, and that is a ruling not a preference:
        //  the owner's 2026-09-16 seed (levelOffsetBastion = 5) was given against
        //  REPLACE semantics, and under add-semantics the same 5 would mean an
        //  effective offset of 8. A levelOffsetDelta<Camp> shape (default 0, no
        //  sentinel needed) was considered and REJECTED for exactly that reason -
        //  a ruling that can be read two ways is the failure to avoid.
        //
        //  The consumer is DeNelle.Village.RaidDifficultyTunables, which owns the
        //  clamps and is the ONLY reader - the RaidLootTunables contract. Camp
        //  suffixes reuse the loot keys' own suffixes so the two families read as
        //  one table.
        //
        //  Bare int consts, because tools/gen-tunable-manifest.mjs resolves ONLY
        //  that shape (see RaidHeartfireMaxChargesDefault above). The generator's
        //  int-const regex accepts a leading sign, and so does the doc-parity
        //  oracle's int.TryParse - which is what makes a negative sentinel safe
        //  to carry through all six registration places.
        // ---------------------------------------------------------------------

        /// <summary>
        /// IDENTITY on the multiplier: 100 percent of whatever scene-configs.json authored.
        /// <para>⚠ THE EIGHT PER-CAMP DEFAULTS BELOW REPEAT THE LITERAL RATHER THAN ALIASING
        /// THESE TWO, AND THAT IS FORCED, NOT SLOPPY. tools/gen-tunable-manifest.mjs resolves a
        /// registry default with a regex that matches a BARE int const and nothing else - a
        /// default written as another const's name silently FAILS TO PARSE and the knob vanishes
        /// from the Command Center with no error (the failure mode recorded at
        /// <see cref="RaidHeartfireMaxChargesDefault"/>, measured 2026-09-04). The two names here
        /// exist for the CONSUMER, which needs one sentinel however many camps there are;
        /// RemoteTunablesDefaultsRegression pins all eight defaults against its own literals and
        /// against the docs table, so a drift between the two shapes goes red rather than
        /// shipping.</para>
        /// </summary>
        public const int RaidDifficultyMultPctIdentity = 100;

        /// <summary>
        /// The "use the authored levelOffset" SENTINEL. Negative on purpose: a real offset is
        /// meaningfully -5..+20, so no legitimate value can collide with it, and a sentinel that
        /// could be typed by accident would silently ignore an operator's row. Same bare-literal
        /// caveat as <see cref="RaidDifficultyMultPctIdentity"/>.
        /// </summary>
        public const int RaidLevelOffsetUseJsonSentinel = -999;

        /// <summary>PERCENT on Camp I's authored difficultyMultiplier. 100 = the JSON value unchanged.</summary>
        public const int RaidDifficultyMultPctCamp1Default = 100;

        /// <summary>PERCENT on Camp II's authored difficultyMultiplier. 100 = the JSON value unchanged.</summary>
        public const int RaidDifficultyMultPctCamp2Default = 100;

        /// <summary>PERCENT on Camp III's authored difficultyMultiplier. 100 = the JSON value unchanged.</summary>
        public const int RaidDifficultyMultPctCamp3Default = 100;

        /// <summary>PERCENT on the Iron Bastion's authored difficultyMultiplier. 100 = unchanged.</summary>
        public const int RaidDifficultyMultPctBastionDefault = 100;

        /// <summary>REPLACEMENT for Camp I's authored levelOffset. -999 = use the authored value.</summary>
        public const int RaidLevelOffsetCamp1Default = -999;

        /// <summary>REPLACEMENT for Camp II's authored levelOffset. -999 = use the authored value.</summary>
        public const int RaidLevelOffsetCamp2Default = -999;

        /// <summary>REPLACEMENT for Camp III's authored levelOffset. -999 = use the authored value.</summary>
        public const int RaidLevelOffsetCamp3Default = -999;

        /// <summary>REPLACEMENT for the Iron Bastion's authored levelOffset. -999 = use the authored value.</summary>
        public const int RaidLevelOffsetBastionDefault = -999;

        /// <summary>Int PERCENT on Camp I's authored difficultyMultiplier. Consumer:
        /// <c>RaidDifficultyTunables</c>, clamped 25..400 there.</summary>
        public const string KeyRaidDifficultyMultPctCamp1 = "raid.difficultyMultPctCamp1";

        /// <summary>Int PERCENT on Camp II's authored difficultyMultiplier. Clamped 25..400 at the consumer.</summary>
        public const string KeyRaidDifficultyMultPctCamp2 = "raid.difficultyMultPctCamp2";

        /// <summary>Int PERCENT on Camp III's authored difficultyMultiplier. Clamped 25..400 at the consumer.</summary>
        public const string KeyRaidDifficultyMultPctCamp3 = "raid.difficultyMultPctCamp3";

        /// <summary>Int PERCENT on the Iron Bastion's authored difficultyMultiplier. Clamped 25..400 at the consumer.</summary>
        public const string KeyRaidDifficultyMultPctBastion = "raid.difficultyMultPctBastion";

        /// <summary>Int REPLACEMENT for Camp I's authored levelOffset. -999 keeps the authored value;
        /// any other value is clamped -5..20 at the consumer.</summary>
        public const string KeyRaidLevelOffsetCamp1 = "raid.levelOffsetCamp1";

        /// <summary>Int REPLACEMENT for Camp II's authored levelOffset. -999 keeps the authored value.</summary>
        public const string KeyRaidLevelOffsetCamp2 = "raid.levelOffsetCamp2";

        /// <summary>Int REPLACEMENT for Camp III's authored levelOffset. -999 keeps the authored value.</summary>
        public const string KeyRaidLevelOffsetCamp3 = "raid.levelOffsetCamp3";

        /// <summary>Int REPLACEMENT for the Iron Bastion's authored levelOffset. -999 keeps the authored value.</summary>
        public const string KeyRaidLevelOffsetBastion = "raid.levelOffsetBastion";

        // ---------------------------------------------------------------------
        //  WO-1773 - TOWN WAVE DIFFICULTY + THE TOWN-REGEN COMBAT GATE.
        //
        //  The felt report, relayed by the owner from an external tester on
        //  2026-09-16: "at the level he is nothing can damage him he can just
        //  stand there", and her direction: "enemies need to really start
        //  scaling with wave i thought, or massive swarms at all sides".
        //
        //  WO-1773 measured TWO independent dead steps off that tester's
        //  wave-176 recording, and fixing either one alone closes nothing:
        //    A. The town safe-zone regen restores a FRACTION OF MaxHp PER
        //       SECOND with NO combat check of any kind. Because it scales with
        //       MaxHp while incoming damage is capped by the wave curve's clamp,
        //       the gap WIDENS with every piece of gear - it can never be
        //       outgrown. Even four Cave Trolls at the engine's hard melee
        //       ceiling lose to it on the ticket's plausible build.
        //    B. Nothing gets harder after wave 60. Strength clamps at the
        //       curve's wave-20 keyframe, the roster at its MaxCount, the
        //       endless multiplier at its countCap, and concurrency never varied
        //       with the wave at all. Wave 176 IS wave 60.
        //
        //  EVERY DEFAULT BELOW IS EXACT IDENTITY, so this whole family ships
        //  INERT and an empty client_tunables table is today's town bit for bit.
        //  The two consumers short-circuit at identity rather than round-tripping
        //  a float. Same bare-int-literal caveat as the raid block above: a
        //  default written as another const's NAME silently fails
        //  tools/gen-tunable-manifest.mjs's regex and the knob vanishes from the
        //  Command Center with no error.
        // ---------------------------------------------------------------------

        /// <summary>
        /// IDENTITY on the in-wave regen share: 100 percent of whatever SafeZoneRecovery's own
        /// per-second fraction is. For the CONSUMER, which needs one name for the short-circuit.
        /// </summary>
        public const int TownRegenDuringWavePctIdentity = 100;

        /// <summary>
        /// IDENTITY on every town-wave COUNT / CONCURRENCY percent. For the CONSUMER, which needs
        /// one name across four knobs.
        /// </summary>
        public const int WaveCountPctIdentity = 100;

        /// <summary>SECONDS after the hero last LOST HP during which town regen stays off. 0 = today (no gate).</summary>
        public const int TownRegenSuppressSecondsAfterHitDefault = 0;

        /// <summary>PERCENT of the normal town regen rate while a wave is ACTIVE. 100 = today.</summary>
        public const int TownRegenPctDuringWaveDefault = 100;

        /// <summary>Enemy HP multiplier growth per wave PAST the curve's clamp band, in hundredths. 0 = today.</summary>
        public const int WaveHpGrowthPctPerWaveDefault = 0;

        /// <summary>Enemy contact-damage multiplier growth per wave PAST the clamp band, in hundredths. 0 = today.</summary>
        public const int WaveDmgGrowthPctPerWaveDefault = 0;

        /// <summary>PERCENT on WaveCompositionBuilder's authored roster ceiling. 100 = today.</summary>
        public const int WaveMaxCountPctDefault = 100;

        /// <summary>PERCENT on waves.json's authored endless countCap. 100 = today.</summary>
        public const int WaveCountCapPctDefault = 100;

        /// <summary>PERCENT on the scene's serialized on-screen concurrency cap. 100 = today.</summary>
        public const int WaveMaxSimultaneousPctDefault = 100;

        /// <summary>Int SECONDS of town-regen suppression after the hero loses HP. Consumer:
        /// <c>TownRegenTunables</c>, clamped 0..120 there.</summary>
        public const string KeyTownRegenSuppressSecondsAfterHit = "town.regenSuppressSecondsAfterHit";

        /// <summary>Int PERCENT of the town regen rate that runs while a wave is ACTIVE. Clamped 0..100 at the consumer.</summary>
        public const string KeyTownRegenPctDuringWave = "town.regenPctDuringWave";

        /// <summary>Int HUNDREDTHS of enemy HP multiplier added per wave past the curve's clamp band.
        /// Consumer: <c>WaveDifficultyTunables</c>, clamped 0..100 there.</summary>
        public const string KeyWaveHpGrowthPctPerWave = "wave.hpGrowthPctPerWave";

        /// <summary>Int HUNDREDTHS of enemy contact-damage multiplier added per wave past the clamp band. Clamped 0..100.</summary>
        public const string KeyWaveDmgGrowthPctPerWave = "wave.dmgGrowthPctPerWave";

        /// <summary>Int PERCENT on the authored roster ceiling. Clamped 25..1000 at the consumer.</summary>
        public const string KeyWaveMaxCountPct = "wave.maxCountPct";

        /// <summary>Int PERCENT on the authored endless count cap. Clamped 25..1000 at the consumer.</summary>
        public const string KeyWaveCountCapPct = "wave.countCapPct";

        /// <summary>Int PERCENT on the on-screen concurrency cap. Clamped 25..400 at the consumer -
        /// tighter than the roster knobs because this one is a PHONE FRAME BUDGET, not a difficulty dial.</summary>
        public const string KeyWaveMaxSimultaneousPct = "wave.maxSimultaneousPct";

        /// <summary>
        /// Int COUNT of free troops granted the first time a save has a Barracks
        /// (map section 2, "the first army is free"). WO-1803, owner verbatim 2026-09-16:
        /// <i>"Instead of giving them three troops, because that's shit, let's give them ten
        /// troops. Let's give them five footmen and five archers."</i>
        /// <para>This knob is the TOTAL. StarterArmyGrant.SplitComposition derives the halves -
        /// even, remainder to Footmen - so 10 is 5 Footmen + 5 Archers and no value of this knob
        /// can produce a one-unit-type squad. 0 disables the grant. Clamped at the consumer to
        /// 0..ArmyStorage.DefaultMaxArmySize (10, the fresh-save army housing cap), so the free
        /// squad can never be seated above the housing it arrives into.</para>
        /// </summary>
        public const int RaidStarterArmySizeDefault = 10;

        /// <summary>Int count. TOTAL free troops granted on the first Barracks (split 50/50
        /// Footmen/Archers by the consumer, remainder to Footmen).</summary>
        public const string KeyRaidStarterArmySize = "raid.starterArmySize";

        // ---------------------------------------------------------------------
        //  WO-1379 - HEARTFIRE. Two knobs, and they are PACING, not economy.
        //  Heartfire is a CHARGE, never a currency: it is not earned, traded,
        //  stored, gifted or bought, so neither of these is a price and neither
        //  may ever be joined to a wallet. See DeNelle.Core.State.HeartfireCharges,
        //  which reads them and is the only consumer.
        // ---------------------------------------------------------------------

        /// <summary>
        /// SHIPPED Heartfire pool ceiling: 3 (canon section 4 - "Three charges ... stacks
        /// to three, so sleeping or working is not punished").
        /// <para>(!) THE NUMBER LIVES HERE, not in the consumer, and that is deliberate on
        /// two counts. This file is where a tunable default is allowed to live at all
        /// (api/_lib/tunables.js says so in capitals), and tools/gen-tunable-manifest.mjs
        /// parses Registry defaults out of THIS file with a regex that resolves a bare
        /// int const and nothing else - a default written as another type's const silently
        /// FAILS TO PARSE and the knob vanishes from the Command Center with no error
        /// (measured 2026-09-04: the first draft of these two entries produced knobs=32 and
        /// no Heartfire rows). DeNelle.Core.State.HeartfireCharges aliases these consts, so
        /// there is exactly one literal and the compiler keeps the two in step.</para>
        /// </summary>
        public const int RaidHeartfireMaxChargesDefault = 3;

        /// <summary>SHIPPED Heartfire rekindle interval: 14400 s = 4 h (canon section 4).
        /// Equal to the shortest authored per-camp cooldown ON PURPOSE - see the Registry
        /// entry.</summary>
        public const int RaidHeartfireRegenSecondsDefault = 14400;

        /// <summary>Int count. The Heartfire pool ceiling (canon: three charges).</summary>
        public const string KeyRaidHeartfireMaxCharges = "raid.heartfireMaxCharges";

        /// <summary>Int seconds. How long one Heartfire charge takes to rekindle.</summary>
        public const string KeyRaidHeartfireRegenSeconds = "raid.heartfireRegenSeconds";

        // ---------------------------------------------------------------------
        //  WO-1388 - BUILDER'S HOUR. One knob: how long the pack-sold +1 Builder
        //  crew lasts. Convenience compresses TIME, never sells power (covenant
        //  docs/monetization-v2-spec.md s2), so this is a duration and nothing else.
        //  A bare int const, because tools/gen-tunable-manifest.mjs resolves ONLY
        //  that shape (see RaidHeartfireMaxChargesDefault above).
        // ---------------------------------------------------------------------

        /// <summary>SHIPPED pack temporary-builder window: 21600 s = 6 h (owner ruling 2026-09-04,
        /// verbatim "6 hours"). BuildTimerConfig.packTemporaryBuilderSeconds authors the same value;
        /// ConvenienceRedeemer.PackTemporaryBuilderSeconds resolves this knob first and falls back to
        /// the config field only when no row overrides it.</summary>
        public const int EconomyPackTemporaryBuilderSecondsDefault = 21600;

        /// <summary>Int seconds. Duration of ONE 'temporary-builder' pack charge (Builder's Hour).</summary>
        public const string KeyEconomyPackTemporaryBuilderSeconds = "economy.packTemporaryBuilderSeconds";

        // ---------------------------------------------------------------------
        //  WO-1384b - THE NIGHT MARKET CARD'S GLOW. Three FEEL knobs for the HUD
        //  card's animated rim light (a soft rounded ring plus three comets that
        //  chase the card's perimeter). Every one is a felt question about a phone
        //  screen, which is exactly what the 2026-09-02 ruling made a row instead
        //  of a rebuild: "dont make it need a code change, make it tweakable from a
        //  db call". Bare int consts, because tools/gen-tunable-manifest.mjs
        //  resolves ONLY that shape (see RaidHeartfireMaxChargesDefault above).
        //
        //  The consumer is DeNelle.HUD.HudKitController.NightMarketGlowKnobs, read
        //  once when the card is built; the animator reads the statics every frame
        //  and owns the clamps (lap 1..60 s, alpha 0..100 %, mask 0..7).
        // ---------------------------------------------------------------------

        /// <summary>SHIPPED glow lap: 5 s for one trip of the comets round the card.</summary>
        public const int HudNightMarketGlowLapSecDefault = 5;

        /// <summary>SHIPPED glow peak alpha: 35 % - a rim light, not a spotlight.</summary>
        public const int HudNightMarketGlowAlphaPctDefault = 35;

        /// <summary>SHIPPED glow palette: Gold(1) | Amber(2) | Rose(4) = 7, the full warm cycle.</summary>
        public const int HudNightMarketGlowPaletteMaskDefault = 7;

        /// <summary>Int seconds. One lap of the Night Market card's comets. Clamped 1..60 at the consumer.</summary>
        public const string KeyHudNightMarketGlowLapSec = "hud.nightMarketGlowLapSec";

        /// <summary>Int percent. Peak alpha of the ring and comet heads. Clamped 0..100 at the consumer.</summary>
        public const string KeyHudNightMarketGlowAlphaPct = "hud.nightMarketGlowAlphaPct";

        /// <summary>Int bitmask. Palette stops: Gold=1, Amber=2, Rose=4. Clamped 0..7 at the consumer;
        /// an empty mask resolves to Gold alone (logged once), never to nothing.</summary>
        public const string KeyHudNightMarketGlowPaletteMask = "hud.nightMarketGlowPaletteMask";

        // ---------------------------------------------------------------------
        //  WO-1366 - THE ARENA WAGER. Four knobs: the three opponent-tier wagers
        //  and the win purse as a percent of the stake. 50 / 100 / 200 / 200 were
        //  hardcoded in ArenaCatalog.cs; with Crystals wagered on Google Play they
        //  are the price of a REAL currency, so they ride the rail (owner
        //  2026-09-02: "make it tweakable from a db call"). The defaults are
        //  TODAY'S VALUES EXACTLY - they were authored against a free 500-seed stub
        //  and are deliberately not re-picked here; the owner feels them live.
        //  Bare int consts, because tools/gen-tunable-manifest.mjs resolves ONLY
        //  that shape (see RaidHeartfireMaxChargesDefault above).
        //
        //  The consumer is DeNelle.Village.Arena.ArenaWagerTunables, which owns
        //  the clamps (wagers 1..100000, purse 100..1000) and reads the rail only
        //  once SpecFor(key) answers - so registering the keys here is the whole
        //  wiring; nothing in the Arena changes.
        // ---------------------------------------------------------------------

        /// <summary>SHIPPED tier-1 wager (Ironhold Marauders): 50 Crystals.</summary>
        public const int ArenaWagerTier1Default = 50;

        /// <summary>SHIPPED tier-2 wager (Grimwatch Reavers): 100 Crystals.</summary>
        public const int ArenaWagerTier2Default = 100;

        /// <summary>SHIPPED tier-3 wager (Blackbanner Host): 200 Crystals.</summary>
        public const int ArenaWagerTier3Default = 200;

        /// <summary>SHIPPED win purse: 200 % of the wager = stake back plus theirs.</summary>
        public const int ArenaWinPursePctDefault = 200;

        /// <summary>Int Crystals. The tier-1 Arena wager. Clamped 1..100000 at the consumer.</summary>
        public const string KeyArenaWagerTier1 = "arena.wagerTier1";

        /// <summary>Int Crystals. The tier-2 Arena wager. Clamped 1..100000 at the consumer.</summary>
        public const string KeyArenaWagerTier2 = "arena.wagerTier2";

        /// <summary>Int Crystals. The tier-3 Arena wager. Clamped 1..100000 at the consumer.</summary>
        public const string KeyArenaWagerTier3 = "arena.wagerTier3";

        /// <summary>Int percent. The win purse as a percent of the wager; 100 = stake back only.
        /// Clamped 100..1000 at the consumer, so a WIN can never lose money.</summary>
        public const string KeyArenaWinPursePct = "arena.winPursePct";

        /// <summary>
        /// WO-1094 — the FALLBACK half-extent, in metres, the hero's off-mesh playable-bounds
        /// clamp uses in a scene whose world extent cannot be MEASURED.
        /// <para>
        /// 50 IS EXACTLY TODAY'S BEHAVIOUR, and this knob is the file's own rule applied
        /// literally rather than an exception to it. WO-1094 replaced a hardcoded ±50 clamp with
        /// one derived from the live Terrain extent, which is correct in the merged hub — but
        /// three shipped scenes carry NO Terrain at all (Village2, RaidBase_*, the legacy
        /// MainCastle_Hall, counted in the scene files 2026-09-09), and in those a purely-derived
        /// bound would have been NO bound: the hero drifts unbounded on the off-mesh transform
        /// fallback, in the raid loop that is the north star. So the measured bound wins wherever
        /// it exists, and where it does not, THIS is the bound — a row, not a constant.
        /// </para>
        /// <para>
        /// It is deliberately a HALF-EXTENT about the origin rather than four edges: that is the
        /// shape the shipped ±50 had, and inventing an asymmetric fallback would be picking a
        /// world nobody measured. Clamped 1..100000 at the consumer, so a console typo of 0
        /// cannot pin every unmeasured scene's hero to the origin.
        /// </para>
        /// </summary>
        public const int HeroPlayableFallbackHalfDefault = 50;

        /// <summary>Int, METRES. Half-extent of the hero clamp in a scene with no measurable extent.</summary>
        public const string KeyHeroPlayableFallbackHalf = "hero.playableFallbackHalf";

        /// <summary>
        /// WO-1095 — the absolute WALL-CLOCK ceiling, in seconds, on raid STAGING before the
        /// stranding watchdog gives up and routes the player home. 900 = fifteen minutes, and it
        /// is exactly the fallback <c>RaidDeployController</c> already answers for itself while
        /// this key has no <see cref="TunableSpec"/> — so registering it changes nothing on its
        /// own and simply makes the number reachable without a rebuild.
        /// </summary>
        public const int RaidStagingCeilingSecondsDefault = 900;

        /// <summary>Int, SECONDS. Wall-clock ceiling on raid staging before the stranding watchdog fires.</summary>
        public const string KeyRaidStagingCeilingSeconds = "raid.stagingCeilingSeconds";

        // ---------------------------------------------------------------------
        //  WO-1594 - THE HONOR MILESTONES. Owner ruling 2026-09-09, verbatim
        //  choice: "Tunables with those defaults".
        //
        //  The raid HUD opens with three stars lit and snuffs them as milestones
        //  pass (RaidScoring.ComputeHonorStars), and Finalize clamps the payout to
        //  min(settle, honor) - so these three numbers decide both what the bar
        //  narrates AND what a raid can pay. They were three compiled consts on
        //  RaidScoring (90f / 150f / 0.50f) from 2026-09-07, which means the
        //  pacing of every raid in the game needed a 30-minute rebuild to move.
        //  Her standing ruling since 2026-09-02 is that a balance number is a row.
        //
        //  THE DEFAULTS ARE TODAY'S BEHAVIOUR EXACTLY. 90 / 150 / 50 are the
        //  values RaidScoring hardcoded, so no row, no network and no parse pays
        //  and narrates precisely what this build shipped. The consumer reads them
        //  through SpecFor(key) FIRST (RaidScoring's HonorThirdStarSeconds and
        //  friends), so an unregistered key answers the shipping default rather
        //  than Int()'s 0-for-unknown - which for the third-star milestone would
        //  have snuffed the star on the first frame of every raid.
        //
        //  THE THIRD ONE IS A PERCENT, DELIBERATELY. The rail is integer-only, and
        //  the scorer wants a 0..1 fraction; "Pct" is in the key name so a console
        //  reader cannot mistake 50 for a fraction. The consumer clamps 0..100 and
        //  divides by 100.
        //
        //  Bare int consts, because tools/gen-tunable-manifest.mjs resolves ONLY
        //  that shape.
        // ---------------------------------------------------------------------

        /// <summary>SHIPPED: elapsed RAID seconds after which the third honor star snuffs.</summary>
        public const int RaidHonorThirdStarSecondsDefault = 90;

        /// <summary>SHIPPED: elapsed RAID seconds after which the second honor star may snuff.</summary>
        public const int RaidHonorSecondStarSecondsDefault = 150;

        /// <summary>SHIPPED: destruction PERCENT that must be reached by T2 to keep the second star.</summary>
        public const int RaidHonorSecondStarMinDestructionPctDefault = 50;

        /// <summary>Int, SECONDS. The speed honor: past this the third honor star goes dark. Clamped to at least 1 at the consumer.</summary>
        public const string KeyRaidHonorThirdStarSeconds = "raid.honorThirdStarSeconds";

        /// <summary>Int, SECONDS. Past this the second honor star goes dark IF destruction is still under the D2 threshold. Clamped to at least 1 at the consumer.</summary>
        public const string KeyRaidHonorSecondStarSeconds = "raid.honorSecondStarSeconds";

        /// <summary>Int PERCENT, 0..100. Destruction that must be reached by T2 to keep the second honor star.</summary>
        public const string KeyRaidHonorSecondStarMinDestructionPct = "raid.honorSecondStarMinDestructionPct";

        // ---------------------------------------------------------------------
        //  WO-1373 - THE ROUGH-STONE CHAIN. Three knobs, owner ruling 2026-09-09,
        //  verbatim: "there is only one stone type till it gets to jeweler, and
        //  then its RND. So only top two tiers of raids can drop stone and no more
        //  than 1 per day. 5% drop rate in dungeons not included the starter
        //  dungeons".
        //
        //  ALL THREE DEFAULTS ARE HER RULING, AND TWO OF THEM ARE DELIBERATE
        //  DEPARTURES FROM "the default is today's behaviour" - stated, not hidden:
        //    * raid.roughStoneMinTier / raid.roughStonePerDayCap open a faucet that
        //      did NOT exist before this ticket (no raid path granted the stone;
        //      grep of Assets/_Modules for the id hit only the catalog and the
        //      polish service). There is no prior behaviour to reproduce. Setting
        //      minTier above the top rung (5) turns the raid drop off entirely, and
        //      THAT is the row that restores the pre-WO-1373 build.
        //    * dungeon.roughStoneDropPct ships at 5, and the build shipped 15
        //      (DungeonController.PostFirstRoughStoneDropRate = 0.15f). 15 restores
        //      it exactly. The ruling is the reason for the change; the row is the
        //      reason nobody needs a rebuild to argue with it.
        //
        //  WHY minTier IS A TIER AND NOT A LIST OF CAMP IDS: the camps already sit
        //  on an ordered I..IV ladder (RaidLootTunables.CampIdCamp1..CampIdBastion),
        //  so "the top two" is arithmetic on that ladder rather than a second copy
        //  of the id set that would rot the day a fifth camp lands. 3 = the LOWER of
        //  the top two rungs, i.e. mage_enclave and iron_bastion drop, Camps I and
        //  II do not. A row of 2 lets fortified_garrison in without a rebuild - see
        //  the WO-1373 RESULT, which names that as the one config the row moves.
        //
        //  Bare int consts, because tools/gen-tunable-manifest.mjs resolves ONLY
        //  that shape.
        // ---------------------------------------------------------------------

        /// <summary>RULED: lowest camp TIER (1..4) that may drop a rough stone. 3 = the top two rungs.</summary>
        public const int RaidRoughStoneMinTierDefault = 3;

        /// <summary>RULED: how many rough stones ALL raids together may pay in one UTC day.</summary>
        public const int RaidRoughStonePerDayCapDefault = 1;

        /// <summary>RULED: PERCENT chance a completed non-starter dungeon run pays a rough stone.</summary>
        public const int DungeonRoughStoneDropPctDefault = 5;

        /// <summary>Int, 1..4. Lowest raid camp tier that may drop a rough stone. Above 4 turns the raid drop off.</summary>
        public const string KeyRaidRoughStoneMinTier = "raid.roughStoneMinTier";

        /// <summary>Int, COUNT per UTC day, across every camp. 0 turns the raid drop off.</summary>
        public const string KeyRaidRoughStonePerDayCap = "raid.roughStonePerDayCap";

        /// <summary>Int PERCENT, 0..100. Post-introduction rough-stone drop chance in a non-starter dungeon.</summary>
        public const string KeyDungeonRoughStoneDropPct = "dungeon.roughStoneDropPct";

        // ---------------------------------------------------------------------
        //  WO-1805 LANE C - THE DUNGEON DARKNESS, on the rail.
        //
        //  The owner's report (2026-09-16): a dungeon goes "suddenly dark" with no
        //  teach and effectively no recourse. Lane A teaches it. This lane makes the
        //  PACE of it answerable without a ~10 minute APK rebuild, which is the only
        //  way a felt call like "is 200 seconds of light fair for eleven rooms" ever
        //  gets settled.
        //
        //  ⚠ THE RAIL CARRIES NO FLOATS. TunableKind is Bool|Int only (see the enum
        //  at the top of this file), so the drain rides as an INTEGER x100 and the
        //  two refills ride as INTEGER PERCENTS. The x100 is in the key NAME so a
        //  reader at the database cannot mistake 50 for 50/s.
        //
        //  ⚠ EVERY DEFAULT BELOW IS AN IDENTITY. 50 = the authored 0.50/s in
        //  dungeon-balance.json; 100 = an oil stone topping the flask, which is what
        //  Lantern.CheckOilStones has always done; 40 = ComposedOilStill.RefillFraction
        //  0.40f; 30 = Lantern.DefaultFinalWarningSeconds. An empty client_tunables
        //  table therefore behaves bit-identically to the pre-ticket build.
        //
        //  ⚠ AND THE CONSUMERS TREAT A ROW AT ITS DEFAULT AS "NOT SET". The drain and
        //  the warning window are ALSO authored in dungeon-balance.json; a rail row
        //  that always won would make that json dead data the first time anyone
        //  re-authored it (the json would say 0.40 and the build would keep burning
        //  0.50 from a default nobody chose). So Lantern.ApplyBalanceData applies the
        //  row only when it DEVIATES from the default below - the json stays the
        //  single authority until the owner actually moves a knob.
        //
        //  Bare int consts, because tools/gen-tunable-manifest.mjs resolves ONLY
        //  that shape.
        // ---------------------------------------------------------------------

        /// <summary>IDENTITY: 50 = 0.50 oil/s, the value dungeon-balance.json authors today (200 s a flask).</summary>
        public const int DungeonLanternDrainPerSecX100Default = 50;

        /// <summary>IDENTITY: 100 = an oil stone tops the flask, exactly as Lantern.CheckOilStones always has.</summary>
        public const int DungeonLanternOilStoneRefillPctDefault = 100;

        /// <summary>IDENTITY: 40 = ComposedOilStill.RefillFraction 0.40f (one field distillation = 40 % of a flask).</summary>
        public const int DungeonLanternStillRefillPctDefault = 40;

        /// <summary>IDENTITY: 30 = Lantern.DefaultFinalWarningSeconds, the final visible collapse window.</summary>
        public const int DungeonLanternFinalWarningSecDefault = 30;

        /// <summary>Int, oil-per-second x100 (50 = 0.50/s). THE duration knob: secondsToEmpty = 100 / (this/100).</summary>
        public const string KeyDungeonLanternDrainPerSecX100 = "dungeon.lanternDrainPerSecX100";

        /// <summary>Int PERCENT, 1..100. How much of the flask one oil stone returns.</summary>
        public const string KeyDungeonLanternOilStoneRefillPct = "dungeon.lanternOilStoneRefillPct";

        /// <summary>Int PERCENT, 1..100. How much of the flask one field-still distillation returns.</summary>
        public const string KeyDungeonLanternStillRefillPct = "dungeon.lanternStillRefillPct";

        /// <summary>Int SECONDS. The final-warning window: range collapse + the 0.45..3.2 m fog wall.</summary>
        public const string KeyDungeonLanternFinalWarningSec = "dungeon.lanternFinalWarningSec";

        // ---------------------------------------------------------------------
        //  WO-1343 - THE NIGHT STORE'S AURA. Four knobs, and they exist because
        //  the owner asked a QUESTION SHE HAS EXPLICITLY NOT ANSWERED.
        //
        //  She tagged NightStoreoption_Aura (top_down_starfall_line_blue) for the
        //  Night Store; then tagged a SECOND candidate, Store_Aura (Loot_flicker),
        //  saying verbatim "i added another option for REalm store, not sure which
        //  will be best"; and separately asked "can we use these [the seven Aura_*
        //  spells] slowly one after another instead at the night store IF THE OTHER
        //  ONE DOESNT LOOK GOOD". Every one of those is a creative call conditional
        //  on device feel, so ALL of it ships in one build and the choice is a row,
        //  per her standing 2026-09-02 ruling: "be smart, dont make it need a code
        //  change, make it tweakable from a db call" / "i have been screaming this
        //  for months."
        //
        //  (S) HER FIRST PICK IS THE DEFAULT AND ROTATION SHIPS OFF. Mode 0 = the
        //  first key she tagged, played verbatim, pulsed every 30 minutes. An empty
        //  table, a 404, a malformed row and an offline player therefore all get
        //  exactly that. Nothing here promotes her second candidate or the family -
        //  choosing between them is the decision she reserved for herself.
        //
        //  (!) THE CADENCE MEANS TWO DIFFERENT THINGS, because the candidates are
        //  two different KINDS of effect (MEASURED, not assumed): both of her store
        //  tags are one-shot BURSTS (all ParticleSystems looping:0) while the Aura_*
        //  family is CONTINUOUS (looping:1). So in a burst mode the cadence RE-FIRES
        //  the burst, and in rotate mode it ADVANCES to the next aura. The consumer
        //  reports which meaning is live on every trace line.
        //
        //  The rotation membership is a BITMASK rather than a string list on
        //  purpose: it rides the existing integer rail instead of growing the
        //  tunables a new value kind, and "take that one out of the rotation" is
        //  still a single number with no code change and no schema change.
        //
        //  The consumer is DeNelle.Village.NightStoreAuraSelector, which owns the
        //  clamps and is the ONLY reader. Nothing here picks a prefab: the family
        //  names are a directory listing of the folder she screenshotted, and an
        //  untagged member is SKIPPED BY NAME rather than substituted.
        // ---------------------------------------------------------------------

        /// <summary>0 = TaggedStarfall (SHIPPED, her first pick). 1 = TaggedLootFlicker
        /// (her second candidate). 2 = RotateFamily. 3 = LegacyBeaconRing.</summary>
        public const int VfxNightStoreAuraModeDefault = 0;

        /// <summary>Minutes between night-store aura cadence ticks. Her "every 30~min".</summary>
        public const int VfxNightStoreAuraCadenceMinDefault = 30;

        /// <summary>Bitmask over the seven Aura_* prefabs, folder order. 127 = all seven.
        /// Written in DECIMAL deliberately: tools/gen-tunable-manifest.mjs resolves a default
        /// const with a decimal-only regex, so a hex literal here would fail the generator.</summary>
        public const int VfxNightStoreAuraFamilyMaskDefault = 127;

        /// <summary>Seconds between EXTRA burst re-fires inside one cadence period.
        /// 0 = OFF, which is her spec read literally: one burst per cadence tick.</summary>
        public const int VfxNightStoreAuraBurstRepeatSecDefault = 0;

        /// <summary>Int enum. What drives the Night Store's aura seat:
        /// 0 tagged-starfall / 1 tagged-lootflicker / 2 rotate-family / 3 legacy-ring.</summary>
        public const string KeyVfxNightStoreAuraMode = "vfx.nightStoreAuraMode";

        /// <summary>Int minutes. Cadence of the night-store aura tick.</summary>
        public const string KeyVfxNightStoreAuraCadenceMin = "vfx.nightStoreAuraCadenceMin";

        /// <summary>Int bitmask. Which Aura_* family members the rotation may select.</summary>
        public const string KeyVfxNightStoreAuraFamilyMask = "vfx.nightStoreAuraFamilyMask";

        /// <summary>Int seconds. Extra burst re-fire period inside one cadence. 0 = off.</summary>
        public const string KeyVfxNightStoreAuraBurstRepeatSec = "vfx.nightStoreAuraBurstRepeatSec";

        // ---------------------------------------------------------------------
        //  Verbosity levels for KeyTraceAssetVerbosity.
        //
        //  ⛔ THERE IS NO "OFF". CLAUDE.md section 12 is binding: instrumentation is
        //  PERMANENT, and a Warn or a Fail that stops being emitted turns a logged
        //  failure back into a silent one. This knob only ever moves the STEP lines -
        //  the narration - and every level below still emits Warn and Fail in full.
        // ---------------------------------------------------------------------

        // ---------------------------------------------------------------------
        //  WO-1348 - TAG A VFX FROM THE COMMAND CENTER, SEE IT ON THE NEXT TOWN LOAD.
        //
        //  Owner ask 2026-09-03, verbatim: "is it possible to tag those from the
        //  command center? and then change pointer on next town load?" /
        //  "realm.vfx(set)" / "that idea". Her namespace proposal is adopted as the
        //  key shape VERBATIM - realm.vfx.<the VFX catalog key> - and the VFX key
        //  keeps its own casing and underscores, because inventing a second spelling
        //  of a key that already exists is how a join silently misses.
        //
        //  (!) THE VALUE IS A STABLE OPTION ID, NOT A PREFAB PATH AND NOT A SORTED
        //  POSITION. This rail is Int-only (TunableKind above is Bool|Int), so the
        //  row carries an integer naming an entry in the SHIPPED option pool,
        //  Assets/Resources/VFX/vfx-pick-options.generated.json, produced from her
        //  own tag file by tools/gen-vfx-pick-options.mjs. Ids are assigned once and
        //  NEVER reassigned: a sorted position would shift the day anybody tags a new
        //  key and would silently re-point a row she set last week while the trace
        //  still said "override applied" - a lying trace, which is the one thing the
        //  work order's instrumentation section forbids by name.
        //
        //  (!) 0 IS "THE BUILD-TIME PICK", so an empty client_tunables table renders
        //  EXACTLY what Assets/Editor/VfxManualPicks.json renders today. That file
        //  stays the default and the record; nothing here deletes or bypasses it.
        //
        //  (!) AN OPTION IS SHIPPED BY CONSTRUCTION. Choosing option N for key K means
        //  "render K with the prefab key <N> already renders with" - so the picker
        //  cannot offer a prefab that was never built. That is CLAUDE.md section 16's
        //  lesson applied one layer up: art picked but never shipped fails with NO
        //  ERROR ON SCREEN, and that silence has cost this project three incidents.
        //  Adding a NEW prefab to the pool is still a build.
        //
        //  The consumer is DeNelle.Core.Vfx.VfxPickOverrides, which snapshots these on
        //  scene load and is the ONLY reader. Four keys are registered - the four the
        //  owner could not fix without a rebuild on 09-03.
        //
        //  (!) ONLY ONE OF THE FOUR IS PLAYED BY THE GAME TODAY, and it is stated here
        //  rather than discovered on a phone. Every line below was checked at SOURCE on
        //  2026-09-10 by grepping the tree for the key, not inferred from the ticket:
        //    - BossDeath_Impact         LIVE. EliteVFXController.cs:326 plays it through
        //                               HeldVfxKeys.BossDeath, gated on isBoss. DragonBoss
        //                               has its own Die() and does NOT route through it.
        //    - atfootprintoftree_Aura   HAS a caller (HeartAuraController.cs:323, through
        //                               HeldVfxKeys.TreeOfLifeFootAura) but the SITE IS
        //                               WITHHELD: AmbientAuraPolicy.WithholdTreeFootAura is
        //                               true (AmbientAuraPolicy.cs:129, owner ruling
        //                               2026-09-07 / WO-1476 - the aura drifted up the Y
        //                               axis over the town), so nothing spawns whatever the
        //                               row says. The pick STANDS and takes effect the day
        //                               that flag flips.
        //    - atfootprintoftree_Impact NO CALLER ANYWHERE, and absent from the tag file -
        //                               this is the CREATION case the ticket demands. A
        //                               pick is invisible until a call site is wired.
        //    - EliteDeath_Impact        NO CALLER ANYWHERE. An elite death plays the BOSS
        //                               key, which is her own ruling ("both get Elite_Death,
        //                               name it BossDeath_Impact"). Registered so the slot
        //                               is ready the day the two are pulled apart; it
        //                               changes nothing on screen today.
        //
        //  ⛔ DO NOT "FIX" THE THREE BY RE-POINTING THEM AT LIVE KEYS. The work order names
        //  these four, VFX keys map owner tags to hooks VERBATIM, and the CLI never makes a
        //  creative pick. The honest fix is the one taken: each Command Center card SAYS IN
        //  WORDS that its slot is not wired yet, so a pick that changes nothing on screen
        //  can never read as a broken feature.
        //  A fifth VFX key becomes tunable by adding a const + a spec here and one
        //  row in each of the other four sources; nothing else changes.
        // ---------------------------------------------------------------------

        /// <summary>Every realm.vfx.* knob ships at 0 = USE THE BUILD-TIME PICK. Never edit this.</summary>
        public const int VfxPickBuildDefaultId = 0;

        /// <summary>Int option id. The aura at the foot of the world tree.</summary>
        public const string KeyRealmVfxTreeFootprintAura = "realm.vfx.atfootprintoftree_Aura";

        /// <summary>Int option id. The impact at the foot of the world tree - NO build-time entry.</summary>
        public const string KeyRealmVfxTreeFootprintImpact = "realm.vfx.atfootprintoftree_Impact";

        /// <summary>Int option id. The elite-death burst.</summary>
        public const string KeyRealmVfxEliteDeathImpact = "realm.vfx.EliteDeath_Impact";

        /// <summary>Int option id. The boss-death burst.</summary>
        public const string KeyRealmVfxBossDeathImpact = "realm.vfx.BossDeath_Impact";

        /// <summary>Failures and warnings only. No Step narration.</summary>
        public const int VerbosityQuiet = 0;

        /// <summary>Failures, warnings, and the lifecycle Steps that name a decision.</summary>
        public const int VerbosityNormal = 1;

        /// <summary>TODAY'S BEHAVIOUR. Every Step, including the per-request narration.</summary>
        public const int VerbosityVerbose = 2;

        /// <summary>
        /// THE REGISTRY. Every knob, its shipping default, what turning it on does, and
        /// which PROD-022 hypothesis it tests.
        /// <para>
        /// ⭐ EVERY Default HERE IS THE VALUE THE SHIPPING CODE USED BEFORE PROD-022
        /// TOUCHED IT. That is not a convention, it is the acceptance criterion: a build
        /// with an empty client_tunables table must behave byte-for-byte like the build
        /// before this work. The pairs are checked against their real owners in
        /// StructureContentWarmer.cs and VisualFactory.cs, which read them through this
        /// file and nowhere else.
        /// </para>
        /// </summary>
        public static readonly TunableSpec[] Registry =
        {
            new TunableSpec(KeyPiEagerStructureWarm, TunableKind.Bool, 0,
                "Pi Browser runs the FULL desktop warm pass (await Addressables init, harvest keys, " +
                "DownloadDependenciesAsync, then load and retain all 35 structure prefabs) instead of " +
                "the on-demand policy.",
                "That on-demand streaming is itself the problem and eager residency is the healthier " +
                "shape on this webview. Deliberately shipped OFF: WO-PROD-022 forbids re-enabling eager " +
                "residency WITHOUT PROOF, and this knob is how the proof is gathered rather than assumed."),

            new TunableSpec(KeyPiAwaitInitBeforeFirstLoad, TunableKind.Bool, 0,
                "Pi Browser awaits Addressables.InitializeAsync and harvests every registered key BEFORE " +
                "the first on-demand LoadAssetAsync is issued; requests raised in the meantime are queued " +
                "and drained when init lands. Residency policy is otherwise untouched (this is NOT the " +
                "eager warm).",
                "PRIME SUSPECT. Today the Pi branch of StructureContentWarmer.Boot returns without ever " +
                "awaiting init and without harvesting keys, so the FIRST on-demand request is the first " +
                "thing that touches the catalog, and State is Degraded from frame one - which makes " +
                "IsSettled TRUE immediately, so a WhenSettled retry can fire before a single location " +
                "exists. That is the shape of the observed 'model not found' storm."),

            new TunableSpec(KeyPiDisableRemoteStructureArt, TunableKind.Bool, 0,
                "Pi Browser issues NO remote structure-art request at all. Every caller keeps the path it " +
                "already takes when an asset is not resident - the baked twin or the pending-art proxy - " +
                "so the town still renders and nothing stalls or blanks.",
                "THE BIG HAMMER, and it is diagnostically decisive in BOTH directions. If the crash loop " +
                "STOPS with this on, asset streaming is implicated beyond argument. If it CONTINUES, " +
                "streaming is exonerated and the cause is elsewhere - which is worth just as much. It " +
                "trades visual fidelity for a clean signal, on purpose."),

            new TunableSpec(KeyAssetsMaxConcurrentRequests, TunableKind.Int, 0,
                "Caps how many residency fetches may be in flight at once, on every host. 0 = TODAY: Pi " +
                "serialises through its own latch and desktop is unbounded. 1 or more installs an explicit " +
                "shared queue with that ceiling.",
                "That several simultaneous multi-MB bundle downloads plus decompression blow a memory " +
                "ceiling that lives OUTSIDE the managed heap - which is exactly how the captured sessions " +
                "look, dying with mem=247MB flat and no exception."),

            new TunableSpec(KeyPiRequestTimeoutSeconds, TunableKind.Int, 20,
                "The UnityWebRequest timeout installed by the Pi Addressables WebRequestOverride.",
                "That 20s is the wrong bound - too long, so a stalled fetch holds the queue past the " +
                "30-60s lifetime we are trying to survive; or too short, so a slow-but-healthy fetch is " +
                "killed and retried. Untunable today, and the WO forbids GUESSING a new constant - so it " +
                "ships at 20 and moves only on data."),

            new TunableSpec(KeyAssetsMaxRequestAttempts, TunableKind.Int, 3,
                "How many async fetch attempts one address gets before it is retired for the launch.",
                "That the retry budget is mis-sized: too high and the retry storm itself is the load that " +
                "kills the tab; too low and one transient webview stall costs a building its art for the " +
                "whole session."),

            new TunableSpec(KeyVisualsMissLogCap, TunableKind.Int, 3,
                "How many full resolve-miss Fail lines VisualFactory emits per address before it " +
                "announces its cap and drops to a throttled line. It NEVER goes silent.",
                "That trace VOLUME is itself a contributor - the observed final seconds were nothing but " +
                "the same four addresses cycling, and every line is a remote trace POST from a device " +
                "that is already the suspect."),

            new TunableSpec(KeyTraceAssetVerbosity, TunableKind.Int, VerbosityVerbose,
                "Narration level for the [Flow:StructureAssets] and [Flow:VisualFactory] families. " +
                "2 = today (every Step). 1 = lifecycle Steps only. 0 = no Steps. Warn and Fail are " +
                "emitted at EVERY level and cannot be turned off.",
                "Same volume hypothesis as the miss-log cap, but separable: this one silences the " +
                "SUCCESS narration while leaving every failure line intact, so a quiet-but-still-" +
                "diagnostic session can be compared against a loud one."),

            new TunableSpec(KeyCombatDrainReturnPct, TunableKind.Int, DrainReturnPctDefault,
                "Percent of the damage a drainshot ability ACTUALLY DEALS that comes back to the caster " +
                "as healing. 100 = TODAY: heal == damage dealt, exactly. Applies to every drainshot - " +
                "mage.siphon, mage.drain and ranger.healing-shot - because HeroAbilities.HealFromDrain " +
                "is the single owner of the drain heal. Clamped to 0..1000 at the consumer.",
                "NOT a PROD-022 hypothesis - this is the BALANCE lever for WO-1306. The owner ruled the " +
                "mage's first talent point must buy a castable that SUSTAINS ('the blm needs to get some " +
                "healing , like drain to stay balanced (early)') and then that the strength must move " +
                "without a rebuild ('be smart, dont make it need a code change, make it tweakable from a " +
                "db call'). 100 is the value the shipped resolver hardcoded, so an offline player, a 404 " +
                "and an empty table all get exactly the drain that shipped; the knob only ever moves it " +
                "on her word."),

            new TunableSpec(KeyVfxParticleBouncePct, TunableKind.Int, VfxParticleBouncePctDefault,
                "Percent restitution allowed on a WORLD-COLLIDING particle inside any VFX host the pooled " +
                "spawner checks out. 0 = THIS BUILD: a particle that hits scene geometry stops there and " +
                "terminates (bounce 0, dampen 1, lifetime-loss 1). 100 = leave the art pack's authored " +
                "collision untouched. The clamp only ever tightens, so it can never make an effect bouncier " +
                "than its author made it.",
                "NOT a PROD-022 hypothesis - a FEEL knob (WO-1327). Spell_Fire_9's Fireballs emitter is " +
                "authored bounce 1.0 / dampen 0 / minKillSpeed 0 against ALL 32 LAYERS at High quality: " +
                "perfectly elastic, nothing ever kills the particle. Cast inside a walled town that is a " +
                "projectile in a box, and the owner reported the fire spell 'casts at me and stays at me' " +
                "twice (F8 seq 4152, 4644). Those numbers live in a GITIGNORED pack prefab, so the clamp " +
                "has to live at the spawn owner; this knob is how the owner moves it without a rebuild, and " +
                "how she puts the authored behaviour back in one word if the new feel is wrong."),

            new TunableSpec(KeyVfxMaxParticleLights, TunableKind.Int, VfxMaxParticleLightsDefault,
                "Ceiling on the total concurrent real-time point lights ONE spawned VFX host may drive " +
                "through its ParticleSystem LightsModules, summed across every emitter on that host. 4 = " +
                "THIS BUILD. 0 turns particle lights off outright. The budget is spent evenly across the " +
                "host's enabled modules and each module's ratio is scaled down with it. It never deletes a " +
                "light prototype.",
                "NOT a PROD-022 hypothesis - a MOBILE PERF knob (WO-1327). Spell_Fire_9 drives 20 lights " +
                "from Fireballs and 5 more from its Explosion sub-emitter: TWENTY-FIVE real-time point " +
                "lights per cast, at intensity 5 and range 5, on the Seeker. That is a frame-rate event on " +
                "every fireball. Like the bounce knob the dial is baked into a gitignored prefab, so the cap " +
                "belongs at the spawn owner - and it must move on device evidence rather than on a number " +
                "somebody picked."),

            new TunableSpec(KeyCombatOverTimeTickMs, TunableKind.Int, OverTimeTickMsDefault,
                "Milliseconds between the pulses of EVERY over-time effect, damage and healing alike - " +
                "the mage's wither, the knight's regen, the shipped burn on knight.emberbrand-throw and " +
                "mage.poison, and the Venombrand poison rider. 1000 = TODAY: exactly the 'const float " +
                "tick = 1f' both shipped DoT coroutines hardcoded. Magnitude per pulse is derived as " +
                "perSecond * interval, so moving this changes CADENCE ONLY - total delivery is invariant. " +
                "Clamped to 50..60000 at the consumer.",
                "NOT a PROD-022 hypothesis - a FEEL knob (WO-1330). How often a DoT ticks is the whole " +
                "READ of the effect: at 1000ms it is four discrete thuds over four seconds, at 250ms it " +
                "is a continuous drain. Which one communicates 'this is still hurting you' is a question " +
                "only felt-testing answers, and the owner is red/green colourblind, so RHYTHM is carrying " +
                "signal that colour cannot. It must move in seconds, not in a rebuild."),

            new TunableSpec(KeyCombatOverTimeMagnitudePct, TunableKind.Int, OverTimeMagnitudePctDefault,
                "Percent scale on the magnitude of every over-time pulse, both signs. 100 = TODAY: the " +
                "dotDamage / healPerSecond authored in abilities.json, unscaled. 50 halves every DoT and " +
                "every regen at once; 0 makes them inert without unauthoring anything. Clamped to 0..1000 " +
                "at the consumer.",
                "NOT a PROD-022 hypothesis - a BALANCE lever (WO-1330). It is ONE knob rather than one " +
                "per ability because the ticket required exactly that ('Prefer ONE shared knob over " +
                "per-ability duplicates where it is genuinely the same concept'): the first tuning " +
                "question is always whether over-time damage as a CLASS of effect is pulling its weight " +
                "against burst, and that is a single dial. Per-ability numbers stay in abilities.json."),

            new TunableSpec(KeyCombatOverTimeDurationPct, TunableKind.Int, OverTimeDurationPctDefault,
                "Percent scale on the duration of every over-time effect, both signs. 100 = TODAY: the " +
                "authored dotSeconds / seconds, unscaled. Raising it lengthens the window and therefore " +
                "adds pulses, so it moves TOTAL delivery where the magnitude knob moves per-pulse size. " +
                "Clamped to 0..1000 at the consumer.",
                "NOT a PROD-022 hypothesis - a BALANCE lever (WO-1330), and the one that decides whether " +
                "an over-time ability is a commitment or a garnish. Separated from the magnitude knob on " +
                "purpose: 'each tick hurts more' and 'it lasts longer' feel completely different at the " +
                "same total damage, and collapsing them into one dial would make that distinction " +
                "untestable."),

            new TunableSpec(KeyVfxNightStoreAuraMode, TunableKind.Int, VfxNightStoreAuraModeDefault,
                "What the Night Store's aura seat plays. 0 = THIS BUILD: her FIRST tagged key " +
                "NightStoreoption_Aura (top_down_starfall_line_blue), a one-shot burst re-fired on the " +
                "cadence. 1 = her SECOND tagged candidate Store_Aura (Loot_flicker), also a burst. " +
                "2 = walk the seven continuous Aura_* spell prefabs, one at a time in folder order, " +
                "advancing on the cadence. 3 = the Marker8 safe-zone ring this build replaced. Any " +
                "other number is ignored and resolves to 0.",
                "NOT a PROD-022 hypothesis - a PURE CREATIVE CHOICE the owner has explicitly not made. " +
                "She tagged one store aura, then a second ('i added another option for REalm store, not " +
                "sure which will be best'), then asked whether the Aura_* family could cycle 'slowly one " +
                "after another instead ... IF THE OTHER ONE DOESNT LOOK GOOD'. Three candidates and a " +
                "conditional. Building one and discarding the rest would either pick for her or cost a " +
                "30-minute rebuild per opinion; this knob makes it a 40-second flip on the device with " +
                "the thing in front of her. Her first pick ships as the default and nothing promotes " +
                "the others (memory vfx-map-owner-tags-no-creative-pick)."),

            new TunableSpec(KeyVfxNightStoreAuraCadenceMin, TunableKind.Int, VfxNightStoreAuraCadenceMinDefault,
                "Minutes between Night Store aura cadence ticks. 30 = TODAY, and it is her number " +
                "verbatim ('its to be random when in town every 30~min'). What a tick DOES depends on " +
                "the mode: in a burst mode it re-fires the burst, in rotate mode it advances to the " +
                "next aura, and against the continuous legacy ring it does nothing. Clamped to " +
                "1..1440 at the consumer. The clock ticks in TOWN only - never during a raid, a battle " +
                "or a dungeon.",
                "NOT a PROD-022 hypothesis - a FEEL knob (WO-1343). Whether a half-hourly pulse reads " +
                "as 'the store just caught my eye' or as 'nothing ever happens there' is a question " +
                "only felt-testing on the device answers, and the owner is red/green colourblind, so " +
                "RHYTHM is carrying signal that colour cannot. It has to move in seconds."),

            new TunableSpec(KeyVfxNightStoreAuraFamilyMask, TunableKind.Int, VfxNightStoreAuraFamilyMaskDefault,
                "Bitmask of which Aura_* prefabs the rotation may select, in the folder's own " +
                "alphabetical order: 1 Arcane, 2 Dark, 4 Fire, 8 Ice, 16 Light, 32 Nature, 64 Storm. " +
                "127 = THIS BUILD: all seven eligible. INERT unless the mode knob is 2. A member the " +
                "owner has not yet tagged in the VFX Caster does not resolve, is skipped BY NAME in " +
                "the trace, and is never substituted for. A mask that enables nothing falls back to " +
                "her first tagged key rather than leaving the store bare.",
                "NOT a PROD-022 hypothesis - the WO-1343 requirement that 'a prefab she dislikes comes " +
                "out without a code change'. A bitmask rather than a string list because that rides the " +
                "existing integer rail: adding a new tunable VALUE KIND for one feature is the second " +
                "configuration mechanism this whole rail exists to avoid."),

            new TunableSpec(KeyVfxNightStoreAuraBurstRepeatSec, TunableKind.Int, VfxNightStoreAuraBurstRepeatSecDefault,
                "Seconds between EXTRA re-fires of the burst INSIDE one cadence period. 0 = THIS " +
                "BUILD: off, exactly one burst per cadence tick, which is her spec read literally. " +
                "Set it to a few seconds to turn the half-hourly pulse into a slow heartbeat. Clamped " +
                "to 0..600. Ignored entirely in the two CONTINUOUS modes (rotate-family and the legacy " +
                "ring), where there is no burst to repeat.",
                "NOT a PROD-022 hypothesis - the escape hatch for the one number in this feature that " +
                "was measured rather than chosen. BOTH of her store tags were verified one-shot (every " +
                "ParticleSystem looping:0 on top_down_starfall_line_blue and Loot_flicker), so her " +
                "isLoop:false is CORRECT and a burst is what she authored. But '30~min' was a rough " +
                "number said in passing, and if one pulse per half hour turns out to read as nothing " +
                "at all, this fixes it without a rebuild and without anyone re-tagging her prefab."),

            new TunableSpec(KeyRaidLootWoodBase, TunableKind.Int, RaidLootWoodBaseDefault,
                "WOOD a raid pays at a PERFECT run (3 stars AND 100% destruction) on a Camp I-tier " +
                "base, before the camp's own difficulty multiplier. 1800 = the owner's number from " +
                "the north-star map. Every lesser result pays a percentage of it off the ladder " +
                "knobs below. 0 restores the old behaviour, in which a raid paid no wood at all. " +
                "Clamped to 0..1000000 at the consumer.",
                "NOT a PROD-022 hypothesis - the CENTRAL BALANCE NUMBER of the whole raid programme " +
                "(WO-1374). The map sizes it as 60-65% of four hours of passive wood output, so that " +
                "a raid funds real construction without making collectors worthless - a ratio that " +
                "only felt-testing on a real save can confirm. She is setting the reward curve for " +
                "the main loop BY FEEL, and every value has to reach her device in seconds."),

            new TunableSpec(KeyRaidLootIronBase, TunableKind.Int, RaidLootIronBaseDefault,
                "IRON a raid pays at a PERFECT run, on the same terms as the wood knob above. " +
                "1100 = the owner's number from the north-star map. 0 restores the old no-iron " +
                "payout. Clamped to 0..1000000 at the consumer.",
                "NOT a PROD-022 hypothesis - the second half of the WO-1374 reward table. Kept as " +
                "its OWN knob rather than derived from wood by a ratio, because wood and iron are " +
                "different construction bottlenecks at different points in the build tree and the " +
                "first question she will ask is which of the two the raid should favour."),

            new TunableSpec(KeyRaidLootFailPct, TunableKind.Int, RaidLootFailPctDefault,
                "Percent of the base wood/iron a FAILED attack pays. 18 = the middle of the map's " +
                "stated 15-20% band. Deliberately NOT zero: the map says a loss paying something is " +
                "what keeps it from being a dead end. Clamped to 0..1000 at the consumer.",
                "NOT a PROD-022 hypothesis - a RETENTION lever. Whether a wipe reads as 'that was a " +
                "waste of twenty minutes' or as 'I nearly had it, go again' is exactly the feeling " +
                "this number sets, and it is unknowable from a spreadsheet."),

            new TunableSpec(KeyRaidLootOneStarPct, TunableKind.Int, RaidLootOneStarPctDefault,
                "Percent of the base wood/iron paid at 1 star. 50 = the map's ladder. Clamped to " +
                "0..1000 at the consumer.",
                "NOT a PROD-022 hypothesis - one rung of the map's performance ladder (fail / 1 / 2 " +
                "/ 3 stars / perfect). The ladder is the mechanism by which GETTING BETTER AT " +
                "RAIDING HAS AN ECONOMIC PAYOFF, which is the map's phrase and the entire reason the " +
                "rungs are separate knobs rather than one curve constant."),

            new TunableSpec(KeyRaidLootTwoStarPct, TunableKind.Int, RaidLootTwoStarPctDefault,
                "Percent of the base wood/iron paid at 2 stars. 75 = the map's ladder. Clamped to " +
                "0..1000 at the consumer.",
                "NOT a PROD-022 hypothesis - see the 1-star rung. The gap between the 2-star and " +
                "3-star rungs is what decides whether a player pushes for the full clear or takes " +
                "the safe two and leaves."),

            new TunableSpec(KeyRaidLootThreeStarPct, TunableKind.Int, RaidLootThreeStarPctDefault,
                "Percent of the base wood/iron paid at 3 stars. 100 = the map's ladder, i.e. the " +
                "base IS the 3-star payout. Clamped to 0..1000 at the consumer.",
                "NOT a PROD-022 hypothesis - the rung that DEFINES what the base means. Moving it " +
                "off 100 re-anchors the whole table without touching the base numbers, which is the " +
                "cheap way to answer 'is every raid paying slightly too much'."),

            new TunableSpec(KeyRaidLootPerfectPct, TunableKind.Int, RaidLootPerfectPctDefault,
                "Percent of the base wood/iron paid at 3 stars WITH 100% destruction. 110 = the " +
                "map's ladder - the only rung that pays above the base, and the reward for razing " +
                "everything rather than just winning. Clamped to 0..1000 at the consumer.",
                "NOT a PROD-022 hypothesis - the top of the ladder, and the one number that says " +
                "mastery is worth more than victory. If the 10% premium turns out not to be worth " +
                "the extra two minutes of mop-up, this is where that is discovered and fixed."),

            new TunableSpec(KeyRaidLootCoinsBaseCamp1, TunableKind.Int, RaidLootCoinsBaseCamp1Default,
                "GOLD a raid pays at a PERFECT run (3 stars AND 100% destruction) on CAMP I, and " +
                "the fallback for any raid whose config id the per-camp table does not name. 2200 = " +
                "the owner's number from the north-star map, sized at 125-140% of that camp's " +
                "designed 1650-gold army replacement cost. Rides the SAME five-rung performance " +
                "ladder as wood and iron. NOT multiplied by the camp's rewardMultiplier - the " +
                "escalation lives in the three per-camp knobs below. 0 stops raids paying gold. " +
                "Clamped to 0..1000000 at the consumer.",
                "NOT a PROD-022 hypothesis - THE MISSING ARROW, named outright by the north-star " +
                "map: 'You currently have Gold -> troops but not troops -> raids -> gold. That " +
                "arrow has to exist.' Gold buys troops, troops win raids, raids pay gold. Whether " +
                "+550 gold of advancement per clear reads as 'I can raid again' or as 'that was " +
                "barely worth it' is the felt question this knob answers, and it is the number " +
                "that closes the loop the whole programme is measured against."),

            new TunableSpec(KeyRaidLootCoinsBaseCamp2, TunableKind.Int, RaidLootCoinsBaseCamp2Default,
                "GOLD a PERFECT run pays on CAMP II. 3100 = the map's number, against that camp's " +
                "designed 2300-gold army. Its own knob rather than a multiplier off Camp I because " +
                "the map publishes a DESIGNED target per camp: x1.5 of 2200 is 3300, not 3100. " +
                "Clamped to 0..1000000 at the consumer.",
                "NOT a PROD-022 hypothesis - one rung of the map's per-camp gold escalation. The " +
                "step between camps is what decides whether unlocking a harder raid feels like " +
                "progress or like the same raid with more HP."),

            new TunableSpec(KeyRaidLootCoinsBaseCamp3, TunableKind.Int, RaidLootCoinsBaseCamp3Default,
                "GOLD a PERFECT run pays on CAMP III. 4500 = the map's number, against a designed " +
                "3300-gold army. Clamped to 0..1000000 at the consumer.",
                "NOT a PROD-022 hypothesis - see the Camp II knob. Sized against the army the " +
                "player is EXPECTED to bring, never their actual one, because an actual-cost " +
                "reward can be gamed by attacking with nothing."),

            new TunableSpec(KeyRaidLootCoinsBaseBastion, TunableKind.Int, RaidLootCoinsBaseBastionDefault,
                "GOLD a PERFECT run pays on the IRON BASTION, the map's fourth and evergreen " +
                "target. 6500 = the map's number, against a designed 4800-gold army. The " +
                "scene-configs.json row for 'iron_bastion' exists as of 2026-09-04, and its " +
                "rewardMultiplier - whatever that file currently authors, READ IT THERE - is " +
                "deliberately IGNORED by gold: the map publishes a DESIGNED gold target per camp " +
                "sized against that camp's expected army cost, so no base-times-multiplier pays all " +
                "four published numbers. Clamped to 0..1000000.",
                "NOT a PROD-022 hypothesis - the top of the map's per-camp gold ladder, registered " +
                "now so the number is a knob from the day the Bastion is switched on rather than a " +
                "literal someone has to find later."),

            new TunableSpec(KeyRaidLootCrystalsBase, TunableKind.Int, RaidLootCrystalsBaseDefault,
                "CRYSTALS a raid pays at 100% destruction, before the per-star bonus. 20 = the " +
                "owner's number; with the per-star knob a perfect clear pays 26, inside the map's " +
                "20-30 band and DOWN from the 55 this build used to pay. Crystals are the ONE " +
                "reward in the table that DECREASES, and they are NOT multiplied by the camp's " +
                "rewardMultiplier, so a harder camp pays more gold/wood/iron and not more timer " +
                "compression. Clamped to 0..1000000 at the consumer.",
                "NOT a PROD-022 hypothesis - a PACING lever, and the one the map is bluntest " +
                "about: 'Crystals are timer compression. If raids dump huge amounts of crystals, " +
                "you accidentally accelerate the already-too-short progression curve.' Crystals " +
                "buy instant-finish, so this number silently sets how long the whole build tree " +
                "takes. It has to be movable in seconds if a raid turns out to be defunding the " +
                "timer ladder the game is paced by."),

            new TunableSpec(KeyRaidLootCrystalsPerStar, TunableKind.Int, RaidLootCrystalsPerStarDefault,
                "Extra CRYSTALS per earned star, on top of the base above. 2 = the owner's number " +
                "(down from 10). Clamped to 0..1000000 at the consumer.",
                "NOT a PROD-022 hypothesis - the second half of the crystal cut. Kept separate " +
                "from the base so 'should a great raid pay MORE crystals or just more gold' stays " +
                "a question she can answer without re-deriving the base."),

            new TunableSpec(KeyRaidLootRepeatClearPct, TunableKind.Int, RaidLootRepeatClearPctDefault,
                "PERCENT of the ordinary wood/iron/stone/gold a REPEAT clear pays - a clear taken " +
                "while the camp's cooldown from the previous clear is still running. 60 = the " +
                "owner's ruling: '100% first clear after cooldown, 60% repeat clear during the " +
                "same cycle, then reset to 100% when the camp's cooldown expires.' Crystals are " +
                "NOT on this axis - they are all-or-nothing on the once-per-UTC-day stamp. " +
                "100 removes the repeat penalty entirely; clamped to 0..100 at the consumer, so " +
                "a repeat can never pay MORE than a first clear.",
                "NOT a PROD-022 hypothesis - the FARM-SUPPRESSION lever, and the number a player " +
                "meets as 'I won and got nothing'. It shipped as a compiled const 0.25f and " +
                "disagreed with her ruling for three days precisely because it was not a knob " +
                "(WO-1461). How hard a re-clear should bite before the cooldown expires is a " +
                "felt question about whether replay is practice or a chore."),

            new TunableSpec(KeyRaidCacheCapPerResource, TunableKind.Int, RaidCacheCapPerResourceDefault,
                "Per-resource ceiling on the RAID CACHE - the temporary hold that catches raid " +
                "loot the town bank has no room for, so a win above the cap waits instead of " +
                "being destroyed. 1800 is one perfect Camp I wood haul, i.e. the cache holds at " +
                "most one raid's worth of any one resource. Applies to the CAPPED resources only " +
                "(wood/iron/stone); crystals and gold have no ceiling and are never clamped. " +
                "0 turns the cache off and restores the pre-WO-1461 burn. Clamped to 0..1000000.",
                "NOT a PROD-022 hypothesis - and the ONE knob here whose default is NOT the " +
                "owner's own number. She ruled the MECHANIC ('never destroy raid loot because " +
                "storage is full ... a temporary Raid Cache with a modest cap') and named no " +
                "size. 1800 is a stated derivation, not a pick, and it is a knob so her number " +
                "replaces it in seconds. Too small and the cache is a slower burn; too large and " +
                "it removes the upgrade pressure the cache exists to create."),

            // -- WO-1763. PER-CAMP RAID DIFFICULTY. See the block above the key consts
            //    for why the offset is REPLACE and why the sentinel is negative.
            new TunableSpec(KeyRaidDifficultyMultPctCamp1, TunableKind.Int, RaidDifficultyMultPctCamp1Default,
                "PERCENT applied to CAMP I's authored difficultyMultiplier - the ONE number that " +
                "scales a raid defender's HP and contact damage (RaidGarrisonSpawner.FoldDifficulty " +
                "touches both and nothing else). 100 = the value scene-configs.json authors for that " +
                "camp, unchanged and bit-identical to today: the consumer short-circuits at 100 and " +
                "never round-trips the float. 160 makes that camp's guards 1.6x as tough on both " +
                "axes, BEFORE the per-level scale. Read the authored baseline off " +
                "scene-configs.json, never off a number in this string. Clamped to 25..400 at the " +
                "consumer. Applies on the NEXT raid entry; nothing already spawned is touched.",
                "NOT a PROD-022 hypothesis - the lever that did not exist. Until WO-1763 the only " +
                "way to make a camp harder was to edit a canonical JSON and ship a build, so 'is " +
                "this camp too easy' cost thirty minutes per opinion. The owner nearly 3-starred " +
                "the top camp with a level-4 hero, which is the felt evidence that the difficulty " +
                "curve needs iterating and therefore needs to be a row."),

            new TunableSpec(KeyRaidDifficultyMultPctCamp2, TunableKind.Int, RaidDifficultyMultPctCamp2Default,
                "PERCENT applied to CAMP II's authored difficultyMultiplier, on the same terms as " +
                "the Camp I knob above. 100 = today. Clamped to 25..400 at the consumer.",
                "NOT a PROD-022 hypothesis - its own row rather than one global dial, because the " +
                "defect this ticket answers is that the camps do not DIFFER enough. A single global " +
                "multiplier would move all four together and could never separate them."),

            new TunableSpec(KeyRaidDifficultyMultPctCamp3, TunableKind.Int, RaidDifficultyMultPctCamp3Default,
                "PERCENT applied to CAMP III's authored difficultyMultiplier. 100 = today. Clamped " +
                "to 25..400 at the consumer.",
                "NOT a PROD-022 hypothesis - see the Camp II knob. The step between camps is what " +
                "decides whether unlocking a harder raid reads as progress or as the same raid."),

            new TunableSpec(KeyRaidDifficultyMultPctBastion, TunableKind.Int, RaidDifficultyMultPctBastionDefault,
                "PERCENT applied to the IRON BASTION's authored difficultyMultiplier. 100 = today. " +
                "⭐ THIS IS THE KNOB THE TICKET WAS OPENED FOR: the Bastion's garrison block in " +
                "scene-configs.json is a field-for-field clone of Camp III's on every difficulty " +
                "axis, so the map's fourth and evergreen target currently fights exactly like its " +
                "third. Raising this is how the two stop being the same fight without a rebuild and " +
                "without touching the authored JSON. Clamped to 25..400 at the consumer.",
                "NOT a PROD-022 hypothesis - the direct answer to a felt report: the owner nearly " +
                "3-starred the Iron Bastion with a LEVEL-4 hero. How much harder the evergreen " +
                "target should be than the camp it was cloned from is a question only playing it " +
                "answers, and the answer will move more than once."),

            new TunableSpec(KeyRaidLevelOffsetCamp1, TunableKind.Int, RaidLevelOffsetCamp1Default,
                "REPLACES CAMP I's authored levelOffset - how far ABOVE the player's own level that " +
                "camp's defenders are rolled. The spawner takes max(baseEnemyLevel, playerLevel + " +
                "offset), so baseEnemyLevel still floors a low-level hero's raid and this only ever " +
                "raises the ceiling. -999 is the SENTINEL meaning 'use the value " +
                "scene-configs.json authors', i.e. today exactly; DELETING the row is the same " +
                "thing and is the table's documented resting state. It REPLACES rather than ADDS, " +
                "so the number you type is the offset. Any non-sentinel value is clamped -5..20 at " +
                "the consumer. Applies on the NEXT raid entry.",
                "NOT a PROD-022 hypothesis - the second of the two difficulty axes, and the one " +
                "that bites hardest late: level scale adds ~8% HP and up to ~4% contact damage per " +
                "level on top of the multiplier. Kept separate from the multiplier because 'more " +
                "levels' and 'tougher for its level' feel completely different to fight and the " +
                "owner needs to be able to move one without the other."),

            new TunableSpec(KeyRaidLevelOffsetCamp2, TunableKind.Int, RaidLevelOffsetCamp2Default,
                "REPLACES CAMP II's authored levelOffset, on the same terms as the Camp I knob " +
                "above. -999 = use the authored value (today). Clamped -5..20 otherwise.",
                "NOT a PROD-022 hypothesis - see the Camp I offset knob. Per camp rather than " +
                "global, for the same reason the multipliers are."),

            new TunableSpec(KeyRaidLevelOffsetCamp3, TunableKind.Int, RaidLevelOffsetCamp3Default,
                "REPLACES CAMP III's authored levelOffset. -999 = use the authored value (today). " +
                "Clamped -5..20 otherwise.",
                "NOT a PROD-022 hypothesis - see the Camp I offset knob."),

            new TunableSpec(KeyRaidLevelOffsetBastion, TunableKind.Int, RaidLevelOffsetBastionDefault,
                "REPLACES the IRON BASTION's authored levelOffset. -999 = use the authored value " +
                "(today). ⭐ The second half of the Bastion pair: with the multiplier knob it is " +
                "what separates the evergreen target from the camp it was cloned from. REPLACE, " +
                "NOT ADD - the number you type is the offset, not a delta on the authored one. " +
                "Clamped -5..20 otherwise.",
                "NOT a PROD-022 hypothesis - the axis the felt report actually named. A level-4 " +
                "hero met the Bastion's authored offset and nearly cleared it; raising the offset " +
                "is how a high-level hero keeps meeting a fight rather than a formality, and it is " +
                "the knob most likely to be re-tuned several evenings in a row."),

            // -- WO-1773. TOWN WAVE DIFFICULTY + THE TOWN-REGEN COMBAT GATE. See the block
            //    above the key consts for the two dead steps these answer, and note that the
            //    regen gate is the PREREQUISITE: a roster ten times heavier still cannot move a
            //    bar that refills faster than it drains.
            new TunableSpec(KeyTownRegenSuppressSecondsAfterHit, TunableKind.Int, TownRegenSuppressSecondsAfterHitDefault,
                "SECONDS after the hero last actually LOST HP during which the town safe-zone regen " +
                "stays OFF - the out-of-combat rule. 0 = today: the regen has no combat check at all " +
                "and runs mid-wave with an enemy in melee contact. This is the knob WO-1773 names as " +
                "the PREREQUISITE for every other difficulty change, because the regen is a fraction " +
                "of MaxHp per second and therefore scales with gear while incoming damage is capped " +
                "by the wave curve - the gap widens with every upgrade and can never be outgrown. " +
                "The between-waves top-up and the FTUE 1-HP recovery both survive this shape with no " +
                "carve-out: stop being hit, the timer runs out, HP comes back. Clamped 0..120 at the " +
                "consumer, and the first-time tutorial is never gated at all.",
                "NOT a PROD-022 hypothesis - the direct answer to a felt report from the external " +
                "tester: 'at the level he is nothing can damage him he can just stand there'. How " +
                "many seconds out of combat should buy a top-up is a feel question that will be " +
                "re-tuned several evenings in a row, and every answer would otherwise cost a build."),

            new TunableSpec(KeyTownRegenPctDuringWave, TunableKind.Int, TownRegenPctDuringWaveDefault,
                "PERCENT of the normal town regen rate that runs while a town wave is ACTIVE. 100 = " +
                "today, bit for bit - the consumer short-circuits at 100 and never round-trips the " +
                "fraction. 0 stops town regen for the whole wave. It COMPOSES with the suppression " +
                "window above, and the window WINS: a hero being hit right now is in combat whatever " +
                "the wave phase says. Clamped 0..100 - above 100 is refused on purpose, because a " +
                "rate that is HIGHER in a wave than between waves is the opposite of the defect. " +
                "The first-time tutorial is never gated.",
                "NOT a PROD-022 hypothesis - the second shape WO-1773 offered the owner for the same " +
                "dead step. Kept alongside the timer rather than instead of it because 'no healing " +
                "during a siege' and 'no healing for six seconds after a hit' feel completely " +
                "different to play, and which one is right is not knowable from source."),

            new TunableSpec(KeyWaveHpGrowthPctPerWave, TunableKind.Int, WaveHpGrowthPctPerWaveDefault,
                "HUNDREDTHS of enemy HP multiplier ADDED per wave beyond the point where " +
                "WaveScalingCurve stops growing on its own. 5 means +0.05 on the multiplier every " +
                "wave past the band. 0 = today: the curve's post-wrap mode is Clamp, so every wave " +
                "past its last keyframe evaluates identically and wave 176 is numerically wave 60. " +
                "ADDITIVE on the clamped multiplier, NOT multiplicative - the two diverge hard at " +
                "high waves and the ticket's arithmetic table, which is what a seed gets picked off, " +
                "is the additive column. The band start is read off the curve's last keyframe, never " +
                "written down. Clamped 0..100 per wave, with an absolute ceiling on the effective " +
                "multiplier because the wave number itself has no clamp.",
                "NOT a PROD-022 hypothesis - the owner's own direction: 'enemies need to really " +
                "start scaling with wave i thought'. The shape is deliberately the DRAGON's: its HP " +
                "and damage already grow linearly and uncapped per return while the entire regular " +
                "roster is clamped, so this applies a pattern the codebase already has to the rest " +
                "of the wave. Separate from the damage knob because more HP and more hurt are " +
                "different feels."),

            new TunableSpec(KeyWaveDmgGrowthPctPerWave, TunableKind.Int, WaveDmgGrowthPctPerWaveDefault,
                "HUNDREDTHS of enemy CONTACT-DAMAGE multiplier ADDED per wave beyond the curve's " +
                "clamp band, on the same additive terms as the HP knob above. 0 = today. This is " +
                "the axis that decides whether a high-level hero can stand still: the ticket's " +
                "arithmetic puts a single heavy melee enemy at parity with the town regen somewhere " +
                "around 5 and clearly ahead of it by 10 - but ONLY once the regen gate above is " +
                "seeded, because until then every row up to 5 still reads as a net heal. Clamped " +
                "0..100 per wave.",
                "NOT a PROD-022 hypothesis - the half of the owner's scaling direction that is " +
                "actually about threat. Scaling HP alone lengthens fights without adding danger, " +
                "which is the least interesting way to make a game harder, so the two knobs are " +
                "deliberately independent and will not be tuned to the same number."),

            new TunableSpec(KeyWaveMaxCountPct, TunableKind.Int, WaveMaxCountPctDefault,
                "PERCENT on the authored ROSTER CEILING - how many bodies a single wave may field in " +
                "total. 100 = today, and today that ceiling binds from wave 21 onward, so a wave-176 " +
                "roster is the same size as a wave-21 one. This is the CHEAP half of the owner's " +
                "'massive swarms at all sides': the roster is drained through the concurrency budget " +
                "as reinforcements, so a bigger roster costs arrival TIME, not frame time. Clamped " +
                "25..1000 at the consumer, and floored at one body so a low percent can never " +
                "produce an empty wave, which the clear logic would read as already cleared.",
                "NOT a PROD-022 hypothesis - the owner asked for swarms and the spawn side already " +
                "uses all four gates from wave 10, so the missing ingredient is volume rather than " +
                "coverage. Whether a longer, denser siege reads as epic or as tedious is a felt " +
                "question and the answer will move."),

            new TunableSpec(KeyWaveCountCapPct, TunableKind.Int, WaveCountCapPctDefault,
                "PERCENT on the authored ENDLESS COUNT CAP - the multiplier the endless mode applies " +
                "to a wave's generated roster once the authored schedule runs out. 100 = today, and " +
                "this is the cap that binds FIRST in practice: WO-1773 measured it binding from wave " +
                "60, a hundred and sixteen waves before the tester's run, which is most of why wave " +
                "176 and wave 60 are the same fight. Raising it is the single largest change in " +
                "roster size available. Clamped 25..1000 at the consumer; an authored cap of 0 means " +
                "uncapped and is handed back untouched.",
                "NOT a PROD-022 hypothesis - the numerically dominant half of dead step B. It is a " +
                "percent rather than a replacement so no number from waves.json is restated on the " +
                "rail, which is the stale-copy failure CLAUDE.md keeps recording."),

            new TunableSpec(KeyWaveMaxSimultaneousPct, TunableKind.Int, WaveMaxSimultaneousPctDefault,
                "PERCENT on the ON-SCREEN CONCURRENCY CAP - how many wave enemies may be alive at " +
                "once, split across the four gates. 100 = today. (!) THIS IS A PHONE FRAME BUDGET, " +
                "NOT A DIFFICULTY DIAL, and it is the one knob in this family that can regress the " +
                "Seeker build's frame rate: the spawner partitions a single budget across sides " +
                "precisely so N gates can never exceed the one-gate cap, after the WO-1113 phone " +
                "frame-rate cliff. Raising it requires measured Perf scope evidence naming the " +
                "dominant cost in milliseconds, before and after, on the device. Clamped 25..400 - " +
                "tighter than the roster knobs for that reason. An authored 0 means UNCAPPED and is " +
                "handed back as 0; a real cap never rounds down to 0, which would read as uncapped.",
                "NOT a PROD-022 hypothesis - the EXPENSIVE half of 'massive swarms at all sides'. It " +
                "is on the rail so the owner can find the real device ceiling by playing rather than " +
                "by a rebuild per guess, and so a bad value is one row-edit away from being undone " +
                "on a build that is already in a tester's hands."),

            new TunableSpec(KeyRaidStarterArmySize, TunableKind.Int, RaidStarterArmySizeDefault,
                "TOTAL free troops a save receives the first time it has a Barracks. 10 = the " +
                "owner's number (WO-1803, 2026-09-16), and the consumer splits it EVEN between " +
                "Footmen and Archers with the remainder to Footmen - so the shipping squad is 5 " +
                "Footmen + 5 Archers and no value here can produce a squad of one unit type. " +
                "Granted once per save and never again, so a demolished-and-rebuilt Barracks is " +
                "not a troop faucet. 0 disables the grant. Clamped at the consumer to " +
                "0..ArmyStorage.DefaultMaxArmySize (10) - the fresh-save army housing cap, read " +
                "off that const rather than a literal so the two cannot drift.",
                "NOT a PROD-022 hypothesis - the FTUE lever the map opens with (section 2): 'A " +
                "player starts with 200 gold but needs 1,650 to participate in the thing you're " +
                "trying to teach them. That's basically putting a nightclub behind a velvet rope " +
                "and handing the player twelve cents.' Three was that lever's first setting and " +
                "the owner's verdict on it was 'that's shit': ten is a squad that can actually " +
                "take the 9-defender first camp, and the 5/5 split is the first thing in the game " +
                "that teaches composition. Whether ten is right is a felt question about the " +
                "first ten minutes of the game - the single most expensive ten minutes to get " +
                "wrong and the most expensive to iterate on with a rebuild."),

            new TunableSpec(KeyRaidHeartfireMaxCharges, TunableKind.Int, RaidHeartfireMaxChargesDefault,
                "How many Heartfire charges the Heart can hold at once. Ships at 3 (canon): a " +
                "charge is spent to march on a camp, one rekindles every four hours, and they " +
                "STACK to the ceiling so a player who sleeps or works is not punished. Clamped " +
                "to 1..9 at the consumer - Heartfire is never unbounded and never zero.",
                "NOT a PROD-022 hypothesis - the pacing lever the raid loop is gated by. Three " +
                "charges is the owner's number and the question it answers is felt, not " +
                "arithmetic: whether a player returning after a night away gets a satisfying " +
                "session or runs dry mid-evening. It is deliberately a knob because the answer " +
                "cannot be derived, only played."),

            new TunableSpec(KeyRaidHeartfireRegenSeconds, TunableKind.Int, RaidHeartfireRegenSecondsDefault,
                "Seconds for one Heartfire charge to rekindle. Ships at 14400 (4 h). Floored at " +
                "60 s at the consumer, because a non-positive interval would make Heartfire " +
                "infinite and delete the gate entirely.",
                "NOT a PROD-022 hypothesis. It ships EQUAL to the shortest authored per-camp " +
                "cooldown (raider_camp_small, 14400 s) on purpose, which is what keeps the " +
                "three-gate stack honest: a rekindled charge always has at least one recovered " +
                "camp to spend on. Raising it above that shortest cooldown breaks the criterion " +
                "'a player holding Heartfire always has somewhere to spend it', and " +
                "HeartfireRegression goes red when it does."),

            new TunableSpec(KeyEconomyPackTemporaryBuilderSeconds, TunableKind.Int, EconomyPackTemporaryBuilderSecondsDefault,
                "Seconds of extra Builder crew ONE 'temporary-builder' pack charge (the $1.99 Builder's " +
                "Hour, WO-1388) grants. Ships at 21600 (6 h). A charge bought while a window is running " +
                "is DEFERRED behind it and starts when it ends - never stacked, never burned. 0 refuses " +
                "the grant and keeps the charge deferred rather than spending it on nothing.",
                "NOT a PROD-022 hypothesis - the one lever on the cheapest micro-transaction, which " +
                "exists because the store has sold nothing (owner, 2026-09-04: 'we have 0 sales'). " +
                "Whether six hours is the number that turns a first tap into a habit is felt, not " +
                "derived, and a rebuild per opinion is the wrong price for finding out."),

            new TunableSpec(KeyHudNightMarketGlowLapSec, TunableKind.Int, HudNightMarketGlowLapSecDefault,
                "Seconds for the Night Market card's comets to make ONE lap of the card's perimeter " +
                "(WO-1384b). Ships at 5. Clamped to 1..60 at the consumer - never frozen, never a blur. " +
                "Read when the HUD builds the card; the animator reads the value every frame.",
                "NOT a PROD-022 hypothesis - a FEEL knob on the store's permanent HUD face. Whether a " +
                "five-second lap reads as alive or as nagging on a phone in a dark room is a felt " +
                "question (owner 2026-09-02: 'make it tweakable from a db call'), and the rebuild per " +
                "opinion is the wrong price for it."),

            new TunableSpec(KeyHudNightMarketGlowAlphaPct, TunableKind.Int, HudNightMarketGlowAlphaPctDefault,
                "Peak alpha, in percent, of the Night Market card's ring and comet heads (WO-1384b). " +
                "Ships at 35 - a rim light, not a spotlight. Clamped to 0..100 at the consumer; 0 " +
                "keeps the card and hides the glow entirely.",
                "NOT a PROD-022 hypothesis - the brightness half of the same feel question. The card " +
                "must stand out from the Heart plate above it without out-shouting the action bar; " +
                "the owner is colourblind, so contrast is judged on device, never from a palette."),

            new TunableSpec(KeyHudNightMarketGlowPaletteMask, TunableKind.Int, HudNightMarketGlowPaletteMaskDefault,
                "Bitmask of the warm palette stops the glow drifts through (WO-1384b): Gold=1, Amber=2, " +
                "Rose=4. Ships at 7 (all three, gold -> amber -> rose -> gold). Clamped to 0..7 at the " +
                "consumer; 0 or any single bit holds one steady colour, and an empty mask resolves to " +
                "Gold alone (logged once), never to nothing.",
                "NOT a PROD-022 hypothesis - 'take that colour out of the cycle' as one number, no " +
                "code change, no schema change, on the same integer rail as the WO-1343 aura mask."),

            new TunableSpec(KeyArenaWagerTier1, TunableKind.Int, ArenaWagerTier1Default,
                "Crystals staked to fight the tier-1 Arena opponent (Ironhold Marauders, WO-1366). " +
                "Ships at 50 - exactly what ArenaCatalog.cs hardcoded. Clamped to 1..100000 at " +
                "ArenaWagerTunables; a wager of 0 would make the Arena free, so 1 is the floor.",
                "NOT a PROD-022 hypothesis - a PRICE. With Crystals bought on Google Play the wager " +
                "is real money at one remove, and the three amounts were authored against a free " +
                "500-seed stub. Whether 50 reads as a flutter or a gamble is felt on device, not derived."),

            new TunableSpec(KeyArenaWagerTier2, TunableKind.Int, ArenaWagerTier2Default,
                "Crystals staked to fight the tier-2 Arena opponent (Grimwatch Reavers, WO-1366). " +
                "Ships at 100. Clamped to 1..100000 at ArenaWagerTunables.",
                "NOT a PROD-022 hypothesis - the middle rung of the same price ladder. Its own knob " +
                "rather than a multiple of tier 1 because the step between tiers is what decides " +
                "whether a harder fight reads as a step up or as a wall."),

            new TunableSpec(KeyArenaWagerTier3, TunableKind.Int, ArenaWagerTier3Default,
                "Crystals staked to fight the tier-3 Arena opponent (Blackbanner Host, WO-1366). " +
                "Ships at 200. Clamped to 1..100000 at ArenaWagerTunables.",
                "NOT a PROD-022 hypothesis - the top of the wager ladder, and the one amount a " +
                "player must actually have bought Crystals to reach. Set by feel, moved by a row."),

            new TunableSpec(KeyArenaWinPursePct, TunableKind.Int, ArenaWinPursePctDefault,
                "The purse an Arena WIN pays, as a PERCENT of the wager (WO-1366). Ships at 200 - " +
                "stake back plus the same again, exactly the 2x ArenaCatalog.cs hardcoded. Clamped " +
                "to 100..1000 at ArenaWagerTunables: 100 returns only the stake, and below that a " +
                "win would lose money, which the clamp refuses.",
                "NOT a PROD-022 hypothesis - the house edge as one number. Whether 2x is generous " +
                "enough to make a Crystal wager feel worth the risk, or so generous the Arena becomes " +
                "a Crystal faucet, is the balance question this row answers without a rebuild."),

            new TunableSpec(KeyHeroPlayableFallbackHalf, TunableKind.Int, HeroPlayableFallbackHalfDefault,
                "Half-extent, in metres, of the hero's off-mesh playable-bounds clamp in a scene " +
                "whose world extent cannot be MEASURED from a Terrain (WO-1094). Ships at 50 - " +
                "EXACTLY the hardcoded bound this build replaced, so an empty table reproduces " +
                "today's behaviour in every unmeasured scene. Where a Terrain IS present the " +
                "MEASURED extent wins and this row is never read. Clamped 1..100000 at the " +
                "consumer, so a typo of 0 cannot pin the hero to the origin.",
                "NOT a PROD-022 hypothesis - the honest floor under a derived bound. Three shipped " +
                "scenes carry no Terrain (Village2, RaidBase_*, legacy MainCastle_Hall), and there " +
                "a purely-derived clamp is NO clamp: the hero drifts unbounded on the off-mesh " +
                "transform fallback, inside the raid loop. Whether 50 is still the right box for a " +
                "raid base - it was authored for the old castle - is a felt question, and a row."),

            new TunableSpec(KeyRaidStagingCeilingSeconds, TunableKind.Int, RaidStagingCeilingSecondsDefault,
                "Raises the absolute wall-clock ceiling on raid STAGING before the stranding " +
                "watchdog routes the player home.",
                "WO-1095 - staging is free on the raid clock, so only a dead session should trip " +
                "a wall-clock bound."),

            new TunableSpec(KeyRaidHonorThirdStarSeconds, TunableKind.Int, RaidHonorThirdStarSecondsDefault,
                "Elapsed RAID seconds after which the THIRD honor star goes dark (WO-1594) - the " +
                "speed honor. Ships at 90, which is half of the 180 s raid clock and EXACTLY the " +
                "value RaidScoring hardcoded. The clock is engagement-gated (WO-1520), so staging " +
                "never spends it. Clamped to at least 1 at the consumer, which also answers this " +
                "default outright while the key has no spec - a 0 here would snuff the third star " +
                "on the first frame of every raid.",
                "NOT a PROD-022 hypothesis - the PACE OF A RAID as one number, and the owner's " +
                "ruling 2026-09-09, verbatim: 'Tunables with those defaults'. How long a player " +
                "may take before the fight stops reading as fast is felt on device, and it moves " +
                "the payout as well as the bar: Finalize clamps loot to min(settle, honor)."),

            new TunableSpec(KeyRaidHonorSecondStarSeconds, TunableKind.Int, RaidHonorSecondStarSecondsDefault,
                "Elapsed RAID seconds after which the SECOND honor star may go dark (WO-1594) - " +
                "and only if destruction is still under the D2 threshold below. Ships at 150: the " +
                "last 30 s before a 180 s timeout, exactly what RaidScoring hardcoded. Clamped to " +
                "at least 1 at the consumer. The FIRST star is never snuffed mid-fight for time " +
                "alone - cracking the camp always pays something - and no row can change that.",
                "NOT a PROD-022 hypothesis - the second half of the same pacing question. Set it " +
                "above the clock to retire the milestone entirely without a rebuild."),

            new TunableSpec(KeyRaidHonorSecondStarMinDestructionPct, TunableKind.Int,
                RaidHonorSecondStarMinDestructionPctDefault,
                "Destruction PERCENT a camp must have reached by T2 to KEEP the second honor star " +
                "(WO-1594). Ships at 50 - the 0.50f RaidScoring hardcoded. It is a percent because " +
                "the rail is integer-only; the consumer clamps 0..100 and divides by 100, so 100 " +
                "means only a total razing keeps the star and 0 means the milestone never bites.",
                "NOT a PROD-022 hypothesis - the 'are you actually making progress' half of the T2 " +
                "milestone. Whether half a camp is the right bar at 150 s is a felt question about " +
                "how punishing a slow raid should be, and now a row instead of a rebuild."),

            new TunableSpec(KeyRaidRoughStoneMinTier, TunableKind.Int, RaidRoughStoneMinTierDefault,
                "Lowest raid camp TIER (1..4 on the RaidLootTunables Camp I..Iron Bastion ladder) " +
                "that may drop a rough stone (WO-1373). Ships at 3 - the LOWER of the top two " +
                "rungs - so mage_enclave and iron_bastion drop and raider_camp_small / " +
                "fortified_garrison do not. 2 lets the Broken Garrison in; 5 turns the raid drop " +
                "off entirely and restores the pre-WO-1373 build. Clamped 1..99 at the consumer.",
                "NOT a PROD-022 hypothesis - the owner's ruling 2026-09-09, verbatim: 'only top " +
                "two tiers of raids can drop stone'. Which rung counts as 'top' is the one part " +
                "of that sentence a fifth camp would change, so it is a row rather than a list."),

            new TunableSpec(KeyRaidRoughStonePerDayCap, TunableKind.Int, RaidRoughStonePerDayCapDefault,
                "How many rough stones EVERY raid together may pay inside one UTC day (WO-1373). " +
                "Ships at 1 - the owner's 'no more than 1 per day'. This is a GLOBAL count, not " +
                "per-camp: clearing both eligible camps on the same day pays one stone, not two. " +
                "0 turns the raid drop off without touching the tier row. Clamped 0..99 at the " +
                "consumer; the ledger is a UTC day key plus a count, so it self-expires.",
                "NOT a PROD-022 hypothesis - the FAUCET BOUND on the only material the Jeweler " +
                "chain consumes. Whether one a day is generous or stingy is exactly the kind of " +
                "felt call that used to cost a rebuild, and it decides how fast the ring ladder " +
                "climbs."),

            new TunableSpec(KeyDungeonRoughStoneDropPct, TunableKind.Int, DungeonRoughStoneDropPctDefault,
                "PERCENT chance a COMPLETED, non-starter dungeon run pays a rough stone once the " +
                "player has already earned their first one (WO-1373). Ships at 5, the owner's " +
                "ruling. NOTE - THE BUILD SHIPPED 15 (DungeonController.PostFirstRoughStoneDropRate = " +
                "0.15f), so 15 restores the previous behaviour exactly. STARTER dungeons (layout " +
                "tier 1) are excluded by a separate tier gate, not by this number. The guaranteed " +
                "FIRST stone is not on this axis and still cannot be rolled away. Clamped 0..100.",
                "NOT a PROD-022 hypothesis - the dungeon half of the same faucet, and the number " +
                "that decides whether a delve reads as worth the lantern oil. Kept on its own row " +
                "from the raid cap so 'should dungeons still be the better source' stays a " +
                "question she can answer by moving one value."),

            // -- WO-1805 LANE C. THE DUNGEON DARKNESS. See the block above the key consts for
            //    why the drain is an integer x100 and why a row at its default means "not set".
            new TunableSpec(KeyDungeonLanternDrainPerSecX100, TunableKind.Int,
                DungeonLanternDrainPerSecX100Default,
                "Oil burned per second by the dungeon lantern, TIMES 100 (the rail carries no " +
                "floats). Ships at 50 = 0.50/s = 200 s per flask, which is exactly what " +
                "dungeon-balance.json authors today, so an empty table burns identically. 33 gives " +
                "~300 s, 25 gives 400 s. THE duration knob: maxOil is deliberately NOT on the rail " +
                "because it is the meter's 100 % and the denominator of the low-oil 0.25, " +
                "darkness-latch 0.12 and min-light 0.35 fractions - moving it silently moves three " +
                "systems. A row at 50 leaves dungeon-balance.json as the single authority; only a " +
                "DEVIATION overrides it (Lantern.ApplyBalanceData). Floored at 0.01/s.",
                "NOT a PROD-022 hypothesis - the owner's report 2026-09-16, verbatim: 'nobody " +
                "understands why the torch runs out and why just become suddenly dark'. Lane A " +
                "teaches the mechanic; this row is how she answers whether 200 s of light for an " +
                "eleven-room dungeon is fair, on device, without a ten-minute rebuild per opinion."),

            new TunableSpec(KeyDungeonLanternOilStoneRefillPct, TunableKind.Int,
                DungeonLanternOilStoneRefillPctDefault,
                "PERCENT of a full flask one authored oil stone returns. Ships at 100 - a cache " +
                "TOPS the flask, arithmetically identical to what Lantern.CheckOilStones has always " +
                "done from any starting level. Each stone is still ONE USE per visit, which this row " +
                "cannot change. Lowering it is the sharpest way to make caches feel scarce without " +
                "touching the burn; dg_starter_loop authors ONE stone for ELEVEN rooms, so this row " +
                "and the drain together set that dungeon's whole light budget. Clamped 1..100.",
                "NOT a PROD-022 hypothesis - the second half of the recourse question in WO-1805. " +
                "Whether finding a cache should feel like a full reprieve or a partial one is a felt " +
                "call, and it used to be a compiled assignment."),

            new TunableSpec(KeyDungeonLanternStillRefillPct, TunableKind.Int,
                DungeonLanternStillRefillPctDefault,
                "PERCENT of a full flask one EMERGENCY FIELD DISTILLATION returns - the one-use " +
                "still that spends 1 Oil Flask + 1 Tattered Cloth. Ships at 40, exactly " +
                "ComposedOilStill.RefillFraction 0.40f, i.e. 80 s at today's drain. 100 makes a " +
                "flask-plus-cloth a full refill, which is the cheapest single lever on 'they can " +
                "actually get more materials and use it' short of new content. ⚠ The still is still " +
                "attached 1:1 to each oil stone (WO-1805 Lane B, SPEC), so raising this does not " +
                "give the player MORE places to distil - only a bigger payoff where one exists. " +
                "Clamped 1..100.",
                "NOT a PROD-022 hypothesis - the owner's 'we need a mechanic, they can actually get " +
                "more materials and use it' (2026-09-16), as far as a knob can carry it. The rest of " +
                "that sentence needs new seams and stays a spec."),

            new TunableSpec(KeyDungeonLanternFinalWarningSec, TunableKind.Int,
                DungeonLanternFinalWarningSecDefault,
                "SECONDS before empty at which the flame begins its final visible collapse: the " +
                "light range falls to the 1.35 u safety halo, the wick flutters, and a LINEAR FOG " +
                "WALL closes from the scene's own distances to 0.45..3.2 m. Ships at 30 - " +
                "Lantern.DefaultFinalWarningSeconds - so an empty table collapses exactly as this " +
                "build does. ⚠ THIS IS THE 'SUDDENLY DARK' KNOB: raise it and the same collapse is " +
                "spread over a longer, readable ramp; 0 removes the ramp and the dark arrives at " +
                "empty with no warning at all. A row at 30 leaves dungeon-balance.json and the " +
                "serialized field alone; only a deviation overrides them. Floored at 0.",
                "NOT a PROD-022 hypothesis - it is the owner's word 'suddenly' as a number. The 0.35 " +
                "min-light floor does NOT protect her (the collapse writes 1.35 u behind a 3.2 m fog " +
                "wall on top of it), and this row is the one that decides how much warning the " +
                "player gets before the ambush multiplier arms."),

            // -- WO-1348. THE VFX PICKS. See the block above the key consts for why the
            //    value is a STABLE OPTION ID and why 0 must stay "the build-time pick".
            new TunableSpec(KeyRealmVfxTreeFootprintAura, TunableKind.Int, VfxPickBuildDefaultId,
                "OPTION ID for the looping aura at the foot of the world tree. 0 = the build-time " +
                "pick from Assets/Editor/VfxManualPicks.json, i.e. today's behaviour exactly. Any " +
                "other value names an entry in the shipped option pool " +
                "(Assets/Resources/VFX/vfx-pick-options.generated.json) and means 'render this key " +
                "with the prefab THAT key already renders with'. An id the build does not carry, or " +
                "one whose loop-ness does not match, FALLS BACK to the build-time pick and says so " +
                "in the trace. Applies on the next TOWN LOAD; nothing already spawned is touched. " +
                "⚠ THE SITE IS CURRENTLY WITHHELD (AmbientAuraPolicy.WithholdTreeFootAura, " +
                "AmbientAuraPolicy.cs:129, her 2026-09-07 ruling), so nothing spawns at the tree " +
                "foot whatever this row says - the pick simply STANDS until that flag flips.",
                "NOT a PROD-022 hypothesis - the first of the four tags the owner could not correct " +
                "on 2026-09-03 without a thirty-minute rebuild. This is the knob that turns a retag " +
                "from a rebuild into a forty-second edit."),

            new TunableSpec(KeyRealmVfxTreeFootprintImpact, TunableKind.Int, VfxPickBuildDefaultId,
                "OPTION ID for the impact burst at the foot of the world tree. THIS KEY HAS NO " +
                "BUILD-TIME ENTRY - it is absent from Assets/Editor/VfxManualPicks.json, so at 0 it " +
                "renders NOTHING, exactly as it does today. Setting an id CREATES the pick, " +
                "borrowing the option's prefab, loop-ness, scale and lifetime. Key CREATION, not " +
                "only key override, is a stated acceptance criterion of WO-1348. ⛔ NO CODE CALLS " +
                "THIS KEY EITHER (verified 2026-09-10: zero call sites), so a pick is not visible " +
                "until a call site is wired - the Command Center card says exactly that.",
                "NOT a PROD-022 hypothesis - the proof that the feature can give her a tag she does " +
                "NOT already have. A design that could only override an existing key would lose " +
                "half the reason this exists."),

            new TunableSpec(KeyRealmVfxEliteDeathImpact, TunableKind.Int, VfxPickBuildDefaultId,
                "OPTION ID for the elite-death burst. 0 = the build-time pick, unchanged. ⛔ NO CODE " +
                "PLAYS THIS KEY TODAY (verified 2026-09-10: zero call sites in Assets/_Modules) - an " +
                "elite death goes through HeldVfxKeys.BossDeath, per her own ruling 'both get " +
                "Elite_Death, name it BossDeath_Impact'. A row here therefore changes NOTHING on " +
                "screen until an elite is given its own call, and the Command Center card says so in " +
                "words. Same pool and same next-town-load timing as the other three.",
                "NOT a PROD-022 hypothesis - the slot kept READY for the day elite and boss deaths " +
                "are pulled apart. Registered rather than omitted because the WO names it; NOT " +
                "re-pointed at a live key, because the CLI never makes a creative pick."),

            new TunableSpec(KeyRealmVfxBossDeathImpact, TunableKind.Int, VfxPickBuildDefaultId,
                "OPTION ID for the boss-death burst. 0 = the build-time pick, unchanged. Same " +
                "shipped-by-construction pool and same next-town-load timing as the other three. " +
                "⭐ THE ONE OF THE FOUR THAT IS LIVE: EliteVFXController.cs:326 plays it through " +
                "HeldVfxKeys.BossDeath on every isBoss death, so a row here IS visible on the next " +
                "town load. DragonBoss (Syndrath) has its own Die() and is NOT covered.",
                "NOT a PROD-022 hypothesis - the boss death she was still hunting a tag for when " +
                "the evening ran out. A creative loop whose iteration cost is thirty minutes is a " +
                "creative loop she stops running."),
        };

        // Swapped atomically by ApplyPayload. Never mutated in place.
        private static Dictionary<string, string> s_remote;
        private static string s_provenance = ProvenanceDefault;

        /// <summary>Where the standing table came from: "default" | "remote" | "remote-cached".</summary>
        public static string TableProvenance => s_provenance;

        /// <summary>True once any payload (live or cached) has been accepted.</summary>
        public static bool Loaded => s_remote != null;

        /// <summary>Keys in the standing table. 0 means every knob answers its default.</summary>
        public static int RowCount => s_remote == null ? 0 : s_remote.Count;

        /// <summary>Bumped on every accepted payload. Lets a reader see the config change mid-session.</summary>
        public static int Generation { get; private set; }

        // =====================================================================
        //  READ SIDE - always answers, never throws, ABSENCE => the DEFAULT.
        // =====================================================================

        /// <summary>Find a knob's contract, or null for an unregistered key (a caller bug).</summary>
        public static TunableSpec SpecFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < Registry.Length; i++)
                if (string.Equals(Registry[i].Key, key, StringComparison.Ordinal))
                    return Registry[i];
            return null;
        }

        /// <summary>
        /// Resolve a BOOL knob. NEVER throws. An unregistered key, an absent row, an
        /// unparseable value and an unreachable server ALL resolve to the shipping default.
        /// </summary>
        public static bool Bool(string key) => Int(key) != 0;

        /// <summary>
        /// Resolve an INT knob. NEVER throws, and the failure answer is always the default.
        /// <para>
        /// Traced ONCE per key per provenance-and-value, so a capture states which value was
        /// used AND where it came from without a reader having to guess (CLAUDE.md section 12).
        /// Re-traced when a later payload changes the answer, because a knob that changed
        /// mid-session is precisely the thing a felt-test needs to see.
        /// </para>
        /// </summary>
        public static int Int(string key)
        {
            var spec = SpecFor(key);
            if (spec == null)
            {
                // A key nobody registered is a CALLER bug, not a data problem. Say so loudly
                // and answer 0 - there is no default to fall back to because there is no knob.
                FlowTrace.Once(Sys, "unregistered:" + key,
                    "UNREGISTERED tunable key '" + (key ?? "null") + "' was read. There is no spec and " +
                    "therefore no default; answering 0. Add it to RemoteTunables.Registry and to " +
                    "docs/PROD022_TUNABLE_FLAGS.md in the same commit.");
                return 0;
            }

            int value = spec.Default;
            string provenance = ProvenanceDefault;

            // (1) REMOTE. The owner at the database.
            var table = s_remote;
            if (table != null && table.TryGetValue(spec.Key, out string raw))
            {
                if (TryParseValue(raw, spec, out int parsed))
                {
                    value = parsed;
                    provenance = s_provenance == ProvenanceCache ? ProvenanceCache : ProvenanceRemote;
                }
                else
                {
                    // Malformed row. It does NOT poison the knob - it falls to the default and
                    // says so. Throttled rather than Once: the row can be corrected live, and a
                    // reader needs to see that the bad value is STILL there.
                    FlowTrace.Throttle(Sys, "badvalue:" + spec.Key, 30f,
                        "row '" + spec.Key + "' carries an unusable value '" + Flatten(raw) + "' for kind " +
                        spec.Kind + " - IGNORED, this knob resolves to its shipping default " +
                        Describe(spec, spec.Default) + ". Fix the row; nothing is broken meanwhile.");
                }
            }

            // (2) LOCAL PlayerPrefs. The human at the device. Most specific, so it wins last.
            int local = ReadLocalOverride(spec);
            if (local != int.MinValue)
            {
                value = local;
                provenance = ProvenanceLocal;
            }

            FlowTrace.Once(Sys, "resolve:" + spec.Key + "=" + value + "@" + provenance,
                "KNOB " + spec.Key + " = " + Describe(spec, value) + "  provenance=" + provenance +
                "  (shipping default " + Describe(spec, spec.Default) + ", generation=" + Generation + "). " +
                (provenance == ProvenanceDefault
                    ? "No database row and no local override - this is TODAY'S BEHAVIOUR, unchanged."
                    : "This is an OVERRIDE of the shipping default."));

            return value;
        }

        /// <summary>
        /// PlayerPrefs override for one knob, or <c>int.MinValue</c> when absent.
        /// Guarded: PlayerPrefs on a hardened WebGL host can throw on access, and a
        /// diagnostic knob must never be the thing that takes the app down.
        /// </summary>
        private static int ReadLocalOverride(TunableSpec spec)
        {
            const int absent = int.MinValue;
            return Guard.Try(Sys, "read local override " + spec.Key, () =>
            {
                int v = UnityEngine.PlayerPrefs.GetInt(LocalPrefix + spec.Key, absent);
                if (v == absent) return absent;
                if (spec.Kind == TunableKind.Bool) return v != 0 ? 1 : 0;
                return v;
            }, absent);
        }

        /// <summary>Parse one wire value. Accepts 0/1 and true/false for bools.</summary>
        private static bool TryParseValue(string raw, TunableSpec spec, out int value)
        {
            value = 0;
            if (raw == null) return false;
            string s = raw.Trim();
            if (s.Length == 0) return false;

            if (spec.Kind == TunableKind.Bool)
            {
                if (s.Equals("1", StringComparison.Ordinal) ||
                    s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("on", StringComparison.OrdinalIgnoreCase)) { value = 1; return true; }
                if (s.Equals("0", StringComparison.Ordinal) ||
                    s.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("off", StringComparison.OrdinalIgnoreCase)) { value = 0; return true; }
                return false;
            }

            return int.TryParse(s, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Human wording for a value: ON/OFF for bools, the number for ints.</summary>
        public static string Describe(TunableSpec spec, int value)
        {
            if (spec == null) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (spec.Kind == TunableKind.Bool) return value != 0 ? "ON" : "OFF";
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // =====================================================================
        //  THE CONFIGURATION LINE - one line, every knob, every session
        // =====================================================================

        /// <summary>
        /// Print the WHOLE configuration on one line: every knob, its resolved value and its
        /// provenance.
        /// <para>
        /// ⭐ THIS IS WHY A FELT-TEST IS NOT WASTED. The owner will flip knobs between runs and
        /// report "that one felt better". Without this line the capture cannot say which
        /// configuration produced it, and the run proves nothing. Emitted at service boot AND
        /// again on every accepted payload, so a mid-session change is visible too.
        /// </para>
        /// Never throws. Uses Warn deliberately when ANY knob is overridden - an overridden build
        /// is not the shipping build, and that must not read as ordinary narration.
        /// </summary>
        public static void LogConfiguration(string why)
        {
            Guard.Try(Sys, "log tunable configuration", () =>
            {
                var sb = new StringBuilder(512);
                int overridden = 0;
                sb.Append("CONFIG (").Append(why ?? "?").Append("): generation=").Append(Generation)
                  .Append(" tableProvenance=").Append(s_provenance)
                  .Append(" rows=").Append(RowCount).Append(" | ");

                for (int i = 0; i < Registry.Length; i++)
                {
                    var spec = Registry[i];
                    int v = Int(spec.Key);
                    if (v != spec.Default) overridden++;
                    if (i > 0) sb.Append("  ");
                    sb.Append(spec.Key).Append('=').Append(Describe(spec, v));
                    if (v != spec.Default) sb.Append("(OVERRIDDEN, default ").Append(Describe(spec, spec.Default)).Append(')');
                }

                if (overridden == 0)
                {
                    FlowTrace.Step(Sys, sb.ToString() +
                        " || EVERY knob is at its shipping default - this session is TODAY'S BEHAVIOUR, " +
                        "unchanged. Nothing was overridden by the database or by PlayerPrefs.");
                }
                else
                {
                    FlowTrace.Warn(Sys, sb.ToString() +
                        " || " + overridden + " knob(s) are OVERRIDDEN. This session is NOT the shipping " +
                        "default configuration - quote this line in any felt-test report, because it is " +
                        "the only record of what produced the run. See docs/PROD022_TUNABLE_FLAGS.md.");
                }
            });
        }

        // =====================================================================
        //  WRITE SIDE
        // =====================================================================

        /// <summary>
        /// Drop the standing table. Every knob answers its shipping default afterwards,
        /// which is the correct resting state and the one this system must always be able
        /// to fall back to.
        /// </summary>
        public static void Clear(string provenance = ProvenanceDefault)
        {
            s_remote = null;
            s_provenance = string.IsNullOrEmpty(provenance) ? ProvenanceDefault : provenance;
            Generation++;
        }

        /// <summary>
        /// Parse a payload and ATOMICALLY swap it in. Returns false on a hard parse failure,
        /// and on that path the EXISTING table is left exactly as it was - a malformed live
        /// payload must never half-apply over a good standing table.
        /// <para>
        /// A payload whose readOk is false is the SERVER saying it could not read its own
        /// table. That clears to defaults rather than being mistaken for "no knobs are set".
        /// </para>
        /// </summary>
        public static bool ApplyPayload(string json, string provenance)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                FlowTrace.Warn(Sys, "payload rejected: empty body from provenance='" +
                                    (provenance ?? "null") + "'. Every knob keeps whatever it already " +
                                    "resolved (tableProvenance=" + s_provenance + ").");
                return false;
            }

            TunablePayload dto = Guard.Try<TunablePayload>(
                Sys, "parse tunables payload (" + (provenance ?? "null") + ")",
                () => JsonConvert.DeserializeObject<TunablePayload>(json),
                null);

            if (dto == null)
            {
                FlowTrace.Warn(Sys, "payload rejected: unparseable (provenance='" + (provenance ?? "null") +
                                    "'). Every knob resolves to its SHIPPING DEFAULT - the remote read is " +
                                    "an override, never a dependency.");
                return false;
            }

            if (dto.Version != PayloadVersion)
            {
                FlowTrace.Warn(Sys, "payload version " + dto.Version + " != expected " + PayloadVersion +
                                    " - parsing anyway (forward-compatible).");
            }

            if (!dto.ReadOk)
            {
                s_remote = null;
                s_provenance = ProvenanceDefault;
                Generation++;
                FlowTrace.Warn(Sys, "server reported readOk=false (reason='" + (dto.Reason ?? "?") +
                                    "') - the tunables table is unreadable ON THE SERVER. Every knob is " +
                                    "back at its shipping default, i.e. today's behaviour. No knob can be " +
                                    "changed until the table reads again.");
                LogConfiguration("server readOk=false");
                return true;
            }

            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            int unknown = 0;

            if (dto.Values != null)
            {
                foreach (var pair in dto.Values)
                {
                    string key = pair.Key;
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (SpecFor(key) == null)
                    {
                        // Forward compatibility, and it is deliberately not an error: the
                        // database may carry a key a NEWER build understands. Say so and move on.
                        unknown++;
                        FlowTrace.Step(Sys, "payload carries unregistered key '" + key +
                                            "' - ignored by this build (it may belong to a newer one).");
                        continue;
                    }
                    next[key] = pair.Value;
                }
            }

            // ATOMIC: one assignment, whole table.
            s_remote = next;
            s_provenance = string.IsNullOrEmpty(provenance) ? ProvenanceRemote : provenance;
            Generation++;

            LogConfiguration("payload accepted, rows=" + next.Count + " unknown=" + unknown);
            return true;
        }

        /// <summary>
        /// Serialise the standing table back to the wire shape, for the on-device cache.
        /// Returns null when there is nothing to cache. Never throws.
        /// </summary>
        public static string SerializeStandingTable()
        {
            var table = s_remote;
            if (table == null) return null;
            return Guard.Try<string>(Sys, "serialize standing tunables", () =>
            {
                var dto = new TunablePayload
                {
                    Version = PayloadVersion,
                    ReadOk = true,
                    Reason = "cache",
                    Values = new Dictionary<string, string>(table, StringComparer.Ordinal),
                };
                return JsonConvert.SerializeObject(dto);
            }, null);
        }

        /// <summary>Flatten a value for one-line logging. Bounded - a row is operator data.</summary>
        private static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            string t = s.Replace('\r', ' ').Replace('\n', ' ');
            return t.Length <= 64 ? t : t.Substring(0, 64) + "...";
        }

        // ---------------------------------------------------------------------
        //  Wire DTO - Newtonsoft. JsonUtility cannot express the 'values' map.
        // ---------------------------------------------------------------------

        [Serializable]
        internal sealed class TunablePayload
        {
            [JsonProperty("version")] public int Version { get; set; }
            [JsonProperty("readOk")] public bool ReadOk { get; set; }
            [JsonProperty("reason")] public string Reason { get; set; }
            [JsonProperty("values")] public Dictionary<string, string> Values { get; set; }
        }
    }
}
