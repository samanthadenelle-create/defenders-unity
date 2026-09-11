# WO-1698 result - Google-email promo binding

Implemented a shared normalized email fingerprint and optional Play lookup table.
Only verified Google email claims populate the lookup; no raw email is persisted.
The optional sign-in metadata write has a one-second abort deadline and cannot
hold up an otherwise valid sign-in when schema or transport is unavailable.

The admin bind endpoint reuses both existing admin keys and conditionally binds
only an active code that is unbound or already belongs to the same player.
Missing/ambiguous identity, missing/inactive code and conflicting binding refuse
with stable words. Responses and audit records disclose no email or player ID.
The Command Center adds an email-only card; optional wallet/name expansion was
not needed for the requested workflow.

## Verification

- Isolated lane: 200 relevant Node tests passed.
- Root: 147 tests passed, zero failed/skipped, in
  `Builds/ready-iter1-backend-tests.log`. Includes new promo-bind tests,
  Command Center/refusal logging, Google payment-provider/verification/RTDN/voided
  tests, and existing promo owner-auth behavior.
- The new tests verify signed Google claims and tamper refusal, existing Play
  X-Session promo authentication, conditional binding, non-disclosure, clearing
  unverified metadata, and a hung optional lookup releasing the sign-in path.
- `node tools/schema-parity.mjs --expected-only`: `SCHEMA_PARSE_OK`, 48 tables.
- `git diff --check` clean for this lane.

## Pending proof

Migration `20260910_0027_play_identity_email_hmac.sql` has not been applied here.
No production deployment or live tester redemption was performed; the UI was
verified by JavaScript tests, not a rendered screenshot. Owner/ops procedure and
account-erasure inventory are in `docs/GOOGLE_PROMO_LOOKUP_OPERATIONS.md`.
Existing players enter the lookup on their next verified sign-in after migration.
This is Fixed awaiting that integration/test proof, not owner-closed.
