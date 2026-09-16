// =============================================================================
// RepairAvailabilityProbe - INSTRUMENTATION ONLY (owner F8 seq=2153: "building is
// on fire but there is no option to repair").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHY THIS EXISTS (CLAUDE.md section 12 - instrument first, never inference-fix):
// the F8 capture for that report carried NO repair-path lines (a per-frame logger
// had flooded the harvest window), so there is no data proving WHY the repair
// option was missing. This probe makes the NEXT capture answer the question
// outright. It READS ONLY - it never repairs, never spawns UI, never mutates a
// structure - so it can ship enabled while the defect is being chased and be
// deleted (or FlowTrace-disabled) once the cause is proven.
//
// "ON FIRE" IS A VFX, NOT A STATE. The fire the owner saw is
// StructureDamageVisuals' Damage_Fire loop, which arms at hp <= the
// damage-states.json 'fire' threshold (0.25 by default) and covers SEVEN
// structure surfaces: wall / building / gate / collector / tower / defensetower
// / arcanetower / harvestsite. The repair SURFACES do not cover the same set,
// and that mismatch is the leading candidate this probe is built to prove or
// kill. So every line below reports the VFX-side state and the REPAIR-side
// availability for the SAME object, on one line, with no interpretation.
//
// THE FOUR QUESTIONS IT ANSWERS (one line per burning structure, on change):
//   1. WHICH structure and WHAT STATE - concrete component type, live hp
//      fraction, broken/destroyed flag. Separates "damaged" from "destroyed"
//      from "burning but healthy-ish".
//   2. OFFERED-BUT-NOT-SURFACED vs NEVER-OFFERED - `inRepairAllSet` says whether
//      the repair BACKEND is already pricing this structure. If it is in the set
//      and the player saw no button, the gap is UI. If it is absent from the set,
//      the gap is logic.
//   3. PLAYER-BUILT vs BAKED - `placed` reports whether a PlacedStructure parent
//      exists (the build-mode placement marker). A baked map structure carries
//      none. NOTE: the baked-registration lane is owned by another work order;
//      this probe only REPORTS the distinction, it never registers anything.
//   4. DESTROYED-IS-CORRECT - `broken=True` means "no repair option" is the
//      CORRECT behaviour (project rule: a destroyed item is rebuilt at full cost,
//      there is no repair discount) and the real defect is the fire VFX still
//      showing. The line says so explicitly so nobody "fixes" correct behaviour.
//
// It also reports the SCENE-LEVEL repair surfaces once per change, because a
// missing surface is invisible from the structure side:
//   * WallRepairController present? ENABLED? (a hub-installed one is
//     deliberately enabled=false, which disables tap-to-select entirely)
//   * HubRepairAffordance present? what is its button showing?
// If BOTH read absent while a structure burns, the player genuinely has no
// repair affordance anywhere and the trace proves it in one line.
//
// ⚠ SEVERITY IS SCENE-CLASS DEPENDENT (WO-1580, 2026-09-07). That both-absent
// line is a FAIL (error level, F8-capturing) everywhere EXCEPT an enemy raid
// base (HubScenes.IsRaid), where "no repair surface" is the authored state and
// the SAME information is reported at Step level. See EmitSurfaceLine.
//
// Instrumented [Flow:RepairProbe]. Null-safe + Guard-wrapped throughout; a throw
// in a diagnostic must never break a play session.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Buildings.Progression;

