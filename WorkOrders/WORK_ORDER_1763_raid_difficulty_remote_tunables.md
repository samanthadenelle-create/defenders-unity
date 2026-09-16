# WORK ORDER 1763 — Per-camp REMOTE raid-difficulty overrides (DB-tunable, no build)

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-16 (CLI banner, two-hundred-and-tenth pass — bumped 1763 -> 1764 in the same edit)
**Silo:** Raid economy / remote tunables — `DeNelle.Core.Ops` + `DeNelle.Village` + `api/` + the tunable docs/manifest chain
**Lane disjointness:** touches NO `.unity`, NO scene builder, NO canonical JSON. File-disjoint from any art, HUD or scene lane.

---

## 0. Why this ticket exists (owner-visible symptom, then the proven cause)

The owner **nearly 3-starred the Iron Bastion with a level-4 hero** and reported that she believed raid
difficulty was already tunable from the database. Both halves of that were checked at source
2026-09-16:

**(a) The Iron Bastion is a field-for-field clone of Camp III on every difficulty field.**
Read out of `Assets/Resources/Data/Canonical/scene-configs.json` (twin at
`Assets/StreamingAssets/Data/Canonical/scene-configs.json`) by parsing the file, not by eye:

| id | composition | baseEnemyLevel | difficultyMultiplier | levelOffset | boss | eliteCount | difficulty | rewardMultiplier |
|---|---|---|---|---|---|---|---|---|
| `raider_camp_small` | orc-berserker x7, orc-shaman x2 | 3 | 1.0 | 0 | orc-necromancer | 0 | Regular | 1.0 |
| `fortified_garrison` | troll x4, ogre x2, orc-berserker x6, orc-shaman x3 | 5 | 1.25 | 2 | orc-necromancer | 1 | Hard | 1.5 |
| `mage_enclave` | hollow-acolyte x7, orc-shaman x5, hollow-warrior x7 | 6 | 1.3 | 3 | necromancer | 3 | Extreme | 2.2 |
| **`iron_bastion`** | **hollow-acolyte x7, orc-shaman x5, hollow-warrior x7** | **6** | **1.3** | **3** | **necromancer** | **3** | **Extreme** | **2.2** |

The two Extreme rows are identical on every difficulty axis. The "fourth and evergreen" target fights
exactly like the third.

**(b) Raid difficulty is NOT on the remote rail.** Every `raid.*` key registered in
`Assets/_Modules/Core/Ops/RemoteTunables.cs` is loot, honor, heartfire, staging or rough-stone —
there is no difficulty knob:

```
raid.lootWoodBase  raid.lootIronBase  raid.lootFailPct  raid.lootOneStarPct  raid.lootTwoStarPct
raid.lootThreeStarPct  raid.lootPerfectPct  raid.lootCoinsBaseCamp1  raid.lootCoinsBaseCamp2
raid.lootCoinsBaseCamp3  raid.lootCoinsBaseBastion  raid.lootCrystalsBase  raid.lootCrystalsPerStar
raid.lootRepeatClearPct  raid.cacheCapPerResource  raid.starterArmySize  raid.heartfireMaxCharges
raid.heartfireRegenSeconds  raid.stagingCeilingSeconds  raid.honorThirdStarSeconds
raid.honorSecondStarSeconds  raid.honorSecondStarMinDestructionPct  raid.roughStoneMinTier
raid.roughStonePerDayCap
```
(`RemoteTunables.cs:414-725`, read 2026-09-16.)

So today the only way to make a camp harder is to edit `scene-configs.json` and ship a build. **The rail
to fix that already exists** — `api/client-tunables.js` + the Neon `client_tunables` table
(`api/migrations/20260902_0018_client_tunables.sql`) + `RemoteTunables.Registry`. This ticket puts
difficulty on it. **Nothing new is invented; the existing rail is reused end to end**, exactly as
`RaidLootTunables` reuses it.

---

## 1. The runtime seam, read at source (cite these lines in the RESULT)

`Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs`:

- `:124` — `var def = SceneConfigCatalog.Find(configId);` — the config arrives by **stored id**, not by
  scene name (the baked scene is `RaidBase_<id>`).
- `:165-167` — the two numbers this ticket overrides:
  ```csharp
  int enemyLevel = Mathf.Max(g.baseEnemyLevel, playerLevel + g.levelOffset);
  float difficulty = g.difficultyMultiplier > 0f ? g.difficultyMultiplier : 1f;
  ```
