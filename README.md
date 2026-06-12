# ARIEC61850 - Clean-Room IEC 61850 Native Stack for .NET

**ARIEC61850** is a clean-room IEC 61850 protocol stack and engineering toolkit
for .NET. It is being built as a reusable foundation for **MMS client/server
testing**, **GOOSE publisher/subscriber tools**, **Sampled Values publisher and
subscriber workflows**, **SCL-driven station validation**, and future
commissioning workbench applications.

[![.NET CI](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml)
[![Pages](https://github.com/masarray/ARIEC61850/actions/workflows/deploy-pages.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/deploy-pages.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](#build-from-source)
[![Protocol](https://img.shields.io/badge/protocol-IEC--61850-0f766e)](#what-this-stack-does)
[![Status](https://img.shields.io/badge/status-lab--validated--MVP-2563eb)](#validation-status)

[Website](https://masarray.github.io/ARIEC61850/) | [Quick Start](docs/QUICK_START.md) | [Architecture](docs/ARCHITECTURE.md) | [Validation](docs/VALIDATION.md) | [Roadmap](ROADMAP.md)

## What this stack does

ARIEC61850 provides original source code for building IEC 61850 engineering
software without depending on restrictive-license protocol implementations. The current stack
focuses on byte-accurate process-bus primitives, SCL import, deterministic test
fixtures, live Sampled Values and GOOSE publishing through a selected Ethernet
adapter, and native MMS discovery against lab IEDs.

Implemented now:

- **SCL import** for SCD, CID, ICD, and IID-style XML files.
- **Sampled Values frame builder and parser** with APPID, VLAN, SavPdu, ASDU,
  `smpCnt`, `confRev`, `smpSynch`, `smpRate`, `smpMod`, and raw payload.
- **GOOSE frame builder and parser** with APPID, VLAN, GOOSE APDU, `stNum`,
  `sqNum`, configuration revision, and typed dataset values.
- **MMS data value codec** for common data values used by GOOSE and future
  reporting.
- **ASN.1 BER reader/writer** for deterministic TLV handling.
- **SCL-backed publisher profiles** for GOOSE and Sampled Values.
- **In-memory transport** for repeatable tests.
- **PCAP writer, PCAP reader, and stream monitor** for offline validation.
- **Npcap raw Ethernet transport** for live SV publish smoke testing.
- **Live GOOSE publisher** with SCL-backed GSEControl selection, retransmission
  schedule, `stNum` and `sqNum` behavior, and optional state toggling.
- **Native MMS client foundation** with TCP/TPKT/COTP, ACSE/MMS association,
  `GetNameList` discovery, DataSet discovery, RCB inventory, bounded RCB
  attribute probing, and Confirmed-Read decode for common MMS data values.
- **CLI tester** for SCL inspection, PCAP generation, PCAP inspection, decoded
  stream playback, adapter discovery, live SV publishing, live GOOSE publishing,
  and MMS discovery.

Planned next:

- SCL-bound SV subscriber and GOOSE subscriber engines.
- Typed SV engineering-value payload packing.
- MMS report enable/disable and InformationReport receive path.
- MMS server and IED simulator.
- WPF station workbench built on top of the reusable stack.

## Why this repository exists

The goal is to build a reusable IEC 61850 engine that can power multiple future
products:

- MMS client tester.
- MMS server and IED simulator.
- GOOSE publisher and subscriber.
- Sampled Values publisher and subscriber.
- SCL-driven station validation tools.
- WPF and CLI tester applications for FAT, SAT, lab, and commissioning support.

The long-term product direction is a serious IEC 61850 station testing
workbench in the same problem class as professional tools such as StationScout,
IEDScout, and SVScout, while remaining an original clean-room implementation.

## Clean-room boundary

This repository contains original implementation work. External IEC 61850
projects and tools may be used only as behavioral references, documentation
pointers, or interoperability peers.

Rules:

- Do not copy or translate restrictive-license implementation code into this repository.
- Keep protocol logic in `src/`, not in tester UI projects.
- Keep transports replaceable.
- Keep every publisher testable through in-memory transport before live network
  output.
- Require explicit adapter selection and operator confirmation for active raw
  Ethernet publishing.

## Quick start

Requirements:

- .NET 8 SDK.
- Windows for live Npcap adapter publishing.
- Npcap installed when using `publish-sv-live`.
- An isolated Ethernet adapter, TAP, or lab switch for active traffic tests.

Inspect a sample SCL:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd
```

List Npcap adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Dry-run an SCL-backed SV publisher without sending to the NIC:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --dry-run
```

Publish one second of 9-2LE-style SV traffic to a selected isolated Ethernet
adapter:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --yes
```

Publish continuously until `Ctrl+C`:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --continuous --yes
```

Use the adapter index from `list-adapters`. Do not guess adapter indexes.

Publish a bounded GOOSE stream from SCL:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --duration-sec 5 --toggle-every-sec 2 --yes
```

Publish GOOSE continuously until `Ctrl+C`:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --continuous --toggle-every-sec 2 --yes
```

Discover an IEC 61850 MMS server or IED:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.16.1.157 --port 102 --timeout-ms 20000 --max-report-probes 16
```

Build the live IED directory and let the stack parse Functional Constraints from raw MMS names:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.16.1.157 --port 102 --timeout-ms 20000 --show-points --raw-limit 40
```

Search, resolve, or read a signal without typing `ST`, `MX`, `CO`, or another FC manually:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-find 192.16.1.157 MMXU --fc MX --raw-limit 40
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-resolve 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
```

Build a read-only report readiness plan before implementing any RCB write/enable workflow:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.16.1.157 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
```

## Offline PCAP workflow

Generate a PCAP containing SCL-backed SV and GOOSE frames:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap --sv-frames 32 --goose-frames 4
```

Inspect the PCAP with the stack:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap
```

Stream decoded events to the console:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\out\processbus-demo.pcap --delay-ms 50 --limit 20
```

## Build from source

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

## Repository layout

```text
ARIEC61850/
  src/
    AR.Iec61850/                     reusable clean-room stack
    AR.Iec61850.Transports.Npcap/    raw Ethernet adapter transport
  apps/
    AR.Iec61850.Cli/                 automation and lab smoke-test CLI
  tests/
    AR.Iec61850.Tests/               unit, round-trip, SCL, PCAP tests
  samples/
    scl/                             SCL fixtures for validation
  docs/
    validation/                      validation notes and lab evidence
    index.html                       GitHub Pages landing page
  ROADMAP.md                         disciplined implementation roadmap
  AGENTS.md                          engineering rules for future agents
```

## Validation status

Validated on 2026-06-12:

- `dotnet build ARIEC61850.slnx -c Release`
- `dotnet test ARIEC61850.slnx -c Release --no-build`
- 32 automated tests passed.
- SCL import resolved three SV streams from the 9-2LE sample file.
- Live SV publish through Npcap sent 20,000 frames over five seconds at roughly
  4,000 frames per second.
- Live GOOSE publish through Npcap sent a bounded stream with SCL `minTime` and
  `maxTime`, `sqNum` retransmission increments, and `stNum` reset behavior on
  simulated state changes.
- Native MMS discovery connected to lab IED `192.16.1.157:102`, completed
  ACSE/MMS association, found 4 logical devices, 10,122 raw variables, 1
  DataSet, and 286 report-control blocks.
- Generated payload followed 4I+4V DataSet order with 64 bytes per SV sample.

Important limitation: active SV publishing is a lab smoke path. It is
software-paced and should not be treated as protection-grade timing evidence.

See [Validation](docs/VALIDATION.md) and
[Live SV Publish Validation](docs/validation/live-sv-publish.md) or
[Live GOOSE Publish Validation](docs/validation/live-goose-publish.md), and
[Live MMS Discovery Validation](docs/validation/live-mms-discovery.md).

## Safety notes

Active publishing sends raw multicast Ethernet frames. Use it only on an
isolated lab NIC, TAP, or test switch. Do not publish on an office network,
production substation LAN, or network carrying real protection traffic.

The current stack is an engineering foundation and validation workbench. It is
not a certified IEC 61850 conformance test system.

## GitHub repository metadata

Recommended repository metadata for discoverability:

- **Description:** `Clean-room IEC 61850 native .NET stack for MMS, GOOSE, Sampled Values, SCL import, PCAP validation, and live SV publishing.`
- **Website:** `https://masarray.github.io/ARIEC61850/`
- **Topics:** `iec61850`, `iec-61850`, `mms`, `goose`, `sampled-values`,
  `sv-publisher`, `scl`, `substation-automation`, `scada`, `process-bus`,
  `protocol-analyzer`, `commissioning`, `fat-testing`, `sat-testing`,
  `dotnet`, `csharp`, `npcap`, `pcap`, `clean-room`, `substation`

## Documentation

- [Website](https://masarray.github.io/ARIEC61850/)
- [Quick Start](docs/QUICK_START.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Validation](docs/VALIDATION.md)
- [Professional Use](docs/PROFESSIONAL_USE.md)
- [FAQ](docs/FAQ.md)
- [Repository Setup](docs/REPOSITORY_SETUP.md)
- [Roadmap](ROADMAP.md)

## License

License has not been declared yet. Add a license before distributing this stack
as an open-source dependency.


### DataSet directory / report member map

Before enabling any report, build the DataSet member map from the live IED model:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-dataset-directory 192.16.1.157 OCR7SR12PROT/LLN0.DataSet --port 102 --timeout-ms 120000 --raw-limit 120
```

This command uses native MMS `GetNamedVariableListAttributes` (IEC 61850 DataSet directory) to resolve DataSet members back into user-friendly references with FC, for example `LD/LN.DO.da [ST|MX|CO]`. It is intentionally read-only and should be run before any future RCB enable/GI workflow.

### Static and dynamic reporting planners

The MMS client now includes read-only planners for report subscription workflows. These commands do not enable `RptEna`; they validate the RCB/DataSet/member mapping first.

```powershell
# Static report plan: select a safe RCB that already has a DataSet
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-static-plan 192.16.1.157 --port 102 --timeout-ms 120000 --read-values

# Dynamic report plan: resolve user points, select a free RCB slot, and propose a DataSet workflow
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-dynamic-plan 192.16.1.157 --port 102 --timeout-ms 120000 --points OCR7SR12PROT/A50PTOC1.Str,OCR7SR12PROT/A50PTOC1.Op --dataset-name AR_DYN_DS01
```

Static reporting is the first safe live-reporting target. Dynamic reporting is planned as: resolve points from the live IED directory, create an MMS NamedVariableList/DataSet, write `RCB.DatSet`, reserve the RCB, enable `RptEna`, trigger `GI`, receive unsolicited `InformationReport`, then clean up. Live write commands remain intentionally gated until the receive pump and report cleanup state machine are complete.
