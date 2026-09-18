# WO-1840 — Language setting just cycles; needs a real picker

**Status: DONE (code)** - committed `def97a479`. Was never flipped from READY FOR LEAD REVIEW despite
landing; caught during a board review. Gate proof: this file has been part of the combined tree on
every `COMPILE_GATE_OK` + `REGRESSION_OK 578/578` run tonight (multiple fresh-log passes,
2026-09-18), so the picker code compiles clean and breaks nothing - that satisfies the brace/NUL
and gate acceptance items. **Two acceptance items remain open, PO-owned, not CLI-closable:** a
device/headless capture actually showing the picker UI, and the owner's own felt-verification of
the widget shape (explicitly flagged in the implementation notes as "first pass, owner may
redirect"). Not marking fully DONE until those close. PRIOR STATUS: READY FOR LEAD REVIEW

## Implementation (lane, 2026-09-17)

`Assets/_Modules/Settings/SettingsController.cs` — the cycle handler is replaced by a real picker:

- `OnChooseLanguageClicked` now opens **its own Obsidian popup** (`ElarionUiKit.BuildObsidianModal`,
  `ModalArchetype.Standard`, sortingOrder 32500 so it sits above Settings' 32000), titled with the
  existing `settings.section.language` copy. Chosen over an in-screen radio row because the settings
  body bands against the hand-summed `RequiredLadderPx` constant — a ten-row list inside it would
  have to re-sum that constant again with every locale table that ships.
- One full-width row per `LocalText.AvailableLocales` entry, on a px ladder inside the existing
  `BuildScrollHost` (120 px button on a 132 px rung, ≥ the 112 px kit touch floor, so
  `ClampMinTouch` never inflates a row); content height = `max(body px, rows needed)`, so a short
  landscape body scrolls instead of squeezing rows.
- Tapping a row calls `LocalText.TrySelectLocale(code)` for **that** locale and destroys the picker.
  A refusal is `FlowTrace.Warn`-ed, never swallowed; the build is wrapped in `Guard.Try`.
- **Non-colour-only current-locale marker:** `[*]` vs `[ ]` in the label (same grammar as
  `HeartfireCharges`) **plus** the shared gold `InkButtonLabel` underline; the Yellow face is a
  third, redundant cue.
- Row copy uses only the EXISTING keys (`settings.language.choose` / `.chooseBeta`) — no new
  canonical-JSON entries in any of the ten locale tables. That means a row reads
  `[*] Choose: Deutsch · Beta`; **first pass, owner may redirect the row wording.**
- `_deviceLanguageButton` untouched. No `PanelHandle` registered (Settings already holds the
  arbiter slot; the picker is a child surface), and `Close()` / `OnDestroy()` tear the picker down
  so a Back / arbiter swap can never orphan it. An open picker retexts in place on
  `LocalText.Changed`.
- `LocalText` locale-resolution logic untouched.

Lane checks: `python tools/gate_brace.py` → `GATE_BRACE_SUMMARY bad=0 of 1`; 90/90 raw braces; zero
NUL bytes. No Unity process run by the lane — the lead gates the combined tree.

## Owner report

> "when you go to settings to change the language, it only allows you to click the button and it
> just changes to another language. It should probably be either a drop-down or a radio button
> set something that allows you to select what it is even if it's [its] own pop-up, they'll let
> you select it."

## Confirmed at source

`Assets/_Modules/Settings/SettingsController.cs:751` `OnChooseLanguageClicked`:

```csharp
private void OnChooseLanguageClicked()
{
    var locales = LocalText.AvailableLocales;
    // ... finds current index ...
    LocalText.TrySelectLocale(locales[(selected + 1) % locales.Count].Code);
    RefreshLanguageControls();
}
```

This is a pure "next language in the list" cycle — a player who overshoots their target language
has to keep tapping through every other locale to get back to it. There is no picker UI at all.

## Fix

Replace the cycle-on-tap behavior with an actual selection surface. The WO deliberately does not
mandate one exact widget (dropdown vs. radio list vs. popup) — the owner named several acceptable
shapes and said "even if it's its own pop-up" is fine. Pick whichever fits the project's existing
Obsidian/Elarion UI kit conventions best (check `ElarionUiKit` for an existing list/picker pattern
before inventing a new one), and:

1. Tapping the "Choose Language" control opens a picker listing every `LocalText.AvailableLocales`
   entry by its display name (not just its code).
2. Tapping an entry in the picker calls `LocalText.TrySelectLocale(code)` for that specific locale
   and closes the picker.
3. The current locale is visually indicated in the picker (checkmark, highlight, etc. — not by hue
   alone, the owner is colorblind).
4. `_deviceLanguageButton` (the separate "use device language" toggle) is unaffected by this change
   — only the explicit-choice control changes shape.
5. Respect existing `PanelManager`/modal conventions already used elsewhere in Settings so this
   doesn't fight with any open-panel/close-grace logic.

## What NOT to touch

- `LocalText.TrySelectLocale`, `LocalText.UseSystemLocale`, or any locale-resolution logic itself —
  this is a UI-surface change only, the underlying locale switching mechanism is correct.
- The device-language button/toggle behavior.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched `.cs`.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] A device/headless capture shows the new picker listing every available locale with the
  current one indicated non-color-only.
- [ ] Owner felt-verifies the picker is usable and picks the exact widget style is fine — flag the
  chosen widget shape as a first pass she can redirect.
