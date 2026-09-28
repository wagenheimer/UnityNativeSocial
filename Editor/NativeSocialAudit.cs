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
        private const string CategoryPlatforms = "Platform SDKs";
        private const string CategoryBootstrap = "Initialization";
        private const string CategoryAchievements = "Achievement Mapping";

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

            AuditPlatformSdks(results);
            AuditBootstrap(results);
            AuditAchievementMapping(results);
            AttachPrompts(results);

            return results;
        }

        #region Platform SDKs

        private static void AuditPlatformSdks(List<AuditResult> results)
        {
            bool gpgsFound = IsTypeAvailable("GooglePlayGames.PlayGamesPlatform");
            Add(results, CategoryPlatforms, "Google Play Games (Android)", gpgsFound,
                "PlayGamesPlatform detected in the project.",
                "Google Play Games plugin not detected: Android achievements/leaderboards will silently no-op.",
                "Install the official Google Play Games Plugin for Unity (v2, GDK-based).", "Get the plugin",
                () => Application.OpenURL("https://github.com/playgameservices/play-games-plugin-for-unity"),
                AuditSeverity.Info);

            bool isIos = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
            Add(results, CategoryPlatforms, "Game Center (iOS)", isIos || true,
                isIos ? "Active build target is iOS." : "Game Center is built into Unity/iOS; switches automatically when the build target is iOS.",
                null, null, failSeverity: AuditSeverity.Info);

            bool steamFound = IsTypeAvailable("Steamworks.SteamUserStats");
            bool steamDefine = HasDefine("WAGENHEIMER_NATIVESOCIAL_STEAM");
            Add(results, CategoryPlatforms, "Steamworks.NET (Steam)", !steamFound || steamDefine,
                steamFound ? "Steamworks.NET detected and WAGENHEIMER_NATIVESOCIAL_STEAM is active." : "Steamworks.NET not installed (only needed for Steam builds).",
                "Steamworks.NET is installed but WAGENHEIMER_NATIVESOCIAL_STEAM is not defined: Steam achievement calls will compile out.",
                "Add the define to the active build target's Scripting Define Symbols.", "Add Define",
                () => SetDefine("WAGENHEIMER_NATIVESOCIAL_STEAM", true), AuditSeverity.Warning);

            bool gpgsDefine = HasDefine("WAGENHEIMER_NATIVESOCIAL_GPGS");
            if (gpgsFound)
            {
                Add(results, CategoryPlatforms, "GPGS scripting define active", gpgsDefine,
                    "WAGENHEIMER_NATIVESOCIAL_GPGS is active (auto-set by the com.google.play.games version define).",
                    "Google Play Games plugin found but WAGENHEIMER_NATIVESOCIAL_GPGS is not defined — verify it's installed as a package (not loose Assets/ files), since versionDefines only fires for a resolvable package version.",
                    failSeverity: AuditSeverity.Warning);
            }
        }

        #endregion

        #region Bootstrap

        private static void AuditBootstrap(List<AuditResult> results)
        {
            bool hasBootstrap = AssetDatabase.FindAssets("NativeSocialBootstrap t:MonoScript").Length > 0;
            Add(results, CategoryBootstrap, "Project bootstrap script", hasBootstrap,
                "NativeSocialBootstrap found in project assets.",
                "No bootstrap script found: NativeSocial.Initialize(...) must be called once at startup, or every Report/SubmitScore call silently no-ops.",
                "Creates a starter NativeSocialBootstrap.cs from the package sample.", "Generate NativeSocialBootstrap.cs",
                CreateBootstrapScriptAsset, AuditSeverity.Warning);
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

            const string samplePath = "Packages/com.wagenheimer.nativesocial/Samples~/DefaultSetup/NativeSocialBootstrap.cs";
            string content = File.Exists(samplePath)
                ? File.ReadAllText(samplePath)
                : @"using UnityEngine;
using Wagenheimer.NativeSocial;

public class NativeSocialBootstrap : MonoBehaviour
{
    private void Awake()
    {
        var androidMap = new System.Collections.Generic.Dictionary<string, string>();
        var iosMap = new System.Collections.Generic.Dictionary<string, string>();
        var steamMap = new System.Collections.Generic.Dictionary<string, SteamEntry>();

        NativeSocial.Initialize(androidMap, iosMap, steamMap);
        Debug.Log(""[NativeSocial] Initialized from Bootstrap."");
    }
}
";

            File.WriteAllText(path, content);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Created", $"NativeSocialBootstrap.cs created at {path}", "OK");
        }

        internal static void AddBootstrapToCurrentScene()
        {
            var go = new GameObject("NativeSocialBootstrap");
            Undo.RegisterCreatedObjectUndo(go, "Create NativeSocialBootstrap");
            Selection.activeGameObject = go;
            Debug.Log("[NativeSocial] Created 'NativeSocialBootstrap' GameObject in active scene.");
        }

        #endregion

        #region Achievement mapping

        private static void AuditAchievementMapping(List<AuditResult> results)
        {
            var maps = FindAllAchievementTierMaps();
            if (maps.Count == 0)
            {
                Add(results, CategoryAchievements, "Achievement Tier Map asset", false,
                    null, "No AchievementTierMap asset found: there is nowhere to keep this game's per-platform achievement IDs.",
                    "Creates an empty AchievementTierMap asset under Assets/.", "Create Achievement Tier Map",
                    CreateAchievementTierMapAsset, AuditSeverity.Warning);
                return;
            }

            if (maps.Count > 1)
                results.Add(Result(CategoryAchievements, "Multiple Achievement Tier Map assets", AuditSeverity.Info,
                    $"{maps.Count} AchievementTierMap assets found; NativeSocial.Initialize should be built from exactly one."));

            foreach (var map in maps)
            {
                var path = AssetDatabase.GetAssetPath(map);
                int total = map.Entries.Count;
                if (total == 0)
                {
                    results.Add(Result(CategoryAchievements, $"'{path}' has entries", AuditSeverity.Warning,
                        "The map asset exists but has zero entries — nothing will be reported to any platform."));
                    continue;
                }

                int missingGoogle = map.CountMissingGooglePlay();
                int missingApple = map.CountMissingApple();
                bool gpgsFound = IsTypeAvailable("GooglePlayGames.PlayGamesPlatform");

                results.Add(Result(CategoryAchievements, $"'{path}': Google Play IDs",
                    missingGoogle == 0 ? AuditSeverity.Pass : (gpgsFound ? AuditSeverity.Warning : AuditSeverity.Info),
                    missingGoogle == 0
                        ? $"All {total} entries have a Google Play achievement ID."
                        : $"{missingGoogle} of {total} entries have no Google Play achievement ID yet (create the achievements in Google Play Console, then paste the IDs in)."));

                results.Add(Result(CategoryAchievements, $"'{path}': Apple Game Center IDs",
                    missingApple == 0 ? AuditSeverity.Pass : AuditSeverity.Info,
                    missingApple == 0
                        ? $"All {total} entries have an Apple Game Center ID."
                        : $"{missingApple} of {total} entries have no Apple Game Center ID yet (create the achievements in App Store Connect, then paste the IDs in)."));
            }
        }

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

        #endregion

        #region Helpers

        private static void Add(List<AuditResult> results, string category, string title, bool pass, string passDetail,
            string failDetail, string hint = null, string fixLabel = null, Action fix = null, AuditSeverity failSeverity = AuditSeverity.Fail)
        {
            var result = Result(category, title, pass ? AuditSeverity.Pass : failSeverity, pass ? passDetail : failDetail);
            if (!pass)
            {
                result.FixHint = hint;
                result.FixLabel = fixLabel;
                result.Fix = fix;
            }
            results.Add(result);
        }

        private static AuditResult Result(string category, string title, AuditSeverity severity, string detail) =>
            new AuditResult { Category = category, Title = title, Severity = severity, Detail = detail };

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
