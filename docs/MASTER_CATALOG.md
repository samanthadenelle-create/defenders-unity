# MASTER CATALOG — Project Index

> # DELTA 2026-09-06  -  read this FIRST; it supersedes every dated block below it
>
> **Live anchor = the ONE root `CANON_GROUND_TRUTH_*.md` WITHOUT a `SUPERSEDED` banner.** ⛔ Its date
> is deliberately not typed here (WO-1482): every dated block further down this file names a *then*-live
> anchor, and each of those names went stale where it stood - `:96` still says 08-21, `:140` says 08-16,
> `:207`/`:219`/`:246` say 08-06. Those are FROZEN history, not competing pointers.
> ⛔ **BRANCH AND PUSH-STATE ARE NEVER WRITTEN IN THIS FILE.** Run `git status -sb` for the branch and
> `git rev-list --count origin/<branch>..HEAD` for the ahead-count. A non-zero ahead-count is normal -
> the cadence is commit local, push only after the owner retests or a regression proves it.
>
> Scope: the commits `949e848a0..HEAD` plus the working tree at the time of writing. Every entry
> below was re-verified by opening the `.cs` file, never a comment digest, and each cites the file
> it was read from. Nothing here restates a number the code already owns.
>
> **NEW TYPE  -  the friend-or-foe authority.** `Assets/_Modules/Core/Combat/CombatFactionRules.cs`
> (WO-1439) is now the ONE answer to "may this attacker hit that?": `MayAttack` /
> `IsFriendlyFire`, each with an `IDamageableStructure` and an `IDamageable` overload (WO-1438),
> both funnelling into one private `Decide`. Its own header carries a MEASURED census of the
> inline `Faction != CombatFaction.Hostile` copies that remain (Pet, ArcaneTower, DefenseTower,
> TowerCombat, PlayerAttackController's ability lane, HeroAbilities) and tells the reader to RUN
> THE GREP rather than trust the list. (!) Overload trap, stated in the source: passing a CONCRETE
> type implementing both interfaces does not compile  -  deliberately, a loud error instead of a
> silent wrong answer. -> `MASTER_CATALOG/core.md` (Combat/).
>
> **CLOUD SAVE STOPPED EATING LOCAL PROGRESS** (`Core/State/GameStateService.cs`): the hand-copied
> seven-field cloud merge is DELETED and replaced by `ApplyBackendState`  -  the same
> migrate -> validate -> `ApplyPersisted` path the local `Load()` uses  -  gated on the new
> `LastLocalSaveUnixMs` recency stamp, returning a `BackendApplyOutcome` enum so an oracle can
> assert the DECISION. The old doc sentence "server wins on BestWave; local wins on Towers and
> Pets" is RETIRED  -  there was never a per-field merge. -> `core.md` (GameStateService).
>
> **A TRANSIENT 500 NO LONGER DESTROYS THE SESSION** (`Core/Backend/BackendRequestSigner.cs` — the
> folder was `Core/Web3/` until WO-1755, 2026-09-15,
> WO-1454): renewal failures are now CLASSIFIED  -  only 401/403 clear the token; 5xx, timeouts,
> unparseable bodies and empty-token 2xx keep it and back off. -> `economy-meta.md`.
>
> **SUITE MARKERS ARE MEASURED, NOT TYPED.** `SessionRegression` no longer prints the literal
> `SESSION_GUARDS_OK 6/6 checks`; the checks are a TABLE and both halves of the marker are derived
> from it (WO-1493). STOP: Do not write a digit into this catalog for it. -> `editor-tools.md`.
>
> **Entries touched this pass:** `core.md` (CombatFactionRules, GameStateService, WorldHold player-
> owned sites) * `economy-meta.md` (BackendRequestSigner) * `village-enemies-world.md`
> (TroopController, Enemy probe cadence, RaidDeployController) * `village-systems.md`
> (WelcomeBackDoorsVM, StructureCardVM, WallSegment, ManageScreenVM) * `resources-art.md`
> (VFXManager release policy) * `hud.md` (HudKitController) * `editor-tools.md` (SessionRegression
> + 13 suites).

> # ▶ DELTA 2026-09-02 — read this FIRST; it supersedes every dated block below it
> **What this pass CORRECTED in this file (each verified by opening the code, never a comment):**
> - ⛔ **The save-schema version is no longer printed anywhere in the catalog.** It said **v38** while
>   `SaveSchema.CurrentVersion` had moved on — the SECOND time this number has rotted here (it was
>   corrected from v36 to v38 on 2026-08-16). **Read it off
>   `Assets/_Modules/Core/State/SaveSchema.cs`**, whose const line doubles as the full changelog. The
>   fix is deleting the number, not writing today's; a restated version is guaranteed to rot.
> - **§2b/§2c scene flow was months stale.** The home hub is the MERGED **`Main_Castle_Overworld`**
>   (one navmesh), resolved through `SceneRouter.Castle` / `CastleCandidates` under
>   `FeatureFlags.MergedWorld` (`defaultOn: true`). **`OuterWorld.unity` and `Village.unity` are
>   DELETED** and `WorldSceneLoader` is a traced NO-OP; `MainCastle_Hall.unity` is still on disk as the
>   LEGACY flag-off branch and is NOT the hub. See §2b.
> - **The §3 risk ledger was re-verified at source.** Items 1, 3, 4, 5, 7, 8, 10, 11, 13 and P2 9 are
>   **RESOLVED** (kept with their original text struck through — a deleted row cannot correct a reader
>   who half-remembers it). Item 2 is now **unprovable as written** (it names a deleted scene). Items
>   6, 12, 14 and the `cleric` half of P2 10 **still bite**. ⚠ Ledger item 7 was itself the failure it
>   warned about: it printed a WO number.
>
> **New systems documented this pass, and where they now live:**
> - **The REMOTE rails** — `Core/Ops/RemoteTunables.cs` + `RemoteTunablesService.cs` (PROD-022 knob
>   rail; contract doc `docs/PROD022_TUNABLE_FLAGS.md`) and `Core/Data/RemoteCatalogSource.cs` +
>   `RemoteCatalogService.cs` + `RemoteCatalogOverrides.cs` (WO-1331, the seam that finally assigns
>   `CanonicalJson.Source`, **flag-gated OFF**: `ff.catalogremote`, `defaultOn: false`), plus
>   **`Core/Combat/OverTimeEffects.cs`** (`OverTimeEngine<TTarget>`, WO-1330 — liveness is a REQUIRED
>   constructor argument that THROWS on null, so the engine cannot be built without saying how to test
>   it; it replaced four ad-hoc tick loops). → `MASTER_CATALOG/core.md` **DELTA 2026-09-02**.
>   ⚠ **Do not restate the tunable knob COUNT** — read `RemoteTunables.Registry`; it changed three
>   times in one evening.
> - **`Village/Vfx/MarqueeSpellVfx.cs`** — a string-set registry ONLY; `VFXManager.PlayKey` remains
>   the single spawn owner. → `MASTER_CATALOG/resources-art.md` §6 DELTA 2026-09-02.
>
> **Facts recorded this pass, each of which cost real time to discover:**
> - ⛔ **`Resources.Load<TextAsset>` resolves FIRST on every platform and `Assets/Resources/` is
>   COMPILED INTO THE PLAYER**, so **"data-driven" has never meant "tunable without a rebuild"**, and
>   editing the StreamingAssets twin alone changes nothing. The single most misunderstood fact in the
>   repo. → `core.md` (DATA/JSON) + `data-catalogs.md` §1.
> - **`Assets/Spells Pack/` is GITIGNORED** — a prefab edit there cannot be committed, never reaches
>   another machine, and dies at the next re-import while still changing the local build.
>   → `resources-art.md` §6 + §8.
> - **`Assets/Blink` holds 777 prefabs and ZERO VFX**; two of its four bundles are README files of
>   unclaimed Asset Store links. → `BLINK.md`.
> - **`Core/State/ServerConfig.cs` is DEAD** — fully wired client-side, but `api/game/load.js` has
>   never emitted a `config` key, so it has never once been settable. → `core.md` (State/).
> - **`heart.json` / `towers.json` have NO RUNTIME READER**, only a regression asserting they are
>   served; the shipped Heart is 100 HP + 2 HP/sec regen (`HeartController.cs:97`,
>   `HeartRegen.cs:61`) while the file authors 160 HP and no regen. → `data-catalogs.md` §7.4.
> - **`Alduin` (Necromancer) and `Aldwin` (Echo #1, the founding wolf) are DIFFERENT characters**, two
>   suites forbid conflating them, and the mistake has been minted TWICE in opposite directions.
>   → `dialogue.md` (NAME PIN).
>
> ⚠ **Not verified in this pass, flagged rather than guessed:** whether the old OuterWorld frame-rate
> cost survived the world merge (ledger 2); whether `OuterWorldBuilder.BakeWorldNavMesh` /
> `SpawnPathVerifier` still open deleted scenes (ledger 25); the 223 MB WebGL figure (ledger 4).


> # ▶ DELTA 2026-08-21 — read this FIRST; it supersedes every dated block below it
> **Live anchor = `../CANON_GROUND_TRUTH_2026-08-21.md`.** Read HEAD, push state, the save-schema
> version (`SaveSchema.CurrentVersion`), the suite counts (the marker line on a FRESH log) and the
> next free WO (the `CLI_LANES_WO_NUMBERS.md` banner) **off their sources, never off this file**.
>
> **What landed tonight, and which area file now carries it:**
> - **PvE SIEGE + the persisted Defense Report (WO-1026)** — a new `Village/Siege/` directory
>   (`SiegeClock`), five new files in `Village/Waves/` (`SiegeScheduler`, `SiegeSession`,
>   `SiegeSchedulerBootstrap`, `DefenseReportBuilder`, `StructureVitalsWatch`), the
>   `Core/Defense/` data model + ledger, `Core/UI/DefenseMapPlate`, and `Village/UI/Defense/`
>   (report panel + bootstrap). ⛔ **NOTHING in the cluster spawns anything** — `WaveManager` stays
>   the single attack authority and the whole integration is one call to `ForceBeginNextWave()`.
>   ⛔ **`Village/Siege/` exists specifically to sit OUTSIDE the `Village/Waves/` sweep** that
>   `DevTimeSkipRegression` case 6 lints for `TimeSource`; do not move it back.
>   → `MASTER_CATALOG/village-enemies-world.md` + `core.md`.
> - **Per-camp raid COOLDOWN (WO-728)** — `Village/World/Camps/RaidCooldownService` +
>   `Core/State/RaidCooldownRecord` + `Core/UI/RaidStrings`, **no schema bump**.
>   → `village-enemies-world.md` + `core.md`.
> - **Battle Pass + Monthly Ledger + The Night Market** — 11 new/moved files under
>   `Assets/_Modules/Wallet/` and a new canonical `battle_monthly.json`. ⚠ **There are now TWO
>   battle-pass runtimes in the tree and the conflict is declared in the code as needing an owner
>   decision.** `PurchaseGate.cs` MOVED from `Village/Monetization/` into `Wallet/`.
>   → `economy-meta.md` + `data-catalogs.md`.
> - **Realm map pins actually publish** — `Village/World/RealmPinProducers` closes a hole where the
>   board, the pin vocabulary and both map surfaces all shipped and **nothing ever published**.
>   → `village-enemies-world.md`.
> - **The crate is a CHEST** — `BreakableContainer` is no longer `IDamageable` /
>   `IDamageableStructure` / `Hostile` and no longer rewrites its layer to "Enemy"; it is opened,
>   out of combat, and its class name is load-bearing (baked into every composed dungeon by GUID +
>   name). Drops now read by SILHOUETTE (`Village/Items/ItemMoteShapes`).
>   → `village-enemies-world.md` + `village-systems.md`.
> - **Sheathe orientation is DERIVED PER MESH** from `mesh.bounds` — the shipped props have
>   Read/Write OFF, so vertex-based approaches are silently inert ON DEVICE. 11 of 12 meshes
>   resolve. → `village-hero.md`.
> - **11 new oracles + `SourceLint` + `HeadlessState`** under `Assets/Editor/Regression/`.
>   → `editor-tools.md`.
> - **`Assets/_Modules/Environment/TorchFireController.cs` is DELETED** (WO-992, provably dead) —
>   already recorded in `misc-modules.md`.
> - **`ElarionUiKit` gained `AddRawImage`**, its first `RawImage` primitive. → `core.md`.
> - **`PanelId` now runs to 20** (`DefenseReport = 18`, `BattlePass = 19`, `MonthlyLedger = 20`) —
>   the enum lives in `Core/UI/PanelRouter.cs`. Any doc saying "PanelId 0-15" is stale.
> - **Four new risk-ledger entries: WO-1135 / 1136 / 1137 / 1138, plus an UNTRACKED-FILES P1.**
>   See §3 P1 items 10-14.

> # ▶ DELTA 2026-08-16 — read this before the banners below
> **Live anchor = `../CANON_GROUND_TRUTH_2026-08-16.md`** (every "live anchor" reference further down this
> file naming 08-02, 08-03, 08-06 or 08-09 is stale). **Read HEAD and push state off `git`, never off a
> hash copied into a doc.**
> Save schema: **read it off `SaveSchema.CurrentVersion` (`Assets/_Modules/Core/State/SaveSchema.cs`).**
> ⛔ **DO NOT restate the number here.** That const line doubles as the full changelog, and the
> version has now gone stale in this file TWICE — corrected from v36 to v38 on 2026-08-16, then drifted
> again by 2026-09-02. A copied version number is guaranteed to rot; the const is the only authority.
> ⚠ **Read every gate count off the marker file, never off this
> doc** — the three entry points emit DISTINCT markers (`REGRESSION_OK` / `CHECKIN_SUITE_OK` /
> `SESSION_GUARDS_OK`), and the newest run's markers are named in the anchor's gate block.
>
> **⚠ THE BODY OF THIS INDEX IS A FILENAME LIST ONLY — the `docs/MASTER_CATALOG/<area>.md` files are the
> trustworthy layer.** §1–§3 below were never refreshed by WO-836 and remain known-stale in a way that
> needs a real code-verified pass; they were deliberately **not** touched by the 2026-08-09 canon
> re-anchor. Use them to find a file, never to assert a fact.
>
> **Moved on 2026-08-08 — read the anchor, not this index:** the **dungeon stairs are SOLVED** (WO-930,
> `3ab1bfb6` → `cb092b7f`; all 4 content dungeons `PathComplete`; root cause was `SolveMate` hardcoding
> `yaw = 0f` on vertical sockets) · **structure orientation** now has a per-catalog-row
> `RepoProps.preservePrefabRotation` (default false, one opt-in) after a global apply laid the town on its
> side — **headless gates cannot see orientation** · **store purchases are re-gated OFF and locked**
> (WO-931, `StubWalletProvider` free-grant hole) · the desktop player now ships **RELEASE**.
>
> **Areas that moved on 2026-08-05/06 — read the anchor + the area file's own dated delta, not this index:**
> - **VFX (the night's headline, two P0s):** `IsLoop` was a hand-authored sticky checkbox, **53 of 122 picks
>   wrong**, and a fire-and-forget loop **permanently consumed one of the 20 global slots** — the archer and
>   ballista were starving the whole VFX budget. Both catalog generators now DERIVE it, with **standing
>   owner rulings PINNED above the derivation**. Separately, `CopyAsset` copies the **prefab only**, so
>   **27 of 28 tracked VFX prefabs / 183 references** pointed into gitignored art; now 0, with ~23.85 MB
>   mirrored to `Assets/Resources/VFX/_Shared/`. ⚠ **`VFXType` serialises by ORDINAL — appends only.**
>   ⚠ **`Build()` does `entries.arraySize = rows.Count`** — a row written only by a builder is silently
>   dropped by the next regenerate. See `resources-art.md`.
> - **Hero:** `ff.knightonly` defaults **OFF** — roster Knight/Ranger/Mage. Any area file still saying
>   "dormant under knight-only" is stale. A **latent invisible-hero P0** is closed (Ranger/Mage had no FBX,
>   fell to a **gitignored** Blink body, and instantiated **nothing** on a fresh clone). **✅ WO-910 is
>   RESOLVED (2026-08-16)** — all three trees re-authored to **3 bases branching wider**: knight
>   3/7/8/7/7 (32 nodes) · ranger 3/5/6/6 (20) · mage 3/6/6/5 (20), verified in `hero-talents.json`.
>   ⚠ Ranger and mage previously had **no authored x/y at all**, so "31 dead nodes" described a missing
>   layout, not a design deficit. **One focus plate per BOARD** (was one per track). Any line still
>   calling WO-910 open is stale. See `village-hero.md`.
> - **Structures:** one owner-ruled **height cadence** — 1.25 landmark / **1.2 towers** / 1.0 base / 0.75
>   siege / 0.35 decoration, recorded in the data as `_heightCadence`, **catalog v8** (6→7 archer, 7→8 cadence). **Walls deliberately
>   excluded** (narrowing opens pathable gaps in saved wall runs). Any "towers 1.25" line is stale.
>   See `village-systems.md` + `data-catalogs.md`.
> - **Accessibility:** the low-health tell is **no longer a red vignette** — pulse rate, guttering depth and
>   a recipe swap below a quarter health. Shape and timing, never hue.
> - **Session ledgers:** `reference/SESSION_INDEX_2026-08-06.md` (incl. every REFUTED belief) and
>   `reference/DEFECT_INDEX_2026-08-05.md` (frozen).

> # ⚠ THIS INDEX FILE WAS **NOT** REFRESHED BY WO-836 — use it as a FILENAME LIST ONLY (flagged 2026-08-03)
> WO-836 rewrote the **19 section files** under `docs/MASTER_CATALOG/`. **It did not rewrite this file's own
> body.** §1–§3 below are ~2026-07 fiction and contradict both the section files and the live anchor:
> Village-Hero "Blaise + class bodies" · NPCs "party-of-4" · Enemies/World "OuterWorld streaming"
> (that scene is DELETED) · Dialogue "64 `.yarn` nodes + vendored Yarn" (Yarn is FULLY REMOVED, WO-557)
> · `SaveSchema CurrentVersion=30` (read the live value off `SaveSchema.CurrentVersion` — never off a doc) · "next free WO = 412" (**never trust a copied number — read the
> `CLI_LANES_WO_NUMBERS.md` banner; corrected 2026-08-06, the 853/863 figures previously printed here
> were themselves stale**) · EconomyService "4-resource wallet" (5 with Coins) · `ZoneManager` village ±42/±33 (actual
> **52/52** — the 42/33 figure mis-classifies the courtyard and IS the 07-26 "enemies inside the castle" bug).
> Several §3 ledger rows are also closed (Aegis set reachable, the six WebGL-broken catalogs pinned,
> Settings/Pause UXML converted, `HUDManager`/`VirtualDPadLean` deleted, backend live, OuterWorld gone).
>
> **⚠ And the 19 section files are code-true as of `b77a178e` (2026-08-02 morning), NOT current HEAD** —
> ~20 commits landed after that fleet ran. Known drift: `economy-meta.md` says WO-830 is "spec only, NOT in
> code" (it shipped) · `docs-wo-state.md` says save v35 / next-WO 836 · `resources-art.md` says the KayKit
> bodies have no Animator wiring (WO-833 shipped it) · `village-npcs.md` documents the
> `"Forge"`→`"Blacksmith"` anchor mapping as correct (that mapping **was** the WO-840 bug).
>
> **Read order that actually works** *(anchor corrected 2026-08-06)*: `CANON_GROUND_TRUTH_2026-08-06.md` → `KEY_FACTS.md` →
> the `CLI_LANES_WO_NUMBERS.md` banner → the specific `docs/MASTER_CATALOG/<area>.md` → `CLAUDE.md` →
> `docs/ARCHITECTURE_PRINCIPLES.md` → the newest `docs/HANDOVER.md` block.

The single master index a new session reads to understand the whole project
**without operating on assumptions**. Each area below has a deep section catalog
under `docs/MASTER_CATALOG/<id>.md`, verified file-by-file (read, not from comments).

> ## ✅ FULL SME REFRESH 2026-08-02 (WO-836 — the owner-ordered 14-agent fleet)
> **ALL 19 section catalogs under `docs/MASTER_CATALOG/` were REWRITTEN 2026-08-02, verified from code
> at HEAD `b77a178e`+** (file:line cites; comments-lie law applied; per-file inventory + seams + risk
> ledger each). The 07-22 §6 catalog-drift ledger is PAID for every area. Read the section files
> directly — they are current. **Live anchor = `CANON_GROUND_TRUTH_2026-08-06.md`** (corrected 2026-08-06).
> Fleet risk roll-up: see the **★★ SESSION HANDOVER — 2026-08-02** block in `docs/HANDOVER.md` (no longer
> the newest — the newest is **2026-08-06**).
> **Any banner below that calls the `<area>` files "2026-06-12-stale" is SUPERSEDED by this line.**

Section catalogs compiled **2026-08-02** (previously 2026-06-12; the stale-framing banner below is
retained for history only — the section files no longer carry the pre-pivot framing).
STOP: **Current branch: DO NOT WRITE IT HERE  -  run `git status -sb`.** This line named
`wip/village2-and-f8-tickets` long after the tree had moved to `feat/synty-art-retheme` (measured
2026-09-06). A branch name copied into a doc is stale the next time anyone branches.

> ⚠ **HISTORICAL (2026-06-12, pre-pivot — no longer describes the section files):** the old section files
> described the hero as **"Blaise"
> + Blink/class bodies** and a **party-of-4** — both SUPERSEDED by the 06-22 single-Knight pivot (hero =
> single Tripo self-rigged "Grom", Blink hero rig JUNKED, everything else autonomous). For LIVE state read
> `CANON_GROUND_TRUTH_2026-06-26.md` + `docs/COMBAT_PIVOT_NORTHSTAR.md`. The per-area code mechanics below
> remain trustworthy; the hero-identity / party / Defend-the-Tower framing does not.

> ⚠ **SUPERSEDED 2026-08-02 by the WO-836 refresh banner at the top of this file — the two notes below
> are kept for history ONLY. The `<area>` files are NOT 06-12-stale and `misc-modules.md` is NOT
> "doubly stale"; all 19 were rewritten from code on 2026-08-02. Do not act on the fix-lists below.**
>
> ~~STALE: 2026-07-26 — the live anchor is now `CANON_GROUND_TRUTH_2026-07-26.md` (delta over the deep `2026-07-22` module anchor); HEAD is `7dec0e07`, local==origin. The `docs/MASTER_CATALOG/<area>.md` section files below are still dated **2026-06-12** — their "how it works" mechanics are largely accurate but their COUNTS + STATE facts are weeks stale (fix-list = the 07-22 anchor §6 catalog-drift ledger + §7 comment-lie registry). **`misc-modules.md` (Dungeons) is doubly stale** — it predates the RoomForge pipeline AND the 07-26 dungeon functional-loop wave (WO-770.1/.2/.3/.3b/.4/.7/.9: exits, correct-return, real win/loss, real-time settle, readable lore, toasts, live Bryn). Trust the 07-26 anchor for live dungeon/raid state.~~
>
> ~~STALE: 2026-07-12 — the live anchor is now `CANON_GROUND_TRUTH_2026-07-12.md` (the 06-26 anchor below is superseded), and HEAD is `f123859d`, not `8aa24c32` (see CANON_GROUND_TRUTH_2026-07-12.md)~~

> READ ORDER for a cold start *(anchor + handover dates corrected 2026-08-06)*:
> **`CANON_GROUND_TRUTH_2026-08-06.md`** (live anchor) → this file → the
> relevant section file → `CLAUDE.md` (binding rules) → `docs/ARCHITECTURE_PRINCIPLES.md` (architecture
> law) → `docs/HANDOVER.md` (**newest = the 2026-08-06 block**). Trust the ground-truth anchor + newest
> handover for live state; trust the section files for "how it actually works."

---

## 1b. NEW SYSTEMS SHIPPED SINCE THE 06-12 CATALOG (added 2026-07-26 — not yet folded into the area files)

> These systems postdate the 2026-06-12 section-file compile. Catalogued here (from code, at HEAD) so the
> index stays green (§15). **⚠ The "the area-file bodies are still 06-12 and do not mention them" caveat
> this section was written under is SUPERSEDED — WO-836 (2026-08-02) rewrote all 19 area files from code,
> so they DO cover these systems now; this section is a summary, no longer the only record.** State legend:
> **SHIPPED** = present + wired · **IN FLIGHT** = present but not asserted done.

**Raid V1 spine — SHIPPED, reachable end-to-end** (CoC deploy-and-watch; `ff.raidwalk` OFF, `ff.barracks` +
`ff.buildtimers` ON). Full beat→class map + P0/P1/P1.5/P2 ladder = `docs/RAID_NORTHSTAR.md` §2A. Classes:
- `TroopTrainingVM` (`Assets/_Modules/Village/Hero/TroopTrainingVM.cs`) + `TroopTrainingPanel` — train UI/queue.
- `ArmyStorage` (`Assets/_Modules/Core/State/ArmyStorage.cs`) — housing cap + perk + veterancy.
- `RaidSelectionScreen` + `RaidSelectionVM`, `RaidDeployScreen` + `RaidDeployVM` (`Assets/_Modules/Village/Hero/`) — pick target + pre-raid.
- `SceneRouter.GoRaid(sceneName)` (`Assets/_Modules/Core/SceneRouter.cs`) → scenes `RaidBase_IronBastion`, `RaidBase_fortified_garrison`, `RaidBase_mage_enclave`, `RaidBase_raider_camp_small` (`Assets/Scenes/`). **No `RaidParams`/loadout bag yet** — the WO-774 P0 seam.
- `RaidDeployController` + `TroopDeployer.SpawnFromArmy(...)` + `TroopController` (`Assets/_Modules/Village/Troops/`) — tap-deploy tray + spawn + auto-fight.
- `RaidScoring` + `RaidHudController` (`Assets/_Modules/Village/Troops/`; oracle `Assets/Editor/Regression/RaidScoringRegression.cs`) — 180s clock, stars, loot.

**Core/Jobs — multi-channel "Obsidian" work queue — SHIPPED (WO-773, landed at save schema v35; for the LIVE schema read `SaveSchema.CurrentVersion`).** `Assets/_Modules/Core/Jobs/`:
- `JobKind.cs` (Build/Upgrade/TowerBuild/TrainTroop/Research/…), `IJobEffect.cs` (per-job apply hook),
  `ObsidianQueueState.cs` (Builder/Train/Research channels + `ChannelId`), `ObsidianQueueEngine.cs` (offline-fair resolve).
- Persistence landed at schema **v35** (for the live schema read `SaveSchema.CurrentVersion`): `SaveMigrator.MigrateToV35` appends `ObsidianQueue` and folds
  legacy `BuildJobs`/`PendingBuilds`/`BuildingCooldowns` into the Builder channel (idempotent, no-loss).
- Surfaced by `Village/BuildMode/ObsidianQueueHud.cs` + `Village/Buildings/BuildTimerService.cs` (now the
  common multi-channel queue front). Player copy = "Builders"/"Training"/"Research", never "Obsidian queue".

**Troops foundation — SHIPPED.** `BarracksData` (`Assets/_Modules/Village/Troops/Data/BarracksData.cs`),
`TroopStatResolver` (`Assets/_Modules/Village/Troops/TroopStatResolver.cs`); data `Assets/Resources/Data/Canonical/barracks.json`,
`troop-upgrades.json`, `troops.json` (dual-copied to `StreamingAssets/Data/Canonical/`).

**Buildings/Progression — the upgrade + collector spine (2026-08-16).** `Assets/_Modules/Village/Buildings/Progression/`:
- `UpgradeFamilyResolver.cs` — the **ONE** decider of a structure's upgrade family. A bare catalog id
  resolving to `UpgradeFamily.None` is what made Manage tell the player a level-1 tower was "fully enhanced".
- `PlacedStructureUpgradeService.cs` — the **SINGLE** start path for placed-structure upgrades. Many
  doorways, one destination page (3D preview, truthful tiers, every `maxLevel > 1` structure).
- **`CollectorStackPropCatalog.cs` EXISTS** — log / flour sack / iron bar. Collectors no longer fall
  back to an abstract bar; do not re-add a generic fallback prop.

**Pets/Echoes — WO-993 retired the PHYSICAL pet stack (2026-08-16, commit `b63bc7190`).**
- **DELETED:** `Village/Pets/AuraController.cs` · `Pets/PetProgression.cs` · `Harvest/EchoSpiritPresentation.cs`.
- ⚠ **`PetTaskController` is NOT deleted — it is RETIRED IN PLACE** as a task-state holder, kept
  because `EchoEngageDialogueRegression` pins its shape by reflection. Its `Update` loop, `TickRepair`
  and `PetTaskInstaller` are gone, and **`SetTask(Repair)` now REFUSES LOUDLY**
  (`Assets/_Modules/Village/Pets/PetTaskController.cs:97`) — repair is passive and count-driven via
  `EchoRepairService`. Writing "PetTaskController deleted" would be a fresh falsehood.
- **`PetHeroLeash` STAYS** — it is what makes the wolf guide move.
- Appearance has ONE owner: **`EchoWorldPresence`** (escort → vanish → return once after battle), and
  `PetDeployer.DespawnEcho` (`Assets/_Modules/Pets/PetDeployer.cs:442`) is the **FIRST despawn path in
  the game**.

**IN FLIGHT *as of 2026-08-06* (present then, do NOT assert done — ⚠ dated snapshot, re-verify against
the board and the tree before acting):** `EnemyResolver` (`Assets/_Modules/Core/Enemies/EnemyResolver.cs`,
+ `Editor/Regression/EnemyResolverRegression.cs`, `Tests/PlayMode/EnemyResolverSpawnTests.cs`); the
barracks-catalog-structure (Barracks as an upgradable placeable building, PAIN_POINTS §3.3); the WO-774
raid-UX polish (loadout handoff / naming split / deploy ring / "Defenders %" copy / Train-queue UI).

---

## 1. INDEX TABLE — areas → section file → role

> STALE: 2026-07-12 — the docs-wo-state row's "next free WO = 412" is ~270 stale: WO specs on disk run through 683, next free = 684, with number collisions on 677/678 (see CANON_GROUND_TRUTH_2026-07-12.md)

| Area | Section file | 1-line role |
|---|---|---|
| **Core** | `docs/MASTER_CATALOG/core.md` | `DeNelle.Core` foundation: interfaces/enums/pure data, GameState + SaveSchema/Migrator persistence spine, SceneRouter, CoreServices registry, CanonicalJson loader, PanelManager, World/Catalog/Quests/Services/Web3 + the `DeNelle.AI` BT primitives. Refs nothing first-party. |
| **Village — Hero** | `docs/MASTER_CATALOG/village-hero.md` | Player hero (Blaise + class bodies): HeroLocomotion (NavMeshAgent), abilities Q/W/E/R, body swap, gear/equip (GearLoadout + EquipmentController), combat-feel/projectiles, SmartMobileCamera, input drivers, inventory/shop UI. |
| **Village — Systems** | `docs/MASTER_CATALOG/village-systems.md` | BuildMode (CREATE verb), Harvest (offline + worker), Tutorial/FTUE + DialogueService/CommandBridge, Arena async-PvP, world-space combat tells, EconomyService + building/upgrade progression. |
| **Village — NPCs** | `docs/MASTER_CATALOG/village-npcs.md` | StoryCompanions (party-of-4), join beats, castle hub injectors + interactables, ambient townsfolk + bubbles, HUD talk/party bridges, companion gear-up sub-beat. |
| **Village — Enemies/World** | `docs/MASTER_CATALOG/village-enemies-world.md` | Enemy/EnemyBrain/EnemyFactory, WaveManager loop, DragonBoss, RegionMobSpawner, the MERGED overworld (OuterWorld is DELETED — see §2b), ZoneManager seam, ward/tribe/settlement, camps/outposts/garrison raid loop, enemies.json/waves.json. |
| **HUD** | `docs/MASTER_CATALOG/hud.md` | `DeNelle.HUD` code-built uGUI town/combat HUD (`VillageHudController`, 3 canvases) + 12 Village→HUD push bridges + PanelManager modal arbiter + popups + diagnostics. |
| **Battle / ATB** | `docs/MASTER_CATALOG/battle-atb.md` | Turn-based Active-Time-Battle: deterministic pure-C# `Engine/` + runtime SO store (`ATBRuntimeState`) + scene `BattleController` + code-built `BattleHudUgui` + `AtbCombatantSwapper`. The breach/dungeon encounter combat. |
| **Dialogue** | `docs/MASTER_CATALOG/dialogue.md` | One shared Yarn runner: `DialogueService` + `DialogueCommandBridge` (~40 verbs) + ClassicRPG `CompanionDialoguePresenter`; intro cinematic bridge; 64 `.yarn` nodes; vendored Yarn addons. |
| **Audio** | `docs/MASTER_CATALOG/audio.md` | `DeNelle.Audio`: AudioService (A/B music crossfade + SFX pool + mixer), AudioBootstrap, MusicTrack registry, SfxClipLibrary, WebGL unlock, jukebox panel. |
| **Economy / Meta** | `docs/MASTER_CATALOG/economy-meta.md` | Pets, Wallet (Solana/SKR), Web3 (Jupiter swap), Cosmetics (Glimmer/BattlePass), PackStore monetization — all reflection-bridged off Village. |
| **Data catalogs** | `docs/MASTER_CATALOG/data-catalogs.md` | The `CanonicalJson` WebGL-safe loader + dual/triple-copy sync rule + ~30 typed catalog classes + every JSON catalog (abilities/enemies/buildings/gear/quests/pets/packs/themes…). |
| **Scenes** | `docs/MASTER_CATALOG/scenes.md` | 14 `Assets/Scenes/*.unity` + build-settings eligibility + the full boot/load-flow routing code (SceneRouter/WorldSceneLoader/HubScenes/SceneTransitionTrigger). |
| **DevTools / Settings / Onboarding** | `docs/MASTER_CATALOG/devtools-settings-onboarding.md` | Two dev panels (DevPanelController dev-only + AdminOverlay ships), Settings/Pause, OnboardingMode + flow + TitleController, DifficultyTuning, the two grant paths. |
| **Misc modules** | `docs/MASTER_CATALOG/misc-modules.md` | Dungeons (`DeNelle.Dungeons`: data-driven Healers Cottage + stub Granary, crafting, Bryn, lantern), Environment (torches/night lights), Data (`MasterAssetCatalog`), UI (`GameOverUI`). |
| **Editor tools** | `docs/MASTER_CATALOG/editor-tools.md` | `DeNelle.Editor` (reflection-only into Village): castle/outerworld/garrison scene builders, animator factories, build tools, QA gates (CompileGate/RegressionSuite), magenta material fixers. |
| **Resources / Art** | `docs/MASTER_CATALOG/resources-art.md` | `Resources.Load` path map (code → asset), Resources art folders (Heroes/Enemies/Structures/HudIcons/…), Assets/Art sources, art-consumer factories, gitignored model packs. |
| **Asset inventory (vendor packs)** | `docs/asset-inventory/README.md` | ★ Exhaustive map of ~21k meshes across vendor packs — most **GITIGNORED + previously uncatalogued** (gitignored ≠ invisible, owner caught the blind spot 2026-06-24). Three UNUSED shared-rig character libs (KayKit Adventurers/MM, Supercyan, + the Action clip lib), polyperfect/Quaternius env, ~1000 Mirza Beig/Spells VFX (only ~38 wired). What we own vs what actually ships (current hero = `Resources/Heroes/Knight.fbx` Tripo). 5 section docs. |
| **Docs — design** | `docs/MASTER_CATALOG/docs-design.md` | The `docs/**` design tree (137 md): canon/vision, narrative, engine-architecture specs, build-mode, combat/economy design, asset-pack notes, audits, port-notes, QA docs. |
| **Docs — WO state** | `docs/MASTER_CATALOG/docs-wo-state.md` | Repo-root governance + 438 work-order spec files + pipeline-state docs; numbering authority (next free WO = 412); current ground-truth state synthesis. |

---

## 2. ARCHITECTURE MAP

### 2a. Assembly / dependency graph (DeNelle.*)

Bounded-context assemblies (HP-B2B architecture law, `docs/ARCHITECTURE_PRINCIPLES.md`).
**Presentation is a separate layer that never touches the gameplay objects.** Core is the
shared spine; nothing references up; nothing references first-party from Core.

```
                         DeNelle.Core   (interfaces, enums, pure data, services,
                          ▲  ▲  ▲  ▲      SceneRouter, GameStateService, CanonicalJson,
                          │  │  │  │      PanelManager, CoreServices, HubScenes, World/
        ┌─────────────────┘  │  │  └──────────────────┐   Catalog/Quests; +DeNelle.AI BT)
        │            ┌───────┘  └────────┐            │
   DeNelle.Data   DeNelle.Village    DeNelle.HUD   DeNelle.BattleATB
   (typed         (Enemy, EnemyBrain, (VillageHud-  (ATB engine + store +
    catalog        WaveManager,        Controller,   BattleController +
    helpers)       HeartController,    +12 bridges   BattleHudUgui)
                   HeroLocomotion,     live HERE on  refs Core, Data
                   EconomyService,     the Village
                   buildings, bridges) side)
        │
   DeNelle.Pets · DeNelle.Wallet · DeNelle.Web3(→Wallet) · DeNelle.Cosmetics ·
   DeNelle.Onboarding · DeNelle.Dungeons · DeNelle.Audio · DeNelle.Settings ·
   DeNelle.DialogueUI(→Village) · DeNelle.DevTools(→Village,HUD,Wallet) · DeNelle.Editor
   (each → Core, some → Data)
```

**Cross-asmdef rules (BINDING — verified held in the section catalogs):**
- `DeNelle.Village → DeNelle.Core` only. `DeNelle.HUD → DeNelle.Core` only.
  **Never Village ↔ HUD directly, never HUD/BattleATB → Village.**
- Core → Village would be a **circular ref (CS0234)** — Core awards crystals by writing
  `GameState` directly (not `Village.CrystalEconomy`); damage attribution / XP go through
  the Core `XpEarnerRegistry` / `DamageAttribution` id-keyed registries, not direct calls.
- **HUD pushes from Village go IN via two seams:** the `IVillageHud` interface
  (`CoreServices.Hud`) for interface methods, and **reflection-by-name** on the concrete
  `VillageHudController` for the "extra" setters not on the interface (Talk/Party/Town/etc).
  The same reflection-across-the-boundary pattern is how HUD/BattleATB/Pets/Cosmetics read
  Village types (HeroLocomotion, WaveManager, Enemy, MineNode, GlimmerCurrencyService)
  without an asmdef ref. `CoreServices` slots: `Hud`, `Audio`, `Jupiter`, `WalletSigner`.
- **`DeNelle.Editor` deliberately does NOT ref Village** — every Village type is reached by
  `FindType` over AppDomain + reflection; all editor entries are menu/`-executeMethod` (no bootstrap).
- **`DeNelle.DevTools`** is the module-isolation EXCEPTION (tooling may ref gameplay) and is
  compiled OUT of release (`UNITY_EDITOR || DEVELOPMENT_BUILD`).
- BattleATB `Engine/` is **pure C#, no UnityEngine** (except the optional unused
  `CombatantDefSO`); deterministic mulberry32 RNG, golden-vector bit-parity tested.

### 2b. Scene boot / load flow (re-verified at source 2026-09-02)

> ⚠ **CORRECTED 2026-09-02.** The flow below previously narrated `MainCastle_Hall` as the home hub
> with `OuterWorld` streamed ADDITIVELY over it. Both halves were wrong and had been for months:
> `OuterWorld.unity` and `Village.unity` are **DELETED from the tree** (verified on disk — the only
> `.unity` files matching castle/village/outer are `MainCastle_Hall`, `Main_Castle_Overworld`,
> `Village2`, `CastleTest` and `Garrison_village2_stronghold`), and `WorldSceneLoader` is a
> **DEPRECATED NO-OP** whose own header says so
> (`Assets/_Modules/Village/World/WorldSceneLoader.cs:2,262-267`). A reader following the old diagram
> would go hunting for an additive stream that cannot happen.

```
Title (#0, boot scene; Core DDOL singletons spin up)
  |- Continue --------------> GoCastle() --> SceneRouter.Castle    (returning player, loads save)
  '- Play Intro / New game -> [StoryIntro cold-open]
                               in-Title hero pick -> GoPetSelect()

HeroSelect -- confirm -> GoPetSelect() -> PetSelect
   '- returning-player skip (hero+pet saved) -> GoCastle()

PetSelect -- confirm (writes StarterPetId, Save) -> GoCastle() --> SceneRouter.Castle

Main_Castle_Overworld (HOME HUB - castle AND overworld in ONE scene, ONE navmesh)
   |- DungeonEntrance / DungeonWorldPortalSpawner -> Dungeon_* (additive)
   |- RaidOutpostSystem -> 4 cardinal in-world EnemyOutposts (in-scene, no stream)
   '- raid access -> Garrison_* / RaidBase_* (ADDITIVE)

Village2 (TD town / raid target) - GoVillage() = LoadVillageWithLoader() (async overlay)

Breach (from Village2 / dungeon) -> GoBattle(BattleParams) -> ATBBattle -> returns to ReturnScene
```

- **Home hub = `Main_Castle_Overworld`** (WO-608 MergedWorld: the castle and the overworld are ONE
  scene on ONE navmesh). ⛔ **The hub scene name is NEVER spelled out at a call site** — it resolves
  through `SceneRouter.Castle`, which is FLAG-DEPENDENT:
  `Castle => FeatureFlags.MergedWorld ? CastleCandidates[0] : CastleCandidates[1]`, with
  `CastleCandidates = { "Main_Castle_Overworld", "MainCastle_Hall" }`
  (`Assets/_Modules/Core/SceneRouter.cs:150-167`). `FeatureFlags.MergedWorld` is
  `Get("mergedworld", defaultOn: true)` (`Assets/_Modules/Core/FeatureFlags.cs:413`), so the LIVE hub
  is the merged scene. **`CastleCandidates` is the only place either name is spelled out** — a gate
  that asserts against the RESOLVED value only proves whichever branch it happens to be flagged into,
  which is exactly how three gates ended up pinned to the retired `MainCastle_Hall` literal
  (WO-1112). Iterate the array; never retype a name.
- ⚠ **`Assets/Scenes/MainCastle_Hall.unity` IS STILL ON DISK and it is NOT the hub.** It is the
  LEGACY two-scene-hub file, reachable only with `ff.mergedworld` forced OFF. Its continued existence
  on disk is what keeps re-seeding stale "the hub is MainCastle_Hall" prose in this and other docs.
  Several runtime scene checks still name it *alongside* the merged scene as a deliberate
  both-configurations guard (`HubScenes.Names`, `AudioService.cs:982-983`,
  `StarterSettlementCompletion.cs:49-50`, `StrategicPlacementMigration.cs:371`) — those are the flag
  branch, not evidence that it is live.
- ⛔ **`OuterWorld` DOES NOT EXIST — do not go looking for the scene or its streaming.**
  `WorldSceneLoader` is retained only for compatibility and its `TryLoadOuterWorld` is a traced no-op
  (`WorldSceneLoader.cs:260-267`). The overworld predicate is `HubScenes.IsOverworld(sceneName)`,
  which is exactly `sceneName == "Main_Castle_Overworld"` (`Assets/_Modules/Core/HubScenes.cs:47-55`)
  — that is the single source every overworld-behaviour gate reads (encounter spawner, harvest
  workers, camps, raid outposts, world boundary).
- **`Village2`** = generated TD town / raid-target stronghold (canonical).
  **`Village.unity` is DELETED** — it is not "abandoned but present"; it is gone.
- `HubScenes.IsHub` is the single hub predicate, read by both `WorldSceneLoader` and
  `VillageHudController`. ⚠ **Its matching is SUBSTRING, not exact**
  (`sceneName.Contains(Names[i])`, `HubScenes.cs:37-43`), so `IsHub("Village2_Test")` is TRUE.
  Replacing a private `== "CastleHub"` list with this predicate is a WIDENING and must be a
  deliberate call at that site.
- Menu scenes (Title/HeroSelect/PetSelect/Intro/Store/...) are on the HUD bootstrap
  **allowlist-skip**; all other gameplay scenes auto-bootstrap a `VillageHudController`.
- **Defend-the-Tower / PatriciaLight = REMOVED 2026-06-09** (module + scene gone; only
  `Resources/PatriciaLight/tower2` kept). All DTT/PatriciaLight WOs are dead — but note
  `SceneRouter.PatriciaLight` and `GoPatriciaLight` are STILL DECLARED in
  `SceneRouter.cs:179` over a scene that does not exist.

### 2c. CRITICAL-PATH systems — where each lives + how it ACTUALLY works

(Verified from the section catalogs by reading source, NOT from code comments.)

- **Hero NAVIGATION — `Assets/_Modules/Village/Hero/HeroLocomotion.cs` (`DeNelle.Village`).**
  Despite a stale header claiming "no Rigidbody, no NavMeshAgent — pure transform," the code
  is the OPPOSITE: Awake() gets/adds a **`NavMeshAgent`** (radius 0.4, height 1.8,
  `updateRotation=false`, speed 30 so Move never caps), reads input → eased `Velocity` →
  **`_agent.Move(step)`** when on-mesh, else `transform.position += step` (off-mesh fallback);
  manual `LookRotation` for facing. So it is a NavMeshAgent **kinematically driven by input**
  (not pathfinding, not pure transform). Awake also OVERRIDES serialized move speeds.
  Input is camera-relative in follow (rotated by `SmartMobileCamera.CameraYaw`), world-absolute
  in top-down. Live mobile input = Village `VirtualJoystick` (HUD `VirtualDPadLean` is orphaned).
  `WarpTo` disables→warps→re-enables the agent (seam crossing). **Treat hero locomotion as
  agent-driven** — debug "can't move/exit" via NavMesh bakes, not colliders. The same trap
  recurs on `Pet.cs` (also a self-added NavMeshAgent) and `Enemy.cs` (NavMeshAgent, honestly
  documented). FTUE auto-walk + SceneTransitionTrigger.WarpTo depend on the agent.

- **Dialogue / Yarn option+command flow — `DeNelle.Village` DialogueService + DialogueCommandBridge.**
  Every Yarn conversation plays through ONE shared runner (`Resources/Dialogue/DialogueSystem.prefab`,
  ClassicRPG Canvas UI, code/Canvas not UXML → WebGL-safe). `DialogueService.Play/PlayStructure`
  hosts-or-reuses it and installs `DialogueCommandBridge` (~40 verbs: camera/audio/structure/
  movement/HUD/combat/pets/quests + the **vendor verbs OpenShop/OpenUpgrade/OpenCraft/OpenEquip/
  OpenArena/OpenRumorBoard**). Vendors, building Buy/Sell/upgrade, yes/no confirms ALL route here,
  NOT bespoke panels. **`NPCCommandBridge` is DEAD/neutralized** — its verbs were consolidated into
  DialogueCommandBridge because YarnSpinner's source generator throws on any action name registered
  twice (every name must register exactly ONCE project-wide). Gotcha: a Yarn **bare command-arg is
  literal** (`<<cmd $var>>` passes the string "$var"); stash in C# (`DialogueService.CurrentStructureId`)
  and read it back, or use `{$var}`. The single parameterized `StructureMenu` node drives all
  building interactions. `TalkHudBridge` gates `SetTalkAvailable`/routes `TalkRequested` to nearest NPC.

- **Economy / wallets — the SPLIT (`DeNelle.Village.EconomyService` vs `DeNelle.Core` GameState).**
  `EconomyService` (DDOL singleton) is a 4-resource wallet where **Wood/Iron live in an in-session
  pool** (shop + HUD bar read this) while **Food/Crystals read-through to `GameState.Resources`**
  (single source of truth). `CanAfford/TrySpend/Grant`. **Wood/Iron dual-wallet hazard:** the
  building-upgrade flow's `ResourceLedger` reads/spends **GameState.Wood/Iron**, which do NOT
  auto-sync with the pool — `GrantSpendable(w,f,i,c)` exists solely to write BOTH (both dev grant
  paths use it). Crystal stores: `GameState.Resources.Crystals` is canonical; `CrystalEconomy`
  is a separate singleton to verify-or-retire; `GameState.AetherCrystals` is DEPRECATED (folded
  into Resources.Crystals at save v18). Persistence spine: `GameState` (SO, 41 partialize fields)
  + `GameStateService` (Load/Save via PlayerPrefs `dotr-save` → migrate → validate → apply) +
  `SaveSchema` (**read `CurrentVersion` at `Assets/_Modules/Core/State/SaveSchema.cs`; never from a doc**) + `SaveMigrator` (v1 → that same top step, which is asserted equal to `CurrentVersion`). Resource model (memory): Wood/Iron/
  Food build structures; Crystals = special arc (unlock spells → jewelry → armor).

- **Companion / FTUE / introducer — `DeNelle.Village` StoryCompanion + `DeNelle.Onboarding`.**
  One unified roster = heroes ARE companions: Knight→Grom, Ranger→Sylas, Mage→Thrain, Cleric→Elara.
  `StoryCompanionInjector` (hub-gated DDOL) spawns ONE mortal body per persisted party member;
  companions follow+fight (leashed 22m, NavMeshAgent or lerp). Canon join order:
  **Sylas (beat-1) → Elara (wave 3) → Grom (first overworld return)** (all hub-gated, one-shot,
  substitute a different free class if it clashes with the player). The **canonical companion-intro
  is now a walk-up NPC** (`CastleCompanionIntroducerInjector`, owner 2026-06-12) at courtyard
  `(-4,0,-30)`; on Talk it plays Yarn `SylasFirstMeeting` (`<<RecruitCompanion Ranger>>`). The old
  `SylasFirstMeeting` auto-beat stands DOWN whenever that injector is `Active`. `PartyHudBridge`
  pushes StoryCompanions (real Hp/MaxHp) into HUD party slots 1..3. Vendors: `CastleVendorNpcInjector` places 8 static vendor
  NPCs, gated on EITHER castle scene by two consts — `TargetScene = "MainCastle_Hall"` and
  `MergedTargetScene = "Main_Castle_Overworld"` (`Village/NPCs/CastleVendorNpcInjector.cs:55-59`),
  i.e. it deliberately covers both `SceneRouter.CastleCandidates` branches rather than the live one
  only. `VillageNpcInjector` (exact `Village2`) places the 4 townsfolk. Note the gating
  inconsistency: vendors use exact-scene names, companions use `HubScenes`.

- **World camps / outposts / dungeons / garrisons — `DeNelle.Village.World(.Camps)` + `DeNelle.Dungeons`.**
  ⚠ **CORRECTED 2026-09-02: OuterWorld does NOT stream over anything — the scene is DELETED and
  `WorldSceneLoader` is a no-op (§2b). All world content is IN `Main_Castle_Overworld`; the predicate
  is `HubScenes.IsOverworld`.** **Two raid mechanisms, easy to conflate:**
  (a) `RaidOutpostSystem` spawns 4 cardinal `EnemyOutpost`s IN-WORLD, in the merged hub scene (no scene load;
  `_enabled` hardcoded ON; spawn delay cut 180s→10s 2026-06-11; header still says "ONE outpost"—STALE);
  (b) standalone `Garrison_*` SCENES loaded ADDITIVELY, driven by `GarrisonController` on `GarrisonRoot`
  (recipe-fed from `garrison-recipes.json`, 4 recipes). `CampSystem` adds 4 claimable camps (clear→
  claim→build outpost→defend); also flag-forced ON. Dungeons via `DungeonLayout`
  (`dungeons/healers-cottage.json`, 12 rooms — full data-driven) vs `Dungeon_FolksGranary` (STUB,
  no JSON); both enter the same ATB combat via `EncounterTrigger`→`SceneRouter.GoBattle`. Region/map
  data: `ZoneManager` (Core, static classifier; village ±42/±33, 4 cardinal regions Goldfields/
  Stoneback/Mirewood/Ashwood by danger tier); `realm-map.json` is StreamingAssets-only → WebGL-null.
  "Missing feature" in the world is usually a working system gated/delayed/region-excluded — check first.

- **Build / upgrade — `DeNelle.Village` BuildMode + Buildings/Progression.**
  Curated predefined catalog (Fallout-4 settlements model, NOT free-form), resource-gated, ~70% built
  end-to-end for towers: HUD Build → top-down cam + frozen waves → palette card → ghost → place →
  charge → persist to `GameState.BaseLayout` (save v14) → `BaseLayoutLoader` rebuilds on reload.
  `BuildButtonBridge` wires HUD `BuildRequested`→`BuildModeController.Toggle` by reflection;
  `BuildModeHudBridge` hides combat HUD while building. Resource-building upgrades (Farm/Lumbermill/
  Forge, 5 levels + Magic-gated Arcane Forge) use `ResourceBuildingProgression`/`State` (HARDCODED
  balance table) via `DialogueCommandBridge`'s `OpenUpgrade` — NOT the orphaned `*Upgrades.json` spec
  data. Buildings spawn their own front NPC (Talk routes to the NPC, dissolving "Talk: Windmill").

- **HUD / PanelManager modal discipline — `DeNelle.Core.UI.PanelManager` + `DeNelle.HUD`.**
  `PanelManager` is a pure-static single-modal arbiter: at most ONE registered panel open at a time
  (`Register(name, close, isOpen)` + `NotifyOpened` closes the prior). HelpMenu, AdminOverlay, and
  cosmetics/village/inventory popups all register + obey it; `MobileInteractButton` suppresses world
  prompts while a modal owns the screen. The HUD is `VillageHudController` (one code-built uGUI HUD;
  three nested canvases — base chrome 100 / Battle 150 / Town 140; context = scene Village2 AND hero
  within TownRadius 60 of origin). `BattleHudVisibilityManager` cross-fades BATTLE / TOWN / HIDDEN.
  **All HUD is code-built — UXML/UIDocument HUDs do NOT render in player builds** (project law; the
  reason onboarding/compass/battle HUDs were rewritten code-built).

---

## 3. STALE / RISK LEDGER (consolidated, prioritized)

> **2026-07-03:** the 07-02→03 convergence session touched ~50 systems (see
> `CANON_GROUND_TRUTH_2026-07-03.md`); per-area docs village-systems/resources-art have same-breath
> notes; full catalog refresh queued.

Every flag the 18 section agents raised, in one prioritized list.
**P1 = blocks/misleads work or breaks a platform · P2 = wrong behavior but contained ·
P3 = dead/stale, cleanup.**

### P1 — blockers / platform breakage / actively misleading

> ## ✅ RE-VERIFIED AT SOURCE 2026-09-02 — items 1, 3, 4, 5, 7, 8, 10, 11, 13 are RESOLVED
> Each verdict below was proved by opening the file, not by reading a status line. **Resolved rows are
> kept, not deleted** — a reader who half-remembers "the six catalogs are WebGL-broken" needs to find
> the row that says otherwise, and a deleted row cannot correct anyone. The rows that still bite are
> 2 (now unprovable as written), 6, 12 and 14.

1. **✅ RESOLVED (verified 2026-09-02). HeroLocomotion's comment no longer lies.**
   `Assets/_Modules/Village/Hero/HeroLocomotion.cs:4-7` now opens
   *"CORRECTED 2026-06-12 — the old header LIED: it claimed 'no NavMeshAgent — pure ...'"* and states
   the real model: a NavMeshAgent driven KINEMATICALLY by input, `Awake` gets-or-adds it with
   `updateRotation` off (agent field at `:701`). ⚠ **This row is the canonical example the whole
   catalog is built on — keep reading it as the METHOD** (verify at code, never at comment) even
   though this particular instance is fixed. The original text follows.
   ~~**HeroLocomotion comment LIES about the navigation model**~~ (village-hero §1, docs-wo §5a,
   editor-tools, scenes FLAG-2). Header + class XML-doc say "pure transform, no NavMeshAgent";
   the code is **NavMeshAgent + `_agent.Move` + `NavMesh.SamplePosition`**. A reader trusting
   the comment mis-diagnoses every hero-movement bug. Doubly dangerous: **RegressionSuite
   source-greps this very file** for the WO-387 camera-yaw basis — a stale comment can fool a
   source-grep gate. Fix the comment; treat nav as agent-driven. (Same class on `Pet.cs` line ~582
   "kinematic drift; NavMeshAgent wiring is the integrator's" — it self-wires the agent via WO-187.)

2. **⚠ UNPROVABLE AS WRITTEN — the scene it names no longer exists (flagged 2026-09-02).**
   `OuterWorld.unity` is DELETED and `WorldSceneLoader` is a no-op (§2b), so "the streamed open world"
   cannot be reproduced or profiled as described; the world is now in-scene in `Main_Castle_Overworld`.
   Whether the frame cost SURVIVED the merge is **not verified** — it needs a fresh profile capture on
   the merged hub, not a re-read. The two fixed per-frame costs and the `TryClericMend` alloc below
   are still real code facts. Original text:
   ~~**OuterWorld ~1 fps open blocker** (docs-wo §4).~~ Even at 0 enemies the streamed open world
   runs "frame by frame." Two provable per-frame costs fixed (`DefenseTower/ArcaneTower.Rescan()`
   whole-world `FindObjectsByType` every 0.4s → `4b5208c`; bridge scans → O(1) registries `463a5e8`),
   root cause UNPROVEN — awaiting owner profile verdict. Worse on mobile/WebGL (OOM risk).
   Related live per-cast alloc: `StoryCompanion.TryClericMend` still `FindObjectsByType` every heal.

3. **✅ RESOLVED (verified on disk 2026-09-02).** All six now have a Resources copy —
   `Assets/Resources/Data/Canonical/` contains `audio-mix.json`, `enemy-roles.json`, `heart.json`,
   `realm-map.json`, `towers.json` and `walls.json`, so `CanonicalJson.Read` no longer returns null for
   them on WebGL. ⚠ **The FAILURE CLASS is not resolved** — the two Canonical folders are NOT a mirror
   (115 `.json` under Resources vs 98 under StreamingAssets, counted 2026-09-02), so a *new*
   StreamingAssets-only catalog is WebGL-null the day it is added. Original text:
   ~~**6 StreamingAssets-only catalogs are WebGL-broken-by-omission** (data §FLAG-2):~~
   `enemy-roles`, `towers`, `walls`, `realm-map`, `heart`, `audio-mix` have NO Resources copy →
   `CanonicalJson.Read` returns `null` in WebGL (Resources miss + no filesystem). Exactly the
   failure class CanonicalJson exists to prevent. Mirror any needed in web to Resources.

4. **✅ CLOSED ON THE BOARD (verified 2026-09-02).**
   `WorkOrders/WORK_ORDER_408_texture_ship_audit.md:1` carries
   *"Status: CLOSED — owner range sweep 2026-08-21 (WO 0-800): completed or immaterial."* — so this is
   no longer an open gate in canon. ⚠ The 223 MB FIGURE itself was not re-measured; if web
   distribution comes back, measure the build, do not cite this row. Original text:
   ~~**WebGL ships at 223 MB (itch rejected).**~~ Fix = Gzip OR run **WO-408** texture-opt (scripts
   committed, **NOT run**). Blocks the web distribution build.

5. **✅ RESOLVED — the gate is dead and two of the four "blocked" WOs never existed
   (verified 2026-09-02).** `WORK_ORDER_405_ugui_design_system.md:8` = *"CLOSED — DEPRECATED,
   audit-verified obsolete (2026-08-21 backlog audit)"*; `WORK_ORDER_411_town_hud_mockup_match.md:4` =
   *"CLOSED — owner range sweep 2026-08-21"*; `WORK_ORDER_400*`, `403*` and `404*` **do not exist
   anywhere in the repo**. A row that blocks work on WOs that are not on disk is worse than no row.
   Original text:
   ~~**WO-405 `ElarionUiKit` design-system gate**~~ blocks all unified-HUD work (WO-400/403/404/411).
   WO-403 unified HUD is STASHED, to be redone modular (<800 lines). Owner-approval gate.

6. **GameAudioMixer is a stub, not the documented 5-group/5-param mixer** (audio §FLAG-1). The
   `.mixer` asset has ONLY a `Master` group, `m_ExposedParameters: []`. Every `SetFloat`/`FirstGroup`
   for Music/SFX/UI/Voice silently fails — only the AudioSource-direct fallback controls volume/mute.
   AudioMixerBridge + Settings sliders persist but don't drive the (absent) per-group mix. The
   documented mixer was never built into the asset.

7. **✅ RESOLVED, AND THIS ROW WAS ITSELF THE FAILURE IT WARNS ABOUT (2026-09-02).**
   The row's own remedy — *"never mint from filesystem max"* — is right, and then it **printed a
   number**, which is exactly how a stale number gets re-seeded from the doc that exists to prevent
   stale numbers. ⛔ **The sole authority is the `CLI_LANES_WO_NUMBERS.md` banner, and each seat bumps
   its OWN banner row in the SAME edit as the mint** (CLAUDE.md §2, two disjoint blocks). Neither
   `MASTER_PIPELINES_BACKLOG` nor this file nor any other copy is a number source. Original text,
   preserved only as the evidence:
   ~~**Numbering authority vs filesystem drift** (docs-wo §5d). Authoritative next-free WO = **412**~~
   (`MASTER_PIPELINES_BACKLOG` + `CLI_LANES_WO_NUMBERS`); 344–351 reserved (skip). PROJECT_INDEX/
   SESSION_START_HERE still say "next 384" — index lines lag. **Never mint from filesystem max**;
   30 WO numbers collide (docs-wo §2h) — renumber 391+. 438 WO files for ~280 distinct numbers.

8. **RESOLVED (verified from code 2026-07-13, WO-714 W8).** Settings/Pause are NO LONGER UXML-bound:
   both were rebuilt as code-built kit modals on 2026-07-03 (WO-F conversion, coverage rows #47/#47b —
   `ElarionUiKit.BuildObsidianModal`, FrameSettings/FrameOptions); `SettingsScreen.uxml` +
   `PauseOverlay.uxml` are DELETED from the tree (script GUIDs appear in no scene). The REAL residual
   gap — no scene placed the controllers and nothing called `PauseGate.RequestBack()`, so the panels
   were unreachable in-game — closed 2026-07-13 by `PauseHudBootstrap` (DeNelle.Settings): auto-installs
   PauseController+SettingsController per gameplay scene + the on-screen pause chip that calls
   RequestBack. (ATBBattle `BattleHUD.uxml`, dungeon panels, PromoCodeUI/InviteFriendsUI/
   WalletConnectDialog/JupiterSwapPanel remain the outstanding UXML-in-build risks.)

---

### P1 — ADDED 2026-08-21 (items 10-14). Verified at source the day they were written.

10. **✅ RESOLVED (verified with `git ls-files` 2026-09-02).** All four files and their `.meta`s are
    now TRACKED — `Core/Defense/DefenseReport.cs`, `Core/Defense/DefenseReportLedger.cs`,
    `Village/UI/Defense/DefenseReportPanel.cs`, `Village/UI/Defense/DefenseReportPanelBootstrap.cs`.
    A fresh clone of HEAD has them and compiles. ⚠ **Keep the LESSON:** the local tree proves nothing
    about what shipped, and an untracked file fails as a missing-namespace error that names none of
    the files responsible. Check `git status --porcelain` after any new-directory wave. Original text:
    ~~**⛔ THE SIEGE CLUSTER'S DATA MODEL AND ITS UI ARE UNTRACKED IN GIT.**~~ Verified with
    `git status --porcelain`: `Assets/_Modules/Core/Defense/` (`DefenseReport.cs` 576 +
    `DefenseReportLedger.cs` 159), `Assets/_Modules/Village/UI/Defense/` (`DefenseReportPanel.cs`
    621 + `DefenseReportPanelBootstrap.cs` 43), plus the `Core/Defense.meta`, `Village/Siege.meta`
    and `Village/UI/Defense.meta` folder metas, are **all `??` — never committed** — while the
    files that `using DeNelle.Core.Defense;` (`SiegeScheduler`, `SiegeSession`,
    `DefenseReportBuilder`, `SiegeClock`, `DefenseMapPlate`, `DefenseReportContractRegression`)
    **were committed** in `0bc68df71`. **A fresh clone of HEAD therefore does not compile**, and
    it fails as a missing-namespace error that names none of this. It works perfectly on this
    machine, which is the entire failure shape CLAUDE.md §16 exists about: the local tree proves
    nothing about what shipped. **Fix = commit the four files + the three folder metas by explicit
    path** (§11 sole-committer rule). Until then, treat any green gate on this machine as
    unrepresentative of the repo.
    ⚠ Also uncommitted and part of the same wave: the `DeNelle.EditorRegression.asmdef`
    modification the new oracles need, and the two deleted `.meta` files for
    `Environment/TorchFireController.cs` and `Village/Monetization/PurchaseGate.cs`.

11. **✅ RESOLVED (verified 2026-09-02) — and it was fixed by DELETING the duplicate, which is the
    right shape.** `CatalogBootstrap.RegisterFallback`
    (`Assets/_Modules/Village/Catalog/CatalogBootstrap.cs:403-410`) no longer hand-constructs three
    rows; it parses a **code-generated, byte-identical embedded copy** of the catalog
    (`CatalogFallbackData.g.cs`, `SourceRowCount = 28`), so all 28 rows are covered and there is no
    second hand-maintained table left to drift. The silent-3-row-game hole is closed. Original text:
    ~~**WO-1137 — `CatalogBootstrap.RegisterFallback` covers 3 of 28 catalog rows and has DRIFTED
    FOUR TIMES.**~~ Verified by count: `Assets/Resources/Data/Canonical/structures-catalog.json`
    holds **28 `entries`**; `RegisterFallback()` constructs **three** (`tower_ground_archer`,
    `tower_ballista`, `tower_arcane_spire`). If the JSON ever fails to load, the player does not
    get an error — **they get a silent, different, 3-row game**, with no tell on screen. That is
    the same shape as §16's missing-bundle trap: it installs, it launches, it plays, and only the
    owner's eyes can detect it. `BuildEconomyRegression` gate 12 (`[fallback-parity]`) now guards
    the three rows against divergence, which stops the drift but does **not** close the 25-row hole.
    The 2026-08-21 rescale had to fix **21 cost fields** in this table.

12. **WO-1138 — the hollow-pass ratchet inspects only a ~4-LINE WINDOW, so its coverage is a
    function of CODE FORMATTING.** A "hollow pass" is a regression case that returns GREEN while
    asserting nothing (`if (dependencyMissing) { notes.Add("SKIPPED..."); return; }` — the caller's
    only channel is the bool, so a skip IS a pass). `FindHollowPassLines` (RULE 4) caught **one**
    site in `CosmeticApplyRegression.cs`; manual review of the same file found **five more, all
    real**, invisible to the ratchet only because their guarding `if` sat further than four lines
    from the `return`. On 2026-08-21 alone hollow passes were found in **two** suites
    (`CosmeticApplyRegression` 6 sites, `RaidCooldownRegression` case 5 vacuous against a null
    fixture — found only because case 6 failed loudly for an unrelated reason and a human read it).
    ⛔ **This is the most expensive defect class in this repo** (memory
    `gates-report-success-without-proving-it`; §8's marker-not-exit-code law; §16). A gate that
    reports success without proving it does not merely miss a bug — it **actively asserts the bug is
    absent**, and work proceeds on that strength. Fix = match the CONTROL-FLOW relationship, not
    textual proximity.

13. **✅ RESOLVED (verified on disk 2026-09-02).** `Assets/Resources/Walls/Materials/` **now
    exists** and holds `wood_wall.mat`, `iron_wall.mat` and `steel_wall.mat` — one per
    `WallTier { Wood = 1, Iron = 2, ReinforcedSteel = 3 }` — alongside the tracked `Textures/` and the
    three FBXes. The tier ladder's art is reachable from TRACKED assets. Original text:
    ~~**WO-1135 — `Assets/Resources/Walls/Materials/` DOES NOT EXIST, and never has.**~~ All three wall
    tiers (`WallTier { Wood = 1, Iron = 2, ReinforcedSteel = 3 }`,
    `Assets/_Modules/Village/Walls/WallTierData.cs:29`) render from **materials embedded in each
    FBX**, which import with `externalObjects: {}` and bind their textures by **absolute path into a
    `.fbm` folder on the original author's machine** — a folder this repo does not contain.
    Verified on disk: `Assets/Resources/Walls/` holds `Textures/` and the FBXes only.
    ⚠ **This is NOT new breakage from the 2026-08-21 work** — it is pre-existing debt that the new
    `RaidWallMaterialRegression` made visible by failing on its first ever run. Do not go looking
    for what "broke" it. The tier ladder is a real gameplay + cost progression the player pays for,
    so the art must be reachable from TRACKED assets.

14. **WO-1136 — `staff_A` is geometrically symmetrical, so no sheathe orientation is derivable.**
    After the per-mesh derivation landed, 11 of 12 shipped meshes resolve. The twelfth measures
    identical at both ends to four decimal places on the taper test (relGap 0.001) and on the
    grip-proximity test (**relGap 0**). ⚠ **This is not a bug in the deriver** — the mesh genuinely
    does not encode which end is up, which is reasonable for a staff. It falls back to the global
    `_sheatheLongAxisSign` and says so in a `FlowTrace.Warn`. The recommendation on the ticket is to
    LOOK at it on device first, since a staff may have no upside down. **Do not "fix" this by
    flipping the global field** — that only moves the defect to the other heroes, which is the
    original WO-1123 defect restated.

---

### P2 — wrong behavior, contained

9. **✅ RESOLVED (verified in the data 2026-09-02).** All four aegis weapons in
   `Assets/Resources/Data/Canonical/weapons.json` now carry `"setId": "aegis"` — `aegis_emberbrand`
   (`:258`), `aegis_heartwood_longbow` (`:275`), `aegis_aetherstaff` (`:293`),
   `aegis_hallowed_censer` (`:311`) — so `WeaponDef.IsAegis` is true and `GearLoadout.AegisSetActive`
   is reachable. Original text:
   ~~**Aegis legendary set is UNREACHABLE** (village-hero §FLAGS): the 4 aegis WEAPONS in weapons.json
   have NO `setId`~~ (only `aegis_plate` armor does) → `WeaponDef.IsAegis` is FALSE for all → 
   `GearLoadout.AegisSetActive` (needs both) can never be true → the Oathweld ward + per-class Aegis
   weapon perk are dead. **Likely a data bug** — add `"setId":"aegis"` to the four aegis weapons.

10. **PARTLY RESOLVED (verified 2026-09-02) — the meshes ARE there; the cleric gap is real.**
    `Assets/Resources/Heroes/Props/Weapons/` now holds real `.fbx` meshes (axe_A, bow_A/B/C, dagger_A,
    hammer_A, shield_A, staff_A-D, sword_D/F/G, wand_A, `_tripobak_sword_A`) plus `sword_A.prefab` and
    a shared `Materials/weapons_bits_texture.mat` — not placeholders, so the tinted-primitive fallback
    is no longer the normal case. **STILL TRUE:** `Assets/Resources/Data/Canonical/abilities.json`
    declares only `mage` (`:6`), `knight` (`:84`) and `ranger` (`:166`) — there is no `cleric` class,
    so Cleric fires the Mage loadout (by design).

11. **ATB enemy model never varies** (battle F-SWAP-2): `AtbCombatantSwapper.ResolveEnemySlug()` is
    hard-coded `"Skeleton_Warrior"` despite a rich 7-entry `ENEMY_DEFS` + `EnemyControllerFor` map.

12. **ATB HUD always shows "WAVE 1"** (battle F-WAVE-1): `BattleHudUgui.Render` hard-codes the wave
    text though `BattleState.Wave` is real and scaled.

13. **ATB caster-vs-melee anim mis-pick** (battle F-CTRL-comment): `IsCasterHeroClass()` string-matches
    the DEV-fallback name `_fallbackHeroName` ("Blaise"), not the resolved hero → attack-vs-cast anim
    can pick wrong for the real selected hero.

14. **ATB idle auto-attack disabled** (battle F-MGR-1): `ATBCombatManager`'s 8s idle timer fires
    `onEnemyAutoAttack`/`onPlayerTurnStart` UnityEvents with **no listeners** → punitive auto-attack
    (WO-93) effectively off. **ATB control-mode toggle unbuilt** (F-CTL-1): engine `ControlMode`
    plumbing complete + tested but `HandleControlModeToggled`/`OnControlModeToggled` never invoked.

15. **Synthesised enemy stat divergence** (enemies-world §contradictory): open-world roster ids
    (orc-raider/caveman/feral-wolf/tiefling-cultist) exist ONLY as code EnemyDefs in THREE places
    (RegionMobSpawner/EnemyOutpost/GarrisonController) with **divergent stat blocks** for the same
    id (e.g. orc-raider hp 95 vs 170) — no single source until they land in enemies.json. Balance-drift hazard.

16. **DailyQuestHud is display-only** (HUD §FLAG-6) — no claim/reward dispense flow yet.
    `DailyQuestService.FeatureShipped` also returns false for harvesting/tower-build/cosmetic-shop/
    hero-talents, filtering out quest templates for features that DO exist (stale gate vs feature state).

17. **3rd stale gear copy** (data §FLAG-1): `Assets/Data/Canonical/{armor,weapons}.json` is loaded by
    nobody (`GearCatalog` reads the Resources copy via CanonicalJson) — drift hazard. `version` field
    missing on armor/weapons (data §FLAG-6) so a dropped-version hand-edit on gear won't be caught.

18. **CastleHubBuilder can't reproduce the owner's hand-dialed offsets** (docs-wo §5c, editor-tools)
    — a scene regen REVERTS owner's committed work. Don't regen MainCastle_Hall.

19. **Three unreconciled persistence stores in economy-meta** (economy §16): PackStore→GameStateService
    (unified save); GlimmerCurrencyService→PlayerPrefs `dotr-cosmetics-v1`; BattlePassManager→PlayerPrefs
    `BP_*`. Two cosmetic-ownership sources of truth (pack SKUs in GameState.OwnedItemIds vs Glimmer-shop
    in the PlayerPrefs blob) not reconciled. PetAcquisitionService active-slot assignment not persisted
    (only StarterPetId survives reload). `pet-skill-trees.json` over-specifies (11 trees, only 3 species
    have PetDefs + map to the enum).

20. **Append-only GameState fields not yet in SaveSchema** (core §append-only, enemies-world):
    `Tribes`, `Settlements`, `Wards`, `Arena`, `PetName` live in-memory per session but do NOT survive
    reload (deferred save-owner follow-up). `Zones/BaseLayout/PartyMemberIds/ArenaDefense` ARE wired.

21. **Arena SKR wager + seed data are stubs** (village-systems §stub): `ArenaWalletService` is a
    PlayerPrefs client stub (seed 500); ArenaCatalog (3 opponents)/ArenaDefenseCatalog (6)/
    DefensePatternLibrary are HARDCODED with `// TODO → *.json`. **SKR mint empty everywhere**
    (`WalletEndpoints.SkrMint* = ""`, `JupiterSwapService._skrMint = "REPLACE_..."`); Jupiter targets
    MAINNET while the wallet stack is DEVNET-only (unreconciled); swap signing is a stub that hard-fails
    in release. SOLANA_SDK off by default → all wallet ops run through the devnet StubWalletProvider.

### P3 — dead / stale / cleanup

22. **`HUDManager` does not exist** (HUD §FLAG-1) — yet `README.md` + the whole `README_HUD.md`
    describe it as shipped, and `VirtualDPadLean.cs` is orphaned by its no-op bootstrap. Live input is
    Village `VirtualJoystick`. Delete README_HUD.md + VirtualDPadLean.cs, or restore HUDManager.

23. **Dead BattleATB infrastructure** (battle §dead): `CombatantDefSO` family (no `.asset` instances),
    `ATBBackgroundController` (dormant orphan, `ATB/Video/*.mp4` unused), `Defs.ATB_BASE_FILL`/
    `AtbCombatantSwapper.HideOwnRenderer` (dead), and the scene's orphaned `_hudDocument`/UIDocument/
    `BattleHUD.uxml`/`BattlePanelSettings` (live HUD is code-built `BattleHudUgui`). `BattleSceneBuilder.cs`
    is STALE (re-wires the gone UXML path). README lists non-existent `BattleHud`/`BattleVfx` + wrong
    "FF7 blue" aesthetic (live is parchment/gilt).

24. **Dead/duplicate hero+village code**: legacy equip stack `HeroEquipment + EquipmentPanel`
    (hardcoded demo items — do not extend; route equip through GearLoadout). `HeroCinemachineRig`,
    `HeroChargeVFX`, `HeroAimIK` (no SetAimTarget caller), `HeroReachRing` (DEF-205 not-attached),
    `GearVisualApplier` (primitive cubes gated off), two victory-pose paths. `NPCCommandBridge` dead.
    `RegionMobSpawner.ModelForRoamer` unused. `WaveManager.BuildPlaceholderEnemy` legacy.

> STALE: 2026-07-12 — item 25's "Both build tools ship BuildOptions.Development" is false for WebGL: `WebGLBuild.cs:124` ships `BuildOptions.None` (Development is opt-in via `-devBuild`, WO-408); the DESKTOP Development flag remains (DesktopBuild.cs:178) (see CANON_GROUND_TRUTH_2026-07-12.md)

25. **DUPLICATE MenuItem `Defenders/Build/WebGL Player`** (editor-tools §dead) in both `WebGLBuild` and
    `DesktopBuild` with contradictory settings (Brotli/Development/512MB vs Gzip/None) — only one binds.
    Both build tools ship `BuildOptions.Development` for the "ship" path → DevTools leak into release.
    ⚠ **UPDATED 2026-09-02:** the second half of this row named scenes that no longer exist.
    `Village.unity` is **DELETED**, not "abandoned", and there is no `OuterWorld` scene to bake solo
    (§2b). Any editor tool that still opens either by name is now a hard failure rather than a risk —
    if `OuterWorldBuilder.BakeWorldNavMesh` / `SpawnPathVerifier` are still in the tree, they need
    re-pointing at `Main_Castle_Overworld` or deleting; that was NOT verified in this pass.
    Original text: ~~`OuterWorldBuilder.BakeWorldNavMesh` + `SpawnPathVerifier` both open the
    **abandoned `Village.unity`** (corruption-cursed) — stale/risky; use `OuterWorldNavBake`
    (OuterWorld-solo) instead.~~

26. **Audio dead/missing**: `SfxClipLibrary.asset` + `DeNelleAudioService.prefab` don't exist →
    `PlaySfxAtPosition(SfxId)` silent no-op, prefab bootstrap branch dead. Dungeon/GameOver/Overworld
    `world.mp3` clips absent (guarded silent). Two MusicTrack enums (Audio-side decl order vs Core-side
    explicit indices) — jukebox PlayerPrefs persists the Audio-side ordinal (reorder = shifted picks).

27. **Unbacked Resources.Load paths → silent null** (resources-art §unbacked): `Pets/*`, `Cosmetics/Pets/*`,
    `Cosmetics/Previews/*`, `HeroPortraits/*`, `Intro/intro-*`, `heart-wing`, `UI/panel_bg|menu_bg`, all
    `Sfx/*` except LookoutHorn → callers fall back (procedural pet/SFX, null portraits, solid fills).
    Shipped-icon typos: `HudIcons/Wizard/wiard.jpg`, `Wizard_Lightining.jpg`. `EnemyVfxSet_Default.asset`
    all arrays empty. `Resources/PatriciaLight/tower2` dead-art remnant. Fresh-clone "black village" =
    gitignored `Assets/Models` KayKit packs absent (Resources prefabs survive).

28. **Stale comment-only `UIDocument` tokens** (HUD §FLAG-3) in `CompassHud`/`CompassHudBootstrap` —
    code is pure uGUI now; comments narrate the retired UIDocument design (false grep flag). AdminOverlay
    dead-but-retained handlers + wallet auth path (`OwnerWalletAddress=""` never passes — chord-only by
    design). PauseController header says "new Input System" but body uses legacy `Input.GetKeyDown`.

29. **Orphaned spec-era data** (data §FLAG-3/4/5): `Upgrades/FarmUpgrades.json` + `WatchtowerUpgrades.json`
    referenced only by WO-237's unwired panel. `orientation-recipes.json` is JSONL not JSON (whole-file
    parse fails). `castle-south-recipe.json` bypasses CanonicalJson (editor-only, OK). Cross-root sync
    unenforced: 26 Resources vs 32 StreamingAssets canonical files, no cross-root diff test.

30. **Backend never deployed** (core §backend-dependent, economy): GameStateService delta-sync, EventTracker,
    PromoCodeService, ReferralService, LeaderboardService all target a Vercel URL that was never deployed.
    They run resilient (local-save-only, circuit-breaker, honest stubs) — pre-deploy stubs, NOT live bugs.
    `BackendAuthConfig.Enforced` off; ClanService is a pure PlayerPrefs stub.

31. **Stale code-banners (the comment-vs-code class, non-load-bearing)**: `SaveSchema.cs` banner says
    v10 (code v20); `SaveMigrator.cs` banner says "v1→v10 nine-step" (code v1→v20); `Theme.cs` banner
    says StreamingAssets/`forbids Resources.Load` (code uses CanonicalJson Resources-first);
    `ResourceType.cs` maps to the deprecated `AetherCrystals`; `PartyHudBridge` header says companions
    are immortal (code reads real Hp); the "no Player tag" comment recurs across NPC files while many
    now `FindWithTag("Player")` first. `RaidOutpostSystem` header says "ONE outpost"/180s (code = 4/10s).

32. **Stale docs** (docs-design §FLAGS, docs-wo §5b): "Avalon" town name + "Blaise" hero baked into
    v2-unity-port-spec + ~10 docs (live canon = **Elarion** + Thrain/Grom/Sylas/Elara). Pi-Network
    economy (PI_PITCH + NORTH_STAR line) superseded by Solana/$SKR. Heart-Tree premise → Cathedral Spire.
    Lantern motif → Stone Choir. `port-notes/week4-*` wire the abandoned `Village.unity`. Unity version
    `6000.4.7f1` → live `6000.4.8f1`. Most C/D/E/F engine docs are SPEC (designed, not built). `BUG_LIST.md`/
    `SESSION_START_HERE` Order Log are 05-31 snapshots; `ORCHESTRATION_PLAN.md` (05-28) do-not-use;
    `BACKLOG_SILOS`/`SILO_FILE_MIGRATION_MAP` describe a restructure that was SKIPPED. 376–382 missing
    from Notion; Notion 328–339 ≠ repo 328–339.

> **Trust rule (owner-mandated, docs-wo §4):** never mark a WO/fix DONE on a green gate alone —
> only the owner's playtest is the verdict. Don't patch-and-claim-fixed.
