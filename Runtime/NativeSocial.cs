using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

#if UNITY_IOS
using UnityEngine.SocialPlatforms.GameCenter;
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
using Steamworks;
#endif

namespace Wagenheimer.NativeSocial
{
    /// <summary>
    /// Platform-native replacement for Unity's deprecated <c>UnityEngine.Social</c> API.
    /// Dispatches achievement/stat calls directly to Google Play Games (Android),
    /// Game Center (iOS), or Steamworks.NET (Steam desktop builds) based on the
    /// active compile-time platform. Callers use a single, platform-agnostic API and
    /// never touch the underlying SDKs directly.
    /// </summary>
    public static class NativeSocial
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

#if UNITY_IOS
            try
            {
                // In Unity, GameKit completion banner is disabled by default. Enable it so Apple's native
                // achievement popup drops down from the top of the screen when progress reaches 100%.
                UnityEngine.SocialPlatforms.GameCenter.GameCenterPlatform.ShowDefaultAchievementCompletionBanner(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeSocial] Failed to enable iOS achievement banner: {ex.Message}");
            }
#endif

            OnLog?.Invoke($"[NativeSocial] Initialized. Android={_androidMap.Count}, iOS={_iosMap.Count}, Steam={_steamMap.Count}");
        }

        // ── Status ────────────────────────────────────────────────────

#if UNITY_ANDROID
        /// <summary>
        /// Whether the player is currently signed in to Google Play Games.
        /// Set this from your GPGS sign-in callback; achievement calls are no-ops until then.
        /// </summary>
        public static bool IsAuthenticated { get; set; }
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
        /// <summary>
        /// Whether the Steamworks API has finished initializing (e.g. <c>SteamManager.Initialized</c>).
        /// Set this once Steam is ready; stat/achievement calls are no-ops until then.
        /// </summary>
        public static bool SteamReady { get; set; }
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
            ReportAndroid(locId, delta, completed);
#elif UNITY_IOS
            ReportIOS(locId, current, total, completed);
#elif WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
            ReportSteam(locId, delta, current, completed);
#else
            OnLog?.Invoke($"[NativeSocial] Report('{locId}') not dispatched: no platform backend on this build target ({Application.platform}). Editor/standalone without Steam keeps progress local only.");
#endif
        }

#if UNITY_ANDROID
        /// <summary>Increments/unlocks the mapped Google Play Games achievement.</summary>
        /// <param name="locId">Game-defined achievement key, looked up in <see cref="_androidMap"/> to find the GPGS achievement ID.</param>
        /// <param name="delta">How much to increment the GPGS incremental-achievement counter by. 0 or negative means "don't increment" (e.g. a completed-only call).</param>
        /// <param name="completed">If true, unlocks the achievement outright regardless of its counter state.</param>
        private static void ReportAndroid(string locId, int delta, bool completed)
        {
            if (locId == null) { OnLog?.Invoke("[NativeSocial] ReportAndroid skipped: locId is null."); return; }
            if (!IsAuthenticated) { OnLog?.Invoke($"[NativeSocial] ReportAndroid('{locId}') skipped: GPGS is not authenticated yet (reports are dropped while signed out)."); return; }
            if (!_androidMap.TryGetValue(locId, out var gpgsId)) { OnLog?.Invoke($"[NativeSocial] ReportAndroid('{locId}') skipped: no GPGS achievement id mapped for this LocID."); return; }

            if (delta > 0)
            {
                OnLog?.Invoke($"[NativeSocial] GPGS IncrementAchievement('{gpgsId}') by {delta} for '{locId}'. NOTE: the Play Games screen only shows a progress bar if '{gpgsId}' is an INCREMENTAL achievement (with steps) in the Play Console; a STANDARD achievement shows locked/unlocked only.");
                PlayGamesPlatform.Instance.IncrementAchievement(gpgsId, delta, _ => { });
            }

            if (completed)
            {
                OnLog?.Invoke($"[NativeSocial] GPGS UnlockAchievement('{gpgsId}') for '{locId}'.");
                PlayGamesPlatform.Instance.UnlockAchievement(gpgsId, _ => { });
            }
        }
#endif

