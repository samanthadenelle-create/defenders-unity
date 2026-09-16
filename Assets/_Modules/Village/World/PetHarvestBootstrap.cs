// =============================================================================
// PetHarvestBootstrap — spawns harvestable MineNodes in the VILLAGE so the
// deployed starter pet actually has something to gather.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village (lives here, NOT the Pets asmdef, so it can reference
// MineNode directly — Pets→Village is reflection-only).
//
// DEF-122 gap (found 2026-06-02): the pet auto-harvest loop is fully wired
// (PetDeployer attaches PetHarvester → MineNodeBridge → MineNode banks to
// GameState), but there were ZERO MineNodes in Village.unity — they only exist
// in OuterWorld. So the pet scanned every second and found nothing. This drops a
// small cluster of nodes near the village centre, inside the pet's ~28m harvest-
// detect radius, so the economy loop runs the moment you load the village.
//
// Code-built + runtime (DDOL, same pattern as StoryCompanionInjector /
// RampartLiftInstaller) — no scene edit, no VillageSceneBuilder change. The nodes
// are visible tinted "ore" so the player sees them and can [F]-tap them too; a
// real model can swap in later via the catalog/VisualFactory.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;   // FlowTrace — §12: this seam was silently dead, it stays instrumented
using DeNelle.Pets;               // PetDeployer/Pet — DeNelle.Village.asmdef already references DeNelle.Pets
using DeNelle.Village;
using DeNelle.Village.World;

namespace DeNelle.Village
{
    /// <summary>Runtime-spawns starter resource nodes in the village for pet harvesting.</summary>
    public sealed class PetHarvestBootstrap : MonoBehaviour
    {
        public static PetHarvestBootstrap Instance { get; private set; }
        // Support both the main Village scene and the Village2 test scene so the
        // pet resource farming loop (WO-106 / PetHarvester + MineNode) is exercisable
        // without requiring OuterWorld. When SpawnPlaceholderNodes (or -spawn flag)
        // is enabled the 4 starter nodes (one per harvestable) appear near centre
        // inside the pet detect radius. Combat priority in PetHarvester still yields
        // to enemies. EconomyService.Grant is the faucet (nodes now route through it).
        private const string TargetScene = "Village2";

        /// <summary>FlowTrace system tag for this bootstrap (§12 — permanent instrumentation).</summary>
        private const string FlowSys = "PetHarvest";

        /// <summary>Cap on Echoes auto-assigned to harvest sites in one pass. Was an inline `4`
        /// with a "limit for demo" comment; named so the bound is visible rather than magic.</summary>
        private const int MaxAutoAssignedPets = 4;
        private bool _built;

        // DEF-258: these MineNodes are unstyled PLACEHOLDERS — tinted primitive cubes
        // (incl. the cyan "Mine AetherCrystal" cube) with a code-built green fill bar
        // (NodeFillIndicator, auto-attached by WorkerManager) that billboards toward the
        // camera and reads in-game as a "green disc" floating on the node. The node-
        // claiming / harvest UX (DEF-166/167) is not production-ready, so the placeholder
        // SPAWN is disabled in the playable build. This kills the cyan cube, the green
        // disc indicator, the "[G] Upgrade Mine" prompt, and any per-frame NullRef the
        // half-built placeholder nodes were generating on Village2 load.
        //
        // The ECONOMY ITSELF IS UNTOUCHED: MineNode / CrystalMineNode / CrystalEconomy /
        // the pet+worker harvest seams and the OuterWorld nodes all remain. Only this
        // runtime placeholder DROP in Village2 is gated off. Flip to true (or pass
        // -spawnPlaceholderMineNodes on the command line) to restore the dev placeholders
        // when the node system is re-enabled with real art + mobile input.
        private static readonly bool SpawnPlaceholderNodes = false; // readonly (not const) so the gated branch doesn't emit CS0162

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            PetDeployer.EchoBodyDespawning -= UnassignDespawningEcho;
            PetDeployer.EchoBodyDespawning += UnassignDespawningEcho;
            if (Instance != null) return;
            // DEF-258: do not even create the bootstrap host when placeholder nodes are
            // disabled — nothing to spawn, so no MonoBehaviour and no scene scan run.
            if (!PlaceholderNodesEnabled()) return;
            new GameObject("PetHarvestBootstrap").AddComponent<PetHarvestBootstrap>();
        }

