using UnityEngine;
using Wagenheimer.NativeSocial;

/// <summary>
/// Sample bootstrap component: builds the per-platform achievement ID maps from
/// Inspector-configured entries and calls <see cref="NativeSocial.Initialize"/> once at startup.
/// Copy this into your project and adapt it, or use it as a reference for your own bootstrap code.
/// </summary>
public class NativeSocialBootstrap : MonoBehaviour
{
    [Header("Android (Google Play Games)")]
    [SerializeField] private AchievementMapEntry[] androidAchievements;

    [Header("iOS (Game Center)")]
    [SerializeField] private AchievementMapEntry[] iosAchievements;

    [Header("Steam")]
    [SerializeField] private SteamAchievementMapEntry[] steamAchievements;

    private void Awake()
    {
        // Convert the Inspector-friendly array entries into the dictionaries NativeSocial.Initialize expects.
        var androidMap = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var e in androidAchievements)
            if (!string.IsNullOrEmpty(e.LocID) && !string.IsNullOrEmpty(e.PlatformID))
                androidMap[e.LocID] = e.PlatformID;

        var iosMap = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var e in iosAchievements)
            if (!string.IsNullOrEmpty(e.LocID) && !string.IsNullOrEmpty(e.PlatformID))
                iosMap[e.LocID] = e.PlatformID;

        var steamMap = new System.Collections.Generic.Dictionary<string, SteamEntry>();
        foreach (var e in steamAchievements)
            if (!string.IsNullOrEmpty(e.LocID))
                steamMap[e.LocID] = new SteamEntry(e.StatName, e.AchievementName);

        NativeSocial.Initialize(androidMap, iosMap, steamMap);
        Debug.Log("[NativeSocial] Initialized with achievement maps.");
    }

    /// <summary>Maps a game-defined LocID to a platform-specific achievement ID (Android/iOS).</summary>
    [System.Serializable]
    public class AchievementMapEntry
    {
        public string LocID;
        public string PlatformID;
    }

    /// <summary>Maps a game-defined LocID to a Steam stat name and achievement API name.</summary>
    [System.Serializable]
    public class SteamAchievementMapEntry
    {
        public string LocID;
        public string StatName;
        public string AchievementName;
    }
}
