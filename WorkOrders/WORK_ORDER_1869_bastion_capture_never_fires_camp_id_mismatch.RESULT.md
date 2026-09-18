# WO-1869 RESULT — camp-id resolution moves from string surgery to the authored catalog

**Status:** IMPLEMENTED PENDING LEAD GATE (edit-only lane — no Unity run, no gate, no commit from this lane).
**Date:** 2026-09-18. **Branch:** `dev`.

---

## 1. What changed

### `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs`

| Lines | Change |
|---|---|
| `:976-1013` | The RCA block replacing the old two-line comment. Records the measured device lines, the PascalCase-vs-snake_case asymmetry, and the ruling that the scene is deliberately NOT renamed. |
| `:1014-1015` | The instance method is now a one-line delegate: `private string ResolveConfigId(RaidGarrisonSpawner spawner) => ResolveConfigId(gameObject.scene.name, spawner != null ? spawner.ConfigId : null);` — the two existing call sites (`:311`, `:385`) are byte-identical and untouched. |
| `:1016-1074` | The new pure static resolver (declaration at `:1031`) (signature below), with the three-step order and the WO's required `FlowTrace.Step` at the resolve seam. |

**⚠ One WO pointer corrected at source.** WO-1869 §Fix item 2 cites `RaidSelectionScreen.cs:452` as a literal `"RaidBase_IronBastion"` check. Grepped 2026-09-18: that line (`Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:451`, and the path in the WO is also wrong — the file is under `Village/Hero/`, not `Village/UI/`) is a **comment**, not a check. The "do not rename" ruling is nonetheless correct and if anything under-stated: the real code sites are `RaidVictoryController.cs:191`, `SceneRouter.RaidBaseIronBastion` (`Assets/_Modules/Core/SceneRouter.cs:195`), `RaidCaptureCensus.cs:26`, `OwnedTownScenePose.cs:59`, `DevSkipKit.cs:87`, `Editor/WallTools/RaidBaseGenerator.cs:299`, both `scene-configs.json` copies and six editor suites. The corrected list is recorded in-code at `:1002-1008`.

Nothing else in the file was touched: no call-site edit, no gate edit, no `System.Reflection`, and **every pre-existing `FlowTrace` call is intact** (CLAUDE.md §12 — instrumentation is permanent). Net FlowTrace delta is `+1 Step` (always, naming the path taken) and `+1 Warn` (fallback only).

**The exact new resolver signature:**

```csharp
public static string ResolveConfigId(string sceneName, string spawnerConfigId)
```

Resolution order, in one sentence each:

1. **`spawner`** — `!string.IsNullOrWhiteSpace(spawnerConfigId)` returns it as-is. The spawner is first because it printed the *correct* id (`config 'iron_bastion'`) in the same second the controller printed the wrong one.
2. **`catalog`** — `SceneConfigCatalog.FindBySceneName(sceneName)?.id`. `FindBySceneName` already existed (`Assets/_Modules/Village/World/SceneConfigCatalog.cs:293-305`, ordinal-ignore-case) and reads the same lazily-loaded `scene-configs.json` the game uses; no new loader, no new JSON, no schema change.
3. **`strip`** — the legacy body byte-for-byte (`OrdinalIgnoreCase` `StartsWith("RaidBase_")`, `"unknown"` for an empty scene), preceded by a `FlowTrace.Warn` naming the scene and the derived id, so the next log proves a derivation happened instead of hiding it.

Public, not internal: the suite lives in `DeNelle.EditorRegression` and must call **the same function the controller calls**, not a copy. The suite uses **no reflection**.

`sceneLabel` is computed into a local before the interpolated `Step` on purpose — a nested string literal inside an interpolation hole is exactly the `CompileGate.BraceBalanced` trap CLAUDE.md §1 documents (a file can read 210/210 raw and 175/174 at the gate).

### `Assets/Editor/Regression/RaidConfigIdResolveRegression.cs` (NEW, 448 lines)

Tag `[raid-config-id]`, markers `RAID_CONFIG_ID_OK` / `RAID_CONFIG_ID_FAIL`, shape `public static bool Run(out string reason)`, never throws, mutes `Raid`/`OwnedBase`/`CanonJson`/`World` in the `try` and calls `FlowTrace.AllOn()` in the `finally`.

