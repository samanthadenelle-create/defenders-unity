using System;
using System.Collections.Generic;
using System.IO;

namespace DeNelle.Editor.Regression
{
    /// <summary>Source oracle for the approved 2026-08-31 four-card Manage launcher.</summary>
    public static class ManageApprovedLauncherRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            const string path = "Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs";
            string panel = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

            // ⚠ PIN MOVED 2026-09-06 (WO-1443), WITH THE RULING. This suite pinned the 2026-08-31
            // APPROVED FOUR-CARD launcher: its order, and its copy. The owner has since drawn the
            // screen herself - docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png panel 1 - and it is
            // THREE cards (BUILD / ARMY / RESEARCH) with her own one-line descriptions on them.
            // CAPTURE_LOOP_GOAL.md 3.0c: where a text ruling and the mockup disagree, the mockup
            // wins, because it is the picture of the thing she wants and the ruling is a sentence
            // about it. So the approved copy MOVED to hers; it was not dropped.
            //   "Town structures & upgrades"  -> "Construct and upgrade your town"
            //   "Train and improve your army" -> "Train and manage your troops"
            //   "Discover realm advancements" -> "Unlock powerful advancements"
            // Defense's line survives because PurposeFor still answers for that tab elsewhere.
            // Everything else this suite defends - the lock feedback, the layered card art, the
            // rapid-tap guard, the shared shell - is untouched and still pinned below.
            foreach (string copy in new[] { "Towers, walls & gates",
                         "Construct and upgrade your town", "Build a Barracks to unlock",
                         "Unlock powerful advancements", "Train and manage your troops" })
                if (!panel.Contains(copy)) failures.Add("missing approved copy: " + copy);

            // ⛔ "Choose a path" IS RETIRED, and this case now FORBIDS it rather than requiring it.
            // Mockup panel 1 carries the title MANAGE and nothing else above the cards; the three
            // cards and their descriptions already say what the choice is. It is the same class of
            // line the owner had struck from every other Manage screen on 2026-09-06 ("remove the
            // manage army and sub line"), and it only survived here because the hub had not been
            // rendered since WO-2001 deleted ShowLauncher.
            // The pin flipped direction rather than being deleted: an approved string that is no
            // longer approved needs a guard against its RETURN, not the absence of a guard.
            if (panel.Contains("\"Choose a path\""))
                failures.Add("the retired 'Choose a path' heading is rendered again - mockup panel 1 " +
                             "has the title and the three cards, and nothing between them");

