@echo off
:: Chum installer
:: Open this file as Administrator: right-click install.cmd -> "Run as administrator"
:: PowerShell will tell you if admin rights are missing.

echo.
echo  Chum Service Installer
echo  ----------------------
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
