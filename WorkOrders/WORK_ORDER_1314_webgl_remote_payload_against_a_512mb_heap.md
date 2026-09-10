> **PARKED 2026-09-02 by owner ruling — the Android APK is the priority. Pi work resumes on her word.**

# WORK ORDER 1314 — The WebGL remote payload is shaped for a native client, against a 512 MB heap

**Status:** READY TO IMPLEMENT - PARKED (owner 2026-09-09: measure in Pi Browser on the Seeker first)

### OWNER RULING 2026-09-09 (recorded by the CLI lead)
Both WO-1484 and WO-1314 need measured over the Seeker in Pi Browser to verify. The 2026-09-09 desktop/controlled-run retractions in docs/READY_RCA_2026-09-09.md do not close them. A measurement session must run the game in Pi Browser ON THE SEEKER (not desktop, not headless).
**Silo:** Web / Content
**Minted:** 2026-09-02 (CLI) while answering the owner's question about Pi breaking on the CDN.
**Severity:** P2 pending proof — see "What is NOT proven" before acting on it.

## Owner question, verbatim

> *"see why it breaks whenever it touches the cdn"* / *"im guessing match on r2"*

## What was RULED OUT — with data, so nobody re-theorises it

Her R2 hypothesis is **wrong, and that is good news**:

- `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=228` on a fresh log
  (`Builds/r2-parity.log`, 2026-09-02 03:34).
- The live `ServerData/WebGL` catalog (`catalog_2026.08.30.347462.bin`/`.hash`) and its bundles
  return **HTTP 200** from `pub-ab6dfaf1b3d74ca78891876611ccb832.r2.dev`.
- R2 serves **`Access-Control-Allow-Origin: *`** and `Access-Control-Expose-Headers:
  Content-Length,ETag`. A browser cross-origin fetch is permitted — **CORS is not the problem.**
- `catalog.json` 404s on all three targets. That is **EXPECTED**, not a defect: real catalogs are
  `catalog_<version>.bin`. Do not "fix" it.

The separate Pi validation-key drift was real and is fixed under WO-1313.

## What the measurement actually shows

`ProjectSettings/ProjectSettings.asset` → **`webGLMemorySize: 512`** (MB heap).

Largest remote objects the WebGL target can be asked for:

| bundle | compressed |
|---|---|
| `enemy_models_assets_enemyfam-hollow_…` | **24.5 MB** |
| `enemy_models_assets_enemyfam-orc_…` | **18.2 MB** |
| `enemy_models_assets_enemyfam-troll_…` | **17.4 MB** |
| `enemy_models_assets_enemyfam-bosses_…` | 4.9 MB |

Total WebGL remote payload: **95 MB** (vs Android 530 MB, Windows 454 MB).

These bundles are **meshes and textures**. Their *decompressed, GPU-and-heap-resident* footprint is
several times the figures above, and it lands in a **512 MB** heap shared with the Unity runtime, the
loaded scene and everything else. On a mobile WebView — which is what Pi Browser is, typically with a
**tighter practical ceiling than desktop Chrome** — that is a plausible OOM.

⚠ **A WebGL OOM does not present as "the CDN failed".** It presents as the tab dying, a black canvas,
or an abort deep in the loader — which is exactly the shape of *"breaks whenever it touches the CDN"*,
because touching the CDN is when the memory actually gets allocated.

## ⚠ A change landed TONIGHT that increases this pressure — read this before measuring a baseline

**WO-1307 (committed `95b75cf75`) made the `hollow` family pre-fetch for the first time.** Its models
resolve as `Skeleton_*`, so the old heuristic yielded the undeclared label `enemyfam-skeleton` and the
pre-fetch silently never happened. It now correctly resolves to `enemyfam-hollow` — meaning the WebGL
client will now pull a **24.5 MB bundle it previously never requested.**

That fix is correct and should stay. But it means **a WebGL memory measurement taken before tonight is
not a valid baseline**, and if Pi got worse after this build, this is the first thing to look at.

## What is NOT proven — do not skip this

**No Pi Browser log has been captured.** Everything above is measured from the repo and the CDN; the
OOM itself is a HYPOTHESIS, and CLAUDE.md sec.12 forbids fixing on one. Two static theories were
already wrong on 2026-08-20 before one device log named the real cause in a single line.

