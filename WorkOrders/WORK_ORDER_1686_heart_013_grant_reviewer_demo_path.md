# WORK ORDER 1686 — HEART-013: Grant / reviewer demo path

**Status:** SPEC
**Silo:** Demo/capture path + a compile-time exclusion + one regression pinning its absence from prod.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:1162-1227` (HEART-013).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW capture path. The exclusion mechanism is the whole ticket, and the spec's own rule is stricter than the repo's existing precedent.**

Spec `:1221-1227`, the demo rule:
> *"Do NOT fake blockchain state in the production build. A development-only pulse simulator may exist behind development flags for repeatable automated testing and video capture. It must never compile into production behavior capable of granting real player rewards."*

### 0a. ⚠ THE SPEC HAS TWO DIFFERENT THINGS IN IT AND THEY NEED DIFFERENT MECHANISMS

Read `:1183` carefully: *"Trigger controlled demonstration of a **legitimate previously-recorded** Heart Pulse."*

That is a **replay of PRESENTATION** for a pulse that already happened and already granted — client-side, no fabricated chain state, no reward. It is not a simulator at all. **And the owner is recorded as a large SKR staker** (`WorkOrders/WORK_ORDER_skr_staking_and_seeker.md` §1 records 1,000,000 SKR staked — ⚠ **dated 2026-06-28, in a CLOSED WO, and NOT verified at HEAD by this lane**), so a real pulse very likely lands on her wallet and there is a real recording to replay. **Confirm the stake is live before planning the capture around it.**

Only the *automated-test* half (`:1224-1226`) fabricates pulses, and that belongs in test infrastructure (WO-1685), not in a shipped artifact of any kind.

**Separating these two is the first deliverable**, because they have different risk and different exclusion mechanisms, and conflating them is how "a demo flag" becomes a reward faucet.

### 0b. ⛔ A FEATURE FLAG IS NOT AN EXCLUSION MECHANISM — this repo has the receipt

The obvious model is `StakeRewardsDemoBootstrap` (`Assets/_Modules/Core/Platform/StakeRewardsDemoBootstrap.cs`), which seeds a `MockStakeQuery` with ~1,000,000 fake SKR behind `ff.stakedemo` (`Assets/_Modules/Core/FeatureFlags.cs:624`, default false). **Do not copy it for anything that can grant.**

Why: `FeatureFlags.cs:691-693` records that **a stored PlayerPrefs value BEATS the default**. A flag-gated simulator is therefore *flippable in a production build* by anything that can write the pref. `StakeRewardsDemoBootstrap` is safe only because it fabricates a *display* value whose one consumer is attempts-only.

**The sanctioned mechanisms in this repo are compile-time**, and they are already load-bearing:
- `#if UNITY_EDITOR` / `DEVELOPMENT_BUILD`.
- **Assembly define-constraints** — `Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:21-23` (`"defineConstraints": ["!GOOGLE_PLAY"]`) takes a whole assembly out of an artifact.
- The **channel stamps** — `Assets/Editor/AndroidBuild.cs:237-238` stamps `GOOGLE_PLAY` or `DAPP_STORE` and filters the other; `Assets/Editor/Regression/GooglePlayPackagingGate.cs:191` forbids `DAPP_STORE`/`GOOGLE_PLAY`/`SOLANA_SDK` as *persistent* defines; `GooglePlayPackagingRegression.cs:40-41` asserts the stamp lines still exist.
- The **artifact gate** — `AndroidBuild.cs:199-204` fails the build (`PLAY_ARTIFACT_REJECTED`, `EditorApplication.Exit(1)`) on forbidden surface in the built artifact. ⭐ **This is the shape to reuse: a check on the ARTIFACT, not on the source.**

Existing test-only defines in the repo: `TESTER_BUILD`, `STORE_RAIL_LOCAL_TEST`, `MAINNET_CANARY_TEST` (`FeatureFlags.cs:748`).

### 0c. ⛔ THE BACKEND HALF IS THE ONE THE SPEC DOES NOT ADDRESS

The spec's *"must never compile into production behavior"* is written for the **client**. But under HEART-001..005 the backend is authoritative: **any "inject a pulse" capability is a backend route**, and a backend route is deployed to production by definition — there is no compile-time exclusion on a Vercel function.

The nearest precedent is `api/admin/ops.js:2` (*"THE WRITE ENDPOINT for the Command Center"*), key-gated by `ADMIN_OPS_KEY`, with the SHA-256 + `timingSafeEqual` comparison pattern at `api/admin/cleanup.js:37-43` and refusal as **400, never 401/403** (`:71-73`).

**So an owner-keyed pulse-injection route is buildable and is guarded like every other admin write — but it is a production endpoint capable of granting real player rewards, which is the exact sentence the spec forbids.** See Q-INJECT.

---

## 1. Deliverables

- **D1** — Split the two things (§0a): **(i)** a client-side *replay of presentation* for a real, already-granted pulse — no chain fabrication, no grant; **(ii)** a *test-only* pulse fabricator that lives in `test/` and never in an artifact.
- **D2** — The exclusion mechanism for (ii), **compile-time, not flag-based** (§0b). Name the define in the RESULT.
- **D3** — ⭐ **A regression that pins its ABSENCE from a production artifact.** CLAUDE.md §8 records the house pattern: for a retired flag, the coverage is *the regression that pins its absence*. Model it on `GooglePlayPackagingGate`'s artifact scan (§0b) rather than a source lint — a source lint proves the code says the right thing; an artifact scan proves the shipped bytes do.
- **D4** — The capture itself (`:1168-1207`): stand near the Heart → open Heartbound → **"Native SKR Detected"** → verified stake → return to the kingdom → the pulse → Heartfire activates, roots illuminate, the settlement receives it → **"THE HEART REMEMBERS"** → **"Echo Event: Rough Stone Discovered"** → open the stone → **show it entering the existing stone/gem progression** → back to Heartbound → `Native SKR → Resonance → Heart Pulse → Living Kingdom`.
- **D5** — The reviewer message (`:1213-1219`), verbatim: *"Native SKR staking is not merely checked for access. Its live staking state becomes part of the simulation of Elarion itself."*