- `:435-441` — `FoldDifficulty(EnemyDef def, float difficulty)`: the **ONE place** the multiplier
  touches combat — `def.Hp *= difficulty; def.ContactDamage *= difficulty;`, no-op at `<=0` or `~1`.
- `:237-280` — `ExpandComposition`: flattens `composition[]`, then appends `cfg.eliteCount` copies of
  the heaviest composition id (or the boss id). `eliteCount` lives on `SceneConfigDef`
  (`SceneConfigCatalog.cs:183`), **not** on the garrison block.
- `:61` — `[SerializeField] private int liveCombatantCap = 36;` — **DO NOT TOUCH** (see §7).
- `:64` / `:66` — boss multipliers `bossHpMult = 3f`, `bossDamageMult = 1.5f`, applied on top.
  (Lead correction 2026-09-16: the two fields are at `:64` and `:66`, not the `:66-69` this WO drafted.)
- `:197-202` — the existing loud entry line this ticket extends:
  ```
  RAID START config='<id>' garrisonAlive=<n> enemyLevel=<n> difficultyx<F2> spire=<n>hp ...
  ```

Level curve: `Assets/_Modules/Village/World/Camps/GarrisonStatBlocks.cs:110-127`
`ApplyLevelScale(def, level)` — `hpScale = 1 + 0.08*(level-1)`;
`dmgScale = 1 + 0.04*min(over,10) + 0.02*min(max(0,over-10),10)`; height, XP and a `(Lv n)` display
suffix ride along.

---

## 2. What to build

### 2.1 Eight new keys in `RemoteTunables.cs`

Follow the **existing** `RaidLootCoinsBaseBastion` pattern exactly — a `const int <Name>Default` with a
doc comment (`RemoteTunables.cs:355-371`), a `const string Key<Name>` with a one-line `<summary>`
naming its consumer (`:434-444`), and a `new TunableSpec(Key…, TunableKind.Int, …Default, whatOnDoes,
hypothesis)` entry in the registry (`:1194-1203`). Camp naming reuses the loot keys' own suffixes so
the two families read as one table.

| Key | Kind | Default | Clamp (at the consumer) | Meaning |
|---|---|---|---|---|
| `raid.difficultyMultPctCamp1` | int | `100` | 25..400 | PERCENT applied to that camp's JSON `difficultyMultiplier`. 100 = the JSON value unchanged. |
| `raid.difficultyMultPctCamp2` | int | `100` | 25..400 | " |
| `raid.difficultyMultPctCamp3` | int | `100` | 25..400 | " |
| `raid.difficultyMultPctBastion` | int | `100` | 25..400 | " |
| `raid.levelOffsetCamp1` | int | `-999` | -5..+20 when not the sentinel | REPLACES that camp's JSON `levelOffset`. `-999` = the sentinel "use the JSON value". |
| `raid.levelOffsetCamp2` | int | `-999` | -5..+20 | " |
| `raid.levelOffsetCamp3` | int | `-999` | -5..+20 | " |
| `raid.levelOffsetBastion` | int | `-999` | -5..+20 | " |

**Semantics, stated so they cannot be read two ways:**
- `difficultyMultPct` is **MULTIPLICATIVE on the JSON value** — effective = `jsonMultiplier * pct/100`.
  At the default 100 the effective value is bit-identical to today's.
- `levelOffset<Camp>` is **REPLACE, not ADD** — effective offset = the remote value when it is not the
  sentinel, else the JSON value. The `max(baseEnemyLevel, playerLevel + offset)` floor at
  `RaidGarrisonSpawner.cs:166` is untouched, so `baseEnemyLevel` still floors a low-level hero's raid.
- ⛔ **A `levelOffsetDelta<Camp>` (default 0, effective = json + delta) was CONSIDERED AND REJECTED.**
  It needs no sentinel, but the owner's 2026-09-16 ruled seed (`levelOffsetBastion = 5`) was given
  against REPLACE semantics, and under delta semantics that same 5 would mean offset 8. Reading a
  ruling two ways is the failure this paragraph exists to prevent. REPLACE it is.
- The sentinel is negative on purpose and that is safe: the doc-parity oracle parses the DEFAULT column
  with `int.TryParse(..., NumberStyles.Integer, InvariantCulture, ...)`
  (`RemoteTunablesDefaultsRegression.cs`, `ParseDocTable`), which accepts a leading sign.
