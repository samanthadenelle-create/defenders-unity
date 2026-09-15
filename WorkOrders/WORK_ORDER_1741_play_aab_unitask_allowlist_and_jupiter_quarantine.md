# WO-1741 — The two owner rulings of 2026-09-15: allowlist the UniTask paths, quarantine the Jupiter panel from Play only

**Status:** DONE
**Silo:** Play packaging / content exclusion — `Assets/Editor/GooglePlayContentExclusion.cs`, `Assets/Editor/Regression/GooglePlayPackagingGate.cs`
**Opened:** 2026-09-15 (edit-only lane)
**Implements:** WO-1740 (the RCA — now UNBLOCKED by these rulings)
**Related:** WO-1739 (the reporting defect that hid all of it, DONE), WO-1363/1364 (the sweep + the gate), WO-1377 (the metadata-identifier ruling that decides §(D))

---

## 1. The rulings, verbatim

> ### OWNER RULING 1 — THE UNITASK CEILING: ALLOWLIST THE PATHS, WITH A WRITTEN REASON
>
> Most `solana` hits in `global-metadata.dat` are the SOURCE-FILE PATHS of vendored UniTask
> (`Packages/com.solana.unity_sdk/Runtime/Plugins/UniTask/…`) — a general-purpose async library that
> merely lives under that directory. It is not crypto code and no script can strip a path.
>
> Add a narrow, explicit allowlist to the gate for those UniTask paths. Requirements:
> - **Scope it as tightly as the evidence allows** — allowlist the UniTask subpath, NOT the whole
>   `com.solana.unity_sdk` package. A future real Solana leak from that package must still be caught.
> - **Write the REASON into the gate itself**, in prose, naming: what UniTask is, why a vendored path
>   string is not a policy surface, and that this is an owner ruling of 2026-09-15. A future seat must be
>   able to see why this is not a hole someone punched to make a gate green.
> - If you can distinguish "path-only metadata hit" from "real code/asset hit" structurally rather than by
>   an allowlist, PREFER THAT and say so — an allowlist is the ruling's mechanism, not necessarily its
>   only correct implementation.

> ### OWNER RULING 2 — THE JUPITER PANEL: QUARANTINE FROM PLAY ONLY
>
> ⛔ **The dApp Store / Solana build MUST KEEP the panel and every wallet surface.** That build carries the
> owner's ONLY real transactions (one SKR, one Pi). Only the GOOGLE PLAY variant strips.
> - Add `Assets/_Modules/Web3/Resources` to `PlayExcludedAssetPaths` (`:131-151`), matching the shape of
>   the existing `Assets/Resources/SolanaUnitySDK` entry.
> - **Sweep for the same pattern elsewhere**: any `Resources` folder under a module whose asmdef is
>   constrained off for Play is the identical latent leak. Report every one you find, whether or not you
>   quarantine it. This class — "the asmdef excludes the code, the Resources folder ships anyway" — is the
>   real finding and it will recur.

---

## 2. Ruling 1 — the UniTask allowlist, and why an allowlist really is the right mechanism here

**Considered and rejected: a structural "path-only hit" test.** The ruling invites a structural
discriminator in preference to an allowlist. There is a plausible one — suppress a hit whose surrounding
printable run looks like a source-file path (ends `.cs`, contains a directory separator). It was rejected
because **it is strictly WIDER than the allowlist, not narrower**: it would suppress
`…\Runtime\codebase\InGameWallet.cs` and `…\Plugins\SolanaWalletAdapterWebGL\*.cs` — real crypto source
paths — with the same stroke. The ruling's own first requirement is "scope it as tightly as the evidence
allows", and the allowlist is the tighter instrument. `IsAllowlistedOccurrence` already suppresses a hit
**only when the matched occurrence lies inside the listed phrase**, which is exactly the semantics wanted.
So: allowlist, and this paragraph is the "say so".

**The exact scope shipped** — two entries appended to `GooglePlayPackagingGate.FalsePositiveAllowlist`:

```csharp
"com.solana.unity_sdk/runtime/plugins/unitask",
@"com.solana.unity_sdk\runtime\plugins\unitask"
```

Both separators, because `global-metadata.dat` carries Windows-built paths with backslashes while the same
path appears forward-slashed elsewhere. The backslash entry is a **verbatim** string so that the
`tools/android/assert-google-play-aab-clean.ps1` parser — which extracts these literals with
`"([^"]*)"` — reads the identical value the compiler does. (Proven: the parser simulation returns
`com.solana.unity_sdk\runtime\plugins\unitask`, single backslashes.)

