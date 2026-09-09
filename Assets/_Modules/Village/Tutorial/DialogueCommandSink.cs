// =============================================================================
// DialogueCommandSink (DeNelle.Village) — wires OUR dialogue's commands + conditions
// to the game, registered at boot (WO-455). Verbs route DIRECTLY to PanelRouter /
// QuestService / the live gameplay systems — no Yarn, no source generator, no
// register-once-globally constraint.
// -----------------------------------------------------------------------------
// ENGINE/INFRA PARITY (WO-455, this pass): every CUSTOM verb the existing Yarn
// content actually fires is now routed here to the SAME service the Yarn
// DialogueCommandBridge delegates to, so when the owner converts the narrative
// content off Yarn the verbs already work. The routing idiom mirrors the bridge
// exactly (PanelRouter / QuestService / GameStateService / SmartMobileCamera /
// TutorialHudOverlay / PetDeployer / TroopDialogueCommands / BuildingUpgradeService).
//
// This sink is a PLAIN C# object (no MonoBehaviour) driven by the synchronous
// DialogueRunner, so verbs map to the SYNCHRONOUS action behind each Yarn handler;
// the bridge's coroutine niceties (camera auto-restore hold, autowalk arrival
// gating, blocking waits) have no equivalent in the tap-to-continue runner and are
// intentionally omitted — camera_return_to_hero / stop_autowalk cover the manual
// counterparts. Verbs with NO backing service (Yarn-var seeding, narrative stubs,
// timed waits) stay on the logged Warn-default — never faked.
//
// Unknown verbs/conditions FlowTrace.Warn (no silent failure). Flag-gated on
// CustomDialogue (default off).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Dialogue;
using DeNelle.Core.UI;
using DeNelle.Core.Quests;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;
using DeNelle.Pets;
using Prog = DeNelle.Village.Buildings.Progression;

namespace DeNelle.Village
{
    public sealed class DialogueCommandSink : IDialogueCommandSink, IDialogueConditionSource
    {
        // Lazily-created host for the scene helper components (autowalk / HUD overlay).
        private GameObject _host;
        private TutorialAutoWalk _autoWalk;
        private TutorialHudOverlay _overlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (!DeNelle.Core.FeatureFlags.CustomDialogue) return; // migration flag (default off)
            var sink = new DialogueCommandSink();
            // Fully-qualify: DeNelle.Village also has a (Yarn) DialogueService that would shadow this.
            DeNelle.Core.Dialogue.DialogueService.RegisterSink(sink);
            DeNelle.Core.Dialogue.DialogueService.RegisterConditions(sink);
            FlowTrace.Step("Dialogue", "Custom dialogue command sink registered (Yarn-free).");
        }

        // Ticket F8-14 ("disable shopping" during the wave): a dialogue already OPEN when
        // the wave countdown starts could still fire a shop verb after the vendors hide —
        // block it on the SAME combat authority the townsfolk flee on (AmbientNPC), Warn
        // (never a silent no-op) and surface the reason via the shared feedback toast.
        private static bool ShopsClosedForCombat(string verb)
        {
            if (!AmbientNPC.IsCombatActive) return false;
            FlowTrace.Warn("Dialogue",
                $"{verb} BLOCKED — combat active (shops closed during the assault).");
            BuildFeedbackToast.Show("Shops closed during the assault!");
            return true;
        }

