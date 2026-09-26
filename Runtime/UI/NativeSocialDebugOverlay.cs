using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.UI
{
    /// <summary>
    /// In-game runtime UI Toolkit debug overlay for inspecting and testing NativeSocial.
    /// Provides live authentication state, achievement progress triggers, leaderboard testing,
    /// and platform UI callers in Unity Editor and Development Builds.
    /// </summary>
    [AddComponentMenu("Wagenheimer/Native Social/Native Social Debug Overlay")]
    [DisallowMultipleComponent]
    public class NativeSocialDebugOverlay : MonoBehaviour
    {
        #region Settings

        [Header("Runtime Access")]
        [Tooltip("Hot key to toggle debug panel visibility in game.")]
        public KeyCode toggleKey = KeyCode.F8;

        [Tooltip("Whether to draw a small floating 'SOCIAL DBG' button on screen.")]
        public bool showFloatingButton = true;

        [Tooltip("Allow overlay to run even in non-development / release builds. Strongly recommended FALSE for production.")]
        public bool enableInReleaseBuilds = false;

        [Tooltip("Optional custom PanelSettings. If null, a high-priority runtime PanelSettings is created automatically.")]
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
        private VisualElement _window;
        private ScrollView _scrollView;

        private Label _statusBanner;
        private Label _statusSubtext;
        private Label _platformLabel;
        private Label _authStatusLabel;
        private Label _steamStatusLabel;

        // Test form inputs
        private TextField _locIdInput;
        private IntegerField _deltaInput;
        private IntegerField _currentInput;
        private IntegerField _totalInput;
        private Toggle _completedToggle;

        private TextField _lbIdInput;
        private LongField _scoreInput;

        private VisualElement _eventLogContainer;
        private readonly List<string> _eventHistory = new List<string>();
        private const int MaxHistoryCount = 12;

        private bool _isOpen;
        private float _lastRefreshTime;
        private const float RefreshInterval = 0.5f;

        // Drag state
        private bool _isDragging;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartWindowPos;

        // Floating button drag state
        private bool _isFloatingDragging;
        private Vector2 _floatingDragStartPointer;
        private Vector2 _floatingDragStartPos;
        private bool _hasDraggedFloating;

        #endregion

        #region Unity Lifecycle

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

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                SetOpen(!_isOpen);
            }

            if (_isOpen && Time.unscaledTime - _lastRefreshTime >= RefreshInterval)
            {
                _lastRefreshTime = Time.unscaledTime;
                RefreshData();
            }
        }

        #endregion

        #region UI Toolkit Initialization

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

            SetOpen(false);
            RefreshData();
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

            var loaded = Resources.Load<PanelSettings>("Wagenheimer/DebugPanelSettings");
            if (loaded != null)
            {
                _uiDocument.panelSettings = loaded;
                return;
            }

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "NativeSocialDebugPanelSettings";
            ps.sortingOrder = 9996;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = BaseReferenceResolution;
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            _uiDocument.panelSettings = ps;
        }

        #endregion

        #region Floating Button

        private void BuildFloatingButton()
        {
            if (!showFloatingButton) return;

            _floatingBtn = new VisualElement();
            var st = _floatingBtn.style;
            st.position = Position.Absolute;
            st.right = 16;
            st.top = 150;
            st.backgroundColor = new Color(0.04f, 0.32f, 0.38f, 0.92f);
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor = new Color(0.00f, 0.75f, 0.85f, 0.85f);
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1.5f;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 20;
            st.paddingLeft = st.paddingRight = 14;
            st.paddingTop = st.paddingBottom = 8;
            st.flexDirection = FlexDirection.Row;
            st.alignItems = Align.Center;

            var iconLbl = new Label("🎮");
            iconLbl.style.fontSize = 13;
            iconLbl.style.marginRight = 6;
            _floatingBtn.Add(iconLbl);

            var textLbl = new Label("SOCIAL DBG");
            textLbl.style.fontSize = 11;
            textLbl.style.color = Color.white;
            textLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            _floatingBtn.Add(textLbl);

            _floatingBtn.RegisterCallback<PointerDownEvent>(evt =>
            {
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
                var delta = (Vector2)evt.position - _floatingDragStartPointer;
                if (delta.sqrMagnitude > 16) _hasDraggedFloating = true;

                st.left = Mathf.Max(0, _floatingDragStartPos.x + delta.x);
                st.top = Mathf.Max(0, _floatingDragStartPos.y + delta.y);
                st.right = StyleKeyword.Auto;
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

        #region Window Construction

        private void BuildWindow()
        {
            _window = new VisualElement();
            var st = _window.style;
            st.position = Position.Absolute;
            st.right = 20;
            st.top = 40;
            st.width = 460;
            st.maxHeight = new StyleLength(new Length(86, LengthUnit.Percent));
            st.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0.97f);
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor = new Color(0.12f, 0.28f, 0.38f);
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1.5f;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 10;
            st.overflow = Overflow.Hidden;

            BuildHeaderBar();

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.paddingLeft = _scrollView.style.paddingRight = 14;
            _scrollView.style.paddingTop = _scrollView.style.paddingBottom = 12;
            _window.Add(_scrollView);

            BuildStatusSection();
            BuildAuthSection();
            BuildAchievementSection();
            BuildLeaderboardSection();
            BuildLogSection();

            _root.Add(_window);
        }

        private void BuildHeaderBar()
        {
            var header = new VisualElement();
            var hst = header.style;
            hst.flexDirection = FlexDirection.Row;
            hst.alignItems = Align.Center;
            hst.justifyContent = Justify.SpaceBetween;
            hst.backgroundColor = new Color(0.07f, 0.15f, 0.22f);
            hst.borderBottomColor = new Color(0.12f, 0.28f, 0.38f);
            hst.borderBottomWidth = 1;
            hst.paddingLeft = hst.paddingRight = 12;
            hst.paddingTop = hst.paddingBottom = 8;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var titleLbl = new Label("🎮 Native Social Debug");
            titleLbl.style.fontSize = 13;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = new Color(0.00f, 0.85f, 0.95f);
            titleRow.Add(titleLbl);
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

            var maxBtn = CreateMiniButton("⛶", ToggleMaximize);
            var closeBtn = CreateMiniButton("✕", () => SetOpen(false));

            ctrlRow.Add(zoomOutBtn);
            ctrlRow.Add(_zoomLabel);
            ctrlRow.Add(zoomInBtn);
            ctrlRow.Add(maxBtn);
            ctrlRow.Add(closeBtn);
            header.Add(ctrlRow);

            // Drag window header
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_isMaximized) return;
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

        private void BuildStatusSection()
        {
            var card = CreateCard("Environment & Diagnostics");
            _statusBanner = new Label("CHECKING...");
            _statusBanner.style.fontSize = 13;
            _statusBanner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusBanner.style.color = Color.yellow;
            card.Add(_statusBanner);

            _statusSubtext = new Label("Evaluating platform integration...");
            _statusSubtext.style.fontSize = 10;
            _statusSubtext.style.color = new Color(0.6f, 0.75f, 0.85f);
            _statusSubtext.style.marginTop = 2;
            _statusSubtext.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_statusSubtext);

            _platformLabel = CreateRow(card, "Platform Target:", Application.platform.ToString());
            _authStatusLabel = CreateRow(card, "Auth State:", "-");
            _steamStatusLabel = CreateRow(card, "Steam API:", "-");

            _scrollView.Add(card);
        }

        private void BuildAuthSection()
        {
            var card = CreateCard("Authentication Tests");

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom = 6;

            var authBtn = CreateActionButton("Authenticate", () =>
            {
                LogEvent("Calling NativeSocial.Authenticate()...");
                NativeSocial.Authenticate(success =>
                {
                    LogEvent($"Authenticate result: {success}");
                    RefreshData();
                });
            });
            btnRow.Add(authBtn);

            var authManualBtn = CreateActionButton("Auth Manual (GPGS)", () =>
            {
                LogEvent("Calling NativeSocial.AuthenticateManually()...");
                NativeSocial.AuthenticateManually(success =>
                {
                    LogEvent($"AuthenticateManually result: {success}");
                    RefreshData();
                });
            });
            btnRow.Add(authManualBtn);
            card.Add(btnRow);

            var authCodeBtn = CreateActionButton("Get Server Auth Code", () =>
            {
                LogEvent("Calling NativeSocial.GetServerAuthCode()...");
                NativeSocial.GetServerAuthCode(code =>
                {
                    LogEvent($"Server Auth Code: {(string.IsNullOrEmpty(code) ? "null/empty" : code.Substring(0, Mathf.Min(8, code.Length)) + "...")}");
                });
            });
            card.Add(authCodeBtn);

            _scrollView.Add(card);
        }

        private void BuildAchievementSection()
        {
            var card = CreateCard("Achievement Testing");

            _locIdInput = new TextField("LocID Key") { value = "ach_first_win" };
            card.Add(_locIdInput);

            _deltaInput = new IntegerField("Delta (Android/Steam)") { value = 1 };
            card.Add(_deltaInput);

            _currentInput = new IntegerField("Current Progress (iOS)") { value = 1 };
            card.Add(_currentInput);

            _totalInput = new IntegerField("Total Required (iOS)") { value = 10 };
            card.Add(_totalInput);

            _completedToggle = new Toggle("Complete Outright") { value = false };
            card.Add(_completedToggle);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 6;

            var reportBtn = CreateActionButton("Report Progress", () =>
            {
                var loc = _locIdInput.value;
                var delta = _deltaInput.value;
                var cur = _currentInput.value;
                var tot = _totalInput.value;
                var comp = _completedToggle.value;

                LogEvent($"Report('{loc}', delta={delta}, {cur}/{tot}, comp={comp})");
                NativeSocial.Report(loc, delta, cur, tot, comp);
            });
            btnRow.Add(reportBtn);

            var uiBtn = CreateActionButton("Open Ach UI", () =>
            {
                var shown = NativeSocial.ShowAchievementsUI();
                LogEvent($"ShowAchievementsUI() -> {shown}");
            });
            btnRow.Add(uiBtn);
            card.Add(btnRow);

            _scrollView.Add(card);
        }

        private void BuildLeaderboardSection()
        {
            var card = CreateCard("Leaderboard Testing");

            _lbIdInput = new TextField("Leaderboard LocID") { value = "lb_high_score" };
            card.Add(_lbIdInput);

            _scoreInput = new LongField("Score Value") { value = 1000 };
            card.Add(_scoreInput);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 6;

            var submitBtn = CreateActionButton("Submit Score", () =>
            {
                var loc = _lbIdInput.value;
                var sc = _scoreInput.value;
                LogEvent($"SubmitScore('{loc}', {sc})");
                NativeSocial.SubmitScore(loc, sc);
            });
            btnRow.Add(submitBtn);

            var showLbBtn = CreateActionButton("Open Leaderboard UI", () =>
            {
                var loc = _lbIdInput.value;
                var shown = NativeSocial.ShowLeaderboardUI(loc);
                LogEvent($"ShowLeaderboardUI('{loc}') -> {shown}");
            });
            btnRow.Add(showLbBtn);
            card.Add(btnRow);

            _scrollView.Add(card);
        }

        private void BuildLogSection()
        {
            var card = CreateCard("Live Event Log");
            _eventLogContainer = new VisualElement();
            _eventLogContainer.style.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
            _eventLogContainer.style.paddingLeft = _eventLogContainer.style.paddingRight = 8;
            _eventLogContainer.style.paddingTop = _eventLogContainer.style.paddingBottom = 6;
            _eventLogContainer.style.borderTopLeftRadius = _eventLogContainer.style.borderTopRightRadius =
                _eventLogContainer.style.borderBottomLeftRadius = _eventLogContainer.style.borderBottomRightRadius = 4;
            card.Add(_eventLogContainer);
            _scrollView.Add(card);
        }

        #endregion

        #region Data Refresh & Event Handlers

        private void RefreshData()
        {
            if (_window == null || !_isOpen) return;

            _platformLabel.text = Application.platform.ToString();

#if UNITY_ANDROID
            bool isAuth = NativeSocial.IsAuthenticated;
            _authStatusLabel.text = isAuth ? "Authenticated" : "Not Authenticated";
            _authStatusLabel.style.color = isAuth ? new Color(0.3f, 0.85f, 0.45f) : new Color(1.0f, 0.7f, 0.2f);
            _statusBanner.text = isAuth ? "GPGS SIGNED IN" : "GPGS NOT SIGNED IN";
            _statusBanner.style.color = isAuth ? new Color(0.3f, 0.85f, 0.45f) : new Color(1.0f, 0.7f, 0.2f);
            _statusSubtext.text = isAuth ? "Google Play Games is ready for achievements & leaderboards." : "Run Authenticate() to connect player account.";
#elif UNITY_IOS
            _authStatusLabel.text = "Game Center Available";
            _authStatusLabel.style.color = new Color(0.3f, 0.85f, 0.45f);
            _statusBanner.text = "IOS GAME CENTER";
            _statusBanner.style.color = new Color(0.3f, 0.85f, 0.45f);
            _statusSubtext.text = "Native iOS Game Center platform integration active.";
#elif WAGENHEIMER_NATIVESOCIAL_STEAM
            bool steam = NativeSocial.SteamReady;
            _steamStatusLabel.text = steam ? "Ready" : "Waiting SteamAPI.Init";
            _steamStatusLabel.style.color = steam ? new Color(0.3f, 0.85f, 0.45f) : new Color(0.95f, 0.35f, 0.35f);
            _statusBanner.text = steam ? "STEAMWORKS READY" : "STEAM NOT READY";
            _statusBanner.style.color = steam ? new Color(0.3f, 0.85f, 0.45f) : new Color(1.0f, 0.7f, 0.2f);
            _statusSubtext.text = steam ? "Steam stat and achievement dispatch enabled." : "Ensure Steam is running and SteamAPI is initialized.";
#else
            _authStatusLabel.text = "Editor / Standalone Simulator";
            _authStatusLabel.style.color = Color.gray;
            _statusBanner.text = "NATIVE SOCIAL ACTIVE";
            _statusBanner.style.color = new Color(0.00f, 0.85f, 0.95f);
            _statusSubtext.text = "Simulated calls dispatched without errors.";
#endif
        }

        private void LogEvent(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _eventHistory.Insert(0, line);
            if (_eventHistory.Count > MaxHistoryCount) _eventHistory.RemoveAt(_eventHistory.Count - 1);

            if (_eventLogContainer != null)
            {
                _eventLogContainer.Clear();
                foreach (var ev in _eventHistory)
                {
                    var lbl = new Label(ev);
                    lbl.style.fontSize = 10;
                    lbl.style.color = new Color(0.7f, 0.85f, 0.95f);
                    lbl.style.marginBottom = 2;
                    _eventLogContainer.Add(lbl);
                }
            }
        }

        #endregion

        #region Helpers

        public void SetOpen(bool open)
        {
            _isOpen = open;
            if (_window != null)
            {
                _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (open)
            {
                RefreshData();
            }
        }

        private VisualElement CreateCard(string title)
        {
            var card = new VisualElement();
            var st = card.style;
            st.backgroundColor = new Color(0.08f, 0.11f, 0.17f);
            st.borderLeftColor = st.borderRightColor = st.borderTopColor = st.borderBottomColor = new Color(0.12f, 0.22f, 0.30f);
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 1;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 6;
            st.paddingLeft = st.paddingRight = 10;
            st.paddingTop = st.paddingBottom = 8;
            st.marginBottom = 10;

            var titleLbl = new Label(title);
            titleLbl.style.fontSize = 11;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = new Color(0.20f, 0.80f, 0.90f);
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
            l.style.color = new Color(0.6f, 0.7f, 0.8f);
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
            st.backgroundColor = new Color(0.08f, 0.30f, 0.40f);
            st.color = Color.white;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 0;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 4;
            st.paddingLeft = st.paddingRight = 8;
            st.paddingTop = st.paddingBottom = 6;
            st.marginLeft = st.marginRight = 3;
            return btn;
        }

        private Button CreateMiniButton(string text, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            var st = btn.style;
            st.fontSize = 10;
            st.backgroundColor = new Color(0.12f, 0.22f, 0.32f);
            st.color = Color.white;
            st.borderLeftWidth = st.borderRightWidth = st.borderTopWidth = st.borderBottomWidth = 0;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 3;
            st.paddingLeft = st.paddingRight = 6;
            st.paddingTop = st.paddingBottom = 3;
            st.marginLeft = 3;
            return btn;
        }

        #endregion
    }
}
