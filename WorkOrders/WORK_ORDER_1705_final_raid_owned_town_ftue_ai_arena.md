# WO-1705 - final raid victory to owned town, building FTUE and AI arena

**Status:** IN PROGRESS - core contracts and independent integration silos
**Minted:** 2026-09-10, owner requested overnight implementation while existing READY fixes continue.

## Owner-established outcome

Chapter One saves the castle. Raids follow. Winning the final raid tier unlocks
a personal town inherited as it stands, damaged and repairable. The player learns
to repair and design it through playable FTUE, then battles AI. AI opponents use
the same versioned JSON build model intended for future player opponents.

The final catalog tier is iron_bastion. **Owner ruling 2026-09-11:** no win-count
lock was ever ruled (not 20, not 10, none). Every flagship raid is unlocked at
`unlockVictories` 0. Capture is a **full 3-star clear of the highest raid**.
A hero-down settle still caps at 2 stars (WO-1526), so it cannot capture.
Earlier raids must not grant the new personal town. Existing story layouts
and existing saves remain intact. No retroactive grant from legacy claim flags.

## Implementation scope

- Verify final-tier scene spawn, route, objectives, victory and return before enabling it in builds.
- Add separate durable ownership, stable structure identities, inherited condition,
  layout revision, capture receipt and resumable onboarding milestones.
- Offer a one-time essential repair supply envelope based on the captured template
  and actual repair costs; retries and replay cannot pay twice. Preserve earned upgrades.
- Reconstruct and edit the owned town using existing placement/repair systems,
  without substituting it into the Chapter One BaseLayout.
- Extend the existing tutorial interpreter: reveal ownership, perform an essential
  repair, make a design improvement, save/reenter, then complete an AI practice battle.
  Service success advances steps; dismissal or button clicks alone do not.
- Use one validated build snapshot for AI opponents and the future player source.
  Initial practice has no wager, ranked payout or claim of live player matchmaking.

Recommendation adopted for implementation: ordinary AI practice becomes available
after the onboarding actions and a completed practice outcome; a win is not required.
Loss gives an improvement and retry path. Future player matching remains a later phase.

## Parallel lanes and integration

Core ownership/save/contracts land first. Raid capture and owned-layout adapters,
FTUE/arena adapters, and bounded persistence endpoints can then share that contract.
Root owns file fences, root comparison, Unity, scene generation, registration and
commits. Current raid floor/wall and storefront fixes complete before overlapping edits.
Architecture source: docs/RAIDS_TO_PVP_ARCHITECTURE_2026-09-10.md; its earlier
one-raid demonstration wording is superseded by the owner's final-tier-only ruling.

## Acceptance and limits

Automated gates cover older saves, ownership round-trip, duplicate capture/payment,
layout isolation, malformed snapshots, revisions, real tutorial signals, and practice
without wager/reward mutation. Saved navigation and rendered UI must be inspected.
Android acceptance follows the owner's preferred phone-first testing: win the final
tier, claim once, restart during FTUE, repair/rearrange, enter AI using the saved build.

Initial PvE completion is client-reported; authentication/idempotency do not make it
server-verified combat. Player certification, authoritative competition, deployment,
and production schema migration are separate gates. No deployment is authorized by
this implementation request. Implemented work awaiting owner test-build proof is
Fixed; owner testing alone closes or reopens it.

## Live device finding, 2026-09-14 (F8 seq 5091, not yet actioned)

`[Flow:Manage] queue row catalog MISS: neither BuildingTierCatalog ('owned-town') nor CatalogRegistry
('owned-town') has a display name for job 'owned-town@personal-iron-bastion|4ec86de0f1e248dbbf93cbaafae45196'
(channel Builder). The player would otherwise read the raw id as a structure name` - caught by
`Guard.Try` in `ManageScreenVM.MakeJobRow` (non-fatal), captured via `ManageScreenPanel.Open`. Owner
screenshot shows the Manage screen with 1 queued item at the moment this fired. Needs a display-name
entry for owned-town structure ids in whichever catalog the Builder-channel queue reads, or the owned-
town job naming needs to fall back to something other than the raw persisted id.
