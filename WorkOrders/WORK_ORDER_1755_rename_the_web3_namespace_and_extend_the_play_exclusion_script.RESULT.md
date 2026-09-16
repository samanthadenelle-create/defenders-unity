# WORK ORDER 1755 — RESULT

**Status of this lane:** IMPLEMENTED, NOT YET GATED. This is a **CLAIM**, not a fact.
**Lane:** edit-only. **No Unity, no gate, no build, no bake, no git command that writes.** The lead holds
the single Unity lock and is the sole committer.
**Date:** 2026-09-15.

> ⛔ **THE LEAD MUST RUN ONE UNITY STEP *BEFORE* THE REGRESSION SUITE, OR IT WILL GO RED FOR THE
> RIGHT REASON.** See §4. The generated catalog blob in the tree is deliberately stale as of this
> hand-back.

---

## 1. PART 1 — the namespace rename

### 1.1 Scope re-verified at source before starting (not taken from the ticket)

| Claim | Command run this session | Result |
|---|---|---|
| declarations of `namespace DeNelle.Core.Web3` | `grep -rn "DeNelle\.Core\.Web3" --include=*.cs Assets/` | **3** — `BackendRequestSigner.cs:43`, `IJupiterService.cs:46`, `IWalletSigner.cs:36` |
| files carrying the string at all | same | **27 files, 34 lines.** ⚠ The ticket said "3 declarations, **27 referencing**". Measured, it is **27 TOTAL** — 3 declaring + 24 referencing. The number moved; recording it rather than repeating the ticket's |
| owning assembly | `ls Assets/_Modules/Core/DeNelle.Core.asmdef` present; no asmdef under `Core/Web3/` | **`DeNelle.Core`** — there is no `Web3` assembly to rename, as the ticket says |
| `backend` in the gate vocabulary? | `GooglePlayPackagingGate.cs:49-63` read | **NOT present.** `DeNelle.Core.Backend` is a lawful name |
| pre-existing `DeNelle.Core.Backend` hits | `grep -rn "DeNelle\.Core\.Backend" --include=*.cs Assets/` | **0** — no collision |

### 1.2 ⚠ I MOVED THE FOLDER, AND THE BRIEF SAID IT COULD STAY. HERE IS THE SENTENCE.

The brief's premise — *"folder names never reach the binary"* — is **contradicted by the RCA the same
brief told me to read.** WO-1740 RCA **§2.5**, measured out of the rejected AAB's
`global-metadata.dat`, lists among the 7 live `web3` hits:

> *"…and the IL2CPP **source paths** `\Assets\_Modules\Core\Web3\BackendRequestSigner.cs` and
> `\Assets\_Modules\Core\Web3\IWalletSigner.cs` at 10,852,131."*

`Web3` there sits between two `\` characters (both word boundaries, `GooglePlayPackagingGate.cs:457-466`)
inside a long printable ASCII run, so it satisfies the short-token rule and **fires live**. Leaving the
folder named `Web3` would have left that hit standing and made **acceptance criteria 1 and 3
unreachable** — discovered only after a ~550 s AAB build plus a gate cycle. WO §1.2 permits the move
explicitly *"if you move them anyway, say why"*; the byte offset above is the why.

The brief's second premise — that moving churns `.meta` guids — is also not so: a guid lives **inside**
the `.meta`, so moving each `.cs` **with** its `.meta` preserves it. Verified after the move:
`Assets/_Modules/Core/Backend.meta` still reads `guid: 681d44b47fa74459a24ecaa926155a3a`, the folder
meta's original guid, and all three `.cs.meta` files moved beside their `.cs`.

⚠ **I did not move `Assets/_Modules/Web3/`.** That is the separate `DeNelle.Web3` assembly, which
carries `!GOOGLE_PLAY` (`DeNelle.Web3.asmdef:17`) and is out of this silo.

### 1.3 The move, as the lead will see it in `git status` (stage by explicit path)

```
 D Assets/_Modules/Core/Web3.meta
 D Assets/_Modules/Core/Web3/BackendRequestSigner.cs
 D Assets/_Modules/Core/Web3/BackendRequestSigner.cs.meta
 D Assets/_Modules/Core/Web3/IJupiterService.cs
 D Assets/_Modules/Core/Web3/IJupiterService.cs.meta
 D Assets/_Modules/Core/Web3/IWalletSigner.cs
 D Assets/_Modules/Core/Web3/IWalletSigner.cs.meta
