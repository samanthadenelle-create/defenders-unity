# WO-1531: the F8 device bridge dedupes on message text forever - 319 captures published 0, including the owner's FLAG

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Tooling/F8 - `.claude/skills/run-defenders/f8-device-bridge.ps1`, `Get-EntryKey`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1531 -> 1532 in the same edit). Found by the WO-1460 lane, which landed
the heartbeat and SURFACED this without fixing it.

## 1. EVIDENCE

Both producers were alive all day - this is not the daemon dying:

```
daemon.pid 13472    device-bridge.pid 2196
device-state.json   updatedUtc 2026-09-07T01:38:59Z
```

Yet publishing stopped twelve hours earlier:

```
logs/f8-inbox/queue-events.log   last publish 2026-09-06T13:42:52Z seq=4685
```

while the staged device log grew to 11,359 lines and ended with the owner pressing the button:

```
{"kind":"flagged","message":"[Main_Castle_Overworld] on-screen FLAG button (mobile)",
 "utc":"2026-09-07T01:18:13.128Z"}
```

Replaying the filter over that log against the live watermark:

```
319 entries newer than the watermark   ->   319 suppressed by the seen-hash
(316 error, 2 possible_softlock, 1 flagged)   ->   0 published
```

`Get-EntryKey` is `kind + first 200 chars of message`. No session boundary, no time component. So the FIRST
occurrence of any message permanently poisons every later one - across sessions, across days, forever.

**The owner pressed FLAG and no seat was told.** That is the exact failure CLAUDE.md sec.14 exists to prevent,
and it is worse than WO-1460's silent daemon: here the harness was running, saw the flag, and discarded it.

## 2. FIX SHAPE

- Key = `kind + message + session id` (from `session_start`) **+ a time bucket** (e.g. 10 minutes). A repeat
  inside one session in one bucket is deduped; a new session, or a later press, publishes.
- **`flagged` is NEVER deduped.** An owner flag is an EVENT, not a message - two identical presses are two
  facts. This is the load-bearing half.
- **Backfill:** replay today's staged log through the new key and publish the **1 flagged + the 2
  possible_softlocks**, NOT the 316 errors. Stated as a deliberate choice: the errors are one repeated
  condition already covered by WO-1450 and WO-1451, and republishing 316 of them would bury the flag that
  matters - the same burial this ticket is fixing.

## 3. WHAT NOT TO DO
- Do not remove dedupe entirely. The 316 errors are why it exists (the WO-1450 storm), and an unfiltered
  bridge would flood the inbox and hide flags just as effectively.
- Do not ack the backfilled captures on the seat's behalf. The queue is a queue (WO-965): triage each, ack one
  at a time.

## 4. ACCEPTANCE
- [x] `Get-EntryKey` includes session id and a time bucket; `flagged` bypasses dedupe entirely.
- [x] Bridge self-test case: **two FLAG presses ten minutes apart both publish**. RED today.
- [x] Bridge self-test case: the same error repeated 300 times within one bucket publishes once (the success
      path AND the refusal, memory `prove-the-success-path-not-just-the-refusal`).
- [ ] The backfill run publishes exactly the 1 flagged + 2 softlocks; the count is pasted in the RESULT.
      **STILL OPEN** - the live watermark has already advanced past those entries (device-state lastUtc
      2026-09-07T05:44:05Z), so this is now a deliberate scoped replay for the lead, not an automatic
      consequence of the fix. A fixture replay of the real log over the same tail is in the RESULT.
- [ ] Cross-referenced in WO-1460 (heartbeat landed there; this is the other half of that silence).
      **STILL OPEN** - WO-1460 is outside this edit-only lane's file list.
