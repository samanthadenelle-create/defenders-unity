# WO-1540: the blank-start census sees a baked CastleBarracks because ff.barracks is ON in batchmode

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Editor/regression environment - `BlankStartCensusRegression` + `FeatureFlags` + possibly
`HubStructureVisualInjector`.
**Source:** wave-two regression `Builds/reg-wave2.log` (422/435), 2026-09-06. Surfaced by
`BlankStartCensusRegression`, **registered tonight by WO-1496**. Minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1540 -> 1541 in the same edit).

## 1. EVIDENCE

```
BLANK START: 1 failure(s):
  EXTRA structure: baked 'CastleBarracks' visible - ff.barracks is ON in this environment
  (default OFF; spawner: scene bake, hidden by HubStructureVisualInjector.TrySwap)
```

The flag's default is OFF. It is ON inside the batchmode run, which means a PlayerPrefs value is bleeding
between suites - one suite sets `ff.barracks` and never restores it, and every later suite runs in an
environment nobody authored.

That is worse than the one failure it produced: any suite after the setter is testing a configuration that
does not match a player's, and the ORDER suites run in silently changes results. This one surfaced because a
census counts everything; the others would just quietly pass or fail wrong.

## 2. FIX SHAPE

Decide from the flag's authority in `FeatureFlags.cs`, then take ONE of:

- **The suite pins flags to their defaults before the census** (and restores after), which fixes this suite;
  or
- **`HubStructureVisualInjector` honours the flag at bake**, which fixes the bake path.

Then, regardless of which: **make flag bleed impossible, not just handled here.** A shared
set-up/tear-down that snapshots and restores PlayerPrefs flags around every suite is the durable fix; one
suite pinning its own flags leaves the next one exposed.

## 3. WHAT NOT TO DO
- Do not set `ff.barracks` OFF at the top of this one suite and call it done. The bleed is the defect; this
  census is only the detector.
- Do not change the flag's default to match the batchmode environment.

## 4. ACCEPTANCE
- [ ] The RESULT names WHICH suite leaves `ff.barracks` ON, with the file:line that sets it.
- [ ] Flags snapshot/restore around suites, so results do not depend on run order.
- [ ] `BlankStartCensusRegression` reports zero failures, run both alone AND after the setter suite.
- [ ] `REGRESSION_OK n/n` on a fresh log.

---

## 4B. THE PREMISE IN SECTION 1 IS FALSE - CORRECTED 2026-09-07 (implementation lane)

**NO SUITE LEAKS `ff.barracks`. There is no bleed of this flag, and there never was.**
Measured at source today, not inferred:

- `Assets/_Modules/Core/FeatureFlags.cs:1110` reads `Get("barracks", defaultOn: **true**)`.
  WO-771 (2026-07-26) flipped it ON deliberately - the comment at `:1107-1109` says so, because
  the raid deploy loop pulls troops from the barracks-gated roster.
- `Get` (`:1373-1379`) returns `defaultOn` whenever the PlayerPrefs key is **absent**.
- Grep over `Assets/Editor` for `ff.barracks` returns **zero writers** (2026-09-07). The only
  `ff.*` setters in the Regression folder are `ff.enemystructureaware` (DataRegression.cs:2229),
  `ff.regionroam` / `ff.raidwalk` (OverworldCombatGateRegression.cs:62-92) and `ff.mergedworld`
  (SceneRoutingRegression.cs:102-129) - and all three already restore in a `finally`.

