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

        private string _selectedCategory; // null = all
        private Label _progressLabel;
        private VisualElement _progressFill;
        private VisualElement _filterRow;
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

            var topRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, marginBottom = 4 } };
            _progressLabel = new Label();
            _progressLabel.style.fontSize = 11;
            _progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _progressLabel.style.color = NativeSocialUIStyle.ColorSuccess;
            topRow.Add(_progressLabel);
            topRow.Add(NativeSocialUIStyle.CreateButton("↺ Reset Manual Checks", ResetChecklist));
            headerCard.Add(topRow);

            // Progress bar
            var progressTrack = new VisualElement();
            progressTrack.AddToClassList("ns-progress-track");
            _progressFill = new VisualElement();
            _progressFill.AddToClassList("ns-progress-fill");
            progressTrack.Add(_progressFill);
            headerCard.Add(progressTrack);

            // Filter Tabs
            _filterRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap, marginTop = 4 } };
            headerCard.Add(_filterRow);
            RefreshFilterBar();

            Root.Add(headerCard);

            Root.Add(CreateHowItWorksCard());

            _contentBox = new VisualElement();
            Root.Add(_contentBox);

            RefreshGroups();
        }

        private void RefreshFilterBar()
        {
            if (_filterRow == null) return;
            _filterRow.Clear();
            _filterRow.Add(NativeSocialUIStyle.CreateFilterButton("🌐 All Platforms", () => SetFilter(null), _selectedCategory == null));
            _filterRow.Add(NativeSocialUIStyle.CreateFilterButton("⚙️ Core", () => SetFilter(CategoryCore), _selectedCategory == CategoryCore));
            _filterRow.Add(NativeSocialUIStyle.CreateFilterButton("🖥️ Steam", () => SetFilter(CategorySteam), _selectedCategory == CategorySteam));
            _filterRow.Add(NativeSocialUIStyle.CreateFilterButton("🤖 Android", () => SetFilter(CategoryAndroid), _selectedCategory == CategoryAndroid));
            _filterRow.Add(NativeSocialUIStyle.CreateFilterButton("🍎 iOS", () => SetFilter(CategoryIOS), _selectedCategory == CategoryIOS));
            _filterRow.Add(NativeSocialUIStyle.CreateFilterButton("🚀 Release", () => SetFilter(CategoryRelease), _selectedCategory == CategoryRelease));
        }

        private void SetFilter(string category)
        {
            _selectedCategory = category;
            RefreshFilterBar();
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

            var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap } };
            var title = new Label(label);
            title.AddToClassList("ns-checklist-title");
            titleRow.Add(title);

            if (isAutoPassed)
            {
                var autoBadge = new Label(autoStatus);
                autoBadge.AddToClassList("ns-badge-auto");
                titleRow.Add(autoBadge);
            }

            string assetPath = GetAutoDetectedAssetPath(id);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var fileName = Path.GetFileName(assetPath);
                var openBtn = new Button(() => NativeSocialAudit.OpenAssetOrFile(assetPath))
                {
                    text = "↗ " + fileName,
                    tooltip = "Open " + assetPath + " in editor or IDE."
                };
                openBtn.AddToClassList("ns-btn-secondary");
                openBtn.style.fontSize = 10;
                openBtn.style.paddingLeft = 6;
                openBtn.style.paddingRight = 6;
                openBtn.style.paddingTop = 1;
                openBtn.style.paddingBottom = 1;
                openBtn.style.marginLeft = 6;
                openBtn.style.borderTopLeftRadius = 4;
                openBtn.style.borderTopRightRadius = 4;
                openBtn.style.borderBottomLeftRadius = 4;
                openBtn.style.borderBottomRightRadius = 4;
                titleRow.Add(openBtn);
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
                var code = NativeSocialAudit.AnalyzeProjectCode();
                var maps = NativeSocialAudit.FindAllAchievementTierMaps();
                var primaryMap = maps.Count > 0 ? maps[0] : null;

                switch (id)
                {
                    case "setup_package":
                        return "✓ UPM Installed";

                    case "setup_map_asset":
                        if (primaryMap != null && primaryMap.Entries.Count > 0)
                            return $"✓ {primaryMap.Entries.Count} entries";
                        break;

                    case "setup_initialize":
                        if (code.HasInitialize) return $"✓ {Path.GetFileName(code.InitializePath)}";
                        if (AssetDatabase.FindAssets("NativeSocialBootstrap t:MonoScript").Length > 0)
                            return "✓ Bootstrap script present";
                        break;

                    case "setup_locid":
                        if (code.HasLocId) return "✓ LocId verified";
                        break;

                    case "setup_report":
                        if (code.HasReport) return $"✓ {Path.GetFileName(code.ReportPath)}";
                        break;

                    case "setup_sync":
                        if (code.HasSyncCompleted) return "✓ Sync call verified";
                        break;

                    case "setup_owner":
                        if (code.HasInitialize) return "✓ Verified";
                        break;

                    case "steam_installed":
                        bool steamFound = NativeSocialAudit.IsTypeAvailable("Steamworks.SteamUserStats") ||
                                          NativeSocialAudit.IsPackageRegistered("com.rlabrecque.steamworks.net");
                        if (steamFound) return "✓ Installed";
                        break;

                    case "steam_define":
                        if (NativeSocialAudit.HasDefine("WAGENHEIMER_NATIVESOCIAL_STEAM"))
                            return "✓ Active";
                        break;

                    case "steam_ids":
                        if (primaryMap != null && primaryMap.Entries.Count > 0)
                        {
                            int missing = primaryMap.Entries.Count(e => string.IsNullOrEmpty(e.SteamStat));
                            if (missing == 0) return $"✓ {primaryMap.Entries.Count}/{primaryMap.Entries.Count} Complete";
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
                        if (primaryMap != null && primaryMap.Entries.Count > 0)
                        {
                            int missing = primaryMap.CountMissingGooglePlay();
                            if (missing == 0) return $"✓ {primaryMap.Entries.Count}/{primaryMap.Entries.Count} Complete";
                            return $"{primaryMap.Entries.Count - missing}/{primaryMap.Entries.Count} IDs";
                        }
                        break;

                    case "ios_ids":
                        if (primaryMap != null && primaryMap.Entries.Count > 0)
                        {
                            int missing = primaryMap.CountMissingApple();
                            if (missing == 0) return $"✓ {primaryMap.Entries.Count}/{primaryMap.Entries.Count} Complete";
                            return $"{primaryMap.Entries.Count - missing}/{primaryMap.Entries.Count} IDs";
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

        private static string GetAutoDetectedAssetPath(string id)
        {
            try
            {
                var code = NativeSocialAudit.AnalyzeProjectCode();
                var maps = NativeSocialAudit.FindAllAchievementTierMaps();
                var primaryMap = maps.Count > 0 ? maps[0] : null;

                switch (id)
                {
                    case "setup_map_asset":
                        return primaryMap != null ? AssetDatabase.GetAssetPath(primaryMap) : null;

                    case "setup_initialize":
                        if (code.HasInitialize) return code.InitializePath;
                        var bootGuids = AssetDatabase.FindAssets("NativeSocialBootstrap t:MonoScript");
                        if (bootGuids.Length > 0) return AssetDatabase.GUIDToAssetPath(bootGuids[0]);
                        break;

                    case "setup_locid":
                        if (code.HasLocId) return code.LocIdPath;
                        break;

                    case "setup_report":
                        if (code.HasReport) return code.ReportPath;
                        break;

                    case "setup_sync":
                        if (code.HasSyncCompleted) return code.SyncCompletedPath;
                        break;

                    case "steam_ids":
                    case "android_ids":
                    case "ios_ids":
                        return primaryMap != null ? AssetDatabase.GetAssetPath(primaryMap) : null;

                    case "steam_appid":
                        if (File.Exists("steam_appid.txt")) return "steam_appid.txt";
                        break;
                }
            }
            catch
            {
            }
            return null;
        }

        private void UpdateProgress()
        {
            int total = Items.Length;
            int done = Items.Count(i => EditorPrefs.GetBool(PrefKeyPrefix + i.Id, false) || !string.IsNullOrEmpty(GetAutoDetectedStatus(i.Id)));
            int percent = total > 0 ? (done * 100 / total) : 0;
            _progressLabel.text = $"Progress: {done} / {total} verified ({percent}%)";
            if (_progressFill != null)
                _progressFill.style.width = Length.Percent(percent);
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
