# WO-1434: the Welcome Back screen advertises 42,782 resources, delivers ZERO, and hides the two biggest rows

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:46, build 2026.09.08.361259). PRIOR STATUS: FIXED - ON THE SEEKER 2026.09.07.358574 - landed in `5bc5025f5` (see RESULT); the zero-headroom popup PNG is still owed. The latent Grant remainder defect found while verifying is WO-1445.
**Silo:** Village/Harvest + Core/Economy + the WelcomeBackPopup view. Disjoint from the Manage 2000-block.
**Source:** owner felt-test 2026-09-06 on build **2026.09.06.358161**, two messages:
> *"something seems wrong with the reward for offline"* / *"this screen too. Way too much here"*

**Evidence is CAPTURED, not inferred.** Device screenshot pulled via `adb screencap` at 12:50:59, and the
`adb logcat` window covering the owner's own COLLECT tap at 12:51:25. Both are quoted below verbatim.

---

## 1. WHAT THE SCREEN SAID

```
YOUR REALM WORKED FOR 3h 40m
WOOD  WAITING   +10609
IRON  WAITING    +6365
STONE WAITING   +25808
Storage nearly full - 10609 wood will wait
Storage nearly full - 6365 iron will wait
Storage nearly full - 25808 stone will wait
[ COLLECT ]
```

## 2. WHAT ACTUALLY HAPPENED WHEN SHE TAPPED IT

Captured, 12:51:25, verbatim:

```
[Flow:Bank] BANK FULL [Grant] Stone: requested 25870, banked 0, LOST 25870 (wallet 2000/2000).
[Flow:Bank] BANK FULL [Grant] Wood:  requested 10656, banked 0, LOST 10656 (wallet 8000/8000).
[Flow:Bank] BANK FULL [Grant] Iron:  requested  6393, banked 0, LOST  6393 (wallet 3000/3000).
[Flow:Eco]  Grant +W0 +I0 -> GameState Wood=8000 Iron=3000
```

**Every wallet is exactly at its cap. The player collected nothing.** The screen presented +42,782 as a
reward and delivered 0.

## 3. THE ROWS SHE WAS NEVER SHOWN - THIS IS THE BIGGEST FINDING

Same log line, the modal's own aggregate, verbatim:

```
[Flow:Bank] harvest-result modal OPEN with 5 aggregated resource row(s):
  Wood  granted=0/10656 lost=10656 store=8000/8000 overCap=False |
  Iron  granted=0/6393  lost=6393  store=3000/3000 overCap=False |
  Stone granted=0/25870 lost=25870 store=2000/2000 overCap=False |
  Wood  granted=0/28800 lost=28800 store=8000/8000 overCap=False |
  Iron  granted=0/28800 lost=28800 store=3000/3000 overCap=False
```

**FIVE rows. The screen displayed THREE.** The two omitted rows are the largest in the set - 28,800 Wood
and 28,800 Iron, **57,600 resources**, granted zero and never surfaced to the player at all.

The three displayed rows match the COLLECTOR pendings exactly
(`accrue-pending farm=25870/30000, lumbermill=10656/23040, forge=6393/13824`), so the popup is rendering
collector pendings and is **blind to the offline-harvest grant itself**.

## 4. THE THREE DEFECTS, SEPARATED

**D1 - the headline number is the PENDING amount, not the COLLECTABLE amount.** `PredictCollectWaits`
(`OfflineHarvestService.cs:659-687`) already computes `Headroom` and `Wait` correctly - when headroom is 0,
`Wait == Pending`, which is why each warning line repeats its row's number exactly. The math is right; the
PRESENTATION is wrong. A row whose entire amount will wait is being drawn as a reward with a `+` on it.

**D2 - the screen says the same three things twice.** Three `WAITING` rows plus three
`Storage nearly full - N <res> will wait` lines = six lines for three facts, and the duplication is
literally the same integer printed twice. This is the owner's "way too much here". The fix is one row per
resource that carries both facts, not two stacked lists.

**D3 - two of five rows are not rendered.** Whatever assembles the popup body iterates
`result.PendingCollectors` only. The offline-harvest grant rows never reach the view. **Determine whether
those 57,600 are recoverable or permanently burned** - the collector rows are explicitly safe
(`[Flow:Harvest] ... NOT burned, banks on the next collect once there is room (WO-1392)`), but the
harvest-grant rows go through `ClampGrant` and the bank log calls them `LOST`. **The two subsystems use
opposite words for the outcome and at most one of them is right.** Prove which; do not assume.

