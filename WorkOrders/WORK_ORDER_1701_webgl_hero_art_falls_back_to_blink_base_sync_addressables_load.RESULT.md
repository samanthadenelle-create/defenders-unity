# WORK ORDER 1701 - RESULT

**Status:** IMPLEMENTED - awaiting lead gate (NOT gated in Unity, NOT committed by this lane)
**Lane:** SME implementation lane, worktree `agent-a74b360b34c7b3fb0` (branch `worktree-agent-a74b360b34c7b3fb0`, HEAD `f5d39acd1`)
**Date:** 2026-09-10

---

## 1. What changed

### `Assets/_Modules/Core/Addressables/HeroAssetLoader.cs`
| Line | Change |
|---|---|
| `:27-54` | Header rewritten: resolution order is now **WARM CACHE -> Addressables sync -> Resources**, with the four captured Pi Browser lines quoted verbatim as the reason the guard exists. |
| `:97` | **NEW** `public static string AddressFor(string slug)` - the single address builder. `Load<T>` (`:124`) and `WarmableAddresses` both call it, so the prewarm can never warm a different string from the one the loader later asks for. |
| `:110` | **NEW** `public static IEnumerable<KeyValuePair<System.Type, string>> WarmableAddresses(string slug)` - the authoritative (type, address) set for a slug, derived from the two public entry points. The prewarm enumerates this; it does not keep a second list. |
| `:137-145` | **NEW step 0 in `Load<T>`**: `HeroContentPrewarmer.TryGetWarm(address, out T warm)` - a dictionary read, no catalog probe, no handle, no wait. On a hit it logs `FlowTrace.Step("HeroAssets", "warm-cache HIT '<address>' ... no Addressables call made.")` and returns. |
| `:172-179` | The `Addressables.LoadAssetAsync` + `WaitForCompletion` pair is now inside `#if !UNITY_WEBGL \|\| UNITY_EDITOR`. The **catalog probe (`AddressableRegistered<T>`) stays OUTSIDE the guard** - it starts no operation, cannot block, and its answer is what makes the WebGL diagnostic below meaningful. |
| `:199-215` | The misleading warning is fixed. It no longer asserts "the bundle is likely missing from the CDN (never pushed)"; it states what it knows (registered, prewarm did not hold it), says **CAUSE NOT DETERMINED FROM HERE**, and lists three candidates in the order worth checking. |
| `:216-220` | `FlowTrace.Fail` now carries the required wording - **"the prewarm did not hold '<address>'"** - and names the player-visible consequence (the Blink base body). |
| `:233` | `AddressableRegistered<T>` made `public` so the warm pass uses the loader's own probe instead of a second copy of the rule. |

### `Assets/_Modules/Core/Addressables/HeroContentPrewarmer.cs`
| Line | Change |
|---|---|
| `:19-42` | Header: records that downloading the bundle was never enough, with the two captured lines one second apart. |
| `:122` | **NEW** `s_warm` - `Dictionary<string, Object>`, keyed `"<TypeName>:<address>"` (same idiom as `StructureContentWarmer.Key`, `StructureContentWarmer.cs:1116`). Type is in the key because the hero prefab and its animator controller deliberately share one address. |
| `:129` | **NEW** `s_warmHandles` - the load handles, retained for the process. **Nothing is ever released**, matching the loader's documented parity with `Resources.Load`. |
| `:133` | **NEW** `public static int WarmCount`. |
| `:144` | **NEW** `public static bool TryGetWarm<T>(string address, out T asset)` - dictionary lookup only. |
| `:164` / `:176` | **NEW** test seams `SeedWarmForTests(Type, address, Object)` / `ClearWarmForTests()`. |
| `:230`, `:298` | `yield return WarmAssets(slug);` inserted on **both** Ready paths - the already-cached (0 bytes) path and the post-download path - before `State = Ready`. The `keys.Count == 0` path (editor AssetDatabase play mode / deliberately-local hero) deliberately does **not** warm. |
| `:447` | **NEW** `WarmAssets(slug)` - enumerates `HeroAssetLoader.WarmableAddresses` for every slug in `BodySlugCandidates`. Logs the count held and handles retained. `:472-481` records, in code, why the `Heroes/Textures/*` atlases are deliberately NOT warmed here (see section 4.3). |
| `:495` | **NEW** `WarmOne<T>(address)` - probe-then-`LoadAssetAsync`, fully async, `Guard.Try` around each step, `FlowTrace.Fail` naming the address when the bytes are local but the asset will not load. |