**Instrument first.** The realistic capture paths, cheapest first:
1. Load the deployed build in **desktop Chrome with a mobile emulation profile** and read the console
   plus `performance.memory`; the loader's own error surface is already owned by the WO-678 block.
2. **`chrome://inspect`** against Pi Browser on a USB-attached Android device — this is the real
   evidence, and it is the one that settles it.
3. Add a `[Flow:WebContent]` breadcrumb around each remote bundle load (size, elapsed, success) so a
   failure names the object rather than the platform.

## Candidate remedies, IF the data supports them — ranked, none to be applied blind

1. **Raise `webGLMemorySize`** — cheapest, and may simply be wrong-headed on a phone that does not
   have the memory to give.
2. **Split `enemy_models` per-enemy rather than per-family** on the WebGL target, so the client pulls
   only what a wave actually needs. Matches the existing per-family ruling in spirit.
3. **A WebGL-specific texture cap / crunch** on enemy models. Note the deck cards already inherit a
   512px WebGL override — there is precedent for a platform override here.
4. **Do not** solve this by reverting WO-1307. That would restore a silent defect on every platform to
   mask a memory limit on one.

## What NOT to touch

- ⛔ Do NOT change `Assets/AddressableAssetsData/**` casually. ANY change there re-hashes every bundle
  on every platform and mandates a fresh `tools\r2-ship.ps1` push (CLAUDE.md sec.16, four incidents).
  If a grouping change is the answer, the push is part of the same work order, not a follow-up.
- ⛔ Do NOT "fix" the `catalog.json` 404. It is expected.
- ⛔ Do NOT touch `Assets/WebGLTemplates/Pi/validation-key.txt` — corrected under WO-1313.
- ⛔ Do not conflate this with WO-1312 (Pi landscape). Different failure, different evidence.

---

# UPDATE 2026-09-02 06:10 — this is now the LEADING candidate, by elimination

Three of the four candidate causes for the owner's *"breaks whenever it touches the cdn"* have been
ruled out **with measurements** tonight:

| candidate | verdict | evidence |
|---|---|---|
| R2 out of sync / wrong bytes | **RULED OUT** | `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=261`, fresh log 06:07 |
| CORS blocking the browser | **RULED OUT** | `Access-Control-Allow-Origin: *`; `R2_CORS_OK public GET/HEAD enabled for WebGL CDN assets` |
| the Pi validation key | **RULED OUT** | prod serves `79ec2d03...` HTTP 200, and all four in-repo copies now match (WO-1313) |
| the landscape gate blocking the validator | **RULED OUT** | production runs a PRE-GATE template (7,396 bytes, no `pi-landscape-gate`) — see WO-1312's correction |
| **WebGL memory shape** | **STILL OPEN — now the leading candidate** | `webGLMemorySize: 512` vs a 95 MB remote payload, single bundles at 24.5 / 18.2 / 17.4 MB |

⚠ **Elimination raises a hypothesis's rank; it does not promote it to a diagnosis.** CLAUDE.md sec.12
still applies, and this ticket still forbids a fix without a capture. On 2026-08-20 two static
theories were wrong before one device log named the cause in a single line.

**Also newly relevant:** the WebGL content that was live until tonight was built from the WRONG
PLATFORM (WO-1315 — `ServerData/WebGL` went from 61 files on an Aug 30 catalog to 112 on the 09-01
one once the target bug was fixed). So the web build the owner was testing against was **missing
roughly half its content**, and any "it breaks on the CDN" observation taken before 2026-09-02 06:00
was taken against a materially different, broken payload. **Re-observe before measuring memory** —
the symptom may have changed or gone.

---

