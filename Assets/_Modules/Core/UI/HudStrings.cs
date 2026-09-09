// =============================================================================
// HudStrings — the ONE home for the words the town HUD's CHIPS and BAR FACES say
// (WO-1144).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// WHY THIS FILE EXISTS
// The 2026-08-22 headed-fleet capture (autopilot-runs/*/break_24_error.png,
// identical across all 8 runs) showed two HUD labels CUT, not abbreviated:
//   • the Collectors rail chip read "Tap to collec" — a word sliced mid-glyph;
//   • the Manage action-bar face read "Manag..." while every sibling fit.
// Neither was a font fault. Both were SENTENCES typed inline at the call site
// and dropped into boxes that were never wide enough for them:
//   • the rail chip is 220 ref px wide by law (== EchoUnlockFeedback.
//     EchoChipWidthPx — three chips share one right edge), so its label rect is
//     ~202 ref px, and "Tap to collect" measures ~214 ref px at the 30 px
//     ElarionUiKit.FontFloor. There is NO legible size at which it fits.
//   • a Manage bar face is one MaxVisibleFaces-th of a 46 %-wide ActionBar zone
//     (~144 ref px of label rect), and WO-1027's "Manage - 2 of 3 idle" is
//     roughly four times that at the floor.
//
// This class now names keys only. English lives in en.json/GameStrings and all
// runtime resolution goes through LocalText.
//
// ⛔ THE STANDING RULE THIS FILE ENCODES: when a HUD label does not fit, the WORDS
// get shorter — never the font (FontFloor/FontHardFloor are floors, not budget),
// never the chip (the rail's shared edge is canon), never the touch target
// (MinTouchPx 112 is a floor too). HudLabelFitRegression MEASURES every string
// below against its real box with the real font's glyph advances, at both
// landscape capture aspects, so a re-lengthened sentence fails the gate instead
// of reaching a device.
//
// This remains a compatibility key catalog while its call sites move to typed
// LocalizedText wrappers. It is not a second table reader.
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.UI
{
    /// <summary>Canon-backed copy for the town HUD's rail chips and bar faces. Keys only.</summary>
    public static class HudStrings
    {
        // ── The ambient Collectors rail chip (WO-900 §4, re-fitted by WO-1144) ──
        // Two SHORT lines in a 220x112 chip. Line 1 is the state, line 2 the action.
        // ⚠ COPY LAW (WO-900 §4): this chip says "Collectors", never "Storage" —
        // "Storage"/"Bank"/current-max is the WALLET's word (WO-857), and the player
        // must never meet two different notions of "full" on one screen.

        /// <summary>Chip line 1 when there is nothing to report yet (the bare word).</summary>
        public const string KeyCollectorsTitle = "hudCollectorsTitle";

        /// <summary>Chip line 1 with the count; {0} = full collectors, {1} = total.</summary>
        public const string KeyCollectorsCount = "hudCollectorsCount";

        /// <summary>Chip line 2 when at least one collector has STOPPED EARNING. The
        /// load-bearing sentence: the fix is one tap. Deliberately the bare imperative —
        /// line 1 already carries "N/M full", so the verb is all that is left to say and
        /// it is all that fits at the legibility floor.</summary>
        public const string KeyCollectorsFullLine = "hudCollectorsFullLine";

        /// <summary>Chip line 2 when nothing is full but one collector is nearly there;
        /// {0} = the fullest collector's percentage.</summary>
        public const string KeyCollectorsNearlyLine = "hudCollectorsNearlyLine";

        /// <summary>Chip line 2 when there is pending yield but no urgency; {0} = the count.</summary>
        public const string KeyCollectorsWaitingLine = "hudCollectorsWaitingLine";

        // ── The Manage bar face's session-shape numeral (WO-1027, re-fitted by WO-1144) ──
        // The face word ("Manage") stays in HudActionBarModel.ManageBaseLabel — the View
        // BUILDS with it, so it is enum-adjacent identity rather than a sentence. Only the
        // numeral half is copy, and it now rides a SECOND LINE on the face rather than a
        // " - " suffix that no bar face has ever been wide enough to hold.

        /// <summary>Manage face line 2 when EVERY line is idle; {0} = the idle count
        /// (the denominator would be noise when it equals the numerator).</summary>
        public const string KeyManageIdleAll = "hudManageIdleAll";

        /// <summary>Manage face line 2 when some lines are cooking; {0} = idle, {1} = total.</summary>
        public const string KeyManageIdleSome = "hudManageIdleSome";

        // -- The store's ONE player-facing name (WO-1398) --
        // Two HUD rows both read "Night Market" and opened two different screens, while the
        // store's own title came from canon (storeWordmark) and the Realm deck card said
        // "Realm Store" - four names for one door, typed at four call sites. Every face that
        // opens PanelId.RealmStore now renders THIS key: the HUD card, the Realm deck card and
        // the Play skin title through this class, the store panel through StoreStrings.
        // KeyWordmark. The key NAME is the single source; the two readers load the same file.

        /// <summary>The store's wordmark - the same canon row StoreStrings.KeyWordmark names.
        /// A face that opens the store says exactly what the store's own title says.</summary>
        public const string KeyStoreWordmark = "storeWordmark";

        // -- The Hero deck and its three destination screens (WO-1410) --
        // Each face and chrome title resolves the same row. Typed synonyms such as
        // Inventory, Talents and Hot-Swap Skills are deliberately not fallbacks.
        public const string KeyHeroBag = "heroBag";
        public const string KeyHeroSkills = "heroSkills";
        public const string KeyHeroLoadout = "heroLoadout";

        // -- The Journey deck's three new cards (WO-1376 / WO-1394 / WO-1396, 2026-09-05) --
        // The purpose lines are the WO-1378 canon record (creative canon section 8.4: five
        // FANTASIES, not five mechanics). Quests and Raids keep their live subtitles; these three
        // are the first readers of their rows.

        /// <summary>Journey deck, Dungeons card purpose line.</summary>
        public const string KeyJourneyDungeons = "journeyCardDungeonsSubtitle";
        /// <summary>Journey deck, Realm Map card purpose line.</summary>
        public const string KeyJourneyRealmMap = "journeyCardRealmMapSubtitle";
        /// <summary>Journey deck, Season card purpose line.</summary>
        public const string KeyJourneySeason = "journeyCardSeasonSubtitle";
        /// <summary>The Dungeons card's locked reason - the WO-1114 ruled WORLD copy for a sealed
        /// door ("The way is barred."), the same row DungeonSealedDoorPanel falls back to. A closed
        /// dungeon reads as world, never as build status (CLAUDE.md section 7).</summary>
        public const string KeyDungeonSealedHeadline = "dungeonSealedHeadline";

        /// <summary>Every key this class names, so an oracle can prove each one resolves
        /// AND measure each one against the box it renders in.</summary>
        public static readonly string[] AllKeys =
        {
            KeyCollectorsTitle, KeyCollectorsCount, KeyCollectorsFullLine,
            KeyCollectorsNearlyLine, KeyCollectorsWaitingLine,
            KeyManageIdleAll, KeyManageIdleSome,
            KeyStoreWordmark,
            KeyHeroBag, KeyHeroSkills, KeyHeroLoadout,
            KeyJourneyDungeons, KeyJourneyRealmMap, KeyJourneySeason,
            KeyDungeonSealedHeadline,
        };

        /// <summary>
        /// The store's name for a face that opens the store (WO-1398). Resolves
        /// <see cref="KeyStoreWordmark"/> and traces the resolution once per built face so a
        /// capture can prove which words a site rendered and where they came from. A missing key
        /// is a FlowTrace.Fail naming the site - never a literal fallback, which would be the
        /// fourth name this ticket exists to remove.
        /// </summary>
        /// <param name="site">Where the face is built: "hud-card", "realm-deck", "play-skin".</param>
        public static string StoreFaceLabel(string site)
        {
            string label = Get(KeyStoreWordmark);
            if (label.IndexOf("[[missing:", StringComparison.Ordinal) >= 0)
            {
                FlowTrace.Fail("Store", "storeWordmark unresolved at " + site +
                                        " - the face renders the placeholder marker, not a typed name");
                return label;
            }
            FlowTrace.Step("Store", "store face label='" + label + "' source=LocalText site=" + site);
            return label;
        }

        /// <summary>Canon-backed Hero destination name, with a trace at each visible face.</summary>
        public static string HeroFaceLabel(string key, string site)
        {
            string label = Get(key);
            FlowTrace.Step("Hero", "face label='" + label +
                                   "' source=LocalText site=" + (site ?? "unknown"));
            return label;
        }

        /// <summary>Resolves through the one global localization authority.</summary>
        public static string Get(string key)
        {
            return LocalText.Get(key);
        }

        /// <summary>Resolves a canon key and formats it. A bad format string degrades to the raw sentence.</summary>
        public static string Format(string key, params object[] args)
        {
            return LocalText.Format(key, args);
        }

        /// <summary>Test/diagnostic hook - drops the cached map so a re-read picks up an edit.</summary>
        [Obsolete("LocalText owns locale table lifetime; Reload is no longer required.")]
        public static void Reload() { }

    }
}
