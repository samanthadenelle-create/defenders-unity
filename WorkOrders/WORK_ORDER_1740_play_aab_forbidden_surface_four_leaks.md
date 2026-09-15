# WO-1740 — The four surfaces that actually made the Play AAB dirty (headed by a live Jupiter swap UI)

**Status:** DONE
*(RCA verified and acted on; implemented by **WO-1741**.)*
**Blocked:** NO (was YES). ⚠ **UNBLOCKED 2026-09-15 by two owner rulings**, quoted verbatim in
`WorkOrders/WORK_ORDER_1741_play_aab_unitask_allowlist_and_jupiter_quarantine.md` §1:
**Q1 -> option (ii)** — allowlist the vendored UniTask source paths in the gate, scoped to the UniTask
subpath (not the package) with the reason written into the gate.
**Q3 -> QUARANTINE, not deletion** — the dApp Store / Solana build KEEPS the panel and every wallet
surface; only the GOOGLE_PLAY variant strips.
**Q2 and Q4 were NOT ruled and remain open** — the two `storeBalance*` values are still authored in the
retired `SKR:` form for all builds (now correctly neutralised for Play only), and `offline-storage.json`'s
`skr`/`skrFastTrack` keys were resolved by QUARANTINING the file from Play rather than by a schema ruling.
⛔ §4's proposed item 4 (a define constraint on `DeNelle.Core.asmdef`) was **rejected as unsafe** — it would
delete the entire core assembly from the Play player. See WO-1741 §6: the residual is the NAME
`DeNelle.Core.Web3`, and a rename is the outstanding ruling request.
**Silo:** Play packaging / content exclusion — `Assets/Editor/GooglePlayContentExclusion.cs`, `Assets/_Modules/Web3/Resources/`, `Assets/_Modules/Core/Web3/`
**Opened:** 2026-09-15 (RCA lane)
**Related:** WO-1739 (the reporting defect that hid this), WO-1363/1364 (the sweep + the gate)

> ### ⛔ THE dApp STORE / SOLANA BUILD MUST KEEP ITS WALLET.
> Only the **GOOGLE_PLAY** variant strips. The Seeker/dApp-Store APK is the owner's ACTUAL revenue path
> (two real transactions, SKR and Pi). Nothing in this WO may remove wallet support globally. Every
> mechanism below is already define-derived per artifact (`GooglePlayContentExclusion.ApplyForDefines`);
> keep it that way.

---

## 1. Verdict: the rejection is **CORRECT**, not a false positive

Every token the gate named is a real string genuinely present in the 434 MiB AAB. This was verified by
opening `Builds/Android/EchoesOfElarion-GooglePlay.aab` as a zip and reading the printable runs around
each hit — not inferred from source. The gate is doing exactly its job. **Do not weaken it.**

The hits are **not** one problem. They are four, with very different severities, and they must be ruled
on separately.

## 2. The four leaks, with the evidence

### (A) ⛔ A LIVE JUPITER TOKEN-SWAP UI SHIPS IN THE PLAY AAB — the serious one

AAB entries `base/assets/bin/Data/b16d347601fc44fbbdf1e33d169ab6d6` and
`.../2e41928d86604dd1bd81df2f818d0696` contain, verbatim:

```
JupiterSwapPanel
Powered by Jupiter Aggregator
swap-skr-out
```

and `base/assets/bin/Data/globalgamemanagers` carries `jupiterswappanel` in the Resources **name table**.

Source: **`Assets/_Modules/Web3/Resources/JupiterSwapPanel.uxml`** and
`Assets/_Modules/Web3/Resources/JupiterSwapPanel.uss`.

**Why the isolation gate is blind to it.** `Assets/_Modules/Web3/DeNelle.Web3.asmdef` carries
`!GOOGLE_PLAY`, and `GooglePlayPackagingGate.InspectSourceIsolation` (`:202`) checks exactly that — so the
C# (`JupiterSwapPanelController.cs`, `JupiterSwapService.cs`, `JupiterSwapBootstrap.cs`) is correctly
excluded and `PLAY_SOURCE_ISOLATION_OK` fires. But the module contains a folder **named `Resources`**, and
everything under such a folder is packed into **every** player by construction, reached by name, with no
assembly reference for a define constraint to sever. This is the identical door
`GooglePlayContentExclusion`'s own header documents at `:12-17` — and `PlayExcludedAssetPaths` (`:131-151`)
quarantines `Assets/Resources/SolanaUnitySDK` but **not** `Assets/_Modules/Web3/Resources`.

Severity: this is a **crypto exchange/swap surface**, the category Google Play's financial-products policy
actually targets. It is not an authoring note. The controller is excluded so the panel cannot be opened,
but the asset is in the package and its text is trivially discoverable by an automated review.

**Age — it is not new, and that is the point.** `JupiterSwapPanel.uxml` was added **2026-05-26**
(`5d13d5b3b`, WO-43). It has been in **every** Play AAB ever produced: both
`Builds/Android/store/EchoesOfElarion-GooglePlay-2026.09.07.*.aab` were opened and scanned for this WO and
**both carry `JupiterSwapPanel`, `Powered by Jupiter` and `swap-skr-out`**, along with every other surface
in §2. No AAB in this tree has ever passed the artifact gate. See WO-1739 §3b.

**⚠ THE STRING EXISTS TWICE, AND ONLY ONE COPY IS CLEANED — this is the lane's key implementation note.**
`"Powered by Jupiter Aggregator"` is *both* hard-coded in the `.uxml` **and** a localized entry
`"swap.poweredBy"` in `Assets/{Resources,StreamingAssets}/Data/Canonical/{en,ar,ja,ko,zh-Hans}.json` and
`Assets/Localization/Tables/GameStrings_en.asset`. `GooglePlayLocalizationVariant` **already handles the
localized copy correctly** — it reported `PLAY_LOCALIZATION_VARIANT_OK - transformed 27 source asset(s)` on
the 09-15 run, and **not one locale file appears in the `PLAY_ARTIFACT_DIRTY` list**. That mechanism works
and is the precedent to follow. The leak is the **UXML's own baked-in copy**, which no mechanism touches.
Do not "fix" the localization path — it is already green.

**Evidence bearing on Q3 (live vs dormant) — read it carefully, it cuts toward DORMANT.**
`Assets/Editor/Regression/PanelDoorRegression.cs:32-35` names `JupiterSwapPanelController` in its list of
types **deliberately EXCLUDED** from the door oracle, with the reason *"They are not screens the player
routes to"*. So the panel is **not a routed player door**. That is evidence, not a ruling — the owner still
answers Q3, but nothing found in this lane shows a live route to it.

### (B) Player-facing HUD copy still reads "SKR:" in the Play build

AAB entry `base/assets/bin/Data/fe03eb5a2a52a4441b9d9e88365f81b5` (the Resources copy of
`canon-strings.json`):

```
"storeBalanceBoundIdentity": "SKR: identity bound - authorize",
"storeBalanceUnavailable":   "SKR: unavailable in this build",
```

These are **rendered** strings, not notes.

