using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

#if UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
using UnityEngine.SocialPlatforms.GameCenter;
#endif

namespace Wagenheimer.NativeSocial
{
    /// <summary>
    /// Platform-native replacement for Unity's deprecated <c>UnityEngine.Social</c> API.
    /// This file holds the public API; the per-platform backends live in the partial files
    /// <c>NativeSocial.Android.cs</c>, <c>NativeSocial.AndroidDiagnostics.cs</c>, <c>NativeSocial.Apple.cs</c>,
    /// <c>NativeSocial.Steam.cs</c> and <c>NativeSocial.Pending.cs</c>.
    /// Dispatches achievement/stat calls directly to Google Play Games (Android),
    /// Game Center (iOS), or Steamworks.NET (Steam desktop builds) based on the
    /// active compile-time platform. Callers use a single, platform-agnostic API and
    /// never touch the underlying SDKs directly.
    /// </summary>
    public static partial class NativeSocial
    {
        // Per-platform achievement ID maps: game-defined "LocID" -> platform-specific achievement ID.
        // Keeping these as data (rather than hardcoding platform IDs across game code) means a single
        // Initialize() call at startup is the only place platform IDs need to be configured.
        private static Dictionary<string, string> _androidMap;
        private static Dictionary<string, string> _iosMap;
        private static Dictionary<string, SteamEntry> _steamMap;
        private static Dictionary<string, string> _androidLeaderboardMap;
        private static Dictionary<string, string> _iosLeaderboardMap;
        private static bool _initialized;
        private static bool _warnedBeforeInitialize;

        /// <summary>Whether NativeSocial.Initialize has been called.</summary>
        public static bool IsInitialized => _initialized;

        /// <summary>Active Android achievement LocID to Google Play Games ID mapping.</summary>
        public static IReadOnlyDictionary<string, string> AndroidMap => _androidMap ?? (_androidMap = new Dictionary<string, string>());

        /// <summary>Active iOS achievement LocID to Game Center ID mapping.</summary>
        public static IReadOnlyDictionary<string, string> IosMap => _iosMap ?? (_iosMap = new Dictionary<string, string>());

        /// <summary>Active Steam achievement LocID to SteamEntry mapping.</summary>
        public static IReadOnlyDictionary<string, SteamEntry> SteamMap => _steamMap ?? (_steamMap = new Dictionary<string, SteamEntry>());

        /// <summary>Active Android leaderboard LocID to Google Play Games ID mapping.</summary>
        public static IReadOnlyDictionary<string, string> AndroidLeaderboardMap => _androidLeaderboardMap ?? (_androidLeaderboardMap = new Dictionary<string, string>());

        /// <summary>Active iOS leaderboard LocID to Game Center ID mapping.</summary>
        public static IReadOnlyDictionary<string, string> IosLeaderboardMap => _iosLeaderboardMap ?? (_iosLeaderboardMap = new Dictionary<string, string>());

        /// <summary>Fired whenever Report is called: (locId, delta, current, total, completed).</summary>
        public static event Action<string, int, int, int, bool> OnReport;

        /// <summary>Fired whenever SubmitScore is called: (locId, score).</summary>
        public static event Action<string, long> OnSubmitScore;

        /// <summary>Fired whenever an authentication attempt finishes: (success).</summary>
        public static event Action<bool> OnAuthenticated;

        /// <summary>Fired on internal NativeSocial logging / diagnostics: (message).</summary>
        public static event Action<string> OnLog;

        /// <summary>
        /// Editor-only hook, set by the package's Editor assembly via <c>[InitializeOnLoadMethod]</c>.
        /// When set, rewrites Apple Game Center IDs stored in App Store Connect's UUID format into the
        /// achievement's vendorIdentifier (<see cref="AchievementTierMap.LocId"/>) in the given
        /// <see cref="AchievementTierMap"/> and persists the change; returns how many entries changed.
        /// Null in player builds, where callers should fall back to an in-memory fix only.
        /// </summary>
        public static Func<AchievementTierMap, int> PersistentAppleIdFixer;

