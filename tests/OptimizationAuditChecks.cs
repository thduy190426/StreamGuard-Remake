using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media;
using StreamGuard.Controls;
using StreamGuard.Helpers;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

public static class OptimizationAuditChecks
{
    public static void Run(Action<bool, string> check)
    {
        // 1. Fast Win32 executable query replaces Process.MainModule enumeration
        var selfPath = WindowEnumerator.TryGetExecutablePathFast(Environment.ProcessId);
        check(!string.IsNullOrWhiteSpace(selfPath) && selfPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
            $"TryGetExecutablePathFast resolves current test executable path: {selfPath}");
        check(WindowEnumerator.TryGetExecutablePathFast(-1) == "", "TryGetExecutablePathFast handles invalid negative PID safely");

        var snapshot = WindowEnumerator.GetOpenWindows();
        var filtered = WindowEnumerator.FindAllIncludingHiddenByProcessName("nonexistent-audit-process-xyz", snapshot);
        check(filtered.Count == 0, "FindAllIncludingHiddenByProcessName honors shared openWindowsSnapshot cleanly");

        // 2. O(W*H) Sliding-Window Box Blur correctness (uniform preservation + radius > dimension edge clamping)
        using (var uniform = new Bitmap(12, 10, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(uniform))
                g.Clear(System.Drawing.Color.FromArgb(255, 42, 128, 210));

            using var blurred = ScreenshotImageProcessor.ApplyFullBlur(uniform, strength: 32, pixelate: false);
            var corner = blurred.GetPixel(0, 0);
            var center = blurred.GetPixel(6, 5);
            var edge = blurred.GetPixel(11, 9);
            check(corner.R == 42 && corner.G == 128 && corner.B == 210 && corner.A == 255,
                $"Sliding-window blur preserves uniform color at corner even when radius > width ({corner})");
            check(center.R == 42 && center.G == 128 && center.B == 210 && edge.B == 210,
                "Sliding-window blur preserves uniform color across center and trailing edge");
        }

        // 3. Sliding-window blur spatial diffusion + high-res latency bound (1920x1080 @ max strength 32)
        using (var big = new Bitmap(1920, 1080, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(big))
            {
                g.Clear(System.Drawing.Color.Black);
                g.FillRectangle(System.Drawing.Brushes.White, 900, 480, 120, 120);
            }

            var sw = Stopwatch.StartNew();
            using var blurredBig = ScreenshotImageProcessor.ApplyFullBlur(big, strength: 32, pixelate: false);
            sw.Stop();
            var blurredEdge = blurredBig.GetPixel(896, 540);
            check(blurredEdge.R > 0 && blurredEdge.R < 255, $"Sliding-window blur diffuses step edge smoothly ({blurredEdge.R})");
            check(sw.ElapsedMilliseconds < 1500, $"1920x1080 max-radius O(W*H) blur finishes rapidly ({sw.ElapsedMilliseconds} ms)");
        }

        // 4. ToastCard frozen badge brush and repeat dismissal cleanup
        var card = new ToastCard(new ToastNotification("Audit repeat test", ToastType.Info, 2000) { RepeatCount = 3 });
        card.UpdateRepeat(4, 1500);
        check(card.RepeatCount == 4, "ToastCard RepeatCount increments cleanly");
        card.Dismiss(animated: false);
        check(card.IsDismissed, "ToastCard Dismiss(animated: false) marks card dismissed cleanly");
    }
}
