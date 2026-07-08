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

#if WAGENHEIMER_NATIVESOCIAL_STEAM
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
        private static bool _initialized;

        /// <summary>
        /// Register achievement ID maps per platform.
        /// Must be called once at game startup before any Report/Auth calls.
        /// </summary>
        /// <param name="androidMap">Maps game-defined LocID -> Google Play Games achievement ID (e.g. "CgkI_aXR36YSEAIQAQ"). Pass null if not targeting Android.</param>
        /// <param name="iosMap">Maps game-defined LocID -> Game Center achievement ID (e.g. "com.example.wildcards"). Pass null if not targeting iOS.</param>
        /// <param name="steamMap">Maps game-defined LocID -> <see cref="SteamEntry"/> (Steam stat + achievement API names). Pass null if not targeting Steam.</param>
        public static void Initialize(
            Dictionary<string, string> androidMap = null,
            Dictionary<string, string> iosMap = null,
            Dictionary<string, SteamEntry> steamMap = null)
        {
            _androidMap = androidMap ?? new Dictionary<string, string>();
            _iosMap = iosMap ?? new Dictionary<string, string>();
            _steamMap = steamMap ?? new Dictionary<string, SteamEntry>();
            _initialized = true;
        }

        // ── Status ────────────────────────────────────────────────────

#if UNITY_ANDROID
        /// <summary>
        /// Whether the player is currently signed in to Google Play Games.
        /// Set this from your GPGS sign-in callback; achievement calls are no-ops until then.
        /// </summary>
        public static bool IsAuthenticated { get; set; }
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM
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
        /// use only the ones it needs (Android/Steam use <paramref name="delta"/>, iOS uses
        /// <paramref name="current"/>/<paramref name="total"/>).
        /// </summary>
        /// <param name="locId">Game-defined achievement key (the key used in the maps passed to <see cref="Initialize"/>). Identifies which achievement this call is for.</param>
        /// <param name="delta">
        /// How much the underlying counter/stat should increase by since the last call
        /// (e.g. "+1 wildcard collected just now"). Used by Android (GPGS IncrementAchievement)
        /// and Steam (adds to the mapped stat). Pass 0 when you only want to report/check
        /// <paramref name="completed"/> without bumping a counter.
        /// </param>
        /// <param name="current">Current absolute progress value (e.g. "5 of 20 wildcards collected"). Only used by iOS, which reports achievements as a 0-100% completion percentage rather than an incrementing counter.</param>
        /// <param name="total">Progress value that represents 100% completion (e.g. 20 wildcards total). Only used by iOS, alongside <paramref name="current"/>, to compute that percentage.</param>
        /// <param name="completed">Whether the achievement is fully completed right now. When true, all platforms unlock/complete it outright regardless of the counter parameters.</param>
        public static void Report(string locId, int delta, int current, int total, bool completed)
        {
            if (!_initialized)
            {
                Debug.LogWarning("[NativeSocial] Report called before Initialize.");
                return;
            }

            // Exactly one of these branches is compiled in per build target — there is
            // no runtime platform switch here, each build only ever contains its own path.
#if UNITY_ANDROID
            ReportAndroid(locId, delta, completed);
#elif UNITY_IOS
            ReportIOS(locId, current, total, completed);
#elif WAGENHEIMER_NATIVESOCIAL_STEAM
            ReportSteam(locId, delta, completed);
#endif
        }

