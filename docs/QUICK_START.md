# Quick Start

## 1. Requirements

- .NET 8 SDK.
- Windows for WPF applications and the current raw-Ethernet transport.
- An isolated laboratory or approved commissioning network for active control or process-bus publishing.

## 2. Build and test

```powershell
git clone https://github.com/masarray/ARIEC61850.git
cd ARIEC61850

dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

## 3. Inspect synthetic SCL and PCAP data

Inspect the included synthetic SCL model:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
```

Generate and inspect a deterministic PCAP:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\.artifacts\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd
```

Generated output belongs under `.artifacts/` or another ignored local folder.

## 4. Discover a live MMS endpoint

Use a documentation-only address in examples and replace it with an approved laboratory endpoint:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.0.2.10 --port 102 --timeout-ms 30000
```

Build the live model directory:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.0.2.10 --port 102 --timeout-ms 30000 --show-points --raw-limit 40
```

Resolve and read a point without manually entering its Functional Constraint:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-resolve 192.0.2.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.0.2.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f
```

These discovery and read commands are read-only.

## 5. Plan and monitor reports

Plan report use before changing an RCB:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.0.2.10 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
```

Run a guarded report monitor only against an approved test endpoint:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.0.2.10 --port 102 --timeout-ms 120000 --rcb IED1LD0/LLN0.RP.rpt01 --duration-sec 60 --evidence .\.artifacts\out\report-session01 --yes
```

The monitor may write report-control attributes. Review the plan, target, reservation state, cleanup behavior, and evidence before execution.

## 6. Run Windows workspaces

IED discovery, reporting, and guarded control:

```powershell
dotnet run --project .\apps\AR.Iec61850.IedDiscovery -c Release
```

Read-only engineering analysis:

```powershell
dotnet run --project .\apps\AR.Iec61850.EngineeringWorkbench -c Release
```

Laboratory MMS simulator:

```powershell
dotnet run --project .\apps\AR.Iec61850.IedSimulator -c Release
```

Sampled Values laboratory publisher:

```powershell
dotnet run --project .\apps\AR.Iec61850.SvPublisher -c Release
```

## 7. Run the simulator and discover it

Start the CLI simulator on loopback:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -c Release -- simulate-ied --port 102 --duration-sec 600
```

From another elevated shell when required by Windows port policy:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -c Release -- mms-discover 127.0.0.1 --port 102 --timeout-ms 30000 --no-report-probe
```

The simulator currently supports deterministic laboratory discovery and read workflows. It is not presented as a production IED or formal conformance reference.

## 8. Process-bus dry runs

List adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Run bounded dry runs before any active publishing:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 1 --stream-index 1 --frames 4 --dry-run
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live .\samples\scl\minimal-station.scd --adapter 1 --stream-index 1 --frames 4000 --dry-run
```

Do not publish on an operational network without authority, an approved plan, isolation, and independent verification.

## 9. Evidence and source hygiene

- Use synthetic or contributor-owned samples.
- Do not commit customer SCL, live captures, station names, serial numbers, credentials, or internal paths.
- Store generated output under `.artifacts/`.
- Run `scripts/verify-source-clean.cmd` before every public push.

For current capability and claim boundaries, see [Engine Maturity Matrix](ENGINE_MATURITY_MATRIX.md). For future work, see [Roadmap](../ROADMAP.md).
