# Validation

## Automated checks

Run these before every public push:

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

If you only need to validate the reusable stack while iterating on a desktop UI, run:

```powershell
dotnet build .\src\AR.Iec61850\AR.Iec61850.csproj -c Release
dotnet build .\src\AR.Iec61850.Simulation\AR.Iec61850.Simulation.csproj -c Release
dotnet build .\apps\AR.Iec61850.Cli\AR.Iec61850.Cli.csproj -c Release
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release
```

## Desktop app checks

```powershell
dotnet build .\apps\AR.Iec61850.SvPublisher\AR.Iec61850.SvPublisher.csproj -c Release
dotnet build .\apps\AR.Iec61850.IedDiscovery\AR.Iec61850.IedDiscovery.csproj -c Release
dotnet build .\apps\AR.Iec61850.IedSimulator\AR.Iec61850.IedSimulator.csproj -c Release
```

Recommended manual checks:

- open the SV Publisher and verify channel/ramp/state sequencing UI;
- open IED Discovery, connect to a lab IED or simulator, and export a JSON discovery snapshot;
- open IED Simulator, start/stop/step the offline runtime, and export a simulator profile JSON;
- confirm all generated output stays under `.artifacts` or another ignored local folder.

## Manual lab checks

Recommended protocol checks before a release:

- inspect each sample SCL file;
- generate and inspect a local PCAP;
- stream a local PCAP with SCL binding and verify GOOSE/SV sequence diagnostics;
- run SV publish dry-run mode;
- run GOOSE publish dry-run mode;
- list Npcap adapters on a Windows lab PC;
- run read-only live GOOSE subscribe on a mirrored or isolated lab adapter;
- publish bounded SV traffic on an isolated lab adapter;
- publish bounded GOOSE traffic on an isolated lab adapter;
- run MMS discovery against a simulator or lab IED;
- run report planning before enabling any RCB;
- export report evidence into ignored local output folders only.

## Current claim boundary

This project is suitable for lab validation, engineering development, education, and repeatable evidence generation. Do not describe any release as formally conformance certified unless a recognized test lab has produced formal evidence for that exact release.
