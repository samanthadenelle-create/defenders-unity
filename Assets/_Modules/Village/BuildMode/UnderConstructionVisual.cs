// =============================================================================
// UnderConstructionVisual — scaffold state for a structure with a live build
// timer (WO-612, wires WO-172 BuildTimerService into placement).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// While BuildTimerService.IsBuilding(key): renderers are dimmed, EVERY combat
// behaviour on the piece is disabled (a structure under construction does not
// fight), and a small world-space countdown floats above the piece. On JobCompleted
// (or the offline sweep having already finished the job) the visual reveals: colors
// restore, behaviours re-enable, this component removes itself.
//
// Self-healing: reveal is driven BOTH by the JobCompleted event and by an
// IsBuilding() check each Update — a missed event (scene churn, load order)
// can delay the reveal by at most one frame, never strand a ghost scaffold.
//
// -- 2026-08-04: THE SCAFFOLD ONLY SILENCED HALF THE TOWERS -------------------
// This file gated exactly ONE component type -- DefenseTower -- so the AoE
// ArcaneTower (catalog row 'tower_arcane_spire', behaviorId "ArcaneTower") acquired
// targets and detonated blasts for its ENTIRE build timer. The owner's own F8
// capture (logs/f8-inbox/LATEST_CAPTURE.md, 2026-08-04 21:29) is the proving data:
// five [Flow:BuildTimerUI] 'tower_arcane_spire@..' scaffolds ticking at
// remaining=270s while [Flow:HUD] reports wave=True -- i.e. 4.5 minutes of free
// defence per spire. The exploit was worth 15 s before WO-855 Phase 4 derived the
// build tier from the cost basket; that change stretched the same hole by up to 18x.
// SilenceCombat/RestoreCombat below now cover every combat family a placed
// structure can carry. See SilenceCombat for what is deliberately left alone.
// =============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;              // WO-899 §4: the countdown's world-space plate (Image)
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;             // WO-899 §4: ElarionUiKit.ApplyRounded / ElarionUi palette

namespace DeNelle.Village
{
    /// <summary>
    /// Scaffold state for a structure whose construction timer is running
    /// (WO-612). Attach via <see cref="Attach"/>; it reveals and removes itself
    /// when the job completes.
    /// </summary>
    public sealed class UnderConstructionVisual : MonoBehaviour
    {
        private bool _frozenOwnedSnapshot;
        private const float DimFactor = 0.45f;   // scaffold grey-down of the albedo

        private string _key;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Color[]> _originalColors = new List<Color[]>();

        /// <summary>The combat behaviours this scaffold SILENCED for the duration of the build
        /// job. Only ones that were live at Bind are recorded, so Reveal restores the exact prior
        /// state (see <see cref="SilenceCombat"/>). Replaces the single DefenseTower-only
        /// `_disabledTower` field that let every Arcane Spire fight through its timer.</summary>
        private readonly List<Behaviour> _silenced = new List<Behaviour>();

        // ── WO-899 §4: the build/upgrade COUNTDOWN, on a plate ─────────────────────────
        // Owner 2026-08-07, verbatim: "add a box around count down timer or make the color
        // black. I can not see them until very close for structures upgrading."
        //
        // WHAT IT WAS: a bare 3D TextMeshPro (fontSize 4) parented straight to the structure.
        // Three things made it unreadable at range, and the plate fixes all three:
        //   1. NO BACKGROUND. Gold glyphs sat directly on whatever the world happened to be
        //      behind them (grass, stone, another building). A near-opaque dark plate is the
        //      "box" the owner asked for and is what actually buys the contrast.
        //   2. NO HOST-SCALE COMPENSATION. It inherited the structure's lossy scale, so the
        //      same timer rendered at wildly different sizes per building.
        //   3. IT SHRANK WITH DISTANCE like any world object. The plate now grows with camera
        //      distance up to a cap, so it holds a roughly constant on-screen size out to
        //      ~50 m instead of vanishing.
        // Colour carries NOTHING here (owner is red/green colourblind): the meaning is the
        // digits, and the plate is the same obsidian in every state.
        private TMP_Text _label;
        private Transform _labelRoot;      // the world-space canvas root: billboarded + distance-scaled
        private float _labelBaseScale = PlateWorldScale;