**Root cause — a one-character vocabulary bug.** `GooglePlayContentExclusion.ContainsForbiddenAuthoringToken`
(`:407-412`) tests for `" skr"` — **with a leading space** — while
`GooglePlayPackagingGate.ForbiddenTokens` (`:49-63`) tests bare `"skr"` under a word-boundary rule. Both
values above **start** with `SKR:`, so the detector never fires, `NeutralizeForbiddenStrings` returns 0 for
them, and the map is never consulted.

The bitter part: `PlayNeutralStringReplacements` (`:200-201`) **already contains the correct neutral copy
for exactly these two keys** —

```csharp
{ "storeBalanceBoundIdentity",  "Balance: identity bound - authorize" },
{ "storeBalanceUnavailable",    "Balance: unavailable in this build" },
```

— and it was never reached. The fix is already authored; only the detector gates it out. (Note the two
values are also the **retired** `SKR: <balance>` form that `canon-strings.json`'s own `_authoringNote`
records the owner rejecting; see §5 Q2.)

### (C) Three token-bearing catalogs are outside the sweep's coverage entirely

`PlayNeutralMirrorPairs` (`:153-164`) is a hand-listed set of four rows. These ship unswept:

| AAB entry | Source | Token | Why it slipped |
|---|---|---|---|
| `base/assets/Data/Canonical/structures-catalog.json` + Resources blob `53a9b82839...` | `structures-catalog.json` | `solana` | **Not in the list at all.** Note added 2026-09-05 by `9a9e65c8a` (WO-1416): *"the game is live on the Solana dApp Store, so renaming it orphans every existing town"* |
| `base/assets/Data/Canonical/ad-placements.json` | `Assets/StreamingAssets/Data/Canonical/ad-placements.json` | `skr` | Row `:163` is **one element** (Resources only). The comment at `:160-161` states *"ad-placements.json has NO StreamingAssets twin (verified 2026-09-04)"* — and the twin was **created on 2026-09-04** by `32af7767c` (WO-1333, em-dash fix). The comment was stale the day it was written. |
| Resources blob `4ddf46b0e4fb...` | `Assets/Resources/Data/Economy/offline-storage.json` | `solana`, `skr` | Outside `Data/Canonical/` entirely. Carries a *"staged local->cloud->Solana save path"* note plus `"skr": 0` and `"skrFastTrack": 0` **object keys** — which `NeutralizeForbiddenStrings` documents (`:350-355`) that it deliberately does not rewrite. |

These are authoring notes and schema keys, not rendered copy. Real text in the artifact; low policy risk.

### (D) The IL2CPP metadata — partly first-party and closable, partly a structural ceiling

`base/assets/bin/Data/Managed/Metadata/global-metadata.dat` and
`globalgamemanagers.assets.split0`, read at source:

**Closable, first-party:**
- `DeNelle.Core.Web3`, `DeNelle.Core.Web3|BackendRequestSigner`, `DeNelle.Core.Web3|IWalletSigner`,
  and the source paths `\Assets\_Modules\Core\Web3\BackendRequestSigner.cs`, `\...\IWalletSigner.cs`.
  These compile into the Play player because they live in **`DeNelle.Core`**, whose asmdef has
  `"defineConstraints": []` (`Assets/_Modules/Core/DeNelle.Core.asmdef:16`). The isolation gate checks
  `Assets/_Modules/Web3/DeNelle.Web3.asmdef` and never looks at `Core/Web3/`. The folder also holds
  **`IJupiterService.cs`**.
- Enum identifiers `SolanaWallet` (`Assets/_Modules/Core/Platform/CurrencySkin.cs:27`) and
  `SolanaDappStore` (`Assets/_Modules/Core/Payments/IPaymentProvider.cs:10`).
- The PlayerPrefs key literal `dotr-arena-skr-balance`
  (`Assets/_Modules/Village/Arena/ArenaWalletService.cs:50`).
- The literal `"... CRYSTALS/SKR/PURCHASED GOODS/EQUIPPED GEAR ..."`
  (`Assets/_Modules/Village/Waves/DefenseReportBuilder.cs:439`).

**The ceiling — and it is NOT wallet functionality.** The overwhelming majority of `solana` hits in
`global-metadata.dat` are the **source-file paths of the vendored UniTask async library**:

```
\Packages\com.solana.unity_sdk\Runtime\Plugins\UniTask\Runtime\AsyncLazy.cs
\Packages\com.solana.unity_sdk\Runtime\Plugins\UniTask\Runtime\Channel.cs
... (dozens more)
```

UniTask is referenced by 16 first-party asmdefs including `DeNelle.Core` and `DeNelle.Village`, and it is
vendored **inside** the Solana SDK package folder — so the token comes from a **directory name**, not from
any crypto code. Removing it needs a `manifest.json` swap + package resolve + full recompile, which cannot
happen inside a build callback. This is the ceiling commit `6979fb961` names in its own title
(*"...and the ceiling neither can cross"*). `8db145c316dd...` is Unity's package-provenance receipt listing
`com.solana.unity_sdk@1.2.9`, the same class of artifact as the `dependencies.pb` already skipped at
`GooglePlayPackagingGate:310-312`.

## 3. The pattern behind (B) and (C): duplicated state

`ContainsForbiddenAuthoringToken` (`:409`) holds
`{ "solana", "jupiter", "$skr", " skr", "usdc", "crypto", "web3", "wallet" }`.
`GooglePlayPackagingGate.ForbiddenTokens` (`:49-63`) holds 28 entries including bare `skr`, the three live
mints, `solflare`, `seed vault`, `mobilewalletadapter`, `blockchain`, `pi network`.

**Two vocabularies for one policy is exactly the duplicated-state failure `CLAUDE.md` §2, §5 and §16 each
describe in their own words** — and WO-1364's own header says the gate's arrays are *"the SINGLE SOURCE OF
TRUTH for this policy"* because *"the two copies had drifted before"*. The sweep is the third copy, and it
drifted. **Do not add a fourth.** Make `ForbiddenTokens` `internal` and have the sweep consume it.

## 4. Proposed work (do NOT start before §5 is ruled)

1. **(A)** Add `Assets/_Modules/Web3/Resources` to `PlayExcludedAssetPaths`. Verify nothing under
   `GOOGLE_PLAY` resolves it by name (the controller is `!GOOGLE_PLAY`, so it should be clean) — and add a
   regression that **fails if any `Assets/_Modules/*/Resources` folder belongs to a `!GOOGLE_PLAY`-constrained
   asmdef and is not quarantined**. That generalises the door instead of patching one instance.
2. **(B)** Delete the `" skr"` entry; have `ContainsForbiddenAuthoringToken` consume
   `GooglePlayPackagingGate.ForbiddenTokens` with the gate's own boundary rule. Confirm the two
   `storeBalance*` values then take their existing mapped replacements.
