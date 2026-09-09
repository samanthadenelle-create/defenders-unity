# WO-1429: an out-of-mana Mage has NO attack at all and can only run

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:06, build 2026.09.08.361259). PRIOR STATUS: FIXED 2026-09-06 - the re-cut (section 0) is implemented and landed in commit `bb51b8b9c`; on the owner's device for the felt test. Gate proof at HEAD: `Builds/reg-wave9.log` (2026-09-07 11:02, postdates the commit) carries `[primary-fallback] PRIMARY FALLBACK OK ...` inside `REGRESSION_OK 446/446 suites -- 446 green, 0 red, 0 skipped`. Section 0.7 (CLAUDE.md section 7 vs. the mobile battle HUD) and section 3.4 (a face that names the live primary) are the owner's calls and stay recorded in the body. *(was: RE-CUT 2026-09-06 - the original spec was WRONG in three ways and a lane correctly REFUSED to implement it; before that: READY TO IMPLEMENT - minted 2026-09-06 (CLI) from the owner's playtest)*
**Silo:** Hero combat (DeNelle.Village.Hero) - the primary-attack resolution seam
**Owner ruling (2026-09-06, verbatim):** *"if you play as the mage once you expel all of your MP you have no ability to
attack in anyway all you can do is run until you regain your mana. I think when mana is gone, it should automatically
flip to attack with the staff just a manual very low attack while it recharges back to at least 50% then it switches
back to magic."*

---

## 1. The defect

A class with no affordable action is a class that cannot play. The Mage at 0 mana can only run away, which is not a
difficulty spike - it is a **dead state with no verb**, the same species as every other defect found on 2026-09-06:
a capability that exists (the melee sweep) which the player cannot reach.

## 2. Root cause, read at source

`HeroAbilities` resolves the primary attack down ONE of two paths (`HeroAbilities.cs:463-500`):
- the class's basic is RANGED - derived, never a per-class table, from the authored def's effect shape plus
  `RangedPrimaryReachFactor = 2f` (`:479`) - so the primary CASTS the locked Q def; or
- it is not, and the primary is the class-agnostic **melee sweep**.

`TryGetRangedPrimary` (`:497+`) is *"the SINGLE decision seam: PlayerAttackController fires through it and
HeroTargetIndicator gates auto-acquire on it, so the input and the targeting can never disagree"*.

**The Knight takes the melee path. The Mage's basic is a spell, so it takes the ranged path - and that path has no
fallback when the cast cannot be PAID FOR.** The sweep is already implemented and already class-agnostic; the Mage
simply never reaches it. Nothing here needs inventing.

⚠ **VERIFY BEFORE EDITING** (this WO asserts the mechanism from a read of the resolution seam, not from a capture):
instrument or headless-run an out-of-mana Mage and confirm the primary input produces NOTHING today. CLAUDE.md section 12 -
static reading LOCATES a cause, it never CONCLUDES one. If the input in fact produces a failed-cast with a message, the
fix is a different one and this WO should be re-scoped rather than implemented as written.

## 3. The fix

**When the class basic is ranged but currently unaffordable, fall through to the melee sweep** - the path the Knight
already uses. The hero always has a verb.

### 3.1 Hysteresis is REQUIRED, and the owner has set the numbers
Switch to the staff at **0 mana**; return to magic at **>= 50%**. Without a gap the hero would flip back to spellcasting
on the first point of regenerated mana and immediately fail again - a flicker between two weapons, several times a
second, at exactly the moment the player is under pressure. The two thresholds must be **separate authored values**, not
one comparison.

### 3.2 The staff attack is DELIBERATELY WEAK
Owner: *"just a manual very low attack"*. It is a floor that keeps the player acting, not a competitive option - it must
never be the optimal Mage rotation. It is the EXISTING class-agnostic sweep; do NOT author a second melee system.

### 3.3 Derive, never hardcode the class
⛔ The existing seam is explicitly derived and its own comment forbids a per-class table: *"DERIVED, NEVER A PER-CLASS
TABLE - that is the same hand-authored-vs-derived defect class as IsLoop, Hidden, and the town that laid itself on its
side"*. **Do not add `if (class == Wizard)`.** The rule is "the resolved primary cannot be paid for", which is true for
any class whose basic has a cost, and it generalises for free.

### 3.4 The player must be able to SEE it
The bar face and any targeting affordance must show which primary is live. A silent weapon swap is the same
tell-the-player-nothing failure as the rest of that day's findings. One word on the face is enough.

## 4. Scope
**In:** the primary-attack resolution seam, the two thresholds, and the face/telegraph that names the live primary.
**Out:** mana regeneration rate, staff damage tuning, the Q ability's own cost, and any other class's kit - all balance,
and **the owner rules on balance**. Bring numbers back rather than choosing them.

## 5. Regression - `PrimaryFallbackRegression`, marker `PRIMARY_FALLBACK_OK` / `_FAIL <case>`
Each case with a one-line REVERT RECIPE; the CLI proves RED then GREEN.
1. `[no-verbless-hero]` **the case that matters**: for every playable class, at every mana value from 0 to max, the
   resolved primary is non-null. RED: restore the unconditional ranged path.
2. `[staff-at-zero]` a ranged-basic class at 0 mana resolves the MELEE sweep.
3. `[magic-at-half]` the same hero at >= 50% resolves the SPELL again.
4. `[hysteresis-gap]` the two thresholds are distinct values, and rising mana from 0 does not re-arm the spell before
   the upper one. RED: make both read the same constant - the flicker returns.
5. `[no-per-class-table]` source: the fallback contains no class-name comparison. RED: add one.
6. `[knight-unaffected]` a melee-basic class resolves exactly as it does today at every mana value.

## 6. Acceptance
- [ ] Brace + NUL on every `.cs`; `COMPILE_GATE_OK`; `REGRESSION_OK n/n` with the new suite green and all six RED
      proofs recorded.
- [ ] **Captured proof of the defect BEFORE the fix** (section 2) and of the behaviour after - a headless run or an F8
      trace showing the primary at 0 mana.
- [ ] Owner felt-test: play the Mage, empty the bar, and confirm you can still fight and that the swap is visible.

## 7. RULED: the staff swing is FREE

**Owner ruling 2026-09-06, verbatim: *"No swing Staff should have no cost only casting magic should."***

The fallback costs **nothing**. Only casting magic spends mana.

**This is what makes section 3's guarantee actually hold, and it is not merely a balance preference.** A fallback with
a cost - stamina, a cooldown, anything - can itself become unavailable, and the hero is back in the dead state this WO
exists to remove. **Free is the only cost that guarantees "the hero always has a verb" is true at every instant.**
Encode it that way: the sweep must not consult any resource pool.

Add to section 5 as case 7:
`[fallback-is-free]` the melee-sweep path spends no resource - no mana, no stamina, no charge - at any mana value.
RED: give the sweep any cost and case 1 `[no-verbless-hero]` fails with it, which is the point.

## 8. Still open for the owner
1. Staff damage - a fraction of the spell, a flat floor, or scaled off the hero's level?
2. Should the same fallback apply to any OTHER class whose basic has a cost, or is the Mage the only one today?


---

# 0. ⛔ RE-CUT 2026-09-06 - READ THIS BEFORE ANY SECTION BELOW

A lane was dispatched to implement sections 1-7 and **stopped without writing code**, because CLAUDE.md section 12
required it to prove the cause first and the proof refuted the spec. **Sections 1-7 below are superseded by this
section wherever they disagree.** They are kept, unrewritten, because the reasoning that produced them is instructive.

## 0.1 THE PROOF - captured, not inferred
`logs/device/freeze-20260904-095249.log:544639` and `:466356`, a real Seeker session. Three consecutive lines, one tap:
```
[Flow:HudKit] command 'attack' fired
[Flow:HeroMana] cast REFUSED slot=Q 'Fireball': cd=0.47s Mana 21.08/24.00 cost=3.00 (authored 3).
[Flow:HudKit] primary command -> class Q for mage gated
```
**Nothing follows.** No swing, no melee trace, no fallback. The tap produced no verb.

## 0.2 THREE THINGS THE ORIGINAL SPEC GOT WRONG

**1. IT IS NOT ABOUT MANA. Both captured refusals are COOLDOWN refusals at near-full mana** - `cd=0.47s`,
`Mana 21.08/24.00`. `HeroAbilities.TryCast:813` refuses on `cd > 0 || _mana < cost` and both exit the SAME
`return false`. **So the dead button is not a rare out-of-mana state - it fires in every cooldown gap, several times a
minute, all game.** That is far worse than reported and it is what the owner is actually feeling.

**2. THE NAMED SEAM IS NOT IN THE INPUT PATH.** Section 2 blames `HeroAbilities.TryGetRangedPrimary`. Refuted at source:
`PlayerAttackController.Update:328` goes straight to `StartAttack()` with no ranged branch, and that file's
`WO-1105 REVISION` block (`:440-465`) records that the ranged-primary input path was DELETED by owner ruling.
**The real gate is `Assets/_Modules/Village/HUD/HudKitCommandBridge.cs:67-73`** - a hardcoded per-class table that
catches `"mage"` or `"ranger"` by name, calls `TryCast(Q)`, and `return`s **before ever reaching the melee swing**.
⚠ Section 3.3 forbids ADDING a per-class branch. **One already exists, and it IS the defect.** The fix DELETES it.

**3. THE HYSTERESIS WAS DESIGNED AGAINST A WRONG MODEL AND MUST NOT SHIP AS SPECIFIED.** If the fallback fires on
cooldown refusals while using the mana thresholds of section 3.1, a **0.47 s cooldown gap would lock the hero to the
staff until mana climbed back to 50%** - strictly worse than today. The owner's intent (never be weaponless) is right;
the mechanism was built on the assumption that 0 mana was the trigger.

## 0.3 TWO MORE FINDINGS
- **The RANGER is in the same branch, and it contradicts written canon.** CLAUDE.md section 7 states *"the phone's one
  attack button never spends an arrow."* `HudKitCommandBridge.cs:67-68` spends one. Same defect, second class - **in
  scope for this WO.**
- **MOBILE ONLY.** `PlayerAttackController.Update:317-330` melees unconditionally for every class on
  keyboard/mouse/gamepad. Only the HUD button is gated - which is why the owner sees it on the Seeker and a desktop
  session never would.

## 0.4 THE RE-CUT FIX - simpler than the original
**You pressed attack, so you attack.** A refused primary - for ANY reason, cooldown or cost - falls through to the free
melee sweep. No thresholds, no mana check, no hysteresis, and **the per-class table is deleted rather than extended**.
The sweep already consults no resource pool (`PlayerAttackController:700`), so the owner's ruling 7 (the swing is FREE)
is satisfied by construction.

⚠ **CLI RECOMMENDATION, OWNER MAY REVERSE:** this replaces the owner's authored hysteresis with something simpler. It
serves her stated intent - never be left without an attack - and it fixes the cooldown case, which is the one she is
actually hitting. If she wants the staff to *persist* for a while rather than filling only the gap, the hysteresis
returns as a separate, later refinement on top of a working fallback.

## 0.5 REVISED ORACLE
Section 5's cases 3, 4 and 7 assume thresholds that no longer exist. Keep case 1 `[no-verbless-hero]` - **for every
class, at every mana value AND during cooldown, the resolved primary is non-null** - plus:
- `[no-per-class-table]` `HudKitCommandBridge` contains no class-name comparison in the primary path. RED: restore it.
- `[ranger-spends-no-arrow]` pins CLAUDE.md section 7's canon, which is currently violated.
- `[fallback-is-free]` unchanged from section 7.
- `[cooldown-gap-still-swings]` a refusal with `cd > 0` and full mana still yields a swing. **This is the case that
  would have caught the real defect**, and no existing suite asks it.

## 0.6 NOT VERIFIED
**0 mana specifically has no capture.** No in-tree log holds a mana-starved refusal - only the two cooldown ones. That
`_mana < cost` reaches the identical dead end is a SOURCE READ (`HeroAbilities.cs:813`, both conditions exit one
`return false`) chained onto a captured cooldown refusal. Say it that way; do not claim a capture that does not exist.


## 0.7 ⛔ OWNER RULING NEEDED - CLAUDE.md section 7 vs the mobile battle HUD

The implementing lane could not satisfy section 0.5's `[ranger-spends-no-arrow]` case **because it is unsatisfiable as
written**, and it refused to ship a permanently-RED case or one asserting the opposite of canon. It is right, and the
conflict is real.

**CLAUDE.md section 7 states:** *"the phone's one attack button never spends an arrow."*

**Measured against the HUD data:** in the `hostile(activebattle)` posture, `hud-areas.json:242-249` leaves the
`actionRail` **EMPTY** - no attack pill, no Q/W/E/R arc - and `actionBar` carries only `combatDock`, whose slot 0 is
`HudCommands.Attack` (`HudKitController.cs:2298-2299`). Slots 2-4 are `AssignableCast`.
**So on the mobile battle HUD the attack button is the ONLY dispatch for the class Q.** Commit `a6daaf44c` added the
per-class table for exactly that reason. Making Attack always-melee would strand **Fireball AND Quick Shot entirely** -
the mage and ranger would have no way to cast their basic in battle at all.

**Two ways out. This is the owner's call, not a seat's:**
1. **Amend the canon.** Accept that on the combat dock the attack face IS the class Q, with a free melee fallback when
   it is refused. Section 7's sentence is then rewritten to say so, in the same change (CLAUDE.md section 15).
2. **Change the HUD.** Restore a Q medallion to `activebattle` in `hud-areas.json` and make Attack always-melee. That
   honours the canon as written but is a posture/HUD change outside this WO.

**Shipped in the meantime:** `[ranger-fallback-spends-no-arrow]` - exactly ONE `TryCast` in the handler, and the
WO-1105 R3 no-double-refund gate still intact. **Versus today the ranger's behaviour is unchanged except that the
dagger now fills refusals**, so nothing regresses while the ruling is pending.

## 0.8 Two presentation gaps, recorded not fixed
- **The mage's fallback swing plays the CAST animation.** `PlayerAttackController.StartAttack:513-517` calls
  `_actor.PlayCast()` for `mage`/`cleric`, so a free melee swing will read visually as a spell. Damage resolves
  normally. Presentation only.
- **Section 3.4 (the player must SEE which primary is live) is NOT implemented.** The dock caption still shows the Q
  verb; the cooldown sweep is the only tell. A face-swap is a creative call and was flagged rather than invented.

## 0.9 One more unverified item worth a capture
Wind-up: `TryCast` also refuses while a cast is in flight (`HeroAbilities.cs:802`), so the hero now swings on that too.
`StartAttack` reads `_abilities` only for class/attack-speed and never cancels an in-flight cast - **but** it re-triggers
the same animator `Cast` trigger for mage/cleric. **NOT VERIFIED at runtime.** If it looks wrong in play, the fix is a
public `IsCasting` on `HeroAbilities` and skipping the fallback on that one refusal.
