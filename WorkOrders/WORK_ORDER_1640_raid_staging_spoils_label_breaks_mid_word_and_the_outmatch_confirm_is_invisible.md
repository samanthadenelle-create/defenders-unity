# WO-1640 - Raid staging: "SPOILS" breaks mid-word into "SPOIL / S", and the two-tap outmatch confirm draws behind the panel that asks for it

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; number block 1637-1642 pre-assigned by the
lead, banner bumped 1637 -> 1643 in the SAME edit)
**Silo / Lane:** UI - `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` and the kit helpers it calls
(`Assets/_Modules/Core/UI/CostFormat.cs`, `ElarionUiKitConformance.ShowToast`). NOT the raid scene,
NOT the raid HUD (that is WO-1639).
**Severity:** P2 felt. Two items on the LAST screen before the player commits to a raid: one broken
word, and one confirm step whose sentence the player never sees.
**Type:** EXISTING system, both halves.
**Owner words:** *"i have mentioned it in testing that it feels incomplete and not polished"*
(2026-09-10).

**WARNING: THE SECOND HALF'S PREMISE WAS WRONG AS BRIEFED, AND THE CORRECTION IS THE TICKET.** The brief said
"BEGIN ASSAULT needed two taps once (first tap only lit the face; not proven systematic - instrument
the tap path)". The device logcat settles it in two lines: the two-tap step is **deliberate, owner-ruled
and systematic**, and the actual defect is that **its explanation is drawn underneath the panel**.
Section 2 has the lines. Nothing needs instrumenting to establish that; sec.5 says what still does.

---

## 1. ITEM A - "SPOILS" breaks mid-word

### 1a. The frame

`Builds/device-frames/2026-09-10_0605_raid_staging.png` (2670x1200, build 363529, Seeker). The right
column of the staging panel, between the POWER / RECON row and SCOUT REPORT, reads:

    SPOIL
        S      1800   1100   2200

Two lines. The `S` of `SPOILS` is alone on the second line. Measured off the original PNG: the word
`SPOIL` occupies roughly x 1605-1700, y 498-522, and the orphan `S` sits at roughly y 530-554 - so
the label had about 95 px of painted width available and the amounts to its right start near x 1720.
The other chips on the same screen (`POWER`, `RECON`, `SCOUT REPORT`, `ARMY 8 / 10`) all render whole.

### 1b. Where it is authored, and why it breaks a word rather than ellipsising

`Assets/_Modules/Village/Hero/RaidDeployScreen.cs:890-892`, inside `BuildSpoilsChips` (`:871`):

    ElarionUiKit.CostRow(plate.transform, DeNelle.Core.UI.CostFormat.Parts(parts),
        new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.90f),
        ElarionUi.Parchment, prefix: "SPOILS", fontPx: 24f);

`SPOILS` is not a `Label` - it is the **prefix argument of `ElarionUiKit.CostRow`**
(`Assets/_Modules/Core/UI/CostFormat.cs:95-131`), forwarded to `AddCostText` (`:113` -> `:133-144`):

    138      text.text = value; text.fontSize = fontPx; text.fontStyle = FontStyles.Bold;
    141      float metricScale = fontPx / 13f;
    142      layout.preferredWidth = Math.Max(28f, value.Length * 8f) * metricScale;
    143      layout.preferredHeight = Math.Max(24f, fontPx + 4f);

Three facts, each read at source:

1. **`AddCostText` never authors a wrapping mode.** It sets text, size, style, colour, alignment and
   `raycastTarget` and stops. TMP's default word-wrap therefore stays live, and TMP breaks a single
   word that cannot fit its line box. Contrast `ElarionUiKitObsidian.cs:971`, where the kit DOES
   author `textWrappingMode = TextWrappingModes.NoWrap` for the wallet amount.
2. **Neither fitter runs on this label.** There is no `FitSingleLine` and no `FitBlock` anywhere in
   `CostFormat.cs`. (The `Spoils unknown` fallback two lines up at `RaidDeployScreen.cs:883` does get
   one. The live path does not.) So there is no ellipsis and no auto-shrink - only the wrap.
3. **The width heuristic undersizes bold caps.** `preferredWidth` for `"SPOILS"` computes as
   `max(28, 6*8 = 48) * (24/13 = 1.846)` = **88.6 reference px**. The 8-px-per-character constant is
   calibrated for the 13 px default size, not for bold caps at 24. `minWidth` is never set, and the
   parent `HorizontalLayoutGroup` runs `childControlWidth = true`,
   `childForceExpandWidth = false` (`CostFormat.cs:110-111`), so when the preferred widths overflow
   the plate the group shrinks every child toward its minimum - which for this label is zero.