namespace DeNelle.Village
{
    /// <summary>
    /// Read-only diagnostic that pairs each BURNING structure's actual state with
    /// the repair options actually available to the player, so an F8 capture
    /// pinpoints whether "no option to repair" is a UI gap, a logic gap, or
    /// correct destroyed-item behaviour. Never mutates anything.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RepairAvailabilityProbe : MonoBehaviour
    {
        /// <summary>Seconds between polls. Slow on purpose - this is a diagnostic, not a system.</summary>
        private const float PollInterval = 2.5f;

        /// <summary>
        /// Safety margin above the data 'fire' threshold. A structure just above the
        /// line is reported too, so a capture shows the approach to the fire state
        /// rather than only its arrival (the owner reports the moment she SEES fire,
        /// which may be a frame either side of the threshold).
        /// </summary>
        private const float ReportMargin = 0.05f;

        private float _timer;

        // Last-reported line per structure, so the log carries TRANSITIONS and not a
        // poll-rate firehose. The flooded seq=2153 harvest is exactly what this avoids.
        private readonly Dictionary<Object, string> _lastLine = new Dictionary<Object, string>();
        private readonly Dictionary<Object, string> _lastInvisibleLine = new Dictionary<Object, string>();
        private string _lastSurfaceLine;

        // ── Self-install (mirrors StructureDamageVisuals / HubRepairAffordance) ──

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawn();
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode) => TrySpawn();

        /// <summary>
        /// Installs UNCONDITIONALLY, unlike HubRepairAffordance which gates on
        /// SceneHasRepairables(). That is deliberate: whether that gate is what
        /// suppressed the repair option is one of the things being measured, so the
        /// probe must not inherit the gate it is measuring.
        /// </summary>
        private static void TrySpawn()
        {
            if (FindAnyObjectByType<RepairAvailabilityProbe>() != null) return;
            var go = new GameObject("RepairAvailabilityProbe");
            go.AddComponent<RepairAvailabilityProbe>();
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = PollInterval;
            Guard.Try("RepairProbe", "poll burning structures", Poll);
        }

        // ── Poll ────────────────────────────────────────────────────────────────

        private void Poll()
        {
            var burning = new List<Row>();
            CollectBurning(burning);

            // WO-1296 RECURRENCE (owner felt-test 2026-09-03, verbatim: "And yellow item not
            // damaged is still showing up"). The pass below answers the OPPOSITE question to the
            // burning pass, and until now NOTHING answered it - which is why that report arrived
            // with no data behind it. See ReportInvisiblyDamaged for the full reasoning.
            var invisible = new List<InvisibleRow>();
            CollectInvisiblyDamaged(invisible);

            if (burning.Count == 0 && invisible.Count == 0)
            {
                // Nothing is on fire and nothing is invisibly damaged - stay silent so the log is
                // not padded, but drop the transition memory so the NEXT event re-reports in full.
                if (_lastLine.Count > 0) _lastLine.Clear();
                if (_lastInvisibleLine.Count > 0) _lastInvisibleLine.Clear();
                _lastSurfaceLine = null;
                return;
            }

            ReportSurfaces();
            ReportInvisiblyDamaged(invisible);

            if (burning.Count == 0) return;

            var repair = FindAnyObjectByType<WallRepairController>();
            string setLine = repair != null
                ? Guard.Try("RepairProbe", "describe repair-all set", () => repair.DescribeRepairAllSet(), "<threw>")
                : "<no WallRepairController - the repair backend is not present in this scene>";

            foreach (var row in burning)
            {
                bool inSet = repair != null && setLine != null &&
                             setLine.IndexOf(row.Name, System.StringComparison.OrdinalIgnoreCase) >= 0;

                string line =
                    $"BURNING '{row.Name}' type={row.TypeName} hp={row.Hp:0.00} broken={row.Broken} " +
                    $"placed={row.Placed} tapRepairable={row.TapRepairable} inRepairAllSet={inSet}";

                if (_lastLine.TryGetValue(row.Key, out string prev) && prev == line) continue;
                _lastLine[row.Key] = line;

                if (row.Broken)
                {
                    // Question 4, answered before anyone can mis-read the line: a destroyed
                    // structure having no repair option is the DESIGNED behaviour. If this
                    // line is what the capture shows, the defect is the FIRE VFX on a ruin,
                    // not the missing repair option - do not "fix" the repair gate.
                    FlowTrace.Step("RepairProbe",
                        $"{line} -> DESTROYED. No repair option is CORRECT here (destroyed items " +
                        "rebuild at full cost, no repair discount). If a FIRE loop is showing on " +
                        "this shell, the defect is the VFX state, not the repair gate.");
                }
                else if (inSet && !row.TapRepairable)
                {
                    FlowTrace.Warn("RepairProbe",
                        $"{line} -> the backend PRICES this structure (it is in the Repair-All set) " +
                        "but RepairTarget cannot wrap it, so tap-to-select can never reach it. " +
                        "Repair-All is the only surface that can fix this one.");
                }
                else if (!inSet)
                {
                    FlowTrace.Warn("RepairProbe",
                        $"{line} -> NEVER OFFERED: the structure is damaged and burning but the " +
                        "Repair-All set does not contain it. This is a LOGIC gap, not a UI gap.");
                }
                else
                {
                    FlowTrace.Step("RepairProbe",
                        $"{line} -> repair IS offered by the backend. If the player saw no option, " +
                        "the gap is in the SURFACES line above (button hidden / affordance absent).");
                }
            }
        }

