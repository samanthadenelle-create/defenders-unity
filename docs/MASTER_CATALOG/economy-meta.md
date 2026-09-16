# MASTER CATALOG — Area: economy-meta (currencies, monetization, meta-economy)

**Rewritten 2026-08-02 — verified from the actual code (not comments), file:line cites throughout.**
Scope: the currency services + wallets (EconomyService, GlimmerCurrencyService), the pack store +
`packs.json`, the battle pass, the monetization covenant oracle, the crystals-vs-SKR money split,
the wallet/Jupiter UI surfaces, the persistence-store split, and the PENDING WO-830 Echo-affinity
economy. Supersedes the 2026-06-12 body (and its 2026-07-22 STALE banner) in full.

> Historical note: the 2026-06-12 revision of this file also carried the full **Pets** module
> inventory (Pet.cs / PetDeployer / PetHarvester / acquisition / skill trees). That inventory was
> NOT re-verified in this pass — consult this file's git history for it; only the pet↔economy
> seams that this pass touched are re-stated below.

Legend: **[LIVE]** wired & functional · **[STUB]** scaffolded/inert · **[DEAD]** unused ·
**[PENDING]** spec only, not implemented · **[FLAG n]** see Risk Ledger.

---

## DELTA 2026-09-06 — WO-1441: the backend session had NO ESTABLISHMENT PATH, and cloud save was dark for every wallet holder

**Read this before touching `BackendRequestSigner`, `WalletSkinBootstrap`, or `api/auth/session.js`.**

**The defect.** Nothing minted a wallet backend session outside a purchase or a promo redeem, so a
wallet holder who had never bought a pack had **no cloud save at all** — every `/api/game/save`
refused fail-closed. Proven on the owner's device (pid 7170, 2026-09-06,
`logs/debug/raid-no-abilities-2026-09-06.log`): `Connect OK` at 12:50:06.956, warm-up deferred at
.960, first `why=missing` at 12:50:11.556, and **`MintSessionAsync` appears ZERO times in 76 MB of
that day's captures**. Present since the WO-1157 fail-bounce (2026-08-27).

**Root cause — a method with zero call sites.** `BackendRequestSigner.MintSessionForExplicitConnectAsync`
was written to be the establishment path and **was never called by anything**. Both connect paths
called `WarmUpSessionAsync`, which deliberately did not mint and traced *"first authenticated action
will mint"* — false since WO-1157, because `TryAttachSession` mints only when `allowMint` is set
(`/api/purchases/*`, or `allowInteractiveSessionMint: true`, today only `PromoCodeService`). Cloud
save is neither.

| Now | Was |
|---|---|
| Both connect paths call `MintSessionForExplicitConnectAsync` | Both called `WarmUpSessionAsync` (no-op) |
| `WarmUpSessionAsync` **DELETED** (no callers left) | The only thing connect called |
| Auto-resume MINTS — one boot handshake | WO-1211: "boot never signs" |
| `TryRenewSessionAsync` — signature-free renewal on the save path | nothing; `why` flipped to `expired` at 15 min |
| `signed_at` caps the renewal chain at 12 h | renewal was uncapped = a permanent login |

- ⭐ **OWNER RULING 2026-09-06 REVERSES WO-1211.** Auto-resume mints. It shows no connect prompt of
  its own, so the handshake is the session's ONLY wallet sheet — under her stated two-prompt shape,
  not over it. A first-run player still sees nothing (`TryAutoResumeAsync` returns early with no
  sealed session). The reasoning WO-1211 was protecting is kept verbatim in-code at
  `WalletSkinBootstrap.TryAutoResumeAsync`; only its arithmetic was wrong.
- ⛔ **RENEWAL ALREADY EXISTED IN PRODUCTION, UNDOCUMENTED AND UNCAPPED.**
  `wallet-auth.verifyWallet` tries the session rail FIRST when `x-session` is offered, so
  `POST /api/auth/session` with a valid session and no nonce has always returned a fresh token with
  no signature. Uncapped, that is exactly the "permanent login" the file's own TTL comment forbids.
  WO-1441 adds the ceiling (`signed_at` + `SESSION_ABSOLUTE_TTL_SECONDS`, carried across rotations,
  old token revoked), **not** the renewal. A capped refusal must `return`, never fall through to
  `verifyWallet` — which would renew it anyway. Pinned by `test/auth.session.renewal-cap.test.js`.
- ⚠ **`SESSION_TTL_SECONDS` (900) and `SESSION_ABSOLUTE_TTL_SECONDS` (43200) are different axes.**
  The first is how long a STOLEN token is useful and must stay short; the second is how long the
  player goes without a wallet sheet. Never "fix" renewal by raising the first.
- ⚠ **Needs `api/schema.sql` applied** (`signed_at`). Until then `renewSession` reports
  `likely_schema` and **falls through to the existing rail on purpose**, so a lagging DB degrades to
  today's behaviour instead of losing renewal.
- `NightMarketSharedCardSession.OpenBrowser()` is a **card-browser modal, not a web browser** — no
  deep link, no return leg. It cost this triage a wrong first hypothesis; the trap is documented at
  the method.

### WO-1454 (same day, later)  -  `TryRenewSessionAsync` now CLASSIFIES the failure instead of counting any failure as a refusal

> STOP: **THE STATUS DECIDES, NOT THE MERE FACT OF FAILURE.** As shipped that morning,
> `TryRenewSessionAsync` called `ClearSession()` on **any** non-`Success` result, on an empty-token
> 2xx, and on a parse exception. **Save passes `allowMint:false` and nothing else re-mints**, so a
> single 500/503/timeout  -  the server saying *"try again"*, which is the opposite of *"you are not
> who you say"*  -  **permanently darkened cloud save for that install** until the player
> re-authenticated by hand. The 500 was not hypothetical: `api/auth/session.js:104` returns
> `SERVER_ERROR` when the renewal query throws, e.g. while the `signed_at` column is missing from a
> database that has not had `api/schema.sql` applied  -  a DEPLOYMENT state, not a verdict on the
> player's credential. Read at source: `Assets/_Modules/Core/Backend/BackendRequestSigner.cs:558-700`
(the folder was `Core/Web3/` and the namespace `DeNelle.Core.Web3` until WO-1755, 2026-09-15).

