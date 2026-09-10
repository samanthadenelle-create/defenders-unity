# WORK ORDER 1668 — the locked ARMY tile's CTA is a DISABLED "LOCKED" face

**Status:** IMPLEMENTED - awaiting gate + capture (see `WORK_ORDER_1668_locked_army_tile_cta_is_a_disabled_locked_face.RESULT.md`)
**Minted:** 2026-09-10 (number PRE-ASSIGNED by the lead; `CLI_LANES_WO_NUMBERS.md` is NOT edited by this lane)
**Lane:** LOCKED-FACE (isolated worktree off `dev` @ `e4b5906a5`)
**Silo:** `Assets/_Modules/Core/Manage/*` + `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` + one regression case

---

## 1. The ruling

Owner, 2026-09-10 12:12, closing **WO-1566 audit row 6.3**:

> the locked ARMY tile's CTA must be a **DISABLED "LOCKED" face**, not "VIEW BARRACKS".

Row 6.3 had been recorded as a FAIL *awaiting an owner ruling* — the auditor explicitly
refused to make the call itself. The ruling makes the row's original wording canon.

---

## 2. The evidence (all re-read at source this session, CLAUDE.md §11B)

### 2.1 The audit row

`WorkOrders/WORK_ORDER_1566_manage_conformance_spec_end_of_session_definition_of_done.RESULT.md:188`

> | 6.3 the action button reads **`LOCKED`** and is visibly disabled | **FAIL (spec divergence -
> likely INTENDED, needs an owner ruling)** | Was NM. `ManageFlow_ARMY_locked_2670x1200.png`: the
> CTA reads **`VIEW BARRACKS`**, a live enabled route - not a disabled `LOCKED`. The word `LOCKED`
> IS present, but as the **state chip top-right**. … **Recorded as a FAIL against the row AS
> WRITTEN; the call is the owner's, not mine.** |

Restated in that RESULT's open-questions table at `:395` ("**OWNER RULING** … One word from the
owner closes it either way") and counted in the panel-6 tally at `:417` / the FAIL roll-up at
`:431` and `:447`.

### 2.2 The frame

`Builds/ui-capture/ManageFlow_ARMY_locked_2670x1200.png` — opened this session. OUTRIDER detail
card:

* state chip top-right reads **`LOCKED`**;
* hint row reads **`Requires Barracks Tier 4`** with a padlock glyph — **the unlock hint already
  exists and is already in the right band**;
* the bottom CTA is a full-width **GOLD, ENABLED** `VIEW BARRACKS` face.

### 2.3 The producers (three files, one chain)

1. **Composer** — `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:4785-4801`
   (`ComposeTroopItem`, the `if (!c.Unlocked)` arm). It sets `item.Badge = Locked`,
   `item.BadgeText = "LOCKED"`, `item.LockReason = c.Requirement`, and adds a **Train** action:
   `Availability = PrerequisiteBlocked`, `Cta = trainFace` ("TRAIN 1 OUTRIDER"),
   `Route = ManageRoute.ToBuildCard("barracks", "VIEW BARRACKS")`, `IsPrimary = true`.

2. **Projection — this is where the word and the enablement are actually decided.**
   `Assets/_Modules/Core/Manage/ManageVmProjection.cs:117-133` (`ProjectAction`):

   ```
   if (blocked && action.Route.IsRoutable)
   {
       vm.Label = action.Route.Cta;          // "TRAIN 1 OUTRIDER"  ->  "VIEW BARRACKS"
       vm.StyleRole = ManageActionStyleRole.Navigate;
       if (navigate != null) { vm.Enabled = true; vm.Activate = () => navigate(route); … }
   ```

   So the composer's `Cta` is **discarded** on this arm and the face becomes an enabled door.

3. **Renderer** — `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs:2016-2027`
   (`BuildActionRow`): `ElarionUiKit.Button(band, face.Label, KindFor(face.StyleRole), …)` then
   `btn.interactable = face.Enabled;`. The kit already owns the disabled look —
   `ElarionUiKit.cs:1658` `cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f)`. **No colour is
   hand-tinted by this WO.**

### 2.4 The constraint that shapes the fix

`Assets/_Modules/Core/Manage/ManageStateInvariants.cs:163-166` — **`[lock-without-a-door]`**:
a `PrerequisiteBlocked` action with `Route.None` is a validator FAILURE (ruling 18). So the
**MODEL keeps its door**; only the **PRESENTATION** changes. That is HP B2B §0 architecture law —
presentation is a separate layer.

Also checked and clear:
* `ManageStateInvariants.cs:188-193` — the `NotUnlocked` contradiction check fires only on
  `Available`, never on `PrerequisiteBlocked`. Unchanged by this WO.
* The `"LOCKED - TAP"` suffix (`ManageScreenVM.cs:5155-5157`) is **RESEARCH-only** and keyed on
  `c.BuildingId`; no troop path reads it.
* `ManageDumbViewRegression` is a source lint over `ManageWorkspacePanel.cs`, which this WO does
  not edit.

---

## 3. What to build

