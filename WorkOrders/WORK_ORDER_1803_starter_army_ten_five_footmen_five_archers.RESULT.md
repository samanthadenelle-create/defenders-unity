# WORK ORDER 1803 — RESULT

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Branch:** `dev` (working tree; this lane did NOT gate, commit, or touch `CLI_LANES_WO_NUMBERS.md` — per brief)

---

## 1. What landed

| File | What |
|---|---|
| `Assets/_Modules/Village/Troops/StarterArmyGrant.cs` | `ArcherTroopId`; `DefaultStarterCount` 3 -> **10**; `SplitComposition(total, out f, out a)` (even, remainder to Footmen); `ResolveComposition`; two-arg `GrantToastFor(footmen, archers)`; `GrantRun` per-id grant helper; housing read + `FlowTrace.Fail`; `ResolveCount` clamped to `ArmyStorage.DefaultMaxArmySize` instead of a literal 10; composition in the grant trace |
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | `RaidStarterArmySizeDefault` 3 -> **10**; key doc + `TunableSpec` prose rewritten |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | `ExpectedDefaults` row `raid.starterArmySize` 3 -> **10** |
| `Assets/Editor/Regression/StarterArmyGrantRegression.cs` | new cases **A2** (per-id composition), **A2b** (defs real/1-slot/tier-1), **A4** (fits the housing const + source lint), **D1** (split rule, 7 values), **E1/E2** (readiness + Camp I door word); **C2** re-pointed to the two-arg toast over 5 compositions |
| `docs/PROD022_TUNABLE_FLAGS.md` | row **32** rewritten |
| `api/_lib/tunables.js` | key comment rewritten (row is the **total**; client derives the split) |
| `api/_lib/tunable-manifest.js` | `label`/`what`/`risk` rewritten in the Command Center voice |
| `api/_lib/tunable-manifest.generated.json` | regenerated |
| `Assets/Editor/Regression/RaidLootCurrencyRegression.cs` | **A SEVENTH REGISTRATION SITE THE BRIEF DID NOT NAME.** `MapStarterArmySize` 3 -> **10** (`:74`), consumed at `:186` by `AssertDefault(RemoteTunables.KeyRaidStarterArmySize, MapStarterArmySize)`. **This suite would have gone RED at the first gate** with the default at 10 and this literal at 3 — a lane that had only touched the brief's six places would have shipped a red gate and spent the diagnosis on the wrong file. Found by grepping the CONST/KEY identifiers (`RaidStarterArmySizeDefault`, `KeyRaidStarterArmySize`), not the lowercase key string — the key-string grep cannot see a suite that pins the default through the C# const. |

**No suite was added**, so **no `DataRegression.cs` registration line is needed** —
`StarterArmyGrantRegression` was already registered at `Assets/Editor/Regression/DataRegression.cs:1586`
and `DataRegression.cs` was not touched.

## 2. Proofs — read/measured THIS SESSION

**The two troop ids, at source** (`Assets/Resources/Data/Canonical/troops.json`, 2026-09-16):
`troop-footman` at `:6` and `troop-archer` at `:29`, both `"slots": 1`, both
`"unlockBarracksTier": 1`. So both are day-one fieldable and each costs one housing slot.

**The housing cap, at source:** `ArmyStorage.DefaultMaxArmySize = 10`
(`Assets/_Modules/Core/State/ArmyStorage.cs:43`). `MaxArmySize` = that const + `armyCapBonus` from the
active perk contract, so a fresh save is exactly 10. **10 bodies x 1 slot = 10 slots = the cap
exactly. No housing change was required and none was made.**

**No truncation is possible:** `ArmyStorage.GrantTrained` (`:231`) appends unconditionally — no
capacity or affordability check — by design (CoC parity). So the grant cannot clip; the added
`FlowTrace.Fail` covers the *over-cap* case instead, naming the cap.

**Camp I garrison = 9,** computed from the authored data (not copied from a doc):
`python` sum over `garrison.composition` in `Assets/Resources/Data/Canonical/scene-configs.json` gave
`raider_camp_small` **9**, `fortified_garrison` 15, `mage_enclave` 19, `iron_bastion` 19.
`RaidSelectionVM.ArmyWarnWord` (`:545-548`) returns the word only when `garrison > deployableTroops`,
so **9 > 10 is false -> no word at ten**, and **9 > 3 is true -> the word was there at three.** Both
halves are pinned in case E2.

