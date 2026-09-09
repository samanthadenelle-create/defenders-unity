// =============================================================================
// DialogueRegression — headless oracle for the CUSTOM MVVM DIALOGUE spine
// (WO-455 rebuild, Yarn fully removed WO-557). The 6th SME regression path.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Contract/style mirrors the
// other Run(out reason) oracles wired into DataRegression.RunAll:
//   public static bool Run(out string reason)
//   markers: DIALOGUE_OK (Debug.Log) / DIALOGUE_FAIL (Debug.LogError → lands in
//   break-log.jsonl per docs/INSTRUMENTATION_STANDARD.md §4/§5).
//
// Data + LOGIC only — NO scene loads, NO play mode. "Real object in, real response
// out": the catalog parses through the REAL DialogueCatalog (CanonicalJson dual-copy
// path), the state machine runs through the REAL plain-C# DialogueRunner /
// DialogueViewModel, the re-entrancy P0 guard is exercised through the REAL
// DialogueService.Play, and every content condition key is fed to the REAL
// DeNelle.Village.DialogueCommandSink.Check.
//
// WHAT THIS SYSTEM ACTUALLY IS (verified from code, NOT the stale MASTER_CATALOG,
// which still documents the removed Yarn/ClassicRPG stack):
//   • Content = Resources/StreamingAssets Data/Canonical/dialogue/dialogues.json
//     → DialogueCatalog (DialogueDef→DialogueNode→lines/commands/options).
//   • Runtime = DialogueRunner (state machine) + DialogueViewModel (MVVM VM).
//   • Verbs = a switch in DeNelle.Village.DialogueCommandSink.Run (compiler-unique
//     case labels — a "double register" is now a COMPILE error, replacing the Yarn
//     source-generator's register-exactly-once rule). The live break surface is
//     therefore a content verb the sink does NOT route → silent Warn-default no-op.
//   • Conditions = DialogueCommandSink.Check (a prefix grammar).
//
// DELIBERATELY NOT COVERED HERE (needs play-mode / rendering — see notes):
//   • DialogueView panel geometry / FrameCore chrome / one-action-one-button chip
//     arbitration → the View is a MonoBehaviour uGUI skin; its layout is F8/visual.
//   • The per-node portrait SPRITE resolving from Resources — portraits import as
//     Texture2D (PortraitCache wraps them), so Resources.Load<Sprite> legitimately
//     returns null and the View falls back to a silhouette; asserting the load would
//     false-red. Speaker→card DATA integrity (name/affiliation/portrait-path present)
//     IS asserted below.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.Dialogue;

namespace DeNelle.Editor
{
    public static class DialogueRegression
    {
        private const string ContentRelPath = "Data/Canonical/dialogue/dialogues.json";
        // The verb registry lives as a switch in this file — scanned for `case "verb":`.
        private const string SinkRelPath = "/_Modules/Village/Tutorial/DialogueCommandSink.cs";

        // The vendor/station verbs the prompt mandates must all be routed by the sink.
        private static readonly string[] MandatedVendorVerbs =
            { "OpenShop", "OpenUpgrade", "OpenCraft", "OpenEquip", "OpenArena", "OpenRumorBoard" };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== DialogueRegression: custom MVVM dialogue spine (WO-455) ===");

            try
            {
                // 1. CATALOG PARSE through the REAL loader (fresh read via CanonicalJson).
                var dialogues = ParseCatalog(failures, log);
                if (dialogues == null) return Verdict(failures, log, out reason);

                // 2. Resources ↔ StreamingAssets dual-copy byte-equal (WebGL-safe contract).
                CheckDualCopy(ContentRelPath, failures, log);

                // 3. Catalog id / node integrity + referential integrity + reachability.
                CheckCatalogIntegrity(dialogues, failures, log);

                // 4. Speaker card-standard data integrity (name / affiliation / portrait path).
                CheckSpeakers(dialogues, failures, log);

                // 5. Every content VERB is routed by the sink switch (+ the 6 vendor verbs).
                CheckVerbRegistry(dialogues, failures, log);

                // 6. Every content CONDITION key is recognised by the sink grammar (+ no-throw
                //    through the REAL DialogueCommandSink.Check).
                CheckConditions(dialogues, failures, log);

                // 7. Runner state machine (REAL DialogueRunner) — lines→commands→options,
                //    condition gating, requires-filtering, Ended-fires-exactly-once.
                CheckRunnerStateMachine(failures, log);

                // 8. VM wiring (REAL DialogueViewModel) — Changed/Closed, Closed fires once.
                CheckViewModel(failures, log);

                // 9. Re-entrancy P0 guard (REAL DialogueService.Play) — a stale dialogue's
                //    Closed must NOT null the successor's ActiveVm (the frozen-build-mode root).
                CheckReentrancyGuard(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add($"DialogueRegression threw: {ex.GetType().Name}: {ex.Message}");
            }

            return Verdict(failures, log, out reason);
        }

