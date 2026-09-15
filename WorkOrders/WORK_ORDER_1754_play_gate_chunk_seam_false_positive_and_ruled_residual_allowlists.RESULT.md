# WORK ORDER 1754 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** edit-only (no Unity, no gate, no build, no git). 2026-09-15.
**Claim, not a fact.** Everything below is either (i) proven by something run THIS session and named
here, or (ii) explicitly labelled unproven. Nothing was compiled: this repo's compile and regression
verdicts come from markers on a fresh Unity log, and this lane never held Unity.

---

## Files changed

| File | What |
|---|---|
| `Assets/Editor/Regression/GooglePlayPackagingGate.cs` | seam fix (all three vectors), `ExactIdentifierAllowlist`, one new `FalsePositiveAllowlist` phrase |
| `tools/android/assert-google-play-aab-clean.ps1` | the same two changes, mirrored |
| `Assets/Editor/Regression/PlayGateChunkSeamRegression.cs` | NEW — the straddle suite with a red fixture |

### Registration line — NOT added by this lane, per the brief

Insert in `Assets/Editor/Regression/DataRegression.cs` immediately after the existing
`play-packaging` line (`:639`), so the two Play-artifact oracles sit together:

```
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "play-gate-chunk-seam suite", () => { if (!DeNelle.Editor.Regression.PlayGateChunkSeamRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[play-gate-chunk-seam] " + r); });
```

Adding it raises `REGRESSION_OK <n>/<n> suites` by one. The count is read off the marker, never a doc.

---

## §1 — the chunk seam

**The rule that replaced the seam.** A chunk now SEARCHES the whole text it holds — so boundaries, the
printable run and both allowlists are read at full width — but only REPORTS hits inside a **decision
range**, and the ranges of successive chunks **tile**: no gap (a missed leak) and no overlap (the
duplicate judgement that produced the false positive). All three vectors fall out of that one rule:

- **(a) tail** — a hit without a full margin of RIGHT context is deferred to the next chunk.
- **(b) head** — the retained bytes are no longer re-judged; the previous chunk already decided them
  with full context, and the decision range starts exactly where that chunk's ended.
- **(c) word end** — `textEndIsStreamEnd` / `textStartIsStreamStart` are now passed in, so a chunk edge
  is never mistaken for a word edge. Kept as an explicit guard even though the range makes it
  unreachable, so the matcher's contract is honest for any caller.

**Two defects found while implementing, neither in the brief, both real:**

1. ⛔ **`IsAllowlistedOccurrence` searches `hit ± allow.Length`, and the old overlap never budgeted for
   the allowlist phrase at all** — only for the longest token and the printable run. `ScanMarginChars`
   now includes the longest phrase and the longest ruled identifier.
2. ⛔ **An entry whose length is an EXACT MULTIPLE of the chunk would have lost its deferred tail.** The
   loop would defer the last margin and then read 0 with no chunk left to judge it. `globalgamemanagers
   .assets.split0` is 1,048,576 bytes — sixteen chunks, no remainder — **in the very artifact this WO was
   measured on**. A final unbounded pass over the retained buffer closes it, and CASE D pins it. Without
   that pass the seam fix would have turned a false positive into a **silent miss**, which is worse.

Also fixed: `Stream.Read` is allowed to return short without EOF, and the seam rule reads a short read
as end-of-stream — so a partial read would have re-armed the very defect. Both scanners now read fully
or prove EOF.

### What was measured, and how

The C# matcher and both scan loops (fixed and pre-WO-1754) were ported to Python **reading the token
arrays out of `GooglePlayPackagingGate.cs` at run time** — so the simulation cannot drift from the
policy it tests — and run over the suite's own synthetic streams.
`margin=264 retain=1584 legacyRetain=264 chunk=65536`; **17/17 assertions pass**, including the seven
pre-existing `MatchesTokenInPayload` pins in `GooglePlayPackagingRegression.cs:194-205`, which still
hold. ⚠ **This proves the ALGORITHM, not the C#.** It is not a compile, not a gate, and not a substitute
for either.

⛔ **TWO AXES THE SIMULATION DOES NOT COVER. Do not read 17/17 as covering them.**
1. **The UTF-16 view is never exercised.** Every synthetic stream A–F is Latin-1. The half-density view
   is the BINDING constraint on the tiling arithmetic — it is the reason retention is six margins and
   not one — so that arithmetic is **argued from the maths, not measured**.
2. **A ruled identifier sitting ON a seam** — e.g. `\0SolanaWallet` whose trailing NUL falls in the next
   chunk. By the flags it defers correctly (`rightClear` = `textEndIsStreamEnd` = false, so it is not
   suppressed there and passes to the chunk that holds the whole name), but that path was **not run**.

Both are the natural next cases if this suite is ever extended. They were left out deliberately: the WO
asked for three vectors and each has its own red fixture.

### The red fixture is the load-bearing part

