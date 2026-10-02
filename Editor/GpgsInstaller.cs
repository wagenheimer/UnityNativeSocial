using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

using UnityEngine;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// One-click installer for the official Google Play Games plugin for Unity.
    ///
    /// <para>
    /// The plugin is consumed straight from Google's own git repository through the UPM package embedded at
    /// <c>Assets/Public/GooglePlayGames/com.google.play.games</c> — the same source the
    /// <c>GooglePlayGamesPlugin-*.unitypackage</c> on the release page is built from. Installing it this way
    /// (instead of importing the .unitypackage) makes it resolve as a real package named
    /// <c>com.google.play.games</c>, which is what lets <c>Wagenheimer.NativeSocial.asmdef</c>'s
    /// <c>versionDefines</c> set <c>WAGENHEIMER_NATIVESOCIAL_GPGS</c> automatically. A loose
    /// <c>Assets/GooglePlayGames</c> copy (the .unitypackage import) never does, so every Android call
    /// silently compiles out.
    /// </para>
    /// </summary>
    internal static class GpgsInstaller
    {
        public const string PackageId = "com.google.play.games";
        public const string PackageDisplayName = "Google Play Games";
        public const string PackageVersion = "2.3.0";
        public const string RepoUrl = "https://github.com/playgameservices/play-games-plugin-for-unity";

        /// <summary>Git UPM spec for Google's embedded package (sub-folder <c>?path=</c> + release tag).</summary>
        public const string GitUrl =
            RepoUrl + ".git?path=Assets/Public/GooglePlayGames/com.google.play.games#v" + PackageVersion;

        // GPGS' package.json depends on EDM4U. If it isn't already present, UPM can't fetch the registry
        // version it asks for (EDM4U isn't on Unity's registry), so we install the official git copy first.
        private const string Edm4uPackageId = "com.google.external-dependency-manager";
        private const string Edm4uGitUrl = "https://github.com/googlesamples/unity-jar-resolver.git?path=upm";

        private sealed class Step
        {
            public string Id;
            public string Spec;
        }

        private static readonly Queue<Step> _queue = new Queue<Step>();
        private static AddRequest _request;
        private static Step _current;
        private static Action<bool, string> _onComplete;

        /// <summary>True while an install/resolve kicked off by this installer is still running.</summary>
        public static bool IsInstalling { get; private set; }

        /// <summary>Raised (success or failure) after an install started by this installer finishes.</summary>
        public static event Action OnInstallCompleted;

        /// <summary>Version of the registered <c>com.google.play.games</c> UPM package, or null when it
        /// is not installed (a loose <c>Assets/</c> import is not a registered package and returns null).</summary>
        public static string GetInstalledVersion()
        {
            try
            {
                return PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(p => p.name == PackageId)?.version;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsInstalled() => !string.IsNullOrEmpty(GetInstalledVersion());

        /// <summary>
        /// Finds loose (non-UPM) copies of the plugin imported under <c>Assets/</c>, e.g.
        /// <c>Assets/GooglePlayGames/com.google.play.games/</c> from the .unitypackage. Returns the folder
        /// directly containing the <c>com.google.play.games</c> package (what must be removed before a UPM
        /// install, or every plugin type is defined twice).
        /// </summary>
        public static List<string> FindLoosePluginRoots()
        {
            var roots = new List<string>();

            // The .unitypackage always imports here; check it directly so detection doesn't depend on
            // AssetDatabase's search filters.
            const string standardRoot = "Assets/GooglePlayGames";
            if (Directory.Exists(standardRoot))
                roots.Add(standardRoot);

            foreach (var guid in AssetDatabase.FindAssets("Google.Play.Games t:AssemblyDefinitionAsset"))
            {
                var asmdefPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!asmdefPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Assets/<root>/com.google.play.games/Runtime/Google.Play.Games.asmdef
                var runtimeDir = Path.GetDirectoryName(asmdefPath);
                var packageDir = runtimeDir != null ? Path.GetDirectoryName(runtimeDir) : null;
                var root = packageDir != null ? Path.GetDirectoryName(packageDir) : null;
                if (!string.IsNullOrEmpty(root) && !roots.Contains(root))
                    roots.Add(root);
            }
            return roots;
        }

        public static bool HasLooseAssetCopy() => FindLoosePluginRoots().Count > 0;

        [MenuItem("Tools/Wagenheimer/Native Social/Install Google Play Games (UPM)...", priority = 2)]
        public static void InstallMenu() => Install();

        [MenuItem("Tools/Wagenheimer/Native Social/Install Google Play Games (UPM)...", true)]
        private static bool InstallMenuValidate() => !IsInstalled();

        public static void Install() => Install(null);

        /// <summary>
        /// Installs the official GPGS plugin from its git UPM repository. If a loose .unitypackage copy is
        /// found it is moved to the Trash first (with a confirmation) so the two don't define duplicate types.
        /// </summary>
        public static void Install(Action<bool, string> onComplete)
        {
            if (IsInstalling)
            {
                EditorUtility.DisplayDialog("Installation already running",
                    PackageDisplayName + " is already being installed. Wait for Unity to finish resolving.", "OK");
                return;
            }

            if (IsInstalled())
            {
                EditorUtility.DisplayDialog("Already installed",
                    PackageDisplayName + " is already installed as a UPM package.", "OK");
                onComplete?.Invoke(true, "Already installed as UPM package.");
                return;
            }

            var looseRoots = FindLoosePluginRoots();
            var message =
                $"{PackageDisplayName} v{PackageVersion} will be installed from Google's official git repository " +
                "as a UPM package via the Package Manager.\n\n" +
                "This is the recommended install: it lets Unity's Version Define set " +
                "WAGENHEIMER_NATIVESOCIAL_GPGS automatically, which the manual .unitypackage import cannot do.";

            if (looseRoots.Count > 0)
                message += "\n\nThe existing loose copy (imported from the .unitypackage) will be moved to the Trash first:\n" +
                           "  • " + string.Join("\n  • ", looseRoots) +
                           "\nKeeping both would define every plugin type twice and fail to compile.";

            message += "\n\nProceed?";

            if (!EditorUtility.DisplayDialog($"Install {PackageDisplayName}", message, "INSTALL", "CANCEL"))
                return;

            foreach (var root in looseRoots)
            {
                if (AssetDatabase.MoveAssetToTrash(root))
                    Debug.Log($"[NativeSocial] Moved loose plugin copy '{root}' to the Trash (replaced by the UPM package).");
                else
                    Debug.LogWarning($"[NativeSocial] Could not move '{root}' to the Trash. Remove it manually before the UPM install, or the project will not compile.");
            }

            IsInstalling = true;
            _onComplete = onComplete;
            _queue.Clear();

            if (!IsRegistered(Edm4uPackageId))
                _queue.Enqueue(new Step { Id = Edm4uPackageId, Spec = Edm4uGitUrl });
            _queue.Enqueue(new Step { Id = PackageId, Spec = GitUrl });

            ProcessNext();
        }

        private static bool IsRegistered(string packageId)
        {
            try
            {
                return PackageInfo.GetAllRegisteredPackages().Any(p => p.name == packageId);
            }
            catch
            {
                return false;
            }
        }

        private static void ProcessNext()
        {
            if (_queue.Count == 0)
            {
                IsInstalling = false;
                _current = null;
                var cb = _onComplete;
                _onComplete = null;
                AssetDatabase.Refresh();
                OnInstallCompleted?.Invoke();
                EditorUtility.DisplayDialog("Finished",
                    $"{PackageDisplayName} has been added to the project. Unity is resolving packages and will " +
                    "recompile shortly — re-run Tools > Wagenheimer > Native Social > Verify Setup when it finishes.",
                    "OK");
                cb?.Invoke(true, "Installed.");
                return;
            }

            _current = _queue.Dequeue();
            Debug.Log($"[NativeSocial] Installing {_current.Id} via: {_current.Spec}");
            _request = Client.Add(_current.Spec);
            EditorApplication.update += MonitorRequest;
        }

        private static void MonitorRequest()
        {
            if (_request == null || !_request.IsCompleted)
                return;

            EditorApplication.update -= MonitorRequest;

            var success = _request.Status == StatusCode.Success;
            var error = _request.Error?.message;
            var step = _current;
            _request = null;

            if (success)
            {
                Debug.Log($"[NativeSocial] Installed {step.Id}.");
                ProcessNext();
                return;
            }

            IsInstalling = false;
            _current = null;
            var cb = _onComplete;
            _onComplete = null;

            Debug.LogError($"[NativeSocial] Failed to install '{step.Id}': {error}");
            OnInstallCompleted?.Invoke();
            EditorUtility.DisplayDialog("Installation failed",
                $"Could not install {step.Id}.\n\n{error}\n\n" +
                "You can add it manually in the Package Manager (Add package from git URL):\n" + step.Spec, "OK");
            cb?.Invoke(false, error);
        }
    }
}