1. **`ManageAction.LockedFace`** (new `bool`, `Assets/_Modules/Core/Manage/ManageStateModel.cs`).
   Documented as: *the door stays in the model so `[lock-without-a-door]` holds; the CTA renders as
   a disabled face carrying the model's own word.*

2. **`ManageScreenVM.ComposeTroopItem`**, locked arm only: the action's `Cta` becomes the same
   word the badge already carries (one local constant feeding BOTH `item.BadgeText` and the
   action's `Cta` — never a second literal), and `LockedFace = true`. `Route` is **UNCHANGED**.

3. **`ManageVmProjection.ProjectAction`**: inside the existing `blocked && Route.IsRoutable`
   branch, when `action.LockedFace` is set, keep `Label = action.Cta`, `Enabled = false`,
   `DisabledReasonText = action.BlockerReason`, and leave `StyleRole` at the blocked default
   (`Secondary` -> `ButtonKind.Quiet`) — the same face treatment the ARMY-FULL arm already gets.

4. **The unlock hint is NOT touched and NO copy is invented.** `ProjectSelection`
   (`ManageVmProjection.cs:347-349`) already promotes `item.LockReason` into `AuxiliaryText` for a
   `NotUnlocked` item; the frame proves it is already painted ("Requires Barracks Tier 4").

5. **Regression, RED-first** — a new case in
   `Assets/Editor/Regression/ManageTroopsTrainDoorRegression.cs`, driving the real
   `ManageScreenVM` -> `OpenDetail` -> `ComposeWorkspace` path (the same idiom as case 8) over the
   fixture's LOCKED troops, asserting the projected primary face reads the model's locked word, is
   `Enabled == false`, carries a `DisabledReasonText`, has an `AuxiliaryText` hint, and that the
   model action's `Route.IsRoutable` is **still true**.
   *RED proof to state in the case:* before the change this branch set
   `Label = Route.Cta` ("VIEW BARRACKS") and `Enabled = true`, so both the word assertion and the
   enablement assertion fail on the old face.

### Consequence, stated so nobody "fixes" it later

`ProjectSelection` fills `RequirementAction` from `FirstBlockedWithRoute(item)` **only when it is
not the primary** (`ManageVmProjection.cs:337-339`). On a locked troop the blocked-with-a-door
action **IS** the primary, so `RequirementAction` stays `Hidden` and the detail card has **no
tappable door at all** after this change. That is exactly what the ruling asks for. **Do not add a
second button to "restore" the door** — the door survives in the model for the validator, and the
hint band is the guidance.

---

## 4. Acceptance criteria

- [ ] A locked troop's detail CTA reads the locked word (`LOCKED`) — never `VIEW BARRACKS`.
- [ ] That face is `interactable = false` via `ManageActionVM.Enabled`, painted by the kit's own
      `disabledColor`; **no new `Color(...)` literal anywhere in this diff.**
- [ ] The unlock hint ("Requires Barracks Tier …") still reaches the hint band, unchanged, with no
      new copy authored.
- [ ] `ManageStateInvariants.Validate` still passes the locked-troop shape (`Route` stays routable,
      `[lock-without-a-door]` never fires).
- [ ] The new regression case is registered where the suite already dispatches its cases and reds
      on the pre-change face (word AND enablement).
- [ ] Unlocked troops are untouched: TRAIN / UPGRADE faces, army-full band, queue-full band all
      behave exactly as before.
- [ ] `python tools/gate_brace.py` clean + no NUL bytes on every edited `.cs`.

## 5. What NOT to touch

- `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` — the **WO-1418 / WO-1406 launcher card**
  and its `"BUILD BARRACKS" : title` door. Different surface, different owner ruling.
- `Assets/Editor/Regression/ManageApprovedLauncherRegression.cs` — it pins **`BUILD BARRACKS`** on
  that launcher card and asserts nothing about the workspace troop CTA. Untouched.
- `Assets/_Modules/Core/Manage/ManageStateInvariants.cs` — the invariant is the reason the model
  keeps its route; it needs no change.
- `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs` — the renderer already honours
  `face.Enabled`; touching it would put a lock decision in the View (canon 9).
- `CLI_LANES_WO_NUMBERS.md` — the number was pre-assigned by the lead.
- No Unity run, no gate, no commit from this lane.

## 6. Finding for the lead (brief correction)

The brief named "the `ManageTroopsTrainDoorRegression` / `ManageApprovedLauncherRegression` pins
that assert VIEW BARRACKS". **Neither file contains that string.** Verified:
`grep -rn "VIEW BARRACKS" Assets/Editor/Regression/` returns hits **only** in
`ManageStateModelRegression.cs` (`:146`, `:163`, `:165`, `:263`, `:264`) — and those are
**hand-built fixtures** feeding `ManageStateInvariants.Validate`, which never inspects the
projected face. **No pin asserted the shipped `VIEW BARRACKS` face at all**, which is why the
divergence in row 6.3 survived to a screenshot audit. This WO therefore ADDS the missing pin
rather than re-pointing an existing one. `ManageStateModelRegression`'s fixtures are left as-is on
purpose: the validator's verdict on them does not change (their `Route` is still routable), and
editing them would add nothing the new live-VM case does not prove harder.
