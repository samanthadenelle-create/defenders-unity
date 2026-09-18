# WORK ORDER 1861 — Pseudolocalization leak-detection harness (the overnight localization QA loop)

**Status:** DONE - committed 6b24fc6c6, COMPILE_GATE_OK + REGRESSION_OK 578/578 verified. PRIOR: READY FOR LEAD REVIEW

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

---

# IMPLEMENTATION RECORD (2026-09-17) — files, proof, the HOW-TO for tonight's lanes, and the flags the lead must read

## Files written

| File | What |
|---|---|
| `Assets/_Modules/Core/UI/PseudolocTextProvider.cs` | **NEW.** Part A. The `ILocalTextProvider` decorator + the static transform/install authority. Whole file wrapped `#if UNITY_EDITOR \|\| QA_SCENARIO_BUILD`. |
| `Assets/_Modules/Core/UI/LocalText.cs` | **EDIT.** The post-resolve hook (`PseudolocHook` + `Pseudo()`), applied to the **three table-resolved returns in `TryGet` only**. Guarded by the same define pair; a shipping build compiles the class with no hook field and an identity `Pseudo`. |
| `Assets/_Modules/Localization/LocalizationBootstrap.cs` | **EDIT.** One guarded `PseudolocTextProvider.InstallIfEnabled(_provider)` immediately after the real provider is installed — the one place the inner provider is in hand. |
| `Assets/_Modules/DevTools/DevScenarioIntent.cs` | **EDIT.** One `InstallIfEnabled()` re-call after the `ff.*` loop, for the first-launch ordering trap (FLAG 4). No change to `ApplyFlag`, no new extra key. |
| `Assets/_Modules/Core/FeatureFlags.cs` | **EDIT, COMMENT ONLY.** A breadcrumb saying `ff.pseudoloc` is deliberately declared elsewhere and why. Zero code. |
| `Assets/Editor/Regression/PseudolocLeakOracle.cs` | **NEW.** Part B's rule: the label walker, the Latin-run finder, the rich-text stripper, and the allowlist parser. In `DeNelle.EditorRegression` so a regression can drive it (see below). |
| `Assets/Editor/Regression/PseudolocHarnessRegression.cs` | **NEW.** Eight checks: the ship quarantine as source-lints **plus the oracle proven RED on a planted leak**. Markers `PSEUDOLOC_HARNESS_OK` / `_FAIL`. |
| `Assets/Editor/Regression/DataRegression.cs` | **EDIT.** Registers the `pseudoloc-harness suite` beside its `device-scenario-kit` neighbour. |
| `Assets/Editor/UICaptureLaunch.cs` | **EDIT.** `AuditPseudolocLeaks` wired into `RenderCanvasToPng` beside `AuditGeometry`; tallies, `ReportPseudolocOracle`, the JSON report, and the `RunPseudolocCaptureHeadless` entry point. |
| `docs/localization/PSEUDOLOC_ALLOWLIST.md` | **NEW.** The machine-parsed growing dictionary. |

**`.meta` files were deliberately NOT hand-authored** for the three new `.cs` files. Unity generates
them on the gate run; the lead stages them then, same as `git status` currently shows for WO-1775's
files (`DeviceScenarioKitRegression.cs.meta` et al. are untracked right now).

---

## HOW ANOTHER AGENT USES THIS TONIGHT — the how-to for the rest of the sweep

### 1. Run the full pseudolocalized catalog + the leak verdict (one command)

```
powershell -File .\run-unity-method.ps1 `
  -Method DeNelle.Editor.UICaptureLaunch.RunPseudolocCaptureHeadless `
  -LogName pseudoloc-capture.log
```

