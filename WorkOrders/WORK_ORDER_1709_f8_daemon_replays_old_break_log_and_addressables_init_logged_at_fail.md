# WORK ORDER 1709 - F8 daemon replays an old break-log; Addressables INIT logged at Fail

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-14 by the CLI lead from F8 triage
**Source of truth:** `docs/F8_TRIAGE_2026-09-14.md` clusters A (`:17`) and D-F (`:20-22`, `:24-30`) and
section 4 (`:117-119`). Harness ticket - **the defects are in the evidence supply, not in the game**.

---

## 1. Objective

Two independent harness defects that poison F8 triage: the daemon re-emits already-handled captures from
an old `break-log.jsonl`, and a benign Addressables message is logged at a severity that F8-captures on
every boot.

## 2. Defect A - the daemon replayed nine 2026-09-11 captures on 2026-09-14

**From the triage doc (`:24-30`, `:117-119`):** all nine desktop captures **5031-5039** carry payload
`utc` in **2026-09-11** while the inbox filed them **2026-09-14 02:45-03:06** on a **~150 s cadence**;
**5038 is byte-identical to 5031, 5039 to 5032**; 5037's harvest block is a *live*
`Main_Castle_Overworld` FTUE session unrelated to its `OwnedTown_IronBastion` payload. Doc's conclusion:
*"the daemon re-scanning an old `break-log.jsonl` and re-emitting already-handled rows."*

**Mechanism, proven at source by this lane** in `.claude/skills/run-defenders/f8-watch-daemon.ps1`:
- `:126-127` replay-from-0 on a shrunk log; `:129` replay-the-backlog on a resumed offset.
- `:148` - `$seenKeys = @{}` is initialised **per process**, so a restart's dedupe table is empty and
  previously published rows re-publish - consistent with 5038 == 5031 byte-for-byte.
- **UNPROVEN:** the ~150 s inter-capture cadence is not the `:9`/`:200` 5 s poll
  (`[int]$PollSeconds = 5`) and nothing read this session explains it. Proof: the per-seq timestamps in
  `logs/f8-inbox/QUEUE.jsonl` for 5031-5039 against the publish path.

**The trigger is proven, and it is the `:129` branch, not `:126`.** `logs/f8-inbox/queue-events.log:27298`
reads `2026-09-14T07:42:31.3281424Z [warn] daemon was DOWN for 1733 break-log line(s) (offset 1331 of
3064) - replaying them now, none dropped`; `:27313` repeats it at `2026-09-14T08:01:24.3125872Z`. At
UTC-5 those are **02:42 and 03:01 local** - they BRACKET the doc's 02:45-03:06 filing window. (UTC-5 is
proven for the DEVICE clock, doc `:6-9`; that this PC log shares it is assumed, not read.)

**The real defect is in that same log:** the offset is stuck at **1331** across every replay from
`2026-09-12T02:01:56Z` (line 27251) to `2026-09-14T08:01:24Z` (line 27313) - at least **13 restarts**,
each replaying 1608-1733 lines. **UNPROVEN why:** `Save-BreakOffset` IS called in the loop (`:232`,
after `$breakBase = $cur` at `:231`) and at startup (`:141`), so the symptom is proven and the cause is
not - its write sits in a `try { } catch { }` (`:139`) that swallows failures. Read the state file and
that catch before fixing.

## 3. Defect B - Addressables INIT is logged at `Fail`

**Cluster A = 6 captures** (5011, 5013, 5014, 5017, 5021, 5024 - doc `:17`, `:101`), all `error` kind,
signature `[Flow:StructureAssets] Addressables INIT handle is INVALID after 3.7s`. `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs:1096` is `FlowTrace.Fail(System, ...)`; its
message ends `:1103-1104` with *"The pass now CONTINUES: an invalid handle means the operation is
finished and gone, and the locator enumeration below needs no handle at all."* (`System` =
`"StructureAssets"`, `:123`). A message documenting its own success path must not be an `error`.
Siblings to review: `:909` (Pi prewarm), `EnemyContentWarmer.cs:674`.

**Safety checks by this lane:** `grep -rn "INIT handle is INVALID" Assets/Editor Assets/Data/Tests`
returns **nothing** - no regression pins the `Fail` severity, so the demote breaks no test.
**UNPROVEN:** whether `FlowTrace.Warn` survives to the device log at all - the daemon comment at
`:149-152` records `Fail` was historically *"the only severity that survived to device"*. If Warn does
not reach device the demote deletes evidence. Proof: one device boot with a deliberate `Warn`, grepped
in `adb logcat`.

## 4. Acceptance criteria

1. A daemon restart does **not** re-publish a capture already present in `logs/f8-inbox/QUEUE.jsonl`.
   **Extend the guard that already exists** - `.claude/skills/run-defenders/f8-device-bridge.ps1` has the
   `lastUtc` guard that "suppresses anything already published" (`logs/f8-inbox/queue-events.log:7278`);
   the desktop break-log path has only the per-process `$seenKeys`. One owner, not a second mechanism.
   Proven by stopping and restarting the daemon and showing `pending` unchanged.
2. `Save-BreakOffset` demonstrably advances: after a replay, `queue-events.log` shows a **new** offset,
   not 1331 again. Proven by two consecutive restarts in the log.
3. `StructureContentWarmer.cs:1096` (and `:909`, `EnemyContentWarmer.cs:674` if same shape) becomes
   `FlowTrace.Warn`, **after** the Warn-reaches-device question above is settled and recorded.
4. **Before the demote lands, the WO-1089 confirm is recorded.** Doc `:126-132` reads seq5024 as a
   *candidate confirm* for WO-1089's "awaiting the resident-structures confirm" - the same INIT line
   6.6 s into the 22:31 boot whose screenshots show textured structures
   (`logs/debug/seeker-365962-logcat.txt:566`). The PO closes WO-1089, not this lane; capture the
   evidence in this RESULT before the log line carrying it changes severity.

## 5. What NOT to touch

- **Never strip a FlowTrace call** (CLAUDE.md section 12): severity demote only, the call stays.
- Do not edit anything under `logs/`, ack any capture, or change `$kindSkip` (`:153`) - the
  `session_start|scene_loaded|note|idle` filter is WO-965 canon.
- Do not re-baseline the daemon to "now": `:132-135` exists so a capture made while the daemon was down
  is never silently dropped (WO-965). The fix is **dedupe**, never skipping the backlog.
- No `.cs` gameplay changes beyond the severity keyword; no `Assets/` scene or data edits.


## Replay continued after minting (lead, 2026-09-14)

Eleven more captures, seq 5041-5051, filed 08:12:32Z to 08:42:57Z at a ~3 min cadence, all re-emissions of
already-handled 09-11 rows (OwnedTownRepairService CS0029 x3, injected save failure x2, EMERGENCY pill x1,
OWNED_TOWN_MOVE_PLAY_FAIL x1, `t.GetParent() == nullptr` assertion x4). Acked under this ticket; the fix is
the stuck break-log offset, not any of the payloads.
