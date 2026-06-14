# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Fails when prohibited public-release wording or source-tree payloads are present.

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

$ForbiddenTextPatterns = @(
    "ARIEC60870", "IEC60870", "IEC 60870", "IEC101", "IEC 101",
    "IEC103", "IEC 103", "IEC104", "IEC 104", "libiec61850",
    "MZ Automation", "GPL", "General Public License", "OCR7SR12", "OMICRON_CMC",
    "C:\Users\", "C:\Program Files\dotnet\sdk", "blocked in the current sandbox", "_wpftmp"
)

$Problems = New-Object System.Collections.Generic.List[string]

function Test-InRepoWorktree {
    param([Parameter(Mandatory=$true)][string]$Path)

    $FullPath = (Resolve-Path -LiteralPath $Path).Path
    if (-not $FullPath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $relative = $FullPath.Substring($RepoRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return -not ($relative -eq ".git" -or $relative.StartsWith(".git\", [System.StringComparison]::OrdinalIgnoreCase) -or $relative.StartsWith(".git/", [System.StringComparison]::OrdinalIgnoreCase))
}

function Test-IsIgnoredGeneratedPath {
    param([Parameter(Mandatory=$true)][string]$Path)

    $FullPath = (Resolve-Path -LiteralPath $Path).Path
    $relative = $FullPath.Substring($RepoRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ([string]::IsNullOrWhiteSpace($relative)) { return $false }

    $parts = $relative -split '[\\/]+'
    foreach ($part in $parts) {
        if ($IgnoredDirectoryNames -contains $part) { return $true }
    }
    return $false
}

foreach ($Pattern in $ForbiddenFilePatterns) {
    Get-ChildItem -Path $RepoRoot -Recurse -Force -File -Filter $Pattern -ErrorAction SilentlyContinue |
        Where-Object { (Test-InRepoWorktree $_.FullName) -and -not (Test-IsIgnoredGeneratedPath $_.FullName) } |
        ForEach-Object { $Problems.Add("Forbidden file: $($_.FullName)") }
}

$TextFiles = Get-ChildItem -Path $RepoRoot -Recurse -Force -File -Include *.md,*.cs,*.xml,*.xaml,*.ps1,*.cmd,*.yml,*.yaml,*.html,*.css,*.js,*.json,*.props,*.sln,*.slnx,*.txt -ErrorAction SilentlyContinue |
    Where-Object { (Test-InRepoWorktree $_.FullName) -and -not (Test-IsIgnoredGeneratedPath $_.FullName) }

foreach ($File in $TextFiles) {
    if ($File.FullName -like "*scripts\verify-source-clean.ps1") { continue }
    $Content = Get-Content -Path $File.FullName -Raw -ErrorAction SilentlyContinue
    foreach ($Pattern in $ForbiddenTextPatterns) {
        if ($Content -match [regex]::Escape($Pattern)) {
            $Problems.Add("Forbidden text '$Pattern': $($File.FullName)")
        }
    }
}

if ($Problems.Count -gt 0) {
    $Problems | ForEach-Object { Write-Error $_ }
    throw "Source tree is not public-release clean."
}

Write-Host "Source tree is public-release clean." -ForegroundColor Green