**Why NOT the package, MEASURED 2026-09-15 rather than assumed.** `ls Packages/com.solana.unity_sdk/Runtime/`
returns `codebase/` and `Plugins/`. `codebase/` holds `IWalletBase.cs`, `InGameWallet.cs`,
`DeepLinkWallets/`, `Metaplex/`. And **UniTask's own siblings inside `Plugins/` are
`SolanaWalletAdapterWebGL/` and `Web3AuthSDK/`** — real crypto surfaces. An allowlist on the package, *or
even on `Runtime/Plugins/`*, would have suppressed them. The tight scope is not tidiness; it is the
difference between a documented suppression and a hole.

**Proof the scope holds** (simulation of `MatchesTokenInPayload` + `IsAllowlistedOccurrence` against the
shipped allowlist, 9/9 pass):

| Payload | Token | Expected | Result |
|---|---|---|---|
| `\Packages\com.solana.unity_sdk\Runtime\Plugins\UniTask\Runtime\AsyncLazy.cs` | `solana` | suppress | suppress |
| `Packages/com.solana.unity_sdk/Runtime/Plugins/UniTask/Runtime/Channel.cs` | `solana` | suppress | suppress |
| `…\Runtime\Plugins\SolanaWalletAdapterWebGL\X.cs` | `solana` | **fire** | **fire** |
| `…\Runtime\Plugins\Web3AuthSDK\Y.cs` | `solana` | **fire** | **fire** |
| `…\Runtime\Plugins\Web3AuthSDK\Y.cs` | `web3` | **fire** | **fire** |
| `…\Runtime\codebase\InGameWallet.cs` | `solana` | **fire** | **fire** |
| `com.solana.unity_sdk@1.2.9` (the package receipt) | `solana` | **fire** | **fire** |
| `Solana.Unity.SDK.Wallet` | `Solana.Unity.` | **fire** | **fire** |
| `…\Plugins\UniTask\Solana.Unity.Foo.cs` | `Solana.Unity.` | **fire** | **fire** |

The last row is the one that matters most: the phrase contains **exactly one** forbidden token, `solana`,
so it can never mask a second one even inside an allowlisted path.

**The reason prose is written into the gate**, immediately above the two entries, covering what UniTask is,
why a vendored path string is not a policy surface, why the scope stops where it does, and that this is the
owner's ruling of 2026-09-15.

---

## 3. Ruling 2 — the Jupiter panel, quarantined from Play only

`Assets/_Modules/Web3/Resources` added to `PlayExcludedAssetPaths`, in the shape of the existing
`Assets/Resources/SolanaUnitySDK` entry, with a dated verification remark in the array's `<remarks>` block
per that block's own stated convention.

**The Seeker guarantee is structural, not a promise.** `ApplyForDefines` *derives* the tree's shape from
the build's defines: a non-Play Android build calls `RestoreAll` and re-asserts the whole tree. So the
panel is present in the dApp Store artifact by the same mechanism that removes it from Play. Nothing was
deleted; nothing is unconditional.

