# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Fails when prohibited public-release wording, third-party contamination, or source-tree payloads are present.

.DESCRIPTION
  This check is safe to run before or after a local build. Generated build output
  folders such as .artifacts, bin, obj, TestResults, publish, and release are ignored
  because they are already excluded by .gitignore and can be removed by
  scripts\clean-local-artifacts.cmd.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$IgnoredDirectoryNames = @(
    ".git", ".vs", "bin", "obj", "out", "artifacts", ".artifacts",
    "evidence", "captures", "pcaps", "reports", "logs", ".idea", ".dotnet_home",
    "TestResults", "coverage", "publish", "release"
)

$ForbiddenFilePatterns = @(
    "*.dll", "*.exe", "*.pdb", "*.deps.json", "*.runtimeconfig.json",
    "*.nupkg", "*.snupkg", "*.pcap", "*.pcapng", "*.etl", "*.binlog",
    "*.log", "*.tmp", "*.cache", "*.suo", "*.user", "*.rsuser"
)

# Vendor manuals, exported help, screenshots, binaries, and copied external-stack
# materials must never enter the repository, regardless of file extension.
$ForbiddenThirdPartyFilePatterns = @(
    "*libiec61850*", "*iedscout*", "*ied scout*", "*svscout*", "*sv scout*",
    "*stationscout*", "*station scout*", "*omicron*", "*mz-automation*"
)

$ForbiddenTextPatterns = @(
    "ARIEC60870", "IEC60870", "IEC 60870", "IEC101", "IEC 101",
    "IEC103", "IEC 103", "IEC104", "IEC 104", "libiec61850",
    "MZ Automation", "OCR7SR12", "OMICRON_CMC",
    "IEDScout", "IED Scout", "StationScout", "Station Scout", "SVScout", "SV Scout",
    "C:\Users\", "C:\Program Files\dotnet\sdk", "blocked in the current sandbox", "_wpftmp"
)

# These files deliberately name third parties to document legal boundaries.
# They are reviewed legal/provenance records, not implementation guidance.
$AllowedLegalReferenceFiles = @(
    "THIRD_PARTY_NOTICES.md",
    "docs/CLEAN_ROOM_POLICY.md",
    "docs/THIRD_PARTY_CLEAN_ROOM_AUDIT_2026-07-14.md"
)

$Problems = New-Object System.Collections.Generic.List[string]

function Get-RepoRelativePath {
    param([Parameter(Mandatory=$true)][string]$Path)

    $FullPath = (Resolve-Path -LiteralPath $Path).Path
    return $FullPath.Substring($RepoRoot.Length).TrimStart(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
}

function Test-InRepoWorktree {
    param([Parameter(Mandatory=$true)][string]$Path)

    $FullPath = (Resolve-Path -LiteralPath $Path).Path
    if (-not $FullPath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $relative = Get-RepoRelativePath -Path $FullPath
    return -not ($relative -eq ".git" -or $relative.StartsWith(".git/", [System.StringComparison]::OrdinalIgnoreCase))
}

function Test-IsIgnoredGeneratedPath {
    param([Parameter(Mandatory=$true)][string]$Path)

    $relative = Get-RepoRelativePath -Path $Path
    if ([string]::IsNullOrWhiteSpace($relative)) { return $false }

    $parts = $relative -split '[\\/]+'
    foreach ($part in $parts) {
        if ($IgnoredDirectoryNames -contains $part) { return $true }
    }
    return $false
}

function Test-IsAllowedLegalReference {
    param([Parameter(Mandatory=$true)][string]$Path)

    $relative = Get-RepoRelativePath -Path $Path
    return $AllowedLegalReferenceFiles -contains $relative
}

foreach ($Pattern in $ForbiddenFilePatterns) {
    Get-ChildItem -Path $RepoRoot -Recurse -Force -File -Filter $Pattern -ErrorAction SilentlyContinue |
        Where-Object { (Test-InRepoWorktree $_.FullName) -and -not (Test-IsIgnoredGeneratedPath $_.FullName) } |
        ForEach-Object { $Problems.Add("Forbidden file: $($_.FullName)") }
}

Get-ChildItem -Path $RepoRoot -Recurse -Force -File -ErrorAction SilentlyContinue |
    Where-Object { (Test-InRepoWorktree $_.FullName) -and -not (Test-IsIgnoredGeneratedPath $_.FullName) } |
    ForEach-Object {
        $file = $_
        foreach ($pattern in $ForbiddenThirdPartyFilePatterns) {
            if ($file.Name -like $pattern -and -not (Test-IsAllowedLegalReference $file.FullName)) {
                $Problems.Add("Forbidden third-party-named file: $($file.FullName)")
                break
            }
        }
    }

$TextFiles = Get-ChildItem -Path $RepoRoot -Recurse -Force -File -Include *.md,*.cs,*.xml,*.xaml,*.ps1,*.cmd,*.yml,*.yaml,*.html,*.css,*.js,*.json,*.props,*.sln,*.slnx,*.txt -ErrorAction SilentlyContinue |
    Where-Object { (Test-InRepoWorktree $_.FullName) -and -not (Test-IsIgnoredGeneratedPath $_.FullName) }

foreach ($File in $TextFiles) {
    if ($File.FullName -like "*scripts\verify-source-clean.ps1") { continue }
    if (Test-IsAllowedLegalReference $File.FullName) { continue }

    $Content = Get-Content -Path $File.FullName -Raw -ErrorAction SilentlyContinue
    foreach ($Pattern in $ForbiddenTextPatterns) {
        if ($Content -match [regex]::Escape($Pattern)) {
            $Problems.Add("Forbidden text '$Pattern': $($File.FullName)")
        }
    }
}

if ($Problems.Count -gt 0) {
    foreach ($Problem in $Problems) {
        Write-Host "ERROR: $Problem" -ForegroundColor Red
    }
    throw "Source tree is not public-release clean. Found $($Problems.Count) problem(s)."
}

Write-Host "Source tree is public-release clean and third-party clean-room boundaries passed." -ForegroundColor Green
