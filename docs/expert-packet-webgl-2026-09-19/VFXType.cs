// =============================================================================
// VFXType — master catalogue of every named VFX event in the game.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Add a new value here whenever a new effect is needed — VFXCatalog then lets
// a designer wire the prefab (or leave it null for procedural fallback).
//
// Naming convention:  Category_Descriptor
//   Impact_*   — oneshot hit/explosion at a point in world space
//   Projectile_* — moving travel effect (attached to a projectile GameObject)
//   Cast_*     — charge/wind-up on the caster Transform
//   Death_*    — oneshot on enemy death
//   Aura_*     — persistent looping effect (returns VFXHandle)
//   Env_*      — persistent environmental loop (torch, fog, portal …)
//   Juice_*    — feedback/feel moments (crit, combo, level-up …)
// =============================================================================

namespace DeNelle.Village
{
    public enum VFXType
    {
        None = 0,

        // ── Impacts ──────────────────────────────────────────────────────────
        /// <summary>Melee contact / generic physical hit (bone-grey sparks).</summary>
        Impact_Physical,
        /// <summary>Arcane / Aether bolt or spell impact (white-violet ring).</summary>
        Impact_Aether,
        /// <summary>Fire or ember blast impact (red-orange burst).</summary>
        Impact_Flame,
        /// <summary>Ice shard or frost bolt impact (pale-blue burst).</summary>
        Impact_Ice,
        /// <summary>Healing contact — green sparkle at the heal target.</summary>
        Impact_Heal,
        /// <summary>Large fire detonation — Meteor Strike, boss death, big boom.</summary>
        Impact_ExplosionFire,
        /// <summary>Large Aether detonation — Withering Surge, necromancer special.</summary>
        Impact_ExplosionAether,
        /// <summary>Expanding flat shockwave ring — Knight slam, ground pound.</summary>
        Impact_ShockwaveRing,
        /// <summary>Bone / crystal shard scatter — skeleton death, Vault Slam.</summary>
        Impact_ShardsBurst,
        /// <summary>Smoke wisp dissolution — death smoke, Reaper trail impact.</summary>
        Impact_SmokeWisps,

        // ── Projectiles (attach to projectile Transform, loop until hit) ─────
        /// <summary>Hero Q Arcane Bolt travel — white-violet orb.</summary>
        Projectile_ArcaneBolt,
        /// <summary>Hero W Frost bolt travel — pale-blue streak.</summary>
        Projectile_FrostBolt,
        /// <summary>Ranger normal arrow travel — green streak.</summary>
        Projectile_Arrow,
        /// <summary>Ranger Flaming Arrow upgrade — orange-tipped arrow streak.</summary>
        Projectile_FlameArrow,
        /// <summary>Arcane Tower bolt travel — dim violet orb.</summary>
        Projectile_TowerArcane,
        /// <summary>Flame Tower bolt (planned) — red-orange bolt.</summary>
        Projectile_TowerFire,
        /// <summary>Frost Tower bolt (planned) — pale-blue bolt.</summary>
        Projectile_TowerIce,
        /// <summary>Hollow Caster enemy bolt — sickly hollow blob.</summary>
        Projectile_EnemyCasterBolt,

        // ── Casting / Charge (on caster Transform, plays before release) ──────
        /// <summary>Mage charge wind-up — swirling white-violet gathering.</summary>
        Cast_MageCharge,
        /// <summary>Fire charge wind-up — gathering ember orbs converging on the
        /// caster before a fire release (Meteor / Radiant Strike). Distinct from the
        /// arcane MageCharge so a fiery ultimate reads fiery, not violet. Battle-polish.</summary>
        Cast_FireCharge,
        /// <summary>Knight slam wind-up — gold sparks and ground crack glow.</summary>
        Cast_KnightSlam,
        /// <summary>Ranger bow-draw — thin green streak spiral around bow.</summary>
        Cast_RangerDraw,
        /// <summary>Healing Beacon buildup — rising warm column of light.</summary>
        Cast_Heal,
        /// <summary>Frost Nova buildup — spreading frost ring on ground.</summary>
        Cast_FrostNova,
        /// <summary>Necromancer summon channel — staff raise + dark wisps.</summary>
        Cast_NecromancerSummon,
        /// <summary>Hollow Caster bolt charge — violet blob swelling before throw.</summary>
        Cast_EnemyCaster,

        // ── Death (oneshot on enemy/boss death position) ──────────────────────
        /// <summary>Standard Hollow One death — bone shards + smoke wisp.</summary>
        Death_Skeleton,
        /// <summary>Boss death — large explosion + distorted shockwave ring.</summary>
        Death_Boss,
        /// <summary>Heavy brute / golem death — heavy crumble + dust cloud.</summary>
        Death_Brute,
        /// <summary>Wolf death — ice burst + light snow scatter.</summary>
        Death_Wolf,
        /// <summary>Tiefling Cultist death — flame burst + ember scatter.</summary>
        Death_Tiefling,
        /// <summary>Generic enemy death fallback when no specific type matches.</summary>
        Death_Generic,

