// =============================================================================
// HudLayoutBands - THE ONE AUTHORITY for the town HUD's left column, for the
// single reserved TOAST ZONE (WO-1219, owner-approved design 2026-08-26), and for
// the RESERVED THUMB BAND that the ability row owns at the bottom of the screen
// (WO-1436, owner ruling 2026-09-06 - see the section above ToastZone).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// WHY THIS FILE EXISTS AT ALL
// ---------------------------
// The left column is the most over-subscribed strip on the HUD (hero plate,
// SKILL chip, Heart bar, minimap, region status line, gear, Store), and every
// one of those seven things was positioned in a DIFFERENT file:
//   * HudAreasHost           - the area mounts (fractions of screen)
//   * HudKitController       - the hero plate + SKILL chip sub-rects, and the
//                              fixed-pixel gear/Store row inside the Dock band
//   * HudMinimapWidget       - the plate and the region chip inside the Minimap band
//   * HubRepairAffordance    - a Village card that lands ON TOP of all of it
// Nobody owned the WHOLE column, so nobody could see that the sum of the parts
// no longer fit. That is exactly how WO-1219 shipped: the gear/Store row sat on
// the minimap's lower edge and the region status line read out from UNDER the
// gear, in two separate device captures, with every marker green.
//
// So the column is now ONE table. Each consumer reads its band from here; the
// oracle (HudUiRegression check 8) reads the SAME table and asserts that no two
// bands intersect at the owner's device resolution. A layout regression is now a
// FAILING BUILD instead of a felt-test report.
//
// ⛔ THE BANDS ARE EXCLUSIVE. That is the whole invariant. If a new element needs
// a seat in the left column, it gets its OWN band here and the neighbours move -
// it does NOT get drawn across one that is already spoken for.
//
// ⚠ FRACTIONS CANNOT PROMISE PIXELS. Three of the seven elements are authored in
// FIXED reference units (the 200-unit minimap plate, the 30-unit region chip, the
// two 112-unit MinTouchPx controls) precisely because a fraction of a band changes
// its aspect with the device. So this file stores the MOUNTS as fractions and the
// occupants as PIXELS, and RESOLVES the two together for a given screen size
// (ResolveLeftColumn). The band arithmetic that WO-1219 had to be re-derived by
// hand, twice, is now executable.
// =============================================================================