        // ── Commands → direct service calls (mirrors DialogueCommandBridge routing) ──
        public void Run(string verb, IReadOnlyList<string> args)
        {
            if (string.IsNullOrEmpty(verb)) return;
            int n = args != null ? args.Count : 0;
            string a0 = n > 0 ? args[0] : null;
            string a1 = n > 1 ? args[1] : null;
            string a2 = n > 2 ? args[2] : null;

            switch (verb)
            {
                // ── Panels via PanelRouter (cross-assembly, reflection-free) ──────
                case "OpenRumorBoard": PanelRouter.Open(PanelId.RumorBoard); break;
                case "OpenUpgrade":    PanelRouter.Open(PanelId.BuildingUpgrade, a0); break;
                // OpenShop [vendor] [mode?] — a1 is an optional "buy"/"sell" mode (owner F8
                // 2026-07-10): the NPC offers Buy/Sell as SEPARATE choices, each opening the shop
                // LOCKED to one flow. When a mode is present route through the subject+mode opener;
                // the single-arg (both-tabs) path still works for any legacy caller.
                case "OpenShop":
                    if (!ShopsClosedForCombat("OpenShop"))
                    {
                        if (!string.IsNullOrEmpty(a1)) PanelRouter.Open(PanelId.PartyShop, a0, a1);
                        else PanelRouter.Open(PanelId.PartyShop, a0);
                    }
                    break;
                case "OpenCraft":      PanelRouter.Open(PanelId.Crafting); break;
                // Apothecary NPC (owner F8 2026-07-02): the herbalist's card-first dialogue ends
                // by opening the consumable-crafting / alchemy bench — the SAME panel the station's
                // own BuildingInteractable opens (BuildingType.ApothecaryWorkbench route).
                case "OpenAlchemy":    PanelRouter.Open(PanelId.ConsumableCrafting); break;
                // Jeweler NPC (owner 2026-07-03 "every building needs an NPC as the speaker"): Sable's
                // dialogue ends by opening the jewelry-crafting bench — the SAME panel the station's own
                // BuildingInteractable opens (BuildingType.JewelersBench route). Mirrors OpenAlchemy.
                case "OpenJeweler":    PanelRouter.Open(PanelId.JewelerCrafting); break;
                case "OpenRoughStonePolish": Crafting.JewelPolishFlowPanel.ShowStart(); break;
                // EYES-SWEEP 2026-07-06: legacy PanelId.HeroTalents route REMOVED (dead panel;
                // rendered black). One panel, one route: HeroSkillTree.
                case "OpenTalents":    PanelRouter.Open(PanelId.HeroSkillTree); break;
                case "OpenCosmetics":  if (!ShopsClosedForCombat("OpenCosmetics")) PanelRouter.Open(PanelId.CosmeticShop); break;
                // Realm Store (SKR/SOL/USDC packs) — the merchant's "Realm Store" option opens the
                // monetization PackStore, alongside the existing gear paths (PackStoreBootstrap
                // registered the opener + find-or-spawns the store host-free on first open).
                case "OpenRealmStore": if (!ShopsClosedForCombat("OpenRealmStore")) PanelRouter.Open(PanelId.RealmStore); break;
                // RETIRED (2026-07-08): "OpenPetSkills" removed — the pet SKILL-TREE stack was deleted
                // (dead content; pets are harvest/companion-only per docs/COMBAT_PIVOT_NORTHSTAR.md).

                // ── Panels via find-or-spawn (no PanelId opener of their own) ──────
                case "OpenEquip": OpenEquipPanel(); break;
                case "OpenArena": OpenArenaPanel(); break;

                // ── Quests → QuestService ─────────────────────────────────────────
                case "StartQuest":   { var q = QuestService.Instance; if (q != null && a0 != null) q.StartQuest(a0); } break;
                case "AdvanceQuest": { var q = QuestService.Instance; if (q != null && a0 != null) q.AdvanceQuest(a0); } break;
                case "CompleteQuest":{ var q = QuestService.Instance; if (q != null && a0 != null) q.CompleteQuest(a0); } break;
                case "GiveKeystone": { var q = QuestService.Instance; if (q != null && a0 != null) q.GiveKeystone(a0); } break;
                // Yarn fires <<SetQuestFlag id flag>>; "SetFlag" kept as the existing alias.
                case "SetFlag":
                case "SetQuestFlag": { var q = QuestService.Instance; if (q != null && n >= 2) q.SetFlag(args[0], args[1]); } break;
                // Enrol a companion class into the persisted party roster (same join API the
                // wave/return joins use; fires PlayerChanged → StoryCompanionInjector spawns it).
                case "RecruitCompanion": if (!string.IsNullOrEmpty(a0)) GameStateService.Instance?.AddToParty(a0); break;

                // ── Building upgrades ─────────────────────────────────────────────
                // <<TryUpgradeBuilding "arcane-tower" 2>> — city tier tree (WO-430).
                case "TryUpgradeBuilding": if (!string.IsNullOrEmpty(a0)) Prog.BuildingUpgradeService.TryUpgrade(a0, ParseInt(a1)); break;
                // <<structure_upgrade $id>> — building level-up (spend + level).
                // BLIND-03-02 dual-authority guard: overlapping ids (forge/lumbermill) live in
                // BOTH the city-tier catalog AND ResourceBuildingProgression; firing both paths
                // double-upgrades. The precedence is resolved by the SHARED
                // Prog.UpgradeFamilyResolver — the SAME call BuildingUpgradeVM (start) and
                // CompletedUpgradeApplier (completion) make, so no site can drift out of step.
                // The CITY tier tree WINS for any overlapping building; resource-only buildings
                // (Farm etc.) still level via ResourceBuildingState.
                case "structure_upgrade":
                    if (!string.IsNullOrEmpty(a0))
                    {
                        string upId = DeNelle.Core.Catalog.CatalogRegistry.ResolveUpgradeId(a0);
                        if (Prog.UpgradeFamilyResolver.Resolve(upId) == Prog.UpgradeFamily.City)
                        {
                            FlowTrace.Step("Dialogue", $"structure_upgrade '{upId}' -> CITY tier authority (overlap precedence).");
                            Prog.BuildingUpgradeService.TryUpgrade(upId, ParseInt(a1));
                        }
                        else
                        {
                            Prog.ResourceBuildingState.TryUpgrade(upId);
                        }
                    }
                    break;

                // ── Audio ─────────────────────────────────────────────────────────
                case "play_sfx": PlaySfx(a0); break;

                // ── Economy / meta ────────────────────────────────────────────────
                case "save_game": GameStateService.Instance?.Save(); break;
                case "grant_resources_for_towers":
                    GameStateService.Instance?.AddCrystals(Mathf.Max(0, ParseInt(a0)) * TowerCrystalCost);
                    break;

                // ── Barracks troop training (WO-453) → TroopDialogueCommands ───────
                case "ShowTrainingUI": TroopDialogueCommands.ShowTrainingUI(); break;
                // WO-897: the Armies surface - compose an army once, muster the whole build-out.
                case "ShowMusterUI":   TroopDialogueCommands.ShowMusterUI(); break;
                case "StartTraining":  if (!string.IsNullOrEmpty(a0)) TroopDialogueCommands.StartTraining(a0, ParseInt(a1)); break;

                // ── Pets → PetDeployer / PetAcquisitionService ────────────────────
                case "spawn_starting_pet": EnsurePetDeployer()?.DeployStarterPets(); break;
                case "spawn_named_pet":    SpawnNamedPet(a0); break;
                // WO-1031: the "pet_task" verb is REMOVED with the world engage prompt that
                // was its only producer. Echo tasking lives in the Echo tab (EchoCardView ->
                // EchoAssignments: the WO-830 resource picker + the WO-811 repair chip).
                // Do NOT re-register it; an authored dialogue must not route tasking here.

                // ── Camera → SmartMobileCamera (synchronous; no auto-restore hold) ─
                case "camera_focus":
                case "camera_glance":         CameraFocus(a0); break;
                // These two stay DIRECT calls, not CameraShakeBridge.Shake, and that is deliberate:
                // the bridge refuses while HeroLocomotion.InputSuppressed is true, which is ALWAYS
                // the case during an authored dialogue — routing them through it would silence the
                // scripted beat entirely. They still gate on CameraShakeBridge.Enabled (2026-08-16)
                // so the player's screen-shake comfort preference is honoured here too.
                case "camera_shake":          if (CameraShakeBridge.Enabled) SmartMobileCamera.Instance?.Shake(Mathf.Clamp(ParseFloat(a0), 0.05f, 0.6f), 0.45f); break;
                case "camera_show_all_gates": if (CameraShakeBridge.Enabled) SmartMobileCamera.Instance?.Shake(0.08f, 0.3f); break;
                case "camera_return_to_hero": { var h = HeroTransform(); if (h != null) SmartMobileCamera.Instance?.SetTarget(h); } break;

                // -- WO-1389: the post-first-raid beat's DOORS ---------------------
                // <<OpenManageTroops [troopId]>> lands on Manage > Troops (optionally preselecting a
                // troop card); <<OpenJourney>> opens the Journey deck where the Raids card lives.
                // Both route through PanelRouter like every other panel verb here - no new opener.
                case "OpenManageTroops":
                    if (!PanelBlockedByBattle("OpenManageTroops"))
                        PanelRouter.Open(PanelId.Manage, string.IsNullOrEmpty(a0) ? "Troops" : "Troops:" + a0);
                    break;
                case "OpenJourney":
                    if (!PanelBlockedByBattle("OpenJourney")) PanelRouter.Open(PanelId.JourneyDeck);
                    break;

                // -- WO-1415: the Heartfire introduction beat's ONE door ------------
                // <<OpenRaids>> lands on the raid card grid itself, not the deck the card
                // sits on - the beat is read on the way OUT of that grid (the WO-795 modal
                // truce), so its door has to be able to put the player straight back in.
                // (!) NO SECOND CAPABILITY CHECK HERE. RaidSelectionScreen.Open is the single
                // door every raid entry passes through and it asks PostureSignals.RaidCapable
                // FIRST (WO-1374); a hand-rolled check beside it is the second predicate that
                // header forbids by name, and the drift between two checks IS the defect.
                case "OpenRaids":
                    if (!PanelBlockedByBattle("OpenRaids")) DeNelle.Village.Hero.RaidSelectionScreen.Open();
                    break;

                // ── HUD objective / hint / highlight → TutorialHudOverlay ─────────
                case "set_hud_objective": EnsureOverlay()?.SetObjective(a0, ParseInt(a1), ParseInt(a2)); break;
                case "set_hud_hint":      EnsureOverlay()?.SetHint(a0); break;
                case "highlight_ui":      EnsureOverlay()?.Highlight(a0, true); break;
                case "unhighlight_ui":    EnsureOverlay()?.Highlight(a0, false); break;

                // ── Movement / control → TutorialAutoWalk / onboarding handoff ────
                case "start_autowalk": StartAutowalk(a0); break;
                case "stop_autowalk":  _autoWalk?.Stop(); break;
                case "enable_full_controls": EnableFullControls(); break;

                // ── Speaker portrait ──────────────────────────────────────────────
                case "portrait": DeNelle.Core.DialoguePortrait.Forced = a0; break;

                default:
                    FlowTrace.Warn("Dialogue",
                        $"command sink: verb '{verb}' has no backing service yet (custom-dialogue migration). " +
                        "Stub/Yarn-var/timed-wait verbs intentionally no-op here.");
                    break;
            }
        }

