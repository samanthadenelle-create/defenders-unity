# WO-1775 — Device scenario harness: scripted scenarios + scrcpy/logcat capture, cheapest-first

**Status:** READY FOR LEAD REVIEW
**Lane:** Tooling / QA harness (no gameplay files; §9-disjoint from every combat/world lane)
**Silo:** device-qa
**Opened:** 2026-09-16
**Author:** read-only spec lane (no code, no Unity, no device touched while writing this)
**Owner ask, verbatim (2026-09-16):** *"can we use some of those tools to better test scenarios, like scrcpy"*

**Scope of the READY label.** The capture harness (`tools/device-scenario.ps1`, the SKILL.md section) is
READY. The scenario intent is READY too — the dev-kit seam it hangs on was found and read at source for
**every** v1 parameter: `newgame` (§1.6.1), `onboarded` (§1.6.2), `level` (§1.3), `wave` (§1.4),
`troops`/`buildings`/`raid`/`town` (§1.1), `ff.*`/`tun.*` (§1.1). Exactly **one** parameter stays SPEC
with its question open: `camera=` (§8 Q2) — implement v1 without it. §10 lists every claim in this spec
that is inferred rather than measured; read it before writing code.

---

## 0. WHY — the three costs this removes

Measured today, not inferred:

1. **WO-1773** (Lv 52 hero at wave 176 taking no damage) arrived as an **external tester's video**. No
   seat can reproduce it: nothing in any build this repo ships can put a hero at Lv 52 **or** a town at
   wave 176 from a script. See §1.6 — the gap is real and this WO closes it.
2. The owner's Bastion raid needed **a manual logcat pull** to settle WO-1764/1765/1767/1768. Every
   proving line in those four RESULTs is dated to build `2026.09.16.371701` and came off one hand-driven
   capture (`WORK_ORDER_1764_….RESULT.md:71`).
3. **Every felt-test today needs the owner playing and plugging the phone in.** That is the exact thing
   CLAUDE.md §14 exists to never rely on (*"the owner is NEVER the bug detector"*).

`scrcpy 4.1` (installed today) removes cost 2 and part of 3. It does **not** remove cost 1 — a mirror
records a scenario, it cannot *set one up*. The setup half is §1.

---

## 1. SCENARIO ENTRY — what the build can already do, and the one addition

### 1.1 The dev kit as it exists (every path below opened at source 2026-09-16)

| Surface | File | Reachable in which build | What it can set |
|---|---|---|---|
| **AdminOverlay** (the LIVE dev panel; Settings → DevTools, or chord Ctrl+Shift+A) | `Assets/_Modules/HUD/AdminOverlay.cs` | outer guard `#if DEVELOPMENT_BUILD \|\| UNITY_EDITOR \|\| TESTER_BUILD` (`:259`) | see 1.2 |
| **DevSkipKit** | `Assets/_Modules/Village/World/Camps/DevSkipKit.cs` | same guard (file-level `#if`, line 1) | `PrepCastlePower`, `GrantCapturedTownAndEnter`, `EnterIronBastionRaid`, `MaxBuildingTiers`, `MaxTroopTypes` |
| **DevPanelController** (F10) | `Assets/_Modules/DevTools/DevPanelController.cs` | `#if DEVELOPMENT_BUILD \|\| UNITY_EDITOR` **and activation gated OFF** — header `:1-7` reads *"DEPRECATED (owner 2026-06-24) … TAGGED FOR REMOVAL"*; hotkey needs `PlayerPrefs ff.devhotkeys=1` (`:265`) | ⚠ **treat as unavailable** — see 1.4 |
| **Remote tunables** | `Assets/_Modules/Core/Ops/RemoteTunables.cs` | all builds | any registered knob; **PlayerPrefs `ff.tun.<key>` wins last** (`:1764-1770`, `LocalPrefix = "ff.tun."` at `:131`) |
| **Feature flags** | `Assets/_Modules/Core/FeatureFlags.cs` | all builds | `ff.*` ints, read live from PlayerPrefs on every `Get` (AdminOverlay `:725-726` states this contract) |

### 1.2 What AdminOverlay grants, split by guard — **this split is the whole constraint**

`AdminOverlay.cs:259-297` carries a **deliberate two-guard structure** with a 12-line comment
(WO-1512) saying why. Do not collapse it.

- **Outer guard admits `TESTER_BUILD`** → these compile into the Firebase tester APK:
  `Set Level 15 (+skill pts)`, `MAX all buildings`, `MAX troop types`,
  `Grant Iron Bastion town`, `Raid: Iron Bastion` (`:281-287`), plus `Trigger next wave`,
  the queue time-skip, the FLAG chip, wallet reset, the orient tool.
- **Inner guard drops `TESTER_BUILD`** (`#if DEVELOPMENT_BUILD || UNITY_EDITOR`, `:273-280` and
  `:288-297`) → **never in a tester APK**: `Load resources (full base)`, `Set Level 5/10`,
  `+25/+100 Wisdom`, and the `OnGiveCrystals` handler (`:838-853`).
  The reason, verbatim from `:262-268`: *"A tester who can mint 50,000 of every resource makes the
  economy unmeasurable and the purchase funnel meaningless."*

**Implication for this WO: hero level and resources are value-minting by an owner ruling that is still
live. A scenario that sets `level=52` or `wood=50000` may NOT ride the TESTER_BUILD APK.** §1.5 gives it
its own variant instead of widening that guard.

### 1.3 Hero level — the seam exists and takes any target

`AdminOverlay.OnSetHeroLevel(int target)` (`:1026-1085`) reaches `DeNelle.Village.HeroProgression` by
reflection and loops `AddXp(XpToNext + 1)` until `Level >= target`, guard `< 500` iterations
(`:1057-1059`). **It is not capped at 15 — only the BUTTONS are.** `target=52` works through the same
real levelling path (each crossing runs `ApplyLevelRewards`, banking Wisdom + a skill point), and it
persists: `GameState.HeroLevel` (`Assets/_Modules/Core/State/GameState.cs:429`), on the wire as
`heroLevel` (`SaveSchema.cs:492`, since v29). **No save change needed.**

### 1.4 Wave number — **a save-backed seam exists; `WaveManager` needs NO change**

The advisor's expectation and the old dev panel both said there is no wave jump. Read at source, that is
half wrong and the half that matters is available:

- `DevPanelController.JumpToWave` (`:1420-1436`) **does not jump** — its own comment says so and it
  calls `manager.BeginLoop()`, i.e. a restart. Its status text even asks an integrator to
  *"Wire WaveManager.JumpToWave"*. **That method has never existed. Do not go looking for it** (same
  class of trap as `RaidHeroSpawner`, CLAUDE.md §8).
- The **real** seam is the resume seed. `WaveManager.ResolveStartWave()`
  (`Assets/_Modules/Village/Waves/WaveManager.cs:1436-1445`) returns
  `Mathf.Max(_startWave, s_resumeWaveId)`, and when `s_resumeWaveId <= 0` it seeds it from
  **`GameState.BestWave + 1`** (`:1438-1443`).
- `GameStateService.RecordRun(waveReached)` (`:1119`) does `_state.BestWave = Mathf.Max(_state.BestWave,
  waveReached)` — monotonic, already the public writer `WaveManager` itself uses at `:3467`.
- `BestWave` is `public int` on `GameState` (`:40`), on the wire as `bestWave` (`SaveSchema.cs:254`),
  floored at 0 by `NonNegInt` (`:855`) with **no upper clamp** — a fact the code states for the endless
  case at `WaveManager.cs:3470-3472`.