        private static void UnassignDespawningEcho(Transform worker)
        {
            if (worker == null) return;
            var sites = FindObjectsByType<HarvestSite>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var site in sites)
                if (site != null) site.UnassignPet(worker);
        }

        /// <summary>Whether the dev placeholder mine nodes should spawn. Off by default
        /// (DEF-258); opt back in via the const above or a command-line override.</summary>
        private static bool PlaceholderNodesEnabled()
        {
            if (SpawnPlaceholderNodes) return true;
            var args = System.Environment.GetCommandLineArgs();
            if (args != null)
                for (int i = 0; i < args.Length; i++)
                    if (args[i] == "-spawnPlaceholderMineNodes") return true;
            return false;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().name == TargetScene) Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var n = scene.name;
            if (n == TargetScene || n == "Village") { _built = false; Build(); }
        }

        private void Build()
        {
            if (_built) return;
            _built = true;
            // DEF-258: never drop placeholder nodes unless explicitly re-enabled.
            if (!PlaceholderNodesEnabled()) return;
            // Idempotent: if the village already has nodes (a future hand-placed pass), skip.
            if (UnityEngine.Object.FindAnyObjectByType<MineNode>() != null) return;

            // A small cluster near the village centre — inside the pet's ~28m harvest detect
            // (PetHarvester) so the deployed Warden finds + works them. One node per
            // harvestable (DEF-121: Wood / Food / Iron / Crystals) so pet auto-harvest
            // raises ALL FOUR resource counts in GameState, visibly.
            //
            // WO-106+: We also create HarvestSite wrappers on these nodes. This gives:
            //   • Themed visual harvest structure (platform + resource top)
            //   • Pet assignment support (AssignPet) → scaled harvest over time
            //   • All income routed through EconomyService.AddResource (single source of truth)
            //   • Floating "+X Resource" popups
            // The raw MineNodes remain for autonomous PetHarvester fallbacks.
            MineNode woodNode = SpawnNode("Wood",     MineResource.Wood,         new Vector3( 11f, 0f,  11f), new Color(0.45f, 0.30f, 0.16f));
            MineNode ironNode = SpawnNode("Iron",     MineResource.Iron,         new Vector3(-12f, 0f,   9f), new Color(0.55f, 0.57f, 0.62f));
            MineNode foodNode = SpawnNode("Food",     MineResource.Stone,         new Vector3(  9f, 0f, -12f), new Color(0.70f, 0.62f, 0.28f));
            MineNode crystalNode = SpawnNode("Crystals", MineResource.AetherCrystal, new Vector3(-9f, 0f, -11f), new Color(0.45f, 0.72f, 0.95f));

            // Wrap them as proper HarvestSites (priority 1) so claiming + assigned-pet
            // harvesting + Economy.AddResource + floating text are demonstrated.
            TryWrapAsHarvestSite(woodNode, "Lumber Harvest");
            TryWrapAsHarvestSite(ironNode, "Ore Harvest");
            TryWrapAsHarvestSite(foodNode, "Farm Harvest");
            TryWrapAsHarvestSite(crystalNode, "Crystal Harvest");

            // Auto-assign any already-deployed pets (from PetDeployer) to the new sites
            // for an immediate "pets harvesting while you defend" demo.
            AutoAssignDeployedPetsToSites();
        }

        private static MineNode SpawnNode(string label, MineResource res, Vector3 pos, Color tint)
        {
            // Snap onto the baked NavMesh so the pet can path to it.
            if (NavMesh.SamplePosition(pos, out var hit, 8f, NavMesh.AllAreas)) pos = hit.position;

            var go = new GameObject($"MineNode-{label}-Village");
            go.transform.position = pos;

            // Visible ore vein (placeholder primitive, tinted by resource). MineNode uses
            // distance checks (not physics), so strip the cube collider to keep paths clear.
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Ore";
            rock.transform.SetParent(go.transform, false);
            rock.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            rock.transform.localScale = new Vector3(1.3f, 1.0f, 1.3f);
            rock.transform.localRotation = Quaternion.Euler(8f, 25f, 6f);
            var col = rock.GetComponent<Collider>(); if (col != null) Destroy(col);
            var mr = rock.GetComponent<Renderer>();
            if (mr != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (sh != null)
                {
                    var m = new Material(sh);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint); else m.color = tint;
                    mr.sharedMaterial = m;
                }
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            var node = go.AddComponent<MineNode>();
            node.Resource         = res;
            // WO-953: demo rates promoted to owner-tunable canonical data
            // (Data/Canonical/harvest-tuning.json via HarvestTuning; defaults are the
            // old hardcodes 5 / 6s, values unchanged). The tuning pass itself is hers.
            node.YieldPerExtract  = HarvestTuning.PetNodeYieldPerExtract;
            node.ExtractCooldown  = HarvestTuning.PetNodeExtractCooldownSeconds;
            node.TotalExtracts    = 0;     // infinite — a steady starter economy that never depletes
            node.RespawnSeconds   = 0f;
            node.UseFiniteReserve = false;
            node.InteractRadius   = 2.5f;  // player can also [F]-tap them
            return node;
        }

        private static void TryWrapAsHarvestSite(MineNode node, string label)
        {
            if (node == null) return;
            var site = node.ClaimAsHarvestSite();
            if (site != null)
            {
                site.name = $"HarvestSite-{label}";
                // Slightly richer base for the demo (pets make it better).
                // WO-953: promoted to harvest-tuning.json (default 5, value unchanged).
                site.BaseYield = HarvestTuning.PetNodeSiteBaseYield;
            }
        }

        private static void AutoAssignDeployedPetsToSites()
        {
            // ⛔ THIS METHOD HAD NEVER COMPLETED A SINGLE TIME BEFORE 2026-08-17 (WO-1038).
            //
            // Its first line was `GameObject.FindGameObjectsWithTag("Pet")`, and **"Pet" is not a
            // declared tag** — ProjectSettings/TagManager.asset declares exactly four:
            // Tower, Building, HeartTarget, Player. FindGameObjectsWithTag THROWS UnityException on
            // an undeclared tag, so this method threw on ENTRY every single call, and the "fallback"
            // beneath it was unreachable dead code. Echo → harvest-site auto-assignment has therefore
            // never worked in any build that ever shipped. Same shape as the RaidHeroSpawner case:
            // a safety net that the throw above it guaranteed could never be reached. A tag typo is
            // not a soft failure in Unity — it is an exception, and an exception in a bootstrap is
            // silent because nothing downstream expects a return value.
            //
            // ⚠ THE FIX IS NOT TO DECLARE A "Pet" TAG. `PetDeployer` ALREADY owns the authoritative
            // registry of deployed pets — `PetDeployer.DeployedPets`, populated at PetDeployer.cs:179 —
            // and `DeNelle.Village.asmdef` ALREADY references `DeNelle.Pets`. The old comment here
            // claiming we must "stay decoupled from Pets asmdef" was STALE, and it is precisely what
            // pushed this code onto (1) a string tag, (2) a FindObjectsByType<GameObject>() scan of the
            // ENTIRE scene as a fallback, and (3) GetComponent("Pet") / SendMessage string reflection.
            // Asking the owner that already knows removes all four problems at once. Declaring the tag
            // would have fixed only the throw and left a second, parallel authority over "which pets
            // are deployed" — the duplicate-authority bug class this project keeps paying for.
            var deployer = FindFirstObjectByType<PetDeployer>(FindObjectsInactive.Exclude);
            if (deployer == null)
            {
                FlowTrace.Step(FlowSys, "no PetDeployer in scene — nothing deployed, no harvest assignment.");
                return;
            }

            var pets = deployer.DeployedPets;
            var sites = FindObjectsByType<HarvestSite>(FindObjectsInactive.Exclude);
            if (pets == null || pets.Count == 0 || sites.Length == 0)
            {
                FlowTrace.Step(FlowSys,
                    $"nothing to assign (deployed pets={pets?.Count ?? 0}, harvest sites={sites.Length}).");
                return;
            }

            int assigned = 0;
            foreach (var site in sites)
            {
                foreach (var pet in pets)
                {
                    if (pet == null) continue;          // a despawned Echo (PetDeployer.DespawnEcho) leaves a null slot

                    if (site.AssignPet(pet.transform))
                    {
                        assigned++;
                        // Steer the Echo at its site. Direct typed call — Pet.SetHomePost is public
                        // (Pet.cs:238) and the PetHarvester already respects HomePost when active.
                        if (site.transform != null) pet.SetHomePost(site.transform.position);

                        if (assigned >= MaxAutoAssignedPets)
                        {
                            FlowTrace.Step(FlowSys, $"auto-assigned {assigned} Echo(es) to harvest sites (cap reached).");
                            return;
                        }
                    }
                }
            }

            FlowTrace.Step(FlowSys, $"auto-assigned {assigned} Echo(es) across {sites.Length} harvest site(s).");
        }
    }
}