- **`IsCredentialRefusal(long status)`** `:678`  -  **`401 || 403` and nothing else.** STOP: **5xx IS NOT
  ON THIS LIST AND MUST NEVER BE ADDED**; the server's real refusals are its
  `quietFail(res, 401, ...)` for a wrong-wallet or absolute-cap token (`api/auth/session.js:117,:135`).
  Only this branch calls `ClearSession()`, which turns the next `why` into `missing`  -  honest, and
  the player re-signs on their next explicit connect or purchase.
- **Everything else KEEPS the token and backs off.** Transport throw, non-`Success` result, a 2xx
  carrying no token (far more likely a captive portal or proxy than a revocation  -  our server only
  ever returns 200 *with* a token), and an unparseable body all call **`ScheduleRenewalRetry()`**
  `:685` and return `false`.
- **Backoff constants** `:81-84`: `RenewBackoffBaseSeconds = 5`, `RenewBackoffMaxSteps = 5` ->
  5/10/20/40/80 s ceiling, held in `_renewFailureStreak` + `_renewBackoffUntilUtc`. (!) **These gate
  the RETRY CADENCE ONLY  -  they never gate the credential itself.** A renewal attempt inside the
  backoff window returns early with `action=keep reason=backoff` and **the token is untouched**;
  a successful renewal calls **`ClearRenewalBackoff()`** `:695`.
- `ClearSession()` `:104` now also clears the backoff  -  the penalty box belonged to the token that
  just went away, and a new wallet must not inherit it.
- **Every branch traces with an `action=` verb** (`action=keep` / `action=clear`) plus `status=`,
  so one capture line names the decision. That grammar is the thing to grep for in a device log.

---

## DELTA 2026-08-30 — WO-1282 Lane A: the store SPLIT into `DeNelle.Commerce` (rail-neutral) + `DeNelle.Wallet` (the Solana rail)

**Read this before any file:line cite below that says `Assets/_Modules/Wallet/PackCatalog.cs` or
`.../ShortfallPackOffer.cs` — those two files MOVED.** New home:
`Assets/_Modules/Commerce/` (assembly **`DeNelle.Commerce`**). Line numbers inside them shifted;
everything else in this document still stands.

**What was done and why.** `DeNelle.Village.asmdef` referenced `DeNelle.Wallet`, which meant a
Google Play artifact could not exclude the Solana rail — `AndroidBuild.BuildGooglePlayAab()` refuses
at `GooglePlayPackagingGate.AssertSourceIsolation()` while that reference exists. Village never
needed the rail, only the contracts. So:

| Moved to `DeNelle.Commerce` (namespace **kept** as `DeNelle.Wallet`) | Stayed in `DeNelle.Wallet` |
|---|---|
| `PackCatalog`, `PackDef`, `PackContents`, `PackPricing`, `PackEconomy`, `ConvenienceItemDef`, `BoostSpec`, `StoreBand`, `PackCatalogData` | `PackStore` (3546 lines, `new WalletService()`), `PackStoreVM`, `PurchaseGate`, `PurchaseQuoteService`, `MainnetCanaryCatalog`, `BattlePassService`, `BattleMonthlyCatalog`, `RewardGrantWriter`, everything `CurrencyKind`-shaped |
| `ShortfallPackOffer`, `ShortfallOffer` | — |

**Three seams were introduced** (new files, namespace `DeNelle.Commerce`), each registered from a
Wallet-side `BeforeSceneLoad` bootstrap and each instrumented so an unregistered seam is visible in
the trace rather than silent (§12):

- `StoreFocusRequest` — the WO-1253 focus **latch**, lifted out of `PackStore.RequestFocusSku`.
  `PackStore.RequestFocusSku` survives as a one-line forwarder, so Wallet-side callers are unchanged;
  `ManageScreenVM.BuySlot` now calls `StoreFocusRequest.RequestFocusSku`.
- `StorefrontRegistry` — a **lazy resolver** for the storefront's scene host, registered by
  `PackStoreBootstrap`. Lazy, not a push-registry, because the store host is disabled in the scene
  and therefore never runs `Awake`. `MarketplaceInteractor` calls `ResolveRoot()` where it used to
  call `FindAnyObjectByType<DeNelle.Wallet.PackStore>(FindObjectsInactive.Include)`.
- `ArenaOutcomeRelay` — `ArenaProgressStore` publishes `(win, streak, perfect)`;
  `BattleMonthlyPanelsBootstrap` subscribes `BattlePassService.OnArenaResult`. **The one-door XP rule
  is unchanged** — the relay carries an OUTCOME, never an amount, and
  `BattleMonthlyRegression`'s `[xp-one-door]` case now asserts BOTH halves (publish + subscription).

**`PackDef` lost four members to the rail.** `AmountFor(CurrencyKind)`, `AmountLabel(CurrencyKind)`,
`UsdApprox` and the private `IsServerPinnedSku` are now extension methods in
`Assets/_Modules/Wallet/SolanaPackPricing.cs`. Bodies verbatim; every ruling in their comments
(WO-1158's "the client does no price arithmetic", the ZERO-is-honest SKR branch, the server-pinned
canary exception, the colourblind "Price unavailable" wording) is unchanged. **The one call-shape
change in the whole refactor: `pack.UsdApprox` is now `pack.UsdApprox()`.**

**⚠ THE NAMESPACE DID NOT CHANGE, AND THAT IS DELIBERATE.** The moved types are still
`DeNelle.Wallet.*`. `Assets/_Modules/Core/Promo/PromoCodeService.cs:334-335` resolves
`"DeNelle.Wallet.PackContents"` and `"DeNelle.Wallet.PackStoreVM"` as **string literals** by
reflection across every loaded assembly; renaming the namespace compiles clean and turns promo-code
redemption into a silent runtime no-op. Namespaces and assemblies are orthogonal — the Play build
excludes the **assembly**, which is `DeNelle.Commerce` vs `DeNelle.Wallet`.