        private const float PlateWorldScale  = 0.01f;   // canvas ref-px -> metres (300x110 px => 3.0 x 1.1 m)
        private const float PlateRefDistance = 16f;     // below this the plate keeps its natural size
        private const float PlateMaxGrow     = 3.2f;    // ...and never grows past this (readable to ~50 m)

        // Owner 2026-07-24: the CALM "work in progress" tell — the owner-tagged
        // "UpgradeVisual_Aura" (a slowly-circling orb) held as ONE persistent loop parented to
        // the structure WHILE the build/upgrade timer runs. Handle-managed through the single
        // VFXManager Hovl pool; Stop()'d in Reveal (completion) + OnDestroy (cancel/teardown) so
        // it never lingers under the completion fireworks and never leaks a loop slot.
        private VFXHandle _upgradeLoop;

        // WO-871: the builder NPC that stands at this structure and WORKS for exactly as long as
        // this scaffold lives. Held as a handle beside _upgradeLoop and released by the SAME three
        // seams (Reveal on completion, OnDestroy on cancel/move/teardown, plus the worker's own
        // self-heal when this component vanishes) so it can never be orphaned -- WO-753.
        // The worker asks NOTHING about build state: this component is its only authority.
        private ConstructionWorker _worker;

        /// <summary>Job key for a placed structure — unique per placement (id + cell).</summary>
        // Delegates to the ONE composer (PlacedUpgradeKey) — the shape is grammar the
        // UpgradeFamilyResolver reads, so it may not be spelled by hand in a second place.
        public static string KeyFor(PlacedStructureData data)
            => DeNelle.Village.Buildings.Progression.PlacedUpgradeKey.Compose(
                   data.itemId, data.cellX, data.cellZ);

        /// <summary>Attach the scaffold to a freshly placed / freshly loaded structure.</summary>
        public static void Attach(PlacedStructure ps, string key)
            => Attach(ps != null ? ps.gameObject : null, key);

        /// <summary>
        /// Attach the scaffold + world-space countdown to ANY structure/building GameObject
        /// (F8 owner 2026-07-17). Idempotent — a host already scaffolded is a no-op. Used by
        /// placement (through the <see cref="PlacedStructure"/> overload) and by the building
        /// -upgrade seams (through <see cref="AttachToBuildingId"/>).
        /// </summary>
        public static void Attach(GameObject host, string key, bool frozenOwnedSnapshot = false)
        {
            if (host == null || string.IsNullOrEmpty(key)) return;
            if (host.GetComponent<UnderConstructionVisual>() != null) return;   // already scaffolded
            var visual = host.AddComponent<UnderConstructionVisual>();
            visual._frozenOwnedSnapshot = frozenOwnedSnapshot;
            visual.Bind(key);
        }

