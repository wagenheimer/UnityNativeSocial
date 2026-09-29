using System.Linq;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>Shows every <see cref="AchievementTierMap"/> asset found in the project and its per-platform completeness.</summary>
    internal sealed class NativeSocialAchievementsView
    {
        public VisualElement Root { get; }

        public NativeSocialAchievementsView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);
            BuildUI();
        }

        private void BuildUI()
        {
            var headerCard = NativeSocialUIStyle.CreateCard("🏆 Achievement Tier Maps",
                "Every AchievementTierMap asset in the project and how many of its entries still need a Google Play / Apple ID.");

            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            btnRow.Add(NativeSocialUIStyle.CreateButton("↻ Refresh", Rebuild));
            btnRow.Add(NativeSocialUIStyle.CreateButton("+ Create New Map", () =>
            {
                NativeSocialAudit.CreateAchievementTierMapAsset();
                Rebuild();
            }, primary: true));
            headerCard.Add(btnRow);
            Root.Add(headerCard);

            _listContainer = new VisualElement();
            Root.Add(_listContainer);

            Rebuild();
        }

        private VisualElement _listContainer;

        private void Rebuild()
        {
            _listContainer.Clear();

            var maps = NativeSocialAudit.FindAllAchievementTierMaps();
            if (maps.Count == 0)
            {
                _listContainer.Add(NativeSocialUIStyle.CreateCallout(
                    "No AchievementTierMap asset found yet. Create one to hold this game's per-platform achievement IDs.",
                    AuditSeverity.Warning));
                return;
            }

            foreach (var map in maps)
            {
                var path = AssetDatabase.GetAssetPath(map);
                var card = NativeSocialUIStyle.CreateCard(path, $"{map.Entries.Count} entries.");

                int missingGoogle = map.CountMissingGooglePlay();
                int missingApple = map.CountMissingApple();
                int total = map.Entries.Count;

                card.Add(NativeSocialUIStyle.CreateInfoRow("Google Play IDs",
                    total == 0 ? "no entries" : $"{total - missingGoogle} / {total} filled in",
                    missingGoogle == 0 && total > 0 ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorWarning));

                card.Add(NativeSocialUIStyle.CreateInfoRow("Apple Game Center IDs",
                    total == 0 ? "no entries" : $"{total - missingApple} / {total} filled in",
                    missingApple == 0 && total > 0 ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorWarning));

                int steamEntries = map.Entries.Count(e => !string.IsNullOrEmpty(e.SteamStat));
                card.Add(NativeSocialUIStyle.CreateInfoRow("Steam stats",
                    total == 0 ? "no entries" : $"{steamEntries} / {total} filled in",
                    NativeSocialUIStyle.ColorTextMuted));

                var btnRow2 = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };

                var selectBtn = NativeSocialUIStyle.CreateButton("Select Asset", () =>
                {
                    Selection.activeObject = map;
                    EditorGUIUtility.PingObject(map);
                });
                btnRow2.Add(selectBtn);

                var exportBtn = NativeSocialUIStyle.CreateButton("⬆ Export for AppDeployHub", () =>
                    AchievementExchangeExporter.ExportToFile(map, PlayerSettings.productName), primary: true);
                exportBtn.style.marginLeft = 8;
                exportBtn.tooltip = "Writes a JSON file (appdeployhub-achievements/v1) with one entry per trophy tier, " +
                    "ready to upload in AppDeployHub's Achievements > Import from Unity.";
                btnRow2.Add(exportBtn);

                card.Add(btnRow2);

                _listContainer.Add(card);
            }
        }
    }
}
