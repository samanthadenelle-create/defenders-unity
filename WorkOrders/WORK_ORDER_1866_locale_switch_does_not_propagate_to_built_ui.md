# WO-1866 — Locale switch does not propagate to already-built UI

**Status: READY TO IMPLEMENT**

## Owner report

Live, while playing: *"switching languages from french to english is still in french"* — used the
WO-1840 Settings language picker, the game stayed in French.

## Root cause — PROVEN from real captured data, not inferred

RCA agent traced this from source + a real device/editor capture
(`Logs/f8-inbox/capture-20260918-085337-seq5615.md:90-98`, 2026-09-18 08:53):

```
[Flow:Settings] language picked from picker: code=fr
...
[Flow:Localization] Loaded 'GameStrings' for locale 'en'.
[Flow:Catalog] resolve 'Data/Canonical/fr.json' <- Resources (70879 chars)
...
[Flow:Localization] Loaded 'GameStrings' for locale 'fr'.
```

**`TrySelectLocale` itself is NOT the bug.** `UnityLocalizationProvider.SelectOrReloadLocale` ->
`BeginTableLoads` sets `_selectedLocaleCode` synchronously on a successful switch
(`Assets/_Modules/Localization/UnityLocalizationProvider.cs:191-193`) — `LocalText.LanguageCode`
reports the new code immediately, confirmed by the capture above.

**The real bug: `LocalText.Changed` (`Assets/_Modules/Core/UI/LocalText.cs:69`) is the ONLY push
signal any already-built widget has to re-pull text after a switch — `LocalizedText.Resolve()` /
`LocalText.Get()` are pull-only, nothing re-invokes them on its own — and it has exactly TWO
subscribers in the entire game:**
- `Assets/_Modules/Settings/SettingsController.cs:157-158,164,167` (`OnLocalizedTextChanged`) —
  retexts only the Settings modal's own widgets (`if (_modal == null) return;`).
- `Assets/_Modules/HUD/Kit/HudKitController.cs:3379-3380,3385-3404` (`RefreshLocalizedHudCopy`) —
  retexts a hardcoded, narrow list only (peaceful-dock labels, outside-dock labels, the collectors
  chip, the heart-plate name, heartfire, the flee label).

Every other one-shot-resolved label in the game has NO subscriber and is architecturally unable to
update after a runtime locale switch — confirmed instances, already proven correctly wired with
correct `fr.json` values on disk by the sibling WO-1857 lane, yet rendering the wrong language live:
- `settings.title` (HUD gear-dock caption) — one-shot resolve at `HudKitController.cs:1721`, not in
  `RefreshLocalizedHudCopy`'s list.
- `storeWordmark` (Night Market card title) — one-shot resolve at `PackStore.cs:995` and
  `NightMarketSharedCardSession.cs:42`, no subscriber at all.

This is exactly the owner's symptom: whichever screen she was on after tapping the picker was
simply not one of the two retext lists, so it kept showing whatever locale it was built under.

## Secondary, independently-proven bug (same investigation, same file)

`BeginTableLoads` (`UnityLocalizationProvider.cs:184-213`) fires `Changed` TWICE on a switch to a
non-English locale — once when the secondary English-table load finishes, again when the target
table finishes — and `TryResolve` (`:64-78`) falls back to `_englishTable` whenever
`_selectedTable` is still null. The capture shows the English load completing BEFORE the French
load. So the FIRST `Changed` fires while `_selectedTable` (fr) is still null: any listener that
retexts on that first `Changed` (Settings, HudKit) can momentarily render ENGLISH even though
`LocalText.LanguageCode` already reports the new locale. Real race, separate from the coverage gap
above — it can make even the two "correctly wired" retext paths flash the wrong language for one
load tick.

## Fix — two parts, both scoped

**A. Coverage gap (primary cause of the owner's report).** Two acceptable shapes, pick one:
- **Minimal:** add every one-shot, persistent text owner outside Settings/HudKit as a new
  `LocalText.Changed` subscriber that retexts itself on fire — at minimum
  `HudKitController.cs:1721` (`settings.title`) and `PackStore.cs:995` /
  `NightMarketSharedCardSession.cs:42` (`storeWordmark`), mirroring the existing
  `OnLocalizedTextChanged`/`RefreshLocalizedHudCopy` pattern.
- **Durable (preferred if time allows):** a small self-updating localized-label component any text
  owner can attach, subscribing/unsubscribing to `LocalText.Changed` in `OnEnable`/`OnDisable`, so
  the NEXT hardcoded-string-turned-key doesn't reintroduce this class of bug. Migrate the two
  confirmed call sites onto it as the first users; migrating every existing label is NOT required
  by this WO — that would balloon scope well past this bug fix.

**B. The race.** In `BeginTableLoads`, do not invoke `Changed` on the English-secondary-table
completion while a different selected-locale load for the SAME generation is still outstanding —
fire once both loads for that generation have settled, or once the SELECTED table specifically is
ready, not on every individual `CompleteTableLoad`.

## What NOT to touch

- `TrySelectLocale`, `SelectOrReloadLocale`'s locale-selection logic itself — proven correct.
- The WO-1840 picker UI/interaction — proven correct, this is a downstream propagation bug.
- Any translation content / canonical JSON — this is a pure runtime-wiring bug, no strings need
  editing to fix it.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched `.cs` (`python tools/gate_brace.py <paths>`).
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] A live repro: switch locale via the Settings picker while a screen showing `settings.title`
  and/or the Night Market card (`storeWordmark`) is visible, or a headless/instrumented equivalent
  proving both retext on the SAME switch that already updates the Settings modal.
- [ ] The race fix: prove (via `FlowTrace`/capture) that `Changed` no longer fires while the
  selected-locale table is still null on a switch to a non-English locale.

## Evidence

RCA agent's proof capture: `Logs/f8-inbox/capture-20260918-085337-seq5615.md:90-98`,
`capture-20260918-085334-seq5614.md:95`, `capture-20260918-085332-seq5613.md:135`.