        /// <summary>
        /// Register achievement ID maps per platform.
        /// Must be called once at game startup before any Report/Auth calls.
        /// </summary>
        /// <param name="androidMap">Maps game-defined LocID -> Google Play Games achievement ID (e.g. "CgkI_aXR36YSEAIQAQ"). Pass null if not targeting Android.</param>
        /// <param name="iosMap">Maps game-defined LocID -> Game Center achievement ID (e.g. "com.example.wildcards"). Pass null if not targeting iOS.</param>
        /// <param name="steamMap">Maps game-defined LocID -> <see cref="SteamEntry"/> (Steam stat + achievement API names). Pass null if not targeting Steam.</param>
        /// <param name="androidLeaderboardMap">Maps game-defined LocID -> Google Play Games leaderboard ID. Only used on Android for <see cref="SubmitScore"/>/<see cref="ShowLeaderboardUI(string)"/>.</param>
        /// <param name="iosLeaderboardMap">Maps game-defined LocID -> Game Center leaderboard ID. Only used on iOS for <see cref="SubmitScore"/>/<see cref="ShowLeaderboardUI(string)"/>.</param>
        public static void Initialize(
            Dictionary<string, string> androidMap = null,
            Dictionary<string, string> iosMap = null,
            Dictionary<string, SteamEntry> steamMap = null,
            Dictionary<string, string> androidLeaderboardMap = null,
            Dictionary<string, string> iosLeaderboardMap = null)
        {
            _androidMap = androidMap ?? new Dictionary<string, string>();
            _iosMap = iosMap ?? new Dictionary<string, string>();
            _steamMap = steamMap ?? new Dictionary<string, SteamEntry>();
            _androidLeaderboardMap = androidLeaderboardMap ?? new Dictionary<string, string>();
            _iosLeaderboardMap = iosLeaderboardMap ?? new Dictionary<string, string>();
            _initialized = true;

#if UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            try
            {
                // In Unity, GameKit completion banner is disabled by default. Enable it so Apple's native
                // achievement popup drops down from the top of the screen when progress reaches 100%.
                UnityEngine.SocialPlatforms.GameCenter.GameCenterPlatform.ShowDefaultAchievementCompletionBanner(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeSocial] Failed to enable Apple achievement banner: {ex.Message}");
            }
#endif

            OnLog?.Invoke($"[NativeSocial] Initialized. Android={_androidMap.Count}, iOS={_iosMap.Count}, Steam={_steamMap.Count}");
        }

        // ── Status ────────────────────────────────────────────────────

#if UNITY_ANDROID
        /// <summary>
        /// Whether the player is currently signed in to Google Play Games. Maintained by NativeSocial's own
        /// sign-in calls (and re-checked when the game returns to the foreground); progress reported while
        /// signed out is queued and sent after the next sign-in.
        /// </summary>
        public static bool IsAuthenticated { get; private set; }
#endif

        // ── Report ────────────────────────────────────────────────────

        /// <summary>
        /// Report achievement progress to the active platform. Dispatches to whichever of
        /// <see cref="ReportAndroid"/>/<see cref="ReportIOS"/>/<see cref="ReportSteam"/> matches
        /// the current build target — pass all five parameters every time and let each platform
        /// use only the ones it needs (Android uses <paramref name="delta"/>; iOS uses
        /// <paramref name="current"/>/<paramref name="total"/>; Steam uses <paramref name="delta"/>
        /// or <paramref name="current"/> depending on <see cref="SteamEntry.SetStatAbsolute"/>).
        /// </summary>
        /// <param name="locId">Game-defined achievement key (the key used in the maps passed to <see cref="Initialize"/>). Identifies which achievement this call is for.</param>
        /// <param name="delta">
        /// How much the underlying counter/stat should increase by since the last call
        /// (e.g. "+1 wildcard collected just now"). Used by Android (GPGS IncrementAchievement)
        /// and by Steam when <see cref="SteamEntry.SetStatAbsolute"/> is false. Pass 0 when you only
        /// want to report/check <paramref name="completed"/> without bumping a counter.
        /// </param>
        /// <param name="current">Current absolute progress value (e.g. "5 of 20 wildcards collected"). Used by iOS (0-100% completion) and by Steam when <see cref="SteamEntry.SetStatAbsolute"/> is true.</param>
        /// <param name="total">Progress value that represents 100% completion (e.g. 20 wildcards total). Only used by iOS, alongside <paramref name="current"/>, to compute that percentage.</param>
        /// <param name="completed">Whether the achievement is fully completed right now. When true, all platforms unlock/complete it outright regardless of the counter parameters.</param>
        public static void Report(string locId, int delta, int current, int total, bool completed)
        {
            if (!_initialized)
            {
                // Log only once: if the caller forgets Initialize() at boot, a repeated
                // failure on every Report call (one per achievement, per frame, etc.)
                // becomes console spam without adding new information.
                if (!_warnedBeforeInitialize)
                {
                    _warnedBeforeInitialize = true;
                    Debug.LogWarning("[NativeSocial] Report called before Initialize.");
                }
                OnLog?.Invoke($"[NativeSocial] Report('{locId}') rejected: not initialized.");
                return;
            }

            OnReport?.Invoke(locId, delta, current, total, completed);
            OnLog?.Invoke($"[NativeSocial] Report('{locId}', delta={delta}, {current}/{total}, comp={completed})");

            // Exactly one of these branches is compiled in per build target — there is
            // no runtime platform switch here, each build only ever contains its own path.
#if UNITY_ANDROID
            ReportAndroid(locId, delta, current, completed);
#elif UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            ReportIOS(locId, current, total, completed);
#elif WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
            ReportSteam(locId, delta, current, completed);
#else
            OnLog?.Invoke($"[NativeSocial] Report('{locId}') not dispatched: no platform backend on this build target ({Application.platform}). Editor/standalone without Steam keeps progress local only.");
#endif
        }

