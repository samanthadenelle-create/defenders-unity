// =============================================================================
// PerfReporter — self-reporting perf gauge (owner 2026-07-01: "add a flow on the
// perf to self report data").
// -----------------------------------------------------------------------------
// Rides the EXISTING web-trace pipeline: it emits its samples through FlowTrace
// (category "Perf"), so on a build with web-tracing active WebTrace captures the
// [Flow:Perf] lines and POSTs them to /api/trace -> Neon. That lets us read REAL
// perf numbers off real Pi / phone devices (FPS, frame time, memory, scene, live
// tower + enemy counts) so we can tune dungeon perf and find the tower limit
// WITHOUT a local playtest.
//
// DESIGN (mirrors WebTrace.cs / FlowTrace.cs — INSTRUMENT-don't-guess, §12):
//   * RELEASE-SAFE: no #if DEVELOPMENT_BUILD, no asmdef change. It lives in
//     DeNelle.Core.Diagnostics (every assembly references DeNelle.Core), so it
//     compiles + runs on every platform incl. WebGL.
//   * Self-bootstrapping: [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] spins up
//     ONE DontDestroyOnLoad host. Every step is Guard.Try-wrapped — a diagnostic
//     must NEVER break startup or a frame.
//   * VOLUME CONTROL: it only samples + emits while FlowTrace.Enabled is true (the
//     same master switch WebTrace flips ON when ff.webtrace is active, and that is
//     ON by default in the editor / a dev build). When Enabled is false the Update
//     early-outs -> zero cost (matches FlowTrace's own off-path philosophy). Because
//     sampling only runs when Enabled, the readout below is live exactly when a dev
//     HUD would be shown (editor / dev build / ff.webtrace).
//   * NO Village reference: PerfReporter is in DeNelle.Core, but Tower / Enemy live
//     in DeNelle.Village (Core does NOT reference Village). We count them by scanning
//     FindObjectsByType<MonoBehaviour> once per sample and matching GetType().Name —
//     no compile dependency on the Village assembly.
//
// The OwnerDevToolsOverlay (or any code) can read LastSummary / LastFps for a live
// on-screen gauge, and call MarkEvent("entered-dungeon") to stamp a perf snapshot
// at a moment of interest.
// =============================================================================
using UnityEngine;
using UnityEngine.SceneManagement;
// Force the engine logger even if a DeNelle.Core.Debug namespace exists
// (see memory: core-namespace-shadows-unityengine-statics).
using Debug = UnityEngine.Debug;

namespace DeNelle.Core.Diagnostics
{
    /// <summary>
    /// Periodically samples runtime perf (FPS, frame ms, memory, scene, tower/enemy
    /// counts) and self-reports it through FlowTrace -> WebTrace -> /api/trace. Gated
    /// by FlowTrace.Enabled (zero cost when off). Exposes LastSummary / LastFps for a
    /// dev HUD and MarkEvent(label) for one-off perf stamps.
    /// </summary>
    public sealed class PerfReporter : MonoBehaviour
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const float SampleInterval = 4f;    // seconds between samples (~1/4s)
        private const float LowFpsWarn     = 30f;    // < this emits a Warn, not a Step
        // WO-1483: the frame-budget roll-up runs on its OWN timer. Do NOT fold it into
        // SampleInterval — that would change the cadence of the live `LOW fps=` telemetry
        // the perf WOs are being read from, which is a behaviour change, not instrumentation.
        private const float BudgetRollupInterval = 1f;  // seconds between frame-budget roll-ups
        private const int   BudgetTopN           = 5;   // "top 5 scopes by ms"

        // ── Readout (for a dev HUD — OwnerDevToolsOverlay) ─────────────────────
        /// <summary>The last emitted perf line (updated on every sample while enabled).</summary>
        public static string LastSummary { get; private set; } = "";
        /// <summary>The last sampled FPS (updated on every sample while enabled).</summary>
        public static float LastFps { get; private set; }
        /// <summary>The last emitted CPU/render/GPU split line (WO-1459). Empty until the first roll-up.</summary>
        public static string LastFrameSplit { get; private set; } = "";

        // ── Singleton / install ───────────────────────────────────────────────
        private static PerfReporter s_instance;
        private static bool s_installed;

        // ── Sampling state (frames-counted / elapsed for accurate FPS) ─────────
        private int   _frames;
        private float _lastSampleTime;

        // ── Frame-budget roll-up state (WO-1483) ──────────────────────────────
        private float _lastBudgetTime;
        private readonly System.Collections.Generic.List<FlowTrace.FrameSample> _budgetBuf =
            new System.Collections.Generic.List<FlowTrace.FrameSample>(32);
        private readonly System.Text.StringBuilder _budgetSb = new System.Text.StringBuilder(256);

        // ── Frame-split state (WO-1459) ───────────────────────────────────────
        // WHY THIS EXISTS. The 2026-09-10 Seeker capture measured town-idle at 22-23 fps /
        // 43.9 ms while ALL 20 FlowTrace.Measure scopes together accounted for a mean of
        // 57.9 ms per 1000 ms of wall clock — about 5.8%. So ~94% of every frame sits
        // OUTSIDE every instrumented scope, and the budget table above can only ELIMINATE
        // those 20 scopes, never name the cause. The one cheap measurement that splits the
        // remaining time is a CPU-main / render-thread / GPU breakdown, which is what this
        // block adds. It is INSTRUMENTATION, not a fix: nothing here changes a frame cost.
        //
        // HOW. UnityEngine.FrameTimingManager (UnityEngine.CoreModule, verified against
        // Unity 6000.4.8f1's own UnityEngine.CoreModule.xml — this project's editor version
        // per ProjectSettings/ProjectVersion.txt):
        //   CaptureFrameTimings()   snapshots the platform's timing data for later access
        //   GetLatestTimings(n,[])  fills the array, RETURNS HOW MANY it could actually get
        //   IsFeatureEnabled()      whether frame timing statistics are enabled
        // and the FrameTiming fields used below, each quoted from that same xml:
        //   cpuFrameTime                 total CPU frame time, end-of-frame to end-of-frame,
        //                                including all waiting time and overheads, in ms
        //   cpuMainThreadFrameTime       start of frame -> main thread finished the job, ms
        //   cpuRenderThreadFrameTime     start of render-thread work -> Present called, ms
        //   cpuMainThreadPresentWaitTime CPU time spent waiting for Present on the main
        //                                thread last frame, ms  (the "wait" term)
        //   gpuFrameTime                 GPU time for the frame, ms
        //   frameStartTimestamp          used HERE only as a frame IDENTITY, to dedupe
        //
        // DEDUPE. GetLatestTimings hands back the latest COMPLETED frame, which lands a few
        // frames behind. Two consecutive Update calls can therefore return the SAME frame.
        // Counting it twice would inflate n and skew the means, so a repeat frameStartTimestamp
        // is skipped and counted separately as a repeat. That is the difference between a mean
        // and a number that merely looks like one.
        //
        // AVAILABILITY. On a platform where the feature is off the manager returns ZERO
        // timings. Zeros would read as "the GPU is free", which is the exact misread that
        // sends a perf ticket down the wrong branch, so the line says unavailable instead —
        // and names IsFeatureEnabled so the reader can tell "feature off" from "on but the
        // platform reported nothing". Likewise a zero gpu sum over a window that DID collect
        // CPU samples prints n/a, not 0.0: GPU timers are graphics-API dependent.
        private float _lastSplitEmitTime;
        private readonly FrameTiming[] _splitBuf = new FrameTiming[1];
        private readonly System.Text.StringBuilder _splitSb = new System.Text.StringBuilder(256);
        private double _splitCpuFrameSum, _splitMainSum, _splitRenderSum, _splitGpuSum, _splitWaitSum;
        private double _splitMainMax, _splitRenderMax, _splitGpuMax;
        private int  _splitSamples;      // frames actually accumulated this window
        private int  _splitPolls;        // Update ticks that asked for a timing this window
        private int  _splitRepeats;      // polls that returned a frame already counted
        private bool _splitFeatureOn;    // last IsFeatureEnabled reading
        private ulong _splitLastFrameStamp;

        // =====================================================================
        //  Bootstrap
        // =====================================================================

        /// <summary>
        /// Self-install after the first scene loads. Creates ONE hidden, persistent
        /// host. Guarded — a diagnostic must never break startup.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Guard.Try("Perf", "install", () =>
            {
                if (s_installed) return;
                s_installed = true;

                var go = new GameObject("PerfReporter (auto)");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                s_instance = go.AddComponent<PerfReporter>();
            });
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            _lastSampleTime = Time.unscaledTime;
            _lastBudgetTime = Time.unscaledTime;
            _lastSplitEmitTime = Time.unscaledTime;
            _frames = 0;
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        // =====================================================================
        //  Per-frame — count frames, throttle to the sample cadence
        // =====================================================================

        private void Update()
        {
            // VOLUME CONTROL: do nothing (zero cost) unless tracing is on. This is the
            // same master switch WebTrace flips ON for ff.webtrace, and is ON by default
            // in the editor / a dev build.
            if (!FlowTrace.Enabled) return;

            _frames++;

            // WO-1459: ACCUMULATE ONLY. One CaptureFrameTimings + one GetLatestTimings per
            // frame is what makes the roll-up below a genuine interval MEAN rather than a
            // single instantaneous reading — the manager only snapshots when asked. Nothing
            // is logged from here: emitting per frame is the logcat flood §12 forbids.
            SampleFrameSplit();

            float now = Time.unscaledTime;

            // WO-1483: once a second, drain the frame-path Measure scopes and emit ONE
            // rolled-up line. Independent of the 4s perf sample above.
            if (now - _lastBudgetTime >= BudgetRollupInterval)
            {
                _lastBudgetTime = now;
                // WO-1459: the split rides the SAME cadence, one adjacent line. It is emitted
                // FIRST and unconditionally, because ReportFrameBudget early-outs when the
                // scope table is empty and the split must still be readable in that case —
                // an empty table is exactly the state that made the split necessary.
                ReportFrameSplit();
                ReportFrameBudget();
            }

            if (now - _lastSampleTime < SampleInterval) return;

            Sample(now);
        }

        // =====================================================================
        //  Frame budget roll-up (WO-1483) — the top scopes by accumulated ms
        // =====================================================================

        /// <summary>
        /// Drain <see cref="FlowTrace.SnapshotAndResetFrameSamples"/> and emit ONE
        /// <c>[Flow:Perf] frame budget: ...</c> line naming the top scopes by total ms
        /// over the last second. Silent when nothing was measured. Guarded — a diagnostic
        /// must never break a frame.
        /// </summary>
        private void ReportFrameBudget()
        {
            Guard.Try("Perf", "frame-budget", () =>
            {
                if (FlowTrace.SnapshotAndResetFrameSamples(_budgetBuf) <= 0) return;

                // Descending by total ms. Insertion sort — the list is ~10 entries and this
                // runs once a second, so it costs nothing and allocates nothing.
                for (int i = 1; i < _budgetBuf.Count; i++)
                {
                    var key = _budgetBuf[i];
                    int j = i - 1;
                    while (j >= 0 && _budgetBuf[j].SumMs < key.SumMs)
                    {
                        _budgetBuf[j + 1] = _budgetBuf[j];
                        j--;
                    }
                    _budgetBuf[j + 1] = key;
                }

                _budgetSb.Length = 0;
                _budgetSb.Append("frame budget: ");
                int n = _budgetBuf.Count < BudgetTopN ? _budgetBuf.Count : BudgetTopN;
                double total = 0.0;
                for (int i = 0; i < _budgetBuf.Count; i++) total += _budgetBuf[i].SumMs;

                for (int i = 0; i < n; i++)
                {
                    var s = _budgetBuf[i];
                    if (i > 0) _budgetSb.Append(", ");
                    _budgetSb.Append(s.What).Append('=')
                             .Append(s.SumMs.ToString("F1")).Append("ms/s")
                             .Append(" (x").Append(s.Count)
                             .Append(" worst ").Append(s.MaxMs.ToString("F1")).Append("ms)");
                }
                _budgetSb.Append(" | measured total=").Append(total.ToString("F1")).Append("ms/s")
                         .Append(" over ").Append(_budgetBuf.Count).Append(" scopes");

                FlowTrace.Step("Perf", _budgetSb.ToString());
            });
        }

        // =====================================================================
        //  Frame split (WO-1459) — CPU main thread vs render thread vs GPU
        // =====================================================================

        /// <summary>
        /// Per-frame ACCUMULATE step. Asks FrameTimingManager for the latest completed
        /// frame and folds it into the window sums. Emits NOTHING — the roll-up owns the
        /// one line. Guarded: a diagnostic must never break a frame.
        /// </summary>
        private void SampleFrameSplit()
        {
            Guard.Try("Perf", "frame-split-sample", () =>
            {
                _splitPolls++;
                _splitFeatureOn = FrameTimingManager.IsFeatureEnabled();

                FrameTimingManager.CaptureFrameTimings();
                uint got = FrameTimingManager.GetLatestTimings(1u, _splitBuf);
                if (got == 0u) return;   // unsupported / stats off -> _splitSamples stays 0

                var t = _splitBuf[0];

                // Frame IDENTITY, not a duration: the same completed frame is handed back
                // until a new one finishes, and double-counting it would fake the mean.
                // A stamp of 0 is treated as NO identity rather than as one shared identity:
                // whether every platform populates this field is not proven here, and if one
                // reports 0 the naive compare would call every frame after the first a repeat
                // and leave the window averaging a single frame while still printing n.
                ulong stamp = t.frameStartTimestamp;
                if (stamp != 0UL && stamp == _splitLastFrameStamp) { _splitRepeats++; return; }
                _splitLastFrameStamp = stamp;

                _splitCpuFrameSum += t.cpuFrameTime;
                _splitMainSum     += t.cpuMainThreadFrameTime;
                _splitRenderSum   += t.cpuRenderThreadFrameTime;
                _splitGpuSum      += t.gpuFrameTime;
                _splitWaitSum     += t.cpuMainThreadPresentWaitTime;

                if (t.cpuMainThreadFrameTime   > _splitMainMax)   _splitMainMax   = t.cpuMainThreadFrameTime;
                if (t.cpuRenderThreadFrameTime > _splitRenderMax) _splitRenderMax = t.cpuRenderThreadFrameTime;
                if (t.gpuFrameTime             > _splitGpuMax)    _splitGpuMax    = t.gpuFrameTime;

                _splitSamples++;
            });
        }

        /// <summary>
        /// Emit ONE <c>[Flow:Perf] frame split:</c> line per roll-up window, averaged over
        /// the frames actually collected, then reset the window. Prints an explicit
        /// unavailable line rather than zeros when the platform reported no timings.
        /// </summary>
        private void ReportFrameSplit()
        {
            Guard.Try("Perf", "frame-split", () =>
            {
                float splitNow = Time.unscaledTime;
                float window   = splitNow - _lastSplitEmitTime;
                _lastSplitEmitTime = splitNow;

                _splitSb.Length = 0;
                _splitSb.Append("frame split: ");

                if (_splitSamples <= 0)
                {
                    // NOT zeros. Zeros would read as "nothing costs anything", which is the
                    // misread that routes a perf ticket to the wrong thread.
                    string enabled = _splitFeatureOn ? "true" : "false";
                    _splitSb.Append("unavailable (FrameTimingManager returned 0 timings over ")
                            .Append(_splitPolls).Append(" polls, IsFeatureEnabled=").Append(enabled)
                            .Append("; this data requires the enableFrameTimingStats player ")
                            .Append("setting, which reads 0 in ProjectSettings)");
                }
                else
                {
                    double inv = 1.0 / _splitSamples;
                    double cpuFrame = _splitCpuFrameSum * inv;
                    double main     = _splitMainSum     * inv;
                    double render   = _splitRenderSum   * inv;
                    double gpu      = _splitGpuSum      * inv;
                    double wait     = _splitWaitSum     * inv;

                    _splitSb.Append("cpuFrame=").Append(cpuFrame.ToString("F1")).Append("ms ")
                            .Append("cpuMain=").Append(main.ToString("F1")).Append("ms ")
                            .Append("cpuRender=").Append(render.ToString("F1")).Append("ms ")
                            .Append("gpu=");

                    // A zero GPU sum across a window that DID collect CPU samples means the
                    // graphics API reported no GPU timer, not that the GPU was idle.
                    if (_splitGpuSum <= 0.0) _splitSb.Append("n/a");
                    else                     _splitSb.Append(gpu.ToString("F1")).Append("ms");

                    _splitSb.Append(" presentWait=").Append(wait.ToString("F1")).Append("ms")
                            .Append(" (worst main=").Append(_splitMainMax.ToString("F1"))
                            .Append(" render=").Append(_splitRenderMax.ToString("F1"))
                            .Append(" gpu=").Append(_splitGpuMax.ToString("F1"))
                            .Append("; n=").Append(_splitSamples)
                            .Append(" frames over ").Append(_splitPolls).Append(" polls, ")
                            .Append(_splitRepeats).Append(" repeats, window ")
                            .Append(window.ToString("F2")).Append("s)");
                }

                LastFrameSplit = _splitSb.ToString();

                // CADENCE IS ASYMMETRIC ON PURPOSE (§12 log volume).
                // AVAILABLE -> Step, once per roll-up window: those lines are the DATA, and
                // they sit next to the frame-budget line so the two read as one window.
                // UNAVAILABLE -> Once, keyed "Perf/frame-split-unavailable", so it prints on
                // the FIRST window of the play session and never again
                // (FlowTrace.Once dedupes on system + "/" + key against a HashSet,
                // FlowTrace.cs:225-237, cleared only by ResetSession at :240). The state is
                // static — a flag that is off stays off for the whole run — so repeating it
                // every second would add a line per second, and WebTrace a POST per second,
                // to say the same thing nothing can act on until a rebuild. The dev-HUD
                // readout above still refreshes every window either way.
                if (_splitSamples <= 0)
                    FlowTrace.Once("Perf", "frame-split-unavailable", LastFrameSplit);
                else
                    FlowTrace.Step("Perf", LastFrameSplit);

                _splitCpuFrameSum = 0.0; _splitMainSum = 0.0; _splitRenderSum = 0.0;
                _splitGpuSum = 0.0;      _splitWaitSum = 0.0;
                _splitMainMax = 0.0;     _splitRenderMax = 0.0; _splitGpuMax = 0.0;
                _splitSamples = 0;       _splitPolls = 0;       _splitRepeats = 0;
            });
        }

        // =====================================================================
        //  Sample — compute + report
        // =====================================================================

        private void Sample(float now)
        {
            Guard.Try("Perf", "sample", () =>
            {
                float elapsed = now - _lastSampleTime;
                // FPS = frames actually rendered over the interval (more accurate than a
                // single smoothed deltaTime), guarded against a zero interval.
                float fps = (elapsed > 0.0001f && _frames > 0) ? _frames / elapsed : 0f;
                float frameMs = fps > 0.0001f ? 1000f / fps : 0f;

                // Reset the counters for the next window.
                _frames = 0;
                _lastSampleTime = now;

                long memMB = SampleTotalMemoryMB();   // total allocated (native+managed); -1 if unavailable
                long gcMB  = SampleManagedMemoryMB();  // managed heap (GC); -1 if unavailable
                string scene = SafeSceneName();

                int towerCount, enemyCount;
                CountByTypeName(out towerCount, out enemyCount);

                // Update the dev-HUD readout on EVERY sample (regardless of the Step/Warn
                // branch below).
                LastFps = fps;
                LastSummary =
                    $"fps={fps:F0} ms={frameMs:F1} mem={memMB}MB gc={gcMB}MB scene={scene} " +
                    $"towers={towerCount} enemies={enemyCount}";

                // EMIT through FlowTrace -> WebTrace -> /api/trace. Low-FPS samples stand
                // out as a Warn so they surface in triage; healthy samples are a Step.
                if (fps > 0f && fps < LowFpsWarn)
                    FlowTrace.Warn("Perf", "LOW " + LastSummary);
                else
                    FlowTrace.Step("Perf", LastSummary);
            });
        }

        /// <summary>
        /// Stamp a perf snapshot at a moment of interest (e.g. "entered-dungeon",
        /// "placed-tower"). Emits event={label} + the last sampled summary through
        /// FlowTrace (which self-gates on Enabled, so this is a no-op when tracing is
        /// off). Safe to call from anywhere.
        /// </summary>
        public static void MarkEvent(string label)
        {
            FlowTrace.Step("Perf", $"event={label} " + LastSummary);
        }

        // =====================================================================
        //  Metric helpers — all guarded (a diagnostic must never throw)
        // =====================================================================

        /// <summary>Total allocated memory (native+managed) in MB, or -1 if unavailable.</summary>
        private static long SampleTotalMemoryMB()
        {
            return Guard.Try("Perf", "total-mem", () =>
            {
                long bytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                return bytes > 0 ? bytes / (1024 * 1024) : -1L;
            }, fallback: -1L);
        }

        /// <summary>Managed (GC) heap in MB, or -1 if unavailable.</summary>
        private static long SampleManagedMemoryMB()
        {
            return Guard.Try("Perf", "gc-mem", () =>
            {
                long bytes = System.GC.GetTotalMemory(false);
                return bytes > 0 ? bytes / (1024 * 1024) : -1L;
            }, fallback: -1L);
        }

        private static string SafeSceneName()
            => Guard.Try("Perf", "scene", () => SceneManager.GetActiveScene().name, fallback: "?");

        /// <summary>
        /// Count live Tower + Enemy instances WITHOUT referencing DeNelle.Village. We
        /// scan all active MonoBehaviours once and match GetType().Name — cheap enough at
        /// ~1/4s and needs no cross-assembly type reference. (Core does not reference
        /// Village; the concrete Tower/Enemy types are unavailable at compile time.)
        /// </summary>
        private static void CountByTypeName(out int towers, out int enemies)
        {
            int t = 0, e = 0;
            Guard.Try("Perf", "count-types", () =>
            {
                var all = Object.FindObjectsByType<MonoBehaviour>();
                for (int i = 0; i < all.Length; i++)
                {
                    var mb = all[i];
                    if (mb == null) continue;
                    string n = mb.GetType().Name;
                    if (n == "Tower") t++;
                    else if (n == "Enemy") e++;
                }
            });
            towers = t;
            enemies = e;
        }
    }
}
