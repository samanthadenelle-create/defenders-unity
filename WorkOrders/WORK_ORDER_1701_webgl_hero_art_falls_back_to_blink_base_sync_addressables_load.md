# WORK ORDER 1701 - On WebGL the hero is the naked Blink base: HeroAssetLoader resolves the hero prefab with WaitForCompletion, which WebGL refuses

**Status:** FIXED - gated (Builds/wave12-compile1 COMPILE_GATE_OK, Builds/wave12-reg2 REGRESSION_OK 507/507) and deployed to defenders-pi.vercel.app 2026-09-10 19:25; NOT device-proven (owner out of tokens) - next Seeker session in Pi Browser: the Mage must enter the town in Mage art
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
