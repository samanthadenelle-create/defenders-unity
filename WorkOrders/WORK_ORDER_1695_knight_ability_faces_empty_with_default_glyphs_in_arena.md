# WORK ORDER 1695 — Knight ability faces read EMPTY and wear the default glyph in the arena

**Status:** FIXED — awaiting lead gate (`COMPILE_GATE_OK` + `REGRESSION_OK`) + owner felt-verify
**Silo:** HUD / combat dock (`Assets/_Modules/HUD/Kit/HudKitController.cs`)
**Opened:** 2026-09-10 · **Lane:** ARENA-FACES SME
**Source:** owner Seeker felt-test, build 363866 — verbatim: *"knight icons in bottom are default not
the icons we created"*

---

## 1. The evidence, quoted

**Frame:** `Builds/device-frames/2026-09-10_1520_owner_icons.png` (2670x1200, Seeker, battle arena,
Knight **"Grom Lv 2"**). Bottom bar, left to right:

| # | caption | glyph as shipped |
|---|---|---|
| 0 | ATTACK | crossed sword + axe on a bronze plate |
| 1 | BLOCK | gold shield |
| 2 | EMPTY | crossed sword + axe |
| 3 | EMPTY | crossed sword + axe |
| 4 | EMPTY | crossed sword + axe |
| 5 | ITEM (badge 6) | red potion |

**The glyph is identified, not guessed.** `Assets/Resources/RpgUi/icons/icon_combat.png` opened at
source is that exact bronze-plate crossed sword+axe, and it is what
`concept-icons.json` names as the catch-all `default` row:

> `"default": { "role": "icons", "name": "icon_combat" }` — `Assets/Resources/Data/Canonical/concept-icons.json`

confirmed on the device itself:

> `[Flow:Icon] concept-icons.json loaded — 93 concept(s) mapped (+default icons/icon_combat)`
> — `Builds/device-frames/2026-09-10_1520_logcat.txt`

⚠ **The lead's premise was off by one face: ITEM is fine.** Face 5 wears a red potion, i.e. its
build-time `UiStyle.Icon("potion", ...)` → `potion/potion_health` resolved. Only ATTACK and the three
EMPTY faces carry the default.

**Every art-availability story is ruled out by the same log:**

> `[Flow:RpgUi] EnsureRole 'abilities' -> LoadAll Resources/RpgUi/abilities`
> `[Flow:RpgUi] role 'abilities' indexed 6 sprite(s)`
> `[Flow:RpgUi] role 'icons' indexed 11 sprite(s)` · `role 'spellicons' indexed 280 sprite(s)`

and there is **no** `[Flow:Icon] miss-art:` line and **no** `RemoteProviderException` / `404` for an
icon anywhere in the 732 k-line capture. The art is on the device and loadable. (Icons ship in
`Resources/`, not through R2 — §16 does not apply to this defect.)

---

## 2. Question 1 — why do the three slots read EMPTY?

**Answered from the log: the loadout resolves perfectly; those three faces are not the class loadout.**

The class Q/W/E/R rail is fully bound for the Knight:

> `[Flow:HudModel] ability bar bound: bar=qwer class='knight' source=HeroAbilities(live) hero='Grom'`
> `ids=[Sword Heroic,Shield Bash,Warden's Grace,Radiant Strike]`
> `sig=Q=Sword Heroic:0/6@0|W=Shield Bash:0/9@3|E=Warden's Grace:0/20@4|R=Radiant Strike:0/40@7`

The dock's faces 2/3/4 are the **hot-swap (assignable) bar**, a different model
(`AssignableLoadoutProducer` → `Model.Assignable`), and the last bind before the arena is empty:

> line **702341**: `ability bar bound: bar=hotswap class='knight' source=GameState(persisted)`
> `hero='Grom' ids=[—,—,—] sig=A0=-:0/0|A1=-:0/0|A2=-:0/0|`
> — and the arena stages at line **719491** (`[Flow:BattleArena] ApplySkyOverride`) / **724666**, with
> no later `bar=hotswap` line in the file.

So: **not** a binder that failed to run (it ran, change-gated, and reported), **not** an unresolved
class loadout. The three slots are genuinely unassigned hot-swap slots.

- **Not level-gated.** `AssignableSkillBar.SlotCount = 3`
  (`Assets/_Modules/Village/Hero/AssignableSkillBar.cs:72`) — a constant, with no level term anywhere
  in the file. Three slots are open at Lv 2 exactly as at Lv 50; the dock draws three regardless.
