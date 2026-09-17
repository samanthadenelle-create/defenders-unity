# RAID POLISH PROGRAM — 2026-09-16

**Owner directive (verbatim, 2026-09-16):** *"I want the entire raid polished from start to engage enemy to everything"* — for the Clock In hackathon video (`docs/HACKATHON_CLOCK_IN_SUBMISSION_PLAN_2026-09-16.md`, act 2: raid → 3-star Iron Bastion clear → capture → town → practice → arena future-state mock). Hero = **the Mage**. Tester build. Two-week window to ~09-30. **Video + bugs only.**

**Lane:** read-only audit. This document and `WorkOrders/WORK_ORDER_1777..1790` are the only files it wrote. No code, no commits, no Unity, no device taps. `CLI_LANES_WO_NUMBERS.md` **not touched** — the block 1777-1790 was pre-assigned by the lead.

---

## 0. ⛔ READ THIS BEFORE ACTING ON ANY ROW BELOW — THE BUILD BOUNDARY

**Build under test = `2026.09.16.371701`**, `ProjectSettings/ProjectSettings.asset:148` (read at source), stamped in commit **`f6653501f`** (2026-09-15 22:02), APK built **2026-09-15 22:06** (`Builds/apk-build.log` mtime). Confirmed against the capture: the only app-version string in the 377 MB logcat is `build=2026.09.16.371701`.

**The capture window is 09-15 11:19 → 09-16 14:31. The 09-16 15:33 commit wave landed AFTER it.** So these are **NOT** in the build the owner played, and every "still broken" observation about them is **pre-fix evidence, not a live defect**:

| Not in build 371701 | commit |
|---|---|
| WO-1763 per-camp remote raid difficulty | `2e34dc3da` |
| WO-1764 troops prefer reachable units over walls | `ed3fd109b` |
| WO-1765 raid over-the-shoulder camera profile | `abba2d89e` |
| WO-1767 Bastion capture census / stable identity + **the scene re-bake** | `bca258130` |
| WO-1768 hero death inside the down-beat | `0da7af499` |
| WO-1703 / 1704 raid floors + continuity, WO-1705 owned town | `3b2ba64b2`, `1fac4dde3` |

**In build 371701** (all committed 09-15 ≤ 22:02): WO-1730 `ba13b93ef`, 1746, 1748-1752, 1756, 1758 `fdba44553`, 1759, 1760, 1761, 1762 `3369a3f11`, 1747 (Cleric half).

⚠ **Memory rule `layout-tickets-need-a-fresh-capture` applies to this whole document.** Before any lane below is worked, take one fresh Bastion capture on a build containing the 09-16 wave. Several rows may close for free.

---

## 1. BEAT-BY-BEAT

