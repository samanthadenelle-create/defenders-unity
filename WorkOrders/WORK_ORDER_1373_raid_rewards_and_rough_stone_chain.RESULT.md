# WO-1373 RESULT - the rough stone drops: top two raid tiers, one a day, 5% in non-starter dungeons

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane RAID-3)
**Lane:** RAID-3, edit-only. No Unity run, no gate, no `git add`, no commit.
**Branch:** `dev`. **Tree at start:** the WORKING TREE, dirty from the finished RAID / RAID-2 /
SPOILS / LOCOMOTION lanes - not HEAD.

⛔ **THIS RESULT CLOSES §4 AND §5's DROP AXIS ONLY.** §2b's three game-changer rings (build times
-20%, Echo yield +20%, troop ready -25%) are **NOT built** and were not in this lane's file list.
§2b's own open question - *can a player wear more than one at once, and do they stack* - is still
unanswered by the owner. §7's acceptance rows about the accessory ladder and the end-to-end capture
are therefore **not claimed**.

---

## 1. THE RULING, AND WHERE EACH HALF OF IT LANDED

Owner, 2026-09-09, verbatim:

> *"there is only one stone type till it gets to jeweler, and then its RND. So only top two tiers of
> raids can drop stone and no more than 1 per day. 5% drop rate in dungeons not included the starter
> dungeons"*

| Claim | Where it is enforced | How it is proven |
|---|---|---|
| ONE stone type | `RaidScoring.RoughStoneItemId` is BOUND to `DungeonExclusiveItems.RoughStoneId` (a `const` initialised from the catalog const, never re-typed) | `[one-stone]` |
| ...RND at the jeweler | **unchanged, verified only** - see §4 | source citation, §4 |
| only top two raid tiers | `RaidScoring.TierOf` + `ShouldDropRoughStone`, knob `raid.roughStoneMinTier` = 3 | `[tier-ladder]`, `[rule-table]`, `[defaults]` |
| no more than 1 per day | global UTC-day ledger in `RaidScoring`, knob `raid.roughStonePerDayCap` = 1 | `[rule-table]`, `[day-ledger]` |
| 5% in dungeons | `DungeonController.PostFirstRoughStoneDropPct`, knob `dungeon.roughStoneDropPct` = 5 | `[defaults]` |
| starters excluded | layout `tier` gate in `DungeonController.GrantRunPayout` | `[starter-gate]` |

---

## 2. (a) RAIDS - WHICH CONFIGS, AND WHERE THE DAY STAMP LIVES

### ⛔ THE FIRST FINDING IS A CORRECTION TO THE LANE BRIEF: THERE IS NO `tier` FIELD

The brief said *"read scene-configs.json to name which configs those are - quote the tier field"*.
**`Assets/Resources/Data/Canonical/scene-configs.json` carries no `tier` field on any config** -
every key on the four raid rows was dumped and read 2026-09-09. What it authors is:

| config id | `displayName` | `difficulty` | `rewardMultiplier` | `unlockVictories` |
|---|---|---|---|---|
| `raider_camp_small` | The Forsaken Camp | `Regular` | 1.0 | **0** |
| `fortified_garrison` | The Broken Garrison | `Hard` | 1.5 | **3** |
| `mage_enclave` | The Veiled Enclave | `Extreme` | 2.2 | **10** |
| `iron_bastion` | The Iron Bastion | `Extreme` | 2.2 | **20** |

**The tier ladder is a CODE table, not a data field:** `RaidLootTunables.CampIdCamp1` /
`CampIdCamp2` / `CampIdCamp3` / `CampIdBastion` (`RaidLootTunables.cs:169-175`), in map order, and
`RaidSelectionVM.cs:56` names the fourth in as many words - *"iron_bastion joined the list on
2026-09-04 (economy map §4, tier 4)"*.

**So "the top two tiers" = `mage_enclave` (3) and `iron_bastion` (4)**, and the two authored
orderings in the catalog corroborate it: `unlockVictories` ascends 0/3/10/20 in the same order, and
`difficulty` puts both of them at `Extreme`.

⚠ **ONE HONEST AMBIGUITY, NAMED NOT BURIED.** `difficulty` has only THREE values across four rows,
so "top two *difficulties*" would be `Hard` + `Extreme` and would admit `fortified_garrison` as
well. This lane read "tiers" as the four-rung camp ladder (which is what the economy map, the gold
table and the selection screen all use) and shipped `minTier = 3`. **`fortified_garrison` is the ONE
config a row of `raid.roughStoneMinTier = 2` flips**, so if her sentence meant difficulties it is a
console change, not a rebuild. `[rule-table]` asserts that exact effect in both directions.

### The grant, and the day stamp

- **Rule (pure, static, nothing loaded):** `RaidScoring.ShouldDropRoughStone(configId, grantedToday,
  minTier, perDayCap, out why)` - `Assets/_Modules/Village/Troops/RaidScoring.cs`.
- **Gate (raid-side):** `RaidVictoryController.GrantRoughStoneIfEarned(configId, stars)`, called
  from `HandleVictory` at **STEP 3.5c**, immediately after the crystal day-stamp and therefore after
  `GrantLoot` (STEP 3.5) and `RaidClaimService.RetainOverflow` (STEP 3.5b).
