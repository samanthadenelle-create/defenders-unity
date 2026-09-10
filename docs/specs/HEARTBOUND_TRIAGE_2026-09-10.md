# HEARTBOUND SKR RESONANCE — TRIAGE INDEX, DEPENDENCY ORDER, AND THE OWNER-QUESTION LIST

**Lane:** HEARTBOUND-TRIAGE (read-only QA triage per CLAUDE.md §13 — classify, spec, never RCA-fix, never write code).
**Date:** 2026-09-10.
**Tree every line number in every one of these thirteen WOs was read against:** worktree at `dev` **`abbeb9362d73dc6ecb851a3727894cb29f21779f`** (`git merge --ff-only refs/heads/dev`, run first).
**Source spec:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md` — 1347 lines, sections at `:12` (PRODUCT RULES), `:31/:183/:260/:376/:469/:617/:726/:807/:899/:953/:1030/:1101/:1162` (HEART-001..013), `:1228` (IMPLEMENTATION ORDER), `:1291` (ARCHITECTURAL RULE), `:1326` (DEFINITION OF DONE).

> ⚠ **Spec provenance, checked rather than inherited.** The lane brief said the spec was "not on dev yet". It **is** on dev at `abbeb9362`: `git diff` and `git diff -w` against that commit both return **empty** — the `M` in `git status` is a CRLF line-ending artifact only. So every `spec :NNN` reference in these WOs points at the committed file. ⚠ **Scope of that claim, stated honestly:** the section headings and the ~30 load-bearing verbatim quotes were located with `grep -n` and corrected against it (several first-pass interior refs were off by a few lines and were fixed). Interior refs not on that list are within-section and approximate — **re-grep at source before quoting one** (CLAUDE.md §11B).

**Nothing in this lane touched code, Unity, or git beyond the ff-only merge. No commits.**

---

## 0. THE FOUR THINGS THAT CHANGE THE SHAPE OF THE WHOLE FEATURE

Ordered by blast radius. Each is proven at source, and each is a finding the spec did not anticipate.

### 0.1 ⛔ THE CHAIN READ ALREADY SHIPS — IN THE CLIENT — AND THAT VIOLATES PRODUCT RULE 7

`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs` already does, on the device, exactly what HEART-001 specifies: the same program id (`:25`), StakeConfig (`:26`), guardian pool (`:27`), the same `["user_stake", config, user, guardian]` PDA seeds (`:52-54`), the same `shares × sharePrice / 1e9` math (`:75`), the same 6 decimals (`:29`), against mainnet (`:57`).

Product rule 7 (spec `:22`): *"The Unity client must never be trusted to report the amount of SKR staked."*

It is trusted today. `StakeRewardsResolver.Query` is a **public settable property** (`Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:175-183`), so a patched build reports any number. That has been *harmless* because the only consumer grants polish **attempts** (`Assets/_Modules/Core/Catalog/PolishBonusProvider.cs:70-78`) — never odds, never resources. **HEART-005 makes it grant resources, and the tolerance ends there.**

⇒ **HEART-001 is a RELOCATION, not an invention.** WO-1674 Q1 asks which of the two reads survives.

### 0.2 ⛔ THE SAVE IS CLIENT-AUTHORED, SO "BACKEND AUTHORITATIVE" CANNOT LIVE IN IT

`api/game/save.js:70-72`: *"soft currency stays client-owned, with server guards… Anti-grief / anti-corruption ceilings, **NOT a server-authoritative economy**."*
`api/game/save.js:410-421`: *"farming / raiding / dungeons / arena have NO per-action endpoint. They are simulated entirely on the client… It is a RECORD, NOT A CONTROL. Do not mistake it for enforcement."*

⇒ Heartbound state and every claim ledger is a **Neon table**, never a save field. ⇒ **`SaveSchema.CurrentVersion` (read at source: `Assets/_Modules/Core/State/SaveSchema.cs:41`, value **41**) DOES NOT BUMP**, and that is the correct answer, not a shortcut (WO-1675 §0b).
⇒ And economic **modifiers** are applied client-side regardless, so product rule 6 covers *deciding* a reward and not *applying* a rate (WO-1682 Q-CLIENTECON).

### 0.3 ⛔ THE WALLET **IS** THE PLAYER ID — SO TWO SPEC SECTIONS DO NOT MAP

`api/schema.sql:60` — `player_id TEXT PRIMARY KEY, -- BoundWallet address`. `api/_lib/wallet-auth.js:129` routes a base58 id straight to ed25519 verification, and `:27-29` records the owner ruling: *"THE WALLET REMAINS THE SOLE IDENTITY ON THE SEEKER/APK ARTIFACT."* **No linkage table exists** (`grep -n "link" api/schema.sql` → only `social_links`, `:902`).

⇒ HEART-002's "Wallet Change" (spec `:235-247`) and HEART-010's "Wallet switching" (`:997-1003`) presume a player who outlives a wallet. There is none. *"Retains lifetime cosmetic achievements"* has nothing to retain them on (WO-1675 Q-WALLET, WO-1683 §1).
⇒ And `guest-local-*` / `play-*` players have **no wallet and no mechanism to attach one**, so Heartbound is a **Seeker-artifact feature**. That is consistent with product rule 12 (playable without SKR) but the spec never says it (WO-1674 Q3).

### 0.4 ⛔ THREE COMPLIANCE GATES IN THIS EXACT FEATURE AREA ARE CITED AS PROTECTION AND HAVE NEVER EXISTED

| Claimed suite | Cited at | Search | Result |
|---|---|---|---|
| `StakingComplianceRegression` | `Assets/_Modules/Core/FeatureFlags.cs:1008` — *"Pinned by StakingComplianceRegression"*, on the `DAPP_STORE` compile-off of `StakingPolishBonus` | `grep -rn "StakingComplianceRegression" .` | **3 hits, all prose. No definition.** |
| `JewelPolishRegression` | `Assets/_Modules/Village/Crafting/JewelPolishService.cs:425` | WO-1673 §0 item 1 | **1 hit — its own claim** |
| `SkrStakingRegression` | `Assets/StreamingAssets/Data/Canonical/skr_staking.json:1` `_comment` | conceded in `MonetizationCovenantRegression`'s header | **never existed** |

Three imaginary firewalls in one feature area is a pattern. The **first** is the worst: it sits on the flag that decides whether token-gated gameplay compiles into a Google Play artifact. Closing it is **WO-1674 D5**; **WO-1685 §0d** is where "the suite exists" must become verifiable.

⚠ **What actually pins the Play boundary today** (cite these, not the imaginary suite): `AndroidBuild.cs:237-238` (the channel stamps), `GooglePlayPackagingRegression.cs:40-41` (asserts them), `GooglePlayPackagingGate.cs:191` (forbids persistent defines) and `:52-59` (forbids `stake.solanamobile` + the live mint **in the built artifact**), plus `Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:21-23` — `"defineConstraints": ["!GOOGLE_PLAY"]`, which takes the **whole Wallet assembly** out of a Play build and is stronger than any flag.

---

## 1. THE THIRTEEN WORK ORDERS

Numbers **pre-assigned 1674..1686 in spec order** by the coordinator. ⛔ `CLI_LANES_WO_NUMBERS.md` was deliberately **NOT** edited by this lane — the lead bumps the banner.

| WO | HEART | Title | Status | Classification |
|---|---|---|---|---|
| **1674** | 001 | Native SKR staking verification layer | **SPEC** | **EXISTING, on the wrong side of rule 7** (§0.1) |
| **1675** | 002 | Heartbound state and persistence | **SPEC** | NEW table; **no save bump**; a rival ladder exists |
| **1676** | 003 | Resonance mathematics and anti-whale curve | **READY TO IMPLEMENT** | NEW pure arithmetic |
| **1677** | 004 | Native SKR Heart Pulse detector | **SPEC** | NEW; **cadence unmeasured**, no `MIN_PULSE_INTERVAL` |
| **1678** | 005 | Echo Event engine | **SPEC** | NEW engine; **7 of 9 events land on ruled/pinned systems** |
| **1679** | 006 | Ten-tier passive benefit system | **SPEC** | NEW over a mostly-unconsumed modifier layer |
| **1680** | 007 | Heart / Tree world integration | **SPEC** | **GREENFIELD** — no stage system exists |
| **1681** | 008 | Heartbound UI / UX panel and door | **SPEC** | NEW panel; shell determined; **Play channel is not** |
| **1682** | 009 | Passive economy guardrails | **SPEC** | NEW ceiling; **the ceiling has no meter** |
| **1683** | 010 | Exploit protection and failure handling | **SPEC** | 8 of 10 covered; **1 unwriteable, 1 false today** |
| **1684** | 011 | Telemetry and balance analytics | **READY TO IMPLEMENT** | NEW events on a determined pipeline |
| **1685** | 012 | Regression and automated test package | **READY TO IMPLEMENT** | NEW tests on 3 determined frameworks |
| **1686** | 013 | Grant / reviewer demo path | **SPEC** | NEW; **the exclusion mechanism is the ticket** |

**Three READY, ten SPEC.** The three READY ones are READY because they need **no ruling**: 1676 is a pure function with the spec's own test vectors; 1684 is fourteen `EventTracker.Track` / `logApiEvent` calls on a pipeline read end-to-end; 1685 is a test map over three frameworks whose markers and registration sites were read at source. **Each still carries an explicit "Blocked by" line** — READY means the seam is determined, not that the inputs exist yet.

Paths, all under `WorkOrders/`:
```
WORK_ORDER_1674_heart_001_native_skr_staking_verification_layer.md
WORK_ORDER_1675_heart_002_heartbound_state_and_persistence.md
WORK_ORDER_1676_heart_003_resonance_mathematics_and_anti_whale_curve.md
WORK_ORDER_1677_heart_004_native_skr_heart_pulse_detector.md
WORK_ORDER_1678_heart_005_echo_event_engine.md
WORK_ORDER_1679_heart_006_ten_tier_passive_benefit_system.md
WORK_ORDER_1680_heart_007_heart_world_integration_and_resonance_visuals.md
WORK_ORDER_1681_heart_008_heartbound_ui_ux_panel_and_door.md
WORK_ORDER_1682_heart_009_passive_economy_guardrails.md
WORK_ORDER_1683_heart_010_exploit_protection_and_failure_handling.md
WORK_ORDER_1684_heart_011_telemetry_and_balance_analytics.md
WORK_ORDER_1685_heart_012_regression_and_automated_test_package.md
WORK_ORDER_1686_heart_013_grant_reviewer_demo_path.md
```

---

## 2. DEPENDENCY ORDER

The spec's own waves (`:1228-1290`) are sound and are kept. Two amendments, both forced by evidence:

```
                    ┌──────────────────────────────────────────────┐
   WAVE 0 (NEW)     │ measure the share-price cadence (1677 §0a)   │
   evidence first   │ answer Q1 · Q2 · Q3 · Q-WALLET · Q-P2W       │
                    └──────────────────┬───────────────────────────┘
                                       │
   WAVE 1            1674 ──► 1675 ──► 1676        (+1685's test MAP, +1684 as each lands)
   foundation        verify   persist  arithmetic
                                       │
   WAVE 2            1677 ──► 1678 ──► 1683
   the heart beats   pulse    events   adversarial
                                       │
   WAVE 3            1679 ◄──► 1682                (mutually gating: the tiers and their ceiling)
   gameplay          tiers     guardrails
                                       │
   WAVE 4            1680 ──► 1681
   presentation      visuals   panel
                                       │
   WAVE 5            1684 (complete) + 1685 (complete)
                                       │
   WAVE 6            1686  reviewer demo
```

**Amendment A — a WAVE 0 that the spec does not have.** CLAUDE.md §12 is binding: *no code edit on a non-trivial system until you can cite captured data.* Two measurements gate design, not implementation: the **share-price advancement cadence** (WO-1677 §0a — it decides whether "5 pending pulses" and the 10% economic ceiling mean anything) and the **Vercel cron granularity** (not answerable from this repo; check and record).

**Amendment B — 1679 and 1682 gate each other.** The spec puts them in one wave (`:1246-1256`). They are mutually dependent: the tier benefits are what the ceiling bounds, and the ceiling's number decides whether the tier benefits are legal. **Design them together; do not let one land first.**

**1684 is the exception to wave order — instrument as you go.** Spec `:1035`: *"Instrument Heartbound from day one."* Its answer to question 9 (*"Are whales gaining disproportionate gameplay advantage?"*) is the **evidence for 1682's ceiling**, so a telemetry pass at the end arrives after the decision it was supposed to inform.

---

## 3. ⛔ CONSOLIDATED OWNER QUESTIONS

### RULED 2026-09-10 13:10 (owner, AskUserQuestion) - Tier 1
- Q1: **backend only** - relocate the read; the client read is deleted or display-only.
- Q2: **fail to last-known verified state** (bounded grace window, then degrade).
- Q3: **Seeker only; Play never shows it.**
- Q-P2W: **economic acceleration is allowed**; combat power stays off the table.

### RULED 2026-09-10 13:12 (owner) - Tier 2
- Q-WALLET: **one wallet, one realm** - no linkage table, no re-binding; HEART-002/010 wallet-change sections drop.
- Q-STONE: **drop the Rough Stone Discovery event.**
- Q-HEARTFIRE: **allow a second source for stakers** - the single-source lint is re-pointed to permit exactly Heartfire Spark.
- Q-LADDER: **merge** - the Heartbound tier drives polish attempts (the polish mapping becomes a benefit row).
### RULED 2026-09-10 13:20 (owner) - Tier 3
- Q-ECHOFIG: **modifiers only** - Tier II Echo Labor and Tier VII Echo Worker Manifestations move numbers; no new world actors, EchoWorldPresence stays the one owner.
- Q-INJECT / Q-DEMO-FLAG: **test builds only** - the pulse-injection route does not exist in the app real players get; the grant-reviewer demo runs on a separate test deploy. (Owner: "Oh for demo yes".)
- Q-AXIS: **threat wins while under attack** - resonance visuals pause during a wave and resume after; option (a).
- Q-NAME: **the Heart everywhere** - "Tree of Life" stays a dev synonym only; no player-facing string carries it.
### RULED 2026-09-10 13:32 (owner) - Tier 3 continued
- Q-CADENCE: **rare and ceremonial, at most one pulse per day** - a daily cron; the player sees at most one pulse per login day; the HEART-007 show plays per pulse.
- Q-CLIENTECON: **accept as a design guardrail** - the 10% ceiling is computed server-side and applied client-side; written down as not-an-anti-cheat, the same posture save.js:70-72 takes for soft currency. Rate modifiers stay.
### RULED 2026-09-10 13:36 (owner) - Tier 3/4
- Q-METER: **production-rate modifiers only** - the 10% ceiling is the sum of the passive tier percentage boosts to resource yield; timers and event drops are not counted.
- Q-SCOUT: **existing report, earlier** - stakers see RaidDeployVM.ScoutReport before committing troops; the information itself stays equal for everyone; no new intel.
- Q-MERCHANT: **own work order; HEART-005 ships without it** - the visiting-NPC lifecycle is a separate ticket (to mint); the merchant row leaves the HEART-005 event table.
- Q-CONFIG: **Command Center, server-only rows** - teach tunable-manifest a serverOnly marker honoured by build(); the client registry never carries the ladder; the status endpoint returns nextTierAt.
### RULED 2026-09-10 13:40 (owner) - Tier 4
- Q4: **inside HEART-001** - the real Play-boundary lint lands with WO-1674; no separate ticket.
- Q-CRON: **daily is fine** - a third daily Vercel cron; worst-case pulse latency 24 h accepted; no plan change.
- Q-COLOR: **count of lit roots/rings** - tier N lights N of ten root segments around the Heart; never hue; greyscale is the gate.
- Q-VFX: still open - needs the owner to tag each HEART-007 prefab (list to be put in front of her by the WO-1680 lane before it starts).

All Tier 1-4 questions except Q-VFX are now ruled; the Heartbound WOs are dispatchable in dependency order (HEART-001 and HEART-003 lanes already running/returned).

**Ordered by blast radius. Every question raised in any of the thirteen WOs appears below** — each is recorded in its own WO with the evidence; this is the complete list to rule from.

### TIER 1 — these change the shape of everything downstream

**Q1. Two native-SKR stake reads will exist. Which survives?** *(WO-1674)*
The client read (`NativeSkrStakeQuery`, feeds the Jeweler attempts perk) and the new backend read. (a) retire the client one and feed `StakeRewardsResolver` from the backend snapshot — one authority, but an offline player's perk becomes network-dependent; (b) keep both, for explicitly different purposes, written down; (c) keep both and let them drift. **(c) is how every scar in CLAUDE.md started.**

**Q2. Fail-closed, or fail-last-known?** *(WO-1674, WO-1677, WO-1683)*
Today the client fails **closed** — RPC error ⇒ stake 0 (`NativeSkrStakeQuery.cs:87-94`), correct for a perk. HEART-001 requires the **opposite** (`RPC_UNAVAILABLE` + `STALE`, never zero, spec `:175`, `:1013-1017`), correct for a streak. If Q1 = (a), one read must serve both and something must choose. **A fairness decision, not an engineering one.**

**Q3. Heartbound is Seeker-only. Confirm?** *(WO-1674)*
A `play-` or `guest-local-` player has no wallet and no way to attach one (§0.3). Consistent with rule 12 and with the Play compliance posture, but unstated in the spec — and it decides who HEART-008's "The Heart is Silent" copy is written for.

**Q-WALLET. The spec assumes a player who outlives a wallet. There is none.** *(WO-1675; rewrites WO-1683 scenario 5)*
(a) accept it — a different wallet is a different player, and HEART-002's Wallet Change section is deleted rather than implemented; (b) build a player↔wallet linkage layer — a large new identity feature with its own account-takeover surface, **not in this spec**.

**Q-P2W. Is economic acceleration bought with a financial position allowed at all?** *(WO-1679, WO-1682)*
Spec rule 10 bans **combat** dominance. The owner's standing ruling is broader: **whale tier = prestige, never a better army.** Between them sits everything the low tiers grant: +2% offline gathering (Tier I), construction acceleration (Tier II), crafting convenience (Tier V), Resource Surge. None is combat power; all of it is compounding economic advantage proportional to staked capital, and in a base-builder the economy **is** the progression. The spec argues against it itself (`:608-615`: tiers should unlock *varieties, reactions, cosmetics, convenience* and **"should NOT simply multiply resources forever"**; `:722`: *"Tier X should feel prestigious rather than economically mandatory"*) — and then opens the ladder with a resource multiplier. **If the answer is "prestige only", Tiers I/II/V need re-specifying and WO-1679 cannot start.**

### TIER 2 — compliance and canon collisions

**Q-STONE. A staked position would become a source of the rough stone — the input to the game's one real RNG.** *(WO-1678; the demo's climax, WO-1686)*
Three facts: the stone's supply is **owner-ruled and capped on both existing faucets** (2026-09-09: one type, top-two raid tiers, 1/day, dungeons 5% minus starters — `RemoteTunables.cs:713-728`, pinned by `RaidRoughStoneDropRegression`); its only use is a weighted roll with a **15% shatter** (`jewel-polish.json`); and WO-1673 `:14` already flags the *weaker* form of this — *"staking buys extra ATTEMPTS… the shape a regulator looks at first"* — as the sensitive adjacency. **HEART-005 escalates it to "staking produces the stone."** Against Korea's unqualified *"probabilistic items"* and Brazil's Digital ECA **prohibition** on a 13+ app — **both recorded by WO-1673 D6 as NOT YET READ AT SOURCE** — this should not be decided in code. (a) drop the event; (b) keep it under one shared daily cap across **all** sources; (c) keep it uncapped. ⛔ **(c) should not be chosen before WO-1673 D6's readings are in hand.**

**Q-HEARTFIRE. Is Heartfire allowed a SECOND source?** *(WO-1678, WO-1679 Tier VI)*
`HeartfireCharges.cs:13-33`: *"HEARTFIRE IS A CHARGE, NOT A CURRENCY. THIS IS THE WHOLE DESIGN… a currency has a SOURCE the player can influence… Heartfire has exactly one source — the passage of time."* A build lint enforces it (`HeartfireRegression.CurrencyLintCases`, `:334-382`, PIN B at `:120`) and **fails on the literal token "Wallet"** appearing in either Heartfire file. A pulse-granted charge is a second source the player influences **by staking**. ⚠ Independent precedent: `WelcomeBackDoorsVM.cs:30-43` records a "Heartfire is full" door **deliberately not built** and raised as an unruled question — a lane adding a faucet here would answer it silently. (a) drop Heartfire Spark; (b) rule a second capped non-purchasable source and move the doc + lint in the same change; (c) make it **purely presentational** — the fire flares, no charge. **(c) is cheapest; needs a word on whether it still reads as a reward.**

**Q-PLAY. On a Google Play artifact, does Heartbound exist — panel, card, or neither?** *(WO-1681)*
The panel **cannot compile in**: the Trust Statement alone (*"Your SKR remains staked through Solana Mobile…"*, spec `:861`) trips `GooglePlayContentExclusion.cs:410`'s token sweep and `GooglePlayPackagingGate.cs:52-59`. So the panel lives in `DeNelle.Wallet` (`!GOOGLE_PLAY`). The **door** is a separate call: (a) neither — a Play player never sees the word (recommended; matches `SkrPreview` being flipped off for store hardening, `FeatureFlags.cs:638-641`); (b) a locked card with non-crypto copy — a narrow needle through the same sweep; (c) a card explaining the Seeker feature — crypto marketing in a Play artifact. **Folds into Q3: they could not participate anyway.**

**Q-LADDER. Two stake→tier ladders over one wallet — fold, or run both?** *(WO-1675, WO-1679, WO-1681)*
`stake-rewards.json` + `StakeRewardsResolver` (`:152`) + `StakeRewardsPanel` + `StakeRewardsVMTests` + a generated byte-exact fallback already map a stake to a tier and cumulative cosmetic rewards. Heartbound's ten tiers are a second ladder over the same input, with a second panel. **The player would see two tier numbers for one stake.** (a) Heartbound absorbs it; (b) both ship, with the reason written down; (c) `stake-rewards.json` becomes Heartbound's tier **data source** (the spec requires config-driven thresholds anyway, `:359`). **Recommend (a) or (c).**

### TIER 3 — design and scope

**Q-CADENCE. What is the minimum interval between two Heart Pulses?** *(WO-1677)*
Unmeasured, and the spec sets no floor. If the share price ticks per epoch, a daily cron suffices and the economy is bounded; if it ticks per slot, *"5 pending pulses"* is meaningless and the 10% ceiling is unenforceable. **Measure first (§12).** But the *policy* is the owner's: is a pulse a **rare, ceremonial event** — which is what HEART-007's 2.5-4 s roots-and-buildings presentation (`:783-799`) is designed for — or a background tick?

**Q-METER. What exactly is "10% combined effective economic acceleration", measured how?** *(WO-1682)*
No such aggregate exists in the repo. Candidates give different answers: production-rate modifiers only; plus time-value of accelerations; or total daily resources vs an unstaked player. And does the **event table** count, or only the passive tier benefits? Spec `:914-918` excludes *"convenience without resource generation"* — but a build acceleration is convenience **that produces resources sooner**, which is exactly Tiers II and V. ⛔ **A ceiling with no meter is a comment** — and would be this repo's fourth imaginary gate (§0.4).

**Q-CLIENTECON. The ceiling is computed server-side and applied client-side. Accept, or enforce?** *(WO-1682)*
§0.2. (a) accept and write down that the cap is a design guardrail, not an anti-cheat control — the same honest posture `save.js:70-72` already takes for soft currency; (b) server-issued signed effect lists; (c) restrict Heartbound to classes the backend already controls (items via a grant ledger, information, cosmetics) and **drop the rate modifiers** — which is also what spec `:608-615` argues for on design grounds alone.

**Q-ECHOFIG. Tier II "Echo Labor" and Tier VII "Echo Worker Manifestations" — modifiers, or figures in the world?** *(WO-1678, WO-1679)*
Spec `:682` says *"Temporary Echo figures can appear in the settlement"*, which collides with the regression-pinned single-appearance-owner rule (`EchoWorldPresence`, `EchoAutoDeployTrigger.cs:222`; `EchoWorldPresenceRegression.cs:69`; CLAUDE.md §7). As pure modifiers both are free.

**Q-SCOUT. Does Scout's Whisper reveal NEW information, or surface existing information earlier?** *(WO-1678)*
`RaidDeployVM.ScoutReport` (`:204`, built `:498-538`) already gives walls, garrison, boss, spoils. The spec's examples (`:541-547`) are new information, and a raid advantage bought with a financial position is nearer pay-to-win than rule 10 admits even though it is information. `:543` states a preference (*"Prefer information over raw combat strength"*), not a limit.

**Q-MERCHANT. Wandering Merchant is greenfield — build it here, or its own WO?** *(WO-1678)*
`grep -rniE "TemporaryNpc|VisitingNpc|NpcVisit|SpawnTemporary|DespawnAfter" --include=*.cs Assets/_Modules` → **0 hits.** Every vendor is permanent and placement-driven. A visiting-NPC lifecycle is larger than the rest of HEART-005 combined. **Recommend its own ticket.**

**Q-CONFIG. Where do backend-only Heartbound knobs live?** *(WO-1676, WO-1682)*
The Command Center rail is a **three-way join** (`RemoteTunables.Registry` + `TUNABLE_KEYS` + `PRESENTATION`), and a `PRESENTATION` row with no **client** registry entry **fails** it — `tunable-manifest.js:848-853`, *"would be INVISIBLE in the Command Center"*. Heartbound's knobs are backend-read. (a) teach the manifest a `serverOnly` marker honoured by `build()`; (b) a separate `heartbound_config` — safe, but a second config system the owner cannot flip from her phone. ⛔ **Not `RemoteTunables.cs` alone** — that is the *client* rail, and a client-readable tier threshold is a second copy of the authority. **Sub-question:** the panel needs next-tier progress — the status endpoint should return `nextTierAt` rather than the client re-deriving it from a copied table.

**Q-INJECT / Q-DEMO-FLAG. Is an admin-keyed pulse-injection route acceptable in production?** *(WO-1686)*
The client half can be excluded at compile time; **a backend route cannot be** — there is no compile-time exclusion on a Vercel function, so any injection capability is a production endpoint able to grant real rewards, which is exactly what spec `:1224` forbids. ⭐ **Note the spec's own wording at `:1183`** — *"a legitimate **previously-recorded** Heart Pulse"* — describes a **replay of presentation**, not a simulator; and the owner is recorded as staking ~1,000,000 SKR (`WorkOrders/WORK_ORDER_skr_staking_and_seeker.md` §1, dated **2026-06-28** and **not verified at HEAD by this lane**), so a real pulse is very likely capturable. (a) admin-keyed route; (b) non-prod DB only; (c) no injection — capture a real pulse and replay the presentation. **(c) is what the spec literally says.** ⚠ And a **feature flag is not an exclusion mechanism** here: `FeatureFlags.cs:691-693` records that a stored PlayerPrefs value **beats the default**, so a flag-gated fabricator is flippable in prod.

### TIER 4 — narrower, but each needs a word before its lane can start

**Q4. `StakingComplianceRegression` has been cited as live protection for weeks and has never existed. Build it under WO-1674, or its own ticket?** *(WO-1674)*
§0.4. It is a small source/artifact lint at most, and leaving a **fourth** citation of an imaginary gate is worse than either option. Raised because HEART-001 is the first ticket that makes the Play boundary load-bearing rather than theoretical.

**Q-CRON. A third Vercel cron, and at what granularity?** *(WO-1677)*
`vercel.json:6-9` declares exactly two, both daily. If daily is the answer, worst-case pulse latency is 24 h and the player sees their pulse on the next login after that. Sub-hourly is a platform-plan question. ⛔ **Not answerable from this repo** — check and record, never assert.

**Q-AXIS. Resonance vs threat on the same object — which wins, and when?** *(WO-1680)*
The Heart already carries two visual state machines: threat-driven (`HeartController.cs:44-80`, seven `HeartState` values with authored colour/emissive/pulse) and HP-driven (`HeartAuraController`). A ten-tier resonance glow is a **third axis on the same object**. During a siege the Heart goes Critical; a Tier X glow that overrides that breaks a combat tell. (a) resonance suppressed in combat; (b) resonance modulates only channels threat does not use — ground markings, root illumination, ambient wisps — so the two never contend (**what the spec's own differentiator list at `:748-758` is already shaped for**); (c) resonance wins.

**Q-COLOR. ⛔ The owner is colourblind — a ten-step ladder cannot be signalled by hue.** *(WO-1680)*
Memory `owner-colorblind-delegate-visual-creative`: never ask her to pick hues; **the greyscale check is the gate**. `HeartAuraController.cs:16-24` already solves this once for the health tell, with a colour-free signal using **size, luminance and motion** — the precedent, not the exception. **Which non-hue channels carry the tier?** Ten steps is a lot to fit into three, and the answer shapes the whole ladder.

**Q-VFX. Every VFX prefab in HEART-007 needs an owner tag.** *(WO-1680)*
Memory `vfx-map-owner-tags-no-creative-pick`: the CLI maps key→hook **verbatim** and holds un-tagged hooks. Exactly one relevant tag exists — `Assets/Editor/Regression/VfxMirrorPairSet.cs:102`, *"Owner tag 2026-08-16 verbatim: ParticlePack FireFlies -> 'Tree of Life Aura'."* Roots, wisps, ground markings, bloom, crown illumination and Echo silhouettes are all **untagged**. ⚠ And memory `sequenced-vfx-special-cases-for-special-events`: a marquee moment gets ordered prefabs, **never a second spawner or pool**.

**Q-NAME. The spec says "Tree of Life" throughout; player-facing canon is "the Heart".** *(WO-1680)*
Every repo hit for "Tree of Life" is internal/dev (`CityManifest.json:75`, `HovlVfxCatalogGenerator.cs:180`, `RegressionSuite.cs:717,768,778`, three regression comments). Canon: *"The Heart, an ancient world tree at the centre of your settlement"* (`docs/CREATIVE_CANON_ELARION_2026-09-04.md:28`); CLAUDE.md §7 "Heart of Elarion". **Confirm it stays an internal identifier only**, so HEART-008's strings are written once.

---

## 4. THINGS THE REPO ALREADY DOES WELL THAT THIS FEATURE SHOULD REUSE, NOT REINVENT

Recorded so thirteen lanes do not each rediscover them.

| Need | Reuse | Where |
|---|---|---|
| `IHeartboundBonusProvider` shape | **`IPolishBonusProvider`** — interface + zero provider + `Install` + an `Active` that reads the platform flag in **exactly one place** | `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs:49,59-63,97-103,109-116,166-170` |
| "grant this player X exactly once" | **`achievement_grants`**, `PRIMARY KEY (wallet, achievement_id)` — the schema describes itself as a *"Generic idempotency ledger"* whose PK *"STRUCTURALLY prevents a double-grant"* | `api/schema.sql:965-984` |
| Value-granting auth | **`authenticateGranting()`** — wallet-only, guests always refused | `api/_lib/wallet-auth.js:829` |
| On-chain read + failure taxonomy | `api/tower-swap/log.js:70-127` — its codes map ~1:1 onto the spec's `VerificationStatus` | `:84-127` |
| A new full-screen panel | `PanelId` (next free **28**) + panel + bootstrap + one deck-card `Route(...)` row | `PanelRouter.cs:37,182,717`; `HeartPanel.cs:49,86-93`; `HeartPanelBootstrap.cs:12-14`; `PlayerDeckWorkspace.cs:717-728` |
| Offline accrual | implement `IOfflineClaimConsumer`, register with `OfflineClaimCoordinator` (4 consumers today) | `OfflineClaimCoordinator.cs:107,163` |
| Item grant | `VillageInventory.AddEarned` — the **reward-authority** grant, deliberately distinct from `Add` | `VillageInventory.cs:110,124` |
| Rough-stone grant | `DungeonRunPayout.GrantRoughStone` → `DungeonController.BankRoughStone`, the **one writer** | `DungeonRunPayout.cs:189-191`; `DungeonController.cs:660-693,703-708` |
| Scout intel | extend `RaidDeployVM` with a **new** property; `ScoutReport`'s last-line contract is pinned | `RaidDeployVM.cs:204,213-218,221-228` |
| Telemetry | `EventTracker.Track` (client) / `logApiEvent` (server) → `analytics_events` | `EventTracker.cs:109`; `audit.js:112`; `schema.sql:370-378` |
| Clock discipline | `serverNowMs` server-side; `TimeSource.NowUnixMs()` client-side, **never `DateTime.UtcNow`** | `save.js:753-755`; `HeartfireService.cs:52-60`; `HeartfireCharges.cs:44-50` |
| Channel-gated doors | mirror the Realm Store card's gating | `RealmStoreSingleRegistrarRegression.cs:210,217` |
| Frame cost on a VFX path | the **4-arg** `FlowTrace.Measure` — never the 3-arg form per-frame | `FlowTrace.cs:293-300,308` |

**Greenfield, with nothing to reuse:** the Heart's multi-stage resonance visual (WO-1680 — no stage system exists; the only visual machines on the Heart are threat-driven `HeartState` and HP-driven `HeartAuraController`); a visiting merchant (WO-1678 Q-MERCHANT); a build-time/crafting-time modifier seam (WO-1679 §0c — `EchoLaneBonuses.CraftingMult` is declared and **unwired**, along with `DefenseMult` and `ExplorationMult`); a server-side cache (no KV/Redis/Edge Config anywhere in `api/`).

---

## 5. ⚠ CANON CORRECTIONS THIS LANE PRODUCED (CLAUDE.md §15)

Recorded because the next seat will otherwise inherit them.

1. ⛔ **There is NO server-side Firebase Auth.** The lane brief cited memory `firebase-auth-neon-architecture`. `package.json:12-22` has no `firebase-admin`; every `firebase` hit in `api/` is a comment calling it retired (`api/_lib/wallet-auth.js:790`, `api/_lib/google-identity.js:16`, `api/auth/google-session.js:26`); `google-identity.js:51` accepts issuer `accounts.google.com` **only**, so a Firebase ID token is rejected outright; and the client's `Assets/_Modules/Core/Auth/FirebaseAuthService.cs:1-19` is marked **RETIRED, ZERO CALLERS**. The three live rails are **wallet (ed25519) / Google (Play only, default OFF) / guest**.
2. ⚠ **`ArenaWalletService`'s SKR arm is a PlayerPrefs stub, not a chain balance** — key `dotr-arena-skr-balance`, seeded 500 (`Assets/_Modules/Village/Arena/ArenaWalletService.cs:16-18,49-53`). It never touches an RPC and never signs. **It is not a Heartbound seam.** ⚠ And the naming trap WO-1673 already flagged: `ArenaMode` (wagered, dormant behind `FeatureFlags.cs:33`) is a **different system** from `BattleArena` (free, live).
3. ⚠ **"Tree of Life" is a dev synonym, not player-facing canon.** Every repo hit is internal (`CityManifest.json:75`, `HovlVfxCatalogGenerator.cs:180`, `RegressionSuite.cs:717,768,778`, three regression comments). Player-facing canon is **the Heart** — *"The Heart, an ancient world tree at the centre of your settlement"* (`docs/CREATIVE_CANON_ELARION_2026-09-04.md:28`), CLAUDE.md §7 "Heart of Elarion". The spec uses "Tree of Life" throughout; that is fine internally and must not reach player copy.
4. ⚠ **`Assets/Prefabs/Environment/TreeOfLife.prefab` exists but is NOT what the builder instantiates** — `CastleHubBuilder.cs:2780` loads the raw `Assets/Art/Tree_Of_Life.fbx`. Editing the prefab changes nothing.
5. ⚠ **`EchoWorldPresence` is not in a file of its own** — it is `public static class EchoWorldPresence` at `Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs:222`. `find -name "EchoWorldPresence*"` finds only the regression.
6. ⚠ **The Command Center is a server-rendered admin console, not a Unity screen** — `api/admin/console.js:66,203` plus `tools/command-centre.ps1`. `grep -rn "CommandCenter" Assets/ --include=*.cs` returns 4 hits, **all doc-comments**.
7. ⚠ **`DataRegression` has no registration array** — 398 hand-written `Guard.Try("Regression", …)` lines in one sequential `RunAll` body (`:396`-`:2002`). Its own comment at `:1505-1511` records a commit that added fourteen suites and registered four: ***"A suite that is not registered is not coverage; it is a file."***
8. ⚠ **`SaveSchema.CurrentVersion` read at source = 41** (`Assets/_Modules/Core/State/SaveSchema.cs:41`) — cited here **dated, not as an authority**, per CLAUDE.md §8. It **does not change** for this feature (§0.2).

---

## 6. WHAT THIS LANE DID NOT DO, AND WHY

- **No code, no Unity, no gate, no commit.** QA triage is read-only (CLAUDE.md §13).
- **`CLI_LANES_WO_NUMBERS.md` not edited.** Numbers were pre-assigned by the coordinator; the lead bumps the banner (CLAUDE.md §2).
- **`BOARD.html` not regenerated.** Thirteen new WOs will appear on the next `python tools/board_build.py` — flagged so the lead runs it with the commit rather than discovering the gap later.
- **The share-price cadence NOT measured** (WO-1677 §0a). It needs a 24-48 h sampling window and network access to mainnet RPC. **Named as unproven rather than estimated** (CLAUDE.md §11B) — an unproven thing named as unproven is useful; an unproven thing stated as fact costs someone a day.
- **The Vercel cron granularity NOT determined.** Not answerable from this repo. `vercel.json:6-9` declares two daily crons and nothing about the plan.
- **The UserStake account's unstaking-field byte layout NOT determined.** The client deserializes only two `u128`s, at offsets 137 and 105 (`NativeSkrStakeQuery.cs:65,74`). The remaining fields are an **external fact from the Solana Mobile IDL** and must be read there, not guessed (WO-1674 D1).
- **Korea's Enforcement Decree and Brazil's Digital ECA NOT read.** They are WO-1673 D6's deliverable and they are the gating input to **Q-STONE**.
