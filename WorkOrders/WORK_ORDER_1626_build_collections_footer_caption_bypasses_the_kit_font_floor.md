# WO-1626 - Build Collections footer caption writes fontSizeMin straight onto TMP, bypassing the kit's font floor

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; bumped 1625 -> 1628 in the SAME edit)
**Silo / Lane:** Village / BuildMode UI (`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`)
**Severity:** P2 legibility. Currently **DORMANT, not shipping** - see sec.2. This is a
one-owner-per-concern repair, not a felt defect.
**Type:** EXISTING system. The footer link exists, is on-screen, and passes its touch floor as of
WO-1623.
**Owner words:** none - lane finding, handed back by the WO-1623 lane and re-proven at source here.

---

## 1. What was measured (read at source 2026-09-10)

### 1a. The hand-back

`WorkOrders/WORK_ORDER_1623_build_collections_manage_defenses_footer_link_under_the_touch_floor.RESULT.md:131-141`,
section headed *"Follow-up, NOT taken here (out of WO-1623 scope, section 7)"*:

> `BuildCollectionBrowser.cs:312` sets `label.fontSizeMin = 16f` on the footer caption. The kit's own
> floor is `ElarionUiKit.FontHardFloor = 20f` [...] The literal bypasses that clamp because it is
> written straight onto the TMP component. [...] Routing it through `FitSingleLine` would also switch
> the label to `Ellipsis` overflow, which is a behaviour change this ticket has no mandate for. Worth
> its own small ticket.

This is that ticket.

### 1b. The line, as it stands in the tree NOW

`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`, inside
`BuildManageDefensesFooterLink()` (declared `:272`, called `:246`):

- `:299` `link.name = "ManageDefensesFooterLink";`
- `:303` `label.enableWordWrapping = false;`
- `:304` `label.enableAutoSizing = true;`
- `:305-311` a six-line in-code note left by the WO-1623 lane, which states the floor breach, states
  that WO-1623 deliberately left it, and ends *"do not 'tidy' it here without a ticket."*
- `:312` `label.fontSizeMin = 16f;`
- `:313` `label.fontSizeMax = 24f;`
- `:314` `label.overflowMode = TextOverflowModes.Overflow;`

**The 1623 RESULT cited `:312` and `:312` is still the line.** Confirmed by direct read this session,
not carried from the doc.

### 1c. The kit constants, at source

`ElarionUiKit` is a **partial static class across two files** - `Assets/_Modules/Core/UI/ElarionUiKit.cs:50`
and `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:54` - so the type name in the 1623 note is right
while the file is not. Read each in its own file:

- **`ElarionUiKitObsidian.cs:3044`** - `public const float FontHardFloor = 20f;`
- **`ElarionUiKit.cs:347`** - `public const float MinTouchPx = 112f;` (the constant WO-1623 was about;
  named here only so the two are not confused again)

`FitSingleLine`, `ElarionUiKitObsidian.cs:3054-3070`, in full:

```
public static void FitSingleLine(TMP_Text t, float minSize = 0f, float maxSize = 0f)
{
    if (t == null) return;
    if (maxSize <= 0f) maxSize = t.fontSize;
    if (minSize <= 0f) minSize = FontFloor;
    // WO-714 P7 (WO-693 mobile floor, factory-enforced): no caller may auto-shrink text
    // below the FontHardFloor readability floor - an explicit sub-floor minSize is clamped
    // UP here (ellipsis past the floor, never sub-legible phone text).
    if (minSize < FontHardFloor) minSize = FontHardFloor;     // :3062
    if (minSize > maxSize) minSize = maxSize;                  // :3063
    t.textWrappingMode = TextWrappingModes.NoWrap;             // :3064
    t.overflowMode = TextOverflowModes.Ellipsis;               // :3065
    t.enableAutoSizing = true;                                 // :3066
    t.fontSizeMin = minSize;                                   // :3067
    t.fontSizeMax = maxSize;                                   // :3068
    ArmFitGuard(t);                                            // :3069
}
```

Its own comment says *"no caller may auto-shrink text below the FontHardFloor readability floor"*.
`:312` is a caller that does, because it never enters the method.

**`ArmFitGuard(t)` at `:3069` is the third thing the current code does not get** - a post-layout
guard the raw-TMP path never arms. Name it in the RESULT; it is part of what routing through the
factory buys, not just the clamp.