        /// <summary>
        /// F8 (owner 2026-07-17 "an upgrade timer that doesn't tell"): show the CoC-style
        /// on-building countdown for a CITY / RESOURCE building upgrade. Those upgrades run
        /// through the tabbed MVVM panel / Yarn (BuildingUpgradeService / ResourceBuildingState),
        /// whose only feedback was a panel status line that vanished when the panel closed —
        /// nothing PERSISTENT told the player the upgrade was in flight. This reuses the WO-612
        /// scaffold + world countdown (dim + "M:SS" label) on the LIVE building(s) whose timer is
        /// keyed by <paramref name="buildingId"/> (the SAME id BuildTimerService.StartUpgrade used,
        /// and the SAME id BuildingUpgradeService.ApplyStructureHp targets). The label self-heals to
        /// reveal + the tier/HP applies when the timer completes.
        ///
        /// Matches the building by UpgradeCatalogId (city ids), then BuildingId, then a gameObject
        /// -name contains (resource ids farm / lumbermill / forge). Guard-wrapped so a bad match
        /// logs + skips and NEVER blocks the upgrade; a no-match is a traced no-op (the timer still
        /// runs — only the visual is absent, e.g. the building isn't spawned in this scene).
        /// </summary>
        public static void AttachToBuildingId(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return;
            Guard.Try("BuildTimerUI", $"attach upgrade countdown for '{buildingId}'", () =>
            {
                var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
                if (buildings == null) return;

                string want = buildingId.ToLowerInvariant();
                int attached = 0;
                foreach (var b in buildings)
                {
                    if (b == null || !BuildingMatches(b, want)) continue;
                    Attach(b.gameObject, buildingId);
                    attached++;
                }

                var svc = BuildTimerService.Instance;
                double rem = svc != null ? svc.RemainingSeconds(buildingId) : 0;
                if (attached > 0)
                    FlowTrace.Step("BuildTimerUI",
                        $"upgrade '{buildingId}' countdown attached to {attached} building(s) remaining={rem:0}s");
                else
                    FlowTrace.Warn("BuildTimerUI",
                        $"upgrade '{buildingId}' countdown found NO live building to anchor (timer still runs) remaining={rem:0}s");
            });
        }

        // Match a live Building to an upgrade-timer id: city catalog id first (same match
        // ApplyStructureHp uses), then the raw BuildingId, then a name contains (resource ids).
        private static bool BuildingMatches(Building b, string wantLower)
        {
            string cat = b.UpgradeCatalogId;
            if (!string.IsNullOrEmpty(cat) && cat.ToLowerInvariant() == wantLower) return true;
            string bid = b.BuildingId;
            if (!string.IsNullOrEmpty(bid) && bid.ToLowerInvariant() == wantLower) return true;
            var go = b.gameObject;
            return go != null && go.name.ToLowerInvariant().Contains(wantLower);
        }

        private void Bind(string key)
        {
            _key = key;
            Guard.Try("Build", "scaffold dim", () =>
            {
                GetComponentsInChildren(true, _renderers);
                foreach (var r in _renderers)
                {
                    var mats = r.materials;   // instances — safe to tint per-structure
                    var saved = new Color[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!mats[i].HasProperty("_BaseColor") && !mats[i].HasProperty("_Color")) continue;
                        string prop = mats[i].HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                        saved[i] = mats[i].GetColor(prop);
                        Color c = saved[i] * DimFactor;
                        c.a = saved[i].a;
                        mats[i].SetColor(prop, c);
                    }
                    _originalColors.Add(saved);
                }
            });

            // A structure under construction does not fight (walls keep blocking -- the scaffold
            // is still physically there; only the ACTIVE behaviours pause).
            SilenceCombat();

            // WO-871: stand a builder at the site for the duration of the job. Null-safe no-op when
            // the body/controller assets are missing or the concurrent-worker cap is reached -- the
            // build then shows exactly what it showed before (dim + countdown + aura).
            // Deliberately BEFORE the countdown label + the aura are parented: the worker anchors
            // itself off the structure's renderer bounds, and a TextMeshPro label / particle system
            // hanging off the same transform would inflate those bounds and push it out of position.
            //
            // GUARDED like every other risky step in this method, and for a reason that was PROVEN
            // rather than imagined: on 2026-08-04 an Animator quirk inside the spawn threw
            // IndexOutOfRangeException straight out of Attach (Builds/wo871-stack.log). Attach is
            // called from BuildModeController placement and from BaseLayoutLoader on load, so an
            // unguarded cosmetic spawn can abort a real structure being placed or reloaded. A worker
            // is DECORATION -- it must never be able to break a build.
            Guard.Try("Build", "stand a build worker at the site", () =>
            {
                if (!_frozenOwnedSnapshot) _worker = ConstructionWorkerPool.Spawn(this, transform, _key);
            });

