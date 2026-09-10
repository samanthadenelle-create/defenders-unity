# WO-1377 RESULT — the safe set is compiled out; the two `Solana*` members are accepted, with reasons

**Date:** 2026-09-09
**Lane:** META (read-only Phase 1, then edit-only Phase 2 — no Unity, no git, no gate run)
**Owner ruling executed, verbatim:** *"Prove persistence first; move only what is safe, accept the
rest with a recorded reason."*

⛔ **NOT VERIFIED HERE, AND SAID PLAINLY (CLAUDE.md §11B):** no compile, no gate, no regression run,
no artifact scan. Every line below is a SOURCE reading or a simulated preprocessor evaluation done
this session. The Play-variant compile, the dApp-variant compile, the `global-metadata.dat`
before/after counts and the save round-trip are all still OPEN and belong to the gating seat.

---

## §1. PHASE 1 — the persistence proof, per identifier

**Method (every path grepped, not inferred):** `Assets/_Modules/Core/State/*` and any `Save*` path
for a field of these types; `PlayerPrefs` writers in `Core/Payments`, `Core/Platform`, `Core/Web3`;
`[JsonProperty]` / `JsonUtility` fields; `ToString()` / `Enum.Parse` / `Enum.TryParse` round-trips;
`Assets/Resources/Data/**/*.json`; `api/**`.

