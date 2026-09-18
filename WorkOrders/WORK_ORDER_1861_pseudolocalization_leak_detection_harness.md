# WORK ORDER 1861 — Pseudolocalization leak-detection harness (the overnight localization QA loop)

**Status:** READY TO IMPLEMENT

**Minted:** 2026-09-17, by the CLI lead, from the owner's overnight directive: a repeatable, all-night
loop that finds every remaining hardcoded/untagged player-facing string by making it visually obvious,
screenshotting every screen, and having a review pass catch anything that still reads as English.

**Owner's explicit correction on pacing:** *"the final pass of the night is a fable pass to fully
validate all slowly and methodically. It is not a race and I want them done methodically."* This
ticket builds the DETECTOR; WO-1857 (and its sharded lanes) is the thing being detected against. The
inner loop (code-based detection) should run fast and often; Fable's own full validation pass is the
deliberately slow, methodical final gate — do not conflate the two.

## Why pseudolocalization, not an 11th fake locale

An earlier version of tonight's plan proposed adding a synthetic "blank" locale to strip all words for
visual comparison. **Do not do this.** `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json`
and multiple DataRegression checks (`LocalizationAuthorityRegression`, `LocaleParityRegression`,
`GooglePlayLocalizationVariantPolicyRegression`) hardcode strict `RequiredLocales`/`EnabledLocales`
sets. An 11th locale would need JSON files, Unity table entries, and would trip every one of those
checks for no real benefit.

**The correct mechanism is pseudolocalization through the existing provider seam.**
`Assets/_Modules/Core/UI/LocalText.cs` declares `ILocalTextProvider` (`TryResolve(key, args, out
value)`) and `LocalText.InstallProvider(provider)` — a single swappable authority already designed for
exactly this kind of interception. Build a **decorator provider** that wraps whatever real provider is
installed: it calls the real provider's `TryResolve`, then transforms the returned string before
handing it back. This touches ZERO locale JSON files, ZERO Unity table assets, and trips none of the
locale-count regressions, because from every other system's point of view the active locale never
changed.

## Scope

### Part A — the pseudoloc provider decorator

New class (e.g. `Assets/_Modules/Core/UI/PseudolocTextProvider.cs`), implementing `ILocalTextProvider`,
wrapping a real inner provider:
- `TryResolve` calls the inner provider, then transforms the result: **keep digits and `{...}`/`{0}`-
  style format placeholders completely intact** (breaking these would cascade into unrelated
  formatting bugs, not prove anything about localization coverage), and map every Latin letter
  (`[A-Za-z]`) to a **Cyrillic** look-alike or Cyrillic block character. Cyrillic is the right choice
  for two reasons the owner should know: (1) it is visually unmistakable from English to a vision
  model or a human, and (2) `ru` is one of only 6 locales with a live Unity String Table asset AND its
  font-glyph coverage was already fixed earlier this session (the tofu-glyph fix), so there is no risk
  of the transform itself producing tofu.
- Enable/disable via a flag read the same way WO-1775's `DevScenarioIntent.cs` already reads intent
  extras / `ff.*` PlayerPrefs — add `ff.pseudoloc` (or similar) as a new dev-only flag, `#if
  QA_SCENARIO_BUILD`-gated or equivalent, so it can never reach a shipping build. Read
  `Assets/_Modules/Core/FeatureFlags.cs` for the existing flag-declaration convention before adding a
  new one.
- Installed via `LocalText.InstallProvider(new PseudolocTextProvider(realProvider))` when the flag is
  set, at the same point the real provider is normally installed — find that call site (grep
  `InstallProvider(`) before wiring this in.

### Part B — the cheap, deterministic code-based leak detector (the INNER loop, run often)

Extend `Assets/Editor/UICaptureLaunch.cs`'s existing capture sweep (WO-1860 just re-verified and
expanded this harness — read its current state first) with a new oracle that runs WHILE pseudoloc is
active: walk every `TMP_Text`/`Text` component in the currently-loaded scene/panel, and for any whose
`.text` matches `[A-Za-z]{2,}` (two or more consecutive Latin letters — one stray letter is noise, a
real word is not), record `{component path, scene/screen name, offending substring, full text}` to a
report file. This is deterministic, free, has exact source attribution (which component, which
screen), and needs no vision model at all for the vast majority of the night's iterations.

