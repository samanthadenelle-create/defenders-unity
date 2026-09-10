# WO-1567 RESULT - the Manage art wave: gated, captured, built, shipped to the device

**Closed out:** 2026-09-10, against `dev` @ `736b6b4b9` (this lane read the WO's own committed copy;
byte-compared against `D:\EoA\WorkOrders\...` and identical ignoring CR, so no stale copy was read).
**Verdict:** **PARTIALLY DONE - AWAITING OWNER MATCH.** Every mechanical gate in §3 that was run is
green and is quoted below from a fresh log this lane opened itself. **The acceptance that actually
closes this ticket is §6.0's, and it is the owner's, not a lane's.** Six items are open (sec.4).

⛔ **WHY THIS IS NOT "DONE", IN THE WO'S OWN WORDS (§6.0, owner ruling 2026-09-07):** *"The acceptance
is a DEVICE SCREENSHOT placed beside its mockup panel and judged a match BY THE OWNER. Items 1-4 below
are evidence toward that judgement. **They can never mark a ticket done.** ... Until then its status
reads `AWAITING OWNER MATCH` and the board buckets it **Verify**, not Done and not Fixed."* Flipping
this to DONE off four green markers would be the exact thing that ruling was written to stop.

---

## 1. §6 ACCEPTANCE, ITEM BY ITEM

| # | Item | Verdict | Evidence, read at source by this lane |
|---|---|---|---|
| 1 | `COMPILE_GATE_OK` on a fresh log over a **quiescent** tree | **MET** | `Builds/wave5-compile5` (1,893,081 B, 08:20:47): `COMPILE_GATE_OK :: scripts compiled clean`, and `COMPILE_GATE_FAIL` = **0 matches**. Quiescence in sec.2. |
| 2 | `REGRESSION_OK <n>/<n>` **with `ManagePortraitCoverageRegression` green** | **MET** | `Builds/wave5-reg3` (2,082,076 B, 08:27:03): `REGRESSION_OK 494/494 suites -- 494 green, 0 red, 0 skipped`, and **by name**: `=== ManagePortraitCoverageRegression ===` -> `MANAGE_PORTRAIT_COVERAGE_OK 68 Manage portrait key(s) resolve; 0 dated art exemption(s) still genuinely absent.` |
| 3 | `MANAGE_FLOW_MAP_OK`, no missing or duplicate frames, **PNGs opened** | **MET** | `Builds/wave5-manageflow2` (1,329,409 B, 08:21:27): `MANAGE_FLOW_MAP_OK 20 frames`; `CAPTURE_LEDGER_MISSING` **0**, `CAPTURE_LEDGER_DUPLICATE` **0**; 20 `Builds/ui-capture/ManageFlow_*.png` on disk, names matching the marker's list exactly (BUILD 7 + ARMY 6 + RESEARCH 7). PNGs opened: sec.3. |
| 4 | Panel-by-panel comparison recorded against WO-1566 §2 / `CAPTURE_LOOP_GOAL.md` §3, each row ticked from a frame or marked BLOCKED | **PARTIAL - OPEN** | The comparison exists and has been audited (`736b6b4b9`: *"WO-1566 Manage yardstick AUDITED - 51 pass / 4 fail / 11 unmeasured of 68 rows"*), so it is neither untouched nor complete. **4 fail + 11 unmeasured are open by that audit's own count.** |
| **6.0** | **Owner judges a device frame >= 95% on SIZE / FONT / STYLE / CONTEXT / IMAGES, criterion zero first** | **NOT DONE - the ticket's actual gate** | Not a lane's call. Frames are ready and named in sec.3. |

**This wave also cleared three gates §6 does not list**, on the same fresh capture:
`UI_GEOMETRY_OK 20 canvases`, `UI_TOUCH_OK 20/20 panels`, and
`UI_GLYPH_OK 20/20 panels labels=361 **baselined=0** unproved=0`. The `baselined=0` is worth naming:
on this entry point the Manage flow now carries **no accepted truncation debt at all**.

## 2. Quiescence - stated beside the result, as §3 demands

Newest `.cs` under `Assets/`: **`Assets/Editor/Regression/ManageQueueDrawerRegression.cs`, 08:19:28** -
which IS the change under test (WO-1651's pin). Every marker landed after it: compile5 **08:20:47**,
manageflow2 **08:21:27**, reg3 **08:27:03**, and nothing has touched a `.cs` since.
⚠ **Precisely:** a log's mtime is the run's END, so this proves ORDERING (no source edit after the
chain finished), not the literal "no `.cs` newer than the run START". No mid-save edit is visible, and
that is as far as mtimes can carry it.

## 3. §3 step 4 - THE PNGs WERE OPENED AND LOOKED AT

Two **device** frames off build `363660` were opened by this lane (not the headless captures - the
device is what §6.0 judges):

**`Builds/device-frames/2026-09-10_0814_363660_manage_hub.png`** (2670x1200)
* ⭐ **CRITERION ZERO LOOKS MET, AND IT WAS THE HEADLINE DEFECT.** The panel now runs essentially
  edge-to-edge - only a sliver of town shows at the extreme margins. §4a's measured defect was a plate
  at *"x 0.18-0.82 = 64% of the canvas"*; the `ManagePanelInsetF = 0.02f` fix has landed on a device.
* **§4a row 1's three named defects all read as fixed:** the cards are portrait-ish, not the 2.2:1
  plaques; all three descriptions render **whole and on two lines** ("Construct and upgrade your town",
  "Train and manage your troops", "Unlock powerful advancements") where the owner's frame showed
  *"Construct and upgrade yo..."*; and **all three cards now carry real art**, closing §5 item 5's
  `hub-build` / `hub-army` / `hub-research` ask. HEART L1 is kept as `UPGRADE HEART / 250 Crystals`.
* ⚠ **What this lane SEES that the owner may not accept, offered as observation and NOT as a verdict:**
  the three cards sit **right of centre**, leaving roughly the left quarter of a now-full-bleed panel
  empty below the Heart chip; and the bottom **CLOSE** plate is very low-contrast against the ground.
  Both are SIZE/STYLE axis material. **The owner judges; this is flagged so the axis is not missed.**

**`Builds/device-frames/2026-09-10_0815_363660_manage_queue_drawer.png`** (2670x1200)
* §4a row 9's chrome defects read as fixed: the **active line has a state** (BUILDERS on a gold plate
  with a left rule, against flat TRAINING / RESEARCH), and the empty state is **per channel and in
  words** - "Nothing is queued on this line. **Tap BUILD** to start something." / "2 slots free - tap
  BUILD to fill them". That is precisely the *"RESEARCH tab telling her to tap TRAIN"* defect, correct
  on the channel this frame shows. CLOSE renders as a full plate here.
