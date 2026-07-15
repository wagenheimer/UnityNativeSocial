using UnityEditor;
using UnityEngine;
using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Checks GitHub once a day for a newer release of this package and, if one is
    /// found, shows <see cref="UpdateAvailableWindow"/> with the changelog notes.
    /// Runs automatically on editor load and can also be triggered manually from the menu.
    /// </summary>
    [InitializeOnLoad]
    internal static class UpdateChecker
    {
        private const string PkgName = "com.wagenheimer.nativesocial";
        private const string RepoUrl = "https://github.com/wagenheimer/UnityNativeSocial";

        // NOTE: must point at raw.githubusercontent.com, not the github.com repo page —
        // the latter returns an HTML wrapper, not the raw file content, so the regex/parse
        // below would never match. Branch is "master" (this repo's default branch).
        private const string RawPkgUrl = "https://raw.githubusercontent.com/wagenheimer/UnityNativeSocial/master/package.json";
        private const string RawChangelogUrl = "https://raw.githubusercontent.com/wagenheimer/UnityNativeSocial/master/CHANGELOG.md";

        private const string EditorPrefsKey = "Wagenheimer_NativeSocial_LastUpdateCheck";
        private const string SkipVersionKey = "Wagenheimer_NativeSocial_SkipVersion";
        private const double CheckIntervalHours = 24;

        const string PackageDisplayName = "Native Social";

        static UpdateChecker()
        {
            // Defer past editor startup so this never delays domain reload / compilation.
            EditorApplication.delayCall += () => Check(force: false);
        }

        [MenuItem("Tools/Wagenheimer/Native Social/Check for Updates...", priority = 41)]
        private static void MenuCheck()
        {
            Check(force: true);
        }

        private static async void Check(bool force)
        {
            // Throttle to once per CheckIntervalHours so we don't hit GitHub on every editor load.
            // A manual (force) check always bypasses the throttle.
            if (!force)
            {
                var lastCheck = EditorPrefs.GetString(EditorPrefsKey, "");
                if (!string.IsNullOrEmpty(lastCheck))
                {
                    if (DateTime.TryParse(lastCheck, out var dt) && (DateTime.UtcNow - dt).TotalHours < CheckIntervalHours)
                        return;
                }
            }

            var localVersion = GetLocalVersion();
            if (localVersion == null)
            {
                Debug.Log("[NativeSocial] Update check failed: could not resolve the installed package version " +
                    "(PackageInfo.FindForAssembly returned null for this assembly).");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, "Failed to check for updates: could not identify the installed version of this package.", "OK");
                return;
            }

            try
            {
                using var wc = new WebClient();
                var json = await wc.DownloadStringTaskAsync(RawPkgUrl);
                var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (!match.Success)
                {
                    Debug.Log("[NativeSocial] Update check failed: remote package.json has no version field.");
                    if (force)
                        EditorUtility.DisplayDialog(PackageDisplayName, "Failed to check for updates: remote package.json has no version field.", "OK");
                    return;
                }

                var remoteVersion = new System.Version(match.Groups[1].Value);
                if (remoteVersion <= localVersion)
                {
                    Debug.Log($"[NativeSocial] Up to date (installed: {localVersion}).");
                    if (force)
                        EditorUtility.DisplayDialog(PackageDisplayName, $"You are already using the latest version ({localVersion}).", "OK");
                    return;
                }

                // User previously chose "Ignore This Version" for this exact release — respect it,
                // unless this is a manual (force) check, which should always show the result.
                var skipVersion = EditorPrefs.GetString(SkipVersionKey, "");
                if (!force && skipVersion == remoteVersion.ToString())
                {
                    Debug.Log($"[NativeSocial] Version {remoteVersion} available (installed: {localVersion}) but ignored by user preference.");
                    return;
                }

                string changelogNotes = "";
                try
                {
                    var changelog = await wc.DownloadStringTaskAsync(RawChangelogUrl);
                    var notesMatch = Regex.Match(changelog, $"##\\s*\\[?{remoteVersion}\\]?[^\\n]*\\n(.*?)(?=\\n##|\\Z)", RegexOptions.Singleline);
                    if (notesMatch.Success) changelogNotes = notesMatch.Groups[1].Value.Trim();
                }
                catch
                {
                    // Changelog is best-effort only; missing/unreachable notes shouldn't block the update prompt.
                }

                EditorPrefs.SetString(EditorPrefsKey, DateTime.UtcNow.ToString("O"));
                Debug.Log($"[NativeSocial] New version available: {remoteVersion} (installed: {localVersion}). See {RepoUrl}/releases/latest");
                UpdateAvailableWindow.Show(localVersion, remoteVersion, changelogNotes);
            }
            catch (Exception e)
            {
                // Background auto-check failures should stay silent (offline, GitHub down, rate-limited,
                // etc.) — but a manual check should always tell the user something happened.
                Debug.Log($"[NativeSocial] Update check failed: {e.Message}");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, $"Failed to check for updates:\n{e.Message}", "OK");
            }
        }

        /// <summary>Reads the installed package version via UPM, using the assembly that ships with this package.</summary>
        private static Version GetLocalVersion()
        {
            // Fully qualified: UnityEditor also defines a (different, obsolete) PackageInfo type,
            // so an unqualified "PackageInfo" here is ambiguous between the two namespaces (CS0104)
            // when both `using UnityEditor;` and `using UnityEditor.PackageManager;` are in scope.
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NativeSocial).Assembly);
            return info != null && Version.TryParse(info.version, out var v) ? v : null;
        }
    }
}
