# Quick Start

## 1. Install requirements

- .NET 8 SDK.
- Windows for the WPF app and Npcap-backed live Ethernet transport.
- Npcap when using live raw Ethernet process-bus publishing.
- An isolated lab adapter, TAP, or test switch for active GOOSE/SV traffic.

## 2. Build and test

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

## 3. Run CLI examples

Inspect an SCL file:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
```

Generate a local PCAP and inspect it:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd
```

List available adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Run an SV publish dry run:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 1 --stream-index 1 --frames 4000 --dry-run
```

## 4. Use MMS discovery and reporting commands

Discover a live IED or simulator:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.168.1.10 --port 102 --timeout-ms 30000 --max-report-probes 16
```

Build a model directory and show points:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.168.1.10 --port 102 --timeout-ms 30000 --show-points --raw-limit 40
```

Resolve and read a point without manually typing the Functional Constraint:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-resolve 192.168.1.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.168.1.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f
```

Plan report usage before enabling anything:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.168.1.10 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
```

Run a guarded report monitor and write evidence outside the repository:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.168.1.10 --port 102 --timeout-ms 120000 --rcb IED1LD0/LLN0.RP.rpt01 --duration-sec 60 --evidence .\out\report-session01 --yes
```

## 5. Build the WPF publisher as a single EXE

```powershell
pwsh .\scripts\publish-windows-singlefile.ps1 -Version 0.1.0
```

The output is created under `artifacts/release`. The folder is ignored by Git and should not be committed.