        // ── Sync completed ────────────────────────────────────────────

        /// <summary>
        /// Re-syncs already-completed achievements to the platform. Call this right after
        /// authentication succeeds (Android GPGS sign-in, iOS Game Center auth, Steam ready),
        /// since the platform's own record can be behind the game's local save (new device,
        /// reinstall, offline progress, switching accounts).
        /// </summary>
        /// <param name="completedLocIds">The LocIDs (from the maps passed to <see cref="Initialize"/>) of every achievement already marked completed in the local save data.</param>
        public static void SyncCompleted(IEnumerable<string> completedLocIds)
        {
            if (!_initialized || completedLocIds == null) return;

#if UNITY_ANDROID
            SyncCompletedAndroid(completedLocIds);
#elif UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            SyncCompletedIOS(completedLocIds);
#elif WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
            SyncCompletedSteam(completedLocIds);
#endif
        }

        // ── Platform UI ───────────────────────────────────────────────

        /// <summary>
        /// Opens the platform-native achievements screen (Google Play Games or Game Center), if one is available.
        /// </summary>
        /// <returns>
        /// True if a native achievements screen was actually shown. False when there is no native
        /// UI to show for the current platform/state — on Android this means the player isn't signed
        /// in yet; on Steam/standalone there is no scriptable achievements overlay in Steamworks.NET
        /// at all (the Steam overlay is entirely driven by the Steam client, not by game code). Callers
        /// should treat false as "show your own custom achievements screen instead" (e.g. MainMenu falls
        /// back to its <c>formAchievements</c> dialog on PC).
        /// </returns>
        public static bool ShowAchievementsUI()
        {
#if UNITY_ANDROID
            if (!IsAuthenticated)
            {
                OnLog?.Invoke("[NativeSocial] ShowAchievementsUI skipped: not authenticated with GPGS (the native screen would be empty/locked).");
                return false;
            }
            OnLog?.Invoke("[NativeSocial] Opening GPGS native achievements UI.");
            PlayGamesPlatform.Instance.ShowAchievementsUI();
            return true;
#elif UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            OnLog?.Invoke("[NativeSocial] Opening Game Center native achievements UI.");
            Social.ShowAchievementsUI();
            return true;
#else
            // Steam/standalone: no native UI exists to show, so the caller must provide its own.
            OnLog?.Invoke("[NativeSocial] ShowAchievementsUI: no native achievements UI on this platform (Steam/standalone).");
            return false;
#endif
        }