> # ⛔ RETRACTED 2026-09-03, WITHIN HOURS, BY THE OWNER. READ THIS BEFORE THE SECTION BELOW.
>
> The "PROVEN" section that follows concluded the deterministic 64% stall was an **iOS webview memory
> ceiling** exhausted by the 165 MB payload. **That conclusion is WRONG.**
>
> **The owner opened the same deployed build in a NORMAL DESKTOP BROWSER and it failed at the SAME
> PERCENTAGE.** Her words: *"it's failing in the [normal] browser at the same percentage which tells
> me that the web UI build is broken not just broken inside Pi."*
>
> A desktop browser has gigabytes of headroom. If the stall is identical there, **a per-process memory
> limit cannot be the mechanism.** The payload is still oversized and still worth reducing — that part
> of this ticket stands — but it is NOT the root of PROD-022.
>
> ⚠ **THE REAL SIGNATURE WAS ALREADY IN HER SCREENSHOT AND I READ PAST IT:**
> `TypeError: undefined is not an object (evaluating 't.subarray')` inside `<hash>.loader.js`.
> `t.subarray` on `undefined` is a **decompression / fetch failure** — the loader asked for bytes and
> got something that is not a typed array. That is a broken BUILD or a broken SERVE. It is not, and
> never looked like, an out-of-memory kill.
>
> **HOW THE ERROR WAS MADE, because the pattern is the point:** determinism was correctly identified
> as the key fact, and then attached to the WRONG mechanism. "Exactly 64% every time" does rule out a
> memory ceiling *as drift* — but instead of following that to "a specific instruction fails on a
> specific byte", it was used to argue for a *hard* memory limit, which the desktop test then
> refuted in one move. **The cheapest disproof — load it in another browser — was available the whole
> time and was never run, because the theory already felt explanatory.**
>
> Everything the overnight lane KILLED remains killed (Addressables/structure-art, R2/CDN, bfcache,
> navigated-away, teardown-during-boot). Only the positive conclusion is withdrawn.
>
> Investigation reopened under the real question: **why is the WebGL build broken in every browser?**
>
> ---
>
> # ⭐ AND THE RETRACTION ITSELF OVER-CORRECTED — resolved 2026-09-03, same day
>
> Minutes later the owner supplied the measurement that reconciles everything:
>
> > *"the build does load on the PC but it sticks at 65% for a while so something big is happening —
> > but when I tried it on my phone, it flat out denied it and crashed."*
>
> **THE BUILD IS NOT BROKEN.** It loads and completes on a desktop. So the "broken build" reading
> that replaced the memory theory is ALSO wrong — a broken build does not finish loading anywhere.
>
> **The payload IS the cause. The mechanism is what I got wrong, twice.** Not a hard ceiling hit
> instantly, and not corruption:
>
> | | what happens at 65% |
> |---|---|
> | **Desktop** | 165 MB of Brotli downloads and decompresses. Has the RAM and the patience. Stalls VISIBLY, then finishes. |
> | **iPhone webview** | Does not survive the same stretch. The content process is reclaimed mid-decompression. |
>
> This reconciles every prior measurement instead of discarding them:
> - Worker and main thread stopping in the SAME instant with `mAgeMs` 450-500 ms to the last line =
>   **reclaimed from outside while healthy**. Not wedged, not crashed.
> - Unity heap flat at `mem=247MB` = the cost is in decompression buffers and wasm memory, **outside
>   the managed heap**, exactly where `mem=` cannot see it.
> - The `t.subarray` screenshot is a **SEPARATE artifact** — her phone held a cached `index.html`
>   pointing at build files a rollback had removed (404 -> undefined -> subarray). Do not conflate it
>   with the 65% stall.
>
> ⭐ **THE LESSON, and it is the expensive one:** the same evidence supported three different
> mechanisms in one day — hard ceiling, broken build, and heavy-but-survivable payload — and the CLI
> committed to each in turn. Determinism, a desktop failure, and a desktop success each looked
> decisive in isolation. **What settled it was not a better theory but a fuller measurement: the
> owner reporting what happens on BOTH platforms in the SAME sentence.** A single-platform
> observation could not have distinguished these, and three were made before anyone asked for both.
>
> **THE FIX IS UNCHANGED AND IS THIS TICKET'S ORIGINAL SCOPE: make the payload smaller.** Cheapest
> first — **WO-1315** (whether a WebGL build ever shipped Windows-shaped content) is free weight if
> real. Then measure the top contributors by category before optimising anything.

## ⛔ SUPERSEDED — the section below is preserved unrewritten (CLAUDE.md §15) and its CONCLUSION is retracted above

## ⭐ PROVEN 2026-09-03 — THIS IS THE ROOT OF PROD-022, AND THE PROOF IS DETERMINISTIC

This ticket was minted "P2 pending proof". The proof arrived. **Promote it: it is the prime root of
the Pi crash loop, not a side issue.**

### The owner's measurement, and it is the one that cracks it

> *"64% 3 times in a row. Resets exactly 64%"* — then, after a full rollback to the previous
> deployment and with every tunable cleared: *"Still 64%"*.

