# From raids to a player-built competitive arena

Architecture proposal, 2026-09-10. Source investigation against root HEAD `c8326f721` during an active integration session. This report changes no code, ticket status, scene, or economy. Proposed gates below have **not** been run; calculations are explicitly separated from measured gameplay results.

## Owner-established journey

**Chapter One: save the castle → raids → capture your own base → design everything CoC style → battle in the arena with your build against AI or a player.**

These are the owner's latest explicit rulings, not inferred milestones. Owning and rebuilding a personal base must not become a prerequisite to the raids that earn it. CoC style establishes player layout, defenses, and progression; it does not prescribe another game's assets or every mechanic. The existing Chapter One castle remains the story location. A captured personal base is a subsequent progression reward.

**Capture model subsequently agreed by the owner:** take over the base as it stands, including damaged buildings; repair, rearrange and develop it; receive a modest one-time capture resource grant covering essential repairs; earn stronger defenses, expansion and upgrades through play. This supersedes a blank-plot or pristine starter-base interpretation. Grant amounts, which damage states can be repaired, and exact repair pacing are still design work, not approved numbers. The owner also explicitly requests playable FTUE for this transition.

The remaining format choice is what participates in an arena match. My recommendation is **the whole authored build**: a legal base layout plus stationed defenses, and a chosen hero/attack squad. The first arena format should be a **paired assault**: each side attacks the other's frozen build; compare objective completion, destruction, then time under one versioned ruleset. AI and players supply the same build contract. AI controls its attack through the same command interface as a player. A base-only defense test and a single practice assault are useful views of this same system.

This preserves both halves of the owner's investment: how the base is designed and how the army is used. A hero-only duel would omit the central base-building payoff.

## Recommendation and alternatives

Build the capture/design loop first, then ship the shared arena against AI. Add asynchronous player-build pairings using the same match authority and rules. Both players need not be online simultaneously; each plays their hero-led attack against the other's AI-controlled defenses. Add simultaneous live play after this arena can reliably validate builds, run combat, recover results, and measure balance.

| Format | Reuse and cost of change | Recommendation |
|---|---|---|
| AI paired assault / defense practice using the player's build | Existing placement, defense points, hero, squads, targetable structures and generated opponents are directly relevant. Requires a proper match boundary and scoring contract. | First arena experience after capture/design. |
| Asynchronous paired player-base assault | Adds certified immutable defense revisions, invitations/matching, two-leg results and server combat. Requires only one human command stream per leg. | First competitive player mode; best bridge from this journey. |
| Simultaneous build-versus-build arena | Adds two live command streams, side-specific heroes, replication, latency handling, disconnect adjudication and substantially more concurrency sensitivity. | Subsequent format on the same contracts; no second economy or build editor. |
| Hero-only live duel | Smaller battlefield but bypasses bases and stationed defenses; not the owner's stated payoff. | Optional much later, not the main path. |
| Client simulation followed by submitted score / peer-hosted rewarded match | Low apparent implementation effort; participant can fabricate results or act as judge. | Unsuitable for ranked or rewarded competition. |
| New fixed-point simulator before completing raids | Can enable cheap verifiable replay eventually, but replacing current spatial combat, navigation and timing is substantial work. | A measured option, not a prerequisite or wholesale rewrite mandate. |

Paired assault and equal-budget ranked rules are architectural recommendations, not new owner rulings. If the owner prefers direct simultaneous invasion, retain the capture/build and authority milestones and move the live format forward; do not substitute a duel silently.

## What the repository actually provides

Source paths below are repository-relative. Line anchors are a snapshot, not an API guarantee.

