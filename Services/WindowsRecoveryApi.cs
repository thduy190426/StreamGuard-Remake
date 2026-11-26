using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using StreamGuard.Models;

namespace StreamGuard.Services;

/// <summary>Restores only the original process instance AND the marked original HWND.</summary>
public sealed class WindowsRecoveryApi : IRecoveryWindowApi
{
    private readonly VirtualDesktopService _desktops;
    private readonly int _forbiddenProcess;
    public WindowsRecoveryApi(VirtualDesktopService desktops) : this(desktops, Environment.ProcessId) { }
    internal WindowsRecoveryApi(VirtualDesktopService desktops, int forbiddenProcess) { _desktops = desktops; _forbiddenProcess = forbiddenProcess; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Placement
    {
        public int Length, Flags, ShowCommand;
        public Point Minimum, Maximum;
        public Rect Normal;
    }
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetPropW(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetPropW(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemovePropW(IntPtr window, string name);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetLong64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetLong32(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetLong64(IntPtr window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetLong32(IntPtr window, int index, int value);
    private static long Style(IntPtr window, int index) => IntPtr.Size == 8 ? GetLong64(window, index).ToInt64() : GetLong32(window, index);
    private static bool SetStyle(IntPtr window, int index, long value)
    {
        if (IntPtr.Size == 8) SetLong64(window, index, (nint)value); else SetLong32(window, index, unchecked((int)value));
        return Style(window, index) == value;
    }
    private static string Property(RecoveryRecord record) => "StreamGuard.Recovery." + record.Id.ToString("N");
    private RecoveryIdentity? Identity(IntPtr window)
    {
        if (!IsWindow(window)) return null;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == _forbiddenProcess || !ProcessSafetyService.Default.TryGetManageableProcess(unchecked((int)pid), out var safe) || safe == null) return null;
        using var process = Process.GetProcessById(safe.ProcessId);
        using var current = Process.GetCurrentProcess();
        if (safe.SessionId != current.SessionId) return null;
        return new(window.ToInt64(), safe.ProcessId, process.StartTime.ToUniversalTime().Ticks,
            safe.SessionId, safe.Name, safe.ExecutablePath);
    }
    private bool Matches(RecoveryIdentity identity)
    {
        if (identity == null || identity.Window == 0 || identity.ProcessStartedUtcTicks <= 0 || identity.SessionId <= 0 ||
            string.IsNullOrWhiteSpace(identity.ExecutablePath) || !Path.IsPathFullyQualified(identity.ExecutablePath)) return false;
        var current = Identity((IntPtr)identity.Window);
        return current != null && current.ProcessId == identity.ProcessId && current.ProcessStartedUtcTicks == identity.ProcessStartedUtcTicks &&
            current.SessionId == identity.SessionId && current.ProcessName.Equals(identity.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFullPath(current.ExecutablePath).Equals(Path.GetFullPath(identity.ExecutablePath), StringComparison.OrdinalIgnoreCase);
    }
    public bool TryCapture(IntPtr window, out RecoverySnapshot snapshot, out string error)
    {
        snapshot = new(); error = "The original window identity or layout could not be verified.";
        try
        {
            var identity = Identity(window);
            if (identity == null || (Style(window, -16) & 0x40000000) != 0) return false;
            var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
            if (!GetWindowPlacement(window, ref placement)) return false;
            var rectangle = placement.Normal;
            if (!ValidRectangle(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom)) return false;
            var parent = GetAncestor(window, 1);
            if (parent == GetDesktopWindow()) parent = IntPtr.Zero;
            var parentIdentity = parent == IntPtr.Zero ? null : Identity(parent);
            if (parent != IntPtr.Zero && parentIdentity == null) return false;
            var owner = GetWindow(window, 4);
            var ownerIdentity = owner == IntPtr.Zero ? null : Identity(owner);
            if (owner != IntPtr.Zero && ownerIdentity == null) return false;
            var text = new StringBuilder(513); GetWindowTextW(window, text, text.Capacity);
            snapshot = new()
            {
                Identity = identity, Parent = parentIdentity, Owner = ownerIdentity, WindowTitle = text.ToString(),
                Style = Style(window, -16), ExStyle = Style(window, -20),
                Left = rectangle.Left, Top = rectangle.Top, Right = rectangle.Right, Bottom = rectangle.Bottom,
                ShowCommand = placement.ShowCommand, DesktopId = _desktops.GetWindowDesktopId(window)
            };
            error = ""; return true;
        }
        catch { return false; }
    }
    private static bool ValidRectangle(int left, int top, int right, int bottom) =>
        (long)right - left is > 0 and <= 100000 && (long)bottom - top is > 0 and <= 100000;

    public bool TryTag(RecoveryRecord record, out string error)
    {
        error = "A recovery marker could not be attached to the original window.";
        try
        {
            if (!Matches(record.Snapshot.Identity) || (record.Snapshot.Parent != null && !Matches(record.Snapshot.Parent)) ||
                (record.Snapshot.Owner != null && !Matches(record.Snapshot.Owner))) return false;
            var name = Property(record); var value = (IntPtr)record.Token;
            if (!SetPropW((IntPtr)record.Snapshot.Identity.Window, name, value)) return false;
            if (record.Snapshot.Parent != null && !SetPropW((IntPtr)record.Snapshot.Parent.Window, name, value))
            { RemoveTag(record); return false; }
            if (record.Snapshot.Owner != null && !SetPropW((IntPtr)record.Snapshot.Owner.Window, name, value))
            { RemoveTag(record); return false; }
            error = ""; return true;
        }
        catch { RemoveTag(record); return false; }
    }
    public void RemoveTag(RecoveryRecord record)
    {
        foreach (var identity in new[] { record.Snapshot.Identity, record.Snapshot.Parent, record.Snapshot.Owner }.OfType<RecoveryIdentity>())
        {
            try
            {
                if (Matches(identity) && GetPropW((IntPtr)identity.Window, Property(record)).ToInt64() == record.Token)
                    RemovePropW((IntPtr)identity.Window, Property(record));
            }
            catch { }
        }
    }
    public RecoveryCheck Validate(RecoveryRecord record)
    {
        try
        {
            var snapshot = record.Snapshot;
            if (!IsWindow((IntPtr)snapshot.Identity.Window)) return new(RecoveryValidation.Closed);
            if (record.Id == Guid.Empty || record.Token <= 0 || !Matches(snapshot.Identity) ||
                GetPropW((IntPtr)snapshot.Identity.Window, Property(record)).ToInt64() != record.Token ||
                (snapshot.Style & 0x40000000) != 0 ||
                !ValidRectangle(snapshot.Left, snapshot.Top, snapshot.Right, snapshot.Bottom) ||
                (snapshot.Parent != null && (!Matches(snapshot.Parent) || GetPropW((IntPtr)snapshot.Parent.Window, Property(record)).ToInt64() != record.Token)) ||
                (snapshot.Owner != null && (!Matches(snapshot.Owner) || GetPropW((IntPtr)snapshot.Owner.Window, Property(record)).ToInt64() != record.Token)))
                return new(RecoveryValidation.Unverified, "The original process, window marker or parent no longer matches. No action was taken.");
            return new(RecoveryValidation.Valid);
        }
        catch { return new(RecoveryValidation.Unverified, "The original process or window could not be verified. No action was taken."); }
    }
    public bool TryRestore(RecoveryRecord record, out string error)
    {
        error = "Windows could not restore this window.";
        try
        {
            var check = Validate(record);
            if (check.State != RecoveryValidation.Valid) { error = check.Detail; return false; }
            var snapshot = record.Snapshot; var window = (IntPtr)snapshot.Identity.Window;
            var parent = snapshot.Parent == null ? IntPtr.Zero : (IntPtr)snapshot.Parent.Window;
            var expectedParent = parent == IntPtr.Zero ? GetDesktopWindow() : parent;
            if (GetAncestor(window, 1) != expectedParent) SetParent(window, parent);
            if (GetAncestor(window, 1) != expectedParent) { error = "The original window parent could not be restored."; return false; }
            if (!SetStyle(window, -16, snapshot.Style) || !SetStyle(window, -20, snapshot.ExStyle))
            { error = "The original window styles could not be restored."; return false; }
            var owner = snapshot.Owner == null ? IntPtr.Zero : (IntPtr)snapshot.Owner.Window;
            if (GetWindow(window, 4) != owner)
            {
                if (IntPtr.Size == 8) SetLong64(window, -8, owner); else SetLong32(window, -8, owner.ToInt32());
                if (GetWindow(window, 4) != owner) { error = "The original window parent could not be restored."; return false; }
            }
            if (snapshot.DesktopId.HasValue && snapshot.DesktopId != Guid.Empty &&
                _desktops.GetWindowDesktopId(window) != snapshot.DesktopId && !_desktops.MoveToDesktop(window, snapshot.DesktopId.Value))
            { error = "The original desktop is unavailable. Recovery information has been retained."; return false; }
            // Apply the restored non-client frame and topmost state after detaching a hosted window / moving back to desktop.
            if (!SetWindowPos(window, new IntPtr((snapshot.ExStyle & 0x8) != 0 ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020 | 0x0040))
            { error = "The original window frame could not be restored."; return false; }
            var placement = new Placement
            {
                Length = Marshal.SizeOf<Placement>(), ShowCommand = snapshot.ShowCommand == 3 ? 3 : 4,
                Normal = new Rect { Left = snapshot.Left, Top = snapshot.Top, Right = snapshot.Right, Bottom = snapshot.Bottom }
            };
            if (!SetWindowPlacement(window, ref placement))
            { error = "The window layout or visibility could not be restored."; return false; }
            ShowWindow(window, snapshot.ShowCommand == 3 ? 3 : 5);
            if (!IsWindowVisible(window))
            { error = "The window layout or visibility could not be restored."; return false; }

            // Force Chromium/Electron/GPU-composited windows (e.g. Antigravity) to rebind and repaint their
            // DirectComposition/D3D11 swapchain after detaching from wrapper host or returning from hidden desktop.
            if (!IsZoomed(window) && GetWindowRect(window, out var currentRect))
            {
                int w = currentRect.Right - currentRect.Left;
                int h = currentRect.Bottom - currentRect.Top;
                if (w > 2 && h > 2)
                {
                    const uint flags = 0x0004 /* SWP_NOZORDER */ | 0x0010 /* SWP_NOACTIVATE */ | 0x0020 /* SWP_FRAMECHANGED */ | 0x0040 /* SWP_SHOWWINDOW */;
                    SetWindowPos(window, IntPtr.Zero, currentRect.Left, currentRect.Top, w - 1, h, flags);
                    SetWindowPos(window, IntPtr.Zero, currentRect.Left, currentRect.Top, w, h, flags);
                }
            }
            else
            {
                SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020 | 0x0040);
            }
            RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
            error = ""; return true;
        }
        catch { return false; }
    }
}
