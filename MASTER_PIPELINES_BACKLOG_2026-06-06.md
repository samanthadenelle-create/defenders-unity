# Master Pipelines — Backlog Board (2026-06-06) · SOURCE OF TRUTH

> ⚠ Numbering: see `CLI_LANES_WO_NUMBERS.md` banner (next free **684**, refreshed 2026-07-12).

Branch **feat/tower-core-loop**. 13 lanes. Drawn from `BACKLOG_SILOS.md`, `PARALLEL_LANES.md`,
`WORK_QUEUE_CONSOLIDATED_2026_06_01.md`, `QUEUE_HEALTH_2026-06-04.md`, `COMBAT_FEEL_PRIORITY_STACK.md`,
this session's live findings, and the design docs (see §"Story → WO mapping").

**WO-NUMBERING AUTHORITY = THIS doc (+ `CLI_LANES_WO_NUMBERS.md`), not the filesystem max.** Reserved new
block **290–305** (minted today). Pre-block: **287** (Threat Intel, L2), **288** (Signature Combat Moves,
L3) used by CLI today; **289 free**. **Next free WO = 430** (306–343 used; 339–343 nightly refill;
**352–390 minted out-of-band 2026-06-08/09**, **391–411 minted on-board (Notion) 2026-06-10/11**,
**412–428 minted on-board (Notion) 2026-06-11/12** — playtest bug sweep + vendor/storefront chain —
and **429** = the owner's "store stock from DB" spec renumbered from a colliding repo WO-414
(2026-06-12 reconcile); all slotted per `CLI_LANES_WO_NUMBERS.md` §"Out-of-band block" sections;
**344–351 skipped — do NOT mint**). Every new WO must be slotted into a lane here.

**Legend:** ✓ done · ◐ in progress · ▶ ready · ⏸ held · ★ new this session · ⚠ blocked/dep
**Parallel rule (CLAUDE.md §9):** `VillageSceneBuilder.cs` = ONE writer (Lane 1 serial). `GameState.cs` /
`SaveSchema` = additive, one-at-a-time. Everything else is file-disjoint and runs concurrently.
**New WO files (290–305)** are spec'd in the project root and trace to the design docs in §"Story → WO mapping".

---

## Lane 0 — NOW: live fixes & verification  ★ (highest priority)

