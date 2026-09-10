#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using DeNelle.Core;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class BuildCollectionPlayerRegression
    {
        private static readonly string[] Ids = { "build-gathering", "build-realm", "build-defenses", "build-crafting", "build-storage", "build-protection", "build-trade" };
        private static readonly string[] Icons = { "Resources", "Realm", "Defense", "Crafting", "Storage", "Protection", "Trade" };

        [MenuItem("Tools/Regression/Run Build Collection Player")]
        public static void RunMenu() { if (!Run(out var r)) throw new Exception(r); UnityEngine.Debug.Log(r); }

        public static bool Run(out string reason)
        {
            string path = "Assets/Resources/Data/Canonical/card-collections.json";
            var doc = JsonConvert.DeserializeObject<CardCollectionDocument>(File.ReadAllText(path));
            var build = doc.Collections.Where(c => c.Context == "build" && c.Active).ToList();
            if (build.Count != 7 || !Ids.SequenceEqual(build.Select(c => c.CollectionId))) return Fail("canonical category order/count changed", out reason);
            for (int i=0;i<Icons.Length;i++)
            {
                string expected = "UI/BuildCollections/" + Icons[i];
                if (build[i].IconKey != expected) return Fail("icon key mismatch: " + build[i].CollectionId, out reason);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Resources/" + expected + ".png") == null) return Fail("approved icon is missing or not imported as a Sprite: " + Icons[i], out reason);
            }
            var defense = build.Single(c => c.CollectionId == "build-defenses");
            if (defense.Items.Count != 5 || CardCollectionPaging.PageCount(defense.Items.Count) != 2 || CardCollectionPaging.FirstIndex(1, 5) != 4)
                return Fail("Defense must page 4+1", out reason);
            var craftingIds = build.Single(c => c.CollectionId == "build-crafting").Items
                .OrderBy(i => i.Order).Select(i => i.ItemId).ToArray();
            var tradeIds = build.Single(c => c.CollectionId == "build-trade").Items
                .OrderBy(i => i.Order).Select(i => i.ItemId).ToArray();
            if (!new[] { "workshop", "jeweler" }.SequenceEqual(craftingIds))
                return Fail("Crafting membership/order must be workshop,jeweler only", out reason);
            if (!new[] { "market", "forge", "armorer" }.SequenceEqual(tradeIds))
                return Fail("Trade membership/order must be market,forge,armorer only", out reason);
            string browser = File.ReadAllText("Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs");
            string palette = File.ReadAllText("Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs");
            if (!browser.Contains("BuildCollectionBrowser : ObsidianNavigationWorkspace<BuildCollectionPage>") ||
                !browser.Contains("public override void Close()") ||
                !browser.Contains("base.Close();") ||
                !browser.Contains("Done(BuildFirstUseGuide.ItemSelected") || !browser.Contains("callback?.Invoke(entry)"))
                return Fail("shared workspace/Place release contract missing", out reason);
            if (!browser.Contains("\"Locked\"") || !browser.Contains("COST: ") || !browser.Contains("vm?.Description"))
                return Fail("readable card/state contract missing", out reason);
            // -- WO-1417 [kit-card] ----------------------------------------------
            // The palette item card is a KIT SURFACE, and its two copy lines are
            // player English. Three pins, each RED against the pre-WO-1417 file:
            //  (a) the card face is the kit's obsidian plate, not a bespoke literal
            //      colour, and it carries the same gold perimeter the sibling
            //      category card uses. The Outline that carried locked-vs-unlocked
            //      by HUE ALONE must stay gone (the owner is red/green colourblind).
            //  (b) the basket runs through the ONE shared cost formatter, so this
            //      surface cannot re-grow a private second wording for a price.
            //  (c) NO STRING LITERAL in the browser contains a '[' bracket glyph or
            //      the retired "NO COST". Scanned over QUOTED LITERALS ONLY -- the
            //      source is full of attribute/indexer brackets, so a whole-file
            //      Contains("[") would pin nothing and fail always.
            if (!browser.Contains("Box(\"BuildCard_\" + itemId, slot.transform, ElarionUiKit.ObsidianFill)") ||
                !browser.Contains("AddGoldPerimeter(card.transform)") ||
                browser.Contains("AddComponent<Outline>(); outline.effectColor = locked"))
                return Fail("palette item card is not the kit obsidian plate + gold bezel, or state is carried by an outline hue", out reason);
            if (!browser.Contains("CostFormat.Words(CostParts(vm.EffectiveCost))") ||
                !browser.Contains("CostFormat.Parts(new[]"))
                return Fail("palette card cost line no longer runs through the one shared cost formatter", out reason);
            foreach (string literal in StringLiterals(browser))
            {
                if (literal.Contains("["))
                    return Fail("palette string carries a bracket glyph: " + literal, out reason);
                if (literal.Contains("NO COST"))
                    return Fail("palette string carries the retired literal NO COST: " + literal, out reason);
            }
            if (!browser.Contains("BuildCardSlot_") ||
                !browser.Contains("Box(\"BuildCard_\" + itemId, slot.transform") ||
                !browser.Contains("ButtonBox(slot.transform, buttonFace") ||
                !browser.Contains("card.anchorMin = new Vector2(0f, .18f)"))
                return Fail("shared collection action is no longer a footer below the card; mobile copy can be covered again", out reason);
            if (!browser.Contains("HiddenUntilFinishedArtId = \"gate_stone\"") ||
                !browser.Contains("VisibleItemIds()"))
                return Fail("unfinished Stone Gate card is exposed instead of presentation-gated", out reason);
            if (!browser.Contains("IsCollectionItemVisible") ||
                !browser.Contains("ProgressionUnlocks.IsUnlocked(itemId)") ||
                !browser.Contains("!string.IsNullOrEmpty(lockReason) && !ProgressionUnlocks.IsUnlocked(itemId)"))
                return Fail("progression-locked collection entries are not hidden-before/unhidden-after authoritative unlock", out reason);
            if (!browser.Contains("CollectionHasVisibleItems(c)") ||
                !browser.Contains("var entry = CatalogRegistry.Get(item.ItemId)") ||
                // WO-1572: was `if (entry == null) continue` -- the predicate now tallies each
                // skip reason for its FlowTrace, so the bare statement became a braced block.
                // Same contract: a row with no catalog entry cannot make a category navigable.
                !browser.Contains("if (entry == null) { missingEntry++; continue; }") ||
                browser.Contains("CollectionHasVisibleItems(c, EconomyService"))
                return Fail("category projection does not hide empty-after-eligibility categories or incorrectly hides unlocked unaffordable goals", out reason);
            // WO-1572 RE-POINTED 2026-09-07. This pin used to require the TWIN-COUNTING
            // predicate `StructureSingleton.IsBuilt(entry)` in both browser sites, which is
            // exactly the defect: an ACTIVE BAKED TWIN made a singleton read "built", and
            // build-realm + build-trade (every item authors a bakedTwin) vanished from the
            // root. The finite-category contract is unchanged - only its AUTHORITY moves to
            // IsPlayerBuilt, the same query BuildModeController.IsSingletonBuilt has asked
            // since WO-843. RED against the fixed file if IsBuilt is ever restored here.
            if (!browser.Contains("StructureSingleton.IsSingleton(entry.id) && StructureSingleton.IsPlayerBuilt(entry)") ||
                browser.Contains("StructureSingleton.IsSingleton(entry.id) && StructureSingleton.IsBuilt(entry)") ||
                !browser.Contains("StructureSingleton.SingletonReleased += OnFiniteCapacityChanged") ||
                !browser.Contains("offered++; // repeatable entries remain visible"))
                return Fail("finite category does not hide at final PLAYER placement/restore on removal, counts a baked twin as built, or repeatable categories are incorrectly removed", out reason);
            if (!browser.Contains("SetArtworkOrFallback") || !browser.Contains("Image coming soon") ||
                !browser.Contains("image.color = new Color(.10f, .11f, .14f, 1f)"))
                return Fail("missing collection art can regress to a white/blank image slot", out reason);
            if (browser.Contains("TextOverflowModes.Ellipsis") || browser.Contains("TextOverflowModes.Truncate") ||
                !browser.Contains("buttonFace = available ? \"PLACE\"") ||
                !browser.Contains("enableWordWrapping=true") || !browser.Contains("enableAutoSizing=true") ||
                !browser.Contains("overflowMode=TextOverflowModes.Overflow"))
                return Fail("collection cards can truncate/ellipsize required copy or lack a full-copy wrapping path", out reason);
            // WO-2006 (OWNER_RULINGS_LOCKED sec.25) RE-POINTED 2026-09-06. This pin used to
            // require the ENTIRE call as one literal, closing paren included:
            //     "_collectionBrowser.Show(entry => OnEntrySelected?.Invoke(entry))"
            // which pinned the call's FORMATTING, not the seam it means to protect. The moment
            // the browser's Show gained its second argument (the MANAGE PLACED door callback)
            // the literal stopped matching and this suite would have failed a change that
            // preserves the Arm seam perfectly -- an oracle blocking the ruling it never had an
            // opinion about. Split into the two facts that ARE the seam: the palette opens the
            // browser, and the entry callback still routes through OnEntrySelected (-> Arm).
            // Do not re-fuse these into one literal; the next argument would break it again.
            if (!palette.Contains("_collectionBrowser.Show(") ||
                !palette.Contains("entry => OnEntrySelected?.Invoke(entry)"))
                return Fail("existing Arm event seam bypassed", out reason);
            // The door itself is a SEPARATE oracle (PlacedStructureDoorRegression [placed-door]),
            // which walks the whole chain card -> palette event -> controller handler. Asserted
            // here only as the one fact this file already owns: the palette supplies the callback,
            // because BuildCollectionBrowser refuses to build a card it cannot honour.
            if (!palette.Contains("OnManagePlacedRequested?.Invoke()"))
                return Fail("palette no longer supplies the Manage Placed door callback to the browser " +
                            "(ruling 25) -- the card would not be built at all", out reason);
            // WO-1411 RE-POINTED 2026-09-06 (owner ruling section 2 #13, written to the default:
            // rename YES, keep the 8th card NO). This block used to require
            //     browser.Contains("upgradeCard.name = \"DefenseUpgradeCard\"")
            //     browser.Contains("\"Upgrade Defenses\"")
            // i.e. it pinned the CARD, which the ruling retires. Keeping those two literals would
            // have made this suite block the ruling and go on asserting a card the review named as
            // the defect ("a Manage door dressed as a category").
            //
            // ⛔ WHAT THIS SUITE IS FOR IS UNCHANGED AND STILL PINNED: the Manage -> Defense DOOR
            // still exists on this screen and still opens the same destination. Only its dress
            // moved from an 8th card to a footer text link. The route literal below is the same
            // one the card carried and must stay green.
            if (!browser.Contains("link.name = \"ManageDefensesFooterLink\"") ||
                !browser.Contains("\"Already built? Manage defenses >\"") ||
                !browser.Contains("PanelRouter.Open(PanelId.Manage, \"Defense\")"))
                return Fail("the Manage > Defense door left the collection browser: no footer link, or it no longer opens the placed-defense upgrade destination", out reason);
            if (browser.Contains("\"Upgrade Defenses\""))
                return Fail("the retired 8th 'Upgrade Defenses' category card is back in the build grid (ruling section 2 #13 says footer link, not card)", out reason);
            // WO-1626 ADDED 2026-09-10. The footer caption used to write its own font floor onto
            // TMP -- a raw minimum BELOW ElarionUiKit.FontHardFloor (20, ElarionUiKitObsidian.cs
            // :3044). A literal written straight onto the component never enters FitSingleLine, so
            // the factory's clamp at ElarionUiKitObsidian.cs:3062 -- whose own comment says "no
            // caller may auto-shrink text below the FontHardFloor readability floor" -- could not
            // see it. One owner per concern: the kit decides the floor, the wrap mode and the
            // post-layout fit guard (ArmFitGuard, :3069) that the raw path never armed.
            //
            // WHY THE LITERAL IS "FitSingleLine(label," AND NOT THE BARE METHOD NAME: this same
            // file already calls ElarionUiKit.FitSingleLine(guidanceLabel, 22f, 28f) for the
            // subtitle, so a pin on "FitSingleLine(" alone is GREEN BEFORE THE FIX and proves
            // nothing. WHY THE NEGATIVE PIN IS "fontSizeMin = 16f" AND NOT "fontSizeMin": the
            // screen has other, in-scope-elsewhere font minima; 16f was this caption's, and the
            // only other 16f in the file is FooterLinkGridGapPx, a px gap, not a font size.
            // RED PROOF: restore `label.fontSizeMin = 16f;` at the footer-link label site (and
            // drop the FitSingleLine call) -- either half alone reds this pin.
            if (!browser.Contains("FitSingleLine(label,") ||
                browser.Contains("fontSizeMin = 16f"))
                return Fail("the Manage > Defense footer caption sets its own font floor instead of the kit's: a sub-floor fontSizeMin literal is back, or the label no longer routes through ElarionUiKit.FitSingleLine", out reason);
            // WO-1628 ADDED 2026-09-10. The seven category cards' affordability caption had its
            // band authored as .05f-.21f of CARD HEIGHT -- 0.16 of a rect that is itself a share
            // of a canvas whose reference height changes with the aspect. The step-1 probe
            // measured the result (Builds/wave2-capture3): 52.2 ref px at 1920x1080 against a
            // two-line requirement of 47.6 (2 lines, 22 of 22 chars), but 43.2 at 2340x1080 and
            // 42.1 at 2670x1200 -- one line, 21 of 22 chars, isTextTruncated TRUE on fourteen
            // labels. Same unit defect WO-1623 retired for the footer band: a height that is a
            // number of lines of readable copy is PIXELS, never a fraction.
            //
            // WHY THE POSITIVE PIN IS BOTH "CaptionBandPx" AND "-CaptionBandPx": the const being
            // DECLARED proves nothing on its own -- a later edit could leave it sitting unread
            // while the fraction crept back. The second literal is the const actually spent as
            // the band's bottom offset, so declaration and use must both survive. Both are RED
            // before the fix, because neither string existed.
            //
            // ⛔ TIGHTENED TO ZERO 2026-09-10 (WO-1629 Step 3). THE ALLOWANCE IS SPENT.
            // This clause used to permit exactly ONE `new Vector2(.08f, .05f)`, because the
            // Manage Placed card's caption legitimately still carried the retired pair and its
            // render had never been captured -- re-pointing it then would have been a fix
            // claimed without evidence. That frame has now been shot. WO-1629 made the capture
            // build the eight-card grid and probed the caption (Builds/wave2-capture5): its
            // 45-character copy needed 95.9 / 71.8 / 71.8 ref px against the .21f ceiling's
            // 68.5 / 56.7 / 55.2, so it was CUT at all three aspects (32 / 16 / 18 of 45,
            // isTextTruncated TRUE, already driven onto the kit floor at fontSize 20). No band
            // constant could seat it, so the OWNER RULED 2026-09-10 that the copy yields rather
            // than the title, the artwork or the floor: the caption is now the categories'
            // ~22-character voice, in a px band of its own (ManageCaptionBandPx).
            //
            // So the retired fraction pair must now occur ZERO times in the file -- there is no
            // caption left on this screen entitled to it, and a single re-appearance is the
            // regression this whole ticket chain exists to stop.
            // WHY THE NEGATIVE PIN NAMES ONLY `.08f, .05f`: it is the y-fraction that was the
            // defect. The x fractions .08/.92 stay proportional by design (WO-1628 proved width
            // was never implicated for the category caption).
            // RED PROOF: restore the `new Vector2(.08f, .05f), new Vector2(.92f, .21f)` pair at
            // EITHER caption -- the seven-card one or the Manage Placed one -- and delete that
            // caption's pivot/offset block. Either site alone reds this pin now, where before
            // the Manage Placed site was tolerated.
            if (!browser.Contains("CaptionBandPx") ||
                !browser.Contains("-CaptionBandPx") ||
                browser.Contains("new Vector2(.08f, .05f)"))
                return Fail("a caption band on the Build Collections cards is a fraction of card height again instead of reference px: CaptionBandPx is missing or unspent, or the retired .08/.05 fraction is back on one of the eight cards (the Manage Placed allowance was spent by WO-1629)", out reason);
            // WO-1629 Step 3 -- and the Manage Placed caption's OWN band must be DECLARED and
            // SPENT, the same declaration-plus-use pair the category band is held to above. A
            // const left unread while the fraction crept back is the exact hole that shape
            // closes. RED PROOF: delete `-ManageCaptionBandPx` from the offsetMin assignment.
            if (!browser.Contains("ManageCaptionBandPx") ||
                !browser.Contains("-ManageCaptionBandPx"))
                return Fail("the Manage Placed caption has no reference-px band of its own: ManageCaptionBandPx is missing or never spent as the band's bottom offset (WO-1629 Step 2)", out reason);
            string managePanel = File.ReadAllText("Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs");
            string manageVm = File.ReadAllText("Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs");
            // -- WO-1422 ruling 3.4 --------------------------------------------
            // RE-POINTED 2026-09-06. This block used to open with
            //   !managePanel.Contains("UPGRADABLE TOWERS - affordable first")
            // which required the PAGED Defense list's section heading. WO-1422 replaces that list
            // with the WO-1418 workspace (portrait rail + one selected card + BUILDING NOW), so the
            // heading is gone -- and it was never true anyway: the Defense tab also lists walls, a
            // crystal mine, a healing caravan and three storage containers (ruling 3.2). Keeping the
            // pin would have made this suite block the ruling, and its own failure text would have
            // gone on asserting the retired design (CLAUDE.md section 15).
            //
            // What this suite is actually FOR -- "the Upgrade Defenses card opens a destination that
            // WORKS" -- is unchanged and now stated as the destination method plus the SECONDARY
            // BUILD ROUTE, which ruling 3.4 explicitly keeps verbatim.
            // RED PROOF: delete RenderDefenseDestination from ManageScreenPanel.cs.
            if (!managePanel.Contains("private void RenderDefenseDestination("))
                return Fail("Manage's Defense tab has no RenderDefenseDestination, so the Upgrade Defenses card opens onto nothing", out reason);
            // ⛔ THIS EXACT CALL MUST STAY GREEN (ruling 3.4). It is the Defense destination's footer
            // door and ManageApprovedLauncherRegression:52 pins the same literal.
            // RED PROOF: delete the AddActionNoteRow("Need another tower?", "Build defense", OpenDefenseBuilder) footer.
            if (!managePanel.Contains("\"Build defense\", OpenDefenseBuilder") ||
                !managePanel.Contains("controller?.EnterBuildMode(DeNelle.Core.Catalog.BuildType.Defense)") ||
                !managePanel.Contains("Action<string>") ||
                !manageVm.Contains("PlacedStructureUpgradeService.MaxLevelFor") ||
                !manageVm.Contains("UpgradeCostFor(entry, level)") ||
                // WO-1405 (2026-09-06): the placed-instance identity is the upgrade-key seam, not the
                // retired "grid X, Z" copy string (the VM now says a compass side, never a coordinate).
                !manageVm.Contains("PlacedUpgradeKey.Compose("))
                return Fail("Defense upgrade screen lost its authority ceiling, authority cost, placed-instance identity, or the secondary Build-defense route", out reason);
            reason = "BUILD_COLLECTION_PLAYER_OK: 7 build categories plus the WO-1411 Manage-defenses FOOTER LINK (the 8th card is retired), approved icons, intentional missing-art fallback, locked entries hidden until authoritative unlock, Stone Gate presentation-gated, shared Obsidian workspace, readable cards, Defense 4+1, pause released before exact Arm seam; WO-1417 kit-card: item card is the kit obsidian plate + gold bezel, cost through the one shared formatter, no bracket glyph and no NO COST in any palette string literal";
            return true;
        }

        private static bool Fail(string value, out string reason) { reason = "BUILD_COLLECTION_PLAYER_FAIL: " + value; return false; }

        /// <summary>WO-1417: every double-quoted string literal in a C# source, with comments,
        /// char literals and escapes excluded. A single-state character walk rather than a regex,
        /// because the cheap regex both mis-reads an escaped quote and cannot tell a '[' in an
        /// attribute or an indexer from a '[' the player will read on a card.</summary>
        private static List<string> StringLiterals(string source)
        {
            var found = new List<string>();
            var buffer = new StringBuilder();
            int i = 0, n = source == null ? 0 : source.Length;
            while (i < n)
            {
                char c = source[i];
                if (c == '/' && i + 1 < n && source[i + 1] == '/')
                {
                    while (i < n && source[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < n && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < n && source[i] != '\'') i += source[i] == '\\' ? 2 : 1;
                    i++;
                    continue;
                }
                if (c == '@' && i + 1 < n && source[i + 1] == '"')
                {
                    i += 2; buffer.Length = 0;
                    while (i < n)
                    {
                        if (source[i] == '"' && i + 1 < n && source[i + 1] == '"') { buffer.Append('"'); i += 2; continue; }
                        if (source[i] == '"') break;
                        buffer.Append(source[i]); i++;
                    }
                    i++; found.Add(buffer.ToString());
                    continue;
                }
                if (c == '"')
                {
                    i++; buffer.Length = 0;
                    while (i < n && source[i] != '"')
                    {
                        if (source[i] == '\\' && i + 1 < n) { buffer.Append(source[i + 1]); i += 2; continue; }
                        buffer.Append(source[i]); i++;
                    }
                    i++; found.Add(buffer.ToString());
                    continue;
                }
                i++;
            }
            return found;
        }
    }
}
#endif
