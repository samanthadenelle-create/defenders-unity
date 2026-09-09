# WO-1534: the raid loop never closes, and three Manage tickets are DONE against a design that was reversed

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:55, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY - PARTIAL: one gate run owed, plus the §G4 owner felt checks. **Measured at HEAD
2026-09-07 (§G): Parts A1-A6 and B2-B5 ALL LANDED** in `d6511b8e5` / `c0c30f715` while this ticket sat;
B1 landed 2026-09-06 (RESULT file). The only code item left is `BuildInventoryFilterRegression` **case 7**,
written this lane and **unrun** — flip on `REGRESSION_OK <n>/<n>` from a fresh log. Both status words are
deliberate and were checked against `tools/board_build.py` at source, not against a doc: the `READY` lead
returns before the substring pass that would otherwise read the word DONE out of this very sentence, and
`has_landed_partial` keys on the literal token `PARTIAL`, so dropping it would have cost the row its
landed sub-badge and rendered nine landed parts as an untouched ticket.
Minted 2026-09-06 from the owner's ask, *"can you review the manage
sections and the raid UX screens and make better"* (follow-up: *"read only detailed WO"*, so nothing was
edited and this file is the deliverable).
<!-- Status wording note (WO-1534 B1, 2026-09-06): the dispatched wording began "PART B1 DONE". It is
     deliberately NOT used. `tools/board_build.py:158-160` only honours a CANONICAL FIRST WORD; "PART" is
     not one, so the row falls to the substring pass at `:183`, where BOTH `has_result` (a RESULT.md now
     exists) and `"DONE" in s` are true - and the ticket's five OPEN parts would render green Done. That
     is the "error only ever ran one way: toward finished" failure the file's own comment at `:143-145`
     describes. A READY lead returns at `:160` before the fallback, and `has_landed_partial` (`:198-206`)
     picks up PARTIAL for the sub-badge, so the slice still reads as landed. -->

**Silo:** TWO parts, deliberately disjoint.
- **PART A (code)** — the raid flow's REPORTING and NAVIGATION seams: `RaidSelectionScreen` /
  `RaidSelectionVM`, `RaidDeployController` (exits only), `RaidVictoryController`, `EndStateVM` /
  `EndStateView`, and the `ManageScreenVM` army summary.
- **PART B (record first, then code)** — the Manage 2000-block's board integrity, then five uncovered
  Manage UX defects.

**Deliberately NOT the raid's layout or art** — WO-1462 / 1463 / 1464 / 1519 hold those on the same
files. **LANDS AFTER** the wave-two gate carrying the uncommitted `RaidDeployScreen.cs` /
`RaidDeployController.cs` / `RaidScoring.cs` / `ManageScreenPanel.cs` / `ManageScreenVM.cs` edits.
**Source:** read-only review 2026-09-06 (CLI seat) — two read-only audit agents plus the seat's own
verification pass. **Every claim below was re-read at source by the CLI before it was written here**
(memory `cli-gatekeeper-agent-role-model`); nothing rests on an agent summary, a doc, or a memory.
Minted from the banner (`CLI_LANES_WO_NUMBERS.md`, main line 1534 -> 1535 in the same edit).

---

## 0. THE FINDING BEHIND BOTH PARTS

A review that returned a defect list would have been worthless here. The Manage screens are owned by the
WO-2001..2017 program and ten tickets minted the same day; the raid screens' layout defects are all in
flight. So the review asked a different question — *where does the game, or its record of itself, say
something that is not true?* — and both halves answered it.

> **PART A — the raid loop never closes.** At every seam where the game should tell the player what they
> achieved or where to go next, it is silent; and at the two seams where it does speak, it contradicts
> itself.
>
> **PART B — the Manage board says DONE for a design that was reversed.** A later owner mockup replaced
> the tab-based IA with a hub, and **nothing recorded the reversal.** Three tickets still read
> DONE/FIXED-verified against acceptances the shipped screen does not meet.

Part B is why Part A's review nearly shipped four false defects (§C). **It is the higher-leverage half**,
because while it stands, every future Manage felt-test re-discovers "fixed" things unfixed, and every
seat reading the canon derives defects that do not exist. Recommend Part B's §B1 lands first; it is
documentation only and costs an hour.

⛔ **NO NEW SCREEN AND NO NEW ART IS PROPOSED ANYWHERE IN THIS FILE.** Every fix is a word, a door, a
condition, or a status line. That is deliberate — the audit kept finding the disease CLAUDE.md §11B and
`OWNER_RULINGS_LOCKED.md` ruling 25 both name: **the capability is built and the door is missing.**

---

## 1. EVIDENCE BASE — and its hard limit

- `Logs/device/screens/seeker-357453-raids.png` — raid selection grid, build **357453**
- `Logs/device/screens/seeker-357453-raid-deploy.png` — deploy modal, build **357453**, same camp
- `Logs/device/screens/owner-screen-20260906-201443.png` — deploy modal, build **358574**, 20:14
- `Logs/device/screens/owner-raid-ui-2026-09-06-143701.png` — in-raid HUD, 14:37
- `Logs/device/screens/owner-screen-144143.png` — **Manage/BUILD, 14:41** — carries a
  **BUILD | ARMY | RESEARCH tab row**
- `Builds/ui-capture/ManageFlow_*_2670x1200.png` — **18:39** — the same screen with **no tab row**