#if UNITY_IOS
        /// <summary>Reports the mapped Game Center achievement as a 0-100% completion percentage.</summary>
        /// <param name="locId">Game-defined achievement key, looked up in <see cref="_iosMap"/> to find the Game Center achievement ID.</param>
        /// <param name="current">Current progress count (e.g. 5 wildcards collected so far). Combined with <paramref name="total"/> to compute the percentage Game Center expects.</param>
        /// <param name="total">Progress count needed to fully complete the achievement (e.g. 20 wildcards). Must be &gt; 0 or the report is skipped.</param>
        /// <param name="completed">If true, reports 100% regardless of <paramref name="current"/>/<paramref name="total"/>.</param>
        private static void ReportIOS(string locId, int current, int total, bool completed)
        {
            if (locId == null) { OnLog?.Invoke("[NativeSocial] ReportIOS skipped: locId is null."); return; }
            if (!_iosMap.TryGetValue(locId, out var gcId)) { OnLog?.Invoke($"[NativeSocial] ReportIOS('{locId}') skipped: no Game Center achievement id mapped for this LocID."); return; }
            if (total <= 0) { OnLog?.Invoke($"[NativeSocial] ReportIOS('{locId}') skipped: total must be > 0 (got {total})."); return; }

            // Game Center achievements are percentage-based rather than increment-based,
            // so we derive a percent from current/total instead of using delta directly.
            double percent = completed
                ? 100.0
                : Math.Min((double)current / total * 100.0, 99.0);

            OnLog?.Invoke($"[NativeSocial] iOS ReportProgress('{gcId}') {percent:F1}% for '{locId}'. Game Center shows this as a progress bar until it reaches 100%.");
            Social.ReportProgress(gcId, percent, success =>
            {
                if (success)
                {
                    OnLog?.Invoke($"[NativeSocial] iOS ReportProgress SUCCESS for '{gcId}' ({percent:F1}%)");
                }
                else
                {
                    string err = $"[NativeSocial] iOS ReportProgress FAILED for '{gcId}' ({percent:F1}%). Check if player is authenticated or if '{gcId}' matches the Game Center Achievement ID (vendorIdentifier in App Store Connect).";
                    Debug.LogWarning(err);
                    OnLog?.Invoke(err);
                }
            });
        }
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
        /// <summary>Updates the mapped Steam stat/achievement and flushes to Steam only when something changed.</summary>
        /// <param name="locId">Game-defined achievement key, looked up in <see cref="_steamMap"/> to find the Steam stat/achievement API names.</param>
        /// <param name="delta">How much to add to the Steam stat (<see cref="SteamEntry.Stat"/>) when <see cref="SteamEntry.SetStatAbsolute"/> is false, e.g. +1 wildcard collected. 0 or negative means "don't touch the stat". Ignored in absolute mode.</param>
        /// <param name="current">Absolute progress value written to the stat when <see cref="SteamEntry.SetStatAbsolute"/> is true (the legacy "set the counter" model). Mirrors the value iOS already uses.</param>
        /// <param name="completed">If true, unlocks the Steam achievement (<see cref="SteamEntry.Achievement"/>) when <see cref="SteamEntry.Mode"/> is <see cref="SteamUnlockMode.ExplicitAchievement"/>. Ignored in <see cref="SteamUnlockMode.StatThreshold"/> (Steamworks unlocks it from the stat).</param>
        private static void ReportSteam(string locId, int delta, int current, bool completed)
        {
            if (locId == null) { OnLog?.Invoke("[NativeSocial] ReportSteam skipped: locId is null."); return; }
            if (!SteamReady) { OnLog?.Invoke($"[NativeSocial] ReportSteam('{locId}') skipped: Steamworks is not ready yet."); return; }
            if (!_steamMap.TryGetValue(locId, out var entry)) { OnLog?.Invoke($"[NativeSocial] ReportSteam('{locId}') skipped: no Steam stat/achievement mapped for this LocID."); return; }

            // Only call StoreStats() (see below) if we actually changed something this call.
            bool dirty = false;

            if (!string.IsNullOrEmpty(entry.Stat))
            {
                if (entry.SetStatAbsolute)
                {
                    // Absolute model (Storm Tale 2 legacy): write the exact counter value the game
                    // holds. Idempotent — skip SetStat when Steam's cached value already matches.
                    bool known = SteamUserStats.GetStat(entry.Stat, out int statCurrent);
                    if (!known || statCurrent != current)
                    {
                        SteamUserStats.SetStat(entry.Stat, current);
                        dirty = true;
                    }
                }
                else if (delta > 0 && SteamUserStats.GetStat(entry.Stat, out int statCurrent))
                {
                    // Delta model: Steam has no "increment stat by N" call, only "set stat to this
                    // absolute value", so read the cached value and add delta to it.
                    SteamUserStats.SetStat(entry.Stat, statCurrent + delta);
                    dirty = true;
                }
            }

            if (entry.Mode == SteamUnlockMode.ExplicitAchievement && completed && !string.IsNullOrEmpty(entry.Achievement))
            {
                // GetAchievement's out param tells us whether it's already unlocked — skip
                // re-unlocking (and marking dirty) if there's nothing to do.
                if (!SteamUserStats.GetAchievement(entry.Achievement, out bool already) || !already)
                {
                    SteamUserStats.SetAchievement(entry.Achievement);
                    dirty = true;
                }
            }

            // SetStat/SetAchievement only update the local cache — StoreStats() is what
            // actually pushes the change to Steam, so only call it when something changed.
            if (dirty)
            {
                OnLog?.Invoke($"[NativeSocial] Steam stat/achievement updated for '{locId}' and flushed (StoreStats).");
                SteamUserStats.StoreStats();
            }
        }

        /// <summary>
        /// Flush pending Steam stats/achievements to Steam's servers. Call this whenever the
        /// game saves — <see cref="ReportSteam"/> and <see cref="SyncCompletedSteam"/> already
        /// call <c>SteamUserStats.StoreStats()</c> themselves when something changes, so this is
        /// a safety-net flush for any edge cases (e.g. process termination) rather than the only place it happens.
        /// </summary>
        public static void Flush()
        {
            if (SteamReady)
                SteamUserStats.StoreStats();
        }