## 5. THE LEAD HYPOTHESIS FOR *WHY* THE BANK IS FULL - PROVE OR KILL IT, DO NOT ASSUME

Her caps read **Wood 8000 / Iron 3000 / Stone 2000**. Base cap is 2000. Her `everBuilt` set (captured in
the same log) contains **`lumberyard`, `foundry` AND `silo`**.

So wood and iron are visibly above base, and **stone sits at exactly base 2000 despite a silo appearing in
`everBuilt`.** That is a candidate defect: a built silo contributing nothing to the stone ceiling.

**It is a HYPOTHESIS, not a finding.** `everBuiltStructureIds` is monotonic and records what was EVER
built, never what is currently placed (see the v36 note in the `SaveSchema.CurrentVersion` changelog), so
the silo may have been sold, may be un-upgraded, or may map to a resource other than Stone. Read
`TownBankCapacity.MaxOf`, the `repo.storageResource` mapping and the placed-structure list, and settle it
with captured data. Do not fix a cap without proving the cap is wrong.

Note also the scale: a farm accrued **25,870 stone in 3h40m against a 2,000 ceiling** - twelve times the
entire cap. Even a correct silo may not close that gap. Report the ratio; balance is the owner's call.

## 5B. THE OWNER'S OWN QUESTION, AND THE MEASURED ANSWER

She asked: *"but how am i collecting so many resources over 3 hours"*. Measured from her device, this
session - production rate derived from the captured pendings over the 3h40m (220 min) window, bank caps
read from the `BANK FULL` lines:

| Resource | Production | Bank cap | Time to fill the ENTIRE bank |
|---|---|---|---|
| Stone | 25870/220min = ~7,050/hr | **2,000** | **17 minutes** |
| Iron  | 6393/220min = ~1,740/hr | 3,000 | 1h 45m |
| Wood  | 10656/220min = ~2,900/hr | 8,000 | 2h 45m |

**She is not collecting too much. The bank is far too small for what the buildings produce.** A 3h40m
absence could only ever end in three full bars.

**The structural finding: COLLECTOR capacity and BANK capacity were authored against different
assumptions and nothing reconciles them.** Captured collector capacities are `farm 30000`,
`lumbermill 23040`, `forge 13824` - the farm's collector holds **15x** the entire stone bank. A collector
sized 15x its destination is a buffer that can only ever overflow.

This is the same species as every other defect this program has surfaced: **two things that should be one.**
Whoever takes this must decide whether the fix is a bank ceiling raise, a production retune, or making one
number derive from the other - and must put the recommendation to the owner rather than picking silently.
**Balance is her call; the reconciliation is an architecture question.**

Ruled OUT as the cause, measured: **Echoes are not the driver.** `offline-storage.json` authors
`baseRatePerHourPerEcho: 120` and `siloBaseCapHours: 4.0`; the device shows 4 Echoes owned
(`lanes [wood:1,iron:1,wood:1,food:1]`, `harvestx4.32 applied`). 4 x 120 = 480/hr against a 1,920 silo
ceiling - negligible beside the collectors.

**NOT TRACED, recorded rather than guessed: the source of the two hidden 28,800 rows.** They match neither
the Echo silo output nor any captured collector capacity. `28800` appears in the tree only as
`raidCooldownSeconds` (Hard camp) and a barracks `buildTimeSeconds`, both unrelated. **Find the real
producer before changing anything about those rows.**

## 6. WHAT TO BUILD

1. **One row per resource**, carrying amount and destiny together. When headroom is 0 the row must not
   read as a gain. When headroom is partial, show what banks and what waits, in that one row.
2. **All five rows rendered**, including the offline-harvest grant. A row the modal counts and does not
   draw is the WO-1430 species of defect: a thing that exists with no door.
3. **Correct, non-alarming language.** Collector pendings are NOT lost - say they wait. If D3 proves the
   harvest-grant rows genuinely burn, that is a different sentence and the player must be told plainly.
   Check every string against `FOUNDATIONAL_RULINGS.md` section 7 before writing it.
4. **The owner is red/green colourblind** (memory `owner-colorblind-delegate-visual-creative`). Meaning
   carries in words and layout, never in hue alone. Greyscale check is the gate.

## 7. ACCEPTANCE

- [ ] A regression that MEASURES the popup body at zero headroom and asserts no row presents an
      uncollectable amount as a gain. It must fail against today's build - state its RED proof in-file.
- [ ] A regression asserting the rendered row count equals the aggregated row count. This is the one that
      would have caught D3, and its absence is why five rows became three silently.
