# SKR in Echoes of Elarion — implementation plan (one-page brief)

Date: 2026-09-16. For: external conversation with a Solana Mobile / Seeker host.
Source of truth: `docs/SKR_CURRENT_USE_RESEARCH_BRIEF_2026-09-11.md` (what is live vs. authored),
`docs/SKR_VISION_RECONCILIATION_2026-09-11.md` (what was cut from the pitch and why).
Every line below is labelled **LIVE**, **BUILT, NOT WIRED**, or **PLANNED** so nothing is oversold.

## The principle

SKR is Solana Mobile's token, not ours. The game never mints, holds, custodies, lends or pays out
SKR. Everything we do with it is either a **payment rail** the player chooses, or a **read-only
verification** of a staking position that already exists on the Seeker. No vault, no yield, no
withdrawable in-game SKR.

## What is live today (shipped in the dApp Store build)

- **Pay with SKR** — Night Market packs carry an SKR price next to SOL. Real purchases, settled
  by the wallet, same rail as SOL. (`CurrencyKind.Skr`, `PackPricing.skr`)
- **Native staking verification** — the backend reads the connected wallet's native SKR staking
  position (active stake, cooldown, freshness) and derives a server-side "Heartbound" snapshot.
  Client claims never authorise a benefit. Minimum eligibility is authored at 100 SKR; tiers are a
  resonance score of stake and tenure, so a smaller long-term staker still climbs.
- **First connected benefit** — verified stakers get extra jewel-polishing attempts (an extra
  weekly reroll at Tier I, a higher per-stone cap at Tier V). Attempts, not better odds.
- **SKR is untouchable in raids** — siege stakes can only touch in-game resources; SKR and
  purchased goods are excluded by rule.

## Built but not wired (do not present as finished)

- Heartbound economic benefits beyond polishing (authored ladder, no gameplay consumers yet).
- Heart Pulse / Echo events driven by staking tenure (server handlers exist; no client caller).
- Arena SKR wagers are a **simulation** in the current build, not real tokens.

## Planned, in order

1. Wire the authored Heartbound benefits to real consumers (scout access, temporary modifiers),
   each proven by a device log line before it is described as live.
2. Deliver Heart Pulse events from a real staking pulse to the player, with the same proof.
3. Keep every SKR benefit **additive and cosmetic or convenience** (attempts, access, prestige).
   No benefit that buys army strength or bypasses the command layer the game is built on.
4. Ranked or wagered play, if it comes, uses a frozen defense snapshot and equal command pools;
   any real-token stake would be a separate, later decision with its own review.

## What we will not do

- No game-owned staking vault, no custody, no withdrawable SKR rewards, no "SKR = premium
  currency" economy (an older draft said that; it was never ratified and is retired).
- No confident zero balance in the UI: a zero is ambiguous (none / unprovisioned / RPC failed).
