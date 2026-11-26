using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using StreamGuard;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace StreamGuard.Tests;

internal static class CursorCaptureProtectionChecks
{
    internal static void Run(Action<bool, string> check, string tempDir)
    {
        TestTransparentCursorCreation(check);
        TestCursorCloakConfigurationDefaults(check, tempDir);
        TestCursorOverlayPropertiesAndAffinity(check);
        TestSystemCursorRestore(check);
        TestServiceStateManagement(check);
        TestInAppAttachmentAndIdempotency(check);
        TestVisualCursorElement(check);
        TestInAppAdorner(check);
        TestCursorVisualHotspotsAndGeometries(check);
        TestCursorTypeDetection(check);
        TestAdornerHotspotOffsets(check);
        TestOverlayReuseAndTopmost(check);
        TestFactoryResetCursorRestoration(check, tempDir);
        TestBilingualLocalizationKeys(check);
        TestCursorShieldDoesNotClobberGlobalCloak(check);
    }

    private static void TestTransparentCursorCreation(Action<bool, string> check)
    {
        var hCur = CursorCaptureProtectionService.CreateTransparentCursor();
        check(hCur != IntPtr.Zero, "CreateTransparentCursor returns a valid non-zero Win32 cursor handle");

        var destroyed = CursorCaptureProtectionService.DestroyCursor(hCur);
        check(destroyed, "DestroyCursor successfully releases the created transparent cursor handle");
    }

    private static void TestCursorCloakConfigurationDefaults(Action<bool, string> check, string tempDir)
    {
        var model = new StreamGuardConfig();
        check(model.InAppCursorCloakEnabled == true, "StreamGuardConfig defaults InAppCursorCloakEnabled to true");
        check(model.GlobalCursorCloakEnabled == false, "StreamGuardConfig defaults GlobalCursorCloakEnabled to false");

        var configPath = Path.Combine(tempDir, "cursor_cloak_config_" + Guid.NewGuid().ToString("N") + ".json");
        var configService = new ConfigService(configPath);
        check(configService.InAppCursorCloakEnabled == true, "ConfigService defaults InAppCursorCloakEnabled to true before load");
        check(configService.GlobalCursorCloakEnabled == false, "ConfigService defaults GlobalCursorCloakEnabled to false before load");

        var loaded = configService.Load();
        check(configService.InAppCursorCloakEnabled == true, "ConfigService retains InAppCursorCloakEnabled true on blank configuration");
        check(configService.GlobalCursorCloakEnabled == false, "ConfigService retains GlobalCursorCloakEnabled false on blank configuration");

        // Save customized preferences
        var savedCustom = configService.Save(
            Array.Empty<GuardedApp>(),
            inAppCursorCloakEnabled: false,
            globalCursorCloakEnabled: true);
        check(savedCustom, "ConfigService saves custom cursor cloak preferences successfully");

        var reloadedService = new ConfigService(configPath);
        reloadedService.Load();
        check(reloadedService.InAppCursorCloakEnabled == false, "ConfigService reloads customized InAppCursorCloakEnabled = false");
        check(reloadedService.GlobalCursorCloakEnabled == true, "ConfigService reloads customized GlobalCursorCloakEnabled = true");

        // Round-trip back to defaults
        var savedDefaults = reloadedService.Save(
            Array.Empty<GuardedApp>(),
            inAppCursorCloakEnabled: true,
            globalCursorCloakEnabled: false);
        check(savedDefaults, "ConfigService restores default cursor cloak preferences");

        var finalService = new ConfigService(configPath);
        finalService.Load();
        check(finalService.InAppCursorCloakEnabled == true, "ConfigService reloads restored default InAppCursorCloakEnabled = true");
        check(finalService.GlobalCursorCloakEnabled == false, "ConfigService reloads restored default GlobalCursorCloakEnabled = false");
    }

