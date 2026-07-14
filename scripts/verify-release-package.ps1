# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Performs a structural and licensing check on an ARIEC61850 WPF single-file release ZIP.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PackagePath,
    [ValidateSet("SvPublisher", "IedDiscovery", "IedSimulator", "EngineeringWorkbench")]
    [string]$App = "SvPublisher",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

$ExeByApp = @{
    SvPublisher = "AR.Iec61850.SvPublisher.exe"
    IedDiscovery = "AR.Iec61850.IedDiscovery.exe"
    IedSimulator = "AR.Iec61850.IedSimulator.exe"
    EngineeringWorkbench = "AR.Iec61850.EngineeringWorkbench.exe"
}

$ResolvedPackage = (Resolve-Path $PackagePath).Path
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ariec61850-package-check-" + [System.Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
    Expand-Archive -Path $ResolvedPackage -DestinationPath $TempRoot -Force

    $Required = @(
        $ExeByApp[$App],
        "README-PORTABLE.txt",
        "LICENSE",
        "NOTICE",
        "COMMERCIAL-LICENSE.md",
        "COPYRIGHT.md",
        "TRADEMARK.md",
        "THIRD_PARTY_NOTICES.md",
        "README.md",
        "docs/LICENSING.md",
        "docs/QUICK_START.md",
        "docs/TROUBLESHOOTING.md",
        "docs/RELEASE_PACKAGING.md",
        "docs/CLEAN_ROOM_POLICY.md"
    )

    $Missing = @()
    foreach ($Item in $Required) {
        $Path = Join-Path $TempRoot $Item
        if (-not (Test-Path $Path)) {
            $Missing += $Item
        }
    }

    $ForbiddenDirectories = Get-ChildItem -Path $TempRoot -Recurse -Directory | Where-Object {
        $_.Name -in @("bin", "obj", ".vs", "out", "artifacts", ".artifacts", "evidence", "captures", "pcaps")
    }

    $HistoricalLicenseFiles = Get-ChildItem -Path $TempRoot -Recurse -File | Where-Object {
        $_.Name -ieq "LICENSE-APACHE-2.0"
    }

    if ($Missing.Count -gt 0) {
        throw ("Package is missing required files:`n" + ($Missing -join "`n"))
    }

    if ($ForbiddenDirectories.Count -gt 0) {
        throw ("Package contains forbidden generated folders:`n" + (($ForbiddenDirectories | ForEach-Object FullName) -join "`n"))
    }

    if ($HistoricalLicenseFiles.Count -gt 0) {
        throw ("Current package contains a historical alternative-license file and is therefore ambiguous:`n" + (($HistoricalLicenseFiles | ForEach-Object FullName) -join "`n"))
    }

    $LicenseText = Get-Content -LiteralPath (Join-Path $TempRoot "LICENSE") -Raw
    if ($LicenseText -notmatch "GNU GENERAL PUBLIC LICENSE" -or $LicenseText -notmatch "Version 3") {
        throw "Current package LICENSE is not the expected GNU GPL version 3 text."
    }

    Write-Host "Release package structure and GPL-only licensing are OK:" -ForegroundColor Green
    Write-Host "  $ResolvedPackage"
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item $TempRoot -Recurse -Force
    }
}
