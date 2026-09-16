> **STALE banner corrected 2026-08-09:** the live anchor is the NEWEST `../CANON_GROUND_TRUTH_<date>.md`
> **sorted by date** (never a name copied here — this line previously hardcoded 08-02 and went stale);
> **the board is `../BOARD.html`, derived from `WorkOrders/*.md`** (`python tools/board_build.py`) —
> **Notion is RETIRED (owner ruling 2026-08-08)**, as are Linear and the task list.
> **RETIRED 2026-09-06: the branch name that used to sit on this line
> (`wip/village2-and-f8-tickets`) went stale, as every copied branch name does.** A branch is never
> stated from a doc - read it off `git status`. No replacement name is written here on purpose.
> Start at **`../HOME.html`** (`python tools/home_build.py`) for
> one-click navigation. If any doc below reads as pre-pivot, the newest anchor wins.

# docs/ — Index

**181 files at `docs/` top level as counted 2026-08-16** (plus the subfolders — `MASTER_CATALOG/`,
`design/`, `qa/`, `_archive/`, …). ⚠ **These counts drift weekly — count them, don't quote them**
(`ls docs/*.md | wc -l`); they read as 167 and 100 for weeks after the tree had moved. Find your
category, don't grep blind. Root-level project files (**118** as counted 2026-08-16) are indexed
separately in `../PROJECT_INDEX.md`.

## Start here / canon

- `../HOME.html` — **the generated wiki-style home page over the doc lake** (rules / architecture / north star / board / catalogs / VFX+sound organization registries) — regenerate `python tools/home_build.py`, never hand-edit; dead links fail the build (WO-943)
- `CLI_OPERATIONS_RUNBOOK.md` — ⭐ **everything a CLI seat needs to RUN the machine, in one file**: session startup + the SME facts that cost days when assumed wrong, the seat model, the board, every gate command and its marker, builds, the R2 content trap, Firebase distribution, Vercel deploys, the database, F8 triage, commit/push discipline. `CLAUDE.md` is the LAW; this is the PROCEDURE.
- `ACCESS_AND_SECRETS.md` — ⭐ **what is public vs secret, and how to share the secret half safely.** Read this before telling anyone you "cannot access prod" — the API base, the endpoints and the project ids are NOT secrets and are listed there. Also: the `.env.local` resolution pattern every tool uses, and the rule that secrets are referred to by name and length, never by value.
- `PROD022_TUNABLE_FLAGS.md` — **the flags you flip instead of rebuilding** (PROD-022). Eight remotely tunable knobs shipped in one build, all defaulting to today's behaviour, so a Pi Browser crash-loop hypothesis costs a `-Tunables` flip instead of a 30-minute WebGL rebuild. Lists every knob, its default, what ON does and which hypothesis it tests, plus the ops worked example and the verbatim provenance trace lines.
- `HANDOVER.md` — **the single operator's manual a new session reads first** (how we work, the binding rules, this-session's new canon, the build/gate/bake cycle, resume points)
- `NORTH_STAR.md`, `NORTH_STAR_PROGRESS.md` — vision + progress against it
- `DESIGN-DECISIONS.md` — **binding creative decisions** (Elarion naming, no Keep, etc.)
- `GAME_DESCRIPTION.md`, `BRAND_BIBLE.md`, `PI_PITCH.md`, `whitepaper.md`
- `TREASURY_PLAIN_ENGLISH.md` — **the treasury explained without jargon**: which address is which, the two that are coin names rather than destinations, the multisig threshold (raised to 2-of-3 on 2026-08-24), and the 6-vs-9 decimals near-miss
- `TREASURY_RUNBOOK_2026-08-23.md` — the step-by-step the owner signs; `tools/treasury-verify.mjs` is the read-only checker
- `MONETIZATION_STATE_2026-08-23.md` — **where the money rail actually stands**: everything code-complete and gated, the live Mainnet 1-SKR canary the only thing outstanding (fail-closed on provisioning, not engineering) + the 6-vs-9 decimals trap

## Asset catalogs & pack notes (check before referencing any prefab)

