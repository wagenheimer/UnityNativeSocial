using UnityEditor;

using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>Replaces Unity's raw "Entries" list for an <see cref="AchievementTierMap"/> with the card-based achievement editor.</summary>
    [CustomEditor(typeof(AchievementTierMap))]
    internal sealed class AchievementTierMapInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => new AchievementMapEditorView((AchievementTierMap)target).Root;
    }
}