So the census failed on a **pristine PlayerPrefs**, in **every** environment and **every** run
order. Acceptance item 1 ("name WHICH suite") has the answer **none**; acceptance item 3 ("run the
census after the setter suite") is unrunnable because no setter exists. The defect is the oracle,
not the environment - see section 4C.

## 4C. WHAT WAS ACTUALLY WRONG, AND WHAT LANDED

`BlankStartCensusRegression.cs` section 3 was wrong on both axes:
1. **Stale premise** - it asserted "default OFF", six weeks after WO-771 flipped it.
2. **Wrong question** - it asked the FLAG whether the twin stands. `FindInScene` uses
   `FindObjectsInactive.Include` (`:364-370`), so the bake host is found whether or not it stands.
   The same run's log proves the twin was suppressed correctly: `Builds/reg-wave3h.log:9366-9367`
   - `blank-town 'barracks': migrated=True everBuilt=False maySurface=False twins=[CastleBarracks]
   -> Suppressed` - while `:13854` called it an EXTRA structure.

Landed (neither forbidden move in section 3: the flag is not pinned OFF and the default is unchanged):
- Section 3 now asks the WO-834 authority, `StructureSingleton.MayBakedTwinSurface("barracks", ...)`,
  against the census's own fixture state. `barracks` is deliberately NOT a `BakedRows` entry
  (`StrategicPlacementMigration.cs:314-336`), so section 2 does not cover it - this stays its own pin.
- **Flag hygiene, fence-level**: `FeatureFlagSnapshot` captures every `ff.*` key before the START
  fence and restores + DIFFs after the END fence, naming any key that drifted. Key set is REGEXED
  out of `FeatureFlags.cs` (never a hardcoded list).
- **Honest limit**: this DETECTS and RESTORES drift; it does not isolate suite N from N-1.
  `RunAll` between the fences is ~200 flat `if (!X.Run(out var r))` lines, not a loop - per-suite
  wrapping means editing every registration line and needs the lead's ruling.
- **Pin**: `FeatureFlagSnapshotRegression` - a dummy suite sets `ff.petcombat` and does not restore
  it; the oracle asserts the drift is NAMED and that the next reader sees the compiled default.

## 5. RELATED FINDING, 2026-09-07 (WO-1571 lane) - `arcane-tower` IS THE SAME CLASS

A VISIBLE BAKED STRUCTURE THAT THE PLACED-STRUCTURE LEDGER DOES NOT OWN. Recorded here, not
in a new ticket, because it is this ticket's class - only the cause differs (this one is not a
feature-flag bleed; it is a baked twin standing in for an unplaced singleton).

**Measured at source, 2026-09-07:**
- `Assets/Resources/Data/Canonical/structures-catalog.json:1008-1010` - row `arcane-tower`
  ("Cathedral of Magic", CRAFT, `repo.singleton: true`) authors
  `"bakedTwins": [ "ArcaneTower_MagicUpgrades" ]`.
- That baked GameObject is placed by `Assets/Editor/CastleHubBuilder.cs:312`
  (`(towerBig, "ArcaneTower_MagicUpgrades", new Vector3(32, 0, 0))`) and is mapped back to the
  row by `Assets/_Modules/Village/BuildMode/StrategicPlacementMigration.cs:97`
  (`new BakedRow { bakedName = "ArcaneTower_MagicUpgrades", itemId = "arcane-tower" }`).
- So the player SEES a Cathedral of Magic standing in the hub while the ledger holds no
  `arcane-tower` row at all. The owner's own reading of the same screen: the research picker
  reports `"capacity derived from 4 live school(s)"` and the Cathedral is not among them.

**Device evidence, build 358872, logcat 2026-09-07 00:58:40** (owner: *"clicking BUILD on it takes
me back to build collection"*), from Manage > BUILD > Cathedral of Magic, whose card reads
NOT BUILT while the twin stands in the world:

```
[Flow:Navigation] opened workspace 'Build Collections' at root
[Flow:Build] BuildMode.Enter - palette shown
```

**Why the two halves are one bug family.** `BuildModeController.IsSingletonBuilt` asks
`StructureSingleton.IsPlayerBuilt` (BuildModeController.cs:2277), so the ARM path correctly reads
the row as buildable - but `BuildCollectionBrowser.cs:406` computes its card's `built` flag from
`StructureSingleton.IsBuilt`, which COUNTS AN ACTIVE BAKED TWIN. One screen therefore says NOT
BUILT and another would say BUILT, off the same state, exactly as `ff.barracks` makes the census
and the player disagree. Whatever fix lands here should state which of `IsBuilt` / `IsPlayerBuilt`
each surface is entitled to ask, rather than leaving each caller to choose.

**Not fixed in the WO-1571 lane** - that lane only re-pointed the Manage BUILD door so the row can
be placed at all. No twin/ledger behaviour was touched.

**AND IT IS WHY THE COLLECTIONS ROOT WAS EMPTY, which was NOT understood when WO-1571 was written.**
Measured 2026-09-07: `Assets/Resources/Data/Canonical/card-collections.json` authors SEVEN
collections - Gathering / Realm / Towers / Crafting / Storage / Walls & Gates / Trade - and
`arcane-tower` sits in **Realm**. The owner's root frame shows only THREE. The filter is
`BuildCollectionBrowser.CollectionHasVisibleItems` (`:613-628`), which drops a collection when
every item is a singleton reading `StructureSingleton.IsBuilt(entry)` - **and that is the twin-
counting predicate**. Realm (barracks / pet-house / arcane-tower), Crafting, Trade and Gathering
are all singleton-heavy rows with authored `bakedTwins`, so the BAKE hid four whole categories
from the build browser. The root the owner called a dead end was not authored short; it was
emptied by the same `IsBuilt`-vs-`IsPlayerBuilt` split described above.

So this ticket's fix has a THIRD consumer beyond the census and the card: the category root itself.