### 1d. The screen already calls the factory elsewhere

Same file, `:461`: `ElarionUiKit.FitSingleLine(guidanceLabel, 22f, 28f);`. So the call shape, the
using, and the assembly reference are all already present in this file. Nothing structural is needed.

## 2. What is NOT claimed

- **NOT claimed: that any player has ever seen sub-floor text here.** WO-1623 gave the band a full
  `MinTouchPx` (112 px) height, and the 1623 RESULT's own words are *"It is now **dormant** rather
  than load-bearing - a 112 px band gives the autosizer room to sit at `fontSizeMax`."* No capture in
  evidence shows this caption rendering below 20. **Do not write a felt-defect claim into the
  RESULT.** This is a one-owner repair against a latent breach, and saying so is the honest framing
  (CLAUDE.md sec.11B).
- **NOT claimed: that Ellipsis is the right answer for this caption.** `FitSingleLine` flips
  `overflowMode` to `Ellipsis` (`:3065`) where the line currently reads `Overflow` (`:314`). That IS
  a behaviour change and it is the reason WO-1623 declined. It is mandated here; see sec.3.
- **NOT measured: the caption's rendered px at any resolution after WO-1623.** The 1623 evidence
  measured the BAND geometry, not this label's resolved font size. If the implementing lane wants a
  number, capture one - do not infer one.
- **NOT in evidence: any other sub-floor caller on this screen being a defect.** Two more exist in
  this same file and are recorded in sec.6 as seen-and-pinned, not as work.

## 3. Target - what "fixed" means

The footer caption's font floor is decided by the kit, in one place, like every other caption on the
screen. No `fontSizeMin` literal below `FontHardFloor` survives at this call site.

## 4. The fix

`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`, the footer-link label block:

Replace the raw triple (`:312` `fontSizeMin`, `:313` `fontSizeMax`, and the autosizing/overflow lines
it belongs to) with the factory call, matching the shape already used at `:461`:

```
ElarionUiKit.FitSingleLine(label, 20f, 24f);
```

- Pass **20f**, not 16f - state the floor at the call site so a reader sees the intent, and let
  `:3062` be a no-op rather than a silent correction.
- `24f` preserves the existing `fontSizeMax` (`:313`) exactly. **Do not re-pick the max.**
- `FitSingleLine` sets `textWrappingMode = NoWrap` (`:3064`), which is what `enableWordWrapping =
  false` (`:303`) already means - the redundant line may go or stay; say which and why.
- The `enableAutoSizing = true` (`:304`) and `overflowMode` (`:314`) lines become the factory's job
  (`:3066`, `:3065`). Removing them is the point; leaving one behind that re-sets `Overflow` AFTER
  the call would silently undo the change.

**THE OVERFLOW CHANGE IS MANDATED AND MUST BE STATED.** The caption goes from `Overflow` (paints past
its rect) to `Ellipsis` (truncates with a marker). Per CLAUDE.md sec.11B, name it in the RESULT as a
deliberate behaviour change with the reason: at a 20 px floor in a 112 px band the string
`"Already built? Manage defenses >"` is not expected to need either, and `Ellipsis` is the kit's
uniform answer for a single-line caption that runs out of room.

**Retire the note at `:305-311` in the SAME edit.** It says *"do not 'tidy' it here without a
ticket"*; this ticket exists and the fix lands, so the note becomes a copy that lies. Replace it with
one short line naming WO-1626 and why the call goes through the factory. Leaving it is the
duplicated-state failure CLAUDE.md sec.15 forbids.

## 5. Acceptance

1. **RED-first, source-text:** before the edit, assert `BuildCollectionBrowser.cs` contains
   `label.fontSizeMin = 16f`. After the edit it must not, and must contain the `FitSingleLine` call
   on the footer label.
