# WO-1097 RESULT - apex boss addressed as a Component type: IMPLEMENTED on HEAD (with a residual)

**Verified:** 2026-09-09 (SILO 0B verify-and-flip pass, read-only; no `.cs` touched)
**Landed in:** `f4e4630e3` ("fix live tester regressions and recurring dragon", 2026-09-09 14:04:37)
**Ancestry proof:** `git merge-base --is-ancestor f4e4630e3 HEAD` -> exit 0 (HEAD = `184c8ff06`, `dev`)
**Compile proof:** `Builds/compile-gate-recurring-dragon-spells.log` (2026-09-09 13:49) ->
`COMPILE_GATE_OK :: scripts compiled clean`.
**Capture-predates-fix proof:** the source captures are
`logs/f8-inbox/capture-20260909-125124-seq4969.md` (12:51). The fix commit is 14:04:37 the same day.

## FILE:LINE PROOF AT HEAD

The unsatisfiable request is gone. `Assets/_Modules/Village/Waves/WaveManager.cs:2715`:

```
GameObject prefabObject = DeNelle.Core.EnemyAssetLoader.LoadEnemyPrefab("Boss_Dragon");
_apexBossPrefab = prefabObject != null ? prefabObject.GetComponentInChildren<DragonBoss>(true) : null;
```

- `EnemyAssetLoader.LoadEnemyPrefab` exists at
  `Assets/_Modules/Core/Addressables/EnemyAssetLoader.cs:102` and is
  `LoadPrefixed<GameObject>(slug)` - the address is asked for as the type Addressables actually
  publishes it as.
- **A GameObject that loads but lacks the component fails loudly, naming address and type**
  (acceptance item 3): `WaveManager.cs:2800`
  `"SpawnApexBoss: GameObject address 'Enemies/Boss_Dragon' did not yield a DragonBoss within {N}s;
  releasing the clear gate for wave {waveId}."` (`FlowTrace.Fail`).
- A not-yet-resident asset no longer drops the boss: `_apexSpawnPending = true;` +
  `AwaitApexBossPrefab(boss, _currentWaveId)` polls with `ignoreTimeScale: true` and re-enters
  `SpawnApexBoss` on success (`:2780-2795`), holding the apex clear gate meanwhile.
- The in-code RCA is recorded at the seam (`:2711-2714`), citing the 2026-09-09 Wave-20 trace.
- **A regression pins the resolve** (acceptance item 5):
  `Assets/Editor/Regression/ApexDragonSpawnRegression.cs:57` emits
  `APEX_DRAGON_SPAWN_OK GameObject address resolved; pending load holds clear gate; ...`, and it is
  registered in `Assets/Editor/Regression/DataRegression.cs` (grep `apex-dragon-spawn`).

## RESIDUAL - ACCEPTANCE ITEM 4 IS **NOT** MET

`Assets/_Modules/Core/Addressables/EnemyAssetLoader.cs` still reads, verbatim at HEAD:

```
/// Generic escape hatch for enemy assets addressed by a FULL Resources-relative key
/// (prefix included), e.g. <c>LoadEnemyAsset&lt;DragonBoss&gt;("Enemies/Boss_Dragon")</c>.
/// </summary>
public static T LoadEnemyAsset<T>(string key) where T : Object => Load<T>(key);
```