`LegacyScanStream` in the new suite rebuilds the pre-WO-1754 loop, and the suite **FAILS if that fixture
does not report the false positives**. Without it a case could drift off the seam and pass for the wrong
reason — a green suite proving only that the synthetic stream no longer straddles anything.

⚠ **One case had to be re-aimed because the fixture caught it.** CASE B (head) was first built one byte
off the retained-window start; the truncated view still showed the `b` of `lib` before the token, which
the LEADING-boundary rule rejects on its own, so the case passed while **reproducing nothing** — the
fixture went clean and said so. The token is now landed at index 0 exactly, which is what makes the old
scanner take its `hit == 0` short circuit and report the false positive. **Acceptance criterion 1 is
therefore MEASURED for cases A, B-legacy and C** (the fixture fires on each), not argued.

### Acceptance criterion 3 is NOT met, and cannot be from this lane

WO §1 ¶2 — *re-measure every token's live-hit count with the fixed scanner and write the new numbers into
this WO* — requires scanning the 455 MB artifact with the fixed matcher. Not done. The counts in the WO
are still the pre-fix ones and are **hearsay until re-measured**.

---

## §2 — the ruled residuals

### The brief's shape would not have been safe, so the shape changed. This is the deviation to read first.

The brief (and RCA item 7) proposed a `FalsePositiveAllowlist` phrase. **A phrase entry reading
`SolanaWallet` also suppresses `SolanaWalletAdapterWebGL`** — a real crypto surface the gate's own
UniTask comment says must keep firing — and **the `walletadapter` token cannot be relied on as a
backstop there**, because `MatchesTokenInPayload` requires a LEADING word boundary and the character
before `WalletAdapter` in that identifier is the `a` of `Solana`. Read at the matcher, not assumed.

So the two ruled identifiers went into a **new, stricter array**, `ExactIdentifierAllowlist`: a hit is
dropped only when the occurrence lies inside an occurrence of the **whole** string that is itself bounded
by a **non-identifier character on both sides**. `SolanaWalletAdapterWebGL` fails on its trailing `A` and
keeps firing; a NUL-packed name-table entry passes, because NUL is not an identifier character.

⛔ **This is what answers the brief's NUL question.** A NUL-bounded phrase — `"\0solanawallet\0"` — is
**impossible**: the PS1 mirror parses these arrays with `'"([^"]*)"'` and would read `\0` as **two
literal characters**, giving the two scanners different vocabularies. The both-side rule is expressible
in BOTH scanners from plain literals, and a NUL satisfies it, so it reaches the name-table case **without
the escape**. No entry in this change contains a quote, a backslash or an escape.

⚠ **`IsIdentifierChar` is not `IsLetterOrDigit`, and that distinction was MEASURED by the simulation.**
A plain alphanumeric test reads the hyphen in `dotr-arena-skr-balance-v2` as a boundary and **suppresses
that key too**. Hyphen and underscore therefore count as identifier characters; the dot deliberately does
not, so a fully-qualified reference to a ruled member is still the ruled member.

### Entry by entry

| Entry | Array | Ruling cited in the code | Safe? |
|---|---|---|---|
| `SolanaDappStore` | Exact | WO-1377, quoted at `PlayMetadataIdentifierRegression.cs:41-51` | **PROVEN narrow** — `SolanaDappStoreProvider` still fires |
| `SolanaWallet` | Exact | same ruling; name-bound to `skin.json:30` | **PROVEN narrow** — `SolanaWalletAdapterWebGL` still fires |
| `dotr-arena-skr-balance` | Exact | WO-1366 §4, in code at `ArenaWalletService.cs:48-50` | **PROVEN narrow** — `…-v2` still fires |
| `com.solana.unity_sdk@` | Phrase | same class as `dependencies.pb`, skipped at `:388-390` | **PROVEN narrow** — the SDK source PATH still fires |

**PS1 parse: PROVEN.** `Get-GateTokenArray` (lines 35-62) was copied **verbatim** into a scratch script
and run against the edited gate: `ForbiddenTokens 31`, `ShortTokensRequiringTextContext 4`,
`FalsePositiveAllowlist 27` (26 prior + 1), `ExactIdentifierAllowlist 3`, each literal byte-identical to
the C#. ⚠ That is the **parser** re-executed, not the script end-to-end — the script needs an `.aab` and
that is out of this lane. The whole script parses clean (`Parser::ParseFile`, 0 errors).

### ⛔ Deviation from the brief, stated plainly

The brief asked for **a pattern, not a literal** for the `PerformanceTestRunInfo` receipt, implying a
filename pattern in `ShouldSkipProvenanceEntry`. **I did not do that.** The receipt's name is a per-build
content hash, so such a pattern would have to match `bin/Data/<32 hex>` — which is Unity's **generic
content-addressed asset naming**. Skipping that class would blind the gate to real shipped content: a
broad pattern that could suppress a genuine future leak, which the brief itself calls a failure, not a
fix. The `com.solana.unity_sdk@` phrase suppresses one occurrence FORM and nothing else. **The root
disposition remains RCA item 2 — drop `com.unity.test-framework.performance` from
`Packages/manifest.json:33`** (`grep -rn "Unity.PerformanceTesting" Assets` returns nothing). That is a
manifest change outside this silo.

