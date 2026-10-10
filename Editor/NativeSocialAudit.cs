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

        /// <summary>Path to the source file or asset detected by this check (e.g. "Assets/.../Main.cs"), if any.</summary>
        public string AssetPath;

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

        public struct ProjectCodeAnalysis
        {
            public bool HasInitialize;
            public string InitializePath;
            public bool HasAuthenticate;
            public string AuthenticatePath;
            public bool HasReport;
            public string ReportPath;
            public bool HasLocId;
            public string LocIdPath;
            public bool HasSyncCompleted;
            public string SyncCompletedPath;
            public bool HasShowAchievementsUI;
            public string ShowAchievementsUIPath;
        }

        private static ProjectCodeAnalysis? _cachedAnalysis;

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

        public static ProjectCodeAnalysis AnalyzeProjectCode(bool forceRefresh = false)
        {
            if (_cachedAnalysis.HasValue && !forceRefresh)
                return _cachedAnalysis.Value;

            var analysis = new ProjectCodeAnalysis();
            var guids = AssetDatabase.FindAssets("t:MonoScript");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.IndexOf("NativeSocial", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (path.EndsWith("NativeSocialAudit.cs", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith("NativeSocialAuditView.cs", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith("NativeSocialChecklistView.cs", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith("NativeSocialBootstrap.cs", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;
                var text = script.text;
                if (string.IsNullOrEmpty(text)) continue;

                if (!analysis.HasInitialize && text.Contains("NativeSocial.Initialize"))
                {
                    analysis.HasInitialize = true;
                    analysis.InitializePath = path;
                }

                if (!analysis.HasAuthenticate && (text.Contains("NativeSocial.Authenticate") || text.Contains("Social.localUser.Authenticate") || text.Contains("PlayGamesPlatform.Instance.Authenticate")))
                {
                    analysis.HasAuthenticate = true;
                    analysis.AuthenticatePath = path;
                }

                if (!analysis.HasReport && text.Contains("NativeSocial.Report"))
                {
                    analysis.HasReport = true;
                    analysis.ReportPath = path;
                }

                if (!analysis.HasLocId && (text.Contains("AchievementTierMap.LocId") || text.Contains(".LocId(")))
                {
                    analysis.HasLocId = true;
                    analysis.LocIdPath = path;
                }

                if (!analysis.HasSyncCompleted && (text.Contains("NativeSocial.SyncCompleted") || text.Contains(".SyncCompleted") || text.Contains("SyncAchievementTiers")))
                {
                    analysis.HasSyncCompleted = true;
                    analysis.SyncCompletedPath = path;
                }

                if (!analysis.HasShowAchievementsUI && (text.Contains("NativeSocial.ShowAchievementsUI") || text.Contains("Social.ShowAchievementsUI")))
                {
                    analysis.HasShowAchievementsUI = true;
                    analysis.ShowAchievementsUIPath = path;
                }

                if (analysis.HasInitialize && analysis.HasAuthenticate && analysis.HasReport && analysis.HasLocId && analysis.HasSyncCompleted && analysis.HasShowAchievementsUI)
                    break;
            }

            _cachedAnalysis = analysis;
            return analysis;
        }

        private static void AuditCommon(List<AuditResult> results)
        {
            var code = AnalyzeProjectCode(forceRefresh: true);
            var bootstrapGuids = AssetDatabase.FindAssets("NativeSocialBootstrap t:MonoScript");
            bool hasBootstrapScript = bootstrapGuids.Length > 0;
            string bootstrapPath = hasBootstrapScript ? AssetDatabase.GUIDToAssetPath(bootstrapGuids[0]) : null;

            // 1. Startup Initialization
            if (code.HasInitialize)
            {
                Add(results, CategoryCommon, "Startup Initialization", true,
                    $"NativeSocial.Initialize(...) detected in project code: '{code.InitializePath}'.",
                    null,
                    whatIsThis: "NativeSocial is initialized at startup directly from your game code with your achievement maps.",
                    assetPath: code.InitializePath);
            }
            else if (hasBootstrapScript)
            {
                Add(results, CategoryCommon, "Startup Initialization", true,
                    "NativeSocialBootstrap component found in project assets. Ensure it is placed in your startup/bootstrap scene.",
                    null,
                    whatIsThis: "NativeSocialBootstrap automatically loads your AchievementTierMap and calls NativeSocial.Initialize at scene start.",
                    assetPath: bootstrapPath);
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

            // 2. Mobile Player Authentication
            if (code.HasAuthenticate)
            {
                Add(results, CategoryCommon, "Mobile Player Authentication", true,
                    $"Player authentication detected in project code: '{code.AuthenticatePath}'.",
                    null,
                    whatIsThis: "NativeSocial.Authenticate(...) signs in with Google Play Games (Android) and Apple Game Center (iOS). Without authentication, mobile achievement progress is rejected.",
                    assetPath: code.AuthenticatePath);
            }
            else
            {
                string targetScript = code.HasInitialize ? code.InitializePath : (hasBootstrapScript ? bootstrapPath : null);
                string fixLabel = !string.IsNullOrEmpty(targetScript) ? $"Add Authenticate to {Path.GetFileName(targetScript)}" : null;
                Action fixAction = !string.IsNullOrEmpty(targetScript) ? () => AddAuthenticateToScript(targetScript) : (Action)null;

                Add(results, CategoryCommon, "Mobile Player Authentication", false,
                    null,
                    "No player authentication found: NativeSocial.Authenticate(...) must be called at startup on mobile (Android/iOS) so Game Center and Google Play Games can accept achievement updates.",
                    "Call NativeSocial.Authenticate(success => { ... }) at startup after NativeSocial.Initialize.",
                    fixLabel, fixAction,
                    AuditSeverity.Warning,
                    whatIsThis: "On iOS, Game Center rejects Social.ReportProgress with GKErrorNotAuthenticated until authenticated. On Android, Google Play Games requires sign-in before recording achievements.",
                    assetPath: targetScript);
            }

            // 3. Progress Reporting Calls
            if (code.HasReport)
            {
                Add(results, CategoryCommon, "Progress Reporting Calls", true,
                    $"NativeSocial.Report(...) call detected in project code: '{code.ReportPath}'.",
                    null,
                    whatIsThis: "Gameplay code dispatches achievement progress through NativeSocial.Report(...) without platform-specific code.",
                    assetPath: code.ReportPath);
            }
            else
            {
                Add(results, CategoryCommon, "Progress Reporting Calls", false,
                    null,
                    "No NativeSocial.Report(...) call detected in project code: achievements will not be updated as the player progresses.",
                    "Call NativeSocial.Report(locId, delta, current, total, completed) from your achievement/stat manager.",
                    null, null,
                    AuditSeverity.Info,
                    whatIsThis: "NativeSocial.Report routes achievement unlocks to the active platform (Steam, Android, or iOS).");
            }

            // 4. In-Game Achievements UI Button (Best Practice)
            if (code.HasShowAchievementsUI)
            {
                Add(results, CategoryCommon, "In-Game Achievements UI Button", true,
                    $"NativeSocial.ShowAchievementsUI() call detected in project code: '{code.ShowAchievementsUIPath}'.",
                    null,
                    whatIsThis: "Opening the platform's achievements overlay allows players to view their progress, percentage and unlocked trophies directly from your UI.",
                    assetPath: code.ShowAchievementsUIPath);
            }
            else
            {
                Add(results, CategoryCommon, "In-Game Achievements UI Button", false,
                    null,
                    "Best Practice: No call to NativeSocial.ShowAchievementsUI() detected. Adding a trophy button in your Settings/Options or Main Menu lets players view their Game Center / Google Play Games achievements on demand.",
                    "Add a trophy/achievements button in your UI calling NativeSocial.ShowAchievementsUI().",
                    null, null,
                    AuditSeverity.Info,
                    whatIsThis: "Apple Human Interface Guidelines and Google Play Games recommend providing an in-game entry point so players can inspect achievements anytime.");
            }

            // 5. Check AchievementTierMap asset
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
                var mapPath = AssetDatabase.GetAssetPath(map);
                Add(results, CategoryCommon, "Achievement Tier Map Asset", total > 0,
                    $"Found '{mapPath}' with {total} configured entries.",
                    $"'{mapPath}' has 0 entries: no achievements will be reported.",
                    "Add achievement entries to your AchievementTierMap asset.",
                    "Select Map Asset", () => Selection.activeObject = map,
                    AuditSeverity.Warning,
                    whatIsThis: "Central asset containing all your trophies, points, and platform IDs.",
                    assetPath: mapPath);

                if (maps.Count > 1)
                {
                    results.Add(Result(CategoryCommon, "Multiple Achievement Tier Map assets", AuditSeverity.Info,
                        $"{maps.Count} AchievementTierMap assets found; NativeSocial.Initialize should be built from exactly one."));
                }
            }
        }

        internal static (bool Found, string ScriptPath) FindInitializeCallInProject()
        {
            var analysis = AnalyzeProjectCode();
            return (analysis.HasInitialize, analysis.InitializePath);
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

    [Tooltip(""Whether to automatically authenticate with Google Play Games (Android) or Game Center (iOS) at startup."")]
    [SerializeField] private bool autoAuthenticateOnMobile = true;

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

#if UNITY_ANDROID || UNITY_IOS
        if (autoAuthenticateOnMobile)
        {
            NativeSocial.Authenticate(success =>
            {
                Debug.Log($""[NativeSocial] Mobile player authentication: "" + (success ? ""Success"" : ""Failed""));
            });
        }
#endif
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

        internal static void AddAuthenticateToScript(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog("File Not Found", $"Could not find file at '{scriptPath}'.", "OK");
                return;
            }

            try
            {
                string text = File.ReadAllText(scriptPath);
                if (text.Contains("NativeSocial.Authenticate"))
                {
                    EditorUtility.DisplayDialog("Already Present", $"NativeSocial.Authenticate is already called in '{scriptPath}'.", "OK");
                    return;
                }

                // Look for the end of the NativeSocial.Initialize call block
                int initIndex = text.IndexOf("NativeSocial.Initialize", StringComparison.Ordinal);
                if (initIndex >= 0)
                {
                    int semiColonIndex = text.IndexOf(';', initIndex);
                    if (semiColonIndex >= 0)
                    {
                        // Determine indent from the line of Initialize
                        int lineStart = text.LastIndexOf('\n', initIndex);
                        string indent = "            ";
                        if (lineStart >= 0)
                        {
                            int spaceCount = 0;
                            for (int i = lineStart + 1; i < initIndex && (text[i] == ' ' || text[i] == '\t'); i++)
                                spaceCount += (text[i] == '\t' ? 4 : 1);
                            indent = new string(' ', spaceCount > 0 ? spaceCount : 12);
                        }

                        string codeToInsert = "\n\n" +
                            indent + "#if UNITY_ANDROID || UNITY_IOS\n" +
                            indent + "NativeSocial.Authenticate(success =>\n" +
                            indent + "{\n" +
                            indent + "    Debug.Log($\"[NativeSocial] Mobile player authentication: {(success ? \"Success\" : \"Failed\")}\");\n" +
                            indent + "});\n" +
                            indent + "#endif";

                        text = text.Insert(semiColonIndex + 1, codeToInsert);
                        File.WriteAllText(scriptPath, text);
                        AssetDatabase.Refresh();
                        Debug.Log($"[NativeSocial] Added NativeSocial.Authenticate to '{scriptPath}'.");
                        EditorUtility.DisplayDialog("Authentication Added", $"Successfully added NativeSocial.Authenticate call to '{scriptPath}'.", "OK");
                        return;
                    }
                }

                // Fallback: Ping the file so the user can add it manually
                OpenAssetOrFile(scriptPath);
                EditorUtility.DisplayDialog("Manual Edit Needed",
                    $"Opened '{Path.GetFileName(scriptPath)}'. Please add:\n\n#if UNITY_ANDROID || UNITY_IOS\nNativeSocial.Authenticate(success => {{ ... }});\n#endif", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeSocial] Failed to add Authenticate to script: {ex.Message}");
            }
        }

        #endregion

        #region Steam

        private static void AuditSteam(List<AuditResult> results)
        {
            bool steamFound = IsTypeAvailable("Steamworks.SteamUserStats") || IsPackageRegistered("com.rlabrecque.steamworks.net");
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
                whatIsThis: "steam_appid.txt tells the Steam client which game is running during local development outside of the Steam launcher.",
                assetPath: hasAppId ? "steam_appid.txt" : null);

            var maps = FindAllAchievementTierMaps();
            if (maps.Count > 0)
            {
                var map = maps[0];
                int total = map.Entries.Count;
                var mapPath = AssetDatabase.GetAssetPath(map);
                int missingSteam = map.Entries.Count(e => string.IsNullOrEmpty(e.SteamStat));
                Add(results, CategorySteam, "Steam Stat/Achievement Names", missingSteam == 0,
                    $"All {total} achievement tiers have a SteamStat name assigned.",
                    $"{missingSteam} of {total} achievement tiers are missing a SteamStat name.",
                    "Configure SteamStat in your AchievementTierMap (e.g. Trophy{N}_{tier}_Status).",
                    "Select Map Asset", () => Selection.activeObject = map,
                    steamFound ? AuditSeverity.Warning : AuditSeverity.Info,
                    whatIsThis: "Steam achievements use the stat/achievement API names configured in Steamworks Partner site.",
                    assetPath: mapPath);

                var modelResult = Result(CategorySteam, "Steam Unlock Model", AuditSeverity.Info,
                    DescribeSteamModel(map),
                    whatIsThis: "How NativeSocial drives Steam achievements. 'Stat threshold' only writes the stat and relies on the Steamworks partner site to unlock each achievement from its stat threshold (the legacy Storm Tale 2 behavior). 'Explicit achievement' calls SetAchievement when a tier is completed.");
                modelResult.AssetPath = mapPath;
                results.Add(modelResult);
            }
        }

        /// <summary>Plain-language summary of the map's Steam unlock model, shown in the Setup Audit.</summary>
        private static string DescribeSteamModel(AchievementTierMap map)
        {
            if (map.SteamDefaultUnlockMode == SteamUnlockMode.StatThreshold)
            {
                int ignoredNames = map.Entries.Count(e => !string.IsNullOrEmpty(e.SteamAchievement));
                var ignored = ignoredNames > 0
                    ? $" ({ignoredNames} per-tier 'Steam achievement' name(s) are ignored in this mode.)"
                    : string.Empty;
                return "Stat threshold: the game writes the Steam stat" +
                       (map.SteamSetStatAbsolute ? " as an absolute counter" : " by accumulating deltas") +
                       " and never calls SetAchievement - configure each achievement on the Steamworks partner site to unlock when its stat reaches the tier value." + ignored;
            }

            int explicitNames = map.Entries.Count(e => !string.IsNullOrEmpty(e.SteamAchievement));
            return "Explicit achievement: on completion the game calls SetAchievement with each tier's 'Steam achievement' name " +
                   $"({explicitNames} set; the stat name is used for the rest).";
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

            if (gpgsFound)
                AuditGpgsApplicationId(results);

            var maps = FindAllAchievementTierMaps();
            if (maps.Count > 0)
            {
                var map = maps[0];
                int total = map.Entries.Count;
                var mapPath = AssetDatabase.GetAssetPath(map);
                int missingGoogle = map.CountMissingGooglePlay();
                Add(results, CategoryAndroid, "Google Play Achievement IDs", missingGoogle == 0,
                    $"All {total} achievement tiers have a Google Play ID assigned.",
                    $"{missingGoogle} of {total} achievement tiers are missing a Google Play ID.",
                    "Create achievements in Google Play Console (Play Console > Grow > Play Games Services > Achievements), then paste the alphanumeric IDs into the map asset.",
                    "Select Map Asset", () => Selection.activeObject = map,
                    gpgsFound ? AuditSeverity.Warning : AuditSeverity.Info,
                    whatIsThis: "Each achievement must have its Google Play Console ID configured in your AchievementTierMap so it can unlock on Android.",
                    assetPath: mapPath);
            }

            var code = AnalyzeProjectCode();
            Add(results, CategoryAndroid, "Google Play Games Player Sign-In", code.HasAuthenticate,
                $"Sign-in detected in project code: '{code.AuthenticatePath}'.",
                "No authentication call found: NativeSocial.Authenticate(...) must be called at startup so Google Play Games accepts achievement reports.",
                "Call NativeSocial.Authenticate(success => { ... }) at startup after NativeSocial.Initialize.",
                !string.IsNullOrEmpty(code.InitializePath) ? $"Add Authenticate to {Path.GetFileName(code.InitializePath)}" : null,
                !string.IsNullOrEmpty(code.InitializePath) ? () => AddAuthenticateToScript(code.InitializePath) : (Action)null,
                AuditSeverity.Warning,
                whatIsThis: "Google Play Games ignores or fails achievement reporting if the local user is not signed in.",
                assetPath: code.AuthenticatePath ?? code.InitializePath);
        }

        #endregion

        #region iOS

        private const string GpgsAppIdPattern = "com\\.google\\.android\\.gms\\.games\\.APP_ID\"\\s+android:value=\"([^\"]*)\"";

        /// <summary>
        /// The Play Games application id lives in the manifest generated by the plugin's Android setup. Left empty (value "\u003"), sign-in
        /// fails on the device with no clear message, so this is checked before building.
        /// </summary>
        private static void AuditGpgsApplicationId(List<AuditResult> results)
        {
            var guid = AssetDatabase.FindAssets("AndroidManifest t:DefaultAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.Contains("GooglePlayGamesManifest.androidlib"));

            string appId = null;
            if (guid != null && System.IO.File.Exists(guid))
            {
                var match = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(guid), GpgsAppIdPattern);
                if (match.Success) appId = match.Groups[1].Value;
            }

            var digits = appId == null ? 0 : appId.Count(char.IsDigit);
            Add(results, CategoryAndroid, "Google Play Games Application ID", digits >= 8,
                $"The Play Games application id is set in {guid}.",
                guid == null
                    ? "The plugin's Android setup was never run: there is no GooglePlayGamesManifest.androidlib, so the build has no application id and Google sign-in will fail."
                    : $"The application id in {guid} is empty or invalid (value '{appId}'). Google sign-in will fail on the device.",
                "Run Window > Google Play Games > Setup > Android setup, paste the Android resources from Play Console (Play Games Services > Publishing > Get resources) and press Setup.",
                "Select manifest", () =>
                {
                    if (guid != null) Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(guid);
                },
                AuditSeverity.Fail,
                whatIsThis: "Android's Google Play Games SDK needs your game's application id (a 12-digit project number) in the manifest to talk to Play Games Services. It is written by the plugin's Android setup window.");
        }

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
                var mapPath = AssetDatabase.GetAssetPath(map);
                int missingApple = map.CountMissingApple();
                Add(results, CategoryIOS, "Apple Game Center Achievement IDs", missingApple == 0,
                    $"All {total} achievement tiers have an Apple Game Center ID assigned.",
                    $"{missingApple} of {total} achievement tiers are missing an Apple Game Center ID.",
                    "Create achievements in App Store Connect (Features > Game Center > Achievements) and paste their IDs into the map asset.",
                    "Select Map Asset", () => Selection.activeObject = map,
                    AuditSeverity.Info,
                    whatIsThis: "Each achievement must have its App Store Connect ID configured in your AchievementTierMap to unlock on iOS.",
                    assetPath: mapPath);
            }

            var code = AnalyzeProjectCode();
            Add(results, CategoryIOS, "Game Center Player Authentication", code.HasAuthenticate,
                $"Authentication detected in project code: '{code.AuthenticatePath}'.",
                "No authentication call found: NativeSocial.Authenticate(...) must be called at startup so Game Center accepts achievement updates and unlocks.",
                "Call NativeSocial.Authenticate(success => { ... }) at startup after NativeSocial.Initialize.",
                !string.IsNullOrEmpty(code.InitializePath) ? $"Add Authenticate to {Path.GetFileName(code.InitializePath)}" : null,
                !string.IsNullOrEmpty(code.InitializePath) ? () => AddAuthenticateToScript(code.InitializePath) : (Action)null,
                AuditSeverity.Warning,
                whatIsThis: "On iOS, calling Social.ReportProgress without authenticating first fails with GKErrorNotAuthenticated, leaving achievements locked at 0%.",
                assetPath: code.AuthenticatePath ?? code.InitializePath);

            Add(results, CategoryIOS, "Game Center In-Game Achievements Button", code.HasShowAchievementsUI,
                $"NativeSocial.ShowAchievementsUI() detected in project code: '{code.ShowAchievementsUIPath}'.",
                "Best Practice: No call to NativeSocial.ShowAchievementsUI() detected. Apple HIG recommends placing a Game Center achievements button in your Settings/Options or Main Menu so players can view their achievements and progress directly.",
                "Add a trophy/achievements button in your UI calling NativeSocial.ShowAchievementsUI().",
                null, null,
                AuditSeverity.Info,
                whatIsThis: "ShowAchievementsUI() opens the native iOS Game Center modal overlay, showing all unlocked and locked achievements with progress bars.",
                assetPath: code.ShowAchievementsUIPath);
        }

        #endregion

        #region Helpers

        public static void OpenAssetOrFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null)
            {
                AssetDatabase.OpenAsset(obj);
                EditorGUIUtility.PingObject(obj);
                return;
            }

            if (File.Exists(path))
            {
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 1);
            }
        }

        internal static List<AchievementTierMap> FindAllAchievementTierMaps() =>
            AssetDatabase.FindAssets("t:AchievementTierMap")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(p => !p.StartsWith("Packages/com.wagenheimer.nativesocial/Tests", StringComparison.OrdinalIgnoreCase))
                .Select(p => AssetDatabase.LoadAssetAtPath<AchievementTierMap>(p))
                .Where(m => m != null)
                .OrderByDescending(m => AssetDatabase.GetAssetPath(m).StartsWith("Assets/Resources", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(m => AssetDatabase.GetAssetPath(m).StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .ToList();

        internal static bool IsPackageRegistered(string packageId)
        {
            try
            {
                return UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Any(p => p.name == packageId);
            }
            catch
            {
                return false;
            }
        }

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
            string whatIsThis = null, string assetPath = null)
        {
            var result = Result(category, title, pass ? AuditSeverity.Pass : failSeverity, pass ? passDetail : failDetail, whatIsThis);
            result.AssetPath = assetPath;
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
