# Smooth Window Animations (Maximize / Minimize / Restore) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement fluent, GPU-accelerated smooth animations for window Maximize, Minimize, Restore, and Close-to-Tray in StreamGuard WPF application.

**Architecture:** A dedicated animation management layer inside `MainWindow.xaml.cs` utilizing WPF `Storyboard`, `DoubleAnimation`, and cubic easing functions (`CubicEase`, `QuadraticEase`). Window content scale and translation transforms are embedded in `MainWindow.xaml` with `RenderTransform` on the root Grid. Smooth bounds transitions (`Left`, `Top`, `Width`, `Height`) are applied during Maximize / Restore to prevent jarring frame snaps or DWM black/white flicker on borderless windows.

**Tech Stack:** C# .NET 8, WPF, XAML, WPF Animation System (Storyboards, DoubleAnimation, EasingFunctions).

## Global Constraints
- Must not introduce any flickering, black background, or artifacts on borderless `WindowChrome`.
- Must respect multi-monitor setups and DPI scaling (convert Win32 `WorkingArea` to WPF DIPs).
- Must preserve all existing stealth protection, hotkeys, and window reconciliation behaviors.
- Must compile cleanly with 0 warnings and 0 errors.

---

## Tasks

### Task 1: XAML Infrastructure for Render Transforms
- [x] Add `x:Name="RootGrid"` and configure `RenderTransform` (ScaleTransform + TranslateTransform) with `RenderTransformOrigin="0.5, 0.5"` in `MainWindow.xaml`.
- [x] Add `x:Name="BtnMaximize"` and `x:Name="IconMaximizePath"` to the Maximize caption button in `MainWindow.xaml` for dynamic icon toggling.
- [x] Verify XAML validity and build.

### Task 2: Smooth Minimize, Restore, and Close-to-Tray Animations
- [x] Implement `AnimateMinimizeSmooth()`: Scale 1.0 -> 0.88, TranslateY 0 -> 35px, Opacity 1.0 -> 0.0 with `EaseInQuad` (180ms), then set `WindowState = WindowState.Minimized`.
- [x] Implement `AnimateRestoreSmooth()`: Pre-set Scale 0.93, TranslateY 25px, Opacity 0.0 -> animate to Scale 1.0, TranslateY 0, Opacity 1.0 with `EaseOutCubic` (220ms).
- [x] Implement `AnimateCloseToTraySmooth()`: Smooth fade-out + scale-down, then call `HideToTray()`.
- [x] Hook `BtnMinimize_Click`, `BtnClose_Click`, and `ShowFromBackground()` / `Window_StateChanged`.

### Task 3: Smooth Maximize and Restore Bounds Animations
- [x] Implement `ToggleMaximizeRestoreSmooth()`:
  - When in Normal state: store `_normalRestoreBounds = new Rect(Left, Top, Width, Height)`, calculate target monitor WorkingArea in WPF DIPs, animate `Left`, `Top`, `Width`, `Height` to WorkingArea in 220ms with `EaseOutCubic`. Update button icon to 2-squares (Restore) icon.
  - When in Maximized state: animate `Left`, `Top`, `Width`, `Height` back to `_normalRestoreBounds` in 220ms with `EaseOutCubic`. Update button icon to 1-square (Maximize) icon.
- [x] Hook `BtnMaximize_Click` and title bar double-click (`TitleBar_MouseDown` when `e.ClickCount == 2`).

### Task 4: Compilation, Testing, and Verification
- [x] Compile Debug and Release (`dotnet build` and `dotnet publish`).
- [x] Verify 0 warnings, 0 errors.
- [x] Verify animations run smoothly with no stutter or frame lock.
