using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Wagenheimer.NativeSocial
{
    /// <summary>Apple Game Center backend (iOS, and macOS outside Steam builds) of <see cref="NativeSocial"/>.</summary>
    public static partial class NativeSocial
    {
        /// <summary>Entitlement macOS/iOS needs before <c>GKLocalPlayer</c> will authenticate.</summary>
        internal const string AppleGameCenterEntitlement = "com.apple.developer.game-center";

#if UNITY_IOS || (UNITY_STANDALONE_OSX && !WAGENHEIMER_NATIVESOCIAL_STEAM)
        /// <summary>Reports the mapped Game Center achievement as a 0-100% completion percentage.</summary>
        /// <param name="locId">Game-defined achievement key, looked up in <see cref="_iosMap"/> to find the Game Center achievement ID.</param>
        /// <param name="current">Current progress count (e.g. 5 wildcards collected so far). Combined with <paramref name="total"/> to compute the percentage Game Center expects.</param>
        /// <param name="total">Progress count needed to fully complete the achievement (e.g. 20 wildcards). Must be &gt; 0 or the report is skipped.</param>
        /// <param name="completed">If true, reports 100% regardless of <paramref name="current"/>/<paramref name="total"/>.</param>
        private static void ReportIOS(string locId, int current, int total, bool completed)
        {
            if (locId == null) { OnLog?.Invoke("[NativeSocial] ReportIOS skipped: locId is null."); return; }
            // _iosMap is null until Initialize: Authenticate can run before it (and its success callback
            // flushes queued progress), so never dereference the map directly.
            if (_iosMap == null || !_iosMap.TryGetValue(locId, out var gcId)) { OnLog?.Invoke($"[NativeSocial] ReportIOS('{locId}') skipped: no Game Center achievement id mapped for this LocID."); return; }
            if (total <= 0) { OnLog?.Invoke($"[NativeSocial] ReportIOS('{locId}') skipped: total must be > 0 (got {total})."); return; }

            // Not signed in yet (auth still running, player declined, offline...): Game Center would just fail.
            // Keep the highest progress and deliver it from the next successful Authenticate.
            if (!IsAuthenticated)
            {
                QueuePendingProgress(locId, current, total, completed, "Game Center");
                return;
            }

            // Game Center achievements are percentage-based rather than increment-based,
            // so we derive a percent from current/total instead of using delta directly.
            double percent = completed
                ? 100.0
                : Math.Min((double)current / total * 100.0, 99.0);

            OnLog?.Invoke($"[NativeSocial] iOS ReportProgress('{gcId}') {percent:F1}% for '{locId}'. Game Center shows this as a progress bar until it reaches 100%.");
            Social.ReportProgress(gcId, percent, success =>
            {
                if (success)
                {
                    OnLog?.Invoke($"[NativeSocial] iOS ReportProgress SUCCESS for '{gcId}' ({percent:F1}%)");
                }
                else
                {
                    string err = $"[NativeSocial] iOS ReportProgress FAILED for '{gcId}' ({percent:F1}%). Check if player is authenticated or if '{gcId}' matches the Game Center Achievement ID (vendorIdentifier in App Store Connect).";
                    Debug.LogWarning(err);
                    OnLog?.Invoke(err);
                }
            });
        }

        /// <summary>Re-reports 100% progress for every already-completed achievement on Game Center.</summary>
        /// <param name="completedLocIds">LocIDs of achievements to re-report, looked up in <see cref="_iosMap"/> to find each Game Center achievement ID.</param>
        private static void SyncCompletedIOS(IEnumerable<string> completedLocIds)
        {
            foreach (var locId in completedLocIds)
            {
                if (locId == null) continue;
                if (_iosMap != null && _iosMap.TryGetValue(locId, out var gcId))
                    Social.ReportProgress(gcId, 100.0, ok => { if (!ok) Debug.LogWarning($"[NativeSocial] Game Center re-sync of '{gcId}' ({locId}) FAILED."); });
            }
        }

        /// <summary>
        /// A full diagnostic snapshot of Apple Game Center on this machine/build. Log it before a sign-in
        /// attempt and on every failure: on macOS the reason is almost never in the game code — it is the
        /// code signature (com.apple.developer.game-center entitlement), the embedded provisioning profile,
        /// or the app not being installed from the App Store / TestFlight.
        /// </summary>
        public static string GetAppleDiagnostics()
        {
            var report = new StringBuilder();
            try
            {
                report.AppendLine($"platform={Application.platform} os={SystemInfo.operatingSystem} unity={Application.unityVersion}");
                report.AppendLine($"app='{Application.productName}' version={Application.version} bundleId={Application.identifier}");
                report.AppendLine($"bundle={AppleBundleRoot()}");

                if (Application.platform == RuntimePlatform.OSXPlayer)
                {
                    report.AppendLine($"masReceipt={AppleHasMacAppStoreReceipt()} codeSignature={AppleHasCodeSignature()} " +
                                      $"translocated={IsAppleTranslocated()} dataPath={Application.dataPath}");
                }

                report.AppendLine($"profile={DescribeAppleProfile()}");
                report.AppendLine(DescribeAppleSocialState());
                report.AppendLine($"nativeSocial: initialized={IsInitialized} iosMap={IosMap.Count} pending={PendingProgressCount}");
                report.AppendLine($"lastAuthReport={LastAuthenticationReport ?? "(null)"}");
            }
            catch (Exception ex)
            {
                report.AppendLine($"diagnostics failed: {ex.GetType().Name}: {ex.Message}");
            }

            return report.ToString().TrimEnd();
        }

        /// <summary>
        /// (macOS) Extra explanation appended to a Game Center failure. Says exactly which signing/profile
        /// requirement is missing instead of the vague error GameKit returns.
        /// </summary>
        internal static string MacGameCenterHint()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if (!File.Exists(AppleProfilePath()))
            {
                return " This Mac build has no embedded provisioning profile (Contents/embedded.provisionprofile), " +
                       "so it was signed WITHOUT the Game Center entitlement and cannot sign in. Sign it with a " +
                       "Mac App Store provisioning profile whose App ID has the Game Center capability.";
            }

            if (!AppleProfileHasEntitlement())
            {
                return " The embedded provisioning profile does not contain com.apple.developer.game-center: " +
                       "enable Game Center for this App ID (Apple Developer / App Store Connect) and regenerate the Mac App Store profile.";
            }

            if (!AppleProfileHasBundleId())
            {
                return $" The embedded provisioning profile is not for this bundle id ({Application.identifier}): " +
                       "the profile's App ID and the build's bundle id must match.";
            }

            if (!AppleHasMacAppStoreReceipt())
            {
                return " The build has the Game Center entitlement but has no Contents/_MASReceipt/receipt, so it was not " +
                       "installed from the App Store / TestFlight. On macOS Game Center only works for an Apple-distributed copy; " +
                       "a local/ad-hoc copy fails even when signed correctly.";
            }

            return " The build looks correctly signed for Game Center. Check that the player is signed in to Game Center " +
                   "in System Settings and that Game Center is enabled for this app in App Store Connect.";
#else
            return string.Empty;
#endif
        }

        private static string DescribeAppleProfile()
        {
            try
            {
                var path = AppleProfilePath();
                var name = Application.platform == RuntimePlatform.OSXPlayer
                    ? "embedded.provisionprofile"
                    : "embedded.mobileprovision";

                if (!File.Exists(path))
                    return $"{name}=MISSING (build signed without a provisioning profile -> no Game Center entitlement)";

                var bytes = File.ReadAllBytes(path);
                var text = Encoding.UTF8.GetString(bytes);
                bool entitlement = text.IndexOf(AppleGameCenterEntitlement, StringComparison.Ordinal) >= 0;
                bool bundleId = !string.IsNullOrEmpty(Application.identifier) &&
                                text.IndexOf(Application.identifier, StringComparison.Ordinal) >= 0;

                return $"{name}={path} ({bytes.Length} bytes, {AppleGameCenterEntitlement}=" +
                       $"{(entitlement ? "present" : "MISSING")}, bundleId={(bundleId ? "match" : "NOT FOUND")})";
            }
            catch (Exception ex)
            {
                return $"profile unreadable ({ex.GetType().Name}: {ex.Message})";
            }
        }

        private static string DescribeAppleSocialState()
        {
            try
            {
                if (Social.Active == null)
                    return "social: Social.Active=NULL (Unity registered no social platform for this build)";

                var user = Social.localUser;
                if (user == null)
                    return $"social: Social.Active={Social.Active.GetType().FullName} localUser=NULL";

                return $"social: Social.Active={Social.Active.GetType().FullName} authenticated={user.authenticated} " +
                       $"id='{user.id}' user='{user.userName}' state={user.state}";
            }
            catch (Exception ex)
            {
                return $"social: state unreadable ({ex.GetType().Name}: {ex.Message})";
            }
        }

        /// <summary>macOS: <c>Application.dataPath</c> is "&lt;App&gt;.app/Contents"; iOS: "&lt;App&gt;.app/Data".</summary>
        private static string AppleBundleRoot()
        {
            var dataPath = Application.dataPath;
            if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.IPhonePlayer)
                return Path.GetDirectoryName(dataPath) ?? dataPath;
            return dataPath;
        }

        private static string AppleProfilePath()
        {
            var root = AppleBundleRoot();
            return Application.platform == RuntimePlatform.OSXPlayer
                ? Path.Combine(root, "Contents", "embedded.provisionprofile")
                : Path.Combine(root, "embedded.mobileprovision");
        }

        private static bool AppleProfileHasEntitlement()
        {
            try
            {
                var path = AppleProfilePath();
                return File.Exists(path) &&
                       Encoding.UTF8.GetString(File.ReadAllBytes(path))
                           .IndexOf(AppleGameCenterEntitlement, StringComparison.Ordinal) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool AppleProfileHasBundleId()
        {
            try
            {
                var path = AppleProfilePath();
                return File.Exists(path) && !string.IsNullOrEmpty(Application.identifier) &&
                       Encoding.UTF8.GetString(File.ReadAllBytes(path))
                           .IndexOf(Application.identifier, StringComparison.Ordinal) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool AppleHasMacAppStoreReceipt()
        {
            try
            {
                return Application.platform == RuntimePlatform.OSXPlayer &&
                       File.Exists(Path.Combine(AppleBundleRoot(), "Contents", "_MASReceipt", "receipt"));
            }
            catch
            {
                return false;
            }
        }

        private static bool AppleHasCodeSignature()
        {
            try
            {
                return File.Exists(Path.Combine(AppleBundleRoot(), "Contents", "_CodeSignature", "CodeResources"));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAppleTranslocated()
        {
            try
            {
                return Application.dataPath.IndexOf("AppTranslocation", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
#else
        /// <summary>Diagnostics exist only on Apple builds; returns an empty string elsewhere.</summary>
        public static string GetAppleDiagnostics() => string.Empty;

        /// <summary>Only macOS/iOS builds can lack the Game Center signing requirements.</summary>
        internal static string MacGameCenterHint() => string.Empty;
#endif
    }
}