- `kaykit-asset-catalog.md`, `polyperfect-asset-catalog.md` — **the two prefab catalogs**
- `INSTALLED_PACKS_INDEX.md` — master list of installed packs
- Pack notes: `KAYKIT_NOTES.md`, `POLYPERFECT_NOTES.md`, `QUATERNIUS_NOTES.md`,
  `MIRZABEIG_VFX_NOTES.md`, `LANA_RPG_VFX_NOTES.md`, `SPELLS_PACK_NOTES.md`,
  `MAGIC_VFX_LIBRARY.md`, `MASTER_ASSET_REFERENCE.md`
- **VFX guidance (Hovl combat):** `vfx/Grok-01-VFX-guidance.md` — Grok-01 towers / sword-shield / spellcasting picks + laws; SME hub `HOVL_STUDIO_SME.md`; inventory `vfx/HovlStudio_Inventory.md`; ability keys `vfx/SkillTree_VFX_Mapping.md`; implement via **WO-715**
- **UI guidance (Blink Obsidian, tight lens):** `UI/Grok-02-Obsidian-UI-guidance.md` — Grok-02 frames/slots/factory/MVVM only (not weapons/orcs); binding formula `UI_BLINK_TEMPLATE_CANON.md`; full pack SME `SME/BLINK_SME.md` §1.4/§2.2
- **Here → There WO program:** `UI/Grok-03-here-to-there-WO-program.md` — explicit WOs **716–722** + **715** (capture → unstyled kill → kit law → build HUD → founding FIX → vitals → expansion; VFX 715)
- Library notes: `LEANTOUCH_NOTES.md`, `UNITASK_NOTES.md`, `YARNSPINNER_DIALOGUE_NOTES.md`
- `Assets/_Modules/HUD/README_HUD.md` — Dark Fantasy Mobile HUD (HUD-001) setup, Lean Touch exclusive input, integration wiring (Economy/Heart/Wave/HeroAbilities/Build), D-Pad locomotion tie, prefab + acceptance steps.

## Architecture

- `ARCHITECTURE.md` — **START HERE: the one authoritative architecture hub** (HP B2B lens, assembly map, world/scene model, data/catalog, save, build mode, instrumentation) — indexes all the deep-dives below
- `../CORE_ARCHITECTURE_PLAN.md` — **historical pre-pivot plan** (root file; TD + dungeon + native Solana wallets + mobile + URP framing). Read for intent, not current state — `ARCHITECTURE.md` above is the live hub.
- `ARCHITECTURE_NORTH_STAR.md`, `ENGINE_MASTER_PLAN.md`, `WORLD_ENGINE_ARCHITECTURE.md`
- `ZONE_STREAMING_ARCHITECTURE.md`, `BUILD_MODE_ARCHITECTURE.md` (+ lowercase dup
  `build-mode-architecture.md`), `CHARACTER_ARCHITECTURE.md`, `MONSTER_FAMILY_ARCHITECTURE.md`
- `CATALOG_SYSTEM.md`, `refactor-feature-modules-spec.md`
- `addressables-implementation-plan.md` — ⚠ **rewritten 2026-08-20** (`8e072153c`). It used to describe a
  2026-05 plan the project never built; it now records the **live packing law** — enemies pack per
  **FAMILY**, structures per **ASSET** — plus the derivation rule and the address invariant (authored by
  `Assets/Editor/ContentPackingSetup.cs`, pinned by `ContentPackingRegression`). **⛔ Read it together with
  `tools/r2-ship.ps1`**: re-running the grouper **re-hashes every bundle**, and a re-pack that is not pushed
  to R2 puts tinted capsules in front of the player with no error on screen (WO-1130)
