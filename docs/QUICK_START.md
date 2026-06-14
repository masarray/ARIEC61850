# Quick Start

## 1. Install requirements

- .NET 8 SDK.
- Windows for the WPF app and Npcap-backed live Ethernet transport.
- Npcap when using live raw Ethernet process-bus publishing or capture.
- An isolated lab adapter, TAP, or test switch for active GOOSE/SV traffic.

## 2. Build and test

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
```

## 3. Run CLI examples

Inspect an SCL file:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
```

Generate a local PCAP and inspect it:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\.artifacts\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\.artifacts\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd --delay-ms 0 --limit 20
```

List available adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Run a read-only live GOOSE subscriber:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- goose-subscribe-live --adapter 1 --scl .\samples\scl\minimal-station.scd --duration-sec 30
```

Run an SV publish dry run:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 1 --stream-index 1 --frames 4000 --dry-run
```

Run a GOOSE publish dry run:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 1 --stream-index 1 --frames 4 --dry-run
```

## 4. Use MMS discovery and reporting commands

Discover a live IED or simulator:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.0.2.10 --port 102 --timeout-ms 30000 --max-report-probes 16
```

Build a model directory and show points:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.0.2.10 --port 102 --timeout-ms 30000 --show-points --raw-limit 40
```

Resolve and read a point without manually typing the Functional Constraint:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-resolve 192.0.2.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.0.2.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f
```

Plan report usage before enabling anything:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.0.2.10 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
```

Run a guarded report monitor and write evidence outside the repository:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.0.2.10 --port 102 --timeout-ms 120000 --rcb IED1LD0/LLN0.RP.rpt01 --duration-sec 60 --evidence .\.artifacts\out\report-session01 --yes
```


## 5. Run the WPF workspaces

SV publisher / injector workspace:

```powershell
dotnet run --project .\apps\AR.Iec61850.SvPublisher -c Release
```

Live IED discovery workspace:

```powershell
dotnet run --project .\apps\AR.Iec61850.IedDiscovery -c Release
```

Offline IED simulator workspace:

```powershell
dotnet run --project .\apps\AR.Iec61850.IedSimulator -c Release
```

The current simulator workspace is intentionally offline. It provides deterministic point values, DataSets, RCB profiles, and JSON export. A network MMS server is a later phase after the model/runtime core is stable.

## 6. Build a WPF app as a single EXE

```powershell
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App SvPublisher
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App IedDiscovery
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App IedSimulator
```

The output is created under `.artifacts/release`. The folder is ignored by Git and should not be committed.

## 7. Keep the source tree clean

Build output is centralized under `.artifacts/` and ignored by Git. To clean and verify the working tree before committing:

```powershell
.\scripts\clean-local-artifacts.cmd
.\scripts\verify-source-clean.cmd
```

## Generate an engineering profile from a live MMS endpoint

Use this read-only command when validating engine maturity against a real IED or simulator. It connects, discovers the model, reads available DataSet directories, classifies report readiness, and writes capability/diagnostic evidence.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-engine-profile 192.0.2.10 --port 102 --timeout-ms 30000 --output .\.artifacts\out\engineering-profile.md --json .\.artifacts\out\engineering-profile.json
```

The command performs no RCB writes. It is intended as the baseline model/capability test before report runtime, GOOSE diagnostics, SV analyzer, and simulator-server phases.

Generate a static report readiness profile before enabling any report:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-readiness-profile 192.0.2.10 --port 102 --timeout-ms 120000 --output .\.artifacts\out\report-readiness.md --json .\.artifacts\out\report-readiness.json --session-json .\.artifacts\out\report-session-profile.json
```

This command is also read-only. It produces acceptance gates, RCB candidate ranking, a selected static report plan, and a guarded report-session profile that future product apps can consume.


## N5.25 — SCL Deep Engineering Profile

This milestone adds an offline SCL engineering profile engine. It extracts access points, server/logical-device/logical-node structure, expected report sessions, expected GOOSE/SV streams, subscriber ExtRef mapping, service declarations, and static findings. The profile is available through `scl-engineering-profile` and is designed as the expected-model input for future report, GOOSE, SV, simulator, and evidence engines.

## N5.26 — Expected-vs-Observed Process-Bus Binding

Generate a deterministic process-bus PCAP from the sample SCL:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
```

Compare the SCL expected model against the observed PCAP:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- process-bus-binding-profile .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap --output .\.artifacts\out\process-bus-binding.md --json .\.artifacts\out\process-bus-binding.json
```

This command is read-only and is the first offline test path for expected-vs-observed GOOSE/SV diagnostics.
