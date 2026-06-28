param([string]$RepoRoot = "$PSScriptRoot\..")

$dst = "$env:ProgramFiles\Chum\App"

if (!(Test-Path $dst)) { Write-Error "Chum not installed at $dst. Run Install-Chum.ps1 as admin first."; exit 1 }

# Build all projects in Release first
Write-Host "Building Release..."
dotnet build "$RepoRoot\src\Chum.sln" -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

$projects = @{
    "Chum.App"           = "$RepoRoot\src\Chum.App\bin\Release\net10.0-windows"
    "Chum.Audio"         = "$RepoRoot\src\Chum.Audio\bin\Release\net10.0-windows"
    "Chum.Llm"           = "$RepoRoot\src\Chum.Llm\bin\Release\net10.0-windows"
    "Chum.Transcription" = "$RepoRoot\src\Chum.Transcription\bin\Release\net10.0-windows"
}

$dlls = @("Chum.App.dll","Chum.App.pdb",
          "Chum.Audio.dll","Chum.Audio.pdb",
          "Chum.Llm.dll","Chum.Llm.pdb",
          "Chum.Transcription.dll","Chum.Transcription.pdb")

# Stop tray app if running
$proc = Get-Process -Name "Chum.App" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "Stopping Chum.App (PID $($proc.Id))..."
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 1
}

foreach ($dll in $dlls) {
    $copied = $false
    foreach ($proj in $projects.Values) {
        $src = Join-Path $proj $dll
        if (Test-Path $src) {
            try {
                Copy-Item $src (Join-Path $dst $dll) -Force
                Write-Host "  [OK] $dll"
                $copied = $true
                break
            } catch {
                Write-Warning "  [FAIL] $dll — $_"
            }
        }
    }
    if (-not $copied) { Write-Host "  [SKIP] $dll (not found in any project bin)" }
}

Write-Host "`nDone. Restart Chum from the Start Menu or tray."
