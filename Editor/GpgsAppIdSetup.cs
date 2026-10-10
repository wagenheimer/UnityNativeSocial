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
        internal static bool Apply(string appId, string webClientId, out string message)
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

            // A blank Web client id passes null so a value already stored by the plugin is kept.
            var clientId = string.IsNullOrWhiteSpace(webClientId) ? null : webClientId.Trim();
            var ok = (bool)method.Invoke(null, new object[] { clientId, appId, null });
            message = ok
                ? $"Application id {appId} saved and the Android manifest regenerated."
                : "The plugin's setup reported a problem (see its dialog / the Console). A common one is a missing Android SDK: install Android Build Support in Unity Hub.";
            return ok;
        }
    }

    internal sealed class GpgsAppIdWindow : EditorWindow
    {
        private string _appId = string.Empty;
        private string _webClientId = string.Empty;
        private string _resourcesXml = string.Empty;
        private Vector2 _xmlScroll;
        private Vector2 _scroll;
        private string _status;
        private MessageType _statusType = MessageType.None;

        internal static void Open()
        {
            var window = GetWindow<GpgsAppIdWindow>(true, "Google Play Games Setup");
            window.minSize = new Vector2(560, 520);
            window._appId = GpgsAppIdSetup.SuggestAppId() ?? string.Empty;
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Google's \"Android setup\" window is only enabled while the active build target is Android. This does the same job from any platform: " +
                "it stores the application id (and the optional Web client id) and regenerates the Play Games Android manifest, so Android sign-in can work. " +
                "You do NOT need Google's constants class (GPGSIds): NativeSocial reads the achievement ids from the Tier Map.",
                MessageType.Info);

            DrawAppId();
            DrawResourcesXml();
            DrawWebClientId();
            DrawApply();

            EditorGUILayout.EndScrollView();
        }

        private void DrawAppId()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("1. Application ID", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The numeric project number in Play Console > Play Games Services > Configuration (for example 123456789012).", EditorStyles.wordWrappedMiniLabel);
            _appId = EditorGUILayout.TextField("Application ID", _appId);

            var suggestion = GpgsAppIdSetup.SuggestAppId();
            if (!string.IsNullOrEmpty(suggestion) && suggestion != _appId)
            {
                EditorGUILayout.HelpBox($"Your achievement ids contain the application id {suggestion}. Confirm it in Play Console before applying.", MessageType.None);
                if (GUILayout.Button("Use " + suggestion, GUILayout.Width(180)))
                    _appId = suggestion;
            }
        }

        private void DrawResourcesXml()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. Android resources XML (optional, but it verifies everything)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Play Console > Play Games Services > Configuration > Achievements > Get resources > Android. Paste it here to fill the application id and to check " +
                "the package name and every achievement id of the Tier Map against what Google really has. Nothing is generated from it.",
                EditorStyles.wordWrappedMiniLabel);

            _xmlScroll = EditorGUILayout.BeginScrollView(_xmlScroll, GUILayout.Height(110));
            _resourcesXml = EditorGUILayout.TextArea(_resourcesXml, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (string.IsNullOrWhiteSpace(_resourcesXml)) return;

            if (!GpgsResourcesXml.TryParse(_resourcesXml, out var xml, out var error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
                return;
            }

            if (xml.AppId != _appId && GUILayout.Button($"Use application id {xml.AppId} from the XML", GUILayout.Width(280)))
                _appId = xml.AppId;

            EditorGUILayout.HelpBox($"XML: application id {xml.AppId}, package {xml.PackageName ?? "(none)"}, {xml.Achievements.Count} achievements.", MessageType.None);

            if (xml.AppId != _appId)
                EditorGUILayout.HelpBox($"The XML application id ({xml.AppId}) is different from the one above ({_appId}).", MessageType.Warning);

            var androidId = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            if (!string.IsNullOrEmpty(xml.PackageName) && xml.PackageName != androidId)
                EditorGUILayout.HelpBox(
                    $"The XML was configured for package {xml.PackageName} but Player Settings > Android uses {androidId}. Google Play Games only signs in builds with the package registered in Play Console.",
                    MessageType.Warning);

            DrawMapComparison(xml);
        }

        private static void DrawMapComparison(GpgsResourcesXml xml)
        {
            var mapIds = AssetDatabase.FindAssets("t:AchievementTierMap")
                .Select(g => AssetDatabase.LoadAssetAtPath<AchievementTierMap>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null)
                .SelectMany(m => m.Entries.Select(e => e.GooglePlayId))
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

            if (mapIds.Count == 0)
            {
                EditorGUILayout.HelpBox("No Google Play ids in a Tier Map to compare with yet.", MessageType.None);
                return;
            }

            var missing = xml.MissingFromXml(mapIds);
            var unused = xml.UnusedByMap(mapIds);
            if (missing.Count == 0 && unused.Count == 0)
            {
                EditorGUILayout.HelpBox($"All {mapIds.Count} Google Play ids of the Tier Map match the XML.", MessageType.Info);
                return;
            }

            if (missing.Count > 0)
                EditorGUILayout.HelpBox($"{missing.Count} id(s) in the Tier Map are NOT in the XML (they will not unlock on Android):\n" + string.Join("\n", missing.Take(8)) +
                                        (missing.Count > 8 ? "\n..." : string.Empty), MessageType.Warning);
            if (unused.Count > 0)
                EditorGUILayout.HelpBox($"{unused.Count} achievement(s) in Play Console are not used by the Tier Map:\n" + string.Join("\n", unused.Take(8)) +
                                        (unused.Count > 8 ? "\n..." : string.Empty), MessageType.None);
        }

        private void DrawWebClientId()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("3. Web client ID (optional)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Only needed to read the player's ID token or a server auth code (NativeSocial.GetServerAuthCode) for your own backend. Not required for sign-in or achievements; leave it empty otherwise. " +
                "Create an OAuth client of type \"Web application\" in the same Google Cloud project and paste its id.",
                EditorStyles.wordWrappedMiniLabel);
            _webClientId = EditorGUILayout.TextField("Client ID", _webClientId);

            var problem = GpgsResourcesXml.ValidateWebClientId(_webClientId, _appId);
            if (problem != null)
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        private void DrawApply()
        {
            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(GpgsResourcesXml.ValidateWebClientId(_webClientId, _appId) != null))
            {
                if (GUILayout.Button("Apply", GUILayout.Height(28)))
                {
                    var ok = GpgsAppIdSetup.Apply(_appId, _webClientId, out var message);
                    _status = message;
                    _statusType = ok ? MessageType.Info : MessageType.Warning;
                }
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);
        }
    }
}
