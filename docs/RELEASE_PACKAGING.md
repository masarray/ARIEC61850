# Release Packaging

ARIEC61850 includes a Windows single-file packaging script for the WPF workspaces.

## Current release license

Current packages are licensed **only** under `GPL-3.0-or-later`. Each ZIP includes the current GPL license text, licensing explanation, commercial-license information, copyright notice, trademark policy, and third-party notices.

Historical alternative-license text is not included in current packages. Historical revisions remain available only on the dedicated archive branch described in [Licensing](LICENSING.md).

## Local packaging

```powershell
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App SvPublisher
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App IedDiscovery
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App IedSimulator
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App EngineeringWorkbench
```

Supported `-App` values:

| App | Published EXE | Purpose |
|---|---|---|
| `SvPublisher` | `AR.Iec61850.SvPublisher.exe` | Sampled Values publisher / injector workspace |
| `IedDiscovery` | `AR.Iec61850.IedDiscovery.exe` | Live MMS model, DataSet, RCB, and discovery evidence workspace |
| `IedSimulator` | `AR.Iec61850.IedSimulator.exe` | Offline simulator profile, values, DataSets, and RCB planning workspace |
| `EngineeringWorkbench` | `AR.Iec61850.EngineeringWorkbench.exe` | Read-only SCL, process-bus diagnostics, MMS loopback, and evidence workspace |

Output location example:

```text
.artifacts/release/
├─ ARIEC61850-IedDiscovery-v0.1.0-win-x64-single-exe.zip
├─ ARIEC61850-EngineeringWorkbench-v0.1.0-win-x64-single-exe.zip
├─ AR.Iec61850.IedDiscovery-v0.1.0-win-x64.exe
├─ AR.Iec61850.EngineeringWorkbench-v0.1.0-win-x64.exe
└─ SHA256SUMS.txt
```

`.artifacts/` is ignored by Git.

## Required package legal files

A current ZIP must include:

```text
LICENSE
NOTICE
COMMERCIAL-LICENSE.md
COPYRIGHT.md
TRADEMARK.md
THIRD_PARTY_NOTICES.md
docs/LICENSING.md
```

The release verifier rejects a package that contains the historical alternative-license file.

## GitHub Actions packaging

Use `.github/workflows/release-package.yml` manually from GitHub Actions or by pushing a tag:

```text
v0.1.0
```

The workflow:

1. checks out the repository;
2. installs .NET 8;
3. verifies source and current-license cleanliness;
4. restores, builds, and tests `ARIEC61850.sln`;
5. publishes the selected WPF app as a self-contained Windows x64 single EXE;
6. verifies the package structure and GPL-only license boundary;
7. uploads the EXE, ZIP, and SHA256 file as workflow artifacts;
8. optionally creates or updates a GitHub Release.

## Runtime note

The WPF app is published as a single EXE, but live raw Ethernet traffic still requires Npcap on the target Windows machine. Npcap is not bundled by this repository.
