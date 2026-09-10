# WO-1654: troop stat rows have NO icon channel at all, and `Attack` is labelled `Damage`

**Status:** IMPLEMENTED - awaiting gate

> ## ⚠ NOTE 2026-09-10 (MANAGE-VM lane) — implemented as specified, with two clarifications.
> Full record in `WORK_ORDER_1654_troop_stat_rows_have_no_icon_channel_and_attack_is_labelled_damage.RESULT.md`.
>
> **§5.4 WAS HONOURED: NO COST ROW WAS ADDED.** `TrainCostText = ""` and the reasoning comment at
> `ManageScreenVM.cs:5246-5257` are untouched. Owner ruling WO-1387 stands.
>
> **THE RENDERER IS `ManageWorkspacePanel.BuildStatRows`, NOT `ManageScreenPanel`.** §3's table is
> right; the lead's brief guessed `ManageScreenPanel.cs`, which was NOT touched — so the MANAGE-CHROME
> lane's `_chromeClose` / queue-row work is untouched by this ticket.
**Silo:** `ManageViewContract.cs` + `ManageScreenVM.cs` + `ManageWorkspacePanel.cs` (contract, producer,
renderer — one vertical slice, no other module).
**Number:** PRE-ASSIGNED by the lead. ⛔ **Do NOT edit `CLI_LANES_WO_NUMBERS.md`.**
**Source:** WO-1566 audit re-tick, RESULT row **5.3** (audit `2039e2c41`, re-tick `9592cdd6f`).
**Yardstick row:** WO-1566 §2 panel 5, row **5.3** — *"Stats: Health / Attack / Range / Speed, each
with its icon."*

> ## ⛔ SCOPE CORRECTION — THIS TICKET IS **5.3 ONLY**. ROW 5.4 IS AN OWNER RULING AND IS EXCLUDED.
> The lead's brief paired 5.3 with 5.4 ("no COST row"). **5.4 is NOT a defect and must not be
> "fixed".** `ManageScreenVM.cs:5246-5257` documents it at length: training is FREE in this build by
> **owner ruling WO-1387 (2026-09-04 23:16), verbatim *"training free ... just time"***, and
> `FillTrainFacts` sets `TrainCostText = ""` (`:280`, `:2471`) for exactly that reason. The code
> already weighed the mockup against the ruling and refused to invent a price, in its own words
> because *"inventing 550 gold to fill a band would be a PRICE the game does not charge, i.e. a lie on
> the one screen a player uses to decide."* **I am withdrawing 5.4 as a FAIL** — it is a ruled
> divergence, correctly reported to the owner rather than fabricated. Anyone who "completes" this
> ticket by adding a cost row has shipped a lie.

---

## 1. THE EVIDENCE

`Builds/wave5-manageflow1` (08:09). Frames opened: `ManageFlow_ARMY_action_2670x1200.png` (Archer) and
`ManageFlow_ARMY_locked_2670x1200.png` (Outrider). Both render the four stats as **plain
label/value text rows with no iconography of any kind**:

```
Health                 60  ->  66          Health      95
Damage               29.0  -> 31.9         Damage    18.0
Range                14.0  -> 15.7         Range       2.5
Speed                                4.0   Speed       5.5
```

The mockup draws an icon against each. **The four sprites exist and are keyed** — WO-1566 §3 / the
audit RESULT §4 confirm `Assets/Resources/UI/ElarionMedieval/Manage/stat-{health,attack,range,speed}.png`
are all present, addressed off `ManageArt.UiFolder` (`ManageArt.cs:80`).

## 2. THE CAUSE — this is a NOT-BUILT gap, not a broken path

`Assets/_Modules/Core/Manage/ManageViewContract.cs`, `class ManageStatVM`, carries exactly three
fields:

```
public string Label;
public string Value;
public string DeltaText;
```

