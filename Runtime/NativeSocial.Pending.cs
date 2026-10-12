using System;
using System.Collections.Generic;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Wagenheimer.NativeSocial.EditorTests")]

namespace Wagenheimer.NativeSocial
{
    /// <summary>
    /// Progress reported while the platform can't take it (Google Play Games / Game Center signed out) is kept
    /// here, in PlayerPrefs so it survives restarts, and delivered after the next successful sign-in. Only the
    /// highest value per achievement is kept: every platform call used to deliver it is absolute
    /// (SetStepsAtLeast / percentage), so re-sending never double counts.
    /// </summary>
    public static partial class NativeSocial
    {
        private const string PendingProgressPrefsKey = "NativeSocial.PendingProgress";

        private static Dictionary<string, PendingProgress> _pendingProgress;

        [Serializable]
        internal class PendingProgress
        {
            public string LocId;
            public int Current;
            public int Total;
            public bool Completed;
        }

        [Serializable]
        internal class PendingProgressList
        {
            public List<PendingProgress> Items = new List<PendingProgress>();
        }

        /// <summary>
        /// Whether a <see cref="Report"/> made right now reaches the platform or is kept to be delivered later.
        /// False only when it would be dropped (Steam before SteamAPI is ready), so callers that track "already
        /// sent" values should wait instead of marking them as sent.
        /// </summary>
        public static bool IsReadyToReport
        {
            get
            {
#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
                return SteamReady;
#else
                return true;
#endif
            }
        }

        /// <summary>Number of achievement updates waiting for the next sign-in (diagnostics).</summary>
        public static int PendingProgressCount => LoadPendingProgress().Count;

        /// <summary>Folds a new report into the pending entry, keeping the highest progress. Returns whether anything changed.</summary>
        internal static bool MergePendingProgress(PendingProgress entry, int current, int total, bool completed)
        {
            var changed = false;
            if (current > entry.Current) { entry.Current = current; changed = true; }
            if (total > 0 && total != entry.Total) { entry.Total = total; changed = true; }
            if (completed && !entry.Completed) { entry.Completed = true; changed = true; }
            return changed;
        }

        internal static string SerializePendingProgress(IEnumerable<PendingProgress> items) =>
            JsonUtility.ToJson(new PendingProgressList { Items = new List<PendingProgress>(items) });

        internal static Dictionary<string, PendingProgress> DeserializePendingProgress(string json)
        {
            var result = new Dictionary<string, PendingProgress>();
            if (string.IsNullOrEmpty(json)) return result;

            var list = JsonUtility.FromJson<PendingProgressList>(json);
            if (list?.Items == null) return result;
            foreach (var item in list.Items)
                if (!string.IsNullOrEmpty(item.LocId)) result[item.LocId] = item;
            return result;
        }

        private static void QueuePendingProgress(string locId, int current, int total, bool completed, string platform)
        {
            var pending = LoadPendingProgress();
            if (!pending.TryGetValue(locId, out var entry))
            {
                entry = new PendingProgress { LocId = locId };
                pending[locId] = entry;
            }

            if (!MergePendingProgress(entry, current, total, completed)) return;

            SavePendingProgress(pending);
            OnLog?.Invoke($"[NativeSocial] {platform} not signed in: queued '{locId}' ({entry.Current}/{entry.Total}, completed={entry.Completed}) to send after sign-in.");
        }

        /// <summary>Hands every queued entry to <paramref name="send"/> and clears the queue.</summary>
        private static void FlushPendingProgress(string platform, Action<PendingProgress> send)
        {
            var pending = LoadPendingProgress();
            if (pending.Count == 0) return;

            Debug.Log($"[NativeSocial] Sending {pending.Count} achievement update(s) queued while signed out of {platform}.");
            foreach (var entry in pending.Values)
                send(entry);

            pending.Clear();
            SavePendingProgress(pending);
        }

        private static Dictionary<string, PendingProgress> LoadPendingProgress()
        {
            if (_pendingProgress != null) return _pendingProgress;

            try
            {
                _pendingProgress = DeserializePendingProgress(PlayerPrefs.GetString(PendingProgressPrefsKey, string.Empty));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NativeSocial] Discarding unreadable queued achievement progress: " + ex.Message);
                _pendingProgress = new Dictionary<string, PendingProgress>();
            }
            return _pendingProgress;
        }

        private static void SavePendingProgress(Dictionary<string, PendingProgress> pending)
        {
            if (pending.Count == 0)
                PlayerPrefs.DeleteKey(PendingProgressPrefsKey);
            else
                PlayerPrefs.SetString(PendingProgressPrefsKey, SerializePendingProgress(pending.Values));
            PlayerPrefs.Save();
        }
    }
}
