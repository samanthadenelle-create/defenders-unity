# Heartbound — how Seeker native staking becomes the essence of the Heart of Elarion

Date: 2026-09-16. Short brief for a host conversation.
Authority: `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md` (design), WO-1674..1682
(what is built). Labels: **LIVE**, **BUILT (server)**, **SPEC**.

## The fantasy

The Heart of Elarion is the world tree at the centre of the player's town. It holds the Echoes of
everyone who came before, and the realm rebuilds beneath its branches. SKR staked natively with
Solana Mobile never moves, but the Heart hears it: staked SKR **resonates** with the Heart, the
kingdom evolves around it, and the Heart pulses with Echo Events. The player's stake stays where it
is, earning what native staking normally earns. Elarion only reads it and answers.

## How it works

- **Verification (LIVE):** the backend reads the wallet's public native staking position and issues
  a verified snapshot. The Unity client is never trusted to say how much is staked.
- **Echo Resonance (BUILT):** one score from two powers. *Stake power* comes from the effective
  resonating stake, which ramps in over successive pulses and follows an anti-whale curve. *Tenure
  power* grows with the count of continuous pulses on a log curve, so time faithful matters as much
  as size. Minimum eligibility is 100 SKR.
- **Ten tiers (BUILT):** Emberbound, Rootbound, Stonebound, Echoing, Awakened, Hearttouched,
  Deep Resonance, Heartforged, Eternal Echo, Heartbound. Thresholds are server configuration.
- **The Heart Pulse (BUILT):** when the native staking program's share price is observed to advance,
  a Heart Pulse is minted, at most one per day, ceremonial. Every eligible player is processed once
  against that pulse. Unverified or stale stake pays nothing.
- **Echo Events (BUILT, not yet delivered to the client):** each pulse can reveal a passive kingdom
  event. Players never spend SKR to trigger one.
- **Benefits (first one LIVE):** game-native, additive, never combat dominance. Extra jewel-polishing
  attempts today; passive production boosts capped under 10% combined across the whole ladder;
  information, access and prestige at higher tiers; Tier X presents choices rather than a bigger number.
- **In the world (SPEC):** the Heart itself shows the tier: root illumination, Heartfire intensity,
  Echo particles, crown bloom. A tier-up is celebrated once. A pulse plays as Heartfire, then the
  roots, then an outward wave the surrounding buildings catch, then the event reveal, 2.5 to 4 s,
  skippable after the first time.

## The rules we hold to

No custody, no transfer of SKR to Elarion, no spending SKR to play, backend authoritative for every
reward, safe when Solana RPC is down (last verified state within a bounded grace), and the whole game
stays fully playable without owning or staking SKR.