* ⛔ **BUT THE QUEUE IS EMPTY (0/2 on all three lines), SO THIS FRAME EXERCISES NO QUEUE ROW.** None of
  §4a row 9's ROW-level fixes - row clipping, full-word CANCEL, the Ad chip, thumbnails, the crystal
  cost collision - are evidenced by it, and neither is WO-1651's refund note. **A device frame with a
  live job is still owed** before row 9 can be ticked from a device.

## 4. ⛔ OPEN ITEMS - what the evidence does NOT cover

1. **§6.0, the owner's 95% x five-axis match.** The ticket's real gate. Not run, not a lane's to run.
2. **§3 step ZERO, `CATALOG_FALLBACK_GEN_OK`, WAS NOT RE-RUN THIS SESSION.** `0` matches in
   `wave5-compile5`, `wave5-reg3`, `wave5-manageflow2` and `apk-build.log`; the only hit anywhere in
   `Builds/` is **`catalog-fallback-gen-current.log`, dated 09-09 13:02** - *yesterday*, before this
   wave. The WO warns in its own words that this step *"comes FIRST and is easy to skip"* and that the
   generated `CatalogFallbackData.g.cs` embeds catalog rows, so a regen after an art wave keeps the
   generated copy and the canonical JSON in step. **It was skipped.** Nothing observed is broken by it -
   and that is not evidence it is fine.
3. **The final step of §3's order, `AAB`, was not run.** No `AAB_OK` and no `.aab` in `apk-build.log`.
   The tester APK path completed; the store artifact did not.
4. **`MANAGE_OPERATIONAL_CAPTURE_OK` was NOT run** - `0` matches across all three wave5 logs. It is a
   **different entry point** and is not part of §3's order, so it blocks nothing here; it is named
   because other Manage tickets (e.g. WO-1406) list it in their own acceptance, and they cannot borrow
   this session's markers for it.
5. **§4a row 3b - PRODUCTION per-hour is still not wired.** The WO records this as an explicit
   HANDBACK, not an omission: there is no single producer for output-at-level-N
   (`ResourceCollector.ThroughputScale` is private and folds live state; `StructureCardVM.UpgradeStats`
   is fixed at level 1 by contract). **Owed: one public producer on `ResourceBuildingProgression`** -
   a `Village/Buildings/Progression` edit, outside the Manage silo. Nothing this session changed it.
6. **The on-device INSTALL and the `dumpsys` version were not verified by this lane.**
   `Builds/overnight-apk-status.txt` carries `APK_START` / `SCHEMA_PARITY_OK` /
   **`APK_OK 08:05:09 size=444MB`** / **`R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL
   objects=279`** / `APK_DONE` - and **no install line**. `Builds/apk-build.log` proves the BUILD's
   identity (`VERSION: name=2026.09.10.363660 code=363660`, `ANDROID_CATALOG_OK ...
   catalog_2026.09.10.363660.bin`) but I found **no `adb install ... Success` and no `dumpsys` read**.
   ⚠ The nine `Builds/device-frames/2026-09-10_08*_363660_*.png` (08:13-08:17, minutes after the 08:05
   build) are strong corroboration that the build reached a device - **but a filename tag is not a
   `dumpsys` read**, and per §11B I am not ticking an install I did not see proven.

## 5. §16 - the content gate, and it is GREEN

`R2_PARITY_OK ... targets=Android,StandaloneWindows64,WebGL objects=279` at **08:05:24**, which
**postdates** `APK_OK` at 08:05:09 - the invariant §16 names (*the proof must postdate the bytes it
claims to prove*). `ANDROID_CATALOG_OK` in `apk-build.log` names
`catalog_2026.09.10.363660.bin`, matching the build's own version stamp, so the APK and the pushed
content are the same wave. **The bundles for this content build were pushed.**

## 6. What the lead does next

1. Put `2026-09-10_0814_363660_manage_hub.png` **beside mockup panel 1** and `_0815_..._manage_queue_drawer.png`
   beside panel 8, and take the owner's ruling on the five axes. That is the only thing that moves this
   to DONE.
2. Capture a queue-drawer device frame **with a live job** so §4a row 9's row-level fixes and WO-1651's
   refund note can be judged on a device rather than inferred.
3. Decide whether the skipped `catalog fallback regen` (open item 2) is re-run before the AAB.
4. Item 5's production producer is a `Village/Buildings/Progression` ticket and wants minting.

**Status flipped to `PARTIALLY DONE - AWAITING OWNER MATCH` in the same edit as this RESULT.**
No code was written for this close-out; no Unity run, no commit.
