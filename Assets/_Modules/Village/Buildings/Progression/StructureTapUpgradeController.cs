// =============================================================================
// StructureTapUpgradeController — WO-1712: tap a building in the world, land in
// its upgrade screen.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Buildings.Progression
//
// OWNER REQUEST (felt-test of release 2026.09.14.369302, verbatim): "it would be
// nice to be able to click on the building directly from the city ... so if
// you're standing there at a tower, you're like wow I didn't upgrade this one
// click on it and have the ability to upgrade it directly from that screen".
//
// ⛔ THIS IS AN ADDITIONAL DOORWAY, NOT A NEW SCREEN, AND NOT A NEW ECONOMY.
//   Every tap resolved here ends in the SAME call the Build screen and the Manage
//   screen already make — PanelRouter.Open(PanelId.BuildingUpgrade, <panel id>) —
//   which is registered by BuildingUpgradePanelMvvm (BuildingUpgradePanelMvvm.cs:261).
//   Nothing is charged, queued or mutated here: the page's own CTA owns the spend
//   through PlacedStructureUpgradeService, exactly as it does from every other door.
//   The owner's own framing is why: "they could always go through the build screen
//   or the managed screen ... I just think it might be a nice option".
//
// WHY THE "ONE DOOR" CANON (CLAUDE.md §7) IS NOT BROKEN BY THIS FILE.
//   The PROD-002 rulings that CLOSED doors (BuildingInteractable._noTalkDoor) closed
//   TALK prompts that opened a room which had MOVED to Manage — a door to nowhere.
//   This adds a SHORTCUT to the room Manage already owns, on the owner's explicit
//   later instruction (2026-09-14). The destination is unchanged and singular; only
//   the number of ways to reach it grows, which is what was asked for.
//
// ⚠ THE PANEL ID GRAMMAR IS NOT A STRING THIS FILE INVENTS.
//   A city/resource building opens under its LADDER id (CatalogRegistry.ResolveUpgradeId);
//   a placed structure (tower / wall / container / mine / caravan) opens under its JOB KEY
//   (PlacedUpgradeKey.Compose → "itemId@cellX_cellZ"), which is the '@' grammar
//   UpgradeFamilyResolver keys UpgradeFamily.PlacedStructure off. Both are composed by
//   calling the EXISTING authorities — PlacedUpgradeKey, UpgradeFamilyResolver,
//   CatalogRegistry.ResolveUpgradeId, PlacedStructureUpgradeService.MaxLevelFor — never by
//   re-spelling a key or re-deriving a ceiling here. The same four authorities are what
//   BuildModeController.UpgradeSelected (BuildModeController.cs:2776-2822) composes, so the
//   two doors cannot disagree about which page a structure opens.
//
//   ⚠ FOLLOW-UP, NAMED RATHER THAN SILENTLY LEFT: that composition now exists in TWO
//   places (there and ResolvePanelIdFor below). It is not duplicated DATA — both sides call
//   the same four authorities — but it is duplicated PRECEDENCE, and CLAUDE.md §2/§5/§16
//   are three separate stories about how that ends. Collapsing BuildModeController's block
//   into ResolvePanelIdFor is a one-line delegation and is deliberately NOT done in this
//   lane: BuildModeController is being edited concurrently by other lanes, and this WO's own
//   acceptance criterion 3 is "existing Build-screen and Manage-screen upgrade paths are
//   unchanged". Do it as its own change, with the gate to itself.
//
// GUARD ORDER, AND WHY EACH GUARD IS PRESENT (WO-1712 acceptance 2).
//   1. suppression window — a UI surface that consumed this press calls
//      SuppressNextWorldTap(), the same two-frame seam WallRepairController exposes.
//   2. PanelManager.AnyOpen / InCloseGrace — while a panel is up (or one just closed on
//      this press) every tap belongs to that panel, never the world. Without the grace
//      clause the press that CLOSES a panel would immediately re-open an upgrade page
//      behind it.
//   3. build mode — BuildModeController owns world taps while it is active (its own
//      UpdateSelectLoop → SelectStructure → BuildSelectionUI path). Two owners of one tap
//      is a fight, so this controller stands down entirely.
//   3b. repair selection — WallRepairController raycasts the PRESS half of this same tap
//      (Input.GetMouseButtonDown / TouchPhase.Began). Its prompt is a HUD prompt, NOT a
//      PanelManager-registered panel, so guard 2 cannot see it. Without this clause one
//      finger on a damaged tower raises the repair prompt AND the upgrade page.
//   3c. owned town — OwnedTownPanel owns structure selection in the captured town, keyed by
//      OwnedTownJobKey rather than catalog-id@cell. Stood down rather than guessed at.
//   4. uGUI — delegated to BuildModeController.IsPointOverUi (BuildModeController.cs:969),
//      an EventSystem.RaycastAll probe. ⚠ It is PHASE-INDEPENDENT, which is why it is
//      preferred over EventSystem.IsPointerOverGameObject here: this controller fires on
//      pointer RELEASE (see below), and IsPointerOverGameObject is documented as reliable
//      only on the press frame — on release it would answer about a finger that has
//      already lifted and guard nothing.
//   5. UI Toolkit — panel.Pick per live UIDocument. ⛔ GUARD 4 CANNOT SEE THIS AND THAT IS
//      THE WHOLE REASON THIS CLAUSE EXISTS: the shipped town bottom bar is the adaptive
//      peaceful dock built by HudKitController (CLAUDE.md §7), i.e. UI TOOLKIT, and UITK
//      elements are not in any graphic raycast. A uGUI-only guard — the WO-1708 pattern
//      taken literally — would let every press on the dock ALSO raycast the world behind it
//      and open an upgrade page the player never aimed at. That is the same class of defect
//      WO-1708 was raised to fix, one toolkit over.
//
// TAP, NOT PRESS. The town camera pans and pinches (BUILD HUD mobile design), so a
// press-down cannot be the trigger: the first frame of a camera drag across a tower would
// open its page. This fires on RELEASE, only when the pointer stayed inside
// TapSlopPx of the press point and inside TapMaxSeconds. A drag is therefore not a tap by
// measurement, not by hope.
//
// INPUT: the LEGACY Input Manager (UnityEngine.Input) — the same API
// WallRepairController's tap loop reads, so town world-tap handling stays on one input
// stack rather than growing a second one.
// =============================================================================

