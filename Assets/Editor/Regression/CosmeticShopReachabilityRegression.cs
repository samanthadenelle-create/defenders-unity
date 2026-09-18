// =============================================================================
// CosmeticShopReachabilityRegression [cosmetic-shop-reach] (WO-1397) - the Cosmetic
// Shop has a door a PLAYER can tap, and the door goes through PanelRouter.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression
// Markers:  COSMETIC_SHOP_REACH_OK / COSMETIC_SHOP_REACH_FAIL
//
// WHAT WAS FOUND (docs/qa/UI_SCREEN_GRAPH_2026-09-04.md dead end 4): CosmeticShopPanel
// registered PanelId.CosmeticShop every session (CosmeticShopPanel.Awake) and its header
// claimed "Opened via its world interactable (Marketplace)" - but BuildingType has no
// Marketplace member and BuildingInteractable.TryPanelFor has no Cosmetic case. The ONLY
// caller was the dialogue verb OpenCosmetics (DialogueCommandSink), which no dialogue in
// either dialogues.json copy uses. Thirty-seven authored looks, an ownership service, an
// applier - and no screen.
//
// THE FIX SHAPE THIS SUITE PINS: ONE door, the Hero deck's fifth card "Wardrobe"
// (PlayerDeckWorkspace.CardsFor, case PlayerDeckKind.Hero), routed like its four siblings
// through Route(...PanelId.CosmeticShop) so Available follows PanelRouter.IsRegistered and
// Open is PanelRouter.Open. The deck grid grows a THIRD ROW for a fifth card instead of
// overflowing its band. OpenOverlay traces what the player sees.
//
//   A  A player-facing, non-verb caller of PanelId.CosmeticShop exists: the Hero deck block
//      of PlayerDeckWorkspace carries a Route line whose title is "Wardrobe" and whose target
//      is PanelId.CosmeticShop, and it is text-free (three string literals - WO-1341 shape).
//   B  The shop is REACHABLE: CosmeticShopPanel still registers PanelId.CosmeticShop against
//      OpenOverlay (a registered opener), and its Awake is still the registrar - a door onto
//      an unregistered id is a locked card forever.
//   C  The stale claim is gone: CosmeticShopPanel.cs no longer says the shop is "Opened via
//      its world interactable (Marketplace)"; the header names the Hero deck door.
//   D  Layout: PlayerDeckWorkspace.RenderPage derives its row count from the card count
//      (Mathf.CeilToInt(cards.Count / ...)) instead of the fixed h * 0.5f - five cards on a
//      two-row grid would push row three under the purpose band.
//   E  Trace: OpenOverlay's success path reaches the "shop opened from Hero deck" Step, and the
//      bridge-miss path is a "shop unavailable:" Warn (no silent empty list).
//   F  Runtime: PanelRouter.IsRegistered(PanelId.CosmeticShop) flips true once a fake opener
//      is registered and the id round-trips through Open - the card's Available predicate is
//      the router's own answer, never a second flag.
//   G  WO-1523: the Wardrobe route is CONDITIONAL - the Hero block gates it on the VM's
//      WardrobeHasUnlocked, badges its purpose through PurposeWithBadge, and clears the badge
//      with HeroDeckWardrobeVM.MarkSeen when the card is opened.
//   H  WO-1523 measured: HeroDeckWardrobeVM answers hide at owned=0, show + NEW at owned=1, and
//      drops NEW after the first open. This is the SAME decider the deck consults, so the case
//      measures what the screen does, not a parallel rule.
//
// RED-FIRST: on the pre-WO-1397 tree A fails (no Wardrobe route), C fails (header claims a
// Marketplace interactable), D fails (fixed h * 0.5f) and E fails (no trace). ONE-LINE
// MUTATION that reds it on the fixed tree: delete the
//   Route("Wardrobe", ..., PanelId.CosmeticShop)
// line from PlayerDeckWorkspace.CardsFor - A fails (no player door; the shop is dead end 4
// again) and HudLabelFitRegression [deck-card-labels] fails its 5-route count in the same run.
//
// Case F is the only runtime case; PanelRouter is a pure static so it runs in EditMode with a
// throwaway opener that is unregistered again before the suite returns.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Core.HudModel;   // WO-1523: CosmeticSignals + HeroDeckWardrobeVM

