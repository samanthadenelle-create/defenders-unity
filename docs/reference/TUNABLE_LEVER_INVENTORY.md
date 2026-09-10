# TUNABLE LEVER INVENTORY — every balance number, and what it costs to change it

**Built:** 2026-09-02 · **Branch:** `feat/synty-art-retheme` · **HEAD:** `9a657c8cb` · **Method:** read-only
shape sweep of `Assets/_Modules/**/*.cs` (1292 files, 25 asmdefs) + every file in
`Assets/Resources/Data/Canonical/` (71 JSONs), plus `api/`. Every row carries `file:line` so any single
row is re-verifiable at a glance (project memory `audit-outputs-as-known-dictionaries`).

> **Owner ruling 2026-09-02, verbatim:** *"be smart, dont make it need a code change, make it tweakable
> from a db call"* — followed by *"i have been screaming this for months."*
>
> She is right, and this file exists because the reason it never happened is that **nobody ever produced
> the list**. Every previous attempt died as a good intention. This is the list.

> **⚠ LINE NUMBERS ARE AGAINST THE LIVE WORKING TREE AT `9a657c8cb`, NOT A FROZEN COMMIT.** A cite that
> is off by a few dozen lines is drift, not an error — **the quoted symbol name is the durable half**.
> Re-grep the symbol, not the line.

> **⚠ THIS IS A DERIVED REGISTRY, NOT A DESIGN DOC.** Where a code comment and the code disagree, the
> code wins and the row says so. Nothing here was taken from a doc, a work order, or a comment without
> being opened at source. Anything not verified is marked **UNCERTAIN** and says why.

---

## 0. The authorities (read these before trusting any other doc)

| Authority | What it decides | Path |
|---|---|---|
| **The rail** — registry, defaults, parse | the ONE mechanism a remote knob may use | `Assets/_Modules/Core/Ops/RemoteTunables.cs` |
| Rail transport / poll / device cache | how a value reaches a client (30 s poll) | `Assets/_Modules/Core/Ops/RemoteTunablesService.cs:107` (`PollSeconds = 30`) |
| Server allowlist | which keys may be WRITTEN (spell-check, never a default) | `TUNABLE_KEYS` in `api/_lib/tunables.js:56-71` |
| Oracle | pins registry ↔ allowlist ↔ owner doc ↔ consumers | `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs:131` (`ExpectedDefaults`) |
| Owner-facing flag table | what she reads before flipping anything | `docs/PROD022_TUNABLE_FLAGS.md` |
| Worked example of a BALANCE knob on the rail | the template every row below should follow | `HeroAbilities.DrainReturnPct` — `Assets/_Modules/Village/Hero/HeroAbilities.cs:1729-1730` |
| Canonical data loader | why "data-driven" still costs a rebuild (§2) | `Assets/_Modules/Core/Data/LocalJsonCatalogSource.cs:30-40` |
| Server-authoritative money | out of scope, and why (§6) | `api/_lib/purchase-catalog.js` |

### 0.1 The invariant every proposal here inherits

> **NO ROW, NO NETWORK, NO SERVER, NO PARSE ⇒ TODAY'S BEHAVIOUR, EXACTLY.**
> (`RemoteTunables.cs:23-24`)

Every knob proposed below has a **build default equal to today's constant**. An offline player, a 404,
a malformed row and an empty table all resolve to the value that ships. That is not a convention, it is
the acceptance criterion, and `[tunable-defaults]` asserts it across seven failure paths.

### 0.2 Adding a knob is a FOUR-file edit, in one commit

`RemoteTunables.Registry` · `TUNABLE_KEYS` in `api/_lib/tunables.js` · `docs/PROD022_TUNABLE_FLAGS.md` ·
`ExpectedDefaults` in `RemoteTunablesDefaultsRegression.cs`. Change one and the oracle reds naming
which two disagree (`RemoteTunables.cs:179-182`). Budget that cost per knob — it is the reason this
inventory is **ranked**, not dumped.

### 0.3 The registry has TWO kinds today: `Bool` and `Int`. There is no float.

`TunableKind` (`RemoteTunables.cs:69-75`) is `Bool = 0, Int = 1`. **Most balance levers in this repo are
`float`.** There are two honest ways forward and this document does not silently assume either:

- **Integer-percent encoding** — the precedent already shipped. `combat.drainReturnPct` is an `int`
  percent divided by `100f` at its single consumer (`HeroAbilities.cs:1774`, `dealt * (pct / 100f)`),
  with the default `100` making it a float identity. Every row below marked **`pct`** can use this with
  no registry change. Where 1% is too coarse, the same trick at **basis points** (`/10000f`) works and
  is still an `int`.
