# WO-1184 — Earned lookout warnings: phone alerts + a LOOKOUT REPORT HUD surface

**Status:** AWAITING OWNER RULING - owner 2026-09-10 morning, verbatim: "i do get a report post battle offline" (the post-battle offline report surface RENDERS on her device); remaining question = close the report half and keep only the phone-alert half as its own spec? (was: BLOCKED ON OWNER CAPTURE - see banner)
read-only RCA lane proved the body describes code that no longer exists. **Do not pick this up without a
device screenshot and the build id.**

> ### OWNER NOTE 2026-09-10 (morning): "i do get a report post battle offline" - the offline post-battle report surface renders on the Seeker; recorded as testimony, no capture yet.
>
> ### ⚠ STALE BODY - BANNER ADDED 2026-09-06. The two findings below are BOTH ALREADY FIXED.
> Verified at source this session, not inferred:
> - *"Finding 1: hosts on `FindAnyObjectByType<UIDocument>()`, may not render on device"* - **false today.**
>   `Assets/_Modules/Village/Waves/LookoutNoticeChip.cs:100-102` builds a ScreenSpaceOverlay uGUI Canvas;
>   `:115` uses `ElarionUiKit.ToastCard(...)`. No `UIDocument` anywhere.
> - *"Finding 2: `BestLookoutLevel()` matches towers by display-name substring"* - **false today.**
>   `Assets/_Modules/Village/Siege/RoamingHordeNotifications.cs:175-224` keys on `IsLookoutCatalogId(...)`
>   plus `StructureRole.Lookout`.
>
> ### THE BOUNCE DOES NOT DESCRIBE THIS SYSTEM.
> Owner note: *"right now its a red dot middle of screen"*. The shipped surface is `ToastTone.Info`
> (parchment/gold, commented in-file as "not Danger, not a red bang"), anchored **top-centre**
> (`anchorMin/Max (0.5,1)`, `anchoredPosition (0,-96)`), carrying **words**: "Lookout notice" /
> "Horde approaching -- ". Wrong colour, wrong shape, wrong position, wrong substrate. **A red dot exists
> somewhere in the game and it is NOT this chip.**
>
> ### HOW THE BOUNCE WAS PRODUCED - this is the transferable finding.
> Commit `695a5c92b` (2026-09-03) shows it arrived in a **bulk board sign-off**: `200 recorded, 198
> validated, closed 40, bounced 3`. The drop file records only `{"note": "...", "verdict": "Needs Work"}`
> - **no build id, no screenshot.** The fix had shipped 08-27 (`086ce14fd`), so the row was already **10
> days old** on her screen. A note typed against an aged row in a bulk pass is not evidence about the
> current binary. See memory `layout-tickets-need-a-fresh-capture` and `diagnose-the-build-under-test`.
>
> ### WHICH BUILD SHE TESTED IS UNPROVEN, AND WAS NOT GUESSED AT.
> No APK in `Builds/Android/` is dated between the 08-27 fix and the 09-03 bounce. The two apk-chain logs
> that day are 11:47 and 12:05, **before** the 13:41 fix. A `prod-apk-chain` ran 16:07 but its logs show
> only the R2 stage. So it cannot be confirmed or ruled out that the fixed code was ever run.
>
> ### WHAT UNBLOCKS THIS: an F8 capture of the red dot (screen + build id in one artifact).
> Also owed, and an owner call not an RCA outcome: a ruling on "vibration and pulsing" as the notice
> style. ⚠ The owner is red/green colourblind - a pulsing COLOUR cue needs care, and the current chip
> deliberately carries its meaning in words.
>
> **The red dot itself was deliberately NOT identified.** A grep for `Color.red` / `ToastTone.Danger` /
> ping markers returns a dozen candidates; per CLAUDE.md section 12 that LOCATES and does not CONCLUDE.
> Naming one would be the inference-fix the gate forbids.

**PRIOR STATUS:** READY TO IMPLEMENT - owner felt-test 2026-09-03 Needs Work - "right now its a red dot middle of screen, Have UI refine to maybe vibration and pulsing warning". Bounced from Fixed. PRIOR: FIXED 2026-08-27 — implemented; awaiting owner felt-verify to CLOSE.
**Silo:** HUD / notifications.
**Origin:** owner, ad hoc alongside batch 4 — *"i added that adhoc"*. ⚠ Minted **after** the code
landed, as a **ticket of record**: work with no work order is how a fix becomes invisible (two
unattributed fixes were flagged on the board the same day).

⛔ **This is NOT WO-1179 and does not advance it.** It is orthogonal presentation over
`SiegeScheduler`'s existing cadence. WO-1179's core — multi-side partitioning of one wave under one
shared concurrency budget — is **untouched**.

## What landed

- Phone warnings via the existing **Mobile Notifications 2.4.3** package (verified in
  `Packages/manifest.json`; the asmdef reference `Unity.Notifications.Unified` is the **real runtime
  assembly name**, confirmed against the resolved package).
- Tiered lead time: **15 / 30 / 60 minutes**.
- **Broad force-size intelligence gated to level-3 lookouts** — a reason to level a watchtower.
- **One replaceable notification**, cancelled on return, so no stale alert survives.
- A fixed top-centre **LOOKOUT REPORT** surface reusing the existing red `!` visual language.
- `SiegeScheduler.SiegeIntervalMs` (read-only) and `SiegeClock.TryGetDueIn` — presentation reads
  cadence; ⛔ it never writes it.

## ⭐ Why the copy is what it is — do not "improve" it

