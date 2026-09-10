# WO-1499: the welcome-back header says "(STORAGE FULL)" when the subject is the 10-hour away-window ceiling

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Village/Harvest/WelcomeBackPopup.cs` + `AwaySummaryReportRegression`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1499 -> 1500 in the same edit).

## 1. EVIDENCE

```
WelcomeBackPopup.cs:678    emits "(STORAGE FULL)" on WasCapped
WelcomeBackPopup.cs:192-194  the code ITSELF concedes:
   "the header suffix carries the SAME wrong subject and is pinned by
    AwaySummaryReportRegression case8 (line 243)"
```

Visible in `WelcomeBack_1920x1080.png`.

`WasCapped` is true when the 10-hour AWAY WINDOW ceiling bit, which is a different thing from the bank being
full. A player told their storage is full will go upgrade storage and see no change - the ceiling that
actually bit was time.

The wrong subject is PINNED, so the suite currently defends the defect.

## 2. FIX SHAPE

- Re-word the header to name the away window (for example "AWAY LIMIT REACHED"), and keep a genuine storage
  message for the genuinely-full case - they are two distinct states and the popup must distinguish them.
- Move the pin at `AwaySummaryReportRegression.cs:243` onto the corrected wording, and add a case for the
  storage-full state so both are covered.

## 3. WHAT NOT TO DO
- Do not delete the suffix. The player needs to know a ceiling bit; the defect is which ceiling it names.

## 4. ACCEPTANCE
- [ ] Away-window cap and bank-full cap produce DIFFERENT headers; both pinned.
- [ ] `AwaySummaryReportRegression` case8 re-pointed; RED proof stated.
- [ ] Fresh `WelcomeBack` capture opened.
- [ ] `REGRESSION_OK n/n` on a fresh log.