3. **(C)** Add `structures-catalog.json` (both mirrors) and `offline-storage.json` to the sweep. ⚠ **Check
   the key paths first**: `NeutralizeForbiddenStrings` **throws** `PLAY_NEUTRAL_UNMAPPED_TOKEN` (`:398`) by
   design on a non-`_`-prefixed player-facing key. If the structures-catalog note's key is not
   `_`-prefixed, adding the file will **fail the Play build** until the key is renamed or mapped — that is
   correct behaviour, but it must be done in the same change, not discovered at 2 a.m.
   `offline-storage.json`'s `"skr"` / `"skrFastTrack"` are **object keys**, which the sweep deliberately
   does not rewrite — they need the packs.json treatment (remove the rail key) or a §5 ruling.
4. **(D)** Put `!GOOGLE_PLAY` on the `Core/Web3` sources (or move them to `DeNelle.Web3`); extend
   `InspectSourceIsolation` to check `Assets/_Modules/Core/Web3/` so this cannot recur silently. Rename or
   `#if`-guard the four first-party literals if §5 Q1 says they must go.
5. **Gate maintenance:** consider extending `ShouldSkipProvenanceEntry` to Unity's package-receipt blob,
   and adding a documented, reasoned allowlist entry for the UniTask source paths under
   `Packages/com.solana.unity_sdk/Runtime/Plugins/UniTask/` — **with the reason written in, per the
   existing `FalsePositiveAllowlist` convention**, because that string is a directory name for an async
   library, not a crypto surface.

## 5. ⛔ WHAT THE OWNER MUST DECIDE BEFORE A PLAY AAB CAN SHIP

**Q1 — The UniTask ceiling. This is the one that decides whether a Play AAB is possible at all.**
Even after (A)(B)(C)(D) are fixed, `global-metadata.dat` will still carry `solana` from dozens of vendored
UniTask source paths, and the gate as written will still say `PLAY_ARTIFACT_DIRTY`. Three options:
  - **(i)** Un-vendor UniTask (install `com.cysharp.unitask` properly, drop the Solana SDK from the Play
    manifest) — the clean fix, a real piece of work, and the only one that makes the artifact genuinely
    token-free.
  - **(ii)** Allowlist the UniTask paths in the gate with a written reason. Cheap, honest, and defensible:
    a folder name is not a crypto feature. But it is the gate being told to look away, and this repo has
    been burned by exactly that (WO-1364's weak-list).
  - **(iii)** Accept that no Play AAB ships until (i) happens.
**I have NOT proven what Google's automated review does with these strings, and I am not able to from
here.** The gate is a deliberately conservative self-imposed proxy for Play policy, not a copy of it.

**Q2 — `storeBalanceBoundIdentity` / `storeBalanceUnavailable`.** Their authored values still use the
`SKR: <balance>` form that `canon-strings.json`'s own `_authoringNote` records the owner **retiring** in
favour of the `Balance:` wording. Should these two be fixed **at the source** for *all* builds (Seeker
included) rather than only neutralised for Play? That would close the leak and honour the earlier ruling in
one edit.

**Q3 — The Jupiter swap panel (A).** Is it live product on the Seeker, or dormant? If dormant, deleting
`Assets/_Modules/Web3/Resources/` outright is simpler and safer than quarantining it every build. If live,
quarantine it — and it stays in the dApp Store build untouched.
*Lane evidence, offered without a recommendation:* `PanelDoorRegression.cs:32-35` explicitly excludes
`JupiterSwapPanelController` because *"They are not screens the player routes to"*, which points toward
dormant — but the panel **is** localized into five languages (`swap.poweredBy`), which points toward a
feature that was finished. `Assets/_Modules/Web3/README.md` was **not read in this lane**, and it is the
obvious next place to look before ruling.

**Q4 — `offline-storage.json`'s `"skr"` / `"skrFastTrack"` cost keys.** Remove them from the Play copy
(the packs.json treatment), or rename the schema key for all builds?

## 6. What NOT to touch

- **Do not weaken `GooglePlayPackagingGate`** to make the build pass. It was correct on every hit. Its
  arrays are the single source of truth for this policy.
- **Do not remove wallet support globally.** Every change must be define-derived per artifact.
- **Do not add a fourth token vocabulary.** Consume the gate's.
- Do not touch `google-play-aab-build.ps1` — WO-1739 owns it and is DONE.

---

## RCA 2026-09-15 (chain run 14:14:58)

**Read-only RCA lane. No code, gate, build, git or edit was run. This section is APPENDED; the WO's
`**Status:**` line is deliberately untouched.**

Every claim below is traced to bytes read THIS SESSION out of
`Builds/Android/rejected/EchoesOfElarion-GooglePlay-20260915-141458.REJECTED.aab` (455,022,573 bytes on
disk) and `Builds/aab-build.log`, or to a source file opened at its cited line. Inferences are labelled
**INFERRED** and collected in §8. Method: the gate's matcher (`MatchesTokenInPayload` / `HasPrintableRun`
/ `IsAllowlistedOccurrence`, `GooglePlayPackagingGate.cs:446-514`) was ported to Python verbatim — same
vocabulary, same short-token set, same allowlist, same `MinPrintableRunForShortTokens = 12` — and run
over each named entry **whole-file**, in both the Latin-1 and UTF-16 views, so every occurrence could be
classified rather than merely counted.

> ### ⛔ READ THIS FIRST: TODAY'S REJECTION WAS **PREDICTED IN WRITING BY WO-1741**. IT IS NOT A REGRESSION.
> `WORK_ORDER_1741_*.md` §7 is titled *"⛔ THE NEXT AAB WILL STILL SAY `PLAY_ARTIFACT_DIRTY`. Do not run
> it expecting green."* Its residual table names **five of today's six offenders** in advance:
> `DeNelle.Core.Web3` + the `Core\Web3\*.cs` source paths, `SolanaWallet`/`SolanaDappStore`,
> `dotr-arena-skr-balance`, the `DefenseReportBuilder` `CRYSTALS/SKR/…` literal, and the
> `com.solana.unity_sdk@1.2.9` provenance blob. The sixth — `crypto` — is named in **§8** of the same
> document as the *"Chunk-seam allowlist truncation"* residual.
> **WO-1741 did exactly what it said it would do, and it is not to be re-opened.** Its quarantine and
> sweep are provably working: **not one** of this WO's original §2 offenders (the Jupiter panel,
> `canon-strings` `storeBalance*`, `ad-placements.json`, `offline-storage.json`, `structures-catalog.json`)
> appears in today's `PLAY_ARTIFACT_DIRTY` list. What follows is the residual set 1741 knowingly
> deferred, now measured at the byte level for the first time.

### 1. The Unity build SUCCEEDED. The gate rejected the artifact afterwards. `exit=8` is NOT a compile failure.

