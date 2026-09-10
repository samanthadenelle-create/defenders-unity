# WO-1445: OfflineHarvestService.Grant banks the clamped amount and throws the remainder away

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes. ! THE TICKET'S FIX SHAPE WAS NOT
FOLLOWED AS WRITTEN and the reason is in the RESULT: there is no pending store on this path to retain
onto. The remainder is NAMED IN WORDS instead (owner law, WO-1461). Contradiction raised for the lead.
PRIOR STATUS: READY TO IMPLEMENT - low severity today (dead on the owner's save), real divergence
**Silo:** `Assets/_Modules/Village/Harvest/OfflineHarvestService.cs` (`Grant`) + `OfflineHarvestRegression.cs`.
Disjoint from the Manage 2000-block, the raid lanes, the HUD kit and Build mode.
**Source:** found 2026-09-06 while re-verifying WO-1434 at source. Minted by the CLI seat from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1445 -> 1446 in the same edit).

## 1. THE FINDING, AT SOURCE

```csharp
// OfflineHarvestService.cs:1043-1050
int iron = TownBankCapacity.ClampGrant(BankResource.Iron, state.Iron, result.Iron, "OfflineHarvest", out _);
int wood = TownBankCapacity.ClampGrant(BankResource.Wood, state.Wood, result.Wood, "OfflineHarvest", out _);
int food = TownBankCapacity.ClampGrant(BankResource.Food, state.Resources.Food, result.Food, "OfflineHarvest", out _);
if (iron > 0) state.Iron += iron;
...
```

The clamp returns what fits; `result.Wood - wood` is computed nowhere and retained nowhere. WO-1434 established
the law for this screen (D3: capped yield is RECOVERABLE, not burned - `HarvestResultCopyRegression`
`[silo-never-burns]`), and both retaining producers (the collectors' pending and the silo's `AttachSiloPending`)
follow it. `Grant` is the one path that still burns.

Dead today because the owner's save reaches `Grant` with `total=0` for these three; it becomes live the first
time an offline absence produces wood/iron/food against a full bank.

## 2. FIX SHAPE

- `Grant` retains the pre-clamp remainder on the same pending store the producers use, so the popup's
  `<WORD> <Pending> WAITING` row is true for these three resources too. One retention mechanism, not a second.
- Permanent `FlowTrace.Warn("OfflineHarvest", "grant clamped ...", remainder)` at the clamp, so a burn is never
  silent again.
- A case in `OfflineHarvestRegression.cs`: bank at cap, `result.Wood = 500`, assert bank unchanged AND pending
  carries 500. State the RED proof in-file (today it goes red on the pending assertion).

## 3. WHAT NOT TO DO
- Do not raise the cap or bank past capacity to make the number land. `TownBankCapacity` is the ceiling.
- Do not add a second pending store; reuse the one WO-1434 made the popup read.

## 4. ACCEPTANCE
- [ ] `Grant` retains the remainder on the existing pending store (file:line cited in the RESULT).
- [ ] The new regression case, RED proof stated, green after.
- [ ] `REGRESSION_OK n/n` on a fresh log.
