> **2026-09-09 WO-1605 (ACTIVE GOAL):** The global Core facade and Unity Localization adapter enforce the 10-region contract and expose the six-locale beta (`en`, `es`, `pt-BR`, `de`, `fr`, `ru`); Arabic, Japanese, Korean, and Simplified Chinese remain parity-required but hidden pending RTL/CJK readiness. The 10 source/mirror catalogs hold 416 translatable keys and the six enabled tables hold 2,496 entries. Dungeon chest interaction remains live through `ChestInteractionText`. Store has 39 translated keys: the seven BUY GATE keys are live through `StoreBuyText`, six Pi Store skin keys through `StorePiText`, and seven safe presentation keys through `StorePresentationText`; the remaining 19 wallet/commerce keys stay staged behind legacy `StoreStrings`/canon readers. `storeBuyWalletRequired` uses named `{Threshold}` exactly twice in all 10 locales. Google Play localization prepares an exact transient variant before Addressables: 57 rows owned by unavailable Wallet/Web3/Settings/Jeweler branches are stripped and five visible rows receive locale-specific Play-neutral replacements. The transaction covers both canonical roots and all seven `GameStrings` assets, composes with content exclusion, then restores all 27 localization assets byte-for-byte. Fresh focused evidence is `LOCALIZATION_REGRESSION_OK 9/9 suites`; the concurrent full-tree run includes `[store-presentation-localization]` and reports `REGRESSION_OK 467/467 suites -- 467 green, 0 red, 0 skipped`. Earlier focused evidence remains `STORE_PI_SKIN_OK`, `PLAY_PACKAGING_REGRESSION_OK`, and `PLAY_LOCALIZATION_VARIANT_TRANSACTION_OK`; the Seeker-size smoke remains 18/18. Compile gates emit `COMPILE_GATE_OK`; wrapper scans still report the known optional Solana WebGL module absence. The final physical AAB still requires scanner proof. The manifest remains report-only/unarmed at 6,034 rows: 416 `keyedEntry`, 85 `keyedCall`, 5,517 `literalCandidate`, and 16 `imageTextCandidate`. Every player-facing copy change still requires all locale catalogs/mirrors, rebuilt enabled tables, and localization tests; translations may remain limited/provisional pending review but cannot be omitted. The overnight procedure is `tools/localization/run-localization-overnight.ps1`; architecture canon is `docs/localization/architecture.md`. Broader visible-text migration remains open.

> **BRANCH MODEL (owner ruling 2026-09-07 evening): `master` = PRODUCTION (fast-forward only on the owner's word, tagged `v<date>-store-<code>`; now f5d39acd1 = `v2026.09.07-store-359722`), `dev` = the daily working line (all lanes, gates, pushes). `feat/synty-art-retheme` is RETIRED at f5d39acd1 - do not push to it. Check `git branch --show-current` reads `dev` at boot.**

> **2026-09-08 WO-1606 (CLI, PROD EXE + APK, INSTALLED ON SEEKER):** Sable now starts with the clean Store categories Buy / Sell / Craft / Leave; Craft routes Potions, Jewelry, or the dedicated timed Polish/reveal flow. The return-home Jeweler FTUE uses the rough-stone art, persistent Sable pointer, localized copy, and read-only native SKR stake check with attempt-only weekly re-roll. Regression is `457/457`. IMPORTANT correction to the earlier startup-only claim: the first release EXE crashed when `raider_camp_small` spawned `Orc_Necromancer`; its log proved Unity could not replace stale cache hash `2220522384eb58b0db363f6c6e1b47ab`, then attempted an impossible native Serialization allocation. The rebuilt release now runs a one-time pre-Addressables cache repair, quarantines later loads after a managed cache-write failure, and serializes both request orders so the Orc cache has one writer. Final fresh-cache dev-player proof entered `RaidBase_raider_camp_small`, reached the exact necromancer spawn, suppressed the inverse family race, queued the other Orc requests, made the real necromancer resident in 6.1s, late-re-skinned every placeholder, exited 0, and logged zero cache-write/crash signatures. The versioned Windows catalog is live on R2 and full parity is green (`Android,StandaloneWindows64,WebGL`, 276 objects). Release artifact: `Builds/Windows/DefendersOfTheRealm.exe` (no dev tools/watermark; SHA-256 `B93CA99DFA1ABAF15AE78B1ED9474EE3F1A7BB1A50A76B01C073EC0F282CE09A`). Store-shaped APK `2026.09.08.361171` (`361171`) passed schema/R2 parity, 16 KB ZIP + Google Sign-In ELF alignment, and large-screen manifest inspection (`resizeableActivity=true`, `fullUser`); install and launch on Seeker `SM02G4061955851` succeeded. Grant-review cut: `C:/Users/Elden/Videos/Screen Recordings/Echoes of Elarion - Grant Reviewer Demo.mp4` (1:44, original untouched).

> **2026-09-08 WO-2020 (CLI, INSTALLED ON SEEKER):** Honest Feedback is rebuilt without overlap, has a permanent Settings > Feedback > Send Feedback door after dismissal, centralizes its copy under localization-ready `feedback.*` keys, and attaches device language metadata to natural-language submissions. Proof: compile clean; regression 457/457; feedback capture geometry/touch 3/3; tester APK `2026.09.08.360990`; R2 parity 276 objects. The Play-signed install initially blocked the tester signature; owner uninstalled it, sanctioned install then returned Success. `adb dumpsys` confirms version/code `2026.09.08.360990 / 360990`, installed `2026-09-08 11:49:24` on Seeker `SM02G4061955851`. Full visible-text localization execution plan is WO-1605; audio is owner-ruled out of scope.

> **2026-09-08 WO-2020 + WO-1605 (CLI):** Honest Feedback is rebuilt without overlap, remains permanently reachable from Settings after dismissal, uses localization-ready `feedback.*` keys, and submits device-language metadata with natural-language notes. Proof: `Builds/proof/wo2020-honest-feedback-seeker-2670x1200.png`; capture geometry/touch 3/3; regression 457/457. Tester APK `2026.09.08.360990` built and R2 parity passed for 276 objects, but it is NOT on Seeker: Android correctly refused it because the phone now holds Google Play-signed `359670`, whose signature differs from the tester signer. Do not uninstall the Play app without owner authorization; that would erase local app data. WO-1605 is the explicit company-style plan to move every visible/written phrase and text-bearing image to one switchable localization library; audio is owner-ruled out of scope.

> **2026-09-08 WO-2019 (CLI, AWAITING OWNER DEVICE MATCH):** Manage QUEUE is now one consistent full modal on BUILD / ARMY / RESEARCH, presented as a work timeline with NOW/numbered nodes, compact model-owned timing, segmented channel capacity chips, and contained action pricing. The legacy narrow ARMY drawer and duplicate modal/global X are retired. Fresh evidence: compile clean; regression 456/456; Manage capture geometry 20/20 and touch 20/20. Owner device approval remains the DONE gate.

> **2026-09-08 WO-2018 (CLI, AWAITING OWNER DEVICE MATCH):** Manage BUILD now opens on one clean row of four category cards sourced in `BuildFilter.Membership`, with only art + category name; choosing one reveals only its buildings and BACK returns to the category root. Tile state/name hierarchy is tightened, detail art is larger with the CTA using the lower well, and Army portraits are explicitly clipped to the same square seat as their authored frames. The behavioral/source regressions and Manage flow capture traversal were updated with the new navigation. Fresh evidence is recorded in the WO-2018 RESULT. Owner device approval remains the only DONE gate.

> **2026-09-07 14:4x (CLI, end of day): READ `docs/EVENING_HANDBACK_2026-09-07.md` FIRST.** The dApp Store submission file is `Builds/Android/store/EchoesOfElarion-store-2026.09.07.359722.apk` (codes 359651 / 359664 are burned in the portal); the Seeker holds tester 2026.09.07.359731; WebGL/Vercel is HELD by owner ruling; three gated waves landed today (446 -> 454 -> 456 suites), the branch is pushed through 3a93f288e. The morning handback below is frozen.

> **2026-09-07 06:10 (CLI overnight): READ `docs/MORNING_HANDBACK_2026-09-07.md` FIRST.** Four commits landed gated (c0c30f715, 94808e2e2, cd57a1c1e, 0bffcd4d1; regression 441/441), the tester APK 2026.09.07.359076 is on the Seeker, the Windows exe, the Google Play AAB and the WebGL deploy (both production domains, parity green) are done. Every Manage ticket sits in the board's VERIFY bucket until the owner scores a device frame >=95% against its mockup panel (ruling 29).

> ## >> SESSION HANDOVER - 2026-09-06 - START HERE <<
>
> **Live anchor: `CANON_GROUND_TRUTH_2026-09-06.md` (repo root)** - the ONE unbannered ground truth;
> every other root `CANON_GROUND_TRUTH_*.md` carries a `SUPERSEDED` banner and is frozen history.
> ⛔ **NO PUSH-STATE NUMBER IS WRITTEN HERE, AND NONE MAY BE ADDED (WO-1482).** Measure it:
> `git rev-list --count origin/<branch>..HEAD` for the branch `git status -sb` names. This line read
> **"117 commits unpushed ... = 117"** until 2026-09-07, by which date the count had moved on again -
> and it is not the first push-state number in this file to rot where it stood (see the 09-03 block's
> `103 unpushed on 2026-09-06`, and the 08-03 block's `43 commits ahead`, both frozen below).
> **The count is not the fact; the command is.** The standing cadence is unchanged:
> commit local, push only after the owner retests (felt/gameplay) or a regression proves it
> (data/logic) - so a non-zero ahead-count is the NORMAL state here, never by itself a defect.
> Every banner below this block is history, kept, not guidance.
>
> ### 2026-09-07 - THE "ALL NINE SCREENS MATCH" CLAIM WAS FALSE. READ THIS BEFORE ANY MANAGE WORK.
>
> Commit `949e848a0` is titled *"all nine screens match the owner's mockup - twenty-four capture rounds"*
> and records `geometry = 0 touch = 0 fidelity = 0 named faults = NONE`. **It did not match.** That night
> the owner walked all nine Manage screens on device build 358872 beside
> `docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png` and **none of them matched** - no card art and
> truncated copy on the hub, ring medallions and blank tier tiles on the BUILD grid, no stat table on
> detail, undimmed locked troops, a dead well under the research picker, clipped queue rows, and every
> panel drawn through a 64%-wide plate over the town rather than filling the screen. Twelve board rows
> sat in the Done and Fixed buckets (ten led `IMPLEMENTED`, two `FIXED`), plus WO-2006 and WO-2008 in
> `WorkOrders/ManageRedesign/`, on the strength of a comparison **a seat made against its own
> headless frames**. The rule that closes it (owner 2026-09-07, ruling 29 in
> `WorkOrders/ManageRedesign/OWNER_RULINGS_LOCKED.md`): **the acceptance for every Manage screen is a
> DEVICE SCREENSHOT beside its mockup panel, judged by the OWNER, at >=95% on SIZE / FONT / STYLE /
> CONTEXT / IMAGES, and it must FILL the screen.** Headless captures are evidence toward that and can
> never mark a ticket done; only her word moves one to DONE. Those tickets now read `AWAITING OWNER
> MATCH` and the board has a new **Verify** bucket (`tools/board_build.py`) so they can never read as
> finished again. Scorecard and loop: `WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md`.
>
> ### What landed tonight (21 commits, `949e848a0..HEAD`, 19:25 - 20:43)
>
> - **The audit fleet minted 72 tickets in one pass, WO-1446..WO-1517** (`0a171806b`), each with quoted
>   evidence. The owner's raid rulings then minted WO-1518..WO-1531 (`758be6125`, `f3825df2c`,
>   `b37b2cb89`). **Mint from the `CLI_LANES_WO_NUMBERS.md` banner and bump it in the same edit.** No
>   number is copied into this block - the working-tree banner is newer than any commit message.
> - **Owner felt-test of tester APK `2026.09.07.358574`** (installed 19:20 on the Seeker): two validation
>   ingests, **15 tickets closed on her Pass**, WO-1414 bounced Fail (`0af22982f`, `815c628e9`).
>   WO-1441/1442/1443/1420 were found already landed and never flipped - RESULTs written (`b64fa0a25`).
> - **API lane:** WO-1446/1505 ledger-driven migration runner + `signed_at` migration + drift oracle
>   (`26e9fb0a8`); WO-1453 wallet-signature verify against reconstructed bytes, no more 500 on
>   redeem/save (`0f35490ad`); WO-1457/1456 schema_version monotonic + nonce IP budget (`321b753c4`);
>   WO-1506/1502/1501 (`f957bdbaa`); WO-1449 builders-hour sellable on both rails (`9fb58306f`).
> - **Ship lane:** WO-1469/1470 - `distribute-android.ps1` gains the R2 parity gate, two chains now
>   refuse a stale parity log (`4ec1a861d`, `b30e551ce`).
> - **Seven gated gameplay lanes** in `eb161dc98` (raid AI breach push, fireball catalog off-by-two,
>   defense rows, welcome-back doors, shield seat facets, Night Market no-wallet, Build affordability).
>
> ### Tree state - WAVE TWO IS UNCOMMITTED AND IT IS LARGE
>
> `git status --short` shows 190 modified + 52 untracked paths. Owning lanes:
> raid/camps (`Village/World/Camps/*`, `RaidDeployScreen`, `RaidScoring`, `RaidGarrisonSpawner`) -
> WO-1520 and WO-1530 (both grep-confirmed against the ticket bodies); Manage/HUD MVVM (`ObsidianQueueVM`, `ArmyMusterVM`, `HeroDeckWardrobeVM`,
> `Core/Bridging/`, `VillageBridgeService`) - grouped by path, owning WO to confirm at commit time;
> 8 new regression suites
> (`AllowlistExpiry`, `CloudLoadRestore`, `CoreReflectionSource`, `DefenseReportLayout`,
> `EnemyProbeCadence`, `FrameBudgetMeasure`, `PreviewRenderTextureSamples`, `RaidStagingMarker`);
> tooling (`f8-*.ps1` - WO-1460/1531; `tools/gen-sku-catalog.mjs` + `api/_lib/sku-catalog.generated.json`
> - WO-1532; both grep-confirmed. `tools/r2-ship.ps1`, `checkin_gate.ps1` grouped by path); canon docs. **Commit by explicit path, per lane.**
>
> ### Gate ladder - one red, and it is known
>
> - `Builds/cg-quiet.log` 20:04 - **`COMPILE_GATE_OK`**.
> - `Builds/reg-quiet.log` 20:07 - **RED**. Verbatim (the log is NUL-padded; `tr -d '\000'` before grep),
>   and both lines truncate in the log exactly as shown:
>   `REGRESSION_FAIL: 2 failure(s) (417/419 registered suites gree` and
>   `REGRESSION MARKER FAIL (1): hollow pass: NightMarketNoWalletRegressio`. Whether that is 2 items or
>   3 was NOT established. Fixes went into the lanes after this run; **all of it is UNPROVEN until the
>   wave-two re-gate.**
> - `Builds/r2-parity.log` 20:24 - **`R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=271`**.
>
> ### Owner actions outstanding (she must do these; do not do them for her)
>
> 1. With `$env:DATABASE_URL` set (it is redacted for every agent seat, so only she can run this):
>    `node tools/run-migrations.mjs --baseline 20260906_0019_promo_guest_redeem_ip_budget.sql` once -
>    records the already-applied files, applies nothing - then plain `node tools/run-migrations.mjs`.
>    Judge `MIGRATIONS_OK applied=N skipped=M`, never the exit code (`tools/run-migrations.mjs:57-82`).
> 2. Set `ADMIN_OPS_KEY` in the Vercel project env.
> 3. Approve the Solana dApp Store update - 4 screenshots + icon.
> 4. **Rulings open:** WO-1527 perk A/B; WO-1477 PrevPage wrap vs no-wrap; WO-1518 research SHORT/LOCKED
>    wording confirm; WO-1475 loot into a full bank (lost or retained); WO-1504 ranger (which is canon);
>    WO-1529 cleric price 1,025; WO-1486 prune keep-one-previous; WO-1446/1505 runner DROP-CONSTRAINT
>    exemption.
>
> ### DEPLOY HOLD
>
> **No `vercel --prod` until the WO-1446 migration runs.** WO-1449 and WO-1453 are FIXED in tree and
> explicitly held behind it. Deploying first puts code against a schema that has not moved.
>
> ### Documents written tonight
>
> `docs/GET_WELL_PLAN_2026-09-06.md`, `docs/READY_RCA_2026-09-06.md`, `docs/GROWTH_RCA_2026-09-06.md`,
> `docs/RAID_BALANCE_AUDIT_2026-09-06.md`, `CANON_GROUND_TRUTH_2026-09-06.md`. The raid audit is
> restructured to the owner's spine (verdict / why / fix first / retest gate / progression / rulings /
> questions / appendix) and carries the WO-1529 provenance: 205 is a row the x5 rescale missed; 1,025
> was intended.
>
> ### NEXT STEPS, IN ORDER
>
> 1. **Wave-two gate on the quiet tree:** compile gate, then regression. Judge the MARKER on a FRESH log,
>    never the exit code (CLAUDE.md section 8). Expect 419/419.
> 2. **Commit each lane by explicit path** - never `git add -A`. `git reset` after any 3-way apply.
> 3. **Flip each WO `**Status:**` in the same commit as its work; write the RESULT files.**
>    Then `python tools/board_build.py`.
> 4. **Tester APK:** `.\overnight-apk-build.ps1 -Tester` (repo root, not `tools/`).
> 5. **Hand to the owner for the Easy-camp retest per WO-1520's acceptance criteria.** She felt-verifies
>    and closes; the CLI does not close.

> ## ⚠ (previous) REFRESHED 2026-09-03 - SUPERSEDED 2026-09-06 - read `CANON_GROUND_TRUTH_2026-09-06.md` FIRST
>
> **The live anchor is `CANON_GROUND_TRUTH_2026-09-03.md` (repo root).** The 09-02 banner below is
> history now, kept, not guidance.
> *(⚠ That anchor line is FROZEN 09-03 text and is no longer true: `CANON_GROUND_TRUTH_2026-09-03.md`
> carries a SUPERSEDED banner. The live anchor is named in the top block of this file. Body left
> unrewritten per CLAUDE.md §15.)*
>
> **`2026.09.04.354315` IS THE PRODUCTION CANDIDATE.** On her Seeker; gates green on fresh logs -
> `COMPILE_GATE_OK` (`Builds/compile-gate.log` 20:10), `REGRESSION_OK 358/358 suites -- 358 green,
> 0 red, 0 skipped` (`Builds/regression.log` 20:13), `R2_PUSH_OK` and
> `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=266` (20:21). Branch
> `feat/synty-art-retheme`. ⛔ **"pushed" was WRONG here and is retired — never state a push state from
> a doc. Measure it: `git rev-list --count origin/<branch>..HEAD` (103 unpushed on 2026-09-06).**
> Version read off `ProjectSettings/ProjectSettings.asset:148,177`.
>
> **THE OPEN ITEM THAT MATTERS MOST HAS NO TICKET: SAVE DATA LOSS.**
> `[Flow:BaseLayout] Enter build mode CENSUS: live PlacedStructure(s) in scene=9, loader.Loaded=9,
> persisted BaseLayout=17` - eight structures gone, and the same shape (0 of 8) sits in
> `logs/device/*.log` from 2026-08-19/20, unworked. Emitter:
> `Assets/_Modules/Village/BuildMode/BuildModeController.cs:513-523`. Read the handover before
> picking anything else up.
>
> **Also open:** the 180s hold ceiling still applies to wallet signing (owner call, money attached,
> WO-1360 / `3e6ae4274`); the signing certificate cannot be proven to match the live release
> (`publishing/SUBMIT_CHECKLIST.md:101`, and its Gate A identity fields are now **STALE** - filled in
> against APK `354266`, shipped `354315`); the VFX Caster tool that authored four wrong tags is still
> unfixed; two silent API 400s are unticketed; textures at 98.9 MB remain the largest payload block.
>
> ⛔ **CLAUDE.md §11B landed today (`f1104a5fd`): never guess, prove it, and follow the documented
> procedure.** Every claim in the 09-03 handover names a commit, marker, log line or file:line.
>
> ⛔ **CLAUDE.md §7's action-bar face count was WRONG AGAIN and is corrected:**
> `HudActionBarModel.MaxVisibleFaces` is **4** (`Assets/_Modules/Core/HudModel/HudActionBarModel.cs:121`),
> pinned by `HudLabelFitRegression` Case 0 and `SessionShapeRegression`. The code and the suites are
> the authority; that line had already been corrected once, on 2026-08-26.

