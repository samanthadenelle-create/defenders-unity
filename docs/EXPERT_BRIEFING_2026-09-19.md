# Echoes of Elarion — briefing for an expert AI

**Date:** 2026-09-19. **Audience:** an outside model that has never seen this repo.  
**Studio:** DeNelle. **Owner / PO:** Samantha. **Working title in-game:** Echoes of Elarion (never “Avalon”). Tagline: *Echoes of a Forgotten Civilization*. Heart of Elarion is the reliquary at world centre.

This is a **Unity 6 (URP)** game: town-builder + wave defense + hero combat + raid/capture. It ships as **Android (Seeker tester)** and a **WebGL** build (currently parked). Art for enemies/structures is **not in the APK**; it is served from an R2 CDN (`Remote.LoadPath`). Missing a content push looks like tinted capsules with **no on-screen error**.

Judge claims by **code and measured logs**, never by comments. Comments in this tree have been wrong (e.g. a “pure transform” header on a `NavMeshAgent`).

---

## 1. What the player is doing (the loops)

There is one home, then several loops that return to it.

### Hub (peace)
Scene: `Main_Castle_Overworld` (merged world, one navmesh). **Not** `MainCastle_Hall.unity` (legacy file still on disk). `Village.unity` / `OuterWorld.unity` are deleted.

The bar the player touches is the **adaptive peaceful dock** (Build / Talk / Hero / Journey / Manage), not the old `HudActionBarModel`. Presentation never references Village types (`DeNelle.HUD` ↛ `DeNelle.Village`).

**Build:** place/upgrade/move/sell structures via `BuildModeController` + catalog. Timed work is one **Obsidian** queue (Builder / Train / Research) with a depth cap, not “more concurrent builders.” First placements of some types are free; after that you pay production.

**Talk:** NPCs / vendors (Weaponsmith, Armorer, Store, etc.).

**Hero:** loadout, abilities, bag. One hero, tag `Player`. No companion combatants. Armor does **not** mesh-swap the body (canon: 2D plate + title + stats in the shop). Weapons do change the hand.

**Journey:** Realm Map (the public door). Map is **not** a dock face.

**Manage:** unified queues/upgrades screen.

### Defend (town waves)
`WaveManager` generates rosters (`waves.json` batch arrays are inert). Enemies find the hero by **component**, never by a `SpawnPoint` tag (that tag **does not exist**; `FindGameObjectsWithTag("SpawnPoint")` throws). Markers are `WaveSpawnPoint` (GateIndex 0–3).

Clearing a wave pays build resources. Echoes (pets, HUD name) add a **passive harvest match bonus** and a **count-driven repair rate** — never a lock, never an assignment.

### Raid (the prize)
Journey → pick a camp → **raid deploy** (army loadout, “Change Army” door exists in tree) → `RaidBase_*` scene → deploy troops, breach, rally, hero abilities. 3-star Iron Bastion **captures** the camp (`RaidVictoryController` → `SceneRouter.GoOwnedTown()` → `OwnedTown_IronBastion`).

### Owned town (post-capture)
WO-1872: captured yard loads **destroyed** (rubble + salvage). Heart stays. The **rebuild UI is a second town stack** (`OwnedTownPanel` modal). Ruling: reuse castle HUD/build with a castle | player-base flag (`BuildModeController.IsOwnedTown` already exists; owned `SelectStructure` still bounces to the modal). WO-1876 is READY, not shipped.

### Dungeons
3D dungeon loop (keys, oil/torch, traps, payouts, recipe unlock gate). Separate from raid.

### Army
Train at barracks. Active roster vs **reserve** (save v42). First raid is a soft gate (3 deployable slots until `everCompletedRaid`). Veterancy on 3-star clears.

### Store / money
Two **mutually exclusive** storefronts in one artifact:

- Non-Play: Night Market, priced in **SKR** (and Pi / Solana rails in `DeNelle.Wallet`).
- Google Play: `DeNelle.GooglePlay` only (`defineConstraints: GOOGLE_PLAY`). Village must never reference Wallet.

Real money has settled (small). Organic buyers are ~0. Promo/guest redeem exists. Ads: rewarded, low volume.

### Circle (guild)
Implemented screen (form/join, members, vault, ballots). **Fresh session reads “Could not reach the server”** (WO-1875 READY): boot never mints a wallet session, every read is non-minting.

---

## 2. What is in the current demo (device)

Tester APK **`2026.09.18.375785`** on the Seeker (`com.denellestudios.echoesofelarion`). Public store build is older (`2026.08.17.328845` last anchored; do not treat store as this tree).

**Playable on that build**

- Boot → hub, peaceful dock, walk the castle, build/upgrade, wave defense, hero combat (melee primary; class Q is locked; W/E/R loadout).
- Raid a camp (Broken Garrison dress is what most raid screenshots show: dirt yard, wooden siege towers, grey ring).
- Army deploy / Change Army on raid deploy (code in tree; felt-frame not closed).
- 3-star Bastion **capture** → owned town **as rubble** (WO-1869/1872).
- Armorer / Village Shop (buy with gold). Preview today is a **generic shield glyph**, not the item’s 2D plate (WO-1877 READY).
- Night Market (wallet-gated SKR).
- Circle screen exists; **sign-in is dead** without a session.

**On screen, not yet the intended fantasy**

- Raid sky: two grey puffs, **no ground fog, no thunderstorm** (WO-1868 READY, bounced on a opened PNG).
- Grey **targetable outer wall** around the raid yard; owner wants **landscape**, not a shootable box (WO-1878 READY). Iron Bastion `towers[]` is **spire-only** so “archer + wizard, fully upgraded, forced encounters, wow VFX” is not what Extreme ships.
- Owned-town rebuild is the Obsidian **modal**, not the castle dock.
- Tutorial **`founding_walk`**: 30d **100 enter / 33 complete (33%)**. Watchdog auto-drops at 120s. Skip is authored and unused.

