using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    internal sealed class NativeSocialAuditView
    {
        private const long CopiedFeedbackMs = 1500;
        private const float IconButtonWidth = 30f;
        private const float BadgeWidth = 72f;

        public VisualElement Root { get; }

        private List<AuditResult> _results;
        private AuditSeverity? _severityFilter;
        private string _platformFilter;
        private VisualElement _resultsContainer;
        private Label _summaryLabel;

        public NativeSocialAuditView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);
            GpgsInstaller.OnInstallCompleted += HandleInstallCompleted;
            BuildUI();
            RunAudit();
        }

        private void HandleInstallCompleted()
        {
            if (Root.panel == null)
            {
                GpgsInstaller.OnInstallCompleted -= HandleInstallCompleted;
                return;
            }
            RunAudit();
        }

        private void BuildUI()
        {
            var headerCard = NativeSocialUIStyle.CreateCard("🔍 Project Setup Verification Audit",
                "Automated scan of platform SDKs, startup bootstrap wiring and achievement-ID mapping completeness across Steam, Android and iOS.");

            var actionsRow = Row();
            actionsRow.Add(NativeSocialUIStyle.CreateButton("▶ Run Audit Now", RunAudit, primary: true));
            actionsRow.Add(CreateCopyButton("📋 Copy Report", "Copy the full audit as Markdown.",
                () => _results != null ? NativeSocialAudit.ToMarkdown(_results) : null));
            var promptBtn = CreateCopyButton("🤖 Copy AI Fix Prompt (all)",
                "Copy one ready-to-paste prompt that makes an AI agent fix every warning and failure.",
                () => _results != null ? NativeSocialAudit.ToPromptMarkdown(_results) : null);
            promptBtn.AddToClassList("ns-btn-primary");
            actionsRow.Add(promptBtn);
            headerCard.Add(actionsRow);

            // Platform Filter Row
            var platformRow = Row();
            platformRow.Add(new Label("Platform:") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 6, alignSelf = Align.Center } });
            platformRow.Add(NativeSocialUIStyle.CreateButton("🌐 All Platforms", () => SetPlatformFilter(null)));
            platformRow.Add(NativeSocialUIStyle.CreateButton("⚙️ Core", () => SetPlatformFilter(NativeSocialAudit.CategoryCommon)));
            platformRow.Add(NativeSocialUIStyle.CreateButton("🖥️ Steam", () => SetPlatformFilter(NativeSocialAudit.CategorySteam)));
            platformRow.Add(NativeSocialUIStyle.CreateButton("🤖 Android", () => SetPlatformFilter(NativeSocialAudit.CategoryAndroid)));
            platformRow.Add(NativeSocialUIStyle.CreateButton("🍎 iOS", () => SetPlatformFilter(NativeSocialAudit.CategoryIOS)));
            headerCard.Add(platformRow);

            // Severity Filter Row
            var severityRow = Row();
            severityRow.Add(new Label("Severity:") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 6, alignSelf = Align.Center } });
            severityRow.Add(NativeSocialUIStyle.CreateButton("All Severities", () => SetSeverityFilter(null)));
            severityRow.Add(NativeSocialUIStyle.CreateButton("✕ Fails Only", () => SetSeverityFilter(AuditSeverity.Fail)));
            severityRow.Add(NativeSocialUIStyle.CreateButton("⚠ Warnings Only", () => SetSeverityFilter(AuditSeverity.Warning)));
            headerCard.Add(severityRow);

            _summaryLabel = new Label("Running audit...");
            _summaryLabel.style.fontSize = 11;
            _summaryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _summaryLabel.style.marginTop = 4;
            headerCard.Add(_summaryLabel);

            Root.Add(headerCard);

            var sceneCard = NativeSocialUIStyle.CreateCard("Scene Setup Actions", "Add the bootstrap component or in-game debug overlay directly to the active scene.");
            var sceneRow = Row();
            sceneRow.Add(NativeSocialUIStyle.CreateButton("Add Bootstrap to Scene", NativeSocialAudit.AddBootstrapToCurrentScene));
            sceneRow.Add(NativeSocialUIStyle.CreateButton("Add In-Game Debug Overlay to Scene", NativeSocialDebugOverlayEditor.AddDebugOverlayToScene));
            sceneCard.Add(sceneRow);
            Root.Add(sceneCard);

            _resultsContainer = new VisualElement();
            Root.Add(_resultsContainer);
        }

        public void RunAudit()
        {
            _results = NativeSocialAudit.RunAudit();
            RefreshResults();
        }

        private void SetPlatformFilter(string platform)
        {
            _platformFilter = platform;
            RefreshResults();
        }

        private void SetSeverityFilter(AuditSeverity? filter)
        {
            _severityFilter = filter;
            RefreshResults();
        }

        private void RefreshResults()
        {
            _resultsContainer.Clear();
            if (_results == null || _results.Count == 0)
            {
                _resultsContainer.Add(NativeSocialUIStyle.CreateCallout("No audit results to display."));
                return;
            }

            UpdateSummary();

            var filtered = _results.Where(r =>
                (!_severityFilter.HasValue || r.Severity == _severityFilter.Value) &&
                (string.IsNullOrEmpty(_platformFilter) || r.Category == _platformFilter)
            ).ToList();

            if (filtered.Count == 0)
            {
                _resultsContainer.Add(NativeSocialUIStyle.CreateCallout("No audit results match the selected filter."));
                return;
            }

            foreach (var group in filtered.GroupBy(r => r.Category))
            {
                var card = NativeSocialUIStyle.CreateCard(group.Key);
                foreach (var item in group)
                    card.Add(CreateRow(item));
                _resultsContainer.Add(card);
            }
        }

        private void UpdateSummary()
        {
            int fails = _results.Count(r => r.Severity == AuditSeverity.Fail);
            int warnings = _results.Count(r => r.Severity == AuditSeverity.Warning);
            int passes = _results.Count(r => r.Severity == AuditSeverity.Pass);

            if (fails > 0)
            {
                _summaryLabel.text = $"❌ {fails} critical failure(s) found: resolve before publishing.";
                _summaryLabel.style.color = NativeSocialUIStyle.ColorError;
            }
            else if (warnings > 0)
            {
                _summaryLabel.text = $"⚠️ {warnings} warning(s) found: review recommended.";
                _summaryLabel.style.color = NativeSocialUIStyle.ColorWarning;
            }
            else
            {
                _summaryLabel.text = $"✓ All {passes} automated checks passed cleanly!";
                _summaryLabel.style.color = NativeSocialUIStyle.ColorSuccess;
            }
        }

        private VisualElement CreateRow(AuditResult item)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.FlexStart;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.137f, 0.192f, 0.267f);

            var textCol = new VisualElement { style = { flexGrow = 1, flexShrink = 1, marginRight = 10 } };
            textCol.Add(CreateText(item.Title, 12, FontStyle.Bold, Color.clear));
            if (!string.IsNullOrEmpty(item.WhatIsThis))
                textCol.Add(CreateText("ℹ️ " + item.WhatIsThis, 10, FontStyle.Italic, new Color(0.62f, 0.66f, 0.72f)));
            if (!string.IsNullOrEmpty(item.Detail))
                textCol.Add(CreateText(item.Detail, 10, FontStyle.Normal, NativeSocialUIStyle.ColorTextMuted));
            if (!string.IsNullOrEmpty(item.FixHint))
                textCol.Add(CreateText("💡 " + item.FixHint, 10, FontStyle.Normal, new Color(0.45f, 0.75f, 0.95f)));
            row.Add(textCol);

            row.Add(CreateActions(item));
            return row;
        }

        private VisualElement CreateActions(AuditResult item)
        {
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };

            if (item.Fix != null)
            {
                var fixBtn = NativeSocialUIStyle.CreateButton("🛠 " + item.FixLabel, () => RunFix(item), primary: true);
                actions.Add(fixBtn);
            }

            actions.Add(CreateIconButton("📋", "Copy this finding.", () => FormatFinding(item)));
            if (!string.IsNullOrEmpty(item.Prompt))
                actions.Add(CreateIconButton("🤖", "Copy an AI prompt that fixes this finding.", () => item.Prompt));
            else
                actions.Add(new VisualElement { style = { width = IconButtonWidth, marginLeft = 4 } });

            var badge = NativeSocialUIStyle.CreateBadge(item.Severity.ToString(), item.Severity);
            badge.style.width = BadgeWidth;
            badge.style.marginLeft = 8;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            actions.Add(badge);

            return actions;
        }

        private void RunFix(AuditResult item)
        {
            try { item.Fix(); }
            catch (Exception ex) { Debug.LogError($"[NativeSocial] Fix '{item.FixLabel}' failed: {ex.Message}"); }

            if (!GpgsInstaller.IsInstalling)
                RunAudit();
        }

        private static Label CreateText(string text, int size, FontStyle style, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = style;
            label.style.whiteSpace = WhiteSpace.Normal;
            if (color != Color.clear) label.style.color = color;
            if (size < 12) label.style.marginTop = 2;
            return label;
        }

        private static VisualElement Row() =>
            new() { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 8, flexWrap = Wrap.Wrap } };

        private static string FormatFinding(AuditResult r)
        {
            var text = $"[{r.Severity}] {r.Category} - {r.Title}";
            if (!string.IsNullOrEmpty(r.Detail)) text += "\n" + r.Detail;
            if (!string.IsNullOrEmpty(r.FixHint)) text += "\nHow to fix: " + r.FixHint;
            return text;
        }

        private static Button CreateIconButton(string icon, string tooltip, Func<string> getText)
        {
            var button = CreateCopyButton(icon, tooltip, getText, "✓");
            button.style.width = IconButtonWidth;
            button.style.marginLeft = 4;
            button.style.marginRight = 0;
            button.style.paddingLeft = 0;
            button.style.paddingRight = 0;
            return button;
        }

        private static Button CreateCopyButton(string label, string tooltip, Func<string> getText, string copiedLabel = "✓ Copied")
        {
            var button = new Button { text = label, tooltip = tooltip };
            button.AddToClassList("ns-btn-secondary");
            button.clicked += () =>
            {
                var text = getText();
                button.text = string.IsNullOrEmpty(text) ? "Run the audit first" : copiedLabel;
                if (!string.IsNullOrEmpty(text))
                    GUIUtility.systemCopyBuffer = text;

                button.schedule.Execute(() => button.text = label).ExecuteLater(CopiedFeedbackMs);
            };
            return button;
        }
    }
}
