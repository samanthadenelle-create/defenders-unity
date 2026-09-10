# WORK ORDER 1672 — RESULT

**Status:** IMPLEMENTED - three open owner questions (WO §7); none blocks the build; awaiting gate
**Lane:** DOCK-CAPTIONS, isolated worktree `.claude/worktrees/agent-ad0b5c7a8c34ea18a`
**Base:** `e4b5906a541c65fc12255a109b5dc3723e328488`, ff-merged from `dev`
**Date:** 2026-09-10

---

## 1. THE ANSWER TO THE QUESTION THE TICKET ASKED

> *find how the HUD knows the hero is outside the castle walls … if no such signal exists, the ticket
> says so and the implementation adds the cheapest honest one*

**A signal exists, it is live, and it already reaches a posture.** Nothing was invented.

`HudContextEvaluator.IsInTownRing` (`Assets/_Modules/Village/HUD/HudContextEvaluator.cs:202-214`) —
hub-scene test plus the hero's horizontal distance from the world origin against `TownRadius = 60f`
(`:74`), hysteresis `8f` (`:75`), polled at 0.20 s (`:85`)
→ `HudContextResolver.Resolve` (`Assets/_Modules/Core/HudModel/HudContextResolver.cs:41-48`)
→ `HudContext.Town` / `HudContext.Overworld` (`Assets/_Modules/Core/HudModel/HudModelTypes.cs:20`)
→ `PostureEvaluator.Derive` (`Assets/_Modules/HUD/Kit/PostureEvaluator.cs:143-145`)
→ `HudPosture.CalmTown` / `HudPosture.CalmExplore` (`Assets/_Modules/HUD/Kit/HudPosture.cs:17-33`)
→ **already traced on every transition** at `PostureEvaluator.cs:78-80`.

**The gap was the last hop only:** `hud-areas.json` listed the same `"peacefulDock"` under both
`calm(town)` and `calm(explore)`, so the dock was byte-identical on both sides of a boundary the HUD had
already crossed. **No new FlowTrace was added for transitions** — `PostureEvaluator.cs:78-80` is that
line, and a second owner of one event is the duplicated-state failure CLAUDE.md §2/§5/§16 each describe.
One `FlowTrace.Once` is added and proves something different: that the third dock built, and with which
faces.

⚠ **Four "castle edge" definitions in this repo disagree** (HUD ring r=60 · zone box ±52 · moat 44→62 ·
the actual walls at z ≈ −40.9 from `castle-south-recipe.json`). Tabulated in WO §1b. **Not reconciled by
this lane** — r=60 is the number the HUD already consumes, so using it adds no new state. Picking or
unifying a boundary is flagged for the owner.

## 2. WHAT LANDED — six files

| File | Change |
|---|---|
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | `BuildAdaptiveOutsideDock` + `BuildOutsideDockProbe`; `BuildPeacefulDockSlot` hoisted to a shared `BuildDockSlot` (the five calm call sites are unchanged, via a thin wrapper); localized-copy refresh; `OnConsumables` drives the outside ITEM badge |
| `Assets/Resources/Data/Canonical/hud-areas.json` | `calm(explore)` actionBar row: `peacefulDock` → `outsideDock` |
| `Assets/StreamingAssets/Data/Canonical/hud-areas.json` | same, byte-identical |
| `Assets/Editor/Regression/HudActionBarRegression.cs` | `CheckMeasuredOutsideDock` + its dispatch line |
| `Assets/Editor/Regression/HudDockLayoutRegression.cs` | **oracle re-point** — the 1/n gap-fraction law extracted `BuildPeacefulDockSlot`, which is now a 3-line wrapper; left there it would have gone green FOREVER while the real builder was free to re-hardcode the gap. Re-pointed to `BuildDockSlot`. |
| `Assets/Editor/Regression/HudLabelFitRegression.cs` | **two oracle re-points** — Case8's icon/label-separation pin now names the shared builder's signature too; and `[bar-face-icons] 8c` (the "caption is STILL live text" law) re-pointed, see §9 |

Faces: **HERO · JOURNEY · MANAGE · ITEM**. BUILD and TALK **not constructed**.

## 3. ABSENT, NOT DISABLED — stated in terms of what the oracle can distinguish

`CheckMeasuredPeacefulDock` walks faces with `GetComponentInChildren<Button>(true)` — include-inactive,
**deliberately**, because `Register()` deactivates the dock root. So a `SetActive(false)` BUILD face
still counts as a face and the oracle **cannot** tell it from a live one. Only a never-constructed face
is assertably absent. Two supporting reasons: a dim must carry a ruled word/number
(`HudActionBarModel.cs:270-285`, colourblind rule) and none is ruled; and at four faces the dock solves
**wider** slots than the calm dock at every surface, never narrower (`MinSlotPx = 112`).

Recorded, not fixed: BUILD and TALK were never gated in town either — `HudCommands.Talk()` fires
unconditionally and never reads `PostureSignals.TalkAvailable`, whose only consumer is the
`HudActionBarModel` that `BindActionBar` never subscribes (`HudKitController.cs:3473`, `:3480-3485`).

