# WO-1871 RESULT — Change Army door on the raid deploy screen (+ the "troops" plural ruling)

**Status:** IMPLEMENTED PENDING LEAD GATE — edit-only lane, 2026-09-18.
**Lane constraint:** no Unity run, no gate, no commit, no `git add`. Every claim below is a CLAIM
until the lead verifies it (CLAUDE.md §11B).

---

## 1. What changed, by file and line

### `Assets/_Modules/Village/Hero/RaidDeployScreen.cs`

*(Every line number below was re-grepped at the FINAL state of the tree, after the last edit. An
earlier draft of this table was written from a mid-session grep and was off by +3 in one file and
+31 in another — recorded here because a RESULT that says "read at source" with numbers that are
not is the hearsay CLAUDE.md §11B names.)*

| Lines | Change |
|---|---|
| `:62-100` | New banner + 4 fields: `_def` `:86` (the raid the View can re-open on — `RaidDeployVM._def` is private and the VM is disposed by `Close()`), `_awaitingMusterReturn` `:90`, `_changeArmyBtn` `:94`, `_staging` `:100`. The banner records the MEASURED sorting fact that forces close-then-open (see §3). |
| `:395-400` | `OpenInternal` caches `_def = def;` (`:399`) and clears `_staging` — **after** its own `Close()`, because `Close()` now clears the return state. |
| `:1094-1115` | **THE DOOR.** `_changeArmyBtn = ElarionUiKit.Button(footer, ChangeArmyFace(), Quiet, (0.00,0.50)-(0.22,0.50), OnChangeArmy)` at `:1109` + `SeatFooterCtaAtCanonicalHeight` + `interactable = !_staging`. Inside the `if (showAssault)` branch only. |
| `:1121` | `EDIT ARMY` moved `0.00..0.28` -> `0.25..0.47` (unchanged handler, unchanged door). |
| `:1132` | `BEGIN ASSAULT` moved `0.32..0.985` -> `0.51..0.985`. |
| `:1159-1193` | New banner explaining why the handlers sit between `SeatFooterCtaAtCanonicalHeight` and `OpenTroopsDoor` (the two existing source-lint windows). |
| `:1196-1199` | `ChangeArmyFace()` — caption from the key `village.troops.raid_deploy_screen.change_army` (`:1198`), same `LocalizedText(...).Resolve()` shape commit `8723810e9` used in this file. |
| `:1201-1250` | **`OnChangeArmy()`** — the exact door handler. Trace -> staging refusal -> `Close()` -> restore `_def` (`:1224`) -> subscribe (`:1227`) -> `ArmyMusterPanel.Show()` under `Guard.Try` (`:1229`) -> **refused-door recovery** (`:1239-1249`, see §11 edge 1). |
| `:1258-1312` | **`OnMusterClosed(bool handedOff)`** — the return re-read seam. Unsubscribes always (`:1260`); returns early on hand-off, on a lost def, and on a PanelManager SWAP (`:1287`, see §11 edge 2); otherwise `OpenInternal(_def)` (`:1296`) then a `FlowTrace.Step("RaidDeploy", ...)` naming `deployableSlots / queued / required / cap / ready / fielded / armyBand`. |
| `:1474-1481` | `OnDeploy` sets `_staging = true` (`:1478`) and dims (never hides) the face at the moment the march commits, with its own trace. |
| `:1548-1552` | `Close()` clears `_def`, `_changeArmyBtn`, `_staging`. |
| `:1558-1561` | `OnDestroy()` unsubscribes from the static event (`:1560`). |

### `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs` (the smallest possible close seam)

| Lines | Change |
|---|---|
| `:102-131` | Banner + `public static event System.Action<bool> Closed;` at `:131` (the bool = `handedOff`). |
| `:133-140` | `public static bool IsAnyOpen` — the one read that tells "the door opened" from "`Show()` refused" (§11 edge 1). |
| `:394-397` | `Open()` tears down with `CloseCore(raiseClosed: false, handedOff: false)` at `:397` — **a rebuild is not a close** (see §4). |
| `:528-534` | `public void Close()` (`:531`) is now a one-line route to `CloseCore(raiseClosed: true, handedOff: false)`. |
| `:536-570` | The old `Close()` body became `CloseCore(bool, bool)` with a `wasOpen` capture at the top; the raise is LAST in the teardown (after `PanelManager.NotifyClosed`), guarded by `wasOpen && raiseClosed`, wrapped in `Guard.Try` at `:568` so a throwing listener cannot half-tear-down the panel. |
| `:1051-1057` | `OnGoRaid()` -> `CloseCore(raiseClosed: true, handedOff: true)` at `:1056`. |

