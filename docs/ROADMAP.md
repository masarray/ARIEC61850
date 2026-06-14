# ARIEC61850 Roadmap

ARIEC61850 is a clean-room Apache-2 IEC 61850 engineering stack for .NET. The project is currently engine-first: desktop apps and CLI commands are development harnesses used to validate the protocol stack before product applications are split into dedicated repositories.

## Current source milestone

### N5.23 - Engine hygiene and engineering-profile foundation

- Removed benchmark-product naming from source, CLI output, tests, and public docs.
- Added forbidden-name checks to `scripts/verify-source-clean.ps1` so future public releases do not regress.
- Added `AR.Iec61850.Engineering`, a deterministic ACSI-oriented facade layer for discovery readiness, capability assessment, report-lab gating, diagnostics, and Markdown evidence.
- Added `Iec61850Client`, a high-level MMS discovery profile wrapper that can connect, discover, read DataSet directories, and build an engineering profile for app/test harnesses.
- Added engineering-profile tests so engine maturity can be validated without live hardware.
- Reframed this roadmap from app polish into protocol/service maturity.

Validation to run on a Windows dev machine with .NET 8 SDK:

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

## Public-ready maturity gates

A public release is allowed only when these gates are true:

| Gate | Required state |
|---|---|
| Build | `dotnet build .\ARIEC61850.sln -c Release` succeeds on clean Windows machine |
| Tests | All tests pass; protocol codec and diagnostics tests keep increasing |
| Clean-room | Source-clean script passes after restore/build/test |
| Naming | No benchmark-product names in source/docs/scripts/tests |
| Docs | README, Quick Start, architecture, roadmap, and release checklist are user-facing |
| Samples | At least one SCL sample, one GOOSE/SV loopback, and one MMS discovery path are documented |
| Packaging | CLI and development harness apps can be packaged as portable single-file builds |
| Evidence | Discovery/report/process-bus commands can export repeatable JSON/Markdown evidence |

## Near-term engine roadmap

### N5.24 - ACSI service facade hardening

- Expand `Iec61850Client` into explicit service groups: model browser, data reader, DataSet client, report client, and file/log/setting placeholders.
- Add service-result contracts for all public engine calls.
- Add a deterministic fake session interface so high-level service workflows can be tested without a live IED.
- Add profile JSON export/import for `Iec61850EngineeringProfile`.

### N5.25 - SCL deep model phase 1

- Resolve `DataTypeTemplates`: `LNodeType`, `DOType`, `DAType`, `EnumType`.
- Parse and normalize `Services`, `Communication`, `ConnectedAP`, `Address/P`, `GSE`, `SMV`, `Inputs/ExtRef`, and `ClientLN`.
- Generate expected-vs-observed models for reports, GOOSE, SV, and DataSets.

### N5.26 - Report industrial hardening

- Implement explicit URCB/BRCB state machine.
- Handle `Resv`, `ResvTms`, `Owner`, `EntryID`, `PurgeBuf`, `BufOvfl`, `TrgOps`, `OptFlds`, `BufTm`, and `IntgPd` as first-class typed fields.
- Add report member-order validation using DataSet directory evidence.
- Add report loss, duplicate, stale timestamp, and GI-result diagnostics.

### N5.27 - GOOSE diagnostics engine

- Add SCL-bound expected stream profiles.
- Add `stNum/sqNum` state machine, TTL expiry, retransmission timing, VLAN/AppID/MAC/confRev checks, and expected-vs-observed matching.
- Add PCAP replay findings for missing publisher, duplicate publisher, unexpected publisher, replay suspicion, and flood/storm patterns.

### N5.28 - Sampled Values analyzer engine

- Add SV subscriber over the existing process-bus frame-source abstraction.
- Add stream registry, sample counter continuity, sample-rate detection, ASDU/layout checks, RMS, phasor, jitter/dropout, and PTP-correlation hooks.
- Keep the current SV publisher/injector as a test harness for the analyzer.

### N5.29 - Read-only MMS server alpha

- Add TCP/TPKT/COTP/ACSE/MMS accept path.
- Serve domains, variables, access attributes, and named variable lists from a simulator model.
- Keep write/control disabled until read-only discovery and report readback are stable.

### N5.30 - Simulator bridge

- Map simulator profiles into MMS server model, GOOSE publisher profiles, and SV publisher profiles.
- Add scenario scheduler for value changes, quality changes, timestamp faults, report triggers, GOOSE transitions, and SV disturbances.

## Product app strategy

Product applications should live in separate repositories after the engine contracts stabilize. This repository should expose the reusable stack and keep only lightweight harnesses for validation:

```text
ARIEC61850 engine repo
├─ protocol stack
├─ simulation core
├─ diagnostics
├─ evidence/export
├─ CLI validation harness
└─ minimal WPF development harnesses
```

Product repos can later consume the engine as project references or NuGet packages without pulling protocol logic into UI code.
