# WORK ORDER 1853 — Clan system, step 10: five-tier perk ladder + ballot

**Status:** READY FOR LEAD REVIEW - implemented 2026-09-17/18 (this lane). 3 endpoints + 1 library + 1 migration + 76 new node:test cases, all green; `node --check` clean on all 5 JS files; suite 1098/1095 -> 1174/1171 with the SAME two pre-existing reds. NOT committed, NOT deployed, NO DDL run. **The participation-threshold numbers and the epoch anchor are FIRST-PASS ENGINEERING DEFAULTS awaiting the owner's ruling** - see FLAG 1, FLAG 2 and FLAG 4 in the implementation record. PRIOR STATUS: READY TO IMPLEMENT

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

---

## IMPLEMENTATION RECORD (2026-09-17/18) — files, proof, and the flags the owner must rule on

**Files written (all NEW; nothing existing was edited except one additive block in `api/schema.sql`)**

| File | What it is |
|---|---|
| `api/migrations/20260917_0035_clan_ballots.sql` | NEW. The three tables, character-for-character from the source spec, plus ONE partial unique index and one read index. |
| `api/schema.sql` | +61 lines, APPENDED ONLY: the descriptive copy, in the `-- ⛔ THE APPLYABLE COPY IS api/migrations/…` shape `schema.sql:2325-2327` established for `clan_rate_limit`. **CRLF preserved byte-exactly** — 2355 → 2416 CRLF, 2416 total LF, **0 bare LF before and 0 after** (measured, not assumed; the file is CRLF and `test/migrations.runner.test.js:486-497` records what a bare LF costs there). |
| `api/_lib/clan-ballot.js` | NEW, the whole rule: epoch math, tier gate, perk catalogue, participation threshold, tally, settle/close, the wire shape. |
| `api/clan/ballot/propose.js` | NEW. `POST /api/clan/ballot/propose`. |
| `api/clan/ballot/vote.js` | NEW. `POST /api/clan/ballot/vote`. |
| `api/clan/ballot/current.js` | NEW. `GET /api/clan/ballot/current`. |
| `test/clan-ballot.test.js` | NEW, **76 cases**. |

**NOT touched:** `api/_lib/clan.js` (imported for `uniqueViolation` + `UUID_RE`, edited not at all),
`api/_lib/clan-vigil.js` (imported for `readClanVigil`, edited not at all), `api/_lib/wallet-auth.js`
(see FLAG 6), anything under `Assets/`, `vercel.json`, any other migration. **No DDL was run, nothing
was committed, nothing was deployed.**

**Test run — before/after, measured twice:**

| | tests | pass | fail | todo |
|---|---|---|---|---|
| before (`node --test` over the 70 other suites) | 1098 | 1095 | 2 | 1 |
| after (`node --test test/*.test.js`, 71 suites) | **1174** | **1171** | **2** | 1 |

+76 tests, +76 passes, **no new failures**. The two reds are PRE-EXISTING and neither file is touched
by this lane; both were present in the before-run:
- `test/heartbound-suite.test.js` — *"⛔ no client-readable file under Assets/ carries a Heartbound tier
  NAME"* (a Unity asset-tree scan).
- `test/wallet-identity.test.js` — *"⛔ no logic anywhere in wallet-auth.js touches the reserved Genesis
  Token columns"*. **This one is WO-1854's in-flight work, not a mystery:** `verifyGenesisToken` now
  exists at `api/_lib/wallet-auth.js:1003` (file mtime 22:26:36, before my baseline run), and that pin
  exists precisely to go red when those columns stop being reserved. It is the WO-1854 lane's to close.
- The counted `todo` is `heartbound-contract.test.js:272` (`WO-1693 finding 2`), as WO-1852's record
  already named.

`node --check` passes on all five JS files (run individually, output captured).

**⛔ THIS LANE WAS COMMITTED MID-FLIGHT, AND THE COMMIT IS ONE FILE SHORT — `api/schema.sql`.**
Commit `1806d3ce4` *"feat(clan): WO-1853 (clan WO-10) - five-tier perk ballot"* landed at **22:40:36**,
while this record was still being written. It carries seven paths: the library, the three routes, the
migration, the suite and this markdown — **but NOT the `api/schema.sql` descriptive block**, which is
still an unstaged working-tree change. That is not cosmetic: **at `1806d3ce4` alone the new suite is
75/76**, because the case *"api/schema.sql carries the DESCRIPTIVE copy…"* goes red. Proven by checking
the file back out, re-running (**75 pass / 1 fail**, that one case), and restoring it byte-exactly
(CRLF 2416 / LF 2416 / **0 bare LF**, unchanged). **Still to stage: `api/schema.sql`, plus this
markdown's post-22:40 edits (FLAG 10, the generated FLAG 1 sequence, the 42883 correction, this
paragraph) and `BOARD.html`.**

