using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Connection settings for AppDeployHub. The server URL and chosen app are project settings
    /// (<c>ProjectSettings/NativeSocialAppDeployHub.json</c>, safe to commit — no secret in it). Who you are is per
    /// user, so it lives in EditorPrefs: the e-mail you signed in with and the device key AppDeployHub issued for
    /// this machine (only valid for the server that issued it). The password is never stored anywhere. A key can also
    /// be supplied through the <c>APPDEPLOYHUB_API_KEY</c> environment variable (CI).
    /// </summary>
    internal static class AppDeployHubSettings
    {
        public const string TokenEnvVar = "APPDEPLOYHUB_API_KEY";
        private const string FilePath = "ProjectSettings/NativeSocialAppDeployHub.json";
        private const string TokenPref = "NativeSocial.AppDeployHub.ApiKey";
        private const string TokenServerPref = "NativeSocial.AppDeployHub.ApiKeyServer";
        private const string EmailPref = "NativeSocial.AppDeployHub.Email";

        [Serializable]
        private class Data
        {
            public string baseUrl = string.Empty;
            public string appId = string.Empty;
            public string appName = string.Empty;
        }

        private static Data _data;

        private static Data Current => _data ??= Load();

        private static Data Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeSocial] Could not read {FilePath}: {ex.Message}");
            }
            return new Data();
        }

        private static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
            catch (Exception ex) { Debug.LogWarning($"[NativeSocial] Could not write {FilePath}: {ex.Message}"); }
        }

        public static string BaseUrl
        {
            get => Current.baseUrl;
            set { Current.baseUrl = (value ?? string.Empty).Trim(); Save(); }
        }

        public static string AppId => Current.appId;
        public static string AppName => Current.appName;

        public static void SetApp(string appId, string appName)
        {
            Current.appId = appId ?? string.Empty;
            Current.appName = appName ?? string.Empty;
            Save();
        }

        /// <summary>The e-mail last used to sign in (remembered for convenience; the password never is).</summary>
        public static string Email
        {
            get => EditorPrefs.GetString(EmailPref, string.Empty);
            set => EditorPrefs.SetString(EmailPref, (value ?? string.Empty).Trim());
        }

        public static bool TokenFromEnvironment => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(TokenEnvVar));

        /// <summary>The key to authenticate with: the environment variable, else the stored key — but only if it was issued by the server currently configured.</summary>
        public static string Token
        {
            get
            {
                var fromEnv = Environment.GetEnvironmentVariable(TokenEnvVar);
                if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

                return EditorPrefs.GetString(TokenServerPref, string.Empty) == NormalizedServer(BaseUrl)
                    ? EditorPrefs.GetString(TokenPref, string.Empty)
                    : string.Empty;
            }
        }

        public static bool IsSignedIn => !string.IsNullOrEmpty(Token);

        /// <summary>Stores a key issued by the configured server (automatically after signing in, or pasted by hand).</summary>
        public static void StoreToken(string token)
        {
            EditorPrefs.SetString(TokenPref, (token ?? string.Empty).Trim());
            EditorPrefs.SetString(TokenServerPref, NormalizedServer(BaseUrl));
        }

        public static void ClearSession()
        {
            EditorPrefs.DeleteKey(TokenPref);
            EditorPrefs.DeleteKey(TokenServerPref);
        }

        private static string NormalizedServer(string url) => (url ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>Talks to AppDeployHub's public <c>/api/v1</c> — see AppDeployHub's README.</summary>
    internal static class AppDeployHubClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        [Serializable] internal class AppSummary { public string id; public string name; public string packageName; public bool hasGooglePlayGamesApplicationId; public string studioId; public string studioName; }
        [Serializable] private class AppList { public AppSummary[] items; }
        [Serializable] private class ErrorBody { public string error; }
        [Serializable] private class LoginRequest { public string email; public string password; public string deviceName; }
        [Serializable] internal class StudioSummary { public string id; public string name; }
        [Serializable] internal class LoginResult { public string token; public string email; public string deviceName; public StudioSummary[] studios; }

        [Serializable]
        internal class ImportResult
        {
            public int googlePlayCreated, googlePlayUpdated, gameCenterCreated, gameCenterUpdated;
            public string[] locales;
            public bool googlePlayPushQueued, gameCenterPushQueued;
        }

        internal struct Response
        {
            public bool Ok;
            public int Status;
            public string Body;

            /// <summary>Human-readable explanation of a failed call, or null when Ok.</summary>
            public string Error;
        }

        /// <summary>The URL is safe to send a secret to: https, or plain http only to this machine.</summary>
        internal static string ValidateBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return "Enter the AppDeployHub server URL first.";
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)) return $"'{baseUrl}' is not a valid URL.";
            if (uri.Scheme == Uri.UriSchemeHttps) return null;
            if (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host == "localhost")) return null;
            return "Use an https:// URL — your password and key would travel unencrypted over plain http.";
        }

        public static string DeviceName => $"Unity editor - {Environment.MachineName}";

        /// <summary>Signs in with the account's e-mail + password. On success AppDeployHub issues a key for this machine.</summary>
        public static Task<Response> LoginAsync(string email, string password) =>
            SendAsync(HttpMethod.Post, "/api/v1/auth/login",
                JsonUtility.ToJson(new LoginRequest { email = email, password = password, deviceName = DeviceName }),
                authenticated: false);

        public static Task<Response> LogoutAsync() => SendAsync(HttpMethod.Post, "/api/v1/auth/logout", null);

        public static Task<Response> GetAppsAsync() => SendAsync(HttpMethod.Get, "/api/v1/apps", null);

        public static Task<Response> ImportAsync(string appId, string json, bool pushGooglePlay, bool pushGameCenter) =>
            SendAsync(HttpMethod.Post,
                $"/api/v1/apps/{appId}/achievements/import?pushGooglePlay={pushGooglePlay.ToString().ToLowerInvariant()}&pushGameCenter={pushGameCenter.ToString().ToLowerInvariant()}",
                json);

        private static async Task<Response> SendAsync(HttpMethod method, string path, string jsonBody, bool authenticated = true)
        {
            var urlError = ValidateBaseUrl(AppDeployHubSettings.BaseUrl);
            if (urlError != null) return Fail(0, urlError);
            if (authenticated && !AppDeployHubSettings.IsSignedIn) return Fail(0, "You are not signed in. Sign in with your AppDeployHub e-mail and password first.");

            try
            {
                using var request = new HttpRequestMessage(method, AppDeployHubSettings.BaseUrl.TrimEnd('/') + path);
                if (authenticated)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppDeployHubSettings.Token);
                if (jsonBody != null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using var response = await Http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var status = (int)response.StatusCode;
                return new Response { Ok = response.IsSuccessStatusCode, Status = status, Body = body, Error = response.IsSuccessStatusCode ? null : Explain(status, body, authenticated) };
            }
            catch (TaskCanceledException)
            {
                return Fail(0, "The request timed out after 60 seconds.");
            }
            catch (Exception ex)
            {
                return Fail(0, $"Could not reach the server: {ex.Message}");
            }
        }

        private static Response Fail(int status, string error) => new Response { Ok = false, Status = status, Error = error };

        internal static string Explain(int status, string body, bool authenticated = true)
        {
            string detail = null;
            try { detail = JsonUtility.FromJson<ErrorBody>(body)?.error; } catch { /* body wasn't JSON */ }

            switch (status)
            {
                case 401 when authenticated: return "Your session is no longer valid (signed out from another place, or revoked). Sign in again.";
                case 401: return string.IsNullOrEmpty(detail) ? "Invalid e-mail or password." : detail;
                case 403: return string.IsNullOrEmpty(detail) ? "403 — not allowed." : detail;
                case 404: return $"404 — {(string.IsNullOrEmpty(detail) ? "not found" : detail)}. Check the server URL and that the selected app belongs to your account.";
                case 423: return string.IsNullOrEmpty(detail) ? "Account temporarily locked after too many failed attempts. Try again later." : detail;
                case 429: return "429 — too many requests; wait a minute and try again.";
                default: return $"{status} — {(string.IsNullOrEmpty(detail) ? Truncate(body, 300) : detail)}";
            }
        }

        private static string Truncate(string text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max) + "…";

        internal static AppSummary[] ParseApps(string body) =>
            JsonUtility.FromJson<AppList>("{\"items\":" + body + "}")?.items ?? Array.Empty<AppSummary>();

        internal static LoginResult ParseLogin(string body) => JsonUtility.FromJson<LoginResult>(body);

        internal static ImportResult ParseImportResult(string body) => JsonUtility.FromJson<ImportResult>(body);

        /// <summary>
        /// Runs <paramref name="task"/> without blocking the Editor and calls <paramref name="onDone"/> on the
        /// main thread when it finishes (polled from EditorApplication.update — no background-thread Unity calls).
        /// </summary>
        public static void RunThen<T>(Task<T> task, Action<T> onDone)
        {
            void Poll()
            {
                if (!task.IsCompleted) return;
                EditorApplication.update -= Poll;
                onDone(task.IsFaulted ? default : task.Result);
            }
            EditorApplication.update += Poll;
        }
    }
}