### `Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs` (NEW)
`namespace DeNelle.Editor.Regression`, matching `HeroRemoteContentRegression` / `FrameBudgetMeasureRegression` in the same folder.
- `public static void RunAll()` (`:79`), `public static bool Run(out string reason)` (`:85`)
- Markers `HERO_WEBGL_LOAD_OK` / `HERO_WEBGL_LOAD_FAIL` (`:56-57`)
- `Case1_BlockingCallIsGuarded` (`:122`) - source lint. Walks a preprocessor stack over the comment/string-blanked source (`ComposedDungeonRunRegression.StripCommentsAndStrings`) and fails on any `WaitForCompletion` occurrence not enclosed by an `#if` whose condition contains `!UNITY_WEBGL`. Also pins `TryGetWarm` before `Addressables.LoadAssetAsync`, and pins that `HeroContentPrewarmer.cs` has zero blocking calls and does call `LoadAssetAsync`.
- `Case2_WarmCacheServesTheLoader` (`:210`) - behaviour. Establishes the premise directly (`AddressableRegistered<GameObject>` false **and** `Resources.Load<GameObject>` null, so neither of the loader's other two sources can answer the probe address), seeds a throwaway `GameObject`, proves `HeroAssetLoader.LoadHeroPrefab` returns that exact reference, proves the cache is type-scoped, then clears the seam in a `finally` and proves the clear took.
  The premise is probed directly rather than by calling `LoadHeroPrefab` cold **on purpose**: a cold call ends in `FlowTrace.Fail`, which routes to `Debug.LogError` (`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:169-172`, read at source). A suite must not manufacture a red error line inside a green run - the gates are judged by grepping a fresh log, and a deliberate `LogError` is indistinguishable from a real one.
- `Case3_DetectorDiscriminates` (`:314`) - six synthetic sources; the detector must flag the unguarded / `#else`-arm / wrong-symbol shapes and must not flag the guarded, guarded-with-`UNITY_EDITOR`, or prose-only shapes.

---

## 2. The exact address list the prewarm now holds

For a chosen class `<Class>`, `BodySlugCandidates` yields `<Class>` plus, for Knight only, `KnightV3`, `KnightPackage`, `knightV2`. For each candidate the warm pass asks `HeroAssetLoader.WarmableAddresses`, which yields **two entries per candidate, one address, two types**:

| Warm key | Address | Type | Source |
|---|---|---|---|
| `GameObject:Heroes/<candidate>` | `Heroes/<candidate>` | `GameObject` | `HeroAssetLoader.WarmableAddresses` (`:110`) |
| `RuntimeAnimatorController:Heroes/<candidate>` | `Heroes/<candidate>` | `RuntimeAnimatorController` | same |

Concretely for the owner's Mage that is exactly two requests: `Heroes/Mage` as `GameObject` and `Heroes/Mage` as `RuntimeAnimatorController`. Any (type, address) pair the catalog does not register is skipped silently - the pre-existing V1-safe behaviour, not a new one. **Expect the `RuntimeAnimatorController` row to be a silent skip on the real catalog:** `HeroRemoteContentRegression`'s own summary text (`:159-171`) calls the controllers part of the deliberately-local set. I have NOT opened the built catalog to confirm whether `Heroes/<slug>` carries a `RuntimeAnimatorController` location, so I am not asserting either way - if it does not, `WarmOne` returns silently at the registration probe and nothing is logged. That costs nothing and the request stays in the list because the loader can still ask for it.

**Textures are deliberately NOT in this list.** `Heroes/Textures/*` is one shared bundle covering every class; warming all of it would put every class's atlas in the heap of the platform least able to afford it, and nothing would read those entries - `HeroTextureLoader` resolves for itself and does not consult this cache (it is outside this lane's edit list). The reasoning is written in code at `HeroContentPrewarmer.cs:472-481`. See section 4.3 for the follow-up, which must warm only the CHOSEN hero's atlases.