            Guard.Try("Build", "scaffold label", () =>
            {
                using var _plateFlow = FlowTrace.Enter("Build", "countdown plate");

                // Read the host bounds BEFORE anything is parented (the worker above relies on the
                // same rule). A world-space Canvas adds only CanvasRenderers, never a Renderer, so
                // it cannot inflate these bounds -- but the ordering stays deliberate.
                float top = 2.5f;
                var rend = GetComponentInChildren<Renderer>();
                if (rend != null) top = rend.bounds.size.y + 1.2f;

                float hostScale = Mathf.Abs(transform.lossyScale.x);
                if (hostScale < 0.0001f || float.IsNaN(hostScale) || float.IsInfinity(hostScale)) hostScale = 1f;
                _labelBaseScale = PlateWorldScale / hostScale;

                // Canvas FIRST: it RequireComponent(RectTransform), so adding it converts the
                // GameObject's Transform. Placement is set after the conversion so the values
                // land on the RectTransform that actually survives.
                var go = new GameObject("BuildCountdown");
                go.transform.SetParent(transform, false);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;   // NO GraphicRaycaster: it is a readout
                var crt = (RectTransform)go.transform;
                crt.sizeDelta = new Vector2(300f, 110f);     // reference px; * scale => ~3.0 x 1.1 m
                crt.localPosition = new Vector3(0f, top, 0f);
                crt.localRotation = Quaternion.identity;
                crt.localScale = Vector3.one * _labelBaseScale;
                _labelRoot = go.transform;

                // The gold-dim RIM peeks out from behind the near-opaque obsidian face.
                var rim = new GameObject("Rim", typeof(RectTransform), typeof(Image));
                rim.transform.SetParent(go.transform, false);
                var rimRt = (RectTransform)rim.transform;
                rimRt.anchorMin = Vector2.zero; rimRt.anchorMax = Vector2.one;
                rimRt.offsetMin = Vector2.zero; rimRt.offsetMax = Vector2.zero;
                var rimImg = rim.GetComponent<Image>();
                ElarionUiKit.ApplyRounded(rimImg);
                rimImg.color = new Color(0.604f, 0.498f, 0.243f, 0.95f);   // #9a7f3e gold-dim
                rimImg.raycastTarget = false;

                var plate = new GameObject("Plate", typeof(RectTransform), typeof(Image));
                plate.transform.SetParent(go.transform, false);
                var prt = (RectTransform)plate.transform;
                prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
                prt.offsetMin = new Vector2(5f, 5f); prt.offsetMax = new Vector2(-5f, -5f);
                var plateImg = plate.GetComponent<Image>();
                ElarionUiKit.ApplyRounded(plateImg);
                plateImg.color = new Color(0.039f, 0.047f, 0.063f, 0.92f);   // near-opaque obsidian
                plateImg.raycastTarget = false;

                var txt = new GameObject("Countdown", typeof(RectTransform));
                txt.transform.SetParent(go.transform, false);
                var trt = (RectTransform)txt.transform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(14f, 10f); trt.offsetMax = new Vector2(-14f, -10f);
                var tmp = txt.AddComponent<TextMeshProUGUI>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontStyle = FontStyles.Bold;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 28f;
                tmp.fontSizeMax = 76f;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.color = new Color(1f, 0.85f, 0.4f);   // kit gold ON the dark plate
                tmp.raycastTarget = false;
                tmp.text = "...";                          // ASCII ONLY (the build font has no ellipsis glyph)
                _label = tmp;

                FlowTrace.Step("Build",
                    $"countdown plate built for '{_key}' at +{top:0.0}m (hostScale={hostScale:0.00}, baseScale={_labelBaseScale:0.0000}).");
            });

            // Start the CALM slow-circling "being worked on" loop (owner-tagged key). Parented to
            // the structure so the orb orbits it; a persistent LOOP (returns a handle) held for the
            // duration of the timer. Null-safe no-op if the key/prefab is missing; motion-based =>
            // colorblind-safe. Reveal()/OnDestroy() Stop() it (immediate) so the fireworks payoff is
            // never muddied by a lingering circle.
            _upgradeLoop = VFXManager.PlayKey("UpgradeVisual_Aura",
                transform.position + Vector3.up * 1f, Quaternion.identity, transform);