- **Grant (THE one site, shared with the dungeon):** `DungeonController.BankRoughStone` - reached
  from the raid through `DungeonRunPayout.GrantRoughStone`. **See §2b: this was corrected after the
  oracle caught a duplicated payout.**
- **⛔ WHY NOT INSIDE `GrantLoot`, and why not inside `ComputeLoot`.** The stone is an **item in the
  larder**, not an axis of `ResourceCost`, so it cannot ride the loot basket at all. And
  `RetainOverflow` measures `loot` against `_credited`, which is a *wallet* before/after
  measurement - putting an item grant inside that window is how a credited-delta measurement starts
  lying. It is a separate step in the same method, wrapped in one `Guard.Try` so a throw can never
  skip the victory screen or the army settle below it.

### §2b. ⛔ THE ORACLE CAUGHT A DUPLICATED PAYOUT. IT WAS RIGHT. CORRECTED 2026-09-09.

`Builds/wave1-reg` 23:34, verbatim:

> `composed-dungeon-run: [exit-pays] 2 sites write DungeonRunPayout.LastPolishScore
> (DungeonController.cs, RaidVictoryController.cs) - the payout was DUPLICATED rather than shared.
> Two payout authorities drift, then double-pay or disagree on the grade; WO-1112 required the
> composed exit to reach the SAME GrantRunPayout, not to copy it`

**The first implementation inlined `inv.AddEarned(...)` + `DungeonRunPayout.LastPolishScore = ...`
into `RaidVictoryController`.** That is exactly the duplicate-authority bug WO-1112 spent a day
undoing, and `ComposedDungeonRunRegression` case `[exit-pays]` (`:368-397`) exists to catch it: it
strips comments and strings from **every** `.cs` under `Assets/_Modules` and fails on a second writer
of `LastPolishScore =`. Nothing was argued with; the shape was rebuilt.

**⚠ AND A DIRECT CALL IS IMPOSSIBLE - the assemblies forbid it, which is why this needed a seam and
not a one-line move.** Read at source 2026-09-09:

```
Assets/_Modules/Dungeons/DeNelle.Dungeons.asmdef   references: [DeNelle.Core, DeNelle.Village, ...]
Assets/_Modules/Village/DeNelle.Village.asmdef     references: [DeNelle.Core, DeNelle.Commerce, ...]   <- no DeNelle.Dungeons
```

`DeNelle.Dungeons -> DeNelle.Village`, so `RaidVictoryController` (Village) **cannot name**
`DungeonController` (Dungeons) without a circular assembly reference. The seam is therefore declared
in `DeNelle.Core`, which both already reference - the same inversion CLAUDE.md §5 mandates for every
other cross-module call (`CoreServices.Hud` / `CoreServices.Audio`).

**The corrected shape, one owner per concern:**

| Layer | Where | What it owns |
|---|---|---|
| **The grant** | `DungeonController.BankRoughStone(score, via, detail)` (DeNelle.Dungeons) | bank + record the grade + announce. **THE ONLY writer of `LastPolishScore` in the project.** |
| **The seam** | `DungeonRunPayout.RoughStoneGrantAuthority` + `.GrantRoughStone(score, via)` (DeNelle.Core) | declares the call; **forwards or FAILS**, never banks |
| **Install** | `DungeonController.InstallRoughStoneGrantAuthority`, `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` | at BOOT, because a raid earns a stone in a `RaidBase_*` scene where no `DungeonController` instance exists |
| **Dungeon policy** | `GrantRunPayout` | engagement / claim / **starter-tier** / drop-roll gates, then calls `BankRoughStone` |
| **Raid policy** | `RaidVictoryController.GrantRoughStoneIfEarned` | **tier gate + per-UTC-day cap**, then calls `DungeonRunPayout.GrantRoughStone` |

⛔ **`GrantRoughStone` has NO local fallback, deliberately.** With no authority installed it emits a
`FlowTrace.Fail` naming the cause and returns false. Banking there "just in case" would make the
seam the second producer - worse than not paying, and precisely the regression the oracle guards.

**The polish grade is now an INPUT, per the coordinator's instruction, not a second table.** The raid
passes its settled star count (0..3, matching `DungeonRunGrade.MaxStars`) as the documented default;
**the star-to-grade mapping stays flagged as unruled** (§13 item 4) - nothing was invented.

**Two gates, one producer, and the day cap only spends on success:** `BankRoughStone` returns TRUE
only when the larder actually took the stone, so `MarkRoughStoneGranted()` is now called on that
return rather than optimistically - the `MarkCrystalsPaid` lesson, kept.

**Measured after the fix** (the same scan the oracle runs, replicated over the working tree):

```
scanned 1364 module .cs -> LastPolishScore writers: ['DungeonController.cs']
```

**One writer. `[exit-pays]` passes.** The rest of that case was re-verified too, since the extraction
moved code past its anchors: `public static void GrantRunPayout` present, and
`DungeonExitInteractable` still calls `DungeonController.GrantRunPayout`.

⚠ **`RoughStoneFanfareRegression` case `[announce-order]` searches the WHOLE `DungeonController.cs`
file, not `GrantRunPayout`'s body, so the extraction does not break it - but the ORDER it asserts had
to be preserved deliberately and was measured, not assumed:**

