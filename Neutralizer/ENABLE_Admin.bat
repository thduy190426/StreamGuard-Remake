@echo off
:: StreamGuard Neutralizer - Auto-elevate to Admin and Enable ALL layers
:: This script will request admin privileges automatically

>nul 2>&1 "%SYSTEMROOT%\System32\cacls.exe" "%SYSTEMROOT%\System32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Requesting Administrative Privileges...
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    exit /B

:gotAdmin
    if exist "%temp%\getadmin.vbs" ( del "%temp%\getadmin.vbs" )
    pushd "%CD%"
    CD /D "%~dp0"

title [ADMIN] StreamGuard Neutralizer - FULL ENABLE
echo.
echo ============================================
echo   StreamGuard Neutralizer - FULL ENABLE
echo   Running with ADMINISTRATOR privileges
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0stream_guard_neutralizer.ps1" -Action Enable
echo.
echo ============================================
echo   Done! Press any key to close...
echo ============================================
pause >nul
