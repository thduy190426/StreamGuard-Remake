@echo off
title StreamGuard Neutralizer - STATUS
echo ============================================
echo   StreamGuard Neutralizer - STATUS CHECK
echo   Kiểm tra trạng thái khắc chế
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0stream_guard_neutralizer.ps1" -Action Status
echo.
echo Nhấn phím bất kỳ để đóng...
pause >nul