        // =====================================================================
        //  1. CATALOG PARSE — real loader, non-empty, no throw.
        // =====================================================================
        private static IReadOnlyList<DialogueDef> ParseCatalog(List<string> failures, StringBuilder log)
        {
            IReadOnlyList<DialogueDef> dialogues;
            try
            {
                DialogueCatalog.Reload();                 // fresh read through CanonicalJson (WebGL path)
                dialogues = DialogueCatalog.Dialogues;
            }
            catch (Exception ex)
            {
                failures.Add($"DialogueCatalog.Reload/Dialogues threw: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            if (dialogues == null || dialogues.Count == 0)
            {
                failures.Add("dialogues.json deserialized to 0 DialogueDef (mapping break or unreadable content).");
                return null;
            }
            log.AppendLine($"  parsed {dialogues.Count} dialogue(s), {DialogueCatalog.Speakers.Count} speaker(s)");
            return dialogues;
        }

        // =====================================================================
        //  2. DUAL-COPY — Resources copy must stay byte-identical to StreamingAssets.
        // =====================================================================
        private static void CheckDualCopy(string relPath, List<string> failures, StringBuilder log)
        {
            string res = Application.dataPath + "/Resources/" + relPath;
            string sa  = Application.dataPath + "/StreamingAssets/" + relPath;
            bool hasRes = System.IO.File.Exists(res);
            bool hasSa  = System.IO.File.Exists(sa);
            if (!hasRes || !hasSa)
            {
                failures.Add($"dual-copy '{relPath}': missing {(hasRes ? "" : "Resources copy ")}{(hasSa ? "" : "StreamingAssets copy")}".Trim());
                return;
            }
            byte[] a = System.IO.File.ReadAllBytes(res);
            byte[] b = System.IO.File.ReadAllBytes(sa);
            bool equal = a.Length == b.Length;
            if (equal)
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) { equal = false; break; }
            if (!equal)
                failures.Add($"dual-copy '{relPath}': Resources and StreamingAssets copies DIVERGED " +
                             $"({a.Length} vs {b.Length} bytes) — editor and WebGL would load different dialogue.");
            else
                log.AppendLine($"  dual-copy '{relPath}' byte-identical ({a.Length} bytes) OK");
        }

        // =====================================================================
        //  3. CATALOG INTEGRITY — unique dialogue ids, per-dialogue unique node ids,
        //     entry node resolves, every goto/next resolves, no orphan nodes.
        // =====================================================================
        private static void CheckCatalogIntegrity(IReadOnlyList<DialogueDef> dialogues,
                                                  List<string> failures, StringBuilder log)
        {
            var seenIds = new HashSet<string>();
            foreach (var d in dialogues)
            {
                if (d == null || string.IsNullOrEmpty(d.Id))
                { failures.Add("dialogue with null/empty id."); continue; }
                if (!seenIds.Add(d.Id))
                    failures.Add($"duplicate dialogue id '{d.Id}' — DialogueCatalog.Find returns the FIRST; the later one is unreachable.");

                if (d.Nodes == null || d.Nodes.Count == 0)
                { failures.Add($"dialogue '{d.Id}' has no nodes."); continue; }

                // Unique node ids within this dialogue (FindNode returns the first).
                var nodeIds = new HashSet<string>();
                foreach (var n in d.Nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.Id))
                    { failures.Add($"dialogue '{d.Id}' has a node with null/empty id."); continue; }
                    if (!nodeIds.Add(n.Id))
                        failures.Add($"dialogue '{d.Id}' duplicate node id '{n.Id}' — FindNode returns the first; a goto/next to it is ambiguous.");
                }

                // Entry resolves (startNode present or Nodes[0]).
                if (!string.IsNullOrEmpty(d.StartNode) && d.FindNode(d.StartNode) == null)
                    failures.Add($"dialogue '{d.Id}' startNode '{d.StartNode}' does not resolve — EntryNode falls back to Nodes[0] silently.");
                if (d.EntryNode() == null)
                    failures.Add($"dialogue '{d.Id}' EntryNode() is null — the conversation cannot start.");

                // Per-node content + referential integrity.
                foreach (var n in d.Nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.Id)) continue;

