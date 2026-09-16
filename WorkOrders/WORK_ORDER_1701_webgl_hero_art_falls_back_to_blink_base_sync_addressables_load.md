# WORK ORDER 1701 - On WebGL the hero is the naked Blink base: HeroAssetLoader resolves the hero prefab with WaitForCompletion, which WebGL refuses

**Follow-up 2026-09-13:** Registered body warm failures now block Ready with Retry instructions; regression added, Unity execution UNRUN by edit-only lane. Original sync guard remains present. Owner bounce cause is not established by the old trace; actual Seeker/Pi Browser art acceptance remains OPEN. See dated RESULT follow-up before treating historical FIXED language as current proof.

**Follow-up 2026-09-14 (this lane):** The `HeroAssetLoader.cs` / `HeroContentPrewarmer.cs` fix (warm-cache-first, `WaitForCompletion` guarded behind `#if !UNITY_WEBGL || UNITY_EDITOR`) was ALREADY COMMITTED at HEAD before this lane started (`a4c0e7cd1`, `e54ebe540`) — confirmed via `git log`/`git status`, working tree clean for both files. The owner's 2026-09-14 Fail was felt-tested on build `2026.09.10.364108` (2026-09-10), which PREDATES both fix commits, so that bounce cannot be evidence the committed fix is wrong; it is evidence device/WebGL-content acceptance was never re-run after the fix shipped. Confirmed the ticket's own flagged residual is real and was NOT a separate WO: `HeroTextureLoader.cs:79` (`Assets/_Modules/Core/Addressables/HeroTextureLoader.cs`) still called `handle.WaitForCompletion()` completely unguarded, reached from the same `"HeroAssets"` `Guard.Try` tag as the fixed hero-prefab loader — this is the file behind the captured `Enemies/OrcTex/Orc_Warrior_basecolor` throw in the WO's own section 1 evidence. Fixed: wrapped that call in the identical `#if !UNITY_WEBGL || UNITY_EDITOR` guard used in `HeroAssetLoader.cs`, and corrected the same "likely missing from the CDN (never pushed)" unproven-cause warning text this file also carried. Extended `HeroAssetLoaderWebGlRegression.Case1_BlockingCallIsGuarded` to lint `HeroTextureLoader.cs` for the same unguarded-call shape (no new `DataRegression.cs` registration needed — the suite entry point is already registered there). Brace/NUL gate clean on both touched files (`gate_brace.py`: `bad=0 of 2`). NOT run through the Unity compiler/regression by this lane (per instruction, the lead batches that). Device/WebGL-content acceptance (section 4) remains OPEN and is NOT closed by this lane.

**Status:** READY TO IMPLEMENT - owner felt-test 2026-09-14 Fail (marked 2026-09-11T01:14:33, build 2026.09.10.364108). Bounced from Fixed. PRIOR STATUS: FIXED PENDING LEAD GATE + DEVICE ACCEPTANCE - HeroTextureLoader.cs:79 residual closed 2026-09-14 (this lane); HeroAssetLoader.cs/HeroContentPrewarmer.cs fix already committed pre-session (`a4c0e7cd1`, `e54ebe540`); awaiting lead COMPILE_GATE_OK + REGRESSION_OK on a fresh log, then a WebGL content build + r2-ship + Pi Browser/Seeker session against a build newer than 2026.09.10.364108 for section 4 acceptance
**Implemented:** 2026-09-10 by the WO-1701 SME lane (worktree `agent-a74b360b34c7b3fb0`). Code + regression written, brace/NUL gate clean, lint proven RED on HEAD and GREEN after. NOT gated in Unity, NOT committed - the lead batch-gates and commits, and registers the suite in `DataRegression.cs`. Result: `WorkOrders/WORK_ORDER_1701_webgl_hero_art_falls_back_to_blink_base_sync_addressables_load.RESULT.md`.
**Minted:** 2026-09-10 17:05 by the CLI lead from the owner's bug report #4 (Seeker, Pi Browser, build 2026.09.10.364108@defenders-pi.vercel.app; owner: "the character is the fallback blink naked mage"). Main-line banner bumped 1701 -> 1702 in the same edit.
**Silo:** Core / Addressables (`Assets/_Modules/Core/Addressables/HeroAssetLoader.cs`, `HeroContentPrewarmer.cs`). Pi / WebGL surface only; the APK and exe hit a warm local cache and are unaffected.
**Severity:** P1 for the Pi listing - every WebGL player sees the placeholder body, and the Pi portal reviewer will too.