        /// <summary>
        /// Report the scene-level repair surfaces on change. A missing surface cannot be
        /// seen from any single structure, and "no option to repair" is most often a
        /// surface that never installed rather than a structure that refused.
        /// </summary>
        private void ReportSurfaces()
        {
            var repair = FindAnyObjectByType<WallRepairController>();
            var hub = FindAnyObjectByType<HubRepairAffordance>();
            var wave = FindAnyObjectByType<WaveManager>();
            string sceneName = SceneManager.GetActiveScene().name;

            string line =
                $"SURFACES scene='{sceneName}' " +
                $"WallRepairController={(repair == null ? "ABSENT" : (repair.enabled ? "present+ENABLED" : "present+DISABLED(no tap-to-select)"))} " +
                $"HubRepairAffordance={(hub == null ? "ABSENT" : "present:" + hub.DiagnosticState)} " +
                $"WaveManager={(wave == null ? "none(pure hub)" : wave.Phase.ToString())}";

            if (line == _lastSurfaceLine) return;
            _lastSurfaceLine = line;

            EmitSurfaceLine(sceneName, repair == null && hub == null, line);
        }

        /// <summary>
        /// SEVERITY DECISION for the surfaces line, split out of <see cref="ReportSurfaces"/>
        /// so it is decidable from a scene NAME alone and can be driven headless
        /// (RepairProbeSeverityRegression). Nothing about the line's content changed.
        ///
        /// <para>WO-1580 (owner Seeker session 2026-09-07, F8 seq 4698, scene
        /// <c>RaidBase_fortified_garrison</c>): the both-absent branch used to call
        /// <c>FlowTrace.Fail</c> unconditionally, which publishes at ERROR level, which is
        /// exactly what the F8 harness captures — so every raid with a burning structure
        /// minted an error capture. Inside an enemy RAID BASE the player is there to DESTROY
        /// structures; no repair surface is the AUTHORED state, not a defect. Anywhere else
        /// the absence still IS a defect and this probe exists to shout about it, so the
        /// <c>Fail</c> text is unchanged for every other scene class.</para>
        ///
        /// <para>⛔ SCOPED TO <see cref="DeNelle.Core.HubScenes.IsRaid"/> ONLY, deliberately.
        /// <c>Garrison_*</c> / <c>Outpost*</c> are NOT IsRaid, and HubScenes.cs:143-152
        /// records that whether they are committed assaults is an owner question nobody has
        /// asked. The capture proves exactly one scene class; widening this to
        /// <c>IsEnemyOutpost</c> or <c>IsDungeon</c> would silence a defect nothing has
        /// ruled on. Read that comment before touching this line.</para>
        /// </summary>
        public static void EmitSurfaceLine(string sceneName, bool noSurfaceAtAll, string line)
        {
            if (!noSurfaceAtAll)
            {
                FlowTrace.Step("RepairProbe", line);
                return;
            }

            if (DeNelle.Core.HubScenes.IsRaid(sceneName))
            {
                FlowTrace.Step("RepairProbe",
                    $"{line} -> NO repair surface exists in this scene at all while a structure burns. " +
                    "expected: no repair surface is authored for a raid base — the player is here to " +
                    "destroy enemy structures, not to repair them. Reported at Step level (WO-1580) so " +
                    "a designed absence does not mint an error capture.");
                return;
            }

            FlowTrace.Fail("RepairProbe",
                $"{line} -> NO repair surface exists in this scene at all while a structure burns. " +
                "The player has no way to repair anything here.");
        }

