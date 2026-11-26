using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows;
using StreamGuard.Models;
using StreamGuard.Services;
using StreamGuard.Views;

namespace StreamGuard.Tests
{
    internal static class SnippingChecks
    {
        public static void Run(Action<bool, string> check, string tempDir)
        {
            TestDefaultDirectory(check);
            TestRegionCropping(check, tempDir);
            TestNegativeIntersectionCropping(check, tempDir);
            TestConfigRoundtrip(check, tempDir);
            TestSnippingHotkeyConflictAndResolution(check, tempDir);
            TestProtectionAndOverlay(check);
            TestOverlayAbnormalCloseLifecycle(check);
            TestDpiCoordinateConversion(check);
            TestSnapshotBackgroundBinding(check);
            TestEscapeHookAndNoActivateProtection(check);
        }

        private static void TestDefaultDirectory(Action<bool, string> check)
        {
            var defaultDir = SnippingToolService.DefaultScreenshotDirectory;
            check(!string.IsNullOrWhiteSpace(defaultDir), "Default screenshot directory should not be empty");
            check(Path.IsPathRooted(defaultDir), "Default screenshot directory should be an absolute path");
            check(defaultDir.Contains("DriverBooster_Screenshots"), "Default screenshot directory should end with DriverBooster_Screenshots");
        }

        private static void TestRegionCropping(Action<bool, string> check, string tempDir)
        {
            var saveDir = Path.Combine(tempDir, "Screenshots");
            using var sample = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sample))
            {
                g.Clear(System.Drawing.Color.MediumPurple);
                using var brush = new SolidBrush(System.Drawing.Color.Gold);
                g.FillRectangle(brush, 50, 50, 150, 100);
            }

            // Normal valid crop
            var cropRect = new System.Drawing.Rectangle(50, 50, 150, 100);
            var result = SnippingToolService.ProcessSelectedRegion(sample, cropRect, saveDir, autoCopyClipboard: false);

            check(result.Success, "Valid region crop should succeed");
            check(result.Width == 150 && result.Height == 100, $"Crop dimensions match: expected 150x100, got {result.Width}x{result.Height}");
            check(result.FilePath != null && File.Exists(result.FilePath), "Saved screenshot file should exist on disk");
            check(new FileInfo(result.FilePath!).Length > 0, "Saved PNG file should have non-zero length");

            // Clamped crop (exceeding bitmap boundary)
            var outOfBoundsRect = new System.Drawing.Rectangle(350, 250, 100, 100);
            var clampedResult = SnippingToolService.ProcessSelectedRegion(sample, outOfBoundsRect, saveDir, autoCopyClipboard: false);
            check(clampedResult.Success, "Out-of-bounds crop should clamp and succeed");
            check(clampedResult.Width == 50 && clampedResult.Height == 50, $"Clamped dimensions: expected 50x50, got {clampedResult.Width}x{clampedResult.Height}");

