# WO-1867 RESULT — Player Issues tab has no dismiss/resolve path

**Status: DONE**

## What shipped

- **Migration** `api/migrations/20260918_0037_bug_reports_status.sql` — additive, idempotent
  (`ADD COLUMN IF NOT EXISTS` + a guarded `pg_constraint` check for the CHECK constraint, matching
  the `20260825_0002_repair_parity_remainder.sql` idiom since Postgres has no
  `ADD CONSTRAINT IF NOT EXISTS`). Adds `status TEXT NOT NULL DEFAULT 'open'` (CHECK `open` /
  `resolved` / `noise`), `reviewed_by TEXT NULL`, `reviewed_at TIMESTAMPTZ NULL`, and a partial index
  `idx_bug_reports_status_open` for the default open-only read. Mirrored into `api/schema.sql`
  (`CREATE TABLE` body updated for fresh deployments + the applyable-copy `ALTER TABLE` block, same
  convention as the `wallet_identity` / `clan_ballots` sections).
- **Write endpoint**: extended `api/admin/ops.js` / `api/_lib/ops.js` with a new allowlisted action
  `bugreport.set_status` (`OPS_ACTIONS`), gated by the existing `X-Admin-Key` + `X-Admin-Ops-Key`
  pattern — no new auth scheme. Accepts `reportId` or `reportIds` (batch, capped at 25, de-duped) +
  `status`. `setBugReportStatus` is an `UPDATE ... SET status, reviewed_by, reviewed_at = NOW() WHERE
  report_id = ANY(...)`, never a `DELETE`. Every write is recorded via the existing
  `recordOpsWrite` audit trail.
- **Read side**: `api/admin/stats.js` `?view=ops` `reports` field now defaults to
  `WHERE status = 'open'` (still capped at 50, newest first); `?reportStatus=all` is the audit
  escape hatch that also returns resolved/noise rows. Both branches stay plain `SELECT`s — the
  endpoint remains SELECT-only by construction (`test/command-center.test.js`'s source lint still
  passes). Response rows now also carry `status` (as a WORD, colourblind-safe), `reviewed_by`,
  `reviewed_at`.
- **Console UI**: `api/admin/console.js` Player Issues tab now renders a "Resolved" / "Noise" button
  per row, POSTing `bugreport.set_status` through the existing `postOps` helper and refreshing via
  the existing `opsResult` → `load()` path. `docs/COMMAND_CENTER_REFERENCE.md` updated — the
  "Read-only — no actions here" line is corrected and the new write/action is documented in the ops
  action table.

## What did NOT change

- `api/bug-report.js` and the Unity client (`HelpMenu.cs`) — untouched, per scope.
- No `DELETE` anywhere in this change. Confirmed by a lint assertion in the new test
  (`setBugReportStatus is an UPDATE ..., never a DELETE`) and by grep.
- No `.cs` files touched — this is backend (`api/`) + console JS only. (Brace/NUL gate: N/A.)

## Tests

`node --test test/command-center.test.js test/command-center.refusal-logging.test.js` — **71/71
pass**, including 5 new tests for this WO:
- `validateBugReportStatusSet accepts a single id or a batch, and bounds both`
- `setBugReportStatus is an UPDATE of status/reviewed_by/reviewed_at, never a DELETE`
- `the bug-report status action reaches the OPS endpoint allowlist and money tables are still untouched`
- `the ops read view defaults Player Issues to OPEN rows, and reportStatus=all is the audit escape hatch`
- `the console renders a resolve/noise action per issue row, and it is not silently read-only any more`

Also re-ran `test/admin.*.test.js` (111/111 pass) and the full `test/*.test.js` suite (1229 tests:
1226 pass, 2 pre-existing failures unrelated to this WO — `welcome letter is one-shot...` and
`the streak reaches heartbound_state` in the Heartbound suite, plus one Unity-asset scan failure
referencing `Assets/Editor/WallTools/RaidPostAudit.cs` — none touch `api/admin`, `api/_lib/ops.js`,
`api/schema.sql`, or `bug_reports`, and none were introduced by this change).

## Not run — needs an explicit go-ahead

The migration was **not applied against the live database**. Applying it, or POSTing the
`bugreport.set_status` write for report ids 11-14, touches live production data and was not run
without confirmation. Exact commands, once approved:

```bash
psql "$DATABASE_URL" -f api/migrations/20260918_0037_bug_reports_status.sql
```

```bash
curl -X POST 'https://defenders-of-the-realm-v2.vercel.app/api/admin/ops' \
  -H 'Content-Type: application/json' \
  -H 'X-Admin-Key: <ADMIN_DASH_KEY>' \
  -H 'X-Admin-Ops-Key: <ADMIN_OPS_KEY>' \
  -d '{"action":"bugreport.set_status","reportIds":[11,12,13,14],"status":"noise","by":"Samantha"}'
```

That marks all four rows `noise` in one call (batch cap is 25, well above 4). Verify with:

```
GET /api/admin/stats?view=ops&reportStatus=all
```

— ids 11-14 should show `status: "NOISE"`, still present in the row list (nothing deleted), and
absent from a plain `?view=ops` (no `reportStatus`) call.