            var svc = BuildTimerService.Instance;
            if (svc != null) svc.JobCompleted += OnJobCompleted;
            FlowTrace.Step("Build", $"under-construction armed for '{_key}'"
                + (_upgradeLoop != null ? " (circling upgrade loop held)." : " (upgrade loop no-op — key/catalog not ready).")
                + (_worker != null ? " Builder worker on site." : " No builder worker (assets absent or worker cap reached)."));
        }

        // =====================================================================
        //  The construction GATE -- one place decides whether a structure may act
        // =====================================================================

        /// <summary>
        /// A structure with an in-flight build job does NOT fight. Silences EVERY combat
        /// behaviour family a placed structure can carry -- not just <see cref="DefenseTower"/>:
        ///
        ///   * <see cref="DefenseTower"/> -- the single-target archer / wall-wizard / siege /
        ///     catapult tower (behaviorId "DefenseTower"; four catalog rows). The ONLY family
        ///     this method used to cover.
        ///   * <see cref="ArcaneTower"/> -- the AoE arcane spire (behaviorId "ArcaneTower",
        ///     catalog row 'tower_arcane_spire'). THE 2026-08-04 DEFECT: a spire ran its full
        ///     Acquire/FireBlast loop through the whole build timer because
        ///     GetComponentInChildren&lt;DefenseTower&gt;() returns null on it. Proven by the
        ///     owner's F8 capture (five spires at remaining=270s during a live wave).
        ///   * <see cref="TowerCombat"/> -- the fire loop of the OTHER tower family
        ///     (Tower.EnsureCombat). Not reachable from BuildModeController placement today,
        ///     but a gate must never half-cover a type split -- that is how this bug happened.
        ///
        /// Only behaviours that were ENABLED are recorded, so <see cref="RestoreCombat"/>
        /// restores the exact prior state and can never switch on something deliberately off.
        ///
        /// DELIBERATELY NOT SILENCED:
        ///   * <c>Tower</c> itself -- its OnEnable/OnDisable maintain the live tower registry the
        ///     HUD counts (Tower.cs), so disabling the component would miscount the town's
        ///     towers. TowerCombat above is the thing that actually fires.
        ///   * WallSegment / Gate -- a scaffold is still physically there; walls must keep
        ///     blocking and keep their "Structure" LoS layer, or towers shoot through the site.
        ///   * Every economy behaviour (CrystalMine / ResourceCollector / HealingFountain).
        ///     They share this defect but are owned by other lanes -- reported, not fixed here.
        ///
        /// A structure with NO scaffold (a baked arena/garrison tower, an EnemyOwned turret,
        /// the prepaid tutorial tower) never reaches this method at all: the gate is opt-in by
        /// attachment, so those paths are byte-identical to before.
        /// </summary>
        private void SilenceCombat()
        {
            _silenced.Clear();
            CollectEnabled<DefenseTower>(_silenced);
            CollectEnabled<ArcaneTower>(_silenced);
            CollectEnabled<TowerCombat>(_silenced);

            for (int i = 0; i < _silenced.Count; i++) _silenced[i].enabled = false;

            // Sec.12 proving line: a capture must show the tower WITHHOLDING FIRE and why, so
            // "the queued tower still shot me" can never be diagnosed by theory again.
            if (_silenced.Count > 0)
                FlowTrace.Step("BuildGate",
                    $"'{_key}' UNDER CONSTRUCTION -> WITHHOLDING FIRE on '{name}': silenced " +
                    $"{_silenced.Count} combat behaviour(s) [{Describe(_silenced)}]. It cannot acquire, " +
                    "fire or damage until the build job completes.");
            else
                FlowTrace.Step("BuildGate",
                    $"'{_key}' UNDER CONSTRUCTION on '{name}': no combat behaviour to silence " +
                    "(non-combat structure) -- dim + countdown only.");
        }