**⚠ THE BASELINE MOVED WHILE I MEASURED, SO HERE IS WHEN.** The before/after pair above was taken at
**22:33 and 22:36** and is the honest comparison for this lane: identical two reds either side. A later
full-suite run at **22:42** read **1223 / 1212 / 10 fail** — +49 tests and +8 reds that arrived with the
WO-1854 lane's own new suite (`test/clan-vault-genesis.test.js`, 6 reds) plus
`test/clan-chat-release-gate.test.js` (1 red), none of them in a file this lane wrote and none of them in
`test/clan-ballot.test.js`, which was **76/76 green on every run including that one**. Read the suite total
as a moving number tonight, and judge this lane by its own suite plus the unchanged two.

**⚠ A CONCURRENT LANE IS IN THIS TREE AND I SAW IT MID-WRITE.** A first full-suite run at 22:31 showed a
THIRD failure — `ReferenceError: vaultToWire is not defined at api/_lib/clan-vigil.js:350` breaking
`test/clan-vigil.test.js`. That was WO-1854 writing `clan-vigil.js` between my two runs (`vaultToWire`
is now defined at `:376`; the file grew 279 → 404 lines, mtime 22:31:47). It re-ran green. **Recording it
because a lead who runs the suite during that lane's next write will see the same transient red and
should not attribute it here** — and because it is direct evidence that `clan-vigil.js` and
`wallet-auth.js` are NOT file-disjoint tonight, which is why nothing in this lane edits either.

---

### ⛔ RED-PROVED, NOT JUST GREEN (the rule a passing suite cannot satisfy on its own)

76/76 passed on the first run, which is not evidence of anything by itself. Both load-bearing rules were
therefore **deliberately sabotaged in `clan-ballot.js` and the suite re-run**:

1. `const passed = hasVotes && turnout.clears && …` → `const passed = hasVotes && …` (the turnout gate
   removed entirely).
2. `DO UPDATE SET option_id = EXCLUDED.option_id` → `… , weight = EXCLUDED.weight` (a re-vote moves the
   weight snapshot).

Result: **71 pass / 5 fail**, and the five reds were exactly the right ones —
*"THE LOAD-BEARING CASE: a clear weighted winner STILL FAILS on turnout"*, *"ACCEPTANCE 4: the vote
UPSERT sets option_id and NOTHING else"*, *"ACCEPTANCE 6: an expired ballot that FAILS turnout closes and
writes NO perk"*, the wire-shape case, and the endpoint-level ACCEPTANCE 6. The file was then restored
from a pre-sabotage copy and re-verified (`grep` confirms `EXCLUDED.weight` count **0** and the gate line
back at `:630`); suite back to 76/76.

### ⛔ THE ONE THING PROVEN AGAINST A REAL DATABASE — the epoch SQL

`readEpoch()` was executed against the **live PostgreSQL** (read-only `SELECT` of a constant expression;
no table read, nothing written), using the exact statement the code ships:

```
EPOCH {"epochIndex":130,"startedAt":"2026-09-18T00:00:00.000Z",
       "endsAt":"2026-09-20T00:00:00.000Z","nowAt":"2026-09-18T03:22:04.220Z"}
window seconds = 172800      now inside window = true
anchor + idx*48h == started  =>  2026-09-18T00:00:00.000Z == 2026-09-18T00:00:00.000Z
```

That proves three separate things a shape-test cannot: the statement is valid PostgreSQL, the window is
exactly 48 h, and the boundaries land on the anchor's own phase.

⚠ **AND ONE CLAIM I HAD WRITTEN WAS FALSE — it is corrected rather than quietly dropped.** Both the code
comment and the first draft of this record said the `::double precision` cast was load-bearing, because
`numeric * interval` "has no operator and is a runtime 42883". **I had not measured that.** Probed
read-only on the same server: `pg_typeof(FLOOR(EXTRACT(EPOCH …)/n))` is indeed `numeric`, server is
**PostgreSQL 17.11**, and the **un-cast form runs fine** and returns the identical boundary
(`2026-09-18T00:00:00.000Z`) — PostgreSQL resolves it through the implicit numeric→double precision cast.
The cast stays because naming a type is cheaper than relying on an implicit one, NOT because omitting it
errors, and `clan-ballot.js`'s header now says exactly that. Recorded because a plausible-sounding SQL
claim that nobody checked is precisely the class of thing CLAUDE.md §11B exists to stop.

