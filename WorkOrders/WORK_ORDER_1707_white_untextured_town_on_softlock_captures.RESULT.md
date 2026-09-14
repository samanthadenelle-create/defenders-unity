# RESULT — WO-1707 white/untextured town

**Status of this RESULT:** PARTIAL. One mechanism is proven with captured data and fixed. The
symptom the owner will actually see most (walls/houses white) is **not proven** — the evidence
ceiling was real and is documented below, with the one instrumentation edit that closes it. Per
CLAUDE.md §11B, this hands back what is proven as proven and what isn't as unproven — not a
guessed fix.

## 1. What was captured and read this session (real evidence, not re-quoted from the WO)

- `logs/f8-inbox/device/SM02G4061955851/break_02_possible_softlock.png` — **ROTATED.** mtime is
  now `2026-09-12 10:39:18`, not the `2026-09-11 21:23:49` the WO cites. The harness's numbered
  slot was overwritten by a later capture. Opened anyway: it is a fresh Lv1/209-gold save
  rendering **fully textured**. This is useful negative evidence (a fresh save alone does not
  produce white) but it is **not** the seq5023 evidence any more — that PNG is gone.
- `break_03_possible_softlock.png` — intact (mtime unchanged, `2026-09-11 19:41:50`), matches the
  WO's description: a wall section and one house render pure white while the wall behind them and
  a second house render normally (per-object, not global).
- `break_04_possible_softlock.png` (new today, not in the WO) — mixed scene, no pure-white
  building geometry; not evidence for this ticket.
- Pulled the device's raw `break-log.jsonl` (`adb pull`, read-only — device was **not** launched or
  tapped, only queried: `adb devices`, `pm list packages`, `dumpsys package`, `ls`, `pull`).
  Confirmed the device is currently on APK `2026.09.14.369734` (installed today) — the build that
  produced these captures (`2026.09.12.365875`, per `docs/WORKLOG_2026-09-11.md:11`) is gone from
  the device; there is no way to re-pull its live state.
- Correlated `t` (seconds since app start) + `utc` across every capture in `logs/f8-inbox/QUEUE.jsonl`
  and the pulled `break-log.jsonl`. This is the key move that split the boots (the WO's own seq
  numbers turned out to span 3 different app sessions, not one):
  - **Boot A** (utc 00:33:15→00:41:49, 2026-09-11 19:33-19:41 local): seq5017 (INIT-invalid,
    t=5.1s) → seq5018 (**Hub albedo zero-bind**, t=30.6s) → seq5019 (softlock, t=285.7s, no
    screenshot) → **seq5020 (softlock, t=516.4s → `break_03`, the white wall/house)**.
  - **Boot B** (utc 02:17:06→02:23:48, 2026-09-11 21:17-21:23 local): seq5021 (INIT-invalid,
    t=6.3s) → seq5022 (**Hub albedo zero-bind**, t=39.9s) → **seq5023 (softlock, t=404.0s →
    the now-rotated `break_02`)**.
  - **Confirmed-good boot** (utc 03:31:41→03:36:02, from the SAME day, cited by the WO §4's
    22:31-local claim): session_start t=2.1s → INIT-invalid t=6.6s → HeroSelect t=11.9s →
    Main_Castle_Overworld t=15.9s → six FLAG presses t=224-263s, no Hub error anywhere in
    between. `logs/debug/seeker-365962-logcat.txt` is this exact boot and is the only full
    (Warn+Info+Error, not error-only) log available.

## 2. Mechanism split — with quoted data, not inference

**Mechanism 3 (missing R2 bundle) — ruled out.** No `RemoteProviderException` / `HTTP 404` line
exists anywhere in `logs/debug/seeker-365962-logcat.txt` or the pulled device `break-log.jsonl`.