**Assembly graph after this change:** `DeNelle.Commerce` -> `DeNelle.Core` **only**, forever.
`DeNelle.Wallet` -> `DeNelle.Commerce`. `DeNelle.Village` -> `DeNelle.Commerce`, and **no longer ->
`DeNelle.Wallet`**. Editor/EditorRegression/DevTools/Web3/both test assemblies gained
`DeNelle.Commerce` alongside their existing `DeNelle.Wallet`.

**Still open (NOT this delta):** Lane B — `MobileWalletAdapter.androidlib` still has no
per-artifact exclusion, so `AssertSourceIsolation` still fails on that fourth condition.
`DeNelle.DevTools` still references `DeNelle.Wallet` under `UNITY_EDITOR || DEVELOPMENT_BUILD`,
which breaks a Play artifact built as a DEVELOPMENT build.

---

## DELTA 2026-08-21 — the Season Track + Monthly Ledger runtime, The Night Market, and PurchaseGate's move into `DeNelle.Wallet`

Read from source 2026-08-21. Where this block and the 08-02 body disagree, this block wins.
**Note first: §9's "WO-830 PENDING — spec only, NOT in code" is stale; that work shipped.**

### ⛔ TWO BATTLE PASSES NOW EXIST IN THE TREE, AND THE CONFLICT IS DECLARED IN THE CODE

`Assets/_Modules/Cosmetics/BattlePassManager.cs` (§5 below) is untouched and still dormant.
`Assets/_Modules/Wallet/BattlePassService.cs` is a NEW, independent, data-driven runtime. The new
file's own header (`:51-82`) declares the conflict and says it **needs an owner decision — "do not
let it sit"**, offering (a) retire `BattlePassManager`, lifting its guarded LevelUpVFX reflection
bridge across first, or (b) keep it dormant deliberately and record why. What must not happen is
both surviving unruled. Reasons the old one could not be built on, verified at source: it is driven
by a `BattlePassData` ScriptableObject that **does not exist as an asset** (and an SO cannot be
validated by a JSON-reading build gate); its premium track costs **2400 Glimmer**, and the owner's
2026-08-21 ruling retired Glimmer as a paid reward line; and it has one flat `xpPerLevel` with no
concept of a season, calendar month, per-tier reward pair, claim state or monthly card.

### NEW canonical data — `battle_monthly.json` (BOTH copies: Resources + StreamingAssets)

A **sibling** of `packs.json`, not a block inside it: `PackDef` describes one purchasable bag of
goods, a season is a TIERED TRACK climbed by playing, and a monthly card is a THIRTY-CLAIM POOL.
Neither shape fits `PackDef`. `packs.json` is untouched.

### `Wallet/BattleMonthlyCatalog.cs` (690) — typed model + loader, and the FIREWALL lives at LOAD

Types: `enum RewardKind` · `RewardEconomy` · `RewardConvenience` · `RewardGrant` (`ParseKind`,
`Describe`) · `BattlePassTier` · `BattlePassXpRules` · `BattlePassSeason` (`XpRequiredScaled`,
`DaysInSeasonMonth`, `SeasonStart`, `DaysRemainingInSeason`) · `MonthlyDailyDrip` · `MonthlyCard`
(`Day(int)`) · `BattleMonthlyData`, plus the static `BattleMonthlyCatalog` (`Seasons`, `Cards`,
`DroppedGrants`, `FindCard(sku)`, `Reload()`, `ResetSkrProbe()`). Loaded WebGL-safe through
`CanonicalJson.Read` (Resources first, StreamingAssets fallback).
Three **separate axes** the loader enforces — do not merge them: **LEGALITY** (a grant kind outside
`{economy, convenience_token, cosmetic_sku, skr, bundle}` is REJECTED; there is no `combat` kind, so
adding one is a code edit this file refuses, and a convenience kind outside `PackCatalog`'s
sanctioned allowlist is rejected by ASKING `PackCatalog.EnforceCovenant` rather than re-listing it,
so the two cannot drift); **REDEEMABILITY** (legal is not redeemable); and deliverability.
`DroppedGrants` is the visible count of what the firewall ate.

### `Wallet/BattlePassService.cs` (536) — static

`enum TierState` · `Xp` · `PremiumLaneOwned` · `XpFor(tier)` · `FreeState`/`PremiumState` ·
`OnArenaResult(win, streak, perfect)` · `ClaimAllReady()` · `AutoClaimAll()` ·
`UnlockPremiumLane(bypassPurchaseCheckForTesting)` · `ResetForTests()`.
⛔ **XP is earned by playing. There is exactly ONE public way it enters — `OnArenaResult`, which
takes an OUTCOME, not an amount.** No reward kind credits XP, no SKU credits XP, and there is no
`AddXp(int)` on the public surface. Owner ruling Q4 (2026-08-21) went further: **never sell tiers** —
no catch-up, no partial-season pricing. Buying the pass unlocks the LANE; the TIERS are earned.
The XP source is the existing arena ledger (`ArenaProgressStore.RecordWin/RecordLoss` in
`DeNelle.Village` notifies this service), so the pass adds **no combat surface at all**.
⚠ **One declared divergence from the WO pseudocode:** a crossed tier moves to **READY** and is
granted on CLAIM, not auto-granted on crossing — auto-granting deletes the only state the UI spec
lets animate.
Persistence is **PlayerPrefs**, deliberately: `SaveSchema.CurrentVersion` is on a live published
game and a bump is an OWNER decision, not a side effect of a monetization feature. `ArenaProgressStore`,
`GlimmerCurrencyService` and `BattlePassManager` all use PlayerPrefs for the same reason. **This is
a FOURTH unreconciled persistence store — see §7.**

### `Wallet/MonthlyCardService.cs` (272) — static

