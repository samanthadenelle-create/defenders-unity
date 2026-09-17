# WORK ORDER 1783 — The capture beat has **no moment**: "it is yours now" is every raid's subtitle, and the inherited town opens on a modal

**Status:** READY TO IMPLEMENT

Scope note: the seams are named; the copy and the shape of the reveal are an owner call — §4 holds them.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** capture presentation — `Assets/_Modules/Village/UI/EndState/EndStateVM.cs`, `Assets/_Modules/Village/World/Camps/OwnedTownController.cs`, `Assets/Resources/Data/Canonical/en.json`. **No `.unity`, no bake, no combat, no route logic (WO-1778 lane).**

**Build under test:** `2026.09.16.371701`, built 2026-09-15 22:06 from `f6653501f`. WO-1705 (`1fac4dde3`) and WO-1767 (`bca258130`) landed 2026-09-16 15:33 and are **not** in it.

**Video path:** act 2 shot 12, *"The town is yours"* — the pivot the whole submission is built around. P0.

---

## 1. SYMPTOM

The game's biggest promotion — the player inherits a town — is delivered by **a subtitle every raid already shows and one changed button label.** There is no dedicated screen, no receipt, no reveal. She taps the button, the screen crossfades, and a modal panel is already up on frame one.

## 2. EVIDENCE — read at source 2026-09-16

**2a. The "yours now" line is unconditional and fires on a 1-star camp clear too.**

`Assets/_Modules/Village/UI/EndState/EndStateVM.cs`, `FromRaidVictory` (declared `:414`):
- `:432` `Title = "Victory!"`
- `:425` `Subtitle = "The base is CLAIMED - it is yours now."` — **no capture condition on this line**
- `:426` appends `destructionPercent + "% razed."`

**2b. The ONLY capture-specific change on screen is a button label.** `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:952-956` sets `vm.PrimaryLabel = LocalText.Get("ownedTown.enter")` → *"Enter your town"* (`Assets/Resources/Data/Canonical/en.json:13`) and `vm.PrimaryGate = CanEnterCapturedTown`. **No title change, no new band, no receipt, no star callout tied to capture.**

**2c. The 3-star requirement is never stated to the player.** `OwnedBaseProgression.cs:20` `CaptureStarsRequired = 3`; the gate is `:31-32`. A grep of `en.json` for the 32 `ownedTown.*` keys (`:4-32`, `:457-467`) finds **no string containing "captured", "receipt", "inherit" or "3 star"** on any player-visible surface.

**2d. There is no reveal.** `RaidVictoryController.cs:1044` → `SceneRouter.GoOwnedTown()` → `Assets/_Modules/Core/SceneRouter.cs:604-611` `LoadSceneWithFade(OwnedTownIronBastion, beforeLoad: CarryHeroAcrossSingleLoad)`. **A crossfade is the entire presentation.** Then `Assets/_Modules/Village/World/Camps/OwnedTownController.cs` — `[DefaultExecutionOrder(-500)]`, `Start()` reconstructs at `:22` and at `:36-40` immediately calls `panel.Show()`. No camera pan, no timeline, no sequenced VFX anywhere on this path.

⚠ **Nothing here is placeholder or debug copy** — the victory path carries no TODO/lorem strings (checked). The defect is that the marquee beat was never given a surface, not that its surface is wrong.

## 3. WHY THIS IS NOT WO-1705 OR WO-1778

- **WO-1705** (`**Status:** IN PROGRESS`) built the owned-town *contracts* — reconstruction, repair, move, practice. Its own RESULT lists what it does not claim; presentation of the capture moment is not in it.
- **WO-1778** is the *route* — that a refused capture still has an exit. This ticket is the *moment*, and the two must not be merged: one is a softlock, one is a feeling.

## 4. THE WORK — and what is HELD for the owner

**Clear from source (do this):**
1. Make `:425` **conditional**. A non-capture raid victory must not claim the base is claimed; give the ordinary clear its own honest subtitle and reserve the claim line for an actual capture.
2. **State the gate before it bites.** Somewhere the player sees the 3-star requirement *before* she settles — the deploy screen or the victory screen's star row is the natural home. Today she can 2-star the final raid and never learn why nothing happened. (⚠ This also touches WO-1789's lane — coordinate; do not both edit the star row.)
3. `OwnedTownController.cs:36-40` — **hold the panel** for a beat so the first frame of her town is her town, not a modal over it.

**HELD for the owner — do not invent these** (memory `vfx-map-owner-tags-no-creative-pick`, `owner-colorblind-delegate-visual-creative`):
- the capture screen's title and body copy;
- whether the reveal is a camera pan, a held wide shot, or a sequenced VFX beat, and how long it runs;
- whether a "capture receipt" is a real surface or the victory screen simply says more.

## 5. ACCEPTANCE

- A device **screenshot** of a 1-star camp clear proving the subtitle no longer claims the base.
- A device **screenshot sequence** of a 3-star Bastion capture: the capture surface, then the first frame of the owned town with no modal over it.
- The owner judges the moment by eye on the filming build; copy and reveal shape are hers to accept.
- `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs. ⚠ `en.json` is a **canonical JSON** file — patch from HEAD bytes, prove the LF count, update the StreamingAssets twin in the same change.

## 6. DO NOT TOUCH

The capture *gate* and route (`OwnedBaseProgression`, `CanEnterCapturedTown`, `SceneRouter.GoOwnedTown` refusals) — WO-1778 owns those. The tutorial steps — WO-1788. The spoils rows and veterancy caption — WO-1789. Any `.unity` file.
