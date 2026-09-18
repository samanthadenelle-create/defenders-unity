// =============================================================================
// IClanChatWebHost — the ONE seam between ClanChatPanel and a WebView plugin.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ HISTORY, KEPT SO THE DECISION TRAIL IS NOT LOST: as of 2026-09-17 this project had
// no WebView plugin at all (Packages/manifest.json listed none; no Vuplex / unity-webview
// / UniWebView under Assets/ or Packages/), so this interface shipped with only the
// Unavailable stand-in below and the panel permanently in its error state.
//
// ⛔ RESOLVED, WO-1858 (2026-09-17, same day): gree/unity-webview is installed via its
// UPM git-URL path (Packages/manifest.json: net.gree.unity-webview ->
// https://github.com/gree/unity-webview.git?path=/dist/package — verified real by
// reading dist/package/package.json over the GitHub API, not assumed from the README).
// ClanChatWebHostGreeWebView.cs implements this interface against the real
// Gree.UnityWebView.WebViewObject API and ClanChatPanel.EnsureBuilt binds it via
// ClanChatWebHostFactory.Create(), which falls back to Unavailable below only when
// WebViewObject.IsWebViewAvailable() says the current platform cannot host a view.
// See ClanChatWebHostGreeWebView.cs's header for the exact API citations.
//
// ⚠ AND WHY THERE IS NO HAND-ROLLED ANDROID WEBVIEW HERE. Driving android.webkit.WebView
// through AndroidJavaObject directly (bypassing a plugin) would have been a new native-
// infrastructure subsystem — UI-thread marshalling, view hierarchy insertion under
// UnityPlayerActivity, lifecycle, an input focus fight with Unity — smuggled into a
// player-facing ticket, and unverifiable without Unity and a device. Adopting an existing,
// maintained plugin (gree/unity-webview) against this seam is what kept it a drop-in.
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
