using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEditor.Build;

using UnityEngine;
using UnityEngine.Rendering;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// "Recommended project settings": the build/Player Settings that Google Play and Level Up expect but that are easy to
    /// forget because they are not code. These checks run in the Editor (no build needed); UnityBuildPipeline applies the
    /// Android ones again at build time so a build started outside the audit is still correct.
    /// </summary>
    public static partial class NativeSocialAudit
    {
        private const string AndroidMainTemplate = "Assets/Plugins/Android/mainTemplate.gradle";

        private static void AuditRecommendedSettings(List<AuditResult> results)
        {
            AuditAndroidArm64(results);
            AuditAndroidIl2Cpp(results);
            AuditAndroidTargetApi(results);
            AuditAndroidGraphicsApi(results);
            AuditAndroidFragmentVersion(results);
            AuditLargeScreenAspectRatio(results);
        }

        /// <summary>Google Play rejects 32-bit-only uploads.</summary>
        private static void AuditAndroidArm64(List<AuditResult> results)
        {
            var architectures = PlayerSettings.Android.targetArchitectures;
            var hasArm64 = (architectures & AndroidArchitecture.ARM64) != 0;

            Add(results, CategoryRecommended, "Android 64-bit (ARM64)", hasArm64,
                "ARM64 is enabled; the build meets Google Play's 64-bit requirement.",
                $"ARM64 is not enabled (Target Architectures: {architectures}). Google Play does not accept 32-bit-only apps.",
                "Enable ARM64 in Player Settings > Android > Other Settings > Target Architectures (IL2CPP is required).",
                "Enable ARM64", () =>
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                    PlayerSettings.Android.targetArchitectures |= AndroidArchitecture.ARM64;
                },
                AuditSeverity.Fail,
                whatIsThis: "Google Play requires a 64-bit ARM64 build for new uploads; 32-bit-only AABs are rejected.");
        }

        /// <summary>ARM64 exists only with IL2CPP; Mono is 32-bit and deprecated on Android.</summary>
        private static void AuditAndroidIl2Cpp(List<AuditResult> results)
        {
            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            var il2cpp = backend == ScriptingImplementation.IL2CPP;

            Add(results, CategoryRecommended, "Android scripting backend (IL2CPP)", il2cpp,
                "IL2CPP is active (required for ARM64).",
                $"The Android scripting backend is {backend}. ARM64 requires IL2CPP; Mono is 32-bit only and deprecated on Android.",
                "Set Scripting Backend to IL2CPP in Player Settings > Android > Other Settings.",
                "Set IL2CPP", () =>
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                    PlayerSettings.Android.targetArchitectures |= AndroidArchitecture.ARM64;
                },
                AuditSeverity.Fail,
                whatIsThis: "IL2CPP is the only Android backend that produces ARM64; Google Play requires a 64-bit build.");
        }

        /// <summary>Automatic follows the installed SDK; a fixed old level eventually fails Play's target API rule.</summary>
        private static void AuditAndroidTargetApi(List<AuditResult> results)
        {
            var target = PlayerSettings.Android.targetSdkVersion;
            var automatic = target == AndroidSdkVersions.AndroidApiLevelAuto;
            var apiLevel = (int)target;
            var meetsPlay = automatic || apiLevel >= 35;

            Add(results, CategoryRecommended, "Android target API level", meetsPlay,
                automatic
                    ? "Target API level is Automatic (follows the highest installed SDK)."
                    : $"Target API level is API {apiLevel}.",
                $"Target API level is fixed at API {apiLevel}. Google Play requires a recent target API; Automatic follows the installed SDK and avoids this problem on future requirements.",
                "Set Target API Level to Automatic (highest installed) in Player Settings > Android > Other Settings.",
                "Use Automatic", () => PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto,
                AuditSeverity.Warning,
                whatIsThis: "Google Play raises the minimum target API every year; a fixed old value eventually blocks uploads.");
        }

        /// <summary>Level Up LU-VK-GAA: Vulkan primary on devices with 2024+ drivers.</summary>
        private static void AuditAndroidGraphicsApi(List<AuditResult> results)
        {
            try
            {
                var automatic = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
                var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android) ?? Array.Empty<GraphicsDeviceType>();
                var vulkanPrimary = !automatic && apis.Length > 0 && apis[0] == GraphicsDeviceType.Vulkan;
                var ok = automatic || vulkanPrimary;

                Add(results, CategoryRecommended, "Android graphics API (Vulkan)", ok,
                    automatic
                        ? "Auto Graphics API: Unity uses Vulkan as primary on supported devices."
                        : "Vulkan is the primary Android graphics API.",
                    $"Android Graphics APIs are {(automatic ? "Automatic" : string.Join(", ", apis))}; Vulkan is not primary. Level Up LU-VK-GAA expects Vulkan first on devices with updated drivers.",
                    "Set Vulkan first with OpenGLES3 as fallback, or leave Auto Graphics API on (Unity 6 picks Vulkan when available).",
                    "Vulkan first", () =>
                    {
                        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
                    },
                    AuditSeverity.Info,
                    whatIsThis: "Vulkan reduces CPU overhead and is a Level Up requirement for supported drivers; OpenGL ES 3 remains as the fallback for older devices.");
            }
            catch (Exception e)
            {
                Add(results, CategoryRecommended, "Android graphics API (Vulkan)", false,
                    string.Empty,
                    $"Could not read the Android graphics APIs: {e.Message}",
                    "Open Player Settings > Android > Other Settings and confirm the Graphics APIs manually.",
                    failSeverity: AuditSeverity.Info,
                    whatIsThis: "The check could not run on this machine; the project may be missing the Android build support module.");
            }
        }

        /// <summary>Play Console flags the transitive androidx.fragment 1.1.0 unless the app overrides it.</summary>
        private static void AuditAndroidFragmentVersion(List<AuditResult> results)
        {
            if (!File.Exists(AndroidMainTemplate))
            {
                Add(results, CategoryRecommended, "androidx.fragment version", true,
                    "No custom mainTemplate.gradle: AndroidX versions come from Unity's defaults.",
                    string.Empty,
                    whatIsThis: "Play Console reports androidx.fragment as an outdated SDK when it resolves to 1.1.0 through play-services-basement.");
                return;
            }

            string text = null;
            try { text = File.ReadAllText(AndroidMainTemplate); } catch { }

            var match = text != null ? Regex.Match(text, @"androidx\.fragment:fragment:([0-9]+(?:\.[0-9]+)+)") : Match.Empty;
            Version version = null;
            if (match.Success)
                Version.TryParse(match.Groups[1].Value, out version);

            var ok = version != null && version >= new Version(1, 2, 1);

            Add(results, CategoryRecommended, "androidx.fragment version", ok,
                $"androidx.fragment is pinned to {version} (1.2.1 or newer).",
                version == null
                    ? "No androidx.fragment override in mainTemplate.gradle. Play Console flags the transitive 1.1.0 pulled by play-services-basement 18.9.0."
                    : $"androidx.fragment is pinned to {version}; Google Play requires 1.2.1 or newer.",
                "Add implementation 'androidx.fragment:fragment:1.8.9' to mainTemplate.gradle, and keep the Assets/Editor resolver XML so Force Resolve does not drop it.",
                "Open mainTemplate", () => OpenAssetOrFile(AndroidMainTemplate),
                AuditSeverity.Warning,
                whatIsThis: "The fragment version is transitive; without an explicit override the app ships 1.1.0 and Play Console reports it as outdated.");
        }
    }
}
