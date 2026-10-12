#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
using System.Collections.Generic;
using Steamworks;

namespace Wagenheimer.NativeSocial
{
    /// <summary>Steamworks backend (desktop Steam builds) of <see cref="NativeSocial"/>.</summary>
    public static partial class NativeSocial
    {
        /// <summary>
        /// Whether the Steamworks API has finished initializing (e.g. <c>SteamManager.Initialized</c>).
        /// Set this once Steam is ready; stat/achievement calls are no-ops until then.
        /// </summary>
        public static bool SteamReady { get; set; }

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
    }
}
#endif