- `ANIMATION_PIPELINE.md` — **canonical animation method** (Shared + per-type, Humanoid retarget; all current/future models)
- `WARDROBE_ARCHITECTURE.md` — **Dressable capability at the rig level** (BlinkWardrobe + VisualFactory.Skin): characters start clothed not in underwear; data-driven per-character wardrobe collection that feeds the cosmetic store (foundation shipped, data layer = WO-456). Read before touching clothing/cosmetics/store.
- `unity-decisions.md`, `UNITY_BEST_PRACTICES_AUDIT.md`
- `INSTRUMENTATION_STANDARD.md` — how new code is written observable-first (FlowTrace/Guard/regression authoring standard; the *method* that operationalizes `CLAUDE.md §12`)
- `UI_PLAYBOOK.md` — **read before adding or changing any screen, panel, HUD element or overlay** (2026-08-09). 15 practices, each stating the defect it prevents and citing the code that demonstrates it: zero UXML, kit builders, fixed-pixel bands, the 112 px touch floor via invisible hit pads, never-colour-alone, own-your-edge, screen-space not billboards, clamp against reserved bands, ASCII-only TMP, near-black panels, safe area, layout callable outside `Update`, verify-by-capture (and how a capture lies), never hang art off a display name, instrument the surface. Ends in a done-checklist.
- `DEBUGGER_TOOLKIT_DESIGN.md` — the debugger/diagnostics toolkit as it ACTUALLY exists (FlowTrace/Guard/BreakCaptureHarness + the AutoPilotProbes fleet oracles + TGVRU); reconciled 2026-06-19, supersedes the old DebugProbe/hotkey design

## Game design specs

- Combat/enemies: `ENCOUNTER_SYSTEM_DESIGN.md`, `REGION_ENEMY_ROSTER.md`,
  `enemy-codex.md`, `enemy-mob-sets-work-order.md`, `DEFENSE_DEPTH_ANALYSIS.md`,
  `DEFENSIVE_CATALOG.md`, `ALERT_INTEL_SYSTEM_DESIGN.md`, `tower-empowerment-spec.md`
- Classes/progression: `BARD_SUPPORT_CLASS_DESIGN.md`, `TALENT_TREE_V2_DESIGN.md`,
  `LEGENDARY_GEAR_DESIGN.md`, `ITEM_DROPS_CONSUMABLES_DESIGN.md`,
  `SCROLL_BLUEPRINT_SYSTEM_DESIGN.md`, `BATTLE_2D_PARTY_DESIGN.md`
- Economy: `RESOURCE_ECONOMY_DESIGN.md`, `GLIMMER_ECONOMY_OPEN_QUESTION.md`,
  `monetization-v2-spec.md`, `anti-cheat-spec.md`. Jewel polishing is data-driven off
  `jewel-polish.json` (dual-copied under `Assets/Resources/Data/Canonical/` **and**
  `Assets/StreamingAssets/Data/Canonical/`), read by `Core/Catalog/PolishBonusProvider`; the surface is
  `Village/Crafting/JewelPolishService` + `JewelPolishConfirmPanel`
- World/village: `avalon-village-layout-spec.md` (historical name; village is Elarion),
  `PLAYER_BASE_DESIGN_CATALOG_ROADMAP.md`, `PROPER_VERTICALITY_PLAN.md`,
  `world-construction-plan.md`, `elemental-codex.md`
- Dungeons: `DUNGEON_DESIGNS.md`, `dungeon-3d-healers-cottage-design.md`,
  `dungeons-3d-unity-layout-spec.md`. Run grading + payout are data-driven: `dungeon-balance.json`
  (dual-copied under `Assets/Resources/Data/Canonical/` **and** `Assets/StreamingAssets/Data/Canonical/`),
  read by `Core/Catalog/DungeonRunGrade`, `DungeonRunPayout` and `DungeonExclusiveItems`; the composed-
  dungeon runtime host lives in `Assets/_Modules/Dungeons/` (`ComposedDungeonHost`, `ComposedPropVisuals`,
  `ComposedPropSpin`, `DungeonLanternBalance`). **Current end-to-end state:** `qa/dungeon-raid-validation-2026-07-26.md` + `qa/dungeon-regression-2026-07-26.md` (dungeons are a functional enter → explore → fight-with-real-win/loss → settle → leave loop; code in `Assets/_Modules/Dungeons/`)
- Raid / troops / work queue (current V1 spine — code in `Assets/_Modules/Village/Troops/` + `Assets/_Modules/Core/Jobs/`): the raid loop is LOCKED to the COC **Teleport/Deploy** model (WO-771); shared enemy classes/families + `EnemyResolver` (WO-772); the common multi-channel "Obsidian" work queue — Builder/Train/Research (WO-773, landed at save v35; **do not quote the live schema version here — read `Assets/_Modules/Core/State/SaveSchema.cs:CurrentVersion`.** This clause said "v36" long after the tree had moved on; a copied number always rots). Firmed-WO set + status: `qa/SUNDAY_STATUS_2026-07-26.md`; validation: `qa/dungeon-raid-validation-2026-07-26.md`.
- Other: `CAMERA_INPUT_OVERHAUL.md`, `CHARACTER_CREATOR.md`, `CHARACTER_REFACTOR_PLAN.md`,
  `audio-mix-spec.md`
