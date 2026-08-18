@echo off
REM ============================================================
REM  Stop the Chum Whisper STT server
REM  (whisper-server.exe listening on port 8000)
REM
REM  Finds the process bound to the port and kills only that one,
REM  so the Qwen server on 8001 is left running.
REM ============================================================
setlocal
set "PORT=8000"
set "LABEL=Whisper STT server"

set "PID="
for /f "tokens=5" %%P in ('netstat -ano ^| findstr /c:"LISTENING" ^| findstr /c:":%PORT%"') do set "PID=%%P"

if not defined PID (
    echo %LABEL% is not running ^(nothing is listening on port %PORT%^).
    goto :end
)

echo Stopping %LABEL% ^(PID %PID%^) on port %PORT% ...
taskkill /PID %PID% /F
if errorlevel 1 (
    echo Failed to stop %LABEL%. Try running this from an elevated prompt.
) else (
    echo %LABEL% stopped.
)

:end
endlocal
pause
