using System.Linq;

using UnityEditor;
using UnityEditor.UIElements;

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
                "Edit every trophy tier: texts (from I2 Localization), Steam / Google Play / Apple IDs, points and visibility.");

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
        private AchievementTierMap _selectedMap;

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

            // One map is the common case: show its editor directly. With several, pick which one to edit.
            if (_selectedMap == null || !maps.Contains(_selectedMap)) _selectedMap = maps[0];

            if (maps.Count > 1)
            {
                var popup = new PopupField<AchievementTierMap>("Map", maps, maps.IndexOf(_selectedMap),
                    m => AssetDatabase.GetAssetPath(m), m => AssetDatabase.GetAssetPath(m));
                popup.RegisterValueChangedCallback(e => { _selectedMap = e.newValue; Rebuild(); });
                popup.style.marginBottom = 8;
                _listContainer.Add(popup);
            }

            _listContainer.Add(new AchievementMapEditorView(_selectedMap).Root);
        }
    }
}
