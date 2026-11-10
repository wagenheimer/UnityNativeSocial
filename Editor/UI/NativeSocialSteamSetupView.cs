using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>
    /// Steamworks setup view in the Native Social Dashboard: guides the developer through creating
    /// Steam stats, configuring the unlock model, and exporting/importing achievements on Steamworks Partner.
    /// </summary>
    internal sealed class NativeSocialSteamSetupView
    {
        public VisualElement Root { get; }

        private AchievementTierMap _selectedMap;
        private VisualElement _contentContainer;
        private Label _modelHelpLabel;

        public NativeSocialSteamSetupView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);

            var maps = NativeSocialAudit.FindAllAchievementTierMaps();
            if (maps.Count > 0) _selectedMap = maps[0];

            BuildUI();
        }

        private void BuildUI()
        {
            Root.Clear();
            Root.Add(CreateHeaderCard());

            _contentContainer = new VisualElement();
            Root.Add(_contentContainer);

            RefreshContent();
        }

        private VisualElement CreateHeaderCard()
        {
            var card = NativeSocialUIStyle.CreateCard("🖥️ Steamworks Achievements & Stats Setup",
                "Everything you need to configure Steam achievements and stats on Steamworks Partner in 3 simple steps.");

            var row = Row();
            row.Add(NativeSocialUIStyle.CreateButton("▶ Verify Setup", () => NativeSocialDashboardWindow.Open(NativeSocialDashboardWindow.Tab.SetupAudit), primary: true));
            row.Add(NativeSocialUIStyle.CreateButton("↗ Open Steamworks", () =>
            {
                var appId = _selectedMap != null && !string.IsNullOrWhiteSpace(_selectedMap.SteamAppId)
                    ? _selectedMap.SteamAppId.Trim()
                    : string.Empty;
                var url = string.IsNullOrEmpty(appId)
                    ? "https://partner.steamgames.com"
                    : $"https://partner.steamgames.com/apps/achievements/{appId}";
                Application.OpenURL(url);
            }));

            if (_selectedMap != null)
            {
                row.Add(NativeSocialUIStyle.CreateButton("🎮 Export Steam Pack", () => SteamPackExporter.ExportWithDialog(_selectedMap)));
            }

            card.Add(row);

            card.Add(NativeSocialUIStyle.CreateCallout(
                "Important: Steamworks has no bulk import for Stats. You must create the stats listed below in Steamworks before bulk-importing achievements.",
                AuditSeverity.Info));

            return card;
        }

        private void RefreshContent()
        {
            _contentContainer.Clear();

            var maps = NativeSocialAudit.FindAllAchievementTierMaps();
            if (maps.Count == 0)
            {
                _contentContainer.Add(NativeSocialUIStyle.CreateCallout(
                    "No AchievementTierMap asset found in the project. Create one in the Achievements tab first.",
                    AuditSeverity.Warning));
                return;
            }

            if (_selectedMap == null || !maps.Contains(_selectedMap)) _selectedMap = maps[0];

            if (maps.Count > 1)
            {
                var mapRow = Row();
                var popup = new PopupField<AchievementTierMap>("Selected Map", maps, maps.IndexOf(_selectedMap),
                    m => AssetDatabase.GetAssetPath(m), m => AssetDatabase.GetAssetPath(m));
                popup.RegisterValueChangedCallback(e =>
                {
                    _selectedMap = e.newValue;
                    RefreshContent();
                });
                popup.style.flexGrow = 1;
                mapRow.Add(popup);
                _contentContainer.Add(mapRow);
            }

            _contentContainer.Add(CreateMapSummaryCard());
            _contentContainer.Add(CreateUnlockModelCard());
            _contentContainer.Add(CreateStatsTableCard());
            _contentContainer.Add(CreateStepByStepGuideCard());
        }

        private VisualElement CreateMapSummaryCard()
        {
            var card = NativeSocialUIStyle.CreateCard($"Map: {_selectedMap.name}",
                $"Asset located at {AssetDatabase.GetAssetPath(_selectedMap)}");

            var entries = _selectedMap.Entries.Where(e => !string.IsNullOrEmpty(e.SteamAchievement)).ToList();
            var stats = SteamPackExporter.BuildStats(entries);

            card.Add(NativeSocialUIStyle.CreateInfoRow("Steam App ID:",
                string.IsNullOrWhiteSpace(_selectedMap.SteamAppId) ? "Not set" : _selectedMap.SteamAppId,
                string.IsNullOrWhiteSpace(_selectedMap.SteamAppId) ? NativeSocialUIStyle.ColorWarning : NativeSocialUIStyle.ColorText));

            card.Add(NativeSocialUIStyle.CreateInfoRow("Steam Achievements:",
                $"{entries.Count} configured",
                NativeSocialUIStyle.ColorSuccess));

            card.Add(NativeSocialUIStyle.CreateInfoRow("Distinct Steam Stats Required:",
                $"{stats.Count} stats",
                NativeSocialUIStyle.ColorPrimary));

            var actions = Row();
            actions.Add(NativeSocialUIStyle.CreateButton("📍 Select Asset in Project", () =>
            {
                Selection.activeObject = _selectedMap;
                EditorGUIUtility.PingObject(_selectedMap);
            }));
            card.Add(actions);

            return card;
        }

        private VisualElement CreateUnlockModelCard()
        {
            var card = NativeSocialUIStyle.CreateCard("⚙️ Steam Unlock Model",
                "Choose how the game unlocks achievements on Steam.");

            var so = new SerializedObject(_selectedMap);
            var modeProp = so.FindProperty("SteamDefaultUnlockMode");
            var absoluteProp = so.FindProperty("SteamSetStatAbsolute");

            var modeField = new EnumField("Unlock Model", _selectedMap.SteamDefaultUnlockMode);
            modeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_selectedMap, "Change Steam Unlock Mode");
                _selectedMap.SteamDefaultUnlockMode = (SteamUnlockMode)evt.newValue;
                EditorUtility.SetDirty(_selectedMap);
                UpdateModelHelp();
            });
            card.Add(modeField);

            var absToggle = new Toggle("Write stat as absolute counter (Recommended)") { value = _selectedMap.SteamSetStatAbsolute };
            absToggle.tooltip = "When enabled, NativeSocial writes the exact cumulative value (current) from your save file to Steam (SetStat(stat, current)). When disabled, it adds delta to Steam's cached stat.";
            absToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_selectedMap, "Change SteamSetStatAbsolute");
                _selectedMap.SteamSetStatAbsolute = evt.newValue;
                EditorUtility.SetDirty(_selectedMap);
                UpdateModelHelp();
            });
            card.Add(absToggle);

            _modelHelpLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8, fontSize = 11, color = NativeSocialUIStyle.ColorTextMuted } };
            card.Add(_modelHelpLabel);
            UpdateModelHelp();

            return card;
        }

        private void UpdateModelHelp()
        {
            if (_modelHelpLabel == null || _selectedMap == null) return;

            var entries = _selectedMap.Entries.Where(e => !string.IsNullOrEmpty(e.SteamAchievement)).ToList();
            var sb = new StringBuilder();

            if (_selectedMap.SteamDefaultUnlockMode == SteamUnlockMode.ExplicitAchievement)
            {
                sb.AppendLine("📌 Unlock Model: Explicit Achievement (Recommended)");
                sb.AppendLine("• The game calls SteamUserStats.SetAchievement(achievement) directly on completion.");
                sb.AppendLine("• The Steam stat is also updated so progress bars work in the Steam Overlay.");
                sb.AppendLine("• Most reliable model — works for all event-based and numerical achievements.");
            }
            else
            {
                sb.AppendLine("⚠️ Unlock Model: Stat Threshold");
                sb.AppendLine("• The game ONLY updates stats and NEVER calls SetAchievement.");
                sb.AppendLine($"• On Steamworks Partner, each of the {entries.Count} achievements MUST have an auto-unlock threshold rule configured.");
            }

            sb.AppendLine();
            if (_selectedMap.SteamSetStatAbsolute)
            {
                sb.AppendLine("📊 Stat Value: Absolute Counter (Enabled - Recommended for Cumulative Saves)");
                sb.AppendLine("• NativeSocial writes the exact cumulative total from your save data: SteamUserStats.SetStat(stat, current).");
                sb.AppendLine("• Idempotent: safe against duplicate calls, offline play, or game restarts.");
                sb.AppendLine("• (Note: originally tagged as 'legacy' from Storm Tale 2, but it is the best practice whenever the game tracks totals in save data).");
            }
            else
            {
                sb.AppendLine("📊 Stat Value: Delta Accumulator (Disabled)");
                sb.AppendLine("• NativeSocial reads Steam's cached cloud stat and adds delta: SetStat(stat, cached + delta).");
                sb.AppendLine("• Use only if your game does not maintain total metrics in its save data.");
            }

            _modelHelpLabel.text = sb.ToString().TrimEnd();
        }

        private VisualElement CreateStatsTableCard()
        {
            var entries = _selectedMap.Entries.Where(e => !string.IsNullOrEmpty(e.SteamAchievement)).ToList();
            var stats = SteamPackExporter.BuildStats(entries);

            var card = NativeSocialUIStyle.CreateCard($"📊 Steam Stats to Create ({stats.Count} stats)",
                "Create each row below in Steamworks > Edit Steamworks Settings > Stats & Achievements > Stats.");

            var btnRow = Row();
            btnRow.Add(NativeSocialUIStyle.CreateButton("📋 Copy Table to Clipboard", () =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("API Name\tType\tSet By\tIncrement Only\tDefault\tMin\tMax Target");
                foreach (var stat in stats)
                {
                    sb.AppendLine($"{stat.Key}\tINT\tClient\tyes\t0\t0\t{stat.Value}");
                }
                EditorGUIUtility.systemCopyBuffer = sb.ToString();
                Debug.Log($"[NativeSocial] Copied {stats.Count} Steam stats to clipboard.");
            }, primary: true));

            btnRow.Add(NativeSocialUIStyle.CreateButton("💾 Copy as CSV", () =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("api_name,type,set_by,increment_only,min_value,default_value,display_name,largest_progress_target");
                foreach (var stat in stats)
                {
                    sb.AppendLine($"\"{stat.Key}\",INT,Client,true,0,0,\"{stat.Key}\",{stat.Value}");
                }
                EditorGUIUtility.systemCopyBuffer = sb.ToString();
                Debug.Log($"[NativeSocial] Copied {stats.Count} Steam stats as CSV to clipboard.");
            }));

            card.Add(btnRow);

            var tableContainer = new VisualElement
            {
                style =
                {
                    marginTop = 10,
                    backgroundColor = NativeSocialUIStyle.ColorBgDark,
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = NativeSocialUIStyle.ColorCardBorder,
                    borderRightColor = NativeSocialUIStyle.ColorCardBorder,
                    borderTopColor = NativeSocialUIStyle.ColorCardBorder,
                    borderBottomColor = NativeSocialUIStyle.ColorCardBorder,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 6,
                    paddingBottom = 6
                }
            };

            // Table Header
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingBottom = 4, borderBottomWidth = 1, borderBottomColor = NativeSocialUIStyle.ColorCardBorder } };
            header.Add(new Label("Stat API Name") { style = { width = 160, color = NativeSocialUIStyle.ColorPrimary, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } });
            header.Add(new Label("Type") { style = { width = 50, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
            header.Add(new Label("Set By") { style = { width = 60, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
            header.Add(new Label("Increment Only") { style = { width = 90, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
            header.Add(new Label("Default") { style = { width = 50, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
            header.Add(new Label("Target (Max)") { style = { flexGrow = 1, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
            tableContainer.Add(header);

            // Table Rows
            foreach (var stat in stats)
            {
                var r = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingTop = 3, paddingBottom = 3 } };
                r.Add(new Label(stat.Key) { style = { width = 160, color = NativeSocialUIStyle.ColorText, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } });
                r.Add(new Label("INT") { style = { width = 50, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
                r.Add(new Label("Client") { style = { width = 60, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
                r.Add(new Label("yes") { style = { width = 90, color = NativeSocialUIStyle.ColorSuccess, fontSize = 11 } });
                r.Add(new Label("0") { style = { width = 50, color = NativeSocialUIStyle.ColorTextMuted, fontSize = 11 } });
                r.Add(new Label($"≥ {stat.Value}") { style = { flexGrow = 1, color = NativeSocialUIStyle.ColorWarning, fontSize = 11 } });
                tableContainer.Add(r);
            }

            card.Add(tableContainer);
            return card;
        }

        private VisualElement CreateStepByStepGuideCard()
        {
            var card = NativeSocialUIStyle.CreateCard("📖 3-Step Steamworks Publishing Guide",
                "Follow these steps to have your achievements live on Steam in minutes.");

            card.Add(NativeSocialUIStyle.CreateBulletPoint("Step 1: Open Steamworks Partner > App > Edit Steamworks Settings > Stats & Achievements > Stats. Create each stat from the table above (Type: INT, Set By: Client, Increment Only: yes)."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint("Step 2: Click '🎮 Export Steam Pack' above to generate the CSV files and icons."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint("Step 3: In Steamworks > Stats & Achievements > Achievements, go to the 'Bulk Import/Export' tab. Configure languages to match, then drag and drop the definitions CSV, localizations CSV, and icons/ folder into the upload box. Click 'Save imported data'."));
            card.Add(NativeSocialUIStyle.CreateBulletPoint("Step 4: Go to the 'Publish' tab in Edit Steamworks Settings and publish the draft changes. Done!"));

            return card;
        }

        private static VisualElement Row() => new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                flexWrap = Wrap.Wrap,
                alignItems = Align.Center,
                marginTop = 6
            }
        };
    }
}
