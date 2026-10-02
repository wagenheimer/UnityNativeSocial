using System;
using System.IO;
using System.Linq;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>
    /// Unified release and verification checklist grouped by platform:
    /// Core Setup, Steam (Steamworks.NET), Android (Google Play Games), iOS (Game Center), and Release Verification.
    /// Combines live automated in-engine checks with persistent store-console and testing review items.
    /// </summary>
    internal sealed class NativeSocialChecklistView
    {
        private const string PrefKeyPrefix = "nativesocial_chk_";

        public const string CategoryCore = "⚙️ Core & Project Setup";
        public const string CategorySteam = "🖥️ Steam (Steamworks.NET)";
        public const string CategoryAndroid = "🤖 Android (Google Play Games)";
        public const string CategoryIOS = "🍎 iOS (Game Center)";
        public const string CategoryRelease = "🚀 Release Verification";

        private static readonly (string Category, string Description)[] Groups =
        {
            (CategoryCore,
                "Central package wiring and data asset: register maps once at startup and dispatch all gameplay calls through NativeSocial."),
            (CategorySteam,
                "Steam desktop builds via Steamworks.NET. Covers package installation, compile define, stat mapping, Steamworks partner configuration, and client testing."),
            (CategoryAndroid,
                "Google Play Games Services integration. Covers UPM Git plugin installation, define, AchievementTierMap IDs, Play Console setup, sign-in, and device testing."),
            (CategoryIOS,
                "Apple Game Center integration. Built into Unity iOS runtime. Covers AchievementTierMap IDs, App Store Connect setup, authentication, and Sandbox device testing."),
            (CategoryRelease,
                "Final end-to-end verification before shipping public game builds.")
        };

        private static readonly (string Category, string Id, string Label, string Description)[] Items =
        {
            // Core & Project Setup ──────────────────────────────────────────────────
            (CategoryCore, "setup_package", "NativeSocial installed (UPM git URL)",
                "Installed as com.wagenheimer.nativesocial from https://github.com/wagenheimer/UnityNativeSocial.git so updates flow through Package Hub."),
            (CategoryCore, "setup_map_asset", "AchievementTierMap asset created and populated",
                "Central asset (Assets > Create > Wagenheimer > Native Social > Achievement Tier Map) with one row per trophy tier. Holds SteamStat, GooglePlayId, and AppleId."),
            (CategoryCore, "setup_initialize", "NativeSocial.Initialize(...) called once at startup",
                "Build the maps from the asset (BuildAndroidMap/BuildIosMap/BuildSteamMap) and pass them to Initialize() before any Report call. Runs in your main game manager (e.g. Main.cs) or via NativeSocialBootstrap."),
            (CategoryCore, "setup_locid", "All Report/SyncCompleted calls use AchievementTierMap.LocId(...)",
                "Always format keys using AchievementTierMap.LocId(trophyNumber, tier) so call sites and map dictionaries match perfectly without typos."),
            (CategoryCore, "setup_report", "Progress reported through NativeSocial.Report(...)",
                "Report(locId, delta, current, total, completed): Android/Steam use delta, iOS uses current/total for percentage. Pass completed: true when reaching the goal."),
            (CategoryCore, "setup_sync", "SyncCompleted(...) called right after sign-in",
                "Re-pushes achievements earned while offline or on another device. Call it immediately after authentication succeeds, passing locally unlocked LocIds."),
            (CategoryCore, "setup_owner", "One owner per platform (no double-reporting)",
                "If your project already had a custom Steam integration (e.g. SteamManager/AchievementsSteam), keep using it for Steam and pass null for steamMap to avoid double unlocks."),

            // Steam (Steamworks.NET) ────────────────────────────────────────────────
            (CategorySteam, "steam_installed", "[In-Project] Steamworks.NET installed",
                "Steamworks.NET package (com.rlabrecque.steamworks.net) is installed in Packages/manifest.json."),
            (CategorySteam, "steam_define", "[In-Project] WAGENHEIMER_NATIVESOCIAL_STEAM define active",
                "Active in the current build target or managed by your automated build pipeline (e.g. PublisherProfile). Without it, NativeSocial's Steam calls compile out."),
            (CategorySteam, "steam_ids", "[In-Project] SteamStat names filled in AchievementTierMap",
                "Every trophy tier shipping on Steam has its SteamStat name configured (e.g. Trophy1_1_Status)."),
            (CategorySteam, "steam_partner", "[Store Console] Stats & achievements created in Steamworks",
                "Create all matching stats and achievements on the Steamworks Partner portal (Admin > Stats & Achievements). API names must match SteamStat exactly."),
            (CategorySteam, "steam_appid", "[Testing] steam_appid.txt present for local testing",
                "Put steam_appid.txt (containing your Steam App ID) in the project root or beside the built .exe when testing outside of the Steam launcher."),
            (CategorySteam, "steam_test", "[Testing] Tested with the Steam client running",
                "Verify achievements unlock with Steam client notifications popping up while the game is running."),

            // Android (Google Play Games) ───────────────────────────────────────────
            (CategoryAndroid, "android_install", "[In-Project] GPGS plugin installed via Git UPM",
                "Installed as com.google.play.games v2.3.0 from Google's git repository. Real UPM package allows versionDefines to fire automatically."),
            (CategoryAndroid, "android_define", "[In-Project] WAGENHEIMER_NATIVESOCIAL_GPGS active",
                "Auto-set by com.google.play.games UPM versionDefine, enabling Android achievement dispatching."),
            (CategoryAndroid, "android_ids", "[In-Project] GooglePlayId filled in AchievementTierMap",
                "Every trophy tier shipping on Android has its alphanumeric ID configured (from Play Console)."),
            (CategoryAndroid, "android_oauth", "[Store Console] GPGS Android Setup wizard completed",
                "Run Window > Google Play Games > Setup > Android setup, paste the Web App Client ID from Play Console, and commit the generated Android resources."),
            (CategoryAndroid, "android_console_achievements", "[Store Console] 54 achievements created in Play Console",
                "Create and publish achievements under Play Console > Grow > Play Games Services > Configuration > Achievements."),
            (CategoryAndroid, "android_auth", "[In-Project] Sign-in called at startup (Authenticate / AuthenticateManually)",
                "Call NativeSocial.Authenticate(...) on startup. If auto-sign-in fails, offer a manual sign-in button that calls NativeSocial.AuthenticateManually(...)."),
            (CategoryAndroid, "android_test", "[Testing] Tested sign-in and unlock on Android device / emulator",
                "Test on a real device or Play Services emulator with a Google account added to the Play Console tester list."),

            // iOS (Game Center) ─────────────────────────────────────────────────────
            (CategoryIOS, "ios_capability", "[In-Project] Game Center capability enabled in Xcode",
                "Ensure Game Center capability is enabled in Xcode project settings (Signing & Capabilities) or through an automated post-process build script."),
            (CategoryIOS, "ios_ids", "[In-Project] AppleId filled in AchievementTierMap",
                "Every trophy tier shipping on iOS has its Game Center achievement ID configured (from App Store Connect)."),
            (CategoryIOS, "ios_console_achievements", "[Store Console] 54 achievements created in App Store Connect",
                "Create achievements under App Store Connect > Apps > Your App > Features > Game Center > Achievements."),
            (CategoryIOS, "ios_auth", "[In-Project] Game Center authentication called at startup",
                "NativeSocial.Authenticate(...) signs into Game Center on iOS. SyncCompleted follows to restore offline achievements."),
            (CategoryIOS, "ios_test", "[Testing] Tested on a real iOS device with Sandbox account",
                "Test with an Apple ID configured in Settings > Game Center > Sandbox on an actual iOS device."),

            // Release Verification ──────────────────────────────────────────────────
            (CategoryRelease, "rel_sync", "Offline to online sync verified",
                "Ensure achievements earned while playing offline are immediately synced upon signing in on Android, iOS, or Steam."),
            (CategoryRelease, "rel_empty_safe", "Unshipped platform IDs confirmed safe",
                "Empty IDs for non-targeted platforms are safely skipped by NativeSocial.Report without throwing."),
            (CategoryRelease, "rel_version", "Package version and changelog up to date",
                "NativeSocial and game version numbers are verified before publishing.")
        };

        public VisualElement Root { get; }

        private string _selectedCategory = null; // null = all
        private Label _progressLabel;
        private VisualElement _contentBox;

        public NativeSocialChecklistView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);
            BuildUI();
        }

        private void BuildUI()
        {
            var headerCard = NativeSocialUIStyle.CreateCard("📋 Release & Setup Checklist",
                "Step-by-step checklist to guarantee 100% working achievements across Steam, Android and iOS. Combines live project detection with store-console instructions.");

            var topRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, marginBottom = 8 } };
            _progressLabel = new Label();
            _progressLabel.style.fontSize = 11;
            _progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _progressLabel.style.color = NativeSocialUIStyle.ColorSuccess;
            topRow.Add(_progressLabel);
            topRow.Add(NativeSocialUIStyle.CreateButton("↺ Reset Manual Checks", ResetChecklist));
            headerCard.Add(topRow);

            // Filter Tabs
            var filterRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap, marginTop = 4 } };
            filterRow.Add(NativeSocialUIStyle.CreateButton("🌐 All Platforms", () => SetFilter(null)));
            filterRow.Add(NativeSocialUIStyle.CreateButton("⚙️ Core", () => SetFilter(CategoryCore)));
            filterRow.Add(NativeSocialUIStyle.CreateButton("🖥️ Steam", () => SetFilter(CategorySteam)));
            filterRow.Add(NativeSocialUIStyle.CreateButton("🤖 Android", () => SetFilter(CategoryAndroid)));
            filterRow.Add(NativeSocialUIStyle.CreateButton("🍎 iOS", () => SetFilter(CategoryIOS)));
            filterRow.Add(NativeSocialUIStyle.CreateButton("🚀 Release", () => SetFilter(CategoryRelease)));
            headerCard.Add(filterRow);

            Root.Add(headerCard);

            Root.Add(CreateHowItWorksCard());

            _contentBox = new VisualElement();
            Root.Add(_contentBox);

            RefreshGroups();
        }

        private void SetFilter(string category)
        {
            _selectedCategory = category;
            RefreshGroups();
        }

        private void RefreshGroups()
        {
            _contentBox.Clear();

            var visibleGroups = Groups.Where(g => string.IsNullOrEmpty(_selectedCategory) || g.Category == _selectedCategory);
            foreach (var group in visibleGroups)
            {
                var card = NativeSocialUIStyle.CreateCard(group.Category, group.Description);
                foreach (var item in Items.Where(i => i.Category == group.Category))
                    card.Add(CreateItem(item.Id, item.Label, item.Description));
                _contentBox.Add(card);
            }

            UpdateProgress();
        }

        private static VisualElement CreateHowItWorksCard()
        {
            var card = NativeSocialUIStyle.CreateCard("💡 How the integration works",
                "One package, one API, three platforms — platform logic is selected at compile-time for zero overhead.");
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "Initialize once: NativeSocial.Initialize(androidMap, iosMap, steamMap) stores LocId → platform-ID mappings."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "Report per change: NativeSocial.Report(locId, delta, current, total, completed) automatically routes to the active platform."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "Zero crashes on missing IDs: If an ID is left empty (or the player is not signed in), the call silently no-ops without throwing."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "SyncCompleted(completedLocIds) called right after sign-in restores all achievements earned while offline."));
            return card;
        }

        private VisualElement CreateItem(string id, string label, string description)
        {
            var element = new VisualElement();
            element.AddToClassList("ns-checklist-item");

            string autoStatus = GetAutoDetectedStatus(id);
            bool isAutoPassed = !string.IsNullOrEmpty(autoStatus);

            bool isChecked = EditorPrefs.GetBool(PrefKeyPrefix + id, false) || isAutoPassed;
            if (isChecked) element.AddToClassList("checked");

            var toggle = new Toggle { value = isChecked };
            toggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(PrefKeyPrefix + id, evt.newValue);
                element.EnableInClassList("checked", evt.newValue);
                UpdateProgress();
            });
            element.Add(toggle);

            var textCol = new VisualElement();
            textCol.AddToClassList("ns-checklist-text");

            var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var title = new Label(label);
            title.AddToClassList("ns-checklist-title");
            titleRow.Add(title);

            if (isAutoPassed)
            {
                var autoBadge = new Label(autoStatus);
                autoBadge.style.fontSize = 10;
                autoBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
                autoBadge.style.color = NativeSocialUIStyle.ColorSuccess;
                autoBadge.style.backgroundColor = new Color(0.12f, 0.28f, 0.18f);
                autoBadge.style.paddingLeft = 6;
                autoBadge.style.paddingRight = 6;
                autoBadge.style.paddingTop = 2;
                autoBadge.style.paddingBottom = 2;
                autoBadge.style.marginLeft = 8;
                autoBadge.style.borderTopLeftRadius = 4;
                autoBadge.style.borderTopRightRadius = 4;
                autoBadge.style.borderBottomLeftRadius = 4;
                autoBadge.style.borderBottomRightRadius = 4;
                titleRow.Add(autoBadge);
            }
            textCol.Add(titleRow);

            var desc = new Label(description);
            desc.AddToClassList("ns-checklist-desc");
            desc.style.whiteSpace = WhiteSpace.Normal;
            textCol.Add(desc);

            element.Add(textCol);
            return element;
        }

        private static string GetAutoDetectedStatus(string id)
        {
            try
            {
                switch (id)
                {
                    case "setup_package":
                        return "✓ UPM Installed";

                    case "setup_map_asset":
                        var maps = NativeSocialAudit.FindAllAchievementTierMaps();
                        if (maps.Count > 0 && maps[0].Entries.Count > 0)
                            return $"✓ {maps[0].Entries.Count} entries";
                        break;

                    case "setup_initialize":
                        var (initFound, _) = NativeSocialAudit.FindInitializeCallInProject();
                        if (initFound) return "✓ Initialized in code";
                        if (AssetDatabase.FindAssets("NativeSocialBootstrap t:MonoScript").Length > 0)
                            return "✓ Bootstrap script present";
                        break;

                    case "steam_installed":
                        bool steamFound = NativeSocialAudit.IsTypeAvailable("Steamworks.SteamUserStats") ||
                                          UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p => p.name == "com.rlabrecque.steamworks.net");
                        if (steamFound) return "✓ Installed";
                        break;

                    case "steam_define":
                        if (NativeSocialAudit.HasDefine("WAGENHEIMER_NATIVESOCIAL_STEAM"))
                            return "✓ Define active";
                        break;

                    case "steam_ids":
                        var sMaps = NativeSocialAudit.FindAllAchievementTierMaps();
                        if (sMaps.Count > 0 && sMaps[0].Entries.Count > 0)
                        {
                            int missing = sMaps[0].Entries.Count(e => string.IsNullOrEmpty(e.SteamStat));
                            if (missing == 0) return $"✓ {sMaps[0].Entries.Count}/{sMaps[0].Entries.Count} Complete";
                        }
                        break;

                    case "steam_appid":
                        if (File.Exists("steam_appid.txt")) return "✓ Found in root";
                        break;

                    case "android_install":
                        string gpgsVer = GpgsInstaller.GetInstalledVersion();
                        if (!string.IsNullOrEmpty(gpgsVer)) return $"✓ UPM v{gpgsVer}";
                        if (GpgsInstaller.FindLoosePluginRoots().Count > 0) return "✓ Loose plugin detected";
                        break;

                    case "android_define":
                        if (GpgsInstaller.IsInstalled() || NativeSocialAudit.HasDefine("WAGENHEIMER_NATIVESOCIAL_GPGS"))
                            return "✓ Active";
                        break;

                    case "android_ids":
                        var aMaps = NativeSocialAudit.FindAllAchievementTierMaps();
                        if (aMaps.Count > 0 && aMaps[0].Entries.Count > 0)
                        {
                            int missing = aMaps[0].CountMissingGooglePlay();
                            if (missing == 0) return $"✓ {aMaps[0].Entries.Count}/{aMaps[0].Entries.Count} Complete";
                            return $"{aMaps[0].Entries.Count - missing}/{aMaps[0].Entries.Count} IDs";
                        }
                        break;

                    case "ios_ids":
                        var iMaps = NativeSocialAudit.FindAllAchievementTierMaps();
                        if (iMaps.Count > 0 && iMaps[0].Entries.Count > 0)
                        {
                            int missing = iMaps[0].CountMissingApple();
                            if (missing == 0) return $"✓ {iMaps[0].Entries.Count}/{iMaps[0].Entries.Count} Complete";
                            return $"{iMaps[0].Entries.Count - missing}/{iMaps[0].Entries.Count} IDs";
                        }
                        break;
                }
            }
            catch
            {
                // Safe fallback for background or editor edge cases
            }

            return null;
        }

        private void UpdateProgress()
        {
            int total = Items.Length;
            int done = Items.Count(i => EditorPrefs.GetBool(PrefKeyPrefix + i.Id, false) || !string.IsNullOrEmpty(GetAutoDetectedStatus(i.Id)));
            _progressLabel.text = $"Progress: {done} / {total} verified ({done * 100 / total}%)";
        }

        private void ResetChecklist()
        {
            if (!EditorUtility.DisplayDialog("Reset Checklist", "Reset all manual checklist checkboxes?", "RESET", "CANCEL"))
                return;

            foreach (var item in Items)
                EditorPrefs.DeleteKey(PrefKeyPrefix + item.Id);

            RefreshGroups();
        }
    }
}
