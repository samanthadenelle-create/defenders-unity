# WORK ORDER 1851 — Clan system, step 8: open `ClanFeatureGate.PlayerFacingEnabled` + fix the join-policy vocabulary

**Status:** READY TO IMPLEMENT

## Context — clan WO-8 in the chain, depends on WO-1848 (landed, committed 43fce9820)

Source: `docs/SKR Integtration.md` ("WO-8"), `CLI_LANES_WO_NUMBERS.md` banner (1851 row). WO-1848's
two-wallet acceptance gate is green — the chain is proven end to end. This ticket flips the public
gate and closes WO-1845's FLAG 1 (the join-policy vocabulary mismatch), which was left open pending
an owner ruling.

## Owner ruling (2026-09-17, resolves WO-1845 FLAG 1)

Two values only: **`invite`** (join requires the clan's code — the current server default, unchanged)
and **`open`** (anyone may join without a code — not built yet on the server side). Default stays
`invite`. The client's existing `'open'`/`'closed'` vocabulary in `ClanService.cs` is WRONG and must
be corrected to match, not the other way around — the server's `invite`/`open` model is now canon.

## Scope

### Part A — Fix the vocabulary, add the constraint

1. New migration: `ALTER TABLE clans ADD CONSTRAINT clans_join_policy_valid CHECK (join_policy IN
   ('invite', 'open'))`. Read `api/migrations/20260917_0030_clan_tables.sql` first — the column
   already exists with no CHECK; this closes that gap. Follow this repo's existing idempotent-DDL
   conventions for adding a constraint to a live table (grep prior migrations, e.g. WO-1108's pattern,
   for the guard idiom already used elsewhere in this file set).
2. `POST /api/clan/create` gains an optional `joinPolicy` field (`'invite'` default, `'open'`
   accepted and stored — even though `open`'s actual join-without-code SERVER BEHAVIOR is not yet
   built; storing it correctly now avoids a second migration later). If `'open'` is requested but
   the join endpoint doesn't yet honor it differently from `invite`, say so explicitly in the
   hand-back as a known, deliberate gap — do not silently half-build open-join behavior under this
   ticket's scope creep.
3. Fix `Assets/_Modules/Village/Talents/ClanService.cs` (or wherever it actually lives — re-verify
   at source, the WO-1845 record cited `ClanService.cs:85,172`) to use `'invite'`/`'open'` instead of
   `'open'`/`'closed'`.

### Part B — Open the gate

`ClanFeatureGate.PlayerFacingEnabled` flips from `false` to `true`. Read
`Assets/Editor/Regression/BattleQuiescenceRegression.cs`... no — read whatever file actually declares
this gate (grep for `ClanFeatureGate` — WO-1847's own test file, `test/clan-chat-embed.test.js`,
already asserts the CURRENT `false` state and its exact bootstrap ordering; that test needs updating
to assert `true` plus re-proving the SAME ordering invariant, not a weakened one).

## Non-scope

- Do NOT build actual open-join (join without a code) server behavior — that's a future ticket if
  ever wanted. This ticket only fixes the vocabulary and adds the constraint.
- Do NOT touch WO-1850 (leaderboard) — file-disjoint, running in parallel.
- Do NOT touch WO-1858 (WebView/chat wiring) — file-disjoint, running in parallel.

## Acceptance criteria

- [ ] `clans_join_policy_valid` CHECK constraint exists; a bad value is rejected at the DB level.
- [ ] `ClanService.cs` uses `invite`/`open`, matching the server, in every place it references a
      join-policy value (grep for the old `open`/`closed` strings and confirm zero remain, or that
      any remaining `closed` reference is deliberately something else entirely — verify, don't assume).
- [ ] `ClanFeatureGate.PlayerFacingEnabled` is `true`.
- [ ] The regression/test that previously pinned the gate as `false` (WO-1847's
      `test/clan-chat-embed.test.js`, case 5, "the release gate and its ordering are exactly as
      WO-1265 left them") is updated to assert `true` while still proving the bootstrap ordering
      invariant (the gate check must still be the LITERAL first line of `SpawnInScene`).
- [ ] `node --test test/*.test.js` before/after counts. `python tools/gate_brace.py` + NUL scan on
      every `.cs` touched. Unity `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>`.

## Test plan

1. Attempt `clans.join_policy = 'closed'` via direct insert; confirm the CHECK constraint refuses it.
2. Create a clan via the client path; confirm it round-trips as `'invite'` and the client no longer
   ever sends/expects `'open'`/`'closed'` as its OWN two-value model.
3. Boot the game; confirm the clan UI is now reachable (whatever gate/menu entry
   `ClanFeatureGate.PlayerFacingEnabled` actually controls — verify at source what that is before
   claiming it's reachable).
4. Full regression suite green.

## Rollback

Flip the gate back to `false`; drop the CHECK constraint; revert `ClanService.cs`. No data loss (no
existing rows can hold an invalid value once the constraint exists, so rollback of the constraint
itself is safe).

## Copy rules

No investment/jargon language. Join policy is a game setting, not a governance mechanism.