| Existing surface | Evidence | Reuse / limitation |
|---|---|---|
| Hero-led PvE raids | `docs/RAID_NORTHSTAR.md` August 2 posture ruling; `RaidDeployController`, `TroopController`, `RaidScoring`, `RaidVictoryController` under `Assets/_Modules/Village/` | Real movement, deployment, AI, destruction, outcome presentation and return flow. The old spectator-only wording is superseded. |
| Base claim | `World/Camps/RaidVictoryController.cs:808–825`: `MarkClaimed(configId)`, `SceneOwnership.SetEnemyOwned(false)`, save. `RaidClaimService.cs:499–526`: local PlayerPrefs claim flag | Useful narrative trigger, **not** a server-owned property record or independently editable base instance. ReturnHome still routes to `SceneRouter.GoCastle()` at 972–981. |
| Layout persistence and reconstruction | `Core/State/GameState.cs:287` `BaseLayout`; `PlacedStructureData.cs`; `Village/BuildMode/BaseLayoutLoader.cs`, `StructureFactory`, `PlacementGrid`, `BuildModeController` | Reuse catalog IDs, cells, yaw, level, free yaw/height/wall-mount fields, placement and reconstruction. Current single BaseLayout is not a collection of separately owned properties. Do not replace it accidentally with the captured raid. |
| Pre-placed arena defenses | `GameState.cs:357` `ArenaDefense`; `ArenaDefenseSetupController`, `ArenaDefenseCatalog` | Already separate from buildings, uses placement/grid and a point budget. Reconcile this with the base contract; avoid a third defense editor. |
| Attacking squad | `ArenaAttackRecruitController.cs` and `ArenaMode.AttackSquad` | Existing point-budget recruitment. The static MVP squad is not a durable, match-locked roster. |
| Existing “Arena” prototype | `Village/Arena/ArenaMode.cs:6–24`, `GetDefenderRecipe`; `ArenaCatalog` | Seeded opponents, local resolution, and “Use My Castle” practice. These names do **not** establish real matchmaking. The own-castle branch reads local BaseLayout. |
| Public base publication | `Core/Social/PublicTownSnapshot.cs`; `api/showcase/publish.js`; `_lib/town-showcase.js` | Versioned immutable public layout records and allowlist DTOs are valuable precedents. Publish verifies requested cosmetic/achievement ownership, but layout/army levels and counts are not certified competitive power. Social publication is not consent to being raided. |
| Account/backend foundation | `api/_lib/wallet-auth.js`, `auth/*`, `game/save.js`, `purchases/*`, schema/migrations | Reuse account identity, sessions, validation, database migrations, audit and fulfillment conventions. Guest bearer identity has deliberately weaker trust; granting authentication is a distinct seam. |
| Current score ingestion | `api/leaderboard/submit.js:85–96, 134–151` accepts `Number(body.score)` and stores `GREATEST` | Authentication proves who submitted, not whether they won. A monotonic merge is not anti-cheat. Competitive standings must not consume this endpoint's client scores. |
| Current save/rewards | `api/game/save.js` merges guarded client deltas; `RaidClaimService` uses local claim/day/cache flags; `RaidVictoryController` grants loot locally | Existing bounds and accrual guards are useful safeguards, not an authoritative competitive ledger. PvP rewards must be isolated from overwriteable save balances. |
| Current wager rail | `ArenaWalletService.cs:6–23` | SKR branch is a seeded PlayerPrefs stub; Play crystals use local GameState mutation. Neither is competitive escrow. Preserve current behavior while keeping the new proof slice free of wagers. |
| Runtime networking | `Packages/manifest.json` includes Multiplayer Center; targeted `_Modules` search found no Netcode/NetworkBehaviour/RPC implementation | An editor package is not a connected authoritative match implementation. Transport/server boot must be proven, not assumed already integrated. |

The governing architecture documents are `docs/ARCHITECTURE.md`, `ARCHITECTURE_PRINCIPLES.md`, `ARCHITECTURE_NORTH_STAR.md`, the relevant `MASTER_CATALOG` areas, `RAID_NORTHSTAR.md`, and `PROGRAM_RAID_ECONOMY_2026-09-04.md`. Keep bounded contexts, Core contracts, Village gameplay and HUD presentation separation. Old `build-mode-architecture.md` claims such as “no Arena code exists” are historical, contradicted by current source. The new owner sequence governs ordering; old planning prose does not reorder it.

## Capture is the first missing product contract

Introduce a durable **OwnedBase** record, initially one personal base per account: opaque base ID, owner identity, source raid/template version, capture receipt, plot rules, active layout revision and progression state. Keep the story castle separate. More properties can become a collection later without loading them all.