| # | Beat | Existing tickets (cite, do not duplicate) | New | Status |
|---|---|---|---|---|
| 1 | **Raid door** — Journey/Raids selection | 1519 IMPLEMENTED | — | ✅ **Clean.** No placeholder/TODO/lorem/raw-id string found across `RaidSelectionScreen.cs` (1305 lines) + `RaidSelectionVM.cs` (1069). Card names fall back to `SpacedDisplayName`, never a raw id (`:912-913`). CLEARED marker **is** wired on the live path (`:474-475`, present at `f6653501f`). Heartfire refusal names the recharge clock (`HeartfireCharges.cs:542-556`). Difficulty badge shows (`:920-933`) — from the **old authored** `SceneConfigDef.difficulty`, not WO-1763's rail. |
| 2 | **Deploy screen** — army tray, scout report | 1464, 1519 IMPLEMENTED | — | ✅ **Clean.** No placeholder string. A troop owned 0 of is simply absent, by design (`RaidDeployVM.cs:663-699`). "Recon" clear-time is a **static** authored estimate, and the file records that as a known gap in its own header (`RaidDeployScreen.cs:37-39`) — not a silent defect. `RowHeightPx = 60f` (`:71`) is under `MinTouchPx (112)` but the rows carry **no** Button and every child has `raycastTarget=false` — **display only, not a tap target.** Parked. |
| 3 | **Scene entry** — carry-across, seat, staging, 180 s gate, readout | 1520, 1464, 1639 IMPLEMENTED | — | ✅ **Working as designed.** Hero is seated at `RaidStagingPoint`, **preferred over** `HeroStartPoint` (`HeroControlEnsurer.cs:687,693-694`); staging = `max(towerMaxReach, ring + 20) + 6 m`, due south with an SW-diagonal fallback (`RaidBaseGenerator.cs:1037-1092`). Clock `DefaultClockSeconds = 180f` (`RaidScoring.cs:134`); `_engaged` has one writer, `NotifyEngagement` (`:1653-1663`), four predicates (`:1704/1715/1723/1734`). Readout is reference-pixel disciplined (`RaidHudController.cs:446-456`). |
| 4 | **Approach + first engagement** — breach, troop AI, targeting, camera, walls, turrets | 1723 (Lane C re-bake **ship-blocking**), 1722, 1730, 1746, 1748, 1749, 1752, 1756, 1764, 1765, 1770; **1504 contested** | **1777**, **1785**, **1790** | 🔴 |
| 5 | **The fight** — Mage VFX/SFX, damage numbers, stars, hero-down | 1776, 1526 CLOSED, 1750, 1768, **888** (`HeroHpStateAura` — the greyscale-surviving low-HP read) | **1781**, **1786**, **1787** (~~1782 retracted~~) | 🔴 |
| 6 | **The win** — spire raze, victory screen, spoils, cache, veterancy | 1543 IMPLEMENTED, 1761, 1768 | **1789** | 🟡 |
| 7 | **Capture → owned town** — census, receipt, reveal, repair FTUE | 1705 IN PROGRESS, 1767 | **1778**, **1783**, **1788** | 🔴 |
| 8 | **Practice arena** | 1705 | (1788 — untaught) | 🟡 |
| 9 | **Return home** | — | (1778 — the strands) | 🔴 |
| — | **Cross-cutting: raid art + frame budget** | 1751, 1757 INVALID, 1758, 1760 CLOSED, 1747 | **1779**, **1780**, **1784** | 🔴 |

---

## 2. THE 14 MINTED TICKETS — **13 ACTIONABLE, 1 RETRACTED** — IN PRIORITY ORDER

P0 = visible in **every** take of the video.

| # | P | Title | Status |
|---|---|---|---|
| **1777** | P0 | A press on the **ability row** is also consumed as a world tap — the UI guard can only see the controller's own canvas (harmless with Breach armed; **deploys a troop** with Deploy armed) | READY TO IMPLEMENT |
| **1778** | P0 | A 3-star capture **strands the player on the victory screen**; the retry has zero callers | READY TO IMPLEMENT |
| **1779** | P0 | The raid runs **18-27 fps**, and `TownActivityProbe` still ticks inside the raid scene | READY TO IMPLEMENT |
| **1780** | P0 | Enemies enter as **tinted capsules** and re-skin mid-fight; 48 structure addresses, **none resident** | NEEDS DATA |
| **1781** | P0 | The Mage kit is **silent** — no ability of any class can carry an SFX | NEEDS DATA |
| ~~**1782**~~ | — | ~~The hero taking damage shows no number and no hit feedback~~ — ⛔ **RETRACTED same-day: the premise was false.** The hero already gets impact VFX, haptics, a hit sound, screen shake, a full-screen damage flash and a low-HP vignette (`HeroHealth.cs:966-977`, `HeroHitReaction.cs`), and the colourblind read is solved by WO-888's `HeroHpStateAura`. **Do not dispatch.** Number spent. | CLOSED - INVALID |
| **1783** | P0 | The capture beat has **no moment**: "it is yours now" is every raid's subtitle | READY TO IMPLEMENT |
| **1784** | P0-enabling | **412 of 1439** material slots in the Bastion raid have no albedo; the census names **12** | READY TO IMPLEMENT |
| **1785** | P1 | The raid camera's occluder pull-in **flaps 42 times in ~100 s** | NEEDS DATA |
| **1786** | P1 | Raid VFX loops stay held off-camera for 89 s; WO-1473's release policy says so itself | READY TO IMPLEMENT |
| **1787** | P1 | The ruled **75 s** third-star window is **nowhere in the repo**; the device receives **90 s** | NEEDS DATA |
| **1788** | P1 | The FTUE "skip all" **erases** the owned-town teaching; practice is untaught | READY TO IMPLEMENT |
| **1789** | P2 | Veterancy denial and the Raid Cache exist **only in the log** | READY TO IMPLEMENT |
| **1790** | P1 | The breach-tap diagnostic **draws an invalid inference in its own message** | READY TO IMPLEMENT |

