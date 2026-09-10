# WO-1521: a quest counter says one is ready to claim while the rumor board says the board is quiet

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (prior: READY TO IMPLEMENT - owner report 2026-09-06 20:18)
**Silo:** Village quests / rumor board - the quest service and its claim surface, `QuestRewardBridge.cs`,
`JourneyDeckSubtitleVM`, and the rumor board panel.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1521 -> 1522 in the same edit).

## 1. EVIDENCE

Owner report, verbatim:

> "quests say one quest to claim but no idea how or what to do to complete it"

Device frame `Logs/device/screens/owner-screen-20260906-201850.png` (build 358574, 20:18):

```
"Brom's Rumor Board"     PREVIOUS / NEXT / CLOSE
centre card              "The board is quiet. / Brom posts more as Elarion wakes."
quest rows               NONE
backdrop                 ABSENT - the town bleeds through (same class as WO-1462)
```

The counter that contradicts it is composed here:

```
Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs:21
  QuestsSubtitle = activeQuests + " active . " + readyToClaim + " ready to claim";
```

So two surfaces read two different quest states: one says something is ready, the other says there is nothing.
Nothing anywhere names the quest, its objective, or where to claim it.

**Correction to WO-1477:** PREVIOUS **already exists** on this screen. WO-1477 (rumor board PREVIOUS button)
must verify at source before adding a second one - the owner's "a previous button would be nice" may have been
about a different surface, or about the button not working. Noted in that ticket too.

## 2. FIX SHAPE

- **ONE quest authority feeds both surfaces.** The counter and the board read the SAME list. Do not add a
  second list to fix the disagreement.
- A **CLAIMABLE** quest renders on the board as its own row: objective text, reward, and a CLAIM door through
  `PanelRouter` / the VM verb - never a second path.
- An **ACTIVE** quest renders with its objective and a GO-TO door to the place that completes it. That is the
  half the owner's "no idea how or what to do" is actually asking for.
