# WO-1614 - Every learned Mage spell has a distinct cast animation and VFX

**Status:** FIXED — implemented and regression-gated for owner verification in the next Windows/APK build

## Definitive RCA

HeroAbilities fired a raw generic `Cast` trigger before ActorAnimator set `CastVariant`. Learned-spell
VFX also selected the motion bundle from the hotbar seat instead of the resolved ability. As a result,
different skill-tree spells equipped in the same seat collapsed onto the same motion and cast flash.

## Resolution

- ActorAnimator is the single normal cast trigger and sets the ability variant before firing.
- Mage.controller now contains dedicated learned-spell states beyond the four stock Q/W/E/R states.
- Shell, Drain, Poison, Frost Nova, Manaweave, Void Rift, Blink, Cataclysm, Thunder, Mend, Meteor,
  Syphon Essence, and Wither each author their own `castAnim` identity and a distinct existing manual-pick
  cast VFX key. The primary attack remains unchanged.
- Cast VFX follows the resolved spell identity, not the hotbar position.

## Evidence

`MAGE_SPELL_IDENTITY_OK` verifies 13 non-primary identities are present and unique, the generic-trigger
race is absent, animator generation is not capped at four states, and VFX uses the resolved identity.