| Evidence | Where |
|---|---|
| `error CS` occurrences in the build log: **0** | `grep -c "error CS" Builds/aab-build.log` |
| `Build Finished, Result: Success.` + `DisplayProgressNotification: Build Successful` | `Builds/aab-build.log` (~:38355) |
| `##utp:{"type":"PlayerBuildInfo"…}` records all 13 build steps completing, `"duration":550619` | same region |
| The artifact exists, 455,022,573 bytes, and opens as a valid zip | the quarantined path above |
| `summary.result == BuildResult.Succeeded` was entered — the gate call happens only inside that branch | `Assets/Editor/AndroidBuild.cs:197-199` |
| The gate returned false → `EditorApplication.Exit(1)` | `AndroidBuild.cs:199-203`; the logged stack reads `GooglePlayPackagingGate:AssertBuiltArtifact (at …GooglePlayPackagingGate.cs:348)` ← `AndroidBuild:BuildAndroidArtifact (at AndroidBuild.cs:199)` |
| `[AndroidBuild] SUCCEEDED` never emitted — `Exit(1)` fires before the marker block (`AndroidBuild.cs:209+`) | `grep -n "SUCCEEDED" Builds/aab-build.log` → **no match** |
| `exit=8` is `run-unity-method.ps1`'s own fail-closed code for *"the log carries no success marker"* (`:29`, `:256`); **Unity itself exited 1** | `run-unity-method.ps1` |

**`AAB_BUILD_MARKER_ABSENT … exit=8` and `PLAY_ARTIFACT_REJECTED` are ONE event, not two** — the chain is
reporting the gate rejection through the marker-absence channel. Nothing about the compile or the player
build is wrong.

### 2. The six offenders, named from the bytes

| # | Entry (size) | Token | What the string ACTUALLY is | Verdict |
|---|---|---|---|---|
| 1 | `bin/Data/437e022738512714fb6002270c4a6d2e` (3,648 B) | `solana` | **Unity's `PerformanceTestRunInfo` build receipt** — 1 occurrence, inside `"Dependencies":["com.solana.unity_sdk@1.2.9",…]` | **INERT** — a package-manifest receipt |
| 2 | `Managed/Metadata/global-metadata.dat` (19,922,432 B) | `crypto` | **`system.security.cryptography.hmacsha256`** — the BCL type-name table | **FALSE POSITIVE — the known gate defect (§3)** |
| 3 | same | `solana` | 72 allowlisted UniTask paths + **4 live**: 2 enum identifiers, 2 authoring-note literals | **INERT**; 2 are already owner-ruled |
| 4 | same | `skr` | **13 live authored C# string literals** — log/`FlowTrace` copy, one dev-tool label, one live PlayerPrefs key | **GENUINE literals**, lowest policy risk of the genuine set |
| 5 | same | `web3` | 7 live — the namespace **`DeNelle.Core.Web3`**, its two shipping types, their source paths | **GENUINE inert NAME; the open rename ruling** |
| 6 | `bin/Data/globalgamemanagers.assets.split0` (1,048,576 B) | `web3` | 3 live — the serialized **script-type table**: `IWalletSigner`, `IJupiterService`, `BackendRequestSigner`, each followed by namespace `DeNelle.Core.Web3` and assembly `DeNelle.Core` | **INERT NAMES**; one is not even in the player (§6) |

#### 2.1 — `437e0227…` is a build receipt, not content

Header: `6000.4.8f1` … `PerformanceTestRunInfo` … `{"TestSuite":"","Date":0,"Player":{…},"Dependencies":["com.solana.unity_sdk@1.2.9","com.unity.2d.sprite@1.0.0",…]`.
Written by **`"com.unity.test-framework.performance": "3.4.0"`** (`Packages/manifest.json:33`). This is
the **same class of artifact as `BUNDLE-METADATA/com.unity/dependencies.pb`, which the gate already skips
by name** — at `GooglePlayPackagingGate.cs:388-390`. *(WO-1741 §7 cites that skip as `:310-312`; the file
has moved since. Cite `:388-390`.)* The only reason this blob is not skipped too is that its name is a
**content hash that changes every build** — WO-1740 §2(D) saw the same receipt as `8db145c316dd…`.
**`grep -rn "Unity.PerformanceTesting" Assets` returns nothing** and no `.asmdef` references it: the
package is unused by first-party code.

#### 2.2 — `crypto` has **ZERO** live occurrences. Proven exhaustively.

119 occurrences exist in `global-metadata.dat`. Under the gate's own rule **every one is already
disqualified**: 89 suppressed by the existing `"cryptograph"` allowlist entry
(`GooglePlayPackagingGate.cs:88`), 30 failing the leading word-boundary test (`:457-459`). **Live: 0.**

The UTF-16 view was checked at **both byte parities** across the whole file:
`crypto: 0, web3: 0, skr: 0, solana: 0` in both. Latin-1 is therefore the only view that can produce a
hit, and Latin-1 whole-file yields no live `crypto`. **No whole-file occurrence exists that the gate
could legitimately have fired on.** That proof by elimination is what makes §3 a certainty.

#### 2.3 — `solana`: 72 allowlisted (WO-1741 working), 4 live, 2 already ruled

The **WO-1741 UniTask allowlist demonstrably ran**: 72 of 76 `solana` occurrences are suppressed by
`com.solana.unity_sdk\runtime\plugins\unitask`. *(§11B provenance: the gate source was committed at
`1cc0c5059` **11:52:49**, mtime **11:15:40**, both before `AAB_START 13:58:03` — this build compiled the
allowlist, so the `solana` line fired on 4, not 76.)*

| Offset | String | Source |
|---|---|---|
| 928,532 | `…enabled manually by LevelPlay support (the Solana dApp Store has no https listing URL for auto-verification).` | `Assets/_Modules/Village/Monetization/Providers/LevelPlayInitializer.cs:226` |
| 1,671,082 | `…the game is live on the Solana dApp Store, so renaming it orphans every existing town.` | ⚠ **`Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs:107`** — see §5 |
| 2,701,514 | enum identifier `SolanaWallet` (`SkinAuthMode`) | `Assets/_Modules/Core/Platform/CurrencySkin.cs:27` |
| 2,706,707 | enum identifier `SolanaDappStore` (`PaymentChannel`) | `Assets/_Modules/Core/Payments/IPaymentProvider.cs:10` |

> ### ⛔ THE LAST TWO ARE **ALREADY OWNER-RULED RESIDUALS**. DO NOT RE-OPEN OR RENAME THEM.
> `Assets/Editor/Regression/PlayMetadataIdentifierRegression.cs:41-51` records the WO-1377 ruling of
> 2026-09-09 in its own header, under *"RESIDUALS, ACCEPTED BY THE OWNER, DELIBERATELY NOT PINNED HERE"*:
> - `PaymentChannel.SolanaDappStore` — a live, un-`#if`'d switch case in `DeNelle.Core` at
>   `CurrencySkinResolver.ResolveWagerCurrency`; the owner's Arena ruling is ONE code path with the
>   currency injected per channel, **explicitly NOT** a `#if GOOGLE_PLAY` inside Arena. Compiling the
>   member out **would break that switch on Play**.
> - `SkinAuthMode.SolanaWallet` — **name-bound to shipped canonical data** (`skin.json`
>   `"authMode": "SolanaWallet"`) via `CurrencySkinResolver.ParseAuth`.
>
> Closing line: *"Neither is persisted in a save; neither may be RENAMED or REORDERED regardless."*
> Their only lawful disposition is **a narrow, reasoned gate allowlist entry citing WO-1377** (§7 item 7).
> They are not a leak to fix; they are a decision already made that the artifact gate was never told about.

