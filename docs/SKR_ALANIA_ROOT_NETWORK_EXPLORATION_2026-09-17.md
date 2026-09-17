# The Root Network — SKR as ancestral life force, not currency

Date: September 17, 2026. Status: **design exploration, not a work order.** Nothing here is scoped,
scheduled, or approved for implementation. This is the working document for digging deeper before
anything gets turned into a WO.

## How Echoes of Elarion actually plays today — for anyone reading this without prior context

This section exists so this document is self-contained and can circulate to another AI, another
collaborator, or a fresh reviewer with zero assumed context.

**The game.** "Echoes of a Forgotten Civilization" (also called "Echoes of Elarion") is a mobile
RPG combining town-building, tower-defense-style wave combat, and dungeon exploration, built in
Unity for Android (primary) and Solana Mobile's Seeker device specifically, with a Google Play
build variant that strips all crypto/wallet surfaces per store policy.

**The core loop.** The player rebuilds and defends a town called Elarion, centered on the Heart of
Elarion — a world-tree/reliquary at the town's origin. Waves of enemies attack on a countdown
cycle; the player builds walls, towers, and defensive structures between waves, trains troops, and
personally fights alongside their hero and a squad of companions during combat. Waves escalate in
difficulty and, past a point, enter an endless mode with rising pressure (extra attacking sides,
tougher enemy types, apex bosses).

**The hero.** The player picks one of several hero classes (Knight, Ranger, Mage, Cleric confirmed
in this project), each with distinct abilities, and levels them up through combat and quests.

**Echoes (companions).** The player collects "Echoes" — described in the game's own fiction as the
essence of a person the hero is guarding or has saved. Echoes assist with resource harvesting,
passive base repair, and combat support, and are a core progression/collection system.

**Raids.** Separate from town defense, the player can raid other bases (enemy camps, garrisons, and
outposts) to capture them, ideally earning stars based on performance, unlocking rewards and
progression tied to how well the raid went.

**Dungeons.** A separate exploration pillar: procedurally-composed dungeon levels with combat,
loot, hazards, and (per this session's work) a lantern/torch fuel mechanic that limits how long a
player can stay in without resupplying.

**The store and monetization.** A player-built town is the core strategic surface (no separate
"base" screen). There's a real-money/crypto store (packs of in-game currency and items), with SKR
support live today: flat-SKR pricing (packs are priced in a fixed SKR amount, not a floating
USD-to-SKR conversion) and read-only SKR staking verification tied to a "Heartbound" companion
bonus mechanic. The project is explicitly pre-revenue as of this session and focused on a Solana
Mobile hackathon submission with a real prize track for creative SKR use.

**What SKR actually is, technically, for this document's purposes.** SKR is Solana Mobile's
governance and incentive token for the Seeker phone and its dApp Store ecosystem — not a
general-purpose smart-contract token with arbitrary custom logic available to a third-party app.
A mobile game can realistically: read a wallet's SKR balance, read whether/how much a wallet has
staked, accept SKR as payment for in-game goods (one-directional; already live here as flat-SKR
pricing), and observe staking-related events over time. A mobile game cannot, without building and
deploying its own separate on-chain program (a materially larger undertaking than reading existing
chain state), implement custom multi-signature fusion mechanics, forcibly seize or freeze a
player's tokens, or otherwise add new rules to how SKR itself behaves. Every idea below is
evaluated against that real technical ceiling, not against what would be true of a token the game
fully controlled.

## The core idea, as the owner stated it

If the central spire holds the sleeping souls of a dead civilization waiting for a hero, SKR
shouldn't function as a traditional currency used to buy walls or cannons. Instead, SKR is the
literal life force of the ancestors buried in the tree. Staking becomes a strategic, emotional
base-building decision — not a purchase.

Three mechanics, as proposed:

1. **The Root Network (staking = defensive specialization).** The player stakes SKR into named
   Roots of the tree, each mapped to a real validator on the backend. Whichever Root holds the
   stake determines which ancestors are "awake" and what they grant: the Deep Root awakens
   stonemasons (wall armor + passive regen), the Canopy Root awakens pyromancers (archer splash
   damage). The base's defensive identity is dictated by where the stake currently sits, not by
   what the player bought.

2. **The Awakening (unstaking = a tactical ultimate).** When the final wall is about to fall, the
   player can trigger an unstake. This manifests the ancestors as a massive map-clearing event —
   the town's last stand made literal. The cost is real: the unbonding cooldown (owner's stated
   assumption: 48 hours) leaves that Root's passive buffs completely dead for the duration, so the
   player is defending with bare strategy and traps until the spirits return.