- ⚠ **RULING NEEDED (not fixed here): the Knight's W/E/R have no face in combat.** Shield Bash,
  Warden's Grace and Radiant Strike are equipped and cooling correctly, but the combat dock is
  `ATTACK(Q) · BLOCK · 3 hot-swap · ITEM`, and in hostile posture the `actionRail` area is EMPTY
  (`Assets/StreamingAssets/Data/Canonical/hud-areas.json:242-245`), so the Q/W/E/R arc does not exist
  either. Three authored abilities are therefore unpressable in the arena. Against memory rule
  *early unlocks put pressable abilities in the bar fast* this looks wrong, but which faces the dock
  carries is an owner ruling, not a lane's call.
- ⚠ **UNPROVEN SECONDARY FINDING, recorded not chased:** the hot-swap bar was **not** always empty
  this session — lines **395657–465404** show `ids=[Mend,—,—]` with a real 12 s cooldown ticking
  (`source=HeroAbilities(live)`). Between there and line 702341 the hero switches to Thrain (mage)
  and back to Grom, and the Knight's bar returns as `[—,—,—]` from `source=GameState(persisted)`. I
  have **not** proven whether the assignment was lost, or merely read from a stale source while the
  live `HeroAbilities` was absent. Worth its own ticket; out of this lane's scope.

---

## 3. Question 2 — why does ATTACK draw the default glyph?

**Answered at source; the failing chain has no unlogged runtime branch left in it.**