#### 2.4 — `skr`: 13 live, all genuine authored C# literals

53 occurrences: 26 fail the leading boundary, 12 the trailing boundary, 2 the printable-run rule,
**13 live**. Every one is a string a human wrote.

- **Log / `FlowTrace` copy (6)** — `native SKR stake read completed; discovery entitlement line refreshed.`
  (1,754,275), `native SKR weekly re-roll CONSUMED: {0}/{1} used.` (1,754,345), `native SKR weekly re-roll
  period advanced to {0}; usage reset.` (1,754,394), `…re-polish started after a native SKR weekly bonus
  check…` (1,826,556), `…re-polish used the verified native SKR weekly bonus attempt.` (1,826,689).
  Owning files (located by literal, **not** by offset): `Core/Platform/StakeRewardsResolver.cs`,
  `Core/Catalog/PolishBonusProvider.cs`, `Core/UI/Mvvm/StakeRewardsVM.cs`,
  `Village/Crafting/JewelPolishService.cs`, `Village/Crafting/JewelerDiscoveryFtue.cs` — all in
  unconstrained assemblies, so they compile into the Play player. *(The `DeNelle.Wallet` copies in
  `NativeSkrStakeQuery.cs` / `HeartboundStatusClient.cs` are correctly excluded — that asmdef is
  `!GOOGLE_PLAY`.)*
- **Report body text (3) — ALL THREE IN ONE FILE, `Assets/_Modules/Village/Waves/DefenseReportBuilder.cs`:**
  - `:439` — `…CRYSTALS/SKR/PURCHASED GOODS/EQUIPPED GEAR ARE UNTOUCHABLE and have no expression here.`
    (offset 460,284) — a **`FlowTrace.Step` argument** (`:435-440`), not rendered copy.
  - `:514` — `…untouchable absolutely). Zeroed before the debit.` (offset 774,113).
  - `:586` — `crystals/SKR/purchased goods/equipped gear untouched).` (offset 1,565,426).
  *(WO-1741 §7 listed only `:439`. There are three.)*
- **A UI label (1)** — `Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:629`,
  `var cost = new Label($"{_costSkr:F0} SKR");` (offset 2,043,262). This one **renders** — and the bound
  on its severity is written in the code: `Assets/_Modules/Village/BuildMode/BuildModeController.cs:129-131`
  says *"The old UIToolkit 3-axis TowerPlacementRotateMenu is now the editor/dev OFFSET tool — no longer
  called from placement."* Its only live callers are `HUD/AdminOverlay.cs:481-498` (by reflection) and
  `DevTools/AutoPilotDriver.cs:1946`. **Not a routed player door** — but the string is in the artifact.
- **A live PlayerPrefs key (2)** — `dotr-arena-skr-balance` at 1,585,378 (the literal) and 10,606,706
  (`ArenaWallet,dotr-arena-skr-balance`, a serialized default).
  ⛔ `Assets/_Modules/Village/Arena/ArenaWalletService.cs:48-50`: *"UNCHANGED on purpose — a renamed key
  would read as a fresh 500 seed (WO-1366 section 4)."* **This key may not be renamed.**
- **A validation message (1)** — `…is not an allow-listed skin (pi|skr|wallet) — ignored.`,
  `Assets/_Modules/Core/Platform/CurrencySkinResolver.cs:436` (offset 270,248).
- **UNPROVEN SOURCE (1)** — the bare token at **239,196**, reading `…one SpawnWave call). SKR, age=
  STATE-CHANGE…`. `grep -rn "SKR, age=" Assets --include=*.cs` returns **nothing**. IL2CPP packs the
  literal table contiguously, so this window is almost certainly **two adjacent unrelated literals**
  (`SKR` and `, age=`) — i.e. a standalone `"SKR"` literal whose owner **I did not identify**.

#### 2.5 — `web3`: the namespace `DeNelle.Core.Web3`, and nothing else

All 7 live metadata occurrences are the same three things: `DeNelle.Core.Web3|BackendRequestSigner`,
`DeNelle.Core.Web3|IWalletSigner`, `DeNelle.Core.Web3.BackendRequestSigner|SessionResponse`,
`…|NonceResponse` (10,831,747 / 10,831,790 / 10,831,849 / 10,831,906); a member-name-table entry at
2,641,174; and the IL2CPP **source paths** `\Assets\_Modules\Core\Web3\BackendRequestSigner.cs` and
`\Assets\_Modules\Core\Web3\IWalletSigner.cs` at 10,852,131.

Both files are **unguarded** — `Core/Web3/BackendRequestSigner.cs:43` and `IWalletSigner.cs:36` open
`namespace DeNelle.Core.Web3` with no `#if !GOOGLE_PLAY` around the type. **And they must not be:**
`BackendRequestSigner.cs:117` states *"the only intended caller is the GOOGLE_PLAY identity assembly
after /api/auth/google-session"*, with an `#if GOOGLE_PLAY` arm at `:205-212`. WO-1741 §6 reached the
same conclusion independently: *"Compiling either out would break Play saves … the residual leak is the
NAME, not the code."* **The only fix is the namespace rename — the ruling WO-1741 §6 left open.**

### 3. The `crypto` hit is the chunk-seam defect — **already recorded by WO-1741 §8**, now measured

⚠ **This is NOT a new discovery, and it must not be reported as one.** `WORK_ORDER_1741_*.md` §8 lists it
under *"Residuals recorded, deliberately not fixed"*:

> *"**Chunk-seam allowlist truncation.** `ScanStream` retains ~264 bytes across 64 KB boundaries. A hit
> whose allow phrase straddles a chunk end is recorded before the next chunk sees full context.
> **Pre-existing for every allowlist entry**, but dozens of UniTask paths raise the odds. If the next AAB
> reports one stray UniTask-path `solana`, this is why — not a scope error in the entry."*

**What this lane adds is the measurement, and one correction to that prediction:** the seam fired, but on
`cryptograph`, not on a UniTask path.

`ScanStream` (`GooglePlayPackagingGate.cs:392-420`) reads the entry in 64 KiB chunks and calls
`MatchesTokenInPayload` on each chunk **in isolation**, carrying a 264-byte `overlap` *forward* (`:417-418`)
so a straddling token is never MISSED. But both the allowlist check (`IsAllowlistedOccurrence`, `:497-514`,
window `hit ± allow.Length`) and the printable-run check (`:479-488`) read context on **both sides** of the
hit, and a chunk is a hard edge on both. There are **three** distinct vectors:

1. **Tail truncation (measured today).** A hit near the chunk end loses its RIGHT context, so the
   suppressing phrase is cut and the hit fires LIVE. `hits.Add` has already run by the time the next
   chunk sees the same bytes with full context.
2. **Head truncation.** The retained 264 bytes are **re-scanned** at `buffer[0..264)` in chunk N+1. A hit
   there whose phrase starts to its LEFT (`solana` sits at offset 4 of `com.solana…`; `crypto` at offset
   6 of `javax.crypto`) has that start cut off and fires LIVE — *even though chunk N suppressed it
   correctly.* A tail-only fix does not close this.
