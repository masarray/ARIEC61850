# ARIEC61850

Clean-room IEC 61850 toolkit for .NET 8: MMS, reporting, GOOSE, Sampled Values, SCL, PCAP, diagnostics, and lab-grade engineering workflows.

[![.NET CI](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml)
[![Pages](https://github.com/masarray/ARIEC61850/actions/workflows/pages.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/pages.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](#build)

**ARIEC61850** is a native C#/.NET engineering stack for IEC 61850 lab tools, protocol education, FAT/SAT assistance, process-bus diagnostics, and repeatable evidence generation.

This repository is intentionally source-first and public-release safe: generated build output, evidence folders, local Visual Studio state, captures, and release artifacts are excluded from source control.

## What is included

| Area | Project / folder | Purpose |
|---|---|---|
| Core library | `src/AR.Iec61850` | BER, MMS, SCL, GOOSE, SV, PCAP, reporting, diagnostics |
| Live Ethernet transport | `src/AR.Iec61850.Transports.Npcap` | Npcap-backed raw process-bus transport for Windows lab use |
| CLI toolkit | `apps/AR.Iec61850.Cli` | SCL inspection, PCAP generation/inspection, MMS discovery/read/reporting commands |
| WPF app | `apps/AR.Iec61850.SvPublisher` | Desktop Sampled Values publisher workspace |
| Tests | `tests/AR.Iec61850.Tests` | Automated unit and protocol-shape tests |
| Samples | `samples/scl` | Small SCL files for local validation and examples |
| Docs | `docs` | Quick start, architecture, reporting workflow, validation, release packaging |
| Landing page | `landing` | Static GitHub Pages website |

## Status

This is a lab-oriented engineering toolkit, not a formal conformance-certified IEC 61850 product.

Implemented areas include:

- ASN.1 BER reader/writer.
- MMS data value codec and common client services.
- TCP/TPKT/COTP/ACSE/MMS association foundation.
- MMS model discovery, FC-aware path resolution, smart read, dataset directory inspection.
- RCB discovery, report planning, guarded report enable, GI trigger, receive loop, diagnostics, and evidence export.
- GOOSE frame builder/parser and SCL-backed publishing helpers.
- Sampled Values frame builder/parser, payload generation, payload decode, and WPF publisher workspace.
- PCAP writer/reader/inspector and stream playback helpers.
- Npcap-backed raw Ethernet transport for isolated Windows lab adapters.

Experimental or future areas:

- multi-vendor long-duration MMS reporting soak evidence;
- full buffered report recovery and replay workflows;
- MMS file/log/setting-group/control model services;
- MMS server / IED simulator;
- live raw GOOSE/SV subscriber loops;
- IEC 62351 security profile;
- formal third-party conformance testing.

## Requirements

- .NET 8 SDK for build/test.
- Windows for the current WPF desktop app and Npcap live transport.
- Npcap when sending or receiving raw Ethernet process-bus traffic.
- Isolated test NIC, TAP, or lab switch for active GOOSE/SV publishing.
- Test IED or simulator for live MMS commands.

## Build

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

Build the WPF publisher directly:

```powershell
dotnet build .\apps\AR.Iec61850.SvPublisher\AR.Iec61850.SvPublisher.csproj -c Release
```

## Quick examples

Inspect an SCL file:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
```

Generate and inspect a demo PCAP:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd
```

Discover a live MMS server or IED:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.168.1.10 --port 102 --timeout-ms 30000 --max-report-probes 16
```

Plan report usage before writing to any RCB:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.168.1.10 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
```

Run a guarded report monitor:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.168.1.10 --port 102 --timeout-ms 120000 --rcb IED1LD0/LLN0.RP.rpt01 --duration-sec 60 --evidence .\out\report-session01 --yes
```

## Windows single-file WPF package

Local packaging:

```powershell
pwsh .\scripts\publish-windows-singlefile.ps1 -Version 0.1.0
```

The script builds and tests the solution, publishes `AR.Iec61850.SvPublisher` as a self-contained Windows x64 single EXE, and creates release assets under `artifacts/release`.

The same packaging flow is available in GitHub Actions through `.github/workflows/release-package.yml`.

## Documentation

- [Quick Start](docs/QUICK_START.md)
- [Architecture](docs/ARCHITECTURE.md)
- [MMS Reporting Workflow](docs/REPORTING_WORKFLOW.md)
- [Release Packaging](docs/RELEASE_PACKAGING.md)
- [Validation](docs/VALIDATION.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Clean-room Policy](docs/CLEAN_ROOM_POLICY.md)
- [Public Release Checklist](docs/PUBLIC_RELEASE_CHECKLIST.md)

## License

Licensed under the [Apache License 2.0](LICENSE).

## Safety note

Active MMS writes, report enable operations, GOOSE publishing, and Sampled Values publishing must be used only in isolated labs or approved test networks. Do not publish process-bus traffic into an operational substation network without an approved test plan and isolation boundary.
