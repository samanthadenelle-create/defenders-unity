# WO-1702 result - explicit revision retests

An optional `Retest` receipt identifies the exact normalized previous failure
and names the delivered revision/evidence. A matching old Fail or Needs Work
is held for retest instead of immediately bouncing a Fixed ticket again.
The generated board shows the previous finding and pending retest, excludes it
from current verification counts, and lets the owner confirm the same Fail again
as a fresh timestamped finding. New findings still reopen; validated Pass closes.

Owner-validation records are preserved. Missing, malformed, duplicate or
mismatched receipts do not suppress a failure. No automatic build/commit aging
was introduced; receipts apply to one explicit ticket revision.

Root verification: `Builds/ready-iter2-board-tests.log` contains
`VALIDATION_ROUNDTRIP_OK`, covering the existing ingest/close/bounce/counting
fleet and new redelivery, changed-finding, malformed-receipt and browser-count
cases. The isolated lane first demonstrated repeated bounce before the fix.
Removing receipt matching in the mutation test reproduces that behavior.

WO-1244's receipt names the current console and existing refusal diagnostics
awaiting a production test; it makes no unsupported claim that its unspecified
prior failure was diagnosed or corrected. Owner board retest remains pending.
