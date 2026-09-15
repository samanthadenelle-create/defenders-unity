# WORK ORDER 1759 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** edit-only (no Unity, no gate, no build, no git). 2026-09-15.
**Claim, not a fact.** Nothing here was compiled. Every number below was measured THIS session from
the artifact named, or is labelled unproven.

---

## ⛔ READ THIS FIRST — THE WO'S PREMISE IS WRONG, AND THAT CHANGED WHAT SHIPPED

The WO (and the brief) assume two things that the measurement disproves:

1. that `costSkr`, `stakedSkr`, `SkrShowcasePanel`, `NativeSkrPolishBonus`, `SkrPreview` etc. are
   **live hits** the gate is reporting (Class A), and
2. that `VoidTaskResult`, `colorMaskRtHandle`, `TaskReceive`, `UpdateMaskRegions`, `skroa` are
   **live false positives** the gate is reporting (Class B).

**Neither is true. The gate reports NONE of them.** All 43 raw `skr` occurrences in that artifact were
run through the gate's own matcher: **exactly THREE fire**, and not one of them is on either list. The
WO's table is an enumeration of *raw* occurrences from a plain scan — not the matcher's verdict.

So **step 2 as written was not implemented, deliberately**, and this is the §11B-B deviation stated in
advance rather than explained afterwards:

- An identifier-boundary rule would not remove a false positive — there are none to remove.
- A camelCase-aware rule that makes `stakedSkr` / `costSkr` / `SkrShowcasePanel` fire (as the brief
  asks me to prove) is a **STRICTNESS INCREASE**. It would manufacture roughly **fifteen new Play
  offenders at once** across `DeNelle.Core` and `DeNelle.Village` — `FeatureFlags.SkrPreview`,
  `CurrencySkin.SkrDefault`, `CurrencySkinResolver.IsSkr`, `WagerCurrency.Skr`, three
  `StakeRewardsResolver` members, two `VerifiedStakeSnapshot` members, `NativeSkrPolishBonus(+Bootstrap)`,
  `ArenaPanel._headerSkr`, `ArenaVM.skrDelta`, and `costSkr`/`_costSkr`, **which is a CRYSTALS cost and
  not a crypto surface at all** (`BuildModeController.cs:1603`). The next AAB would be **dirtier**, not
  cleaner.
- That is a gate-policy **ruling for the owner**, not a lane call. **It is now pinned instead of
  guessed**: `PlayGateChunkSeamRegression` CASE H goes RED the moment any of those identifiers starts
  firing, so the tightening has to be made out loud rather than arriving as an AAB rejection.

Step 1 was done — against the **measured** sources, which are different from the ones the WO names.
Step 3 was done, pinning what was measured on both sides.

---

## The measurement (the whole ticket turns on it)

`Builds/Android/rejected/EchoesOfElarion-GooglePlay-20260915-165534.REJECTED.aab` →
`base/assets/bin/Data/Managed/Metadata/global-metadata.dat`, **19,874,336 bytes** (matches the WO's own
figure exactly), extracted with `zipfile` to scratch.

`GooglePlayPackagingGate.MatchesTokenInWindow` + `HasPrintableRun` + `IsAllowlistedOccurrence` +
`IsExactIdentifierAllowlisted` were ported to Python **parsing the four token arrays out of
`GooglePlayPackagingGate.cs` at run time** (WO-1754's lane pattern, so the port cannot drift from the
policy). Parse check: `ForbiddenTokens 31 · ShortTokens 4 · FalsePositiveAllowlist 27 ·
ExactIdentifierAllowlist 3 · MinPrintableRunForShortTokens 12` — identical to the counts WO-1754's
RESULT recorded.

**Whole vocabulary, both views (Latin-1 and UTF-16), whole 19.8 MB file:**

| token | raw occurrences | **fires** |
|---|---|---|
| `skr` | 43 | **3** |
| every one of the other 30 forbidden tokens | — | **0** |

**The `GOOGLE_PLAY` define was LIVE in that artifact — measured, not assumed.** `CurrencySkin.cs:130-145`'s
`#else` arm carries the literals `"$SKR"` and `"Spend $SKR"`, and the token `$skr` has **0 raw
occurrences** in the 19.8 MB file. That arm did not compile, so the define was on — which is the
precondition both fixes below rest upon.