        /// <summary>
        /// One-liner to open the platform's native achievements screen from buttons or menus.
        /// <para>
        /// If already authenticated, opens the native achievements screen immediately.
        /// If not authenticated yet, automatically triggers the appropriate sign-in flow for the platform
        /// (<see cref="AuthenticateManually"/> on Android to show profile UI, or <see cref="Authenticate"/> on iOS/macOS)
        /// and opens the achievements screen as soon as authentication succeeds.
        /// </para>
        /// </summary>
        /// <param name="onResult">
        /// Callback invoked when the operation completes.
        /// Parameters: <c>(bool opened, string errorMessage)</c>.
        /// <c>opened</c> is true if the native UI was displayed.
        /// If false, <c>errorMessage</c> contains an English description of the failure suitable for diagnostics or fallback.
        /// </param>
        public static void OpenAchievementsUI(Action<bool, string> onResult = null)
        {
            if (ShowAchievementsUI())
            {
                onResult?.Invoke(true, null);
                return;
            }

#if UNITY_ANDROID
            Debug.Log("[NativeSocial] OpenAchievementsUI: Not authenticated. Requesting manual GPGS sign-in...");
            AuthenticateManually(success =>
            {
                if (success && ShowAchievementsUI())
                {
                    onResult?.Invoke(true, null);
                }
                else
                {
                    string err = success
                        ? "Could not open the achievements screen."
                        : (LastAuthenticationReport ?? "Could not sign in to Google Play Games.");
                    onResult?.Invoke(false, err);
                }
            });
#elif UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            Debug.Log("[NativeSocial] OpenAchievementsUI: Not authenticated. Requesting Apple Game Center sign-in...");
            Authenticate(success =>
            {
                if (success && ShowAchievementsUI())
                {
                    onResult?.Invoke(true, null);
                }
                else
                {
                    string err = success
                        ? "Could not open the achievements screen."
                        : (LastAuthenticationReport ?? "Could not sign in to Apple Game Center.");
                    onResult?.Invoke(false, err);
                }
            });
#else
            OnLog?.Invoke("[NativeSocial] OpenAchievementsUI: No native achievements UI on this platform (Steam/standalone).");
            onResult?.Invoke(false, "No native achievements screen on this platform.");
#endif
        }

        // ── Leaderboards ─────────────────────────────────────────────

        /// <summary>
        /// Submits a score to the mapped platform leaderboard (Google Play Games on Android,
        /// Game Center on iOS). Silent no-op when not authenticated or the locId has no mapping.
        /// </summary>
        public static void SubmitScore(string locId, long score)
        {
            if (!_initialized || locId == null) return;

            OnSubmitScore?.Invoke(locId, score);
            OnLog?.Invoke($"[NativeSocial] SubmitScore('{locId}', score={score})");

#if UNITY_ANDROID
            if (!IsAuthenticated)
            {
                OnLog?.Invoke($"[NativeSocial] SubmitScore('{locId}') skipped: not authenticated with GPGS.");
                return;
            }
            if (_androidLeaderboardMap.TryGetValue(locId, out var androidId))
            {
                OnLog?.Invoke($"[NativeSocial] GPGS ReportScore('{androidId}') = {score}.");
                PlayGamesPlatform.Instance.ReportScore(score, androidId, ok => LogGpgsCallback("ReportScore", androidId, locId, ok));
            }
            else OnLog?.Invoke($"[NativeSocial] SubmitScore('{locId}') skipped: no GPGS leaderboard id mapped.");
#elif UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            if (_iosLeaderboardMap.TryGetValue(locId, out var iosId))
            {
                OnLog?.Invoke($"[NativeSocial] Game Center ReportScore('{iosId}') = {score}.");
                Social.ReportScore(score, iosId, ok => { if (!ok) Debug.LogWarning($"[NativeSocial] Game Center ReportScore('{iosId}') FAILED for '{locId}'."); });
            }
            else OnLog?.Invoke($"[NativeSocial] SubmitScore('{locId}') skipped: no Game Center leaderboard mapped.");
#endif
        }