        /// <summary>
        /// Release the gate: re-enable exactly what <see cref="SilenceCombat"/> silenced, so a
        /// completed tower engages from THIS frame with no relaunch. Components destroyed with
        /// the structure in the meantime are skipped (never a null-deref on teardown).
        /// </summary>
        private void RestoreCombat()
        {
            if (_silenced.Count == 0) return;
            int restored = 0;
            for (int i = 0; i < _silenced.Count; i++)
            {
                var b = _silenced[i];
                if (b == null) continue;   // torn down with the structure while building
                b.enabled = true;
                restored++;
            }
            FlowTrace.Step("BuildGate",
                $"'{_key}' BUILD COMPLETE -> FIRE RELEASED on '{name}': re-enabled {restored}/" +
                $"{_silenced.Count} combat behaviour(s) [{Describe(_silenced)}]. Engages this frame.");
            _silenced.Clear();
        }

        /// <summary>Append every ENABLED <typeparamref name="T"/> in this hierarchy (inactive
        /// objects included) to <paramref name="into"/>.</summary>
        private void CollectEnabled<T>(List<Behaviour> into) where T : Behaviour
        {
            var found = GetComponentsInChildren<T>(true);
            if (found == null) return;
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null && found[i].enabled) into.Add(found[i]);
        }

        /// <summary>Comma-joined type names, for the trace lines above.</summary>
        private static string Describe(List<Behaviour> list)
        {
            if (list == null || list.Count == 0) return "none";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i] != null ? list[i].GetType().Name : "<destroyed>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Reads construction from the attached scaffold's authority. Ordinary structures use
        /// the live queue; owned structures also retain their saved unfinished state. Practice
        /// scaffolds represent the frozen entry build, independent of the owner's live timer.
        /// </summary>
        public static bool IsUnderConstruction(GameObject host)
        {
            if (host == null) return false;
            var scaffold = host.GetComponentInChildren<UnderConstructionVisual>(true);
            if (scaffold == null) return false;
            if (scaffold._frozenOwnedSnapshot) return true;
            if (DeNelle.Core.State.OwnedTownJobKey.TryParse(scaffold._key, out var baseId, out var instanceId))
            {
                var property = DeNelle.Core.State.GameStateService.Instance?.State?.OwnedBase;
                if (property?.baseId == baseId && property.structures.Exists(s => s.instanceId == instanceId && s.constructionPending))
                    return true;
            }
            var svc = BuildTimerService.Instance;
            return svc != null && svc.IsBuilding(scaffold._key);
        }

        /// <summary>
        /// F8-51: the job key is cell-derived, so a structure MOVED mid-timer re-keys this
        /// scaffold (paired with BuildTimerService.RepointJob) — otherwise the self-heal
        /// poll sees IsBuilding(oldKey)==false and reveals early while the job still runs.
        /// </summary>
        public void Rekey(string newKey)
        {
            if (!string.IsNullOrEmpty(newKey)) _key = newKey;
        }

        private void Update()
        {
            var svc = BuildTimerService.Instance;
            bool savedPending = false;
            if (DeNelle.Core.State.OwnedTownJobKey.TryParse(_key, out var baseId, out var instanceId))
            {
                var property = DeNelle.Core.State.GameStateService.Instance?.State?.OwnedBase;
                savedPending = property?.baseId == baseId && property.structures.Exists(s => s.instanceId == instanceId && s.constructionPending);
            }
            if (!_frozenOwnedSnapshot && !savedPending && (svc == null || !svc.IsBuilding(_key))) { Reveal(); return; }

            if (_label != null)
            {
                double s = !_frozenOwnedSnapshot && svc != null ? svc.RemainingSeconds(_key) : 0;
                // F8 (owner 2026-07-17): headless proof the countdown SHOWS + TICKS (~1/s).
                FlowTrace.Throttle("BuildTimerUI", _key, 1f, $"'{_key}' remaining={s:0}s");
                _label.text = _frozenOwnedSnapshot || (savedPending && (svc == null || !svc.IsBuilding(_key)))
                    ? "..." : s >= 60 ? $"{(int)(s / 60)}:{(int)(s % 60):00}" : $"{(int)s}s";
            }

            // WO-899 §4: billboard the PLATE (not the text) and hold a roughly constant
            // on-screen size out to PlateRefDistance * PlateMaxGrow metres, so an upgrading
            // structure's timer is readable from across town instead of only up close.
            if (_labelRoot != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 p = _labelRoot.position;
                    Vector3 toCam = p - cam.transform.position;
                    if (toCam.sqrMagnitude > 1e-4f)
                    {
                        _labelRoot.rotation = Quaternion.LookRotation(toCam);
                        float k = Mathf.Clamp(toCam.magnitude / PlateRefDistance, 1f, PlateMaxGrow);
                        _labelRoot.localScale = Vector3.one * (_labelBaseScale * k);
                    }
                }
            }
        }

        private void OnJobCompleted(BuildJobData job)
        {
            if (!_frozenOwnedSnapshot && job.StructureId == _key) Reveal();
        }

        /// <summary>
        /// Construction finished: restore the albedo, RELEASE the combat gate
        /// (<see cref="RestoreCombat"/>), drop the countdown + the circling loop, and remove
        /// this component. Driven by the JobCompleted event and by the Update self-heal; PUBLIC
        /// so a headless regression can drive the completed-tower case without a play session
        /// (there is no other way in -- the event needs a live BuildTimerService).
        /// </summary>
        public void Reveal()
        {
            Guard.Try("Build", "scaffold reveal", () =>
            {
                for (int i = 0; i < _renderers.Count && i < _originalColors.Count; i++)
                {
                    if (_renderers[i] == null) continue;
                    var mats = _renderers[i].materials;
                    var saved = _originalColors[i];
                    for (int m = 0; m < mats.Length && m < saved.Length; m++)
                    {
                        if (!mats[m].HasProperty("_BaseColor") && !mats[m].HasProperty("_Color")) continue;
                        string prop = mats[m].HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                        if (saved[m] != default) mats[m].SetColor(prop, saved[m]);
                    }
                }
                RestoreCombat();
                // WO-899 §4: drop the WHOLE plate (canvas root), not just the text child -- the
                // label is now a grandchild, so destroying it alone would strand the plate.
                if (_labelRoot != null) DestroyHost(_labelRoot.gameObject);
                else if (_label != null) DestroyHost(_label.gameObject);
                _labelRoot = null;
                _label = null;
            });
            // Stop the circling loop IMMEDIATELY on completion so it can't linger under the
            // UpgradeStructureComplete_Aura fireworks the upgrade-apply path fires this same beat.
            StopUpgradeLoop();
            // WO-871: the builder leaves the instant the job completes -- same beat, same discipline.
            StopWorker();
            FlowTrace.Step("Build", $"construction complete — revealed '{_key}'");
            DestroyHost(this);
        }

        /// <summary>
        /// Destroy that also works OUTSIDE play mode. UnityEngine.Object.Destroy THROWS in edit
        /// mode ("Destroy may not be called from edit mode"), which would abort the reveal
        /// half-done for any headless edit-mode caller. Play mode is untouched -- it takes the
        /// deferred Destroy exactly as before.
        /// </summary>
        private static void DestroyHost(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void OnDestroy()
        {
            // Catch-all for cancel / host-destroy / scene teardown: never leak the circling loop,
            // and never leave a builder standing at a structure that is gone (WO-753).
            StopUpgradeLoop();
            StopWorker();
            var svc = BuildTimerService.Instance;
            if (svc != null) svc.JobCompleted -= OnJobCompleted;
        }

        /// <summary>Return the build-site worker to its pool (idempotent -- safe from both Reveal
        /// and OnDestroy; the handle is nulled so the second call is a no-op). The worker is never
        /// a child of this structure, so this is safe to call while the host is being destroyed.</summary>
        private void StopWorker()
        {
            if (_worker == null) return;
            _worker.Release();
            _worker = null;
        }

        /// <summary>Return the circling upgrade loop to its pool (idempotent — safe to call from
        /// both Reveal and OnDestroy; the handle is nulled so the second call is a no-op).</summary>
        private void StopUpgradeLoop()
        {
            if (_upgradeLoop == null) return;
            _upgradeLoop.Stop(immediate: true);
            _upgradeLoop = null;
        }
    }
}
