using UnityEngine;
using Wagenheimer.NativeSocial;

/// <summary>
/// Bootstrap component for NativeSocial.
/// Automatically binds to an AchievementTierMap asset and calls NativeSocial.Initialize at startup.
/// </summary>
public class NativeSocialBootstrap : MonoBehaviour
{
    [Tooltip("The AchievementTierMap containing all trophy definitions and platform IDs. If left empty, will try loading from Resources 'Social/AchievementTierMap' or finding any map asset.")]
    [SerializeField] private AchievementTierMap tierMap;

    [Tooltip("Whether to mark this GameObject as persistent across scene loads.")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    private void Awake()
    {
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        if (tierMap == null)
        {
            tierMap = Resources.Load<AchievementTierMap>("Social/AchievementTierMap");
            if (tierMap == null)
            {
                var maps = Resources.FindObjectsOfTypeAll<AchievementTierMap>();
                if (maps != null && maps.Length > 0) tierMap = maps[0];
            }
        }

        if (tierMap != null)
        {
            NativeSocial.Initialize(
                androidMap: tierMap.BuildAndroidMap(),
                iosMap: tierMap.BuildIosMap(),
                steamMap: tierMap.BuildSteamMap()
            );
            Debug.Log($"[NativeSocial] Initialized from '{tierMap.name}' ({tierMap.Entries.Count} entries).");
        }
        else
        {
            NativeSocial.Initialize();
            Debug.LogWarning("[NativeSocial] Initialized with empty maps because no AchievementTierMap was found.");
        }
    }
}
