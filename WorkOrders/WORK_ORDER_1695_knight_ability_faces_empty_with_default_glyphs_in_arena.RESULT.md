# WO-1695 RESULT — Knight ability faces: EMPTY x3 explained, default glyph fixed

**Status:** FIXED — awaiting lead gate (`COMPILE_GATE_OK` + `REGRESSION_OK`) + owner felt-verify
**Lane:** ARENA-FACES SME · **Date:** 2026-09-10 · **No Unity run, no commit, no push** (per brief)

---

## Verdict in one line each

- **Q1 (three faces read EMPTY):** the class loadout is **fine** and the binder **ran** — those three
  faces are the *hot-swap* bar, and it was genuinely unassigned at arena time. **Not a bug.** The real
  gap is that the Knight's authored W/E/R have **no pressable face anywhere in combat**, which is an
  owner ruling, surfaced not decided.
- **Q2 (default glyph):** **a real bug, fixed.** The combat dock's primary face silently dropped the
  in-band `text:` IconKey mode the rail honours, so the owner's Knight placeholder resolved no concept
  and `SetIcon`'s never-blank backstop painted `icons/icon_combat`. The three EMPTY faces hit the same
  backstop via `SetIcon(null)`.

## Evidence, file:line

| Claim | Proving line |
|---|---|
| The glyph IS the concept default | `Assets/Resources/RpgUi/icons/icon_combat.png` opened — the bronze crossed sword+axe in the frame; `[Flow:Icon] concept-icons.json loaded — 93 concept(s) mapped (+default icons/icon_combat)` (`Builds/device-frames/2026-09-10_1520_logcat.txt`) |
| Icon art IS on the device | `[Flow:RpgUi] role 'abilities' indexed 6 sprite(s)`, `role 'icons' indexed 11`, `role 'spellicons' indexed 280`; zero `[Flow:Icon] miss-art:` and zero icon `404`/`RemoteProviderException` in 732 k lines |
| Class loadout resolves | `ability bar bound: bar=qwer class='knight' source=HeroAbilities(live) hero='Grom' ids=[Sword Heroic,Shield Bash,Warden's Grace,Radiant Strike]` |
| Hot-swap bar empty at arena time | logcat line **702341** `bar=hotswap … ids=[—,—,—]`; arena stages at **719491** / **724666** (`[Flow:BattleArena] ApplySkyOverride`); no later hotswap bind in the file |
| Slots are not level-gated | `AssignableSkillBar.SlotCount = 3` — `Assets/_Modules/Village/Hero/AssignableSkillBar.cs:72` |
| The producer forces a text key for knight.q | `Assets/_Modules/Village/HUD/HudModelProducers.cs:617-621` (`icon = "text:Dodge/\nAttack";`) |
| The rail honours it, the dock did not | rail `Assets/_Modules/HUD/Kit/HudKitController.cs:4228` (HEAD `0b942d0be`) vs. the dock's shipped `SetLabel(null); SetIcon(UiStyle.Icon(q.IconKey));` |
| The backstop that paints the default | `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:1027` — `if (s == null) s = ConceptIconResolver.DefaultSprite();` |
| The face WAS bound (not skipped) | the frame shows `icon_combat`, not the build-time `abilities/attack_sword` (a glowing blue-white sword, opened at source) — only the bind path produces the default here |
| Removing the placeholder is a creative pick | `ef4b3cde5` 2026-07-06 authored `charge_knight.png` **and** the `knight.q` concept row; `977b3737c` 2026-07-11 added the word placeholder **over** it |

## Explicitly NOT proven

- Whether the hot-swap `Mend` assignment (logcat **395657–465404**, `ids=[Mend,—,—]`, live 12 s
  cooldown) was **lost** across the Thrain↔Grom hero switch or merely read from a stale
  `GameState(persisted)` source by **702341**. Recorded as a secondary finding; needs its own capture.
- The on-device appearance of this fix. No Unity, no build, no capture ran in this lane.
- Whether the owner wants words or `charge_knight` on the ATTACK face — asked, not assumed.

## Files

**Edited**
- `Assets/Editor/Regression/ClassPrimaryAndKnightBlockRegression.cs` (chain 49 re-point at `:34` - see the Chain 49 section below; repo-relative per CLAUDE.md sec.0)
- `D:\EoA\.claude\worktrees\agent-a9119d7f1bb8d4e3e\Assets\_Modules\HUD\Kit\HudKitController.cs`

**Added**
- `D:\EoA\.claude\worktrees\agent-a9119d7f1bb8d4e3e\Assets\Editor\Regression\KnightCombatDockIconRegression.cs`
- `D:\EoA\.claude\worktrees\agent-a9119d7f1bb8d4e3e\WorkOrders\WORK_ORDER_1695_knight_ability_faces_empty_with_default_glyphs_in_arena.md`
- `D:\EoA\.claude\worktrees\agent-a9119d7f1bb8d4e3e\WorkOrders\WORK_ORDER_1695_knight_ability_faces_empty_with_default_glyphs_in_arena.RESULT.md`