> ## ⚠ (previous) REFRESHED 2026-09-02 — SUPERSEDED 2026-09-06 — read `CANON_GROUND_TRUTH_2026-09-06.md` FIRST
>
> The **★★ SESSION HANDOVER — 2026-09-02 ★★** block below is the current one. Every banner and block
> beneath it is history, kept, not guidance.
>
> ⛔ **THE BRANCH CHANGED AND ALMOST NOTHING IN THIS FILE KNOWS IT.** The live branch is
> **`feat/synty-art-retheme`**. Every block below — including 08-21's — says
> `wip/village2-and-f8-tickets`, and those lines are frozen history per CLAUDE.md §15, not a
> correction you should make in their bodies. **Nothing is pushed.** Read branch and push-state off
> `git status` / `git log`, never off a block in this file.
>
> ⛔ **OWNER RULING 2026-09-02 EVENING — THE ANDROID APK IS THE PRIORITY; PI/WebGL IS PARKED, NOT
> CANCELLED.** Verbatim in `KEY_FACTS.md`. PROD-022 drops to a quiet read-only triage lane. If you are
> about to pick up a Pi ticket, you are working the parked lane — pick a player-felt APK defect instead.
>
> ⛔ **WO-1316 2026-09-03 — FOUR Vercel projects serve this game and TWO are public production.** A
> `--prod` deploy from this repo updates only the project `.vercel/project.json` links and reports
> success while the other production domain keeps serving an older build (measured 40,100 vs 32,609
> bytes, different Unity payload hashes, one day after a hand patch). **The surface list, the project
> ids and each project's purpose live ONLY in `tools/web-ship.ps1`'s `$Surfaces` registry — read them
> with `-ListSurfaces`, never copy them into a doc.** The parity gate is `tools\web-ship.ps1`
> (marker `WEB_PARITY_OK`, fresh `Builds\web-parity.log`, judge the marker not the exit code), wired
> into `tools\command-centre.ps1` step 6b. Two owner decisions are still open — which URL Pi's
> Developer Portal points at, and whether `defenders-webgl` is retired. See
> `CANON_GROUND_TRUTH_2026-09-02.md`.
>
> ⛔ **STANDING RULE 2026-09-02 — A BALANCE VALUE IS A TUNABLE, NOT A CONSTANT. DEFAULT ANSWER: YES.**
> Also verbatim in `KEY_FACTS.md`. The rail exists (`docs/PROD022_TUNABLE_FLAGS.md`); do not build a
> second one. You ask for a ruling only in the reverse direction — if a value must NOT be tunable, say why.
>
> ⚠ **The 08-21 banner immediately below is still RIGHT about one thing and WRONG about another.**
> Right: the numbers-live-at-source table. Wrong by two supersessions: **the pay path IS activated**
> (owner 2026-08-23, WO-1159), so an economy removal is no longer a clean purge — and **every unit of
> revenue so far is the owner's own** (2026-08-25). Both are on the `KEY_FACTS.md` card.
>
> ## ⚠ (previous) REFRESHED 2026-08-21 — SUPERSEDED 2026-09-02 — read `CANON_GROUND_TRUTH_2026-09-02.md` FIRST
>
> The **★★ SESSION HANDOVER — 2026-08-21 ★★** block below is the current one. Every banner and block
> beneath it is history, kept, not guidance.
>
> ⛔ **One correction big enough to sit in the banner: the game is PUBLISHED on the Solana dApp Store,
> but the PAY PATH HAS NEVER BEEN ACTIVATED — nobody has ever bought anything.** "Published on a
> store" and "taking money" are different facts, and this repo's canon has stated the first loudly for
> weeks while the second has never been true. Practical consequence: a currency/economy REMOVAL is a
> **clean purge**, not a balance-preserving migration — there is nobody to grandfather or compensate.
> Still read-migrate a removed save field so existing dev/test saves LOAD (ordinary defensive
> deserialisation, not value preservation). ⚠ This does **NOT** license flipping any payment flag.
>
> ## ⚠ (previous) REFRESHED 2026-08-18 — SUPERSEDED 2026-08-21 — read `CANON_GROUND_TRUTH_2026-08-21.md` FIRST
>
> The **★★ SESSION HANDOVER — 2026-08-18 ★★** block was the current one until 2026-08-21. It is kept
> below as history, not as guidance.
>
> ⚠ **Note for anyone tracing the chain:** the **2026-08-16 anchor never got a handover block** — the
> newest block before tonight was **08-09**, seven days behind the anchor it pointed at. If you are
> reconstructing what happened between 08-09 and 08-18, read the anchors
> (`CANON_GROUND_TRUTH_2026-08-16.md`, then 08-18), not this file's block sequence.
>
> ## ⚠ (previous) REFRESHED 2026-08-09 — SUPERSEDED 2026-08-18 — read `CANON_GROUND_TRUTH_2026-08-18.md` FIRST
>
> The **★★ SESSION HANDOVER — 2026-08-09 ★★** block below is the current one. ⚠ **The 08-08 banner
> directly under this is now WRONG on both its points:** the machine block is **RESOLVED** (rebooted
> 08-08 08:07:21; EXE + APK both built) and the **dungeon-stair hunt is CLOSED** (WO-930 shipped; root
> cause was stair YAW). It is kept below as history, not as guidance.
>
> ## ⚠ (previous) STALE as of 2026-08-08 01:00 — read `CANON_GROUND_TRUTH_2026-08-08.md` FIRST
>
> **08-08 anchor supersedes the 08-07 one.** ⛔ **The machine cannot build players** — commit charge
> 119.5 GB of a 127.8 GB limit with no Unity process running; EXE/APK/WebGL all OOM. **Reboot first.**
> Editor batchmode (gates, bakes, regression) still works. Also new since 08-07: `REGRESSION_OK
> 130/130`, two guards shipped and proven-red, WO-853 §7 ruled 50/30/20, WO-912 D2 settled to
> LevelPlay, and **three dungeon-stair hypotheses tested and killed** — do not re-run them.
>
> ## ⚠ (previous) STALE as of 2026-08-07 02:30 — read `CANON_GROUND_TRUTH_2026-08-07.md` FIRST
>
> This file was last refreshed 2026-08-06 ~10:50, BEFORE the overnight run. It does not know about:
> the wallet main-thread fix, the arena stranding fix, the Manage/Queues screen (bottom bar is now
> **6 faces**, Upgrade re-pointed, Map moved into Bag and flag-gated off), **save schema v37**, the
> rewarded-ad gate, town suspension while in a dungeon, the barracks adoption, the F8 harness
> un-blinding, or the UI seat moving to the **1000-block**.
>
> Per CLAUDE.md §15 this is a `STALE:` flag rather than a rewrite — the anchor wins on any conflict.
# HANDOVER — the one sheet a new session reads to be productive now

> **Read order for a new session:** the newest ★★ SESSION HANDOVER block immediately below (currently
> **2026-09-02**) → this sheet → `../CANON_GROUND_TRUTH_2026-09-02.md` (the live reality anchor; each
> anchor deltas the one before it, back through 08-23 → 08-21 → 08-18 → 08-16 → 08-09 → …) →
> `../KEY_FACTS.md` (the LIVING card — it holds the current owner rulings and beats any dated doc) →
> **`CLI_OPERATIONS_RUNBOOK.md`** (2026-08-27 — the PROCEDURE doc: startup, gates, ship, deploy, in one
> file. `CLAUDE.md` is the LAW, that runbook is HOW to run the machine, and this sheet is WHY each rule
> exists. It is the most accurate operational doc in the repo; read it before you run anything) →
> `MASTER_CATALOG.md` (mandatory, be the SME) → `ARCHITECTURE.md` (the architecture hub) → the relevant
> `MASTER_CATALOG/<area>.md` for what you're about to touch. **ALSO** skim the auto-memory index
> `MEMORY.md` (index lines are pointers — read the file before asserting). The code wins on truth —
> comments lie. (The old `OVERNIGHT_AUTOPILOT_LOG.md` mandate is retired; that log is archived at
> `_archive/root/OVERNIGHT_AUTOPILOT_LOG.md` for history only.)
>
> **Canon maintenance (WO-520, BINDING — CLAUDE.md §15):** the single live anchor is
> `CANON_GROUND_TRUTH_<date>.md` at repo root. Update the relevant load-bearing doc in the SAME
> change as any architecture/state/canon shift (or add a top-of-file `STALE:` flag). Weekly 5-minute
> skim of the read-first set against the anchor. Dated ledgers are frozen — banner, never rewrite.

---

## ★★ SESSION HANDOVER — 2026-09-06 (WO-1441: the wallet session that was never created) ★★

**One line:** cloud save has been dark for **every wallet holder who never bought a pack** since
2026-08-27, because the method that creates the backend session **had zero call sites**.

- **Proof, not inference** (pid 7170, `logs/debug/raid-no-abilities-2026-09-06.log`): `Connect OK`
  12:50:06.956 → `session warm-up deferred` .960 → `why=missing /api/game/save` 12:50:11.556, and
  **`MintSessionAsync` appears ZERO times across 76 MB of that day's four captures.** `why=missing`
  means NEVER CREATED — not expired, not refused.
- **`MintSessionForExplicitConnectAsync` was never called by anything.** Both connect paths called
  `WarmUpSessionAsync`, which does not mint and traced *"first authenticated action will mint"* —
  false since WO-1157 made minting `allowMint`-only (purchases + promo). **Cloud save is neither.**
  Now: both paths mint; `WarmUpSessionAsync` is **deleted** (an uncalled method is what caused this).
- ⭐ **OWNER RULING 2026-09-06 — auto-resume MINTS. This reverses WO-1211's "boot never signs."**
  Auto-resume shows no connect prompt, so the handshake is the session's only wallet sheet. WO-1211's
  reasoning is preserved verbatim in-code at `WalletSkinBootstrap.TryAutoResumeAsync` — only its
  arithmetic (it assumed an EXTRA prompt) was wrong. First-run players still see nothing.
- ⛔ **A REGRESSION ORACLE WAS PINNING THE LIE.** `BackendSaveAuthRegression` *required* the literal
  string `"first authenticated action will mint"`, guarding a false trace against correction for ten
  days. **An oracle that pins PROSE pins whatever the prose says, true or not — pin behaviour.** The
  new pin is the one that would have caught this: the mint's **call site**, not its body.
- ⛔ **Session renewal already existed in production, undocumented and UNCAPPED.**
  `wallet-auth.verifyWallet` tries the session rail first, so `POST /api/auth/session` with a valid
  `X-Session` and no nonce always returned a fresh token with no signature — i.e. one signature could
  be walked forward forever, the "permanent login" that file's own TTL comment forbids. WO-1441 adds
  the **ceiling** (`signed_at`, 12 h absolute, token rotated on renew), not the feature.
  **Needs `api/schema.sql` applied**; until then it degrades to today's rail on purpose.
- **`NightMarketSharedCardSession.OpenBrowser()` is a card modal, not a web browser.** Two log lines
  14 s apart with "nothing coming back" were stack frames, not a failed round trip. If you arrive
  there hunting a deep link, stop — there is none.
- **Verified from the client only.** The fix reaches the owner through a **store submission**; no
  server deploy can help, because the client aborts before sending. Outstanding device proof: a
  successful save, the queue drain (`offline queue DRAINED`), and that renewal actually renews.

---

## ★★ SESSION HANDOVER — 2026-09-02 (the night "data-driven" turned out not to mean what everyone assumed) ★★

**Anchor:** **`../CANON_GROUND_TRUTH_2026-09-02.md`** — minted tonight, supersedes the 08-23 anchor
(bannered/frozen). Read it before this block; it carries the owner rulings verbatim and the file:line
citations. Read **`../KEY_FACTS.md`** next: it is the LIVING card, edited in place, and it beats any
dated doc including this one. This block is the **operator's manual half**: how you work here, and why
each rule exists — with the incident that forged it, because a rule without its incident gets skipped.

### Where every number actually lives — the 08-21 table still stands, and nothing on it has moved

Do not copy a number into a doc, **including this one**. Every duplicated number in this repo has
eventually rotted and cost a session: the stale WO block (CLAUDE.md §2), the retired assembly table
(§5), the hardcoded repo root (§0), the drifted R2 push (§16). Read the 08-21 block's table below —
save schema off `SaveSchema.CurrentVersion`, next WO number off **your seat's** `CLI_LANES_WO_NUMBERS.md`
banner row (bumped in the SAME edit as the mint), gate results off the MARKER on a **fresh** log under
`Builds/`, board status off `python tools/board_build.py`, HEAD and push-state off `git log` / `git status`.

**Tonight added a fresh instance of the same class, and it is worth reading as a warning:** the
`Builds/overnight-apk-status.txt` stamp said `R2_PARITY_FAILED — DO NOT INSTALL OR DISTRIBUTE` while
`Builds/r2-parity.log` — the actual proof — verified the exact catalog that APK requests plus every
bundle it names. The APK was believed unshippable for most of a day because a status file was a second
copy of a fact that a later push had made obsolete. **Judge R2 by `r2-parity.log`, never by a status stamp.**

### State tonight, in the only form that cannot go stale

Branch **`feat/synty-art-retheme`**. **Nothing is pushed** — the standing cadence is unchanged: commit
local, push only after the owner retests (felt/gameplay) or a regression proves it (data/logic). Count
the unpushed commits with `git rev-list --count`; do not trust a number written in a handover.

The APK is the shipping artifact. `.githooks/pre-push` still refuses any push whose `ServerData/` bytes
postdate the `R2_PARITY_OK` proof, there is deliberately no override flag, and it is wired by
`git config core.hooksPath .githooks` — **local config, set once per clone, does not travel with the tree.**

### ⛔ THE OWNER RULING THAT REORDERS THE BOARD: the APK is the priority, Pi is PARKED

Verbatim in `../KEY_FACTS.md`: *"we have spent most of today triaging and trying to get Pi to work, but
have made almost no progress"* / *"so we need to shift back to the apk. thats the real vision so that
needs to be the priority."*

- The **Android APK / Seeker** path is the active lane. Player-felt gameplay defects and polish outrank
  every Pi/WebGL ticket.
- **PROD-022** (the Pi Browser crash loop) drops from P0-active to a **quiet read-only triage lane**.
  Its Lane A `[PiLifecycle]` instrumentation is deployed to production and gathers evidence on its own —
  that is the point of instrumenting, and it is why parking costs nothing.
- The Pi/WebGL cluster is **PARKED, NOT CANCELLED.** It resumes on the owner's word, not on a seat's
  judgement that Pi looks tractable again.

**The evidence, recorded so nobody re-litigates it:** of the 27 commits made on 2026-09-02 before the
ruling, exactly ONE was gameplay — and it landed nine minutes after that morning's APK was built, so it
was not even in the artifact. **A full day of triage produced no player-facing progress.** The cost of
ignoring this rule is not abstract; it is precisely one day, already paid.

### ⛔ THE STANDING RULE THAT CHANGES HOW YOU WRITE EVERY NUMBER: a balance value is a TUNABLE

Verbatim: *"be smart, dont make it need a code change, make it tweakable from a db call"* — followed by
**"i have been screaming this for months."**

**Read the second sentence as the actual defect.** The idea was never rejected. It was agreed to in
conversation, repeatedly, and never written to disk — the failure this canon already named on 08-23:
*a ruling recorded but not applied is indistinguishable from no ruling.* It is written down now, so no
seat needs telling again.

**THE RULE:** when you are about to hardcode a number that exists to be TUNED — a drain ratio, a
cooldown, a cost, a rate, a threshold, a duration, a drop chance — you register it on the remote
tunables rail instead. **You do not ask whether it is worth it; the answer is yes by default.** Ask for
a ruling only in the reverse direction: if you believe a value must NOT be tunable, say why.

**DO NOT BUILD A SECOND RAIL — it already exists.** Contract and worked example:
`PROD022_TUNABLE_FLAGS.md`. Registry/defaults `Assets/_Modules/Core/Ops/RemoteTunables.cs` · transport
`RemoteTunablesService.cs` · server allowlist `TUNABLE_KEYS` in `api/_lib/tunables.js` · operator surface
`tools/command-centre.ps1 -Tunables` and the phone via `POST /api/admin/ops` · oracle
`Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs`. **All four sources change in the SAME
commit**; the `[tunable-defaults]` oracle goes red naming which two disagree.

**THE INVARIANT THAT OUTRANKS THE FEATURE:** *no row, no network, no server, no parse ⇒ TODAY'S
BEHAVIOUR, EXACTLY.* The registered default must equal the value the constant would have had. The remote
read is an OVERRIDE, never a dependency, and never blocks or delays boot. **An empty `client_tunables`
table is the correct resting state and is what ships.**

**Why it is worth real effort, in the owner's own economics:** a WebGL rebuild costs ~30 minutes, an APK
~10. A knob reaches a running client in ~40 seconds. Every value left hardcoded converts one of her
felt-tests into a half-hour round trip — and she is the only person who can judge feel, so that cost
lands entirely on the one resource the project cannot buy more of.

⚠ **Scope it honestly:** this is for BALANCE and PRESENTATION levers. It is NOT for anything
server-authoritative — prices, entitlements, grants — which stay on the quote/verify rail where the
SERVER is the authority and a client-side override would be an exploit.

### ⛔ THE DISCOVERY OF THE NIGHT — "data-driven" in this repo does NOT mean "tunable without a rebuild"

This is the single most load-bearing thing on the page, because it invalidates an assumption the project
has operated on for months, and it explains why the screaming never worked.

**`LocalJsonCatalogSource` resolves `Resources.Load<TextAsset>` FIRST, on every platform, and
`Assets/Resources/` is COMPILED INTO THE PLAYER.** Therefore:

- Editing any of the 71 canonical JSONs costs a **full build** — ~10 min APK, ~30 min WebGL.
- Editing the **StreamingAssets twin changes nothing at all.** It is not read when the Resources copy
  resolves.