3. **Yield as organic expansion.** Staking yield accruing on-chain is read as the tree's physical
   growth. As yield accumulates, the canopy visibly expands in the client, pushing back the fog of
   war and the enemy spawn line, opening new buildable plots.

The framing that makes this work: nothing here is pay-to-win in the traditional sense. A player
can't buy a stronger tower. They have to decide where to move a finite, already-owned resource
(their own staked position) to counter what's coming — which is a real strategic decision with a
real cost (the Awakening's cooldown, the opportunity cost of which Root is empowered right now),
not a checkbox purchase.

## What's already real vs. what this would need — read at source, not assumed

This project already has actual read-only SKR staking verification wired up (`skr-staking.js`,
`heartbound/status.js` — confirmed present this session during an unrelated audit). That's real
groundwork: the game can already ask "does this wallet have a stake, and how much" without
inventing anything new. What none of the three mechanics above have yet:

- **Per-Root stake allocation.** Today's staking read is almost certainly a single aggregate
  balance/stake check, not "which named validator/Root is this wallet's stake sitting in right
  now." If Solana staking natively supports delegating to different validators (it does, in
  general), the Root Network idea maps onto that reasonably cleanly — but this needs to be
  verified against what the actual staking program in use supports before assuming a clean 1:1
  mapping between "validator" and "Root."
- **A real unstake-initiate hook the game can observe.** The Awakening needs the game to detect
  "this wallet just initiated an unstake" and the cooldown countdown, not just a point-in-time
  balance. That's a different, harder read than a balance check.
- **The 48-hour figure needs verification, not assumption.** Solana's unbonding/deactivation
  period is a network/epoch-driven parameter, not a fixed wall-clock constant the game controls,
  and it can differ by staking mechanism (native stake account vs. a liquid-staking wrapper token
  like SKR itself might use). Confirm what SKR's actual unbonding period is before writing it into
  any player-facing copy or mechanic timing.
- **Yield-to-canopy-growth needs a defined read cadence and a defined mapping function**, not just
  "yield grows the tree." How often is yield checked, what's the growth curve, does it ever shrink
  (can the canopy recede if a player unstakes/moves stake away, matching the fiction that the
  ancestors' presence is what's actually growing the tree)?

None of this is a blocker — it's exactly what "digging deeper" means before this becomes a WO.

## Cross-check against the standing governing review (`SKR_VISION_RECONCILIATION_2026-09-11.md`)

That review is binding context, not optional reading, for anything SKR-related in this project.
Relevant constraints it already established, checked against this concept:

- **No SKR-to-crystal cash-out loop.** ✅ Clean — nothing in the Root Network concept converts SKR
  into spendable game currency or vice versa. The player's SKR stays staked (or unstaked back to
  their own wallet) the whole time; the game only ever *reads* where it's parked and *reacts* with
  gameplay effects. This is meaningfully different from the cash-out loop that review explicitly
  ruled out.
- **No player-vs-player wagering.** ✅ Clean — this is a PvE base-defense mechanic. Nothing here
  proposes staking against another player or a payout contingent on beating another player.
