@echo off
title StreamGuard Neutralizer - ENABLE
echo ============================================
echo   StreamGuard Neutralizer - ENABLE MODE
echo   Biến app thành "Người Thực Vật"
echo ============================================
echo.
echo Yêu cầu: Chạy với quyền Administrator
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0stream_guard_neutralizer.ps1" -Action Enable
echo.
echo Nhấn phím bất kỳ để đóng...
pause >nul
