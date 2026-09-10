# WO-1454: one transient 5xx on renewal clears a still-valid session and darkens cloud save permanently

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Web3/BackendRequestSigner.cs`. Client only.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1454 -> 1455 in the same edit).

## 1. EVIDENCE

```
BackendRequestSigner.cs:570-575   ClearSession(); return false;      // on ANY non-Success result
BackendRequestSigner.cs:558-568   only a TRANSPORT throw preserves the token
```

So an HTTP 500 or 503 - the server saying "try again", not "you are not who you say" - destroys the stored
token. Save passes `allowMint:false`, so nothing re-mints it: from that moment the client reports
`why=missing` on every save, permanently, until the player re-authenticates by hand.

Given WO-1446 and WO-1453 both produce 500s on live rails today, this is not hypothetical.

## 2. FIX SHAPE

- Clear the session ONLY on 401 and 403 (the server has actually rejected the credential).
- On 5xx, timeout, or any transport failure: keep the token and retry with backoff.
- Trace the decision permanently: `FlowTrace.Warn("Web3", "renewal failed status=... action=keep|clear")`.

## 3. WHAT NOT TO DO
- Do not set `allowMint:true` on save as the workaround; that mints sessions on server hiccups.

## 4. ACCEPTANCE
- [ ] 500/503/timeout leave the token intact and schedule a retry; 401/403 clear it. File:line in the RESULT.
- [ ] Regression covering all four status classes, RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.