namespace DeNelle.Editor.Regression
{
    public static class CosmeticShopReachabilityRegression
    {
        private const string Tag = "[cosmetic-shop-reach]";
        private const string DeckSrc = "Assets/_Modules/HUD/PlayerDeckWorkspace.cs";
        private const string ShopSrc = "Assets/_Modules/HUD/CosmeticShopPanel.cs";
        private const string VerbSrc = "Assets/_Modules/Village/Tutorial/DialogueCommandSink.cs";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("COSMETIC_SHOP_REACH_OK - " + reason);
            else Debug.LogError("COSMETIC_SHOP_REACH_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- COSMETIC SHOP REACHABILITY (WO-1397): the Hero deck Wardrobe card is the door ---");

            string deck = ReadOrNull(DeckSrc);
            string shop = ReadOrNull(ShopSrc);
            string verb = ReadOrNull(VerbSrc);
            if (deck == null || shop == null || verb == null)
            {
                reason = Tag + " could not read " + DeckSrc + " / " + ShopSrc + " / " + VerbSrc;
                return false;
            }

            try
            {
                CaseA_HeroDeckCarriesTheDoor(deck, failures, log);
                CaseB_ShopRegistersTheId(shop, failures, log);
                CaseC_StaleMarketplaceClaimGone(shop, failures, log);
                CaseD_GridGrowsRows(deck, failures, log);
                CaseE_DoorIsTraced(shop, failures, log);
                CaseF_RouterRoundTrip(failures, log);
                CaseG_WardrobeGatedOnUnlock(deck, failures, log);
                CaseH_VmDecidesShowAndNew(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = "COSMETIC_SHOP_REACH_FAIL\n - " + string.Join("\n - ", failures) + "\n" + log;
                return false;
            }
            reason = log.ToString().TrimEnd();
            return true;
        }

        // A: the Hero deck block routes a text-free "Wardrobe" card to PanelId.CosmeticShop.
        private static void CaseA_HeroDeckCarriesTheDoor(string deck, List<string> failures, StringBuilder log)
        {
            int cardsFor = deck.IndexOf("List<Card> CardsFor(", StringComparison.Ordinal);
            if (cardsFor < 0)
            {
                failures.Add(Tag + " A: no 'List<Card> CardsFor(' in " + DeckSrc + " - the deck card table was renamed and A is measuring nothing");
                return;
            }
            string hero = Between(deck.Substring(cardsFor), "case PlayerDeckKind.Hero:", "case PlayerDeckKind.Journey:");
            if (hero == null)
            {
                failures.Add(Tag + " A: no 'case PlayerDeckKind.Hero:' block followed by the Journey case in " + DeckSrc);
                return;
            }

            string wardrobeLine = null;
            foreach (string raw in hero.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.IndexOf("Route(", StringComparison.Ordinal) < 0) continue;
                if (line.IndexOf("PanelId.CosmeticShop", StringComparison.Ordinal) < 0) continue;
                wardrobeLine = line;
                break;
            }
            if (wardrobeLine == null)
            {
                failures.Add(Tag + " A: the Hero deck has NO Route(...) to PanelId.CosmeticShop - the Cosmetic Shop is unreachable by a player (dead end 4)");
                return;
            }
            // WO-1857 re-point: the literal "Wardrobe" title moved behind
            // hud.hero_deck.wardrobe_title (en value confirmed still "Wardrobe" at source), so the
            // route now opens with a LocalText.Get call rather than the bare literal.
            if (wardrobeLine.IndexOf("Route(LocalText.Get(\"hud.hero_deck.wardrobe_title\")", StringComparison.Ordinal) != 0)
                failures.Add(Tag + " A: the Cosmetic Shop route is not titled \"Wardrobe\": '" + wardrobeLine + "'");
            int quotes = 0;
            for (int i = 0; i < wardrobeLine.Length; i++) if (wardrobeLine[i] == '"') quotes++;
            if (quotes != 6)
                failures.Add(Tag + " A: the Wardrobe route has " + (quotes / 2) + " string literals, expected 3 (title, purpose, concept). " +
                             "A 4th is an ART KEY and no text-free PNG exists for Wardrobe - that is the WO-1341 double-label shape");
            for (int i = 0; i < wardrobeLine.Length; i++)
            {
                if (wardrobeLine[i] <= 126) continue;
                failures.Add(Tag + " A: non-ASCII U+" + ((int)wardrobeLine[i]).ToString("X4") + " in the Wardrobe route - player copy is ASCII-only");
                break;
            }
            log.AppendLine("  A: Hero deck routes \"Wardrobe\" -> PanelId.CosmeticShop, text-free");
        }