`enum MonthlyDayState` · `IsActive(sku)` · `ClaimsRemaining(sku)` · `NextDay(sku)` ·
`CanClaimToday(sku)` · `DayState(sku, day)` · `ActivateCard(sku)` · `Claim(sku)` ·
`ResetCardForTests(sku)`.
⭐ **THE POOL MODEL IS THE PRODUCT DECISION.** `durationDays` counts **CLAIMS, not calendar days**.
A missed day is never lost; nothing expires, so **nothing counts down and there must be no timer on
the screen** — a ticking clock over a pool that cannot lapse manufactures urgency the spec promises
not to apply. No streak, no streak penalty. Re-buying while active **EXTENDS** the pool, never
overwrites. The drip is a BONUS ON TOP OF the free daily system: nothing here reads or writes
`DailyQuestRewardBridge` state, and claiming one never consumes the other.
One claim per UTC day, **latched** — the stamp is written BEFORE the grant is attempted, mirroring
`DailyQuestRewardBridge.ClaimedAtUnix`.

### `Wallet/RewardGrantWriter.cs` (350) — static, the ONE fulfillment writer

`enum GrantOrigin` · `Grant(RewardGrant, GrantOrigin, string where)` · `Save(where)`. It dispatches
on the reward's **KIND** and never on a SKU, season id, tier index or day number — which is what
makes a new reward a data edit. It cannot grant combat power because no `RewardKind` expresses it.
⚠ **CAPPED vs PURCHASED is a real distinction, not a copy-paste slip:** EARNED rewards (every
battle-pass tier) route through the town-bank-capped `GrantSpendable` — a season tier is income and
income obeys storage. PAID rewards (every monthly-card drip) route through
`GrantSpendablePurchased`, the same uncapped seam `PackStoreVM` uses; silently shaving a paid drip
against a full store is a refund problem. The caller states which it is.
⚠ **Declared duplication:** the AppDomain reflection bridges are duplicated from `PackStoreVM`'s
private copies. No DECISION lives in either copy, which is the test the file applies.

### `Wallet/UI/SeasonTrackPanel.cs` (592) + `Wallet/UI/MonthlyLedgerPanel.cs` (501)

Code-built uGUI (**not UXML** — UXML renders empty in player builds; the dead `PackStore.uxml`/`.uss`
sitting next to them are the fossils of that lesson). Each registers a `PanelHandle` in `Awake`,
calls `NotifyOpened` in `OnEnable` and **closes itself when that returns false** (the WO-437
battle-lock refusal), and calls `NotifyClosed` in `OnDisable`.
- **Season Track** is TWO PARALLEL ROWS (free top, premium bottom), one column per tier, scrolling
  horizontally with the current tier centred on open — read DOWN a column to compare lanes at one
  tier, ACROSS a row to see a lane's arc. Four column states, each printing its **word** from
  canon-strings; READY is the only state that animates.
- **Monthly Ledger** is a 10x3 grid drawing **all thirty days at once, before the player has paid
  anything** — the "no hidden mystery day" promise made structural: there is no code path in the
  file that omits a cell. ⛔ **No countdown anywhere**; the header reads "N claims left".

### `Wallet/BattleMonthlyPanelsBootstrap.cs` (161) — the runtime DOOR

`OpenSeasonTrack()` · `OpenMonthlyLedger()` / `OpenMonthlyLedger(sku)`. Exists because two seats
built these screens independently: the surviving canon-compliant pair had exactly one defect —
**nothing registered them**, so `PanelRouter.Open(PanelId.BattlePass)` returned false and the
screens shipped unopenable. The retired rival (`BattleMonthlyPanels.cs`, 145 lines) was wired but
typed player-facing sentences INLINE and derived on-screen state words from `enum.ToString()`,
putting the developer identifier "PremiumLocked" on a player's screen — two CLAUDE.md §7 violations.
Its text is preserved under "RETIRED DUPLICATE" in the WO. New `PanelId`s: `BattlePass = 19`,
`MonthlyLedger = 20` (`Core/UI/PanelRouter.cs`).

### The Night Market (WO-1050) — store presentation redesign

- **`Wallet/NightMarketPalette.cs` (103)** — `For(StoreBand)` · `AllBandLights()` ·
  `Luma255(Color)` · `ParseTint(hex, fallback)`. It is a FILE and not four `new Color(...)` literals
  inside `PackStore` precisely so something can CHECK the colourblind rule: `NightMarketRegression`
  asserts the four band lights step apart in rec.709 greyscale and that every band also carries a
  text eyebrow and a mark. Identification order is **eyebrow > mark > greyscale value > hue**, and
  hue is the first thing allowed to be lost.
- **`Wallet/StoreAurora.cs` (403)** — `MonoBehaviour`; `AddDrift` / `AddSweep` (both return
  `RawImage`, via the new `ElarionUiKit.AddRawImage`) · `CrossfadeTo(Color)` ·
  `SetLightImmediate(Color)`. Exactly four motion moments: aurora drift (~22 s), a 400 ms light
  crossfade on selection, a 700 ms specular sweep across the CTA every 6 s, and a ~14 s patronage
  sheen. Three enforceable rules: **motion never carries meaning** (set `FeatureFlags.ReducedMotion`
  and nothing but movement is lost — that is the acceptance test); **nothing a player reads to
  decide ever moves** (no motion on prices, quantities, ledger bars, badges, the balance chip or the
  trust strip); and the budget stays on the spotlight, off the shelf's information.
- **`Wallet/StoreStrings.cs` (306)** — `Get` / `Format` / `Reload`, keys only; every buy-gate refusal
  gets its own sentence in `canon-strings.json` (both copies, ASCII-only), each saying what the
  player CAN still do, none of them saying "the flag is off". A missing key returns the visible
  `[[missing:key]]` marker and self-reports through `FlowTrace`. It is the FOURTH module-local
  strings twin (`CanonStrings`/Onboarding, `VillageStrings`/Village, `PromoStrings`/Core) — the
  asmdefs do not let one module reach another's reader, and the SENTENCE is not duplicated, only the
  twenty-line loader.
- **`Wallet/PurchaseGate.cs` (406) — MOVED** from `Village/Monetization/` (git rename, 57% similarity)
  into `DeNelle.Wallet`. Any doc or grep pointing at `Assets/_Modules/Village/Monetization/PurchaseGate.cs`
  is now wrong.
- New oracle `BuyGateAndPriceLadderRegression`; `ImpulsePackRegression` and
  `WalletProviderSelectionRegression` updated.

---

## 1. THE CURRENCY MAP (the one table to know)

