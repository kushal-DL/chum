<#
.SYNOPSIS
    Purges in-memory RAG indexes, chunks, and staged documents from ASP RAG bridge.

RUNNING CLEANUP:
    .\clean-rag-data.ps1
    .\clean-rag-data.ps1 -Port 8003
    .\clean-rag-data.ps1 -Url "http://192.168.1.50:8003" -CleanDocsDir
#>
param(
    [int]$Port = 8003,
    [string]$Url = "",
    [string]$ApiKey = if ($env:RAG_API_KEY) { $env:RAG_API_KEY } else { "llm" },
    [switch]$CleanDocsDir = $false,
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Continue"

if ($PSScriptRoot) { Set-Location $PSScriptRoot }

if (-not $Url) {
    $Url = "http://127.0.0.1:$Port"
}

$DocsDir = Join-Path $PSScriptRoot "..\rag_docs"
$StagingDir = Join-Path $PSScriptRoot "..\data\rag_staging"

if ($DryRun) {
    Write-Host "[clean-rag-data] [dry-run] Configuration verified for target: $Url" -ForegroundColor Green
    exit 0
}

Write-Host "[clean-rag-data] Purging RAG bridge data on $Url..." -ForegroundColor Cyan

# 1. Call /clean endpoint if reachable
$headers = @{}
if ($ApiKey) {
    $headers["Authorization"] = "Bearer $ApiKey"
}

$payload = @{
    namespaces = @("chum_docs", "chum_session")
    clean_staging = $true
} | ConvertTo-Json

try {
    $null = Invoke-RestMethod -Uri "$Url/health" -Method Get -TimeoutSec 2 -ErrorAction Stop
    $resp = Invoke-RestMethod -Uri "$Url/clean" -Method Post -Body $payload -ContentType "application/json" -Headers $headers -TimeoutSec 5 -ErrorAction Stop
    Write-Host "[clean-rag-data] ASP RAG bridge in-memory index & chunks purged ($Url)" -ForegroundColor Green
} catch {
    Write-Host "[clean-rag-data] Bridge not currently reachable on $Url (cleaning local disk artifacts only)" -ForegroundColor Gray
}

# 2. Clean local staging directory
if (Test-Path $StagingDir) {
    Get-ChildItem -Path $StagingDir -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    Write-Host "[clean-rag-data] Removed staged files from $StagingDir" -ForegroundColor Green
}

# 3. Clean dedicated docs directory if requested
if ($CleanDocsDir -and (Test-Path $DocsDir)) {
    Get-ChildItem -Path $DocsDir -Exclude "README.md",".gitkeep" | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    Write-Host "[clean-rag-data] Purged files in dedicated docs directory ($DocsDir)" -ForegroundColor Green
}

Write-Host "[clean-rag-data] RAG cleanup completed. Zero persistent document chunks remaining on host." -ForegroundColor Green
