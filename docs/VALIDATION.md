# Validation

## Automated checks

Run these before every public push:

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

## Manual lab checks

Recommended manual checks before a release:

- inspect each sample SCL file;
- generate and inspect a local PCAP;
- run SV publish dry-run mode;
- list Npcap adapters on a Windows lab PC;
- publish bounded SV traffic on an isolated lab adapter;
- run MMS discovery against a simulator or lab IED;
- run report planning before enabling any RCB;
- export report evidence into ignored local output folders only.

## Current claim boundary

This project is suitable for lab validation and engineering development. It should not be described as formally conformance certified unless a recognized test lab has produced formal evidence for the exact release.
