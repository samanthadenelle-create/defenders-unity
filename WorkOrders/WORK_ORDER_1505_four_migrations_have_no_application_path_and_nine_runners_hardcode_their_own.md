# WO-1505: four numbered migrations have NO application path, and nine hand-rolled runners each hardcode their own list

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:50, build 2026.09.08.361259). PRIOR STATUS: FIXED - tools/run-migrations.mjs is the single runner; seven run-*-migration.mjs are refusal shims (exit 16 -> use run-migrations.mjs), every .sql on disk reachable, pinned in test/migrations.runner.test.js (npm test 452/452, 2026-09-07). RESIDUAL for the owner: tools/wo1440-apply-0013-repair.mjs and wo1440-apply-migration.mjs still apply their own ids (outside the run-*.mjs glob); no --dry-run exists (needs DB contact); the live-anchor docs still name run-schema-repair.mjs (re-point per s15). PRIOR STATUS: READY TO IMPLEMENT
**Silo:** `api/migrations/` + `tools/run-*-migration*.mjs`. Parent of WO-1446 (the signed_at gap is one
instance of this family).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1505 -> 1506 in the same edit).

## 1. EVIDENCE

`api/migrations/` holds 0001 through 0019. Nine separate runners each hardcode which ones they apply:

```
tools/run-schema-repair.mjs               0001, 0002
tools/run-promo-pack-migration.mjs        0005, 0006
tools/run-google-play-ledger-migration.mjs 0007
tools/run-card-collection-migration.mjs   0008
tools/run-town-showcase-migration.mjs     0009, 0011
tools/run-showcase-contest-migration.mjs  0010, 0012
tools/wo1440-apply-0013-repair.mjs        0013
tools/run-play-policy-migrations.mjs      0014-0016
tools/wo1440-apply-migration.mjs          0019
```

**0003 (patronage_benefactors), 0004 (promo_reward_tiers), 0017 (pi_payments) and 0018 (client_tunables) are
referenced by NO runner** - they have never had an application path. Live state is unprovable without
`DATABASE_URL`.

WO-1446's missing `auth_sessions.signed_at` is the same family: there is no ledger saying what prod has, so a
column can be INSERTed by code that no migration created and nothing notices until a 500.

## 2. FIX SHAPE

- ONE ledger-driven runner that applies every file under `api/migrations/` in order and records applied ids in
  an `applied_migrations` table. The owner runs it once.
- The schema-parity gate compares the live DB against the LEDGER, not against a hand-written list.
- Retire the nine bespoke runners once the ledger runner covers their ids.

## 3. WHAT NOT TO DO
- Do not write a tenth bespoke runner for 0003/0004/0017/0018. That is the defect, one iteration later.
- Do not assume the four unapplied migrations are absent from prod; the ledger run will tell you.

## 4. ACCEPTANCE
- [ ] Ledger runner exists; a dry run lists exactly which of 0001-0019 prod is missing (output pasted).
- [ ] After the owner's run, the ledger table matches the directory.
- [ ] The parity gate reads the ledger; proven by adding a migration and showing the gate red before it runs.
- [ ] `node --test` green across `test/`.