            if (!panel.Contains("ManageTab.Buildings, ManageTab.Troops, ManageTab.Research"))
                failures.Add("hub card order drifted - the mockup's panel 1 is BUILD / ARMY / RESEARCH");
            if (panel.Contains("ManageTab.Defense, ManageTab.Buildings, ManageTab.Troops, ManageTab.Research"))
                failures.Add("the hub is back to FOUR cards - Defense and Buildings are ONE destination " +
                             "since WO-2001 and a Defense card opens the Build tab");
            // The player-facing words are the mockup's, not the internal tab labels.
            if (!panel.Contains("case ManageTab.Troops: return \"ARMY\";") ||
                !panel.Contains("private static string HubTitleFor(ManageTab tab)"))
                failures.Add("the hub cards no longer carry the mockup's own words (BUILD / ARMY / " +
                             "RESEARCH). TabLabels reads 'Buildings' and 'Troops', which the player " +
                             "meets nowhere else on this screen");
            if (panel.Contains("QueueBadgePlate_") || panel.Contains("0/5 queued\";"))
                failures.Add("launcher reintroduced non-actionable queue-depth clutter");
            if (!panel.Contains("MedievalUiSkin.ApplyShell(chrome)"))
                failures.Add("Manage no longer consumes the shared medieval shell/medallion contract");
            if (!panel.Contains("BarracksUnlock.IsUnlocked") || !panel.Contains("BuildLockBadge") ||
                !panel.Contains("UI/ElarionMedieval/badges/lock-badge"))
                failures.Add("Troops lock is not worded + visual + source-authoritative");
            foreach (string card in new[] { "cards/defense", "cards/buildings", "cards/troops-locked", "cards/research" })
                if (!panel.Contains(card)) failures.Add("approved layered card art missing: " + card);
            // WO-1406 / WO-1418 (owner ruling 2026-09-05, BATCH_STATE PART 8): the locked Troops card is a DOOR,
            // not a toast - its face is a build CTA and the tap enters Town build mode. The retired toast
            // literal "Build a Barracks to unlock Troops." must NOT return; the purpose line keeps the sentence.
            // ⚠ PIN RE-POINTED 2026-09-10, WITH A RULING - the face copy MOVED, the door did not.
            // OWNER, verbatim: "Manage ARMY copy: BUILD BARRACKS, keep the cook fire".
            // WHY: WO-1636's glyph oracle measured that face drawing 11 of 14 printable glyphs at BOTH
            // captured aspects (ManageWorkspace_2340x1080 and _2670x1200) - the player read "BUILD A BARRA...".
            // The band was already widened to the gold perimeter and the cell is height-clamped by
            // HubCardAspect, so THIS pin was the last lever and it only moves on the owner's word. It has now
            // moved with it: the article is dropped and the door is unchanged.
            // ⛔ AND THE PIN IS ON THE CODE SHAPE, NOT ON THE BARE WORDS. `ManageScreenPanel.cs` carries both
            // "BUILD A BARRACKS" and "BUILD BARRACKS" inside its own WO-1636 reasoning COMMENTS, so a
            // panel.Contains("BUILD BARRACKS") would have passed before the edit and would keep passing after
            // a revert - a pin that cannot fail. `"BUILD BARRACKS" : title` is the assignment itself.
            if (panel.Contains("Build a Barracks to unlock Troops."))
                failures.Add("locked-card tap shows the retired toast instead of the BUILD BARRACKS door (WO-1406)");
            if (!panel.Contains("\"BUILD BARRACKS\" : title") || !panel.Contains("BarracksUnlock.IsUnlocked"))
                failures.Add("locked-card tap has no door: the faceText assignment must read \"BUILD BARRACKS\" : title " +
                             "(owner ruling 2026-09-10) plus a BarracksUnlock.IsUnlocked refusal (WO-1406 / WO-1636)");
            if (panel.Contains("\"BUILD A BARRACKS\" : title"))
                failures.Add("the ARMY face is back to the article form, which the glyph oracle measured at 11 of 14 " +
                             "glyphs on both captured aspects (owner ruling 2026-09-10: \"BUILD BARRACKS\")");
            // WO-1418: the strip chips reuse the launcher door with commitLauncherNavigation:false, so the one-shot
            // latch guards CARD taps only; the guard is now conditional on that flag.
            if (!panel.Contains("_categoryNavigationCommitted") ||
                !panel.Contains("if (commitLauncherNavigation && _categoryNavigationCommitted) return"))
                failures.Add("rapid category taps are not guarded");
            if (!panel.Contains("card.transition = Selectable.Transition.ColorTint"))
                failures.Add("pressed/focused visual state is absent");
            if (!panel.Contains("ApplyOperationalMedievalSkin()") ||
                !panel.Contains("MedievalUiSkin.ApplyButton(button, primary)"))
                failures.Add("operational destinations still bypass the shared medieval button family");
            if (!panel.Contains("string.Equals(objectName, \"Scrim\"") ||
                !panel.Contains("string.Equals(objectName, \"CloseButton\""))
                failures.Add("bulk operational styling can repaint the modal scrim or shared Close");
            if (!panel.Contains("\"Build defense\""))
                failures.Add("Defense empty-state CTA is not mobile-readable");

            reason = failures.Count == 0
                ? "MANAGE_APPROVED_LAUNCHER_OK four-card hierarchy, lock feedback, clean summaries, and rapid-tap guard"
                : "MANAGE_APPROVED_LAUNCHER_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }
    }
}
