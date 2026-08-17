<#
.SYNOPSIS
    Starts a local, OpenAI-compatible Whisper transcription API for Chum.

.DESCRIPTION
    Launches whisper_api_server.py (FastAPI + uvicorn), which serves
    openai/whisper-large-v3-turbo on your GPU via DirectML behind a
    POST /v1/audio/transcriptions endpoint matching OpenAI's Audio API.

    Point Chum's STT provider settings at:
        Base URL : http://127.0.0.1:<Port>/v1
        API Key  : the key printed below (or blank if -NoAuth was used)
        Model    : whisper-large-v3-turbo

.PARAMETER Port
    Port to listen on. Default 8000.

.PARAMETER ApiKey
    Fixed API key to require. If omitted, a random key is generated each run.

.PARAMETER NoAuth
    Disable API key checking entirely (fine since this binds to localhost only).
#>
param(
    [int]$Port = 8000,
    [string]$ApiKey = "",
    [switch]$NoAuth
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $NoAuth -and [string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = -join ((48..57) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

if ($NoAuth) {
    $env:WHISPER_API_KEY = ""
} else {
    $env:WHISPER_API_KEY = $ApiKey
}

Write-Host ""
Write-Host "=== Chum Whisper API ===" -ForegroundColor Cyan
Write-Host "Base URL : http://127.0.0.1:$Port/v1"
if ($NoAuth) {
    Write-Host "API Key  : (none - auth disabled)"
} else {
    Write-Host "API Key  : $ApiKey"
}
Write-Host "Model    : whisper-large-v3-turbo"
Write-Host "========================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Enter these into Chum's STT provider settings. Starting server..." -ForegroundColor Yellow
Write-Host ""

python -m uvicorn whisper_api_server:app --host 127.0.0.1 --port $Port