**Under the banked-pressure ruling nothing attacks while the player is away**, so the message says
*"Horde approaching. Expected at the town in {timing}. Return to defend live."* ⛔ It **never** claims
combat is occurring offline, because that would be **factually false**.
See `FOUNDATIONAL_RULINGS.md` §3 — including the hard fence that **a notification may never be paired
with a shield offer.**

## Review findings — neither blocks, both matter

### ⚠ 1. The HUD surface may not render on device — and this is PRE-EXISTING

`AlertIntelSystem` hosts its banner on `FindAnyObjectByType<UIDocument>()` — from commit `98131cb07`,
**not** this work. But **CLAUDE.md §8: *"UXML in builds: does NOT work"***, and **WO-1182 exists
precisely because a `UIDocument` surface renders blank on device.**

⭐ **UPGRADED FROM A DOUBT TO A RECOMMENDATION - the owner confirmed 2026-08-24: *"the alert for
raids, it was old legacy."***

That settles it. The new LOOKOUT REPORT is **back-patched onto a legacy path**, and onto **the exact
substrate WO-1182 exists to replace** - `UIDocument` surfaces that render blank on device. So this may
be **unreachable content**: written, compiled, gated, and invisible on the phone. ⛔ *Unreachable
content is not content.*

**RECOMMEND: move the LOOKOUT REPORT to the code-built Obsidian kit (uGUI)**, the same substrate every
other player-facing surface uses and the same move WO-1182 is making for the crafting modal. ⛔ Not
because the code is poor - it is clean - but because it sits on a substrate the project has already
decided does not ship.

⚠ **The notification half is UNAFFECTED and is the valuable half** - it needs no UI substrate at all.
⭐ **So this ticket can felt-verify as a SUCCESS even if the on-screen report never appears**, and the
two halves must be judged separately or a working feature reads as broken.

⚠ **Capture first, either way** (§12): confirm whether the report renders in a player build before
anyone rebuilds it. A rebuild on inference is the banned move even when the inference is good.

### ⚠ 2. `BestLookoutLevel()` matches towers by DISPLAY-NAME SUBSTRING

`tower.Data.towerName.IndexOf("Archer" / "Watchtower")`. ⛔ Display names are **not stable keys**, and
this repo already fixed keyword-matching **at the mechanism** once (`a698ec5ed`).

⚠ **CORRECTION 2026-08-24, by the lead, against my own finding.** I first wrote that WO-1163 makes
this fragile *right now*. **That overstated it:** WO-1163 renames `collector_farm` → Quarry and
`silo` → Stoneyard, and **touches no tower name at all**. There is no live collision today.

The finding still stands as a **pattern** rather than an emergency: a display name is not a stable
key, this repo already fixed keyword-matching at the mechanism once (`a698ec5ed`), and **the failure
mode is silent** — a level-0 lookout simply sends no
force-size intel. Match a catalog **id** or the **role enum** instead.

## Acceptance

- [ ] Owner felt-verifies a real warning on the Seeker, with correct lead time
- [ ] ⚠ A capture proves the LOOKOUT REPORT actually renders **in a player build**, not just the editor
- [ ] Level-3 gating proven — a level-2 lookout sends no force-size line
- [ ] Returning to the app cancels the pending notification (no stale alert)
- [ ] `BestLookoutLevel` keys off a stable identifier, not a display name

---
## RCA re-verified 2026-09-04 (QA read-only pass)
**Verdict:** UNPROVABLE
**Evidence:**
- Owner bounce (ingested `695a5c92b` 2026-09-03): "right now its a red dot middle of screen, Have UI refine to maybe vibration and pulsing warning". No F8 capture carries it: `logs/f8-inbox/QUEUE.jsonl` seq 4674-4680 are quiescence/pause/EndState lines, none tagged `Lookout`.
- The shipped notice is NOT a red dot: `Assets/_Modules/Village/Waves/LookoutNoticeChip.cs:115` `ElarionUiKit.ToastCard(transform, ElarionUiKit.ToastTone.Info, ...)` (parchment/gold), `:118-121` anchored top-centre (`anchorMin/Max (0.5,1)`, `anchoredPosition (0,-96)`), `:100-102` ScreenSpaceOverlay uGUI canvas. `AlertIntelSystem.cs:24` "NO UIDocument", `:92` `_chip`, `:163` `_chip.Show(...)`. A grep for `red` / `Danger` / `Color.red` in both files hits only comments.
- `RoamingHordeNotifications.cs:182-224` `BestLookoutLevel` keys on `IsLookoutCatalogId(rec.itemId)` over `BaseLayout` + `PlacedStructure`; no `IndexOf("Archer")`. `StructureRole.cs:101` `Lookout = "lookout"`. So findings 1 and 2 of this WO's body (UIDocument substrate, display-name matching) are already fixed and that text is stale.
- Suite: `Assets/Editor/Regression/LookoutAlertRegression.cs` exists, registered `DataRegression.cs:346` `[lookout-alert]`.
- git log: the five cited files were last touched `086ce14fd` 2026-08-27; RESULT dated 2026-08-27 matches. No superseding WO.
**What changed since the RCA:** nothing in code since 08-27. The bounce describes a surface this system does not draw.
**Ready for a lane?** no - "red dot middle of screen" matches nothing in `LookoutNoticeChip`; without a screenshot it may be a different surface entirely (a minimap ping, a Heart marker). Files a lane would touch (only after the capture names them): `LookoutNoticeChip.cs`, `AlertIntelSystem.cs`.
**Pins/rulings needed:** (1) a device screenshot of the "red dot" plus the build id she tested; (2) an owner ruling on "vibration and pulsing" as the notice style - that is a design choice, not an RCA.
