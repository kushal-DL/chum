@echo off
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0start-whisper-api.ps1" %*
echo.
echo PowerShell exited with code %ERRORLEVEL%
pause