```
ShouldAwardPostFirstStone(UnityEngine.Random.value)  @35120   (in GrantRunPayout)
inv.AddEarned(stoneId, 1);                           @39335   (in BankRoughStone)
RaiseRoughStoneGranted(stoneId, score, firstDungeonStone);  @40240
   roll < add: True     raise > add: True
```

`"rough stone granted listeners"` and the event declaration are untouched.
`JewelerDiscoveryFtueRegression:35`, which also needs the literal `inv.AddEarned(stoneId, 1)`
somewhere in that file, still finds it.

⭐ **A SIDE EFFECT WORTH NAMING: open item 7 is now CLOSED.** The earlier RESULT flagged that a
raid-granted stone had no fanfare. Because the raid now reaches the same authority, it raises the
same `RoughStoneGranted` announcement - so a raid stone gets the same presentation beat a dungeon
stone does, with no second spawner and no listener that grants anything.

---

**THE DAY STAMP - where, and no schema bump.** `PlayerPrefs`, two keys, in `RaidScoring`:

```
dotr-raid-stoneday     the UTC day key   (DeNelle.Core.UtcDay.Key(), "yyyy-MM-dd")
dotr-raid-stonecount   the count paid that day
```

**No `SaveSchema` bump, and none was needed.** This is the same shape as the two neighbouring
records the brief pointed at: `RaidClaimService`'s crystal day-stamp
(`dotr-raid-crystalday-<configId>`, `RaidClaimService.cs:64`) and `DungeonRunPayout`'s polish FIFO
(`dungeon.pendingpolishscores`), whose own header states the convention - *"a schema bump is a
migration, and this is a small, self-healing, non-authoritative hint... the AUTHORITATIVE record of
what the player owns is the larder count, which is already persisted properly."* Losing this ledger
costs the player at most one extra stone. It **self-expires**: tomorrow's read compares against a
different day key and answers 0, so nothing ever cleans it up. `[day-ledger]` proves that by
stamping yesterday's key by hand.

⚠ **THE CAP IS GLOBAL, NOT PER CAMP, and that is a reading of her sentence.** *"no more than 1 per
day"* has no camp in it, and the crystal stamp beside it is deliberately per-camp - so a per-camp
stone stamp would have let the two eligible camps pay two stones a day. One row
(`raid.roughStonePerDayCap = 2`) reproduces that if she meant it per camp.

---

## 3. (b) DUNGEONS - 5%, AND HOW A STARTER DUNGEON IS IDENTIFIED IN DATA

**The field, quoted:** `"tier"`, in the authored layout at
`Assets/Resources/Data/Canonical/dungeon-layouts/<dungeonId>.json`. Read at source 2026-09-09:

```
dg_starter_loop      "tier": 1      dg_sunken_vault   "tier": 2
dg_healers_cottage   "tier": 1      dg_bonecrypt      "tier": 3
dg_folks_granary     "tier": 1      dg_ember_deep     "tier": 4
dg_hollow_roads      "tier": 1
```

**Tier 1 IS the starter band. There is no `starter` flag anywhere in the dungeon data** - checked
across `dungeon-graphs/`, `dungeon-layouts/`, `dungeon-balance.json` and `dungeon-kit.json`; the
string does not appear as a field in any of them. So the lane did **not** have to STOP on (b).

**The grant site, named:** `DungeonController.GrantRunPayout(DungeonRuntimeState st, string via)`
(`Assets/_Modules/Dungeons/DungeonController.cs`) - the single dungeon payout authority, called by
`DungeonController.ExitToVillage` and by `DungeonExitInteractable.cs:1098` (the composed exit).
**Its signature was NOT changed**, because that second caller is outside this lane; the tier is
resolved *inside* from `st.DungeonId`.

**What changed there:**
1. `PostFirstRoughStoneDropRate` was `public const float = 0.15f`. It is now a property over a new
   rail-backed percent, `PostFirstRoughStoneDropPct` (`dungeon.roughStoneDropPct`, default 5), read
   through `SpecFor` **before** `Int` so an unregistered key answers 5 rather than `Int`'s
   0-for-unknown - which here would mean *no dungeon ever pays a stone again*. **The name is
   deliberately unchanged** (two oracles read it).
2. A new `ResolveDungeonTier(string)` reads the layout's `tier`, and `GrantRunPayout` refuses the
   stone below `RoughStoneMinDungeonTier` (2).

⛔ **THE ONE DECISION IN (b) THAT NEEDS HER EYES - AND IT IS THE TOP OPEN ITEM.**
The gate covers the **guaranteed first stone** as well as the 5% roll, so a player who only ever
plays `dg_starter_loop` now never receives one, and **`JewelerProgression.IsUnlocked` is
`HasEverAcquired(RoughStoneId)`** - so this MOVES when the Jeweler is revealed, from the starter
dungeon to the player's first tier-2+ delve (or their first eligible raid). The alternative reading
- gate only the 5% roll, let a starter still pay the one-time introduction - keeps the FTUE where it
is but leaves starter dungeons dropping stone, which is the thing *"not included"* most plainly
refuses. **Both are one `if` apart. She should pick.**

