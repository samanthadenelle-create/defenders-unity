// =============================================================================
// HubScenes — the ONE canonical list of home/hub scenes.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core
//
// WHY: "is this a town/hub scene?" was answered in TWO drifted places — the HUD
// (`VillageHudController.EvaluateInVillage` only knew "Village2") and the world
// streamer (`WorldSceneLoader.HubSceneNames` knew the full set). That drift hid the
// whole town HUD on MainCastle_Hall (WO-411 root cause A). This is the single source
// both read, so adding a hub scene = one edit + the test below stays green.
//
// Lives in Core so BOTH DeNelle.Village (WorldSceneLoader) and DeNelle.HUD
// (VillageHudController) can reference it (Village→Core, HUD→Core; never Village↔HUD).
// =============================================================================
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Core
{
    public static class HubScenes
    {
        /// <summary>Canonical home/hub scene names. Add a new hub here (and only here).</summary>
        public static readonly string[] Names = { "Village2", "MainCastle_Hall", "CastleHub", "CastleHub_MainKeep", "Main_Castle_Overworld" };

        /// <summary>True if <paramref name="sceneName"/> is a home/hub scene (exact or prefix-contains,
        /// matching WorldSceneLoader's prior behaviour so CastleHub* variants still count).</summary>
        /// <remarks>
        /// MATCHING IS SUBSTRING, NOT EXACT. `sceneName.Contains(Names[i])` means
        /// IsHub("CastleHub_MainKeep_Backup") and IsHub("Village2_Test") are BOTH true, where a
        /// private `== "CastleHub"` list returns false. Replacing any private `==` hub list with
        /// this predicate is therefore a WIDENING, and must be a deliberate call at that call site
        /// (see HeroEquipHud.IsHubScene for the reasoning template). Conversely, tightening this to
        /// exact-or-StartsWith is a behaviour change across ~40 call sites - do not do it as a
        /// side effect of another ticket. Assets/Tests/EditMode/HubScenesTest.cs guards the shape.
        /// </remarks>
        public static bool IsHub(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            for (int i = 0; i < Names.Length; i++)
                if (sceneName == Names[i] || sceneName.Contains(Names[i])) return true;
            return false;
        }

        /// <summary>True if <paramref name="sceneName"/> is an OVERWORLD scene — the merged
        /// <c>Main_Castle_Overworld</c> scene (WO-608). The single source of truth for
        /// overworld-behavior gates (encounter spawner, harvest workers, camps, raid
        /// outposts, world boundary). OuterWorld was removed; all world content is now
        /// in Main_Castle_Overworld (MergedWorld).</summary>
        public static bool IsOverworld(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return sceneName == "Main_Castle_Overworld";
        }

        /// <summary>True if <paramref name="sceneName"/> is an enemy RAID scene
        /// (<c>RaidBase_*</c>) — the single source both the HUD (combat-cluster gate,
        /// WO-457) and the Village (RaidDeployController self-install) read so the raid
        /// naming convention lives in ONE place.</summary>
        public static bool IsRaid(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName)
                && sceneName.StartsWith("RaidBase", System.StringComparison.OrdinalIgnoreCase);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SCENE KIND + the COMBAT DECLARATION (WO-1436)
        // -----------------------------------------------------------------------
        //  THE DEFECT THIS CLOSES (owner felt-test 2026-09-06, build
        //  2026.09.06.358161: "in the raid, i had no way to fight. No combast
        //  skills"). Inside RaidBase_raider_camp_small the HUD context resolver
        //  logged, for the whole raid:
        //
        //    [Flow:HUD] context inputs: wave=False battleLock=False pursuit=False
        //               inVillage=False modal=False scene='RaidBase_raider_camp_small'
        //               -> Overworld
        //
        //  ...so the posture resolved to calm(explore) and the action bar rendered
        //  the PEACEFUL dock. The bar was not broken; it was asked the wrong
        //  question. NOTHING told the HUD that standing inside an assault is combat.
        //  Measured from the same capture, the posture flapped
        //  calm(explore) <-> hostile(*) SEVEN times across a 49 s raid, tracking
        //  transient pursuit pulses instead of the fact that the player had pressed
        //  BEGIN ASSAULT.
        //
        //  WHY IsRaid AND NOT IsEnemyOwnedScene. The owner's 2026-07-05 ruling --
        //  "scene ground alone does NOT flip Battle" -- was written about open
        //  enemy-owned GROUND, and Village2 carries ownership:"Enemy" in
        //  scene-configs.json, so keying combat off enemy-ownership would drag the
        //  whole raid-target village into a permanent battle posture and overturn
        //  that ruling. A RaidBase_* scene is not ground the player wanders onto: it
        //  is entered only through BEGIN ASSAULT, with troops committed and a scored
        //  180 s clock (RaidScoring), and [Flow:Raid] RAID START fires ~1 s after the
        //  load. That is a DECLARED fight, so it declares combat. The 07-05 ruling is
        //  refined, not overturned.
        //
        //  WHY IT LIVES HERE. Same reason as the rest of this class: "what kind of
        //  scene is this?" was already answered in drifted partial copies (WO-411,
        //  WO-920). The combat declaration is one more answer to that same question,
        //  so it goes next to IsHub/IsOverworld/IsRaid/IsDungeon rather than becoming
        //  a private test inside the HUD.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The scene families the HUD posture seam classifies against.
        /// Exhaustive over the build list -- <c>Unknown</c> is a FAILURE the seam
        /// oracle reports, never a silent default.</summary>
        public enum SceneKind
        {
            /// <summary>Title / HeroSelect / PetSelect -- front-end, no world HUD.</summary>
            FrontEnd,
            /// <summary>A home/hub town scene (<see cref="IsHub"/>).</summary>
            Hub,
            /// <summary>An enemy raid base entered through BEGIN ASSAULT (<see cref="IsRaid"/>).</summary>
            Raid,
            /// <summary>An open-air enemy outpost — <c>Garrison_*</c>, <c>Outpost1</c>, <c>Outpost2</c>.
            /// Deliberately NOT <see cref="Raid"/>: see <see cref="IsEnemyOutpost"/>.</summary>
            EnemyOutpost,
            /// <summary>A dungeon -- composed <c>dg_*</c>, hand-built <c>Dungeon*</c>, or the outpost.</summary>
            Dungeon,
            /// <summary>The staged ATB battle scene.</summary>
            Battle,
            /// <summary>Open world / anything else that is a real playable scene.</summary>
            Overworld,
            /// <summary>No predicate names this scene. The seam oracle FAILS on this.</summary>
            Unknown,
            OwnedTown,
            TownPractice,
        }

        /// <summary>Front-end / menu scenes: no world, no posture worth asserting.</summary>
        public static bool IsFrontEnd(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return sceneName == SceneRouter.Title
                || sceneName.StartsWith("HeroSelect", StringComparison.OrdinalIgnoreCase)
                || sceneName.StartsWith("PetSelect", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The OPEN-AIR enemy outposts baked outside the raid pipeline: <c>Garrison_*</c> and
        /// the two hand-named <c>Outpost1</c>/<c>Outpost2</c> scenes. The same three families
        /// FeatureFlags and SceneTransitionTrigger already group together.
        ///
        /// <para>⚠ THEY ARE NOT <see cref="IsRaid"/>, AND THAT IS A DELIBERATE NON-DECISION.
        /// WO-1436 was raised on a <c>RaidBase_*</c> scene and proven there; whether a garrison
        /// or an outpost is ALSO a committed assault that should declare combat is a design
        /// question the owner has not been asked. They are named here so the scene/posture seam
        /// oracle can classify the whole build list without a false Unknown — naming a scene is
        /// not the same as ruling on it. If the owner rules that they are assaults too, the
        /// change is one line in <see cref="SceneDeclaresCombat"/> and the oracle's expectation
        /// for this kind.</para>
        /// </summary>
        public static bool IsEnemyOutpost(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return sceneName.StartsWith("Garrison_", StringComparison.OrdinalIgnoreCase)
                || sceneName.StartsWith("Outpost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Classify a scene into exactly one <see cref="SceneKind"/>. Order matters and is
        /// deliberate: the front-end and raid/dungeon tests run BEFORE <see cref="IsHub"/>,
        /// because IsHub matches by SUBSTRING (see its remarks) and a future
        /// "Dungeon_Village2_Ruins" must not read as a town.
        /// </summary>
        public static SceneKind Classify(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return SceneKind.Unknown;
            if (IsOwnedTown(sceneName)) return SceneKind.OwnedTown;
            if (IsTownPractice(sceneName)) return SceneKind.TownPractice;
            if (IsFrontEnd(sceneName)) return SceneKind.FrontEnd;
            if (IsRaid(sceneName)) return SceneKind.Raid;
            // IsDungeon BEFORE IsEnemyOutpost: KayKitChallengeOutpost is a DUNGEON that happens
            // to end in "Outpost", and only this ordering keeps it one.
            if (IsDungeon(sceneName)) return SceneKind.Dungeon;
            if (IsEnemyOutpost(sceneName)) return SceneKind.EnemyOutpost;
            if (sceneName == SceneRouter.ATBBattle) return SceneKind.Battle;
            if (IsHub(sceneName)) return SceneKind.Hub;
            if (IsOverworld(sceneName)) return SceneKind.Overworld;
            return SceneKind.Unknown;
        }

        /// <summary>
        /// TRUE when the SCENE ITSELF declares that the player is in a fight, independently
        /// of waves, pursuit pulses or a battle lock -- the missing HUD context input behind
        /// WO-1436. Read by <c>HudContextEvaluator</c> so the posture resolves to a combat
        /// posture for the WHOLE time the player stands in an assault, and by the scene/posture
        /// seam oracle so a raid scene resolving peaceful FAILS the build.
        ///
        /// <para>TODAY THIS IS EXACTLY <see cref="IsRaid"/>, and that narrowness is the point.
        /// A dungeon is explored before it is fought, and open enemy ground stays peaceful
        /// until something threatens the hero (owner 2026-07-05) -- both keep the existing
        /// pursuit-driven arc. Only the raid is a committed, clocked, scored assault. Widening
        /// this predicate is an owner ruling, not a refactor.</para>
        /// </summary>
        public static bool IsOwnedTown(string sceneName) => sceneName == "OwnedTown_IronBastion";
        public static bool IsTownPractice(string sceneName) => sceneName == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName;

        public static bool SceneDeclaresCombat(string sceneName) => IsRaid(sceneName) || IsTownPractice(sceneName);

        // ─────────────────────────────────────────────────────────────────────
        //  Dungeon scene test (WO-920)
        // -----------------------------------------------------------------------
        //  Added here rather than in a new file for the exact reason this class
        //  exists (see the header): "is this a dungeon?" was ALREADY answered in two
        //  drifted, partial places — JupiterSwapBootstrap L133 tests StartsWith
        //  "Dungeon_" (misses every composed dg_* scene and the outpost), and the
        //  editor-only DungeonSceneCapture carries a folder + an ExtraScenes array.
        //  WO-920 needed a THIRD answer at runtime; that is how HubScenes' own root
        //  cause (WO-411) happened, so it goes next to IsHub/IsOverworld/IsRaid.
        //
        //  THREE naming families, all verified against the scenes on disk 2026-08-07:
        //    dg_*                     composed dungeons, Assets/Scenes/DungeonCompose/
        //                             (ids authored in RoomForge/Phase2DungeonBatch L30-32
        //                             + GraphDungeonComposer L97-101). On disk:
        //                             dg_starter_loop, dg_descent_probe, dg_sunken_vault,
        //                             dg_bonecrypt, dg_ember_deep.
        //    Dungeon*                 hand-built: Dungeon, Dungeon_Demo,
        //                             Dungeon_HealersCottage, Dungeon_FolksGranary.
        //    KayKitChallengeOutpost   hand-coded starter outpost — a dungeon that lives
        //                             outside BOTH builders, which is why it keeps missing
        //                             pipeline fixes (DungeonSceneCapture L59-69 carries it
        //                             as an explicit extra for the same reason).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The hand-coded starter outpost — a dungeon named by neither convention.</summary>
        public const string OutpostSceneName = "KayKitChallengeOutpost";

        /// <summary>
        /// True if <paramref name="sceneName"/> is a DUNGEON scene — composed (<c>dg_*</c>),
        /// hand-built (<c>Dungeon*</c>), or the hand-coded <c>KayKitChallengeOutpost</c>.
        /// The single source for dungeon-behaviour gates (WO-920 camera + clear colour).
        /// </summary>
        /// <remarks>
        /// MATCHING IS PREFIX/EXACT, NOT SUBSTRING — deliberately TIGHTER than
        /// <see cref="IsHub"/>. Substring matching would swallow anything merely mentioning
        /// "Dungeon" and, more importantly, "dg_" is short enough that a Contains test would
        /// be a live grenade for any future scene name. Garrison_* / RaidBase_* / Outpost1-2
        /// are deliberately NOT dungeons: they are open-air raid targets that keep the outdoor
        /// camera and a real sky.
        /// </remarks>
        public static bool IsDungeon(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return IsComposedDungeon(sceneName)
                || sceneName.StartsWith("Dungeon", StringComparison.OrdinalIgnoreCase)
                || sceneName.Equals(OutpostSceneName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True ONLY for a COMPOSED dungeon (<c>dg_*</c>, baked by DungeonBaker /
        /// GraphDungeonComposer). The narrower half of <see cref="IsDungeon"/>.
        /// </summary>
        /// <remarks>
        /// WHY THE NARROWER TEST EARNS ITS OWN NAME (WO-1112, the dungeon twin of WO-1109):
        /// the two dungeon pipelines need OPPOSITE hero handling, so "is this a dungeon?"
        /// is the wrong question at the hero seam and answering it with IsDungeon would ship
        /// a bug. A COMPOSED scene's baked hero is a bare rig (HeroLocomotion +
        /// HeroBodySwapper, DungeonBaker.PopulateForPlay) with NO HeroAbilities, so the town
        /// hero must be CARRIED in over it. A hand-built scene's hero is owned by that
        /// scene's DungeonController through SERIALIZED references; carrying a hero in there
        /// makes HeroControlEnsurer.DedupeHeroes destroy the baked one and null those refs.
        /// Same word, opposite correct behaviour — hence one test per pipeline, in the one
        /// file that already owns scene-family naming (this class exists because that answer
        /// had drifted into three partial copies, WO-411/920).
        /// </remarks>
        public static bool IsComposedDungeon(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName)
                && sceneName.StartsWith("dg_", StringComparison.OrdinalIgnoreCase);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Enemy-owned scene test (WO-470 / HUD-RCA)
        // -----------------------------------------------------------------------
        //  WHY HERE (architecture): the authoritative ownership flag lives in
        //  DeNelle.Village.SceneOwnership, but the HUD (DeNelle.HUD) may NOT
        //  reference DeNelle.Village (CLAUDE.md §5). It CAN reference Core. The
        //  ownership DATA — scene-configs.json — is read through the Core-side
        //  CanonicalJson loader (the SAME loader SceneOwnership/SceneConfigCatalog
        //  use). So we expose a Core-clean test that reads the SAME canonical data
        //  here, rather than adding a CoreServices delegate + a Village-side setter
        //  (more surface). One JSON parse, cached. SceneOwnership remains the
        //  runtime gameplay flag; this is the read-only mirror the HUD consumes.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>StreamingAssets-relative path (CanonicalJson strips the ext for Resources).</summary>
        private const string SceneConfigsPath = "Data/Canonical/scene-configs.json";

        // Cached sceneName(lower) -> isEnemy map, built once from scene-configs.json.
        private static Dictionary<string, bool> _enemyByScene;

        /// <summary>True if <paramref name="sceneName"/> is an ENEMY-OWNED scene per
        /// scene-configs.json (ownership == "Enemy"). HUD-readable mirror of
        /// DeNelle.Village.SceneOwnership without a Village reference (WO-470).
        /// Default false (safe) for any scene without a matching config entry.</summary>
        public static bool IsEnemyOwnedScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            EnsureOwnershipLoaded();
            return _enemyByScene != null
                && _enemyByScene.TryGetValue(sceneName, out bool enemy)
                && enemy;
        }

        /// <summary>WO-550 chokepoint: should TOWN / SOCIAL / ECONOMY HUD panels be SUPPRESSED
        /// in <paramref name="sceneName"/>? True for enemy-owned raid scenes (Village2), false for
        /// the home hub (MainCastle_Hall) and every other scene. The single semantic test the
        /// ~14 per-panel bootstraps gate on so combat scenes stop bootstrapping town panels
        /// (jukebox / clan chat / shops / crafting / quests / skill trees / building upgrade / swap).
        /// Combat-appropriate HUD (BattleHud9Zone, Compass, vitals, loadout) is NOT gated by this.</summary>
        public static bool SuppressTownHud(string sceneName) => IsEnemyOwnedScene(sceneName);

        private static void EnsureOwnershipLoaded()
        {
            if (_enemyByScene != null) return;
            _enemyByScene = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string text = CanonicalJson.Read(SceneConfigsPath);
                if (string.IsNullOrEmpty(text))
                {
                    Debug.LogWarning("[Flow:HUD] HubScenes: scene-configs.json not found — " +
                                     "all scenes default to NOT enemy-owned.");
                    return;
                }

                var file = JsonConvert.DeserializeObject<SceneConfigFile>(text);
                if (file != null && file.configs != null)
                {
                    foreach (var c in file.configs)
                    {
                        if (c == null || string.IsNullOrEmpty(c.sceneName)) continue;
                        _enemyByScene[c.sceneName] =
                            string.Equals(c.ownership, "Enemy", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                // §12 no silent failure: report + default to not-enemy-owned (safe).
                Debug.LogWarning("[Flow:HUD] HubScenes: failed to read scene-configs.json (" +
                                 ex.Message + ") — all scenes default to NOT enemy-owned.");
            }
        }

        // Minimal JSON shape (mirrors scene-configs.json: { configs:[{ sceneName, ownership }] }).
        [Serializable]
        private sealed class SceneConfigFile
        {
            public List<SceneConfigEntry> configs;
        }

        [Serializable]
        private sealed class SceneConfigEntry
        {
            public string sceneName;
            public string ownership;
        }
    }
}