        /// <summary>
        /// Opens the platform-native leaderboard screen (Google Play Games on Android, Game Center
        /// on iOS/macOS). Pass null to open the all-leaderboards screen on Android.
        /// </summary>
        /// <returns>True if a native UI was shown; false when there is no UI for the current platform/state.</returns>
        public static bool ShowLeaderboardUI(string locId = null)
        {
#if UNITY_ANDROID
            if (!IsAuthenticated) { OnLog?.Invoke("[NativeSocial] ShowLeaderboardUI skipped: not authenticated with GPGS."); return false; }
            if (locId == null)
            {
                OnLog?.Invoke("[NativeSocial] Opening GPGS all-leaderboards UI.");
                PlayGamesPlatform.Instance.ShowLeaderboardUI();
                return true;
            }
            if (_androidLeaderboardMap.TryGetValue(locId, out var androidId))
            {
                OnLog?.Invoke($"[NativeSocial] Opening GPGS leaderboard UI for '{androidId}'.");
                PlayGamesPlatform.Instance.ShowLeaderboardUI(androidId);
                return true;
            }
            OnLog?.Invoke($"[NativeSocial] ShowLeaderboardUI skipped: no GPGS leaderboard id mapped for '{locId}'.");
            return false;
#elif UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            if (locId != null && _iosLeaderboardMap.ContainsKey(locId))
            {
                OnLog?.Invoke($"[NativeSocial] Opening Game Center leaderboards UI for '{locId}'.");
                Social.ShowLeaderboardUI();
                return true;
            }
            OnLog?.Invoke($"[NativeSocial] ShowLeaderboardUI skipped: no Game Center leaderboard mapped for '{locId}'.");
            return false;
#else
            OnLog?.Invoke("[NativeSocial] ShowLeaderboardUI: no native leaderboard UI on this platform (Steam/standalone).");
            return false;
#endif
        }

        // ── Authentication ────────────────────────────────────────────

        /// <summary>The outcome of the last sign-in attempt in plain words (service, success or the failure reason), for diagnostics UIs. Null before the first attempt.</summary>
        public static string LastAuthenticationReport { get; private set; }

        /// <summary>Always visible in logcat / the Console (not only through <see cref="OnLog"/>): authentication failures are otherwise silent.</summary>
        private static void LogAuthenticationResult(string service, bool success, string detail)
        {
            LastAuthenticationReport = success ? $"{service}: signed in." : $"{service}: FAILED - {detail}";

            if (success)
                Debug.Log($"[NativeSocial] {service} sign-in succeeded.");
            else
                Debug.LogWarning($"[NativeSocial] {service} sign-in FAILED: {detail}");
        }

        /// <summary>A short, actionable explanation for a Google Play Games <c>SignInStatus</c> name.</summary>
        internal static string DescribeSignInFailure(string statusName)
        {
            switch (statusName)
            {
                case "DeveloperError":
                    return "The app is not set up for this build: check the Play Games application id in the manifest, that the package name matches the one in Play Console, " +
                           "and that the SHA-1 of the key that SIGNED this build (debug, upload or app-signing key) is registered under Play Games Services > Credentials.";
                case "UiSignInRequired":
                case "NotAuthenticated":
                    return "No Play Games player is signed in on this device. Call NativeSocial.AuthenticateManually() to show the sign-in UI.";
                case "NetworkError":
                    return "No connection to Google Play Games.";
                case "Canceled":
                    return "Google Play Games returned Canceled. This can mean the player cancelled, but the plugin can also return this status for configuration failures. " +
                           "Check Android logcat for PlayGamesServices[SignInAuthenticator] and DEVELOPER_ERROR; verify the application id, package name, signing certificate SHA-1 and tester access in Play Console.";
                case "AlreadyInProgress":
                    return "A sign-in was already running.";
                case "InternalError":
                case "Failed":
                    return "Google Play Games reported an internal error. The usual causes are a missing or wrong application id, a SHA-1 that is not registered, " +
                           "or the device account not being a tester while the game is unpublished.";
                default:
                    return "See the Google Play Games documentation for this status.";
            }
        }

        /// <summary>
        /// Authenticates with Game Center on iOS, or Google Play Games on Android.
        /// On Android, this returns the result of the plugin's automatic sign-in attempt
        /// (Play Games Services v11+ tries to sign in automatically when the app starts).
        /// On Steam desktop builds it is a no-op, since Steamworks authenticates via SteamAPI.Init().
        /// </summary>
        /// <param name="callback">Invoked with true on successful authentication, false on failure or on any non-supported platform.</param>
        public static void Authenticate(Action<bool> callback)
        {
#if UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
            try
            {
                UnityEngine.SocialPlatforms.GameCenter.GameCenterPlatform.ShowDefaultAchievementCompletionBanner(true);
            }
            catch {}

            // UnityEngine.SocialPlatforms.Social (the deprecated Unity Social API) is still the only
            // entry point Unity exposes for Game Center authentication — GameCenterPlatform itself has
            // no direct Authenticate method. This is an intentional, unavoidable use of the deprecated
            // API surface, not leftover code to "clean up"; removing it would break iOS auth entirely.
            Social.localUser.Authenticate((success, error) =>
            {
                if (success)
                    FlushPendingProgress("Game Center", entry => ReportIOS(entry.LocId, entry.Current, entry.Total, entry.Completed));
                OnAuthenticated?.Invoke(success);
                OnLog?.Invoke($"[NativeSocial] Game Center Authenticate: {success}");
                var detail = string.IsNullOrEmpty(error)
                    ? "no error message from Game Center (is Game Center enabled in Settings and the app set up in App Store Connect?)"
                    : error;
                LogAuthenticationResult("Game Center", success, success ? "signed in" : detail + MacGameCenterHint());
                callback?.Invoke(success);
            });
#elif UNITY_ANDROID
            EnsureGpgsActivated();

            // Say it loudly BEFORE signing in: a missing Play Games application id makes every call fail without a useful message.
            var configurationProblem = FindAndroidAppIdProblem();
            if (configurationProblem != null)
                Debug.LogError("[NativeSocial] Google Play Games is not configured: " + configurationProblem);
            LogAndroidDiagnosticsOnce();

            Debug.Log($"[NativeSocial] Iniciando autenticação automática GPGS (DebugLogEnabled={PlayGamesPlatform.DebugLogEnabled})...");
            _signInInProgress = true;
            PlayGamesPlatform.Instance.Authenticate(status =>
            {
                _signInInProgress = false;
                var success = status == SignInStatus.Success;
                HandleGpgsSignInResult(success);
                OnAuthenticated?.Invoke(success);
                Debug.Log($"[NativeSocial] GPGS Authenticate callback: status={status}, success={success}, localUser={Social.localUser.userName} ({Social.localUser.id})");
                OnLog?.Invoke($"[NativeSocial] GPGS Authenticate: {status} ({success})");
                LogAuthenticationResult("Google Play Games", success, success ? "signed in" : $"{status}. {DescribeSignInFailure(status.ToString())}");
                callback?.Invoke(success);
            });
#else
            OnAuthenticated?.Invoke(false);
            OnLog?.Invoke("[NativeSocial] Authenticate not supported on current platform.");
            callback?.Invoke(false);
#endif
        }

        /// <summary>
        /// (Android) Manually requests sign-in with Play Games Services, showing the profile-creation
        /// UI when the player has no Play Games Services profile yet. Use this as the retry path when
        /// <see cref="Authenticate"/> fails — GPGS v2 no longer triggers sign-in UI from the automatic
        /// attempt alone. Requires GPGS plugin v11+.
        /// </summary>
        public static void AuthenticateManually(Action<bool> callback)
        {
#if UNITY_ANDROID
            EnsureGpgsActivated();
            LogAndroidDiagnosticsOnce();

            Debug.Log("[NativeSocial] Iniciando GPGS ManuallyAuthenticate...");
            var startedAt = Time.realtimeSinceStartup;
            _signInInProgress = true;
            PlayGamesPlatform.Instance.ManuallyAuthenticate(status =>
            {
                _signInInProgress = false;
                var elapsed = Time.realtimeSinceStartup - startedAt;
                var success = status == SignInStatus.Success;
                HandleGpgsSignInResult(success);
                RememberManualSignInOutcome(success, status, elapsed);
                OnAuthenticated?.Invoke(success);
                Debug.Log($"[NativeSocial] GPGS ManuallyAuthenticate callback after {elapsed * 1000f:0} ms: status={status}, success={success}, localUser={Social.localUser.userName} ({Social.localUser.id})");

                if (!success && status == SignInStatus.Canceled && elapsed < QuickCancelSeconds)
                    Debug.LogError($"[NativeSocial] GPGS manual sign-in was Canceled after only {elapsed * 1000f:0} ms - too fast for a player to cancel. " +
                                   "Google rejected this install before showing the sign-in UI: almost always the signing SHA-1 below is not registered for this package " +
                                   "in Play Console > Play Games Services > Credentials, or the device account is not a tester while Play Games Services is unpublished.\n" +
                                   GetAndroidDiagnostics());
                OnLog?.Invoke($"[NativeSocial] GPGS ManuallyAuthenticate: {status} ({success})");
                LogAuthenticationResult("Google Play Games (Manual)", success, success ? "signed in" : $"{status}. {DescribeSignInFailure(status.ToString())}");
                callback?.Invoke(success);
            });
#else
            OnAuthenticated?.Invoke(false);
            callback?.Invoke(false);
#endif
        }

