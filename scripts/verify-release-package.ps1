# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Performs a lightweight structural check on an ARIEC61850 WPF single-file release ZIP.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PackagePath,
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$ResolvedPackage = (Resolve-Path $PackagePath).Path
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ariec61850-package-check-" + [System.Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
    Expand-Archive -Path $ResolvedPackage -DestinationPath $TempRoot -Force

    $Required = @(
        "AR.Iec61850.SvPublisher.exe",
        "README-PORTABLE.txt",
        "LICENSE",
        "NOTICE",
        "THIRD_PARTY_NOTICES.md",
        "README.md",
        "docs/QUICK_START.md",
        "docs/TROUBLESHOOTING.md",
        "docs/RELEASE_PACKAGING.md"
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

    if ($Missing.Count -gt 0) {
        throw ("Package is missing required files:`n" + ($Missing -join "`n"))
    }

    if ($ForbiddenDirectories.Count -gt 0) {
        throw ("Package contains forbidden generated folders:`n" + (($ForbiddenDirectories | ForEach-Object FullName) -join "`n"))
    }

    Write-Host "Release package structure OK:" -ForegroundColor Green
    Write-Host "  $ResolvedPackage"
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item $TempRoot -Recurse -Force
    }
}
