# WORK ORDER 1377 - The crypto tokens that survive every string guard: IL2CPP ships TYPE NAMES

**Status:** IMPLEMENTED (safe set) - awaiting gate (2026-09-09 lane META); residual identifiers accepted per owner ruling
**Silo / Lane:** Core assembly boundaries - `Assets/_Modules/Core/Web3/` -> `DeNelle.Web3`
**Type:** EXISTING architecture, Play-variant blocker
**Minted:** 2026-09-04 (CLI), surfaced by the WO-1363 purge
**Blocks:** the Google Play AAB passing its own artifact scan once WO-1364 widens the gate.

## THE FINDING - and it is the CEILING on WO-1363

WO-1363 moved **24 shipping token-bearing string literals down to 2**. It cannot get to zero, and no
amount of further `#if` work will, because:

⛔ **IL2CPP's `global-metadata.dat` carries TYPE AND MEMBER NAMES, not just string literals.**

A `#if` around a string removes the string. It does nothing to an identifier. Still shipping in
`DeNelle.Core`, which is in every build:

| Identifier | Home |
|---|---|
| `IJupiterService` | `Core/Web3/IJupiterService.cs` |
| `CoreServices.Jupiter` / `RegisterJupiter` / `UnregisterJupiter` | `Core/CoreServices.cs:154,157,172` |
| `FeatureFlags.JupiterSwap` | `Core/FeatureFlags.cs` |
| `SwapToken.USDC` | `Core/Web3/` |
| `PaymentChannel.SolanaDappStore` | `Core/Payments/` |
| `SkinAuthMode.SolanaWallet` | `Core/Platform/` |

**So the artifact scan WILL still find `solana`, `jupiter` and `usdc` after WO-1363 lands.** That is
not a purge failure - it is a different defect with a different fix, and conflating them will send
someone hunting for string literals that are not there.

## THE FIX SHAPE

Move `Assets/_Modules/Core/Web3/*` into the **already `!GOOGLE_PLAY`-constrained** `DeNelle.Web3`
assembly (`DeNelle.Web3.asmdef:17`), so the types are not compiled into the Play variant at all -
the same **Tier 1 assembly exclusion** that WO-1362 proved genuinely works (the merged dex contains
no MWA, no Solana). Then rename the two offending enum members.

⚠ This is CROSS-ASSEMBLY. `CoreServices` is referenced by everything; removing `Jupiter` from it in
the Play variant means every caller needs the same guard or the seam needs re-shaping.

## §4. ⛔ THE OWNER RULING THIS IS BLOCKED ON - a rename can be DATA LOSS

**`PaymentChannel` and `SkinAuthMode` may be save-serialised.** A grep of `Assets/_Modules/Core/State/`
found no obvious persistence and no `[JsonProperty]` on them - **but that is NOT PROVEN safe.**

⛔ **If either is stored by NAME in a save, renaming the member silently breaks every existing
player's save on read.** If stored by ORDINAL, renaming is safe but REORDERING is not.

**Establish which, at source, before touching either** - and if they are persisted, this needs a
read-migration and probably a schema bump, which makes it a much larger ticket than the asmdef move.

⚠ **The cheaper alternative worth putting to the owner:** if the identifiers cannot be moved safely,
is a Play artifact that contains the *type name* `IJupiterService` - with no reachable code, no
string copy and no UI - actually a policy problem? **That is a judgement call about what a reviewer
would object to, and it is hers.** A dead type name in a metadata blob is a very different thing from
"Powered with SKR" on a screen. ⛔ Do not assume either answer.

## §4b. ⭐ THE RULING, AND THE ANSWER TO §4 — 2026-09-09 (lane META)

**Owner ruling, verbatim:** ***"Prove persistence first; move only what is safe, accept the rest with
a recorded reason."***

