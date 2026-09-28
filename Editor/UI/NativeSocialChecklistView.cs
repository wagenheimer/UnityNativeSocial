using System.Linq;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>Persistent manual release checklist (items that cannot be verified from source).</summary>
    internal sealed class NativeSocialChecklistView
    {
        private const string PrefKeyPrefix = "nativesocial_chk_";

        private static readonly (string Category, string Id, string Label, string Description)[] Items =
        {
            ("Setup", "setup_gpgs", "Google Play Games plugin installed & configured", "Web client ID / OAuth2 configured in the GPGS setup wizard, and the resources it generates are committed."),
            ("Setup", "setup_gamecenter", "Game Center capability enabled", "Enabled in Xcode's Signing & Capabilities, or via an iOS post-process build script."),
            ("Setup", "setup_steam_define", "Steamworks.NET installed & WAGENHEIMER_NATIVESOCIAL_STEAM active", "Steam achievement/leaderboard calls compile out entirely without this define."),
            ("Setup", "setup_map_asset", "AchievementTierMap asset created and referenced from the bootstrap", "One asset per game; built once at startup into the maps NativeSocial.Initialize needs."),
            ("Setup", "setup_initialize", "NativeSocial.Initialize(...) called before any Report/SubmitScore call", "Every call silently no-ops until Initialize has run once."),
            ("Google Play Console", "gpc_listing", "App listing created in Google Play Console", "Required before any achievement can be configured."),
            ("Google Play Console", "gpc_achievements", "All achievements from the game's achievement doc created there", "Each one issues the ID pasted into GooglePlayId."),
            ("Google Play Console", "gpc_ids_pasted", "IDs pasted into the AchievementTierMap asset", "Empty GooglePlayId is safe (no-op) but blocks real unlocks."),
            ("Google Play Console", "gpc_tested", "Tested sign-in + unlock on a real Android device", "Editor Play Mode cannot reach Google Play Games services."),
            ("App Store Connect", "asc_listing", "App record created in App Store Connect", "Required before any Game Center achievement can be configured."),
            ("App Store Connect", "asc_achievements", "All achievements from the game's achievement doc created there", "Each one issues the ID pasted into AppleId."),
            ("App Store Connect", "asc_ids_pasted", "IDs pasted into the AchievementTierMap asset", "Empty AppleId is safe (no-op) but blocks real unlocks."),
            ("App Store Connect", "asc_tested", "Tested sign-in + unlock on a real iOS device", "Editor Play Mode cannot reach Game Center."),
            ("Steam", "steam_stats", "Steamworks partner-site stats/achievements configured", "Names must match the SteamStat values in the AchievementTierMap asset exactly."),
            ("Steam", "steam_tested", "Tested via the Steam client (not just Steamworks.NET compiling)", "Requires steam_appid.txt and the Steam client running."),
            ("Release", "rel_sync", "SyncCompleted(...) called right after auth succeeds", "Re-syncs offline progress earned before the platform account was authenticated."),
            ("Release", "rel_version", "Package version bumped and CHANGELOG generated", "Automated by bump-version.yml from Conventional Commits.")
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
                "Manual checks that cannot be verified from source. Saved per machine.");

            var topRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, marginBottom = 8 } };
            _progressLabel = new Label();
            _progressLabel.style.fontSize = 11;
            _progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _progressLabel.style.color = NativeSocialUIStyle.ColorSuccess;
            topRow.Add(_progressLabel);
            topRow.Add(NativeSocialUIStyle.CreateButton("↺ Reset All", ResetChecklist));
            headerCard.Add(topRow);
            Root.Add(headerCard);

            foreach (var group in Items.GroupBy(i => i.Category))
            {
                var card = NativeSocialUIStyle.CreateCard(group.Key);
                foreach (var item in group)
                    card.Add(CreateItem(item.Id, item.Label, item.Description));
                Root.Add(card);
            }

            UpdateProgress();
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