| Identifier | Where it lives (file:line) | Assembly | asmdef `!GOOGLE_PLAY`? | Persisted? | Evidence | Verdict |
|---|---|---|---|---|---|---|
| `PaymentChannel` (type) + `SolanaDappStore = 1` | `Assets/_Modules/Core/Payments/IPaymentProvider.cs:7,10` | `DeNelle.Core` | **NO** — `Assets/_Modules/Core/DeNelle.Core.asmdef` `"defineConstraints": []` | **NO** | `grep PaymentChannel Assets/_Modules/Core/State/` → **0 hits**. No field of the type outside the resolver/provider seam (full list: `IPaymentProvider.cs:71,115`, `PaymentChannelResolver.cs:14,16,34`, `GooglePlayBillingProvider.cs:36`, `PiBrowserPaymentProvider.cs:120`, `CurrencySkinResolver.cs:303`, `PackStore.cs:785`, `PurchaseGate.cs:152,167,291,438`) — every one a **live in-memory read**, none a serializer. No `PlayerPrefs` writer of the enum. No `ToString()`/`Parse` of it anywhere. `SolanaDappStore` in `api/` → 0 hits. The only *stamped* string is `ArtifactVariantStamp.VariantName` (`:34-47`), which returns hand-written `"GOOGLE_PLAY"` / `"DAPP_STORE"` literals — deliberately **"a separate, plain string so a log reader needs no enum table"** (`:31-33`) — and goes to `FlowTrace`, not to disk. | **STAYS — accepted, see §3** |
| `SkinAuthMode` (type) + `SolanaWallet = 1` | `Assets/_Modules/Core/Platform/CurrencySkin.cs:22,27` | `DeNelle.Core` | **NO** | **NO to a save — but BOUND BY NAME to shipped canonical data** | `grep SkinAuthMode Assets/_Modules/Core/State/` → **0 hits**. ⛔ It **is** parsed by NAME: `CurrencySkinResolver.ParseAuth` (`:499-505`) does `s.Trim().Equals("SolanaWallet", OrdinalIgnoreCase)` against `Assets/Resources/Data/Canonical/skin.json:30` `"authMode": "SolanaWallet"` (and the byte-twin `Assets/StreamingAssets/Data/Canonical/skin.json:30`). That is a **content file, not a player save** — so a rename is not save data-loss, it is a code↔data contract break. The one `ToString()` (`LoginPanelController.cs:248`) lands only in the `FlowTrace.Step("Auth", …)` at `:269-275`; **traced, never written**. | **STAYS — accepted, see §3** |
| `IJupiterService`, `SwapQuote`, `SwapInputToken`, `SwapInputToken.USDC` | `Assets/_Modules/Core/Web3/IJupiterService.cs` — **post-edit** lines: `interface :52`, `class SwapQuote :75`, `enum SwapInputToken :92`, `USDC = 0 :95`, guard `#if !GOOGLE_PLAY :45` … `#endif :100` | `DeNelle.Core` | **NO** | **NO** | No field of any of them in `Core/State`. Full consumer list, measured: `CoreServices.cs:155,158,167,173` (the registry slot); `Assets/_Modules/Web3/{JupiterSwapService,JupiterSwapBootstrap,SwapVM}.cs` — assembly **`DeNelle.Web3`, `DeNelle.Web3.asmdef:17` `"!GOOGLE_PLAY"`**; `Assets/Tests/EditMode/SwapVMTests.cs` — `DeNelle.Tests.EditMode`, `"defineConstraints": ["UNITY_INCLUDE_TESTS"]`, `includePlatforms: ["Editor"]`, never in a player build. **Zero GOOGLE_PLAY-side consumers.** | ✅ **MOVED (compiled out at type level)** |
| `CoreServices.Jupiter` / `RegisterJupiter` / `UnregisterJupiter` | `Assets/_Modules/Core/CoreServices.cs` — **post-edit**: guard `#if !GOOGLE_PLAY :159`, `Jupiter :164`, `RegisterJupiter :167`, `UnregisterJupiter :180` | `DeNelle.Core` | **NO** | **NO** | Registry slot only; the only registrant is `JupiterSwapService.cs:99,112` in `DeNelle.Web3`. No caller anywhere else in the tree. | ✅ **MOVED (compiled out at member level)** |
| `FeatureFlags.JupiterSwap` + key literal `"jupiterswap"` | `Assets/_Modules/Core/FeatureFlags.cs` — **post-edit**: property `:1317`, editor menu `:1620` onward (both inside `#if !GOOGLE_PLAY`) | `DeNelle.Core` | **NO** | **PlayerPrefs key, yes — but not a save field, and NOT renamed** | The flag reads/writes `PlayerPrefs "ff.jupiterswap"` (`:1610`). That is a per-device debug toggle, **not** `SaveSchema`/`PersistedState`; `SaveSchema.PlayerPrefsKey` is `"dotr-save"` (`SaveSchema.cs:46`). The key literal is **unchanged** — the property is compiled out on Play only, so on the dApp lane the same key still reads the same value. Sole runtime caller: `JupiterSwapBootstrap.cs:92` (`DeNelle.Web3`, excluded). | ✅ **MOVED (compiled out at member level)** |
| ⚠ `SwapToken.USDC` — **the WO's name is WRONG** | no such type exists | — | — | — | `grep -rn "SwapToken" --include=*.cs Assets/` → **0 hits**. The real identifier is **`SwapInputToken.USDC`**, `IJupiterService.cs:66,69`. A seat hunting `SwapToken` finds nothing and concludes the identifier is already gone. | corrected in place |

**SaveSchema cross-check:** `SaveSchema.CurrentVersion = 41` (`Assets/_Modules/Core/State/SaveSchema.cs:41`,
read at source today — not from `docs/reference/SAVE_SCHEMA_FIELD_MAP.md`, which is aged and whose
only `channel` rows are `ObsidianQueueState`/`ChannelId`, an unrelated build-queue axis). **No schema
bump was needed and none was made**, because nothing in the safe set is on the wire.

---

## §2. PHASE 2 — what changed

All four edits are **TYPE-LEVEL or MEMBER-LEVEL `#if !GOOGLE_PLAY`**, never a runtime guard inside a
method. That distinction is the whole ticket: a runtime `#if` removes behaviour and leaves the
identifier in `global-metadata.dat`.