**Dead-code proof for the entry (the array's contract requires one).** `JupiterSwapPanel` appears nowhere
outside `Assets/_Modules/Web3/` except a single **comment** at
`Assets/Editor/Regression/PanelDoorRegression.cs:33` (editor-only). No `Resources.Load`, no scene
reference, no Addressable, no UXML template include. The only loader is `JupiterSwapPanelController` in
`DeNelle.Web3`, whose asmdef carries `!GOOGLE_PLAY`.

### 3a. The sweep for the same pattern — the class, not just the instance

Every `Resources` folder under `Assets/_Modules/`, cross-referenced against every asmdef carrying a
`GOOGLE_PLAY` constraint:

| `Resources` folder | Owning asmdef | `defineConstraints` | Leak? |
|---|---|---|---|
| `Assets/_Modules/Web3/Resources` | `DeNelle.Web3` | `!GOOGLE_PLAY` | **YES — the instance. Now quarantined.** |
| `Assets/_Modules/Audio/Resources` | `DeNelle.Audio` | `[]` (read at source) | No — assembly ships on Play; the payload is intentional |
| `Assets/_Modules/Onboarding/Resources` | `DeNelle.Onboarding` | `[]` (read at source) | No — same |

`DeNelle.Wallet` (`!GOOGLE_PLAY`) has **no** `Resources` folder. `DeNelle.GooglePlay` is constrained
`GOOGLE_PLAY` **positive** — Play-only by design, not a leak.

### ⚠ A SECOND INSTANCE EXISTS, OUTSIDE `Assets/` — REPORTED, DELIBERATELY NOT QUARANTINED HERE

The sweep was widened past `Assets/_Modules/` because **Unity force-includes a `Resources` folder from an
embedded PACKAGE exactly as it does one under `Assets/`**, and the two remaining `!GOOGLE_PLAY` asmdefs
live in `Packages/`. `find Packages/com.solana.unity_sdk -type d -name Resources` returns:

```
Packages/com.solana.unity_sdk/Resources/background.png
Packages/com.solana.unity_sdk/Resources/DefaultCollectionIcon.png
Packages/com.solana.unity_sdk/Resources/magicblock-logo.png
```

`com.solana.unity_sdk.asmdef` carries `!GOOGLE_PLAY`, so the package's **code** is excluded from the Play
player while these three textures are force-included into it — the identical class, one directory root
over. `magicblock-logo.png` is Solana-ecosystem **branding art**. It is **not** in
`PlayExcludedAssetPaths` (grep: NOT LISTED).

**Why it was not quarantined in this lane, and this is a judgement the lead should confirm:**
- It does **not** currently make the gate fire. None of the three filenames carries a forbidden token
  (`magicblock` is not in the vocabulary), and the gate reported no hit for them.
- The quarantine mechanism is `AssetDatabase.MoveAsset` into `Assets/PlayQuarantine`. Every existing entry
  moves **within** `Assets/`; a `Packages/` -> `Assets/` move crosses asset roots and is not a shape this
  file has ever exercised. Doing it blind, in a lane that cannot run Unity, risks the one failure this
  file's header calls worse than the leak it closes: a half-restored tree that silently strips the Seeker
  build.
- Neither owner ruling covers it.

**It is a policy question (shipping Solana-ecosystem branding in a Play artifact), not a gate failure.**
Recorded for a ruling; see §8.

### 3b. Evidence on live-vs-dormant, recorded for a future ruling — NOT acted on

The ruling is quarantine, and quarantine is correct under either reading, so nothing here changes what
shipped. Recorded so the eventual live/dormant ruling has its inputs:

- `PanelDoorRegression.cs:32-35` lists `JupiterSwapPanelController` among types deliberately excluded from
  the door oracle, reason given: *"They are not screens the player routes to."* → not a routed player door.
- `Assets/_Modules/Web3/README.md`, read at source 2026-09-15, describes the module as *"Jupiter (Solana
  DEX) swap integration. Phases per WO-43/44/45, WO-210"* and lists `WalletBridgeStub` — *"stand-in for
  `IWalletSigner` until Wallet integration"*. A **stub** signer is the strongest signal yet that the swap
  was never wired to a real wallet.
- Countervailing: the panel's copy is localized into five languages (`swap.poweredBy`), which suggests the
  feature was finished at some point.

⚠ `"Powered by Jupiter Aggregator"` exists **twice** — hard-coded in the UXML *and* as the localized
`swap.poweredBy`. `GooglePlayLocalizationVariant` already handles the localized copy correctly (27 assets
transformed on the 2026-09-15 run; not one locale file appears in the dirty list). **The localized path was
deliberately left alone** — editing it would re-fix a green path, and canonical JSON is binary-edit-only
with mirrors that must stay in sync. Quarantining the folder closes the UXML copy, which is the one that
leaks.

---

## 4. (B) The leading-space detector defect — and deleting the fourth vocabulary

`ContainsForbiddenAuthoringToken` held a **fourth** hand-maintained copy of the policy, including
`" skr"` **with a leading space**. A token check with a leading space matches mid-sentence and never at the
start of one, so:

```
storeBalanceBoundIdentity = "SKR: identity bound - authorize"
storeBalanceUnavailable   = "SKR: unavailable in this build"
```

were never detected, `NeutralizeForbiddenStrings` returned 0 for them, and the already-authored neutral
replacements sitting in `PlayNeutralStringReplacements` for **exactly those two keys** were never
consulted. Both are rendered HUD copy. **Independently re-derived this session** by walking
`canon-strings.json` under the old vocabulary: of the seven `storeBalance*`/`storeCommerce*` values, five
were caught by bare `wallet` and **precisely those two were missed** — the WO-1740 RCA confirmed without
relying on it.

**Fixed by deleting the copy, not improving it.** The method now calls
`GooglePlayPackagingGate.ContainsForbiddenAuthoringToken(value)`, so the sweep consumes the gate's
vocabulary *and* the gate's matcher (word boundaries + the documented false-positive suppressions).

