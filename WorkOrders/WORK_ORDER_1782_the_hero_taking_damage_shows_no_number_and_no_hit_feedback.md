# WORK ORDER 1782 — RETRACTED. The hero's hit feedback is rich and deliberate; my grep was too narrow

**Status:** CLOSED - INVALID

Closed 2026-09-16, the same day it was minted, by the lane that minted it. **The premise was false.** Precedent for this shape of close: WO-1757, closed same-day as *"The census was never truncated; the LEAD's grep was wrong."*

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`). **The number is spent — do not reuse it.**

⛔ **DO NOT DISPATCH A LANE ON THIS TICKET.** It is kept, not deleted, because the retraction is more useful to the next reader than a missing file.

---

## 1. WHAT I CLAIMED

> *"The hero taking damage shows no floating number and no hit feedback of any kind … no flash, no vignette, no shake, no hit sound wired at this class."* — filed **P0**, `READY TO IMPLEMENT`.

## 2. WHAT IS ACTUALLY THERE — read at source 2026-09-16

**Every hit on the hero already fires five separate feedback channels**, in one block in `HeroHealth.TakeDamage` that is literally headed *"Combat feel (additive)"* (`Assets/_Modules/Village/Hero/HeroHealth.cs:966-977`):

| channel | site |
|---|---|
| impact VFX | `HeroHealth.cs:971` `VFXManager.Play(VFXType.Impact_Physical, …)` |
| haptics | `:972` `_impactFeedback?.PlayHaptic(0.25f, 0.12f)` |
| **hit sound** | `:973` `GameSfx.PlayHeroHit();   // hero took a hit — audible grunt/impact (was silent)` |
| screen shake | `:977` `HitStopManager.DoImpact(HitTier.Light);   // subtle shake per hit` |
| **full-screen damage flash** | `Assets/_Modules/Village/Hero/HeroHitReaction.cs` — *"a red full-screen tint spikes and fades whenever the hero's HP drops"*, gated to a decrease at `:339`, auto-attached at `HeroControlEnsurer.cs:642` and `HeroHealth.cs:2413-2414` |

And a sixth at low HP: `HeroInjuredVignette`, auto-added at `HeroHealth.cs:298`.

**The accessibility angle I was about to raise as the ticket's substantive half is ALSO already solved, and better than I would have specified it.** `Assets/_Modules/Village/Hero/HeroHpStateAura.cs:1-30` is WO-888, and it says so in its own header: *"Until now the ONLY 'you are about to die' signal in the game was a RED screen-edge vignette (HeroInjuredVignette). The owner is red/green colourblind: she cannot reliably see the one signal that tells her she is in danger. That is a real bug."* It carries the read on **pulse rate, guttering depth, recipe shape and motion direction** — four channels that survive greyscale — and `HeroInjuredVignette.cs:14-15` records that the red cue is *"deliberately KEPT … redundancy is good accessibility."*

## 3. WHY I GOT IT WRONG — the mechanism, because it will recur

I searched **`heroDamaged|OnHeroHit|HeroTookDamage|damageFlash|hurtFlash|HeroHurt|hitFlash`**. Not one of those is the name of any of the six systems above — they are `HeroHitReaction`, `HeroInjuredVignette`, `HeroHpStateAura`, `GameSfx.PlayHeroHit`, `HitStopManager`, `VFXManager`.

⛔ **I then reported that search as "verified by TOKEN across the whole repo, not by name in one file" (memory `search-by-token-not-by-name`) — and it was neither.** It was a guessed list of seven *names* that happened to be spelled like tokens. A genuine token sweep is the concept: `grep -rniE 'flash|vignette|hitstop|haptic|PlayHero' Assets/_Modules/Village/Hero`, which returns all six in one pass.

The correct claim was available and narrow: `grep -c 'DamageNumberSpawner' Assets/_Modules/Village/Hero/HeroHealth.cs` = **0** — the hero gets no **floating damage number** while structures (`StructureHitReaction.cs:350`) and enemy units (`Enemy.cs:2833`, `:2838`) do. **That is the entire residue, and it is P3 at most:** the hero already has five hit channels, the number would be the sixth, and nothing in the 2026-09-16 capture or in any owner report says she wants it. **If it is ever wanted it needs a fresh ticket and an owner word — not this one.**

## 4. WHAT THIS COSTS THE PROGRAM

Nothing but the number. No code was written. The P0 count in `docs/RAID_POLISH_PROGRAM_2026-09-16.md` drops by one; lane **F** is struck from §3.

## 5. THE STANDING LESSON — this was the SECOND retraction in one session

WO-1777 was also first written on a false premise (the "wall collider is half the visible wall" reading, retracted in that ticket's §2 and in the program index §5). Both failures have the identical shape and it is the one CLAUDE.md §11B names: **a real measurement used to support a conclusion it did not support.** In 1777 it was renderer-bounds arithmetic; here it was an absence-of-evidence grep.

⚠ **An absence proves nothing until the search that produced it is shown to be capable of finding the thing.** A zero-hit grep is a claim about the *pattern*, not about the *repo* — and stating it as the latter is a guess wearing a measurement's clothes.
