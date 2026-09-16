# Raid difficulty levers — what is tunable, from where, and what was changed today (2026-09-16)

**Status:** current as of 2026-09-16. Every `file:line` below was opened at source on **2026-09-16**;
every live value was fetched on **2026-09-16**. Nothing here is copied from an earlier doc without
re-reading it. Read with `docs/RAID_BALANCE_AUDIT_2026-09-06.md` (the measurement),
`docs/PROGRAM_RAID_ECONOMY_2026-09-04.md` (the ruling map) and
`WorkOrders/WORK_ORDER_1763_raid_difficulty_remote_tunables.md` (the open ticket).

⛔ **No count, version or live value is restated in this doc as a fact to trust.** Where a number
matters it is given with the file and line it was read from, so the authority stays the code.

---

## 1. The Iron Bastion is a clone of the Veiled Enclave on every difficulty field

`Assets/Resources/Data/Canonical/scene-configs.json` — `mage_enclave` at `:230`, `iron_bastion` at
`:304`. The `Assets/StreamingAssets/Data/Canonical/scene-configs.json` twin is **byte-identical**
(same MD5, both files hashed 2026-09-16), so there is one authored value per field, not two.

**Fields that match exactly:** `difficulty` (`Extreme`), `wallTier` (`ReinforcedSteel`), `baseRadius`,
`wallSegmentsPerSide`, `centralBuilding` (`tower_arcane_spire`), `towers[]` (x2), `props`,
`garrison.composition` (hollow-acolyte x7 + orc-shaman x5 + hollow-warrior x7),
`garrison.baseEnemyLevel`, `garrison.difficultyMultiplier`, `garrison.boss` (`necromancer`),
`garrison.levelOffset`, `recommendedClearTime`, `twoStarTime`, `oneStarTime`, `rewardMultiplier`,
`raidCooldownSeconds`, `entranceCount`, `interiorWallLayers`, `towerPlacementStyle`,
`archerTowerCount`, `mageTowerCount`, `eliteCount`, `shardDropChance`, `unlockVictories`, `themeColor`.

**Fields that differ:** `id`, `displayName`, `description`, `sceneName`, `faction` (`hollow` vs
`mixed`) and `raidDress` — **present on `mage_enclave`, absent on `iron_bastion`**.

Two notes on the differences, because neither is a difficulty stat but one is felt:
- `faction` is inert on this path. `RaidGarrisonSpawner.cs:218` spawns the garrison "Hostile by
  default — no faction code."
- `raidDress` is the **bake-time** dressing block (WO-1608). The Enclave's rows include
  `"cover": true` props (pillars, rubble, crates, chests). The Bastion authors none, so the top tier
  ships the arena with **no authored cover** — a felt difference in the player's favour that no stat
  field records.