- **Case A — catalog parity.** Iterates `SceneConfigCatalog.All` (the real loader) and filters rows exactly the way `RaidVictoryController.KnownRaidConfigIds` (`:627-645`) filters them (`sceneName.StartsWith("RaidBase", OrdinalIgnoreCase)`), then asserts `ResolveConfigId(row.sceneName, null) == row.id` ordinally for every row. **Two vacuity guards**, both load-bearing: zero raid rows is a FAIL (a broken catalog load would otherwise pass this case while proving nothing), and the absence of a row whose id is `OwnedBaseProgression.FinalRaidId` is a FAIL.
- **Case B — capture-gate reachability (an honest PROXY, and it says so in its own reason strings).** B1: `ResolveConfigId("RaidBase_IronBastion", null) == OwnedBaseProgression.FinalRaidId`. B2/B3: the resolved id is driven through the real `OwnedBaseProgression.TryCapture`, which applies the same `FinalRaidId` and `CaptureStarsRequired` gates the controller's inline expression reads — at full stars the refusal must have moved *past* the id gate to `"Captured template is missing"`, and one star short it must still refuse for the *star* reason (`"three-star"`), so the fix cannot loosen the bar. B4: source pin (comment-stripped) that the controller still assigns `captureRaidId = ResolveConfigId(` and that `captureRaidId == OwnedBaseProgression.FinalRaidId` appears **at least twice** — the capture gate *and* the WO-1783 shortfall latch.
- **Case C — preference order and safe degrade.** The spawner id wins; an un-catalogued `RaidBase_*` scene degrades to the warned strip rather than throwing; a null scene still yields the literal `"unknown"`.

`CaptureStrandExitRegression` is **untouched and not weakened** — it sets `_captureRequired` directly and therefore can never see this defect, which is why this suite exists beside it.

---

## 2. Why Case A is RED on HEAD — reasoned from the code and the captured log, and one honest caveat

**The caveat first (CLAUDE.md §11B):** I cannot run Unity from this lane, so I have not *observed* a FAIL line. Worse, the WO's literal instruction ("run it before the fix and paste the FAIL line") is **not executable as written**: on HEAD the `(string, string)` overload does not exist, so the suite would not compile against the pre-fix tree. The red-first proof is therefore (a) the measured device evidence that the strip path produced the wrong id, and (b) a **body-only** revert recipe that reproduces the red without a compile break. Both are below.

**(a) The measurement — `Logs/device/logcat-bastion-victory-20260918.txt`, build 2026.09.18.374427:**

```
:202350  [Flow:Raid] OBJECTIVE COMPLETE - RaidSpire 'RaidSpire' (config 'iron_bastion') RAZED.
:202352  [Flow:Raid] VICTORY — raid 'IronBastion' won (SPIRE RAZED).
:202395  [Flow:EndState] RAID VICTORY composed: baseClaimed=False captureStarsRequired=0 stars=3 lead=ordinary-clear.
```

Two different ids for one camp, two lines apart, from the same settlement. `grep -c 'CAPTURE ELIGIBLE'` = 0 and `grep -c 'highest raid settled'` = 0 across the whole 208k-line log; one of the two **must** print whenever the ordinal compare at `:387`/`:395` holds, so the compare failed. That is the defect Case A asserts against, measured, not inferred.

**(b) Row-by-row, against the strip-only body:**

| `sceneName` (scene-configs.json) | strip yields | row `id` | Case A |
|---|---|---|---|
| `RaidBase_raider_camp_small` | `raider_camp_small` | `raider_camp_small` | pass |
| `RaidBase_fortified_garrison` | `fortified_garrison` | `fortified_garrison` | pass |
| `RaidBase_mage_enclave` | `mage_enclave` | `mage_enclave` | pass |
| `RaidBase_IronBastion` (`scene-configs.json:304`/`:310`) | `IronBastion` | `iron_bastion` | **FAIL** |

The three snake_case scenes agree with the catalog **by coincidence of naming**, which is precisely why the defect hid for so long. Exactly one row is red, and it is the row that owns the town capture. The FAIL text the suite would emit:

