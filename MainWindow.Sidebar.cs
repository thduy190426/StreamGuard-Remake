using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace StreamGuard
{
    public partial class MainWindow
    {
        // WPF device-independent pixels: retain a usable main panel at every window size.
        private const double CompactSidebarWidth = 60;
        private const double MinimumExpandedSidebarWidth = 200;
        private const double MaximumSidebarWidth = 300;
        private const double MinimumMainContentWidth = 520;
        private const double SidebarCollapseThreshold = 160;
        private double _sidebarExpandedWidth = 210;
        private bool _sidebarManuallyCollapsed;
        private bool _isSidebarCollapsed;
        private double _sidebarDragOffset;
        private double _sidebarBeforeDragWidth;
        private bool _sidebarBeforeDragCollapsed;

        private double SidebarWindowWidth => ActualWidth > 0 ? ActualWidth : Width;
        private double SidebarWindowHeight => ActualHeight > 0 ? ActualHeight : Height;
        private double SidebarMaximum(double windowWidth) => Math.Min(MaximumSidebarWidth,
            Math.Max(0, windowWidth - MinimumMainContentWidth));

        internal double CurrentUiScale { get; private set; } = 1.0;

        internal double CalculateUiScale(double windowWidth, double windowHeight)
        {
            if (!double.IsFinite(windowWidth) || windowWidth <= 0) windowWidth = 860;
            if (!double.IsFinite(windowHeight) || windowHeight <= 0) windowHeight = 600;

            var isMaximized = _isCustomMaximized || WindowState == WindowState.Maximized;
            if (isMaximized)
            {
                var maxRatio = Math.Min(windowWidth / 860.0, windowHeight / 600.0);
                return Math.Clamp(1.0 + Math.Max(0.18, (maxRatio - 1.0) * 0.38), 1.18, 1.30);
            }

            if (windowWidth < 760 || windowHeight < 540)
            {
                var smallRatio = Math.Min(windowWidth / 760.0, windowHeight / 540.0);
                return Math.Clamp(0.92 + (smallRatio - 0.84) * 0.50, 0.92, 0.98);
            }

            if (windowWidth >= 1020 && windowHeight >= 680)
            {
                var largeRatio = Math.Min(windowWidth / 860.0, windowHeight / 600.0);
                return Math.Clamp(1.0 + (largeRatio - 1.15) * 0.35, 1.0, 1.22);
            }

            return 1.0;
        }

        internal void ApplyResponsiveScale(double windowWidth, double windowHeight)
        {
            if (MainContentScaleTransform == null || AppBrandText == null) return;

            var scale = Math.Round(CalculateUiScale(windowWidth, windowHeight), 3);
            CurrentUiScale = scale;

            MainContentScaleTransform.ScaleX = scale;
            MainContentScaleTransform.ScaleY = scale;
            FontSize = Math.Round(12.0 * scale, 1);

            AppBrandText.FontSize = Math.Round(13.0 * scale, 1);
            if (VersionText != null) VersionText.FontSize = Math.Round(8.0 * scale, 1);
            if (SidebarSubtitleText != null) SidebarSubtitleText.FontSize = Math.Round(9.0 * scale, 1);

            foreach (var label in new[] { TabProtectedText, TabAvailableText, TabThreatsText, TabSettingsText, TabRecoveryText, TabCaptionsText, TabBrowserText, TabDiagnosticsText })
            {
                if (label != null) label.FontSize = Math.Round(12.0 * scale, 1);
            }

            foreach (var tab in new[] { TabProtected, TabAvailable, TabThreats, TabSettings, TabRecovery, TabCaptions, TabBrowser, TabDiagnostics })
            {
                if (tab?.Content is Grid content && content.Children.Count > 0 && content.Children[0] is FrameworkElement icon)
                {
                    icon.Width = Math.Round(14.0 * scale, 1);
                    icon.Height = Math.Round(14.0 * scale, 1);
                }
            }

            if (PanicText != null) PanicText.FontSize = Math.Round(11.0 * scale, 1);
            if (PanicHintText != null) PanicHintText.FontSize = Math.Round(9.0 * scale, 1);
            if (AppVersionFooterText != null) AppVersionFooterText.FontSize = Math.Round(9.0 * scale, 1);
            if (BtnPanic != null) BtnPanic.Height = Math.Round(34.0 * scale, 1);
            if (CaptureBadgeText != null) CaptureBadgeText.FontSize = Math.Round(10.0 * scale, 1);
        }

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e) => ToggleSidebar(!_isSidebarCollapsed);

        private void ToggleSidebar(bool collapse)
        {
            _sidebarManuallyCollapsed = collapse;
            ApplySidebarLayout(SidebarWindowWidth);
        }

        private void ResizeSidebarTo(double requestedWidth)
        {
            if (!double.IsFinite(requestedWidth)) return;
            _sidebarManuallyCollapsed = requestedWidth < SidebarCollapseThreshold;
            // Collapsing keeps the last expanded size for the hamburger button.
            if (!_sidebarManuallyCollapsed && SidebarMaximum(SidebarWindowWidth) >= MinimumExpandedSidebarWidth)
                _sidebarExpandedWidth = Math.Clamp(requestedWidth, MinimumExpandedSidebarWidth, SidebarMaximum(SidebarWindowWidth));
            ApplySidebarLayout(SidebarWindowWidth);
        }

        private void ApplySidebarLayout(double windowWidth)
        {
            // SizeChanged can occur while InitializeComponent is still constructing controls.
            if (SidebarResizeThumb == null || SidebarCol == null) return;
            var maximum = SidebarMaximum(windowWidth);
            _isSidebarCollapsed = _sidebarManuallyCollapsed || maximum < MinimumExpandedSidebarWidth;
            var isMaximized = _isCustomMaximized || WindowState == WindowState.Maximized;
            var scale = CalculateUiScale(windowWidth, SidebarWindowHeight);
            var desiredExpandedWidth = isMaximized
                ? Math.Max(_sidebarExpandedWidth, Math.Round(210 * scale))
                : _sidebarExpandedWidth;
            SidebarCol.Width = new GridLength(_isSidebarCollapsed ? CompactSidebarWidth : Math.Min(desiredExpandedWidth, maximum));
            var visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            foreach (var label in new FrameworkElement?[] { SidebarBrandText, TabProtectedText, TabAvailableText,
                TabThreatsText, TabSettingsText, TabRecoveryText, TabCaptionsText, TabBrowserText, TabDiagnosticsText, PanicText, PanicHintText })
                if (label != null) label.Visibility = visibility;

            foreach (var tab in new[] { TabProtected, TabAvailable, TabThreats, TabSettings, TabRecovery, TabCaptions, TabBrowser, TabDiagnostics })
            {
                if (tab?.Content is Grid content && content.Children.Count > 0)
                {
                    content.HorizontalAlignment = _isSidebarCollapsed ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch;
                    ((FrameworkElement)content.Children[0]).Margin = new Thickness(0, 0, _isSidebarCollapsed ? 0 : 10, 0);
                }
            }
            SidebarBrandRow.HorizontalAlignment = _isSidebarCollapsed ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left;
            SidebarBrandRow.Margin = _isSidebarCollapsed ? new Thickness(0) : new Thickness(12, 0, 10, 0);
            SidebarLogo.Margin = new Thickness(0, 0, _isSidebarCollapsed ? 0 : 8, 0);
            PanicIcon.Margin = new Thickness(0, 0, _isSidebarCollapsed ? 0 : 6, 0);
            ApplyResponsiveScale(windowWidth, SidebarWindowHeight);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplySidebarLayout(e.NewSize.Width);
            ApplyResponsiveScale(e.NewSize.Width, e.NewSize.Height);
        }

        private void SidebarResize_DragStarted(object sender, DragStartedEventArgs e)
        {
            _sidebarBeforeDragWidth = _sidebarExpandedWidth;
            _sidebarBeforeDragCollapsed = _sidebarManuallyCollapsed;
            _sidebarDragOffset = Mouse.GetPosition(RootGrid).X - SidebarCol.Width.Value;
        }

        private void SidebarResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // The handle moves with the sidebar. Root coordinates avoid feedback/jumps when it snaps to icons.
            ResizeSidebarFromPointer(Mouse.GetPosition(RootGrid).X);
            e.Handled = true;
        }

        private void ResizeSidebarFromPointer(double pointerX) => ResizeSidebarTo(pointerX - _sidebarDragOffset);

        private void SidebarResize_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (!e.Canceled) return;
            _sidebarExpandedWidth = _sidebarBeforeDragWidth;
            _sidebarManuallyCollapsed = _sidebarBeforeDragCollapsed;
            ApplySidebarLayout(SidebarWindowWidth);
        }

        private void SidebarResize_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    ResizeSidebarTo(SidebarCol.Width.Value <= MinimumExpandedSidebarWidth ? CompactSidebarWidth : SidebarCol.Width.Value - 16);
                    break;
                case Key.Right:
                    ResizeSidebarTo(_isSidebarCollapsed ? MinimumExpandedSidebarWidth : SidebarCol.Width.Value + 16);
                    break;
                case Key.Home: ToggleSidebar(true); break;
                case Key.End: ResizeSidebarTo(Math.Max(MinimumExpandedSidebarWidth, SidebarMaximum(SidebarWindowWidth))); break;
                case Key.Escape when SidebarResizeThumb.IsDragging: SidebarResizeThumb.CancelDrag(); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