using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>
    /// Listens for a world tap on a placed / built structure and opens that structure's
    /// existing upgrade page (PanelId.BuildingUpgrade) under the right panel id. Opens
    /// nothing else, charges nothing, and stands down whenever another owner holds the tap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructureTapUpgradeController : MonoBehaviour
    {
        // ── Tap shape (measured, not felt) ───────────────────────────────────

        /// <summary>
        /// Pointer travel, in REFERENCE pixels, still counted as a tap rather than a camera
        /// drag. Converted to real device pixels by <see cref="TapSlopPixels"/> so the same
        /// thumb movement reads identically on the Seeker and in a 1080p editor window — a
        /// raw pixel literal would be three times stricter on a high-density phone, i.e.
        /// the one platform this feature is for.
        /// </summary>
        public const float TapSlopReferencePx = 24f;

        /// <summary>The reference width those slop pixels are authored against.</summary>
        public const float ReferenceScreenWidth = 1920f;

        /// <summary>A press held longer than this is not a tap (it is a drag or a hold).</summary>
        public const float TapMaxSeconds = 0.6f;

        /// <summary>How far the selection ray reaches. Matches WallRepairController's reach.</summary>
        private const float RayDistance = 1000f;

        // ── Runtime state ────────────────────────────────────────────────────

        private Camera _camera;
        private LayerMask _selectableMask = ~0;
        private bool _pressActive;
        private Vector2 _pressPoint;
        private float _pressStartedAt;
        private int _suppressTapUntilFrame = -1;

        /// <summary>Per-frame UIDocument cache — the WO-1831 shape (never FindObjectsByType per probe).</summary>
        private static UIDocument[] s_uiDocs;
        private static int s_uiDocsFrame = -1;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            _camera = Camera.main;
            FlowTrace.Step("TapUpgrade",
                "StructureTapUpgradeController armed (WO-1712): a world tap on a built or placed " +
                "structure opens PanelId.BuildingUpgrade for THAT structure. Build screen and " +
                "Manage screen doors are untouched.");
        }

        private void Update()
        {
            // 4-arg accumulating Measure: this is a per-frame site, and the 3-arg form on a
            // frame path floods the log and evicts the boot window out of the device logcat
            // ring (CLAUDE.md §12, FlowTrace.cs:293-300).
            using var _ = FlowTrace.Measure("Perf", "StructureTapUpgradeController.Update", 2f, 1f);

            if (!PollTap(out Vector2 screen)) return;
            HandleTap(screen);
        }

        // =====================================================================
        //  Input — press/release with a slop + time budget
        // =====================================================================

        /// <summary>
        /// True for the single frame a TAP completes, with the release screen point. A press
        /// that travelled past the slop budget, or was held past the time budget, is a camera
        /// drag and is consumed WITHOUT reporting a tap.
        /// </summary>
        private bool PollTap(out Vector2 screen)
        {
            screen = Vector2.zero;

            bool down = PressBeganThisFrame();
            bool up = PressEndedThisFrame();
            Vector2 now = PointerScreenPosition();

            if (down)
            {
                _pressActive = true;
                _pressPoint = now;
                _pressStartedAt = Time.unscaledTime;
                return false;
            }

            if (!_pressActive) return false;

            // Travelled too far, or held too long: this press belongs to the camera.
            float travel = Vector2.Distance(now, _pressPoint);
            float slop = TapSlopPixels();
            bool tooFar = travel > slop;
            bool tooLong = Time.unscaledTime - _pressStartedAt > TapMaxSeconds;

            if (!up)
            {
                if (tooFar || tooLong)
                {
                    _pressActive = false;
                    string dragReason = tooFar ? "travelled" : "held";
                    FlowTrace.Throttle("TapUpgrade", "press-became-drag", 2f,
                        "press DISCARDED as a camera drag before release - " + dragReason +
                        ": travel=" + travel.ToString("0") + "px slop=" + slop.ToString("0") +
                        "px held=" + (Time.unscaledTime - _pressStartedAt).ToString("0.00") +
                        "s budget=" + TapMaxSeconds.ToString("0.00") + "s. No upgrade page opens " +
                        "from a pan or pinch (WO-1712: the town camera owns those).");
                }
                return false;
            }

            _pressActive = false;
            if (tooFar || tooLong) return false;

            screen = now;
            return true;
        }

        /// <summary>
        /// The slop budget in REAL device pixels: the authored reference budget scaled by this
        /// screen's width against <see cref="ReferenceScreenWidth"/>. Clamped at the reference
        /// value so a narrow window can never make the tap test harder than authored.
        /// Public + static so a regression can assert the scaling without a play session.
        /// </summary>
        public static float TapSlopPixels(float screenWidth)
        {
            if (screenWidth <= 0f) return TapSlopReferencePx;
            float scaled = TapSlopReferencePx * (screenWidth / ReferenceScreenWidth);
            return Mathf.Max(TapSlopReferencePx, scaled);
        }

        private static float TapSlopPixels() => TapSlopPixels(Screen.width);

        private static bool PressBeganThisFrame()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).phase == TouchPhase.Began;
            return Input.GetMouseButtonDown(0);
        }

        private static bool PressEndedThisFrame()
        {
            if (Input.touchCount > 0)
            {
                TouchPhase phase = Input.GetTouch(0).phase;
                return phase == TouchPhase.Ended || phase == TouchPhase.Canceled;
            }
            return Input.GetMouseButtonUp(0);
        }

        private static Vector2 PointerScreenPosition()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return Input.mousePosition;
        }

        /// <summary>
        /// Ignore world taps for this frame and the next. A UI surface that consumed a
        /// pointer-press calls this so the one OS click is not ALSO raycast into the world —
        /// the same two-frame seam WallRepairController.SuppressNextWorldTap provides.
        /// </summary>
        public void SuppressNextWorldTap() => _suppressTapUntilFrame = Time.frameCount + 1;

        // =====================================================================
        //  The tap
        // =====================================================================

        private void HandleTap(Vector2 screen)
        {
            // Guard 1 — a UI surface consumed this press and said so.
            if (Time.frameCount <= _suppressTapUntilFrame)
            {
                FlowTrace.Throttle("TapUpgrade", "tap-suppressed", 2f,
                    "world tap IGNORED - a UI surface consumed this press and called " +
                    "SuppressNextWorldTap (two-frame window, frame=" + Time.frameCount + ").");
                return;
            }

            // Guard 2 — a panel owns the tap while it is open, and also on the very press
            // that closed it (InCloseGrace), so closing a page cannot re-open one behind it.
            if (PanelManager.AnyOpen || PanelManager.InCloseGrace)
            {
                FlowTrace.Throttle("TapUpgrade", "tap-while-panel-open", 2f,
                    "world tap IGNORED - a panel is open or just closed on this press " +
                    "(anyOpen=" + PanelManager.AnyOpen + " closeGrace=" + PanelManager.InCloseGrace +
                    "). Taps belong to the open panel, never the world behind it.");
                return;
            }

            // Guard 3 — build mode already owns world taps (UpdateSelectLoop -> SelectStructure
            // -> BuildSelectionUI Move/Upgrade/Sell). Two owners of one tap is a fight.
            var build = BuildModeController.Instance;
            if (build != null && build.IsActive)
            {
                FlowTrace.Throttle("TapUpgrade", "tap-while-build-mode", 2f,
                    "world tap IGNORED - build mode is ACTIVE and owns world taps " +
                    "(BuildModeController.UpdateSelectLoop -> SelectStructure -> BuildSelectionUI). " +
                    "WO-1712 adds a door for the IDLE town view only; it never competes with build mode.");
                return;
            }

            // ⛔ Guard 3b — THE REPAIR LOOP RAYCASTS THE SAME TAP, AND IT GETS THERE FIRST.
            // WallRepairController.Update reads Input.GetMouseButtonDown / TouchPhase.Began, i.e.
            // the PRESS; this controller fires on the RELEASE of that same press. So tapping a
            // DAMAGED tower raises the repair prompt on the way down and would open the upgrade
            // page on the way up — two surfaces from one finger. The repair prompt is a HUD
            // prompt, NOT a PanelManager-registered panel, so guard 2 does not cover it.
            // Whoever claimed the press wins: if a repair selection is live, this door stands down.
            var repair = FindAnyObjectByType<WallRepairController>();
            if (repair != null && repair.HasSelection)
            {
                FlowTrace.Throttle("TapUpgrade", "tap-while-repair-selected", 2f,
                    "world tap IGNORED - WallRepairController holds a live repair selection from " +
                    "the PRESS half of this same tap. It reads TouchPhase.Began and this door reads " +
                    "the release, so without this guard one finger on a damaged structure would " +
                    "raise the repair prompt AND the upgrade page. Repair a structure first, then " +
                    "tap it again to upgrade.");
                return;
            }

            // Guard 3c — the captured owned town has its OWN structure editor. BuildModeController
            // routes owned-town selections to OwnedTownPanel (or to a construction page for a
            // pending build) through OwnedTownJobKey, not through a catalog id + grid cell. Those
            // are different semantics and this lane has no measured evidence about them, so the
            // honest move is to stand down and say so rather than guess a key shape.
            string scene = SceneManager.GetActiveScene().name;
            if (DeNelle.Core.HubScenes.IsOwnedTown(scene))
            {
                FlowTrace.Throttle("TapUpgrade", "tap-in-owned-town", 5f,
                    "world tap IGNORED in owned town '" + scene + "' - OwnedTownPanel owns structure " +
                    "selection there (OwnedTownJobKey keys, not catalog-id@cell keys). WO-1712's door " +
                    "is the home town; extending it to the owned town is its own ticket.");
                return;
            }

            // Guard 4 — uGUI. Phase-independent EventSystem probe, so a release-frame test
            // still resolves (see the header note on IsPointerOverGameObject).
            if (BuildModeController.IsPointOverUi(screen))
            {
                FlowTrace.Throttle("TapUpgrade", "tap-over-ugui", 2f,
                    "world tap IGNORED - the press landed on a uGUI widget at screen " +
                    screen.x.ToString("0") + "," + screen.y.ToString("0") +
                    " (EventSystem.RaycastAll hit something). No world raycast, no upgrade page. " +
                    "WO-1712 acceptance 2.");
                return;
            }

            // Guard 5 — UI Toolkit. The shipped town dock is UITK and is invisible to guard 4.
            if (PointerOverPickableUi(screen, out string blocker))
            {
                FlowTrace.Throttle("TapUpgrade", "tap-over-uitk", 2f,
                    "world tap IGNORED - the press landed on a UI Toolkit element: " + blocker +
                    ". This clause is NOT redundant with the uGUI guard: the shipped town bottom " +
                    "bar is the UITK adaptive peaceful dock (CLAUDE.md sec.7), which never appears in " +
                    "a graphic raycast. WO-1712 acceptance 2.");
                return;
            }

            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                FlowTrace.Warn("TapUpgrade",
                    "world tap DROPPED - no camera to cast from (Camera.main is null). " +
                    "The upgrade door is inert this frame; Build and Manage are unaffected.");
                return;
            }
            _camera = cam;

            if (!Physics.Raycast(cam.ScreenPointToRay(screen), out RaycastHit hit, RayDistance, _selectableMask))
                return; // tapped empty sky / ground beyond the ray — nothing to say.

            var collider = hit.collider;
            if (collider == null) return;

            if (!TryResolvePanelIdFor(collider.transform, out string panelId, out string why))
            {
                // NO SILENT FAILURE (CLAUDE.md §12.2). This IS the path a player walks when they
                // tap a prop, a tree or a structure with no ladder and nothing happens — name the
                // hit chain and the refusing rule so a capture can tell "tapped scenery" from
                // "tapped a structure this door cannot open". Strings are composed into LOCALS
                // first: the compile gate's brace scanner has no interpolated-string model, so a
                // quote inside an interpolation hole desynchronises its count (CLAUDE.md §1).
                string hitName = collider.gameObject != null ? collider.gameObject.name : "<null>";
                string chain = WallRepairController.DescribeHitChain(collider.transform, 3);
                string line =
                    "tap hit '" + hitName + "' but no upgrade page could be resolved - " + why +
                    ". HIT CHAIN (up to 3 levels, hit object first): " + chain +
                    ". Resolution walks GetComponentInParent<PlacedStructure> -> <Building> -> " +
                    "<ResourceCollector>; a chain showing NONE of those is scenery on the " +
                    "selectable mask, which is expected and harmless.";
                FlowTrace.Throttle("TapUpgrade", "tap-not-upgradeable", 2f, line);
                return;
            }

            // The ONE destination — the exact call the Build screen (BuildModeController.cs:2822)
            // and the Manage screen (ManageScreenVM.cs:3067) already make.
            if (!PanelRouter.Open(PanelId.BuildingUpgrade, panelId))
            {
                FlowTrace.Fail("TapUpgrade",
                    "PanelRouter.Open(BuildingUpgrade,'" + panelId + "') returned FALSE - the page " +
                    "did NOT open from a world tap. Either BuildingUpgradePanelMvvm is not " +
                    "registered in this scene (FeatureFlags.BuildingUpgradePanel off, or the " +
                    "bootstrap skipped it) or PanelManager refused the open (battle lock). The " +
                    "Build and Manage doors call this same router and would fail identically.");
                return;
            }

            FlowTrace.Step("TapUpgrade",
                "world tap OPENED the upgrade page for '" + panelId + "' (hit '" +
                (collider.gameObject != null ? collider.gameObject.name : "?") +
                "') - WO-1712, the owner's direct-from-the-city door.");
        }

        // =====================================================================
        //  Panel id resolution — composed from the EXISTING authorities only
        // =====================================================================

        /// <summary>
        /// Resolves the upgrade-page panel id for whatever structure owns
        /// <paramref name="hit"/>, or false with <paramref name="why"/> naming the refusing
        /// rule. Precedence mirrors BuildModeController.UpgradeSelected exactly:
        /// a PlacedStructure's catalog id wins, then a Building's id, then a
        /// ResourceCollector's id.
        /// <para>A city/resource family opens under its LADDER id; a placed structure opens
        /// under its JOB KEY. Neither string is spelled here — PlacedUpgradeKey.Compose and
        /// CatalogRegistry.ResolveUpgradeId own them.</para>
        /// </summary>
        public static bool TryResolvePanelIdFor(Transform hit, out string panelId, out string why)
        {
            panelId = null;
            why = "nothing resolved";

            if (hit == null) { why = "the hit transform was null"; return false; }

            var placed = hit.GetComponentInParent<PlacedStructure>();
            if (placed != null && !string.IsNullOrEmpty(placed.itemId))
            {
                string upgradeId = CatalogRegistry.ResolveUpgradeId(placed.itemId);
                var entry = CatalogRegistry.Get(placed.itemId);
                int maxLevel = PlacedStructureUpgradeService.MaxLevelFor(entry);
                string jobKey = PlacedUpgradeKey.Compose(placed.itemId, placed.gridCell.x, placed.gridCell.y);
                return TryComposePanelId(upgradeId, jobKey, maxLevel, out panelId, out why);
            }

            var building = hit.GetComponentInParent<Building>();
            if (building != null && !string.IsNullOrEmpty(building.BuildingId))
                return TryComposeLadderPanelId(building.BuildingId, "Building", out panelId, out why);

            var collector = hit.GetComponentInParent<ResourceCollector>();
            if (collector != null && !string.IsNullOrEmpty(collector.BuildingId))
                return TryComposeLadderPanelId(collector.BuildingId, "ResourceCollector", out panelId, out why);

            why = "no PlacedStructure, Building or ResourceCollector in the hit's parent chain";
            return false;
        }

        /// <summary>
        /// A baked / scene-built structure that carries only an id (no grid cell): it can only
        /// ever open on a City or Resource ladder, so a PlacedStructure family here means the
        /// id has no ladder this door can address.
        /// </summary>
        private static bool TryComposeLadderPanelId(string rawId, string sourceKind,
            out string panelId, out string why)
        {
            panelId = null;
            string upgradeId = CatalogRegistry.ResolveUpgradeId(rawId);
            var family = UpgradeFamilyResolver.Resolve(upgradeId);
            if (family == UpgradeFamily.City || family == UpgradeFamily.Resource)
            {
                panelId = upgradeId;
                why = sourceKind + " '" + rawId + "' opens its " + family + " ladder";
                return true;
            }

            why = sourceKind + " '" + rawId + "' resolves to upgradeId '" + upgradeId +
                  "' whose family is " + family + " - no city/resource ladder knows it, and a " +
                  "scene-built body carries no grid cell to compose a placed job key from";
            return false;
        }

        /// <summary>
        /// THE PURE RULE, extracted so a regression can prove it with no scene, no catalog and
        /// no play session: a city/resource family opens under <paramref name="upgradeId"/>;
        /// anything else opens under <paramref name="jobKey"/>, and only when the catalog
        /// ceiling actually offers a second level.
        /// </summary>
        public static bool TryComposePanelId(string upgradeId, string jobKey, int maxLevel,
            out string panelId, out string why)
        {
            panelId = null;
            var family = UpgradeFamilyResolver.Resolve(upgradeId);
            bool ladderBuilding = family == UpgradeFamily.City || family == UpgradeFamily.Resource;

            if (ladderBuilding)
            {
                panelId = upgradeId;
                why = "upgradeId '" + upgradeId + "' is a " + family + " ladder";
                return true;
            }

            if (maxLevel > 1)
            {
                panelId = jobKey;
                why = "placed structure with a level ladder (maxLevel=" + maxLevel + ")";
                return true;
            }

            why = "no upgrade ladder: family=" + family + " and the catalog ceiling is " +
                  maxLevel + " (MaxStructureLevel-clamped), so there is nothing to upgrade to";
            return false;
        }

        // =====================================================================
        //  UI Toolkit pick guard
        // =====================================================================

        /// <summary>
        /// True when a live UIDocument has a pickable element under <paramref name="screen"/>,
        /// naming the highest-sorting culprit. Dev-console documents are skipped (they sit on
        /// top legitimately and are not gameplay blockers), matching the rule
        /// BuildModeController's own panel-block probe uses.
        /// <para>UIDocuments are cached per FRAME, never fetched per probe: an uncached
        /// FindObjectsByType here is the 34 ms spike WO-1831 removed from the build-mode
        /// click guard.</para>
        /// </summary>
        private static bool PointerOverPickableUi(Vector2 screen, out string blocker)
        {
            blocker = null;

            int frame = Time.frameCount;
            if (s_uiDocs == null || s_uiDocsFrame != frame)
            {
                s_uiDocsFrame = frame;
                s_uiDocs = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);
            }
            var docs = s_uiDocs;
            if (docs == null) return false;

            float bestSort = float.MinValue;
            string foundBlocker = null;
            foreach (var doc in docs)
            {
                if (doc == null) continue;
                string docName = doc.gameObject != null ? doc.gameObject.name : "?";
                if (docName.IndexOf("Dev", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var root = doc.rootVisualElement;
                var panel = root != null ? root.panel : null;
                if (panel == null) continue;

                float sort = doc.panelSettings != null ? doc.panelSettings.sortingOrder : 0f;
                float capturedSort = sort;
                string capturedName = docName;
                var localPanel = panel;
                Guard.Try("TapUpgrade", "UITK pick on '" + capturedName + "'", () =>
                {
                    Vector2 p = RuntimePanelUtils.ScreenToPanel(localPanel, screen);
                    var picked = localPanel.Pick(p);
                    if (picked == null || capturedSort < bestSort) return;
                    bestSort = capturedSort;
                    string pickedName = string.IsNullOrEmpty(picked.name) ? picked.GetType().Name : picked.name;
                    foundBlocker = capturedName + " > " + pickedName;
                });
            }

            blocker = foundBlocker;
            return blocker != null;
        }
    }

    /// <summary>
    /// Spawns exactly one <see cref="StructureTapUpgradeController"/> per playable town
    /// scene. Mirrors BuildingUpgradePanelMvvmBootstrap: the SAME scene gate (enemy-owned
    /// raid scenes are suppressed), the SAME global dedupe, and the SAME hero-present test
    /// so Title / HeroSelect never grow a world-tap listener.
    /// </summary>
    public static class StructureTapUpgradeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void EnsureFirst()
        {
            SpawnInScene(SceneManager.GetActiveScene());
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnInScene(scene);

        private static void SpawnInScene(Scene scene)
        {
            if (!scene.IsValid()) return;

            // The door leads to BuildingUpgradePanelMvvm. With its kill-switch off no page is
            // registered at all, so a listener here could only ever log a failed open.
            if (!DeNelle.Core.FeatureFlags.BuildingUpgradePanel)
            {
                FlowTrace.Warn("TapUpgrade",
                    "world-tap upgrade door NOT armed - FeatureFlags.BuildingUpgradePanel is OFF, " +
                    "so no BuildingUpgrade page is registered for it to open (WO-1712).");
                return;
            }

            string active = SceneManager.GetActiveScene().name;
            if (DeNelle.Core.HubScenes.SuppressTownHud(active))
            {
                FlowTrace.Warn("TapUpgrade",
                    "world-tap upgrade door suppressed in enemy-owned scene '" + active +
                    "' - base-building upgrade is a TOWN flow (the WO-550 rule this mirrors).");
                return;
            }

            foreach (var existing in Object.FindObjectsByType<StructureTapUpgradeController>(
                         FindObjectsInactive.Include))
            {
                if (existing != null)
                {
                    FlowTrace.Warn("TapUpgrade",
                        "duplicate StructureTapUpgradeController suppressed (one already exists) - " +
                        "two listeners would open the page twice on one tap.");
                    return;
                }
            }

            if (Object.FindAnyObjectByType<DeNelle.Village.HeroLocomotion>() == null) return;

            var go = new GameObject("StructureTapUpgradeController");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<StructureTapUpgradeController>();
            FlowTrace.Step("TapUpgrade",
                "StructureTapUpgradeController created in '" + scene.name +
                "' (single instance) - WO-1712 world-tap door to the upgrade page.");
        }
    }
}
