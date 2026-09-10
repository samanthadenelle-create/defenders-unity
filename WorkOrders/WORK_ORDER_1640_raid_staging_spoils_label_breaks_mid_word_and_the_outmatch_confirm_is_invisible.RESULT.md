# WO-1640 RESULT — raid staging: "SPOILS" whole again, and the outmatch confirm draws above the panel

**Status:** IMPLEMENTED - awaiting gate + device frame (lane RAID-STAGING 2026-09-10)
**Lane:** RAID-STAGING, edit-only worktree `D:\EoA\.claude\worktrees\agent-ae49ed9e35bde6122`
**Base:** `a96bfe332` (fast-forwarded from `f5d39acd1` to `refs/heads/dev` at lane start; tree clean before edits)
**Constraint honoured:** EDIT ONLY. No Unity run, no gate, no commit, no push. `RaidHudController.cs`
(WO-1639) and `RaidScoring.cs` untouched; `RaidDeployVM.cs` untouched (HeartfireRegression PIN F);
`RaidSelectionVM.cs` / `scene-configs.json` untouched.

---

## 0. What is PROVEN and what is NOT (CLAUDE.md §11B)

**Proven this session, at source or on a frame:**

- The wrap is real and is the prefix. `Builds/device-frames/2026-09-10_0605_raid_staging.png` opened:
  the right column reads `SPOIL` over an orphan `S`, with `1800 1100 2200` beside it. Every other chip
  on that frame (`POWER`, `RECON`, `SCOUT REPORT`, `ARMY 8 / 10`) renders whole.
- The prefix had **no wrapping mode and a heuristic width**. `CostFormat.cs` `AddCostText` set text,
  size, style, colour, alignment, raycast and two `LayoutElement` numbers and stopped — read at source
  before the edit; `preferredWidth = Math.Max(28f, value.Length * 8f) * (fontPx / 13f)`, i.e.
  **88.6 ref px** for `"SPOILS"` at `fontPx: 24`. No `FitSingleLine`/`FitBlock` anywhere in the file.
- **Both canvases are `ScreenSpaceOverlay`**, so the 720-vs-31050 comparison is a plain sorting one and
  the occlusion premise holds: `ElarionUiKit.BuildModalCanvas` sets
  `canvas.renderMode = RenderMode.ScreenSpaceOverlay` at `Assets/_Modules/Core/UI/ElarionUiKit.cs:104`
  and `canvas.sortingOrder = sortingOrder` at `:105`; `ShowToast` sets the same render mode and
  `canvas.sortingOrder = sortingOrder` at `Assets/_Modules/Core/UI/ElarionUiKitConformance.cs:409-410`,
  default `720` at `:393-394`. The panel is built at `31050` with `overrideSorting = true`
  (`RaidDeployScreen.cs:334-336`, unchanged by this lane) and carries the WO-1462 0.94-alpha kit
  `Backdrop`. **This is the one that licensed the fix.**
- `sortingOrder` was **already a caller-supplied optional** on `ShowToast`
  (`ElarionUiKitConformance.cs:393-394`), so no kit change was needed.
- `31400` clears every modal in the game and stays below the F8 band. Enumerated 2026-09-10 over
  `Assets/_Modules` + `Assets/Editor`: highest panel is `BuildModalCanvas(WorkspaceName + "Canvas", 31300)`
  (PlayerDeckWorkspace); `ManageScreenUI`/`HeartPanelUI` are `31200`; `BugReportToastCanvas` and
  `Village2VictoryBanner` sit at `32000` and keep the top band.
- `FitSingleLine` would have been the WRONG tool here: `ElarionUiKitObsidian.cs:3054-3070` clamps
  `maxSize` to the label's current size and `minSize` up to `FontHardFloor`, so at `fontPx 24` (already
  under `FontFloor = 30f`, `:3033`) it collapses to `min = max = 24` and switches
  `overflowMode = Ellipsis`. The row would have read **`SPOIL…`** instead of `SPOIL / S`. Recorded so
  nobody proposes it in review.

**NOT proven — say so, do not tick:**

- **Render-time numbers.** No Unity ran in this lane. `preferredWidth` vs resolved rect width,
  `textInfo.lineCount`, `characterCount` — all of it is read by the new regression case on a settled
  canvas; none of it has been observed. The authoring-time half is now traced (see §3).