**There is no icon channel on the contract at all.** Nothing is failing to resolve, nothing is
rendering blank — the model has no place to put an icon key, so the renderer has nothing to paint.
(Other VMs in the same file DO carry one: `IconKey` at `:174` and `:373`, `StateIconKey` at `:237` and
`:317`, `TimeIconKey` at `:346` — so the pattern to follow is already in the file.)

⚠ **CLASSIFY IT HONESTLY (CLAUDE.md §13): this is a NEW FEATURE, not an RCA fix.** It needs a contract
field, a producer assignment, a renderer band and a resolver — four touches across three files. It is
minted READY because the design is fully determined by the mockup and the sprites are already
delivered, but **do not treat it as a regression hunt**; there is nothing to un-break.

### The second half, which IS a one-line existing-code fix
`ManageScreenVM.cs:5517` emits `StatRow("Damage", ...)`. The mockup and row 5.3 both say **`Attack`**.
`:5516-5519` are the four calls, in order Health / Damage / Range / Speed.

## 3. FILES TO EDIT

| File | Change |
|---|---|
| `Assets/_Modules/Core/Manage/ManageViewContract.cs` | Add an icon key to `ManageStatVM`, following the existing `IconKey` naming already used in this file. Keep the contract Unity-free — `ManageDumbViewRegression` pins `[contract-is-pure]`. |
| `Assets/_Modules/Core/Manage/ManageArt.cs` | Add the four stat keys beside the existing `UiFolder` constants (`:80`, `:88`), the same shape as `ResWood`. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | `TroopStatRows` (`:5498-5525`) / `StatRow` (`:5655`): carry the key. Rename the `Damage` label to `Attack` at `:5517`. |
| `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs` | `BuildStatRows`: paint the icon. |
| `Assets/Editor/Regression/*` + `DataRegression.cs` | RED-first case + registration. |

## 4. ACCEPTANCE

1. **Row 5.3 from a frame:** a fresh `RunManageFlowMapCaptureHeadless`; open
   `ManageFlow_ARMY_action_2670x1200.png` and see an icon against each of the four stats.
2. The label reads **`Attack`**, not `Damage`, on both the Archer and Outrider cards.
3. ⛔ **Greyscale is the gate (WO-1566 C8, owner is red/green colourblind):** every stat stays
   identifiable with hue stripped — the icon is an ADDITION to the worded label, never a replacement.
   The label must survive.
4. **A resolution oracle, not a source lint:** the four keys must be proven to RESOLVE, the way
   `ManagePortraitCoverageRegression` proves portrait keys (`MANAGE_PORTRAIT_COVERAGE_OK ... keys
   resolve`). ⛔ A case that only proves the constants exist would pass with all four PNGs deleted.
5. RED-first: the new case fails against today's tree.
6. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.
7. `**Status:**` -> `AWAITING OWNER MATCH` per WO-1566 §2.0.

## 5. WHAT NOT TO TOUCH

- ⛔ **ROW 5.4 / the train cost band.** See the scope box. `TrainCostText = ""` is owner ruling WO-1387.
  Do not add a cost row, do not "restore" a price, do not delete the reasoning comment at `:5246-5257`.
- ⛔ **`TroopStatRows`' four-stat choice and their values.** Only the label string and the icon channel
  move; no stat is added, removed or recomputed.
- ⛔ **`StatRow`'s gold delta / BOLD promotion** — `[detail-next-by-weight]` pins it and the Archer
  frame proves it renders.
- ⛔ **`ManageWorkspacePanel` must stay a plain class.** `ManageDumbViewRegression` pins 16 forbidden
  shapes, the using-allowlist and not-a-MonoBehaviour. Adding an icon must not import a new namespace
  outside that allowlist.
- ⛔ **The BUILDING stat rows** (`BuildingStatRows`, `:5583`) — a different row (3.3) and a different
  ticket (**WO-1653**). Do not fold them together.
- ⛔ Do not touch `ManageArt.BuildingPortraitKey` or its verbatim-id rule (`ManageArt.cs:306`, `:328`).
