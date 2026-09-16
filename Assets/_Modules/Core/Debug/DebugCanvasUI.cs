// =============================================================================
// DEPRECATED + TAGGED FOR REMOVAL (owner 2026-06-28): superseded by the live
// Settings -> Dev Tools panel (AdminOverlay). Retained only until confirmed no
// unique tool is lost; safe to delete after that. Do not extend this panel.
// =============================================================================
// =============================================================================
// DebugCanvasUI — playtesting overlay (Editor / development builds only)
// -----------------------------------------------------------------------------
// Toggle with F12.  Attach to a Canvas GameObject in the boot scene.
// Wires to GameStateService.Instance for wallet binding, backend sync,
// and state inspection without touching production code paths.
//
// Field-name corrections vs. work-order template:
//   • GameStateService.Instance.State  (not .GameState — State is the public property)
//   • Resources.Crystals / .Food / .Coins  (not .Gold / .Gems / .XP)
//   • BindWallet() mutator instead of direct field write (so events fire + Save() runs)
//   • UniTaskVoid button handlers (avoids async void exception suppression)
// =============================================================================

using Cysharp.Threading.Tasks;
using DeNelle.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Core.DevOverlay
{
    /// <summary>
    /// Playtesting overlay — wallet binding, backend sync, and state inspection.
    /// Visible only in Editor / development builds (hidden via <c>#if</c> in Awake).
    /// Toggle with <c>F12</c>.
    /// </summary>
    public sealed class DebugCanvasUI : MonoBehaviour
    {
        // ── Inspector wiring ────────────────────────────────────────────────
        [Header("Buttons")]
        [SerializeField] private Button _bindWalletButton;
        [SerializeField] private Button _clearWalletButton;
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _loadButton;
        [SerializeField] private Button _printButton;
        [SerializeField] private Button _closeButton;

        [Header("Labels")]
        [SerializeField] private TextMeshProUGUI _walletLabel;
        [SerializeField] private TextMeshProUGUI _statusLog;

        // ── Lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            // In release builds this whole overlay is inactive — nothing leaks.
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            gameObject.SetActive(false);
            return;
#endif
            _bindWalletButton?.onClick.AddListener(() => OnBindWallet().Forget());
            _clearWalletButton?.onClick.AddListener(OnClearWallet);
            _saveButton?.onClick.AddListener(() => OnSave().Forget());
            _loadButton?.onClick.AddListener(() => OnLoad().Forget());
            _printButton?.onClick.AddListener(OnPrint);
            _closeButton?.onClick.AddListener(() => gameObject.SetActive(false));

            RefreshWalletLabel();
        }

        private void Update()
        {
            // PLAYER-BUILD SAFETY: the F12 toggle is gated behind the global
            // DevHotkeys kill-switch (default OFF) so a key press can never reveal
            // this overlay in the shipped .exe OR the editor unless a dev opts in
            // (PlayerPrefs ff.devhotkeys=1).
            if (DeNelle.Core.FeatureFlags.DevHotkeys && Input.GetKeyDown(KeyCode.F12))
                gameObject.SetActive(!gameObject.activeSelf);
        }

        // ── Button handlers ─────────────────────────────────────────────────

        private async UniTaskVoid OnBindWallet()
        {
            // Generate a deterministic fake wallet address for playtesting.
            var fake = "0xTEST" + System.Guid.NewGuid().ToString("N").Substring(0, 32).ToUpper();

            var svc = GameStateService.Instance;
            if (svc == null) { Log("GameStateService not found"); return; }

            svc.BindWallet(fake);          // raises PlayerChanged + Save()
            RefreshWalletLabel();
            Log($"Wallet bound: {fake[..14]}…");

            await svc.SaveBeforeSceneChange();
            Log("Sync triggered after wallet bind.");
        }

        private void OnClearWallet()
        {
            var svc = GameStateService.Instance;
            if (svc == null) { Log("GameStateService not found"); return; }

            svc.BindWallet(string.Empty);
            RefreshWalletLabel();
            Log("Wallet cleared.");
        }

        private async UniTaskVoid OnSave()
        {
            var svc = GameStateService.Instance;
            if (svc == null) { Log("GameStateService not found"); return; }

            Log("Saving to backend…");
            await svc.SaveBeforeSceneChange();
            Log("Save complete.");
        }

        private async UniTaskVoid OnLoad()
        {
            var svc = GameStateService.Instance;
            if (svc == null) { Log("GameStateService not found"); return; }

            Log("Loading from backend…");
            await svc.LoadFromBackend();
            RefreshWalletLabel();
            Log("Load complete.");
        }

        private void OnPrint()
        {
            var svc = GameStateService.Instance;
            if (svc == null) { Log("GameStateService not found"); return; }

            var s = svc.State;
            // ResourceBalance has Crystals / Food / Coins — not Gold/Gems/XP.
            UnityEngine.Debug.Log(
                $"[DebugCanvas] State snapshot\n" +
                $"  BoundWallet : {s.BoundWallet ?? "<none>"}\n" +
                $"  BestWave    : {s.BestWave}\n" +
                $"  Crystals    : {s.Resources.Crystals}  Food: {s.Resources.Stone}  Coins: {s.Resources.Coins}\n" +
                // WO-1212: the Stone readout now shows the LIVE Stone slot (Resources.Stone),
                // not the retired GameState.Stone field it used to print - the only place in
                // the game that ever displayed the dead balance.
                $"  Voidshards  : {s.Voidshards}  Stone: {s.Resources.Stone}  Iron: {s.Iron}  Wood: {s.Wood}\n" +
                $"  Towers      : [{string.Join(",", s.Towers)}]\n" +
                $"  TowerAbil   : [{string.Join(",", s.TowerAbilities)}]\n" +
                $"  Pets        : {s.Pets.Count} owned\n" +
                $"  OwnedPets   : {s.OwnedPets.Count}"
            );
            Log("State printed to Console.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private void RefreshWalletLabel()
        {
            if (_walletLabel == null) return;
            var wallet = GameStateService.Instance?.State?.BoundWallet;
            _walletLabel.text = string.IsNullOrEmpty(wallet)
                ? "<color=#FF6B6B>No Wallet Bound</color>"
                : $"<color=#6BFF9E>{wallet[..Mathf.Min(wallet.Length, 16)]}…</color>";
        }

        private void Log(string msg)
        {
            if (_statusLog == null) return;
            var ts = System.DateTime.Now.ToString("HH:mm:ss");
            _statusLog.text = $"[{ts}] {msg}\n" + _statusLog.text;

            // Cap log length so it doesn't grow forever.
            const int maxChars = 1200;
            if (_statusLog.text.Length > maxChars)
                _statusLog.text = _statusLog.text[..maxChars];
        }
    }
}
