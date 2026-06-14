# ARIEC61850 Roadmap

## Current source milestone

### N5.21 - Suite foundation for native IEC 61850 lab tooling

- Baseline SDK aligned to .NET 8.0.422 for local builds and GitHub Actions.
- Added `apps/AR.Iec61850.IedDiscovery`, a WPF workspace for live MMS model, DataSet, RCB, discovery JSON export, and first safe static report profile export.
- Added `src/AR.Iec61850.Simulation`, an offline simulator foundation for deterministic point values, DataSets, RCB profiles, and event snapshots.
- Added `apps/AR.Iec61850.IedSimulator`, a WPF offline simulator workspace for model/runtime UX before network-server work.
- Added simulator runtime tests.
- Updated public docs to use `.sln` as the primary build path and to describe the repository as a suite, not a single SV app.

Validation to run on a Windows dev machine with .NET 8 SDK:

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

## Near term

- Promote MMS report setup into the IED Discovery workflow: connect, discover, select DataSet/RCB, validate readiness, save profile, then monitor.
- Add runtime reporting workspace with active RCB, DataSet members, GI indicator, report timeline, sequence diagnostics, and evidence export.
- Expand IED Simulator from offline value engine into a read-only MMS model server after the model/runtime contract is stable.
- Improve WPF SV Publisher usability and release polish.
- Validate live GOOSE subscriber over Npcap receive, then add SV subscriber loop over the same abstraction.
- Add live GoCB discovery/readback over MMS: `GoEna`, `GoID`, `DatSet`, `ConfRev`, `NdsCom`, `MinTime`, `MaxTime`, and destination address.

## Mid term

- Improve multi-vendor SCL and reporting compatibility evidence.
- Add MMS file, log, setting-group, and selected control-model services.
- Add simulator profile import/export from SCL.
- Add guided training profiles for common SAS/FAT scenarios.

## Long term

- Prepare formal validation evidence for selected protocol areas.
- Add security-profile work where practical and safe.
- Publish stable release notes for each tagged public release.
