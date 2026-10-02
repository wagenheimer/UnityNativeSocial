using System.Linq;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>
    /// Persistent manual release checklist (items that cannot be verified from source), grouped so the
    /// per-platform wiring is spelled out in detail — Android (Google Play Games), iOS (Game Center) and
    /// Steam (Steamworks.NET), plus the store-console work and the release steps.
    /// </summary>
    internal sealed class NativeSocialChecklistView
    {
        private const string PrefKeyPrefix = "nativesocial_chk_";

        private static readonly (string Category, string Description)[] Groups =
        {
            ("Project setup",
                "Wire the package into the game once: the maps are registered at startup and every later call goes through NativeSocial."),
            ("Steam (Steamworks.NET)",
                "Steam is desktop-only and is compiled in through the WAGENHEIMER_NATIVESOCIAL_STEAM define (auto-set when Steamworks.NET is installed). If the project already syncs Steam directly, you do not need NativeSocial's Steam path — just make sure only one of the two owns it."),
            ("Android (Google Play Games)",
                "Google Play Games v2 is a UPM package installed from Google's git repository; the game then talks to it only through NativeSocial. Achievements no-op until the player is signed in and the matching Google Play ID is filled in."),
            ("iOS (Game Center)",
                "Game Center is built into Unity's iOS support — no separate plugin to install. Same rule as Android: no sign-in or no filled ID means a silent no-op."),
            ("Google Play Console",
                "Create the achievements (18 trophies x 3 tiers = 54) and paste the IDs they issue into the map asset's GooglePlayId column."),
            ("App Store Connect",
                "Create the same 54 achievements for Game Center and paste their IDs into the map asset's AppleId column."),
            ("Steamworks partner site",
                "Configure the 54 stats/achievements by name. No public API can create these for you — this part stays manual."),
            ("Release",
                "Final checks before shipping a build.")
        };

        private static readonly (string Category, string Id, string Label, string Description)[] Items =
        {
            // Project setup ─────────────────────────────────────────────────────────
            ("Project setup", "setup_package", "NativeSocial installed (UPM git URL)",
                "Installed as com.wagenheimer.nativesocial from https://github.com/wagenheimer/UnityNativeSocial.git so updates flow through Package Hub."),
            ("Project setup", "setup_map_asset", "AchievementTierMap asset created and complete",
                "One asset (Assets > Create > Wagenheimer > Native Social > Achievement Tier Map) with one row per trophy tier. SteamStat is required for every row that ships on Steam; GooglePlayId/AppleId are filled in as each console issues them."),
            ("Project setup", "setup_initialize", "NativeSocial.Initialize(...) called once at startup",
                "Build the maps from the asset (BuildAndroidMap/BuildIosMap/BuildSteamMap) and pass them to Initialize() before any Report call. Until Initialize runs, every Report/SubmitScore silently no-ops."),
            ("Project setup", "setup_locid", "All Report/SyncCompleted calls use AchievementTierMap.LocId(...)",
                "Never hand-format the \"Trophy{N}_{tier}\" string. The map builders and the reporting call sites must produce the exact same key or the report stops matching any platform ID."),
            ("Project setup", "setup_report", "Progress reported through NativeSocial.Report(...)",
                "Report(locId, delta, current, total, completed): Android/Steam use delta, iOS uses current/total for its percentage. Pass completed: true at the threshold — it unlocks outright on every platform."),
            ("Project setup", "setup_sync", "SyncCompleted(...) called right after sign-in",
                "Re-pushes achievements already earned offline / on another device. Call it immediately after Authenticate succeeds, using the completed LocIds from the local save."),
            ("Project setup", "setup_owner", "One owner per platform (no double-reporting)",
                "Decide whether NativeSocial or an existing direct integration drives each platform. In projects that already had Steam (e.g. a SteamManager/AchievementsSteam script calling Steamworks directly), keep using that for Steam and pass null/empty for the steam map — enabling both unlocks each achievement twice."),

            // Steam ─────────────────────────────────────────────────────────────────
            ("Steam (Steamworks.NET)", "steam_installed", "Steamworks.NET installed and define active",
                "WAGENHEIMER_NATIVESOCIAL_STEAM is set automatically from the com.rlabrecque.steamworks.net version define. Without it the entire Steam path compiles out (not even a runtime check)."),
            ("Steam (Steamworks.NET)", "steam_ready", "NativeSocial.SteamReady set true after SteamAPI init",
                "Set NativeSocial.SteamReady = true once SteamManager reports Initialized (or after SteamAPI.Init succeeds). Every Steam stat/achievement call no-ops until then."),
            ("Steam (Steamworks.NET)", "steam_map", "steamMap passed to Initialize (only if NativeSocial owns Steam)",
                "Use achievementMap.BuildSteamMap(). It assumes the achievement API name equals the stat name (Trophy{N}_{tier}_Status); pass a custom Dictionary<string, SteamEntry> if your Steam achievement names differ."),
            ("Steam (Steamworks.NET)", "steam_partner", "54 stats/achievements configured on the partner site",
                "Names must match the SteamStat values exactly (Trophy1_1_Status ... Trophy18_3_Status). Steamworks has no public API for bulk creation, so this is manual."),
            ("Steam (Steamworks.NET)", "steam_appid", "steam_appid.txt present for local testing",
                "Put steam_appid.txt (containing just the App ID) next to the built .exe for development; it is not needed in the shipped build."),
            ("Steam (Steamworks.NET)", "steam_test", "Tested through the Steam client, not just in the Editor",
                "Stat/achievement calls only reach Steam when the game is launched by the Steam client (or with steam_appid.txt + the client running). Editor Play Mode alone will not show progress."),

            // Android ───────────────────────────────────────────────────────────────
            ("Android (Google Play Games)", "android_install", "GPGS plugin installed via UPM / Git",
                "Use the Setup Audit's \"Install Google Play Games (UPM)\" button (or add https://github.com/playgameservices/play-games-plugin-for-unity.git?path=Assets/Public/GooglePlayGames/com.google.play.games#v2.3.0). Do NOT import the .unitypackage — a loose Assets/ copy is not a resolvable package and never sets the define."),
            ("Android (Google Play Games)", "android_define", "WAGENHEIMER_NATIVESOCIAL_GPGS active",
                "Auto-set by the com.google.play.games version define. If the Setup Audit warns it is missing, the plugin is installed as loose Assets/ files instead of a package."),
            ("Android (Google Play Games)", "android_oauth", "Web client ID / OAuth configured and resources committed",
                "Run the GPGS setup wizard (Window > Google Play Games > Setup > Android setup), fill in the Web client ID from your Play Console OAuth client, and commit the generated resources (Plugins/Android and the GooglePlayGames resources)."),
            ("Android (Google Play Games)", "android_ids", "GooglePlayId filled in for every shipped tier",
                "Empty IDs are safe (Report no-ops) but the achievement will never unlock in-game. Create them in the Play Console, then paste the IDs into the map asset (or let AppDeployHub create/push them)."),
            ("Android (Google Play Games)", "android_auth", "Sign-in called at startup and IsAuthenticated tracked",
                "Call NativeSocial.Authenticate(...) early; on success it sets NativeSocial.IsAuthenticated and achievement calls start working. Use AuthenticateManually(...) as the retry path when automatic sign-in fails (GPGS v11+ needs it to show the profile-creation UI)."),
            ("Android (Google Play Games)", "android_sync", "SyncCompleted(...) called right after sign-in",
                "After auth succeeds, re-push the locally completed LocIds so offline progress is restored on Google Play Games."),
            ("Android (Google Play Games)", "android_test", "Tested sign-in + unlock on a real Android device",
                "Editor Play Mode cannot reach Play Games services. Sign in with a test account that is added to the Play Console's testing list, on a real device or an emulator with Play Services."),

            // iOS ───────────────────────────────────────────────────────────────────
            ("iOS (Game Center)", "ios_capability", "Game Center capability enabled for the iOS build",
                "Enable it in Xcode's Signing & Capabilities, or via an iOS post-process build script so every build gets it."),
            ("iOS (Game Center)", "ios_ids", "AppleId filled in for every shipped tier",
                "Create the achievements in App Store Connect (Features > Game Center > Achievements) and paste their IDs into the map asset's AppleId column."),
            ("iOS (Game Center)", "ios_auth", "Game Center authentication runs and SyncCompleted follows",
                "NativeSocial.Authenticate(...) drives Game Center on iOS. Game Center reports a 0-100% percentage, so Report needs a non-zero total (the tier threshold) to compute it."),
            ("iOS (Game Center)", "ios_test", "Tested sign-in + unlock on a real iOS device",
                "Editor Play Mode cannot reach Game Center. Use a sandbox Game Center account on the device; clear the app between runs to re-test first-time flows."),

            // Google Play Console ───────────────────────────────────────────────────
            ("Google Play Console", "gpc_listing", "App listing created in Google Play Console",
                "Required before any achievement can be configured."),
            ("Google Play Console", "gpc_achievements", "All 54 achievements created and published",
                "One per trophy tier. Each one issues the alphanumeric ID that goes into the map asset's GooglePlayId column."),
            ("Google Play Console", "gpc_ids", "IDs pasted into the AchievementTierMap asset",
                "Keep the human-readable docs table and the asset in sync; the asset is what runs."),
            ("Google Play Console", "gpc_incremental", "Incremental achievements configured when used",
                "If a tier counts steps, mark IsIncremental and set StepsToUnlock on the entry so the Console side matches the in-game counter."),

            // App Store Connect ─────────────────────────────────────────────────────
            ("App Store Connect", "asc_listing", "App record created in App Store Connect",
                "Required before any Game Center achievement can be configured."),
            ("App Store Connect", "asc_achievements", "All 54 achievements created",
                "One per trophy tier; each issues the ID that goes into the map asset's AppleId column."),
            ("App Store Connect", "asc_ids", "IDs pasted into the AchievementTierMap asset",
                "Empty AppleId is safe (no-op) but blocks real unlocks on iOS."),

            // Steamworks partner site ───────────────────────────────────────────────
            ("Steamworks partner site", "steamws_stats", "Stats and achievements created by name",
                "Names must match the AchievementTierMap's SteamStat values exactly (Trophy{N}_{tier}_Status). If NativeSocial owns Steam, it assumes the achievement API name equals the stat name."),
            ("Steamworks partner site", "steamws_test", "Tested via the Steam client",
                "Requires the Steam client running and either the game launched from Steam or steam_appid.txt beside the build."),

            // Release ───────────────────────────────────────────────────────────────
            ("Release", "rel_sync", "SyncCompleted verified on a fresh install of each platform",
                "Proves offline-earned progress is restored after sign-in on Android/iOS/Steam."),
            ("Release", "rel_empty_safe", "Shipped with empty IDs where a platform is not targeted",
                "Leaving GooglePlayId/AppleId empty is safe: Report no-ops for that LocId instead of throwing. Only fill the platforms you actually ship."),
            ("Release", "rel_version", "Package version bumped and CHANGELOG generated",
                "Automated by the repo's bump-version.yml from Conventional Commits.")
        };

        public VisualElement Root { get; }

        private Label _progressLabel;

        public NativeSocialChecklistView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);
            BuildUI();
        }

        private void BuildUI()
        {
            var headerCard = NativeSocialUIStyle.CreateCard("📋 Release Review Checklist",
                "Manual checks that cannot be verified from source. Saved per machine. " +
                "Each platform group spells out exactly how the integration is wired and what must exist on the store side.");

            var topRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, marginBottom = 8 } };
            _progressLabel = new Label();
            _progressLabel.style.fontSize = 11;
            _progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _progressLabel.style.color = NativeSocialUIStyle.ColorSuccess;
            topRow.Add(_progressLabel);
            topRow.Add(NativeSocialUIStyle.CreateButton("↺ Reset All", ResetChecklist));
            headerCard.Add(topRow);
            Root.Add(headerCard);

            Root.Add(CreateHowItWorksCard());

            foreach (var group in Groups)
            {
                var card = NativeSocialUIStyle.CreateCard(group.Category, group.Description);
                foreach (var item in Items.Where(i => i.Category == group.Category))
                    card.Add(CreateItem(item.Id, item.Label, item.Description));
                Root.Add(card);
            }

            UpdateProgress();
        }

        private static VisualElement CreateHowItWorksCard()
        {
            var card = NativeSocialUIStyle.CreateCard("How the integration works",
                "One package, one API, three platforms — the platform is chosen at build time, not at runtime.");
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "Initialize once: NativeSocial.Initialize(androidMap, iosMap, steamMap) stores LocId → platform-ID maps. Nothing is sent yet."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "Report per change: NativeSocial.Report(locId, delta, current, total, completed) routes to whichever platform was compiled into this build."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "The platform is fixed per build: UNITY_ANDROID → Google Play Games, UNITY_IOS → Game Center, WAGENHEIMER_NATIVESOCIAL_STEAM → Steamworks.NET. A Windows build with Steamworks.NET installed compiles the Steam path."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "Steam already handled elsewhere? Perfectly fine — NativeSocial's Steam path is optional. Use one owner for Steam, never both, or a trophy unlocks twice."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "Nothing is lost when a platform is unfinished: Report no-ops for an unmapped LocId, and for Android/iOS until the player is signed in."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint(
                "SyncCompleted(completedLocIds) right after sign-in re-pushes achievements earned offline or on another device."));
            return card;
        }

        private VisualElement CreateItem(string id, string label, string description)
        {
            var element = new VisualElement();
            element.AddToClassList("ns-checklist-item");

            bool isChecked = EditorPrefs.GetBool(PrefKeyPrefix + id, false);
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

            var title = new Label(label);
            title.AddToClassList("ns-checklist-title");
            textCol.Add(title);

            var desc = new Label(description);
            desc.AddToClassList("ns-checklist-desc");
            desc.style.whiteSpace = WhiteSpace.Normal;
            textCol.Add(desc);

            element.Add(textCol);
            return element;
        }

        private void UpdateProgress()
        {
            int total = Items.Length;
            int done = Items.Count(i => EditorPrefs.GetBool(PrefKeyPrefix + i.Id, false));
            _progressLabel.text = $"Progress: {done} / {total} verified ({done * 100 / total}%)";
        }

        private void ResetChecklist()
        {
            if (!EditorUtility.DisplayDialog("Reset Checklist", "Reset all checklist items to unchecked?", "RESET", "CANCEL"))
                return;

            foreach (var item in Items)
                EditorPrefs.DeleteKey(PrefKeyPrefix + item.Id);

            Root.Clear();
            BuildUI();
        }
    }
}
