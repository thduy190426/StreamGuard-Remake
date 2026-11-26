# Comprehensive Optimization Implementation Plan (Core, Performance, UI/UX)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Optimize StreamGuard across all dimensions: eliminate COM allocation thrashing in the 50ms wrapper loop, support fullscreen maximize for wrapped apps (Antigravity IDE), prune dead window handles, enhance hotkey badge UI/UX, and ensure rock-solid stability with zero regressions.

**Architecture:** 
1. `ProtectedWindowWrapperService.cs`: Throttle COM shell scrubbing to a 2-second heartbeat, implement `IsZoomed` tracking for native child window fullscreen maximization, and add smooth fade transitions.
2. `MainWindow.xaml.cs`: Prune invalid/closed HWNDs in `ReconcileProtectedWindows()`, enhance single-instance reactivation visual feedback.
3. `MainWindow.xaml`: Re-style the hotkey pill into a modern, prominent keyboard badge with intuitive tooltips and crisp typography.

**Tech Stack:** C# .NET 8, WPF, WinForms interop (`MinimalWrapHostForm`), Win32 User32 / DWM APIs.

## Global Constraints
- Preserve all existing functionalities (OBS/screen capture exclusion, hotkeys, process containment).
- Zero warnings, zero errors on both Debug and Release builds.
- No flickering, no frame drops, no memory leaks.

---

## Tasks

### Task 1: Wrapper Host Performance & Child Window Maximization
- [ ] In `Services/ProtectedWindowWrapperService.cs`, throttle `ScrubProtectedShellEntries` and `ApplyCaptureShield` in the 50ms timer to run once every 40 ticks (~2 seconds) instead of every tick.
- [ ] Add `[DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);` in `ProtectedWindowWrapperService`.
- [ ] In `MinimalWrapHostForm` timer, check `IsZoomed(_child)`: if true, expand host to current monitor's `WorkingArea`; if restored from zoom, return to `_normalBounds`.
- [ ] Verify build and behavior.

### Task 2: Dead Window Handles Pruning in Reconcile Loop
- [ ] In `MainWindow.xaml.cs`, inside `ReconcileProtectedWindows()`, add pruning of dead handles: `app.ProtectedWindows.RemoveAll(s => s.Handle == IntPtr.Zero || !IsWindow(s.Handle));`.
- [ ] In `BringProtectedAppToFront()`, verify `IsWindow` before attempting to bring to front, removing dead handles if needed.
- [ ] Verify build.

### Task 3: UI/UX Modernization for Hotkey Badge & Controls
- [ ] In `MainWindow.xaml`, redesign the `SecureHotkey` control into a prominent keyboard badge with `FontSize="11"`, `Height="22"`, distinct keyboard icon, and clear tooltip.
- [ ] In `MainWindow.xaml.cs`, improve user experience on hotkey change with validation feedback.
- [ ] Verify XAML parsing and layout rendering.

### Task 4: Complete Verification and Publication
- [ ] Run `dotnet build` (Debug).
- [ ] Run `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`.
- [ ] Verify 0 warnings, 0 errors.