On an eligible raid victory, offer “Claim this base,” create the ownership record idempotently, and materialize the inherited layout into editable recipes **with its settled structural damage**. Capture combines the versioned template with validated battle outcome state, not an unvalidated client graph or copied Unity scene objects/enemy scripts. Give inherited structures stable instance IDs and persist condition independently from their layout poses and upgrade levels. Designer-authored scenic geometry remains scenery; editable wall/tower/building entries are explicit catalog records. Reentering the base reconstructs the same owned revision and damage, with its correct permission context. Decide whether fully destroyed objects become repairable ruins or empty plots: the current repair/rebuild and producer-recovery exceptions must be reconciled explicitly, not erased.

The smallest slice is one eligible base, one starter template, one owner, and move/rotate/place/save/reenter. Existing broad build features need an audit against this captured plot, not another implementation. Placement validation must include legal catalog entries, footprints, plot bounds, gate clearance, reachable objective and stationed-defense budgets. Arbitrary `worldY`, yaw offsets and wall mounts need certified support surfaces rather than trusting public snapshot coordinates.

Migration must be additive and reversible: retain the current story BaseLayout, claim flags, ArenaDefense and balances; create a separate OwnedBase revision and a recorded mapping from inherited structure IDs to editable instances. Backfill existing players only after an explicit eligible capture/adoption decision. A veteran's historical claim is not permission to overwrite their current town or grant the same supplies repeatedly. Snapshot the source record before migration, test save round-trip and fallback to the untouched original when conversion cannot complete, and never silently delete unconvertible owner-placed content.

## Capture affordability without flattening progression

The accepted one-time repair grant is preferable to a global cost reduction: it targets the moment where the player owns a damaged base but cannot get its income running, while preserving later construction/upgrade value. Starter-tier reductions would benefit repeat construction everywhere and need a wider balance pass. Do not apply either alternative as an unreviewed global multiplier.

Current catalog math provides useful bounds, **not a chosen grant**. `Assets/Resources/Data/Canonical/structures-catalog.json` `repo.cost` holds multi-resource baskets; the legacy scalar `buildCost` is not consistently their sum (barracks, for example, has scalar 480 beside 600 wood + 320 iron). Use the actual runtime quote resolver before shipping prices, never the scalar or a prose estimate. `WallRepairController.CostForFraction:625` applies ceil(component × damage fraction), normalizes fully destroyed state to full cost, applies the existing repair talent discount, and leaves ordinary crystal cost zero. Optional crystal top-up is a separate existing rule and should not fund the essential capture loop.

| Candidate inherited essential | Current catalog basket: wood / iron / internal food slot | Illustrative 25% repair, no discount |
|---|---|---|
| `collector_lumbermill` | 160 / 120 / 80 | 40 / 30 / 20 |
| `armorer` (also opens Iron harvest) | 240 / 280 / 0 | 60 / 70 / 0 |
| `lumberyard` | 800 / 320 / 0 | 200 / 80 / 0 |
| `foundry` | 960 / 480 / 0 | 240 / 120 / 0 |
| `tower_ballista` | 240 / 400 / 0 | 60 / 100 / 0 |
| **Illustrative five-entry basket** | **2,400 / 1,600 / 80** | **600 / 400 / 20** |

This example assumes those five structures actually exist and are each 25% damaged; it is **not** a starter layout mandate or the approved X. At 50% damage the same basket is 1,200 / 800 / 40. A barracks would add 150 wood + 80 iron at 25%; each `wall_wood` segment adds 20 wood. The internal `food` resource is player-facing Stone on the current progression mapping; use the existing resource-label reader in UI. Do not include both paired iron producers as separate prerequisites merely because two catalog IDs exist.

The baseline L1 production definitions in `ResourceBuildingProgression.Build` and `IntervalForLevel` are wood 10, iron 6, and Stone 13 per 50 seconds: 720 wood, 432 iron and 936 Stone per hour at x1 modifiers and uninterrupted collection. Thus the illustrative 25% basket costs about 50 minutes of wood and 55.6 minutes of iron income after those faucets are running. It is impossible to earn that income from a producer that cannot operate until repaired; that is exactly where the grant belongs. These are calculated baseline scenarios, **not measured session incomes**: Echo/talent modifiers, collection caps, existing stores, repair-condition effects and offline rules must be included in a gameplay measurement.

