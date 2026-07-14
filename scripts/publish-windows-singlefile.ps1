# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Builds a Windows x64 single-file portable release for an ARIEC61850 WPF app.

.DESCRIPTION
  Restores, builds, tests, publishes the selected WPF app as a self-contained
  single EXE, creates a ZIP package, and writes SHA256 checksums. Generated
  output is written to .artifacts/release and must not be committed.

.EXAMPLE
  powershell.exe -ExecutionPolicy Bypass -File .\scripts\publish-windows-singlefile.ps1 -Version 0.1.0 -App SvPublisher
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [ValidateSet("SvPublisher", "IedDiscovery", "IedSimulator", "EngineeringWorkbench")]
    [string]$App = "SvPublisher",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$Apps = @{
    SvPublisher = @{
        Project = "apps\AR.Iec61850.SvPublisher\AR.Iec61850.SvPublisher.csproj"
        Exe = "AR.Iec61850.SvPublisher.exe"
        PackageName = "ARIEC61850-SvPublisher"
        DisplayName = "ARIEC61850 SV Publisher"
        Note = "IEC 61850 Sampled Values lab publishing / injection workspace."
    }
    IedDiscovery = @{
        Project = "apps\AR.Iec61850.IedDiscovery\AR.Iec61850.IedDiscovery.csproj"
        Exe = "AR.Iec61850.IedDiscovery.exe"
        PackageName = "ARIEC61850-IedDiscovery"
        DisplayName = "ARIEC61850 IED Discovery"
        Note = "Live MMS model, DataSet, RCB, and discovery evidence workspace."
    }
    IedSimulator = @{
        Project = "apps\AR.Iec61850.IedSimulator\AR.Iec61850.IedSimulator.csproj"
        Exe = "AR.Iec61850.IedSimulator.exe"
        PackageName = "ARIEC61850-IedSimulator"
        DisplayName = "ARIEC61850 IED Simulator"
        Note = "Offline IED profile, point runtime, DataSet, and RCB planning workspace."
    }
    EngineeringWorkbench = @{
        Project = "apps\AR.Iec61850.EngineeringWorkbench\AR.Iec61850.EngineeringWorkbench.csproj"
        Exe = "AR.Iec61850.EngineeringWorkbench.exe"
        PackageName = "ARIEC61850-EngineeringWorkbench"
        DisplayName = "ARIEC61850 Engineering Workbench"
        Note = "Read-only SCL, process-bus diagnostics, MMS loopback, and evidence workspace."
    }
}

$Selected = $Apps[$App]
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ArtifactRoot = Join-Path $RepoRoot ".artifacts"
$ReleaseRoot = Join-Path $ArtifactRoot "release"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$PackageBase = $Selected.PackageName
$PackageRoot = Join-Path $ReleaseRoot "$PackageBase-v$Version-$Runtime-single-exe"
$ExeName = $Selected.Exe
$RenamedExe = ($ExeName -replace "\.exe$", "") + "-v$Version-$Runtime.exe"
$ZipPath = Join-Path $ReleaseRoot "$PackageBase-v$Version-$Runtime-single-exe.zip"
$DirectExePath = Join-Path $ReleaseRoot $RenamedExe
$ChecksumFile = Join-Path $ReleaseRoot "SHA256SUMS.txt"
$SelfContained = if ($FrameworkDependent) { "false" } else { "true" }

Write-Host "ARIEC61850 WPF single-file packaging" -ForegroundColor Cyan
Write-Host "Repository     : $RepoRoot"
Write-Host "App            : $App"
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

    dotnet restore .\ARIEC61850.sln
    dotnet build .\ARIEC61850.sln -c $Configuration --no-restore

    if (-not $SkipTests) {
        dotnet test .\ARIEC61850.sln -c $Configuration --no-build --verbosity normal
    }

    dotnet publish $Selected.Project `
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
$($Selected.DisplayName) v$Version
Windows x64 single-file portable package

Run:
  $ExeName

Purpose:
  $($Selected.Note)

Notes:
  - The app is self-contained and does not require a separate .NET runtime.
  - Npcap is required only for workflows that use live raw Ethernet traffic.
  - Use active publishing only on isolated lab networks or approved test systems.
  - Do not commit runtime evidence, captures, or generated release artifacts.
"@
    Set-Content -Path (Join-Path $PackageRoot "README-PORTABLE.txt") -Value $PortableReadme -Encoding UTF8

    # ARIEC_LEGAL_FILES: include licensing and attribution documents in distributed packages.
$legalFiles = @("LICENSE", "LICENSE-APACHE-2.0", "COMMERCIAL-LICENSE.md", "TRADEMARK.md", "COPYRIGHT.md", "THIRD_PARTY_NOTICES.md", "NOTICE")
foreach ($legalFile in $legalFiles) {
    $sourceLegalFile = Join-Path $root $legalFile
    if (Test-Path $sourceLegalFile) {
        Copy-Item $sourceLegalFile (Join-Path $publishDir $legalFile) -Force
    }
}

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
