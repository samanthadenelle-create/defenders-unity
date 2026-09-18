using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;   // WO-1857 - LocalText for the NEW badge sentence.
using UnityEngine;

namespace DeNelle.Core.HudModel
{
    /// <summary>
    /// WO-1523 (owner ruling 2026-09-06: "everything in wardobe is locked so dont show
    /// the section in hero"). Pure, single-read state copy for the Hero deck's Wardrobe
    /// card - the ONE place that decides whether the section exists and whether it is
    /// still new. The View builds the card only when <see cref="WardrobeHasUnlocked"/> is
    /// true and computes nothing itself, the same contract every other Manage/Hero
    /// surface follows (JourneyDeckSubtitleVM is the shape this copies).
    ///
    /// TENSION WORTH KNOWING: WO-1008 settled that a RAID door which hides itself reads
    /// as broken ("I do not see a way to start a raid"), and WO-1357 kept the Journey
    /// raid card visible-and-locked for exactly that reason. WO-1523 is the owner's
    /// explicit contrary ruling for THIS surface: a wardrobe in which every row is locked
    /// teaches nothing and costs a screenful, so it is removed from the tree entirely -
    /// not collapsed, not greyed. Do not "restore consistency" with the raid card without
    /// a new ruling.
    /// </summary>
    public sealed class HeroDeckWardrobeVM
    {
        /// <summary>The word the card carries the first time it appears. ASCII only
        /// (mobile font-atlas law) and owned here so the View never spells it.</summary>
        public const string NewWord = "NEW";

        /// <summary>PlayerPrefs key for "the player has opened the wardrobe since it
        /// appeared". One flag, one owner - the badge is not persisted state anywhere
        /// else.</summary>
        public const string SeenPrefKey = "dotr-wardrobe-seen-v1";

        /// <summary>True when the player owns at least one cosmetic. The Hero deck builds
        /// the Wardrobe card ONLY when this is true.</summary>
        public bool WardrobeHasUnlocked { get; }

        /// <summary>True while the section is unlocked but has never been opened since it
        /// arrived - the card carries <see cref="NewWord"/> so the player notices it.</summary>
        public bool WardrobeIsNew { get; }

        /// <summary>How many looks the player owns, as Cosmetics last published it.</summary>
        public int OwnedCount { get; }

        public HeroDeckWardrobeVM(int ownedCount, bool seen)
        {
            OwnedCount = ownedCount < 0 ? 0 : ownedCount;
            WardrobeHasUnlocked = OwnedCount > 0;
            WardrobeIsNew = WardrobeHasUnlocked && !seen;
        }

        public static HeroDeckWardrobeVM FromCurrentState()
        {
            int owned = CosmeticSignals.OwnedCount;
            bool seen = false;
            Guard.Try("HUD", "read wardrobe seen flag", () =>
            {
                seen = PlayerPrefs.GetInt(SeenPrefKey, 0) != 0;
            });
            var vm = new HeroDeckWardrobeVM(owned, seen);
            // Named counts, so a trace can tell data-empty (owned=0) from
            // built-but-invisible without a second run.
            FlowTrace.Step("HUD", "hero deck wardrobe: owned=" + vm.OwnedCount +
                " show=" + vm.WardrobeHasUnlocked + " new=" + vm.WardrobeIsNew);
            return vm;
        }

        /// <summary>The card purpose the View mounts - the badge word is prefixed HERE, so
        /// the View still renders one string it did not compose.</summary>
        public string PurposeWithBadge(string purpose)
        {
            if (!WardrobeIsNew) return purpose;
            // WO-1857: the badged line is ONE authored row with a POSITIONAL hole ({0} = the
            // purpose), never `NewWord + " - " + purpose` - a locale that reads the badge after
            // the phrase, or joins it with something other than " - ", cannot be expressed by a
            // concatenation. NewWord stays the ASCII identity and the no-purpose default, and
            // CosmeticShopReachabilityRegression Case H still probes for it at index 0.
            return string.IsNullOrEmpty(purpose)
                ? LocalText.Get("hud.hero_deck.new_badge")
                : LocalText.Format("hud.hero_deck.purpose_new_badge", purpose);
        }

        /// <summary>Called when the player opens the wardrobe: the badge has done its job.</summary>
        public static void MarkSeen()
        {
            Guard.Try("HUD", "write wardrobe seen flag", () =>
            {
                if (PlayerPrefs.GetInt(SeenPrefKey, 0) != 0) return;
                PlayerPrefs.SetInt(SeenPrefKey, 1);
                PlayerPrefs.Save();
                FlowTrace.Step("HUD", "hero deck wardrobe: NEW cleared (opened)");
            });
        }

        /// <summary>Test seam - suites clear the flag so the NEW case measures a first
        /// arrival and not whatever a previous gate run on this machine left behind.</summary>
        public static void ClearSeenForTests()
        {
            Guard.Try("HUD", "clear wardrobe seen flag", () =>
            {
                PlayerPrefs.DeleteKey(SeenPrefKey);
            });
        }
    }
}
