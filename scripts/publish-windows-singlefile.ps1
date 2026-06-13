# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Builds a Windows x64 single-file portable release for the ARIEC61850 WPF SV Publisher.

.DESCRIPTION
  The script restores, builds, tests, publishes apps/AR.Iec61850.SvPublisher as
  a self-contained single EXE, creates a small ZIP package, and writes SHA256
  checksums. Generated output is written to artifacts/release and must not be
  committed.

.EXAMPLE
  pwsh ./scripts/publish-windows-singlefile.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ArtifactRoot = Join-Path $RepoRoot "artifacts"
$ReleaseRoot = Join-Path $ArtifactRoot "release"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$PackageRoot = Join-Path $ReleaseRoot "ARIEC61850-SvPublisher-v$Version-$Runtime-single-exe"
$ExeName = "AR.Iec61850.SvPublisher.exe"
$RenamedExe = "AR.Iec61850.SvPublisher-v$Version-$Runtime.exe"
$ZipPath = Join-Path $ReleaseRoot "ARIEC61850-SvPublisher-v$Version-$Runtime-single-exe.zip"
$DirectExePath = Join-Path $ReleaseRoot $RenamedExe
$ChecksumFile = Join-Path $ReleaseRoot "SHA256SUMS.txt"
$SelfContained = if ($FrameworkDependent) { "false" } else { "true" }

Write-Host "ARIEC61850 WPF single-file packaging" -ForegroundColor Cyan
Write-Host "Repository     : $RepoRoot"
Write-Host "Version        : $Version"
Write-Host "Runtime        : $Runtime"
Write-Host "Configuration  : $Configuration"
Write-Host "Self-contained : $SelfContained"

Push-Location $RepoRoot
try {
    if (Test-Path $ReleaseRoot) {
        Remove-Item $ReleaseRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $PublishRoot, $PackageRoot | Out-Null

    dotnet restore .\ARIEC61850.slnx
    dotnet build .\ARIEC61850.slnx -c $Configuration --no-restore

    if (-not $SkipTests) {
        dotnet test .\ARIEC61850.slnx -c $Configuration --no-build --verbosity normal
    }

    dotnet publish .\apps\AR.Iec61850.SvPublisher\AR.Iec61850.SvPublisher.csproj `
        -c $Configuration `
        -r $Runtime `
        --self-contained:$SelfContained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishReadyToRun=true `
        -o $PublishRoot

    $PublishedExe = Join-Path $PublishRoot $ExeName
    if (-not (Test-Path $PublishedExe)) {
        throw "Published EXE was not found: $PublishedExe"
    }

    Copy-Item $PublishedExe $DirectExePath -Force
    Copy-Item $PublishedExe (Join-Path $PackageRoot $ExeName) -Force

    Copy-Item LICENSE, NOTICE, THIRD_PARTY_NOTICES.md, README.md -Destination $PackageRoot -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $PackageRoot "docs") | Out-Null
    Copy-Item docs\QUICK_START.md, docs\TROUBLESHOOTING.md, docs\RELEASE_PACKAGING.md, docs\CLEAN_ROOM_POLICY.md -Destination (Join-Path $PackageRoot "docs") -Force

    $PortableReadme = @"
ARIEC61850 SV Publisher v$Version
Windows x64 single-file portable package

Run:
  $ExeName

Notes:
  - The app is self-contained and does not require a separate .NET runtime.
  - Npcap is still required on the Windows machine for live raw Ethernet traffic.
  - Use only on isolated lab networks or approved test systems.
  - Do not commit runtime evidence, captures, or generated release artifacts.
"@
    Set-Content -Path (Join-Path $PackageRoot "README-PORTABLE.txt") -Value $PortableReadme -Encoding UTF8

    Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $ZipPath -CompressionLevel Optimal

    $HashLines = @()
    foreach ($Asset in @($DirectExePath, $ZipPath)) {
        $Hash = Get-FileHash -Algorithm SHA256 $Asset
        $HashLines += ("{0}  {1}" -f $Hash.Hash.ToLowerInvariant(), (Split-Path $Asset -Leaf))
    }
    $HashLines | Set-Content -Path $ChecksumFile -Encoding ASCII

    Write-Host "Release assets created:" -ForegroundColor Green
    Write-Host "  $DirectExePath"
    Write-Host "  $ZipPath"
    Write-Host "  $ChecksumFile"
}
finally {
    Pop-Location
}
