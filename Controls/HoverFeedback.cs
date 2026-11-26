using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StreamGuard.Controls;

/// <summary>Suppresses hover feedback inside this process, including separate popup visual trees.</summary>
internal static class HoverFeedback
{
    static HoverFeedback()
    {
        EventManager.RegisterClassHandler(typeof(UIElement), Mouse.QueryCursorEvent,
            new QueryCursorEventHandler(UseDefaultCursor), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ContentElement), Mouse.QueryCursorEvent,
            new QueryCursorEventHandler(UseDefaultCursor), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(UIElement), ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler(SuppressToolTip), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(FrameworkContentElement), ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler(SuppressToolTip), handledEventsToo: true);
    }

    public static void Initialize() { }

    public static void Apply(FrameworkElement root)
    {
        Initialize();
        root.Cursor = System.Windows.Input.Cursors.Arrow;
        root.ForceCursor = true;
        ToolTipService.SetIsEnabled(root, false);
    }

    private static void UseDefaultCursor(object sender, QueryCursorEventArgs e)
    {
        e.Cursor = System.Windows.Input.Cursors.Arrow;
        e.Handled = true;
    }
    private static void SuppressToolTip(object sender, ToolTipEventArgs e) => e.Handled = true;
}
