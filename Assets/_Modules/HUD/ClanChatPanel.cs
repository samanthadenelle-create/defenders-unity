// =============================================================================
// ClanChatPanel — a THIN WEBVIEW HOST for Cherry's clan chat embed (WO-1847).
// -----------------------------------------------------------------------------
// ⛔ WHAT THIS PANEL STOPPED DOING. Until WO-1847 it rendered clan chat itself: a
// scrollable message list, a phrase-chip rail built from ChatPhraseCatalog, a custom
// free-text composer, and the create/leave clan form. ALL OF IT IS GONE. Cherry owns
// message rendering, delivery and persistence now, so a native renderer here would be a
// second, always-stale view of a list this game no longer holds. What is left is the
// Obsidian frame (chrome, the ONE shared Close, the tap-outside scrim, PanelManager
// membership) wrapped around a web surface — nothing more.
//
// ⛔ AND WHAT IT DELIBERATELY DOES NOT DO: there is NO native fallback. If the embed
// fails to load the panel shows a non-blocking error and stops. Falling back to the old
// UI is the ticket's explicit non-scope, and it would be worse than the error: a player
// typing into a local-only chat nobody receives is a silent failure, which is the exact
// shape §12 exists to forbid.
//
// ⚠ NO WEBVIEW PLUGIN IS PRESENT IN THIS PROJECT YET (verified at source 2026-09-17 —
// see IClanChatWebHost.cs for the evidence and the two candidate plugins). So the default
// host is ClanChatWebHostUnavailable and this panel ships permanently in that error state
// until a plugin is adopted and handed to Bind(). Every other part of the flow — the room
// scoping, the bridge parsing, the unread badge, the report path — is complete and is
// exercised by Assets/Tests/EditMode/ClanChatVMTests.cs and test/clan-chat-embed.test.js.
//
// The room is the clan's SERVER-SIDE UUID (clans.id) and nothing else; see
// ClanRoomBinding in ClanChatSource.cs for why that is the one open wiring point.
// =============================================================================

