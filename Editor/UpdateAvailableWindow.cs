using UnityEditor;
using UnityEngine;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Modal-ish utility window shown by <see cref="UpdateChecker"/> when a newer package
    /// version is available on GitHub. Lets the user update via UPM, view the full
    /// changelog, snooze, or permanently ignore that specific version.
    /// </summary>
    internal class UpdateAvailableWindow : EditorWindow
    {
        private static readonly string RepoUrl = "https://github.com/wagenheimer/UnityNativeSocial";
        private static readonly string GitUrl = "https://github.com/wagenheimer/UnityNativeSocial.git";
        private const string SkipVersionKey = "Wagenheimer_NativeSocial_SkipVersion";

        private Version _local;
        private Version _latest;
        private string _changelogNotes;
        private Vector2 _scroll;

        // In-flight UPM "add" request, polled from EditorApplication.update instead of
        // blocked on synchronously — Client.Add() is asynchronous, and busy-waiting on
        // request.IsCompleted on the main thread would freeze the entire editor UI.
        private AddRequest _addRequest;
        private bool _updating;
        private string _updateError;

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

            if (!string.IsNullOrEmpty(_updateError))
                EditorGUILayout.HelpBox(_updateError, MessageType.Error);

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(_updating))
            {
                if (GUILayout.Button(_updating ? "Updating..." : "Update Now", GUILayout.Height(32)))
                    StartUpdate();
            }

            if (GUILayout.Button("View Full Changelog", GUILayout.Height(32)))
                Application.OpenURL($"{RepoUrl}/releases/tag/v{_latest}");

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Remind Later"))
                Close();

            if (GUILayout.Button("Ignore This Version"))
            {
                EditorPrefs.SetString(SkipVersionKey, _latest.ToString());
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void StartUpdate()
        {
            _updating = true;
            _updateError = null;
            _addRequest = Client.Add(GitUrl);
            EditorApplication.update += PollUpdate;
        }

        private void PollUpdate()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= PollUpdate;

            // The window may have been closed while the request was in flight.
            if (this == null)
                return;

            _updating = false;

            if (_addRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[NativeSocial] Updated to {_addRequest.Result.version}");
                Close();
            }
            else
            {
                _updateError = _addRequest.Error?.message ?? "Unknown error while updating.";
                Debug.LogError($"[NativeSocial] Update failed: {_updateError}");
                Repaint();
            }
        }

        private void OnDestroy()
        {
            EditorApplication.update -= PollUpdate;
        }
    }
}
