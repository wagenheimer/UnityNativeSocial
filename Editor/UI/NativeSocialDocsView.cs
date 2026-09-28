using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>Ready-to-copy code snippets, About info and the update checker.</summary>
    internal sealed class NativeSocialDocsView
    {
        public VisualElement Root { get; }

        public NativeSocialDocsView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);
            BuildUI();
        }

        private void BuildUI()
        {
            var headerCard = NativeSocialUIStyle.CreateCard("Documentation & Code Snippets",
                "Ready-to-copy code patterns for game initialization and runtime calls.");
            Root.Add(headerCard);

            const string initSnippet =
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
            Root.Add(NativeSocialUIStyle.CreateCodeCard("1. Initializing Achievement Maps", initSnippet));

            const string reportSnippet =
@"// Report progress safely on any platform:
// Android & Steam use 'delta'; iOS uses 'current' / 'total'.
NativeSocial.Report(
    locId: ""ach_first_win"",
    delta: 1,
    current: currentWins,
    total: 10,
    completed: currentWins >= 10
);";
            Root.Add(NativeSocialUIStyle.CreateCodeCard("2. Reporting Achievement Progress", reportSnippet));

            const string lbSnippet =
@"// Submit high score
NativeSocial.SubmitScore(""lb_high_score"", 15420);

// Open platform native UI
NativeSocial.ShowLeaderboardUI(""lb_high_score"");";
            Root.Add(NativeSocialUIStyle.CreateCodeCard("3. Leaderboard Score Submission", lbSnippet));

            const string syncSnippet =
@"// Call this immediately after sign-in succeeds to push any offline-earned achievements
var completedLocIds = localSaveData.GetCompletedAchievementKeys();
NativeSocial.SyncCompleted(completedLocIds);";
            Root.Add(NativeSocialUIStyle.CreateCodeCard("4. Syncing Offline / Completed Progress", syncSnippet));

            const string tierMapSnippet =
@"// Reusable, game-agnostic mapping asset: fill it in via
// Assets > Create > Wagenheimer > Native Social > Achievement Tier Map
[SerializeField] private AchievementTierMap achievementMap;

void Awake()
{
    NativeSocial.Initialize(
        achievementMap.BuildAndroidMap(),
        achievementMap.BuildIosMap(),
        achievementMap.BuildSteamMap());
}

// Report a tier by trophy number, no hand-formatted strings:
NativeSocial.Report(AchievementTierMap.LocId(trophyNumber: 4, tier: 2), delta: 1, current: 2, total: 3, completed: false);";
            Root.Add(NativeSocialUIStyle.CreateCodeCard("5. Using AchievementTierMap (recommended)", tierMapSnippet));

            BuildAboutCard();
        }

        private void BuildAboutCard()
        {
            var aboutCard = NativeSocialUIStyle.CreateCard("About Native Social", "A core component of the Wagenheimer Game Tooling Suite.");
            aboutCard.Add(NativeSocialUIStyle.CreateBulletPoint("Author: Cezar Wagenheimer"));
            aboutCard.Add(NativeSocialUIStyle.CreateBulletPoint("License: MIT"));
            aboutCard.Add(NativeSocialUIStyle.CreateBulletPoint("Repository: github.com/wagenheimer/UnityNativeSocial"));
            aboutCard.Add(NativeSocialUIStyle.CreateBulletPoint("Designed for seamless cross-platform deployment on Android, iOS, and Steam with 0 deprecated warnings."));

            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 12, flexWrap = Wrap.Wrap } };
            btnRow.Add(NativeSocialUIStyle.CreateButton("Documentation", () => Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial#readme")));
            btnRow.Add(NativeSocialUIStyle.CreateButton("Report Issue", () => Application.OpenURL("https://github.com/wagenheimer/UnityNativeSocial/issues/new")));
            btnRow.Add(NativeSocialUIStyle.CreateButton("🔄 Check for Updates", () => UpdateChecker.CheckForUpdate(force: true), primary: true));
            aboutCard.Add(btnRow);
            Root.Add(aboutCard);

            var hubCard = NativeSocialUIStyle.CreateCard("Ecosystem Integration", "Manage and update all Wagenheimer tools from a unified dashboard.");
            hubCard.Add(NativeSocialUIStyle.CreateButton("Open Wagenheimer Package Hub", PackageHubWindow.ShowWindow, primary: true));
            Root.Add(hubCard);
        }
    }
}
