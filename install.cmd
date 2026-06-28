@echo off
:: Chum installer launcher
:: Requires administrator rights - your IT team must approve and provide elevation.

:: Check whether already elevated
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo  Chum requires administrator access to install.
    echo  Requesting elevation - please approve the UAC prompt or provide admin credentials.
    echo.
    powershell -NoProfile -Command "Start-Process cmd -ArgumentList '/c \"%~f0\"' -Verb RunAs -Wait"
    exit /b
)

:: Running as admin - launch the PowerShell installer
echo.
echo  Running Chum service installer...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-Chum.ps1" -StartService
echo.
if %errorlevel% neq 0 (
    echo  Installation failed. Check the output above for details.
    pause
    exit /b %errorlevel%
)
echo  Done. Chum is installed and running.
pause
