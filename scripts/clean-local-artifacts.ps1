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
$Names = @(".artifacts", "artifacts", "out", "evidence", "captures", "pcaps", "reports", "logs", ".vs", "bin", "obj", "TestResults")
$Extensions = @("*.dll", "*.exe", "*.pdb", "*.deps.json", "*.runtimeconfig.json", "*.pcap", "*.pcapng", "*.etl", "*.binlog", "*.log")

$Targets = @()
foreach ($Name in $Names) {
    $Targets += Get-ChildItem -Path $RepoRoot -Recurse -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq $Name }
}
foreach ($Pattern in $Extensions) {
    $Targets += Get-ChildItem -Path $RepoRoot -Recurse -Force -File -Filter $Pattern -ErrorAction SilentlyContinue
}

$Targets = $Targets | Sort-Object FullName -Unique
if ($Preview) {
    $Targets | ForEach-Object { Write-Host $_.FullName }
    Write-Host "Preview only. Re-run without -Preview to remove these files." -ForegroundColor Yellow
    return
}

foreach ($Target in $Targets) {
    if ($PSCmdlet.ShouldProcess($Target.FullName, "Remove")) {
        Remove-Item $Target.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Local artifacts removed." -ForegroundColor Green