It forces pseudoloc on for the process, drives `RunCaptureHeadless()` **and**
`RunRegisteredSecondaryCaptureHeadless()` (WO-1860's full 267-PNG / 125-stem catalog — they are
called verbatim, so pseudoloc coverage cannot drift from the normal sweep's coverage), publishes the
verdict, and turns pseudoloc off in a `finally`.

### 2. Read the verdict — and read it from the right place

- **`Builds/ui-capture/pseudoloc-leaks.json`** is the machine-readable report: every finding with
  `screen`, `path`, `component`, `offending`, `fullText`, plus `panelsScanned`, `labelsScanned`,
  `labelsTransformed`, `allowlisted`, `findingCount`. **Read this, not the Unity log** — Unity logs
  are UTF-16 and the per-finding log lines are capped at 60.
- **The one marker that is the verdict** is `UI_PSEUDOLOC_OK` / `UI_PSEUDOLOC_FAIL` /
  `UI_PSEUDOLOC_INACTIVE`, judged on a **fresh** log, never by exit code.
  - `UI_PSEUDOLOC_INACTIVE` = pseudoloc was never installed. **Not a clean result** — no rule looked.
  - `UI_PSEUDOLOC_FAIL transform=did-not-take` = the mode was on and **nothing came out Cyrillic**.
    The findings in that run are **not** leak evidence; fix the install and re-run before reading one.
  - `UI_PSEUDOLOC_FAIL allowlist=faulted` = the allowlist did not parse. A faulted allowlist
    suppresses nothing by design, so the findings are unfiltered and no verdict may be read.
- ⛔ **Do NOT quote a pseudoloc run's `UI_GLYPH_*` / `UI_CAPTURE_*` / `UI_TOUCH_*` markers.** See
  FLAG 1 — they legitimately move under Cyrillic. Layout verdicts come from a NORMAL capture run.

### 3. Fix one finding (the Part D loop, per finding)

1. Open the component named by `path` on the screen named by `screen`.
2. Trace the string to its authoring site.
   - **A hardcoded literal** → wire it to a locale key in your WO-1857 lane. This is the normal case.
   - **A live `LocalText.Get(key, englishFallback)` / `FormatWithFallback` call-site fallback** → also
     a real finding: reaching that fallback *means the key is absent from the table*. The hook
     deliberately does **not** transform it (see FLAG 3). Add the key.
   - **`[[missing:<key>]]`** → same: add the key. Also not transformed, on purpose.
   - **A value resolved ONCE into a `static readonly` / cached field** — e.g.
     `static readonly string X = LocalText.Get("k");` — resolved before `ForceOn()` ran, so it surfaces
     as a Latin finding. ⛔ **This is NOT a false positive to allowlist. It is a real runtime defect:**
     a field like that also cannot follow the player changing language in Settings, because
     `LocalText.Changed` can never reach it. The fix is **make the resolve lazy** (a property, or
     re-resolve on `LocalText.Changed`) — not a locale key, and not an allowlist entry.
   - **Provably language-independent** (brand, player name, generated code, data identifier) → add
     the **narrowest** allowlist entry, `path:` over `screen:`, **with a reason you can defend**.
3. Re-run and confirm the finding is gone.

So a finding has **four** dispositions, and three of them are code fixes: add the key, make the resolve
lazy, wire the literal to `LocalText` — or (only when provable) allowlist it.

### 4. Just re-check one screen — **this requires a code edit, it is not a call recipe**

⛔ **There is no per-screen pseudoloc entry point, and you cannot assemble one from outside.**
`ResetPseudolocOracle`, `ReportPseudolocOracle`, `AuditPseudolocLeaks` and every `Capture*` helper are
**private** to `UICaptureLaunch`. So scoping a re-check means *editing `UICaptureLaunch.cs`* to add a
second public entry point that mirrors `RunPseudolocCaptureHeadless`'s body (reset → `ForceOn()` → the
one `Capture*` you want → `ReportPseudolocOracle()` → `ForceOff()` in a `finally`), which puts you in
that file's serialization lane.

**The default is step 1 — run the full sweep.** It is one command, needs no edit, and cannot drift from
the catalog. Only add a scoped entry point if the full sweep's cost is actually the blocker, and
coordinate it as a `UICaptureLaunch.cs` edit. Whatever you add, **`ForceOff()` goes in a `finally`** —
a leaked transform pseudolocalizes every panel built later in that editor session, the owner's own play
mode included.

### 5. On a device (the QA_SCENARIO_BUILD path)

`adb shell am start ... --ei dotr.ff.pseudoloc 1` on a `-Scenario` APK. `DevScenarioIntent.ApplyFlag`
already writes `ff.pseudoloc` like any other `ff.*` key — no parser change was needed. It takes effect
on that same launch (FLAG 4). Turn it off with `--ei dotr.ff.pseudoloc 0`; the pref is sticky.

### 6. The allowlist's grammar, in one line