        // ── Collection ──────────────────────────────────────────────────────────

        /// <summary>One burning structure's read-only snapshot.</summary>
        private struct Row
        {
            public Object Key;
            public string Name;
            public string TypeName;
            public float Hp;
            public bool Broken;
            public bool Placed;
            public bool TapRepairable;
        }

        /// <summary>
        /// Collect every structure at or near the data-driven 'fire' threshold, across
        /// the SAME surfaces StructureDamageVisuals scans. Deliberately mirrors that
        /// scan rather than sharing code with it: if the two ever diverge, the probe
        /// reporting a structure the visuals ignore (or vice versa) is itself a finding.
        /// </summary>
        private void CollectBurning(List<Row> into)
        {
            AddIfBurning<WallSegment>(into, "wall", c => HpOf(c), c => BrokenOf(c));
            AddIfBurning<Building>(into, "building",
                c => c is Building b ? b.HpFraction : 1f,
                c => c is Building b && b.IsDestroyed);
            AddIfBurning<Gate>(into, "gate", c => HpOf(c), c => BrokenOf(c));
            AddIfBurning<Tower>(into, "tower",
                c => c is Tower t ? t.HpFraction : 1f,
                c => c is Tower t && t.IsBroken);
            AddIfBurning<DefenseTower>(into, "defensetower",
                c => c is DefenseTower t ? t.HpFraction : 1f,
                c => c is DefenseTower t && t.IsBroken);
            AddIfBurning<ArcaneTower>(into, "arcanetower",
                c => c is ArcaneTower t ? t.HpFraction : 1f,
                c => c is ArcaneTower t && t.IsBroken);
            AddIfBurning<DeNelle.Village.World.HarvestSite>(into, "harvestsite",
                c => c is DeNelle.Village.World.HarvestSite h ? h.HpFraction : 1f,
                c => c is DeNelle.Village.World.HarvestSite h && h.IsBroken);

            // Collectors come from the registry (no scene scan), matching StructureDamageVisuals.
            float collectorLine = DamageStatesCatalog.Fire("collector") + ReportMargin;
            foreach (var c in ResourceCollectorRegistry.All)
            {
                if (c == null) continue;
                float hp = c.HpFraction;
                if (!c.IsBroken && hp > collectorLine) continue;
                into.Add(BuildRow(c, "ResourceCollector", c.BuildingId, hp, c.IsBroken));
            }
        }

