# WORK ORDER 1851 — Clan system, step 8: open `ClanFeatureGate.PlayerFacingEnabled` + fix the join-policy vocabulary

**Status:** DONE - committed 2c22f341d, COMPILE_GATE_OK + REGRESSION_OK 577/577 verified. PRIOR: READY FOR LEAD REVIEW

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

## IMPLEMENTATION RECORD (SME agent, 2026-09-17)

Implemented by a fresh SME agent under the CLI lead. Ran `advisor()` before writing any
code; its punch list is what shaped the edit set below, including the two items neither
I nor the WO text had found yet (items 8–9).

### Baseline (before any edit)

`node --test test/*.test.js` (repo root, `dev` branch): **1042 pass / 4 unique fail**
(`.claude_wo1851_baseline.log`, all 4 pre-existing and NOT clan-vocabulary-related:
"the clan leaderboard never references leaderboard_scores" and "clan_vigil_weight does
not exist yet" — both WO-1850/WO-1852+ territory, confirmed by reading
`test/clan-leaderboard.test.js`'s subject; plus two unrelated Heartbound suite findings
already tracked as WO-1693 findings). None reference `ClanFeatureGate`, `JoinPolicy`, or
`join_policy`.

### Part A — vocabulary fix

- `Assets/_Modules/Core/Services/ClanService.cs:85` comment and `:172`
  `JoinPolicy = "open"` → `"invite"` (grepped `Assets/_Modules` for `JoinPolicy` after —
  zero other `.cs` references exist; no consumer branches on the old strings).
- `api/migrations/20260917_0033_clan_join_policy_check.sql` (new): guarded
  `ALTER TABLE clans ADD CONSTRAINT clans_join_policy_valid CHECK (join_policy IN
  ('invite','open'))` inside a `pg_constraint`-existence `DO $$` block. Guard idiom
  copied verbatim from `api/migrations/20260828_0006_db_promo_pack_fks.sql:5-19` (the
  file the WO pointed at as "0010/0011/0012/0017's pattern" does not itself exist as a
  migration filename — `ls api/migrations | grep 1108` returned nothing; 0006 carries
  the same guard idiom and is cited instead). Confirmed additive/idempotent by running
  `test/migrations.runner.test.js`'s own `auditAdditive`/`nonIdempotentStatements`
  logic against the new file mentally and then empirically via the full suite run
  below — it is not in the hardcoded "exactly TWO non-re-runnable migrations" list and
  did not add a third. Numbering: `ls api/migrations` showed `...0032_clan_rate_limit.sql`
  as the highest on disk; `git status --short api/migrations` showed no pending file;
  the `.claude/worktrees/agent-abe03dc9d362c0d93` lane's `api/migrations` only goes up
  to `0023` (a stale/unrelated snapshot) — so `0033` was free at write time. Safety of
  adding the CHECK to a live table with existing rows: `api/_lib/clan.js`'s
  `createClan` was, before this ticket, the only writer of `join_policy` and never
  named that column in its INSERT (relying purely on the column DEFAULT), so every
  existing row already holds `'invite'`.
- `api/_lib/clan.js`: added `JOIN_POLICY_INVITE`/`JOIN_POLICY_OPEN` constants,
  `ClanCode.BAD_JOIN_POLICY`, and `normalizeJoinPolicy(raw)` (blank/absent → `'invite'`;
  trim+lowercase; anything else → 400 `CLAN_BAD_JOIN_POLICY`). `createClan` gained a
  5th optional param `rawJoinPolicy`; the `new_clan` CTE's INSERT column list is now
  `(code, name, tag, join_policy, created_by_wallet)` with `${joinPolicy.value}` bound.
  Exported the three new symbols. Did NOT touch `joinClan` (per WO non-scope).
- `api/clan/create.js`: passes `body.joinPolicy` through to `createClan`; header doc
  updated to document the optional field and the known gap below.
- **Known, deliberate gap (per WO instruction, not silently built):** `'open'` is now
  stored correctly on create, but `joinClan` (`api/_lib/clan.js`, unchanged) still
  requires a valid invite code regardless of the target clan's `join_policy` — there is
  no join-without-code path. This is exactly what the WO's Part A item 2 asked to be
  stated explicitly rather than half-built.