**Mechanism 2 in its literal form (INIT handle kills the whole warm coroutine, "not one
structure address was ever requested") — ruled out, proven, not just asserted.** The
`Addressables INIT handle is INVALID` Fail fires in **all three** boots (good and bad) at
t=5-7s, and the good boot's full logcat proves the pass continues exactly as the Fail message
claims it does:
```
22:31:46.311 E [Flow:StructureAssets] Addressables INIT handle is INVALID after 4.0s: ...
              The pass now CONTINUES ...
22:31:46.312 I [Flow:StructureAssets] warm pass found 45 structure address(es) under 'Structures/'.
22:31:47.692 I [Flow:StructureAssets] warm pass requested 45 structure asset(s) for RESIDENCY.
```
This closes cluster A/WO-1089's "awaiting the resident-structures confirm" as an actual confirm,
not a proposal — the doc's own §3 line called this "log-level-noise... describes its own success
path"; it is now proven to, not just plausibly do so.

**Mechanism 1, as the WO scoped it (arcane-only forced-albedo path) — real, and it is the ONLY
mechanism with a directly quoted Fail line inside either bad boot:**
```
Boot A t=30.6s: [Flow:Hub] 'ArcaneTower_MagicUpgrades': forced albedo 'Structures/ArcaneTower_Albedo'
  RESOLVED but bound onto ZERO of 2 material slot(s) — no material declares _BaseMap or _MainTex
  (first shader 'Synty/Generic_Basic').
Boot B t=39.9s: identical line, same structure, same shader name.
```
Contrast with the SAME call in the confirmed-good boot, ~1s after the SAME scene load:
```
22:31:56.581 I [Flow:StructureAssets] 'Structures/ArcaneTower_Albedo' served RESIDENT from the
              structure warm cache ...
22:31:56.581 I [Flow:Hub] 'ArcaneTower_MagicUpgrades': forced albedo 'Structures/ArcaneTower_Albedo'
              BOUND onto 2/2 material slot(s) via StructureAssetLoader.
```
`ArcaneTower_MagicUpgrades` is a **baked hub twin** (`CastleHubBuilder.cs:312`) — placeholder
scene geometry that `HubStructureVisualInjector.TrySwap`/`SkinStorefront` swaps for the real Tripo
model at runtime. `Synty/Generic_Basic` is the **placeholder's own shader**, not the real model's —
so the bad-boot Fail is not (only) a property-naming miss, it is the forced-albedo call landing
**on the un-swapped placeholder** because the swap had not completed when `ApplyAll()` ran. In the
good boot the warm pass finished 8+ seconds earlier, during Title/HeroSelect, well before the town
scene loaded, so the real model was already in place. In both bad boots the warm pass and the town
scene load happened close together and the placeholder lost the race.

This is a real, data-proven bug: **`ApplyForcedAlbedo`'s "bound onto ZERO of N slots" branch never
arms a retry** (`Assets/_Modules/Village/HubStructureVisualInjector.cs`, pre-edit lines 725-732) —
unlike its sibling branch two lines above (`tex == null`), which does call
`ArmForcedAlbedoRetry(s)`. A structure caught by this race stayed permanently white for that scene
visit; the player standing still for 180s+ (which is what triggered the `possible_softlock`
capture in both bad boots) could never self-heal it.

**Fix applied (`Assets/_Modules/Village/HubStructureVisualInjector.cs`):** the zero-bound branch
now also calls `ArmForcedAlbedoRetry(s)`. `s_texRetryArmed` already dedupes it to one retry, so a
genuinely permanent shader mismatch (if one exists) logs the same Fail once more and stops — no
loop. Gated: `python tools/gate_brace.py Assets/_Modules/Village/HubStructureVisualInjector.cs`
→ `GATE_BRACE_SUMMARY bad=0 of 2` (run together with the second file below); raw brace/NUL check
also clean (`open=133 close=133`, no NUL bytes).

## 3. What is NOT proven — the wall/house symptom, which is the bulk of what the screenshot shows

`ArcaneTower_MagicUpgrades` only explains the Cathedral/arcane structure. It does **not** explain
the wall segment + house that are the dominant visible defect in `break_03`. Walls/houses do not
go through the forced-albedo swap table at all (confirmed by code read: every other `bakedName` in
the good boot's logcat logs `NO texPath authored — forced-albedo rebind SKIPPED BY DESIGN`), and
`Wall_Medieval_Stone` is baked into the scene at editor-bake time by `CastleHubBuilder.cs` from a
real polyperfect material (`M_21_Grey_Light_LPUP`), not loaded at runtime the same way.

The one instrumented signal that COULD explain a plain structure rendering flat white —
`TripoMaterialFixer.VerifyAllRenderersUrp`'s "NO ALBEDO" line, which runs for every structure
(`SkinOptions.Structure` sets `FixTripoMaterials = true` unconditionally, so every catalog
structure carries this fixer) — **is `FlowTrace.Warn`, not `Fail`.** Per
`docs/INSTRUMENTATION_STANDARD.md` / the run-defenders skill, `break-log.jsonl` (and therefore the
F8 device bridge and every capture this ticket has access to) is **error-level only**. That line
could have fired in both bad boots and left zero trace anywhere I can read. This is a proven,
citable **capture-pipeline gap**, not a guess: I confirmed by grepping the pulled device
`break-log.jsonl` for `TripoMatFix`/`NO ALBEDO` across both bad boots — zero hits — while the same
grep against the good boot's FULL logcat (which is not error-filtered) shows plenty of `TripoMatFix`
activity for other objects. The Warn-only line is exactly the blind spot.

**Instrumentation added (`Assets/_Modules/Core/TripoMaterialFixer.cs`,
`VerifyAllRenderersUrp`):** when a slot has no albedo AND its tint is literally white
(`tint.r/g/b > 0.95`, i.e. no designed miss-tint or flat-coat degrade pushed it off white — this is
exactly the pure-white symptom, not a legitimate flat-tint structure), the line is now emitted as
`FlowTrace.Fail` instead of `Warn`, so it reaches `break-log.jsonl` / the F8 inbox on the next
capture. A non-white tint (the designed degrade path) stays a `Warn` — this does not turn every
untextured decorative mesh into break-log noise. Gated together with the Hub file above:
`GATE_BRACE_SUMMARY bad=0 of 2`; raw check `open=82 close=82`, no NUL bytes.

**This is instrumentation, not a fix**, per the acceptance-criteria note in §5.1 of the WO and
CLAUDE.md §12/§11B: I have not proven what actually fails for the wall/house case, only closed the
hole that prevented proving it. The next `possible_softlock` capture with a white wall/house in
frame will now carry either a `[Flow:TripoMatFix] NO ALBEDO ... Tint is WHITE` Fail naming the exact
object/shader, or — if that line does NOT fire — proof that the wall/house whiteness is a
*different* mechanism entirely (e.g. the cosmetic-application path for `wall-tier-2`/
`village-walls-stoneweave`, which I found as a plausible but unverified candidate and did **not**
touch).

## 4. Explicitly not done, and why

- **No fresh boot was launched.** Building the Windows player or firing any Unity batchmode gate is
  reserved for the lead per this task's own instructions; launching/tapping the physical device is
  forbidden by standing memory (`device-lanes-overlay-apps-and-start-new`,
  `owner-prefs-playtest`). All device interaction here was read-only (`adb devices`, `pm list
  packages`, `dumpsys package`, `ls`, `pull`) — nothing was started or tapped.
- **No post-fix screenshot** (WO acceptance criterion 2) — needs an actual boot, which needs the
  lead's gate/build step.
- **No regression written.** A regression asserting the retry-arming behavior, or asserting
  `VerifyAllRenderersUrp` never emits a pure-white Fail after a settled load, needs to run through
  `DataRegression` to prove it compiles and passes — that is the lead's gate, not mine to claim
  green on faith (CLAUDE.md §12: "Never claims fixed on faith").
- **Did not touch** the ~200 lines of unrelated uncommitted WIP already sitting in
  `StructureFactory.cs` / `HubStructureVisualInjector.cs` / `DependencyClosureTrace.cs` /
  `TripoMaterialFixer.cs` (the WO-707/WO-719/cluster-B albedo-oracle generalization from a prior
  session) beyond the two additive edits described above. Did not evaluate whether that WIP is
  ready to ship.

## 5. Files touched

- `Assets/_Modules/Village/HubStructureVisualInjector.cs` — armed the missing retry on the
  zero-bound forced-albedo branch.
- `Assets/_Modules/Core/TripoMaterialFixer.cs` — promoted the pure-white "NO ALBEDO" case from
  `Warn` to `Fail` so it reaches the capture pipeline.
- `WorkOrders/WORK_ORDER_1707_white_untextured_town_on_softlock_captures.md` — Status flipped to
  PARTIAL (this file).

Both files: `python tools/gate_brace.py <path>` → `GATE_BRACE_SUMMARY bad=0 of 2`. Raw
brace+NUL one-liner also clean on both. No `.unity` scene files touched. No commit made (per this
task's instructions — the lead batches gate + commit).

## 6. Recommended next step for the lead

1. Gate the combined tree (this ticket's 2 files + whatever else is queued).
2. Get one fresh capture that reproduces a white wall/house — either the owner's next felt-test, or
   a headless/headed editor proof that boots `Main_Castle_Overworld` on a fresh/low-level save. If a
   `[Flow:TripoMatFix] ... Tint is WHITE` Fail appears, that names the exact object and shader and
   closes this ticket for real. If it does NOT appear, the wall/house mechanism is not
   `TripoMaterialFixer` at all and the cosmetic-application path (`wall-tier-2` /
   `village-walls-stoneweave`, `Assets/_Modules/Cosmetics/CosmeticApplier.cs`) is the next candidate
   to instrument — not to guess-fix.
3. Once a mechanism is named by data, write the regression the WO's acceptance criterion 3 asks
   for against that specific assertion.
