# WO-1838 — Dungeon exit confirm button truncates to "CONTINUE TO EX..."

**Status: DONE**

## Owner report

Screenshot, dungeon "Leave Dungeon?" confirm modal: the confirm button reads "CONTINUE TO EX..."
instead of its full label. Owner, same message: "also continue to exit cut off, just Exit" —
explicit direction to shorten the copy rather than re-fight the truncation/font-floor system.

## Fix (direct, one-line ruling-driven edit)

`Assets/_Modules/Dungeons/DungeonExitInteractable.cs:960` — `confirmLabel: "Continue to exit"` →
`confirmLabel: "Exit"`. Also updated the matching `FlowTrace.Step` debug line at `:982` from
`faces=[Continue to exit | Cancel]` to `faces=[Exit | Cancel]` so the instrumentation stays honest
about what's actually shown. The modal's body message ("Continue to exit returns you to town.
Cancel keeps you in the dungeon.") is unaffected — it was never the truncating element, only the
button label was.

No regression pinned the old string (`grep -rn "Continue to exit" Assets/Editor` = no hits).

## Verification

- `python tools/gate_brace.py Assets/_Modules/Dungeons/DungeonExitInteractable.cs` → `GATE_BRACE_SUMMARY bad=0 of 1`
- NUL-byte scan clean.
- Folded into the session's combined-tree `COMPILE_GATE_OK` + `REGRESSION_OK` pass alongside the
  other 21 concurrent lanes.
