using StreamGuard.Helpers;
using StreamGuard.Models;
using StreamGuard.Services;
using StreamGuard.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace StreamGuard
{
    public partial class MainWindow : Window
    {
        private enum ProtectionKind
        {
            Wrapper,
            HiddenDesktop,
            HideFallback
        }

        // ── Windows API ───────────────────────────────────────────────────────
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
        const uint RDW_RESTORE_REPAINT = 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400;
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMSBT_MAINWINDOW = 2;
        const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        const uint WDA_NONE               = 0x00000000;
        const int GWL_EXSTYLE             = -20;
        const nint WS_EX_TOOLWINDOW       = 0x00000080;
        const nint WS_EX_APPWINDOW        = 0x00040000;
        const nint WS_EX_NOACTIVATE       = 0x08000000;
        const int WM_MOUSEACTIVATE        = 0x0021;
        const int MA_ACTIVATE             = 1;
        const int MA_NOACTIVATE           = 3;
        const int SW_HIDE                 = 0;
        const int SW_SHOWNOACTIVATE       = 4;
        const int SW_SHOW                 = 5;
        const uint SWP_NOSIZE             = 0x0001;
        const uint SWP_NOMOVE             = 0x0002;
        const uint SWP_NOZORDER           = 0x0004;
        const uint SWP_NOACTIVATE         = 0x0010;
        const uint SWP_FRAMECHANGED       = 0x0020;
        const uint SWP_SHOWWINDOW         = 0x0040;
        const uint SWP_HIDEWINDOW         = 0x0080;
        static readonly IntPtr HWND_TOPMOST   = new(-1);
        static readonly IntPtr HWND_NOTOPMOST = new(-2);

        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        // ── State ─────────────────────────────────────────────────────────────
        private readonly ObservableCollection<GuardedApp> _apps = new();
        private readonly ObservableCollection<WindowInfo> _availableWindows = new();
        private readonly VirtualDesktopService _vdService       = new();
        private readonly HotkeyService         _hotkeys         = new();
        private readonly ConfigService         _config;
        private readonly ThreatBlockerService  _threatBlocker   = new();
        private SystemToolsBlockerService      _systemToolsBlocker = new();
        private AntigravityNotificationsService _antigravityNotifications = new();
        private readonly AntigravityInstallerService _antigravityInstaller = new();
        private readonly AntigravityAuthBridgeService _antigravityAuthBridge = new();
        private readonly AiTrafficProtectionService _aiTrafficProtection = new();
        private readonly SnippingToolService   _snippingTool    = new();
        private readonly IPrivacyCurtain _privacyCurtain;
        private readonly PhysicalOutputGuardService _physicalOutputGuard = new();
        private readonly CursorShieldService   _cursorShield    = new();
        private readonly CursorCaptureProtectionService _cursorCaptureProtection = CursorCaptureProtectionService.Instance;
        private readonly SwitchCooldownService _switchCooldown = new();
        private readonly Dictionary<GuardedApp, int> _appHotkeyIds = new();
        private readonly DispatcherTimer       _selfProtectionTimer = new();
        private readonly DispatcherTimer       _physicalOutputTimer = new();
        private NotifyIcon?                    _trayIcon;
        private bool _isProtecting  = false;
        private volatile bool _cleanupDone = false;
        private bool _isLoadingConfig = true;
        private bool _passiveFocusMode = true;
        private IntPtr _previousForegroundHwnd = IntPtr.Zero;
        private bool _isTitleBarDragging;
        private POINT _titleBarDragStartCursor;
        private RECT _titleBarDragStartWindowRect;
        private bool _exitRequested = false;
        private bool _hotkeysReady = false;
        private bool _suppressProcessSelection = false;
        private bool _isRefreshingAvailableWindows = false;
        private bool _physicalOutputCurtainActive = false;
        private DateTime _lastAvailableWindowsRefresh = DateTime.MinValue;
        private static readonly bool ShowTrayIcon = false;
        private static readonly bool KillEnabledAppsOnExit = false;
        private bool _forceKillOnExit = false;
        private const bool EnableWrapperProtection = true;
        private bool _isAnimatingTransition = false;
        private bool _isCustomMaximized = false;
        private Rect _normalRestoreBounds = new Rect(100, 100, 860, 600);
        private int _snippingHotkeyId = -1;
        private string _snippingHotkey = "Ctrl+Alt+S";
        private static string _currentSnippingHotkey = "Ctrl+Alt+S";
        private bool _isRecordingSnippingHotkey = false;
        private string _recordingOriginalSnippingHotkey = "";
        private bool _isRecordingHotkey = false;
        private GuardedApp? _recordingApp = null;
        private string _recordingOriginalHotkey = "";
        private ToastNotificationService? _toastService;
        public ToastNotificationService? ToastService => _toastService;

        // ── Init ──────────────────────────────────────────────────────────────
        public MainWindow() : this(new ConfigService()) { }

        internal MainWindow(ConfigService config) : this(config, new PrivacyCurtainService()) { }

        internal MainWindow(ConfigService config, IPrivacyCurtain captureCurtain)
            : this(config, captureCurtain, null) { }

        private readonly Func<GuardedApp, System.Threading.Tasks.Task<List<WindowInfo>>> _launchAppWindows;
        private readonly HashSet<GuardedApp> _pendingSecureOpens = new();
        private bool _isStartingProtection;
        private int _protectionRevision;

        internal MainWindow(ConfigService config, IPrivacyCurtain captureCurtain,
            Func<GuardedApp, System.Threading.Tasks.Task<List<WindowInfo>>>? appLauncher)
            : this(config, captureCurtain, appLauncher, new PrivacyCurtainService()) { }

        internal MainWindow(ConfigService config, IPrivacyCurtain captureCurtain,
            Func<GuardedApp, System.Threading.Tasks.Task<List<WindowInfo>>>? appLauncher, IPrivacyCurtain manualCurtain)
            : this(config, captureCurtain, appLauncher, manualCurtain, null) { }

        internal MainWindow(ConfigService config, IPrivacyCurtain captureCurtain,
            Func<GuardedApp, System.Threading.Tasks.Task<List<WindowInfo>>>? appLauncher, IPrivacyCurtain manualCurtain, RecoveryService? recovery)
            : this(config, captureCurtain, appLauncher, manualCurtain, recovery, null) { }

        internal MainWindow(ConfigService config, IPrivacyCurtain captureCurtain,
            Func<GuardedApp, System.Threading.Tasks.Task<List<WindowInfo>>>? appLauncher, IPrivacyCurtain manualCurtain, RecoveryService? recovery, CaptionService? captions)
        {
            _config = config;
            _capturePrivacyCurtain = captureCurtain;
            _privacyCurtain = manualCurtain;
            if (_capturePrivacyCurtain is PrivacyCurtainService capCurtain)
                capCurtain.DismissRequested += DismissAllPrivacyCurtains;
            if (_privacyCurtain is PrivacyCurtainService manCurtain)
                manCurtain.DismissRequested += DismissAllPrivacyCurtains;
            _launchAppWindows = appLauncher ?? LaunchAndWaitForWindowsAsync;
            _recovery = recovery ?? new RecoveryService(new RecoveryJournalStore(config.RecoveryJournalPath), new WindowsRecoveryApi(_vdService));
            Controls.HoverFeedback.Apply(this);
            InitializeComponent();
            if (ToastCardsStack != null)
            {
                _toastService = new ToastNotificationService(ToastCardsStack, ToastQueueFooter, TxtQueueSummary, BtnClearAllToasts);
            }
            var asmVer = typeof(MainWindow).Assembly.GetName().Version;
            if (AppVersionFooterText != null && asmVer != null)
                AppVersionFooterText.Text = $"v{asmVer.Major}.{asmVer.Minor}.{Math.Max(0, asmVer.Build)}";
            ShowInTaskbar = false;
            ShowActivated = false;
            AppListBox.ItemsSource = _apps;
            AvailableWindowsListBox.ItemsSource = _availableWindows;
            _apps.CollectionChanged += (_, _) =>
            {
                UpdateEmptyState();
                SyncAvailableWindowsShieldState();
            };
            LoadConfig();
            InitializeCaptureGuard();
            UpdateEmptyState();
            SetProtectionUiState(false);
            InitializeRecoveryCenter();
            InitializeCaptions(captions);
            _cursorCaptureProtection.AttachInAppCloak(this);
            _cursorCaptureProtection.GlobalCloakStateChanged += OnGlobalCloakStateChanged;
            _cursorCaptureProtection.EmergencyEscapeTriggered += OnEmergencyEscapeTriggered;
            long _lastMainWindowEscapeTimestamp = 0;
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && DangerConfirmModalOverlay?.Visibility == Visibility.Visible)
                {
                    CloseDangerConfirmModal();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape && !_isRecordingHotkey && !_isRecordingSnippingHotkey)
                {
                    if (_cursorCaptureProtection.IsGlobalCloakActive)
                    {
                        long now = Environment.TickCount64;
                        bool isCtrlShift = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift);
                        if (isCtrlShift || (now - _lastMainWindowEscapeTimestamp <= 500))
                        {
                            _lastMainWindowEscapeTimestamp = 0;
                            ToggleGlobalCursorCloak();
                            e.Handled = true;
                        }
                        else
                        {
                            _lastMainWindowEscapeTimestamp = now;
                        }
                    }
                }
            };
            if (_config.LastLoadError != null)
                UpdateStatus(_config.LoadedFromBackup
                    ? "Configuration restored from backup. Review your application list and preferences."
                    : "Configuration could not be read. The original file has been retained; review preferences before applying changes.", false);
            SourceInitialized += (_, _) =>
            {
                ApplyModernDwmAttributes();
                ApplyStealthWindowStyle();
                ProtectSelfFromCapture();
            };
            Loaded += OnLoaded;
            _selfProtectionTimer.Interval = TimeSpan.FromSeconds(1);
            _selfProtectionTimer.Tick += (_, _) =>
            {
                ProtectSelfFromCapture();
                ReconcileProtectedWindows();
                ReassertTopmostIfPassive();
            };
            _physicalOutputTimer.Interval = TimeSpan.FromSeconds(2);
            _physicalOutputTimer.Tick += (_, _) => UpdatePhysicalOutputGuard();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Ẩn StreamGuard khỏi screen capture
            ApplyModernDwmAttributes();
            ApplyStealthWindowStyle();
            ProtectSelfFromCapture();
            _selfProtectionTimer.Start();
            StartCaptureRiskMonitoring();

            // Keep the shield idle at startup. Auto-containment previously opened
            // the privacy curtain during startup and could crash native WPF/DWM.
            _threatBlocker.StatusChanged += OnThreatBlockerStatusChanged;
            _snippingTool.SnippingCompleted += OnSnippingCompleted;
            AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                new System.Windows.Input.MouseButtonEventHandler(OnWindowPreviewMouseLeftButtonDownForSwitchCooldown),
                false);
            AddHandler(UIElement.PreviewKeyDownEvent,
                new System.Windows.Input.KeyEventHandler(OnWindowPreviewKeyDownForSwitchCooldown),
                false);
            LocalizationService.BindText(ThreatStatusText, System.Windows.Controls.TextBlock.TextProperty, "RAIA containment idle");
            LocalizationService.BindText(ThreatDetailText, System.Windows.Controls.TextBlock.TextProperty, "Click Contain RAIA when you want to apply blocks");

            // Hotkeys
            _hotkeys.Initialize(this);
            _hotkeys.Register(HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT, 0x47, ToggleVisibility);              // Ctrl+Shift+G
            _hotkeys.RegisterShiftSgChord(ToggleDriverBoosterFromChord);                                               // Shift+S+G (Toggle: 1st open/restore, 2nd minimize/hide)
            _hotkeys.RegisterCtrlShiftSgChord(KillStreamGuardAndManagedApps);                                          // Ctrl+Shift+S+G
            var addAppHotkey = _hotkeys.Register(HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT | HotkeyService.MOD_ALT, 0x41, AddForegroundWindowToGuardedApps); // Ctrl+Shift+Alt+A
            if (addAppHotkey < 0)
                _hotkeys.RegisterCtrlShiftAltAChord(AddForegroundWindowToGuardedApps);
            _hotkeys.Register(HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT | HotkeyService.MOD_ALT, 0x48, PanicRestoreAll); // Ctrl+Shift+Alt+H
            _hotkeys.Register(HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT, 0x4D, ToggleGlobalCursorCloak); // Ctrl+Shift+M
            _captureCurtainHotkeyReady = _hotkeys.Register(HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT | HotkeyService.MOD_ALT, 0x43, TogglePrivacyCurtain) >= 0; // Ctrl+Shift+Alt+C
            _hotkeys.Register(HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT | HotkeyService.MOD_ALT, 0x58, RequestExit); // Ctrl+Shift+Alt+X
            _hotkeys.VirtualDesktopHotkeyBlocked += OnVirtualDesktopHotkeyBlocked;
            _hotkeys.RegisterCurtainEscapeGuard(IsAnyPrivacyCurtainVisible, DismissAllPrivacyCurtains);
            _hotkeysReady = true;
            RegisterSnippingHotkey();
            UpdateCaptureGuardState();
            EnsureDefaultAppHotkeys();
            RebuildAppHotkeys();

            if (ShowTrayIcon)
                InitTray();

            // Kiểm tra và hiển thị trạng thái Virtual Desktop
            UpdateDesktopAvailabilityUi();

            ScrubSelectionSurface();
            _ = RefreshAvailableWindowsAsync(force: true);
            if (_config.LastLoadError == null || _config.LoadedFromBackup)
            {
                _ = ApplyWebsiteBlockPreferenceAsync(
                    BlockRaiaHostCheckBox.IsChecked == true,
                    BlockPortalHostCheckBox.IsChecked == true,
                    saveConfig: false,
                    showProgress: false);
                _ = ApplyDevicePrivacyPreferenceAsync(
                    BlockCameraAccessCheckBox.IsChecked == true,
                    BlockMicrophoneAccessCheckBox.IsChecked == true,
                    saveConfig: false,
                    showProgress: false);
                if (BlockSystemToolsCheckBox.IsChecked == true)
                {
                    ApplySystemToolsPreference(
                        enabled: true,
                        saveConfig: false,
                        updateBanner: false);
                }
                if (MuteAntigravityNotificationsCheckBox.IsChecked == true)
                {
                    ApplyAntigravityNotificationsPreference(
                        muted: true,
                        saveConfig: false,
                        updateBanner: false);
                }

            }
            _normalRestoreBounds = new Rect(Left, Top, Width, Height);
            AnimateRestoreSmooth();

            if (_config.AutoStartShield)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!_cleanupDone && _config.AutoStartShield && !_isProtecting)
                    {
                        // Never black-screen the desktop automatically on app launch via AutoStartShield;
                        // user can explicitly resume the auto-curtain if desired.
                        _captureGuardPaused = true;
                        StartProtecting();
                    }
                }));
            }
        }

        // ── System Tray ───────────────────────────────────────────────────────
        private void InitTray()
        {
            // Tạo icon đơn giản (16x16 màu indigo) vì không có file .ico
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(new SolidBrush(System.Drawing.Color.FromArgb(0x81, 0x8c, 0xf8)), 1, 1, 14, 14);
            }

            var menu = new ContextMenuStrip { ShowItemToolTips = false, Cursor = System.Windows.Forms.Cursors.Default };
            menu.Items.Add("Show / Hide",  null, (_, _) => ToggleVisibility());
            menu.Items.Add("Privacy Curtain", null, (_, _) => TogglePrivacyCurtain());
            menu.Items.Add("Take Screenshot", null, (_, _) => TriggerSnippingTool());
            menu.Items.Add("-");
            menu.Items.Add("Panic — Show All", null, (_, _) => PanicRestoreAll());
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (_, _) => RequestExit());

            _trayIcon = new NotifyIcon
            {
                Icon             = System.Drawing.Icon.FromHandle(bmp.GetHicon()),
                Text             = "",
                ContextMenuStrip = menu,
                Visible          = true
            };
            _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
            RefreshTrayLanguage();
        }

        // ── Config ────────────────────────────────────────────────────────────
        private void LoadConfig()
        {
            _isLoadingConfig = true;
            foreach (var c in _config.Load())
                _apps.Add(new GuardedApp
                {
                    Name = c.Name,
                    ProcessName = c.ProcessName,
                    ExecutablePath = c.ExecutablePath,
                    IsEnabled = c.IsEnabled,
                    SecureHotkey = c.SecureHotkey,
                    IsHotkeyDisabled = c.IsHotkeyDisabled,
                    Status = string.IsNullOrWhiteSpace(c.ExecutablePath)
                        ? "Queued: visible"
                        : "Queued: secure launch ready"
                });
            BlockRaiaHostCheckBox.IsChecked = _config.BlockRaiaWebsite;
            BlockPortalHostCheckBox.IsChecked = _config.BlockPortalWebsite;
            BlockCameraAccessCheckBox.IsChecked = _config.BlockCameraAccess;
            BlockMicrophoneAccessCheckBox.IsChecked = _config.BlockMicrophoneAccess;
            PassiveFocusCheckBox.IsChecked = _config.PassiveFocusMode;
            _passiveFocusMode = _config.PassiveFocusMode;
            CaptureGuardCheckBox.IsChecked = _config.CaptureGuardEnabled;
            CaptureMultiPurposeCheckBox.IsChecked = _config.CaptureGuardIncludeMultiPurpose;
            if (InAppCursorCloakCheckBox != null)
                InAppCursorCloakCheckBox.IsChecked = _config.InAppCursorCloakEnabled;
            if (GlobalCursorCloakCheckBox != null)
                GlobalCursorCloakCheckBox.IsChecked = _config.GlobalCursorCloakEnabled;
            _cursorCaptureProtection.IsInAppCloakEnabled = _config.InAppCursorCloakEnabled;
            if (_config.GlobalCursorCloakEnabled)
            {
                _cursorCaptureProtection.StartGlobalCursorCloak();
            }
            else
            {
                _cursorCaptureProtection.StopGlobalCursorCloak();
            }
            UpdateCursorCloakUiState();
            BlockSystemToolsCheckBox.IsChecked = _config.BlockSystemTools;
            UpdateSystemToolsUiState(_config.BlockSystemTools);
            MuteAntigravityNotificationsCheckBox.IsChecked = _config.MuteAntigravityNotifications;
            UpdateAntigravityNotificationsUiState(_config.MuteAntigravityNotifications);
            UpdateAntigravityInstallerUiState();
            _antigravityInstaller.InstallationChanged += (_, _) => Dispatcher.Invoke(UpdateAntigravityInstallerUiState);
            EnableCursorShieldCheckBox.IsChecked = _config.EnableCursorShield;
            UpdateCursorShieldUiState(_config.EnableCursorShield);
            EnableAiTrafficShieldCheckBox.IsChecked = _config.EnableAiTrafficShield;
            UpdateAiTrafficShieldUiState(_config.EnableAiTrafficShield);
            TxtScreenshotDir.Text = _config.ScreenshotDirectory;
            ScreenshotClipboardCheckbox.IsChecked = _config.ScreenshotAutoCopyClipboard;
            ScreenshotExcludeCheckbox.IsChecked = _config.ScreenshotExcludeFromCapture;
            if (BlockVirtualDesktopHotkeysSettingsCheckBox != null)
                BlockVirtualDesktopHotkeysSettingsCheckBox.IsChecked = _config.BlockVirtualDesktopHotkeys;
            _hotkeys.BlockVirtualDesktopHotkeys = _config.BlockVirtualDesktopHotkeys;
            UpdateVirtualDesktopHotkeysUiState(_config.BlockVirtualDesktopHotkeys);
            UpdateDesktopAvailabilityUi();
            _snippingHotkey = string.IsNullOrWhiteSpace(_config.SnippingHotkey) ? "Ctrl+Alt+S" : _config.SnippingHotkey;
            _currentSnippingHotkey = _snippingHotkey;
            SyncSnippingHotkeyUi();
            if (AutoStartShieldCheckBox != null)
                AutoStartShieldCheckBox.IsChecked = _config.AutoStartShield;
            if (AutoStartShieldQuickCheckBox != null)
                AutoStartShieldQuickCheckBox.IsChecked = _config.AutoStartShield;
            UpdateAutoStartShieldUiState(_config.AutoStartShield);
            LocalizationService.Current.SetLanguage(_config.Language);
            LanguageComboBox.SelectedValue = LocalizationService.Current.Language;
            UpdateAntigravityInstallerUiState();
            _isLoadingConfig = false;
        }

        private bool SaveConfig() => _config.Save(
            _apps,
            BlockRaiaHostCheckBox.IsChecked == true || BlockPortalHostCheckBox.IsChecked == true,
            BlockRaiaHostCheckBox.IsChecked == true,
            BlockPortalHostCheckBox.IsChecked == true,
            BlockCameraAccessCheckBox.IsChecked == true,
            BlockMicrophoneAccessCheckBox.IsChecked == true,
            PassiveFocusCheckBox?.IsChecked == true,
            LocalizationService.Current.Language,
            CaptureGuardCheckBox?.IsChecked == true,
            CaptureMultiPurposeCheckBox?.IsChecked == true,
            inAppCursorCloakEnabled: InAppCursorCloakCheckBox?.IsChecked ?? _config.InAppCursorCloakEnabled,
            globalCursorCloakEnabled: GlobalCursorCloakCheckBox?.IsChecked ?? _config.GlobalCursorCloakEnabled,
            blockSystemTools: BlockSystemToolsCheckBox?.IsChecked == true,
            MuteAntigravityNotificationsCheckBox?.IsChecked == true,
            screenshotDirectory: TxtScreenshotDir?.Text,
            screenshotAutoCopyClipboard: ScreenshotClipboardCheckbox?.IsChecked,
            screenshotExcludeFromCapture: ScreenshotExcludeCheckbox?.IsChecked,
            enableCursorShield: EnableCursorShieldCheckBox?.IsChecked ?? _config.EnableCursorShield,
            snippingHotkey: _snippingHotkey,
            enableAiTrafficShield: EnableAiTrafficShieldCheckBox?.IsChecked ?? _config.EnableAiTrafficShield,
            blockVirtualDesktopHotkeys: BlockVirtualDesktopHotkeysSettingsCheckBox?.IsChecked ?? _config.BlockVirtualDesktopHotkeys,
            autoStartShield: AutoStartShieldCheckBox?.IsChecked ?? AutoStartShieldQuickCheckBox?.IsChecked ?? _config.AutoStartShield);

        private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoadingConfig || e.AddedItems.Count == 0 ||
                e.AddedItems[0] is not System.Windows.Controls.ComboBoxItem { Tag: string language }) return;
            // This updates presentation bindings only. It must not restart protection or rebuild hotkeys.
            LocalizationService.Current.SetLanguage(language);
            RefreshTrayLanguage();
            SyncSnippingHotkeyUi();
            UpdateAntigravityInstallerUiState();
            RefreshDiagnosticsLanguage();
            RefreshTranscriptLanguage();
            _toastService?.UpdateFooter();
            if (!SaveConfig())
                UpdateStatus("Language changed for this session, but could not be saved. Check configuration file permissions.", false);
        }

        private void RefreshTrayLanguage()
        {
            var items = _trayIcon?.ContextMenuStrip?.Items;
            if (items == null || items.Count < 7) return;
            items[0].Text = LocalizationService.Current.Translate("Show / Hide");
            items[1].Text = LocalizationService.Current.Translate("Privacy Curtain");

            var actionTake = LocalizationService.GetString("Snipping.ActionTakeScreenshot",
                LocalizationService.Current.Language == "vi" ? "Chụp màn hình" : "Take Screenshot");
            items[2].Text = _snippingHotkey == "Ctrl+Alt+S"
                ? LocalizationService.GetString("Snipping.TrayMenuItem", LocalizationService.Current.Language == "vi" ? "Chụp màn hình (Ctrl+Alt+S)" : "Take Screenshot (Ctrl+Alt+S)")
                : $"{actionTake} ({_snippingHotkey})";

            items[4].Text = LocalizationService.Current.Translate("Panic — Show All");
            items[6].Text = LocalizationService.Current.Translate("Exit");
        }

        private void EnsureDefaultAppHotkeys()
        {
            foreach (var app in _apps)
            {
                if (string.IsNullOrWhiteSpace(app.SecureHotkey) && !app.IsHotkeyDisabled)
                    app.SecureHotkey = GetNextDefaultHotkey();
            }
        }

        private string GetNextDefaultHotkey()
        {
            var used = _apps
                .Select(a => a.SecureHotkey)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i <= 9; i++)
            {
                var candidate = $"Ctrl+Alt+{i}";
                if (!used.Contains(candidate))
                    return candidate;
            }

            for (var i = 1; i <= 12; i++)
            {
                var candidate = $"Ctrl+Alt+F{i}";
                if (!used.Contains(candidate))
                    return candidate;
            }

            return "";
        }

        private static bool IsReservedSystemHotkey(string hotkey)
        {
            var chord = hotkey.Replace(" ", "");
            if (chord.Equals("Shift+S+G", StringComparison.OrdinalIgnoreCase) ||
                chord.Equals("Ctrl+Shift+S+G", StringComparison.OrdinalIgnoreCase)) return true;
            if (!TryParseHotkey(hotkey, out var modifiers, out var key, out var normalized, out _)) return false;

            // Reserved for Snipping Tool
            var snip = _currentSnippingHotkey;
            if (!string.IsNullOrWhiteSpace(snip) &&
                TryParseHotkey(snip, out _, out _, out var normSnip, out _) &&
                normalized.Equals(normSnip, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return (modifiers == (HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT) && key == 0x47) ||
                (modifiers == (HotkeyService.MOD_CTRL | HotkeyService.MOD_ALT) && key == 0x54) ||
                (modifiers == (HotkeyService.MOD_CTRL | HotkeyService.MOD_ALT | HotkeyService.MOD_SHIFT) &&
                    key is 0x41 or 0x43 or 0x48 or 0x58);
        }

        private void TemporarilyUnregisterAppHotkeys()
        {
            foreach (var id in _appHotkeyIds.Values.ToList())
                _hotkeys.Unregister(id);
            _appHotkeyIds.Clear();
        }

        private void CommitSecureHotkey(GuardedApp app, string hotkeyText)
        {
            if (string.IsNullOrWhiteSpace(hotkeyText))
            {
                app.SecureHotkey = "";
                app.IsHotkeyDisabled = true;
                app.Status = "Secure hotkey disabled";
                Refresh(app);
                SaveConfig();
                RebuildAppHotkeys();
                UpdateStatus($"Đã tắt phím tắt cho {app.ProcessName}", false);
                return;
            }

            if (!TryParseHotkey(hotkeyText, out _, out _, out var normalized, out var error))
            {
                app.Status = $"Invalid hotkey: {error}";
                Refresh(app);
                RebuildAppHotkeys();
                UpdateStatus($"Phím tắt không hợp lệ cho {app.ProcessName}: {error}", false);
                return;
            }

            if (IsReservedSystemHotkey(normalized))
            {
                app.Status = "This shortcut is reserved for application recovery.";
                Refresh(app);
                RebuildAppHotkeys();
                UpdateStatus(app.Status, false);
                return;
            }

            // Kiểm tra trùng phím tắt với ứng dụng khác trong danh sách
            var duplicateApp = _apps.FirstOrDefault(a => a != app && TryParseHotkey(a.SecureHotkey, out _, out _, out var existing, out _) &&
                existing.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (duplicateApp != null)
            {
                app.Status = $"Trùng phím tắt với {duplicateApp.ProcessName}";
                Refresh(app);
                RebuildAppHotkeys();
                UpdateStatus($"Phím tắt {normalized} đã được gán cho {duplicateApp.ProcessName}! Vui lòng chọn phím tắt khác.", false);
                return;
            }

            app.SecureHotkey = normalized;
            app.IsHotkeyDisabled = false;
            app.Status = $"Secure hotkey ready: {normalized}";
            Refresh(app);
            SaveConfig();
            RebuildAppHotkeys();
            UpdateStatus($"Đã gán phím tắt {normalized} cho {app.ProcessName} thành công", true);
        }

        private void RebuildAppHotkeys()
        {
            if (!_hotkeysReady)
                return;

            foreach (var id in _appHotkeyIds.Values.ToList())
                _hotkeys.Unregister(id);
            _appHotkeyIds.Clear();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in _apps.ToList())
            {
                if (!IsManageableApp(app)) continue;
                if (app.IsHotkeyDisabled || string.IsNullOrWhiteSpace(app.SecureHotkey))
                    continue;

                if (!TryParseHotkey(app.SecureHotkey, out var modifiers, out var vk, out var normalized, out var error))
                {
                    app.Status = $"Invalid hotkey: {error}";
                    Refresh(app);
                    continue;
                }

                app.SecureHotkey = normalized;
                if (IsReservedSystemHotkey(normalized))
                {
                    app.Status = "This shortcut is reserved for application recovery.";
                    Refresh(app);
                    continue;
                }
                if (!used.Add(normalized))
                {
                    app.Status = $"Duplicate hotkey: {normalized}";
                    Refresh(app);
                    continue;
                }

                var capturedApp = app;
                var id = _hotkeys.Register(modifiers, vk, () => _ = SecureOpenAppAsync(capturedApp));
                if (id < 0)
                {
                    app.Status = $"Hotkey unavailable: {normalized}";
                    Refresh(app);
                    continue;
                }

                _appHotkeyIds[app] = id;
            }
        }

        private static bool TryParseHotkey(
            string text,
            out uint modifiers,
            out uint vk,
            out string normalized,
            out string error)
        {
            modifiers = 0;
            vk = 0;
            normalized = "";
            error = "";

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                error = "use a modifier, e.g. Ctrl+Alt+1";
                return false;
            }

            var keyText = "";
            foreach (var raw in parts)
            {
                var part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyService.MOD_CTRL;
                }
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyService.MOD_ALT;
                }
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyService.MOD_SHIFT;
                }
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyService.MOD_WIN;
                }
                else if (keyText.Length == 0)
                {
                    keyText = part;
                }
                else
                {
                    error = "only one non-modifier key is allowed";
                    return false;
                }
            }

            if (modifiers == 0)
            {
                error = "add Ctrl, Alt, or Shift";
                return false;
            }

            if (!TryParseVirtualKey(keyText, out vk, out var keyName))
            {
                error = "supported keys are A-Z, 0-9, and F1-F12";
                return false;
            }

            var labels = new List<string>();
            if ((modifiers & HotkeyService.MOD_CTRL) != 0) labels.Add("Ctrl");
            if ((modifiers & HotkeyService.MOD_WIN) != 0) labels.Add("Win");
            if ((modifiers & HotkeyService.MOD_ALT) != 0) labels.Add("Alt");
            if ((modifiers & HotkeyService.MOD_SHIFT) != 0) labels.Add("Shift");
            labels.Add(keyName);
            normalized = string.Join("+", labels);
            return true;
        }

        private static bool TryParseVirtualKey(string keyText, out uint vk, out string keyName)
        {
            vk = 0;
            keyName = "";

            if (string.IsNullOrWhiteSpace(keyText))
                return false;

            var key = keyText.Trim().ToUpperInvariant();
            if (key.Length == 1)
            {
                var c = key[0];
                if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                {
                    vk = c;
                    keyName = c.ToString();
                    return true;
                }
            }

            if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 12)
            {
                vk = (uint)(0x70 + f - 1);
                keyName = $"F{f}";
                return true;
            }

            return false;
        }

        // ── Process Combo ─────────────────────────────────────────────────────
        private Func<List<WindowInfo>> _enumerateAvailableWindows = WindowEnumerator.GetOpenWindows;
        private async System.Threading.Tasks.Task RefreshAvailableWindowsAsync(bool force = false)
        {
            if (_cleanupDone || _isRefreshingAvailableWindows)
                return;

            if (force)
            {
                ProcessSafetyService.Default.ClearCache();
            }
            else if (DateTime.Now - _lastAvailableWindowsRefresh < TimeSpan.FromSeconds(2))
                return;

            _isRefreshingAvailableWindows = true;
            LocalizationService.BindText(BtnRefreshApps, System.Windows.Controls.ContentControl.ContentProperty, "Scanning");

            try
            {
                var windows = await System.Threading.Tasks.Task.Run(() =>
                    _enumerateAvailableWindows()
                        .Where(IsSelectableWindow)
                        .GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g
                            .OrderByDescending(w => !string.IsNullOrWhiteSpace(w.ExecutablePath))
                            .First())
                        .Take(80)
                        .ToList());

                if (_cleanupDone || Dispatcher.HasShutdownStarted) return;
                _availableWindows.Clear();
                foreach (var window in windows)
                    _availableWindows.Add(window);

                SyncAvailableWindowsShieldState();
                _lastAvailableWindowsRefresh = DateTime.Now;
                UpdateAvailableEmptyState();
            }
            catch (Exception ex)
            {
                if (!_cleanupDone && !Dispatcher.HasShutdownStarted) UpdateStatus($"Could not scan open apps: {ex.Message}", false);
            }
            finally
            {
                if (!_cleanupDone && !Dispatcher.HasShutdownStarted)
                    LocalizationService.BindText(BtnRefreshApps, System.Windows.Controls.ContentControl.ContentProperty, "Refresh");
                _isRefreshingAvailableWindows = false;
            }
        }

        private bool IsSelectableWindow(WindowInfo? window)
        {
            return window != null
                && window.Handle != IntPtr.Zero
                && IsWindow(window.Handle)
                && WindowEnumerator.IsValidCandidateWindow(window.Handle)
                && !string.IsNullOrWhiteSpace(window.ProcessName)
                && !ProcessSafetyService.IsProtectedIdentity(window.ProcessName, window.ExecutablePath)
                && ProcessSafetyService.Default.CanManageWindow(window.Handle, window.ProcessId)
                && !IsOwnProcess(window.ProcessName);
        }

        private void RefreshProcessCombo()
        {
            var windows = _availableWindows.ToList();

            ProcessCombo.ItemsSource  = windows.Select(w => $"{w.ProcessName}  —  {w.Title}").ToList();
            ProcessCombo.Tag          = windows;
        }

        private void ProcessCombo_DropDownOpened(object sender, EventArgs e) => _ = RefreshAvailableWindowsAsync(force: true);

        private void ProcessCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressProcessSelection || ProcessCombo.SelectedIndex < 0)
                return;

            var app = AddSelectedProcessToGuardedApps();
            ScrubSelectionSurface();
            if (app != null)
                UpdateStatus($"Ready to protect {app.ProcessName}", false);
        }

        private void AddForegroundWindowToGuardedApps()
        {
            var foreground = GetForegroundWindowInfo();

            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (foreground != null && (ProcessSafetyService.IsProtectedIdentity(foreground.ProcessName, foreground.ExecutablePath) ||
                    !ProcessSafetyService.Default.CanManageWindow(foreground.Handle, foreground.ProcessId)))
                {
                    UpdateStatus("System or unverified processes cannot be managed.", false);
                    return;
                }
                ProtectSelfFromCapture();

                var selected = IsSelectableWindow(foreground)
                    ? foreground
                    : AvailableWindowsListBox.SelectedItem as WindowInfo;

                if (!IsSelectableWindow(selected))
                {
                    await RefreshAvailableWindowsAsync(force: true);
                    selected = _availableWindows.FirstOrDefault(IsSelectableWindow);
                }

                if (!IsSelectableWindow(selected))
                {
                    UpdateStatus("No app found to add. Open or select an app first.", false);
                    return;
                }

                ScrubSelectionSurface();
                var app = AddWindowToGuardedApps(selected!);
                if (app != null)
                {
                    UpdateStatus($"Added {app.ProcessName} to protected apps", true);
                    await RefreshAvailableWindowsAsync(force: true);
                }
            });
        }

        private WindowInfo? GetForegroundWindowInfo()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return null;

            var ownHwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == ownHwnd)
                return null;

            var titleLength = GetWindowTextLength(hwnd);
            var title = "";
            if (titleLength > 0)
            {
                var sb = new StringBuilder(titleLength + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                title = sb.ToString();
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return null;

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(unchecked((int)pid));
                var processName = process.ProcessName;
                if (string.IsNullOrWhiteSpace(processName) || IsOwnProcess(processName))
                    return null;

                var exePath = "";
                try { exePath = process.MainModule?.FileName ?? ""; } catch { }

                return new WindowInfo
                {
                    Handle = hwnd,
                    Title = string.IsNullOrWhiteSpace(title) ? processName : title,
                    ProcessName = processName,
                    ProcessId = unchecked((int)pid),
                    ExecutablePath = exePath
                };
            }
            catch
            {
                return null;
            }
        }

        private void RevealProcessPicker()
        {
            _ = RefreshAvailableWindowsAsync(force: true);
        }

        // ── Add / Remove ──────────────────────────────────────────────────────
        private async void RefreshApps_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAvailableWindowsAsync(force: true);
            UpdateStatus($"Available apps refreshed: {_availableWindows.Count}", false);
        }

        private async void AddApp_Click(object sender, RoutedEventArgs e)
        {
            var app = AddSelectedProcessToGuardedApps();
            if (app == null)
            {
                await RefreshAvailableWindowsAsync(force: true);
                UpdateStatus("Select an app in Available Apps, then click Add Selected", false);
                return;
            }

            ScrubSelectionSurface();
            UpdateStatus($"Ready to protect {app.ProcessName}", false);
        }

        private void AvailableWindowsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var app = AddSelectedProcessToGuardedApps();
            if (app != null)
            {
                ScrubSelectionSurface();
                UpdateStatus($"Ready to protect {app.ProcessName}", false);
            }
        }

        private GuardedApp? AddSelectedProcessToGuardedApps()
        {
            if (AvailableWindowsListBox.SelectedItem is WindowInfo available && IsSelectableWindow(available))
                return AddWindowToGuardedApps(available);

            if (ProcessCombo.SelectedIndex < 0) return null;
            var windows = ProcessCombo.Tag as System.Collections.Generic.List<WindowInfo>;
            if (windows == null || ProcessCombo.SelectedIndex >= windows.Count) return null;

            var selected = windows[ProcessCombo.SelectedIndex];
            return AddWindowToGuardedApps(selected);
        }

        private GuardedApp? AddWindowToGuardedApps(WindowInfo selected)
        {
            if (ProcessSafetyService.IsProtectedIdentity(selected.ProcessName, selected.ExecutablePath) ||
                !ProcessSafetyService.Default.CanManageWindow(selected.Handle, selected.ProcessId))
            {
                UpdateStatus("System or unverified processes cannot be managed.", false);
                return null;
            }
            if (IsOwnProcess(selected.ProcessName))
            {
                ProtectSelfFromCapture();
                UpdateStatus("Driver Booster is already self-protected", true);
                return null;
            }

            var name = selected.Title.Length > 40 ? selected.Title[..40] + "..." : selected.Title;
            var existing = _apps.FirstOrDefault(a =>
                a.ProcessName.Equals(selected.ProcessName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Name = name;
                existing.WindowHandle = selected.Handle;
                if (!string.IsNullOrWhiteSpace(selected.ExecutablePath))
                    existing.ExecutablePath = selected.ExecutablePath;
                existing.IsEnabled = true;
                if (string.IsNullOrWhiteSpace(existing.SecureHotkey) && !existing.IsHotkeyDisabled)
                    existing.SecureHotkey = GetNextDefaultHotkey();
                if (!existing.IsProtected)
                    existing.Status = string.IsNullOrWhiteSpace(existing.ExecutablePath)
                        ? "Queued: visible"
                        : "Queued: secure launch ready";
                Refresh(existing);
                SaveConfig();
                RebuildAppHotkeys();
                return existing;
            }

            var app = new GuardedApp
            {
                Name = name,
                ProcessName = selected.ProcessName,
                ExecutablePath = selected.ExecutablePath,
                WindowHandle = selected.Handle,
                IsEnabled = true,
                SecureHotkey = GetNextDefaultHotkey(),
                Status = string.IsNullOrWhiteSpace(selected.ExecutablePath)
                    ? "Queued: visible"
                    : "Queued: secure launch ready"
            };

            _apps.Add(app);
            SaveConfig();
            RebuildAppHotkeys();
            return app;
        }

        private void AppEnabledCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig || sender is not System.Windows.Controls.CheckBox cb || cb.Tag is not GuardedApp app)
                return;

            app.IsEnabled = cb.IsChecked == true;
            if (!app.IsEnabled && IsAppCurrentlyGuarded(app))
            {
                RestoreApp(app);
                if (!HasActiveAppProtection())
                    SetProtectionUiState(false);
                UpdateCursorShieldForProtectionState();
            }

            if (!IsAppCurrentlyGuarded(app))
            {
                app.Status = !app.IsEnabled
                    ? "Disabled"
                    : string.IsNullOrWhiteSpace(app.ExecutablePath)
                        ? "Queued: visible"
                        : "Queued: secure launch ready";
            }

            SaveConfig();
            RebuildAppHotkeys();
            UpdateStopButtonState();
            UpdateStatus(
                app.IsEnabled
                    ? $"Ready to protect {app.ProcessName}"
                    : $"Disabled protection for {app.ProcessName}",
                HasActiveAppProtection());
        }

        private void RemoveApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GuardedApp app)
            {
                if (IsAppCurrentlyGuarded(app) && !RestoreApp(app)) return;
                _apps.Remove(app);
                if (!HasActiveAppProtection())
                    SetProtectionUiState(false);
                UpdateCursorShieldForProtectionState();
                SaveConfig();
                RebuildAppHotkeys();
            }
        }

        private void SecureApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GuardedApp app)
                _ = SecureOpenAppAsync(app);
        }

        private void SecureHotkey_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb && !tb.IsFocused)
            {
                tb.Focus();
                e.Handled = true;
            }
        }

        private void SecureHotkey_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb && tb.Tag is GuardedApp app)
            {
                _isRecordingHotkey = true;
                _recordingApp = app;
                _recordingOriginalHotkey = app.SecureHotkey;

                TemporarilyUnregisterAppHotkeys();
                ResetBadgeVisual(tb, isRecording: true);
                tb.Text = LocalizationService.Current.Translate("Nhấn phím...");
                UpdateStatus($"Đang ghi nhận phím tắt cho {app.ProcessName}: Bấm tổ hợp phím (ví dụ Ctrl+Alt+1), Esc để hủy, Del để xóa phím tắt", true);
            }
        }

        private void SecureHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb || tb.Tag is not GuardedApp app)
                return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // 1. Phím Escape: Hủy bỏ và giữ nguyên phím tắt cũ
            if (key == Key.Escape)
            {
                CancelRecording(tb);
                UpdateStatus($"Đã hủy đổi phím tắt cho {app.ProcessName}", false);
                e.Handled = true;
                return;
            }

            // 2. Phím Delete hoặc Backspace: Xóa/tắt phím tắt
            if (key is Key.Delete or Key.Back)
            {
                FinishRecording(tb);
                CommitSecureHotkey(app, "");
                e.Handled = true;
                return;
            }

            // 3. Kiểm tra trạng thái các phím bổ trợ (Ctrl, Alt, Shift)
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ||
                        Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ||
                       Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ||
                         Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            // 4. Nếu chỉ vừa bấm các phím bổ trợ (chưa ấn phím ký tự/hàm chính) -> hiển thị phản hồi trực tiếp
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                var modParts = new List<string>();
                if (ctrl) modParts.Add("Ctrl");
                if (alt) modParts.Add("Alt");
                if (shift) modParts.Add("Shift");

                tb.Text = modParts.Count > 0 ? string.Join("+", modParts) + "+..." : LocalizationService.Current.Translate("Nhấn phím...");
                e.Handled = true;
                return;
            }

            // 5. Nếu ấn phím chính hợp lệ (A-Z, 0-9, F1-F12)
            if (TryConvertWpfKeyToKeyName(key, out var keyName))
            {
                if (!ctrl && !alt && !shift)
                {
                    UpdateStatus("Phím tắt cần ít nhất một phím bổ trợ: Ctrl, Alt hoặc Shift (ví dụ: Ctrl+Alt+1)", false);
                    e.Handled = true;
                    return;
                }

                var parts = new List<string>();
                if (ctrl) parts.Add("Ctrl");
                if (alt) parts.Add("Alt");
                if (shift) parts.Add("Shift");
                parts.Add(keyName);

                var hotkeyString = string.Join("+", parts);

                // Kiểm tra xem có trùng với phím tắt hệ thống StreamGuard không
                if (IsReservedSystemHotkey(hotkeyString))
                {
                    UpdateStatus($"Tổ hợp phím {hotkeyString} đã dành riêng cho chức năng hệ thống Driver Booster. Hãy chọn phím khác (ví dụ: Ctrl+Alt+1..9)", false);
                    e.Handled = true;
                    return;
                }

                tb.Text = hotkeyString;
                FinishRecording(tb);
                CommitSecureHotkey(app, hotkeyString);
                e.Handled = true;
                return;
            }

            // Chặn các phím không hỗ trợ khác
            e.Handled = true;
        }

        private void SecureHotkey_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb && _isRecordingHotkey)
            {
                if (tb.Tag is GuardedApp app)
                {
                    if (tb.Text == LocalizationService.Current.Translate("Nhấn phím...") || tb.Text.EndsWith("+..."))
                    {
                        tb.Text = _recordingOriginalHotkey;
                        app.SecureHotkey = _recordingOriginalHotkey;
                    }
                }
                FinishRecording(tb);
            }
        }

        private void CancelRecording(System.Windows.Controls.TextBox tb)
        {
            if (tb.Tag is GuardedApp app)
            {
                tb.Text = _recordingOriginalHotkey;
                app.SecureHotkey = _recordingOriginalHotkey;
            }
            FinishRecording(tb);
        }

        private void FinishRecording(System.Windows.Controls.TextBox tb)
        {
            _isRecordingHotkey = false;
            _recordingApp = null;
            _recordingOriginalHotkey = "";

            ResetBadgeVisual(tb, isRecording: false);
            RebuildAppHotkeys();
            Keyboard.ClearFocus();
        }

        private static void ResetBadgeVisual(System.Windows.Controls.TextBox tb, bool isRecording)
        {
            if (tb.Parent is System.Windows.Controls.StackPanel sp && sp.Parent is System.Windows.Controls.Border badge)
            {
                badge.BorderBrush = isRecording
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x81, 0x8c, 0xf8))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x31, 0x2e, 0x81));
                badge.Background = isRecording
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1e, 0x1b, 0x4b))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x13, 0x19, 0x2b));
            }
            tb.Foreground = isRecording
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf4, 0x72, 0xb6))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xc7, 0xd2, 0xfe));
        }

        private static bool TryConvertWpfKeyToKeyName(Key key, out string keyName)
        {
            keyName = "";
            if (key is >= Key.A and <= Key.Z)
            {
                keyName = key.ToString().ToUpperInvariant();
                return true;
            }
            if (key is >= Key.D0 and <= Key.D9)
            {
                keyName = ((char)('0' + (key - Key.D0))).ToString();
                return true;
            }
            if (key is >= Key.NumPad0 and <= Key.NumPad9)
            {
                keyName = ((char)('0' + (key - Key.NumPad0))).ToString();
                return true;
            }
            if (key is >= Key.F1 and <= Key.F12)
            {
                keyName = key.ToString().ToUpperInvariant();
                return true;
            }
            return false;
        }

        private void FocusApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GuardedApp app)
                FocusGuardedApp(app);
        }

        private void AppListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AppListBox.SelectedItem is GuardedApp app)
                FocusGuardedApp(app);
        }

        private void FocusGuardedApp(GuardedApp app)
        {
            if (!IsManageableApp(app)) return;
            var hwnd = app.ProtectedWindows
                .Select(s => s.Handle)
                .FirstOrDefault(h => h != IntPtr.Zero && IsWindow(h));
            if (hwnd == IntPtr.Zero && app.WindowHandle != IntPtr.Zero && IsWindow(app.WindowHandle))
                hwnd = app.WindowHandle;

            var wasProtected = IsAppCurrentlyGuarded(app);
            if (wasProtected)
            {
                if (BringProtectedAppToFront(app))
                {
                    UpdateStatus($"Secure focus: {app.ProcessName} stays protected", true);
                    return;
                }

                if (app.DesktopId.HasValue && _vdService.IsAvailable)
                {
                    if (_vdService.ToggleHiddenDesktop())
                    {
                        UpdateStatus($"Switched view to {app.ProcessName} on Desktop 2. Press hotkey or toggle again to return.", true);
                        return;
                    }
                }

                if (!RestoreApp(app)) { UpdateStatus(app.Status, false); return; }
                if (!HasActiveAppProtection())
                    SetProtectionUiState(false);
                UpdateCursorShieldForProtectionState();
            }

            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                hwnd = WindowEnumerator.FindAllByProcessName(app.ProcessName)
                    .Select(w => w.Handle)
                    .FirstOrDefault(h => h != IntPtr.Zero && IsWindow(h));
            }

            if (hwnd == IntPtr.Zero)
            {
                UpdateStatus($"No active window found for {app.ProcessName}", false);
                return;
            }

            if (!IsAppWindow(app, hwnd)) return;
            TryShowWindow(hwnd);
            SetForegroundWindow(hwnd);
            app.WindowHandle = hwnd;
            app.Status = wasProtected
                ? $"Visible for control at {DateTime.Now:HH:mm:ss} - press Start Protecting again when done"
                : $"Focused at {DateTime.Now:HH:mm:ss}";
            Refresh(app);
            UpdateStatus(wasProtected
                ? $"Restored {app.ProcessName} for interaction. Press Start Protecting again when done."
                : $"Focused {app.ProcessName}", true);
        }

        private async System.Threading.Tasks.Task SecureOpenAppAsync(GuardedApp app)
        {
            if (_cleanupDone || _isStartingProtection || !_apps.Contains(app) || !IsManageableApp(app) || !_pendingSecureOpens.Add(app)) return;
            try { await SecureOpenAppCoreAsync(app, _protectionRevision); }
            catch (Exception error) { if (!_cleanupDone) UpdateStatus($"Could not open application: {error.Message}", false); }
            finally { _pendingSecureOpens.Remove(app); }
        }

        private bool IsCurrentProtectionRequest(int revision, GuardedApp app) =>
            !_cleanupDone && revision == _protectionRevision && _apps.Contains(app) && app.IsEnabled && IsManageableApp(app);

        private async System.Threading.Tasks.Task SecureOpenAppCoreAsync(GuardedApp app, int revision)
        {
            if (!IsManageableApp(app)) return;
            ProtectSelfFromCapture();
            ScrubSelectionSurface();
            app.IsEnabled = true;

            if (IsAppCurrentlyGuarded(app))
            {
                // ── Toggle Show/Hide ─────────────────────────────────────────
                // 1. Nếu app đang visible (wrapper window đang hiển thị) → ẩn nó đi
                if (IsProtectedAppVisible(app))
                {
                    HideProtectedApp(app);
                    app.Status = $"Hidden at {DateTime.Now:HH:mm:ss} — hotkey to show again";
                    Refresh(app);
                    UpdateStatus($"Hidden: {app.ProcessName} (press hotkey again to show)", true);
                    return;
                }

                // 2. App đang ẩn dạng wrapper → bring to front
                if (BringProtectedAppToFront(app))
                {
                    UpdateStatus($"Secure focus: {app.ProcessName} stays protected", true);
                    return;
                }

                // 3. Virtual Desktop 2 mode: Toggle view giữa Desktop 1 và Desktop 2
                if (app.DesktopId.HasValue && _vdService.IsAvailable)
                {
                    if (_vdService.ToggleHiddenDesktop())
                    {
                        UpdateStatus($"Toggled desktop view for {app.ProcessName} on Desktop 2", true);
                        return;
                    }
                }

                // BringToFront fail & VD switch unavailable -> restore to visible state
                if (!RestoreApp(app)) { UpdateStatus(app.Status, false); return; }
                if (!HasActiveAppProtection())
                    SetProtectionUiState(false);
                UpdateCursorShieldForProtectionState();
                app.Status = $"Visible for interaction at {DateTime.Now:HH:mm:ss}";
                Refresh(app);
                UpdateStatus($"Restored {app.ProcessName} to visible state. Press hotkey again to protect.", true);
                return;
            }

            var windows = GetCandidateWindows(app);
            if (windows.Count == 0)
                windows = await _launchAppWindows(app);

            if (!IsCurrentProtectionRequest(revision, app)) return;

            if (windows.Count == 0)
            {
                app.Status = "Waiting: no visible window for secure hotkey";
                Refresh(app);
                UpdateStatus($"No visible window found for {app.ProcessName}", false);
                return;
            }

            if (ProtectSingleApp(app, windows, out var protectedWindows))
            {
                SetProtectionUiState(true);
                UpdateCursorShieldForProtectionState();
                BringProtectedAppToFront(app);
                UpdateStatus($"Secure hotkey protected {app.ProcessName}: windows={protectedWindows}", true);
                UpdatePhysicalOutputGuard(forceStatus: true);
                SaveConfig();
            }
        }

        /// <summary>
        /// Kiểm tra xem app đang được bảo vệ có đang visible (wrapper window hiển thị) không.
        /// </summary>
        private static bool IsProtectedAppVisible(GuardedApp app)
        {
            foreach (var state in app.ProtectedWindows)
            {
                // Wrapper window: kiểm tra WrapperWindow hoặc WrapperForm
                if (state.WrapperWindow != null)
                {
                    if (state.WrapperWindow.IsVisible) return true;
                    continue;
                }
                if (state.WrapperForm is System.Windows.Forms.Form form && !form.IsDisposed)
                {
                    if (form.Visible && form.Location.X > -10000 && form.Location.Y > -10000)
                        return true;
                }
                // Hide fallback: window vẫn đang ẩn → return false
                if (state.HiddenByStreamGuard)
                    continue;
            }
            // Không có protected windows cụ thể → dùng WindowHandle
            return false;
        }

        /// <summary>
        /// Ẩn app đang được bảo vệ mà không release protection (toggle hide).
        /// </summary>
        private void HideProtectedApp(GuardedApp app)
        {
            if (!IsManageableApp(app)) return;
            foreach (var state in app.ProtectedWindows)
            {
                if (!ProcessSafetyService.Default.CanManageWindow(state.Handle, state.ProcessId)) continue;
                if (state.WrapperForm != null)
                {
                    ProtectedWindowWrapperService.HideWrapper(state);
                }
                else if (state.WrapperWindow != null)
                {
                    // Ẩn wrapper window bằng WinAPI thay vì Hide() để tránh mất ownership
                    var wrapperHwnd = new WindowInteropHelper(state.WrapperWindow).Handle;
                    if (wrapperHwnd != IntPtr.Zero)
                        ShowWindow(wrapperHwnd, SW_HIDE);
                }
                else if (state.Handle != IntPtr.Zero && IsWindow(state.Handle))
                {
                    ShowWindow(state.Handle, SW_HIDE);
                }
            }
        }

        private async System.Threading.Tasks.Task<System.Collections.Generic.List<WindowInfo>> LaunchAndWaitForWindowsAsync(GuardedApp app)
        {
            var revision = _protectionRevision;
            if (!IsManageableApp(app)) return new();
            var windows = GetCandidateWindows(app);
            if (windows.Count > 0)
                return windows;

            if (string.IsNullOrWhiteSpace(app.ExecutablePath) || !System.IO.File.Exists(app.ExecutablePath))
                return windows;

            // Do not relaunch an existing service/critical or unreadable instance under a desktop account.
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(app.ProcessName))
            {
                using (process)
                    if (!ProcessSafetyService.Default.CanManageProcess(process.Id)) return windows;
            }

            try
            {
                using var launched = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(app.ExecutablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(app.ExecutablePath) ?? ""
                });
                app.Status = "Launching under protection...";
                Refresh(app);
            }
            catch (Exception ex)
            {
                app.Status = $"Launch failed: {ex.Message}";
                Refresh(app);
                UpdateStatus($"Could not launch {app.ProcessName}: {ex.Message}", false);
                return windows;
            }

            for (var attempt = 0; attempt < 40; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(100);
                if (!IsCurrentProtectionRequest(revision, app)) return new();
                windows = GetCandidateWindows(app);
                if (windows.Count > 0)
                    break;
            }

            return windows;
        }

        private bool BringProtectedAppToFront(GuardedApp app)
        {
            if (!IsManageableApp(app)) return false;
            var brought = false;
            foreach (var state in app.ProtectedWindows.ToList())
            {
                if (state.Handle == IntPtr.Zero || !IsWindow(state.Handle))
                {
                    if (state.ProcessId > 0)
                        ProcessSafetyService.Default.InvalidateCache(state.ProcessId);
                    app.ProtectedWindows.Remove(state);
                    continue;
                }
                if (!ProcessSafetyService.Default.CanManageWindow(state.Handle, state.ProcessId)) continue;

                if (ProtectedWindowWrapperService.TryBringProtectedToFront(state))
                {
                    brought = true;
                }
            }

            if (brought)
            {
                app.Status = $"Secure focus at {DateTime.Now:HH:mm:ss} - still protected";
                Refresh(app);
                UpdateCursorShieldForProtectionState();
                return true;
            }

            return false;
        }

        private System.Collections.Generic.List<WindowInfo> GetCandidateWindows(GuardedApp app)
        {
            if (!IsManageableApp(app)) return new();
            // Dùng FindAllIncludingHiddenByProcessName để bắt cả hidden/background apps
            var windows = WindowEnumerator.FindAllIncludingHiddenByProcessName(app.ProcessName)
                .Where(window => IsAppWindow(app, window.Handle, window.ProcessId))
                .GroupBy(w => w.Handle)
                .Select(g => g.First())
                .ToList();

            // Fallback cuối: dùng app.WindowHandle đã biết trước đó
            if (windows.Count == 0 && app.WindowHandle != IntPtr.Zero && IsWindow(app.WindowHandle) &&
                IsAppWindow(app, app.WindowHandle))
            {
                windows.Add(new WindowInfo
                {
                    Handle = app.WindowHandle,
                    ProcessId = GetProcessIdForWindow(app.WindowHandle),
                    ProcessName = app.ProcessName,
                    Title = app.Name
                });
            }

            return windows;
        }

        private static bool MatchesAppProcess(GuardedApp app, ProcessSafetyInfo process)
        {
            if (!process.Name.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(app.ExecutablePath)) return true;
            try { return System.IO.Path.GetFullPath(app.ExecutablePath).Equals(System.IO.Path.GetFullPath(process.ExecutablePath), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static bool IsAppWindow(GuardedApp app, IntPtr window, int expectedProcessId = 0)
        {
            var processId = GetProcessIdForWindow(window);
            return processId > 0 && (expectedProcessId == 0 || expectedProcessId == processId) &&
                ProcessSafetyService.Default.TryGetManageableProcess(processId, out var process) && process != null && MatchesAppProcess(app, process);
        }

        private static int GetProcessIdForWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                    return 0;

                GetWindowThreadProcessId(hwnd, out var pid);
                return unchecked((int)pid);
            }
            catch
            {
                return 0;
            }
        }

        private bool ProtectSingleApp(GuardedApp app, System.Collections.Generic.IReadOnlyCollection<WindowInfo> windows, out int protectedWindows)
        {
            protectedWindows = 0;
            if (!IsManageableApp(app)) return false;
            var wrappedProtected = 0;
            var desktopProtected = 0;
            var hideFallbackProtected = 0;
            var failed = 0;

            app.ProtectedWindows.Clear();
            app.IsProtected = false;
            app.DesktopId = null;
            app.HiddenByStreamGuard = false;

            foreach (var window in windows)
            {
                if (!IsAppWindow(app, window.Handle, window.ProcessId)) { failed++; continue; }
                if (TryProtectWindow(window.Handle, app.Name, out var state, out var protectionKind))
                {
                    state.ProcessId = window.ProcessId != 0 ? window.ProcessId : GetProcessIdForWindow(window.Handle);
                    app.ProtectedWindows.Add(state);
                    protectedWindows++;
                    if (protectionKind == ProtectionKind.Wrapper)
                        wrappedProtected++;
                    else if (protectionKind == ProtectionKind.HiddenDesktop)
                        desktopProtected++;
                    else
                        hideFallbackProtected++;
                }
                else
                {
                    failed++;
                }
            }

            if (app.ProtectedWindows.Count == 0)
            {
                app.Status = failed > 0
                    ? $"Failed: {failed} window(s) could not be protected"
                    : "Failed: no protectable window found";
                Refresh(app);
                return false;
            }

            app.WindowHandle = app.ProtectedWindows[0].Handle;
            app.IsProtected = true;
            app.DesktopId = app.ProtectedWindows.FirstOrDefault(s => s.DesktopId.HasValue)?.DesktopId;
            app.HiddenByStreamGuard = app.ProtectedWindows.Any(s => s.HiddenByStreamGuard);
            app.Status = EnableWrapperProtection
                ? $"Protected: {app.ProtectedWindows.Count} window(s), wrap={wrappedProtected}, desktop={desktopProtected}, hide={hideFallbackProtected}"
                : $"Protected: {app.ProtectedWindows.Count} window(s), desktop={desktopProtected}, hide={hideFallbackProtected}";
            Refresh(app);
            return true;
        }

        private void AppList_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                foreach (var f in files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (ProcessSafetyService.IsProtectedIdentity(name, f)) continue;
                    if (_apps.Any(a => a.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    _apps.Add(new GuardedApp
                    {
                        Name = name,
                        ProcessName = name,
                        ExecutablePath = f,
                        IsEnabled = true,
                        SecureHotkey = GetNextDefaultHotkey(),
                        Status = "Queued: secure launch ready"
                    });
                }
                SaveConfig();
                RebuildAppHotkeys();
            }
        }

        // ── Protect / Stop ────────────────────────────────────────────────────
        private void BtnProtect_Click(object sender, RoutedEventArgs e) => StartProtecting();
        private void BtnStop_Click(object sender, RoutedEventArgs e)    => StopProtecting();
        private void BtnSecureDesktop_Click(object sender, RoutedEventArgs e) => StartSecureDesktop();

        private void StartSecureDesktop()
        {
            ProtectSelfFromCapture();
            ScrubSelectionSurface();
            UpdateStatus("Secure Desktop is experimental and disabled in this stable build. Use Start Protecting for the current protection flow.", false);
        }

        private void SetProtectionUiState(bool isRunning)
        {
            _isProtecting = isRunning;
            if (BtnProtectText != null)
                LocalizationService.BindText(BtnProtectText, System.Windows.Controls.TextBlock.TextProperty, isRunning ? "Shield Active" : "Start Protecting");

            if (BtnProtectIcon != null)
            {
                BtnProtectIcon.Data = isRunning
                    ? Geometry.Parse("M12 2L4 5V11C4 16.5 7.4 21.6 12 23C16.6 21.6 20 16.5 20 11V5L12 2Z")
                    : Geometry.Parse("M8 5v14l11-7z");
            }

            if (BtnProtect != null)
            {
                BtnProtect.Background = isRunning
                    ? (System.Windows.Media.Brush)FindResource("ShieldActiveGradient")
                    : (System.Windows.Media.Brush)FindResource("AccentGradient");
            }

            if (BtnStopText != null)
                LocalizationService.BindText(BtnStopText, System.Windows.Controls.TextBlock.TextProperty, "Stop");

            if (!isRunning) _captureGuardPaused = false;
            UpdateCaptureGuardState();
            UpdateStopButtonState();
        }

        private void UpdateStopButtonState()
        {
            if (BtnStop != null)
            {
                BtnStop.IsEnabled = _isProtecting ||
                                    HasActiveAppProtection() ||
                                    (_privacyCurtain?.IsVisible == true) ||
                                    (_capturePrivacyCurtain?.IsVisible == true) ||
                                    (_recovery != null && _recovery.Records.Any(record => record.CanRetry && !record.IsPreviousSession));
            }
        }

        private async void StartProtecting() => await StartProtectingAsync();

        private async System.Threading.Tasks.Task StartProtectingAsync()
        {
            if (_cleanupDone || _isStartingProtection || _pendingSecureOpens.Count > 0) return;
            _isStartingProtection = true;
            try { await StartProtectingCoreAsync(_protectionRevision); }
            catch (Exception error)
            {
                if (!_cleanupDone) { SetProtectionUiState(HasActiveAppProtection()); UpdateStatus($"Could not start protection: {error.Message}", false); }
            }
            finally { _isStartingProtection = false; }
        }

        private async System.Threading.Tasks.Task StartProtectingCoreAsync(int revision)
        {
            ProtectSelfFromCapture();

            if (AvailableWindowsListBox.SelectedItem is WindowInfo || ProcessCombo.SelectedIndex >= 0)
                AddSelectedProcessToGuardedApps();
            ScrubSelectionSurface();

            var enabledApps = _apps.Where(a => a.IsEnabled && IsManageableApp(a)).ToList();
            if (enabledApps.Count == 0)
            {
                _cursorShield.Deactivate();
                UpdateStatus("Choose a process first, then Start Protecting", false);
                return;
            }

            int protectedCount = 0;
            int wrappedProtected = 0;
            int desktopProtected = 0;
            int hideFallbackProtected = 0;
            int failed = 0;
            int protectedWindows = 0;
            foreach (var app in enabledApps)
            {
                if (IsAppCurrentlyGuarded(app))
                {
                    protectedCount++;
                    protectedWindows += app.ProtectedWindows.Count;
                    wrappedProtected += app.ProtectedWindows.Count(s => s.WrappedByStreamGuard);
                    desktopProtected += app.ProtectedWindows.Count(s => s.DesktopId.HasValue && !s.HiddenByStreamGuard);
                    hideFallbackProtected += app.ProtectedWindows.Count(s => s.HiddenByStreamGuard);
                    app.Status = app.ProtectedWindows.Count > 0
                        ? $"Protected: {app.ProtectedWindows.Count} window(s), already active"
                        : "Protected: already active";
                    Refresh(app);
                    continue;
                }

                app.ProtectedWindows.Clear();
                app.IsProtected = false;
                app.DesktopId = null;
                app.HiddenByStreamGuard = false;

                var windows = GetCandidateWindows(app);
                if (windows.Count == 0)
                    windows = await _launchAppWindows(app);

                if (_cleanupDone || revision != _protectionRevision) return;
                if (!IsCurrentProtectionRequest(revision, app)) continue;

                if (windows.Count == 0)
                {
                    app.WindowHandle = IntPtr.Zero;
                    app.Status = LocalizationService.GetString("Status.WaitingForAppWindow", "Standby: waiting for application window");
                    Refresh(app);
                    continue;
                }

                app.WindowHandle = windows[0].Handle;
                var appWrapProtected = 0;
                var appDesktopProtected = 0;
                var appHideProtected = 0;
                var appFailed = 0;

                foreach (var window in windows)
                {
                    if (!IsAppWindow(app, window.Handle, window.ProcessId)) { appFailed++; continue; }
                    if (TryProtectWindow(window.Handle, app.Name, out var state, out var protectionKind))
                    {
                        state.ProcessId = window.ProcessId != 0 ? window.ProcessId : GetProcessIdForWindow(window.Handle);
                        app.ProtectedWindows.Add(state);
                        protectedWindows++;
                        if (protectionKind == ProtectionKind.Wrapper)
                        {
                            wrappedProtected++;
                            appWrapProtected++;
                        }
                        else if (protectionKind == ProtectionKind.HiddenDesktop)
                        {
                            desktopProtected++;
                            appDesktopProtected++;
                        }
                        else
                        {
                            hideFallbackProtected++;
                            appHideProtected++;
                        }
                    }
                    else
                    {
                        appFailed++;
                    }
                }

                if (app.ProtectedWindows.Count > 0)
                {
                    app.IsProtected = true;
                    app.DesktopId = app.ProtectedWindows.FirstOrDefault(s => s.DesktopId.HasValue)?.DesktopId;
                    app.HiddenByStreamGuard = app.ProtectedWindows.Any(s => s.HiddenByStreamGuard);
                    app.Status = EnableWrapperProtection
                        ? $"Protected: {app.ProtectedWindows.Count} window(s), wrap={appWrapProtected}, desktop={appDesktopProtected}, hide={appHideProtected}"
                        : $"Protected: {app.ProtectedWindows.Count} window(s), desktop={appDesktopProtected}, hide={appHideProtected}";
                    protectedCount++;
                }
                else
                {
                    app.Status = appFailed > 0
                        ? $"Failed: {appFailed} window(s) could not be protected"
                        : "Failed: no protectable window found";
                    failed++;
                }

                Refresh(app);
            }

            if (protectedCount == 0)
            {
                if (_config.AutoStartShield && enabledApps.Count > 0 && failed == 0)
                {
                    SetProtectionUiState(true);
                    var standbyMsg = LocalizationService.GetString("Status.ShieldStandby", "Protection active: shield armed and monitoring guarded applications.");
                    UpdateStatus(standbyMsg, true);
                    return;
                }

                _cursorShield.Deactivate();
                SetProtectionUiState(false);
                var reason = failed > 0
                    ? $"Protection failed for {failed} app(s). Try a normal visible window."
                    : "No matching visible app window found";
                UpdateStatus(_recovery.Warning ?? reason, false);
                return;
            }

            SetProtectionUiState(true);
            UpdateCursorShieldForProtectionState();
            var protectionMessage = EnableWrapperProtection
                ? $"Protection active: {protectedCount} app(s), windows={protectedWindows}, wrap={wrappedProtected}, desktop={desktopProtected}, hide={hideFallbackProtected}, self=on"
                : $"Protection active: {protectedCount} app(s), windows={protectedWindows}, desktop={desktopProtected}, hide={hideFallbackProtected}, self=on";
            UpdateStatus(protectionMessage, true);
            UpdatePhysicalOutputGuard(forceStatus: true);
        }

        private void StopProtecting()
        {
            _protectionRevision++;
            _privacyCurtain.Hide();
            _capturePrivacyCurtain.Hide();
            if (!_isProtecting && !HasActiveAppProtection() && !_recovery.Records.Any(record => record.CanRetry && !record.IsPreviousSession))
            {
                _cursorShield.Deactivate();
                HidePhysicalOutputGuardCurtain();
                SetProtectionUiState(false);
                UpdateStatus("No app protection active. Self-protection and RAIA shield are still on.", false);
                return;
            }

            var restoreFailed = false;
            foreach (var app in _apps.Where(IsAppCurrentlyGuarded).ToList())
                if (!RestoreApp(app)) restoreFailed = true;

            foreach (var record in _recovery.Records.Where(record => record.CanRetry && !record.IsPreviousSession).ToList())
                if (!RestoreRecoveryRecord(record.Id).WindowReleased) restoreFailed = true;

            if (!HasActiveAppProtection() && _recovery.PendingCount == 0) _vdService.RemoveHiddenDesktop();
            SetProtectionUiState(false);
            _cursorShield.Deactivate();
            HidePhysicalOutputGuardCurtain();
            UpdateStatus(restoreFailed
                ? "App protection stopped. Some windows could not be verified and were left unchanged."
                : "App protection stopped: windows restored. Self-protection and RAIA shield stay on.", false);
        }

        private bool RestoreApp(GuardedApp app)
        {
            if (!IsManageableApp(app)) return false;
            var blocked = false;
            if (app.ProtectedWindows.Count > 0)
            {
                foreach (var state in app.ProtectedWindows.ToList())
                {
                    if (state.RecoveryId.HasValue)
                    {
                        var result = _recovery.Restore(state.RecoveryId.Value);
                        if (result.WindowReleased) { CloseRecoveredHost(state); app.ProtectedWindows.Remove(state); }
                        else blocked = true;
                        continue;
                    }
                    if (!IsWindow(state.Handle))
                    {
                        if (state.ProcessId > 0)
                            ProcessSafetyService.Default.InvalidateCache(state.ProcessId);
                        app.ProtectedWindows.Remove(state);
                        continue;
                    }
                    if (!ProcessSafetyService.Default.CanManageWindow(state.Handle, state.ProcessId))
                    {
                        blocked = true;
                        continue;
                    }
                    var restored = true;
                    if (state.ShieldedByStreamGuard || state.WrappedByStreamGuard)
                        restored = ProtectedWindowWrapperService.Restore(state);
                    else if (state.HiddenByStreamGuard)
                        restored = TryShowWindow(state.Handle);
                    else if (state.DesktopId.HasValue && state.DesktopId != Guid.Empty)
                        restored = _vdService.MoveToDesktop(state.Handle, state.DesktopId.Value);
                    if (restored) app.ProtectedWindows.Remove(state);
                    else blocked = true;
                }
            }
            else if (app.HiddenByStreamGuard)
            {
                blocked = IsWindow(app.WindowHandle) && !TryShowWindow(app.WindowHandle);
            }
            else if (app.DesktopId.HasValue && app.DesktopId != Guid.Empty)
            {
                blocked = IsWindow(app.WindowHandle) && !_vdService.MoveToDesktop(app.WindowHandle, app.DesktopId.Value);
            }

            if (blocked)
            {
                app.Status = "Some windows could not be restored. Recovery information has been retained.";
                Refresh(app);
                return false;
            }

            app.IsProtected = false;
            app.DesktopId   = null;
            app.HiddenByStreamGuard = false;
            app.Status = "Visible";
            Refresh(app);
            UpdateCursorShieldForProtectionState();
            return true;
        }

        private static bool IsAppCurrentlyGuarded(GuardedApp app)
        {
            return app.IsProtected ||
                   app.ProtectedWindows.Count > 0 ||
                   app.HiddenByStreamGuard ||
                   app.DesktopId.HasValue;
        }

        private bool HasActiveAppProtection()
        {
            return _apps.Any(IsAppCurrentlyGuarded);
        }

        private void UpdateCursorShieldForProtectionState()
        {
            var isGlobalCloakActive = _cursorCaptureProtection?.IsGlobalCloakActive == true;
            var shouldShield = (EnableCursorShieldCheckBox?.IsChecked ?? _config.EnableCursorShield)
                && HasActiveAppProtection()
                && !isGlobalCloakActive;
            if (shouldShield)
            {
                _cursorShield.Activate();
                UpdatePhysicalOutputGuard();
            }
            else
            {
                _cursorShield.Deactivate(restoreSystemCursors: !isGlobalCloakActive);
                HidePhysicalOutputGuardCurtain();
            }
            UpdateCursorShieldUiState(EnableCursorShieldCheckBox?.IsChecked ?? _config.EnableCursorShield);
            UpdateStopButtonState();
        }

        private void BlockVirtualDesktopHotkeysCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            var enabled = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            if (BlockVirtualDesktopHotkeysSettingsCheckBox != null)
                BlockVirtualDesktopHotkeysSettingsCheckBox.IsChecked = enabled;

            ApplyVirtualDesktopHotkeysPreference(enabled, saveConfig: true, updateBanner: true);
        }

        private void ApplyVirtualDesktopHotkeysPreference(bool enabled, bool saveConfig, bool updateBanner)
        {
            _hotkeys.BlockVirtualDesktopHotkeys = enabled;
            UpdateVirtualDesktopHotkeysUiState(enabled);
            UpdateDesktopAvailabilityUi();
            if (saveConfig)
                SaveConfig();
            if (updateBanner)
            {
                var message = enabled
                    ? LocalizationService.GetString("Message.VirtualDesktopHotkeysBlocked", "Virtual Desktop hotkeys locked: Win+Ctrl+D/F4/Arrows and Win+Tab are suppressed")
                    : LocalizationService.GetString("Message.VirtualDesktopHotkeysAllowed", "Virtual Desktop hotkeys unlocked: default Windows shortcuts active");
                UpdateStatus(message, enabled);
            }
        }

        private void UpdateVirtualDesktopHotkeysUiState(bool enabled)
        {
            var statusKey = enabled ? "VirtualDesktop.StatusOn" : "VirtualDesktop.StatusOff";
            var brush = enabled
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xd3, 0x99))
                : (SolidColorBrush)FindResource("TextDimBrush");

            if (VirtualDesktopHotkeysSettingsStatusText != null)
            {
                LocalizationService.BindKey(VirtualDesktopHotkeysSettingsStatusText, System.Windows.Controls.TextBlock.TextProperty, statusKey);
                VirtualDesktopHotkeysSettingsStatusText.Foreground = brush;
            }
        }

        private void UpdateDesktopAvailabilityUi()
        {
            if (DesktopAvailText == null) return;
            if (!_vdService.IsAvailable)
            {
                if (_hotkeys.BlockVirtualDesktopHotkeys)
                {
                    LocalizationService.BindKey(DesktopAvailText, System.Windows.Controls.TextBlock.TextProperty, "VirtualDesktop.HeaderOffLocked");
                    DesktopAvailText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xfb, 0xbf, 0x24));
                }
                else
                {
                    LocalizationService.BindKey(DesktopAvailText, System.Windows.Controls.TextBlock.TextProperty, "Message.001");
                    DesktopAvailText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xfb, 0xbf, 0x24));
                }
            }
            else if (_hotkeys.BlockVirtualDesktopHotkeys)
            {
                LocalizationService.BindKey(DesktopAvailText, System.Windows.Controls.TextBlock.TextProperty, "VirtualDesktop.HeaderLocked");
                DesktopAvailText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xbd, 0xf8));
            }
            else
            {
                LocalizationService.BindKey(DesktopAvailText, System.Windows.Controls.TextBlock.TextProperty, "Message.002");
                DesktopAvailText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xd3, 0x99));
            }
        }

        private void OnVirtualDesktopHotkeyBlocked(string shortcut)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var template = LocalizationService.GetString("VirtualDesktop.HotkeyBlockedToast", "Blocked Virtual Desktop shortcut '{0}' to protect secure workspace.");
                UpdateStatus(string.Format(template, shortcut), false);
            });
        }

        private void EnableCursorShieldCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            var enabled = EnableCursorShieldCheckBox.IsChecked == true;
            ApplyCursorShieldPreference(enabled, saveConfig: true, updateBanner: true);
        }

        private void ApplyCursorShieldPreference(bool enabled, bool saveConfig, bool updateBanner)
        {
            UpdateCursorShieldForProtectionState();
            if (saveConfig)
                SaveConfig();
            if (updateBanner)
            {
                var message = enabled
                    ? LocalizationService.GetString("Message.CursorShieldEnabled", "Cursor shield enabled: cursor shape will be masked while apps are protected")
                    : LocalizationService.GetString("Message.CursorShieldDisabled", "Cursor shield disabled: default system cursor behaviors restored");
                UpdateStatus(message, enabled);
            }
        }

        private void UpdateCursorShieldUiState(bool enabled)
        {
            if (CursorShieldStatusText == null) return;
            LocalizationService.BindKey(
                CursorShieldStatusText,
                System.Windows.Controls.TextBlock.TextProperty,
                enabled ? "CursorShield.StatusOn" : "CursorShield.StatusOff");
            CursorShieldStatusText.Foreground = enabled
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xd3, 0x99))
                : (SolidColorBrush)FindResource("TextDimBrush");
        }

        private void BtnRestoreCursors_Click(object sender, RoutedEventArgs e)
        {
            RestoreSystemCursorsManual();
        }

        public void RestoreSystemCursorsManual()
        {
            try { _cursorCaptureProtection.StopGlobalCursorCloak(); } catch { }
            _cursorShield.Deactivate();
            CursorShieldService.RestoreAllCursors();
            UpdateStatus(
                LocalizationService.GetString(
                    "Message.CursorRestored",
                    "Default Windows system cursors restored successfully"),
                true);
        }

        // ── Panic ─────────────────────────────────────────────────────────────
        private void UpdatePhysicalOutputGuard(bool forceStatus = false)
        {
            // Đã tắt tính năng tự động phủ màn đen lên màn hình HDMI phụ
            HidePhysicalOutputGuardCurtain();
            return;
        }

        private void HidePhysicalOutputGuardCurtain()
        {
            if (!_physicalOutputCurtainActive)
                return;

            try { _privacyCurtain.Hide(); } catch { }
            _physicalOutputCurtainActive = false;
        }

        private void BtnPanic_Click(object sender, RoutedEventArgs e) => PanicRestoreAll();

        private void BtnBlockRaia_Click(object sender, RoutedEventArgs e)
        {
            if (!_threatBlocker.IsActive)
                _threatBlocker.Start();
            else
                _threatBlocker.BlockNow();
        }

        private async void BlockHostsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            await ApplyWebsiteBlockPreferenceAsync(
                BlockRaiaHostCheckBox.IsChecked == true,
                BlockPortalHostCheckBox.IsChecked == true,
                saveConfig: true,
                showProgress: true);
        }

        private async void DevicePrivacyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            await ApplyDevicePrivacyPreferenceAsync(
                BlockCameraAccessCheckBox.IsChecked == true,
                BlockMicrophoneAccessCheckBox.IsChecked == true,
                saveConfig: true,
                showProgress: true);
        }

        private void PassiveFocusCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            var enabled = PassiveFocusCheckBox.IsChecked == true;
            _passiveFocusMode = enabled;
            ApplyStealthWindowStyle();
            SaveConfig();

            UpdateStatus(enabled
                ? "Passive Focus Mode enabled: Driver Booster clicks will not deactivate Chrome"
                : "Passive Focus Mode disabled: Standard window activation restored",
                enabled);
        }

        private void BlockSystemToolsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            var enabled = BlockSystemToolsCheckBox.IsChecked == true;
            ApplySystemToolsPreference(enabled, saveConfig: true, updateBanner: true);
        }

        private void ApplySystemToolsPreference(bool enabled, bool saveConfig, bool updateBanner)
        {
            var status = _systemToolsBlocker.SetEnabled(enabled);
            UpdateSystemToolsUiState(status.IsEnabled);
            if (saveConfig)
                SaveConfig();
            if (updateBanner)
                UpdateStatus(status.Message, status.IsEnabled);
        }

        private void UpdateSystemToolsUiState(bool enabled)
        {
            if (SystemToolsStatusText == null) return;
            LocalizationService.BindKey(
                SystemToolsStatusText,
                System.Windows.Controls.TextBlock.TextProperty,
                enabled ? "SystemTools.StatusOn" : "SystemTools.StatusOff");
            SystemToolsStatusText.Foreground = enabled
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xd3, 0x99))
                : (SolidColorBrush)FindResource("TextDimBrush");
        }

        private void BtnEmergencyUnlockSystemTools_Click(object sender, RoutedEventArgs e)
        {
            EmergencyUnlockSystemTools();
        }

        public void EmergencyUnlockSystemTools()
        {
            // 1. Force unlock Windows registry tools immediately on current thread
            SystemToolsBlockerService.ForceUnlockAllTools();

            // 2. Stop instance blocking immediately
            _systemToolsBlocker.SetEnabled(false);

            // 3. Ensure UI and config updates happen on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isLoadingConfig = true;
                    if (BlockSystemToolsCheckBox != null)
                    {
                        BlockSystemToolsCheckBox.IsChecked = false;
                    }
                    _isLoadingConfig = false;

                    UpdateSystemToolsUiState(false);
                    SaveConfig();

                    // 4. Ensure emergency restore .bat script exists
                    SystemToolsBlockerService.EnsureEmergencyRestoreScript();

                    // 5. Notify user
                    UpdateStatus(
                        LocalizationService.GetString(
                            "Message.SystemToolsEmergencyRestored",
                            "Emergency restore completed: Task Manager, Registry Editor, CMD, and Group Policy unlocked"),
                        true);
                }));
                return;
            }

            // If already on UI thread:
            _isLoadingConfig = true;
            if (BlockSystemToolsCheckBox != null)
            {
                BlockSystemToolsCheckBox.IsChecked = false;
            }
            _isLoadingConfig = false;

            UpdateSystemToolsUiState(false);
            SaveConfig();

            // 4. Ensure emergency restore .bat script exists
            SystemToolsBlockerService.EnsureEmergencyRestoreScript();

            // 5. Notify user
            UpdateStatus(
                LocalizationService.GetString(
                    "Message.SystemToolsEmergencyRestored",
                    "Emergency restore completed: Task Manager, Registry Editor, CMD, and Group Policy unlocked"),
                true);
        }

        private void BtnOpenEmergencyScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var scriptPath = SystemToolsBlockerService.EnsureEmergencyRestoreScript();
                if (System.IO.File.Exists(scriptPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{scriptPath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    UpdateStatus("Emergency restore script could not be located.", false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Could not open script location: {ex.Message}", false);
            }
        }

        private void MuteAntigravityNotificationsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            var muted = MuteAntigravityNotificationsCheckBox.IsChecked == true;
            ApplyAntigravityNotificationsPreference(muted, saveConfig: true, updateBanner: true);
        }

        private void ApplyAntigravityNotificationsPreference(bool muted, bool saveConfig, bool updateBanner)
        {
            var status = _antigravityNotifications.SetMuted(muted);
            UpdateAntigravityNotificationsUiState(status.IsMuted);
            if (saveConfig)
                SaveConfig();
            if (updateBanner)
                UpdateStatus(status.Message, status.IsMuted);
        }

        private void UpdateAntigravityNotificationsUiState(bool muted)
        {
            if (AntigravityNotificationsStatusText == null) return;
            LocalizationService.BindKey(
                AntigravityNotificationsStatusText,
                System.Windows.Controls.TextBlock.TextProperty,
                muted ? "AgyNotify.StatusOn" : "AgyNotify.StatusOff");
            AntigravityNotificationsStatusText.Foreground = muted
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xd3, 0x99))
                : (SolidColorBrush)FindResource("TextDimBrush");
        }

        private void UpdateAntigravityInstallerUiState()
        {
            if (AntigravityStatusBadgeText == null || AntigravityInstalledPanel == null || AntigravityNotInstalledPanel == null)
                return;

            var info = _antigravityInstaller.GetInstallationInfo();
            var installerPath = _antigravityInstaller.GetOfflineInstallerPath();

            if (info.IsInstalled)
            {
                AntigravityStatusBadgeText.Text = LocalizationService.GetString("AgyInstall.BadgeInstalled", "ĐÃ CÀI ĐẶT");
                AntigravityStatusBadgeBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x06, 0x4e, 0x3b));
                AntigravityStatusBadgeText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xd3, 0x99));

                AntigravityNotInstalledPanel.Visibility = Visibility.Collapsed;
                AntigravityInstalledPanel.Visibility = Visibility.Visible;

                if (AntigravityVersionInfoText != null)
                {
                    var template = LocalizationService.GetString("AgyInstall.VersionInfo", "Phiên bản: {0} • Vị trí: {1}");
                    var version = string.IsNullOrWhiteSpace(info.Version) ? "2.32.1" : info.Version;
                    var loc = string.IsNullOrWhiteSpace(info.ExePath) ? (info.InstallLocation ?? "Installed") : info.ExePath;
                    AntigravityVersionInfoText.Text = string.Format(template, version, loc);
                }

                if (BtnLaunchAntigravity != null)
                    BtnLaunchAntigravity.IsEnabled = !string.IsNullOrWhiteSpace(info.ExePath) && System.IO.File.Exists(info.ExePath.Trim('"', ' '));
                if (BtnAntigravitySecureLogin != null)
                    BtnAntigravitySecureLogin.IsEnabled = !string.IsNullOrWhiteSpace(info.ExePath) && System.IO.File.Exists(info.ExePath.Trim('"', ' '));
                if (BtnReinstallAntigravity != null)
                    BtnReinstallAntigravity.IsEnabled = !string.IsNullOrEmpty(installerPath);
            }
            else
            {
                AntigravityStatusBadgeText.Text = LocalizationService.GetString("AgyInstall.BadgeNotInstalled", "CHƯA CÀI ĐẶT");
                AntigravityStatusBadgeBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x45, 0x1a, 0x03));
                AntigravityStatusBadgeText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xfb, 0xbf, 0x24));

                AntigravityNotInstalledPanel.Visibility = Visibility.Visible;
                AntigravityInstalledPanel.Visibility = Visibility.Collapsed;

                var hasInstaller = !string.IsNullOrEmpty(installerPath) && System.IO.File.Exists(installerPath);
                if (AntigravityInstallerSourceText != null)
                {
                    if (hasInstaller)
                    {
                        var fileInfo = new System.IO.FileInfo(installerPath!);
                        var mb = fileInfo.Length / (1024.0 * 1024.0);
                        var template = LocalizationService.GetString("AgyInstall.SourceFound", "Phát hiện bộ cài Offline tích hợp sẵn: {0} ({1} MB)");
                        AntigravityInstallerSourceText.Text = string.Format(template, System.IO.Path.GetFileName(installerPath), mb.ToString("0.#"));
                    }
                    else
                    {
                        AntigravityInstallerSourceText.Text = LocalizationService.GetString("AgyInstall.SourceMissing", "Không tìm thấy bộ cài đặt offline trong thư mục installers.");
                    }
                }

                if (BtnInstallAntigravity != null)
                    BtnInstallAntigravity.IsEnabled = hasInstaller;
                if (BtnSilentInstallAntigravity != null)
                    BtnSilentInstallAntigravity.IsEnabled = hasInstaller;
            }
        }

        private async void BtnInstallAntigravity_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAntigravityInstallAsync(silent: false);
        }

        private async void BtnSilentInstallAntigravity_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAntigravityInstallAsync(silent: true);
        }

        private async Task ExecuteAntigravityInstallAsync(bool silent)
        {
            if (BtnInstallAntigravity != null) BtnInstallAntigravity.IsEnabled = false;
            if (BtnSilentInstallAntigravity != null) BtnSilentInstallAntigravity.IsEnabled = false;
            if (BtnReinstallAntigravity != null) BtnReinstallAntigravity.IsEnabled = false;

            if (AntigravityInstallProgressText != null)
            {
                AntigravityInstallProgressText.Text = LocalizationService.GetString(
                    silent ? "AgyInstall.InstallingSilent" : "AgyInstall.InstallingInteractive",
                    silent ? "Silent installation in progress. Please wait..." : "Launching interactive Antigravity installer...");
                AntigravityInstallProgressText.Visibility = Visibility.Visible;
            }

            try
            {
                var result = await _antigravityInstaller.InstallAsync(silent);
                var message = result.Success
                    ? LocalizationService.GetString("AgyInstall.InstallSuccess", result.Message)
                    : (result.Message.Contains("Offline installer not found", StringComparison.OrdinalIgnoreCase)
                        ? LocalizationService.GetString("AgyInstall.SourceMissing", result.Message)
                        : LocalizationService.GetString("AgyInstall.InstallFailed", result.Message));
                UpdateStatus(message, result.Success);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Antigravity setup error: {ex.Message}", false);
            }
            finally
            {
                if (AntigravityInstallProgressText != null)
                    AntigravityInstallProgressText.Visibility = Visibility.Collapsed;

                UpdateAntigravityInstallerUiState();
            }
        }

        private void BtnLaunchAntigravity_Click(object sender, RoutedEventArgs e)
        {
            var launched = _antigravityInstaller.LaunchAntigravity();
            if (launched)
            {
                UpdateStatus(LocalizationService.GetString("AgyInstall.LaunchSuccess", "Antigravity Desktop launched."), true);
            }
            else
            {
                UpdateStatus(LocalizationService.GetString("AgyInstall.LaunchFailed", "Failed to launch Antigravity Desktop."), false);
            }
        }

        private void BtnAntigravitySecureLogin_Click(object sender, RoutedEventArgs e)
        {
            StartAntigravitySecureLoginFlow();
        }

        private void StartAntigravitySecureLoginFlow()
        {
            var info = _antigravityInstaller.GetInstallationInfo();
            if (!info.IsInstalled)
            {
                UpdateStatus(LocalizationService.GetString("AgyBridge.NotInstalled", "Vui lòng cài đặt Antigravity Desktop trước khi đăng nhập."), false);
                return;
            }

            // 1. Check if clipboard already has a valid Google Auth URL
            string? clipText = null;
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                    clipText = System.Windows.Clipboard.GetText();
            }
            catch { }

            var capturedUrl = AntigravityAuthBridgeService.ExtractAuthUrl(clipText);
            if (!string.IsNullOrEmpty(capturedUrl))
            {
                ExecuteAntigravityBrowserNavigation(capturedUrl);
                return;
            }

            // 2. Launch Antigravity if not running
            if (!_antigravityInstaller.IsAntigravityRunning())
            {
                _antigravityInstaller.LaunchAntigravity();
            }

            // 3. Start listening with bridge
            _antigravityAuthBridge.StartListening(url =>
            {
                Dispatcher.Invoke(() => ExecuteAntigravityBrowserNavigation(url));
            }, TimeSpan.FromSeconds(90));

            UpdateStatus(LocalizationService.GetString("AgyBridge.Listening", "Đang chờ liên kết đăng nhập từ Antigravity... (Hãy bấm 'Sign in' trên Antigravity hoặc sao chép link)"), true);
        }

        private void ExecuteAntigravityBrowserNavigation(string url)
        {
            if (TabBrowser != null)
            {
                TabBrowser.IsChecked = true;
            }

            NavigateBrowser(url);
            UpdateStatus(LocalizationService.GetString("AgyBridge.Captured", "Đã bắt liên kết đăng nhập Antigravity! Đang tải trong Trình duyệt bảo vệ..."), true);
        }

        private void EnableAiTrafficShieldCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;

            var enabled = EnableAiTrafficShieldCheckBox.IsChecked == true;
            ApplyAiTrafficShieldPreference(enabled, saveConfig: true, updateBanner: true);
        }

        private void ApplyAiTrafficShieldPreference(bool enabled, bool saveConfig, bool updateBanner)
        {
            UpdateAiTrafficShieldUiState(enabled);
            if (saveConfig)
                SaveConfig();

            if (enabled)
            {
                var proxy = _aiTrafficProtection.CheckProxyStatus();
                if (proxy.IsRogueProxy)
                {
                    var purged = _aiTrafficProtection.PurgeProxySettings();
                    UpdateAiTrafficShieldUiState(true);
                    if (purged)
                    {
                        if (updateBanner)
                            UpdateStatus(LocalizationService.GetString("AiTraffic.AutoPurgedBanner", "Rogue proxy intercepted and purged automatically to protect AI traffic!"), true);
                        return;
                    }
                }
            }

            if (updateBanner)
            {
                var msg = enabled
                    ? LocalizationService.GetString("AiTraffic.ToggleEnabledBanner", "AI Traffic Shield activated: Auto-purging loopback proxy and monitoring sniffers")
                    : LocalizationService.GetString("AiTraffic.ToggleDisabledBanner", "AI Traffic Shield paused: Auto-purge disabled");
                UpdateStatus(msg, enabled);
            }
        }

        private void UpdateAiTrafficShieldUiState(bool enabled, CaptureAssessment? assessment = null, AiTrafficAssessment? aiAssessment = null)
        {
            if (AiTrafficProxyStatusText == null) return;

            var proxy = aiAssessment?.Proxy ?? _aiTrafficProtection.CheckProxyStatus();
            var hosts = aiAssessment?.Hosts ?? _aiTrafficProtection.CheckAiHostsProtection();
            if (proxy.IsRogueProxy)
            {
                if (AiTrafficProxyDot != null)
                    AiTrafficProxyDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
                var proxyDetail = !string.IsNullOrWhiteSpace(proxy.ProxyServer) ? proxy.ProxyServer : (proxy.AutoConfigUrl ?? "127.0.0.1");
                LocalizationService.BindText(AiTrafficProxyStatusText, System.Windows.Controls.TextBlock.TextProperty,
                    string.Format(LocalizationService.GetString("AiTraffic.ProxyStatusRogue", "Proxy: Hijacked! Rogue loopback proxy detected ({0})"), proxyDetail));
                AiTrafficProxyStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            }
            else if (hosts.IsTampered)
            {
                if (AiTrafficProxyDot != null)
                    AiTrafficProxyDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
                LocalizationService.BindKey(AiTrafficProxyStatusText, System.Windows.Controls.TextBlock.TextProperty, "AiTraffic.HostsStatusTampered");
                AiTrafficProxyStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            }
            else
            {
                if (AiTrafficProxyDot != null)
                    AiTrafficProxyDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                LocalizationService.BindKey(AiTrafficProxyStatusText, System.Windows.Controls.TextBlock.TextProperty, "AiTraffic.ProxyStatusSafe");
                AiTrafficProxyStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            }

            var certs = aiAssessment?.RogueCertificates ?? _aiTrafficProtection.ScanRogueCertificates();
            if (certs.Count > 0)
            {
                if (AiTrafficCertDot != null)
                    AiTrafficCertDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf5, 0x9e, 0x0b));
                LocalizationService.BindText(AiTrafficCertStatusText, System.Windows.Controls.TextBlock.TextProperty,
                    string.Format(LocalizationService.GetString("AiTraffic.CertStatusWarning", "Root CA Warning: {0} suspicious MITM certificate(s) detected!"), certs.Count));
                AiTrafficCertStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf5, 0x9e, 0x0b));
            }
            else
            {
                if (AiTrafficCertDot != null)
                    AiTrafficCertDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                LocalizationService.BindKey(AiTrafficCertStatusText, System.Windows.Controls.TextBlock.TextProperty, "AiTraffic.CertStatusSafe");
                AiTrafficCertStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            }

            var sniffers = aiAssessment?.ActiveSniffers ?? (assessment?.Tools ?? _captureAssessment?.Tools ?? Array.Empty<CaptureTool>()).Where(t => t.Kind == CaptureToolKind.Sniffer).ToList();
            if (sniffers.Count > 0)
            {
                if (AiTrafficSnifferDot != null)
                    AiTrafficSnifferDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
                var snifferNames = string.Join(", ", sniffers.Select(s => s.Name));
                LocalizationService.BindText(AiTrafficSnifferStatusText, System.Windows.Controls.TextBlock.TextProperty,
                    string.Format(LocalizationService.GetString("AiTraffic.SnifferStatusDetected", "Sniffers: Active packet sniffer detected: {0}"), snifferNames));
                AiTrafficSnifferStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            }
            else
            {
                if (AiTrafficSnifferDot != null)
                    AiTrafficSnifferDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                LocalizationService.BindKey(AiTrafficSnifferStatusText, System.Windows.Controls.TextBlock.TextProperty, "AiTraffic.SnifferStatusSafe");
                AiTrafficSnifferStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            }
        }

        private void BtnPurgeProxy_Click(object sender, RoutedEventArgs e)
        {
            var purged = _aiTrafficProtection.PurgeProxySettings();
            var hostsAssessment = _aiTrafficProtection.CheckAiHostsProtection();
            if (hostsAssessment.IsTampered)
            {
                _aiTrafficProtection.PurgeAiHostsHijacks();
            }
            UpdateAiTrafficShieldUiState(EnableAiTrafficShieldCheckBox?.IsChecked ?? _config.EnableAiTrafficShield);
            if (purged)
            {
                UpdateStatus(LocalizationService.GetString("AiTraffic.PurgeSuccessBanner", "Proxy settings purged successfully. Direct connection restored."), true);
            }
            else
            {
                UpdateStatus(LocalizationService.GetString("AiTraffic.PurgeFailedBanner", "Could not purge proxy settings from registry."), false);
            }
        }

        private async void BtnScanAiTraffic_Click(object sender, RoutedEventArgs e)
        {
            BtnScanAiTraffic.IsEnabled = false;
            try
            {
                var result = await Task.Run(() => _aiTrafficProtection.ScanAll(_captureAssessment));
                UpdateAiTrafficShieldUiState(EnableAiTrafficShieldCheckBox?.IsChecked ?? _config.EnableAiTrafficShield, null, result);
                if (result.IsAllSafe)
                {
                    UpdateStatus(LocalizationService.GetString("AiTraffic.ScanCleanBanner", "AI Traffic Scan completed: All checks normal (No proxy, clean certificates, no sniffers)."), true);
                }
                else
                {
                    var issues = new List<string>();
                    if (result.Proxy.IsRogueProxy) issues.Add("Rogue Proxy");
                    if (result.RogueCertificates.Count > 0) issues.Add($"{result.RogueCertificates.Count} MITM Root Cert(s)");
                    if (result.ActiveSniffers.Count > 0) issues.Add(string.Join(", ", result.ActiveSniffers.Select(s => s.Name)));
                    if (result.Hosts.IsTampered) issues.Add("AI Hosts hijacked");
                    var summary = string.Join(", ", issues);
                    UpdateStatus(string.Format(LocalizationService.GetString("AiTraffic.ScanWarningBanner", "AI Traffic Scan warning: {0}"), summary), false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"AI Traffic Scan error: {ex.Message}", false);
            }
            finally
            {
                BtnScanAiTraffic.IsEnabled = true;
            }
        }

        private void TriggerSnippingTool()
        {
            var saveDir = TxtScreenshotDir?.Text;
            if (string.IsNullOrWhiteSpace(saveDir))
            {
                saveDir = !string.IsNullOrWhiteSpace(_config.ScreenshotDirectory)
                    ? _config.ScreenshotDirectory
                    : SnippingToolService.DefaultScreenshotDirectory;
            }

            bool autoCopy = ScreenshotClipboardCheckbox?.IsChecked ?? _config.ScreenshotAutoCopyClipboard;
            bool excludeCapture = ScreenshotExcludeCheckbox?.IsChecked ?? _config.ScreenshotExcludeFromCapture;

            _snippingTool.StartSnipping(saveDir, autoCopy, excludeCapture);
        }

        private void BtnStartSnipping_Click(object sender, RoutedEventArgs e)
        {
            TriggerSnippingTool();
        }

        private void BtnBrowseScreenshotDir_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationService.GetString("Snipping.Browse", "Chọn thư mục lưu ảnh chụp màn hình"),
                UseDescriptionForTitle = true,
                SelectedPath = !string.IsNullOrWhiteSpace(TxtScreenshotDir.Text) && System.IO.Directory.Exists(TxtScreenshotDir.Text)
                    ? TxtScreenshotDir.Text
                    : SnippingToolService.DefaultScreenshotDirectory
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtScreenshotDir.Text = dialog.SelectedPath;
                SaveConfig();
            }
        }

        private void BtnOpenScreenshotDir_Click(object sender, RoutedEventArgs e)
        {
            var dir = string.IsNullOrWhiteSpace(TxtScreenshotDir.Text)
                ? SnippingToolService.DefaultScreenshotDirectory
                : TxtScreenshotDir.Text.Trim();

            try
            {
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UpdateStatus(string.Format(LocalizationService.GetString("Snipping.InvalidDir", "Đường dẫn thư mục không hợp lệ: {0}"), ex.Message), false);
            }
        }

        private void TxtScreenshotDir_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;
            SaveConfig();
        }

        private void TxtScreenshotDir_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (!_isLoadingConfig)
                    SaveConfig();
                // Move focus away to clear editing caret
                BtnStartSnipping.Focus();
                e.Handled = true;
            }
        }

        private void ScreenshotPreference_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;
            SaveConfig();
        }

        private void OnSnippingCompleted(SnippingResult result)
        {
            Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    var format = LocalizationService.GetString("Snipping.Success", "Đã lưu ảnh chụp vào {0} ({1}×{2} px)");
                    var msg = string.Format(format, result.FilePath, result.Width, result.Height);
                    UpdateStatus(msg, true);
                }
                else
                {
                    var msg = LocalizationService.GetString("Snipping.Canceled", result.Message);
                    UpdateStatus(msg, false);
                }
            });
        }

        private void RegisterSnippingHotkey()
        {
            if (_hotkeys == null) return;
            if (_snippingHotkeyId >= 0)
            {
                _hotkeys.Unregister(_snippingHotkeyId);
                _snippingHotkeyId = -1;
            }

            var hotkey = string.IsNullOrWhiteSpace(_snippingHotkey) ? "Ctrl+Alt+S" : _snippingHotkey;
            if (!TryParseHotkey(hotkey, out var modifiers, out var vk, out var normalized, out _))
            {
                hotkey = "Ctrl+Alt+S";
                TryParseHotkey(hotkey, out modifiers, out vk, out normalized, out _);
            }

            _snippingHotkey = normalized;
            _currentSnippingHotkey = normalized;
            _snippingHotkeyId = _hotkeys.Register(modifiers, vk, TriggerSnippingTool);
            if (_snippingHotkeyId < 0 && _hotkeysReady)
            {
                UpdateStatus(string.Format(LocalizationService.GetString("Snipping.HotkeyRegisterFailed",
                    "Không thể đăng ký phím tắt {0} (có thể bị ứng dụng khác chiếm giữ)"), normalized), false);
            }
        }

        private static string FormatHotkeyDisplay(string hotkey)
        {
            if (string.IsNullOrWhiteSpace(hotkey)) return "Ctrl + Alt + S";
            var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(" + ", parts);
        }

        private void CommitSnippingHotkey(string hotkeyText)
        {
            if (!TryParseHotkey(hotkeyText, out _, out _, out var normalized, out var error))
            {
                UpdateStatus(string.Format(LocalizationService.GetString("Audit.InvalidHotkey", "Phím tắt không hợp lệ: {0}"), error), false);
                SyncSnippingHotkeyUi();
                return;
            }

            _snippingHotkey = normalized;
            _currentSnippingHotkey = normalized;
            RegisterSnippingHotkey();
            SaveConfig();
            RebuildAppHotkeys();
            SyncSnippingHotkeyUi();
            UpdateStatus(string.Format(LocalizationService.GetString("Snipping.HotkeyAssigned",
                "Đã gán phím tắt chụp màn hình: {0}"), normalized), false);
        }

        private void SyncSnippingHotkeyUi()
        {
            if (TxtSnippingHotkey != null) TxtSnippingHotkey.Text = _snippingHotkey;
            if (TxtSnippingHotkeyEdit != null) TxtSnippingHotkeyEdit.Text = _snippingHotkey;
            if (TxtSnippingHotkeySettings != null) TxtSnippingHotkeySettings.Text = FormatHotkeyDisplay(_snippingHotkey);

            var actionTake = LocalizationService.GetString("Snipping.ActionTakeScreenshot",
                LocalizationService.Current.Language == "vi" ? "Chụp màn hình" : "Take Screenshot");
            var actionQuick = LocalizationService.GetString("Snipping.ActionQuickSnip",
                LocalizationService.Current.Language == "vi" ? "Chụp màn hình bảo vệ" : "Protected Screen Snip");

            var startText = _snippingHotkey == "Ctrl+Alt+S"
                ? LocalizationService.GetString("Snipping.StartButton", LocalizationService.Current.Language == "vi" ? "Chụp màn hình (Ctrl+Alt+S)" : "Take Screenshot (Ctrl+Alt+S)")
                : $"{actionTake} ({_snippingHotkey})";

            var quickTooltip = _snippingHotkey == "Ctrl+Alt+S"
                ? LocalizationService.GetString("Snipping.QuickSnipTooltip", LocalizationService.Current.Language == "vi" ? "Chụp màn hình bảo vệ (Ctrl+Alt+S)" : "Protected Screen Snip (Ctrl+Alt+S)")
                : $"{actionQuick} ({_snippingHotkey})";

            if (BtnStartSnipping != null)
            {
                BtnStartSnipping.Content = startText;
                BtnStartSnipping.ToolTip = quickTooltip;
            }
            if (BtnQuickSnip != null)
            {
                BtnQuickSnip.ToolTip = quickTooltip;
            }
            RefreshTrayLanguage();
        }

        private void SnippingHotkey_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                _isRecordingSnippingHotkey = true;
                _recordingOriginalSnippingHotkey = _snippingHotkey;

                ResetSnippingBadgeVisual(tb, isRecording: true);
                tb.Text = LocalizationService.Current.Translate("Nhấn phím...");
                UpdateStatus(LocalizationService.GetString("Snipping.HotkeyRecording",
                    "Đang ghi nhận phím tắt chụp màn hình: Bấm tổ hợp phím (ví dụ Ctrl+Alt+S), Esc để hủy"), true);
            }
        }

        private void SnippingHotkey_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb && !tb.IsKeyboardFocusWithin)
            {
                tb.Focus();
                e.Handled = true;
            }
        }

        private void SnippingHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb)
                return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                CancelSnippingHotkeyRecording(tb);
                UpdateStatus(LocalizationService.GetString("Snipping.HotkeyCanceled",
                    LocalizationService.Current.Language == "vi" ? "Đã hủy đổi phím tắt chụp màn hình" : "Snipping hotkey recording canceled"), false);
                e.Handled = true;
                return;
            }

            if (key is Key.Delete or Key.Back)
            {
                FinishSnippingHotkeyRecording(tb);
                CommitSnippingHotkey("Ctrl+Alt+S");
                e.Handled = true;
                return;
            }

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ||
                        Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ||
                       Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ||
                         Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0 ||
                       Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                var modParts = new List<string>();
                if (ctrl) modParts.Add("Ctrl");
                if (win) modParts.Add("Win");
                if (alt) modParts.Add("Alt");
                if (shift) modParts.Add("Shift");

                tb.Text = modParts.Count > 0 ? string.Join("+", modParts) + "+..." : LocalizationService.Current.Translate("Nhấn phím...");
                e.Handled = true;
                return;
            }

            if (TryConvertWpfKeyToKeyName(key, out var keyName))
            {
                if (!ctrl && !alt && !shift && !win)
                {
                    UpdateStatus(LocalizationService.Current.Translate("Phím tắt cần ít nhất một phím bổ trợ: Ctrl, Alt hoặc Shift (ví dụ: Ctrl+Alt+1)"), false);
                    e.Handled = true;
                    return;
                }

                var parts = new List<string>();
                if (ctrl) parts.Add("Ctrl");
                if (win) parts.Add("Win");
                if (alt) parts.Add("Alt");
                if (shift) parts.Add("Shift");
                parts.Add(keyName);

                var hotkeyString = string.Join("+", parts);

                var chord = hotkeyString.Replace(" ", "");
                if (chord.Equals("Shift+S+G", StringComparison.OrdinalIgnoreCase) ||
                    chord.Equals("Ctrl+Shift+S+G", StringComparison.OrdinalIgnoreCase) ||
                    (ctrl && shift && !alt && !win && keyName.Equals("G", StringComparison.OrdinalIgnoreCase)) ||
                    (ctrl && shift && alt && !win && keyName is "A" or "C" or "H" or "X"))
                {
                    UpdateStatus(LocalizationService.GetString("UI.ReservedHotkey",
                        "Tổ hợp phím này đã dành riêng cho chức năng hệ thống Driver Booster. Hãy chọn phím khác"), false);
                    e.Handled = true;
                    return;
                }

                var duplicateApp = _apps.FirstOrDefault(a => !a.IsHotkeyDisabled && IsManageableApp(a) &&
                    TryParseHotkey(a.SecureHotkey, out _, out _, out var existing, out _) &&
                    existing.Equals(hotkeyString, StringComparison.OrdinalIgnoreCase));
                if (duplicateApp != null)
                {
                    UpdateStatus(string.Format(LocalizationService.GetString("Snipping.HotkeyConflict",
                        "Phím tắt chụp màn hình bị trùng với ứng dụng bảo vệ {0}"), duplicateApp.ProcessName), false);
                    e.Handled = true;
                    return;
                }

                FinishSnippingHotkeyRecording(tb);
                CommitSnippingHotkey(hotkeyString);
                e.Handled = true;
                return;
            }

            e.Handled = true;
        }

        private void SnippingHotkey_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb && _isRecordingSnippingHotkey)
            {
                if (tb.Text == LocalizationService.Current.Translate("Nhấn phím...") || tb.Text.EndsWith("+..."))
                {
                    tb.Text = _recordingOriginalSnippingHotkey;
                }
                FinishSnippingHotkeyRecording(tb);
            }
        }

        private void SnippingHotkeyDisplay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TxtSnippingHotkeyEdit != null)
            {
                TxtSnippingHotkeyEdit.Focus();
            }
            else if (TxtSnippingHotkey != null)
            {
                TxtSnippingHotkey.Focus();
            }
        }

        private void CancelSnippingHotkeyRecording(System.Windows.Controls.TextBox tb)
        {
            tb.Text = _recordingOriginalSnippingHotkey;
            FinishSnippingHotkeyRecording(tb);
        }

        private void FinishSnippingHotkeyRecording(System.Windows.Controls.TextBox tb)
        {
            _isRecordingSnippingHotkey = false;
            _recordingOriginalSnippingHotkey = "";

            ResetSnippingBadgeVisual(tb, isRecording: false);
            Keyboard.ClearFocus();
        }

        private static void ResetSnippingBadgeVisual(System.Windows.Controls.TextBox tb, bool isRecording)
        {
            if (tb.Parent is System.Windows.Controls.StackPanel sp && sp.Parent is System.Windows.Controls.Border badge)
            {
                badge.BorderBrush = isRecording
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x81, 0x8c, 0xf8))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x31, 0x2e, 0x81));
            }
        }

        private void BtnQuickChromeFocusLock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var script = BrowserScriptService.GetAntiBlurScript();
                System.Windows.Clipboard.SetText(script);
                ReassertTopmostIfPassive();
                UpdateStatus(LocalizationService.GetString("Browser.FocusLockCopied",
                    "Đã sao chép mã Anti-Blur! Mở Chrome F12 -> Console -> Dán (Ctrl+V) rồi Enter để khóa Focus."), true);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Không thể sao chép vào Clipboard: {ex.Message}", false);
            }
        }

        private void BtnCopyAntiBlurScript_Click(object sender, RoutedEventArgs e) =>
            BtnQuickChromeFocusLock_Click(sender, e);

        private void BtnCopyBookmarklet_Click(object sender, RoutedEventArgs e) =>
            CopyBrowserScript(BrowserScriptService.GetAntiBlurBookmarklet(), "Clipboard.CopiedBookmarklet");

        private void BtnCopyPasteScript_Click(object sender, RoutedEventArgs e) =>
            CopyBrowserScript(BrowserScriptService.GetCopyPasteScript(), "Clipboard.CopiedConsole");

        private void BtnCopyPasteBookmarklet_Click(object sender, RoutedEventArgs e) =>
            CopyBrowserScript(BrowserScriptService.GetCopyPasteBookmarklet(), "Clipboard.CopiedBookmarklet");

        private void BtnDisableCopyPasteScript_Click(object sender, RoutedEventArgs e) =>
            CopyBrowserScript(BrowserScriptService.GetDisableCopyPasteScript(), "Clipboard.CopiedDisable");

        private void CopyBrowserScript(string script, string messageKey)
        {
            try
            {
                System.Windows.Clipboard.SetText(script);
                UpdateStatus("", true);
                LocalizationService.BindKey(StatusText, System.Windows.Controls.TextBlock.TextProperty, messageKey);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Không thể sao chép vào Clipboard: {ex.Message}", false);
            }
        }

        private async System.Threading.Tasks.Task ApplyWebsiteBlockPreferenceAsync(
            bool blockRaiaWebsite,
            bool blockPortalWebsite,
            bool saveConfig,
            bool showProgress)
        {
            if (saveConfig)
                SaveConfig();

            BlockRaiaHostCheckBox.IsEnabled = false;
            BlockPortalHostCheckBox.IsEnabled = false;
            var enabled = blockRaiaWebsite || blockPortalWebsite;
            if (showProgress)
            {
                LocalizationService.BindText(ThreatDetailText, System.Windows.Controls.TextBlock.TextProperty, enabled
                    ? "Applying website block..."
                    : "Removing website block...");
            }

            var status = await RunPreferenceUpdateAsync(() =>
                _threatBlocker.SetWebsiteBlockPreference(blockRaiaWebsite, blockPortalWebsite), BlockRaiaHostCheckBox, BlockPortalHostCheckBox);
            if (status == null) return;

            BlockRaiaHostCheckBox.IsEnabled = true;
            BlockPortalHostCheckBox.IsEnabled = true;
            var action = enabled ? "enabled" : "disabled";
            var ok = !status.Message.Contains("failed", StringComparison.OrdinalIgnoreCase);
            UpdateStatus($"Website block {action}: {_threatBlocker.KnownBlockedWebsites}", enabled && ok);
            LocalizationService.BindText(ThreatDetailText, System.Windows.Controls.TextBlock.TextProperty, status.Message);
        }

        private async System.Threading.Tasks.Task ApplyDevicePrivacyPreferenceAsync(
            bool blockCameraAccess,
            bool blockMicrophoneAccess,
            bool saveConfig,
            bool showProgress)
        {
            if (saveConfig)
                SaveConfig();

            BlockCameraAccessCheckBox.IsEnabled = false;
            BlockMicrophoneAccessCheckBox.IsEnabled = false;
            if (showProgress)
                LocalizationService.BindText(ThreatDetailText, System.Windows.Controls.TextBlock.TextProperty, "Applying camera and microphone preferences...");

            var status = await RunPreferenceUpdateAsync(() =>
                _threatBlocker.SetDevicePrivacyPreference(blockCameraAccess, blockMicrophoneAccess), BlockCameraAccessCheckBox, BlockMicrophoneAccessCheckBox);
            if (status == null) return;

            BlockCameraAccessCheckBox.IsEnabled = true;
            BlockMicrophoneAccessCheckBox.IsEnabled = true;
            var ok = !status.Message.Contains("failed", StringComparison.OrdinalIgnoreCase);
            UpdateStatus(
                $"Camera {(blockCameraAccess ? "blocked" : "allowed")}, microphone {(blockMicrophoneAccess ? "blocked" : "allowed")}",
                ok && (blockCameraAccess || blockMicrophoneAccess));
            LocalizationService.BindText(ThreatDetailText, System.Windows.Controls.TextBlock.TextProperty, status.Message);
        }

        private async System.Threading.Tasks.Task<ThreatBlockerStatus?> RunPreferenceUpdateAsync(
            Func<ThreatBlockerStatus> apply, params System.Windows.Controls.Control[] controls)
        {
            if (_cleanupDone) return null;
            foreach (var control in controls) control.IsEnabled = false;
            try
            {
                var result = await System.Threading.Tasks.Task.Run(() => _cleanupDone ? null : apply());
                return _cleanupDone ? null : result;
            }
            catch (Exception error)
            {
                if (!_cleanupDone) UpdateStatus($"Could not apply preferences: {error.Message}", false);
                return null;
            }
            finally { if (!_cleanupDone) foreach (var control in controls) control.IsEnabled = true; }
        }

        private bool IsAnyPrivacyCurtainVisible() =>
            _privacyCurtain?.IsVisible == true || _capturePrivacyCurtain?.IsVisible == true;

        private void DismissAllPrivacyCurtains()
        {
            Dispatcher.Invoke(() =>
            {
                if (_capturePrivacyCurtain?.IsVisible == true)
                    _captureGuardPaused = true;
                _capturePrivacyCurtain?.Hide();
                _privacyCurtain?.Hide();
                UpdateCaptureGuardState();
                UpdateStopButtonState();
            });
        }

        private void TogglePrivacyCurtain()
        {
            Dispatcher.Invoke(() =>
            {
                if (_capturePrivacyCurtain.IsVisible)
                {
                    _captureGuardPaused = true;
                    _capturePrivacyCurtain.Hide();
                    _privacyCurtain.Hide();
                    UpdateCaptureGuardState();
                }
                else if (_privacyCurtain.IsVisible) _privacyCurtain.Hide();
                else if (_captureCurtainHotkeyReady)
                {
                    _privacyCurtain.Show();
                    _cursorCaptureProtection.ReassertOverlayTopmost();
                }
                else
                {
                    UpdateStatus("", false);
                    LocalizationService.BindKey(StatusText, System.Windows.Controls.TextBlock.TextProperty, "Capture.RecoveryUnavailable");
                }
                UpdateStopButtonState();
            });
        }

        public void PanicRestoreAll()
        {
            Dispatcher.Invoke(() =>
            {
                StopProtecting();
                try { _cursorCaptureProtection.StopGlobalCursorCloak(); } catch { }
                try { CursorShieldService.RestoreAllCursors(); } catch { }
                if (BlockSystemToolsCheckBox?.IsChecked == true || _systemToolsBlocker.IsEnabled)
                {
                    _isLoadingConfig = true;
                    if (BlockSystemToolsCheckBox != null)
                        BlockSystemToolsCheckBox.IsChecked = false;
                    _isLoadingConfig = false;
                    ApplySystemToolsPreference(false, saveConfig: true, updateBanner: false);
                }
                SystemToolsBlockerService.ForceUnlockAllTools();
                SystemToolsBlockerService.EnsureEmergencyRestoreScript();
                foreach (var record in _recovery.Records.Where(record => record.CanRetry).ToList()) RestoreRecoveryRecord(record.Id);
                var allRestored = !HasActiveAppProtection() && _recovery.PendingCount == 0;
                var message = allRestored ? "⚡ PANIC: All applications restored to main screen!"
                    : "Some windows could not be restored. Recovery information has been retained.";
                UpdateStatus(message, false);
                if (StatusBanner != null)
                {
                    StatusBanner.BorderBrush = (SolidColorBrush)FindResource("DangerBrush");
                }
                if (StatusText != null)
                {
                    StatusText.Foreground = (SolidColorBrush)FindResource("DangerBrush");
                    LocalizationService.BindText(StatusText, System.Windows.Controls.TextBlock.TextProperty, message);
                }
                if (StatusDot != null)
                {
                    StatusDot.Fill = (SolidColorBrush)FindResource("DangerBrush");
                }

            });
        }

        // ── Toggle window ─────────────────────────────────────────────────────
        public void ToggleVisibility()
        {
            Dispatcher.Invoke(() =>
            {
                if (IsVisible && WindowState != WindowState.Minimized) { HideToTray(); }
                else { ShowFromBackground(); }
            });
        }

        public void ToggleDriverBoosterFromChord()
        {
            Dispatcher.Invoke(() =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var isForeground = hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;
                if (IsVisible && WindowState != WindowState.Minimized && (IsActive || isForeground || _passiveFocusMode))
                {
                    // Lần thứ hai (khi đang mở): ẩn ứng dụng Driver Booster; tránh Minimized khi bật PassiveFocusMode để không kích hoạt SW_RESTORE làm rớt Fullscreen Chrome
                    if (!_passiveFocusMode)
                        WindowState = WindowState.Minimized;
                    HideToTray();
                }
                else
                {
                    // Lần thứ nhất (khi đang ẩn, thu nhỏ hoặc ở background): mở nhanh lại ứng dụng Driver Booster
                    ShowFromBackground();
                }
            });
        }

        // ── UI helpers ────────────────────────────────────────────────────────
        public void ShowFromBackground()
        {
            Dispatcher.Invoke(() =>
            {
                var prevFg = GetForegroundWindow();
                var hwnd = new WindowInteropHelper(this).Handle;
                GetWindowThreadProcessId(prevFg, out uint fgPid);
                if (prevFg != IntPtr.Zero && fgPid != (uint)Environment.ProcessId)
                {
                    _previousForegroundHwnd = prevFg;
                }

                if (_passiveFocusMode)
                {
                    ShowActivated = false;
                    Topmost = true;
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                        SetWindowPos(
                            hwnd,
                            HWND_TOPMOST,
                            0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }
                    if (Visibility != Visibility.Visible)
                    {
                        Visibility = Visibility.Visible;
                    }
                    if (WindowState == WindowState.Minimized)
                    {
                        WindowState = WindowState.Normal;
                    }
                    ReassertTopmostIfPassive();
                    if (prevFg != IntPtr.Zero && prevFg != hwnd && IsWindow(prevFg))
                    {
                        try { SetForegroundWindow(prevFg); } catch { }
                    }
                }
                else
                {
                    ShowActivated = true;
                    Show();
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                    Topmost = true;
                    Topmost = false;
                    Focus();
                    if (hwnd != IntPtr.Zero)
                        SetForegroundWindow(hwnd);
                }

                ApplyStealthWindowStyle();
                ProtectSelfFromCapture();
                if (_captionOverlayHiddenWithMain && _captionOverlay != null)
                {
                    _captionOverlayHiddenWithMain = false;
                    _captionOverlay.Show();
                    _captionOverlay.ApplySettings();
                    UpdateCaptionCaptureStatus();
                }
                RestoreCaptureGuardActivePolling();
                _threatBlocker.RestoreActivePolling();
                _ = RefreshAvailableWindowsAsync();
                AnimateRestoreSmooth();
            });
        }

        private void HideToTray()
        {
            CloseDangerConfirmModal();
            if (_captionOverlay is { IsVisible: true })
            {
                _captionOverlayHiddenWithMain = true;
                _captionOverlay.Hide();
            }
            if (BrowserControl != null && _isBrowserInitialized)
            {
                BrowserControl.Visibility = Visibility.Collapsed;
            }
            SetCaptureGuardMinimized(true);
            _threatBlocker.SetWindowMinimized(true);
            Hide();
            ProtectSelfFromCapture();

            if (_previousForegroundHwnd != IntPtr.Zero && IsWindow(_previousForegroundHwnd))
            {
                try { SetForegroundWindow(_previousForegroundHwnd); } catch { }
            }
        }

        private void RequestExit()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _exitRequested = true;
                CleanupAndExit();
                System.Windows.Application.Current.Shutdown();
            });
        }

        private void KillStreamGuardAndManagedApps()
        {
            _forceKillOnExit = true;
            RequestExit();
        }

        private void ProtectSelfFromCapture()
        {
            UpdateVerifiedCaptureProtection();
            ReinforceCaptionOverlayProtection();
        }

        private void ApplyModernDwmAttributes()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                int backdrop = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            catch
            {
            }
        }

        private void ApplyStealthWindowStyle()
        {
            try
            {
                ShowInTaskbar = false;
                ShowActivated = !_passiveFocusMode;
                Topmost = _passiveFocusMode;
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~WS_EX_APPWINDOW;
                if (_passiveFocusMode)
                    exStyle |= WS_EX_NOACTIVATE;
                else
                    exStyle &= ~WS_EX_NOACTIVATE;
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
                SetWindowPos(
                    hwnd,
                    _passiveFocusMode ? HWND_TOPMOST : HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch
            {
            }
        }

        public void ReassertTopmostIfPassive()
        {
            if (!_passiveFocusMode || !IsVisible || WindowState == WindowState.Minimized)
                return;
            try
            {
                bool fullScreenOverlayActive =
                    _snippingTool.IsSnippingActive ||
                    _privacyCurtain?.IsVisible == true ||
                    _capturePrivacyCurtain?.IsVisible == true;

                if (!fullScreenOverlayActive)
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        SetWindowPos(
                            hwnd,
                            HWND_TOPMOST,
                            0,
                            0,
                            0,
                            0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                    if (_captionOverlay is { IsVisible: true })
                    {
                        _captionOverlay.ReassertTopmost();
                    }
                }
                _cursorCaptureProtection.ReassertOverlayTopmost();
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProcHook);
            }
            catch { }
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_passiveFocusMode && msg == WM_MOUSEACTIVATE)
            {
                if (IsMouseOverToastOverlay() || IsMouseOverSidebarNavigation())
                {
                    handled = true;
                    return (IntPtr)MA_NOACTIVATE;
                }

                if (TabBrowser?.IsChecked == true || IsMouseOverTextEntryControl())
                {
                    handled = true;
                    return (IntPtr)MA_ACTIVATE;
                }

                handled = true;
                return (IntPtr)MA_NOACTIVATE;
            }
            return IntPtr.Zero;
        }

        private bool IsMouseOverSidebarNavigation()
        {
            try
            {
                if (SidebarCol == null || SidebarCol.ActualWidth <= 0)
                    return false;

                var pt = Mouse.GetPosition(this);
                return pt.X >= 0 && pt.X <= SidebarCol.ActualWidth &&
                       pt.Y >= 0 && pt.Y <= ActualHeight;
            }
            catch
            {
                return false;
            }
        }

        private bool IsMouseOverToastOverlay()
        {
            try
            {
                if (ToastOverlayHost == null || ToastOverlayHost.Visibility != Visibility.Visible)
                    return false;

                if (_toastService == null || _toastService.ActiveCards.Count == 0)
                    return false;

                if (ToastCardsStack != null && ToastCardsStack.IsMouseOver)
                    return true;

                if (ToastCardsStack != null && ToastCardsStack.ActualWidth > 0 && ToastCardsStack.ActualHeight > 0)
                {
                    var pt = Mouse.GetPosition(ToastCardsStack);
                    return pt.X >= 0 && pt.X <= ToastCardsStack.ActualWidth &&
                           pt.Y >= 0 && pt.Y <= ToastCardsStack.ActualHeight;
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private bool IsMouseOverTextEntryControl()
        {
            try
            {
                if (IsMouseOverToastOverlay()) return false;
                if (TabBrowser?.IsChecked == true) return true;

                var over = Mouse.DirectlyOver as DependencyObject;
                if (over != null && IsTextEntryVisual(over))
                    return true;

                var pt = Mouse.GetPosition(this);
                HitTestResult result = VisualTreeHelper.HitTest(this, pt);
                if (result?.VisualHit != null && IsTextEntryVisual(result.VisualHit))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsTextEntryVisual(DependencyObject? visual)
        {
            while (visual != null)
            {
                if (visual is System.Windows.Controls.Primitives.TextBoxBase tb)
                {
                    if (!tb.IsReadOnly)
                        return true;
                }
                else if (visual is System.Windows.Controls.PasswordBox ||
                    visual is System.Windows.Controls.ComboBox || visual is System.Windows.Controls.ComboBoxItem ||
                    visual is System.Windows.Controls.Primitives.Thumb { Name: "SidebarResizeThumb" } ||
                    visual is Microsoft.Web.WebView2.Wpf.WebView2 || visual is System.Windows.Interop.HwndHost)
                {
                    return true;
                }
                visual = VisualTreeHelper.GetParent(visual);
            }
            return false;
        }

        private static nint GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : GetWindowLong32(hWnd, nIndex);
        }

        private static nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, value)
                : SetWindowLong32(hWnd, nIndex, unchecked((int)value));
        }

        private static bool IsOwnProcess(string processName)
        {
            return processName.Equals(
                System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManageableApp(GuardedApp app) =>
            !ProcessSafetyService.IsProtectedIdentity(app.ProcessName, app.ExecutablePath) && !IsOwnProcess(app.ProcessName);

        private bool TryProtectWindow(
            IntPtr hWnd,
            string title,
            out ProtectedWindowState state,
            out ProtectionKind protectionKind)
        {
            state = new ProtectedWindowState { Handle = hWnd };
            protectionKind = ProtectionKind.HideFallback;
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !WindowEnumerator.IsValidCandidateWindow(hWnd)) return false;
            if (!_recovery.TryPrepare(hWnd, title, out var recovery) || recovery == null)
            {
                UpdateStatus(_recovery.Warning ?? "Recovery data could not be prepared.", false);
                TabRecovery.IsChecked = true;
                return false;
            }
            var protectedWindow = TryProtectWindowCore(hWnd, title, out state, out protectionKind);
            state.RecoveryId = recovery.Id;
            if (protectedWindow)
                _recovery.MarkProtected(recovery.Id, protectionKind switch
                {
                    ProtectionKind.Wrapper => RecoveryMethod.WindowHost,
                    ProtectionKind.HiddenDesktop => RecoveryMethod.VirtualDesktop,
                    _ => RecoveryMethod.HiddenWindow
                });
            else _recovery.Restore(recovery.Id);
            return protectedWindow;
        }

        private bool TryProtectWindowCore(IntPtr hWnd, string title, out ProtectedWindowState state, out ProtectionKind protectionKind)
        {
            state = new ProtectedWindowState { Handle = hWnd };
            protectionKind = ProtectionKind.HideFallback;

            if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !WindowEnumerator.IsValidCandidateWindow(hWnd))
                return false;

            // 1. Ưu tiên di chuyển sang Desktop 2 (Virtual Desktop) — tương thích 100% với ứng dụng GPU (Chromium, Discord, Games)
            if (_vdService.IsAvailable && _vdService.MoveToHidden(hWnd, out var origId))
            {
                var onCurrentDesktop = _vdService.IsWindowOnCurrentDesktop(hWnd);
                if (onCurrentDesktop == false)
                {
                    state.DesktopId = origId;
                    state.HiddenByStreamGuard = false;
                    protectionKind = ProtectionKind.HiddenDesktop;
                    return true;
                }

                if (origId != Guid.Empty)
                    _vdService.MoveToDesktop(hWnd, origId);
            }

            // 2. Phương án dự phòng: Bọc trong wrapper Form có WDA_EXCLUDEFROMCAPTURE
            if (EnableWrapperProtection && ProtectedWindowWrapperService.TryWrap(hWnd, title, out state))
            {
                protectionKind = ProtectionKind.Wrapper;
                return true;
            }

            // 3. Phương án dự phòng cuối cùng: Ẩn Win32 (SW_HIDE)
            if (TryHideWindow(hWnd))
            {
                state.HiddenByStreamGuard = true;
                protectionKind = ProtectionKind.HideFallback;
                return true;
            }

            return false;
        }

        private void ReconcileProtectedWindows()
        {
            if (!_isProtecting)
                return;

            try
            {
                var enabledApps = _apps.Where(a => a.IsEnabled && IsManageableApp(a)).ToList();
                foreach (var app in enabledApps)
                {
                    // Dọn dẹp các handle đã chết hoặc cửa sổ đã bị đóng
                    bool hasDead = false;
                    foreach (var s in app.ProtectedWindows)
                    {
                        if (s.Handle == IntPtr.Zero || !IsWindow(s.Handle)) { hasDead = true; break; }
                    }
                    if (hasDead)
                    {
                        var deadStates = app.ProtectedWindows.Where(s => s.Handle == IntPtr.Zero || !IsWindow(s.Handle)).ToList();
                        foreach (var dead in deadStates)
                        {
                            if (dead.ProcessId > 0)
                                ProcessSafetyService.Default.InvalidateCache(dead.ProcessId);
                            try { dead.WrapperForm?.Close(); } catch { }
                            if (dead.RecoveryId.HasValue && _recovery != null)
                            {
                                try { _recovery.Restore(dead.RecoveryId.Value); } catch { }
                            }
                        }
                        var removed = app.ProtectedWindows.RemoveAll(s => s.Handle == IntPtr.Zero || !IsWindow(s.Handle));
                        if (removed > 0)
                        {
                            if (app.ProtectedWindows.Count == 0)
                            {
                                app.IsProtected = false;
                                app.HiddenByStreamGuard = false;
                                app.DesktopId = null;
                                app.Status = "Closed: window exited";
                            }
                            else
                            {
                                app.Status = $"Protected: {app.ProtectedWindows.Count} window(s)";
                            }
                            Refresh(app);
                            if (!HasActiveAppProtection() && !_config.AutoStartShield)
                                SetProtectionUiState(false);
                            UpdateCursorShieldForProtectionState();
                        }
                    }

                    var currentHwnds = new HashSet<IntPtr>(app.ProtectedWindows.Select(s => s.Handle));
                    var newWindows = GetCandidateWindows(app)
                        .Where(w => w.Handle != IntPtr.Zero && !currentHwnds.Contains(w.Handle) && WindowEnumerator.IsValidCandidateWindow(w.Handle))
                        .ToList();

                    if (newWindows.Count > 0)
                    {
                        var newlyAdded = 0;
                        foreach (var win in newWindows)
                        {
                            if (TryProtectWindow(win.Handle, app.Name, out var state, out _))
                            {
                                state.ProcessId = win.ProcessId != 0 ? win.ProcessId : GetProcessIdForWindow(win.Handle);
                                app.ProtectedWindows.Add(state);
                                newlyAdded++;
                            }
                        }
                        if (newlyAdded > 0)
                        {
                            app.IsProtected = true;
                            if (app.WindowHandle == IntPtr.Zero || !IsWindow(app.WindowHandle))
                                app.WindowHandle = app.ProtectedWindows[0].Handle;
                            app.DesktopId = app.ProtectedWindows.FirstOrDefault(s => s.DesktopId.HasValue)?.DesktopId;
                            app.HiddenByStreamGuard = app.ProtectedWindows.Any(s => s.HiddenByStreamGuard);
                            app.Status = $"Protected: {app.ProtectedWindows.Count} window(s)";
                            Refresh(app);
                            SetProtectionUiState(true);
                            UpdateCursorShieldForProtectionState();
                            var statusTpl = LocalizationService.GetString("Status.ShieldStandbyReconciled", "Protection active: automatically shielded {0} window(s) for {1}.");
                            UpdateStatus(string.Format(statusTpl, newlyAdded, app.ProcessName), true);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void ScrubSelectionSurface()
        {
            try
            {
                _suppressProcessSelection = true;
                ProcessCombo.IsDropDownOpen = false;
                ProcessCombo.SelectedIndex = -1;
                ProcessCombo.SelectedItem = null;
                ProcessCombo.Text = "";
                ProcessCombo.ItemsSource = null;
                ProcessCombo.Tag = null;
                ProcessCombo.Visibility = Visibility.Collapsed;
                LocalizationService.BindText(BtnAddApp, System.Windows.Controls.ContentControl.ContentProperty, "Add Selected");
            }
            catch
            {
            }
            finally
            {
                _suppressProcessSelection = false;
            }
        }

        private static bool TryHideWindow(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !ProcessSafetyService.Default.CanManageWindow(hWnd))
                    return false;

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    ShowWindow(hWnd, SW_HIDE);
                    ShowWindowAsync(hWnd, SW_HIDE);
                    SetWindowPos(
                        hWnd,
                        IntPtr.Zero,
                        0,
                        0,
                        0,
                        0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);

                    System.Threading.Thread.Sleep(50);
                    if (!IsWindowVisible(hWnd))
                        return true;
                }

                return !IsWindowVisible(hWnd);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsGpuCompositedWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
            try
            {
                var sb = new StringBuilder(128);
                if (GetClassName(hWnd, sb, sb.Capacity) <= 0) return false;
                var cls = sb.ToString();
                return IsGpuCompositedClassName(cls);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsGpuCompositedClassName(string? className) =>
            !string.IsNullOrEmpty(className) &&
            (className.StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase) ||
             className.Equals("MozillaWindowClass", StringComparison.OrdinalIgnoreCase) ||
             className.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase));

        private static bool TryShowWindow(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !ProcessSafetyService.Default.CanManageWindow(hWnd))
                    return false;

                ShowWindow(hWnd, SW_SHOW);
                ShowWindowAsync(hWnd, SW_SHOW);
                SetWindowPos(
                    hWnd,
                    IntPtr.Zero,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
                RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero, RDW_RESTORE_REPAINT);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnWindowPreviewMouseLeftButtonDownForSwitchCooldown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TryGuardSwitchCooldown(e.OriginalSource as DependencyObject))
            {
                e.Handled = true;
            }
        }

        private void OnWindowPreviewKeyDownForSwitchCooldown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Space)
                return;

            var target = System.Windows.Input.Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject;
            if (TryGuardSwitchCooldown(target))
            {
                e.Handled = true;
            }
        }

        private bool TryGuardSwitchCooldown(DependencyObject? source)
        {
            var featureSwitch = SwitchCooldownService.FindFeatureSwitch(source);
            if (featureSwitch == null)
                return false;

            if (_switchCooldown.TryAcquireToggle(featureSwitch, out var remainingSeconds))
                return false;

            var template = LocalizationService.GetString(
                "Switch.CooldownWarning",
                "Thao tác quá nhanh! Vui lòng đợi {0:0.0} giây trước khi bật/tắt lại chức năng này.");
            var message = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, remainingSeconds);
            ShowToast(message, ToastType.Warning, 2200);
            return true;
        }

        private void UpdateStatus(string msg, bool active)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateStatus(msg, active)));
                return;
            }

            LocalizationService.BindText(StatusText, System.Windows.Controls.TextBlock.TextProperty, msg);
            var color = active
                ? System.Windows.Media.Color.FromRgb(0x10, 0xb9, 0x81)
                : System.Windows.Media.Color.FromRgb(0x94, 0xa3, 0xb8);
            StatusText.Foreground = new SolidColorBrush(color);
            StatusDot.Fill        = new SolidColorBrush(color);

            if (StatusPulse != null)
            {
                StatusPulse.Fill = new SolidColorBrush(color);
                StatusPulse.Opacity = active ? 0.35 : 0.15;
            }
            if (StatusBanner != null)
            {
                StatusBanner.BorderBrush = active
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xaa, 0x10, 0xb9, 0x81))
                    : (SolidColorBrush)FindResource("BorderBrush2");
            }

            if (!string.IsNullOrWhiteSpace(msg))
            {
                var translated = LocalizationService.Current.Translate(msg);
                var displayMsg = string.IsNullOrWhiteSpace(translated) ? msg : translated;
                var toastType = active ? ToastType.Success : ClassifyErrorOrInfo(msg, displayMsg);
                ShowToast(displayMsg, toastType);
            }
        }

        internal static ToastType ClassifyErrorOrInfo(string raw, string translated)
        {
            var lower = (raw + " " + translated).ToLowerInvariant();
            if (lower.Contains("lỗi") || lower.Contains("error") || lower.Contains("fail") ||
                lower.Contains("không thể") || lower.Contains("conflict") || lower.Contains("trùng") ||
                lower.Contains("invalid") || lower.Contains("cannot") || lower.Contains("unable") ||
                lower.Contains("reserved"))
            {
                return ToastType.Error;
            }

            // Exclude "phím tắt" before checking "tắt" (which indicates disabled / turned off)
            var normalized = lower.Replace("phím tắt", "hotkey").Replace("phim tat", "hotkey");
            if (normalized.Contains("cảnh báo") || normalized.Contains("warning") || normalized.Contains("warn") ||
                normalized.Contains("tắt") || normalized.Contains("disabled") || normalized.Contains("stopped") ||
                normalized.Contains("paused"))
            {
                return ToastType.Warning;
            }
            return ToastType.Info;
        }

        public void ShowToast(string message, ToastType type = ToastType.Info, int durationMs = 2000)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowToast(message, type, durationMs)));
                return;
            }

            _toastService?.Show(message, type, durationMs);
        }

        private void UpdateEmptyState()
        {
            if (EmptyStateBorder != null)
                EmptyStateBorder.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void AnimateViewFadeIn(FrameworkElement view)
        {
            view.BeginAnimation(OpacityProperty, null);
            view.Opacity = 0.0;
            view.Visibility = Visibility.Visible;

            var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                view.BeginAnimation(OpacityProperty, null);
                view.Opacity = 1.0;
            };
            view.BeginAnimation(OpacityProperty, anim);
        }

        private void SwitchToTab(FrameworkElement activeView)
        {
            var allViews = new[] { ViewProtected, ViewAvailable, ViewThreats, ViewSettings, ViewRecovery, ViewCaptions, ViewBrowser, ViewDiagnostics };
            foreach (var view in allViews)
            {
                if (view == null) continue;
                if (view == activeView)
                {
                    if (view == ViewBrowser)
                    {
                        // Avoid Opacity animation on HwndHost (WebView2 Airspace glitch)
                        view.BeginAnimation(OpacityProperty, null);
                        view.Opacity = 1.0;
                        view.Visibility = Visibility.Visible;
                        if (BrowserControl != null && _isBrowserInitialized)
                            BrowserControl.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        AnimateViewFadeIn(view);
                    }
                }
                else
                {
                    view.BeginAnimation(OpacityProperty, null);
                    if (view == ViewBrowser && BrowserControl != null)
                    {
                        BrowserControl.Visibility = Visibility.Collapsed;
                    }
                    view.Visibility = Visibility.Collapsed;
                }
            }
            ApplyStealthWindowStyle();
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewProtected == null || ViewAvailable == null || ViewThreats == null) return;

            if (TabProtected?.IsChecked == true)
            {
                SwitchToTab(ViewProtected);
            }
            else if (TabAvailable?.IsChecked == true)
            {
                SwitchToTab(ViewAvailable);
                _ = RefreshAvailableWindowsAsync(force: false);
            }
            else if (TabThreats?.IsChecked == true)
            {
                SwitchToTab(ViewThreats);
            }
            else if (TabSettings?.IsChecked == true && ViewSettings != null)
            {
                SwitchToTab(ViewSettings);
            }
            else if (TabRecovery?.IsChecked == true && ViewRecovery != null)
            {
                SwitchToTab(ViewRecovery);
                if (_recovery != null) _recovery.Refresh();
            }
            else if (TabCaptions?.IsChecked == true && ViewCaptions != null)
            {
                SwitchToTab(ViewCaptions);
            }
            else if (TabBrowser?.IsChecked == true && ViewBrowser != null)
            {
                SwitchToTab(ViewBrowser);
                _ = EnsureBrowserInitializedAsync();
                Activate();
            }
            else if (TabDiagnostics?.IsChecked == true && ViewDiagnostics != null)
            {
                SwitchToTab(ViewDiagnostics);
                _ = LoadHardwareDiagnosticsAsync(force: false);
            }
        }

        private void BtnAddAppFromDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (TabAvailable != null)
                TabAvailable.IsChecked = true;
        }

        private void BtnAddSingleWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is WindowInfo win)
            {
                var wasAlreadyInShield = _apps.Any(a => a.ProcessName.Equals(win.ProcessName, StringComparison.OrdinalIgnoreCase));
                var app = AddWindowToGuardedApps(win);
                if (app != null)
                {
                    ScrubSelectionSurface();
                    SyncAvailableWindowsShieldState();
                    UpdateStatus(
                        wasAlreadyInShield
                            ? $"Updated {app.Name} in shield list"
                            : $"Added {app.Name} to shield list",
                        false);
                }
            }
        }

        private void SyncAvailableWindowsShieldState()
        {
            var shieldedProcesses = _apps
                .Select(a => a.ProcessName)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var window in _availableWindows)
                window.IsInShieldList = shieldedProcesses.Contains(window.ProcessName);
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var query = SearchBox?.Text ?? "";
            if (BtnClearSearch != null)
                BtnClearSearch.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

            if (SearchPlaceholderText != null)
                SearchPlaceholderText.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

            ApplyAvailableWindowsFilter();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null)
                SearchBox.Text = "";
        }

        private void ApplyAvailableWindowsFilter()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableWindowsListBox?.ItemsSource);
            if (view == null) return;

            string query = SearchBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(query))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = item =>
                {
                    if (item is WindowInfo win)
                    {
                        return win.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               win.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               win.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                };
            }

            UpdateAvailableEmptyState();
        }

        private void UpdateAvailableEmptyState()
        {
            if (AvailableEmptyState == null || AvailableWindowsListBox == null) return;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableWindowsListBox.ItemsSource);
            if (view == null) return;

            int count = 0;
            foreach (var _ in view)
            {
                count++;
                if (count > 0) break;
            }

            if (count == 0)
            {
                bool hasQuery = !string.IsNullOrWhiteSpace(SearchBox?.Text);
                if (AvailableEmptyStateTitle != null)
                    LocalizationService.BindText(AvailableEmptyStateTitle, System.Windows.Controls.TextBlock.TextProperty, hasQuery ? "Không tìm thấy ứng dụng phù hợp" : "Chưa có cửa sổ ứng dụng nào");
                if (AvailableEmptyStateSubtitle != null)
                    LocalizationService.BindText(AvailableEmptyStateSubtitle, System.Windows.Controls.TextBlock.TextProperty, hasQuery
                        ? "Thử tìm kiếm với từ khóa khác hoặc bấm Xóa tìm kiếm."
                        : "Mở ứng dụng bạn muốn bảo vệ rồi bấm Refresh để quét lại.");

                AvailableEmptyState.Visibility = Visibility.Visible;
            }
            else
            {
                AvailableEmptyState.Visibility = Visibility.Collapsed;
            }
        }

        private void Refresh(GuardedApp app)
        {
            int idx = _apps.IndexOf(app);
            if (idx < 0) return;
            _apps.RemoveAt(idx);
            _apps.Insert(idx, app);
        }

        private void OnThreatBlockerStatusChanged(ThreatBlockerStatus status)
        {
            if (Dispatcher.HasShutdownStarted) return;

            Dispatcher.BeginInvoke(() =>
            {
                var containedNow = status.LastContained > 0;
                LocalizationService.BindText(ThreatStatusText, System.Windows.Controls.TextBlock.TextProperty, containedNow ? "RAIA contained" : "RAIA containment active");
                LocalizationService.BindText(ThreatDetailText, System.Windows.Controls.TextBlock.TextProperty, $"{status.Message}. Total contained: {status.TotalContained}");

                var bannerColor = containedNow
                    ? System.Windows.Media.Color.FromRgb(0x7f, 0x1d, 0x1d)
                    : System.Windows.Media.Color.FromRgb(0x16, 0x3b, 0x35);
                ThreatBanner.Background = new SolidColorBrush(bannerColor);
            });
        }

        // ── Smooth Window Transitions & Animations ─────────────────────────────
        private void ToggleMaximizeRestoreSmooth()
        {
            if (_isAnimatingTransition) return;

            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var workArea = screen.WorkingArea;

            var source = PresentationSource.FromVisual(this);
            var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            var targetLeft = workArea.Left / dpiX;
            var targetTop = workArea.Top / dpiY;
            var targetWidth = workArea.Width / dpiX;
            var targetHeight = workArea.Height / dpiY;

            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (!_isCustomMaximized)
            {
                _normalRestoreBounds = new Rect(Left, Top, Width, Height);
                _isCustomMaximized = true;
                _isAnimatingTransition = true;
                UpdateMaximizeButtonState();
                ApplySidebarLayout(targetWidth);
                ApplyResponsiveScale(targetWidth, targetHeight);

                var animLeft = new DoubleAnimation(Left, targetLeft, duration) { EasingFunction = ease };
                var animTop = new DoubleAnimation(Top, targetTop, duration) { EasingFunction = ease };
                var animWidth = new DoubleAnimation(Width, targetWidth, duration) { EasingFunction = ease };
                var animHeight = new DoubleAnimation(Height, targetHeight, duration) { EasingFunction = ease };

                animHeight.Completed += (_, _) =>
                {
                    BeginAnimation(LeftProperty, null);
                    BeginAnimation(TopProperty, null);
                    BeginAnimation(WidthProperty, null);
                    BeginAnimation(HeightProperty, null);

                    Left = targetLeft;
                    Top = targetTop;
                    Width = targetWidth;
                    Height = targetHeight;

                    _isCustomMaximized = true;
                    _isAnimatingTransition = false;
                    UpdateMaximizeButtonState();
                    ApplySidebarLayout(targetWidth);
                    ApplyResponsiveScale(targetWidth, targetHeight);
                };

                BeginAnimation(LeftProperty, animLeft);
                BeginAnimation(TopProperty, animTop);
                BeginAnimation(WidthProperty, animWidth);
                BeginAnimation(HeightProperty, animHeight);
            }
            else
            {
                _isCustomMaximized = false;
                _isAnimatingTransition = true;

                var destLeft = _normalRestoreBounds.Left;
                var destTop = _normalRestoreBounds.Top;
                var destWidth = _normalRestoreBounds.Width;
                var destHeight = _normalRestoreBounds.Height;

                UpdateMaximizeButtonState();
                ApplySidebarLayout(destWidth);
                ApplyResponsiveScale(destWidth, destHeight);

                if (destLeft < targetLeft || destLeft + destWidth > targetLeft + targetWidth)
                    destLeft = Math.Max(targetLeft, targetLeft + (targetWidth - destWidth) / 2);
                if (destTop < targetTop || destTop + destHeight > targetTop + targetHeight)
                    destTop = Math.Max(targetTop, targetTop + (targetHeight - destHeight) / 2);

                var animLeft = new DoubleAnimation(Left, destLeft, duration) { EasingFunction = ease };
                var animTop = new DoubleAnimation(Top, destTop, duration) { EasingFunction = ease };
                var animWidth = new DoubleAnimation(Width, destWidth, duration) { EasingFunction = ease };
                var animHeight = new DoubleAnimation(Height, destHeight, duration) { EasingFunction = ease };

                animHeight.Completed += (_, _) =>
                {
                    BeginAnimation(LeftProperty, null);
                    BeginAnimation(TopProperty, null);
                    BeginAnimation(WidthProperty, null);
                    BeginAnimation(HeightProperty, null);

                    Left = destLeft;
                    Top = destTop;
                    Width = destWidth;
                    Height = destHeight;

                    _isCustomMaximized = false;
                    _isAnimatingTransition = false;
                    UpdateMaximizeButtonState();
                    ApplySidebarLayout(destWidth);
                    ApplyResponsiveScale(destWidth, destHeight);
                };

                BeginAnimation(LeftProperty, animLeft);
                BeginAnimation(TopProperty, animTop);
                BeginAnimation(WidthProperty, animWidth);
                BeginAnimation(HeightProperty, animHeight);
            }
        }

        private void UpdateMaximizeButtonState()
        {
            if (BtnMaximize == null || IconMaximizePath == null) return;

            if (_isCustomMaximized)
            {
                LocalizationService.BindText(BtnMaximize, FrameworkElement.ToolTipProperty, "Restore Down");
                IconMaximizePath.Data = Geometry.Parse("M4 8v12h12V8H4zm2 2h8v8H6v-8zm2-6h12v12h-2V6H8V4z");
            }
            else
            {
                LocalizationService.BindText(BtnMaximize, FrameworkElement.ToolTipProperty, "Maximize");
                IconMaximizePath.Data = Geometry.Parse("M4 4h16v16H4V4zm2 2v12h12V6H6z");
            }
        }

        private void AnimateMinimizeSmooth()
        {
            if (BrowserControl != null && _isBrowserInitialized)
                BrowserControl.Visibility = Visibility.Collapsed;

            if (_isAnimatingTransition || WindowScaleTransform == null || WindowTranslateTransform == null || RootGrid == null)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            _isAnimatingTransition = true;
            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };

            var animScaleX = new DoubleAnimation(1.0, 0.88, duration) { EasingFunction = ease };
            var animScaleY = new DoubleAnimation(1.0, 0.88, duration) { EasingFunction = ease };
            var animTransY = new DoubleAnimation(0.0, 35.0, duration) { EasingFunction = ease };
            var animOpacity = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

            animOpacity.Completed += (_, _) =>
            {
                _isAnimatingTransition = false;
                WindowState = WindowState.Minimized;

                WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                WindowTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                RootGrid.BeginAnimation(OpacityProperty, null);

                WindowScaleTransform.ScaleX = 1.0;
                WindowScaleTransform.ScaleY = 1.0;
                WindowTranslateTransform.Y = 0;
                RootGrid.Opacity = 1.0;
            };

            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animScaleX);
            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animScaleY);
            WindowTranslateTransform.BeginAnimation(TranslateTransform.YProperty, animTransY);
            RootGrid.BeginAnimation(OpacityProperty, animOpacity);
        }

        public void AnimateRestoreSmooth()
        {
            if (WindowScaleTransform == null || WindowTranslateTransform == null || RootGrid == null) return;

            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            WindowTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            RootGrid.BeginAnimation(OpacityProperty, null);

            WindowScaleTransform.ScaleX = 0.93;
            WindowScaleTransform.ScaleY = 0.93;
            WindowTranslateTransform.Y = 25.0;
            RootGrid.Opacity = 0.0;

            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var animScaleX = new DoubleAnimation(0.93, 1.0, duration) { EasingFunction = ease };
            var animScaleY = new DoubleAnimation(0.93, 1.0, duration) { EasingFunction = ease };
            var animTransY = new DoubleAnimation(25.0, 0.0, duration) { EasingFunction = ease };
            var animOpacity = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };

            animOpacity.Completed += (_, _) =>
            {
                WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                WindowTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                RootGrid.BeginAnimation(OpacityProperty, null);

                WindowScaleTransform.ScaleX = 1.0;
                WindowScaleTransform.ScaleY = 1.0;
                WindowTranslateTransform.Y = 0;
                RootGrid.Opacity = 1.0;

                if (TabBrowser?.IsChecked == true && BrowserControl != null && _isBrowserInitialized)
                {
                    BrowserControl.Visibility = Visibility.Visible;
                }
            };

            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animScaleX);
            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animScaleY);
            WindowTranslateTransform.BeginAnimation(TranslateTransform.YProperty, animTransY);
            RootGrid.BeginAnimation(OpacityProperty, animOpacity);
        }

        private void AnimateCloseToTraySmooth()
        {
            if (BrowserControl != null && _isBrowserInitialized)
                BrowserControl.Visibility = Visibility.Collapsed;

            if (_isAnimatingTransition || WindowScaleTransform == null || WindowTranslateTransform == null || RootGrid == null)
            {
                HideToTray();
                return;
            }

            _isAnimatingTransition = true;
            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };

            var animScaleX = new DoubleAnimation(1.0, 0.90, duration) { EasingFunction = ease };
            var animScaleY = new DoubleAnimation(1.0, 0.90, duration) { EasingFunction = ease };
            var animTransY = new DoubleAnimation(0.0, 20.0, duration) { EasingFunction = ease };
            var animOpacity = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

            animOpacity.Completed += (_, _) =>
            {
                _isAnimatingTransition = false;
                HideToTray();

                WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                WindowTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                RootGrid.BeginAnimation(OpacityProperty, null);

                WindowScaleTransform.ScaleX = 1.0;
                WindowScaleTransform.ScaleY = 1.0;
                WindowTranslateTransform.Y = 0;
                RootGrid.Opacity = 1.0;
            };

            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animScaleX);
            WindowScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animScaleY);
            WindowTranslateTransform.BeginAnimation(TranslateTransform.YProperty, animTransY);
            RootGrid.BeginAnimation(OpacityProperty, animOpacity);
        }

        // ── Title bar ─────────────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                ToggleMaximizeRestoreSmooth();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var current = e.OriginalSource as DependencyObject;
                while (current != null && current != sender)
                {
                    if (current is System.Windows.Controls.Primitives.ButtonBase)
                        return;
                    current = VisualTreeHelper.GetParent(current);
                }

                if (_isCustomMaximized)
                {
                    // Hủy bỏ mọi animation đang chạy để tránh xung đột với cử chỉ kéo của người dùng
                    BeginAnimation(LeftProperty, null);
                    BeginAnimation(TopProperty, null);
                    BeginAnimation(WidthProperty, null);
                    BeginAnimation(HeightProperty, null);
                    _isAnimatingTransition = false;

                    var mouseInWindow = e.GetPosition(this);
                    var mouseScreen = PointToScreen(mouseInWindow);

                    var source = PresentationSource.FromVisual(this);
                    var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                    var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                    var mouseDipX = mouseScreen.X / dpiX;
                    var mouseDipY = mouseScreen.Y / dpiY;

                    var targetWidth = Math.Max(MinWidth, _normalRestoreBounds.Width > 100 ? _normalRestoreBounds.Width : 860);
                    var targetHeight = Math.Max(MinHeight, _normalRestoreBounds.Height > 100 ? _normalRestoreBounds.Height : 600);

                    // Tính tỉ lệ vị trí con trỏ trên thanh tiêu đề để cửa sổ co lại ngay tại tâm chuột đang giữ
                    var ratioX = Math.Clamp(mouseInWindow.X / Math.Max(1, ActualWidth), 0.05, 0.95);
                    var newLeft = mouseDipX - (ratioX * targetWidth);
                    var newTop = Math.Max(0, mouseDipY - Math.Min(mouseInWindow.Y, 25));

                    // Khôi phục kích thước chuẩn tức thì, không chạy animation cạnh tranh với DragMove
                    _isCustomMaximized = false;
                    Left = newLeft;
                    Top = newTop;
                    Width = targetWidth;
                    Height = targetHeight;

                    UpdateMaximizeButtonState();
                    ApplySidebarLayout(targetWidth);
                    ApplyResponsiveScale(targetWidth, targetHeight);
                }

                if (_passiveFocusMode)
                {
                    if (sender is UIElement titleBar)
                    {
                        StartTitleBarNonActivatingDrag(titleBar, e);
                    }
                    return;
                }

                try
                {
                    DragMove();
                }
                catch
                {
                    // Bỏ qua ngoại lệ nếu chuột nhả quá nhanh trước khi DragMove khởi tạo
                }
            }
        }

        private void StartTitleBarNonActivatingDrag(UIElement element, MouseButtonEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (!GetCursorPos(out _titleBarDragStartCursor) || !GetWindowRect(hwnd, out _titleBarDragStartWindowRect))
                return;

            _isTitleBarDragging = true;
            element.CaptureMouse();
            e.Handled = true;
        }

        private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isTitleBarDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                if (_isTitleBarDragging)
                    EndTitleBarNonActivatingDrag(sender as UIElement);
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (GetCursorPos(out var currentCursor))
            {
                int deltaX = currentCursor.X - _titleBarDragStartCursor.X;
                int deltaY = currentCursor.Y - _titleBarDragStartCursor.Y;
                int newX = _titleBarDragStartWindowRect.Left + deltaX;
                int newY = _titleBarDragStartWindowRect.Top + deltaY;

                SetWindowPos(hwnd, IntPtr.Zero, newX, newY, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        private void TitleBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isTitleBarDragging)
            {
                EndTitleBarNonActivatingDrag(sender as UIElement);
                e.Handled = true;
            }
        }

        private void TitleBar_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isTitleBarDragging)
            {
                EndTitleBarNonActivatingDrag(sender as UIElement);
            }
        }

        private void EndTitleBarNonActivatingDrag(UIElement? element)
        {
            _isTitleBarDragging = false;
            element?.ReleaseMouseCapture();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => AnimateMinimizeSmooth();

        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximizeRestoreSmooth();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => AnimateCloseToTraySmooth();

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Normal)
            {
                AnimateRestoreSmooth();
                RestoreCaptureGuardActivePolling();
                _threatBlocker.RestoreActivePolling();
            }
            else if (WindowState == WindowState.Minimized)
            {
                if (BrowserControl != null && _isBrowserInitialized)
                    BrowserControl.Visibility = Visibility.Collapsed;
                SetCaptureGuardMinimized(true);
                _threatBlocker.SetWindowMinimized(true);
                if (_previousForegroundHwnd != IntPtr.Zero && IsWindow(_previousForegroundHwnd))
                {
                    try { SetForegroundWindow(_previousForegroundHwnd); } catch { }
                }
            }
            ApplySidebarLayout(SidebarWindowWidth);
            ApplyResponsiveScale(SidebarWindowWidth, SidebarWindowHeight);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_exitRequested)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            CleanupAndExit();
        }

        public void CleanupAndExit()
        {
            if (_cleanupDone) return;
            _cleanupDone = true;
            try { _toastService?.ClearAll(); } catch { }
            try { _transcriptTimer.Stop(); } catch { }
            try { CleanupCaptions(); } catch { }
            try { StopCaptureRiskMonitoring(); } catch { }
            try { _selfProtectionTimer.Stop(); } catch { }
            try { _physicalOutputTimer.Stop(); } catch { }
            try { HidePhysicalOutputGuardCurtain(); } catch { }
            try { KillEnabledManagedApps(); } catch { }
            try { StopProtecting(); } catch { }
            try { _cursorCaptureProtection.StopGlobalCursorCloak(); } catch { }
            try { _cursorCaptureProtection.Dispose(); } catch { }
            try { _cursorShield.Dispose(); } catch { }
            try { CursorShieldService.RestoreAllCursors(); } catch { }
            try { CursorCaptureProtectionService.RestoreSystemCursors(); } catch { }
            try { _privacyCurtain.Hide(); } catch { }
            try { _systemToolsBlocker.Dispose(); } catch { }
            try { _antigravityNotifications.Dispose(); } catch { }
            try { _threatBlocker.Dispose(); } catch { }
            try { _hotkeys.Dispose(); } catch { }
            try { _vdService.Dispose(); } catch { }
            try { _antigravityAuthBridge.Dispose(); } catch { }
            try { CleanupBrowser(); } catch { }
            try { _threatBlocker.StatusChanged -= OnThreatBlockerStatusChanged; } catch { }
            try { _snippingTool.SnippingCompleted -= OnSnippingCompleted; } catch { }
            try { if (_recovery != null) _recovery.Changed -= UpdateRecoveryCenter; } catch { }
            try
            {
                if (_capturePrivacyCurtain is PrivacyCurtainService capCurtain)
                    capCurtain.DismissRequested -= DismissAllPrivacyCurtains;
                if (_privacyCurtain is PrivacyCurtainService manCurtain)
                    manCurtain.DismissRequested -= DismissAllPrivacyCurtains;
            }
            catch { }
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                    source?.RemoveHook(WndProcHook);
                }
            }
            catch { }
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            catch { }
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    SetWindowDisplayAffinity(hwnd, WDA_NONE);
            }
            catch { }
        }

        private void KillEnabledManagedApps()
        {
            if (!KillEnabledAppsOnExit && !_forceKillOnExit)
                return;

            var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var processIds = new HashSet<int>();

            foreach (var app in _apps.Where(a => a.IsEnabled && IsManageableApp(a)).ToList())
            {
                foreach (var state in app.ProtectedWindows)
                {
                    if (state.ProcessId > 0 && state.ProcessId != currentPid)
                        processIds.Add(state.ProcessId);
                }

                try
                {
                    foreach (var window in WindowEnumerator.FindAllByProcessName(app.ProcessName))
                    {
                        if (window.ProcessId > 0 && window.ProcessId != currentPid)
                            processIds.Add(window.ProcessId);
                    }
                }
                catch
                {
                }

                try
                {
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName(app.ProcessName))
                    {
                        using (process)
                        {
                            if (process.Id != currentPid)
                                processIds.Add(process.Id);
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (var pid in processIds)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (!ProcessSafetyService.Default.TryGetManageableProcess(pid, out var identity) || identity == null ||
                        !_apps.Any(app => app.IsEnabled && MatchesAppProcess(app, identity))) continue;
                    process.Kill(entireProcessTree: false);
                    process.WaitForExit(1200);
                }
                catch
                {
                }
            }
        }
    }
}