- **Resetting a camp to its JSON value = DELETE the row**, which is the table's documented resting
  state ("an EMPTY table is therefore the correct and expected state",
  `api/migrations/20260902_0018_client_tunables.sql`). Setting `levelOffset<Camp>` back to `-999`
  explicitly is also valid; the manifest `min` must therefore admit the sentinel (see §2.4(e)).
- `raid.eliteCount<Camp>` is **OUT OF SCOPE for this ticket** and recorded as a follow-up in §8 —
  every key costs six coordinated edits (§2.4), and eliteCount also interacts with a cap this ticket
  has not read (§7).

### 2.2 `RaidDifficultyTunables` — a small pure consumer

New file `Assets/_Modules/Village/Troops/RaidDifficultyTunables.cs`, namespace `DeNelle.Village`,
assembly `DeNelle.Village` — **beside `RaidLootTunables.cs`, and modelled on it line for line**
(`ClampAndReport` + `FlowTrace.Once` on a clamp; the per-camp `CampId*` constants; the
unknown-id fallback that says so ONCE and by id).

Reuse the camp-id constants rather than re-declaring the four strings: `RaidLootTunables.CampIdCamp1`
= `raider_camp_small`, `CampIdCamp2` = `fortified_garrison`, `CampIdCamp3` = `mage_enclave`,
`CampIdBastion` = `iron_bastion` (`RaidLootTunables.cs:170-177`). Matching is
ordinal-case-insensitive and trimmed, same as `CoinsBaseFor`, because the id comes from a
hand-authored JSON catalog.

Required shape (names are a suggestion; the behaviour is not):

```csharp
/// Resolve the EFFECTIVE difficulty for one camp. Pure: no scene, no save, no network.
public static Resolved Resolve(string configId, float jsonMultiplier, int jsonLevelOffset)
```
returning at minimum: `EffectiveMultiplier` (float), `EffectiveLevelOffset` (int),
`MultiplierPct` (int, post-clamp), `MultiplierSource` and `OffsetSource` (`"json"` / `"remote"`).

Rules:
1. An id **not** in the four-camp table resolves to the JSON values **unchanged** and says so ONCE via
   `FlowTrace.Once` naming the id and the four known ids — the `CoinsBaseFor` precedent. Never fall
   back to another camp's knob and never to zero: `FoldDifficulty` at `0` is a no-op, so a silent zero
   would look like "difficulty applied" while applying nothing.
2. Source label = `"json"` when the resolved value equals the registry default (i.e. no override in
   play), else `"remote"`. `RemoteTunables.Int` already emits the authoritative provenance line —
   `KNOB <key> = <v> provenance=<default|remote|cache|local>` once per key
   (`RemoteTunables.cs:1577-1580`) — so **do not re-implement provenance detection**; that KNOB line
   is the proof of where the value came from, and this label is only a human hint on the RAID line.
3. Clamps live HERE, not in the registry, exactly as `RaidLootTunables.ClampAndReport` owns the loot
   clamps. A clamped value logs once per key per process and the raid uses the **clamped** value.
4. Pure and static — an oracle must be able to assert the whole table with nothing loaded.

### 2.3 Wire it at the seam

In `RaidGarrisonSpawner.ActivateRoutine`, replace the two lines at `:166-167` with a resolve against
`configId` and then use the effective values unchanged downstream (`SpawnGarrisonStaggered`,
`FoldDifficulty`, `ApplyLevelScale`). Extend the existing `RAID START` line at `:197-202` — **do not
add a second line** — so it carries both numbers and both sources, e.g.:

```
RAID START config='iron_bastion' garrisonAlive=23 enemyLevel=9 (json offset 3, remote offset 5)
  difficultyx2.08 (json 1.30 x 160%, src=remote) spire=...hp ...
```

Keep it ONE line and keep the existing tokens (`config=`, `garrisonAlive=`, `enemyLevel=`,
`difficultyx`, `spire=`) spelled the same — other readers grep them.

### 2.4 The registration chain is SIX places per key family. All six, or the suite goes red.

This is the part the ticket exists to spell out; skipping any one of (b)-(f) ships a **dead knob** or a
**red `[tunable-defaults]` suite**:

