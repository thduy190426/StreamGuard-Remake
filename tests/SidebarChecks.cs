using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using StreamGuard;
using StreamGuard.Localization;

internal static class SidebarChecks
{
    internal static void Run(MainWindow window, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var column = (ColumnDefinition)window.FindName("SidebarCol");
        var root = (Grid)window.FindName("RootGrid");
        var handle = (Thumb)window.FindName("SidebarResizeThumb");
        var available = (RadioButton)window.FindName("TabAvailable");
        var protectedTab = (RadioButton)window.FindName("TabProtected");
        void Layout(double width)
        {
            window.Width = width;
            window.Measure(new Size(width, 600)); window.Arrange(new Rect(0, 0, width, 600));
            Call("ApplySidebarLayout", width);
            root.Measure(new Size(width, 600)); root.Arrange(new Rect(0, 0, width, 600)); root.UpdateLayout();
        }
        void Resize(double width) { Call("ResizeSidebarTo", width); Layout(window.Width); }
        void Key(Key key)
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, new OffscreenSource(), 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
            handle.RaiseEvent(e);
            check(e.Handled, "Sidebar handles resize key: " + key);
            Layout(window.Width);
        }
        foreach (var language in new[] { "en", "vi" })
        {
            LocalizationService.Current.SetLanguage(language);
            Layout(860); Call("ToggleSidebar", false); Resize(258);
            check(column.Width.Value == 258, "Sidebar follows a requested width: " + language);
            check(((TextBlock)window.FindName("TabAvailableText")).Visibility == Visibility.Visible, "Expanded sidebar shows localized labels");
            Resize(10000);
            check(column.Width.Value == 300 && root.ColumnDefinitions[1].ActualWidth >= 520, "Sidebar maximum preserves main content");
            Layout(780);
            check(column.Width.Value == 260 && root.ColumnDefinitions[1].ActualWidth >= 520, "Window resize clamps sidebar to available space");
            Layout(1200);
            check(column.Width.Value == 300, "Enlarging window restores preferred expanded width");
            Layout(719);
            check(column.Width.Value == 60 && ((TextBlock)window.FindName("TabAvailableText")).Visibility == Visibility.Collapsed, "Narrow window automatically uses icons only");
            Layout(860);
            check(column.Width.Value == 300, "Automatic compact mode restores width after enlarging");
            Resize(159);
            check(column.Width.Value == 60, "Dragging below threshold collapses sidebar");
            var icon = (FrameworkElement)((Grid)available.Content).Children[0];
            var center = icon.TranslatePoint(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2), root);
            check(Math.Abs(center.X - 30) <= 2, "Compact navigation icon is centered");
            check(!string.IsNullOrWhiteSpace(available.ToolTip as string), "Compact navigation retains translated tooltip");
            foreach (var name in new[] { "TabProtected", "TabAvailable", "TabThreats", "TabSettings", "TabRecovery", "TabCaptions", "TabBrowser", "TabDiagnostics" })
            {
                var tab = (RadioButton)window.FindName(name);
                check(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(tab)) &&
                    System.Windows.Automation.AutomationProperties.GetName(tab) == tab.ToolTip as string,
                    "Compact navigation has a translated accessible name without showing a tooltip: " + name);
            }
            Layout(1000);
            check(column.Width.Value == 60, "Manual collapse survives window resize");
            Key(System.Windows.Input.Key.Right);
            check(column.Width.Value == 200, "Keyboard expands from compact mode");
            Key(System.Windows.Input.Key.Left);
            check(column.Width.Value == 60, "Keyboard can collapse from minimum expanded width");
            Key(System.Windows.Input.Key.End);
            check(column.Width.Value == 300, "End expands to bounded maximum");
            Key(System.Windows.Input.Key.Home);
            check(column.Width.Value == 60, "Home switches to icons only");
            Resize(160);
            check(column.Width.Value == 200, "Expansion threshold snaps to readable minimum width");
            Resize(247);
            handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            typeof(MainWindow).GetField("_sidebarDragOffset", flags)!.SetValue(window, 4d);
            Call("ResizeSidebarFromPointer", 149d);
            check(column.Width.Value == 60, "Pointer drag crosses collapse threshold");
            Call("ResizeSidebarFromPointer", 209d);
            check(column.Width.Value == 205, "Pointer drag expands continuously after compact snap");
            Call("ResizeSidebarFromPointer", 251d);
            handle.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            check(column.Width.Value == 247, "Completed drag keeps chosen width without accumulating handle movement");
            handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            Resize(80);
            handle.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
            check(column.Width.Value == 247, "Canceled drag restores expanded size and mode");
            Call("ToggleSidebar", true);
            handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            Resize(280);
            handle.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
            check(column.Width.Value == 60, "Canceled drag restores manual compact state");
            Call("BtnToggleSidebar_Click", window, new RoutedEventArgs());
            check(column.Width.Value == 247, "Menu button restores previously chosen expanded size");
            Resize(double.NaN); Resize(double.PositiveInfinity);
            check(column.Width.Value == 247, "Invalid widths do not corrupt sidebar state");
            Layout(640); Key(System.Windows.Input.Key.End); Layout(860);
            check(column.Width.Value == 247, "Expansion key in a narrow window does not force manual collapse");
            foreach (var width in new[] { 640d, 700, 720, 740, 800, 860, 1024, 1920 })
            {
                Layout(width); Resize(10000);
                check(column.Width.Value <= 300 && root.ColumnDefinitions[1].ActualWidth >= 520,
                    "Sidebar and main content bounds at window width " + width);
            }
            Call("ToggleSidebar", true); protectedTab.IsChecked = true;
            check(protectedTab.IsChecked == true && ((FrameworkElement)window.FindName("ViewProtected")).Visibility == Visibility.Visible,
                "Navigation still works in compact mode");
        }
        Layout(860); Resize(210); LocalizationService.Current.SetLanguage("en");
        var focus = typeof(MainWindow).GetMethod("IsTextEntryVisual", BindingFlags.Static | BindingFlags.NonPublic)!;
        check((bool)focus.Invoke(null, new object[] { handle })!, "Resize handle accepts keyboard focus in passive mode");
        check(!(bool)focus.Invoke(null, new object[] { new Thumb() })!, "Other controls retain passive focus behavior");
    }

    private sealed class OffscreenSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
}