### ⛔ WHAT IS **NOT** PROVEN, said plainly

- **The DDL has never been executed.** `20260917_0035_clan_ballots.sql` was not applied — running
  migrations is the lead's/owner's call, not a lane's. Its statements are pinned by the suite
  (`auditAdditive()` imported from the real runner returns `[]`; every `CREATE` carries `IF NOT EXISTS`;
  it seeds no rows) but **no server has parsed them.** The first apply is the first real test.
- **The other seven statements** (insert/upsert/close/perk/roster/votes/count) were proven only by their
  TEXT and their bound values through `recordingSql`, never against a server. The partial unique index's
  23505 is simulated with a hand-built error carrying `code: '23505'`.
- **No endpoint has been called over HTTP.** Every route test drives the handler in-process with a faked
  `req`/`res`, the way every other clan suite does.
- **No client work exists.** Nothing in `Assets/` reads a perk yet — explicitly the WO's non-scope.

---

## ⛔ FLAG 1 — THE PARTICIPATION THRESHOLD IS A FIRST-PASS DEFAULT. Please rule on the numbers.

Shipped as `PARTICIPATION_BANDS` (`api/_lib/clan-ballot.js`), a three-band step table keyed by member
count, with `requiredVoters = ceil(ratio × memberCount)` clamped into `[1, memberCount]`:

| Clan size | Required ratio | Your illustrative fraction | Voters needed |
|---|---|---|---|
| 1–4 members | **1/2** | "2/4" | 1, 1, 2, 2 |
| 5–12 members | **2/3** | "2/3" | 4, 4, 5, 6, 6, 7, 8, 8 |
| 13+ members | **5/6** | "5/6 for a larger one" | 11, 12, 13, 14, 15, 15, 16, 17 (n=13…20); 84 at n=100 |

⚠ **Those three runs were GENERATED by running `requiredVoters(1..20)`, not typed.** The first draft of
this table was hand-copied and was wrong at n=8 and n=11 (it read 5 and 7; the answers are 6 and 8) —
caught in review, and the cure was to make the test pin the entire 1..20 run so there is exactly one
source for the sequence the ruling is made on. Raw output, for the record:
`1:1 2:1 3:2 4:2 5:4 6:4 7:5 8:6 9:6 10:7 11:8 12:8 13:11 14:12 15:13 16:14 17:15 18:15 19:16 20:17`.

Reasoning, so the table can be argued with rather than just replaced: your three fractions were read in
the monotone order this ticket's own gloss states (small clans a smaller ratio, larger clans a bigger
one), which is why "2/4" anchors the bottom band and "2/3" the middle — and at 3 members the bottom band
already produces `ceil(1.5) = 2`, i.e. your "2/3" falls out of it anyway. **CEILING, not rounding**:
rounding down lets fewer members clear the bar than the fraction names, which is the one direction a
quorum must never err in.

**⚠ THE CONSEQUENCE YOU SHOULD SEE BEFORE RULING: in a TWO-member clan, ONE member can pass a ballot**
(`requiredVoters(2) = 1`). The alternative — 2 of 2 — means one absent member blocks that clan forever.
In a four-member clan (your own "2/4") one voter does **not** clear it. Both are pinned as tests, one of
them titled `⚠ FLAGGED:` so it is findable.

**Not presented as final. A retune is one edit to that one table** — the bands are the only place the
numbers exist, and the suite asserts the SHAPE (monotone, ceiling, never zero, never above the clan size)
rather than hardcoding your fractions as law.

## ⛔ FLAG 2 — TURNOUT IS COUNTED IN HEADS; the weighted reading is REPORTED, not enforced

Your words were *"the amount that they need is based on how many they have in their group weighted by
duration or length"*, which admits two readings: the BAR scales with size (implemented), or the TURNOUT
itself is measured in Vigil weight rather than heads (not implemented as the gate).

The gate is head count, for one decisive reason: **a weighted turnout ratio is undefined when a clan's
total Vigil is zero** — every member unstaked, or every chain read degraded — and that is the ordinary
state of a new clan, so the gate would divide by zero or pass vacuously exactly when it matters least. A
head count is always defined.

