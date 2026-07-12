# ARIEC61850

**Native IEC 61850 client, smart IED control, reporting, GOOSE, Sampled Values, SCL, and PCAP engineering toolkit for .NET 8.**

[![.NET CI](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml)
[![GitHub Pages](https://github.com/masarray/ARIEC61850/actions/workflows/pages.yml/badge.svg)](https://masarray.github.io/ARIEC61850/)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/core-cross--platform-0f766e)](#platform-support)

ARIEC61850 is a clean-room C# implementation for substation automation laboratories, IEC 61850 commissioning support, FAT/SAT preparation, protocol research, and repeatable engineering evidence. The repository combines a reusable protocol stack with focused CLI and Windows desktop tools.

> **Current validation boundary:** the Smart Control Tester has completed a live command path to a laboratory IED. This is engineering evidence, not an IEC 61850 conformance certification or permission to operate primary equipment.

[Website](https://masarray.github.io/ARIEC61850/) · [Quick start](docs/QUICK_START.md) · [Smart Control](docs/SMART_CONTROL_STACK.md) · [Control Tester](docs/IED_DISCOVERY_SMART_CONTROL_TESTER.md) · [Roadmap](docs/FULL_STACK_ROADMAP.md)

## Engineering highlights

- **Smart IEC 61850 control:** select a control Data Object such as `CSWI.Pos`; the engine discovers `ctlModel`, exact live MMS types, and automatically executes Direct Operate or Select-Before-Operate.
- **Operator-friendly OPEN/CLOSE workflow:** guarded WPF control tester with live status, interlock check, synchrocheck, test mode, originator, Orig ID, timeout, cancellation, and evidence.
- **MMS client and reporting:** association, model discovery, FC-aware reads, DataSet/RCB inventory, guarded report planning, GI, monitoring, and diagnostics.
- **GOOSE and Sampled Values:** frame codecs, SCL-backed profiles, PCAP inspection, publishing, subscription, sequence supervision, and diagnostics.
- **SCL and engineering evidence:** expected-vs-observed analysis, capability/readiness profiles, Markdown/JSON evidence, and deterministic simulation foundations.
- **Clean public source:** Apache-2.0 license, warnings-as-errors, automated tests, source hygiene scripts, CI, and GitHub Pages.

## Smart IED Control Tester

The IED Discovery desktop app keeps the normal operator workflow intentionally simple:

```text
Select CSWI.Pos → open Control Tester → verify status → press OPEN or CLOSE
```

The application automatically handles the protocol sequence:

| Discovered control model | Native sequence | Completion boundary |
|---|---|---|
| Direct, normal security | `Oper` | Confirmed MMS result |
| SBO, normal security | `SBO` → `Oper` | Confirmed MMS result |
| Direct, enhanced security | `Oper` → CommandTermination | Positive/negative termination |
| SBO, enhanced security | `SBOw` → `Oper` → CommandTermination | Positive/negative termination |

The native control stack also manages:

- exact live `ctlVal` binding for DPC, SPC, INC/ISC, BSC, and APC variants;
- immutable `ctlNum`, timestamp `T`, origin, Test, and Check values across a sequence;
- interlock and synchrocheck request bits;
- SBO ownership, expiry, best-effort `Cancel`, and association-loss cleanup;
- `LastApplError`, `ControlError`, `AddCause`, and request/response evidence;
- process feedback readback through the discovered status reference.

See [IEC 61850 Smart Control Stack](docs/SMART_CONTROL_STACK.md), [IED Discovery Smart Control Tester](docs/IED_DISCOVERY_SMART_CONTROL_TESTER.md), and [Live IED control validation](docs/LIVE_IED_CONTROL_VALIDATION.md).

## Applications and libraries

| Component | Path | Role |
|---|---|---|
| Core protocol library | `src/AR.Iec61850` | BER, MMS, reporting, native control, SCL, GOOSE, SV, PCAP, diagnostics |
| Npcap transport | `src/AR.Iec61850.Transports.Npcap` | Raw Ethernet process-bus transport for Windows labs |
| Simulation library | `src/AR.Iec61850.Simulation` | Deterministic IED profiles, points, and events |
| IED Discovery | `apps/AR.Iec61850.IedDiscovery` | Live model browser, reporting workspace, and Smart Control Tester |
| Engineering Workbench | `apps/AR.Iec61850.EngineeringWorkbench` | SCL/PCAP diagnostics and evidence-pack workflow |
| IED Simulator | `apps/AR.Iec61850.IedSimulator` | SCL-backed deterministic laboratory server foundation |
| SV Publisher | `apps/AR.Iec61850.SvPublisher` | Sampled Values injection and waveform workspace |
| CLI | `apps/AR.Iec61850.Cli` | Automation, discovery, diagnostics, simulation, and PCAP commands |
| Automated tests | `tests/AR.Iec61850.Tests` | Protocol-shape, state-machine, binding, diagnostics, and regression tests |

## Quick start

### Requirements

- .NET 8 SDK.
- Windows for WPF applications and the current Npcap live transport.
- Npcap for raw GOOSE/SV traffic.
- An isolated laboratory network for active publishing or control.

### Build and test

```powershell
git clone https://github.com/masarray/ARIEC61850.git
cd ARIEC61850

dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
```

### Run IED Discovery and Smart Control Tester

```powershell
dotnet run `
  --project .\apps\AR.Iec61850.IedDiscovery\AR.Iec61850.IedDiscovery.csproj `
  -c Release
```

Live discovery is read-only. It builds the model from MMS domain and named-variable directories, preserves DataSet member order, inventories RCBs, queries `GetVariableAccessAttributes` from each logical-node root to recover the FC/DO/DA type hierarchy, and can list the MMS file directory. File service unavailability is reported as evidence and does not discard an otherwise valid model snapshot.

1. Connect to the test IED.
2. Select a controllable Data Object such as `LD0/CSWI1.Pos`.
3. Open **Control** and verify the detected control model and current status.
4. Use Test/interlock/synchrocheck/origin settings as required by the IED.
5. Arm live control only after the test circuit is confirmed safe.
6. Press **OPEN** or **CLOSE** and review termination, AddCause, and process feedback.

### Focused Smart Control tests

```powershell
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~SmartControlStackTests|FullyQualifiedName~MmsReceiveRouterTests"
```

### Discover a live MMS server

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- `
  mms-discover 192.0.2.10 --port 102 --timeout-ms 30000
```

### Inspect SCL and PCAP

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- `
  inspect-scl .\samples\scl\minimal-station.scd

dotnet run --project .\apps\AR.Iec61850.Cli -- `
  generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap

dotnet run --project .\apps\AR.Iec61850.Cli -- `
  inspect-pcap .\.artifacts\out\processbus-demo.pcap `
  --scl .\samples\scl\minimal-station.scd
```

## Capability status

| Area | Current scope |
|---|---|
| MMS client | Association, discovery, FC-aware read/write services, type inspection |
| IEC 61850 control | Native Direct/SBO normal/enhanced client sequence, typed values, termination/error handling |
| Reporting | DataSet/RCB discovery, safe planning, guarded enable/GI/monitoring, evidence |
| GOOSE | Encode/decode, SCL profiles, publish/subscribe, sequence and timing diagnostics |
| Sampled Values | Encode/decode, waveform/payload generation, publishing, diagnostics, PCAP workflows |
| SCL | Station/model parsing, expected communication profiles, engineering analysis |
| PCAP | Read/write/inspect, stream analysis, expected-vs-observed binding |
| Simulator | Deterministic laboratory services; broader third-party interoperability remains in progress |
| Security | IEC 62351 profiles are not yet implemented |
| Certification | No formal third-party conformance claim |

Detailed status is maintained in the [engine maturity matrix](docs/ENGINE_MATURITY_MATRIX.md) and [full-stack roadmap](docs/FULL_STACK_ROADMAP.md).

## Platform support

The reusable core targets `.NET 8` and is designed to remain cross-platform where the underlying transport permits it. Current desktop applications use WPF and therefore require Windows. Raw process-bus Ethernet uses the Windows Npcap transport in the current public implementation.

## Documentation

- [Documentation index](docs/README.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Quick Start](docs/QUICK_START.md)
- [Smart Control Stack](docs/SMART_CONTROL_STACK.md)
- [Smart Control Tester](docs/IED_DISCOVERY_SMART_CONTROL_TESTER.md)
- [Live IED Control Validation](docs/LIVE_IED_CONTROL_VALIDATION.md)
- [MMS Reporting Workflow](docs/REPORTING_WORKFLOW.md)
- [GOOSE Diagnostics](docs/GOOSE_DIAGNOSTICS_PROFILE.md)
- [Sampled Values Diagnostics](docs/SV_DIAGNOSTICS_PROFILE.md)
- [Validation](docs/VALIDATION.md)
- [Security Policy](SECURITY.md)
- [Public Release Checklist](docs/PUBLIC_RELEASE_CHECKLIST.md)
- [Changelog](CHANGELOG.md)

## Safety and claim boundary

ARIEC61850 is intended for isolated laboratories, approved commissioning environments, education, and engineering research. Active MMS control, RCB writes, GOOSE publishing, and Sampled Values publishing can change equipment state or network behavior.

Do not connect active functions to an operational substation network without an approved test plan, switching authority, isolation boundary, and independent verification. A successful laboratory command does not establish multi-vendor interoperability or formal IEC 61850 conformance.

## Contributing

Issues, reproducible protocol captures, sanitized SCL samples, tests, and focused pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the [clean-room policy](docs/CLEAN_ROOM_POLICY.md) before submitting implementation work.

## License

Licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
