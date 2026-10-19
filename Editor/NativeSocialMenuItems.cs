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

        [MenuItem("Tools/Wagenheimer/Native Social/Create Delete Account UI Prefab", priority = 35)]
        private static void CreateDeleteAccountUIPrefab()
        {
            var type = System.Type.GetType("Wagenheimer.CloudSave.Editor.CloudSaveUIPrefabGenerator, Wagenheimer.CloudSave.Editor");
            if (type != null)
            {
                var method = type.GetMethod("GenerateAccountDeletionUI", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, null);
            }
            else
            {
                EditorUtility.DisplayDialog("Native Social", "UnityCloudSave package is required for UGS Account Deletion.", "OK");
            }
        }

        [MenuItem("Tools/Wagenheimer/Native Social/Report Issue", priority = 50)]
        private static void ReportIssue()
        {
            Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial/issues/new");
        }
    }
}
