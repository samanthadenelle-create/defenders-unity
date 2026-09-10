# WORK ORDER 1681 — HEART-008: Heartbound UI / UX — the panel and its one door

**Status:** SPEC
**Silo:** UI (code-built panel + one deck card). No backend, no gameplay, no scene files.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:807-898` (HEART-008).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW panel; the shell is fully determined; the PLAY-CHANNEL question is not**

### 0a. ⛔ THE PANEL CANNOT COMPILE INTO A GOOGLE PLAY ARTIFACT. This is a packaging gate, not a flag.

`Assets/Editor/Regression/GooglePlayPackagingGate.cs:52-59` lists forbidden tokens in the **built artifact**, including `"stake.solanamobile"` and the live SKR mint. `Assets/Editor/GooglePlayContentExclusion.cs:410` sweeps the token set `{ "solana", "jupiter", "$skr", " skr", "usdc", "crypto", "web3", "wallet" }`. `AndroidBuild.cs:199-204` **fails the build** (`PLAY_ARTIFACT_REJECTED`, `EditorApplication.Exit(1)`) when the gate finds forbidden surface — and the gate's own header at `:23-25` records that it once passed an artifact carrying **solana ×74, SKR ×35**.

**The spec's Trust Statement alone would trip it.** Spec `:859-863`:
> *"Your SKR remains staked through Solana Mobile. Echoes of Elarion reads your public staking status and never takes custody of your SKR."*

That string contains `SKR` and `Solana`. Compiled into a Play artifact, it is a build failure.

**So the panel lives in `DeNelle.Wallet`** — `Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:21-23`, `"defineConstraints": ["!GOOGLE_PLAY"]`. That constraint takes the **entire assembly** out of the Play build and is a stronger guarantee than any runtime flag.

⚠ **And the DOOR must be channel-gated too, not just the panel.** A locked "Heartbound" deck card visible on Play is precisely the crypto-marketing surface `SkrPreview` was flipped OFF to avoid — `Assets/_Modules/Core/FeatureFlags.cs:638-641` records the store-hardening reason. ⭐ **Channel-conditional doors already exist:** `Assets/Editor/Regression/RealmStoreSingleRegistrarRegression.cs:210` pins that *"the Night Market has no door in a DAPP_STORE artifact"*, and `:217` counts Play-side carriage. **Mirror how the Realm Store card is gated — do not design a new mechanism.**

### 0b. ⭐ The panel shell is fully determined — four files, one worked example

Read at source. A new full-screen panel touches exactly:
1. **`Assets/_Modules/Core/UI/PanelRouter.cs`** — append one `PanelId` member. The enum is at `:37`; the highest live value is `HonestFeedback = 27` (`:182`), so **the next free value is 28**. ⛔ It is append-only and values are load-bearing (stated at `:121`, `:137`, `:150`, `:164`); never renumber.
2. **The panel class** — `MonoBehaviour`, `PanelRouter.Register` in `Awake`, `Unregister` in `OnDestroy`. Worked example: `Assets/_Modules/Village/UI/Manage/HeartPanel.cs:49`, registers both overloads at `:86-91`, unregisters `:93`, `Open()` `:112`, chrome via `ElarionUiKit.BuildModalCanvas` `:154` / `BuildObsidianPanel` `:159` (its failure at `:165` is a `FlowTrace.Fail`, not a silent null), body under `Guard.Try` `:248`.
3. **A bootstrap** — `Assets/_Modules/Village/UI/Manage/HeartPanelBootstrap.cs:26`, `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` `:31`, idempotent `:34-41`, `DontDestroyOnLoad`. Its header `:12-14` is the law: *"A panel with no spawner is a panel with no door… deleting it silently retires PanelId.Heart."*
4. **The door** — one `Route(...)` row in `Assets/_Modules/HUD/PlayerDeckWorkspace.cs`, factory at `:717-728`. `Available = () => PanelRouter.IsRegistered(target)`, so a card whose panel never registered renders **locked, never dead**.

⛔ **UXML does not work in builds** (CLAUDE.md §8). Code-built only.

### 0c. ⛔ The door is a DECK CARD, never a dock face
CLAUDE.md §7: the bottom bar the player touches is the adaptive peaceful dock, its face count is oracle-driven (`HudActionBarRegression.CheckMeasuredPeacefulDock`), and **no reasoning about it may start from a constant**. New panels get deck cards — that is the sanctioned pattern, and §7 records the Realm Map's dormant-second-door scar as the reason. Spec `:814-818` agrees from its own direction: *"Add a Heartbound access point near the Tree of Life and/or appropriate existing Web3/wallet surface. Do NOT turn the main HUD into a crypto dashboard."*

### 0d. ⚠ A stake panel ALREADY EXISTS
`Assets/_Modules/Core/UI/StakeRewardsPanel.cs` renders the current stake standing and the tier ladder, with `Assets/_Modules/Core/UI/Mvvm/StakeRewardsVM.cs` behind it and `Assets/_Modules/Core/UI/SkrShowcasePanel.cs` beside it. **A second stake panel over the same wallet is WO-1675 Q-LADDER's question surfacing as UI.**

---

## 1. Deliverables

- **D1** — `PanelId.Heartbound = 28`, the panel, its bootstrap, its deck card. All four files (§0b), channel-gated per §0a.
- **D2** — Content per spec `:821-841`: verified staked SKR, effective resonating SKR, resonance score, current tier, streak, total pulses, current passive effects, next-tier progress, last pulse, latest Echo Event.
- **D3** — The four states, each with its exact copy from the spec:
  - **Normal** — `:823-829`.
  - **Effective-stake tooltip** `:843-853`: *"Your bond strengthens over successive Heart Pulses. New stake begins resonating immediately and reaches full strength over time."* ⭐ `:853`: *"Do not expose the anti-exploit implementation as punitive language."*
  - **No stake** `:873-883`: **"The Heart is Silent"**, *"No native SKR stake was detected for this wallet."* ⛔ `:865`: *"Do not show a giant BUY button."*
  - **Unstaking** `:885-891`: **"The Echo Weakens"**.
  - **RPC failure** `:893-898`: ⛔ *"Never display `0 SKR` because RPC failed."* Show last known + **"Verification temporarily unavailable"** + `Last verified: <time>`.
- **D4** — ⛔ **No arithmetic in the panel.** Spec `:1274`: *"UI contains no staking calculations."* Next-tier progress comes from the status endpoint (WO-1676 Q-CONFIG), not from a client-side threshold table.
- **D5** — Localization. Every string through the locale pipeline; ⚠ `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json:27` already exists precisely because a Seeker subtitle names SKR and Play needs a variant. And `HeartfireRegression`'s ASCII lint (`Assets/Editor/Regression/HeartfireRegression.cs:320-328`) records the general trap: **non-ASCII renders as tofu on the mobile font atlas.**

---

## 2. Acceptance

1. `UI_CAPTURE_OK` with the PNGs **OPENED**, at 2670x1200 landscape (memory `owner-rulings-2026-09-10-morning`: **LANDSCAPE ONLY**).
2. Every touch target ≥ `ElarionUiKit.MinTouchPx` (112) **as authored**, not rescued by the runtime clamp — WO-1664 is the standing ticket for exactly that failure and `LayoutOracle.cs:235-241` explains why a rescued face spills into its neighbours.
3. Every label fits at every aspect — the WO-1662/1663 class of defect.
4. All four states captured, including the RPC-failure state showing last-known rather than zero.
5. **A regression proving no Heartbound string reaches a `GOOGLE_PLAY` artifact.** §0a — and note this is the ticket where WO-1674 D5's missing `StakingComplianceRegression` finally has real work to do.
6. `COMPILE_GATE_OK`, `gate_brace.py` clean, zero NUL bytes.
7. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 3. Dependencies

**Blocked by:** WO-1675 (state to display), WO-1676 (score/tier + `nextTierAt`), WO-1679 (the "current passive effects" list), and Q-PLAY / Q-LADDER below.
**Blocks:** WO-1686.

---

## 4. ⛔ OWNER QUESTIONS

### Q-PLAY. On the Google Play artifact, does Heartbound exist at all — panel, card, or neither?
§0a proves the **panel** cannot compile in. The **card** is a separate decision:
- **(a) Neither.** A Play player never sees the word. Cleanest against the packaging gate and consistent with `SkrPreview` being flipped off for store hardening (`FeatureFlags.cs:638-641`).
- **(b) A locked card with non-crypto copy** ("A bond with the Heart, on Seeker devices"). ⚠ Any copy naming SKR, Solana, staking or a wallet trips `GooglePlayContentExclusion.cs:410`'s sweep, so this is a narrow needle.
- **(c) A card that explains the Seeker feature.** That is crypto marketing in a Play artifact — the thing store hardening removed.

**Recommend (a).** But it is the owner's, because it decides whether a Play player is ever told the feature exists. **Related and already open: WO-1674 Q3 — a Play/guest player has no wallet and no mechanism to attach one, so they could not participate even if they saw it.**

### Q-LADDER (restated from WO-1675, and this is where the player would see it). Two stake panels, or one?
`StakeRewardsPanel` already renders a stake→tier→rewards ladder for the same wallet (§0d). Shipping the Heartbound panel beside it shows the player **two tier numbers for one stake**. Fold, replace, or run both — a ruling, not a preference.

---

## 5. What NOT to touch

- ⛔ **The peaceful dock.** §0c. No bar face, no `ActionBarButtonId`, no reasoning from `HudActionBarModel.ButtonCount`. CLAUDE.md §7 records three separate occasions on which a written face count went stale.
- ⛔ **`PanelId` values.** Append 28; never renumber. Load-bearing, stated four times in the enum's own comments.
- ⛔ **`DeNelle.Wallet.asmdef`'s `!GOOGLE_PLAY` constraint** and **`GooglePlayContentExclusion`'s token sweep**. Widening either to let the panel through is a compliance decision, never a build fix.
- ⛔ **UXML.** Code-built only (CLAUDE.md §8).
- ⛔ **`StakeRewardsPanel` / `SkrShowcasePanel`** until Q-LADDER is ruled. `SkrShowcasePanel` is behind `ff.skrpreview`, default false (`FeatureFlags.cs:642`), and it was turned off deliberately.
- ⛔ **Any client-side tier arithmetic.** D4.
- No backend. No `.unity` scene files. No `SaveSchema` change.

---

## 6. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`Assets/_Modules/Core/UI/PanelRouter.cs:37,121,137,150,164,182,192,194,204,213,221,291,304,314,321,333`
`Assets/_Modules/Village/UI/Manage/HeartPanel.cs:49,86-93,112,152-165,248`; `HeartPanelBootstrap.cs:12-14,26,31,34-41`
`Assets/_Modules/HUD/PlayerDeckWorkspace.cs:12,21,58-59,690-712,717-728,730,864-867`
`Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:21-23`
`Assets/Editor/Regression/GooglePlayPackagingGate.cs:23-25,52-59,71-77`; `Assets/Editor/GooglePlayContentExclusion.cs:145-148,304,410`; `Assets/Editor/AndroidBuild.cs:199-204,237-238`
`Assets/Editor/Regression/RealmStoreSingleRegistrarRegression.cs:210,217`
`Assets/_Modules/Core/FeatureFlags.cs:638-641,642`
`Assets/_Modules/Core/UI/StakeRewardsPanel.cs`; `Assets/_Modules/Core/UI/Mvvm/StakeRewardsVM.cs`; `Assets/_Modules/Core/UI/SkrShowcasePanel.cs`
`Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json:27`; `Assets/Editor/Regression/HeartfireRegression.cs:320-328`
`WorkOrders/WORK_ORDER_1664_five_controls_authored_under_the_touch_floor_rely_on_the_clamp.md` (the touch-floor precedent, `LayoutOracle.cs:235-241`)
