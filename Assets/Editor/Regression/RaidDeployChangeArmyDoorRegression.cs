// =============================================================================
// RaidDeployChangeArmyDoorRegression - WO-1871: the raid deploy screen must carry a
// door to the army, that door must come BACK, and no plural troop noun may be
// compiled into C# ever again.
// Markers: RAID_DEPLOY_CHANGE_ARMY_OK / RAID_DEPLOY_CHANGE_ARMY_FAIL.
// Tag: [raid-deploy-change-army]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
// Shape: public static bool Run(out string reason) - registered into
// DeNelle.Editor.DataRegression.RunAll with ONE line by the lead. NEVER throws.
//
// -----------------------------------------------------------------------------
//  WHY IT EXISTS - THE OWNER'S OWN WORDS, NOT AN INFERENCE
// -----------------------------------------------------------------------------
// Owner, 2026-09-18: "also there is no screen that you can access that allows you to
// change configuration of troops you want to use in raid."
//
// MEASURED, 2026-09-18 (WO-1871 "The measured gap"): the army surface EXISTS -
// ArmyMusterPanel has carried per-troop Manage (Move to Reserve / Return to army /
// Dismiss) and the Loadouts preset bank since WO-1811. What did not exist was a DOOR:
// neither RaidDeployScreen.cs nor RaidSelectionScreen.cs referenced ArmyMusterPanel at
// all; its only callers were ObsidianQueueHud, TroopTrainingPanel, ArmyComposition and
// ArmyMusterService/VM. A player standing at the deploy screen could not change who goes.
//
// Second ruling, same message thread: "that correct footman/footmen (or make generic
// troops) can be used across the board for plural troops" - the generic word "troops"
// IS the plural, everywhere. That ruling is what unblocks the one line the WO-1857
// localization sweep skipped (StarterArmyGrant.GrantToastFor).
//
// -----------------------------------------------------------------------------
//  WHY THIS SUITE IS A SOURCE-LINT AND NOT A LIVE BUILD
// -----------------------------------------------------------------------------
// Every element under test is inside a MonoBehaviour that builds a modal canvas, joins
// PanelManager and disposes a VM - RaidDeployScreen.OpenInternal needs a SceneConfigDef,
// a live canvas and a GameState, and ArmyMusterPanel.Show refuses outright without a
// built Barracks. Standing that up in edit-mode would be measuring a rig, not the game.
// The three facts this ticket can actually be wrong about are all STRUCTURAL - is the
// face wired, does the close signal exist and is it wired back, is a plural noun still
// compiled in - and a comment-stripped source lint judges exactly those.
//
// ⛔ COMMENT-STRIPPED, ALWAYS. All three files under test carry RCA prose that names
// the very symbols and words these cases search for (this ticket's own banners say
// "Footmen" and "ArmyMusterPanel.Show" in English sentences). A raw-text match would
// match the EXPLANATION instead of the code - the false-pass/false-fail trap
// RaidConfigIdResolveRegression documents at its own ReadSource. The stripper below is
// literal-aware, so a '//' inside a string cannot swallow the rest of a line.
//
// -----------------------------------------------------------------------------
//  THE RED-FIRST DISCRIMINATORS (the revert recipe, stated as behaviour)
// -----------------------------------------------------------------------------
//  (A) Delete the CHANGE ARMY face from RaidDeployScreen.BuildDeployBar - i.e. the
//      `_changeArmyBtn = ElarionUiKit.Button(footer, ChangeArmyFace(), ... OnChangeArmy)`
//      statement - and A1 reports "BuildDeployBar wires no OnChangeArmy handler". That
//      is the pre-WO-1871 state of this file, exactly.
//  (B) Change ArmyMusterPanel.Open's first teardown back to a raising close (the
//      pre-split `Close()`), and B4 reds: the Loadouts toggle would fire the return trip
//      on every surface swap. Delete the `Closed` event entirely and B1/B2/B5 red.
//  (C) Restore the Footman/Footmen + Archer/Archers ternaries in
//      StarterArmyGrant.GrantToastFor and C1/C2 red on the quoted literals.
//
// -----------------------------------------------------------------------------
//  WHAT THIS SUITE DOES NOT PROVE - an unproven thing named as unproven is useful, an
//  unproven thing stated as fact costs someone a day (CLAUDE.md section 11B)
// -----------------------------------------------------------------------------
//  * That the face RENDERS, fits its caption, or clears ElarionUiKit.MinTouchPx on a
//    real surface. Nothing in the repo measures deploy-footer face WIDTH (the existing
//    [deploy-bar-kit-button] lint measures the seating call, not the rect), and this
//    lane did not add such an oracle. WO-1871 acceptance item 2 - a headless or device
//    capture of the deploy screen - is where that is judged, by eye.
//  * That the round trip works at runtime. The close signal is pinned at BOTH ends by
//    source (B2 + B5); that the player actually lands back on the briefing with a fresh
//    count is the owner felt-verify in WO-1871 acceptance item 3.
//  * Anything about the raid gate. It is deliberately untouched (owner ruling
//    2026-09-16, full army); RaidEntryGate / RaidSelectionScreen remain its one
//    authority and no case below asks a readiness question.
//  * That the new locale keys resolve. They are authored in the WO-1871 sidecar and
//    merged into the catalogs by the lead; until that merge LocalText.Format answers
//    "[[missing:<key>]]" by design (LocalText.cs:157-162).
//
// NO REFLECTION: this suite reads files and nothing else.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1871: the Change Army door (Case A), the close signal it returns on (Case B),
    /// and the generic-plural ruling (Case C). DataRegression-shaped: true = pass with a
    /// one-line summary, false = fail with the offending detail. NEVER throws.
    /// </summary>
    public static class RaidDeployChangeArmyDoorRegression
    {
        private const string Tag = "[raid-deploy-change-army]";

        /// <summary>Relative to Application.dataPath.</summary>
        private const string DeployRel = "_Modules/Village/Hero/RaidDeployScreen.cs";
        private const string MusterRel = "_Modules/Village/Troops/ArmyMusterPanel.cs";
        private const string GrantRel = "_Modules/Village/Troops/StarterArmyGrant.cs";

        /// <summary>The ONE key the starter-squad toast is allowed to name. Authored in
        /// Logs/debug/scratch/sweep-sidecar-wo1871.json; merged into the catalogs by the lead.</summary>
        private const string StarterKey = "village.troops.starter_army.first_squad_ready_fmt";

        /// <summary>The door's caption key.</summary>
        private const string DoorKey = "village.troops.raid_deploy_screen.change_army";

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "RAID_DEPLOY_CHANGE_ARMY_FAIL " + Tag + " suite THREW: " +
                         ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            CaseA_DoorIsWired(failures, notes);
            CaseB_CloseSignalRoundTrip(failures, notes);
            CaseC_NoCompiledPluralNoun(failures, notes);

            if (failures.Count > 0)
            {
                var sb = new StringBuilder("RAID_DEPLOY_CHANGE_ARMY_FAIL ");
                for (int i = 0; i < failures.Count; i++)
                {
                    if (i > 0) sb.Append(" | ");
                    sb.Append(failures[i]);
                }
                reason = sb.ToString();
                return false;
            }

            var ok = new StringBuilder("RAID_DEPLOY_CHANGE_ARMY_OK ");
            for (int i = 0; i < notes.Count; i++)
            {
                if (i > 0) ok.Append("; ");
                ok.Append(notes[i]);
            }
            reason = ok.ToString();
            return true;
        }

        // =====================================================================
        //  CASE A - THE DEPLOY SCREEN WIRES A FACE WHOSE HANDLER OPENS THE MUSTER PANEL
        // =====================================================================
        /// <summary>
        /// The WO's acceptance shape: "the deploy screen source wires a face whose handler calls
        /// ArmyMusterPanel.Open (comment-stripped source pin)".
        ///
        /// <para>The handler calls <c>ArmyMusterPanel.Show</c>, which IS that entry: Show is the
        /// static door (ArmyMusterPanel.cs:380) that owns the singleton host, carries the
        /// Barracks-not-built spoken refusal, and then calls Open. Reaching Open directly would
        /// require making the private <c>s_host</c> public - a second way in, minus the refusal -
        /// so A3 pins Show and this paragraph records why the WO's wording resolves to it.</para>
        /// </summary>
        private static void CaseA_DoorIsWired(List<string> failures, List<string> notes)
        {
            int before = failures.Count;
            string text = ReadSource(DeployRel);
            if (text == null)
            {
                failures.Add(Tag + " A: could not read " + DeployRel);
                return;
            }

            // -- A1/A2: the FACE, inside the footer builder ------------------------------
            // The window is bounded exactly the way the two existing deploy-bar lints bound it,
            // so all three suites agree on what "the footer" is.
            string bar = Slice(text, "private void BuildDeployBar(",
                               "private static void SeatFooterCtaAtCanonicalHeight(", 8000);
            if (bar == null)
            {
                failures.Add(Tag + " A1: RaidDeployScreen.BuildDeployBar not found - the footer builder " +
                             "moved, so nothing below is judgeable. Fix the window first.");
                return;
            }

            if (bar.IndexOf("OnChangeArmy", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " A1: BuildDeployBar wires no OnChangeArmy handler - the deploy screen " +
                             "has no door to the army again. That is precisely the gap the owner reported " +
                             "on 2026-09-18 (\"there is no screen that you can access that allows you to " +
                             "change configuration of troops you want to use in raid\"): the muster panel " +
                             "existed, the door did not.");

            if (bar.IndexOf("ChangeArmyFace()", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " A2: the CHANGE ARMY face does not take its caption from " +
                             "ChangeArmyFace() - a hand-typed caption here is a hardcoded UI string, which " +
                             "the localization law forbids outright. Every new string on this lane is a key.");

            // -- A3: the caption resolves through the locale table ----------------------
            string face = Slice(text, "private static string ChangeArmyFace()", "private void OnChangeArmy(", 1200);
            if (face == null)
                failures.Add(Tag + " A3: RaidDeployScreen.ChangeArmyFace() not found - the caption's one " +
                             "resolution site is gone.");
            else if (face.IndexOf("\"" + DoorKey + "\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " A3: ChangeArmyFace() does not resolve the key \"" + DoorKey +
                             "\" - the sidecar authors that key and nothing else, so a renamed key here " +
                             "ships a face reading \"[[missing:...]]\" in all ten catalogs.");

            // -- A4/A5/A6: the handler ---------------------------------------------------
            string door = Slice(text, "private void OnChangeArmy()", "private void OnMusterClosed(", 4000);
            if (door == null)
            {
                failures.Add(Tag + " A4: RaidDeployScreen.OnChangeArmy not found - the face's handler is " +
                             "gone, so A1's wiring points at nothing.");
                return;
            }

            if (door.IndexOf("ArmyMusterPanel.Show(", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " A4: OnChangeArmy does not call ArmyMusterPanel.Show( - the WO's " +
                             "\"opens the existing ArmyMusterPanel\". Show is the static entry that owns " +
                             "the singleton host AND carries the Barracks-not-built spoken refusal; a door " +
                             "that reaches Open directly would need s_host made public and would drop that " +
                             "refusal on the floor.");

            int closeAt = door.IndexOf("Close();", StringComparison.Ordinal);
            int showAt = door.IndexOf("ArmyMusterPanel.Show(", StringComparison.Ordinal);
            if (closeAt < 0)
                failures.Add(Tag + " A5: OnChangeArmy does not Close() the deploy screen before opening " +
                             "the muster panel. This is NOT tidiness: the deploy canvas is sortingOrder " +
                             "31050 with an opaque 0.94-alpha backdrop and the muster canvas is 31000, both " +
                             "ScreenSpaceOverlay - so an un-closed deploy screen paints the muster panel " +
                             "entirely out of sight, and the player's tap looks dead. The close-to-nothing " +
                             "is also what ARMS the WO-1400 return door (PanelManager.cs:371-376).");
            else if (showAt >= 0 && closeAt > showAt)
                failures.Add(Tag + " A5: OnChangeArmy calls Close() AFTER ArmyMusterPanel.Show( - the order " +
                             "is the whole point (see A5's sorting note); closing second would tear down " +
                             "the wrong thing at the wrong time.");

            if (door.IndexOf("ArmyMusterPanel.Closed += OnMusterClosed", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " A6: OnChangeArmy does not subscribe to ArmyMusterPanel.Closed - the " +
                             "door would be one-way and the WO's return re-read could never fire.");

            if (door.IndexOf("FlowTrace.Step(\"RaidDeploy\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " A6: the door tap emits no FlowTrace.Step(\"RaidDeploy\", ...) - WO-1871 " +
                             "item 2 asks for it by name, and without it an F8 capture cannot tell a tap " +
                             "that was never received from a tap that was received and swallowed " +
                             "(CLAUDE.md section 12).");

            // -- A7: dimmed, never hidden (WO-1871 item 5) -------------------------------
            if (bar.IndexOf("_changeArmyBtn.interactable", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " A7: the CHANGE ARMY face's staging state is not carried by " +
                             "interactable. WO-1871 item 5: dimmed, never hidden. A control that vanishes " +
                             "reads as a bug, and the state must survive a greyscale read - the owner is " +
                             "red/green colourblind, so a hue can never be the channel.");
            if (bar.IndexOf("_changeArmyBtn.gameObject.SetActive(false)", StringComparison.Ordinal) >= 0 ||
                bar.IndexOf("_changeArmyBtn.gameObject.SetActive( false", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " A7: the CHANGE ARMY face is HIDDEN rather than dimmed - the one thing " +
                             "WO-1871 item 5 names as forbidden.");

            if (failures.Count == before)
                notes.Add("A deploy footer wires CHANGE ARMY (keyed caption) -> OnChangeArmy -> Close() " +
                          "then ArmyMusterPanel.Show(), traced, dimmed-not-hidden while staging");
        }

        // =====================================================================
        //  CASE B - THE CLOSE SIGNAL, AND THE RETURN RE-READ IT FEEDS
        // =====================================================================
        /// <summary>
        /// The WO's acceptance shape: "the muster panel's close path raises the event the raid
        /// screen re-reads from (pin the seam name)". The seam name is
        /// <c>ArmyMusterPanel.Closed</c>, and BOTH ends are pinned - a raised event nobody hears
        /// and a subscriber to an event nobody raises are the same broken door with two different
        /// green suites.
        ///
        /// <para>B4 is the case that would have caught the subtle one: ArmyMusterPanel.Open tears
        /// down before it rebuilds, and OnToggleLoadouts rebuilds by calling Open. If the rebuild
        /// teardown raised, tapping Loadouts would fire the deploy screen's return trip onto a
        /// panel that is mid-rebuild.</para>
        /// </summary>
        private static void CaseB_CloseSignalRoundTrip(List<string> failures, List<string> notes)
        {
            int before = failures.Count;
            string muster = ReadSource(MusterRel);
            if (muster == null)
            {
                failures.Add(Tag + " B: could not read " + MusterRel);
                return;
            }

            // -- B1: the seam exists, by name and shape ---------------------------------
            if (muster.IndexOf("public static event System.Action<bool> Closed", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " B1: ArmyMusterPanel declares no `public static event " +
                             "System.Action<bool> Closed` - the seam the deploy screen returns on. The bool " +
                             "is load-bearing (it is the hand-off flag, see B3); a parameterless event " +
                             "cannot tell an ordinary close from the panel routing the player onward.");

            // -- B2: something actually raises it ---------------------------------------
            if (muster.IndexOf("Closed?.Invoke(", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " B2: nothing in ArmyMusterPanel raises Closed - the event is declared " +
                             "and dead, which is worse than absent because both ends look wired.");

            // -- B3: the ordinary close raises; the GO hand-off raises with the flag ----
            string close = Slice(muster, "public void Close()", "private void CloseCore(", 600);
            if (close == null || close.IndexOf("CloseCore(raiseClosed: true", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " B3: ArmyMusterPanel.Close() does not route to " +
                             "CloseCore(raiseClosed: true, ...) - the shared kit Close and the scrim tap are " +
                             "the player's normal way back, and they must signal or the deploy screen never " +
                             "re-reads.");

            string go = Slice(muster, "private void OnGoRaid()", "private void BuildTroopLadder(", 900);
            if (go == null)
                failures.Add(Tag + " B3: ArmyMusterPanel.OnGoRaid not found - the hand-off path cannot be " +
                             "judged.");
            else if (go.IndexOf("handedOff: true", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " B3: OnGoRaid does not close with handedOff: true. Its GO face opens " +
                             "RaidSelectionScreen itself, so a plain return signal there would re-open the " +
                             "deploy screen (canvas 31050, opaque) on top of the selection grid (31000) the " +
                             "player just chose - the deploy screen would appear to have hijacked the tap.");

            // -- B4: the REBUILD must not raise -----------------------------------------
            string open = Slice(muster, "public void Open()", "_ui = ElarionUiKit.BuildModalCanvas(", 1200);
            if (open == null)
                failures.Add(Tag + " B4: ArmyMusterPanel.Open's head could not be sliced - the rebuild " +
                             "teardown cannot be judged.");
            else if (open.IndexOf("CloseCore(raiseClosed: false", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " B4: ArmyMusterPanel.Open does not tear down with " +
                             "CloseCore(raiseClosed: false, ...). Open() closes first, and OnToggleLoadouts " +
                             "rebuilds the surface by calling Open() - so a raising teardown here fires the " +
                             "deploy screen's return trip EVERY TIME the player taps Loadouts, re-opening a " +
                             "briefing over a panel that is mid-rebuild. A rebuild is not a close.");

            // -- B5: the other end - the deploy screen re-reads on the signal -----------
            string deploy = ReadSource(DeployRel);
            if (deploy == null)
            {
                failures.Add(Tag + " B5: could not read " + DeployRel + " to pin the listening end.");
                return;
            }

            string handler = Slice(deploy, "private void OnMusterClosed(bool handedOff)",
                                   "private void OpenTroopsDoor(", 4000);
            if (handler == null)
                failures.Add(Tag + " B5: RaidDeployScreen.OnMusterClosed(bool) not found - nothing listens " +
                             "to the seam, so B1-B4 prove a signal into the void.");
            else
            {
                if (handler.IndexOf("ArmyMusterPanel.Closed -= OnMusterClosed", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " B5: OnMusterClosed does not unsubscribe itself. A static event plus " +
                                 "a surviving MonoBehaviour is a leak with a user-visible symptom: some later " +
                                 "muster close re-opens a briefing for a raid the player left long ago.");
                if (handler.IndexOf("OpenInternal(_def)", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " B5: OnMusterClosed does not re-open the briefing via " +
                                 "OpenInternal(_def) - the RETURN RE-READ is the half of WO-1871 item 1 that " +
                                 "makes the door useful. OpenInternal takes a fresh ArmyReadiness.Compute " +
                                 "snapshot at its top, so re-opening IS the re-read; there is deliberately no " +
                                 "second readiness call and no cached count to invalidate.");
                if (handler.IndexOf("deployableSlots=", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " B5: the return trip emits no FlowTrace naming the counts. WO-1871 " +
                                 "item 2 asks for a Step on the return re-read naming them, and without it a " +
                                 "capture cannot show the summary actually changed.");
                if (handler.IndexOf("handedOff", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " B5: OnMusterClosed ignores the handedOff flag - it would re-open " +
                                 "the deploy screen over the raid selection grid (see B3).");
            }

            if (failures.Count == before)
                notes.Add("B ArmyMusterPanel.Closed(handedOff) raised on the real close and on the GO " +
                          "hand-off, never on a rebuild; RaidDeployScreen re-opens + re-reads on it and " +
                          "unsubscribes");
        }

        // =====================================================================
        //  CASE C - NO PER-UNIT PLURAL NOUN IS COMPILED INTO C#
        // =====================================================================
        /// <summary>
        /// Owner ruling 2026-09-18: "that correct footman/footmen (or make generic troops) can be
        /// used across the board for plural troops" - the generic word is the plural, everywhere.
        ///
        /// <para>C1 is deliberately a QUOTED-LITERAL search, not a word search: the file legitimately
        /// carries the identifiers <c>grantedFootmen</c> / <c>grantedArchers</c> and the troop ids
        /// <c>"troop-footman"</c> / <c>"troop-archer"</c> (lower case, which is why they do not
        /// match). A bare word search would red on working code and teach the next seat to rename
        /// variables to appease a suite.</para>
        ///
        /// <para>C2 is the stronger half and is revert-proof: inside GrantToastFor, the ONLY string
        /// literal allowed is the locale key. That kills a re-add in any spelling - " Footmen",
        /// "Footmen and", a concatenated fragment - without enumerating spellings.</para>
        /// </summary>
        private static void CaseC_NoCompiledPluralNoun(List<string> failures, List<string> notes)
        {
            int before = failures.Count;
            string text = ReadSource(GrantRel);
            if (text == null)
            {
                failures.Add(Tag + " C: could not read " + GrantRel);
                return;
            }

            // -- C1: the exact literals the retired ternaries used -----------------------
            string[] retired = { "\"Footman\"", "\"Footmen\"", "\"Archer\"", "\"Archers\"" };
            foreach (var lit in retired)
            {
                if (text.IndexOf(lit, StringComparison.Ordinal) >= 0)
                    failures.Add(Tag + " C1: StarterArmyGrant.cs still compiles the literal " + lit +
                                 " . The owner ruled on 2026-09-18 that the generic word \"troops\" is the " +
                                 "plural across the board; a per-unit plural form in C# is English " +
                                 "morphology that no other catalog can follow (Russian alone has three " +
                                 "plural categories), and it is the exact line the WO-1857 sweep skipped.");
            }

            // -- C2: the toast says ONE thing, and it is a key ---------------------------
            string body = Slice(text, "public static string GrantToastFor(int footmen, int archers)",
                                "private const float PollInterval", 4000);
            if (body == null)
            {
                failures.Add(Tag + " C2: StarterArmyGrant.GrantToastFor(int, int) not found. The SIGNATURE " +
                             "is deliberately unchanged by WO-1871 - SplitComposition's 10 -> 5/5 contract " +
                             "and StarterArmyGrantRegression's composition cases read both arguments; only " +
                             "the NARRATION went generic.");
                return;
            }

            if (body.IndexOf("LocalText.Format(", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " C2: GrantToastFor does not build its sentence through " +
                             "LocalText.Format - the FTUE toast would be an untranslated hardcoded string " +
                             "on the very first screen a new player is shown.");

            var literals = StringLiterals(body);
            foreach (var lit in literals)
            {
                if (string.Equals(lit, StarterKey, StringComparison.Ordinal)) continue;
                failures.Add(Tag + " C2: GrantToastFor contains the string literal \"" + lit + "\". The ONLY " +
                             "literal this method may hold is the locale key \"" + StarterKey + "\"; " +
                             "everything else is copy, and copy lives in the catalogs. This is the " +
                             "spelling-proof half of the ruling - it reds on \" Footmen\" and on any other " +
                             "re-add without this suite having to enumerate the words.");
            }

            if (body.IndexOf("\"" + StarterKey + "\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " C2: GrantToastFor does not name the key \"" + StarterKey + "\" - that " +
                             "is the key the WO-1871 sidecar authors, and a different one ships " +
                             "\"[[missing:...]]\" as the first sentence of the game.");

            if (failures.Count == before)
                notes.Add("C no Footman/Footmen/Archer/Archers literal survives; GrantToastFor holds exactly " +
                          "one string, the key");
        }

        // =====================================================================
        //  SOURCE READING - comments stripped, string literals preserved
        // =====================================================================
        /// <summary>
        /// Reads one source file as CODE. All three files under test carry RCA prose naming the
        /// very symbols and words the cases search for, so a raw-text match would match the
        /// explanation instead of the code (the trap RaidConfigIdResolveRegression documents at its
        /// own ReadSource). Literal-aware, so a '//' inside a string cannot swallow a line - the
        /// same failure mode CLAUDE.md section 1 documents for the brace gate.
        /// </summary>
        private static string ReadSource(string relative)
        {
            string path = Path.Combine(UnityEngine.Application.dataPath,
                relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? StripComments(File.ReadAllText(path)) : null;
        }

        /// <summary>One bounded window of a source file, from <paramref name="startNeedle"/> to
        /// <paramref name="endNeedle"/> (or <paramref name="maxLen"/> characters when the end
        /// needle has moved). Null when the start needle is absent - every caller treats that as
        /// its own failure rather than silently judging an empty string.</summary>
        private static string Slice(string src, string startNeedle, string endNeedle, int maxLen)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int start = src.IndexOf(startNeedle, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = string.IsNullOrEmpty(endNeedle)
                ? -1
                : src.IndexOf(endNeedle, start + startNeedle.Length, StringComparison.Ordinal);
            if (end < 0) end = Math.Min(start + maxLen, src.Length);
            return src.Substring(start, end - start);
        }

        /// <summary>Every double-quoted literal in a comment-stripped span, unescaped only enough
        /// to compare. Verbatim (@"...") spans are handled by the stripper, which leaves them
        /// quoted, so they land here too.</summary>
        private static List<string> StringLiterals(string src)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(src)) return found;
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] != '"') continue;
                var sb = new StringBuilder();
                i++;
                while (i < src.Length && src[i] != '"')
                {
                    if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(src[i + 1]); i += 2; continue; }
                    if (src[i] == '\n') break;
                    sb.Append(src[i]);
                    i++;
                }
                found.Add(sb.ToString());
            }
            return found;
        }

        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);

            for (int i = 0; i < src.Length; i++)
            {
                char ch = src[i];

                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }

                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    continue;
                }

                if (ch == '@' && i + 1 < src.Length && src[i + 1] == '"')
                {
                    sb.Append(src[i]).Append(src[i + 1]);
                    i += 2;
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '"') { sb.Append('"').Append('"'); i += 2; continue; }
                            sb.Append('"');
                            break;
                        }
                        sb.Append(src[i]);
                        i++;
                    }
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    char quote = ch;
                    sb.Append(ch);
                    i++;
                    while (i < src.Length && src[i] != quote)
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(src[i]).Append(src[i + 1]); i += 2; continue; }
                        if (src[i] == '\n') break;
                        sb.Append(src[i]);
                        i++;
                    }
                    if (i < src.Length) sb.Append(src[i]);
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }
    }
}