- `TUTORIAL_V2_SPEC_2026-07-02.md` — **Tutorial V2** (7 owner-ratified steps, tutorial-steps.json + interpreter; BUILT behind `ff.tutorialv2`, default OFF)
- `_archive/docs/MONETIZATION_REVIEW_2026-07-02.md` — monetization review (Curiosity Shop; loot boxes NO-GO mainnet / GO testnet; dev wallet banked). **Archived.**

## Narrative

- `narrative-bible.md`, `STORYLINE.md`, `ECHOES_OF_ELARION_NARRATIVE.md`,
  `PARTY_OF_FOUR_STORYLINE.md`, `dungeons-storyline.md`, `regions-narrative-and-npcs.md`,
  `BRAND_NOTE_wall_segment.md`

## Backend / platform

- `v2-unity-port-spec.md`, `v2-unity-port-backend-spec.md`, `draft-backend-endpoints`
- `admin-console-spec.md`, `webgl-hosting-notes.md`, `wallets-of-record.md`
- `WEBGL_DELIVERY_PLAN_2026-07-03.md` — **WO-545 Addressables streaming blueprint** (boot target 15–25MB, Cloudflare R2 remote; "what CAN stream, SHOULD stream")

## Process / QA / audits (mostly dated — newest wins)

- **2026-09-06 planning + RCA set (added to the index 2026-09-06; read from the files themselves):**
  `GET_WELL_PLAN_2026-09-06.md` - LIVE PLAN. Sequences what the two RCAs below found, from the
  read-only audit fleet plus the owner's two felt-test batches; every line cites the audit that
  measured it, with the evidence in the minted WOs (WO-1446 onward).
  `READY_RCA_2026-09-06.md` - every ticket that was READY TO IMPLEMENT or REOPENED at HEAD
  `c9efbe0a5`, root-caused from captured logs, PNGs and `file:line`; unproven causes say so and
  name what would prove them.
  `GROWTH_RCA_2026-09-06.md` - answers the owner's verbatim question *"determine why I can not
  gain any exposure and grow a base"*; every claim read from the tree, a live tool call or a
  fetched page, or marked UNPROVEN.
  `RAID_BALANCE_AUDIT_2026-09-06.md` - the raid economy measured for an OUTSIDE reader (designers,
  testers, partners with no repo context): every payout, cost and cooldown read from the data files
  or the owner's own device logs that day, ticket references pushed to an appendix. Read it with
  the north-star map `PROGRAM_RAID_ECONOMY_2026-09-04.md`, which is the ruling; this is the
  measurement underneath it.
  `RAID_DIFFICULTY_LEVERS_2026-09-16.md` - which raid-difficulty knobs are RUNTIME vs BAKE-TIME, and
  which can change without a build (the tunables rail is live and carries no difficulty key; the
  remote-catalog seam is dormant and its allowlist excludes `scene-configs.json`); the Iron
  Bastion/Veiled Enclave clone, the 2026-09-16 `raid.honorThirdStarSeconds` change and its capture
  side effect, and the WO-1763 per-camp plan.

- **Manage redesign (WO-1418+) flow & prerequisites:** `manage-flow-map/MAP.md` — the redesign's dataflow lanes and mock-up mapping; `PREREQUISITE_REGISTRY_2026-09-06.md` — the audit of every gate and cost in the system, with the container singletons ruling (Ruling 23) and barracks/Heart Level unification (Ruling 21) reflected; `ART_DELIVERY_2026-09-06_manage_assets.md` — Synty building portraits checklist; `ART_REQUEST_2026-09-06_manage_tab_portraits.md` — owner art request for the redesigned tabs
- **ARCHIVED (path fixed 2026-09-06 - these seven moved under `_archive/docs/` and the index still
  pointed at `docs/` top level):** `_archive/docs/BACKLOG_TRIAGE_2026-06-04.md`,
  `_archive/docs/ARCHIVED_ISSUES_2026_06_04.md`, `_archive/docs/ROI_PLAN_2026-06-03.md`,
  `_archive/docs/REUSABILITY_AUDIT_2026-06-03.md`, `_archive/docs/QA_player_sanity_pass_2026-05-30.md`,
  `_archive/docs/acceptance_verification_2026-05-30.md`, `_archive/docs/VISION_GAP_ANALYSIS_2026-05-30.md`