using System;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using TMPro;
using UnityEngine;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class ClanChatPanel : MonoBehaviour
    {
        private ElarionUiKit.ObsidianModal _modal;
        private Transform _bodyHost;
        private TextMeshProUGUI _statusText;

        private bool _visible;

        private ClanChatVM _vm;
        private IClanChatWebHost _host;
        private bool _hostOwned;          // true when this panel created the host and must dispose it
        private string _loadedUrl;

        private PanelHandle _panelHandle;

        /// <summary>
        /// Unread messages in the clan room, straight from Cherry's own unreadState event.
        /// Zero when there is no VM, no room, or no embed — never a stale count.
        /// </summary>
        public int UnreadCount => _vm != null ? _vm.UnreadCount : 0;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Clan Chat", () => SetVisible(false), () => _visible);
        }

        /// <summary>
        /// Hand this panel a real WebView host. Call BEFORE the first open; the panel takes
        /// ownership only of a host it created itself, so a caller-supplied host outlives it.
        /// </summary>
        public void Bind(IClanChatWebHost host)
        {
            DetachHost();
            _host = host;
            _hostOwned = false;
            AttachHost();
        }

        private void OnDestroy()
        {
            DetachHost();
            if (_hostOwned) _host?.Dispose();
            _host = null;
            if (_vm != null) _vm.Changed -= Repaint;
            _vm?.Dispose();
            _vm = null;
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        // Mobile-first: the panel opens via Toggle() (public), called by the kit HUD chat
        // dock (HudKitController.OpenClanChat). No key poll, no hotkey.
        public void Toggle() => SetVisible(!_visible);

        private void SetVisible(bool on)
        {
            if (on)
            {
                FlowTrace.Step("ClanChat", "SetVisible(true) — opening the Cherry chat host.");
                EnsureBuilt();
            }
            if (_modal == null || _modal.canvas == null) { _visible = false; return; }
            _visible = on;
            _modal.canvas.SetActive(on);
            if (on)
            {
                if (!PanelManager.NotifyOpened(_panelHandle))
                {
                    _visible = false;
                    _modal.canvas.SetActive(false);   // battle-lock reject — never force-show
                    _host?.SetVisible(false);
                    return;
                }
                Repaint();
                EnsureLoaded();
            }
            else
            {
                // The surface is HIDDEN, not destroyed: tearing the embed down on every close
                // would re-run Cherry's wallet connect and re-fetch the SDK each time the
                // player glanced at chat.
                _host?.SetVisible(false);
                PanelManager.NotifyClosed(_panelHandle);
            }
        }

        // ── UI construction (kit modal, lazy on first open) ──────────────────
        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;
            using var _ = FlowTrace.Enter("ClanChat", "EnsureBuilt");

            _vm = ClanChatVM.CreateDefault(() => SetVisible(false));
            _vm.Changed += Repaint;

            _modal = ElarionUiKit.BuildObsidianModal("ClanChatUI", "Clan Chat",
                new Vector2(0.24f, 0.10f), new Vector2(0.76f, 0.92f), () => SetVisible(false),
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "crest");

            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? (Transform)_modal.chrome.layout.body
                : _modal.chrome.content.transform;
            _bodyHost = body;

            // The ONE native widget left: a status line that carries the error state. Cherry
            // draws everything else, inside the web surface placed over this rect.
            _statusText = MakeText(body, "", 15, ElarionUi.ParchmentDim, FontStyles.Italic,
                TextAlignmentOptions.Center, new Vector2(0.04f, 0.35f), new Vector2(0.96f, 0.65f));

            if (_host == null)
            {
                // No plugin present — the honest default. See this file's header.
                _host = new ClanChatWebHostUnavailable();
                _hostOwned = true;
                FlowTrace.Warn("ClanChat", "no WebView host bound — using ClanChatWebHostUnavailable; " +
                                           "the panel will show its error state (see IClanChatWebHost.cs).");
                AttachHost();
            }

            _modal.canvas.SetActive(false);   // built hidden; SetVisible shows it
        }

        private void AttachHost()
        {
            if (_host == null) return;
            _host.MessageReceived += OnBridgeMessage;
            _host.LoadFailed += OnHostLoadFailed;
        }

        private void DetachHost()
        {
            if (_host == null) return;
            _host.MessageReceived -= OnBridgeMessage;
            _host.LoadFailed -= OnHostLoadFailed;
        }

        // ── Load ─────────────────────────────────────────────────────────────
        private void EnsureLoaded()
        {
            if (_vm == null || _host == null) return;

            if (!_vm.CanEmbed)
            {
                // No wallet or no room: the VM already holds the reason, and Repaint showed it.
                FlowTrace.Warn("ClanChat", "not embedding — reason=" + (_vm.ErrorReason ?? "unknown"));
                _host.SetVisible(false);
                return;
            }

            _host.SetViewport(BodyViewport());

            var url = _vm.EmbedUrl;
            if (!string.Equals(_loadedUrl, url, StringComparison.Ordinal))
            {
                _loadedUrl = url;
                FlowTrace.Step("ClanChat", "loading the Cherry host page for the bound clan room.");
                // Load() on the unavailable host raises LoadFailed immediately, which is how the
                // missing plugin becomes a named error instead of a blank panel.
                Guard.Try("ClanChat", "host.Load", () => _host.Load(url));
            }
            _host.SetVisible(true);
        }

        /// <summary>
        /// The modal body as a normalised screen rect, so the web surface lands inside the
        /// Obsidian frame rather than over the whole screen.
        /// </summary>
        private Rect BodyViewport()
        {
            // The modal is built with these normalised anchors (see EnsureBuilt); reading them
            // back off the RectTransform keeps the two from drifting if the frame is retuned.
            var rt = _bodyHost as RectTransform;
            if (rt == null) return new Rect(0.24f, 0.10f, 0.52f, 0.82f);
            return Guard.Try("ClanChat", "BodyViewport", () =>
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                float w = Mathf.Max(1f, Screen.width);
                float h = Mathf.Max(1f, Screen.height);
                float x0 = Mathf.Clamp01(corners[0].x / w);
                float y0 = Mathf.Clamp01(corners[0].y / h);
                float x1 = Mathf.Clamp01(corners[2].x / w);
                float y1 = Mathf.Clamp01(corners[2].y / h);
                return new Rect(x0, y0, Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
            }, new Rect(0.24f, 0.10f, 0.52f, 0.82f));
        }

        // ── Bridge (payloads from site/clan-chat.html's sendToUnity) ──────────

        [Serializable]
        private sealed class BridgePayload
        {
            public string type;
            public int count;
            public string reason;
            public string detail;
            public string messageId;
            public string clanId;
            public bool signedIn;
        }

        private void OnBridgeMessage(string json)
        {
            // ⛔ NEVER TRUST THE PAYLOAD. It arrives from a page that loads a third-party SDK,
            // so every field is parsed under Guard and a malformed payload is logged and
            // skipped, never allowed to throw into the Unity loop.
            Guard.Try("ClanChat", "OnBridgeMessage", () =>
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var p = JsonUtility.FromJson<BridgePayload>(json);
                if (p == null || string.IsNullOrEmpty(p.type))
                {
                    FlowTrace.Warn("ClanChat", "bridge payload had no type — ignored.");
                    return;
                }

                switch (p.type)
                {
                    case "mounted":
                        FlowTrace.Step("ClanChat", "Cherry embed mounted.");
                        _vm?.OnMounted();
                        break;

                    case "unread":
                        _vm?.OnUnread(p.count);
                        break;

                    case "report":
                        OnReportRequested(p.messageId);
                        break;

                    case "auth":
                        FlowTrace.Step("ClanChat", "embed auth state changed: signedIn=" + p.signedIn);
                        break;

                    case "warn":
                        FlowTrace.Warn("ClanChat", "embed warning: " + p.reason + " " + p.detail);
                        break;

                    case "error":
                        FlowTrace.Fail("ClanChat", "embed error: " + p.reason + " " + p.detail);
                        _vm?.OnError(p.reason);
                        break;

                    default:
                        FlowTrace.Warn("ClanChat", "unknown bridge type: " + p.type);
                        break;
                }
            });
        }

        private void OnHostLoadFailed(string reason)
        {
            FlowTrace.Fail("ClanChat", "web host failed to load: " + reason);
            _vm?.OnError(reason);
        }

        /// <summary>
        /// The player reported a message in the embed. The SIGNED POST happens on this side —
        /// the page is never handed a key or a session.
        /// </summary>
        private void OnReportRequested(string messageId)
        {
            if (_vm == null) return;
            FlowTrace.Step("ClanChat", "report requested from the embed — sending signed POST.");
            _vm.ReportMessage(messageId, ok =>
            {
                if (ok) FlowTrace.Step("ClanChat", "report recorded by the server.");
                else FlowTrace.Warn("ClanChat", "report was NOT recorded — see the preceding line.");
            });
        }

        // ── Repaint ──────────────────────────────────────────────────────────

        private void Repaint()
        {
            if (_modal == null || !_visible || _vm == null || _statusText == null) return;

            if (_vm.HasError)
            {
                _statusText.gameObject.SetActive(true);
                _statusText.text = PlayerFacingError(_vm.ErrorReason);
                _host?.SetVisible(false);
                return;
            }

            // No error: Cherry's own UI is the content, so the native status line gets out of
            // the way entirely rather than sitting behind the web surface.
            _statusText.gameObject.SetActive(!_vm.IsMounted);
            if (!_vm.IsMounted) _statusText.text = "Opening clan chat...";
        }

        /// <summary>
        /// Machine reason -> plain player copy. ⛔ No investment language and no blame: the
        /// panel is a communication surface, not a financial one, and an error here is never
        /// something the player did wrong.
        /// </summary>
        private static string PlayerFacingError(string reason)
        {
            switch (reason)
            {
                case ClanChatVM.NoWallet: return "Connect your wallet to use clan chat.";
                case ClanChatVM.NoRoom: return "Join a clan to use clan chat.";
                case ClanChatWebHostUnavailable.Reason: return "Clan chat is not available in this build.";
                case "missing_app_id": return "Clan chat is not available in this build.";
                default: return "Clan chat could not load. Try again in a moment.";
            }
        }

        // ── uGUI helper ──────────────────────────────────────────────────────

        private static TextMeshProUGUI MakeText(Transform parent, string text, float size,
            Color color, FontStyles style, TextAlignmentOptions align, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            ElarionUiKit.EnsureFont(t);
            return t;
        }
    }
}