        /// <summary>
        /// One-liner zero-boilerplate social connector for games.
        /// <para>Automatically detects the current runtime platform and initiates authentication when appropriate:</para>
        /// <list type="bullet">
        ///   <item><description><b>Android:</b> Activates Google Play Games with debug logging enabled, runs automatic sign-in, and falls back to <see cref="AuthenticateManually"/> if it fails - unless the player already dismissed that sign-in screen once (remembered in PlayerPrefs until a successful sign-in), in which case it stays signed out and <see cref="OpenAchievementsUI"/> offers sign-in on demand.</description></item>
        ///   <item><description><b>iOS:</b> Connects to Apple Game Center directly.</description></item>
        ///   <item><description><b>macOS:</b> If <paramref name="enablePlatformSocial"/> is true (e.g. Mac App Store build), connects to Game Center. If false (e.g. Big Fish / Steam / generic standalone), remains no-op.</description></item>
        ///   <item><description><b>Other / Standalone:</b> Stays local/no-op.</description></item>
        /// </list>
        /// </summary>
        /// <param name="enablePlatformSocial">
        /// Pass true to allow platform social services on desktop (e.g. <c>Config.Publisher == Publisher.MacAppStore</c>). On mobile platforms (Android/iOS), this is considered always true.
        /// </param>
        /// <param name="onComplete">Optional callback invoked with the final authentication outcome.</param>
        public static void AutoConnect(bool enablePlatformSocial = true, Action<bool> onComplete = null)
        {
#if UNITY_IOS
            Debug.Log("[NativeSocial] AutoConnect: Initiating Apple Game Center authentication...");
            Authenticate(success =>
            {
                if (success)
                    Debug.Log($"[NativeSocial] Game Center authentication: Success (User: '{Social.localUser.userName}', ID: '{Social.localUser.id}').");
                else
                    Debug.LogWarning($"[NativeSocial] Game Center authentication: Failed. Report: {LastAuthenticationReport}");
                onComplete?.Invoke(success);
            });
#elif UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM
            if (!enablePlatformSocial)
            {
                Debug.Log("[NativeSocial] AutoConnect: Skipped on macOS (platform social is disabled for this store/publisher).");
                onComplete?.Invoke(false);
                return;
            }

            Debug.Log("[NativeSocial] AutoConnect: Initiating Mac App Store Game Center authentication...");
            Authenticate(success =>
            {
                if (success)
                    Debug.Log($"[NativeSocial] Game Center authentication: Success (User: '{Social.localUser.userName}', ID: '{Social.localUser.id}').");
                else
                    Debug.LogWarning($"[NativeSocial] Game Center authentication: Failed. Report: {LastAuthenticationReport}");
                onComplete?.Invoke(success);
            });
#elif UNITY_ANDROID
            Debug.Log("[NativeSocial] AutoConnect: Initiating Google Play Games authentication...");
            Authenticate(success =>
            {
                if (success)
                {
                    Debug.Log($"[NativeSocial] GPGS authentication: Success (User: '{Social.localUser.userName}', ID: '{Social.localUser.id}').");
                    onComplete?.Invoke(true);
                }
                else if (PlayerDeclinedManualSignIn)
                {
                    // Google's guidance: don't push the sign-in UI at every launch on a player who said no.
                    Debug.Log($"[NativeSocial] GPGS auto sign-in did not succeed ({LastAuthenticationReport}) and the player dismissed the sign-in screen before: " +
                              "not prompting at startup. NativeSocial.OpenAchievementsUI() still offers sign-in from the achievements button.");
                    onComplete?.Invoke(false);
                }
                else
                {
                    Debug.LogWarning($"[NativeSocial] GPGS auto sign-in did not succeed ({LastAuthenticationReport}). Retrying with AuthenticateManually...");
                    AuthenticateManually(manualSuccess =>
                    {
                        if (manualSuccess)
                            Debug.Log($"[NativeSocial] GPGS manual authentication: Success (User: '{Social.localUser.userName}', ID: '{Social.localUser.id}').");
                        else
                            Debug.LogWarning($"[NativeSocial] GPGS manual authentication: Failed. Report: {LastAuthenticationReport}");
                        onComplete?.Invoke(manualSuccess);
                    });
                }
            });
#else
            Debug.Log($"[NativeSocial] AutoConnect: No platform social service active on this target ({Application.platform}). Keeping progress local.");
            onComplete?.Invoke(false);
#endif
        }

