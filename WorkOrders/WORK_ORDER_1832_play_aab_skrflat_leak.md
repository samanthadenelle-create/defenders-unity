# WO-1832 — The 2026-09-17 Play AAB rejection: `pricing.skrFlat` in packs.json, and one re-worded C# sentence in the metadata

**Status:** IMPLEMENTED
**Silo:** Play packaging / content exclusion — `Assets/Editor/GooglePlayContentExclusion.cs`, `Assets/_Modules/Commerce/PackCatalog.cs`, `Assets/Editor/Regression/{GooglePlayPackagingRegression,PlayMetadataIdentifierRegression}.cs`
**Opened:** 2026-09-17 (edit lane)
**Caused by:** WO-1815 (`baeba6b5b`, 2026-09-16, *"the shelf is SKR-only at flat amounts"*)
**Builds on:** WO-1740 (the RCA doctrine), WO-1741 (the implementation pattern), WO-1754 (the chunk seam), WO-1759 (the literal-per-artifact pattern)

> ### ⛔ THE GATE WAS RIGHT, TWICE. NOTHING HERE WEAKENS IT.
> Both tokens the gate named are genuinely present in the 456,128,789-byte artifact and were read out
> of it this session. No allowlist entry was added, no token removed, no scan relaxed.
> Only the **GOOGLE_PLAY** variant strips; the dApp Store / Solana build keeps every surface
> unchanged, and both changes are define-derived per artifact.

---

## 1. What happened

`google-play-aab-build.ps1` ran 2026-09-17. **The Unity build SUCCEEDED** (`Build Finished, Result:
Success.`, `Builds/aab-build.log:~35017`); the artifact gate then rejected it and quarantined it to
`Builds/Android/rejected/EchoesOfElarion-GooglePlay-20260917-115237.REJECTED.aab`.

`Builds/aab-build.log:35029-35031`:

```
[GooglePlayPackagingGate] PLAY_ARTIFACT_DIRTY:
 - content:base/assets/Data/Canonical/packs.json token:skr
 - content:base/assets/bin/Data/Managed/Metadata/global-metadata.dat token:skr
```

**Two offenders only** — down from six on 2026-09-15. Every other residual WO-1741 §7 predicted
(`web3`, `solana`, `crypto`, the UniTask paths, the provenance blob) is **gone**: the `solana`,
`web3` and `crypto` lines do not appear at all. WO-1754/1759 closed them.

**Both of today's offenders are the SAME failure wearing two hats: a hand-maintained list of names
tracking live authoring.** The identical duplicated-state failure `CLAUDE.md` §2, §5 and §16 each
describe in their own words.

## 2. Method — the gate's own matcher, ported, run over the rejected bytes

`GooglePlayPackagingGate`'s `MatchesTokenInWindow` / `HasPrintableRun` / `IsAllowlistedOccurrence` /
`IsExactIdentifierAllowlisted` (`:695-737`, `:743`, `:769`, `:812`) were ported to Python verbatim and
the four arrays were **parsed out of the gate source** rather than retyped
(`ForbiddenTokens` 31 entries, `ShortTokensRequiringTextContext` 4, `FalsePositiveAllowlist` 27,
`ExactIdentifierAllowlist` 3), then run **whole-file** over both named entries of the rejected AAB in
the Latin-1 and UTF-16 views. Nothing below is inferred from a source grep alone.

⚠ *One process note worth recording: the first port loaded the allowlists with
`src.index('FalsePositiveAllowlist')`, which matched the array's name inside the file's own
**doc comment** and produced a garbage vocabulary that suppressed everything — it reported CLEAN on a
provably dirty artifact. A measurement that agrees with nothing is a bug in the measurement; the port
was fixed to anchor on the `string[] <name> = {` declaration and re-run.*

## 3. CAUSE #1 — `pricing.skrFlat`, 27 times, in the shipped packs.json

**Measured, from `base/assets/Data/Canonical/packs.json` inside the rejected AAB** (40,526 bytes):
**27 live `skr` hits, every single one a `"skrFlat"` key**, at offsets 4206, 6099, 7763, 8784, 9842,
11940, 13195, 14461, 15809, 17430, 18341, 19885, 21153, 22435, 24109, 25771, 27265, 28108, 29779,
31265, 32183, 33912, 35467, 36322, 37189, 38050, 40094. **No other token fires on that entry** — so
the existing transform's removals of `usdc` / `sol` / `skr` and of `currencyDisclaimer` and the
`_schemaNotes` rows all **worked**.

