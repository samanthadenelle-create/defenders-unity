# WO-1626 RESULT - Build Collections footer caption now routes through the kit's FitSingleLine

**Status:** IMPLEMENTED - awaiting gate (lane FOOTER-FONT 2026-09-10)
**Lane:** FOOTER-FONT, isolated worktree `.claude/worktrees/agent-a22e41529ea6dc56c`
**Base sha:** `8ed4c14ab` (fast-forwarded from `f5d39acd1`; carries WO-1623's footer band)
**Scope:** EDIT ONLY. This lane did not run Unity, did not gate, did not commit (CLAUDE.md sec.11).

---

## 1. What changed

### `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`

Inside `BuildManageDefensesFooterLink()`, the `if (label != null)` block. Twelve lines at `:303-314`
of the base file became eight comment lines plus one call, ending at `:310` post-edit:

```
ElarionUiKit.FitSingleLine(label, 20f, 24f);
```

Retired in the same edit, exactly as WO sec.4 directs:

| Base line | Content | Fate |
|---|---|---|
| `:303` | `label.enableWordWrapping = false;` | **REMOVED.** `FitSingleLine` sets `textWrappingMode = TextWrappingModes.NoWrap` (`ElarionUiKitObsidian.cs:3064`, read this session) - the same state under the current API name. Keeping it is the duplicated-state pattern this ticket exists to close. |
| `:304` | `label.enableAutoSizing = true;` | **REMOVED.** Now the factory's job (`:3066`). |
| `:305-311` | the WO-1623 six-line note ending *"do not 'tidy' it here without a ticket"* | **REPLACED** by an eight-line note naming WO-1626 and why the call goes through the factory. The old note became a copy that lies the moment the fix landed (CLAUDE.md sec.15). |
| `:312` | `label.fontSizeMin = 16f;` | **REMOVED** - the sub-floor literal that is the whole ticket. |
| `:313` | `label.fontSizeMax = 24f;` | **PRESERVED AS A VALUE** - passed as the `maxSize` argument. Not re-picked. |
| `:314` | `label.overflowMode = TextOverflowModes.Overflow;` | **REMOVED.** Now the factory's job (`:3065`). See sec.3. |

`20f` is passed rather than `16f` so the floor is stated at the call site and the clamp at
`ElarionUiKitObsidian.cs:3062` is a visible no-op rather than a silent correction (WO sec.4).

Nothing after the call writes `overflowMode`, `enableAutoSizing`, `fontSizeMin` or `fontSizeMax` on
`label` - verified by reading `:296-316` post-edit and by the grep in sec.5.

### `Assets/Editor/Regression/BuildCollectionPlayerRegression.cs`

One new pin added after the WO-1411 negative pin (base `:157-158`), in the suite's existing shape - a
source-text literal scan on the `browser` string that `:45` reads from
`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`, with a stated RED proof in the
`:101-108` / `:141-152` comment style:

```
if (!browser.Contains("FitSingleLine(label,") ||
    browser.Contains("fontSizeMin = 16f"))
    return Fail("the Manage > Defense footer caption sets its own font floor instead of the
                 kit's: a sub-floor fontSizeMin literal is back, or the label no longer routes
                 through ElarionUiKit.FitSingleLine", out reason);
```

## 2. Evidence - every constant read at source this session

| Fact | Where read |
|---|---|
| `public const float FontHardFloor = 20f;` | `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3044` |
| `public const float MinTouchPx = 112f;` | `Assets/_Modules/Core/UI/ElarionUiKit.cs:347` (WO-1623's constant; named only so the two are not confused - untouched here) |
| `ElarionUiKit` is one partial static class across those two files | both opened; the type name in the WO-1623 note is right, the file it named was not |
| `FitSingleLine` body: clamp `:3062`, `minSize > maxSize` `:3063`, `NoWrap` `:3064`, overflow `:3065`, autosize `:3066`, `fontSizeMin` `:3067`, `fontSizeMax` `:3068`, `ArmFitGuard(t)` `:3069` | `ElarionUiKitObsidian.cs:3054-3070` |
| the defect line `label.fontSizeMin = 16f;` at `:312` of the base file | `BuildCollectionBrowser.cs`, read before the edit; matches the WO |
| the existing same-file call shape `ElarionUiKit.FitSingleLine(guidanceLabel, 22f, 28f);` | `BuildCollectionBrowser.cs:461` |
| the suite reads `browser` from the browser file | `BuildCollectionPlayerRegression.cs:45` |

**`ArmFitGuard(t)` (`:3069`) is newly armed on this label.** The raw-TMP path never called it, so the
caption had no post-layout fit guard at all. That is part of what routing through the factory buys,
not just the clamp - naming it here per WO sec.1c.

## 3. The behaviour change, named deliberately (CLAUDE.md sec.11B)

**The caption's overflow mode goes from `Overflow` to `Ellipsis`.** `Overflow` paints past the rect;
`Ellipsis` truncates with a marker. This is mandated by WO sec.4 and it is the reason WO-1623 declined
to make this change.

Reason it is acceptable: at a 20 px floor inside WO-1623's 112 px band, the string
`"Already built? Manage defenses >"` is not expected to need either mode, and `Ellipsis` is the kit's
uniform answer for a single-line caption that runs out of room. **Not measured:** the caption's
resolved px at any resolution, before or after. This lane ran no capture (edit-only), so no number is
claimed - see sec.6.

## 4. What is NOT claimed

- **No felt defect.** No capture in evidence shows this caption rendering below 20. WO-1623's own
  RESULT calls the sub-floor minimum *"dormant rather than load-bearing"*. This is a
  one-owner-per-concern repair against a latent breach.
- **No rendered-px measurement.** Not taken, not inferred.
- **The suite was not RUN.** See sec.6.

## 5. Verification performed in this lane

**Acceptance 1 - RED-first, source-text.** Proven against the base blob, not from memory:
`git show 8ed4c14ab:Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs` contains
`fontSizeMin = 16f` -> **True**, contains `FitSingleLine(label,` -> **False**. Both halves of the new
pin are RED at the base sha. The bare method name `FitSingleLine(` is **True** at the base sha, which
confirms WO sec.5.2's warning: a pin on the bare name would have been green before the fix and proved
nothing.

Post-edit, on the working tree: `fontSizeMin = 16f` -> **False**, `FitSingleLine(label,` -> **True**.

**Acceptance 3 - the Ellipsis pin at `:116-120`, CONFIRMED GREEN, not assumed.** (The WO writes
`~:117-120`; the `if` opens at `:116` in the file as read.) Every literal that pin tests was evaluated
against the post-edit browser source in Python, using the suite's exact strings:

| Literal | Pin requires | Post-edit |
|---|---|---|
| `TextOverflowModes.Ellipsis` | absent | **absent** |
| `TextOverflowModes.Truncate` | absent | **absent** |
| `buttonFace = available ? "PLACE"` | present | present |
| `enableWordWrapping=true` | present | present |
| `enableAutoSizing=true` | present | present |
| `overflowMode=TextOverflowModes.Overflow` | present | present |

The last three carry **no spaces around `=`** and live in the card-copy helper at `:825-829`, which
this edit did not touch. The footer block's spaced forms were never what satisfied that pin. The
`Ellipsis` flip happens inside `ElarionUiKitObsidian.cs:3065`, in a file this suite does not read, so
no such literal enters the browser source. **The pin was not relaxed, re-pointed or widened.**

The replacement comment was deliberately written to avoid spelling either enum member, since that pin
scans the whole file including comments.

The other `browser` pins in the suite were evaluated the same way and all hold: the three footer-link
literals at `:153-156` (link name, caption string, `PanelRouter.Open(PanelId.Manage, "Defense")`) all
present; the `"Upgrade Defenses"` negative pin at `:157-158` still absent.

**Acceptance 5 - brace + NUL.** `python tools/gate_brace.py` on both edited files:
`GATE_BRACE_SUMMARY bad=0 of 2`, exit 0. Raw counts: browser 65/65, regression 23/23. Byte scan for
`\x00`: none in either file.

## 6. Acceptance 4 - DEFERRED, not dropped

WO sec.5.4 asks for the suite's own pass/fail reason string quoted verbatim. **This lane cannot
execute it** - the brief is edit-only, no Unity. It is not skipped silently: the lead's batch gate run
judges this suite by its own marker on a fresh log. No marker string is written into any file by this
lane.

## 7. Pins honoured

- `ElarionUiKitObsidian.cs` and `ElarionUiKit.cs` - **opened, cited, unmodified.** No kit constant was
  raised or lowered to accommodate this caption.
- WO-1623's band geometry - `FooterLinkBottomInsetPx`, `FooterLinkBandPx`, the `x .28-.72` fractions,
  and the `FlowTrace.Step` that records them - **untouched.** Not a px re-tuned.
- The caption TEXT - unchanged.
- `fontSizeMax = 24f`'s value - preserved as the `maxSize` argument.
- **Seen and left, per WO sec.6:** `manageTitle.fontSizeMin = 15f` (base `:410`, post-edit `:406`) and
  `t.fontSizeMin=Mathf.Min(20f,size)` (base `:827`, post-edit `:823`; both re-grepped). Each is
  its own legibility ruling; widening this ticket to them would make the diff unreviewable.
  `explicitTitle` (`:224-226`) and `buttonLabel` (`:546-549`) are already at the floor.
- No other file in `Assets/_Modules/Village/BuildMode/`, no `hud-areas.json`, no panel router, no
  `ManageScreenPanel.cs`.

## 8. What a UI capture must show

On the Build Collections screen, in the footer band below the category grid:

- The caption **`Already built? Manage defenses >`** reads on **one line**, fully, with **no ellipsis
  marker**. An ellipsis appearing is the one visual signal that the Overflow -> Ellipsis flip changed
  something the band cannot absorb - and it is the only thing this change could regress.
- Glyphs sit at or near the **24 px max** (the 112 px band gives the autosizer room), and **never
  below 20** - that is the clamp the fix installs.
- The band's geometry is unchanged from the WO-1623 capture: same position, same 112 px height, same
  `x .28-.72` width.
- Tapping it still opens Manage -> Defense.

## 9. Files changed (2)

- `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`
- `Assets/Editor/Regression/BuildCollectionPlayerRegression.cs`

Plus board: this file, and the `**Status:**` flip in
`WorkOrders/WORK_ORDER_1626_build_collections_footer_caption_bypasses_the_kit_font_floor.md`.
