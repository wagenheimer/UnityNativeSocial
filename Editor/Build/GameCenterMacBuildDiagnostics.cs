#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Build-time counterpart of <c>NativeSocial.GetAppleDiagnostics()</c> for macOS.
    ///
    /// Unity never adds the Game Center entitlement to a macOS build: the .app has to be signed with
    /// <c>com.apple.developer.game-center</c> AND carry a Mac App Store provisioning profile for the same
    /// App ID. When that is forgotten, the built game fails the Game Center sign-in with a vague error and
    /// nothing in the Editor warns about it. This post-process inspects the produced .app and says exactly
    /// which piece is missing before anyone uploads or runs it.
    ///
    /// It never changes the build or signs anything: the Mac App Store signing step is intentionally left
    /// to the release workflow (Xcode / codesign / the store tooling).
    /// </summary>
    public sealed class GameCenterMacBuildDiagnostics : IPostprocessBuildWithReport
    {
        private const string GameCenterEntitlement = "com.apple.developer.game-center";

        public int callbackOrder => 10002;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX) return;

            var appPath = FindAppBundle(report.summary.outputPath);
            if (appPath == null)
            {
                Debug.LogWarning($"[NativeSocial] macOS Game Center check skipped: no .app bundle found at '{report.summary.outputPath}'.");
                return;
            }

            var profilePath = Path.Combine(appPath, "Contents", "embedded.provisionprofile");
            var builtBundleId = ReadBundleIdentifier(Path.Combine(appPath, "Contents", "Info.plist"));
            var playerBundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            var bundleId = string.IsNullOrEmpty(builtBundleId) ? playerBundleId : builtBundleId;

            Debug.Log($"[NativeSocial] macOS Game Center check for '{Path.GetFileName(appPath)}' (bundle id: {bundleId}).");

            if (!File.Exists(profilePath))
            {
                Debug.LogWarning(
                    "[NativeSocial] This macOS .app has NO embedded provisioning profile (Contents/embedded.provisionprofile). " +
                    "It was signed without the Game Center entitlement, so GKLocalPlayer will not authenticate and the game will report " +
                    "a Game Center sign-in failure. Sign it with a Mac App Store provisioning profile whose App ID has Game Center enabled. " +
                    "A local/ad-hoc build cannot sign in even when the code is correct.");
                return;
            }

            string profile;
            try
            {
                profile = File.ReadAllText(profilePath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NativeSocial] Could not read '{profilePath}': {ex.Message}");
                return;
            }

            var hasEntitlement = profile.IndexOf(GameCenterEntitlement, System.StringComparison.Ordinal) >= 0;
            var hasBundleId = !string.IsNullOrEmpty(bundleId) &&
                              profile.IndexOf(bundleId, System.StringComparison.Ordinal) >= 0;

            if (!hasEntitlement)
            {
                Debug.LogWarning(
                    $"[NativeSocial] The embedded provisioning profile does NOT contain {GameCenterEntitlement}. " +
                    "Enable Game Center for this App ID in Apple Developer / App Store Connect and regenerate the Mac App Store profile; " +
                    "without it the app cannot sign in to Game Center.");
            }
            else if (!hasBundleId)
            {
                Debug.LogWarning(
                    $"[NativeSocial] The embedded provisioning profile does not match this bundle id ('{bundleId}'). " +
                    "The profile's App ID and the build's bundle id must be identical.");
            }
            else
            {
                Debug.Log(
                    $"[NativeSocial] macOS Game Center signing looks correct: {GameCenterEntitlement} present and the profile matches '{bundleId}'. " +
                    "Remember that Game Center only works for a copy installed from the App Store / TestFlight.");
            }
        }

        private static string FindAppBundle(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath)) return null;
            if (Directory.Exists(outputPath) && outputPath.EndsWith(".app", System.StringComparison.OrdinalIgnoreCase))
                return outputPath;
            if (!Directory.Exists(outputPath)) return null;

            var apps = Directory.GetDirectories(outputPath, "*.app");
            return apps.Length > 0 ? apps[0] : null;
        }

        private static string ReadBundleIdentifier(string infoPlistPath)
        {
            try
            {
                if (!File.Exists(infoPlistPath)) return null;
                var match = Regex.Match(File.ReadAllText(infoPlistPath),
                    @"<key>\s*CFBundleIdentifier\s*</key>\s*<string>([^<]+)</string>");
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif
