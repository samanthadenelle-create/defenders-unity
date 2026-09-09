// =============================================================================
// CombatCueAuthorityRegression [combat-cue-authority]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
//
// Pins the combat-cue fixes so none of them can silently rot
// back to the state the owner was playing at wave 5-6:
//
//   CASE 1 - EVERY ENEMY RESOLVES A NON-NULL TYPE VFX SET.
//     Enemy._typeVfxSet was NEVER populated: it appears in exactly one prefab
//     (Enemy_HollowWalker.prefab:123, value {fileID: 0}) and the sole
//     EnemyTypeVfxSet asset's GUID appears only in its own .meta. Every telegraph
//     / per-type sound / hit-VFX branch took its hardcoded fallback forever. The
//     fix resolves the set at RUNTIME from a Resources path
//     (EnemyTypeVfxLibrary), which has no serialized edge to lose. This case
//     drives the REAL resolver over EVERY def in the REAL enemies.json and fails
//     if any resolves null or to a zero-length telegraph - and fails if the
//     catalog yields zero defs, so it can never pass hollow. It also lints (on
//     comment- AND string-stripped source) that Enemy.cs still calls the resolver
//     from both Awake and Configure: deleting either call restores the defect
//     while leaving this file compiling.
//
//   CASE 2 - EXACTLY ONE AUTHORITY ADDS A WAVE-5 HEAVY.
//     waves.json wave 5 authors a 1050 HP Cave Troll; WaveCompositionBuilder
//     independently added an ELITE on every 5th wave. Both fired, so wave 5
//     fielded TWO heavies (and wave 20 stacked an elite on the apex dragon).
//     The authored wave now wins and the cadence defers. This case rebuilds the
//     REAL composition for every authored wave and asserts: an authored-heavy
//     wave carries NO elite slot; a cadence wave with no authored heavy still
//     carries exactly one (proving the deferral, not a broken pool, is what
//     removed it); and that at least one wave of each kind was examined.
//
//   CASE 3 - A BOSS SPAWN ID RESOLVES OR FAILS LOUDLY.
//     WaveManager hardcoded SpawnPoint = "spawn-0" while the only live producer
//     emits "spawn-castle-<dir>-<i>" (CastleSpawnPointInjector.cs:156), so the
//     lookup always missed and fell to the first element of an UNORDERED
//     FindObjectsByType list, warned only through Debug.LogWarning (invisible to
//     F8). This case exercises the real WaveSpawnResolver against hand-built
//     markers - preferred direction present, absent, and none at all - and asserts
//     the reason string and exactness flag that decide the caller's FlowTrace
//     severity. It also lints that WaveManager routes the boss through the
//     resolver and can still emit FlowTrace.Fail.
//
//   CASE 4 - ENEMY OVERHEAD CUES ARE TEXT-ONLY.
//     The encounter cue used a 78%-opaque Image behind the alert mark and enemy
//     name, producing a heavy shaded box over every enemy's head. The text remains,
//     parented directly to the world-space billboard Canvas, with no panel Image.
//
// SOURCE-LINT DISCIPLINE: every lint runs on source with comments AND string
// literals removed, so this suite can never be satisfied (or tripped) by prose or
// by a name that only appears inside a string.
//
// Markers: COMBAT_CUE_AUTHORITY_OK / COMBAT_CUE_AUTHORITY_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.CombatCueAuthorityRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class CombatCueAuthorityRegression
    {
        private const string EnemySourcePath =
            "Assets/_Modules/Village/Enemies/Enemy.cs";
        private const string WaveManagerSourcePath =
            "Assets/_Modules/Village/Waves/WaveManager.cs";
        private const string EncounterSpawnerSourcePath =
            "Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs";

        public static void RunAll()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log("COMBAT_CUE_AUTHORITY_OK\n" + reason);
            else    Debug.LogError("COMBAT_CUE_AUTHORITY_FAIL\n" + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes    = new List<string>();

            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-cue case 1",
                () => Case1_EveryEnemyResolvesTypeVfxSet(failures, notes));
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-cue case 2",
                () => Case2_OneHeavyAuthorityPerWave(failures, notes));
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-cue case 3",
                () => Case3_BossSpawnResolvesOrFailsLoudly(failures, notes));
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-cue case 4",
                () => Case4_EnemyOverheadCueIsTextOnly(failures, notes));

            if (failures.Count == 0)
            {
                reason = string.Join("; ", notes);
                return true;
            }

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" failure(s):");
            foreach (string f in failures) sb.Append("\n  - ").Append(f);
            if (notes.Count > 0) sb.Append("\n  (context: ").Append(string.Join("; ", notes)).Append(')');
            reason = sb.ToString();
            return false;
        }

        // =====================================================================
        //  CASE 1 - every enemy resolves a non-null EnemyTypeVfxSet
        // =====================================================================
        private static void Case1_EveryEnemyResolvesTypeVfxSet(List<string> failures, List<string> notes)
        {
            // --- 1a: the REAL catalog through the REAL resolver ---------------
            EnemyCatalog catalog = LoadEnemyCatalog(out string catalogError);
            if (catalog == null || catalog.Enemies == null || catalog.Enemies.Count == 0)
            {
                failures.Add("[case1] enemies.json yielded ZERO enemy defs (" +
                             (catalogError ?? "empty catalog") + "). This case cannot pass on an " +
                             "empty roster - a hollow pass here is how the missing type-VFX set " +
                             "survived in the first place.");
                return;
            }

            EnemyTypeVfxLibrary.ClearCache();   // never assert against a warm cache

            int checkedDefs = 0;
            var familiesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EnemyDef def in catalog.Enemies)
            {
                if (def == null) continue;
                checkedDefs++;
                familiesSeen.Add(string.IsNullOrEmpty(def.Family) ? "hollow" : def.Family);

                EnemyTypeVfxSet set = EnemyTypeVfxLibrary.Resolve(def);
                if (set == null)
                {
                    failures.Add("[case1] enemy '" + (def.Id ?? "<no-id>") + "' (family '" +
                                 (def.Family ?? "<none>") + "') resolved a NULL EnemyTypeVfxSet - " +
                                 "it would have no wind-up telegraph, no per-type sound and no hit VFX.");
                    continue;
                }
                if (!(set.TelegraphDuration > 0f))
                {
                    failures.Add("[case1] enemy '" + (def.Id ?? "<no-id>") + "' resolved a set whose " +
                                 "TelegraphDuration is " + set.TelegraphDuration.ToString("0.###") +
                                 " - a zero wind-up is the instant-hit defect DEF-48 closed.");
                }
            }

            if (checkedDefs == 0)
            {
                failures.Add("[case1] the catalog parsed but every entry was null - zero defs checked.");
                return;
            }
            notes.Add("case1: " + checkedDefs + " enemy def(s) across " + familiesSeen.Count +
                      " family(ies) all resolved a non-null set with a positive telegraph");

            // --- 1b: the call sites that make it durable ---------------------
            string enemySrc = ReadStripped(EnemySourcePath, out string enemyErr);
            if (enemySrc == null)
            {
                failures.Add("[case1] could not read " + EnemySourcePath + " for the call-site lint (" +
                             enemyErr + ")");
                return;
            }

            // Awake floor + Configure upgrade. Both are required: Awake alone leaves a
            // pooled/factory enemy on the default family, Configure alone leaves a
            // hand-placed enemy with no set at all.
            if (!enemySrc.Contains("EnsureTypeVfxSet(null)"))
                failures.Add("[case1] " + EnemySourcePath + " no longer calls EnsureTypeVfxSet with no " +
                             "def (the Awake floor). A hand-placed or test-spawned enemy would lose its " +
                             "telegraph again.");
            if (!enemySrc.Contains("EnsureTypeVfxSet(def)"))
                failures.Add("[case1] " + EnemySourcePath + " no longer calls EnsureTypeVfxSet from " +
                             "Configure (the per-family upgrade). Configure is the one place every spawn " +
                             "path sets the stat block.");
            if (!enemySrc.Contains("EnemyTypeVfxLibrary.Resolve"))
                failures.Add("[case1] " + EnemySourcePath + " no longer resolves through " +
                             "EnemyTypeVfxLibrary. A serialized-only reference is exactly the shape that " +
                             "silently un-assigned and cost every enemy its cues.");
            notes.Add("case1: Enemy.cs still resolves through EnemyTypeVfxLibrary from both Awake and Configure");
        }

        // =====================================================================
        //  CASE 2 - exactly one authority adds a wave-5 heavy
        // =====================================================================
        private static void Case2_OneHeavyAuthorityPerWave(List<string> failures, List<string> notes)
        {
            WaveSchedule schedule = LoadWaveSchedule(out string scheduleError);
            if (schedule == null || schedule.Waves == null || schedule.Waves.Count == 0)
            {
                failures.Add("[case2] waves.json yielded ZERO waves (" + (scheduleError ?? "empty schedule") +
                             "). This case cannot pass without a schedule to examine.");
                return;
            }

            EnemyCatalog catalog = LoadEnemyCatalog(out _);   // may be null - Build tolerates it

            int authoredHeavyWaves = 0, cadenceOnlyWaves = 0;
            foreach (WaveDef wave in schedule.Waves)
            {
                if (wave == null || wave.WaveId <= 0) continue;
                bool authored = !string.IsNullOrEmpty(wave.Boss) || wave.IsApexBossWave;

                EnemyWaveComposition comp = WaveCompositionBuilder.Build(wave.WaveId, authored, catalog);
                if (comp == null)
                {
                    failures.Add("[case2] wave " + wave.WaveId + " built a NULL composition.");
                    continue;
                }

                if (authored)
                {
                    authoredHeavyWaves++;
                    if (comp.HasElite)
                        failures.Add("[case2] wave " + wave.WaveId + " authors a heavy in waves.json (boss='" +
                                     (wave.Boss ?? "apex") + "') AND the generated composition still carries " +
                                     "an elite slot. That is two heavy authorities on one wave - the exact " +
                                     "double-boss the owner met at wave 5.");
                }
                else if (wave.WaveId % 5 == 0)
                {
                    cadenceOnlyWaves++;
                    if (!comp.HasElite)
                        failures.Add("[case2] wave " + wave.WaveId + " authors NO heavy, so the every-5th-wave " +
                                     "elite cadence is the only authority and must still field one - but the " +
                                     "composition carries no elite slot. The deferral has over-reached and " +
                                     "these waves lost their heavy entirely.");
                }
            }

            if (authoredHeavyWaves == 0)
                failures.Add("[case2] no wave in waves.json authors a boss/apexBoss, so the conflicting-" +
                             "authority assertion examined NOTHING. Either the schedule lost its bosses or " +
                             "this oracle is pointed at the wrong file.");
            if (cadenceOnlyWaves == 0)
                failures.Add("[case2] no cadence-only wave (waveId % 5 == 0 with no authored boss) was found, " +
                             "so the 'the cadence still works' half of this case examined NOTHING.");

            // The caller must keep TELLING the builder which authority owns the wave. The
            // parameter is required, so it cannot be dropped - but it can be hardcoded false.
            string wmSrc = ReadStripped(WaveManagerSourcePath, out string wmErr);
            if (wmSrc == null)
                failures.Add("[case2] could not read " + WaveManagerSourcePath + " (" + wmErr + ")");
            else if (!wmSrc.Contains("WaveCompositionBuilder.Build(waveId, WaveHasAuthoredHeavy(wave)"))
                failures.Add("[case2] WaveManager no longer passes WaveHasAuthoredHeavy(wave) to " +
                             "WaveCompositionBuilder.Build. A hardcoded false there restores the double heavy " +
                             "while everything still compiles.");

            notes.Add("case2: " + authoredHeavyWaves + " authored-heavy wave(s) examined for a duplicate " +
                      "elite; " + cadenceOnlyWaves + " cadence-only wave(s) checked to still field theirs");
        }

        // =====================================================================
        //  CASE 3 - a boss spawn id resolves, or fails loudly
        // =====================================================================
        private static void Case3_BossSpawnResolvesOrFailsLoudly(List<string> failures, List<string> notes)
        {
            var spawned = new List<GameObject>();
            try
            {
                // Ids/directions mirror CastleSpawnPointInjector.cs:156 verbatim
                // ("spawn-castle-{dir}-{i}" with dir in south/west/north/east).
                WaveSpawnPoint south = MakePoint(spawned, "spawn-castle-south-0", 2, "south");
                WaveSpawnPoint north = MakePoint(spawned, "spawn-castle-north-0", 0, "north");
                WaveSpawnPoint east  = MakePoint(spawned, "spawn-castle-east-0",  1, "east");

                // (a) preferred direction present -> exact hit on the north marker.
                var all = new List<WaveSpawnPoint> { south, east, north };
                WaveSpawnPoint pick = WaveSpawnResolver.ResolveBossSpawn(all, out string reasonA, out bool exactA);
                if (pick == null || pick != north || !exactA)
                    failures.Add("[case3] with a '" + WaveSpawnResolver.PreferredBossDirection +
                                 "' marker present the boss must resolve to it exactly, but got '" +
                                 (pick != null ? pick.SpawnId : "<null>") + "' exact=" + exactA +
                                 " (" + reasonA + ").");
                if (string.IsNullOrEmpty(reasonA))
                    failures.Add("[case3] the resolver returned an empty reason on the exact-hit path - the " +
                                 "caller cannot instrument what it is not told.");

                // (b) preferred direction ABSENT -> deterministic, and NOT reported as exact.
                var noNorth = new List<WaveSpawnPoint> { south, east };
                WaveSpawnPoint fb1 = WaveSpawnResolver.ResolveBossSpawn(noNorth, out string reasonB, out bool exactB);
                var reversed = new List<WaveSpawnPoint> { east, south };
                WaveSpawnPoint fb2 = WaveSpawnResolver.ResolveBossSpawn(reversed, out _, out _);
                if (fb1 == null || exactB)
                    failures.Add("[case3] with no '" + WaveSpawnResolver.PreferredBossDirection +
                                 "' marker the resolver must still return a point but must NOT claim an exact " +
                                 "match (got " + (fb1 != null ? fb1.SpawnId : "<null>") + ", exact=" + exactB +
                                 "). Silently claiming exactness is how a wrong-gate boss stayed invisible.");
                if (fb1 != fb2)
                    failures.Add("[case3] the fallback is NOT deterministic: the same markers in a different " +
                                 "order resolved '" + (fb1 != null ? fb1.SpawnId : "<null>") + "' vs '" +
                                 (fb2 != null ? fb2.SpawnId : "<null>") + "'. FindObjectsByType order is " +
                                 "arbitrary, which is the original defect.");
                if (string.IsNullOrEmpty(reasonB))
                    failures.Add("[case3] the resolver returned an empty reason on the fallback path.");

                // (c) NOTHING to resolve -> null plus a reason, so the caller can FlowTrace.Fail.
                WaveSpawnPoint none = WaveSpawnResolver.ResolveBossSpawn(
                    new List<WaveSpawnPoint>(), out string reasonC, out bool exactC);
                if (none != null || exactC || string.IsNullOrEmpty(reasonC))
                    failures.Add("[case3] with no markers at all the resolver must return null + a non-empty " +
                                 "reason (got point=" + (none != null) + ", exact=" + exactC + ", reason='" +
                                 (reasonC ?? string.Empty) + "').");

                notes.Add("case3: resolver exact-hits north, falls back deterministically, and reports " +
                          "null+reason on an empty scene");
            }
            finally
            {
                foreach (GameObject go in spawned)
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            // The caller must actually USE the resolver and must be able to fail loudly.
            string wmSrc = ReadStripped(WaveManagerSourcePath, out string wmErr);
            if (wmSrc == null)
            {
                failures.Add("[case3] could not read " + WaveManagerSourcePath + " (" + wmErr + ")");
                return;
            }
            if (!wmSrc.Contains("WaveSpawnResolver.ResolveBossSpawn("))
                failures.Add("[case3] WaveManager no longer routes the authored boss through " +
                             "WaveSpawnResolver.ResolveBossSpawn - a hardcoded spawn id that can never match " +
                             "would once again land the boss at an arbitrary gate.");
            if (!wmSrc.Contains("WaveSpawnResolver.FirstDeterministic("))
                failures.Add("[case3] WaveManager.FindSpawnPoint no longer takes its fallback from " +
                             "WaveSpawnResolver.FirstDeterministic - the fallback is arbitrary again.");
            if (!wmSrc.Contains("FlowTrace.Fail("))
                failures.Add("[case3] WaveManager contains no FlowTrace.Fail call. An unresolvable spawn id " +
                             "would report only through Debug.LogWarning, which the F8 harness never captures.");
            notes.Add("case3: WaveManager routes through the resolver and can still FlowTrace.Fail");
        }

        private static WaveSpawnPoint MakePoint(
            List<GameObject> owned, string id, int gateIndex, string direction)
        {
            var go = new GameObject("RegressionSpawn_" + id);
            go.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(go);
            var p = go.AddComponent<WaveSpawnPoint>();
            p.Configure(id, gateIndex, direction, Vector3.zero);
            return p;
        }

        // =====================================================================
        //  CASE 4 - overhead alert + enemy name remain, shaded panel does not
        // =====================================================================
        private static void Case4_EnemyOverheadCueIsTextOnly(List<string> failures, List<string> notes)
        {
            string src = ReadStripped(EncounterSpawnerSourcePath, out string err);
            if (src == null)
            {
                failures.Add("[case4] could not read " + EncounterSpawnerSourcePath + " (" + err + ")");
                return;
            }

            const string startToken = "private void RaiseThreatCue()";
            const string endToken = "private string FoeName()";
            int start = src.IndexOf(startToken, StringComparison.Ordinal);
            int end = start >= 0 ? src.IndexOf(endToken, start, StringComparison.Ordinal) : -1;
            if (start < 0 || end <= start)
            {
                failures.Add("[case4] could not isolate RaiseThreatCue in " + EncounterSpawnerSourcePath +
                             "; the oracle refuses a hollow pass.");
                return;
            }

            string cue = src.Substring(start, end - start);
            if (cue.Contains("AddCuePanel(") || cue.Contains("AddComponent<Image>("))
                failures.Add("[case4] RaiseThreatCue builds an Image-backed panel again. Enemy overhead " +
                             "cues must show the alert/name text with the world visible behind it.");

            int textCalls = CountOccurrences(cue, "AddCueText(");
            if (textCalls != 2 || !cue.Contains("FoeName()"))
                failures.Add("[case4] RaiseThreatCue must retain exactly two text elements (the alert mark " +
                             "and FoeName), but found " + textCalls + " AddCueText call(s), FoeName=" +
                             cue.Contains("FoeName()") + ".");

            if (!cue.Contains("RenderMode.WorldSpace") || !cue.Contains("AddComponent<DeNelle.Village.UI.Billboard>"))
                failures.Add("[case4] the text-only cue lost its world-space billboard behavior.");

            if (failures.Count == 0 || !failures.Exists(f => f.StartsWith("[case4]", StringComparison.Ordinal)))
                notes.Add("case4: enemy overhead cue retains alert + name as a world-space billboard with no Image panel");
        }

        private static int CountOccurrences(string value, string token)
        {
            int count = 0;
            for (int at = 0; (at = value.IndexOf(token, at, StringComparison.Ordinal)) >= 0; at += token.Length)
                count++;
            return count;
        }

        // =====================================================================
        //  Loading helpers - the SAME relative-path constants the runtime uses
        // =====================================================================
        private static EnemyCatalog LoadEnemyCatalog(out string error)
        {
            error = null;
            string json = ReadCanonical(WaveDataLoader.EnemiesRelativePath, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<EnemyCatalog>(json); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
        }

        private static WaveSchedule LoadWaveSchedule(out string error)
        {
            error = null;
            string json = ReadCanonical(WaveDataLoader.WavesRelativePath, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<WaveSchedule>(json); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
        }

        private static string ReadCanonical(string relativePath, out string error)
        {
            error = null;
            string json = null;
            try { json = CanonicalJson.Read(relativePath); }
            catch (Exception e) { error = "Resources read threw " + e.GetType().Name + ": " + e.Message; }

            if (!string.IsNullOrEmpty(json)) return json;

            string streaming = Path.Combine(Application.streamingAssetsPath, relativePath);
            try
            {
                if (File.Exists(streaming)) return File.ReadAllText(streaming);
                error = (error ?? string.Empty) + " StreamingAssets copy missing at '" + streaming + "'";
            }
            catch (Exception e)
            {
                error = (error ?? string.Empty) + " StreamingAssets read threw " + e.GetType().Name + ": " + e.Message;
            }
            return null;
        }

        // =====================================================================
        //  Source lint - comments AND string literals removed
        // =====================================================================
        /// <summary>
        /// Reads a .cs file with // and block comments AND the contents of string /
        /// char literals removed, so a lint can only match real CODE. Prose that
        /// describes a defect must never be able to satisfy the check that guards it.
        /// Returns null (with a reason) when the file cannot be read.
        /// </summary>
        private static string ReadStripped(string path, out string error)
        {
            error = null;
            string raw;
            try
            {
                if (!File.Exists(path)) { error = "file not found"; return null; }
                raw = File.ReadAllText(path);
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }

            var sb = new StringBuilder(raw.Length);
            bool inLine = false, inBlock = false, inStr = false, inChar = false, inVerbatim = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                char n = i + 1 < raw.Length ? raw[i + 1] : '\0';

                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock) { if (c == '*' && n == '/') { inBlock = false; i++; } else if (c == '\n') sb.Append(c); continue; }
                if (inVerbatim)
                {
                    if (c == '"' && n == '"') { i++; continue; }
                    if (c == '"') { inVerbatim = false; sb.Append('"'); }
                    else if (c == '\n') sb.Append(c);
                    continue;
                }
                if (inStr)
                {
                    if (c == '\\' && n != '\0') { i++; continue; }
                    if (c == '"') { inStr = false; sb.Append('"'); }
                    continue;
                }
                if (inChar)
                {
                    if (c == '\\' && n != '\0') { i++; continue; }
                    if (c == '\'') { inChar = false; sb.Append('\''); }
                    continue;
                }

                if (c == '/' && n == '/') { inLine = true; i++; continue; }
                if (c == '/' && n == '*') { inBlock = true; i++; continue; }
                if (c == '@' && n == '"') { inVerbatim = true; sb.Append("\""); i++; continue; }
                if (c == '$' && n == '"') { inStr = true; sb.Append('"'); i++; continue; }
                if (c == '"') { inStr = true; sb.Append('"'); continue; }
                if (c == '\'') { inChar = true; sb.Append('\''); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