- **A new `TunableKind.Float`** — needed only where a lever is a raw magnitude with no natural anchor
  (e.g. `WaveScalingCurve`'s curve keys). Rows needing it are marked **`NEEDS FLOAT KIND`**. Do not
  assume it exists; it does not.

---

## 1. The three categories, and why conflating them is the whole problem

| # | Category | What it means | Cost to change TODAY |
|---|---|---|---|
| **1** | **ALREADY DATA-DRIVEN** | the number lives in `Assets/Resources/Data/Canonical/*.json` | **Still a full player rebuild + redeploy.** See §2 — this is the trap. |
| **2** | **HARDCODED IN C#** | a real constant / field initializer | Full rebuild. **These are the prize.** |
| **3** | **NOT A BALANCE LEVER** | structural: array bounds, layout px, buffer sizes, schema versions, epsilons, network timeouts | Not proposed, on purpose. §7 says why for each. |

---

## 2. ⛔ THE HEADLINE: "data-driven" does NOT mean "tunable without a rebuild"

This is the single most important finding in the document, and it is why the owner's frustration is
correct even though the team kept moving numbers into JSON.

`CanonicalJson.Read` delegates to `LocalJsonCatalogSource`, which resolves **`Resources.Load<TextAsset>`
FIRST on every platform** and only falls back to `StreamingAssets`
(`Assets/_Modules/Core/Data/LocalJsonCatalogSource.cs:30-40`; the precedence is restated at
`CanonicalJson.cs:9-17`). **`Assets/Resources/` is compiled into the player.** So in a shipped APK or
WebGL build:

- editing `Assets/Resources/Data/Canonical/*.json` requires a **full Unity player build** — measured
  cost per CLAUDE.md §16 / `PROD022_TUNABLE_FLAGS.md:8`: **~10 min APK, ~30 min WebGL**;
- editing only the `StreamingAssets` twin changes **nothing**, because Resources wins first;
- all **71** canonical JSONs exist in both trees (`Resources` 71 / `StreamingAssets` 71, counted).

**Five canonical files state, in their own authoring notes, that the owner "retunes in playtest with NO
recompile":** `dungeon-balance.json`, `echoes-balance.json`, `kill-rewards.json`, `siege-stakes.json`,
`vendors.json` (grepped 2026-09-02). Read literally that claim is TRUE — no *C# compile* is needed —
and read the way an owner reads it, it is **misleading**: the round trip to her phone is a full build
either way. **The distinction the docs kept making is not the distinction she cares about.**

### 2.1 The seam that already exists for fixing this wholesale

`CanonicalJson.Source` is a settable `ICatalogSource` (`CanonicalJson.cs:34`), documented as a one-line
swap to a remote/DB source with no call-site churn (`ICatalogSource.cs:8`). **It is never assigned
anywhere in the tree** (grepped: only the three comment/doc references). So the seam is real, unused,
and would convert the *entire* category-1 surface — measured at **5,224 numeric leaves** across the 70
balance-relevant canonical JSONs (excluding `widget-params.json`, which is UI layout) — into remotely
updatable content in one change, with `LocalJsonCatalogSource` as the fail-to-default fallback the
rail's invariant requires.

**This is NOT a second mechanism** — it is the documented extension point of the mechanism that already
ships. It is Tier 2 of the plan in §8 for exactly that reason.

### 2.2 ⭐ THE SEAM IS NOW CONNECTED (WO-1331, 2026-09-02) — flag-gated and OFF

`CanonicalJson.Source` is assigned in exactly **one** place in the tree:
`RemoteCatalogService.Install()`, and that method **returns before touching it** while the seam is
disarmed. So a default build does not merely *behave* like today — it runs today's code path, with
`CanonicalJson.Source` still holding the `LocalJsonCatalogSource` from its own field initializer.
Nothing is constructed, nothing is polled.

| Layer | File |
|---|---|
| State, parse, **validation**, the two lists | `Assets/_Modules/Core/Data/RemoteCatalogOverrides.cs` |
| The `ICatalogSource` decorator (override, else delegate) | `Assets/_Modules/Core/Data/RemoteCatalogSource.cs` |
| Transport, poll, device cache, **the one install** | `Assets/_Modules/Core/Data/RemoteCatalogService.cs` |
| Local arm (default OFF) | `FeatureFlags.RemoteCatalogs` — PlayerPrefs `ff.catalogremote` |
| Remote arm (rail knob, **not yet registered**) | `catalog.remoteEnabled` — read only once it exists in `RemoteTunables.Registry` |
| Endpoint (not yet deployed → 404 → compiled copies) | `GET /api/client-catalogs` |
| Oracle | `Assets/Editor/Regression/RemoteCatalogSeamRegression.cs` — `[catalog-seam]`, `CATALOG_SEAM_OK` |

**The proven set is FIVE catalogs, not 71** — `enemies.json`, `waves.json`, `echoes-balance.json`,
`kill-rewards.json`, `siege-stakes.json`. Widening is a data edit in
`RemoteCatalogOverrides.Allowlist` plus the matching literal in the oracle.

**The money boundary is code, not prose.** `RemoteCatalogOverrides.Denylist` (`packs.json`,
`wallets.json`) is checked **before** the allowlist, and a payload naming a denied path is rejected
**wholesale** — the honest rows in it do not get a pass either. Prices, entitlements, grants,
base-unit amounts, token decimals and quote TTL stay server-side in `api/_lib/purchase-catalog.js`.

**A payload is accepted whole or rejected whole.** Validation runs *before* anything is replaced:
allowlist, size cap, JSON parse (which is what catches a truncated body), root-kind match against the
compiled copy, and every top-level key the compiled copy has. One failure ⇒ `FlowTrace.Fail` and
nothing changes. There is no partial merge and no path that can blank a catalog.

---

## 3. ⚠ A SECOND REMOTE-CONFIG MECHANISM ALREADY EXISTS IN THE CLIENT — AND ITS SERVER HALF WAS NEVER BUILT

`Assets/_Modules/Core/State/ServerConfig.cs` is a complete, wired, live-ops remote-config record: 11
balance fields, each documented with the Vercel env var meant to set it, absorbed at
`GameStateService.cs:1792-1796` from the `config` block of `api/game/load`, and **actually consumed** —
`WaveManager.cs:3571-3597` reads it for boss-wave crystal drops.

**`api/game/load.js` never emits a `config` key.** Its only response is at `:111-131`
(`ok/success/serverNowMs/serverLastSeenMs/mode/schemaVersion/updatedAt/data`) and a grep of the whole
`api/` tree for `bossWaveCrystalDropChance` or `BOSS_CRYSTAL_DROP_CHANCE` returns **nothing**. So
`resp.Config` is always null, `ServerConfig` is permanently `ServerConfig.Default`
(`ServerConfig.cs:159-176`), and **not one of these 11 knobs has ever been remotely settable.**

| Field | Live value (the `Default`) | `ServerConfig.cs` line |
|---|---|---|
| `BossWaveCrystalDropChance` | `0.45f` | `:161` |
| `BossWaveCrystalMin` / `Max` | `1` / `3` | `:162-163` |
| `BossWaveInterval` | `5` | `:164` |
| `PackSaleActive` / `PackSaleDiscountPct` | `false` / `0` | `:165-166` — **monetization, see §6** |
| `EventBonusCrystals` | `0` | `:169` |
| `EmpowermentCostMultiplier` | `1.0f` | `:172` |
| `CrystalRefundRate` | `0.5f` | `:173` |
| `MaintenanceMode` | `false` | `:174` — **superseded** by the live `maintenance_toggles` rail |

**Recommendation (owner decision, not a tuning one):** do **not** build the missing server half. That
would resurrect a second configuration mechanism, which the rail's own design note forbids
(`PROD022_TUNABLE_FLAGS.md:204-206`). Either **retire `ServerConfig`** and move the four keys worth
keeping (`bossWaveCrystalDropChance`, `bossWaveCrystalMin`, `bossWaveCrystalMax`, `bossWaveInterval`)
onto the tunables rail as rows in §5, or leave it dormant and documented. **Its `MaintenanceMode` is
already superseded** by `maintenance_toggles` / `MaintenanceService`, which is live.

> ⚠ **A published statement to correct:** a sweep of this tree can easily read
> `ServerConfig.CrystalRefundRate` as "remote-overridable via `CRYSTAL_REFUND_RATE`". It is not. The
> env var is named only in a comment; no backend reads it and no response carries it. `0.5f` ships.

---

## 4. THE TOP 15 — ranked by *her* cost, not by row count

Ranking rule: **P(she wants to move this during a felt-test) × the round-trip cost today.** A rebuild is
~10 min (APK) / ~30 min (WebGL). Everything in this table is a lever she could plausibly want to move
**tonight**, and every one of them currently costs a build.

Design note that shapes every proposal: **prefer ONE scalar percent knob at a single-owner consumer
over N knobs.** That is exactly what `combat.drainReturnPct` does — one `int`, one clamp, one call site,
default `100` = identity (`HeroAbilities.cs:1729-1730` and `:1774`). A knob that *multiplies an existing
curve* is far cheaper (four-file edit, one oracle row) and far safer than re-authoring the curve
remotely.

⚠ Several rows below are `[SerializeField]`, not `const` — verified at source: `HeroHealth.cs:39`,
`PlayerAttackController.cs:43, 47, 90`, `SiegeScheduler.cs:61, 66, 71`, `RaidSpire.cs:78`,
`HeartController.cs:97`, `OfflineHarvestService.cs:98`. **Read §5.0.1 before touching any of them** —
a scene-saved value outranks the C# initializer, which is one more reason to put the knob at the
*consumer* rather than edit the field.

| # | Lever | file:line | Today | Proposed key | Kind | Why it's #N |
|---|---|---|---|---|---|---|
| 1 | **Hero XP-to-next-level curve** | `Assets/_Modules/Village/Hero/HeroProgression.cs:110` | `150f + (L-1)*350f + (L-1)²*500f` | `progression.xpToNextPct` | `int` pct, default `100` | **Retention is the business problem** (project memory `retention-is-the-business-problem`). This one formula gates every level, every Wisdom point, and therefore every ability that reaches the action bar. A scalar over the whole curve moves time-to-first-castable in seconds instead of a build. |
| 2 | **Wisdom granted per level** | `HeroProgression.cs:126` (`level <= 8 ? 2 : 3`); `:360` `StarterPoints = 2` | 2 / 3 / start 2 | `progression.wisdomPerLevelEarly`, `.wisdomPerLevelLate`, `.starterPoints` | `int` ×3 | The direct faucet for *"unlock a few items that can go in the quick swap bar fast"* — her own retention ruling, recorded verbatim in `hero-talents.json`'s `_ownerRuling` (2026-09-02). Already integers; no encoding needed. |
| 3 | **Build-timer base + tier growth** | `Assets/_Modules/Core/Catalog/BuildTimerConfig.cs:52` (`45f`), `:56` (`3.2f`) | 45 s, ×3.2/tier | `build.baseSecondsPct`, `build.tierGrowthPct` | `int` pct ×2 | Sets the entire wait ladder (T0 45 s → T7 24 h). §5.2: **this file is shaped like data and has no backing asset**, so it is pure C#. The most-felt pacing surface in a town builder. |
| 4 | **Resource harvest interval ladder** | `Assets/_Modules/Village/Buildings/Progression/ResourceBuildingProgression.cs:189` | `{50, 42.5, 35, 27.5, 20}` s | `economy.harvestIntervalPct` | `int` pct | The master income faucet — how fast the town earns anything at all. One scalar preserves the 2.5× L1→L5 ratio the file's own note protects while moving absolute pace, and sidesteps the duplicate table at `:343`. |
| 5 | **Enemy wave stat-scaling curve** | `Assets/_Modules/Village/Waves/WaveScalingCurve.cs:71-72, 80-81, 90-91` | HP ×2.5, speed ×1.4, damage ×2.0 by wave 20 | `waves.enemyHpScalePct`, `.enemySpeedScalePct`, `.enemyDamageScalePct` | `int` pct ×3 | **The largest difficulty lever in the game, and it has no JSON at all.** Every other wave knob migrated to `waves.json`; this one did not. Difficulty feel is precisely the thing only she can judge. |
| 6 | **Wave size ramp** | `Assets/_Modules/Village/Waves/WaveCompositionBuilder.cs:149-152` | `4`, `+0.9/wave`, cap `22`, elite every `5` | `waves.baseCount`, `.countPerWavePct`, `.maxCount`, `.eliteEveryNth` | `int` ×4 | How crowded a fight feels. These moved *out of* `waves.json` into code on 2026-07-30 (WO-783 D1) and the `[wave-authoring]` regression now **fails the gate** if batches reappear in the JSON — so the rail is the only remaining route. |
| 7 | **Hero base max HP** | `Assets/_Modules/Village/Hero/HeroHealth.cs:39` (`_maxHp = 100f`) | 100 | `combat.heroMaxHpPct` | `int` pct | **The only hero stat with no data seam whatsoever.** Mana seeds from `abilities.json` (`HeroAbilities.cs:312-313`); enemy HP from `enemies.json` (`Enemy.Configure`). Hero HP is a bare field plus additive bonuses (`:183`). ⚠ scene-serialized — §5.0.1. |
| 8 | **Melee primary damage & cadence** | `Assets/_Modules/Village/Enemies/PlayerAttackController.cs:43` (`52.5f`), `:90` (`0.6f`) | 52.5 dmg, 0.6 s | `combat.meleeDamagePct`, `.meleeCooldownPct` | `int` pct ×2 | **The phone has one attack button and this is it** (canon §7: primary is the melee sweep for every class, and the bow never fires from it). Every "combat feels bad" felt-test lands here first. |
| 9 | **Hero move speed** | `Assets/_Modules/Village/Hero/HeroLocomotion.cs:116` (`6.0f`), `:117` (`5.0f`) | 6.0 open / 5.0 combat | `combat.heroMoveSpeedPct`, `.heroCombatMoveSpeedPct` | `int` pct ×2 | The comment at `:115` ends with the literal word **"Tunable."** while both values are `private const`. Traversal pace is pure feel and gets re-judged every session. |
| 10 | **Offline accrual cap** | `Assets/_Modules/Village/Harvest/OfflineHarvestService.cs:98` | `OfflineCapHours = 10f` | `economy.offlineCapHours` | `int` (hours) | Its own tooltip: *"The retention dial … Owner-tunable in playtest."* It is a `[SerializeField]` on a service that is `AddComponent`'d at runtime (`OfflineHarvestBootstrap.cs:28`) and appears in no scene — so the code default **is** live and there is no inspector to reach in a build. Already an integer count of hours. |
| 11 | **Crystal price of time** | `BuildTimerConfig.cs:128` (`1`), `:131` (`10`), `:174` (`0.75f`), `:109` (`600f`), `:120` (`3`) | 1 cr/min, floor 10, exp 0.75, ad-skip 600 s ×3/window | `monet.instantFinishCrystalsPerMinute`, `.instantFinishMinCrystals`, `.instantFinishCurveExpPct`, `.adSkipSeconds`, `.adSkipsPerWindow` | `int` ×5 | Directly decides how a wait converts into spend, and the owner has re-ruled it twice (2026-08-06, 2026-08-21). **⚠ Read §6.2 before shipping these** — a client-trusted price meets a device-local override layer. |
| 12 | **Siege cadence** | `Assets/_Modules/Village/Waves/SiegeScheduler.cs:61`, `:66`, `:71` | 6 h interval, 1 pending, 24 h offline cap | `siege.intervalHours`, `.maxPending`, `.offlineCapHours` | `int` ×3 | How often the town is attacked — the top-level pressure knob for the entire defend loop. Already integer-shaped, so it is nearly free to ship. |
| 13 | **Heart HP and regen** | `Assets/_Modules/Village/Heart/HeartController.cs:97` (`100f`), `Assets/_Modules/Village/Heart/HeartRegen.cs:61` (`2f`) | 100 HP, 2 HP/s | `combat.heartMaxHpPct`, `.heartRegenPct` | `int` pct ×2 | This is **the loss condition**. And `heart.json` authors `maxHp 160` with `regenPerSecondOutOfCombat 0` — **and nothing reads it** (§5.7). The shipped Heart is materially easier than the designed one and no doc says so. |
| 14 | **Echo income magnitude** | `Assets/_Modules/Village/Harvest/EchoBonusCalculator.cs:89-91` | `3600f` / `900f` / `4f` per hour | `economy.echoCommonPerHourPct`, `.echoGoldPerHourPct`, `.echoCrystalPerHour` | `int` ×3 | `echoes-balance.json` authors the **percentages**; these three constants are the **magnitudes those percentages multiply**. The file presents Echo income as fully owner-tunable and half of it is not. |
| 15 | **Raid length** | `Assets/_Modules/Village/Troops/RaidScoring.cs:83` (`180f`), `Assets/_Modules/Village/World/Camps/RaidSpire.cs:78` (`1200f`) | 180 s clock, 1200 HP spire | `raid.clockSeconds`, `raid.spireHpPct` | `int` ×2 | The two numbers that decide whether a raid reads as tense or as a slog. Both code-only — while the adjacent siege-**stakes** system is already fully JSON-backed, which is exactly the inconsistency that makes the loop hard to tune. |

**Rows 1–15 name 33 distinct constants.** If only the first five ship, the owner gains same-evening
control over progression pace, build pace, income pace and difficulty — the four surfaces a felt-test
actually judges.

---

## 5. THE REGISTRY — category (2), hardcoded in C#, by domain

### 5.0 Two mechanical cautions that apply to many rows below

1. **`[SerializeField]` on a scene component is NOT a `const`.** Where a lever is a serialized field on
   a component saved into a scene, the **scene YAML wins over the C# initializer**. Verified example:
   `HeartController._hp` is serialized in the hub at `Assets/Scenes/Main_Castle_Overworld.unity:15483`
   (`_hp: 100`), so editing `HeartController.cs:97` alone would change nothing there. A rail knob read
   at the *consumer* sidesteps this entirely — one more reason to prefer a scalar at the read site.
2. **Some levers are oracle-pinned by an explicit owner ruling**, and a knob would fight the pin.
   `BuildTimerConfig.freeBuildSlots` is asserted `== 2` at
   `Assets/Editor/Regression/BuildEconomyRegression.cs:1416-1417` and
   `Assets/Editor/Regression/BuilderSkuRegression.cs:125`; `queueDepthPerLine` is asserted `== 5` at
   `Assets/Editor/Regression/ObsidianQueueRegression.cs:400-401`. These are **deliberate scarcity
   rulings, not drift.** Do not put them on the rail without the owner relaxing the pin first.

### 5.1 Progression / XP / talents

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Village/Hero/HeroProgression.cs:110` | `150 + 350(L-1) + 500(L-1)²` | hero XP-to-next-level curve | `progression.xpToNextPct` | int pct |
| `Village/Hero/HeroProgression.cs:126` | `level <= 8 ? 2 : 3` | Wisdom per level-up — the sole direct Wisdom faucet | `progression.wisdomPerLevelEarly` / `.Late` | int ×2 |
| `Village/Hero/HeroProgression.cs:360` | `StarterPoints = 2` | bonus points granted on the first level-up | `progression.starterPoints` | int |
| `Village/Hero/HeroProgression.cs:55` | `DamagePerLevel = 0.06f` | +6% ability damage per hero level | `progression.damagePerLevelPct` | int pct |
| `Village/Hero/HeroProgression.cs:56` | `MaxDamageMultiplier = 3f` | ceiling on level-driven damage scaling | `progression.maxDamageMultPct` | int pct |
| `Core/Progression/SkillSystem.cs:115` | `NewPlayerPointGift = 2` | skill points a brand-new save starts with | `progression.newPlayerPointGift` | int |
| `Village/Progression/TierSystem.cs:108-113` | `(5,5) (10,8) (15,12) (20,15) (25,20) (30,25)` | level→Wisdom milestone table | *table — Tier 2* | — |
| `Village/Progression/TierSystem.cs:117-119` | `RepeatEvery 10`, `RepeatStart 40`, `RepeatWisdom 30` | post-L30 repeating Wisdom cadence | `progression.repeatTierWisdom` etc. | int ×3 |
| `Village/Progression/ProgressionManager.cs:36,38,40,42` | `0.25f`, `0.20f`, `0.5f`, `6f` | top-damager credit share; +20% XP per wave; kill XP = enemy maxHP × 0.5; XP floor per kill | `progression.killXpFromMaxHpPct`, `.xpPerWaveBonusPct`, `.minKillXp` | int ×3 |
| `Village/Population/PopulationBootstrap.cs:56-58` | `150`, `300`, `80` | population XP per quest / outpost / wave (drives Echo-slot unlocks) | `progression.popXpPerQuest` etc. | int ×3 |
| `Village/Waves/WaveFeedbackDirector.cs:42` | `WisdomPerWave = 0` | per-wave Wisdom — **deliberately zeroed; a live lever sitting at 0**, not dead code | `progression.wisdomPerWave` | int |

**Talent node values are already JSON** — `hero-talents.json` v2, **83 nodes measured** (knight 32 /
ranger 20 / mage 20 / shared 11), with `tierCosts` 1/2/3/5, `sharedNodeCost` 2, `respecCostCrystals`
300, and every node carrying its own `effect.value`. What is **not** JSON is every **ceiling**:
`Assets/_Modules/Village/Talents/HeroTalentModifiers.cs:54-58, 69-71, 150-160, 253-259` — roughly 30
`Max*` / `Min*` floats bounding damage (3×), cooldown (0.4×), HP (3×), incoming reduction (0.85), block
(0.85), attack speed (2.0), move speed (1.75), **crit chance (0.50)**, harvest rate, collector cap,
build-cost reduction, salvage, tower damage / range / attack speed, structure toughness, and the mage's
spell-power / mana-cost / shell / max-mana caps. **These decide whether a talent build ever *feels*
strong.** Treat as one Tier-3 cluster, not as 30 separate knobs.

### 5.2 Build / upgrade time and cost — the densest single target

> ⛔ **`BuildTimerConfig` is a `ScriptableObject` with `[Tooltip]` and `[Min]` attributes, and there is
> NO backing asset in the tree.** `BuildTimerService.cs:2070-2071` calls
> `Resources.Load<BuildTimerConfig>(BuildTimerConfig.ResourcesPath)` and falls back to
> `BuildTimerConfig.CreateDefault()` (`:373-379`). Verified: `find Assets -path "*Resources/Economy*"`
> returns **nothing**, and no `.asset` anywhere in the tree references the type. **Every field
> initializer in this file is therefore the live value and needs a rebuild to change** — in the one
> file whose own comment at `:219-221` says the queue cap is *"Authored as DATA so 'upgradable later'
> is a data change, not a code change."* That sentence is, today, false. **This is the owner's
> complaint, stated inside the file that most loudly denies it.**
>
> **Cheapest possible fix, and it needs no code at all:** author one
> `Assets/Resources/Economy/BuildTimerConfig.asset`. That converts all ~19 rows below from category (2)
> to category (1) — the loader already prefers it. It does **not** make them remotely tunable (§2), but
> it makes them editable without touching C#, and it is a five-minute change.

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Core/Catalog/BuildTimerConfig.cs:52` | `45f` | tier-0 build duration | `build.baseSecondsPct` | int pct |
| `:56` | `3.2f` | per-tier duration growth | `build.tierGrowthPct` | int pct |
| `:59` | `24 h` | ceiling on any one job | `build.maxDurationSeconds` | int |
| `:62` | `1.25f` | upgrades vs a fresh build of the same tier | `build.upgradeMultiplierPct` | int pct |
| `:94` | `{600,1200,2400,4200,7000,11000,17000}` | cost-basket → build-tier bands | *array — Tier 2* | — |
| `:292` | `wood + 1.5·iron + 1.0·food + 2.0·crystals` | the basket weighting that assigns every structure its tier | *formula — Tier 2* | — |
| `:109, :120, :123` | `600f`, `3`, `4 h` | rewarded-ad skip size, count per window, window length | `monet.adSkipSeconds`, `.adSkipsPerWindow`, `.adSkipWindowSeconds` | int ×3 |
| `:128, :131, :174` | `1`, `10`, `0.75f` | crystal instant-finish per minute, price floor, curve exponent | `monet.instantFinish*` | int ×3 — **§6.2** |
| `:192` | `15f` | first-build grace (onboarding pace) | `build.firstBuildSeconds` | int |
| `:196` | `2` | free concurrent builders | — **oracle-pinned, §5.0.2** | — |
| `:200` | `24 h` | temporary-builder grant duration | `build.temporaryBuilderSeconds` | int |
| `:223` | `5` | queue depth per line | — **oracle-pinned, §5.0.2** | — |
| `:227, :230` | `250`, `2` | extra queue slot crystal price; Echo floor to unlock buying | `monet.extraSlotBaseCrystals`, `build.extraSlotEchoFloor` | int ×2 |
| `:266, :270` | `60f`, `0.6f` | building-perk research floor; research seconds per gold | `build.researchBaseSeconds`, `.researchSecondsPerGoldPct` | int ×2 |
| `Village/BuildMode/BuildModeController.cs:2884-2887` | bare `/ 2` ×4 | **the 50% sell/salvage refund** — no named constant, so it is invisible to a grep for `Refund` | `economy.sellRefundPct` | int pct |
| `Village/BuildMode/BuildModeController.cs:2718-2725` | `Mathf.Max(1, fromLevel)` | fallback linear upgrade-cost multiplier for catalog rows with no authored `upgradeCost` | `economy.upgradeCostFallbackPct` | int pct |
| `Village/Buildings/Progression/VillageTierService.cs:46` | `250 * next` | crystal cost to reach village tier N — the comment says *"tunable in v2"* | `economy.villageTierCrystals` | int |

### 5.3 Economy — production, storage, offline

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Village/Buildings/Progression/ResourceBuildingProgression.cs:189` | `{50, 42.5, 35, 27.5, 20}` | harvest tick interval by level — **the master faucet** (duplicated at `:343`) | `economy.harvestIntervalPct` | int pct |
| `:250, :259, :273` | farm `13 / +4`, mill `10 / +3`, forge `6 / +2` | per-tick yield base and per-level step | `economy.harvestYieldPct` (one scalar) | int pct |
| `:252, :261, :275` | `130 / ×1.9`, `125 / ×1.9`, `200 / ×2.0` | upgrade cost base and geometric per-level step | `economy.buildingUpgradeCostPct` | int pct |
| `:325, :328` | `18f`, `1.1f` | Arcane Forge tick interval and haul multiplier | `economy.arcaneHarvestIntervalPct` etc. | int pct ×2 |
| `Village/Harvest/OfflineHarvestService.cs:98` | `10f` | **offline hours credited per claim** | `economy.offlineCapHours` | int |
| `Village/Buildings/Progression/ResourceCollector.cs:597-598` | `50.0`, `× 2.0` | fallback collector capacity (~2 h of production at current level) | `economy.collectorCapacityHoursPct` | int pct |
| `Village/Buildings/Progression/ResourceCollector.cs:41` | `DefaultMaxHp = 120f` | collector HP, which scales accrual linearly via `HpFraction` | `economy.collectorMaxHpPct` | int pct |
| `Village/Buildings/Progression/AutoHarvestService.cs:27` | `TickInterval = 20f` | auto-collect sweep cadence | `economy.autoHarvestIntervalSeconds` | int |
| `Village/Harvest/EchoBonusCalculator.cs:89-91` | `3600f`, `900f`, `4f` | Echo income **magnitudes** (the percentages are JSON) | `economy.echo*PerHour*` | int ×3 |
| `Core/Economy/TownBankCapacity.cs:256` | `AbsoluteMinBaseCap = 1000` | hard floor under whatever `storage-caps.json` authors | `economy.absoluteMinBaseCap` | int |
| `Village/Population/PopulationService.cs:56` | `{5, 8, 12, 16}` | population cap per village tier — comment says *"owner-tunable"*, it is a code array | *array — Tier 2* | — |
| `Village/Population/PopulationService.cs:52` | `MaxEchoSlots = 5` | ceiling on Echo workforce slots | `economy.maxEchoSlots` | int |
| `Village/Buildings/HealingFountain.cs:70` | `{1.0f, 2.0f, 3.5f}` | healing-fountain output per building level | `economy.fountainOutputPct` | int pct |
| `Village/Monetization/HarvestBoostService.cs:71,74,83,86` | `2.0f`, `2.0f`, `120`, `4 h` | 2× harvest boost: cap, value, crystal price, duration | `monet.harvestBoost*` | int ×4 — **§6.2** |
| `Village/Monetization/ConvenienceRedeemer.cs:51,54` | `24 h`, `2.0f` | XP-weekend / auto-collect window and cap | `monet.convenienceWindowSeconds`, `.xpMaxMultPct` | int ×2 |
| `Village/Monetization/DailyChestController.cs:20` | `BaseGold = 500` | daily chest payout | `economy.dailyChestGold` | int |
| `Core/State/DifficultyTuning.cs:73, 76, 79` | `300f`, `600f`, `180f` | Normal / Easy / Hard build-window seconds — **absent from `difficulty-profile.json`, which authors every other difficulty knob** | `waves.buildWindowSecondsNormal` etc. | int ×3 |
| `Core/State/NestedTypes.cs:106` | `StrategicGold = 200` | strategic-placement gold seed | `economy.strategicGold` | int |
| `Village/Buildings/CrystalMine.cs:74` | `DefaultCrystalsPerWave = 1` | crystals a crystal mine pays per wave | `economy.crystalsPerWave` | int |

### 5.4 Combat — hero, abilities, melee

Per-ability `cooldown` / `manaCost` / `damage` / `range` are **already JSON**: `abilities.json` v6,
**43 ability definitions measured** (mage / knight / ranger 4 each, plus 31 skill entries), read via
`Village/Hero/AbilityCatalog.cs:259`. The three class `resource` blocks (`max` / `regenPerSecond` /
`onHitRestore`) are JSON too, with C# fallbacks at `HeroAbilities.cs:53, 56`. Everything below is what
is **not** in any JSON.

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Village/Hero/HeroHealth.cs:39` | `_maxHp = 100f` | **hero base max HP — no data seam at all** | `combat.heroMaxHpPct` | int pct |
| `HeroHealth.cs:45, 46, 47, 49` | `1.5f`, `1.0f`, `6f`, `4` | enemy contact: engage radius, tick interval, fallback damage, **max attackers per tick** | `combat.contactDamageIntervalPct`, `.maxAttackersPerTick` | int ×2 |
| `HeroHealth.cs:60` | `0.45f` | damage taken while HUD-blocking | `combat.blockDamagePct` | int pct |
| `HeroHealth.cs:78` | `0.25f` | Aegis auto-fire HP threshold | `combat.aegisAutoThresholdPct` | int pct |
| `HeroHealth.cs:101, 106` | `1.75f`, `1.5f` | down time before respawn; post-respawn invulnerability | `combat.downSecondsPct`, `.respawnInvulnPct` | int pct ×2 |
| `HeroHealth.cs:198, 224` | `0.30f`, `0.85f` | injured threshold; move-speed scale while injured | `combat.injuredFractionPct`, `.injuredMoveScalePct` | int pct ×2 |
| `Village/World/SafeZoneRecovery.cs:52` | `0.12f` | **out-of-combat hero regen**, fraction of max HP per second | `combat.townRegenBasisPts` | int (basis pts) |
| `Village/Enemies/PlayerAttackController.cs:43, 90` | `52.5f`, `0.6f` | **melee primary damage and cadence** | `combat.meleeDamagePct`, `.meleeCooldownPct` | int pct ×2 |
| `PlayerAttackController.cs:47` | `3.2f` | melee hit radius — a full 360° `OverlapSphere`; **there is no swing arc in this file** | `combat.meleeRangePct` | int pct |
| `PlayerAttackController.cs:145, 149, 157` | `0.03f`, `0.13f`, `1.25f` | perfect-hit window open / close, and its damage multiplier | `combat.perfectHitMultPct` | int pct |
| `PlayerAttackController.cs:212, 213` | `3`, `1.1f` | combo length; combo reset gap | `combat.comboLength` | int |
| `PlayerAttackController.cs:349, 350, 351` | `0.25f`, `2.0f`, `3.0f` | parry window, riposte window, **riposte damage ×3** | `combat.parryWindowPct`, `.riposteMultPct` | int pct ×2 |
| `PlayerAttackController.cs:710` | `damage *= 1.5f` | **crit damage multiplier** — crit *chance* is a talent, the multiplier is not | `combat.critMultPct` | int pct |
| `Village/Hero/HeroLocomotion.cs:116, 117` | `6.0f`, `5.0f` | hero run speed open-world / combat — the comment at `:115` says **"Tunable."** | `combat.heroMoveSpeedPct`, `.heroCombatMoveSpeedPct` | int pct ×2 |
| `Core/Combat/ElementalDamageResolver.cs:32, 33` | `1.25f`, `0.75f` | elemental vulnerable / resisted multipliers | `combat.elementVulnerablePct`, `.elementResistedPct` | int pct ×2 |
| `Village/Hero/HeroAbilities.cs:1568` | `0.05f` | floor on the incoming-damage multiplier (mitigation caps at 95%) | `combat.minDamageTakenPct` | int pct |
| `HeroAbilities.cs:1569, 1570` | `40f`, `4f` | Arcane Shell reduction % and duration when the def authors none | *fallbacks — low priority* | int ×2 |
| `HeroAbilities.cs:2217-2222` | `0.25f`, `0.50f`, `8f`, `0.05f`, `2f`, `0.20f` | Warden's Grace: base heal %, defense-scaled bonus, shield seconds, HoT %, HoT tick, damage reduction | `combat.grace*` | int pct ×6 |
| `HeroAbilities.cs:2098, 2150` | `1f`, `1f` | burn / poison DoT tick intervals | `combat.dotTickSecondsPct` | int pct |
| `HeroAbilities.cs:2063` | `4f` | burn DoT duration fallback | `combat.burnDotSecondsPct` | int pct |
| `HeroAbilities.cs:1859` | `AmmoPoisonStacks = 2` | Venomtip max concurrent poison stacks | `combat.ammoPoisonStacks` | int |
| `HeroAbilities.cs:1977-1979` | `6f`, `1.5f`, `0.4f` | knockback impulse; slow duration; freeze duration | `combat.knockback*` | int pct ×3 |
| `HeroAbilities.cs:2832, 2833` | `3.4f`, `45f` | default melee reach; ranged cast placement reach | `combat.meleeDefaultReachPct` | int pct |
| `HeroAbilities.cs:405, 415` | `2f`, `1.25f` | ranged-vs-melee classification factor; shot escape-range grace | *low priority* | int pct |
| `Village/Hero/RangedAttackVFX.cs:40, 43, 50` | `18f`, `0.4f`, `24f` | arrow speed, arrow arc, spell-orb speed | `combat.projectileSpeedPct` | int pct |
| `Village/Hero/ProjectileMover.cs:85-88` | `3f`, `1f`, `2f`, `30f` | projectile lifetime factor, grace, floor, ceiling | *low priority* | int ×4 |
| `Village/Arena/BattleArena.cs:110, 117` | `240f`, `2.5f` | arena fight hard timeout; out-of-arena grace | `combat.arenaTimeoutSeconds` | int |

### 5.5 Enemies, waves and difficulty

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Village/Waves/WaveScalingCurve.cs:71-72, 80-81, 90-91` | HP ×2.5, speed ×1.4, damage ×2.0 by wave 20 | **the enemy power ramp — no JSON exists for it** | `waves.enemyHpScalePct`, `.enemySpeedScalePct`, `.enemyDamageScalePct` | int pct ×3 |
| `Village/Waves/WaveScalingCurve.cs:58, 61` | `Max(1f, …)`, `Max(0.5f, …)` | floors under the HP / speed multipliers | *guards — low priority* | — |
| `Village/Waves/WaveCompositionBuilder.cs:149-152` | `4`, `0.9f`, `22`, `5` | wave size ramp, cap, elite cadence | `waves.baseCount`, `.countPerWavePct`, `.maxCount`, `.eliteEveryNth` | int ×4 |
| `WaveCompositionBuilder.cs:189, 193, 198-199` | `1.0`; `0.6/0.4`; `min(0.4, 0.12+0.03(w-6))`; `0.4` | weak / medium / strong tier mix by wave band | *cluster — Tier 3* | — |
| `WaveCompositionBuilder.cs:334-336, 346` | `w<=2`, `w<=5` | brute-family and elite-family unlock bands | *cluster — Tier 3* | — |
| `Village/Waves/SmartEnemySpawner.cs:509, 512` | `TwoSideFromWave 5`, `FourSideFromWave 10` | first two-gate and four-gate waves — the "step that matters" | `waves.twoSideFromWave`, `.fourSideFromWave` | int ×2 |
| `Village/Waves/WaveManager.cs:303` | `_maxSimultaneousEnemies = 8` | live-enemy cap; the excess is held as reinforcements (changes arrival pacing) | `waves.maxSimultaneousEnemies` | int |
| `Village/Waves/WaveManager.cs:483` | `EndlessCycleStartWaveId = 4` | which authored wave endless mode replays from | `waves.endlessCycleStartWaveId` | int |
| `Village/Enemies/Enemy.cs:222` | `EnemyAttackIntervalScale = 1.12f` | **global scale over every enemy's authored attack interval** — a lane-wide pacing lever on top of the JSON | `waves.enemyAttackIntervalPct` | int pct |
| `Village/Enemies/Enemy.cs:194, 199` | `7f`, `2.5f` | hero-aggro radius and its hysteresis — **not in `aggro-tuning.json`** | `waves.heroAggroRadiusPct` | int pct |
| `Village/Enemies/Enemy.cs:162, 159, 152` | `2.5f`, `3f`, `1.1f` | Heart arrival radius; structure sweep radius; melee contact reach | *cluster — Tier 3* | — |
| `Village/Enemies/Enemy.cs:1789, 1797` | `1.0f`, `1.2f` | minimum melee telegraph / ranged wind-up — **the player's reaction window** | `combat.enemyTelegraphFloorPct` | int pct |
| `Village/Enemies/EnemyBrain.cs:83-92` | `6f`, `0.7f`, `15f`, `2f` | enemy **healer** scan radius, threshold, heal amount, interval | `waves.enemyHealAmountPct`, `.enemyHealIntervalPct` | int pct ×2 |
| `EnemyBrain.cs:69, 74, 79` | `12f`, `20f`, `11f` | threat / tower / hero acquisition radii | `waves.enemyScanRadiusPct` | int pct |
| `EnemyBrain.cs:96, 99` | `8f`, `1.0f` | brain-driven attack fallback damage / cooldown (no `EnemyDef`) | *fallbacks* | int ×2 |
| `EnemyBrain.cs:334-337` | `10f`, `6f`, `1.5f`, `1.6f` | caster kiting envelope and fire rate | *cluster — Tier 3* | — |
| `EnemyBrain.cs:518, 551` | `TargetEvalInterval 2f`, `ProvokeDuration 6f` | how fast enemies notice you; taunt hold duration | `waves.targetEvalIntervalPct` | int pct |
| `Village/Arena/BattleArena.cs:1681, 1692-1709, 1743` | `+8%/threat`; nine full stat lines (Warlord `520/34/2.6/1.8`, Hollow Walker `55/9/2.8/1.3`, …); `AggroRadius 18f` | **arena enemy stat blocks synthesized in code** — the file admits at `:1704` they are not read from `enemies.json` | *cluster — Tier 2, wire to `enemies.json`* | — |
| `Village/Arena/BattleArena.cs:170` | `BossSpawnChance = 0.05f` | ~5% of arena encounters gain a boss | `waves.arenaBossChancePct` | int pct |
| `Village/World/RegionMobSpawner.cs:580-588, 480, 602` | five roamer stat lines; `Lerp(0.35f, 1f, BestWave/6f)`; `AggroRadius 14f` | overworld roamer stats and the early-game ease ramp | *cluster — Tier 2* | — |
| `Village/World/Camps/EnemyOutpost.cs:56, 59, 902-921, 940-963` | `BaseGuardCount 5`, `GarrisonRing 6f`, Warlord `420/22/16`, guard stat rows, `+10%/threat` | outpost garrison size, boss and guard stats, threat scale | *cluster — Tier 2* | — |
| `Village/World/Camps/CampDefenseWave.cs:45, 48, 124, 284` | `4`, `SpawnRing 14f`, `+clamp(threat/2,0,3)`, `+10%/threat` | counterattack wave size, ring and scaling | `waves.campDefenseBaseCount` | int |
| `Village/Enemies/OverworldEncounterSpawner.cs:43-51, 65-76, 103-104, 999-1016` | pack `1..7`; `RepChaseSpeed 6.3f`; `RepCount 6`; `ScatterRespawn 180f`; `ScatterLiveCap 8`; aggro/engage/leash `14/2.6/14`; `PostLossGrace 3.5f` | overworld population, chase speed **vs the hero's 6.0**, respawn cadence, aggro envelope | `world.repChaseSpeedPct`, `.repCount`, `.scatterRespawnSeconds` | int ×3 |
| `Village/Enemies/OutpostEnemyGroupSpawner.cs:59, 60, 66, 85` | `3`, `7`, `10f`, `6f` | dungeon/outpost group size, leash and wake radius | *cluster — Tier 3* | — |
| `Village/World/Camps/GarrisonStatBlocks.cs:37` | `GlobalDifficultyMult = 1.2f` | one global multiplier over every garrison stat block | `raid.garrisonDifficultyPct` | int pct |
| `Village/Waves/WaveData.cs:131` | `AggroRadius = 8f` | ⚠ a "fallback" with **no JSON field to fall back from** — no `enemies.json` row supplies `aggroRadius`, so it is effectively hardcoded | `waves.defaultAggroRadiusPct` | int pct |

### 5.6 Drops, rewards and payouts

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Village/World/Camps/EnemyOutpost.cs:96-98` | `0.04f`, `0.00f`, `0.04f` | **gear drop chance** per raid-clear roll, threat scaling (disabled), cap | `loot.gearDropChanceBasisPts` | int (basis pts) |
| `EnemyOutpost.cs:84-87` | `40`, `12`, `20`, `8` | wood / iron clear payout base and per-threat | `loot.outpostWoodBase` etc. | int ×4 |
| `EnemyOutpost.cs:101-104, 110-111` | `4`, `0.30f`, `75`, `15`, `6`, `0.25f` | rare-gem gate / chance / crystals / per-threat; quest-token gate / chance | `loot.rareGem*`, `.questItem*` | int ×6 |
| `EnemyOutpost.cs:67, 69, 666, 690` | `40`, `120`, `+10/threat`, `+25/threat` | flat raid-clear crystals and XP | `loot.clearCrystals`, `.clearXp` | int ×2 |
| `Village/Arena/BattleArena.cs:3144-3146, 3163, 3166, 1752` | `0.04f`, `0.65f`, `0.5f`, `0.15f` | arena gear drop chance; common/uncommon split; weapon/armor split; reward variance | `loot.arenaGearChanceBasisPts`, `.arenaCommonSplitPct` | int ×2 |
| `Village/Arena/BattleStarRating.cs:22, 24, 28-32` | `90f`, `120f`, `1.00 / 1.25 / 1.50` | star thresholds by clear time; reward multiplier per star | `loot.threeStarSeconds`, `.twoStarSeconds`, `.starRewardMultPct` | int ×3 |
| `Village/Troops/RaidScoring.cs:83, 92-98, 161, 175, 342, 375` | `180f`; `25 / 60 / 10 / 20`; `0.50f / 0.30f`; `0.70f`; `0.5f` | raid clock; loot base + per-star; destruction weights; survival gate; 1-star floor | `raid.clockSeconds`, `.lootBaseCrystals`, `.spireWeightPct`, … | int ×6 |
| `Village/World/Camps/RaidClaimService.cs:78` | `RepeatClearLootMultiplier = 0.25f` | **repeat-clear loot** — the farm-suppression lever | `raid.lootRepeatClearPct` (corrected 2026-09-09 WO-1461 RESULT; proposed key was `loot.repeatClearPct`) | int pct |
| `Village/World/Camps/CampDefenseWave.cs:51, 54, 236-237` | `25`, `60`, `+8/threat`, `+15/threat` | camp-defense crystals and XP | `loot.campDefend*` | int ×4 |
| `Village/World/Camps/ChallengeOutpostVictoryController.cs:44, 45` | `120`, `120` | challenge-outpost clear gold and XP | `loot.challengeClearGold`, `.challengeClearXp` | int ×2 |
| `Village/Enemies/Enemy.cs:3151` | `max(4, round(XpReward * 0.4f))` | **gold-from-XP fallback** for any enemy with `coinReward: 0` — the *input* to the `kill-rewards.json` material formula, and it is code | `loot.goldFromXpPct`, `.minGoldPerKill` | int ×2 |
| `Village/Waves/KillComboTracker.cs:58, 59` | `25`, `60` | tier-2 / tier-3 kill-combo gold | `loot.combo*Gold` | int ×2 |
| `Core/Catalog/DungeonRunGrade.cs:88, 91` | `8`, `3` | dungeon star-grade kill and depth thresholds | `dungeon.engagementKills`, `.deepDelveFloor` | int ×2 |
| `Core/Catalog/DungeonRunPayout.cs:92` | `BaseRollCap = 5` | jewel-polish rolls per run | `dungeon.baseRollCap` | int |
| `Village/Arena/ArenaDefenseCatalog.cs:80` | `DefensePointPool = 50` | arena defense budget — carries its own `// TODO data-driven: arena-defense.json` | `raid.defensePointPool` | int |
| `Village/Enemies/OverworldEncounterSpawner.cs:926, 933-954` | `0.15f`; `+8%/threat`, `Hp 98f`, `XpReward 14×bodies×scale` | pack bounty variance and the pack-leader body | *cluster — Tier 2* | — |

### 5.7 Defense structures, Heart, walls, siege, raid

> ⛔ **TWO CANONICAL FILES HAVE NO RUNTIME READER AT ALL.** Verified by grepping `Assets/**/*.cs` for
> `heart.json` and `towers.json`: the only hits are
> `Assets/Editor/Regression/DataWebRegression.cs:155, 158` — which asserts the files are *served*, not
> that anything *reads* them — and one comment at `Village/Vfx/ArcaneCrownAura.cs:60`. **They are dead
> data,** and the shipped values differ from the authored ones:
>
> - **`heart.json`** authors `maxHp 160`, `regenPerSecondOutOfCombat 0`, phases `0.6 / 0.25`. The game
>   ships **100 HP, 2 HP/s regen, phases 75 / 40** — a materially *easier* Heart than the design says —
>   with `100f` written out in **three** places (`HeartController.cs:97`, `HeartRegen.cs:76`,
>   `HeartHudBridge.cs:40`).
> - **`towers.json`** authors a full `range` / `damage` / `cooldownSeconds` ladder plus `slotsPerZone 3`.
>   The live ladder is `structures-catalog.json` base × the hardcoded `{1, 1, 1.25, 1.55}` at
>   `BuildModeController.cs:2663`.
>
> **Wiring these two files would collapse ~10 constants into data at zero design cost** — the numbers
> are already authored and already reviewed. That is a better first move than adding knobs for them.

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Village/Heart/HeartController.cs:97` | `100f`, `[Range(0,100)]` | **Heart max HP = the loss condition** ⚠ scene-serialized, §5.0.1 | `combat.heartMaxHpPct` | int pct |
| `Village/Heart/HeartRegen.cs:61` | `_regenPerSecond = 2f` | Heart out-of-combat regen | `combat.heartRegenPct` | int pct |
| `Village/Heart/HeartRegen.cs:70` | `_combatPauseRadius = 18f` | enemy proximity that suppresses regen — the real "under siege" test | `combat.heartCombatPauseRadiusPct` | int pct |
| `Village/Heart/HeartAuraController.cs:101-103` | `75f`, `40f`, `100f` | Heart phase thresholds (diverge from `heart.json`'s 60 / 25) | *wire `heart.json` — Tier 2* | — |
| `Village/BuildMode/BuildModeController.cs:2663` | `{1f, 1f, 1.25f, 1.55f}` | **per-tier tower range + damage curve**; second copy at `StructureCardVM.cs:170` | `towers.tierMultPct` | int pct |
| `Village/Buildings/Tower.cs:43` | `MaxLevel = 3` | tower upgrade ceiling | `towers.maxLevel` | int |
| `Village/Buildings/Tower.cs:130`; `DefenseTower.cs:165`; `ArcaneTower.cs:122` | `200f`, `200f`, `160f` | **tower max HP — no catalog `hp` key exists anywhere** | `towers.maxHpPct` | int pct |
| `Village/Buildings/DefenseTower.cs:60-62` | `14f`, `8f`, `1.2f` | fallback range / damage / **fire rate**; `ApplyTierStats` deliberately never scales fire rate, so this is the shipped cadence wherever the catalog row omits it | `towers.fireRatePct` | int pct |
| `Village/Buildings/TowerCombat.cs:32-35` | `1.1f`, `0.2f`, `12f`, `22f` | L1 shot cooldown; idle re-scan cadence; fallback range / damage | `towers.baseCooldownPct` | int pct |
| `TowerCombat.cs:50-60` | `5`, `0.6f`, `2.5f`, `3.0f`, `4f`, `4f` | Mana Surge burst interval + multiplier; Glacial pulse + slow; Ember burn DPS + duration | *cluster — Tier 3* | — |
| `Village/Buildings/TowerDamageVisuals.cs:69-71` | `0.75f`, `0.50f`, `0.25f` | tower damage-state thresholds — **a parallel ladder that does not read `damage-states.json`** | *defect, §9* | — |
| `Village/Walls/WallSegment.cs:104` | `MaxHp = 100f` | every wall's HP track | `walls.maxHpPct` | int pct |
| `WallSegment.cs:101` / `WallTierData.cs:70` | `{1f, 1f, 1.6f, 2.56f}` ×2 | wall per-tier toughness — **one lever, duplicated in two files** | `walls.tierToughnessPct` | int pct |
| `Village/Walls/WallTierData.cs:66, 80, 84` | `25 wood`; `120 iron`; `200 iron + 40 crystals` | wall build and upgrade costs | `walls.buildWoodCost` etc. | int ×3 |
| `Village/Walls/WallTierData.cs:155` | `MaxReachableWallLevel = 1` | **hard gate** — build mode tops out at Stone, making `walls.json`'s Steel / Spiked tiers (incl. the 9 spike DPS) unreachable | `walls.maxReachableLevel` | int |
| `Village/Walls/WallLayout.cs:126, 129` | `28f`, `21f` | perimeter half-extents — **diverge from `walls.json halfSize: 25`** and set how much wall must be repaired | *defect, §9* | — |
| `Village/Gates/Gate.cs:81, 100` | `100f`, `0.25f` | gate HP; force-field collapse threshold | `walls.gateMaxHpPct` | int pct |
| `Village/Walls/WallRepairController.cs:554, 881` | `0.999f`; `wood 30 / iron 15` | full-rebuild billing threshold; emergency repair basket when the catalog row is missing | `walls.emergencyRepair*` | int ×2 |
| `Village/Walls/WallRepairController.cs:660` | `0.625f` | measured crystals-per-iron floor the repair rate must price above | *guard — leave* | — |
| `Village/Buildings/StructureBurn.cs:63, 69, 73` | `0.5f`, `0.02f`, `0.5f` | ignite HP fraction; **burn DPS as %/sec of max HP**; tick cadence | `structures.burnBasisPtsPerSecond` | int (basis pts) |
| `Village/Harvest/EchoRepairService.cs:121, 125` | `2f`, `4f` | banked mend ceiling (caps offline repair at 2 structures); mend offline window | `economy.echoRepairBankedMax`, `.echoRepairOfflineHours` | int ×2 |
| `Village/Waves/SiegeScheduler.cs:61, 66, 71` | `6f` h, `1`, `24f` h | **siege interval**, pending-siege queue depth, offline siege accrual cap | `siege.intervalHours`, `.maxPending`, `.offlineCapHours` | int ×3 |
| `Core/Defense/SiegeStakesBalance.cs:101` | `MaxStealFraction = 0.25f` | **code-side ceiling the JSON cannot exceed** | `siege.maxStealPct` | int pct |
| `Village/Troops/RaidDeployController.cs:649-653` | `5 m`, `20 m`, `45 m` | troop attrition recovery per camp difficulty — no data override path exists | `raid.attritionRecoveryPct` | int pct |
| `Village/World/Camps/RaidSpire.cs:78` | `_maxHp = 1200f` | **raid objective HP = raid length** | `raid.spireHpPct` | int pct |
| `Village/World/Camps/RaidGarrisonSpawner.cs:61, 64, 66, 72-76` | `36`; `3f`, `1.5f`; `16f / 8f / 0.8f` | live combatant cap; raid boss HP and damage multipliers; enemy turret range / damage / fire rate | `raid.bossHpMultPct`, `.turret*Pct` | int pct ×3 |
| `Village/Troops/ArmyReadiness.cs:51` | `FirstRaidMinDeployableSlots = 3` | army slots required before the first raid unlocks | `raid.firstRaidMinSlots` | int |
| `Village/Troops/ArmyComposition.cs:199` | `TrainQueueDepthCap = 5` | train-queue depth — upstream limit on deploy size | `raid.trainQueueDepthCap` | int |
| `Village/Waves/WaveManager.cs:210, 214` | `9f`, `0.5f` | breach ring radius around the Heart; breach arm delay | `waves.innerRingRadiusPct` | int pct |

### 5.8 Dungeons

| file:line | Today | Controls | Proposed key | Kind |
|---|---|---|---|---|
| `Dungeons/RandomEncounterTable.cs:44, 47, 50, 53` | `25f`, `0.012f`, `{1.0, 1.3, 1.7}`, `1.6f` | encounter cooldown; base rate/sec; per-tier rate; **darkness rate multiplier** | `dungeon.encounterRatePct`, `.darknessRateMultPct` | int pct ×2 |
| `RandomEncounterTable.cs:56, 59, 61, 64-66` | `0.03f`, `3.0f`, `12`, `0.6f / 0.3f` | quiet-stretch pity ramp and ceiling; reward cap per run (anti-farm); encounter-pool rarity weights | `dungeon.rewardCap`, `.poolWeight*` | int ×3 |
| `Dungeons/Lantern.cs:50, 53, 75, 79, 82, 167` | `6f`, `1.5f`, `0.25f`, `0.35f`, `30f`, `0.12f` | lit radius (the core risk lever), cloak bonus, low-oil warning, light floor, final warning, **the darkness latch** — none of these fractions are in `dungeon-balance.json`; only `maxOil` / `oilDrainPerSec` are | `dungeon.lanternRangePct`, `.darknessLatchPct` | int pct ×2 |
| `Dungeons/ComposedAmbushDirector.cs:22, 23, 45` | `6f`, `28f`, `8f` | ambush tick, minimum gap between ambushes, post-entry grace | `dungeon.ambushMinGapPct` | int pct |
| `Dungeons/DungeonController.cs:601` | `0.15f` | rough-stone drop rate after the first guaranteed one | `dungeon.roughStoneDropPct` | int pct |

> ⚠ **`Lantern.cs:68, 71` fallbacks are `_maxOil = 100f` / `_oilDrainPerSec = 1.6f`, and the `1.6` is a
> stale pre-fix value** — `dungeon-balance.json` authors `0.5`. If that JSON ever fails to load, the old
> 62.5-second burn returns silently. Not a lever; a latent defect. §9.

### 5.9 The BattleATB turn lane — hardcoded end to end, and ranked LOW on purpose

`Assets/_Modules/BattleATB/Engine/` is a C# port of a TypeScript engine, and **no `Resources.Load` or
`CanonicalJson` call exists anywhere in the module** for tuning data. Every number is a `const`:

- `Defs.cs:60-66, 79-80` — ATB scale `100`; fill multipliers `0.5` / `1.5`; **crit chance `0.12`, crit
  multiplier `1.6`**.
- `Defs.cs:91-100` — all ten status blueprints (Burn `3 turns / 6 potency`, Poison `4/4`, Bleed `4/3`,
  Slow `2`, Freeze `1`, Stun `1`, Regen `3/5`, Haste `3`, Shield `1`, Mark `3 / 0.3`).
- `Defs.cs:116-204` — all twelve hero abilities' cost / cooldown / damage / proc chance.
- `Defs.cs:218-236` — the three class stat blocks (Knight `180 HP / 24 atk`, Ranger `120 / 20`, Mage
  `90 / 16`); `:251-327` pet stat blocks and ability costs; `:381-448` enemy per-ability damage.
- `Combat.cs:30-35, 61, 64, 77` — element advantage `1.25` / disadvantage `0.85`; mark `1.3`; defend
  `0.5`; damage spread `±8%`.
- `BattleScaling.cs:29-62` — `BOSS_EVERY 6`; boss HP `+60%` per boss; enemy count `8 → 12`; enemy HP
  `+16%`/step; speed `+1.2%`/step capped `1.28`; heart damage `+5%`/step.

**This is the largest block the existing `int`-only registry could absorb with no type work** — nearly
every value is already an integer. **Rank it LOW anyway.** The village real-time lane is what the owner
felt-tests; this lane is reached only through specific encounter paths. And the honest first question
is whether it should become JSON-backed (like every other roster in the game) rather than knob-backed.
That is a Tier-3 decision, not a Tier-1 one.

---

## 6. ⛔ SERVER-AUTHORITATIVE — OUT OF SCOPE, AND WHY THAT LINE IS NOT NEGOTIABLE

**The game takes real money on Solana mainnet.** It is live on the dApp Store. Anything that decides an
**amount charged**, a **price**, or an **entitlement granted** is resolved by the server and must stay
there. A client-side override of any of these is **an exploit, not a feature**.

### 6.1 The hard exclusions — never propose a knob for any of these

| Surface | Authority | Why a client knob is an exploit |
|---|---|---|
| Pack **USD prices** (the `1.99 / 2.99 / 4.99 / 9.99 / 19.99 / 49.99` ladder, 29 SKUs) | `USD_ANCHORS`, `api/_lib/purchase-catalog.js:83-117` | The file states it directly (`:59-62`): the USD anchor is the only number a human authors, and the SKR amount is **derived server-side at purchase time**. |
| **Purchase amounts in base units** (the two pinned canaries) | `DEVNET_PACKS` / `MAINNET_PACKS`, `purchase-catalog.js:33-49` | Protocol constants checked by the verifier on **exact equality**, and mirrored under a price-parity law. |
| **Token decimals** (devnet 9 / mainnet 6) | `SKR_DECIMALS_BY_NETWORK`, `purchase-catalog.js:57` | The file's own recorded near-miss: reading one network's decimals for the other authorised a **1000× overcharge**, and `/verify` runs *after* settlement, so the money is already gone. |
| **Quote lifetime and settlement grace** | `QUOTE_TTL_SECONDS 300`, `QUOTE_SETTLEMENT_GRACE_SECONDS 180`, `purchase-catalog.js:121-128` | An unexpiring quote is a free option on a volatile asset. |
| **Rate source and caching** | `RATE_URL` / `RATE_SOURCE` / `RATE_CACHE_MS`, `purchase-catalog.js:133-136` | The client does **no** pricing arithmetic, by design. |
| **Entitlements / grants** | `api/_lib/sku-entitlement-read.js`, `patronage.js`, `benefactors.js`, `google-play-purchases.js`, `pi-payments.js` | What a player *receives* for money is a server record, not a client belief. |
| `PackSaleActive` / `PackSaleDiscountPct` | `ServerConfig.cs:165-166` (dead rail, §3) | A **discount** is a price. Even though this rail is inert today, do not revive it on the client side. |

### 6.2 ⚠ THE GREY ZONE, and it deserves a deliberate owner decision

Rows in §5 marked **§6.2** — the crystal instant-finish price (`BuildTimerConfig.cs:128, 131, 174`), the
ad-skip allowance (`:109, 120, 123`), the extra queue slot (`:227`), and the harvest boost
(`HarvestBoostService.cs:83, 86`) — are **not** purchases. They price **soft/premium currency the player
already owns** against **time**. They are client-resolved today and always have been.

But the tunables rail has a **device-local override layer that outranks the remote value**:
`PlayerPrefs["ff.tun.<key>"]` beats the database row (`RemoteTunables.cs:35-42` and `:375-381`). That
layer exists so a human at a device can bisect a crash — it is right for a diagnostic flag. **Putting a
crystal price behind it means a player who can edit PlayerPrefs sets their own instant-finish price to
zero.**

That is not a new hole in absolute terms — the value is already client-side — but it converts "patch the
binary" into "edit a preferences key", which is a materially different bar.

**Two honest options, and this is an owner call, not a tuning one:**
1. Ship these knobs and accept the exposure, on the grounds that no purchase has ever completed
   (project memory `published-but-payments-never-activated`) and the currency is not yet real.
2. Add a per-spec `LocalOverridable` flag to `TunableSpec` so economically-sensitive knobs read
   **remote-or-default only**, skipping the PlayerPrefs layer. That is a small, contained addition to
   the existing rail — **not** a second mechanism — and it is the recommendation if any of these ship.

### 6.3 Related rails that are NOT this one, so nobody confuses them

- `api/_lib/maintenance.js` + `MaintenanceService` — the six-area kill switch. Live, deliberately
  **cacheless**, and its "offline ⇒ everything open" ruling is about seals and **does not transfer** to
  tunables (`PROD022_TUNABLE_FLAGS.md:142-146`).
- `api/_lib/catalog-read.js` — a server-side **shop/build presentation** catalog (titles, icons, card
  art, visibility). Not balance; do not fold balance into it.
- `FeatureFlags` (`Assets/_Modules/Core/FeatureFlags.cs`) — ~50+ `ff.*` gameplay switches, resolved
  **PlayerPrefs-over-default only**. There is **no remote layer** on this family, and the tunables rail
  deliberately leaves it untouched with a distinct `ff.tun.` prefix so the two namespaces can never
  collide (`RemoteTunables.cs:121-122`). Making `ff.*` remotely settable is a separate, larger question
  and is out of scope here.

---

## 7. CATEGORY (3) — NOT BALANCE LEVERS, and why each is excluded

These were found by the same sweeps and are **deliberately not proposed**. Making a structural constant
remotely settable turns a compile-time invariant into a runtime failure mode, which is strictly worse.

| Kind | Representative cites | Why excluded |
|---|---|---|
| **Schema / representable-range ceilings** | `Core/Catalog/RepoProps.cs:111` `MaxStructureLevel = 6`; `Core/State/SaveSchema.cs:41` `CurrentVersion`; every `ExpectedVersion = 1` JSON guard | They bound what is *representable* and what a save can express, not what anything costs or yields. CLAUDE.md §8 names `MaxStructureLevel` as the single ceiling; a remote change would desync it from the save schema and the catalogs at once. |
| **Array and buffer bounds** | `SupportFieldStructure.cs:167` / `CaravanHealField.cs:82` `OverlapBufferSize = 32`; `HeroHealth` / `HeroAbilities` physics buffers sized `24`; `Defs.cs:75-76` `MAX_PARTY / MAX_ENEMIES 8`; `BuildTimerService.cs:1860` `MaxPublishedCards 24` | A remote value here is an out-of-range exception, not a balance change. |
| **NavMesh sample radii and pathing geometry** | `SmartEnemySpawner.cs:66` `NavSnap 8f`; `TroopFactory.cs:43` `NavSampleRadius 6f`; `HeartController.cs:282-283` blocker carve; `WallLayout.cs:138-147` gate width / section length / thickness | Spatial correctness. A wrong value produces un-spawnable enemies or un-walkable ground, not a harder game. |
| **UI layout pixels and fractions** | `HeroSkillTreePanelMvvm.cs:116-302` (~40 `*Px`); `TowerManagerPanel.cs:187-202`; `BuildMenuLayout.cs:55-117`; `HeroHealth.cs:1585-1596` debug overlay; `BuildTimerService.cs:884, 891` toast px | Layout, not balance. `widget-params.json` (10,617 numeric leaves — the largest canonical file by far) is entirely this and is excluded from every count in §2. |
| **VFX / animation / audio timing** | `HeroAbilities.cs:2955, 2989-3006` particle authoring; `Enemy.cs:206, 215, 280, 299`; `ProjectileMover.cs:59`; `DefenseTower.cs:102-114` projectile visual fit; `GameSfx.cs:65` `Rate = 44100` | Presentation. `DefenseTower.cs:922` explicitly confirms damage / targeting / travel never read those fit values. |
| **Loop guards, watchdogs and epsilons** | `Turn.cs:38, 56, 268`; `ATBRuntimeState.cs:93, 96`; `WaveManager.cs:403, 1418-1419, 1440`; `BattleQuiescenceGate.cs:115` `0.01f` epsilon; `ResourceCollector.cs:39` 30-day clock-tamper bound | Safety rails and float comparisons. Several exist specifically to survive bad data — putting them behind bad data inverts their purpose. |
| **Network timeouts, poll intervals, telemetry ring sizes** | every `RequestTimeoutSeconds` / `PollSeconds` under `Core/Ops`, `Core/Payments`, `Core/World`, `Core/Social`; `WebTrace.cs:80, 82` `RingCap 500` | Transport tuning. (`pi.requestTimeoutSeconds` is already on the rail as a PROD-022 *diagnostic* knob — that is a different purpose from balance.) |
| **Determinism seeds** | `WaveCompositionBuilder.cs:179` PRNG salts; `BattleController.cs:74` `_fallbackSeed = 42` | Reproducibility plumbing. |
| **Presentation-only scoring** | `Village/Waves/DefenseReportBuilder.cs:183-188` weights `100/75/40/5/20/35` | The file header states it: *"no reward, no matchmaking, no stake, no gate."* Verified — `DefenseScore` feeds only the report panel. |
| **Validation bounds on untrusted input** | `Core/Social/PublicTownSnapshot.cs:89, 95` `MaxStructureLevel 20`, `MaxPublicLevel 1000` | Guards against hostile inbound data. Remote-settable guards are not guards. |

---

## 8. THE PLAN — how to work this list incrementally, in priority order

Each tier is independently shippable and independently valuable. **Do not attempt the whole list.**

### Tier 0 — free wins that need no rail work at all (do these first)

| Action | Effect | Cost |
|---|---|---|
| Author `Assets/Resources/Economy/BuildTimerConfig.asset` | Converts **~19 category-(2) rows to category (1)**. The loader already prefers it (`BuildTimerService.cs:2070`); zero code change. | minutes |
| Wire `heart.json` | Collapses ~6 constants across three files into authored data that **already exists and is already reviewed**. Also fixes the undocumented 100-vs-160 / 2-vs-0 divergence. | small |
| Wire `towers.json`, or delete it | Ends a dead-data file that reads as authoritative. Either is better than the status quo. | small |
| De-duplicate the four known twins | `HarvestIntervalByLevel` (`ResourceBuildingProgression.cs:189` / `:343`), wall toughness (`WallSegment.cs:101` / `WallTierData.cs:70`), tower tier multiplier (`BuildModeController.cs:2663` / `StructureCardVM.cs:170`), Heart `100f` (three files). | small |

### Tier 1 — the rail, for the levers she'd touch tonight (§4 rows 1–8)

Ship as **integer-percent scalars at single-owner consumers**, default `100`, exactly like
`combat.drainReturnPct`. Four-file edit per knob (§0.2). ~15 keys covers rows 1–8 of the Top 15.

> **The invariant is the acceptance criterion, not a nicety:** every knob's build default must equal
> today's constant, so an offline player, a 404 and an empty table all get byte-for-byte today's game.
> `[tunable-defaults]` will assert it across the same seven failure paths as the existing eight knobs.

### Tier 2 — the wholesale fix: a remote `ICatalogSource`

Implement one `ICatalogSource` that fetches canonical JSON remotely, caches it the way
`RemoteTunablesService` caches its payload, and **falls back to `LocalJsonCatalogSource` on any failure**.
Assign it once at `CanonicalJson.Source` (`CanonicalJson.cs:34`). That converts the whole **5,224
numeric leaves** of category-1 data into remotely updatable content — gear tier curves, talent node
values, ability numbers, troop stats, enemy stats, loot tables, quest rewards — with **no call-site
change anywhere**, because the seam was designed for exactly this (`ICatalogSource.cs:8`).

**This is where the arrays and tables that §5 marks *"Tier 2"* belong** (build-tier bands, the cost
basket, population caps, the TierSystem milestone table, the arena/roamer/outpost stat blocks). Those
are content, not knobs, and forcing them through an int-keyed registry would be the wrong shape.

### Tier 3 — the long tail

Talent ceilings (`HeroTalentModifiers.cs`, ~30), the BattleATB lane (§5.9), tower perk numbers, kiting
envelopes, wave tier-mix bands. Real levers, but each is either niche, clustered, or better solved by
Tier 2. **Do not open these before Tiers 0–2 have landed.**

---

## 9. UNCERTAIN, AND DEFECTS FOUND ALONG THE WAY

Recorded as found and **not fixed** — this was a read-only pass.

### 9.1 Rows I could not classify cleanly

| Item | Why it is ambiguous |
|---|---|
| `HeroAbilities.cs:75` `_enemyHitRadius = 0.85f` | Documented as a *collision-radius correction* matching the React `ENEMY_HIT_R`, added to every authored range. It is geometry compensation, not an authored range — but it changes every ability's effective reach. **Called (3) here; a reasonable person could call it (2).** |
| `HeroAbilities.cs:2127` `_venomStacks.Count >= 32` | A ledger-prune threshold that also gates stack bookkeeping. Structural in intent, balance-adjacent in effect. |
| `WallSegment.cs:109` `SinkFraction = 0.85f` | How far a razed section sinks. Filed as a destruction *tell* (presentation), but it is authored alongside real balance. |
| `Defs.cs:75-76` `MAX_PARTY / MAX_ENEMIES = 8` | Array bounds **and** a design constraint on encounter size. Excluded as structural; flag if party size becomes a design axis. |
| `BossHealthBar.cs:53-54`, `DragonBoss.cs:372, 375` phase thresholds `0.60f / 0.25f` | Boss *phase pacing* rather than the ability surface any sweep targeted. Not enumerated; **a boss-pacing sweep is still owed.** |
| `HeroHealth.cs:47` `DamagePerEnemy = 6f` | Labelled a fallback, but it fires whenever an attacker's own `ContactDamage` is non-positive. How often that happens was not measured. |

### 9.2 Defects found (not levers — file separately)

1. **`heart.json` and `towers.json` are dead data** with values that differ from what ships (§5.7).
   `DataWebRegression.cs:155, 158` asserts they are *served*, which is exactly the kind of green check
   that makes an orphan look wired.
2. **`WallLayout.cs:126, 129`** (`28f`, `21f`) **contradicts `walls.json halfSize: 25`** — and it sets
   how much wall the player must pay to repair.
3. **`TowerDamageVisuals.cs:69-71`** runs a parallel damage-state ladder that never reads
   `damage-states.json`, so tower tells can drift from every other structure's.
4. **`WallTierData.cs:155` `MaxReachableWallLevel = 1`** makes the Steel and Spiked tiers authored in
   `walls.json` — including the 9 spike DPS — **unreachable in build mode**.
5. **`Lantern.cs:71` `_oilDrainPerSec = 1.6f`** is a stale pre-fix fallback against
   `dungeon-balance.json`'s `0.5`. A JSON load failure silently restores the old 62.5-second burn.
6. **`EchoBalanceCatalog`'s `perLevelBonus` code default (`0.05f`) no longer matches the retuned JSON
   value (`0.01`)** — a load failure would silently 5× the per-level Echo bonus.
7. **Three independent copies of "what a tower costs in crystals"** in the onboarding path — `150`
   (`Village/Tutorial/V2/TutorialFlow.cs:168`), `120` (`TutorialSignalAdapters.cs:66`), `50`
   (`DialogueCommandSink.cs:216`). No common source of truth.
8. **`ServerConfig` is a fully-wired client rail with no server half** (§3), and reading its comments
   makes eleven values look remotely settable when none are.

### 9.3 What this pass did NOT cover

- **Boss phase pacing** (`DragonBoss`, `BossHealthBar`) — named above, not enumerated.
- **Vendors, crafting, jeweler and consumable recipes** — surveyed as category (1) via
  `vendors.json` / `crafting-recipes.json` / `jeweler-recipes.json` / `consumable-recipes.json` /
  `jewel-polish.json`, but their **C# consumers were not swept** for hardcoded multipliers.
- **Quest and daily-quest reward *logic*** — the payouts are authored in `quests.json` (213 numeric
  leaves) and `daily-quests.json` (92), but any code-side scaling was not chased.
- **Audio mix, cosmetics, ad placements** — out of the balance scope by definition.

---

## 10. THE COUNTS — measured, with their limits stated

**Counted directly by this pass:**

| Measure | Value | How |
|---|---|---|
| `.cs` files under `Assets/_Modules` | **1,292** | `find … -name "*.cs" \| wc -l` |
| `.asmdef` under `Assets/_Modules` | **25** | same |
| Canonical JSONs, `Resources` / `StreamingAssets` | **71 / 71** | `ls … \| wc -l` on both trees |
| `const float` occurrences under `Assets/_Modules` | **1,961** | `grep -rn "const float" \| wc -l` |
| `const int` occurrences | **526** | same shape |
| `[SerializeField] … float` occurrences | **501** | same shape |
| Numeric leaves in canonical JSON, all files | **15,841** | JSON walk, `_`-prefixed keys skipped |
| …excluding `widget-params.json` (UI layout) | **5,224** | same walk |
| Ability definitions in `abilities.json` | **43** | parsed: mage/knight/ranger 4 each + 31 skills |
| Talent nodes in `hero-talents.json` | **83** | parsed: knight 32 / ranger 20 / mage 20 / shared 11 |
| Knobs on the rail today | **9** | `RemoteTunables.Registry`; 8 PROD-022 + 1 balance |
| Canonical files claiming "NO recompile" | **5** | grep: dungeon-balance, echoes-balance, kill-rewards, siege-stakes, vendors |

**Candidate category-(2) rows returned by the four domain sweeps** — reported per sweep because the
union is **not de-duplicated** and I did not measure the de-duplicated total:

| Sweep | Rows |
|---|---|
| Combat / abilities | 138 |
| Economy / progression | 72 |
| Enemies / waves / drops | 68 |
| Defense / siege / raid | 59 |
| **Raw sum (overlapping)** | **337** |

⚠ **Known overlaps, named rather than estimated:** `HeroProgression.cs` and `HeroTalentModifiers.cs`
appear in both the combat and economy sweeps; `EnemyOutpost.cs`, `CampDefenseWave.cs`,
`BattleStarRating.cs`, `RandomEncounterTable.cs`, `DungeonController.cs` and `DungeonRunPayout.cs` in
both enemies and economy; `RaidGarrisonSpawner.cs` in both enemies and defense; `ResourceCollector.cs`,
`WallTierData.cs` and `WallRepairController.cs` in both economy and defense; `Enemy.cs` and
`BattleArena.cs` in both combat and enemies. **At least 15 clusters are double-counted**, so the true
distinct total is lower than 337 and was not measured.

**What §5 actually enumerates, de-duplicated by hand: roughly 200 distinct constants across 8 domain
tables.** §4's Top 15 names **33** of them. That is the number to plan against.

---

## 11. Change log

| Date | Change |
|---|---|
| 2026-09-02 | Created at HEAD `9a657c8cb` on `feat/synty-art-retheme`. Four read-only domain sweeps + a first-hand pass over the rail, the data loader, and the purchase boundary. |

**Keep this file current the way CLAUDE.md §15 requires:** when a lever moves onto the rail, change its
row to name the key and strike it from the Top 15; when a constant is deleted or wired to data, update
or remove the row **in the same commit**. A stale inventory is worse than none — it is what the last
three attempts produced.
