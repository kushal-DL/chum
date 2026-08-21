$Port  = 8000
$Label = "Whisper STT server"

$conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if (-not $conn) {
    Write-Host "$Label is not running (nothing is listening on port $Port)."
} else {
    $ownPid = ($conn | Select-Object -First 1).OwningProcess
    $proc   = Get-Process -Id $ownPid -ErrorAction SilentlyContinue
    Write-Host "Stopping $Label (PID $ownPid) on port $Port ..."
    Stop-Process -Id $ownPid -Force -ErrorAction SilentlyContinue
    if ($?) { Write-Host "$Label stopped." -ForegroundColor Green }
    else     { Write-Host "Failed to stop $Label. Try running from an elevated prompt." -ForegroundColor Red }
}

Read-Host "Press Enter to close"
