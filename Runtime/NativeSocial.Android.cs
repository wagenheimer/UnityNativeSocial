#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using UnityEngine;

namespace Wagenheimer.NativeSocial
{
    /// <summary>
    /// Google Play Games plumbing shared by the public API in <c>NativeSocial.cs</c>: one-time activation,
    /// sign-in bookkeeping, progress that survives being signed out, and re-checking sign-in when the game
    /// comes back to the foreground.
    /// </summary>
    public static partial class NativeSocial
    {
        private const string PendingProgressPrefsKey = "NativeSocial.GpgsPendingProgress";
        private const string ManualSignInDeclinedPrefsKey = "NativeSocial.GpgsManualSignInDeclined";

        private static bool _gpgsActivated;
        private static bool _signInInProgress;
        private static Dictionary<string, PendingProgress> _pendingProgress;
        private static readonly HashSet<string> _reportedGpgsFailures = new HashSet<string>();

        [Serializable]
        private class PendingProgress
        {
            public string LocId;
            public int Steps;
            public bool Completed;
        }

        [Serializable]
        private class PendingProgressList
        {
            public List<PendingProgress> Items = new List<PendingProgress>();
        }

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

        /// <summary>Keeps the highest progress seen while signed out (in PlayerPrefs, so it survives restarts) until the next sign-in.</summary>
        private static void QueuePendingAndroidProgress(string locId, int steps, bool completed)
        {
            var pending = LoadPendingProgress();
            if (!pending.TryGetValue(locId, out var entry))
            {
                entry = new PendingProgress { LocId = locId };
                pending[locId] = entry;
            }

            var changed = steps > entry.Steps || (completed && !entry.Completed);
            if (!changed) return;

            entry.Steps = Math.Max(entry.Steps, steps);
            entry.Completed |= completed;
            SavePendingProgress(pending);
            OnLog?.Invoke($"[NativeSocial] GPGS not signed in: queued '{locId}' (steps={entry.Steps}, completed={entry.Completed}) to send after sign-in.");
        }

        private static void FlushPendingAndroidProgress()
        {
            var pending = LoadPendingProgress();
            if (pending.Count == 0) return;

            Debug.Log($"[NativeSocial] Sending {pending.Count} achievement update(s) queued while signed out of Google Play Games.");
            foreach (var entry in pending.Values)
            {
                if (_androidMap != null && _androidMap.TryGetValue(entry.LocId, out var gpgsId))
                    SendAndroidProgress(entry.LocId, gpgsId, 0, entry.Steps, entry.Completed);
            }

            pending.Clear();
            SavePendingProgress(pending);
        }

        private static Dictionary<string, PendingProgress> LoadPendingProgress()
        {
            if (_pendingProgress != null) return _pendingProgress;

            _pendingProgress = new Dictionary<string, PendingProgress>();
            var json = PlayerPrefs.GetString(PendingProgressPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return _pendingProgress;

            try
            {
                var list = JsonUtility.FromJson<PendingProgressList>(json);
                if (list?.Items != null)
                    foreach (var item in list.Items)
                        if (!string.IsNullOrEmpty(item.LocId)) _pendingProgress[item.LocId] = item;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NativeSocial] Discarding unreadable queued GPGS progress: " + ex.Message);
            }
            return _pendingProgress;
        }

        private static void SavePendingProgress(Dictionary<string, PendingProgress> pending)
        {
            if (pending.Count == 0)
                PlayerPrefs.DeleteKey(PendingProgressPrefsKey);
            else
                PlayerPrefs.SetString(PendingProgressPrefsKey, JsonUtility.ToJson(new PendingProgressList { Items = new List<PendingProgress>(pending.Values) }));
            PlayerPrefs.Save();
        }
    }
}
#endif