using UnityEngine;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// Authored geometry for the town HUD's left column plus the one reserved toast
    /// zone. Pure data + pure arithmetic: no MonoBehaviour, no scene dependency, so
    /// the Editor regression can resolve the exact same rects the runtime uses.
    /// </summary>
    public static class HudLayoutBands
    {
        // ── The canvas the HUD kit is authored against (HudAreasHost) ────────────
        /// <summary>Reference resolution of the HUD kit canvas (CanvasScaler).</summary>
        public const float CanvasRefWidth  = 1080f;
        /// <summary>Reference resolution of the HUD kit canvas (CanvasScaler).</summary>
        public const float CanvasRefHeight = 1920f;
        /// <summary>CanvasScaler matchWidthOrHeight used by HudAreasHost.</summary>
        public const float CanvasMatch = 0.5f;

        /// <summary>The owner's device, and therefore the resolution every band claim in
        /// this file is stated at (Seeker, landscape).</summary>
        public const float DeviceWidth  = 2670f;
        /// <summary>See <see cref="DeviceWidth"/>.</summary>
        public const float DeviceHeight = 1200f;

        // ── MOUNTS: fractions of screen, y-UP, handed to HudAreasHost ────────────
        // Read top-to-bottom, they ARE the approved stack:
        //   hero plate -> SKILL chip -> Heart bar -> minimap -> status line -> gear + Store.

        /// <summary>Vitals mount - holds TWO exclusive bands: the hero plate on top and the
        /// SKILL (Wisdom) chip beneath it. They are separate sub-rects, never a shared box.</summary>
        public static readonly Rect VitalsMount = Rect.MinMaxRect(0.011f, 0.800f, 0.240f, 0.983f);

        /// <summary>Heart of Elarion plate mount. WO-1384 (owner felt-test 2026-09-04, Seeker):
        /// the plate now seats FOUR text rows - name, objective, the Heartfire marks row and the
        /// rekindle line - so it GREW from 0.700 to 0.655 (0.090 -> 0.135 of screen = 130 ref
        /// units at 2670x1200, 146 at 1920x1080). The owner's ruling was "grow the plate, never
        /// shrink the text": the old 87-unit band had two Heartfire lines forced into a
        /// 23-unit slot, which is why the marks drew half off the plate at the plate's smallest
        /// size. The 0.010 gap to the Minimap mount below is deliberate breathing room for the
        /// Night Market card's soft ring (HudKitController.NightMarketRingPx, WO-1384b; it was
        /// the flat gold frame NightMarketCardFramePx before the 2026-09-04 23:59 refinement).</summary>
        public static readonly Rect HeartMount = Rect.MinMaxRect(0.011f, 0.655f, 0.240f, 0.790f);

        /// <summary>Minimap mount - holds TWO exclusive bands: the square plate hanging from
        /// the mount's top-left, and the region STATUS LINE in its own band BELOW the plate.
        /// ⛔ The status line is never drawn ACROSS the plate and never beside it: the owner
        /// captured "Elarion - Safe - N threats" competing with both the map and the gear.
        /// WO-1384: yMax 0.685 -> 0.645 to hand the Heart plate above the room it needed; the
        /// Night Market card hangs from this edge, so its band moved down with it.</summary>
        public static readonly Rect MinimapMount = Rect.MinMaxRect(0.011f, 0.420f, 0.240f, 0.645f);

        /// <summary>Dock mount - the gear + Store row (side by side, never stacked) and the
        /// slide-out drawer that opens to the right of both.</summary>
        public static readonly Rect DockMount = Rect.MinMaxRect(0.000f, 0.360f, 0.230f, 0.470f);

        // ── SUB-RECTS inside the Vitals mount (fractions OF THE MOUNT) ───────────
        // Derived once here so HudKitController never re-derives them by hand.

        /// <summary>Hero nameplate, as a fraction of <see cref="VitalsMount"/>. Screen band
        /// 0.011..0.240 x, 0.883..0.983 y.</summary>
        public static readonly Rect HeroPlateInVitals = Rect.MinMaxRect(0f, 0.180f, 1f, 1f);

        // ── FIXED-PIXEL occupants (reference units, never fractions) ────────────

        /// <summary>Minimap plate edge in reference units (HudMinimapWidget.PlateSize).</summary>
        public const float MinimapPlatePx = 200f;
        /// <summary>Region status-line height in reference units (HudMinimapWidget.ChipHeight).</summary>
        public const float StatusLinePx = 30f;
        /// <summary>Gap between the minimap plate and the status line band below it.</summary>
        public const float StatusLineGapPx = 4f;

        /// <summary>Gear / Store control edge. This is <c>ElarionUiKit.MinTouchPx</c> VERBATIM -
        /// the kit touch floor. ⛔ Nothing here may shrink it, and satisfying it may not create a
        /// new overlap (that is what broke hero-select). Both controls are authored at EXACTLY
        /// the floor, so ClampMinTouch is a no-op on both and is NOT a cause of anything here.</summary>
        public const float DockControlPx = ElarionUiKit.MinTouchPx;
        /// <summary>Gap between the gear and the Store face, and between the pair and the drawer.</summary>
        public const float DockGapPx = 12f;
        /// <summary>Left breathing margin for the dock row (SafeAreaInset.EdgeMarginPx).</summary>
        public const float DockEdgePx = SafeAreaInset.EdgeMarginPx;

        // ── WO-1335 — THE NIGHT MARKET CARD'S OWN BAND ──────────────────────────
        //
        // Owner ruling 2026-09-03: *"the realm store is hidden away needs a permanent face on
        // hud"* / *"can you take the realm store card from settings > night market and anchor it
        // smaller to left side on hud"*. So the card that already exists inside the Realm deck
        // (PlayerDeckWorkspace's "Realm Store" route, art key `realm-store`) gets a permanent
        // seat in this column, and it gets it HERE rather than by being drawn across somebody
        // else's band - the mistake this whole file exists to stop.
        //
        // ⭐ WHY IT SITS IN THE MINIMAP MOUNT, MEASURED RATHER THAN GUESSED. The column has room
        // for exactly one more control and only in one place. At the owner's device (2670x1200,
        // canvas 2147.9 x 965.4 reference units) the clear vertical between the region status
        // line's bottom edge and the MoveCluster mount's top edge is
        //     0.4426 - 0.330 = 0.1126 of screen height = 108.7 reference units,
        // which is 3.3 units UNDER the 112-unit touch floor. So a second control CANNOT be added
        // beside or below the gear without moving a neighbour. The Minimap mount, by contrast, is
        // 491.9 x 255.8 units and is EMPTY at runtime: HudKitController constructs no
        // HudMinimapWidget ("Locked adaptive-HUD ruling: no minimap is constructed on the player
        // HUD"), so MinimapPlatePx / StatusLinePx above currently describe a plate and a status
        // line that nothing draws.
        //
        // ⚠ THEREFORE THIS BAND TAKES THE PLATE'S SEAT, AND THAT CONFLICT IS STATED, NOT HIDDEN.
        // If the minimap plate is ever constructed again, these two collide and MUST be re-split
        // in this file. The pocket to the RIGHT of the plate inside the same mount is
        // 291.7 x 200 units (x from MinimapMount.xMin + MinimapPlatePx, y the plate's own band);
        // it held the original 272 x 132 card but does NOT hold the WO-1384 320 x 156 one, so
        // that day the card and the plate must be re-split vertically or the plate must go
        // elsewhere. Do not resolve it by drawing the card across the plate.
        //
        // ⛔ CLEAR OF THE MOVEMENT STICK BY CONSTRUCTION, WHICH IS THE ONE NON-NEGOTIABLE. The
        // card's band bottoms out at y 0.483 of screen (WO-1384: 0.645 - 156 / 965.4); the gear
        // row tops out at 0.473 and the MoveCluster mount at 0.330. Covering the stick breaks the
        // game's only movement control, so the oracle asserts this rather than trusting the
        // arithmetic above to stay true.

        /// <summary>Night Market card width in reference units. WO-1384 (owner, 2026-09-04:
        /// "it needs to be the shining gem ... above all stands out"): 272 -> 320, so the card is
        /// the LARGEST control in the column by construction - 320 x 156 = 49920 units^2 against
        /// the gear's 112 x 112 = 12544. 320 x 156 keeps the 1798x875 `realm-store` card art at
        /// its authored 2.055:1 aspect, fits the Minimap mount's 491.9-unit width, and clears
        /// the touch floor on BOTH axes. Pinned by HudLabelFitRegression [night-market-standout].</summary>
        public const float NightMarketCardWidthPx = 320f;
        /// <summary>See <see cref="NightMarketCardWidthPx"/>. 156 = 320 / 2.055 (155.7), the
        /// card art's own aspect - never an independently chosen number.</summary>
        public const float NightMarketCardHeightPx = 156f;

        /// <summary>
        /// The Night Market card's screen band, hung from the TOP-LEFT of
        /// <see cref="MinimapMount"/> at a fixed reference size (pixels, never a fraction of the
        /// mount - see this file's header: a fraction changes its aspect with the device and a
        /// touch floor is stated in pixels).
        /// </summary>
        public static Rect ResolveNightMarketCard(float screenW, float screenH)
        {
            var refSize = CanvasReferenceSize(screenW, screenH);
            float ux = refSize.x > 0f ? 1f / refSize.x : 0f;
            float uy = refSize.y > 0f ? 1f / refSize.y : 0f;
            return Rect.MinMaxRect(
                MinimapMount.xMin,
                MinimapMount.yMax - NightMarketCardHeightPx * uy,
                MinimapMount.xMin + NightMarketCardWidthPx * ux,
                MinimapMount.yMax);
        }

        // ── THE RESERVED THUMB BAND (WO-1436, owner ruling 2026-09-06) ──────────
        //
        // ⭐ OWNER RULING, VERBATIM IN SUBSTANCE: the ABILITY ROW owns the thumb position at the
        // bottom of the screen; the raid deploy bar (FOOTMAN / ARCHER / Rally / RETREAT) stacks
        // ABOVE it. Her reasoning is the frequency argument — casting is CONSTANT, deploying is
        // OCCASIONAL, so the constant action belongs under the thumb.
        //
        // WHY THE NUMBER LIVES IN DeNelle.Core AND NOWHERE ELSE
        // ----------------------------------------------------
        // The two surfaces that must not collide are in assemblies that CANNOT see each other:
        //   * the ability row  — HudAreasHost (DeNelle.HUD), area mount HudArea.ActionBar
        //   * the raid deploy bar — RaidDeployController (DeNelle.Village)
        // DeNelle.Village may not reference DeNelle.HUD (CLAUDE.md §5 — the one cross-assembly
        // invariant that is actually enforced by asmdef; AdminOverlay's reflection exists
        // precisely because of it). So the raid HUD cannot ASK the kit where the free band starts.
        // The only lawful shared vocabulary is DeNelle.Core, which both already reference — the
        // CoreServices pattern, and the pattern this very file already uses for ToastZone.
        //
        // ⛔ THE ALTERNATIVE — a Y literal typed into HudAreasHost AND a matching one typed into
        // RaidDeployController — is DUPLICATED STATE, the single failure mode CLAUDE.md documents
        // four separate times (§2 the stale WO-number block, §5 the retired dependency table, §8
        // the restated MaxVisibleFaces, §16 the copy-pasted R2 verify). Every one of those was
        // correct the day it was written. Do not add a fifth. If the row's height changes, it
        // changes HERE, once, and the deploy bar follows without anybody remembering to.
        //
        // ⚠ THE TWO CANVASES ARE DIFFERENT OBJECTS AND THE FRACTIONS ARE STILL COMPARABLE.
        // HudAreasHost's canvas is ScreenSpaceOverlay at sortingOrder 4000; the raid deploy HUD is
        // a separate ScreenSpaceOverlay canvas at 30000. Anchor fractions on a ScreenSpaceOverlay
        // canvas are fractions OF THE SCREEN regardless of the CanvasScaler each one carries — the
        // scaler changes the reference-unit size, never where anchorMin 0.16 lands. So band
        // arithmetic across the two is valid, and sortingOrder becomes irrelevant once the bands
        // are exclusive. ⛔ Do not "fix" a future overlap by re-ordering the canvases; separate
        // the bands.

        /// <summary>Bottom edge of the ability/action row (HudArea.ActionBar), as a screen
        /// fraction. ⛔ THIS AND <see cref="ThumbActionRowMaxY"/> ARE THE ONLY COPY. HudAreasHost
        /// reads them for its ActionBar mount — it does not author its own.</summary>
        public const float ThumbActionRowMinY = 0.015f;
        /// <summary>Top edge of the ability/action row. See <see cref="ThumbActionRowMinY"/>.</summary>
        public const float ThumbActionRowMaxY = 0.150f;

        /// <summary>Breathing gap between the thumb row and anything stacked on top of it, and
        /// between an overlay's own stacked parts. One number, so a bar and its status line never
        /// drift apart.</summary>
        public const float ThumbBandClearanceGap = 0.010f;

        /// <summary>
        /// ⭐ THE FLOOR FOR ANY BOTTOM-ANCHORED OVERLAY THAT IS NOT THE ABILITY ROW. A module that
        /// wants a bottom bar starts HERE and grows upward. Nothing may reach below it.
        /// </summary>
        public static float BottomOverlayFloorY
        {
            get { return ThumbActionRowMaxY + ThumbBandClearanceGap; }
        }

        /// <summary>The thumb row's own band at a given x span, for oracle arithmetic. The x edges
        /// are NOT authored here (they live on HudAreasHost, whose source text is pinned by
        /// HudDockLayoutRegression) — the caller passes them in.</summary>
        public static Rect ThumbActionRowBand(float xMin, float xMax)
        {
            return Rect.MinMaxRect(xMin, ThumbActionRowMinY, xMax, ThumbActionRowMaxY);
        }

        /// <summary>A band of <paramref name="height"/> (screen fraction) seated immediately above
        /// the reserved thumb band, plus <paramref name="stackIndex"/> further bands of the same
        /// height already stacked there. Pure arithmetic; no scene, no canvas.</summary>
        public static Rect StackAboveThumbBand(float xMin, float xMax, float height, float offset)
        {
            float y0 = BottomOverlayFloorY + offset;
            return Rect.MinMaxRect(xMin, y0, xMax, y0 + height);
        }

        // ── WO-1464 — THE MOVEMENT STICK'S BAND, AND THE RAID READOUT'S SEAT ────
        //
        // ⭐ MEASURED BASELINE, NOT INFERRED: Logs/device/screens/owner-screen-20260907-004502.png
        // (build 358872, 2670x1200, mid-raid at 1:13). Three raid surfaces were drawn across town
        // HUD bands in that one frame:
        //   * the deploy TRAY   (x 0.020-0.980, y 0.160-0.310)  across the movement stick;
        //   * the deploy STATUS (x 0.020-0.980, y 0.320-0.360)  across the stick's top edge;
        //   * the raid READOUT  (x 0.020-0.980, y 0.860-0.990)  across the hero nameplate
        //     ("1:13" over "Th... Lv 7") and the compass ("1/3" and "Troops 10/10" over NE/E).
        // Every one of them was authored as a Village-local literal that could not see the band
        // it was landing on - the same shape as WO-1219 and WO-1436, for the third time.
        //
        // ⛔ WHY THE STICK'S BAND MOVES HERE. RaidDeployController (DeNelle.Village) has to start
        // its tray to the RIGHT of the movement stick, and DeNelle.Village may not reference
        // DeNelle.HUD (CLAUDE.md sec.5). So the stick's band becomes shared DATA, exactly as
        // ThumbActionRowMinY/MaxY did for the ability row in WO-1436. HudAreasHost reads it; it
        // does not author its own. ⚠ HudAreasHost.ActionBarMinX (0.270f) is documented there as
        // "also the MoveCluster's RIGHT edge" and its SOURCE TEXT is pinned verbatim by
        // HudDockLayoutRegression, so that const cannot be dissolved into this table. The two
        // therefore have to AGREE, and agreement-by-hand is the failure this file exists to end -
        // so RaidHudThumbBandRegression asserts ActionBarMinX == MoveClusterMount.xMax and a
        // drift is a RED BUILD rather than a felt-test report.

        /// <summary>The virtual movement stick's mount (HudArea.MoveCluster). ⛔ THIS IS THE ONLY
        /// COPY - HudAreasHost reads it. Covering this band removes the player's only locomotion
        /// control on a phone, which is why it is stated as shared data rather than trusted to two
        /// files agreeing.</summary>
        public static readonly Rect MoveClusterMount = Rect.MinMaxRect(0.010f, 0.030f, 0.270f, 0.330f);

        /// <summary>
        /// ⭐ THE LEFT EDGE FOR ANY BOTTOM-ANCHORED RAID/COMBAT OVERLAY THAT SPANS THE SCREEN.
        /// A full-width strip seated above the thumb band still crosses the stick, because the
        /// stick's band reaches up to y 0.330 - higher than <see cref="BottomOverlayFloorY"/>.
        /// So a bottom overlay starts HERE on x, not at the screen edge.
        /// </summary>
        public static float BottomOverlayLeftX
        {
            get { return MoveClusterMount.xMax + ThumbBandClearanceGap; }
        }

        /// <summary>
        /// ⭐ THE RAID READOUT'S OWN BAND (timer / stars / spire / razed) - a RIGHT-HAND COLUMN,
        /// because there is no free full-width strip at the top of this HUD and gap-fitting one
        /// into the slivers between the authored top-row mounts is the hand-nudge WO-1464 sec.3
        /// forbids.
        ///
        /// ⛔ WHY A COLUMN AND NOT A TOP STRIP, MEASURED FROM THE MOUNT TABLE RATHER THAN EYED.
        /// The top row is fully spoken for at every x: Vitals 0.011-0.240 reaches y 0.983, Status
        /// (the compass) 0.340-0.660 reaches 0.990, System 0.845-0.995 reaches 0.985. Above all
        /// three there is 0.010 of screen left - not a band, a seam. Below them TargetInfo
        /// (0.280-0.720 x, 0.660-0.840 y) and the Heart plate (x &lt;= 0.240, 0.655-0.790 y) close
        /// the next row. A full-width raid strip CANNOT exist without evicting a town element.
        ///
        /// ⭐ WHAT MAKES THIS COLUMN FREE IS OCCUPANCY, AND IT IS AUTHORED, NOT ASSUMED.
        /// BOTH shipped copies of hud-areas.json (Assets/Resources/Data/Canonical/ and
        /// Assets/StreamingAssets/Data/Canonical/ - read and compared 2026-09-07, they agree)
        /// list `actionRail` with an EMPTY widget array in BOTH hostile postures, and neither
        /// lists `queueStatus` in either. Checking only one copy would have been hearsay about
        /// the shipped build; hostile(activebattle) DOES occupy `system` with fleeButton +
        /// settingsButton, which is why the top edge stops short of it. And WO-1436
        /// made a raid declare combat for its whole duration, so a raid is hostile end to end.
        /// The column those two mounts share (x 0.780-0.995) is therefore empty for exactly as
        /// long as the raid lasts. It also happens to be where the spire/razed group already sat
        /// in the owner's capture, and where Clash of Clans puts its raid clock.
        ///
        /// ⚠ THE CONFLICT IS STATED, NOT HIDDEN (the ResolveNightMarketCard precedent above).
        /// This band TAKES the ActionRail + QueueStatus seat. If either area is ever occupied
        /// during a hostile posture, the two collide and MUST be re-split HERE - not resolved by
        /// drawing one across the other, and never by re-ordering the canvases.
        ///
        /// The top edge stops one clearance gap under the System mount's bottom edge (0.880),
        /// because hostile(activebattle) DOES occupy System with fleeButton + settingsButton.
        /// </summary>
        public static readonly Rect RaidReadoutBand = Rect.MinMaxRect(0.780f, 0.510f, 0.995f, 0.870f);

        // ── WO-1670 — THE RIGHT COLUMN'S QUEUE BAND AND THE ECHOES CHIP ────────
        //
        // ⭐ OWNER RULING 2026-09-10 12:16: the Echoes chip encroaches 0.022 of the canvas into
        // the QueueStatus band — fix it via a HudLayoutBands seam. That ruling is what releases
        // WO-1642 §4's explicit refusal to move a placement the owner felt-tested on 2026-07-24.
        //
        // WHY THE NUMBER LIVES HERE AND NOWHERE ELSE — the fourth time, identically shaped.
        // ---------------------------------------------------------------------------------
        // The two surfaces are in assemblies that cannot see each other:
        //   * the QueueStatus band — HudAreasHost (DeNelle.HUD), area mount HudArea.QueueStatus,
        //     occupied by the Collectors chip and the ATTACK REPORT chip;
        //   * the Echoes chip      — EchoUnlockFeedback (DeNelle.Village), its own canvas.
        // DeNelle.Village may not reference DeNelle.HUD (CLAUDE.md §5 — the one cross-assembly
        // invariant asmdef actually enforces). So the chip restated a fraction it could not see,
        // and EchoUnlockFeedback's own doc comment had already diagnosed itself: "⛔ DO NOT WRITE
        // A MOUNT RECT INTO THIS FILE AGAIN. The only cure for the copy is deleting it." Same
        // shape as ThumbActionRowMinY (WO-1436) and MoveClusterMount (WO-1464) above.
        //
        // ⭐ THE FIX IS TO AUTHOR THE CHIP'S TOP EDGE, NOT ITS CENTRE — AND THAT IS THE WHOLE
        // POINT, MEASURED RATHER THAN PREFERRED. The chip is a FIXED 112 ref px box; a CENTRE
        // fraction therefore yields a DIFFERENT top edge at every aspect, so no single centre
        // value can be disjoint from a fractional band everywhere. At centre 0.475 the band read
        // 0.4170..0.5330 at 2670x1200 and 0.4231..0.5269 at 1920x1080 — 0.0230 and 0.0169 INSIDE
        // the mount's 0.510 floor. Hung from its TOP the edge is 0.500 at every resolution and
        // the box hangs down: disjointness stops depending on the device. (This is the
        // ResolveNightMarketCard idiom above — mounts are fractions, occupants are pixels.)
        //
        // ⚠ THE BANDS OVERLAPPED; THE INK DID NOT. WO-1642 measured the PLATES on
        // Builds/device-frames/2026-09-10_0602_town.png (ATTACK REPORT ink ends at device row 545,
        // the Echo plate's ink starts at 585) and correctly reported no pixel collision. That gap
        // exists only because MedievalUiSkin's sprite ink occupies 0.145..0.736 of its band. The
        // resting ATTACK REPORT chip's BAND bottoms out at y 0.5118 (2670x1200) / 0.5370
        // (1920x1080) while the old Echo band's top was 0.5330 / 0.5269 — 0.0212 and 0.0101 of
        // genuinely shared screen, on an identical 220 px right gutter. Both statements are true;
        // the band overlap is the one an oracle can hold, and HudUiRegression check 11 holds it.
        //
        // ⛔ THE RAIL'S EXPANDED DEPTH IS NOT MODELLED HERE, AND THAT IS STATED, NOT HIDDEN.
        // HudRailClearance (DeNelle.HUD) derives each rail chip's y from the laid-out resource
        // panel and is explicitly allowed to hang a chip BELOW this mount ("a mount point, not a
        // clip rect"). With the panel open at four rows the ATTACK REPORT chip derives to y
        // 0.4667..0.3507 and crosses the Echo band. It crossed the OLD band too, so it is
        // pre-existing, and NO authored fraction can close it — the depth is a runtime function
        // of kinds.Length and the chip is on a canvas DeNelle.Village cannot reach. WO-1670 §8.1
        // records the three options and that all three need the owner. Do not "fix" it by
        // reserving the worst-case depth here: at six rows that pushes the Echoes chip to y 0.218
        // and makes the DEFAULT screen worse for a transient state.

        /// <summary>The Builders/Training/Collectors status band on the right column
        /// (HudArea.QueueStatus). ⛔ THIS IS THE ONLY COPY — HudAreasHost reads it, it does not
        /// author its own. Its FLOOR is what <see cref="EchoChipTopY"/> is derived from, so the
        /// two can no longer drift apart by a hand edit.</summary>
        public static readonly Rect QueueStatusMount = Rect.MinMaxRect(0.780f, 0.510f, 0.995f, 0.750f);

        /// <summary>Echoes chip width in reference units. Canon: this is the SAME 220 as
        /// HudKitController.RailChipWidthPx — three rail chips and this one share a single right
        /// edge, and HudLabelFitRegression pins both. ⛔ Narrowing it is a forbidden route out of
        /// any overlap (WO-1144: the fix for a tight label is fewer characters, never a smaller
        /// box).</summary>
        public const float EchoChipWidthPx = 220f;

        /// <summary>Echoes chip height in reference units — <see cref="ElarionUiKit.MinTouchPx"/>
        /// VERBATIM, so ClampMinTouch is a no-op on it. A sub-floor chip is grown symmetrically
        /// about its CENTRE into its neighbour, which is how WO-868 pushed a corner button
        /// off-screen; that route back into this overlap is closed by construction.</summary>
        public const float EchoChipHeightPx = ElarionUiKit.MinTouchPx;

        /// <summary>Right-edge inset for the Echoes chip, reference units. This expression is
        /// <c>HudKitController.RailGutterPx</c> VERBATIM (that const is <c>ElarionUi.PadPanel * 3f</c>
        /// and its own comment names this chip as the reason), so the whole right column keeps ONE
        /// right edge. ⛔ Never a raw 54 — authored as a multiple of PadPanel (WO-779 spacing rule).
        /// The measured justification is on EchoUnlockFeedback.BuildPetBoxButton: 54 ref px ~= 24 dp,
        /// 1.5x the Material screen margin, clear of the rounded corner / cutout / gesture band.</summary>
        public const float EchoChipEdgeInsetPx = ElarionUi.PadPanel * 3f;

        /// <summary>
        /// ⭐ THE ECHOES CHIP'S TOP EDGE, as a screen fraction — one clearance gap under the
        /// QueueStatus mount's floor. A PURE FRACTION, so it is identical at every resolution
        /// (0.500 today); only the 112 px box below it is pixels. The chip anchors here with
        /// pivot (1,1) and hangs down.
        /// <para>⛔ Derived, never typed. <see cref="ThumbBandClearanceGap"/> is REUSED rather
        /// than a second clearance constant being minted — one number, so a band and its
        /// neighbour cannot drift apart, which is the failure this whole file exists to end.</para>
        /// </summary>
        public static float EchoChipTopY
        {
            get { return QueueStatusMount.yMin - ThumbBandClearanceGap; }
        }

        /// <summary>
        /// The Echoes chip's screen band, hung from <see cref="EchoChipTopY"/> at its fixed
        /// reference size against the RIGHT screen edge. Pure arithmetic, so the Editor oracle
        /// resolves the exact rect the runtime builds (HudUiRegression check 11).
        /// </summary>
        public static Rect ResolveEchoChip(float screenW, float screenH)
        {
            var refSize = CanvasReferenceSize(screenW, screenH);
            float ux = refSize.x > 0f ? 1f / refSize.x : 0f;
            float uy = refSize.y > 0f ? 1f / refSize.y : 0f;
            float xMax = 1f - EchoChipEdgeInsetPx * ux;
            float yMax = EchoChipTopY;
            return Rect.MinMaxRect(xMax - EchoChipWidthPx * ux, yMax - EchoChipHeightPx * uy,
                                   xMax, yMax);
        }

        // ── THE ONE RESERVED TOAST ZONE ─────────────────────────────────────────

        /// <summary>
        /// ⭐ THE LEGAL SEAT FOR ANY TRANSIENT TOAST ON THE TOWN HUD - centred above the action
        /// bar, overlapping NOTHING (owner-approved, WO-1219 addendum 2026-08-26).
        ///
        /// WHY IT IS A SHARED CONSTANT AND NOT A LOCAL FIX: the Repair All card was authored in
        /// DeNelle.Village against a left-column seat it could not see the contents of, and it
        /// landed on the minimap, the status line AND the gear at once (tmp/shield-seat-101829.png).
        /// Any module can raise a toast; only ONE of them can pick a seat, so the seat is data.
        /// WO-1236's dungeon-flag acknowledgement uses THIS zone too - it is the convention, not
        /// a 1219-local patch.
        ///
        /// Verified clear of every HudAreasHost band at the owner's resolution: ActionBar tops out
        /// at y 0.150, MoveCluster ends at x 0.270, ActionRail and QueueStatus start at x 0.780,
        /// TargetInfo does not begin until y 0.660, and the whole left column ends at x 0.240.
        ///
        /// <para>⛔ THE ZONE'S HEIGHT IS A TOUCH-FLOOR FLOOR, NOT A LOOK (WO-1664, 2026-09-10).
        /// Every control seated here inherits THIS rect's height, because
        /// <see cref="ToastZoneSlice"/> slices X only (its from/to are "0..1 across the zone's
        /// WIDTH"). So when the Seeker printed
        /// <c>CLAMP FIRED HubRepairAffordance/HubRepairCanvas/ObsBtn_REPAIR ALL: authored
        /// 386.6x101.4 -&gt; grown 386.6x112</c>, the 101.4 was NOT that call site's business:
        /// HubRepairAffordance asks for <c>ToastZoneSlice(0f, 0.72f)</c> and 0.72 is its WIDTH
        /// share (0.72 x 0.25 x 2148 ref px = 386.6, which is the logged width exactly). The
        /// height was this constant's, whole: (0.308 - 0.203) x 965.4 = 101.3 ref px.
        /// ⚠ WO-1664 §2B/§4B read 0.72 as a HEIGHT fraction and told the lane to raise it to
        /// ~0.80. That would only have made the card WIDER and left the clamp firing — the
        /// ticket's own §4B/§6 escape hatch ("if the zone itself cannot seat 112 px at any
        /// fraction, the change belongs in HudLayoutBands and it moves for every toast") is the
        /// branch that is true, and this is it.</para>
        ///
        /// <para>THE ARITHMETIC. refHeight is the CanvasScaler's post-scale height and the kit
        /// scaler is referenceResolution (1080,1920) / MatchWidthOrHeight 0.5
        /// (ElarionUiKit.cs:109-111), so refHeight = H / ((W/1080)^0.5 x (H/1920)^0.5):
        /// 1080.0 at 1920x1080, 978.4 at 2340x1080, 965.4 at 2670x1200. 965.4 is the smallest,
        /// so the zone is authored against it: it needs at least
        /// ElarionUiKit.MinTouchPx / 965.4 = 0.1160 of screen height, against the old 0.105.
        /// yMax 0.308 -&gt; 0.325 gives 0.122 x 965.4 = 117.8 ref px.</para>
        ///
        /// <para>IT GROWS UPWARD, and the headroom is measured, not assumed: the only band
        /// sharing this zone's x range (0.375-0.625) is TargetInfo (x 0.280-0.720), whose floor
        /// is y 0.660 — 0.335 of screen above the new top. Downward there is only the ActionBar,
        /// topping out at 0.150, i.e. 0.053 under the UNCHANGED yMin, and the bottom edge is
        /// deliberately left where it is so the thumb-reach edge of every toast stays put.</para>
        ///
        /// <para>⚠ EVERY CONSUMER GROWS WITH IT, by design — that is what "it moves in ONE place"
        /// means: HudKitController's toast (ApplyToastZone, :561-562), BreakCaptureHarness's
        /// FLAGGED acknowledgement (:749), WO-1236's dungeon-flag ack, and both faces of the
        /// Repair All card (the button and its acknowledge close, which takes slice 0.76..1).</para>
        /// </summary>
        public static readonly Rect ToastZone = Rect.MinMaxRect(0.375f, 0.203f, 0.625f, 0.325f);

        /// <summary>Seat a RectTransform in the reserved toast zone (anchors, full stretch).</summary>
        public static void ApplyToastZone(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(ToastZone.xMin, ToastZone.yMin);
            rt.anchorMax = new Vector2(ToastZone.xMax, ToastZone.yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A horizontal slice of the reserved toast zone, so a card that needs two
        /// controls (e.g. Repair All + its acknowledge close) still lands wholly inside it.
        /// <paramref name="from"/>/<paramref name="to"/> are 0..1 across the zone's width.</summary>
        public static Rect ToastZoneSlice(float from, float to)
        {
            float x0 = Mathf.Lerp(ToastZone.xMin, ToastZone.xMax, Mathf.Clamp01(from));
            float x1 = Mathf.Lerp(ToastZone.xMin, ToastZone.xMax, Mathf.Clamp01(to));
            return Rect.MinMaxRect(x0, ToastZone.yMin, x1, ToastZone.yMax);
        }

        // ── RESOLUTION: fractions + pixels -> the seven real screen rects ────────

        /// <summary>The CanvasScaler reference size (in reference units) for a screen, using the
        /// kit's authored reference resolution and match mode. This is the arithmetic WO-1219 had
        /// to do by hand twice; it is executable now.</summary>
        public static Vector2 CanvasReferenceSize(float screenW, float screenH)
        {
            if (screenW <= 0f || screenH <= 0f) return new Vector2(CanvasRefWidth, CanvasRefHeight);
            float logW = Mathf.Log(screenW / CanvasRefWidth, 2f);
            float logH = Mathf.Log(screenH / CanvasRefHeight, 2f);
            float scale = Mathf.Pow(2f, Mathf.Lerp(logW, logH, CanvasMatch));   // Unity's own formula
            if (scale <= 0f) return new Vector2(CanvasRefWidth, CanvasRefHeight);
            return new Vector2(screenW / scale, screenH / scale);
        }

        /// <summary>The seven left-column bands, in the order they stack top-to-bottom.
        /// Index order matches <see cref="LeftColumnNames"/>.</summary>
        public static Rect[] ResolveLeftColumn(float screenW, float screenH)
        {
            var refSize = CanvasReferenceSize(screenW, screenH);
            float ux = refSize.x > 0f ? 1f / refSize.x : 0f;   // one reference unit, as an x-fraction
            float uy = refSize.y > 0f ? 1f / refSize.y : 0f;   // one reference unit, as a y-fraction

            var heroPlate = SubRect(VitalsMount, HeroPlateInVitals);

            // The minimap plate hangs from the mount's TOP-LEFT at a fixed square size, and the
            // status line takes its own band immediately BELOW it. Both are pixels, so both are
            // resolved against the canvas rather than the band's aspect.
            float plateW = MinimapPlatePx * ux, plateH = MinimapPlatePx * uy;
            var minimapPlate = Rect.MinMaxRect(MinimapMount.xMin, MinimapMount.yMax - plateH,
                                               MinimapMount.xMin + plateW, MinimapMount.yMax);
            float lineTop    = MinimapMount.yMax - (MinimapPlatePx + StatusLineGapPx) * uy;
            float lineBottom = lineTop - StatusLinePx * uy;
            var statusLine = Rect.MinMaxRect(MinimapMount.xMin + StatusLineGapPx * ux, lineBottom,
                                             MinimapMount.xMax - StatusLineGapPx * ux, lineTop);

            // The gear is a fixed-pixel control centred on the Dock band.
            //
            // ⚠ WO-1335 - THE SECOND SEAT IN THIS ROW IS GONE, AND ITS REMOVAL IS A CORRECTION.
            // This resolver used to return a 112-unit "Store" band beside the gear. No such
            // control has existed since HudKitController folded secondary navigation into the one
            // menu handle ("The former persistent 'Realm' face was actually the Store/Night
            // Market"), so the table was reserving a seat for a widget that is not built - a
            // phantom occupant is worse than a missing one, because the next reader budgets
            // around it. The store face is back as of WO-1335, but as the NIGHT MARKET CARD in
            // its own band (ResolveNightMarketCard), not as a square beside the gear: 108.7 of
            // the 112 units that seat needs are all the column has left there.
            float ctrlH = DockControlPx * uy, ctrlW = DockControlPx * ux;
            float rowMidY = (DockMount.yMin + DockMount.yMax) * 0.5f;
            float rowBottom = rowMidY - ctrlH * 0.5f, rowTop = rowMidY + ctrlH * 0.5f;
            float gearX = DockMount.xMin + DockEdgePx * ux;
            var gear = Rect.MinMaxRect(gearX, rowBottom, gearX + ctrlW, rowTop);

            var nightMarketCard = ResolveNightMarketCard(screenW, screenH);

            return new[] { heroPlate, HeartMount, minimapPlate, statusLine, gear, nightMarketCard };
        }

        /// <summary>Human names for <see cref="ResolveLeftColumn"/>, same order.</summary>
        public static readonly string[] LeftColumnNames =
        {
            "hero plate", "Heart objective", "minimap plate", "status line", "gear",
            "Night Market card",
        };

        /// <summary>Project a sub-rect expressed as a fraction of a parent band into screen
        /// fractions.</summary>
        public static Rect SubRect(Rect parent, Rect childFraction)
        {
            return Rect.MinMaxRect(
                parent.xMin + childFraction.xMin * parent.width,
                parent.yMin + childFraction.yMin * parent.height,
                parent.xMin + childFraction.xMax * parent.width,
                parent.yMin + childFraction.yMax * parent.height);
        }

        /// <summary>True when two bands share any area. Touching edges are NOT an overlap.</summary>
        public static bool Intersects(Rect a, Rect b)
        {
            return a.xMin < b.xMax - Epsilon && b.xMin < a.xMax - Epsilon &&
                   a.yMin < b.yMax - Epsilon && b.yMin < a.yMax - Epsilon;
        }

        /// <summary>Slack allowed before two bands count as colliding (sub-pixel authoring noise).</summary>
        public const float Epsilon = 0.0005f;
    }
}
