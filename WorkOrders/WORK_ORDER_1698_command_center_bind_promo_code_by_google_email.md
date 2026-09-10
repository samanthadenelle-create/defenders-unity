# WORK ORDER 1698 - Command Center: look a Google-account player up by email and bind a promo code to them

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 16:30 by the CLI lead from the owner's request ("add a spot in the command center to use a google email address to look up automatically and add to code"); main-line banner bumped 1698 -> 1699 in the same edit. NOT dispatched (owner wrap-down 15:30).
**Silo:** Backend (api/auth + api/admin + one additive migration) + Command Center page. No gameplay, no .cs, no scene.
**Owner intent:** a Play (Google-signed-in, no wallet) tester should be able to receive a bound promo code without the owner hunting for their `play-...` id.

## 1. Facts read at source (2026-09-10)
- The Google rail derives the player id as `'play-' + HMAC-SHA256(GOOGLE_IDENTITY_KEY, sub)` (`api/_lib/google-identity.js:113-127`); the client never mints it; the rail is behind `GOOGLE_IDENTITY_ENABLED` (`:36`, `:88`).
- **The email is NOT stored.** `grep -i email` over `api/_lib/google-identity.js`, `api/auth/google-session.js` and `api/schema.sql` finds no email column for the Play identity (the only hit, `schema.sql:1470`, is the patron-name rule). So today there is nothing to look an email up against.
- `promo_codes.bound_wallet` (`api/schema.sql:440`) is compared against the player id regardless of shape - a `play-...` id binds exactly like a wallet. The Command Center already reports whether a code is bound (`api/admin/stats.js:766-784`) and never leaks the id (`:2053`).
- Promo redeem admits the Play shape on granting routes per `api/_lib/wallet-auth.js:133-146` (PLAY_RE); the route's own acceptance of `X-Session` for a Play id is **unverified** by the lead - verify first.

## 2. Deliverables
1. **Store an email fingerprint at sign-in, never the email.** In `api/auth/google-session.js`, when the verified ID token carries `email` with `email_verified=true`, persist `email_hmac = HMAC-SHA256(GOOGLE_IDENTITY_KEY, lowercase(trim(email)))` on the Play identity row (additive migration `api/migrations/20260910_0027_play_identity_email_hmac.sql`: `ADD COLUMN IF NOT EXISTS email_hmac TEXT` + index; mirror the CREATE TABLE description in `api/schema.sql`). A raw email must never land in a column or a log (`api/_lib/audit.js` hashIp idiom). Existing players get the fingerprint on their next sign-in; say so in the UI.
2. **Admin lookup + bind, one route:** `api/admin/promo-bind.js` (admin-authed like the other `api/admin/*` routes - copy the auth preamble, do not invent one): body `{ email, code }` -> compute the same HMAC -> find the `play-...` id -> `UPDATE promo_codes SET bound_wallet = <id> WHERE code = <code> AND (bound_wallet IS NULL OR bound_wallet = <id>)`; refuse (200 + `{success:false, error}`) on NO_MATCH, CODE_NOT_FOUND, ALREADY_BOUND_ELSEWHERE, or a code with `active=false`; never return the id to the page (report "bound" only, matching `stats.js:2053`). Audit via `logApiEvent`.
3. **Command Center page:** a "Bind code to Google player" card in `api/admin/console.js`: email field, code field, one button, result line in words. Colourblind-safe: words, not colour.
4. **Tests:** node:test for the HMAC derivation (same key + same input = same fingerprint; case/whitespace normalised), the route's four refusals, and that no response or log line contains the raw email.

## 3. Acceptance
- A Google tester signs in once, the owner types their email + a code, the card says "bound", the tester redeems the code in the app, `promo_redemptions` shows the `play-...` id.
- `node --test` green for the new file; `test/command-center.test.js` still green; migrations runner `MIGRATIONS_OK` after the owner applies 0027.

## 4. Do NOT touch
`api/promo/redeem.js` (identity gates), `api/_lib/wallet-auth.js`, any `.cs`, `CLI_LANES_WO_NUMBERS.md`.

## 5. Owner questions
- Q1: should the lookup also work for a wallet address typed into the same field (cheap: the field accepts either shape)?
- Q2: should the card show the tester's display name back for confirmation (needs a stored name; today nothing is stored)?
