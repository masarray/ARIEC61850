# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Removes local build, publish, evidence, and IDE artifacts from the working tree.
#>
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [switch]$Preview
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Names = @(".artifacts", "artifacts", "out", "evidence", "captures", "pcaps", "reports", "logs", ".vs", ".idea", ".dotnet_home", "bin", "obj", "TestResults", "coverage", "publish", "release")
$Extensions = @("*.dll", "*.exe", "*.pdb", "*.deps.json", "*.runtimeconfig.json", "*.nupkg", "*.snupkg", "*.pcap", "*.pcapng", "*.etl", "*.binlog", "*.log", "*.tmp", "*.cache")

function Test-InRepoWorktree {
    param([Parameter(Mandatory=$true)][string]$Path)

    $FullPath = (Resolve-Path -LiteralPath $Path).Path
    if (-not $FullPath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $relative = $FullPath.Substring($RepoRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return -not ($relative -eq ".git" -or $relative.StartsWith(".git\", [System.StringComparison]::OrdinalIgnoreCase) -or $relative.StartsWith(".git/", [System.StringComparison]::OrdinalIgnoreCase))
}

$Targets = @()
foreach ($Name in $Names) {
    $Targets += Get-ChildItem -Path $RepoRoot -Recurse -Force -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $Name -and (Test-InRepoWorktree $_.FullName) }
}
foreach ($Pattern in $Extensions) {
    $Targets += Get-ChildItem -Path $RepoRoot -Recurse -Force -File -Filter $Pattern -ErrorAction SilentlyContinue |
        Where-Object { Test-InRepoWorktree $_.FullName }
}

$Targets = $Targets | Sort-Object FullName -Unique
if ($Preview) {
    $Targets | ForEach-Object { Write-Host $_.FullName }
    Write-Host "Preview only. Re-run without -Preview to remove these files." -ForegroundColor Yellow
    return
}

foreach ($Target in $Targets) {
    if ($PSCmdlet.ShouldProcess($Target.FullName, "Remove")) {
        Remove-Item -LiteralPath $Target.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Local artifacts removed." -ForegroundColor Green