## 1. Captured data (2026-09-10, web_trace session wt-56a6e825d88; bug_reports id 4 screenshot shows the bare HumanMale body holding a staff)
- 21:54:23Z `[Flow:HeroPrewarm] 'Mage' art downloaded and cached on attempt 1 - Ready.`
- 21:54:24Z `error: [Flow:HeroAssets] Addressables resolve 'Heroes/Mage' (GameObject) FAILED: Exception: WebGLPlayer does not support synchronous Addressable loading. Please do not use WaitForCompletion on the WebGLPlayer platform.`
- 21:54:24Z `warning: [Flow:HeroAssets] Addressables 'Heroes/Mage' IS registered but resolved null - the bundle is likely missing from the CDN (never pushed). Fell back to Resources.Load("Heroes/Mage") -> ALSO NULL.` (the warning's own theory is wrong: the bundle was downloaded one second earlier; the throw is the cause)
- 21:54:24Z `error: [Flow:HeroAssets] hero asset 'Heroes/Mage' (GameObject) not found via Addressables OR Resources - caller falls back.` then `[Flow:HeroBody] class=Mage slug=Mage - kicking Blink base load 'hero/base/HumanMale'.`
- Same throw, three times, for `Enemies/OrcTex/Orc_Warrior_basecolor` at 21:55:01Z (HeroAssets texture path).
- R2 parity is green for WebGL (`R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=279`, 16:38 local), so this is not a missing push.

## 2. Cause (read at source)
`HeroAssetLoader.cs:94-95`: `var handle = Addressables.LoadAssetAsync<T>(address); result = handle.WaitForCompletion();` inside `Guard.Try`. The comment above it says this is safe because `HeroContentPrewarmer` already downloaded the bundle. On WebGL that is not enough: Addressables throws on `WaitForCompletion` regardless of cache state, `Guard.Try` logs and swallows, the result stays null, and `HeroBodySwapper` degrades to the Blink base body. The structure and enemy loaders already solved this (`StructureContentWarmer.cs:74` "DO NOT ADD WaitForCompletion", `StructureEditorSyncResolver` is the one allowlisted sync site, editor-only); the hero loader was never brought in line.

## 3. Deliverable
- `HeroContentPrewarmer` keeps the loaded `GameObject` / `Texture2D` / `RuntimeAnimatorController` handles it already fetches (it downloads the bundle today; make it `LoadAssetAsync` the hero's addresses and hold the handles), and `HeroAssetLoader` consults that warm dictionary FIRST. On every platform a warm hit is a dictionary read. The `WaitForCompletion` branch stays for the editor and standalone (behind `#if !UNITY_WEBGL`), and on WebGL a miss falls to `Resources.Load` exactly as now, with a `FlowTrace.Fail` naming the miss as "prewarm did not hold this address" rather than "never pushed".
- Fix the misleading warning text at the same time (it asserts "never pushed" without evidence).
- Regression: an editor case that drives `HeroAssetLoader.LoadHeroPrefab("Mage")` after a simulated prewarm and asserts the dictionary path is taken; a source lint that `HeroAssetLoader.cs` has no `WaitForCompletion` outside the `#if !UNITY_WEBGL` block. Register in `DataRegression.cs` (lead does the registration line).
- Also check `Enemies/OrcTex/*` texture resolves through the same seam (same throw, same session).

## 4. Acceptance
- WebGL on the Seeker in Pi Browser: the Mage enters the town in the Mage art (not the HumanMale base), zero `WebGLPlayer does not support synchronous Addressable loading` lines in the session trace.
- APK and exe: byte-identical hero path (warm cache hit).

## 5. Not this ticket
- `[Flow:VisualFactory] model not found via Addressables OR Resources: 'Structures/Tower_Wooden_Watchtower'` and `'Structures/lumbermill'` (21:48Z, same session) - the untextured watchtower in the same screenshot. Different loader (`VisualFactory`), no sync throw recorded; separate RCA needed.
- The 65% loader crash (WO-1314 / WO-1484) and the "long tap to keep a panel open" report (no capture yet).

## Lead-lane verification 2026-09-14

Read-only lane, branch `dev` HEAD `f06a73600`. Every line below was opened or run this session.

### 1. Claimed files vs the tree
| File | `git status --short` | `git diff --stat` | Claimed edit found at source |
|---|---|---|---|
| `Assets/_Modules/Core/Addressables/HeroContentPrewarmer.cs` | ` M` | 26 ins / 13 del | YES - `State = Downloading` at start of the attempt (`:195`), `if (!ValidateWarmBodies(slug)) yield break;` on BOTH Ready paths (`:232`, `:301`), new `ValidateWarmBodies` (Failed + "tap Retry" + FlowTrace.Warn naming the address), `Addressables.Release(handle)` added on the failed-warm path |
| `Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs` | ` M` | 53 ins / 0 del | YES - `Case4_FailedBodyCannotPassReady` added and invoked from `Run` |
| `Assets/_Modules/Core/Addressables/HeroAssetLoader.cs` | clean (no diff) | - | Consistent with the RESULT's "no HeroAssetLoader change was needed". The 09-10 guard is already at HEAD in commit `a4c0e7cd1`: `TryGetWarm` probe `:137`, `#if !UNITY_WEBGL || UNITY_EDITOR` / `WaitForCompletion` `:172-174` |
| `Assets/Editor/Regression/DataRegression.cs` | ` M` | 22 ins / 3 del | **NOT this lane.** The suite registration is already at HEAD (`:1773`). The working-tree diff is other lanes' work (owned-town proofs, fresh-town-building-visual, Stone/Food cost fields). **Must NOT be staged with WO-1701.** |
| `Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs.meta` | tracked, clean | - | Exists (09-10 18:51); the RESULT's "meta does not exist yet" is stale |

### 2. Marker evidence - the "UNITY TESTS UNRUN" claim is STALE
- Both edited files: mtime `2026-09-13 21:09:56`.
- `Builds/night-hero-webgl.log`, mtime `2026-09-13 21:56` (Unity 6000.4.8f1, `-projectPath D:\eoa`, `-executeMethod DeNelle.Editor.Regression.HeroAssetLoaderWebGlRegression.RunAll`), **0 `error CS` lines**, `:489` `HERO_WEBGL_LOAD_OK`. `:481` carries the new Case-4 line `[Flow:HeroPrewarm] world entry BLOCKED: registered body 'Heroes/__wo1701_warm_probe__' was not held...` and `:488` `Readiness probe: missing registered body blocks; retained retry body passes; both warm paths checked.` The run therefore compiled and exercised THESE bytes.
- `Builds/data-regression.log`, mtime `2026-09-14 04:17`: `:17762` `[hero-webgl-load] ...`, `:15428-15434` the same Case-2/Case-4 probe lines, and `:17875` `REGRESSION_OK 522/522 suites -- 522 green, 0 red, 0 skipped`. The suite is registered AND green inside the full run, on the current working-tree bytes.
- `HERO_WEBGL_LOAD_FAIL` appears in no `Builds\*.log`.

### 3. Brace / NUL
`python tools/gate_brace.py` on both edited files: `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0. Raw counts `HeroContentPrewarmer.cs` 81/81, `HeroAssetLoaderWebGlRegression.cs` 61/61; `NUL=0` on both.

### 4. Cross-reference against untracked files
The two edited files reference only `HeroAssetLoader`, `HeroContentPrewarmer`/`HeroPrewarmState` (declared in the edited file, `:62`), `ComposedDungeonRunRegression` (tracked, clean), `Guard`/`FlowTrace`, and Unity Addressables types. **No `??` untracked file is referenced** - a by-path commit of the two files cannot break. The 30+ untracked `Assets/Editor/Owned*/Owner*/Raid*Proof.cs` files belong to other lanes and are unrelated.

### 5. Acceptance, item by item
| Criterion | Verdict |
|---|---|
| sec.4 WebGL on Seeker in Pi Browser: Mage enters in Mage art | **UNPROVABLE HEADLESS.** Needs a WebGL content build + `tools
2-ship.ps1` push for THAT build (bundle names are content-hashed, CLAUDE.md sec.16) + a Pi Browser session on the Seeker with the trace captured. |
| sec.4 zero `WebGLPlayer does not support synchronous Addressable loading` lines in session | **NOT PROVEN, and structurally still reachable.** `HeroTextureLoader.cs:79` still calls `WaitForCompletion` with no `#if` guard (opened at source; its own header `:26-27` records the WebGL caveat). The captured `Enemies/OrcTex/Orc_Warrior_basecolor` throws come from that file, so a session can still emit those lines. |
| sec.4 APK and exe byte-identical hero path (warm cache hit) | **NOT PROVEN** headless; no build comparison run. Structurally the sync branch is preserved by `#if !UNITY_WEBGL \|\| UNITY_EDITOR` (`HeroAssetLoader.cs:172`). |
| sec.3 warm-cache-first + guarded sync branch + fixed warning text | **MET** - `HeroAssetLoader.cs:137/:172-174`, already committed in `a4c0e7cd1`. |
| sec.3 regression (behaviour case + source lint) registered in `DataRegression.cs` | **MET** - suite at `Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs`, registration `DataRegression.cs:1773`, green in `REGRESSION_OK 522/522`. |
| sec.3 "also check `Enemies/OrcTex/*` through the same seam" | **NOT MET** - deliberately deferred by the lane (`HeroTextureLoader` untouched); needs its own WO. |
| Follow-up claim: registered body warm failure blocks Ready with Retry | **MET and PROVEN** - Case 4 green on two fresh logs (sec.2). |

**Observation (not a blocker):** the follow-up diff replaced the `WarmAssets` XML doc block with the `ValidateWarmBodies` doc, so `WarmAssets` now has no summary comment; its open-design-question note (old RESULT item 4.5) is gone because the question is now answered in code.

### Recommendation
**COMMIT-READY** for exactly two paths: `Assets/_Modules/Core/Addressables/HeroContentPrewarmer.cs` and `Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs` - both green on a fresh `REGRESSION_OK 522/522` log dated after the edits; do **not** stage `DataRegression.cs` (other lanes' work) and do not flip the ticket to FIXED, since section 4 acceptance is device-only and `HeroTextureLoader` remains an unguarded sync seam.