3. **False trailing boundary.** `MatchesTokenInPayload:464` treats `after == text.Length` as a word
   boundary, so a **short** token ending exactly at a chunk tail passes the trailing-boundary test
   regardless of the next byte.

**Measured, today, in this artifact:**
- `crypto` at offset **1,900,536**; the suppressing phrase `cryptograph` runs to **1,900,547**; the 30th
  chunk ends at 65,536 × 29 = **1,900,544**. The token (ending 1,900,542) is fully inside the chunk; the
  phrase is **cut 3 bytes short** → reported LIVE. The string is
  `…system.security.cryptography.hmacsha1system.security.cryptography.hmacsha256…`.
- Exactly **one** of the 89 allowlisted `crypto` occurrences crosses a 64 KiB boundary this way, and it is
  that one. *(Right-side crossings only; left-side/head crossings were not enumerated — the mechanism and
  the verdict are unchanged, and `hits.Distinct()` collapses duplicates either way.)*
- **UniTask exposure this run: 0 of 72.** The phrase is 44 chars with `solana` at offset 4, giving a
  ~38-byte danger window per occurrence (34 right + 4 left). The allowlist held this build.

Because bundle content is hashed, offsets move every content build, so this presents as **flakiness** —
the same tree passing and failing on consecutive runs. That is why it is item 1 below.
**Fixing it is a correctness fix: it removes zero vocabulary and weakens nothing.**

### 4. Q4 — exactly how the gate tokenises, and the allowlist mechanism

Answering the brief's question 4 directly, with lines:

- **One vocabulary for every entry.** `TokensForEntry` (`:378-382`) ignores the entry name and returns
  `ForbiddenTokens` (`:49-63`, 28 entries) for all of them. The WO-1364 tier split is gone.
- **Matching is substring and case-insensitive.** `MatchesTokenInPayload:453` —
  `text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase)`. Not regex, not whole-word by default.
- **Leading boundary, conditionally.** `:457-459` — required **only when `token[0]` is alphanumeric**; a
  hit preceded by a letter or digit is skipped. *(This is why `jupiter` never fired: every occurrence is
  `IJupiterService`, and the leading `I` disqualifies it. Luck, not design.)*
- **Short tokens carry two extra rules, in binary entries only.** `ShortTokensRequiringTextContext`
  (`:75-78`) = `skr`, `$skr`, `usdc`, `web3`. When `readableEntry` is false, `:461-466` additionally
  requires a trailing word boundary **and** `HasPrintableRun(…, MinPrintableRunForShortTokens)` — a
  contiguous printable-ASCII run of ≥ **12** (`:231`, `:479-490`).
- **Readable vs binary.** `IsUserFacingContentEntry` (`:363-372`) — true for anything under
  `base/assets/data/canonical/` or ending `.json/.txt/.html/.xml/.uxml`. **All six of today's entries are
  binary**, so the short-token rules were in force for `skr` and `web3`.
- **Two views per chunk.** `ScanStream:407-414` builds a Latin-1 view (`:427-432`, byte-exact — explicitly
  *not* `Encoding.ASCII`, which would map every high byte to `?`) and a UTF-16 view, and matches both.
- **Allowlist = containment, not exclusion.** `IsAllowlistedOccurrence` (`:497-514`), called at `:468`,
  drops a hit **only when the matched occurrence lies wholly INSIDE** a longer phrase from
  `FalsePositiveAllowlist` (`:83-173`) — `found <= hit && found + allow.Length >= hit + token.Length`
  (`:507`). A phrase that does not itself contain the token is skipped outright (`:501`), so an entry can
  never silently disable an unrelated token.
- **The WO-1741 entry shape** (`:171-172`) is the precedent to copy: two literals
  (`com.solana.unity_sdk/runtime/plugins/unitask` and its backslash twin, because metadata carries
  Windows-built paths), preceded by ~40 lines of written reasoning (`:134-170`) covering *what UniTask is*,
  *why a vendored path is not a policy surface*, *why it is scoped to `.../Plugins/UniTask` and not to the
  package* (the siblings `SolanaWalletAdapterWebGL/` and `Web3AuthSDK/` are real crypto surfaces and must
  keep firing), and an explicit check that the phrase contains exactly one forbidden token.
