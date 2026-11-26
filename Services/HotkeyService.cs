using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StreamGuard.Services
{
    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hmod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern IntPtr GetModuleHandle(string? lpModuleName);

        private const int WM_HOTKEY = 0x0312;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int LLKHF_EXTENDED = 0x01;
        private const int LLKHF_INJECTED = 0x10;
        private const int LLKHF_ALTDOWN = 0x20;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const int VK_PACKET = 0xE7;
        private static readonly IntPtr ReplayExtraInfo = new(0x5347_5250); // "SGRP"
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_A = 0x41;
        private const int VK_S = 0x53;
        private const int VK_G = 0x47;
        private const int VK_TAB = 0x09;
        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;
        private const int VK_D = 0x44;
        private const int VK_F4 = 0x73;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_ESCAPE = 0x1B;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        internal readonly record struct ReplayedKeyEvent(int VkCode, int ScanCode, int Flags, bool IsKeyDown);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public int VkCode;
            public int ScanCode;
            public int Flags;
            public int Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private IntPtr _hwnd;
        private HwndSource? _source;
        private readonly record struct FallbackHotkey(uint Modifiers, int VkCode, Action Callback);
        private readonly Dictionary<int, Action> _actions = new();
        private readonly Dictionary<int, FallbackHotkey> _fallbackHotkeys = new();
        private int _nextId = 9000;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _keyboardProc;
        private Action? _shiftSgAction;
        private Action? _ctrlShiftSgAction;
        private Action? _ctrlShiftAltAAction;
        private Func<bool>? _curtainEscapeCheck;
        private Action? _curtainEscapeAction;
        private bool _ctrlDown;
        private bool _shiftDown;
        private bool _altDown;
        private bool _winDown;
        private bool _aDown;
        private bool _sDown;
        private bool _gDown;
        private bool _shiftSgFired;
        private bool _ctrlShiftSgFired;
        private bool _ctrlShiftAltAFired;
        private bool _blockVirtualDesktopHotkeys;
        private long _lastBlockedHotkeyTicks;
        private readonly HashSet<int> _swallowedDownKeys = new();
        private bool _disposed;
        private readonly HashSet<int> _pressedKeys = new();
        private int _pendingVk;
        private int _pendingScanCode;
        private int _pendingFlags;
        private bool _chordRolloverInvalidated;

        internal Func<IReadOnlyList<ReplayedKeyEvent>, bool>? ReplayInputOverride { get; set; }

        public bool BlockVirtualDesktopHotkeys
        {
            get => _blockVirtualDesktopHotkeys;
            set
            {
                _blockVirtualDesktopHotkeys = value;
                if (value) EnsureKeyboardHook();
            }
        }

        public event Action<string>? VirtualDesktopHotkeyBlocked;

        // Modifiers
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CTRL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        public void Initialize(Window window)
        {
            _hwnd = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(HwndHook);
        }

        public int Register(uint modifiers, uint vk, Action callback)
        {
            if (_disposed || _hwnd == IntPtr.Zero || _source == null) return -1;
            int id = _nextId++;
            if (RegisterHotKey(_hwnd, id, modifiers | 0x4000 /* MOD_NOREPEAT */, vk))
            {
                _actions[id] = callback;
                return id;
            }

            // Fallback to WH_KEYBOARD_LL when RegisterHotKey fails (e.g. fullscreen/hotkey contention)
            _fallbackHotkeys[id] = new FallbackHotkey(modifiers, (int)vk, callback);
            EnsureKeyboardHook();
            return id;
        }

        internal int RegisterFallbackHotkey(uint modifiers, uint vk, Action callback)
        {
            if (_disposed) return -1;
            int id = _nextId++;
            _fallbackHotkeys[id] = new FallbackHotkey(modifiers, (int)vk, callback);
            EnsureKeyboardHook();
            return id;
        }

        public void RegisterShiftSgChord(Action callback)
        {
            _shiftSgAction = callback;
            EnsureKeyboardHook();
        }

        public void RegisterCtrlShiftSgChord(Action callback)
        {
            _ctrlShiftSgAction = callback;
            EnsureKeyboardHook();
        }

        public void RegisterCtrlShiftAltAChord(Action callback)
        {
            _ctrlShiftAltAAction = callback;
            EnsureKeyboardHook();
        }

        public void RegisterCurtainEscapeGuard(Func<bool> isCurtainVisible, Action onEscape)
        {
            _curtainEscapeCheck = isCurtainVisible;
            _curtainEscapeAction = onEscape;
            EnsureKeyboardHook();
        }

        private void EnsureKeyboardHook()
        {
            if (_disposed) return;
            if (_keyboardHook != IntPtr.Zero)
                return;

            _keyboardProc = LowLevelKeyboardHook;
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            var moduleHandle = module?.ModuleName != null
                ? GetModuleHandle(module.ModuleName)
                : IntPtr.Zero;
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        }

        public void Unregister(int id)
        {
            if (_actions.Remove(id))
            {
                UnregisterHotKey(_hwnd, id);
            }
            _fallbackHotkeys.Remove(id);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!_disposed && msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
            {
                action();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var id in _actions.Keys.ToList())
                UnregisterHotKey(_hwnd, id);
            _source?.RemoveHook(HwndHook);
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            _shiftSgAction = null;
            _ctrlShiftSgAction = null;
            _ctrlShiftAltAAction = null;
            _curtainEscapeCheck = null;
            _curtainEscapeAction = null;
            VirtualDesktopHotkeyBlocked = null;
            _actions.Clear();
            _fallbackHotkeys.Clear();
            _pressedKeys.Clear();
            _swallowedDownKeys.Clear();
            ClearPendingKey();
            _chordRolloverInvalidated = false;
            _source = null;
        }

        private static readonly int[] ModifierVirtualKeys =
        {
            VK_SHIFT, VK_LSHIFT, VK_RSHIFT,
            VK_CONTROL, VK_LCONTROL, VK_RCONTROL,
            VK_MENU, VK_LMENU, VK_RMENU,
            VK_LWIN, VK_RWIN
        };

        private IntPtr LowLevelKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                        if (data.ExtraInfo != ReplayExtraInfo &&
                            (data.Flags & LLKHF_INJECTED) == 0 &&
                            (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
                        {
                            ReconcileStaleModifiersWithOs(data.VkCode);
                        }

                        if (ProcessKeyboardEvent(msg, data.VkCode, data.ScanCode, data.Flags, data.ExtraInfo))
                        {
                            return (IntPtr)1;
                        }
                    }
                }
                catch
                {
                }
            }

            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        internal bool ProcessKeyboardEvent(int msg, int vkCode) =>
            ProcessKeyboardEvent(msg, vkCode, 0, 0, IntPtr.Zero);

        internal bool ProcessKeyboardEvent(int msg, int vkCode, int scanCode, int flags, IntPtr extraInfo)
        {
            if (_disposed) return false;
            var keyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

            if (extraInfo == ReplayExtraInfo)
            {
                UpdateChordState(vkCode, keyDown);
                return false;
            }

            if (_pendingVk != 0)
            {
                int partnerVk = _pendingVk == VK_S ? VK_G : VK_S;
                if (keyDown)
                {
                    if (vkCode == _pendingVk)
                    {
                        // Swallow auto-repeat while waiting to see if the partner key completes Shift+S+G
                        return true;
                    }

                    if (vkCode == partnerVk && _shiftDown && !_altDown && (flags & LLKHF_ALTDOWN) == 0 && !_winDown && IsShiftSgChordEnabledForCurrentModifiers())
                    {
                        // Chord completed: promote deferred key into swallowed chord keys
                        int deferredVk = _pendingVk;
                        ClearPendingKey();
                        _swallowedDownKeys.Add(deferredVk);
                        _pressedKeys.Add(deferredVk);
                        UpdateChordState(vkCode, true);
                        _swallowedDownKeys.Add(vkCode);
                        return true;
                    }

                    if (IsShiftKey(vkCode) || (IsControlKey(vkCode) && _ctrlDown))
                    {
                        UpdateChordStateCore(vkCode, true, (flags & LLKHF_ALTDOWN) != 0);
                        return false;
                    }

                    var pendingDown = new ReplayedKeyEvent(_pendingVk, _pendingScanCode, _pendingFlags, true);
                    var triggerDown = new ReplayedKeyEvent(vkCode, scanCode, flags, true);
                    ClearPendingKey();
                    UpdateChordStateCore(vkCode, true, (flags & LLKHF_ALTDOWN) != 0);
                    if (!IsChordModifierKey(vkCode))
                    {
                        _chordRolloverInvalidated = true;
                    }
                    return ReplayInputs(new[] { pendingDown, triggerDown });
                }
                else
                {
                    if (vkCode == _pendingVk)
                    {
                        var pendingDown = new ReplayedKeyEvent(_pendingVk, _pendingScanCode, _pendingFlags, true);
                        var triggerUp = new ReplayedKeyEvent(vkCode, scanCode, flags, false);
                        ClearPendingKey();
                        return ReplayInputs(new[] { pendingDown, triggerUp });
                    }

                    if (IsShiftKey(vkCode))
                    {
                        bool stillHasShift = _pressedKeys.Any(k => k != vkCode && IsShiftKey(k));
                        if (stillHasShift)
                        {
                            UpdateChordState(vkCode, false);
                            return false;
                        }

                        var pendingDown = new ReplayedKeyEvent(_pendingVk, _pendingScanCode, _pendingFlags, true);
                        var shiftUp = new ReplayedKeyEvent(vkCode, scanCode, flags, false);
                        ClearPendingKey();
                        UpdateChordState(vkCode, false);
                        return ReplayInputs(new[] { pendingDown, shiftUp });
                    }

                    if (IsControlKey(vkCode) && _ctrlDown)
                    {
                        bool stillHasCtrl = _pressedKeys.Any(k => k != vkCode && IsControlKey(k));
                        if (stillHasCtrl)
                        {
                            UpdateChordState(vkCode, false);
                            return false;
                        }

                        var pendingDown = new ReplayedKeyEvent(_pendingVk, _pendingScanCode, _pendingFlags, true);
                        var ctrlUp = new ReplayedKeyEvent(vkCode, scanCode, flags, false);
                        ClearPendingKey();
                        UpdateChordState(vkCode, false);
                        return ReplayInputs(new[] { pendingDown, ctrlUp });
                    }

                    // Unrelated keyup (e.g. previous typing rollover) should not flush pending chord key
                    UpdateChordState(vkCode, false);
                    return false;
                }
            }

            // Defer initial VK_S or non-Ctrl VK_G press when Shift is held (without Alt/Win) so Shift+S+G never leaks 'S' or 'G'
            if (keyDown && ShouldDeferInitialChordKey(vkCode, flags))
            {
                _pendingVk = vkCode;
                _pendingScanCode = scanCode;
                _pendingFlags = flags;
                return true;
            }

            if (keyDown && (_sDown || _gDown) && vkCode != VK_S && vkCode != VK_G && !IsChordModifierKey(vkCode))
            {
                _chordRolloverInvalidated = true;
            }

            bool wasAlreadyDown = keyDown && _pressedKeys.Contains(vkCode);
            UpdateChordStateCore(vkCode, keyDown, (flags & LLKHF_ALTDOWN) != 0);

            if (keyDown)
            {
                // Swallow auto-repeat keydowns of keys already swallowed by a chord/block, even if modifiers were released first
                if (_swallowedDownKeys.Contains(vkCode))
                {
                    return true;
                }

                if (ShouldBlockVirtualDesktopKey(vkCode, out var shortcutName))
                {
                    _swallowedDownKeys.Add(vkCode);
                    var now = Environment.TickCount64;
                    if (now - _lastBlockedHotkeyTicks > 800)
                    {
                        _lastBlockedHotkeyTicks = now;
                        var action = VirtualDesktopHotkeyBlocked;
                        if (action != null)
                        {
                            try { action.Invoke(shortcutName); } catch { }
                        }
                    }
                    return true; // Consume keydown to block Windows virtual desktop action
                }

                if (ShouldSwallowChordKeyCore(vkCode, true, (flags & LLKHF_ALTDOWN) != 0))
                {
                    _swallowedDownKeys.Add(vkCode);
                    return true; // Consume keydown to block character from leaking into other apps
                }

                if (!wasAlreadyDown && vkCode == VK_ESCAPE && _curtainEscapeCheck?.Invoke() == true)
                {
                    _swallowedDownKeys.Add(VK_ESCAPE);
                    QueueChordAction(_curtainEscapeAction);
                    return true;
                }

                if (!wasAlreadyDown && TryTriggerFallbackHotkey(vkCode, (flags & LLKHF_ALTDOWN) != 0))
                {
                    _swallowedDownKeys.Add(vkCode);
                    return true;
                }
            }
            else
            {
                // Symmetrically swallow keyup if key was swallowed on keydown
                if (_swallowedDownKeys.Remove(vkCode))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsShiftKey(int vkCode) =>
            vkCode == VK_SHIFT || vkCode == VK_LSHIFT || vkCode == VK_RSHIFT;

        private static bool IsControlKey(int vkCode) =>
            vkCode == VK_CONTROL || vkCode == VK_LCONTROL || vkCode == VK_RCONTROL;

        private static bool IsChordModifierKey(int vkCode) =>
            IsShiftKey(vkCode) || IsControlKey(vkCode) ||
            vkCode == VK_MENU || vkCode == VK_LMENU || vkCode == VK_RMENU ||
            vkCode == VK_LWIN || vkCode == VK_RWIN;

        private void ClearPendingKey()
        {
            _pendingVk = 0;
            _pendingScanCode = 0;
            _pendingFlags = 0;
        }

        private bool ShouldDeferInitialChordKey(int vkCode, int flags)
        {
            if (!_shiftDown || _altDown || (flags & LLKHF_ALTDOWN) != 0 || _winDown || _sDown || _gDown)
                return false;

            if (vkCode == VK_S)
            {
                return !_swallowedDownKeys.Contains(VK_S) &&
                       IsShiftSgChordEnabledForCurrentModifiers();
            }

            if (vkCode == VK_G)
            {
                // Only defer VK_G when Ctrl is NOT held so Ctrl+Shift+G (RegisterHotKey ToggleVisibility)
                // continues to pass straight through on KeyDown without deferral.
                return !_ctrlDown &&
                       _shiftSgAction != null &&
                       !_swallowedDownKeys.Contains(VK_G);
            }

            return false;
        }

        private bool IsShiftSgChordEnabledForCurrentModifiers() =>
            _ctrlDown ? _ctrlShiftSgAction != null : _shiftSgAction != null;

        private void ReconcileStaleModifiersWithOs(int currentVkCode)
        {
            bool changed = false;
            foreach (var modVk in ModifierVirtualKeys)
            {
                if (modVk != currentVkCode &&
                    _pressedKeys.Contains(modVk) &&
                    (GetAsyncKeyState(modVk) & 0x8000) == 0)
                {
                    _pressedKeys.Remove(modVk);
                    changed = true;
                }
            }

            if (!changed) return;

            RefreshChordStateFromPressedKeys();
            if (_pendingVk != 0 &&
                (!_shiftDown ||
                 (_pendingVk == VK_S && !IsShiftSgChordEnabledForCurrentModifiers()) ||
                 (_pendingVk == VK_G && (_ctrlDown || _shiftSgAction == null))))
            {
                ClearPendingKey();
            }
        }

        internal static KEYBDINPUT BuildKeybdInput(ReplayedKeyEvent ev)
        {
            if (ev.VkCode == VK_PACKET)
            {
                return new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)ev.ScanCode,
                    dwFlags = KEYEVENTF_UNICODE | (ev.IsKeyDown ? 0u : KEYEVENTF_KEYUP),
                    time = 0,
                    dwExtraInfo = ReplayExtraInfo
                };
            }

            uint dwFlags = (ev.Flags & LLKHF_EXTENDED) != 0 ? KEYEVENTF_EXTENDEDKEY : 0;
            if (!ev.IsKeyDown)
            {
                dwFlags |= KEYEVENTF_KEYUP;
            }

            return new KEYBDINPUT
            {
                wVk = (ushort)ev.VkCode,
                wScan = (ushort)ev.ScanCode,
                dwFlags = dwFlags,
                time = 0,
                dwExtraInfo = ReplayExtraInfo
            };
        }

        private bool ReplayInputs(IReadOnlyList<ReplayedKeyEvent> events)
        {
            if (ReplayInputOverride != null)
            {
                return ReplayInputOverride(events);
            }

            if (events.Count == 0) return true;
            var inputs = new INPUT[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                inputs[i] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion
                    {
                        ki = BuildKeybdInput(events[i])
                    }
                };
            }

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            return sent == (uint)inputs.Length;
        }

        internal bool ShouldSwallowChordKey(int vkCode, bool isKeyDown) =>
            ShouldSwallowChordKeyCore(vkCode, isKeyDown, false);

        private bool ShouldSwallowChordKeyCore(int vkCode, bool isKeyDown, bool osAltDown)
        {
            if (_disposed) return false;
            if (!_chordRolloverInvalidated && IsShiftSgChordEnabledForCurrentModifiers() && _shiftDown && !_altDown && !osAltDown && !_winDown)
            {
                if (vkCode == VK_G && _sDown)
                    return true;
                if (vkCode == VK_S && _gDown)
                    return true;
            }
            if (vkCode == VK_A && _ctrlDown && _shiftDown && (_altDown || osAltDown) && _ctrlShiftAltAAction != null)
            {
                return true;
            }
            return false;
        }

        internal void UpdateChordState(int vkCode, bool isDown) =>
            UpdateChordStateCore(vkCode, isDown, false);

        private void UpdateChordStateCore(int vkCode, bool isDown, bool osAltDown)
        {
            if (_disposed) return;
            if (isDown) _pressedKeys.Add(vkCode); else _pressedKeys.Remove(vkCode);
            RefreshChordStateFromPressedKeys(osAltDown);
        }

        private void RefreshChordStateFromPressedKeys(bool osAltDown = false)
        {
            _ctrlDown = _pressedKeys.Contains(VK_CONTROL) || _pressedKeys.Contains(VK_LCONTROL) || _pressedKeys.Contains(VK_RCONTROL);
            _shiftDown = _pressedKeys.Contains(VK_SHIFT) || _pressedKeys.Contains(VK_LSHIFT) || _pressedKeys.Contains(VK_RSHIFT);
            _altDown = _pressedKeys.Contains(VK_MENU) || _pressedKeys.Contains(VK_LMENU) || _pressedKeys.Contains(VK_RMENU);
            _winDown = _pressedKeys.Contains(VK_LWIN) || _pressedKeys.Contains(VK_RWIN);
            _aDown = _pressedKeys.Contains(VK_A);
            _sDown = _pressedKeys.Contains(VK_S);
            _gDown = _pressedKeys.Contains(VK_G);
            if (!_sDown && !_gDown && _pendingVk == 0)
            {
                _chordRolloverInvalidated = false;
            }

            UpdateShiftSgChord(osAltDown);
            UpdateCtrlShiftAltAChord();
        }

        internal bool ShouldBlockVirtualDesktopKey(int vkCode, out string shortcutName)
        {
            shortcutName = string.Empty;
            if (!_blockVirtualDesktopHotkeys) return false;

            if (vkCode != VK_TAB && vkCode != VK_D && vkCode != VK_F4 && vkCode != VK_LEFT && vkCode != VK_RIGHT)
                return false;

            bool winActive = _winDown || (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
            if (!winActive) return false;

            bool ctrlActive = _ctrlDown || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
            bool altActive = _altDown || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0;

            // 1. Win + Tab (Task View / Desktop overview). Must NOT block Alt + Tab or plain Tab.
            if (vkCode == VK_TAB && !altActive)
            {
                shortcutName = "Win + Tab";
                return true;
            }

            // 2. Win + Ctrl virtual desktop shortcuts:
            if (ctrlActive)
            {
                bool shiftActive = _shiftDown || (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;
                switch (vkCode)
                {
                    case VK_D:
                        shortcutName = "Win + Ctrl + D";
                        return true;
                    case VK_F4:
                        shortcutName = "Win + Ctrl + F4";
                        return true;
                    case VK_LEFT:
                        shortcutName = shiftActive ? "Win + Ctrl + Shift + Left Arrow" : "Win + Ctrl + Left Arrow";
                        return true;
                    case VK_RIGHT:
                        shortcutName = shiftActive ? "Win + Ctrl + Shift + Right Arrow" : "Win + Ctrl + Right Arrow";
                        return true;
                }
            }

            return false;
        }

        private void UpdateShiftSgChord(bool osAltDown = false)
        {
            if (_chordRolloverInvalidated || !(_shiftDown && _sDown && _gDown) || _altDown || osAltDown || _winDown)
            {
                _shiftSgFired = false;
                _ctrlShiftSgFired = false;
                return;
            }

            if (_ctrlDown)
            {
                if (_ctrlShiftSgFired)
                    return;

                _ctrlShiftSgFired = true;
                _shiftSgFired = true;
                var ctrlAction = _ctrlShiftSgAction;
                QueueChordAction(ctrlAction);
                return;
            }

            if (_shiftSgFired)
                return;

            _shiftSgFired = true;
            var action = _shiftSgAction;
            QueueChordAction(action);
        }

        private void UpdateCtrlShiftAltAChord()
        {
            if (!(_ctrlDown && _shiftDown && _altDown && _aDown))
            {
                _ctrlShiftAltAFired = false;
                return;
            }

            if (_ctrlShiftAltAFired)
                return;

            _ctrlShiftAltAFired = true;
            var action = _ctrlShiftAltAAction;
            QueueChordAction(action);
        }

        private void QueueChordAction(Action? action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (action == null || _disposed || dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
        }

        private bool TryTriggerFallbackHotkey(int vkCode, bool osAltDown)
        {
            if (_fallbackHotkeys.Count == 0) return false;
            bool altActive = _altDown || osAltDown;
            foreach (var entry in _fallbackHotkeys.Values)
            {
                if (entry.VkCode != vkCode) continue;
                bool needCtrl = (entry.Modifiers & MOD_CTRL) != 0;
                bool needShift = (entry.Modifiers & MOD_SHIFT) != 0;
                bool needAlt = (entry.Modifiers & MOD_ALT) != 0;
                bool needWin = (entry.Modifiers & MOD_WIN) != 0;
                if (_ctrlDown == needCtrl && _shiftDown == needShift && altActive == needAlt && _winDown == needWin)
                {
                    QueueChordAction(entry.Callback);
                    return true;
                }
            }
            return false;
        }
    }
}