`<token|phrase|path|screen>: <value>   # <reason>` inside a fenced ```` ```allowlist ```` block in
`docs/localization/PSEUDOLOC_ALLOWLIST.md`. **The reason is mandatory** — an entry without one fails
the parse and reds the suite. A `token` value may not contain whitespace (use `phrase`).

---

## Proof — what was measured, and how

**Brace + NUL gate (CLAUDE.md §1), on every file touched.** `python tools/gate_brace.py` over all
nine `.cs` files → `GATE_BRACE_SUMMARY bad=0 of 9`, exit 0. NUL scan over all nine plus the `.md` →
`NUL_SCAN bad=0 of 10`, no BOMs.

> ⚠ The **raw** brace counts of two files are deliberately unequal and that is correct:
> `PseudolocTextProvider.cs` reads 37/38 and `LocalText.cs` 63/61. All four skewing lines in the
> provider are `'{'` / `'}'` char literals and `"{{"` / `"}}"` string literals in the placeholder
> branch (+3, +2, −4, −2 = −1, exactly the skew). `LocalText.cs` was **already** 61/59 at `HEAD`
> (`git show HEAD:… | count`), and this change added 2 open + 2 close — structurally neutral. The
> gate's own rule, which skips comments and string/char literals, reads both as balanced. This is
> precisely the divergence CLAUDE.md §1 warns about; `tools/gate_brace.py` is the authority and it is
> green. No interpolated strings were used anywhere in this change, so the gate's known
> no-interpolation blind spot cannot bite here.

**The transform's glyph set was MEASURED against the live `ru` table, not chosen by taste.**
`Assets/Resources/Data/Canonical/ru.json` holds **62** distinct Cyrillic code points: `U+0410..U+042F`
upper **except `U+0424` (Ф) and `U+042A` (Ъ)**, `U+0430..U+044F` lower **except `U+044A` (ъ)**, plus
`U+0451` (ё). All 26 targets are distinct, and **all 52 (both cases) appear in that file** —
verified by script. Ф/ф, Ъ/ъ and ё are unused on purpose: their missing case is the unproven glyph.
`PseudolocHarnessRegression` check 4 **re-measures this every gate run**, so the claim cannot rot into
a comment.

**Homoglyphs were avoided by design, which is the difference between a working detector and a
useless one.** All five English vowels and the 12 most frequent consonants map to Cyrillic letters
with **no Latin look-alike** (`д э й ю ч и з ь я г л ц щ ж ы б ш`); only `y p b v k j x q z` fall back
to look-alikes. Had the map used the visually identical set (`а е о р с х у А В Е К М Н О Р С Т Х`),
`coop` → `соор` and the Part C visual pass could not have told a transformed screen from an
untransformed one. Worked example: `Build` → `Вчйлг`.

**The oracle's asserted counts were PROVEN, not assumed.** I ported the transform, the rich-text
stripper, the Latin-run finder and the allowlist parser to Python and ran them over the **real**
allowlist file and the **exact** `[oracle-red]` fixture:

```
ALLOWLIST entries=5 problems=0
ORACLE-RED  labelsScanned=5 (assert 5)  cyrillic=2 (assert 2)  suppressed=1 (assert 1)  findings=3 (assert 3)
            findings: [('LeakLabel','Reforge'), ('LeakLabel','the'), ('LeakLabel','Heart')]
            all on LeakLabel: True
```

So the planted leak is caught with correct attribution, the allowlisted brand beside it is suppressed,
and rich-text markup (`<color=#FF0000>`) does **not** produce `color`/`FF` findings — the four things
acceptance criteria 3 and 4 ask for.

**⚠ THAT PORT CAUGHT TWO REAL DEFECTS IN MY OWN ASSERTIONS, AND BOTH ARE FIXED.** Recording them
because they are the reason the port was worth writing:

1. The "digits are preserved" fixture was `"12,345 / 60 (+5%) — 3.5s"`. The trailing **`s` is a Latin
   letter**, so the transform correctly mapped it to `з` and the assertion would have **failed on
   working code**. A digits fixture containing a letter is testing something else. Fixed to `…3.5`.
2. I had asserted `Transform("{{literal}}") == "{{literal}}"`. Measured behaviour is
   `"{{лйшэядл}}"` — and **the code is right, the assertion was wrong**. `{{`/`}}` are .NET escaped
   literal braces, so `{{literal}}` *renders* as `{literal}`: the word between them is real copy that
   must be pseudolocalized, while the escapes must survive or `string.Format` throws. The assertion now
   pins both halves (escapes preserved, inner text transformed) instead of pinning a bug as a contract.

