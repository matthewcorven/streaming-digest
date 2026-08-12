#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..')).Path

$defaultContract = Join-Path $repoRoot 'src/StreamingDigest.AppHost/required-parameters.localdev.txt'
$contractFile = if ($args.Count -gt 0 -and $args[0]) { $args[0] } else { $defaultContract }
$appHostProject = Join-Path $repoRoot 'src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj'

if (-not (Test-Path -Path $contractFile -PathType Leaf)) {
    Write-Error "Contract file not found: $contractFile"
    exit 2
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet not found in PATH.'
    exit 127
}

$secretsLines = dotnet user-secrets list --project $appHostProject 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Unable to read user secrets for $appHostProject"
    exit 2
}

$foundKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($line in $secretsLines) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = $line.Split('=', 2)
    $key = $parts[0].Trim()
    if ($key.Length -gt 0) {
        [void]$foundKeys.Add($key)
    }
}

$missing = New-Object 'System.Collections.Generic.List[string]'
Get-Content -Path $contractFile | ForEach-Object {
    $requiredKey = $_.Trim()
    if ([string]::IsNullOrWhiteSpace($requiredKey) -or $requiredKey.StartsWith('#')) {
        return
    }

    if (-not $foundKeys.Contains($requiredKey)) {
        $missing.Add($requiredKey)
        Write-Output "MISSING_KEY $requiredKey"
    }
}

if ($missing.Count -gt 0) {
    Write-Error "Missing required AppHost parameter keys ($($missing.Count))."
    exit 1
}

Write-Output 'All required AppHost parameter keys are present.'