### 1c. The authored band, exactly

- `RaidDeployScreen.cs:177` - `private const float SpoilsBandY0 = 0.394f, SpoilsBandY1 = 0.492f;`
  (0.098 of body height). Both drop by `FallbackShift = 0.060f` (`:164`) on the procedural-chrome
  path (`:211`, `:219`).
- `:219` - the band-table row: `new DeployBand("spoils", ColumnRight, SpoilsBandY0 - d,
  SpoilsBandY1 - d, ElarionUi.FontLabel)`.
- X extent: `RightColX0 = 0.51f` (`:182`) to `1.00f`; the plate at `:873` is
  `Well(body, (0.51, Y0), (1.00, Y1))`, and the `CostRow` sits at `(0.04, 0.10)`-`(0.96, 0.90)` of it.
- Canvas reference `1080x1920`, `MatchWidthOrHeight`, match `0.5` (`ElarionUiKit.cs:109-111`).

**A second mismatch, found on the way and worth the lane's eye:** the band table declares this row's
seat font as `ElarionUi.FontLabel = 40` (`ElarionUi.cs:114`) while the row is drawn at **24** - below
`ElarionUiKit.FontFloor = 30f` (`ElarionUiKitObsidian.cs:3033`). So `DeployBand.NeedsPx`
(`RaidDeployScreen.cs:198-200`) is measuring a font this row does not use. Report on it; do not
silently "fix" it by raising the draw size, which would change every amount on the row.

---

## 2. ITEM B - the outmatch confirm is invisible, and that is the whole "two taps" story

### 2a. What the device did, timestamped

`Builds/device-frames/2026-09-10_raid_logcat_stream.txt` - first tap:

    :25544  06:01:48.866  [Flow:Raid] OUTMATCH CONFIRM acknowledged for raid='raider_camp_small'
            (garrison outnumbers the 8 bodies the player can field). The next BEGIN ASSAULT marches.
            This is a confirm STEP, never a refusal - WO-1542 owner ruling.
    :25546  06:01:48.869  [Flow:UI] kit toast -> 'Outmatched: 9 defenders against your 8. Tap BEGIN
            ASSAULT again to march anyway.' tone=Info
    :25547  06:01:48.869  [Flow:Raid] BEGIN ASSAULT: outmatched camp - asked once, did NOT refuse.
            The next tap marches.

Second tap, 29 seconds later:

    :27886  06:02:17.884  [RaidDeployScreen] BEGIN ASSAULT -> SceneRouter.GoRaid('RaidBase_raider_camp_small').
    :27888  06:02:17.884  [Flow:SceneRouter] GoRaid name='RaidBase_raider_camp_small' ...
    :27890  06:02:17.884  [Flow:SceneRouter] LoadSceneWithFade name='RaidBase_raider_camp_small' fade=0.40s

The scene load began in the same millisecond as the second tap. **There was no slow load and no stall.**
The gap is the player waiting, with no visible reason to tap again.

### 2b. Why it is systematic

`RaidDeployScreen.cs:1097-1104` returns early whenever `_vm.NeedsOutmatchConfirm`.
`RaidDeployVM.cs:396-398` defines that as `!_outmatchAcknowledged && RaidSelectionVM.
OutmatchConfirmToast(_def, DeployableCount) != null`; the predicate is `GarrisonCount(d) >
deployableTroops` (`RaidSelectionVM.cs:544-548`, `:748-755`). `raider_camp_small`'s garrison is
7 orc-berserker + 2 orc-shaman = **9** (`Assets/Resources/Data/Canonical/scene-configs.json:106-117`)
against 8 fielded. The selection screen said so in words - frame `..._0604_raid_selection.png` reads
`Outmatched - Army 9 advised` - and the log agrees (`:16023`, `lock="Outmatched - Army 9 advised"`).

So: **every outmatched camp costs one tap, every time the screen is opened.** Not a one-off.

### 2c. The defect: the confirm sentence draws underneath the panel that asked for it

- The staging panel's canvas: `RaidDeployScreen.cs:334-336`,
  `BuildModalCanvas("RaidDeployScreenUI", 31050)` with `overrideSorting = true`, and since WO-1462 it
  carries an opaque 0.94-alpha kit `Backdrop` (`:348-362`).
- The toast's canvas: `ElarionUiKitConformance.cs:393-431`, built at `sortingOrder = 720`
  (default parameter `:394`, applied `:411`), anchored bottom-centre +220 (`:423-425`).

