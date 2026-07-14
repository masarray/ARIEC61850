# ARIEC61850

**IEC 61850 client, guarded IED control, reporting, GOOSE, Sampled Values, SCL, and PCAP engineering toolkit for .NET 8.**

[![.NET CI](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml)
[![GitHub Pages](https://github.com/masarray/ARIEC61850/actions/workflows/pages.yml/badge.svg)](https://masarray.github.io/ARIEC61850/)
[![License](https://img.shields.io/badge/license-GPL--3.0--or--later-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/core-cross--platform-0f766e)](#platform-support)

ARIEC61850 is an independently developed C# implementation maintained under a documented clean-room and provenance policy. It supports substation-automation laboratories, approved commissioning work, FAT/SAT preparation, protocol research, education, and repeatable engineering evidence.

> **Validation boundary:** a guarded control path has been exercised with a laboratory IED. This is engineering evidence, not formal IEC 61850 conformance, broad interoperability evidence, functional-safety approval, or permission to operate primary equipment.

[Website](https://masarray.github.io/ARIEC61850/) · [Quick start](docs/QUICK_START.md) · [Control stack](docs/SMART_CONTROL_STACK.md) · [Control tester](docs/IED_DISCOVERY_SMART_CONTROL_TESTER.md) · [Current status](docs/ENGINE_MATURITY_MATRIX.md) · [Roadmap](ROADMAP.md)

> **License:** the current `main` branch and current community release packages are licensed only under `GPL-3.0-or-later`. A separate commercial license is available for proprietary integration, OEM/white-label distribution, and contractual support. See [Licensing](docs/LICENSING.md).

## Terminology

- **Native C#/.NET** means a project-owned managed implementation rather than an application wrapper around an unrelated IEC 61850 stack.
- **Smart Control** means automated discovery of `ctlModel`, live MMS type information, required control sequence, and completion evidence. It does not mean AI-based decision-making or autonomous equipment operation.
- **Guarded** means the application adds explicit target selection, typed planning, confirmation, bounded execution, cleanup, and evidence. It does not prove that equipment, interlocking, or switching procedures are safe.

## Engineering highlights

- **Control-model-aware IEC 61850 control:** select a control Data Object such as `CSWI.Pos`; the engine discovers `ctlModel`, exact live MMS types, and applies Direct Operate or Select-Before-Operate sequencing.
- **Operator-oriented OPEN/CLOSE workflow:** WPF control tester with live status, interlock check, synchrocheck, Test mode, originator, timeout, cancellation, and protocol evidence.
- **MMS client and reporting:** association, model discovery, FC-aware reads, DataSet/RCB inventory, report planning, GI, monitoring, and diagnostics.
- **GOOSE and Sampled Values:** frame codecs, SCL-backed profiles, PCAP inspection, bounded publishing, subscription, sequence supervision, and diagnostics.
- **SCL and engineering evidence:** expected-vs-observed analysis, readiness profiles, Markdown/JSON evidence, and deterministic simulation.
- **Public-source controls:** GPL licensing, warnings-as-errors, automated tests, provenance rules, source-hygiene scripts, CI, and release checks.

## Guarded IED Control Tester

The IED Discovery desktop app keeps the normal operator workflow concise:

```text
Select CSWI.Pos → open Control Tester → verify status and test conditions → stage OPEN or CLOSE → confirm
```

The engine applies the discovered sequence:

| Discovered control model | Sequence | Completion boundary |
|---|---|---|
| Direct, normal security | `Oper` | Confirmed MMS result |
| SBO, normal security | `SBO` → `Oper` | Confirmed MMS result |
| Direct, enhanced security | `Oper` → CommandTermination | Positive or negative termination |
| SBO, enhanced security | `SBOw` → `Oper` → CommandTermination | Positive or negative termination |

The control stack also manages:

- exact live `ctlVal` binding for DPC, SPC, INC/ISC, BSC, and APC variants;
- immutable `ctlNum`, timestamp `T`, origin, Test, and Check values across a sequence;
- interlock and synchrocheck request bits;
- SBO ownership, expiry, best-effort `Cancel`, and association-loss cleanup;
- `LastApplError`, `ControlError`, `AddCause`, and request/response evidence;
- process-feedback readback through the discovered status reference.

See [Control Stack](docs/SMART_CONTROL_STACK.md), [Control Tester](docs/IED_DISCOVERY_SMART_CONTROL_TESTER.md), and [Live IED Control Validation](docs/LIVE_IED_CONTROL_VALIDATION.md).

## Applications and libraries

| Component | Path | Role |
|---|---|---|
| Core protocol library | `src/AR.Iec61850` | BER, MMS, reporting, control, SCL, GOOSE, SV, PCAP, diagnostics |
| Raw Ethernet transport | `src/AR.Iec61850.Transports.Npcap` | Windows laboratory process-bus transport |
| Simulation library | `src/AR.Iec61850.Simulation` | Deterministic IED profiles, points, DataSets, reports, and events |
| IED Discovery | `apps/AR.Iec61850.IedDiscovery` | Live model browser, reporting workspace, and control tester |
| Engineering Workbench | `apps/AR.Iec61850.EngineeringWorkbench` | SCL/PCAP diagnostics and evidence-pack workflow |
| IED Simulator | `apps/AR.Iec61850.IedSimulator` | Deterministic laboratory MMS server and simulation workspace |
| SV Publisher | `apps/AR.Iec61850.SvPublisher` | Sampled Values laboratory publishing and waveform workspace |
| CLI | `apps/AR.Iec61850.Cli` | Automation, discovery, diagnostics, simulation, and PCAP commands |
| Tests | `tests/AR.Iec61850.Tests` | Codec, state-machine, binding, diagnostics, and regression coverage |

## Quick start

### Requirements

- .NET 8 SDK.
- Windows for WPF applications and the current raw-Ethernet transport.
- An isolated laboratory or approved commissioning network for active publishing or control.

### Build and test

```powershell
git clone https://github.com/masarray/ARIEC61850.git
cd ARIEC61850

dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

### Run IED Discovery

```powershell
dotnet run --project .\apps\AR.Iec61850.IedDiscovery\AR.Iec61850.IedDiscovery.csproj -c Release
```

Live discovery is read-only. It builds the model from MMS directories, preserves DataSet member order, inventories RCBs, and retrieves type information where exposed. File-service unavailability is reported without discarding an otherwise valid model snapshot.

Before a live command:

1. connect only to an approved test IED;
2. verify the selected control object and current process status;
3. verify switching authority, isolation, blocking, and independent indications;
4. configure Test, interlock, synchrocheck, and origin as required;
5. stage the command and review the confirmation;
6. review MMS acceptance, termination, application errors, and feedback separately.

### Discover a live MMS endpoint

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.0.2.10 --port 102 --timeout-ms 30000
```

### Inspect synthetic SCL and PCAP data

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\.artifacts\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd
```

## Capability status

| Area | Current public scope |
|---|---|
| MMS client | Association, discovery, FC-aware reads, typed write services, and type inspection |
| IEC 61850 control | Direct/SBO normal/enhanced sequencing, typed values, termination and application-error handling |
| Reporting | DataSet/RCB discovery, planning, guarded enable/GI, persistent monitoring, and evidence |
| GOOSE | Encode/decode, SCL profiles, publish/subscribe, sequence and supervision diagnostics |
| Sampled Values | Encode/decode, waveform and payload generation, publishing, diagnostics, and PCAP workflows |
| SCL | Station/model parsing, communication profiles, and engineering analysis |
| PCAP | Read/write/inspect, stream analysis, and expected-vs-observed binding |
| Simulator | Deterministic laboratory MMS server for discovery and read workflows; broader interoperability remains under validation |
| Security profiles | IEC 62351 profiles are not currently claimed |
| Certification | No formal third-party conformance claim |

Detailed evidence is maintained in the [Engine Maturity Matrix](docs/ENGINE_MATURITY_MATRIX.md).

## Platform support

The reusable core targets `.NET 8` and is designed to remain cross-platform where the underlying transport permits it. Current desktop applications use WPF and require Windows. Raw process-bus Ethernet uses a Windows-specific transport in the current public implementation.

## Safety, security, and claim boundary

Active MMS writes, report-control changes, control operations, GOOSE publishing, Sampled Values publishing, and raw packet replay can change equipment state or network behavior.

Do not connect active functions to an operational substation network without switching authority, an approved procedure, isolation and blocking boundaries, independent verification, and asset-owner authorization. See [Security and Operational-Risk Policy](SECURITY.md).

## Contributing

Synthetic or contributor-owned protocol fixtures and SCL samples are welcome when provenance, redistribution rights, and sanitization are documented. Do not submit customer, employer, station, credential, or proprietary material.

Read [CONTRIBUTING.md](CONTRIBUTING.md), [AGENTS.md](AGENTS.md), and the [Clean-Room and Interoperability Policy](docs/CLEAN_ROOM_POLICY.md) before submitting implementation work.

## License

The current `main` branch and current public community release packages are licensed **only** under the **GNU General Public License v3.0 or later** (`GPL-3.0-or-later`). See [LICENSE](LICENSE).

A separate negotiated commercial license is available from the copyright holder for proprietary integration, OEM or white-label distribution, closed-source redistribution, warranty, maintenance, and priority engineering support. See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).

Official branding is governed separately. See [TRADEMARK.md](TRADEMARK.md).

Historical revisions through `d61a83f5b04e7bd2b847174eeac7f4f6e81ee8e1` remain available under their original terms on branch `archive/apache-2.0-final`. The historical license text is intentionally not included in the current source tree or current release packages. See [Licensing](docs/LICENSING.md).