            // Tiny / invalid crop (< 4px)
            var tinyRect = new System.Drawing.Rectangle(10, 10, 2, 2);
            var tinyResult = SnippingToolService.ProcessSelectedRegion(sample, tinyRect, saveDir, autoCopyClipboard: false);
            check(!tinyResult.Success, "Sub-4px crop should be rejected as too small");
        }

        private static void TestNegativeIntersectionCropping(Action<bool, string> check, string tempDir)
        {
            var saveDir = Path.Combine(tempDir, "ScreenshotsNegative");
            using var sample = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sample))
            {
                g.Clear(System.Drawing.Color.DodgerBlue);
            }

            // Negative start that overlaps with screen: (-50, -30) to (100, 70) => expected intersection is (0, 0, 100, 70)
            var overlapRect = new System.Drawing.Rectangle(-50, -30, 150, 100);
            var overlapResult = SnippingToolService.ProcessSelectedRegion(sample, overlapRect, saveDir, autoCopyClipboard: false);
            check(overlapResult.Success, "Overlap crop with negative coordinates should succeed via Intersect");
            check(overlapResult.Width == 100 && overlapResult.Height == 70, $"Expected intersection dimensions 100x70, got {overlapResult.Width}x{overlapResult.Height}");

            // Completely outside the bitmap (-100, -100, 50, 50) => empty intersection
            var outsideRect = new System.Drawing.Rectangle(-100, -100, 50, 50);
            var outsideResult = SnippingToolService.ProcessSelectedRegion(sample, outsideRect, saveDir, autoCopyClipboard: false);
            check(!outsideResult.Success, "Completely outside crop should be rejected");
        }

        private static void TestConfigRoundtrip(Action<bool, string> check, string tempDir)
        {
            var configPath = Path.Combine(tempDir, "snipping_config.json");
            var configService = new ConfigService(configPath);
            check(configService.SnippingHotkey == "Ctrl+Alt+S", "Default snipping hotkey in ConfigService must be Ctrl+Alt+S");

            var customDir = Path.Combine(tempDir, "CustomCaptures");
            var apps = new[] { new GuardedApp { Name = "TestApp", ProcessName = "test", ExecutablePath = "test.exe" } };

            var saved = configService.Save(
                apps,
                screenshotDirectory: customDir,
                screenshotAutoCopyClipboard: false,
                screenshotExcludeFromCapture: true,
                snippingHotkey: "Ctrl+Alt+P");

            check(saved, "ConfigService.Save with screenshot options should succeed");

            var reloader = new ConfigService(configPath);
            reloader.Load();

            check(reloader.ScreenshotDirectory == customDir, $"ScreenshotDirectory persisted: expected {customDir}, got {reloader.ScreenshotDirectory}");
            check(reloader.ScreenshotAutoCopyClipboard == false, "ScreenshotAutoCopyClipboard persisted as false");
            check(reloader.ScreenshotExcludeFromCapture == true, "ScreenshotExcludeFromCapture persisted as true");
            check(reloader.SnippingHotkey == "Ctrl+Alt+P", $"SnippingHotkey persisted: expected Ctrl+Alt+P, got {reloader.SnippingHotkey}");

            // Test corrupt or empty hotkey defaults back to Ctrl+Alt+S
            var fallbackConfig = new ConfigService(configPath);
            fallbackConfig.Save(apps, snippingHotkey: "");
            var reloadedFallback = new ConfigService(configPath);
            reloadedFallback.Load();
            check(reloadedFallback.SnippingHotkey == "Ctrl+Alt+S", "Empty snipping hotkey in config defaults back to Ctrl+Alt+S");
        }

        private static void TestSnippingHotkeyConflictAndResolution(Action<bool, string> check, string tempDir)
        {
            var mainWindowType = typeof(MainWindow);
            var isReservedMethod = mainWindowType.GetMethod("IsReservedSystemHotkey", BindingFlags.Static | BindingFlags.NonPublic)!;

            // Reset current snipping hotkey to default Ctrl+Alt+S for test stability
            var currentSnipField = mainWindowType.GetField("_currentSnippingHotkey", BindingFlags.Static | BindingFlags.NonPublic);
            currentSnipField?.SetValue(null, "Ctrl+Alt+S");

            // 1. Ctrl+Shift+S must NO LONGER be a reserved system hotkey (freeing Save All in other apps)
            var ctrlShiftSReserved = (bool)isReservedMethod.Invoke(null, new object[] { "Ctrl+Shift+S" })!;
            check(!ctrlShiftSReserved, "Ctrl+Shift+S is not reserved for StreamGuard, freeing Save All for IDEs and office software");

            // 2. Ctrl+Alt+S IS reserved as the default snipping hotkey so guarded apps cannot steal it
            var ctrlAltSReserved = (bool)isReservedMethod.Invoke(null, new object[] { "Ctrl+Alt+S" })!;
            check(ctrlAltSReserved, "Ctrl+Alt+S is recognized as a reserved system hotkey when configured as snipping shortcut");

            // 3. Core recovery hotkeys must remain reserved
            var ctrlShiftGReserved = (bool)isReservedMethod.Invoke(null, new object[] { "Ctrl+Shift+G" })!;
            check(ctrlShiftGReserved, "Ctrl+Shift+G (ToggleVisibility) remains reserved");

            var shiftSgChordReserved = (bool)isReservedMethod.Invoke(null, new object[] { "Shift+S+G" })!;
            check(shiftSgChordReserved, "Shift+S+G recovery chord remains reserved");

            var ctrlShiftSgChordReserved = (bool)isReservedMethod.Invoke(null, new object[] { "Ctrl+Shift+S+G" })!;
            check(ctrlShiftSgChordReserved, "Ctrl+Shift+S+G kill chord remains reserved");

            // 4. TryParseHotkey handles standard and Win modifiers cleanly
            var tryParseMethod = mainWindowType.GetMethod("TryParseHotkey", BindingFlags.Static | BindingFlags.NonPublic)!;
            var args1 = new object?[] { "Ctrl+Alt+S", null, null, null, null };
            var ok1 = (bool)tryParseMethod.Invoke(null, args1)!;
            check(ok1 && (uint)args1[1]! == (HotkeyService.MOD_CTRL | HotkeyService.MOD_ALT) && (uint)args1[2]! == 0x53 && (string)args1[3]! == "Ctrl+Alt+S",
                "TryParseHotkey parses Ctrl+Alt+S correctly");

            var args2 = new object?[] { "Win+Alt+S", null, null, null, null };
            var ok2 = (bool)tryParseMethod.Invoke(null, args2)!;
            check(ok2 && (uint)args2[1]! == (HotkeyService.MOD_WIN | HotkeyService.MOD_ALT) && (uint)args2[2]! == 0x53 && (string)args2[3]! == "Win+Alt+S",
                "TryParseHotkey parses Win+Alt+S correctly with MOD_WIN");

            var args3 = new object?[] { "ctrl+alt+s", null, null, null, null };
            var ok3 = (bool)tryParseMethod.Invoke(null, args3)!;
            check(ok3 && (string)args3[3]! == "Ctrl+Alt+S", "TryParseHotkey normalizes casing to Ctrl+Alt+S");

            var args4 = new object?[] { "Win+Alt+F7", null, null, null, null };
            var ok4 = (bool)tryParseMethod.Invoke(null, args4)!;
            check(ok4 && (uint)args4[1]! == (HotkeyService.MOD_WIN | HotkeyService.MOD_ALT) && (string)args4[3]! == "Win+Alt+F7",
                "TryParseHotkey parses function key combinations like Win+Alt+F7");

            var args5 = new object?[] { "Ctrl", null, null, null, null };
            var ok5 = (bool)tryParseMethod.Invoke(null, args5)!;
            check(!ok5, "TryParseHotkey rejects modifier without key");
        }

        private static void TestProtectionAndOverlay(Action<bool, string> check)
        {
            var service = new WindowCaptureProtectionService();
            var nullResult = service.ApplyAndVerify(IntPtr.Zero);
            check(nullResult.Mode == CaptureAffinityMode.Unverified, "Null window handle returns Unverified safely");

            // Test overlay window instantiation
            using var dummyBmp = new Bitmap(10, 10);
            var overlay = new SnippingOverlayWindow(dummyBmp, 0, 0, 100, 100, excludeFromCapture: false);
            check(overlay.Topmost, "Overlay window should be Topmost");
            check(overlay.AllowsTransparency, "Overlay window should allow transparency");
            check(overlay.WindowStyle == System.Windows.WindowStyle.None, "Overlay window should have no standard chrome");
        }

        private static void TestOverlayAbnormalCloseLifecycle(Action<bool, string> check)
        {
            using var dummyBmp = new Bitmap(10, 10);
            var overlay = new SnippingOverlayWindow(dummyBmp, 0, 0, 100, 100, excludeFromCapture: false);
            int cancelCount = 0;
            overlay.Canceled += () => cancelCount++;

            // Simulate abnormal window close (like Alt+F4 or OS close)
            overlay.Close();

            check(cancelCount == 1, $"Canceled event fired exactly once on abnormal close: actual={cancelCount}");

            // Second close must not fire again
            overlay.Close();
            check(cancelCount == 1, "Canceled event does not fire repeatedly on multiple Close calls");
        }

        private static void TestDpiCoordinateConversion(Action<bool, string> check)
        {
            // 1. Scale 100% (1.0x)
            var bounds100 = SnippingCoordinateHelper.CalculateOverlayBounds(0, 0, 1920, 1080, 1.0, 1.0);
            check(bounds100.Left == 0 && bounds100.Top == 0 && bounds100.Width == 1920 && bounds100.Height == 1080,
                $"DPI 100% overlay bounds: expected (0,0,1920,1080), got {bounds100}");

            var phys100 = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(new Rect(100, 150, 800, 600), 1.0, 1.0);
            check(phys100.X == 100 && phys100.Y == 150 && phys100.Width == 800 && phys100.Height == 600,
                $"DPI 100% DIP to physical: expected (100,150,800,600), got {phys100}");

            var dips100 = SnippingCoordinateHelper.ConvertPhysicalPixelsToDips(new Rectangle(100, 150, 800, 600), 1.0, 1.0);
            check(dips100.X == 100 && dips100.Y == 150 && dips100.Width == 800 && dips100.Height == 600,
                $"DPI 100% physical to DIP: expected (100,150,800,600), got {dips100}");

            // 2. Scale 125% (1.25x)
            var bounds125 = SnippingCoordinateHelper.CalculateOverlayBounds(0, 0, 1920, 1080, 1.25, 1.25);
            check(bounds125.Left == 0 && bounds125.Top == 0 && Math.Abs(bounds125.Width - 1536) < 0.001 && Math.Abs(bounds125.Height - 864) < 0.001,
                $"DPI 125% overlay bounds: expected (0,0,1536,864), got {bounds125}");

            var phys125 = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(new Rect(80, 40, 320, 240), 1.25, 1.25);
            check(phys125.X == 100 && phys125.Y == 50 && phys125.Width == 400 && phys125.Height == 300,
                $"DPI 125% DIP to physical: expected (100,50,400,300), got {phys125}");

            // Non-integer DIPs with rounding at 125%
            var phys125Round = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(new Rect(10.4, 20.8, 40.4, 60.8), 1.25, 1.25);
            check(phys125Round.X == 13 && phys125Round.Y == 26 && phys125Round.Height == 76,
                $"DPI 125% rounded conversion: got {phys125Round}");

            // 3. Scale 150% (1.50x)
            var bounds150 = SnippingCoordinateHelper.CalculateOverlayBounds(0, 0, 1920, 1080, 1.5, 1.5);
            check(bounds150.Left == 0 && bounds150.Top == 0 && Math.Abs(bounds150.Width - 1280) < 0.001 && Math.Abs(bounds150.Height - 720) < 0.001,
                $"DPI 150% overlay bounds: expected (0,0,1280,720), got {bounds150}");

            var phys150 = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(new Rect(100, 100, 400, 300), 1.5, 1.5);
            check(phys150.X == 150 && phys150.Y == 150 && phys150.Width == 600 && phys150.Height == 450,
                $"DPI 150% DIP to physical: expected (150,150,600,450), got {phys150}");

            // Multi-monitor with negative virtual origin at 150%
            var multiBounds150 = SnippingCoordinateHelper.CalculateOverlayBounds(-1920, 0, 3840, 1080, 1.5, 1.5);
            check(Math.Abs(multiBounds150.Left - (-1280)) < 0.001 && multiBounds150.Top == 0 &&
                  Math.Abs(multiBounds150.Width - 2560) < 0.001 && Math.Abs(multiBounds150.Height - 720) < 0.001,
                $"DPI 150% multi-monitor bounds: expected (-1280,0,2560,720), got {multiBounds150}");

            // 4. Scale 200% (2.00x)
            var bounds200 = SnippingCoordinateHelper.CalculateOverlayBounds(0, 0, 3840, 2160, 2.0, 2.0);
            check(bounds200.Left == 0 && bounds200.Top == 0 && Math.Abs(bounds200.Width - 1920) < 0.001 && Math.Abs(bounds200.Height - 1080) < 0.001,
                $"DPI 200% overlay bounds: expected (0,0,1920,1080), got {bounds200}");

            var phys200 = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(new Rect(50, 25, 200, 150), 2.0, 2.0);
            check(phys200.X == 100 && phys200.Y == 50 && phys200.Width == 400 && phys200.Height == 300,
                $"DPI 200% DIP to physical: expected (100,50,400,300), got {phys200}");

            // 5. Scale edge cases (zero/negative scales fallback to 1.0 safely)
            var boundsZeroScale = SnippingCoordinateHelper.CalculateOverlayBounds(0, 0, 1920, 1080, 0.0, -1.0);
            check(boundsZeroScale.Width == 1920 && boundsZeroScale.Height == 1080,
                $"Zero/negative scale safe fallback: got {boundsZeroScale}");

            var physNegativeScale = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(new Rect(10, 10, 50, 50), -0.5, 0);
            check(physNegativeScale.Width == 50 && physNegativeScale.Height == 50,
                $"Negative scale safe fallback: got {physNegativeScale}");
        }

        private static void TestSnapshotBackgroundBinding(Action<bool, string> check)
        {
            // 1. Create a sample 32bppRgb bitmap (screen snapshot native format) and test ConvertBitmapToBitmapSource
            using var sampleRgb = new Bitmap(640, 480, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(sampleRgb))
            {
                g.Clear(System.Drawing.Color.SteelBlue);
                using var brush = new SolidBrush(System.Drawing.Color.Crimson);
                g.FillRectangle(brush, 10, 10, 100, 100);
            }

            var wpfSourceRgb = SnippingToolService.ConvertBitmapToBitmapSource(sampleRgb);
            check(wpfSourceRgb != null, "ConvertBitmapToBitmapSource should succeed for Format32bppRgb Bitmap");
            check(wpfSourceRgb!.PixelWidth == 640 && wpfSourceRgb.PixelHeight == 480,
                $"Format32bppRgb BitmapSource pixel dimensions: expected 640x480, got {wpfSourceRgb.PixelWidth}x{wpfSourceRgb.PixelHeight}");
            check(wpfSourceRgb.IsFrozen, "Format32bppRgb BitmapSource should be frozen for thread safety and immutability");

            // Also verify Format32bppArgb sample bitmap
            using var sample = new Bitmap(640, 480, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sample))
            {
                g.Clear(System.Drawing.Color.ForestGreen);
                using var brush = new SolidBrush(System.Drawing.Color.Goldenrod);
                g.FillRectangle(brush, 20, 20, 80, 80);
            }

            var wpfSource = SnippingToolService.ConvertBitmapToBitmapSource(sample);
            check(wpfSource != null, "ConvertBitmapToBitmapSource should succeed for valid Bitmap");
            check(wpfSource!.PixelWidth == 640 && wpfSource.PixelHeight == 480,
                $"BitmapSource pixel dimensions: expected 640x480, got {wpfSource.PixelWidth}x{wpfSource.PixelHeight}");
            check(wpfSource.IsFrozen, "BitmapSource should be frozen for thread safety and immutability");

            // 2. Instantiate SnippingOverlayWindow with snapshot bitmap and verify binding
            var overlay = new SnippingOverlayWindow(sampleRgb, 0, 0, 640, 480, excludeFromCapture: false);
            try
            {
                check(overlay.SnapshotSource != null, "Overlay SnapshotSource should be bound");
                check(overlay.SnapshotSource!.PixelWidth == 640 && overlay.SnapshotSource.PixelHeight == 480,
                    $"Overlay SnapshotSource dimensions: expected 640x480, got {overlay.SnapshotSource.PixelWidth}x{overlay.SnapshotSource.PixelHeight}");
                check(overlay.SnapshotSource.IsFrozen, "Overlay SnapshotSource should be frozen");
                check(overlay.BackgroundBitmap == sampleRgb, "Overlay BackgroundBitmap references source bitmap");
                check(overlay.VirtualLeft == 0 && overlay.VirtualTop == 0 && overlay.VirtualWidth == 640 && overlay.VirtualHeight == 480,
                    "Overlay virtual bounds properties stored accurately");
            }
            finally
            {
                overlay.Close();
            }

            // 3. Null and edge case handling
            var nullSource = SnippingToolService.ConvertBitmapToBitmapSource(null);
            check(nullSource == null, "ConvertBitmapToBitmapSource returns null for null bitmap without throwing");

            var nullOverlay = new SnippingOverlayWindow(null, 0, 0, 800, 600, excludeFromCapture: false);
            try
            {
                check(nullOverlay.SnapshotSource == null, "Overlay with null snapshot safely has null SnapshotSource");
            }
            finally
            {
                nullOverlay.Close();
            }
        }

        private static void TestEscapeHookAndNoActivateProtection(Action<bool, string> check)
        {
            using var dummyBmp = new Bitmap(10, 10);
            var overlay = new SnippingOverlayWindow(dummyBmp, 0, 0, 200, 200, excludeFromCapture: true);
            bool canceled = false;
            overlay.Canceled += () => canceled = true;

            try
            {
                check(!overlay.ShowActivated, "SnippingOverlayWindow ShowActivated must be false to protect Chrome fullscreen focus");
                check(!overlay.Focusable, "SnippingOverlayWindow Focusable must be false to avoid stealing focus");
                check(overlay.Topmost, "SnippingOverlayWindow Topmost must be true to cover fullscreen Chrome");

                const int WM_KEYDOWN = 0x0100;
                const int WM_KEYUP = 0x0101;
                const int VK_ESCAPE = 0x1B;
                const int VK_A = 0x41;

                // Non-Escape key must not be swallowed
                check(!overlay.HandleLowLevelEscapeKey(WM_KEYDOWN, VK_A), "Non-Escape key is not swallowed by SnippingOverlayWindow");

                // Escape keydown must be swallowed so Chrome never exits fullscreen
                check(overlay.HandleLowLevelEscapeKey(WM_KEYDOWN, VK_ESCAPE), "Escape WM_KEYDOWN is swallowed by SnippingOverlayWindow");

                // Matching Escape keyup must also be swallowed
                check(overlay.HandleLowLevelEscapeKey(WM_KEYUP, VK_ESCAPE), "Matching Escape WM_KEYUP is swallowed by SnippingOverlayWindow");

                // Pump dispatcher so BeginInvoke(Cancel) executes
                var frame = new System.Windows.Threading.DispatcherFrame();
                overlay.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);

                check(canceled, "Swallowed Escape key cancels SnippingOverlayWindow cleanly");
                check(!overlay.HandleLowLevelEscapeKey(WM_KEYDOWN, VK_ESCAPE), "After cancel/completion, Escape hook stops swallowing keys");
            }
            finally
            {
                overlay.Close();
            }
        }
    }
}