**⇒ `RecordRun(175)` + `Save()` ⇒ the loop starts at wave 176.** `RecordRun` is
`_state.BestWave = Mathf.Max(…); WaveRecorded.Invoke(); Save();` (`GameStateService.cs:1117-1122`) —
**it mints nothing**, it only raises the `WaveRecorded` event. Fully inside the existing save model: no
schema bump, no new field, no `WaveManager` edit.

> ### ⚠ TIMING IS THE WHOLE TRICK — apply `wave=` BEFORE the first `BeginLoop`.
> `s_resumeWaveId` is re-seeded **only when `<= 0`**, and it is zeroed **once per play session** by
> `ResetResumeStatic`, a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` at
> `WaveManager.cs:589-594`. Its own comment at `:577-578` says the static *"survives a scene reload
> WITHIN a play session"* — so **routing through a hub reload does NOT re-seed it.** Writing `BestWave`
> after a `BeginLoop` has already resolved a start wave is a silent no-op. The scenario therefore writes
> `BestWave` in its **pre-hub phase** (§1.8), and the proof is the existing line at `:1424`:
> `BeginLoop data OK — calling EnterCountdown(startWave=176) [resume=176, dev_startWave=1]`.

### 1.5 The addition: a scenario intent, `am start --es`, on its own build variant

**Chosen mechanism: Android intent EXTRAS on the existing launcher activity. Rejected: a `dotr://`
custom scheme.**

Why, measured:
- There is **no `AndroidManifest.xml` for the game** — the only three under `Assets/Plugins/Android/`
  belong to `AdsIdentity`, `FirebaseApp` and `MobileWalletAdapter`. A custom scheme needs a new
  manifest + an `<intent-filter>`, i.e. a **ship-facing surface** change under the WO-1741 Play
  compliance regime (`WORK_ORDER_1741_play_aab_unitask_allowlist_and_jupiter_quarantine.RESULT.md`,
  Status DONE). Not worth it for a QA harness.
- **Nothing in the game reads a deep link today.** `grep -rn "deepLinkActivated"` over `Assets/`
  returns nothing; the `Application.absoluteURL` readers (`FeatureFlags.cs:1459-1471`,
  `UICaptureMode.cs`, `CurrencySkinResolver.cs:412`) are **WebGL-only** and empty off-web by their own
  comments.
- Intent extras need **zero manifest change**, and the idiom is already in this tree:
  `Assets/_Modules/Wallet/TargetedLocalAssociationScenario.cs:226` does
  `unityPlayer.GetStatic<AndroidJavaObject>("currentActivity")`.

**Gate: a NEW define `QA_SCENARIO_BUILD`, stamped ALONGSIDE `TESTER_BUILD` — `-Scenario` ⇒
`TESTER_BUILD;QA_SCENARIO_BUILD`.**

> ### ⛔ `QA_SCENARIO_BUILD` ALONE WOULD COMPILE OUT EVERY SEAM THIS KIT DISPATCHES TO.
> `DevSkipKit.cs` is **file-wrapped** in `#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD`
> (line 1) and the `DeNelle.DevTools` asmdef carries a matching define constraint
> (`DevPanelController.cs:19-23`). A QA APK stamped with `QA_SCENARIO_BUILD` and **not**
> `TESTER_BUILD` would contain **no `DevSkipKit` at all** — no `GrantCapturedTownAndEnter`, no
> `EnterIronBastionRaid`, no `MaxBuildingTiers`. The exclusion that matters is **QA-vs-store**, not
> QA-vs-tester, and that is already acceptance #14. So `-Scenario` implies `-Tester`'s define too.

Rationale for a *separate* define rather than widening `TESTER_BUILD`: (a) §1.2's ruling forbids
value-minting in the APK that goes to Firebase testers, and this kit mints — a scenario APK is
**never** uploaded to Firebase or a store; (b) `AndroidBuild.cs:141` builds with
`options = BuildOptions.None`, so **`DEVELOPMENT_BUILD` is never defined by this chain at all** — a
DEVELOPMENT_BUILD gate would compile to nothing on every APK the repo produces.
`overnight-apk-build.ps1` already forwards arbitrary defines (`-Defines`, `:46-52` →
`run-unity-method.ps1 -ExtraScriptingDefines`), so the variant costs one switch, no new build method.

**⛔ `DevScenarioIntent` calls the seams DIRECTLY — do not reach them through `AdminOverlay`.** Those
handlers are `private` instance methods on a `MonoBehaviour` and several sit under the inner
`DEVELOPMENT_BUILD || UNITY_EDITOR` guard (`AdminOverlay.cs:897-1024`), so they are unreachable from a
QA APK. `DeNelle.DevTools` already references `Core`, `Core.State`, `Village`, `HUD` and `Wallet` by the
documented tooling exception (`DevPanelController.cs:26-34`), so the intent handler calls
`HeroProgression.AddXp`, `GameStateService`, `DevSkipKit` and `SceneRouter` **without reflection**.
AdminOverlay is cited throughout this WO as the **pattern and the proof the seam works**, never as the
callee.

**File to create: `Assets/_Modules/DevTools/DevScenarioIntent.cs`** (DevTools already carries the
"tooling may reference gameplay modules" exception — `DevPanelController.cs:26-34` — and its asmdef
already has a define constraint, so add `QA_SCENARIO_BUILD` to that constraint list).

```
#if QA_SCENARIO_BUILD
// [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] → read the extras ONCE, park the parsed
// request, apply it after GameStateService.Instance is alive, then FlowTrace the whole thing.
#endif
```

Read the extras exactly like the wallet file does:

```csharp
using var player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
using var intent   = activity.Call<AndroidJavaObject>("getIntent");
// One TYPED extra per key — see the boxed rule below. No packed "k=v;k=v" string.
string newgame = intent.Call<string>("getStringExtra", "dotr.newgame");   // null when absent
int    level   = intent.Call<int>("getIntExtra", "dotr.level", 0);        // 0 = not requested
```

⚠ **`getStringExtra` returns `null` for an absent key** — every read goes through `Guard.Try` and a
null/empty `raw` is a **silent no-op with one `FlowTrace.Once`**, never a throw and never a default
scenario. A build that carries the kit but is launched normally must behave *exactly* as today.

**Command shape the wrapper emits** (force-stop first, so `onNewIntent` is never in play):

```powershell
adb -s <serial> shell am force-stop com.denellestudios.echoesofelarion
adb -s <serial> shell am start -n com.denellestudios.echoesofelarion/<launcher-activity> `
    --es dotr.newgame knight --ei dotr.onboarded 1 --ei dotr.level 52 --ei dotr.wave 176 `
    --ei dotr.troops 10 --es dotr.raid RaidBase_IronBastion
```

> ### ⛔ ONE EXTRA PER KEY. DO NOT PACK THEM INTO A `"k=v;k=v"` STRING.
> `adb shell` hands the whole command line to the **device's `sh`**, which treats `;` as a statement
> separator — a packed `--es dotr.scenario "class=knight;level=52"` splits on the device and the second
> half is executed as a shell command. Typed extras (`--es` string, `--ei` int, `--ez` bool) also mean
> the parser gets `getIntExtra`/`getBooleanExtra` and never has to parse numbers out of text. Keys are
> namespaced `dotr.*` so a stray extra from another app can never be mistaken for a scenario.

