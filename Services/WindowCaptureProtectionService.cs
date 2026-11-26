using System.Runtime.InteropServices;

namespace StreamGuard.Services;

public enum CaptureAffinityMode { Unverified, Excluded, MonitorOnly }
public sealed record CaptureAffinityResult(CaptureAffinityMode Mode, int ErrorCode = 0);

public interface IWindowCaptureApi
{
    bool TryGet(IntPtr window, out uint affinity, out int error);
    bool TrySet(IntPtr window, uint affinity, out int error);
}

public sealed class WindowCaptureProtectionService
{
    private const uint Monitor = 0x01;
    private const uint Exclude = 0x11;
    private readonly IWindowCaptureApi _api;
    private readonly bool _supportsExclusion;

    public WindowCaptureProtectionService(IWindowCaptureApi? api = null, bool? supportsExclusion = null)
    {
        _api = api ?? new NativeApi();
        _supportsExclusion = supportsExclusion ?? OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
    }

    public CaptureAffinityResult ApplyAndVerify(IntPtr window)
    {
        if (window == IntPtr.Zero) return new(CaptureAffinityMode.Unverified);
        try
        {
            var requested = _supportsExclusion ? Exclude : Monitor;
            if (!_api.TryGet(window, out var current, out _) || current != requested)
            {
                if (!_api.TrySet(window, requested, out var error)) return new(CaptureAffinityMode.Unverified, error);
            }
            if (!_api.TryGet(window, out var actual, out var readError)) return new(CaptureAffinityMode.Unverified, readError);
            return actual switch
            {
                Exclude when _supportsExclusion => new(CaptureAffinityMode.Excluded),
                Monitor => new(CaptureAffinityMode.MonitorOnly),
                _ => new(CaptureAffinityMode.Unverified)
            };
        }
        catch { return new(CaptureAffinityMode.Unverified); }
    }

    private sealed class NativeApi : IWindowCaptureApi
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        public bool TryGet(IntPtr window, out uint affinity, out int error)
        {
            var ok = GetWindowDisplayAffinity(window, out affinity);
            error = ok ? 0 : Marshal.GetLastWin32Error();
            return ok;
        }
        public bool TrySet(IntPtr window, uint affinity, out int error)
        {
            var ok = SetWindowDisplayAffinity(window, affinity);
            error = ok ? 0 : Marshal.GetLastWin32Error();
            return ok;
        }
    }
}
