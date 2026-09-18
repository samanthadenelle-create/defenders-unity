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
// ⚠ WO-1858 (2026-09-17): gree/unity-webview is now installed and EnsureBuilt binds the
// real host (ClanChatWebHostFactory.Create) instead of always falling back to
// ClanChatWebHostUnavailable — see IClanChatWebHost.cs's header for the resolution and
// ClanChatWebHostGreeWebView.cs for the implementation. The room is fed by
// ClanMembershipClient (GET /api/clan/me) on every open. Every other part of the flow —
// the bridge parsing, the unread badge, the report path — is unchanged and is exercised
// by Assets/Tests/EditMode/ClanChatVMTests.cs, ClanMembershipClientTests.cs and
// test/clan-chat-embed.test.js. The ONE remaining blocker to a working embed is
// CHERRY_APP_ID in site/clan-chat.html, which stays a placeholder by design (out of scope
// for both WO-1847 and WO-1858 — see that file's header).
//
// The room is the clan's SERVER-SIDE UUID (clans.id) and nothing else; see
// ClanRoomBinding in ClanChatSource.cs for why that was the one open wiring point.
// =============================================================================

using System;
using Cysharp.Threading.Tasks;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using TMPro;
using UnityEngine;

namespace DeNelle.HUD
{
    /// <summary>
    /// Clan chat's player-facing copy keys (WO-1858). English lives in
    /// Data/Canonical/en.json under the "clanChat.*" keys; this class names keys only —
    /// the standing localization law (no hardcoded player-facing literals).
    /// </summary>
    internal static class ClanChatStrings
    {
        public const string KeyNoWallet = "clanChat.noWallet";
        public const string KeyNoClan = "clanChat.noClan";
        public const string KeyUnavailable = "clanChat.unavailable";
        public const string KeyGenericError = "clanChat.genericError";
        public const string KeyOpening = "clanChat.opening";

        public static readonly LocalizedText NoWallet = new LocalizedText(KeyNoWallet);
        public static readonly LocalizedText NoClan = new LocalizedText(KeyNoClan);
        public static readonly LocalizedText Unavailable = new LocalizedText(KeyUnavailable);
        public static readonly LocalizedText GenericError = new LocalizedText(KeyGenericError);
        public static readonly LocalizedText Opening = new LocalizedText(KeyOpening);
    }

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
        /// WO-1870: the height of the always-visible Circle-door strip at the bottom of the chat
        /// body, in reference px. AUTHORED ABOVE THE TOUCH FLOOR rather than left to
        /// ClampMinTouch - the button inside it takes 0.08-0.92 of the strip, so the strip must
        /// be tall enough that the FACE still clears MinTouchPx.
        /// </summary>
        private const float CircleDoorStripPx = ElarionUiKit.MinTouchPx / 0.84f + 8f;

        /// <summary>
        /// Unread messages in the clan room, straight from Cherry's own unreadState event.
        /// Zero when there is no VM, no room, or no embed — never a stale count.
        /// </summary>
        public int UnreadCount => _vm != null ? _vm.UnreadCount : 0;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Remnant Chat", () => SetVisible(false), () => _visible);
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
            if (_vm != null) _vm.Changed -= HandleVmChanged;
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
                // WO-1858: ask the backend which clan (if any) this wallet belongs to, every
                // open — membership can change between opens, and ClanRoomBinding.Changed
                // (via HandleVmChanged below) re-attempts EnsureLoaded once the answer lands.
                ClanMembershipClient.RefreshAsync().Forget();
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
            _vm.Changed += HandleVmChanged;

            _modal = ElarionUiKit.BuildObsidianModal("ClanChatUI",
                new LocalizedText("common.remnant_chat").Resolve(),
                new Vector2(0.24f, 0.10f), new Vector2(0.76f, 0.92f), () => SetVisible(false),
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "crest");

            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? (Transform)_modal.chrome.layout.body
                : _modal.chrome.content.transform;