**The vocabulary is a UNION, and that detail is load-bearing.** Consuming `ForbiddenTokens` *alone* would
have been a **regression**: the gate has no bare `wallet`, and `storeBuyWalletRequired` reads *"need a
connected wallet"*, which `connect wallet` does **not** match (`connected` ≠ `connect `). Five player-facing
strings would have started shipping their Seeker copy to Play. So the gate gained a small, documented
`AuthoringOnlyTokens = { "wallet" }` — tokens the authoring sweep polices that the artifact scan
deliberately does not, kept beside the vocabulary it extends so there is still one source of truth. Net
effect: **strictly stronger than either previous list.**

**MEASURED before the change** by walking every string value of all five swept catalogs under the new
union vocabulary: every token-bearing value resolves to `MAPPED` or `_`-prefixed `NOTE`. **Zero new
`PLAY_NEUTRAL_UNMAPPED_TOKEN` failures.** `storeBuyWalletRequiredCta` = `"Connect Wallet"` is now caught by
the gate's `connect wallet` token and takes its mapped `"Continue"`.

---

## 5. (C) The three catalogs outside the sweep

**No JSON file was edited.** The sweep runs at build time against a backup-and-restore ledger, so closing
these leaks needs list entries, not content edits — which also keeps the lane clear of the
binary-edit-only / mirror-sync hazard entirely.

- **`structures-catalog.json`** — added as a **mirror pair** (both copies byte-identical, 113776 bytes,
  `cmp`). It is a **live gameplay catalog** (`StructureFactory` reads `visualPrefabPath`), so it must be
  SWEPT, never quarantined — removing it would strip every building from the Play build. Its **only**
  token-bearing value, measured across both mirrors, is `entries[21]._quarryNote`; the `_` prefix routes it
  to the neutral-note branch, so the addition cannot trip `PLAY_NEUTRAL_UNMAPPED_TOKEN`.
- **`ad-placements.json`** — row widened from one element to the **pair**. The comment claiming *"has NO
  StreamingAssets twin (verified 2026-09-04)"* was **stale the day it was written**: the twin was created
  on 2026-09-04 by `32af7767c` (WO-1333). Both files measured 12853 bytes and byte-identical. **The same
  stale fact appeared twice** — also at `ValidateNeutralMirrorEquality` — and **both** copies were
  corrected in this edit. Its two hits (`_LAW_1_NO_PREMIUM_CURRENCY`, `_REMOVED_2026_08_07.reward.crystals.small`)
  are both `_`-routed.
- **`offline-storage.json`** — **quarantined, not swept.** It carries the VALUE `"skr"` at
  `premium.currency`, a non-`_` key with no neutral copy, which the sweep would *correctly* refuse to ship.
  It has **no runtime reader at all**: the only two matches for `offline-storage` under `Assets/_Modules/`
  are prose comments in `WelcomeBackPopup.cs` (`:354`, `:903`). Removing the file from the Play artifact is
  strictly stronger than neutralising its strings, and it cannot starve a reader that does not exist.

---

## 6. (D) `DeNelle.Core.asmdef` — DO NOT ADD A DEFINE CONSTRAINT. This needs an owner ruling.

**Decision: no edit. The proposed fix is unsafe and the honest fix is a different one.**

Adding `!GOOGLE_PLAY` to `Assets/_Modules/Core/DeNelle.Core.asmdef` would **delete the entire core
assembly from the Google Play player** — every interface, every service, `CoreServices` itself. It does not
scope to `Core/Web3/`; an asmdef constrains its whole assembly. It would not merely break the Solana build,
it would break *both*.

The per-file remedy is also already partly applied and partly **forbidden**:

- `IJupiterService.cs` **already carries `#if !GOOGLE_PLAY` around the whole namespace body** (`:45`), and
  `PlayMetadataIdentifierRegression` pins it **two-sided** — absent with `GOOGLE_PLAY`, present without —
  precisely so nobody "fixes" it by deleting a real dApp-lane feature.
- `BackendRequestSigner.cs` **must compile on Play**: it is the backend save-auth path and carries its own
  `#if GOOGLE_PLAY` arm at `:205`. `IWalletSigner` is the Core-side seam `GameStateService` resolves; Core
  cannot reference `DeNelle.Wallet` (circular). Compiling either out would break Play saves.

