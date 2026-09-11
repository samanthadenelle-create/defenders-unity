# Google promo email lookup: operator procedure (WO-1698)

The lookup uses `play_identities.player_id` and `email_hmac`. It stores no raw Google
email or subject. The fingerprint is lookup metadata, never authentication proof.

Apply migration `20260910_0027_play_identity_email_hmac.sql` before enabling the
Command Center workflow, and verify the migration runner's `MIGRATIONS_OK` marker.
An existing player appears after their next verified Google sign-in. Only a boolean
`email_verified: true` claim populates the fingerprint. Missing/unverified claims
clear an older fingerprint. Rotation-pinned players retain their resolved player ID.

The lookup write is optional: a missing schema or database error is logged without
input or database error text, and sign-in continues. Its one-second deadline aborts
only the metadata request. Authentication/session lifetimes and purchase guards are
unchanged. A timed-out write may already have reached the database; retrying sign-in
safely upserts the same row. The admin route fails closed if lookup is unavailable.

In the Command Center Promos tab, create or choose an active code, then use **Bind
code to Google player**. Both admin read and write keys are required. A successful
response reports **Bound**, never the player ID or email. Multiple matching identity
rows refuse as `AMBIGUOUS_MATCH`; investigate the records rather than choosing one
arbitrarily. Wallet input and display-name confirmation are outside this workflow.

## Account erasure inventory

`api/account/delete-request.js` and `api/_lib/account-deletion.js` enqueue verified
requests; they do not perform erasure. When fulfilling a verified full-account
request, the operator must include the `play_identities` row in the existing erasure
workflow, keyed by that request's `player_id`. Use a parameterized operation:

```sql
DELETE FROM play_identities WHERE player_id = $1;
```

Verify no row remains for that identifier before marking the request completed.
Never put the supplied email, fingerprint, or Google subject in `operator_note` or
an execution log. Record the request reference, action and affected-row count.
Apply existing retention decisions to purchase/security records separately; this
lookup table does not justify retaining its email fingerprint. Partial associated-
data requests retain their requested category scope and must not be silently
expanded into account deletion.

Erasing lookup metadata does not revoke sessions or prevent a later valid Google
sign-in from recreating it. Coordinate this step with the existing full-account
fulfillment process; this feature adds no account-disable or authentication behavior.

## Release proof still required

Local tests do not prove a deployed migration or Google session. After deployment,
verify: migration marker; tester signs in again; owner binds the existing code;
Google tester redeems it; `promo_redemptions` records the expected Play player.
Do not copy that identifier into the Command Center output or public evidence.