`BuildTimerConfig` defaults currently read 45 seconds base, 3.2 tier growth, 1.25 upgrade multiplier, 24-hour ceiling. These are code defaults, not proof of a loaded asset or live override, and must not be treated as repair durations. Measure the effective quote/time for each inherited repair, first defense improvement and later upgrade through the real controller. Training cost prose in older documents is also stale: `PROGRAM_RAID_ECONOMY_2026-09-04.md` explicitly records the later time-only troop-training ruling.

Size the grant from an **authored essential repair envelope** for the eligible captured template: restore a working income chain, enough usable storage, a functional defense/entrance and the repair FTUE choice. Account for existing supplies and guaranteed capture payout in simulations, but avoid a means-test that punishes a player for saving resources. Prefer a predictable template-specific supply award over an amount inflated by client-reported damage or last-second spending. Damage envelope and grant together must make at least one viable recovery sequence affordable at zero carried resources. The player chooses repair order and layout; additional upgrades remain earned.

Store grant entitlement with the capture transaction and pay through an idempotent receipt. Scope uniqueness to the intended progression award/account, not an easily recreated scene instance; abandoning, recapturing, replaying victory, resetting or changing devices must not mint another grant. If storage is insufficient, retain the entitlement in a durable claimable supply reserve instead of discarding resources or creating a second untracked currency. This requires server settlement before competitive rewards, and a carefully migrated local story equivalent if capture ships earlier.

Pacing acceptance: a zero-resource eligible player can restore an income path in the initial capture session; an early affordable improvement has a visible defense payoff; the next meaningful objective is reachable with the measured expected return interval; later tiers still require sustained play. Collect median and p90 time-to-first-repair, first income, first chosen improvement and return completion across zero/typical/wealthy inventory scenarios. Propose exact targets after these measurements rather than inventing X or promising an arbitrary number of minutes.

## Playable FTUE for ownership, repair and the arena

Extend the existing `Village/Tutorial/V2/TutorialFlow`, `TutorialSignalAdapters`, `Core/Tutorial/TutorialSignals`, authored `Resources/Data/Canonical/tutorial/tutorial-steps.json`, and `GameState.SeenTutorials` persistence. `PostRaidBeatTokens` already resolves real army/progression numbers for post-raid dialogue; use that pattern for actual damaged count, repair quote and supplies remaining. `Village/Quests/StoryQuestSignalBridge` is the quest integration seam. Audit the active flow/feature flags before extending it; do not create another tutorial runner or revive UXML gameplay surfaces.

Recommended in-play sequence:

1. On durable capture, reveal **“This base is yours.”** Show the inherited base and damaged structures, with the one-time supplies receipt; keep the camera in the place the player earned.
2. Highlight a choice of affordable essential repairs. Selecting one opens the real quote. Completion requires the actual repair transaction/condition change, not dismissing dialogue or tapping a highlighted button.
3. Invite the player to move a defense or choose a useful improvement. Save a real layout revision; permit equivalent valid choices and let experienced players skip guidance. Do not dictate the final base design.
4. Run a short AI defense test using that exact saved build. Show the route enemies took, what held and what failed; connect the result to one repair/placement choice. Practice should not destroy the newly restored base or consume premium resources.
5. Offer one clear next earned objective, then the shared arena's AI practice and later certified player contest readiness. Do not present locked player competition as immediately available.

Persist versioned milestones keyed to ownedBaseId: capture acknowledged, supplies available/claimed, essential repair completed, layout choice saved, AI test completed, next objective acknowledged. These are observation receipts; economy authority remains in capture/repair services. On restart, derive the next valid step from current state and transaction receipts: never repeat a grant, demand moving an already-correct defense, or strand a player whose chosen building no longer exists. Separate “guidance dismissed” from actual gameplay prerequisites and handle actions completed before the prompt appears. Returning players resume at the next useful contextual beat; they do not repeat Chapter One.

Use current contextual world highlights/brief dialogue and HUD objective presentation, not a sequence of blocking explanatory modals. Required gameplay actions remain available after skipping guidance. Emit privacy-minimal events for step offered, accepted, action completed, skipped, resumed, affordability refusal and exit; include tutorial version/base template/ruleset and duration, not raw saves or account addresses. Extend the existing TutorialStepReachability, TutorialCompletionPublisher, anchor/watchdog regressions with interruption at every step, alternate valid choices, early completion, insufficient supplies, storage-full claim and repeated capture. Device gates must prove the player can repair, move and test via the actual touch controls.

