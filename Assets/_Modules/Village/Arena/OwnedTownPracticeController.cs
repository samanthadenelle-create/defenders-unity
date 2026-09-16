using System;
using System.Collections;
using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Village.World.Camps;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.Arena
{
    [DefaultExecutionOrder(-500)]
    public sealed class OwnedTownPracticeController : MonoBehaviour
    {
        private static OwnedTownPracticeSession _pending;
        private OwnedTownPracticeSession _session;
        private readonly List<Enemy> _attackers = new List<Enemy>();
        private readonly HashSet<int> _defeated = new HashSet<int>();
        private Func<bool> _battleProbe;
        private GameObject _ui;
        private bool _running;
        private string _failure;
        private float _deadline;
        private float _entryHp;
        private HeroHealth _practiceHero;
        public bool Running => _running;
        public int SpawnedCount => _attackers.Count;
        public string Failure => _failure;
        public OwnedTownPracticeOutcome Outcome => _session?.Outcome ?? OwnedTownPracticeOutcome.None;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPending() => _pending = null;

        public static bool TryEnter(out string reason)
        {
            if (_pending != null || UnityEngine.Object.FindAnyObjectByType<OwnedTownPracticeController>() != null)
            { reason = "Practice is already starting or in progress."; return false; }
            if (!SceneRouter.IsSceneInBuild(PracticeCombatPolicy.SceneName))
            { reason = "The practice scene is not installed."; return false; }
            var service = GameStateService.Instance;
            if (service == null) { reason = "The save service is unavailable."; return false; }
            if (!OwnedTownLayoutSnapshot.TryCreate(service.State.OwnedBase, service.State.HeroClass, out var snapshot, out reason) ||
                !OwnedTownPracticeSession.TryCreate(service.State.OwnedBase, snapshot,
                    new OwnedTownLayoutSnapshot(OwnedTownTemplateManifest.Load()), out var session, out reason)) return false;
            _pending = session;
            SceneRouter.GoTownPractice();
            return true;
        }

        private void Awake()
        {
            SceneOwnership.SetEnemyOwned(false);
            _battleProbe = () => _running;
            BattleLock.RegisterProbe(_battleProbe);
        }

        private IEnumerator Start()
        {
            _session = _pending; _pending = null;
            if (_session == null || !OwnedTownSnapshotImporter.TryImport(gameObject.scene, _session.CopyLayout(), out _failure))
            { _failure = _failure ?? "Enter practice from your saved town."; ShowPanel(); yield break; }
            float readyDeadline = Time.realtimeSinceStartup + 30f;
            while ((HeroHealth.Instance == null || HeroHealth.Instance.gameObject.scene != gameObject.scene) &&
                Time.realtimeSinceStartup < readyDeadline) yield return null;
            var hero = HeroHealth.Instance;
            if (hero == null || hero.gameObject.scene != gameObject.scene)
            { _failure = "The practice hero did not arrive."; ShowPanel(); yield break; }
            _entryHp = hero.Hp;
            _practiceHero = hero;
            hero.RestoreAfterPractice(hero.MaxHp);
            for (int i = 0; i < 3; i++) yield return null;
            if (!OwnedTownNavigation.Validate(gameObject.scene, out _failure)) { ShowPanel(); yield break; }
            Transform spawn = null;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                    if (node.name == "HeroStartPoint_PlayerSpawn") spawn = node;
            if (spawn == null) { _failure = "Practice entrance is missing."; ShowPanel(); yield break; }
            // Named gate slots, with existing catalog stats and the existing role/attack director.
            var slots = new[] { new Vector3(-3, 0, 0), Vector3.zero, new Vector3(3, 0, 0) };
            for (int i = 0; i < slots.Length; i++)
            {
                if (!NavMesh.SamplePosition(spawn.position + slots[i], out var hit, 3f, NavMesh.AllAreas))
                { _failure = "An attacker cannot reach the practice entrance."; StopAttackers(); ShowPanel(); yield break; }
                var def = GarrisonStatBlocks.BuildTypedDef("hollow-walker", 1);
                var enemy = EnemyFactory.Build(def, hit.position, Quaternion.identity, transform);
                if (enemy == null) { _failure = "Practice attacker content is unavailable."; StopAttackers(); ShowPanel(); yield break; }
                SceneManager.MoveGameObjectToScene(enemy.transform.root.gameObject, gameObject.scene);
                enemy.Configure("practice-attacker-" + i, def, hero.transform);
                enemy.SetBrainTarget(hero.transform);
                var brain = enemy.GetComponent<EnemyBrain>();
                if (brain == null) brain = enemy.gameObject.AddComponent<EnemyBrain>();
                brain.Role = EnemyBrain.RoleForId(def.Id); brain.RosterId = def.Id;
                EnemyBrain.ApplyRoleTactics(brain, brain.Role);
                enemy.Died += OnAttackerDied;
                _attackers.Add(enemy);
            }
            _running = true; _deadline = Time.time + 180f;
            ShowPanel();
            FlowTrace.Step("Practice", "OWNED_TOWN_PRACTICE_STARTED attackers=" + _attackers.Count);
        }

        private void Update()
        {
            if (!_running) return;
            if (HeroHealth.Instance == null || !HeroHealth.Instance.IsAlive || HeroHealth.Instance.PracticeDefeated || Time.time >= _deadline)
                Resolve(OwnedTownPracticeOutcome.Loss);
            else if (_defeated.Count == _attackers.Count) Resolve(OwnedTownPracticeOutcome.Win);
            else if (_attackers.Exists(e => e == null))
            { _failure = "An attacker disappeared before combat resolved. Please retry practice."; _running = false; StopAttackers(); ShowPanel(); }
        }

        private void OnAttackerDied(Enemy enemy) { if (enemy != null) _defeated.Add(enemy.GetInstanceID()); }

        private void Resolve(OwnedTownPracticeOutcome outcome)
        {
            if (!_running || !_session.TryResolve(outcome, out _failure)) return;
            _running = false; StopAttackers();
            _session.TrySaveCompletion(GameStateService.Instance, out _failure);
            if (_session.CompletionSaved) DeNelle.Core.Tutorial.TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.OwnedTownPracticed);
            ShowPanel();
        }

        private void StopAttackers()
        {
            foreach (var enemy in _attackers)
                if (enemy != null) enemy.gameObject.SetActive(false);
        }

        public void ReturnToTown()
        {
            if (_running) { _session.TryResolve(OwnedTownPracticeOutcome.Abandoned, out _); _running = false; StopAttackers(); }
            else if (_session != null && (Outcome == OwnedTownPracticeOutcome.Win || Outcome == OwnedTownPracticeOutcome.Loss) &&
                !_session.TrySaveCompletion(GameStateService.Instance, out _failure)) { ShowPanel(); return; }
            // Startup may refuse before a hero is captured. Restore only the actor whose
            // health this practice changed; default zero must never damage a late/new hero.
            if (_practiceHero != null && _practiceHero.gameObject.scene == gameObject.scene)
                _practiceHero.RestoreAfterPractice(_entryHp);
            _practiceHero = null;
            if (_session != null && _session.CompletionSaved) DeNelle.Core.Tutorial.TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.OwnedTownPracticed);
            SceneRouter.GoOwnedTown();
        }

        private void ShowPanel()
        {
            if (!string.IsNullOrEmpty(_failure)) FlowTrace.Warn("Practice", _failure);
            if (_ui != null) Destroy(_ui);
            _ui = ElarionUiKit.BuildModalCanvas("TownPracticePanel", 650);
            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, LocalText.Get("ownedTown.practiceTitle"),
                new Vector2(.62f, .61f), new Vector2(.98f, .97f), ReturnToTown, withBackdrop: false, withClose: false);
            var body = chrome.content.transform;
            // The shared full-screen panel header band is too short in a compact HUD panel.
            // Fit both title and its shadow before adding body labels.
            foreach (var title in body.GetComponentsInChildren<TMP_Text>(true))
            {
                title.rectTransform.anchorMin = new Vector2(.06f, .82f);
                title.rectTransform.anchorMax = new Vector2(.94f, .96f);
                title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
            }
            string key = _running ? "practiceHint" : Outcome == OwnedTownPracticeOutcome.Win ? "practiceWin" : "practiceLoss";
            ElarionUiKit.Label(body, _failure ?? LocalText.Get("ownedTown." + key), .46f, .77f,
                ElarionUi.Parchment, 22, TextAlignmentOptions.TopLeft, .06f, .94f);
            ElarionUiKit.BuildObsidianButton(body, LocalText.Get("ownedTown." + (_running ? "practiceAbandon" : "practiceReturn")),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(.06f, .04f), new Vector2(.94f, .40f), ReturnToTown);
        }

        private void OnDestroy()
        {
            BattleLock.UnregisterProbe(_battleProbe);
            foreach (var enemy in _attackers) if (enemy != null) enemy.Died -= OnAttackerDied;
            if (_running) _session?.TryResolve(OwnedTownPracticeOutcome.Abandoned, out _);
            if (_ui != null) Destroy(_ui);
        }
    }
}
