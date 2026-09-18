// =============================================================================
// WalletConnectDialog — the connect / account control (Week 7)
// -----------------------------------------------------------------------------
// C# + UI Toolkit port of src/modules/wallet/ConnectWalletButton.tsx. A
// self-contained MonoBehaviour control with two states:
//
//   Disconnected → a "Connect Wallet" button. On a real provider this opens the
//                  Mobile Wallet Adapter on the Seeker / a deep-link wallet on
//                  iOS/desktop; with the StubWalletProvider it mock-connects.
//   Connected    → shows the short address, the network badge and a Disconnect
//                  button (the React dropdown's copy / disconnect actions).
//
// The dialog binds to a UIDocument's VisualTree (the host scene supplies the
// .uxml). It also exposes plain C# entry points (Connect / Disconnect) so a
// HUD or the title screen can drive it without UI Toolkit at all.
//
// async UniTask throughout — never async void (spec Part 3). Button callbacks
// fire-and-forget into UniTask via Forget(), which surfaces exceptions instead
// of swallowing them the way `async void` would.
// =============================================================================

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;

namespace DeNelle.Wallet
{
    /// <summary>
    /// Connect / account control for a wallet. Mirrors ConnectWalletButton.tsx —
    /// a "Connect Wallet" button when disconnected, an address + Disconnect
    /// control when connected.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class WalletConnectDialog : MonoBehaviour
    {
        [Tooltip("UIDocument hosting the connect-dialog visual tree. Falls back to the component on this GameObject.")]
        [SerializeField] private UIDocument _document;

        // Element names expected in the bound .uxml. A scene without these
        // simply runs headless — the plain Connect()/Disconnect() API still works.
        private const string ConnectButtonName = "wallet-connect-button";
        private const string DisconnectButtonName = "wallet-disconnect-button";
        private const string AddressLabelName = "wallet-address-label";
        private const string StatusLabelName = "wallet-status-label";
        private const string NetworkBadgeName = "wallet-network-badge";

        private WalletService _wallet;

        private Button _connectButton;
        private Button _disconnectButton;
        private Label _addressLabel;
        private Label _statusLabel;
        private Label _networkBadge;

        /// <summary>The wallet service this dialog drives.</summary>
        public WalletService Wallet => _wallet;

        /// <summary>Raised after a successful connect — carries the connected account.</summary>
        public event Action<WalletAccount> Connected;

        /// <summary>Raised after a disconnect.</summary>
        public event Action Disconnected;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            // Defaults to the devnet StubWalletProvider — no Solana SDK needed.
            _wallet = new WalletService();
            _wallet.StatusChanged += OnStatusChanged;
        }

        private void OnEnable()
        {
            BindElements();
            RefreshUi();
        }

        private void OnDisable()
        {
            UnbindElements();
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.StatusChanged -= OnStatusChanged;
        }

        /// <summary>
        /// Injects a specific <see cref="WalletService"/> (e.g. a shared one, or
        /// one over a real provider once the Solana SDK lands). Call before the
        /// first <see cref="OnEnable"/>, or it rebinds immediately if already live.
        /// </summary>
        public void SetWalletService(WalletService service)
        {
            if (service == null) return;
            if (_wallet != null) _wallet.StatusChanged -= OnStatusChanged;
            _wallet = service;
            _wallet.StatusChanged += OnStatusChanged;
            RefreshUi();
        }

        // =====================================================================
        //  UI Toolkit binding
        // =====================================================================

        private void BindElements()
        {
            using var _ = FlowTrace.Enter("WalletUI", "BindElements");
            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
            {
                // Headless is a supported mode (the plain Connect()/Disconnect() API still works), so
                // this is a Warn, not a Fail — but surface it so a "button does nothing" report has a trace.
                FlowTrace.Warn("WalletUI", "BindElements: UIDocument root is null — dialog runs headless (no buttons bound).");
                return;
            }

            _connectButton = root.Q<Button>(ConnectButtonName);
            _disconnectButton = root.Q<Button>(DisconnectButtonName);
            _addressLabel = root.Q<Label>(AddressLabelName);
            _statusLabel = root.Q<Label>(StatusLabelName);
            _networkBadge = root.Q<Label>(NetworkBadgeName);

            // V/U: a null Connect button means the player literally cannot start a connect from this UI —
            // surface it (was a silent null). Other elements are display-only; Warn so a capture pinpoints.
            if (_connectButton == null) FlowTrace.Warn("WalletUI", $"BindElements: connect button '{ConnectButtonName}' not found — player cannot connect from this dialog.");
            if (_disconnectButton == null) FlowTrace.Warn("WalletUI", $"BindElements: disconnect button '{DisconnectButtonName}' not found.");
            if (_statusLabel == null) FlowTrace.Warn("WalletUI", $"BindElements: status label '{StatusLabelName}' not found — connection state will be invisible.");

            if (_connectButton != null) _connectButton.clicked += OnConnectClicked;
            if (_disconnectButton != null) _disconnectButton.clicked += OnDisconnectClicked;
        }