## 4. THE ITEM FACE

Reuses the combat path exactly: `OpenItemPicker` + `ElarionUiKit.StyleAsStackBadge` +
`SeatStackBadgeInMedallion` (the WO-1468 seat pinned by `HudUiRegression`). Two checks made because they
would have shipped a wrong face:

- **Driven, not frozen:** `OnConsumables` now refreshes the outside slot's count and interactable
  alongside the combat one. Without it the badge holds its build-frame value.
- **The picker pauses the world.** `OpenItemPicker` has **no** combat precondition (only a re-entrancy
  guard; `BattleLock` appears zero times in `HudKitController.cs`), but it takes
  `WorldHold.AcquirePlayerOwned`, and `WorldHold` is the only code in the project that writes
  `Time.timeScale` (`Assets/_Modules/Core/UI/WorldHold.cs:24`, `:59`). So opening it outside the castle
  stops the world. Consistent with combat — and flagged as an open question, not assumed acceptable.

Caption is the literal `"ITEM"`: `HudStrings` has no item key (`AllKeys`, `HudStrings.cs:128-139`) and
the combat face is the same literal. Minting `hud.nav.item` means touching every locale file mid-flight
on the localization lane — a follow-up ticket, not a half-measure here.

## 5. THE JSON EDIT — proven, not assumed

Edited **binary, from the HEAD bytes** (memory `canonical-json-edits-binary-only-verify-newlines`).
Measured before and after in both copies: **275 LF / 275 CRLF, unchanged** (CRLF throughout); both parse
as JSON; the two copies are **byte-identical by SHA-256**; `"peacefulDock"` count 2 → 1 (the surviving
one is `calm(town)`), `"outsideDock"` count 0 → 1. The replaced occurrence was verified to belong to
`calm(explore)` by locating its nearest preceding `"posture"` key at byte 1917.

The two pre-existing occupancy oracles (`HudActionBarRegression.cs:595`,
`ObsidianQueueRegression.cs:402`) only require `peacefulDock` to appear *somewhere*; it still does, so
they stay green — correctly, since neither ever asserted which row. `CheckMeasuredOutsideDock` is what
now asserts the row.

## 6. THE PIN

`CheckMeasuredOutsideDock` builds the real dock through `BuildOutsideDockProbe` and asserts from the
tree: the exact ordered set `[HERO JOURNEY MANAGE ITEM]`; **separately**, that no BUILD face and no TALK
face exist and that an ITEM face does (so a re-order cannot pass by accident); a WO-1671 caption plate
on every face; the touch floor at all four shipping surfaces, re-derived from the count found; and that
`calm(explore)` mounts `"outsideDock"` and no longer mounts `"peacefulDock"` in **both** JSON copies —
because a perfectly-built dock no posture mounts is invisible.

⛔ **RED-FIRST BY CONSTRUCTION, NOT BY EXECUTION — this lane has no Unity and DID NOT RUN THE SUITE.**
Against HEAD `e4b5906a5` there is no `BuildOutsideDockProbe` to call, so the case does not compile
against the old tree — the strongest red available. Against a tree with the builder but the JSON
unchanged it fails on both `calm(explore)` rows; against a tree where BUILD was merely disabled it fails
with *"the outside dock still carries a 'BUILD' face — the owner ruled the player cannot build outside
the castle"*. No claim is made that `HUD_ACTIONBAR_OK` was observed. It was not.

## 7. CHECKS RUN IN THIS LANE

