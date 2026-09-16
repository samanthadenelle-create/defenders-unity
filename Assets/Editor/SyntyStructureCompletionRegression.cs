// WO-1291: standalone non-mutating safety oracle. Same Editor assembly as the importer;
// EditorRegression cannot reference Editor without creating an assembly cycle.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class SyntyStructureCompletionRegression
    {
        public static void RunAll()
        {
            try
            {
                var before = new Dictionary<string, string[]>
                {
                    { "Structures/Watermill_Medieval", new[] { "old-body", "approved-wrapper" } },
                    { "Structures/GenericContainer", new[] { "old-container" } },
                    { "Structures/ArcaneTower_Albedo", new[] { "texture-guid" } },
                    { "Structures/Tower_Wooden_Watchtower", new[] { "owner-tripo" } },
                    { "Structures/Synty_Tower_Castle_Wall_S", new[] { "honest-stone" } }
                };
                // Owner 2026-09-13: these original Tripo storefronts cannot be claimed by completion.
                var originalStorefronts = new[]
                {
                    "PetHouse2", "arcane tower", "Forge", "armorer", "store",
                    "jeweler", "lumbermill", "farm", "barracks"
                };
                foreach (string leaf in originalStorefronts)
                    before.Add("Structures/" + leaf, new[] { "original-tripo-" + leaf });
                var after = new Dictionary<string, string[]>(before)
                {
                    ["Structures/Watermill_Medieval"] = new[] { "approved-wrapper" },
                    ["Structures/GenericContainer"] = new[] { "new-approved-wrapper" }
                };
                var owned = new Dictionary<string, string>
                {
                    { "Structures/Watermill_Medieval", "approved-wrapper" },
                    { "Structures/GenericContainer", "new-approved-wrapper" }
                };
                SyntyStructureRetheme.AssertCompletionResolution(before, after, owned);
                int rejected = 0;
                ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, before, owned), ref rejected);
                var wrong = new Dictionary<string, string[]>(after) { ["Structures/Watermill_Medieval"] = new[] { "old-body" } };
                ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, wrong, owned), ref rejected);
                foreach (string preserved in new[] { "Structures/ArcaneTower_Albedo", "Structures/Tower_Wooden_Watchtower", "Structures/Synty_Tower_Castle_Wall_S" })
                {
                    var damaged = new Dictionary<string, string[]>(after) { [preserved] = new[] { "replacement" } };
                    ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, damaged, owned), ref rejected);
                }
                var lost = new Dictionary<string, string[]>(after);
                lost.Remove("Structures/ArcaneTower_Albedo");
                ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, lost, owned), ref rejected);
                var added = new Dictionary<string, string[]>(after) { ["Structures/new-key"] = new[] { "new-guid" } };
                ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, added, owned), ref rejected);
                var stolen = new Dictionary<string, string>(owned) { ["Structures/Tower_Wooden_Watchtower"] = "owner-tripo" };
                ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, after, stolen), ref rejected);
                var unknown = new Dictionary<string, string>(owned) { ["Structures/unknown"] = "anything" };
                ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, after, unknown), ref rejected);
                foreach (string leaf in originalStorefronts)
                {
                    string key = "Structures/" + leaf;
                    var replaced = new Dictionary<string, string[]>(after) { [key] = new[] { "unapproved-synty" } };
                    ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, replaced, owned), ref rejected);
                    // Both sides agree on the replacement, so only the protected-ownership rule
                    // can reject this attempted expansion of completion's authority.
                    var takeover = new Dictionary<string, string>(owned) { [key] = "unapproved-synty" };
                    ExpectRejected(() => SyntyStructureRetheme.AssertCompletionResolution(before, replaced, takeover), ref rejected);
                }
                Debug.Log("STRUCTURE_COMPLETION_SAFETY_OK legal resolution accepted; rejected " + rejected + " destructive mutations; no assets changed");
            }
            catch (Exception ex)
            {
                Debug.LogError("STRUCTURE_COMPLETION_SAFETY_FAIL " + ex.Message);
            }
        }

        private static void ExpectRejected(Action action, ref int rejected)
        {
            try { action(); }
            catch (InvalidOperationException) { rejected++; return; }
            throw new Exception("unsafe address mutation escaped the completion postcondition");
        }
    }
}