| # | File | What to add |
|---|---|---|
| (a) | `Assets/_Modules/Core/Ops/RemoteTunables.cs` | default const + key const + `TunableSpec` registry entry, per §2.1 |
| (b) | `api/_lib/tunables.js` | a `{ key: 'raid.difficultyMultPctCamp1', kind: 'int' }` row per key in `TUNABLE_KEYS` (`:55+`). ⛔ **`readTunables` filters every row through `isKnownKey` and counts the rest as `ignored`** — an unlisted key's DB row is silently discarded, so without this the owner's seed does nothing at all. |
| (c) | `docs/PROD022_TUNABLE_FLAGS.md` | one table row per key, **with the default backticked in the DEFAULT column** — `ParseDocTable` requires a backticked, parseable integer there |
| (d) | `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | add each key to `ExpectedDefaults`. Case 5 fails on `compared != ExpectedDefaults.Length` and case 4 `[key-domain]` asserts the key set is **identical** across `RemoteTunables.Registry`, `api/_lib/tunables.js` and the doc table. |
| (e) | `api/_lib/tunable-manifest.js` | the hand-authored `area` / `label` / `what` / `min` / `max` per key (pattern at `:441-449`). `min` for the offset keys must admit the sentinel `-999`. |
| (f) | `api/_lib/tunable-manifest.generated.json` | regenerate: `node tools/gen-tunable-manifest.mjs`, judged by the marker **`TUNABLE_MANIFEST_GEN_OK`** (never the exit code), then `node tools/gen-tunable-manifest.mjs --check` must not print `TUNABLE_MANIFEST_DRIFT`; `test/tunables-manifest.test.js` must pass. |

⛔ **Do not write a new knob COUNT anywhere.** `RemoteTunablesDefaultsRegression.cs` already carries two
stale counts in its own prose — the header says "TWENTY-SIX knobs" and case 4's comment says "The 14
keys", while `api/_lib/tunables.js` has 56 `key:` rows today. Make the prose you touch **count-free**
(point at `Registry` / `TUNABLE_KEYS`), and do not "correct" the counts to new literals — that is the
duplicated-state disease CLAUDE.md §2/§5/§8/§16 each record a scar from.

### 2.5 The ruled Bastion seed — OWNER RULING 2026-09-16, not an example

> Owner ruling relayed 2026-09-16: **`difficultyMultPctBastion = 160` and `levelOffsetBastion = 5`.**
> The other three camps stay at **identity** (no rows).

Apply it to the live Neon `client_tunables` table as part of this ticket, and prove it with the
headless/Player.log `RAID START` line (§5). Write through the sanctioned lever —
`tools/client-tunables.mjs` (driven by `tools\command-centre.ps1 -Tunables`) or `api/admin/ops.js` —
which normalises and allowlist-checks the value. The equivalent SQL, for the record and for a manual
recovery:

```sql
-- OWNER RULING 2026-09-16. The Iron Bastion stops being a Camp III clone.
INSERT INTO client_tunables (key, value, updated_by) VALUES
  ('raid.difficultyMultPctBastion', '160', 'WO-1763 owner ruling 2026-09-16'),
  ('raid.levelOffsetBastion',       '5',   'WO-1763 owner ruling 2026-09-16')
ON CONFLICT (key) DO UPDATE
  SET value = EXCLUDED.value, updated_by = EXCLUDED.updated_by, updated_at = NOW();
