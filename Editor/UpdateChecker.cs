using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Wagenheimer.NativeSocial.Editor
{
    [InitializeOnLoad]
    internal static class UpdateChecker
    {
        private const string PkgName = "com.wagenheimer.nativesocial";
        private const string RepoUrl = "https://github.com/wagenheimer/UnityNativeSocial";
        private const string RawPkgUrl = RepoUrl + "/main/package.json";
        private const string RawChangelogUrl = RepoUrl + "/main/CHANGELOG.md";
        private const string EditorPrefsKey = "Wagenheimer_NativeSocial_LastUpdateCheck";
        private const string SkipVersionKey = "Wagenheimer_NativeSocial_SkipVersion";
        private const double CheckIntervalHours = 24;

        static UpdateChecker()
        {
            EditorApplication.delayCall += Check;
        }

        [MenuItem("Tools/Wagenheimer/Native Social/Check for Updates...", priority = 41)]
        private static void MenuCheck()
        {
            Check();
        }

        private static async void Check()
        {
            var lastCheck = EditorPrefs.GetString(EditorPrefsKey, "");
            if (!string.IsNullOrEmpty(lastCheck))
            {
                if (DateTime.TryParse(lastCheck, out var dt) && (DateTime.UtcNow - dt).TotalHours < CheckIntervalHours)
                    return;
            }

            var localVersion = GetLocalVersion();
            if (localVersion == null) return;

            try
            {
                using var wc = new WebClient();
                var json = await wc.DownloadStringTaskAsync(RawPkgUrl);
                var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (!match.Success) return;

                var remoteVersion = new System.Version(match.Groups[1].Value);
                if (remoteVersion <= localVersion) return;

                var skipVersion = EditorPrefs.GetString(SkipVersionKey, "");
                if (skipVersion == remoteVersion.ToString()) return;

                string changelogNotes = "";
                try
                {
                    var changelog = await wc.DownloadStringTaskAsync(RawChangelogUrl);
                    var notesMatch = Regex.Match(changelog, $"##\\s*\\[?{remoteVersion}\\]?[^\\n]*\\n(.*?)(?=\\n##|\\Z)", RegexOptions.Singleline);
                    if (notesMatch.Success) changelogNotes = notesMatch.Groups[1].Value.Trim();
                }
                catch { }

                EditorPrefs.SetString(EditorPrefsKey, DateTime.UtcNow.ToString("O"));
                UpdateAvailableWindow.Show(localVersion, remoteVersion, changelogNotes);
            }
            catch { }
        }

        private static Version GetLocalVersion()
        {
            var info = PackageInfo.FindForAssembly(typeof(NativeSocial).Assembly);
            return info != null && Version.TryParse(info.version, out var v) ? v : null;
        }
    }
}
