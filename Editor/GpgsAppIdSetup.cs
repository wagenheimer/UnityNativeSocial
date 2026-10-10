using System;
using System.Linq;
using System.Reflection;

using UnityEditor;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Sets the Google Play Games application id without the plugin's own "Android setup" window.
    /// That window is enabled only while the active build target is Android (<c>#if UNITY_ANDROID</c> in its menu validation), which is why the menu
    /// item looks greyed out when you work on another platform. The plugin exposes the same step as a public static method
    /// (<c>GPGSAndroidSetupUI.PerformSetup(webClientId, appId, nearbyServiceId)</c>, documented for automated builds); this calls it and regenerates
    /// <c>Assets/Plugins/Android/GooglePlayGamesManifest.androidlib/AndroidManifest.xml</c> with the application id.
    /// </summary>
    internal static class GpgsAppIdSetup
    {
        private const string SetupTypeName = "GooglePlayGames.Editor.GPGSAndroidSetupUI";
        private const string SetupMethodName = "PerformSetup";

        [MenuItem("Tools/Wagenheimer/Native Social/Set Google Play Games Application ID...", priority = 3)]
        internal static void OpenWindow() => GpgsAppIdWindow.Open();

        /// <summary>
        /// Google Play achievement ids are small protobuf messages (Base64URL); the first field is the Play Games application id (project number).
        /// Returns it, or null when the text is not such an id.
        /// </summary>
        internal static string DecodeAppId(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId)) return null;

            try
            {
                var base64 = achievementId.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                var bytes = Convert.FromBase64String(base64);

                // 0x0A <length> 0x08 <varint application id> ...
                if (bytes.Length < 4 || bytes[0] != 0x0A || bytes[2] != 0x08) return null;

                ulong value = 0;
                var shift = 0;
                for (var i = 3; i < bytes.Length && shift < 64; i++)
                {
                    value |= (ulong)(bytes[i] & 0x7F) << shift;
                    if ((bytes[i] & 0x80) == 0) return value.ToString();
                    shift += 7;
                }
            }
            catch (FormatException)
            {
                return null;
            }

            return null;
        }

        /// <summary>The application id embedded in the first Google Play achievement id of the project's tier maps, or null.</summary>
        internal static string SuggestAppId()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AchievementTierMap"))
            {
                var map = AssetDatabase.LoadAssetAtPath<AchievementTierMap>(AssetDatabase.GUIDToAssetPath(guid));
                if (map == null) continue;

                var id = map.Entries.Select(e => e.GooglePlayId).FirstOrDefault(s => !string.IsNullOrEmpty(s));
                var decoded = DecodeAppId(id);
                if (decoded != null) return decoded;
            }

            return null;
        }

        /// <summary>Runs the plugin's own setup step with the given application id. Returns false (with a message) when it could not.</summary>
        internal static bool Apply(string appId, out string message)
        {
            appId = (appId ?? string.Empty).Trim();
            if (appId.Length < 5 || !appId.All(char.IsDigit))
            {
                message = "The application id is the numeric project number shown in Play Console (Play Games Services > Configuration), for example 123456789012.";
                return false;
            }

            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(SetupTypeName, false))
                .FirstOrDefault(t => t != null);
            var method = type?.GetMethod(SetupMethodName, BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string), typeof(string) }, null);

            if (method == null)
            {
                message = "The Google Play Games plugin's editor setup was not found. Install the plugin (Package Manager: com.google.play.games) and try again.";
                return false;
            }

            var ok = (bool)method.Invoke(null, new object[] { null, appId, null });
            message = ok
                ? $"Application id {appId} saved and the Android manifest regenerated."
                : "The plugin's setup reported a problem (see its dialog / the Console). A common one is a missing Android SDK: install Android Build Support in Unity Hub.";
            return ok;
        }
    }

    internal sealed class GpgsAppIdWindow : EditorWindow
    {
        private string _appId = string.Empty;
        private string _status;
        private MessageType _statusType = MessageType.None;

        internal static void Open()
        {
            var window = GetWindow<GpgsAppIdWindow>(true, "Google Play Games Application ID");
            window.minSize = new Vector2(520, 260);
            window._appId = GpgsAppIdSetup.SuggestAppId() ?? string.Empty;
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Google's \"Android setup\" window is only enabled while the active build target is Android. This does the same step from any platform: " +
                "it stores the application id and regenerates the Play Games Android manifest, so Android sign-in can work.",
                MessageType.Info);

            EditorGUILayout.Space(6);
            _appId = EditorGUILayout.TextField("Application ID", _appId);

            var suggestion = GpgsAppIdSetup.SuggestAppId();
            if (!string.IsNullOrEmpty(suggestion))
            {
                EditorGUILayout.HelpBox(
                    $"Your achievement ids contain the application id {suggestion}. Confirm it in Play Console (Play Games Services > Configuration) before applying.",
                    MessageType.None);
                if (GUILayout.Button("Use " + suggestion, GUILayout.Width(180)))
                    _appId = suggestion;
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Apply", GUILayout.Height(28)))
            {
                var ok = GpgsAppIdSetup.Apply(_appId, out var message);
                _status = message;
                _statusType = ok ? MessageType.Info : MessageType.Warning;
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);
        }
    }
}