**720 under 31050, behind a 0.94-alpha plate.** Both toasts on this path are affected: the confirm at
`RaidDeployScreen.cs:1100` and `"Assaulting ..."` at `:1113`.

Frame `Builds/device-frames/2026-09-10_0607_arena_01_entry.png` is the staging screen after the first
tap: BEGIN ASSAULT carries a bright gold lit face and **no confirm text appears anywhere on screen**.
That frame is why the brief read the step as "the tap only lit the face".

WARNING: **NOT PROVEN from the frame alone: that the toast was on screen and hidden, versus already expired.**
The frame's wall-clock label is approximate and the toast's lifetime was not read. What IS proven is
the sorting arithmetic above, and that is enough to license the fix - but say which one the lane
confirmed, and do not report "the toast was hidden" as a measurement until a frame or a probe shows it.

---

## 3. Target - what "fixed" means

- Every chip prefix on the staging screen renders as a whole word at every captured aspect. `SPOILS`
  is `SPOILS`.
- A player who taps BEGIN ASSAULT on an outmatched camp SEES, on this panel, what the game is asking
  and what the second tap will do - without leaving the panel and without a second modal.

---

## 4. The fix

### Item A

The safe shape is at the PREFIX, not the amounts. Two candidates; pick from the Step-1 numbers in
sec.5, do not pick now:

- author the prefix `NoWrap` (the kit already does exactly this for the wallet amount at
  `ElarionUiKitObsidian.cs:971`), and/or
- give the prefix a `minWidth` / a width computed from its measured preferred width rather than the
  8-px-per-character heuristic at `CostFormat.cs:142`.

STOP: **Do NOT bolt a fitter onto `AddCostText` without reading the kit law first.**
`ElarionUiKitObsidian.cs:964-967` (WO-697) says a currency value never ellipsises or auto-shrinks. It
is written about the wallet chip's amount label and does not textually bind `CostFormat.AddCostText` -
but it is the same argument and a reviewer will raise it. **A number must not shrink. The prefix is
not a number.** If the change reaches the amounts, say so explicitly in the hand-back.

Note that `CostFormat.CostRow` is shared. Any change to `AddCostText` touches every caller; a change
confined to the prefix branch does not. Prefer the confined one and say which you took.

### Item B

The panel asked a question and the answer went somewhere the player cannot see. Fix the VISIBILITY,
not the confirm step - the two-tap step is an owner ruling (WO-1542, quoted verbatim in the trace at
`:25544`) and stays.

Shapes, in the order of least blast radius:

1. **Say it on the panel.** Paint the confirm sentence into a band on the staging panel itself and
   re-caption the primary face for the armed state. No sorting change, no kit change, and the message
   persists instead of expiring.
2. Raise the toast overlay's sorting above the modal band. STOP: This is a KIT-WIDE change affecting
   every toast in the game and it needs its own justification and its own pins - do not take it
   casually to fix one screen.

**Whatever is chosen, the second tap must still march.** `AcknowledgeOutmatch`
(`RaidDeployVM.cs:407-415`) is the latch and it is not this ticket's to move.

### Also found on this path - RAISE, do not fix here

- **No re-entrancy latch anywhere on the tap path.** `BuildObsidianButton` adds a plain listener
  (`ElarionUiKitObsidian.cs:646`); `interactable` is set once at `RaidDeployScreen.cs:1002` and never
  touched again; neither `OnDeploy`, `RaidDeployVM.Deploy()` (`:420`) nor `SceneRouter.GoRaid` has a
  guard. Two taps start two pipelines. **Not observed in this session** - the device loaded once and
  cleanly - so it is a hazard, not a defect, and it is not in this ticket.
- **`LoadSceneWithFade` shows no loading overlay and the raid path installs no fader.**
  `SceneRouter.cs:266-332`; `ScreenFader.EnsureInstalled` is called only from `BattleArena.cs:593,
  2426, 2976` and `DungeonPortLink.cs:196`. The comment at `SceneRouter.cs:302` claims the screen is
  already covered; on this path it is not. Again NOT what happened here (sec.2a: the load started in
  the same millisecond as the tap), so it is raised, not fixed.
- **A 15-second value that matches the brief's guess but is NOT what happened.**
  `GameStateService.RequestTimeoutSeconds = 15` (`:1876`), awaited inside `SaveBeforeSceneChange`
  before the fade. It would produce a live, re-tappable staging screen for exactly 15 s. **This
  session's trace rules it out** - there is no `LoadSceneWithFade` line 15 s before the load. Recorded
  so the next reader does not re-derive it as the cause.