```
RAID_CONFIG_ID_FAIL raid-config-id CASE A: row 'iron_bastion' (scene 'RaidBase_IronBastion') resolved to
'IronBastion'. The resolver is deriving the id from the scene name instead of reading the catalog row that
already holds it - the WO-1869 defect. ...
```

Case B1 is red on the same tree for the same reason, and B2 would return `"A completed final-tier raid receipt is required."` instead of `"Captured template is missing."` — a second, independent red line from the real `OwnedBaseProgression` contract.

**Green after:** step 2 returns `iron_bastion` for `RaidBase_IronBastion` straight off `scene-configs.json:304`, and returns each snake_case row's own id for the other three (the catalog row is the authority in every case now, so the coincidence is no longer load-bearing). Case B1 then equals `FinalRaidId`, B2 falls through the id and star gates to the template refusal, B3 still refuses at 2 stars, B4's two source pins hold against the unmodified gate lines.

---

## 3. Revert recipe (per case)

**Body-only — do NOT revert the signature** (a signature revert stops the suite compiling, which is a build break, not a red test):

1. Open `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:1031`.
2. Replace the whole body of `public static string ResolveConfigId(string sceneName, string spawnerConfigId)` with the pre-WO-1869 block:
   ```csharp
   const string prefix = "RaidBase_";
   if (!string.IsNullOrEmpty(sceneName) &&
       sceneName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
       return sceneName.Substring(prefix.Length);
   return string.IsNullOrEmpty(sceneName) ? "unknown" : sceneName;
   ```
3. Re-run the suite. `RunCore` short-circuits on the first failure, so the printed line is **Case A**'s, quoted above. B1/B2 and C1 are red on that same tree (the `FinalRaidId` compare, and the spawner id no longer winning) but are never reached unless A is bypassed. C2/C3 stay green — they pin the legacy degrade, which is deliberately unchanged.
4. Full revert of the change: `git checkout -- Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` and delete `Assets/Editor/Regression/RaidConfigIdResolveRegression.cs` (+ its `.meta`), plus the lead's one registration line in `DataRegression.cs`.

---

## 4. For the lead (what this lane deliberately did NOT do)

- **Register the suite.** One line beside the others in `Assets/Editor/Regression/DataRegression.cs` (pattern at `:1542`):
  ```csharp
  DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-config-id suite", () => { if (!DeNelle.Editor.Regression.RaidConfigIdResolveRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-config-id] " + r); });
  ```
  The suite total `<n>/<n>` in `REGRESSION_OK` moves by one as a result.
- **`.meta`** — `Assets/Editor/Regression/RaidConfigIdResolveRegression.cs.meta` does not exist yet; Unity writes it on the first import. Stage it with the suite (four other new `.cs` files' `.meta` entries are sitting `??` in `git status` for exactly this reason).
- **No Unity run, no gate, no commit, no `git add`, no scene edit, no JSON edit** from this lane.

## 5. Gate output (pasted verbatim)

```
$ python tools/gate_brace.py Assets/_Modules/Village/World/Camps/RaidVictoryController.cs Assets/Editor/Regression/RaidConfigIdResolveRegression.cs
GATE_BRACE_SUMMARY bad=0 of 2
EXIT=0
```

```
$ CLAUDE.md §1 brace + NUL one-liner
Assets/_Modules/Village/World/Camps/RaidVictoryController.cs: Braces balanced (138) OK, no NUL bytes
Assets/Editor/Regression/RaidConfigIdResolveRegression.cs: Braces balanced (43) OK, no NUL bytes
```

## 6. Acceptance still open

- `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs with the new suite green — **lead**.
- Next Seeker build: a 3-star Bastion clear logs `config id resolved: 'iron_bastion' via spawner for scene 'RaidBase_IronBastion'` followed by `CAPTURE ELIGIBLE`, and the rough-stone line no longer says `camp 'IronBastion'` — **owner felt-verify closes**.
  **Expect `via spawner`, not `via catalog`.** On the owner's run the spawner existed and already held the right id (`:202350` `config 'iron_bastion'` is the spawner's own line), so branch 1 wins; `via catalog` only appears when `_spawner` is null or its `ConfigId` is blank, and `via strip` should never appear for an authored scene. Reading `via catalog` on that log is not a failure either — both reach `iron_bastion` — but `via strip` on a catalogued scene IS, and the `Warn` beside it names why.
