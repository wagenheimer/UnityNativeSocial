using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

using UnityEditor;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// The SHA-1 fingerprint of the key Android builds are signed with, read with the JDK's <c>keytool</c> that ships with Unity. Google Play Games only
    /// accepts a sign-in from a build whose signing key's SHA-1 is registered under Play Games Services &gt; Credentials, and that is the thing people
    /// cannot see. The keystore password is passed through an environment variable, never on the command line, and is never logged.
    /// </summary>
    internal static class AndroidSigningInfo
    {
        private const string DebugAlias = "androiddebugkey";
        private const string DebugPassword = "android";
        private const string PasswordVariable = "NATIVESOCIAL_KEYSTORE_PASS";
        private const int KeytoolTimeoutMs = 15000;

        internal readonly struct Info
        {
            public readonly string Description;
            public readonly string Sha1;
            public readonly string Sha256;
            public readonly string Error;

            public Info(string description, string sha1, string sha256, string error)
            {
                Description = description;
                Sha1 = sha1;
                Sha256 = sha256;
                Error = error;
            }

            public bool HasFingerprint => !string.IsNullOrEmpty(Sha1);
        }

        private static readonly Dictionary<string, Info> Cache = new Dictionary<string, Info>();

        /// <summary>The fingerprint of the keystore Player Settings would sign with (custom keystore, otherwise Unity's debug keystore).</summary>
        internal static Info Read()
        {
            string path;
            string password;
            string alias;
            string description;

            if (PlayerSettings.Android.useCustomKeystore && !string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
            {
                path = Path.GetFullPath(PlayerSettings.Android.keystoreName);
                password = PlayerSettings.Android.keystorePass;
                alias = PlayerSettings.Android.keyaliasName;
                description = "custom keystore " + Path.GetFileName(path);
            }
            else
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "debug.keystore");
                password = DebugPassword;
                alias = DebugAlias;
                description = "Unity debug keystore";
            }

            if (!File.Exists(path))
                return new Info(description, null, null, $"keystore not found at {path}");

            var key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{alias}";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var info = RunKeytool(path, password, alias, description);
            Cache[key] = info;
            return info;
        }

        private static Info RunKeytool(string keystorePath, string password, string alias, string description)
        {
            var keytool = FindKeytool();
            var arguments = $"-list -v -keystore \"{keystorePath}\" -storepass:env {PasswordVariable}" +
                            (string.IsNullOrEmpty(alias) ? string.Empty : $" -alias \"{alias}\"");

            try
            {
                var startInfo = new ProcessStartInfo(keytool, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.EnvironmentVariables[PasswordVariable] = password ?? string.Empty;

                using (var process = Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(KeytoolTimeoutMs))
                    {
                        process.Kill();
                        return new Info(description, null, null, "keytool timed out");
                    }

                    var sha1 = Match(output, "SHA-?1");
                    var sha256 = Match(output, "SHA-?256");
                    return sha1 != null
                        ? new Info(description, sha1, sha256, null)
                        : new Info(description, null, null, FirstLine(error) ?? "keytool printed no SHA-1 (wrong keystore password or alias?)");
                }
            }
            catch (Exception ex)
            {
                return new Info(description, null, null, "could not run keytool: " + ex.Message);
            }
        }

        private static string Match(string text, string algorithm)
        {
            var match = Regex.Match(text ?? string.Empty, algorithm + @":\s*([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2})+)");
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lines = text.Split('\n');
            return lines[0].Trim();
        }

        /// <summary>Unity's own JDK when present, otherwise whatever <c>keytool</c> is on the PATH.</summary>
        private static string FindKeytool()
        {
            var exe = Application.platform == RuntimePlatform.WindowsEditor ? "keytool.exe" : "keytool";
            var bundled = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "OpenJDK", "bin", exe);
            return File.Exists(bundled) ? bundled : exe;
        }
    }
}