**DETERMINISM IS THE WHOLE FINDING.** Every prior theory on PROD-022 predicted VARIANCE - sessions
died at 8s, 30s, 53s, 283s, which is what kept a memory *ceiling* (something you drift into) as the
leading candidate for weeks. A stall at *exactly* the same percentage, three times, across two
different deployments, is the opposite: **the same allocation failing at the same point in the same
file, every time.**

### The payload, measured from the open internet

```
Build/e1e3fcce12ebd0ccca5a2cf9a6c72035.data.unityweb   Content-Length: 165,180,640   Content-Encoding: br
Build/80780cf875d616d984528ffe257eeee3.wasm.unityweb   Content-Length:  16,951,877   Content-Encoding: br
Build/64177f0b519d6f04938617b0aba7ce74.framework.js.unityweb        93,143           Content-Encoding: br
Build/5466bb137be1f061b5b050cb15c0787e.loader.js                    (200)            Content-Encoding: br
```

⛔ **165 MB is the COMPRESSED size of the .data file.** It decompresses to several hundred MB, and
that has to be materialised in the webview's address space *alongside* the Unity heap. All four files
serve 200 with correct `Content-Encoding: br` - **the encoding is fine, the SIZE is not.**

### It retro-explains every measurement on PROD-022

| Observation | Why a too-large payload explains it |
|---|---|
| Worker + main thread stop in the SAME instant, `mAgeMs` 450-500 ms to the last line | iOS reclaims the whole content process. The main thread was healthy - it was killed, not wedged. |
| Unity heap flat at `mem=247MB` right up to death, no error, no exception | The memory is OUTSIDE the managed heap - decompression buffers and wasm memory - exactly where `mem=` cannot see it. |
| Desktop Chrome ran the identical build for 62 minutes | No comparable per-process constraint. |
| NO `JetsamEvent` on 2026-09-02 despite 10+ deaths | A content-process reclaim is not a full-app jetsam. The owner's Analytics check was a TRUE negative that was misread as exonerating memory. |
| `[Title]`-scoped lines only; dies before structure art is requested | It never survives load, so the Addressables policy was never implicated. |

### What this KILLS - do not spend another night on these

- **Memory *ceiling* as a drift/race.** It is a hard, reproducible allocation failure.
- **The Addressables / structure-art streaming theory.** `pi.disableRemoteStructureArt` was armed and
  then cleared; **64% both ways.** Knobs 1/2/4/5/6 are exonerated exactly as predicted in advance.
- **The R2 / CDN theory** (already ruled out above, and re-confirmed: all four Build files 200).
- **The deploy-target and stale-edge-shell theories.** Real defects, both recorded, but NOT this.

### What is still NOT proven

The exact allocation that fails. "Too big" is proven; *which* limit is hit - decompression buffer,
wasm `INITIAL_MEMORY`, total process footprint - is not. **Do not pick one and start optimising.**
The next step is to measure the decompressed `.data` size and the build's memory settings, then
reduce the largest contributor. This is REAL WORK - a payload reduction, not a flag flip, and no
tunable on the rail can move it.

**Related and still READY:** WO-1315 (WebGL built Windows content) - if a WebGL build has ever
shipped desktop-shaped assets, that is a direct contributor to this file's size and should be checked
FIRST, because it would be the cheapest win available.

---

# ADDENDUM 2026-09-03 (second pass) — THE PAYLOAD IS MEASURED. Top win = 40.2 MB in TWO FILES.

**Who:** investigation agent, read-only. Nothing built, deployed, or committed. No Unity batchmode run.

## 0. First, the two corrections this pass forces

**(a) The "PROVEN" retraction above was RIGHT to fire but OVER-CORRECTED.** The owner then reported:
*"the build does load on the PC but it sticks at 65% for a while — but on my phone it flat out denied
it and crashed."* A build that **completes** on desktop is not a broken build. So:

- RETRACTED and STAYS RETRACTED: "a hard iOS webview memory ceiling is the PROVEN root."
- REINSTATED as the live hypothesis: the **oversized payload** is the mechanism. Desktop has the RAM
  and the patience for the download+decompress stretch at 65%; the iPhone content process is reclaimed
  part-way through it. Consistent with the heartbeat evidence (worker + main thread stopping in the
  same instant, `mAgeMs` 450-500 ms = reclaimed while healthy, not wedged).
- The `t.subarray` screenshot is a **SEPARATE artifact** and must not be conflated with the 65% stall.
  Proven below: it is a stale cached `index.html` pointing at build hashes the rollback removed.

