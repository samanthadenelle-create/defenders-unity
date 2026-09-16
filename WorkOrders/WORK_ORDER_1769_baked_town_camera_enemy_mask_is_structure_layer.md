# WORK ORDER 1769 — The baked `_enemyMask: 256` is the STRUCTURE layer, in two scenes

**Status:** BLOCKED - owner-felt ruling required: changes town framing; ships only together with globalising IsFramingSubject

**Minted:** 2026-09-16 by the lead from WO-1765 §17.1

**Silo:** camera / `.unity` scene edits

---

## Symptom / Evidence

The town camera's combat zoom and auto-framing have only ever been driven by masonry — no mob can enter the scan.

**Evidence:** `ProjectSettings/TagManager.asset` (read 2026-09-16): layers are `0 Default, 1 TransparentFX, 2 Ignore Raycast, 3 Tower, 4 Water, 5 UI, 6 Building, 7 Enemy, 8 Structure`. So `m_Bits: 256` = `1 << 8` = **Structure**, not Enemy. It is serialized on the camera in `Assets/Scenes/Main_Castle_Overworld.unity:3034-3036` and `Assets/Scenes/Village2.unity:3387-3389` (a second Main_Castle_Overworld instance at `:22972-22974` carries `4294967295` = `~0`).

---

## Files to edit

- `Assets/Scenes/Main_Castle_Overworld.unity:3034-3036` — camera `_enemyMask` m_Bits field
- `Assets/Scenes/Village2.unity:3387-3389` — camera `_enemyMask` m_Bits field

---

## What NOT to touch

- No code edits. `.unity` hand-edits forbidden (CLAUDE.md §3); baked scene values change only through a builder/batchmode step.
- SmartMobileCamera.cs raid branch shipped by WO-1765; do not change the runtime narrowing.

---

## Acceptance criteria

1. The correct value (Enemy layer = `128`) is wired into both scenes.
2. The change ships **together** with globalising `IsFramingSubject` (the pair in WO-1765 §11.6), because each alone changes town framing in the opposite direction.
3. Owner felt-test: town hub wave, no camera zoom change vs pre-WO-1765 behaviour. PO closes (CLAUDE.md §13).

---

## Source

WO-1765 RESULT §12.1 and WO §17.1