**The XML doc example IS the bug, and it is unchanged.** The WO states plainly: *"Anyone following the
docs writes it again. Fixing the doc is part of this ticket, not a nicety."* Nor was any type screen
added - the constraint is still `where T : Object`, which admits `Component`, so the compiler still
accepts a call that can only fail at runtime. **The player-facing defect is fixed and pinned; the
systemic hole behind it is not.** Recommend a follow-up WO (doc fix + a type screen + the sweep of
`StructureAssetLoader` / the hero prewarmer the WO's own Unproven section asks for).

## WHAT THE OWNER FELT-TESTS

Reach an apex wave. The dragon should appear and orbit the Heart. No `InvalidKeyException` for
`Enemies/Boss_Dragon` in the capture.

## WHAT IS **NOT** PROVEN

- **Acceptance item 1 ("confirmed by a run, not by compile-green") is not met.** No apex wave has
  been driven since the change; `ApexDragonSpawnRegression` asserts the address resolves, which is not
  the same as a dragon flying.
- No fresh `REGRESSION_OK <n>/<n>` marker was produced for this change; the only fresh gate evidence
  is the compile gate. (The 2026-09-09 checkpoint regression log is separately RED on two unrelated
  suites - see `docs/READY_RCA_2026-09-09.md`.)
- Whether the apex wave has EVER spawned its dragon historically remains unestablished, as the WO says.

---

## 2026-09-09 pins (edit-only PINS lane) - NO new suite written, and here is why

**`[apex-dragon-spawn]` already pins this ticket's pinnable half. It was read, not assumed:**
`Assets/Editor/Regression/ApexDragonSpawnRegression.cs` (read in full this session) asserts the
GameObject-address resolution (`LoadEnemyPrefab("Boss_Dragon")` +
`GetComponentInChildren<DragonBoss>(true)`), FAILS if the old component-typed request comes back, and
pins that the wave cannot clear while the apex prefab is still loading (`_apexSpawnPending ||`). It is
registered at `Assets/Editor/Regression/DataRegression.cs:726`. **Acceptance items 3 and 5 are covered;
duplicating them would add a second copy of a live assertion, which is the failure mode CLAUDE.md S2/S5
records.** No file was written for them.

**Acceptance item 4 CANNOT be pinned green from an edit-only lane - it is a FOLLOW-UP, not a pin.**
Item 4 reads *"the doc example at `EnemyAssetLoader.cs:112-115` compiles and works as written."* Read at
source this session, `Assets/_Modules/Core/Addressables/EnemyAssetLoader.cs` still carries
`LoadEnemyAsset<DragonBoss>("Enemies/Boss_Dragon")` in its XML doc, and the constraint is still
`where T : Object`, which admits `Component`. **The item is UNMET on HEAD.** A pin asserting the fixed
state would therefore be RED the moment it landed, and this lane may not touch a producer to make it
green - so writing it would either red the gate or force a producer edit outside this lane's scope.

**Recommended follow-up WO (number to be minted off the `CLI_LANES_WO_NUMBERS.md` banner):**
1. Fix the XML doc example on `LoadEnemyAsset<T>` to a GameObject-typed call.
2. Add a type screen so a `Component`-typed request cannot compile (or fails loudly at the seam with the
   address and the type), instead of failing only at runtime.
3. Sweep the sibling loaders the WO's own Unproven section names - `StructureAssetLoader` and the hero
   prewarmer - for the same shape.
4. **Then** a pin is trivial and belongs with that change: a source case asserting the doc example is
   GameObject-typed, plus a compile-time-or-loud-failure assertion on the type screen. It is cheap to
   write once the producer is right, and expensive/dishonest before.

**Unproven:** the apex dragon has still not been seen flying (item 1); nothing in this lane changes that.
This lane has no Unity and executed nothing.

---

## 2026-09-09 — lane LOADER: acceptance item 4 IMPLEMENTED (edit-only, awaiting gate)

The PINS lane above was right that item 4 could not be pinned green **while the producer was wrong**.
This lane owns the producer, so the follow-up it recommended is done here rather than deferred.

**Files (edit-only; no Unity, no git):**
- `Assets/_Modules/Core/Addressables/EnemyAssetLoader.cs`
  - XML doc on `LoadEnemyAsset<T>` rewritten. The example is now
    `LoadEnemyAsset<GameObject>("Enemies/Boss_Dragon")` followed by
    `GetComponentInChildren<DragonBoss>(true)` — byte-for-byte the shape this RESULT already proved
    working at `WaveManager.cs:2715`. The broken Component-typed literal does not appear anywhere in
    the file, not even as a counter-example, so a lint for it cannot match its own tombstone.
  - **Type screen** at the head of `Load<T>` (so it covers `LoadEnemyAsset`, `LoadEnemyPrefab` and
    `LoadEnemyController` alike): when `typeof(Component).IsAssignableFrom(typeof(T))` it emits
    `FlowTrace.Fail` naming the address AND the requested type, then returns null. **Null, not a
    throw** — this runs on the player path from wave callbacks and scene entry, and the file's whole
    contract is that a content problem degrades rather than stops. Reports dedupe per address+type so
    a per-frame caller cannot flood the log and evict the boot window (§12).
  - New public surface: `IsUnsatisfiableComponentRequest(Type)` (pure, no side effects),
    `const string TypeScreenMarker` (the gate matches this, never a retyped copy),
    `ResetTypeScreenReports()` (documented gate-only).
  - A `using SysType = System.Type;` alias was required: the class declares
    `public const string System`, which shadows the namespace inside it.
  - Every existing FlowTrace/Guard/Once/Throttle call is intact — `EnemyLoadBoundedRegression`'s
    RequireToken set (`EnemyContentWarmer.TryGet`, `.Request`, `FlowTrace.Fail`, `SecondsWaiting`,
    `FamilyOf`, no blocking call) is unaffected.
- `Assets/Editor/Regression/EnemyAssetTypeScreenRegression.cs` — NEW.
  Marker `ENEMY_ASSET_TYPE_SCREEN_OK` / `..._FAIL x<n> :: <reasons>`.
  Cases: (1) `LoadEnemyAsset<DragonBoss>("Enemies/Boss_Dragon")` returns null and does not throw;
  (2) the refusal is **captured**, not linted — the suite installs a `CapturingSink` on
  `FlowTrace.Sink` (forcing `FlowTrace.Enabled`, restoring both in `finally`) and asserts the emitted
  line carries the marker, the address and the type; (3) `LoadEnemyPrefab("Boss_Dragon")` emits **no**
  refusal (it asserts the screen did not fire — deliberately NOT that the asset resolves, which is
  item 5's domain and depends on batchmode Addressables state); (3b) the predicate is true for
  `DragonBoss`/`Transform`, false for `GameObject`/`RuntimeAnimatorController`/`Texture`; (4) the doc
  lint for item 4 itself.
  It does **not** re-assert items 3 or 5 — `[apex-dragon-spawn]` owns those.

**Registration line (for the DataRegression owner — this lane must not touch that file), to sit
directly after `apex-dragon-spawn` at `Assets/Editor/Regression/DataRegression.cs:726`:**

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-asset-type-screen suite", () => { if (!DeNelle.Editor.Regression.EnemyAssetTypeScreenRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-asset-type-screen] " + r); });
```

**RED-first mutations (described, NOT run — this lane has no Unity):**
1. Delete the `if (IsUnsatisfiableComponentRequest(typeof(T)))` block from `Load<T>` → the DragonBoss
   request falls through to `EnemyEditorSyncResolver`, no marker line is captured → case 2 fails with
   *"the Component-typed refusal was NOT traced"*.
2. Restore the old doc literal `LoadEnemyAsset&lt;DragonBoss&gt;("Enemies/Boss_Dragon")` → case 4 fails
   with *"the XML doc STILL prescribes the broken Component-typed call"*.

**Gate hygiene:** brace balance 49/49 (loader) and 17/17 (regression); zero NUL bytes in both, read as
bytes.

**NOT PROVEN by this lane:** no Unity ran here, so compile-green, `REGRESSION_OK` and both RED
mutations are **described, not executed**. "The doc example works as written" is proven only by it
matching the shape this RESULT already verified at `WaveManager.cs:2715` — it has not been compiled or
run from the doc. Item 1 (a dragon actually seen flying) is still unmet, unchanged by this lane. The
sibling sweep (`StructureAssetLoader` / `HeroAssetLoader` / `VfxAssetLoader`) was **not** done: item 4
does not name it, and those files were not touched.