**§4 IS ANSWERED. NEITHER ENUM IS PERSISTED.** `grep PaymentChannel|SkinAuthMode` over
`Assets/_Modules/Core/State/` → **0 hits**; no field of either type exists outside the live
resolver/provider seam; no `PlayerPrefs` writer; no `ToString()`/`Enum.Parse` round-trip to storage.
`SaveSchema.CurrentVersion = 41` (`Assets/_Modules/Core/State/SaveSchema.cs:41`) is untouched, and
**no schema bump was needed**. Full per-identifier evidence table:
`WORK_ORDER_1377_crypto_identifiers_in_il2cpp_metadata.RESULT.md` §1.

**MOVED (compiled out at TYPE / MEMBER level under `#if !GOOGLE_PLAY`, never a runtime guard):**
`IJupiterService`, `SwapQuote`, `SwapInputToken`, `SwapInputToken.USDC`, `CoreServices.Jupiter` /
`RegisterJupiter` / `UnregisterJupiter`, `FeatureFlags.JupiterSwap` + its editor menu. **Proven safe
because they have ZERO consumers in any assembly that ships under GOOGLE_PLAY** — every implementation
lives in `DeNelle.Web3` (`DeNelle.Web3.asmdef:17` `"!GOOGLE_PLAY"`) and the only test lives in
`DeNelle.Tests.EditMode` (`UNITY_INCLUDE_TESTS`, Editor-only).

⚠ **The WO's `SwapToken.USDC` DOES NOT EXIST.** `grep -rn "SwapToken"` → 0 hits. The real identifier
is **`SwapInputToken.USDC`** (`Core/Web3/IJupiterService.cs`). A seat hunting the WO's name finds
nothing and wrongly concludes it is already gone.

**ACCEPTED AS RESIDUAL, with the reason recorded (this is the half the owner accepted):**

- **`PaymentChannel.SolanaDappStore` STAYS.** It is a live, un-`#if`'d `switch` case *inside*
  `DeNelle.Core`: `CurrencySkinResolver.ResolveWagerCurrency` (`Core/Platform/CurrencySkinResolver.cs:310`),
  which compiles into the Play build. It cannot be `#if`'d there either — the owner's Arena ruling is
  recorded in that file's own comment at `:266-272`: *"ONE Arena, ONE code path; the CURRENCY is the
  only thing that varies by channel, and it is resolved HERE … **never by a `#if GOOGLE_PLAY` inside
  the Arena module**."* Hiding the token would fork the code path the owner ruled must not fork.
  Not persisted; explicit value `= 1`. ⛔ **Never rename, never reorder.**
- **`SkinAuthMode.SolanaWallet` STAYS.** It is bound **by NAME** to shipped canonical data:
  `CurrencySkinResolver.ParseAuth:501` matches the literal `"SolanaWallet"` against
  `Assets/Resources/Data/Canonical/skin.json:30` `"authMode": "SolanaWallet"` (+ the `StreamingAssets`
  byte-twin), with live shipping references at `CurrencySkin.cs:143` and `PiSignInController.cs:501`.
  Compiling it out breaks the skin table in **every** build, not just Play. Not a save field — so a
  rename is not save data-loss — but it **is** a code↔data contract. ⛔ **Never rename, never reorder.**

**So the Play artifact will still contain the type name `PaymentChannel` and the member names
`SolanaDappStore` / `SolanaWallet` in `global-metadata.dat`, with no reachable crypto code, no string
copy and no UI. That is the accepted residual.** The `jupiter` and `usdc` identifier families are gone
from the Play variant's `DeNelle.Core` source.

**Pinned by:** `Assets/Editor/Regression/PlayMetadataIdentifierRegression.cs` — **two-sided** (absent
with `GOOGLE_PLAY`, present without it, so a *deletion* also goes red) + the `DeNelle.Web3.asmdef`
`"!GOOGLE_PLAY"` assertion. ⛔ **It is SOURCE-level. The physical `global-metadata.dat` scan stays a
SHIP-CHAIN step** and is the only thing that proves the shipped bytes.

⛔ **STILL OPEN, not ticked:** both-variant compiles, the metadata before/after counts, the save
round-trip, and the one-line registration in `DataRegression.RunAll` (RESULT §4) — **the tree is RED
on `RegressionMarkerRegression` RULE 3 until that line lands.**

## ACCEPTANCE

