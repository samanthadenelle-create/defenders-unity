# WO-1668 RESULT — the locked ARMY tile's CTA is a DISABLED "LOCKED" face

**Status:** IMPLEMENTED - awaiting gate + capture
**Lane:** LOCKED-FACE, isolated worktree, fast-forwarded to `dev` @ `e4b5906a541c65fc12255a109b5dc3723e328488`
**Date:** 2026-09-10
**Not done by this lane, by instruction:** no Unity run, no `COMPILE_GATE_OK`, no UI capture, no commit.

---

## What shipped

Three production files and one oracle. The change is a **presentation** change only — the model's
route is untouched, which is the whole design (CLAUDE.md architecture law: presentation is a
separate layer).

| File | Change |
|---|---|
| `Assets/_Modules/Core/Manage/ManageStateModel.cs` | New `ManageAction.LockedFace` bool + doc. Also re-pointed the class-doc canon example for "Locked Outrider", which asserted CTA `"VIEW BARRACKS"` — with the retired reading kept in place so it is not moved back. |
| `Assets/_Modules/Core/Manage/ManageVmProjection.cs` | `ProjectAction`'s `blocked && Route.IsRoutable` branch gains an early return for `LockedFace`: keeps `Label = action.Cta`, sets `Enabled = false` + `DisabledReasonText = BlockerReason`, leaves `StyleRole` at the blocked default (`Secondary` → `ButtonKind.Quiet`). Method doc updated. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | New `public const string LockedTroopWord = "LOCKED"`. `ComposeTroopItem`'s `!c.Unlocked` arm now feeds that ONE constant to **both** `item.BadgeText` and the Train action's `Cta`, and sets `LockedFace = true`. `Route = ManageRoute.ToBuildCard("barracks", …)` is **unchanged**. |
| `Assets/Editor/Regression/ManageTroopsTrainDoorRegression.cs` | New **case 16** + its dispatch line. |

### Why the route stays

`ManageStateInvariants.cs:163-166` `[lock-without-a-door]` (ruling 18) **FAILS** a
`PrerequisiteBlocked` action carrying `Route.None`. Clearing the route to kill the button would
have traded the owner's ruling for a validator failure. The model still names the destination; the
projection simply declines to offer it as a button. Case 16 pins **both halves** so neither can be
"tidied" into the other.

### Where the disabled look comes from — no hand-tint

`ManageWorkspacePanel.BuildActionRow` (`:2016-2027`) already does
`btn.interactable = face.Enabled;` verbatim, and `ElarionUiKit.cs:1658` sets
`cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f)` on every kit button. **This diff contains no
new `Color(...)` literal and does not touch the renderer.**

### The unlock hint — not invented, not moved

`ManageVmProjection.ProjectSelection:347-349` already promotes a `NotUnlocked` item's `LockReason`
into `AuxiliaryText`. The audited frame proves it is already painted: **"Requires Barracks Tier 4"**
with a padlock glyph. No copy was authored by this WO. Case 16 fails if that band goes empty.

### The consequence, stated on purpose

`ProjectSelection` fills `RequirementAction` from `FirstBlockedWithRoute(item)` **only when it is
not the primary** (`:337-339`). On a locked troop the blocked-with-a-door action **is** the primary,
so `RequirementAction` stays `Hidden` and the card has **no tappable door at all** after this
change. That is what the owner ruled. **Do not add a second button to "restore" it.**

---

## The oracle (case 16), RED-first

`ManageTroopsTrainDoorRegression.CheckLockedTroopCtaIsADisabledLockedFace` drives the **real**
`ManageScreenVM` (`EnterTab(Army)` → `OpenDetail` → `ComposeWorkspace`) — the same idiom as case 8 —
over every `TroopChoices` entry with `!Unlocked`, and asserts:

1. `PrimaryAction.Label == ManageScreenVM.LockedTroopWord` (asserted against the **symbol**, never a
   re-typed literal, so the pin cannot drift from the composer);
2. `PrimaryAction.Enabled == false`, and `Activate == null`;
3. `DisabledReasonText` is non-empty (the button went dead, its explanation did not);
4. `Selection.AuxiliaryText` is non-empty (the unlock hint is still on the screen);
5. at SOURCE: `ManageScreenVM.cs` still contains `LockedFace = true` **and**
   `ManageRoute.ToBuildCard("barracks"` — the door survives in the model;
6. zero locked troops measured is a **FAIL, not a skip**.

**RED argument, written into the case:** before this change the branch set
`vm.Label = action.Route.Cta` and `vm.Enabled = true`, so assertions 1 and 2 both fail on the old
face (label reads `VIEW BARRACKS`, button is pressable). Mutations that red it again are named in
the case doc: delete the `action.LockedFace` early-return, or drop `LockedFace = true` from the
composer.

---