        // Crystals granted per "tower" by grant_resources_for_towers N (mirrors the bridge).
        private const int TowerCrystalCost = 50;

        // ── Panels (find-or-spawn) ───────────────────────────────────────────────

        private static void OpenEquipPanel()
        {
            if (PanelBlockedByBattle("OpenEquip")) return;
            var panel = Object.FindAnyObjectByType<DeNelle.Village.Hero.EquipmentPanel>();
            if (panel == null) panel = new GameObject("EquipmentPanelHost").AddComponent<DeNelle.Village.Hero.EquipmentPanel>();
            panel.Open();
        }

        private static void OpenArenaPanel()
        {
            if (PanelBlockedByBattle("OpenArena")) return;
            var panel = Object.FindAnyObjectByType<DeNelle.Village.Arena.ArenaPanel>();
            if (panel == null) panel = new GameObject("ArenaPanelHost").AddComponent<DeNelle.Village.Arena.ArenaPanel>();
            panel.Open();
        }

        // A dialogue verb must NOT pop a gameplay panel mid-battle (WO-437).
        private static bool PanelBlockedByBattle(string verb)
        {
            if (!DeNelle.Core.Combat.BattleLock.IsInBattle()) return false;
            FlowTrace.Warn("Input", "battle-lock: dialogue verb '" + verb + "' skipped (in battle)");
            return true;
        }