- `"The board is quiet."` paints ONLY when the list is empty.
- The counter's tap opens the board scrolled to the claimable row.
- Backdrop from the kit (shares WO-1462's fix).

## 3. WHAT NOT TO DO
- Do not add a second quest list or a second claim path.
- Do not auto-claim. She wants to know what to do, not to have it done.

## 4. ACCEPTANCE
- [ ] Measured case: a claimable quest in state produces a board ROW carrying its objective and a CLAIM door.
- [ ] Measured case: the counter and the board agree on the count, from the one authority.
- [ ] Measured case: the quiet copy NEVER paints while the list is non-empty. RED today.
- [ ] Door assertions follow `WelcomeBackDoorsRegression`'s pattern.
- [ ] Headless `RumorBoard_*.png` captured and opened.
- [ ] `REGRESSION_OK n/n` on a fresh log.

---

## 5. IMPLEMENTED 2026-09-06 (edit-only lane - NOT gated, NOT committed)

**One authority.** `DailyQuestService` now owns the claimable fact: `IsClaimable(q)` (the predicate),
`ClaimableCount` (the number), `TodayQuests`, `Find(id)`. `JourneyDeckSubtitleVM.FromCurrentState`
reads `ClaimableCount` instead of its own inline `Completed && ClaimedAtUnix == 0` loop - that copy
was the disagreement.

**One list.** `RumorBoardVM.Rebuild` composes `Rows` = CLAIMABLE dailies, then ACTIVE story quests,
then AVAILABLE offers. Claimable leads so the counter's tap lands on page 0. `AvailableQuests` stays
as the available-only subset. Paging (`PageCount` / `BuildPage`) moved from `_available` to `_rows`.

**One verb per poster.** `KindOf` / `ActionLabelFor` ("Claim" / "Go To" / "Accept") / `Invoke`. The
View calls `Invoke` only and never branches on kind. `ObjectiveFor` gives an ACTIVE row its CURRENT
stage objective and a CLAIMABLE row its finished job - the half the owner's report was asking for.

**The CLAIM door is real, and there is still exactly ONE payer.** `DailyQuestService.RequestClaim`
raises a new `ClaimRequested` event; `DailyQuestRewardBridge` subscribes it with the SAME
`HandleQuestCompleted` handler it already uses for `QuestCompleted`, so the `_payingOut` re-entrancy
set and the `ClaimedAtUnix` latch are the existing ones. `RequestClaim` returns the payer's VERDICT
(the latch landed), never "an event was raised" - a claim that credits nothing keeps the row and the
board says why (WO-978's full-bank case).

**The GO TO door.** `RumorBoardLiveBackend.GoTo` routes through `PanelRouter` when the active stage's
`completeOn` is kind `panel` (quests.json ships PanelId names verbatim: BuildingUpgrade / Crafting /
Inventory / JewelerCrafting / RumorBoard). Every other completion kind happens in the world, so it
returns false and the VM pins the quest to the HUD tracker and says so. No invented destinations.

**The quiet copy.** `RumorBoardPanel.Repaint` gated `BuildEmptyNote()` on `shown == 0` - THIS PAGE.
It now gates on `RumorBoardVM.IsQuiet` - the whole LIST. That single token is the acceptance line
"the quiet copy NEVER paints while the list is non-empty".

### Files
- `Assets/_Modules/Core/Quests/DailyQuests.cs` (claim seam + the one count)
- `Assets/_Modules/Village/Quests/DailyQuestRewardBridge.cs` (subscribe `ClaimRequested`)
- `Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs` (read the one authority)
- `Assets/_Modules/Village/Hero/RumorBoardVM.cs` (rows, kinds, objective, Claim/GoTo/Invoke)
- `Assets/_Modules/Village/Hero/RumorBoardPanel.cs` (one door, VM-named face, IsQuiet gate)
- `Assets/Editor/Regression/RumorBoardLayoutRegression.cs` (source-laws re-pointed, see below)
- `Assets/Editor/UICaptureLaunch.cs` (`WorstCaseRumorBackend` implements the new seams; the capture
  fixture now carries one claimable daily so every RumorBoard shot proves all three row kinds)
- `Assets/Tests/EditMode/RumorBoardVMTests.cs` (5 new measured cases; one rewritten - see below)

### Pins updated in the same edit (they would otherwise FAIL on this change)
- `[source-laws]` required the literal `BuildObsidianButton(... "Accept")`. The door's face is now a
  VM projection, so the law is `... ActionLabelFor`, plus new asserts that `ActionLabelFor`/`Invoke`
  exist, that the View never calls a specific verb, and that the empty gate is `IsQuiet`.
- `[source-laws]` forbade `public void Track(` anywhere in `RumorBoardVM.cs`. The GO TO fallback pins
  through the BACKEND seam, so the rule is re-pointed at a Track COMMAND on the VM and now also
  requires `GoTo` and `ClaimDaily` to exist.
- `accepting_the_last_rumor_on_a_page_walks_the_page_back` was rewritten: ACCEPT no longer removes a
  row (it becomes an ACTIVE row), so the page-shrink it guards is now driven by COMPLETION.

### NOT DONE, and why
- **Backdrop.** UNPROVEN, so untouched. WO-1462's shape does NOT apply here: `RumorBoardPanel` calls
  `ElarionUiKit.BuildObsidianModal`, which calls `BuildObsidianPanel` with `withBackdrop` at its
  default `true` (`ElarionUiKit.cs:568,573-579`), so a 0.94-alpha full-screen "Backdrop" IS built,
  and `MedievalUiSkin.ApplyShell` never touches `chrome.backdrop`. Nothing found in a source read
  removes or hollows it. The device frame plainly shows the town, so something does - but naming it
  from static reading would be the inference-fix CLAUDE.md sec.12 bans. **Needs one runtime capture**
  (a hierarchy dump of `RumorBoardPanelUI` with the Backdrop's `Image.color.a` and `activeInHierarchy`)
  before any edit. Split it to its own ticket rather than guessing here.
- Gate, headless `RumorBoard_*.png`, `REGRESSION_OK n/n`: this was an EDIT-ONLY lane with no Unity.

### P0 FOUND WHILE BUILDING THE DOOR - the latch was never persisted

`DailyQuestService.Report` calls its private `Save()` **before** `QuestCompleted?.Invoke(q)`, and the
bridge writes `ClaimedAtUnix` from inside that handler. Nothing saved afterwards. So a daily paid as
the last act of a session **reloaded as `Completed && ClaimedAtUnix == 0`** - i.e. CLAIMABLE. Until
today that only made the counter lie (a third candidate for the owner's screen, alongside WO-978's
zero-credit case). The moment WO-1521 gives that state a CLAIM face it becomes a **DOUBLE GRANT**, so
`Report` now calls `Save()` after the completion handlers run - the same rule `RequestClaim` follows.

Also fixed in the same pass: `RumorBoardLiveBackend.Changed` wired only `QuestService.QuestChanged`,
so a claimed daily would have sat on the board until the panel was reopened. It now wires
`DailyQuestService.SetChanged` too.