**Every source-lint the new suite performs was independently confirmed green from outside Unity**
(script re-implementing each check against the tree): `Pseudo(` sites in `LocalText` = 5; zero
`englishFallback` lines routed through `Pseudo(`; `PseudolocHook` code mentions 2 / guarded 2;
`pseudoloc` in `FeatureFlags` **code** = 0; `AuditPseudolocLeaks(canvasGo` present in
`UICaptureLaunch`; all three markers present; the gate-fold pattern absent; `AndroidBuild.cs` and all
four ship-chain scripts name none of the four pseudoloc tokens; the provider's first real line is
`#if UNITY_EDITOR || QA_SCENARIO_BUILD` and its last is `#endif`; `DevScenarioIntent`'s own
first/last gate lines are untouched (so `DeviceScenarioKitRegression` check 1 still holds); and the
suite is registered in `DataRegression.RunAll`. → `SELF_VERIFY OK (0 problems)`.

---

## ✅ COMPILATION IS PROVEN — on a log this lane did not run, read after the fact

I did not fire Unity (instructed not to, and did not). But a compile gate another lane had already
launched **imported these files while I was finishing**, and its captured log is decisive. The chain,
each link measured:

| Link | Measurement |
|---|---|
| The run existed and has finished | Unity pid 14564, `StartTime 21:54:32`; re-checked minutes later, process gone (`Unity alive : False`) |
| It **imported my three new files** | `PseudolocLeakOracle.cs.meta`, `PseudolocHarnessRegression.cs.meta`, `PseudolocTextProvider.cs.meta` — `CreationTime 21:54:46`, i.e. 14 s after that process started. Unity wrote them; I did not author any `.meta`. |
| It saw their **final** bytes | latest mtime across all nine touched files = `21:49:42` (`PseudolocHarnessRegression.cs`), every one strictly **older** than the import and the log |
| Its verdict, on a fresh log | `Builds/compile-gate-overnight1.log`, last write `21:56:34` → **`COMPILE_GATE_OK :: scripts compiled clean`** |
| Nothing of mine is red | `error CS` lines referencing `Assets/` = **0**. All 21 `error CS` lines are `Packages/com.solana.unity_sdk/.../WebGLInput.cs` — the known WO-1575 `COMPILE_GATE_WEBGL_ADVISORY` / `_SKIPPED reason=package-reference-gap` gap, which by design does not withhold `COMPILE_GATE_OK`. |
| Nothing of mine even warns | the 16 `CS####` lines naming a file I touched are all pre-existing `CS0618` obsolete-API warnings at `UICaptureLaunch.cs:5800` / `:5836` — far from this change's insertion points, and the three new files produce **zero** diagnostics |

So the `#if`-guarded provider, the `LocalText` hook's **guarded** form, the oracle, the suite and the
`DataRegression` registration all **compile clean in the editor configuration**, and that is measured,
not argued.

### ⛔ …and here is exactly what that run did NOT compile, because `UNITY_EDITOR` was defined

An editor compile takes the `#if` branch of every guard, so **two one-line additions were compiled as
nothing at all** and remain unproven:

1. **`LocalText.cs`'s `#else` identity `Pseudo`** — the player-only branch. That log's own
   `COMPILE_GATE_WEBGL_SKIPPED reason=package-reference-gap` line says in as many words that no player
   script stage produced an assembly this run.
2. **`DevScenarioIntent.cs`'s new `InstallIfEnabled()` line** — that file's whole body is
   `#if QA_SCENARIO_BUILD`, which is undefined in the editor, so its body compiled as empty. This is the
   same limit `DeviceScenarioKitRegression`'s own header states about the file it guards ("pins the
   GATE, not the runtime behaviour … that needs a device").

Both are trivially likely to be fine — a `=> value;` identity method and one fully-qualified static
call — but **"likely" is not measured**, and this repo's rule is to say so. The run that would prove
them: `overnight-apk-build.ps1 -Scenario` (compiles the `QA_SCENARIO_BUILD` + player configuration).
Nothing here blocks on it.

## What was NOT executed, stated plainly

- **`DataRegression` did NOT run, so there is no `REGRESSION_OK` and no `PSEUDOLOC_HARNESS_OK`.** That
  same log contains neither marker (grepped); the newest data-regression log, `data-regression-1860.log`,
  is `21:03` — **before** any of this code existed, so it cannot be cited for it. **The suite's eight
  checks have never executed. Acceptance criterion 5's regression half is the lead's to satisfy.**
- ⚠ **A Unity instance was live at 21:54–21:56.** It has exited, but per
  `run-unity-method-silent-when-another-unity-runs`, check `Get-Process Unity` before the gate run or it
  returns silently with no log.
