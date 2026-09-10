# WORK ORDER 1673 — RESULT (PARTIAL: D7 + D1)

**Lane:** COMPLIANCE / touch-floor seat, 2026-09-10. Isolated worktree
`.claude/worktrees/agent-a35ba6c65858e79ea`, fast-forwarded to `dev` at **`e4b5906a5`**.
**No Unity was run and nothing was committed** — by instruction.
Scope executed: **D7** (the stale drop-model note) and **D1** (the imaginary regression).
**D2 not started. D3/D4/D5/D6 deliberately untouched — they are owner-gated.**

---

## 1. What landed

| Deliverable | State | Path |
|---|---|---|
| **D7** — stale "guaranteed" drop-model note | **DONE**, binary-safe, newline-proven | `Assets/Resources/Data/Canonical/jewel-polish.json` + StreamingAssets twin |
| **D1** — `JewelPolishRegression` built for real | **DONE**, 6 cases, RED-first proven | `Assets/Editor/Regression/JewelPolishRegression.cs` (**new**) |
| D2 — derive the covenant file list | not started | — |
| D3/D4/D5/D6 | **owner-gated, untouched** | — |

⛔ **The RATE WAS NOT TOUCHED.** 5% post-first / tier-2+ / raid tier-3 / 1-per-day remain exactly
as the owner ruled on 2026-09-09 (WO-1373). D7 is **text only**; D1 **pins** those numbers rather
than arguing with them.

---

## 2. D7 — the note, and the newline proof

### 2a. What it said, and why that was a compliance defect

`_tuning.dropModel` claimed every completed run paid a stone with certainty and that runs and gems
were interchangeable one-for-one. **That had been false since 2026-09-09.** It is the only prose in
the repo describing the rough-stone economy in one place, so it is what a seat would read to answer
a Google Play probabilistic-item question — and it would have declared a guaranteed drop the game
does not have.

The note now states: first stone **guaranteed**; every one after is a **5 percent** roll on a
completed dungeon of **tier 2 or higher**; the rate lives on the remote key
`dungeon.roughStoneDropPct` (so the note explicitly refuses to become a second authority for it);
and **raids are not a roll at all** — `RaidScoring.ShouldDropRoughStone` has no RNG, it is tier ≥ 3
AND ≤ 1/day.

It also carries a **`!! CONSEQUENCE, NOT YET RE-TUNED !!`** flag: `runsForTheFirstUpgrade` and
`runsForTheWholeRingChain` in the same block are still computed on the retired one-stone-per-run
model, so they count **polishes, not runs** — past the first stone, `runs = polishes / 0.05`.
⛔ **Deliberately flagged, not silently rewritten:** recomputing them is a balance decision
(25 runs → ~481 at score 0), and that is the owner's call, not a doc lane's.

### 2b. Binary-safe, and the numbers that prove it

Both twins are **CRLF**. A text-mode read/write would have rewritten all 66 line endings — the exact
failure that once flattened twelve canonical files. So the patch opened `rb`, replaced **bytes**, and
wrote `wb`, refusing on any census change:

| | bytes | LF | CRLF | CR | NUL | BOM |
|---|---|---|---|---|---|---|
| before (HEAD) | 6798 | 66 | 66 | 66 | 0 | none |
| after | **8647** | **66** | **66** | **66** | **0** | none |

**Every newline count is unchanged**; only the note's own length moved. `git diff --stat` reads
**1 line changed per file** (`2 files changed, 2 insertions(+), 2 deletions(-)`) — a whole-file
reflow would have shown 66. Both twins re-read after writing: **byte-identical**,
`sha256 4d3bbf1ecafff2a998c5678c8e49090b…`, and each still parses as JSON with the value round-tripping
exactly.

### 2c. ⚠ The RED-first proof caught a bug in MY OWN fix — worth recording

The first pass **quoted the retired sentence verbatim** inside its own "why this note is written this
hard" explanation. `JewelPolishRegression` greps that field for exactly that sentence, so **the fixed
file went RED, and it was right to**: a quotation is byte-identical to a relapse, and no grep, diff or
hurried reader can tell them apart.