| File | Change |
|---|---|
| `Assets/_Modules/Core/Web3/IJupiterService.cs` | Whole `namespace DeNelle.Core.Web3 { … }` body wrapped in `#if !GOOGLE_PLAY` / `#endif`, with a header recording the Phase-1 proof. `USDC = 0` / `SOL = 1` untouched — **no rename, no reorder**. |
| `Assets/_Modules/Core/CoreServices.cs` | The `Jupiter` slot + `RegisterJupiter` + `UnregisterJupiter` wrapped in `#if !GOOGLE_PLAY`. The pre-existing inner `#if GOOGLE_PLAY` warning arm (WO-1363's channel-neutral message) became unreachable and was collapsed to the single remaining branch — **it still warns**, never a silent replace (§12). `using DeNelle.Core.Web3;` stays valid: `BackendRequestSigner.cs` and `IWalletSigner.cs` still declare that namespace. |
| `Assets/_Modules/Core/FeatureFlags.cs` | `JupiterSwap` property wrapped in `#if !GOOGLE_PLAY`; the `Defenders/Debug/Jupiter Swap Panel` menu block wrapped too, **nested inside the existing `#if UNITY_EDITOR`**, so an editor compile carrying `-ExtraScriptingDefines GOOGLE_PLAY` does not fail on a menu that toggles a property no longer there. |
| `Assets/Editor/Regression/PlayMetadataIdentifierRegression.cs` | **NEW** oracle, `[play-metadata-identifiers]`, markers `PLAY_METADATA_IDENTIFIERS_OK` / `_FAIL`. |

**Nothing else was touched.** No `.asmdef` needed changing — the safe set stays in `DeNelle.Core` and
is *compiled out*, because a physical move is impossible: `DeNelle.Web3` references `DeNelle.Core`, so
`CoreServices` cannot reference `DeNelle.Web3` without a cycle, and `Core/Web3/` also holds
`BackendRequestSigner` (used across `DeNelle.Core`) and `IWalletSigner`, which must stay.

### The regression is TWO-SIDED on purpose
An "absent under GOOGLE_PLAY" assertion alone is satisfied by **deleting** the identifier — which this
WO's own *WHAT NOT TO TOUCH* forbids. So each of the 9 pins is asserted twice: **gone** with
`GOOGLE_PLAY` defined, **present** without it. A third case asserts `DeNelle.Web3.asmdef` still carries
`"!GOOGLE_PLAY"` — the exclusion the whole shape rests on, and the one this WO says never to widen.

Its preprocessor evaluator resolves **only** bare `GOOGLE_PLAY` / `!GOOGLE_PLAY` conditions and keeps
both arms of every other `#if`. That conservatism is chosen deliberately and in the safe direction: it
can produce a false FAILURE, never a false PASS.

⛔ **It is a SOURCE-level oracle. The physical `global-metadata.dat` scan for `solana`/`jupiter`/`usdc`
remains a SHIP-CHAIN step** (the AAB chain / `GooglePlayPackagingGate`) and is the only thing that can
prove the shipped bytes. This suite stops the source regressing between ship-chain runs; it does not
replace that scan. A source grep is exactly what certified the dirty build on 2026-09-01.

### Two neighbouring oracles checked so this change cannot break them
- **`ShippedSurfaceGateRegression` Case 3 stays GREEN.** It matches `bool\s+JupiterSwap\s*=>` against
  `ReadStripComments(FlagsRel)` (`:402-424`), which calls `RegressionSourceText.StripComments`
  (`RegressionSourceText.cs:57-60` → `Strip(src, blankStringBodies: false)`, `:67-97`). That scanner
  **blanks comment bodies to spaces and leaves preprocessor lines untouched**, so the wrapped property
  declaration still matches. Read at source, not assumed from the method name.
- **The new suite reuses that same helper** rather than hand-rolling a stripper — and deliberately
  `StripComments`, not `StripCommentsAndStrings`, because one pin (`"jupiterswap"`) *is* a quoted
  literal that the full stripper would blank out from under its own assertion.

**Pre-run simulation (not a substitute for the gate):** the evaluator's algorithm was reimplemented
line-for-line and run over the three edited files this session. All 9 pins: `underGP=False`,
`offGP=True`. A token sweep of the surviving GOOGLE_PLAY text of those three files returns
`jupiter|usdc|solana` = **zero** in all three. The one non-zero hit, `SKR` in `FeatureFlags.cs`, is a
**false positive of the deliberately-conservative evaluator**: it keeps both arms of every non-
`GOOGLE_PLAY` `#if`, so the `#if UNITY_EDITOR` menu strings at `:1531,:1539,:1551,:1559` survive the
simulation even though `UNITY_EDITOR` is false in a player build. **They are not in the Play artifact**,
and they are not one of this WO's identifiers. The evaluator failing in *this* direction is the design
(see §2): it can raise a false alarm, never grant a false pass.

---

## §3. THE ACCEPTED RESIDUALS — recorded reasons (owner ruling: "accept the rest with a recorded reason")

### `PaymentChannel.SolanaDappStore` — STAYS
It is referenced by a **live, un-`#if`'d `switch` case inside `DeNelle.Core`**:
`CurrencySkinResolver.ResolveWagerCurrency` (`Assets/_Modules/Core/Platform/CurrencySkinResolver.cs:310`),
which compiles into the Play build. Compiling the member out breaks that switch.

And it may not simply be `#if`'d there either, because the owner's Arena ruling forbids it — the file's
own comment at `:266-272` states it verbatim: *"ONE Arena, ONE code path; the CURRENCY is the only
thing that varies by channel, and it is resolved HERE … **never by a `#if GOOGLE_PLAY` inside the Arena
module**."* The per-channel currency table (`GooglePlay -> Crystals`, `SolanaDappStore -> SKR`) is
exactly the seam that ruling created. **Removing the channel to hide a token would fork the code path
the owner ruled must not fork.**

Not persisted (§1). Explicit value `= 1`, so no reorder hazard. **Never rename it, never reorder it.**

### `SkinAuthMode.SolanaWallet` — STAYS
Bound **by NAME** to shipped canonical data: `CurrencySkinResolver.ParseAuth:501` matches the literal
`"SolanaWallet"` against `Assets/Resources/Data/Canonical/skin.json:30` (and its `StreamingAssets`
twin). It also has live references in shipping code — `CurrencySkin.cs:143`, `PiSignInController.cs:501`.
Compiling it out breaks the skin table for every build, not just Play.

It is **not** in a player save, so a rename is not save data-loss — but it is still a code↔data
contract, and this WO's own rule holds: **never rename a name-bound enum member, never reorder one.**

### Two residuals this lane MEASURED but did not act on (out of the WO's named scope)
- **`web3` survives as the namespace `DeNelle.Core.Web3`**, because `BackendRequestSigner` lives there
  and is used across `DeNelle.Core` (`SkuEntitlementService.cs:58,65`, `CardCollectionRemoteService.cs:27`,
  `GameStateService`, …). `web3` is in `GooglePlayPackagingGate.OpaqueExecutableTokens`' deliberately-
  dropped set, so today's gate does not see it either way. **Not in this WO's identifier table — flagged, not fixed.**
- **`SkrPreview` / `"skrpreview"` and `StakeDemoMenu` in `FeatureFlags.cs`** still carry `SKR`. Measured:
  the `SKR` occurrences at `:1531,1539,1551,1559` are all inside the file's `#if UNITY_EDITOR` menu
  block, so they are **not in a player build at all**; `SkrPreview` at `:642` is a live shipping
  property whose name is `Skr*`, not a bare `skr` token. Out of this WO's scope; **pinning it is a
  separate ticket**, and `ShippedSurfaceGateRegression` already fails if `SkrPreview` stops defaulting OFF.

---

## §4. REGISTRATION LINE FOR THE GATING SEAT (I did not edit `DataRegression.cs`)

⛔ **THE TREE IS RED UNTIL THIS LANDS, AND THAT IS BY DESIGN.**
`RegressionMarkerRegression` RULE 3 (`Assets/Editor/Regression/RegressionMarkerRegression.cs:592-599`)
fails any oracle exposing `Run(out string)` that is not referenced in `DataRegression.RunAll`:
*"an unregistered oracle is a file that never runs."* The alternative — declaring
`regression-registry: standalone` in the header — was **deliberately not taken**, because a suite that
opts out of the registry is a suite that never runs in the gate.

Add to `Assets/Editor/Regression/DataRegression.cs`, alongside the other Play/surface oracles
(the shipped-surface line sits at `:1325`; anywhere in `RunAll`'s body works):

```csharp
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "play-metadata-identifiers suite", () => { if (!DeNelle.Editor.Regression.PlayMetadataIdentifierRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[play-metadata-identifiers] " + r); });
```

⚠ **COLLISION WARNING — `DataRegression.cs` IS ALREADY `M` IN THE SHARED TREE.** `git diff --stat`
reads **+39 lines from another lane**, uncommitted. Add the line above *on top of* that diff; do **not**
`git checkout`/revert the file to clear an unrelated change, or both lanes are lost (CLAUDE.md §11
multi-session reconciliation: stage by explicit path, never blind-replace a file).

**No suite-count constant to bump.** `RegressionMarkerRegression.TryGetExpectedSuiteCount` (`:1062-1079`)
DERIVES the expected count by parsing `DataRegression.RunAll`'s body, so the `<n>/<n>` in the marker
moves on its own when the line above lands.

---

## §5. FILE QUALITY GATE (CLAUDE.md §1 + WO-434)

| File | Braces | NUL bytes |
|---|---|---|
| `Assets/_Modules/Core/Web3/IJupiterService.cs` | 10 / 10 ✓ | 0 ✓ |
| `Assets/_Modules/Core/CoreServices.cs` | 31 / 31 ✓ | 0 ✓ |
| `Assets/_Modules/Core/FeatureFlags.cs` | 33 / 33 ✓ | 0 ✓ |
| `Assets/Editor/Regression/PlayMetadataIdentifierRegression.cs` | 53 / 53 ✓ | 0 ✓ |

---

## §6. UNPROVEN — the open acceptance criteria, named rather than ticked

- ⛔ **Neither variant was compiled.** `-ExtraScriptingDefines GOOGLE_PLAY` **and** the default build
  both still need to pass. The lane was instructed not to run Unity. The WO's own warning applies:
  a Play-only change can pass the default compile and still be wrong.
- ⛔ **The `global-metadata.dat` before/after counts for `solana`/`jupiter`/`usdc` have NOT been
  measured.** WO-1362 §1 recorded `Jupiter x12` in the 09-01 artifact; there is no "after" reading.
  A source grep does not close this criterion and is not offered as if it did.
- ⛔ **No save round-trip was run.** Phase 1 proves *nothing in the safe set is on the wire*, which is
  why no migration was written — but the criterion says prove it with a real load, and that is open.
- ⚠ **Pre-existing, not caused here:** `Assets/Tests/EditMode/DeNelle.Tests.EditMode.asmdef` references
  `DeNelle.Web3` with **no** `!GOOGLE_PLAY` constraint of its own. That assembly is
  `UNITY_INCLUDE_TESTS`/Editor-only and is never compiled into a player build, so a Play *player* build
  is unaffected — but an **editor** compile carrying `GOOGLE_PLAY` would already have failed there
  before this change. Recorded so nobody attributes it to WO-1377.
- **Not run, not claimed:** `COMPILE_GATE_OK`, `REGRESSION_OK`, `PLAY_METADATA_IDENTIFIERS_OK`,
  `PLAY_ARTIFACT_CLEAN_OK`. Nothing was committed.