## Finding: the brief's re-point target did not exist

The brief asked to re-point "the `ManageTroopsTrainDoorRegression` / `ManageApprovedLauncherRegression`
pins that assert VIEW BARRACKS". **Neither file contains that string.** Verified:

```
grep -rn "VIEW BARRACKS" Assets/Editor/Regression/
  ManageStateModelRegression.cs:146,163,165,263,264
```

— and those five are **hand-built fixtures** fed to `ManageStateInvariants.Validate`, which never
inspects a projected face. `ManageApprovedLauncherRegression` pins `"BUILD BARRACKS" : title` on the
**WO-1406/1418 launcher card** in `ManageScreenPanel.cs`, a different surface entirely; it was left
untouched.

**So no pin asserted the shipped `VIEW BARRACKS` face at all** — which is precisely why the row-6.3
divergence survived until a screenshot audit found it. This WO therefore **adds the missing pin**
rather than re-pointing an existing one. `ManageStateModelRegression`'s fixtures are deliberately
left as-is: the validator's verdict on them does not change (their `Route` is still routable), and
editing them would add nothing case 16 does not prove harder, on the live VM.

---

## Verification performed by this lane

- `python tools/gate_brace.py` over all four edited files → **`GATE_BRACE_SUMMARY bad=0 of 4`**, exit 0.
- NUL scan over all four → none; raw brace counts balanced (16/16, 30/30, 460/460, 276/276).
- Frame opened at source: `Builds/ui-capture/ManageFlow_ARMY_locked_2670x1200.png` (gold enabled
  `VIEW BARRACKS`, `LOCKED` chip top-right, `Requires Barracks Tier 4` hint already present).
- Every cited line number above was read at source this session.

## Collateral sweep — nothing else observes this face

Four greps, all run this session:

1. `grep -rln "ARMY_locked\|NotUnlocked\|RequirementAction\|StyleRole.Navigate\|IsRoutable" Assets/Editor`
   → `ManageMockupConformanceRegression.cs`, `ManageStateModelRegression.cs`,
   `ManageTroopsTrainDoorRegression.cs` (the last is this WO's own new text). The mockup suite's only
   hit is `:677`, a source pin on `ProjectTile`'s `RequirementText = item.Ownership == NotUnlocked`
   — a TILE field, untouched. **No oracle anywhere asserts the locked troop's CTA is enabled,
   `Navigate`-styled, or reads `VIEW BARRACKS`.** Nothing reds.
2. `grep -n "\.Cta\b" ManageVmProjection.cs` → `:112` (`ProjectAction`), `:138` (the route branch),
   `:280`/`:287` (`ProjectRowAction`, which returns `Hidden` for any non-`Available` action, so a
   locked troop's row never paints a Cta). `ProjectTile` reads no Cta. The locked tile does **not**
   now say LOCKED twice.
3. `grep -rn "trainFace\|TRAIN 1 " Assets/Editor/Regression` → `ManageMockupConformanceRegression.cs:575`
   and `ManageTroopsTrainDoorRegression.cs:1463` both assert the VM/panel SOURCE still contains the
   literal `"TRAIN 1 "`. It does — `string trainFace = "TRAIN 1 " + …` is unchanged; only the locked
   arm stopped consuming it. Neither pin counts occurrences.
4. `grep -c` on `ManageScreenVM.cs`: `ManageRoute.ToBuildCard("barracks"` = **1**,
   `LockedFace = true,` = **1**. Both source assertions in case 16 can therefore actually fail on a
   revert. The `LockedFace` pin deliberately includes the **trailing comma** (the object-initialiser
   shape), so a comment quoting the flag can never satisfy it — the trap
   `ManageApprovedLauncherRegression.cs:77-79` records.

Label fit is not at risk: the face shrinks from `VIEW BARRACKS` (13 chars) to `LOCKED` (6) in the same
band `ManageWorkspacePanel.cs:1570` measured.

## Known stale comment, left for the lead to rule on

`Assets/Editor/Regression/ManageStateModelRegression.cs:146` still reads
`-- "Locked Outrider": not unlocked + Train PrerequisiteBlocked, CTA VIEW BARRACKS.` and its fixture
at `:163`/`:165` still sets that Cta. The fixture's **verdict does not change** (the route is still
routable, so the validator still passes it), but the comment now contradicts the canon example this
WO re-pointed in `ManageStateModel.cs`. Left untouched on purpose — flagged here rather than left for
the next seat to re-derive.

## Still owed (lead)

- `COMPILE_GATE_OK` on the combined tree.
- `REGRESSION_OK <n>/<n>` on a fresh log — case 16 must appear inside `MANAGE_TRAIN_DOOR_OK`.
- A fresh `ManageFlow_ARMY_locked` capture showing the disabled `LOCKED` face, and owner felt-close
  of WO-1566 row 6.3.
