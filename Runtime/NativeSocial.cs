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
    public static class NativeSocial
    {
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
        public static bool IsAuthenticated { get; set; }
#endif

#if WAGENHEIMER_NATIVESOCIAL_STEAM
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

#if UNITY_ANDROID
            ReportAndroid(locId, delta, completed);
#elif UNITY_IOS
            ReportIOS(locId, current, total, completed);
#elif WAGENHEIMER_NATIVESOCIAL_STEAM
            ReportSteam(locId, delta, completed);
#endif
        }

#if UNITY_ANDROID
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
        private static void ReportIOS(string locId, int current, int total, bool completed)
        {
            if (locId == null) return;
            if (!_iosMap.TryGetValue(locId, out var gcId)) return;
            if (total <= 0) return;

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

        public static void ShowAchievementsUI()
        {
#if UNITY_ANDROID
            if (IsAuthenticated)
                PlayGamesPlatform.Instance.ShowAchievementsUI();
#elif UNITY_IOS
            GameCenterPlatform.ShowAchievementsUI();
#else
            Debug.LogWarning("[NativeSocial] ShowAchievementsUI not implemented on this platform.");
#endif
        }

        // ── Authentication ────────────────────────────────────────────

        public static void Authenticate(Action<bool> callback)
        {
#if UNITY_IOS
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
        public string Stat;
        public string Achievement;

        public SteamEntry(string stat, string achievement)
        {
            Stat = stat;
            Achievement = achievement;
        }
    }
}