#if UNITY_ANDROID
        /// <summary>Increments/unlocks the mapped Google Play Games achievement.</summary>
        /// <param name="locId">Game-defined achievement key, looked up in <see cref="_androidMap"/> to find the GPGS achievement ID.</param>
        /// <param name="delta">How much to increment the GPGS incremental-achievement counter by. 0 or negative means "don't increment" (e.g. a completed-only call).</param>
        /// <param name="completed">If true, unlocks the achievement outright regardless of its counter state.</param>
        private static void ReportAndroid(string locId, int delta, bool completed)
        {
            if (!IsAuthenticated || locId == null) return;
            if (!_androidMap.TryGetValue(locId, out var gpgsId)) return;

            if (delta > 0)
                PlayGamesPlatform.Instance.IncrementAchievement(gpgsId, delta, _ => { });

            if (completed)
                PlayGamesPlatform.Instance.UnlockAchievement(gpgsId, _ => { });
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
            if (locId == null) return;
            if (!_iosMap.TryGetValue(locId, out var gcId)) return;
            if (total <= 0) return;

            // Game Center achievements are percentage-based rather than increment-based,
            // so we derive a percent from current/total instead of using delta directly.
            double percent = completed
                ? 100.0
                : Math.Min((double)current / total * 100.0, 99.0);

            GameCenterPlatform.ReportProgress(gcId, percent, success =>
            {
                if (!success)
                    Debug.LogWarning($"[NativeSocial] iOS ReportProgress failed for {gcId} ({percent:F1}%)");
            });
        }
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM
        /// <summary>Updates the mapped Steam stat/achievement and flushes to Steam only when something changed.</summary>
        /// <param name="locId">Game-defined achievement key, looked up in <see cref="_steamMap"/> to find the Steam stat/achievement API names.</param>
        /// <param name="delta">How much to add to the Steam stat (<see cref="SteamEntry.Stat"/>), e.g. +1 wildcard collected. 0 or negative means "don't touch the stat".</param>
        /// <param name="completed">If true, unlocks the Steam achievement (<see cref="SteamEntry.Achievement"/>) if it isn't already unlocked.</param>
        private static void ReportSteam(string locId, int delta, bool completed)
        {
            if (!SteamReady || locId == null) return;
            if (!_steamMap.TryGetValue(locId, out var entry)) return;

            // Only call StoreStats() (see below) if we actually changed something this call.
            bool dirty = false;

            // GetStat reads the current cached value so we can add delta to it — Steam has no
            // "increment stat by N" call, only "set stat to this absolute value".
            if (delta > 0 && !string.IsNullOrEmpty(entry.Stat) && SteamUserStats.GetStat(entry.Stat, out int current))
            {
                SteamUserStats.SetStat(entry.Stat, current + delta);
                dirty = true;
            }

            if (completed && !string.IsNullOrEmpty(entry.Achievement))
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
                SteamUserStats.StoreStats();
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
#elif WAGENHEIMER_NATIVESOCIAL_STEAM
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
                    GameCenterPlatform.ReportProgress(gcId, 100.0, _ => { });
            }
        }
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM
        /// <summary>Re-unlocks every already-completed achievement on Steam, storing once at the end.</summary>
        /// <param name="completedLocIds">LocIDs of achievements to re-unlock, looked up in <see cref="_steamMap"/> to find each Steam achievement API name.</param>
        private static void SyncCompletedSteam(IEnumerable<string> completedLocIds)
        {
            if (!SteamReady) return;
            bool dirty = false;
            foreach (var locId in completedLocIds)
            {
                if (locId == null) continue;
                if (_steamMap.TryGetValue(locId, out var entry) && !string.IsNullOrEmpty(entry.Achievement))
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
            if (!IsAuthenticated) return false;
            PlayGamesPlatform.Instance.ShowAchievementsUI();
            return true;
#elif UNITY_IOS
            GameCenterPlatform.ShowAchievementsUI();
            return true;
#else
            // Steam/standalone: no native UI exists to show, so the caller must provide its own.
            return false;
#endif
        }

        // ── Authentication ────────────────────────────────────────────

        /// <summary>
        /// Authenticates with Game Center on iOS. No-op (always reports failure) on other platforms,
        /// since Android uses GPGS's own sign-in flow and Steam authenticates automatically via SteamAPI.Init().
        /// </summary>
        /// <param name="callback">Invoked with true on successful Game Center authentication, false on failure or on any non-iOS platform.</param>
        public static void Authenticate(Action<bool> callback)
        {
#if UNITY_IOS
            // UnityEngine.SocialPlatforms.Social (the deprecated Unity Social API) is still the only
            // entry point Unity exposes for Game Center authentication — GameCenterPlatform itself has
            // no direct Authenticate method. This is an intentional, unavoidable use of the deprecated
            // API surface, not leftover code to "clean up"; removing it would break iOS auth entirely.
            Social.localUser.Authenticate(success => callback?.Invoke(success));
#else
            callback?.Invoke(false);
#endif
        }
    }

    // ── Types ─────────────────────────────────────────────────────────

    /// <summary>Steam achievement definition (stat + achievement API name).</summary>
    public struct SteamEntry
    {
        /// <summary>Steamworks stat API name (e.g. "STAT_WILDCARDS"), or empty if this achievement has no backing stat.</summary>
        public string Stat;

        /// <summary>Steamworks achievement API name (e.g. "ACH_WILDCARDS").</summary>
        public string Achievement;

        /// <param name="stat">Steamworks stat API name, or empty/null if this achievement has no backing stat (only unlocks, never increments).</param>
        /// <param name="achievement">Steamworks achievement API name.</param>
        public SteamEntry(string stat, string achievement)
        {
            Stat = stat;
            Achievement = achievement;
        }
    }
}