```

**What those two numbers mean in absolute terms — put this arithmetic in the RESULT:**
- `160` → effective difficulty `1.3 x 1.60 = 2.08` (was 1.30). `FoldDifficulty` scales guard **HP and
  contact damage** by that, so **x1.60 vs today** on both, before level scaling.
- `5` (REPLACE) → `enemyLevel = max(6, playerLevel + 5)`. The owner's level-4 hero meets **Lv 9**
  instead of today's `max(6, 4+3) = Lv 7`.
- Compounded through `ApplyLevelScale`: HP `1 + 0.08x8 = 1.64` vs `1.48` today, contact damage
  `1 + 0.04x8 = 1.32` vs `1.24`. Net vs today: guard HP **~x1.77**, guard contact damage **~x1.70**.
  The boss's `x3` HP / `x1.5` damage ride on top unchanged.

⛔ **The client's compiled defaults still reproduce today's behaviour byte for byte.** Only the DB rows
change the feel, and only once (b) is deployed (§4). A player who is offline, times out, gets a 404 or
malformed JSON resolves every one of these to identity — the rail's own stated invariant.

### 2.6 Fix the stale `2.8` drift in the same WO

Both of these restate a number that the JSON has since moved off (`iron_bastion.rewardMultiplier` is
**2.2**, read 2026-09-16). **Correct them to POINT AT THE JSON rather than restate any number** — do
not swap 2.8 for 2.2, which just re-arms the same trap:

- `Assets/_Modules/Core/Ops/RemoteTunables.cs:1197-1198` — "its rewardMultiplier of 2.8 is deliberately
  IGNORED by gold, because 2200 x 2.8 is 6160 and her number is 6500."
- `Assets/_Modules/Village/Troops/RaidLootTunables.cs:166-167` — "rewardMultiplier 2.8). That
  multiplier is deliberately NOT applied to gold: 2,200 x 2.8 is 6,160 and the map's Bastion number is
  6,500."

The surviving claim in both places is the **rule**: the camp's `rewardMultiplier` is deliberately not
applied to gold, because gold is sized per camp by its own knob against a designed army cost — read the
live multiplier off `scene-configs.json`.

Two adjacent one-line drifts in the same files, cheap to fold in while you are there:
- `RaidLootTunables.cs:174` (this WO drafted `:177`; corrected by the lead 2026-09-16) — `CampIdBastion`'s summary still says "(no scene-config row yet)". The row
  exists and the owner has played the raid.
- `api/_lib/tunable-manifest.js:444-447` — `raid.lootCoinsBaseBastion`'s `what` says "This dial does
  nothing yet - the Bastion map is built but is not switched on in the game." It is switched on.

---

## 3. Regression suite

New `Assets/Editor/Regression/RaidDifficultyTunablesRegression.cs` (namespace
`DeNelle.Editor.Regression`), modelled on the existing raid oracles. **Pure — no scene, no save, no
network, no PlayerPrefs.** Own marker pair (`RAID_DIFFICULTY_OK` / `RAID_DIFFICULTY_FAIL`) for a
standalone run, distinct-substring from every existing marker.

Cases:
1. **`[identity]`** — with no overrides in play, `Resolve` returns the JSON values **exactly** for all
   four camps: `mage_enclave` -> `1.30 / 3`, `iron_bastion` -> `1.30 / 3`, `fortified_garrison` ->
   `1.25 / 2`, `raider_camp_small` -> `1.0 / 0`, and every source reads `json`. Read the JSON values
   through `SceneConfigCatalog`, not as literals, so the case cannot certify a stale table.
2. **`[folds]`** — an override folds into HP and contact damage: with `difficultyMultPct = 160` the
   effective multiplier is `2.08`, and driving a real `EnemyDef` through the same
   `FoldDifficulty` + `ApplyLevelScale` arithmetic yields the §2.5 numbers (HP x1.64 at Lv 9 on top of
   the x2.08 fold). Assert HP **and** contact damage; `FoldDifficulty` touches both.
3. **`[offset-replace]`** — `levelOffset<Camp>` REPLACES rather than adds, the sentinel `-999` falls
   back to the JSON offset, and the `max(baseEnemyLevel, playerLevel + offset)` floor still holds for a
   low-level hero.
4. **`[unknown-camp]`** — an id not in the table (e.g. `"no_such_camp"`, and `""`/`null`) resolves to
   the passed-in JSON values unchanged. A zero/identity-by-accident result must FAIL this case.
5. **`[clamps]`** — out-of-range values clamp to 25..400 / -5..+20 and the raid uses the clamped value.
6. **`[defaults-identity]`** — the registry defaults for all eight keys are exactly `100` / `-999`, so
   a fresh install and an empty table are the same raid. This is the "no felt change until the owner
   sets a DB value" invariant, pinned.

Registration: the **lead adds the line** in `Assets/Editor/Regression/DataRegression.cs` — follow the
established shape and keep it inside a `Guard.Try` like its neighbours:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-difficulty suite", () => { if (!DeNelle.Editor.Regression.RaidDifficultyTunablesRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-difficulty] " + r); });
```

`[tunable-defaults]` (`RemoteTunablesDefaultsRegression`) is already registered and **must stay green** —
that is what §2.4(d) buys.

---

## 4. Hard precondition on the DB-backed proof — the api change must be DEPLOYED

- `readTunables` discards any row whose key is not in `TUNABLE_KEYS`. Until §2.4(b) is **live on
  Vercel**, the owner's seed rows are `ignored` server-side and the client sees no override.
- `api/client-tunables.js` records in its own header: **`vercel.json` sets
  `"git": { "deploymentEnabled": false }` — PUSHING DOES NOT DEPLOY.** A deploy must be run.