- **Five canonical files advertise in their own authoring notes that she "retunes with NO recompile."**
  That sentence is literally true (no C# recompiles) and false in the only sense she experiences. It is
  misleading, and it misled everyone — including seats who read it as proof the problem was already solved.
- **Every past attempt to fix tunability by moving numbers into JSON was working on the wrong axis.**
  If you catch yourself proposing "make it data-driven" as the answer to a tuning round trip, you are
  about to repeat that mistake. The axis that matters is *what resolves at runtime*, not *what format the
  number is written in*.

`CanonicalJson.Source` is a settable `ICatalogSource`, documented in its own comments as a one-line swap
to a remote source — and it was **assigned nowhere**. **WO-1331 connects it**, shipped **flag-gated and
OFF** (`FeatureFlags.RemoteCatalogs` ⇒ `Get("catalogremote", defaultOn: false)`). Off was
non-negotiable: this changes how every catalog in the game loads, the blast radius is the whole product,
and the game is live on a store with the pay path activated. With the flag off, resolution is exactly as
before. Scoped deliberately to five allowlisted catalogs (`RemoteCatalogOverrides.Allowlist`), not all
71; a payload is validated BEFORE it replaces anything, because a half-parsed catalog overwriting a good
one is strictly worse than no feature at all. New suite: `[catalog-seam] CATALOG SEAM OK`.

**Two findings left deliberately unactioned, both owner decisions, both recorded so they are not lost:**

1. **`heart.json` and `towers.json` have NO RUNTIME READER AT ALL** — only a regression asserting they
   are *served*. Shipped Heart is 100 HP with 2 HP/s regen; the authored files say 160 HP with zero
   regen. **The game has been ignoring reviewed, authored balance data.** Wiring them moves live balance
   in two directions at once, so it is an OWNER RULING, not a cleanup.
2. **`ServerConfig.cs` is a DEAD SECOND MECHANISM** — 11 fields fully wired client-side and consumed in
   `WaveManager`, but `api/game/load.js` has never emitted a config key, so none of it has ever been
   settable. **Retire it or fold its useful keys onto the rail; do NOT build the missing server half** —
   that would create a second configuration mechanism, the class of duplication this repo pays for most.

The full survey is `reference/TUNABLE_LEVER_INVENTORY.md`: 33 distinct constants, every one opened at
source, ranked by the owner's real cost — how likely she is to tune it × how expensive the round trip is
today. Use it as the work queue for the rail.

### What landed tonight, and the transferable lesson each one paid for

Read the commit bodies (`git log --since=2026-09-02`); each carries its RCA and its proving line. The
lessons outlive the fixes:

1. **A detector that fires on every healthy case is worse than no detector** (WO-1301/1302). It trains
   every future reader to ignore it, so the one real failure arrives wearing the same face as a thousand
   false ones. `OpenItemPicker` announced itself to the arbiter three lines before assigning the field
   the arbiter probes, so the ghost-modal verdict was false **by construction** on every open. And
   `DependencyClosureTrace` asked two hardcoded texture-property names and read "not those two" as "no
   albedo", so every fully-textured Synty shader-graph material reported as an untextured grey blob. The
   fix asks the SHADER for its properties and classifies by token: **a classifier, not a name allowlist**
   — an allowlist needs a new entry per shader and rots.
2. **A release seam that exists and an owner that never reaches for it** (WO-1308, the wolf stuck in
   fight). `WaveManager` raises a global battle-lock through `_phase == Active` and was the one such
   owner that never registered a battle-session unwind, so a retreat announced the session end and
   nobody asked the wave loop whether its claim was still true. Same family as the August arena softlock,
   confirmed rather than assumed. **When you add a global claim, register its unwind in the same change.**
3. **Guard the write, not the theory** (WO-1299). One unguarded `AudioSource` write inside an unawaited
   `async UniTaskVoid` threw on scene teardown, surfaced only through the unobserved-task handler, and
   left `_fading` latched — so the *next* crossfade hard-stopped both sources. That is the audible "music
   cuts out on a transition". Every bail now clears the latch and emits a `FlowTrace.Warn` naming the
   reason: **no silent catch, ever** (§12).
4. **An impossible candidate list means you are reading the wrong file** (WO-1293). All three causes the
   ticket listed were structurally impossible. The real one was a fourth: a second `LayoutGroup` added to
   an object that already carried one — and `LayoutGroup` is `[DisallowMultipleComponent]`, so
   **`AddComponent` returned NULL** and the next line dereferenced it. It fails silently and reads as
   working software, which is how it survived. The evidence that would have named it instantly is now
   permanent: the probe line carries a LayoutGroup census.
5. **Instrumentation is the fix's other half** (WO-1298, WO-1300). A suppressed hero wrote `Speed = 0`
   into the animator every frame while something else moved the root — "input taken away" is not
   "standing still". A wedged tutorial walk beat emitted **zero** probe lines, because the probe returned
   in silence on a null hero, and the founding band's only publisher sat behind a signal that a fault
   upstream guaranteed could never be raised. Both now trace. **A bug that cannot report itself next time
   is only half fixed.**
6. **Correct the premise before writing code** (WO-1330). The CLI grepped, found damage-over-time in
   `DeNelle.BattleATB`, and reported the mechanic as existing. The owner said *"it doesnt but it wouldnt
   be too challenging"* and she was right: BattleATB is the **superseded** turn-based path — proven, not
   assumed, by `ff.dungeonrealtime` defaulting ON and `ATBCombatManager`'s GUID appearing in zero scenes
   or prefabs. **A DoT living only there is one the shipping game cannot cast.** What actually existed was
   four unrelated ad-hoc tick loops, none tunable; they are now one over-time engine that cannot be built
   without a liveness test.
7. **Three facts, three owners, none written twice** (WO-1328, the Command Center Balance tab). Key, kind
   and default are **parsed out of `RemoteTunables.cs`** by `tools/gen-tunable-manifest.mjs`, which
   THROWS on an unresolvable entry rather than silently dropping it; may-this-key-be-written comes from
   `TUNABLE_KEYS` by direct require; only area, label, prose and safe-range are hand-authored. The oracle
   re-parses the `.cs` from disk every run and asserts the checked-in JSON is byte-identical to a fresh
   derivation. **It caught two real drifts unprompted, within the minute** — parallel lanes landed new
   knobs into the shared tree mid-flight and it went red naming each. That is what a non-duplicated fact
   looks like in practice; copy the shape.
8. **The healthy-looking target was the degraded one** (the wolf's coat). The premise was backwards and
   measurement said so: `wolf_color` is **absent** from the WebGL payload and present in the Android and
   Windows ones, so Pi looked correct *because the coat map never shipped there* and the species-tint
   fallback painted a clean pale body. The APK binds the real map — mean saturation 0.091, genuinely a
   grey coat — and multiplies the pale tint into it. **When one platform looks right and the others look
   wrong, measure before assuming the right-looking one is the reference.**
9. **Most of a stale ticket can already be built** (WO-1294) — verify at source before re-implementing.
   The real remaining defect was invisible because **nothing compared the two files**: the talent tree
   resolves art through `talent-icon-map.json`, the hot-swap bar through `concept-icons.json`, and only
   the tree side had an oracle. Three abilities rendered as different icons in the two surfaces.
10. **Name what the data does NOT prove** (WO-1327). Real misconfiguration was found on the fire spell's
    emitter and fixed — and the commit says plainly it is **not the proven root** of the owner's two
    captures, because the instrumented root for one of them landed the same day and the YAML alone cannot
    settle the other. **Shipping a correct fix while refusing to claim it closes the ticket is the
    discipline**, not a hedge.

### The build / gate / ship cycle — unchanged, and still the order that matters

1. **Be the SME first.** `MASTER_CATALOG.md` + the `MASTER_CATALOG/<area>.md` for what you will touch.
   The code wins on truth; comments lie.
2. **Instrument before you edit (§12).** No code edit on a non-trivial bug until you can cite CAPTURED
   DATA. Static reading LOCATES candidates; it never CONCLUDES. **Never strip a `FlowTrace` or a
   `Guard`** — flag it off if you must, the calls stay, because a stripped `Warn` turns a logged failure
   back into a silent one and the next regression in that system starts from zero evidence.
3. **Compile gate**, judged by `COMPILE_GATE_OK` on a **fresh** log — never by the exit code. This repo's
   runners exit 0 on refusals and FAILs. Always pass `-ExpectMarker`; without it you get
   `VERDICT=PASS-UNASSERTED`, which proves nothing about which run produced the text.
4. **Data regression** → `REGRESSION_OK <n>/<n> suites`. The three entry points emit DISTINCT markers
   (`REGRESSION_OK` / `CHECKIN_SUITE_OK` / `SESSION_GUARDS_OK`) so a small suite's pass can never read as
   the full suite's. **Read the count off the marker; never restate one from a doc.**
5. **Eyes, for anything visual.** Headless gates cannot see orientation, layout or colour. Open the PNGs
   or take a device screencap. FlowTrace shows what the code believes; the screenshot shows what the
   player sees.
6. **Never bake with the Unity editor open**, and **never hand-edit a curated `.unity`**.
7. **Ship through `tools\r2-ship.ps1`** — one file, push + verify, marker-judged. Bundle names are
   content-hashed, so **every content build needs its own push**; "I pushed yesterday" is never an answer
   and a full-looking bucket proves nothing. A raw `adb install` of a hand-built APK bypasses all of it.

The full procedure — startup order, every command, every marker — is **`CLI_OPERATIONS_RUNBOOK.md`**
(2026-08-27). `CLAUDE.md` is the LAW, that runbook is the PROCEDURE, this sheet is the WHY. Where the
runbook and `CLAUDE.md` disagree, `CLAUDE.md` wins and the runbook is the thing that is stale.

### Resume points

1. **Owner felt-test of the current APK** — that is the gate on pushing any of tonight's work.
2. **Work the APK lane.** Pull player-felt gameplay and polish tickets off the board
   (`python tools/board_build.py`). Do not open a Pi ticket without an owner word.
3. **Walk `reference/TUNABLE_LEVER_INVENTORY.md` top-down**, migrating constants onto the rail in its
   ranked order. Four sources per knob, one commit, or the `[tunable-defaults]` oracle goes red.
4. **Two owner rulings owed, both from WO-1331:** whether to wire `heart.json` / `towers.json` (it moves
   live balance in two directions at once), and whether to retire `ServerConfig.cs` or fold its useful
   keys onto the rail.
5. **`FeatureFlags.RemoteCatalogs` stays OFF** until the seam is proven. ⚠ A default flip is not a state
   change on a machine that already answered the question — `FeatureFlags.Get` reads PlayerPrefs FIRST,
   so clear the pref when testing a default.

---

## ★★ SESSION HANDOVER — 2026-08-21 (the gate-sweep night: gates that passed while asserting nothing) — SUPERSEDED (see 2026-09-02 above) ★★

> ⚠ **FROZEN, NOT REWRITTEN (CLAUDE.md §15).** Two lines in this block are now false and are left
> standing on purpose: the branch is **`feat/synty-art-retheme`**, not `wip/village2-and-f8-tickets`;
> and its "nobody has ever bought anything" framing was overturned on 08-23. Its numbers-live-at-source
> table is still binding.

**Anchor:** **`../CANON_GROUND_TRUTH_2026-08-21.md`** — minted tonight, supersedes the 08-18 anchor
(bannered/frozen). Read it before this block; it carries the owner rulings verbatim and the file:line
citations. This block is the **operator's manual half**: how you work here, and why each rule exists.

### Where every number actually lives — do not copy one into a doc, including this one

This repo's most repeated failure is a number written down twice. It has now produced the stale WO
number block (CLAUDE.md §2), the retired assembly dependency table (§5), the hardcoded repo root (§0),
the drifted R2 push (§16) and — tonight — a fallback cost table covering 3 of 28 rows that has drifted
four times (**WO-1137**). So, every time:

| You want | Read it off | Never off |
|---|---|---|
| Save schema version | `SaveSchema.CurrentVersion` (`Assets/_Modules/Core/State/SaveSchema.cs`) | any doc |
| Next free WO number | the `CLI_LANES_WO_NUMBERS.md` banner row **for your seat** | the filesystem max, a backlog doc, this file |
| Suite counts / gate result | the MARKER on a **fresh** log under `Builds/` | a remembered count |
| Board / ticket status | `python tools/board_build.py` -> `BOARD.html`, derived from `WorkOrders/*.md` | Notion, Linear, a task list (all retired) |
| HEAD, ahead-count, tree state | `git status` / `git log` / `git rev-list --count origin/<branch>..HEAD` | a snapshot in a handover |

**The rule that makes the WO banner work: bump YOUR seat's banner row in the SAME edit as the mint.**
A mint on disk without a banner bump *is* the collision. Two disjoint blocks exist (main line and the
UI seat) precisely so both seats can mint in parallel without reading each other's state.

### State tonight, in the only form that cannot go stale

Branch `wip/village2-and-f8-tickets`. **Nothing is pushed** — the owner felt-verifies first, and the
standing cadence is *commit local, push only after she retests (felt/gameplay) or a regression proves it
(data/logic)*. Count the unpushed commits with `git rev-list --count`, do not trust a number here.

Gate on a fresh log: **`COMPILE_GATE_OK` present**; `DataRegression` reports **`REGRESSION_FAIL: 2
failure(s)`**, so **`REGRESSION_OK` is ABSENT — and absence of the marker is a FAILURE, never an
unknown.** Both failures are ticketed ASSET gaps that no code change can close: **WO-1135** (no tracked
material exists for any of the three wall tiers — the art has always ridden each FBX's embedded
material, which binds to a `.fbm` folder that is not in this repo, so a paid tier upgrade renders as
untextured white slabs with no error on screen) and **WO-1136** (`staff_A` is geometrically symmetrical
— `relGap 0` on both the taper and the grip test — so no sheathe orientation is derivable at all).
**Both oracles were committed RED on purpose: a gap nothing checks is a gap nobody fixes.**

Build: Seeker APK at `Builds/Android/DefendersOfTheRealm.apk` with **`R2_PARITY_OK`** stamped in
`Builds/overnight-apk-status.txt` — content is proven hosted, so no capsule enemies.

### The build / gate / ship cycle — the actual order, and what each step is for

1. **Be the SME first.** `docs/MASTER_CATALOG.md` + the `docs/MASTER_CATALOG/<area>.md` for what you
   are about to touch. The code wins on truth; comments lie.
2. **Instrument before you edit** (CLAUDE.md §12). No code edit on a non-trivial bug until you can cite
   CAPTURED DATA. Static reading LOCATES candidates; it never CONCLUDES. Never strip a `FlowTrace` or a
   `Guard` afterwards — flag it off if you must, the calls stay.
3. **Compile gate:** `powershell tools\run-unity-method.ps1 -Method DeNelle.Editor.CompileGate.Run
   -LogName <name>.log`. Judge by `COMPILE_GATE_OK` on that fresh log — **never by the exit code**;
   this repo's runners exit 0 on refusals and FAILs.
4. **Data regression:** `DeNelle.Editor.DataRegression.RunAll` -> `REGRESSION_OK <n>/<n> suites`. The
   three entry points emit **DISTINCT** markers on purpose (`REGRESSION_OK` / `CHECKIN_SUITE_OK` /
   `SESSION_GUARDS_OK`) so a small suite's pass can never read as the full suite's.
5. **Eyes, for anything visual.** Headless gates cannot see orientation, layout or colour. Run the UI
   capture pass and **open the PNGs**, or take a device screencap. A screenshot is primary evidence for
   a visual defect: FlowTrace shows what the code believes, the screenshot shows what the player sees.
6. **Never bake with the Unity editor open** (project lock), and **never hand-edit a curated `.unity`**
   — rebuild through the builder method.
7. **Ship through the scripts, never by hand** (§16, below).

### Shipping content: the one file, and the one per-clone setup step

Enemy and structure **art is served from the R2 CDN and there is no local fallback**. A build whose
bundles were never uploaded installs, launches and plays — with tinted capsule enemies and no error on
screen. **Bundle names are content-hashed, so EVERY content build needs ITS OWN push**; "I pushed
yesterday" is never an answer, and a full-looking bucket proves nothing.

- The sanctioned path is **one file: `tools\r2-ship.ps1`** (push + verify, marker-judged, exit 16 on
  failure). `morning-ship-chain.ps1` and `overnight-apk-build.ps1` call it and BLOCK;
  `install-apk-to-seeker.ps1` calls it `-WarnOnly` (a deliberately-offline sideload is legitimate there
  and only there). **Do not re-inline the push or the verify anywhere** — that duplication had already
  drifted between the two chains once.
- ⛔ **A raw `adb install` of a hand-built APK bypasses all of it.** Installing or distributing goes
  through the scripts.
- **`.githooks/pre-push` refuses any push whose `ServerData/` bytes are newer than the
  `R2_PARITY_OK` proof in `Builds/r2-parity.log`.** There is deliberately no override flag. It is wired
  by `git config core.hooksPath .githooks` — **local config, so it does NOT travel with the tree: set it
  once per clone.** (Verified set on this machine.) A docs-only push passes untouched.

### THE LESSON OF THE NIGHT — a gate that passes while asserting nothing

**Hollow passes were found in TWO separate suites in one run.** A hollow pass is a case that returns
GREEN while asserting nothing — typically `if (dependencyMissing) { notes.Add("SKIPPED ..."); return; }`
where the notes feed the SUCCESS string, so **the caller's only channel is the bool and a skip IS a
pass**. Six in the cosmetic suite, plus a silently vacuous raid-cooldown case. **Only ONE of the six was
caught by the existing ratchet**; the other five escaped because its detection window is about **four
lines**, i.e. its coverage is a function of code formatting — the least reliable signal available
(**WO-1138**).

> **The taxonomy that came out of it — apply it to every regression case you write:**
> - **fixture-absent -> FAIL**, naming the missing path.
> - **harness-capability-absent -> a VISIBLE stand-down** that can never be read as a pass.
> - **content/art-absent -> assert THROUGH it** — the proven fallback path becomes the assertion.

Why this class is the most expensive one here: a gate that reports success without proving it does not
merely fail to catch a bug — it **actively asserts the bug is absent**, and work then proceeds on that
assertion. That is strictly worse than having no gate at all.

### Four more transferable rules, each paid for tonight

1. **Check the fixture's health before believing a failure.** The harness `DestroyImmediate`'d a state
   while it was still installed; Unity fake-null then made every read return null, producing a
   regression failure that looked *exactly* like a real product defect (a cooldown window of 0) — and a
   silent vacuous green in the neighbouring case. `HeadlessState` now re-installs a live state before
   teardown and asserts its own health, so a demolished fixture can never masquerade as either again.
   **A red that describes a plausible product bug is still, first, a claim about the harness.**
2. **When you relocate code, check what was asserting against its old location.** Moving the siege
   cadence and ledger stamps out of a swept directory (into `SiegeClock.cs`, one directory outside the
   sweep, to keep skippable queue/economy time separate from never-skippable combat time) would have
   **silently disarmed two rules** in the sibling `SiegeSpawnAuthority` lint. The fix extended that
   lint's single-file guard into a file ARRAY covering both homes. **A fix that quietly removes a guard
   is the failure this repo keeps paying for** — the oracle must follow the code.
3. **When reconciling parallel seats, PRESERVE before you delete.** Two agents independently built the
   same two screens because neither could see the other. The canon-compliant pair survived; the retired
   half was **uncommitted work with no commit to fall back to**, so its full text was pasted verbatim
   into its work order *before* deletion (see WO-1053's "RETIRED DUPLICATE" section). Losing seat, wired
   or not: nothing is unrecoverable only if you preserve it first. (This is also why there is exactly
   ONE committer, staging by EXPLICIT PATH — never `git add -A`.)
4. **Never let an oracle be weakened to make a change fit.** Tonight's pattern held in both directions:
   a genuinely-correct exception was **pinned in the existing owner-pinned table with its reason** (the
   store beacon's loop flag) rather than by widening a derivation that would have flipped seven
   siblings and re-opened a real leak; and a convex-pricing exponent is **hard-failed at `e >= 1`** so
   the word cannot be used to undo the owner's ruling. Pin the exception, never soften the rule.

### What is still OFF, and why (do not flip these)

- **`FeatureFlags.Siege` OFF** (`Assets/_Modules/Core/FeatureFlags.cs`, `defaultOn: false`) until
  **WO-1139** lands the ruled loss stakes. The cadence would otherwise open sieges that resolve and
  report but take nothing.
- **`FeatureFlags.RealmStorePurchase` OFF**, mainnet block unlifted. Monetization stays off until the
  owner rules; see the banner at the top of this file.
- **No cosmetic or SKR rows are authored in the battle pass**, and a regression **fails the build** if
  either is authored before its gate opens (no art; no `ISkrLedger`).
- ⚠ **A default flip is not a state change on a machine that already answered the question** —
  `FeatureFlags.Get` reads PlayerPrefs FIRST. Clear the pref when testing a default.

### Resume points

1. **Owner felt-test of tonight's APK** — that is the gate on pushing any of it.
2. **WO-1139** loss stakes (ruled; note the stakes ruling reversed twice inside one exchange — read all
   three in WO-1026, the third is live), then **WO-1126** (glimmer purge + `BattlePassManager`
   retirement), **WO-874** (wire elite VFX), **WO-887** (map the five owner-tagged surface impacts),
   **WO-1133** (inventory redesign — half of it is removal), **WO-1134** (endgame loop, fully ruled).
3. **The four tickets minted tonight, all READY:** 1135, 1136, 1137, 1138. **1138 is the leveraged one**
   — widen the ratchet to a control-flow relationship instead of a line window, then re-run it across
   every registered suite and triage what it surfaces. Two suites were read by hand in one day and both
   were dirty; expect more.
4. **Still owner-owed:** WO-823 first-raid softness, WO-1029/PROD-012 backend + online-required, R5/R6
   buy button and season pass.

⛔ **One trap worth naming before you touch the raid ladder:** the ruled terminus (12/18/24 clears)
deliberately **DIVERGES** from `TribeManager.ClearsUntilGone`. Copy the shape of a terminating ladder,
**never the vanishing** — camps PLATEAU and remain repeatable. A camp that disappears deletes the loop.

---

## ★★ SESSION HANDOVER — 2026-08-18 (the overnight loop: the correction pass that corrected the wrong file) — SUPERSEDED (see 2026-08-21 above) ★★

**Anchor:** **`../CANON_GROUND_TRUTH_2026-08-18.md`** — minted tonight, supersedes the 08-16 anchor
(bannered/frozen). Read it before this block; it carries the file:line citations.

**No HEAD sha, gate count or WO number is recorded here.** Read them off `git status` /
`git rev-list origin/..HEAD`, the MARKER files under `Builds/`, and the `CLI_LANES_WO_NUMBERS.md`
banner. Written overnight while the CLI seat ran a fix-and-verify loop; the owner's instruction was
*"Check in everything and notate the record as well."*

### The headline — orientation was fixed in a file nothing reads

`f995c4706` set `bakeAxisConversion: 1` on ten structure FBXs and zeroed ten rows in
`Assets/OffsetForge/offsets.json`. **Those rows are INERT for structures** — `AttachmentOffsetRegistry`
is keyed by hero/enemy attachment mesh ids. The LIVE channels are **(a)** `entry.orientation` in
`structures-catalog.json`, applied at `Assets/_Modules/Village/Catalog/StructureFactory.cs:151-158`
when `manual == true`, and **(b)** hardcoded `pitchDeg` in
`Assets/_Modules/Village/HubStructureVisualInjector.cs` (~:81-91) for hub-scene swaps. Both still
carried the legacy `-90`, so **bake AND legacy correction were both applying** — models lying down.

Channel (a) fixed tonight for `forge`, `workshop`, `jeweler`, `barracks`, `tower_ballista`; catalog
**version 22 → 23**. **Channel (b) was IN FLIGHT at time of writing — treat as NOT done and verify.**

### ⛔ Eight `-90`s are CORRECT and MUST STAY

`pet-house`, `market`, `arcane-tower`, `collector_farm`, `collector_lumbermill`, `lumberyard`,
`foundry`, `silo` — their FBX metas read `bakeAxisConversion: 0`, so the `-90` is what stands them
up. A "tidy up the remaining -90s" pass breaks all eight, **including `collector_lumbermill`, the
FTUE's first building**. The rule is **"-90 is legacy IFF that FBX's meta says
`bakeAxisConversion: 1`"** — check the meta, per asset, every time.

**The lesson:** the pass fixed the file it could see, not the file that is read, and **no gate could
tell** — headless gates cannot see orientation, which `f995c4706`'s own message conceded ("sits
correctly in the town is a felt claim"). The instrument exists in-repo and went unused:
`Assets/Editor/WoodenWatchtowerBuilder.cs:277` `UprightAspectMin = 1.2f` (1.70–1.92 upright vs
0.52–0.59 lying down). Filed **PROD-008**.

### The rest of the night, in one line each

- **Realm Store (PROD-003)** — upright and facing the plaza via an owner-authored
  `Quaternion.Euler(0, 180, 90)` on `RealmStorePlacer`'s `opts.LocalRotation`. Owner: *"store is on
  its side needs rot 90 euler 0,0,90f"* then *"after you stand it up, rotate it 180 degrees as its
  facing the wall"*. ⚠ **Deliberately NOT in Offset Forge** — `Assets/Editor/TripoAxisBake.cs:147-154`
  auto-rewrites `axisBaked` rows' `rot`. Measured: scale 5.49, boundsSize (5.12, 4.00, 6.35),
  collider (3.400, 4.000, 5.503), height exactly 4.00 m, `REALM_STORE_REACHABLE_OK nearest walkable
  0.08m` — a pre-run falsifiable prediction matched to three decimals.
- **Sign-in gate (PROD-006)** — the gate read ONLY `FirebaseAuthService.Instance.IsSignedIn`, but the
  identity law (same file, ~:556-557) says email/Google binds NOTHING and only the wallet path
  re-keys the save, so **a wallet-only player would have seen SIGN IN on every launch forever**. Not
  a race: wallet `connected=True` 20:21:38.597, gate decided 20:21:43.478. Fixed with a pure
  `ShouldContinueWithoutLogin(walletConnected, walletIdentityBound, firebaseSignedIn)` +
  `GameStateService.HasAttestedWalletIdentity` (true synchronously at boot). **No timing constant.**
  Pinned `[login-gate]`.
- **MWA session sealing WORKS** — `auto-resume: sealed session present`, `MWA session found for
  CHKK...sfkC`, silent reauthorize ~3.3 s, `auto-resume SUCCEEDED`. `6e9f86cc3` is doing its job;
  only the gate ignored it. **Do not re-debug MWA off the sign-in symptom.**
- **CDN (PROD-009/010)** — two remote groups, `Structure_Art` 19.71 MiB + `Enemy_Art` 64.45 MiB,
  ~84.26 MiB first run; `m_UseAssetBundleCache: 1` so one-time PER BUILD (content hashing means each
  APK re-downloads). `PackTogether`, **zero labels** on all 78 enemy + 35 structure entries → all-or-
  nothing. `StructureAssetLoader.cs` (~:99-100) and its five siblings use synchronous
  `WaitForCompletion` — a **main-thread FREEZE**, not pop-in. No prewarm anywhere. Worst case is FTUE
  beat 7/8 `founding_defend` pulling the 64 MiB enemy bundle **as combat opens**.
- **⛔ "Keep the CDN" was RIGHT** — `m_DisableCatalogUpdateOnStart: 0` means installed APKs adopt the
  new remote catalog at launch, so re-pointing an asset local = **invisible buildings for everyone
  already playing**, with no client change. Re-grouping rehashes bundles = full re-download for all.
- **R2 (PROD-011)** — every APK build REQUIRES `python tools/r2_sync.py --push ServerData` —
  **`ServerData`, NOT `ServerData/Android`** (relpath keying flattens it to the bucket root; the
  docstring at `tools/r2_sync.py:22` still documents the wrong form). Push AFTER build, BEFORE
  install. `--check` proves credentials only; `--push` skips by SIZE not hash and `catalog_*.hash` is
  always exactly 32 bytes. **No gate exists for APK-vs-bucket mismatch** — `16e22dba3`: *"NO GATE
  COULD HAVE CAUGHT THIS."* Tonight a build shipped with a never-uploaded enemy bundle; caught by hand.
- **Monetization stays OFF** — verified at source: `FeatureFlags.RealmStorePurchase` `defaultOn:
  false`, Buy CTA reads "Coming soon", `Purchase()` refuses at entry, `WalletService.Pay`/`PayFlat`
  refuse for the stub, Devnet with a hard mainnet block, and `Web3.Wallet` is never assigned so
  `SendPayment` cannot construct a transfer. Two HARD blockers: no server-authoritative economy
  (`api/game/save.js:404-421` is an explicit built-to-flip seam; a client can set its own crystals up
  to `MAX_RESOURCE`) and no payment-verification endpoint (`@solana/web3.js` isn't a dependency).
  Flipping on devnet would grant real pack contents for worthless tokens and emit
  `purchase_completed` events indistinguishable from real revenue.
- **Security fix (UNCOMMITTED at time of writing, NOT DEPLOYED — promotion is the owner's call)** —
  `api/promo/redeem.js` + `api/referral/claim.js` now require the signed wallet rail via a new
  `authenticateGranting()` in `api/_lib/wallet-auth.js`. The guest rail was self-asserted bearer trust
  (`verifyGuest` regex-checks `^guest-local-[0-9a-f]{64}$` and echoes it back), so an attacker could
  mint unlimited identities to burn `max_redemptions` and bypass `per_player_limit`. ⚠ **BREAKING:
  guest players lose promo redemption and referral claiming.** `TEST10` (10 crystals, active,
  uncapped, unbound), seeded unconditionally by `api/schema.sql`, is now behind an explicit opt-in.

### Regression baseline

**Known-red: 4** — `CaravanStatusChip` (UI-OBSIDIAN), `vfx-self-contained`, `vfx-null-slot` (awaiting
owner ruling), `WANDERER BUBBLE x4` (needs a dungeon re-bake **in an isolated worktree**). Two NEW
reds tonight were **FIXED AT SOURCE, not baselined**: `[fallback-parity] tower_ballista` code/JSON
drift, and a **hollow pass** in the newly written `LoginGateRegression`. *(Never restate a suite count
from a doc — read the marker files under `Builds/`.)*

### ⏳ IN FLIGHT at time of writing — re-verify before acting

1. **Hub-injector orientation lane** — channel (b) above. Every `Swap` row in
   `HubStructureVisualInjector.cs` still carried `pitchDeg = -90f` when this was written. Each row
   must be decided **against its own FBX meta**, not swept.
2. **Gear seating lane** — a prop measured `worldBounds=(0,0,0)` (a degenerate/unresolved bounds
   read) and a **`parent-scale compensate` firing every frame** rather than once at seat time. Both
   are symptoms of the same seat-time-vs-update-time confusion; instrument before editing (§12).

### Next actions

1. Finish + verify the **hub-injector orientation lane**, per-asset against `bakeAxisConversion`.
2. Land the **gear seating** RCA from captured data, not from reading the seating code.
3. **PROD-008** — turn `UprightAspectMin` into a real orientation gate so this class of defect stops
   being felt-only.
4. **PROD-009/010** — an Addressables prewarm, and labels on the two remote groups so
   `founding_defend` stops pulling 64 MiB through a blocking call at combat open.
5. **PROD-011** — fix the `tools/r2_sync.py:22` docstring and add an APK-vs-bucket check.
6. **Owner decisions to surface (do NOT decide these):** PROD-012 is-internet-required; pack pricing
   (five SKUs above the $5 early-access cap, up to $49.99); mainnet; the Realm Store vendor NPC body;
   storefront height 4 m vs the 1.25 landmark tier; `vfx-null-slot` retag-or-repair; and whether to
   **deploy the api/ security fix** knowing it breaks guest redemption.

---

## ★★ SESSION HANDOVER — 2026-08-09 (the 08-08 ship day: machine unblocked, stairs SOLVED, store re-gated) — SUPERSEDED (see 2026-08-18 above) ★★

**Anchor:** **`../CANON_GROUND_TRUTH_2026-08-16.md`** (minted 2026-08-16; 08-09 and 08-07 are now
bannered/frozen). ⚠ The HEAD / push-state / gate lines in the rest of this block are the **08-09
snapshot** — read the 08-16 anchor and `git status` for current state. Branch `wip/village2-and-f8-tickets`, **HEAD `c8320434`,
PUSHED — local == origin, 0 ahead / 0 behind** (push landed 2026-08-08 19:52:45). **30 commits landed
2026-08-08** (counted from `git log`; the 08-08 anchor's last edit `07d2c6f8` sits **exactly 21 commits**
behind HEAD). Save schema **v38** (`SaveSchema.cs:41` — the const is the authority). **v38 = WO-934 the
ARMY LOADOUT BANK**: `ArmyStorage.loadouts` (3 named composition presets) + `activeLoadout`, additive on
the nested Army JSON, `MigrateToV38` runs `EnsureLoadouts` for empty slots. (v37 = WO-911 M2, the per-job
PAID BASKET `paidWood/paidFood/paidIron/paidCrystals/paidMagic` on `BuildJobData`; cancel refunds
**100% of what was paid, flat**, and **a pre-v37 job refunds ZERO and says so**.)

⚠ **Working tree NOT clean, and it is a SHARED tree (CLAUDE.md §11):** `ProjectSettings.asset` (diff is
**exactly two auto-stamped keys** — `bundleVersion`/`AndroidBundleVersionCode` 316839 → 316856, written by
`AndroidBuild.BuildSeekerApk`, so **an Android build ran AFTER HEAD**; not a hand edit) plus **4 DELETED
files under `tools/webbot/`** (see below). **Reconcile by EXPLICIT PATH — one committer, never
`git add -A`.**

**Gates last emitted — transcribed from the marker files, and only true of those runs:**
`Builds/gate-ship3.log` (19:36) → `COMPILE_GATE_OK` · `Builds/regression-ship3.log` (19:38) →
`REGRESSION_OK 130/130 suites` · `Builds/ui-capture-ship.log` (14:30) → `UI_CAPTURE_OK 44`.
⚠ **`Builds/test-results-EditMode.xml` reads 930/930 green but is stamped 2026-08-04 — five days stale.
Do not cite it as current evidence.** ⚠ **Never restate a suite count from a doc** — the three entry
points emit DISTINCT markers (`REGRESSION_OK` / `CHECKIN_SUITE_OK` / `SESSION_GUARDS_OK`).

**✅ THE 08-08 MACHINE BLOCK IS RESOLVED.** The machine rebooted 2026-08-08 08:07:21. Commit charge is
back to **45.7 GB of a 127.8 GB limit**, 11.9 GB physical free, no Unity running. **Windows EXE built
08-08 14:33 · Android APK 08-08 20:00 (572,202,338 bytes) · Firebase ran.** ⚠ **The morning order
(reboot → EXE → APK → Firebase → WebGL) completed EXCEPT its last step:** `Builds/WebGL` is still dated
**2026-08-05** and there is **no `Builds/webgl-chain-status.txt`**. The web rail is the open one.

**★ THE DUNGEON STAIRS ARE SOLVED — the whole `PathPartial` hunt is CLOSED, and this is the lead story.**
WO-930's one-room stairwell shipped the same morning the 08-08 anchor said nothing cheap remained:
`3ab1bfb6` 11:24 (**the first floor-to-floor `PathComplete` in project history**; the old pair-model probe
kept as a control) → `e7163c9c` 11:27 (skinned via shared `RoomForgeMaterials`, **0 bad surfaces**) →
`5f0e23aa` 11:53 (3 candle lights under the URP 4-light cap, **plus a caught RED gate — `dg_sunken_vault.json`
dual-copy drift: Resources held the OLD 17-room layout and Resources WINS at runtime, so the game would
have loaded the old dungeon**) → `cb092b7f` 12:03 (bonecrypt + ember_deep converted: **all 4 content
dungeons PathComplete, 12 descents, 0 mate failures, 14/14 dual-copy parity**; `dg_descent_probe` and
`dg_stair_rig` deliberately left on the old model as controls) → `51a89364` 14:34 (`RoomPrefabMeta` stamped
on `StairwellRoom` — the overlap gate had been measuring a **20x10 m room as one 10 m cell**; oracle
rewritten, 8 new cases, 3 legacy quarantined).

**ROOT CAUSE = STAIR YAW.** `GraphDungeonComposer.SolveMate` **hardcoded `yaw = 0f` on vertical sockets**
(the planar solve degenerates when both outwards point straight up/down), so only a Delta of **180** put
the flight's top nose in the floor hole; anything else climbed into a solid slab, no clearance, no carve,
nothing to path from. **It was never a property of the stair** — which is exactly why four rounds of
bucketing the stair's own scalars all came back negative. **Transferable:** when a population bucketed
against scalar after scalar keeps returning nothing, the variable is not on the axis being measured.
The 08-08 anchor's "next move is to dump navmesh triangles" is **dead guidance**; keep its
killed-hypotheses table as history — it still stops re-runs.

**⚠ HEADLESS GATES CANNOT SEE ORIENTATION — the transferable lesson of the day.** `70a86c17` (12:41) is a
**REVERT of `bb6dc010`**: applying `SkinOptions.PreservePrefabRotation` to ALL structures **laid the whole
town on its side** (13 catalog rows carry a manual -90 that composes to 180), and it only reproduces on the
**dungeon → town return path** via `BaseLayoutLoader` — with every marker green the entire time. **This
defect class needs eyes, not markers:** the UI capture pass, a device screencap, or the owner's hands.
The correct narrow fix is `439e03ee` (14:35): a per-catalog-row **`RepoProps.preservePrefabRotation`**
(default false, **exactly one row opts in — `tower_ground_archer`**) with `StructureFactory.OptsFor` made
the single reader unifying `Create` / `MeasureUprightFootprintMetres` / `GhostPreview`.
⚠ **Still-live root cause named in that commit:** `Resources/Structures` holds both a `.fbx` and a
same-stem `.prefab`, making `Resources.Load` **ambiguous**. Unfixed.

**⚠ STORE PURCHASES RE-GATED OFF AND LOCKED — security-grade** (`576601e3`, 19:15).
`FeatureFlags.RealmStorePurchase` is back to `defaultOn: false`. `StubWalletProvider` has **NO
`#if UNITY_EDITOR` / `DEVELOPMENT_BUILD` guard**, so it compiles into **every** shipped player: it
fabricates a wallet, a **2000 SKR mock balance** and a base58 signature, and `ApplyPackContents` then
**grants the pack for ZERO payment** while firing `purchase_completed` with the fake txSig. **The
submitted store build had a tappable Buy button.** This is **WO-931, READY TO IMPLEMENT**, and it is
**precondition 3 of 3** in that flag's DO-NOT-TURN-ON block.

**Legal + publishing.** `640bfc1c` (19:48) sets `productName` → **"Echoes of Elarion"** so the app installs
under the store listing name. `c8320434` (19:48, HEAD) authored `docs/TERMS_OF_USE.md` and hosts it
**verbatim** at `site/terms.html`, live at `https://echoes-of-elarion.vercel.app/terms` (verified HTTP 200),
linked from the landing nav + footer; governing law **Texas**; ⚠ **no arbitration clause, no class-action
waiver, no jury-trial waiver — deliberately left for the owner's attorney.** Publishing scaffold added under
`publishing/` (`config.yaml`, `SUBMIT_CHECKLIST.md`, `media/README.md`) plus `tools/store_previews_resize.py`.
⚠ **TWO FLAGS RAISED IN THAT COMMIT ARE STILL OPEN:**
1. **`PRIVACY_POLICY.md:87-89` contains ONE FALSE SENTENCE on a LIVE PUBLISHED PAGE** — it says the Ad
   button "grants that time saving immediately without presenting any advertisement", and **that button is
   now ABSENT from the UI entirely**. The core no-ads claim is **verified TRUE**; only the explanatory
   sentence is stale. ⛔ **Do NOT edit it** — live legal copy is the owner's and her attorney's call.
2. **`docs/PUBLISHING_STEPS.md` Rail 1 is OBSOLETE** (now bannered). `dapp-store-cli@1.0.0` has **no
   `init` / `create` / `validate` / `publish`** — its entire surface is `dapp-store --apk-file ...
   --whats-new ...` — and the app must **already exist in the portal with an App NFT**. Publisher and app
   are created **in the web portal with a browser wallet**; `publishing/config.yaml` is kept as the
   verified **paste-source** for that form.

**⚠ `tools/webbot/` WAS DELETED OUTSIDE GIT — open decision.** All four files (`canvas-probe.js`,
`introtest.js`, `package.json`, `webbot.js`) are **present at HEAD**, **no commit has ever deleted them**
(`git log --diff-filter=D` over that path is empty), they are **not gitignored**, and the directory is
**absent on disk**. That is the Playwright web-build self-test rig — the eyes on the deployed web build.
`git checkout -- tools/webbot/` restores it; **it has NOT been run.** Deliberate vs accidental is
unestablished — this is the owner's call, not a decided action.

**Dev tooling out of the shipped player.** `eeb2d389` (12:13) flips `ff.devresourcetool` **OFF** by default
and moves DevPanel under Settings (`PanelId.DevPanel` = 17, gated on `PanelRouter.IsRegistered`);
`374ccd26` (12:55) ships a **RELEASE desktop player**, verified by `DeNelle.DevTools.dll` being **absent**
(206 DLLs, was 207) — ✅ this **closes the long-standing KEY_FACTS item "desktop release still ships
Development builds."** ⚠ **TRAP: the flag flip did nothing on this machine** — `FeatureFlags.Get` reads
**PlayerPrefs FIRST** and this box has `ff.devresourcetool=1` persisted from 08-07. A default change is not
a state change on a machine that already answered the question.

**Felt fixes.** `2f10f6ac` (14:34) — auto-upgrade was handing **every level-2 knight a paid Forge
`knight_flameblade` for free**; candidate set narrowed to owned gear, with tri-state ownership so it
survives a `VillageInventory.EnsureLoaded` pre-load race. `763d1a60` (14:35) — building nameplates were
rendering literal **`[[missing:market]]` / `[[missing:jeweler]]`** to the player; forge/armorer duplicate
resolved; "Lumber Mill" renamed across catalog, quests and prefab.

**⚠ F8 — ONE UNACKNOWLEDGED capture, seq 2248** (2026-08-08 13:17:10, `Main_Castle_Overworld`):
`Cannot set the parent of the GameObject '[VFX_Harvest_Wood]' while activating or deactivating the parent
GameObject 'Lumbermill'.` This is the **WO-929** defect class and WO-929 already names `HarvestAura.cs`
among its four candidate sites — **but every proving line in WO-929 is `OutpostEnemy (...)`, a POOLED
ENEMY.** This capture proves the same illegal `SetParent` fires from a **BUILDING**, so **a fix scoped to
the pooled-enemy path would be incomplete.** WO-929 needs its scope widened.

**WO board.** `0d75bc06` (08:45) — an audit found **52 of ~91 WO statuses wrong**; output is
`docs/reference/WO_TRUE_STATUS_2026-08-08.md`. It also surfaced that **WO-884's VFX facade never existed**,
**WO-898's `crystalsPerBracket` has 0 hits**, and **WO-875 / WO-877 were never attempted.** Corrected with
this handover: WO-930's own file still said `READY TO IMPLEMENT` / `SHIP-BLOCKING` although it shipped
(`3ab1bfb6` / `cb092b7f`); WO-927 is superseded by its own §0 (root cause = yaw); and the
`CLI_LANES_WO_NUMBERS.md` block table had gone stale against its own header — **only the table row was
fixed.** ⚠ **Read the next-free WO off that banner, never from a doc** (copying numbers caused five
collisions in one day on 2026-08-02). **RESULT-file debt on the live arc:** none of
**921 / 923 / 924 / 925 / 926 / 927 / 928 / 929 / 930 / 931 / 1006 / 1007 / 1008 / 1009** has a
`.RESULT.md`. **None were fabricated** — a RESULT is written by the seat that verified the work.

**★ FOUR LONG-STANDING CANON CLAIMS WERE REFUTED AT SOURCE — they are CLOSED, stop carrying them**
(anchor §9; each verified line-by-line, each corrected in `KEY_FACTS.md` in place):
1. **"THE SEAM" IS CLOSED.** The 08-03 claim that nothing can damage a wall, gate or enemy tower is FALSE
   at HEAD. WO-853 dual-implemented `IDamageable` + `IDamageableStructure` on `WallSegment.cs:53`,
   `Gate.cs:67`, `DefenseTower.cs:57` and `RaidSpire.cs:61`, and widened the troop mask on **both** entry
   points so a factory-supplied Enemy-only mask cannot strip it (`TroopController.cs:189`, `:201-202`,
   `:394`), with the collider buffer raised 48 → 128 (`:104`) so wall panels cannot crowd enemy colliders
   out of `OverlapSphereNonAlloc`. Walls stay on the **Structure** layer deliberately — that layer is the
   tower LoS blocker mask. Covered by `TowerWallLosRegression`, `StructureTargetableRegression:440`,
   `DefenseTargetableRegression:136`, `RaidArenaShapeRegression:363`.
   ⚠ **Consequence: the raid-roadmap prerequisite is SATISFIED, so the WO-774.0 drop-and-watch-vs-led
   posture ruling is NO LONGER FREE TO DEFER** — it was parked *because* the seam blocked both roadmaps.
2. **The "orphan third copy" of the gear catalogs is GONE.** `Assets/Data/Canonical` **does not exist**
   (deleted in `c55a5561`), and it could not have shadowed the pair anyway —
   `LocalJsonCatalogSource.Read` probes only `Resources.Load<TextAsset>` then `streamingAssetsPath`
   (`:33-52`). `CANON_GROUND_TRUTH_2026-07-22.md:193` §5.8 and two design docs are stale on this and were
   deliberately not edited.
3. **`CatalogBootstrap.RegisterFallback` drift is FIXED and GUARDED.** All three rows are field-equal,
   including `tower_arcane_spire.visualTexturePath = "Structures/ArcaneSpire_Albedo"`
   (`CatalogBootstrap.cs:307`) — the pure-white defect is CLOSED. Enforced by
   `BuildEconomyRegression.cs:1191-1290` gate 12 `[fallback-parity]`.
4. **Dual-copy is HEALTHY.** Swept 80 JSON per side, 77 paired: **only `weapons.json` and `armor.json`
   drift, and both are the DELIBERATE owner gear ruling.** The 08-08 `dg_sunken_vault.json` drift is
   FIXED (v1 / 14 rooms both sides); all dungeon layouts and graphs byte-identical. **The defect is the
   missing GATE, not current drift** — see below.

**⚠ FIVE NEW GAPS FROM THE SAME AUDITS — all OPEN and UNCOVERED** (anchor §10):
- **HIGH — three of the five difficulty multipliers are computed and thrown away.**
  `EnemyCountMultiplier`, `BossHpMultiplier`, `BossDamageMultiplier`
  (`Core/Difficulty/DynamicDifficulty.cs:119,122,125`) have **no reader anywhere outside
  `Core/Difficulty`** — the only external hits are `DynamicDifficultyRegression.cs:276-292` and
  `Assets/Tests/EditMode/DynamicDifficultyTests.cs`, and **both call `DifficultyMath.*`, never the live
  `DynamicDifficulty.*` properties.** **Every boss wave ignores the softer boss curve the math file exists
  to produce.** The suite proves the math and the oracle only — no `WaveManager` reference, no consumption
  assertion — so a lever can be correct and unwired with the gate green.
  *(This narrows, not confirms, canon's "adaptive difficulty is INERT": all six `EncounterSample` fields
  ARE measured and recorded — `WaveManager.cs:2471-2484`, armed `:2341`, consumed `:1761-1762` and
  `:1876-1877`.)* ⚠ **Namespace vs. path, both correct:** the folder is
  `Assets/_Modules/Core/Difficulty/` but all six files declare `namespace DeNelle.Core.Adaptive` — the
  08-03 rename moved the namespace (it shadowed the persisted enum), not the folder. **Fix neither.**
- **The data gates cannot see the copy that WINS at runtime.** Both `DataWebRegression` checks iterate the
  **StreamingAssets root only** (`:208` drift, `:356` version), so a Resources-only file is never drift-
  or version-checked. Verified Resources-only: `ad-creatives.json`, `ad-placements.json`,
  `widget-params.json` — and **`widget-params.json` has no `version` field at all.**
- **The version check never asserts that a change bumps it** (`:352-398` is presence + cross-copy agreement
  only): **24 catalogs had content changed with no version bump on their most recent commit** — worst
  `enemies.json` +95, `en.json` +265, `themes.json` +369, `waves.json`, `abilities.json`.
- **`RoomForgeRegression.cs:162`'s dual-copy gate is a hardcoded 3-file list** with **no `dg_*` layout in
  it** — including `dg_sunken_vault.json`, the exact file that drifted. **The next drift ships the same way.**
- **`DungeonBaker`'s reachability probe is a SINGLE `placedOrder[0] → placedOrder[last]` path and is
  LOG-ONLY** (`:432-445`, `:457-479`): a `PathPartial` prints `PATH DIES` and **`SaveScene` runs
  unconditionally** (`:490-494`). No per-descent probe, no abort — so a dungeon whose **first** descent
  fails is indistinguishable from one whose last does, and reachability is gated by the first failure.

**⚠ ONE CORRECTION TO THE STAIRWELL WIN — state it accurately.** WO-930's spec said it **DELETES**
`StairUp`/`StairDown`, the vertical mate branch, `IsVertical`, `SEALED_VERTICAL`, the floor holes and the
ceiling shafts. **That did NOT happen, deliberately.** All of it is **retained as a quarantined, gated
CONTROL GROUP** under an explicit **"⚠ DO NOT DELETE"** banner (`DungeonMultiLevelRegression.cs:41-63`),
because the code is still live, still loaded by three graphs, and deleting it would leave live code with
no oracle while letting the A/B control group rot. **`dg_stair_rig` and `dg_descent_probe` are TEST
FIXTURES, not stale content and NOT regressions** — `[graphs-converted]` asserts they STILL name the
retired prefabs so a tidy-up cannot remove the control group by accident. Converted layouts, verified as
pure `"prefab": "StairwellRoom"` in both copies: `dg_bonecrypt`, `dg_ember_deep`, `dg_sunken_vault`,
`dg_stairwell_probe`. The deletion is a future **single-commit** job (WO-930 §5).

**Data-fact correction:** **`structures-catalog.json` is `version: 15`** (identical both copies, 29
entries, `_heightCadence` present). Any doc saying v6/v7/v8 is a stale point-in-time reading — read it
off the file.

**CARRIED FORWARD — still open, and the 08-08 anchor silently dropped every one of these:** the VFX
**ONESHOT pool saturates 40/40** in three captures (**different pool, different reclaim path — explicitly
NOT closed** by the 08-06 loop-cap fix) · the **absence** of `SKIPPED - active loops 20/20` across a full
wave has **never been proven** and is owed a fleet run · **`VFXType` serialises by ORDINAL, not name —
appends only**, and `Build()` does `entries.arraySize = rows.Count` so a row written only by a builder is
silently dropped by the next regenerate · **✅ WO-910 is RESOLVED (2026-08-16)** — all three talent trees
re-authored to **3 bases branching wider** (knight 3/7/8/7/7, ranger 3/5/6/6, mage 3/6/6/5); ⚠ ranger and
mage had had **no authored x/y at all**, so the old "31 dead nodes" line described a missing layout, not a
design deficit; **one focus plate per BOARD** (was one per track) · **hero select SELF-SKIPS** when the save already records a
class, so testing a class change needs **New Game / Play Intro**, never Continue · height cadence **1.25**
landmark / **1.2** towers / **1.0** building base / **0.75** siege / **0.35** decoration with **WALLS
DELIBERATELY EXCLUDED** (a uniform fit narrows a wall, which **opens PATHABLE GAPS in saved wall runs**;
**`collector_farm` at 1.4 is a COMPENSATION, not an outlier — do not "fix" it**) · ~~**`api/` is deployed
to PREVIEW only** while the game hardcodes the prod domain, and prod's nonce endpoint has **no CORS** —
promoting it is the owner's call~~ **REFUTED 2026-08-10 against LIVE HTTP — see the correction note
immediately below** · still **colour-only and OPEN** (owner is red/green colourblind): **the
build placement ghost** and **the hero health bar** · the bottom action bar is **6 visible faces** with
`Upgrade` re-pointed to Manage/Queues — `HudActionBarModel.ButtonCount` stays **7** (enum identity),
`MaxVisibleFaces` is the number that went 7 → 6, and **`Map` stays dormant at ordinal 4 and must never be
renumbered** (the face arrays are indexed by ordinal).

> ### ⚠ CORRECTION 2026-08-10 — `api/` IS IN PRODUCTION, and has been since 2026-08-03
> Verified against the **LIVE Vercel deployment record + live HTTP responses**, not against another doc.
> Three separate claims that this handover carried forward (here, and in the 08-03 block further down)
> are **REFUTED**:
> 1. *"`api/` is deployed to PREVIEW only"* — **false.** `target: production` deploys landed
>    **2026-08-03T22:50Z**, **2026-08-04T19:33Z** and **2026-08-05T23:37Z**
>    (`dpl_9vGadbKyPrQ55HR3PaUT53i9CNUh`, commit `8fdb29a5`).
> 2. *"prod runs OLD code (prose error shape)"* — **false.** A live
>    `GET https://defenders-of-the-realm-v2.vercel.app/api/auth/nonce?wallet=...` returns the **NEW
>    structured shape** `{"ok":false,"code":"AUTH_WALLET_MALFORMED","ref":"..."}`.
> 3. *"prod's nonce endpoint has NO CORS"* — **false.** That same response carries
>    `access-control-allow-origin: *` and an `access-control-allow-headers` list including
>    **`X-Wallet, X-Nonce, X-Signature`**. The WebGL wallet rail is **not** blocked by CORS in prod.
>
> **Why it stayed wrong, and the mechanism to remember:** `.vercelignore:17` (`!/api`) allowlists `/api`, so
> **every `--prod` deploy from the repo root re-ships `api/` to production.** That is a **standing
> property of the deploy layout**, not a one-night event — there is no such thing as promoting only the
> WebGL payload from this repo. A seat reading the un-struck lines above would have (a) planned a
> "promote `api/`" action that had already happened three times, and (b) treated the prod `api/`
> surface as un-exposed when it is fully live. **"Promoting `api/` to prod" is NOT an open action item.**

**Open, needing the owner:** **WO-774.0's posture ruling, newly un-parked** (the seam that made it
deferrable is closed) · **the three dead difficulty levers** (wire them or delete them) · **the three gate
holes** (Resources-only files unchecked, no change-bumps-version rule, RoomForge's hardcoded list) ·
the WebGL / web-deploy step (never ran; owner 2026-08-10: deferred to end of the morning session) ·
`tools/webbot/` (restore or not) ·
the false `PRIVACY_POLICY.md` sentence on a live page · the Terms' missing arbitration/waiver clauses ·
~~**WO-931** (architecture call — left UNPICKED)~~ **RESOLVED 2026-08-10: owner picked (b) runtime
refusal; IMPLEMENTED at the `WalletService.Pay`/`PayFlat` seams (uncommitted pending the batch gate);
preconditions 1+2 on the flag still open** ·
**WO-910** · ~~promoting `api/` to prod~~ **NOT AN OPEN ACTION — `api/` has been in production since
2026-08-03; see the correction note above** · ~~widening **WO-929** to the building path~~ **DONE 2026-08-10:
WO-929 §2b now pins FOUR proven host classes (building/hero-aura/enemy-caster/pooled enemy) — fix at the
shared attach seam** · the RESULT
debt · the ambiguous `Resources/Structures` `.fbx`-vs-`.prefab` pair.

**The 08-06 block below is SUPERSEDED — kept as history, not guidance.**

---

## ★★ SESSION HANDOVER — 2026-08-06 (the VFX night: two P0s, Ranger/Mage unlocked, one height cadence) — SUPERSEDED (see 2026-08-09 above) ★★

**Anchor:** `../CANON_GROUND_TRUTH_2026-08-06.md` (NEW — supersedes 08-05, bannered). Branch
`wip/village2-and-f8-tickets`, **HEAD `1534dffb`, local is 43 commits AHEAD of origin — NOT PUSHED.**
⚠ **Working tree NOT clean, and it is a SHARED tree (CLAUDE.md §11):** `ProjectSettings.asset` carries a
newer APK stamp (`2026.08.05.312459` / code `312459`, above the committed `312348`);
`WorkOrders/WORK_ORDER_885`–`894` are untracked; and **a concurrent implementation lane of ~32 modified
`.cs` files plus the dual-copy `structures-catalog.json` / `damage-states.json`** is sitting in the tree
(consistent with WO-889–893 in flight). **Reconcile by EXPLICIT PATH — one committer, never `git add -A`.**
Save **v36**, unchanged. Gates last emitted: `COMPILE_GATE_OK` + **`REGRESSION_OK 120/120 suites`** plus
`VFX_LOOPFLAG_OK`, `VFX_ART_MIRROR_OK`, `PARTICLE_PACK_VFX_BUILD_OK`, `BOSS_FIREBREATH_BUILD_OK`.

**⚠ Read the marker, not a doc.** The suite count moved **117 → 118 → 119 → 120 inside eight hours**. The
three entry points now emit **DISTINCT** markers (`REGRESSION_OK` / `CHECKIN_SUITE_OK` /
`SESSION_GUARDS_OK`) precisely so a 22-case suite's pass can never again read as the full suite's.

**THE PATTERN OF THE NIGHT — this is the transferable finding.** Six separate defects were the same shape:
**a flag authored BY HAND instead of DERIVED from the thing it describes.** `IsLoop` in the VFX catalog
(a sticky UI checkbox — **53 of 122 picks wrong**) · the "self-contained" tracked VFX prefab (**`CopyAsset`
copies the prefab only — 183 pack references**) · `HeroTalentNodeDef.Hidden` (**zero runtime readers**, its
own comment claiming otherwise) · `TalentStrategyRegression.HiddenTrees` (**40 player-reachable nodes never
audited**) · the UI capture harness resolution (**a label, not a layout**) · `CatalogBootstrap.RegisterFallback`
(**all three rows drifted**). **Derive the value from the artefact, and PIN the owner's standing rulings
above the derivation with their reason** — because the prefab is the authority on what the art *does*, not
on what the game *should do*.

**⚠ P0 #1 — THE VFX LOOP CAP WAS LEAKING DRY.** A loop row never returns its slot (the oneshot branch
registers a deadline and gets swept; the loop branch does a bare `++`, and the only reclaim frees
**destroyed** hosts — pooled objects are never destroyed). **The cap is 20.** The archer and ballista fire
`PP_MuzzleFlash` — a single burst mis-flagged as a loop — and **discard the handle**, so after ~20 shots a
tower renders **no projectile at all** and in the same breath starves the Tree of Life aura and every POI
marker. `break-log` across **six F8 sessions on two dates** shows `SKIPPED - active loops 20/20` naming
five victims (`ArcherTower_Projectile`, `ARcaneTower_Projectile`, `ArcaneTower-Baselevel_Projectile`,
`Poi_NodeAura`, `Poi_Landmark`) — **all five were themselves the mis-flagged culprits filling the cap that
starved them.** Both catalog generators now DERIVE the flag; the caster window's checkbox is read-only.
⚠ **NOT YET PROVEN: the ABSENCE of the cap message across a full wave. That needs a fleet run.**
⚠ **A SEPARATE signature, deliberately NOT bundled: the ONESHOT pool saturates 40/40** in three other
captures — different pool, different reclaim path. **Do not assume the loop fix closed it.**

**⚠ P0 #2 — THE TRACKED VFX PREFABS WERE NEVER SELF-CONTAINED.** `CopyAsset` duplicates the prefab but not
its materials, textures, shaders, meshes or animations, so **27 of 28 prefabs / 183 references / 73 distinct
assets** pointed back into gitignored art — magenta, untextured or invisible on any machine without the
packs. **Now 0**, verified twice (the mirror's own report *and* an independent GUID walk that does not reuse
the builder's code); **~23.85 MB mirrored, deduped**, into `Assets/Resources/VFX/_Shared/`.
⚠ **Two pack MonoBehaviours could not be mirrored and were STRIPPED — felt-visible: `Casting_Fire` no
longer spawns a projectile.** ⚠ The mirror **only converged on a first run** until it was fixed to re-seed
from everything already mirrored — six prefabs read as self-contained while their art was one hop away.
**Lana Studio is NOT gitignored** (only its URP upgrade subfolder is), contrary to standing assumption.

**⚠ RANGER AND MAGE ARE UNLOCKED — AND ✅ THEIR TREES ARE NOW AUTHORED (WO-910 RESOLVED 2026-08-16).**
`ff.knightonly` defaults OFF; roster is Knight/Ranger/Mage through the single `PlayableHeroes` registry
(**Cleric deliberately out — no authored kit**). All three trees were **re-authored to 3 bases branching
wider**, verified in `Assets/StreamingAssets/Data/Canonical/hero-talents.json`: knight **3/7/8/7/7**
(32 nodes) · ranger **3/5/6/6** (20) · mage **3/6/6/5** (20). ⚠ **Ranger and mage previously had NO
authored x/y at all** — the old "31 dead nodes / Ranger 1 usable of 20 / Mage 5 / tier-4 capstone rows
dead" framing (surfaced by emptying `TalentStrategyRegression`'s `HiddenTrees`) described a **missing
layout**, not a design deficit, and is now history. Also fixed: **one focus plate per BOARD** — the view
had been consuming `HeroSkillTreeVM`'s per-TRACK `nextTaken` signal as board-level and drawing a plate on
every track. ⚠ **Hero select SELF-SKIPS when the save already records a class** — testing a class change needs
**New Game / Play Intro**, never Continue.

**⚠ A LATENT INVISIBLE-HERO P0, FIXED.** Ranger and Mage have **no FBX at all** and fell through to a Blink
base body — and **`Assets/Blink` is gitignored**. On a fresh clone the terminal fallback logged a failure and
**returned without instantiating anything**, after `Start` had already destroyed the placeholder. Not a
Knight-degrade: **nothing at all.** Both bail-outs now build a tracked KayKit body.

**⚠ ONE HEIGHT CADENCE ACROSS EVERY STRUCTURE** (owner ruling): **1.25** landmark / **1.2** towers /
**1.0** building base / **0.75** siege / **0.35** decoration, now recorded **in the data** as `_heightCadence`
(catalog **v6 → v7 → v8** — 7 = the archer, 8 = the cadence; verified at HEAD). **WALLS ARE DELIBERATELY EXCLUDED** — the fit is uniform, so lowering a wall NARROWS it,
and every wall in a saved town sits on the cell pitch of its old claim: shrinking **opens PATHABLE GAPS in
existing wall runs** and shrinks the navmesh obstacle with them. Needs a measured audit plus a migration
decision. **`collector_farm` at 1.4 is a COMPENSATION, not an outlier** (windmill blades inflate the Y
bounds) — do not "fix" it.

**ACCESSIBILITY — the low-health tell is no longer a colour.** It reads by **pulse rate 0.85 → 3.2 Hz**,
**guttering depth** (trough to a tenth of authored density) and a **recipe swap to a candle gutter below a
quarter health** — a shape change, not a hue change. The vignette stays as a *redundant* cue; colour-ONLY
was the bug. Still colour-only and OPEN: **the build placement ghost** (valid/invalid on the red/green axis,
in the one mode where the player commits resources) and **the hero health bar**.

**UI this session:** the Echo unlock is **ONE screen with two buttons** (owner ruling) · victory screen
gets real generated stars, the right reward icons (the broken one was **one letter** — label `Crystals`,
data key `crystal`) and a two-column landscape spoils list, a **documented deviation from WO-894's own
wireframe because the WO's star-band spec made the crush worse** · the right rail is one collapsed chip
style with one shared gutter · the side-menu "duplicate gear" was the **drawer handle**, not a duplicate ·
the potion tap was dead because **the button disabled itself at zero** (proven: **433,897 log lines, zero
`command potion fired`**, while `attack` fired hundreds of times through the same mount).
⚠ **THERE IS NO APOTHECARY** — the recipes, panel, VM and service all exist, but no catalog row does, so
the empty state pointed at a place the player cannot reach by any path.
⚠ **The numeral `1` renders as a bare vertical stroke** in the chip font; ticketed as an owner look call.
✖ **`ClampMinTouch` was CHECKED AND RULED OUT** at three sites (bands resolved 117 / 116.7-130.6 / exactly
112.0 px) — a real class, but check the arithmetic before naming it.

**⚠ THE UI CAPTURE HARNESS WAS GEOMETRY-BLIND** until `7e05e6d3` — only `canvas.scaleFactor` was rewritten,
never `Screen.*`, so **every PNG shared one layout and the filename resolution was a LABEL, NOT A LAYOUT**.
**2670x1200 — the Seeker's real surface — had never been rendered in this repo.** A run that cannot move
`Screen.*` now degrades **loudly**. **Several of tonight's UI commits are explicitly not geometry-verified
and need a device check.**

**OPEN for the owner:** (1) **WO-910** — rule on the Ranger/Mage trees. (2) The **surface taxonomy** WO-887
refused — no surface signal exists anywhere in the game. (3) **`Death_Wolf`/`Death_Tiefling`/`Death_Skeleton`**
— the roster has three families; routing them is a creative pick. (4) **Which accessory carries the heal
aura** (inert until tagged; only the flameblade carries element data). (5) **`Cast_Heal` is a green glow** —
a second colourblind pass. (6) **The market has three player-facing names** and no authority. (7) **Potion
crafting has no reachable entry point.** (8) The wave stand-down **restarts the countdown from full** —
product call. (9) **Promote `api/` + `assetlinks` to prod** before any APK carrying the new wallet identity.
(10) **Push** — 43 commits are local-only. (11) The absence-of-cap-message **fleet run** is still owed.

**Full ledger, incl. every REFUTED belief and its killing evidence:**
`reference/SESSION_INDEX_2026-08-06.md` · earlier half of the same day: `reference/DEFECT_INDEX_2026-08-05.md`.

---

## ★★ SESSION HANDOVER — 2026-08-03 (the solo-night wave + first live server verification) ★★

> ⚠ **SUPERSEDED 2026-08-06 — frozen ledger, do not rewrite the body.** HEAD is now `1534dffb` (43 commits
> ahead of origin, unpushed), gates are `REGRESSION_OK 120/120`, and the anchor is
> `../CANON_GROUND_TRUTH_2026-08-06.md`. Its **WO block numbers (main 853 / UI-seat 863) are stale** —
> read the `CLI_LANES_WO_NUMBERS.md` banner. Its **canon-health paragraph is partly paid**: the
> `docs/MASTER_CATALOG.md` index and several area files were corrected 2026-08-06. Everything else in this
> block (the server probe, the wall/gate damage seam, adaptive difficulty being inert) still stands.

**Anchor:** `../CANON_GROUND_TRUTH_2026-08-03.md` (superseded — see above). Branch
`wip/village2-and-f8-tickets`, **HEAD `56be3ae2`, local==origin, pushed. Working tree CLEAN.** Prod
untouched. Save **v36**. Gates: `COMPILE_GATE_OK` + **`REGRESSION_OK 104/104 suites`** +
**`TESTS_OK 912/912`, zero reds** + `UI_CAPTURE_OK 28`. WO blocks unchanged (main **853** / UI-seat **863**).

**⚠ Read the marker, not a doc.** The 08-02 anchor pinned `e60b19e5`; 17 commits landed after it, and
three boot documents inherited both the stale sha and the stale `884/884` test count. That count was
current for less than a day.

**Shipped overnight (15 commits, `e60b19e5` → `8e70a3d4`, all pushed):**
- **Enemies now reach you** — DPS/Ranged were targeting their own wounded ally, and
  `_stopTightenedForHero` survived pooling so a reused body halted 2.5 m out, outside the 1.5 m engage
  ring. Pooled enemies also stopped freezing as statues (`_casting` never cleared on death).
- **Raids are not a square room** — footprint 2.4% → **20/49/60%** of the authored floor with a **central
  spire as the win condition**. Raid walls had **no colliders at all**; no raid scene had a hero spawn
  point, so the hero landed on castle courtyard coordinates, inside the walls on the objective. Arenas
  re-baked 4/4 walkable.
- **Raid troops animate and aren't magenta** — nothing under `Troops/` ever assigned an AnimatorController,
  and `MagentaGuard` only swept at scene load.
- Unarmed level-1 Mage fixed; shield upgrades do something; defense cap is one constant at **0.90**
  (was fourteen literals drifted into two numbers); tutorial Hollow step completable.
- **The check-in gate had never been running** — `checkin_gate.ps1` did not *parse* under PowerShell 5.1.
- `DeNelle.Core.Difficulty` → **`DeNelle.Core.Adaptive`** (it shadowed the persisted `Difficulty` enum).

**⚠ SERVER — probed live this session, corrects the 08-02 anchor:**
- **`auth_nonces` EXISTS.** Prod `GET /api/auth/nonce` returns **HTTP 200** with a real nonce. The 08-02
  "table does not exist" line is dead. **Caveat:** the only rows in it were minted by the probe.
- **`api/` is deployed to PREVIEW only** and the game hardcodes the prod domain — prod is provably running
  OLD code (prose error shape; `bugreports`/`authrejects` views absent).
- **NEW, not in the night report: prod's nonce endpoint has NO CORS and `OPTIONS` returns 400** — a browser
  blocks the WebGL wallet rail no matter what the client does.
- `player_data` = **2 test rows, newest 2026-05-31**. `bug_reports` = **0**. `analytics_events` = 80,749.
- **`vercel deploy --prod` is the single highest-value action on the board. Owner's call.**

> **⚠ CORRECTION BANNER 2026-08-10 (this 08-03 ledger is FROZEN — body left as written, per CLAUDE.md
> §15).** The three server bullets above (`api/` PREVIEW-only · prod runs OLD code / prose error shape ·
> prod's nonce endpoint has NO CORS) were **true when written on 08-03 and are REFUTED as of today.**
> Live HTTP to `https://defenders-of-the-realm-v2.vercel.app/api/auth/nonce?wallet=...` returns the NEW
> structured shape `{"ok":false,"code":"AUTH_WALLET_MALFORMED","ref":"..."}` **with**
> `access-control-allow-origin: *` and `access-control-allow-headers` including
> `X-Wallet, X-Nonce, X-Signature`. Production deploys landed 2026-08-03T22:50Z, 2026-08-04T19:33Z and
> 2026-08-05T23:37Z. The `vercel deploy --prod` bullet **was actioned** — mechanism: `.vercelignore:17` (`!/api`)
> allowlists `/api`, so **every `--prod` from the repo root re-ships `api/`.** See the correction note in
> the current 08-09 handover block above.

**⚠ THE SEAM worth ranking first if one architectural thing gets built:** nothing in the game can damage a
wall, gate, or enemy tower. `WallSegment.cs:28` + `Gate.cs:45` implement `IDamageableStructure`;
`TroopController.cs:449-459` sweeps for `IDamageable`; the interfaces are **disjoint**. ~2–3 days. It sits
under both raid roadmaps, which makes the **WO-774.0 posture ruling free to defer**.

**OPEN:** (1) promote `api/`. (2) owner felt-verify the rescaled raids, shields, starter loadout, respawn,
treasure cache, Echo picker — and Perfect Hit as a double-tap (100 ms window, unvalidated by hand).
(3) **Adaptive difficulty is INERT** — math oracle-proven, but `WaveManager` records none of its six fields.
(4) Design calls: should shields drop at all; `lumberyard` in `FoundingKit` vs the WO-837 ruling; `category`
on ten legacy `weapons.json` rows. (5) WO-848 / 851 / 861 / 862 / 837 as before. (6) The 6 Echo emergence PNGs.

**⚠ CANON HEALTH:** `docs/MASTER_CATALOG.md` — the **INDEX** — was NOT refreshed by WO-836; only the 19
area files were. Its body still claims Blaise + party-of-4, OuterWorld streaming, 64 Yarn nodes,
`SaveSchema v30`, "next free WO = 412". **Use it as a filename index only.** The 19 area files are code-true
as of `b77a178e` (08-02 morning), not HEAD. `docs/reference/REGRESSION_COVERAGE_MATRIX.md` is two Sundays
overdue and still says "16 suites" against a 104-suite tree — use its proposed assertions, never its counts.

---

## ★★ SESSION HANDOVER — 2026-08-02 (marathon day 2: Echo program · tester wallet · dungeon+gear evening) ★★

> ⚠ **SUPERSEDED 2026-08-03 — frozen ledger.** HEAD is now `56be3ae2` (17 commits later), gates are
> `REGRESSION_OK 104/104` + `TESTS_OK 912/912`, the working tree is clean, and **`auth_nonces` exists**.
> See the 08-03 block above.

**Anchor:** `../CANON_GROUND_TRUTH_2026-08-02.md` (NEW — supersedes 08-01, bannered). Branch
`wip/village2-and-f8-tickets`, **HEAD `e60b19e5`, local==origin, pushed.** Prod untouched.
Save **v36** (`v35→v36` added `everBuiltStructureIds`; Echo lane tokens moved to a `<resource>:<level>`
grammar and are read-migrated — no further bump). **WO numbering: point at the
`CLI_LANES_WO_NUMBERS.md` banner, never copy a number — TWO blocks are live: main line (CLI) next free
853, reserved 860–899 (UI seat) next free 863.** Gates: `COMPILE_GATE_OK` + `REGRESSION_OK` +
**EditMode 884/884, zero reds** + **`UI_CAPTURE_OK 28`** (pixels opened).
⚠ The working tree is **NOT clean** — an in-flight item-identity lane is uncommitted.

**Shipped this session (each gated before commit — 21 commits dated 08-02):**
- **WO-830/831 the Echo harvest program.** Six Echoes, each with a harvest **affinity** — but
  **affinity is a MATCH BONUS, never a lock**: the player picks each Echo's harvest resource from a
  picker and matching the affinity **doubles** the yield. **Maren harvests Crystals, not Repairs.**
  Token grammar `<resource>:<level>`. 3 disclosed pair synergies + 1 hidden tri-synergy.
- **WO-835 action bar** — `HudActionBarModel` (Core) is the single enum-ordered authority; the View
  renders from the array and re-packs centered, so **holes are impossible by construction**.
- **WO-839 raid deploy · WO-840 armorer (`"Forge"`→`"Blacksmith"` anchor fix) · WO-841 upgrade countdown.**
- **WO-842/843/844 felt fixes** — GameState is the single Wood/Iron authority; destroyed/sold singleton
  buildings are rebuildable again (`IsPlayerBuilt` split from `IsBuilt`); Bag potions apply real effects.
- **WO-797 + WO-849 dungeons:** rooms OWN their enemies (wake-from-footprint + confine-above-retaliation),
  the exit carries a discoverability beacon, and pursuit now clamps to `max(slack, wakeRadius)` —
  "a mob may pursue as far as it can perceive". **WO-850** put a treasure cache in the deepest room
  (torch recipe unlock + fixed crafting supply).
- **WO-766 the tester program is real** — Solana wallet connect live (see the anchor §3), wallet-first
  Android login, bug-report attribution keyed to the bound wallet.
- **WO-836 MASTER_CATALOG full SME refresh** (`1812f3f8`, 14-agent fleet): **all 19
  `MASTER_CATALOG/<area>.md` sections rewritten from the actual code. They are no longer 06-12-stale.**
- **Evening gear/balance lane:** **WO-852** Echo card fixed-band layout · **WO-860** starter loadout is
  **sword + shield** (not the stale axe) + shelf thinning · **WO-861** Phase 0 (Sylas + Thrain) ·
  the global **tower research ladder** restored · **shields actually defend** (defense ladder + level
  gating) · **a respawn now MOVES you** — no more waking up on your own corpse.
- **Ten new oracles today**: raid-deploy-ui, wallet-provider, hud-actionbar, echo-picker,
  dungeon-room-ownership, realm-map + dungeon-treasure, echo-card-layout, starter-loadout, shield-defense.

**OPEN:** (1) owner felt-verify the evening lane (shields, starter loadout, respawn, treasure cache,
Echo picker). (2) **WO-848** restore Android managed stripping Medium (lowered to Low for the Solana
SDK's BouncyCastle CIL-linker resolve). (3) **WO-851** spec on disk, not implemented; **WO-861** in
flight; **WO-862** minted, not implemented. (4) **WO-774.0** raid spectator model + **WO-838** magenta
troops + **WO-837** stockpile caps — owner rulings captured, nothing built. (5) `auth_nonces` table does
not exist in Neon → wallet-auth stays permissive. (6) Owner ratification of the 860–899 UI-seat block
(the allocation is already operational). (7) The 6 Echo emergence PNGs (owner-owed art).

**⚠ APK PRECONDITION:** the Solana SDK is a **git-URL** package that re-resolves into
`Library/PackageCache` — run `tools/android/patch-solana-sdk.ps1` (idempotent) after packages resolve
and **before** any APK build, or the Android build fails.

---

## ★★ SESSION HANDOVER — 2026-08-01 (post-reboot ship wave + release train + canon refresh) ★★

**Anchor:** `CANON_GROUND_TRUTH_2026-08-01.md` (NEW — supersedes 07-26, bannered). Branch
`wip/village2-and-f8-tickets`, **HEAD `ac0a52e3` + the canon-refresh commit after it, local==origin,
pushed.** Prod untouched. Save **v35** (no new fields today). **WO next-free = 832** (banner is the sole
authority — point at it, never copy the number). Gates: `COMPILE_GATE_OK` + `REGRESSION_OK` (103 checks:
26 inline + 77 suites) + `UI_CAPTURE_OK 23` (pixels eyeballed; archive `Builds\ui-capture-archive\2026-08-01\`).

> ⚠ **SUPERSEDED 2026-08-02 — frozen ledger, true as of 08-01 only.** Current: save **v36**, gates
> **EditMode 884/884** + **`UI_CAPTURE_OK 28`**, and WO numbering now runs **two blocks** (main line
> next free 853 / reserved 860–899 UI seat next free 863). See the 08-02 block above.

**Shipped this session (each gated before commit):**
- **WO-818 ALL PHASES** (`e8bd17b0` + `777dd9ff`): 12 KayKit NPC bodies tracked (`KAYKIT_STAGE_OK 12/12`,
  Humanoid) + `repo.npcModel` on 12 owner rows (structures-catalog v6, dual-copy) + `KayKitNpcBody`
  KayKit-first resolver in both injectors + `NPC_MODELS` oracle. Body swap = one-word owner retag.
- **WO-826 Realm Map** (`eb5d0710`): parchment panel, Elarion home + 5 fog regions, strict MVVM over
  `realm-map.json`, HUD **Map** button (post-Onboarded), `REALM_MAP` oracle + 8 EditMode tests, capture
  verified. Travel stubbed → 827. WO-825 program IN FLIGHT.
- **OWNER RULING: bar Queues button RETIRED** (same commit) — right-column **Builders chip** (QueueStatus
  band, above resources) is the ONE Queues entry; 6-face bar; `ObsidianQueueRegression` 7c enforces.
- **ProjectSettings batching RCA CLOSED** (`ac0a52e3`): reverter runs INSIDE BuildPlayer (twice-captured);
  DesktopBuild now re-asserts static=0/dynamic=1 post-build.
- **Dungeon verified from a captured run**: owner-ordered log test — all 7 proving lines + R-A1 arena
  guard green (open: 770.10 vitals placeholder, 770.8 props, `EnvTreeFix 'Skeleton_Mage_Hat'` minor).
- **Release train:** fresh desktop exe · Seeker APK built + **installed on-device (adb Success)** +
  **Firebase App Distribution to testers** · WebGL→Vercel PREVIEW in flight (promotion = owner).
- **UI seat reconciled** (sole-committer flow): WO-830 Echo harvest-affinity program + WO-831 emergence
  sprite beat minted (banner → 832); frozen `docs/qa/UI_REVIEW_2026-08-01.md` banked.
- **Canon refresh (this doc-pass):** 08-01 anchor minted; KEY_FACTS/START_HERE/CLAUDE.md §7-8 corrected
  (incl. the two boot-router lies: the disproven `vercel logs [sig]` read path and "api/ gitignored" —
  it is TRACKED, 25 files); WO 774-831 status headers reconciled (18 stale headers flipped, RESULT files
  added for 818/826); flag XML-summary lies documented (12 flags — trailing `//` comment is truth).

**OPEN:** (1) owner felt-verify: Realm Map, 6-face bar + Builders chip, KayKit NPCs, 819/820/810/808/
812/813, WO-825 R1-R4 rulings, ~~wave-1 zero-enemy data ruling (2 reds in
`Assets/Data/Tests/WaveDataTest.cs`)~~ **← CLOSED: there is NO open ruling. The owner ruled
smart-composition on 07-30; both tests were rewritten to assert the batches are EMPTY and the reds are
gone (EditMode 884/884 at 08-02). A re-add now FAILS.**
(2) Queue: 822 → 817 ph1-2 (phase 0 needs owner image-pair sign-off) → 821 → 827/828/829; 830/831
owner-sequenced **← 830/831 SHIPPED 08-02.** (3) 823 Phase E soft gate — owner ruling. (4)
~~MASTER_CATALOG `<area>` files remain 2026-06-12-stale (07-22 §6/§7 ledgers = fix list; housekeeping WO
still unminted)~~ **← DONE: WO-836 (`1812f3f8`, 2026-08-02) rewrote ALL 19 `<area>` sections from the
actual code. Not stale.** (5) CS-1 ring/amulet
non-persist. (6) PIPELINE_STATE deep-history sections still carry dragon-license trap rows (L203/L226 —
assets git-rm'd in WO-760; do not act on them).

---

## ★★ SESSION HANDOVER — 2026-07-26 (dungeon+raid felt-test wave + Sunday housekeeping) ★★

**Anchor:** `CANON_GROUND_TRUTH_2026-07-26.md` (NEW — supersedes 07-22, bannered). Branch
`wip/village2-and-f8-tickets`, HEAD `7dec0e07`, **local==origin — the wave IS pushed** (a change from
07-22's push-HELD). Prod untouched. **Save schema = v35** — code-verified (`SaveSchema.CurrentVersion = 35`):
WO-773's Obsidian multi-channel queue (`obsidianQueue`) HAS shipped with a v34→v35 migrator, so treat it as
landed, not the backlog item this block's OPEN list (4) shows (that reflected the Sunday doc-pass, pre-landing).

**What this session shipped (felt-test wave, committed AND pushed):**
- **Dungeons are now a functional end-to-end loop.** WO-770 sub-orders: 770.1 (always-open exit + boss
  back-door), 770.2 (return to the CORRECT dungeon), 770.3 (real victory/defeat carrier — a lost fight ends
  the run), 770.3b (real-time `BattleArena.OnBattleEnded` → shared `SettleEncounter`, fixes the never-releases
  combat lock), 770.4 (readable lore stones + code-built modal), 770.7 (toast layer + live Bryn dialogue),
  770.9 (stale-read `OnEnable` clear). Plus DungeonHero sole-mover + taller camera + exit interaction
  (`82e1f3a4`), Bryn pill-hide over a skinned body (`f42e6f7e`).
- **Non-dungeon felt fixes:** enemies-stay-out-of-castle + battle-mode BattleLock (`e05f92f7`), towers no
  longer shoot through walls (Structure layer + LoS, `2cb3c40d`), MagentaGuard Android compile-failed-shader
  catch (`386a932f`), loading overlay + standard loading bar (`4edf8dcc`/`7dec0e07`), gate-traversal teleport
  disabled (`8c35332f`), collector buildings get vendor NPCs (`804a02a2`, Lever 1 in progress), Alchemy
  recipe list scroll-fix (`8ca95735`).
- **Firmed the dungeon/raid/enemy/queue WO set** (`docs/qa/`): WO-770 (dungeon functional, 11 sub-orders),
  WO-771 v2 (COC **Teleport/Deploy** raid — owner-LOCKED loop; walk-to retired), WO-772 (shared enemy
  system — classes/families/armor/weapons + `EnemyResolver`, fixes generic-skeleton bug), WO-773 (common
  Obsidian job queue). Validation sign-off: `docs/qa/dungeon-raid-validation-2026-07-26.md`.

**Sunday housekeeping (this doc-only pass):** minted the 07-26 anchor (delta over 07-22); refreshed the
read-first set (SESSION_CANON_LOADER, this doc, KEY_FACTS, PIPELINE_STATE, START_HERE, PROJECT_INDEX,
MASTER_CATALOG top); produced `docs/qa/SUNDAY_STATUS_2026-07-26.md` (full WO/ticket table); reconciled WO
numbering to **next-free = 774** in `CLI_LANES_WO_NUMBERS.md`.

**OPEN:** (1) **WO-772 Phase 1 UNBLOCKED** (PM 2026-07-26: Hollow Ones approved — see
`docs/PAIN_POINTS_2026-07-26.md`); Wildlands deferred. (2) Dungeon backlog 770.5/.6/.8/.10/.11.
(3) Raid V1 spine (771.0→771.1→771.1b→771.4→reuse combat→771.9+773 multi-channel→771.6 stakes→…) — nothing
built; **no 771.3 first**. (4) WO-773 multi-channel queue (Builder/Train/Research). (5) CS-1 ring/amulet
non-persist. (6) Art travel policy + verify script (PAIN_POINTS §1.2). (7) Re-run `DataRegression.RunAll`
before next ship.

---

## ★★ SESSION HANDOVER — 2026-07-22 (SME fan-out + canon refresh + branch hygiene) ★★

**Anchor:** `CANON_GROUND_TRUTH_2026-07-22.md` (NEW — supersedes 07-19, bannered). Branch
`wip/village2-and-f8-tickets`, HEAD `148ab637`, local==origin, push HELD.

**What this session did (read-only sweep + hygiene, no gameplay code touched):**
- **17-agent SME fan-out** (12 module + 5 high-level), each verified **from code not comments** (§12).
  Verdict: **code healthy, gates green (REGRESSION_OK 16 suites/0 reds, save v34)** — the debt is
  **DOCUMENTATION DRIFT.** The `MASTER_CATALOG/<area>` sections (dated 2026-06-12) are weeks stale.
- **Canon refresh (this doc's job):** minted the 07-22 anchor with a **§6 catalog-drift ledger** (every stale
  section + its correction) + a **§7 comment-vs-code lies registry**. Bannered 07-19 SUPERSEDED; updated
  `KEY_FACTS.md` + `SESSION_CANON_LOADER.md` same-breath (§15).
- **Branch hygiene:** removed 2 stale agent worktrees + local branches (dungeon work verified already-merged
  into wip); purged 2 stale remotes — `feat/tower-core-loop` (`cea673e4`), `samantha-village-progress-2025-05-23`
  (`40a570a6`). Remotes now `master` + `wip` only.

**Headline drift corrections (trust the 07-22 anchor over any catalog):** home hub = `Main_Castle_Overworld`
(MergedWorld ON, one navmesh, `Village.unity` deleted); `ff.atbdungeon` doesn't exist (real gate
`ff.dungeonrealtime` → dungeons into BattleArena); save v34 not v33; CoreServices 7 slots not 3; 23 build
scenes; ~70 catalogs; packs 13 not 5; HudKit replaced the 3-canvas HUD; MVVM ratchet closed; audio 5-group
mixer never built; HeroPortraits folder absent; deploy chain writes `CHAIN_DONE` on failure.

**OPEN (owner):** (1) real bug **CS-1 — equipped ring/amulet don't persist across reload** (declared +
migrator-seeded v26, no GameState field/Snapshot-Apply) → needs a ticket. (2) Queue the §6/§7 doc fixes as a
housekeeping WO (mint 754). (3) `GAP_AUDIT_2026-07-18.md` edit is commit-ready (documents the 07-18 fix batch
+ surfaces CS-1). (4) Push authorization still held. (5) Felt-verify queue + minted-but-open WOs 750-756 +
Grok 715-722 (PAIRWALK_716) unchanged from 07-20.

---

## ★★ SESSION HANDOVER — 2026-07-19 EVENING (felt-test fix wave) ★★

**Anchor:** `CANON_GROUND_TRUTH_2026-07-19.md` (still current). Branch `wip/village2-and-f8-tickets`.
This block sits ON TOP of the 07-19 morning block below (WO-748/749 done, all 5 regression reds GREEN
-> `REGRESSION_OK`, save v34, both known dictionaries, d4 purged). Do not act on the morning block's
"WO next-free = 750" line — it advanced this evening.

**THE EVENING FELT-TEST FIX WAVE (in progress; CLI is committing it):**
- **Pet screen** sort-order fix.
- **HUD** de-overlap pass.
- **WO-751 Y-height normalization (IMPLEMENTED):** every placed item normalized by Y-height —
  default **4m**, tower override **7m**, siege override **3m** — plus a Y-height audit tool.
- **Echo modal single-arbiter:** routed through `PanelManager` so only one Echo modal can arbitrate.
- **Upgrade panel visuals:** event-driven rebuild, text-fit, hotkey key-letters removed (mobile = no keys on HUD).
- **Flag-screenshot** saves on release (not on press).
- **In-flight (still being committed):** upgrade no-op blocker; white-ballista / magenta-weapon material
  fixes; **WO-753 Destructible lifecycle** (destroyed items = NO rebuild + full-cost + VFX cleanup via a
  new `Destructible` component).

**NEW WOs MINTED 2026-07-19 evening:**
- **750** — Right ActionBar naming + Warden's Grace redesign. SPEC (blocked on 2 clip IDs).
- **751** — Y-height normalization. IMPLEMENTED this wave (default 4m / tower 7m / siege 3m + audit tool).
- **752** — Echo founding-card overhaul + post-tutorial interjection. SPEC + creative sign-off (awaiting copy).
- **753** — Destructible lifecycle (no-rebuild + full-cost + VFX cleanup). IN PROGRESS (spec file pending).

**NEW DESIGN RULINGS / MEMORIES (2026-07-19):**
- **Right ActionBar = Attack + Q/W/E/R named skills:** Sword Wielding / Sword Heroic / Shield Charge /
  Warden's Grace / Radiant Strike. **Mobile HUD shows NO key-letters.**
- **All placed items normalized by Y-height** (WO-751 tiers above).
- **Echo = the essence of a person the tree guards** — 6 named people: Aldwin, Elowen, Corvin, Bran,
  Doran, Maren (feeds WO-752 founding-card overhaul).
- **Destroyed items = no rebuild + full-cost + VFX cleanup** via the `Destructible` component (WO-753).
- **A headless UI-screenshot pass must run before builds.**

**OPEN (owner):** felt-verify the fix wave on mobile (pet sort, HUD overlap, Y-heights, Echo modal,
upgrade panel legibility, flag screenshot-on-release); confirm the 2 clip IDs for WO-750; copy sign-off
for WO-752; then authorize the held push. `/mcp` still pending to unblock live Notion sync.
**WO next-free = 754** (750-753 consumed).

---

## ★★ SESSION HANDOVER — 2026-07-19 (all 5 regression reds GREEN; WO-748/749 done; d4 purged) ★★

**Anchor:** `CANON_GROUND_TRUTH_2026-07-19.md` (read first). Branch `wip/village2-and-f8-tickets`,
HEAD `98ff1135`, **local ahead of origin by 7, PUSH HELD** (owner authorizes push + prod).

**LANDED this session (7 commits, gate-green):**
- **DataRegression `REGRESSION_OK` — ZERO reds** (first time). All 5 owner-plan reds fixed: R1 arena
  ground texture (`00568728`), R2 dual-wallet Grant->GameState (`ef6f097b`), R5 orc-raider SSOT
  enemies.json Hp 130 (`6ac98fa3`), R3+R4 save v34 persist pet-slot + Tribes/Wards/Arena (`98ff1135`).
- **WO-748 Default Town founding choice** (`f5fcbde2`, RESULT-filed) + **WO-749 dungeons as crafting-
  ingredient source** (`0c64daaa`, RESULT-filed; +7 gear-component MaterialDefs).
- **Corrupt `d4_sunken_crypt` scene PURGED** (`c5b3461c`) + stale merged branch `feat/room-forge-dungeon-baker`
  deleted + the dungeon session's broken uncommitted socket rework restored out of the tree.
- **Process:** `SUNDAY_HOUSEKEEPING.md` weekly ritual + KNOWN DICTIONARIES registry (memories). **Notion
  setup kit** staged (`docs/notion/`), awaiting owner `/mcp`.

**OPEN (owner):** felt-verify the 5 red fixes on mobile (arena look/perf; orc-raider wave balance; multi-slot
pet + tribe/ward/arena survive reload; dual-wallet upgrade income) + WO-748/749 screens; authorize the 7-commit
push; `/mcp` to finish Notion. **LANDING:** the two known dictionaries (hero-animation, regression-coverage)
from the audit fleets. WO next-free = **750**.

---

## ★★ SESSION HANDOVER — 2026-07-18 (MVVM migration + Room Forge landed; hackathon WON) ★★

**Anchor:** `CANON_GROUND_TRUTH_2026-07-18.md` (read first). Branch `wip/village2-and-f8-tickets`,
**pushed to origin** through `b337affe` (+ the ping-time canon commit). Prod UNTOUCHED.

**LANDED + pushed this arc:**
- **WO-744 — strict-MVVM migration DONE.** Every panel View (all 36 from the audit) binds an
  `IPanelViewModel`; zero runtime game-state reads. Silos B/C/D/E/F/G + landmines. The `[ui-mvvm]`
  conformance oracle (`UiMvvmConformanceRegression`, in `DataRegression`) is armed **HardFailOnNew=true**
  with an EMPTY baseline — a new state-reading View now hard-fails the gate. BattleHudVM is behind
  `ff.battlehudvm` (default OFF — ATB feel-sim byte-unchanged); DialogueView's WO-702 build-truce is
  RELOCATED not deleted. Spec: `docs/UI_MVVM_MIGRATION_PLAN.md`. Repair-Wall dead-button also fixed.
- **WO-740–745 — Room Forge into mainline DONE.** Merged the dungeon session's socketed-room pipeline;
  17 prefabs + materials; demo bakes clean; `[room-forge]` 10-case oracle + `[Flow:DungeonBake]` + baker
  contract fixes. RESULT: `WorkOrders/WORK_ORDER_745.RESULT.md`.

**OPEN (owner):** felt-verify the converted screens + repair button + Room Forge scene; image-pair
screenshots (behavior-preserving, so pairs should show no change); Notion sync (needs `/mcp` auth).
**HAZARD:** the dungeon session shares this working tree — it caused branch + editor-lock collisions;
it should move to a separate git worktree. WO banner next-free = **746**.

---

## ★★ SESSION HANDOVER — 2026-07-13 MIDDAY (owner felt-pass + 7-lane parallel wave) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`, HEAD `2de11256` (07-13 morning brief),
**ahead 22 of origin** (a push landed 07-12 morning — older "95+ ahead" claims stale), push HELD.
Live anchor = **`CANON_GROUND_TRUTH_2026-07-13.md`** (07-12 bannered). Preview `9ncz1sks9`
**owner felt-passed 07-13 morning** → **PO CLOSED WO-677/678/682/683/685** (Done on Notion);
604/605 Dropped as deprecated.

**THE 07-13 WAVE (all edit-complete in the DIRTY TREE, UNGATED — one batch cycle pending):**
WO-680/UPG-1 upgrade-panel legibility (IsMax → no CTA; named-action gate copy; `[Flow:Upgrade]`
band-state traces; spec amendment A1–A4 parked — needs a factory pass) · **WO-602 home-return**
(4 runtime-injected "Enter Elarion" bridge-mouth portals → courtyard fade-warp, `ff.homereturnportal`
ON; KEY FINDING: **MergedWorld is ON — the live scene is `Main_Castle_Overworld`**, old anchor
world-line was stale) · WO-681/ECHO-1 echo card (Obsidian modal on the TalkPromptRegistry seam,
hosts the WO-658 picker, placeholder wisps — no Echo body existed; `echoLanes` additive save field,
**v31 bump pending at reconcile**) · WO-693 jeweler/crafting readability (shared parchment detail
card in the kit, OK/X + have/need rows, FontFloorMobile=30; real defect was raw 13/14px literals,
not FitBlock) · **WO-695 strategic placement flag REMOVED** (ex-682; 21 files; blank-template new
game, marker-latched one-shot migration proven, FTUE guard = grace-default Forge record) ·
**REP-1 root-fixed** (hardcoded `Repair(100f)` vs Building MaxHp 120–240 additive clamp = full-price
spend, partial restore; walls/gates ≤100 masked it; fix = `RepairTarget.RepairFull()` at both call
sites + permanent `[Flow:Repair]` traces + standalone `RepairProbeRegression.RunStandalone` whose one
run emits BOTH §12 closure lines). **IN FLIGHT:** WO-697/RES-1 currency CompactNumber + icon-chips.

**HYGIENE LANDED:** every WO number on disk is unique — dupes → 688–695, fresh UI-seat mints →
696 (repair-context, ex-684; wall granularity RULED: nearest damaged segment) · 697 (RES-1) ·
698 (encounter budget, ex-685, all 4 pins RULED). Banner next-free = **699** + a UI-seat
translation table (its 682=695 · 683=693 · 684=696 · 685=698); owner syncing that seat.

**NEXT:** WO-697 lands → reconcile (v31 bump; GameStateService/CoreSaveRegression overlap review
echo-vs-strategic) → ONE batch: CompileGate → DataRegression → REPAIR_PROBE → build → fleet
(HOME_RETURN + tutorial + panel probes = verdicts) → commit by lane → owner felt-pass → READY
queue (674, 676, 679, 696-after-REP-1-verify, 698). Boards: Task list has all 8 tickets w/
handoffLog; Notion rows per the anchor's list.

---

## ★★ SESSION HANDOVER — 2026-07-12 EVENING (mobile-web demo wave — ⚠ superseded as newest by the 2026-07-13 block above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`; **5 lane commits LOCAL tonight, push HELD**
for the owner's word. NEXT CLI: **read `START_HERE.md` (repo root) FIRST.**

**LANDED (all gated):** WO-678 Pi 120s timeout clean wrap — template unhandledrejection/showBanner
ownership (`66b3272f`) · WO-677+683 build-mode touch verbs — uGUI verb bar; kit d-pad re-hosted on the
build overlay publishing HudMoveInput, merged into the arrow-key move read; "Rotate Left/Right" text
labels; palette chips de-glyphed; AssertBuildMoveChain DPAD probe link (`c963a553`) · hidden mobile dev
unlock — 5 taps on help title → Grant Resources (`33799026`) · WO-682 quiet web errors — db-proven
`Loading FSB failed for audio clip "SwordSwing"` + 167ms/4000ms stalls; 13 Sfx metas swept of WebGL
platformSettingOverrides; AudioService Guard-wrapped + PrewarmCombatSfx on Battle/Arena music cue +
dead-clip quarantine; new SFX_WEBGL_OK oracle (`965309a6`) · docs + **`CANON_GROUND_TRUTH_2026-07-12`
anchor (supersedes 07-08)** (`683b917b`). RESULT files written for 677/678/682/683.

**GATES:** COMPILE_GATE_OK; DataRegression = the 3 known pre-existers only, zero new. Windows build
SUCCESS tonight; at handoff a chain is RUNNING: 4-bot fleet (seeds 8200) + ship WebGL (**NO -DevBuild**
— kills the Development-overlay "giant json failure screen" class) + Vercel preview.

**WEB DEBUG LOOP PROVEN:** WebTrace (`?trace=1`) → POST `/api/trace` → Neon `analytics_events`; CLI
read path = the `[sig]` echo in Vercel runtime logs (`get_runtime_logs` / `vercel logs`) because
DATABASE_URL is sensitive/unpullable. `api/` lives **IN THIS REPO**, gitignored (`api/` at the repo
root — the root itself is machine-dependent, so never write a drive letter) —
the older "separate React repo" canon is WRONG.

**OWNER RULINGS tonight:** errors caught quietly (never a player-visible failure screen) · build-screen
d-pad = the kit d-pad + text labels · pre-warm combat audio on battle load.

**IN FLIGHT at handoff:** VFX Caster tagging extension — tag effect → Cast/Projectile/Impact key via
manual-overlay JSON, generator merges manual-wins.

**OPEN:** owner felt-pass on the new preview → push authorization · WO numbering authority refresh
(next free 684; 677/678 collisions) · loader-error beacon idea · preview SSO bypass friction.

---

## ★★ SESSION HANDOVER — 2026-07-11 F8 BATCH + ACTION KEYWORD REGISTRY (⚠ superseded as newest by the 2026-07-12 EVENING block above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`. Origin sits at `369c4f30`; local commits through
`10c60eb3` (push held for owner word). **Save schema = v29** (heroLevel/heroXp/heroLifetimeXp — F8-47).
**Felt-verify exe = `Builds/Windows/DefendersOfTheRealm.exe` stamped 2026-07-11 15:02:34.**

**LANDED (owner's 1:30 PM F8 batch, all RCA-proven-by-data):** F8-47 level-reset-on-outpost-return =
save-v29 persistence (`4064a44e`) · F8-43 compact-banner Continue CTA removed + F8-45 damage report
(WO-38 repair prompt self-installs into real wave scenes + WaveDamageReport rows w/ repair costs +
collector damage scales accrual — owner: "damage to collectors reduces economy") (`761d1d16`) · F8-46
option A: pursuit raises BattleLock via PursuitBattleProbe (`431f3ea0`) · F8-44 20-wave schedule, Syndrath
at wave 20, Necromancer cadence 6/12/18 (`c768fe6a`) · RepairTarget undeclared-HeroTarget-tag fix — latent
§7 violation woken by the F8-45 install, fleet-verified GONE (`10c60eb3`).

**NEW ARCHITECTURE:** `docs/ACTION_KEYWORD_REGISTRY_ARCHITECTURE.md` (BINDING for motion work) —
keyword→action registry (`motion-castings.json`, dual-copy): targets×keywords→{clip,vfxKey,sfxId,
vfxDelay,attachBone,playOneShot}, manual:true = owner canon, bake-time V1 / runtime Phase 2. Foundation
committed (`941ef16c`): MotionCastings resolver + ActionKeywords + builder seams (empty registry =
byte-identical bakes) + EditMode gate tests. WO-670 = Motion Caster authoring window (lane in flight),
WO-671 = action bundle rows + runtime ActionBundlePlayer (lane B done in tree, uncommitted). §9a of the
arch doc = the Grok Action System adopted/rejected ledger.

**FLEET (exe 15:02:34):** clean of new tickets; remaining = known pre-existers only (WO-453 encounter
strand, WO-602 home return, CavePortal reach) + AssertTutorialFirstTower probe drift (candidates occupied
by the 07-10 Colosseum_ArenaEntrance — player placement fine, probe fix filed). DataRegression: 3
pre-existers (arena ground texture → F8-37 evidence · B2 dual-wallet · pet-slot flag_17), zero new.

**NEXT:** owner felt-pass on exe 15:02:34 → push on her word · WO-670/671 lanes gate+commit ·
open owner pins: F8-40 max-tier tower identity · WO-614 tree→W/E/R rail seam · probe fix.

**MORNING ADDENDUM (2026-07-12 — current felt-verify exe = 2026-07-11 23:51:48):** overnight
session delivered: (1) **Regroup death-cycle FIXED** — RCA proved one death fired two racing
recovery systems (arena loss-return left the death latch set under ff.noautoheal; HeroHealth's
respawn double-warped); arena now owns loss recovery, HandleDeath defers w/ 10s net (36acc05f).
(2) **Registry-only motion VFX** (owner directive): all abilities.json Vfx* defaults + the
hardcoded per-swing Melee_Slash burst OFF; the ONLY VFX authority = owner Motion Caster rows,
now wired to runtime for the first time via ActionBundleCatalog (17862c51). (3) **Movement feel
restored** — f7740f4e's per-frame velocity snap rate-limited to 540°/s (ee52e399); stale-clip F8
sentinel retired. (4) **F8-49 ROOT-FIXED** — 19 Lana Studio mats upgraded to URP at source
(LANA_URP_FIX_OK, b5694a05). (5) **SME PROGRAM: all 8 pack dossiers written + committed** —
router `docs/SME/README.md`, ledger of all 34 store products; headlines: Hovl demos run Bloom 5
vs our Bloom OFF; Blink's rigged orc bundle + 608 icons unused; KayKit 33 rigged characters
unused; polyperfect 240 rigged villagers unused; ⛔ apex-dragon model CC BY-NC (license/replace
before commercial release, memory + dossier); Raid BGM dead-wired (~8-line fix, AUDIO_SME).
(6) **WO-688 minted** (renumbered from colliding WO-677, 2026-07-13) — Asset Caster toolkit family (Icon/Gear/Audio/Character/Texture Casters),
Phase 0 applicability assessment ready to run on the dossiers. Tools shipped earlier same night:
VFX Caster window + Motion Caster preview gear/mocap filter. 24 commits local, push HELD.

**NIGHT ADDENDUM (2026-07-11 late — superseded as newest by the MORNING block above):** the orc
frozen-bones family is CLOSED: RCA proved loose-part Tripo exports (mesh not skinned to the animated
skeleton — no importer fix exists); owner re-exported Warrior/Tank/Mage via AccuRig (proper
pelvis/spine skeleton); ImportOrcFamily verdicts = ENTIRE family "OK Humanoid" incl. the previously
unrepairable Berserker; the fleet's standing Berserker rig warning is GONE. Grok session landed 8
commits (T-pose take stripping, walkforward01 calm gait rework, post-combat facing/sheath sync,
camera recenter, VFX stacking, AccuRig Tank) — reconciled + gated. Motion Caster is now owner
self-service (bundle preview w/ VFX-on-bone, SFX audition, one-button FBX intake with per-take
T-pose + root-travel warnings). Grok escalation pack: `logs/debug/BROKEN_ITEMS_2026-07-11.md` +
`GROK_ESCALATION_2026-07-11_orc-rig-family.md`. Queued next: vfxEuler rotation dial on bundle rows ·
WO-674 walls · WO-675 panel · WO-673B fast-follows · push authorization.

**EVENING ADDENDUM (same day, waves 2+3 — superseded as newest by the NIGHT block above):** the 15:02
batch was FULLY felt-closed (F8-43/44/45/46/47 all owner-verified). Then landed, gated, fleet-clean
(only the 3 known pre-existers): **F8-48** Mend heals (28/28 casts were move-interrupted; now instant +
real cast take, `5c7782f9`+anim) · **WO-672/F8-50** unified damage lifecycle (hp==0 = broken shell
everywhere, damage bars + Ember smolder/fire tells, Raid_Explosion on break, `damage-states.json`;
Repair All on the wave report via the one crystal spend path; `80a2f944`+`1b3224f6`) · **F8-49** 135
built-in legacy particle slots URP-swapped at source via re-runnable MagentaMaterialFixer pass
(`15b8bf30`) · **owner clip picks ×5** (Leap=jump-stab, W=Slash, Block=swipe01→02 chain, Heal=
magespellcast-02, Fireball=magespellcast-04) — SwordShieldMovesImporter extracted 12 clips (first
Magical Moves extraction), KnightPackage rebaked, `[MotionCaster] (manual)` consume lines proven
(`54d5e9fd`) · **Q medallion "Dodge/Attack" text placeholder** (`977b3737`) · **endless waves past 20**
(owner ruling: manual DEFEND starts, stats+counts scale, apex returns as cycle capstone; `04481c59`) ·
fleet self-blindness fixes (probe validates via the real placement gate `2990aaf6`; bots reset wave
progress `d2f57867`). WO-670/671 committed (`8a0bdddd`/`8084d8ee`): Motion Caster window + runtime
ActionBundlePlayer. Open: WO-614 rail seam · F8-40 · pet-slot persistence · B2 dual-wallet · arena
ground texture (F8-37 evidence) · broken-state save persistence follow-up · push authorization.

---

## ★★ SESSION HANDOVER — 2026-07-08 WAVE-2 CLOSE (⚠ superseded as newest by the 2026-07-11 block above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`, **HEAD `d944d161`, 71 commits ahead of origin,
clean tree, push HELD for the owner's word.** Live anchor = **`CANON_GROUND_TRUTH_2026-07-08.md`**;
wave-2 prep + open board = **`CLI_PREP_2026-07-08_next-session.md`**. The overnight P0 fix (below) is
**owner felt-confirmed** (tutorial completes, placement works).

**WAVE 2 CLOSED (05:10):** all morning F8 lanes committed through `bb0094cc`; final fleet on exe
**2026-07-08 05:10:11 = ZERO tickets, all probes PASS**. Landed since the overnight sweep: F8-24 castle
wall-stairs swept from the SHIPPED merged scene + navmesh rebaked (`13e85e12`), F8-31/32 nameplate GUID
repair + portrait circle-mask, F8-33/35 Victory rows + BR ability icons, F8-15 death forensics
(`[Flow:DeathTrace]`), F8-6 tree pose, "Tap to continue ▸" passive hint replacing the Continue chip,
WO-614 skill-tree rulings stamped, fleet self-fixes (ArcaneTower fake-null NRE, popup oracle tap-advance
contract, compass ForceProviderPoll). **WebGL PREVIEW = https://defenders-of-the-realm-v2-h0h6hfsf5.vercel.app**
(from `bb0094cc`, READY; supersedes `2dizrqgws`). **Production untouched** (07-01 Pi build) — promotion +
push are the owner's. Fresh HEAD gates this session: `COMPILE_GATE_OK` + `REGRESSION_OK`. Save schema **v28**.

**NEXT:** owner felt-pass on exe 05:10:11 → name passes → push. Big next lane = **WO-614 skill-tree solo
rework** (RULED, READY). Open owner directives: F8-40 max-tier tower identity · F8-41 waves attack the
city · F8-42 repair costs (all in `CLI_PREP_2026-07-08_next-session.md`). Pre-existers unchanged: WO-602
home-return, CavePortal seam, WO-453 rep spawn.

---

## ★★ SESSION HANDOVER — 2026-07-08 overnight verified-root-cause sweep (⚠ superseded as newest by the WAVE-2 CLOSE block above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`, ~75 local commits through `7e663981`,
**push HELD for the owner's morning word**. The owner's night session ended with a BINDING directive
(now memory `step-in-step-out-verified-root-cause-every-bug` + TICKET_PIPELINE rule 0): every
reported-broken flow gets step-in/step-out gate instrumentation + a REAL-PATH automated probe, and
closes only with TWO verbatim captured lines (root cause + post-fix verification).

**THE P0:** "still cant do the tower" — dialogue Closed re-entrancy destroyed the successor
dialogue's panel → `InputSuppressed` stuck → build-mode Update frozen, zero click evaluations.
Fixed with a per-VM Closed identity guard (`82422d11`); every placement gate now names itself when
it blocks (`aec9feca`).

**VERIFIED (final exe 2026-07-07 23:50:04, 4/4 fleet runs, real input seams):** 8 new probes ALL
PASS — first-tower placement chain, dialogue chain survival, tutorial arms on fresh save (F8-29),
orient-modal release (F8-30), wave vendor rules (F8-14), compass pips (F8-16), scatter bands (F8-8),
hero albedo 19/19 with no WHITE HERO ROOT (`f4aeae8c` probes, `7e663981` retired the -nographics
HasProperty false-Fail — audits now read the serialized material sheet). Remaining fleet tickets =
the 3 known pre-existers only (WO-602 home-return, CavePortal seam reach, WO-453 rep spawn).

**FULL LEDGER:** `RESUME_2026-07-08_overnight-f8-sweep.md` (the morning report — verify list +
verbatim-line ledger + the honest open list). WebGL rebuilt from `7e663981` and deployed to the
**Vercel PREVIEW**: https://defenders-of-the-realm-v2-2dizrqgws.vercel.app — production untouched,
promotion is the owner's call. Windows felt-pass exe: `Builds/Windows/DefendersOfTheRealm.exe`
stamped **2026-07-07 23:50:04**.

---

## ★★ SESSION HANDOVER — 2026-07-07 evening F8 batch (⚠ superseded as newest by the 2026-07-08 overnight block above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`, 5 lanes committed LOCAL (`26cc6d47` →
`90541989`), **push held for owner felt-pass**. The owner's evening felt-test produced 7 F8 flags +
3 chat directives — all triaged live via the F8 watcher (QA read-only RCA agents, every fix
§12-data-proven), implemented, and verified: COMPILE_GATE_OK + REGRESSION_OK + fresh build + 4-bot
fleet (13/13 panels, popup-close clean, vendor talk-route 0 violations, combat invariants PASS).

**LANDED (ticket → commit):** F8-2 wizard tower z-90 (`26cc6d47` — orientation was authored but
inert, manual=false; now euler Z-90 + manual=true, and ReskinForLevel no longer applies base-authored
euler to tier models) · F8-6 wood node y+90 (`25da4062` — per-Wood LocalRotation pre-SeatFlat at
MineNodeVisual + HarvestSite) · F8-4 black interact removed for buildings (`3b795bf0` — NPC Talk is
the one path; uncovered buildings self-report via Warn; the old cover gate leaked on null hook ids
Apothecary/JewelersBench) · F8-7 target frame hides without a target + moved to its designed
TargetInfo zone, compass keeps Status in combat postures (`e01553aa`) · F8-3+F8-9 attack pill art
(root cause: icon_energy_sword shipped textureType:0, never a Sprite → fallback to old icon_sword;
proof Player.log:15629) + first-ever currency icon mirror, all five owner picks (gold=Blink
Gold_Currency, wood/food/crystal/iron=HudIcons) (`90541989`).

**HELD OPEN:** F8-1/F8-5 dialogue Close seat (RCA proves box-in-a-box in DialogueView.BuildUi —
inner DialogueInterior plate + frame + Close seated at the plate floor — but the current tree's
seat math may already differ from her build; adjudicate from a fresh capture before editing) ·
F8-8 roaming enemy families + danger gradient (owner directive, canon-grounded — needs a spec WO;
consolidate the divergent RegionMobSpawner/EnemyOutpost/GarrisonController stat blocks into
enemies.json as part of it) · F8-10 PartyBar 'Grom' label 0-glyphs (fleet 4/4, pre-existing class).

**VERIFY NEXT (owner):** fresh build at Builds/Windows — wizard tower roll, log-pile yaw, attack
pill = pixel energy-sword, no black interact at storefronts, compass visible in combat + target
frame only with a real target, resource chips show the five icons. Board = Task list tickets
F8-1..F8-10 with full hand-off logs.

**WAVE-2 ADDENDUM (2026-07-08 late night, commits `908add29`→wave end):** owner directive "get
everyone working" — 10 parallel agents, one batch gate, ONE build (owner ruling: no piecemeal
rebuilds — memory `one-build-one-handoff-never-retest-stale`). Landed: F8-8 scatter enemy families
(18 seeded records, 3 danger bands, sight-instantiated 85m/cull 115m — runtime traces pending owner
session) · F8-1/5 dialogue rebuilt on FrameCore (interior plate deleted) · F8-14 wave rules (vendors
hide, shops closed toast, build timers verified wall-clock) · tower identity (Ballista 22-range
physical BOLTS, Arcane CASTS — orb + Aether blast on arrival, new repo.projectileStyle) · F8-29
tutorial bootstrap fixed (one-shot hub gate on Title = V2 never constructed; now sceneLoaded re-arm)
· F8-10 PartyBar label (fleet-verified GONE) · F8-13 watchdog build-mode gate · F8-21 harvest verbs
· ff.combathud611 default ON · flag screenshots session-stamped (evidence-loss fix) · WO-613B
outpost chunk spec. Fleet: 3 confirmed = pre-existing knowns only. RCA docs:
docs/STRUCTURE_TRANSFORM_CENSUS_2026-07-08.md (+risks R1-R6, R1 = fit-before-upright). Exe 21:39:47.

**LATE-SESSION ADDENDUM (2026-07-08, commits `b45bb0bb`→`c7d913a3`):** RCA-PROOF-BY-DATA is now
BINDING pipeline rule 0 (`75e4d128`; owner directive — every ticket carries verbatim proving lines).
Landed: F8-11 DevTools scroll + yarn row removed · F8-12 dock pinned to real size (was ~5% of screen
— fraction-of-parent in the tiny Dock mount) · Wizard Tower → **Ballista** (owner ruling; upright
X-90; Ground placement — was stuck on the old WallWalk rule, 'stays red' RCA `164d0c24`) · Arcane
Spire base euler (-90,90,90) + WHITE FIX (extraction never ran + the remap step was never in the
code; new single-asset extract+remap+save, externalObjects verified — `f23d05ae`) · **Orient tool
saves locally** (StructureOrientationLocalStore, persistentDataPath overlay wins at catalog load —
the gear-offsets pattern; `96a90054`) · Ballista card art ×3 transparent (`917e8d23`) · F8-15 stage-1
death slow-trace (listener dump + down-beat milestones, `e95c538d`) · owner gear/sheathed harvests
(`c7d913a3`). RCA doc: `docs/RCA_DIALOGUE_DOUBLE_FRAME_2026-07-07.md` (owner decision pending:
option A = window frame + delete the interior plate). Board tickets F8-11..F8-16 filed. Exe built
2026-07-07 late evening carries ALL of it — the owner was felt-testing a stale exe earlier (proof:
session_start 19:24 vs exe 19:53); RELAUNCH before judging.

---

## ★★ SESSION HANDOVER — 2026-07-07 offset persistence (⚠ superseded as newest by the evening F8 batch above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`, **PUSHED** (owner logout handoff). Latest:
`0492d7dc` **fix(gear): local offset settings persist + immediate re-equip on save** — stacks on
overnight `88d6fbc9` (WYSIWYG scale parity + `@sheathed` registry + Seating Editor Drawn/Sheathed
toggle).

**WHAT CHANGED (gear offsets — owner ask: "save should stick in town immediately"):**
- **Local settings file (primary authority):** `Application.persistentDataPath/attachment-offsets.json`
  — every in-game Save writes here; entries **win over** shipped `Resources/OffsetForge/offsets.json`.
  Legacy `offsets-dev.json` auto-migrates on first boot. **PlayerPrefs** mirror: `dotr.attachment-offsets`
  (restores file if deleted).
- **Always fresh on apply:** `AttachmentOffsetRegistry.Reload()` runs before every `EquipBestForHero()`
  (scene load, gear swap, post-save re-seat).
- **Save = immediate re-equip:** Seating Editor Save persists → reload → full re-attach from file
  (not preview-only). Status shows the local path.
- **Town carry fallback:** when no `<mesh>@sheathed` entry exists, drawn keys (`sword_A`, `shield_A`)
  nudge the built-in back pose so hub/town isn't a second ignored orientation system.
- **Seating Editor default:** opens in **Sheathed** mode when hero is out of combat (town view).

**KEY FILES:** `AttachmentOffsetRegistry.cs`, `EquipmentController.cs` (`ApplySheathedOffset`,
`SaveSeating`, `TryResolveSheathedOffset`), `SeatingEditorOverlay.cs`. RCA:
`docs/RCA_WEAPON_OFFSETS_2026-07-07.md`.

**VERIFY NEXT (owner):** launch build → town → Seating Editor → dial → Save → walk hub without restart;
pose should match saved file. Optional fine-tune: explicit `sword_A@sheathed` / `shield_A@sheathed`
entries for perfect back pose (Drawn/Sheathed toggle).

**A/B ADDENDUM (2026-07-07, pre-felt-test review):** the `0492d7dc` drawn→sheathed fallback composed
the HAND-frame drawn euler (e.g. `sword_A` (117,-61,-111)) onto the chest-socket back pose — a frame
mismatch flagged in review. Now flag-gated: **default = position-only nudge** (frame-safe);
**`ff.sheathdrawnrot`=1 = the full pos+rot compose** as the backup if position-only doesn't carry the
town fix. Explicit `@sheathed` entries identical under both. Also: legacy `offsets-dev.json` is deleted
after migration (stale-shadow guard) + the RC3b Resources-first banner restored in
`AttachmentOffsetRegistry.cs`.

**COMMITS PUSHED THIS HANDOFF:** `75bffabd` → `88d6fbc9` → `3b4cfeac` → `b5547351` → `0492d7dc`.

---

## ★★ SESSION HANDOVER — 2026-07-06/07 (⚠ SUPERSEDED by offset block above for gear; UI/HUD lanes still valid) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`, 4 lanes committed + PUSHED (owner-authorized
for the demo recording): **WO-611 combat HUD** (owner v8 design: inset vitals well, d-pad cross, attack
pill with owner pixel energy-sword art fit-to-frame, medallion arc, lock crosshair, hostile→CloseAll
incl. wave countdown; F1 blank-icon guarantee; F3 truthful enemy Level), **WO-612 build timers**
(WO-172 service finally wired at its documented seam — 15 s base, 2 slots, offline-fair scaffold+
countdown; `ff.buildtimers` ON; owner direction = grow to option-3 "free income": rewarded-ad skips,
never a wall, no real player cost), **tier-model reskin** (`StructureFactory.ReskinForLevel` — the
write-only `upgradeVisualPath` is now consumed on upgrade + reload; owner F8 "upgrade just makes it
bigger" fixed), **3-type palette** (Archer/Wizard/Arcane; catapult/siege/walls/gates filtered,
reversible), owner card art + Tripo tower models (Wizard base + ArcaneSpire 1→2→3; force-added — the
Structures folder is gitignored for polyperfect mirrors, Tripo assets are owner-sourced), **archer
Tribal ladder** (`_bug22` RESOLVED via CatalogPrefabImporter `_T`-root support), owner Seating-Editor
offsets harvested, **'K' high-level scatter rig** (Lv15–27, 120–200 m out, hold-ground, skull plates),
`ff.skrpreview` ON for the demo (panel self-labels PREVIEW·TESTNET).

**VERIFIED:** COMPILE_GATE_OK ×4, REGRESSION_OK, 4-bot fleet — 13/13 panels, popup-close clean,
economy/equip/save green; only pre-existing errors (white Paladin albedo, WO-602 home-return,
WO-453 encounter strand). **HONEST FINDING (owner asked):** there is NO hero-vs-enemy level-delta
damage rule — level = authored-HP band only; damage is stat-driven. A real level-gap curve = open
owner design decision. **OPEN F8 TICKETS (board #1–5):** town vitals bars outside plates (RCA done:
shared BuildPartyNameplate StatBars inset insufficient — ElarionUiKitNameplate.cs:133), XP bar +
Wisdom "434" chip unlabeled (+ TWO XP bars redundancy), resource rows without identifiers (WO-611 F4
currency art), build-palette proportions, death-panel overlap. **NEXT:** owner demo recording →
ticket batch #1–5 → WO-613 VFX moments (overnight spec READY) → WO-545 Addressables.

---

## ★★ SESSION HANDOVER — 2026-07-05 (⚠ SUPERSEDED by the 2026-07-06/07 block above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`. This session landed the **AccuRig skeleton
family** (Mage / Warrior / Ranger→`Skeleton_Rogue` / Healer), `SkeletonHumanoid.controller`, codex
catalog updates, **hollow-warrior → Skeleton_Warrior** with stats tuned off bruiser, and **proportional
sword/shield** on the hub knight (`EquipmentController`). The **rig importer** now self-verifies —
`PeopleCharacterImporter.ImportSkeletonFamily` runs a per-model avatar verdict (OK Humanoid / WARN
Generic / FAIL) + 3-pass bone-map repair, so a missing/mismapped bone surfaces at import, not as an
in-game T-pose. KayKit legacy kept for Minion / Golem / Necromancer. Earlier the same day, two
**hero-feel fixes** landed: (1) **walking animation / turn-clip conflict** — the `turnleft180`
turn-in-place clip (low-pivot, reads as a crouch) was fighting the walk-forward clip when the hero
turned while walking; fixed by making turn-in-place clips combat-only + slewing town facing by input
+ a town walk-speed cap so KnightMocap stays on the upright `Shared_Walk_Forward` gait (`86847b7f`);
and (2) **native sword grip** (SeatNative, `ff.weapongripinfer` rolled back — `d48bfd41` WO-478).
(Separate same-session combat-anim work: posture flip + directional death — `315d60e3` WO-609 /
`38c7fd4b` WO-586.) **Committed locally; push held for owner felt-pass.**

**VERIFY NEXT:** mixed hollow wave in Windows build — four silhouettes animate, warrior feels mid-tier
(not golem), knight gear scale. F8 queue still open (HUD left panel, Forge mobile, battle posture flip).
Full notes: `RESUME_2026-07-05_skeleton-family-handoff.md`.

**IMPORT (if re-exporting skeleton FBX):** `Defenders → Animation → Import Skeleton Family (AccuRig)`
or batchmode `PeopleCharacterImporter.ImportSkeletonFamily`.

---

## ★★ SESSION HANDOVER — 2026-07-03 (⚠ SUPERSEDED by 2026-07-05 block above) ★★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`; the 07-02→03 **convergence session** (~25
specialist agents, two felt-tests, 46 F8 flags all triaged) is in the tree, uncommitted — commit lanes
being staged by explicit path, **push held for owner felt-pass**. Current focus = **THE FEEL ARC**
(owner: "the most important thing is how it FEELS"; the ten-year-old test is the standing quality bar) —
this supersedes web/Pi stabilization as the live thread. Owner verdict after the convergence build:
"I love the terrain… feels like there is something real now."

**LANDED (fleet-verified):** **south vertical slice 6/6** round trips, `tapped=False` (masked warp
mid-bridge both ways — the natural raise→moat→water→bridge seam works; N/W/E waits for south "feels
perfect"). Post-processing was structurally DEAD until 07-02 — fixed (WorldFeelInjector, `ff.worldfeel`,
dusk palette) + terrain relief/treelines. Character/combat/UI feel passes across ~50 systems (double-sided
materials, anim cadence/smoothing, HUD-bleed fix, NPC cards, vendors data-mapped, WO-596 bug report,
end-state template). **Tutorial V2 BUILT** behind `ff.tutorialv2` (default OFF — flip after its own fleet
pass). Vercel preview = full convergence build; prod stays on the 07-01 Pi build until promoted.

**BINDING RULE RATIFIED:** **read-before-assert for EVERYTHING** — code and non-code; memory lines are
pointers, never answers. Plus the extended UI canon (earns-its-place, one action = one button, no dead
buttons, shared currency chip) and "what CAN stream, SHOULD stream."

**OPEN OWNER DECISIONS:** un-park seam un-stack WO-453 (encounter-return strands ~7.1km, publisher
critique #1) · promote preview→prod · push authorization · wall stairs · ramp decks · necromancer 50%
beat · caster cast clip · dungeon theme · CastleMoat default-ON.

**NEXT:** owner south walk → commit lanes → **WO-545 Addressables streaming**
(`docs/WEBGL_DELIVERY_PLAN_2026-07-03.md`). Resume doc: `RESUME_2026-07-03_morning.md`.

---

## ★ SESSION HANDOVER — 2026-06-28 (⚠ SUPERSEDED by the 2026-07-03 block above — kept for history) ★

**WHERE WE ARE:** Branch `wip/village2-and-f8-tickets`, HEAD `7c05cd1b`, **nothing pushed**. The
single-Knight-pivot arc plus the WO-560→584 arc landed: overworld real-time **BattleArena** (lock-on
WO-512, 9-zone HUD, victory/defeat + star rating), **Echo workforce** wired (1–4 echoes, offline
real-clock, **save v27**: v26 ring/amulet, v27 wall-mount seating), **village-tier upgrade** unlocking
the WO-432 building tree, **store redesign** (WO-501) + **gear balance** (WO-500), **Offset Forge** offsets
on weapon attach (WO-490/510), **castle moat + 4 drawbridges** (`ff.castlemoat`) + **four-side warp gates**
(RuntimeRegionGate) + **tree aura/tower glow** (`ff.hubambientvfx`). **WO-560→584 arc:** UI Blink
master-frame template (`docs/UI_BLINK_TEMPLATE_CANON.md`, BINDING), **title rebrand WO-570** ("Echoes of
Elarion" / tagline "Hold the last light."), **WO-584 dungeon/outpost/arena consolidation** (one warp-in
space primitive, 3 skins, resolver + ownership flip — replaces flat ATB dungeon, `ff.atbdungeon` OFF),
wave-loop-in-hub. itch web build is LIVE; Vercel parked.

**IN-FLIGHT (carried from the 06-26 snapshot — re-confirm against HEAD before trusting):** the
hero-priority structure sweep (`ff.enemystructureaware`) was **UNVERIFIED** (0 sweep acquires) as of
`8aa24c32`; HEAD has since advanced ~30 commits — re-check its status before pushing. Verify any untracked
`.cs` triage state with `git status` rather than assuming.

**CANON STATE (the corrections that supersede everything older):** hero = **single Tripo self-rigged
Knight ("Grom")**, static armor, no mesh-swap — **Blink hero rig JUNKED 06-22** (Blink = UI re-skin only).
**ATB is flat/static**; animated combat lives in the **overworld BattleArena**. Base-defense/tower-defense
= **V2-gated** (`ff.basebuilding`). **Defend-the-Tower/PatriciaLight REMOVED 06-09.** Yarn being dropped
(WO-455). Home = `MainCastle_Hall`; `Village2` = raid target; `Village.unity` ABANDONED.

**QUEUED (captured, not built):** WO-509 functional N/E/W moat seams · WO-513 coordinated orc family ·
WO-514 tower cap + Population→Saved-Echoes→SP + siege-AI (mobs target towers) · WO-430 offline-garrison.

**CREATIVE FORK CLOSED (owner ruling, WO-520 / memory `canon-maintenance-wo520`):** the **living
world-Tree** is canon (NOT the Cathedral Spire) — STORYLINE/DESIGN-DECISIONS "Spire replaces the Tree" is
SUPERSEDED. See `CANON_READINESS_LEDGER_2026-06-26.md`.

**RESUME POINT:** finish the targeting proof on HEAD → triage the two untracked `.cs` → then WO-509/513/514.
Full doc-canon reconciliation = **WO-520** (`CANON_READINESS_LEDGER_2026-06-26.md`).

---

## ★ SESSION HANDOVER — 2026-06-19 (⚠ SUPERSEDED by the 2026-06-26 block above — kept for history) ★

**WHERE WE ARE:** A long session (architecture + core-loop fixes + an overnight autopilot run).
Owner is rebooting her PC (an OS/audio patch — her machine audio, NOT a game bug; she has working
Realtek endpoints, it was a default-output-device thing). When she's back she will do a **manual
playthrough to validate before we push.** NOTHING below is pushed.

**⛔ DO NOT PUSH** until the owner confirms her playthrough passes. 6 local commits await review:

| Commit | What | Verified? |
|---|---|---|
| `10282535` | core-loop: enemies fight (partial-path + chase fix) + clear→claim→companion + **interim** travel-tap | enemies/claim: logic-only; travel-tap is TEMPORARY |
| `fd9314af` | WO-449 "continuous walk" loop | ⚠️ **built on a FALSE premise — see below; NOT working** |
| `62a8bb88` | Blink rig migration (armor on the playable hero body) | compile-clean, **NOT felt-verified** |
| `3a3e4aeb` | armor fix (bodyless-hero swap + re-entrant Addressables release) | ✅ verified resolved (overnight) |
| `721da6c5` | autopilot stale-log wipe (harness truth fix) | ✅ verified |
| `14a70111` | TriggerWave probe-flake fix | ✅ verified resolved (overnight) |

**THE BIG CORRECTION (do not re-trip on this):** the WO-449 walk loop (`fd9314af`) was built on the
premise "OuterWorld is one continuous NavMesh you walk freely." **The overnight autopilot FALSIFIED
that.** Reality (RCA-confirmed, in `OVERNIGHT_AUTOPILOT_LOG.md`): MainCastle_Hall and OuterWorld are
baked **STACKED at the same origin** (the DUAL-NAVMESH error, 12/12 every run), and the castle→
OuterWorld crossing is a **WARP by design** (`SceneTransitionTrigger` disables→warps→re-enables the
agent). So a continuous castle→outpost walk **is not possible in the current layout**; the warp lands
the hero in the overlap (0,0.5,-12), far from the ±70 outpost anchors, and the outpost never realizes
headless → **zero outpost/combat/walk coverage**. This is the **WO-453** cluster. DO NOT auto-"fix"
the dual-navmesh/gate-island/warp — it's owner-led world-architecture work.

**WO-453 = THE NEXT BIG THING (design ratified, spec not yet written).** Full canon in memory
`world-architecture-gated-regions-playable-connectors.md`. In one breath: the world is **HYBRID
GATED REGIONS** — 2–4 navmesh-stitched low-poly scenes per region, sized by a **measured** memory/
frame budget (not a scene count), **seamless WITHIN** a region, with **NATURAL/DIEGETIC gates BETWEEN**
regions that are usually **playable connectors** (cave/tunnel/gatehouse) doubling as load-mask +
spatial bridge + content. Mobile-first consensus (even Genshin gates between regions). A **danger
gradient** soft-gates (tougher enemies toward the outward gate; "get stronger before venturing
further"). Loss = **Elden-Ring drop & recover**: die → drop unbanked XP/currency + unequipped loot
(NEVER equipped gear; keep claims), compass marker to the cache, recover before a 2nd death OR **pay
tribute** (Echo retrieves it; can't afford → harvest locally or risk the run; big cache = 2 Echoes).
Mobile guards: an interruption must not count as the 2nd death; respawn distance scaled, not a trudge.
**Owner's locked picks for Region 1:** gatehouse/portcullis gate · **wooded** first region · death =
harsh-but-recoverable (Elden-Ring style). FIRST STEP when resumed: write `WORK_ORDER_453` for Region 1
(prove ONE seam: castle→connector→wooded region→walk to a visible, guarded outpost, with a perf-budget
probe + chaos-fleet oracles), then replicate the convention per region. NOT a blind build — confirm
the seam approach matches the canon, then go.

**OPEN, DEFERRED TO OWNER (not blind-patched):** the dialogue `Stop()`-race (`No node has been
selected` + TMPro NRE, intermittent). Full RCA + two fix options in `OVERNIGHT_AUTOPILOT_LOG.md`
ledger. Yarn content is correct; the real fix touches the Yarn runner lifecycle → owner decides.

**THE OVERNIGHT AUTOPILOT RUN (done, terminated 06:46):** 13 cycles, 168 bot runs, fire-and-dormant
via a session cron (now deleted). It found+fixed 3 things (the 2 verified ✅ above + the harness wipe),
verified the armor fix, deferred the dialogue race, and proved a STABLE hub baseline. Coverage is
HUB-ONLY (WO-453 blocks the rest). The chaos design (seeded per-bot, fixed oracles) is canon —
`autopilot-chaos-not-one-scripted-path.md`. The loop self-validated cycle 1 by hand and caught a
harness bug (stale appended logs faking "fixed bugs reappearing") — the lesson: **validate the
harness before trusting its metrics.**

**HOW WE WORKED THIS SESSION (the behaviors that earned trust — keep doing them):**
- **Instrument, don't guess** (§12). When the walk/armor "didn't work," we traced + RCA'd from real
  capture data, not hypotheses. We split "shows nothing" into data-empty vs built-but-invisible vs
  threw-and-skipped *before* touching code.
- **Validate before claiming; verify before pushing.** The autopilot caught that the armor fix only
  *looked* unresolved (stale logs) and that the walk premise was wrong — before the owner wasted time.
- **RCA-gate every fix; defer the deep/risky.** Not everything gets an autonomous fix — the dialogue
  runner-lifecycle change was deferred rather than blind-edited at 1am.
- **Deliver complete + verified, not piecemeal** (memory `deliver-complete-verified-not-piecemeal`).
  "Rather be right than ran many times." Confirm the felt bug is gone before reporting done.
- **Read the embedded canon FIRST** (memory `read-embedded-canon-first-or-owner-pays`) — don't
  re-derive what's already in the catalog/docs/memories; the owner pays for rework.
- **Structural/creative forks are the owner's call** — name them explicitly (we used AskUserQuestion
  for the walk approach, rig migration, region fiction). When we guessed a structural direction
  without confirming (the original Travel-button), it was wrong and cost a redo.

**RESUME POINT (do this in order):**
1. Owner reboots → does her playthrough against the validation list (below in this block / I gave it
   in chat). She'll **F8-capture** any failure.
2. For each failure: RCA from the F8 break-log + screenshot (`break-log.jsonl`), fix, gate, commit by
   explicit path. **Push only the items she confirms pass.**
3. Then start **WO-453 Region 1** (write the spec first; confirm the seam approach vs canon; build the
   one proven seam + perf probe + oracle; replicate).

**THE VALIDATION LIST (what the owner is checking — test in the EDITOR, not the exe: Play mode
resolves Addressables via the asset DB so the Blink body/armor load without a content build):**
- ✅ **Hotkeys stripped** — only WASD/arrows, weapon skills, F8, F9 do anything (F1/F12/J/K/L/etc.
  dead). F9 green overlay is EXPECTED.
- ✅ **Armor on the playable hero** — where the hero is the real Blink body, it wears its class set
  (Knight=Centurion, Ranger=BeastHunter, Mage=Dragonic): no T-pose, not naked, not a personless
  mannequin; weapon+bow in the hands. *If the hub/start hero looks like the old placeholder, that's
  expected* — the Blink body builds in the gameplay context (HeroBodySwapper), not the hub.
- ✅ **Enemies fight** — they detect, chase, and land hits (no freezing at range, no parking ~1m short).
- ✅ **Reach a base via the interim Travel tap → clear → next companion joins + returned.**
- ⛔ **NOT yet (don't log as regressions):** the natural distance-gated walk / "see it coming" — WO-453.

---

## 1. HOW WE WORK — the orchestrator / CLI-gatekeeper model

Three roles (CLAUDE.md §2, §11):

- **UI (Claude):** writes work orders + specs, does the flow-first triage / RCA, makes creative
  calls, and writes `.cs` (Windows path, Write/Edit only — see §2 below).
- **CLI (this seat / lead):** the **sole committer + gatekeeper**. Owns batchmode (gates, bakes,
  builds), reconciles every session's diffs by explicit path, commits, pushes **only on owner OK**.
- **Owner (Samantha):** PM; final creative + sequencing decisions; runs the editor for felt/playtest.

The loop:

1. **Flow-first triage** — what *should* happen given the state ("is this state even expected?"),
   NOT culprit-hunting a stack trace. Ambiguous tickets (no repro/screen/stack) bounce back.
2. **Fan out agents** — each does ONE focused task. Read-only **diagnosis/verify** agents are
   gate-free → fan out many. **Edit-only** implementation agents run on **file-disjoint silos**
   (the §9 lanes; same-file work = one agent), told NOT to gate/commit.
3. **Batch-gate ONCE** — the orchestrator runs the compile gate over the combined tree
   (`COMPILE_GATE_OK`), then **commits each lane by explicit path** (never `git add -A`).
4. **Push only after** the owner retests/confirms (felt/gameplay) or a regression passes
   (data/logic) — "push the ones that passed."

**Notion is the live WO board** — *Defenders of the Realm — Pipelines* "Work Orders" DB
(data source `5f66b263-c732-4075-b94a-f5f4de9f8087`). Full WO spec files stay in the repo
(`WORK_ORDER_NNN_*.md`). WO-numbering authority = `MASTER_PIPELINES_BACKLOG_2026-06-06.md`, **not**
the filesystem max. Migrated off Linear; see `NOTION_SOURCE_OF_TRUTH.md`.

---

## 2. THE NON-NEGOTIABLE RULES (binding — condensed)

1. **UI never touches code; CLI writes ALL code.** (Owner 2026-06-13, binding.) The UI session does
   RCA / specs / narrative / screenshots / board grooming — it does NOT write or edit `.cs`. Only CLI
   writes code, on the **Windows path with Write/Edit only** — never `cat >`/`echo >>` via the §0 Linux
   mount (it does NOT sync reliably; redirects truncate/duplicate/interleave). If a file is broken on
   Windows, only CLI fixes it. The
   **NUL-byte gate now enforces this**: `CompileGate.Run` scans every `Assets/**/*.cs` for embedded
   NUL bytes and withholds `COMPILE_GATE_OK` if any are found (catches mount-garble that looks clean).
2. **§1 Quality gate on every `.cs` you touch** — brace balance + leak-scan (no stray
   `</content>`/`</invoke>` junk from agent Writes) + NUL-scan. **`DeNelle.Editor.CompileGate.Run`
   is the authoritative gate** — its `COMPILE_GATE_OK` marker is the only proof a tree compiles clean.
3. **Reconcile, don't replace.** WO specs predate the branch — treat as intent, add additively,
   never blind-replace a file.
4. **Stage by explicit path — never `git add -A`.** LFS-clean textures show as ~132-byte pointer
   diffs; a blanket add mass-converts them. Stage each path you reviewed.
5. **Never hand-edit `.unity` scenes.** `Village.unity` is corruption-cursed and ABANDONED
   (`Village2` is canonical). Rebuild via the builder (`VillageSceneBuilder.BuildVillage`,
   `CastleHubBuilder` — but do NOT regen the hand-dialed castle, it reverts owner offsets).
6. **One committer.** Two committers duel on `.git/index.lock` → stale locks + false "pushed."
   Other sessions write + signal "ready"; the one committer reconciles.
7. **Unity editor must be CLOSED for any batchmode gate/bake/build** — project lock otherwise.

---

## 3. RULES WE ADDED THIS SESSION (the new canon)

- **INSTRUMENT-FIRST debugging (CLAUDE.md §12, BINDING).** We do **not** guess at bugs — we
  instrument the flow and let the data say where it dies. Four `DeNelle.Core` helpers in
  `Assets/_Modules/Core/Diagnostics/`:
  - **`FlowTrace`** — `Step/Warn/Fail/Throttle/Once/Measure`, `[Flow:<system>]`-tagged. Trace flow
    entry, every branch *taken*, every fallback, service resolution, and the render/commit seam.
  - **`Guard`** — `Try`/`TryEach`; **one bad object must never blank a whole list/screen** (list
    population uses `Guard.TryEach`). Never compile-stripped (it changes control flow).
  - **`BreakCaptureHarness`** — F8 flight recorder → `break-log.jsonl` + screenshots.
  - **`DataRegression`** — headless "real object in → assert → one marker" gate.
  - **No silent failures:** a `catch` that swallows without logging is forbidden; every fallback is
    a `Warn`, every real failure a `Fail` (error-level → lands in the recorder). Method =
    `docs/INSTRUMENTATION_STANDARD.md`.
- **The AutoPilot bot / fleet.** A headless player bot (`Assets/_Modules/DevTools/AutoPilot*`,
  `Assets/Editor/AutoPilot/`) drives the game and emits ranked tickets. The **player .exe needs no
  Unity license**, so `run-autopilot-fleet.ps1 -Count N` runs dozens of instances in parallel (each
  a distinct `--seed`/`--run`); `AutoPilotTickets.Emit` dedupes + ranks by how many runs reproduced
  each break. `-nographics` → logic/flow/crash coverage only (UITK picking won't resolve headless).
- **Confirm-to-cross seam + WarpTo.** Two-scene navmeshes don't auto-connect. `SceneTransitionTrigger`
  disables → warps → re-enables the hero's `NavMeshAgent` across the seam. Debug "can't cross/exit"
  as a **navmesh bake** issue, not colliders. The hero returns to a **return-point** (`ReturnScene`
  in `BattleParams` for combat; the seam warp for world crossings).
- **Hero tag = `Player` (one tag, now declared).** Locomotion/camera/HUD/triggers all
  `FindWithTag("Player")` (set in `HeroControlEnsurer.Ensure`). **Enemy AI finds the hero by
  COMPONENT** (`FindFirstObjectByType<HeroLocomotion>()`), NOT a `HeroTarget` tag — that tag was
  never declared and a GameObject has only one tag (CLAUDE.md §7).
- **Vendor-stock contract.** `Assets/_Modules/Village/Hero/VendorStockContract.cs` is the single
  source of truth for what each store TYPE sells (armorer=armor, etc.). Two consumers read the same
  `AllowedFor()` mapping: `PartyShopVM` (`:248`) filters stock; the AutoPilot bot asserts the built
  stock matches — so the bot checks intent, not a duplicate. *(Re-pointed 2026-09-06, WO-1430: the
  first consumer used to be the legacy `ShopPanel.ShowBuy`; that panel was deleted as doorless.)*
- **Seam radius / nav lesson.** The seam is a **proximity** trigger; the hero (a `NavMeshAgent`)
  stops at the **navmesh edge**, so the trigger radius must overlap the walkable surface or the hero
  never reaches it. Tune the seam against the bake, not the visual mesh.
- **Pet-from-shop flow.** Pets are acquired through the shop flow (not only PetSelect onboarding) —
  trace via `[Flow:*]` if a purchased pet doesn't appear.
- **OnboardingPanelGuard.** The "dev tools / UI dead after Yarn" bug: a UIDocument backed by the
  shared `OnboardingPanelSettings` leaked into a gameplay scene and its raycaster sat on top of the
  click stack, eating every click. `Assets/_Modules/Onboarding/OnboardingPanelGuard.cs` enforces the
  invariant (that panel may only intercept input in Title/HeroSelect/PetSelect) on every scene load.
  **Fixed.**

---

## 4. THE BUILD / GATE / BAKE CYCLE

All batchmode runs through `run-unity-method.ps1` (handles the relaunch-fork quirk — poll for the
exe/marker, not the wrapper exit code; the 505 license line is transient/non-fatal). **Editor must
be closed.**

Player-artifact callers must name the success marker they expect. Android APK callers pass
`-ExpectMarker '[AndroidBuild] SUCCEEDED'`; `PASS-UNASSERTED` is not a shippable verdict.

| Task | Invocation |
|---|---|
| **Compile gate (authoritative)** | `run-unity-method.ps1 -Method DeNelle.Editor.CompileGate.Run` → `COMPILE_GATE_OK` (brace + leak + NUL scan) |
| **Data/logic regression** | `run-unity-method.ps1 -Method DeNelle.Editor.DataRegression.RunAll -LogName data-regression.log` → `REGRESSION_OK` / `REGRESSION_FAIL` |
| **Castle rebake** | `BatchRebuildCastleFromRecipeAndBake` (do NOT regen the hand-dialed hub geometry) |
| **Outpost wiring** | `BatchWireOutpostsAndSave` |
| **Village rebuild** | `DeNelle.Editor.VillageSceneBuilder.BuildVillage` (never hand-edit the scene) |
| **Windows player build** | `build-windows.ps1` |
| **AutoPilot fleet** | `run-autopilot-fleet.ps1 -Count N` (player exe; no license needed) |
| **WebGL ship** | `ship-webgl.ps1` / `build-webgl-isolated.ps1 -Ship` → butler → itch |
| **Content ship (R2 CDN)** | `tools\r2-ship.ps1` → `R2_PUSH_OK` + `R2_PARITY_OK` — **mandatory on every build that reaches a device or a store; full rule = CLAUDE.md §16** |

- **F8 break-logs land in `break-log.jsonl`** (+ screenshots) via `BreakCaptureHarness`; fleet runs
  namespace theirs per `--run`. `Fail`/`LogError` lines are what the recorder captures.
- **exe-stub quirk (load-bearing):** incremental player builds skip re-emitting the exe stub → stale
  exe vs fresh scenes → `level3 corrupted` native crash. **ALWAYS delete `Builds/Windows` before
  `build-windows.ps1`.** Also: build via the Defenders→Build menu / `build-windows.ps1`, NOT the
  Build Profile "Build" button (it skips the Static-Batching-off mitigation).

---

## 5. CURRENT STATE + RESUME POINTS

**Playable loop:** Title → HeroSelect → PetSelect → `MainCastle_Hall` (home hub) with `OuterWorld`
streaming additively; south-gate seam → OuterWorld; raids via `RaidOutpostSystem` (4 in-world
outposts, ~10s delay) and additive `Garrison_*` scenes; `Village2` = TD raid target; ATB battles
return to `ReturnScene`. Store ~70% built (do NOT greenfield — `PackStore` exists; scene-wiring
disabled pending its own PanelSettings). Build mode wired end-to-end for towers (~70%).

**Recently fixed this session:**
- Dev-tools-dead-after-Yarn → `OnboardingPanelGuard` (§3).
- Archer/blast tower behavior — fixed.
- Vendor stock leakage (armorer selling weapons/potions) → `VendorStockContract` (§3).
- Raid outpost never found — 3-min spawn delay cut to 10s.

**Known-open / watch:**
- **South-gate ~34m nav reach** — verify the seam trigger radius overlaps the walkable navmesh
  (the hero stops at the navmesh edge; §3 seam lesson). Test in Play/build, not batchmode
  (`NavMesh.SamplePosition` fakes a complete path in headless).
- Remaining AutoPilot audit findings — work the ranked tickets from the latest fleet run.
- Cross-zone *AI* pathing across the seam is deferred (off-mesh links when raids walk between zones).

**Pointers:** `docs/ARCHITECTURE.md` (architecture hub) · `docs/MASTER_CATALOG.md` (verified-from-code
SME catalog) · `docs/INSTRUMENTATION_STANDARD.md` (the §12 method) · `docs/MODEL_CATALOG.md` +
`docs/polyperfect-asset-catalog.md` / `docs/kaykit-asset-catalog.md` (check before referencing a
prefab) · Notion "Work Orders" DB (live board) · `PIPELINE_STATE.md` (full pipeline detail).

---

*Maintenance: keep §3 and §5 current as the canon and the loop move. This sheet is the entry point —
depth stays in the deep-dives it points to.*
> **2026-09-09 LIVE TESTER FIX SET (WO-1612–1614):** All implemented findings are in the board's
> Fixed bucket for next-build owner verification. Wave-20 dragon RCA is definitive: the live log
> reached StartWave(20), then Addressables rejected `LoadEnemyAsset<DragonBoss>` because the published
> address is a `GameObject`. WaveManager now loads the prefab correctly, holds clear while pending,
> and fields progressively harder dragons at 20/25/30/... (HP +25%, damage +12%, attack gaps -5% per
> return, gap floor 65%). Mage cast RCA is also closed: a raw generic trigger raced the variant and
> VFX followed the hotbar seat; the generated Mage controller and ability data now give 13 non-primary
> spells distinct cast identities/VFX. WO-1612 lists the rest of the gated live fixes. The “stuck
> battle” capture was a real active Wave 23 with live enemies; F8 intentionally freezes time.