⚠ **`ResolveDungeonTier` FAILS OPEN on an unknown tier** (missing or unparsable layout), and says so
in a `Warn`. Reasoning is written at the method: the gate exists to hold back the starters, which
are the ids most certain to have a layout on disk, and failing closed would silently and permanently
delete the Jeweler's only input on any dungeon whose layout had not been mirrored into Resources.

---

## 4. (c) THE JEWELER OUTCOME IS ALREADY RANDOM - VERIFIED AT SOURCE, NOTHING CHANGED

Read 2026-09-09, nothing edited:

- `Assets/_Modules/Village/Crafting/JewelPolishService.cs:201` -
  `TryStart(DungeonExclusiveItems.RoughStoneId, polishScore, ...)`: the rough stone is the INPUT and
  the gem is rolled at the bench.
- `:499` - `bool isRePolish = input != DungeonExclusiveItems.RoughStoneId;` - the re-roll path, i.e.
  the outcome is a roll that can be re-rolled, not a lookup.
- `:308` - `if (inputItemId == DungeonExclusiveItems.RoughStoneId) DungeonRunPayout.ResetRolls();`
  the per-stone roll counter resets on a fresh stone.
- The odds table itself is asserted by `DungeonGemExclusivityRegression`'s cases 4-6 (ALWAYS PAYS /
  ODDS ONLY / ONE TABLE) - *"the grade moves probabilities and NOTHING else"*.

**So the RND half of the ruling was already true and this lane changed nothing at the bench.**

⚠ **ONE CONSEQUENCE OF THE RAID DROP THAT THE BENCH FORCED, and it is not optional.**
`DungeonRunPayout` is a **FIFO of polish grades, one per un-polished stone**, paid oldest-first
precisely because stones are indistinguishable in the larder. A raid stone added with no matching
push would silently consume a dungeon run's grade at the bench and leave the last stone rolling on
the floor row. So the grant pushes one: `DungeonRunPayout.LastPolishScore = clamp(stars, 0,
DungeonRunGrade.MaxStars)` - the raid's settled star count, both scales being 0..3.

⛔ **THAT MAPPING IS NOT AN OWNER RULING.** WO-1373 §5.2 asks whether the drop should scale by stars
and she has not answered. Stars were used because the FIFO *must* be fed and a 0..3 star count is
the only 0..3 number a raid has. **Open item.**

---

## 5. (d) THE THREE KNOBS, ON ALL SIX SOURCES IN ONE CHANGE

Per `docs/PROD022_TUNABLE_FLAGS.md`'s own six-source list:

| Source | `raid.roughStoneMinTier` | `raid.roughStonePerDayCap` | `dungeon.roughStoneDropPct` |
|---|---|---|---|
| `RemoteTunables.cs` const + key + `TunableSpec` | 3 | 1 | 5 |
| `RemoteTunablesDefaultsRegression.ExpectedDefaults` (independent literals) | 3 | 1 | 5 |
| `docs/PROD022_TUNABLE_FLAGS.md` DEFAULT column | row 50 | row 51 | row 52 |
| `api/_lib/tunables.js` allowlist | present | present | present |
| `api/_lib/tunable-manifest.js` presentation card | present | present | present |
| `api/_lib/tunable-manifest.generated.json` | regenerated | regenerated | regenerated |

`ExpectedKnobCount` **49 -> 52**, and the reason string at `RemoteTunablesDefaultsRegression.cs:401`
was extended to name the new family (it enumerates categories and would otherwise have gone stale
while still passing).

**Node output, measured this session, not expected:**

```
$ node tools/gen-tunable-manifest.mjs
TUNABLE_MANIFEST_GEN_OK knobs=52 -> api/_lib/tunable-manifest.generated.json (rewritten)
$ node tools/gen-tunable-manifest.mjs --check
TUNABLE_MANIFEST_GEN_OK knobs=52 (checked, no drift)
$ node --test test/tunables-manifest.test.js
ℹ tests 23   ℹ pass 23   ℹ fail 0
```

That suite includes *"the manifest is 7-bit ASCII, because the served page must be"*, so the three
new cards are proven ASCII rather than assumed.

### ⚠ ONE DEPARTURE FROM "the default is today's behaviour", and one non-departure

- **`dungeon.roughStoneDropPct` IS a departure.** The build shipped **15**
  (`const float PostFirstRoughStoneDropRate = 0.15f`); she ruled 5. **A row of `15` restores the
  previous rate exactly**, and both the doc row and the `ExpectedDefaults` comment say so. This is
  the same shape as the two `vfx.*` fixes and `raid.lootRepeatClearPct` before it.
- **`raid.roughStoneMinTier` / `raid.roughStonePerDayCap` are NOT departures, because there is no
  prior behaviour to depart from.** No raid path granted the stone before this ticket - `grep
  rough_stone Assets/_Modules/**/*.cs` hit only `DungeonExclusiveItems.cs` and
  `JewelPolishService.cs`. `raid.roughStoneMinTier = 5` is the row that turns the new faucet back
  off.

### One knob NOT registered, named rather than smuggled in

`DungeonController.RoughStoneMinDungeonTier` (2) is a **`const`, not a row.** The lane brief
enumerated exactly three knobs; §6's standing rule says every number is a row. **Open item:
`dungeon.roughStoneMinTier` is the fourth knob this feature wants**, and adding it unasked would
have been a fourth unrequested registration.

