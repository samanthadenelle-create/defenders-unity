# WO-1654 RESULT — the troop stat rows get an icon channel, and `Damage` becomes `Attack`

**Status:** IMPLEMENTED - awaiting gate
**Lane:** MANAGE-VM (isolated worktree `.claude/worktrees/agent-a6afae9850478e00d`, branch `dev` @ `efc56f67c`)
**Date:** 2026-09-10
**No Unity run, no commit** — per the lane brief.

> ## ✅ §5.4 HONOURED — NO COST ROW WAS ADDED, NO PRICE WAS INVENTED.
> `FillTrainFacts`' `TrainCostText = ""` and the reasoning comment at `ManageScreenVM.cs:5246-5257`
> are **untouched**. Owner ruling WO-1387 ("training free … just time") stands. The scope box was read
> before any edit.

> ## ⚠ THE RENDERER IS `ManageWorkspacePanel.cs`, NOT `ManageScreenPanel.cs`.
> The lane brief warned that MANAGE-CHROME is editing `ManageScreenPanel.cs`'s hub CLOSE path.
> **`ManageScreenPanel.cs` WAS NOT TOUCHED.** `_chromeClose` and the queue row builder are untouched
> by this ticket. The stat-row renderer lives in `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs`,
> which the WO's own §3 table names correctly.

---

## 1. WHAT SHIPPED — the four touches the WO specified, in the four files it named

| File | Change |
|---|---|
| `Assets/_Modules/Core/Manage/ManageViewContract.cs` | `ManageStatVM` gains **`public string IconKey;`** (`:393`), following the `IconKey` naming already used at `:182` / `:194` in the same file. Contract stays Unity-free — no new `using`. |
| `Assets/_Modules/Core/Manage/ManageArt.cs` | Four keys beside the existing `UiFolder` constants, same shape as `ResWood`: **`StatHealth` / `StatAttack` / `StatRange` / `StatSpeed`** (`:110-113`), plus **`StatIconKeys`** — the four enumerated **once** so the coverage oracle can prove they resolve without re-listing them. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | `StatRow` gains a **trailing optional** `string iconKey = null`, so **no existing call site changed** — the building / storage / placed rows pass nothing and draw no glyph, which is correct (no sheet is authored for them, and a wrong glyph is worse than none). `TroopStatRows` passes the four keys and **renames `Damage` → `Attack`** (`:5544`). |
| `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs` | `BuildStatRows` paints the glyph **inline, to the left of the word**, via the existing `PaintSprite` helper, and shifts the label's `x0` right by the icon width. A row with **no** key keeps the full column width. |

### The label rename, and why the code was the thing that was wrong
`ManageScreenVM.cs:5517` emitted `StatRow("Damage", …)`. The delivered sheet is **`stat-attack.png`**
and yardstick row 5.3 reads *"Health / Attack / Range / Speed"* — the card was the last surface calling
it Damage, so **the screen disagreed with its own art over one stat's name**. The **VALUE is
untouched**: still `TroopStatResolver`'s `AttackDamage`, the same number the live unit fights with.
Only the word the player reads moved. The four stats, their order and their values are unchanged
(WO §5).

### Greyscale gate (WO-1566 C8, acceptance 3)
**The icon is an ADDITION; the label survives, unconditionally.** Nothing in the renderer is
conditional on the sprite resolving — `ManageArt.LoadSprite` degrades an unresolvable key to a
transparent Image (`PaintSprite` sets alpha 0), and the **word** is the channel a red/green
colourblind read survives on. The reasoning is written into `ManageStatVM.IconKey`'s doc comment and
repeated at both the producer and the renderer, so a later seat cannot "tidy" the label away.

### Placement discipline
The glyph is drawn **inside** the row's own column (`x0 → x0 + iconW`), never hung off the left edge at
a negative offset — the mistake that made the why-band padlock **vanish** from a capture, recorded in
the `needWhy` block a few lines above. That precedent is cited at the new code.

---

## 2. THE ORACLES — a resolution proof and a composed-VM pin, deliberately separate

### (a) `[stat-glyphs-resolve]` — `Assets/Editor/Regression/ManagePortraitCoverageRegression.cs:267`
Registered in `Run` beside `CheckChrome`. Pushes **every key in `ManageArt.StatIconKeys`** through
`Require`, i.e. through **`ManageArt.LoadSprite` — the production loader** — exactly as the portrait
keys are (`MANAGE_PORTRAIT_COVERAGE_OK <n> … keys resolve`), which is what acceptance 4 asks for.

⛔ **This is why a source lint would have been worthless, and it is written into the case:** a case
that only proved the constants exist **would pass with all four PNGs deleted**, and it would pass
loudly, because an unresolvable key degrades to a *transparent* image — no pink square, no exception,
just a blank glyph column with nobody's eyes on it but the owner's. That is precisely the detector
CLAUDE.md §14 exists to never rely on.