---

## 3. Architecture (so you do not fight the grain)

**HP B2B lens:** bounded context per component; **presentation never touches the objects**; queue player-felt work, never smuggle structural refactors into a felt ticket; right over easy.

**Assemblies:** ~25 `.asmdef` under `_Modules`. Hard invariant: **HUD never references Village** (either direction). Cross-module via `CoreServices` with `?.`. Village never references Wallet (Play packaging gate).

**Data:** canonical JSON in `Assets/Resources/Data/Canonical/` **and** `StreamingAssets` twins. Catalogs drive structures, waves, tutorial, dialogues, packs. Dual-write when a path string changes.

**Save:** `SaveSchema.CurrentVersion = 42` (army reserve). Neon cloud save uses the same migrate→validate→apply path as local. Anonymous players share id `"anonymous"` and are **never** a person in metrics.

**Scenes:** never hand-edit `*.unity`. Rebuild via editor builders / batchmode with the **Unity editor closed**.

**Content:** Addressables on R2. Every content build **re-hashes bundle names**. Push parent `ServerData`, verify `ServerData/Android`. Judge `R2_PARITY_OK` on a **fresh** log.

**Gates (markers, never process exit codes):** `COMPILE_GATE_OK`, `REGRESSION_OK n/n`, `UI_CAPTURE_OK`, `R2_PARITY_OK`.

**Instrumentation:** `FlowTrace` / `Guard` stay in the tree forever. F8 device watcher was **retired** 2026-09-18.

---

## 4. Combat (what “works” means)

North star (2026-06-22): **one hero**, no companion fighters, no Blink armor mesh-swap. Combat is a **composed tableau** (ATB-adjacent / staged), not a MOBA camera. Town/raid also has real-time hero attack + troop deploy.

Ranger Q / primary-input is **contested** (WO-1504 open): prose said melee primary for every class; code still fires a ranged primary when Q is a long-range strike. Do not “fix” without an owner ruling.

---

## 5. Live ops / analytics (as of 2026-09-19 UTC)

Command center: `https://defenders-of-the-realm-v2.vercel.app/api/admin/console` (key-gated reads). Events in Neon `analytics_events`.

**Identity bug (WO-1879, patched in tree):** `web_trace` rows use `player_id = X-Trace-Session` (WebGL log-batch UUID). 446 such ids / 30d, **0** overlap with `session_start` or play. Pulse (DAU from `session_start`) was already clean: **50 / 141 / 200**. New-players / total-ids were inflated (770→324 and 781→335 once the filter is on the **deployed** API).

**Funnel cliff (WO-1881):** `founding_walk` 33% complete; drops sit at idle **120.0s** (watchdog). Code now completes if the hero walked ≥8m when the watchdog fires. **Not proven on real guests.** No `wave_started` in the wild until a client with WO-1880 ships.

**Returners:** 19 people with qualifying play on ≥2 days; 0 stalled. Everyone else is one-and-done or phantom.

**Error firehose (17–18 Sep):** Jeweler untextured albedo (86 ids), Addressables R2 404s, RaidArt grey `ArenaBoundary` slabs.

72-hour watch runbook: `docs/FUNNEL_72H_WATCH.md`.

---

## 6. What is implemented vs what is the demo

| Loop | Implemented in tree | In the 375785 demo as intended |
|---|---|---|
| Hub + dock + build + waves | Yes | Yes |
| Hero combat + abilities | Yes | Yes (primary-input dispute) |
| Army train / raid deploy | Yes | Mostly; Change Army felt-frame open |
| Raid fight | Yes | Yes, presentation short (fog, landscape, Extreme identity) |
| Capture → destroyed yard | Yes | Yes |
| Rebuild captured town with castle HUD | Flag exists; modal still the door | **No** |
| Armorer 2D plates | Icons on disk; shop throws them away for a glyph | **No** |
| Night Market SKR | Yes | Yes if wallet session exists |
| Circle | UI yes | Sign-in **No** |
| Tutorial walk | Yes, watchdog drops 2/3 | **No** (33%) |
| Cloud save / schema v42 | Yes | Yes |
| Google Play vs Wallet isolation | Yes (compile-time) | Play artifact is a separate ship |

---

## 7. How to work on this (if you are the expert)

- Do not guess. Cite a file:line or a measured log line. “Probably” is not a claim.
- Do not hand-edit `.unity`. Do not strip `FlowTrace`.
- Do not invent a third town UI or a second HUD kit.
- Armor: 2D image + title + stats. Never mesh-swap the body.
- Enemy/building art missing on device → R2 push, not a shader theory.
- WO numbers only from `CLI_LANES_WO_NUMBERS.md` (next free was **1882** after 1879–1881).
- Sole git committer is the CLI seat; push only on the owner’s word.

**Open READY (prize / growth), not an exhaustive board:** WO-1868 raid fog/storm; WO-1875 Circle session; WO-1876 owned-town uses castle HUD; WO-1877 Armorer 2D plates; WO-1878 Iron Bastion Extreme identity; WO-1879–1881 funnel (in tree, A needs API deploy, B/C need a client).

---

## 8. One-line product truth

A real Unity game with a working hub, waves, raid, and capture — whose **onboarding dies at “walk to the gate,”** whose **dashboard was 75% WebGL log sessions,** and whose **hardest raid does not yet look or fight like the hardest raid.**