            // ── WO-1870 DOOR 1: the Circle screen, reachable from inside Circle Chat ──────
            // ⛔ IT IS VISIBLE IN EVERY STATE, not only the no-Circle one. The owner looked here
            // for a join option and found a sentence and no door; a player already in a Circle
            // still needs the roster, the code and the ballots. The gear drawer cannot carry a
            // seventh row (AddDockTab is a full 2x3 grid), so this IS the discoverable entry.
            //
            // ⚠ AND IT IS CARVED OUT OF THE EMBED'S OWN RECT, not painted over it. The WebView
            // is a NATIVE surface laid over BodyViewport() and it occludes anything underneath,
            // so a button inside that rect would vanish the moment Cherry mounted. The strip
            // below is subtracted from the host area first, and _bodyHost then points at what
            // is left - which is what BodyViewport() measures.
            var doorStrip = new GameObject("CircleDoorStrip", typeof(RectTransform));
            doorStrip.transform.SetParent(body, false);
            var doorRt = (RectTransform)doorStrip.transform;
            doorRt.anchorMin = new Vector2(0f, 0f);
            doorRt.anchorMax = new Vector2(1f, 0f);
            doorRt.pivot = new Vector2(0.5f, 0f);
            doorRt.anchoredPosition = Vector2.zero;
            doorRt.sizeDelta = new Vector2(0f, CircleDoorStripPx);

            ElarionUiKit.Button(doorStrip.transform,
                new LocalizedText(CircleScreenPanel.ChatDoorKey).Resolve(),
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.10f, 0.08f), new Vector2(0.90f, 0.92f), OpenCircleScreen);

            var surface = new GameObject("EmbedSurface", typeof(RectTransform));
            surface.transform.SetParent(body, false);
            var surfaceRt = (RectTransform)surface.transform;
            surfaceRt.anchorMin = Vector2.zero;
            surfaceRt.anchorMax = Vector2.one;
            surfaceRt.offsetMin = new Vector2(0f, CircleDoorStripPx);
            surfaceRt.offsetMax = Vector2.zero;
            _bodyHost = surface.transform;

            // The ONE native widget left: a status line that carries the error state. Cherry
            // draws everything else, inside the web surface placed over this rect.
            _statusText = MakeText(_bodyHost, "", 15, ElarionUi.ParchmentDim, FontStyles.Italic,
                TextAlignmentOptions.Center, new Vector2(0.04f, 0.35f), new Vector2(0.96f, 0.65f));

            if (_host == null)
            {
                // WO-1858: gree/unity-webview is now installed — bind the real host when the
                // platform can host a view, falling back to ClanChatWebHostUnavailable
                // (unchanged honest-default behaviour) otherwise. See IClanChatWebHost.cs and
                // ClanChatWebHostGreeWebView.cs for the two halves of this decision.
                _host = ClanChatWebHostFactory.Create();
                _hostOwned = true;
                AttachHost();
            }

            _modal.canvas.SetActive(false);   // built hidden; SetVisible shows it
        }

        /// <summary>
        /// WO-1870 door 1. Routes through PanelRouter, so the modal arbiter closes this panel as
        /// the Circle screen opens - the two hold distinct PanelManager handles ("Remnant Chat"
        /// and "Circle") precisely so that hand-off is clean.
        /// </summary>
        private static void OpenCircleScreen()
        {
            FlowTrace.Step("ClanChat", "Circle door tapped - routing to the Circle screen.");
            PanelRouter.Open(PanelId.Circle);
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

        /// <summary>
        /// The VM's Changed handler. Repaints the status line AND, if the panel is open,
        /// re-attempts EnsureLoaded — the one case that matters is the room arriving AFTER
        /// the panel already opened in its NoRoom state (WO-1858: the clan/me answer lands
        /// asynchronously). EnsureLoaded is a safe no-op when nothing changed: it dedupes on
        /// _loadedUrl and early-returns when CanEmbed is still false.
        /// </summary>
        private void HandleVmChanged()
        {
            Repaint();
            if (_visible) EnsureLoaded();
        }

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
            if (!_vm.IsMounted) _statusText.text = ClanChatStrings.Opening.Resolve();
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
                case ClanChatVM.NoWallet: return ClanChatStrings.NoWallet.Resolve();
                // WO-1858 copy rule: plain language, never a "join a clan" imperative that
                // reads as blame — "You're not in a Remnant yet" (clanChat.noClan in en.json,
                // reworded from "clan" to "Remnant" by WO-1859).
                case ClanChatVM.NoRoom: return ClanChatStrings.NoClan.Resolve();
                case ClanChatWebHostUnavailable.Reason: return ClanChatStrings.Unavailable.Resolve();
                case "missing_app_id": return ClanChatStrings.Unavailable.Resolve();
                default: return ClanChatStrings.GenericError.Resolve();
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
