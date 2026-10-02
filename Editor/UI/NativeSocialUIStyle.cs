using System;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    public static class NativeSocialUIStyle
    {
        public static readonly Color ColorBgDark = new(0.067f, 0.078f, 0.110f);
        public static readonly Color ColorCardDark = new(0.090f, 0.125f, 0.184f);
        public static readonly Color ColorCardBorder = new(0.157f, 0.224f, 0.314f);
        public static readonly Color ColorPrimary = new(0.000f, 0.706f, 0.847f);
        public static readonly Color ColorPrimaryHover = new(0.012f, 0.518f, 0.780f);
        public static readonly Color ColorText = new(0.886f, 0.910f, 0.941f);
        public static readonly Color ColorTextMuted = new(0.580f, 0.639f, 0.722f);
        public static readonly Color ColorCodeBg = new(0.043f, 0.059f, 0.098f);
        public static readonly Color ColorSuccess = new(0.220f, 0.741f, 0.447f);
        public static readonly Color ColorWarning = new(0.961f, 0.620f, 0.043f);
        public static readonly Color ColorError = new(0.937f, 0.267f, 0.267f);

        public static void SetRadius(this VisualElement el, float radius)
        {
            if (el == null) return;
            el.style.borderTopLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderBottomRightRadius = radius;
        }

        public static void SetPadding(this VisualElement el, float horizontal, float vertical)
        {
            if (el == null) return;
            el.style.paddingLeft = horizontal;
            el.style.paddingRight = horizontal;
            el.style.paddingTop = vertical;
            el.style.paddingBottom = vertical;
        }

        public static void SetBorder(this VisualElement el, float width, Color color)
        {
            if (el == null) return;
            el.style.borderLeftWidth = width;
            el.style.borderRightWidth = width;
            el.style.borderTopWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftColor = color;
            el.style.borderRightColor = color;
            el.style.borderTopColor = color;
            el.style.borderBottomColor = color;
        }

        // ── Shared factory helpers (used by every Dashboard tab view) ──────────────

        public static VisualElement CreateCard(string title, string description = null)
        {
            var card = new VisualElement();
            card.AddToClassList("ns-card");

            var header = new VisualElement();
            header.AddToClassList("ns-card-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("ns-card-title");
            header.Add(titleLabel);
            card.Add(header);

            if (!string.IsNullOrEmpty(description))
            {
                var descLabel = new Label(description);
                descLabel.AddToClassList("ns-card-desc");
                card.Add(descLabel);
            }

            return card;
        }

        public static VisualElement CreateCodeCard(string title, string code)
        {
            var card = CreateCard(title);

            var codeBox = new VisualElement();
            codeBox.AddToClassList("ns-code-block");
            codeBox.Add(new Label(code));
            card.Add(codeBox);

            var copyBtn = CreateButton("Copy Snippet", () =>
            {
                EditorGUIUtility.systemCopyBuffer = code;
                Debug.Log($"[NativeSocial] Copied snippet '{title}' to clipboard.");
            });
            copyBtn.style.marginTop = 8;
            copyBtn.style.alignSelf = Align.FlexEnd;
            card.Add(copyBtn);

            return card;
        }

        public static VisualElement CreateInfoRow(string label, string value, Color valueColor)
        {
            var row = new VisualElement();
            row.AddToClassList("ns-row");
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 6;

            var l = new Label(label) { style = { color = ColorTextMuted, fontSize = 12 } };
            var v = new Label(value) { style = { color = valueColor, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold } };
            row.Add(l);
            row.Add(v);
            return row;
        }

        public static VisualElement CreateBulletPoint(string text)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart, marginBottom = 4 } };
            row.Add(new Label("• ") { style = { color = ColorPrimary, fontSize = 12 } });
            row.Add(new Label(text) { style = { color = ColorText, fontSize = 11, whiteSpace = WhiteSpace.Normal, flexGrow = 1 } });
            return row;
        }

        public static Button CreateButton(string text, Action onClick, bool primary = false)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList(primary ? "ns-btn-primary" : "ns-btn-secondary");
            return button;
        }

        public static Button CreateFilterButton(string text, Action onClick, bool active)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("ns-filter-btn");
            if (active) button.AddToClassList("ns-filter-btn-active");
            return button;
        }

        public static VisualElement CreateCallout(string message, AuditSeverity level = AuditSeverity.Info)
        {
            var box = new VisualElement();
            box.AddToClassList("ns-callout");
            box.AddToClassList(level switch
            {
                AuditSeverity.Pass => "ns-callout-pass",
                AuditSeverity.Warning => "ns-callout-warn",
                _ => "ns-callout-info"
            });

            box.Add(new Label(message) { style = { whiteSpace = WhiteSpace.Normal } });
            return box;
        }

        public static Label CreateBadge(string text, AuditSeverity severity)
        {
            var badge = new Label(text);
            badge.AddToClassList("ns-badge");
            badge.AddToClassList(severity switch
            {
                AuditSeverity.Pass => "ns-badge-success",
                AuditSeverity.Warning => "ns-badge-warning",
                AuditSeverity.Fail => "ns-badge-error",
                _ => "ns-badge-neutral"
            });
            return badge;
        }

        public static void Apply(VisualElement element)
        {
            if (element == null) return;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.wagenheimer.nativesocial/Editor/UI/NativeSocialCommon.uss")
                        ?? AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/UI/NativeSocialCommon.uss");
            if (sheet != null) element.styleSheets.Add(sheet);
        }
    }
}
