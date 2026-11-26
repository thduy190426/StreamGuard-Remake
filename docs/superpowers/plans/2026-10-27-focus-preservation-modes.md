# Focus Preservation Modes Implementation Plan (Passive Focus & Browser Anti-Blur)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate a passive non-activating window mode (`WS_EX_NOACTIVATE` / `MA_NOACTIVATE`) and a browser focus lock toolkit (anti-blur injection & emulate focused page helpers) into StreamGuard without breaking existing functionality or stealing focus from full-screen Chrome.

**Architecture:**
1. `Services/ConfigService.cs`: Persist `PassiveFocusMode` boolean in `config.json`.
2. `MainWindow.xaml.cs`: Attach `HwndSourceHook` for `WM_MOUSEACTIVATE` returning `MA_NOACTIVATE`, toggle `WS_EX_NOACTIVATE` on the window handle, support seamless text editing when clicking into textboxes, and implement clipboard/bookmarklet copy handlers for Chrome anti-blur.
3. `MainWindow.xaml`: Add UI cards in Tab 3 (Privacy & Security / `ViewThreats`) for "ACTIVE WINDOW & FOCUS PRESERVATION" and "BROWSER ANTI-BLUR & FOCUS LOCK (CHROME / EDGE)".

**Tech Stack:** C# .NET 8, WPF, Win32 User32 (`WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE`, `MA_NOACTIVATE`), JavaScript DOM event manipulation.

## Global Constraints
- Preserve all existing functionalities (OBS screen capture exclusion `WDA_EXCLUDEFROMCAPTURE`, global hotkeys, wrapped windows, search filtering, hotkey recorder).
- Zero warnings, zero errors on both Debug and Release builds.
- Seamless transition between passive clicking and active typing in textboxes.

---

## Tasks

### Task 1: Update ConfigService for PassiveFocusMode Persistence
- [ ] Add `PassiveFocusMode` property to `ConfigService.cs`.
- [ ] Add `PassiveFocusMode` property to `StreamGuardConfig`.
- [ ] Update `Load()` and `Save()` methods to include `PassiveFocusMode`.
- [ ] Verify build with `dotnet build`.

### Task 2: Implement Passive Non-Activating Window Logic in MainWindow.xaml.cs
- [ ] Add Win32 constants: `WM_MOUSEACTIVATE = 0x0021`, `MA_ACTIVATE = 1`, `MA_NOACTIVATE = 3`, `WS_EX_NOACTIVATE = 0x08000000`.
- [ ] Add `_passiveFocusMode` boolean field and `SetPassiveFocusMode(bool enable)` method.
- [ ] Attach `HwndSourceHook` in `OnSourceInitialized` to intercept `WM_MOUSEACTIVATE` and return `MA_NOACTIVATE`.
- [ ] Handle input focus for `TextBox` controls (`SearchBox`, `SecureHotkeyRecorder`) so typing still functions seamlessly when a user explicitly focuses an input box.
- [ ] Wire up `PassiveFocusCheckBox_Changed` event handler.

### Task 3: Implement Browser Anti-Blur & Focus Lock Helper in MainWindow.xaml.cs
- [ ] Create specialized JavaScript anti-blur snippet blocking `blur`, `focusout`, `visibilitychange`, and freezing `document.hasFocus = () => true` and `document.visibilityState = 'visible'`.
- [ ] Add `BtnCopyAntiBlurScript_Click` to copy JavaScript to Clipboard with toast feedback.
- [ ] Add `BtnCopyBookmarklet_Click` to copy bookmarklet format (`javascript:...`) to Clipboard with toast feedback.

### Task 4: Design & Add UI Controls in MainWindow.xaml
- [ ] In `ViewThreats` (Tab 3: Privacy & Security), add `ACTIVE WINDOW & FOCUS PRESERVATION` card containing `PassiveFocusCheckBox` and detailed tooltip/description.
- [ ] In `ViewThreats`, add `BROWSER ANTI-BLUR & FOCUS LOCK` card containing:
  - Button to copy anti-blur script.
  - Button to copy 1-click bookmarklet.
  - Quick reference guide for Chrome DevTools `Emulate a focused page`.

### Task 5: Verify Build, Stability & Absence of Conflicts
- [ ] Run `dotnet build` to confirm 0 warnings, 0 errors.
- [ ] Run `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`.
- [ ] Verify that global hotkeys, window cloaking, and searching/editing still function normally.
