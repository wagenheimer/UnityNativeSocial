using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wagenheimer.NativeSocial
{
    /// <summary>Apple Game Center backend (iOS, and macOS outside Steam builds) of <see cref="NativeSocial"/>.</summary>
    public static partial class NativeSocial
    {
#if UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
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

            // Not signed in yet (auth still running, player declined, offline...): Game Center would just fail.
            // Keep the highest progress and deliver it from the next successful Authenticate.
            if (!Social.localUser.authenticated)
            {
                QueuePendingProgress(locId, current, total, completed, "Game Center");
                return;
            }

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

#if UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
        /// <summary>Re-reports 100% progress for every already-completed achievement on Game Center.</summary>
        /// <param name="completedLocIds">LocIDs of achievements to re-report, looked up in <see cref="_iosMap"/> to find each Game Center achievement ID.</param>
        private static void SyncCompletedIOS(IEnumerable<string> completedLocIds)
        {
            foreach (var locId in completedLocIds)
            {
                if (locId == null) continue;
                if (_iosMap.TryGetValue(locId, out var gcId))
                    Social.ReportProgress(gcId, 100.0, ok => { if (!ok) Debug.LogWarning($"[NativeSocial] Game Center re-sync of '{gcId}' ({locId}) FAILED."); });
            }
        }
#endif

        /// <summary>
        /// (macOS) Extra explanation appended to a Game Center failure. A Mac build can only use Game Center when it
        /// was signed with the com.apple.developer.game-center entitlement, which requires an embedded Mac App Store
        /// provisioning profile; builds without one (or copies re-signed to run locally) fail with a vague error.
        /// </summary>
        internal static string MacGameCenterHint()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            // On a Mac player Application.dataPath is "<app>/Contents".
            if (!System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath, "embedded.provisionprofile")))
                return " This Mac build has no embedded provisioning profile, so it was signed WITHOUT the Game Center entitlement and cannot sign in. " +
                       "The Forge build log says why (Mac App Store profile missing, or the App ID lacks the Game Center capability).";
            return " On macOS Game Center only works in the build installed from the App Store / TestFlight; a copy re-signed to run locally has no Game Center entitlement.";
#else
            return string.Empty;
#endif
        }
    }
}
