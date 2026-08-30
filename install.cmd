@echo off
setlocal
title Chum Installer

:: ---------------------------------------------------------------------------
::  Chum one-step installer.
::  Just right-click this file and choose "Run as administrator"
::  (or simply double-click it - it will request admin rights for you).
:: ---------------------------------------------------------------------------

:: --- Self-elevate to Administrator if needed -------------------------------
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo  Chum needs administrator rights to install a Windows service.
    echo  Requesting elevation...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo  Chum Installer
echo  ======================
echo.

set "PS1=%~dp0scripts\Install-Chum.ps1"

if exist "%PS1%" (
    rem Normal case: installer script ships next to us (repo or deploy package).
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -StartService
) else (
    rem Bootstrap: only install.cmd is present - fetch the installer, which will
    rem then download the published release binaries from GitHub.
    echo  Installer script not found locally. Downloading it from GitHub...
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; try { Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/kushal-DL/chum/main/scripts/Install-Chum.ps1' -OutFile \"$env:TEMP\Install-Chum.ps1\" } catch { exit 1 }"
    if not exist "%TEMP%\Install-Chum.ps1" (
        echo.
        echo  Could not download the installer. Check your internet connection
        echo  and try again, or download the release ZIP from:
        echo    https://github.com/kushal-DL/chum/releases
        echo.
        pause
        exit /b 1
    )
    powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\Install-Chum.ps1" -StartService -Source download
)

set "RC=%errorlevel%"
if not "%RC%"=="0" (
    echo.
    echo  Installation failed ^(exit %RC%^). See the output above for details.
    pause
    exit /b %RC%
)

echo.
echo  Launching Chum tray app...
schtasks /Run /TN "Chum Tray Application" >nul 2>&1

echo.
echo  Done. Chum is installed and running.
echo  Look for the Chum icon in your system tray (bottom-right).
echo  Click the ^ arrow near the clock if you don't see it.
echo  Right-click the tray icon and choose Settings to enter your API key.
echo.
pause