**No player-side deploy-tray ceiling exists.** `RaidDeployVM.Rebuild` (`:657-724`) iterates
`_army.GetDeployable()`, groups by `TroopDefId` into one `ItemVM` per type with the count as its price,
and sets `Fielded => Readiness.DeployableSlots`. Grepped `MaxRows|DeployCap|maxDeploy|Fielded` over
`RaidDeployVM.cs`: the only roster read is that one `foreach`. `liveCombatantCap = 36`
(`Village/World/Camps/RaidGarrisonSpawner.cs:61`) is the **defender** budget.
`ArmyReadiness.Compute(army, deployableSlots, 0)` (`:123`) defaults `everCompletedRaid: true`, so
`required = cap = 10` and `10 >= 10` -> `Ready`. **Pinned by the new E1, but NOT yet executed —
see §4.**

**No re-grant for existing saves:** `TryGrant` still latches on `state.MarkEverAcquired("grant.starter-army")`,
which returns true only on the first add and is both the check and the latch. Cases B1/B2 (unchanged)
pin that a second call and a save already carrying the key both grant **0**. No schema bump, no
migrator, no new field. A 3-troop save is **not** topped up — deliberately (see WO §2c).

**Manifest regeneration:** `node tools/gen-tunable-manifest.mjs` ->
**`TUNABLE_MANIFEST_GEN_OK knobs=71 -> api/_lib/tunable-manifest.generated.json (rewritten)`**.

**Node tests, after the edits:** `node --test test/*.test.js` -> **`tests 761 / pass 760 / fail 0 /
todo 1`**. The one `✖` printed is the `todo` (`heartbound-contract.test.js:272`, *"WO-1693 finding 2 —
recordVerifiedStake names no streak column"*), which node lists but does not count as a failure; it is
pre-existing and unrelated. `node --test test/tunables-manifest.test.js` alone -> **27/27 pass**.

**Brace + NUL, per `.cs`:** `python tools/gate_brace.py` over the four touched `.cs` files ->
**`GATE_BRACE_SUMMARY bad=0 of 4`, exit 0**. Raw byte scan: NUL = **0** in all eight touched files;
raw `{`/`}` balanced in every one (17/17, 37/37, 44/44, 86/86, 138/138, 114/114, 72/72, 2/2).

**Canonical JSON:** **none touched.** No file under `Assets/Resources/Data/Canonical/` or
`Assets/StreamingAssets/Data/Canonical/` was modified by this lane (troops.json and scene-configs.json
were **read only**), so the dual-copy byte-identity rule has nothing to verify here.

**`docs/PROD022_TUNABLE_FLAGS.md` line-ending integrity:** patched in binary mode on the exact row
(index 80, asserted to start `| 32 | \`raid.starterArmySize\``); LF count **444 -> 444** and CRLF
**444 -> 444**, so the file is byte-stable apart from that one row.

## 3. ⚠ CROSS-LANE FINDING THE LEAD MUST SEE BEFORE COMMITTING

`api/_lib/tunable-manifest.generated.json` is **derived from the whole registry**, and the registry in
the working tree already carried **seven uncommitted knobs from another lane** (the town/wave
difficulty tunables: `town.regenSuppressSecondsAfterHit`, `town.regenPctDuringWave`,
`wave.hpGrowthPctPerWave`, `wave.dmgGrowthPctPerWave`, `wave.maxCountPct`, `wave.countCapPct`,
`wave.maxSimultaneousPct`) whose author had edited `tunables.js` and `tunable-manifest.js` but had
**not regenerated the manifest**. My regeneration is therefore correct and necessary, but its diff is
**+36/-1**: one line is mine (`raid.starterArmySize` 3 -> 10) and **35 belong to that lane.**

`api/_lib/tunables.js` and `api/_lib/tunable-manifest.js` are likewise **co-owned right now** — their
diffs contain that lane's new blocks as well as my `starterArmySize` edits. My edits are additive and
confined to the `raid.starterArmySize` block/comment in each; nothing of theirs was altered.

**Recommendation:** commit the three `api/_lib/tunable-*` files **once**, with both lanes, or land the
wave/town lane first and re-run `node tools/gen-tunable-manifest.mjs`. A path-scoped commit of the
generated file alone would carry that lane's rows under this ticket's message.

`api/client-tunables.js` and `test/tunables-manifest.test.js` are also dirty in the tree and are
**NOT mine** — grepped both for `starterArmySize`: **no hits**, so nothing of this ticket belongs in
them and I left them untouched.

## 3b. Consumer sweep + canon sweep (§15)

**Every consumer of the const/key, proven by grepping the IDENTIFIERS over all `*.cs`**
(`RaidStarterArmySizeDefault|KeyRaidStarterArmySize|StarterArmySize`) — 8 hits, all accounted for:
`RemoteTunables.cs:670/:674/:1642` (mine), `StarterArmyGrant.cs:122/:333` (mine),
`RaidLootCurrencyRegression.cs:74/:186` (**the seventh site, fixed**), and
`RaidDoorBeatTokens.cs:224` — a **comment only**, which deliberately explains why the raid-door prompt
reads the *published* army projection and **not** this knob. No number there, so no change needed.
⚠ That comment's closing quote is now slightly stale (it quotes the old `GrantToastFor` header's
phrase *"rather than a hardcoded 3"*). `RaidDoorBeatTokens.cs` belongs to WO-1802's lane, so it was
**not** touched — flagged for that lane, one word, no behaviour.