- **No unsupported legal claims.** ⚠ Watch this. The review specifically flagged "sponsorship is
  legal in every jurisdiction" and similar unsupported claims as things to strip from any pitch.
  This concept doesn't currently make legal claims, but if it's pitched publicly (hackathon
  materials, a trailer, store copy), avoid any language implying SKR staking through this feature
  constitutes an investment return, a financial product, or anything beyond "your existing staking
  position visibly matters in the game you're playing." Consult before any public copy is written.
- **"Specify wallet linking, recovery, multiple wallets, missing history, and stake changes before
  using it as persistent character identity."** This review's own words, aimed at the separate
  "Bound Echo" concept, apply just as hard here: what happens if the player switches wallets
  mid-game? What if the wallet they staked from has no verifiable history yet? What if they have
  stake split across multiple validators/Roots already, outside the game's control? These are real
  open questions, not edge cases to hand-wave.
- **"Verified local implementation... does not establish a real Arena escrow or token-earning/
  cash-out loop."** Consistent with this concept as described — the Root Network never proposes
  earning new SKR from gameplay, only relocating and reacting to SKR the player already owns.

**Net: this concept reads as compatible with the standing governing review**, specifically because
it never asks SKR to become a currency, a wager, or an earn-to-cash-out loop — it asks SKR to be a
*state* the game observes and dramatizes. That's the safest lane available and it's also, not
coincidentally, the most thematically honest one for "ancestors sleeping in a tree."

## Additional ideas, building on the core three

- **Root conflict, not just Root choice.** Right now the concept reads as "player picks a Root,
  gets that Root's buff." A sharper strategic layer: what if splitting stake across multiple Roots
  simultaneously (if the underlying staking mechanism allows fractional/multi-validator delegation)
  gives partial, diluted versions of multiple buffs instead of one full buff — so "go all-in on
  stonemasons" vs. "spread thin across three ancestor lines" is a real, felt trade-off every wave,
  not a one-time pick?
- **The ancestors remember which Root failed them.** If a wave breaches while a given Root's
  buff is dead (mid-Awakening-cooldown, or simply unstaked), that Root's ancestors could carry a
  narrative "grudge" — a returning reference in flavor text or a slightly harder re-Awakening next
  time — reinforcing that the cost of the Awakening was real and remembered, not just numerically
  penalized.
- **A visible, ambient "who's home" readout**, distinct from a HUD number: since which Root is
  staked determines the town's whole defensive character, the town itself should visibly change
  (which structures glow, which ancestor silhouettes are visible in the canopy) so a player — or a
  spectator watching a hackathon demo — can read the base's current strategy at a glance without
  opening a menu. This is also the single highest-leverage thing for a judge: the mechanic needs to
  be *visually legible*, not just mechanically real.
- **Tie the Awakening's map-clearing visual directly to the game's existing dragon/apex-boss art
  and VFX budget** (this session already built a twin-dragon endless-mode escalation and a
  fire-breath system) rather than inventing a whole new spectacle from scratch — the ancestors
  manifesting could reuse and re-skin existing high-drama assets, which is both cheaper to build
  before a deadline and thematically coherent (their fury takes the shape the civilization's own
  dragons/magic already had).
- **A "first Awakening" moment gets a guaranteed, non-punishing tutorial framing** the first time a
  player ever triggers it — per the governing review's general instinct on RNG/rare-unlock cost
  ("make the first useful tactical unlock guaranteed"), the first time a player learns this
  mechanic exists should not be the first time they're desperate enough to need it blind.

## Open questions to dig into next (not yet answered, not yet a WO)

1. What does the actual on-chain staking mechanism for SKR support — is per-validator delegation
   (the technical basis for "which Root") actually how SKR staking works, or does the token use a
   different staking model that doesn't map this cleanly? This needs a real technical read before
   the Root Network's central mechanic can be confirmed feasible as described.
2. What is SKR's actual unbonding/unstake cooldown period, and is it fixed or epoch-variable? This
   directly determines whether "48 hours" is a real, quotable number or needs to become "a few
   days, varies" in both the design and any player-facing copy.
