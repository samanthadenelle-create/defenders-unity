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
