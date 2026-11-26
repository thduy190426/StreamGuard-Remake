using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using StreamGuard.Controls;

internal static class HoverFeedbackChecks
{
    internal static void Run(FrameworkElement root, Action<bool, string> check)
    {
        check(root.Cursor == Cursors.Arrow && root.ForceCursor && !ToolTipService.GetIsEnabled(root),
            "Window disables hover hints and keeps the default arrow cursor");
        // Detached controls model popup visual trees and controls created after startup.
        var targets = new DependencyObject[] {
            root, new Button(), new TextBox(), new RadioButton(), new CheckBox(),
            new Thumb(), new ComboBox(), new ComboBoxItem(), new MenuItem(),
            new ContextMenu(), new Hyperlink(new Run("Help"))
        };
        var tooltipConstructor = typeof(ToolTipEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)!;
        foreach (var target in targets)
        {
            ToolTipService.SetToolTip(target, "Translated help");
            ToolTipService.SetIsEnabled(target, true); // A later assignment cannot re-enable hover hints.
            var tooltip = (ToolTipEventArgs)tooltipConstructor.Invoke(new object[] { true });
            Raise(target, tooltip);
            check(tooltip.Handled, $"Tooltip opening suppressed on {target.GetType().Name}");
            check((string)ToolTipService.GetToolTip(target) == "Translated help",
                "Suppressing hover preserves accessible help metadata");
            foreach (var alreadyHandled in new[] { false, true })
            {
                var cursor = new QueryCursorEventArgs(Mouse.PrimaryDevice, Environment.TickCount) {
                    RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.Hand, Handled = alreadyHandled
                };
                Raise(target, cursor);
                check(cursor.Handled && cursor.Cursor == Cursors.Arrow,
                    $"Arrow cursor enforced on {target.GetType().Name}, previously handled={alreadyHandled}");
            }
        }
        ToolTipService.SetIsEnabled(root, false);
        ToolTipService.SetToolTip(root, null);
        var button = new Button();
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(clicks == 1, "Suppressing hover does not consume button clicks");
        var thumb = new Thumb();
        var dragged = 0.0;
        thumb.DragDelta += (_, e) => dragged += e.HorizontalChange;
        thumb.RaiseEvent(new DragDeltaEventArgs(12, 0));
        check(dragged == 12, "Default cursor preserves drag events");
    }

    private static void Raise(DependencyObject target, RoutedEventArgs args)
    {
        if (target is UIElement element) element.RaiseEvent(args);
        else ((ContentElement)target).RaiseEvent(args);
    }
}
