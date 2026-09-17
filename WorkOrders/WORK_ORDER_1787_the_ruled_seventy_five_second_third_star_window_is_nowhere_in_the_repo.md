# WORK ORDER 1787 — The "T3 = 75 s" third-star window is **nowhere in the repo**; the device receives **90 s**, and the owner's Bastion run settled at 2 stars

**Status:** BLOCKED - needs data (the capture named in the ticket)

Why NEEDS DATA: an owner ruling on the number. The code seam is fully identified (§3); what is missing is which value is canon, and 3 stars is the gate on the entire capture beat.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** raid honor tunables — `Assets/_Modules/Core/Ops/RemoteTunables.cs` + the remote tunables rail. **No UI, no `.unity`, no bake.**

**Build under test:** `2026.09.16.371701`, built 2026-09-15 22:06 from `f6653501f`.

**Video path:** act 2 shots 11-14. **Capture requires a 3-star clear** (`Assets/_Modules/Core/State/OwnedBaseProgression.cs:20` `CaptureStarsRequired = 3`, gate `:31-32`). If the third-star window cannot be met on camera, the video has no act 2 second half. P1, and it gates a P0.

---

## 1. WHAT THE CAPTURE SHOWS

From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped, 2026-09-16):

```
[Flow:Raid] honor milestones REFRESHED at tunables generation 25: T3=90s T2=150s D2=50%.
[Flow:Raid] honor milestones REFRESHED at tunables generation 26: T3=90s T2=150s D2=50%.
[Flow:Raid] honor milestones REFRESHED at tunables generation 27: T3=90s T2=150s D2=50%.
[Flow:Raid] veterancy: 2 star(s) - no ranks granted (3 stars required).
```

The rail is live and remote (`[Flow:Tunables] CONFIG (payload accepted…) tableProvenance=remote`), and at **three separate generations it delivered T3 = 90 s.** The owner's run did not make 90 s, so she settled at **2 stars** — and capture was therefore never attempted in the only Bastion run captured this week.

## 2. THE CLAIM THIS TICKET EXISTS TO CORRECT

The raid-polish brief carried *"T3 now 75 s via DB"* as a statement of current state.

⛔ **75 s is not in this repository as an honor value, and it is not what the device received.** Proven by grep at source, 2026-09-16:
- the authored default is `RemoteTunables.RaidHonorThirdStarSecondsDefault = 90` — `Assets/_Modules/Core/Ops/RemoteTunables.cs:862`;
- the key is `"raid.honorThirdStarSeconds"` at `:871`, registered as a `TunableSpec` at `:1754`;
- the only nearby `75` is `RaidLootTwoStarPctDefault = 75` at `:349` — an unrelated loot percentage. **Do not confuse these two.**

⚠ `RemoteTunables.cs` is **MODIFIED-UNCOMMITTED** in the working tree (`git status --short`); the readings above are working-tree values.

This is recorded as a finding, not a defect: per CLAUDE.md §11B a number copied from a doc is hearsay until re-read at source, and this one did not survive the re-read.

## 3. THE SEAM — so the ruling can be applied in one line

- Thresholds read: `Assets/_Modules/Village/Troops/RaidScoring.cs:170-180` (`HonorThirdStarSeconds`) and `:186-196` (`HonorSecondStarSeconds`); cached once per raid at `:1598-1599`.
- The snuff, in the pure projector `ComputeHonorStars`: `RaidScoring.cs:761-764`
  `if (heroDied || elapsedSeconds > thirdStarSeconds) stars = Mathf.Min(stars, 2);` then `:765-767` for T2.
- ⚠ **`heroDied` also caps at 2 stars.** In the captured run the hero *did* go down (`[Flow:EndState] hero death in LIVE raid scene 'RaidBase_IronBastion'`), so **it is NOT proven that the clock is what cost her the third star** — either condition alone caps it. The capture in §5 must separate them.
- To change T3 in code: `RemoteTunables.cs:862`. To change it live: a remote row on `raid.honorThirdStarSeconds` — which is the path already in use.

## 4. THE QUESTION FOR THE OWNER

Honor milestones are tunable by her ruling. For the filming build:

1. **Is T3 = 90 s, or 75 s, or something else?** (Lowering it makes 3 stars *harder*; raising it makes the capture beat filmable.)
2. **Should the hero-death cap apply for the video?** It currently caps at 2 stars unconditionally, so a single death anywhere in the run forfeits capture.
3. **Should the camp-specific difficulty land first?** WO-1763 (`2e34dc3da`, per-camp remote raid difficulty, Bastion seed ruled 160% / offset 5) is `IMPLEMENTED, NOT YET GATED` and **not in the measured build** — it changes how hard 90 s is.

## 5. ACCEPTANCE — the capture to take

One Bastion raid on a build containing `2e34dc3da`, capturing:
1. the `honor milestones REFRESHED … T3=` line, to prove which value the device actually received;
2. the settle, with `elapsedSeconds` and `heroDied` both quoted, so the cause of any cap is unambiguous;
3. `veterancy: 3 star(s)` — or, if not, the exact margin.

Then apply the ruling at `RemoteTunables.cs:862` or on the rail, and re-capture. Judge by the marker on a **fresh** log, never an exit code.

## 6. DO NOT TOUCH

`RaidLootTwoStarPctDefault` (`:349`). The victory screen's captions (WO-1789 lane). The capture gate itself (WO-1778). Any `.unity` file.
