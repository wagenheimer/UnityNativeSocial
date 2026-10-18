using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wagenheimer.NativeSocial
{
    /// <summary>
    /// Google Play Games configuration checks and the diagnostics block (package, Play Games app id, signing
    /// SHA-1, installer, Play services versions) logged before sign-in.
    /// </summary>
    public static partial class NativeSocial
    {
#if UNITY_ANDROID
        private const string GpgsAppIdMetaData = "com.google.android.gms.games.APP_ID";
        private const int PackageManagerGetMetaData = 128;
        private const int MinimumAppIdDigits = 8;

        /// <summary>
        /// Null when the built app has a plausible Google Play Games application id in its manifest meta-data; otherwise what is wrong.
        /// The id is written there by "Window > Google Play Games > Setup > Android setup"; an empty value is the most common reason sign-in never works.
        /// </summary>
        internal static string FindAndroidAppIdProblem()
        {
#if UNITY_EDITOR
            return null;
#else
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    var value = ReadAndroidAppId(packageManager, activity.Call<string>("getPackageName"));
                    if (string.IsNullOrEmpty(value))
                        return $"the manifest has no '{GpgsAppIdMetaData}' meta-data. Run Window > Google Play Games > Setup > Android setup and rebuild.";

                    var digits = 0;
                    foreach (var c in value)
                        if (char.IsDigit(c)) digits++;

                    return digits >= MinimumAppIdDigits
                        ? null
                        : $"the Play Games application id in the manifest is empty or invalid (value '{value}'). Run Window > Google Play Games > Setup > Android setup with your Play Console resources and rebuild.";
                }
            }
            catch (Exception ex)
            {
                return "could not read the manifest meta-data: " + ex.Message;
            }
#endif
        }

        private static string ReadAndroidAppId(AndroidJavaObject packageManager, string package)
        {
            using (var info = packageManager.Call<AndroidJavaObject>("getApplicationInfo", package, PackageManagerGetMetaData))
            using (var metaData = info.Get<AndroidJavaObject>("metaData"))
                return metaData != null ? metaData.Call<string>("getString", GpgsAppIdMetaData) : null;
        }

        private const int PackageManagerGetSignatures = 64;
        private const int PackageManagerGetSigningCertificates = 0x08000000;
        private const int AndroidApiPie = 28;
        private const int AndroidApiR = 30;
        private const string PlayStoreInstaller = "com.android.vending";

        /// <summary>
        /// A Canceled from ManuallyAuthenticate faster than this means Google refused the app before showing any UI
        /// (nobody taps "cancel" that fast), which points at configuration, not the player.
        /// </summary>
        private const float QuickCancelSeconds = 2f;

        private static bool _androidDiagnosticsLogged;

        /// <summary>Once per session, so the identity Google checks sits next to the sign-in result in logcat.</summary>
        private static void LogAndroidDiagnosticsOnce()
        {
            if (_androidDiagnosticsLogged) return;
            _androidDiagnosticsLogged = true;
            Debug.Log("[NativeSocial] GPGS diagnostics:\n" + GetAndroidDiagnostics());
        }

        private static string ReadSigningSha1(AndroidJavaObject packageManager, string package, int sdk)
        {
            try
            {
                AndroidJavaObject[] signatures;
                if (sdk >= AndroidApiPie)
                {
                    using (var info = packageManager.Call<AndroidJavaObject>("getPackageInfo", package, PackageManagerGetSigningCertificates))
                    using (var signingInfo = info.Get<AndroidJavaObject>("signingInfo"))
                        signatures = signingInfo.Call<AndroidJavaObject[]>("getApkContentsSigners");
                }
                else
                {
                    using (var info = packageManager.Call<AndroidJavaObject>("getPackageInfo", package, PackageManagerGetSignatures))
                        signatures = info.Get<AndroidJavaObject[]>("signatures");
                }

                if (signatures == null || signatures.Length == 0) return "(no signatures returned)";

                var fingerprints = new List<string>();
                foreach (var signature in signatures)
                {
                    using (signature)
                    {
                        var raw = signature.Call<sbyte[]>("toByteArray");
                        var bytes = new byte[raw.Length];
                        Buffer.BlockCopy(raw, 0, bytes, 0, raw.Length);
                        using (var sha1 = System.Security.Cryptography.SHA1.Create())
                            fingerprints.Add(BitConverter.ToString(sha1.ComputeHash(bytes)).Replace('-', ':'));
                    }
                }
                return string.Join(" | ", fingerprints);
            }
            catch (Exception ex)
            {
                return "(could not read: " + ex.Message + ")";
            }
        }

        private static string ReadInstaller(AndroidJavaObject packageManager, string package, int sdk)
        {
            try
            {
                string installer;
                if (sdk >= AndroidApiR)
                {
                    using (var source = packageManager.Call<AndroidJavaObject>("getInstallSourceInfo", package))
                        installer = source.Call<string>("getInstallingPackageName");
                }
                else
                {
                    installer = packageManager.Call<string>("getInstallerPackageName", package);
                }

                if (installer == PlayStoreInstaller) return installer + " (Google Play: signed with the Play app-signing key)";
                return (string.IsNullOrEmpty(installer) ? "(none)" : installer) +
                       " (NOT Google Play: adb/bundletool/sideload, signed with whatever key built it - that key's SHA-1 must be registered too)";
            }
            catch (Exception ex)
            {
                return "(could not read: " + ex.Message + ")";
            }
        }

        private static string ReadVersionName(AndroidJavaObject packageManager, string package)
        {
            try
            {
                using (var info = packageManager.Call<AndroidJavaObject>("getPackageInfo", package, 0))
                    return info.Get<string>("versionName");
            }
            catch (Exception)
            {
                return "(not installed or not visible to this app)";
            }
        }