For local story progression, do not demand a server rewrite of every existing raid first. Before a captured base enters ranked play, enroll it in the server-owned competitive progression path. Legacy claims/layouts may import into practice; do not silently certify client-stated ownership or power. Ranked normalization can certify a legal budgeted build without trusting legacy resource wealth. If progression power itself is ranked, its acquisition must become authoritative too.

## One arena contract, multiple opponent sources

Compose a versioned **ArenaBuild** from existing data: OwnedBase identity/revision, canonical building/defense entries, hero loadout, attack roster, cosmetic projection and ruleset hash. Preserve BaseLayout and ArenaDefense semantics through adapters during migration; do not double-count a defense represented in both. Snapshot recipes reference stable IDs, not prefab GUIDs sent by clients.

Opponent providers should return the same certified contract: authored AI build, the player's own practice build, invited player's build, or matched player's build. Combat never needs to know which discovery surface supplied it. Pin each match to exact catalog/balance/map/build versions; publishing a new revision only affects later matches.

Use two explicit layers:

1. **Control API/database:** authenticate, own bases, validate/publish builds, reserve participants/revisions, allocate a worker, maintain match status, settle results, update standings, issue receipts.
2. **Authoritative battle worker:** instantiate bounded combat data, accept commands, own time/movement/targets/damage/cooldowns/AI/destruction, emit state/events and final outcome. Client presentation consumes these; it never writes the winner.

