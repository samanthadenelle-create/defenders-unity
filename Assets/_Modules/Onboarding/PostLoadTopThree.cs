using System.Collections;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core;
using DeNelle.Core.Combat;
using DeNelle.Core.Services;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Onboarding
{
    /// <summary>WO-1266: one non-blocking live Top 3 moment per app session.</summary>
    public sealed class PostLoadTopThree : MonoBehaviour
    {
        private static bool _shownThisSession;
        private ElarionUiKit.ObsidianModal _modal;
        private PanelHandle _panelHandle;
        private bool _open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_shownThisSession || Application.isBatchMode) return;
            if (FindAnyObjectByType<PostLoadTopThree>() != null) return;
            var go = new GameObject("[PostLoadTopThree]");
            DontDestroyOnLoad(go);
            go.AddComponent<PostLoadTopThree>();
        }

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Top 3 Players", Close, () => _open);
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            while (!SafeGameplayMoment()) yield return new WaitForSecondsRealtime(1f);
            IReadOnlyList<LeaderboardEntry> rows = null;
            LeaderboardService.Instance?.FetchTopAsync(LeaderboardMetric.BestWave, 3, result => rows = result);
            float deadline = Time.realtimeSinceStartup + 9f;
            while (rows == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (rows == null || rows.Count == 0) { Destroy(gameObject); yield break; }
            while (!SafeGameplayMoment()) yield return new WaitForSecondsRealtime(1f);
            Show(rows);
        }

        private static bool SafeGameplayMoment()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            string scene = SceneManager.GetActiveScene().name;
            bool gameplay = scene != SceneRouter.Title && scene != SceneRouter.HeroSelect && scene != SceneRouter.PetSelect;
            return state != null && state.Onboarded && gameplay && !PanelManager.AnyOpen && !BattleLock.IsInBattle();
        }

        private void Show(IReadOnlyList<LeaderboardEntry> rows)
        {
            _modal = ElarionUiKit.BuildObsidianModal("PostLoadTopThreeUI",
                new LocalizedText("onboarding.top_three.title").Resolve(),
                new Vector2(0.24f, 0.22f), new Vector2(0.76f, 0.78f), Close, sortingOrder: 31005);
            if (_modal?.canvas == null || _modal.chrome == null) { Destroy(gameObject); return; }
            _open = true;
            if (!PanelManager.NotifyOpened(_panelHandle)) { _open = false; Destroy(_modal.canvas); Destroy(gameObject); return; }

            // WO-1857: the heading and the row shape were hand-concatenated English. The row
            // becomes ONE positional format key (rank / name / score) so a locale can move
            // the WAVE word — or drop it — without this loop changing.
            var text = new StringBuilder(
                new LocalizedText("onboarding.top_three.heading").Resolve());
            text.Append("\n\n");
            int count = Mathf.Min(3, rows.Count);
            for (int i = 0; i < count; i++)
                text.Append(LocalText.Format("onboarding.top_three.row",
                                             rows[i].Rank, rows[i].Name, rows[i].Score))
                    .Append('\n');
            var label = ElarionUiKit.Label(_modal.chrome.content.transform, text.ToString(),
                0.30f, 0.88f, ElarionUi.Parchment, 36, TextAlignmentOptions.Center, 0.08f, 0.92f);
            if (label != null) { label.enableAutoSizing = true; label.fontSizeMin = 22f; label.fontSizeMax = 36f; }
            ElarionUiKit.BuildObsidianButton(_modal.chrome.content.transform,
                new LocalizedText("common.continue").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.30f, 0.05f), new Vector2(0.70f, 0.22f), Close);
            _shownThisSession = true;
        }

        private void Close()
        {
            _open = false;
            PanelManager.NotifyClosed(_panelHandle);
            if (_modal?.canvas != null) Destroy(_modal.canvas);
            Destroy(gameObject);
        }
    }
}