## Registration line for the lead — `Assets/Editor/Regression/DataRegression.cs` (beside `:1239`)

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "knight-combat-dock-icons suite", () => { if (!DeNelle.Editor.Regression.KnightCombatDockIconRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[knight-combat-dock-icons] " + r); });
```

## RED-on-HEAD proof — four reverts, each caught

⚠ The probe-seam checks alone would NOT be RED against a re-inlined `OnAbilities` / `OnAssignable`,
so the suite ALSO reads the wiring out of `HudKitController.cs` (precedent:
`MageProtectionDockRegression` reads the same file; `CopyHygieneRegression` counts call sites).
Counts verified in the worktree: `ApplyCombatPrimaryFaceArt(primary, q.IconKey)` = 1,
`SetEmptyCombatDockFace(h)` = 2, `primary.SetIcon(string.IsNullOrEmpty(q.IconKey)` = 0.

| revert | caught by |
|---|---|
| delete the `text:` branch inside `ApplyCombatPrimaryFaceArt` | `CheckPrimaryTextFace` |
| re-inline HEAD's `primary.SetLabel(null); primary.SetIcon(...)` in `OnAbilities` | the wiring read |
| put `h.SetIcon(null)` back in either `OnAssignable` unassigned branch | the wiring read |
| break the `+` plate / interactable order in `SetEmptyCombatDockFace` | `CheckEmptyFace` |

## Kit behaviours READ at source before the suite leaned on them (not assumed)

- `ActionSlotHandle.SetLabel` activates `label.gameObject`, assigns the text **verbatim** (no
  uppercase/transform) and sets `icon.enabled = false` — `ElarionUiKitObsidian.cs:1040-1068`.
- `SetCaption(null)` deactivates the strip and clears `_shownCaption`; a later `SetCaption("EMPTY")`
  re-activates it and re-assigns the text — same file, `:1095-1123`.
- `SetCooldown` returns early when `cdRing == null` and otherwise ends `button.interactable = !cooling`
  — which is exactly why `SetEmptyCombatDockFace` forces `interactable = false` **last**.
- `HudBlockPressRelay` (added to face 1 by the builder) has **no** `Awake`/`Start`/`OnEnable` and no
  scene lookups — `Assets/_Modules/HUD/Kit/HudBlockPressRelay.cs` — so the headless probe cannot throw
  on it.
- ⛔ `CopyHygieneRegression.cs:95` pins the literal
  `h.SetCaption(equipped && !string.IsNullOrWhiteSpace(s.Name) ? s.Name : "EMPTY")`. My first draft
  had dropped the `equipped &&` term and would have turned that suite RED; the line is restored
  verbatim with an in-code note saying why it may not be simplified.

## Gate evidence from this lane

```
GATE_BRACE_SUMMARY bad=0 of 2            (tools/gate_brace.py — the gate's own scanner rule)
HudKitController.cs                NUL=0  raw braces 430/430
KnightCombatDockIconRegression.cs  NUL=0  raw braces 15/15
```

No Unity gate run (out of lane scope). Nothing committed, nothing pushed.

## Owner rulings requested

1. **ATTACK face:** keep the 2026-07-11 words *"Dodge/Attack"*, or switch to the authored
   `abilities/charge_knight` icon? (One-line producer deletion either way; the regression already pins
   that the icon resolves.)
2. **Knight W/E/R in combat:** Shield Bash / Warden's Grace / Radiant Strike are equipped and cooling
   but have no face in the arena — the dock is `ATTACK · BLOCK · 3 hot-swap · ITEM` and the Q/W/E/R
   rail is empty in hostile posture (`hud-areas.json:242-245`). Should they get faces?

---

## Chain 49 — `CLASS_PRIMARY_BLOCK_FAIL: missing contract: primary.SetIcon`

`ClassPrimaryAndKnightBlockRegression.cs:34` lint-pinned the literal `primary.SetIcon`;
`ApplyCombatPrimaryFaceArt` moved that write. **Re-pointed WITH the ruling, not worked around** —
full reasoning in §7 of the WO. In short: the row's contract is "the class Q reaches the primary face
as art", and the old literal was **green throughout the defect**; keeping it would force the
`text:`-vs-concept branch back to the call site and re-create the rail/dock divergence WO-1695 closed.
This is the same re-point idiom the file already records for WO-1429 at `:15-26`.

- `Require(hud, "primary.SetIcon")` → `Require(hud, "ApplyCombatPrimaryFaceArt(primary, q.IconKey)")`
  — strictly stronger: it also pins that the class Q's own IconKey is the argument.
- `+ Forbid(hud, "primary.SetIcon(string.IsNullOrEmpty(q.IconKey)")` — revert guard, WO-1429 idiom.
- `var q = a.Slots[0]` and `primary.SetCaption` rows **untouched** (present, count 1 each).

Simulated against the tree with the suite's own `IndexOf`/`Contains` rule:

```
REQ  OK  1 'var q = a.Slots[0]'
REQ  OK  1 'ApplyCombatPrimaryFaceArt(primary, q.IconKey)'
REQ  OK  1 'primary.SetCaption'
FORB OK  0 'primary.SetIcon(string.IsNullOrEmpty(q.IconKey)'
CLASS_PRIMARY_BLOCK HUD rows -> PASS
```

⚠ Simulation is not the suite: the other rows of that suite (bridge / HeroAnimatorFactory /
TutorialSkipUi / ElarionUiKit) were **not** re-run here and are untouched by this lane. The marker on
a fresh log is the verdict.

`GATE_BRACE_SUMMARY bad=0 of 3` · NUL=0 · braces 7/7, 431/431, 19/19. No commit, no Unity, no push.
