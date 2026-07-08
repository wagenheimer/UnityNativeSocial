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
        /// Report achievement progress to the active platform.
        /// </summary>
        /// <param name="locId">Local identifier (key in the map passed to Initialize).</param>
        /// <param name="delta">How much the counter increased (used by Android/Steam).</param>
        /// <param name="current">Current counter value (used by iOS for percentage).</param>
        /// <param name="total">Total needed to complete (used by iOS for percentage).</param>
        /// <param name="completed">Whether the achievement is fully completed.</param>
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
        private static void ReportSteam(string locId, int delta, bool completed)
        {
            if (!SteamReady || locId == null) return;
            if (!_steamMap.TryGetValue(locId, out var entry)) return;

            bool dirty = false;

            if (delta > 0 && !string.IsNullOrEmpty(entry.Stat) && SteamUserStats.GetStat(entry.Stat, out int current))
            {
                SteamUserStats.SetStat(entry.Stat, current + delta);
                dirty = true;
            }

            if (completed && !string.IsNullOrEmpty(entry.Achievement))
            {
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

        /// <summary>Flush pending Steam stats to disk/server. Call on game save.</summary>
        public static void Flush()
        {
            if (SteamReady)
                SteamUserStats.StoreStats();
        }
#endif

        // ── Sync completed ────────────────────────────────────────────

        /// <summary>Sync already-completed achievements after platform auth.</summary>
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

        /// <summary>Opens the platform-native achievements screen (Google Play Games or Game Center).</summary>
        public static void ShowAchievementsUI()
        {
#if UNITY_ANDROID
            if (IsAuthenticated)
                PlayGamesPlatform.Instance.ShowAchievementsUI();
#elif UNITY_IOS
            GameCenterPlatform.ShowAchievementsUI();
#else
            // Steam has no built-in achievements overlay API exposed through Steamworks.NET
            // for this purpose (it's driven by the Steam client's own overlay), so there is
            // intentionally no branch here for Steam — callers should show their own UI instead.
            Debug.LogWarning("[NativeSocial] ShowAchievementsUI not implemented on this platform.");
#endif
        }

        // ── Authentication ────────────────────────────────────────────

        /// <summary>
        /// Authenticates with Game Center on iOS. No-op (always reports failure) on other platforms,
        /// since Android uses GPGS's own sign-in flow and Steam authenticates automatically via SteamAPI.Init().
        /// </summary>
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

        public SteamEntry(string stat, string achievement)
        {
            Stat = stat;
            Achievement = achievement;
        }
    }
}