**Why it slipped.** `GooglePlayContentExclusion.ApplyNeutralRewrites` removed three keys **by exact
name**:

```csharp
pricing?.Property("usdc")?.Remove();
pricing?.Property("sol")?.Remove();
pricing?.Property("skr")?.Remove();
```

WO-1815 added a **fourth** rail, `pricing.skrFlat`, to all 27 priced packs of
`Assets/{Resources,StreamingAssets}/Data/Canonical/packs.json` (both mirrors byte-identical, `cmp`).
The gate matches a **substring** (`MatchesTokenInWindow:708`), and `Data/Canonical/*.json` is a
**readable** entry (`IsUserFacingContentEntry:467`), so the trailing-boundary rule that saves
`skrFlat` in a binary payload **does not apply** — `"skrFlat"` fires on the bare `skr` token exactly
as `"skr"` did, while `Property("skr")` removed only the exact name.

**The fix is the RULE, not a fourth name** (`GooglePlayContentExclusion.cs:386-416`, the name-driven
removal at `:413-415`): the pricing
block now removes **any rail whose KEY NAME carries a forbidden token**, read off the gate's own
vocabulary through `ContainsForbiddenAuthoringToken`, so a rail added tomorrow is covered the day it
lands. `sol` stays explicit because its name carries no token (`solana` does; `sol` does not — checked
against the ported matcher). `usd` deliberately survives: it is the Play/Pi reference price and the
only figure a Play pack row has left.

**A Play pack row after the transform is `pricing { usd }`** — measured, not assumed.

## 4. CAUSE #2 — ONE re-worded sentence, and it is new as of last night

**Measured: EXACTLY ONE live `skr` hit in `global-metadata.dat`** (19,991,740 bytes), at
**offset 1,114,669**, reading:

```
…d clip (synth fallback)Price unavailablePriced in Pi when you tap Buy.Priced in SKR - token value moves.Primary Touch…
```

Source: **`Assets/_Modules/Commerce/PackCatalog.cs:416`** —
`_data.CurrencyDisclaimer ?? "Priced in SKR - token value moves."`.

**It is WO-1815's, and the file says so.** The comment immediately above it (`PackCatalog.cs:412-415`)
records: *"Re-worded with packs.json's own `currencyDisclaimer` on 2026-09-16 (WO-1815): the old
'Token price moves with the market.' reads as a disclaimer about the PRICE…"*. The retired wording
carried **no forbidden token**. (No `git` was run in this lane — the dating is the file's own record
plus the offset, not a commit read.)

`Assets/_Modules/Commerce/DeNelle.Commerce.asmdef` has `"defineConstraints": []`, so the assembly —
and every literal in it — compiles into the Play player. IL2CPP writes every string literal into
`global-metadata.dat` whether its branch can run or not.

### 4.1 Why the attribute is NOT the leak, and the field name is not either

The brief's suspected shape — `[JsonProperty("skrFlat")] public double SkrFlat;`
(`PackCatalog.cs:98`) — **does not fire, and cannot.** `skr` is in
`ShortTokensRequiringTextContext` (`GooglePlayPackagingGate.cs:85-88`), and in a **binary** entry a
short token additionally requires a **trailing word boundary** (`:722-723`) and a printable run ≥ 12
(`:728`, `MinPrintableRunForShortTokens = 12` at `:335`). `skrFlat`'s next character is `F` — a
letter — so the hit is skipped. `[JsonProperty("skr")]`'s literal is a 3-character run. **Confirmed
by the scan: neither appears among the live hits.** So no deserialization key had to move, and the
dApp Store's serialization match against packs.json's real `"skrFlat"` JSON key is untouched.

### 4.2 The fix, and why it is safe in both directions

`PackCatalog.cs:410-439` (the `#if` at `:435`, the neutral arm `:436`, the Seeker arm `:438`) — the
fallback sentence is now split per artifact:

```csharp
#if GOOGLE_PLAY
        get { EnsureLoaded(); return _data.CurrencyDisclaimer ?? "Priced by the store at checkout."; }
#else
        get { EnsureLoaded(); return _data.CurrencyDisclaimer ?? "Priced in SKR - token value moves."; }
#endif
```