        // ── Audio ────────────────────────────────────────────────────────────────

        private static void PlaySfx(string id)
        {
            switch (id)
            {
                case "horn_warning": GameSfx.PlayLookoutHorn(); break;
                default:             CoreServices.Audio?.PlayUiClick(); break;
            }
        }

        // ── Camera ───────────────────────────────────────────────────────────────

        private void CameraFocus(string targetName)
        {
            Transform t = ResolveTransform(targetName);
            if (t == null) { FlowTrace.Warn("Dialogue", $"camera_focus '{targetName}' unresolved — skipping."); return; }
            SmartMobileCamera.Instance?.SetTarget(t);
        }

        // ── HUD overlay (lazily hosted) ──────────────────────────────────────────

        private GameObject EnsureHost()
        {
            if (_host == null) _host = new GameObject("DialogueSinkHost");
            return _host;
        }

        private TutorialHudOverlay EnsureOverlay()
        {
            if (_overlay == null) _overlay = EnsureHost().AddComponent<TutorialHudOverlay>();
            return _overlay;
        }

        // ── Movement / control ───────────────────────────────────────────────────

        private void StartAutowalk(string destName)
        {
            if (_autoWalk == null) _autoWalk = EnsureHost().AddComponent<TutorialAutoWalk>();
            var hero = Object.FindAnyObjectByType<HeroLocomotion>();
            if (hero != null) _autoWalk.SetHero(hero);
            Transform t = ResolveTransform(destName);
            if (t != null) _autoWalk.WalkTo(t.position);
            else FlowTrace.Warn("Dialogue", $"start_autowalk '{destName}' unresolved — staying put.");
        }

