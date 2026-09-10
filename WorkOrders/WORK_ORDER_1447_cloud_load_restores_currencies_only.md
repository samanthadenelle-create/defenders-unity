# WO-1447: cloud LOAD restores currencies only - a reinstall or a new device loses the entire town

**Status:** IMPLEMENTED - bea5c8240 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Core/State/GameStateService.cs` (LoadFromBackend) + `api/game/load.js` (read-only
confirmation). Disjoint from the raid, HUD and Manage lanes.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1447 -> 1448 in the same edit).

## 1. EVIDENCE

`GameStateService.cs:2894` deserializes a full `PersistedState` from the backend payload. The apply block,
`GameStateService.cs:2099-2140`, then copies exactly seven fields:

```
BestWave / Resources / Voidshards / AetherCrystals / Stone / Iron / Wood
```

and calls `Save()` at `GameStateService.cs:2145`.

It never calls `ApplyPersisted` and never calls `MigrateForImport`. Those two run only on the LOCAL load path
(`GameStateService.cs:387` and `:404`).

The server is not the limiter - `api/game/load.js:100-104` returns the whole state document. Structures,
army, build queue, echoes, cosmetics and quest state are all present in the payload and all dropped on the
floor. A player who reinstalls, or signs in on a second device, gets their currencies and a blank town.

## 2. FIX SHAPE

- Route the deserialized backend `PersistedState` through the SAME `MigrateForImport` + `ApplyPersisted` path
  the local load uses. One apply function, two callers - not a second field-copy list.
- Keep the currency fields flowing through that same path; delete the bespoke seven-field block.
- Regression: build a server row carrying structures + army + a queued job, run `LoadFromBackend`, assert all
  three are present afterwards. State the RED proof in-file (today it goes red on structures).

## 3. WHAT NOT TO DO
- Do not extend the seven-field list to twenty fields. A hand-maintained copy list is the defect; every new
  save field would silently fail to restore again.
- Do not change the save schema version to force a path.

## 4. ACCEPTANCE
- [ ] `LoadFromBackend` calls `MigrateForImport` + `ApplyPersisted` (file:line in the RESULT).
- [ ] New regression case, RED proof stated, green after.
- [ ] `REGRESSION_OK n/n` on a fresh log.
