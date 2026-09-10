# WO-1446: the renewal cap cannot deploy - auth_sessions.signed_at does not exist on the live Neon DB

**Status:** IMPLEMENTED - owner ran the schema repair (her word, 2026-09-09); api deploy hold LIFTED; prod parity re-check owed at the next deploy

### OWNER RULING 2026-09-09 (recorded by the CLI lead)
The owner ran `tools/run-schema-repair.mjs` against live Neon before 2026-09-09 evening. The migration file exists on HEAD, and the runner executed it live. API deploy hold is lifted; a prod parity re-check is owed at the next deploy to confirm the live column matches. WO-1449 and WO-1453 were held behind this blocker.
**Note 2026-09-06:** the `signed_at` ALTER TABLE in `api/schema.sql` arrived under WO-1441, not under this ticket. This ticket's own acceptance - a numbered migration file, the live-DB sweep, and the drift test - is still unmet, so it stays READY.
**Silo:** `api/auth/session.js` + `api/_lib/wallet-auth.js` + `api/migrations/` + `api/schema.sql`. Backend only,
disjoint from every Unity lane.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1446 -> 1447 in the same edit).

## 1. EVIDENCE

Live-DB sweep, run this session:

```
node tools/wo1440-alter-column-sweep.mjs
MISSING ON LIVE DB: auth_sessions.signed_at
```

The column is INSERTed unconditionally on the normal mint path:

```
api/_lib/wallet-auth.js:315-320   INSERT ... signed_at ...
api/auth/session.js:175           calls issueSession(...)
```

`api/auth/session.js:137-146` claims a schema-missing column falls through safely - but the fall-through
lands in `issueSession`, which is the very statement that names the column. No numbered migration exists;
the column appears only in `api/schema.sql:244`, which is not applied to prod. The uncommitted renewal-cap
diff is preserved at `WorkOrders/patches/wo1441-api-renewal-cap.UNCOMMITTED.patch`.

Net: shipping the renewal cap as it stands 500s every wallet session on prod.

## 2. FIX SHAPE

- Add `api/migrations/2026090x_00xx_auth_sessions_signed_at.sql` (ADD COLUMN IF NOT EXISTS, plus the index
  the renewal cap reads). Owner runs `tools/run-schema-repair.mjs`; record the shape query, not the exit code.
- Make `issueSession` tolerate the column being absent (probe once, cache), OR gate the deploy on a green
  `wo1440-alter-column-sweep.mjs` - one of the two, named in the RESULT.
- Add a `node --test` case that FAILS if `wallet-auth.js` INSERTs any column that no file under
  `api/migrations/` creates. That test is the durable fix; the migration is only today's instance.

## 3. WHAT NOT TO DO
- Do not rely on `CREATE TABLE IF NOT EXISTS` in `schema.sql` to add a column to an existing table - it
  reports success and does nothing (memory `idempotent-ddl-hides-a-stale-table`).
- Do not delete the renewal cap to unblock the deploy.

## 4. ACCEPTANCE
- [ ] Numbered migration exists and the live sweep reports the column PRESENT (paste the sweep output).
- [ ] `issueSession` path proven on prod with a real wallet session (HTTP status + body quoted).
- [ ] The column-vs-migration drift test exists and goes red when a column is removed from migrations.
- [ ] `node --test` green across `test/`.
