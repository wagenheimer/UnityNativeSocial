using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>
    /// Play Games Android setup as a Dashboard tab: application id, the optional Android resources XML and the optional
    /// Web client id. Rebuilt in UI Toolkit so it matches the rest of the package (the old IMGUI window is gone), with the
    /// validation that used to live only behind the Apply button made visible section by section.
    ///
    /// Nothing here duplicates Google's logic: <see cref="GpgsAppIdSetup.Apply"/> still calls the plugin's own
    /// <c>GPGSAndroidSetupUI.PerformSetup(webClientId, appId, nearbyServiceId)</c>, so the manifest is regenerated exactly
    /// as before. The only additions are read-only helpers (current state) and the Cloud Console shortcuts.
    /// </summary>
    internal sealed class NativeSocialGpgsSetupView
    {
        private const string ManifestPath = "Assets/Plugins/Android/GooglePlayGamesManifest.androidlib/AndroidManifest.xml";

        public VisualElement Root { get; }

        private string _appId;
        private string _webClientId;
        private string _resourcesXml = string.Empty;

        private TextField _appIdField;
        private TextField _webClientField;
        private TextField _xmlField;

        private VisualElement _appIdFeedback;
        private VisualElement _webClientFeedback;
        private VisualElement _xmlFeedback;
        private VisualElement _stateBody;
        private VisualElement _statusArea;
        private Button _applyButton;

        // Expensive to query (AssetDatabase / PackageManager); the setup tab does not change them.
        private List<string> _mapIds;
        private string _pluginVersion;

        public NativeSocialGpgsSetupView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);

            _mapIds = LoadMapIds();
            _pluginVersion = GpgsInstaller.GetInstalledVersion();

            // The manifest is the source of truth once setup ran; the achievement ids are the fallback on a fresh project.
            _appId = ReadManifestAppId() ?? GpgsAppIdSetup.SuggestAppId() ?? string.Empty;
            _webClientId = GpgsAppIdSetup.ReadWebClientId() ?? string.Empty;

            Root.Add(CreateHeaderCard());
            Root.Add(CreateStateCard());
            Root.Add(CreateApplicationIdCard());
            Root.Add(CreateResourcesXmlCard());
            Root.Add(CreateWebClientCard());
            Root.Add(CreateApplyCard());

            RefreshAll();
        }

        // ── Cards ─────────────────────────────────────────────────────────────────

        private VisualElement CreateHeaderCard()
        {
            var card = NativeSocialUIStyle.CreateCard("🎮 Play Games Android Setup",
                "Everything Android sign-in needs, in one place. Applying writes the same configuration as Google's \"Android setup\" window " +
                "(application id, optional Web client id) and regenerates the Play Games Android manifest.");

            var row = Row();
            row.Add(NativeSocialUIStyle.CreateButton("▶ Verify Setup", () => NativeSocialDashboardWindow.Open(NativeSocialDashboardWindow.Tab.SetupAudit), primary: true));
            row.Add(NativeSocialUIStyle.CreateButton("↗ Open Play Console", () => Application.OpenURL("https://play.google.com/console")));
            row.Add(NativeSocialUIStyle.CreateButton("↗ Google Cloud credentials", () => OpenCloud("https://console.cloud.google.com/apis/credentials")));
            card.Add(row);

            card.Add(NativeSocialUIStyle.CreateCallout(
                "You do NOT need Google's constants class (GPGSIds): NativeSocial reads the achievement ids from the Tier Map.", AuditSeverity.Info));

            return card;
        }

        private VisualElement CreateStateCard()
        {
            var card = NativeSocialUIStyle.CreateCard("Current state",
                "Read-only snapshot. \"Manifest\" is the generated AndroidManifest.xml; \"stored\" is the Play Games settings asset the runtime reads.");
            _stateBody = new VisualElement();
            card.Add(_stateBody);
            return card;
        }

        private VisualElement CreateApplicationIdCard()
        {
            var card = NativeSocialUIStyle.CreateCard("1. Application ID",
                "The numeric project number in Play Console > Play Games Services > Configuration (for example 148394552703). " +
                "It is written to the manifest meta-data com.google.android.gms.games.APP_ID. It is NOT an OAuth client id.");

            _appIdField = new TextField("Application ID") { value = _appId };
            _appIdField.RegisterValueChangedCallback(evt =>
            {
                _appId = evt.newValue;
                RefreshAll();
            });
            card.Add(_appIdField);

            var suggestion = GpgsAppIdSetup.SuggestAppId();
            if (!string.IsNullOrEmpty(suggestion) && suggestion != _appId)
            {
                var row = Row();
                row.Add(NativeSocialUIStyle.CreateButton($"Use {suggestion} (decoded from the achievement ids)", () =>
                {
                    _appId = suggestion;
                    _appIdField.SetValueWithoutNotify(_appId);
                    RefreshAll();
                }));
                card.Add(row);
            }

            _appIdFeedback = new VisualElement();
            card.Add(_appIdFeedback);
            return card;
        }

        private VisualElement CreateResourcesXmlCard()
        {
            var card = NativeSocialUIStyle.CreateCard("2. Android resources XML (optional)",
                "Play Console > Play Games Services > Configuration > Achievements > Get resources > Android. Paste it to verify the application id, " +
                "the package name and every achievement id of the Tier Map against what Google really has. Nothing is generated from it.");

            _xmlField = new TextField { multiline = true, value = _resourcesXml };
            _xmlField.style.minHeight = 100;
            _xmlField.RegisterValueChangedCallback(evt =>
            {
                _resourcesXml = evt.newValue;
                RefreshAll();
            });
            card.Add(_xmlField);

            _xmlFeedback = new VisualElement();
            card.Add(_xmlFeedback);
            return card;
        }

        private VisualElement CreateWebClientCard()
        {
            var card = NativeSocialUIStyle.CreateCard("3. Web client ID (Cloud Save / server auth code)",
                "Not needed for sign-in or achievements. It IS needed to read a server auth code (NativeSocial.GetServerAuthCode), " +
                "which is what links the Unity Authentication player to Google Play Games for Cloud Save. " +
                "Create an OAuth client of type \"Web application\" in the SAME Google Cloud project and paste the full id " +
                "(123456789012-xxxxxxxx.apps.googleusercontent.com) — the project number alone is not a client id.");

            _webClientField = new TextField("Client ID") { value = _webClientId };
            _webClientField.RegisterValueChangedCallback(evt =>
            {
                _webClientId = evt.newValue;
                RefreshAll();
            });
            card.Add(_webClientField);

            var row = Row();
            row.Add(NativeSocialUIStyle.CreateButton("↗ Open Google Cloud credentials", () => OpenCloud("https://console.cloud.google.com/apis/credentials")));
            row.Add(NativeSocialUIStyle.CreateButton("➕ Create Web OAuth client", () => OpenCloud("https://console.cloud.google.com/apis/credentials/oauthclient")));
            row.Add(NativeSocialUIStyle.CreateButton("📋 Copy Client ID", () =>
            {
                if (!string.IsNullOrWhiteSpace(_webClientId))
                    GUIUtility.systemCopyBuffer = _webClientId.Trim();
            }));
            card.Add(row);

            _webClientFeedback = new VisualElement();
            card.Add(_webClientFeedback);

            card.Add(NativeSocialUIStyle.CreateCallout(
                "The Web client id is public; its client SECRET is not. Enter the secret only in Project Settings > Services > Authentication > " +
                "ID Providers > Google Play Services, together with this same id. This tab never stores secrets.",
                AuditSeverity.Info));

            return card;
        }

        private VisualElement CreateApplyCard()
        {
            var card = NativeSocialUIStyle.CreateCard("Apply",
                "Calls Google's own Play Games setup, which stores the values above and regenerates the manifest. Safe to run more than once.");

            _applyButton = NativeSocialUIStyle.CreateButton("✔ Apply configuration", Apply, primary: true);
            _applyButton.style.marginTop = 4;
            _applyButton.style.alignSelf = Align.FlexStart;
            card.Add(_applyButton);

            _statusArea = new VisualElement();
            card.Add(_statusArea);
            return card;
        }

        // ── Refresh / validation ──────────────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshAppId();
            RefreshWebClient();
            RefreshXml();
            RefreshState();

            if (_applyButton != null)
                _applyButton.SetEnabled(ValidateAppId(_appId) == null && IsWebClientAcceptable());
        }

        private void RefreshAppId()
        {
            if (_appIdFeedback == null) return;
            _appIdFeedback.Clear();

            var problem = ValidateAppId(_appId);
            _appIdFeedback.Add(problem != null
                ? NativeSocialUIStyle.CreateCallout(problem, AuditSeverity.Warning)
                : NativeSocialUIStyle.CreateCallout("Application id looks valid. It will be written to the manifest when you Apply.", AuditSeverity.Pass));
        }

        private void RefreshWebClient()
        {
            if (_webClientFeedback == null) return;
            _webClientFeedback.Clear();

            var problem = GpgsResourcesXml.ValidateWebClientId(_webClientId, _appId);
            if (problem != null)
            {
                _webClientFeedback.Add(NativeSocialUIStyle.CreateCallout(problem, AuditSeverity.Warning));
                return;
            }

            _webClientFeedback.Add(string.IsNullOrWhiteSpace(_webClientId)
                ? NativeSocialUIStyle.CreateCallout(
                    "Empty: sign-in and achievements are unaffected. Without it, Android Cloud Save cannot link the player account " +
                    "(Unity Authentication rejects the link) and progress stays tied to the install.", AuditSeverity.Info)
                : NativeSocialUIStyle.CreateCallout("Web client id looks valid and belongs to the same Play Games project.", AuditSeverity.Pass));
        }

        private void RefreshXml()
        {
            if (_xmlFeedback == null) return;
            _xmlFeedback.Clear();

            if (string.IsNullOrWhiteSpace(_resourcesXml))
            {
                _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout("Paste the XML to cross-check the ids. Optional.", AuditSeverity.Info));
                return;
            }

            if (!GpgsResourcesXml.TryParse(_resourcesXml, out var xml, out var error))
            {
                _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout(error, AuditSeverity.Warning));
                return;
            }

            _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout(
                $"XML: application id {xml.AppId}, package {xml.PackageName ?? "(none)"}, {xml.Achievements.Count} achievements.",
                AuditSeverity.Pass));

            if (!string.IsNullOrEmpty(xml.AppId) && xml.AppId != _appId)
            {
                var row = Row();
                row.Add(NativeSocialUIStyle.CreateButton($"Use application id {xml.AppId} from the XML", () =>
                {
                    _appId = xml.AppId;
                    _appIdField.SetValueWithoutNotify(_appId);
                    RefreshAll();
                }));
                _xmlFeedback.Add(row);
                _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout(
                    $"The XML application id ({xml.AppId}) differs from the field above ({_appId}).", AuditSeverity.Warning));
            }

            var androidPackage = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            if (!string.IsNullOrEmpty(xml.PackageName) && xml.PackageName != androidPackage)
                _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout(
                    $"The XML was configured for package {xml.PackageName} but Player Settings > Android uses {androidPackage}. " +
                    "Play Games only signs in builds with the package registered in Play Console.", AuditSeverity.Warning));

            var mapIds = _mapIds;
            if (mapIds.Count == 0)
                return;

            var missing = xml.MissingFromXml(mapIds);
            var unused = xml.UnusedByMap(mapIds);

            if (missing.Count == 0 && unused.Count == 0)
            {
                _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout(
                    $"All {mapIds.Count} Google Play ids of the Tier Map match the XML.", AuditSeverity.Pass));
                return;
            }

            if (missing.Count > 0)
                _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout(
                    $"{missing.Count} id(s) in the Tier Map are NOT in the XML (they will not unlock on Android): {Join(missing)}",
                    AuditSeverity.Fail));

            if (unused.Count > 0)
                _xmlFeedback.Add(NativeSocialUIStyle.CreateCallout(
                    $"{unused.Count} achievement(s) in Play Console are not used by the Tier Map: {Join(unused)}",
                    AuditSeverity.Info));
        }

        private void RefreshState()
        {
            if (_stateBody == null) return;
            _stateBody.Clear();

            var manifestAppId = ReadManifestAppId();
            var storedWebClientId = GpgsAppIdSetup.ReadWebClientId();
            var androidPackage = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);

            _stateBody.Add(StateRow("Manifest application id", manifestAppId ?? "(none)",
                manifestAppId == null ? AuditSeverity.Fail : AuditSeverity.Pass));
            _stateBody.Add(StateRow("Stored Web client id", string.IsNullOrEmpty(storedWebClientId) ? "(empty)" : storedWebClientId,
                string.IsNullOrEmpty(storedWebClientId) ? AuditSeverity.Info : AuditSeverity.Pass));
            _stateBody.Add(StateRow("Installed Play Games plugin", _pluginVersion ?? "(not installed)",
                string.IsNullOrEmpty(_pluginVersion) ? AuditSeverity.Fail : AuditSeverity.Info));
            _stateBody.Add(StateRow("Android package name", androidPackage, AuditSeverity.Info));
            _stateBody.Add(StateRow("Achievements in Tier Map", _mapIds.Count.ToString(), AuditSeverity.Info));
        }

        // ── Apply ─────────────────────────────────────────────────────────────────

        private void Apply()
        {
            var appIdProblem = ValidateAppId(_appId);
            if (appIdProblem != null)
            {
                SetStatus(appIdProblem, AuditSeverity.Fail);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_webClientId) && GpgsResourcesXml.ValidateWebClientId(_webClientId, _appId) != null)
            {
                SetStatus("Fix the Web client id (or clear it) before applying.", AuditSeverity.Fail);
                return;
            }

            var ok = GpgsAppIdSetup.Apply(_appId.Trim(), _webClientId.Trim(), out var message);
            SetStatus(message, ok ? AuditSeverity.Pass : AuditSeverity.Fail);

            if (!ok) return;

            _appId = _appId.Trim();
            _webClientId = _webClientId.Trim();
            _appIdField.SetValueWithoutNotify(_appId);
            _webClientField.SetValueWithoutNotify(_webClientId);
            RefreshAll();
        }

        private void SetStatus(string message, AuditSeverity severity)
        {
            if (_statusArea == null || string.IsNullOrEmpty(message)) return;
            _statusArea.Clear();
            _statusArea.Add(NativeSocialUIStyle.CreateCallout(message, severity));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Same rules the Apply button enforces: digits only, long enough to be a Play Games project number.</summary>
        private static string ValidateAppId(string appId)
        {
            var value = (appId ?? string.Empty).Trim();
            if (value.Length == 0)
                return "Enter the numeric application id from Play Console > Play Games Services > Configuration.";

            if (!value.All(char.IsDigit))
                return "The application id contains only digits. It is the Play Games project number, not an OAuth client id.";

            if (value.Length < 5)
                return "That is too short for a Play Games application id (they are usually 12 digits).";

            return null;
        }

        private bool IsWebClientAcceptable() =>
            string.IsNullOrWhiteSpace(_webClientId) || GpgsResourcesXml.ValidateWebClientId(_webClientId, _appId) == null;

        private static List<string> LoadMapIds() =>
            AssetDatabase.FindAssets("t:AchievementTierMap")
                .Select(guid => AssetDatabase.LoadAssetAtPath<AchievementTierMap>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(map => map != null)
                .SelectMany(map => map.Entries)
                .Select(entry => entry.GooglePlayId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

        /// <summary>Reads the application id Google's setup wrote to the generated manifest (the 12-digit Play Games number).</summary>
        private static string ReadManifestAppId()
        {
            try
            {
                if (!System.IO.File.Exists(ManifestPath)) return null;

                var match = System.Text.RegularExpressions.Regex.Match(
                    System.IO.File.ReadAllText(ManifestPath),
                    @"com\.google\.android\.gms\.games\.APP_ID[\s\S]*?android:value=""\\u003(\d+)""");

                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Opens a Google page for the project in the Application ID field, when there is one.</summary>
        private void OpenCloud(string url)
        {
            var project = (_appId ?? string.Empty).Trim();
            Application.OpenURL(string.IsNullOrEmpty(project) ? url : $"{url}?project={project}");
        }

        private static string Join(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            return string.Join(", ", list.Take(8)) + (list.Count > 8 ? ", …" : string.Empty);
        }

        private static VisualElement StateRow(string label, string value, AuditSeverity severity)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 5;

            row.Add(new Label(label) { style = { color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });

            var right = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 1 } };
            right.Add(new Label(value)
            {
                style =
                {
                    fontSize = 11,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = NativeSocialUIStyle.ColorText,
                    whiteSpace = WhiteSpace.Normal,
                    marginRight = 8
                }
            });

            var badge = NativeSocialUIStyle.CreateBadge(
                severity == AuditSeverity.Pass ? "OK" : severity == AuditSeverity.Fail ? "Missing" : "—", severity);
            badge.style.minWidth = 60;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            right.Add(badge);

            row.Add(right);
            return row;
        }

        private static VisualElement Row() =>
            new() { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 8, flexWrap = Wrap.Wrap } };
    }
}
