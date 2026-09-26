using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    public class NativeSocialHubWindow : EditorWindow
    {
        public enum Tab
        {
            Overview,
            Checker,
            Helper,
            Guides,
            About
        }

        private Tab _currentTab = Tab.Overview;
        private VisualElement _tabContent;
        private readonly Dictionary<Tab, Button> _tabButtons = new();

        // Helper Tab Form Fields
        private string _testLocId = "ach_first_win";
        private int _testDelta = 1;
        private int _testCurrent = 1;
        private int _testTotal = 10;
        private bool _testCompleted = false;
        private string _testLeaderboardId = "lb_high_score";
        private long _testScore = 1000;

        [MenuItem("Tools/Wagenheimer/Native Social/Dashboard...", priority = 0)]
        public static void Open()
        {
            Open(Tab.Overview);
        }

        public static void Open(Tab tab)
        {
            var window = GetWindow<NativeSocialHubWindow>("Native Social");
            window.minSize = new Vector2(760, 520);
            window._currentTab = tab;
            window.Show();
            window.SelectTab(tab);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Native Social", EditorGUIUtility.IconContent("d_SocialNetworks").image);
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("ns-root");

            // Apply fail-safe layout
            rootVisualElement.style.backgroundColor = NativeSocialUIStyle.ColorBgDark;
            rootVisualElement.style.color = NativeSocialUIStyle.ColorText;
            rootVisualElement.style.flexGrow = 1;

            // Load USS
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.wagenheimer.nativesocial/Editor/UI/NativeSocialCommon.uss");
            if (uss != null)
            {
                rootVisualElement.styleSheets.Add(uss);
            }

            BuildHeader();
            BuildTabBar();

            _tabContent = new ScrollView(ScrollViewMode.Vertical);
            _tabContent.AddToClassList("ns-body");
            _tabContent.style.flexGrow = 1;
            _tabContent.SetPadding(18, 16);
            rootVisualElement.Add(_tabContent);

            SelectTab(_currentTab);
        }

        private void BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("ns-banner");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.backgroundColor = new Color(0.05f, 0.10f, 0.16f);
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.00f, 0.46f, 0.71f);
            header.SetPadding(20, 14);

            var left = new VisualElement();
            var title = new Label("Native Social Dashboard");
            title.AddToClassList("ns-banner-title");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = NativeSocialUIStyle.ColorPrimary;

            var subtitle = new Label("Multiplatform Native Achievements, Leaderboards & Auth");
            subtitle.AddToClassList("ns-banner-subtitle");
            subtitle.style.fontSize = 11;
            subtitle.style.color = new Color(0.56f, 0.88f, 0.94f);
            left.Add(title);
            left.Add(subtitle);
            header.Add(left);

            var right = new VisualElement();
            right.style.flexDirection = FlexDirection.Row;
            right.style.alignItems = Align.Center;

            var pkgBtn = new Button(() => PackageHubWindow.OpenToPackage("com.wagenheimer.nativesocial"))
            {
                text = "Package Hub"
            };
            pkgBtn.AddToClassList("ns-btn-secondary");
            pkgBtn.style.marginRight = 8;
            right.Add(pkgBtn);

            var githubBtn = new Button(() => Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial"))
            {
                text = "GitHub"
            };
            githubBtn.AddToClassList("ns-btn-secondary");
            right.Add(githubBtn);

            header.Add(right);
            rootVisualElement.Add(header);
        }

        private void BuildTabBar()
        {
            var tabBar = new VisualElement();
            tabBar.AddToClassList("ns-tab-bar");
            tabBar.style.flexDirection = FlexDirection.Row;
            tabBar.style.backgroundColor = new Color(0.086f, 0.125f, 0.180f);
            tabBar.style.borderBottomWidth = 1;
            tabBar.style.borderBottomColor = new Color(0.149f, 0.220f, 0.322f);
            tabBar.SetPadding(14, 6);
            tabBar.style.paddingBottom = 0;

            _tabButtons.Clear();
            AddTabButton(tabBar, Tab.Overview, "Overview");
            AddTabButton(tabBar, Tab.Checker, "Checker & Audit");
            AddTabButton(tabBar, Tab.Helper, "Helper & Tester");
            AddTabButton(tabBar, Tab.Guides, "Guides & Snippets");
            AddTabButton(tabBar, Tab.About, "About");

            rootVisualElement.Add(tabBar);
        }

        private void AddTabButton(VisualElement container, Tab tab, string title)
        {
            var btn = new Button(() => SelectTab(tab)) { text = title };
            btn.AddToClassList("ns-tab-btn");
            btn.style.backgroundColor = Color.transparent;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 2;
            btn.style.borderBottomColor = Color.transparent;
            btn.SetPadding(16, 8);
            btn.style.marginRight = 4;
            btn.style.color = NativeSocialUIStyle.ColorTextMuted;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;

            _tabButtons[tab] = btn;
            container.Add(btn);
        }

        public void SelectTab(Tab tab)
        {
            _currentTab = tab;
            foreach (var kv in _tabButtons)
            {
                var isSelected = kv.Key == tab;
                if (isSelected)
                {
                    kv.Value.AddToClassList("ns-tab-btn-active");
                    kv.Value.style.backgroundColor = new Color(0.118f, 0.200f, 0.290f);
                    kv.Value.style.borderBottomColor = NativeSocialUIStyle.ColorPrimary;
                    kv.Value.style.color = new Color(0.220f, 0.741f, 0.973f);
                }
                else
                {
                    kv.Value.RemoveFromClassList("ns-tab-btn-active");
                    kv.Value.style.backgroundColor = Color.transparent;
                    kv.Value.style.borderBottomColor = Color.transparent;
                    kv.Value.style.color = NativeSocialUIStyle.ColorTextMuted;
                }
            }

            RenderTabContent(tab);
        }

        private void RenderTabContent(Tab tab)
        {
            if (_tabContent == null) return;
            _tabContent.Clear();

            switch (tab)
            {
                case Tab.Overview:
                    RenderOverviewTab();
                    break;
                case Tab.Checker:
                    RenderCheckerTab();
                    break;
                case Tab.Helper:
                    RenderHelperTab();
                    break;
                case Tab.Guides:
                    RenderGuidesTab();
                    break;
                case Tab.About:
                    RenderAboutTab();
                    break;
            }
        }

        // =========================================================================
        // TAB 1: OVERVIEW
        // =========================================================================
        private void RenderOverviewTab()
        {
            // Status Card
            var statusCard = CreateCard("Platform & Environment Status", "Current build configuration and platform support summary.");
            var activeTarget = EditorUserBuildSettings.activeBuildTarget;
            var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

            var row1 = CreateInfoRow("Active Build Target", activeTarget.ToString(), NativeSocialUIStyle.ColorPrimary);
            statusCard.Add(row1);

            bool gpgsFound = IsTypeAvailable("GooglePlayGames.PlayGamesPlatform");
            bool steamFound = IsTypeAvailable("Steamworks.SteamUserStats");
            bool steamDefine = HasSteamDefine();

            var gpgsBadge = gpgsFound ? "Installed" : "Not Detected";
            var gpgsColor = gpgsFound ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorTextMuted;
            statusCard.Add(CreateInfoRow("Google Play Games (Android)", gpgsBadge, gpgsColor));

            var gcStatus = activeTarget == BuildTarget.iOS ? "Native iOS Active" : "Available on iOS Target";
            statusCard.Add(CreateInfoRow("Game Center (iOS)", gcStatus, activeTarget == BuildTarget.iOS ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorTextMuted));

            var steamStatus = steamFound ? (steamDefine ? "Installed & Define Active" : "Installed (Define Missing)") : "Not Detected";
            var steamColor = steamFound && steamDefine ? NativeSocialUIStyle.ColorSuccess : (steamFound ? NativeSocialUIStyle.ColorWarning : NativeSocialUIStyle.ColorTextMuted);
            statusCard.Add(CreateInfoRow("Steamworks.NET (Steam)", steamStatus, steamColor));

            _tabContent.Add(statusCard);

            // Quick Actions Row
            var actionsCard = CreateCard("Quick Actions", "Direct shortcuts for common setup and verification workflows.");
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.flexWrap = Wrap.Wrap;

            var checkBtn = new Button(() => SelectTab(Tab.Checker)) { text = "Run Verification & Audit" };
            checkBtn.AddToClassList("ns-btn-primary");
            checkBtn.style.marginRight = 8;
            checkBtn.style.marginBottom = 8;
            btnRow.Add(checkBtn);

            var testBtn = new Button(() => SelectTab(Tab.Helper)) { text = "Open Live Tester" };
            testBtn.AddToClassList("ns-btn-secondary");
            testBtn.style.marginRight = 8;
            testBtn.style.marginBottom = 8;
            btnRow.Add(testBtn);

            var guideBtn = new Button(() => SelectTab(Tab.Guides)) { text = "View Integration Guides" };
            guideBtn.AddToClassList("ns-btn-secondary");
            guideBtn.style.marginRight = 8;
            guideBtn.style.marginBottom = 8;
            btnRow.Add(guideBtn);

            actionsCard.Add(btnRow);
            _tabContent.Add(actionsCard);

            // Architecture Highlights
            var archCard = CreateCard("Why Native Social?", "Core architectural advantages of the package.");
            archCard.Add(CreateBulletPoint("Zero Obsolete Warnings: Directly replaces Unity's deprecated UnityEngine.Social without warnings."));
            archCard.Add(CreateBulletPoint("Unified Single API: One call (Report, SubmitScore, Authenticate) works identically across Android, iOS and Steam."));
            archCard.Add(CreateBulletPoint("Compile-Time Separation: Native SDK headers only compile on their respective target, keeping APK/IPA builds lightweight."));
            archCard.Add(CreateBulletPoint("Idempotent Synchronization: SyncCompleted() safely reconciles local offline progress upon cloud auth."));
            _tabContent.Add(archCard);
        }

        // =========================================================================
        // TAB 2: CHECKER & AUDIT
        // =========================================================================
        private void RenderCheckerTab()
        {
            var headerCard = CreateCard("Verification Engine & Platform Checker", "Diagnoses required dependencies, defines, and configuration for all supported platforms.");
            _tabContent.Add(headerCard);

            // Check 1: Android & GPGS
            var gpgsFound = IsTypeAvailable("GooglePlayGames.PlayGamesPlatform");
            var gpgsCard = CreateCard("1. Android (Google Play Games Services)", "Requires the official Google Play Games plugin for Unity (v11+ recommended).");
            var gpgsStatusText = gpgsFound ? "PASS: PlayGamesPlatform detected in project." : "INFO: Google Play Games plugin not detected.";
            var gpgsStatusColor = gpgsFound ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorWarning;
            gpgsCard.Add(CreateInfoRow("GPGS Status", gpgsStatusText, gpgsStatusColor));

            if (!gpgsFound)
            {
                var docBtn = new Button(() => Application.OpenURL("https://github.com/playgameservices/play-games-plugin-for-unity"))
                {
                    text = "Get Google Play Games Plugin"
                };
                docBtn.AddToClassList("ns-btn-secondary");
                docBtn.style.marginTop = 6;
                gpgsCard.Add(docBtn);
            }
            _tabContent.Add(gpgsCard);

            // Check 2: iOS Game Center
            var iosCard = CreateCard("2. iOS (Apple Game Center)", "Built directly into iOS and Unity without external third-party SDKs.");
            var isIos = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
            var iosStatusText = isIos ? "PASS: Active build target is iOS." : "INFO: Active build target is not iOS (switches automatically on iOS build).";
            iosCard.Add(CreateInfoRow("iOS Target", iosStatusText, isIos ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorTextMuted));
            iosCard.Add(CreateBulletPoint("Remember to enable the 'Game Center' capability in Xcode or via an iOS post-process build script."));
            _tabContent.Add(iosCard);

            // Check 3: Steamworks.NET
            var steamFound = IsTypeAvailable("Steamworks.SteamUserStats");
            var hasDefine = HasSteamDefine();
            var steamCard = CreateCard("3. Steam (Steamworks.NET)", "Requires Steamworks.NET and the WAGENHEIMER_NATIVESOCIAL_STEAM scripting define symbol.");

            var steamSdkText = steamFound ? "PASS: Steamworks.NET detected." : "INFO: Steamworks.NET not found (install if targeting Steam desktop).";
            steamCard.Add(CreateInfoRow("Steamworks SDK", steamSdkText, steamFound ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorTextMuted));

            var defineText = hasDefine ? "PASS: WAGENHEIMER_NATIVESOCIAL_STEAM is defined." : "WARNING: WAGENHEIMER_NATIVESOCIAL_STEAM is NOT defined.";
            steamCard.Add(CreateInfoRow("Scripting Define", defineText, hasDefine ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorWarning));

            var defineBtnRow = new VisualElement();
            defineBtnRow.style.flexDirection = FlexDirection.Row;
            defineBtnRow.style.marginTop = 8;

            if (!hasDefine)
            {
                var addDefineBtn = new Button(() =>
                {
                    SetSteamDefine(true);
                    SelectTab(Tab.Checker);
                })
                { text = "Add WAGENHEIMER_NATIVESOCIAL_STEAM Define" };
                addDefineBtn.AddToClassList("ns-btn-primary");
                defineBtnRow.Add(addDefineBtn);
            }
            else
            {
                var removeDefineBtn = new Button(() =>
                {
                    SetSteamDefine(false);
                    SelectTab(Tab.Checker);
                })
                { text = "Remove Steam Define" };
                removeDefineBtn.AddToClassList("ns-btn-secondary");
                defineBtnRow.Add(removeDefineBtn);
            }
            steamCard.Add(defineBtnRow);
            _tabContent.Add(steamCard);

            // Check 4: Bootstrap Script in Project
            var bootstrapCard = CreateCard("4. Project Bootstrap & ID Mapping", "Ensures your project has an initialization component to map LocIDs to platform IDs.");
            var hasBootstrap = AssetDatabase.FindAssets("NativeSocialBootstrap").Length > 0;
            var bootText = hasBootstrap ? "PASS: NativeSocialBootstrap found in project assets." : "RECOMMENDED: Create a bootstrap script to configure achievement IDs.";
            bootstrapCard.Add(CreateInfoRow("Bootstrap Script", bootText, hasBootstrap ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorWarning));

            var bootBtnRow = new VisualElement();
            bootBtnRow.style.flexDirection = FlexDirection.Row;
            bootBtnRow.style.marginTop = 8;

            if (!hasBootstrap)
            {
                var createScriptBtn = new Button(CreateBootstrapScriptAsset) { text = "Generate NativeSocialBootstrap.cs" };
                createScriptBtn.AddToClassList("ns-btn-primary");
                createScriptBtn.style.marginRight = 8;
                bootBtnRow.Add(createScriptBtn);
            }

            var addToSceneBtn = new Button(AddBootstrapToCurrentScene) { text = "Add Bootstrap to Current Scene" };
            addToSceneBtn.AddToClassList("ns-btn-secondary");
            bootBtnRow.Add(addToSceneBtn);

            bootstrapCard.Add(bootBtnRow);
            _tabContent.Add(bootstrapCard);
        }

        // =========================================================================
        // TAB 3: HELPER & TESTER
        // =========================================================================
        private void RenderHelperTab()
        {
            var headerCard = CreateCard("Interactive Live Helper & Tester", "Test authentication, achievement reporting, and leaderboard submissions directly in the Editor.");
            _tabContent.Add(headerCard);

            if (!EditorApplication.isPlaying)
            {
                var warnBox = new VisualElement();
                warnBox.style.backgroundColor = new Color(0.35f, 0.20f, 0.05f);
                warnBox.SetRadius(6);
                warnBox.SetPadding(12, 10);
                warnBox.style.marginBottom = 12;
                var warnLabel = new Label("Notice: Unity is in Edit Mode. Live calls to native SDKs (GPGS / Game Center / Steam) require Play Mode or standalone runtime. You can still test method invocation and verify code generation.");
                warnLabel.style.color = NativeSocialUIStyle.ColorWarning;
                warnLabel.style.whiteSpace = WhiteSpace.Normal;
                warnBox.Add(warnLabel);
                _tabContent.Add(warnBox);
            }

            // Auth Tester
            var authCard = CreateCard("Authentication Helper", "Triggers native authentication on supported platforms.");
            var authBtnRow = new VisualElement();
            authBtnRow.style.flexDirection = FlexDirection.Row;
            authBtnRow.style.flexWrap = Wrap.Wrap;

            var authBtn = new Button(() =>
            {
                NativeSocial.Authenticate(success =>
                {
                    Debug.Log($"[NativeSocial Helper] Authenticate result: {success}");
                });
            })
            { text = "Test Authenticate()" };
            authBtn.AddToClassList("ns-btn-primary");
            authBtn.style.marginRight = 8;
            authBtn.style.marginBottom = 6;
            authBtnRow.Add(authBtn);

            var authManualBtn = new Button(() =>
            {
                NativeSocial.AuthenticateManually(success =>
                {
                    Debug.Log($"[NativeSocial Helper] AuthenticateManually result: {success}");
                });
            })
            { text = "Test AuthenticateManually()" };
            authManualBtn.AddToClassList("ns-btn-secondary");
            authManualBtn.style.marginRight = 8;
            authManualBtn.style.marginBottom = 6;
            authBtnRow.Add(authManualBtn);

            var authCodeBtn = new Button(() =>
            {
                NativeSocial.GetServerAuthCode(code =>
                {
                    Debug.Log($"[NativeSocial Helper] Server Auth Code: {(string.IsNullOrEmpty(code) ? "null/empty" : code)}");
                });
            })
            { text = "Test GetServerAuthCode()" };
            authCodeBtn.AddToClassList("ns-btn-secondary");
            authCodeBtn.style.marginBottom = 6;
            authBtnRow.Add(authCodeBtn);

            authCard.Add(authBtnRow);
            _tabContent.Add(authCard);

            // Achievement Progress Reporter
            var achCard = CreateCard("Achievement Report Helper", "Test reporting incremental or completed achievements.");

            var locIdField = new TextField("LocID Key") { value = _testLocId };
            locIdField.RegisterValueChangedCallback(evt => _testLocId = evt.newValue);
            achCard.Add(locIdField);

            var deltaField = new IntegerField("Delta (Android/Steam)") { value = _testDelta };
            deltaField.RegisterValueChangedCallback(evt => _testDelta = evt.newValue);
            achCard.Add(deltaField);

            var curField = new IntegerField("Current Progress (iOS)") { value = _testCurrent };
            curField.RegisterValueChangedCallback(evt => _testCurrent = evt.newValue);
            achCard.Add(curField);

            var totalField = new IntegerField("Total Required (iOS)") { value = _testTotal };
            totalField.RegisterValueChangedCallback(evt => _testTotal = evt.newValue);
            achCard.Add(totalField);

            var compToggle = new Toggle("Completed Immediately") { value = _testCompleted };
            compToggle.RegisterValueChangedCallback(evt => _testCompleted = evt.newValue);
            achCard.Add(compToggle);

            var reportRow = new VisualElement();
            reportRow.style.flexDirection = FlexDirection.Row;
            reportRow.style.marginTop = 10;

            var sendReportBtn = new Button(() =>
            {
                Debug.Log($"[NativeSocial Helper] Reporting: LocID={_testLocId}, Delta={_testDelta}, Current={_testCurrent}, Total={_testTotal}, Completed={_testCompleted}");
                NativeSocial.Report(_testLocId, _testDelta, _testCurrent, _testTotal, _testCompleted);
            })
            { text = "Report Achievement" };
            sendReportBtn.AddToClassList("ns-btn-primary");
            sendReportBtn.style.marginRight = 8;
            reportRow.Add(sendReportBtn);

            var showAchUiBtn = new Button(() =>
            {
                var shown = NativeSocial.ShowAchievementsUI();
                Debug.Log($"[NativeSocial Helper] ShowAchievementsUI returned: {shown}");
            })
            { text = "Show Platform Achievements UI" };
            showAchUiBtn.AddToClassList("ns-btn-secondary");
            reportRow.Add(showAchUiBtn);

            achCard.Add(reportRow);
            _tabContent.Add(achCard);

            // Leaderboard Helper
            var lbCard = CreateCard("Leaderboard Submit Helper", "Test posting leaderboard scores to platform services.");

            var lbField = new TextField("Leaderboard LocID") { value = _testLeaderboardId };
            lbField.RegisterValueChangedCallback(evt => _testLeaderboardId = evt.newValue);
            lbCard.Add(lbField);

            var scoreField = new LongField("Score") { value = _testScore };
            scoreField.RegisterValueChangedCallback(evt => _testScore = evt.newValue);
            lbCard.Add(scoreField);

            var lbRow = new VisualElement();
            lbRow.style.flexDirection = FlexDirection.Row;
            lbRow.style.marginTop = 10;

            var submitLbBtn = new Button(() =>
            {
                Debug.Log($"[NativeSocial Helper] Submitting Score: LocID={_testLeaderboardId}, Score={_testScore}");
                NativeSocial.SubmitScore(_testLeaderboardId, _testScore);
            })
            { text = "Submit Score" };
            submitLbBtn.AddToClassList("ns-btn-primary");
            submitLbBtn.style.marginRight = 8;
            lbRow.Add(submitLbBtn);

            var showLbUiBtn = new Button(() =>
            {
                var shown = NativeSocial.ShowLeaderboardUI(_testLeaderboardId);
                Debug.Log($"[NativeSocial Helper] ShowLeaderboardUI returned: {shown}");
            })
            { text = "Show Leaderboard UI" };
            showLbUiBtn.AddToClassList("ns-btn-secondary");
            lbRow.Add(showLbUiBtn);

            lbCard.Add(lbRow);
            _tabContent.Add(lbCard);
        }

        // =========================================================================
        // TAB 4: GUIDES & SNIPPETS
        // =========================================================================
        private void RenderGuidesTab()
        {
            var headerCard = CreateCard("Documentation & Code Snippets", "Ready-to-copy code patterns for game initialization and runtime calls.");
            _tabContent.Add(headerCard);

            // Snippet 1: Initialization
            var initSnippet =
@"// Game startup: Map your game's LocIDs to platform-specific achievement IDs
var androidMap = new Dictionary<string, string> {
    { ""ach_first_win"", ""CgkI_aXR36YSEAIQAQ"" }
};
var iosMap = new Dictionary<string, string> {
    { ""ach_first_win"", ""grp.ach_first_win"" }
};
var steamMap = new Dictionary<string, SteamEntry> {
    { ""ach_first_win"", new SteamEntry(""STAT_WINS"", ""ACH_FIRST_WIN"") }
};

NativeSocial.Initialize(androidMap, iosMap, steamMap);";
            _tabContent.Add(CreateCodeCard("1. Initializing Achievement Maps", initSnippet));

            // Snippet 2: Reporting
            var reportSnippet =
@"// Report progress safely on any platform:
// Android & Steam use 'delta'; iOS uses 'current' / 'total'.
NativeSocial.Report(
    locId: ""ach_first_win"",
    delta: 1,
    current: currentWins,
    total: 10,
    completed: currentWins >= 10
);";
            _tabContent.Add(CreateCodeCard("2. Reporting Achievement Progress", reportSnippet));

            // Snippet 3: Leaderboard
            var lbSnippet =
@"// Submit high score
NativeSocial.SubmitScore(""lb_high_score"", 15420);

// Open platform native UI
NativeSocial.ShowLeaderboardUI(""lb_high_score"");";
            _tabContent.Add(CreateCodeCard("3. Leaderboard Score Submission", lbSnippet));

            // Snippet 4: Re-syncing local progress
            var syncSnippet =
@"// Call this immediately after sign-in succeeds to push any offline-earned achievements
var completedLocIds = localSaveData.GetCompletedAchievementKeys();
NativeSocial.SyncCompleted(completedLocIds);";
            _tabContent.Add(CreateCodeCard("4. Syncing Offline / Completed Progress", syncSnippet));
        }

        // =========================================================================
        // TAB 5: ABOUT
        // =========================================================================
        private void RenderAboutTab()
        {
            var aboutCard = CreateCard("About Native Social", "A core component of the Wagenheimer Game Tooling Suite.");
            aboutCard.Add(CreateBulletPoint("Author: Cezar Wagenheimer"));
            aboutCard.Add(CreateBulletPoint("License: MIT"));
            aboutCard.Add(CreateBulletPoint("Repository: github.com/wagenheimer/UnityNativeSocial"));
            aboutCard.Add(CreateBulletPoint("Designed for seamless cross-platform deployment on Android, iOS, and Steam with 0 deprecated warnings."));

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 12;

            var docsBtn = new Button(() => Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial#readme"))
            { text = "Documentation" };
            docsBtn.AddToClassList("ns-btn-secondary");
            docsBtn.style.marginRight = 8;
            btnRow.Add(docsBtn);

            var issueBtn = new Button(() => Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial/issues/new"))
            { text = "Report Issue" };
            issueBtn.AddToClassList("ns-btn-secondary");
            btnRow.Add(issueBtn);

            aboutCard.Add(btnRow);
            _tabContent.Add(aboutCard);

            var hubCard = CreateCard("Ecosystem Integration", "Manage and update all Wagenheimer tools from a unified dashboard.");
            var hubBtn = new Button(() => PackageHubWindow.Open())
            { text = "Open Wagenheimer Package Hub" };
            hubBtn.AddToClassList("ns-btn-primary");
            hubBtn.style.marginTop = 6;
            hubCard.Add(hubBtn);
            _tabContent.Add(hubCard);
        }

        // =========================================================================
        // UI HELPERS
        // =========================================================================
        private VisualElement CreateCard(string title, string description)
        {
            var card = new VisualElement();
            card.AddToClassList("ns-card");
            card.style.backgroundColor = NativeSocialUIStyle.ColorCardDark;
            card.SetBorder(1, NativeSocialUIStyle.ColorCardBorder);
            card.SetRadius(8);
            card.SetPadding(16, 14);
            card.style.marginBottom = 12;

            var header = new VisualElement();
            header.AddToClassList("ns-card-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.137f, 0.192f, 0.267f);
            header.style.paddingBottom = 8;
            header.style.marginBottom = 10;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("ns-card-title");
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = Color.white;
            header.Add(titleLabel);
            card.Add(header);

            if (!string.IsNullOrEmpty(description))
            {
                var descLabel = new Label(description);
                descLabel.AddToClassList("ns-card-desc");
                descLabel.style.fontSize = 11;
                descLabel.style.color = NativeSocialUIStyle.ColorTextMuted;
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.marginBottom = 10;
                card.Add(descLabel);
            }

            return card;
        }

        private VisualElement CreateCodeCard(string title, string code)
        {
            var card = CreateCard(title, null);

            var codeBox = new VisualElement();
            codeBox.AddToClassList("ns-code-block");
            codeBox.style.backgroundColor = NativeSocialUIStyle.ColorCodeBg;
            codeBox.SetBorder(1, new Color(0.118f, 0.161f, 0.231f));
            codeBox.SetRadius(6);
            codeBox.SetPadding(12, 10);

            var codeLabel = new Label(code);
            codeLabel.style.color = new Color(0.220f, 0.741f, 0.973f);
            codeLabel.style.fontSize = 11;
            codeLabel.style.whiteSpace = WhiteSpace.Normal;
            codeBox.Add(codeLabel);
            card.Add(codeBox);

            var copyBtn = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = code;
                Debug.Log($"[NativeSocial] Copied snippet '{title}' to clipboard.");
            })
            { text = "Copy Snippet" };
            copyBtn.AddToClassList("ns-btn-secondary");
            copyBtn.style.marginTop = 8;
            copyBtn.style.alignSelf = Align.FlexEnd;
            card.Add(copyBtn);

            return card;
        }

        private VisualElement CreateInfoRow(string label, string value, Color valueColor)
        {
            var row = new VisualElement();
            row.AddToClassList("ns-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 6;

            var l = new Label(label);
            l.style.color = NativeSocialUIStyle.ColorTextMuted;
            l.style.fontSize = 12;

            var v = new Label(value);
            v.style.color = valueColor;
            v.style.fontSize = 12;
            v.style.unityFontStyleAndWeight = FontStyle.Bold;

            row.Add(l);
            row.Add(v);
            return row;
        }

        private VisualElement CreateBulletPoint(string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 4;

            var bullet = new Label("\u2022 ");
            bullet.style.color = NativeSocialUIStyle.ColorPrimary;
            bullet.style.fontSize = 12;

            var lbl = new Label(text);
            lbl.style.color = NativeSocialUIStyle.ColorText;
            lbl.style.fontSize = 11;
            lbl.style.whiteSpace = WhiteSpace.Normal;

            row.Add(bullet);
            row.Add(lbl);
            return row;
        }

        // =========================================================================
        // UTILITIES
        // =========================================================================
        private static bool IsTypeAvailable(string typeFullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Any(t => t.FullName == typeFullName);
        }

        private static bool HasSteamDefine()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
#if UNITY_2021_2_OR_NEWER
            var namedGroup = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            var defines = PlayerSettings.GetScriptingDefineSymbols(namedGroup);
#else
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
            return defines.Split(';').Contains("WAGENHEIMER_NATIVESOCIAL_STEAM");
        }

        private static void SetSteamDefine(bool enable)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
#if UNITY_2021_2_OR_NEWER
            var namedGroup = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            var defines = PlayerSettings.GetScriptingDefineSymbols(namedGroup).Split(';').Select(d => d.Trim()).Where(d => !string.IsNullOrEmpty(d)).ToList();
#else
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';').Select(d => d.Trim()).Where(d => !string.IsNullOrEmpty(d)).ToList();
#endif
            const string symbol = "WAGENHEIMER_NATIVESOCIAL_STEAM";
            if (enable && !defines.Contains(symbol))
            {
                defines.Add(symbol);
            }
            else if (!enable && defines.Contains(symbol))
            {
                defines.Remove(symbol);
            }

            var joined = string.Join(";", defines);
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(namedGroup, joined);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, joined);
#endif
            AssetDatabase.SaveAssets();
            Debug.Log($"[NativeSocial] Scripting defines updated. {symbol} is {(enable ? "ENABLED" : "DISABLED")}.");
        }

        private static void CreateBootstrapScriptAsset()
        {
            var dir = "Assets/Scripts/Social";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var path = Path.Combine(dir, "NativeSocialBootstrap.cs");
            if (File.Exists(path))
            {
                EditorUtility.DisplayDialog("Script Exists", $"A script already exists at {path}", "OK");
                return;
            }

            var samplePath = "Packages/com.wagenheimer.nativesocial/Samples~/DefaultSetup/NativeSocialBootstrap.cs";
            string content;
            if (File.Exists(samplePath))
            {
                content = File.ReadAllText(samplePath);
            }
            else
            {
                content =
@"using UnityEngine;
using Wagenheimer.NativeSocial;

public class NativeSocialBootstrap : MonoBehaviour
{
    private void Awake()
    {
        var androidMap = new System.Collections.Generic.Dictionary<string, string>();
        var iosMap = new System.Collections.Generic.Dictionary<string, string>();
        var steamMap = new System.Collections.Generic.Dictionary<string, SteamEntry>();

        NativeSocial.Initialize(androidMap, iosMap, steamMap);
        Debug.Log(""[NativeSocial] Initialized from Bootstrap."");
    }
}
";
            }

            File.WriteAllText(path, content);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Created", $"NativeSocialBootstrap.cs created at {path}", "OK");
        }

        private static void AddBootstrapToCurrentScene()
        {
            var go = new GameObject("NativeSocialBootstrap");
            Undo.RegisterCreatedObjectUndo(go, "Create NativeSocialBootstrap");
            Selection.activeGameObject = go;
            Debug.Log("[NativeSocial] Created 'NativeSocialBootstrap' GameObject in active scene.");
        }
    }
}