        private void EnableFullControls()
        {
            _autoWalk?.Stop();
            // Finish onboarding (opens the wave loop) exactly once, at the END of the narrative.
            var svc = GameStateService.Instance;
            if (svc != null && svc.State != null && !svc.State.Onboarded) svc.FinishOnboarding();
        }

        // ── Pets ─────────────────────────────────────────────────────────────────

        private void SpawnNamedPet(string species)
        {
            if (string.IsNullOrWhiteSpace(species))
            { FlowTrace.Warn("Dialogue", "spawn_named_pet — empty species arg; skipping."); return; }

            var deployer = EnsurePetDeployer();
            if (deployer == null) { FlowTrace.Warn("Dialogue", "spawn_named_pet — could not create a PetDeployer."); return; }

            // Record ownership exactly once (idempotent); PetAcquisitionService bootstraps itself.
            var acq = PetAcquisitionService.Instance;
            if (acq != null) acq.Acquire(species, PetAcquisitionSource.Gift);
            deployer.DeployChosen(species);
        }

        // Self-heal a PetDeployer if the scene ships none (mirrors DialogueCommandBridge.EnsurePetDeployer).
        private PetDeployer EnsurePetDeployer()
        {
            var deployer = Object.FindAnyObjectByType<PetDeployer>();
            if (deployer != null) return deployer;

            var go = new GameObject("PetDeployer");
            deployer = go.AddComponent<PetDeployer>();

            Vector3 heartPos = Vector3.zero;
            var heart = Object.FindAnyObjectByType<HeartController>();
            if (heart != null) heartPos = heart.transform.position;
            deployer.SetHeartPosition(heartPos);

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            deployer.SetEnemyMask(enemyLayer >= 0 ? (1 << enemyLayer) : ~0);

            var svc = GameStateService.Instance;
            if (svc != null && svc.State != null && svc.State.PetBonds != null)
            {
                var b = svc.State.PetBonds;
                int aether = b.Count > 0 ? b[0] : 0;
                int flame  = b.Count > 1 ? b[1] : 0;
                int ice    = b.Count > 2 ? b[2] : 0;
                deployer.SetBondRanks(aether, flame, ice);
            }
            return deployer;
        }

        // ── Target resolution (camera / autowalk) ────────────────────────────────

        private Transform ResolveTransform(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.ToLowerInvariant();
            switch (n)
            {
                case "companion":
                    return StoryCompanionInjector.Instance != null ? StoryCompanionInjector.Instance.CompanionTransform : null;
                case "pet":
                {
                    var pet = Object.FindAnyObjectByType<Pet>();
                    return pet != null ? pet.transform : null;
                }
                case "hero":
                case "player":
                    return HeroTransform();
                case "village_tour":
                {
                    var heart = Object.FindAnyObjectByType<HeartController>();
                    return heart != null ? heart.transform : HeroTransform();
                }
            }
            var go = GameObject.Find(name) ?? GameObject.Find(name.Replace('_', ' '));
            return go != null ? go.transform : null;
        }

