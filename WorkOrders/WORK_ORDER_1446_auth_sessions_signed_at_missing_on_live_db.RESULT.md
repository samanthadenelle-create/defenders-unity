# WO-1446 RESULT — auth_sessions.signed_at migration completed

**Committed:** 2026-09-09  
**Status:** IMPLEMENTED by owner 2026-09-09 / CLI recorded

## What is PROVEN

**Migration file on HEAD:** `api/migrations/20260906_0020_auth_sessions_signed_at.sql`  
**Commit hash:** `26e9fb0a8`

- The numbered migration exists at source and is tracked in git.
- The runner `tools/run-schema-repair.mjs` applies this migration.
- The owner executed the runner against the live Neon database before 2026-09-09 evening (her word, recorded 2026-09-09).

## What is NOT proven from this seat

**The live column state:** Only a credentialed parity run (a Neon query against the live database, accessible to the owner and the deployment) can prove that the `auth_sessions.signed_at` column now exists on prod with the correct schema. This seat cannot read prod; therefore:

- ✗ No proof that the live sweep now reports `COLUMN PRESENT` instead of `MISSING`.
- ✗ No proof of the live column's type, constraints or index (though the migration defines it, the alter may have failed silently).

**Next step for verification:** at the next production api deploy, run a fresh instance of `tools/wo1440-alter-column-sweep.mjs` against the live Neon database and confirm it reports the column present. That check is **owed and pending** at the next deploy gate.

## Blocking releases held behind this ticket

- **WO-1449** — blocked on this column existing live.
- **WO-1453** — blocked on this column existing live.

Both are freed by the owner's execution of the schema repair, conditional on the next deploy's parity check passing.

## Notes

- The `issueSession` path tolerates the column being absent (`api/auth/session.js:137-146`), so shipping without full column proof is safe — but the drift-test suite (`node --test` on `api/auth/session.js`) validates that no new INSERT statement adds a column that no migration creates. That test is the durable fix and prevents this class of issue recurring.
- The renewal-cap diff (`WorkOrders/patches/wo1441-api-renewal-cap.UNCOMMITTED.patch`) can land once this deploy confirms parity.