1. ★ Compile-verify `HUDManager.cs` + `GearVisualApplier.cs` on Windows (CompileGate) — edited this session, not yet built.
2. ⚠ Hero rig (WO-286): run `HeroFbxImporter.FixHeroFbx` → must log **`human=True`** → `HeroAnimatorFactory.BuildAll` → build → playtest upright/scale.
3. ★ **WO-302** — Floating health-bar oversize fix (green pill → small bar; host-scale cancel in `FloatingHealthBar.cs`).
4. ★ **WO-303** — Wire combat party HUD (`HUDManager`) to live party/combat data (replace demo values).
5. ▶ Smoke-test the hero anim chain (WO-283/284/285) — visual checks (no T-pose/slide; idle/walk/run + attack/cast/hit/death/victory).
6. ▶ Build-verify WO-107–111 — `QA_CHECKLIST_FILLED` marks them wired but that's code-inspection only; gate + write RESULT files.
7. ▶ Dedupe numbering collisions: two WO-106, two WO-282, duplicate WO-110; **also dup repo files for 329/330/331/333/334 + Notion's divergent 328–339 P0 block (06-08 session)**; repo-414 vs board-414 resolved 06-12 (repo spec → WO-429). (Next free WO = 430.)
8. ★ **WO-301** — Party persistence (now spec'd; lives in Lane 7).
9. ~~★ **WO-328**~~ — **CLOSED** (ambiguous / non-reproducible; CLI confirmed no repro). WO-314/317/325/327 tracked independently.
10. ◐ **WO-329** — Check-in regression test suite (UI static gate + CLI Unity tests + manual QA) — agent authoring; CLI build-verifies the C# tests.

## Lane 1 — World / Environment  (VillageSceneBuilder — SINGLE-WRITER, serialize in order)

1. ▶ **WO-253** (DEF-154) — Split VillageSceneBuilder into partials — **BOTTLENECK, do first**.
2. ⚠ **DEF-156** — Village roster reconcile (magenta ghosts) — after split.
3. ⚠ **WO-157** (DEF-157) — Strip crystal veins (magenta).
4. ⚠ **WO-173** (DEF-163) — Exterior terrain missing (black void).
5. ▶ **DEF-61** — World terrain foundation + fog → blocks DEF-62.
6. ⚠ **DEF-62** — Nature & environment population (blocked by DEF-61).
7. ▶ **WO-246/268** (DEF-91) — Replace KayKit NPCs with character pack.
8. ▶ **DEF-96** — Upside-down tree reappeared; then **WO-137** castle/rampart rebake.
9. ★ **WO-311** — Tree of Life canonical placement at (0,0,0), dominant/emissive (reconcile WO-240).
10. ★ **WO-312** — Replace Farm building with a small harvestable **Food node** (HarvestSite + Economy; coord Lane 6).
11. ★ **WO-313** — Windmill production-crafter station from catalog, wired like lumbermill/armorer/forge (Wren; WO-291/294).
12. ★ **WO-321** — Missing gate on the side exit near Pet House (place gatehouse; verify all 4 cardinal gates + spawn align).
13. ★ **WO-323** — Trees render all white (URP material fix — Fix Polyperfect URP Materials / Quaternius equiv; may run parallel if it doesn't touch the builder).

## Lane 2 — Combat / AI  (code-only, parallel-safe)

1. ▶ **WO-254** (DEF-147) — Hero hover exploit fix — urgent.
2. ▶ **WO-255** (DEF-155) — Hero backwards + walk anim not playing — urgent.
3. ▶ **WO-145** — Advanced enemy tactics.
4. ▶ **WO-146** — Formation movement.
5. ▶ **WO-147** — Situational awareness / perception.
6. ⚠ **WO-155** — Region enemy spawning + red-skull (reads WO-164 ThreatLevel).
7. ▶ **WO-128** — Pet anti-ranged ability.
8. ⚠ **DEF-38** — Spire Defense Mode (large — needs sub-tasks; finale wiring = WO-292).
9. ⚠ **WO-287** — Threat Assessment / Defensibility Intel ("can my base hold?") — SPEC, read-only intel layer (Combat/AI + HUD); build once the raid/outpost-defender loop is solid.
10. ★ **WO-310** — Companion renders wrong color (green tint) fix (StoryCompanion/HeroBodySwapper diffuse; mirror WO-286).
11. ★ **WO-315** — Enemies walk backwards (facing/locomotion; mirror WO-255 hero fix).
12. ★ **WO-316** — Mobs not spawning in families/role groups (compose family+role groups; reuse EnemyFactory; ties WO-146/155).
13. ★ **WO-317** — Defend the Tower: player not grounded (floating) — PatriciaLight spawn/grounding (+ NRE spam null-guard).
14. ★ **WO-318** — Defend the Tower: aim stays north + head-only pivot clamp (turret stance).
15. ★ **WO-320** — Defend the Tower: losing has no impact — defeat screen (WO-235) + tunable penalty + Retry/Return.
16. ★ **WO-326** — Hero walks north but model/anim 90° to the right (rig forward-axis offset; shared facing fix w/ WO-255/315).
17. ★ **WO-327** — Admin "Trigger next wave" no-op — add/wire public `WaveManager.ForceBeginNextWave()`.
18. ★ **WO-330** — **DTT hero cyan silhouette** — hero renders as solid bright-cyan unlit shape; fix URP material assignment in HeroBodySwapper / hero prefab (see screenshot docs/screenshots/dtt_bugs.png).
19. ★ **WO-331** — **DTT keyboard hotkeys** — ability bar buttons (Snare Trap/Mercury Salve/Storm of Arrows/ATTACK) have no key bindings; add 1/2/3/Space hotkeys, serialized inspector fields, shown as labels on buttons.
20. ★ **WO-332** — **DTT aim sensitivity** — mouse rotation far too fast; add serialized `_aimSensitivity` field (default 0.8), clamp vertical ±40°. Coordinate with WO-318 (aim-north fix).
21. ★ **WO-419** — Enemies do not attack after castle → OuterWorld transition (06-11 playtest; Notion spec).
22. ★ **WO-423** — Hero attacks without facing target — rotate-to-target + turn anim blending (Notion spec).
23. ★ **WO-333** — **Tree of Life <30% HP → defense modal broken** (**HIGH**) — logic existed and worked; modal (Defend the Tower / Enter Battle ATB) no longer fires when HeartController drops ≤30% HP. Most likely: scene reference to modal went null after a VillageSceneBuilder rebuild. FIX the wiring — do NOT rewrite. Secondary: skill-point panel stays open behind death screen — force-close all HUD panels on `HeartController.OnHeartDestroyed`.

## Lane 3 — Combat Feel / Animation  (sequential within — biggest UX gain)

**Active:** ◐ **WO-288** — Class signature combat moves (core shipped; class variants + slo-mo/parry/audio = SPEC). *Highest-leverage for the grant demo — the first fight sells it.*

1. ▶ **WO-217** — Animation polish (anticipation → impact → recovery) — do first.
2. ▶ **WO-218** — Animation layering (attack while moving).
3. ▶ **WO-219** — Visual feedback (hit-stop, screen shake, particles, damage numbers).
4. ▶ **WO-220** — Audio feedback (spec only / future).
5. ▶ **WO-213** — Troop downscale → real character models (replace battle pills).
6. ▶ Directional hit/death blends (HitDir/DeathDir) — deferred from WO-285.
7. ▶ Knight full combo trees (~95 imported-but-unwired clips) — deferred from WO-283.
8. ▶ Migrate Enemy/Pet/Dragon/DungeonHero to `ActorAnimator` — deferred from WO-284.
9. ★ **WO-295** — Legendary "Aegis of Elarion" set + Oathweld ward + per-class weapon perks (combat feel of the set).
10. ★ **WO-319** — Defend the Tower: town hero model parity + firing-to-target animation + faster fire rate.
11. ✔ **WO-1734** — Restore the camera occluder FADE (the DEF-151 hard pull-in came back in `486cd7b17` under WO-385's own comment = the owner's "camera spin"), and give the hero the troops' unit-over-wall acquisition rule + anti-oscillation stickiness. FIXED 2026-09-15, awaiting gate + felt-test. *(Number from the `CLI_LANES_WO_NUMBERS.md` banner, not from this doc.)*

## Lane 4 — UI / HUD  (parallel-safe)

1. ★ **WO-303** — Combat party HUD wired to live data (`HUDManager`).
2. ★ **WO-302** — Floating health-bar oversize fix (green pill) — (DEF-255 adjacent).
3. ▶ **WO-110** — Yarn blue-button fix + mobile-first HUD (dedupe the two specs first).
4. ▶ **WO-257** (DEF-204) — Hero Select screen layout fix.
5. ⚠ **WO-156** (DEF-112) — Camera over walls + store polish.
6. ▶ **DEF-152** — Gate crossing blind exit / intel.
7. ▶ **WO-124** — Resource HUD.
8. ▶ **WO-237** — Building upgrade panel polish.
9. ★ **WO-307** — HUD visual overhaul (sleek/grouped/responsive web + mobile; consolidate VillageHud + HUDManager).
10. ★ **WO-308** — Ability bar: active-hero skills as icons + cooldown rings + symbols (needs 307).
11. ★ **WO-309** — Resource bar icons + quantity (food/wood/iron/crystals; rename Gems→Crystals; needs 307).
12. ★ **WO-322** — Compass not visible (fix CompassHud/bootstrap visibility; needed to orient at exits; coord WO-307).
13. ⛔ **WO-411** — P1: Town HUD does not match `hud_mobile_town.png`. **BLOCKED on WO-405** (CLI must NOT pick up). 11 deviations: resources wrong corner/icons · duplicate hero health bars · missing Heart of Elarion · missing pet bars · quest tracker is a persistent yellow panel overlapping gear/"i" (should be a MODAL) · BAG a gray panel (should be a right-side icon) · missing 4-button TOWN ACTIONS row (BUILD/TALK/BAG/QUESTS) · wrong compass icon · "Talk: Windmill" building prompt (building interaction must route through TALK → NPC dialogue) · missing wave timer/PLAY banner · missing enemy INTEL panel. Requires UGUI **layout groups** (not manual anchoring). **Verification gate: side-by-side screenshot vs mockup + owner sign-off — NO self-certification.** Actual: `docs/UI_Mockups/WO-411_town_hud_actual_2026-06-11.png`. Spec: `WORK_ORDER_411_town_hud_mockup_match.md`. (Partial down-payment exists uncommitted: TOWN ACTIONS row + BAG-as-icon + INTEL slot.)

14. ★ **WO-414** — Black circle under TALK button (AttentionGlowUi first-frame color; NOT gated by 405). *(Note: the repo's old WO-414 store-stock spec was renumbered → WO-429, Lane 7.)*
15. ★ **WO-415** — Vendor storefront UI (armor first) from Tech hud elements pack (after 412 fix; isShoppable only per WO-413 canon).
16. ★ **WO-416** — Hide floating "Talk: <target>" world prompt — TALK-button glow is the trigger (supersedes WO-411 #9).
17. ★ **WO-417** — **P1 DO FIRST:** Settings + Dev Tools panels render labels under the row layer — all rows blank (owner's test harness).
18. ★ **WO-421** — Battle HUD broken — skill bar empty, no abilities showing (in WO-405 design-set scope).
19. ★ **WO-428** — Hero damage not shown on HUD — health bar never moves (one-shot-init bug family w/ 414/419).

## Lane 5 — World / Exploration  (OuterWorldBuilder + runtime, parallel-safe)

1. ▶ **WO-164** — Zone foundation (ThreatLevel/depth/ZoneState) — **keystone, do FIRST** (Lanes 2 & 5 read it).
2. ▶ **WO-153** — World crystal mine.
3. ⚠ **WO-159** — Node settlements (claim/defend/deplete) — big, phased; needs WO-164.
4. ⚠ **WO-160** — Wandering tribes (randomized raids) — needs WO-164.
5. ▶ **WO-165** — Dungeon world portals.
6. ▶ **WO-142** — Outer world regions.
7. ▶ **WO-144** — Regional crystal subtypes.
8. ▶ **WO-154 / WO-143** — Rare timed crystal spawns / roaming raids.
9. ★ **WO-305** — Relic-recovery quests (lost Elarion blades → lore + saga components). Needs WO-290; serialize OuterWorldBuilder edits.
10. ★ **WO-324** — Dungeon: placeholder pill lantern NPC + 2-circle exit → real prefab + portal VFX (WO-250/272).

11. ★ **WO-418** — Castle → OuterWorld transition hard pop — blend the two scenes (Notion spec).
12. ★ **WO-426** — Enable the node/outpost claim loop in OuterWorld (builds on WO-106 ClaimableCamp; ties WO-159/239).

## Lane 6 — Economy / Progression  (Core+Village code+data, parallel-safe)

1. ▶ Wallet/economy merge — GameState single source (RESOURCE_ECONOMY Step 0) — underlies 108/151/159/293.
2. ✓/▶ **WO-228** — Resource nodes + pet harvesting (code ready; needs prefabs + placement).
3. ▶ **WO-229** — Harvest visual feedback + HUD display.
4. ⚠ **WO-151** — Village progression + crafting (needs wallet merge).
5. ▶ **WO-115** — Offline harvest accrual.
6. ▶ **WO-117** — Worker dispatch autocollect.
7. ▶ **WO-119** — Pet auto-harvest.
8. ▶ Consume `TerritoryMultiplier` in WaveManager/DifficultyTuning (hook added in WO-106).
9. ★ **WO-293** — Crafting tiers (Common/Fine/Master/Legendary) + legendary recipe system (needs WO-290 gate).
10. ★ **WO-297** — Pet acquisition (tame/egg/rescue) + active slots (reconcile PetDeployer/PetUnlockTracker; needs WO-290).
11. ★ **WO-298** — Pet skill catalog content + balance (4 branches + signatures; extends PetSkillTreeCatalog).
12. ★ **WO-325** — Nothing happens at resource node ([G] Upgrade Mine dead + NRE) — fix node interact wiring + null-guard; EconomyService.TrySpend.
13. ▶ **WO-413** — P1 (Ready): Upgradable buildings wrongly offer the shop menu. **Data-driven** per building (NOT name-matching): `isUpgradable` (windmill, lumbermill, armorer, forge — per Forgemasters doc) → menu = Upgrade / Add Perks / Talk / Leave (NO Buy/Sell); `isShoppable` → Buy / Sell / Talk / Leave. If the upgrade/perk panels don't exist yet, wire the menu correctly + log a follow-up WO for the panels — do NOT ship dead buttons. Ties to WO-411 #9 ("Talk: <building>" prompt) + the buildings-collection capability model (ARCHITECTURE §2b). Canon: `isupgradable-isshoppable-building-rule` memory.

14. ★ **WO-412** — Vendor Wares BUY tab empty (◐ layout root-cause fixed `ca89d9b`; build-test + gear-catalog runtime load still open).
15. ★ **WO-424** — Harvested resources not added to HUD resource count (EconomyService bank → HUD counter).
16. ★ **WO-425** — Hero spawns unarmed — grant default weapon at start (integrates EQUIP flow, WO-412/415/423).

## Lane 7 — Persistence / Backend  (GameState/SaveSchema = coordinate additive)

1. ★ **WO-301** — Party persistence: wallet-keyed roster in GameState + local fallback id (renders via WO-303).
2. ★ **WO-339** — SaveSchema: add quest state versioning + migration stub (anchor for all quest WOs).
3. ★ **WO-340** — PlayerPrefs migration: legacy pet/party data → GameState on load.
4. ★ **WO-341** — Backend: auth token refresh + expiry handling.
5. ▶ **WO-120** — Backend spec reconciliation.
6. ▶ **WO-80** — Vercel + Neon backend.
7. ▶ **WO-129** — Leaderboard / profile / social.
8. ▶ **WO-121** — Metrics / analytics dashboard.
9. ▶ **WO-118** — Rewarded ads route.
10. ▶ **DEF-121** — Resource economy correction (server-authoritative).
11. ★ **WO-429** — Store stock served from the Neon DB (`StoreService` + offline-first local-catalog fallback; backend GET endpoint in React repo; renumbered from repo WO-414). Coordinate w/ WO-412/406.

## Lane 8 — Monetization / Store  (fully isolated, ~70% built — do NOT greenfield)

1. ▶ **WO-236** — Cosmetic store UI.
2. ▶ **WO-73** — Shop UI + battle pass.
3. ▶ **WO-74** — Solana crypto payments.
4. ▶ **WO-75** — Shop UI crypto tabs.
5. ▶ **WO-77** — Staking full integration.
6. ▶ **WO-78** — Tx verification + staking dashboard.
7. ▶ **WO-76** — Staked SKR bonus.
8. ▶ **DEF-29** — Glimmer earn path (Coppin's "The Glimmer Road", see WO-291).

## Lane 9 — VFX / Audio  (no gameplay deps, parallel-safe)

1. ▶ **WO-256** (DEF-205) — Blue ring / circle removal.
2. ⚠ **WO-264** (DEF-94) — Portal color incorrect → blocks WO-272 glow.
3. ▶ **WO-195** — Spell VFX factory.
4. ▶ **WO-111** — Audio depth + boss battles + enemy outposts.
5. ▶ **WO-171** — ATB battle theme.
6. ▶ **WO-170** — 2D retro battle VFX.
7. ▶ **WO-66** — Boss VFX phases.
8. ▶ **WO-243** — Audio full pass.

## Lane 10 — Build / Deploy / Performance

1. ▶ **WO-196** — WebGL no-Brotli rebuild (web deploy blocker).
2. ▶ **WO-211** — WebGL optimize (remove unused assets).
3. ★ **WO-342** — WebGL: memory optimization + GC pressure reduction.
4. ★ **WO-343** — Analytics: event batching + periodic backend flush.
5. ▶ **WO-191** — WebGL size optimization.
6. ▶ **WO-51** — Mobile performance pass.
7. ▶ **WO-53** — Animator culling.
8. ▶ **WO-54** — LOD setup.
9. ▶ **WO-57** — Mobile quality settings.
10. ⏸ **WO-282** — Heroes → Addressables (HELD — daytime play-verified session).
11. ★ **WO-410** — **P0:** 0.1 fps in MainCastle_Hall — main-thread GC storm (13–22 MB alloc/frame) + combat-object leak (PERFDIAG logs on the Notion row).
12. ▶ **WO-1080** — **Capture-provenance gate for layout/touch tickets** (READY): `RunCaptureHeadless` emits `UI_CAPTURE_HEAD <sha>`, a minted layout ticket cites it via a `**Capture:**` line, and `tools/board_build.py` flags the pair as STALE-CAPTURE. Minted after WO-1075/1076/1077/1078 were all authored from the one aged `Builds/wo1060-capture.log` — 1076 was already fixed in `a2162f17d`, and three of them computed different repo-wide drops from the same `UI_TOUCH_FAIL x43`. Files: `Assets/Editor/UICaptureLaunch.cs`, `tools/board_build.py`, `docs/INSTRUMENTATION_STANDARD.md`. Parallel-safe with every panel lane (it edits no panel `.cs`).

## Lane 11 — Build Mode / Player Base  (keystone — mostly own files in BuildMode/*)

1. ⭐▶ **WO-108** — Player Build Mode (the keystone build).
2. ▶ **WO-215** — Build mode click-to-place + validation.
3. ▶ **WO-282** — BuildPreviewModal premium rotation (per-prefab yaw registry).
4. ▶ **WO-113** — Arcane tower buildable.
5. ▶ **WO-114** — Wall upgrade tiers.
6. ▶ **WO-181** — Rampart stairs + upper siege defenses.
7. ▶ **WO-104** — Castle fortification + moat.
8. ▶ **WO-239** — Node claiming + outpost build.
9. ★ **WO-292** — Keystone → Spire finale wiring (≥6 Keystones → Spire Defense → Necromancer; needs WO-290, DEF-37/38, WO-190).
10. ★ **WO-314** — BuildPreviewModal preview-pane cleanup (isolate RT/layer/camera; fix non-functional preview; precursor to WO-282).
11. ⭐★ **WO-1081** — The palette never says what a building DOES (owner felt-test 2026-08-24, gates all green). No reachable detail card since 2026-06-19 — placement = 3 gestures, reading = none; a tile is icon + name + cost; the one existing sentence is a hardcoded per-`type` switch and is FALSE for the Crystal Mine. Adds an authored `description` field + 14 Town strings + one tile line + an oracle. READY TO IMPLEMENT. **Sequence BEFORE WO-1082** (it adds keys to `structures-catalog.json`; WO-1082 moves rows in the same file).
12. ★ **WO-1082** — Build palette leads with the wrong rows (owner ask 2026-08-24, "order the collectors last"). Data-only row move, zero code; the default "All" chip view renders flat, so array order IS screen order. ⚠ ONE OWNER WORD OWED: her two stated reasons both describe the **storage containers**, not the `collector_*` rows. READY TO IMPLEMENT (default variant has no gate conflict; the alternative fails `BuildCarouselTutorialOrderRegression`).

## Lane 12 — Narrative / Onboarding / Quests

1. ★ **WO-290** — QuestService + tracker UI — **FOUNDATIONAL, do first** (291/292/294/296/299/304/305 depend on it).
2. ★ **WO-291** — Vendor Yarn pack (9 NPCs) + NPCCommandBridge quest verbs (needs WO-290; WO-110 fix applies).
3. ★ **WO-304** — Brom's rumor board (quest-board UI; may fold into WO-290).
4. ★ **WO-294** — Forgemasters' Saga: 4 deep crafter Yarn + 3 reconciliation scenes (needs 290/291/293).
5. ★ **WO-296** — Reforge choice (Heart vs cleansed regions) → finale/ending (needs 293/295/292).
6. ★ **WO-299** — Pet bond questlines (Fenn "Wild Hearts" + per-species; needs 290/291/297).
7. ★ **WO-300** — Elarion weaponsmithing lore integration (item flavor, maker's marks, appraisal; needs 293/291).
8. ▶ **WO-230** — Hero Select: 4 character cards (Sylas/Grom/Thrain/Elara — consolidates WO-223/224/225/226).
9. ⚠ **WO-222** — Tutorial redesign (hero → free tower → supplies quest) — after build mode (WO-215).
10. ⚠ **WO-227** — Opening cutscene + story companion — after WO-222 + WO-230.
11. ▶ **WO-238** — Sylas first-meeting narrative.
12. ▶ **WO-277** — Tutorial companion onboarding.
13. ▶ **WO-116** — NPC dialogue / bark system.
14. ▶ **WO-235** — Death & spire-destroyed screens.
15. ▶ **WO-133** — Onboarding FTUE wiring.
16. ★ **WO-422** — Echo Warden re-offers pet selection after a pet is chosen — gate it + unlock-story dialogue + unlock quest (check pet block 290–305 first; ties WO-297/299).

---

## Story → WO mapping (design docs → work orders → lane)

**The Dimming (meta-arc spine)** — `DESIGN_VENDOR_STORYLINES_AND_QUESTLINES.md` §0
→ backbone **WO-290** (L12) · vendor talk **WO-291** (L12) · discovery **WO-304** (L12) ·
convergence/finale **WO-292** (L11) · relics **WO-305** (L5). Culminates in DEF-37/38 (Spire) + WO-190 (Necromancer).

**Vendor "Talk" storylines → questlines** — `DESIGN_VENDOR_STORYLINES_AND_QUESTLINES.md` §1–2
→ **WO-291** (9 vendor Yarn + quest verbs, L12) · **WO-290** (quest state, L12) · **WO-304** (rumor board, L12) ·
Keystone finale **WO-292** (L11). (Per-vendor quests authored inside WO-291/294.)

**Forgemasters' Saga + Legendary gear** — `DESIGN_FORGEMASTERS_SAGA_LEGENDARY.md`
→ tiers/recipes **WO-293** (L6) · deep crafter saga Yarn + reconciliation scenes **WO-294** (L12) ·
legendary set + ward **WO-295** (L3) · Heart-vs-regions reforge choice **WO-296** (L12) · finale **WO-292** (L11).

**Elarion weaponsmithing lore** — `LORE_ELARION_WEAPONSMITHING.md`
→ flavor/marks/appraisal/ceremony **WO-300** (L12) · relic-recovery quests **WO-305** (L5) · tier flavor in **WO-293** (L6).

**Pet system** — `DESIGN_PET_SYSTEM.md`
→ acquisition + slots **WO-297** (L6) · skill catalog content **WO-298** (L6) · bond questlines **WO-299** (L12).
(Reconciles with existing PetUnlockTracker / PetSkillTreeCatalog / PetDeployer / PetHarvester; WO-128 anti-ranged feeds the combat branch.)

**This-session HUD / fixes / persistence** — `RUNNING_PIPELINES_HANDOVER_2026-06-06_PM.md`
→ party HUD wire **WO-303** (L4/L0) · floating-bar fix **WO-302** (L4/L0) · party persistence **WO-301** (L7/L0).

### Flat index — new WOs 290–305

| WO | Title | Lane | Depends on |
|---|---|---|---|
| 290 | QuestService + tracker UI | 12 | — (foundational) |
| 291 | Vendor Yarn pack (9) + quest verbs | 12 | 290 |
| 292 | Keystone → Spire finale wiring | 11 | 290, DEF-37/38, 190 |
| 293 | Crafting tiers + legendary recipes | 6 | 290 |
| 294 | Forgemasters' Saga Yarn + scenes | 12 | 290, 291, 293 |
| 295 | Legendary Aegis set + ward | 3 | 293, 290 |
| 296 | Reforge choice → finale/ending | 12 | 293, 295, 292 |
| 297 | Pet acquisition + slots | 6 | 290 |
| 298 | Pet skill catalog content | 6 | 297 |
| 299 | Pet bond questlines | 12 | 290, 291, 297 |
| 300 | Weaponsmithing lore integration | 12 | 293, 291 |
| 301 | Party persistence (wallet-keyed) | 7 | — (coordinate SaveSchema) |
| 302 | Floating health-bar oversize fix | 4 | — |
| 303 | Combat party HUD wire-to-data | 4 | 301 (pref) |
| 304 | Brom's rumor board | 12 | 290 |
| 305 | Relic-recovery quests | 5 | 290 |

**Pre-290 block (CLI, today):** WO-287 Threat-Assessment Intel → Lane 2 (SPEC) · WO-288 Class Signature
Combat Moves → Lane 3 (in progress). 289 free; 306–343 used (339–343 nightly refill, Lane 7/10 thin).
**352–390 minted out-of-band 2026-06-08/09**; **391–411 minted on-board 2026-06-10/11** (P0 unified-HUD chain
405→403→404, plus 400/411, all gated on 405 Done, in L4; building-tier 392/407 in L11; bugs 393/394/398/406/409;
399 L3, 395 L5, 401 L12, 408 L10 — lane slotting in `CLI_LANES_WO_NUMBERS.md`); 344–351 skipped.
**412–428 minted on-board 2026-06-11/12** (owner playtest sweep: vendor chain 412→415, building-menu 413,
HUD/UI bugs 414/416/417/421/428, world 418/419/426, combat 423, economy 424/425, pets 422; 420/427 used
on-board, titles not mirrored — do NOT mint). **429** = store-stock-from-DB (repo WO-414 renumbered —
collided with Notion's WO-414 TALK-glow fix). **Next free WO = 430.**
⚠ Collisions to dedupe (Lane 0 item 7): duplicate repo files for 329/330/331/333/334; Notion carries a
different 328–339 P0-bug block from the 06-08 session. Do not reuse these numbers — renumber from 391+ when cleaned.

---

## Suggested concurrent assignment (file-disjoint, no collisions)

- **Solo lane:** Lane 1 (VillageSceneBuilder) — one agent, sequential.
- **Start immediately (no deps):** Lane 0 verify, WO-302, WO-303, WO-301; Lane 2 (combat), Lane 3 (combat feel),
  Lane 9 (VFX/audio), Lane 10 (build/perf), WO-164 (zone), wallet/economy merge, **WO-290 (QuestService)**.
- **Unlock after WO-290 lands:** 291 → 293/294/295/296, 297→298→299, 300, 304, 305, 292.
- **Unlock after WO-164 + wallet merge land:** rest of Lane 5, Lane 6, Lane 11 keystone.
- **Shared-file coordination:** `GameState.cs` / `SaveSchema` additive one-at-a-time (Lanes 5/6/7/11 + 290/293/297/301);
  `OuterWorldBuilder.cs` serialize if two Lane-5 items touch it at once (incl. WO-305).
