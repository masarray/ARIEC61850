# ARIEC61850 Roadmap

ARIEC61850 is a clean-room Apache-2 IEC 61850 engineering stack for .NET. The project is currently engine-first: desktop apps and CLI commands are development harnesses used to validate the protocol stack before product applications are split into dedicated repositories.

## Current source milestone


### N5.28 - Sampled Values diagnostics profile foundation

- Added `SampledValuesDiagnosticsProfile`, a typed evidence contract for SV stream health, sample counter continuity, synchronization state, payload length, and SCL-to-traffic consistency.
- Added `SampledValuesDiagnosticsProfileBuilder` with deterministic findings for missing/extra SV streams, APPID/MAC/VLAN/confRev mismatch, `nofASDU` mismatch, sample-rate/sample-mode mismatch, payload decode issues, `smpCnt` gap/duplicate/out-of-order/wrap, and `smpSynch` issues.
- Added `sv-diagnostics-profile`, a read-only CLI command that consumes `<scl-file> <pcap-file>` and exports Markdown/JSON diagnostic evidence.
- Extended `generate-pcap` with `--sv-scenario diagnostic` so the SV finding engine can be tested offline without IED hardware.
- Added deterministic unit tests for healthy, missing, unexpected, sample-counter anomaly, synchronization/payload anomaly, and Markdown evidence paths.

Validation command for the new milestone:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\sv-diagnostic-demo.pcap --goose-frames 0 --sv-scenario diagnostic
dotnet run --project .\apps\AR.Iec61850.Cli -- sv-diagnostics-profile .\samples\scl\minimal-station.scd .\.artifacts\out\sv-diagnostic-demo.pcap --output .\.artifacts\out\sv-diagnostics.md --json .\.artifacts\out\sv-diagnostics.json
```

### N5.27 - GOOSE diagnostics profile foundation

- Added `GooseDiagnosticsProfile`, a typed evidence contract for GOOSE health, sequence integrity, supervision, flag status, and SCL-to-traffic consistency.
- Added `GooseDiagnosticsProfileBuilder` with deterministic findings for missing/extra publishers, APPID/MAC/VLAN/confRev mismatch, DataSet value-count mismatch, `stNum`/`sqNum` gaps or regressions, supervision timeout, test flag, needs-commissioning flag, and suspicious value changes without a state-number increment.
- Added `goose-diagnostics-profile`, a read-only CLI command that consumes `<scl-file> <pcap-file>` and exports Markdown/JSON diagnostic evidence.
- Extended `generate-pcap` with `--goose-scenario diagnostic` so the GOOSE finding engine can be tested offline without IED hardware.
- Added deterministic unit tests for healthy, missing, unexpected, sequence/supervision anomaly, flag detection, and Markdown evidence paths.

Validation command for the new milestone:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\goose-diagnostic-demo.pcap --sv-frames 0 --goose-scenario diagnostic
dotnet run --project .\apps\AR.Iec61850.Cli -- goose-diagnostics-profile .\samples\scl\minimal-station.scd .\.artifacts\out\goose-diagnostic-demo.pcap --output .\.artifacts\out\goose-diagnostics.md --json .\.artifacts\out\goose-diagnostics.json
```

### N5.26 - Expected-vs-observed process-bus binding foundation

- Added `ExpectedObservedBindingProfile`, a typed evidence contract that compares SCL expected GOOSE/SV streams against observed PCAP/live process-bus summaries.
- Added `ExpectedObservedBindingProfileBuilder` with deterministic findings for missing expected streams, unexpected observed streams, APPID/MAC/VLAN/confRev mismatch, DataSet value-count mismatch when available, and sequence/timing anomalies.
- Added `process-bus-binding-profile`, a read-only CLI command that consumes `<scl-file> <pcap-file>` and exports Markdown/JSON evidence.
- Added deterministic unit tests for exact binding, missing expected streams, unexpected observed streams, partial mismatch detection, and Markdown evidence.
- This is the first bridge from static SCL engineering into observed process-bus traffic, preparing the engine for full GOOSE/SV diagnostics and station-level mapping validation.

Validation command for the new milestone:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- process-bus-binding-profile .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap --output .\.artifacts\out\process-bus-binding.md --json .\.artifacts\out\process-bus-binding.json
```

### N5.25 - SCL engineering profile foundation

- Added an offline SCL engineering profile engine that extracts access points, server/logical-device/logical-node structure, expected report sessions, expected GOOSE/SV streams, subscriber ExtRef mapping, service declarations, and static findings.
- Added `scl-engineering-profile`, an offline CLI command that exports Markdown/JSON profile evidence without requiring live hardware.
- Added tests for SCL profile extraction, ExtRef mapping, service declaration extraction, incomplete process-bus binding detection, and Markdown generation.

### N5.24 - Report readiness profile foundation

- Added a static report readiness profile builder that turns live discovery + DataSet directory evidence into acceptance gates, RCB candidate ranking, selected static report plan, diagnostics, and guarded session profile JSON.
- Added `Iec61850ReportReadinessProfile` as a deterministic engine contract for future report-workspace apps.
- Added `Iec61850Client.DiscoverStaticReportReadinessProfileAsync(...)` so product apps can request a report profile without touching low-level MMS session classes.
- Added `mms-report-readiness-profile`, a read-only CLI command that can export Markdown, JSON readiness evidence, and a guarded report-session profile.
- Added deterministic unit tests for ready, blocked, occupied, reserved, and Markdown evidence cases.

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

### N5.28 - Report industrial hardening

- Implement explicit URCB/BRCB state machine.
- Handle `Resv`, `ResvTms`, `Owner`, `EntryID`, `PurgeBuf`, `BufOvfl`, `TrgOps`, `OptFlds`, `BufTm`, and `IntgPd` as first-class typed fields.
- Add report member-order validation using DataSet directory evidence.
- Add report loss, duplicate, stale timestamp, and GI-result diagnostics.

### N5.29 - Sampled Values analyzer engine

- Add SV subscriber over the existing process-bus frame-source abstraction.
- Add stream registry, sample counter continuity, sample-rate detection, ASDU/layout checks, RMS, phasor, jitter/dropout, and PTP-correlation hooks.
- Keep the current SV publisher/injector as a test harness for the analyzer.

### N5.31 - TPKT/COTP/ACSE/MMS read-only listener alpha

- Attach TPKT framing to the listener skeleton.
- Add COTP connection confirm and ACSE associate response.
- Add MMS initiate response and confirmed read-directory/read request dispatch.
- Keep write/control disabled until read-only discovery and report readback are stable.

### N5.32 - Simulator bridge

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

## N5.30 — MMS Listener Skeleton Profile

The engine now has a loopback TCP listener skeleton for the read-only virtual IED service handler. It can be tested without hardware using `mms-listener-skeleton-profile`, and it validates listener bind, accepted connection, request dispatch, read-only service responses, write rejection, and Markdown/JSON evidence.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-listener-skeleton-profile --port 0 --output .\.artifacts\out\mms-listener-skeleton.md --json .\.artifacts\out\mms-listener-skeleton.json
```

## N5.29 — MMS Read-Only Server Alpha

The engine now has an offline server-side model profile that converts the simulator profile into a read-only virtual IED. It can be tested without hardware using `mms-server-readonly-profile`, and it validates directory, read, DataSet read, RCB exposure, and write rejection.