- [ ] `OfflineHarvestRegression`'s `[warn-before-collect]` case is re-pointed WITH the copy change, not
      around it. It currently pins the exact string `"Storage nearly full - 414 wood will wait"`
      (`OfflineHarvestRegression.cs:150`). **A pin that requires the old copy is a pin that forbids the
      fix** - move it deliberately and record the ruling in the file.
- [ ] D3 answered with captured evidence: are the 57,600 recoverable or burned?
- [ ] Section 5 settled with data either way.
- [ ] `REGRESSION_OK n/n` plus a headless capture of the popup at zero headroom, **PNG opened**.

---

## 8. IMPLEMENTATION FINDINGS (edit lane, 2026-09-06) — TWO CORRECTIONS TO THIS WO

All settled from the owner's **live device**, `adb logcat` pulled 2026-09-06 ~13:0x with pid 7170 still
running, so the whole 12:50-12:56 window was still in the ring buffer. Lines quoted verbatim.

### ⛔ CORRECTION 1 — the two hidden rows are the ECHO SILO, not the offline-harvest grant.

Section 5B recorded this as NOT TRACED. Traced:
```
12:50:05.591 [Flow:Echo]    claim #1: 'echo-silo' share = 13221s of the 13221s window -> +52884 to silo -> 57600/57600 (echoes 4).
12:50:05.596 [Flow:Offline] accrued over 13221s: worker-owned=0 node(s), total=0
12:51:25.139 [Flow:Echo]    DumpSilos split (pool 57600) by harvest weights [W 7200/I 7200/F 0/G 0/C 0] -> wood 28800, iron 28800, food 0, ...
```
The offline-harvest haul was **ZERO on every axis**, so `OfflineHarvestService.Grant` never ran and the
popup's haul rows correctly drew nothing. Sections 3 and 4's "blind to the offline-harvest grant" is
misattributed. The real defect is one axis over and worse: **the Echo silo had no row on the return
screen AND no term in `HasSummaryContent`** — the largest single thing that happened in her absence
scored on none of the four gate axes and reached her only after the tap, called "lost".

### ⛔ CORRECTION 2 — "Echoes are not the driver" (§5B) is off by ~30x.

Measured: `+52884 to silo` over `13221s` = **~14,400/hr**, not the 480/hr the WO derived from
`baseRatePerHourPerEcho x 4`. The silo alone (57,600, at cap) is **4.4x the entire town bank**
(8000+3000+2000 = 13,000). The Echoes are the single largest producer in her town, and because the
silo is pinned at its ceiling they are currently producing **nothing**.

### ✅ D3 ANSWERED — the 57,600 are RECOVERABLE. Nothing burned, on either producer.

1. `12:51:25.147 [Flow:Harvest] silo dump: 28800 wood stayed in the silo - Wood storage full` (+ iron).
2. Code: `EchoService.DumpSilos` settles `s.SiloResources -= bankedFromSilo` (the applied basket,
   WO-1392), not `-= pool`.
3. **Empirical, across three taps:** `pool 57600` at 12:51:25, `pool 57600` at 12:56:03, `pool 57600`
   at 12:56:06. Two dumps consumed nothing.
4. Collectors likewise grew rather than reset: wood `10656 -> 10776`, stone `25870 -> 26026`.

**So the bank is the one using the wrong word.** `BANK FULL ... LOST N` is the BANK saying it *refused*
the units; retention is the caller's business, and **both live callers retain**. The modal's
"they are lost" was false for the silo and has been fixed.
*Latent, NOT fixed (out of scope, and dead on this save): `OfflineHarvestService.Grant` genuinely does
discard — it banks the clamped value and drops the pre-clamp accrual. It accrued `total=0` here.*

### ✅ SECTION 5 SETTLED — the silo hypothesis is KILLED. Do not touch the cap.

```
12:51:25.115 [Flow:Bank]      MaxOf(stone) = base 2000 + containers 0 across 0 built container(s) = 2000.
12:51:25.121 [Flow:Bank]      MaxOf(wood)  = base 2000 + containers 6000 across 2 built container(s) = 8000.
12:51:25.131 [Flow:Bank]      MaxOf(iron)  = base 2000 + containers 1000 across 1 built container(s) = 3000.
12:50:14.771 [Flow:Singleton] blank-town 'lumberyard': ... a PLACED 'lumberyard' owns the singleton
12:50:14.772 [Flow:Singleton] blank-town 'foundry':    ... a PLACED 'foundry' owns the singleton
12:50:14.773 [Flow:Singleton] blank-town 'silo': migrated=True everBuilt=True maySurface=True twins=[<none>] -> None
```
`lumberyard` and `foundry` each report a PLACED instance; **`silo` reports none.** The catalog mapping
is correct at source — `silo.repo.storageResource = "stone"`, `TownBankCapacity.WordOf(Food) = "stone"`,
`storageCapacity 1000`. The silo is in `everBuiltStructureIds` (monotonic) and **not in `BaseLayout`**.
Stone reads base 2000 because she has no Stoneyard placed. *(Wood's 2 containers / 6000 is the WO-2005
grandfathered double-container case, working as authored.)*