**Port fidelity: closed, not asterisked.** Python's `str.isalnum()` and C#'s `char.IsLetterOrDigit`
disagree on six Latin-1 bytes (`² ³ ¹ ¼ ½ ¾`, category `No`), which could in principle hide a hit. The
whole scan was re-run with an exact `IsLetterOrDigit` (`category L* or Nd`): **identical result, the
same 3 offsets.**

That reproduces the chain's single `PLAY_ARTIFACT_DIRTY` line exactly (`…global-metadata.dat token:skr`,
and nothing else), which is the corroboration that the port matches the gate.

### The three live hits, and their sources

| offset | measured bytes | source |
|---|---|---|
| **238,770** | `…one SpawnWave call). SKR, age= STATE-CHANGE SWING…` | `Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs:202-203` — the line ended `+ " SKR" +` and Roslyn **folds the two adjacent constants** `" SKR" + ", age="` into ONE literal `" SKR, age="`. |
| **1,587,839** | `…dotr-arena-pursedotr-arena-skr-balancedotr-arena-streak…` | `Assets/_Modules/Village/Arena/ArenaWalletService.cs:50` — the WO-1366 §4 ruled PlayerPrefs key. |
| **10,556,064** | `ArenaWallet,dotr-arena-skr-balance\xF4` | the same const, serialized copy. |

### ⛔ THE FINDING WO-1754 COULD NOT HAVE SEEN