**Load-bearing canon is clean.** Grepped `free Footmen|starter squad|starter army|three Footmen|
3 Footmen|starterArmySize` (case-insensitive) over `KEY_FACTS.md`, `SESSION_CANON_LOADER.md`,
`PIPELINE_STATE.md`, `CANON_GROUND_TRUTH_*.md` and `docs/HANDOVER.md`: **no matches.** So no §15
load-bearing doc carries the retired "3 free Footmen" and none needed a fix or a `STALE:` banner.
The dated docs that do carry it (`docs/PROGRAM_RAID_ECONOMY_2026-09-04.md`, WO-1374 and its RESULT)
are **frozen point-in-time ledgers** and were correctly left alone.

## 4. Unproven / not done — stated plainly (§11B)

- ⛔ **The Unity gate was NOT run** (per brief): no `COMPILE_GATE_OK`, no `REGRESSION_OK`, no
  `STARTER_ARMY_OK` on a fresh log. **Every claim about the new regression cases is a claim about code
  I wrote and read, not a measured pass.** In particular A2/A2b/A4/D1/E1/E2 have never executed. The
  brace gate and the raw byte scan passed; that is not compilation.
- **The composition, the housing fit and the Camp I word are proven ARITHMETICALLY from values read at
  source this session** (slots 1+1, cap 10, garrison 9) — not from a running build. The first gate run
  is what turns them into measurements.
- **Not felt-verified:** whether ten troops makes the first raid *feel* winnable is the owner's call
  and the reason the number is on the remote rail.
- **`{army.used}` / `{army.cap}` reading "10 of 10"** in the `ctx_post_raid` beat is derived from
  reading `tutorial-steps.json` and `PostRaidBeatTokens`' described inputs; I did not execute that
  token resolution. The tutorial files are another lane's, so it is flagged, not changed.
- **The pre-edit `node --test` baseline was not captured.** The two `api/_lib` files were already dirty
  from the other lane when I arrived, so a `git checkout HEAD` baseline would have measured the wrong
  tree and destroyed their work. What I can prove: no test in `test/` references `starterArmySize`
  (grepped), and `tunables-manifest.test.js` passes 27/27 after the change.
- **Line endings:** `git diff` prints its usual *"LF will be replaced by CRLF"* notice for the two
  `.cs` files I edited. The HEAD blobs for those paths are **also LF-only** (checked via `git show`),
  and the diffs are targeted (+202/-... and +258/-..., not whole-file rewrites), so no line-ending
  churn will land. I did **not** establish what the on-disk endings were before my first edit.
- **Known citation drift I created, in two files I am not allowed to touch.** Inserting the WO-1803
  header block pushed `StarterArmyGrant.cs`'s section anchors down ~22 lines, so two existing
  cross-references now point at the wrong prose:
  `Assets/_Modules/Village/Tutorial/V2/TutorialSignalAdapters.cs:274` cites `StarterArmyGrant.cs:25-37`
  (the "WHY A POLLING BRIDGE" block) — **now `:47-59`**; and
  `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs:2749` cites `:40-52` (the "IDEMPOTENT ACROSS
  RELAUNCHES" block) — **now `:62-74`**. `TutorialFlow.cs` is off-limits per brief and
  `TutorialSignalAdapters.cs` is dirty from another lane, so neither was edited. Two one-line fixes
  for whoever owns those files.
- **Non-ASCII:** I added exactly one comment line containing `U+2500` box-drawing
  (`// -- WO-1803 THE HOUSING CHECK ... --` in `StarterArmyGrant.cs:248`), matching that file's own
  pre-existing section-rule style at `:203`. It is a comment, never a player string. All player-facing
  strings added (the toast) are 7-bit ASCII and case C2 enforces that.

## 5. Board

This WO's own `**Status:**` line is **`IMPLEMENTED, NOT YET GATED`** in both files. `BOARD.html` is
**not** regenerated by this lane (the lead regenerates it and commits the flip with the work).

---

**WO:** `WorkOrders/WORK_ORDER_1803_starter_army_ten_five_footmen_five_archers.md`
**RESULT:** `WorkOrders/WORK_ORDER_1803_starter_army_ten_five_footmen_five_archers.RESULT.md`