        // ── Auras (persistent loops — callers receive VFXHandle for Stop) ─────
        /// <summary>Hollow Caster aura — pale violet electroCore loop.</summary>
        Aura_EnemyCaster,
        /// <summary>Necromancer boss aura — deep violet ghost-portal loop.</summary>
        Aura_Necromancer,
        /// <summary>Hollow Mender / healer aura — soft warm white finalRest loop.</summary>
        Aura_Healer,
        /// <summary>Tiefling Cultist fire aura — red fire loop.</summary>
        Aura_Flame,
        /// <summary>Feral Wolf ice aura — light snow loop (feet + flanks).</summary>
        Aura_Ice,
        /// <summary>Physical heavy enemy foot-dust — dusty loop.</summary>
        Aura_Dust,
        /// <summary>Reaper scythe wisps — slow smoke wisps loop.</summary>
        Aura_SmokeReaper,
        /// <summary>Heart of Elarion ambient pulse — nucleus loop.</summary>
        Aura_HeartPulse,
        /// <summary>Empowered tower persistent glow — nucleus loop (tinted by element).</summary>
        Aura_EmpowerTower,
        /// <summary>Pet aura level 1 — dim sparkle loop.</summary>
        /// <summary>Pet aura level 2 — medium sparkle loop, brighter particles.</summary>
        Aura_TalentNode,
        /// <summary>Pet aura level 3 — bright sparkle loop, high emission.</summary>

        // ── Environment (attach to scene objects, DontDestroyWithOwner) ────────
        /// <summary>Torch / brazier fire loop — Lana Studio torch flame.</summary>
        Env_TorchFlame,
        /// <summary>Lantern warm glow loop — soft point-light + embers.</summary>
        Env_LanternGlow,
        /// <summary>Cold biome dungeon ground fog loop.</summary>
        Env_GroundFog,
        /// <summary>Dungeon portal loop — swirling hyperspace/portal effect.</summary>
        Env_DungeonPortal,
        /// <summary>Title screen ambient embers + streak particles.</summary>
        Env_TitleEmbers,
        /// <summary>Destroyable object impact dust (barrel, crate, wall section).</summary>
        Env_DestructionDust,
        /// <summary>Destroyable object sparks on break.</summary>
        Env_DestructionSparks,

        // ── Juice / Feedback ──────────────────────────────────────────────────
        /// <summary>Critical hit flash — bright white burst + size pop.</summary>
        Juice_CriticalHit,
        /// <summary>Kill-streak combo burst — radial flash at the last kill point.</summary>
        Juice_KillStreak,
        /// <summary>Wave complete celebration — upward sparkle fountain.</summary>
        Juice_WaveClear,
        /// <summary>Tower or hero level-up burst — ring + rising stars.</summary>
        Juice_LevelUp,
        /// <summary>Ground decal — scorch mark (Flame) or frost patch (Ice).</summary>
        Juice_GroundDecal_Flame,
        /// <summary>Ground decal — frost patch from Ice impacts.</summary>
        Juice_GroundDecal_Ice,

        // ── Portal ───────────────────────────────────────────────────────────
        /// <summary>Oneshot burst when hero enters a dungeon portal.</summary>
        Portal_Enter,
        /// <summary>Oneshot burst when hero exits a dungeon portal.</summary>
        Portal_Exit,

        // ── WO-62 celebration / combo / pet aliases ───────────────────────────
        // Named to match the VfxToSfx mapping table in VFXManager (WO-62).
        // These mirror the Juice_ / Aura_ values above; VFXCatalog can point
        // both to the same prefab, or differentiate them independently.
        /// <summary>Wave-clear celebration burst (alias for Juice_WaveClear). WO-62.</summary>
        WaveClear_Celebration,
        /// <summary>Level-up celebration burst (alias for Juice_LevelUp). WO-62.</summary>
        LevelUp_Celebration,
        /// <summary>Kill-combo tier-1 feedback burst. WO-62.</summary>
        Combo_Tier1,
        /// <summary>Kill-combo tier-2 feedback burst (bigger than tier 1). WO-62.</summary>
        Combo_Tier2,
        /// <summary>Pet fire aura loop. WO-62. (The Aura_PetLevel* ladder it once alternated with was deleted 2026-08-20 with the unshipped pet-aura system.)</summary>
        Pet_Aura_Fire,
        /// <summary>Pet ice aura loop. WO-62. (See the fire note above.)</summary>
        Pet_Aura_Ice,
        /// <summary>Oneshot pet attack impact burst. WO-62.</summary>
        Pet_Attack,

        // Environment / Weather (WO-52)
        /// <summary>Shooting star streak. WeatherManager pools these. WO-52.</summary>
        ShootingStar,
        // -- WO-59 Dungeon VFX -----------------------------------------------------
        /// <summary>Larger, darker enemy death explosion for dungeon runs. WO-59.</summary>
        Death_EnemyExplosion_Dungeon,