?? Assets/_Modules/Core/Backend.meta
?? Assets/_Modules/Core/Backend/
```

⚠ Memory `a-deletion-in-git-status-will-ship-if-you-build` applies: these ` D ` lines are **mine and
intentional**, and the working tree is already correct — but they must be staged together with the
`??` additions or a build would compile a tree with the files simply gone.

### 1.4 Files edited — namespace + using lines ONLY

**Declarations (3)** — `Assets/_Modules/Core/Backend/`, each one line:
`BackendRequestSigner.cs:43`, `IJupiterService.cs:46`, `IWalletSigner.cs:36`.

**`using` lines (21 files, one line each):**
`Assets/Editor/Regression/EventTrackerIdentityRegression.cs:53` ·
`Assets/Tests/EditMode/SwapVMTests.cs:25` ·
`Assets/_Modules/Core/CoreServices.cs:27` ·
`Assets/_Modules/Core/Entitlements/SkuEntitlementService.cs:3` ·
`Assets/_Modules/Core/Payments/Providers/GooglePlay/GooglePlaySettlementComposer.cs:45` ·
`Assets/_Modules/Core/Promo/PromoCodeService.cs:54` ·
`Assets/_Modules/Core/Referral/ReferralService.cs:38` ·
`Assets/_Modules/Core/Social/CommunityShowcaseVoting.cs:5` ·
`Assets/_Modules/Core/State/GameStateService.cs:31` ·
`Assets/_Modules/GooglePlay/GooglePlayIdentityClient.cs:7` ·
`Assets/_Modules/GooglePlay/GooglePlayStorefrontVM.cs:9` ·
`Assets/_Modules/Village/Feedback/HonestFeedbackService.cs:101` ·
`Assets/_Modules/Village/Progression/RewardedProgression.cs:6` ·
`Assets/_Modules/Wallet/HeartboundStatusClient.cs:42` ·
`Assets/_Modules/Wallet/PackStore.cs:61` ·
`Assets/_Modules/Wallet/PurchaseEntitlementVerifier.cs:5` ·
`Assets/_Modules/Wallet/PurchaseQuoteService.cs:39` ·
`Assets/_Modules/Wallet/WalletService.cs:24` ·
`Assets/_Modules/Wallet/WalletSkinBootstrap.cs:25` (trailing comment kept, indentation re-aligned) ·
`Assets/_Modules/Web3/JupiterSwapService.cs:35` ·
`Assets/_Modules/Web3/SwapVM.cs:31`.

**Fully-qualified call sites (4 files, 10 lines):**
`Assets/_Modules/Core/Analytics/EventTracker.cs:175,327,359,384` ·
`Assets/_Modules/Core/Data/CardCollectionRemoteService.cs:27` ·
`Assets/_Modules/Core/Social/TownShowcaseClient.cs:57` ·
`Assets/_Modules/Core/State/GameStateService.cs:2332,2333,3242,3243`.

**Path strings that would have broken at RUN time, not compile time (5 files, 6 lines).** These are
`const string` paths a suite opens off disk; with the folder moved they would each have failed
file-not-found with a confusing message:
`Assets/Editor/Regression/BackendSaveAuthRegression.cs:13` (path) and `:48` (the escaped regex
`DeNelle\.Core\.Web3\.BackendRequestSigner` inside `Regex.IsMatch`) ·
`Assets/Editor/Regression/PlayMetadataIdentifierRegression.cs:85` ·
`Assets/Editor/Regression/WalletConnectFailureAttributionRegression.cs:41` ·
`Assets/Editor/Regression/WalletIdentityRegression.cs:88` ·
`Assets/_Modules/Core/Analytics/EventTracker.cs:310` (a comment citing the signer's path).

**Docs kept green (CLAUDE.md §15 — the load-bearing set, not dated ledgers):**
`Assets/_Modules/Web3/README.md:7` · `Assets/_Modules/Core/README.md:22` (the module map's `Web3/` row
is now `Backend/`, with a one-line "was" note so a seat grepping the old name still lands here) ·
`docs/MASTER_CATALOG.md:34-35` · `docs/MASTER_CATALOG/economy-meta.md:77-78`.
⚠ `Assets/_Modules/README.md:35` was checked and **correctly left alone** — its `Web3/` row is
`Assets/_Modules/Web3/` (the `DeNelle.Web3` assembly), a different folder.
`PROJECT_INDEX.md` carries no hit.

**Nothing else changed in any of these files.** No member, no signature, no call, no behaviour.

### 1.4b Source-text pins on the strings I changed — checked, none assert

The repo pins exact source substrings in suites constantly (I re-pointed one myself at
`BackendSaveAuthRegression.cs:48`). Grepping `Assets/Editor/Regression/` and `Assets/Tests/` for every
old substring I removed (`native SKR`, `CRYSTALS/SKR`, `crystals/SKR`, `(pi|skr|wallet)`, `F0} SKR`,
`stake read completed`, `weekly bonus`, `weekly re-roll`, `Solana dApp Store has no https`) returns
**6 hits, and every one is inside a FAILURE-MESSAGE string, never inside the assertion.** Opened and
read individually: `JewelerDiscoveryFtueRegression.cs:111,115,121,150,215` assert on IDENTIFIERS
(`JewelerDiscoveryText.StakeChecking`, `TryConsumeWeeklyReroll`, `PolishBonuses.TryConsumeWeeklyReroll()`,
the staking program address …), and `SiegeUntouchableRegression.cs:212` is prose in a `failures.Add`.
**No suite reads the FlowTrace prose I reworded.** Nothing needed re-pointing.

### 1.4c Comment-blind source lints — checked, and the comments reworded anyway

The comments I added sit in **runtime** files that compile into the Play player, and my first draft
spelled the very tokens the edits remove. Comments never reach IL2CPP metadata, so the artifact gate is
untouched — but a source-text suite would not know that. I checked the four suites that open the
affected files (`AdGateAndArenaReturnRegression.cs:89`, `HudUiRegression.cs:163`,
`MonetizationActivationRegression.cs:30`, `PiAdRewardVerificationRegression.cs:260`) and **none does a
token sweep on raw text** — each asserts on a specific identifier or phrase, and
`PiAdRewardVerificationRegression` even calls `StripComments` first. `CurrencySkinResolver.cs` is read
by no suite; `CatalogBootstrap.cs` is read by `AssetRootsRegression.cs:122` and
`JsonMirrorLiteralRegression.cs:91` (a path list and a `Consumer` name check, neither a token scan).

**I reworded the comments regardless** — they now describe the token ("the store's chain name", "the
wallet-rail currency name", "one of the ids is a gate token") and cite the byte offset, instead of
spelling it. Nothing is lost and a whole class of future source-lint risk is. Verified: grepping
`Assets/_Modules` for the three phrases returns **only pre-existing comments** in files that have
shipped through every gate (`CatalogEntry.cs:62`, `StructureRole.cs:32`, `FeatureFlags.cs:991,1370`,
`ManageVmProjection.cs:414`, `LoginViewModel.cs:23`, `HonestFeedbackTuning.cs:77`,
`PurchaseGate.cs:19`) plus the stale `.g.cs` that step 2 of §4 regenerates. **None is mine.**

### 1.4d CLAUDE.md §15 — four "byte for byte" claims that my own change made false

The generator's and the consumer's headers both asserted the embedded blob was the file byte-for-byte.
That stopped being true the moment I changed the emission, so it is corrected **in the same change**,
not deferred behind a `STALE:` flag:
`Assets/Editor/CatalogFallbackGenerator.cs:31` · `Assets/_Modules/Village/Catalog/CatalogBootstrap.cs:20,
81, 373`. ⚠ `CatalogBootstrap.cs` is a **runtime** file — brace-checked and NUL-checked with the rest.
The other `byte-identical` hits in both files (`CatalogFallbackGenerator.cs:101,115,200`,
`CatalogBootstrap.cs:386`) are about the **two canonical copies** being identical to each other, which
is still true and deliberately left alone.

### 1.5 The tree-wide grep (WO §1.4 — the load-bearing one)

| Surface | Result |
|---|---|
| `Assets/` — `.cs .asmdef .asmref .xml .json .uxml .uss .md .txt .asset .prefab` | **0 hits** (grep exit 1) |
| `Assets/` — `.unity` scene files | **0 hits.** Scenes bind scripts by `guid`+`fileID`, never by namespace, and every guid was preserved by the meta-with-file move |
| `Packages/` | **0 hits** |
| `tools/`, `ProjectSettings/`, `.githooks/` | **0 hits** |
| **`link.xml` — the runtime-not-compile-time trap** | **NO CHANGE NEEDED, and this is proven, not assumed.** `Assets/link.xml` preserves by **ASSEMBLY**, never by namespace: `<assembly fullname="DeNelle.Core" preserve="all" />`. A namespace rename inside `DeNelle.Core` is invisible to it. The other three link.xml files (`Assets/AddressableAssetsData/`, `Assets/Firebase/FirebaseApp/Internal/`, `Packages/com.solana.unity_sdk/Runtime/Plugins/Web3AuthSDK/`) name no `DeNelle.Core.Web3` type — the Addressables one preserves `DeNelle.Village.DragonBoss` only |
| `.asmdef` / `.asmref` carrying `Web3` | three hits, **all the SEPARATE `DeNelle.Web3` assembly**: `DeNelle.Tests.EditMode.asmdef:12`, `DeNelle.Web3.asmdef:2` (`name`), `:3` (`rootNamespace`). Untouched, correctly |
| **Reflection / serialized type names** | `grep -rn "TypeNameHandling\|SerializeReference" --include=*.cs Assets/` → **0 hits repo-wide**, and `GetType("…Web3…")` → **0 hits**. This is the check that matters most: nothing persists an assembly-qualified `DeNelle.Core.Web3.*` name into a save or an asset, so **no save can fail to load because of this rename** |

**Remaining occurrences, all outside the shipping surface and DELIBERATELY NOT TOUCHED (CLAUDE.md §15 —
dated point-in-time ledgers are frozen, never rewritten):**
`BOARD.html` (generated — regenerates itself) · `CLI_LANES_WO_NUMBERS.md:1476` ·
`Logs/device/pull-20260830/boot-logcat.txt`, `Logs/device/session-20260829-160136/*` (captured device
logs — rewriting a captured log would be falsifying evidence) · `docs/READY_RCA_2026-09-06.md` ·
`docs/handoffs/SESSION_HANDOVER_2026-09-15_play_track_and_wall_fork.md` ·
`WorkOrders/WORK_ORDER_{43,44,210,1160,1377,1377.RESULT,1440.RESULT,1441,1441.RESULT,1454.RESULT,1583,1583.RESULT,1733,1735,1735.RESULT,1740,1741,1741.RESULT,1754,1755}*.md`.

**`Builds/` — 300+ hits, ALL stale build ARTIFACTS, and none is a source surface.** A whole-repo
`grep -rl` (314 files total) surfaced the old namespace inside `Builds/{Windows,Windows-Dev-RaidSmoke,
EchoesOfElarion-Windows-Tester-2026.09.09.362377}/DefendersOfTheRealm_Data/Managed/*.dll` + `*.pdb` +
`globalgamemanagers.assets`, and inside a stale copy of the Assets tree at
`Builds/loc-manifest-store-buy-runtime-2/Assets/_Modules/Core/Web3/`. **`Builds/` is gitignored and
`git ls-files Builds` returns nothing**, so none of it is tracked, committed, or compiled from.
⚠ Worth one sentence anyway: `Builds/Windows/…/DeNelle.Core.dll` is a **shipped player carrying the old
namespace**, so it is now out of date with the tree. Memory `wipe-rebuild-exe-on-ready` already says to
wipe `Builds\Windows` and rebuild after a gate-green wave — this is one more reason not to skip it.
Outside `Builds/` and the frozen records above, the whole-repo scan surfaced **nothing else**.

One further occurrence is **mine and deliberate**: `Assets/_Modules/Core/README.md:22` now carries
`DeNelle.Core.Web3 → DeNelle.Core.Backend` inside its "was" note, so that a future seat grepping the old
name lands on the explanation instead of on nothing. It is a `.md`; it reaches no artifact.

> ⚠ **THIS IS A CONFLICT BETWEEN ACCEPTANCE CRITERION 1 AND CLAUDE.md §15, AND I AM NOT RESOLVING IT
> SILENTLY.** AC-1 says *"zero occurrences … (code, asmdef, link.xml, docs)"*. §15 says dated ledgers,
> RESULT files and captured logs are **frozen, never rewritten**. Every residual above is one of those.
> **AC-1 is met for everything that compiles, ships, or is read as current.** Whether the historical
> record should be rewritten too is **the lead's/owner's call**, not mine.

### 1.6 Proof the Solana / dApp Store variant is unaffected — what I actually checked

1. **The assembly boundary did not move.** `Assets/_Modules/Web3/DeNelle.Web3.asmdef` is byte-untouched;
   its `"name"`/`"rootNamespace"` are still `DeNelle.Web3` and its `!GOOGLE_PLAY` constraint is intact.
   `PlayMetadataIdentifierRegression.CaseWeb3AssemblyStillExcluded` (`:204-230`) reads that file and
   still passes — I only re-pointed a *different* const in that suite (`JupiterSvcRel`, `:85`).
2. **Every consumer is compile-bound, not name-bound.** All 27 files resolve `BackendRequestSigner` /
   `IWalletSigner` / `IJupiterService` through `using` or a fully-qualified expression, both of which the
   compiler rewrites. There is no `Type.GetType`, no `AddComponent(string)`, no `TypeNameHandling`.
3. **Nothing is persisted under the old name.** The `TypeNameHandling`/`SerializeReference` grep above
   returned zero repo-wide, so no save, ScriptableObject or prefab carries
   `DeNelle.Core.Web3.BackendRequestSigner+SessionResponse, DeNelle.Core`. **This was the one real risk
   to the Solana build and it is closed by measurement, not by reasoning.**
4. **`IJupiterService` stayed in the folder deliberately.** Its type-level `#if !GOOGLE_PLAY` (`:45`) is
   untouched, so WO-1377's compile-out still holds and the RCA §6 finding (absent from Play metadata)
   is unchanged. It cannot move to `DeNelle.Web3`: `CoreServices` holds the registry slot and `Core`
   cannot reference that assembly without a cycle (`WORK_ORDER_1377…RESULT.md:53`).
5. **`IWalletSigner` was NOT renamed** — WO-1740 item 9 leaves that open and bare `wallet` is an
   `AuthoringOnlyTokens` entry (`GooglePlayPackagingGate.cs:182-185`) never applied to artifact entries.
   Renaming it was not asked for and would be scope creep.

---

## 2. PART 3 — the authored `skr` literals

I classified each of the RCA's **located** hits. I did **not** bulk-rewrite, and I did not touch
anything the RCA measured as *not* live.

### 2.1 Reworded (authored strings that can simply be neutral) — 9 literals, 6 files

⛔ Every one is a **reword, never a delete or a `#if`** — CLAUDE.md §12 forbids stripping
instrumentation, and a removed `FlowTrace` turns a logged failure into a silent one.

| File:line | Was | Now |
|---|---|---|
| `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs:215` | `native SKR weekly re-roll CONSUMED: …` | `native stake weekly re-roll CONSUMED: …` |
| `…/PolishBonusProvider.cs:229` | `native SKR weekly re-roll period advanced…` | `native stake weekly re-roll period advanced…` |
| `Assets/_Modules/Village/Crafting/JewelerDiscoveryFtue.cs:298` | `native SKR stake read completed…` | `native stake read completed…` |
| `Assets/_Modules/Village/Crafting/JewelPolishService.cs:267` | `…verified native SKR weekly bonus attempt.` | `…verified native stake weekly bonus attempt.` |
| `…/JewelPolishService.cs:269` | `…after a native SKR weekly bonus check…` | `…after a native stake weekly bonus check…` |
| `Assets/_Modules/Village/Waves/DefenseReportBuilder.cs:439` | `CRYSTALS/SKR/PURCHASED GOODS/…` | `CRYSTALS/PREMIUM RAIL/PURCHASED GOODS/…` |
| `…/DefenseReportBuilder.cs:513` | `crystals/SKR/purchased goods/…` | `crystals/premium rail/purchased goods/…` |
| `…/DefenseReportBuilder.cs:586` | `crystals/SKR/purchased goods/…` | `crystals/premium rail/purchased goods/…` |
| `Assets/_Modules/Core/Platform/CurrencySkinResolver.cs:436` | `…not an allow-listed skin (pi\|skr\|wallet) — ignored.` | `…not an allow-listed skin — ignored.` + a 4-line comment saying why |

⚠ **On `CurrencySkinResolver.cs:436` specifically — I did NOT make the message lie.** The allow-list
itself is a code condition eight lines up (`:432`, `val == "pi" \|\| val == "skr" \|\| val == "wallet"`)
and is **unchanged**; only the message's *enumeration* of it was removed, and the message still names
the rejected value, which is the part a reader needs. Deleting the enumeration while leaving the
condition is the honest edit; changing the condition would have been a behaviour change.

**`Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs:629` — the ONE rendered token of the six
offenders.** `new Label($"{_costSkr:F0} SKR")` → `new Label($"{_costSkr:F0} cost")`, with a comment
recording that this menu is the editor/dev OFFSET tool (`BuildModeController.cs:129-131`), reached only
by `AdminOverlay` reflection and `AutoPilotDriver`. **The FIELD `_costSkr` is untouched** — it fails the
gate's leading word-boundary rule (`:457-459`), so renaming it would be churn with no effect on the
artifact.

### 2.2 Left alone, owner-ruled un-renamable → WO-1754's allowlist, not mine

- **`dotr-arena-skr-balance`** (`Assets/_Modules/Village/Arena/ArenaWalletService.cs:48-50`). Its own
  in-code banner: a renamed key reads as a fresh 500 seed (WO-1366 §4). **Untouched.**
- `PaymentChannel.SolanaDappStore`, `SkinAuthMode.SolanaWallet` — WO-1377, quoted at
  `PlayMetadataIdentifierRegression.cs:41-51`. **Untouched.**

### 2.3 Nothing was dead. Nothing was deleted.

Every located literal was live authored copy; none qualified for the ticket's "dead → delete" bucket.

### 2.4 ⚠ THE UNTRACED LITERAL AT OFFSET 239,196 — STILL UNPROVEN, AND I NARROWED IT

The brief said it stays recorded as unproven. **It does.** But the search is now much narrower and that
is worth writing down. I grepped every standalone `"SKR"` literal in `Assets/_Modules` with
`grep -rn --include=*.cs -P '(?<![A-Za-z0-9_"])"SKR"'` and each hit is **already accounted for**:

| Hit | Why it is not the 239,196 literal |
|---|---|
| `Core/Platform/CurrencySkin.cs:142` `currencyName: "SKR"` | inside the `#else` arm of `#if GOOGLE_PLAY` at `:130-151`; under Play the row reads `currencyName: "Store credit"`. **Compiled out.** |
| `Core/Platform/StakeRewardsResolver.cs:101` `DefaultCurrencySymbol = "SKR"` | same shape — `#if GOOGLE_PLAY` yields `"pts"`. The file's own header (`:94-96`) says it was made a const *because* the literal "reached the Google Play artifact's global-metadata.dat". **Compiled out.** |
| `Wallet/PurchaseGate.cs:506` | assembly `DeNelle.Wallet`, `!GOOGLE_PLAY`. **Not in the Play player.** |
| every other `SKR` occurrence in `Assets/_Modules` | a `//` or `///` COMMENT — comments never reach IL2CPP metadata |

`grep -rn "SKR, age=" --include=*.cs Assets/` still returns **nothing**, matching the RCA.
**I did not find it, and I am not claiming it is closed.** WO-1740 item 11 stands.

### 2.5 Candidates I found and deliberately did NOT touch, because the RCA measured them as not live

`Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs:202` (`" SKR"`, unguarded) and
`Assets/_Modules/DevTools/DevPanelController.cs:861,1568` (`"Mock SKR"`, unguarded) are authored
literals in Play-compiled assemblies. The RCA's whole-file measurement found **13 live `skr` hits out
of 53 occurrences**, and these are not among the 13 — i.e. they were measured as already disqualified
by the boundary or printable-run rules. **Rewriting them would be exactly the bulk-rewrite the ticket
forbids, on no evidence.** Recorded here so the next seat does not re-discover them as "missed".

---

## 3. PART 2 — `GooglePlayContentExclusion.cs`

### 3.1 ⛔ IT GETS NO NEW `PlayExcludedAssetPaths` OR `PlayNeutralMirrorPairs` ENTRY, AND THAT IS THE ANSWER

Both candidate dispositions for `CatalogFallbackData.g.cs` are wrong, and the WO says so itself:

- **Quarantine it** → deletes the JSON-load-failure fallback from the Play player. That fallback *is*
  WO-1137's entire purpose; without it a Play player that fails to read the catalog boots with an empty
  build palette.
- **Neutral-rewrite it at build time** → it is a `.cs`, so the Play AAB would compile **source nobody
  reviewed and no gate or suite ever saw**. That is the opposite of what every marker in this chain is for.

So I **fixed the generator instead**, which is the RCA's own item 5, and I wrote the decision *into the
file the next seat will open* — a 30-line block after the `PlayNeutralMirrorPairs` array in
`Assets/Editor/GooglePlayContentExclusion.cs`, recording the third copy, the measured offset, why
neither array may carry it, where the real fix lives, and the residual in §3.4 below. **That comment is
the extension.** Inventing a code change in this file to have touched it would have been worse than
nothing.

### 3.2 The generator fix — `Assets/Editor/CatalogFallbackGenerator.cs`

**NEW FILE: `Assets/Editor/Regression/CatalogFallbackProjection.cs`** — `public static class
CatalogFallbackProjection` with one method, `Strip(string json)`, plus a private recursive helper. It
removes every `'_'`-prefixed property at every depth and serialises deterministically.

> ⚠ **WHY THE NEW FILE EXISTS, AND WHY IT IS IN THE *REGRESSION* ASSEMBLY — this was a real blocker
> I caught before hand-back, not a style choice.** My first cut put `ProjectForFallback` on
> `CatalogFallbackGenerator` and had `BuildEconomyRegression` call it, on the strength of both types
> sharing `namespace DeNelle.Editor`. **A shared namespace is not a shared assembly.**
> `Assets/Editor/DeNelle.Editor.asmdef` and `Assets/Editor/Regression/DeNelle.EditorRegression.asmdef`
> are two assemblies, and `DeNelle.Editor`'s `references` array **already contains
> `DeNelle.EditorRegression`** — so the call I wrote was not merely inaccessible, the reference that
> would have fixed it is a **CYCLE**. The rule therefore has to live in the LOWER assembly, where both
> callers can reach it: the generator (`DeNelle.Editor`) calls **down**, and the suite finds it at home.
> `CatalogFallbackGenerator.ProjectForFallback` survives as a one-line delegating wrapper so the
> generator still reads as owning its own emission step.
> ⚠ **The new file needs a `.meta`, which only Unity can mint.** It will appear on the lead's first
> Unity run and must be staged with the file.

**Two parser settings that are deliberate, and would each silently change a value I never meant to
touch:**
- **`DateParseHandling.None`.** Newtonsoft's DEFAULT is `DateParseHandling.DateTime`: any string VALUE
  that looks like an ISO date is converted and re-emitted in a different form (`"2026-08-26"` →
  `"2026-08-26T00:00:00"`). The old generator never re-serialised, so **this hazard is new with this
  change**, and it is not theoretical — `grep -c '"20[0-9][0-9]-[0-9][0-9]-[0-9][0-9]'` on the catalog
  returns **6**. The reader is configured explicitly. *(My Python simulation could not have seen this;
  it is a Newtonsoft behaviour.)*
- **Newlines normalised to `\n`.** The writer emits `Environment.NewLine`, so the generated file's
  bytes — and therefore the gate's hash — would otherwise depend on which machine ran the generator.

**Changed in `Generate()`:** `BuildSource` is now handed `ProjectForFallback(json)` instead of the raw
`json`. **`resHash` and `resBytes.Length` are still the SOURCE FILE's**, so
`CatalogFallbackData.SourceSha256` / `SourceByteLength` keep their existing meaning and the staleness
gate (`BuildEconomyRegression` case B, `:1670`) is **completely unchanged**.

**Changed in `BuildSource()`:** the emitted banner and the `Json` doc-comment no longer claim
byte-identity; they say what the payload now is. The success log line reports the projected length.

#### Why stripping is safe — measured, not assumed

| Check | Result |
|---|---|
| `'_'`-prefixed keys in `structures-catalog.json`, at every depth | **61**, and **every single one carries a STRING value** (0 non-string). They are notes, not data |
| any runtime type reading one | **none.** Grepping all 35 distinct key names as quoted literals across `Assets/_Modules` + `Assets/Editor` returns exactly ONE reader: `Assets/Editor/Regression/CollectorIncomeRegression.cs:1357` (`row["_quarryNote"]`) — an **Editor** suite that reads the **JSON FILE**, which this change does not touch |
| ⚠ `_displayOrder`, `_firstStepRule`, `_costBasketRule`, `_heightCadence` — the ones that *look* like data | all four are **string prose notes**. The real field is `CatalogEntry.displayOrder` (`:134`), **no underscore**, and it is not stripped |
| rows / version survive the projection | **29 rows before and after; `version: 42` preserved.** Top-level keys after: `version`, `notes`, `entries` |
| forbidden tokens left in the projected payload | **ZERO.** I ran the gate's full vocabulary (`solana`, `jupiter`, `skr`, `usdc`, `web3`, `crypto`, `blockchain`, `walletadapter`, …, case-insensitive) over the projection: **not one hit.** The un-prefixed `notes` key survives and is clean |
| size | 112,046 → **47,436 chars**. 58% of the compiled blob was authoring prose |

### 3.3 The matching gate change — `Assets/Editor/Regression/BuildEconomyRegression.cs`

Case **C** ("nobody hand-edited the generated file") could previously re-hash the embedded string and
expect `SourceSha256`, because the payload *was* the file. It now **calls
`CatalogFallbackProjection.Strip` — the same method the generator calls** — and compares against that:
**one definition of the rule, two callers.** Writing the strip rule out a second time in the suite would
be precisely the duplicated state CLAUDE.md §2/§5/§16 each describe, and would let the gate certify a
file the generator would not produce. No asmdef reference was added (see the cycle note in §3.2).

Case B (staleness), case A (dual-copy), case D (row count) and case E are **untouched**. The two header
comment lines describing the old byte-identity were corrected in the same change (§15).

⚠ **I deliberately did NOT add a new `EmbeddedSha256` const to the generated file.** The suite would
then reference a const that does not exist until after regeneration — and Unity must **compile** before
a `[MenuItem]` can run, so the tree would be unbuildable and the generator unrunnable. Deriving the
expectation instead has no such ordering problem.

### 3.3b The fourth live `solana` hit — closed here, not deferred

WO-1740 RCA §2.3 lists four live `solana` hits. Two are owner-ruled (`SolanaDappStore`, `SolanaWallet` →
WO-1754's allowlist), one is `CatalogFallbackData.g.cs` (§3.2 above). **The fourth,
`Assets/_Modules/Village/Monetization/Providers/LevelPlayInitializer.cs:226` at offset 928,532, was
owned by no ticket at all** — it would have reached the lead as a surprise in the next AAB. RCA item 6
calls it *"one word, no ruling"*, so I did it: `(the Solana dApp Store has no https listing URL…)` →
`(the dApp Store has no https listing URL…)`, with the reason in-line. `dapp store` is not in the
vocabulary; `solana` was. **Checked first:** no suite pins that phrase
(`grep -rn "Solana dApp Store has no https\|auto-verification" Assets/Editor/ Assets/Tests/` → 0 hits).
Without this, acceptance criterion 3 could not have been met.

### 3.4 Residual, recorded not hidden

If a **player-facing** (non-`_`) value in `structures-catalog.json` ever carries a forbidden token, the
sweep's `PlayNeutralStringReplacements` branch fixes the two FILES and the `.g.cs` would still carry the
authored value. Measured 2026-09-15 by WO-1741: `_quarryNote` was the **only** token-bearing string in
that file, so there is nothing today. Written into `GooglePlayContentExclusion.cs` so it is found where
it matters.

### 3.5 `Packages/com.solana.unity_sdk/Resources/` — REPORTED, NOT QUARANTINED (owner ruling needed)

Contents, listed this session — **three PNGs and their metas, nothing else**:

| File | Size |
|---|---|
| `DefaultCollectionIcon.png` | 13,449 B |
| `background.png` | 9,886 B |
| `magicblock-logo.png` | 18,194 B |

Facts for the ruling:
- **The gate does not fire on them.** A token grep over the folder (`solana\|jupiter\|wallet\|skr\|usdc\|web3\|crypto\|blockchain`, case-insensitive, filenames and bytes) returns **nothing**. `magicblock` is not in `ForbiddenTokens`.
- **But they DO ship.** Anything under a folder named `Resources` is force-included into every player by construction — the same door this class's own header documents and the same one that shipped the Jupiter panel (WO-1741).
- **`magicblock-logo.png` is a blockchain company's logo**, shipping inside a Google Play artifact. That is a **policy** question, not a gate question, and the gate will never raise it.
- Unlike the WO-1741 quarantine, this path is under `Packages/`, not `Assets/`, so quarantining it into `Assets/PlayQuarantine` is a different and less-tested operation.

**I did not touch it.** It stays unruled, exactly as the brief instructed.

---

## 4. ⛔ WHAT THE LEAD MUST DO, IN THIS ORDER

1. **Reconcile the folder move** — stage the 7 ` D ` paths and the 2 `??` paths in §1.3 **together**, by
   explicit path. A build run against a half-staged tree compiles a `DeNelle.Core` with the signer
   missing.
2. **Run the generator BEFORE the regression suite.** The `.g.cs` in the tree is the **pre-WO-1755**
   blob and still embeds the authoring notes, so `[fallback-parity]` case C **will fail until it is
   regenerated — correctly, and its failure message says exactly this.**
   ```
   powershell -NoProfile -File .\run-unity-method.ps1 `
       -Method DeNelle.Editor.CatalogFallbackGenerator.Generate `
       -LogName catalog-fallback-gen.log `
       -ExpectMarker CATALOG_FALLBACK_GEN_OK
   ```
   Judge by the **`CATALOG_FALLBACK_GEN_OK` marker on a fresh log**, never the exit code (CLAUDE.md §8).
   ⚠ **This run is also the combined tree's FIRST COMPILE.** If it fails, grep the log for `error CS`
   *before* `CATALOG_FALLBACK_GEN_FAIL` — a compile error there is the compile gate failing on the
   merged tree (mine or another lane's), not the generator misbehaving.
   The regenerated `Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs` is part of this
   commit — expect a large, mostly-deletion diff (~64,600 fewer chars of embedded prose).
3. `COMPILE_GATE_OK`, then `REGRESSION_OK <n>/<n>` on fresh logs.
4. **Acceptance criterion 3 needs a fresh AAB chain run** — I could not run one. The predictions, stated
   as predictions:
   - **`web3` leaves the list entirely.** All 7 `global-metadata.dat` hits and all 3 `split0` hits were
     the namespace, its two shipping type names and the two IL2CPP source paths; every one of those five
     strings now reads `Backend`.
   - **`skr` drops from 13 live to 3.** 10 literals reworded (the 9 in §2.1 plus the rendered
     `TowerPlacementRotateMenu` label). The 3 that remain are the **two** `dotr-arena-skr-balance`
     occurrences, which the owner ruled un-renamable, and the **one** untraced literal at offset
     239,196 (§2.4). Both belong to WO-1754's allowlist / item 11, not here.
   - **`solana` drops from 4 live to 2** — the `.g.cs` note (§3.2) and `LevelPlayInitializer.cs:226`
     (§3.3b) are gone; the two owner-ruled enum identifiers remain and are WO-1754's.
   - **`crypto` will still fire.** That is the chunk-seam false positive, WO-1754, a separate lane.
   - Offender #1, the `PerformanceTestRunInfo` receipt (`com.unity.test-framework.performance`,
     `Packages/manifest.json:33`), is **untouched** — RCA item 2, out of this silo.

---

## 5. QUALITY GATE (CLAUDE.md §1) — run on every `.cs` touched

| Check | Command | Result |
|---|---|---|
| Gate-rule brace balance, Part 1 (31 files) | `python tools/gate_brace.py …` | `GATE_BRACE_SUMMARY bad=0 of 31`, **exit 0** |
| Gate-rule brace balance, Part 3 (6 files) | same | `GATE_BRACE_SUMMARY bad=0 of 6`, **exit 0** |
| Gate-rule brace balance, Part 2 (3 files) | same | `GATE_BRACE_SUMMARY bad=0 of 3`, **exit 0** |
| Gate-rule brace balance, post-advisor pass (4 files: the new projection file, the generator, the suite, `LevelPlayInitializer.cs`) | same | `GATE_BRACE_SUMMARY bad=0 of 4`, **exit 0** |
| **FINAL consolidated run over every `.cs` this lane touched** | `python tools/gate_brace.py <43 paths>` | **`GATE_BRACE_SUMMARY bad=0 of 43`, exit 0** |
| NUL bytes (WO-434) | `grep -qP '\x00'` over all 43 | **0 files carry a NUL** |
| `.unity` scenes hand-edited | — | **none** |
| new `System.Reflection` in a bridge script | — | **none added**; the reflection grep was read-only |

**43 distinct `.cs` files touched** — 31 (Part 1) + 6 (Part 3) + 3 (Part 2) + 3 from the post-advisor
pass (`CatalogFallbackProjection.cs`, `LevelPlayInitializer.cs`, `CatalogBootstrap.cs`); the generator
and `BuildEconomyRegression.cs` were re-edited, not newly touched. **One is NEW:**
`Assets/Editor/Regression/CatalogFallbackProjection.cs` (needs a Unity-minted `.meta`).
Plus 5 markdown files (§1.4) and this RESULT.

---

## 6. PROVEN vs UNPROVEN (CLAUDE.md §11B)

**Proven — read or measured this session:**
- The 3 declarations / 27 files / 34 lines, and that they are now zero across every code, data, scene,
  asmdef, link.xml, tools and ProjectSettings surface.
- `backend` is absent from `ForbiddenTokens` (`GooglePlayPackagingGate.cs:49-63`) and
  `DeNelle.Core.Backend` had 0 prior hits.
- `TypeNameHandling` / `SerializeReference` / `GetType("…Web3…")`: **zero repo-wide** — the rename cannot
  break a save load.
- `Assets/link.xml` preserves by assembly (`<assembly fullname="DeNelle.Core" preserve="all" />`), so it
  needs no edit.
- The folder meta guid `681d44b47fa74459a24ecaa926155a3a` survived the move.
- All 61 `'_'` keys are strings; no runtime reader; 29 rows and `version: 42` survive the projection;
  **zero forbidden tokens** remain in it.
- `Packages/com.solana.unity_sdk/Resources/` holds exactly three PNGs; no vocabulary hit.
- **`DeNelle.Editor.asmdef` references `DeNelle.EditorRegression`** (read at source), which is why the
  shared projection lives in the regression assembly and why the obvious reverse reference is a cycle.
- **No suite asserts on any string I reworded** — the 6 grep hits are failure-message prose; each
  assertion was opened and reads identifiers (§1.4b). No pin on the LevelPlay phrase either.
- The catalog carries **6** date-shaped string values, so `DateParseHandling.None` is a real guard.
- Brace and NUL results above.

**UNPROVEN — stated as unproven, because it is:**
- **I ran no Unity, no compile gate, no suite and no build.** Nothing here is compile-verified. Every
  claim about compilation is a claim.
- **The `skr` literal at offset 239,196 is still unlocated.** §2.4 narrows it and eliminates four
  candidates; it does not find it.
- **What a fresh AAB will actually report** (§4 item 4) is a prediction from the RCA's byte inventory,
  not a measurement.
- **Whether `Environment.NewLine` was the only non-determinism** in the Newtonsoft serialisation. I
  normalised it explicitly; I did not run the generator twice to prove byte-stability.
- The projection was simulated in Python (`json` + an equivalent strip) to measure rows, version and
  tokens. **Newtonsoft's `ToString(Formatting.Indented)` will not produce byte-identical text to
  Python's `json.dumps`** — that does not affect the row/version/token findings, which are about
  content, but the exact emitted bytes are whatever the generator produces.
- **Numeric re-serialisation.** Newtonsoft round-trips JSON numbers through `JValue`, so a value
  authored `2.50` may emit as `2.5`. Parse-equivalent, and no consumer compares the text — but it is a
  re-serialisation side effect I did not empirically rule out, so it is named rather than assumed away.
- **I did not run the generator twice** to prove byte-stability of its output.

## 6b. THE COMPLETE STAGING LIST (the tree is shared — do NOT derive this from `git status`)

⚠ The working tree was **already dirty from other lanes** when this lane started (the session snapshot
shows dozens of unrelated ` M ` files). `git add -A` would sweep them in. Stage exactly these:

**Moved (7 deletions + 2 additions, all together):** the block in §1.3.

**Modified `.cs` — Part 1:** `Assets/Editor/Regression/{EventTrackerIdentityRegression,BackendSaveAuthRegression,PlayMetadataIdentifierRegression,WalletConnectFailureAttributionRegression,WalletIdentityRegression}.cs` ·
`Assets/Tests/EditMode/SwapVMTests.cs` ·
`Assets/_Modules/Core/{CoreServices,Analytics/EventTracker,Data/CardCollectionRemoteService,Entitlements/SkuEntitlementService,Payments/Providers/GooglePlay/GooglePlaySettlementComposer,Promo/PromoCodeService,Referral/ReferralService,Social/CommunityShowcaseVoting,Social/TownShowcaseClient,State/GameStateService}.cs` ·
`Assets/_Modules/GooglePlay/{GooglePlayIdentityClient,GooglePlayStorefrontVM}.cs` ·
`Assets/_Modules/Village/{Feedback/HonestFeedbackService,Progression/RewardedProgression}.cs` ·
`Assets/_Modules/Wallet/{HeartboundStatusClient,PackStore,PurchaseEntitlementVerifier,PurchaseQuoteService,WalletService,WalletSkinBootstrap}.cs` ·
`Assets/_Modules/Web3/{JupiterSwapService,SwapVM}.cs`

**Modified `.cs` — Part 3:** `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs` ·
`Assets/_Modules/Core/Platform/CurrencySkinResolver.cs` ·
`Assets/_Modules/Village/Crafting/{JewelerDiscoveryFtue,JewelPolishService}.cs` ·
`Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs` ·
`Assets/_Modules/Village/Waves/DefenseReportBuilder.cs` ·
`Assets/_Modules/Village/Monetization/Providers/LevelPlayInitializer.cs`

**Modified `.cs` — Part 2:** `Assets/Editor/CatalogFallbackGenerator.cs` ·
`Assets/Editor/GooglePlayContentExclusion.cs` · `Assets/Editor/Regression/BuildEconomyRegression.cs`

**NEW:** `Assets/Editor/Regression/CatalogFallbackProjection.cs` **+ its Unity-minted `.meta`**

**Regenerated by step 2 of §4:** `Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs`

**Also modified, §15 canon-in-the-same-breath (see §1.4d):**
`Assets/_Modules/Village/Catalog/CatalogBootstrap.cs` (runtime — comments only, gated with the rest)

⚠ **BOARD LABEL, so nobody is surprised:** the brief dictated `**Status:** IMPLEMENTED, NOT YET GATED`,
and `tools/board_build.py:187` maps the leading `IMPLEMENTED` token to the **Done** bucket — the board
will show this ungated ticket as Done. Read `BOARD_CHECK_*` before committing the board
(memory `status-label-is-a-fixed-word-rulings-go-in-prose`).

**Docs:** `Assets/_Modules/Core/README.md` · `Assets/_Modules/Web3/README.md` ·
`docs/MASTER_CATALOG.md` · `docs/MASTER_CATALOG/economy-meta.md` ·
`WorkOrders/WORK_ORDER_1755_*.md` (Status flipped) + `…1755_*.RESULT.md` (this file) ·
`BOARD.html` after `python tools/board_build.py` (the lead's step — I ran no build tool).

⚠ `Assets/Editor/Regression/{CollectorIncomeRegression,DefenseReportContractRegression}.cs` appear
modified in `git status` and **are NOT mine** — I only read them.

## 7. NEEDS A RULING
1. **AC-1 vs CLAUDE.md §15** (§1.5) — do the frozen dated ledgers, RESULT files and captured device logs
   get rewritten, or does AC-1 mean "everything that ships or is read as current"? I did the latter.
2. **`Packages/com.solana.unity_sdk/Resources/`** (§3.5) — three PNGs, one of them a blockchain
   company's logo, force-included into every Play AAB. Gate-clean; policy-open.
3. **`IWalletSigner`** — deliberately not renamed (WO-1740 item 9 leaves it open).