The weighted reading is still computed and shipped on the response as
`ballot.turnout.weighted_turnout` (voted weight ÷ the clan's current Vigil, or `null` when there is no
weight to take a fraction of — **null rather than 0, because a 0 would read as "nobody with weight
voted"**). So you can watch both numbers on real ballots and switch the gate with one line if the
weighted reading is what you meant. One pinned consequence of the current choice:
**a whale who is the clan's entire Vigil still cannot pass a ballot alone** — ⚠ true of an ordinary
member, and NOT true of a whale who is also the Leader, because the Leader can kick the members who make
up the bar. See FLAG 10.

## ⛔ FLAG 3 — T2–T5 ARE PLACEHOLDERS, and the unit is the useful part

`TIER_THRESHOLDS = { 1: 0 (strictly >0), 2: 86400, 3: 604800, 4: 2592000, 5: 7776000 }`.

The magnitudes are guesses — this repo has never measured a real `vigil_weight`, exactly as the ticket
says. What was chosen deliberately is the **UNIT: fully-staked member-seconds.** Since
`vigil_contribution = percent_staked (0..1) × tenure_seconds`, one member with their whole SKR position
staked accrues 1.0/second, so the ladder reads as a duration: T2 = one member-day, T3 = one member-week,
T4 = 30 member-days, T5 = 90 member-days. That makes a retune a judgement about time ("a clan should need
about a month of collective vigil for Tier 4") instead of a judgement about an opaque float.

Tier 1's bar is `> 0`, not `>= 0` — the ticket's own acceptance criterion 1 — and that strictness lives in
`meetsTier()` alone, with the stored bar at 0.

## ⛔ FLAG 4 — THE EPOCH ANCHOR: `2026-01-01T00:00:00Z`, and where I looked for a ruling

The ticket says the fixed anchor "was already ruled by the owner per this repo's WO-numbering banner note
for this ticket." **I opened that note.** `CLI_LANES_WO_NUMBERS.md:266` reads, in full:
*"**1853** = clan WO-10, five-tier perk ballot, fixed-anchor 48h cadence per owner ruling (needs 1852)"*.
So the SHAPE (fixed anchor, 48 h, not chain-read) is ruled and **the VALUE is stated nowhere.** Rather
than invent a third candidate, this takes the ticket's own example, which its VERIFY-BEFORE-BUILD
explicitly sanctions. Changing it re-phases every future ballot and nothing else — no stored row
references the anchor, because `closes_at` is a materialised timestamp.

**⚠ AN EDGE THIS LEAVES OPEN, DELIBERATELY UNPATCHED.** `closes_at` is the NEXT epoch boundary, so a
ballot proposed five minutes before a boundary runs for five minutes and will almost certainly fail on
turnout. I considered a minimum-open-window rule (e.g. roll to the following boundary if less than a
quarter-epoch remains) and **did not ship it**, because it is a mechanic you did not ask for and an
invented rule is harder to unwind than an absent one. If you want ballots to always get a full epoch, say
so and it is a three-line change in `insertBallot`'s caller.

## FLAG 5 — THE BALLOT IS SECRET; THE TALLIES ARE PUBLIC

The ticket says `GET /current` returns "the open ballot + votes + caller's own vote". Shipped as
**per-option tallies (weight + voter count) plus the caller's own vote, and NO wallet-to-option map** —
the response renders no wallet address at any depth, and a test asserts that by string search. Reasons:
the standing convention in this API is that a wallet is never rendered where a tally or a count would do
(`clan.js getLeaderboard`'s header states it), and a secret ballot with public totals is the shape every
real vote takes. **If you want a clan to see who voted for what, that is a one-field addition** — say the
word. Raised rather than picked silently.

## FLAG 6 — THE RATE BUDGET runs on the UNDECLARED-ACTION FALLBACK (a 2-line follow-up for the lead)

`propose` and `vote` call the shared preamble with the actions `'ballot_propose'` / `'ballot_vote'`, and
neither is a key of `CLAN_RATE_LIMITS` (`api/_lib/wallet-auth.js:749`). `touchClanRate` therefore applies
the **strictest declared budget (3/hour)** and logs `'[wallet-auth] clan rate: undeclared action …'` —
which is precisely the mechanism that function's author built for this case (`:757-761`), not an accident.

Passing NO action was the alternative and was rejected: the same comment says an unlimited default is
"the one outcome a rate limiter must never have". **The proper fix is two lines in `CLAN_RATE_LIMITS`**
(e.g. `ballot_propose: 5, ballot_vote: 20`) and it is left to the lead because `wallet-auth.js` is a 74 KB
file another lane edited at 22:26 tonight — CLAUDE.md §9's same-file hazard, and the very hazard the
transient `vaultToWire` red (recorded above, under the test run) demonstrates. 3/hour is genuinely fine for propose (one open ballot per clan
anyway); it is tight for a member who re-votes a lot. The action strings are pinned by a test, so the
follow-up cannot land on a typo.

## FLAG 7 — THE ACCEPTED BODY IS `{ playerId, tier, optionIds }`, not the ticket's `{ tier, optionIds }`

The shared preamble resolves the caller's identity from `body.playerId` first
(`api/_lib/clan-http.js:111-115`) and cannot be called without an identity at all, so `playerId` is
required in the POST body (`wallet` also works, third in that chain; `optionIds` accepts `options` as an
alias, and `ballotId`/`optionId` accept the snake_case spellings). **This is a note for the client lane**,
recorded the way `clanTargetWallet`'s header recorded the identical class of gap for `/kick`.

## FLAG 8 — THREE SCHEMA READINGS THAT ARE ENGINEERING CALLS, NOT RULINGS

1. **One perk per tier, and a later winning ballot REPLACES it.** `clan_perks`' primary key is
   `(clan_id, tier)` — the ticket fixed that — so the write is `ON CONFLICT (clan_id, tier) DO UPDATE`.
   The alternatives (refuse the write, or grow a history table) both contradict the schema as written.
2. **`expires_at` is always written NULL** (VERIFY-BEFORE-BUILD item 3). No expiry ruling exists — I
   searched and the WO-1853 banner note carries only the cadence. The column is declared so a future
   ruling needs no second migration.
3. **A ONE-OPTION ballot is legal.** Your tier table names exactly one Tier 5 option, and inventing a
   second would be writing game content this lane was not asked to write. So Tier 5 ships a
   confirmation-style ballot with a single option — and it still has to clear the participation threshold
   to pass. If Tier 5 should offer a real choice, it needs one more option from you.

## FLAG 9 — THE ONE ADDITION TO THE FIXED SCHEMA: a partial unique index

The three table bodies are the spec's, unchanged. I added
`CREATE UNIQUE INDEX IF NOT EXISTS clan_ballots_one_open_per_clan ON clan_ballots (clan_id) WHERE
closed_at IS NULL` (plus a `(clan_id, opened_at DESC)` read index).

Why it is not optional: acceptance criterion 7 ("only one open ballot per clan; a second propose returns
409") **cannot be held by a pre-SELECT in the route** — two simultaneous proposes both read "none open",
both insert, and the clan then has two ballots and no way to say which is current. The index makes that
race one row and one 23505, which the route answers as the same 409. It is the same shape, for the same
reason, as `clan_members_one_clan_per_wallet` in migration 0030. Both paths are tested.

## ⛔ FLAG 10 — THE TURNOUT DENOMINATOR IS THE CLAN'S **CURRENT** SIZE, and that is exploitable

`required_voters` is derived from `SELECT COUNT(*) FROM clan_members` **at settle time**, and `voters` is
simply the number of rows on the ballot. Two consequences I did not design for and am not going to pick
between on your behalf:

1. **A Leader can shrink the bar by kicking non-voters just before the ballot closes.** Kick two silent
   members of a five-member clan and `required_voters` falls from 4 to 2. This also **falsifies a property
   I asserted under FLAG 2**: "a whale who is the clan's entire Vigil cannot pass a ballot alone" is true
   of an ordinary member and NOT true of a whale who is also the Leader, because the Leader owns `/kick`.
2. **A member who left or was kicked mid-ballot still counts as a voter**, since `readVotes` does not join
   `clan_members`. Their weight stays in the tally.

The obvious alternatives each break something else, which is why this is a ruling and not a fix:
snapshotting `member_count` into `clan_ballots` when the ballot OPENS makes joiners produce a turnout
ratio above 1 and lets a mid-ballot recruiting push pass anything; filtering votes to current members
silently discards a departed member's cast ballot. **I recommend nothing here** — it is your mechanic. If
you want the exploit closed cheaply, the smallest honest option is to snapshot the member count at open
AND cap `voters` at it, and I can do that in one migration column plus four lines.

---

## Smaller decisions, on the record

- **Closing an expired ballot spends NO chain read, and that is why the weight is a snapshot.** Every
  vote's weight is stored in `clan_ballot_votes` when cast, so the verdict is pure SQL and an SKR RPC
  outage can never leave a clan's ballot stuck open. A test asserts the settle path issues no roster
  query at all.
- **Closing is a conditional `UPDATE … WHERE id = $1 AND closed_at IS NULL RETURNING`.** Whoever moves
  the row owns the outcome; the loser re-reads and reports the winner's result instead of writing a second
  perk. No transaction is used — `sql.transaction([…])` exists in the neon driver and this repo uses it
  once (`api/purchases/quote.js:470`), but it takes a pre-built array of statements and cannot span a
  decision made between two reads, which is exactly what closing is.
- **A perk write that fails SELF-HEALS.** If the close committed but the perk INSERT threw, the next read
  notices `winning_option` set with no matching `clan_perks` row and writes it then. Reporting
  `passed: true, perk_written: false` forever was the honest-but-useless alternative: the player would see
  a won ballot with no perk and nothing would ever fix it. Both the throw path and the heal path are
  tested.
- **`winning_option IS NULL` on a closed ballot IS the "did not pass" encoding** (the ticket's own
  design), so the `reason` is re-derived at read time from the vote count: `no_votes` when nobody voted,
  `insufficient_turnout` otherwise. Nothing new is stored to carry it.
- **Degraded reads, both directions.** On propose: a weight that CLEARS the bar proceeds (the true weight
  is at least what was read, since a degraded member contributes 0 — `clan-vigil.js:127-132`), while a
  weight BELOW the bar with a degraded read is a **503**, not a 403, because "your clan has not earned
  this tier" would be stating something that was not measured. On vote: if the CALLER'S OWN read degraded
  it is a 503, never a zero-weight vote — the snapshot rule means a 0 written now could never be
  corrected. A member who has genuinely staked nothing is **not** degraded and votes at weight 0, which
  counts toward head-count turnout and adds nothing to the plurality.
- **A zero-weight electorate still produces a winner** (tie-break: weight DESC, then voter count DESC,
  then option id ASC — fully deterministic, the "never an unordered pick" rule). "No winner" would make
  the turnout gate unreachable for exactly the clans most likely to clear it.
- **`clan_ballots.options` stores option IDs only**; every player-visible string is resolved from the
  code catalogue at render time, so the copy has one home. An id retired later renders as its bare id
  rather than throwing (tested).
- **The Vigil is read on all three endpoints** (tier gate / weight snapshot / `tiers[].unlocked`), via
  `readClanVigil` rather than a second tenure query — bounded at 5 concurrent RPC reads and cached 60 s
  per wallet by `skr-staking`. A test asserts `clan-ballot.js` never mentions `first_seen_staked_at`, so a
  second spelling of "how long has this wallet been staked" cannot creep in.
- **`GET /current` spends no rate budget** (three-argument preamble), like `/me`, `/leaderboard` and
  `/vigil`.
- **Copy rules are tested, not trusted:** every player-facing string is machine-checked for
  hours/days/weeks/minutes and for investment vocabulary (`invest|returns|profit|apy|apr|dividend|
  interest|earnings|portfolio` — "yield" deliberately excluded: it is this game's farming noun and your
  own tier table says "+3% harvest yield"), and the headline plus the special-troop line are pinned
  verbatim.
- **Route paths are nested one level deeper than any existing route** (`api/clan/ballot/*.js`; all 71
  existing functions are `api/<dir>/<file>.js`). Vercel's file-system routing handles it and the ticket
  names those URLs explicitly — noted only because it is a precedent break someone may query.

## What the lead still has to do

1. Gate/commit by explicit path (7 paths, listed in the table above).
2. Apply the migration when the owner is ready: `node tools/run-migrations.mjs` — the new file sorts last
   and needs no `--baseline` handling of its own.
3. The two-line `CLAN_RATE_LIMITS` follow-up in `wallet-auth.js` (FLAG 6), once that file is quiet.
4. Carry **FLAGS 1, 2, 4, 5 and 10** to the owner. Nothing here should ship to players as final until she
   has seen the threshold table, the anchor, and the kick-shrinks-the-bar consequence.
5. `BOARD.html` in this tree is a **wholesale regeneration** I ran (`python tools/board_build.py` —
   `BOARD_CHECK_OK 0 unlabeled, 0 missing status lines, 0 status contradictions`), so it carries whatever
   every other lane's Status line said at 22:36 tonight. Regenerate it at commit time rather than staging
   mine.
