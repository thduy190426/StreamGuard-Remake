@echo off
title StreamGuard Neutralizer - DISABLE
echo ============================================
echo   StreamGuard Neutralizer - DISABLE MODE
echo   Gỡ bỏ tất cả lớp phòng thủ
echo ============================================
echo.
echo Yêu cầu: Chạy với quyền Administrator
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0stream_guard_neutralizer.ps1" -Action Disable
echo.
echo Nhấn phím bất kỳ để đóng...
pause >nul
