using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using StreamGuard;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class StealthFocusChecks
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_ACTIVATE = 1;
    private const int MA_NOACTIVATE = 3;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_LWIN = 0x5B;
    private const int VK_S = 0x53;
    private const int VK_G = 0x47;
    private const int VK_A = 0x41;
    private const int VK_O = 0x4F;
    private const int VK_T = 0x54;

    internal static void Run(Action<bool, string> check, string tempDir)
    {
        TestPassiveFocusModeDefaults(check, tempDir);
        TestHotkeyChordSwallowing(check);
        TestIsTextEntryVisualLogic(check);
        TestCaptionOverlayNonActivating(check);
        TestBrowserScriptServiceAntiBlur(check);
    }

    private static void TestPassiveFocusModeDefaults(Action<bool, string> check, string tempDir)
    {
        var configPath = Path.Combine(tempDir, "stealth_focus_config_" + Guid.NewGuid().ToString("N") + ".json");
        var configService = new ConfigService(configPath);
        check(configService.PassiveFocusMode == true, "ConfigService defaults PassiveFocusMode to true");

        var configModel = new StreamGuardConfig();
        check(configModel.PassiveFocusMode == true, "StreamGuardConfig model defaults PassiveFocusMode to true");

        var loaded = configService.Load();
        check(configService.PassiveFocusMode == true, "Fresh loaded configuration maintains PassiveFocusMode = true");

        configService.Save(Array.Empty<GuardedApp>(), passiveFocusMode: false);
        var reloaded = new ConfigService(configPath);
        reloaded.Load();
        check(reloaded.PassiveFocusMode == false, "Explicitly saved false PassiveFocusMode is preserved");

        configService.Save(Array.Empty<GuardedApp>(), passiveFocusMode: true);
        var reloadedTrue = new ConfigService(configPath);
        reloadedTrue.Load();
        check(reloadedTrue.PassiveFocusMode == true, "Explicitly saved true PassiveFocusMode is preserved");
    }

    private static void TestHotkeyChordSwallowing(Action<bool, string> check)
    {
        using var hotkeys = new HotkeyService();
        int shiftSgCalls = 0;
        int ctrlShiftSgCalls = 0;

        hotkeys.RegisterShiftSgChord(() => shiftSgCalls++);
        hotkeys.RegisterCtrlShiftSgChord(() => ctrlShiftSgCalls++);

        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var replayed = new System.Collections.Generic.List<HotkeyService.ReplayedKeyEvent>();
        hotkeys.ReplayInputOverride = batch => { replayed.AddRange(batch); return true; };
        var replayExtra = (IntPtr)typeof(HotkeyService).GetField("ReplayExtraInfo", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        void FeedReplayedBatch()
        {
            foreach (var ev in replayed)
            {
                bool swallowed = hotkeys.ProcessKeyboardEvent(ev.IsKeyDown ? WM_KEYDOWN : WM_KEYUP, ev.VkCode, ev.ScanCode, ev.Flags, replayExtra);
                check(!swallowed, "Replayed key event passes straight through without re-interception");
            }
            replayed.Clear();
        }

        // 1. Normal 'G' press without modifiers -> MUST NOT be swallowed
        bool gDownSwallowed = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool gUpSwallowed = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        check(!gDownSwallowed && !gUpSwallowed, "Single 'G' key is not swallowed by HotkeyService");

        // 2. Shift + G without S -> defers G KeyDown and replays [G_down, G_up] cleanly on G KeyUp without firing chord
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool shiftGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool shiftGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        check(shiftGDown && shiftGUp, "Normal Shift+G defers G KeyDown and bundles [G_down, G_up] on G KeyUp");
        check(replayed.Count == 2 &&
              replayed[0] == new HotkeyService.ReplayedKeyEvent(VK_G, 0x22, 0, true) &&
              replayed[1] == new HotkeyService.ReplayedKeyEvent(VK_G, 0x22, 0x80, false),
              "Normal Shift+G replays exact [G_down, G_up] sequence with hardware scan code");
        FeedReplayedBatch();
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        Pump();
        check(shiftSgCalls == 0, "Normal Shift+G without S does not fire Shift+S+G chord");

        // 3. S + G without Shift -> MUST NOT be swallowed
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool sgDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool sgUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        check(!sgDown && !sgUp, "S + G without Shift is not swallowed");

        // 4. Shift + S + G chord -> BOTH 'S' and 'G' KeyDown/KeyUp MUST BE SWALLOWED symmetrically
        bool chordShiftDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool chordSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool chordSRepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool chordGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool chordGRepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool chordGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        bool chordSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        bool chordShiftUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        Pump();

        check(!chordShiftDown && !chordShiftUp, "Shift modifier passes through on Shift + S + G");
        check(chordSDown && chordSRepeat && chordSUp, "Shift + S + G swallows VK_S KeyDown, repeat, and KeyUp so 'S' never leaks into inputs");
        check(chordGDown, "Shift + S + G KeyDown of VK_G is swallowed (returns true) to prevent key leakage");
        check(chordGRepeat, "Shift + S + G repeat KeyDown of VK_G is swallowed");
        check(chordGUp, "Shift + S + G KeyUp of VK_G is symmetrically swallowed");
        check(replayed.Count == 0, "Shift + S + G chord never replays deferred 'S'");
        check(shiftSgCalls == 1, "Shift + S + G chord callback was executed");

        // 5. Ctrl + Shift + S + G chord -> BOTH 'S' and 'G' KeyDown/KeyUp MUST BE SWALLOWED
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool ctrlChordSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool ctrlChordGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool ctrlChordGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        bool ctrlChordSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL);
        Pump();

        check(ctrlChordSDown && ctrlChordSUp, "Ctrl + Shift + S + G KeyDown and KeyUp of VK_S are swallowed");
        check(ctrlChordGDown, "Ctrl + Shift + S + G KeyDown of VK_G is swallowed");
        check(ctrlChordGUp, "Ctrl + Shift + S + G KeyUp of VK_G is symmetrically swallowed");
        check(replayed.Count == 0, "Ctrl + Shift + S + G chord never replays deferred 'S'");
        check(ctrlShiftSgCalls == 1, "Ctrl + Shift + S + G chord callback was executed");

        // 6. Edge case: Non-chord 'G' down followed by modifiers -> KeyUp of 'G' MUST NOT be swallowed
        // If 'G' was pressed first without chord, swallowing its KeyUp would leave 'G' stuck down in OS!
        bool nonChordGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool nonChordSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool nonChordGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        bool nonChordSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        check(!nonChordGDown && !nonChordGUp, "KeyUp is NOT swallowed if KeyDown was not swallowed (no stuck key in OS)");
        check(nonChordSDown && nonChordSUp, "Completing Shift + G + S chord on S swallows VK_S cleanly");
        Pump();
        shiftSgCalls = 0;

        // 7. Edge case: Modifiers released before 'G' KeyUp -> KeyUp MUST still be swallowed
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool earlySDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool earlyReleaseGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT); // user releases shift before G
        bool earlyReleaseSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S); // user releases S before G
        bool earlyReleaseGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G); // G finally released
        Pump();
        check(earlySDown && earlyReleaseSUp, "Early-release Shift + S + G swallows VK_S symmetrically");
        check(earlyReleaseGDown && earlyReleaseGUp, "KeyUp is symmetrically swallowed even if modifiers released first");
        check(replayed.Count == 0 && shiftSgCalls == 1, "Early-release chord fires once with zero replayed inputs");

        // 8. Normal uppercase 'S' typing (Shift + S, release S) -> replays [S_down, S_up] cleanly
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool tapSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool tapSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S, 0x1F, 0x80, IntPtr.Zero);
        check(tapSDown && tapSUp, "Normal Shift+S defers S KeyDown and bundles [S_down, S_up] on S KeyUp");
        check(replayed.Count == 2 &&
              replayed[0] == new HotkeyService.ReplayedKeyEvent(VK_S, 0x1F, 0, true) &&
              replayed[1] == new HotkeyService.ReplayedKeyEvent(VK_S, 0x1F, 0x80, false),
              "Normal Shift+S replays exact [S_down, S_up] sequence with hardware scan code");
        FeedReplayedBatch();
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        Pump();
        check(shiftSgCalls == 1, "Normal Shift+S does not fire Shift+S+G chord");

        // 9. Fast touch-typing 'St' (Shift down -> S down -> Shift up -> T down -> S up)
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool fastShiftUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        check(fastShiftUp && replayed.Count == 2 &&
              replayed[0].VkCode == VK_S && replayed[0].IsKeyDown &&
              replayed[1].VkCode == VK_LSHIFT && !replayed[1].IsKeyDown,
              "Releasing Shift before S flushes S_down BEFORE Shift_up so uppercase 'S' is preserved");
        FeedReplayedBatch();
        bool tDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_T, 0x14, 0, IntPtr.Zero);
        bool tUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_T, 0x14, 0x80, IntPtr.Zero);
        bool lateSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S, 0x1F, 0x80, IntPtr.Zero);
        check(!tDown && !tUp && !lateSUp, "Subsequent keys and late S_up pass through cleanly in 'St' typing");

        // 10. Typing 'SO' with Shift held (Shift down -> S down -> O down)
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool oDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_O, 0x18, 0, IntPtr.Zero);
        check(oDown && replayed.Count == 2 &&
              replayed[0].VkCode == VK_S && replayed[0].IsKeyDown &&
              replayed[1].VkCode == VK_O && replayed[1].IsKeyDown,
              "Pressing 'O' after Shift+S flushes [S_down, O_down] in exact typing order");
        FeedReplayedBatch();
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_O, 0x18, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S, 0x1F, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);

        // 11. Double-Shift (LShift + RShift + S -> LShift up -> G down) must NOT flush S prematurely
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_RSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool lShiftUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        bool doubleShiftG = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_RSHIFT);
        Pump();
        check(!lShiftUp && doubleShiftG && replayed.Count == 0 && shiftSgCalls == 2,
              "Releasing one Shift while other Shift remains down keeps S pending for Shift+S+G");

        // 12. Win + Shift + S (Windows Snipping Tool) must NOT be deferred or swallowed
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LWIN);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool winShiftS = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LWIN);
        check(!winShiftS && replayed.Count == 0, "Win + Shift + S passes through immediately without deferral");

        // 13. Repeated toggle while holding Shift+S (G down/up twice)
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool g1Down = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool g1Up = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        bool g2Down = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool g2Up = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        bool heldSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        Pump();
        check(g1Down && g1Up && g2Down && g2Up && heldSUp && replayed.Count == 0 && shiftSgCalls == 4,
              "Repeated Shift+S+G toggles while holding Shift+S swallow all G and S events cleanly");

        // 14. Verify Win32 SendInput INPUT struct size matches native ABI (40 on x64, 28 on x86)
        var inputType = typeof(HotkeyService).GetNestedType("INPUT", BindingFlags.NonPublic)!;
        int inputSize = System.Runtime.InteropServices.Marshal.SizeOf(inputType);
        check(inputSize == (IntPtr.Size == 8 ? 40 : 28), $"Win32 INPUT struct size matches native ABI: {inputSize}");

        // 15. Early Shift release while S/G remain held -> auto-repeat KeyDowns of S and G MUST still be swallowed
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool r15S = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool r15G = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT); // Shift released before S & G
        bool r15SRepeatAfterShiftUp = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool r15GRepeatAfterShiftUp = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool r15SUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        bool r15GUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        Pump();
        check(r15S && r15G && r15SRepeatAfterShiftUp && r15GRepeatAfterShiftUp && r15SUp && r15GUp && replayed.Count == 0,
              "Auto-repeat KeyDowns of swallowed S/G after early Shift release are still swallowed");

        // 16. Ctrl+Shift+Alt+A early Alt release -> auto-repeat KeyDown of A MUST still be swallowed
        int addAppCalls = 0;
        hotkeys.RegisterCtrlShiftAltAChord(() => addAppCalls++);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_MENU);
        bool r16A = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_A);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_MENU);
        bool r16ARepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_A);
        bool r16AUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_A);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL);
        Pump();
        check(r16A && r16ARepeat && r16AUp && addAppCalls == 1,
              "Auto-repeat KeyDown of swallowed A after early Alt release is still swallowed");

        // 17. VK_PACKET Unicode replay formatting uses KEYEVENTF_UNICODE with wVk = 0
        const int VK_PACKET = 0xE7;
        const uint KEYEVENTF_UNICODE = 0x0004;
        var kiPacket = HotkeyService.BuildKeybdInput(new HotkeyService.ReplayedKeyEvent(VK_PACKET, 0x00E1, 0, true));
        check(kiPacket.wVk == 0 && kiPacket.wScan == 0x00E1 && kiPacket.dwFlags == KEYEVENTF_UNICODE,
              "BuildKeybdInput encodes VK_PACKET with KEYEVENTF_UNICODE and wVk=0");

        // 18. Only Ctrl+Shift+S+G registered -> plain Shift+S+G without Ctrl passes through, Ctrl+Shift+S+G swallows
        using (var ctrlOnlyHotkeys = new HotkeyService())
        {
            int ctrlOnlyCalls = 0;
            ctrlOnlyHotkeys.RegisterCtrlShiftSgChord(() => ctrlOnlyCalls++);
            ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
            bool plainSDown = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
            bool plainGDown = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
            bool plainGUp = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
            bool plainSUp = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
            ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);

            ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL);
            ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
            bool ctrlSDown = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
            bool ctrlGDown = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
            bool ctrlGUp = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
            bool ctrlSUp = ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
            ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
            ctrlOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL);
            Pump();

            check(!plainSDown && !plainGDown && !plainGUp && !plainSUp &&
                  ctrlSDown && ctrlGDown && ctrlGUp && ctrlSUp && ctrlOnlyCalls == 1,
                  "When only Ctrl+Shift+S+G is registered, Shift+S+G without Ctrl is not swallowed while Ctrl+Shift+S+G is swallowed");
        }

        // 19. Shift + G + S (G pressed BEFORE S while Shift is held, no Ctrl) -> BOTH 'G' and 'S' swallowed cleanly with zero leak
        int callsBeforeGs = shiftSgCalls;
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool gsGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool gsGPendingRepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool gsSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool gsGRepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool gsSRepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool gsSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S, 0x1F, 0x80, IntPtr.Zero);
        bool gsGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        Pump();
        check(gsGDown && gsGPendingRepeat && gsSDown && gsGRepeat && gsSRepeat && gsSUp && gsGUp &&
              replayed.Count == 0 && shiftSgCalls == callsBeforeGs + 1,
              "Shift + G + S (G before S) swallows all G and S KeyDown, repeat, and KeyUp events with zero replayed inputs");

        // 20. Early Shift release on Shift + G + S -> repeats and KeyUps of G/S still swallowed symmetrically
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool erGsGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool erGsSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        bool erGsGRepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool erGsSRepeat = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool erGsGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        bool erGsSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        Pump();
        check(erGsGDown && erGsSDown && erGsGRepeat && erGsSRepeat && erGsGUp && erGsSUp &&
              replayed.Count == 0 && shiftSgCalls == callsBeforeGs + 2,
              "Early Shift release on Shift + G + S keeps swallowing repeats and KeyUps of G and S");

        // 21. Typing 'GO' with Shift held (Shift down -> G down -> O down)
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool goGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool goODown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_O, 0x18, 0, IntPtr.Zero);
        check(goGDown && goODown && replayed.Count == 2 &&
              replayed[0] == new HotkeyService.ReplayedKeyEvent(VK_G, 0x22, 0, true) &&
              replayed[1] == new HotkeyService.ReplayedKeyEvent(VK_O, 0x18, 0, true),
              "Pressing 'O' after Shift+G flushes [G_down, O_down] in exact typing order");
        FeedReplayedBatch();
        bool goGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        bool goOUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_O, 0x18, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        check(!goGUp && !goOUp, "KeyUps after 'GO' replay pass through cleanly");

        // 22. Typing 'GG' with Shift held (two consecutive Shift+G taps)
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool gg1Down = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool gg1Up = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        check(gg1Down && gg1Up && replayed.Count == 2, "First 'G' in 'GG' replays [G_down, G_up]");
        FeedReplayedBatch();
        bool gg2Down = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool gg2Up = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        check(gg2Down && gg2Up && replayed.Count == 2, "Second 'G' in 'GG' replays [G_down, G_up]");
        FeedReplayedBatch();
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        Pump();
        check(shiftSgCalls == callsBeforeGs + 2, "Typing 'GG' never fires Shift+S+G chord");

        // 23. Fast touch-typing 'Go' (Shift down -> G down -> Shift up -> O down -> G up -> O up)
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool goFastShiftUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        check(goFastShiftUp && replayed.Count == 2 &&
              replayed[0].VkCode == VK_G && replayed[0].IsKeyDown &&
              replayed[1].VkCode == VK_LSHIFT && !replayed[1].IsKeyDown,
              "Releasing Shift before G flushes G_down BEFORE Shift_up so uppercase 'G' is preserved");
        FeedReplayedBatch();
        bool goLowODown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_O, 0x18, 0, IntPtr.Zero);
        bool goLowOUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_O, 0x18, 0x80, IntPtr.Zero);
        bool goLateGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        check(!goLowODown && !goLowOUp && !goLateGUp, "Subsequent keys and late G_up pass through cleanly in 'Go' typing");

        // 24. Double-Shift (LShift + RShift + G -> LShift up -> S down) keeps G pending for Shift+G+S
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_RSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool dsLShiftUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        bool dsSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool dsSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        bool dsGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_RSHIFT);
        Pump();
        check(!dsLShiftUp && dsSDown && dsSUp && dsGUp && replayed.Count == 0 && shiftSgCalls == callsBeforeGs + 3,
              "Releasing one Shift while other Shift remains down keeps G pending for Shift+G+S");

        // 25. Ctrl + Shift + G (RegisterHotKey ToggleVisibility) passes straight through without deferral or swallowing
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool ctrlShiftGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool ctrlShiftGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL);
        Pump();
        check(!ctrlShiftGDown && !ctrlShiftGUp && replayed.Count == 0 &&
              shiftSgCalls == callsBeforeGs + 3 && ctrlShiftSgCalls == 1,
              "Ctrl + Shift + G passes straight through on KeyDown/KeyUp so RegisterHotKey ToggleVisibility works immediately");

        // 26. Win + Shift + G passes through immediately without deferral
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LWIN);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool winShiftG = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LWIN);
        check(!winShiftG && replayed.Count == 0, "Win + Shift + G passes through immediately without deferral");

        // 27. Repeated Shift + G + S toggles while holding Shift + G
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        bool s1Down = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool s1Up = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        bool s2Down = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool s2Up = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        bool heldGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        Pump();
        check(s1Down && s1Up && s2Down && s2Up && heldGUp && replayed.Count == 0 && shiftSgCalls == callsBeforeGs + 5,
              "Repeated Shift+G+S toggles while holding Shift+G swallow all S and G events cleanly");

        // 28. Only Shift+S+G registered -> Ctrl+Shift+S and Ctrl+Shift+S+G pass through without deferral/swallowing
        using (var shiftOnlyHotkeys = new HotkeyService())
        {
            int shiftOnlyCalls = 0;
            shiftOnlyHotkeys.RegisterShiftSgChord(() => shiftOnlyCalls++);
            shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL);
            shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
            bool onlyShiftCtrlSDown = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
            bool onlyShiftCtrlGDown = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
            bool onlyShiftCtrlGUp = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
            bool onlyShiftCtrlSUp = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
            shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
            shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL);

            shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
            bool plainGsGDown = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
            bool plainGsSDown = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
            bool plainGsSUp = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
            bool plainGsGUp = shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
            shiftOnlyHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
            Pump();

            check(!onlyShiftCtrlSDown && !onlyShiftCtrlGDown && !onlyShiftCtrlGUp && !onlyShiftCtrlSUp &&
                  plainGsGDown && plainGsSDown && plainGsSUp && plainGsGUp && shiftOnlyCalls == 1,
                  "When only Shift+S+G is registered, Ctrl+Shift+S and Ctrl+Shift+S+G pass through while Shift+G+S is swallowed");
        }

        // 29. Rapid 'GS' and 'SG' typing while holding Shift -> replays cleanly without triggering Shift+S+G
        int callsBeforeGsSg = shiftSgCalls;
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool rGsGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool rGsGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        check(rGsGDown && rGsGUp && replayed.Count == 2 && replayed[0].VkCode == VK_G && replayed[1].VkCode == VK_G,
              "Sequential 'G' in 'GS' replays [G_down, G_up]");
        FeedReplayedBatch();
        bool rGsSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool rGsSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S, 0x1F, 0x80, IntPtr.Zero);
        check(rGsSDown && rGsSUp && replayed.Count == 2 && replayed[0].VkCode == VK_S && replayed[1].VkCode == VK_S,
              "Sequential 'S' in 'GS' replays [S_down, S_up]");
        FeedReplayedBatch();
        bool rSgSDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool rSgSUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S, 0x1F, 0x80, IntPtr.Zero);
        FeedReplayedBatch();
        bool rSgGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool rSgGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        FeedReplayedBatch();
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        Pump();
        check(rSgSDown && rSgSUp && rSgGDown && rSgGUp && shiftSgCalls == callsBeforeGsSg,
              "Sequential 'GS' and 'SG' typing while holding Shift never triggers Shift+S+G");

        // 30. Shift + G followed by Ctrl rollover flushes [G_down, Ctrl_down] so 'G' precedes Ctrl
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool rollGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool rollCtrlDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL, 0x1D, 0, IntPtr.Zero);
        check(rollGDown && rollCtrlDown && replayed.Count == 2 &&
              replayed[0].VkCode == VK_G && replayed[0].IsKeyDown &&
              replayed[1].VkCode == VK_LCONTROL && replayed[1].IsKeyDown,
              "Pressing Ctrl after Shift+G flushes [G_down, Ctrl_down] before Ctrl reaches OS");
        FeedReplayedBatch();
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL, 0x1D, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);

        // 31. Alt+Shift+G / LLKHF_ALTDOWN passes straight through without deferral or chord firing
        const int LLKHF_ALTDOWN = 0x20;
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        bool altFlagGDown = hotkeys.ProcessKeyboardEvent(0x0104 /* WM_SYSKEYDOWN */, VK_G, 0x22, LLKHF_ALTDOWN, IntPtr.Zero);
        bool altFlagSDown = hotkeys.ProcessKeyboardEvent(0x0104 /* WM_SYSKEYDOWN */, VK_S, 0x1F, LLKHF_ALTDOWN, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(0x0105 /* WM_SYSKEYUP */, VK_S, 0x1F, LLKHF_ALTDOWN | 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(0x0105 /* WM_SYSKEYUP */, VK_G, 0x22, LLKHF_ALTDOWN | 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        Pump();
        check(!altFlagGDown && !altFlagSDown && replayed.Count == 0 && shiftSgCalls == callsBeforeGsSg,
              "Shift+G/S with LLKHF_ALTDOWN passes through immediately without deferral or chord firing");

        // 32. Dispose while VK_G is pending clears pending state and ignores subsequent events
        using (var disposedHotkeys = new HotkeyService())
        {
            disposedHotkeys.RegisterShiftSgChord(() => { });
            disposedHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
            bool dPendingG = disposedHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
            disposedHotkeys.Dispose();
            bool dAfterS = disposedHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
            check(dPendingG && !dAfterS, "Dispose while VK_G is pending clears pending state cleanly");
        }

        // 33. 'SOG' rollover with Shift held (S -> O -> G before S_up) must NOT falsely swallow 'G' or trigger Shift+S+G
        int callsBeforeSog = shiftSgCalls;
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT, 0x2A, 0, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S, 0x1F, 0, IntPtr.Zero);
        bool sogODown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_O, 0x18, 0, IntPtr.Zero);
        FeedReplayedBatch();
        bool sogGDown = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G, 0x22, 0, IntPtr.Zero);
        bool sogGUp = hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G, 0x22, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_O, 0x18, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S, 0x1F, 0x80, IntPtr.Zero);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT, 0x2A, 0x80, IntPtr.Zero);
        Pump();
        check(sogODown && !sogGDown && !sogGUp && shiftSgCalls == callsBeforeSog,
              "'SOG' typing rollover with Shift held does not swallow G or fire Shift+S+G");

        // After releasing S and G, Shift+S+G works normally again
        hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
        bool postSogS = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_S);
        bool postSogG = hotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_G);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_S);
        hotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
        Pump();
        check(postSogS && postSogG && shiftSgCalls == callsBeforeSog + 1,
              "Shift+S+G works normally again after rollover keys are released");

        // 34. Fallback registered hotkey (e.g. Ctrl+Shift+M when RegisterHotKey is contested in fullscreen)
        using (var fbHotkeys = new HotkeyService())
        {
            int fbCalls = 0;
            const int VK_M = 0x4D;
            int fbId = fbHotkeys.RegisterFallbackHotkey(HotkeyService.MOD_CTRL | HotkeyService.MOD_SHIFT, VK_M, () => fbCalls++);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
            bool fbDown = fbHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_M);
            bool fbRepeat = fbHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_M);
            bool fbUp = fbHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_M);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL);
            Pump();
            check(fbId >= 0 && fbDown && fbRepeat && fbUp && fbCalls == 1,
                  "Fallback hotkey fires once (no repeat) and swallows KeyDown/repeat/KeyUp symmetrically");

            fbHotkeys.Unregister(fbId);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LCONTROL);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_LSHIFT);
            bool afterUnregDown = fbHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_M);
            bool afterUnregUp = fbHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_M);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LSHIFT);
            fbHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_LCONTROL);
            Pump();
            check(!afterUnregDown && !afterUnregUp && fbCalls == 1,
                  "Unregister removes fallback hotkey cleanly");
        }

        // 35. Curtain Escape guard: passes Esc through when hidden, swallows + dismisses when curtain visible
        using (var escHotkeys = new HotkeyService())
        {
            const int VK_ESCAPE = 0x1B;
            bool curtainVisible = false;
            int escDismissCalls = 0;
            escHotkeys.RegisterCurtainEscapeGuard(() => curtainVisible, () => escDismissCalls++);

            bool normalEscDown = escHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_ESCAPE);
            bool normalEscUp = escHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_ESCAPE);
            Pump();
            check(!normalEscDown && !normalEscUp && escDismissCalls == 0,
                  "Esc passes through untouched when no Privacy Curtain is visible");

            curtainVisible = true;
            bool curtainEscDown = escHotkeys.ProcessKeyboardEvent(WM_KEYDOWN, VK_ESCAPE);
            bool curtainEscUp = escHotkeys.ProcessKeyboardEvent(WM_KEYUP, VK_ESCAPE);
            Pump();
            check(curtainEscDown && curtainEscUp && escDismissCalls == 1,
                  "Esc is swallowed and dismisses Privacy Curtain when curtain is visible");
        }
    }

    private static void TestIsTextEntryVisualLogic(Action<bool, string> check)
    {
        // Editable TextBox -> returns true
        var editableTextBox = new TextBox { IsReadOnly = false };
        check(MainWindow.IsTextEntryVisual(editableTextBox), "Editable TextBox is detected as text entry control");

        // ReadOnly TextBox (like CaptionTranscriptText) -> returns false!
        var readOnlyTextBox = new TextBox { IsReadOnly = true };
        check(!MainWindow.IsTextEntryVisual(readOnlyTextBox), "ReadOnly TextBox is NOT detected as text entry control");

        // PasswordBox -> returns true
        var passwordBox = new PasswordBox();
        check(MainWindow.IsTextEntryVisual(passwordBox), "PasswordBox is detected as text entry control");

        // Ordinary Button -> returns false
        var button = new Button();
        check(!MainWindow.IsTextEntryVisual(button), "Button is NOT detected as text entry control");

        // Border wrapping ReadOnly TextBox -> returns false
        var parentBorder = new Border { Child = readOnlyTextBox };
        check(!MainWindow.IsTextEntryVisual(parentBorder), "Border wrapping ReadOnly TextBox is NOT detected as text entry control");

        // Border wrapping Editable TextBox -> returns true when testing the TextBox
        var editableBorder = new Border { Child = editableTextBox };
        check(MainWindow.IsTextEntryVisual(editableTextBox), "Editable TextBox inside Border is detected as text entry control");

        // Generic Thumb -> returns false
        var genericThumb = new Thumb();
        check(!MainWindow.IsTextEntryVisual(genericThumb), "Generic Thumb retains passive focus behavior");

        // Named SidebarResizeThumb -> returns true
        var resizeThumb = new Thumb { Name = "SidebarResizeThumb" };
        check(MainWindow.IsTextEntryVisual(resizeThumb), "SidebarResizeThumb is detected for keyboard accessibility");

        // ComboBox & ComboBoxItem -> returns true for keyboard dropdown support
        var comboBox = new ComboBox();
        check(MainWindow.IsTextEntryVisual(comboBox), "ComboBox is detected for keyboard selection support");
        var comboItem = new ComboBoxItem();
        check(MainWindow.IsTextEntryVisual(comboItem), "ComboBoxItem is detected for keyboard selection support");
    }

    private static void TestCaptionOverlayNonActivating(Action<bool, string> check)
    {
        var settings = new CaptionSettings();
        var overlay = new CaptionOverlayWindow(settings);

        check(!overlay.Focusable, "CaptionOverlayWindow has Focusable = false");
        check(!overlay.ShowActivated, "CaptionOverlayWindow has ShowActivated = false");

        // Invoke Hook via reflection to test WM_MOUSEACTIVATE
        var hookMethod = typeof(CaptionOverlayWindow).GetMethod("Hook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object[] args = { IntPtr.Zero, WM_MOUSEACTIVATE, IntPtr.Zero, IntPtr.Zero, false };
        var result = (IntPtr)hookMethod.Invoke(overlay, args)!;
        bool handled = (bool)args[4];

        check(result == (IntPtr)MA_NOACTIVATE && handled, "CaptionOverlayWindow Hook(WM_MOUSEACTIVATE) returns MA_NOACTIVATE (3) with handled=true");

        overlay.ReassertTopmost();
        check(true, "CaptionOverlayWindow.ReassertTopmost executes cleanly");

        // Verify EndNonActivatingDrag persists position via SettingsChanged event
        bool settingsFired = false;
        overlay.SettingsChanged += () => settingsFired = true;
        var endDragMethod = typeof(CaptionOverlayWindow).GetMethod("EndNonActivatingDrag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var isDraggingField = typeof(CaptionOverlayWindow).GetField("_isDragging", BindingFlags.Instance | BindingFlags.NonPublic)!;
        isDraggingField.SetValue(overlay, true);
        endDragMethod.Invoke(overlay, new object[] { new Border() });
        check(settingsFired, "EndNonActivatingDrag fires SettingsChanged to persist overlay position");
    }

    private static void TestBrowserScriptServiceAntiBlur(Action<bool, string> check)
    {
        var antiBlur = BrowserScriptService.GetAntiBlurScript();
        check(antiBlur.Contains("__streamGuardAntiBlur"), "GetAntiBlurScript defines __streamGuardAntiBlur key");
        check(antiBlur.Contains("'blur'") && antiBlur.Contains("'focusout'") && antiBlur.Contains("'visibilitychange'"), "GetAntiBlurScript intercepts blur, focusout, and visibilitychange");
        check(antiBlur.Contains("'mouseleave'") && antiBlur.Contains("'pointerleave'") && antiBlur.Contains("mouseout"), "GetAntiBlurScript intercepts mouseleave and viewport-leaving mouseout to prevent leave-window detection");
        check(antiBlur.Contains("document.hasFocus = () =>"), "GetAntiBlurScript maintains document.hasFocus() = true");
        check(antiBlur.Contains("'fullscreenchange'") && antiBlur.Contains("'webkitfullscreenchange'"), "GetAntiBlurScript intercepts fullscreenchange and webkitfullscreenchange");
        check(antiBlur.Contains("'fullscreenerror'") && antiBlur.Contains("'webkitfullscreenerror'"), "GetAntiBlurScript intercepts fullscreenerror events");
        check(antiBlur.Contains("fullscreenElement") && antiBlur.Contains("webkitFullscreenElement") && antiBlur.Contains("webkitCurrentFullScreenElement"), "GetAntiBlurScript spoofs document.fullscreenElement variants to document.documentElement");
        check(antiBlur.Contains("exitFullscreen") && antiBlur.Contains("webkitExitFullscreen"), "GetAntiBlurScript stubs exitFullscreen and webkitExitFullscreen to prevent proctoring drop");
        check(antiBlur.Contains("navigator.keyboard.lock"), "GetAntiBlurScript neutralizes navigator.keyboard.lock so fullscreen websites cannot trap hotkeys");

        var bookmarklet = BrowserScriptService.GetAntiBlurBookmarklet();
        check(bookmarklet.StartsWith("javascript:"), "GetAntiBlurBookmarklet starts with javascript: protocol prefix");
        check(bookmarklet.Contains(Uri.EscapeDataString("__streamGuardAntiBlur")), "GetAntiBlurBookmarklet contains URL-encoded AntiBlur script");

        // Verify SnippingOverlayWindow non-activating properties
        var snipOverlay = new StreamGuard.Views.SnippingOverlayWindow(null, 0, 0, 800, 600, false);
        check(!snipOverlay.ShowActivated, "SnippingOverlayWindow has ShowActivated = false");
        check(!snipOverlay.Focusable, "SnippingOverlayWindow has Focusable = false");
    }

    internal static void RunUiChecks(MainWindow window, Action<bool, string> check)
    {
        // Verify MainWindow has ShowActivated = false in PassiveFocusMode
        check(!window.ShowActivated, "MainWindow has ShowActivated = false in PassiveFocusMode");
        check(window.Topmost, "MainWindow has Topmost = true in PassiveFocusMode");

        // Verify ReassertTopmostIfPassive executes cleanly without errors
        window.ReassertTopmostIfPassive();
        check(true, "MainWindow.ReassertTopmostIfPassive executes cleanly");

        // Verify Quick Chrome Focus Lock button exists in title bar
        var btnQuickFocusLock = (Button?)window.FindName("BtnQuickChromeFocusLock");
        check(btnQuickFocusLock != null, "BtnQuickChromeFocusLock exists in MainWindow TitleBar");

        // Verify children of BtnQuickChromeFocusLock resolve to ButtonBase
        if (btnQuickFocusLock?.Content is FrameworkElement btnChild)
        {
            bool resolvesToButton = false;
            DependencyObject? curr = btnChild;
            while (curr != null)
            {
                if (curr is ButtonBase) { resolvesToButton = true; break; }
                curr = VisualTreeHelper.GetParent(curr) ?? (curr as FrameworkElement)?.Parent;
            }
            check(resolvesToButton, "BtnQuickChromeFocusLock child elements resolve to ButtonBase to prevent spurious titlebar drag");
        }

        // Verify Chrome Focus Lock button exists in Captions panel
        var btnCaptionFocusLock = (Button?)window.FindName("BtnCaptionChromeFocusLock");
        check(btnCaptionFocusLock != null, "BtnCaptionChromeFocusLock exists in Captions panel");

        // Verify CaptionTranscriptText is read-only and IsTextEntryVisual returns false for it
        var captionTranscriptText = (TextBox?)window.FindName("CaptionTranscriptText");
        check(captionTranscriptText != null && captionTranscriptText.IsReadOnly, "CaptionTranscriptText is configured with IsReadOnly = true");
        check(!MainWindow.IsTextEntryVisual(captionTranscriptText), "IsTextEntryVisual returns false for CaptionTranscriptText");

        // Verify PassiveFocusCheckBox is checked by default
        var passiveFocusBox = (CheckBox?)window.FindName("PassiveFocusCheckBox");
        check(passiveFocusBox != null && passiveFocusBox.IsChecked == true, "PassiveFocusCheckBox is checked by default");

        // Verify WndProcHook under PassiveFocusMode returns MA_NOACTIVATE for CaptionTranscriptText
        var wndProcMethod = typeof(MainWindow).GetMethod("WndProcHook", BindingFlags.Instance | BindingFlags.NonPublic);
        if (wndProcMethod != null)
        {
            var tabCaptions = (RadioButton?)window.FindName("TabCaptions");
            if (tabCaptions != null) tabCaptions.IsChecked = true;

            object[] args = { IntPtr.Zero, WM_MOUSEACTIVATE, IntPtr.Zero, IntPtr.Zero, false };
            var res = (IntPtr)wndProcMethod.Invoke(window, args)!;
            bool handled = (bool)args[4];
            check(res == (IntPtr)MA_NOACTIVATE && handled, "WndProcHook returns MA_NOACTIVATE in Captions tab without editable controls");

            // Verify IsMouseOverSidebarNavigation method exists and behaves safely
            var isNavMethod = typeof(MainWindow).GetMethod("IsMouseOverSidebarNavigation", BindingFlags.Instance | BindingFlags.NonPublic);
            check(isNavMethod != null, "IsMouseOverSidebarNavigation method exists on MainWindow");
        }
    }
}
