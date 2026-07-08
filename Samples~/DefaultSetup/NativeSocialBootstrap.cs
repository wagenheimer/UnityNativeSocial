using UnityEngine;
using Wagenheimer.NativeSocial;

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

    [System.Serializable]
    public class AchievementMapEntry
    {
        public string LocID;
        public string PlatformID;
    }

    [System.Serializable]
    public class SteamAchievementMapEntry
    {
        public string LocID;
        public string StatName;
        public string AchievementName;
    }
}
