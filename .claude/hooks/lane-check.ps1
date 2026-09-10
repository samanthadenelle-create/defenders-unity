# lane-check.ps1 -- turn-start ORCHESTRATION check (owner directive 2026-08-26).
#
# WHY THIS EXISTS, and why it is a HOOK and not a line in CLAUDE.md:
#   CLAUDE.md section 11 already says "orchestrate, don't solo" and "the pipeline never idles".
#   The seat read it at boot and then hand-implemented work anyway, repeatedly, on the same day.
#   Owner, 2026-08-26, verbatim: "i tell you this 20 times a day, then you apologize state the
#   lost time, say your recording to canon so it doesnt happen and you own it, then do the same
#   thing a few hours later" -> "how do i implement this as law you must follow".
#
#   A rule that depends on the seat REMEMBERING it competes with ~500KB of other prose and loses.
#   This repo already learned that once and wrote it down (CLAUDE.md section 14): the F8 discipline
#   "stopped being followed within a month -- the harness now executes it instead of trusting the
#   seat to." Same remedy here. This fires without the seat's cooperation, every turn.
#
#   THE SECOND PROPERTY IS THE LOAD-BEARING ONE: the owner sees this line too. An unassigned
#   READY queue is now OBSERVABLE to her at the moment it happens, instead of something she has
#   to notice by watching what the seat DIDN'T do. That is what turns a promise into a check.
#
# Deliberately TERSE: this runs on EVERY prompt. A wall of text every turn would be tuned out,
# which is the failure mode it exists to prevent. Count + the three oldest + the rule.
# Silent (exit 0, no output) when the READY queue is empty -- nothing to say, so say nothing.

$ErrorActionPreference = 'SilentlyContinue'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$WoDir    = Join-Path $RepoRoot 'WorkOrders'

if (-not (Test-Path $WoDir)) { exit 0 }

# A RESULT file means the work is done; the board's Done bucket keys off it. Exclude them, or a
# finished ticket whose spec still says READY reads as outstanding forever.
#
# !! PERF: this runs on EVERY prompt, so it is ONE Select-String pass over the whole glob, not one
# call per file. The per-file loop this replaced took 9.1s against ~570 work orders -- a per-turn
# tax that big would earn the hook a disable, which is the same outcome as not having it.
#
# !! COUNTING AUTHORITY: `python tools/board_build.py` -> BOARD.html is the authority on bucket
# counts; it applies nuances this cheap scan does not (partials, supersedes, RESULT pairing), so
# the two numbers can differ. That is fine and deliberate -- this hook exists to make the queue
# VISIBLE, not to be the ledger. Read the board before quoting a number to the owner.
# 2026-09-09: match the FIRST `**Status:**` line per file and test THAT for READY. The old pattern
# matched ANY line starting `**Status:** READY`, so a CLOSED ticket whose body quotes an old status
# (WO-1356:120) counted as READY and a lane was dispatched at a closed ticket. The board is the
# ledger; this must at least read the same line the board reads.
$ready = @(
    Select-String -Path (Join-Path $WoDir 'WORK_ORDER_*.md') `
                  -Pattern '^\*\*Status:\*\*' -List -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -notlike '*.RESULT.md' -and $_.Line -match '^\*\*Status:\*\*\s*READY' } |
        ForEach-Object {
            [pscustomobject]@{
                Name = [IO.Path]::GetFileNameWithoutExtension($_.Path)
                Age  = (Get-Item $_.Path).LastWriteTime
            }
        }
)

if ($ready.Count -eq 0) { exit 0 }

# Oldest first: a ticket that has sat longest is the one most likely to have been forgotten.
$oldest = @($ready | Sort-Object Age | Select-Object -First 3 | ForEach-Object {
    $_.Name -replace '^WORK_ORDER_', '' -replace '_', ' '
})

$ctx = "ORCHESTRATION CHECK (CLAUDE.md section 11, hook-enforced): $($ready.Count) READY work order(s) are on the board. " +
       "Oldest: " + ($oldest -join ' | ') + ". " +
       "BEFORE hand-implementing anything yourself, assign READY work to file-disjoint agents -- your hands stay on GATE and COMMIT. " +
       "The pipeline never idles: on any lane completion, top up with the next disjoint READY ticket. " +
       "If you are about to edit code that a READY ticket covers, that is the violation this check exists to catch. " +
       "Legitimate exceptions (say which, out loud, if you take one): a one-line ruling-driven edit, an oracle re-point that must move WITH a ruling, a gate/commit step, or work the owner explicitly asked you to do yourself."

@{ hookSpecificOutput = @{ hookEventName = 'UserPromptSubmit'; additionalContext = $ctx } } | ConvertTo-Json -Compress -Depth 4
exit 0
