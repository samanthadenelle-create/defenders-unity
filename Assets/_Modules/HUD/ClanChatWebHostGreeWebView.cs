// =============================================================================
// ClanChatWebHostGreeWebView — the real IClanChatWebHost, backed by gree/unity-webview
// (net.gree.unity-webview, WO-1858).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ VERIFIED AT SOURCE, NOT FROM THE README ALONE (2026-09-17):
//   * Packages/manifest.json now carries
//     "net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package"
//     — the exact UPM git-URL path gree's own README documents (README.md:30-52,
//     fetched raw from github.com/gree/unity-webview at master), and confirmed real by
//     reading dist/package/package.json (name net.gree.unity-webview, version 1.0.0)
//     over the GitHub contents API, not assumed from the README's prose.
//   * The API surface below is read from the real
//     dist/package/Assets/Plugins/WebViewObject.cs (namespace Gree.UnityWebView, class
//     WebViewObject) and the sample at sample/Assets/Scripts/SampleWebView.cs — not from
//     a summary. Method names/signatures used here (Init, LoadURL, SetVisibility,
//     SetMargins, IsInitialized, IsWebViewAvailable, CallFromJS->cb, CallOnError->err,
//     CallOnHttpError->httpErr) all exist verbatim at those paths.
//
// ⛔ COORDINATE CONVENTION MISMATCH, HANDLED HERE ON PURPOSE. IClanChatWebHost.SetViewport
// takes a NORMALISED rect with origin BOTTOM-LEFT (Unity's own UI convention — see that
// file's doc comment). gree's SetMargins(left, top, right, bottom) is PIXELS from each
// edge with `top` measured from the TOP of the screen (confirmed at WebViewObject.cs:1335,
// SetMargins body: `mt = top` feeds `_CWebViewPlugin_SetRect`/`webView.Call("SetMargins", ...)`
// directly, and the Android `relative` branch divides by Screen.height with `top` from the
// top edge). ApplyViewport below is the ONE place that conversion happens.
//
// ⛔ THE unity: SCHEME BRIDGE NEEDS NO CODE HERE. Android's native side in this plugin
// intercepts `unity:`-scheme navigation itself and delivers it through CallFromJS -> the
// `cb` delegate passed to Init — exactly the third fallback site/clan-chat.html's
// sendToUnity already speaks (`window.location.href = 'unity:' + encodeURIComponent(json)`,
// clan-chat.html:148). No EvaluateJS shim is required on Android (the sample only injects
// one for OSX/iOS/WebGL — SampleWebView.cs's `ld:` callback, guarded by
// `#if UNITY_EDITOR_OSX || ... || UNITY_IOS` with an `#else var js = "";` for everything
// else, Android included).
//
// ⚠ WHY THE INIT-THEN-WAIT SHAPE. gree/unity-webview issue #1094 documents a race where a
// LoadURL called immediately after Init can be dropped before the native view finishes
// setting up; the maintainers' own fix in the current sample is to poll IsInitialized()
// before touching the view further (SampleWebView.cs's `while (!webViewObject.
// IsInitialized()) yield return null;`). WaitForInitThenLoad mirrors that, bounded by a
// timeout so a stuck native side degrades to "load anyway" rather than a silently dead host.
// =============================================================================

using System;
using System.Collections;
using DeNelle.Core.Diagnostics;
using Gree.UnityWebView;
using UnityEngine;

namespace DeNelle.HUD
{
    /// <summary>
    /// Real WebView host for <see cref="ClanChatPanel"/>, backed by gree/unity-webview's
    /// <see cref="WebViewObject"/>. Owns one hidden GameObject + WebViewObject for its
    /// lifetime; every call is Guard-wrapped so a native/plugin failure surfaces as
    /// <see cref="LoadFailed"/> instead of throwing into the Unity loop.
    /// </summary>
    public sealed class ClanChatWebHostGreeWebView : IClanChatWebHost
    {
        private const string Sys = "ClanChat";
        private const float InitTimeoutSeconds = 8f;

        private GameObject _hostGo;
        private WebViewObject _webView;
        private bool _visible;
        private Rect _viewport = new Rect(0f, 0f, 1f, 1f);

        public event Action<string> MessageReceived;
        public event Action<string> LoadFailed;

        /// <summary>
        /// True when gree's own availability probe says this platform can host a view.
        /// <see cref="WebViewObject.IsWebViewAvailable"/> is a static, side-effect-free
        /// check (returns true everywhere except a device Android query of its own native
        /// plugin) — Guarded because it still crosses into AndroidJavaObject on-device.
        /// </summary>
        public bool IsAvailable => Guard.Try(Sys, "GreeWebView.IsWebViewAvailable",
            () => WebViewObject.IsWebViewAvailable(), false);

        public void Load(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                LoadFailed?.Invoke("empty_url");
                return;
            }
            if (!IsAvailable)
            {
                FlowTrace.Warn(Sys, "GreeWebView.Load — WebViewObject reports unavailable on this platform.");
                LoadFailed?.Invoke("webview_unavailable");
                return;
            }