---

## 6. (e) THE ORACLE RE-POINTS - AND THE BRIEF'S PREDICTION WAS WRONG

### ⛔ `DungeonGemExclusivityRegression` will NOT go red. Proven by reading it.

The brief said it *"will now red because raids drop stone"*. It will not.
`Assets/Editor/Regression/DungeonGemExclusivityRegression.cs:90-117` (`CheckCatalogExclusivity`)
scans **eleven JSON catalog files** - `packs.json`, `skr_store.json`, `skr_staking.json`,
`battle_monthly_packs.sample.json`, `cosmetics.json`, `stake-rewards.json`, `quests.json`,
`daily-quests.json` - for the id as *text*. `CheckVendorShelves` resolves vendor shelves. **Neither
reads any C# source, and neither knows what a raid is.** A grant from
`RaidVictoryController.GrantRoughStoneIfEarned` touches none of those files, so all eight of its
cases still pass unchanged. **No edit to it is required, and none was made.**

**What the ruling DID invalidate is the PROSE.** `DungeonExclusiveItems.cs`'s header and `<summary>` both said the ids *"may only ever enter
the player's inventory by descending"*. That sentence is now false. ⚠ **NOT DONE BY THIS LANE** -
`Core/Catalog/DungeonExclusiveItems.cs` was in the lane's file list but nothing in it needed a code
change, and rewriting a doc-comment that another oracle may lint is a judgement call for the lead.
**Proposed replacement, for the lead to land or route:** replace *"may only ever enter... by
descending"* with *"may only ever be EARNED - never sold by a vendor, never bundled in a purchasable
pack, never granted as a quest/stake payout. Earned now means a delve OR a top-tier raid clear
(WO-1373, owner ruling 2026-09-09); the purchase fence is unchanged."*

**And the stricter re-point the WO-1159 precedent asks for is landed as a NEW case, not as an edit
to that suite:** `RaidRoughStoneDropRegression`'s `[one-stone]` asserts
`DungeonExclusiveItems.Contains(RoughStoneId)` is **still true** - i.e. the raid may EARN the stone
and nothing may BUY it - and `[grant-site]` source-lints that the raid grant sits behind the tier
gate and the day cap. That is the invariant moving with the ruling instead of being deleted.

### ⛔ TWO SUITES *WILL* GO RED. Both are stale pins that move WITH the ruling. NEITHER WAS EDITED.

**RE-POINT 1 - `Assets/Editor/Regression/JewelerDiscoveryFtueRegression.cs:29`**

```csharp
if (!dungeon.Contains("PostFirstRoughStoneDropRate = 0.15f")) f.Add("post-first rate is not pinned to 15%");
```

That literal no longer exists: the const became a rail-backed property and the ruled rate is 5.
**Minimal re-point, for the seat that owns it** - it should pin the RULING, not a literal, so it
cannot go stale the next time she moves the row:

```csharp
if (DeNelle.Core.Ops.RemoteTunables.DungeonRoughStoneDropPctDefault != 5)
    f.Add("the dungeon rough-stone drop rate default is no longer the owner's ruled 5% (WO-1373, 2026-09-09)");
```

and the failure text updated from *"pinned to 15%"* to name the 5% ruling.

**RE-POINT 2 - `Assets/Editor/Regression/RaidRepeatClearRegression.cs:371`** (item 3, below)

```csharp
int iRead = handleCleared.IndexOf("RaidClaimService.IsClaimed(", StringComparison.Ordinal);
```

`Village2RaidController.HandleCleared` now calls `IsRepeatClearInCycle` (which itself calls
`IsClaimed` internally, `RaidClaimService.cs:153`), so that exact string is gone and case `iRead < 0`
fails. **Minimal re-point:**

```csharp
int iRead = handleCleared.IndexOf("RaidClaimService.IsRepeatClearInCycle(", StringComparison.Ordinal);
```

with the failure text updated to name the cycle predicate, and - because the new predicate reads the
COOLDOWN STAMP as well as the claim flag - the ordering check extended to also require
`iRead < handleCleared.IndexOf("BeginAfterClear(")`. **⚠ Note for whoever lands it:** the existing
`iClaim` search on `"ClaimBase("` IS live here (`Village2RaidController` calls `ClaimBase()` inside
`HandleCleared`), so that half of the case is real and should be kept, not dropped. This is the
exact same re-point WO-1461's addendum ADD-1 already proposed for the `RaidVictoryController` half
of the same case - **they are one edit and should land together.**

---

## 7. ITEM 2 - THE HONOR MILESTONE CACHE

**The measured cost:** `RaidScoring.PresentationStars` is read by the live HUD
(`RaidHudController.cs:322`, in its bind loop) **and again** by `TraceHonorStarSnuff`
(`RaidScoring.cs`, called from `Update` on every engaged frame). Its getter read all three
`Honor*` properties, and each of those walks `RemoteTunables.SpecFor` then `RemoteTunables.Int`
(dictionary probe, then a `PlayerPrefs` read on a miss) - **six tunable lookups per frame** on a
device the raid lane measured at 22 fps.