3. Does a player need to already have staked SKR before this feature does anything for them, or is
   there a fallback/onboarding path for a player who owns SKR but has never staked it? (Relevant
   both for hackathon-judge onboarding, where a judge trying the game cold has no existing stake,
   and for ordinary players.)
4. Is the yield-to-canopy-growth mapping meant to be per-player (my own stake grows my own tree) or
   ecosystem-wide (aggregate network yield grows every player's tree, echoing the World Pulse idea
   from the separate creative deep-dive done earlier this session)? These are different features
   with different technical asks — worth deciding explicitly rather than letting them blur.
5. How does this interact with a hackathon judge's realistic play session length (likely minutes,
   not days)? A 48-hour Awakening cooldown is real and thematically important for a live player,
   but a judge will never see it resolve in one sitting — does the demo need a sped-up/simulated
   mode for presentation purposes, and if so, how is that made honest (clearly labeled as a demo
   mode) rather than misleading about the real mechanic's pacing?

## Where this sits relative to the separate SKR creative deep-dive done earlier this session

A parallel research pass (same session, different framing) produced four other ideas graded
against the same governing review: World Pulse (network-wide staking as ambient weather),
Proof of Vigil (stake *tenure* gates lore, not power), Wallet-Fingerprint Echo (deterministic
cosmetic identity from wallet+stake), and Sponsor's Ward (a sponsor's sustained stake grants a
shared server-wide buff). The Root Network concept in this document is a stronger, more mechanically
integrated idea than any of those four — it's the first one that makes staking a moment-to-moment
strategic decision inside the core gameplay loop rather than a background signal or a cosmetic
flourish. World Pulse's "network health as weather" instinct and this document's per-player Root
allocation are not mutually exclusive; question 4 above is exactly where they'd need to be
reconciled if both are pursued.

## Second creative pass — four more ideas, owner-submitted, evaluated with the same discipline

These four were submitted as a batch with an explicit framing worth preserving: most hackathon
entries use SKR as a currency (buying swords) or a key (unlocking a room), and the way to be
memorable is to exploit its actual real-world baggage — its ties to physical hardware, its staking
mechanics, its governance role — into something a standard token couldn't do. That framing is
right and matches this document's own governing discipline: an idea earns real credit here for
being *specific to what SKR is*, not for being generically clever.

Recorded as submitted, each followed by an honest technical-feasibility and legal-fit check
against the ceiling described above and the governing review.

### 1. Proof of Presence — device-to-device fusion

**As submitted:** SKR balance decays/mutates based on real-world proximity. Two players physically
meet and "sync" their Seeker devices, fusing their SKR into a temporary shared in-game entity. A
third party can't buy into the fusion, because breaking it or transferring it out requires a
signature from both original devices' Seed Vaults — turning SKR into a social contract that can't
be bypassed by market speculation.

**Check:** The core insight — using hardware-backed signing to create something that can't be
bought, only earned through a real relationship — is the strongest *thematic* idea in this batch.
But "the fusion breaks unless both devices co-sign" is a custom multi-party smart-contract rule.
SKR does not natively support that; building it means designing and deploying an entirely separate
on-chain program, which is a materially bigger undertaking than anything else in this document and
almost certainly not buildable inside a hackathon window. **Feasibility: low as literally
specified.** A scaled-down version that keeps the spirit — two Seeker devices in physical proximity
(local Bluetooth/NFC handshake, no chain write at all) unlocking a temporary shared in-game buff or
a co-op-only zone — would deliver the "you had to be there, together, for real" feeling without
needing new on-chain logic. **Legal/policy check: clean** — nothing here moves value between
wallets or promises a return.

### 2. The SKR Staking Oracle — unstake-to-damage boss shields

**As submitted:** Bosses have "Staking Shields" only damageable by unstaking SKR at the exact
moment of attack. Because unstaking has a cooldown, players must pre-commit 48 hours ahead of a
boss fight. Guess wrong and the SKR "stays locked, dealing no damage."

