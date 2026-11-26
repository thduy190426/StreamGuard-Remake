using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;
using Button = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Orientation = System.Windows.Controls.Orientation;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace StreamGuard;

/// <summary>Native WPF overlay, owned by Driver Booster; no independent event loop or hotkeys.</summary>
internal sealed class CaptionOverlayWindow : Window
{
    private readonly CaptionSettings _settings;
    private readonly TextBlock _previous, _current, _status;
    private readonly ProgressBar _meter;
    private readonly Button _pauseAction, _stopAction, _copyAction, _translateAction;
    private string _latest = "", _prior = "";
    private string? _latestVietnamese, _priorVietnamese;
    private HwndSource? _source;
    public event Action? PauseRequested, HideRequested, StopRequested, SettingsChanged, ToggleTranslationRequested;
    public bool IsClickThrough { get; private set; }
    public CaptureAffinityResult? CaptureResult { get; private set; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetLong(IntPtr window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private bool _isDragging;
    private POINT _dragStartCursor;
    private RECT _dragStartWindowRect;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTopmost = new(-1);
    public CaptionOverlayWindow(CaptionSettings settings)
    {
        Controls.HoverFeedback.Apply(this);
        _settings = settings;
        Focusable = false;
        Title = "Driver Booster — Live Captions"; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Width = Math.Min(760, Math.Max(360, SystemParameters.WorkArea.Width - 32)); MinWidth = 360; Height = 160; MinHeight = 130;
        MaxHeight = 400;
        var card = new Border { Background = new SolidColorBrush(Color.FromArgb(245, 20, 27, 43)), BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 90)), BorderThickness = new(1), CornerRadius = new(12), Padding = new(16, 10, 16, 14) };
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var toolbar = new DockPanel { LastChildFill = true, Margin = new(0, 0, 0, 8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right);
        Button Add(string text, string tooltip, Action action, string? icon = null)
        {
            var button = new Button { Content = text, FontSize = 11, Padding = new(8, 4, 8, 4), Margin = new(4, 0, 0, 0), Focusable = false, Style = (Style)FindResource("BaseButton") };
            if (icon != null) Controls.Icon.SetData(button, (Geometry)FindResource(icon));
            LocalizationService.BindKey(button, ToolTipProperty, tooltip);
            LocalizationService.BindKey(button, System.Windows.Automation.AutomationProperties.NameProperty, tooltip);
            button.Click += (_, _) => action(); actions.Children.Add(button); return button;
        }
        _pauseAction = Add("", "Captions.PauseResume", () => PauseRequested?.Invoke(), "UiIconPause");
        Add("A−", "Captions.Smaller", () => ResizeFont(-2)); Add("A+", "Captions.Larger", () => ResizeFont(2));
        _translateAction = Add("VI", "Captions.ToggleTranslation", () => ToggleTranslationRequested?.Invoke());
        _copyAction = Add("", "Captions.Copy", CopyLatest, "UiIconCopy"); _copyAction.IsEnabled = false;
        Add("—", "Captions.Hide", () => HideRequested?.Invoke()); _stopAction = Add("", "Captions.Stop", () => StopRequested?.Invoke(), "UiIconStop");
        toolbar.Children.Add(actions);
        _status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        toolbar.Children.Add(_status); layout.Children.Add(toolbar);
        var captions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _previous = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), TextWrapping = TextWrapping.Wrap, MaxHeight = 80, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 0, 4) };
        _current = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, MaxHeight = 150, TextTrimming = TextTrimming.CharacterEllipsis };
        captions.Children.Add(_previous); captions.Children.Add(_current);
        _meter = new ProgressBar { Style = (Style)FindResource("CaptionAudioMeter"), Minimum = 0, Maximum = 1, Height = 3, Margin = new(0, 7, 0, 0), IsHitTestVisible = false }; captions.Children.Add(_meter);
        Grid.SetRow(captions, 1); layout.Children.Add(captions); card.Child = layout; Content = card;
        card.MouseLeftButtonDown += (s, e) =>
        {
            if (!IsInteractiveVisual(e.OriginalSource as DependencyObject) && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                StartNonActivatingDrag(card, e);
            }
        };
        card.MouseMove += (s, e) => HandleMouseMove(card, e);
        card.MouseLeftButtonUp += (s, e) =>
        {
            if (_isDragging)
            {
                EndNonActivatingDrag(card);
                e.Handled = true;
            }
        };
        card.LostMouseCapture += (s, e) => EndNonActivatingDrag(card);
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); _source?.AddHook(Hook);
            ApplyNativeStyle(); ApplySettings();
        };
        LocationChanged += (_, _) => { if (IsVisible) { _settings.Left = Left; _settings.Top = Top; } };
        Closed += (_, _) => { _source?.RemoveHook(Hook); SettingsChanged?.Invoke(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { HideRequested?.Invoke(); e.Handled = true; }
            else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { ToggleTranslationRequested?.Invoke(); e.Handled = true; }
        };
        ResetPosition(useSaved: true); ApplySettings(); SetStatus(CaptionState.Starting);
    }
    private static bool IsInteractiveVisual(DependencyObject? visual)
    {
        while (visual != null)
        {
            if (visual is Button || visual is System.Windows.Controls.Primitives.ButtonBase ||
                visual is System.Windows.Controls.Primitives.ScrollBar || visual is System.Windows.Controls.Primitives.Thumb)
                return true;
            visual = VisualTreeHelper.GetParent(visual);
        }
        return false;
    }
    private void StartNonActivatingDrag(UIElement element, MouseButtonEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (!GetCursorPos(out _dragStartCursor) || !GetWindowRect(handle, out _dragStartWindowRect))
            return;

        _isDragging = true;
        element.CaptureMouse();
        e.Handled = true;
    }
    private void HandleMouseMove(UIElement element, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            if (_isDragging)
                EndNonActivatingDrag(element);
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        if (GetCursorPos(out var current))
        {
            int newX = _dragStartWindowRect.Left + (current.X - _dragStartCursor.X);
            int newY = _dragStartWindowRect.Top + (current.Y - _dragStartCursor.Y);
            SetWindowPos(handle, IntPtr.Zero, newX, newY, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
    }
    private void EndNonActivatingDrag(UIElement element)
    {
        if (_isDragging)
        {
            _isDragging = false;
            element.ReleaseMouseCapture();
            SettingsChanged?.Invoke();
        }
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    { if (message == 0x21) { handled = true; return new IntPtr(3); } return IntPtr.Zero; }
    public void ApplyNativeStyle()
    {
        ShowInTaskbar = false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = (GetLong(handle, -20).ToInt64() | 0x08000000L | 0x80L) & ~0x00040000L; // NOACTIVATE | TOOLWINDOW & ~APPWINDOW
        style = IsClickThrough ? style | 0x20 : style & ~0x20L;
        SetLong(handle, -20, (nint)style);
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    public void ReassertTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }
    public void SetClickThrough(bool enabled) { IsClickThrough = enabled; ApplyNativeStyle(); }
    public bool RefreshCaptureProtection()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return false;
        var previous = CaptureResult;
        if (_settings.ExcludeFromCapture)
            CaptureResult = new WindowCaptureProtectionService().ApplyAndVerify(handle);
        else
            CaptureResult = SetWindowDisplayAffinity(handle, 0) && GetWindowDisplayAffinity(handle, out var affinity) && affinity == 0
                ? null
                : new(CaptureAffinityMode.Unverified);
        return CaptureResult != previous;
    }
    public void ApplySettings()
    {
        _settings.Normalize();
        _previous.FontSize = Math.Max(12, _settings.FontSize - 2);
        _current.FontSize = _settings.FontSize;
        _previous.Visibility = _settings.SingleLine && (!_settings.EnableTranslation || _settings.TranslationMode == "VietnameseOnly")
            ? Visibility.Collapsed
            : Visibility.Visible;
        Height = Math.Clamp(80 + _settings.FontSize * (_settings.SingleLine && (!_settings.EnableTranslation || _settings.TranslationMode == "VietnameseOnly") ? 2.5 : 4.5), 130, 320);
        var workArea = SystemParameters.WorkArea;
        if (Top + Height > workArea.Bottom - 10)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - Height - 16);
        }
        UpdateTranslateButtonState();
        ApplyNativeStyle();
        RefreshCaptureProtection();
    }
    public void UpdateTranslateButtonState()
    {
        if (_translateAction == null) return;
        _translateAction.Foreground = (System.Windows.Media.Brush)FindResource(_settings.EnableTranslation ? "AccentBrush" : "TextDimBrush");
        _translateAction.FontWeight = _settings.EnableTranslation ? FontWeights.Bold : FontWeights.Normal;
    }
    private void ResizeFont(int amount) { _settings.FontSize += amount; ApplySettings(); SettingsChanged?.Invoke(); }
    public void ResetPosition(bool useSaved = false)
    {
        var area = SystemParameters.WorkArea;
        Left = useSaved && _settings.Left.HasValue ? _settings.Left.Value : area.Left + (area.Width - Width) / 2;
        Top = useSaved && _settings.Top.HasValue ? _settings.Top.Value : area.Bottom - Height - 32;
        if (Left + Width < SystemParameters.VirtualScreenLeft + 40 || Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 ||
            Top + Height < SystemParameters.VirtualScreenTop + 40 || Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        { Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 32; }
        if (!useSaved) { _settings.Left = Left; _settings.Top = Top; SettingsChanged?.Invoke(); }
    }
    public void UpdateSubtitle(string text, string? translation = null)
    {
        _prior = _latest;
        _latest = text;

        if (_settings.EnableTranslation && _settings.TranslationMode == "VietnameseOnly")
        {
            _priorVietnamese = _latestVietnamese;
            _latestVietnamese = !string.IsNullOrWhiteSpace(translation) ? translation : null;
            _previous.Text = _priorVietnamese ?? _prior;
            _current.Text = !string.IsNullOrWhiteSpace(translation) ? translation : text;
        }
        else if (_settings.EnableTranslation) // Bilingual
        {
            _latestVietnamese = !string.IsNullOrWhiteSpace(translation) ? translation : null;
            _previous.Text = text;
            _current.Text = !string.IsNullOrWhiteSpace(translation) ? translation : "";
        }
        else
        {
            _previous.Text = _prior;
            _current.Text = text;
        }

        _copyAction.IsEnabled = !string.IsNullOrWhiteSpace(text);
    }
    public void UpdateTranslation(string text, string translation)
    {
        if (!_settings.EnableTranslation || string.IsNullOrWhiteSpace(translation) || translation.Equals(text, StringComparison.OrdinalIgnoreCase))
            return;

        if (_latest.Equals(text, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_latest))
        {
            _latestVietnamese = translation;
            if (_settings.TranslationMode == "VietnameseOnly")
            {
                if (!string.IsNullOrWhiteSpace(_priorVietnamese))
                    _previous.Text = _priorVietnamese;
                _current.Text = translation;
            }
            else // Bilingual
            {
                _previous.Text = text;
                _current.Text = translation;
            }
        }
    }
    public void UpdateLevel(double value) => _meter.Value = Math.Clamp(value, 0, 1);
    public void SetStatus(CaptionState state)
    {
        LocalizationService.BindKey(_status, TextBlock.TextProperty, "Captions." + state);
        _pauseAction.IsEnabled = state is CaptionState.Running or CaptionState.Paused;
        _stopAction.IsEnabled = state is not (CaptionState.Stopped or CaptionState.Stopping);
        Controls.Icon.SetData(_pauseAction, (Geometry)FindResource(state == CaptionState.Paused ? "UiIconPlay" : "UiIconPause"));
        _status.Foreground = (System.Windows.Media.Brush)FindResource(state switch
        { CaptionState.Running => "SuccessBrush", CaptionState.Paused => "WarningBrush", CaptionState.Faulted => "DangerBrush", _ => "TextDimBrush" });
    }
    public void CopyLatest()
    {
        if (_settings.EnableTranslation && _settings.TranslationMode == "Bilingual")
        {
            if (!string.IsNullOrWhiteSpace(_previous.Text) && !string.IsNullOrWhiteSpace(_current.Text))
            {
                try { System.Windows.Clipboard.SetText($"{_previous.Text}\r\n{_current.Text}"); } catch (ExternalException) { }
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(_current.Text))
        {
            try
            {
                System.Windows.Clipboard.SetText(_current.Text);
            }
            catch (ExternalException) { }
        }
        else if (!string.IsNullOrWhiteSpace(_previous.Text))
        {
            try
            {
                System.Windows.Clipboard.SetText(_previous.Text);
            }
            catch (ExternalException) { }
        }
    }
}
