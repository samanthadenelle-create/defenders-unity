// =============================================================================
// BreakableContainerChestRegression [chest] (WO-1132) - the loot chest is OPENED,
// never attacked.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers: CHEST_OK / CHEST_FAIL.
//
// THE ORACLE FOR WO-1132. Owner ruling 2026-08-21: "can we make it a chest?" /
// "open chest" / "not attackable item" / "can only open outside of combat" /
// "prevents player from trying to run in collect and go".
//
// DeNelle.Village.BreakableContainer used to be a STATIC HOSTILE: it implemented
// IDamageable + IDamageableStructure, declared Faction => Hostile, and rewrote its
// own layer to "Enemy" so the hero's enemy-mask OverlapSphere would find it and
// TakeDamage() it. That is exactly how a dungeon prop came to register as a HOSTILE
// target (WO-1047). WO-1132 did not FILTER that defect - it deleted the concern, so
// the ambiguity stops existing. Every assertion below is one careless "restore the
// smash" edit away from bringing the defect class back.
//
//   1 [chest-not-hostile]  By REFLECTION on the loaded type: BreakableContainer
//       implements NEITHER IDamageable NOR IDamageableStructure, and exposes NO
//       member named Faction / TakeDamage / ApplyContactDamage / ApplyStatus. This
//       is the CORE of the ruling. Re-adding any one of them re-creates WO-1047:
//       the hero's hostile sweep acquires a crate, the reticle locks onto furniture
//       and the combat camera frames a barrel.
//
//   2 [chest-not-enemy-layer]  SOURCE-LINT: the file contains no ASSIGNMENT that puts
//       the object ON the Enemy layer, it still carries the migration-OFF line
//       (host.layer = 0) that corrects every ALREADY-BAKED scene with no re-bake, and
//       the token CombatFaction does not appear in code. NOTE the lint is deliberately
//       for an ASSIGNMENT, not a MENTION: the file legitimately resolves
//       LayerMask.NameToLayer("Enemy") in order to COMPARE against it and migrate off
//       it. A mention-based lint here would ban the fix itself. The CombatFaction lint is
//       likewise on CODE with comments stripped: the header legitimately quotes the retired
//       "Faction => CombatFaction.Hostile" line while explaining why it must not come back.
//
//   3 [chest-combat-gate]  SOURCE-LINT (comments stripped): the out-of-combat gate is
//       BattleLock.IsInBattle(), the single sanctioned combat-state authority, and no
//       competing authority (WaveManager / ATBCombatManager / HudPosture /
//       AmbientNPC.IsCombatActive) is consulted. IsInBattle must appear at least TWICE
//       in code - once on the prompt path and again inside Open() - so a tap that lands
//       on the frame combat STARTS cannot slip through the gate. (The comment strip
//       matters: the header prose legitimately says composed scenes "carry no
//       WaveManager", which a raw substring lint would read as a second authority.)
//
//   4 [chest-refusal-has-words]  A refused open is NEVER a dead tap. The semantic
//       interaction.chest.* keys live in ChestInteractionText as LocalizedText values,
//       resolve from both English locale mirrors, and no longer remain duplicated in
//       legacy canon-strings.json. BreakableContainer must use the same localized
//       blocked sentence for its prompt and toast.
//
//   5 [chest-drop-survives]  The loot lane is UNCHANGED by the redesign - that is the
//       whole point of it. The file still calls ItemDropSystem.RollLines and
//       ItemPickupSpawner.Spawn (the world-mote path) AND ItemDropSystem.RollAndDeposit
//       (the fallback that keeps the open PAID when pickups are off or the roll was
//       empty), and still exposes the public LootTableId property that every baker sets.
//
//   6 [chest-create-signature]  BreakableContainer.Create must still be a PUBLIC STATIC
//       method taking exactly (Transform, Vector3, string, string) and returning
//       BreakableContainer, and the class must still be NAMED BreakableContainer.
//       DungeonBaker.PlaceComposeChests and DungeonChainBuilder invoke Create by
//       REFLECTION, so a rename or an arity change does not fail at COMPILE - it
//       silently stops placing chests in every composed dungeon and shows up only as a
//       bake-time warning nobody reads. And every baked .unity on disk references the
//       component by class name, so a rename orphans it everywhere.
//
//   7 [hostile-admit-instrumentation-intact]  WO-1047's instrumentation must SURVIVE
//       (CLAUDE.md sec.12: instrumentation is PERMANENT, never stripped as "cleanup").
//       HeroTargetIndicator.cs must still carry the literal [hostile-admit] with BOTH
//       branches intact: the ENEMY FlowTrace.Step branch and the NON-ENEMY ADMITTED
//       FlowTrace.Warn branch. After WO-1132 the non-enemy branch should record ZERO
//       admissions at runtime - that silence IS the proof the defect class is gone, and
//       deleting the code deletes the only way to observe it.
//
//   8 [hostile-admit-routes-through-faction-rules]  WO-1458, and a HARD FAIL. The reticle's
//       admit rule must call CombatFactionRules.MayAttack and must carry ZERO inline
//       `Faction != CombatFaction.Hostile` copies, and TraceAdmission must classify by
//       faction (IsFriendlyFire), not by "does it carry an Enemy component?". The old
//       classifier turned every legitimately-hostile raid wall and the RaidSpire crown into
//       a NON-ENEMY ADMITTED warning - 320 of them in one device session - which is how
//       WO-1047's guard became noise nobody acted on. The layers are NOT the bug: the crown
//       is on Enemy because RaidSpire.EnsureHittable puts it there so the hero's masked
//       sweep can reach the win condition, and the walls are on Structure by design.
//
// WHAT THIS SUITE DELIBERATELY DOES NOT ASSERT:
//   * That an open actually PAYS at runtime. ItemDropSystem rolls against loot-tables.json
//     and deposits into the larder; ItemDropSystemRegression owns that contract. This
//     suite pins only that the chest still CALLS it.
//   * The lid-swing presentation. Silhouette-over-hue is a felt/screenshot check.
//
// OVERLAP NOTE: StructureTargetableRegression [faction-derived] carries an
// ExpectedImplementors ratchet of IDamageable types, and BreakableContainer was REMOVED
// from it by WO-1132 on purpose. Case 1 here asserts the OPPOSITE of that ratchet - if
// BreakableContainer is ever re-added there, the two suites deadlock against each other,
// which is the intended alarm.
//
// Standalone: run-unity-method
//   -Method DeNelle.Editor.Regression.BreakableContainerChestRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Editor.Regression
{
    public static class BreakableContainerChestRegression
    {
        private const string Sys = "ChestRegression";

        private const string ChestTypeName = "DeNelle.Village.BreakableContainer";
        private const string DamageableName = "DeNelle.Core.Combat.IDamageable";
        private const string DamageableStructName = "DeNelle.Core.Combat.IDamageableStructure";

        // Relative to Application.dataPath (never a hardcoded drive letter - the repo root
        // is machine-dependent, CLAUDE.md sec.0).
        private const string ChestSrcRel = "_Modules/Village/World/BreakableContainer.cs";
        private const string ChestTextSrcRel = "_Modules/Village/World/ChestInteractionText.cs";
        private const string ReticleSrcRel = "_Modules/Village/Hero/HeroTargetIndicator.cs";
        private const string CanonResRel = "Resources/Data/Canonical/canon-strings.json";
        private const string CanonSaRel = "StreamingAssets/Data/Canonical/canon-strings.json";
        private const string LocaleResRel = "Resources/Data/Canonical/en.json";
        private const string LocaleSaRel = "StreamingAssets/Data/Canonical/en.json";

        // The combat-state authorities that are NOT allowed to appear in this file. One
        // authority or the gate disagrees with itself somewhere.
        private static readonly string[] CompetingAuthorities =
        {
            "WaveManager",
            "ATBCombatManager",
            "HudPosture",
            "AmbientNPC.IsCombatActive",
        };

        // The members that made the chest a hostile. Any of them returning is WO-1047.
        private static readonly string[] BannedMembers =
        {
            "Faction",
            "TakeDamage",
            "ApplyContactDamage",
            "ApplyStatus",
        };

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("CHEST_OK - " + reason);
            else Debug.LogError("CHEST_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            int cases = 0;
            int passed = 0;

            try
            {
                cases++; if (Case(failures, "chest-not-hostile", () => Case1_NotHostile(failures, notes))) passed++;
                cases++; if (Case(failures, "chest-not-enemy-layer", () => Case2_NotEnemyLayer(failures, notes))) passed++;
                cases++; if (Case(failures, "chest-combat-gate", () => Case3_CombatGate(failures, notes))) passed++;
                cases++; if (Case(failures, "chest-refusal-has-words", () => Case4_RefusalHasWords(failures, notes))) passed++;
                cases++; if (Case(failures, "chest-drop-survives", () => Case5_DropSurvives(failures, notes))) passed++;
                cases++; if (Case(failures, "chest-create-signature", () => Case6_CreateSignature(failures, notes))) passed++;
                cases++; if (Case(failures, "hostile-admit-instrumentation-intact", () => Case7_InstrumentationIntact(failures, notes))) passed++;
                cases++; if (Case(failures, "hostile-admit-routes-through-faction-rules", () => Case8_AdmitRoutesThroughFactionRules(failures, notes))) passed++;
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";

            if (failures.Count == 0)
            {
                reason = passed + "/" + cases + " cases - BreakableContainer is an OPENABLE chest: it implements " +
                         "neither damage contract and declares no Faction/TakeDamage/ApplyContactDamage/ApplyStatus, " +
                         "never assigns itself the Enemy layer (and still migrates baked scenes OFF it), gates on " +
                         "BattleLock.IsInBattle twice with no competing authority, refuses in real canon words that " +
                         "exist ASCII-clean and identical in BOTH canon-strings.json copies, still rolls and pays " +
                         "through the unchanged loot lane, keeps the reflection-invoked Create(Transform,Vector3," +
                         "string,string) signature, WO-1047's [hostile-admit] instrumentation is intact, and " +
                         "WO-1458's admit rule routes through CombatFactionRules with no inline faction copy" + noteStr;
                return true;
            }

            reason = "chest FAIL x" + failures.Count + " (" + passed + "/" + cases + " cases clean): " +
                     string.Join(" | ", failures) + noteStr;
            return false;
        }

        /// <summary>Runs one case; returns true when it added no failures and did not throw.</summary>
        private static bool Case(List<string> failures, string name, Action body)
        {
            int before = failures.Count;
            try { body(); }
            catch (Exception ex)
            {
                failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            return failures.Count == before;
        }

        // =====================================================================
        //  CASE 1 - the chest is not a combat object at all.
        // =====================================================================
        private static void Case1_NotHostile(List<string> failures, List<string> notes)
        {
            var chest = FindType(ChestTypeName);
            if (chest == null)
            {
                failures.Add("[chest-not-hostile] the loaded type " + ChestTypeName + " was not found. Either " +
                             "DeNelle.Village is not loaded or the class was RENAMED - and the class name is " +
                             "load-bearing: every composed dungeon and KayKit outpost on disk baked this component " +
                             "into its .unity by class name, so a rename orphans the chest in every scene in the tree.");
                return;
            }

            var iDamageable = FindType(DamageableName);
            var iStructure = FindType(DamageableStructName);

            if (iDamageable == null)
                notes.Add(DamageableName + " not found in any loaded assembly - the interface check was skipped " +
                          "(the member lint below still stands)");
            else if (iDamageable.IsAssignableFrom(chest))
                failures.Add("[chest-not-hostile] BreakableContainer implements IDamageable again. That is the seam the " +
                             "PLAYER, the hero's abilities, troops and pets SEARCH through, so the chest becomes a valid " +
                             "hostile TARGET - the reticle locks onto furniture and the combat camera frames a crate. " +
                             "That is WO-1047 verbatim, and WO-1132 removed the concern instead of filtering it. Do NOT " +
                             "re-add it and then exclude it in HeroTargetIndicator: that is the inferior fix this ruling " +
                             "replaced.");

            if (iStructure == null)
                notes.Add(DamageableStructName + " not found in any loaded assembly - the interface check was skipped");
            else if (iStructure.IsAssignableFrom(chest))
                failures.Add("[chest-not-hostile] BreakableContainer implements IDamageableStructure again. That is the " +
                             "seam ENEMIES acquire through (Enemy.SweepForNearestStructure / ProbeForStructureForward), " +
                             "so hollows would path to a crate and siege it instead of hunting the hero. A chest is " +
                             "furniture on BOTH sides of the fight.");

            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

            foreach (var banned in BannedMembers)
            {
                var hits = new List<string>();
                foreach (var m in chest.GetMembers(all))
                {
                    if (m == null) continue;
                    // Explicit interface implementations carry a dotted prefix - catch those too.
                    string n = m.Name;
                    int dot = n.LastIndexOf('.');
                    string simple = dot >= 0 ? n.Substring(dot + 1) : n;
                    if (!string.Equals(simple, banned, StringComparison.Ordinal)) continue;
                    if (m.DeclaringType == typeof(object) || m.DeclaringType == typeof(MonoBehaviour) ||
                        m.DeclaringType == typeof(Behaviour) || m.DeclaringType == typeof(Component) ||
                        m.DeclaringType == typeof(UnityEngine.Object)) continue;
                    hits.Add(m.MemberType + " " + n);
                }

                if (hits.Count == 0) continue;

                failures.Add("[chest-not-hostile] BreakableContainer exposes '" + banned + "' again (" +
                             string.Join(", ", hits) + "). WO-1132 removed the 'may the hero damage this?' concern " +
                             "outright so that 'is this a thing to lock onto?' stops being ambiguous. Re-introducing " +
                             banned + " puts the chest back on the combat surface even if nothing calls it today - the " +
                             "next sweep that searches by member or by contract will find it.");
            }
        }

        // =====================================================================
        //  CASE 2 - never assigned the Enemy layer, still migrates OFF it.
        // =====================================================================
        private static void Case2_NotEnemyLayer(List<string> failures, List<string> notes)
        {
            string src = ReadSource(ChestSrcRel, "chest-not-enemy-layer", failures);
            if (src == null) return;

            string code = StripComments(src);

            // An ASSIGNMENT of the Enemy layer, in either shape:
            //   <x>.layer = LayerMask.NameToLayer("Enemy");
            //   <x>.layer = enemyLayer;            (a local holding the resolved index)
            // Deliberately NOT a lint on the MENTION: the file resolves NameToLayer("Enemy")
            // in order to COMPARE (host.layer == enemyLayer) and migrate off it, which is the
            // fix itself. Banning the mention would ban the fix.
            var directAssign = new Regex(@"\.layer\s*=\s*[^;\r\n]*NameToLayer\s*\(\s*""Enemy""", RegexOptions.Compiled);
            var viaLocal = new Regex(@"\.layer\s*=\s*\w*[eE]nemy\w*\s*;", RegexOptions.Compiled);

            if (directAssign.IsMatch(code))
                failures.Add("[chest-not-enemy-layer] BreakableContainer.cs ASSIGNS the object to LayerMask." +
                             "NameToLayer(\"Enemy\"). That single line is the whole WO-1047 mechanism: the Enemy layer " +
                             "is what the hero's enemy-mask OverlapSphere searches, so a chest on it is admitted to the " +
                             "hostile target set no matter what interfaces it does or does not implement. Chests are " +
                             "furniture - layer 0.");

            if (viaLocal.IsMatch(code))
                failures.Add("[chest-not-enemy-layer] BreakableContainer.cs assigns .layer from a local named for the " +
                             "Enemy layer (the 'host.layer = enemyLayer' shape). Same defect as the direct assignment, " +
                             "one variable removed - the chest lands back on the hostile-search layer.");

            if (!new Regex(@"\.layer\s*=\s*0\s*;", RegexOptions.Compiled).IsMatch(code))
                failures.Add("[chest-not-enemy-layer] the migration-off line (host.layer = 0) is GONE. Chests are placed " +
                             "at BAKE time and saved into the .unity, so every already-baked dungeon on disk still " +
                             "carries layer=Enemy from the old Create(). That one runtime normalisation is what retires " +
                             "the defect on existing content WITHOUT a re-bake of every composed scene - drop it and " +
                             "the fix only applies to chests baked after today.");

            // CODE, not prose: the header legitimately explains that the chest USED to declare
            // "Faction => CombatFaction.Hostile" and why that was removed. Banning the token in
            // the comments would delete the one paragraph that stops someone restoring it.
            if (code.IndexOf("CombatFaction", StringComparison.Ordinal) >= 0)
                failures.Add("[chest-not-enemy-layer] the token 'CombatFaction' appears in BreakableContainer.cs CODE. A chest " +
                             "has no allegiance: declaring one - even as an unused leftover - is " +
                             "the first half of putting it back on the combat surface, and Faction is the ONLY thing " +
                             "standing between a target sweep and a piece of furniture.");
        }

        // =====================================================================
        //  CASE 3 - ONE combat authority, checked on BOTH paths.
        // =====================================================================
        private static void Case3_CombatGate(List<string> failures, List<string> notes)
        {
            string src = ReadSource(ChestSrcRel, "chest-combat-gate", failures);
            if (src == null) return;

            string code = StripComments(src);

            if (code.IndexOf("BattleLock.IsInBattle", StringComparison.Ordinal) < 0)
                failures.Add("[chest-combat-gate] BreakableContainer.cs no longer calls BattleLock.IsInBattle(). That is " +
                             "THE combat-state authority and the only one sanctioned here (it is live in a composed " +
                             "dungeon: those scenes stage no BattleArena, but HeroCombatEngagement raises a BattleLock " +
                             "probe from every hero-aggro hollow). Without it the owner ruling 'can only open outside of " +
                             "combat' is not implemented at all, and looting rewards sprinting past a room instead of " +
                             "clearing it.");

            int gateCount = CountOccurrences(code, "IsInBattle");
            if (gateCount < 2)
                failures.Add("[chest-combat-gate] IsInBattle appears " + gateCount + " time(s) in CODE (expected at least " +
                             "2). The gate must be checked on the PROMPT path AND again inside Open(): a tap that lands " +
                             "on the very frame combat starts would otherwise slip through, which is exactly the " +
                             "'run in, collect and go' the ruling exists to prevent. A single check makes the refusal " +
                             "cosmetic.");

            foreach (var rival in CompetingAuthorities)
            {
                if (code.IndexOf(rival, StringComparison.Ordinal) < 0) continue;
                failures.Add("[chest-combat-gate] BreakableContainer.cs consults '" + rival + "'. There is exactly ONE " +
                             "combat-state authority (BattleLock) and a second one guarantees the two disagree " +
                             "somewhere - the prompt would say 'not while enemies are near' while the open path allows " +
                             "it, or the reverse. The HUD's hostile(activebattle) posture in particular is a laggy " +
                             "0.20s-poll derivative of this same lock, and it lives in DeNelle.HUD which DeNelle.Village " +
                             "may not reference (CLAUDE.md sec.5).");
            }
        }

        // =====================================================================
        //  CASE 4 - both states resolve through the localization authority.
        // =====================================================================
        private static void Case4_RefusalHasWords(List<string> failures, List<string> notes)
        {
            var textType = FindType("DeNelle.Village.ChestInteractionText");
            if (textType == null)
            {
                failures.Add("[chest-refusal-has-words] ChestInteractionText not found - the chest has no typed " +
                             "localization catalog for its prompt and refusal.");
                return;
            }

            string refusalKey = ReadConst(textType, "KeyBlockedByEnemies", failures);
            string promptKey = ReadConst(textType, "KeyOpen", failures);

            if (refusalKey != null && !string.Equals(refusalKey, "interaction.chest.blockedByEnemies", StringComparison.Ordinal))
                failures.Add("[chest-refusal-has-words] KeyBlockedByEnemies is '" + refusalKey +
                             "', not the semantic interaction.chest.blockedByEnemies key.");

            if (promptKey != null && !string.Equals(promptKey, "interaction.chest.open", StringComparison.Ordinal))
                failures.Add("[chest-refusal-has-words] KeyOpen is '" + promptKey +
                             "', not the semantic interaction.chest.open key.");

            var res = LoadCanon(LocaleResRel, "Resources English locale", failures);
            var sa = LoadCanon(LocaleSaRel, "StreamingAssets English locale", failures);

            foreach (var key in new[]
                     {
                         refusalKey ?? "interaction.chest.blockedByEnemies",
                         promptKey ?? "interaction.chest.open",
                     })
            {
                string a = Lookup(res, key);
                string b = Lookup(sa, key);

                if (res != null && string.IsNullOrEmpty(a))
                    failures.Add("[chest-refusal-has-words] localized key '" + key + "' is missing or empty in the " +
                                 "Resources English catalog.");

                if (sa != null && string.IsNullOrEmpty(b))
                    failures.Add("[chest-refusal-has-words] localized key '" + key + "' is missing or empty in the " +
                                 "StreamingAssets English catalog.");

                if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && !string.Equals(a, b, StringComparison.Ordinal))
                    failures.Add("[chest-refusal-has-words] localized key '" + key + "' drifted between the dual copies: " +
                                 "Resources says \"" + a + "\" and StreamingAssets says \"" + b + "\". The editor and the " +
                                 "shipped player would tell the player two different things about the same chest.");
            }

            string src = ReadSource(ChestSrcRel, "chest-refusal-has-words", failures);
            if (src == null) return;
            string code = StripComments(src);
            if (code.IndexOf("VillageStrings.Canon", StringComparison.Ordinal) >= 0)
                failures.Add("[chest-refusal-has-words] BreakableContainer still resolves player copy through the legacy " +
                             "VillageStrings.Canon reader.");
            if (CountOccurrences(code, "ChestInteractionText.BlockedByEnemies.Resolve()") < 2)
                failures.Add("[chest-refusal-has-words] the localized blocked sentence must be used by both the prompt " +
                             "and refusal toast path.");
            if (code.IndexOf("ChestInteractionText.Open.Resolve()", StringComparison.Ordinal) < 0)
                failures.Add("[chest-refusal-has-words] the open prompt does not resolve through ChestInteractionText.");
            if (code.IndexOf("ShowToast", StringComparison.Ordinal) < 0)
                failures.Add("[chest-refusal-has-words] BreakableContainer.cs no longer calls ShowToast. The refusal " +
                             "sentence exists in the locale table but never reaches the SCREEN, so a refused tap becomes silent - " +
                             "and a silent tap reads as a broken button, which is the one outcome the ruling names.");

            string textSrc = ReadSource(ChestTextSrcRel, "chest-refusal-has-words", failures);
            if (textSrc != null && CountOccurrences(StripComments(textSrc), "new LocalizedText(") != 2)
                failures.Add("[chest-refusal-has-words] ChestInteractionText must expose exactly two LocalizedText values.");

            var canonRes = LoadCanon(CanonResRel, "legacy Resources canon", failures);
            var canonSa = LoadCanon(CanonSaRel, "legacy StreamingAssets canon", failures);
            foreach (string oldKey in new[] { "chestOpenPrompt", "chestCombatRefusal" })
            {
                if (Lookup(canonRes, oldKey) != null || Lookup(canonSa, oldKey) != null)
                    failures.Add("[chest-refusal-has-words] legacy key '" + oldKey +
                                 "' still duplicates localized chest copy in canon-strings.json.");
            }
        }

        // =====================================================================
        //  CASE 5 - the loot lane is untouched by the redesign.
        // =====================================================================
        private static void Case5_DropSurvives(List<string> failures, List<string> notes)
        {
            string src = ReadSource(ChestSrcRel, "chest-drop-survives", failures);
            if (src == null) return;

            string code = StripComments(src);

            if (code.IndexOf("ItemDropSystem.RollLines", StringComparison.Ordinal) < 0)
                failures.Add("[chest-drop-survives] ItemDropSystem.RollLines is no longer called. The whole point of " +
                             "WO-1132 is that only the INTERACTION changed - the chest still rolls its table. Without the " +
                             "roll the chest opens and pays nothing, which is worse than the hostile crate it replaced.");

            if (code.IndexOf("ItemPickupSpawner.Spawn", StringComparison.Ordinal) < 0)
                failures.Add("[chest-drop-survives] ItemPickupSpawner.Spawn is no longer called - the world pickup mote " +
                             "(walk-over to collect) is gone, so an opened chest gives the player nothing to see and " +
                             "nothing to walk to.");

            // ⚠ CORRECTED 2026-08-22. This case used to demand a literal
            // "ItemDropSystem.RollAndDeposit" call, and it went RED against BETTER code.
            //
            // RollAndDeposit rolls the table a SECOND time. The chest now captures ONE roll
            // (RollLines) and routes that same List down either delivery path - a world mote
            // when pickups are on and the roll produced lines, DepositLines otherwise. So the
            // player is always paid EXACTLY what was rolled, which is the property that
            // actually matters and which the old assertion could not express.
            //
            // The lesson, and the reason this comment is long: the assertion named a METHOD
            // instead of the BEHAVIOUR, so improving the implementation broke the oracle. Pin
            // "the open is always paid from the one captured roll" -- never a call signature.
            bool depositsCapturedRoll = code.IndexOf("DepositLines", StringComparison.Ordinal) >= 0;
            bool rollsAndDeposits     = code.IndexOf("ItemDropSystem.RollAndDeposit", StringComparison.Ordinal) >= 0;
            if (!depositsCapturedRoll && !rollsAndDeposits)
                failures.Add("[chest-drop-survives] the chest has NO deposit fallback. Neither " +
                             "ItemDropSystem.DepositLines (the captured-roll path) nor RollAndDeposit " +
                             "(the older re-roll path) is called, so when world pickups are disabled or " +
                             "the roll produced no lines, opening a chest silently swallows the reward " +
                             "for clearing a room.");
            if (rollsAndDeposits && !depositsCapturedRoll)
                failures.Add("[chest-drop-survives] the chest fell back to ItemDropSystem.RollAndDeposit, " +
                             "which rolls the loot table a SECOND time - the player can then be paid " +
                             "something other than what the mote showed. Capture ONE roll with RollLines " +
                             "and hand that same list to DepositLines.");

            var chest = FindType(ChestTypeName);
            if (chest == null) return;

            var prop = chest.GetProperty("LootTableId", BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
                failures.Add("[chest-drop-survives] the public LootTableId property is gone. Every placer " +
                             "(DungeonBaker, DungeonChainBuilder, the outpost builders) sets the table through it, so " +
                             "without it every chest in the game silently falls back to the default crate table.");
            else if (prop.PropertyType != typeof(string))
                failures.Add("[chest-drop-survives] LootTableId is " + prop.PropertyType.Name + ", not string - the " +
                             "loot-tables.json id is a string key and callers assign it as one.");
        }

        // =====================================================================
        //  CASE 6 - the reflection-invoked factory signature.
        // =====================================================================
        private static void Case6_CreateSignature(List<string> failures, List<string> notes)
        {
            var chest = FindType(ChestTypeName);
            if (chest == null)
            {
                failures.Add("[chest-create-signature] " + ChestTypeName + " not found. The class name is LOAD-BEARING: " +
                             "every baked .unity in the tree references this component by name, and a rename orphans it " +
                             "on every composed dungeon and KayKit outpost already on disk. ('Breakable' is now a " +
                             "misnomer - the chest is opened, not broken - but the misnomer is the cheap half.)");
                return;
            }

            if (!string.Equals(chest.Name, "BreakableContainer", StringComparison.Ordinal))
                failures.Add("[chest-create-signature] the type resolved as '" + chest.Name + "' - it must stay named " +
                             "BreakableContainer for every baked scene to keep its component.");

            var expected = new[] { typeof(Transform), typeof(Vector3), typeof(string), typeof(string) };
            var create = chest.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, expected, null);

            if (create == null)
            {
                var any = chest.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                var shapes = new List<string>();
                foreach (var m in any)
                {
                    if (!string.Equals(m.Name, "Create", StringComparison.Ordinal)) continue;
                    var ps = m.GetParameters();
                    var names = new List<string>();
                    foreach (var p in ps) names.Add(p.ParameterType.Name);
                    shapes.Add((m.IsPublic ? "public " : "non-public ") + "Create(" + string.Join(",", names) + ")");
                }

                failures.Add("[chest-create-signature] BreakableContainer.Create(Transform,Vector3,string,string) was not " +
                             "found as a PUBLIC STATIC method" +
                             (shapes.Count > 0 ? " (found instead: " + string.Join("; ", shapes) + ")" : " (no Create at all)") +
                             ". DungeonBaker.PlaceComposeChests and DungeonChainBuilder invoke it by REFLECTION with " +
                             "exactly those four arguments, so a rename or an arity change does NOT fail at compile - it " +
                             "silently stops placing chests in every composed dungeon and surfaces only as a bake-time " +
                             "warning. Empty rooms, no error, nobody notified.");
                return;
            }

            if (create.ReturnType != chest)
                failures.Add("[chest-create-signature] Create returns " + create.ReturnType.Name + ", not " + chest.Name +
                             ". The bakers assign the result to configure the loot table on the chest they just placed; " +
                             "a different return type means the reflected call succeeds and the table is never set.");
        }

        // =====================================================================
        //  CASE 7 - WO-1047's instrumentation is permanent (CLAUDE.md sec.12).
        // =====================================================================
        private static void Case7_InstrumentationIntact(List<string> failures, List<string> notes)
        {
            string src = ReadSource(ReticleSrcRel, "hostile-admit-instrumentation-intact", failures);
            if (src == null) return;

            int total = CountOccurrences(src, "[hostile-admit]");
            if (total == 0)
            {
                failures.Add("[hostile-admit-instrumentation-intact] the literal [hostile-admit] is GONE from " +
                             "HeroTargetIndicator.cs. Instrumentation is PERMANENT (CLAUDE.md sec.12) - once WO-1132 " +
                             "landed, this trace is the ONLY way to observe that no prop is being admitted to the " +
                             "hostile target set any more. Deleting it does not make the fix truer, it makes the proof " +
                             "unobservable and starts the next regression in this system from zero evidence. Flag it " +
                             "off if it must go quiet; never strip it.");
                return;
            }

            bool enemyBranch = new Regex(@"FlowTrace\.Step\s*\([^;]*\[hostile-admit\][^;]*ENEMY", RegexOptions.Singleline).IsMatch(src)
                            || (src.IndexOf("[hostile-admit] ENEMY", StringComparison.Ordinal) >= 0
                                && src.IndexOf("FlowTrace.Step", StringComparison.Ordinal) >= 0);

            bool nonEnemyBranch = src.IndexOf("NON-ENEMY ADMITTED", StringComparison.Ordinal) >= 0
                               && src.IndexOf("FlowTrace.Warn", StringComparison.Ordinal) >= 0;

            if (!enemyBranch)
                failures.Add("[hostile-admit-instrumentation-intact] the ENEMY branch of [hostile-admit] (the " +
                             "FlowTrace.Step line) is missing. It is WO-1047 acceptance criterion 6: proving the prop " +
                             "STOPPED being admitted is worthless without simultaneously proving that REAL enemies still " +
                             "reach the reticle. A hostile-set change that quietly rejects everything would read as a " +
                             "clean run.");

            if (!nonEnemyBranch)
                failures.Add("[hostile-admit-instrumentation-intact] the NON-ENEMY ADMITTED branch (the FlowTrace.Warn " +
                             "line that dumps path/impl/faction/layer/tag for anything non-enemy that reaches the " +
                             "hostile target set) is missing. After WO-1132 that branch should record ZERO admissions at " +
                             "runtime - the SILENCE is the proof the defect class is gone, and you cannot read silence " +
                             "from code that was deleted.");

            if (total < 2)
                notes.Add("[hostile-admit] appears only " + total + " time(s) in HeroTargetIndicator.cs - both branches " +
                          "were still detected, but the two-line shape WO-1047 shipped has changed");
        }

        // =====================================================================
        //  CASE 8 - WO-1458. The admit rule ROUTES through CombatFactionRules,
        //  and the hand-copied faction comparison is GONE. HARD FAIL, not a note.
        // =====================================================================
        private static void Case8_AdmitRoutesThroughFactionRules(List<string> failures, List<string> notes)
        {
            const string CaseName = "hostile-admit-routes-through-faction-rules";
            string src = ReadSource(ReticleSrcRel, CaseName, failures);
            if (src == null) return;

            // (a) The one predicate must be CALLED. CombatFactionRules.cs opens by forbidding, in
            // capitals, the copying of its comparison into a call site; HeroTargetIndicator held
            // THREE such copies until WO-1458 (RebuildCandidates x2 + PickEnemyAtScreenPoint).
            if (src.IndexOf("CombatFactionRules.MayAttack", StringComparison.Ordinal) < 0)
            {
                failures.Add("[" + CaseName + "] HeroTargetIndicator.cs no longer calls " +
                             "CombatFactionRules.MayAttack. The hostile target set must be gated by the ONE " +
                             "friend-or-foe predicate (Core/Combat/CombatFactionRules.cs), not by a comparison " +
                             "re-typed at the reticle. The sweep mask is deliberately WIDE (Enemy|Structure, " +
                             "HeroTargetIndicator.Awake, WO-853), so faction is the only thing standing between " +
                             "the player's own perimeter and the reticle.");
            }

            // (b) The copies must not come BACK. This is the whole failure mode: someone adds a
            // fourth admit site, hand-types the comparison, and the reticle silently regains a
            // route that CombatFactionRules never sees. Match the shape, not one spelling.
            var inlineCopy = new Regex(@"Faction\s*(!=|==)\s*CombatFaction\s*\.\s*(Hostile|Friendly)");
            var copies = inlineCopy.Matches(src);
            if (copies.Count > 0)
            {
                failures.Add("[" + CaseName + "] HeroTargetIndicator.cs re-introduces the inline faction " +
                             "comparison " + copies.Count + " time(s) (first: '" + copies[0].Value + "'). Route it " +
                             "through CombatFactionRules.MayAttack(HeroFaction, d) instead - that call also folds " +
                             "in the null and IsAlive checks, so it replaces the whole guard. Four hand-copies of " +
                             "one predicate is the duplicated-state failure this repo has already paid for in " +
                             "CLAUDE.md sec.2, sec.5, sec.8 and sec.16.");
            }

            // (c) The classifier that decides WHICH admission is a defect must be faction-based.
            // Before WO-1458 it asked only "does this carry an Enemy component?", so every raid
            // wall and the RaidSpire crown - legitimately Hostile, legitimately targetable - fell
            // into the Warn branch: 320 [hostile-admit] NON-ENEMY ADMITTED lines in ONE device
            // session, none of them a defect. A guard that cries wolf 320 times is a guard nobody
            // reads, which is exactly how WO-1047's real finding sat unactioned for two weeks.
            if (src.IndexOf("CombatFactionRules.IsFriendlyFire", StringComparison.Ordinal) < 0)
            {
                failures.Add("[" + CaseName + "] TraceAdmission no longer classifies by FACTION " +
                             "(CombatFactionRules.IsFriendlyFire is absent). The Warn branch must fire ONLY for a " +
                             "target of the hero's OWN faction - a genuine invariant breach now that every admit " +
                             "site gates on MayAttack. Classifying on GetComponentInParent<Enemy>() instead makes " +
                             "every hostile STRUCTURE (raid walls, the RaidSpire objective) read as a defect.");
            }

            if (src.IndexOf("HOSTILE STRUCTURE", StringComparison.Ordinal) < 0)
            {
                notes.Add("the [hostile-admit] HOSTILE STRUCTURE Step branch is gone - a raid session will no " +
                          "longer PROVE the reticle still acquires walls and the spire, only that it stayed quiet");
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static string AssetPath(string relative)
        {
            return Path.Combine(Application.dataPath, relative).Replace('\\', '/');
        }

        private static string ReadSource(string relative, string caseName, List<string> failures)
        {
            string path = AssetPath(relative);
            string result = null;
            Guard.Try(Sys, "read " + relative, () =>
            {
                if (!File.Exists(path))
                {
                    failures.Add("[" + caseName + "] source not found: " + path + " - WO-1132 is not in this tree, or the " +
                                 "file moved without updating this oracle (which silently disarms the lint).");
                    return;
                }
                result = File.ReadAllText(path);
            });
            if (result == null && File.Exists(path))
                failures.Add("[" + caseName + "] could not read " + path + " - the lint could not run, so treat this as a " +
                             "FAILURE and not an unknown (CLAUDE.md sec.16: marker absence is a failure).");
            return result;
        }

        /// <summary>
        /// Reads an <c>internal const string</c> off the type. Consts are literals in metadata,
        /// so GetRawConstantValue is the read that does not need an instance.
        /// </summary>
        private static string ReadConst(Type t, string name, List<string> failures)
        {
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (f == null)
            {
                failures.Add("[chest-refusal-has-words] " + t.FullName + "." + name + " not found. The localization key is the " +
                             "contract between the code and canon-strings.json - without it the words are either " +
                             "hardcoded in the file (canon bypassed, so the copy can never be re-authored without a code " +
                             "change) or gone entirely (a dead tap).");
                return null;
            }
            if (!f.IsLiteral)
            {
                failures.Add("[chest-refusal-has-words] " + t.FullName + "." + name + " is no longer a const literal - it " +
                             "can now be assigned at runtime, so nothing pins which canon key the chest actually reads.");
                return f.GetValue(null) as string;
            }
            try { return f.GetRawConstantValue() as string; }
            catch (Exception ex)
            {
                failures.Add("[chest-refusal-has-words] reading " + name + " threw " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// canon-strings.json is NOT a flat string map - it carries nested objects (_sources),
        /// so a Dictionary&lt;string,string&gt; deserialize throws on the whole file. Read it as
        /// a JObject and coerce only the values that really are strings.
        /// </summary>
        private static Dictionary<string, string> LoadCanon(string relative, string label, List<string> failures)
        {
            string path = AssetPath(relative);
            if (!File.Exists(path))
            {
                failures.Add("[chest-refusal-has-words] canon-strings.json " + label + " copy missing: " + path + " - half " +
                             "the build targets read that copy, so the chest's words cannot be validated for them.");
                return null;
            }

            Dictionary<string, string> map = null;
            Guard.Try(Sys, "parse canon-strings.json " + label, () =>
            {
                var root = JObject.Parse(File.ReadAllText(path));
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in root)
                {
                    if (pair.Value == null) continue;
                    if (pair.Value.Type != JTokenType.String) continue;   // _sources et al are objects
                    map[pair.Key] = pair.Value.Value<string>();
                }
            });

            if (map == null)
            {
                failures.Add("[chest-refusal-has-words] canon-strings.json " + label + " copy failed to PARSE (see the " +
                             "Guard line above). Every canon string in the game reads through this file - a parse failure " +
                             "blanks far more than the chest.");
                return null;
            }
            if (map.Count == 0)
            {
                failures.Add("[chest-refusal-has-words] canon-strings.json " + label + " copy parsed to ZERO string " +
                             "entries - every key would look like a typo.");
                return null;
            }
            return map;
        }

        private static string Lookup(Dictionary<string, string> map, string key)
        {
            if (map == null || string.IsNullOrEmpty(key)) return null;
            string v;
            return map.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>
        /// Strips // line comments and block comments so a lint decides on CODE, not prose.
        /// BreakableContainer.cs legitimately NAMES the rejected authorities in its header
        /// (it explains why WaveManager is absent), and a raw substring lint would read that
        /// explanation as the violation it forbids. String literals are left intact - they
        /// are code, and the tokens we hunt for are not plausible copy.
        /// </summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var noBlocks = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlocks, @"//[^\r\n]*", " ");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int count = 0;
            int i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += needle.Length;
            }
            return count;
        }

        private static int FirstNonAsciiIndex(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] < (char)32 || s[i] > (char)126) return i;
            return -1;
        }

        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(full, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }
    }
}
