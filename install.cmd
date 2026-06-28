@echo off
:: Chum installer
:: Must be run as Administrator: right-click install.cmd and choose "Run as administrator"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo  ERROR: Administrator access required.
    echo  Right-click install.cmd and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

echo.
echo  Running Chum service installer...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-Chum.ps1" -StartService
if %errorlevel% neq 0 (
    echo.
    echo  Installation failed. See output above for details.
    pause
    exit /b %errorlevel%
)
echo.
echo  Done. Chum is installed and running.
pause