        private void UnbindElements()
        {
            if (_connectButton != null) _connectButton.clicked -= OnConnectClicked;
            if (_disconnectButton != null) _disconnectButton.clicked -= OnDisconnectClicked;
        }

        private void OnConnectClicked() => Connect().Forget();

        private void OnDisconnectClicked() => Disconnect().Forget();

        // =====================================================================
        //  Connect / Disconnect — the public C# API
        // =====================================================================

        /// <summary>
        /// Opens the wallet-connect flow. Resolves with the connected account, or
        /// an invalid <see cref="WalletAccount"/> on failure / cancellation.
        /// </summary>
        public async UniTask<WalletAccount> Connect()
        {
            using var _ = FlowTrace.Enter("WalletUI", "Connect");
            if (_wallet == null)
            {
                FlowTrace.Fail("WalletUI", "Connect: no WalletService — cannot connect.");
                return default;
            }
            if (_wallet.IsConnected) return _wallet.Account;

            var account = await _wallet.Connect();
            if (account.IsValid)
            {
                FlowTrace.Step("WalletUI", $"Connect OK — {account.ShortAddress}.");
                Connected?.Invoke(account);
            }
            else
            {
                FlowTrace.Warn("WalletUI", "Connect: account invalid — cancelled/failed; dialog stays disconnected.");
            }
            RefreshUi();
            return account;
        }

        /// <summary>Disconnects the current wallet.</summary>
        public async UniTask Disconnect()
        {
            using var _ = FlowTrace.Enter("WalletUI", "Disconnect");
            if (_wallet == null)
            {
                FlowTrace.Warn("WalletUI", "Disconnect: no WalletService — nothing to disconnect.");
                return;
            }
            await _wallet.Disconnect();
            Disconnected?.Invoke();
            RefreshUi();
        }

        // =====================================================================
        //  Rendering
        // =====================================================================

        private void OnStatusChanged(WalletStatus status) => RefreshUi();

        private void RefreshUi()
        {
            if (_wallet == null)
            {
                FlowTrace.Warn("WalletUI", "RefreshUi: no WalletService — UI not refreshed.");
                return;
            }

            var status = _wallet.Status;
            var connected = status == WalletStatus.Connected;
            var connecting = status == WalletStatus.Connecting;

            if (_networkBadge != null)
            {
                _networkBadge.text = _wallet.NetworkLabel;
                _networkBadge.style.display = DisplayStyle.Flex;
            }

            if (_connectButton != null)
            {
                // WO-1857: two alternative captions, two keys. The disconnected face reuses the
                // existing settings.wallet.connect row (same job: the connect-wallet button verb).
                _connectButton.text = connecting
                    ? new LocalizedText("wallet.connect.connecting").Resolve()
                    : new LocalizedText("settings.wallet.connect").Resolve();
                _connectButton.SetEnabled(!connecting && !connected);
                _connectButton.style.display = connected ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_disconnectButton != null)
                _disconnectButton.style.display = connected ? DisplayStyle.Flex : DisplayStyle.None;

            if (_addressLabel != null)
            {
                _addressLabel.text = connected ? _wallet.Account.ShortAddress : string.Empty;
                _addressLabel.style.display = connected ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_statusLabel != null)
            {
                switch (status)
                {
                    // WO-1857: {0} = the connected wallet's own product name (Phantom, Solflare…),
                    // a proper noun supplied by the provider and never translated.
                    case WalletStatus.Connected:
                        _statusLabel.text = LocalText.Format("wallet.connect.connected_as",
                            _wallet.Account.WalletName);
                        break;
                    case WalletStatus.Connecting:
                        _statusLabel.text = new LocalizedText("wallet.connect.connecting").Resolve();
                        break;
                    default:
                        _statusLabel.text = new LocalizedText("wallet.connect.none").Resolve();
                        break;
                }
            }
        }
    }
}