**Safe on Play, proven not assumed: the property has NO Play-side consumer.** All three call sites are
in `DeNelle.Wallet` — `PackStore.cs:1559`, `StoreLegalFooter.cs:104` and `:126` — and
`Assets/_Modules/Wallet/DeNelle.Wallet.asmdef` carries `"!GOOGLE_PLAY"` (read at source, one of the
five asmdefs in the tree with a `GOOGLE_PLAY` constraint). Nothing in a Play player reads the string,
so the Play arm is unobservable in the shipped game. It is kept **non-empty** because
`Assets/Data/Tests/PackCatalogTest.cs:193` asserts non-empty and an editor compile can carry
`-ExtraScriptingDefines GOOGLE_PLAY` (`FeatureFlags.cs:1651` records exactly that path).

**Safe on Seeker:** the `#else` arm is the authored sentence, byte-for-byte. Nothing was deleted.

### 4.3 The full sweep behind "exactly one"

Every `.cs` under `Assets/` outside `_Modules/Wallet`, `_Modules/Web3`, `Assets/Editor`,
`Assets/Tests` and any `*/Editor/*` folder was scanned for string literals matching
`skr(?![A-Za-z0-9])`, with each hit's `#if` guard stack resolved. **42 hits; after the guards, one
unguarded literal long enough to clear the printable-run floor: `PackCatalog.cs:416`.** The rest are
already scoped: `SkrShowcasePanel.cs` (whole file `#if !GOOGLE_PLAY`, `:46`),
`StakeRewardsPanel.cs:57,67`, `TitleController.cs:222,381`, `CurrencySkinResolver.cs:140,355`,
`StakeRewardsResolver.cs:98`, `ArenaWalletService.cs:69` (the WO-1366 §4 key, Play spelling
`dotr-arena-wager-balance`), `FeatureFlags.cs:1565-1593` (inside `#if UNITY_EDITOR` at `:1502`),
`DevPanelController.cs:861,1568` (`#if DEVELOPMENT_BUILD || UNITY_EDITOR`), and
`TowerPlacementRotateMenu.cs:636`, already reworded to `"{_costSkr:F0} cost"`.

## 5. The guards added, so the next SKR field fails headless instead of at 11:52

