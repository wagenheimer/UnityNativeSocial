using UnityEditor;
using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>Convenience shortcuts under Tools/Wagenheimer/Native Social for docs and issue reporting.</summary>
    internal static class NativeSocialMenuItems
    {
        [MenuItem("Tools/Wagenheimer/Native Social/Integration Guide", priority = 30)]
        private static void OpenIntegrationGuide()
        {
            Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial#readme");
        }

        [MenuItem("Tools/Wagenheimer/Native Social/Report Issue", priority = 50)]
        private static void ReportIssue()
        {
            Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial/issues/new");
        }
    }
}
