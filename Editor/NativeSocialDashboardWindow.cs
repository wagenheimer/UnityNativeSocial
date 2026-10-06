using System;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

using Wagenheimer.NativeSocial.Editor.UI;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Unified dashboard for Native Social: setup audit with one-click fixes, achievement-mapping overview,
    /// a live Play Mode tester, a persistent release checklist, and docs/updates. Built with UI Toolkit like
    /// the sibling packages (RewiredHelper, IAPHelper).
    /// </summary>
    public class NativeSocialDashboardWindow : EditorWindow
    {
        public enum Tab
        {
            SetupAudit,
            Achievements,
            LiveTester,
            Checklist,
            DocsAndUpdates
        }

        private Tab _currentTab = Tab.SetupAudit;
        private VisualElement _root;
        private ScrollView _contentContainer;

        [MenuItem("Tools/Wagenheimer/Native Social/Dashboard...", priority = 0)]
        public static void OpenDashboard() => Open(Tab.SetupAudit);

        public static void ShowWindow() => Open(Tab.SetupAudit);

        public static void Open() => Open(Tab.SetupAudit);

        public static void OpenAuditTab() => Open(Tab.SetupAudit);

        public static void Open(Tab tab)
        {
            var window = GetWindow<NativeSocialDashboardWindow>("Native Social");
            window.minSize = new Vector2(760, 520);
            window.titleContent = new GUIContent("Native Social", FindIcon());
            window._currentTab = tab;
            EnsureWindowOnScreen(window);
            window.Show();
            window.Focus();
            if (window._root != null) window.RebuildUI();
        }

        private static void EnsureWindowOnScreen(EditorWindow window)
        {
            Rect host;
            try
            {
                host = EditorGUIUtility.GetMainWindowPosition();
            }
            catch
            {
                return;
            }

            if (host.width < 1f || host.height < 1f)
                return;

            var rect = window.position;

            var degenerate = float.IsNaN(rect.x) || float.IsNaN(rect.y) ||
                             float.IsInfinity(rect.x) || float.IsInfinity(rect.y) ||
                             rect.width < 50f || rect.height < 50f;

            const float margin = 40f;
            var overlaps = rect.xMax > host.x + margin &&
                           rect.yMax > host.y + margin &&
                           rect.x < host.xMax - margin &&
                           rect.y < host.yMax - margin;

            if (!degenerate && overlaps)
                return;

            const float defaultWidth = 840f;
            const float defaultHeight = 600f;
            var width = degenerate ? defaultWidth : rect.width;
            var height = degenerate ? defaultHeight : rect.height;

            width = Mathf.Clamp(width, 760f, Mathf.Max(760f, host.width - 40f));
            height = Mathf.Clamp(height, 520f, Mathf.Max(520f, host.height - 40f));

            window.position = new Rect(
                Mathf.Round(host.x + (host.width - width) * 0.5f),
                Mathf.Round(host.y + (host.height - height) * 0.5f),
                Mathf.Round(width),
                Mathf.Round(height));
        }

        public void CreateGUI()
        {
            _root = rootVisualElement;
            _root.style.flexGrow = 1;
            NativeSocialUIStyle.Apply(_root);
            RebuildUI();
        }

        private void RebuildUI()
        {
            _root.Clear();
            _root.Add(CreateHeaderBanner());
            _root.Add(CreateTabBar());

            _contentContainer = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            _root.Add(_contentContainer);

            RebuildContent();
        }

        private VisualElement CreateHeaderBanner()
        {
            var banner = new VisualElement();
            banner.AddToClassList("ns-header");

            var row = new VisualElement();
            row.AddToClassList("ns-header-row");

            var left = new VisualElement();
            left.AddToClassList("ns-header-left");
            left.Add(new Label("🌐") { style = { fontSize = 20, marginRight = 8 } });

            var title = new Label("Native Social");
            title.AddToClassList("ns-header-title");
            left.Add(title);

            var versionBadge = new Label("v" + GetPackageVersion());
            versionBadge.AddToClassList("ns-header-version");
            left.Add(versionBadge);
            row.Add(left);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("ns-toolbar-actions");
            toolbar.Add(NativeSocialUIStyle.CreateButton("🔄 Updates", () => UpdateChecker.CheckForUpdate(force: true)));
            row.Add(toolbar);
            banner.Add(row);

            var subtitle = new Label("Unified Android/iOS/Steam achievements, leaderboards and auth on top of native platform SDKs.");
            subtitle.AddToClassList("ns-header-subtitle");
            banner.Add(subtitle);

            return banner;
        }

        private VisualElement CreateTabBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("ns-tab-row");

            (Tab tab, string icon, string title)[] tabs =
            {
                (Tab.SetupAudit, "🔍", "Setup Audit"),
                (Tab.Achievements, "🏆", "Achievements"),
                (Tab.LiveTester, "🧪", "Live Tester"),
                (Tab.Checklist, "📋", "Checklist"),
                (Tab.DocsAndUpdates, "📚", "Docs & Updates")
            };

            foreach (var (tab, icon, title) in tabs)
            {
                var tabValue = tab;
                var button = new Button(() =>
                {
                    _currentTab = tabValue;
                    RebuildUI();
                });

                NativeSocialUIStyle.ApplyIconText(button, $"{icon} {title}");

                button.AddToClassList("ns-tab-btn");
                if (_currentTab == tab) button.AddToClassList("ns-tab-btn-active");
                bar.Add(button);
            }

            return bar;
        }

        private void RebuildContent()
        {
            _contentContainer.Clear();

            VisualElement view = _currentTab switch
            {
                Tab.SetupAudit => new NativeSocialAuditView().Root,
                Tab.Achievements => new NativeSocialAchievementsView().Root,
                Tab.LiveTester => new NativeSocialHelperView().Root,
                Tab.Checklist => new NativeSocialChecklistView().Root,
                _ => new NativeSocialDocsView().Root
            };
            _contentContainer.Add(view);
        }

        internal static string GetPackageVersion()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NativeSocialDashboardWindow).Assembly);
                if (package != null && !string.IsNullOrEmpty(package.version))
                    return package.version;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeSocial] Could not read package version: {ex.Message}");
            }
            return "?";
        }

        /// <summary>A built-in icon that exists on this Unity version; FindTexture returns null instead of logging when one is missing.</summary>
        private static Texture FindIcon()
        {
            foreach (var name in new[] { "d_BuildSettings.Standalone.Small", "BuildSettings.Standalone.Small", "d_UnityEditor.ConsoleWindow" })
            {
                var icon = EditorGUIUtility.FindTexture(name);
                if (icon != null) return icon;
            }
            return null;
        }
    }
}