**(a) Build-time post-condition — `PLAY_NEUTRAL_RESIDUAL_KEY`**
(`GooglePlayContentExclusion.AssertNoResidualForbiddenKey`, called at the end of each file's rewrite).
WO-1363 closed the **VALUE** axis by asserting a rule instead of maintaining a list; keys had no such
rule. This asserts the **OUTCOME**: after the named transform and the value sweep, **no object key may
carry a forbidden token**, or the Play build fails naming the file and the full key path.

⚠ **MEASURED BEFORE IT WAS ADDED, over all five swept catalogs in both mirrors, with the ported
readable matcher: ZERO residual keys.** The same walk *without* the new pricing rule returns **27**,
all `pricing.skrFlat` — matching the 27 offsets in §3 exactly, which is what makes the oracle
non-vacuous. It cannot fail the next Play build on something nobody foresaw.

**(b) Headless catalog oracle — `GooglePlayPackagingRegression.CheckSweptCatalogKeys`.** The build-time
assert is stronger but only runs inside an AAB build. This case reads the five swept catalogs directly
and permits a token-bearing key only when the transform provably removes it: a **`pricing` rail**
(removed by name) or an **`_`-container entry whose own value also carries a token** (removed whole).
Measured 2026-09-17: **84 token-bearing keys per mirror, all 84 covered** — 81 pricing rails
(`skr`/`skrFlat`/`usdc` × 27) and 3 `_schemaNotes` rows (`skrFlat`, `skrPeg`, `walletGate`, each of
whose *values* carries a token). It also asserts its own catalog list against the exclusion's source
text, so the list cannot drift silently the way the key names it polices did.

**(c) Source pins** in the same file: the name-driven rail rule, the post-condition's declaration AND
its call site, `PackCatalog.cs`'s `#if GOOGLE_PLAY`, and the Play-neutral sentence's **presence** —
the positive half that stops an absence assertion passing by deletion (HollowPassScanner arm D).

**(d) Two-sided `Pin`** in `PlayMetadataIdentifierRegression.Pins`: `"Priced in SKR - token value
moves."` must be **GONE** under `GOOGLE_PLAY` (case 1) and must **still exist** without it (case 2).

⚠ **`PackCatalog.cs` is deliberately NOT in `LiteralFreeUnderPlay`.** Case 4c flags *any* `skr`
inside a shipped literal body, while the gate also demands a trailing boundary and a 12-character run.
PackCatalog keeps `[JsonProperty("skr")]` and `[JsonProperty("skrFlat")]`, which must survive on both
variants (§4.1) and which no artifact scan objects to. Pinning the file into case 4 would have gone
red on working code. The reasoning is written into the source beside the pin.

## 6. Residuals recorded, deliberately not fixed

- **The generalised literal sweep is NOT written.** The right long-term guard is a case that walks
  every Play-shipping `.cs`, evaluates the `GOOGLE_PLAY` arm, extracts **literal bodies** and runs
  `GooglePlayPackagingGate.MatchesTokenInPayload(body, token, readableEntry:false)` — the gate's own
  matcher, so it cannot drift. Two concrete blockers, both measured this session, and neither is
  guesswork:
  1. `PlayMetadataIdentifierRegression.EvaluateGooglePlayArms` (`:558`) resolves **only** bare
     `GOOGLE_PLAY` / `!GOOGLE_PLAY` and keeps both arms of every other `#if`. A fleet sweep would
     therefore false-fire on the six editor-only literals in `FeatureFlags.cs` and
     `DevPanelController.cs` (§4.3). It needs a player-view mode treating `UNITY_EDITOR` and
     `DEVELOPMENT_BUILD` as false.
  2. The existing kept-vs-blanked literal detector is **per-character by design**
     (`FirstSkrBearingLiteral`'s own comment explains why), and a space inside a literal blanks to
     itself — so reconstructing a whole literal body for the printable-run rule is a genuine piece of
     work, not a rename. Seed for that ticket: the 42-hit guard-resolved scan in §4.3.
- **`_schemaNotes.walletGate` survives only because its VALUE happens to carry a token.** If that note
  is ever re-worded neutrally, the key survives the sweep and the new post-condition throws on
  `wallet` — which the **artifact** scan would not have objected to (`AuthoringOnlyTokens`,
  `GooglePlayPackagingGate:182-185`, is deliberately never applied to artifact entries). The
  post-condition is intentionally stricter and fail-closed; the fix in that case is to rename the key,
  not to widen the assert.
- **`Packages/com.solana.unity_sdk/Resources`** (WO-1741 §8) is still unruled and still not
  quarantined. Not touched.

## 7. ⛔ WHAT COULD NOT BE PROVEN FROM HERE

- **That the next AAB is CLEAN.** That needs a full Unity + bundletool rebuild (several minutes) and
  was **not run** — this lane is edit-only and no Unity process was started. The two fixes are proven
  against the *rejected* artifact's bytes and against a faithful port of the gate's matcher; the
  shipped verdict is the marker on a fresh log, and only that.
- **Nothing was gated or committed.** `COMPILE_GATE_OK` / `REGRESSION_OK` are the lead's single gate.
- **What Google's automated review actually does with any of these strings**, as WO-1740 §5 and its
  RCA both record. The gate is a conservative self-imposed proxy for Play policy, not a copy of it.

## 8. Owner ruling requested (one, and it is NOT blocking)

**The Play-side disclaimer copy.** The Play arm now reads **"Priced by the store at checkout."**
This is an *unobservable default*, not a design decision: no Play-side code reads
`PackCatalog.CurrencyDisclaimer` (§4.2), and the Play transform already removes
`packs.json`'s own `currencyDisclaimer` outright, so the Play shelf shows **no currency disclaimer at
all** today — the existing behaviour, which this change reuses rather than invents. **If the Play
store shelf ever renders a disclaimer**, the owner should rule on the wording then; the neutral
sentence is a placeholder deliberately chosen to parallel the shipped
`"Priced in Pi when you tap Buy."` line that sits beside it in the metadata.

## 9. What NOT to touch

- **Do not weaken `GooglePlayPackagingGate`.** Both hits were real. No allowlist entry was added here.
- **Do not rename `pricing.skrFlat` in `packs.json`.** It is the authored server price key, copied
  verbatim into `api/_lib/sku-catalog.generated.json` by `tools/gen-sku-catalog.mjs`, and pinned by
  `StoreSkrFlatLadderRegression`. The Play build **removes** it; every other build keeps it.
- **Do not edit the canonical JSON mirrors for this.** The fix lives in the build-time transform; the
  two mirrors stay byte-identical for every build target.
- **Do not add a `#if` around `[JsonProperty("skrFlat")]` or the `SkrFlat` member** (§4.1) — neither
  fires, and removing either breaks the dApp Store's deserialization.
- Do not re-open WO-1740/1741/1754/1759, and do not touch anything gated by `DeNelle.Web3`.