                    // Lines: text must be non-empty (speaker MAY be empty = narration).
                    if (n.Lines != null)
                        for (int i = 0; i < n.Lines.Count; i++)
                            if (n.Lines[i] == null || string.IsNullOrWhiteSpace(n.Lines[i].Text))
                                failures.Add($"'{d.Id}'/{n.Id} line[{i}] has blank text — an empty dialogue bubble.");

                    // Options: text non-empty; non-empty goto (≠ "end") must resolve.
                    if (n.Options != null)
                    {
                        for (int i = 0; i < n.Options.Count; i++)
                        {
                            var o = n.Options[i];
                            if (o == null) { failures.Add($"'{d.Id}'/{n.Id} option[{i}] is null."); continue; }
                            if (string.IsNullOrWhiteSpace(o.Text))
                                failures.Add($"'{d.Id}'/{n.Id} option[{i}] has blank text.");
                            if (!IsEndTarget(o.Goto) && d.FindNode(o.Goto) == null)
                                failures.Add($"'{d.Id}'/{n.Id} option[{i}] goto '{o.Goto}' does not resolve — Choose would end the dialogue silently (typo?).");
                        }
                    }

                    // next: non-empty (≠ "end") must resolve, else the node ends early (typo).
                    if (!IsEndTarget(n.Next) && d.FindNode(n.Next) == null)
                        failures.Add($"'{d.Id}'/{n.Id} next '{n.Next}' does not resolve — the node would END instead of advancing (typo?).");
                }

                // Reachability: BFS from entry via option.goto + next; any unvisited node is
                // orphaned dead data (the classic result of a mistyped goto stranding a node).
                CheckReachability(d, failures);
            }
            log.AppendLine($"  catalog integrity: {dialogues.Count} dialogue(s), ids unique, gotos/next resolved, no orphan nodes");
        }

        private static bool IsEndTarget(string target) =>
            string.IsNullOrEmpty(target) || string.Equals(target, "end", StringComparison.OrdinalIgnoreCase);

        private static void CheckReachability(DialogueDef d, List<string> failures)
        {
            var entry = d.EntryNode();
            if (entry == null) return;
            var reachable = new HashSet<string>();
            var stack = new Stack<DialogueNode>();
            stack.Push(entry);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n == null || string.IsNullOrEmpty(n.Id) || !reachable.Add(n.Id)) continue;
                if (n.Options != null)
                    foreach (var o in n.Options)
                        if (o != null && !IsEndTarget(o.Goto))
                        { var t = d.FindNode(o.Goto); if (t != null) stack.Push(t); }
                if (!IsEndTarget(n.Next))
                { var t = d.FindNode(n.Next); if (t != null) stack.Push(t); }
            }
            foreach (var n in d.Nodes)
                if (n != null && !string.IsNullOrEmpty(n.Id) && !reachable.Contains(n.Id))
                    failures.Add($"'{d.Id}'/{n.Id} is UNREACHABLE from the entry node — orphan dead data (mistyped goto/next?).");
        }

        // =====================================================================
        //  4. SPEAKERS — every non-narration line speaker resolves to a speakers-block
        //     record (card standard: name + affiliation + portrait); block records well-formed.
        // =====================================================================
        private static void CheckSpeakers(IReadOnlyList<DialogueDef> dialogues,
                                          List<string> failures, StringBuilder log)
        {
            var speakers = DialogueCatalog.Speakers;
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in speakers)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Name))
                { failures.Add("speakers block has a record with no name."); continue; }
                declared.Add(s.Name);
                if (string.IsNullOrWhiteSpace(s.Affiliation))
                    failures.Add($"speaker '{s.Name}' has no affiliation — the card sub-line would be blank (card standard).");
                if (string.IsNullOrWhiteSpace(s.Portrait))
                    failures.Add($"speaker '{s.Name}' has no portrait path — falls to silhouette (card standard wants a portrait).");
            }

            var missing = new HashSet<string>();
            foreach (var d in dialogues)
            {
                if (d == null || d.Nodes == null) continue;
                foreach (var n in d.Nodes)
                {
                    if (n?.Lines == null) continue;
                    foreach (var l in n.Lines)
                    {
                        if (l == null || string.IsNullOrEmpty(l.Speaker)) continue;   // narration is legal
                        if (!declared.Contains(l.Speaker)) missing.Add(l.Speaker);
                    }
                }
            }
            foreach (var s in missing)
                failures.Add($"line speaker '{s}' is not in the speakers block — no affiliation/portrait card (falls to a nameless silhouette).");

            // FindSpeaker must resolve a declared speaker (the exact lookup the View uses).
            foreach (var s in speakers)
                if (s != null && !string.IsNullOrWhiteSpace(s.Name) && DialogueCatalog.FindSpeaker(s.Name) == null)
                    failures.Add($"DialogueCatalog.FindSpeaker('{s.Name}') returned null for a declared speaker — case/lookup break.");

            log.AppendLine($"  speakers: {declared.Count} declared, all line speakers carded");
        }

        // =====================================================================
        //  5. VERB REGISTRY — every verb the CONTENT fires must have a `case "verb":`
        //     in DialogueCommandSink.Run (else it hits the Warn-default = silent no-op),
        //     and the 6 mandated vendor verbs must all be routed. The switch is
        //     compiler-unique, so this replaces Yarn's register-exactly-once rule:
        //     the live break is a content verb with NO handler, not a double-register.
        // =====================================================================
        private static void CheckVerbRegistry(IReadOnlyList<DialogueDef> dialogues,
                                             List<string> failures, StringBuilder log)
        {
            string src;
            try { src = System.IO.File.ReadAllText(Application.dataPath + SinkRelPath); }
            catch (Exception ex)
            {
                failures.Add($"could not read DialogueCommandSink.cs ({SinkRelPath}) — verb registry unverifiable: {ex.Message}");
                return;
            }

            bool Routed(string verb) => src.Contains("case \"" + verb + "\"");

            // The 6 vendor/station verbs (prompt-mandated).
            foreach (var v in MandatedVendorVerbs)
                if (!Routed(v))
                    failures.Add($"MANDATED vendor verb '{v}' is NOT routed by DialogueCommandSink (no `case \"{v}\":`).");

            // Every verb the shipped content actually fires.
            var contentVerbs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in dialogues)
            {
                if (d?.Nodes == null) continue;
                foreach (var n in d.Nodes)
                {
                    if (n?.Commands == null) continue;
                    foreach (var c in n.Commands)
                        if (c != null && !string.IsNullOrEmpty(c.Verb)) contentVerbs.Add(c.Verb);
                }
            }
            foreach (var v in contentVerbs)
                if (!Routed(v))
                    failures.Add($"content verb '{v}' is fired by a dialogue but has no `case \"{v}\":` in DialogueCommandSink " +
                                 "— it silently hits the Warn-default (no-op). Route it or remove it from content.");

            log.AppendLine($"  verb registry: {MandatedVendorVerbs.Length} vendor + {contentVerbs.Count} content verb(s) all routed");
        }

        // =====================================================================
        //  6. CONDITIONS — every content condition key is recognised by the sink
        //     grammar (typo guard) AND survives the REAL Check without throwing.
        // =====================================================================
        private static void CheckConditions(IReadOnlyList<DialogueDef> dialogues,
                                           List<string> failures, StringBuilder log)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in dialogues)
            {
                if (d?.Nodes == null) continue;
                foreach (var n in d.Nodes)
                {
                    if (n == null) continue;
                    if (!string.IsNullOrEmpty(n.Condition)) keys.Add(n.Condition);
                    if (n.Options != null)
                        foreach (var o in n.Options)
                            if (o != null && !string.IsNullOrEmpty(o.Requires)) keys.Add(o.Requires);
                }
            }

            // REAL object: feed each key to the actual sink's condition evaluator.
            var sink = new DeNelle.Village.DialogueCommandSink();
            foreach (var key in keys)
            {
                if (!GrammarRecognises(key))
                    failures.Add($"condition key '{key}' is NOT recognised by the sink grammar — a content typo would silently " +
                                 "hide the option forever (unknown => false). If the sink gained a new condition family, extend " +
                                 "DialogueRegression.GrammarRecognises to match.");
                try { sink.Check(key); }
                catch (Exception ex)
                { failures.Add($"DialogueCommandSink.Check('{key}') THREW headless ({ex.GetType().Name}: {ex.Message}) — must degrade gracefully."); }
            }
            log.AppendLine($"  conditions: {keys.Count} content key(s) grammar-recognised + Check no-throw");
        }

        // Mirrors DeNelle.Village.DialogueCommandSink.Check's recognised grammar. Keep in
        // sync with that method (canon §15 — update in the same breath as the sink).
        private static bool GrammarRecognises(string c)
        {
            if (string.IsNullOrEmpty(c)) return true;               // Check returns true for empty
            if (c[0] == '!') return GrammarRecognises(c.Substring(1));
            if (c.StartsWith("quest_") && (c.EndsWith("_active") || c.EndsWith("_done"))) return true;
            if (c.StartsWith("keystone_count_min_")) return true;
            if (c.StartsWith("keystone_")) return true;
            if (c.StartsWith("pet_owned_")) return true;
            if (c.StartsWith("pet_grantable_")) return true;
            if (c == "pet_select_closed") return true;
            if (c == "jeweler_unlocked") return true;
            if (c == "onboarded") return true;
            return false;
        }

        // =====================================================================
        //  7. RUNNER STATE MACHINE — the REAL plain-C# DialogueRunner walked over a
        //     synthetic def: lines→commands→requires-filtered options→goto→end, a
        //     condition-gated entry that ends, and Ended firing EXACTLY ONCE (idempotent
        //     Stop — the runner-level analog of the Closed-guard invariant).
        // =====================================================================
        private static void CheckRunnerStateMachine(List<string> failures, StringBuilder log)
        {
            // (a) Full walk: 2 lines -> command node (v1) with 2 options, one gated hidden.
            var sink = new RecordingSink();
            var def = new DialogueDef
            {
                Id = "oracle_walk",
                StartNode = "start",
                Nodes = new List<DialogueNode>
                {
                    Node("start", lines: new[] { Line("Hero", "one"), Line("Hero", "two") }, next: "cmd"),
                    new DialogueNode
                    {
                        Id = "cmd",
                        Commands = new List<DialogueCommand> { Cmd("v1", "a0") },
                        Options = new List<DialogueOption>
                        {
                            Opt("go on", null, "branch"),
                            Opt("hidden path", "hidden", "branch2"),   // filtered by requires
                        },
                    },
                    Node("branch",  lines: new[] { Line(null, "branch line") }),
                    Node("branch2", lines: new[] { Line(null, "unreached") }),
                },
            };

            var runner = new DialogueRunner();
            DialogueLine lastLine = null;
            IReadOnlyList<DialogueOption> lastOptions = null;
            int ended = 0;
            runner.LineShown += l => lastLine = l;
            runner.OptionsShown += o => lastOptions = o;
            runner.Ended += () => ended++;

            runner.Begin(def, sink, sink);               // sink.Check("hidden") => false (not in TrueConditions)
            if (lastLine == null || lastLine.Text != "one") failures.Add("runner: Begin did not present the first line ('one').");
            if (!runner.IsRunning) failures.Add("runner: not IsRunning after Begin on a multi-line node.");

            runner.Advance();
            if (lastLine == null || lastLine.Text != "two") failures.Add("runner: Advance did not move to line 'two'.");

            runner.Advance();                            // past the line block -> PostLines
            if (sink.Verbs.Count != 1 || sink.Verbs[0] != "v1")
                failures.Add($"runner: command node did not fire exactly [v1] (got [{string.Join(",", sink.Verbs)}]).");
            if (lastOptions == null || lastOptions.Count != 1)
                failures.Add($"runner: requires-filter broke — expected 1 visible option (hidden filtered), got {(lastOptions == null ? "null" : lastOptions.Count.ToString())}.");

            runner.Choose(0);                            // -> branch
            if (lastLine == null || lastLine.Text != "branch line") failures.Add("runner: Choose(0) did not enter 'branch'.");

            runner.Advance();                            // branch has no next/options -> End
            if (ended != 1) failures.Add($"runner: Ended fired {ended} time(s), expected exactly 1 at natural end.");
            if (runner.IsRunning) failures.Add("runner: still IsRunning after the dialogue ended.");

            runner.Stop();                               // idempotent — must NOT re-fire Ended
            if (ended != 1) failures.Add($"runner: Stop() after end re-fired Ended (now {ended}) — idempotency broken (Closed-guard analog).");

            // (b) Command-only node ends synchronously inside Begin.
            var sink2 = new RecordingSink();
            var cmdOnly = new DialogueDef
            {
                Id = "oracle_cmdonly",
                Nodes = new List<DialogueNode>
                {
                    new DialogueNode { Id = "only", Commands = new List<DialogueCommand> { Cmd("x"), Cmd("y") } },
                },
            };
            var r2 = new DialogueRunner();
            int ended2 = 0; r2.Ended += () => ended2++;
            r2.Begin(cmdOnly, sink2, sink2);
            if (sink2.Verbs.Count != 2 || sink2.Verbs[0] != "x" || sink2.Verbs[1] != "y")
                failures.Add($"runner: command-only node did not fire [x,y] (got [{string.Join(",", sink2.Verbs)}]).");
            if (ended2 != 1) failures.Add($"runner: command-only node did not end synchronously (Ended={ended2}).");
            if (r2.IsRunning) failures.Add("runner: command-only node left the runner running.");

            // (c) Condition-gated entry with a false condition ends without a line.
            var gatedSink = new RecordingSink();   // Check(anything) => false
            var gated = new DialogueDef
            {
                Id = "oracle_gated",
                Nodes = new List<DialogueNode>
                {
                    new DialogueNode { Id = "gate", Condition = "blocked", Lines = new List<DialogueLine> { Line("X", "should not show") } },
                },
            };
            var r3 = new DialogueRunner();
            int ended3 = 0; DialogueLine gatedLine = null;
            r3.Ended += () => ended3++; r3.LineShown += l => gatedLine = l;
            r3.Begin(gated, gatedSink, gatedSink);
            if (gatedLine != null) failures.Add("runner: a false-condition entry node still presented a line.");
            if (ended3 != 1 || r3.IsRunning) failures.Add("runner: a false-condition entry node did not end cleanly.");

            log.AppendLine("  runner state machine: walk + requires-filter + command-only + condition-gate + Ended-once OK");
        }

        // =====================================================================
        //  8. VIEWMODEL — the REAL DialogueViewModel over a synthetic def: Changed
        //     fires on state changes, Closed fires EXACTLY ONCE, IsOpen transitions.
        // =====================================================================
        private static void CheckViewModel(List<string> failures, StringBuilder log)
        {
            var sink = new RecordingSink();
            var def = new DialogueDef
            {
                Id = "oracle_vm",
                Nodes = new List<DialogueNode>
                {
                    Node("a", lines: new[] { Line("Hero", "hello") }),   // one line then ends on Advance
                },
            };
            var vm = new DialogueViewModel();
            int changed = 0, closed = 0;
            vm.Changed += () => changed++;
            vm.Closed += () => closed++;

            vm.Begin(def, sink, sink);
            if (!vm.IsOpen) failures.Add("VM: not IsOpen after Begin.");
            if (changed < 1) failures.Add("VM: Changed did not fire for the first line.");
            if (vm.Text != "hello") failures.Add($"VM: Text is '{vm.Text}', expected 'hello'.");

            vm.Advance();                       // past the only line -> End
            if (vm.IsOpen) failures.Add("VM: still IsOpen after the last line advanced.");
            if (closed != 1) failures.Add($"VM: Closed fired {closed} time(s), expected exactly 1.");

            vm.Close();                          // idempotent — must not re-fire Closed
            if (closed != 1) failures.Add($"VM: Close() after end re-fired Closed (now {closed}) — idempotency broken.");

            log.AppendLine("  view-model: Changed/Closed wiring + Closed-once OK");
        }

        // =====================================================================
        //  9. RE-ENTRANCY GUARD — the REAL DialogueService.Play. When dialogue A is
        //     superseded by B, closing A must NOT null ActiveVm (which now points at B).
        //     This is the ReferenceEquals(ActiveVm, vm) per-VM guard — the P0 fix root
        //     of the frozen-build-mode bug. Uses two real, always-open content dialogues.
        // =====================================================================
        private static void CheckReentrancyGuard(List<string> failures, StringBuilder log)
        {
            var sink = new RecordingSink();
            DialogueService.RegisterSink(sink);
            DialogueService.RegisterConditions(sink);

            // Two shipped dialogues that stay OPEN after Begin (they present a line / options
            // and wait). 'farm' -> portrait cmd -> talk (2 lines). 'market' -> talk + options.
            if (DialogueCatalog.Find("farm") == null || DialogueCatalog.Find("market") == null)
            {
                log.AppendLine("  " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "re-entrancy check", "'farm'/'market' content ids absent (content changed)"));
                return;
            }

            if (!DialogueService.Play("farm")) { failures.Add("re-entrancy: Play('farm') returned false."); return; }
            var vmA = DialogueService.ActiveVm;
            if (vmA == null || !vmA.IsOpen) { failures.Add("re-entrancy: 'farm' did not stay open after Play."); CleanupActive(); return; }

            if (!DialogueService.Play("market")) { failures.Add("re-entrancy: Play('market') returned false."); CleanupActive(); return; }
            var vmB = DialogueService.ActiveVm;
            if (vmB == null || ReferenceEquals(vmA, vmB)) { failures.Add("re-entrancy: Play('market') did not replace ActiveVm."); CleanupActive(); return; }

            // Now close the SUPERSEDED A. Its Closed handler must see ActiveVm != vmA and skip.
            vmA.Close();
            if (!ReferenceEquals(DialogueService.ActiveVm, vmB))
                failures.Add("re-entrancy P0: closing the superseded dialogue NULLED/replaced ActiveVm — the successor VM is orphaned " +
                             "(the frozen-build-mode root; the ReferenceEquals per-VM guard regressed).");
            else if (!vmB.IsOpen)
                failures.Add("re-entrancy: the successor VM closed when the stale one did.");
            else
                log.AppendLine("  re-entrancy: stale Close ignored, successor ActiveVm survives OK");

            CleanupActive();   // close B so we don't leave a dangling open dialogue
        }

        private static void CleanupActive()
        {
            try { DialogueService.Stop(); } catch { /* best-effort teardown */ }
        }

        // ── Synthetic builders + a recording sink/condition source ───────────────
        private sealed class RecordingSink : IDialogueCommandSink, IDialogueConditionSource
        {
            public readonly List<string> Verbs = new List<string>();
            public readonly HashSet<string> TrueConditions = new HashSet<string>();
            public void Run(string verb, IReadOnlyList<string> args) => Verbs.Add(verb);
            public bool Check(string condition) => TrueConditions.Contains(condition);
        }

        private static DialogueLine Line(string speaker, string text) =>
            new DialogueLine { Speaker = speaker, Text = text };

        private static DialogueOption Opt(string text, string requires, string gotoNode) =>
            new DialogueOption { Text = text, Requires = requires, Goto = gotoNode };

        private static DialogueCommand Cmd(string verb, params string[] args) =>
            new DialogueCommand { Verb = verb, Args = new List<string>(args) };

        private static DialogueNode Node(string id, DialogueLine[] lines = null, string next = null)
        {
            var n = new DialogueNode { Id = id, Next = next };
            if (lines != null) n.Lines = new List<DialogueLine>(lines);
            return n;
        }

        // =====================================================================
        //  Verdict + markers
        // =====================================================================
        private static bool Verdict(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = "DIALOGUE OK — catalog parse/ids + dual-copy + node/goto/next integrity + reachability " +
                         "+ speaker cards + verb routing (vendor + content) + condition grammar + runner state machine " +
                         "+ VM Closed-once + re-entrancy P0 guard all hold";
                Debug.Log("DIALOGUE_OK\n" + log);
                return true;
            }
            reason = $"DIALOGUE: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"DIALOGUE_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}