- **Acceptance criterion 2 — "produces visibly transformed screenshots" — is UNPROVEN.** It needs the
  capture run and eyes on the PNGs. The code path is argued from source (see FLAG 2 for the exact
  reason a decorator alone would have produced an all-English catalog), not measured.
- **Acceptance criteria 3 and 4 are proven by construction, not by execution.** The `[oracle-red]`
  check performs the plant-and-revert automatically on every gate run — which is strictly better than
  the ticket's manual "temporarily hardcode one, then revert" — but that C# has not itself run. My
  Python port of the identical logic produces the asserted counts exactly.
- **TMP font-atlas coverage of the 52 targets is NOT proven, only strongly evidenced.** I proved every
  target character *appears in the live `ru` table*, i.e. the `ru` locale requires it. I did **not**
  read a TMP font asset's glyph table. Cheapest close: the capture run's own `UI_GLYPH_*` markers plus
  a look at any two PNGs — a tofu'd target would show as a box, and the glyph oracle would also see
  zero drawn glyphs on labels it can measure.

---

## FLAGS the lead must read

### FLAG 1 — a pseudoloc run's LAYOUT markers will legitimately go red, and must not be cited
Cyrillic sets different metrics than English, so `UI_GLYPH_FAIL` (and therefore `UI_CAPTURE_FAIL`) is
the **expected** outcome of a pseudoloc sweep: a band that fits `MANAGE` can truncate `ЩДИДБЭ`. The
sub-calls emit their own markers because they were called verbatim rather than refactored, and that is
deliberate (a shared-core extraction would have been a large diff through the middle of an 11k-line
file that other lanes are editing tonight, for identical coverage). **Judge a pseudoloc run by
`UI_PSEUDOLOC_*` only; take layout verdicts from a normal capture run.** This is stated at the entry
point, in the report, and here, because it is the one way a lane could misread the night's output.
No change is needed unless you want the sub-markers suppressed on this path — that would be a real
change to shared reporters and I did **not** make it unasked.

### FLAG 2 — the ticket named the decorator; the decorator ALONE could not have worked, so there are two halves
The spec's seam (`InstallProvider(new PseudolocTextProvider(realProvider))`) is built and wired. But
in **edit mode there is no provider**: `LocalizationBootstrap.Install` is
`[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]` and never runs, and the only other installer,
`LocaleSmokeCapture`, sets it back to `null` in its own `finally` (`LocaleSmokeCapture.cs:111`). So
during WO-1860's capture sweep — the exact thing this detector runs against — every string comes from
the `Data/Canonical/en.json` fallback inside `LocalText.TryGet`, and a decorator over `null` would
have transformed nothing and produced an all-English "pseudolocalized" catalog. The added
`LocalText.PseudolocHook` is the one seam that covers the provider path and the JSON fallback path
together. **Double application is a proven no-op** (the transform's output holds no `[A-Za-z]`, and
check 4 asserts idempotence), which is why the two coexist with no ordering rule. This is an addition
to the spec's mechanism, not a substitution for it — flagged because it touches a shipping file.

### FLAG 3 — a call-site `englishFallback` is deliberately NOT transformed
`LocalText.Get(key, englishFallback)` and `FormatWithFallback` return the call-site English without the
hook, and `[[missing:<key>]]` is likewise untouched. Reaching either **means the key is absent from
the table** — which is exactly the leak WO-1857 exists to find. Transforming it would paint the leak
Cyrillic and delete it from the report. Consequence the fix lanes must know: **findings will include
missing-key cases as well as pure hardcoded literals**, and both are real work. Pinned by check 2,
which fails if any `englishFallback` line is ever routed through `Pseudo(`.

### FLAG 4 — a real ordering trap on device, mostly closed with one line; read the residual
`LocalizationBootstrap.Install` is `AfterAssembliesLoaded`; `DevScenarioIntent` (which *writes*
`ff.pseudoloc`) is `AfterSceneLoad`. On the **first** launch passing `dotr.ff.pseudoloc=1` the pref did
not exist when the bootstrap asked, so pseudoloc would only have appeared on the **next** launch and a
tester would have reported "the flag does nothing". `DevScenarioIntent` now re-calls the idempotent
`InstallIfEnabled()` after its `ff` loop. `DevScenarioIntent.cs` is `QA_SCENARIO_BUILD`-only and its
own first/last gate lines are unchanged, so `DeviceScenarioKitRegression` checks 1–2 still hold
(verified).