### ⚠ BALANCE — a recommendation, not a change. NOTHING WAS RETUNED.

Two capacity systems sized against different bases, with nothing reconciling them:
| | Sizing basis | Her values |
|---|---|---|
| Collectors | hours of production | farm 30000, lumbermill 23040, forge 13824 |
| Echo silo | `siloCapHours x TotalHarvestRatePerHour` | 57600 |
| Town bank | container ladder (`baseCap + storageCapacity x level`) | 8000 / 3000 / 2000 |

**Buffers total 124,464 against a 13,000 bank — 9.6x.** Every buffer can only ever end full.
**Recommendation: make the bank ceiling DERIVE from the buffers** (or the buffers from the bank) so one
number moves both — the same "two things that should be one" cure this ticket is made of. Raising the
ladder alone re-opens the WO-1128 §3.5 clock-forge bound in proportion. **Owner's call; not taken here.**

### Files changed (edit-only lane; no gate, no regression run, no commit)
`EchoService.cs` (split extracted to a pure `SplitPool` + `PredictDumpSplit` + `SiloAtCap`) ·
`OfflineHarvestResult.cs` (silo axis + fifth gate term) · `OfflineHarvestService.cs`
(`AttachSiloPending`, `BuildReturnRows`/`ReturnRowLabel`/`ReturnRowDestiny`/`ReturnFooterLine`/
`SiloStalledLine`; `PredictCollectWaits`/`CollectWaitLine` retired) · `WelcomeBackPopup.cs` (one row
per resource, all producers, COLLECT gate widened to the silo) · `BankOverflowToastPresenter.cs`
(stamps the warn-scope source onto the rows) · `HarvestOverflowModal.cs` (silo branch) ·
`OfflineHarvestRegression.cs` + `HarvestResultCopyRegression.cs` (**two** pins moved — see below).

### The pins that forbade the fix — BOTH moved, rulings recorded in-file
1. `OfflineHarvestRegression` case 4 pinned `"Storage nearly full - 414 wood will wait"` **and** pinned
   the popup for `PredictCollectWaits(_result)` / `AddCollectWaitRows`. Together they *required* the
   duplicated second list. Re-pointed at `BuildReturnRows`; every real assertion (pure, headroom-driven,
   rail order, no false alarm, ASCII) survives, plus new `[no-gain-without-headroom]` and
   `[every-producer-rendered]` cases.
### Known limits of this lane — stated, not hidden
- **The new cases cannot reproduce the behavioural RED.** They pin the PURE model plus popup source
  text; the popup is code-built and cannot be constructed headless. In-file RED proof is "does not
  compile against the pre-WO-1434 tree" plus the cited `"+" + line.Pending` line. **Acceptance item 6
  (headless capture, PNG opened) is the lead's gate and is what actually proves the rendered body.**
- **Over-cap is not distinguished from full on this screen.** `TownBankCapacity.RoomFor` returns 0 for
  both, so a paid over-cap balance reads "STORAGE FULL". Ruling 7 separates them and the modal/toast
  already do. Pre-existing; not widened, not fixed here.
- **`AwayTextFor`'s "(STORAGE FULL)" suffix names storage for a TIME cap** (`WasCapped` =
  `window.ExceedsCap(OfflineCapHours)`). Pinned by `AwaySummaryReportRegression` line 243, so it was
  left alone rather than half-moved; the unpinned body line under it ("Check in sooner to keep every
  reward" — false loss language) IS fixed. The suffix needs its own pin move.
- **No `.json` was touched**, so there is nothing to newline-verify.
- **Canon:** `EchoService.SplitPool` / `PredictDumpSplit` / `SiloAtCap` and
  `OfflineHarvestService.BuildReturnRows` + friends are new public API — `docs/MASTER_CATALOG` Harvest
  needs the same-commit update (§15).

2. `HarvestResultCopyRegression` asserted a silo row must say *"were not added to storage"*, commented
   *"the silo dump still burns its overflow today and must keep saying so"*. **That was already false
   when written** — WO-1392 had made the silo retain. The assertion is **inverted**, not deleted.
