# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Fails when tracked repository content contains prohibited public-release wording,
  external-product identifiers, confidential evidence, binary payloads, ambiguous
  current-license metadata, or known stale claim language.

.DESCRIPTION
  The gate scans every Git-tracked path. Disallowed external identifiers are
  represented only by one-way fingerprints so the repository does not publish or
  repeat unrelated product and company names.

  The current branch and current releases are GPL-3.0-or-later only. Historical
  license text belongs only on the dedicated archive branch.
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

$ForbiddenIdentifierHashes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    "dee5292b6aa3319833a7fb015d79494b0f1b69c3dc90258b39c042db388ccd71",
    "1343b354d479ded45dde0d7f4ea01daddf1d411a669724f9f3e3de78db038ffc",
    "439003d0d54d022f61a705da700bff916414fdf8308f0cae6a5b9e5903e86fdf",
    "bec65696741e77e0dd0de446b99fe3c069edb3a8f5c81a9939f9813b33e595ea",
    "4ed56753cb552f928aca8147069753f0f3741e28598c56533d8cddcd79fa574e",
    "bbfd365f0891c3e0205503f5d2a1678a0a6ea60d68f3dcc174ed4f60dd87e708",
    "d6a2feb71892b018d0ffec8d3cd438dabe599369d5a1921c7044137146107230",
    "048832a53880fe4fc5feeee9fa0ae445b143c99a956356bee231d3faadbb7af0",
    "0e443fe512c39ce723fc1be519b8e2a13a4ba75916989123078b59308480b2f8"
) | ForEach-Object { [void]$ForbiddenIdentifierHashes.Add($_) }

$CandidateLengths = [System.Collections.Generic.HashSet[int]]::new()
@(7, 8, 12, 22) | ForEach-Object { [void]$CandidateLengths.Add($_) }

$ForbiddenTextPatterns = @(
    "ARIEC60870", "IEC60870", "IEC 60870", "IEC101", "IEC 101",
    "IEC103", "IEC 103", "IEC104", "IEC 104",
    "C:\Users\", "C:\Program Files\dotnet\sdk", "blocked in the current sandbox", "_wpftmp"
)

$ForbiddenPublicWordingPatterns = @(
    "https://www.apache.org/licenses/LICENSE-2.0",
    "clean-room Apache-2",
    "External IP Cleanliness Audit",
    "field-grade findings",
    "safety arming",
    "confirmed safe",
    "Correct IEC 61850 sequencing",
    "public product capability descriptions",
    "behavioral references",
    "regulated engineering workbench",
    "192.16.1.157"
)

$TextExtensions = @(
    ".md", ".cs", ".xml", ".xaml", ".ps1", ".cmd", ".yml", ".yaml",
    ".html", ".css", ".js", ".json", ".props", ".targets", ".sln", ".slnx", ".txt"
)

$Problems = New-Object System.Collections.Generic.List[string]

function Normalize-RelativePath {
    param([Parameter(Mandatory=$true)][string]$Path)
    return $Path.Replace('\', '/').TrimStart('/')
}

function Get-Sha256Hex {
    param([Parameter(Mandatory=$true)][string]$Value)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        return -join ($algorithm.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $algorithm.Dispose()
    }
}

function Test-ContainsForbiddenIdentifier {
    param([AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    $words = @([regex]::Matches($Text.ToLowerInvariant(), '[a-z0-9]+') | ForEach-Object { $_.Value })

    for ($index = 0; $index -lt $words.Count; $index++) {
        $candidate = ""
        for ($count = 1; $count -le 4 -and ($index + $count - 1) -lt $words.Count; $count++) {
            $candidate += $words[$index + $count - 1]
            if ($candidate.Length -gt 22) { break }
            if ($CandidateLengths.Contains($candidate.Length) -and $ForbiddenIdentifierHashes.Contains((Get-Sha256Hex $candidate))) {
                return $true
            }
        }
    }

    return $false
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

    if ($relative -ieq "LICENSE-APACHE-2.0") {
        $Problems.Add("Historical license file must not be present on the current GPL-only branch: $relative")
    }

    foreach ($pattern in $ForbiddenFilePatterns) {
        if ($relative -like $pattern) {
            $Problems.Add("Forbidden tracked file: $relative")
            break
        }
    }

    if (Test-ContainsForbiddenIdentifier $relative) {
        $Problems.Add("Forbidden external identifier in path: $relative")
    }

    if ($relative -eq "scripts/verify-source-clean.ps1") { continue }
    if ($TextExtensions -notcontains [IO.Path]::GetExtension($relative).ToLowerInvariant()) { continue }

    $content = Get-Content -LiteralPath $fullPath -Raw -ErrorAction SilentlyContinue
    if (Test-ContainsForbiddenIdentifier $content) {
        $Problems.Add("Forbidden external identifier in text: $relative")
    }

    foreach ($pattern in $ForbiddenTextPatterns) {
        if ($content -match [regex]::Escape($pattern)) {
            $Problems.Add("Forbidden internal-release text '$pattern': $relative")
        }
    }

    foreach ($pattern in $ForbiddenPublicWordingPatterns) {
        if ($content -match [regex]::Escape($pattern)) {
            $Problems.Add("Stale or ambiguous public wording '$pattern': $relative")
        }
    }
}

if ($Problems.Count -gt 0) {
    foreach ($problem in ($Problems | Sort-Object -Unique)) {
        Write-Host "ERROR: $problem" -ForegroundColor Red
    }
    throw "Source tree is not public-release clean. Found $($Problems.Count) problem(s)."
}

Write-Host "All Git-tracked content passed source hygiene, provenance, wording, and current-license checks." -ForegroundColor Green