#endif

        /// <summary>
        /// (Android) What Google checks before letting this install sign in to Play Games: package name, Play Games
        /// application id, the SHA-1 of the certificate that signed THIS install, who installed it, and the Google Play
        /// services / Play Games versions. Compare the SHA-1 with Play Console &gt; Play Games Services &gt; Configuration &gt;
        /// Credentials: an install signed with an unregistered key (adb/bundletool debug key, upload key while only the
        /// app-signing key is registered...) fails sign-in with Canceled/DeveloperError without showing any UI.
        /// Returns a short notice on other platforms and in the Editor.
        /// </summary>
        public static string GetAndroidDiagnostics()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var report = new System.Text.StringBuilder();
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    var sdk = version.GetStatic<int>("SDK_INT");
                    var package = activity.Call<string>("getPackageName");
                    var appId = ReadAndroidAppId(packageManager, package);

                    report.AppendLine($"  package:               {package}");
                    report.AppendLine($"  Play Games app id:     {(string.IsNullOrEmpty(appId) ? "(missing)" : appId)}");
                    report.AppendLine($"  signing cert SHA-1:    {ReadSigningSha1(packageManager, package, sdk)}");
                    report.AppendLine($"  installer:             {ReadInstaller(packageManager, package, sdk)}");
                    report.AppendLine($"  Google Play services:  {ReadVersionName(packageManager, "com.google.android.gms")}");
                    report.AppendLine($"  Google Play Games app: {ReadVersionName(packageManager, "com.google.android.play.games")}");
                    report.AppendLine($"  device:                {SystemInfo.deviceModel}, Android API {sdk}, app {Application.version}");
                    report.Append("  -> the SHA-1 above must be listed for this package under Play Console > Play Games Services > Configuration > Credentials.\n");
                    report.Append("  -> capture a fresh sign-in attempt with: adb logcat -d -v time -s Unity 'PlayGamesServices[SignInAuthenticator]' SignInChimeraActivity");                }
            }
            catch (Exception ex)
            {
                report.Append("  diagnostics failed: " + ex.Message);
            }
            return report.ToString();
#else
            return "Android diagnostics are only available on an Android device.";
#endif
        }
    }
}