- Turnaround to a running client is ~40 s worst case: 10 s edge `s-maxage` + the 30 s client poll.
- The Vercel project this repo deploys to is `defenders-of-the-realm-v2` per the session memory
  `four-vercel-projects-serve-this-game`; **that is memory, not a file this lane opened** — confirm
  before deploying (§9).
- **Deploy-free alternative for the fold proof:** `RemoteTunables` also honours a local PlayerPrefs
  override (`ReadLocalOverride`, `RemoteTunables.cs:1573-1578`), which wins last. Use it to prove the
  arithmetic folds without any backend; the DB row then proves the **rail**. Both, not one.

---

## 5. Acceptance criteria

1. `COMPILE_GATE_OK` on a fresh log. Before the gate run, `python tools/gate_brace.py` clean on every
   touched `.cs` (the gate's brace scanner has no interpolated-string model — CLAUDE.md §1).
2. `REGRESSION_OK <n>/<n> suites` on a fresh log, with `[raid-difficulty]` and `[tunable-defaults]`
   both present and passing. **Judge by the marker, never the exit code**, and read the log with
   PowerShell (UTF-16).
3. `TUNABLE_MANIFEST_GEN_OK` from `node tools/gen-tunable-manifest.mjs`, no `TUNABLE_MANIFEST_DRIFT`
   from `--check`, and `test/tunables-manifest.test.js` green.
4. **Identity proof:** a raid entry with an EMPTY `client_tunables` table logs
   `enemyLevel=7 difficultyx1.30 ... src=json` for `iron_bastion` at a level-4 hero — byte-identical to
   today. No felt change ships with the code.
5. **Remote proof:** with the §2.5 ruled rows applied and the api deployed, the same entry logs
   `enemyLevel=9 ... difficultyx2.08 (json 1.30 x 160%, src=remote)`, and a `KNOB
   raid.difficultyMultPctBastion = 160 provenance=remote` line appears in the same capture. Paste both
   lines into the RESULT with the log path and its timestamp.
6. The `2.8` comments at `RemoteTunables.cs:1197-1198` and `RaidLootTunables.cs:166-167` no longer
   restate a reward multiplier; `grep -n "2\.8" ` over both files returns nothing from those passages.
7. `docs/PROD022_TUNABLE_FLAGS.md` carries a row per new key with a backticked default.
8. Board: this WO's own `**Status:**` line flipped and `WORK_ORDER_1763_raid_difficulty_remote_tunables.RESULT.md`
   written **in the same commit as the work**; `python tools/board_build.py` re-run and its
   `BOARD_CHECK_*` marker reported.

---

## 6. Owner-visible consequence to note (no ruling needed)

Per the **WO-1705 ruling 2026-09-11**: a full **3-star Iron Bastion clear triggers owned-town
capture**; a **hero-down settles the cap at 2 stars**. Raising the Bastion therefore changes how often
the capture tradeoff fires, in both directions — a harder Bastion means fewer captures **and** more
hero-downs. This is flagged so the felt-test is read with it in mind; it needs no decision here, and
nothing in this ticket touches the capture path.

---

## 7. What NOT to touch

- ⛔ `Assets/Resources/Data/Canonical/scene-configs.json` and its `Assets/StreamingAssets/` twin — **no
  value changes.** The JSON stays the baseline the percentages multiply. (Canonical JSON edits are
  binary-only in this repo anyway; there is no reason to open these files.)
- ⛔ `Assets/Editor/WallTools/RaidBaseGenerator.cs` — `TierFor` (`:373-381`) owns spire HP, the tower
  DPS budget, tower counts and wall tier. Those are **BAKE-TIME** knobs: changing them needs a full
  raid-scene regen (`BuildAllRaidScenes`) and a NavMesh bake. **EXPLICITLY OUT OF SCOPE for this WO.**
- ⛔ Any `.unity` file. Nothing here is a scene change.
- ⛔ `liveCombatantCap` (`RaidGarrisonSpawner.cs:61`, 36) — this ticket changes how hard each defender
  is, never how many there are.
- ⛔ `GarrisonStatBlocks.GlobalDifficultyMult = 1.2f` (`:38`) — a compiled const multiplying every
  garrison stat block. A real difficulty lever, but it needs a build and it is global, so it is the
  wrong instrument for a per-camp ruling. Named here so the next seat does not "also fix" it.
- ⛔ Do not add a second FlowTrace line for the raid entry, do not rename the `RAID START` tokens, and
  do not strip any existing instrumentation (CLAUDE.md §12).
- ⛔ Do not re-implement remote-config plumbing. `RemoteTunables` + `client_tunables` is the rail.

---

## 8. Follow-ups deliberately left out

- **`raid.eliteCount<Camp>`** — the third difficulty axis (`SceneConfigDef.eliteCount`, folded at
  `RaidGarrisonSpawner.cs:249-280`). Left out because each key costs the six coordinated edits in
  §2.4, and because elite count interacts with `liveCombatantCap`: today's Bastion roster is 19
  composition units + boss + 3 elites = **23** against a cap of 36, and **this lane did not read how
  `SpawnGarrisonStaggered` clips at the cap**. Worth its own ticket, with that clip path read first.
- **Per-camp bake-time difficulty** (spire HP / tower DPS / wall tier) — needs `RaidBaseGenerator` plus
  a scene regen and bake. Separate ticket, separate gate.
- The two stale counts in `RemoteTunablesDefaultsRegression.cs` prose (§2.4) — make count-free only
  where this ticket already edits; a full sweep is not this ticket's job.

---

## 9. Facts NOT verified by this lane (open until the implementer or lead proves them)

1. **No headless runner that reaches `RAID START` was found.** `grep "RAID START"` over
   `Assets/Editor/` returned **no matches**, and a wider sweep of `Assets/` + `tools/` + `.claude/`
   returned only **runtime** emitters/readers — `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs`
   (the emitter), `Assets/_Modules/Core/HubScenes.cs`, `Assets/_Modules/Village/HUD/HudContextEvaluator.cs`,
   plus stale copies of those three inside `.claude/worktrees/` and two WO markdowns. **No script,
   scenario or runner anywhere greps or drives that token**, and there is no `tools/autopilot`
   directory. So the line is a runtime `FlowTrace` in the spawner, not
   something an EditMode oracle emits. The implementer must identify the play-mode/AutoPilot path that
   enters a raid headlessly; **if none exists, the §5.4/§5.5 proof is a device or Windows-player
   `Player.log` line**, and the WO is still acceptable on that evidence. The pure regression in §3 is
   what carries the arithmetic.
2. **The Vercel project name** (`defenders-of-the-realm-v2`) comes from session memory, not from a file
   this lane opened. Confirm before deploying.
3. **Whether `api/_lib/tunable-manifest.js` `min` can express the `-999` sentinel** without tripping a
   manifest test was not exercised; if it cannot, state that and use "delete the row" as the documented
   reset, keeping `min` at `-5`.
4. **The live `client_tunables` table contents were not read** — this lane has no DATABASE_URL. Whether
   any `raid.*` row is already set is unknown; check before upserting so the seed does not overwrite
   something the owner set by hand.

---

## 10. Files to edit (checklist)

| File | Change |
|---|---|
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | 8 defaults + 8 key consts + 8 `TunableSpec` entries; fix the `2.8` comment at `:1197-1198` |
| `Assets/_Modules/Village/Troops/RaidDifficultyTunables.cs` | **NEW** — the pure consumer (§2.2) |
| `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs` | resolve at `:166-167`; extend the `RAID START` line at `:197-202` |
| `Assets/_Modules/Village/Troops/RaidLootTunables.cs` | fix the `2.8` comment at `:166-167`; the `CampIdBastion` "no row yet" line at `:174` |
| `Assets/Editor/Regression/RaidDifficultyTunablesRegression.cs` | **NEW** — the oracle (§3) |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | `ExpectedDefaults` + count-free prose (§2.4d) |
| `Assets/Editor/Regression/DataRegression.cs` | registration line — **the lead adds this** |
| `api/_lib/tunables.js` | 8 `TUNABLE_KEYS` rows |
| `api/_lib/tunable-manifest.js` | 8 hand-authored rows; fix the `lootCoinsBaseBastion` "does nothing yet" `what` |
| `api/_lib/tunable-manifest.generated.json` | regenerated by `node tools/gen-tunable-manifest.mjs` |
| `docs/PROD022_TUNABLE_FLAGS.md` | 8 table rows, backticked defaults |
| Neon `client_tunables` | the §2.5 ruled Bastion seed, via `tools/client-tunables.mjs` |

Every source line cited in this work order was opened at source on **2026-09-16**.