**Check:** There's a real internal inconsistency worth surfacing rather than smoothing over: once
a real unstake is initiated on Solana-family staking, it isn't something that can be "guessed
wrong" and reversed — it proceeds and the tokens become liquid after the cooldown regardless of
what happens in the game meanwhile. So the mechanic as described doesn't quite hold together
technically; what it's actually reaching for is closer to "you must have STARTED an unstake more
than 48 hours before this fight for it to count as a damage-dealing event now" — a real,
observable on-chain fact the game can check without needing the unstake to be conditional on
anything. That version is buildable: read whether a wallet has an unstake in progress and how long
it's been cooling, and let that state gate boss damage. **Feasibility: medium**, once reframed as
"reading a real, already-committed decision" rather than "the token stays magically locked on a
wrong guess." **Legal/policy check: clean** — it's reading an existing on-chain fact, not moving or
freezing anything itself.

### 3. Tokenized Reputation — reverse governance / "Sinner's Contract"

**As submitted:** In-game "sins" forcibly lock a portion of a player's SKR into a tainted contract;
tainted votes accumulate in a "Chaos Pool," and if that pool crosses 51%, every player's assets —
including staked SKR — freeze for a week.

**Check: this is the one to flag hardest, not build.** The governing review is explicit that no
mechanic here should imply SKR functions as anything beyond honest existing purchase/staking
utility, and a mechanic where the *game* forcibly locks or freezes a player's real token holdings
based on their in-game behavior — worse, a mechanic where one player's actions can freeze *every
other player's* assets — is a different category of risk than anything else in either creative
pass. It reads, to a non-technical reviewer, uncomfortably close to an unauthorized seizure of
value the player never agreed to at the moment it happens, and doing it to an entire playerbase at
once compounds that. The underlying narrative idea — a visible, game-tracked reputation ledger for
betrayal/treaty-breaking — is genuinely good fiction for a "forgotten civilization" story. Keep the
reputation ledger; drop the part where it touches real tokens at all. A "Sinner's Mark" that's
purely an in-game cosmetic/narrative flag (visible to other players, affects which NPCs trust you,
never touches wallet state) gets the same storytelling value with none of the risk.
**Legal/policy check: do not build as specified.**

### 4. Hardware-Bound Rent — Seeker device leasing

**As submitted:** SKR grants access to lease other players' idle Seeker hardware (or just its
signing capability) to "mine" rare in-game resources; the device owner gets a cut, the renter gets
the gameplay advantage — landlords who own hardware but don't play, renters who play but don't own.

**Check:** This is DePIN-flavored and thematically interesting, but it assumes an API surface that
does not appear to exist: nothing in Solana Mobile's documented Seed Vault or Seeker hardware
exposes third-party apps a way to lease another user's device's idle compute or signing capacity.
Seed Vault is a secure key-storage/signing facility for the device's *own* owner, not a
peer-to-peer compute marketplace. **Feasibility: low, likely not buildable on the actual platform
as described**, unless there's a specific Solana Mobile SDK capability for this that hasn't
surfaced in this project's research yet — worth a direct, narrow technical check before spending
any more design time here, rather than continuing to design against a capability that may not
exist.

### Where this leaves the full set

Ranked across both creative passes by the combination of "genuinely SKR-specific" and "actually
buildable before a hackathon deadline": **World Pulse** (pass one) and the **Root Network** (the
main concept in this document) remain the strongest two — both are clean on technical feasibility
and legal fit, and both are load-bearing enough to be a demo's centerpiece rather than a side
feature. **Proof of Presence**, scaled down to a proximity handshake with no chain write, and the
**reframed Staking Oracle** (reading committed unstakes, not magic locking) are worth a second look
as secondary features layered onto either of those two. **Tokenized Reputation** should keep its
narrative core and lose every part that touches real tokens. **Hardware-Bound Rent** needs one
direct technical question answered — does Solana Mobile expose anything resembling device-leasing
at all — before it's worth designing further.
