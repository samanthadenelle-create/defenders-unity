# ECHOES OF ELARION
## HEARTBOUND SKR RESONANCE SYSTEM
### Detailed Implementation Work Orders

**Feature Codename:** HEARTBOUND  
**External Technology:** Solana Mobile native SKR staking  
**Player-facing Currency/Concept:** Echo Resonance  
**Core Fantasy:** SKR staked with Solana Mobile resonates with the Heart of Elarion, causing the kingdom to evolve and experience passive Echo Events.

---

# PRODUCT RULES

These rules are non-negotiable across every work order.

1. SKR remains staked through the native Solana Mobile staking program.
2. Echoes of Elarion never takes custody of the player's SKR.
3. Players do not transfer SKR to Elarion to participate.
4. Players do not spend SKR to trigger Echo Events.
5. Elarion reads verified public on-chain staking state.
6. The backend is authoritative for game rewards.
7. The Unity client must never be trusted to report the amount of SKR staked.
8. SKR staking itself continues providing its normal external staking rewards.
9. Heartbound provides separate game-native benefits.
10. Heartbound bonuses must enhance gameplay without creating pay-to-win combat dominance.
11. Any SKR feature must continue functioning safely if Solana RPC temporarily becomes unavailable.
12. Existing gameplay must remain fully playable without owning or staking SKR.

---

# HEART-001
## Native SKR Staking Verification Layer

### Objective

Create a backend service capable of determining the amount of SKR currently natively staked by the wallet linked to an Elarion player account.

### Architecture

Unity:

`Player`
→ `Existing Wallet Connection`
→ `Authenticated Player Wallet Link`

Backend:

`Player ID`
→ `Verified Wallet`
→ `SKR Staking Verifier`
→ `Solana Mainnet`
→ `StakeConfig + UserStake`
→ `Heartbound Snapshot`

### Requirements

Codex must first inspect the existing repository and identify:

- current wallet authentication
- Solana integration
- backend framework
- player identity model
- API conventions
- persistence layer
- existing caching implementation

Do NOT introduce a second backend framework if one already exists.

Do NOT duplicate wallet authentication.

### Data Source

Use the native Solana Mobile SKR staking program.

Relevant program configuration must live in configuration/constants and not be duplicated throughout gameplay code.

Current official SKR staking uses:

`SKR Mint`
`SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3`

`Staking Program`
`SKRskrmtL83pcL4YqLWt6iPefDqwXQWHSw9S9vz94BZ`

SKR uses **6 decimal places**.

The staking integration must obtain:

- user shares
- current staking share price
- calculated staked SKR
- unstaking amount
- unstake timestamp
- cooldown state
- guardian pool
- Solana slot/block context where practical
- verification timestamp

The official Solana Mobile implementation calculates staked SKR from:

`user shares × current share price`

rather than assuming the original deposit amount remains static.

### New Model

`SkrStakeSnapshot`

Fields:

- playerId
- walletAddress
- guardianPool
- sharesRaw
- sharePriceRaw
- activeStakedSkr
- unstakingSkr
- unstakingReady
- verifiedAtUtc
- sourceSlot
- verificationStatus
- errorCode nullable

### Verification Status

Allowed states:

- VERIFIED
- NO_STAKE
- RPC_UNAVAILABLE
- ACCOUNT_NOT_FOUND
- WALLET_NOT_LINKED
- INVALID_RESPONSE
- STALE

### Refresh Rules

Verify:

- at authenticated game login
- when Heartbound screen opens and cache is stale
- following wallet-link changes
- from background Heartbound processing
- after a player manually requests refresh

Do not hammer RPC on every UI render.

Recommended initial cache:

`5 minutes`

Manual refresh cooldown:

`60 seconds`

### Security

The client may request:

`GET /heartbound/status`

The client may NOT submit:

`stakedSkr = 500000`

or any equivalent staking value.

Server resolves wallet from authenticated player identity.

### Acceptance Criteria

- Player with no SKR stake returns NO_STAKE.
- Player with SKR stake returns calculated active stake.
- Unstaking amount is not counted as actively resonating SKR.
- RPC failure does not incorrectly turn stake into zero.
- Client cannot modify staking value.
- Reconnecting the same authenticated wallet does not create duplicate Heartbound accounts.
- Values are calculated using integer/raw values before display conversion.
- Mainnet is explicitly used for SKR verification.