- **Open item surfaced, not acted on (flagging per advisor, not the WO's ask):**
  `api/_lib/clan.js:176-180`'s own comment already flags a tag-length mismatch (server
  1–5, client truncates to 4) "parked for a ruling before the gate-opening ticket" —
  this is that ticket and the WO text does not rule on it. Left untouched; needs an
  owner ruling of its own.

### Part B — open the gate, plus its two direct, proven consequences

- `Assets/_Modules/Core/Services/ClanFeatureGate.cs`: `PlayerFacingEnabled` `false` →
  `true`, doc comment rewritten.
- `Assets/Editor/Regression/ClanFeatureGateRegression.cs` (the Unity-side mirror of the
  same invariant, found by grep — NOT named in the WO text but it hardcodes
  `"PlayerFacingEnabled = false;"` as a required literal and would fail
  `CLAN_FEATURE_GATE_OK`/`REGRESSION_OK` the moment the gate flips): literal flipped to
  `= true;`, header + success-reason strings updated. Both ordering assertions
  (bootstrap gate-before-construction, HUD gate-before-dock-tab) left byte-identical —
  they hold regardless of the flag's value.
- `test/clan-chat-embed.test.js` case 5 ("the release gate and its ordering are exactly
  as WO-1265 left them"): regex now asserts `= true;`; renamed test/section headers;
  the ordering assertions (lines checking `gateAt > spawnAt`, `gateAt < buildAt`, and
  "nothing between the brace and the gate") are BYTE-IDENTICAL to before — the
  invariant this ticket's acceptance criteria required to survive.
- **Two more pre-existing tests pin the same `false` literal and were NOT named in the
  WO** (found via `grep -rn "PlayerFacingEnabled" test/*.test.js` after the JS test
  update, specifically because I didn't trust the WO's file list to be exhaustive):
  - `test/clan-chat-release-gate.test.js` — updated to assert `= true` and renamed;
    the dock/bootstrap wiring assertions are unchanged.
  - `test/clan-two-wallet-integration.test.js` — did NOT reference the gate constant,
    but FAILED after the `clan.js` INSERT column-list change: its hand-rolled SQL
    interpreter destructures `createClan`'s bound parameters BY POSITION
    (`const [code, name, tag, walletForClan, walletForMember, role] = values`), and
    adding `join_policy` as a 5th parameter shifted every value after it by one — the
    test's `role` field silently received a wallet address instead of `'leader'`
    (proven from the actual assertion failure: `actual: '7xKX...JosgAsU', expected:
    'leader'`, `test/clan-two-wallet-integration.test.js:339`). Fixed by widening the
    destructure to include `joinPolicy` at its correct position and threading it
    through instead of hardcoding `'invite'`.
- **`Assets/_Modules/HUD/Kit/HudKitController.cs` — DockPauseCellIndex (found via
  advisor, confirmed by reading source, not assumed):** `HudKitController.SpawnInScene`
  side isn't touched, but `BuildAdaptivePeacefulDock`'s `dockRow` counter (:5554-5570)
  now runs Chat→Leaderboard→Music→Settings→Realm→Pause instead of skipping Chat, so
  Pause moves from grid cell 4 to cell 5 (2x3 grid, `AddDockTab`, :5744-5745). The
  separate hardcoded `public const int DockPauseCellIndex = 4` (:5690, previously) is
  consumed by `Assets/Editor/Regression/HudUiRegression.cs:1974-1975` to measure
  whether the Pause cell overlaps the movement-stick mount (the WO-1465 regression for
  the captured `AdaptiveHudGearOpen` defect). Left at `4` post-flip, that regression
  would silently measure the now-Realm cell and report a false-clean pass while the
  real Pause cell (5) goes unchecked. Fixed by deriving the constant from the gate
  itself — `DockPauseCellIndex = ClanFeatureGate.PlayerFacingEnabled ? 5 : 4` — rather
  than hardcoding a second copy that can drift out of sync with `dockRow` (CLAUDE.md
  §5/§8's duplicated-state pattern). Correctness at index 5 is not asserted by any
  running suite in this hand-back (this is a C# compile-time constant; Unity
  `COMPILE_GATE_OK`/`REGRESSION_OK` were NOT run by this lane per instruction) — the
  citation for why it should hold is `HudUiRegression.cs:1985`'s case 9b, which asserts
  the WHOLE open-drawer panel clears the MoveCluster mount, so any cell inside that
  panel (including the new bottom-right one) clears it too. **Flagging for the lead's
  own Unity gate run to confirm**, since I could not run it myself.
- `api/schema.sql`: added the same `clans_join_policy_valid` CHECK inline in the
  `CREATE TABLE clans` block (schema.sql documents the CURRENT shape, not migration
  history — matches this repo's existing convention, e.g. `clans_code_format` etc.
  already inline there). Confirmed the "schema.sql may never disagree with
  api/migrations" test (`test/clan-membership.test.js:797-805`) only loose-matches
  table names + the migration filename string, not exact CHECK text, so this addition
  cannot break it — proven by the final green suite run below.

### Verification (after all edits)

- `python tools/gate_brace.py` on all four touched `.cs` files: `GATE_BRACE_SUMMARY
  bad=0 of 4`.
- NUL-byte scan on all four touched `.cs` files: `0` in each.
- `node --check` on `api/_lib/clan.js`, `api/clan/create.js`,
  `test/clan-chat-embed.test.js`, `test/clan-chat-release-gate.test.js`,
  `test/clan-two-wallet-integration.test.js`: all syntax-OK.
- `node --test test/*.test.js` AFTER: **1045 pass / 2 unique fail**
  (`.claude_wo1851_final.log`) — the 2 remaining failures are the SAME pre-existing
  Heartbound-suite findings present in the baseline (WO-1693 territory, unrelated to
  clans). The two clan-leaderboard baseline failures are also gone in this run —
  evidence points to a concurrent lane (WO-1850, editing the same `api/_lib/clan.js`
  and `test/clan-leaderboard.test.js` in the shared working tree throughout this
  session, confirmed via `git status --short` showing `M api/_lib/clan.js` before I
  even started reading it) landing its own fix mid-session; NOT something this lane
  touched or claims credit for.
- **NOT run by this lane, per instruction:** Unity `COMPILE_GATE_OK` and
  `REGRESSION_OK <n>/<n>`. The lead must run these before commit — the
  `ClanFeatureGateRegression.cs` and `HudUiRegression.cs`-adjacent changes above are
  proven only by static reading and the JS suite, not by an actual Unity batchmode
  pass.
- **NOT run — requires a live database, unavailable in this environment:** test plan
  item 1 (direct `INSERT ... join_policy = 'closed'` against a real Postgres instance
  to prove the CHECK constraint fires at the DB level). The constraint's SQL is
  standard Postgres `CHECK (col IN (...))` syntax and the migration passes this repo's
  own additive/idempotency audits, but the live-DB behavior itself is unproven from
  here — say so rather than claim it.

### Files touched by this lane (git status, confirmed file-disjoint from WO-1850/1858)

Modified: `Assets/Editor/Regression/ClanFeatureGateRegression.cs`,
`Assets/_Modules/Core/Services/ClanFeatureGate.cs`,
`Assets/_Modules/Core/Services/ClanService.cs`,
`Assets/_Modules/HUD/Kit/HudKitController.cs`, `api/_lib/clan.js`,
`api/clan/create.js`, `api/schema.sql`, `test/clan-chat-embed.test.js`,
`test/clan-chat-release-gate.test.js`, `test/clan-two-wallet-integration.test.js`.
New: `api/migrations/20260917_0033_clan_join_policy_check.sql`.
Confirmed NOT touched: `api/clan/leaderboard.js`, `test/clan-leaderboard.test.js`
(WO-1850), `Assets/_Modules/HUD/ClanChatPanel.cs`, `IClanChatWebHost.cs`,
`ClanChatWebHostGreeWebView.cs`, `ClanMembershipClient.cs`, `ClanChatSource.cs`
(WO-1858) — all left exactly as found in the shared working tree.

Not committed (per instruction — sole committer is the CLI lead).