**The fix was to the NOTE, not to the check.** A disclosure field must not carry the false sentence in
any form. The retired claim is now *described*, and the note says why it is not reproduced. Had the
proof been skipped, this would have shipped as a permanently-red gate that the next seat would have
"fixed" by weakening the assertion.

---

## 3. D1 — `JewelPolishRegression`, which two files have claimed for weeks

**It did not exist.** Before today, `grep -rn "JewelPolishRegression" .` over the whole repo returned
**two hits, and both were the claim itself**:

- `JewelPolishService.cs:425` — *"JewelPolishRegression fails if a second odds source appears or if
  these numbers stop matching the table."*
- `jewel-polish.json` `_tuning.fairness` — *"…and JewelPolishRegression FAILS if a second table ever
  appears. This is the fairness property the whole design rests on."*

The one mechanic that discloses odds to the player had an **imaginary** gate protecting that
disclosure's accuracy — the same comment-only-firewall failure `MonetizationCovenantRegression`'s own
header was written to close. **Both sentences are now true.** The file is named
`JewelPolishRegression` precisely so they are satisfied verbatim (WO-1673 §D1 had provisionally called
it `JewelPolishOddsRegression`; the real name wins).

### The six cases, and the independent authority each is paired against

| Case | Kind | Independent authority |
|---|---|---|
| `[drop-model-note-is-not-stale]` ⭐ **the RED-first one** | text | the retired sentence's absence **and** the current model's presence (5% / guaranteed-first / `RaidScoring`) |
| `[twins-byte-identical]` | bytes | the two canonical copies against each other |
| `[drop-rate-authority]` | const + lint | `DungeonRoughStoneDropPctDefault == 5`, a `TunableSpec` **row must exist** (a missing row silently strands the knob while the value still reads 5), and `DungeonController` must still read the rail **key**, not a literal |
| `[tier-floor-matches-authored-layouts]` | **data vs const** | the authored `"tier"` in `dungeon-layouts/*.json` vs `RoughStoneMinDungeonTier` |
| `[odds-disclosed-equal-odds-rolled]` | data + arithmetic | this suite parses `jewel-polish.json` **off disk**; `DescribeOdds` reads it **through the catalog** — two readers of one file |
| `[one-odds-source]` | lint | `DescribeOdds` must still derive via `RowFor(score)`; `RaidScoring.ShouldDropRoughStone` must stay RNG-free |

**The pairing that earns its keep** is the tier one. `RoughStoneMinDungeonTier = 2` is documented as
"anything past the starters", which is only true while the starters are authored at tier 1 — and
**nothing connects the const to those JSONs at runtime.** Measured this session: **seven** layouts at
tier 1 (the four owner-named starters plus `dg_descent_probe`, `dg_stair_rig`, `dg_stairwell_probe`)
and exactly **three** at or above the floor (`dg_sunken_vault` 2, `dg_bonecrypt` 3, `dg_ember_deep` 4).
So the case also asserts **at least one layout reaches the floor** — because if a re-tier ever dropped
all of them below it, the post-first stone would become **unobtainable**, the Jeweler chain would
dead-end, and **nothing else in the repo would notice**: the roll simply never fires.

The odds case asserts the shatter model is the **independent-first** one — gem odds are
`weight/total × (1 − shatter)`. A "simplification" reporting the raw normalised weights on a re-polish
would overstate every gem chance by `1/(1−0.15) ≈ 18%` and still look perfectly plausible in the
panel. That is exactly the disclosure error a regulator finds.

⚠ **Recorded in the file, not left to be discovered:** `JewelerDiscoveryFtueRegression:33` **already**
asserts the same `DungeonRoughStoneDropPctDefault == 5` (it re-pointed there when WO-1373 retired its
old literal `0.15f` pin). Two readers of one const is redundant coverage, not duplicated state — but a
seat deleting one should know the other exists rather than finding out by going red.

**Harness honesty:** the odds case stands down by name via `RegressionOutcome.PartialSkip` if
`JewelPolishCatalog` cannot resolve the file through `Resources` in a given batch context. That is a
harness capability, not a product defect, and a silent pass would have been the arithmetic bug
`RegressionOutcome` exists to end.

---

## 4. Registration line — for the LEAD to paste (this lane did NOT edit `DataRegression.cs`)