- **Authoring-only extension.** `AuthoringOnlyTokens` (`:182-185`) = bare `wallet`, applied **only** by
  `ContainsForbiddenAuthoringToken` (`:203-212`, the catalog sweep's single entry point), never to artifact
  entries — deliberately, because `wallet` is too common in engine binaries to be evidence.
- **Two skips.** `ShouldSkipProvenanceEntry` (`:388-390`) drops `BUNDLE-METADATA/com.unity/dependencies.pb`
  by exact name; `IsSignatureDigestEntry` (`:221-227`) strips **short** tokens from `META-INF/*.SF` and
  `MANIFEST.MF`, because a base64 digest is a long printable run that defeats the run rule.
- ⛔ **The arrays are parsed out of this source file at run time** by
  `tools/android/assert-google-play-aab-clean.ps1` (header `:36-40`), using
  `[regex]::Matches($text, '"([^"]*)"')` (`:56`). **Keep every entry a simple literal with no embedded
  quotes or escapes**, or that parser silently produces a different vocabulary than the C# gate — see the
  warning in item 7.

### 5. New finding: `CatalogFallbackData.g.cs` is a THIRD copy of the catalog, invisible to the sweep

`Builds/aab-build.log:9164-9165` shows the Play-neutral sweep working:
`PLAY_NEUTRAL_TOKEN_SWEEP - structures-catalog.json: neutralised 1 token-bearing string(s)` — and no
`Data/Canonical/structures-catalog.json` entry appears in the `PLAY_ARTIFACT_DIRTY` list.

The identical `_authoringNote` shipped anyway as a **C# string literal**, from
`Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs:107` — a **git-tracked, generated**
file embedding the whole catalog JSON, authoring notes included. Its generator
(`Assets/Editor/CatalogFallbackGenerator.cs:82`) is a **`[MenuItem]` only**, so it is hand-run and
committed. *(How I know the exclusion path never regenerates it: `grep -rn "CatalogFallbackGenerator"
Assets/Editor` returns only the generator itself and four regression suites — no call site in
`GooglePlayContentExclusion`. I did not open `GooglePlayContentExclusion.cs` in this lane.)*
The sweep rewrites copy 1 (Resources) and copy 2 (StreamingAssets); **copy 3 is compiled into the
player.** Same **duplicated-state** failure `CLAUDE.md` §2/§5/§16 describe and WO-1740 §3 named for the
token vocabularies.

### 6. Footnote: WO-1377's compile-out of `IJupiterService` WORKED

`IJupiterService` appears in `globalgamemanagers.assets.split0`'s script-type table but is **absent from
`global-metadata.dat` entirely** (it is not among the 7 `web3` hits). `Core/Web3/IJupiterService.cs`
carries WO-1377's type-level `#if !GOOGLE_PLAY` around the whole namespace body (WO-1741 §6 cites `:45`).
So the type is **not in the Play player**; `split0` carries a serialized type-table entry naming a type
the player does not contain. That is why offender #6 is the most inert of the six.

### 7. FIX PLAN — ranked, single numbering

> Ranked by *what unblocks a Play AAB soonest at least risk*. Class tags: **(a)** genuine removal,
> **(b)** inert → narrow reasoned allowlist, **(c)** needs an owner ruling, **(g)** gate maintenance.
> **Nothing below was applied. This pass ends with the plan.**

**1. (a) ⛔ FIRST, BEFORE ANY LEAK: close the `ScanStream` chunk seam — all three vectors of §3.**
*File:* `Assets/Editor/Regression/GooglePlayPackagingGate.cs:392-420` (+ `:446-473`).
*Change:* add `internal static readonly int MaxAllowlistPhraseLength = FalsePositiveAllowlist.Max(a => a.Length);`
(currently 44) and give `MatchesTokenInPayload` a **scan window** rather than the whole chunk string:
- **Right bound** — on a non-final chunk, accept a hit only when `p + token.Length + MaxAllowlistPhraseLength <= count`;
  defer the rest to the next chunk.
- **Left bound** — on chunk N+1, examine only `p >= retained - token.Length - MaxAllowlistPhraseLength`,
  skipping the head that chunk N already judged with full context. Deferred hits then sit at buffer
  position ≥ 264 − 6 − 44 = **214**, so left context is always sufficient.
- **Trailing-boundary fix** — `:464`'s `after == text.Length` must count as "unknown", not "boundary",
  on a non-final chunk; the deferral above makes this automatic once the right bound is in.
- After `read <= 0`, run **one final unbounded pass** over the retained buffer so the file's true tail is
  still scanned. The existing 264-byte overlap (≥ 44 + 44) guarantees nothing is missed.
*Regression to add:* synthesize a stream in which `system.security.cryptography.hmacsha256` and a
`…com.solana.unity_sdk\Runtime\Plugins\UniTask\Runtime\AsyncLazy.cs` path are each cut at **every** split
offset `0..phraseLen` relative to a 65536 boundary, and assert **CLEAN** at all of them; then a case where
a **bare live `solana`** straddles the same boundary and assert **DIRTY**, proving the fix does not weaken
the gate. *This alone removes offender #2.*

**2. (a) Drop `com.unity.test-framework.performance` from the shipping manifest.**
*File:* `Packages/manifest.json:33`. *Safety evidence:* `grep -rn "Unity.PerformanceTesting" Assets`
returns nothing; no `.asmdef` references it. A performance **test** package writing a dependency receipt
into `bin/Data` of a **shipping** player is wrong for every variant, not just Play. Check no CI job
invokes it before removing. *Removes offender #1 at the root, with no allowlist.* Fallback if the package
must stay: item 8.

**3. (a) Reword the `SKR` log/`FlowTrace` literals to a rail-neutral word** (`native stake`).
*Files:* `Core/Platform/StakeRewardsResolver.cs`, `Core/Catalog/PolishBonusProvider.cs`,
`Core/UI/Mvvm/StakeRewardsVM.cs`, `Village/Crafting/JewelPolishService.cs`,
`Village/Crafting/JewelerDiscoveryFtue.cs`, `Village/Waves/DefenseReportBuilder.cs:439,514,586`,
`Core/Platform/CurrencySkinResolver.cs:436`.
⛔ **Reword — do NOT delete or `#if` these out.** `CLAUDE.md` §12 forbids stripping instrumentation; a
removed `FlowTrace` line turns a logged failure into a silent one. Rewording keeps the trace and removes
the token.

**4. (a) `TowerPlacementRotateMenu.cs:629` — replace the rendered `"{0:F0} SKR"` label.** It is a
dev/editor tool (`BuildModeController.cs:129-131`), so a neutral word costs nothing; a `#if GOOGLE_PLAY`
split is the alternative. **The only rendered token of the six.**

**5. (a) `CatalogFallbackData.g.cs` — fix the GENERATOR, not the file.**
*File:* `Assets/Editor/CatalogFallbackGenerator.cs`. *Change:* strip every `_`-prefixed key
(`_authoringNote`, `_note`, `_heightNote`, …) while emitting — authoring notes have no business in a
runtime fallback blob. This closes the door for **every future note**, not just WO-1416's. Re-run the
`[MenuItem]` and commit the regenerated `.g.cs` in the same change. Coordinate with
`Assets/Editor/Regression/JsonMirrorLiteralRegression.cs:89,313,407`, which pins the generator ↔ `.g.cs`
relationship. *Removes 1 of the 4 live `solana` hits.*

**6. (a) `LevelPlayInitializer.cs:226` — reword the "Solana dApp Store" authoring literal** to
`the dApp Store`. The token is `solana`; `dapp store` is not in the vocabulary. One word, no ruling.

**7. (b) Allowlist `SolanaDappStore` + `SolanaWallet`, citing WO-1377.**
*File:* `GooglePlayPackagingGate.cs`, `FalsePositiveAllowlist`. *Reason to write in:* the owner ruled both
ACCEPTED residuals on 2026-09-09 (`PlayMetadataIdentifierRegression.cs:41-51`) — `SolanaDappStore` is a
live un-`#if`'d switch case whose removal breaks the Arena currency path on Play; `SolanaWallet` is
name-bound to shipped `skin.json` data; **neither may be renamed or reordered.** A decision the owner has
already made is the strongest justification an allowlist entry can carry, and the gate is the one place
that was never told.
⚠ **SCOPE MEASUREMENT REQUIRED BEFORE WRITING — do not write this entry blind.** A bare `"solanawallet"`
phrase would also suppress `SolanaWalletProvider` and **`SolanaWalletAdapterWebGL`**, the latter a real
crypto surface the gate's own comment (`:157-164`) says must keep firing. And a **fully-qualified dotted
phrase will not work**: the windows at 2,701,514 / 2,706,707 show these are **NUL-packed standalone
name-table entries** (`PiSdk`·`SolanaWallet`·`SkinIdentityKeyKind`; the dots in §2.3 are my printable
substitution), so `DeNelle.Core.Platform.SkinAuthMode.SolanaWallet` never occurs as a contiguous string.
The precise scope is a **NUL-bounded phrase** (`"\0solanawallet\0"` — Latin-1 preserves char 0 and
`IndexOf` matches it) — **but** the PS1 mirror parses these arrays with `'"([^"]*)"'` (§4, `:56`) and
would read `\0` as two literal characters, giving the two scanners **different vocabularies**. Verify
against `tools/android/assert-google-play-aab-clean.ps1` first; if it cannot carry the escape, this
becomes a (c) item.

**8. (b) Fallback only, if the perf-test package must stay: allowlist `"com.solana.unity_sdk@"`.**
*Reason to write in:* the trailing `@` binds it to the **package-version receipt form**
(`com.solana.unity_sdk@1.2.9`) — a resolved-manifest listing, the same class as
`BUNDLE-METADATA/com.unity/dependencies.pb` already skipped at `:388-390`. It cannot mask `Solana.Unity.`
(next char is `_`) and cannot suppress a code path. **Prefer item 2.**

**9. (c) ⛔ The `web3` namespace — the one item with no lawful workaround.**
The token is `DeNelle.Core.Web3`, carrying `BackendRequestSigner` and `IWalletSigner`, and
`BackendRequestSigner` is **required by the GOOGLE_PLAY identity/save path** (`:117`, `:205-212`;
WO-1741 §6 concurs). It cannot be compiled out, excluded, or quarantined.
*The ask:* rename the namespace and move the folder together — the IL2CPP **source paths** are in the
metadata too, so `Assets/_Modules/Core/Web3/` must move with it. WO-1741 §6 proposes
**`DeNelle.Core.Backend` / `Assets/_Modules/Core/Backend/`** and warns it touches `CoreServices`,
`GameStateService` and at least five regression suites that pin the path string. `IJupiterService.cs`
stays behind or moves to `DeNelle.Web3` (already `!GOOGLE_PLAY`). **This is WO-1741 §6's open ruling;
today's build is the evidence nothing else will close it.** *Also to decide:* whether `IWalletSigner` is
renamed at the same time — it does not fire today (bare `wallet` is `AuthoringOnlyTokens`, `:182-185`,
deliberately not applied to artifact entries) but is one vocabulary change from doing so.

**10. (c) `dotr-arena-skr-balance`.** ⛔ **A rename is forbidden** — `ArenaWalletService.cs:48-50` records
that a renamed key reads as a fresh 500 seed (WO-1366 §4). Options: a `#if GOOGLE_PLAY` split of the key
literal (neutral on Play, live on Seeker — acceptable only if a Play player's arena balance need not match
a Seeker player's), or a read-migration. **Owner's call** — it is a live save key on the revenue artifact.
*(`ArenaWalletService` describes this as a devnet **stub** balance, which may make the split trivial; I
have not proven the stub is the only writer.)*

**11. (c) The unidentified bare `"SKR"` literal** at offset 239,196 (§2.4). Less a ruling than unfinished
work: **its source was not found.** It must be traced before anyone claims `skr` is closed — a rewording
pass driven by the file list in item 3 would miss it and leave the gate red for a reason nobody can name.

**12. (g) Make `PLAY_ARTIFACT_DIRTY` self-diagnosing.** The line names entry + token and nothing else, so
establishing *what actually fired* required re-implementing the matcher in Python. *File:*
`GooglePlayPackagingGate.cs:339,414`. *Change:* carry the first hit's **byte offset** and a ~40-character
printable window into the hit string. With that in place, §2.2 and §3 would have been a single read of
the log. This is the WO-1739 class of fix.

**13. (g) `aab-status.txt` ordering (§1).** `AAB_BUILD_UNPROVEN … no '[AndroidBuild] SUCCEEDED' marker`
prints **above** `AAB_REJECTED`, so a gate rejection reads as a build failure. When the gate rejects, the
status file should say so first and present marker-absence as the *consequence*. Small — and it cost this
lane its first ten minutes. ⚠ Check WO-1739's scope first: this WO §6 says do not touch
`google-play-aab-build.ps1`.

**Process note for the lead:** items 1–13 are a work order's worth of scope, not an RCA appendix. A new
WO must be minted from the `CLI_LANES_WO_NUMBERS.md` banner (§2 numbering authority) — **this lane did not
mint one.**

### 8. THE CLAIM — proven by bytes vs. inferred

**Proven by bytes read this session:**
- The Unity build succeeded (0 `error CS`, `Result: Success.`, 455,022,573-byte valid zip,
  `summary.result == Succeeded` branch entered per the logged stack). `exit=8` is
  `run-unity-method.ps1`'s marker-absence code; Unity exited 1 from `AndroidBuild.cs:203`.
- `crypto` has **zero** live occurrences whole-file, in Latin-1 and in UTF-16 at both parities. The string
  is `system.security.cryptography.hmacsha256`.
- `437e0227…` is Unity's `PerformanceTestRunInfo` receipt; its single `solana` sits inside
  `"com.solana.unity_sdk@1.2.9"`. `com.unity.test-framework.performance` is at `manifest.json:33` and is
  referenced by no first-party `.cs` or `.asmdef`.
- `solana` in metadata: 76 occurrences, 72 suppressed by the WO-1741 UniTask allowlist, 4 live, each
  located to a named source line. The gate binary compiled that allowlist (commit `1cc0c5059` 11:52:49,
  mtime 11:15:40, both < `AAB_START` 13:58:03).
- `skr`: 13 live literals. **12 of 13 located** — 6 to owning FILES by literal text, 6 to exact
  file:line (`DefenseReportBuilder.cs:439,514,586`; `TowerPlacementRotateMenu.cs:629`;
  `ArenaWalletService.cs:50`; `CurrencySkinResolver.cs:436`). **The 13th (offset 239,196) is unlocated.**
- `web3`: 7 live in metadata + 3 in `split0`, all the `DeNelle.Core.Web3` namespace, its two shipping
  types and their source paths. `BackendRequestSigner.cs`/`IWalletSigner.cs` are unguarded;
  `IJupiterService` is absent from metadata.
- WO-1377's ruling text on `SolanaDappStore` / `SolanaWallet`, quoted from
  `PlayMetadataIdentifierRegression.cs:41-51`; WO-1741 §6, §7 and §8 quoted from that file.
- `CatalogFallbackData.g.cs` is git-tracked; its generator is a `[MenuItem]` only and has no call site in
  `Assets/Editor` outside regressions.
- The gate's tokenising rules in §4, each read at the cited line of `GooglePlayPackagingGate.cs`, and the
  PS1 mirror's `'"([^"]*)"'` array parser at `assert-google-play-aab-clean.ps1:56`.