---

# HEART-002
## Heartbound State and Persistence

### Objective

Create Elarion's persistent interpretation of the verified SKR staking state.

The blockchain answers:

**"What is staked now?"**

Heartbound answers:

**"What has this player's bond with the Heart become?"**

### Model

Create:

`HeartboundState`

Fields:

- playerId
- walletAddress
- activatedAtUtc
- lastVerifiedAtUtc
- lastActualStake
- effectiveResonatingStake
- continuousPulseCount
- totalLifetimePulses
- lastGlobalPulseId
- lastPlayerPulseId
- resonanceScore
- resonanceTier
- highestLifetimeTier
- currentTreeResonanceStage
- pendingEchoEvents
- lastEchoEventId
- status
- version

### Status

- DORMANT
- ACTIVE
- STALE
- UNSTAKING
- DISCONNECTED
- SUSPENDED

### Activation

The first time verified active SKR is detected:

1. Create HeartboundState.
2. Record activation timestamp.
3. Grant the player immediate Heartbound visual acknowledgement.
4. Begin effective stake ramp.
5. Do NOT retroactively award historical Elarion pulses.

Historical SKR staking before joining Elarion does not create retroactive game rewards.

### Wallet Change

Heartbound follows the authenticated player account, but staking must always be verified against the currently linked wallet.

Changing wallet:

- stops rewards from the old wallet
- requires new verification
- does not transfer the old wallet's effective staking weight
- retains lifetime cosmetic achievements
- starts a new continuous staking streak

---

# HEART-003
## Resonance Mathematics and Anti-Whale Curve

### Objective

Convert native SKR staking into a game score where:

- more SKR matters
- duration matters
- whales receive diminishing returns
- temporarily staking large quantities cannot instantly reach maximum resonance
- reducing stake takes effect immediately

### Minimum Stake

Initial configurable eligibility floor:

`MIN_HEARTBOUND_STAKE = 100 SKR`

Keep this server-configurable.

### Effective Resonating Stake

Never use raw current stake directly for full gameplay power.

When stake increases:

`effectiveStake` ramps toward `actualStake`.

Initial activation:

`effectiveStake = actualStake × 0.25`

Each successful Heart Pulse:

`effectiveStake += (actualStake - effectiveStake) × 0.25`

When stake decreases:

`effectiveStake = MIN(actualStake, effectiveStake)`

Therefore:

- increases earn trust over time
- decreases take effect immediately

This kills flash-staking exploits.

### Stake Power

Use logarithmic diminishing returns:

`StakePower = 1000 × log10(1 + EffectiveStake / 250)`

Examples are not contractually fixed and should be covered by unit tests.

Approximate raw StakePower:

- 100 SKR: 146
- 500 SKR: 477
- 1,000 SKR: 699
- 5,000 SKR: 1,322
- 10,000 SKR: 1,613
- 25,000 SKR: 2,004
- 50,000 SKR: 2,303
- 100,000 SKR: 2,603
- 250,000 SKR: 3,000
- 500,000 SKR: 3,301

This is intentional.

500,000 SKR does NOT provide 500 times the power of 1,000 SKR.

### Tenure Power

Continuous participation adds:

`TenurePower = MIN(700, 150 × ln(1 + ContinuousPulseCount / 5))`

### Final Score

`ResonanceScore = FLOOR(StakePower + TenurePower)`

### Resonance Tiers

| Tier | Name | Score |
|---|---|---:|
| 0 | Silent | <300 |
| I | Emberbound | 300 |
| II | Rootbound | 600 |
| III | Stonebound | 900 |
| IV | Echoing | 1,200 |
| V | Awakened | 1,500 |
| VI | Hearttouched | 1,800 |
| VII | Deep Resonance | 2,150 |
| VIII | Heartforged | 2,500 |
| IX | Eternal Echo | 2,850 |
| X | Heartbound | 3,200 |

Thresholds must be configuration-driven.

Do not scatter them through gameplay code.

### Tier Downgrades

If stake decreases, tier may decrease.

However:

- `highestLifetimeTier` never decreases
- earned cosmetics remain
- gameplay bonuses follow current tier
- lifetime achievements remain visible

---

# HEART-004
## Native SKR Heart Pulse Detector

### Objective

Make actual SKR staking activity become the heartbeat of Elarion.

### Core Concept

SKR staking rewards cause the staking system's share price to increase.

Elarion monitors the authoritative global staking configuration.

When:

`newSharePrice > previouslyProcessedSharePrice`

create:

`GlobalHeartPulse`

This ties Elarion's Heart directly to native SKR staking activity rather than an arbitrary game timer.

### GlobalHeartPulse Model

Fields:

- pulseId
- sequenceNumber
- previousSharePrice
- newSharePrice
- detectedAtUtc
- sourceSlot
- chainReference
- status
- processedAtUtc

### Processing

There must be exactly one global pulse for a unique observed share-price advancement.

All eligible Heartbound players are then processed against that pulse.

### Player Eligibility

At pulse processing:

1. Obtain fresh or acceptable verified staking state.
2. Confirm active stake >= minimum.
3. Confirm wallet relationship.
4. Update effective stake.
5. Increment continuousPulseCount.
6. Increment totalLifetimePulses.
7. Recalculate resonance.
8. Generate one Echo Event.
9. Update Tree resonance.
10. Persist transactionally.

### Idempotency

Unique constraint:

`playerId + globalPulseId`

A pulse can never pay the same player twice.

Retrying failed background jobs must be safe.

### RPC Failure

If SKR verification temporarily fails:

- do not reset the player's streak
- do not generate unverified rewards
- mark processing as pending
- retry later

Suggested stale grace:

`72 hours`

After successful verification, pending eligible pulses may be processed.

Cap catch-up processing to prevent pathological backlogs.

Initial cap:

`5 pending pulses`

Anything beyond this should require explicit reconciliation rather than silently vomiting six months of resources into someone's castle.

---

# HEART-005
## Echo Event Engine

### Objective

Every successful Heart Pulse should cause the player's kingdom to experience something.

Not:

`+5 currency`

Instead:

**The world reacted.**

### Naming

Player-facing term:

**Echo Event**

Never use:

- lottery
- jackpot
- bet
- wager

No SKR is consumed.

### Seed

Each event must be server-authoritative and deterministic.

Suggested seed:

`SHA256(globalPulseId + playerId + eventTableVersion)`

If appropriate, include a stable on-chain pulse reference in the seed.

Same inputs must always return the same event.

### Event Table V1

#### Rough Stone Discovery

An Echo worker discovers a rough stone.

Result:

- one rough stone added to inventory
- stone can later enter the existing polishing/gem loop

#### Resource Surge

One gathering structure receives a temporary production surge.

Suggested duration:

`2 hours`

Do not exceed the overall Heartbound economic power budget.

#### Scout's Whisper

Scouts reveal useful information about an available raid.

Examples:

- enemy composition
- resistance
- recommended troop type
- reward preview refinement

Prefer information over raw combat strength.

#### Crafting Inspiration

One crafting action receives a small temporary convenience benefit.

Examples:

- reduced crafting time
- reduced mundane material requirement

Never provide premium-equivalent currency directly.

#### Echo Labor

An Echo worker assists the settlement.

Examples:

- short construction acceleration
- collection assistance
- queue convenience

#### Wandering Merchant

A rare merchant temporarily visits the player's settlement.

Inventory must use game-native items.

SKR is not the purchase currency.

#### Heartfire Spark

The Heart produces a small Heartfire-related game benefit.

This must integrate with existing Heartfire mechanics rather than introduce a second Heartfire system.

#### Echo Bloom

The Tree of Life blooms following a Heart Pulse.

Produces:

- visual transformation
- special interaction
- small bounded gameplay reward

#### Ancient Echo

Extremely rare narrative event.

Examples:

- lore fragment
- NPC apparition
- unique visual
- collectible codex entry
- cosmetic banner component

The ideal Ancient Echo reward is memorable, not economically massive.

### Important Design Rule

Higher SKR tiers should primarily unlock:

- more event varieties
- richer world reactions
- cosmetic transformations
- convenience

They should NOT simply multiply resources forever.

---