---

## 5. Instrument first - the two seams that are still dark

Per CLAUDE.md sec.12 these go in BEFORE the edit and STAY in afterwards.

1. **`OnDeploy` entry has no trace.** `RaidDeployScreen.cs:1056` - the happy path's only record is a
   `Debug.Log` at `:1114`. Every refusal branch traces; arrival does not, so "tap not received" and
   "tap received and swallowed" are indistinguishable except by inference. Add one `FlowTrace.Step`
   at entry naming the tap and the branch it is about to take.
2. **Item A's real numbers.** Before choosing between the two shapes in sec.4, print for the prefix
   label: `preferredWidth`, the rect width it actually resolved to, `textInfo.lineCount`,
   `textInfo.characterCount` vs `text.Length`, and the wrapping/overflow modes as they stand at
   render. That is the same measurement WO-1628 sec.4 Step 1 established as the pattern for a fit
   defect, and it decides whether the band or the heuristic is the cause.

Compute interpolated parts into locals before building either string - the gate's brace scanner has
no interpolated-string model (CLAUDE.md sec.1).

---

## 6. Acceptance

1. **The frame.** A fresh staging capture at the device aspect showing `SPOILS` whole on one line
   with the three amounts beside it. Paste what the row reads. The owner's standing rule is
   *"I want images to verify anything that is a viewable issue"*.
2. **The other prefixes did not regress.** Every other `CostRow` prefix in the game still renders
   whole - name the call sites checked.
3. **The confirm reads.** A capture (or a device frame) of the staging panel in the armed state
   showing the outmatch sentence where the player can see it. Paste the text.
4. **The second tap still marches.** A trace showing `:1101`'s Step on tap 1 and `GoRaid` on tap 2,
   unchanged.
5. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 7. Pins - what must not move

- `Assets/Editor/Regression/RaidDeployUiRegression.cs:369-389` - the button is built by
  `BuildObsidianButton` (`:369`), Yellow face (`:375`), the literal `"BEGIN ASSAULT"` (`:376`), wires
  `OnDeploy` (`:379`), two CTAs on one row (`:386`).
- `Assets/Editor/Regression/RaidDeployZeroArmyRegression.cs:201` - `RaidDeployVM.PrimaryAssaultLabel`
  must equal `"BEGIN ASSAULT"`; `:248-260` - the literal appears EXACTLY once and only inside the
  `troops > 0` branch; `:367-374` - the `Fielded <= 0` refusal precedes `SceneRouter.GoRaid(` in
  `Deploy()`.
- STOP: **`RaidDeployVM.cs:374-380`** - `CanDeploy`, `ShowAssault` and `Deploy()` gain NO new refusal
  branch, and HeartfireRegression PIN F reds this file for readiness-shaped gates (WO-1379 / WO-1403).
  Any debounce added here must be worded as a TAP DEBOUNCE, never as readiness.
- `RaidDeployLayoutRegression.cs:191`, `:596-663` (case `[vm-spoils-chips]`) - one producer, three
  chips, no parsed string. The DATA is pinned; the label is not, which is why this shipped.
- The WO-1542 two-tap confirm ruling itself (`RaidDeployVM.cs:396-415`,
  `RaidSelectionVM.cs:544-548`, `:748-755`).

**Coverage gap to close:** `CostRowFitRegression.cs:126-128`, `:250` probes only the DEFAULT `fontPx`
with a short `"NEED"` prefix. Nothing covers `fontPx: 24` with a 6-character prefix - exactly this
row. Add that case; per `LayoutOracle.cs:17-20` any new rule must be SEEN RED first against the
unfixed code, so write it before the fix and paste the red.

---

## 8. What NOT to touch

- The raid HUD inside the arena - readout panel, DEPLOY face, HERO DOWN, chevron. **That is WO-1639**,
  a different file (`RaidHudController.cs` / `RaidDeployController.cs`) and a different lane. Do not
  widen into it.
- `RaidSelectionVM` / the selection grid / `RaidSelectionSpoilsRegression`.
- The garrison data in `scene-configs.json` (read-only here; `WO-1634` owns those arrays).
- `SceneRouter`, `GameStateService.SaveBeforeSceneChange`, `HeroContentPrewarmer` - all read-only,
  all raised in sec.4 rather than changed.
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 9. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1640_raid_staging_spoils_label_breaks_mid_word_and_the_outmatch_confirm_is_invisible.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.