        private Transform HeroTransform()
        {
            var loco = Object.FindAnyObjectByType<HeroLocomotion>();
            if (loco != null) return loco.transform;
            var tagged = GameObject.FindWithTag("Player");
            return tagged != null ? tagged.transform : null;
        }

        // ── Parsing ──────────────────────────────────────────────────────────────

        private static int ParseInt(string s) => int.TryParse(s, out int v) ? v : 0;
        private static float ParseFloat(string s) => float.TryParse(s, out float v) ? v : 0f;

        // ── Conditions → live game state ─────────────────────────────────────────
        // Keys: !<key> (negation) · quest_<id>_active · quest_<id>_done ·
        //       keystone_<name> · keystone_count_min_<n> · pet_owned_<species> ·
        //       pet_grantable_<species> · pet_select_closed · jeweler_unlocked · onboarded.
        //       Unknown => false (logged).
        public bool Check(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return true;

            // Negation infra (mirrors Yarn's `not`/`== false` content).
            if (condition[0] == '!') return !Check(condition.Substring(1));

            var svc = QuestService.Instance;

            const string qp = "quest_";
            if (svc != null && condition.StartsWith(qp) && condition.EndsWith("_active"))
                return svc.IsActive(condition.Substring(qp.Length, condition.Length - qp.Length - "_active".Length));
            if (svc != null && condition.StartsWith(qp) && condition.EndsWith("_done"))
                return svc.IsCompleted(condition.Substring(qp.Length, condition.Length - qp.Length - "_done".Length));

            // keystone_count_min_<n> MUST be tested before the keystone_ prefix.
            const string kc = "keystone_count_min_";
            if (condition.StartsWith(kc))
                return svc != null && svc.KeystoneCount >= ParseInt(condition.Substring(kc.Length));
            if (condition.StartsWith("keystone_"))
                return svc != null && svc.HasKeystone(condition.Substring("keystone_".Length));

            const string po = "pet_owned_";
            if (condition.StartsWith(po))
            {
                var acq = PetAcquisitionService.Instance;
                return acq != null && acq.Owns(condition.Substring(po.Length));
            }

            // pet_grantable_<species> — TRUE when the player does NOT own this echo AND a free
            // deploy slot exists (so the Echo Hollow may offer it). Mirrors the Yarn
            // <<if not pet_owned("x")>> option gate, but ALSO folds in the A7 "no free slot"
            // closure (the custom model's `requires` is a single key, so this composite key
            // replaces the AND that Yarn expressed with two separate gates). No service =>
            // lenient (allow), matching a fresh pre-bootstrap state.
            const string pg = "pet_grantable_";
            if (condition.StartsWith(pg))
            {
                var acq = PetAcquisitionService.Instance;
                if (acq == null) return true;
                return !acq.Owns(condition.Substring(pg.Length)) && acq.FilledSlotCount < acq.MaxSlots;
            }

            // pet_select_closed — A7 whole-flow gate: TRUE when the player already owns an echo
            // AND has no free deploy slot, so the Echo Hollow must NOT offer a second attune.
            // Mirrors DialogueCommandBridge.FnPetSelectClosed (owns-any AND slots-full); the
            // starting cap is 1 (PetAcquisitionService.DefaultMaxSlots), Fenn's questline raises it.
            if (condition == "pet_select_closed")
            {
                var acq = PetAcquisitionService.Instance;
                if (acq == null) return false; // no service -> selection open
                bool ownsAny = acq.Owns("ice-wolf") || acq.Owns("flame-pup") || acq.Owns("aether-sprite");
                return ownsAny && acq.FilledSlotCount >= acq.MaxSlots;
            }

            // The storefront and bench converge on the same polishing panel only after
            // the first dungeon-earned rough stone. Before then, hide the route instead
            // of advertising a destination that correctly refuses to open.
            if (condition == "jeweler_unlocked")
                return DeNelle.Village.Crafting.JewelerProgression.IsUnlocked;

            if (condition == "onboarded")
            {
                var gs = GameStateService.Instance;
                return gs != null && gs.State != null && gs.State.Onboarded;
            }

            FlowTrace.Warn("Dialogue", $"condition '{condition}' unknown — treated false.");
            return false;
        }
    }
}