- Still at `docs/` top level: `WO_ROI_TRIAGE.md`, `bug-triage.md`, `diagnosis-report.md`,
  `village-review-suggestions.md`, `recovery-work-orders.md`, `claude-code-work-order.md`
- `_archive/docs/UI_BLINK_CONFORMANCE_AUDIT_2026-07-02.md` — screen-by-screen UI audit against the Blink template canon (+ owner addenda; source of the extended UI canon rules). **Archived.**
- `_archive/docs/PUBLISHER_CRITIQUE_2026-07-03.md` — publisher-lens critique (pass-with-revisit; ranks the seam un-stack #1). **Archived.**
- `reference/` — **known dictionaries** (SUNDAY_HOUSEKEEPING §2): durable, source-cited registries rather
  than one-off reports. **Frozen ledgers — banner, never rewrite.**
  - `reference/SESSION_INDEX_2026-08-06.md` — the 2026-08-05 evening / 08-06 overnight VFX session: every
    defect with its proving line, **every REFUTED belief with the evidence that killed it**, every refusal
    with its disqualifying measurement, the owner rulings, and the open items
  - `reference/DEFECT_INDEX_2026-08-05.md` — the same, for the earlier half of 2026-08-05 (dungeon P0,
    wallet dossier, catalog fallback drift)
  - `reference/ICON_CATALOG.md` — **the single icon registry** (2026-08-16): all 1 076 icon files across
    `ItemIcons` / `RpgUi` / `Talents` / `HudIcons` / `ProjectileIcons`, every row tagged
    Ranger / Knight / Mage / Shared / Cleric / Unassigned and cited. Records the authored-first resolution
    order, the authored-vs-fallback health ratio, orphans, missing rows and collisions. Pairs with
    `reference/WEAPON_CATALOG.md` (which is the authority on the gear curation pipeline).
  - `reference/REGRESSION_ORACLE_INVENTORY.md` - **NEW 2026-09-06.** Every one of the 414 `[tag]`
    oracles registered between the fences in `Assets/Editor/Regression/DataRegression.cs`, with its
    suite file, its registration line and one line on what it locks; plus the measured known-red
    baseline and the 7 suites that exist but are NOT counted by the marker.
 - `reference/REGRESSION_COVERAGE_MATRIX.md` - **RE-MEASURED 2026-09-06 (Sunday sweep step 4);
    the "counts are stale, use its proposed assertions only" caveat that sat on this row is
    RETIRED.** Every row was opened at source that night; the two older censuses (2026-08-16 and
    the frozen 2026-07-19 ledger) are preserved, not carried forward, at
    `git show bd798ad73:docs/reference/REGRESSION_COVERAGE_MATRIX.md`. Pairs with the oracle
    inventory above: the inventory is the per-oracle roll, this is the coverage view over it.
  - `reference/HERO_ANIMATION_DICTIONARY.md` · `reference/DOTWEEN_SME.md` ·
    `reference/MASTER_BACKLOG_2026-07-19.md`
- `vfx/` — VFX direction + inventory: `VFX_CREATIVE_PICKS_REGISTRY.md` (owner-ratified elemental wheel x
  6-beat kit), `VFX_PREFAB_HANDBOOK.md`, `PARTICLE_PACK_UTILIZATION_MAP.md`, `HovlStudio_Inventory.md`,
  `SkillTree_VFX_Mapping.md`, `Grok-01-VFX-guidance.md`, `weapon_vfx_design.md`
- Subfolders: `audit/`, `qa/`, `roadmap/`, `port-notes/`, `reference/`, `vfx/`

## Media

- `screenshot-*.png` — village/dungeon/dragon reference shots

> Maintenance: add new docs to the right category when you create them.
