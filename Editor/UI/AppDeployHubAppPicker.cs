using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>
    /// Searchable, multi-select list of the AppDeployHub apps, grouped per game. A game usually has one record per
    /// store (Android + iOS); each row says which one it is and which store's achievements it receives, so the two
    /// "Storm Tale 2" entries are no longer indistinguishable.
    /// </summary>
    internal sealed class AppDeployHubAppPicker
    {
        private readonly AppDeployHubClient.AppSummary[] _apps;
        private readonly HashSet<string> _selected;
        private readonly Action<IReadOnlyCollection<string>> _onChanged;
        private readonly VisualElement _list = new VisualElement();
        private string _filter = string.Empty;

        public AppDeployHubAppPicker(AppDeployHubClient.AppSummary[] apps, IEnumerable<string> selectedIds, Action<IReadOnlyCollection<string>> onChanged)
        {
            _apps = apps ?? Array.Empty<AppDeployHubClient.AppSummary>();
            _selected = new HashSet<string>(selectedIds ?? Array.Empty<string>());
            _onChanged = onChanged;
        }

        // ── Pure logic (unit-tested) ─────────────────────────────────────────────

        /// <summary>The store an app record receives achievements for.</summary>
        internal static string StoreLabel(AppDeployHubClient.AppSummary app) => (app.platform ?? string.Empty) switch
        {
            "Android" => "Google Play",
            "iOS" or "MacOS" => "Apple Game Center",
            _ => "Google Play + Apple Game Center"
        };

        internal static string PlatformLabel(AppDeployHubClient.AppSummary app) => (app.platform ?? string.Empty) switch
        {
            "Android" => "Android",
            "iOS" => "iOS",
            "MacOS" => "macOS",
            "Universal" => "Universal",
            "CrossPlatform" => "Cross-platform",
            _ => "App"
        };

        /// <summary>The identifier that tells two records apart: the Android package name, else the bundle id.</summary>
        internal static string Identifier(AppDeployHubClient.AppSummary app) =>
            !string.IsNullOrEmpty(app.packageName) ? app.packageName : app.bundleId ?? string.Empty;

        /// <summary>Records of the same game: same studio and the same link key, or (when unlinked) the same name.</summary>
        internal static string GroupId(AppDeployHubClient.AppSummary app) =>
            $"{app.studioId}|{(!string.IsNullOrWhiteSpace(app.groupKey) ? app.groupKey : (app.name ?? string.Empty).Trim().ToLowerInvariant())}";

        internal static List<List<AppDeployHubClient.AppSummary>> Group(IEnumerable<AppDeployHubClient.AppSummary> apps) =>
            apps.GroupBy(GroupId)
                .Select(g => g.OrderBy(a => a.platform, StringComparer.Ordinal).ToList())
                .OrderBy(g => g[0].studioName, StringComparer.OrdinalIgnoreCase).ThenBy(g => g[0].name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Every record of the same game as any of <paramref name="ids"/> (so picking one also picks its other store).</summary>
        internal static HashSet<string> WithSiblings(AppDeployHubClient.AppSummary[] apps, IEnumerable<string> ids)
        {
            var groups = new HashSet<string>(apps.Where(a => ids.Contains(a.id)).Select(GroupId));
            return new HashSet<string>(apps.Where(a => groups.Contains(GroupId(a))).Select(a => a.id));
        }

        internal static bool Matches(AppDeployHubClient.AppSummary app, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            var haystack = $"{app.name} {app.studioName} {Identifier(app)} {PlatformLabel(app)}";
            return filter.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(word => haystack.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── UI ───────────────────────────────────────────────────────────────────

        public VisualElement Build()
        {
            var root = new VisualElement { style = { marginTop = 6 } };

            var search = new TextField("Search") { tooltip = "Filter by game, studio, package / bundle id or platform." };
            search.RegisterValueChangedCallback(e => { _filter = e.newValue; RebuildList(); });
            root.Add(search);

            _list.style.flexShrink = 0;
            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { maxHeight = 300, marginTop = 4, flexShrink = 0 } };
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.Add(_list);
            root.Add(scroll);

            RebuildList();
            return root;
        }

        private void RebuildList()
        {
            _list.Clear();
            var visible = _apps.Where(a => Matches(a, _filter)).ToList();
            if (visible.Count == 0)
            {
                _list.Add(new Label("No app matches.") { style = { color = NativeSocialUIStyle.ColorTextMuted, marginTop = 4 } });
                return;
            }

            foreach (var group in Group(visible))
                _list.Add(BuildGroup(group));
        }

        private VisualElement BuildGroup(List<AppDeployHubClient.AppSummary> group)
        {
            var box = new VisualElement { style = { backgroundColor = NativeSocialUIStyle.ColorBgDark, marginBottom = 6, flexShrink = 0 } };
            box.SetRadius(6);
            box.SetPadding(8, 6);
            box.SetBorder(1, NativeSocialUIStyle.ColorCardBorder);

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
            header.Add(new Label($"{group[0].name}") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1, flexShrink = 1, color = NativeSocialUIStyle.ColorText } });
            header.Add(new Label(group[0].studioName) { style = { color = NativeSocialUIStyle.ColorTextMuted, marginRight = 6 } });
            if (group.Count > 1)
            {
                var all = group.All(a => _selected.Contains(a.id));
                header.Add(NativeSocialUIStyle.CreateButton(all ? "Clear" : "Select all", () =>
                {
                    foreach (var a in group) { if (all) _selected.Remove(a.id); else _selected.Add(a.id); }
                    Changed();
                    RebuildList();
                }));
            }
            box.Add(header);

            foreach (var app in group) box.Add(BuildRow(app));
            return box;
        }

        private VisualElement BuildRow(AppDeployHubClient.AppSummary app)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 3, flexShrink = 0 } };

            var toggle = new Toggle { value = _selected.Contains(app.id) };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) _selected.Add(app.id); else _selected.Remove(app.id);
                Changed();
            });
            row.Add(toggle);

            var badge = new Label(PlatformLabel(app))
            {
                style = { backgroundColor = BadgeColor(app), color = Color.white, unityFontStyleAndWeight = FontStyle.Bold, minWidth = 78, unityTextAlign = TextAnchor.MiddleCenter, marginRight = 8 }
            };
            badge.SetRadius(8);
            badge.SetPadding(6, 1);
            row.Add(badge);

            var text = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
            var id = Identifier(app);
            text.Add(new Label(string.IsNullOrEmpty(id) ? "(no package / bundle id)" : id) { style = { color = NativeSocialUIStyle.ColorText } });
            text.Add(new Label($"receives {StoreLabel(app)} achievements") { style = { color = NativeSocialUIStyle.ColorTextMuted, fontSize = 10 } });
            row.Add(text);
            return row;
        }

        private static Color BadgeColor(AppDeployHubClient.AppSummary app) => app.platform switch
        {
            "Android" => new Color(0.18f, 0.55f, 0.34f),
            "iOS" or "MacOS" => new Color(0.30f, 0.38f, 0.75f),
            _ => new Color(0.45f, 0.45f, 0.50f)
        };

        private void Changed() => _onChanged?.Invoke(_selected.ToArray());
    }
}