⚠ **The residual, stated precisely rather than rounded off to "it works".** That re-call reaches
`InstallCore(null)`: `DevScenarioIntent` cannot see `LocalizationBootstrap`'s private `_provider`
(different assembly), so on that first launch the **hook is installed but the decorator is not
wrapped, and `LocalText.Changed` never fires** — only `InstallProvider` raises it. Consequence:
**UI already built at that moment (the Title screen) stays English until it next rebuilds; every panel
built afterwards is transformed.** That is enough for the harness's purpose (the screens being swept
are opened later) and it is why the how-to says the pref is sticky — a second launch goes through
`LocalizationBootstrap` with the pref present and wraps the decorator properly.
**If the lead wants the first launch to repaint too:** add `internal static void RaiseChanged() =>
Changed?.Invoke();` inside `LocalText`'s existing `#if` and call it at the end of `InstallCore`. Three
lines. I did **not** make that change unasked because it re-opens a file whose current bytes are
covered by the compile proof above, and the behaviour is adequate without it.

### FLAG 5 — the editor path is NOT driven by PlayerPrefs, on purpose
`ForceOn()`/`ForceOff()` scope pseudoloc to one process. A stale `ff.pseudoloc=1` in the **editor**
registry would pseudolocalize the owner's own play-mode UI and every later capture for the rest of the
night with nothing saying why. `ff.pseudoloc` is the **device** path only.

### FLAG 6 — the oracle lives in `DeNelle.EditorRegression`, not in `UICaptureLaunch.cs`
`UICaptureLaunch.cs:6490-6502` records in its own words why three layout rules that lived in that file
had "never been seen go red": nothing could reach them from a regression suite. The scan rule is
therefore in the regression assembly with two callers sharing one implementation — the capture harness
and the planted-leak check. This is the same remedy WO-1060 applied, not a new pattern.

### FLAG 7 — the allowlist ships with 5 entries and two deliberately EMPTY sections
Only the ticket-named brand nouns (`Solana`, `SKR`, `Cherry`, `Elarion`, `Remnant`) are seeded. The
per-widget (`path:`) and capture-fixture (`screen:`) sections are empty **by design**: those values are
only knowable from a real run's report, and inventing a hierarchy path would prove nothing and risk
matching a label that also carries real copy. **The first lane to run the sweep will have triage work**,
and the allowlist header states the shape and the triage rule (`path:` over `screen:`, always). The
number of findings on the first run is **unknown to me** — I have not run it.

### FLAG 8 — the allowlist GROWS; it is not one of this repo's shrink-only baselines
`TouchBaseline` and `GlyphBaseline` in `UICaptureLaunch.cs` are shrink-only because they park known
*defects*. This file is a dictionary of *facts about the game's vocabulary* and is expected to grow.
The header says so explicitly so nobody copies the shrink-only rule onto it — and equally so nobody
uses "it's allowed to grow" to park a leak. The mandatory-reason parse rule is the mechanical half of
that; honesty is the other half and no parser can enforce it.

### FLAG 9 — what this oracle structurally CANNOT see (Part C's actual job)
It reads live `TMP_Text` / `Text` components, so it is blind to: text **baked into a sprite or art
asset**; any panel **not in the capture set** (WO-1860 deferred `ArmyMusterPanel` and
`RedeemCodePanel` as not fitting the reflected-secondary recipe); and anything gated behind state the
fixtures do not reach. That is the precise remit of the cost-bounded Opus pass — and the
`UI_PSEUDOLOC_OK` line names it as the point at which that spend is worth making, so the two passes
cannot be conflated.

### FLAG 10 — the oracle does NOT gate `UI_CAPTURE_OK`, and check 7 stops anyone wiring it in
Both the touch oracle's §5 and the WO-1648 banner record the same law: a widened assert wired straight
into a live gate turns every commit red and gets suppressed instead of fixed. It is worse here — the
scan is **inert unless pseudoloc is active**, so folding it in would make the normal nightly capture's
verdict depend on a mode it never runs in. Its own marker, its own verdict.

---

## Non-scope respected

Zero locale JSON files and zero Unity String Table assets were touched. `RequiredLocales` /
`EnabledLocales` are unchanged, so `LocalizationAuthorityRegression`, `LocaleParityRegression` and
`GooglePlayLocalizationVariantPolicyRegression` see nothing new. No new shipped locale. No vision
model was run. Parts C and D are process, documented in the how-to above rather than coded.

## Rollback

Delete the three new `.cs` files, the allowlist `.md`, and revert the five edits. Everything is
additive; no save-schema, no locale data, no scene. The ship-quarantine checks prove the harness never
reached a real build even if it is left in place mid-development.