# HEART-006
## Ten-Tier Passive Benefit System

### Objective

Give every Heartbound tier a meaningful passive identity.

### Tier I: Emberbound

Unlock Heartbound.

Passive:

`+2% offline gathering efficiency`

Tree receives faint Heartfire particles.

### Tier II: Rootbound

Passive:

Echo Labor becomes available in the event pool.

Minor glowing roots become visible around the Tree.

### Tier III: Stonebound

Unlock:

**Rough Stone Discovery**

Heart Pulse can produce polishable stones.

### Tier IV: Echoing

Unlock:

**Scout's Whisper**

Heartbound now interacts with the raid loop.

No direct damage buff.

### Tier V: Awakened

Unlock:

**Crafting Inspiration**

Small crafting convenience events become available.

### Tier VI: Hearttouched

Unlock:

**Heartfire Spark**

Heartfire begins visibly responding to SKR pulses.

### Tier VII: Deep Resonance

Unlock:

**Echo Worker Manifestations**

Temporary Echo figures can appear in the settlement while passive effects are active.

### Tier VIII: Heartforged

Unlock:

**Echo Bloom**

The Tree can enter a rare bloom state following eligible pulses.

### Tier IX: Eternal Echo

Unlock:

**Ancient Echo**

Rare narrative/cosmetic encounters enter the event table.

Kingdom-wide ambient resonance becomes visible.

### Tier X: Heartbound

Unlock full:

**Heartbound Kingdom**

Benefits:

- maximum Tree resonance appearance
- exclusive settlement aura
- Heartbound banner/crest
- enhanced Echo-event presentation
- one additional event-choice mechanic

Instead of simply granting a stronger reward, Tier X may present:

`Choose one of three Echo manifestations`

The underlying economic value must remain bounded.

Tier X should feel prestigious rather than economically mandatory.

---

# HEART-007
## Tree of Life World Integration

### Objective

Make Heartbound visually impossible to mistake for a wallet balance screen.

The Tree of Life becomes the visible receiver of SKR resonance.

### Mapping

Use the existing Tree progression assets wherever possible.

Do not create ten completely separate Tree models unless art review determines it is necessary.

Map ten resonance tiers into approximately six base Tree visual states.

Then differentiate tiers using:

- particle density
- Heartfire intensity
- root illumination
- Echo particles
- ambient wisps
- ground markings
- bloom effects
- crown illumination
- occasional Echo silhouettes

### State Changes

When the player's tier increases:

1. Camera may subtly acknowledge Tree.
2. Heart pulse travels through roots.
3. Tree VFX intensifies.
4. New resonance tier appears.
5. Newly unlocked passive system appears.
6. Celebration occurs once.

Do not replay a giant celebration every login.

### Heart Pulse Presentation

When a player logs in after receiving one or more pulses:

Tree emits:

`THUMP`

Not literal text.

Visual sequence:

Heartfire center
→ root illumination
→ outward pulse
→ surrounding buildings briefly catch the light
→ Echo Event reveal

Target duration:

`2.5 to 4 seconds`

Must be skippable after first viewing.

### Performance

Mobile first.

Avoid:

- huge particle counts
- permanent transparent overdraw
- expensive real-time lights
- heavy shader permutations

Use pooled VFX.

---

# HEART-008
## Heartbound UI / UX

### Objective

Create a simple player-facing screen explaining the system without requiring knowledge of staking mechanics.

### Entry

Add a Heartbound access point near the Tree of Life and/or appropriate existing Web3/wallet surface.

Do NOT turn the main HUD into a crypto dashboard.

### Main Panel

Header:

**HEARTBOUND**

Primary display:

`27,481 SKR Resonating`

Secondary:

`Tier VII - Deep Resonance`

Show:

- verified staked SKR
- effective resonating SKR
- Resonance Score
- current tier
- continuous Heart Pulse streak
- total Heart Pulses
- current passive effects
- next tier progress
- last Heart Pulse
- latest Echo Event

### Explain Effective Stake

Tooltip:

**Resonating SKR**

"Your bond strengthens over successive Heart Pulses. New stake begins resonating immediately and reaches full strength over time."

Do not expose the anti-exploit implementation as punitive language.

### Trust Statement

Display concise text:

