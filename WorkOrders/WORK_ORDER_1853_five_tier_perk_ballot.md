# WORK ORDER 1853 — Clan system, step 10: five-tier perk ladder + ballot

**Status:** READY TO IMPLEMENT

## Context — clan WO-10 in the chain, depends on WO-1852

Source: `docs/SKR Integtration.md` ("WO-10"). Adds `clan_ballots`, `clan_ballot_votes`, `clan_perks`
tables and the propose/vote/read endpoints, gated by `vigil_weight` from WO-1852.

## Owner ruling on the vote mechanic (2026-09-17, resolves this ticket's vote-weight AND pass-threshold questions)

Two separate things, both real:
1. **Vote weight is tenure-weighted** — the source spec already has this right: each voter's weight
   is their `vigil_contribution` (percent_staked × tenure_seconds) from WO-1852, not one-wallet-one-
   vote. The winning option is whichever gets the plurality of total weighted votes cast. Keep this
   exactly as DeepSeek's WO-10 spec already has it.
2. **NEW: the ballot must also clear a participation/passing threshold that SCALES WITH CLAN SIZE**,
   not a single fixed ratio for every clan (owner, verbatim: *"the amount that they need is based on
   how many they have in their group weighted by duration or length"*). This is layered ON TOP of
   plurality-wins — a ballot can have a clear plurality winner by weight and STILL fail to pass if
   turnout doesn't clear the size-scaled bar. Small clans need a smaller ratio of members voting; larger
   clans need a bigger ratio (illustrative examples the owner gave in conversation: roughly 2/3 for a
   small group, 2/4, up to 5/6 for a larger one — these are ILLUSTRATIVE, not a final table).
   **This is a first-pass engineering default, not a locked owner ruling on the exact numbers** — ship
   a simple, clearly-labeled formula (e.g. a participation-ratio curve or a small lookup table keyed by
   member count) and flag it explicitly in the hand-back for the owner to review/adjust once she's
   awake. Do not present the shipped numbers as final.

## Schema (new migration file, exact shape from the source spec)

```sql
CREATE TABLE IF NOT EXISTS clan_ballots (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  proposed_by_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  tier INTEGER NOT NULL,
  options JSONB NOT NULL,
  opened_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  closes_at TIMESTAMPTZ NOT NULL,
  closed_at TIMESTAMPTZ NULL,
  winning_option TEXT NULL,
  CONSTRAINT clan_ballots_tier_range CHECK (tier BETWEEN 1 AND 5)
);

CREATE TABLE IF NOT EXISTS clan_ballot_votes (
  ballot_id UUID NOT NULL REFERENCES clan_ballots(id) ON DELETE CASCADE,
  wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  option_id TEXT NOT NULL,
  voted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  weight NUMERIC NOT NULL,
  PRIMARY KEY (ballot_id, wallet)
);

CREATE TABLE IF NOT EXISTS clan_perks (
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  tier INTEGER NOT NULL,
  perk_id TEXT NOT NULL,
  activated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  expires_at TIMESTAMPTZ NULL,
  PRIMARY KEY (clan_id, tier)
);
```

## Tier gating (first pass, tunable — ship placeholders, retune after real vigil_weight data exists)

| Tier | Vigil weight threshold | Example perk options |
|---|---|---|
| 1 | > 0 | +5% build speed, +3% harvest yield |
| 2 | ≥ T2 | +10% build speed, +6% harvest yield, +5% wall HP |
| 3 | ≥ T3 | +15% build speed, +10% harvest yield, +10% wall HP |
| 4 | ≥ T4 | A new building type, a defensive turret variant |
| 5 | ≥ T5 | A special troop type (ancestor-summoned) |

T2–T5 numeric values are VERIFY-BEFORE-BUILD placeholders — they depend on real `vigil_weight`
magnitudes this repo does not have yet. Ship placeholders, say so plainly.

## Cadence (owner-ruled: fixed anchor, not chain-read)

Epoch-driven, 48-hour cadence, anchored to a **fixed timestamp** (e.g. `2026-01-01T00:00:00Z`), not
read from a live chain source — this was already ruled by the owner per this repo's WO-numbering
banner note for this ticket. A ballot opens on the first observation of a new epoch boundary (computed
server-side from the fixed anchor + 48h intervals) and closes 48 hours later.

## Ballot lifecycle

1. Any clan member: `POST /api/clan/ballot/propose { tier, optionIds }`. Server validates the clan's
   current `vigil_weight` meets the tier threshold. Only one open ballot per clan at a time (409 on a
   second propose).
2. Members: `POST /api/clan/ballot/vote { ballotId, optionId }`. Weight = voter's `vigil_contribution`
   snapshotted at vote time. One vote per wallet per ballot; re-voting updates the option, never the
   weight snapshot.
3. `GET /api/clan/ballot/current` returns the open ballot + votes + caller's own vote.
4. When `closes_at` passes, the next request to any ballot endpoint closes it: determines the
   plurality-by-weight winner, checks the size-scaled participation threshold from this ticket's owner
   ruling above — if turnout clears the bar, sets `winning_option` and inserts a `clan_perks` row; if
   it does NOT clear the bar, the ballot still closes but writes no perk (flag this outcome clearly in
   the response, e.g. `{ closed: true, passed: false, reason: 'insufficient_turnout' }`).

## Non-scope

- No Squads multisig integration — that's WO-1854.
- No Genesis Token gating — that's WO-1854.
- No perk effects in the game client beyond a flag the client can read — the actual gameplay effect of
  each perk is a separate future client ticket.
- No ballot history beyond the current/most-recently-closed ballot.

## Acceptance criteria

- [ ] A clan with `vigil_weight = 0` cannot open a Tier 1 ballot.
- [ ] A clan with `vigil_weight ≥ T2` can open a Tier 2 ballot.
- [ ] Two members with different `vigil_contribution` produce different vote weights.
- [ ] Re-voting updates the option, not the weight.
- [ ] A closed ballot that clears the participation threshold writes a `clan_perks` row.
- [ ] A closed ballot that does NOT clear the participation threshold closes without writing a perk,
      and says so plainly in its response.
- [ ] Only one open ballot per clan at a time; a second propose returns 409.
- [ ] An expired ballot closes on next read and returns the result (pass or fail).

## Test plan

1. Two-member clan, weights 0.5 and 1.0. Open Tier 1 ballot. Vote differently. Verify winning option
   by weight AND verify the participation-threshold check against this two-member clan's size-scaled
   bar (both members voting should clear almost any reasonable formula — use this to prove the happy
   path).
2. Same setup, but only one of two members votes. Verify the ballot correctly reports whether that
   turnout clears or fails this clan size's threshold, per whichever formula this lane ships.
3. Open Tier 3 ballot with weight 0.5 (below T3). Verify 403.
4. Advance time past `closes_at`. Call `GET /api/clan/ballot/current`. Verify closed + correct
   pass/fail outcome.

## Rollback

Drop the three tables. Remove the endpoints. The client reads no perks, so no client state is lost.

## VERIFY BEFORE BUILD

- Confirm the fixed epoch anchor timestamp — if genuinely undecided, pick a clearly-labeled default
  (e.g. today's date at 00:00 UTC) and flag it as changeable, rather than blocking on it.
- The size-scaled participation threshold formula is this ticket's own first-pass default — ship it,
  flag it, do not present it as final.
- Whether perks should expire — the schema allows `expires_at`; if no ruling exists, ship it always
  NULL and say so.

## Copy rules

Ballot copy: "The ancestors listen to those who gather. Stake together, and they will consider your
requests." Perk descriptions must never use investment language. Special-troop-type copy: "The
ancestors stir. A new shape is possible." Never state duration in hours/days — use "epoch" or "vigil."