**What changed:**
- `ComputeHonorStars(engaged, elapsed, destruction, heroDied)` - the **PINNED four-argument
  signature**, called directly by `RaidWatchdogHonorRegression.cs:568` - is untouched and now
  delegates to a new seven-argument overload that takes the three milestones as parameters. One
  formula, two entry points, so they cannot diverge.
- Three instance fields (`_honorT3/_honorT2/_honorD2`) plus `_honorGeneration`, seeded by
  `RefreshHonorCacheIfStale()`.
- **The refresh signal is `RemoteTunables.Generation`** (`RemoteTunables.cs:1286`), which is the
  rail's own change announcement - bumped by `ApplyPayload` and by `Clear`. Read
  `RemoteTunablesService.cs` first as instructed: it exposes no event, only
  `ApplyCachedPayload` / `PollForeverAsync` / `RefreshOnceAsync`, so `Generation` is the signal.
- **Seeded at `NotifyEngagement`**, the one authority that starts the clock (WO-1520), so the first
  frame of the fight is already a cache hit.
- `TraceHonorStarSnuff`'s message now reads the cached triple too - so the numbers it prints are
  provably the ones the tier was computed from.

**The pin:** `RaidRoughStoneDropRegression` case `[honor-cache]` -
1. behavioural: the two overloads agree across 21 x 11 x 2 = 462 (elapsed, destruction, heroDied)
   combinations;
2. source-lint (comments AND strings stripped): `PresentationStars`' getter names **none** of
   `HonorThirdStarSeconds` / `HonorSecondStarSeconds` / `HonorSecondStarMinDestruction`, and it
   **does** call `RefreshHonorCacheIfStale`;
3. `RefreshHonorCacheIfStale` reads `RemoteTunables.Generation` (not a timer, not a frame count);
4. `NotifyEngagement` seeds it.

**"One read per engagement, not per frame"** is therefore pinned as *one read per rail payload*,
which is the stronger and the honest statement - a knob pushed mid-raid must still land, and it does.

---

## 8. ITEM 3 - `Village2RaidController.HandleCleared`

`Assets/_Modules/Village/World/Camps/Village2RaidController.cs`, one substitution:

```csharp
bool repeatClear = RaidClaimService.IsRepeatClearInCycle(ConfigId);   // was IsClaimed(ConfigId)
```

**The ordering shape `RaidVictoryController` now uses was verified as ALREADY CORRECT here** and was
not moved: the read precedes `RaidCooldownService.BeginAfterClear(ConfigId)` (which stamps the
window the new predicate reads) AND `ClaimBase()` (which flips the claim flag). Query after either
and every clear, including the first, reports as a repeat.

Two comments were corrected in the same edit, because this file's own header asserted the read and
`RaidRepeatClearRegression`'s failure text is literally about *"a comment asserting a read that does
not exist"*: the class header at `:51-53`, and the `REPEAT CLEAR` warn message, which said *"it was
already claimed"* (permanent) and now says *"INSIDE ITS CURRENT CYCLE"* and states the reset.

**What `repeatClear` feeds here, read at `:223-260` before calling it done:** exactly one thing - the
`FlowTrace.Warn`. **Village2 pays no resource loot at all** (it has no `RaidScoring`), so no payout
changes; the one-time payoff is the companion, gated separately on `newClaim` from `ClaimBase()`.
So this fix makes the raid-VILLAGE path *narrate* the same ruling the raid-CAMP path *pays*.

⚠ **Consequence: `RaidRepeatClearRegression.cs:371` goes red.** See RE-POINT 2, §6.

---

## 9. FILES CHANGED

| File | What |
|---|---|
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | 3 defaults, 3 keys, 3 `TunableSpec` registry entries |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | 3 `ExpectedDefaults` literals; `ExpectedKnobCount` 49 -> 52; reason string extended |
| `Assets/_Modules/Village/Troops/RaidScoring.cs` | honor-milestone cache + 7-arg `ComputeHonorStars`; `RoughStoneItemId`, `RoughStoneMinTier`, `RoughStonePerDayCap`, `TierOf`, `ShouldDropRoughStone`, and the UTC-day ledger (`RoughStonesGrantedToday` / `MarkRoughStoneGranted` / `ClearRoughStoneDayLedger`) |
| `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` | STEP 3.5c call + `GrantRoughStoneIfEarned` (gates only - routes to the shared authority, banks nothing itself) |
| `Assets/_Modules/Core/Catalog/DungeonRunPayout.cs` | **§2b correction:** `RoughStoneGrantAuthority` delegate + `GrantRoughStone(score, via)` - the cross-assembly seam. Forwards or fails; never banks |
| `Assets/_Modules/Village/World/Camps/Village2RaidController.cs` | `IsClaimed` -> `IsRepeatClearInCycle`; two comment corrections |
| `Assets/_Modules/Dungeons/DungeonController.cs` | starter-tier gate in `GrantRunPayout`; `PostFirstRoughStoneDropRate` const -> rail-backed property + `PostFirstRoughStoneDropPct`; `RoughStoneMinDungeonTier`; `ResolveDungeonTier`. **§2b correction:** bank+grade+announce extracted to `BankRoughStone` (the ONE `LastPolishScore` writer) + `InstallRoughStoneGrantAuthority` at boot |
| `api/_lib/tunables.js` | 3 allowlist entries |
| `api/_lib/tunable-manifest.js` | 3 hand-authored presentation cards |
| `api/_lib/tunable-manifest.generated.json` | regenerated by `node tools/gen-tunable-manifest.mjs` (never hand-edited) |
| `docs/PROD022_TUNABLE_FLAGS.md` | rows 50, 51, 52 |
| `Assets/Editor/Regression/RaidRoughStoneDropRegression.cs` | **NEW** |
| `WorkOrders/WORK_ORDER_1373_*.md` | Status flipped + a scope banner |