**(b) Serve/brotli theories are DEAD. Measured, not reasoned:**

| Check | Measurement | Verdict |
|---|---|---|
| Live `index.html` to `Build/*` hashes | index names `5466bb13....loader.js`, `e1e3fcce....data`, `64177f0b....framework`, `80780cf8....wasm`; all four return **HTTP 200** | **self-consistent pair. NOT mismatched.** |
| Brotli actually decodes? | Fetched `64177f0b....framework.js.unityweb`, `brotli.decompress` gives **608,082 bytes**, begins `var unityFramework = (() => {` | **valid brotli, valid content** |
| Unity marker present? | First bytes `6b 8d 00` then `UnityWeb Compressed Content (brotli)` | correct Unity brotli stream |
| `Content-Encoding` | `br` on all four `/Build/*` responses, per `vercel.json` | **serve config is correct** |
| Local `.data` decompresses? | 165,180,012 gives **209,582,571 bytes**, parses as `UnityWebData1.0` with an intact 8-entry table | **payload is NOT corrupt** |

**Where `t.subarray` lives — read at source, not guessed.** Offset 116413 of the live `loader.js` is
`e.decode(t.subarray(0,i.length))` inside the `UnityWebData1.0` header parser, where `t` is the
resolved `dataUrl` promise. `t` being `undefined` means **the data fetch resolved with nothing** — a
404. That is the stale-shell artifact, not a decompression fault.

**The build is not broken and the edge is not misserving it. Close both lanes.**

## 1. What is actually inside the 165 MB — ground truth, two independent sources

**Source A — the `.data` container's own file table** (parsed from the decompressed bytes):

```
total decompressed        209,582,571
    131,144,936  data.unity3d
     50,954,842  resources.resource
     24,213,084  Il2CppData/Metadata/global-metadata.dat
      1,631,152  Resources/unity default resources
      1,574,906  sharedassets0.resource
```

**Source B — Unity's own build report**, `Builds/webgl-build.log:17338`:

```
Textures      98.9 mb  33.5%      Sounds        48.6 mb  16.5%
Meshes        68.7 mb  23.3%      Shaders       12.8 mb   4.3%
Animations    11.5 mb   3.9%      Other Assets  53.0 mb  18.0%
Total User Assets 295.1 mb        Complete build size 196.3 mb
```

### Top source folders (9,000 rows aggregated from the same report)

| MB | files | folder |
|---:|---:|---|
| **47.4** | 88 | `Assets/Resources/Heroes` |
| **44.1** | 20 | `Assets/Audio/Resources` |
| **43.0** | 617 | `Assets/Resources/RpgUi` |
| 30.4 | 759 | `Assets/Spells Pack/Particles` |
| **9.5** | 482 | `Packages/com.unity.ai.inference` |
| 9.4 | 69 | `Assets/Resources/UI` |
| 8.7 | 45 | URP Shaders |

## 2. THE SINGLE LARGEST WIN — 40.2 MB in TWO FILES, with a near-controlled experiment

The two largest assets **in the entire build** are hero models:

```
26.90 MB  Assets/Resources/Heroes/Ranger.fbx     <- #1 asset in the build, 13.7% of user assets
13.30 MB  Assets/Resources/Heroes/Mage.fbx       <- #2 asset, 6.8%
 1.30 MB  Assets/Resources/Heroes/KnightV3.fbx   <- a fully-featured shipping hero
```

**The controlled pair — this is the proving measurement.** `Mage.fbx` and `KnightV3.fbx` are within
**4 KB of each other on disk** (8,565,536 vs 8,561,776 bytes — same vendor rig), yet differ **10.2x**
in the build. The only differing importer flag:

| file | source bytes | `meshCompression` | build size | ratio |
|---|---:|---:|---:|---:|
| `KnightV3.fbx` | 8,561,776 | **1** (on) | 1.30 MB | 0.15x |
| `Mage.fbx` | 8,565,536 | **0** (OFF) | 13.30 MB | 1.55x |
| `Ranger.fbx` | 13,946,976 | **0** (OFF) | 26.90 MB | 1.93x |
| `Knight.fbx` / `knightV2.fbx` | — | **1** (on) | 0.27 / 0.69 MB | — |

Every Knight variant ships with mesh compression **on**; Ranger and Mage are the only two heroes with
it **off**, and they are the only two heroes that are enormous. All five also carry `isReadable: 1`
(Read/Write Enabled keeps a second CPU-side copy) and `optimizeGameObjects: 0`.