### ⚠ A side effect the lead must verify — it changes the SWEEP, not just the artifact scan

The new suppression lives in the **one matcher**, which `ContainsForbiddenAuthoringToken` also uses — the
single entry point of the Play-neutral catalog sweep in `GooglePlayContentExclusion` (off limits to this
lane, not read). Consequence: the sweep will **stop neutralising** a quote-bounded `SolanaWallet` /
`SolanaDappStore` / `dotr-arena-skr-balance` value in catalog JSON.

`Assets/Resources/Data/Canonical/skin.json:30` is `"authMode": "SolanaWallet"`, and
`CurrencySkinResolver.ParseAuth` reads that value back by name. **So this change plausibly FIXES a latent
break — a sweep rewriting a value the resolver must read verbatim — but I have NOT proven the sweep was
rewriting it, and I did not open the exclusion path.** Treat it as an effect to verify, not a fix to
claim. (`canon-strings.json:164` also contains `SolanaWalletProvider` inside an `_authoringNote`; that is
a prefix, so it is NOT suppressed and the sweep still sees it.)

---

## What the next AAB's `PLAY_ARTIFACT_DIRTY` list should contain

Predicted, not measured — the offsets move every content build:

- ❌ **`crypto` — GONE.** The seam was its only cause; whole-file live count was zero.
- ❌ **the `437e0227…` receipt entry — GONE.**
- ✅ **`global-metadata.dat token:solana`** — still fires. Two live literals remain and are product work,
  not gate work: `LevelPlayInitializer.cs:226` and `CatalogFallbackData.g.cs:107`. The two ruled enum
  identifiers no longer contribute.
- ✅ **`global-metadata.dat token:skr`** — still fires. ~11-12 authored literals remain (RCA §2.4). The
  `dotr-arena-skr-balance` literal at 1,585,378 is suppressed; ⚠ the serialized copy at 10,606,706
  (`ArenaWallet,dotr-arena-skr-balance`) is bounded on the LEFT by a comma but **its right-hand neighbour
  was never read**, so whether that one is suppressed is UNPROVEN. Also unresolved: the untraced bare
  `SKR` at 239,196.
- ❓ **`web3` on both entries** — the genuine leak, and §3's open owner ruling. ⚠ **I cannot say whether
  it will appear:** the brief states the `DeNelle.Core.Web3` → `DeNelle.Core.Backend` rename is landed
  but ungated in this tree. If the folder moved with the namespace, `web3` should be gone; if either half
  is outstanding, it fires. Do not read a green `web3` as this WO's doing.

**So: a fully green `PLAY_ARTIFACT_CLEAN_OK` is NOT expected from this change alone.** Exactly as
WO-1741 §7 said of its own run.

---

## Checks run this session

| Check | Result |
|---|---|
| `python tools/gate_brace.py` on both `.cs` | `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0 |
| NUL scan (file bytes) on all three files | 0 |
| Raw brace balance | gate 54/54, suite 22/22 |
| PowerShell `Parser::ParseFile` on the mirror | 0 errors |
| Mirror's `Get-GateTokenArray`, verbatim, vs the edited gate | 31 / 4 / 27 / 3, literals exact |
| Algorithm simulation (matcher + both loops, arrays read from source) | 17/17 pass |
| Existing `GooglePlayPackagingRegression` source pins | **ALL 46** `Require(...)` / `Reject(...)` string pins extracted by regex from that suite and counted against the two EDITED files: **46 checked, 0 violations**. Every `Require` present, every `Reject` (`OpaqueExecutableTokens`, `$userFacingTokens = @(`, `$opaqueTokens = @(`, `"mwa/"`, `'mwa/'`, `"phantom"`, `'phantom'`) still absent — the new comment text introduced none of them. ⚠ `GetEncoding(28591)` now occurs **once**, in `Find-StreamTokens` only, because `Test-StreamToken` no longer carries its own copy of the loop; a `Contains` pin is satisfied, and that was confirmed by count, not assumed |

## NOT done / NOT proven

- **No compile, no gate, no regression run, no build, no git** — per the brief.
- **`COMPILE_GATE_OK` / `REGRESSION_OK` are unproven.** The suite has never executed.
- **Re-measured live-hit counts (acceptance 3) not produced.**
- **Acceptance 2 (a fresh AAB run) not produced.**
- `Test-StreamToken` in the mirror now DELEGATES to `Find-StreamTokens` instead of carrying a second
  hand-written copy of the chunked scan. It has no caller inside the script; it is kept because
  `GooglePlayPackagingRegression` pins the name. Behaviour change is intended and documented in place.
