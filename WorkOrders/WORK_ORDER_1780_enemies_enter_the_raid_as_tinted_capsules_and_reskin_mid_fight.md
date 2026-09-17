# WORK ORDER 1780 — Enemies enter the Bastion raid as **tinted capsules** and re-skin mid-fight; 48 structure addresses resolve and **not one is resident**

**Status:** BLOCKED - needs data (the capture named in the ticket)

Why NEEDS DATA: the capture to take is named in §5. The seam is understood; what is unproven is whether the bundles were pushed for this build (CLAUDE.md §16) or whether the download is merely slow on the Seeker's link.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** remote content — Addressables / R2 (CLAUDE.md §16) + `StructureContentWarmer`. **No gameplay code, no `.unity`, no bake.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`.

**Video path:** act 2, the first seconds of every engagement. The viewer's first look at an enemy is a coloured pill. P0.

---

## 1. SYMPTOM

The first enemies the player meets in the raid are **tinted capsules**. The real mesh arrives later and the capsule is re-skinned *while the fight is happening*. Structures render as placeholders in the same window.

## 2. EVIDENCE — the device said it in plain words

From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped, 2026-09-16, hero = the Mage):

```
[Flow:Enemy] model 'Orc_Warrior' (id 'orc-warrior') has no renderable mesh at 'Enemies/Orc_Warrior' YET
  — the family 'Orc' bundle is NOT YET DOWNLOADED (familyDownloading=False, catalogState=Ready).
  Spawning the tinted capsule NOW and RE-SKINNING when it lands — deliberately not waiting,
[Flow:Enemy] model 'Orc_Warrior' (family 'Orc', label 'enemyfam-orc', declared=True) is NOT YET DOWNLOADED
  — the enemy spawns and slides for now, and a late bind is armed.
```

and, later in the same run, the recovery:

```
[Flow:Enemy] model 'Orc_Warrior' (id 'orc-warrior') — real mesh, not capsule
```

`Orc_Shaman` / `orc-shaman` produces the same pair (5 occurrences each).

**The structure half is worse, and the code itself names the precedent:**

```
[Flow:StructureAssets] warm pass discovered 48 structure address(es) but NOT ONE is resident.
  Reporting DEGRADED, never Warm. This is the exact 2026-08-20 'pills loading' regression:
  bundles downloaded, assets never loaded, TryGet missed every…
[Flow:StructureAssets] Addressables INIT handle is INVALID after N.Ns: something else owns the
  shared initialisation operation and released it on c…
```

⚠ **`familyDownloading=False` with `catalogState=Ready` is the load-bearing detail.** The catalog resolved, the family was declared, and **no download was in flight** — so this is not "still downloading", it is "nothing asked for it, or the object is not there". That is the CLAUDE.md §16 signature, and §16 records three prior occurrences (2026-08-18, 08-19, 08-20), the last of which the owner met as *"EVERY enemy was a capsule"*.

⛔ **For ENEMIES the §16 "never pushed" hypothesis is RULED OUT, not merely unproven.** `real mesh, not capsule` landing later in the same run proves the Orc bundle **is** in the bucket and reachable; there is also NO `RemoteProviderException` and NO `HTTP/1.1 404` anywhere in the capture (both greps return zero). **So the enemy defect is that the download was never INITIATED before spawn** — which is exactly what the pre-warm in §4.2 targets. The **structure** half (48 resolved / 0 resident, `Addressables INIT handle is INVALID`) is a different and unresolved failure and stands as written.

## 3. WHY THIS IS NOT A DUPLICATE

The tinted-capsule fallback is **deliberate** (`"deliberately not waiting: waiting on this seam is what deadlocked the game on 2026-08-20"`), and it is correct as engineering. **This ticket is not about removing it.** It is about the *video*: the fallback must never be what the camera sees. Two cheap, separable answers exist and neither changes the no-wait rule.

## 4. THE WORK

1. **Prove the bundle state for the filming build first.** Run the one sanctioned path — `tools\r2-ship.ps1` — and judge by **`R2_PUSH_OK` + `R2_PARITY_OK` on a FRESH log, never the exit code** (CLAUDE.md §16, §8; memory `gates-report-success-without-proving-it`). Record the object count and the catalog filename. ⛔ Do not re-inline the push or the verify into any script or chain.
2. **Pre-warm the raid's enemy families and structure addresses during the deploy screen**, not at first spawn. The player sits on `RaidDeployScreen` for seconds before `BEGIN ASSAULT`; that is free download time the game is not using. The roster is already known there (`RaidDeployVM` builds the garrison/boss preview).
3. **`Addressables INIT handle is INVALID … something else owns the shared initialisation operation and released it`** is a distinct defect and must be traced to its second owner before anything else is trusted; the 48-resolved / 0-resident line is its downstream symptom.

## 5. ACCEPTANCE — the capture to take

A fresh Seeker capture of one Bastion raid on the filming build, plus the R2 logs:

1. `R2_PUSH_OK` and `R2_PARITY_OK` on fresh `tools\r2-ship.ps1` logs, with the object count and catalog name quoted.
2. `grep -c "Spawning the tinted capsule NOW"` in the raid window = **0**.
3. `grep "warm pass discovered"` reports residency **> 0**, and no `Reporting DEGRADED`.
4. `grep -c "Addressables INIT handle is INVALID"` = **0**.
5. A screenshot of the first engagement showing real enemy meshes.

## 6. DO NOT TOUCH

The no-wait fallback policy itself. The frame budget (WO-1779 lane). Any `.unity` file. Do not install or distribute through raw `adb install` (CLAUDE.md §16).
