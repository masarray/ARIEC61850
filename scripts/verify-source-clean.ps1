# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Fails when tracked repository content contains prohibited public-release wording,
  third-party contamination, confidential evidence, or binary payloads.

.DESCRIPTION
  The legal gate scans every Git-tracked path rather than skipping directories by
  name. This prevents proprietary material from being hidden below folders such as
  captures, evidence, logs, or a product-named directory, while avoiding false
  positives from untracked local build output.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$ForbiddenFilePatterns = @(
    "*.dll", "*.exe", "*.pdb", "*.deps.json", "*.runtimeconfig.json",
    "*.nupkg", "*.snupkg", "*.pcap", "*.pcapng", "*.etl", "*.binlog",
    "*.log", "*.tmp", "*.cache", "*.suo", "*.user", "*.rsuser",
    "*.pdf", "*.chm", "*.hlp"
)

# Match the complete repo-relative path, not only the leaf filename. This also
# blocks assets hidden below a product-named folder.
$ForbiddenThirdPartyPathPatterns = @(
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

$TextExtensions = @(
    ".md", ".cs", ".xml", ".xaml", ".ps1", ".cmd", ".yml", ".yaml",
    ".html", ".css", ".js", ".json", ".props", ".targets", ".sln", ".slnx", ".txt"
)

# These files deliberately name third parties to document legal boundaries.
# They are reviewed legal/provenance records, not implementation guidance.
$AllowedLegalReferenceFiles = @(
    "THIRD_PARTY_NOTICES.md",
    "docs/CLEAN_ROOM_POLICY.md",
    "docs/THIRD_PARTY_CLEAN_ROOM_AUDIT_2026-07-14.md"
)

$Problems = New-Object System.Collections.Generic.List[string]

function Normalize-RelativePath {
    param([Parameter(Mandatory=$true)][string]$Path)
    return $Path.Replace('\', '/').TrimStart('/')
}

function Test-IsAllowedLegalReference {
    param([Parameter(Mandatory=$true)][string]$RelativePath)
    return $AllowedLegalReferenceFiles -contains (Normalize-RelativePath $RelativePath)
}

function Get-TrackedRelativePaths {
    $paths = @(& git -C $RepoRoot ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate Git-tracked files for source-clean verification."
    }

    return @(
        $paths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Normalize-RelativePath $_ }
    )
}

foreach ($relative in (Get-TrackedRelativePaths)) {
    $platformRelative = $relative.Replace([char]'/', [IO.Path]::DirectorySeparatorChar)
    $fullPath = Join-Path $RepoRoot $platformRelative

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $Problems.Add("Tracked path is missing from the worktree: $relative")
        continue
    }

    foreach ($pattern in $ForbiddenFilePatterns) {
        if ($relative -like $pattern) {
            $Problems.Add("Forbidden tracked file: $relative")
            break
        }
    }

    if (-not (Test-IsAllowedLegalReference $relative)) {
        foreach ($pattern in $ForbiddenThirdPartyPathPatterns) {
            if ($relative -like $pattern) {
                $Problems.Add("Forbidden third-party-named path: $relative")
                break
            }
        }
    }

    if ($relative -eq "scripts/verify-source-clean.ps1") { continue }
    if (Test-IsAllowedLegalReference $relative) { continue }
    if ($TextExtensions -notcontains [IO.Path]::GetExtension($relative).ToLowerInvariant()) { continue }

    $content = Get-Content -LiteralPath $fullPath -Raw -ErrorAction SilentlyContinue
    foreach ($pattern in $ForbiddenTextPatterns) {
        if ($content -match [regex]::Escape($pattern)) {
            $Problems.Add("Forbidden text '$pattern': $relative")
        }
    }
}

if ($Problems.Count -gt 0) {
    foreach ($problem in ($Problems | Sort-Object -Unique)) {
        Write-Host "ERROR: $problem" -ForegroundColor Red
    }
    throw "Source tree is not public-release clean. Found $($Problems.Count) problem(s)."
}

Write-Host "All Git-tracked content passed source-clean and third-party clean-room checks." -ForegroundColor Green
