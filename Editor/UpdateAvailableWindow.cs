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
        const string PackageDisplayName = "Native Social";
        static readonly Color AccentColor = new Color(0.24f, 0.48f, 0.95f);

        private static readonly string RepoUrl = "https://github.com/wagenheimer/UnityNativeSocial";
        private static readonly string GitUrl = "https://github.com/wagenheimer/UnityNativeSocial.git";
        private const string SkipVersionKey = "Wagenheimer_NativeSocial_SkipVersion";

        private Version _local;
        private Version _latest;
        private string _changelogNotes;
        private Vector2 _notesScroll;

        // In-flight UPM "add" request, polled from EditorApplication.update instead of
        // blocked on synchronously — Client.Add() is asynchronous, and busy-waiting on
        // request.IsCompleted on the main thread would freeze the entire editor UI.
        private AddRequest _addRequest;
        private bool _updating;
        private string _updateError;

        Texture2D _headerTex;
        Texture2D _dividerTex;
        GUIStyle _headerTitleStyle;
        GUIStyle _headerSubtitleStyle;
        GUIStyle _arrowStyle;
        GUIStyle _primaryButtonStyle;
        GUIStyle _footerStyle;
        bool _stylesBuilt;

        public static void Show(Version local, Version latest, string changelogNotes)
        {
            var w = CreateInstance<UpdateAvailableWindow>();
            w.titleContent = new GUIContent($"{PackageDisplayName} — Update");
            w._local = local;
            w._latest = latest;
            w._changelogNotes = string.IsNullOrEmpty(changelogNotes)
                ? "No release notes available — see the full changelog on GitHub."
                : changelogNotes;

            var size = new Vector2(460, 420);
            w.minSize = size;
            w.maxSize = new Vector2(640, 760);
            w.ShowUtility();
        }

        void BuildStyles()
        {
            if (_stylesBuilt)
                return;
            _stylesBuilt = true;

            var dark = EditorGUIUtility.isProSkin;
            var headerBg = dark ? new Color(0.17f, 0.17f, 0.19f) : new Color(0.90f, 0.92f, 0.97f);
            var dividerColor = dark ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.10f);

            _headerTex = MakeTex(headerBg);
            _dividerTex = MakeTex(dividerColor);

            _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                normal = { textColor = dark ? Color.white : new Color(0.12f, 0.12f, 0.14f) }
            };

            _headerSubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = dark ? new Color(1f, 1f, 1f, 0.6f) : new Color(0f, 0f, 0f, 0.55f) }
            };

            _arrowStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = AccentColor }
            };

            _primaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold
            };

            _footerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = dark ? new Color(1f, 1f, 1f, 0.4f) : new Color(0f, 0f, 0f, 0.4f) }
            };
        }

        static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            BuildStyles();

            var headerRect = GUILayoutUtility.GetRect(position.width, 56);
            GUI.DrawTexture(headerRect, _headerTex);
            var innerHeader = new Rect(headerRect.x + 16, headerRect.y + 8, headerRect.width - 32, headerRect.height - 12);
            GUI.BeginGroup(innerHeader);
            GUI.Label(new Rect(0, 0, innerHeader.width, 20), "New version available", _headerTitleStyle);
            GUI.Label(new Rect(0, 20, innerHeader.width, 16), PackageDisplayName, _headerSubtitleStyle);
            GUI.EndGroup();

            DrawDivider();
            GUILayout.Space(12);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_local.ToString(), EditorStyles.label);
                GUILayout.Label("→", _arrowStyle, GUILayout.Width(22));
                GUILayout.Label(_latest.ToString(), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(14);

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("What's new in this version", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            _notesScroll = EditorGUILayout.BeginScrollView(_notesScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField(_changelogNotes, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndScrollView();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_updateError))
            {
                GUILayout.Space(6);
                EditorGUILayout.HelpBox(_updateError, MessageType.Error);
            }

            GUILayout.Space(10);
            DrawDivider();
            GUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = AccentColor;
                using (new EditorGUI.DisabledScope(_updating))
                {
                    if (GUILayout.Button(_updating ? "Updating..." : "Update Now", _primaryButtonStyle, GUILayout.Height(30), GUILayout.MinWidth(140)))
                        StartUpdate();
                }
                GUI.backgroundColor = prevColor;

                if (GUILayout.Button("View Changelog", GUILayout.Height(30)))
                    Application.OpenURL($"{RepoUrl}/releases/tag/v{_latest}");
                GUILayout.Space(10);
            }

            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                if (GUILayout.Button("Remind me later", EditorStyles.miniButton))
                    Close();

                if (GUILayout.Button("Skip this version", EditorStyles.miniButton))
                {
                    EditorPrefs.SetString(SkipVersionKey, _latest.ToString());
                    Close();
                }
                GUILayout.Space(10);
            }

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.Label(RepoUrl, _footerStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        void DrawDivider()
        {
            var rect = GUILayoutUtility.GetRect(position.width, 1, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, _dividerTex);
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
            if (_headerTex != null) DestroyImmediate(_headerTex);
            if (_dividerTex != null) DestroyImmediate(_dividerTex);
        }
    }
}