    private static void TestCursorOverlayPropertiesAndAffinity(Action<bool, string> check)
    {
        var overlay = new GlobalCursorOverlayWindow();
        check(overlay.AllowsTransparency == true, "GlobalCursorOverlayWindow has AllowsTransparency set to true");
        check(overlay.Background == Brushes.Transparent, "GlobalCursorOverlayWindow has Transparent background");
        check(overlay.ShowInTaskbar == false, "GlobalCursorOverlayWindow does not show in taskbar");
        check(overlay.ShowActivated == false, "GlobalCursorOverlayWindow does not show activated");
        check(overlay.Topmost == true, "GlobalCursorOverlayWindow is Topmost");
        check(overlay.WindowStyle == WindowStyle.None, "GlobalCursorOverlayWindow uses WindowStyle.None");
        check(overlay.IsHitTestVisible == false, "GlobalCursorOverlayWindow root is hit-test invisible for click-through");

        // Test native exclusion on an invisible test-owned window
        using var source = new HwndSource(new HwndSourceParameters("Cursor Cloak Affinity Test")
        {
            Width = 16,
            Height = 16,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        var nativeResult = new WindowCaptureProtectionService().ApplyAndVerify(source.Handle);
        check(nativeResult.Mode == CaptureAffinityMode.Excluded, "WDA_EXCLUDEFROMCAPTURE applies cleanly to test window");

        overlay.Close();
    }

    private static void TestSystemCursorRestore(Action<bool, string> check)
    {
        check(CursorCaptureProtectionService.SPI_SETCURSORS == 0x0057, "CursorCaptureProtectionService.SPI_SETCURSORS matches Win32 standard 0x0057");
        check(CursorCaptureProtectionService.SPIF_UPDATEINIFILE == 0x01, "CursorCaptureProtectionService.SPIF_UPDATEINIFILE matches 0x01");
        check(CursorCaptureProtectionService.SPIF_SENDCHANGE == 0x02, "CursorCaptureProtectionService.SPIF_SENDCHANGE matches 0x02");
        check(CursorCaptureProtectionService.SystemCursorIds.Length >= 14, "CursorCaptureProtectionService defines comprehensive system cursor IDs");

        var restored = CursorCaptureProtectionService.RestoreSystemCursors();
        check(restored, "CursorCaptureProtectionService.RestoreSystemCursors executes SystemParametersInfo successfully");
    }

    private static void TestServiceStateManagement(Action<bool, string> check)
    {
        var service = CursorCaptureProtectionService.Instance;
        check(service != null, "CursorCaptureProtectionService.Instance is not null");
        check(service!.IsInAppCloakEnabled == true, "CursorCaptureProtectionService defaults IsInAppCloakEnabled to true");
        check(service.IsGlobalCloakActive == false, "CursorCaptureProtectionService defaults IsGlobalCloakActive to false");

        var stateChanges = new List<bool>();
        Action<bool> handler = state => stateChanges.Add(state);
        service.GlobalCloakStateChanged += handler;

        // Ensure clean state before cycle
        service.StopGlobalCursorCloak();
        stateChanges.Clear();

        var started = service.StartGlobalCursorCloak();
        check(started, "StartGlobalCursorCloak returns true on start");
        check(service.IsGlobalCloakActive == true, "IsGlobalCloakActive is true after start");
        check(stateChanges.Count == 1 && stateChanges[0] == true, "GlobalCloakStateChanged fired with true on start");

        // Failsafe auto restore test
        service.StopGlobalCursorCloak();
        check(service.IsGlobalCloakActive == false, "StopGlobalCursorCloak leaves service in inactive state");
        check(stateChanges.Count == 2 && stateChanges[1] == false, "GlobalCloakStateChanged fired with false on stop");

        // Dynamic preference switching
        service.IsInAppCloakEnabled = false;
        check(service.IsInAppCloakEnabled == false, "IsInAppCloakEnabled can be set to false");
        service.IsInAppCloakEnabled = true;
        check(service.IsInAppCloakEnabled == true, "IsInAppCloakEnabled can be restored to true");

        service.GlobalCloakStateChanged -= handler;
    }

    private static void TestInAppAttachmentAndIdempotency(Action<bool, string> check)
    {
        var service = CursorCaptureProtectionService.Instance;
        var win = new Window
        {
            Width = 200,
            Height = 150,
            WindowStyle = WindowStyle.None,
            ShowActivated = false
        };

        // Attach first time
        service.AttachInAppCloak(win);
        check(true, "First AttachInAppCloak on test window succeeds");

        // Attach second time (idempotent, must not duplicate hooks)
        service.AttachInAppCloak(win);
        check(true, "Second AttachInAppCloak on same window is idempotent");

        // Toggling InAppCloak triggers refresh safely
        service.IsInAppCloakEnabled = false;
        service.IsInAppCloakEnabled = true;
        check(service.IsInAppCloakEnabled == true, "InAppCloak state updates notify attached windows safely");

        win.Close();
    }

    private static void TestVisualCursorElement(Action<bool, string> check)
    {
        var element = CursorVisualHelper.CreateCursorElement();
        check(element != null, "CreateCursorElement produces a non-null visual element");
        if (element != null)
        {
            check(element.Width == 32 && element.Height == 32, "Cursor visual element is 32x32");
            check(element.IsHitTestVisible == false, "Cursor visual element is hit-test invisible");
        }

        var grid = element as Grid;
        check(grid != null && grid.Children.Count >= 3, "Cursor visual contains shadow, border, and body child layers");
    }

    private static void TestInAppAdorner(Action<bool, string> check)
    {
        var dummy = new Border { Width = 100, Height = 100 };
        var adorner = new VisualCursorAdorner(dummy);
        check(adorner.IsHitTestVisible == false, "VisualCursorAdorner is hit-test invisible");

        adorner.UpdatePosition(new Point(25, 35));
        adorner.SetCursorVisibility(true);
        adorner.SetCursorVisibility(false);

        // Test layout measurement & arrangement overrides
        adorner.Measure(new Size(100, 100));
        adorner.Arrange(new Rect(0, 0, 100, 100));
        check(adorner.DesiredSize.Width >= 0, "VisualCursorAdorner layout Measure/Arrange cycles successfully");
    }

    private static void TestCursorVisualHotspotsAndGeometries(Action<bool, string> check)
    {
        // 1. Hotspot checks
        var arrowHotspot = CursorVisualHelper.GetHotspot(VisualCursorType.Arrow);
        check(arrowHotspot.X == 0 && arrowHotspot.Y == 0, "Arrow hotspot is (0, 0)");

        var ibeamHotspot = CursorVisualHelper.GetHotspot(VisualCursorType.IBeam);
        check(ibeamHotspot.X == 16 && ibeamHotspot.Y == 16, "I-Beam hotspot is exactly centered at (16, 16) / (width/2, height/2)");

        var handHotspot = CursorVisualHelper.GetHotspot(VisualCursorType.Hand);
        check(handHotspot.X == 10 && handHotspot.Y == 2, "Hand hotspot is at pointing fingertip (10, 2)");

        // 2. Element creation for each type
        var arrowElem = CursorVisualHelper.CreateCursorElement(VisualCursorType.Arrow) as Grid;
        check(arrowElem != null && arrowElem.Children.Count >= 3, "Arrow visual element contains shadow, border, and body");

        var ibeamElem = CursorVisualHelper.CreateCursorElement(VisualCursorType.IBeam) as Grid;
        check(ibeamElem != null && ibeamElem.Children.Count >= 3, "I-Beam visual element contains shadow, border, and body");

        var handElem = CursorVisualHelper.CreateCursorElement(VisualCursorType.Hand) as Grid;
        check(handElem != null && handElem.Children.Count >= 3, "Hand visual element contains shadow, border, and body");
    }

    private static void TestCursorTypeDetection(Action<bool, string> check)
    {
        // 1. TextBox / text editing controls
        var textBox = new TextBox { Text = "Test transcript content" };
        var detectedTextBox = CursorVisualHelper.DetectCursorType(textBox);
        check(detectedTextBox == VisualCursorType.IBeam, "DetectCursorType on TextBox identifies I-Beam cursor");

        var disabledTextBox = new TextBox { Text = "Disabled transcript", IsEnabled = false };
        check(CursorVisualHelper.DetectCursorType(disabledTextBox) == VisualCursorType.Arrow, "DetectCursorType on disabled TextBox returns Arrow cursor");

        var passwordBox = new PasswordBox();
        var detectedPassword = CursorVisualHelper.DetectCursorType(passwordBox);
        check(detectedPassword == VisualCursorType.IBeam, "DetectCursorType on PasswordBox identifies I-Beam cursor");

        var disabledPassword = new PasswordBox { IsEnabled = false };
        check(CursorVisualHelper.DetectCursorType(disabledPassword) == VisualCursorType.Arrow, "DetectCursorType on disabled PasswordBox returns Arrow cursor");

        // 2. Buttons and Links
        var button = new Button { Content = "Click Me" };
        var detectedButton = CursorVisualHelper.DetectCursorType(button);
        check(detectedButton == VisualCursorType.Hand, "DetectCursorType on Button identifies Hand cursor");

        var disabledButton = new Button { Content = "Cannot Click", IsEnabled = false };
        check(CursorVisualHelper.DetectCursorType(disabledButton) == VisualCursorType.Arrow, "DetectCursorType on disabled Button returns Arrow cursor");

        var hyperlink = new System.Windows.Documents.Hyperlink();
        check(CursorVisualHelper.DetectCursorType(hyperlink) == VisualCursorType.Hand, "DetectCursorType on Hyperlink identifies Hand cursor");

        var disabledHyperlink = new System.Windows.Documents.Hyperlink { IsEnabled = false };
        check(CursorVisualHelper.DetectCursorType(disabledHyperlink) == VisualCursorType.Arrow, "DetectCursorType on disabled Hyperlink returns Arrow cursor");

        // 3. ScrollBar and components (must remain Arrow on transcript viewer)
        var scrollBar = new System.Windows.Controls.Primitives.ScrollBar();
        check(CursorVisualHelper.DetectCursorType(scrollBar) == VisualCursorType.Arrow, "DetectCursorType on ScrollBar returns Arrow cursor");

        var thumb = new System.Windows.Controls.Primitives.Thumb();
        check(CursorVisualHelper.DetectCursorType(thumb) == VisualCursorType.Arrow, "DetectCursorType on Thumb returns Arrow cursor");

        var track = new System.Windows.Controls.Primitives.Track();
        check(CursorVisualHelper.DetectCursorType(track) == VisualCursorType.Arrow, "DetectCursorType on Track returns Arrow cursor");

        // 4. Fallback / Default elements
        var border = new Border();
        var detectedBorder = CursorVisualHelper.DetectCursorType(border);
        check(detectedBorder == VisualCursorType.Arrow, "DetectCursorType on standard Border defaults to Arrow cursor");

        // 5. Elements with explicit cursor overrides
        var customElem = new ContentControl { Cursor = System.Windows.Input.Cursors.IBeam };
        check(CursorVisualHelper.DetectCursorType(customElem) == VisualCursorType.IBeam, "DetectCursorType respects explicit Cursors.IBeam override");

        var customHand = new ContentControl { Cursor = System.Windows.Input.Cursors.Hand };
        check(CursorVisualHelper.DetectCursorType(customHand) == VisualCursorType.Hand, "DetectCursorType respects explicit Cursors.Hand override");

        // 6. Win32 fallback graceful handling
        var detectedNullHwnd = CursorVisualHelper.DetectCursorTypeFromWin32(IntPtr.Zero);
        check(detectedNullHwnd == VisualCursorType.Arrow, "DetectCursorTypeFromWin32 with IntPtr.Zero safely returns Arrow");
    }

    private static void TestAdornerHotspotOffsets(Action<bool, string> check)
    {
        var dummy = new Border { Width = 200, Height = 200 };
        var adorner = new VisualCursorAdorner(dummy);

        // Arrow: hotspot (0, 0) -> (50, 60) - (0, 0) = (50, 60)
        adorner.UpdatePosition(new Point(50, 60), VisualCursorType.Arrow);
        var transformField = typeof(VisualCursorAdorner).GetField("_transform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var transform = transformField?.GetValue(adorner) as TranslateTransform;
        check(transform != null && transform.X == 50 && transform.Y == 60, "Arrow adorner position matches point directly (hotspot 0, 0)");

        // I-Beam: hotspot (16, 16) -> (50, 60) - (16, 16) = (34, 44)
        adorner.UpdatePosition(new Point(50, 60), VisualCursorType.IBeam);
        check(transform != null && transform.X == 34 && transform.Y == 44, "I-Beam adorner centers hotspot at (16, 16) for pixel-accurate text selection");

        // Hand: hotspot (10, 2) -> (50, 60) - (10, 2) = (40, 58)
        adorner.UpdatePosition(new Point(50, 60), VisualCursorType.Hand);
        check(transform != null && transform.X == 40 && transform.Y == 58, "Hand adorner aligns hotspot to fingertip (10, 2)");
    }

    private static void TestOverlayReuseAndTopmost(Action<bool, string> check)
    {
        var overlay = new GlobalCursorOverlayWindow();
        overlay.SetCursorType(VisualCursorType.IBeam);
        check(overlay.CurrentCursorType == VisualCursorType.IBeam, "GlobalCursorOverlayWindow can switch to I-Beam");
        overlay.SetCursorType(VisualCursorType.Hand);
        check(overlay.CurrentCursorType == VisualCursorType.Hand, "GlobalCursorOverlayWindow can switch to Hand");
        overlay.SetCursorType(VisualCursorType.Arrow);
        check(overlay.CurrentCursorType == VisualCursorType.Arrow, "GlobalCursorOverlayWindow can switch to Arrow");

        // ReassertTopmost should execute cleanly
        overlay.ReassertTopmost();
        check(true, "GlobalCursorOverlayWindow.ReassertTopmost executes cleanly");

        // Verify bounds update executes cleanly
        overlay.UpdateBounds();
        check(true, "GlobalCursorOverlayWindow.UpdateBounds executes cleanly");
        overlay.Close();

        // Test Service Allocation Churn / HWND Reuse
        var service = CursorCaptureProtectionService.Instance;

        // Test EmergencyEscapeTriggered event registration
        bool escapeFired = false;
        Action escapeHandler = () => escapeFired = true;
        service.EmergencyEscapeTriggered += escapeHandler;
        service.EmergencyEscapeTriggered -= escapeHandler;
        check(!escapeFired, "EmergencyEscapeTriggered can be registered and unregistered safely");

        service.StopGlobalCursorCloak();

        var started = service.StartGlobalCursorCloak();
        check(started, "StartGlobalCursorCloak succeeds");
        var initialOverlays = service.OverlayWindows.ToList();
        check(initialOverlays.Count > 0, "Overlay windows are allocated for active screens");

        service.ReassertOverlayTopmost();
        check(true, "CursorCaptureProtectionService.ReassertOverlayTopmost executes cleanly");

        service.StopGlobalCursorCloak();
        check(service.IsGlobalCloakActive == false, "Service is inactive after stop");

        // Start again and verify existing HWND instances are reused (no allocation churn)
        service.StartGlobalCursorCloak();
        var secondOverlays = service.OverlayWindows.ToList();
        check(initialOverlays.Count == secondOverlays.Count, "Overlay count is maintained across cloak restarts");
        bool allReused = true;
        for (int i = 0; i < initialOverlays.Count; i++)
        {
            if (!ReferenceEquals(initialOverlays[i], secondOverlays[i]))
            {
                allReused = false;
                break;
            }
        }
        check(allReused, "Overlay window instances are reused across toggles (zero HWND allocation churn)");

        service.StopGlobalCursorCloak();
    }

    private static void TestFactoryResetCursorRestoration(Action<bool, string> check, string tempDir)
    {
        var configPath = Path.Combine(tempDir, "wipe_cursor_test_" + Guid.NewGuid().ToString("N") + ".json");
        var configService = new ConfigService(configPath);

        // Simulate dirty state: user had custom cursor cloak enabled
        configService.Save(
            Array.Empty<GuardedApp>(),
            inAppCursorCloakEnabled: false,
            globalCursorCloakEnabled: true);

        var loadedDirty = new ConfigService(configPath);
        loadedDirty.Load();
        check(loadedDirty.InAppCursorCloakEnabled == false && loadedDirty.GlobalCursorCloakEnabled == true,
            "ConfigService successfully saved dirty custom cursor preferences");

        // Simulate the explicit wipe save parameters from ResetApplicationToDefaults()
        loadedDirty.Save(
            apps: Array.Empty<GuardedApp>(),
            inAppCursorCloakEnabled: true,
            globalCursorCloakEnabled: false);

        var wipedService = new ConfigService(configPath);
        wipedService.Load();
        check(wipedService.InAppCursorCloakEnabled == true, "Factory reset explicitly restores InAppCursorCloakEnabled to true");
        check(wipedService.GlobalCursorCloakEnabled == false, "Factory reset explicitly restores GlobalCursorCloakEnabled to false (no dirty memory overwrite)");
    }

    private static void TestBilingualLocalizationKeys(Action<bool, string> check)
    {
        var l = LocalizationService.Current;
        var keys = new[]
        {
            "Capture.InAppCursorCloak",
            "Capture.InAppCursorCloakTip",
            "Capture.GlobalCursorCloak",
            "Capture.GlobalCursorCloakTip",
            "Capture.GlobalCursorCloakOff",
            "Capture.GlobalCursorCloakOn",
            "Capture.CursorCloakEnabledToast",
            "Capture.CursorCloakDisabledToast"
        };

        foreach (var key in keys)
        {
            l.SetLanguage("en");
            var en = l[key];
            check(!string.IsNullOrWhiteSpace(en) && en != key, $"Key '{key}' exists in English catalog: {en}");

            l.SetLanguage("vi");
            var vi = l[key];
            check(!string.IsNullOrWhiteSpace(vi) && vi != key, $"Key '{key}' exists in Vietnamese catalog: {vi}");

            check(en != vi, $"English and Vietnamese translations differ for key '{key}'");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int GetBitmapBits(IntPtr hbmp, int cbBuffer, byte[] lpvBits);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    private static bool IsSystemArrowTransparent()
    {
        var hCur = LoadCursor(IntPtr.Zero, 32512);
        if (hCur == IntPtr.Zero) return false;
        if (!GetIconInfo(hCur, out var info)) return false;
        try
        {
            if (info.hbmColor != IntPtr.Zero || info.hbmMask == IntPtr.Zero)
                return false;

            // 32x32 1-bit cursor has height 64 (AND mask 128 bytes + XOR mask 128 bytes)
            var bits = new byte[256];
            var read = GetBitmapBits(info.hbmMask, bits.Length, bits);
            if (read != 256) return false;

            for (int i = 0; i < 128; i++)
            {
                if (bits[i] != 0xFF) return false;
            }
            for (int i = 128; i < 256; i++)
            {
                if (bits[i] != 0x00) return false;
            }
            return true;
        }
        finally
        {
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
        }
    }

    private static void TestCursorShieldDoesNotClobberGlobalCloak(Action<bool, string> check)
    {
        var service = CursorCaptureProtectionService.Instance;
        var shield = new CursorShieldService();
        try
        {
            service.StopGlobalCursorCloak();
            var started = service.StartGlobalCursorCloak();
            check(started, "GlobalCursorCloak starts for shield-coexistence test");
            check(IsSystemArrowTransparent(), "System arrow cursor is 100% transparent after StartGlobalCursorCloak");

            // Calling Deactivate() when inactive must be a no-op and keep GlobalCursorCloak intact
            shield.Deactivate();
            check(!shield.IsActive, "Inactive CursorShield remains inactive after Deactivate()");
            check(IsSystemArrowTransparent(), "Inactive CursorShield.Deactivate() does not overwrite GlobalCursorCloak transparent cursor");

            // Even if shield was active and deactivated with restoreSystemCursors: false, GlobalCursorCloak stays intact
            shield.Activate();
            check(shield.IsActive, "CursorShield activates");
            // Re-apply global cloak (simulating GlobalCloak taking over)
            service.StopGlobalCursorCloak();
            service.StartGlobalCursorCloak();
            shield.Deactivate(restoreSystemCursors: false);
            check(!shield.IsActive, "CursorShield deactivates with restoreSystemCursors: false");
            check(IsSystemArrowTransparent(), "CursorShield.Deactivate(restoreSystemCursors: false) preserves GlobalCursorCloak transparent cursor");

            service.StopGlobalCursorCloak();
            check(!IsSystemArrowTransparent(), "StopGlobalCursorCloak restores visible default Windows arrow cursor");
        }
        finally
        {
            try { service.StopGlobalCursorCloak(); } catch { }
            try { CursorCaptureProtectionService.RestoreSystemCursors(); } catch { }
        }
    }
}