**RED PROOF:** delete any one of
`Assets/Resources/UI/ElarionMedieval/Manage/stat-{health,attack,range,speed}.png`.
All four were confirmed present on disk this session.

### (b) `[case 8]` re-pointed — `Assets/Editor/Regression/ManageTroopsTrainDoorRegression.cs:418-451`
The suite that already owns `TroopStatRows`' composed output. **Re-pointed, never deleted:**
- `sawDamage` → `sawAttack`; the by-name four-row assertion and its message move with it.
- A **new** explicit failure fires if a row is still labelled `"Damage"` — so the rename cannot silently
  revert.
- **New:** each of the four stat rows must carry a non-empty `IconKey`, or the case names the row and
  says the glyph column is blank. The building / storage / placed rows are deliberately **not** forced
  a key.

**RED PROOF:** drop the `ManageArt.Stat*` argument from any `StatRow` call in `TroopStatRows`.

**No `DataRegression.cs` registration line was needed** — both suites are already wired.

### ⚠ HOW HONEST THE "RED-FIRST" CLAIM IS (acceptance 5)
- **`Attack`**: genuinely RED against today's tree — the card reads `Damage`, so the re-pointed
  `sawAttack` half fails before the VM change.
- **The four keys resolving**: genuinely RED — delete a PNG and it fails.
- **The `IconKey` half**: ⛔ **cannot be RED on the pre-fix tree, and saying otherwise would be a
  fiction.** The field did not exist, so the case would not have *compiled*, let alone failed. It is
  RED **by construction** against the post-contract tree via the recipe above.
- ⛔ **NOTHING HERE HAS BEEN RUN.** No Unity in this lane. The gate is what turns any of it into a fact.

---

## 3. ACCEPTANCE, ANSWERED HONESTLY

| # | WO says | Status |
|---|---|---|
| 1 | Fresh `RunManageFlowMapCaptureHeadless`; an icon against each of the four stats in `ManageFlow_ARMY_action_2670x1200.png` | **NOT DONE — no Unity in this lane.** Needs the lead's capture + the PNG opened. |
| 2 | The label reads `Attack` on Archer and Outrider | ✅ in code (`ManageScreenVM.cs:5544`); pinned by `[case 8]`. Unverified on a frame. |
| 3 | Greyscale gate — icon ADDS, label survives | ✅ structurally: the label is drawn unconditionally, the icon only shifts its `x0`. |
| 4 | A **resolution** oracle, not a source lint | ✅ `[stat-glyphs-resolve]`, through `ManageArt.LoadSprite`. |
| 5 | RED-first | ✅ for `Attack` and for key resolution; **honestly qualified** for the `IconKey` half — see §2. |
| 6 | `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs | **NOT DONE — the lead gates.** `python tools/gate_brace.py` clean on all touched files; NUL scan clean. |
| 7 | Status → `AWAITING OWNER MATCH` | Set to **`IMPLEMENTED - awaiting gate`** on the lead's explicit instruction; it becomes `AWAITING OWNER MATCH` once the gate is green. |

---

## 4. WHAT WAS NOT TOUCHED (WO §5, line by line)

- ⛔ **Row 5.4 / the train cost band** — no cost row, no price, reasoning comment intact.
- ⛔ **`TroopStatRows`' four-stat choice and their values** — only the label string and the icon channel moved.
- ⛔ **`StatRow`'s gold delta / BOLD promotion** — untouched; `[detail-next-by-weight]` scans
  `BuildStatRows` for `bold: promoted`, which is intact (the edit sits above the value label).
- ⛔ **`ManageWorkspacePanel` stays a plain class** — no new `using`, no new namespace; the edit reuses
  `PaintSprite` and `Vector2`, both already in the file.
- ⛔ **The BUILDING stat rows** — `BuildingStatRows` is byte-identical. (WO-1653 was handled without
  editing it either.)
- ⛔ **`ManageArt.BuildingPortraitKey` and its verbatim-id rule** — untouched.

### One adjacent correction made in the same breath (CLAUDE.md §15)
`TroopStatRows`' summary claimed *"FIVE ROWS … `BuildStatRows` seats `Mathf.Min(count, 5)`"*. **There is
no literal 5** — the renderer seats `Mathf.Min(seats, stats.Count)` where `seats` is DERIVED from the
band's measured pixel height (`ManageWorkspacePanel.BuildStatRows`). The comment also still counted a
Train-time row that the same method's own closing note says **left for the clock band**. Both were
corrected where they sat, since the doc describes the exact renderer this ticket edits. (The WO's §5
repeats the `Mathf.Min(count, 5)` figure from that same stale comment — noted, not acted on beyond the
comment.)
