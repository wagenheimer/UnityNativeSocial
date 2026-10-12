#if UNITY_ANDROID
using System.Collections.Generic;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using UnityEngine;

namespace Wagenheimer.NativeSocial
{
    /// <summary>
    /// Google Play Games plumbing shared by the public API in <c>NativeSocial.cs</c>: one-time activation,
    /// sign-in bookkeeping, delivery of progress queued while signed out (<c>NativeSocial.Pending.cs</c>), and
    /// re-checking sign-in when the game comes back to the foreground.
    /// </summary>
    public static partial class NativeSocial
    {
        private const string ManualSignInDeclinedPrefsKey = "NativeSocial.GpgsManualSignInDeclined";

        private static bool _gpgsActivated;
        private static bool _signInInProgress;
        private static readonly HashSet<string> _reportedGpgsFailures = new HashSet<string>();

        /// <summary>
        /// Activates the Play Games platform once per session (with SDK debug logging) and starts watching for
        /// the game returning to the foreground, so a player who signs in through the Play Games app is picked up.
        /// </summary>
        private static void EnsureGpgsActivated()
        {
            if (_gpgsActivated) return;
            _gpgsActivated = true;

            PlayGamesPlatform.DebugLogEnabled = true;
            PlayGamesPlatform.Activate();
            Application.focusChanged += OnApplicationFocusChanged;
        }

        /// <summary>
        /// Back in the foreground and still signed out: ask GPGS again without any UI. Only a success is acted on,
        /// so a player who keeps playing signed out sees nothing and the log stays quiet.
        /// </summary>
        private static void OnApplicationFocusChanged(bool hasFocus)
        {
            if (!hasFocus || IsAuthenticated || _signInInProgress) return;

            _signInInProgress = true;
            PlayGamesPlatform.Instance.Authenticate(status =>
            {
                _signInInProgress = false;
                if (status != SignInStatus.Success) return;

                Debug.Log("[NativeSocial] GPGS signed in after the game returned to the foreground (player signed in outside the game).");
                HandleGpgsSignInResult(true);
                LogAuthenticationResult("Google Play Games", true, "signed in");
                OnAuthenticated?.Invoke(true);
            });
        }

        /// <summary>Single place that updates the signed-in state; a successful sign-in also delivers queued progress.</summary>
        private static void HandleGpgsSignInResult(bool success)
        {
            IsAuthenticated = success;
            if (success) FlushPendingAndroidProgress();
        }

        /// <summary>
        /// The player dismissed the sign-in UI themselves (not a configuration rejection). AutoConnect stops
        /// prompting at every launch after this; <see cref="OpenAchievementsUI"/> still offers sign-in on demand.
        /// </summary>
        private static void RememberManualSignInOutcome(bool success, SignInStatus status, float elapsedSeconds)
        {
            if (success)
                PlayerPrefs.DeleteKey(ManualSignInDeclinedPrefsKey);
            else if (status == SignInStatus.Canceled && elapsedSeconds >= QuickCancelSeconds)
                PlayerPrefs.SetInt(ManualSignInDeclinedPrefsKey, 1);
            else
                return;

            PlayerPrefs.Save();
        }

        private static bool PlayerDeclinedManualSignIn => PlayerPrefs.GetInt(ManualSignInDeclinedPrefsKey, 0) == 1;

        /// <summary>
        /// Sends progress for a mapped achievement. With an absolute <paramref name="steps"/> it uses
        /// <c>SetStepsAtLeast</c>, which is idempotent, so progress can be re-sent after a later sign-in without
        /// double counting; <c>IncrementAchievement</c> is only the fallback when no absolute value is known.
        /// </summary>
        private static void SendAndroidProgress(string locId, string gpgsId, int delta, int steps, bool completed)
        {
            if (steps > 0)
            {
                OnLog?.Invoke($"[NativeSocial] GPGS SetStepsAtLeast('{gpgsId}', {steps}) for '{locId}'. NOTE: only INCREMENTAL achievements (with steps) in the Play Console accept steps; a STANDARD one only unlocks.");
                PlayGamesPlatform.Instance.SetStepsAtLeast(gpgsId, steps, ok => LogGpgsCallback("SetStepsAtLeast", gpgsId, locId, ok));
            }
            else if (delta > 0)
            {
                OnLog?.Invoke($"[NativeSocial] GPGS IncrementAchievement('{gpgsId}') by {delta} for '{locId}'.");
                PlayGamesPlatform.Instance.IncrementAchievement(gpgsId, delta, ok => LogGpgsCallback("IncrementAchievement", gpgsId, locId, ok));
            }

            if (completed)
            {
                OnLog?.Invoke($"[NativeSocial] GPGS UnlockAchievement('{gpgsId}') for '{locId}'.");
                PlayGamesPlatform.Instance.UnlockAchievement(gpgsId, ok => LogGpgsCallback("UnlockAchievement", gpgsId, locId, ok));
            }
        }

        /// <summary>
        /// GPGS reports failures only through these callbacks. Warns once per call/id pair: a STANDARD achievement
        /// rejects step updates on every report, which would otherwise flood the log.
        /// </summary>
        private static void LogGpgsCallback(string call, string gpgsId, string context, bool ok)
        {
            if (ok) return;
            if (!_reportedGpgsFailures.Add(call + "|" + gpgsId)) return;

            var message = $"[NativeSocial] GPGS {call}('{gpgsId}') FAILED for '{context}'. Check that the id exists in this game's Play Console, " +
                          "that its type matches the call (steps need an INCREMENTAL achievement), and that the player is still signed in.";
            Debug.LogWarning(message);
            OnLog?.Invoke(message);
        }

        private static void QueuePendingAndroidProgress(string locId, int steps, bool completed) =>
            QueuePendingProgress(locId, steps, 0, completed, "Google Play Games");

        private static void FlushPendingAndroidProgress() =>
            FlushPendingProgress("Google Play Games", entry =>
            {
                if (_androidMap != null && _androidMap.TryGetValue(entry.LocId, out var gpgsId))
                    SendAndroidProgress(entry.LocId, gpgsId, 0, entry.Current, entry.Completed);
            });

        /// <summary>Sets the steps of / unlocks the mapped Google Play Games achievement, or queues it while signed out.</summary>
        /// <param name="locId">Game-defined achievement key, looked up in <see cref="_androidMap"/> to find the GPGS achievement ID.</param>
        /// <param name="delta">Increment used only when <paramref name="current"/> is not known (0 or negative).</param>
        /// <param name="current">Absolute progress, sent with the idempotent <c>SetStepsAtLeast</c> so it can be re-sent after a later sign-in. Must use the same unit as the achievement's steps in the Play Console.</param>
        /// <param name="completed">If true, unlocks the achievement outright regardless of its counter state.</param>
        private static void ReportAndroid(string locId, int delta, int current, bool completed)
        {
            if (locId == null) { OnLog?.Invoke("[NativeSocial] ReportAndroid skipped: locId is null."); return; }
            if (!_androidMap.TryGetValue(locId, out var gpgsId)) { OnLog?.Invoke($"[NativeSocial] ReportAndroid('{locId}') skipped: no GPGS achievement id mapped for this LocID."); return; }

            if (!IsAuthenticated)
            {
                QueuePendingAndroidProgress(locId, current, completed);
                return;
            }

            SendAndroidProgress(locId, gpgsId, delta, current, completed);
        }

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
                    PlayGamesPlatform.Instance.UnlockAchievement(gpgsId, ok => LogGpgsCallback("UnlockAchievement", gpgsId, locId, ok));
            }
        }
    }
}
#endif