        // B: the shop still registers the id against a real opener.
        private static void CaseB_ShopRegistersTheId(string shop, List<string> failures, StringBuilder log)
        {
            string awake = Between(shop, "private void Awake()", "private bool IsOverlayOpen()");
            if (awake == null || awake.IndexOf("PanelRouter.Register(PanelId.CosmeticShop, OpenOverlay);", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " B: CosmeticShopPanel.Awake does not register PanelId.CosmeticShop -> OpenOverlay; the Wardrobe card would render LOCKED forever");
            if (shop.IndexOf("PanelRouter.Unregister(PanelId.CosmeticShop, OpenOverlay);", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " B: CosmeticShopPanel no longer unregisters its opener on destroy - a dead delegate stays routable");
            log.AppendLine("  B: CosmeticShopPanel registers/unregisters PanelId.CosmeticShop -> OpenOverlay");
        }

        // C: the header no longer claims an interactable that does not exist.
        private static void CaseC_StaleMarketplaceClaimGone(string shop, List<string> failures, StringBuilder log)
        {
            if (shop.IndexOf("Opened via its world", StringComparison.Ordinal) >= 0 ||
                shop.IndexOf("interactable (Marketplace)", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " C: " + ShopSrc + " still claims the shop is opened via a Marketplace interactable - BuildingType has no Marketplace and TryPanelFor has no Cosmetic case");
            if (shop.IndexOf("Wardrobe", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " C: " + ShopSrc + " does not name its real door (the Hero deck Wardrobe card) - the next reader inherits a header that lies");
            log.AppendLine("  C: header names the Hero deck Wardrobe door, no Marketplace claim");
        }

        // D: the deck grid grows rows from the card count.
        private static void CaseD_GridGrowsRows(string deck, List<string> failures, StringBuilder log)
        {
            string render = Between(deck, "protected override void RenderPage(", "private void BuildCard(");
            if (render == null)
            {
                failures.Add(Tag + " D: PlayerDeckWorkspace.RenderPage not found");
                return;
            }
            if (render.IndexOf("Mathf.CeilToInt(cards.Count /", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " D: RenderPage does not derive its row count from cards.Count - a fifth card overflows the two-row grid into the purpose band");
            if (render.IndexOf("h * 0.5f", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " D: RenderPage still fixes the cell height at h * 0.5f (two rows) - five cards do not fit");
            if (render.IndexOf("\"deck '\" + page.Kind + \"' grid \"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " D: RenderPage does not trace the grid shape it built ('[Flow:Navigation] deck '<kind>' grid CxR ...')");
            log.AppendLine("  D: grid rows = max(2, ceil(cards / columns)); shape traced");
        }

        // E: the door is traced on both paths.
        private static void CaseE_DoorIsTraced(string shop, List<string> failures, StringBuilder log)
        {
            if (shop.IndexOf("\"shop opened from Hero deck; owned=\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " E: OpenOverlay does not trace '[Flow:CosmeticShop] shop opened from Hero deck; owned=<n>/<total> equipped=<id|none>'");
            if (shop.IndexOf("\"shop unavailable: ", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " E: the bridge-miss path is not a '[Flow:CosmeticShop] shop unavailable: <reason>' Warn - an empty shop would be silent");
            string open = Between(shop, "public void OpenOverlay()", "public void CloseOverlay()");
            if (open == null || open.IndexOf("TraceOpened();", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " E: OpenOverlay's success path does not call TraceOpened()");
            log.AppendLine("  E: OpenOverlay traces opened (owned/total/equipped) and unavailable (reason)");
        }

        // F: the router answers the card's Available predicate.
        //
        // PanelRouter.Register REPLACES the opener for an id (dictionary set), and Unregister only
        // removes when the current delegate is ours - so if a LIVE CosmeticShopPanel already holds
        // the id (a play-mode gate) the swap is skipped and its registration IS the proof. In
        // EditMode nothing is registered, so a throwaway opener stands in. That opener records
        // itself with the modal arbiter exactly as CosmeticShopPanel.OpenOverlay does
        // (PanelManager.NotifyOpened), because PanelRouter.Open verifies a panel is recorded open
        // afterwards and FlowTrace.Fails the WO-465 invisible-scrim class when none is.
        private static void CaseF_RouterRoundTrip(List<string> failures, StringBuilder log)
        {
            if (PanelRouter.IsRegistered(PanelId.CosmeticShop))
            {
                log.AppendLine("  F: a live opener already holds PanelId.CosmeticShop - the Wardrobe card is Available (no swap)");
                return;
            }
            bool isOpen = false;
            int opened = 0;
            PanelHandle handle = PanelManager.Register("Cosmetic Shop (regression stand-in)", () => isOpen = false, () => isOpen);
            Action opener = () =>
            {
                opened++;
                isOpen = true;
                if (!PanelManager.NotifyOpened(handle)) isOpen = false;
            };
            PanelRouter.Register(PanelId.CosmeticShop, opener);
            try
            {
                if (!PanelRouter.IsRegistered(PanelId.CosmeticShop))
                    failures.Add(Tag + " F: PanelRouter.IsRegistered(CosmeticShop) is false after Register - the Wardrobe card's Available predicate would lock it");
                bool ok = PanelRouter.Open(PanelId.CosmeticShop);
                if (opened != 1)
                    failures.Add(Tag + " F: PanelRouter.Open(CosmeticShop) invoked the opener " + opened + " time(s), expected 1");
                if (!ok)
                    failures.Add(Tag + " F: PanelRouter.Open(CosmeticShop) returned false - the opener ran but no panel was recorded open (or battle-lock refused)");
            }
            finally
            {
                if (isOpen) { isOpen = false; PanelManager.NotifyClosed(handle); }
                PanelRouter.Unregister(PanelId.CosmeticShop, opener);
            }
            if (PanelRouter.IsRegistered(PanelId.CosmeticShop))
                failures.Add(Tag + " F: PanelId.CosmeticShop is still registered after Unregister - the stand-in leaked into the next suite");
            log.AppendLine("  F: PanelId.CosmeticShop round-trips Register -> IsRegistered -> Open (arbiter-verified) -> Unregister");
        }

        // G (WO-1523): the Wardrobe card is CONDITIONAL on the VM, and it is ABSENT rather than
        // locked/collapsed when nothing is unlocked. Source case - the owner's ruling is
        // "dont show the section", and a card that is built and then hidden still lands in a
        // measured layout capture.
        private static void CaseG_WardrobeGatedOnUnlock(string deck, List<string> failures, StringBuilder log)
        {
            int cardsFor = deck.IndexOf("List<Card> CardsFor(", StringComparison.Ordinal);
            string hero = cardsFor < 0 ? null
                : Between(deck.Substring(cardsFor), "case PlayerDeckKind.Hero:", "case PlayerDeckKind.Journey:");
            if (hero == null)
            {
                failures.Add(Tag + " G: no Hero deck block in " + DeckSrc + " - G is measuring nothing");
                return;
            }
            if (hero.IndexOf("WardrobeHasUnlocked", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " G: the Hero deck does not gate its Wardrobe route on " +
                             "HeroDeckWardrobeVM.WardrobeHasUnlocked - WO-1523 rules an all-locked wardrobe " +
                             "off the Hero screen entirely");
            if (hero.IndexOf("PurposeWithBadge", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " G: the Wardrobe card does not take its purpose from " +
                             "HeroDeckWardrobeVM.PurposeWithBadge - the NEW word must arrive with the section");
            if (hero.IndexOf("HeroDeckWardrobeVM.MarkSeen", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " G: opening the Wardrobe card does not call HeroDeckWardrobeVM.MarkSeen - " +
                             "the NEW word would never clear");
            log.AppendLine("  G: Wardrobe route is gated on WardrobeHasUnlocked, badged, and clears NEW on open");
        }

        // H (WO-1523): the measured case. Zero unlocked cosmetics -> the section is not shown;
        // one -> it is shown and carries the NEW word. Read through the VM, which is the ONE
        // decider the deck consults, so this measures the same answer the screen gets.
        private static void CaseH_VmDecidesShowAndNew(List<string> failures, StringBuilder log)
        {
            int restore = CosmeticSignals.OwnedCount;
            bool restoreSeen = PlayerPrefs.GetInt(HeroDeckWardrobeVM.SeenPrefKey, 0) != 0;
            try
            {
                // A previous gate run on this machine can leave the seen flag set; the NEW arm
                // measures a FIRST arrival, so clear it deliberately rather than inherit it.
                HeroDeckWardrobeVM.ClearSeenForTests();

                CosmeticSignals.SetOwnedCount(0);
                var locked = HeroDeckWardrobeVM.FromCurrentState();
                if (locked.WardrobeHasUnlocked)
                    failures.Add(Tag + " H: with ZERO unlocked cosmetics WardrobeHasUnlocked is true - " +
                                 "the Hero screen would still carry an all-locked wardrobe section");
                if (locked.WardrobeIsNew)
                    failures.Add(Tag + " H: a hidden wardrobe reports WardrobeIsNew - a NEW word on a " +
                                 "section nobody can see");

                CosmeticSignals.SetOwnedCount(1);
                var unlocked = HeroDeckWardrobeVM.FromCurrentState();
                if (!unlocked.WardrobeHasUnlocked)
                    failures.Add(Tag + " H: with ONE unlocked cosmetic WardrobeHasUnlocked is false - " +
                                 "the section never returns and the shop is dead end 4 again");
                if (!unlocked.WardrobeIsNew)
                    failures.Add(Tag + " H: the first appearance does not carry NEW");
                string badged = unlocked.PurposeWithBadge("Looks for your hero, Echo, and town");
                if (badged == null || badged.IndexOf(HeroDeckWardrobeVM.NewWord, StringComparison.Ordinal) != 0)
                    failures.Add(Tag + " H: the badged purpose does not lead with '" +
                                 HeroDeckWardrobeVM.NewWord + "': '" + badged + "'");

                HeroDeckWardrobeVM.MarkSeen();
                var seen = HeroDeckWardrobeVM.FromCurrentState();
                if (!seen.WardrobeHasUnlocked)
                    failures.Add(Tag + " H: the section disappeared after being opened once");
                if (seen.WardrobeIsNew)
                    failures.Add(Tag + " H: NEW survives the first open - MarkSeen did not stick");
            }
            finally
            {
                if (restoreSeen) PlayerPrefs.SetInt(HeroDeckWardrobeVM.SeenPrefKey, 1);
                else HeroDeckWardrobeVM.ClearSeenForTests();
                CosmeticSignals.SetOwnedCount(restore);
            }
            log.AppendLine("  H: owned=0 hides the section; owned=1 shows it carrying " +
                           HeroDeckWardrobeVM.NewWord + ", cleared by the first open");
        }

        private static string ReadOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static string Between(string src, string from, string until)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(until, a + from.Length, StringComparison.Ordinal);
            return b < 0 ? null : src.Substring(a, b - a);
        }
    }
}