**Numbers from the block NOT used: none. The whole block 1777-1790 is spent.** The next raid-polish ticket needs a fresh number from the `CLI_LANES_WO_NUMBERS.md` banner.

---

## 3. SUGGESTED LANES — file-disjoint groups, run in parallel

| Lane | Tickets | Files it owns |
|---|---|---|
| **A · raid input** | 1777, 1790 | `_Modules/Village/Troops/RaidDeployController.cs` **only** — same file, so **ONE agent for both** |
| **B · raid exit + owned town** | 1778 | `RaidVictoryController.cs`, `OwnedTownPanel.cs`, `Core/SceneRouter.cs`, `OwnedBaseProgression.cs`, `RaidCaptureCensus.cs` |
| **C · frame budget** | 1779 | `Village/World/TownActivityProbe.cs`, `Hero/HeroLocomotion.cs` (measure-only) |
| **D · remote content** | 1780 | Addressables settings, `StructureContentWarmer`, `tools/r2-ship.ps1` runs — **no `.cs` gameplay** |
| **E · ability audio** | 1781 | `Hero/AbilityCatalog.cs`, `abilities.json` + twin, `motion-castings.json` |
| ~~**F · hero feedback**~~ | ~~1782~~ | ⛔ **STRUCK — 1782 retracted, premise false. No lane, no agent.** |
| **G · capture presentation** | 1783 | `EndState/EndStateVM.cs`, `OwnedTownController.cs`, `en.json` |
| **H · raid art diagnostics** | 1784 | the `[Flow:RaidArt]` census emitter |
| **I · camera** | 1785 | the occluder contract — ⚠ **shares files with WO-1765/1770/1771; one agent at a time** |
| **J · VFX loop budget** | 1786 | the `[Flow:Vfx]` loop registry (WO-1473's owner) |
| **K · tunables** | 1787 | `Core/Ops/RemoteTunables.cs` + the rail — **owner ruling first** |
| **L · tutorial** | 1788 | `Tutorial/V2/TutorialFlow.cs`, `tutorial-steps.json` |
| **M · victory captions** | 1789 | `EndStateView.cs` star row, `RaidDeployController.cs:1886` — ⚠ **1783 §4.2 also wants the star row; coordinate** |

**Two collision points, stated once:** lanes **A** share one file (1777 + 1790 = one agent); lanes **G** and **M** both want the star row. Everything else is disjoint.

**Sequencing:** run **D** (prove the R2 push) and **C** (the probe) first — they are cheap and they change what every subsequent capture shows. Then **A**, **B**, **H**. **I**, **K** need a fresh capture / a ruling before code.

---

## 3B. ⚠ SCOPE FLAG FOR THE LEAD — two of the fourteen are not BUG or POLISH-IN-VIDEO-PATH

The brief allowed **BUG**, **POLISH IN THE VIDEO PATH**, or **park**. Two minted tickets are **process / instrument** work and the lead may want to re-park them if the block was meant for video items only:

- **1790** — the breach-tap diagnostic states a diagnosis it has not measured. Not player-visible. Minted because it **already cost this lane a wrong P0 today** (§5), and CLAUDE.md §12 makes every other fix depend on the instrument being truthful.
- **1784** — half of it is player-visible art (412 untextured slots in a scene the camera pans across) and half is **diagnostic widening** (the census names 12 of 412). The art half cannot be prioritised until the diagnostic half runs, which is why they are one ticket rather than two.

Everything else — 1777, 1778, 1779, 1780, 1781, 1783, 1785, 1786, 1787, 1788, 1789 — is a BUG or video-path polish. **1782 is retracted** (§2).

---

## 4. ⚠ CLAIMS THIS LANE COULD NOT VERIFY — do not act on these as facts

1. **The exact ability FACE at x≈820, y≈100 is unnamed; its BAND is proven.** `RaidDeployController.cs:1923-1932` measures the kit ability row at `Y 0.015-0.150` = **y 18-180** of 1200, and the 40 taps sit at y 78-130 — inside it. The virtual joystick is **ruled out by arithmetic** (zone r = 326.4 px about (259.2, 259.2); the taps start at x = 774 where the zone reaches x ≈ 547). Which face it is needs **one in-game screencap at 2670x1200** — the only PNG in the pull directory (`Logs/device/scrcpy-proof-20260916-143307.png`) is the Android home screen. Not required to implement 1777; required to close it cleanly.
2. **`FireSpellOrb -> … hovl=<none>` on 47 of 54 Mage casts.** The capture shows this, and `hovl=PP_FireBall` on the other 7. **NOT minted as a ticket** because most Mage abilities legitimately carry no `vfxProjectile` key, so `<none>` may be correct — and `PooledProjectile` builds its own visual in `Awake`. Whether the orb is actually invisible on camera needs one screenshot of a Mage cast. Recorded here so it is not lost.
3. **`SpawnImpact element=None` ×205 vs `element=Aether` ×47.** `ImpactPath` (`ProjectileVFXCatalog.cs:75-81`) routes `None`/`Physical` to `Explosion_Storm` while `Explosion_Arcane`/`_Fire`/`_Ice` exist. Whose projectiles the 205 are was **not** established — `ArcaneTower.cs:631` and `Enemy.cs` are the launchers, and `towers=0` in the raid. Not minted.
4. **The tester video (`Logs/device/owner-video/DefenderDemoRun.mp4`) was NOT watched by this lane.** WO-1774 already owns its one raid-adjacent finding (an untextured player-built wall) and is `NEEDS DATA` on the tester's build string. No raid claim here rests on that video.
5. **WO-1504 is LIVE for the Mage and unruled.** `TryGetRangedPrimary` returns **true** for the Mage: locked Q is `mage.fireball`, effect `strike`, range 14 m against `meleeReach*2 = 6.4 m` (`HeroAbilities.cs:499-515`, `:479`; `PlayerAttackController.cs:921-922` — ⚠ that file is under `_Modules/Village/Enemies/`, not `/Hero/`). **So the Mage's primary attack button fires Fireball, not the melee sweep**, which contradicts CLAUDE.md §7's prose. For a Mage-led video that is arguably the *right* behaviour — **but it is the owner's ruling, not this lane's**, and no ticket was minted on it.
6. **No turret telegraph exists.** `DefenseTower.UpdateEnemyOwned` (`:698-711`) fires in the same frame the cooldown expires (`:706-710`); the only tell is a continuous aim beam (`:1399-1412`). A pre-fire tell is a **NEW FEATURE**, so per CLAUDE.md §13 it goes back to the PO as a spec, not an RCA. **Not minted.**
7. **The staging SW-diagonal fallback logs a warning that the approach no longer lines up with the south gate.** Worth an owner felt-check; no defect proven. Not minted.
8. **`RaidSelectionVM: no ClaimedProvider wired (expected headless / EditMode)` fires twice on a real device** — `BuildTimerService.PublishJourneyOpenCamps` (`:2507`) builds a throwaway VM on a 1 Hz heartbeat before the screen is ever opened. **No player impact** (that VM renders nothing); the trace's own "(expected headless)" text is simply wrong on device. Log hygiene only. **Not minted** — the block was full.
9. **`Assets/_Modules/Village/Hero/HeroAbilities.cs`, `abilities.json` (+ StreamingAssets twin), `RemoteTunables.cs` and `Assets/Editor/Regression/OverTimeEffectRegression.cs` are MODIFIED-UNCOMMITTED.** Every line number cited against them in 1781/1787 is against the **working tree**, not HEAD.
10. **WO-1723's Status may itself be stale.** §1 beat 4 cites it as *"LANE C RE-BAKE OUTSTANDING (§11.6, ship-blocking)"*, which is what its `**Status:**` line still says — but `bca258130` (2026-09-16) rewrote `RaidBase_IronBastion.unity` and the tree now carries 158 `WallSegment` / 174 `Clad_*` / 158 `Ruin_` / 168 `NavMeshObstacle`, i.e. the Lane B partition **is** in the scene. **Whether Lane C is still outstanding was NOT verified by this lane** — the ship-blocking citation is repeated from the ticket, not re-proven. Check before treating it as a blocker.
11. **`RaidBase_IronBastion.unity` in the working tree is NOT the scene in build 371701** — it was rewritten by `bca258130` (mtime Sep 16 15:10). The 158-wall / 174-clad census in WO-1777 §3 describes the **tree**. Do not compare owner-felt behaviour in that APK against this tree's scene.

---

## 5. WHAT THIS LANE GOT WRONG, on the record

WO-1777 was first written on a false premise: the raw counts (40 failed taps, 1 success) plus `colliderBounds Extents (1.96, 2.00, 0.75)` against `rendererBounds Extents (4.12, 2.00, 1.73)` were read as "the wall's collider is half the visible wall". **Both halves were wrong.** The collider matches its clad panel to 0.02 m (`RaidBaseGenerator.cs:2436` writes it, `RaidBaseDresser.cs:755` reads it back and fits the panel to `segW + 0.02`); the 8.24 m "rendererBounds" is a `GetComponentsInChildren<Renderer>(true)` union including the inactive placeholder twin and the `Ruin_*` tiles. **WO-1723 §6 had already retired that exact reading nine days earlier.** The real cause was in the screen coordinates the same log lines already carried.

This is the CLAUDE.md §11B failure in miniature — a real measurement used to support a conclusion it did not support — and it is why **WO-1790 exists**: the instrument that produced it states a diagnosis it has not measured, and an instrument that lies is worse than none.

**And it happened a SECOND time, which makes the pattern the finding rather than the incident.** WO-1782 claimed the hero *"shows no floating number and no hit feedback of any kind — no flash, no vignette, no shake, no hit sound."* **All of it false.** `HeroHealth.TakeDamage` carries a block headed *"Combat feel (additive)"* (`:966-977`) firing impact VFX (`:971`), haptics (`:972`), `GameSfx.PlayHeroHit()` (`:973`) and `HitStopManager.DoImpact` (`:977`); `HeroHitReaction` adds a full-screen damage flash on any HP decrease (auto-attached `HeroControlEnsurer.cs:642`, `HeroHealth.cs:2413-2414`); `HeroInjuredVignette` adds a low-HP edge cue (`HeroHealth.cs:298`). The accessibility half I was about to raise is solved too — WO-888's `HeroHpStateAura` carries the low-HP read on pulse rate, guttering depth, shape and motion direction *because* the owner is red/green colourblind, and says so in its own header (`HeroHpStateAura.cs:6-30`). **WO-1782 is CLOSED - INVALID and its lane is struck.**

**The mechanism, because it is the reusable lesson:** I searched seven guessed *names* (`heroDamaged|OnHeroHit|HeroTookDamage|damageFlash|hurtFlash|HeroHurt|hitFlash`), none of which is the name of any of the six real systems, and then reported that search as *"verified by TOKEN across the whole repo"* (memory `search-by-token-not-by-name`) when it was neither a token sweep nor capable of finding the thing.

⛔ **An absence proves nothing until the search that produced it is shown able to find the thing.** A zero-hit grep is a claim about the **pattern**, not about the **repo**. Both of this session's retractions are the same error in different clothes: evidence stated at a wider width than it supports.