- `python tools/gate_brace.py` (the port of the gate's OWN rule, CLAUDE.md §1) on all six `.cs` →
  `GATE_BRACE_SUMMARY bad=0 of 6`, exit 0
- raw brace balance: ElarionUiKit 375/375, HudDockSlotLayout 13/13, HudKitController 423/423,
  HudActionBarRegression 70/70, HudLabelFitRegression 219/219, hud-areas.json 46/46 (both copies).
  ⚠ `HudDockLayoutRegression.cs` reads **54/53 RAW** — that imbalance is **PRE-EXISTING and not from
  this lane**: the diff on that file adds a comment block and changes two string literals, and
  contains **zero braces**. `gate_brace.py`, which ports `CompileGate.BraceBalanced` exactly, passes
  it. This is the naive-vs-gate counting difference CLAUDE.md §1 warns about, recorded rather than
  "fixed" by a seat that did not cause it.
- NUL-byte scan on every touched `.cs` **and both `.json`** → clean
- `ExtractMethod`'s needle verified against the new multi-line signature: it does `IndexOf(signature)`
  then takes the first `{` after it (`HudDockLayoutRegression.cs:419-436`), so a signature whose
  parameters wrap still resolves the body
- No `.unity` scene touched, no `System.Reflection` added

## 8. OPEN OWNER QUESTIONS — none blocks the build

1. HERO / JOURNEY / MANAGE outside the walls — **not ruled**; carried over as the least-surprising
   default.
2. ITEM's position — **not ruled**; rightmost, matching the combat dock.
3. The item picker **pauses the world** outside combat (§4) — acceptable, or a non-pausing hold out
   there?
4. ⚠ **`calm(explore)` is every NON-HUB scene, dungeons included** — `IsInTownRing` opens with
   `HubScenes.IsHub` (`HudContextEvaluator.cs:204`), pinned by `HudActionBarRegression.cs:167` and
   `ScenePostureSeamRegression.cs:174-209`. So this ticket also removes BUILD/TALK and adds ITEM
   **inside dungeons**. Almost certainly wanted, but the ruling said "outside the castle" — written
   down rather than discovered in a felt-test. Does not block.

Out of scope, each with its own seam: the four disagreeing castle-edge definitions; TALK ignoring
`PostureSignals.TalkAvailable` in town; the missing `hud.nav.item` string key.

---

## 9. CHAIN 39 RED — CAUGHT BY THE LEAD, FIXED HERE (2026-09-10, re-based on `de91a22ee`)

**The failure, from `Builds/wave8-reg2`:**
```
hud-label-fit FAIL x1: [bar-face-icons] BuildPeacefulDockSlot no longer resolves and paints its
label key - the face's word must stay LIVE text; art must never become its only producer
```

**Cause — mine.** Hoisting the calm dock's slot builder into the shared `BuildDockSlot` changed the
line `string caption = HudStrings.Get(labelKey);` into
`string caption = literalCaption ?? HudStrings.Get(labelKey);`. Case 8c pins that line by literal text,
so the needle stopped matching.

⚠ **One correction to the hand-off note, for the record (§11B):** the case does **not** extract
`BuildPeacefulDockSlot`'s body — it is a whole-file `src.IndexOf` on that exact line
(`HudLabelFitRegression.cs`, section `---- 8c`). Same effect, different mechanism; naming it wrongly
would send the next seat looking for an `ExtractMethod` that is not there.

**Fix — a minimal hunk around 8c only** (the file also carries WO-1666/1667 edits; the lead merges
3-way). The needle is now `string caption = literalCaption ?? HudStrings.Get(labelKey);`, which is
**stronger, not weaker**: it still proves the word is resolved from a localization key at runtime, and
it additionally pins that the one non-localized path is the explicitly-named `literalCaption`
parameter sitting **second** in the `??`. A seat making literals the default would flip the operands
and red here. The message now names `BuildDockSlot`.

### 9b. THE REAL LESSON — I swept for the wrong thing the first time

My original sweep grepped `Assets/Editor` for the *identifier* `BuildPeacefulDockSlot` and cleared the
hits by reading them. That misses every law that pins a **line of the body** without naming the method
— which is exactly what 8c is. **The correct sweep is by the text you CHANGED, not by the symbol you
renamed** (memory `search-by-token-not-by-name`).

Done now, and recorded so it is not re-learned: every literal removed from or altered inside the
builder was grepped across `Assets/Editor` — `_peacefulDockRoot.transform`, `_peacefulDockLabels`,
`_peacefulDockLayout`, `const int count = 5`, `HudStrings.Get(labelKey)`,
`BuildActionSlot(_peaceful`, `private void BuildPeacefulDockSlot`. The only live hit is
`LocaleSmokeCapture.cs:351`, which reflects the field `_peacefulDockLabels` — still present, still
`TMP_Text[5]`, still populated through the shared builder's `labelSink`. **Clear.**

### 9c. ALL NEEDLES RE-VERIFIED AGAINST THE LIVE BUILDER — measured, not assumed

Every positive needle in `Case8_BarFaceIcons` and in `HudDockLayoutRegression`'s dock section was
replayed in Python against the working-tree `HudKitController.cs`: **18 present, the one negative
needle absent, no call-site icon leak on any of the five faces — BAD = 0.**

And the re-point is confirmed to have restored real coverage rather than merely gone quiet: the
extracted `BuildDockSlot` body is 4 388 chars, the 1/n slicing regex **MATCHES it (True)**, and it
reads `HudDockLayout.GapFraction` **(True)** — so the law correctly does not fire, *while actually
policing arithmetic again*. Pointed at the three-line wrapper it had no arithmetic to match at all and
would have been green forever.

### 9d. RE-RUN CHECKS (base `de91a22ee`)

- `python tools/gate_brace.py` on all six `.cs` → `GATE_BRACE_SUMMARY bad=0 of 6`, exit 0
- NUL scan on all six `.cs` + both `.json` → clean
- raw braces: ElarionUiKit 375/375, HudDockSlotLayout 13/13, HudKitController 423/423,
  HudActionBarRegression 70/70, HudLabelFitRegression 219/219, hud-areas.json 46/46 x2.
  `HudDockLayoutRegression.cs` still reads 54/53 raw — **pre-existing**, my diff there contains zero
  braces, and `gate_brace` (the port of `CompileGate.BraceBalanced`) passes it.
- Chain 39 reported green for the rest of this lane's work: `UI_CAPTURE_OK 106`, touch/glyph OK
  including the AdaptiveHud panels.