- [ ] Established at source whether `PaymentChannel` / `SkinAuthMode` are persisted, and how (name vs
      ordinal). Quote the evidence.
- [ ] Owner has ruled §4 - either the move happens, or the residual identifiers are accepted with a
      recorded reason.
- [ ] If moved: the Play variant compiles (`-ExtraScriptingDefines GOOGLE_PLAY`) AND the dApp variant
      compiles. ⭐ Both, every time - the purge already proved a Play-only change can pass the default
      compile and still be wrong.
- [ ] ⛔ **Proven against the ARTIFACT**: scan `global-metadata.dat` for `solana`/`jupiter`/`usdc` and
      quote the before/after counts. A source grep does not close this - a source grep is exactly what
      certified the dirty build.
- [ ] No existing save fails to load. Prove it with a real save round-trip, not a code read.

## WHAT NOT TO TOUCH

- ⛔ Do not rename an enum member before answering §4. That is the data-loss path.
- ⛔ Do not delete `IJupiterService` - Jupiter swap is a real dApp-lane feature, only absent on Play.
- ⛔ Do not widen `DeNelle.Web3`'s constraint - `!GOOGLE_PLAY` is correct and is what makes this work.

---
## RCA re-verified 2026-09-04 (QA read-only pass)
**Verdict:** NEEDS-RULING
**Evidence:**
- The identifiers are where the WO says: `Assets/_Modules/Core/Web3/` holds `BackendRequestSigner.cs`, `IJupiterService.cs`, `IWalletSigner.cs`; `Assets/_Modules/Core/CoreServices.cs:154` `public static IJupiterService Jupiter`, `:157 RegisterJupiter`, `:172 UnregisterJupiter`; `Assets/_Modules/Core/Payments/IPaymentProvider.cs:10` `SolanaDappStore = 1`; `Assets/_Modules/Core/Platform/CurrencySkin.cs:27` `SolanaWallet = 1`, `:143` `authMode: SkinAuthMode.SolanaWallet`. `DeNelle.Web3.asmdef:17` `"!GOOGLE_PLAY"` - the exclusion this WO would reuse. `Core/Web3` last touched `13770a912` 2026-08-30.
- The artifact scan the WO predicts is now REAL: `Builds/wo1367-aab.log:37493 PLAY_ARTIFACT_DIRTY ... token:solana` / `:37507 PLAY_ARTIFACT_REJECTED` (WO-1364's widened gate, `GooglePlayPackagingGate.cs:50-62`). That particular hit is a canon-strings.json literal (WO-1363's residue), so the metadata-only count this ticket needs (`global-metadata.dat` before/after) has NOT been measured yet.
- s4 persistence question, partial evidence from this seat: `grep PaymentChannel|SkinAuthMode` over `Assets/_Modules/Core/State/*.cs` = 0 hits (not a GameState field); `grep PaymentChannel` over `Assets/_Modules/**/*.cs` filtered to `PlayerPrefs|Parse|ToString()` = 0 hits; `SolanaDappStore|SolanaWallet` in `Assets/Resources/Data/**/*.json` hits only `skin.json` (a skin row, not a save), and in `api/**/*.js` only comments. That is evidence of NO name-persistence in the paths grepped; it is not the "prove it with a real save round-trip" the WO demands.
- No RESULT; referenced by WO-1363 (SUPERSEDED, ceiling) and WO-1366 (VALID).
**What changed since the RCA:** nothing in the cited code; the gate that will catch the identifiers now exists and is RED on a different token.
**Ready for a lane?** no - blocked on the s4 owner ruling exactly as stated (move + rename vs accept a dead type name in metadata). Files a lane would touch once ruled: `Core/Web3/*` -> `Assets/_Modules/Web3/`, `Core/CoreServices.cs:154-172` (+ every `CoreServices.Jupiter` caller), `Core/FeatureFlags.cs`, `Core/Payments/IPaymentProvider.cs:10`, `Core/Platform/CurrencySkin.cs:27`, both asmdefs, save read-migration if persisted.
**Pins/rulings needed:** owner rules s4; CLI proves (name vs ordinal vs not persisted) with a real save round-trip before any rename.