**Projected saving** at KnightV3's measured 0.152 ratio: Ranger 26.9 to ~2.1 MB, Mage 13.3 to ~1.3 MB
= **~36.8 MB off 293.8 MB of user assets (12.5%)**.

**HONEST CAVEAT, and the reason this is written as a next action and not a conclusion:**
`meshCompression` is a **global ModelImporter setting with no per-platform override**, so it also
affects the Android/Seeker build. It *shrinks* the APK and introduces no gameplay change, and it is
already the project's norm for the Knights — but it is not WebGL-only, so it needs the owner's word.
**The disproving test is cheap and must be run before anyone claims this:** set `meshCompression: 1`
on those two `.fbx.meta`, reimport, rebuild WebGL, read the new build report. Numbers, not theory.

## 3. Second win — 9.5 MB of a package NOTHING references

`Packages/manifest.json:8` pulls **`com.unity.ai.inference` 2.6.1** (Sentis). It contributes
**9.5 MB across 482 files**, including the **6.50 MB `ConvGeneric.compute`** — the **#3 largest asset
in the whole build**, an ML convolution kernel in a tower-defense RPG.

Measured references: **0** C# files (`grep -rl "Unity.Sentis|Unity.InferenceEngine"` over `Assets/`
returns 0), **0** `.asmdef` references, and `packages-lock.json` lists it as a direct entry, not a
transitive dependency of anything. It ships because its `Resources/` folder is unconditionally
included.

**This is the cleanest win available: 9.5 MB, zero quality cost, zero gameplay risk, all platforms.**

## 4. What is ALREADY DONE — do not spend a session re-doing it

**The WebGL texture pass HAS been run, and it is TIGHTER than Android's.** Parsed all 7,162 texture
metas:

- **7,054 of 7,162 carry `buildTarget: WebGL` with `overridden: 1`** — 5,604 capped at **512 px**,
  1,211 at 128 px, all compressed.
- Android by comparison: only **2,195 of 7,105** overridden, and mostly at 1024 px.

Textures are still the largest *category* (98.9 MB) but that is 7,000+ already-shrunk files, not a
missed pass. **That lever is spent. Report it closed.**

## 5. WO-1315 — is it real? YES, but it is NOT a payload lever, and it is already DONE

`WORK_ORDER_1315` is **Status: DONE** with a shipped regression
(`ContentBuildTargetRegression.cs`, `CONTENT_BUILD_TARGET_OK`). It was real: a WebGL build emitted
`ADDRESSABLES_CONTENT_OK ... target=StandaloneWindows64`, and fixing it took `ServerData/WebGL` from
61 files to 112.

**But it does not shrink the `.data` — it works the opposite way.** It governs which *remote R2
catalog* the build resolves. Its fix **added** correct remote content; none of that content is inside
the 165 MB. **WO-1315 is closed and is not a lever here. Do not reopen it for size.**

## 6. Remaining levers, measured, cheapest last

- **Audio — 48.6 MB (16.5%).** Only **36 of 132** audio assets carry a WebGL override; **96 files
  (48.1 MB of source) inherit defaults**. The 36 that are overridden use `loadType: 0`
  (DecompressOnLoad), `compressionFormat: 7` (AAC), `quality: 0.3`, **stereo at 44,100 Hz**
  (`forceToMono: 0`). Audio importer settings **are per-platform** — mono + 22,050 Hz on the WebGL
  override alone is Android-safe and plausibly halves this. `Assets/Audio/Resources` is **44.1 MB in
  20 music files**, all in a `Resources/` folder, so all 20 ship in the first load whether or not the
  track is ever played.
- **`Resources/` is the structural problem.** Everything under any `Resources/` folder is included
  unconditionally. `Assets/Resources` is **478 MB on disk**. All 47.4 MB of `Resources/Heroes` — every
  class — downloads before the player picks one. The project already has a working Addressables/R2
  remote path for enemies and structures; **heroes and music are the obvious next tenants**, and that
  moves weight out of first load entirely rather than compressing it.

## 7. Verdict + the single next action

- **Is the build broken? NO.** It decompresses, parses, and completes on desktop.
- **Is the serve broken? NO.** Headers, brotli, and the index/Build hash pair all verified correct.
- **Surviving hypothesis:** the **payload size** is the mechanism; iOS loses the content process during
  the 65% download+decompress stretch that desktop merely endures.
