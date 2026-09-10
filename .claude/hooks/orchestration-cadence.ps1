# orchestration-cadence.ps1 -- the 5-minute "come back on track" reminder (owner directive 2026-09-09).
#
# WHY: lane-check.ps1 fires at TURN START. The drift the owner sees happens MID-TURN, twenty tool
# calls in, when the seat has started typing a fix itself. So this hook fires after tool use and
# re-injects the cadence rule at most once per CADENCE_MINUTES of wall clock, from ONE text file
# (.claude/hooks/ORCHESTRATION_CADENCE.md) so the rule is never copied into a second place.
#
# Wired on PostToolUse (all tools). Silent (exit 0, no output) until the interval has elapsed.
# State: a stamp file under logs/ (gitignored). Deleting it forces the next tool call to remind.
#
# Also wired on SubagentStop with the -LaneDone switch: a lane finishing is exactly the moment the
# board flip and the next top-up are owed, so that path always emits regardless of the timer.

param([switch]$LaneDone)

$ErrorActionPreference = 'SilentlyContinue'
$CadenceMinutes = 5
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$RuleFile = Join-Path $PSScriptRoot 'ORCHESTRATION_CADENCE.md'
$StampDir = Join-Path $RepoRoot 'logs'
$Stamp    = Join-Path $StampDir 'orchestration-cadence.stamp'

if (-not (Test-Path $RuleFile)) { exit 0 }
if (-not (Test-Path $StampDir)) { New-Item -ItemType Directory -Path $StampDir -Force | Out-Null }

# 2026-09-09 21:0x: a lane reported "tenth identical fire" - the SubagentStop wiring re-prompted
# the SUBAGENT on every stop attempt (the event runs in the subagent's context, not the lead's).
# That event is UNWIRED in settings.json; -LaneDone stays for a manual invocation only. Hooks are
# session-wide, so PostToolUse can also fire inside a lane - the shared stamp caps that at one
# reminder per interval across all of them, which is acceptable. NOT PROVEN which context each
# fire lands in; measure before changing the cadence again.

$now = Get-Date
$due = $true
if (-not $LaneDone -and (Test-Path $Stamp)) {
    $last = (Get-Item $Stamp).LastWriteTime
    if (($now - $last).TotalMinutes -lt $CadenceMinutes) { $due = $false }
}
if (-not $due) { exit 0 }

Set-Content -Path $Stamp -Value $now.ToString('o') -Encoding ascii

$rule = (Get-Content -Path $RuleFile -Raw).Trim()
$head = if ($LaneDone) { 'LANE COMPLETED -- board flip + top-up are owed NOW. ' } else { "" }
$eventName = if ($LaneDone) { 'SubagentStop' } else { 'PostToolUse' }

@{ hookSpecificOutput = @{ hookEventName = $eventName; additionalContext = ($head + $rule) } } |
    ConvertTo-Json -Compress -Depth 4
exit 0