        // -- WO-66 Elite / Boss ----------------------------------------------------
        /// <summary>Rising dark energy burst on elite enemy spawn. WO-66.</summary>
        Elite_Spawn,
        /// <summary>Dark energy collapse on elite enemy death. WO-66.</summary>
        Elite_Death,
        /// <summary>Dark energy + lightning bolt burst on boss spawn. WO-66.</summary>
        Boss_Spawn,
        /// <summary>Massive explosion + lingering smoke + debris on boss death. WO-66.</summary>
        Boss_Death,
        /// <summary>Oversized shockwave ring on boss melee impact. WO-66.</summary>
        Boss_AttackImpact,

        // -- WO-66 boss phase VFX (DragonBoss phase transitions) -------------------
        /// <summary>One-shot enrage/phase-transition burst when a boss crosses an HP threshold. WO-66.</summary>
        Boss_PhaseTransition,
        /// <summary>Telegraph wind-up burst on the boss just before a special attack (swoop/breath). WO-66.</summary>
        Boss_Telegraph,
        /// <summary>Persistent phase aura loop — early/calm phase (Circling). Swapped per phase. WO-66.</summary>
        Boss_Aura_Phase1,
        /// <summary>Persistent phase aura loop — mid/enraged phase (Stooping). WO-66.</summary>
        Boss_Aura_Phase2,
        /// <summary>Persistent phase aura loop — final/relentless phase (LastWing). WO-66.</summary>
        Boss_Aura_Phase3,

        // -- WO-757 / WO-759 Particle Pack boss breath -----------------------------
        // APPEND-ONLY: this enum is serialised by ordinal into VFXCatalog.asset, so a
        // value INSERTED above would silently re-map every existing catalog row.
        /// <summary>Sustained multi-layer fire-breath cone from the boss's mouth socket
        /// (Particle Pack FlameThrower recipe — flame + embers + smoke). CONTINUOUS
        /// family: catalog row IsLoop=true; played with PlayAura(type, socket) and ended
        /// with the returned VFXHandle.Stop(). WO-757/759.</summary>
        Boss_FireBreath,

        // -- WO-884 / registry / handbook batch (Grok single-owner append 2026-08-05) --
        // Sourced from docs/vfx/VFX_CREATIVE_PICKS_REGISTRY.md + VFX_PREFAB_HANDBOOK.md §3.3.
        // CLI and builders REFERENCE these names only — do not append further without the
        // single-owner rule (WO-884 §0.2). Healer structure field reuses Aura_Healer (no new value).

        // Environment (P1 dungeon dress)
        /// <summary>Dungeon / prop candle flame loop (Particle Pack Candles or TinyFlames). Family A Ambient. IsLoop=true.</summary>
        Env_Candle,
        /// <summary>Geothermal / vent rising steam loop (Particle Pack RisingSteam). Family A Ambient. IsLoop=true.</summary>
        Env_SteamVent,
        /// <summary>Pressurised steam jet oneshot or short stream (Particle Pack PressurisedSteam). Family B Impact or short A.</summary>
        Env_SteamBurst,

        // Combat release
        /// <summary>Barrel / weapon muzzle flash oneshot (Particle Pack MuzzleFlash). Family B Muzzle. IsLoop=false.</summary>
        Cast_MuzzleFlash,

        // Spawn / despawn (scripted dissolve recipes — one-shot play, no demo loop)
        /// <summary>Enemy / summon materialize (Particle Pack Respawn + SpawnEffect cutoff). Scripted oneshot.</summary>
        Enemy_Spawn,
        /// <summary>Blink / despawn dissolve (Particle Pack Dissolve + SpawnEffect reversed). Scripted oneshot.</summary>
        Despawn_Dissolve,

        // HP-state + item auras (colourblind primary read — world space, not red vignette)
        /// <summary>Hero low-HP loop (SmokeEffect guttering wisps). Family A Aura. IsLoop=true. Stop when healed above threshold.</summary>
        Aura_LowHealth,
        /// <summary>Hero near-death loop (TinyFlames fast gutter). Family A Aura. IsLoop=true. Sub-tier under ~0.25 HP.</summary>
        Aura_NearDeath,
        /// <summary>Active regen / healing-in-progress loop (RisingSteam upward). Family A Aura. IsLoop=true.</summary>
        Aura_HealingInProgress,
        /// <summary>Item-granted heal aura held on body (RisingSteam low). Family A Aura. IsLoop=true. GearAura seat.</summary>
        Aura_ItemHeal,

        // Harvest / economy node auras (motion-differentiated, colourblind). Family A Aura. IsLoop=true.
        /// <summary>Iron harvest node aura — heavy dust settling + metal spark glint.</summary>
        Harvest_Iron,
        /// <summary>Wood harvest node aura — flat sideways-drifting chip motes.</summary>
        Harvest_Wood,
        /// <summary>Food harvest node aura — sparse rising pollen motes.</summary>
        Harvest_Food,
        /// <summary>Crystal harvest node aura — suspended twinkle (no travel).</summary>
        Harvest_Crystal,
        /// <summary>Gold harvest node aura — short falling glint pops.</summary>
        Harvest_Gold,
        /// <summary>Collector / node ready-to-collect beacon (FireFlies rising bob). Family A Aura. IsLoop=true.</summary>
        Collector_Ready,
    }
}
