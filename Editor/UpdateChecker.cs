using UnityEditor;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.NativeSocial.Editor
{
    public static class UpdateChecker
    {
        [MenuItem("Tools/Wagenheimer/Native Social/Check for Updates...", priority = 100)]
        public static void CheckForUpdateMenu() => CheckForUpdate(true);

        public static void CheckForUpdate(bool force = false)
        {
            PackageHubWindow.OpenToPackage("com.wagenheimer.nativesocial");
        }
    }
}
