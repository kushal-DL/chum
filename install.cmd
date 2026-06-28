@echo off
:: Chum installer
:: Right-click -> "Run as administrator"

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
echo  Launching Chum tray app via scheduled task (runs as current user)...
schtasks /Run /TN "Chum Tray Application" >nul 2>&1

echo.
echo  Done. Chum is installed and running.
echo  Look for the Chum icon in your system tray (bottom-right).
echo  Click the ^ arrow near the clock if you don't see it.
echo  Right-click the tray icon and choose Settings to enter your API key.
echo.
pause