        private void AddIfBurning<T>(List<Row> into, string typeKey,
            System.Func<Component, float> hp, System.Func<Component, bool> broken) where T : Component
        {
            float line = DamageStatesCatalog.Fire(typeKey) + ReportMargin;
            foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (c == null) continue;
                bool isBroken = broken(c);
                float f = hp(c);
                if (!isBroken && f > line) continue;
                into.Add(BuildRow(c, typeof(T).Name, c.gameObject.name, f, isBroken));
            }
        }

        // =====================================================================
        //  INVISIBLY-DAMAGED pass - the INVERSE of the burning pass
        // ---------------------------------------------------------------------
        //  OWNER, felt-test 2026-09-03, verbatim: "And yellow item not damaged is
        //  still showing up". "STILL" points at WO-1296 (commit a8811ec7), which
        //  silenced the "That structure is undamaged" toast on an intact-structure
        //  tap. That fix cannot cover this report, because the two are DIFFERENT
        //  predicates:
        //
        //    WO-1296 covered  DamageFraction == 0        (truly pristine, tap ignored)
        //    THIS covers      0 < DamageFraction < 0.5   (damaged to the CODE,
        //                                                 pristine to the PLAYER)
        //
        //  The mismatch is between two thresholds that were never reconciled:
        //    * RepairTarget.NeedsRepair  => DamageFraction > 0.0001f
        //      (RepairTarget.cs) - so 99.99% HP already counts as repairable, is
        //      collected into the Repair-All set, is priced, and offers a prompt.
        //    * The first VISIBLE damage tell is the SMOLDER loop, which arms at
        //      HP <= damage-states.json 'smolder' (default 0.5), with fire at 0.25
        //      (StructureDamageVisuals.DamageStatesCatalog.DefaultsDef).
        //
        //  Everything between those two lines - HP 50%..99.99% - looks PRISTINE and
        //  is nonetheless offered, priced and charged for. That is exactly the
        //  player-side sentence "not damaged ... is still showing up".
        //
        //  ⚠ THIS PROBE DOES NOT DECIDE WHICH THRESHOLD IS WRONG. Suppressing the
        //  affordance above smolder would remove a real feature (a 60%-HP structure
        //  could no longer be repaired at all); showing a tell from the first point
        //  of damage is an art-lane change. Both are owner rulings. The probe's job
        //  is to make the NEXT occurrence name itself instead of arriving as prose.
        //
        //  ⛔ The burning pass could NEVER have caught this: Poll() used to return
        //  early whenever nothing was on fire, and an invisibly-damaged structure is
        //  BY DEFINITION not on fire. So the previous capture carried no repair lines
        //  at all - not because the path was quiet, but because the probe was.
        // =====================================================================

        /// <summary>One invisibly-damaged structure's read-only snapshot.</summary>
        private struct InvisibleRow
        {
            public Object Key;
            public string Name;
            public string TypeName;
            public string TypeKey;
            public float Hp;             // 0..1 HP fraction
            public float VisibleAt;      // HP fraction at/below which the player first SEES damage
            public bool OptOut;          // this type carries a bespoke tell instead of the shared one
            public bool TapRepairable;
            public bool Placed;
            public Component Component;
        }

        /// <summary>
        /// Collects every structure that the repair predicate calls DAMAGED while the
        /// damage-tell catalog says the player can see NOTHING - i.e.
        /// <c>smolder &lt; hp &lt; 1.0</c>. Mirrors <see cref="CollectBurning"/>'s surface
        /// list exactly so the two passes cover the same objects from opposite ends.
        /// Read-only: it never repairs, never spawns UI, never mutates a structure.
        /// </summary>
        private void CollectInvisiblyDamaged(List<InvisibleRow> into)
        {
            AddIfInvisiblyDamaged<WallSegment>(into, "wall", c => HpOf(c), c => BrokenOf(c));
            AddIfInvisiblyDamaged<Building>(into, "building",
                c => c is Building b ? b.HpFraction : 1f,
                c => c is Building b && b.IsDestroyed);
            AddIfInvisiblyDamaged<Gate>(into, "gate", c => HpOf(c), c => BrokenOf(c));
            AddIfInvisiblyDamaged<Tower>(into, "tower",
                c => c is Tower t ? t.HpFraction : 1f,
                c => c is Tower t && t.IsBroken);
            AddIfInvisiblyDamaged<DefenseTower>(into, "defensetower",
                c => c is DefenseTower t ? t.HpFraction : 1f,
                c => c is DefenseTower t && t.IsBroken);
            AddIfInvisiblyDamaged<ArcaneTower>(into, "arcanetower",
                c => c is ArcaneTower t ? t.HpFraction : 1f,
                c => c is ArcaneTower t && t.IsBroken);
            AddIfInvisiblyDamaged<DeNelle.Village.World.HarvestSite>(into, "harvestsite",
                c => c is DeNelle.Village.World.HarvestSite h ? h.HpFraction : 1f,
                c => c is DeNelle.Village.World.HarvestSite h && h.IsBroken);

            float collectorVisible = DamageStatesCatalog.Smolder("collector");
            bool collectorOptOut = DamageStatesCatalog.OptOut("collector");
            foreach (var c in ResourceCollectorRegistry.All)
            {
                if (c == null || c.IsBroken) continue;
                float hp = c.HpFraction;
                if (!IsInvisiblyDamaged(hp, collectorVisible)) continue;
                into.Add(BuildInvisibleRow(c, "ResourceCollector", c.BuildingId, hp,
                                           collectorVisible, "collector", collectorOptOut));
            }
        }

        private void AddIfInvisiblyDamaged<T>(List<InvisibleRow> into, string typeKey,
            System.Func<Component, float> hp, System.Func<Component, bool> broken) where T : Component
        {
            float visibleAt = DamageStatesCatalog.Smolder(typeKey);
            bool optOut = DamageStatesCatalog.OptOut(typeKey);
            foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (c == null || broken(c)) continue;
                float f = hp(c);
                if (!IsInvisiblyDamaged(f, visibleAt)) continue;
                into.Add(BuildInvisibleRow(c, typeof(T).Name, c.gameObject.name, f,
                                           visibleAt, typeKey, optOut));
            }
        }

        /// <summary>
        /// The whole finding in one expression: the repair predicate's own lower bound
        /// (<c>DamageFraction &gt; 0.0001</c>, i.e. <c>hp &lt; 0.9999</c>) is tripped while
        /// the structure is still ABOVE the HP at which any damage tell arms.
        /// </summary>
        private static bool IsInvisiblyDamaged(float hp, float visibleAt)
            => hp < 0.9999f && hp > visibleAt;

        private static InvisibleRow BuildInvisibleRow(Component c, string typeName, string name,
            float hp, float visibleAt, string typeKey, bool optOut)
        {
            return new InvisibleRow
            {
                Key = c,
                Name = string.IsNullOrEmpty(name) ? typeName : name,
                TypeName = typeName,
                TypeKey = typeKey,
                Hp = hp,
                VisibleAt = visibleAt,
                OptOut = optOut,
                TapRepairable = RepairTarget.TryWrap(c) != null,
                Placed = c.GetComponentInParent<PlacedStructure>() != null,
                Component = c,
            };
        }

        /// <summary>
        /// One line per invisibly-damaged structure, on change. Every number the next
        /// triage needs is ON the line: which structure, its live HP fraction, the damage
        /// fraction the repair predicate reads, the HP at which the player would first SEE
        /// damage, whether the repair backend is pricing it, and WHAT it is charging.
        /// </summary>
        private void ReportInvisiblyDamaged(List<InvisibleRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                if (_lastInvisibleLine.Count > 0) _lastInvisibleLine.Clear();
                return;
            }

            var repair = FindAnyObjectByType<WallRepairController>();
            string setLine = repair != null
                ? Guard.Try("RepairProbe", "describe repair-all set",
                            () => repair.DescribeRepairAllSet(), "<threw>")
                : "<no WallRepairController>";

            foreach (var row in rows)
            {
                bool inSet = repair != null && setLine != null && !string.IsNullOrEmpty(row.Name) &&
                             setLine.IndexOf(row.Name, System.StringComparison.OrdinalIgnoreCase) >= 0;

                float damageFraction = 1f - row.Hp;
                string price = "<unpriced - no WallRepairController>";
                if (repair != null)
                    price = Guard.Try("RepairProbe", "price invisibly-damaged structure",
                        () => DescribeCost(repair.CostForStructure(row.Component, damageFraction)),
                        "<threw>");

                string line =
                    $"INVISIBLY-DAMAGED '{row.Name}' type={row.TypeName} key={row.TypeKey} " +
                    $"hp={row.Hp:0.000} damageFraction={damageFraction:0.000} " +
                    $"needsRepairAbove=0.0001 firstVisibleTellAtHp={row.VisibleAt:0.00} " +
                    $"optOutOfSharedTell={row.OptOut} placed={row.Placed} " +
                    $"tapRepairable={row.TapRepairable} inRepairAllSet={inSet} price={price}";

                if (_lastInvisibleLine.TryGetValue(row.Key, out string prev) && prev == line) continue;
                _lastInvisibleLine[row.Key] = line;

                if (!inSet)
                {
                    // Damaged, invisible, and NOT offered. Harmless to the player, but it means
                    // the two predicates disagree in the other direction too - worth the line.
                    FlowTrace.Step("RepairProbe",
                        $"{line} -> damaged below the visible-tell line and NOT in the Repair-All " +
                        "set, so no affordance can be showing for this one.");
                    continue;
                }

                FlowTrace.Warn("RepairProbe",
                    $"{line} -> THIS IS THE 'not damaged but the repair prompt shows' SHAPE. " +
                    "The structure is above its first visible damage tell " +
                    $"(hp {row.Hp:0.000} > {row.VisibleAt:0.00}), so the player sees a PRISTINE " +
                    "structure, while RepairTarget.NeedsRepair (DamageFraction > 0.0001) has it in " +
                    "the Repair-All set and is charging " + price + " for it. Two thresholds that " +
                    "were never reconciled - NOT a highlight/marker bug, and NOT the WO-1296 " +
                    "intact-tap toast (that path is a different predicate and is already silent). " +
                    "Which threshold moves is an OWNER RULING: suppress the affordance above the " +
                    "tell, or show a tell from the first point of damage.");
            }
        }

        /// <summary>
        /// Compact materials rendering for a probe line. Uses the SAME struct the repair
        /// backend prices in (<c>DeNelle.Core.Catalog.ResourceCost</c>, whose slots are the
        /// lower-case JSON field names) so the number on the line is the number charged.
        /// </summary>
        private static string DescribeCost(DeNelle.Core.Catalog.ResourceCost cost)
            => $"wood={cost.wood} iron={cost.iron} food={cost.stone} crystals={cost.crystals}";

        private static Row BuildRow(Component c, string typeName, string name, float hp, bool broken)
        {
            // PlacedStructure is the build-mode placement marker: present => the player
            // built it, absent => it is a baked map structure. Reported, never acted on.
            bool placed = c.GetComponentInParent<PlacedStructure>() != null;
            // Tap-to-select only reaches what RepairTarget can wrap (wall / gate /
            // building). Towers, harvest sites and collectors wrap to null, so a tap on
            // one is silently ignored no matter how damaged it is.
            bool tappable = RepairTarget.TryWrap(c) != null;
            return new Row
            {
                Key = c,
                Name = string.IsNullOrEmpty(name) ? typeName : name,
                TypeName = typeName,
                Hp = hp,
                Broken = broken,
                Placed = placed,
                TapRepairable = tappable,
            };
        }

        /// <summary>HP fraction via the uniform RepairTarget view (walls / gates / buildings).</summary>
        private static float HpOf(Component c)
        {
            var t = RepairTarget.TryWrap(c);
            return t != null && t.IsValid ? 1f - t.DamageFraction : 1f;
        }

        /// <summary>Destroyed test via the uniform RepairTarget view.</summary>
        private static bool BrokenOf(Component c)
        {
            var t = RepairTarget.TryWrap(c);
            return t != null && t.IsValid && t.DamageFraction >= 0.999f;
        }
    }
}