- **The RED was not observed.** Both new RED cases are red *by construction* against the pre-fix code.
  To SEE them: `git stash` `Assets/_Modules/Core/UI/CostFormat.cs` (Item A) or
  `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` (Item B) and run the two suites.
- **Whether the toast on `2026-09-10_0607_arena_01_entry.png` was on screen and occluded, or had
  already expired.** The WO's own caveat stands; the frame's wall-clock label is approximate and the
  toast lifetime was not read off that capture. What is proven is the sorting arithmetic, and that is
  what the fix acts on.
- **Whether the confirm card overlaps the BEGIN ASSAULT face.** The toast anchors bottom-centre
  `+220` ref px (`ElarionUiKitConformance.cs:424`) and the card is now 760x132. Rough frame
  geometry puts the CTA row near there. **Unmeasured.** The next frame must show it (§6).

---

## 1. ITEM A — the fix, and why it is confined to the prefix

`Assets/_Modules/Core/UI/CostFormat.cs`

- `:113-116` — `CostRow`'s prefix branch now reads
  `SealPrefixCell(AddCostText(root.transform, prefix, color, fontPx), prefix, fontPx);`.
  The amount cells go through the unchanged `AddCostText` path.
- `:138-156` — `AddCostText` now **returns** the `TextMeshProUGUI` (so there is no second construction
  path) and calls `EnsureFont(text)` before `.text`.
- `:160` — `PrefixPadPx = 6f`.
- `:162-229` — `SealPrefixCell` (comment `:162-197`, body `:198-229`): authors `textWrappingMode = NoWrap`, and widens the cell to
  `GetPreferredValues(value).x + PrefixPadPx` **taken as a MAX against the existing heuristic**, then
  emits the §5.2 trace.

**I took the CONFINED shape, per WO §4.** The wrap fix and the width fix touch **only the prefix cell**.

**ONE line does touch the amount cells, and it is declared here:** `EnsureFont(text)` in `AddCostText`.
Justification: it is the kit's own documented law — `ElarionUiKit.Label` at
`Assets/_Modules/Core/UI/ElarionUiKit.cs:1911` says *"assign a font BEFORE .text so first generation
can't NRE"*, and this construction path never did. `EnsureFont` (`ElarionUiKit.cs:1938`) **returns
immediately when `t.font != null`**, and it authors no width — `LayoutElement.preferredWidth` is the
only thing the `HorizontalLayoutGroup` reads (`childControlWidth = true`, `CostFormat.cs:110`), so
containment is arithmetically unaffected. It also makes the prefix's `GetPreferredValues` honest
instead of font-dependent.

**Two things deliberately NOT done:**

- **No `minWidth`, anywhere.** A forced minimum would let the row's minimum sum exceed its band and the
  layout group would push children outside it — the exact WO-1060 escape `CostRowFitRegression` reds
  for. Reasoned and rejected before typing; written into the code comment so it is not re-proposed.
- **No fitter on `AddCostText`.** WO §4's STOP is respected: the WO-697 kit law
  (`ElarionUiKitObsidian.cs:964-967`) forbids ellipsis/auto-shrink on a currency VALUE. `NoWrap` does
  neither, so the law holds even though the prefix is not a number. **The change never reaches the
  amounts' fit behaviour.**

**Prior art cited in the code:** `ElarionUiKitObsidian.cs:973` — the kit already authors exactly
`textWrappingMode = NoWrap` for the wallet amount label.

### The second mismatch WO §1c asked me to REPORT and NOT fix

`RaidDeployScreen.cs:219` declares the `spoils` band's seat font as `ElarionUi.FontLabel` — **40**
(`Assets/_Modules/Core/UI/ElarionUi.cs:114`) — while `BuildSpoilsChips` draws the row at
**`fontPx: 24f`** (`RaidDeployScreen.cs:890-892`), which is **below**
`ElarionUiKit.FontFloor = 30f` (`ElarionUiKitObsidian.cs:3033`). So `DeployBand.NeedsPx`
(`RaidDeployScreen.cs:198-200`) is sizing the band against a font this row does not use, and the row
itself ships under the mobile-legibility floor.

**NOT fixed, as instructed** — raising the draw size would change every amount on the row, and the
FontFloor question is a ruling, not a lane call. **Flagged for the owner:** either the band table's
seat font comes down to what is drawn, or the row comes up to the floor. Note the tension with the
lane brief's "never a font below the floor": this row **already is**, at HEAD, and this WO explicitly
forbade resolving it here.

