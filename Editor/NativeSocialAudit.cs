using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    public enum AuditSeverity
    {
        Pass,
        Info,
        Warning,
        Fail
    }

    public struct AuditResult
    {
        public string Category;
        public string Title;
        public AuditSeverity Severity;
        public string Detail;
        public string FixHint;

        /// <summary>Plain-language explanation of what this check is, why it exists and how it behaves —
        /// shown under the title so a developer unfamiliar with platform SDKs understands the finding
        /// without needing to read source code.</summary>
        public string WhatIsThis;

        /// <summary>Ready-to-paste task for an AI coding agent. Empty for Pass results.</summary>
        public string Prompt;

        /// <summary>One-click in-Editor fix (label + action). Null when the finding needs manual work.</summary>
        public string FixLabel;
        public Action Fix;
    }

    /// <summary>
    /// Headless-friendly audit of the project's Native Social setup: which platform SDKs are present,
    /// whether a bootstrap/initialization exists, and whether an <see cref="AchievementTierMap"/> covers
    /// every platform. Run from the Dashboard, or in CI:
    /// <code>Unity -batchmode -quit -projectPath "&lt;project&gt;" -logFile - -executeMethod Wagenheimer.NativeSocial.Editor.NativeSocialAudit.RunHeadlessAndLog</code>
    /// </summary>
    public static class NativeSocialAudit
    {
        public const string CategoryCommon = "⚙️ Core & Mapping";
        public const string CategorySteam = "🖥️ Steam (Steamworks.NET)";
        public const string CategoryAndroid = "🤖 Android (Google Play Games)";
        public const string CategoryIOS = "🍎 iOS (Game Center)";

        [MenuItem("Tools/Wagenheimer/Native Social/Verify Setup...", priority = 1)]
        public static void OpenWindow() => NativeSocialDashboardWindow.Open(NativeSocialDashboardWindow.Tab.SetupAudit);

        /// <summary>Writes a Markdown report to the log and exits with code 1 if any check failed (batch mode).</summary>
        public static void RunHeadlessAndLog()
        {
            var results = RunAudit();
            Debug.Log(ToMarkdown(results));

            if (results.Any(r => r.Severity == AuditSeverity.Fail))
                EditorApplication.Exit(1);
        }

        public static List<AuditResult> RunAudit()
        {
            var results = new List<AuditResult>();

            AuditCommon(results);
            AuditSteam(results);
            AuditAndroid(results);
            AuditIOS(results);
            AttachPrompts(results);

            return results;
        }

        #region Core & Mapping

        private static void AuditCommon(List<AuditResult> results)
        {
            // 1. Check startup initialization in code or via bootstrap script
            var (initFound, initScriptPath) = FindInitializeCallInProject();
            bool hasBootstrapScript = AssetDatabase.FindAssets("NativeSocialBootstrap t:MonoScript").Length > 0;

            if (initFound)
            {
                Add(results, CategoryCommon, "Startup Initialization", true,
                    $"NativeSocial.Initialize(...) detected in project code: '{initScriptPath}'.",
                    null,
                    whatIsThis: "NativeSocial is initialized at startup directly from your game code with your achievement maps.");
            }
            else if (hasBootstrapScript)
            {
                Add(results, CategoryCommon, "Startup Initialization", true,
                    "NativeSocialBootstrap component found in project assets. Ensure it is placed in your startup/bootstrap scene.",
                    null,
                    whatIsThis: "NativeSocialBootstrap automatically loads your AchievementTierMap and calls NativeSocial.Initialize at scene start.");
            }
            else
            {
                Add(results, CategoryCommon, "Startup Initialization", false,
                    null,
                    "No initialization found: NativeSocial.Initialize(...) must be called once at startup (in your main game manager or via NativeSocialBootstrap), or achievement calls silently no-op.",
                    "Generates NativeSocialBootstrap.cs linked to your AchievementTierMap asset.",
                    "Generate NativeSocialBootstrap.cs",
                    CreateBootstrapScriptAsset,
                    AuditSeverity.Warning,
                    whatIsThis: "NativeSocial requires a single Initialize() call before achievement progress can be reported.");
            }

            // 2. Check AchievementTierMap asset
            var maps = FindAllAchievementTierMaps();
            if (maps.Count == 0)
            {
                Add(results, CategoryCommon, "Achievement Tier Map Asset", false,
                    null,
                    "No AchievementTierMap asset found: there is nowhere to keep this game's per-platform achievement IDs.",
                    "Creates an empty AchievementTierMap asset under Assets/Social.",
                    "Create Achievement Tier Map",
                    CreateAchievementTierMapAsset,
                    AuditSeverity.Warning,
                    whatIsThis: "AchievementTierMap holds all trophies and their corresponding platform IDs (Steam, Google Play, Apple Game Center).");
            }
            else
            {
                var map = maps[0];
                int total = map.Entries.Count;
                Add(results, CategoryCommon, "Achievement Tier Map Asset", total > 0,
                    $"Found '{AssetDatabase.GetAssetPath(map)}' with {total} configured entries.",
                    $"'{AssetDatabase.GetAssetPath(map)}' has 0 entries: no achievements will be reported.",
                    "Add achievement entries to your AchievementTierMap asset.",
                    "Select Map Asset", () => Selection.activeObject = map,
                    AuditSeverity.Warning,
                    whatIsThis: "Central asset containing all your trophies, points, and platform IDs.");

                if (maps.Count > 1)
                {
                    results.Add(Result(CategoryCommon, "Multiple Achievement Tier Map assets", AuditSeverity.Info,
                        $"{maps.Count} AchievementTierMap assets found; NativeSocial.Initialize should be built from exactly one."));
                }
            }
        }

        internal static (bool Found, string ScriptPath) FindInitializeCallInProject()
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.IndexOf("NativeSocial", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (path.EndsWith("NativeSocialAudit.cs", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith("NativeSocialAuditView.cs", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith("NativeSocialBootstrap.cs", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;
                var text = script.text;
                if (!string.IsNullOrEmpty(text) && text.Contains("NativeSocial.Initialize"))
                {
                    return (true, path);
                }
            }
            return (false, null);
        }

        internal static void CreateBootstrapScriptAsset()
        {
            const string dir = "Assets/Scripts/Social";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "NativeSocialBootstrap.cs");
            if (File.Exists(path))
            {
                EditorUtility.DisplayDialog("Script Exists", $"A script already exists at {path}", "OK");
                return;
            }

            const string content = @"using UnityEngine;
using Wagenheimer.NativeSocial;

/// <summary>
/// Bootstrap component for NativeSocial.
/// Automatically binds to an AchievementTierMap asset and calls NativeSocial.Initialize at startup.
/// </summary>
public class NativeSocialBootstrap : MonoBehaviour
{
    [Tooltip(""The AchievementTierMap containing all trophy definitions and platform IDs. If left empty, will try loading from Resources 'Social/AchievementTierMap' or finding any map asset."")]
    [SerializeField] private AchievementTierMap tierMap;

    [Tooltip(""Whether to mark this GameObject as persistent across scene loads."")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    private void Awake()
    {
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        if (tierMap == null)
        {
            tierMap = Resources.Load<AchievementTierMap>(""Social/AchievementTierMap"");
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
            Debug.Log($""[NativeSocial] Initialized from '{tierMap.name}' ({tierMap.Entries.Count} entries)."");
        }
        else
        {
            NativeSocial.Initialize();
            Debug.LogWarning(""[NativeSocial] Initialized with empty maps because no AchievementTierMap was found."");
        }
    }
}
";

            File.WriteAllText(path, content);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Created", $"NativeSocialBootstrap.cs created at {path}", "OK");
        }

        internal static void AddBootstrapToCurrentScene()
        {
            var type = FindBootstrapType();
            if (type == null)
            {
                bool created = AssetDatabase.FindAssets("NativeSocialBootstrap t:MonoScript").Length == 0;
                if (created) CreateBootstrapScriptAsset();

                var message = created
                    ? "NativeSocialBootstrap.cs was just generated and Unity needs to recompile before it can be attached to a GameObject. Click 'Add Bootstrap to Scene' again in a few seconds, once compiling finishes."
                    : "NativeSocialBootstrap.cs exists but isn't compiled yet (still compiling, or it has a compile error — check the Console). Try again once Unity finishes compiling.";
                EditorUtility.DisplayDialog("Not ready yet", message, "OK");
                Debug.LogWarning("[NativeSocial] " + message);
                return;
            }

            var go = new GameObject("NativeSocialBootstrap");
            Undo.RegisterCreatedObjectUndo(go, "Create NativeSocialBootstrap");
            var comp = go.AddComponent(type);
            var maps = FindAllAchievementTierMaps();
            if (maps.Count > 0)
            {
                var so = new SerializedObject(comp);
                var prop = so.FindProperty("tierMap");
                if (prop != null)
                {
                    prop.objectReferenceValue = maps[0];
                    so.ApplyModifiedProperties();
                }
            }
            Selection.activeGameObject = go;
            Debug.Log("[NativeSocial] Created 'NativeSocialBootstrap' GameObject with the NativeSocialBootstrap component attached and linked to AchievementTierMap.");
        }

        private static Type FindBootstrapType() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "NativeSocialBootstrap" && typeof(MonoBehaviour).IsAssignableFrom(t));

        #endregion

        #region Steam

        private static void AuditSteam(List<AuditResult> results)
        {
            bool steamFound = IsTypeAvailable("Steamworks.SteamUserStats") ||
                              UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p => p.name == "com.rlabrecque.steamworks.net");
            bool steamDefine = HasDefine("WAGENHEIMER_NATIVESOCIAL_STEAM");

            Add(results, CategorySteam, "Steamworks.NET Plugin (Steam)", steamFound,
                "Steamworks.NET is installed.",
                "Steamworks.NET not installed (only required for Steam desktop builds).",
                whatIsThis: "Steamworks.NET enables Steam achievement and stat synchronization for desktop builds.");

            if (steamFound)
            {
                Add(results, CategorySteam, "Steam Scripting Define Symbol", steamDefine,
                    "WAGENHEIMER_NATIVESOCIAL_STEAM is active in the current build target.",
                    "Steamworks.NET is installed but WAGENHEIMER_NATIVESOCIAL_STEAM is not defined in Player Settings. (Note: if you use UnityBuildPipeline/PublisherProfile, it applies defines automatically during build).",
                    "Add the define to active build target's Scripting Define Symbols for local testing.",
                    "Add Define", () => SetDefine("WAGENHEIMER_NATIVESOCIAL_STEAM", true),
                    AuditSeverity.Info,
                    whatIsThis: "Compiler switch for Steam achievement calls. Can be added to local PlayerSettings or configured in PublisherProfile for automated builds.");
            }

            bool hasAppId = File.Exists("steam_appid.txt");
            Add(results, CategorySteam, "Steam AppID File (Local Testing)", hasAppId,
                "steam_appid.txt found in project root.",
                "steam_appid.txt not found in project root (only needed for local Editor/standalone testing with Steam client).",
                failSeverity: AuditSeverity.Info,
                whatIsThis: "steam_appid.txt tells the Steam client which game is running during local development outside of the Steam launcher.");

            var maps = FindAllAchievementTierMaps();
            if (maps.Count > 0)
            {
                var map = maps[0];
                int total = map.Entries.Count;
                int missingSteam = map.Entries.Count(e => string.IsNullOrEmpty(e.SteamStat));
                Add(results, CategorySteam, "Steam Stat/Achievement Names", missingSteam == 0,
                    $"All {total} achievement tiers have a SteamStat name assigned.",
                    $"{missingSteam} of {total} achievement tiers are missing a SteamStat name.",
                    "Configure SteamStat in your AchievementTierMap (e.g. Trophy{N}_{tier}_Status).",
                    "Select Map Asset", () => Selection.activeObject = map,
                    steamFound ? AuditSeverity.Warning : AuditSeverity.Info,
                    whatIsThis: "Steam achievements use the stat/achievement API names configured in Steamworks Partner site.");
            }
        }

        #endregion

        #region Android

        private static void AuditAndroid(List<AuditResult> results)
        {
            string gpgsVersion = GpgsInstaller.GetInstalledVersion();
            bool gpgsPackageInstalled = !string.IsNullOrEmpty(gpgsVersion);
            bool gpgsLoose = GpgsInstaller.FindLoosePluginRoots().Count > 0;
            bool gpgsEditorType = IsTypeAvailable("GooglePlayGames.Editor.GPGSProjectSettings");
            bool gpgsRuntimeType = IsTypeAvailable("GooglePlayGames.PlayGamesPlatform");
            bool gpgsFound = gpgsPackageInstalled || gpgsLoose || gpgsEditorType || gpgsRuntimeType;

            Add(results, CategoryAndroid, "Google Play Games Plugin (Android)", gpgsFound,
                gpgsPackageInstalled
                    ? $"Google Play Games is installed via Git as UPM package com.google.play.games v{gpgsVersion}."
                    : (gpgsLoose ? "PlayGamesPlatform detected in project (loose Assets/ import — consider migrating to UPM)." : "Google Play Games plugin detected."),
                "Google Play Games plugin not detected: Android achievements/leaderboards will silently no-op.",
                $"Installs Google's official plugin v{GpgsInstaller.PackageVersion} from its git repository as a UPM package.",
                "Install via Package Manager", () => GpgsInstaller.Install(),
                AuditSeverity.Info,
                whatIsThis: "Google's SDK for reporting achievements and leaderboards on Android. NativeSocial talks to it automatically once installed.");

            bool gpgsDefine = gpgsPackageInstalled || HasDefine("WAGENHEIMER_NATIVESOCIAL_GPGS");
            if (gpgsFound)
            {
                Add(results, CategoryAndroid, "GPGS Scripting Define Symbol", gpgsDefine,
                    gpgsPackageInstalled
                        ? "WAGENHEIMER_NATIVESOCIAL_GPGS is active automatically via com.google.play.games UPM versionDefine."
                        : "WAGENHEIMER_NATIVESOCIAL_GPGS is active in Scripting Define Symbols.",
                    "Google Play Games plugin found as loose files but WAGENHEIMER_NATIVESOCIAL_GPGS is not defined in Player Settings.",
                    "Add WAGENHEIMER_NATIVESOCIAL_GPGS to Scripting Define Symbols or reinstall as UPM package.",
                    "Add Define", () => SetDefine("WAGENHEIMER_NATIVESOCIAL_GPGS", true),
                    AuditSeverity.Warning,
                    whatIsThis: "WAGENHEIMER_NATIVESOCIAL_GPGS enables the GPGS integration inside NativeSocial.");
            }

            var maps = FindAllAchievementTierMaps();
            if (maps.Count > 0)
            {
                var map = maps[0];
                int total = map.Entries.Count;
                int missingGoogle = map.CountMissingGooglePlay();
                Add(results, CategoryAndroid, "Google Play Achievement IDs", missingGoogle == 0,
                    $"All {total} achievement tiers have a Google Play ID assigned.",
                    $"{missingGoogle} of {total} achievement tiers are missing a Google Play ID.",
                    "Create achievements in Google Play Console (Play Console > Grow > Play Games Services > Achievements), then paste the alphanumeric IDs into the map asset.",
                    "Select Map Asset", () => Selection.activeObject = map,
                    gpgsFound ? AuditSeverity.Warning : AuditSeverity.Info,
                    whatIsThis: "Each achievement must have its Google Play Console ID configured in your AchievementTierMap so it can unlock on Android.");
            }
        }

        #endregion

        #region iOS

        private static void AuditIOS(List<AuditResult> results)
        {
            bool isIosTarget = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
            Add(results, CategoryIOS, "Apple Game Center Support", true,
                isIosTarget ? "Active build target is iOS." : "Game Center is built directly into Unity iOS support.",
                null,
                whatIsThis: "Apple Game Center requires no third-party package; it is built into Unity's iOS runtime.");

            var maps = FindAllAchievementTierMaps();
            if (maps.Count > 0)
            {
                var map = maps[0];
                int total = map.Entries.Count;
                int missingApple = map.CountMissingApple();
                Add(results, CategoryIOS, "Apple Game Center Achievement IDs", missingApple == 0,
                    $"All {total} achievement tiers have an Apple Game Center ID assigned.",
                    $"{missingApple} of {total} achievement tiers are missing an Apple Game Center ID.",
                    "Create achievements in App Store Connect (Features > Game Center > Achievements) and paste their IDs into the map asset.",
                    "Select Map Asset", () => Selection.activeObject = map,
                    AuditSeverity.Info,
                    whatIsThis: "Each achievement must have its App Store Connect ID configured in your AchievementTierMap to unlock on iOS.");
            }
        }

        #endregion

        #region Helpers

        internal static List<AchievementTierMap> FindAllAchievementTierMaps() =>
            AssetDatabase.FindAssets("t:AchievementTierMap")
                .Select(guid => AssetDatabase.LoadAssetAtPath<AchievementTierMap>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(m => m != null)
                .ToList();

        internal static void CreateAchievementTierMapAsset()
        {
            const string dir = "Assets/Social";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Social");

            var path = AssetDatabase.GenerateUniqueAssetPath(dir + "/AchievementTierMap.asset");
            var asset = ScriptableObject.CreateInstance<AchievementTierMap>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            Debug.Log($"[NativeSocial] Created '{path}'. Fill in TrophyNumber/Tier/SteamStat/GooglePlayId/AppleId for each achievement tier.");
        }

        private static void Add(List<AuditResult> results, string category, string title, bool pass, string passDetail,
            string failDetail, string hint = null, string fixLabel = null, Action fix = null, AuditSeverity failSeverity = AuditSeverity.Fail,
            string whatIsThis = null)
        {
            var result = Result(category, title, pass ? AuditSeverity.Pass : failSeverity, pass ? passDetail : failDetail, whatIsThis);
            if (!pass)
            {
                result.FixHint = hint;
                result.FixLabel = fixLabel;
                result.Fix = fix;
            }
            results.Add(result);
        }

        private static AuditResult Result(string category, string title, AuditSeverity severity, string detail, string whatIsThis = null) =>
            new AuditResult { Category = category, Title = title, Severity = severity, Detail = detail, WhatIsThis = whatIsThis };

        internal static bool IsTypeAvailable(string typeFullName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Any(t => t.FullName == typeFullName);

        internal static bool HasDefine(string symbol)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
#if UNITY_2021_2_OR_NEWER
            var namedGroup = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            var defines = PlayerSettings.GetScriptingDefineSymbols(namedGroup);
#else
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
            return defines.Split(';').Contains(symbol);
        }

        internal static void SetDefine(string symbol, bool enable)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
#if UNITY_2021_2_OR_NEWER
            var namedGroup = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            var defines = PlayerSettings.GetScriptingDefineSymbols(namedGroup).Split(';').Select(d => d.Trim()).Where(d => !string.IsNullOrEmpty(d)).ToList();
#else
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';').Select(d => d.Trim()).Where(d => !string.IsNullOrEmpty(d)).ToList();
#endif
            if (enable && !defines.Contains(symbol)) defines.Add(symbol);
            else if (!enable && defines.Contains(symbol)) defines.Remove(symbol);

            var joined = string.Join(";", defines);
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(namedGroup, joined);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, joined);
#endif
            Debug.Log($"[NativeSocial] {symbol} is now {(enable ? "ENABLED" : "DISABLED")}.");
        }

        #endregion

        #region Export

        private static void AttachPrompts(List<AuditResult> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                bool isActionable = r.Severity == AuditSeverity.Fail || r.Severity == AuditSeverity.Warning ||
                                    (r.Severity == AuditSeverity.Info && !string.IsNullOrEmpty(r.FixHint));
                if (!isActionable) continue;

                r.Prompt = $"In the Unity project, resolve this Native Social audit finding.\nCategory: {r.Category}\nFinding: {r.Title}\n" +
                           $"Evidence: {r.Detail}\nExpected fix: {r.FixHint}\n" +
                           "Keep the change minimal and re-run Tools > Wagenheimer > Native Social > Verify Setup to confirm.";
                results[i] = r;
            }
        }

        public static string ToMarkdown(List<AuditResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Native Social - Audit Report").AppendLine();
            sb.AppendLine($"Fails: {results.Count(r => r.Severity == AuditSeverity.Fail)}  |  " +
                          $"Warnings: {results.Count(r => r.Severity == AuditSeverity.Warning)}  |  " +
                          $"Passed: {results.Count(r => r.Severity == AuditSeverity.Pass)}");

            foreach (var group in results.GroupBy(r => r.Category))
            {
                sb.AppendLine().AppendLine($"## {group.Key}");
                foreach (var r in group)
                {
                    sb.AppendLine($"- **[{r.Severity}]** {r.Title}");
                    if (!string.IsNullOrEmpty(r.Detail)) sb.AppendLine($"  - {r.Detail}");
                    if (!string.IsNullOrEmpty(r.FixHint)) sb.AppendLine($"  - Fix: {r.FixHint}");
                }
            }
            return sb.ToString();
        }

        public static string ToPromptMarkdown(List<AuditResult> results)
        {
            var pending = results.Where(r => !string.IsNullOrEmpty(r.Prompt)).ToList();
            if (pending.Count == 0) return "No actionable Native Social findings.";

            var sb = new StringBuilder("Fix every Native Social finding below, then re-run the audit.\n");
            for (int i = 0; i < pending.Count; i++)
                sb.AppendLine().AppendLine($"### {i + 1}. {pending[i].Title}").AppendLine(pending[i].Prompt);
            return sb.ToString();
        }

        #endregion
    }
}
