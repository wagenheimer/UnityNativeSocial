using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEditor;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Platform-level checks that are not about achievements: the Android large-screen aspect ratio (Level Up LU-LS-GAC)
    /// and the iOS Game Center capability that the package adds to the generated Xcode project.
    /// </summary>
    public static partial class NativeSocialAudit
    {
        private const int AspectRatioModeCustom = 2;
        private const float AspectRatioMin = 1.32f;
        private const float AspectRatioMax = 2.34f;

        /// <summary>
        /// Level Up LU-LS-GAC: landscape games must render full screen at 4:3, 16:10 and 21:9. Custom mode with
        /// 1.32-2.34 keeps 4:3 (1.333) and 21:9 (2.333) inside the range instead of letterboxing them.
        /// </summary>
        private static void AuditLargeScreenAspectRatio(List<AuditResult> results)
        {
            var mode = ReadAspectRatioMode();
            var min = PlayerSettings.Android.minAspectRatio;
            var max = PlayerSettings.Android.maxAspectRatio;
            var ok = mode == AspectRatioModeCustom && min <= 1.34f && max >= 2.33f;

            Add(results, CategoryRecommended, "Large-screen aspect ratios (Level Up)", ok,
                $"Custom mode {min:0.00}-{max:0.00} fills 4:3, 16:10 and 21:9 without letterboxing.",
                $"Aspect ratio mode is {DescribeAspectRatioMode(mode)} with {min:0.00}-{max:0.00}. Landscape games must fill 4:3 (1.33), 16:10 (1.60) and 21:9 (2.33) for Level Up LU-LS-GAC; a 3:2 minimum letterboxes 4:3 tablets.",
                "Set Aspect Ratio Mode to Custom, Minimum 1.32 and Maximum 2.34 (UnityBuildPipeline also applies this on Android builds).",
                "Fix aspect ratio", ApplyLargeScreenAspectRatio,
                AuditSeverity.Warning,
                whatIsThis: "Google Play requires landscape games to use the full screen at 4:3, 16:10 and 21:9. It is an Android Player Setting, not a code change.");
        }

        private static void ApplyLargeScreenAspectRatio()
        {
            SetAspectRatioMode(AspectRatioModeCustom);
            PlayerSettings.Android.minAspectRatio = AspectRatioMin;
            PlayerSettings.Android.maxAspectRatio = AspectRatioMax;
            AssetDatabase.SaveAssets();
            Debug.Log($"[NativeSocial] Android aspect ratio set to Custom {AspectRatioMin:0.00}-{AspectRatioMax:0.00}.");
        }

        /// <summary>supportedAspectRatioMode is internal (0 = Legacy 1.86, 1 = Native/2.1, 2 = Custom), so it is accessed by reflection.</summary>
        private static int ReadAspectRatioMode()
        {
            try
            {
                var property = typeof(PlayerSettings.Android).GetProperty("supportedAspectRatioMode",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (property != null) return (int)property.GetValue(null);
            }
            catch
            {
                // Fall through to "unknown".
            }

            return -1;
        }

        private static void SetAspectRatioMode(int mode)
        {
            try
            {
                var property = typeof(PlayerSettings.Android).GetProperty("supportedAspectRatioMode",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(null, mode);
                    return;
                }
            }
            catch
            {
                // Fall back to the serialized field below.
            }

            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (assets == null || assets.Length == 0) return;

                var serialized = new SerializedObject(assets[0]);
                var field = serialized.FindProperty("androidSupportedAspectRatio");
                if (field == null) return;

                field.intValue = mode;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NativeSocial] Could not set the Android aspect ratio mode: {e.Message}");
            }
        }

        private static string DescribeAspectRatioMode(int mode) => mode switch
        {
            0 => "Legacy Wide Screen (1.86)",
            1 => "Native (2.1)",
            2 => "Custom",
            _ => "unknown"
        };

        /// <summary>
        /// The package adds the Game Center capability to the generated Xcode project. If the post-process is missing or
        /// disabled, GKLocalPlayer never authenticates and Cloud Save stays anonymous on iOS.
        /// </summary>
        private static void AuditGameCenterCapability(List<AuditResult> results)
        {
            var present = AssetDatabase.FindAssets("GameCenterCapabilityBuildStep t:MonoScript").Length > 0;
            var optedOut = HasDefine("WAGENHEIMER_NATIVESOCIAL_NO_GAMECENTER");

            Add(results, CategoryIOS, "Game Center capability (Xcode)", present && !optedOut,
                "The package adds com.apple.developer.game-center to the generated Xcode project during the iOS build.",
                optedOut
                    ? "The Game Center capability build step is disabled by WAGENHEIMER_NATIVESOCIAL_NO_GAMECENTER."
                    : "No Game Center capability build step found: GKLocalPlayer will not authenticate and Cloud Save stays anonymous.",
                optedOut
                    ? "Remove WAGENHEIMER_NATIVESOCIAL_NO_GAMECENTER from Scripting Define Symbols if the game uses Game Center."
                    : "Update com.wagenheimer.nativesocial (v1.46+) or add the capability manually in Xcode > Signing & Capabilities.",
                optedOut ? "Open Player Settings" : null,
                optedOut ? () => SettingsService.OpenProjectSettings("Project/Player") : (Action)null,
                AuditSeverity.Warning,
                whatIsThis: "Unity has no Game Center toggle. Without the entitlement in the generated Xcode project, Game Center authentication fails silently and Cloud Save cannot link the player.");
        }
    }
}