        /// <summary>
        /// (Android) Requests a server auth code (OAuth 2.0 authorization code) for the signed-in
        /// player, for use with server-side sign-in — e.g. Unity Authentication's
        /// <c>LinkWithGooglePlayGamesAsync(serverAuthCode)</c>. Requires GPGS plugin v2+.
        /// Returns null through the callback when not authenticated.
        /// </summary>
        public static void GetServerAuthCode(Action<string> callback, bool forceRefreshToken = false)
        {
#if UNITY_ANDROID
            if (!IsAuthenticated)
            {
                callback?.Invoke(null);
                return;
            }

            PlayGamesPlatform.Instance.RequestServerSideAccess(forceRefreshToken, code => callback?.Invoke(code));
#else
            callback?.Invoke(null);
#endif
        }
    }

    // ── Types ─────────────────────────────────────────────────────────

    /// <summary>
    /// How the game drives a Steam achievement.
    /// <see cref="ExplicitAchievement"/>: on completion the game calls <c>SetAchievement</c> with
    /// <see cref="SteamEntry.Achievement"/>. <see cref="StatThreshold"/>: the game only writes the
    /// stat and Steamworks auto-unlocks the achievement configured for that stat at its threshold
    /// (the legacy Storm Tale 2 model) — no <c>SetAchievement</c> call is made.
    /// </summary>
    public enum SteamUnlockMode
    {
        /// <summary>Game calls SetAchievement(Achievement) when the achievement is completed.</summary>
        ExplicitAchievement = 0,

        /// <summary>Game only writes the stat; Steamworks unlocks the achievement from the stat threshold.</summary>
        StatThreshold = 1
    }

    /// <summary>Steam achievement definition (stat + achievement API name + unlock model).</summary>
    public struct SteamEntry
    {
        /// <summary>Steamworks stat API name (e.g. "STAT_WILDCARDS"), or empty if this achievement has no backing stat.</summary>
        public string Stat;

        /// <summary>Steamworks achievement API name (e.g. "ACH_WILDCARDS"). Ignored when <see cref="Mode"/> is <see cref="SteamUnlockMode.StatThreshold"/>.</summary>
        public string Achievement;

        /// <summary>Whether the achievement is unlocked explicitly by the game or by a Steamworks stat threshold. Defaults preserve the pre-existing behavior.</summary>
        public SteamUnlockMode Mode;

        /// <summary>
        /// When true, <see cref="NativeSocial.Report"/> writes the absolute <c>current</c> value to
        /// <see cref="Stat"/> (legacy "set the counter" model). When false, it adds the reported delta
        /// to the stat's cached value.
        /// </summary>
        public bool SetStatAbsolute;

        /// <param name="stat">Steamworks stat API name, or empty/null if this achievement has no backing stat (only unlocks, never increments).</param>
        /// <param name="achievement">Steamworks achievement API name. Empty means "no explicit unlock" (<see cref="SteamUnlockMode.StatThreshold"/>).</param>
        public SteamEntry(string stat, string achievement)
            : this(stat, achievement,
                string.IsNullOrEmpty(achievement) ? SteamUnlockMode.StatThreshold : SteamUnlockMode.ExplicitAchievement,
                false)
        {
        }

        /// <param name="stat">Steamworks stat API name, or empty/null if this achievement has no backing stat.</param>
        /// <param name="achievement">Steamworks achievement API name, or empty when <see cref="SteamUnlockMode.StatThreshold"/>.</param>
        /// <param name="mode">Explicit game unlock vs. Steamworks stat threshold.</param>
        /// <param name="setStatAbsolute">Write the stat as an absolute value instead of adding a delta.</param>
        public SteamEntry(string stat, string achievement, SteamUnlockMode mode, bool setStatAbsolute)
        {
            Stat = stat;
            Achievement = achievement;
            Mode = mode;
            SetStatAbsolute = setStatAbsolute;
        }
    }
}
