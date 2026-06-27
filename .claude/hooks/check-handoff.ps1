# check-handoff.ps1
# Stop hook: blocks Claude from ending its turn when source code was modified
# without also updating session-handoff.md and BACKLOG-STATUS.md.
# Also warns (non-blocking) when new files/directories are created that may
# require a REPO_STRUCTURE.md update.
#
# Exit 0 = all good, allow stop
# Exit 2 = required docs missing, block stop and show message to Claude

Set-Location (Split-Path $PSScriptRoot -Parent | Split-Path -Parent)

$statusLines = git status --porcelain 2>$null
if (-not $statusLines) { exit 0 }

# --- Check 1 (blocking): code changed without updating handoff + backlog ---

$codeChanged = @($statusLines | Where-Object { $_ -match '\.(cs|xaml|csproj)\s*$' })

if ($codeChanged.Count -gt 0) {
    $handoffUpdated = $statusLines | Where-Object { $_ -match 'session-handoff\.md' }
    $backlogUpdated = $statusLines | Where-Object { $_ -match 'BACKLOG-STATUS\.md' }

    $missing = @()
    if (-not $handoffUpdated) { $missing += 'session-handoff.md' }
    if (-not $backlogUpdated) { $missing += 'product-backlog/BACKLOG-STATUS.md' }

    if ($missing.Count -gt 0) {
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
    }
}

# --- Check 2 (blocking): new directories created without REPO_STRUCTURE.md update ---
# A new directory suggests a structural change; REPO_STRUCTURE.md should reflect it.

# New untracked directories appear in git status as "?? path/" or new staged dirs as "A  path/file"
# Detect truly new directories: entries with "??" prefix that end in "/" (new untracked dir)
# or staged new files ("A ") whose parent directory doesn't already appear in REPO_STRUCTURE.md

$newDirs = @($statusLines | Where-Object { $_ -match '^\?\? ' } | ForEach-Object {
    $path = $_ -replace '^.{3}', ''
    # Get the first new path segment under src/ that isn't already a known directory
    if ($path -match '^src/[^/]+/([^/]+)/') { $Matches[1] }
} | Where-Object { $_ } | Sort-Object -Unique)

if ($newDirs.Count -gt 0) {
    $repoStructureUpdated = $statusLines | Where-Object { $_ -match 'REPO_STRUCTURE\.md' }
    if (-not $repoStructureUpdated) {
        Write-Output "REPO STRUCTURE CHECK: New directories detected under src/: $($newDirs -join ', ')"
        Write-Output ""
        Write-Output "If these directories represent a new structural pattern (new project, new layer, new subsystem):"
        Write-Output "  -> Update REPO_STRUCTURE.md to document them."
        Write-Output "If they are just new class files in an existing directory, no update is needed -- clear this by updating REPO_STRUCTURE.md with a trivial whitespace touch, or confirm the directories are already documented."
        Write-Output ""
        Write-Output "Re-read REPO_STRUCTURE.md now and decide whether it needs updating before finishing."
        exit 2
    }
}

exit 0
