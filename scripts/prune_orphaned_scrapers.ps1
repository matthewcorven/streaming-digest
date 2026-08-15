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

# Remove stale scraper containers
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

# Remove old commit-hash-tagged images (keep only the most recent 3)
foreach ($imageName in @('scraper', 'streaming-digest-api', 'streaming-digest-worker', 'streaming-digest-whisper')) {
    Write-Output "[scraper-prune] Pruning old $imageName images (keeping 3 most recent)..."
    
    $imageList = & $dockerCmd images --format "table {{.Repository}}:{{.Tag}}\t{{.CreatedAt}}" --filter "reference=$imageName*" 2> $null
    if ($LASTEXITCODE -eq 0 -and $imageList.Count -gt 3) {
        # Skip header line and parse results
        $images = @()
        $imageList | Select-Object -Skip 1 | ForEach-Object {
            $parts = $_ -split '\s{2,}'
            if ($parts.Count -ge 2) {
                $images += @{ Image = $parts[0]; Created = [datetime]$parts[1] }
            }
        }
        
        # Sort by creation time descending and keep only the 3 most recent
        $toDelete = $images | Sort-Object -Property Created -Descending | Select-Object -Skip 3
        if ($toDelete.Count -gt 0) {
            foreach ($img in $toDelete) {
                Write-Output "[scraper-prune] Removing old image: $($img.Image)"
                & $dockerCmd rmi "$($img.Image)" *> $null
            }
        }
    }
}

Write-Output '[scraper-prune] Pruning unused Docker containers and images.'
& $dockerCmd container prune -f *> $null
& $dockerCmd image prune -f *> $null

Write-Output '[scraper-prune] Cleanup complete.'