**Allowlist, or this never converges (owner-directed: be thorough, not stuck in a loop).** Real,
legitimate Latin text will appear under pseudoloc: player-chosen hero names, clan/Remnant invite codes
(e.g. `GZL2ED`), wallet address fragments, brand names that are correct in every language (Solana, SKR,
Cherry, Elarion, Remnant — once WO-1859 lands, "Remnant" itself is canon, not a leak), and any
raw non-localized identifier that is CORRECTLY not localized (an enum value used as a data key, not
copy). Build `docs/localization/PSEUDOLOC_ALLOWLIST.md` — a durable, growing dictionary of
confirmed-legitimate exceptions (per this repo's own `audit-outputs-as-known-dictionaries` convention)
that the detector consults before flagging. First entries: known brand/proper nouns above, and the
existing 6-locale/10-locale distinction (an ar/ja/ko/zh-Hans screen showing Latin because its Unity
table doesn't exist yet is a KNOWN GAP already documented this session, not a new leak — cite it in
the allowlist rather than re-discovering it every round).

### Part C — Opus visual pass (the OUTER, occasional check — cost-bounded)

Per standing cost discipline (this project is under a $200/month stop-loss during the hackathon push),
do **NOT** run a vision model over all ~267 screenshots every round. Opus's role is:
1. A genuinely final pass once the code-based detector reports zero un-allowlisted leaks, reviewing
   the full screenshot catalog once as a real independent check (catches baked-text sprites or art
   assets the code oracle can't see, since it only reads live `Text`/`TMP_Text` components).
2. Spot-checks (a small, deliberately-chosen sample) on screens the code oracle already calls clean,
   as a sanity check on the oracle itself, not a full re-review every round.

### Part D — the fix-and-repeat loop

Any leak the code oracle reports (after allowlist filtering) is a work item for a low-tier (Haiku)
lane: locate the literal string at the cited component/screen, wire it to a proper locale key (or add
it to the allowlist if investigation shows it's actually legitimate and was mis-flagged — but this
requires a real reason, not just "make the flag go away"), re-run the capture + detector for that
specific screen, confirm clean, move to the next.

## Non-scope

- No changes to any locale JSON file or Unity table asset in THIS ticket — that's WO-1857 and its
  sharded lanes. This ticket builds the detector only.
- No new shipped locale, no changes to `RequiredLocales`/`EnabledLocales`.
- Do not run Opus in a tight loop — cost discipline above is binding, not a suggestion.

## Acceptance criteria

- [ ] `PseudolocTextProvider` correctly transforms resolved strings (digits/placeholders intact, Latin
      letters mapped to Cyrillic) without touching any locale file.
- [ ] Enabling pseudoloc via the dev-only flag and re-running WO-1860's capture sweep produces visibly
      transformed screenshots for every screen that reads through `LocalText`.
- [ ] The code-based leak detector correctly flags a deliberately-reintroduced hardcoded string (test
      this: temporarily hardcode one, confirm the oracle catches it, then revert the test hardcode).
- [ ] The allowlist correctly suppresses known-legitimate Latin (player name, invite code) without
      suppressing a genuine leak planted in the same test.
- [ ] `python tools/gate_brace.py` + NUL scan on every `.cs` file touched. Unity `COMPILE_GATE_OK` +
      `REGRESSION_OK <n>/<n>` on the combined tree.
- [ ] The pseudoloc flag/provider can NEVER activate in a non-QA_SCENARIO_BUILD context — pin this
      with a regression the same way WO-1775's own kit is pinned as ship-quarantined.

## Test plan

1. Enable pseudoloc, run WO-1860's full capture sweep, visually confirm 3-5 screenshots show
   transformed (Cyrillic-mapped) text where English used to be.
2. Plant a deliberate hardcoded English string in a test UI element, confirm the code oracle flags it
   with correct component/screen attribution, then revert the plant.
3. Confirm a known-legitimate Latin string (a test player name) does not get flagged once allowlisted.
4. Full regression suite green.

## Rollback

Remove the provider decorator, the flag, and the oracle addition — all additive, no schema/save
implications, and the flag's ship-quarantine regression proves it never reached a real build even if
left in mid-development.

## Copy rules

N/A — this ticket does not write player-facing copy.