| Currency | Backing store | Earn seam | Spend seam | Persists? |
|---|---|---|---|---|
| **Wood / Iron** | DUAL: `EconomyService._wood/_iron` in-session pool (`EconomyService.cs:114-115`) **AND** `GameState.Wood/Iron` (upgrade ledger) | `Grant` fills the pool AND mirrors to GameState (`EconomyService.cs:311-317`) | `TrySpend` deducts the POOL ONLY (`EconomyService.cs:281-282`) — **[FLAG 1]** | pool: no (session); GameState: yes |
| **Food** | `GameState.Resources.Food` read-through (`EconomyService.cs:129-136`) | `GameStateService.AddFood` via `Grant` (`:330`) | `AddFood(-n)` via `TrySpend` (`:284`) | yes |
| **Crystals** (Aether) | `GameState.Resources.Crystals` read-through (`EconomyService.cs:143-150`) | `AddCrystals` via `Grant` (`:333`), BattlePass rewards, packs | `AddCrystals(-n)` via `TrySpend` (`:286`) | yes |
| **Coins (Gold)** | `GameState.Resources.Coins` read-through (`EconomyService.cs:157-164`) | `AddCoins(+)` (`:464-474`) — shops/refunds/packs | `AddCoins(-)` via `TrySpend` (`:288`) | yes (AddCoins calls `gs.Save()` `:472`) |
| **Glimmer** | PlayerPrefs blob `dotr-cosmetics-v1` (`GlimmerCurrencyService.cs:59`) | `TryAddGlimmer` (`:193-206`) — quests, IAP top-up, packs | `TryPurchase` / `SpendGlimmer` (`:121-147`, `:214-226`) | yes (separate blob) — **[FLAG 3]** |
| **SKR / SOL / USDC** | On-chain (stubbed) — `WalletService` over `StubWalletProvider` by default | never earned in-game | `WalletService.Pay/PayFlat` (pack purchase, top-up) | n/a |

**Money-split doctrine (owner + covenant):** Crystals are an EARNABLE soft currency (battle-pass
rewards `BattlePassManager.cs:209`, pack economy grants, planned Echo trickle in WO-830 §3b);
real money enters only on the SKR/SOL/USDC rails via `WalletService`. Glimmer is a THIRD, separate
soft currency — "Crystals→Glimmer is not allowed" (`GlimmerCurrencyService.cs:23-25` header, from
cosmetic-shop-spec §2.3). Everything sellable is cosmetic / soft-currency / time-saving
convenience — enforced by the covenant oracle (§6).

---

## 2. EconomyService — `Assets/_Modules/Village/EconomyService.cs`  [LIVE]

`sealed MonoBehaviour : IEconomy` (`IEconomy` at `Assets/_Modules/Village/IEconomy.cs:24`),
`DeNelle.Village`, self-bootstrapping singleton (`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`
`EconomyService.cs:204-211`, DDOL).

- **Starting pool:** Wood 200, Iron 80 (Inspector, `:114-115`). Wood/Iron in-session pool resets
  on reload BY DESIGN (`:33-39` header) — but see the dual-wallet reality below.
- **API:** `CanAfford(ResourceCost)` (`:263-270`), `TrySpend(ResourceCost)` (`:278-291`),
  `Grant(ResourceCost)` (`:294-338`), `Grant(int,int,int,int)` (`:341-344`),
  `GrantSpendable(...)` dev/pack grant (`:361-380`), unified income seam
  `AddResource(ResourceType|MineResource, int)` (`:393-435`), `AddCoins(int delta)` (`:464-474`),
  event `OnChanged(ResourceSnapshot)` (`:200`, fired with subscriber-count FlowTrace `:476-483`).
- **`ResourceCost`** struct (`:71-100`): Wood/Food/Iron/Crystals + `Coins` appended last with
  default 0 so legacy 4-arg constructors compile unchanged (`:77-91`).
- **Deprecated Wood-only API kept:** `[Obsolete] CanAfford(int)` / `Spend(int)` (`:442-452`) —
  still compiled for TowerPlacementSystem/TowerUpgradeButton migration.
- **B2 dual-wallet convergence (Grant side only):** `Grant` mirrors Wood/Iron gains into
  `GameState.Wood/Iron` (`:311-317`, FlowTrace-proven `:318-325`) because the building-upgrade
  flow's ResourceLedger reads/spends GameState.Wood/Iron, not the pool. `GrantSpendable` no longer
  double-mirrors (`:363-368`) — it only adds `gs.Save()` + `ResourcesChanged.Invoke()` on top
  (`:374-379`).
- **HUD bridge:** subscribes `GameStateService.ResourcesChanged` → re-emits `OnChanged`
  (`AttachResourcesBridge` `:232-239`, retried at Start `:221-222`, detached `:241-258`) so
  GameState-side gains (harvest/empower/camp) refresh the village HUD.
- **Territory:** `SecuredOutpostCount` / `TerritoryMultiplier = 1 + 0.05·count` (`:176-184`),
  `OnOutpostSecured()` (`:191-195`) — fed by ClaimableCamp.
- **Tests:** `Assets/Tests/EditMode/EconomyServiceTests.cs` — starting defaults, grant/clamp,
  afford, atomic TrySpend, OnChanged, territory ramp (9 tests). None covers the Flag-1 asymmetry.

### ★ [FLAG 1] The TrySpend asymmetry landmine
`Grant` writes Wood/Iron to BOTH wallets (`:311-317`); **`TrySpend` deducts Wood/Iron from the
in-session pool ONLY** (`:281-282` — no GameState mirror), and the upgrade flow's ResourceLedger
spends GameState.Wood/Iron without touching the pool. Net effect: every earn credits both stores,
every spend debits only one → the two Wood/Iron views drift apart monotonically (the persisted
ledger inflates vs the pool, or vice-versa depending on which side spends). `CanAfford` also checks
the POOL for Wood/Iron (`:265-267`) but GameState for Food/Crystals/Coins — a mixed-authority
affordability check. Any future "why do I have more wood in the upgrade panel than the shop"
ticket lands here.

---

## 3. GlimmerCurrencyService — `Assets/_Modules/Cosmetics/GlimmerCurrencyService.cs`  [LIVE]