**INFERRED — hypotheses, not measurements:**
- That the gate's chunk boundaries fell at exact 64 KiB multiples. `DeflateStream.Read` may return short
  reads, which would move every boundary. **This does not affect the verdict** — the seam is the only
  possible mechanism given zero whole-file live occurrences — but offset 1,900,544 is the best-fitting
  candidate, not a measured boundary. Proving it exactly needs the gate instrumented to log chunk offsets.
  Left-side/head crossings were **not enumerated**; only right-side ones were.
- The ~38-byte-per-occurrence UniTask danger window is arithmetic over the 72 measured offsets, not an
  observation. **Measured today: 0 of 72 straddle a boundary.**
- That `"SKR, age="` is two adjacent literals rather than one string. Consistent with how IL2CPP packs the
  literal table, but the source was not identified (item 11).
- That the six `native SKR` literals are the ones in those five files: they were located by **literal
  text grep**, not by mapping each metadata offset to a file. A reword pass should re-grep, not trust the
  offsets here.
- Whether a NUL-bounded allowlist phrase survives the PS1 mirror parser (item 7). **Not tested.**
- **What Google's automated review actually does with any of these strings.** As WO-1740 §5 recorded, the
  gate is a deliberately conservative self-imposed proxy for Play policy, not a copy of it. Nothing here
  changes that, and it cannot be proven from this machine.

**Not attempted (read-only lane):** no code edit, no gate run, no build, no commit, no WO minted.
