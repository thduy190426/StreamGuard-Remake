@echo off
title StreamGuard Neutralizer - LIVE MONITOR
echo ============================================
echo   StreamGuard Neutralizer - LIVE MONITOR
echo   Giám sát liên tục (Ctrl+C để dừng)
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0stream_guard_neutralizer.ps1" -Action Monitor
