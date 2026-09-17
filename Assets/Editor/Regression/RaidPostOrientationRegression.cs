// =============================================================================
// RaidPostOrientationRegression [raid-post-orientation]
//   markers RAID_POST_ORIENTATION_OK / _FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Edit mode, opens the BAKED raid scenes.
// Registered ONCE in DataRegression.RunAll.  NEVER throws.
//
// WHAT IT LOCKS (WO-1807, 2026-09-16 — owner: "corners seem upside down",
// "towers are inverted", RaidBase_fortified_garrison, build 2026.09.17.372984):
//
// RaidBaseDresser.ReplaceChildrenWith clads every CornerPost_* / Watchtower_* /
// RaidSpire with a "/Visual" child. It used to destroy CHILDREN only — and the
// prefabs it clads carry their mesh on the ROOT (Assets/StructureContent/
// Tower_Medieval_Wood.prefab is ONE GameObject with MeshFilter + MeshRenderer +
// MeshCollider and `m_Children: []`). So the clad was STACKED on the original and
// every corner post was TWO mismatched towers in one place.
//
// ⛔ THE OBVIOUS ASSERTIONS WOULD HAVE PASSED THE BROKEN BAKE. This is the whole
// reason this file is written the way it is. On the owner's build:
//   * the host rotation is pure-yaw — CornerPost_Outer_E is w=-0.38268 y=0.92388
//     (225°) in the baked YAML, x=z=0;
//   * the /Visual child's local rotation is IDENTITY (w=1);
//   * the clad's bounds are 4.3 x 7.5 x 4.3 m, tall-in-Y, min.y ≈ 0.05.
// So "up-vector dot Vector3.up > 0.9" and "bounds.min.y within 0.5 m of ground"
// — the two natural pins — were BOTH GREEN while the owner was looking at the
// defect. An oracle that only asks those questions is theatre.
//
// THEREFORE PIN 1 IS THE LOAD-BEARING ONE: no enabled Renderer on the host ROOT.
// A root renderer IS the second tower. Pins 2–4 stay, because they are cheap and
// they catch the OTHER ways this can break (a clad that fails to instantiate, a
// clad pitched by the wall-panel −90 X correction, a clad left floating by a bad
// seat) — but a green here means pin 1 held, not merely that nothing was tipped.
//
// ⚠ NO SECOND COPY OF THE RULE. DeNelle.EditorRegression cannot reference
// DeNelle.Editor, so this cannot call RaidPostAudit.Evaluate, and that separation
// is deliberate: an oracle that imports its subject's own helper cannot catch the
// subject changing it (the same reasoning RaidWallMaterialRegression's header
// records). The audit is the INSTRUMENT (it also takes the PNGs); this is the GATE.
//
// Scene list is DISCOVERED from disk — never copied from RaidNavBake.RaidScenes or
// from a doc (CLAUDE.md §2/§5/§16).
//
// Entry points:
//   Run(out report)  — DataRegression suite shape.
//   RunAll()         — standalone, prints the marker.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core;            // CanonicalJson (WO-1817 pin 5)
using DeNelle.Core.Catalog;    // CatalogEntry / RepoProps
using DeNelle.Village;             // DefenseTower, StructureFactory.OptsFor
using DeNelle.Village.World.Camps; // RaidSpire.VisualHeight (WO-1820)
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor.Regression
{
    public static class RaidPostOrientationRegression
    {
        /// <summary>
        /// PANCAKE threshold for the clad's bounds ratio (size.y / max(size.x, size.z)).
        ///
        /// ⚠ THIS IS DELIBERATELY **NOT** RaidBaseGenerator.EnsureUpright's 0.8, AND THE REASON IS
        /// MEASURED, NOT PREFERRED. The first cut of this file used 0.8 — EnsureUpright's own rule —
        /// and the post-fix audit (`Builds/raid-post-audit-after.log`, 2026-09-16 20:46) reported 14
        /// "renders FLAT" defects at **ratio 0.75 / 0.76**, every one of them a `hexagon-green` kit
        /// post clad with `building_watchtower_green`. Those posts were then OPENED as frames
        /// (`Builds/raid-post-audit/RaidBase_raider_camp_small_CornerPost_Outer_E.png`): a clean,
        /// upright, crenellated stone watchtower standing correctly on the corner. The art is simply
        /// SQUAT. EnsureUpright's own warning text says this in as many words — *"If the art is
        /// genuinely squat, this is a false positive."*
        ///
        /// So the two thresholds answer two different questions and must not share a number:
        ///   * EnsureUpright asks "should I ROTATE this model?" — it can afford to be eager,
        ///     because rotating a squat prop is a visible mistake someone will notice.
        ///   * this gate asks "is a clad PANCAKED?" — it must not red on shipped, correct art,
        ///     because a gate that cries wolf gets ignored (see RaidWallMaterialRegression's header).
        ///
        /// 0.65 is derived from the three measured populations, not tuned until green:
        ///   * a clad pitched -90 X — the failure this pin exists to catch — is 4.30 / 7.52 = **0.57**
        ///     (the Synty `SM_Bld_Castle_Wall_Tower_M_01` bounds, read off the same audit);
        ///   * legitimately squat shipped art measures **0.75 / 0.76**;
        ///   * correct upright clads measure **1.75, 1.84, 2.16**.
        /// 0.65 sits between 0.57 and 0.75 with margin on both sides. If a future clad lands between
        /// them, OPEN THE FRAME and judge it — do not move this number to make a red go away.
        ///
        /// The definitive inversion test is <see cref="MinUpDot"/> below, which is exact and has no
        /// such ambiguity; this pin is the backstop for a clad that is upright-transformed but
        /// pancaked (e.g. a height fit applied on the wrong axis).
        /// </summary>
        private const float UprightRatio = 0.65f;

        /// <summary>Minimum up-vector alignment for a clad that is not pitched or inverted.</summary>
        private const float MinUpDot = 0.9f;

        /// <summary>How far a seated post's lowest RENDERED point may sit from the ground.</summary>
        private const float SeatToleranceM = 0.5f;

        /// <summary>
        /// WO-1817 — how far a <c>Watchtower_*</c>'s CLAD height may sit from its authored turret
        /// cadence, as a fraction of that cadence.
        ///
        /// ⛔ THE PIN THIS FILE WAS MISSING, AND THE REASON IS THE SAME ONE ITS HEADER ALREADY GIVES.
        /// WO-1807 pinned upright-ness and seating and left absolute HEIGHT unasserted — so the four
        /// baked scenes passed `RAID_POST_AUDIT_OK 59` on 2026-09-16 20:59 while shipping watchtowers
        /// at **0.05 m** (Iron Bastion — the final raid), 0.86 m (mage enclave), 1.11 m (camp) and
        /// 7.52 m (garrison), against an authored cadence of 4.80 m. Every one of them was upright
        /// and seated. A gate that asks only "is it the right way up" passes a tower the size of a
        /// coffee cup.
        ///
        /// Cause: the clad hangs off the host with `SetParent(parent, false)`, so it renders at
        /// `hostLossyScale * cladNativeHeight` — and WO-1807 removed the host's own renderer, so the
        /// model the generator FITTED is no longer on screen at all.
        ///
        /// 10% for the same reason RaidPostAudit uses it: the measured failure spans 1%–374% of the
        /// target, two orders of magnitude clear of float noise on a bounds round-trip.
        /// </summary>
        private const float CladHeightTolerance = 0.10f;

        private const string Remedy =
            "REMEDY: the clad seam is RaidBaseDresser.ReplaceChildrenWith / StripRootArt " +
            "(Assets/Editor/WallTools/RaidBaseDresser.cs). After any change there the raid scenes " +
            "MUST be re-baked - they are baked files: run " +
            "DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes then DeNelle.Editor.RaidNavBake.BakeAll. " +
            "For the frames, run DeNelle.Editor.RaidPostAudit.AuditAll and OPEN the PNGs under " +
            "Builds/raid-post-audit/.";

        // -- entry points -----------------------------------------------------

        /// <summary>Standalone batch entry - prints the RAID_POST_ORIENTATION_OK/_FAIL marker.</summary>
        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log("RAID_POST_ORIENTATION_OK " + report);
            else Debug.LogError("RAID_POST_ORIENTATION_FAIL " + report);
        }

        public static bool Run(out string report)
        {
            try { return RunCore(out report); }
            catch (Exception ex)
            {
                report = "raid-post-orientation: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // ---------------------------------------------------------------------

        private static bool RunCore(out string report)
        {
            var failures = new List<string>();
            var notes = new StringBuilder();
            string restore = SceneManager.GetActiveScene().path;

            // Re-read the catalog every run: a static cache surviving into a later run in the same
            // editor session would judge a rebake against the PREVIOUS catalog (CLAUDE.md s.2 - a
            // copy that outlives its source is the bug).
            _catalog = null;

            var scenes = DiscoverScenes();
            if (scenes.Count == 0)
            {
                // A missing bake is a GAP, not a pass - say so and fail closed, exactly as
                // CLAUDE.md §8 says of an absent marker.
                report = "raid-post-orientation: NO RaidBase_*.unity under Assets/Scenes - the raid " +
                         "bake has never run on this clone, so nothing was checked. " + Remedy;
                return false;
            }

            int scanned = 0;
            int scenesWithPosts = 0;

            for (int i = 0; i < scenes.Count; i++)
            {
                string path = scenes[i];
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                Scene scene;
                try
                {
                    // SINGLE, mirroring RaidKeepReachRegression: an additive open in batchmode
                    // gives non-deterministic results, and a flaky oracle trains everyone to
                    // ignore it.
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                }
                catch (Exception ex)
                {
                    failures.Add($"raid-post-orientation: {sceneName} would not open ({ex.GetType().Name}: {ex.Message})");
                    continue;
                }
                if (!scene.IsValid())
                {
                    failures.Add($"raid-post-orientation: {sceneName} opened INVALID");
                    continue;
                }

                int inScene = 0;
                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    var transforms = roots[r].GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < transforms.Length; t++)
                    {
                        var tr = transforms[t];
                        if (tr == null || tr.name == null) continue;
                        // WO-1820: RaidSpire is clad by the SAME seam and was never scanned here,
                        // which is how the raid's win condition shipped at 1/100 scale unnoticed.
                        if (!tr.name.StartsWith("CornerPost_", StringComparison.Ordinal) &&
                            !tr.name.StartsWith("Watchtower_", StringComparison.Ordinal) &&
                            !string.Equals(tr.name, "RaidSpire", StringComparison.Ordinal))
                            continue;

                        inScene++;
                        scanned++;
                        string fault = Judge(tr.gameObject);
                        if (fault != null) failures.Add($"raid-post-orientation: {sceneName}/{tr.name}: {fault}");
                    }
                }

                if (inScene > 0) scenesWithPosts++;
                notes.Append(sceneName).Append('=').Append(inScene).Append(' ');
            }

            RestoreScene(restore);

            if (scanned == 0)
            {
                report = $"raid-post-orientation: {scenes.Count} raid scene(s) opened but NOT ONE carries a " +
                         "CornerPost_* or Watchtower_* - the ring was never built. This is a gap, not a pass. " +
                         Remedy;
                return false;
            }

            if (failures.Count > 0)
            {
                report = $"{failures.Count} post(s) bad of {scanned} across {scenesWithPosts} scene(s): " +
                         string.Join(" | ", failures) + " -- " + Remedy;
                return false;
            }

            report = $"raid-post-orientation: {scanned} corner post(s)/watchtower(s) across {scenesWithPosts} " +
                     $"baked scene(s) each render through EXACTLY ONE upright, seated /Visual clad with no " +
                     $"stacked root mesh [{notes.ToString().TrimEnd()}]";
            return true;
        }

        /// <summary>
        /// The four pins on one post. Returns null when clean, else the sentence naming
        /// every fault found (all of them, not the first - a seat fixing this wants the
        /// whole list from one run).
        /// </summary>
        private static string Judge(GameObject host)
        {
            var faults = new List<string>();

            // ── PIN 1 (the load-bearing one) — no art on the host ROOT ──────────
            var rootRends = host.GetComponents<Renderer>();
            for (int i = 0; i < rootRends.Length; i++)
            {
                var rend = rootRends[i];
                if (rend == null || !rend.enabled) continue;
                var mf = host.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                string mat = rend.sharedMaterial != null ? rend.sharedMaterial.name : "?";
                var rb = rend.bounds;
                faults.Add($"the HOST ROOT still renders '{mesh}' (mat '{mat}', " +
                           $"{rb.size.x:0.#}x{rb.size.y:0.#}x{rb.size.z:0.#}m) STACKED on the clad - " +
                           "two towers in one spot, which is what reads as inverted from the ground");
                break;
            }

            // ── PIN 2 — exactly one /Visual clad, and it carries the renderers ───
            var vis = host.transform.Find("Visual");
            if (vis == null)
            {
                faults.Add("no '/Visual' clad child - ReplaceChildrenWith never ran or its model failed to load");
                return string.Join("; ", faults);
            }

            if (!Encapsulate(vis.gameObject, out Bounds b))
            {
                faults.Add("'/Visual' carries NO renderer - the clad is invisible");
                return string.Join("; ", faults);
            }

            // ── PIN 3 — the clad is UPRIGHT, both ways of asking ────────────────
            float upDot = Vector3.Dot(vis.up, Vector3.up);
            if (upDot <= MinUpDot)
                faults.Add($"clad up-vector dot Vector3.up = {upDot:0.###} (<= {MinUpDot:0.0}) - it is pitched/inverted " +
                           "(the -90 X wall-panel correction must never reach a post)");

            float widest = Mathf.Max(b.size.x, b.size.z);
            float ratio = widest <= 0.0001f ? 0f : b.size.y / widest;
            if (ratio < UprightRatio)
                faults.Add($"clad bounds ratio {ratio:0.##} < {UprightRatio:0.0} " +
                           $"({b.size.x:0.#}x{b.size.y:0.#}x{b.size.z:0.#}m) - it renders FLAT");

            // ── PIN 4 — the clad is SEATED on the ground ───────────────────────
            // ⛔ THE RAID SPIRE IS EXEMPT, AND THE EXEMPTION IS MEASURED, NOT ASSUMED (WO-1820).
            // It stands on the KeepPlatform, not the ground: RaidBaseGenerator.ReseatSpireOnKeepPlatform
            // lifts it onto the slab ("1.5 m on the castle kits, 0.8 m on dungeon-stone",
            // RaidBaseGenerator.cs:2184) because WO-1749 proved a ground-seated spire ends up INSIDE
            // the slab, which cost 1650 PathPartial and ZERO PathComplete on the owner's Seeker run.
            // On the 2026-09-17 bake this pin flagged exactly those three lifts - 1.5 / 1.5 / 0.8,
            // matching the documented per-kit slab to the centimetre.
            // The platform-seat invariant is NOT re-implemented here: it already has an owner that
            // measures it properly - the generator's `SPIRE SEAT` step and the chain's
            // `OWNED_TOWN_SPIRE_RESEAT_OK` marker ("base y 1.500 vs KeepPlatform top y 1.500, delta
            // 0.0000m" on this bake). A second copy would be duplicated state (CLAUDE.md §2/§5/§16),
            // and here the copy would have been the wrong one. The spire's HEIGHT stays pinned above.
            if (host.GetComponent<RaidSpire>() == null && Mathf.Abs(b.min.y) > SeatToleranceM)
                faults.Add($"clad lowest rendered point is y={b.min.y:0.##}m, more than {SeatToleranceM:0.0}m " +
                           "off the ground - it floats or is sunk");

            // ── PIN 5 (WO-1817) — the clad renders at the authored turret cadence ──
            CheckCladHeight(host, b, faults);

            return faults.Count == 0 ? null : string.Join("; ", faults);
        }

        /// <summary>
        /// WO-1817 pin 5: a <c>Watchtower_*</c>'s clad must render within
        /// <see cref="CladHeightTolerance"/> of the height its catalog row authors.
        ///
        /// ⛔ THE EXPECTATION IS COMPUTED THROUGH A DIFFERENT PATH FROM THE ONE THAT PRODUCES IT,
        /// AND THAT IS THE WHOLE POINT. The dresser fits the clad using
        /// <c>RaidBaseGenerator.TurretCadenceHeight</c>; this gate re-derives the same number from
        /// the CATALOG plus <c>StructureFactory.OptsFor(entry).FitHeight</c> — the town's own
        /// fit-to-height authority. This file cannot reference DeNelle.Editor at all (see the header),
        /// so importing the subject's helper is not merely discouraged here, it is impossible — and
        /// an oracle that imports its subject's helper cannot catch the subject changing it.
        ///
        /// ⚠ THE TWO PATHS HAVE A KNOWN, DELIBERATE DIVERGENCE: <c>OptsFor</c> defaults
        /// <c>repo.heightMul</c> to 1.0, the raid generator defaults it to 1.2 (already recorded at
        /// StructureCadenceRegression.cs:114-121 as "RaidBaseGenerator builds its own SkinOptions").
        /// Every live turret row authors the key — tower_catapult 0.75, tower_arcane_spire 1.2,
        /// tower_ground_archer 1.2 — so they agree today. If a future row omits it the two disagree
        /// by 20% and THIS PIN REDS. That is the correct outcome, not a bug in the pin: it forces the
        /// convergence ticket rather than letting a 20% size split ship. Do NOT "fix" a red here by
        /// widening the tolerance to 0.25.
        ///
        /// Silent (not a fault) when: the post is not a Watchtower_*, carries no DefenseTower, or its
        /// row authors no cadence. A MISSING catalog row for a stamped id IS a fault — that means the
        /// bake armed a turret against a row that does not exist.
        /// </summary>
        private static void CheckCladHeight(GameObject host, Bounds cladBounds, List<string> failures)
        {
            // ── WO-1820: the SPIRE, judged against the height the bake recorded on it ──
            // Not a re-derivation of the monument clamp: RaidBaseGenerator's
            // SpireMonumentMultiplier/Min/Max are `internal` to DeNelle.EditorWallTools, which this
            // assembly cannot reference at all, so any formula here would be a COPY that drifts
            // (CLAUDE.md §2/§5/§16). RaidSpire.VisualHeight is the value PlaceSpire fitted the host
            // to AND the value EnsureHittable sizes the hero's contact collider from — so this pin
            // asserts the art agrees with the collider, which is the thing that actually broke:
            // three of four scenes rendered the objective at 0.14 m inside a 14.40 m hit box.
            var spire = host.GetComponent<RaidSpire>();
            if (spire != null)
            {
                float want = spire.VisualHeight;
                // Same rule as the DefenseTower guard below: not a skip. `_visualHeight` is
                // [SerializeField, Min(1f)], so a value at or below 0.01 cannot be an authored choice
                // - it means Configure never ran on this spire and the bake did not finish it.
                if (!(want > 0.01f))
                {
                    failures.Add($"the RAID OBJECTIVE records a visual height of {want:0.###}m, which is below the " +
                               "[Min(1f)] floor on the field - RaidBaseGenerator.PlaceSpire never called " +
                               "Configure on it, so neither its render nor its hero-contact collider has an " +
                               "authored size");
                    return;
                }
                float spireOff = Mathf.Abs(cladBounds.size.y - want) / want;
                if (spireOff > CladHeightTolerance)
                    failures.Add($"the RAID OBJECTIVE renders {cladBounds.size.y:0.##}m but its bake recorded " +
                               $"{want:0.##}m (config '{spire.ConfigId}', art '{spire.CatalogId}') - {spireOff:P0} " +
                               $"off (> {CladHeightTolerance:P0}). RaidSpire.EnsureHittable sizes the hero's " +
                               "contact collider from the recorded number, so the player swings at a hit box " +
                               "the size of the spire that was INTENDED, not the one drawn");
                return;
            }

            if (!host.name.StartsWith("Watchtower_", StringComparison.Ordinal)) return;

            var tower = host.GetComponent<DefenseTower>();

            // ⛔ NOT A SKIP — A NAMED FAILURE. A `Watchtower_*` with no DefenseTower, or one whose
            // CatalogId is blank, is THE SUBJECT OF THIS PIN, not an object outside its scope: with no
            // armed id there is no authored height, so the clad's size is whatever two prefab scales
            // happened to multiply to — exactly the WO-1817/WO-1820 defect, arriving by a different
            // road. Returning green here would make the gate hollow precisely where it matters most
            // (caught by the three-way lint, [A-missing-dependency], 2026-09-17), and it would also
            // mean an UNARMED enemy turret shipped in a raid scene, which is its own defect.
            if (tower == null)
            {
                failures.Add("it is named Watchtower_* but carries NO DefenseTower, so it has no armed catalog " +
                           "id and therefore no authored height to render at - the clad's size is then only " +
                           "whatever the host and prefab scales multiplied to. RaidBaseGenerator.ArmTower " +
                           "stamps this component at bake time; its absence means this post never went " +
                           "through PlaceTowerProp/ArmTower");
                return;
            }
            if (string.IsNullOrEmpty(tower.CatalogId))
            {
                failures.Add("its DefenseTower carries an EMPTY CatalogId, so neither its authored height nor " +
                           "its combat stats can be resolved - ArmTower sets this from the TowerPlan and an " +
                           "empty value means the plan reached the scene without one");
                return;
            }

            var entry = FindCatalogEntry(tower.CatalogId);
            if (entry == null)
            {
                failures.Add($"its DefenseTower is armed with catalog id '{tower.CatalogId}', which has NO row in " +
                           CatalogRelPath + " - the clad height cannot be judged and the turret's own stats " +
                           "came from nowhere");
                return;
            }

            float target;
            try { target = StructureFactory.OptsFor(entry).FitHeight; }
            catch (Exception ex)
            {
                failures.Add($"StructureFactory.OptsFor('{tower.CatalogId}') threw {ex.GetType().Name}: {ex.Message}");
                return;
            }
            // Not a skip either: OptsFor ALWAYS sets FitHeight from YHeightVariable * heightMul with a
            // guarded multiplier, so a non-positive result means the row is malformed. A turret with no
            // resolvable fit height has no authored size for its clad - the ticket's defect.
            if (!(target > 0.01f))
            {
                failures.Add($"StructureFactory.OptsFor('{tower.CatalogId}') returned FitHeight {target:0.###} - " +
                           "that row resolves to no authored height at all, so nothing pins this clad's size");
                return;
            }

            float off = Mathf.Abs(cladBounds.size.y - target) / target;
            if (off <= CladHeightTolerance) return;

            failures.Add($"clad renders {cladBounds.size.y:0.##}m but '{tower.CatalogId}' authors a fit height of " +
                       $"{target:0.##}m - {off:P0} off (> {CladHeightTolerance:P0}). The HOST is what " +
                       "RaidBaseGenerator.PlaceTowerProp fits and WO-1807 strips its renderer, so the CLAD is the " +
                       "only thing on screen: RaidBaseDresser.FitCladToCadence must scale it to the same target");
        }

        /// <summary>
        /// One catalog row by id, read straight off the canonical JSON. Cached for the run - Judge is
        /// called once per post (59 of them across four scenes) and re-reading the file each time
        /// would dominate the suite.
        /// </summary>
        private static CatalogEntry FindCatalogEntry(string id)
        {
            if (_catalog == null)
            {
                _catalog = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string json = CanonicalJson.Read(CatalogRelPath);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var settings = new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore,
                            MissingMemberHandling = MissingMemberHandling.Ignore,
                        };
                        var file = JsonConvert.DeserializeObject<StructuresFile>(json, settings);
                        if (file != null && file.Entries != null)
                            for (int i = 0; i < file.Entries.Count; i++)
                            {
                                var e = file.Entries[i];
                                if (e != null && !string.IsNullOrEmpty(e.id)) _catalog[e.id] = e;
                            }
                    }
                }
                catch (Exception ex)
                {
                    // Never throw out of an oracle: an unreadable catalog leaves the map EMPTY, so
                    // every stamped id reds as "no row" above - loud, not silent.
                    Debug.LogWarning("[RaidPostOrientation] could not read " + CatalogRelPath + ": " + ex.Message);
                }
            }
            return string.IsNullOrEmpty(id) || !_catalog.TryGetValue(id, out var entry) ? null : entry;
        }

        private const string CatalogRelPath = "Data/Canonical/structures-catalog.json";
        private static Dictionary<string, CatalogEntry> _catalog;

        [Serializable]
        private sealed class StructuresFile
        {
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        private static bool Encapsulate(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (!any) { bounds = rends[i].bounds; any = true; }
                else bounds.Encapsulate(rends[i].bounds);
            }
            return any;
        }

        private static List<string> DiscoverScenes()
        {
            var found = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                string file = System.IO.Path.GetFileName(path);
                if (file.StartsWith("RaidBase_", StringComparison.Ordinal)) found.Add(path);
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        private static void RestoreScene(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (SceneManager.GetActiveScene().path == path) return;
            if (!System.IO.File.Exists(path)) return;
            try { EditorSceneManager.OpenScene(path, OpenSceneMode.Single); }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidPostOrientation] could not restore " + path + ": " + ex.Message);
            }
        }
    }
}
