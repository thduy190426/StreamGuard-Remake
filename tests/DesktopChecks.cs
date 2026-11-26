using System.Windows.Interop;
using StreamGuard.Services;

internal static class DesktopChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var source = new HwndSource(new HwndSourceParameters("Audit-owned desktop policy test")
        { Width = 16, Height = 16, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000) });
        foreach (var existing in new[] { false, true })
        {
            var shell = new FakeShell(existing);
            var manager = new FakeManager(shell);
            using var service = new VirtualDesktopService(manager, shell);
            check(service.IsAvailable, "Injected desktop APIs are available");
            check(service.MoveToHidden(source.Handle, out var original) && original == shell.First.Id, "Desktop operation records the original desktop before moving");
            check(!service.MoveToDesktop(IntPtr.Zero, original), "Invalid window is never moved");
            check(service.MoveToDesktop(source.Handle, original), "Restore returns the window to its original desktop");
            check(service.SwitchToHiddenDesktop() && shell.Current == shell.Hidden, "Switch to target desktop");
            check(service.ToggleHiddenDesktop() && shell.Current == shell.First, "Toggle returns to initial desktop");
            check(service.ToggleHiddenDesktop() && shell.Current == shell.Hidden, "Toggle can return to target desktop");
            check(service.SwitchToInitialDesktop() && shell.Current == shell.First, "Explicit desktop recovery");
            shell.Current = shell.Hidden;
            service.RemoveHiddenDesktop();
            check(shell.RemoveCount == (existing ? 0 : 1), "Existing user desktops are never deleted; only an owned desktop may be removed");
            if (!existing) check(shell.LastFallback == shell.First, "Removing the current owned desktop uses a different fallback desktop");
            service.Dispose(); service.Dispose();
            check(!service.IsAvailable && service.GetCurrentDesktopId() == Guid.Empty, "Desktop disposal is repeatable and disables later operations");
        }
        using var unavailable = new VirtualDesktopService(null, null);
        check(!unavailable.MoveToHidden(source.Handle, out _) && !unavailable.ToggleHiddenDesktop() &&
            !unavailable.SwitchToHiddenDesktop() && !unavailable.SwitchToInitialDesktop() && unavailable.IsWindowOnCurrentDesktop(source.Handle) == null,
            "Unavailable platform APIs fall back without reporting success");

        // Virtual Desktop Hotkeys Shield Tests
        using (var hotkeys = new HotkeyService())
        {
            check(!hotkeys.BlockVirtualDesktopHotkeys, "BlockVirtualDesktopHotkeys is disabled by default");

            const int VK_TAB = 0x09;
            const int VK_LEFT = 0x25;
            const int VK_RIGHT = 0x27;
            const int VK_D = 0x44;
            const int VK_F4 = 0x73;
            const int VK_LWIN = 0x5B;
            const int VK_LCONTROL = 0xA2;
            const int VK_LMENU = 0xA4;

            // When switch is false, nothing is blocked
            hotkeys.UpdateChordState(VK_LWIN, true);
            hotkeys.UpdateChordState(VK_LCONTROL, true);
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_D, out _), "When switch is off, Win+Ctrl+D is not blocked");
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_TAB, out _), "When switch is off, Win+Tab is not blocked");

            // Enable switch
            hotkeys.BlockVirtualDesktopHotkeys = true;
            check(hotkeys.BlockVirtualDesktopHotkeys, "BlockVirtualDesktopHotkeys can be enabled");

            // 1. Win + Ctrl + D
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_D, out var dName) && dName == "Win + Ctrl + D", "Win+Ctrl+D is blocked when switch is on");

            // 2. Win + Ctrl + F4
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_F4, out var f4Name) && f4Name == "Win + Ctrl + F4", "Win+Ctrl+F4 is blocked when switch is on");

            // 3. Win + Ctrl + Left Arrow
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_LEFT, out var leftName) && leftName == "Win + Ctrl + Left Arrow", "Win+Ctrl+Left Arrow is blocked when switch is on");

            // 4. Win + Ctrl + Right Arrow
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_RIGHT, out var rightName) && rightName == "Win + Ctrl + Right Arrow", "Win+Ctrl+Right Arrow is blocked when switch is on");

            // 5. Release Ctrl, keep Win: Test Win + Tab
            hotkeys.UpdateChordState(VK_LCONTROL, false);
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_TAB, out var tabName) && tabName == "Win + Tab", "Win+Tab is blocked when switch is on");

            // 6. Win + D (ordinary show desktop, Ctrl is NOT held): NOT blocked
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_D, out _), "Win+D (Show Desktop) is NOT blocked");

            // 7. Alt + Tab (ordinary window switch): NOT blocked
            hotkeys.UpdateChordState(VK_LWIN, false);
            hotkeys.UpdateChordState(VK_LMENU, true);
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_TAB, out _), "Alt+Tab (Task Switcher) is NOT blocked");

            // 8. Plain Tab: NOT blocked
            hotkeys.UpdateChordState(VK_LMENU, false);
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_TAB, out _), "Plain Tab is NOT blocked");

            // 9. Plain arrows: NOT blocked
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_LEFT, out _), "Plain Left Arrow is NOT blocked");
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_RIGHT, out _), "Plain Right Arrow is NOT blocked");

            // 10. Event registration
            string? blockedReceived = null;
            hotkeys.VirtualDesktopHotkeyBlocked += name => blockedReceived = name;
            hotkeys.UpdateChordState(VK_LWIN, true);
            hotkeys.UpdateChordState(VK_LCONTROL, true);
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_D, out var eventName) && eventName == "Win + Ctrl + D", "Virtual desktop hotkey identification works");

            const int VK_LSHIFT = 0xA0;

            // 11. Win + Ctrl + Shift + Left Arrow (Move window across desktops)
            hotkeys.UpdateChordState(VK_LSHIFT, true);
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_LEFT, out var shiftLeftName) && shiftLeftName == "Win + Ctrl + Shift + Left Arrow", "Win+Ctrl+Shift+Left Arrow is blocked");
            check(hotkeys.ShouldBlockVirtualDesktopKey(VK_RIGHT, out var shiftRightName) && shiftRightName == "Win + Ctrl + Shift + Right Arrow", "Win+Ctrl+Shift+Right Arrow is blocked");

            // 12. Win + Shift + Left Arrow (Multi-monitor window move, Ctrl is NOT held): NOT blocked!
            hotkeys.UpdateChordState(VK_LCONTROL, false);
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_LEFT, out _), "Win+Shift+Left Arrow (Multi-monitor window move) is NOT blocked");
            check(!hotkeys.ShouldBlockVirtualDesktopKey(VK_RIGHT, out _), "Win+Shift+Right Arrow (Multi-monitor window move) is NOT blocked");
            hotkeys.UpdateChordState(VK_LSHIFT, false);
        }
    }

    private sealed class Desktop : IVirtualDesktop
    {
        public Guid Id { get; } = Guid.NewGuid();
        public bool IsViewVisible(IntPtr view) => false;
        public Guid GetId() => Id;
    }
    private sealed class DesktopArray : IObjectArray
    {
        private readonly Desktop[] _items;
        internal DesktopArray(params Desktop[] items) => _items = items;
        public void GetCount(out int count) => count = _items.Length;
        public void GetAt(int index, ref Guid id, out object value) => value = _items[index];
    }
    private sealed class FakeShell : IVirtualDesktopManagerInternal
    {
        internal Desktop First = new(), Hidden = new();
        internal Desktop Current;
        internal bool Existing;
        internal int RemoveCount;
        internal IVirtualDesktop? LastFallback;
        internal FakeShell(bool existing) { Existing = existing; Current = First; }
        public int GetCount() => Existing ? 2 : 1;
        public void MoveViewToDesktop(IntPtr view, IVirtualDesktop desktop) { }
        public bool CanViewMoveDesktops(IntPtr view) => true;
        public IVirtualDesktop GetCurrentDesktop() => Current;
        public void GetDesktops(out IObjectArray array) => array = Existing ? new DesktopArray(First, Hidden) : new DesktopArray(First);
        public IVirtualDesktop GetAdjacentDesktop(IVirtualDesktop reference, int direction) => Hidden;
        public void SwitchDesktop(IVirtualDesktop desktop) => Current = (Desktop)desktop;
        public IVirtualDesktop CreateDesktop() => Hidden;
        public void RemoveDesktop(IVirtualDesktop remove, IVirtualDesktop fallback) { RemoveCount++; LastFallback = fallback; }
        public IVirtualDesktop FindDesktop(ref Guid id) => Hidden;
    }
    private sealed class FakeManager : IVirtualDesktopManager
    {
        private readonly FakeShell _shell;
        private Guid _windowDesktop;
        internal FakeManager(FakeShell shell) { _shell = shell; _windowDesktop = shell.First.Id; }
        public bool IsWindowOnCurrentVirtualDesktop(IntPtr window) => _windowDesktop == _shell.Current.Id;
        public Guid GetWindowDesktopId(IntPtr window) => _windowDesktop;
        public void MoveWindowToDesktop(IntPtr window, ref Guid desktop) => _windowDesktop = desktop;
    }
}