**So the residual leak is the NAME, not the code**: the namespace `DeNelle.Core.Web3` and the folder path
`Assets\_Modules\Core\Web3\…` land in `global-metadata.dat` as identifiers and source paths, and the gate's
`web3` token fires on them. **The fix is a rename to a neutral name** (e.g. `DeNelle.Core.Backend` /
`Assets/_Modules/Core/Backend/`) — a mechanical but wide change touching `CoreServices`, `GameStateService`
and at least five regression suites that pin the path string. That is **a ruling to request, not a change
to smuggle into this lane.**

WO-1377 already recorded the same shape for `PaymentChannel.SolanaDappStore` and `SkinAuthMode.SolanaWallet`
as **accepted residuals**, explicitly not to be renamed or reordered.

---

## 7. ⛔ THE NEXT AAB WILL STILL SAY `PLAY_ARTIFACT_DIRTY`. Do not run it expecting green.

This lane closes (A), (B), (C) and the UniTask ceiling. It does **not** close the WO-1740 §2(D)
first-party identifier list, and the gate's matcher fires on every one of these:

| Residual | Why it still fires | Status |
|---|---|---|
| `DeNelle.Core.Web3` + `\Assets\_Modules\Core\Web3\*.cs` | `web3`, boundary `.`/`\`, printable run ≥ 12 | **§6 — needs a rename ruling** |
| `SolanaWallet` (`CurrencySkin.cs:27`), `SolanaDappStore` (`IPaymentProvider.cs:10`) | `solana` | WO-1377 accepted residual; renaming **forbidden** |
| `dotr-arena-skr-balance` (`ArenaWalletService.cs:50`) | `skr` with boundaries, run 22 | needs a ruling |
| `"... CRYSTALS/SKR/PURCHASED GOODS ..."` (`DefenseReportBuilder.cs:439`) | `skr` | needs a ruling |
| `com.solana.unity_sdk@1.2.9` package-provenance blob | `solana` | same class as `dependencies.pb`, already skipped at `GooglePlayPackagingGate:310-312`; **deliberately not extended here** — that is a gate change beyond these rulings |

---

## 8. Residuals recorded, deliberately not fixed

- **Chunk-seam allowlist truncation.** `ScanStream` retains ~264 bytes across 64 KB boundaries. A hit whose
  allow phrase straddles a chunk end is recorded before the next chunk sees full context. **Pre-existing
  for every allowlist entry**, but dozens of UniTask paths raise the odds. If the next AAB reports one
  stray UniTask-path `solana`, this is why — not a scope error in the entry.
- **`DateParseHandling` on the JSON round-trip.** `JObject.Parse` defaults to `DateParseHandling.DateTime`,
  so a value that is exactly an ISO date would round-trip to `…T00:00:00`. On a live gameplay catalog that
  would be an unpinned Play/Seeker divergence. **MEASURED 2026-09-15: zero ISO-date-shaped values in any of
  the five swept catalogs**, so it is not a live hazard and no behaviour was changed. Named here so the
  next seat adding a catalog to the sweep checks it.
- **Follow-up: rule on `Packages/com.solana.unity_sdk/Resources`** (§3a). Three force-included textures,
  one of them Solana-ecosystem branding, shipping into the Play player from a `!GOOGLE_PLAY` package. Not a
  gate failure today; needs an owner call and a `Packages/`-aware move path before it is quarantined.
- **Follow-up: generalise the `Resources`-under-constrained-asmdef guard.** WO-1740 §4.1 asks for a
  regression failing if any `Assets/_Modules/*/Resources` belongs to a `!GOOGLE_PLAY` asmdef and is not
  quarantined. **Not written here**: `PlayExcludedAssetPaths` is `internal` to `DeNelle.Editor`, and
  `DeNelle.EditorRegression` does not reference `DeNelle.Editor` (the reference runs the other way), so the
  oracle would have to be a source-text regex — a second uncompiled file in a lane that cannot gate. Seed
  for that ticket: the three-folder sweep in §3a.

## 9. What NOT to touch

- **Do not weaken `GooglePlayPackagingGate`.** Every hit it reported was real. The only suppression added
  is the two UniTask path entries, scoped and reasoned in §2.
- **Do not remove wallet support globally.** Every mechanism stays define-derived per artifact.
- **Do not add a fifth token vocabulary.** The sweep now consumes the gate's.
- **Do not edit the locale JSON / `GameStrings_*.asset` for `swap.poweredBy`** — already green via
  `GooglePlayLocalizationVariant` (§3b).
- **Do not add a define constraint to `DeNelle.Core.asmdef`** (§6).
