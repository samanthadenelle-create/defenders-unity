using System;
using System.Text;
using System.Threading.Tasks;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Payments;
using DeNelle.Core.State;
using DeNelle.Core.Backend;
using Google;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.GooglePlay
{
    /// <summary>Google credential -> verified backend session -> stable play-* save identity.</summary>
    internal static class GooglePlayIdentityClient
    {
        private const string WebClientId =
            "264518851517-q9i3gj5dfocqme8v9vh8ria4na6avlj1.apps.googleusercontent.com";
        private const string SessionUrl = BackendRequestSigner.BackendBase + "/api/auth/google-session";
        private static Task<bool> _inFlight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            GooglePlayIdentityBridge.Register(EnsureSignedInAsync);
        }

        internal static Task<bool> EnsureSignedInAsync()
        {
            string current = BackendRequestSigner.CurrentPlayerId();
            if (GameStateService.IsGooglePlayIdentity(current))
            {
                using var probe = UnityWebRequest.Get("about:blank");
                if (BackendRequestSigner.TryAttachCachedSession(probe, current))
                    return Task.FromResult(true);
            }
            return _inFlight ??= SignInAndExchangeAsync();
        }

        private static async Task<bool> SignInAndExchangeAsync()
        {
            try
            {
                GoogleSignIn.Configuration = new GoogleSignInConfiguration
                {
                    WebClientId = WebClientId,
                    RequestIdToken = true,
                    RequestEmail = true,
                    UseGameSignIn = false,
                };
                GoogleSignInUser user = await GoogleSignIn.DefaultInstance.SignIn();
                if (user == null || string.IsNullOrWhiteSpace(user.IdToken)) return false;

                string payload = JsonUtility.ToJson(new TokenRequest { idToken = user.IdToken });
                using var request = new UnityWebRequest(SessionUrl, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = 20,
                };
                request.SetRequestHeader("Content-Type", "application/json");
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                if (request.result != UnityWebRequest.Result.Success) return false;

                var reply = JsonUtility.FromJson<SessionReply>(request.downloadHandler.text);
                var state = GameStateService.Instance;
                if (reply == null || !reply.success || state == null ||
                    !BackendRequestSigner.InstallVerifiedSession(reply.playerId, reply.token, reply.expiresAt) ||
                    !state.BindVerifiedExternalIdentity(reply.playerId))
                    return false;

                FlowTrace.Step("Auth", "Google Play identity verified and bound; purchase may proceed.");
                return true;
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Auth", "Google Play sign-in failed: " + ex.GetType().Name);
                return false;
            }
            finally { _inFlight = null; }
        }

        [Serializable] private sealed class TokenRequest { public string idToken; }
        [Serializable] private sealed class SessionReply
        {
            public bool success;
            public string playerId;
            public string token;
            public string expiresAt;
        }
    }
}