`PackageId = "com.denellestudios.echoesofelarion"` is read at `Assets/Editor/AndroidBuild.cs:49`.
⚠ **The launcher activity class is UNPROVEN.** `ProjectSettings/ProjectSettings.asset:84` reads
`androidApplicationEntry: 2`, which in Unity's enum is **GameActivity** (⇒ almost certainly
`com.unity3d.player.UnityPlayerGameActivity`), but no merged manifest exists on disk to confirm it.
**The implementer resolves it from the device, never from this doc:**
`adb shell cmd package resolve-activity --brief com.denellestudios.echoesofelarion` (last line is
`pkg/activity`). The wrapper must do that resolve at runtime and print it, so the value is never
copy-pasted state (CLAUDE.md §0/§2/§5 failure mode).

### 1.6 Parameter table — what the current save/state model can set, and what it cannot

| Param | Seam (read at source) | Save impact | Verdict |
|---|---|---|---|
| `newgame=<class>` | `GameStateService.ResetToNewGame()` — the **headless** new-game seam the fleet already uses (`AutoPilotDriver.cs:3414`, `:3550` *"a genuinely new town: ResetToNewGame through the REAL service"*). ⛔ **PERMITTED ONLY WHEN NO SAVE EXISTS** — `!PlayerPrefs.HasKey("dotr-save")` (the key is documented at `GameStateService.cs:475`). With a save present it is **REFUSED** with a `FlowTrace.Fail`. | writes a fresh save | **READY — and it is the PREREQUISITE, see below** |
| `onboarded=1` | `GameState.Onboarded = true` + `Save()` — the seam `AdminOverlay.OnSetOnboarded` uses (`:855-862`) | `Onboarded` bool, already persisted | **READY — required by (a), see below** |
| `class=<knight\|ranger\|mage…>` | folded into `newgame=<class>` above: class is chosen at hero select, and the only scriptable door is a genuine new game. **No seam swaps class on an existing save** and this WO does not add one. ⛔ Never drive the title row by coordinates (the 2026-09-10 hazard). | — | **READY on a fresh target; REFUSED on an existing save** |
| `level=N` | `AdminOverlay.OnSetHeroLevel` → `HeroProgression.AddXp` loop (`:1057`) | `GameState.HeroLevel` (`:429`), wire `heroLevel` v29 | **READY** |
| `wave=N` | `GameStateService.RecordRun(N-1)` + `Save()` + hub reload → `ResolveStartWave()` (`WaveManager.cs:1436`) | `GameState.BestWave` (`:40`), wire `bestWave`, no upper clamp | **READY** |
| `resources=wood:50000,iron:50000,…` | `DevSkipKit`/AdminOverlay `OnLoadResources` (inner-guard handler) | `Resources` struct, already persisted | **READY** (value-minting → `QA_SCENARIO_BUILD` only) |
| `buildings=max` | `DevSkipKit.MaxBuildingTiers` + `MaxPlacedStructureLevels` (`:88-115`) | `BuildingTiers` dict + `BaseLayout` rows; ceiling from `RepoProps.MaxStructureLevel` | **READY** |
| `raid=<sceneId>` | `DevSkipKit.EnterIronBastionRaid` → `SceneRouter.GoRaid(id)` (`:81-88`) | none | **READY** |
| `troops=N` | ⚠ `MaxTroopTypes` (`:117-148`) fills **to the army cap**, not to N. Needs a sibling `TrainExactly(int n)` doing the same `army.TrainNow(def.Id, TroopDialogueCommands.SlotOf, _ => true)` loop, bounded by `n`. | `ArmyStorage` (v22) | **READY — one new DevSkipKit method** |
| `town=granted` | `DevSkipKit.GrantCapturedTownAndEnter` (`:30-79`) | `OwnedBaseState` via `TryRecordPendingTownCapture`/`TryRecoverPendingTownCapture` | **READY** — but see §4(c): it **bypasses the capture census** |
| `tun.<key>=N` | `PlayerPrefs.SetInt("ff.tun." + key, N)` — `RemoteTunables.ReadLocalOverride` (`:1786-1796`), *local wins last* (`:1764`) | PlayerPrefs, not the save | **READY** |
| `ff.<key>=N` | `PlayerPrefs.SetInt("ff." + key, N)` — `FeatureFlags.Get` reads live per call | PlayerPrefs | **READY** |
| `camera=<profile>` | ⚠ not resolved. WO-1765 touched a raid over-the-shoulder profile; the knob was not located by this lane. | unknown | **SPEC — open question Q2** |

### 1.6.1 ⛔ WHY `newgame=` IS NOT OPTIONAL — a scripted target has NO SAVE

Every `DevSkipKit` entry point opens with `if (svc?.State == null) return "No game state."`
(`:19-20`, `:33-34`, and the army read at `:84`). A fresh emulator or a fresh guest user has **no
save** — a save is created only by `ResetToNewGame`, which on the real device path sits behind the
title row + hero select. **Without a bootstrap, nothing in §4 runs on a scripted target at all.**
`newgame=<class>` is that bootstrap, and `AutoPilotDriver.cs:3414` proves the service call works
headlessly with no UI.

The two rules that keep this safe, and they are the same rule from two directions:

- **`newgame=` is REFUSED whenever `PlayerPrefs.HasKey("dotr-save")` is true.** A scripted reset can
  then never destroy an existing save — including the owner's, if her device is ever wired up by
  mistake. This is a *fail-closed* guard, not a policy note.
