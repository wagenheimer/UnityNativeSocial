using UnityEditor;
using UnityEngine;
using UnityEditor.PackageManager;
using System;

namespace Wagenheimer.NativeSocial.Editor
{
    internal class UpdateAvailableWindow : EditorWindow
    {
        private static string _repoUrl = "https://github.com/wagenheimer/UnityNativeSocial";
        private static string _gitUrl = "https://github.com/wagenheimer/UnityNativeSocial.git";
        private Version _local;
        private Version _latest;
        private string _changelogNotes;
        private Vector2 _scroll;

        public static void Show(Version local, Version latest, string changelogNotes)
        {
            var w = CreateInstance<UpdateAvailableWindow>();
            w.titleContent = new GUIContent("Native Social Update");
            w._local = local;
            w._latest = latest;
            w._changelogNotes = changelogNotes;
            w.minSize = new Vector2(420, 320);
            w.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Update Available", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"Current version:  {_local}");
            EditorGUILayout.LabelField($"Latest version:   {_latest}");
            EditorGUILayout.Space();

            if (!string.IsNullOrEmpty(_changelogNotes))
            {
                EditorGUILayout.LabelField("Release Notes:", EditorStyles.boldLabel);
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(120));
                EditorGUILayout.HelpBox(_changelogNotes, MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space();
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Update Now", GUILayout.Height(32)))
            {
                var request = Client.Add(_gitUrl);
                while (!request.IsCompleted) { }
                if (request.Status == StatusCode.Success)
                    Debug.Log($"[NativeSocial] Updated to {_latest}");
                else
                    Debug.LogError($"[NativeSocial] Update failed: {request.Error?.message}");
                Close();
            }

            if (GUILayout.Button("View Full Changelog", GUILayout.Height(32)))
                Application.OpenURL($"{_repoUrl}/releases/tag/v{_latest}");

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Remind Later"))
                Close();

            if (GUILayout.Button("Ignore This Version"))
            {
                EditorPrefs.SetString("Wagenheimer_NativeSocial_SkipVersion", _latest.ToString());
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