`sealed MonoBehaviour` singleton, `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` bootstrap
(`:82-89`), DDOL. Wallet + cosmetic ownership + per-category equip state.

- **Persistence:** PlayerPrefs key **`dotr-cosmetics-v1`** (`PrefKey` `:59`), Newtonsoft JSON blob
  `GlimmerSaveData {glimmer, ownedCosmetics[], equippedByCategory{}}` (`:41-49`).
  `StartingGlimmer = 25` (`:60`). Load-failure resets to fresh state with a `FlowTrace.Fail`
  ("purchases at risk", `:308`).
- **API:** `Glimmer` (`:71-74`), `Owns(id)` (`:101-106`), `EquippedFor(cat)` (`:109-114`),
  `TryPurchase(id)` (`:121-147`), `Equip(id)` (`:154-180`), `UnequipCategory` (`:183-190`),
  `TryAddGlimmer(n)` (`:193-206` — also reports `wildcard.earn-glimmer` daily-quest progress
  `:204`), `SpendGlimmer(n)` (`:214-226` — BattlePass premium debit), `GrantAchievement(id)`
  (`:233-247` — catalog-gated free grant), event `Changed` (`:65`).
- **Debit-grant invariant instrumented:** `TryPurchase` logs DEBIT and fails loudly on
  "DEBIT-WITHOUT-GRANT" (`:136-141`) — the highest-risk economy op is trace-proven.
- **`MarkCosmeticOwned(id)` (`:260-272`) — the ECON-02 bridge:** registers ownership
  **catalog-independently** (no `CosmeticCatalog.Find` gate), writing straight into the same
  `_ownedSet`/`OwnedCosmetics` backing `Owns()` reads. Exists because pack cosmetic SKUs (e.g.
  `cosmetic.founders-vow.hero-outfit`) are NOT `cosmetics.json` rows, so `GrantAchievement` and
  `TryPurchase` no-op on them. Called by `PackStoreVM.TryGrantCosmeticOwnership` via reflection
  (§5).
