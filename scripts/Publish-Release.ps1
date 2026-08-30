#Requires -Version 5.1
<#
.SYNOPSIS
    Dev tool: build Chum, zip a self-contained deploy package, and (optionally)
    publish it as a GitHub Release so end users can one-click install via install.cmd.

.DESCRIPTION
    Run this on a machine that has the source code and the .NET SDK. It:
      1. Publishes Chum.Service and Chum.App self-contained (win-x64).
      2. Assembles a deploy package: App\, Service\, scripts\, install.cmd.
      3. Zips it to dist\chum-<version>.zip.
      4. With -Publish, creates/updates the matching GitHub Release and uploads
         the zip as an asset (requires the GitHub CLI 'gh', authenticated).

    End users then just download install.cmd (or the zip) and run it as admin;
    Install-Chum.ps1 pulls the latest release automatically when no binaries are
    present locally.

.PARAMETER Version   Release version/tag (e.g. 1.2.0). Default: AssemblyVersion from Chum.App.csproj.
.PARAMETER RepoSlug  GitHub owner/repo. Default: kushal-DL/chum
.PARAMETER Publish   Create/update the GitHub Release and upload the zip (needs 'gh').
.PARAMETER Draft     When publishing, create the release as a draft.
.PARAMETER OutputDir Where to write the zip. Default: <repo>\dist

.EXAMPLE
    .\scripts\Publish-Release.ps1
    # Builds dist\chum-0.1.0.zip locally (no upload).

.EXAMPLE
    .\scripts\Publish-Release.ps1 -Publish
    # Builds and publishes the release to GitHub so install.cmd can download it.
#>
param(
    [string]$Version   = '',
    [string]$RepoSlug  = 'kushal-DL/chum',
    [switch]$Publish,
    [switch]$Draft,
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir = $PSScriptRoot
$RepoRoot  = Split-Path $ScriptDir -Parent
$SrcRoot   = Join-Path $RepoRoot 'src'
$SvcProj   = Join-Path $SrcRoot 'Chum.Service\Chum.Service.csproj'
$AppProj   = Join-Path $SrcRoot 'Chum.App\Chum.App.csproj'

function Write-Step([string]$m) { Write-Host "  $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  [OK] $m" -ForegroundColor Green }

Write-Host "`nChum Release Publisher" -ForegroundColor White
Write-Host "-----------------------------------------" -ForegroundColor DarkGray

if (-not (Test-Path $SvcProj)) { throw "Not in a source tree: $SvcProj not found." }
if (-not (Test-Path $AppProj)) { throw "Not in a source tree: $AppProj not found." }

# --- Resolve version --------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Version)) {
    $csproj = Get-Content $AppProj -Raw
    $m = [regex]::Match($csproj, '<AssemblyVersion>\s*([0-9]+(\.[0-9]+){1,3})\s*</AssemblyVersion>')
    if (-not $m.Success) { throw "Could not read <AssemblyVersion> from $AppProj. Pass -Version explicitly." }
    $parts = $m.Groups[1].Value.Split('.')
    $Version = ($parts[0..([Math]::Min(2, $parts.Length - 1))] -join '.')  # first three octets
}
$Tag = "v$Version"
Write-Ok "Version $Version  (tag $Tag)"

if ($OutputDir -eq '') { $OutputDir = Join-Path $RepoRoot 'dist' }
$stageDir = Join-Path $OutputDir "chum-$Version"
$zipPath  = Join-Path $OutputDir "chum-$Version.zip"

# --- Clean staging ----------------------------------------------------------
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
if (Test-Path $zipPath)  { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

# --- Publish (self-contained) ----------------------------------------------
Write-Step "Publishing Chum.Service (self-contained, win-x64)..."
& dotnet publish $SvcProj -r win-x64 -c Release --self-contained true -o (Join-Path $stageDir 'Service') --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Chum.Service" }
Write-Ok "Chum.Service published"

Write-Step "Publishing Chum.App (self-contained, win-x64)..."
& dotnet publish $AppProj -r win-x64 -c Release --self-contained true -o (Join-Path $stageDir 'App') --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Chum.App" }
Write-Ok "Chum.App published"

# --- Assemble installer scripts --------------------------------------------
Write-Step "Adding installer scripts..."
$stageScripts = Join-Path $stageDir 'scripts'
New-Item -ItemType Directory -Force -Path $stageScripts | Out-Null
Copy-Item (Join-Path $ScriptDir 'Install-Chum.ps1')   $stageScripts -Force
Copy-Item (Join-Path $ScriptDir 'Uninstall-Chum.ps1') $stageScripts -Force
Copy-Item (Join-Path $RepoRoot  'install.cmd')         $stageDir     -Force
Write-Ok "Scripts added"

# --- Zip --------------------------------------------------------------------
Write-Step "Creating $zipPath ..."
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force
$zipMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Ok "Package created ($zipMB MB)"

# --- Publish to GitHub Releases --------------------------------------------
if ($Publish) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        Write-Warning "GitHub CLI 'gh' not found. Install from https://cli.github.com/ and run 'gh auth login'."
        Write-Warning "Zip is ready at $zipPath - upload it to a release named '$Tag' manually."
    } else {
        $exists = (& gh release view $Tag --repo $RepoSlug 2>$null)
        if ($LASTEXITCODE -eq 0 -and $exists) {
            Write-Step "Release $Tag exists - uploading asset (clobber)..."
            & gh release upload $Tag $zipPath --repo $RepoSlug --clobber
            if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
        } else {
            Write-Step "Creating release $Tag and uploading asset..."
            $ghArgs = @('release','create',$Tag,$zipPath,'--repo',$RepoSlug,
                        '--title',"Chum $Version",'--notes',"Chum $Version - run install.cmd as administrator to install.")
            if ($Draft) { $ghArgs += '--draft' }
            & gh @ghArgs
            if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
        }
        Write-Ok "Published to https://github.com/$RepoSlug/releases/tag/$Tag"
    }
}

Write-Host ""
Write-Host "  Deploy package: $zipPath" -ForegroundColor Green
if (-not $Publish) {
    Write-Host "  To publish it so install.cmd can auto-download:" -ForegroundColor DarkGray
    Write-Host "    .\scripts\Publish-Release.ps1 -Publish" -ForegroundColor DarkGray
}
Write-Host ""
