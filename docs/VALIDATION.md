# Validation

## Automated checks

Run these before every public push:

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

If the local SDK cannot evaluate the WPF project, validate the reusable stack and CLI directly:

```powershell
dotnet build .\apps\AR.Iec61850.Cli\AR.Iec61850.Cli.csproj -c Release --no-restore --no-incremental
dotnet build .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-restore --no-incremental
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-build
```

## Manual lab checks

Recommended manual checks before a release:

- inspect each sample SCL file;
- generate and inspect a local PCAP;
- stream a local PCAP with SCL binding and verify GOOSE/SV sequence diagnostics;
- run SV publish dry-run mode;
- run GOOSE publish dry-run mode;
- list Npcap adapters on a Windows lab PC;
- publish bounded SV traffic on an isolated lab adapter;
- publish bounded GOOSE traffic on an isolated lab adapter;
- run MMS discovery against a simulator or lab IED;
- run report planning before enabling any RCB;
- export report evidence into ignored local output folders only.

## Latest local evidence

### N5.19 GOOSE engine smoke

Validated on 2026-06-14:

```powershell
dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' build .\apps\AR.Iec61850.Cli\AR.Iec61850.Cli.csproj -c Release --no-restore --no-incremental
dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' build .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-restore --no-incremental
dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-build
dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll generate-pcap .\samples\scl\minimal-station.scd .\out\n5-19-goose-demo.pcap
dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll inspect-pcap .\out\n5-19-goose-demo.pcap --scl .\samples\scl\minimal-station.scd
dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll stream-pcap .\out\n5-19-goose-demo.pcap --scl .\samples\scl\minimal-station.scd --delay-ms 0 --limit 20
```

Observed:

- `dotnet test`: 142 passed, 0 failed.
- PCAP generated: 20 Ethernet frames, including 4 GOOSE frames.
- `inspect-pcap`: GOOSE `TAL=1000ms`, `stateChanges=1`, `retrans=2`, `timeouts=0`.
- `stream-pcap`: retransmission frames showed `changed=0`; final state-change frame showed `seq=StateChange` and changed Boolean/timestamp values.

Solution-wide build note: in this local environment, the reusable stack, Npcap transport, tests, and CLI build, but solution-wide WPF evaluation fails at the generated `*_wpftmp` project because the temporary WPF assets file under `.artifacts\obj` is not generated. Track this separately from GOOSE stack validation.

## Current claim boundary

This project is suitable for lab validation and engineering development. It should not be described as formally conformance certified unless a recognized test lab has produced formal evidence for the exact release.
