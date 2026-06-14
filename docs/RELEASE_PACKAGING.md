# Release Packaging

ARIEC61850 includes a Windows single-file packaging script for the WPF workspaces.

## Local packaging

```powershell
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App SvPublisher
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App IedDiscovery
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App IedSimulator
```

Supported `-App` values:

| App | Published EXE | Purpose |
|---|---|---|
| `SvPublisher` | `AR.Iec61850.SvPublisher.exe` | Sampled Values publisher / injector workspace |
| `IedDiscovery` | `AR.Iec61850.IedDiscovery.exe` | Live MMS model, DataSet, RCB, and discovery evidence workspace |
| `IedSimulator` | `AR.Iec61850.IedSimulator.exe` | Offline simulator profile, values, DataSets, and RCB planning workspace |

Output location example:

```text
.artifacts/release/
├─ ARIEC61850-IedDiscovery-v0.1.0-win-x64-single-exe.zip
├─ AR.Iec61850.IedDiscovery-v0.1.0-win-x64.exe
└─ SHA256SUMS.txt
```

`.artifacts/` is ignored by Git.

## GitHub Actions packaging

Use `.github/workflows/release-package.yml` manually from GitHub Actions or by pushing a tag:

```text
v0.1.0
```

The workflow:

1. checks out the repository;
2. installs .NET 8;
3. restores, builds, and tests `ARIEC61850.sln`;
4. publishes the selected WPF app as a self-contained Windows x64 single EXE;
5. verifies the package structure;
6. uploads the EXE, ZIP, and SHA256 file as workflow artifacts;
7. optionally creates or updates a GitHub Release.

## Runtime note

The WPF app is published as a single EXE, but live raw Ethernet traffic still requires Npcap on the target Windows machine. Npcap is not bundled by this repository.