`docs/RAID_BALANCE_AUDIT_2026-09-06.md:181` already recorded the clone ("same composition as the
Enclave, 19"), so this is a re-confirmation, not a new finding.

---

## 2. Two layers of knobs, and they need different ceremony

### 2a. RUNTIME — read from the catalog on raid entry, no bake

`Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs`:

| Knob | Where it is read / applied |
|---|---|
| `garrison.composition[]` | flattened by `ExpandComposition` (`:240-281`) |
| `garrison.baseEnemyLevel`, `garrison.levelOffset` | `:166` — `enemyLevel = Max(baseEnemyLevel, playerLevel + levelOffset)` |
| `garrison.difficultyMultiplier` | `:167`, then folded at `FoldDifficulty` (`:435-440`) into `Hp` **and** `ContactDamage` — the ONE place it touches combat |
| `eliteCount` (on `SceneConfigDef`, not the garrison block) | `:249-278` inside `ExpandComposition` (`:240-`) — appends that many copies of the heaviest composition id, else the boss id |
| boss multipliers | `bossHpMult = 3f` (`:64`), `bossDamageMult = 1.5f` (`:66`), applied at `:295-296` on top of everything else |
| live cap | `liveCombatantCap = 36` (`:61`); the spawn budget is `cap - alive` (`:220`) |

Level curve: `GarrisonStatBlocks.ApplyLevelScale` (`Assets/_Modules/Village/World/Camps/GarrisonStatBlocks.cs:110-127`)
— `hpScale = 1 + 0.08*(level-1)`; damage scales `+0.04` per level for the first ten levels over 1 and
`+0.02` for the next ten, i.e. a **deliberate damage ceiling** while HP keeps climbing (reasoning
in-code at `:115-117`).

`GarrisonStatBlocks.GlobalDifficultyMult = 1.2f` (`:38`) is a **compiled const** folded into HP and
contact damage in every garrison builder — `BuildTrollDef` (`:65`, `:67`), `BuildStonebellyDef`
(`:97`, `:99`) and the shared `FromTable` (`:249`, `:251`) that the `BuildTypedDef` switch
(`:135-155`) dispatches most ids to. `RaidGarrisonSpawner.cs:290` reaches it through `BuildTypedDef`.
It is a real difficulty lever, it is **global**, and it needs a build — so it is the wrong instrument
for a per-camp ruling. Named here so no seat "also fixes" it.

### 2b. BAKE-TIME — requires a scene regen and a nav bake

`Assets/Editor/WallTools/RaidBaseGenerator.cs`:

- `TierFor(string difficulty)` (`:373-381`) maps `Regular` / `Hard` / `Extreme` to a `SpireHp` and a
  `TowerDpsBudget`. Destroying the spire wins the raid (`:27-28`).
- Tower **count** = `archerTowerCount + mageTowerCount` (`:1134-1135`, asserted at `:1554`).
- `wallTier` drives the wall art through `ParseTier(def.wallTier, …)` (`:718`) →
  `WallTierData.Get(tier).SegmentPrefabPath` (`:2193`).

**Ceilings that exist today, read at source:**
- **No tier above `Extreme`** — `TierFor` returns `Regular` for anything it does not recognise
  (`:380`), so a new string does not create a harder tier; it silently creates the easiest one.
- **Tower damage scale is clamped to ≤ 1** — `k = budget / worstRawDps` only when the worst arena
  point exceeds the budget, else `k = 1f` (`:1227-1229`). A turret is never made *stronger* than its
  catalog row authors.
- **`liveCombatantCap = 36`** (`RaidGarrisonSpawner.cs:61`) caps concurrent defenders.
- **`WallTier` tops at `ReinforcedSteel`** — the enum has exactly three members
  (`Assets/_Modules/Village/Walls/WallTierData.cs:30-35`), and the top two camps already author it.

Changing any 2b value needs `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes` **then**
`DeNelle.Editor.RaidNavBake.BakeAll`, in that order — per
`WorkOrders/WORK_ORDER_1732_top_raid_tier_never_regenerated_by_build_all_raid_scenes.RESULT.md:152-155`
and the generator's own header (`:58-60`: "this builder does NOT bake one - RaidNavBake is the ONE
baker for these scenes … Run it AFTER this").

---

## 3. What can actually change without a build, today

### The tunables rail is LIVE

- Server: `api/client-tunables.js` (public read, no auth), edge-cached
  `s-maxage=10, stale-while-revalidate=30` (`:63`), over the Neon `client_tunables` table.
- Operator lever: `tools/client-tunables.mjs` (`set` / `clear` / `list`; markers
  `TUNABLES_SET_OK` / `TUNABLES_CLEAR_OK` / `TUNABLES_LIST_OK`, `:32`, `:174`).
- Client: `Assets/_Modules/Core/Ops/RemoteTunablesService.cs` — endpoint `/api/client-tunables`
  (`:95`), polled every `PollSeconds = 30` (`:107`, `:234`).

**Proven live 2026-09-16** by a `GET` against
`https://defenders-of-the-realm-v2.vercel.app/api/client-tunables`, which answered
`{"ok":true,...,"readOk":true,"reason":"OK",...}`, whose `values` object included
`"raid.honorThirdStarSeconds":"60"`.

**But no `raid.*` key is a difficulty knob.** Grepped `"raid.` over
`Assets/_Modules/Core/Ops/RemoteTunables.cs` (key consts spanning `:414-725`): every registered key
is loot (`lootWoodBase` … `lootRepeatClearPct`, `cacheCapPerResource`), army (`starterArmySize`),
heartfire (`heartfireMaxCharges`, `heartfireRegenSeconds`), staging (`stagingCeilingSeconds`), honor
(`honorThirdStarSeconds`, `honorSecondStarSeconds`, `honorSecondStarMinDestructionPct`) or rough
stone (`roughStoneMinTier`, `roughStonePerDayCap`). **Nothing touches
`difficultyMultiplier`, `levelOffset`, `eliteCount`, composition or any bake-time value.**

### The remote CATALOG seam is compiled in but DORMANT

`Assets/_Modules/Core/Data/RemoteCatalogService.cs` (WO-1331) can serve allowlisted canonical JSON
from the database instead of the compiled copy. It is **off**, on three independent counts:

1. **Flag default false.** `FeatureFlags.RemoteCatalogs => Get("catalogremote", defaultOn: false)`
   (`Assets/_Modules/Core/FeatureFlags.cs:1423`).
2. **The endpoint does not exist.** `GET /api/client-catalogs` on the live deployment returned
   **HTTP 404** (curled 2026-09-16).
3. **The arming key is unregistered.** `RailKey = "catalog.remoteEnabled"`
   (`RemoteCatalogService.cs:148`); grepping `catalog.remoteEnabled` across `Assets/_Modules/Core/`
   hits only that file and the `FeatureFlags` doc comment (`:1422`) — never
   `RemoteTunables.cs`, so the registry has no such spec and the rail cannot arm it. The service's
   own header (`:28-33`) records that the registry edit was deliberately left out of WO-1331.

**And the allowlist would not cover difficulty anyway.**
`Assets/_Modules/Core/Data/RemoteCatalogOverrides.cs` — `Allowlist` declared at `:138`, entries
`:140-144` — lists exactly
`Data/Canonical/enemies.json`, `waves.json`, `echoes-balance.json`, `kill-rewards.json`,
`siege-stakes.json`. **`scene-configs.json` and the wall data are NOT in it.**

### Addressables / R2 carry art only

The remote content on R2 is art. `EnemyFactory` builds the body through
`VisualFactory.Skin(..., SkinOptions.Enemy(height))` (`Assets/_Modules/Village/Enemies/EnemyFactory.cs:206`,
`:263`); `SkinOptions.Enemy` sets `StripColliders = true`
(`Assets/_Modules/Village/VisualFactory.cs:111-112`) and `VisualFactory.cs:368-370` destroys every
`Collider` on the instantiated art. (Rigidbody stripping at `EnemyFactory.cs:936-937` is the *weapon
prop* path, not the body.) Stats come from the `EnemyDef` the code built —
`EnemyFactory.Build(EnemyDef def, …)` (`:32`) — never from the prefab. **So no art push can change
difficulty, in either direction.**

---

## 4. The one difficulty-adjacent change made today

`raid.honorThirdStarSeconds` was set to **60** on the live rail (build default **90**,
`RemoteTunables.cs:663`). Reported by the lead as `node tools/client-tunables.mjs set
raid.honorThirdStarSeconds 60 --by cli-lead` at 13:42 CDT, marker `TUNABLES_SET_OK`.

**Proven here:** the live `GET` (§3, 2026-09-16) returns `"raid.honorThirdStarSeconds":"60"`.

**It is GLOBAL to all four camps** — one key, no per-camp suffix (`:672`).

⚠ **This is not only presentation, and that is the part to carry forward.**
`docs/PROD022_TUNABLE_FLAGS.md:86` (its row 47) states it, and the code confirms it:
`RaidScoring.Finalize` computes `int stars = Mathf.Min(cappedStars, honorStars)`
(`Assets/_Modules/Village/Troops/RaidScoring.cs:1795`) and that clamp is what lands in
`RaidResult.Stars` (`:1806`). So lowering T3 lowers the settled star count of a slow raid, which
lowers **what it pays**.

⚠ **Second-order consequence, read at source today and worth a ruling:** the **same** clamped number
is the capture gate. `RaidVictoryController.cs:305-308` takes `_victoryStars = result.Stars` and
requires `>= OwnedBaseProgression.CaptureStarsRequired`, which is `3`
(`Assets/_Modules/Core/State/OwnedBaseProgression.cs:20`, with `FinalRaidId = "iron_bastion"` at
`:11`); `OwnedBaseProgression.TryCapture` re-checks it at `:31-32`. **A Bastion clear slower than the
T3 window can no longer capture the town** — so setting T3 to 60 s tightened the capture window on
the top tier as a side effect, not just the payout.

**Undo:** `node tools/client-tunables.mjs clear raid.honorThirdStarSeconds` — deleting the row is the
table's documented resting state.

---

## 5. The per-camp plan: WO-1763 (READY TO IMPLEMENT)

`WorkOrders/WORK_ORDER_1763_raid_difficulty_remote_tunables.md` puts **per-camp difficulty** on the
existing rail. In brief:

- **Eight new int keys**: `raid.difficultyMultPct{Camp1,Camp2,Camp3,Bastion}` (default `100`) and
  `raid.levelOffset{Camp1,Camp2,Camp3,Bastion}` (default `-999`, a sentinel meaning "use the JSON").
  The percent is **multiplicative** on the JSON `difficultyMultiplier`; the offset **REPLACES** the JSON
  `levelOffset`. A `levelOffsetDelta` variant was considered and explicitly rejected, because the
  owner's seed was ruled against REPLACE semantics.
- **A new pure consumer** `RaidDifficultyTunables`, modelled on `RaidLootTunables`, owning the clamps
  (25..400 / -5..+20) and the unknown-camp fallback; wired at `RaidGarrisonSpawner.cs:166-167` and
  reported on the existing single `RAID START` line — no second log line.
- **Registration is six coordinated places per key family** (`RemoteTunables.cs`,
  `api/_lib/tunables.js`, `docs/PROD022_TUNABLE_FLAGS.md`, `RemoteTunablesDefaultsRegression.cs`,
  `api/_lib/tunable-manifest.js`, the generated manifest JSON). Skipping `api/_lib/tunables.js` ships
  a **dead knob**: `readTunables` discards any key not in `TUNABLE_KEYS`.
- **Identity invariant:** at the compiled defaults, and with an empty table / offline / 404 /
  malformed payload, the raid is bit-identical to today. Only a DB row changes the feel.
- **Out of scope by name:** `scene-configs.json` values, `RaidBaseGenerator.TierFor`,
  `liveCombatantCap`, `GlobalDifficultyMult`, any `.unity` file, and `raid.eliteCount<Camp>`
  (deferred — it interacts with the live cap and that clip path is unread).

**Owner ruling 2026-09-16, recorded in the WO §2.5:** `difficultyMultPctBastion = 160` and
`levelOffsetBastion = 5` (REPLACE); the other three camps stay at identity with **no rows**. Against
today's JSON that is effective difficulty `1.3 x 1.60 = 2.08` and `enemyLevel = max(6, playerLevel + 5)`
— a level-4 hero meets Lv 9 instead of Lv 7.

**It is not live and cannot be:** the eight keys must exist in a shipped client (**the next APK**) and
`api/_lib/tunables.js` must be **deployed** — `api/client-tunables.js` records in its own header that
`vercel.json` sets `"git": {"deploymentEnabled": false}`, so pushing does not deploy.

---

## 6. The capture tradeoff (WO-1705 ruling, 2026-09-11)

`WorkOrders/WORK_ORDER_1705_final_raid_owned_town_ftue_ai_arena.md:15-16`: capture of the personal
town is **a full 3-star clear of the highest raid** (`unlockVictories` 0, not a 20-win counter), and a
**hero-down settle caps at 2 stars** (WO-1526), so a death-win cannot capture. Pinned in code at
`OwnedBaseProgression.cs:19-20` + `:31-32`.

So **every** difficulty change moves capture frequency in both directions: a harder Bastion means
fewer captures **and** more hero-downs. Read any Bastion felt-test with that in mind.

---

## 7. Drift ledger — known-stale prose, with its correction owner

| Stale claim | Where | Correction |
|---|---|---|
| Bastion `rewardMultiplier` "2.8" | `Assets/_Modules/Core/Ops/RemoteTunables.cs:1197` and `Assets/_Modules/Village/Troops/RaidLootTunables.cs:166-167` (both read 2026-09-16) | The JSON says **2.2** (`scene-configs.json`, `iron_bastion` row). WO-1763 §2.6 corrects both **to point at the JSON**, not to swap in a new literal — swapping re-arms the same trap. The surviving *rule* in both places is real: the camp's `rewardMultiplier` is deliberately not applied to gold. |
| `CampIdBastion` "(no scene-config row yet)" | `RaidLootTunables.cs:174` (read 2026-09-16 — ⚠ WO-1763 §2.6 cites `:177`, which is off by three; fix the citation when implementing) | The row exists and the owner has played the raid. |
| ~~`raid.lootCoinsBaseBastion` "does nothing yet"~~ | `api/_lib/tunable-manifest.js:441-448` | **ALREADY FIXED** — read 2026-09-16, the `what` now says "The Bastion is LIVE … so this dial pays out now." WO-1763 §2.6 still lists it as outstanding; that half of §2.6 is stale. |
| "Threat Levels: Iron Bastion I/II/III… +8% enemy strength, +5% loot per level" | `docs/PROGRAM_RAID_ECONOMY_2026-09-04.md:183` | **Never built.** No `threatLevel` key in `scene-configs.json` and no raid-side reader. The only `ThreatLevel` in the tree is the open-world claimable-camp axis (`Assets/_Modules/Village/World/Camps/ClaimableCamp.cs:42`) — a different system, not the raid tier ladder. |

---

## 8. Build under test — what the owner is actually playing

- **Seeker APK `371627`** (`2026.09.16.371627`; the versionName date is UTC, the local clock was the
  evening of 2026-09-15). Identity, size and SHA-256 are recorded in
  `docs/releases/GOOGLE_PLAY_2026.09.16.371610.md:13-22`.
- **Provenance:** the release record **names no source commit**. Its build window is
  `20:29 → 20:54` local, 2026-09-15 (`:5-6`), and the WO-1760 Arcane Spire re-point it carries was
  committed **nine minutes after the build finished** as `f74bf0829` (`2026-09-15 21:03:52 -0500`). So
  the APK was built from the working tree that **became** `f74bf0829`; treat that hash as the closest
  commit, not as a build stamp. (The record's `:64-68` "uncommitted 2026-09-13 dedup of
  `Structure_Art.asset`" is the *cause* WO-1760 fixed — an earlier uncommitted dedup that had left
  `ArcaneSpire_2` mapped to a wall tower — not a description of this build's tree.)
- **WO-1730's fix is NOT in it.** `ba13b93ef` ("a troop's route to the spire is OPEN when its path
  ends inside the arrival radius") is dated `2026-09-15 22:02:27 -0500` and
  `git merge-base --is-ancestor ba13b93ef f74bf0829` returns **false**. It postdates the build window
  as well.
- **The 09-15 camera fixes ARE in it.** `06da9b7b6` (WO-1734, 10:12:52), `4d3ec15c5` (WO-1751,
  15:02:49) and `877fe785d` (WO-1753, 16:38:30) are all ancestors of `f74bf0829` and all predate
  20:29.

---

## 9. Not verified by this lane

1. **The 13:42 CDT timestamp, the exact command and the `--by cli-lead` author** of the
   `honorThirdStarSeconds` set are the lead's report. No `TUNABLES_SET_OK` line for it was found on
   disk under `logs/` or `Builds/`. What **is** proven is the live endpoint's value (§3/§4).
2. **The live `client_tunables` table was not read directly** (no `DATABASE_URL` in this lane) — only
   the public `GET` projection of it.
3. **The capture-window consequence in §4 is proven from code, not from a raid.** No captured raid log
   showing a clear settling below 3 stars *because of* the 60 s T3 was read; the chain
   (`Finalize → min(settle, honor) → RaidResult.Stars → _victoryStars → CaptureStarsRequired`) is what
   is proven.
4. **Whether the Vercel project serving the endpoint is the one this repo deploys to** was not
   re-derived; the URL above answered, which is the fact used.
5. **Whether `FromTable` is the builder every raid composition id actually lands in** — the
   `BuildTypedDef` switch (`GarrisonStatBlocks.cs:145-153`) routes `hollow-acolyte`, `hollow-warrior`
   and `orc-shaman` there and `troll` to `BuildTrollDef`, but the four "pinned, code-built" ids below
   `:155` were not read. The const is folded on every path read; that it is folded on *all* of them is
   not proven here.