#endif

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
#elif UNITY_IOS
            SyncCompletedIOS(completedLocIds);
#elif WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
            SyncCompletedSteam(completedLocIds);
#endif
        }

#if UNITY_ANDROID
        /// <summary>
        /// Re-unlocks every already-completed achievement on Google Play Games.
        /// Needed because local save data can be ahead of the platform (e.g. offline progress,
        /// account switch, reinstall) — UnlockAchievement is idempotent, so this is safe to repeat.
        /// </summary>
        /// <param name="completedLocIds">LocIDs of achievements to re-unlock, looked up in <see cref="_androidMap"/> to find each GPGS achievement ID.</param>
        private static void SyncCompletedAndroid(IEnumerable<string> completedLocIds)
        {
            if (!IsAuthenticated) return;
            foreach (var locId in completedLocIds)
            {
                if (locId == null) continue;
                if (_androidMap.TryGetValue(locId, out var gpgsId))
                    PlayGamesPlatform.Instance.UnlockAchievement(gpgsId, _ => { });
            }
        }
#endif

#if UNITY_IOS
        /// <summary>Re-reports 100% progress for every already-completed achievement on Game Center.</summary>
        /// <param name="completedLocIds">LocIDs of achievements to re-report, looked up in <see cref="_iosMap"/> to find each Game Center achievement ID.</param>
        private static void SyncCompletedIOS(IEnumerable<string> completedLocIds)
        {
            foreach (var locId in completedLocIds)
            {
                if (locId == null) continue;
                if (_iosMap.TryGetValue(locId, out var gcId))
                    Social.ReportProgress(gcId, 100.0, _ => { });
            }
        }
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
        /// <summary>Re-unlocks every already-completed achievement on Steam, storing once at the end.</summary>
        /// <param name="completedLocIds">LocIDs of achievements to re-unlock, looked up in <see cref="_steamMap"/> to find each Steam achievement API name.</param>
        private static void SyncCompletedSteam(IEnumerable<string> completedLocIds)
        {
            if (!SteamReady) return;
            bool dirty = false;
            foreach (var locId in completedLocIds)
            {
                if (locId == null) continue;
                if (_steamMap.TryGetValue(locId, out var entry) &&
                    entry.Mode == SteamUnlockMode.ExplicitAchievement &&
                    !string.IsNullOrEmpty(entry.Achievement))
                {
                    if (!SteamUserStats.GetAchievement(entry.Achievement, out bool already) || !already)
                    {
                        SteamUserStats.SetAchievement(entry.Achievement);
                        dirty = true;
                    }
                }
            }
            if (dirty) SteamUserStats.StoreStats();
        }
#endif

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
#elif UNITY_IOS
            OnLog?.Invoke("[NativeSocial] Opening Game Center native achievements UI.");
            Social.ShowAchievementsUI();
            return true;
#else
            // Steam/standalone: no native UI exists to show, so the caller must provide its own.
            OnLog?.Invoke("[NativeSocial] ShowAchievementsUI: no native achievements UI on this platform (Steam/standalone).");
            return false;
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
                PlayGamesPlatform.Instance.ReportScore(score, androidId, _ => { });
            }
            else OnLog?.Invoke($"[NativeSocial] SubmitScore('{locId}') skipped: no GPGS leaderboard id mapped.");
