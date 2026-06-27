# check-handoff.ps1
# Stop hook: blocks Claude from ending its turn when source code was modified
# without also updating session-handoff.md and BACKLOG-STATUS.md.
#
# Exit 0 = docs are up to date, allow stop
# Exit 2 = docs are missing, block stop and show message to Claude

Set-Location (Split-Path $PSScriptRoot -Parent | Split-Path -Parent)

$statusLines = git status --porcelain 2>$null
if (-not $statusLines) { exit 0 }

# Detect modified source code files (.cs / .xaml / .csproj)
$codeChanged = @($statusLines | Where-Object { $_ -match '\.(cs|xaml|csproj)\s*$' })
if ($codeChanged.Count -eq 0) { exit 0 }

# Check whether the mandatory doc files were also touched
$handoffUpdated = $statusLines | Where-Object { $_ -match 'session-handoff\.md' }
$backlogUpdated = $statusLines | Where-Object { $_ -match 'BACKLOG-STATUS\.md' }

$missing = @()
if (-not $handoffUpdated) { $missing += 'session-handoff.md' }
if (-not $backlogUpdated) { $missing += 'product-backlog/BACKLOG-STATUS.md' }

if ($missing.Count -eq 0) { exit 0 }

# Summarise which code files triggered this
# Strip the 3-char git status prefix (XY + space) WITHOUT trimming first
$sample = ($codeChanged | Select-Object -First 3 | ForEach-Object { $_ -replace '^.{3}', '' }) -join ', '
if ($codeChanged.Count -gt 3) { $sample += " (and $($codeChanged.Count - 3) more)" }

Write-Output "DOCS NOT UPDATED: $($missing -join ' and ') must be updated before finishing."
Write-Output ""
Write-Output "Code files modified this turn: $sample"
Write-Output ""
Write-Output "Required actions:"
Write-Output "1. Update session-handoff.md -- add a 'What Was Done' entry for this turn."
Write-Output "2. Update product-backlog/BACKLOG-STATUS.md -- set story statuses to Scaffolded/Built for any stories whose code changed."
exit 2