Keep Vercel endpoints for the first layer. Vercel documents that Functions cannot act as WebSocket servers, so persistent battle connections require a separate worker/service. No hosting vendor or price is selected here. [Vercel WebSocket guidance](https://vercel.com/kb/guide/do-vercel-serverless-functions-support-websocket-connections)

The lowest-risk experiment is a dedicated Unity battle build that reuses existing combat, with rendering/HUD/account-singleton dependencies removed or adapted at a match boundary. Unity supports a Dedicated Server build target; that does not prove this project's scripts are server-ready. Initially run **one battle per process**: static player input, global ownership and singleton helpers make multiple matches in one process unsafe until explicitly isolated. [Unity Dedicated Server build](https://docs.unity3d.com/6000.0/Documentation/Manual/dedicated-server-build.html)

## Determinism and replay: evidence before promises

`RaidDeployLog` records troop ID, elapsed float time and X/Z only. It omits hero input, ability choices, target changes, random state, damage events and balance versions. It cannot verify this hero-led combat. `TroopController.Update` consumes `Time.deltaTime`, uses physics overlaps and NavMesh paths; `HeroAbilities` also consumes Unity time and physics. `HeroLocomotion` exposes static scripted input, and arena code resolves the single Player-tagged hero. These are concrete extraction/multi-actor risks.

The dormant `BattleATB/Engine/Rng.cs` provides a seeded mulberry32 port and golden-vector tests. Reuse the testing discipline and seed interface where appropriate; its turn-based engine is not a deterministic version of the live spatial raid. A seed and fixed tick alone do not establish deterministic replay.

For the recommended route, the server runs the battle live and decides the result. It records accepted sequence-numbered commands, ticks, ruleset/build hashes, state checkpoints and outcome events. Playback uses authoritative state/events initially. Byte-identical resimulation is an optional later verification/optimization gate, not required for server authority. This is an explicit refinement of the older architecture northstar's deterministic-sim recommendation, not a claim that recommendation has already been satisfied.

If worker cost or offline submission requirements justify deterministic replay later, first spike a tiny rules slice across target server builds and machines. Require identical per-tick hashes over a declared repeat corpus, stable entity ordering and explicit RNG/clock/navigation contracts. Preserve the known-good live worker until equivalence is proven. Never accept client outcomes while waiting for that work.

## Match lifecycle, cheating and recovery

Proposed durable states: `Created → Reserved → Loading → Ready → Running → Settling → Completed`; explicit terminal `Cancelled`, `Forfeited`, `Expired` and `InfrastructureAborted`. A paired contest owns two independently recorded assault legs and settles only under its published completion/expiry policy.

- Match creation uses an idempotency key. The server verifies participant, ruleset and frozen build revisions, and reserves one active ranked match per account. Reservations have expiry and compare-and-set transitions; two workers cannot claim the same lease.
- Short-lived match tickets bind account, match, side, build hash and expiry. Commands carry monotonic sequence numbers; reject duplicates, impossible rates, foreign entities, illegal deploy positions, unaffordable casts, invalid targets and client-authored damage/results.
- Server clock determines start, timeout and disconnect grace. A reconnect returns the same match and a fresh state snapshot; it never resets cooldown, HP, ammunition or the opponent. A second device cannot fork the match.
- For asynchronous pairings, lock both defensive revisions before either leg starts. Do not expose the first leg's detailed replay until the contest completes; avoid informing the second attacker how to optimize against a known score. Expiry and unanswered legs need an explicit forfeit rule.
- Infrastructure failure is distinct from a player disconnect. Start with an audited no-reward/no-rating infrastructure abort. Do not promise worker crash recovery until snapshots restore all gameplay state. A disconnected attacker cannot keep resubmitting a losing attempt until it wins.
- A trusted worker submits settlement with match/lease identity. One database transaction records terminal outcome, unique reward ledger entries and rating changes; retry returns the same receipt. Use a unique `(matchId, participantId, rewardKind)` boundary. Client result screens poll receipts; closing the screen cannot lose or duplicate a grant.
- Never route ranked settlement through the current client score submitter or local ArenaWalletService. Separate competitive balances/entitlements from ordinary cloud-save merges. Purchased/cosmetic ownership checks do not certify combat stats.
- Limit repeat-opponent rewards, self-matches, multi-account farming and intentional loss rings. Keep identity and sparse audit evidence private; public IDs and replay redaction follow the existing snapshot privacy pattern. Device attestation can be supplemental, never the result authority.

## Fairness, loadouts and PvP balance

For the first ranked ruleset, recommend equal deployment/defense budgets and normalized effective levels, with unlocked choices and cosmetics expressing progression. Keep a progression-strength practice/unranked category if desired. Do not mix the two in one rating pool. Server normalization derives every effective stat from a pinned catalog; ignore supplied attack/HP/speed values, impossible levels, unsupported gear combinations and forged inventory counts.

Base layout remains meaningful: legal pathing, placement, range, cover and troop composition still determine outcomes under equal budgets. Freeze layout, roster, hero kit, consumables and balance version at reservation. Initially use no paid combat advantages, external wager, or consumed town army in arena practice; competitive depletion/repair would be a separate economy decision. Existing PvE loot curves stay unchanged.

Paired scoring should compare discrete objective outcome first, then validated destruction, then remaining time; publish tie handling before rating is enabled. Derive from existing RaidScoring concepts, but do not copy its PvE honor/reward rules blindly. Test offense/defense asymmetry, healer stalls, invulnerable objective layouts, wall-maze timeouts, ranged kiting, area attacks through walls, deployment exploits and spawn camping. Add classes and abilities only after their PvP variants pass balance and targeting tests.

## Matching, concurrency and operating cost

Begin with explicit AI/practice selection and invitation codes. Asynchronous player matching needs opted-in certified builds but does not require simultaneous population. Match within ruleset/content compatibility first, then normalized budget band and rating; avoid immediate repeats and widen rating tolerance gradually. Do not silently substitute bots in a player-ranked queue.

Live queues later add region/latency and readiness checks. Do not fragment a small population across many maps, currencies, team sizes and ranked modes. Browser and native transport compatibility must be an early test: Unity Transport documents browser WebSocket use in place of ordinary UDP. Selecting that transport requires a project-version/platform spike, not assuming the editor package provides it. [Unity Transport WebGL support](https://docs.unity3d.com/Packages/com.unity.transport@2.3/manual/websockets.html)

No credible monthly price or supported-player count can be inferred from the repo. Measure worker boot time, peak resident memory, p95/p99 simulation tick duration, outbound bytes/player-second, active battle duration and idle capacity. Model active legs as `leg arrivals/second × mean leg duration`; paired contests require two legs. Capacity is constrained by measured CPU, memory and bandwidth, with explicit headroom. Cost includes worker-hours, idle reserve, networking, persistent storage/replay retention and control API/database load. Benchmark one-match-per-process before optimizing tenancy. Obtain actual quotes only after these measurements and expected concurrency are agreed.

## Milestones and exit gates

The numbers here are proposed acceptance targets, not measured capabilities or owner promises.

| Stage | Concrete delivery | Exit gate |
|---|---|---|
| 0. Finish the raid prerequisite | Readable controls, continuous intended walls/gates/ground, reachable objectives, reliable win/lose/return | Owner verifies current READY fixes on device; automated raid scoring/path/cleanup checks pass. Do not make PvP a diversion from raid completion. |
| 1. Capture and own | One eligible raid captures one durable damaged base; editable inherited layout; one-time essential repair supplies; own-base reentry | Capture retry creates exactly one base/grant; restart/relogin/two-device tests retain ownership, damage and layout; story castle remains intact; cross-account edit refused. |
| 2. Design and certify | Existing build editor + defense setup compose one ArenaBuild; practice reconstruction | Every accepted layout reconstructs exact IDs/poses/budgets; illegal footprint/height/mount/gate/ownership rejected; objective reachable. Save/reload and edit-versus-published revision tests pass. |
| 3. Small AI arena slice | One build and one AI build, one hero kit, one troop type, one tower, walls/gate/objective, one short assault on authoritative worker | Device input drives server combat; tampered damage/score rejected; no local wallet payout; authoritative receipt survives reconnect/retry. Capture and compare actual phone controls and server/client states. |
| 4. Complete AI paired contest | Attack and defend legs, same commands/rules and build contract | At least 100 automated contests cover both sides, death/timeout/tie/pathological layouts; zero duplicate settlements or orphan leases; outcome explanation matches authoritative event log. |
| 5. Invited asynchronous player contest | Two certified builds, frozen revisions, two legs, expiry and reconnect | Two real accounts/devices complete both legs; race/replay/forfeit/worker-failure tests pass; external save edits cannot affect frozen matches. No rating/rewards until this gate. |
| 6. Ranked player-build arena | Matching, season rules, trusted standings and grant ledger | Published fairness policy; adversarial settlement/load tests; measured peak-load headroom; support can reconstruct disputed results. First use modest non-wager rewards. |
| 7. Simultaneous live format | Two live commanders/heroes using their builds under same contracts | Per-actor input/ownership isolation; client prediction and reconciliation; latency/loss/jitter tests on required devices; reconnect/forfeit policy demonstrated; no host advantage from authority. |

For networking experiments, test at least 50/100/200 ms RTT, jitter and 1–5% loss, plus background/resume and a brief connection outage. These are a test matrix, not a launch latency guarantee. Set the acceptable input feel and frame/tick budgets from measured Seeker/Play/browser results before opening ranked queues.

## Smallest first implementation and bounded ownership

The immediate vertical slice is **win one raid → inherit the damaged base and its one-time repair supplies → repair one essential building → choose/move one defense → save/reenter → test that exact build against AI → view the result and next earned objective**. The connected arena proof then runs the same contract on its authoritative worker. This is a demonstration slice of the whole promised journey, not a new large-map feature.

Split it into independently reviewable lanes: (A) Core ownership/build DTOs and migrations, (B) adapters over existing capture/layout/defense flows, (C) API certification/match/settlement tests, (D) isolated battle worker/input/state adapters, (E) UI entry/result views and device acceptance. Parent CLI owns integration, Unity and builder serialization. Assign file fences before editing; no parallel raids/world bakes. Maintain existing PvE entry paths while the arena feature is gated.

The capture/damage/repair-supplies model is agreed. Remaining recommended implementation choices are one captured base initially; catalog-priced essential damage/grant envelope; paired assault as the shared AI/player format; whole build = layout + stationed defense + hero/squad; equal-budget ranked mode; asynchronous player mode first; no wager in the proof slice. The FTUE sequence above is the architect's concrete recommendation answering the owner's onboarding request. None requires inventing new art or replacing the game's combat wholesale.

The expensive seam is getting current combat under trusted match authority with per-match inputs and state. The useful shortcut is reusing the authored game and proving that seam on one small battle. Trusting a client-reported victory would only postpone the missing architecture until players can exploit it.
