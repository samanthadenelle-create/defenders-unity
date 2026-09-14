// =============================================================================
// SfxId — enumeration of every named SFX event in the game. WO-62.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Audio   Namespace: DeNelle.Audio
//
// Add a new value here when a new sound is needed. AudioService.PlaySfxAtPosition
// and VFXManager.VfxToSfx() reference this enum so the compiler catches typos
// and missing mappings — no magic strings.
//
// Naming convention:  Category_Descriptor  (mirrors VFXType)
//   None        — sentinel; no sound played
//   Fire*       — fire / flame sounds
//   Arcane*     — aether / arcane magic sounds
//   Tower*      — tower combat sounds
//   Pet*        — pet aura and attack sounds
//   Enemy*      — enemy death / reaction sounds
//   Wave*       — wave state sounds (clear, incoming)
//   Level*      — level-up / progression sounds
//   Combo*      — kill-combo feedback sounds
// =============================================================================

namespace DeNelle.Audio
{
    /// <summary>
    /// Every named SFX event. Used as the key to <see cref="AudioService.PlaySfxAtPosition"/>
    /// and in VFXManager's VfxToSfx() mapping (WO-62). <see cref="None"/> means no sound.
    /// </summary>
    public enum SfxId
    {
        /// <summary>Sentinel — no sound plays when this value is passed.</summary>
        None = 0,

        // ── Impact sounds ─────────────────────────────────────────────────────
        /// <summary>Fire explosion / Meteor Strike impact boom + crackle.</summary>
        FireExplosion,
        /// <summary>Arcane / Aether detonation — resonant ring + shimmer.</summary>
        ArcaneExplosion,
        /// <summary>Expanding flat shockwave ring — Knight slam, ground pound.</summary>
        Shockwave,
        /// <summary>Healing contact — warm rising chime at the heal target.</summary>
        Heal,
        /// <summary>
        /// WO-1717: masonry impact — a blow landing on a STRUCTURE (wall, gate, building,
        /// tower, collector, harvest site). Short, dry, low: stone taking a hit, not an
        /// explosion. Played by StructureHitReaction on the same beat as the dust burst and
        /// the floating damage number, so the read is MOTION + SOUND + NUMBER and never a
        /// colour (the owner is colourblind).
        ///
        /// <para>NO AUTHORED CLIP EXISTS YET. This falls through to ProceduralSfx's synth
        /// recipe (a placeholder thud) exactly as every other id does on a fresh clone. An
        /// artist/owner pick dropped at the audio key <c>Sfx/Sfx_StructureImpact</c> — or a
        /// row in a generated SfxClipLibrary — wins over the synth with NO code change.</para>
        /// </summary>
        StructureImpact,

        // ── Casting / projectile sounds ───────────────────────────────────────
        /// <summary>Wizard cast charge-up — swirling arcane wind-up sound.</summary>
        WizardCast,
        /// <summary>Flame arrow launch — short fiery whoosh.</summary>
        FlameArrowLaunch,
        /// <summary>Generic tower shot — short punchy impact.</summary>
        TowerShot,

        // ── Enemy sounds ──────────────────────────────────────────────────────
        /// <summary>Enemy death — quick squash / pop sound.</summary>
        EnemyDeath,

        // ── Wave / progression sounds ─────────────────────────────────────────
        /// <summary>Wave clear — victory sting / celebration fanfare.</summary>
        WaveClear,
        /// <summary>Tower or hero level-up — rising chime burst.</summary>
        LevelUp,

        // ── Combo sounds ──────────────────────────────────────────────────────
        /// <summary>Kill combo tier 1 — quick reward sting.</summary>
        ComboSmall,
        /// <summary>Kill combo tier 2 — bigger fanfare, more satisfying.</summary>
        ComboBig,

        // ── Pet sounds ────────────────────────────────────────────────────────
        /// <summary>Pet fire aura ambient loop sound.</summary>
        PetFireAura,
        /// <summary>Pet attack impact sound.</summary>
        PetAttack,

        // ── Ward-tether sounds (WO-112) ───────────────────────────────────────
        /// <summary>Ward-stone relit — the Heart's song swells, a warm rising kindle.</summary>
        WardLit,
        /// <summary>Ward-stone goes cold / the song thins — a low, hollow dim.</summary>
        WardDim,
    }
}
