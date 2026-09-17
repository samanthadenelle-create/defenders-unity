// =============================================================================
// IClanChatWebHost — the ONE seam between ClanChatPanel and a WebView plugin.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ THE FINDING THIS FILE EXISTS FOR: THIS PROJECT HAS NO WEBVIEW PLUGIN.
// Verified at source on 2026-09-17, not assumed — Packages/manifest.json lists no
// WebView package, and there is no Vuplex / unity-webview (gree) / UniWebView anywhere
// under Assets/ or Packages/. Unity ships no built-in WebView either. WO-1847 needs one
// to host Cherry's embed (site/clan-chat.html), so adopting a plugin is a real decision
// with a real cost, and it belongs to the owner and the lead, not to this lane:
//
//   * gree/unity-webview — free, MIT, imported as a .unitypackage. Its bridge convention
//     is a `unity:` scheme navigation, which site/clan-chat.html already speaks.
//   * Vuplex 3D WebView — paid, per-platform. Better supported; a spend decision under
//     the standing stop-loss.
//
// Until one is chosen, ClanChatPanel binds Unavailable below and the panel shows its
// error state — which is EXACTLY the shipped behaviour WO-1847 already specifies for a
// failed embed load ("show a non-blocking error and do not fall back to the old native
// UI"). So the panel is complete and correct today; it is simply always in that state.
//
// ⚠ AND WHY THERE IS NO HAND-ROLLED ANDROID WEBVIEW HERE. Driving android.webkit.WebView
// through AndroidJavaObject is a new native-infrastructure subsystem — UI-thread
// marshalling, view hierarchy insertion under UnityPlayerActivity, lifecycle, an input
// focus fight with Unity — smuggled into a player-facing ticket, and unverifiable without
// Unity and a device. The architecture rule (bounded context; never smuggle structural
// work into player-facing work) says that is its own ticket. This seam is what makes that
// ticket a drop-in: implement this interface, hand it to ClanChatPanel.Bind, done.
// =============================================================================

using System;
using UnityEngine;

namespace DeNelle.HUD
{
    /// <summary>
    /// One embedded web surface, owned by <see cref="ClanChatPanel"/>. A plugin-backed
    /// implementation fulfils this; <see cref="ClanChatWebHostUnavailable"/> stands in
    /// while no plugin is present.
    /// </summary>
    public interface IClanChatWebHost
    {
        /// <summary>True when this host can actually display a page on this platform.</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Show the given URL. Called once per open; implementations may reuse a view.
        /// </summary>
        void Load(string url);

        /// <summary>Show or hide the web surface without tearing it down.</summary>
        void SetVisible(bool visible);

        /// <summary>
        /// Place the surface, in NORMALISED screen coordinates (0..1, origin bottom-left) so
        /// the panel never has to know the plugin's pixel convention or the device's DPI. The
        /// panel passes the modal's own body rect, which is what keeps Cherry inside the
        /// Obsidian frame instead of over the whole screen.
        /// </summary>
        void SetViewport(Rect normalizedScreenRect);

        /// <summary>Tear the surface down and release the native view.</summary>
        void Dispose();

        /// <summary>
        /// Raised for every bridge payload the page sends (the raw JSON string from
        /// site/clan-chat.html's sendToUnity). Never assume it is well-formed — the panel
        /// parses it under Guard.
        /// </summary>
        event Action<string> MessageReceived;

        /// <summary>
        /// Raised when the host itself fails (no plugin, plugin init failure, navigation
        /// error). Carries a short machine reason for the trace, not player-facing copy.
        /// </summary>
        event Action<string> LoadFailed;
    }

    /// <summary>
    /// The no-plugin implementation: reports unavailable and fails the load immediately
    /// with a reason naming the actual cause, so the trace says "no WebView plugin" rather
    /// than leaving a blank panel to be re-diagnosed from scratch.
    /// </summary>
    public sealed class ClanChatWebHostUnavailable : IClanChatWebHost
    {
        /// <summary>The machine reason this host always fails with.</summary>
        public const string Reason = "no_webview_plugin";

        public bool IsAvailable => false;

        public event Action<string> MessageReceived;
        public event Action<string> LoadFailed;

        public void Load(string url)
        {
            // Fail LOUDLY into the trace and the panel's error state. A silent no-op here
            // is the banned shape: it turns a missing dependency into an unexplained blank
            // screen that costs a session to re-diagnose.
            LoadFailed?.Invoke(Reason);
        }

        public void SetVisible(bool visible) { }

        public void SetViewport(Rect normalizedScreenRect) { }

        public void Dispose()
        {
            MessageReceived = null;
            LoadFailed = null;
        }
    }
}