- **[FLAG 3] Store split-brain (bridged, not reconciled):** cosmetic ownership lives in TWO
  stores — `GameState.OwnedItemIds` (pack system's `IsOwned`, `PackStoreVM.cs:53-58`) and this
  PlayerPrefs blob (wardrobe/shop's `Owns`). ECON-02 dual-writes both on pack grant
  (`PackStoreVM.cs:111-124`), and the `PACK_COSMETIC_INTEGRITY` oracle holds it green — but the
  stores are never migrated/reconciled: clearing one (save wipe, cloud-restore of GameState only,
  PlayerPrefs clear) desyncs ownership silently.

---

## 4. Pack store — `Assets/_Modules/Wallet/` (PackStore / PackStoreVM / PackStoreBootstrap) + `Assets/_Modules/Commerce/` (PackCatalog / ShortfallPackOffer)

> ⚠ **WO-1282 (2026-08-30): `PackCatalog.cs` and `ShortfallPackOffer.cs` are under
> `Assets/_Modules/Commerce/` now, in assembly `DeNelle.Commerce`.** Their namespace is still
> `DeNelle.Wallet`. See the DELTA at the top of this file. Every cite below that names
> `_Modules/Wallet/PackCatalog.cs` should be read as `_Modules/Commerce/PackCatalog.cs`.

### packs.json — `Assets/Resources/Data/Canonical/packs.json` (+ byte-identical StreamingAssets copy)  [LIVE]
- **version 2, 13 packs, tiers 1–13** (`packs.json:17`, skus at `:21,37,54,72,91,111,128,145,162,180,198,215,232`):
  the 5 original ladder packs (hearth-spark $1.99 → founders-vow $49.99 `founderOnly:true` `:96`)
  + 8 starter bundles authored 2026-06-28 (frostfall / embergrove / bloomtide / starters-hand /
  echo-patron / hero-wardrobe / realm-defender / builders-cache), price-anchored to
  $4.99/$9.99/$19.99 (`_schemaNotes.pricing` `:11`). `tier` is a UNIQUE lookup key, not the price
  band (`:12`).
- `currencyDisclaimer: "Token price moves with the market."` (`:18`). Convenience kinds are
  time-saving only per `_schemaNotes.convenience` (`:13`). Every economy block carries
  glimmer+crystals+food+coins (some add wood/iron).
- Loader: `PackCatalog.cs` (`Packs`, `Find(sku)`, `FindByTier`, WebGL-safe via CanonicalJson).

### PackStore.cs  [LIVE — code-built uGUI, PanelRouter-opened]
- View only since WO-744. Header (`PackStore.cs:1-30`): WO-F conversion 2026-07-03 — UIDocument/
  UITK replaced with **code-built uGUI on the Obsidian master frame** (`ElarionUiKit.BuildObsidianModal`),
  lazily built on first open; open/close = SetActive (MarketplaceInteractor contract preserved).
  The old "store scene-wiring DISABLED pending PanelSettings" state is OBSOLETE — the store now
  opens via `PanelRouter.Open(PanelId.RealmStore)`.
- Default rail `CurrencyKind.Skr` (`:52`). Purchase flow: async UniTask → `WalletService.Pay` →
  on confirm `_vm.ApplyPackContents(pack)` (`:510-512`). Renders the covenant line
  *"You are never required to spend anything. Ever."* verbatim + treasury transparency +
  CurrencyDisclaimer (header `:20-25`). Registers a `PanelHandle` with PanelManager (`:60-64`).

### PackStoreVM.cs  [LIVE — the money/entitlement seam]
- `ApplyPackContents(PackDef)` (`:68-153`) — ECON-01: every advertised currency routes through its
  canonical persisted seam, each exactly once (`:82-107`): Glimmer→`TryAddGlimmer`,
  Wood/Iron/Food/Crystals→`EconomyService.GrantSpendable`, Coins→`AddCoins`. Ownership: pack SKU +
  every cosmetic SKU into `GameState.OwnedItemIds` (`RecordOwned` `:111-116`, `:155-159`) AND —
  ECON-02 — into GlimmerCurrencyService via `GrantAchievement` (catalog-gated, harmless no-op) +
  the load-bearing `MarkCosmeticOwned` (`TryGrantCosmeticOwnership` `:213-242`).
- All cross-asmdef grants go by AppDomain type-name reflection (Wallet can't ref Cosmetics/Village
  — `:161-181`); **every miss is a `FlowTrace.Fail` naming a LOST paid entitlement**
  (`:189,195,218,235,250,256` etc.). Post-grant ownership is verified before Save (`:132-137`) and
  a single proof line logs every delta (`:142-145`).
- Also owns the store-close resolve: `CloseViaInteractor` → reflection `MarketplaceInteractor.
  CloseStore` + `ReEnableDisabledHeroLocomotion` soft-lock fallback (`:290-358`).

### PackStoreBootstrap.cs  [LIVE]
- Static, `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` registers the
  `PanelId.RealmStore` opener with PanelRouter (`:43-48`); find-or-spawns the PackStore host on
  first open (no scene dependency); demo URL trigger `?realmstore=1` auto-opens once per session
  (`:33-36`, hook `:50-58`).

### Regression oracles (all wired into `DataRegression.RunAll`)
- **`PackGrantRegression.cs`** [pack-grant] — drives the REAL `ApplyPackContents('founders-vow')`
  over real singletons + throwaway GameState; asserts Glimmer +1000, crystals/food/coins by
  packs.json amounts, all 5 cosmetic SKUs `Owns()==true`. Marker `PACK_GRANT_OK` (header `:1-18`).
- **`PackCosmeticIntegrityRegression.cs`** [pack-cosmetic-integrity] — generalizes to EVERY pack:
  every advertised cosmetic SKU must end up owned post-grant ("advertised ⇒ grantable", explicitly
  NOT "in cosmetics.json" — header `:9-26`). Marker `PACK_COSMETIC_INTEGRITY_OK`.
- **[FLAG 4] Convenience tokens still not applied** — `ApplyPackContents` records the SKU but
  grants no token inventory ("no token tray yet; Week-8 inventory pass" `PackStoreVM.cs:126-128`).
  Every pack sells convenience counts that do nothing at runtime.

---

## 5. Battle pass — `Assets/_Modules/Cosmetics/BattlePassManager.cs`  [LIVE code, INERT without SO]

> ⚠ **STILL TRUE, BUT NO LONGER THE ONLY BATTLE PASS (2026-08-21).** `DeNelle.Wallet.BattlePassService`
> is a second, data-driven runtime and the two are UNRECONCILED by an explicit owner-decision flag in
> its header. See the DELTA at the top of this file before treating either as "the" battle pass.

- Singleton MonoBehaviour, DDOL (`:81-95`). **Persists to raw PlayerPrefs ints:** `BP_Level`,
  `BP_XP`, `BP_HasPremium` (`:292-294`, save/load `:296-309`) — the third unreconciled store
  (§7).
- `xpPerLevel = 800` (`:63`), `premiumCostGlimmer = 2400` (`:67`), season "Season 1 - Shadow
  Realms" (`:53`). `AddXP` loops tier-ups + `GrantReward` per tier (`:108-123`).
  `PurchasePremiumPass` debits via `SpendGlimmer` then back-dates all premium tiers (`:130-164`,
  debit/grant FlowTrace-paired `:146-162`).
- Reward kinds (`ApplyReward` `:195-238`): Crystals → `GameStateService.AddCrystals` (`:209`);
  Cosmetic → `GlimmerCurrencyService.GrantAchievement` (`:221` — catalog-gated, so a pass reward
  SKU must exist in cosmetics.json); **Resource → log-only "hook pending"** (`:228-232`)
  **[FLAG 5]**. Level-up VFX via reflection to `DeNelle.Village.LevelUpVFXController` (`:261-288`).
- **[FLAG 5] Requires an authored `BattlePassData` SO in the Inspector or it is a warn-and-no-op**
  (`:92-94`) — and nothing auto-spawns/wires this component; no scene/bootstrap reference was
  found in this pass. The battle pass is effectively parked.

---

## 6. Monetization covenant oracle — `Assets/Editor/Regression/MonetizationCovenantRegression.cs`  [LIVE gate]

Editor-only, wired into `DataRegression.RunAll` (`DataRegression.cs:258-259`, tag `[covenant]`).
Enforces the covenant (monetization-v2-spec §2/§5.3): sellables are cosmetic / soft-currency /
time-saving convenience — never combat power, never a stat, never RNG.

- Sweeps 6 monetization JSONs (`:92-100`): both `packs.json` copies, `skr_store.json`,
  `skr_staking.json`, `battle_monthly_packs.sample.json`, `WorkOrders/economy_store_packs.sample.json`.
  Missing file = skip-with-note, never throw (`:147-151`).
- FAILS on: banned kind strings (combat/stat/damage/buff… `:60-65`), non-zero combat-stat fields
  on a sellable (`:68-73`, check `:201-203`), any probability/odds/roll/chance/random field — the
  no-gacha rule (`:76-79`, `:193-199`), or a convenience/grant kind outside the allowlist
  (`:215-218`).
- Allowlists are DERIVED live from `skr_staking.json` (`convenienceAllowList` + `perkKindEnum`,
  `:122-137`) unioned with the documented sets (`:42-57`) — the JSON is the single source of truth.

---

## 7. [FLAG 3/5] The three unreconciled persistence stores

> ⚠ **THREE IS NOW FOUR (2026-08-21):** `BattlePassService` + `MonthlyCardService` add PlayerPrefs
> state of their own (deliberately, to avoid a schema bump on a live published game). See the DELTA.

| Store | Holds | Owner |
|---|---|---|
| `GameStateService` save (`dotr-save`; cloud-synced via Neon `/api/game/save`) | Resources (Food/Crystals/Coins), GameState.Wood/Iron ledger, `OwnedItemIds` (pack SKUs + pack cosmetics) | Core |
| PlayerPrefs `dotr-cosmetics-v1` | Glimmer balance, cosmetic ownership set, equips | GlimmerCurrencyService |
| PlayerPrefs `BP_Level` / `BP_XP` / `BP_HasPremium` | Battle-pass progress + premium flag | BattlePassManager |

Cosmetic ownership is dual-written across stores 1+2 (ECON-02) but never migrated or
cross-checked at load; Glimmer and battle-pass state never reach the cloud save. A
wallet-keyed cloud restore (BoundWallet identity) restores store 1 only — paid Glimmer/premium
state stays on-device.

---

## 8. Wallet / Web3 surfaces (spot-re-verified 2026-08-02)

- **`WalletService` + `StubWalletProvider`** [LIVE] — default provider is still the devnet stub;
  `SOLANA_SDK` flips only when the Solana Unity SDK package resolves. Nothing transacts on-chain
  today.
- **`CryptoPaymentManager`** [LIVE bridge] — SOL/SKR/USDC top-ups → `WalletService.PayFlat`;
  success grants Glimmer by reflection (`GlimmerCurrencyService.TryAddGlimmer` — the landing point
  named in `GlimmerCurrencyService.cs:200`).
- **[FLAG 6] `WalletConnectDialog.cs` — UXML-DEAD panel.** Still binds real UXML by element name
  (`rootVisualElement.Q<Button>("wallet-connect-button")` etc., `:115-128`). UXML renders empty in
  player builds (CLAUDE.md §8 hard rule) → the dialog is headless in a build; only its plain C#
  `Connect()/Disconnect()` API works.
- **[FLAG 6] Jupiter swap (`Assets/_Modules/Web3/`) — UXML-dead + stub-signed + net-contradicted.**
  `JupiterSwapService.cs` still targets the MAINNET public aggregator (`quote-api.jup.ag/v6/*`
  `:51-52`) while the wallet stack is devnet; `_skrMint` is still the placeholder
  `"REPLACE_WITH_SKR_MINT_ADDRESS"` (`:65`, guarded `:155`); swap signing is `WalletBridgeStub`
  (fake sig in dev, LogError in release). `JupiterSwapPanelController` drives
  `JupiterSwapPanel.uxml` by name → empty in a player build. The whole swap surface is
  demo-plumbing, not shippable.
- SKR mints in `WalletEndpoints` remain empty strings (integrator-fill) — SKR transfers fail
  cleanly; SKR pack pricing works via the stub.

---

## 9. WO-830 — Echo harvest-affinity economy  [PENDING — spec only, NOT in code]

`WorkOrders/WORK_ORDER_830_echo_harvest_affinity_synergy.md`, status READY TO IMPLEMENT
(owner-approved 2026-08-01; **Repairs affinity REMOVED by owner ruling 2026-08-02** — banner at
`:5-10`). Nothing below exists in the tree yet; do not catalog it as live.

- **Six harvest affinities** (spec §3a table `:57-64`): Elowen→Wood, Doran→Iron, Aldwin→Food,
  Corvin→Gold(Coins), **Bran→Crystals AND Maren→Crystals — Crystals is the one deliberately
  DOUBLED affinity**; all six get `PreferredLane = Harvest`.
- **Dump credits per affinity** (§3b): Gold via `EconomyService.AddCoins`, Crystals via
  `AddCrystals`/`Grant` — with the OWNER CONFIRM constraint that the COMBINED Bran+Maren crystal
  trickle stays the slowest income of the six (crystals are earnable but must never become a fast
  faucet — monetization guard, §7 of the WO).
- **Three disclosed pair-synergies** (§3c): Provisions (Elowen+Aldwin), Forge (Doran+Maren),
  Fortune (Corvin+Bran) — populate `echoes-balance.json` `crossBonuses`.
- **Hidden tri-synergy** (§3d): all three pairs running → undisclosed flat `hiddenTriSynergyBonus`
  (default 0.25) applied in `AggregateHarvestMultiplier` only; MUST be excluded from every
  displayed `+%` (applied ≠ displayed, FlowTrace-proven).
- Also in scope: silo capacity/rate reconciliation (`EchoService.cs:148` caveat), balance re-tune
  (`preferredLaneMatchBonus` 0.75 → ~0.35-0.45), `EchoSpecializationRegression` rewrite, both
  `echoes-balance.json` copies byte-identical.
- **When implemented, this file's §1 currency map gains a sixth earn seam (Echo silo Dump) for
  Wood/Iron/Food/Gold/Crystals — update in the same commit (§15 canon rule).**

---

## RISK LEDGER (prioritized)

1. **TrySpend/Grant dual-wallet asymmetry (Wood/Iron)** — earns mirror to both stores, spends
   debit only one; GameState ledger and in-session pool drift monotonically; `CanAfford` is
   mixed-authority (`EconomyService.cs:263-291` vs `:311-317`). No oracle covers the round-trip.
2. **WO-830 lands new money faucets (Gold + doubled Crystals) into this exact seam** — the Dump
   path must use `AddCoins`/`AddCrystals` (GameState-authoritative) and NOT the Wood/Iron pool
   path, or Flag 1 widens. Spec already warns (`WO-830 §7` "do NOT credit AetherCrystal via the
   old GrantSpendable(wood,food,iron) overload").
3. **Cosmetic-ownership split-brain is bridged, not unified** — dual-write via `MarkCosmeticOwned`
   (ECON-02) holds only while both stores survive together; cloud restore / PlayerPrefs wipe
   desyncs paid entitlements (§7).
4. **Convenience tokens: sold but inert** — all 13 packs advertise counts that grant nothing
   (`PackStoreVM.cs:126-128`). Covenant-clean but a shipped-lie risk on the pack card.
5. **Battle pass is parked** — nothing spawns `BattlePassManager`, no `BattlePassData` SO exists,
   Resource rewards are log-only; `BP_*` PlayerPrefs schema is live code awaiting a product
   decision.
6. **Wallet-connect + Jupiter panels are UXML-dead in builds**; Jupiter is additionally
   mainnet-vs-devnet contradicted, stub-signed, and mintless. Anything demoing "real wallet"
   flows must go through PackStore (code-built) or the Solana Mobile SDK work (WO-766), not these.
7. **Glimmer wallet is PlayerPrefs-fragile** — a corrupt blob resets to 25 Glimmer with purchases
   lost (guarded by a FlowTrace.Fail, but no backup/repair path; `GlimmerCurrencyService.cs:298-313`).