**Nothing else in this file moved.** No layout table, no band, no VM call, no `Rebuild` path.

### `Assets/_Modules/Village/Troops/StarterArmyGrant.cs` (WO item 4)

`:163-196` (the `LocalText.Format` call is `:195`) — `GrantToastFor(int footmen, int archers)` keeps its **signature** (`SplitComposition`'s
10 -> 5/5 contract and `StarterArmyGrantRegression`'s composition cases read both arguments) and
loses its body: the `Footman/Footmen` + `Archer/Archers` ternaries are replaced by
`LocalText.Format("village.troops.starter_army.first_squad_ready_fmt", total)` where
`total = footmen + archers`. The split is still granted; it is simply no longer narrated unit by unit.

### `Assets/Editor/Regression/RaidDeployChangeArmyDoorRegression.cs` (NEW, 3 cases)

Tag `[raid-deploy-change-army]`, markers `RAID_DEPLOY_CHANGE_ARMY_OK` / `_FAIL`,
`public static bool Run(out string reason)`, never throws, **no reflection**, comment-stripped
source lint (all three files under test carry RCA prose naming the very symbols searched for).

### `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs` — **NOT TOUCHED, deliberately**

Grepped 2026-09-18: that screen carries no army SUMMARY band. What it has is a per-card warning
WORD (`_vm.ArmyWarnWordFor(id)`, `:1072-1096`) and the pre-existing full-army gate redirect
(`:342-395`). Neither is a summary needing the same face, so the WO's "only if" did not fire.

---

## 2. The exact seams the brief asked for

- **Door handler:** `RaidDeployScreen.OnChangeArmy()` — `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1201`.
  It calls `DeNelle.Village.ArmyMusterPanel.Show()` at `:1229`.
- **Return re-read seam:** `ArmyMusterPanel.Closed` (`System.Action<bool>`) declared at
  `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:131`, raised at `:568`, consumed by
  `RaidDeployScreen.OnMusterClosed(bool)` at `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:1258`,
  whose re-read is `OpenInternal(_def)` at `:1296`.

---

## 3. Why the door closes the deploy screen FIRST (measured, not preferred)

`RaidDeployScreen.OpenInternal` builds `BuildModalCanvas("RaidDeployScreenUI", 31050)` with
`overrideSorting = true` and — since WO-1462 — the kit's opaque 0.94-alpha Backdrop.
`ArmyMusterPanel.Open` builds `BuildModalCanvas("ArmyMusterPanelUI", 31000)`
(`ArmyMusterPanel.cs:408`). Both are ScreenSpaceOverlay, so the comparison is a plain
`sortingOrder` one and **31000 loses**: a muster panel opened over a live deploy screen is painted
entirely behind an opaque plate and the player's tap looks dead.

The close-then-open order is also what ARMS the WO-1400 return door — the same reasoning
`OpenTroopsDoor` already records, and `PanelManager.cs:371-376` is where the "return door KEPT"
line is emitted when a successor opens. The raise therefore happens **after**
`PanelManager.NotifyClosed`, so the arm-then-open sequence matches the already-proven
`OpenTroopsDoor` shape.

**Not proven from here:** that the `KEPT` line actually prints on this new path. It is a source
reading of `PanelManager`, not a captured log line. One F8/headless capture of the round trip would
close it.

## 4. Why the `Closed` event carries a bool, and why `Open()` does not raise

Two real traps, both found by reading the panel rather than by running it:

1. **`Open()` tears down before it rebuilds** (`:397`), and `OnToggleLoadouts` rebuilds the surface
   by calling `Open()` (`:1041-1046`). A raising teardown there would fire the deploy screen's
   return trip **every time the player tapped Loadouts**, re-opening a briefing over a panel that is
   mid-rebuild. Hence `CloseCore(raiseClosed: false, ...)`.
2. **`OnGoRaid()` routes the player onward itself** — it closes and then opens
   `RaidSelectionScreen`. A plain return signal there would re-open the deploy screen (31050,
   opaque) on top of the selection grid (31000) the player just chose. Hence `handedOff: true`, and
   `OnMusterClosed` unsubscribes on both values but re-opens only on `false` — which also stops the
   subscription leaking into a later, unrelated muster close.

## 5. "Dimmed, never hidden" (WO item 5)

`_changeArmyBtn.interactable` is the channel — set `!_staging` when the footer is built
(`:1115`) and forced `false` the instant `OnDeploy` commits the march (`:1479`). Never
`SetActive(false)`, never a tint: the owner is red/green colourblind, so the state has to survive a
greyscale read, and a control that vanishes reads as a bug.

**Not proven:** the face's rendered width / caption fit / `MinTouchPx` clearance. Nothing in the
repo measures deploy-footer face *width* (the existing `[deploy-bar-kit-button]` lint measures the
seating call, not the rect), and this lane did not add such an oracle. That is WO-1871 acceptance
item 2 — the headless or device capture — judged by eye. The three faces are authored
`0.00-0.22 / 0.25-0.47 / 0.51-0.985` of the footer.

**One more unproven detail, named:** `_staging = true` is set BEFORE `_vm.Deploy()`, so a VM-side
refusal after that point would leave the face dimmed with the screen still open. That is
practically unreachable — every VM refusal the View can hit (`_vm == null`, empty scene name,
scene-not-in-build, `Fielded <= 0`, outmatch confirm) is mirrored and returned from ABOVE this
line — but it is an ordering choice, not a proof.

---

## 6. Why each suite case is RED on HEAD

Run against the pre-change tree (the revert recipe in §7 reproduces each):

| Case | Red-on-HEAD line |
|---|---|
| **A1** | `BuildDeployBar wires no OnChangeArmy handler` — on HEAD `RaidDeployScreen.cs` contains no `OnChangeArmy` anywhere, and the WO's own measured gap says so: the file never referenced `ArmyMusterPanel` at all. |
| **A2/A3** | `ChangeArmyFace()` does not exist -> `Slice` returns null -> `ChangeArmyFace() not found`. |
| **A4/A5/A6** | `OnChangeArmy` does not exist -> `RaidDeployScreen.OnChangeArmy not found`. |
| **A7** | `_changeArmyBtn` does not exist -> `the CHANGE ARMY face's staging state is not carried by interactable`. |
| **B1** | `ArmyMusterPanel declares no public static event System.Action<bool> Closed` — on HEAD the file has no `Closed` event; its only events are `BarracksService.Changed` / `ArmyMusterService.Mustered`, which it *consumes*. |
| **B2** | `nothing in ArmyMusterPanel raises Closed`. |
| **B3** | `Close()` on HEAD has the teardown as its whole body, so the slice to `private void CloseCore(` returns null -> red; `OnGoRaid` has no `handedOff: true`. |
| **B4** | `Open()` on HEAD calls bare `Close()` -> no `CloseCore(raiseClosed: false` -> red. |
| **B5** | `OnMusterClosed(bool)` does not exist -> `nothing listens to the seam`. |
| **C1** | `StarterArmyGrant.cs` on HEAD compiles `"Footman"`, `"Footmen"`, `"Archer"`, `"Archers"` as literals at `:167-172` — **four** red lines. |
| **C2** | `GrantToastFor` on HEAD holds the literals `" "`, `"Footman"`, `"Footmen"`, `" and "`, `"Archer"`, `"Archers"`, `"Your first squad is ready - "`, `", free. Open Journey, then Raids."` and no `LocalText.Format(` — red on the `LocalText.Format` check, on the key check, and once per non-key literal. |

**Vacuity guards:** every case that cannot find its window (`Slice` -> null) FAILS with a named
reason rather than passing silently. There is no branch in this suite that can report OK without
having matched something.

### 6b. MEASURED, not reasoned — the suite's needles run against both trees

The suite's comment-stripper, `Slice`, `StringLiterals` and all 22 needles were ported verbatim to
a scratch Python harness and run twice: once against the working tree, once against
`git show HEAD:<path>` copies of the three files. Output, this session:

```
working tree : SIMULATED: RAID_DEPLOY_CHANGE_ARMY_OK
HEAD         : SIMULATED: FAIL ['A bar missing OnChangeArmy', 'A bar missing ChangeArmyFace()',
               'A bar missing _changeArmyBtn.interactable', 'A3', 'A4 no door', 'B1', 'B2',
               'B3 close', 'B3 go', 'B4', 'B5 none', 'C1 "Footman"', 'C1 "Footmen"',
               'C1 "Archer"', 'C1 "Archers"', 'C2 fmt', "C2 literal ' '",
               "C2 literal 'Footman'", "C2 literal 'Footmen'", "C2 literal ' and '", ...,
               "C2 literal 'Your first squad is ready - '",
               "C2 literal ', free. Open Journey, then Raids.'", 'C2 key']
```

Every case is red on HEAD and green after the change. **What this proves and what it does not:** it
proves the NEEDLES match the code in both directions — it does NOT prove the C# compiles or that
Unity runs the suite. That is the lead's `COMPILE_GATE_OK` + `REGRESSION_OK`.

### 6c. The two existing deploy-bar lints were simulated too

`[zero-army-footer]` and `[deploy-bar-kit-button]` were re-run as scripts against the edited file:

```
guard=1101  firstReturn=4902  "BEGIN ASSAULT"=4391  "TRAIN TROOPS"=5519
  -> assault INSIDE the branch = True, train OUTSIDE = True
BuildObsidianButton call: Yellow=True, labelled "BEGIN ASSAULT"=True
AddImage( = 0    "DeployGlow" = 0    return; in bar = 1 (unchanged)
SeatFooterCtaAtCanonicalHeight( calls = 4  (was 3; the check is >= 2)
whole-file PanelRouter.Open(PanelId.Manage count = 1  (unchanged)
```

**This forced two comment rewrites and they are worth recording**, because it is exactly the trap
CLAUDE.md §1 documents for the brace gate: a first draft of the new banner quoted the two neighbour
captions in prose and used the word `return;` in prose. `[deploy-bar-kit-button]` reads this method
as RAW text, so a quoted caption in a comment reads as a second button, and a prose `return;` would
have become "the first branch exit" and moved the whole position window. Both were rewritten to say
the same thing without the tokens, and the in-file comment now says why.

---

## 7. Revert recipe (per the WO)

- **A red:** delete the `_changeArmyBtn = ElarionUiKit.Button(footer, ChangeArmyFace(), ... OnChangeArmy)`
  statement from `BuildDeployBar` (and its `SeatFooterCtaAtCanonicalHeight` / `interactable` lines).
  A1 and A7 red immediately; deleting `OnChangeArmy`/`ChangeArmyFace` as well reds A2-A6.
- **B red:** change `ArmyMusterPanel.Open`'s first teardown back to a bare `Close()` — B4 reds, and
  that revert *is* the Loadouts-toggle bug. Deleting the `Closed` event reds B1/B2/B5.
- **C red:** restore the `footmen == 1 ? "Footman" : "Footmen"` / `archers == 1 ? "Archer" : "Archers"`
  ternaries in `GrantToastFor`. C1 reds four times, C2 reds on every restored literal.

---

## 8. ⚠ LEAD ACTIONS — three, all in files outside this lane's silo

0. **Import the new file.** `Assets/Editor/Regression/RaidDeployChangeArmyDoorRegression.cs` has no
   `.meta` yet — the lead's Unity import generates it, and it must be staged with the `.cs`.
1. **Register the suite.** One line in `Assets/Editor/Regression/DataRegression.cs` for
   `RaidDeployChangeArmyDoorRegression.Run`. (Lane was told not to touch that file.)
2. **Merge the 2 sidecar keys** from `Logs/debug/scratch/sweep-sidecar-wo1871.json` into the 10
   catalogs + 7 tables. Until then `LocalText` answers `[[missing:<key>]]` by design
   (`LocalText.cs:157-162`) — the face and the FTUE toast both read that way, and item 3 below
   stays red for a second reason.
3. **🔴 BLOCKING — re-point `StarterArmyGrantRegression.cs:281-292`.** Case C2 asserts the starter
   toast names `N Footmen` / `N Archers` **by unit**. That is precisely the wording the owner
   retired on 2026-09-18, so those four assertions go RED on this change. This was **found by
   reading the suite, not by running it** — the lane cannot run Unity and cannot edit that file.
   Suggested one-edit replacement for `:281-292` (the four by-unit assertions only):

   ```csharp
   // WO-1871 (owner ruling 2026-09-18): "troops" is the generic plural across the board;
   // a per-unit plural form is never authored again. The toast names the TOTAL.
   int total = f + a;
   if (total > 0 && !toast.Contains(total.ToString()))
       failures.Add("[C2] the " + label + " toast does not name the total granted (" +
                    total + "): \"" + toast + "\"");
   if (toast.IndexOf("Footm", System.StringComparison.Ordinal) >= 0 ||
       toast.IndexOf("Archer", System.StringComparison.Ordinal) >= 0)
       failures.Add("[C2] the " + label + " toast names a per-unit plural, which the " +
                    "2026-09-18 ruling retired: \"" + toast + "\"");
   ```

   `:295-299` (the `Journey` -> `Raids` check) is **deliberately left green** — the English copy in
   the sidecar keeps both words, and the sidecar records that as a translator constraint.

---

## 9. Gates run in this lane (edit-only; no Unity)

```
$ python tools/gate_brace.py Assets/_Modules/Village/Hero/RaidDeployScreen.cs \
    Assets/_Modules/Village/Troops/ArmyMusterPanel.cs \
    Assets/_Modules/Village/Troops/StarterArmyGrant.cs \
    Assets/Editor/Regression/RaidDeployChangeArmyDoorRegression.cs
GATE_BRACE_SUMMARY bad=0 of 4        (exit 0)
```

```
CLAUDE.md §1 brace + NUL one-liner:
Braces balanced (62)  OK  NUL=0  Assets/_Modules/Village/Hero/RaidDeployScreen.cs
Braces balanced (115) OK  NUL=0  Assets/_Modules/Village/Troops/ArmyMusterPanel.cs
Braces balanced (17)  OK  NUL=0  Assets/_Modules/Village/Troops/StarterArmyGrant.cs
Braces balanced (41)  OK  NUL=0  Assets/Editor/Regression/RaidDeployChangeArmyDoorRegression.cs
exit 0
```

Non-ASCII scan of every added range: all hits are in COMMENTS (em dash / ⛔ / ⚠, the house style
already in these files). **No non-ASCII in any string literal** — the mobile font-atlas law holds.

`COMPILE_GATE_OK` / `REGRESSION_OK n/n` / `UI_CAPTURE_OK` are **NOT claimed** — this lane does not
run Unity.

## 10. Existing pins this change was written around (read at source, not assumed)

- `RaidDeployZeroArmyRegression [zero-army-footer]` bounds `BuildDeployBar` at the FIRST `return;`
  after `if (showAssault)`. **No new `return;` was added inside that branch** — the staging dim uses
  `if (_changeArmyBtn != null) { ... }`, not an early-out. `"BEGIN ASSAULT"` still appears exactly
  once, inside the branch; `"TRAIN TROOPS"` still after it; `OnEditArmy` / `OnTrainTroops` still
  wired; the `deploy footer fielded=` trace and `PrimaryCtaLabel` untouched.
- `RaidDeployUiRegression [deploy-bar-kit-button]`: no `AddImage(` added to the bar; `BEGIN ASSAULT`
  is still `BuildObsidianButton` Yellow wiring `OnDeploy`; `SeatFooterCtaAtCanonicalHeight` call
  count goes 2 -> **3** (the check is `>= 2`).
- `RaidDeployZeroArmyRegression [zero-army-door]` counts `PanelRouter.Open(PanelId.Manage` across the
  whole file and demands exactly **1**. This change adds none. The new handlers are placed OUTSIDE
  the `OpenTroopsDoor`..`OnDeploy` window on purpose.
- `StarterArmyGrantRegression` `SplitComposition` / composition cases: signature and split untouched.

### 10b. The wider sweep — every suite that reads these three files

`grep -ln "RaidDeployScreen\|ArmyMusterPanel\|StarterArmyGrant" Assets/Editor/Regression/*.cs` returns
**27** files. Each was checked for a needle my new tokens could move. The non-obvious ones:

- **`ArmyMusterRegression`** reads `ArmyMusterPanel.cs` as source and needles `ArmyMusterVM` and
  `"village.troops.army_muster.save_slot"` — both untouched. Its **`CheckAsciiOnly`** case scans
  that same file line by line and demands ASCII on any line containing a `"` that is **not**
  comment-prefixed. Every new non-ASCII character in this change is on a line whose trimmed start
  is `//` or `///`, so it is skipped. Verified by re-running that rule's logic over the added ranges.
- **`ArmyMusterLayoutRegression`** measures `ComputeBands` / `ComputeLoadoutBands` / `ComputeRowBands`
  — no layout table, band, row or constant was touched.
- **`RaidDeployLayoutRegression`** iterates `BandsFor()` / `EnemyCardBands()` / `PartyRowBands()` —
  none touched. The footer is not in the band table (it is `chrome.layout.footer`).
- **`UiMvvmConformanceRegression`** — the change adds no model, no `GameState` read and no rule to
  either View; `_def` is the identity of the screen it is already open for, and the one readiness
  read stays inside `OpenInternal`.
- The remaining 20 read these files for unrelated needles (Heartfire, cooldown, funnel, spoils,
  soft-gate, copy, Echo memory, single-hero join, remote tunables, tutorial reachability …).

**NOT PROVEN:** this is a read of each suite's needles, not a run of them. Only
`[zero-army-footer]` and `[deploy-bar-kit-button]` were simulated end to end (§6c). The lead's
`REGRESSION_OK n/n` on a fresh log is the proof for all 27 — and `StarterArmyGrantRegression` is
the ONE this lane expects to be red until §8 item 3 lands.

## 11. Edges — the three this round trip has, all named

**Edge 1 — a REFUSED door (FIXED in this change).** `ArmyMusterPanel.Show()` returns having opened
nothing when the Barracks is not built (`ArmyMusterPanel.cs:382-387`, `return;` at `:386` after a
spoken toast) and can also
be rejected by the PanelManager battle-lock. On either path `Closed` never fires — so a caller that
had already closed itself would have stranded the player on the bare HUD with an armed subscription
that re-opened the old briefing at some unrelated later moment. `ArmyMusterPanel.IsAnyOpen`
(`:140`) is read straight after `Show()` (`RaidDeployScreen.cs:1239-1249`); on a refusal the door
unsubscribes, clears the flag, traces a `Warn`, and re-opens the briefing, so the tap costs nothing
and the toast still explains why.
*Reachability NOT proven:* whether `Fielded > 0` (which is what draws the face) can coexist with an
unbuilt Barracks. Dev scenario kits and `ff.raidtest` can seed an army, which is why it is handled
rather than argued away.

**Edge 2 — a PanelManager SWAP is not a return (FIXED in this change).** `Close()` is the callback
`PanelManager` invokes on `previous` when something else opens, and it does that AFTER setting
`_open` to the new handle (`PanelManager.cs:365-366`, then `previous.Close?.Invoke()` at `:389`).
Since `Close()` now raises,
an un-gated listener would re-open the deploy screen from INSIDE another panel's open and
immediately close the thing that was arriving. `OnMusterClosed` therefore returns early on
`PanelManager.AnyOpen` (`RaidDeployScreen.cs:1287`). That read is the right discriminator because
`NotifyClosed` sets `_open = null` ONLY when the handle is still the open one
(`PanelManager.cs:437-438`) — so a swap leaves `AnyOpen` true and a genuine close leaves it false.
*Also checked:* `ElarionUiKit.BuildObsidianModal` (the manage sheet, sortingOrder 32200) does NOT
register with `PanelManager` — grep returns nothing — so opening the sheet cannot trigger this path.

**Edge 3 — the GO hand-off does not bring the briefing back (a lane product call, not a ruling).**
If the player opens CHANGE ARMY and then uses the muster panel's own GO face, the deploy screen
does not reappear; the raid selection grid the player chose stays on top (§4). If the owner wants
the briefing back in that case, flip `OnMusterClosed`'s `handedOff` branch to re-open. It is one
`if`, and it is called out here rather than decided silently.