**Not touched, as instructed:** `RaidClaimService.cs`, `RaidSelection*.cs`, `RaidDeployController.cs`,
`RaidHudController.cs`, `DataRegression.cs`, any scene, any View.
**Also not touched:** `Core/Catalog/DungeonExclusiveItems.cs` - in the lane's file list, no code
change needed (see §4 and §6). `Core/Catalog/DungeonRunPayout.cs` WAS edited by the §2b correction;
it is in the lane's file list.
⛔ **`ComposedDungeonRunRegression.cs` and `RoughStoneFanfareRegression.cs` were NOT edited.** The
[exit-pays] red was a real architecture violation, not a stale pin - the code moved to satisfy the
oracle, never the reverse.
**No canonical JSON twin was edited**, so there is no newline count to prove.

---

## 10. REGISTRATION LINE FOR `DataRegression.RunAll`

This lane did not edit `DataRegression.cs`. One line, beside the other raid suites:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-rough-stone suite", () => { if (!DeNelle.Editor.Regression.RaidRoughStoneDropRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-rough-stone] " + r); });
```

Markers: `RAID_ROUGH_STONE_OK` / `RAID_ROUGH_STONE_FAIL`. Standalone entry:
`run-unity-method DeNelle.Editor.Regression.RaidRoughStoneDropRegression.RunStandalone`.

⚠ **The suite count in the `REGRESSION_OK <n>/<n>` marker moves by one when this is registered.**

---

## 11. RED-FIRST PROOF

No Unity run is available to this lane, so the RED is stated as **what fails against the pre-change
tree**, and stated as exactly that rather than dressed up as a captured run.

1. **`[defaults]` fails on the pre-change tree.** `DungeonController.PostFirstRoughStoneDropRate`
   was `0.15f` (`DungeonController.cs:628` before this change), so
   `PostFirstRoughStoneDropPct != 5`. The failure text detects that specific value and says *"that
   is the PRE-RULING rate this build shipped as a compiled const float 0.15f, so the ruling has
   been reverted rather than retuned"* - the mutation is not just detected, it is named.
2. **THE SUITE DOES NOT COMPILE against the pre-change tree, and that is the honest statement for
   five of its cases** - `[rule-table]`, `[tier-ladder]`, `[day-ledger]`, `[grant-site]` and the
   behavioural half of `[honor-cache]`. Every one of `RaidScoring.TierOf`,
   `.ShouldDropRoughStone`, `.RoughStoneMinTier`, `.RoughStonePerDayCap`,
   `.RoughStonesGrantedToday`, `.MarkRoughStoneGranted`, `.ClearRoughStoneDayLedger`,
   `.RoughStoneItemId`, the seven-argument `ComputeHonorStars` overload, and
   `RaidVictoryController.GrantRoughStoneIfEarned` did not exist there.
   ⚠ **Stated as a compile failure rather than dressed up as a run**, and specifically NOT claimed
   as "the lint half would have failed": it would never have executed.
3. **`[starter-gate]` fails on the pre-change tree** - `DungeonController.RoughStoneMinDungeonTier`
   and `ResolveDungeonTier` did not exist, and `GrantRunPayout` named neither. (Same compile
   caveat as 2: the behavioural `RoughStoneMinDungeonTier` read is what does not resolve.)
4. **`[defaults]` also fails on a deliberate live mutation that costs nothing to try:** setting the
   `ff.tun.dungeon.roughStoneDropPct` local override to 15 reproduces the pre-ruling rate exactly,
   which is what the doc row promises. The suite clears and restores that override for the run, so
   it measures the SHIPPING default and not a developer's console state.
5. **`[one-stone]` fails the moment anyone deletes `ing_rough_stone` from `DungeonExclusiveItems`**
   - the mutation that "makes the raid drop work" by dropping the purchase fence with it.

---

## 12. GATE CHECKS RUN IN THIS LANE

Brace balance, NUL scan, and a per-line non-ASCII classification, measured this session:

```
file                                  braces      NUL   non-ASCII outside comments
RemoteTunables.cs                     44/44       0     0
RemoteTunablesDefaultsRegression.cs   86/86       0     0
RaidScoring.cs                        150/150     0     2   <- PRE-EXISTING, see below
RaidVictoryController.cs              113/113     0     16  <- PRE-EXISTING
Village2RaidController.cs             46/46       0     16  <- PRE-EXISTING
DungeonController.cs                  340/340     0     56  <- PRE-EXISTING
RaidRoughStoneDropRegression.cs       77/77       0     0
```

**Every one of the 90 non-ASCII hits is a pre-existing em-dash**, in `Debug.Log` / `FlowTrace`
strings and `[Tooltip]` attributes that predate this ticket. **HOW THAT WAS PROVEN, and it is not
an eyeball:** the identical classifier was run over `git show HEAD:<path>` for each file and the
counts are byte-for-byte equal to the working tree -

```
                                      HEAD   tree
