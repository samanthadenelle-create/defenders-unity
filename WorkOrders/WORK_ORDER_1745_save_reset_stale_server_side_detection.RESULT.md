# WO-1745 — RESULT

**Status:** DONE (2026-09-15). Edit-only lane: no git, no gate, no deploy — the lead gates and commits.
**Parent:** WO-1742 Lane A2.

## Files changed

| File | Change |
|---|---|
| `api/game/save.js` | the 409 `SAVE_RESET_STALE` refusal is audited through `logAuthReject` instead of `logApiEvent('save_reset_refused')` — one row, in the shape the admin view reads. `console.warn` kept. The 409 status/body/guard UNCHANGED. |
| `api/admin/db.js` | `view=authrejects` widened at **all four** `event_name IN (…)` sites to also read `'save_reset_refused'` (the historical rows, 2026-09-07 → deploy), plus a per-era path label so those rows are not mislabelled `(legacy auth_failed)`. |
| `test/game.save.reset-epoch.test.js` | 13 → 14 cases. Case 3 asserts the `logAuthReject` row (code / ref / identity / `detail.incoming,stored,behindBy`) **and** that no second row is written; case 4's vacuous pin replaced; new case parses `db.js` and pins both event names in every IN list. |

No `.cs` file was opened for edit. `GameStateService.cs` untouched.

## The finding that corrects the parent

WO-1742 §8 says the 409 is *"invisible to the dashboard because it is client-side only"*. **Wrong.**
`save.js:539` has written a durable row on every refusal since WO-1598 landed 2026-09-07 — under the
event name `save_reset_refused`, which `api/admin/db.js`'s only refusal reader never included in its
filter. The blindness was at the VIEW layer, not the write layer.

⚠ **WO-1742 §3's "zero refusals on `/api/game/save` in seven days" was read off that blind view and
is a VIEW ARTIFACT, not a measurement.** Flag it `STALE:` in the parent.

## Audit row shape

**Table:** `analytics_events` (no migration — `logAuthReject`'s existing home).
**`event_name`:** `api_auth_reject` · **`player_id`:** the identity that made the request.

```jsonc
{ "code": "SAVE_RESET_STALE", "ref": "<the ref handed to the player>",
  "mode": "guest|wallet|google", "method": "POST", "path": "/api/game/save",
  "identityLen": 76, "ipHash": "<12-hex salted>",
  "detail": { "incoming": 2, "stored": 5, "behindBy": 3, "stage": "reset_epoch" } }
```

No secrets: no body, no signature, no token, no raw IP.

## Admin view + the post-deploy query

`api/admin/db.js` → `view=authrejects` (the only reader; widened here).

```
GET /api/admin/db?view=authrejects&code=SAVE_RESET_STALE&since_hours=168
```

`summary[].distinct_ids` = **how many separate devices are frozen** — correct across BOTH eras;
`rows[].player_id` names each. Answers retroactively over 2026-09-07 → today on deploy (no new
traffic needed) and settles WO-1742 §7c.

⚠ `detail.incoming/stored/behindBy` and `ipHash` exist **only on post-deploy rows**. Historical
`save_reset_refused` rows keep `incoming`/`stored` at the top of `properties`, so the view's
`detail` column is NULL for them — read those by direct SQL, not as a zero.

⚠ Retention differs by era: `api/admin/cleanup.js:114-128` purges `api_auth_reject` after 7 days
(which equals the view's own 168h ceiling, so nothing readable is lost); the historical
`save_reset_refused` rows are swept by nothing and persist.

**Checked:** no other consumer of the `api_auth_reject` bucket exists in `api/`, `tools/` or
`.claude/skills/` — only that retention sweep and a comment at `api/_lib/ops.js:55`. No cron,
throttle or alert counts rejects per player, so a frozen device cannot feed one.

## Tests

- `node --test test/game.save.reset-epoch.test.js` → **tests 14, pass 14, fail 0**
- `node --test test/*.test.js` → **tests 736, pass 735, fail 0, skipped 0, todo 1**
- Red proof: removing `'save_reset_refused'` from the four IN lists → **pass 13, fail 1** on the new
  case; restored, re-run green.

## Not proven / still blocked

- **AC #1 ("proven against a real response") is NOT PROVEN** — it needs a deploy this lane is
  forbidden to run. The §5 URL above is the single step that closes it.
- **Lane A remains BLOCKED on the owner's ruling** (WO-1742 §8 (a)/(b)/(c)): a device that is behind
  still drops the marker forever and tells nobody. This lane makes it **countable**, not **fixed**.
- `BOARD.html` not regenerated (lead's step, in the commit that carries the flip).

## ⚠ FOREIGN HUNKS IN THE SHARED TREE — read before committing by path

`api/game/save.js` carries **two hunks that are NOT WO-1745**, left uncommitted by another lane
(they match the untracked test case *"preserves owned town but never exports local capture recovery
intent"*):

- `:88-89` — `'pendingTownCapture', 'PendingTownCapture'` added to `RESERVED_KEYS`
- `:1187` — `module.exports.buildState = buildState;`

The lead commits by explicit path and cannot split a file, so both will ride along. The
735-pass/0-fail suite result was measured **with them present**.

Likewise: `CLI_LANES_WO_NUMBERS.md` shows ~114 changed lines — only the single top block
(pass 196, `next free = 1746`) is WO-1745; the rest is the uncommitted 1744 pass. And
`WorkOrders/WORK_ORDER_1742_…md` is still `??` untracked — this lane added the §1 `STALE` banner to
that other lane's uncommitted file (CLAUDE.md §15 requires it); reported so nobody overwrites it.