⚠ **D4's rough-stone beat is blocked by WO-1678 Q-STONE.** If the owner rules that a pulse may not produce a rough stone, **the demo's climax changes** and D4 needs a different Echo Event. Do not storyboard around the stone until that is ruled.

---

## 2. Acceptance

1. The demo runs on a **real** verified stake and a **real** recorded pulse — no fabricated chain state anywhere in the captured artifact (spec `:1223`).
2. **A production artifact contains no pulse-fabrication capability**, proven by the D3 artifact scan on a real build, **not** by reading the source.
3. The captured video shows the rough stone (or its replacement) entering the existing progression — the reviewer's whole point is that it is not a token gate.
4. `UI_CAPTURE_OK` with the frames **OPENED**; landscape (memory `owner-rulings-2026-09-10-morning`).
5. `COMPILE_GATE_OK`, `gate_brace.py` clean, zero NUL bytes; `REGRESSION_OK <n>/<n> suites` on a **fresh** log with D3 registered, **proven RED before green**.
6. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 3. Dependencies

**Blocked by:** all twelve siblings — this is the spec's own Wave 6 (`:1276-1280`) and it demonstrates the finished feature. Most tightly: WO-1680 (the presentation), WO-1681 (the panel), WO-1678 (the event, and Q-STONE decides the climax).

---

## 4. ⛔ OWNER QUESTIONS

### Q-INJECT. Is an `ADMIN_OPS_KEY`-gated pulse-injection route acceptable in production, or must fabrication be non-prod-DB only?
§0c. The client half can be excluded at compile time; the backend half cannot. **Options:** (a) an owner-keyed admin route, guarded exactly like `api/admin/ops.js`, that can mint a pulse — convenient for capture and re-capture, and **it is a production endpoint capable of granting real rewards**, which is the thing spec `:1224` forbids; (b) fabrication only against a non-production database, with the demo captured against real chain state — slower to iterate, and it is what `:1223` literally says; (c) no injection at all — wait for a real pulse, capture it, replay the *presentation* forever after (§0a). **(c) is the cleanest and is what the spec's own wording at `:1183` points at.** Owner's call.

### Q-DEMO-FLAG. Which define, and does it need to be a define at all for the replay half?
The **replay** half (§0a-i) fabricates nothing and grants nothing — it re-plays a presentation for a pulse the player genuinely received. Arguably it should ship to *everyone* as the "watch it again" affordance, which also satisfies HEART-007's *"Must be skippable after first viewing"* (`:790`) from the other direction. **If so it is not demo-only at all and needs no exclusion**, and only the fabricator (§0a-ii) does. Confirm, because it changes what D2 and D3 are guarding.

### Q-STONE (restated from WO-1678, and this is where it becomes visible to a reviewer). If a pulse may not produce a rough stone, what is the demo's climax?
The whole reviewer argument — spec `:1201-1207` — is *"show that it enters the existing stone/gem progression."* That beat is the proof it is a simulation and not a token gate. If WO-1678 Q-STONE removes the stone from the event table, this demo needs a new climax that makes the same point, and the candidates (a resource surge, a scout's whisper) are visibly weaker on camera.

---

## 5. What NOT to touch

- ⛔ **`StakeRewardsDemoBootstrap` / `ff.stakedemo`** as a model for anything that grants. §0b — a stored pref beats the default (`FeatureFlags.cs:691-693`).
- ⛔ **`AndroidBuild.cs:237-238`'s channel stamps** and `GooglePlayPackagingGate`'s forbidden lists. Cite and extend the gate; never widen it to let demo surface through.
- ⛔ **A real reward through the demo path.** Spec `:1227`. The replay half shows a pulse that already paid; it never pays again.
- ⛔ **Raw `adb install` for the capture build.** CLAUDE.md §16: installing or distributing goes **through the scripts** (`install-apk-to-seeker.ps1`), never raw `adb` — that hole cost a build in which every enemy was a capsule.
- ⛔ **Faking chain state in anything that reaches a device or a store.**
- No `.unity` scene files hand-edited. No `SaveSchema` change.

---

## 6. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`Assets/_Modules/Core/Platform/StakeRewardsDemoBootstrap.cs`; `Assets/_Modules/Core/FeatureFlags.cs:624,642,691-693,748`
`Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:21-23`
`Assets/Editor/AndroidBuild.cs:199-204,237-238,241-248`; `Assets/Editor/Regression/GooglePlayPackagingGate.cs:52-59,191`; `GooglePlayPackagingRegression.cs:40-41`
`api/admin/ops.js:2`; `api/_lib/ops.js:2`; `api/admin/cleanup.js:37-43,71-73`
`WorkOrders/WORK_ORDER_skr_staking_and_seeker.md` §1 (the owner's 1,000,000 SKR staked — why a real pulse is capturable)
`Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:157-159` (`DemoMockStakeSkr = 1_000_000L`, the mock that must not reach the capture)