            Guard.Try(Sys, "GreeWebView.Load", () =>
            {
                EnsureCreated();
                _webView.StartCoroutine(WaitForInitThenLoad(url));
            });
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_webView == null) return;
            Guard.Try(Sys, "GreeWebView.SetVisible", () => _webView.SetVisibility(visible));
        }

        public void SetViewport(Rect normalizedScreenRect)
        {
            _viewport = normalizedScreenRect;
            if (_webView == null) return;
            ApplyViewport();
        }

        public void Dispose()
        {
            Guard.Try(Sys, "GreeWebView.Dispose", () =>
            {
                if (_hostGo != null) UnityEngine.Object.Destroy(_hostGo);
            });
            _hostGo = null;
            _webView = null;
            MessageReceived = null;
            LoadFailed = null;
        }

        // ── Internals ────────────────────────────────────────────────────────

        private void EnsureCreated()
        {
            if (_webView != null) return;

            _hostGo = new GameObject("ClanChatWebView");
            UnityEngine.Object.DontDestroyOnLoad(_hostGo);
            _webView = _hostGo.AddComponent<WebViewObject>();

            _webView.Init(
                cb: msg => MessageReceived?.Invoke(msg),
                err: msg =>
                {
                    FlowTrace.Fail(Sys, "GreeWebView onError: " + msg);
                    LoadFailed?.Invoke("webview_error:" + msg);
                },
                httpErr: msg =>
                {
                    FlowTrace.Warn(Sys, "GreeWebView onHttpError: " + msg);
                    LoadFailed?.Invoke("webview_http_error:" + msg);
                },
                ld: loadedUrl => FlowTrace.Step(Sys, "GreeWebView page loaded."),
                started: startedUrl => FlowTrace.Step(Sys, "GreeWebView navigation started."));

            // Created hidden — SetVisible(true) from the panel is what actually shows it,
            // matching IClanChatWebHost's "Load once per open; SetVisible show/hide" contract.
            Guard.Try(Sys, "GreeWebView.InitialHide", () => _webView.SetVisibility(false));
        }

        private IEnumerator WaitForInitThenLoad(string url)
        {
            float deadline = Time.realtimeSinceStartup + InitTimeoutSeconds;
            while (_webView != null && !Guard.Try(Sys, "GreeWebView.IsInitialized", () => _webView.IsInitialized(), true))
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    FlowTrace.Warn(Sys, "GreeWebView init did not report ready within " + InitTimeoutSeconds +
                                        "s — loading anyway rather than hanging the open forever.");
                    break;
                }
                yield return null;
            }
            if (_webView == null) yield break;

            Guard.Try(Sys, "GreeWebView.LoadAfterInit", () =>
            {
                ApplyViewport();
                _webView.SetVisibility(_visible);
                _webView.LoadURL(url);
            });
        }

        /// <summary>
        /// Converts the panel's normalised, BOTTOM-LEFT-origin viewport rect into gree's
        /// pixel margins measured from each screen edge with TOP measured from the top —
        /// see this file's header for the citation that pins that convention.
        /// </summary>
        private void ApplyViewport()
        {
            if (_webView == null) return;
            Guard.Try(Sys, "GreeWebView.ApplyViewport", () =>
            {
                float w = Mathf.Max(1f, Screen.width);
                float h = Mathf.Max(1f, Screen.height);
                var r = _viewport;
                float x0 = Mathf.Clamp01(r.x);
                float y0 = Mathf.Clamp01(r.y);
                float x1 = Mathf.Clamp01(r.x + r.width);
                float y1 = Mathf.Clamp01(r.y + r.height);

                int left = Mathf.RoundToInt(x0 * w);
                int right = Mathf.RoundToInt((1f - x1) * w);
                int top = Mathf.RoundToInt((1f - y1) * h);      // distance from the TOP to the rect's top edge
                int bottom = Mathf.RoundToInt(y0 * h);          // distance from the BOTTOM to the rect's bottom edge

                _webView.SetMargins(left, top, right, bottom);
            });
        }
    }

    /// <summary>
    /// One decision point: prefer the real gree host when this platform can host a view,
    /// fall back to <see cref="ClanChatWebHostUnavailable"/> otherwise. Kept separate from
    /// <see cref="ClanChatPanel"/> so the panel's own EnsureBuilt stays a one-line swap.
    /// </summary>
    public static class ClanChatWebHostFactory
    {
        public static IClanChatWebHost Create()
        {
            var host = new ClanChatWebHostGreeWebView();
            if (host.IsAvailable)
            {
                FlowTrace.Step("ClanChat", "binding the real gree/unity-webview host.");
                return host;
            }
            FlowTrace.Warn("ClanChat", "gree/unity-webview reports unavailable on this platform — " +
                                       "falling back to ClanChatWebHostUnavailable.");
            host.Dispose();
            return new ClanChatWebHostUnavailable();
        }
    }
}