⛔ **NO CAPTURE POSTDATES THE TREE.** Newest frame **18:39**; commit `949e848a0` ("all nine screens match
the owner's mockup") **18:51**; `ManageScreenVM.cs` uncommitted at **20:47**. **No frame in the repo
shows the current Manage code.** With WO-1489 (the capture plan cannot see four of nine screens), a
purely visual Manage review cannot be evidence-based right now. Every acceptance below that needs a
frame must capture a **fresh** one.

⛔ **The `seeker-357453-*` Manage frames (00:58) are the OLD rail+card design** (WO-1418 / 1422 shape), a
different build family. Do not mix them with the 18:39 grid frames as evidence for the same screen.

**There is no post-raid result PNG in the repo at all.** §A3, §A4 and §A5 are proven from source only,
and their acceptance therefore REQUIRES a new capture.

---

# PART A — THE RAID LOOP NEVER CLOSES

Walk it as a player:

| Step | What the game does | § |
|---|---|---|
| Build an army in Manage | names the camp you are training for, and offers **no way to reach it** | A1 |
| Open the raid grid | says **`LOCKED - needs Army 9`** | A2 |
| Tap that locked camp | **opens anyway**; `BEGIN ASSAULT` is live | A2 |
| Retreat, or run out the clock | **no result screen at all** — you are simply in town | A3 |
| Win | the result screen **leaves on a 12 s timer you cannot stop** | A4 |
| Win again | ladder progress **is never announced** | A5 |
| Return to the grid | the cleared camp **looks exactly like one you never fought** | A6 |

Six seams, one class. Each has its own acceptance and they are separable for scheduling, but they should
be **ruled on together** — three are design calls whose answers interact (§D).

### A1 — Manage names the camp you are fighting for, and gives you no way to reach it

`ManageScreenVM.BuildTroopArmySummary()` (`:1947-1986`) composes what is arguably the most motivating
sentence in the game — `Army 8 / 10 - The Forsaken Camp fields 12` — and `ManageScreenPanel.cs:3950`
renders it with `ElarionUiKit.Label(...)`: **a label, not a button.** The game names your enemy, counts
their garrison, and offers nothing to press. The reverse direction exists — `RaidDeployScreen.cs:857`
opens `PanelId.Manage, "Troops"` when you are short (WO-1403). **The thread is one-way.**

⛔ **AND IT IS COMPOSED TWICE, WHICH IS THE ACTUAL DEFECT.**

| Producer | Output | Second clause derived from |
|---|---|---|
| `JourneyDeckSubtitleVM.cs:22` | `Army 8 / 10 . 2 camps open` | `PostureSignals.RaidOpenCampCount` |
| `ManageScreenVM.cs:1978-1984` | `Army 8 / 10 - The Forsaken Camp fields 12` | its **own** `RaidSelectionVM` walk (`:1965`) |

Different separators, different clauses, two derivations of "which camp is next". The comment twelve
lines above the first names this exact failure — `PlayerDeckWorkspace.cs:719-723`: *"ONE rule, TWO
surfaces... a second check would drift from the first, and the drift is the actual defect."* And
`PostureSignals.cs:333-336` states why the publish-a-count pattern exists: *"rather than making the
Journey card reach across the assembly boundary or duplicate the camp predicate."*

⚠ **PRECISION:** `ManageScreenVM` is in `DeNelle.Village`, so building a `RaidSelectionVM` is **NOT an
assembly violation.** It breaks the one-producer rule, not the boundary. Do not "fix" it by moving code.

⚠ **THE FIX IS NOT A ONE-LINE READ.** `PostureSignals` publishes only a **count** (`:337`), never a named
camp. Owner call (§D1): **(a)** the authority gains a published *next camp* fact both surfaces read, or
**(b)** Manage drops the name and uses the count. (a) is the stronger game; (b) is cheap.

**Typography, same line:** drawn at `ElarionUi.FontMicro` (32) — which `ElarionUi.cs:115` reserves for
*"hotkey badge, rune strip"*, the smallest authored role — in `ParchmentDim`, in a 26 px band. The most
motivating sentence on the card is ranked last. ⛔ Do **not** pay for it by shrinking a neighbour:
`ManageScreenPanel.cs:3941-3949` records this band was already starved to 18.2 px once, which culled the
whole line, and ends *"Never re-shrink a text band below ~24px on this card."*

**Acceptance**
1. Exactly ONE producer composes army-vs-raid readiness copy; the other reads it. An oracle fails the
   build if a second appears (the WO-1521 `ClaimableCount` shape).
2. The Manage/ARMY line is a door to the raid grid, **or deliberately is not** per §D1 — recorded either way.
3. The line is not `FontMicro`, and its band is not shrunk below 24 px to pay for it.

### A2 — `LOCKED - needs Army 9` is a word the door does not honour

`armyWord` appears at `RaidSelectionScreen.cs:993`, `:996-999` and `:1023` — **card face and one log
line, nowhere else.** It is display-only. `OnCardTapped` (`:1060-1132`) refuses on exactly two
conditions — the escalation lock and Heartfire (`:1115-1127`) — then falls through to
`RaidDeployScreen.Open(def)` at `:1130`. Downstream `RaidDeployVM.CanDeploy` (`:129-132`) tests **scene
name + Build Settings only**, and `RaidDeployScreen.cs:760-762` states the footer asks *"never
`Snapshot.Ready`"*.

**Same build, same camp:** `seeker-357453-raids.png` reads `LOCKED - needs Army 9`;
`seeker-357453-raid-deploy.png` reads `you field 8` under a lit `BEGIN ASSAULT`.

⚠ **NEITHER SIDE IS A BUG ALONE, WHICH IS WHY IT SURVIVED.** WO-1402 authored the word as a row label;
WO-1403's RESULT *deliberately* decoupled the deploy footer from readiness *"so the first-raid soft gate
stays at the ONE door."* Both correct; nobody reconciled them. ⛔ **Do NOT restore a readiness check
inside the deploy screen** — that is the second-gate shape WO-1379 forbids and `HeartfireRegression`
PIN F reds the file for.

⛔ **AND THE CARD IS NOT EVEN DIMMED.** `:811` — `bool dimmed = locked;` — with the comment *"Locked is
the ONLY dimmed state left on a card (WO-1379 retired the cooldown dim)."* `locked` is the **escalation**
lock; `armyLocked` (`:999`) is a separate boolean that feeds the face text and nothing else. So an
army-locked card **renders at full brightness, like any available camp, while its own face reads
`LOCKED`.** The word, the styling and the door disagree three ways.

⚠ **This also narrows the contract argument, so state it correctly:** the *"the card stays TAPPABLE on
purpose... OnCardTapped answers with the refusal"* comment (`:1048-1054`) sits **inside `if (dimmed)`**,
so it was written about escalation locks and does not promise anything for the army word. **The defect is
not a broken promise — it is that the strongest word on the card implies a refusal the tap never gives,
and the styling never signals.**

**Acceptance:** the grid word and the door agree by whichever rule §D2 picks, stated once in the VM; the
soft first-raid gate still lives at the ONE door and PIN F stays green; an oracle pins that a card whose
face says LOCKED cannot silently open.

### A3 — Retreat and clock-expiry end the raid with NO result screen. **P0.**

`RaidDeployController.DoRetreat()` (`:752-763`): `SettlePartialLoot` -> `ReconcileRaidEnd(0)` ->
`TroopRally.Clear` -> `Save` -> `SetStatus("Retreating to the castle...")` -> `SceneRouter.GoCastle()`.
No `EndStateVM`, no `EndStateView.Show`. **`grep -c "EndStateVM\.\|EndStateView.Show"` on that file
returns 1, and the single hit is a comment at `:290`.** The timeout exit funnels into the same method:
`OnRaidTimeExpired()` (`:401`) sets a status string and calls `DoRetreat()`.

⛔ **AND NOTHING PICKS IT UP IN TOWN EITHER — this was checked, because "deferred to town" would have
downgraded this finding.** `RaidResult` is the settled outcome object (`RaidScoring.Finalize`,
`:916-939`), and **every reader of it is raid-scene-side**: `RaidDeployController.cs:800, :808`,
`RaidScoring.cs`, `RaidVictoryController.cs:259`. Grepping `RaidResult` across `Assets/_Modules` returns
no town-side consumer, and the "welcome-back" surfaces are the offline-harvest popups
(`ResourceCollectorService`, `HarvestOverflowModal`), which are about collectors and never mention a
raid. **The outcome is computed, banked, and then discarded unread.**

A player who retreats — or simply runs out the 3:00 clock — is **teleported into town with no screen.**
They have earned real loot (`SettlePartialLoot`) and can have earned a star (`RaidScoring.cs:455`, 1 star
at `destructionPct >= 0.5f`), and are told **none of it**: not razed %, not stars, not spoils, not which
troops came home wounded. A won raid gets the full treatment (`RaidVictoryController.cs:765`).

⚠ **This is the retention seam** (memory `retention-is-the-business-problem`). The most likely raid a new
player finishes is one they lose — and that is the one the game says nothing about.

**Not covered by WO-1437** (`raid_never_ends_softlock`): it proves the three exits *fire*; its §4 asks
only whether the session terminates. **Nothing in it asks an exit to REPORT.** WO-1526 changes *when*
hero death settles, not *what* a non-victory exit shows.

**Acceptance**
1. Every non-victory exit (retreat, timeout, and — after WO-1526 — the capped hero-death clear) shows a
   result naming razed %, stars, spoils **actually banked**, and troops lost/wounded.
2. It reuses `EndStateVM` / `EndStateView`. **No new screen.**
3. It reports what was BANKED, not promised — must not re-open WO-1461 (deploy card quotes 1,800 wood,
   25 arrives).
4. A fresh capture of the retreat screen is attached to the RESULT.

### A4 — The victory screen leaves after 12 seconds and cannot be stopped

`RaidVictoryController.cs:61` `_autoReturnSeconds = 12f` -> `EndStateVM.cs:416` `AutoDismissSeconds` ->
`EndStateView.cs:989-990` -> `:2628-2632` `WaitForSecondsRealtime(...)` then `FirePrimary()`.
**There is no cancel:** `EndStateView.cs:2771` states the file contains no `StopAllCoroutines` /
`StopCoroutine`. No tap, no scroll stops it.

In 12 s the player must read the star result, up to five spoils rows, a companion-join line, and — when
the bank overflowed — *"Some of the reward could not be paid out"* (`RaidVictoryController.cs:782-784`).
That last is precisely the message a player must not miss, on the screen that leaves by itself.

⚖ **STATED FAIRLY: this is a deliberate choice, not a bug.** `:753` calls it *"the anti-soft-lock
guard"*, and it exists because an end-state that never dismisses can strand a player. The codebase
already knows how to opt out — `EndStateVM.cs:379` sets `AutoDismissSeconds = 0f`, *"deliberate: no
softlock-guard here — Retry must be chosen."* **So the question is not "remove it"** but whether 12 s
reads a five-row screen, and whether a touch should hold it. WWCD: Clash of Clans waits indefinitely.

**Recommendation to react to, NOT a decision:** keep the guard, raise it, and **cancel on first touch** —
the player who is reading is the player who is touching, and the one who walked away is exactly who the
guard is for.

**Acceptance:** the screen does not route home while the player is interacting; the anti-softlock guard
still exists (**do not delete it**); the overflow caveat is legible for as long as the player wants.

### A5 — Winning never tells you what the win unlocked

`RaidVictoryController.ResolveUnlockLine(victories)` (`:814-821`) **returns `null` unconditionally**, and
its own trace says so: *"no ladder gate is wired into this seam yet... only the announcement is unowned
(section-4 thresholds belong to the ladder lane, not to this file)."* **That lane is WO-1375, CLOSED
2026-09-06, and its RESULT does not claim this seam** — so the seam is not deferred, it is orphaned.
Meanwhile the grid advertises the ladder, the win is counted (`:344-361`), and the win screen is silent.
`PostRaidBeatTokens.cs:130-147` shows only the **first-raid tutorial dialogue** ever speaks it.

**Acceptance:** a victory crossing a threshold announces it in words on the existing screen; one crossing
nothing stays silent, and the trace still distinguishes the two so the absence stays observable in a
capture (the property `:810-812` was written to preserve).

### A6 — A cleared camp is indistinguishable from one you have never fought

`RaidClaimService.MarkClaimed` persists the win (`RaidVictoryController.cs:685`). Grepping
`RaidSelectionVM.cs` / `RaidSelectionScreen.cs` for `RaidClaimService|IsClaimed|Cleared` returns
**comments only** (`RaidSelectionVM.cs:50,52,73`) — **no functional read.** The row's bottom band falls
through to `RewardHint` -> `"- x1 Loot"` (`RaidSelectionScreen.cs:998, 1167-1174`). The return leg of the
loop has no memory, and nothing warns that a repeat clear pays a fraction — which WO-1461 sets at 60%.

**Not covered by WO-1461**, which puts repeat-clear economics on the **deploy card** and never touches the
grid row. A player choosing among four camps chooses on the **grid**, one screen earlier.

⚠ **Not proven:** whether any camp was claimed on the save behind `seeker-357453-raids.png`. The PNG
corroborates; the source read is the proof.

**Acceptance:** a cleared camp is marked as cleared on the grid row, read from `RaidClaimService` (never a
second claim predicate); the row states the repeat rate or points at it, **before** the deploy card.

---

# PART B — THE MANAGE RECORD DISAGREES WITH THE MANAGE SCREEN

## B1 — Three tickets are DONE/FIXED against acceptances the shipped screen does not meet. **Do this first; it is documentation.**

A later owner mockup (`docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png`) replaced the tab IA with a hub.
**The reversal is LEGITIMATE — the owner's mockup is the spec.** The defect is that **nothing recorded
it**, so three tickets still certify the superseded design:

| Ticket | Status | Its acceptance | What ships | Proof |
|---|---|---|---|---|
| **WO-2001** *"Replace Manage Hub With Direct BUILD/ARMY/RESEARCH Tabs"* | **DONE — verified** | `:71` *"BUILD, ARMY, RESEARCH are one tap from each other"* | **no tab row**; navigation is hub + back arrow | `ManageWorkspacePanel.cs:412` — *"⛔ AND THERE IS NO TAB ROW AT ALL ANY MORE — `BuildTabs` IS DELETED, NOT EMPTIED"* |
| **WO-2005 RESULT** | **FIXED** | `:16` *"**6 filters implemented:** ALL, ECONOMY, DEFENSE, CRAFT, STORAGE, CIVIC"* + a CIVIC membership table at `:26` | **five** chips | `BuildFilter.cs:87-90` `Chips = { All, Economy, Defense, Craft, Storage }`; `:59` *"⛔ THERE IS NO CIVIC CHIP. Do not add one back."* |
| **WO-2006** | **DONE — verified** | `:64` *"≥12 tiles visible when inventory/filter size allows"* | **5 × 2 = 10**, authored | `ManageScreenVM.cs:3500-3507` |

**The tab reversal is independently visible in two frames three hours apart:**
`owner-screen-144143.png` (14:41) carries the BUILD | ARMY | RESEARCH row;
`ManageFlow_BUILD_gridtop` (18:39) does not.

⚠ **BE FAIR TO WO-2005 — ITS RESULT WAS TRUE WHEN IT WAS WRITTEN, AND THIS WAS CHECKED.**
`git show a6bbc523d:Assets/_Modules/Core/Catalog/BuildFilter.cs` has `Civic` at `:59` and **six** entries
in `Chips` at `:76`. So CIVIC genuinely shipped in WAVE 0 (11:20); `32659c0f6` (16:51, *"the Manage
screens rebuilt against the owner's mockup"*) removed it five hours later. **Nobody wrote a false claim.
The RESULT went stale under a legitimate reversal and nothing bannered it** — which is the whole point of
§B1 and exactly the failure §15 exists to prevent. Treat all three rows this way: not as bad work, as an
unrecorded reversal.

⛔ **A CLAIMED CONSEQUENCE OF THIS — "barracks, Store, Echo Hollow and Healing Caravan are now reachable
only under ALL" — IS FALSE, AND IT REACHED THE OWNER AS A QUESTION BEFORE IT WAS CHECKED. Recorded here
because the mistake is the instructive part.** Measured in `structures-catalog.json` on 2026-09-06:
`healing_caravan -> [DEFENSE]`, `pet-house -> [ECONOMY]`, `market -> [ECONOMY]`, `arcane-tower -> [CRAFT]`,
`barracks -> [DEFENSE]`. **Zero rows carry a CIVIC token**, and the only rows with no membership at all are
`deco_torch` and `repair_default` — which `BuildFilter.cs:59-73` explicitly excludes as *"not player
content"*. **The data already implements the re-homing that file documents; nothing is stranded under ALL
and no re-home is owed.** The lesson is §11B's: the CLI verified the three table rows above at source and
then passed this one consequence through from an agent summary unverified. **A claim is hearsay until it is
re-read at source — including the ones that merely sit next to proven claims.**

> **THE LAST LINK, WALKED AT SOURCE 2026-09-06 (WO-1534 B1).** The data being right only refutes the claim
> if the chip actually READS it, so the whole chain was opened rather than assumed:
> `CatalogEntry.manageFilters` (`Assets/_Modules/Core/Catalog/CatalogEntry.cs:87`) ->
> `BuildInventoryModel.Reconcile`, `Filters = e.manageFilters ?? Array.Empty<string>()`
> (`Assets/_Modules/Village/BuildMode/BuildInventoryModel.cs:283`) -> `BuildInventoryModel.For(chip)`,
> which matches a row against the chip on that array (`:200-206`) and rejects a non-chip with
> `FlowTrace.Fail` rather than a wrong list (`:193-199`) -> `Tiles(chip)`, narrowing to Offered (`:229-234`)
> -> `ManageScreenVM`: `_inventoryTiles = BuildInventoryModel.Tiles(_activeFilter);` (`ManageScreenVM.cs:3905`),
> with `_activeFilter` set from the tapped chip at `:3248` and validated against `BuildFilter.Chips` at
> `:3242`. `BuildFilter.Matches` (`BuildFilter.cs:121-130`) applies the same rule for callers that hold an
> entry. **So the ruling at section D4 ("re-home the four service structures") is already satisfied end to
> end by the tree and owes NO code change** - barracks and Healing Caravan under DEFENSE, Store and Echo
> Hollow under ECONOMY, the Cathedral under CRAFT, none of them on the scrolling filter alone.

**The canon carries the same rot**, and it is what made this review nearly ship two false defects (§C):
`OWNER_RULINGS_LOCKED.md:7` still says CIVIC; `00_MANAGE_REDESIGN_CANON.md:19,61` still says "at least
12 visible tiles". Both supersessions are recorded **only** in `BuildFilter.cs:59-73` and
`CAPTURE_LOOP_GOAL.md:82` — **neither canon file carries a `STALE:` banner**, which CLAUDE.md §15
requires. This is the duplicated-state failure §2 (stale WO block), §5 (retired dependency table) and
§16 (copy-pasted R2 verify) each describe in their own words.

**Acceptance**
1. WO-2001, WO-2005 and WO-2006 carry a banner naming the mockup that superseded their acceptance, and a
   status that no longer certifies an unmet criterion. ⛔ **Do NOT rewrite the ticket bodies** — §15
   freezes them; banner them.
2. `OWNER_RULINGS_LOCKED.md` rulings 5 and 7 and `00_MANAGE_REDESIGN_CANON.md:19,52,61` carry one-line
   supersession banners naming the mockup and the implementing code.
3. `python tools/board_build.py` regenerated so the board stops showing three false greens.
4. **Owner ruling wanted (§D4):** ALL is now the only home for four service structures. Is that
   acceptable, or does a fifth membership need re-homing?

## B2 — Grid tiles carry no state word and no level; the model composes one and the renderer discards it

`BuildTile` (`ManageWorkspacePanel.cs:754-845`) reads `Title`, `PortraitKey`, `FrameKey`,
`StateIconKey` — and **references `tile.Subtitle` and `tile.StateText` exactly ZERO times**, with `:826`
stating the name is *"the only text on the tile"*. Both fields are declared on the contract
(`ManageViewContract.cs:196, :206`) and **are painted by the sibling renderer** `BuildListRow` (`:677`,
`:717`). `ManageFlow_RESEARCH_school` proves the contrast — its rows read `RESEARCHED`, `QUEUE FULL`,
`RESEARCHING` in words, while the BUILD and ARMY grids say nothing.

⚠ **THE COLOURBLIND ANGLE IS UNTICKETED ANYWHERE, and it is the reason to fix this** (memory
`owner-colorblind-delegate-visual-creative`): five distinct states collapse onto five small glyphs
(`ManageArt.cs:74-78`, `StatusFor` `:112-120`) that differ partly by a red dot. WO-1516's landed lane
makes it **worse**, not better — `ProjectAffordanceTile` withholds the medallion for the Available
catch-all, leaving those tiles with neither glyph nor word.

**Acceptance:** the grid tile paints the `StateText` the model already composes — a binding, not a new
concept. State is legible without relying on glyph shape alone.

## B3 — The research picker wastes over half the panel and orphans a school

`ManageScreenVM.cs:3502-3507` authors the picker `GridColumns = 4, GridRows = 1` (*"four research
BUILDINGS in ONE row"*) — but **five schools exist**. `ManageFlow_RESEARCH_gridtop` shows four across,
Lumber Mill alone on row 2 beside three empty cells, and roughly 60% of the well black.

**Not covered by WO-2010**, whose acceptance is "all schools visible without scrolling" — which *passes*
on this frame, so the ticket is satisfied while the screen reads as broken.

**Acceptance:** capacity is derived from the live school count, not authored; the cells grow into the
well rather than leaving it empty.

## B4 — The queue drawer prints raw internal ids and developer arrow notation

`ManageFlow_BUILD_queue` rows read `Tower Ground Archer -> L2` and `Barracks -> L4`. Composed in the
**model**: `ManageScreenVM.cs:842` `label = name + " -> L" + job.TargetTier`; on a catalog miss it falls
through to `BuildTimerService.PrettyJobLabel` (`:2328-2340`), which title-cases the id's tokens —
`tower_ground_archer` -> `Tower Ground Archer` — its own comment conceding *"no catalog lookup"*.

**Not covered by WO-1491**, which enumerates exactly five copy artifacts and this is none of them.
WO-1418 lane B claimed *"`-> T` labels gone"* — true of the card, untrue of the queue. ⚠ Canon §9 forbids
the **UI** parsing ids; here the **VM** does it, so the dumb-UI rule is technically honoured while the
player still reads an identifier.

**Acceptance:** queue rows name the structure and the level in words; a catalog miss is a traced failure,
not a title-cased id.

## B5 — The BUILD detail's "what does it do" is a type-level stub

`ManageFlow_BUILD_max` — the Catapult reads *"A defensive tower … auto-fires on enemies in range."*
Source: `StructureCardVM.DescriptionFor` (`:459-469`), a **fallback** that fires only when
`CatalogEntry.description` is unauthored, logging `desc-unauthored-<id>`. So every `CatalogType.Tower`
gets the same sentence — Archer Tower, Ballista, Sky Ballista (anti-air) and Catapult read identically,
and **the Catapult is described as a tower**.

**Not covered by WO-2014**, which is about *removing* copy; nothing tickets *authoring*
`CatalogEntry.description`. WO-1491 saw this string and diagnosed a "triple space" — it is an em-dash the
font does not carry, a different fix.

**Acceptance:** descriptions are authored per catalog entry; the unauthored fallback fails the catalog
gate rather than painting prose.

## B6 — Surfaced, not filed: the activity strip

`ManageScreenVM.FillActiveTab:3755` hard-sets `tab.Activity = new ManageActivityVM { Visible = false }`
on every Manage screen while keeping `ComposeActivity` (`:3709`) alive. **WO-2012 (IN PROGRESS) still
requires that strip on each tab.** One of the two must yield; today the code has silently decided. Route
to WO-2012 rather than duplicating it here.

---

## C. REFUTED — four candidate findings, recorded so nobody re-finds them

Each looked real; each was disproved by opening the source. **This is the most reusable section of this
document, and it is the evidence for §B1's cost.**

1. **"CIVIC filter chip is missing."** The **code** is right — `BuildFilter.cs:59-73` removes it by the
   owner's mockup and re-homes all five rows, each named. (The **record** is wrong — §B1.)
2. **"BUILD shows 10 tiles where canon demands 12."** The mockup authors `5 × 2 = 10`
   (`CAPTURE_LOOP_GOAL.md:82`) and `ManageScreenVM.cs:3500-3504` implements exactly that. The canon file
   is the stale copy, not the code — §B1.
3. **"Twelve `FitSingleLine` sites pass a minimum below the documented `FontHardFloor` of 20."**
   Harmless — `ElarionUiKitObsidian.cs:3062` clamps a sub-floor minimum **UP**, and
   `HudLabelFitRegression.cs:1512-1515` already relies on that clamp.
4. **"`ManageFlow_ARMY_gridtop` and `_gridbottom` are byte-identical (MD5), as are the two RESEARCH
   frames."** True and CORRECT — `UICaptureLaunch.cs:7379-7380, 7817-7828`: a gridbottom whose content
   already fits has no bottom to scroll to, so *"gridbottom IS gridtop, by construction"*. A named,
   documented exemption. Only BUILD scrolls, and only BUILD differs.

---

## D. OPEN FOR THE OWNER — four design calls, deliberately not answered

1. **§A1** — should Manage/ARMY carry a door to the raid grid, and should its line name a camp
   (**a**, richer, needs a new published fact) or match the Journey deck's count (**b**, cheaper)?
2. **§A2** — does `LOCKED - needs Army N` become a warning (e.g. *"Outmatched — Army 9 advised"*), or
   does the door start honouring it? WWCD: Clash of Clans lets you attack an over-matched base but never
   calls it locked. **Recommend the warning, with a confirm toast** — but this is hers.
3. **§A4** — how long should a victory screen hold, and should a touch stop the timer?
4. **§B1** — with CIVIC gone, four service structures live only under ALL. Acceptable, or re-home them?

⚠ **1 and 2 interact.** If §A2 becomes a real gate, §A1's door matters much more — the player now needs a
route from "you are locked out" back to the place they fix it.

### D. RULINGS - the owner answered all four, 2026-09-06

Ruled via the question tool on the CLI seat, 2026-09-06. These CLOSE the four calls above; the numbered
questions stay as written for provenance.

1. **A1 = option (a), the NAMED CAMP plus the door.** The raid authority publishes ONE "next camp" fact;
   the Journey deck and Manage both READ it, so there is one producer and no second source to drift. The
   Manage/ARMY line becomes a DOOR to the raid grid.
2. **A2 = a WARNING, not a lock.** `LOCKED - needs Army N` becomes *"Outmatched - Army 9 advised"*; the
   card stays TAPPABLE, and a confirm toast fires on BEGIN ASSAULT. **NO SECOND GATE** - this stays
   inside section E's "must not add one" (WO-1379 / `HeartfireRegression` PIN F).
3. **A4 = first touch cancels the timer.** The anti-softlock guard STAYS, lengthened to about **30 s**.
4. **B1 = re-home the four service structures.** **Already satisfied at HEAD - see the section B1
   refutation above.** No code change is owed for this ruling; the tree already implements it.

---

## E. WHAT NOT TO TOUCH

- **Raid layout, art, backdrop, overlaps, magenta flag, "make it pop"** — WO-1462 / 1463 / 1464 / 1519,
  in flight on these exact files. Part A is words, doors and conditions only.
- **The staging area and the clock start** — WO-1520 (P0), same files.
- **Hero-death settlement** — WO-1526. §A3 must ACCOMMODATE its 2-star cap, not re-decide it.
- **Loot amounts, the Raid Cache, repeat multipliers** — WO-1461. §A6 adds a grid DISCLOSURE only.
- **A second WHEN gate on raiding** — WO-1379 / `HeartfireRegression` PIN F. §A2 must not add one.
- **The Manage grid's 10 tiles, its five chips, and the hub IA** — all CORRECT (§C). The **record** is
  what is wrong (§B1).
- **The activity strip** — WO-2012 (§B6).

---

## F. PROVENANCE AND WHAT IS NOT PROVEN

Read-only review, 2026-09-06, CLI seat. Two read-only audit agents (Manage coverage; raid coverage) plus
the seat's own verification pass; **every §A and §B claim was re-read at source by the CLI.**

⛔ **Named as unproven rather than ticked:**
- whether closing the deploy modal lands on the Journey deck or bare town
  (`RaidSelectionScreen.cs:1130-1132` says the grid is closed either way);
- whether any camp was claimed on the save behind `seeker-357453-raids.png` (§A6 rests on the source read);
- whether the uncommitted `ManageScreenPanel.cs` / `ManageScreenVM.cs` edits compile, or whether the 18:39
  frames still reproduce at HEAD — §B2, B3, B4, B5 are cited from source and do not depend on the frames;
- **anything about how the current Manage build LOOKS.** No capture postdates the tree (§1).

---

## G. WAVE-THREE/FOUR RECONCILIATION — measured at HEAD 2026-09-07 (implementation lane)

**Every remaining part of this ticket had already LANDED before this lane opened.** The ticket was minted
2026-09-06 against a tree that the wave-three and wave-four gates then moved past. Each seam below was
re-read at HEAD with `git grep <pattern> HEAD -- <path>` (the working tree is dirty with other lanes, so
HEAD and the working tree were kept separate throughout), and each landing commit found with
`git log -1 -S<token>`.

| Part | Verdict at HEAD | Proving read | Landed in |
|---|---|---|---|
| **A1** named camp + door | LANDED | `ManageScreenVM.cs:2217-2218` reads `PostureSignals.RaidNextCampName` / `RaidNextCampGarrison`; `:532-533` records "never re-derived here" | `d6511b8e5` (WO-1541) |
| **A2** warning, not a lock | LANDED | `RaidSelectionScreen.cs:1034` `armyOutmatched`, feeding the row word and the card colour at `:1037` and the log at `:1058`; the word is `vm.ArmyWarnWordFor` (`:47`) | `d6511b8e5` |
| **A3** non-victory result screen | LANDED | `RaidDeployController.cs:875-883` builds `EndStateVM.FromRaidRetreat` and calls `EndStateView.Show`; `:433` / `:784` route timeout and retreat through it with `EndStateVM.TimeoutReason` / `RetreatReason`. The ticket's `grep -c` of 1 is now 12 | `d6511b8e5` |
| **A4** touch holds the timer | LANDED | `EndStateVM.HoldOnInteraction` (`:135`, set `:443` and `:590`), honoured at `EndStateView.cs:2674`; the guard SURVIVES, lengthened — `RaidVictoryController.cs:69` `_autoReturnSeconds = 30f` | `d6511b8e5` (WO-1543) |
| **A5** ladder announcement | LANDED | `RaidVictoryController.ResolveUnlockLine` (`:831`) no longer returns null unconditionally — it asks `RaidSelectionVM.UnlockAnnouncementFor`, and still traces BOTH branches, which is the capture property `:810-812` was written to preserve | `d6511b8e5` (WO-1562) |
| **A6** cleared camp on the grid | LANDED | `RaidSelectionVM.IsClearedFor` (`:413`) / `ClearedWordFor` (`:430`) formatting `RaidClaimService.RepeatClearLootMultiplier` (`:438`); painted at `RaidSelectionScreen.cs:957`. `RaidSelectionVM.cs:134` pins "NEVER A SECOND CLAIM PREDICATE" | `d6511b8e5` |
| **B2** tile paints `StateText` | LANDED | `ManageWorkspacePanel.cs:1322` and `:1036`; `:1280-1281` records the exact defect this ticket named, and `:1317` says a source oracle now fails if the token leaves the method body | `c0c30f715` (WO-1567) |
| **B3** derived picker capacity | LANDED | `ManageScreenVM.cs:4845` `tab.GridColumns = columns` — derived; `:4788` names the retired authored `GridColumns = 4, GridRows = 1` | `d6511b8e5` |
| **B4** queue words, not ids | LANDED | `ManageScreenVM.cs:1022` `label.Replace(" -> L", " - Level ")`; `:948` deliberately leaves `PrettyJobLabel` alone because it has other callers | `d6511b8e5` |
| **B5** authored descriptions | LANDED (data + prose deletion) | all 26 filtered catalog rows carry an authored description, measured in `structures-catalog.json` 2026-09-07 (`tower_catapult` = *"Lobs stones at long range; ground foes only."*, no longer the tower stub); `BuildEconomyRegression.CheckStructureDescriptions` FAILS an unauthored buildable row and pins the deletion of the type-level prose | `d6511b8e5` (WO-1565) |

⚠ **The ticket's §B5 frame (`ManageFlow_BUILD_max`, 18:39 on 09-06) simply predates the fix**:
`d6511b8e5` is dated `2026-09-07 00:12:24 -0500`, `c0c30f715` `02:17:16`. §1's own warning — *"NO CAPTURE
POSTDATES THE TREE"* — is now true by a wider margin, not a narrower one.

### G1 — the ONE gap that was left, and the pin that closes it

WO-1565's gate keys on **`visualPrefabPath`**, exempting prefab-less rows as *"DATA rows, never placeable
and never shown"*. The Manage grid renders on a **different fact** — `manageFilters`, through
`BuildAvailability.NotPlayerContent` -> `ManageTiles` -> `BuildInventoryRow.Description`. Measured
2026-09-07 the two sets coincide **exactly** (26 filtered rows, all with a prefab path; the only
prefab-less row is `repair_default`, which carries no filter), so nothing is broken today — but **nothing
makes them coincide.** Clearing one prefab path on a filtered row drops that row out of the description
gate while it keeps rendering to the player, blank, with no oracle objecting.

**Added: `BuildInventoryFilterRegression` case 7 `[description-gate-covers-manage]`** — a COVERAGE pin,
deliberately **not** a second copy of WO-1565's rule (the case comment says so at length, because a second
copy is the duplicated state §B1 of this very ticket is about). It fails any row carrying `manageFilters`
with no `visualPrefabPath`. Registered already — `DataRegression.cs:1098`, no registration edit needed.

**Proven RED before it was written**, the only way available without Unity: the predicate was replicated
in Python against a scratch copy of the catalog. Real data -> **0 failures / 26 rows covered**. Mutated
copy (`tower_catapult` prefab path blanked; `repair_default` given an `ECONOMY` token) -> **both rows
named**. ⛔ **The C# case itself is UNRUN** — no Unity in this lane. `REGRESSION_OK <n>/<n>` on a fresh log
is the proof, and it is owed.

### G2 — the same rot, inside this lane's own files (§15, fixed in the same breath)

`BuildFilter.cs` **contradicted itself in its own header**: the title said *"the six Manage > BUILD filter
chips"* and the canon line listed CIVIC, while `:57` of the same file removes CIVIC and `Chips` holds five.
`BuildInventoryFilterRegression.cs` said *"one of the five memberships"* over an array of four, and cited
OWNER_RULINGS #5's *"six chips"* as live after §B1 had bannered it STALE. `CatalogEntry.description`'s doc
comment still promised *"absent/blank falls back to the per-type sentence"* — the prose WO-1565 deleted,
i.e. the field's own documentation described the defect as the behaviour.

All four were fixed by **deleting the copy, never by correcting the number** (CLAUDE.md §7's cure). This is
§B1's finding recurring one layer down: the record disagreeing with the screen, in code comments this time.

### G3 — SPEC for `tools/board_build.py` (NOT implemented here — the lead owns that file)

This ticket needed an HTML comment at `:6-12` to stop a five-part-open ticket rendering green, and its
RESULT needed a paragraph explaining why acceptance 3 was moot. Both are workarounds for one missing
capability, and this file is the third multi-part ticket to hit it.

> **Parse per-part status, and derive the ticket's.** Recognise, anywhere in the body, lines of the fixed
> shape `- PART <id>: <STATUS>[ <commit>]` — e.g. `- PART A: DONE d6511b8e5`, `- PART B2: READY`. Where at
> least two such lines exist, the row renders `N/M parts landed` as its sub-badge and the ticket's own
> status is **DERIVED**: `DONE` only when every part is DONE, otherwise the least-advanced part's status.
> The `**Status:**` line stays the human summary and is no longer the classifier's only input.
>
> **Why:** `classify_status` honours only a canonical FIRST WORD (`:158-160`) and otherwise falls to the
> substring pass at `:183`, where `has_result or "DONE" in s` is true — so *any* partial wording either
> reads fully Done or hides its landed half behind a `READY` lead. The file's own comment at `:143-145`
> names this: *"the error only ever ran one way: toward finished."* A per-part parse removes the
> either/or, and it cannot go stale, because the parts are read from the ticket rather than summarised
> into a prose line a seat has to remember to update.
>
> **Cheap variant** if the above is too much: keep `has_landed_partial` (`:198-206`) but make the
> substring fallback at `:183` refuse to promote to Done when the status text contains `PARTIAL`, `OPEN`
> or `READY` anywhere — closing the one-way error without a new grammar.

### G4 — unproven by this lane

- **The regression compiles and passes.** No Unity was run (lane constraint). Case 7 is source-verified
  and brace-checked only.
- **Whether the 18:39 `ManageFlow_*` frames still reproduce at HEAD**, and **anything about how the
  current build LOOKS** — §1's limit is unchanged; §A3's acceptance 4 (a fresh capture of the retreat
  screen) and §A4's legibility question are FELT tests and belong to the owner (§13), not to this lane.
- **Whether the other lanes' uncommitted edits to these same files compile together.** The working tree
  carried 30+ modified files from other lanes throughout; every measurement above was taken at HEAD and
  labelled as such.
