# WO-1448: every scene enter overwrites local resources from a possibly stale server row

**Status:** IMPLEMENTED - bea5c8240 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Core/State/PersistenceBridge.cs` + `GameStateService.LoadFromBackend`. Same silo as
WO-1447; sequence this AFTER it.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1448 -> 1449 in the same edit).

## 1. EVIDENCE

```
PersistenceBridge.cs:117-138    fires LoadFromBackend on EVERY scene enter
GameStateService.cs:2104        _state.Resources = server.Resources;   // unconditional
GameStateService.cs:2145        Save();
```

There is no recency comparison. `serverLastSeenMs` is parsed at `GameStateService.cs:2079` and then never
read again.

So a player who spends resources, enters a raid scene, and returns before the server row catches up has the
older server numbers written over the newer local ones and immediately persisted. The overwrite is silent -
no trace line records that a decision was even made.

## 2. FIX SHAPE

- Compare the server row's `updated_at` (already carried as `serverLastSeenMs`) against the local last-save
  timestamp. Newer wins. Apply nothing when local is newer.
- Emit a permanent `FlowTrace.Step("Persist", "backend load: server=... local=... winner=...")` on both
  branches, so the choice is always in the log (never strip, CLAUDE.md sec.12).
- Regression: server row older than local -> local resources unchanged; server newer -> applied.

## 3. WHAT NOT TO DO
- Do not stop calling `LoadFromBackend` on scene enter; that is the cross-device sync path.
- Do not merge field-by-field with max(); resources are spendable and a max() merge mints currency.

## 4. ACCEPTANCE
- [ ] Recency gate in place, both branches traced.
- [ ] Two regression cases (older server, newer server), RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.
