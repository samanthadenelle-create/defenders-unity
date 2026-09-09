# WO-1452: the absolute session cap is defeated by sending any junk X-Nonce header

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:42, build 2026.09.08.361259). PRIOR STATUS: FIXED - the renewal gate keys on a VERIFIABLE signature (nonce+signature offered -> session withheld from verifyWallet, signature verified, nonce burned; junk headers fall back to the capped renewal); behavioural tests against a faked Neon driver, npm test 452/452 (2026-09-07); deployed to production in the same hour; owner felt-test closes (sign in, play a day, no re-prompt, no bypass). PRIOR STATUS: READY TO IMPLEMENT
**Silo:** `api/auth/session.js` + `api/_lib/wallet-auth.js`. Sequence AFTER WO-1446 (the column must exist).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1452 -> 1453 in the same edit).

## 1. EVIDENCE

The renewal path is gated on the ABSENCE of a nonce header:

```
api/auth/session.js:91     if (sessionHeader && !nonceHeader) { ...renewal, carries signed_at forward... }
```

But with a nonce present, `verifyWallet` still tries the SESSION rail first and returns ok before it ever
looks at the nonce:

```
api/_lib/wallet-auth.js:569-572   session rail attempted first, returns ok
```

Control then reaches `issueSession` with `signedAt` undefined, and the INSERT resolves
`COALESCE(NULL, NOW())` - which RESETS the chain origin.

So a client that presents a valid session token together with any arbitrary `X-Nonce` value renews forever.
The absolute cap the whole feature exists to enforce never fires.

## 2. FIX SHAPE

- When a VALID session is presented, always carry the existing `signed_at` forward, regardless of what other
  headers accompany it. The chain origin is a property of the session, not of the request shape.
- Make the branch at `session.js:91` about which credential VERIFIED, not about which header was present.

## 3. WHAT NOT TO DO
- Do not reject requests that carry both headers. Clients legitimately send stale headers; the cap must hold
  without depending on client hygiene.

## 4. ACCEPTANCE
- [ ] `node --test` case: valid session + junk nonce -> `signed_at` unchanged in the row; RED proof stated.
- [ ] `node --test` case: the absolute cap expires the session at the boundary even under repeated
      session+nonce renewals.
- [ ] `node --test` green across `test/`.