- **WO-1314's "PROVEN" banner: keep it RETRACTED** (a hard heap ceiling is disproven by the desktop run
  completing) **but reinstate the payload as the live cause.**

**SINGLE NEXT ACTION — one Unity session, three edits, one rebuild, read the report:**
1. `meshCompression: 1` on `Ranger.fbx.meta` + `Mage.fbx.meta` (owner sign-off: touches Android too).
2. Remove `com.unity.ai.inference` from `Packages/manifest.json`.
3. Rebuild WebGL (**never `-DevBuild`**) and diff the new `Build Report` against
   `Textures 98.9 / Meshes 68.7 / Sounds 48.6 / Total 295.1 mb`.

Expected combined: **~46 MB off 295.1 MB (~15.6%)**, taking the compressed `.data` from 165 MB toward
~135 MB. That is a *projection from a measured ratio*, not a result. It is not proven until the new
build report is read.

After ANY rebuild, run `tools\r2-ship.ps1` — the build regenerates the catalog named after
`bundleVersion` and the new one 404s until pushed (CLAUDE.md section 16).

## 8. Method note — how these numbers were obtained (reproducible, no Unity)

- `.data.unityweb` streamed through `brotli.Decompressor`, then its `UnityWebData1.0` header table
  parsed directly (`u32` offset/size/pathLength triples) for the container breakdown.
- `Builds/webgl-build.log` "Used Assets and files from the Resources folder" section parsed —
  **9,000 rows, 293.8 MB** — and aggregated by folder and by extension.
- All 7,162 texture `.meta` files parsed for per-`buildTarget` `overridden` / `maxTextureSize` /
  `textureCompression`.
- Live HTTP checks against `echoes-of-elarion.vercel.app` with explicit `Accept-Encoding`.

---
## RCA re-verified 2026-09-04 (QA read-only pass)
**Verdict:** SUPERSEDED
**Evidence:**
- This WO `:1` reads `PARKED 2026-09-02 by owner ruling`; `:5` carries the RETRACTED note; `:125-195` hold the retraction and the same-day over-correction.
- Its s7 "SINGLE NEXT ACTION" is already executed: `5163f425c 2026-09-03 perf(webgl): mesh compression on two heroes + drop unused Sentis - measured, and the projection did NOT hold` (.data 165,180,012 -> 157,461,237; Ranger 26.9 -> 13.7 MB; Mage 13.3 -> 6.8 MB; Sentis -9.5 MB; projected 46 MB, delivered 29.2 MB).
- Tree confirms: `Packages/manifest.json` 0 hits for `inference`; `Assets/HeroContent/Ranger.fbx.meta:37 meshCompression: 1`, `Assets/HeroContent/Mage.fbx.meta:37 meshCompression: 1`. The cited `Assets/Resources/Heroes/*.fbx.meta` path no longer exists (moved by `d706b430b` 2026-09-03, WO-1187; pinned by `HeroRemoteContentRegression.cs:1-13`).
- `Builds/webgl-build.log:16810-16811` (Sep 3): `Total User Assets 248.3 mb / Complete build size 177.2 mb` (WO cites 295.1 / 196.3).
- `ProjectSettings/ProjectSettings.asset:834 webGLMemorySize: 512` unchanged - remedy #1 was never applied and no data ever supported it.
- PROD-022: `WORK_ORDER_PROD-022_pi_browser_crash_loop.md:3 **Status:** DONE`; its RESULT (from `d24d4210d` 2026-09-04) says instrumentation deployed, the >10-min survival acceptance UNMET pending owner felt-test. PROD-022 never mentions 1314. WO-1315 and WO-1324 are DONE. No 1314 RESULT file.
**What changed since the RCA:** both levers this ticket named were applied and measured (`5163f425c`); heroes moved to R2 (`d706b430b`); PROD-022 instrumentation shipped and awaits a device measurement.
**Ready for a lane?** no - the actionable scope is done and the ticket is PARKED by owner ruling; the one open thread (does the phone now survive load) is a PROD-022 felt-test, not code. Files a lane would touch: none.
**Pins/rulings needed:** owner: close 1314 as DONE-by-`5163f425c` or unpark the Pi lane; the s6 audio item (48.6 MB, 96 files without a WebGL override) would be a NEW WO if she wants more reduction.
