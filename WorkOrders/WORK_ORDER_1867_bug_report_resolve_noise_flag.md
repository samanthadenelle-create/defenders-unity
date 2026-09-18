# WO-1867 — Player Issues tab has no dismiss/resolve path

**Status: READY TO IMPLEMENT**

## Owner report

Looking at the 4 newest `bug_reports` rows (ids 11-14): same player (masked `9de8…3ff9`), same
route, submitted 3 seconds apart, description `"ggsgagsbshhshs"` on all four (keyboard mash, not a
real report). Owner: *"mark as resolved or noise so we can isolate"* — she wants to triage these
out of the active queue without deleting the data.

## Confirmed at source

`bug_reports` (`api/schema.sql:847-855`) has no status column at all. The admin console's Player
Issues tab is explicitly documented as read-only: `docs/COMMAND_CENTER_REFERENCE.md` "Tab: Player
issues" — *"Read-only — no actions here."* There is currently no way to mark a report reviewed,
resolved, or noise anywhere in the stack.

## Scope

- New migration (`api/migrations/<timestamp>_bug_reports_status.sql`): add a `status` column to
  `bug_reports`, default `'open'`, with a CHECK constraint limiting it to `'open'`, `'resolved'`,
  `'noise'`. Also add `reviewed_by` (free text, admin identifies themself) and `reviewed_at`
  (nullable timestamptz). Mirror the migration into `api/schema.sql` per this project's existing
  convention (grep any other migration for the "applyable copy" pattern already used elsewhere in
  that file, e.g. the `wallet_identity`/`clan_ballots` sections, and follow the same shape).
- New write endpoint (or extend `api/admin/ops.js` if its POST-write pattern fits): a way to set
  `status` + `reviewed_by` + `reviewed_at` on one or more `report_id`s. Auth: same write-key shape
  as every other admin write in `ops.js` (`X-Admin-Key` AND `X-Admin-Ops-Key`).
- `stats.js`'s `ops` view (`reports` field) should default to returning only `status = 'open'` rows
  (still capped at newest 50), with an optional query param to include resolved/noise ones for
  audit purposes. Do NOT delete or filter out the underlying rows — this is a triage flag, not data
  loss.
- Console UI (`api/admin/console.js`, Player Issues tab): add a small per-row action (or a
  lightweight bulk-select) to mark a report `resolved` or `noise`, calling the new endpoint. Follow
  the console's existing colorblind-safe state-word conventions (no color-only signal) and its
  no-PII rule (nothing new that surfaces an identity).

## What NOT to touch

- The `bug_reports` write path itself (`api/bug-report.js` / the client's `HelpMenu.cs` POST) —
  this WO is read-side triage only, not changing what gets recorded.
- No deletion of any row, ever — this is a status flag, not a purge.

## Acceptance criteria

- [ ] Migration applies cleanly against the live schema (additive column, existing rows default to
  `'open'`).
- [ ] The 4 flagged rows (ids 11-14) can be marked `noise` via the new endpoint/console action and
  then no longer appear in the default Player Issues view.
- [ ] The underlying rows still exist in the table (verify by a direct query with the audit param).
- [ ] `node --test` suite for `api/admin/*` still passes; add a test for the new status filter.
- [ ] Brace/NUL gate clean on any `.cs` touched (none expected — this is a backend + console-JS WO).