---

## 2. ITEM B — the fix, and which shape it is

`Assets/_Modules/Village/Hero/RaidDeployScreen.cs`

- `:1056-1090` — the WO-1640 block: the sorting arithmetic, the two device citations
  (`2026-09-10_raid_logcat_stream.txt:25546-25547` and `2026-09-10_0607_arena_01_entry.png`), the
  31400 justification, and the "not proven" note about the 0607 frame.
- `:1092` — `private const int ToastSortingOrder = 31400;`
- `:1098-1099` — `ConfirmToastWidth = 760f`, `ConfirmToastHeight = 132f`, `ConfirmToastLife = 5.0f`.
- `:1120`, `:1126`, `:1134`, `:1146`, `:1168`, `:1185` — **all six**
  `ShowToast` calls in `OnDeploy` now pass `sortingOrder: ToastSortingOrder`.
- `:1168-1170` — the WO-1542 confirm additionally gets the bigger card and the longer life. The
  sentence is ~80 characters, exactly the 480x76 default card's stated two-line capacity
  (`ElarionUiKitConformance.cs:387-392`), past which a third line draws **outside** the plate.

**This is NEITHER of the WO's two shapes verbatim, and that is deliberate — it is smaller than both.**
Shape 2 was written as "raise the toast overlay's sorting", called out as **kit-wide** and needing its
own justification and pins. `sortingOrder` is already a caller-supplied optional, so raising it **at
this one caller** moves this screen's toasts and **nothing else in the game** — no kit edit, no other
screen's toast changed, no new kit-wide pin owed. Shape 1 (a band on the panel) was measured against
and **rejected**: the body/footer seam is roughly `0.015` of panel height (`ElarionUiKit.cs:684`,
`bodyFloor = z.footer.w + 0.015f`) and the two CTAs already overflow the footer band via
`SeatFooterCtaAtCanonicalHeight` (`CanonCtaHeight = 132f`, `ElarionUiKit.cs:341`) — there is no room
I can prove exists, and every band in `BandsFor` is pinned disjoint.

**The WO-1542 two-tap step is UNTOUCHED.** `_vm.NeedsOutmatchConfirm` / `AcknowledgeOutmatch()` /
`_vm.Deploy()` are called exactly as before; the branch still returns; the second tap still marches.
`RaidDeployVM.cs` was not opened for edit at all.

**Deliberately NOT re-captioned.** The VM's sentence says *"Tap BEGIN ASSAULT again to march anyway"*
and it is composed by `RaidSelectionVM.OutmatchConfirmToast`, which this WO puts off-limits. Changing
the face's verb would have made the button contradict the sentence naming it. The face keeps
`"BEGIN ASSAULT"`, so `RaidDeployUiRegression:369-389` and `RaidDeployZeroArmyRegression:248-260`
(the literal exactly once, inside the `troops > 0` branch) are untouched — `BuildDeployBar` was not
edited at all.

**`OpenTroopsDoor`'s toast (`RaidDeployScreen.cs:1048`) is deliberately left at the kit default**: it
fires *after* `Close()`, so there is no panel for it to hide behind.

---

## 3. Instrument first (WO §5) — both seams, both permanent

1. **`OnDeploy` arrival.** `RaidDeployScreen.cs:1103-1116`, the call at `:1110` — one `FlowTrace.Step` before any branch,
   naming the raid, the scene, `fielded`, `needsOutmatchConfirm` and **the branch it is about to
   take** (`no-vm` / `no-scene` / `scene-not-in-build` / `zero-army` / `outmatch-confirm` / `march`).
   The chain mirrors **every** branch below it in order — an earlier draft omitted the
   `!IsSceneInBuild` refusal (`:1131`) and would have traced that tap as `zero-army` or `march`. A
   trace that names a branch the code did not take is worse than one that names none. Before this, "tap not
   received" and "tap received and swallowed" were indistinguishable except by inference.
