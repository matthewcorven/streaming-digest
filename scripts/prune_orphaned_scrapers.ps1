#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Phase = 'manual'
)

Write-Output "[scraper-prune] phase=$Phase"

$dockerCmd = if ($env:DOCKER) { $env:DOCKER } else { 'docker' }

if (-not (Get-Command $dockerCmd -ErrorAction SilentlyContinue)) {
    Write-Output '[scraper-prune] Docker CLI not found; skipping cleanup.'
    exit 0
}

try {
    & $dockerCmd info *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Output '[scraper-prune] Docker daemon is not available; skipping cleanup.'
        exit 0
    }
}
catch {
    Write-Output '[scraper-prune] Docker daemon is not available; skipping cleanup.'
    exit 0
}

$containers = & $dockerCmd ps -aq --filter "name=streaming-digest-scraper"
if ($LASTEXITCODE -ne 0) {
    $containers = @()
}

if ($containers.Count -gt 0) {
    $containerList = ($containers | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', '
    Write-Output "[scraper-prune] Removing stale scraper containers: $containerList"
    & $dockerCmd rm -f $containers *> $null
}
else {
    Write-Output '[scraper-prune] No stale scraper containers found.'
}

Write-Output '[scraper-prune] Pruning unused Docker containers and images.'
& $dockerCmd container prune -f *> $null
& $dockerCmd image prune -f *> $null

Write-Output '[scraper-prune] Cleanup complete.'
