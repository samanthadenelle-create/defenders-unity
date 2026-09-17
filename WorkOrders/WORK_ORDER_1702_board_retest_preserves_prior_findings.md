# WO-1702 - a redelivered fix must await a new test without erasing the old finding

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:20:17, build 2026.09.17.373943). PRIOR STATUS: FIXED - root roundtrip tests passed 2026-09-10; awaiting owner board retest. Prior findings remain intact and actionable when renewed. See RESULT. PRIOR STATUS: READY TO IMPLEMENT
**Minted:** 2026-09-10, root CLI during owner-authorized READY clearance; banner 1702 -> 1704 includes WO-1703.

## Captured problem

`Builds/ready-owner-validation-board.log` repeatedly leaves WO-1244 Ready.
Its saved validation is Fail/validated with no timestamp. The current bounce
pass acts on that same mark every time a delivered revision is set Fixed.
The owner now directs all implementations awaiting test-build proof into Fixed.
Deleting the finding or disabling bounce would lose the owner's evidence.

## Scoped correction

Add an explicit per-ticket retest receipt referencing the exact normalized
previous Fail/Needs Work mark by SHA-256, the delivered revision/evidence, and
the reason for retesting. Only a matching acknowledged old finding holds the
new Fixed revision for retest. Any changed owner finding follows normal bounce.
Show the previous finding and awaiting-retest state in the generated board.
Preserve owner-validations bytes; no automatic commit/build-age invalidation.
Malformed receipts must report and leave the original bounce behavior intact.

## Acceptance

- Old acknowledged Fail does not immediately reopen a redelivered Fixed item.
- New Fail/Needs Work reopens; new Pass plus validation closes normally.
- Unrelated tickets and existing historical sign-offs remain unchanged.
- Real round-trip tests include adversarial/malformed receipt cases.
- Board regenerates with `BOARD_CHECK_OK`; result and documentation match code.

## Fence

`tools/board_close_pass.py`, `tools/board_build.py`,
`tools/board_validation_roundtrip_test.py`, `docs/BOARD.md`.
Root owns ticket/status/receipt/RESULT and generated board edits.
