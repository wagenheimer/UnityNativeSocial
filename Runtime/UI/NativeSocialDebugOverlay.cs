using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.UI
{
    /// <summary>
    /// In-game runtime UI Toolkit debug overlay for inspecting and testing NativeSocial.
    /// Provides live authentication state, visual achievement cards with progress simulation,
    /// leaderboard triggers, manual command dispatch, and a live event stream in
    /// Unity Editor and Development Builds.
    /// </summary>
    [AddComponentMenu("Tools/Wagenheimer/Native Social/Native Social Debug Overlay")]
    [DisallowMultipleComponent]
    public class NativeSocialDebugOverlay : MonoBehaviour
    {
        #region Settings

        [Header("Runtime Access")]
        [Tooltip("Hot key to toggle debug panel visibility in game.")]
        public KeyCode toggleKey = KeyCode.F7;

        [Tooltip("Whether to draw a small floating 'SOCIAL DBG' button on screen.")]
        public bool showFloatingButton = true;

        [Tooltip("Allow overlay to run even in non-development / release builds. Strongly recommended FALSE for production.")]
        public bool enableInReleaseBuilds = false;

        [Tooltip("Optional custom PanelSettings. If null, uses Wagenheimer/NativeSocialDebugPanelSettings or high-priority runtime fallback.")]
        public PanelSettings customPanelSettings;

        [Header("Scale (mobile-friendly)")]
        [Tooltip("Initial UI zoom on touch platforms. Adjustable in-game with the A-/A+ header buttons (saved per device).")]
        [Range(1f, 3f)]
        public float mobileDefaultScale = 1.75f;

        [Tooltip("Initial UI zoom on desktop/Editor.")]
        [Range(0.75f, 3f)]
        public float desktopDefaultScale = 1f;

        #endregion

        #region Private Fields

        private UIDocument _uiDocument;
        private VisualElement _root;

        private const float ZoomMin = 0.75f;
        private const float ZoomMax = 3f;
        private const float ZoomStep = 0.25f;
        private const string ZoomPrefsKey = "NativeSocialDebugOverlay.Zoom";
        private static readonly Vector2Int BaseReferenceResolution = new Vector2Int(1920, 1080);
        private float _zoom = 1f;
        private bool _isMaximized;
        private Label _zoomLabel;
        private StyleLength _restoreLeft, _restoreRight, _restoreTop, _restoreWidth, _restoreHeight, _restoreMaxHeight;

        private VisualElement _floatingBtn;
        private VisualElement _floatingDot;
        private VisualElement _window;
        private ScrollView _scrollView;

        // Header and diagnostics
        private Label _statusBanner;
        private Label _statusSubtext;
        private Label _platformLabel;
        private Label _authStatusLabel;
        private Label _userLabel;
        private Label _mapsCountLabel;

        // Filter and Search for Achievements
        private string _searchFilter = "";
        private string _platformFilter = "All"; // All, Android, iOS, Steam
        private string _statusFilter = "All";   // All, InProgress, Completed
        private VisualElement _achievementsContainer;
        private Label _achCountBadge;

        // Custom tester fields
        private TextField _locIdInput;
        private IntegerField _deltaInput;
        private IntegerField _currentInput;
        private IntegerField _totalInput;
        private Toggle _completedToggle;

        private TextField _lbIdInput;
        private LongField _scoreInput;

        // In-Game Achievement Unlock Toast
        private VisualElement _toast;
        private Label _toastTitle;
        private Coroutine _toastCoroutine;

        // Log container
        private VisualElement _eventLogContainer;
        private readonly List<LogItem> _eventHistory = new List<LogItem>();
        private const int MaxHistoryCount = 60;
        private string _logFilter = "All"; // All, Reports, Auth, Errors

        // Progress simulation memory
        private readonly Dictionary<string, SimProgress> _simProgressMap = new Dictionary<string, SimProgress>();

        private bool _isOpen;
        private float _lastRefreshTime;
        private const float RefreshInterval = 0.5f;

        // Window drag state
        private bool _isDragging;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartWindowPos;

        // Floating button drag state
        private bool _isFloatingDragging;
        private Vector2 _floatingDragStartPointer;
        private Vector2 _floatingDragStartPos;
        private bool _hasDraggedFloating;

        private struct LogItem
        {
            public string Time;
            public string Message;
            public LogType Type;
        }

        private class SimProgress
        {
            public int Current;
            public int Total = 100;
            public bool Completed;
        }

        #endregion

        #region Palette

        private static readonly Color ColorAccentCyan   = new Color(0.00f, 0.85f, 0.95f);
        private static readonly Color ColorAccentGreen  = new Color(0.18f, 0.85f, 0.45f);
        private static readonly Color ColorAccentAmber  = new Color(0.98f, 0.72f, 0.20f);
        private static readonly Color ColorAccentRed    = new Color(0.95f, 0.32f, 0.30f);
        private static readonly Color ColorAccentPurple = new Color(0.70f, 0.45f, 0.98f);
        private static readonly Color ColorTextMuted    = new Color(0.65f, 0.75f, 0.85f);
        private static readonly Color ColorCardBg       = new Color(0.07f, 0.10f, 0.15f, 0.95f);
        private static readonly Color ColorCardBorder   = new Color(0.12f, 0.22f, 0.32f);

        #endregion

        #region Auto-Initialization & Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (!Debug.isDebugBuild && !Application.isEditor)
                return;

            EnsureOverlay();
        }

        /// <summary>
        /// Creates or returns the single runtime instance of NativeSocialDebugOverlay.
        /// </summary>
        public static NativeSocialDebugOverlay EnsureOverlay()
        {
            var existing = FindFirstObjectByType<NativeSocialDebugOverlay>();
            if (existing != null) return existing;

            var go = new GameObject("[NativeSocialDebugOverlay]");
            DontDestroyOnLoad(go);
            return go.AddComponent<NativeSocialDebugOverlay>();
        }

        private void Awake()
        {
            if (!Debug.isDebugBuild && !Application.isEditor && !enableInReleaseBuilds)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            InitializeUI();
        }

        private void OnEnable()
        {
            NativeSocial.OnReport += HandleOnReport;
            NativeSocial.OnSubmitScore += HandleOnSubmitScore;
            NativeSocial.OnAuthenticated += HandleOnAuthenticated;
NativeSocial.OnAuthenticated += AutoSyncAfterAuth;
            NativeSocial.OnLog += HandleOnLog;
        }

        private void OnDisable()
        {
            NativeSocial.OnReport -= HandleOnReport;
            NativeSocial.OnSubmitScore -= HandleOnSubmitScore;
            NativeSocial.OnAuthenticated -= HandleOnAuthenticated;
            NativeSocial.OnLog -= HandleOnLog;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                SetOpen(!_isOpen);
            }

            if (_isOpen && Time.unscaledTime - _lastRefreshTime >= RefreshInterval)
            {
                _lastRefreshTime = Time.unscaledTime;
                RefreshDiagnostics();
            }
        }

        #endregion

        #region Event Callbacks

        private void HandleOnReport(string locId, int delta, int current, int total, bool completed)
        {
            UpdateSimProgress(locId, delta, current, total, completed);
            AddLog($"[REPORT] {locId} (+{delta}, {current}/{total}, comp={completed})", completed ? LogType.Log : LogType.Log);
            if (completed)
            {
                string title = ResolveLocIdTitle(locId);
                ShowUnlockToast(title);
            }
            if (_isOpen) RefreshAchievementsList();
        }

        private string ResolveLocIdTitle(string locId)
        {
            if (string.IsNullOrEmpty(locId)) return "Achievement";
            var map = FindMapAsset();
            if (map != null && map.Entries != null)
            {
                foreach (var entry in map.Entries)
                {
                    if (AchievementTierMap.LocId(entry.TrophyNumber, entry.Tier) == locId)
                        return ResolveAchievementTitle(entry);
                }
            }
            return locId;
        }

        /// <summary>Best-effort lookup of the project's AchievementTierMap (Resources first, then any loaded asset).</summary>
        private static AchievementTierMap FindMapAsset()
        {
            var map = Resources.Load<AchievementTierMap>("Social/AchievementTierMap");
            if (map != null) return map;

            map = Resources.Load<AchievementTierMap>("AchievementTierMap");
            if (map != null) return map;

            var maps = Resources.FindObjectsOfTypeAll<AchievementTierMap>();
            return maps != null && maps.Length > 0 ? maps[0] : null;
        }

        private void HandleOnSubmitScore(string locId, long score)
        {
            AddLog($"[SCORE] {locId} => {score}", LogType.Log);
        }

        private void HandleOnAuthenticated(bool success)
        {
            AddLog($"[AUTH] Result: {(success ? "SUCCESS" : "FAILED")}", success ? LogType.Log : LogType.Warning);
            RefreshDiagnostics();
        }

        private void HandleOnLog(string msg)
        {
            AddLog(msg, LogType.Log);
        }

        private void AutoSyncAfterAuth(bool success)
        {
            if (!success) return;
            var completedKeys = _simProgressMap
                .Where(p => p.Value.Completed)
                .Select(p => p.Key)
                .ToList();
            if (completedKeys.Count > 0)
            {
                NativeSocial.SyncCompleted(completedKeys);
                AddLog($"[AUTO-SYNC] Sent {completedKeys.Count} completed achievements after auth.", LogType.Log);
            }
            else
            {
                AddLog("[AUTO-SYNC] No completed achievements to sync.", LogType.Log);
            }
        }

        private void UpdateSimProgress(string locId, int delta, int current, int total, bool completed)
        {
            if (string.IsNullOrEmpty(locId)) return;
            if (!_simProgressMap.TryGetValue(locId, out var sim))
            {
                sim = new SimProgress();
                _simProgressMap[locId] = sim;
            }

            if (total > 0) sim.Total = total;
            if (completed)
            {
                sim.Completed = true;
                sim.Current = sim.Total;
            }
            else
            {
                if (current > 0) sim.Current = current;
                else if (delta > 0) sim.Current += delta;

                if (sim.Current >= sim.Total) sim.Completed = true;
            }
        }

        #endregion

        #region UI Toolkit Setup

        private void InitializeUI()
        {
            _uiDocument = gameObject.GetComponent<UIDocument>();
            if (_uiDocument == null)
            {
                _uiDocument = gameObject.AddComponent<UIDocument>();
            }

            EnsurePanelSettings();

            _uiDocument.panelSettings = Instantiate(_uiDocument.panelSettings);
            _zoom = LoadZoom();
            ApplyZoom();

            _root = _uiDocument.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;

            BuildFloatingButton();
            BuildWindow();
            BuildToast();

            SetOpen(false);
            RefreshDiagnostics();
            RefreshAchievementsList();
        }

        private void BuildToast()
        {
            _toast = new VisualElement();
            _toast.name = "NativeSocialUnlockToast";
            _toast.pickingMode = PickingMode.Ignore;
            var st = _toast.style;
            st.position = Position.Absolute;
            st.top = 22;
            st.alignSelf = Align.Center;
            st.flexDirection = FlexDirection.Row;
            st.alignItems = Align.Center;
            st.backgroundColor = new Color(0.05f, 0.12f, 0.08f, 0.96f);
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor = ColorAccentGreen;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1.5f;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 8;
            st.paddingLeft = 14;
            st.paddingRight = 14;
            st.paddingTop = 8;
            st.paddingBottom = 8;
            st.display = DisplayStyle.None;

            var badge = new Label("ACHIEVEMENT UNLOCKED");
            badge.style.fontSize = 10;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = ColorAccentGreen;
            badge.style.marginRight = 8;
            _toast.Add(badge);

            _toastTitle = new Label("");
            _toastTitle.style.fontSize = 11;
            _toastTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toastTitle.style.color = Color.white;
            _toast.Add(_toastTitle);

            _root.Add(_toast);
        }

        public void ShowUnlockToast(string title)
        {
            if (_toast == null) return;
            if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
            _toastTitle.text = title;
            _toast.style.display = DisplayStyle.Flex;
            _toastCoroutine = StartCoroutine(HideToastRoutine());
        }

        private IEnumerator HideToastRoutine()
        {
            yield return new WaitForSecondsRealtime(4.0f);
            if (_toast != null) _toast.style.display = DisplayStyle.None;
            _toastCoroutine = null;
        }

        private float LoadZoom()
        {
            var fallback = Application.isMobilePlatform ? mobileDefaultScale : desktopDefaultScale;
            return Mathf.Clamp(PlayerPrefs.GetFloat(ZoomPrefsKey, fallback), ZoomMin, ZoomMax);
        }

        private void SetZoom(float value)
        {
            _zoom = Mathf.Clamp(Mathf.Round(value / ZoomStep) * ZoomStep, ZoomMin, ZoomMax);
            PlayerPrefs.SetFloat(ZoomPrefsKey, _zoom);
            PlayerPrefs.Save();
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            _uiDocument.panelSettings.referenceResolution = new Vector2Int(
                Mathf.RoundToInt(BaseReferenceResolution.x / _zoom),
                Mathf.RoundToInt(BaseReferenceResolution.y / _zoom));

            if (_zoomLabel != null) _zoomLabel.text = $"{_zoom:0.##}x";
        }

        private void ToggleMaximize()
        {
            _isMaximized = !_isMaximized;
            var st = _window.style;

            if (_isMaximized)
            {
                _restoreLeft = st.left; _restoreRight = st.right; _restoreTop = st.top;
                _restoreWidth = st.width; _restoreHeight = st.height; _restoreMaxHeight = st.maxHeight;

                st.left = 0; st.right = 0; st.top = 0;
                st.width = new StyleLength(new Length(100, LengthUnit.Percent));
                st.height = new StyleLength(new Length(100, LengthUnit.Percent));
                st.maxHeight = new StyleLength(new Length(100, LengthUnit.Percent));
                return;
            }

            st.left = _restoreLeft; st.right = _restoreRight; st.top = _restoreTop;
            st.width = _restoreWidth; st.height = _restoreHeight; st.maxHeight = _restoreMaxHeight;
        }

        private void EnsurePanelSettings()
        {
            if (_uiDocument.panelSettings != null) return;

            if (customPanelSettings != null)
            {
                _uiDocument.panelSettings = customPanelSettings;
                return;
            }

            var loaded = Resources.Load<PanelSettings>("Wagenheimer/DebugPanelSettings")
                         ?? Resources.Load<PanelSettings>("Wagenheimer/NativeSocialDebugPanelSettings");
            if (loaded != null)
            {
                _uiDocument.panelSettings = loaded;
                return;
            }

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "NativeSocialDebugPanelSettings";
            ps.sortingOrder = 9997;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = BaseReferenceResolution;
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;

            var themes = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>();
            if (themes != null && themes.Length > 0)
            {
                ps.themeStyleSheet = themes[0];
            }

            _uiDocument.panelSettings = ps;
        }

        #endregion

        #region Floating Button

        private void BuildFloatingButton()
        {
            if (!showFloatingButton) return;

            _floatingBtn = new VisualElement();
            _floatingBtn.name = "NativeSocialDebugFloatingButton";
            _floatingBtn.pickingMode = PickingMode.Position;
            var st = _floatingBtn.style;
            st.position = Position.Absolute;
            st.right = 18;
            st.bottom = 60;
            st.height = 34;
            st.backgroundColor = new Color(0.06f, 0.12f, 0.18f, 0.94f);
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor = new Color(0.00f, 0.75f, 0.85f, 0.85f);
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1.2f;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 17;
            st.paddingLeft = st.paddingRight = 12;
            st.flexDirection = FlexDirection.Row;
            st.alignItems = Align.Center;
            st.justifyContent = Justify.Center;

            _floatingDot = new VisualElement();
            _floatingDot.style.width = 8;
            _floatingDot.style.height = 8;
            _floatingDot.style.borderTopLeftRadius = _floatingDot.style.borderTopRightRadius =
                _floatingDot.style.borderBottomLeftRadius = _floatingDot.style.borderBottomRightRadius = 4;
            _floatingDot.style.backgroundColor = ColorAccentAmber;
            _floatingDot.style.marginRight = 6;
            _floatingBtn.Add(_floatingDot);

            var label = new Label("SOCIAL DBG");
            label.style.fontSize = 11.5f;
            label.style.color = Color.white;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _floatingBtn.Add(label);

            _floatingBtn.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || _isMaximized) return;
                _isFloatingDragging = true;
                _hasDraggedFloating = false;
                _floatingDragStartPointer = evt.position;
                _floatingDragStartPos = new Vector2(_floatingBtn.resolvedStyle.left, _floatingBtn.resolvedStyle.top);
                _floatingBtn.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            _floatingBtn.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isFloatingDragging) return;
                Vector2 delta = (Vector2)evt.position - _floatingDragStartPointer;
                if (delta.sqrMagnitude > 16f) _hasDraggedFloating = true;

                if (_hasDraggedFloating)
                {
                    _floatingBtn.style.bottom = StyleKeyword.Auto;
                    _floatingBtn.style.right = StyleKeyword.Auto;
                    _floatingBtn.style.left = Mathf.Max(0, _floatingDragStartPos.x + delta.x);
                    _floatingBtn.style.top = Mathf.Max(0, _floatingDragStartPos.y + delta.y);
                }
                evt.StopPropagation();
            });

            _floatingBtn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isFloatingDragging) return;
                _isFloatingDragging = false;
                _floatingBtn.ReleasePointer(evt.pointerId);
                evt.StopPropagation();

                if (!_hasDraggedFloating)
                {
                    SetOpen(!_isOpen);
                }
            });

            _root.Add(_floatingBtn);
        }

        #endregion

        #region Main Window Construction

        private void BuildWindow()
        {
            _window = new VisualElement();
            _window.name = "NativeSocialDebugWindow";
            _window.pickingMode = PickingMode.Position;
            var st = _window.style;
            st.position = Position.Absolute;
            st.right = 20;
            st.top = 36;
            st.width = 520;
            st.maxWidth = new StyleLength(new Length(96, LengthUnit.Percent));
            st.maxHeight = new StyleLength(new Length(88, LengthUnit.Percent));
            st.backgroundColor = new Color(0.05f, 0.08f, 0.12f, 0.98f);
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor = new Color(0.12f, 0.28f, 0.38f);
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1.2f;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 10;
            st.overflow = Overflow.Hidden;

            BuildHeaderBar();

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.paddingLeft = _scrollView.style.paddingRight = 12;
            _scrollView.style.paddingTop = 10;
            _scrollView.style.paddingBottom = 14;
            _window.Add(_scrollView);

            _scrollView.Add(BuildStatusSection());
            _scrollView.Add(BuildQuickActionsSection());
            _scrollView.Add(BuildAchievementsSection());
            _scrollView.Add(BuildManualDispatcherSection());
            _scrollView.Add(BuildLogSection());

            _root.Add(_window);
        }

        private void BuildHeaderBar()
        {
            var header = new VisualElement();
            var hst = header.style;
            hst.flexDirection = FlexDirection.Row;
            hst.alignItems = Align.Center;
            hst.justifyContent = Justify.SpaceBetween;
            hst.height = 38;
            hst.backgroundColor = new Color(0.08f, 0.14f, 0.22f);
            hst.borderBottomColor = new Color(0.14f, 0.25f, 0.35f);
            hst.borderBottomWidth = 1;
            hst.paddingLeft = 12;
            hst.paddingRight = 8;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var titleLbl = new Label("Native Social Debug");
            titleLbl.style.fontSize = 13;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = ColorAccentCyan;
            titleRow.Add(titleLbl);

            var badge = new Label($"[{Application.platform}]");
            badge.style.fontSize = 9.5f;
            badge.style.color = ColorTextMuted;
            badge.style.marginLeft = 6;
            badge.style.backgroundColor = new Color(0.04f, 0.08f, 0.12f);
            badge.style.paddingLeft = badge.style.paddingRight = 5;
            badge.style.paddingTop = badge.style.paddingBottom = 2;
            badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius =
                badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 3;
            titleRow.Add(badge);

            header.Add(titleRow);

            var ctrlRow = new VisualElement();
            ctrlRow.style.flexDirection = FlexDirection.Row;
            ctrlRow.style.alignItems = Align.Center;

            var zoomOutBtn = CreateMiniButton("A-", () => SetZoom(_zoom - ZoomStep));
            _zoomLabel = new Label($"{_zoom:0.##}x");
            _zoomLabel.style.fontSize = 10;
            _zoomLabel.style.color = Color.white;
            _zoomLabel.style.marginLeft = _zoomLabel.style.marginRight = 3;
            var zoomInBtn = CreateMiniButton("A+", () => SetZoom(_zoom + ZoomStep));

            var maxBtn = CreateMiniButton("[ ]", ToggleMaximize);
            var closeBtn = CreateMiniButton("X", () => SetOpen(false));

            ctrlRow.Add(zoomOutBtn);
            ctrlRow.Add(_zoomLabel);
            ctrlRow.Add(zoomInBtn);
            ctrlRow.Add(maxBtn);
            ctrlRow.Add(closeBtn);
            header.Add(ctrlRow);

            // Drag handling
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || _isMaximized) return;
                _isDragging = true;
                _dragStartPointer = evt.position;
                _dragStartWindowPos = new Vector2(_window.resolvedStyle.left, _window.resolvedStyle.top);
                header.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isDragging || _isMaximized) return;
                var delta = (Vector2)evt.position - _dragStartPointer;
                _window.style.left = Mathf.Max(0, _dragStartWindowPos.x + delta.x);
                _window.style.top = Mathf.Max(0, _dragStartWindowPos.y + delta.y);
                _window.style.right = StyleKeyword.Auto;
                evt.StopPropagation();
            });

            header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isDragging) return;
                _isDragging = false;
                header.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            _window.Add(header);
        }

        #endregion

        #region Card 1: Status & Diagnostics

        private VisualElement BuildStatusSection()
        {
            var card = CreateCard("Environment & Diagnostics");

            var bannerBox = new VisualElement();
            bannerBox.style.paddingTop = bannerBox.style.paddingBottom = 6;
            bannerBox.style.paddingLeft = bannerBox.style.paddingRight = 8;
            bannerBox.style.backgroundColor = new Color(0.04f, 0.07f, 0.11f);
            bannerBox.style.borderTopLeftRadius = bannerBox.style.borderTopRightRadius =
                bannerBox.style.borderBottomLeftRadius = bannerBox.style.borderBottomRightRadius = 5;
            bannerBox.style.marginBottom = 8;

            _statusBanner = new Label("CHECKING...");
            _statusBanner.style.fontSize = 12.5f;
            _statusBanner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusBanner.style.color = ColorAccentAmber;
            bannerBox.Add(_statusBanner);

            _statusSubtext = new Label("Evaluating social platform integration...");
            _statusSubtext.style.fontSize = 10;
            _statusSubtext.style.color = ColorTextMuted;
            _statusSubtext.style.marginTop = 2;
            _statusSubtext.style.whiteSpace = WhiteSpace.Normal;
            bannerBox.Add(_statusSubtext);
            card.Add(bannerBox);

            _platformLabel = CreateRow(card, "Platform Target:", Application.platform.ToString());
            _authStatusLabel = CreateRow(card, "Auth State:", "-");
            _userLabel = CreateRow(card, "Player Info:", "-");
            _mapsCountLabel = CreateRow(card, "Registered Maps:", "-");

            return card;
        }

        #endregion

        #region Card 2: Quick Actions

        private VisualElement BuildQuickActionsSection()
        {
            var card = CreateCard("Quick Platform Actions");

            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.marginBottom = 6;

            var authBtn = CreateActionButton("Authenticate", () =>
            {
                AddLog("Calling NativeSocial.Authenticate()...", LogType.Log);
                NativeSocial.Authenticate(success =>
                {
                    AddLog($"Authenticate result: {success}", success ? LogType.Log : LogType.Warning);
                    RefreshDiagnostics();
                });
            });
            row1.Add(authBtn);

            var manualAuthBtn = CreateActionButton("Manual Auth (GPGS)", () =>
            {
                AddLog("Calling NativeSocial.AuthenticateManually()...", LogType.Log);
                NativeSocial.AuthenticateManually(success =>
                {
                    AddLog($"AuthenticateManually result: {success}", success ? LogType.Log : LogType.Warning);
                    RefreshDiagnostics();
                });
            });
            row1.Add(manualAuthBtn);
            card.Add(row1);

            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;

            var showAchBtn = CreateActionButton("Show Achievements UI", () =>
            {
                var shown = NativeSocial.ShowAchievementsUI();
                AddLog($"ShowAchievementsUI() -> {shown}", shown ? LogType.Log : LogType.Warning);
            });
            row2.Add(showAchBtn);

            var showLbBtn = CreateActionButton("Show Leaderboard UI", () =>
            {
                var shown = NativeSocial.ShowLeaderboardUI();
                AddLog($"ShowLeaderboardUI() -> {shown}", shown ? LogType.Log : LogType.Warning);
            });
            row2.Add(showLbBtn);

            var syncBtn = CreateActionButton("Re-Sync Completed", () =>
            {
                var completedKeys = _simProgressMap.Where(p => p.Value.Completed).Select(p => p.Key).ToList();
                NativeSocial.SyncCompleted(completedKeys);
                AddLog($"SyncCompleted dispatched for {completedKeys.Count} achievements.", LogType.Log);
            });
            row2.Add(syncBtn);

            // New button: Check iOS Achievement IDs
            var checkIosBtn = CreateActionButton("Check iOS IDs", () =>
            {
                AddLog("Starting iOS Achievement ID check...", LogType.Log);
                // Load the AchievementTierMap asset (assumes it's placed under Resources)
                var map = Resources.Load<AchievementTierMap>("AchievementTierMap");
                if (map == null)
                {
                    AddLog("AchievementTierMap not found in Resources. Cannot perform check.", LogType.Error);
                    return;
                }
                int checkedCount = 0;
                foreach (var entry in map.Entries)
                {
                    if (string.IsNullOrEmpty(entry.AppleId)) continue;
                    checkedCount++;
                    // Report 0% progress to trigger logging (total must be >0)
                    NativeSocial.Report(AchievementTierMap.LocId(entry.TrophyNumber, entry.Tier), 0, 0, 100, false);
                }
                AddLog($"iOS ID check completed for {checkedCount} entries. Check overlay logs for failures.", LogType.Log);
            });
            row2.Add(checkIosBtn);

            // New button: Test iOS Report
            var testIosBtn = CreateActionButton("Test iOS Report", () =>
            {
                AddLog("Starting iOS Report test...", LogType.Log);
                var map = Resources.Load<AchievementTierMap>("AchievementTierMap");
                if (map == null)
                {
                    AddLog("AchievementTierMap not found in Resources. Cannot perform test.", LogType.Error);
                    return;
                }
                int tested = 0;
                foreach (var entry in map.Entries)
                {
                    if (string.IsNullOrEmpty(entry.AppleId)) continue;
                    tested++;
                    // Report 0% progress to trigger logging (total must be >0)
                    NativeSocial.Report(AchievementTierMap.LocId(entry.TrophyNumber, entry.Tier), 0, 0, 100, false);
                }
                AddLog($"iOS Report test completed for {tested} entries. Check overlay logs for results.", LogType.Log);
            });
            row2.Add(testIosBtn);

            card.Add(row2);
            return card;
        }

        #endregion

        #region Card 3: Interactive Achievements Explorer

        private VisualElement BuildAchievementsSection()
        {
            var card = CreateCard("Achievements Explorer & Interactive Tester");

            // Header info row
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.SpaceBetween;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 6;

            var subTitle = new Label("Click actions to test live reports & progress");
            subTitle.style.fontSize = 10;
            subTitle.style.color = ColorTextMuted;
            topRow.Add(subTitle);

            _achCountBadge = new Label("0 items");
            _achCountBadge.style.fontSize = 9.5f;
            _achCountBadge.style.color = ColorAccentCyan;
            topRow.Add(_achCountBadge);
            card.Add(topRow);

            // Search bar
            var searchField = new TextField("Search");
            searchField.style.marginBottom = 6;
            searchField.labelElement.style.minWidth = 50;
            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchFilter = evt.newValue ?? "";
                RefreshAchievementsList();
            });
            card.Add(searchField);

            // Filter pills
            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.marginBottom = 8;

            filterRow.Add(CreateFilterPill("All", () => SetPlatformFilter("All"), _platformFilter == "All"));
            filterRow.Add(CreateFilterPill("Android", () => SetPlatformFilter("Android"), _platformFilter == "Android"));
            filterRow.Add(CreateFilterPill("iOS", () => SetPlatformFilter("iOS"), _platformFilter == "iOS"));
            filterRow.Add(CreateFilterPill("Steam", () => SetPlatformFilter("Steam"), _platformFilter == "Steam"));

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            filterRow.Add(spacer);

            filterRow.Add(CreateFilterPill("Done", () => SetStatusFilter("Completed"), _statusFilter == "Completed"));
            filterRow.Add(CreateFilterPill("In Progress", () => SetStatusFilter("InProgress"), _statusFilter == "InProgress"));
            card.Add(filterRow);

            _achievementsContainer = new VisualElement();
            card.Add(_achievementsContainer);

            return card;
        }

        private void SetPlatformFilter(string platform)
        {
            _platformFilter = platform;
            RefreshAchievementsList();
        }

        private void SetStatusFilter(string status)
        {
            _statusFilter = _statusFilter == status ? "All" : status;
            RefreshAchievementsList();
        }

        private void RefreshAchievementsList()
        {
            if (_achievementsContainer == null) return;
            _achievementsContainer.Clear();

            var entries = GatherAllAchievementEntries();
            int displayedCount = 0;

            foreach (var item in entries)
            {
                // Apply Search
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    bool match = item.LocId.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 item.Title.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (!string.IsNullOrEmpty(item.AndroidId) && item.AndroidId.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (!string.IsNullOrEmpty(item.AppleId) && item.AppleId.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (!string.IsNullOrEmpty(item.SteamStat) && item.SteamStat.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) continue;
                }

                // Apply Platform Filter
                if (_platformFilter == "Android" && string.IsNullOrEmpty(item.AndroidId)) continue;
                if (_platformFilter == "iOS" && string.IsNullOrEmpty(item.AppleId)) continue;
                if (_platformFilter == "Steam" && string.IsNullOrEmpty(item.SteamStat)) continue;

                // Progress state
                if (!_simProgressMap.TryGetValue(item.LocId, out var sim))
                {
                    sim = new SimProgress { Total = item.TotalSteps > 0 ? item.TotalSteps : 100 };
                    _simProgressMap[item.LocId] = sim;
                }

                // Status Filter
                if (_statusFilter == "Completed" && !sim.Completed) continue;
                if (_statusFilter == "InProgress" && sim.Completed) continue;

                displayedCount++;
                _achievementsContainer.Add(BuildAchievementCard(item, sim));
            }

            if (_achCountBadge != null)
            {
                _achCountBadge.text = $"{displayedCount} / {entries.Count} items";
            }

            if (displayedCount == 0)
            {
                var emptyLabel = new Label("No matching achievements found.");
                emptyLabel.style.fontSize = 10;
                emptyLabel.style.color = ColorTextMuted;
                emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                emptyLabel.style.paddingTop = emptyLabel.style.paddingBottom = 8;
                _achievementsContainer.Add(emptyLabel);
            }
        }

        private VisualElement BuildAchievementCard(AchievementItemView item, SimProgress sim)
        {
            var card = new VisualElement();
            var st = card.style;
            st.backgroundColor = new Color(0.04f, 0.07f, 0.11f);
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor =
                sim.Completed ? new Color(0.18f, 0.85f, 0.45f, 0.4f) : new Color(0.12f, 0.20f, 0.28f);
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 5;
            st.paddingLeft = st.paddingRight = 8;
            st.paddingTop = st.paddingBottom = 7;
            st.marginBottom = 6;

            // Title & Status Badge
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.justifyContent = Justify.SpaceBetween;
            titleRow.style.alignItems = Align.Center;

            var titleLbl = new Label(item.Title);
            titleLbl.style.fontSize = 11;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = sim.Completed ? ColorAccentGreen : Color.white;
            titleRow.Add(titleLbl);

            var statusBadge = new Label(sim.Completed ? "COMPLETED" : $"{sim.Current}/{sim.Total}");
            statusBadge.style.fontSize = 9.5f;
            statusBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusBadge.style.color = sim.Completed ? ColorAccentGreen : ColorAccentCyan;
            titleRow.Add(statusBadge);
            card.Add(titleRow);

            // Subtitle / IDs
            var metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.marginTop = 2;
            metaRow.style.marginBottom = 4;

            var locIdLbl = new Label($"LocID: {item.LocId}");
            locIdLbl.style.fontSize = 9.5f;
            locIdLbl.style.color = ColorTextMuted;
            locIdLbl.style.marginRight = 8;
            metaRow.Add(locIdLbl);

            if (!string.IsNullOrEmpty(item.AndroidId))
            {
                var tag = CreateIdTag("GPGS", item.AndroidId);
                metaRow.Add(tag);
            }
            if (!string.IsNullOrEmpty(item.AppleId))
            {
                var tag = CreateIdTag("iOS", item.AppleId);
                metaRow.Add(tag);
                // Detect UUID format (contains hyphens) and show warning with swap button
                if (item.AppleId.Contains("-"))
                {
                    var warning = new Label("⚠ UUID");
                    warning.style.fontSize = 9f;
                    warning.style.color = ColorAccentRed;
                    metaRow.Add(warning);
                    var swapBtn = CreateCardButton("Use Identifier", () =>
                    {
                        AddLog($"Swap requested for iOS ID of {item.LocId}.", LogType.Log);
                    });
                    metaRow.Add(swapBtn);
                }
            }
            if (!string.IsNullOrEmpty(item.SteamStat))
            {
                var tag = CreateIdTag("Steam", item.SteamStat);
                metaRow.Add(tag);
            }
            card.Add(metaRow);

            // Progress Bar
            var track = new VisualElement();
            track.style.height = 4;
            track.style.backgroundColor = new Color(0.09f, 0.14f, 0.20f);
            track.style.borderTopLeftRadius = track.style.borderTopRightRadius =
                track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 2;
            track.style.marginBottom = 6;

            var fill = new VisualElement();
            fill.style.height = 4;
            float percent = sim.Total > 0 ? Mathf.Clamp01((float)sim.Current / sim.Total) * 100f : (sim.Completed ? 100f : 0f);
            fill.style.width = new StyleLength(new Length(percent, LengthUnit.Percent));
            fill.style.backgroundColor = sim.Completed ? ColorAccentGreen : ColorAccentCyan;
            track.Add(fill);
            card.Add(track);

            // Quick Actions Row
            var actRow = new VisualElement();
            actRow.style.flexDirection = FlexDirection.Row;

            var add1Btn = CreateCardButton("+1 Step", () =>
            {
                int next = sim.Current + 1;
                bool done = next >= sim.Total;
                NativeSocial.Report(item.LocId, 1, next, sim.Total, done);
            });
            actRow.Add(add1Btn);

            var add5Btn = CreateCardButton("+5 Steps", () =>
            {
                int next = sim.Current + 5;
                bool done = next >= sim.Total;
                NativeSocial.Report(item.LocId, 5, next, sim.Total, done);
            });
            actRow.Add(add5Btn);

            var unlockBtn = CreateCardButton("Unlock", () =>
            {
                NativeSocial.Report(item.LocId, 0, sim.Total, sim.Total, true);
            }, ColorAccentGreen);
            actRow.Add(unlockBtn);

            var resetBtn = CreateCardButton("Reset", () =>
            {
                sim.Current = 0;
                sim.Completed = false;
                AddLog($"[RESET] In-memory progress for {item.LocId} reset to 0.", LogType.Log);
                RefreshAchievementsList();
            });
            actRow.Add(resetBtn);

            card.Add(actRow);
            return card;
        }

        private struct AchievementItemView
        {
            public string LocId;
            public string Title;
            public string AndroidId;
            public string AppleId;
            public string SteamStat;
            public int TotalSteps;
        }

        private List<AchievementItemView> GatherAllAchievementEntries()
        {
            var result = new Dictionary<string, AchievementItemView>();

            // 1. From loaded AchievementTierMap assets in Resources / scene
            var mapAssets = Resources.FindObjectsOfTypeAll<AchievementTierMap>();
            if (mapAssets != null)
            {
                foreach (var map in mapAssets)
                {
                    if (map.Entries == null) continue;
                    foreach (var entry in map.Entries)
                    {
                        var loc = AchievementTierMap.LocId(entry.TrophyNumber, entry.Tier);
                        string title = ResolveAchievementTitle(entry);

                        result[loc] = new AchievementItemView
                        {
                            LocId = loc,
                            Title = title,
                            AndroidId = entry.GooglePlayId,
                            AppleId = entry.AppleId,
                            SteamStat = entry.SteamStat,
                            TotalSteps = entry.StepsToUnlock > 0 ? entry.StepsToUnlock : 100
                        };
                    }
                }
            }

            // 2. From registered NativeSocial Maps (if any were initialized directly without TierMap)
            if (NativeSocial.AndroidMap != null)
            {
                foreach (var kvp in NativeSocial.AndroidMap)
                {
                    if (!result.TryGetValue(kvp.Key, out var item))
                    {
                        item = new AchievementItemView { LocId = kvp.Key, Title = kvp.Key, TotalSteps = 100 };
                    }
                    item.AndroidId = kvp.Value;
                    result[kvp.Key] = item;
                }
            }

            if (NativeSocial.IosMap != null)
            {
                foreach (var kvp in NativeSocial.IosMap)
                {
                    if (!result.TryGetValue(kvp.Key, out var item))
                    {
                        item = new AchievementItemView { LocId = kvp.Key, Title = kvp.Key, TotalSteps = 100 };
                    }
                    item.AppleId = kvp.Value;
                    result[kvp.Key] = item;
                }
            }

            if (NativeSocial.SteamMap != null)
            {
                foreach (var kvp in NativeSocial.SteamMap)
                {
                    if (!result.TryGetValue(kvp.Key, out var item))
                    {
                        item = new AchievementItemView { LocId = kvp.Key, Title = kvp.Key, TotalSteps = 100 };
                    }
                    item.SteamStat = kvp.Value.Stat ?? kvp.Value.Achievement;
                    result[kvp.Key] = item;
                }
            }

            return result.Values.OrderBy(x => x.LocId).ToList();
        }

        private string ResolveAchievementTitle(AchievementTierEntry entry)
        {
            // Attempt I2 Localization translation via reflection
            if (!string.IsNullOrEmpty(entry.NameTerm))
            {
                string trans = TryGetI2Translation(entry.NameTerm);
                if (!string.IsNullOrEmpty(trans))
                {
                    return $"Trophy {entry.TrophyNumber} - Tier {AchievementTierMap.RomanNumeral(entry.Tier)} - {trans}";
                }
            }

            if (!string.IsNullOrEmpty(entry.DisplayName))
            {
                return $"Trophy {entry.TrophyNumber} - Tier {AchievementTierMap.RomanNumeral(entry.Tier)} - {entry.DisplayName}";
            }

            return $"Trophy {entry.TrophyNumber} - Tier {AchievementTierMap.RomanNumeral(entry.Tier)}";
        }

        private static MethodInfo _i2GetTranslationMethod;
        private static bool _i2Resolved;

        private static string TryGetI2Translation(string term)
        {
            if (!_i2Resolved)
            {
                _i2Resolved = true;
                var locType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("I2.Loc.LocalizationManager"))
                    .FirstOrDefault(t => t != null);
                if (locType != null)
                {
                    _i2GetTranslationMethod = locType.GetMethod("GetTranslation", new[] { typeof(string), typeof(bool), typeof(int), typeof(bool), typeof(bool), typeof(GameObject), typeof(string) })
                                              ?? locType.GetMethod("GetTranslation", new[] { typeof(string) });
                }
            }

            if (_i2GetTranslationMethod != null)
            {
                try
                {
                    var pars = _i2GetTranslationMethod.GetParameters();
                    if (pars.Length == 1)
                        return _i2GetTranslationMethod.Invoke(null, new object[] { term }) as string;
                    if (pars.Length == 7)
                        return _i2GetTranslationMethod.Invoke(null, new object[] { term, true, 0, true, false, null, null }) as string;
                }
                catch { }
            }

            return null;
        }

        #endregion

        #region Card 4: Manual Command Dispatcher

        private VisualElement BuildManualDispatcherSection()
        {
            var card = CreateCard("Manual Command Dispatcher");

            var achTitle = new Label("Custom Achievement Report");
            achTitle.style.fontSize = 10.5f;
            achTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            achTitle.style.color = ColorAccentCyan;
            achTitle.style.marginBottom = 4;
            card.Add(achTitle);

            _locIdInput = new TextField("LocID Key") { value = "Trophy1_1" };
            card.Add(_locIdInput);

            var numRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _deltaInput = new IntegerField("Delta (+)") { value = 1 };
            _deltaInput.style.flexGrow = 1;
            _currentInput = new IntegerField("Current") { value = 1 };
            _currentInput.style.flexGrow = 1;
            _totalInput = new IntegerField("Total") { value = 10 };
            _totalInput.style.flexGrow = 1;
            numRow.Add(_deltaInput);
            numRow.Add(_currentInput);
            numRow.Add(_totalInput);
            card.Add(numRow);

            _completedToggle = new Toggle("Complete Outright") { value = false };
            card.Add(_completedToggle);

            var sendReportBtn = CreateActionButton("Dispatch Report()", () =>
            {
                var loc = _locIdInput.value;
                var delta = _deltaInput.value;
                var cur = _currentInput.value;
                var tot = _totalInput.value;
                var comp = _completedToggle.value;

                AddLog($"Manual Report('{loc}', delta={delta}, {cur}/{tot}, comp={comp})", LogType.Log);
                NativeSocial.Report(loc, delta, cur, tot, comp);
            });
            sendReportBtn.style.marginTop = 4;
            sendReportBtn.style.marginBottom = 10;
            card.Add(sendReportBtn);

            // Leaderboard sub-section
            var lbTitle = new Label("Custom Leaderboard Submission");
            lbTitle.style.fontSize = 10.5f;
            lbTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbTitle.style.color = ColorAccentPurple;
            lbTitle.style.marginBottom = 4;
            card.Add(lbTitle);

            _lbIdInput = new TextField("Leaderboard LocID") { value = "lb_high_score" };
            card.Add(_lbIdInput);

            _scoreInput = new LongField("Score Value") { value = 1000 };
            card.Add(_scoreInput);

            var lbBtnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            var submitBtn = CreateActionButton("Submit Score", () =>
            {
                var loc = _lbIdInput.value;
                var sc = _scoreInput.value;
                NativeSocial.SubmitScore(loc, sc);
            });
            lbBtnRow.Add(submitBtn);

            var openLbBtn = CreateActionButton("Open UI", () =>
            {
                var loc = _lbIdInput.value;
                NativeSocial.ShowLeaderboardUI(loc);
            });
            lbBtnRow.Add(openLbBtn);
            card.Add(lbBtnRow);

            return card;
        }

        #endregion

        #region Card 5: Live Event Log

        private VisualElement BuildLogSection()
        {
            var card = CreateCard("Live Event Log & Console");

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.SpaceBetween;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 6;

            var filterRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            filterRow.Add(CreateFilterPill("All", () => SetLogFilter("All"), _logFilter == "All"));
            filterRow.Add(CreateFilterPill("Reports", () => SetLogFilter("Reports"), _logFilter == "Reports"));
            filterRow.Add(CreateFilterPill("Auth", () => SetLogFilter("Auth"), _logFilter == "Auth"));
            filterRow.Add(CreateFilterPill("Errors", () => SetLogFilter("Errors"), _logFilter == "Errors"));
            topRow.Add(filterRow);

            var clearBtn = CreateMiniButton("Clear", () =>
            {
                _eventHistory.Clear();
                RefreshEventLog();
            });
            topRow.Add(clearBtn);
            card.Add(topRow);

            _eventLogContainer = new VisualElement();
            _eventLogContainer.style.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
            _eventLogContainer.style.paddingLeft = _eventLogContainer.style.paddingRight = 8;
            _eventLogContainer.style.paddingTop = _eventLogContainer.style.paddingBottom = 6;
            _eventLogContainer.style.borderTopLeftRadius = _eventLogContainer.style.borderTopRightRadius =
                _eventLogContainer.style.borderBottomLeftRadius = _eventLogContainer.style.borderBottomRightRadius = 5;
            _eventLogContainer.style.minHeight = 80;
            card.Add(_eventLogContainer);

            return card;
        }

        private void SetLogFilter(string filter)
        {
            _logFilter = filter;
            RefreshEventLog();
        }

        private void AddLog(string msg, LogType type)
        {
            var item = new LogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Message = msg,
                Type = type
            };

            _eventHistory.Insert(0, item);
            if (_eventHistory.Count > MaxHistoryCount) _eventHistory.RemoveAt(_eventHistory.Count - 1);

            if (_isOpen) RefreshEventLog();
        }

        private void RefreshEventLog()
        {
            if (_eventLogContainer == null) return;
            _eventLogContainer.Clear();

            var filtered = _eventHistory.Where(item =>
            {
                if (_logFilter == "Reports") return item.Message.IndexOf("Report", StringComparison.OrdinalIgnoreCase) >= 0;
                if (_logFilter == "Auth") return item.Message.IndexOf("Auth", StringComparison.OrdinalIgnoreCase) >= 0;
                if (_logFilter == "Errors") return item.Type == LogType.Error || item.Type == LogType.Warning || item.Message.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || item.Message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
                return true;
            }).Take(20);

            foreach (var item in filtered)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 2;

                var timeLbl = new Label($"[{item.Time}] ");
                timeLbl.style.fontSize = 9.5f;
                timeLbl.style.color = ColorTextMuted;
                row.Add(timeLbl);

                var msgLbl = new Label(item.Message);
                msgLbl.style.fontSize = 9.5f;
                msgLbl.style.whiteSpace = WhiteSpace.Normal;
                msgLbl.style.color = item.Type == LogType.Warning ? ColorAccentAmber :
                                     item.Type == LogType.Error ? ColorAccentRed : Color.white;
                row.Add(msgLbl);

                _eventLogContainer.Add(row);
            }

            if (_eventLogContainer.childCount == 0)
            {
                var emptyLbl = new Label("No logged events.");
                emptyLbl.style.fontSize = 9.5f;
                emptyLbl.style.color = ColorTextMuted;
                _eventLogContainer.Add(emptyLbl);
            }
        }

        #endregion

        #region Data Refresh & Diagnostics

        private void RefreshDiagnostics()
        {
            if (_window == null) return;

            bool isInit = NativeSocial.IsInitialized;
            int androidCount = NativeSocial.AndroidMap != null ? NativeSocial.AndroidMap.Count : 0;
            int iosCount = NativeSocial.IosMap != null ? NativeSocial.IosMap.Count : 0;
            int steamCount = NativeSocial.SteamMap != null ? NativeSocial.SteamMap.Count : 0;

            if (_platformLabel != null)
                _platformLabel.text = $"{Application.platform} (v{Application.version})";

            if (_mapsCountLabel != null)
                _mapsCountLabel.text = $"Android: {androidCount} | iOS: {iosCount} | Steam: {steamCount}";

            string userString = "None";
            if (Social.localUser != null && !string.IsNullOrEmpty(Social.localUser.userName))
            {
                userString = $"{Social.localUser.userName} ({Social.localUser.id})";
            }
            if (_userLabel != null) _userLabel.text = userString;

#if UNITY_ANDROID
            bool isAuth = NativeSocial.IsAuthenticated;
            if (_authStatusLabel != null)
            {
                _authStatusLabel.text = isAuth ? "Authenticated (GPGS)" : "Signed Out";
                _authStatusLabel.style.color = isAuth ? ColorAccentGreen : ColorAccentAmber;
            }
            if (_statusBanner != null)
            {
                _statusBanner.text = isAuth ? "GOOGLE PLAY GAMES CONNECTED" : "GPGS SIGNED OUT";
                _statusBanner.style.color = isAuth ? ColorAccentGreen : ColorAccentAmber;
            }
            if (_statusSubtext != null)
            {
                _statusSubtext.text = isAuth ? "Google Play Games Services signed in and ready." : "Click Authenticate to sign in with Play Games Services.";
            }
            if (_floatingDot != null) _floatingDot.style.backgroundColor = isAuth ? ColorAccentGreen : ColorAccentAmber;

#elif UNITY_IOS
            bool isAuth = Social.localUser.authenticated;
            if (_authStatusLabel != null)
            {
                _authStatusLabel.text = isAuth ? "Authenticated (Game Center)" : "Game Center Available";
                _authStatusLabel.style.color = isAuth ? ColorAccentGreen : ColorAccentAmber;
            }
            if (_statusBanner != null)
            {
                _statusBanner.text = isAuth ? "GAME CENTER CONNECTED" : "GAME CENTER AVAILABLE";
                _statusBanner.style.color = isAuth ? ColorAccentGreen : ColorAccentAmber;
            }
            if (_statusSubtext != null)
            {
                _statusSubtext.text = isAuth ? "Apple Game Center authenticated and active." : "Game Center ready for authentication.";
            }
            if (_floatingDot != null) _floatingDot.style.backgroundColor = isAuth ? ColorAccentGreen : ColorAccentAmber;

#elif WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
            bool steam = NativeSocial.SteamReady;
            if (_authStatusLabel != null)
            {
                _authStatusLabel.text = steam ? "Steamworks Initialized" : "Waiting SteamAPI";
                _authStatusLabel.style.color = steam ? ColorAccentGreen : ColorAccentRed;
            }
            if (_statusBanner != null)
            {
                _statusBanner.text = steam ? "STEAMWORKS READY" : "STEAM NOT INITIALIZED";
                _statusBanner.style.color = steam ? ColorAccentGreen : ColorAccentRed;
            }
            if (_statusSubtext != null)
            {
                _statusSubtext.text = steam ? "Steam stat & achievement dispatch active." : "Check Steam client and SteamAPI.Init().";
            }
            if (_floatingDot != null) _floatingDot.style.backgroundColor = steam ? ColorAccentGreen : ColorAccentRed;

#else
            if (_authStatusLabel != null)
            {
                _authStatusLabel.text = isInit ? "Active (Editor Simulation)" : "Not Initialized";
                _authStatusLabel.style.color = isInit ? ColorAccentCyan : ColorAccentAmber;
            }
            if (_statusBanner != null)
            {
                _statusBanner.text = isInit ? "SIMULATION MODE ACTIVE" : "AWAITING INITIALIZATION";
                _statusBanner.style.color = isInit ? ColorAccentCyan : ColorAccentAmber;
            }
            if (_statusSubtext != null)
            {
                _statusSubtext.text = isInit ? "Dispatches simulated events cleanly without errors." : "Call NativeSocial.Initialize() to register achievement maps.";
            }
            if (_floatingDot != null) _floatingDot.style.backgroundColor = isInit ? ColorAccentCyan : ColorAccentAmber;
#endif
        }

        #endregion

        #region Helpers & Element Factories

        public void SetOpen(bool open)
        {
            _isOpen = open;
            if (_window != null)
            {
                _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (open)
            {
                RefreshDiagnostics();
                RefreshAchievementsList();
                RefreshEventLog();
            }
        }

        private VisualElement CreateCard(string title)
        {
            var card = new VisualElement();
            var st = card.style;
            st.backgroundColor = ColorCardBg;
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor = ColorCardBorder;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 6;
            st.paddingLeft = st.paddingRight = 10;
            st.paddingTop = st.paddingBottom = 8;
            st.marginBottom = 10;

            var titleLbl = new Label(title);
            titleLbl.style.fontSize = 11.5f;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = ColorAccentCyan;
            titleLbl.style.marginBottom = 6;
            card.Add(titleLbl);

            return card;
        }

        private Label CreateRow(VisualElement parent, string label, string defaultValue)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 3;

            var l = new Label(label);
            l.style.fontSize = 10;
            l.style.color = ColorTextMuted;
            row.Add(l);

            var v = new Label(defaultValue);
            v.style.fontSize = 10;
            v.style.color = Color.white;
            v.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(v);

            parent.Add(row);
            return v;
        }

        private Button CreateActionButton(string text, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            var st = btn.style;
            st.flexGrow = 1;
            st.fontSize = 10;
            st.unityFontStyleAndWeight = FontStyle.Bold;
            st.backgroundColor = new Color(0.08f, 0.22f, 0.32f);
            st.color = Color.white;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 0;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 4;
            st.paddingLeft = st.paddingRight = 8;
            st.paddingTop = st.paddingBottom = 6;
            st.marginLeft = st.marginRight = 3;
            return btn;
        }

        private Button CreateCardButton(string text, Action onClick, Color? accent = null)
        {
            var btn = new Button(onClick) { text = text };
            var st = btn.style;
            st.fontSize = 9.5f;
            st.unityFontStyleAndWeight = FontStyle.Bold;
            st.backgroundColor = accent.HasValue ? new Color(accent.Value.r * 0.25f, accent.Value.g * 0.25f, accent.Value.b * 0.25f) : new Color(0.10f, 0.16f, 0.24f);
            st.color = accent ?? Color.white;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 0;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 3;
            st.paddingLeft = st.paddingRight = 6;
            st.paddingTop = st.paddingBottom = 3;
            st.marginRight = 4;
            return btn;
        }

        private Button CreateMiniButton(string text, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            var st = btn.style;
            st.fontSize = 10;
            st.backgroundColor = new Color(0.12f, 0.20f, 0.28f);
            st.color = Color.white;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 0;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 3;
            st.paddingLeft = st.paddingRight = 6;
            st.paddingTop = st.paddingBottom = 3;
            st.marginLeft = 3;
            return btn;
        }

        private VisualElement CreateFilterPill(string label, Action onClick, bool active)
        {
            var btn = new Button(onClick) { text = label };
            var st = btn.style;
            st.fontSize = 9.5f;
            st.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            st.backgroundColor = active ? new Color(0.00f, 0.40f, 0.55f) : new Color(0.08f, 0.12f, 0.18f);
            st.color = active ? Color.white : ColorTextMuted;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 0;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 10;
            st.paddingLeft = st.paddingRight = 8;
            st.paddingTop = st.paddingBottom = 3;
            st.marginRight = 4;
            return btn;
        }

        private VisualElement CreateIdTag(string platform, string id)
        {
            var tag = new Label($"{platform}: {id}");
            tag.style.fontSize = 8.5f;
            tag.style.color = new Color(0.55f, 0.70f, 0.85f);
            tag.style.backgroundColor = new Color(0.08f, 0.14f, 0.20f);
            tag.style.paddingLeft = tag.style.paddingRight = 4;
            tag.style.paddingTop = tag.style.paddingBottom = 1;
            tag.style.borderTopLeftRadius = tag.style.borderTopRightRadius =
                tag.style.borderBottomLeftRadius = tag.style.borderBottomRightRadius = 2;
            tag.style.marginRight = 4;
            return tag;
        }

        #endregion
    }
}
