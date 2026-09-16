#if UNITY_EDITOR
// =============================================================================
// EndStateBodyFitRegression — WO-952. THE `COMPRESSED`-ABSENCE ORACLE THAT WAS
// SPECCED IN AUGUST AND NEVER WRITTEN.
// -----------------------------------------------------------------------------
// ⛔ READ THIS BEFORE WEAKENING ANYTHING HERE. WO-952 §2 bullet 3 (2026-08-10)
// asked for an oracle asserting the ABSENCE of the `body rows COMPRESSED to fit`
// line. KEY_FACTS.md flagged the same day that it did not exist. The ticket was
// nevertheless flipped to DONE by an AUDIT on 2026-08-21 — an audit, not device
// evidence — and on 2026-09-04 `grep -rn "COMPRESSED" Assets/Editor/Regression/`
// still returned ZERO hits. So the bug walked back in and THE OWNER found it,
// on the production candidate, instead of a gate:
//
//   [Flow:EndState] body rows COMPRESSED to fit: need=578px well=540px scale=0.933
//     - the panel hit its screen-height clamp; every band is now below its own
//       content size
//   (F8 seq 4680, SM02G4061955851, 2026-09-04T14:35:49Z, arena victory with a
//    gear drop — 5 spoils rows, panel 907px = frac 0.94, pinned at MaxPanelHalf)
//
// ⭐ THE TRIGGER IS THE GEAR DROP. Four spoils rows fit at 496 = 496 px, scale
// 1.000. The fifth row cannot share a band at 2 columns, so the grid goes 2 bands
// -> 3, the need goes 496 -> 578 px, and the panel grows into its clamp where the
// well stops at 540 px. Every band then compresses to 0.933 — below its own
// content's fixed size, which is the exact class the fixed-px band law exists to
// prevent. The WAVE-CLEAR path is clean (340 = 340, scale 1) — the 2026-08-10 fix
// holds; this is arena-with-gear only, which is why Case 1 below is an ARENA case
// and a wave-clear case would have proven nothing.
//
// ⭐ HOW TO SEE IT RED (the WO-1138 rule: a test that has never failed proves
// nothing). Set EndStateView.MaxSpoilColumns to 2 — that is precisely the pre-fix
// engine — and Case 1 fails with `scale=0.933 need=578 well=540`, the captured
// numbers to the pixel. Case 2 documents the same arithmetic as data, so the RED
// state is legible without editing anything at all.
//
// ⛔ IT ASKS THE SHIPPED SOLVER, IT DOES NOT RE-DERIVE IT. Every number comes from
// EndStateView.ProbeFit, which runs SpoilColumns / RequiredBodyPxAt /
// PanelHalfHeight and Bind's own well derivation. A suite that recomputed the band
// budget would be duplicated state and would drift exactly the way this WO's first
// "DONE" did.
//
// ⚠ WHAT THIS SUITE CANNOT DO, said plainly. It proves the GEOMETRY SOLVE fits. It
// cannot prove the panel LOOKS right — glyph rendering, art seating, the staged
// reveal. That needs eyes: `UICaptureLaunch.CaptureEndStateWaveClear` +
// `UI_ENDSTATE_FIT_OK`, and the EndState case at the device's real 2670x1200 that
// WO-952 also asked for. This suite closes the half that a gate can hold.
//
// ⛔ AND GEOMETRY CANNOT CATCH ALPHA — READ THIS BEFORE TRUSTING A GREEN RUN.
// On 2026-09-06 the owner watched a WAVE 7 CLEARED panel show its title over an
// EMPTY interior for the panel's whole 5-8 second life ("it is an empty box was
// sad"). EVERY case in this suite would have passed that panel: the rows were
// BUILT, the bands SOLVED, the scale was fine. They were simply never made
// visible — EndStateView.Track parks every body element at CanvasGroup.alpha 0
// (EndStateView.cs:2558) and a reveal coroutine is the only thing that raises it.
//
// ⛔ THE SCREENSHOT GATES ARE BLIND TO IT TOO, which is why only her eyes caught
// it. BOTH capture paths FORCE the alphas to 1 before photographing anything:
//   • EndStateView.Bind's own `if (!Application.isPlaying)` branch, and
//   • UICaptureLaunch `group.alpha = 1f` (UICaptureLaunch.cs:1416, :3062) — whose
//     comment at :5161 states plainly that "every CanvasGroup is still parked at
//     its start-of-tween alpha 0".
// So a reveal that never completes is invisible to the ENTIRE editor gate stack.
//
// The pin for that defect is therefore NOT here and CANNOT be: an EditMode suite
// has no player loop, so it cannot advance a coroutine that measures
// Time.unscaledDeltaTime. It is EndStateView.VerifyRevealCompleted — a RUNTIME
// probe that waits out the last reveal and FlowTrace.Fails with the final root
// alpha and a count of still-transparent elements, on the F8 channel. A PlayMode
// test would be the only way to gate it in CI. ⚠ DO NOT read a green run of this
// suite as "the end-state renders".
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Village.UI;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-952: no shipped end-state may compress its body bands below their own content
    /// size. The absence of the `COMPRESSED to fit` condition IS the acceptance signal.</summary>
    public static class EndStateBodyFitRegression
    {
        // The three surfaces the capture set already renders at, plus the one the owner plays on.
        private static readonly (int w, int h, string name)[] Surfaces =
        {
            (2670, 1200, "Seeker (her device)"),
            (2340, 1080, "common Android landscape"),
            (1920, 1080, "16:9 desktop"),
            (1080, 1920, "portrait"),
        };

        public static void RunStandalone()
        {
            if (!Run(out string result)) throw new InvalidOperationException(result);
            Debug.Log("ENDSTATE_BODY_FIT_OK " + result);
        }

        public static bool Run(out string result)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            try
            {
                TheCapturedArenaVictoryFits(failures, log);
                TheSpoilsLadderNeverCompresses(failures, log);
                TheGuardsThatMakeTheAboveMeanSomething(failures, log);
                TheWaveClearDamageReportFits(failures, log);
                TheModalRepairActionsRemainDistinct(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[endstate-fit] the suite itself threw: " + ex);
            }
            finally
            {
                try { ElarionUiKit.ClearSurfaceOverride(); } catch { }
            }

            if (failures.Count > 0)
            {
                result = "EndState body fit BROKEN:\n  - " + string.Join("\n  - ", failures);
                return false;
            }
            result = "no end-state body compresses below its own content size on any shipped " +
                     "surface, including the gear-drop arena victory that reached the owner.\n" +
                     log.ToString().TrimEnd();
            return true;
        }

        // Inspect the real buttons and runtime UnityEvent listeners. Never invoke the
        // scene's economy callback (or an edit-mode Destroy) to prove a binding.
        private static void TheModalRepairActionsRemainDistinct(List<string> failures, StringBuilder log)
        {
            foreach (var surface in Surfaces)
            foreach (bool repair in new[] { false, true })
            {
                EndStateView view = null;
                GameObject ownedEventSystem = null;
                float entryTimeScale = Time.timeScale;
                int entryHoldCount = WorldHold.Count;
                ElarionUiKit.SetSurfaceOverride(surface.w, surface.h);
                string tag = $"[wave-actions/{surface.name}/repair={repair}]";
                try
                {
                    var vm = EndStateVM.FromWaveClear(7);
                    vm.Spoils.Clear();
                    vm.AutoDismissSeconds = 0;
                    vm.Primary = null;
                    vm.Cta = null;
                    vm.CtaLabel = repair ? "Repair All - 120 wood, 40 iron" : null;
                    vm.CtaRoute = repair ? "repair-all" : "cta";
                    vm.CtaEnabled = false; // unaffordable must remain visible with cost
                    vm.Subtitle = "The realm holds - but it took damage.";
                    vm.Spoils.Add(new SpoilRowVM { Label = "Wood", Amount = "+240" });
                    vm.Spoils.Add(new SpoilRowVM { Label = "Iron", Amount = "+85" });
                    vm.Spoils.Add(new SpoilRowVM { Label = "North Gate - DESTROYED, looted 120",
                        Amount = "Rebuild 100 wood, 30 iron", Wide = true });
                    vm.Spoils.Add(new SpoilRowVM { Label = "Wall x3 - damaged 50%",
                        Amount = "Repair 20 wood, 10 iron", Wide = true });
                    if (vm.Compact || !vm.HoldWorld || vm.PrimaryLabel != "Prepare for Wave 8" || vm.PrimaryRoute != "prepare-next-wave")
                        failures.Add(tag + " live factory action/modal/hold contract changed");
                    // Geometry/binding proof only: EditMode does not guarantee OnDestroy on
                    // an unawakened behaviour. Do not acquire a runtime hold to measure UI.
                    vm.HoldWorld = false;
                    // Match the real edit-mode capture: runtime EnsureEventSystem creates a
                    // persistent object, which Unity correctly rejects outside Play mode.
                    if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                    {
                        ownedEventSystem = new GameObject("~EndStateFitEventSystem");
                        ownedEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                    }
                    view = EndStateView.Show(vm);
                    if (view == null) { failures.Add(tag + " view missing"); continue; }
                    Canvas.ForceUpdateCanvases();
                    float canvasH = ElarionUiKit.PostScaleCanvasHeight(view.transform);
                    var fit = EndStateView.ProbeFit(vm, canvasH);
                    if (fit.Scale < EndStateView.CompressFailBelowFrac || fit.RowsShown != 4)
                        failures.Add(tag + $" report lost fit/content: scale={fit.Scale} rows={fit.RowsShown}/4");
                    UnityEngine.UI.Button primary = null, secondary = null;
                    foreach (var button in view.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    {
                        var label = button.GetComponentInChildren<TMPro.TMP_Text>(true);
                        if (label == null) continue;
                        if (string.Equals(label.text, vm.PrimaryLabel, StringComparison.OrdinalIgnoreCase)) primary = button;
                        if (string.Equals(label.text, "Repair All - 120 wood, 40 iron", StringComparison.OrdinalIgnoreCase)) secondary = button;
                    }
                    if (primary == null || !primary.gameObject.activeInHierarchy)
                        failures.Add(tag + " Prepare control missing/inactive");
                    else if (!BoundTo(primary, view, "FirePrimary") || BoundTo(primary, view, "FireCta"))
                        failures.Add(tag + " Prepare is not bound to FirePrimary");
                    if (!repair && secondary != null) failures.Add(tag + " absent repair became a control");
                    if (repair)
                    {
                        if (secondary == null || !secondary.gameObject.activeInHierarchy)
                            failures.Add(tag + " priced Repair All control missing/inactive");
                        else
                        {
                            if (secondary.interactable) failures.Add(tag + " unaffordable repair is enabled");
                            if (!BoundTo(secondary, view, "FireCta") || BoundTo(secondary, view, "FirePrimary")) failures.Add(tag + " repair is not bound to FireCta");
                            if (primary != null)
                            {
                                var a = ButtonBounds(primary, view.transform);
                                var b = ButtonBounds(secondary, view.transform);
                                if (a.Intersects(b)) failures.Add(tag + " action rectangles overlap");
                            }
                        }
                    }
                    foreach (var button in new[] { primary, secondary })
                    {
                        if (button == null) continue;
                        var rt = (RectTransform)button.transform;
                        if (rt.rect.width < 112f || rt.rect.height < 112f)
                            failures.Add(tag + " action fell below 112px target floor");
                        var label = button.GetComponentInChildren<TMPro.TMP_Text>(true);
                        if (repair && primary != null && secondary != null
                            && Mathf.Abs(((RectTransform)primary.transform).anchoredPosition.y
                                - ((RectTransform)secondary.transform).anchoredPosition.y) < 1f)
                        {
                            var corners = new Vector3[4];
                            label.rectTransform.GetWorldCorners(corners);
                            float left = rt.InverseTransformPoint(corners[0]).x - rt.rect.xMin;
                            float right = rt.rect.xMax - rt.InverseTransformPoint(corners[2]).x;
                            if (left < 47.9f || right < 47.9f)
                                failures.Add(tag + " paired caption consumes the 48px painted-face side inset");
                        }
                        label.ForceMeshUpdate(true, true);
                        if (label.isTextTruncated || label.isTextOverflowing || label.textInfo.characterCount == 0)
                            failures.Add(tag + " action label clipped/blank: " + label.text
                                + $" (button={rt.rect.width:0.##}x{rt.rect.height:0.##}, label={label.rectTransform.rect.width:0.##}x{label.rectTransform.rect.height:0.##}, font={label.fontSize:0.##})");
                        if (label.textInfo.lineCount != 1 || label.fontSize < 30f
                            || label.textWrappingMode != TMPro.TextWrappingModes.NoWrap)
                            failures.Add(tag + " action caption must fit one painted-face line at >=30px: " + label.text);
                    }
                    log.AppendLine(tag + " real controls/fit/bindings inspected");
                }
                finally
                {
                    if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
                    if (ownedEventSystem != null) UnityEngine.Object.DestroyImmediate(ownedEventSystem);
                    if (WorldHold.Count != entryHoldCount)
                        failures.Add(tag + " geometry fixture changed WorldHold ownership count");
                    if (!Mathf.Approximately(Time.timeScale, entryTimeScale))
                        failures.Add(tag + " geometry fixture changed Time.timeScale");
                    Time.timeScale = entryTimeScale;
                    ElarionUiKit.ClearSurfaceOverride();
                }
            }
        }

        private static Bounds ButtonBounds(UnityEngine.UI.Button button, Transform root)
        {
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            var bounds = new Bounds(root.InverseTransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < 4; i++) bounds.Encapsulate(root.InverseTransformPoint(corners[i]));
            return bounds;
        }

        private static bool BoundTo(UnityEngine.UI.Button button, EndStateView target, string method)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var prepare = typeof(UnityEngine.Events.UnityEventBase).GetMethod("PrepareInvoke", flags);
            var calls = prepare?.Invoke(button.onClick, null) as System.Collections.IEnumerable;
            if (calls == null) return false;
            foreach (var call in calls)
            for (var type = call.GetType(); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(flags | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (!(field.GetValue(call) is Delegate callback)) continue;
                if (DelegateReaches(callback, target, method, new HashSet<object>(), 0)) return true;
            }
            return false;
        }

        private static bool DelegateReaches(object value, EndStateView target, string method,
            HashSet<object> visited, int depth)
        {
            if (value == null || depth > 8 || !visited.Add(value)) return false;
            if (value is Delegate callback)
            {
                foreach (var listener in callback.GetInvocationList())
                {
                    if (ReferenceEquals(listener.Target, target) && listener.Method.Name == method) return true;
                    if (DelegateReaches(listener.Target, target, method, visited, depth + 1)) return true;
                }
                return false;
            }
            // ElarionUiKit wraps Action in () => onClick(). Follow only compiler-generated
            // closure fields and delegate targets; never traverse arbitrary scene objects,
            // invoke callbacks/properties, or accept a matching method name on another view.
            var type = value.GetType();
            if (!Attribute.IsDefined(type, typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
                return false;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            foreach (var field in type.GetFields(flags))
            {
                var child = field.GetValue(value);
                if (child is Delegate || (child != null && Attribute.IsDefined(child.GetType(),
                    typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))))
                    if (DelegateReaches(child, target, method, visited, depth + 1)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------------
        //  CASE 1 — ⭐ THE CAPTURE, TO THE PIXEL. The exact VM shape behind
        //  F8 seq 4680, on her device's surface.
        // ---------------------------------------------------------------------
        private static void TheCapturedArenaVictoryFits(List<string> failures, StringBuilder log)
        {
            // Post-scale canvas height MEASURED off the capture: panel 907px at frac 0.94 means
            // 907 / (2 * 0.47) = 965 ref px. Pinned, so this is the number the clamp produced.
            const float CanvasH = 965.4f;

            ElarionUiKit.SetSurfaceOverride(2670, 1200);
            try
            {
                var vm = ArenaVictoryFixture(spoils: 5);
                var fit = EndStateView.ProbeFit(vm, CanvasH);

                if (fit.Scale < EndStateView.CompressFailBelowFrac)
                    failures.Add($"[arena-gear] ⛔ THE WO-952 DEFECT IS BACK: a 5-row arena victory " +
                                 $"(gear drop) compresses to scale={fit.Scale:0.###} " +
                                 $"(need={fit.NeedPx:0}px well={fit.WellPx:0}px, panel {fit.PanelPx:0}px " +
                                 $"= frac {fit.PanelFrac:0.###}, {fit.Columns} column(s), " +
                                 $"{fit.SpoilBands} spoils band(s)). Every band is now BELOW its own " +
                                 "content's fixed size - the subtitle under its line box, the stars " +
                                 "under their bbox, the rows under their icon. The owner saw this on " +
                                 "the production candidate. ⛔ The fix is to REFLOW (more spoils " +
                                 "columns), never to shrink, and never to raise MaxPanelHalf - at " +
                                 "0.47 against a 0.50 centre the panel already spans the documented " +
                                 "0.03..0.97 and any more clips the top edge (WO-894).");
                else
                    log.AppendLine($"  [arena-gear] 5-row arena victory at 2670x1200: need={fit.NeedPx:0}px " +
                                   $"well={fit.WellPx:0}px scale={fit.Scale:0.###} " +
                                   $"({fit.Columns} columns, {fit.SpoilBands} bands, panel " +
                                   $"frac {fit.PanelFrac:0.###})");

                // The 4-row victory that ALREADY worked must be untouched: same 2-column layout,
                // same 638px panel, scale 1. A "fix" that reflows everything is a different screen.
                var four = EndStateView.ProbeFit(ArenaVictoryFixture(spoils: 4), CanvasH);
                if (four.Columns != 2)
                    failures.Add($"[arena-4row] the 4-row arena victory now solves at {four.Columns} " +
                                 "columns. It fitted at 2 (496 = 496px, scale 1.000, panel frac 0.661) " +
                                 "on 2026-09-04 and its layout is the WO-894 ruling - nothing may " +
                                 "reflow unless it does not fit.");
                if (four.Scale < EndStateView.CompressFailBelowFrac)
                    failures.Add($"[arena-4row] the 4-row victory regressed into compression " +
                                 $"(scale={four.Scale:0.###}).");
                else
                    log.AppendLine($"  [arena-4row] unchanged: {four.Columns} columns, " +
                                   $"need={four.NeedPx:0}px well={four.WellPx:0}px scale={four.Scale:0.###}");
            }
            finally { ElarionUiKit.ClearSurfaceOverride(); }
        }

        // ---------------------------------------------------------------------
        //  CASE 2 — the whole spoils ladder, on every shipped surface. The
        //  capture was ONE row count on ONE device; the defect class is
        //  "content taller than the clamp", so sweep it.
        // ---------------------------------------------------------------------
        private static void TheSpoilsLadderNeverCompresses(List<string> failures, StringBuilder log)
        {
            foreach (var s in Surfaces)
            {
                ElarionUiKit.SetSurfaceOverride(s.w, s.h);
                try
                {
                    // The canvas the kit resolves for this surface, in the same reference space
                    // PanelHalfHeight works in. 965.4 is the measured Seeker value; the others
                    // scale with the short axis exactly as the ScaleWithScreenSize scaler does.
                    float canvasH = 965.4f * (Mathf.Min(s.w, s.h) / 1200f);
                    if (s.h > s.w) canvasH = 1920f;   // portrait: the kit's own reference height

                    for (int rows = 1; rows <= 6; rows++)
                    {
                        var fit = EndStateView.ProbeFit(ArenaVictoryFixture(rows), canvasH);
                        if (fit.Scale < EndStateView.CompressFailBelowFrac)
                            failures.Add($"[ladder/{s.name}] {rows} spoils row(s) compress to " +
                                         $"scale={fit.Scale:0.###} (need={fit.NeedPx:0}px " +
                                         $"well={fit.WellPx:0}px, panel frac {fit.PanelFrac:0.###}, " +
                                         $"{fit.Columns} column(s)). " +
                                         (fit.PinnedAtClamp
                                            ? "The panel is PINNED at MaxPanelHalf, so the well cannot " +
                                              "grow to meet the need - reflow the grid or cut content; " +
                                              "do NOT raise the clamp and do NOT let the bands shrink."
                                            : "The panel is NOT at its clamp, so this is a solve bug " +
                                              "rather than a content-too-tall case - the panel should " +
                                              "have grown to fit."));
                    }
                }
                finally { ElarionUiKit.ClearSurfaceOverride(); }
            }
            log.AppendLine("  [ladder] 1-6 spoils rows x " + Surfaces.Length +
                           " shipped surfaces: no case compresses");
        }

        // ---------------------------------------------------------------------
        //  CASE 3 — ⛔ SILENCE MUST NOT PASS, AND THE FIX MUST NOT BE A CHEAT.
        //  This suite is only worth its runtime if the probe is really wired to
        //  the shipped solver and the escape hatch really has a floor.
        // ---------------------------------------------------------------------
        private static void TheGuardsThatMakeTheAboveMeanSomething(List<string> failures, StringBuilder log)
        {
            // A probe that answers zeros would make every case above pass vacuously.
            ElarionUiKit.SetSurfaceOverride(2670, 1200);
            try
            {
                var fit = EndStateView.ProbeFit(ArenaVictoryFixture(5), 965.4f);
                if (fit.NeedPx <= 1f || fit.WellPx <= 1f || fit.PanelPx <= 1f || fit.SpoilBands < 1)
                    failures.Add("[guard] EndStateView.ProbeFit returned a degenerate solve " +
                                 $"(need={fit.NeedPx:0} well={fit.WellPx:0} panel={fit.PanelPx:0} " +
                                 $"bands={fit.SpoilBands}). Zeros make every assertion above pass " +
                                 "without measuring anything - that is the hollow-green class this " +
                                 "suite exists to end.");

                // The reflow must stay inside the LEGIBILITY floor, or it has simply moved the
                // defect from "too short" to "too narrow" (MinSpoilColumnPx is derived: the
                // longest stock label, 'Experience', needs a 407px cell to stay off the font floor).
                if (fit.Columns > 3)
                    failures.Add($"[guard] the spoils grid reflowed to {fit.Columns} columns. Past 3 a " +
                                 "reward plate stops reading as a line item and becomes a tile grid - " +
                                 "and the WO-894 ruling that made two columns legal was about " +
                                 "READABILITY, not about winning height at any price.");

                // Portrait must stay single-column, as ruled: at 1080x1920 a column is ~269px,
                // under the 420px floor.
                ElarionUiKit.ClearSurfaceOverride();
                ElarionUiKit.SetSurfaceOverride(1080, 1920);
                var portrait = EndStateView.ProbeFit(ArenaVictoryFixture(5), 1920f);
                if (portrait.Columns != 1)
                    failures.Add($"[guard] PORTRAIT solved to {portrait.Columns} spoils columns. A portrait " +
                                 "column is ~269 ref px against a 420px legibility floor, so multi-column " +
                                 "portrait was ruled out in WO-894 and the width floor is what enforces it.");
                else
                    log.AppendLine("  [guard] portrait stays single-column; the reflow is width-gated, " +
                                   "not height-driven");
            }
            finally { ElarionUiKit.ClearSurfaceOverride(); }
        }

        // ---------------------------------------------------------------------
        //  CASE 4 — ⭐ THE WAVE-CLEAR DAMAGE REPORT (WO-952 REOPEN #3).
        //  F8 build 2026.09.06.357599, SM02G4061955851, 2026-09-06T09:12:29Z:
        //    [Flow:EndState] body rows COMPRESSED to fit: need=668px well=540px
        //      scale=0.808
        //    (EndStateView.BuildBody <- Bind <- Show <- WaveClearRoutine)
        //
        //  ⛔ WHY CASES 1-3 WERE ALL GREEN WHILE THE OWNER'S DEVICE FAILED, which
        //  is the real lesson here: EVERY case above uses ArenaVictoryFixture, and
        //  that fixture sets Stars=3 / TimeSeconds=41 and builds ZERO
        //  SpoilRowVM.Wide rows. Those two facts are exactly what make the two
        //  existing reflow levers WORK for it — and exactly what a wave-clear
        //  damage report does not have. This suite's own banner even records
        //  "the WAVE-CLEAR path is clean (340 = 340, scale 1)": true, and true
        //  only because the wave-clear VM it measured carried no damage report.
        //  The defect class was never "arena with a gear drop"; it was "content
        //  the levers cannot reflow", and the ladder swept the wrong axis.
        //
        //  On the shape below BOTH existing levers are STRUCTURALLY INERT:
        //    • columns — every damage row is Wide, and SpoilBandPlan gives a wide
        //      row a band to itself at 1, 2 OR 3 columns. Escalating changes
        //      nothing at all.
        //    • strip   — FromWaveClear leaves Stars/TimeSeconds at -1, so
        //      NarrativeStripPx == EmblemPx and merging one element saves 0px.
        //  Hence the third lever (EndStateView.SpoilRowsShown) and this case.
        //
        //  ⭐ HOW TO SEE IT RED (the WO-1138 rule): raise MinSpoilRowsShown in
        //  EndStateView to 8 — that disables the trim for this shape and restores
        //  the pre-fix engine — and WaveClearDamageFixture(2, 5) fails with
        //  need=668px well=540px scale=0.808, the captured numbers to the pixel.
        //
        //  ⚠ (2, 5) IS SEVEN ROWS AND SIX BANDS, and the distinction is the whole
        //  reason the 668 is reproducible: the 2 reward rows are NARROW and PAIR
        //  into one band at 2 columns, while each of the 5 damage rows is Wide and
        //  takes a band alone. The budget counts BANDS, never rows:
        //    64 emblem + 60 subtitle + 16 lead gap + 6x64 rows + 8x18 gaps = 668.
        //  (2, 4) is 5 bands = 586px and does NOT reproduce the capture.
        // ---------------------------------------------------------------------
        private static void TheWaveClearDamageReportFits(List<string> failures, StringBuilder log)
        {
            foreach (var s in Surfaces)
            {
                ElarionUiKit.SetSurfaceOverride(s.w, s.h);
                try
                {
                    float canvasH = 965.4f * (Mathf.Min(s.w, s.h) / 1200f);
                    if (s.h > s.w) canvasH = 1920f;

                    // WaveDamageReport.MaxRows is 8, and the FULL modal had no ceiling at all
                    // (EndStateVM.FromWaveClear: damageBudget = vm.Compact ? ... : damageAvailable),
                    // so sweep the whole range the model can actually hand over.
                    for (int dmg = 1; dmg <= 8; dmg++)
                    {
                        var vm = WaveClearDamageFixture(rewardRows: 2, damageRows: dmg);
                        var fit = EndStateView.ProbeFit(vm, canvasH);

                        // (a) THE INVARIANT: no band may resolve below its own content size.
                        if (fit.Scale < EndStateView.CompressFailBelowFrac)
                            failures.Add($"[wave-damage/{s.name}] ⛔ WO-952 REOPEN #3 IS BACK: a " +
                                         $"wave clear with {dmg} damage row(s) compresses to " +
                                         $"scale={fit.Scale:0.###} (need={fit.NeedPx:0}px " +
                                         $"well={fit.WellPx:0}px, panel frac {fit.PanelFrac:0.###}, " +
                                         $"{fit.Columns} column(s), {fit.SpoilBands} band(s), " +
                                         $"rows {fit.RowsShown}/{fit.RowsRequested}). The column and " +
                                         "strip levers CANNOT help this shape (wide rows / no " +
                                         "stars+time) - the row trim is the only lever, so this " +
                                         "means the trim stopped trimming. Do NOT answer it by " +
                                         "raising MaxPanelHalf or by letting the bands shrink.");

                        // (b) THE BODY IS NEVER EMPTY. The trim exists to keep rows legible, never
                        //     to remove the reason the panel opened.
                        if (fit.RowsShown < 1 || fit.SpoilBands < 1)
                            failures.Add($"[wave-damage/{s.name}] {dmg} damage row(s) trimmed the body " +
                                         $"to {fit.RowsShown} row(s) / {fit.SpoilBands} band(s). A " +
                                         "wave-clear panel that shows a headline over an EMPTY ledger " +
                                         "is the defect, not the fix - MinSpoilRowsShown is the floor.");

                        // (c) REWARDS SURVIVE THE TRIM. The trim takes from the TAIL, and
                        //     FromWaveClear appends rewards FIRST and damage LAST, so a trim that
                        //     ever reaches the reward block has trimmed in the wrong direction.
                        if (fit.RowsShown < 2)
                            failures.Add($"[wave-damage/{s.name}] the trim cut into the REWARD rows " +
                                         $"({fit.RowsShown} of {fit.RowsRequested} shown, 2 of them " +
                                         "rewards). Rewards are the earn beat the banner exists for; " +
                                         "the damage tail is what may be dropped.");

                        // (d) A DROP IS NEVER SILENT: the shortfall band is budgeted as a real band,
                        //     so a trimmed panel always carries one MORE band than it has rows.
                        if (fit.RowsDropped > 0 && fit.SpoilBands <= fit.RowsShown - 1)
                            failures.Add($"[wave-damage/{s.name}] {fit.RowsDropped} row(s) were dropped " +
                                         $"but the plan holds only {fit.SpoilBands} band(s) for " +
                                         $"{fit.RowsShown} shown row(s) - the shortfall band is missing, " +
                                         "so the player is not told the ledger is partial. A hidden " +
                                         "damage row reads as 'that building is fine'.");
                    }

                    // ⭐ THE CAPTURED SHAPE, and the band arithmetic behind the 668 spelled out so
                    // nobody has to re-derive it: 2 REWARD rows are NARROW, so at 2 columns they
                    // PAIR INTO ONE band; the 5 damage rows are Wide and take one each. That is
                    // 6 SPOILS BANDS from 7 rows — and it is BANDS the budget counts:
                    //   emblem 64 + subtitle 60x1 + lead gap 16 + 6x64 rows + 8x18 gaps = 668 px,
                    // against the 540 px well the clamp allows. (2,4) would be 5 bands = 586 px
                    // and would NOT reproduce the capture — the row count is not the band count.
                    var captured = EndStateView.ProbeFit(WaveClearDamageFixture(2, 5), canvasH);
                    log.AppendLine($"  [wave-damage/{s.name}] the captured shape (7 rows -> 6 bands): " +
                                   $"need={captured.NeedPx:0}px well={captured.WellPx:0}px " +
                                   $"scale={captured.Scale:0.###}, rows " +
                                   $"{captured.RowsShown}/{captured.RowsRequested} " +
                                   $"(dropped {captured.RowsDropped}), {captured.SpoilBands} band(s)");
                }
                finally { ElarionUiKit.ClearSurfaceOverride(); }
            }
        }

        // ---------------------------------------------------------------------
        //  THE WAVE-CLEAR FIXTURE — the shape behind the 2026-09-06 capture.
        //  Mirrors EndStateVM.FromWaveClear: emblem, ONE-line subtitle, NO stars,
        //  NO time, resource reward rows first (narrow), damage rows last (Wide).
        //  Hand-built for the same reason ArenaVictoryFixture is: the real factory
        //  resolves icons through RpgUiCatalog and calls WaveDamageReport.Collect,
        //  neither of which is answerable in a headless editor run.
        // ---------------------------------------------------------------------
        private static EndStateVM WaveClearDamageFixture(int rewardRows, int damageRows)
        {
            var vm = new EndStateVM
            {
                Kind = EndStateKind.WaveResults,
                Title = "Wave 7 Cleared",
                // The exact one-line string FromWaveClear sets once damage exists.
                Subtitle = "The realm holds - but it took damage.",
                Emblem = OnePixelSprite(),
                PrimaryLabel = "Prepare for Wave 8",
                PrimaryRoute = "prepare-next-wave",
                Compact = false,
                HoldWorld = true,
                // ⚠ LOAD-BEARING OMISSION: Stars and TimeSeconds stay at their -1 defaults,
                // exactly as FromWaveClear leaves them. Setting them here would hand the strip
                // lever something to merge and the case would stop reproducing the capture.
            };
            var rewards = new (string label, string amount)[]
            {
                ("Wood", "+180"), ("Iron", "+64"), ("Crystals", "+12"),
            };
            for (int i = 0; i < Mathf.Clamp(rewardRows, 0, rewards.Length); i++)
                vm.Spoils.Add(new SpoilRowVM { Label = rewards[i].label, Amount = rewards[i].amount });

            // Damage rows: a SENTENCE plus a materials cost, and Wide - the grammar
            // FromWaveClear gives them, and the reason the column lever cannot pair them.
            for (int i = 0; i < damageRows; i++)
                vm.Spoils.Add(new SpoilRowVM
                {
                    Label  = "Archer Tower " + (i + 1) + " - damaged " + (20 + i * 7) + "%",
                    Amount = "Repair 40 wood, 12 iron",
                    Wide   = true,
                });
            return vm;
        }

        // ---------------------------------------------------------------------
        //  THE FIXTURE — the arena victory VM, built by hand.
        // ---------------------------------------------------------------------
        //  ⚠ NOT EndStateVM.FromBattleVictory, and the reason matters: that factory
        //  resolves its emblem and row icons through RpgUiCatalog / ItemIconCatalog,
        //  which can return null in a headless editor run. A null Emblem silently
        //  drops a 64px band from the budget, so the suite would measure a DIFFERENT
        //  panel from the one that shipped and would pass while the screen failed.
        //  UICaptureLaunch's EndState cases build fixtures for exactly this reason.
        //
        //  The shape is the capture's: emblem + one-line subtitle + stars + time +
        //  N spoils rows, which reproduces need=578px at 2 columns to the pixel.
        // ---------------------------------------------------------------------
        private static EndStateVM ArenaVictoryFixture(int spoils)
        {
            var vm = new EndStateVM
            {
                Kind = EndStateKind.Victory,
                Title = "Victory!",
                Subtitle = "The realm is safer because of you!",
                Stars = 3,
                TimeSeconds = 41f,
                Emblem = OnePixelSprite(),
                PrimaryLabel = "Continue",
                PrimaryRoute = "return-home",
                Compact = false,
                HoldWorld = true,
            };
            // Descending importance, exactly as FromBattleVictory builds them; the 5th is the
            // GEAR DROP that tipped the panel past its clamp.
            var rows = new (string label, string amount)[]
            {
                ("Experience", "+60"),
                ("Gold",       "+120"),
                ("Wood",       "+33"),
                ("Iron",       "+15"),
                ("Emberglass Staff", "Equipped"),
                ("Wisdom",     "+4"),
            };
            for (int i = 0; i < Mathf.Clamp(spoils, 0, rows.Length); i++)
                vm.Spoils.Add(new SpoilRowVM { Label = rows[i].label, Amount = rows[i].amount });
            return vm;
        }

        private static Sprite _emblem;

        /// <summary>A real, non-null Sprite so the emblem band is BUDGETED. Its pixels are never
        /// drawn — only `vm.Emblem != null` is read by the solve.</summary>
        private static Sprite OnePixelSprite()
        {
            if (_emblem != null) return _emblem;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.Apply();
            _emblem = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _emblem.hideFlags = HideFlags.HideAndDontSave;
            return _emblem;
        }
    }
}
#endif