#elif UNITY_IOS
            if (_iosLeaderboardMap.TryGetValue(locId, out var iosId))
            {
                OnLog?.Invoke($"[NativeSocial] Game Center ReportScore('{iosId}') = {score}.");
                Social.ReportScore(score, iosId, _ => { });
            }
            else OnLog?.Invoke($"[NativeSocial] SubmitScore('{locId}') skipped: no Game Center leaderboard mapped.");
#endif
        }

        /// <summary>
        /// Opens the platform-native leaderboard screen (Google Play Games on Android, Game Center
        /// on iOS). Pass null to open the all-leaderboards screen on Android.
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
#elif UNITY_IOS
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

        /// <summary>
        /// Authenticates with Game Center on iOS, or Google Play Games on Android.
        /// On Android, this returns the result of the plugin's automatic sign-in attempt
        /// (Play Games Services v11+ tries to sign in automatically when the app starts).
        /// On Steam desktop builds it is a no-op, since Steamworks authenticates via SteamAPI.Init().
        /// </summary>
        /// <param name="callback">Invoked with true on successful authentication, false on failure or on any non-supported platform.</param>
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
                    return "The player cancelled the sign-in.";
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

#if UNITY_ANDROID
        private const string GpgsAppIdMetaData = "com.google.android.gms.games.APP_ID";
        private const int PackageManagerGetMetaData = 128;
        private const int MinimumAppIdDigits = 8;

        /// <summary>
        /// Null when the built app has a plausible Google Play Games application id in its manifest meta-data; otherwise what is wrong.
        /// The id is written there by "Window > Google Play Games > Setup > Android setup"; an empty value is the most common reason sign-in never works.
        /// </summary>
        internal static string FindAndroidAppIdProblem()
        {
#if UNITY_EDITOR
            return null;
#else
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var info = packageManager.Call<AndroidJavaObject>("getApplicationInfo", activity.Call<string>("getPackageName"), PackageManagerGetMetaData))
                using (var metaData = info.Get<AndroidJavaObject>("metaData"))
                {
                    var value = metaData != null ? metaData.Call<string>("getString", GpgsAppIdMetaData) : null;
                    if (string.IsNullOrEmpty(value))
                        return $"the manifest has no '{GpgsAppIdMetaData}' meta-data. Run Window > Google Play Games > Setup > Android setup and rebuild.";

                    var digits = 0;
                    foreach (var c in value)
                        if (char.IsDigit(c)) digits++;

                    return digits >= MinimumAppIdDigits
                        ? null
                        : $"the Play Games application id in the manifest is empty or invalid (value '{value}'). Run Window > Google Play Games > Setup > Android setup with your Play Console resources and rebuild.";
                }
            }
            catch (Exception ex)
            {
                return "could not read the manifest meta-data: " + ex.Message;
            }
#endif
        }
#endif

        public static void Authenticate(Action<bool> callback)
        {
#if UNITY_IOS
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
                OnAuthenticated?.Invoke(success);
                OnLog?.Invoke($"[NativeSocial] Game Center Authenticate: {success}");
                LogAuthenticationResult("Game Center", success, success ? "signed in" : (string.IsNullOrEmpty(error) ? "no error message from Game Center (is Game Center enabled in Settings and the app set up in App Store Connect?)" : error));
                callback?.Invoke(success);
            });
#elif UNITY_ANDROID
            // Say it loudly BEFORE signing in: a missing Play Games application id makes every call fail without a useful message.
            var configurationProblem = FindAndroidAppIdProblem();
            if (configurationProblem != null)
                Debug.LogError("[NativeSocial] Google Play Games is not configured: " + configurationProblem);

            PlayGamesPlatform.Instance.Authenticate(status =>
            {
                var success = status == SignInStatus.Success;
                if (success) IsAuthenticated = true;
                OnAuthenticated?.Invoke(success);
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
            PlayGamesPlatform.Instance.ManuallyAuthenticate(status =>
            {
                var success = status == SignInStatus.Success;
                if (success) IsAuthenticated = true;
                OnAuthenticated?.Invoke(success);
                OnLog?.Invoke($"[NativeSocial] GPGS ManuallyAuthenticate: {status} ({success})");
                callback?.Invoke(success);
            });
#else
            OnAuthenticated?.Invoke(false);
            callback?.Invoke(false);
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