1. `AbilityLoadoutProducer` OVERRIDES the Knight's Q icon key with an in-band text token:

   > ```csharp
   > if (equipped && slot == AbilitySlot.Q && def.Id == "knight.q")
   > {
   >     icon = "text:Dodge/\nAttack";
   >     resolvedKey = icon;   // deliberate text face — not an unmapped-icon fallback (F8-33)
   > }
   > ```
   > — `Assets/_Modules/Village/HUD/HudModelProducers.cs:617-621` (the literal at `:619`)
   > (owner placeholder 2026-07-11, commit `977b3737c`: *"instead of the heroic leap image use word
   > Dodge/Attack"*)

2. The **rail** honours that prefix — `Assets/_Modules/HUD/Kit/HudKitController.cs:4228` (HEAD `0b942d0be`):
   `if (!string.IsNullOrEmpty(s.IconKey) && s.IconKey.StartsWith("text:", ...)) h.SetLabel(...)`

3. The **combat dock's primary face did not.** As shipped:
   `primary.SetLabel(null); primary.SetIcon(string.IsNullOrEmpty(q.IconKey) ? null : UiStyle.Icon(q.IconKey));`
   → `UiStyle.Icon("text:Dodge/\nAttack")` is an unmapped concept → `null`.

4. `ActionSlotHandle.SetIcon` then substitutes the never-blank backstop —
   `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:1027`:
   `if (s == null) s = ConceptIconResolver.DefaultSprite();` → **`icons/icon_combat`**.

**The frame itself is the proof step 3 ran.** An *unbound* face would still be wearing its build-time
icon, `UiStyle.Icon("attack", "energy-sword", "sword")` → `abilities/attack_sword`, which is a
distinctly different sprite (a glowing blue-white pixel sword — opened at
`Assets/Resources/RpgUi/abilities/attack_sword.png`). The face shows `icon_combat`, which only the
bind path can produce. And in hostile posture this dock is the **only** combat control, so the
owner's word placeholder has never once rendered where she could see it — it has only ever produced a
wrong glyph.

The same `SetIcon(null)` backstop is why the three EMPTY faces wear it: `OnAssignable`'s unequipped
branch called `h.SetIcon(null)`, while the authored empty treatment (`SetEmptyMedallion` — *"an
unassigned slot is a dimmed '+' plate, not a blank"*, WO-611 + WO-917 Phase B) sits unused 20 lines
above in the same file.

### Why the placeholder was NOT simply deleted (creative pick, owner ruling required)

`git log` settles the order, and it goes the wrong way for a silent removal:

- `ef4b3cde5` **2026-07-06** authored both `Assets/Resources/RpgUi/abilities/charge_knight.png` and
  the `"knight.q": { "role": "abilities", "name": "charge_knight" }` row in `concept-icons.json`.
- `977b3737c` **2026-07-11** added the `text:Dodge/\nAttack` placeholder — i.e. it **knowingly
  overrode already-authored art** at the owner's direction.

So the placeholder's own exit clause (*"Remove this block once the rebound ability ships its own
icon"*) is **not** met by an icon that predates it. Deleting it is a creative decision.

> ⛔ **OWNER RULING NEEDED — one question:** the ATTACK face now renders your 2026-07-11 words
> **"Dodge/Attack"**. Do you instead want the authored **`abilities/charge_knight`** icon there (the
> purple charging-knight medallion), retiring the July word placeholder? Your 2026-09-10 wording
> ("the icons we created") reads that way, but it reverses a directive you gave for this exact face,
> so it is not a lane's call. (Fair caveat: the July directive was written for the **Q medallion** —
> the adaptive combat dock did not exist yet — so this is a re-ask for a new surface, not a reversal
> you already ruled on.) ⚠ **Note what the fix as shipped looks like:** the face will read the words
> **"Dodge/Attack"** with the caption **"ATTACK"** underneath — a visible doubling, and one more
> reason you may want the icon instead. `KnightCombatDockIconRegression` already pins that `knight.q` resolves
> `charge_knight`, so the switch is a one-line producer deletion whenever you say.

---

## 4. What changed

`Assets/_Modules/HUD/Kit/HudKitController.cs`

1. **`ApplyCombatPrimaryFaceArt(face, iconKey)`** (new, private static) — the ONE art rule for the
   primary face: an in-band `text:` key is a text face, anything else is a concept resolved through
   `UiStyle.Icon`. `OnAbilities`'s dock block now calls it, so the rail and the dock can never again
   disagree about what an IconKey means. Adds a `FlowTrace.Once("Icon", "dock-primary-miss:…")` when a
   non-empty key resolves no art — the §12 net this defect went four months without, because the
   substitution is silent by design in both the pixels and the log.
2. **`SetEmptyCombatDockFace(h)`** (new, private static) — an unassigned hot-swap face now gets the
   authored dimmed `+` plate (`SetEmptyMedallion`) instead of `SetIcon(null)` → `icon_combat`, then
   re-applies `SetCaption("EMPTY")` (the hue-free channel that names the state; owner is red/green
   colourblind) and forces `button.interactable = false` **last** — `SetEmptyMedallion` and
   `SetCooldown` both re-enable the button for the rail's toast, and this dock's onClick is
   `HudCommands.AssignableCast(i)`.
3. `OnAssignable`'s equipped branch now clears text mode (`SetLabel(null)`) and un-dims the frame, so
   a slot that becomes assigned leaves the `+` plate cleanly.
4. **`BuildCombatDockProbe` / `ApplyCombatPrimaryFaceArtProbe` / `SetEmptyCombatDockFaceProbe`**
   (public oracle seams) — the documented `BuildPeacefulDockProbe` idiom: no live caller, no state, so
   a rename fails to compile instead of silently turning the oracle off.

⛔ **Deliberately NOT touched:** the `BuildCombatDockSlot(n, "EMPTY", …)` build-time literals
(pinned by `CopyHygieneRegression.cs:92-93`), `HudModelProducers.cs` (the creative ruling above),
`WaveManager` / `BattleArena` lock code (WO-1694), `EchoWorldPresence` / `PetDeployer` (WO-1696), any
`.unity`, `DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`.

`Assets/Editor/Regression/KnightCombatDockIconRegression.cs` (new) — builds the **real** combat dock
and compares sprite references against `ConceptIconResolver.DefaultSprite()`. Marker
`KNIGHT_COMBAT_DOCK_ICON_OK` / `_FAIL`, tag `[knight-combat-dock-icons]`.

**Registration line for the lead** (`Assets/Editor/Regression/DataRegression.cs`, beside `:1239`):

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "knight-combat-dock-icons suite", () => { if (!DeNelle.Editor.Regression.KnightCombatDockIconRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[knight-combat-dock-icons] " + r); });
```

**RED on HEAD — four reverts, each of which the suite actually catches.** ⚠ The helper checks alone
would NOT be RED against a re-inlined `OnAbilities`/`OnAssignable`, so the suite also **reads the
wiring** out of `HudKitController.cs` (precedent: `MageProtectionDockRegression`, and
`CopyHygieneRegression`'s call counting). Verified in the worktree — the pinned strings count
`ApplyCombatPrimaryFaceArt(primary, q.IconKey)` = 1, `SetEmptyCombatDockFace(h)` = 2,
`primary.SetIcon(string.IsNullOrEmpty(q.IconKey)` = 0:

| revert | caught by |
|---|---|
| delete the `text:` branch inside `ApplyCombatPrimaryFaceArt` | `CheckPrimaryTextFace` — no words, default sprite |
| re-inline HEAD's `primary.SetLabel(null); primary.SetIcon(...)` in `OnAbilities` | the wiring read (`ApplyCombatPrimaryFaceArt(primary, q.IconKey)` count ≠ 1, and the old line reappears) |
| put `h.SetIcon(null)` back in either `OnAssignable` unassigned branch | the wiring read (`SetEmptyCombatDockFace(h)` count ≠ 2) |
| break `SetEmptyCombatDockFace`'s `+` plate / interactable order | `CheckEmptyFace` |

---

## 5. Acceptance

- [ ] `COMPILE_GATE_OK` on a fresh log (lead — no Unity in this lane).
- [ ] `[knight-combat-dock-icons] KNIGHT_COMBAT_DOCK_ICON_OK` in a fresh `REGRESSION_OK <n>/<n>` run
      once registered.
- [ ] Owner felt-verify on a device frame: the arena bottom bar's ATTACK face reads **Dodge/Attack**
      (or the ruled icon), and the three unassigned faces read as dimmed `+` plates captioned EMPTY —
      no crossed sword+axe anywhere but a genuinely default concept.
- [ ] Owner rules §3 (word placeholder vs. `charge_knight`) and §2 (do Knight W/E/R get faces in the
      arena?).

## 6. Not proven from here

- Whether the hot-swap `Mend` assignment was **lost** across the hero switch, or merely read from a
  stale source (§2, lines 395657 → 702341). Needs its own capture.
- The visual outcome of this change on a device — no Unity, no build, no capture in this lane.

## 7. Chain 49 fallout — `CLASS_PRIMARY_BLOCK_FAIL`, and why the oracle was RE-POINTED

`Assets/Editor/Regression/ClassPrimaryAndKnightBlockRegression.cs:34` pinned the literal
`primary.SetIcon` by source lint. `ApplyCombatPrimaryFaceArt` moved that write, so the suite red'd:
`CLASS_PRIMARY_BLOCK_FAIL: missing contract: primary.SetIcon`.

**Decision: re-point the row WITH the WO-1695 ruling — not bolt a literal back on.** The contract that
row exists for is *"the class-authored Q reaches the combat dock's primary face as art"*;
`primary.SetIcon` was one **spelling** of it, not the contract itself. Three facts settle it:

1. **The old token could not tell the working case from the defect.** It was **GREEN the whole time**
   the Knight's ATTACK face was painting `icons/icon_combat` on the owner's Seeker (§3). A pin that
   passes through the exact defect the ticket is about is not protecting the contract.
2. **Keeping a literal `primary.SetIcon` at the call site means putting the branch back at the call
   site.** The two IconKey modes write *different members* — `SetLabel` for `text:`, `SetIcon` for a
   concept — so a "resolve in the helper, write at the block" split re-creates the very divergence
   between rail and dock that caused this bug. That is a pin dictating a shape, which is exactly the
   case this file's own WO-1429 block re-pointed for ("*a pin that requires the defect is a pin that
   forbids the fix*"). Same idiom, one ticket later.
3. **The replacement is strictly stronger, not weaker.** `Require(hud, "ApplyCombatPrimaryFaceArt(primary, q.IconKey)")`
   names the call **and** that the class Q's own `q.IconKey` is what is handed to it — so a face wired
   to a constant, to another slot, or to nothing now FAILS where the old token passed. And
   `Forbid(hud, "primary.SetIcon(string.IsNullOrEmpty(q.IconKey)")` re-arms the revert guard the way
   WO-1429 did, so the pre-WO-1695 line cannot come back and still pass.

`Require(hud, "var q = a.Slots[0]")` and `Require(hud, "primary.SetCaption")` are **untouched** — both
still present, count 1 each. Only the one row moved; no other row, file or suite was edited.

Simulated against the working tree (the suite's own `IndexOf`/`Contains` rule):

```
REQ  OK  1 'var q = a.Slots[0]'
REQ  OK  1 'ApplyCombatPrimaryFaceArt(primary, q.IconKey)'
REQ  OK  1 'primary.SetCaption'
FORB OK  0 'primary.SetIcon(string.IsNullOrEmpty(q.IconKey)'
CLASS_PRIMARY_BLOCK HUD rows -> PASS
```

Exact lines changed: `ClassPrimaryAndKnightBlockRegression.cs:34` replaced by the re-point block
(`Require` + `Forbid`) with the reasoning in-code. `GATE_BRACE_SUMMARY bad=0 of 3`; NUL=0 on all three
(`ClassPrimaryAndKnightBlockRegression.cs` 7/7, `HudKitController.cs` 431/431,
`KnightCombatDockIconRegression.cs` 19/19). No commit, no Unity.
