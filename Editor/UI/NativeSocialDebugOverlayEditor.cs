using UnityEditor;
using UnityEngine;
using Wagenheimer.NativeSocial.UI;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    public static class NativeSocialDebugOverlayEditor
    {
        [MenuItem("Tools/Wagenheimer/Native Social/Add Native Social Debug Overlay to Scene", priority = 20)]
        public static void AddDebugOverlayToScene()
        {
            var existing = Object.FindFirstObjectByType<NativeSocialDebugOverlay>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.Log("[NativeSocial] NativeSocialDebugOverlay already exists in the current scene.");
                return;
            }

            var go = new GameObject("[NativeSocialDebugOverlay]");
            go.AddComponent<NativeSocialDebugOverlay>();
            Undo.RegisterCreatedObjectUndo(go, "Create Native Social Debug Overlay");
            Selection.activeGameObject = go;
            Debug.Log("[NativeSocial] Added NativeSocialDebugOverlay to active scene.");
        }
    }
}