RaidScoring.cs                          2      2
RaidVictoryController.cs               16     16
Village2RaidController.cs              16     16
DungeonController.cs                   56     56
RemoteTunables.cs                       0      0
```

so this lane added exactly ZERO non-ASCII characters outside comments. **Deliberately NOT touched:** they are live log
strings other suites could pin, and rewriting them would be unrelated churn in files this lane
entered for one reason. Flagged so the next seat can fix them on purpose.

⚠ **`RaidRoughStoneDropRegression.cs` declares `OpenBrace`/`CloseBrace` `const char`s** instead of
writing `'{'` three times and `'}'` once in its brace-matching helper. Without that the file counts
77/76 and fails CLAUDE.md §1's gate on a file that is not actually broken - the note is in the code,
so nobody learns to ignore that check.

`node tools/gen-tunable-manifest.mjs --check` -> `TUNABLE_MANIFEST_GEN_OK knobs=52 (checked, no drift)`
`node --test test/tunables-manifest.test.js` -> `23 pass, 0 fail`

**NOT PROVEN FROM HERE, and not claimed:** compilation (no Unity in this lane),
`COMPILE_GATE_OK`, `REGRESSION_OK <n>/<n>` on a fresh log, and any runtime capture of the drop. The
lead's gate. In particular the acceptance row *"proven by capture - quote the `[Flow:*]` lines from
raid grant -> inventory -> polish -> accessory granted"* is **NOT delivered**; the trace lines exist
(`ROUGH STONE GRANTED` / `ROUGH STONE withheld` / `ROUGH STONE day-ledger`) but nothing has run them.

---

## 13. UNPROVEN, AND OPEN - the list, so nobody has to derive it

1. ⛔ **Does the starter gate cover the GUARANTEED FIRST STONE, or only the 5% roll?** Shipped
   covering both; the consequence is that the Jeweler's reveal moves off `dg_starter_loop`. §3.
2. ⛔ **Is "the top two TIERS" the camp ladder (3+4) or the two top DIFFICULTIES (Hard+Extreme,
   which adds `fortified_garrison`)?** Shipped as the ladder; one row flips it. §2.
3. ⛔ **Is the per-day cap GLOBAL or PER CAMP?** Shipped global; `perDayCap = 2` approximates
   per-camp. §2.
4. ⛔ **Should the raid drop scale by STARS (WO-1373 §5.2, unanswered)?** No star gate shipped, but
   the star count IS pushed as the polish grade because the FIFO must be fed. §4.
5. ⚠ **`dungeon.roughStoneMinTier` is not a row** - it is `const int RoughStoneMinDungeonTier = 2`.
   §5.
6. ⚠ **`DungeonRoomBinder.LoadTier` is a private duplicate** of `DungeonController.ResolveDungeonTier`
   - same path, same field, two implementations. Out of lane; flagged for consolidation.
7. ✅ **CLOSED by the §2b correction** (was: *"a raid-granted stone has NO FANFARE"*). Routing the
   raid through `DungeonController.BankRoughStone` means it raises the same
   `DungeonController.RoughStoneGranted` announcement a dungeon stone does - one producer, one
   announcement, no second spawner and no listener that grants anything.
   ⚠ **Not proven end to end:** `RoughStoneFanfarePanel.Show` is wired by
   `DungeonExitInteractable` (which subscribes/unsubscribes AROUND its `GrantRunPayout` call), so
   whether a beat actually appears on a **raid victory screen** depends on a subscriber existing in
   that scene - and nothing in a `RaidBase_*` scene subscribes today. The event now fires; the
   surface is still a View question, and Views were outside this lane.
8. ⚠ **`DungeonExclusiveItems.cs`'s "only by descending" prose is now false** and was not rewritten
   here. Proposed replacement text is in §6.
9. **§7's "state the measured ratio - raid payout vs collector yield over the same wall-clock"** is
   not answered by this lane and needs a runtime measurement, not a code read.
10. **§2b's three rings are not built**, and her "can you wear more than one" question is open.
11. ⚠ **THE DAY LEDGER TRUSTS THE DEVICE CLOCK.** `RoughStonesGrantedToday()` compares against
    `UtcDay.Key()`, which is `DateTime.UtcNow` - so a player who rolls the device clock forward
    re-arms the one-stone-a-day cap. That is the SAME exposure `RaidClaimService.CrystalsPaidToday`
    already carries (identical mechanism, same helper), and matching the existing precedent was
    deliberate rather than accidental. But note the contrast, because it is right next door:
    `RaidCooldownService.BeginAfterClear` stamps from a **server-anchored** clock, and
    `RaidVictoryController` STEP 2.5's own comment says *"never DateTime.UtcNow here"*. So a clock
    roll re-arms the stone cap and the crystal stamp while NOT re-arming the cooldown. Closing it
    means moving both day-stamps onto the anchored clock - one change, two callers, and outside
    this lane's files (`RaidClaimService.cs`).