One line, beside the covenant suite in `DataRegression.RunAll`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "jewel-polish suite", () => { if (!DeNelle.Editor.Regression.JewelPolishRegression.Run(out var jpReason)) failures.Add(jpReason); else log.AppendLine("[jewel-polish] " + jpReason); });
```

Suggested seat: immediately after the `[covenant]` line (currently `DataRegression.cs:332`), since the
two are the monetization/disclosure pair. Standalone marker for a focused run:
`JEWEL_POLISH_OK` / `JEWEL_POLISH_FAIL` via `JewelPolishRegression.RunAll()`.

**Assembly check, done not assumed:** the file sits in `Assets/Editor/Regression/`, i.e.
`DeNelle.EditorRegression`, whose `.asmdef` references `DeNelle.Core`, `DeNelle.Village` **and**
`DeNelle.Dungeons` — every type it touches (`JewelPolishService`/`JewelPolishCatalog`,
`DungeonController`, `RemoteTunables`, `RegressionOutcome`) resolves. `TunableSpec` is a `sealed class`,
so the `SpecFor(...) == null` comparison is legal; `RoughStoneMinDungeonTier` and
`KeyDungeonRoughStoneDropPct` are both `public const`.

---

## 5. Evidence

**RED-first, run this session** (`git show HEAD:<path>` for the RED side — the retired sentence is read
from the commit, never reconstructed):

```
=== RED: the case applied to HEAD's committed twins ===
  FAIL HEAD:.../Resources/.../jewel-polish.json: RETIRED drop-model sentence is back (found 'per COMPLETED run, guaranteed')
  FAIL HEAD:.../Resources/.../jewel-polish.json: RETIRED drop-model sentence is back (found 'runs = gems')
  FAIL HEAD:.../Resources/.../jewel-polish.json: RETIRED drop-model sentence is back (found 'the arithmetic below is exact')
  FAIL HEAD:.../Resources/.../jewel-polish.json: note never states the post-first drop chance
  FAIL HEAD:.../Resources/.../jewel-polish.json: note does not distinguish the raid path
  ... (same five on the StreamingAssets twin)
  -> 10 failure(s)

=== GREEN: the same case applied to the working tree ===
  -> 0 failure(s)

RED_FIRST_PROOF_OK
```

⚠ **This reproduces the CASE's rule outside Unity; it is not a `REGRESSION_OK` run.** The suite has
never executed. That is owed (§6).

**Gate hygiene:** `python tools/gate_brace.py` over all eight touched `.cs` →
**`GATE_BRACE_SUMMARY bad=0 of 8`**. NUL scan → **0 NUL bytes in 8 files**.

**Diff:**

```
 Assets/Resources/Data/Canonical/jewel-polish.json       | 2 +-
 Assets/StreamingAssets/Data/Canonical/jewel-polish.json | 2 +-
 Assets/Editor/Regression/JewelPolishRegression.cs       | new file
```

---

## 6. What is NOT proven

1. **No Unity run.** `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n>` are owed on a fresh log, judged by
   the **marker**, not the exit code. The new suite has never executed; API and assembly references were
   verified at source (§4), but *"it should compile"* is a guess and is not claimed.
2. **The registration line is untested** — it is written in the shape of the adjacent covenant line and
   the lead pastes it.
3. `runsForTheFirstUpgrade` / `runsForTheWholeRingChain` are **flagged, not corrected** — owner balance
   call (§2a).
4. **D2 and D3–D6 are open**, per §1.

## 7. Files touched

```
Assets/Resources/Data/Canonical/jewel-polish.json          (D7 - note text only)
Assets/StreamingAssets/Data/Canonical/jewel-polish.json    (D7 - byte-identical twin)
Assets/Editor/Regression/JewelPolishRegression.cs          (NEW - D1, 6 cases)
WorkOrders/WORK_ORDER_1673_...korea_brazil.md              (Status flipped to PARTIALLY)
WorkOrders/WORK_ORDER_1673_...korea_brazil.RESULT.md       (this file)
```

Not touched, per the WO's §5: the drop **rate**, `JobRushPolicy`, the ad-skip carve-out, the odds
**values**, `publishing/config.yaml`, the IARC declaration, `MonetizationCovenantRegression`'s semantic
lists, **and `DataRegression.cs`** (registration handed to the lead, §4).
