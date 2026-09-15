// =============================================================================
// StructureBurnRegression [structure-burn] - proves WO-761 fire lingers till repaired.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Core).
//
// Drives the real DeNelle.Village.StructureBurn component (composed on a throwaway
// GameObject) through its production seam, with a stub IDamageableStructure standing
// in for a tower/wall, and PROVES the three load-bearing behaviours:
//   (1) IGNITE + TICK  - a burning structure loses HP over time via ApplyContactDamage.
//   (2) REPAIR = EXTINGUISH - restoring HP (fraction jumps back above 50%) stops the
//       burn on the next tick (self-detected; no repair-path hook needed).
//   (3) DESTROY - burn damage can bring the structure to 0; the burn then ends.
// Also asserts STACK = REFRESH (a re-ignite never double-composes / double-burns).
//
// WO-1352 APPENDS THE SCUFF ORACLE (see ScuffOracle below): no HP band between 0 and
// 100% may be visually silent while it is repair-eligible. Same subject - what a damaged
// structure SHOWS - so it lives in this suite rather than a new registration.
//
// ITS FOLLOW-UP APPENDS THE STRENGTH ORACLE (ScuffStrengthOracle, section E): the first
// rung must be loud enough to NOTICE and the last quiet enough not to pre-empt the
// smolder. Sections A-D shipped green while the tell was still invisible on the owner's
// device, because "the band has a rung here" and "a player can see that rung" are two
// different claims and only the first was pinned.
//
// VFXManager.Instance is null in edit mode, so StartFireVfx is a proven no-op here -
// this suite validates the DAMAGE + STATE machine; the fire VFX is null-safe.
//
// Marker: STRUCTURE_BURN_OK / STRUCTURE_BURN_FAIL. Expected: GREEN.
//
// Wire (DataRegression.RunAll):
//   Guard.Try(... () => { if (!StructureBurnRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[structure-burn] " + r); });
// =============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class StructureBurnRegression
    {
        // A minimal burnable stand-in: HP on a 0..max scale, the same two verbs the
        // real structures expose to StructureBurn (IsAlive + ApplyContactDamage).
        private sealed class StubStructure : IDamageableStructure
        {
            public float Hp;
            public float Max;
            public bool IsAlive => Hp > 0f;
            // WO-1439 — burn is a structure's OWN fire ticking itself down, not an actor
            // attacking it, so the faction here is inert; Friendly matches every real
            // burnable in the player's town and keeps this suite's outcomes unchanged.
            public CombatFaction Faction => CombatFaction.Friendly;
            public void ApplyContactDamage(float amount) => Hp = Mathf.Max(0f, Hp - amount);
            public float Fraction => Max > 0f ? Mathf.Clamp01(Hp / Max) : 0f;
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- STRUCTURE BURN (WO-761: fire lingers on <=50% structures till repaired/destroyed) ---");

            GameObject host = null;
            try
            {
                host = new GameObject("StructureBurnTestHost");
                var burn = host.AddComponent<StructureBurn>();

                // (1) IGNITE + TICK: a structure sitting at exactly 50% catches fire and drains.
                var stub = new StubStructure { Hp = 50f, Max = 100f };
                burn.Configure(stub, () => stub.Fraction, stub.Max);
                burn.Ignite();
                if (!burn.IsBurning) failures.Add("Ignite did not set IsBurning at 50% HP");

                float before = stub.Hp;
                for (int i = 0; i < 5; i++) burn.TickForTest(0.5f);   // 5 ticks * 0.5s
                log.AppendLine($"  after 5 ticks: HP {before:0.0} -> {stub.Hp:0.0} (burning={burn.IsBurning})");
                if (stub.Hp >= before) failures.Add($"burn ticks did not lower HP ({before:0.0} -> {stub.Hp:0.0})");
                if (!burn.IsBurning) failures.Add("burn extinguished on its own while still damaged (must NOT self-expire)");

                // STACK = REFRESH: a second ignite must not add a second StructureBurn.
                burn.Ignite();
                int comps = host.GetComponents<StructureBurn>().Length;
                if (comps != 1) failures.Add($"re-ignite stacked components ({comps} StructureBurn on host, expected 1)");

                // (2) REPAIR = EXTINGUISH: HP fraction jumps back above 50% -> burn stops.
                stub.Hp = stub.Max;                 // a full repair
                burn.TickForTest(0.5f);
                if (burn.IsBurning) failures.Add("repair did not extinguish the burn (still burning after HP restored)");
                float afterRepair = stub.Hp;
                burn.TickForTest(0.5f);
                if (stub.Hp < afterRepair) failures.Add("burn kept ticking AFTER extinguish (repaired structure still taking burn damage)");

                // (3) DESTROY: re-ignite low, burn all the way to 0, burn ends (no infinite loop).
                stub.Hp = 6f;
                burn.Ignite();
                if (!burn.IsBurning) failures.Add("re-ignite at 6% HP did not start a fresh burn");
                for (int i = 0; i < 40 && stub.Hp > 0f; i++) burn.TickForTest(0.5f);
                log.AppendLine($"  burn-to-death: HP now {stub.Hp:0.0} (burning={burn.IsBurning}, alive={stub.IsAlive})");
                if (stub.Hp > 0f) failures.Add("burn never destroyed the structure (HP stuck above 0)");
                if (burn.IsBurning) failures.Add("burn still active after the structure was destroyed (leaked DoT)");
            }
            catch (System.Exception ex)
            {
                failures.Add($"StructureBurn drive threw: {ex.Message}");
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
            }

            // (4) WO-1352 - THE NO-SILENT-BAND ORACLE. Appended to this suite because it
            //     pins the same subject: what a damaged structure SHOWS.
            ScuffOracle(failures, log);

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        // =====================================================================
        // WO-1352 SCUFF ORACLE - "no HP band between 0 and 100% is visually
        // silent while being repair-eligible."
        // ---------------------------------------------------------------------
        // THE DEFECT IT PINS, stated as the two numbers that disagreed:
        //   RepairTarget.NeedsRepair          => DamageFraction > 0.0001
        //   the first VISIBLE tell (smolder)  => hp <= 0.5
        // So 50%..99.99% HP was PRISTINE to the player and DAMAGED to the code, and
        // Repair-All BILLED for it - the owner's device toast read "Repaired 1 structures
        // for Wood 35, Iron 7" against a building with no visible damage at all.
        //
        // ⚠ IT BINDS TO THE SHIPPING CODE, NOT TO A COPY OF THE NUMBERS. The eligibility
        // side is asked of a REAL RepairTarget wrapping a REAL WallSegment that has been
        // really damaged; the visibility side is asked of StructureDamageVisuals'
        // TellOrdinalFor, which is the same function Evaluate itself calls. An oracle that
        // re-declares 0.0001 and 0.5 locally would only ever prove its own duplicates are
        // intact - which is the exact failure mode CLAUDE.md section 5 and section 2 are
        // written against.
        //
        // RED-FIRST MUTATION (the proof this oracle has teeth): set "scuffOnset" in
        // Assets/{Resources,StreamingAssets}/Data/Canonical/damage-states.json to 0.5 -
        // i.e. exactly the pre-WO-1352 world, where the first tell IS the smolder. Every
        // sample in the 0.5..0.9999 sweep then reports ordinal 0 while NeedsRepair is true
        // and this oracle fails with "SILENT while repair-eligible" on the first one.
        // Restoring 0.9999 turns it green. The gap is the thing under test, not the file.
        // =====================================================================
        private static void ScuffOracle(List<string> failures, StringBuilder log)
        {
            log.AppendLine("--- WO-1352 SCUFF ORACLE (no repair-eligible HP band may be visually silent) ---");

            DamageStatesCatalog.Invalidate();   // read the authored thresholds fresh
            const string TypeKey = "wall";
            float smolder = DamageStatesCatalog.Smolder(TypeKey);
            float onset   = DamageStatesCatalog.ScuffOnset(TypeKey);
            int   steps   = DamageStatesCatalog.ScuffSteps;
            log.AppendLine($"  thresholds: scuffOnset {onset}, smolder {smolder}, scuffSteps {steps}");

            // -- A. COVERAGE STARTS AT OR BELOW THE REPAIR PREDICATE ------------
            // Asked of the REAL predicate: damage a real wall by the smallest amount that
            // still makes it repair-eligible, then ask the real ladder what it shows.
            GameObject wallHost = null;
            try
            {
                wallHost = new GameObject("ScuffOracleWall");
                var seg = wallHost.AddComponent<WallSegment>();
                var target = RepairTarget.TryWrap(seg);
                if (target == null || !target.IsValid)
                {
                    failures.Add("scuff oracle: RepairTarget.TryWrap refused a real WallSegment - the " +
                                 "oracle cannot bind to the live repair predicate");
                }
                else
                {
                    if (target.NeedsRepair)
                        failures.Add($"scuff oracle: an UNDAMAGED wall already reports NeedsRepair " +
                                     $"(damageFraction {target.DamageFraction:0.######}) - the predicate moved");

                    // A hair of damage: on the shared 0..100 wall track this is ~0.05 points, an
                    // amount no player would call "damaged" - and precisely the amount that
                    // used to be billable and invisible at the same time.
                    //
                    // ⛔ WO-1737 - THE RAW AMOUNT IS SCALED SO THE *LANDED* AMOUNT STAYS 0.05.
                    // ApplyContactDamage divides by the tier toughness, whose floor moved from
                    // x1 to WallSegment.BaseToughness on the owner's 2026-09-15 durability
                    // ruling. A bare 0.05f now lands a quarter of that, which still clears
                    // NeedsRepair (> 0.0001 fraction) only by a 25% margin - and would fall
                    // straight through it if she re-tunes the floor again (WO-1738 Branch A
                    // prices floors up to 8x higher). Deriving the raw amount keeps this oracle
                    // testing the boundary its own failure message names, at any floor.
                    seg.ApplyContactDamage(0.05f * WallSegment.ToughnessFor(seg.Tier));
                    float hp = 1f - target.DamageFraction;
                    int ord = StructureDamageVisuals.TellOrdinalFor(hp, false, TypeKey);
                    log.AppendLine($"  first-blood: damageFraction {target.DamageFraction:0.######} " +
                                   $"hp {hp:0.######} needsRepair={target.NeedsRepair} tellOrdinal={ord}");

                    if (!target.NeedsRepair)
                        failures.Add($"scuff oracle: a 0.05-point hit did not make the wall repair-eligible " +
                                     $"(damageFraction {target.DamageFraction:0.######}) - re-pick the probe amount, " +
                                     "the oracle is no longer testing the boundary it claims to");
                    else if (ord <= 0)
                        failures.Add($"scuff oracle: the FIRST point of damage is SILENT while repair-eligible " +
                                     $"(hp {hp:0.######}, needsRepair=True, tellOrdinal 0). This is WO-1352's " +
                                     "defect exactly: Repair-All would charge for a structure the player sees " +
                                     "nothing wrong with.");
                }
            }
            catch (System.Exception ex)
            {
                // Loud, never silent - but not a false red on the whole suite. The pure-band
                // assertions below still run and still carry the invariant; what is lost is
                // only the binding to the live predicate, and the log says so in those words.
                log.AppendLine("  WARN: could not drive a real WallSegment in edit mode (" + ex.Message +
                               ") - the live-predicate binding was SKIPPED; the band sweep below still ran.");
            }
            finally
            {
                if (wallHost != null) Object.DestroyImmediate(wallHost);
            }

            // -- B. THE SWEEP: no silent sample anywhere in the eligible band ----
            // 0.0001 is the repair predicate's epsilon, so hp = 1 - 0.0001 is the HIGHEST
            // HP at which a structure is still billable. Every sample from there down to
            // the broken shell must show SOMETHING.
            const float PredicateEpsilon = 0.0001f;
            float top = 1f - PredicateEpsilon - 1e-6f;   // just inside eligible
            int silent = 0, samples = 0;
            float firstSilentHp = -1f;
            // The sweep walks HP DOWNWARD, so the ordinal must never DECREASE. Seeded at the
            // lowest possible ordinal for that reason - seeding it high (and testing the
            // other direction) silently passes everything, which is what the first draft of
            // this oracle did until the arithmetic was actually run.
            int prevOrd = 0;
            bool monotonic = true;
            float regressAtHp = -1f;

            for (int i = 0; i <= 200; i++)
            {
                float hp = Mathf.Lerp(top, 0f, i / 200f);
                int ord = StructureDamageVisuals.TellOrdinalFor(hp, false, TypeKey);
                samples++;
                if (ord <= 0) { silent++; if (firstSilentHp < 0f) firstSilentHp = hp; }
                if (ord < prevOrd) { monotonic = false; if (regressAtHp < 0f) regressAtHp = hp; }
                prevOrd = ord;
            }
            log.AppendLine($"  sweep: {samples} samples over hp [{top:0.####} .. 0], silent={silent}, " +
                           $"monotonic={monotonic}");

            if (silent > 0)
                failures.Add($"scuff oracle: {silent}/{samples} repair-eligible HP samples are VISUALLY SILENT " +
                             $"(first at hp {firstSilentHp:0.####}). A band that is billable and invisible is " +
                             "the WO-1352 defect; the tell's coverage must start at or below the repair " +
                             "predicate's threshold and stay continuous to the smolder handoff.");
            if (!monotonic)
                failures.Add($"scuff oracle: the tell ladder goes BACKWARDS as HP falls (first regression at " +
                             $"hp {regressAtHp:0.####}) - a structure would visibly clean itself up as it got " +
                             "closer to being destroyed");

            // -- C. THE HANDOFF IS CONTINUOUS, NOT A POP -------------------------
            // Arriving AT the smolder from a fully-scuffed surface is the escalation the
            // owner ruled for. If the last scuff step were not reached before the smolder
            // arms, smoke would still pop onto a pristine-looking building.
            float justAbove = Mathf.Min(onset, smolder + 0.001f);
            int ordJustAbove = StructureDamageVisuals.TellOrdinalFor(justAbove, false, TypeKey);
            int ordAtSmolder = StructureDamageVisuals.TellOrdinalFor(smolder, false, TypeKey);
            log.AppendLine($"  handoff: hp {justAbove:0.###} -> ordinal {ordJustAbove}; " +
                           $"hp {smolder:0.###} -> ordinal {ordAtSmolder} (smolder rung = {steps + 1})");

            if (ordJustAbove != steps)
                failures.Add($"scuff oracle: the surface is only at step {ordJustAbove} of {steps} immediately " +
                             $"above the smolder threshold - the smoke would arrive on a barely-marked " +
                             "building instead of a fully battered one (escalation flattened)");
            if (ordAtSmolder != steps + 1)
                failures.Add($"scuff oracle: the smolder rung reads {ordAtSmolder}, expected {steps + 1} - the " +
                             "scuff band and the burn ladder have drifted apart");

            // -- D. THE ONSET CANNOT BE AUTHORED BACK INTO A GAP -----------------
            if (onset < 1f - PredicateEpsilon)
                failures.Add($"scuff oracle: scuffOnset {onset} is BELOW the repair predicate's threshold " +
                             $"({1f - PredicateEpsilon}) - damage-states.json has re-opened a billable, " +
                             "invisible band. scuffOnset must stay at or above it.");
            if (steps < 1)
                failures.Add($"scuff oracle: scuffSteps {steps} < 1 - the band would have no visible step at all");

            ScuffStrengthOracle(failures, log, steps);
        }

        // =====================================================================
        // (E) WO-1352 FOLLOW-UP - THE STRENGTH ORACLE. "Non-silent" and "visible"
        // are DIFFERENT CLAIMS, and until this section existed only the first one
        // was pinned - which is exactly how a green suite shipped a tell the owner
        // could not see. Sections A-D prove the ladder has a rung at every eligible
        // HP; this section proves the FIRST rung is loud enough to notice and the
        // LAST rung is still quiet enough not to pre-empt the smolder.
        // ---------------------------------------------------------------------
        // THE MEASURED DEFECT IT PINS, in the owner's own device trace:
        //   [Flow:DamageVis] scuff step 1/3: hp=0.960 band=SCUFF
        //       applied albedo x0.88 (VALUE only, no hue shift) + smoothness x1.00
        // Two things are wrong on that line and both are now assertions here:
        //   1. x0.88 is a 12% one-channel value drop on a sunlit building - under
        //      the noticeability floor.
        //   2. smoothness x1.00 means the SECOND channel did nothing at all, and it
        //      could not have: the ramp interpolated from a hardcoded 1.0 on
        //      t = (step-1)/(steps-1), which is 0 at step 1 BY CONSTRUCTION. No
        //      authored value could have moved it. That is why the fix needed a
        //      step-1 endpoint of its own (DamageStatesCatalog.ScuffGlossStep1) and
        //      not merely a re-tune.
        //
        // ⚠ IT BINDS TO THE SHIPPING RAMP. StructureDamageVisuals.ScuffDarkenFor /
        // ScuffGlossMulFor are the exact functions ApplyScuff calls to build the
        // property block; they were extracted out of it for this reason. Re-deriving
        // lerp(min,max,t) locally here would prove only that the copy is intact.
        //
        // RED-FIRST MUTATION (the proof this section has teeth) - restore the
        // pre-follow-up values in BOTH damage-states.json twins:
        //     "scuffSteps": 3, "scuffMinDarken": 0.12, "scuffMaxDarken": 0.34,
        //     "scuffGlossFloor": 0.40, and DELETE "scuffGlossStep1".
        // Deleting the key makes ScuffGlossStep1 fall back to its C# initializer, so
        // to reproduce the ORIGINAL one-channel behaviour exactly, author
        // "scuffGlossStep1": 1.0 instead. That state fails E1 (step-1 darkening 0.120
        // < the 0.18 floor), E3 (step-1 gloss 1.000 is not below 0.95 - the second
        // channel is inert) and E4 (the gloss ladder's first jump is 0.000 while a
        // later jump is 0.300, i.e. back-loaded). Restoring 4 / 0.20 / 0.38 / 0.55 /
        // 0.25 turns it green. The GAP between billable and noticeable is the thing
        // under test, not the file.
        // =====================================================================
        private static void ScuffStrengthOracle(List<string> failures, StringBuilder log, int steps)
        {
            log.AppendLine("--- WO-1352 STRENGTH ORACLE (the first rung must be noticeable, the last must not pre-empt the smolder) ---");

            // The noticeability floor. Below this the tell is a rounding error on a
            // sunlit surface with no undamaged twin beside it to compare against.
            const float Step1DarkenFloor = 0.18f;
            // The "do not make her town a slum" ceiling. Most structures sit near full
            // HP most of the time, so step 1 is what a HEALTHY town looks like after a
            // graze: it must read on inspection, never at a glance.
            const float Step1DarkenCeiling = 0.25f;
            // The last scuff rung hands off to smoke. Past this the surface alone would
            // read as ruined and the smolder would stop being an escalation.
            const float LastDarkenCeiling = 0.45f;
            // Step 1 must engage the SECOND (non-colour) channel, not just darken.
            const float Step1GlossMustBeBelow = 0.95f;

            var darken = new float[steps + 1];
            var gloss = new float[steps + 1];
            darken[0] = StructureDamageVisuals.ScuffDarkenFor(0);
            gloss[0] = StructureDamageVisuals.ScuffGlossMulFor(0);
            for (int s = 1; s <= steps; s++)
            {
                darken[s] = StructureDamageVisuals.ScuffDarkenFor(s);
                gloss[s] = StructureDamageVisuals.ScuffGlossMulFor(s);
                log.AppendLine($"  step {s}/{steps}: albedo x{1f - darken[s]:0.000} (darken {darken[s]:0.000}) " +
                               $"gloss x{gloss[s]:0.000}");
            }

            // E0. PRISTINE IS PRISTINE. Step 0 must touch neither channel, or an
            // undamaged structure would be written to - which is also the SRP-batcher
            // cost guard the whole band is built around.
            if (darken[0] != 0f || gloss[0] != 1f)
                failures.Add($"strength oracle: step 0 is not a no-op (darken {darken[0]:0.###}, gloss {gloss[0]:0.###}) - " +
                             "an undamaged structure would take a property block and drop out of the SRP batcher");

            // E1. THE FIRST RUNG IS LOUD ENOUGH TO SEE.
            if (darken[1] < Step1DarkenFloor)
                failures.Add($"strength oracle: scuff step 1 darkens by only {darken[1]:0.###} (albedo x{1f - darken[1]:0.###}), " +
                             $"below the {Step1DarkenFloor:0.###} noticeability floor. This is the WO-1352 follow-up defect " +
                             "exactly: the band is non-silent and the player still cannot see it, so she is still being " +
                             "charged to repair a building that looks pristine.");

            // E2. AND NOT SO LOUD THAT A HEALTHY TOWN READS AS A SLUM.
            if (darken[1] > Step1DarkenCeiling)
                failures.Add($"strength oracle: scuff step 1 darkens by {darken[1]:0.###}, above the {Step1DarkenCeiling:0.###} " +
                             "ceiling. Most structures sit near full HP most of the time, so this is what an ordinary town " +
                             "would look like at a glance - the tell must read on INSPECTION, not turn the skyline grim.");
            if (darken[steps] > LastDarkenCeiling)
                failures.Add($"strength oracle: the last scuff step darkens by {darken[steps]:0.###}, above the " +
                             $"{LastDarkenCeiling:0.###} ceiling - the surface alone would read as ruined and the smolder " +
                             "at hp<=0.5 would stop being an escalation.");

            // E3. STEP 1 ENGAGES BOTH CHANNELS. The colour channel alone is the thing
            // that was measured as invisible; the gloss channel is free (same float, same
            // block) and is entirely outside the colourblind risk surface.
            if (!(gloss[1] < Step1GlossMustBeBelow))
                failures.Add($"strength oracle: scuff step 1 leaves smoothness at x{gloss[1]:0.###} - the SECOND, " +
                             "non-colour channel is inert at first blood, so the whole tell rests on one 20%-ish value " +
                             "drop. Her device line read 'smoothness x1.00' for precisely this reason: the ramp's t is 0 " +
                             "at step 1, so the gloss endpoint at step 1 must be authored (scuffGlossStep1), not inherited.");

            // E4. THE LADDER IS FRONT-LOADED, IN BOTH CHANNELS. pristine -> step 1 is the
            // only transition that changes CATEGORY (untouched -> worn) and the only one
            // with no rung above it to be compared against, so it must be the biggest
            // jump. Every later rung is read against its predecessor AND is converging on
            // the smolder, which brings a whole new channel (motion + smoke) of its own.
            float firstDarkenJump = darken[1] - darken[0];
            float firstGlossJump = gloss[0] - gloss[1];
            for (int s = 2; s <= steps; s++)
            {
                float dj = darken[s] - darken[s - 1];
                float gj = gloss[s - 1] - gloss[s];
                if (dj > firstDarkenJump + 1e-4f)
                    failures.Add($"strength oracle: the albedo ladder is BACK-loaded - the step {s - 1}->{s} jump " +
                                 $"({dj:0.###}) is larger than the pristine->step 1 jump ({firstDarkenJump:0.###}). " +
                                 "The budget is being spent telling 83% HP apart from 67%, which no player reads off a " +
                                 "building, instead of on the one transition that carries meaning.");
                if (gj > firstGlossJump + 1e-4f)
                    failures.Add($"strength oracle: the gloss ladder is BACK-loaded - the step {s - 1}->{s} jump " +
                                 $"({gj:0.###}) is larger than the pristine->step 1 jump ({firstGlossJump:0.###}).");
            }

            // E5. STRICT MONOTONICITY ON BOTH CHANNELS. A structure may never get
            // brighter or shinier as it gets closer to destruction.
            for (int s = 2; s <= steps; s++)
            {
                if (!(darken[s] > darken[s - 1]))
                    failures.Add($"strength oracle: darkening does not increase from step {s - 1} " +
                                 $"({darken[s - 1]:0.###}) to step {s} ({darken[s]:0.###}) - a rung that shows nothing " +
                                 "new is a rung the ladder does not need");
                if (!(gloss[s] < gloss[s - 1]))
                    failures.Add($"strength oracle: smoothness does not fall from step {s - 1} " +
                                 $"({gloss[s - 1]:0.###}) to step {s} ({gloss[s]:0.###}) - the surface would get SHINIER " +
                                 "as it got closer to being destroyed");
            }

            // E6. THE COLOURBLIND GUARANTEE, PROVEN RATHER THAN ASSERTED IN A COMMENT.
            // The owner is red/green colourblind, so the tell is a SCALAR multiply on R,
            // G and B alike - never a tint. Reproduce ApplyScuff's exact colour maths on a
            // reference albedo with three DIFFERENT channel values (a flat grey would pass
            // this test no matter how badly the code tinted) and prove three things:
            // hue is unchanged, saturation is unchanged, and the Rec.709 greyscale luma
            // scales by exactly the multiplier - i.e. the tell survives full desaturation
            // at full strength, which is the strongest form of the guarantee available.
            var reference = new Color(0.62f, 0.41f, 0.23f, 0.77f);   // warm timber, alpha != 1 on purpose
            Color.RGBToHSV(reference, out float h0, out float s0, out float v0);
            float refLuma = 0.2126f * reference.r + 0.7152f * reference.g + 0.0722f * reference.b;
            for (int s = 1; s <= steps; s++)
            {
                float mul = Mathf.Clamp01(1f - darken[s]);
                var tinted = new Color(reference.r * mul, reference.g * mul, reference.b * mul, reference.a);
                Color.RGBToHSV(tinted, out float h1, out float s1, out float v1);
                float luma = 0.2126f * tinted.r + 0.7152f * tinted.g + 0.0722f * tinted.b;

                if (Mathf.Abs(h1 - h0) > 1e-4f)
                    failures.Add($"strength oracle: step {s} SHIFTED HUE ({h0:0.#####} -> {h1:0.#####}). The scuff must " +
                                 "multiply R, G and B by the identical factor; the owner is red/green colourblind and a " +
                                 "hue-carried tell is unreadable to her.");
                if (Mathf.Abs(s1 - s0) > 1e-4f)
                    failures.Add($"strength oracle: step {s} CHANGED SATURATION ({s0:0.#####} -> {s1:0.#####}) - the tell " +
                                 "is no longer a pure value change.");
                if (Mathf.Abs(luma - refLuma * mul) > 1e-4f)
                    failures.Add($"strength oracle: step {s} greyscale luma {luma:0.#####} != base {refLuma:0.#####} x " +
                                 $"{mul:0.###} - the tell does NOT survive desaturation intact.");
                if (!Mathf.Approximately(tinted.a, reference.a))
                    failures.Add($"strength oracle: step {s} altered ALPHA ({reference.a:0.###} -> {tinted.a:0.###}) - a " +
                                 "transparent material must not become opaque because it took a hit.");
                if (s == steps)
                    log.AppendLine($"  desaturation proof at the strongest step ({s}): hue {h0:0.####}->{h1:0.####}, " +
                                   $"sat {s0:0.####}->{s1:0.####}, value {v0:0.####}->{v1:0.####}, " +
                                   $"greyscale luma {refLuma:0.####}->{luma:0.####} (= base x{mul:0.###}).");
            }
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                log.AppendLine("STRUCTURE_BURN_OK");
                return "ignite<=50% -> tick DoT -> repair extinguishes -> burn-to-death ends (no self-expire, no stack)";
            }
            log.AppendLine("STRUCTURE_BURN_FAIL");
            foreach (var f in failures) log.AppendLine("  FAIL: " + f);
            return "STRUCTURE_BURN_FAIL: " + string.Join("; ", failures);
        }
    }
}