2. Extend `Assets/Editor/Regression/BuildCollectionPlayerRegression.cs` beside its existing footer
   pin. **That pin is at `:153-156`** and reads:
   ```
   if (!browser.Contains("link.name = \"ManageDefensesFooterLink\"") ||
       !browser.Contains("\"Already built? Manage defenses >\"") ||
       !browser.Contains("PanelRouter.Open(PanelId.Manage, \"Defense\")"))
   ```
   Add, in that suite's existing shape (a source-text literal scan with a stated RED proof):
   - the file must contain **`FitSingleLine(label,`** - pin that LITERAL, not the bare method name.
     `:461` already contains `ElarionUiKit.FitSingleLine(guidanceLabel, 22f, 28f)`, so a pin on
     `FitSingleLine(` alone is GREEN BEFORE THE FIX and proves nothing.
   - the file must **not** contain `fontSizeMin = 16f` (`:312` is the only `16f` font literal in the
     file; the other `16f`, `FooterLinkGridGapPx`, is a px gap)
   Write the RED proof as a comment the way `:122-130` and `:141-147` do - this suite documents why
   each pin exists and what breaks it, and a new pin without that comment does not match the file.
3. **READ THIS BEFORE ASSUMING A CONFLICT:** the same suite fails at `:117-120` if the browser source
   contains the literal `TextOverflowModes.Ellipsis`:
   ```
   if (browser.Contains("TextOverflowModes.Ellipsis") || browser.Contains("TextOverflowModes.Truncate") || ...)
       return Fail("collection cards can truncate/ellipsize required copy or lack a full-copy wrapping path", ...)
   ```
   **This pin scans SOURCE TEXT of `BuildCollectionBrowser.cs` only.** The fix puts no such literal in
   that file - the flip happens inside `ElarionUiKitObsidian.cs:3065`. So the pin stays green and
   **must not be relaxed, re-pointed, or widened.** Confirm it green in the RESULT and say that you
   confirmed it rather than assuming it. If it does red, STOP and hand back - a genuine conflict
   between this ticket and that pin is a ruling, not an edit.
4. Run the suite and quote its own `BUILD_COLLECTION_PLAYER_OK` / `_FAIL` reason string verbatim in
   the RESULT.
5. Brace + NUL check the two edited `.cs` files, and run `python tools/gate_brace.py` on them
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).
6. Gate and screenshot per the lead's batch. **This lane does not gate or commit** (CLAUDE.md sec.11).

## 6. Pins - what must not move

- **The three footer-link literals at `:153-156` of the regression** - the link name, the caption
  string `"Already built? Manage defenses >"`, and `PanelRouter.Open(PanelId.Manage, "Defense")`. The
  door and its destination are the thing that suite protects; this ticket touches typography only.
- **The negative pin at `:157-158`** (`"Upgrade Defenses"` must not return). Untouched.
- **The Ellipsis/Truncate source-text pin at `:117-120`.** See sec.5.3.
- **WO-1623's band geometry**: `FooterLinkBottomInsetPx`, `FooterLinkBandPx` (= `ElarionUiKit.MinTouchPx`),
  the `x .28-.72` fractions, and the `FlowTrace.Step` at `:316` that records them. **The band is not
  this ticket.** Do not re-tune a px.
- **`label.fontSizeMax = 24f`'s VALUE.** Preserved as the `maxSize` argument.
- **`ElarionUiKitObsidian.cs` and `ElarionUiKit.cs` are READ-ONLY in this lane.** `FontHardFloor`,
  `FitSingleLine`, `FitBlock`, `ArmFitGuard`, `MinTouchPx` - open them, cite them, change nothing.
  Raising or lowering a kit constant to accommodate one caption is the inverse of this fix.
- **Two other sub-floor writers in the SAME file, SEEN and OUT OF SCOPE:** `:409-410`
  (`manageTitle.fontSizeMin = 15f`) and `:826-829` (`t.fontSizeMin = Mathf.Min(20f, size)`). Recorded
  here so the next reader knows they were seen and left. Do **not** widen this ticket to them - each
  is its own legibility ruling with its own history, and a sweep is how a small fix becomes an
  unreviewable diff. `:224-226` (`explicitTitle`, min 20f) and `:546-549` (`buttonLabel`, min 20f) are
  already at the floor.

## 7. What NOT to touch

- No other file in `Assets/_Modules/Village/BuildMode/`.
- No kit file, no `hud-areas.json`, no panel router, no `ManageScreenPanel.cs`.
- Do **not** change the caption's TEXT. It is pinned by the regression and it is player-facing copy.
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

## 8. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1626_build_collections_footer_caption_bypasses_the_kit_font_floor.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.