---

## 3. Proven this session

| Claim | Evidence |
|---|---|
| The source lint is **RED on HEAD** | Python port of the suite's exact `ScanBlockingCalls` + `StripCommentsAndStrings`, run over `git show HEAD:.../HeroAssetLoader.cs`: `occurrences=1 unguarded=1 at_code_lines=[95] warmProbeAt=-1 addrLoadAt=1370 -> RED (suite FAILS)`. Code-line 95 is the throw site the WO cites. |
| The source lint is **GREEN after the edit** | Same port over the working-tree file: `occurrences=1 unguarded=0 at_code_lines=[] warmProbeAt=1870 addrLoadAt=2736 -> GREEN (suite PASSES)`. The warm probe precedes the Addressables load. |
| The Case-3 oracle expectations are correct | All six synthetic cases run through the same port: `ORACLE_CASES bad=0 of 6` (unguarded flags, guarded passes, `!UNITY_WEBGL \|\| UNITY_EDITOR` passes, `#else` arm flags, `UNITY_STANDALONE` flags, prose/string-only passes). |
| Brace gate clean | `python tools/gate_brace.py` over all three files: `GATE_BRACE_SUMMARY bad=0 of 3`, exit 0. Baseline before the edit was also `bad=0 of 2`, so the pass is attributable. |
| No NUL bytes; raw brace balance | `HeroAssetLoader.cs` 33/33, `HeroContentPrewarmer.cs` 81/81, `HeroAssetLoaderWebGlRegression.cs` 54/54; `NUL=0` on all three. |
| Line endings + ASCII | All three files pure CRLF (250/592/476 CRLF, zero bare LF). The new regression file contains **zero non-ASCII characters**. |
| The two edited files matched the live `dev` working tree before I touched them | `diff --strip-trailing-cr` against `D:\EoA\Assets\...`: identical. (This worktree's HEAD is `f5d39acd1`, 09-07, but these files had not moved.) |

---

## 4. NOT proven - read this before closing the ticket

1. **Nothing was compiled, gated or run in Unity by this lane.** No `COMPILE_GATE_OK`, no `REGRESSION_OK`. The C# has not been through a compiler; the Python port proves the *rule*, not that the suite builds.
2. **The acceptance criterion is untested.** "The Mage enters the town in Mage art on the Seeker in Pi Browser, zero `WebGLPlayer does not support synchronous Addressable loading` lines" needs a WebGL build, an R2 push for that content build (CLAUDE.md section 16 - bundle names are content-hashed) and a device session. I have not done any of that.
3. **`Enemies/OrcTex/Orc_Warrior_basecolor` does NOT go through `HeroAssetLoader` - it is `HeroTextureLoader`.** The WO's section 1 lists that throw under "HeroAssets texture path"; the tag `"HeroAssets"` is shared by both files, which is why it reads as one seam. Read at source: the throw is `Assets/_Modules/Core/Addressables/HeroTextureLoader.cs:79` (`result = handle.WaitForCompletion();`), reached from `Guard.Try("HeroAssets", $"Addressables resolve texture '{address}'", ...)` at `:73`. `HeroTextureLoader.cs` is **outside this lane's edit list**, so:
   - it still calls `WaitForCompletion` unguarded and **still throws on WebGL today**;
   - `Enemies/OrcTex/*` is not collected by `HeroContentPrewarmer.CollectKeys` (which only takes `Heroes/Textures/*`) and is not produced by `HeroAssetLoader.WarmableAddresses`, so **the warm cache does not hold it and cannot**;
   - `HeroTextureLoader.cs:23-24` states in its own header that the enemy `Enemies/OrcTex/*` atlases were deliberately not migrated, so on a normal build that address is unregistered and falls to `Resources.Load` - but the captured session shows it registered enough to reach the throw, and I have **not** established which is true for the WebGL catalog.
   - **Recommended follow-up (needs its own WO):** route `HeroTextureLoader.Load` through `HeroContentPrewarmer.TryGetWarm<Texture2D>`, guard its `WaitForCompletion` the same way, and have the warm pass load **only the chosen hero's atlases** - not the whole `Heroes/Textures/*` bundle, which spans every class. The cache is already generic and already keys `Texture2D:` entries, so the change is a probe, a guard and a narrowed key list. The lint in this suite can then be pointed at that file too.

4. **Companions take the same fallback, and this ticket does not fix them.** `StoryCompanionInjector.cs:526` and `:564` call `HeroAssetLoader.LoadHeroPrefab` / `LoadHeroController` for **other** classes' slugs. `HeroContentPrewarmer` warms only the CHOSEN class (`PrewarmChosenHero` reads one `HeroClass` off the save), so on WebGL a companion's address will miss the warm cache, hit the compiled-out sync branch, and fall through exactly as the Mage did. `AtbCombatantSwapper.cs:135/:645` resolve the player's own slug and are covered. Not this ticket - the lead needs it named and ticketed.
5. **Open design question, deliberately not decided here.** If the bundle downloads but a hero **body** fails to LOAD, `WarmOne` reports `FlowTrace.Fail` with the address and the pass continues; it does **not** flip `State = Failed` and hold the load screen the way a failed *download* does. The WO did not rule on this and I did not invent a rule. On WebGL a body that failed to warm is unrecoverable (the sync branch is compiled out), so an argument exists for holding the load screen - that is the owner's call.
6. **The WO refers to `HeroAssetLoader.Resolve<T>`; there is no such method.** The private resolver is `Load<T>` (now `:124`), reached from `LoadHeroPrefab` / `LoadHeroController`. I changed `Load<T>` and did not rename it - a rename would churn nothing useful and would break the WO's own citation of `:94-95`.
7. **`#if !UNITY_WEBGL || UNITY_EDITOR`, not bare `#if !UNITY_WEBGL`.** The WO says the branch "stays for the editor and standalone". A bare `!UNITY_WEBGL` also compiles the branch out of the **Editor whenever the WebGL build target is selected**, which would silently break hero loading in editor play mode for whoever is testing the WebGL build. `UNITY_EDITOR` is in the condition for that reason; the Editor resolves through the AssetDatabase/local providers and never runs the WebGL player. The lint accepts any `#if` condition containing `!UNITY_WEBGL`, and Case 3 pins that both forms pass while the `#else` arm and unrelated symbols fail.
8. **This worktree's HEAD is `f5d39acd1` (2026-09-07), three days behind `dev`.** The WO file did not exist at this HEAD - I copied it in from the main working tree byte-identical (`md5 ed0bb21adec63cf3ff09e715640f8866`) before flipping its Status. `tools/gate_brace.py` also does not exist at this HEAD; I invoked the copy in the main working tree against worktree paths. The lead should merge by explicit path onto `dev` and re-verify the two edited files still match there.

---

## 5. What the lead must do

1. Register the suite in `Assets/Editor/Regression/DataRegression.cs`:
   **`DeNelle.Editor.Regression.HeroAssetLoaderWebGlRegression.Run`** (markers `HERO_WEBGL_LOAD_OK` / `HERO_WEBGL_LOAD_FAIL`, standalone entry `RunAll`).
2. Batch-gate the combined tree (`COMPILE_GATE_OK`, then `REGRESSION_OK <n>/<n>` on a fresh log - the count goes up by one).
3. Stage the new `.cs` **together with the `.meta` Unity generates for it on import** - `Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs.meta` does not exist yet, because nothing has imported the file. A `.cs` committed without its `.meta` gives the next clone a fresh GUID.
4. Commit the four files (+ the new `.meta`) plus the WO Status flip and a regenerated `BOARD.html` in one commit.
5. Route the `HeroTextureLoader` residual (item 4.3) and the companion residual (item 4.4) as their own tickets, and put the warm-failure policy question (item 4.5) to the owner.
