// =============================================================================
// FrameBudgetMeasureRegression - WO-1483 (empty Overworld runs at 22 fps with
// nothing spawned) and WO-1459 (device frame floor).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Shape: public static bool Run(out string reason)
// - registered into DataRegression.RunAll by the orchestrator.
//
// HONEST SCOPE (WO-1494: six suites claimed to MEASURE and were source text lint).
// THIS SUITE IS A SOURCE LINT AND SAYS SO IN EVERY REASON STRING. It opens each
// named file, extracts the named METHOD BODY by brace-matching, and asserts a
// FlowTrace.Measure scope is present inside it. It does NOT and CANNOT prove the
// frame cost - that is a headless + device capture (WO-1483 acceptance 1/2).
// What a lint CAN do is stop the scopes being deleted by a later edit, which is
// exactly the outcome CLAUDE.md sec.12 forbids ("NEVER STRIP FLOWTRACE").
//
// WHAT THIS PINS, AND WHY EACH ONE IS HERE
//   1. [sites]     every named Update/tick on the town frame path carries a
//                  FlowTrace.Measure INSIDE ITS OWN BODY. Anywhere-in-the-file is
//                  not good enough: WaveManager.cs already had a Measure on a LOAD
//                  path, and a file-scope grep would have passed on that alone
//                  while the frame path stayed unmeasured - the exact hole WO-1483
//                  opened with ("only 5 sites repo-wide, NONE on the frame path").
//   2. [4-arg]     each frame-path scope uses the ACCUMULATING 4-arg overload, not
//                  the 3-arg one. The 3-arg Scope logs on every Dispose; across the
//                  Sites table x 60 fps that is >1000 lines/sec, which evicts the boot
//                  window out of the Android logcat ring (memory:
//                  logcat-ring-buffer-destroys-evidence). Instrumentation that
//                  destroys the evidence is worse than none. NOTE: the ring size
//                  is PER DEVICE and must be read with `adb logcat -g`, never
//                  assumed - this comment carried a flat "256 KiB" until
//                  2026-09-10, when that command on the Seeker returned 16 MiB
//                  per buffer with nothing evicted (WO-1459 device capture). The
//                  rule is unchanged: a per-frame emit is forbidden regardless of
//                  ring size, because the ring you are logging into is unknown.
//   3. [budget]    the budget passed is a real, bounded number. A budget of 0
//                  silently disables the warn; a budget of 100 never fires. The
//                  measured floor is 45.1ms/frame, so a per-scope budget has to sit
//                  well under one 60fps frame to name a dominant cost.
//   4. [overload]  FlowTrace still DECLARES the 4-arg overload, its accumulator
//                  drain (SnapshotAndResetFrameSamples), and does NOT log from the
//                  FrameScope dispose path.
//   5. [rollup]    PerfReporter still emits the once-per-second "frame budget:"
//                  line, on its OWN timer - folding it into SampleInterval would
//                  change the cadence of the live `LOW fps=` telemetry these two
//                  WOs are being read from, which is a behaviour change.
//   6. [split]     PerfReporter still emits the CPU-main / render-thread / GPU
//                  "frame split:" line, from a helper called ONCE per roll-up
//                  window and never from Update, sampling every frame, with an
//                  explicit unavailable branch instead of zeros. WO-1459's
//                  2026-09-10 Seeker capture measured 43.9ms frames of which all
//                  20 accumulating scopes explained 5.8%; without this line the
//                  other ~94% has no owner and the ticket cannot be routed.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DeNelle.Editor.Regression
{
    public static class FrameBudgetMeasureRegression
    {
        private const string FlowTracePath   = "Assets/_Modules/Core/Diagnostics/FlowTrace.cs";
        private const string PerfReporterPath = "Assets/_Modules/Core/Diagnostics/PerfReporter.cs";

        // A per-scope budget outside this range is not a budget. Below the floor it
        // fires on every frame and becomes the spam it was added to avoid; above the
        // ceiling one scope could eat a whole 60fps frame and never announce itself.
        private const float MinSaneBudgetMs = 0.5f;
        private const float MaxSaneBudgetMs = 16f;

        // Declared as a PAIR so this file's own brace count stays balanced - CLAUDE.md sec.1
        // runs a naive open-vs-close brace tally over every .cs, and a lone open-brace char
        // literal in the brace-matcher below reads to that gate as a missing close.
        private const char OpenBrace = '{', CloseBrace = '}';

        /// <summary>file path -> the method signature whose BODY must carry the scope.</summary>
        private static readonly (string Path, string Signature, string Why)[] Sites =
        {
            ("Assets/_Modules/Village/Hero/HeroLocomotion.cs", "private void Update()",
             "the hero tick - a NavMeshAgent driven kinematically every frame"),
            ("Assets/_Modules/Village/Hero/SmartMobileCamera.cs", "private void LateUpdate()",
             "the camera tick - EnforceSoleCamera plus a follow solve every frame"),
            ("Assets/_Modules/Village/Waves/WaveManager.cs", "private void Update()",
             "the wave tick - runs in an EMPTY town too, which is WO-1483's whole case"),
            ("Assets/_Modules/Village/HUD/HudContextEvaluator.cs", "protected override void Poll()",
             "the HUD posture tick - 0.20s cadence, rebuilds context off scene state"),
            ("Assets/_Modules/Pets/PetHeroLeash.cs", "private void Update()",
             "the EchoWorldPresence tick - the Echo's one per-frame owner"),
            ("Assets/_Modules/Village/Buildings/Progression/ResourceCollectorService.cs",
             "public static List<PendingLine> PendingByResource()",
             "the collector sweep - static, ticked by CollectorStatusPublisher at 0.5s"),

            // --- WO-1483 second pass -------------------------------------------------
            // The six above named the hero/camera/wave/HUD spine. They cannot account for
            // 45.1ms on their own, and a table with holes in it attributes the missing ms
            // to nothing at all - which is how a perf WO turns back into a guess. These
            // nineteen close the rest of the EMPTY-town frame.
            //
            // EVIDENCE, STATED AT ITS ACTUAL TIER (CLAUDE.md sec.11B): what was measured is
            // (a) none of them is AUTHORED in Main_Castle_Overworld.unity - a scene-GUID grep
            // returns 0 for every one, because the town is BUILT AT RUNTIME, so a zero there
            // is not evidence of absence either way - and (b) each has a runtime
            // AddComponent<T> call site in the tree. That is NOT the same as proving each one
            // ticks in that scene on a given run; the roll-up's own Count column is what
            // proves that, per run, and a scope that reports Count=0 is a finding too.
            ("Assets/_Modules/Core/Addressables/EnemyContentWarmer.cs", "private void Update()",
             "the enemy addressables pump - drains deferred loads every frame"),
            ("Assets/_Modules/Core/Addressables/StructureContentWarmer.cs", "private void Update()",
             "the structure addressables pump - the town's own content path"),
            ("Assets/_Modules/Village/Vfx/VfxPool.cs", "private void Update()",
             "the pooled-VFX tick - PER-INSTANCE, one per live effect"),
            ("Assets/_Modules/Village/Vfx/VfxAuraProximityCuller.cs", "private void Update()",
             "the aura culler - RANKS every registered aura by distance"),
            ("Assets/_Modules/Village/Vfx/VfxPerformanceGate.cs", "private void Update()",
             "the VFX gate - walks VFXManager occupancy every frame"),
            ("Assets/_Modules/Village/NPCs/AmbientNPC.cs", "private void Update()",
             "the townsfolk brain - PER-INSTANCE and ticking with zero enemies spawned"),
            ("Assets/_Modules/Village/Enemies/EnemyBrain.cs", "private void Update()",
             "the enemy brain - measured so the table SEPARATES empty-town floor from population"),
            ("Assets/_Modules/Village/Buildings/Progression/CollectorStatusPublisher.cs",
             "private void Update()", "the 0.5s collector publish tick"),
            ("Assets/_Modules/Village/HUD/TownHudBridge.cs", "private void Update()",
             "the town HUD feed tick"),
            ("Assets/_Modules/HUD/Kit/HudMinimapWidget.cs", "private void LateUpdate()",
             "the minimap redraw - polls the scene, runs in an empty town"),
            ("Assets/_Modules/HUD/Kit/PostureEvaluator.cs", "private void Update()",
             "the HUD posture poll"),
            ("Assets/_Modules/Village/Enemies/PlayerAttackController.cs", "private void Update()",
             "the hero attack/input tick"),
            ("Assets/_Modules/Village/Hero/HeroTargetIndicator.cs", "private void Update()",
             "auto-acquire - scans for targets every frame even with nothing to acquire"),
            ("Assets/_Modules/Village/Hero/HeroTargetIndicator.cs", "private void LateUpdate()",
             "the SAME component's second frame callback - RebuildCandidates lives here, so " +
             "measuring only its Update would leave the scan unattributed"),
            ("Assets/_Modules/Village/World/TownActivityProbe.cs", "private void Update()",
             "the town activity probe - Poll ENUMERATES the town"),
            ("Assets/_Modules/Environment/NightTorchLightSystem.cs", "private void Update()",
             "the torch-light ramp - realtime lights are a classic empty-town floor cost"),
            ("Assets/_Modules/Village/World/WardTetherService.cs", "private void Update()",
             "kindle ticking plus the periodic tether eval"),
            ("Assets/_Modules/Village/BuildMode/BuildModeController.cs", "private void Update()",
             "the place loop - ticks in town whether or not build mode is armed"),
            ("Assets/_Modules/Village/Hero/HeroAbilities.cs", "private void Update()",
             "mana regen + cooldown ticks, every frame in town"),
        };

        public static bool Run(out string reason)
        {
            var notes = new StringBuilder();

            // --- 4. the accumulating overload still exists -----------------------------
            if (!TryRead(FlowTracePath, out string flowRaw, out reason)) return false;
            string flowCode = StripLiterals(StripComments(flowRaw));

            if (!flowCode.Contains("FrameScope Measure(string system, string what, float warnAboveMs, float everySeconds)"))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [overload]: FlowTrace no longer " +
                         "declares the 4-arg accumulating Measure overload. Every frame-path scope " +
                         "depends on it; without it they would have to fall back to the 3-arg form, " +
                         "which logs on EVERY dispose (~400 lines/sec across the seven sites).";
                return false;
            }
            if (!flowCode.Contains("SnapshotAndResetFrameSamples"))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [overload]: FlowTrace no longer " +
                         "exposes SnapshotAndResetFrameSamples. That drain is the ONLY way the " +
                         "accumulated per-frame table reaches a log line - without it every frame " +
                         "scope accumulates into a table nothing ever reads, which is silent " +
                         "instrumentation (CLAUDE.md sec.12: no silent failures).";
                return false;
            }

            string frameScopeBody = ExtractBody(flowCode, "public void Dispose()", "FrameScope");
            if (frameScopeBody == null)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [overload]: could not locate the " +
                         "FrameScope.Dispose body in " + FlowTracePath + ". The lint cannot prove the " +
                         "dispose path stays log-free, so it fails rather than pass blind.";
                return false;
            }
            if (frameScopeBody.Contains("Sink.Info") || frameScopeBody.Contains("Sink.Warn"))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [overload]: FrameScope.Dispose logs " +
                         "directly to the Sink. That is the 3-arg Scope's behaviour and it is exactly " +
                         "what the frame path must not do - one line per scope per frame evicts the " +
                         "boot window out of the Android logcat ring - whose size is per device and " +
                         "must be read with adb logcat -g, not assumed. Accumulate; let the throttled " +
                         "warn and PerfReporter's 1s roll-up do the emitting.";
                return false;
            }
            notes.Append("overload=4-arg+drain+silent-dispose ");

            // --- 5. the once-per-second roll-up still fires on its own timer ------------
            if (!TryRead(PerfReporterPath, out string perfRaw, out reason)) return false;
            string perfNoComments = StripComments(perfRaw);
            string perfCode = StripLiterals(perfNoComments);

            if (!perfNoComments.Contains("frame budget: "))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [rollup]: PerfReporter no longer " +
                         "emits the \"frame budget: \" line. The per-frame scopes accumulate into a " +
                         "table; this line is the only thing that reads it out, so removing it makes " +
                         "every scope on the frame path invisible.";
                return false;
            }
            if (!perfCode.Contains("SnapshotAndResetFrameSamples"))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [rollup]: PerfReporter does not " +
                         "drain the frame-sample table. Without the drain the accumulator grows " +
                         "forever and every roll-up reports the whole session, not the last window.";
                return false;
            }
            var rollupM = Regex.Match(perfCode,
                @"BudgetRollupInterval\s*=\s*([0-9]*\.?[0-9]+)\s*f?\s*;");
            if (!rollupM.Success)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [rollup]: BudgetRollupInterval is " +
                         "not a readable literal constant. The cadence must be a named, auditable " +
                         "number - a magic expression cannot be range-checked by this oracle.";
                return false;
            }
            float rollup = float.Parse(rollupM.Groups[1].Value,
                                       System.Globalization.CultureInfo.InvariantCulture);
            if (rollup <= 0f || rollup > 5f)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [rollup]: BudgetRollupInterval=" +
                         rollup.ToString("0.##") + "s is outside the sane 0..5s range. WO-1483 asks " +
                         "for a once-per-second roll-up; far slower and a hitch is averaged away, " +
                         "far faster and the roll-up itself becomes the spam.";
                return false;
            }
            // Pin the DESIGN (two independent timers), never the numbers. An exact-value
            // check here would fail a future perf WO that legitimately retunes the sample
            // cadence - this oracle does not own PerfReporter's sampling rate, it only owns
            // the rule that the roll-up must not ride on it.
            if (!Regex.IsMatch(perfCode, @"SampleInterval\s*=\s*[0-9]"))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [rollup]: PerfReporter no longer " +
                         "declares SampleInterval as its own constant. The frame-budget roll-up must " +
                         "run on a SEPARATE timer from the perf sample; folding them together changes " +
                         "the cadence of the live `LOW fps=` telemetry WO-1483 and WO-1459 are read " +
                         "from, which is a behaviour change, not instrumentation.";
                return false;
            }
            notes.Append("rollup=" + rollup.ToString("0.##") + "s ");

            // --- 6. the CPU-main / render-thread / GPU split rides the SAME cadence -----
            // WO-1459, device capture 2026-09-10: town idle measured 22-23 fps / 43.9 ms
            // while all 20 accumulating scopes together came to a mean of 57.9 ms per
            // 1000 ms of wall clock - about 5.8 percent. So the budget table above can
            // ELIMINATE those 20 sites and can never name the cost; the split line is the
            // instrument that says whether the missing time is main-thread, render-thread
            // or GPU. If it is ever deleted the ticket loses its only routing evidence and
            // the next seat is back to theorising, which is what sec.12 forbids.
            if (!perfNoComments.Contains("frame split: "))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: PerfReporter no longer " +
                         "emits the \"frame split: \" line. That line is the ONLY per-thread " +
                         "attribution in the build; without it roughly 94 percent of every device " +
                         "frame is unmeasured and a perf ticket can only be guessed at.";
                return false;
            }
            foreach (string ident in new[]
                     {
                         "FrameTimingManager.CaptureFrameTimings",
                         "FrameTimingManager.GetLatestTimings",
                         "FrameTimingManager.IsFeatureEnabled",
                         "cpuFrameTime",
                         "cpuMainThreadFrameTime",
                         "cpuRenderThreadFrameTime",
                         "gpuFrameTime",
                         "cpuMainThreadPresentWaitTime",
                     })
            {
                if (perfCode.Contains(ident)) continue;
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: PerfReporter no longer " +
                         "reads `" + ident + "`. The split needs the capture call, the count-returning " +
                         "getter, the availability check and all four timing terms - dropping any one " +
                         "of them turns the line into a partial picture that still reads as complete.";
                return false;
            }
            if (!perfNoComments.Contains("unavailable"))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: PerfReporter no longer " +
                         "carries the unavailable branch. When the platform reports no timings the " +
                         "line must SAY so; printing zeros instead reads as \"the GPU is free\" and " +
                         "routes the ticket to the wrong thread.";
                return false;
            }

            string splitEmitBody = ExtractBody(perfCode, "private void ReportFrameSplit()", null);
            if (splitEmitBody == null || splitEmitBody.IndexOf("FlowTrace.Step(", StringComparison.Ordinal) < 0)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: ReportFrameSplit is absent " +
                         "or no longer emits through FlowTrace. An accumulator nothing reads out is " +
                         "silent instrumentation - CLAUDE.md sec.12 forbids the silent path.";
                return false;
            }
            // The two cadences are DELIBERATELY asymmetric. The available line is data and
            // earns one emit per roll-up window; the unavailable line says the same static
            // thing every window and must therefore emit ONCE per play session. Without this
            // pin a build with the player setting off prints a line a second, and WebTrace
            // POSTs one a second, purely to repeat a fact no one can act on until a rebuild -
            // the same log-volume failure the 4-arg accumulating overload exists to prevent.
            if (splitEmitBody.IndexOf("FlowTrace.Once(", StringComparison.Ordinal) < 0)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: ReportFrameSplit no longer " +
                         "routes the unavailable state through FlowTrace.Once. Once dedupes on " +
                         "system plus key for the play session, which is what keeps a build with the " +
                         "frame-timing player setting off from repeating an unactionable line every " +
                         "second for the whole run.";
                return false;
            }

            // The emit must live at the ROLL-UP cadence and NEVER inside Update. Once per
            // frame is ~23 lines a second on the measured device, which is the flood the
            // 4-arg overload exists to prevent. Literals are read from the comment-stripped
            // (literal-preserving) text, structure from the literal-stripped text - the same
            // split the [rollup] section above uses, so prose cannot fake either half.
            string updateLiterals = ExtractBody(perfNoComments, "private void Update()", null);
            string updateCode     = ExtractBody(perfCode,       "private void Update()", null);
            if (updateLiterals == null || updateCode == null)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: could not locate " +
                         "PerfReporter's Update body, so the lint cannot prove the split line is not " +
                         "emitted per frame. It fails rather than pass blind.";
                return false;
            }
            if (updateLiterals.Contains("frame split")
                || updateCode.Contains("FlowTrace.Step(")
                || updateCode.Contains("FlowTrace.Warn("))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: PerfReporter.Update emits " +
                         "a trace line directly. The split and the budget roll-up must both be emitted " +
                         "from their throttled helpers - a per-frame emit is the log flood that evicts " +
                         "the evidence the capture was taken for.";
                return false;
            }
            if (!updateCode.Contains("SampleFrameSplit()"))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: PerfReporter.Update no " +
                         "longer calls SampleFrameSplit(). The timings must be captured EVERY frame " +
                         "and averaged; sampling only at the roll-up yields one instantaneous frame " +
                         "reading wearing the clothes of a mean.";
                return false;
            }
            int splitCallAt  = updateCode.IndexOf("ReportFrameSplit()", StringComparison.Ordinal);
            int splitCallAt2 = splitCallAt < 0
                ? -1
                : updateCode.IndexOf("ReportFrameSplit()", splitCallAt + 1, StringComparison.Ordinal);
            int rollupGuardAt = updateCode.IndexOf("BudgetRollupInterval", StringComparison.Ordinal);
            if (splitCallAt < 0 || splitCallAt2 >= 0 || rollupGuardAt < 0 || splitCallAt < rollupGuardAt)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [split]: ReportFrameSplit() is not " +
                         "called exactly once, from inside the BudgetRollupInterval branch. The split " +
                         "must ride the SAME cadence as the frame-budget line so the two read as one " +
                         "window in the device log - and it must not be called on the raw frame path.";
                return false;
            }
            notes.Append("split=cadence+availability-guarded ");

            // --- 1/2/3. every named frame-path method carries a 4-arg scope IN ITS BODY --
            var missing = new List<string>();
            foreach (var site in Sites)
            {
                if (!TryRead(site.Path, out string raw, out reason)) return false;

                // Comments AND literals stripped: a header mentioning Measure must not fake
                // a pass, and a brace inside a literal must not break the brace-match. The
                // only things read out of the body are the call shape and a numeric budget,
                // neither of which lives in a literal.
                string body = ExtractBody(StripLiterals(StripComments(raw)), site.Signature, null);
                if (body == null)
                {
                    reason = "[frame-budget-measure] SOURCE LINT FAIL [sites]: could not find `" +
                             site.Signature + "` in " + site.Path + " (" + site.Why + "). Either the " +
                             "method was renamed - in which case update this oracle in the SAME edit - " +
                             "or the frame path moved and is now unmeasured.";
                    return false;
                }

                int call = body.IndexOf("FlowTrace.Measure(", StringComparison.Ordinal);
                if (call < 0)
                {
                    missing.Add(site.Path + " :: " + site.Signature + " (" + site.Why + ")");
                    continue;
                }

                // [4-arg] + [budget]: read the argument list of THIS call.
                string args = ArgsOf(body, call + "FlowTrace.Measure(".Length);
                if (args == null || CountTopLevelArgs(args) != 4)
                {
                    reason = "[frame-budget-measure] SOURCE LINT FAIL [4-arg]: " + site.Path + " :: " +
                             site.Signature + " uses a FlowTrace.Measure overload with " +
                             (args == null ? "an unreadable" : CountTopLevelArgs(args).ToString()) +
                             " argument list. The frame path MUST use the 4-arg accumulating overload " +
                             "(system, what, warnAboveMs, everySeconds). The 3-arg form logs on every " +
                             "dispose - one line per frame per site.";
                    return false;
                }

                string budgetArg = TopLevelArg(args, 2);
                var bm = Regex.Match(budgetArg ?? "", @"([0-9]*\.?[0-9]+)\s*f?");
                if (!bm.Success)
                {
                    reason = "[frame-budget-measure] SOURCE LINT FAIL [budget]: " + site.Path + " :: " +
                             site.Signature + " passes a budget this oracle cannot read (\"" +
                             (budgetArg ?? "") + "\"). The budget must be an auditable literal.";
                    return false;
                }
                float budget = float.Parse(bm.Groups[1].Value,
                                           System.Globalization.CultureInfo.InvariantCulture);
                if (budget < MinSaneBudgetMs || budget > MaxSaneBudgetMs)
                {
                    reason = "[frame-budget-measure] SOURCE LINT FAIL [budget]: " + site.Path + " :: " +
                             site.Signature + " has warnAboveMs=" + budget.ToString("0.##") + "ms, " +
                             "outside the sane " + MinSaneBudgetMs + ".." + MaxSaneBudgetMs + "ms range. " +
                             "Below the floor it warns every frame and becomes the spam; above the " +
                             "ceiling one scope can eat a whole 60fps frame silently. The measured " +
                             "empty-town floor is 45.1ms/frame (WO-1483).";
                    return false;
                }
            }

            if (missing.Count > 0)
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL [sites]: " + missing.Count +
                         " town frame-path method(s) carry NO FlowTrace.Measure scope in their own " +
                         "body: " + string.Join(" | ", missing.ToArray()) + ". CLAUDE.md sec.12 - " +
                         "instrumentation is PERMANENT; flag it off, never strip it. WO-1483 opened " +
                         "because the frame path had zero measurement and the 45.1ms could not be " +
                         "attributed to anything.";
                return false;
            }

            reason = "[frame-budget-measure] SOURCE LINT PASS: " + Sites.Length +
                     " town frame-path methods each carry a 4-arg (accumulating) FlowTrace.Measure " +
                     "scope in their own body; " + notes.ToString().Trim() +
                     ". NOTE: this is a source lint - it proves the scopes EXIST, never what they cost. " +
                     "The ms numbers come from a headless + device capture (WO-1483 acceptance 1/2).";
            return true;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool TryRead(string relPath, out string text, out string reason)
        {
            text = null;
            string full = Path.Combine(Directory.GetCurrentDirectory(), relPath);
            if (!File.Exists(full))
            {
                reason = "[frame-budget-measure] SOURCE LINT FAIL: " + relPath + " not found.";
                return false;
            }
            text = File.ReadAllText(full);
            reason = null;
            return true;
        }

        /// <summary>
        /// Brace-match the body that follows <paramref name="signature"/>. When
        /// <paramref name="afterMarker"/> is given, the search starts after that marker
        /// (used to pick FrameScope's Dispose out of the two Dispose bodies in FlowTrace).
        /// Returns null when the signature is absent or the braces do not close.
        /// </summary>
        private static string ExtractBody(string src, string signature, string afterMarker)
        {
            int from = 0;
            if (!string.IsNullOrEmpty(afterMarker))
            {
                from = src.IndexOf(afterMarker, StringComparison.Ordinal);
                if (from < 0) return null;
            }

            int sig = src.IndexOf(signature, from, StringComparison.Ordinal);
            if (sig < 0) return null;

            int open = src.IndexOf(OpenBrace, sig + signature.Length);
            if (open < 0) return null;

            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == OpenBrace) depth++;
                else if (src[i] == CloseBrace)
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }

        /// <summary>The argument text of a call whose '(' has already been consumed at
        /// <paramref name="start"/>, or null when the parentheses do not close.</summary>
        private static string ArgsOf(string src, int start)
        {
            int depth = 1;
            for (int i = start; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return src.Substring(start, i - start);
                }
            }
            return null;
        }

        private static int CountTopLevelArgs(string args)
        {
            if (string.IsNullOrEmpty(args.Trim())) return 0;
            int n = 1, depth = 0;
            bool inStr = false;
            for (int i = 0; i < args.Length; i++)
            {
                char c = args[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') inStr = true;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0) n++;
            }
            return n;
        }

        /// <summary>Zero-based top-level argument, or null when out of range.</summary>
        private static string TopLevelArg(string args, int index)
        {
            int depth = 0, at = 0, start = 0;
            bool inStr = false;
            for (int i = 0; i <= args.Length; i++)
            {
                if (i == args.Length || (!inStr && depth == 0 && args[i] == ','))
                {
                    if (at == index) return args.Substring(start, i - start).Trim();
                    at++;
                    start = i + 1;
                    continue;
                }
                char c = args[i];
                if (inStr)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') inStr = true;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
            }
            return null;
        }

        /// <summary>Blank out // and /* */ comments, preserving length-ish structure.</summary>
        private static string StripComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            bool inLine = false, inBlock = false, inStr = false, inChar = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';

                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock) { if (c == '*' && n == '/') { inBlock = false; i++; } else if (c == '\n') sb.Append(c); continue; }
                if (inStr) { sb.Append(c); if (c == '\\') { if (i + 1 < src.Length) sb.Append(src[++i]); } else if (c == '"') inStr = false; continue; }
                if (inChar) { sb.Append(c); if (c == '\\') { if (i + 1 < src.Length) sb.Append(src[++i]); } else if (c == '\'') inChar = false; continue; }

                if (c == '/' && n == '/') { inLine = true; continue; }
                if (c == '/' && n == '*') { inBlock = true; i++; continue; }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '\'') { inChar = true; sb.Append(c); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Blank out string/char literal CONTENT so prose cannot fake an identifier check.</summary>
        private static string StripLiterals(string src)
        {
            var sb = new StringBuilder(src.Length);
            bool inStr = false, inChar = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') { inStr = false; sb.Append('"'); }
                    continue;
                }
                if (inChar)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '\'') { inChar = false; sb.Append('\''); }
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append('"'); continue; }
                if (c == '\'') { inChar = true; sb.Append('\''); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