2. **Item A's authoring-time numbers.** `CostFormat.cs:216-228` — `FlowTrace.Once("CostFormat",
   "prefix-fit-" + value, ...)` prints `len`, `fontPx`, `heuristicPx`, `measuredPx`, `authoredPx`,
   `widened`, `wrap`. The **render-time** half (resolved rect width, `textInfo.lineCount`,
   `characterCount`) is read post-`Settle` by the new regression case — that is the only place a
   settled canvas exists in this pipeline, and the split is stated rather than blurred.
   Interpolated parts are computed into locals first (CLAUDE.md §1 — the gate's brace scanner has no
   interpolated-string model).

Nothing was stripped (CLAUDE.md §12, "NEVER STRIP FLOWTRACE").

---

## 4. Pins — the coverage gap WO §7 named, closed RED-first

### `Assets/Editor/Regression/CostRowFitRegression.cs`

- `:44` — added `using TMPro;`.
- `:90` — `casesRun += 5`; the two new cases run at both landscape aspects.
- `:110-116` — the `COST_ROW_FIT_OK` reason now names the prefix contract.
- `:239-264` (the gap statement + fixture constants), `:266-273` (`HeuristicPrefixWidthPx`),
  `:275-318` — `CasePrefixIsOneWholeWord`: builds the **staging screen's own row** through the
  production `ElarionUiKit.CostRow` via `BuildSpoilsRow` (`:383-398`) and `PrefixLabel` (`:400-405`)
  (prefix `"SPOILS"`, `fontPx: 24`, the three spoils resources) on a
  **deliberately wide 900 px card**, then `ForceMeshUpdate()` and asserts
  `textInfo.lineCount == 1` **and** `textInfo.characterCount == "SPOILS".Length`.
  The wide card is the point: a narrow band would confound "the heuristic undersized the word" with
  "the group ran out of room", which are different bugs with different fixes.
- `:320-381` — `CaseRedWhenPrefixWraps`: restores the pre-fix **condition** — a cell narrower than the
  word, with `TextWrappingModes.Normal` live — and **fails if the word does NOT break**. Written as
  `Mathf.Min(heuristic, measured - 4f)` rather than the raw heuristic on purpose: pinning 88.6 would
  make the red depend on the shipped font's metrics, and a font swap would then turn the RED case into
  a false FAILURE instead of a finding.
- Both cases fail loudly and non-vacuously when the fixture degenerates (no children / first child not
  a TMP / no `LayoutElement`).
- `Card(Transform)` kept; `Card(Transform, float widthPx)` added beneath it (`:429-440`). The existing three cases
  are byte-for-byte unchanged in behaviour.

**Why the existing suite could never have caught this:** all three prior cases assert **containment** —
whether a child's *rect* escapes the band. A wrapped word escapes nothing; it reads as two lines inside
a rect exactly as wide as it always was. That is the gap, stated at `:239-264`.

### `Assets/Editor/Regression/RaidDeployUiRegression.cs`

- `:65` — `CheckDeployToastAboveModal(failures, notes)` added to `Run()`. **`DataRegression.cs` was NOT
  touched** — this suite is already registered at `Assets/Editor/Regression/DataRegression.cs:619`.
- `:419-549` — `[deploy-toast-above-modal]` (method at `:442`). It **reads both numbers out of the live source**:
  the sorting argument of the **assignment** `_ui = ElarionUiKit.BuildModalCanvas("RaidDeployScreenUI", …)`
  — the assignment form deliberately, because the new WO-1640 comment block at `:1056-1090` quotes
  the canvas name and the band, and a bare-call search would have parsed the COMMENT and passed if the
  real call were ever removed — and the value of
  `private const int ToastSortingOrder`, and requires `toast > modal`. Then it requires every
  `ShowToast(` inside `OnDeploy`'s body to carry `sortingOrder: ToastSortingOrder`, and requires the
  first `FlowTrace.` in that body to **precede** the first `ShowToast` (§5.1 arrival).

  **A source lint, not a built probe, and that is forced:** `ShowToast` early-outs on
  `!Application.isPlaying` (`ElarionUiKitConformance.cs:397`), so no edit-mode oracle can build a
  toast to measure. Neither number is copied into the suite — it reads both, so it cannot go stale the
  way a pinned literal does. It fails loudly (never silently green) if the canvas call, the const, or
  `OnDeploy` is renamed, and if `OnDeploy` fires no toast at all.

  **RED by construction:** against the pre-WO-1640 file there is no `ToastSortingOrder` const and not
  one `ShowToast` carries the argument, so every branch fires. **Not observed by this lane (edit-only).**

**No gate marker string appears anywhere in this lane's edits** other than the pre-existing
`COST_ROW_FIT_OK` / `COST_ROW_FIT_FAIL` inside that suite's own reason strings, which were already there.

---

## 5. Regressions that must stay green — and why they do

| Pin | Verdict |
|---|---|
| `RaidDeployUiRegression.cs:369-389` (`BuildObsidianButton`, Yellow, `"BEGIN ASSAULT"`, wires `OnDeploy`, two seated CTAs) | `BuildDeployBar` was **not edited**. |
| `RaidDeployZeroArmyRegression.cs:201` (`PrimaryAssaultLabel == "BEGIN ASSAULT"`), `:248-260` (literal exactly once, inside the `troops > 0` branch), `:367-374` (`Fielded <= 0` precedes `GoRaid` in `Deploy()`) | `RaidDeployVM.cs` and `BuildDeployBar` both untouched. The one new `"BEGIN ASSAULT"`-bearing string in `OnDeploy` is inside a `FlowTrace` message, outside the lint's `BuildDeployBar` span. |
| `RaidDeployVM.cs:374-380` — no new refusal branch in `CanDeploy` / `ShowAssault` / `Deploy()`; HeartfireRegression PIN F | `RaidDeployVM.cs` not opened for edit; no debounce added anywhere. ⚠ **Correction to the WO's §7 wording, read at source:** PIN F's source-lint targets are `HeartfireRegression.cs:91-92` — `SelectRel = "_Modules/Village/Hero/RaidSelectionScreen.cs"` and `DeployRel = "_Modules/Village/Troops/RaidDeployController.cs"`. It does **not** read `RaidDeployVM.cs` or `RaidDeployScreen.cs` at all, so the new `tapFielded <= 0` token in the View's trace cannot trip it. |
| `RaidDeployLayoutRegression.cs:191`, `:596-663` (`[vm-spoils-chips]`) | Read at source: `CaseSpoilsChips` (`:602`) asserts on `vm.SpoilsChips` (`:622-623`) — the VM's **data**. It never touches the built row, its `CostText` children, their widths or their line counts. The producer is untouched by this lane. `BandsFor` is untouched, so `[deploy-bands-disjoint]` is unaffected. |
| `CostRowFitRegression` containment cases | `childControlWidth` untouched; **no `minWidth` introduced anywhere.** The only width that moves is the prefix's, and only upward. ⚠ **The `"NEED"` prefix's measured width was NOT measured by this lane** — if TMP measures it wider than its 32 px heuristic, that cell widens too. That does **not** break containment: with `childControlWidth = true` and `minWidth` unset at 0, a preferred sum over the band makes the group shrink every child *inside* the band rather than spill — which is precisely the WO-1060 law the existing `CaseRedWhenWidthUncontrolled` proves is being enforced. The green `NEED` case measures rects and will say so if this reasoning is wrong; **it has not been run.** |
| `RaidSelectionLayoutRegression` S7 `deploy-opaque-backdrop` | The backdrop is untouched; `withBackdrop: false` was not passed back in. |

`grep -rn 31050 Assets/Editor` returns **no hits** — no suite pins that literal, so nothing had to be
re-pointed for the new lint to read it.

---

## 5b. WO §6.2 — every other `CostRow` prefix in the game, named

`grep -rn "ElarionUiKit.CostRow(" Assets/_Modules Assets/Editor` 2026-09-10 returns **six** call sites.
`BuildingUpgradePanelMvvm.BuildNextCostRow` (`:1206`) is NOT one of them — read at source, it builds
its own `ElarionUiKit.Label`s (`:1211`, `:1227`) and never enters this path.

| Call site | prefix | fontPx | Heuristic width for that prefix |
|---|---|---|---|
| `Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs:1505-1508` | `"NEED"` when unaffordable-and-unlocked, else **null** | default `13f` | `max(28, 32) * 1.0 = 32.0` |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:890-892` | `"SPOILS"` | `24f` | `max(28, 48) * 1.846 = 88.6` — **the defect** |
| `Assets/_Modules/Village/UI/Manage/HeartPanel.cs:367-369` | `"Cost:"` | `ElarionUi.FontLabel` = 40 | `max(28, 40) * 3.077 = 123.1` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:4733-4735` | `"Upgrade:"` | `ElarionUi.FontMicro` = 32 | `max(28, 64) * 2.462 = 157.5` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5731-5733` | `"Upgrade:"` | `FontMicro` = 32 | `157.5` |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:6087-6089` | `"Research:"` | `FontMicro` = 32 | `max(28, 72) * 2.462 = 177.2` |

⚠ **NOT RENDERED, NOT MEASURED — no Unity ran in this lane.** What can be said, and only this:

- The width change is a **MAX**. No prefix above can come back narrower than the width it has today,
  so **no site that renders whole today can start wrapping** because of this change. That is the whole
  safety argument and it is arithmetic, not observation.
- The **wrap mode** change can only *remove* a break: `NoWrap` cannot break a word. A prefix already
  whole stays whole; a prefix that was breaking stops.
- If TMP measures any of these prefixes **wider** than its heuristic, that cell widens and the row's
  preferred sum rises. In a narrow band — `BuildPaletteUI`'s 228.8 px card is the narrowest, and the
  one with the 33-finding history — that makes the `HorizontalLayoutGroup` shrink every child
  *proportionally inside the band* rather than spill, because `childControlWidth = true`
  (`CostFormat.cs:110`) and **no `minWidth` is set by this lane**. That is exactly the WO-1060 law
  `CaseRedWhenWidthUncontrolled` proves is being enforced, and the existing green `NEED` case measures
  it. **That case has not been run by this lane** — it is the first thing the gate settles.
- The three `FontMicro`/`FontLabel` sites draw at 32 and 40, i.e. **at or above** `FontFloor = 30f`,
  so none of them is in the sub-floor position the spoils row is in.

---

## 6. What the next staging device frame must show

1. The right column reads **`SPOILS  1800  1100  2200` on ONE line**, with `SPOILS` whole. Paste the
   row.
2. Tap **BEGIN ASSAULT once** on `raider_camp_small` (garrison 9 vs 8 fielded) and capture: the
   sentence *"Outmatched: 9 defenders against your 8. Tap BEGIN ASSAULT again to march anyway."*
   must be **visible on top of the staging panel**, all of it, on one card — not clipped, and not a
   third line drawn outside the plate.
3. **Check the overlap I could not measure:** whether that card sits across the bottom of the
   BEGIN ASSAULT face. If it does, the fix is the card's `anchoredPosition` y (currently `+220` ref
   px, `ElarionUiKitConformance.cs:423-425`) — a follow-up, not this ticket.
4. Tap **BEGIN ASSAULT a second time** — it must march. The trace must show
   `BEGIN ASSAULT tap received … branch=outmatch-confirm` then
   `… branch=march` then `GoRaid`, unchanged from `:27886-27890`.
5. Grep the fresh log for `[Flow:CostFormat] CostRow prefix 'SPOILS'` and paste the line — it carries
   `heuristicPx`, `measuredPx`, `authoredPx` and `widened`, which is the authoring-time half of WO §5.2.

---

## 7. Files changed (all inside the lane worktree)

| Path | What |
|---|---|
| `Assets/_Modules/Core/UI/CostFormat.cs` | Item A — `SealPrefixCell` (NoWrap + measured width, MAX'd), `AddCostText` returns the label + `EnsureFont`, `PrefixPadPx`, the §5.2 trace |
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` | Item B — `ToastSortingOrder = 31400` + confirm card/life consts, all six `OnDeploy` toasts raised above the modal band, the §5.1 arrival trace |
| `Assets/Editor/Regression/CostRowFitRegression.cs` | `[fit-SPOILS-24]` + its RED companion; `Card(Transform,float)` overload; `using TMPro` |
| `Assets/Editor/Regression/RaidDeployUiRegression.cs` | `[deploy-toast-above-modal]` case 7, wired into this suite's own `Run()` (`DataRegression.cs` untouched — already registered at its `:619`) |
| `WorkOrders/WORK_ORDER_1640_….md` | Status flipped (copied into the lane worktree — the WO is not on `dev`) |
| `WorkOrders/WORK_ORDER_1640_….RESULT.md` | this file |

**Checks run (CLAUDE.md §1, WO §6.5):**
`python tools/gate_brace.py` on all four `.cs` → `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0.
Raw brace counts balanced: CostFormat 21/21, RaidDeployScreen 54/54, CostRowFitRegression 45/45,
RaidDeployUiRegression 65/65. **NUL bytes: 0 in every file.**

**Not run, by lane constraint:** `CompileGate`, `DataRegression`, any Unity batchmode, any capture.
The lead gates the combined tree and commits.
