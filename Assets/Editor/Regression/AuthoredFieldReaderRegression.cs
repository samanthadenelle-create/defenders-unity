// =============================================================================
// AuthoredFieldReaderRegression [authored-field-reader] -- WAVE 0 LANE C seam
// oracle #3 of the family opened by ProgressionReachabilityRegression (WO-1423).
// -----------------------------------------------------------------------------
// AN AUTHORED FIELD NOBODY READS IS A PROMISE NOBODY CAN KEEP. Every authored
// text field in the canonical catalogs is a claim someone made about the game:
// how a cosmetic is unlocked, which hero a quest needs, what curve a ladder
// follows. If NO production code path ever reads that field, the claim is
// decorative -- it is authored, reviewed, shipped and inert, and nothing on
// screen or in any suite says so.
//
// WHY THIS SUITE EXISTS (the class of bug, not one instance): on 2026-09-06 the
// full suite ran 394 green while seven of nine troop types were unreachable.
// Every oracle asked "does this system do its job"; none asked "do these two
// things agree". A field with no reader is that question at its simplest: the
// data side and the code side of one seam, where only one side exists.
//
// -----------------------------------------------------------------------------
// SCOPE -- what this suite reasons about, precisely
// -----------------------------------------------------------------------------
// AUTHORED FIELD = a `[JsonProperty("<key>")] public string <Member>` declared in
// a .cs under Assets/_Modules/ (the production catalog/DTO types). String fields
// only: they are the ones that carry authored PROSE and authored MODE KEYS, which
// is where a claim can hide. Numeric fields are out of scope.
// Measured 2026-09-06: 463 distinct authored string fields.
//
// PRODUCTION READER = any .cs under Assets/_Modules/ that names `.<Member>` on a
// line that is not the declaration itself, with comments and string literals
// stripped first. Reads inside the declaring file COUNT (a catalog that resolves
// its own field is a reader -- that is how DailyQuests.RequiresFeature is read).
//
// ⚠ A READER IN Assets/Editor/ OR Assets/Tests/ IS **NOT** A PRODUCTION READER.
// A regression suite that validates `unlockMethod` is in {buy,achievement}
// (EconomyMetaCatalogRegression.cs:129-138) proves the STRING is well-formed; it
// does not make the game honour it. Fifteen fields are in exactly that state
// today; they are logged as [editor-only-reader] and listed in the baseline, not
// silently ignored.
//
// -----------------------------------------------------------------------------
// WHAT THIS ORACLE CAN AND CANNOT PROVE -- read before trusting a green
// -----------------------------------------------------------------------------
// It CANNOT parse English and does not try. It CANNOT tell a mechanical claim
// from flavour by looking at a field name -- so it does not guess. It asks two
// different, honest questions:
//
//   CASE B  a CURATED registry of fields that DO make a mechanical claim (each
//           entry states the claim and the evidence) must have a production
//           reader. This is judgement, written down and reviewable.
//   CASE C  a RATCHET over everything else: the set of authored fields with no
//           production reader may not GROW. New unread fields fail by name. This
//           is discovery without judgement -- it cannot say a new field matters,
//           only that nobody wired it and nobody said so.
//   CASE E  the INVERSE of Case B, added 2026-09-10: fields an owner RETIRED must
//           stay retired -- no declaration under Assets/_Modules, no key in either
//           canonical json twin. Cases B/C/D all reason about fields that EXIST, so
//           a retired field walking back in is invisible to every one of them.
//
// It does NOT prove:
//   * that a field with a reader is read CORRECTLY, or read on the path the
//     player takes. `effect` on building-tiers.json rows is read by
//     BuildingUpgradeVM.cs:1228/1280 -- for DISPLAY. Read-for-display is not
//     honoured. This suite would call that field read, and it is right to: the
//     barracks announce-copy seam is already pinned by
//     TroopRosterRegression.cs:116-127 (barracks tier `effect` must name its
//     unit) and by that suite's tier-gate cases. NOT DUPLICATED HERE.
//   * anything about non-string authored fields.
//   * that a baseline entry is harmless. The baseline records what was true on
//     2026-09-06; it is a floor to ratchet down, not an approval.
//
// A false NEGATIVE is possible and deliberate: `.<Member>` is matched by NAME, so
// a member read on a DIFFERENT type of the same name counts as a reader here.
// That makes the suite under-report, never over-report -- the safe direction for
// a gate that blocks a ship.
//
// Marker: AUTHORED_FIELD_READER_OK / AUTHORED_FIELD_READER_FAIL <case>.
// STATE 2026-09-10: the five curated Case B fields the suite arrived RED on are all
// resolved. Two were WIRED on 2026-09-09 (WO-1430 lane FIELDS, commit 4f2698b86) and
// their exemptions deleted. The other three -- levelCurve, visibilityRule,
// expiry_behavior -- were RETIRED on 2026-09-10 by owner ruling on WO-1430 fields 3-5
// ("Drop them - remove the dead fields from the catalog; re-add each when a system
// needs it"). ParkedClaims is therefore EMPTY, and Case E below turns the question
// inside out for those three: it no longer asks "does it have a reader", it asserts
// they STAY GONE -- from the declarations AND from the canonical json.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "authored-field-reader suite", () => { if (!DeNelle.Editor.AuthoredFieldReaderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[authored-field-reader] " + r); });
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Seam oracle: an authored field with no production reader is an unkept promise.</summary>
    public static class AuthoredFieldReaderRegression
    {
        private const string Tag = "[authored-field-reader]";

        // Key shape used everywhere below: "Member|jsonKey|path under Assets/_Modules/".
        private const char Sep = '|';

        // =====================================================================
        // CASE B REGISTRY -- fields that make a MECHANICAL claim. Judgement,
        // written down. Each entry states the claim and how it was measured.
        // These are FAILING on the day this suite is written; they are findings
        // for the CLI seat to triage, NOT exceptions.
        // =====================================================================
        // =====================================================================
        // PARKED 2026-09-06 by the CLI seat (five then, THREE on 2026-09-09, ZERO now).
        // Each WAS a real finding - an authored promise no code keeps - and each is
        // written up, with the decision owed, in:
        //   WorkOrders/WORK_ORDER_1430_seam_oracle_findings_three_doorless_panels_and_five_unread_fields.md
        //
        // ⚠ RATCHET, NOT AMNESTY. Case B still FAILS on any mechanical-claim field
        // NOT named here, and Case C still fails on any NEW unread authored field.
        // A parked entry that gains a production reader FAILS LOUDLY below, so the
        // list cannot quietly outlive the findings.
        //
        // RETIRES WHEN: the field gets a production reader, or the field is
        // deliberately retired and the copy implying it is corrected in the same
        // change (CLAUDE.md §15). Delete the entry then - do NOT leave it.
        // =====================================================================
        //
        // ---------------------------------------------------------------------
        // 2026-09-09 (WO-1430 lane FIELDS): TWO OF THE FIVE ARE WIRED AND THEIR
        // ENTRIES ARE DELETED, not amended - `unlockMethod` (now the gate inside
        // CosmeticOwnershipService.GrantAchievement, via
        // CosmeticCatalog.IsAchievementUnlock) and `requiresHero` (now read in
        // DailyQuestService.RollOne beside its sibling requiresFeature). Their
        // UnreadBaseline rows below were deleted in the SAME change: Case C
        // `continue`s on a read field BEFORE consulting the baseline, so a stale
        // baseline row is silently tolerated and nothing would ever remind us.
        //
        // ---------------------------------------------------------------------
        // 2026-09-10 (WO-1430 lane FIELDS-DROP): THE REMAINING THREE ARE RETIRED,
        // NOT WIRED. Owner ruling, verbatim: "Drop them - remove the dead fields
        // from the catalog; re-add each when a system needs it." levelCurve,
        // visibilityRule and expiry_behavior no longer exist as authored client
        // fields, so their MechanicalClaims entries and their UnreadBaseline rows
        // were deleted in the SAME change (Case D would otherwise fire on ghost
        // baseline rows, which is the suite working as designed).
        //
        // ParkedClaims is now EMPTY and stays declared on purpose. The `parked`
        // branch in Case B is the mechanism for the NEXT field that needs an owner
        // ruling; ripping it out would mean rebuilding it from scratch, and an
        // empty set is honest about the current state in a way a deleted one is not.
        // =====================================================================
        private static readonly HashSet<string> ParkedClaims = new HashSet<string>(StringComparer.Ordinal)
        {
            // EMPTY 2026-09-10 -- see the block above. Add a key here ONLY alongside a
            // written-up finding in a WO, never to quiet a failure.
        };

        // =====================================================================
        // CASE E REGISTRY -- fields RETIRED by an owner ruling. The inverse of Case
        // B: these must have NO declaration and NO authored row anywhere. A retired
        // field that comes back is a promise re-made with nothing behind it, and it
        // would come back SILENTLY -- Case C only ratchets fields that are unread,
        // and a re-added field with a reader would sail past every other case here.
        //
        // Entry shape: "<jsonKey>|<why it was retired, and what re-adding it costs>".
        // Matched on the JSON KEY, not on Member|key|path: a re-add under a renamed
        // member, in a different file, or in a different catalog must still fire.
        // =====================================================================
        private static readonly string[][] RetiredFields =
        {
            new[]
            {
                "levelCurve",
                "retired 2026-09-10 (owner ruling, WO-1430 fields 3-5). It named a curve " +
                "EchoBonusCalculator never asked for, so authoring a second name changed nothing. " +
                "Re-adding it re-creates that unkept promise unless the formula that honours it " +
                "lands in the SAME change"
            },
            new[]
            {
                "visibilityRule",
                "retired 2026-09-10 (owner ruling, WO-1430 fields 3-5). The client declared a bare " +
                "string while the server emits a visibility OBJECT, and it was authored on ZERO rows. " +
                "Re-adding it re-opens a shape disagreement between the two sides of one seam"
            },
            new[]
            {
                "expiry_behavior",
                "the CLIENT member was retired 2026-09-10 (owner ruling, WO-1430 fields 3-5): it was " +
                "parse-and-discard, so every expiry behaved the same way whatever the server said. " +
                "⚠ THE SERVER SIDE IS DELIBERATELY UNTOUCHED (api/schema.sql, api/_lib/catalog-read.js, " +
                "api/admin/showcase-finalize.js) and live responses STILL carry the key - it is now an " +
                "ignored unknown member. This case scans Assets/ ONLY; it is not a claim about api/"
            },
        };

        private static readonly string[][] MechanicalClaims =
        {
            new[]
            {
                "UnlockMethod|unlockMethod|Cosmetics/CosmeticCatalog.cs",
                "cosmetics.json authors unlockMethod on 37 rows, EVERY one of them \"achievement\" " +
                "(re-counted at source 2026-09-09 in BOTH canonical twins). That is a claim about HOW " +
                "the item is obtained, and since 2026-09-09 it is a GATE: CosmeticCatalog." +
                "IsAchievementUnlock is the one reader, and CosmeticOwnershipService.GrantAchievement " +
                "REFUSES a row that does not claim it. ⚠ THIS ENTRY'S ORIGINAL TEXT WAS WRONG about " +
                "\"the only code that touches the key\": HUD/CosmeticShopPanel.cs:402 reads it BY " +
                "REFLECTION (the HUD asmdef may not reference DeNelle.Cosmetics) to pick the price " +
                "caption - a reader this scan structurally cannot see, and display-only either way. " +
                "If this case fires again, the GATE was deleted and any milestone caller can hand out " +
                "a purchase-only cosmetic free"
            },
            // "LevelCurve|..." DELETED 2026-09-10 (WO-1430 lane FIELDS-DROP) - RETIRED by owner
            // ruling, not wired. Case B asks "does this field have a reader"; a field that no
            // longer exists cannot be asked that, and leaving the entry would have fired
            // [mechanical-claim-registry-stale] instead. The question it used to ask is now
            // asked in reverse by Case E.
            new[]
            {
                "RequiresHero|requiresHero|Core/Quests/DailyQuests.cs",
                "DailyQuests declares requiresHero beside requiresFeature. requiresFeature IS read " +
                "(in DailyQuests.cs itself) and daily-quests.json authors it (\"raids\" on 2 rows). " +
                "requiresHero had NO reader anywhere until 2026-09-09: the gate was half-built, so the " +
                "day someone authored a hero requirement it would be silently ignored - the WO-1038 " +
                "shape (authored content, no code, no error). It is now read in DailyQuestService." +
                "RollOne through the pure predicate DailyQuestService.HeroRequirementMet, which fails " +
                "CLOSED and warns on an unrecognised hero name. If this case fires again the gate was " +
                "removed and daily-quests.json can once more make a promise the roller ignores"
            },
            // "VisibilityRule|..." and "ExpiryBehavior|..." DELETED 2026-09-10 (WO-1430 lane
            // FIELDS-DROP) - both RETIRED by owner ruling. Same reasoning as the LevelCurve
            // tombstone above; both are now asserted ABSENT by Case E.
        };

        // =====================================================================
        // CASE C BASELINE -- every authored string field that had NO production
        // reader on 2026-09-06 (58 of 463). The ratchet fails on anything NOT in
        // this list, so the pile can shrink but never grow.
        //
        // TO REMOVE AN ENTRY: wire a production reader, then delete the line. The
        // suite FAILS on a baseline entry whose declaration no longer exists
        // (case D), so this list cannot rot into ghosts that quietly excuse new
        // fields.
        // TO ADD AN ENTRY: don't. A new unread authored field is the finding.
        // =====================================================================
        private static readonly string[] UnreadBaseline =
        {
            "ActiveEventName|activeEventName|Core/State/ServerConfig.cs",
            "AmountBaseUnits|amountBaseUnits|Wallet/PurchaseQuoteService.cs",
            "Branch|branch|Village/Talents/HeroTalentCatalog.cs",
            "CdnUrl|cdnUrl|Core/Data/CardCollectionCatalog.cs",
            "ClaimModel|claimModel|Wallet/BattleMonthlyCatalog.cs",
            "EndUtc|endUtc|Wallet/BattleMonthlyCatalog.cs",
            "EventDisplayText|eventDisplayText|Core/State/ServerConfig.cs",
            "EventName|eventName|Core/Analytics/EventTracker.cs",
            // "ExpiryBehavior|..." DELETED 2026-09-10 (WO-1430 lane FIELDS-DROP) - the client
            // member was RETIRED, so Case D would fire on this row as a ghost. Case E asserts it
            // stays gone.
            "ExplorerNote|explorerNote|Wallet/WalletRegistry.cs",
            "ExportedAt|exportedAt|Core/State/SaveSchema.cs",
            "FallbackCollectionId|fallbackCollectionId|Core/Data/CardCollectionCatalog.cs",
            "FallbackSku|fallback_sku|Core/Data/CardCollectionCatalog.cs",
            "FlavorText|flavorText|Village/Troops/Data/BarracksData.cs",
            "Footprint|footprint|Village/Buildings/BuildingCatalog.cs",
            "GlowColor|glowColor|Onboarding/IntroPetCatalog.cs",
            "GlowColor|glowColor|Pets/PetCatalog.cs",
            "GrantedAt|granted_at|Core/Entitlements/SkuEntitlementSnapshot.cs",
            "Holder|holder|Wallet/WalletRegistry.cs",
            "IconCdnUrl|iconCdnUrl|Core/Data/CardCollectionCatalog.cs",
            "IconSha256|iconSha256|Core/Data/CardCollectionCatalog.cs",
            // "LevelCurve|..." DELETED 2026-09-10 (WO-1430 lane FIELDS-DROP) - RETIRED, see above.
            "Lighting|lighting|Core/Data/GarrisonRecipe.cs",
            "MaintenanceMessage|maintenanceMessage|Core/State/ServerConfig.cs",
            "Mint|mint|Wallet/PurchaseQuoteService.cs",
            "PackExclusiveCosmetic|packExclusiveCosmetic|Commerce/PackCatalog.cs",
            "PackSaleLabel|packSaleLabel|Core/State/ServerConfig.cs",
            "ParticleColor|particleColor|Pets/PetCatalog.cs",
            "Perk|perk|Pets/PetCatalog.cs",
            "PerkDescription|perkDescription|Pets/PetCatalog.cs",
            "PremiumPassSku|premiumPassSku|Wallet/BattleMonthlyCatalog.cs",
            "PreviewColor|previewColor|Cosmetics/CosmeticCatalog.cs",
            "PropSet|propSet|Core/World/RealmMapCatalog.cs",
            "Recipient|recipient|Wallet/PurchaseQuoteService.cs",
            "RecipientAta|recipientAta|Wallet/PurchaseQuoteService.cs",
            "RequestedCollectionId|requested_collection_id|Core/Data/CardCollectionCatalog.cs",
            "RequiresFlag|requiresFlag|Core/Quests/QuestCatalog.cs",
            // "RequiresHero|..." DELETED 2026-09-09 (WO-1430 lane FIELDS) - it gained a
            // production reader in DailyQuestService.RollOne. Case C returns early on a
            // read field, so leaving the row would have been tolerated in silence.
            "SafeFallbackItemId|safeFallbackItemId|Core/Data/CardCollectionCatalog.cs",
            "Saga|saga|Village/Crafting/GearCraftingRecipeCatalog.cs",
            "Saga|saga|Village/Crafting/JewelerRecipeCatalog.cs",
            "Sheet|sheet|Core/UI/SpriteSheetSlices.cs",
            "Size|size|Core/Data/GarrisonRecipe.cs",
            "Stack|stack|Commerce/PackCatalog.cs",
            "StartNode|startNode|Core/Dialogue/DialogueModel.cs",
            "StartUtc|startUtc|Wallet/BattleMonthlyCatalog.cs",
            // "UnlockMethod|..." DELETED 2026-09-09 (WO-1430 lane FIELDS) - CosmeticCatalog.
            // IsAchievementUnlock reads it and GrantAchievement gates on it. Same reasoning
            // as the RequiresHero row above: a read field never reaches the baseline check.
            "UpgradeType|upgradeType|Village/Buildings/BuildingCatalog.cs",
            // "VisibilityRule|..." DELETED 2026-09-10 (WO-1430 lane FIELDS-DROP) - RETIRED, see above.
            "archetype|archetype|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "dungeonId|dungeonId|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "facing|facing|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "fromRoomId|fromRoomId|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "keyId|keyId|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "seatMode|seatMode|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "themePalette|themePalette|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "toRoomId|toRoomId|Dungeons/RoomForge/DungeonComposeLayout.cs",
            "visual|visual|Dungeons/RoomForge/DungeonComposeLayout.cs",
        };

        private sealed class Field
        {
            public string Member;
            public string JsonKey;
            public string Rel;        // path under Assets/_Modules/
            public string Key;        // Member|JsonKey|Rel
            public string DeclFile;   // absolute
            public List<string> ProductionReaders = new List<string>();
            public List<string> EditorReaders = new List<string>();
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== AuthoredFieldReaderRegression (Wave 0 Lane C) ===\n");
            try
            {
                CheckAuthoredFieldsHaveReaders(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "AUTHORED_FIELD_READER_OK every curated mechanical-claim field has a production " +
                         "reader, and no authored string field outside the recorded baseline is unread";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "AUTHORED_FIELD_READER_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // =====================================================================
        private static void CheckAuthoredFieldsHaveReaders(List<string> failures, StringBuilder log)
        {
            string assets = Application.dataPath.Replace('\\', '/');
            string modules = assets + "/_Modules";

            if (!Directory.Exists(modules))
            {
                // A MISSING FIXTURE FAILS AND NAMES ITSELF -- never a silent pass.
                failures.Add(Tag + " [authored-field-corpus-present] Assets/_Modules does not exist at '" +
                             modules + "'. The authored-field inventory cannot be built, so no claim about " +
                             "readers can be made. FAIL, not a skip");
                return;
            }

            // ---- corpora ----------------------------------------------------
            var declSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // raw
            var readerBodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // stripped
            var isProduction = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in new[] { "/_Modules", "/Data", "/Editor", "/Tests" })
            {
                string dir = assets + root;
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string norm = f.Replace('\\', '/');
                    string raw = SafeRead(norm);
                    readerBodies[norm] = StripCommentsAndStrings(raw);
                    isProduction[norm] = root == "/_Modules";
                    if (root == "/_Modules") declSources[norm] = raw;
                }
            }

            // ---- 1. the authored-field inventory ----------------------------
            // Matched on RAW source because the JsonProperty key lives inside a string
            // literal -- the very thing the reader scan strips. Two different questions,
            // two different readings, stated so the asymmetry is deliberate.
            var declRx = new Regex("\\[JsonProperty\\(\"([A-Za-z0-9_]+)\"\\)\\]\\s*public\\s+string\\s+([A-Za-z0-9_]+)",
                                   RegexOptions.Compiled);
            var fields = new Dictionary<string, Field>(StringComparer.Ordinal);
            foreach (var kv in declSources)
            {
                string rel = ToModulesRel(kv.Key, modules);
                foreach (Match m in declRx.Matches(kv.Value))
                {
                    string key = m.Groups[2].Value + Sep + m.Groups[1].Value + Sep + rel;
                    if (fields.ContainsKey(key)) continue;   // same field declared twice in one file
                    fields[key] = new Field
                    {
                        Member = m.Groups[2].Value,
                        JsonKey = m.Groups[1].Value,
                        Rel = rel,
                        Key = key,
                        DeclFile = kv.Key,
                    };
                }
            }

            // CASE A  [authored-field-corpus-present]  -- the anti-vacuity floor.
            // Without it a detector regression would report a perfect zero unread fields.
            // REVERT RECIPE (RED): change the declRx pattern's `public\s+string` to
            // `public\s+decimal` -- the inventory collapses and this case fires.
            const int CorpusFloor = 380;
            if (fields.Count < CorpusFloor)
            {
                failures.Add(Tag + " [authored-field-corpus-present] only " + fields.Count + " authored string " +
                             "fields were discovered under Assets/_Modules (floor " + CorpusFloor +
                             ", measured 463 on 2026-09-06). The detector is broken or the catalogs moved; " +
                             "every reader question below would pass vacuously. FAIL, not a skip");
                return;
            }

            // ---- 2. the reader question -------------------------------------
            foreach (var f in fields.Values)
            {
                var pat = new Regex("\\." + Regex.Escape(f.Member) + "\\b", RegexOptions.Compiled);
                foreach (var kv in readerBodies)
                {
                    if (!pat.IsMatch(kv.Value)) continue;
                    bool sameFile = string.Equals(kv.Key, f.DeclFile, StringComparison.OrdinalIgnoreCase);
                    if (sameFile && !HasNonDeclarationHit(kv.Value, pat)) continue;
                    if (isProduction[kv.Key]) f.ProductionReaders.Add(Path.GetFileName(kv.Key));
                    else f.EditorReaders.Add(Path.GetFileName(kv.Key));
                    if (f.ProductionReaders.Count > 0) break;   // one production reader is enough
                }
            }

            // ---- 3. CASE B: the curated mechanical claims --------------------
            // REVERT RECIPE (RED, and the recipe that proves the case GREEN too):
            // in Assets/_Modules/Cosmetics/CosmeticCatalog.cs add a production reader,
            // e.g. `public static bool IsAchievementUnlock(CosmeticDef d) => d != null &&
            // d.UnlockMethod == "achievement";` -- UnlockMethod then has a production
            // reader and drops out of this case. Removing that method restores the RED.
            foreach (var entry in MechanicalClaims)
            {
                string key = entry[0];
                string why = entry[1];
                Field f;
                if (!fields.TryGetValue(key, out f))
                {
                    // The registry must not rot into claims about fields that no longer exist.
                    failures.Add(Tag + " [mechanical-claim-registry-stale] '" + key + "' is registered as a " +
                                 "mechanical-claim field but no such [JsonProperty] string field is declared " +
                                 "under Assets/_Modules any more. Either the field was renamed (update this " +
                                 "entry) or removed (delete it) - a registry entry that matches nothing " +
                                 "silently stops asking its question");
                    continue;
                }
                bool parked = ParkedClaims.Contains(key);
                if (f.ProductionReaders.Count > 0)
                {
                    // A PARKED field that has gained a reader is a finding RESOLVED - say so loudly
                    // rather than passing in silence, so the stale entry gets deleted (WO-1430).
                    if (parked)
                        failures.Add(Tag + " [parked-claim-now-read] '" + key + "' is in ParkedClaims but " +
                                     "NOW HAS a production reader (" + Join(f.ProductionReaders) + "). The " +
                                     "finding is resolved - DELETE its ParkedClaims entry. An exemption that " +
                                     "outlives its finding is the rot this suite exists to prevent");
                    continue;
                }
                if (parked) continue;

                failures.Add(Tag + " [mechanical-field-has-a-reader] Assets/_Modules/" + f.Rel + " authors `" +
                             f.JsonKey + "` (" + f.Member + ") and NO production code reads it" +
                             (f.EditorReaders.Count > 0
                                ? " (read only by " + Join(f.EditorReaders) + ", which validates the string " +
                                  "without making the game honour it)"
                                : "") +
                             ". " + why);
            }

            // ---- 4. CASE C: the ratchet -------------------------------------
            // REVERT RECIPE (RED): add
            // `[JsonProperty("proofField")] public string ProofField;` to any catalog
            // type under Assets/_Modules and read it nowhere -- it is not in the
            // baseline, so this case names it immediately.
            var baseline = new HashSet<string>(UnreadBaseline, StringComparer.Ordinal);
            int unread = 0, editorOnly = 0, newlyUnread = 0;
            foreach (var f in fields.Values)
            {
                if (f.ProductionReaders.Count > 0) continue;
                unread++;
                if (f.EditorReaders.Count > 0) editorOnly++;
                if (baseline.Contains(f.Key)) continue;
                newlyUnread++;
                failures.Add(Tag + " [no-new-unread-authored-field] Assets/_Modules/" + f.Rel + " declares `" +
                             f.JsonKey + "` (" + f.Member + ") and no production code reads it. It is not in " +
                             "the 2026-09-06 baseline, so it is a NEW authored promise with nothing behind it" +
                             (f.EditorReaders.Count > 0
                                ? " (an editor/test reader - " + Join(f.EditorReaders) + " - validates the " +
                                  "string; that is not the game honouring it)"
                                : "") +
                             ". Wire a reader, or if it is genuinely inert say so in the baseline with a reason");
            }

            // ---- 5. CASE D: the baseline may not rot ------------------------
            // A baseline entry whose declaration is gone would silently widen the
            // ratchet's blind spot, exactly like §2's stale WO-number block.
            // REVERT RECIPE (RED): delete the `[JsonProperty("saga")] public string Saga`
            // line from Assets/_Modules/Village/Crafting/JewelerRecipeCatalog.cs.
            foreach (var key in UnreadBaseline)
            {
                if (fields.ContainsKey(key)) continue;
                failures.Add(Tag + " [baseline-entry-still-exists] the unread baseline lists '" + key +
                             "' but no such authored field is declared under Assets/_Modules any more. A " +
                             "baseline of ghosts stops being a floor: delete the line in the same change that " +
                             "removed the field");
            }

            // ---- 5b. CASE E: a RETIRED field stays retired -------------------
            // THE INVERSE QUESTION, and the one nothing else here asks. Cases B, C
            // and D all reason about fields that EXIST; a field an owner deliberately
            // DROPPED could walk back in - declaration re-added, key re-authored -
            // and every one of them would stay silent, because a re-added field WITH
            // a reader is indistinguishable from a healthy field. That is exactly the
            // duplicated-state failure CLAUDE.md §2/§5/§16 each describe: the ruling
            // lives in a WO, the code has no memory of it.
            //
            // Owner ruling 2026-09-10 (WO-1430 fields 3-5), verbatim: "Drop them -
            // remove the dead fields from the catalog; re-add each when a system needs
            // it." The second half is the point: a re-add is LEGITIMATE, but only in
            // the same change as the system that reads it. This case does not forbid
            // the re-add - it forbids the re-add happening QUIETLY. Delete the entry
            // here in the same change and the case goes green by construction.
            //
            // Matched on the JSON KEY, not on the Member|key|path triple, so a re-add
            // under a renamed member or in a different catalog file still fires.
            //
            // REVERT RECIPE (RED): put `[JsonProperty("levelCurve")] public string
            // LevelCurve = "linear";` back into EchoBalanceCatalog.EchoBalanceData, or
            // restore the `"levelCurve": "linear",` line to
            // Assets/Resources/Data/Canonical/echoes-balance.json. Either half fires.
            CheckRetiredFieldsStayRetired(failures, assets, declSources);

            // ---- 6. PRESENCE, so the absence assertions cannot pass vacuously
            // The baseline itself must still be REACHED by the scan. If the corpus
            // suddenly contained none of it, cases C and D would both go quiet.
            // REVERT RECIPE (RED): point the module walk at "/Editor" instead of
            // "/_Modules" -- the baseline stops resolving and this fires.
            int baselineSeen = 0;
            foreach (var key in UnreadBaseline) if (fields.ContainsKey(key)) baselineSeen++;
            if (baselineSeen < UnreadBaseline.Length / 2)
                failures.Add(Tag + " [baseline-entry-still-exists] only " + baselineSeen + " of " +
                             UnreadBaseline.Length + " baseline fields were found in the scanned corpus. The " +
                             "scan is not reading the tree the baseline was measured from, so a green would " +
                             "mean nothing. FAIL, not a skip");

            log.AppendLine("authored string fields: " + fields.Count +
                           "  unread by production: " + unread +
                           " (of which editor/test-only readers: " + editorOnly + ")" +
                           "  new since baseline: " + newlyUnread +
                           "  baseline entries resolved: " + baselineSeen + "/" + UnreadBaseline.Length);
        }

        /// <summary>
        /// CASE E. Every RetiredFields key must be absent from BOTH sides of the seam:
        /// no [JsonProperty("key")] declaration under Assets/_Modules, and no "key" in
        /// either canonical json twin (Resources AND StreamingAssets - the twins are
        /// kept byte-identical, and checking only one would miss a half-revert).
        /// </summary>
        private static void CheckRetiredFieldsStayRetired(
            List<string> failures, string assets, Dictionary<string, string> declSources)
        {
            // ---- the json corpus, read ONCE ---------------------------------
            var jsonBodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in new[] { "/Resources/Data/Canonical", "/StreamingAssets/Data/Canonical" })
            {
                string dir = assets + rel;
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                    jsonBodies[f.Replace('\\', '/')] = SafeRead(f);
            }

            // ANTI-VACUITY FLOOR, fail-not-skip. Without it a moved/renamed catalog
            // directory would make every absence assertion below pass on an EMPTY
            // corpus - the detector reporting a perfect green because it read nothing.
            // REVERT RECIPE (RED): point the two rel paths at "/Resources/Data/Nowhere".
            const int JsonCorpusFloor = 20;
            if (jsonBodies.Count < JsonCorpusFloor)
            {
                failures.Add(Tag + " [retired-field-stays-retired] only " + jsonBodies.Count +
                             " canonical json files were read under Assets/Resources/Data/Canonical + " +
                             "Assets/StreamingAssets/Data/Canonical (floor " + JsonCorpusFloor + "). The " +
                             "authored side of this case cannot be judged, so a green would mean nothing. " +
                             "FAIL, not a skip");
                return;
            }

            foreach (var entry in RetiredFields)
            {
                string key = entry[0];
                string why = entry[1];

                // (a) the DECLARATION side. Raw source, same reason declRx is raw: the
                // key lives inside a string literal.
                // ⚠ DELIBERATELY LOOSER THAN THE INVENTORY'S declRx: no `)]` tail and no
                // `public string` requirement. For an ABSENCE assertion, OVER-matching is
                // the safe direction - a re-add as
                // `[JsonProperty("levelCurve", Required = Required.Default)]`, or as a
                // non-string member, must still fire. The inventory regex may under-report
                // (its false negatives are safe there); this one may not.
                var declRx = new Regex("\\[JsonProperty\\(\\s*\"" + Regex.Escape(key) + "\"",
                                       RegexOptions.Compiled);
                foreach (var kv in declSources)
                {
                    if (!declRx.IsMatch(kv.Value)) continue;
                    failures.Add(Tag + " [retired-field-stays-retired] '" + key + "' is declared again in " +
                                 kv.Key + ", but it was " + why + ". If a system now NEEDS it, the owner's " +
                                 "ruling allows the re-add - land the reader in the SAME change and delete " +
                                 "this key from RetiredFields, so the re-add is recorded rather than silent");
                }

                // (b) the AUTHORED side. A key can come back in the data with no
                // declaration at all (a hand-edited catalog row), and that is just as
                // much a promise nothing keeps.
                var jsonRx = new Regex("\"" + Regex.Escape(key) + "\"\\s*:", RegexOptions.Compiled);
                foreach (var kv in jsonBodies)
                {
                    if (!jsonRx.IsMatch(kv.Value)) continue;
                    failures.Add(Tag + " [retired-field-stays-retired] '" + key + "' is authored again in " +
                                 kv.Key + ", but it was " + why + ". An authored key with no member and no " +
                                 "reader is the WO-1038 shape: authored content, no code, no error");
                }
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>A hit on a line that is not the field's own declaration.</summary>
        private static bool HasNonDeclarationHit(string strippedBody, Regex pat)
        {
            foreach (var line in strippedBody.Split('\n'))
            {
                if (!pat.IsMatch(line)) continue;
                // The declaration survives stripping as `[JsonProperty( )] public string X;`
                // (the key is a literal and is blanked), so the attribute is the tell.
                if (line.IndexOf("JsonProperty", StringComparison.Ordinal) >= 0) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes // line comments, /* */ block comments and "string literals". Comments are
        /// stripped because this repo leaves tombstone prose naming retired members; literals
        /// are stripped because a field name inside a Debug.Log is not a reader.
        /// </summary>
        private static string StripCommentsAndStrings(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i += 2;
                }
                else if (c == '"')
                {
                    i++;
                    while (i < n && src[i] != '"')
                    {
                        if (src[i] == '\\') i++;
                        if (i < n && src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return string.Empty; }
        }

        private static string ToModulesRel(string abs, string modules)
        {
            return abs.StartsWith(modules, StringComparison.OrdinalIgnoreCase)
                ? abs.Substring(modules.Length).TrimStart('/')
                : abs;
        }

        private static string Join(List<string> items)
        {
            if (items == null || items.Count == 0) return "none";
            var uniq = new List<string>();
            foreach (var s in items) if (!uniq.Contains(s)) uniq.Add(s);
            uniq.Sort(StringComparer.Ordinal);
            if (uniq.Count > 4) uniq.RemoveRange(4, uniq.Count - 4);
            return string.Join(",", uniq.ToArray());
        }
    }
}
