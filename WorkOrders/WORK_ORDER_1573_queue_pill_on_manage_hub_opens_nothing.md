# WO-1573: QUEUE pill on Manage hub opens nothing

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:46, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT
**Silo:** HUD - `ManageScreenPanel` + `ManageQueueDrawerRegression`.
**Source:** Manage pass-three lane handback 2026-09-07. Minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1573 -> 1574 in the same edit).

## 1. EVIDENCE (re-read at source 2026-09-07)

- The Manage hub renders a QUEUE pill (`ManageScreenPanel.ApplyScreenVisibility`, drawer
  initialized as a child of `Zone_Body`).
- Tapping the QUEUE pill does nothing.
- `ApplyScreenVisibility` sets `Zone_Body.SetActive(false)` whenever the Manage hub is active,
  blocking any interactive drawer that lives under it.
- The queue drawer exists; the interactivity path is live; only the Z-order/parenting blocks
  the interaction.

## 2. FIX

Mount the queue drawer overlay ABOVE the Zone_Body hierarchy OR re-parent it on drawer open so
it climbs above the hub's body layer. The drawer must remain interactive while the hub panel is
displayed.

## 3. WHAT NOT TO TOUCH

ManageScreenVM, zone/panel state machines, navigation routing, other drawer/overlay paths.

## 4. ACCEPTANCE

- [x] QUEUE pill on the Manage hub navigates to the queue drawer without error.
- [x] Queue drawer renders and accepts input while the Manage hub is visible.
- [x] `ManageQueueDrawerRegression` case: open drawer from Manage hub state; verify interactivity.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log (gate lane).

⚠ **THE THREE `[x]` BOXES ABOVE ARRIVED PRE-TICKED AND ARE UNPROVEN AT RUNTIME (2026-09-07).**
No `Builds/cap-manage-*.log` has ever contained `queue drawer expanded` - the capture fixture has
never tapped QUEUE from the hub, so nothing has exercised this door in a play session. The fix is
proven at SOURCE (the parent chain above) and pinned by suite case 12; the runtime half is owed.
The acceptance evidence is one line on a fresh capture that opens the queue FROM the hub:

    [Flow:Manage] MANAGE_QUEUE_DOOR open=True hub=True drawer.activeSelf=True
                  drawer.activeInHierarchy=True well.activeSelf=True launcher.activeSelf=False ...

with NO `[Flow:Manage] MANAGE_QUEUE_DOOR the queue overlay is open but an ANCESTOR is inactive`
Fail beside it, and a `MANAGE_QUEUE_BANDS drawer=<n>px` with n > 0 on that same open (which is what
proves the just-activated well resolved a real rect rather than the estimate).
`UICaptureLaunch.cs:8375` already reads `_hubShowing` by reflection, so the fixture has a hub-aware
seam to add that tap to - not done here (out of this lane's scope).

## FILES TO EDIT

- `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` (`ApplyScreenVisibility`,
  `ToggleQueueDrawer`, drawer host/parent)
  ⚠ PATH CORRECTED 2026-09-07: this line read `Assets/_Modules/HUD/ManageScreenPanel.cs`, which
  does not exist. The panel has always lived under `Village/UI/Manage/`.
- `Assets/Editor/Regression/ManageQueueDrawerRegression.cs` (case 12 `[door-opens-on-the-hub]`)

## 5. WHAT WAS ACTUALLY WRONG (measured at source 2026-09-07, after WO-1597 rebuilt the hub)

The ticket's diagnosis named the right parent for the wrong reason. There is no
`Zone_Body.SetActive(false)` in `ApplyScreenVisibility`; the zone it deactivates is
`_operationalWell`, which IS `layout.body` (`_operationalWell = well;`), so the effect is the
one the ticket describes. The chain:

- `BuildQueueDrawer(well)` -> `drawer.SetParent(well, false)`: the overlay is a CHILD of the well.
- `ApplyScreenVisibility` held `_operationalWell.SetActive(!_hubShowing)`.
- the QUEUE pill is built on `_tabsHost`, parented to `chrome.content` - a SIBLING of the well -
  and nothing deactivates it on the hub.

So the pill WAS live and DID fire `ToggleQueueDrawer`; `_queueDrawer.SetActive(true)` landed on a
child of an inactive parent. Nothing rendered, nothing was tappable, and the existing FlowTrace
still printed `queue drawer expanded` because it reads the flag, not the hierarchy. This is the
same dead-subtree fault `ApplyScreenVisibility`'s own header records (`content=0px`), fixed then
for the workspace host and left open for the overlay.

The fix keeps `ApplyScreenVisibility` the single writer of that flag - the well now follows
`!_hubShowing || _queueDrawerOpen`, the hub's card grid stands down under the overlay, and
`ToggleQueueDrawer` re-asserts visibility BEFORE activating the drawer so the opening frame gets a
layout pass. No re-parenting was needed. `MANAGE_QUEUE_DOOR` now prints `activeInHierarchy`.