- **`ResetToNewGame` is still not reachable as a bare verb.** There is no `reset=1`, no `wipe=`, no
  `deleteall=`. `AdminOverlay.cs:1148-1170` (the full-reset door that wiped her town on 2026-09-10) is
  never called by this kit. Any key resolving to a reset on a save-bearing device is rejected with a
  `FlowTrace.Fail`, and the regression pins that (acceptance #12).

### 1.6.2 ⛔ THE FTUE GATE BLOCKS SCENARIO (a) — `onboarded=1` IS REQUIRED

`WaveManager.Update` re-checks `TutorialFlow.WaveLoopSuppressedForTutorial` **every tick**, not just at
the door (`:1205-1225`), and memory `enemies-never-spawn-tutorial-onboarded-gate` records that the real
gate on "enemies never spawn" is `!Onboarded`. A freshly-founded town at Lv 52 with `BestWave=175`
therefore **sits in the tutorial forever** and wave 176 never arrives. So scenario (a) is
`newgame=…;onboarded=1;level=52;wave=176`, and a run that omits `onboarded` is a false negative, not a
bug in the wave loop.

### 1.6.3 ⛔ TWO PHASES — the order falls out of §1.4 and §1.6.2, so write it down

The dispatcher parses once, **parks** the request, and applies it in two phases, `FlowTrace`-ing each:

| Phase | When | Keys | Why here |
|---|---|---|---|
| **PRE-HUB** (GameState only, before the first `BeginLoop`) | as soon as `GameStateService.Instance?.State` is non-null, at Title | `newgame`, `onboarded`, `wave`, `resources`, `buildings`, `troops`, `ff.*`, `tun.*` | `wave` is a no-op after the first `BeginLoop` (§1.4). `ff.*`/`tun.*` are read live per call, so earlier is strictly safer. |
| **POST-HUB** (scene-bound objects) | after the hub scene is loaded and the hero exists | `level`, `raid`, `town` | `FindAnyObjectByType<HeroProgression>()` is **null at Title** — `AdminOverlay.OnSetHeroLevel` says so itself: *"Level: HeroProgression not in scene yet."* (`:1044`). `SceneRouter.GoRaid` needs a loaded hub to route from. |

A parked key whose phase never arrives must `FlowTrace.Warn` by name — never expire in silence.

### 1.7 ⛔ adb CANNOT write PlayerPrefs on any APK this repo builds

A PlayerPrefs-driven "scenario on boot" was considered and **rejected as unscriptable**:
`AndroidBuild.cs:141` sets `options = BuildOptions.None` ⇒ no development build ⇒
`android:debuggable=false` ⇒ `adb shell run-as <pkg>` is refused ⇒
`/data/data/<pkg>/shared_prefs/*.xml` is unreachable without root. The Seeker is not rooted (assumed;
`adb shell su -c id` is the one-line check). **Therefore the intent extra is the only adb-scriptable
door, and the intent handler is the thing that writes the PlayerPrefs keys** (`ff.*`, `ff.tun.*`) from
inside the process. Stated here so nobody re-proposes the prefs path.

---

## 2. CAPTURE — the run recipe

Ordering is load-bearing. **`adb logcat -c` before a `-d` destroys unpulled evidence**, and on the
owner's device that evidence may be the only copy of something she just saw.

```powershell
# 0. adb on PATH for the session (never assume it) — SKILL.md "Device felt-test from the PC (scrcpy)"
$env:PATH = "C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools;$env:PATH"

# 1. SAFETY GATE — overlay check (memory `device-lanes-overlay-apps-and-start-new`; see §3)
adb -s $Serial shell dumpsys window windows | Select-String "SYSTEM_ALERT|TYPE_APPLICATION_OVERLAY"
#    any hit -> STOP and report. Never disable an app on the owner's device.

# 2. ring size FIRST, then drain, and ONLY THEN clear
adb -s $Serial logcat -g                      # per-device; Seeker read 16 MiB on 2026-09-10
adb -s $Serial logcat -d > logs\device\pull-<ts>-<scenario>\pre.log
adb -s $Serial logcat -c                      # never before the -d above

# 3. launch the scenario (§1.5), then record
scrcpy -s $Serial --no-control --no-playback --no-audio --video-codec=h264 `
       --record logs\device\pull-<ts>-<scenario>\run.mp4 --time-limit <sec>

# 4. drain again + a still
adb -s $Serial logcat -d > logs\device\pull-<ts>-<scenario>\run.log
```

Screenshot: reuse **`tools/capture-seeker-screen.ps1`** — it already has the exactly-one-device guard
(`:11-14`) and pipes through `cmd.exe /d /s /c` (`:19-23`) specifically to stop Windows rewriting the
PNG bytes. **Do not re-invent it.** (From the Bash tool, `adb exec-out screencap -p > file.png` also
works; from PowerShell `>` corrupts the PNG — proven, memory `scrcpy-on-seeker-h264-no-control`.)

**Proven-today constraints to carry verbatim into the wrapper:**
- `--video-codec=h264` is load-bearing: the default codec wrote a **0-byte** mp4 three times
  (`Recording stopped before headers were processed`); h264 wrote 155 193 bytes.
- `--no-control` is mandatory for any observation lane — a mirror window forwards taps and keys.
- A static screen starves the encoder; record while something moves.
- Never leave scrcpy running: `Get-Process scrcpy` must come back empty at lane end.
- **Ring-buffer caution:** the Flow firehose can evict the boot window. `adb logcat -g` is **per
  device** (16 MiB on the Seeker 2026-09-10, 256 KiB on 2026-08-05) — run it before claiming eviction
  (memory `logcat-ring-buffer-destroys-evidence`).

### 2.1 Frame extraction — contact sheets, not frames

ffmpeg at `C:\Users\Elden\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-essentials_build\bin\ffmpeg.exe`
(directory listed present 2026-09-16).

```powershell
# one contact sheet per 30 s of run: 1 fps, 6x5 tiles, 320 px wide cells
ffmpeg -i run.mp4 -vf "fps=1,scale=320:-1,tile=6x5" -y sheet-%03d.png
# drill-down for ONE flagged second only:
ffmpeg -ss <sec> -i run.mp4 -frames:v 1 -y frame-<sec>.png
```

### 2.2 The two evidence readers

| Evidence | Reader | Rule |
|---|---|---|
| `[Flow:*]` lines | **Grep, never Read** — `Select-String` / the Grep tool over `run.log` | scope to the scenario's judging tokens (§4). Never read a multi-MB log into context. |
| Frames | the **Read** tool on `sheet-*.png` | sheets first; a single `frame-<sec>.png` only after a sheet flags a second. |

Both feed the §14 chain unchanged: `f8-device-bridge-start.ps1` already pulls the device's own
`break-log.jsonl` + flag screenshots into `logs/f8-inbox/`, and the queue discipline
(`f8-check-inbox.ps1` → triage → `f8-ack.ps1`, **one ack per capture**) is untouched by this WO.

---

## 3. TARGET DEVICE POLICY

> ### ⛔ THE OWNER'S SEEKER IS FOR HER FELT-TESTS. NO SCRIPTED TAPS, EVER.
> On 2026-09-10 a scripted device lane's session ended with her town (Thrain Lv1, Heartfire 3/3,
> buildings) reset to a fresh Grom save. A third-party recorder's overlay bubble ate a back key;
> `TitleController.OnStartNew` calls `ResetToNewGame` at `:417` **before hero select and before any
> confirm**; the local save is one PlayerPrefs slot with no backup. `adbd` never logged the tap.

Policy, three tiers:

1. **Owner's Seeker — OBSERVE ONLY.** `scrcpy --no-control`, `adb logcat -d`,
   `capture-seeker-screen.ps1`. The overlay check of §2 step 1 runs first, every time. A scenario
   *intent* is a scripted state change, so **the intent path is not used on her device** unless she
   says so for that specific run. No `am force-stop` on her device either — it can lose unsaved state.
2. **Emulator AVD — the scripted target.** ⚠ **UNPROVEN, and likely blocked as configured:**
   `C:\Users\Elden\.android\avd\Pixel_10_Pro_XL.ini` reads `abi.type=x86_64`, `hw.cpu.arch=x86_64`,
   `image.sysdir.1=system-images\android-37.1\google_apis_playstore_ps16k\x86_64\`, while
   `AndroidBuild.cs:393` logs `IL2CPP, ARM64`. **An ARM64-only IL2CPP APK will not run on an x86_64
   system image.** Two ways out, both to be measured by the implementer, not guessed:
   (a) create a second AVD on an `arm64-v8a` system image (slow under emulation on x86 hosts, may be
   unusable for a real-time game); (b) have `AndroidBuild` emit an **x86_64 QA variant** under the same
   `QA_SCENARIO_BUILD` switch. **Check to run:** `emulator -list-avds`, boot it, then
   `adb install <apk>` and read the failure — `INSTALL_FAILED_NO_MATCHING_ABIS` confirms the theory in
   one line.
3. **Seeker guest/second user — the fallback if the emulator cannot run the game.** A second Android
   user gets its **own** `/data/user/<N>/<pkg>/` tree, so her PlayerPrefs slot is untouched *by
   construction* — that is the attraction. ⚠ **Every step below is UNPROVEN on this Seeker.** Checks,
   in order, read-only first:
   - `adb shell pm list users` — what exists, and whether multi-user is even enabled.
   - `adb shell pm get-max-users` / `dumpsys user | head` — the cap.
   - `adb shell pm create-user --guest dotr-qa` — may be refused without `MANAGE_USERS`
     (a shell-side permission on many OEM builds).
   - `adb shell am start-user <N>`, `adb install --user <N> <apk>`, `adb shell am switch-user <N>`.
   - **Unknown and consequential:** whether the Seed Vault / wallet binding is per-user, and whether
     switching users disturbs her session. **Do not run `create-user` or `switch-user` on her device
     without an explicit owner OK** — this is a device-wide change, not a sandbox.

---

## 4. SCENARIO LIBRARY v1 — each with the lines that judge it

Grep tokens below were verified to exist in this tree (source or a dated RESULT) **except where marked
unverified**.

⚠ **Every scripted scenario is prefixed by `newgame=<class>` + `onboarded=1`** on a save-free target
(§1.6.1/§1.6.2) — the per-scenario keys listed below are what goes *after* that prefix. Only (c1) can
skip `onboarded`, because it never runs the wave loop.

### (a) High-level town waves — hero Lv 52 at wave 176 (WO-1773)
`newgame=knight`, `onboarded=1`, `level=52`, `wave=176`, `resources=max` on the QA variant, town hub.
**All five are load-bearing:** without `newgame` there is no save (§1.6.1), without `onboarded` the wave
loop never starts (§1.6.2), and `wave` must land in the pre-hub phase (§1.6.3).
- **Setup proof:** `[Flow:Hero] DevPanel (AdminOverlay) set hero -> Lv.52 (target 52), Wisdom <n>`
  (the literal format string is at `AdminOverlay.cs:1078-1079`).
- **Setup proof:** `[Flow:Wave] BeginLoop data OK — calling EnterCountdown(startWave=176) [resume=176,
  dev_startWave=1]` (`WaveManager.cs:1424`).
- **The judgement — hero damage taken per wave.** ⚠ **WO-1773 is NOT on disk** (the highest
  `WorkOrders/WORK_ORDER_177*` file is 1772), so **its dead-step line could not be read and is not
  cited here.** What *is* greppable today: `[Flow:Death] lethal hit` (cited in
  `WORK_ORDER_1768_….RESULT.md:44`). **Action for the implementer:** before running this scenario, open
  WO-1773 (wherever it was minted) and take the judging token from the ticket; if the hero-damage path
  has no `FlowTrace` at all, §12 says **instrument first** — a `HeroHealth` damage-applied
  `FlowTrace.Throttle` is the opening move, not a fix.

### (b) Iron Bastion raid, Lv 4 hero, 10 troops (WO-1763/1764/1765)
`level=4;troops=10;raid=RaidBase_IronBastion`.
- **Knob provenance (WO-1763):** the single `RAID START` line,
  `RaidGarrisonSpawner.cs:212-229`. Expected shape, quoted from
  `WORK_ORDER_1763_….RESULT.md:63`:
  `RAID START config='iron_bastion' garrisonAlive=23 enemyLevel=9 (json offset 3, remote offset 5,
  src=remote) difficultyx2.08 (json 1.30 x 160%, src=remote) spire=…hp scene='RaidBase_IronBastion'`.
  Pair it with `KNOB <key> = … provenance=remote|local|default` from `RemoteTunables.cs:1771-1779`.
  ⚠ That RESULT records **no headless runner reaches `RAID START`** — it is a runtime `FlowTrace`, so a
  device or Windows-player log is the only proof. **This harness is the thing that makes that proof
  cheap**, which is a direct argument for the WO.
- **Troop target arbitration (WO-1764):** `[Flow:RaidAI] id=… routeUnit=[…] structSrc= sweepWall=
  sweepNonWall=` plus `breachStance=True stanceYield=wall` (cited
  `WORK_ORDER_1764_….RESULT.md:27,73`). The RESULT counted `breachStance=` **742×** in one capture —
  so grep with a count, and read a bounded sample, never the whole stream.
- **Camera yaw (WO-1765):** `yawSrc=`. ⚠ That RESULT is explicit
  (`:93`, `:288`): **`yawSrc=` appears 8× in 3.16 M lines and ZERO times in the raid** — the yaw is
  still unmeasured. The acceptance it names is `yawSrc=recenter` with `step=` summing past ~90° while
  the thumb is off screen (confirms the recenter whip) vs `yawSrc=input` throughout (refutes it). **A
  scripted run that produces that comparison is this scenario's primary value.**

### (c) 3-star Bastion clear → capture → owned town → practice (WO-1767/1768)
**Split in two — the dev grant skips the seam under test.**
- **(c1) Owned-town leg — scriptable today.** `town=granted` →
  `DevSkipKit.GrantCapturedTownAndEnter` (`:30-79`). ⚠ It builds the `OwnedBaseState` **directly from
  `OwnedTownTemplateManifest`** and calls `OwnedBaseProgression.TryCapture` with a synthetic receipt —
  it therefore **never runs the precombat capture census**. Judge: `[Flow:DevSkip] DEV 3-STAR CAPTURE
  granted receipt=… structures=<n>` (`:74-75`), then the owned-town/practice entry.
- **(c2) Real clear leg — needs a real raid** (emulator or guest, never her device). This is the only
  leg that can produce WO-1767's failing line:
  `[Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable identity:
  Wall_Outer_SS_0` (`WORK_ORDER_1767_….RESULT.md:20`) — **absence of that line on a 3-star clear is
  the pass.** And WO-1768's victory/hero-down race is judged by `[Flow:Death] lethal hit` ordering
  against `[Flow:SafeZone] SAFE-ZONE full recovery` (`WORK_ORDER_1768_….RESULT.md:44,64`).

### (d) Fresh-save FTUE on the emulator
The equivalent lane **already exists free on the Windows player**:
`run-autopilot-fleet.ps1 -Lane freshsave-ftue`, `Count=1`, `Graphics=$true`, marker
**`FRESH_SAVE_FTUE_OK`** (`run-autopilot-fleet.ps1:65-78`). **Run it there first.** The device leg
exists only to catch platform-specific FTUE breakage (touch, safe-area, GameActivity boot) and needs
**no scenario intent at all** — a clean install on a fresh user/AVD *is* a fresh save.
⚠ On the emulator this is blocked by the ABI finding in §3.2 until that is resolved.

---

## 5. COST

**Free, headless, no device — run this first, every time:**
`run-autopilot-fleet.ps1` on `Builds\Windows\DefendersOfTheRealm.exe`. Judged by the lane's own marker
on a **fresh per-instance** `Player.log` (`run-autopilot-fleet.ps1:56-63`: *"Marker absence is a
FAILURE, never an unknown"*). Anything a Windows player can prove must not cost a device run.

**Needs a device (or emulator) — and only these:**
- `RAID START` / `KNOB … provenance=` — no headless runner reaches it (WO-1763 RESULT `:130-131`).
- `yawSrc=` / touch-yaw behaviour — an input-device question by definition.
- Android-only boot, GameActivity, safe-area/layout, remote-bundle load (R2, CLAUDE.md §16).
- Anything the owner reports by eye.

**Token cost shape of a frames lane — the number that matters:**
a 5-minute run at 1 fps is **300 frames**. Read as 6×5 contact sheets that is **10 images**, and the
lane drills to a single full frame only on a flagged second. **Never read the 300.** Same discipline on
logs: `Select-String` with a scenario-scoped token and a bounded `-Context`, never a whole-file read —
WO-1764's capture was **3.16 M lines / 377 MB**.

**Model routing** (memory `cost-always-top-of-mind`): running the wrapper, pulling logs, building sheets
and grepping a known token are **mechanical** → the cheapest seat or a direct call. Judging whether a
`breachStance`/`yawSrc` pattern refutes a theory is **reasoning** → an Opus lane or the lead. Never both.

---

## 6. FILES

### Create
| Path | What |
|---|---|
| `tools/device-scenario.ps1` | the one wrapper: `-Serial` (**required, never baked in**), `-Scenario "k=v;k=v"`, `-Seconds`, `-Observe` (capture only, no intent — the **default**), `-Target seeker\|emulator\|user:<N>`. Resolves adb + the launcher activity at runtime, runs §2 in order, writes `logs/device/pull-<ts>-<scenario>/{pre.log,run.log,run.mp4,sheet-*.png,INDEX.md}`, prints **`DEVICE_SCENARIO_OK <dir>`** and exits non-zero without it. |
| `Assets/_Modules/DevTools/DevScenarioIntent.cs` | `#if QA_SCENARIO_BUILD` intent-extra reader + parser + **two-phase** dispatcher (§1.5/§1.6/§1.6.3). Calls the seams **directly** (no reflection). Every step `FlowTrace`d; every risky op in `Guard.Try`; absent extra = silent no-op; `newgame` fail-closed on `PlayerPrefs.HasKey("dotr-save")`. |
| `Assets/Editor/Regression/DeviceScenarioKitRegression.cs` | pins the gate, not the behaviour: `DevScenarioIntent.cs` is wrapped in `#if QA_SCENARIO_BUILD`; the parser exposes **no** bare reset verb and `newgame` is guarded by the `dotr-save` check; `AndroidBuild.cs` / the ship chain never stamp `QA_SCENARIO_BUILD` for a store, Play or **Firebase-tester** artifact. Register it in `Assets/Editor/Regression/DataRegression.cs` so it runs under `REGRESSION_OK`. |

### Modify
| Path | What |
|---|---|
| `Assets/_Modules/Village/World/Camps/DevSkipKit.cs` | add `TrainExactly(GameStateService, int n)` beside `MaxTroopTypes` (§1.6 `troops=N`). Nothing else. |
| `Assets/_Modules/DevTools/DeNelle.DevTools.asmdef` | add `QA_SCENARIO_BUILD` to the existing `defineConstraints` (a comment at `DevPanelController.cs:19-23` reports them as `UNITY_EDITOR \|\| DEVELOPMENT_BUILD`) so the assembly compiles for the QA variant. ⚠ **read the asmdef before editing — that constraint text is quoted from a CODE COMMENT, not from the file** (CLAUDE.md §5: comments lie). |
| `overnight-apk-build.ps1` | add `-Scenario` → appends **`TESTER_BUILD;QA_SCENARIO_BUILD`** to `$Defines`, in the same shape `-Tester` appends `TESTER_BUILD` (`:46-52`). It **implies** `-Tester` (§1.5) — accept both together, and make the artifact filename carry `-scenario` so a QA APK can never be mistaken for the tester upload. Do **not** re-inline the R2 push/verify — it calls `tools\r2-ship.ps1` and must keep doing so (CLAUDE.md §16). |
| `.claude/skills/run-defenders/SKILL.md` | new subsection **"Scripted device scenarios"** under the existing `## Device felt-test from the PC (scrcpy)` (line 202): the wrapper command, the §3 device policy, the §4 scenario table, the contact-sheet rule. |
| `WorkOrders/WORK_ORDER_1775_….md` | `**Status:**` flipped in the same commit as the work + a `.RESULT.md` (CLAUDE.md §11). |

### ⛔ DO NOT TOUCH
- `Assets/_Modules/HUD/AdminOverlay.cs` — **especially not the two-guard split at `:259-297`.** Widening
  the inner guard to `TESTER_BUILD` re-opens the WO-1512 door the comment forbids in writing.
- `Assets/_Modules/DevTools/DevPanelController.cs` — deprecated, tagged for removal, activation off.
  Do not revive it, do not "fix" `JumpToWave`.
- `Assets/_Modules/Village/Waves/WaveManager.cs` — §1.4 needs **no** change to it. Do not add a
  `JumpToWave`.
- `Assets/_Modules/Core/State/SaveSchema.cs` / `GameState.cs` — **no schema bump**; every parameter
  rides an existing persisted field.
- Any `.unity` scene; `Assets/AddressableAssetsData/`; `.githooks/pre-push`; `tools/r2-ship.ps1`.
- The owner's device apps, users, or settings. The F8 inbox ack state.

---

## 7. ACCEPTANCE CRITERIA

**Harness (no game code) — provable without a device:**
1. `tools/device-scenario.ps1 -Serial <s> -Observe -Seconds 6` on **any** reachable device produces
   `logs/device/pull-<ts>-observe/` containing a **non-zero** `run.mp4` whose header reads `ftyp isom`,
   a non-empty `run.log`, at least one `sheet-001.png` that the Read tool opens, an `INDEX.md`, and the
   marker **`DEVICE_SCENARIO_OK <dir>`** on the console. **Judge the marker on a fresh log, never the
   exit code** (CLAUDE.md §8).
2. With an overlay window present, the wrapper **STOPS before recording**, prints the offending window,
   and exits non-zero. Proven by a deliberate overlay on the **emulator**, never on the Seeker.
3. `logcat -c` is provably never issued before the `-d` drain (read the script; assert the order).
4. `-Serial` is required and no serial literal appears anywhere in the script or SKILL.md.
5. `Get-Process scrcpy` is empty after the wrapper returns, on both the pass and the fail path.
6. Without `-Scenario`, the wrapper sends **no** intent and no `force-stop` (observe is the default).
7. ⚠ With **two** devices attached (Seeker + emulator) the wrapper still works: `capture-seeker-screen.ps1`
   counts the whole `adb devices` list (`:11-14`) and `adb -s <serial> devices` **still lists every
   device**, so it throws even when a serial was given. Either pass a resolved single-device context or
   have the wrapper take the screenshot itself via `cmd.exe /d /s /c` when the list length is > 1.

**Scenario kit:**
8. A `QA_SCENARIO_BUILD` APK launched with **no** extra behaves identically to a TESTER APK: exactly one
   `[Flow:DevScenario]` line saying no scenario was requested, and no state written.
9. `newgame=knight` on a **save-free** target founds a town (one `ResetToNewGame`, traced); the **same**
   key on a save-bearing target is **refused** with a `FlowTrace.Fail` and leaves the save byte-identical.
   **Both halves must be shown** — the refusal is the half that protects the owner.
10. `onboarded=1` ⇒ waves actually start (no `WaveLoopSuppressedForTutorial` stand-down in the run log).
11. `level=52` ⇒ the hero reaches Lv 52 through `HeroProgression.AddXp`, traced with the level + Wisdom
    (the shape `AdminOverlay.cs:1078-1079` prints).
12. `wave=176` ⇒ `[Flow:Wave] BeginLoop data OK — calling EnterCountdown(startWave=176) [resume=176, …]`,
    **and** a run that sets it in the post-hub phase is shown to `FlowTrace.Warn` rather than silently
    no-op (the §1.4 trap).
13. `troops=10` ⇒ an army of exactly 10 owned troops (`TrainExactly`), traced with the count.
14. `raid=RaidBase_IronBastion` ⇒ the one `RAID START config='iron_bastion' … src=<json|remote>` line.
15. The parser exposes **no** bare reset/wipe verb; anything resembling one is refused with a
    `FlowTrace.Fail`, pinned by the regression.
16. No **store**, **Play** or **Firebase-tester** artifact ever carries `QA_SCENARIO_BUILD` — pinned by
    the new regression alongside the WO-1741 quarantine checks, and the QA artifact's filename carries
    `-scenario` so the two can never be confused at upload time.

**Gates:** `python tools/gate_brace.py` on every touched `.cs` **then** `COMPILE_GATE_OK` +
`REGRESSION_OK <n>/<n> suites` on a **fresh** log. `python tools/board_build.py` with its
`BOARD_CHECK_*` marker in the lead's commit.

---

## 8. OPEN QUESTIONS

**Q1 (`class=`) is CLOSED** by §1.6.1: hero select is the only door, so class rides `newgame=<class>` on
a save-free target and is refused otherwise. That leaves:

- **Q2 — `camera=`:** is there a named camera-profile knob a scenario can set (WO-1765 touched a raid
  over-the-shoulder profile), or is the profile derived per scene?
- **Q3 — owner ruling:** may a **guest user** be created on the Seeker (§3.3), or is the emulator the
  only scripted target? The emulator is currently **ABI-blocked** (§3.2), so a "no" on Q3 makes an
  `arm64` AVD or an x86_64 QA variant a prerequisite for scenarios (a) (c2) and (d).

---

## 9. RELATIONSHIP TO WO-1766

`WORK_ORDER_1766_mobile_use_agent_felt_test_pilot.md` (**Status: READY TO IMPLEMENT**) pilots
minitap-ai / mobile-use as a **natural-language felt-test driver**, emulator-only and isolated.

The two are complementary and **1775 comes first**: 1775 provides *deterministic setup* (a scenario
string) and *evidence capture* (logs, video, contact sheets). 1766 adds *exploratory driving* (taps
chosen by a model) on top. **1766 is not required for 1775 v1** — v1 drives nothing; it launches a
scenario with an intent and watches. Once both exist, 1766 should call `tools/device-scenario.ps1` for
setup and capture rather than growing its own, so there is **one** device recipe (CLAUDE.md §16's
don't-re-inline lesson).

---

## 10. UNVERIFIED CLAIMS IN THIS SPEC (read before implementing)

1. **Launcher activity class** — inferred from `androidApplicationEntry: 2`; resolve on device
   (§1.5).
2. **Emulator cannot run the APK** — inferred from `abi.type=x86_64` vs `IL2CPP, ARM64`; confirm with
   `adb install` and look for `INSTALL_FAILED_NO_MATCHING_ABIS` (§3.2).
3. **`run-as` refused / Seeker not rooted** — inferred from `BuildOptions.None`; confirm with
   `adb shell run-as com.denellestudios.echoesofelarion ls` and `adb shell su -c id` (§1.7).
4. **Every guest-user step** — `pm list users`, `pm create-user`, `am start-user`, `pm install --user`,
   and whether the wallet/Seed Vault binding is per-user (§3.3).
5. **WO-1773's judging line** — the ticket is **not on disk**; no dead-step line was read or cited
   (§4a).
6. **DevTools asmdef constraint text** — quoted from a code comment, not from the `.asmdef` (§6).
7. **`--stay-awake`** — listed in SKILL.md as not exercised; treat as unproven.
8. **`QA_SCENARIO_BUILD` forwarding through `-ExtraScriptingDefines`** — the mechanism is read at
   `overnight-apk-build.ps1:46-52`, but no build was run with a new define by this lane.
9. **`camera=` seam** — not located (§8 Q2).
10. **Intent extras survive to a Unity GameActivity launch** — the `getIntent` idiom is proven in this
    tree for an activity **result** (`TargetedLocalAssociationScenario.cs:226`), but **no lane has read a
    launch extra on this app**. One-line check on the QA APK: log `intent.Call<string>("getDataString")`
    and the extras bundle key set on first frame.
11. **`;` splits on the device's `sh`** — asserted from POSIX shell behaviour, not measured here. It costs
    nothing to avoid (§1.5 uses one typed extra per key), so the design does not depend on the answer.
12. **`capture-seeker-screen.ps1` throws with two devices attached** — inferred from `adb devices`
    semantics + the count guard at `:11-14`; not measured with two devices online (acceptance #7).
13. **`Onboarded` is the gate on scenario (a)** — the per-tick stand-down is read at
    `WaveManager.cs:1205-1225` and the `!Onboarded` root is from memory
    `enemies-never-spawn-tutorial-onboarded-gate`; the two were **not** traced end-to-end this session.

---

## IMPLEMENTATION RECORD (2026-09-17) — files, re-verification, and flags for the lead

**Files written**
- `tools/device-scenario.ps1` — the capture wrapper (new).
- `Assets/_Modules/DevTools/DevScenarioIntent.cs` — the `#if QA_SCENARIO_BUILD` intent reader +
  two-phase dispatcher (new).
- `Assets/Editor/Regression/DeviceScenarioKitRegression.cs` — the gate-only source-lint suite,
  8 checks (new).
- `Assets/Editor/Regression/DataRegression.cs` — one `Guard.Try` registration line for the new
  suite, same idiom as the `echo-world-presence suite` line immediately above it.
- `Assets/_Modules/Village/World/Camps/DevSkipKit.cs` — added `TrainExactly(GameStateService, int)`
  beside `MaxTroopTypes`. Nothing else touched in this file.
- `Assets/_Modules/DevTools/DeNelle.DevTools.asmdef` — `defineConstraints` widened from
  `"UNITY_EDITOR || DEVELOPMENT_BUILD"` to `"UNITY_EDITOR || DEVELOPMENT_BUILD || QA_SCENARIO_BUILD"`.
- `overnight-apk-build.ps1` — added `-Scenario` switch (implies `-Tester`); tags the built APK's
  filename with `-scenario` (a `Rename-Item` step right after the freshness check, so the
  `[filename-tag]` gate check has real code — not just a comment — to find).
- `.claude/skills/run-defenders/SKILL.md` — new "Scripted device scenarios (WO-1775)" subsection
  under "Device felt-test from the PC (scrcpy)".
- This file — `**Status:**` flipped to READY FOR LEAD REVIEW.

**Gates run by this lane (per the task brief — NOT the Unity compile gate/DataRegression, which
are the lead's):**
- `python tools/gate_brace.py` on every touched `.cs`: `DevScenarioIntent.cs`, `DevSkipKit.cs`,
  `DeviceScenarioKitRegression.cs`, `DataRegression.cs` — all `bad=0`.
- A NUL-byte scan (`open(path,'rb').read().count(b'\x00')`) on the same four files — all `0`.
- `[System.Management.Automation.Language.Parser]::ParseFile` on both touched `.ps1` files
  (`device-scenario.ps1`, `overnight-apk-build.ps1`) — both `PARSE_OK`.
- **NOT run by this lane:** `COMPILE_GATE_OK`, `REGRESSION_OK <n>/<n> suites`,
  `python tools/board_build.py` / `BOARD_CHECK_*`, and no Unity, no device, no APK build was
  invoked. Per the task brief, the lead runs the Unity gate over the combined tree.

**Re-verified at source this session (§10's list, one by one) vs trusted from the spec:**
- **Every §1.6 seam the spec cited** (`AdminOverlay.cs` two-guard split `:259-297`,
  `OnSetHeroLevel` `:1026-1085`, `OnSetOnboarded` `:855-862`, `DevSkipKit.cs` whole file,
  `GameStateService.RecordRun` `:1117-1122`, `GameStateService.ResetToNewGame` `:1366`,
  `WaveManager.ResolveStartWave`/`s_resumeWaveId` `:1501-1509`/`:628`,
  `RemoteTunables.LocalPrefix` `:131`, `SaveSchema.PlayerPrefsKey` `:47`,
  `TargetedLocalAssociationScenario`'s `getIntent`/`currentActivity` idiom `:222-226`,
  `AndroidBuild.cs` `PackageId`/`BuildOptions.None`/IL2CPP-ARM64 log `:49/:141/:393`,
  `overnight-apk-build.ps1`'s `-Tester`/`-Defines` shape `:36-53`,
  `capture-seeker-screen.ps1`'s device-count guard `:11-14`) — **read at source this session,
  confirmed present, line numbers drifted by only a few lines from the spec's citations (natural
  churn), no claim was wrong.**
- **§10 item 6 (DevTools asmdef constraint quoted from a comment, not the file)** — **confirmed
  exactly as the spec warned.** The actual `defineConstraints` array is ONE string,
  `"UNITY_EDITOR || DEVELOPMENT_BUILD"` — not two array entries as a careless read of
  `DevPanelController.cs:19-23`'s comment might suggest. Edited the real file, not the comment's
  paraphrase.
- **A seam the spec did NOT cite, found and used instead of a manual field write:**
  `GameStateService.ChooseHero(HeroClass cls)` (`:1209-1230`, doc-commented
  `playerSlice 'chooseHero' — lock in the hero class`) is the REAL hero-select commit path —
  it runs the `PlayableHeroes.IsPlayable` coercion, fires `PlayerChanged`, and `Save()`s. The
  spec's §1.6 table only said "class rides `newgame=<class>`" without naming the seam;
  `ApplyNewGame` calls `svc.ResetToNewGame(); svc.ChooseHero(cls);` rather than assigning
  `svc.State.HeroClass` directly, so the scenario gets the same coercion/eventing a real hero
  pick gets.
- **§10 items 1–4, 7, 9, 12 (launcher activity class, emulator ABI, `run-as`/root, guest-user
  steps, `--stay-awake`, define-forwarding-with-a-real-build, two-device screenshot throw)** —
  **NOT re-verified; still unproven, exactly as the spec said.** None of these need a live
  device/build to WRITE the harness correctly per spec (the wrapper resolves the activity at
  runtime and never hardcodes it; it never calls `capture-seeker-screen.ps1` at all — see FLAG 1
  below), so they were left as open device-side facts for whoever runs the wrapper first, not
  guessed at here.
- **§10 item 5 (WO-1773's judging line, not on disk)** — still true; the harness makes NO claim
  about what token proves scenario (a)'s hero-damage judgement, per the spec's own instruction.
- **§10 item 13 (`Onboarded`/`WaveLoopSuppressedForTutorial` not traced end-to-end)** — still not
  traced end-to-end by this lane either; `ApplyOnboarded` implements the write the spec specified
  (`GameState.Onboarded = value; Save();`), and its correctness rests on the same unverified
  memory citation the spec rests on.

### FLAG 1 — the wrapper never calls `capture-seeker-screen.ps1` at all
Acceptance #7 worries about `capture-seeker-screen.ps1` throwing with two devices attached even
when a serial is passed (its own `adb devices` call, not `adb -s <serial> devices`, counts the
WHOLE fleet). Rather than patch that helper (out of scope — `DO NOT TOUCH` lists nothing about it,
but it also isn't listed under `Modify`) or duplicate its `cmd.exe` PNG-safe idiom, the wrapper
produces its evidence entirely from `scrcpy --record` + `ffmpeg` contact sheets and never takes a
standalone screenshot — so the two-device hazard that helper has simply never triggers from this
path. Every `adb` call inside `device-scenario.ps1` goes through `Invoke-Adb`, which always passes
`-s $Serial`. **Not measured with two devices physically attached this session** (no second
device was online) — the design avoids the failure mode rather than proving the avoidance live.

### FLAG 2 — `resources=` and `buildings=` value grammar is this lane's own design, not spec-dictated
WO-1775's scenario library only ever writes `resources=max` and `buildings=max` in its worked
examples; the §1.6 table's `resources=wood:50000,iron:50000,…` is the only hint at a general
grammar. `ApplyResources` accepts `max` (mirrors `AdminOverlay.OnLoadResources`'s
50000/50000/50000/50000 + 50000 coins) or a `key:amount,key:amount` list
(`wood|food|iron|crystals`); `ApplyBuildings` accepts only `max` (calls `DevSkipKit.PrepCastlePower()`
directly, which folds in `MaxPlacedStructureLevels` and a troop top-up the spec's table didn't
mention as a side effect) and Warns on anything else. Neither grammar choice is spec-cited, so
flagging it for the lead rather than presenting it as WO-approved.

### FLAG 3 — `troops=N` cannot shrink an already-larger army
`DevSkipKit.TrainExactly` only trains UP to `n` (no disband verb exists anywhere in this kit, per
§1.6.1's "no bare reset" stance extended to troops). If `buildings=max` ran first in the same
scenario string (it does, in the PRE-HUB apply order) its own `MaxTroopTypes` call may already
have trained more unique troop types than a smaller `troops=N` target — `ApplyTroops` then
`FlowTrace.Warn`s that the army was "left at" a higher count rather than silently claiming N.
Scenario (a)/(b)/(c) in this WO never combine `buildings=max` with a smaller `troops=`, so this
never fires in the documented library, but a future scenario string could hit it.

### FLAG 4 — `wave=` landing late Warns via a structural gate, not a dynamic WaveManager read
Acceptance #12's second half ("a run that sets it in the post-hub phase is shown to
`FlowTrace.Warn`") is satisfied STRUCTURALLY: `wave` is only ever read from the PRE-HUB key set in
`TryApplyPreHub`, never from `TryApplyPostHub`'s key set, so the normal two-phase dispatch cannot
literally apply it post-hub. The Warn path added (`TryApplyPostHub` firing before `s_preHubDone`
because the hub scene loaded before any `GameStateService` was ever seen) covers the one route
this dispatcher's own state machine could still let it happen late. This was **not proven against
a live `WaveManager`** — `s_resumeWaveId` has no public accessor, so nothing here reads the real
resume-seed state to confirm the "already resolved" claim; the Warn fires on the dispatcher's own
phase-ordering evidence, not on a WaveManager read.

### FLAG 5 — the `[ship-quarantine]` regression check needed a bug fix mid-write, left as a lesson in its own header
The first draft of `DeviceScenarioKitRegression.CheckShipQuarantine` scanned RAW
`overnight-apk-build.ps1` text for `QA_SCENARIO_BUILD` — which found this WO's own `#`-comment
documentation paragraph FIRST (before any real code), so the "is it behind an `if` guard" window
landed in prose and would have FALSE-FAILED the gate the moment it ran. Fixed by adding a
PowerShell-comment-stripping helper (`StripPowerShellComments`, quote-aware) and reading that
instead of raw text for this one check; verified by hand-simulating the same strip in Python
against the real file (`first code idx 219`, window contains `if ($Scenario) {`). This is exactly
the class of self-inflicted false-positive CLAUDE.md §12 warns against — caught before hand-back,
not by the lead's gate.

**What was NOT executed, stated plainly:** no Unity batchmode run, no APK build, no device/emulator
was touched. Every claim about `DevScenarioIntent.cs`'s runtime behavior (the two-phase dispatch,
the `newgame`/`onboarded`/`wave`/`level`/`troops`/`resources`/`buildings`/`raid`/`town`/`ff.*`/
`tun.*` appliers) is reasoned-from-the-source, not measured — WO-1775 §5 says exactly this class of
behavior needs a device to prove, and that is the lead's or a device-qa lane's next step once the
Unity gate is green.