`ExactIdentifierAllowlist` **cannot suppress a hit in the IL2CPP string-literal table.** WO-1754's
RESULT reasoned that *"a NUL-packed name-table entry passes, because NUL is not an identifier
character"* — true for the **name** table, and **false for the LITERAL table, which is not
NUL-delimited**: literals are packed end to end, so **BOTH of the key's neighbours are the adjacent
literals** — the `e` of `…-purse` on the left and the `d` of `dotr-arena-streak` on the right — and the
both-side rule correctly refuses. ⚠ It is **not** a right-edge-only defect; a left-anchored allowlist
would fix nothing. (Hit 3 is the same failure by a third route: its trailing byte `0xF4` decodes in the
gate's Latin-1 view to `ô`, **a letter**.)

**The allowlist must keep refusing** — that same both-side rule is what makes `dotr-arena-skr-balance-v2`
fire, which WO-1754 pinned on purpose. So the leak is removed at the SOURCE, never by widening the gate.
This also retires the note in WO-1754's RESULT that left hit 3 "UNPROVEN": it fires, and now it is
pinned.

---

## Files changed (4)

| File | Line(s) | What |
|---|---|---|
| `Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs` | 198-215 | `" SKR"` → `" " + StakeStanding.DefaultCurrencySymbol` |
| `Assets/_Modules/Village/Arena/ArenaWalletService.cs` | 48-73 | `PrefBalanceKey` now `#if GOOGLE_PLAY` / `#else` |
| `Assets/Editor/Regression/PlayMetadataIdentifierRegression.cs` | 89-165, 183-190, 224, 262-267, 331-420, 428-490 | 2 two-sided Pins + new CASE 4 (three assertions per file) + its two helpers |
| `Assets/Editor/Regression/PlayGateChunkSeamRegression.cs` | 168-170, 186-188, 226-330 | new CASE H (18 verdicts) + `caseTag` param |

### Which assembly each SKR type lives in, and how it is excluded

| Surface | Assembly | Ships to Play? | Disposition |
|---|---|---|---|
| `SkrShowcasePanel` | `DeNelle.Core` | yes | **Already** `#if !GOOGLE_PLAY` at the TYPE level (WO-1363), and the neutral `#else` stub's own name never fires (`Skr` followed by `S`). **Untouched.** |
| `NativeSkrPolishBonus(+Bootstrap)`, `SkrPreview`, `SkrDefault`, `IsSkr`, `stakedSkr`, `activeStakeSkr`, `DemoMockStakeSkr`, `RewardBearingStakeSkr`, `SkrBaseUnits` | `DeNelle.Core` | yes | Identifiers only. **Do not fire.** Constraining `DeNelle.Core` is impossible (Play needs it) and moving them is churn with no artifact effect — so **untouched, and pinned as suppressed** so a silent tightening is caught. |
| `costSkr` / `_costSkr` | `DeNelle.Village` | yes | A crystals cost, not a crypto surface. Untouched (`TowerPlacementRotateMenu.cs:634` already records WO-1755's reasoning). |
| `_headerSkr`, `skrDelta` | `DeNelle.Village` | yes | Arena presentation identifiers. Untouched. |
| `NativeSkrStakeQuery`, `SkrValuationOracle`, `WalletSkinBootstrap`, `BattleMonthlyCatalog` | `DeNelle.Wallet` | **no** | `DeNelle.Wallet.asmdef` already carries `!GOOGLE_PLAY` in `defineConstraints`. |
| `JupiterSwap*` | `DeNelle.Web3` | **no** | already `!GOOGLE_PLAY`. |
| the `" SKR"` trace literal | `DeNelle.Core` | yes | **FIXED** — routed through `StakeStanding.DefaultCurrencySymbol`, which is already `#if GOOGLE_PLAY "pts" / #else "SKR"` at `StakeRewardsResolver.cs:98-102`. One owner of the spelling. |
| `dotr-arena-skr-balance` | `DeNelle.Village` | yes | **FIXED** — Play arm spells it `dotr-arena-wager-balance`. |

**⚠ The Arena key change is NOT a migration, and here is why that is safe.** The Play channel resolves
`WagerCurrency.Crystals`, so `EnsureLoaded()` / the stub path is unreachable in that variant — the Play
build has **never written this key**, so there is no stored value for a different spelling to orphan.
WO-1366 §4's ruling is that the key must not be renamed *on the path that reads it*; the dApp / Seeker
arm keeps it **verbatim**. If the lead reads §4 as covering the Play arm too, bounce it — but note that
`ArenaCatalogRegression.cs:187` already asserts *"Play must NEVER touch dotr-arena-skr-balance"*, which
this change strengthens rather than weakens. (That suite reads its own const and runs in-Editor, where
`GOOGLE_PLAY` is undefined, so it is unaffected.)

### Proof the Solana / dApp variant still compiles the surface

A Python port of `RegressionSourceText.StripComments` + `EvaluateGooglePlayArms` was run over the two
edited runtime files in both preprocessor states:

```
-- case 2: PRESENT without GOOGLE_PLAY --
  OK present: DefaultCurrencySymbol="SKR"
  OK present: the ruled Arena key
-- case 1: GONE under GOOGLE_PLAY --
  OK gone: DefaultCurrencySymbol="SKR"        OK gone: the ruled Arena key
-- case 4: no `skr` literal under GOOGLE_PLAY --
  OK clean: VerifiedStakeSnapshot.cs          OK clean: ArenaWalletService.cs
```

⚠ **This is the PREPROCESSOR, not a compile.** It proves the dApp arm still carries the strings; it does
not prove the tree builds. `StakeStanding` is `public`, same namespace `DeNelle.Core.Platform`, same
assembly — read at source, not assumed — but only `COMPILE_GATE_OK` settles it.

### ⛔ ROUND 2 — the lead's gate caught a HOLLOW PASS in case 4, and the lint was right

Reported on `Builds/reg1759`: `REGRESSION_FAIL: 1 failure(s) (537/538 green)` —
`hollow pass: PlayMetadataIdentifierRegression.cs:325 [D-vacuous-against-absent-fixture] guard
'found != null'`. `COMPILE_GATE_OK` on `Builds/cg1759`, 0 errors, so the code compiled; the defect was
the SHAPE of the case.

**The lint's complaint, restated in this case's terms.** Case 4 asserted only an ABSENCE (no `skr`
literal under GOOGLE_PLAY) and its single `failures.Add` sat inside `if (found != null)`. Read
`HollowPassScanner.cs:193-194`: arm D fires when EVERY assertion in a verdict method is nested inside a
positive-existence guard (`!= null` / `File.Exists` / `Directory.Exists`). So if the SUBJECT had simply
vanished — someone deleting the trace line, or the whole `PrefBalanceKey` member — there would be no
literal to find, `found` would be null, and the case would have **reported OK over nothing**. That is
the precise way a reverted exclusion ships unnoticed, which is what the case exists to prevent.

**Fix: three assertions per file per variant, not one guarded absence.** `LiteralFreeUnderPlay` became a
`LiteralPin[]` carrying a `PlayProof` and an `OffPlayProof` pattern, and each entry now asserts:

| | what is asserted | what it catches |
|---|---|---|
| **4a** | under `GOOGLE_PLAY` the **Play-neutral carrier is PRESENT** — `StakeStanding.DefaultCurrencySymbol` in `VerifiedStakeSnapshot.cs`, the literal `"dotr-arena-wager-balance"` in `ArenaWalletService.cs` | the subject vanishing; this is the assertion arm D demanded |
| **4b** | without `GOOGLE_PLAY` the **dApp / Seeker spelling SURVIVES** — the same symbol, and `"dotr-arena-skr-balance"` | a case-4a pass obtained by DELETION; WO-1759 is variant scoping, and WO-1377's never-renamed / never-reordered rule still binds |
| **4c** | under `GOOGLE_PLAY`, **no `skr` sits in a shipped literal body** | the original leak returning |

⛔ **Not satisfied by deleting the guard, weakening the suite, or an exemption entry.** 4a and 4b are new
positive assertions; 4c is unchanged in strength. ⚠ The guards deliberately read
`ReadPreprocessed(...) ?? string.Empty` rather than `arm != null && …`: a `!= null` guard is **itself** a
positive-existence guard, so the obvious spelling would have landed straight back in arm D. A missing
file is already a recorded failure from `ReadPreprocessed`, and an empty arm fails the match on its own.

**Measured on the four scenarios** (same preprocessor + stripper ports, re-run after the edit):

```
GREEN, tree as it stands   VerifiedStakeSnapshot.cs -> []      ArenaWalletService.cs -> []
RED, fix reverted          -> ['4a play-carrier MISSING', '4b dApp-spelling MISSING', '4c literal SURVIVES: " SKR"']
RED, #if removed (arena)   -> ['4a play-carrier MISSING', '4c literal SURVIVES: "dotr-arena-skr-balance"']
RED, SUBJECT DELETED       -> ['4a play-carrier MISSING', '4b dApp-spelling MISSING']   <- the vacuous case, now caught
RED, dApp spelling deleted -> ['4b dApp-spelling MISSING']
```

### ⚠ A second defect found while fixing the first — the literal detector was wrong, and it was measured

The first implementation of 4c took **maximal runs where the two strippers differ** and called each run a
literal body. **That is wrong, and the simulation caught it:** a SPACE inside a literal is blanked to
itself, so the two views agree there and one literal splits into several runs — each losing the `$` that
marks it interpolated. Consequence: `$"Resolved stake={activeStakeSkr} -> tier"`
(`StakeRewardsResolver.cs:231` has exactly this shape) reported a **false leak** on an identifier that
never reaches metadata.

The shipped form asks the question per-character instead: the token has no spaces, so *"is this `skr`
inside a literal"* is exactly *"are its three characters blank in the string-stripped view"*.
Interpolation holes are excluded by walking back to the literal's own opening quote. **7/7 edge cases
pass** — `"stake=" + stakedSkr + "s"`, `$"…{activeStakeSkr}…"` and a bare identifier are silent; a plain
literal, a `$`-literal with `skr` in its TEXT, a verbatim `@"…skr…"` and an escaped-brace
`$"{a}{{skr}}{b}"` all fire.

### Red fixtures — the pins are load-bearing, not hollow

Both new pins were re-run against a **simulated revert** and both go red:

```
RED FIXTURE case4 on reverted VerifiedStakeSnapshot  -> " SKR"
RED FIXTURE case4 on un-#if-ed ArenaWalletService    -> "dotr-arena-skr-balance"
```

### CASE H — all 18 verdicts confirmed against the port before they were written

3 FIRE (the measured live shapes: the folded `" SKR, age="` literal; the packed-literal-table run; the
serialized copy with its `0xF4` neighbour) and 15 SUPPRESS (`costSkr`, `_costSkr`, `stakedSkr`,
`SkrShowcasePanel`, `NativeSkrPolishBonus`, `get_SkrPreview`, `SkrBaseUnits`, `_headerSkr`, `skrDelta`,
`RewardBearingStakeSkr`, bare `Skr`, plus the BCL's `VoidTaskResult`, `ValueTaskReceive`,
`AnyTaskRequiresNotifyDebuggerOfWaitCompletion`, `_userTokenTaskResultProperty`, `colorMaskRtHandle`,
`UpdateMaskRegions`). **0 mismatches.**

---

## What was NOT changed, and why

- **`GooglePlayPackagingGate.cs` — UNTOUCHED.** No token, no allowlist, no matcher rule. The brief's
  step 2 would have been a strictness ruling (above); the false positives it targets do not exist.
- **`tools/android/assert-google-play-aab-clean.ps1` — UNTOUCHED, and therefore still in lockstep.**
  The mirror tracks the gate's four token arrays; none changed, so the two scanners' vocabularies are
  byte-identical by construction. ⚠ Not re-parsed this session — WO-1754's lane proved the parser
  against these same arrays and they are unmodified.
- **No new suite → no `DataRegression.cs` registration line is needed.** Both halves landed in suites
  that are already registered (`play-metadata-identifiers`, `play-gate-chunk-seam`). Suite count is
  unchanged; case count is up by two.

---

## Asset-side residue (the lead's explicit question)

**None reached the artifact — measured, not swept.** The 16:55 chain's `PLAY_ARTIFACT_DIRTY` listed
**one** entry, `global-metadata.dat`, and the whole-vocabulary scan above found no other token in it.
Every `.json`/`.uxml`/`.uss` carrier that *does* hold `skr` on disk — `Assets/_Modules/Web3/Resources/
JupiterSwapPanel.uxml`+`.uss` (the WO-1740/1741 Jupiter residue), `Assets/Resources/Data/Canonical/
{skin,stake-rewards,packs,siege-stakes,canon-strings,battle_monthly,wallets}.json`, the localization
`en/de/fr/...json`, `StreamingAssets/Data/Canonical/skr_staking.json` + `skr_store.json` — therefore
either did not ship or was neutralised by `GooglePlayContentExclusion` before packaging. ⚠ **I did not
open the exclusion path** (out of this silo); the evidence is the artifact's clean entry list, not a
reading of the sweep.

---

## Checks run this session

| Check | Result |
|---|---|
| `python tools/gate_brace.py` (4 files, re-run after round 2) | `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0 |
| raw brace balance (CLAUDE.md §1 one-liner) | 15/15, 74/74, **60/60**, 24/24 |
| ⚠ the two counters disagreed once, and it was fixed not ignored | `PlayMetadataIdentifierRegression.cs` read **61/60 raw** while the gate read **55/55**: three brace CHAR LITERALS (`'{'` ×2, `'}'` ×1) in the interpolation-hole walker. Replaced with the balanced-pair constants `HoleOpen`/`HoleClose` on ONE line, following `HollowPassScanner.cs:143-144`'s own precedent for exactly this. Both counters now agree. |
| embedded/trailing NUL | none in any of the 4 |
| `ArenaWalletService.cs` ASCII-only (its own header rule; it was ASCII-only at HEAD) | restored — verified `bytes.decode('ascii')` clean |
| no `.unity` scene touched | confirmed |
| Preprocessor simulation of all 4 regression cases | ALL PASS + 2 red fixtures fire |
| Case 4 re-simulated after the round-2 reshape | green on the tree; 4a/4b/4c each fire on their own red scenario, including the vacuous "subject deleted" one |
| Literal-detector edge cases (concat, interpolation hole, bare identifier, verbatim, escaped brace) | 7/7 correct |
| Matcher port vs. 18 CASE H fixtures | 0 mismatches |
| Port re-run with C#-exact `IsLetterOrDigit` | same 3 offsets — no hidden fourth hit |

---

## Predicted next AAB — and its one honest caveat

`global-metadata.dat` should come back **clean for every token**, because the only three firing
occurrences in it trace to the two literals now removed from the Play arm, and no other token fires
anywhere in the file. Since that entry was the **only** dirty entry in the 16:55 run,
`PLAY_ARTIFACT_DIRTY` is predicted **empty**.

⚠ **Predicted, not measured.** The metadata is regenerated by the build; bundle names are content-hashed
and every offset moves. Judge by `AAB_OK → AAB_SIGNING_OK → R2_PARITY_OK → AAB_SIZE_OK → AAB_DONE` on a
**fresh** log, never by this paragraph.

## Open for the owner / lead

1. **The strictness ruling.** Should `skr` gain a camelCase identifier-boundary rule that makes
   `stakedSkr` / `SkrShowcasePanel` / `costSkr` fire? Today they do not. Saying yes means compiling ~15
   Core/Village identifiers out of the Play variant **in the same change**. CASE H pins the current
   answer either way.
2. **WO-1366 §4 and the Play-arm key spelling** — flagged above; bounce if the ruling is read wider.