"Your SKR remains staked through Solana Mobile. Echoes of Elarion reads your public staking status and never takes custody of your SKR."

### No Stake State

Do not show a giant BUY button.

Show:

**The Heart is Silent**

"No native SKR stake was detected for this wallet."

Provide informational path to staking where appropriate.

### Unstaking State

Show:

**The Echo Weakens**

"SKR currently leaving active staking no longer contributes to Resonance."

### RPC Failure

Never display:

`0 SKR`

because RPC failed.

Show last known data with:

**Verification temporarily unavailable**

`Last verified: <time>`

---

# HEART-009
## Passive Economy Guardrails

### Objective

Prevent Heartbound from destabilizing Elarion's economy.

### Maximum Economic Effect

Direct recurring production modifiers from Heartbound should initially remain below:

`10% combined effective economic acceleration`

This does not include:

- cosmetics
- information
- visual effects
- lore
- convenience without resource generation

### Forbidden Rewards

Do not award from Heartbound pulses:

- SKR
- SOL
- USDC
- withdrawable tokens
- tradable financial assets
- uncapped premium currency

### Preferred Rewards

Favor:

- rough stones
- information
- time-limited boosts
- queue convenience
- cosmetic progression
- lore
- Heartfire interaction
- temporary NPCs
- bounded crafting assistance

### Config

All reward amounts and weights belong in data/config.

No reward values hardcoded in presentation classes.

---

# HEART-010
## Exploit Protection and Failure Handling

### Required Scenarios

Test:

**Flash stake**

Player stakes large amount immediately before login.

Expected:

Only initial portion enters effective resonance.

**Stake reduction**

Player reduces active stake.

Expected:

Effective stake falls immediately.

**Full unstake**

Expected:

No future pulses awarded while active stake is below threshold.

**Cancel unstake**

Expected:

System resumes after chain verification.

Do not restore missed pulses unless eligibility can be verified.

**Wallet switching**

Expected:

Old wallet stops contributing immediately after account relationship changes.

**Client tampering**

Modified APK reports false stake.

Expected:

Ignored.

**RPC outage**

Expected:

No streak destruction and no unverified reward generation.

**Duplicate background job**

Expected:

Unique player + pulse constraint prevents duplicate event.

**Clock manipulation**

Changing Android device time has no effect.

Server UTC and chain state are authoritative.

**Offline player**

Expected:

Eligible player can receive pending Echo Events through backend processing.

---

# HEART-011
## Telemetry and Balance Analytics

### Objective

Instrument Heartbound from day one.

### Events

Track:

`heartbound_detected`

`heartbound_activated`

`skr_verification_success`

`skr_verification_failed`

`heart_pulse_global_detected`

`heart_pulse_player_processed`

`heart_pulse_player_deferred`

`resonance_score_changed`

`resonance_tier_up`

`resonance_tier_down`

`echo_event_generated`

`echo_event_claimed`

`echo_event_expired`

`heartbound_screen_opened`

### Properties

Where appropriate:

- tier
- effectiveStake bucket
- actualStake bucket
- streak bucket
- event type
- player level
- account age
- source platform
- processing duration
- RPC latency

Do not put unnecessary wallet addresses into general analytics.

### Questions Analytics Must Answer

1. What percentage of players have SKR staked?
2. Does Heartbound increase retention?
3. Which tiers contain most users?
4. Are Echo Events being claimed?
5. Which events players interact with most?
6. Is Heartbound materially inflating resources?
7. Does staking duration correlate with retention?
8. Are users increasing stake after discovering Heartbound?
9. Are whales gaining disproportionate gameplay advantage?
10. Does the Tree presentation cause players to open Heartbound?

---

# HEART-012
## Regression and Automated Test Package

### Unit Tests

Cover:

- raw SKR conversion
- share-to-token conversion
- effective stake ramp
- immediate stake reduction
- StakePower curve
- TenurePower
- resonance thresholds
- tier upgrades
- tier downgrades
- max tenure
- deterministic Echo Event generation
- idempotent pulse processing
- reward caps
- stale state

### Integration Tests

Mock:

- StakeConfig account
- UserStake account
- no UserStake account
- stake amount increase
- unstaking state
- share-price increase
- RPC timeout
- malformed RPC response

### End-to-End

Required E2E scenario:

1. Existing player links wallet.
2. Native SKR stake detected.
3. Heartbound activates.
4. Tree changes state.
5. Global share-price advancement is detected.
6. Heart Pulse created.
7. Player eligibility verified.
8. Effective stake advances.
9. Resonance recalculates.
10. Echo Event generated.
11. Player logs in.
12. Tree pulses.
13. Echo Event appears.
14. Reward is granted once.
15. Reopening screen cannot duplicate reward.

Add all tests to the existing regression framework.

No implementation is complete until regression is green.

---

# HEART-013
## Grant / Reviewer Demo Path

### Objective

Create one extremely clear sequence demonstrating why this is more than a token gate.

### Demo Sequence

Start with player standing near Tree of Life.

Open Heartbound.

Show:

**Native SKR Detected**

Show verified stake.

Return to kingdom.

Trigger controlled demonstration of a legitimate previously-recorded Heart Pulse.

Tree Heartfire activates.

Roots illuminate.

Settlement receives pulse.

Display:

**THE HEART REMEMBERS**

Then:

**Echo Event: Rough Stone Discovered**

Open the discovered rough stone.

Show that it enters the existing stone/gem progression.

Return to Heartbound.

Show:

`Native SKR`
→ `Resonance`
→ `Heart Pulse`
→ `Living Kingdom`

### Reviewer Message

The feature should communicate without a paragraph of explanation:

**Native SKR staking is not merely checked for access. Its live staking state becomes part of the simulation of Elarion itself.**

### Demo Rule

Do NOT fake blockchain state in the production build.

A development-only pulse simulator may exist behind development flags for repeatable automated testing and video capture.

It must never compile into production behavior capable of granting real player rewards.

---

# IMPLEMENTATION ORDER

Recommended build sequence:

**Wave 1: Foundation**

HEART-001  
HEART-002  
HEART-003

No major UI yet.

Prove native SKR can reliably become authoritative HeartboundState.

**Wave 2: The Heart Beats**

HEART-004  
HEART-005  
HEART-010

Prove one real SKR staking inflation event can result in exactly one idempotent player Echo Event.

**Wave 3: Gameplay**

HEART-006  
HEART-009

Wire bonuses into existing systems through interfaces.

Do not let Heartbound directly own gathering, crafting, raid, inventory, or Heartfire logic.

Example:

`IHeartboundBonusProvider`

Gameplay systems ask the provider what modifiers currently apply.

They do not query Solana.

**Wave 4: Presentation**

HEART-007  
HEART-008

The UI consumes HeartboundState.

UI contains no staking calculations.

**Wave 5: Harden**

HEART-011  
HEART-012

Full telemetry and regression.

**Wave 6: Reviewer Path**

HEART-013

Capture the SKR → Heart Pulse → Tree → Echo Event experience.

---

# ARCHITECTURAL RULE

This dependency direction is mandatory:

`Solana`
↓
`SKR Verification`
↓
`Heartbound Model`
↓
`Resonance Engine`
↓
`Bonus Provider / Echo Event Engine`
↓
`Existing Game Systems`
↓
`UI + VFX`

Never:

`UI`
→ directly queries blockchain
→ directly awards item

And never:

`Gathering System`
→ knows what SKR is

To Gathering, Crafting, Raids, Inventory and the Tree, Heartbound is simply another authoritative gameplay modifier provider.

This preserves the model-first architecture and keeps Web3 concerns from infecting the rest of the codebase.

---

# DEFINITION OF DONE

Heartbound is considered complete when:

- real native SKR stake is verified from Solana mainnet
- Elarion never custodies SKR
- client cannot spoof stake
- actual staking share-price advancement generates a global Heart Pulse
- each eligible player processes each pulse exactly once
- increased stake ramps into gameplay power
- decreased stake reduces power immediately
- duration contributes meaningfully
- whales experience logarithmic diminishing returns
- ten resonance tiers function
- Echo Events function
- Tree visibly responds
- offline players are supported
- RPC outages do not destroy streaks
- all rewards remain economically bounded
- telemetry is present
- automated regression is green
- production has no debug reward bypass
- non-SKR players retain the complete core game experience