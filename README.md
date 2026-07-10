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
| Core library | `src/AR.Iec61850` | BER, MMS, SCL, GOOSE, SV, PCAP, reporting, diagnostics, engineering/report-readiness facades |
| Live Ethernet transport | `src/AR.Iec61850.Transports.Npcap` | Npcap-backed raw process-bus transport for Windows lab use |
| CLI toolkit | `apps/AR.Iec61850.Cli` | SCL inspection, PCAP generation/inspection, MMS discovery/read/reporting commands |
| WPF app | `apps/AR.Iec61850.SvPublisher` | Desktop Sampled Values publisher / injector workspace |
| WPF app | `apps/AR.Iec61850.IedDiscovery` | Live IED discovery workspace for MMS model, DataSet, and RCB inventory |
| WPF app | `apps/AR.Iec61850.IedSimulator` | SCL-backed IED simulator with deterministic values, DataSets, RCBs, and a read-only loopback MMS server |
| WPF app | `apps/AR.Iec61850.EngineeringWorkbench` | Read-only engineering workbench for SCL, PCAP diagnostics, MMS loopback, and evidence-pack export |
| Simulation library | `src/AR.Iec61850.Simulation` | In-memory IED profile and deterministic point/event simulation foundation |
| Tests | `tests/AR.Iec61850.Tests` | Automated unit and protocol-shape tests |
| Samples | `samples/scl` | Small SCL files for local validation and examples |
| Docs | `docs` | Quick start, architecture, roadmap, engine maturity matrix, reporting workflow, validation, release packaging |
| Landing page | `landing` | Static GitHub Pages website |

## Status

This is a lab-oriented engineering toolkit, not a formal conformance-certified IEC 61850 product.

Implemented areas include:

- ASN.1 BER reader/writer.
- MMS data value codec and common client services.
- TCP/TPKT/COTP/ACSE/MMS association foundation.
- MMS model discovery, FC-aware path resolution, smart read, dataset directory inspection.
- RCB discovery, report planning, guarded report enable, GI trigger, receive loop, diagnostics, and evidence export.
- GOOSE frame builder/parser, SCL-backed publisher profiles, publisher session, PCAP sniffer diagnostics, live subscriber command, `stNum`/`sqNum`/TAL supervision, and changed-value summaries.
- Sampled Values frame builder/parser, payload generation, payload decode, and WPF publisher/injector workspace.
- PCAP writer/reader/inspector and stream playback helpers.
- Npcap-backed raw Ethernet transport for isolated Windows lab adapters.
- WPF IED Discovery workspace for live MMS model/DataSet/RCB snapshot export.
- IED Discovery data browser with expandable MMS structure rows and guarded Enable RCB dialog.
- IED Simulator with deterministic/SCL-derived profiles, read-only loopback MMS server, directory/read service probes, and write rejection.
- WPF Engineering Workbench Alpha that orchestrates SCL engineering, process-bus binding, GOOSE/SV diagnostics, MMS read-only loopback, and evidence-pack export.
- Engineering-profile facade that converts live discovery into capability assessment, report-lab readiness, diagnostics, and Markdown evidence.
- Report-readiness profile engine that produces acceptance gates, RCB candidate ranking, selected static report plan, and guarded session-profile JSON before any RCB write.

Experimental or future areas:

- multi-vendor long-duration MMS reporting soak evidence;
- full buffered report recovery and replay workflows;
- MMS file/log/setting-group/control model services;
- multi-client and third-party MMS interoperability evidence for the IED simulator server;
- live raw SV subscriber CLI loop on top of the Npcap receive path;
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
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
```

Build the WPF apps directly:

```powershell
dotnet build .\apps\AR.Iec61850.SvPublisher\AR.Iec61850.SvPublisher.csproj -c Release
dotnet build .\apps\AR.Iec61850.IedDiscovery\AR.Iec61850.IedDiscovery.csproj -c Release
dotnet build .\apps\AR.Iec61850.IedSimulator\AR.Iec61850.IedSimulator.csproj -c Release
dotnet build .\apps\AR.Iec61850.EngineeringWorkbench\AR.Iec61850.EngineeringWorkbench.csproj -c Release
```

## Quick examples

Inspect an SCL file:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
```

Generate and inspect a demo PCAP:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\.artifacts\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\.artifacts\out\processbus-demo.pcap --scl .\samples\scl\minimal-station.scd --delay-ms 0 --limit 20
```

Discover a live MMS server or IED:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.0.2.10 --port 102 --timeout-ms 30000 --max-report-probes 16
```

Plan report usage before writing to any RCB:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.0.2.10 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
```

Generate read-only report readiness evidence and a guarded session profile:

  ```powershell
  dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-readiness-profile 192.0.2.10 --port 102 --timeout-ms 120000 --output .\.artifacts\out\report-readiness.md --json .\.artifacts\out\report-readiness.json --session-json .\.artifacts\out\report-session-profile.json
  ```

Run a local, read-only virtual IED from the demo profile or an SCL file:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- simulate-ied --scl .\samples\scl\minimal-station.scd --port 102 --duration-sec 60
```

The virtual IED is a lab-only server: it serves the implemented directory/read paths and rejects writes. It is not a formal conformance claim.

  Run a guarded report monitor:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.0.2.10 --port 102 --timeout-ms 120000 --rcb IED1LD0/LLN0.RP.rpt01 --duration-sec 60 --evidence .\.artifacts\out\report-session01 --yes
```

Run a bounded GOOSE publisher dry run from SCL:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 1 --stream-index 1 --frames 4 --dry-run
```

Live GOOSE publishing requires `--yes` and must only be used on an isolated lab adapter.

Run a read-only live GOOSE subscriber on a lab adapter:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- goose-subscribe-live --adapter 1 --scl .\samples\scl\minimal-station.scd --duration-sec 30
```

Without `--scl`, the subscriber still decodes traffic but reports values as semantically anonymous.

## Windows single-file WPF package

Local packaging:

```powershell
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App SvPublisher
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0 -App EngineeringWorkbench
```

The script builds and tests the solution, publishes the selected WPF app as a self-contained Windows x64 single EXE, and creates release assets under `.artifacts/release`.

The same packaging flow is available in GitHub Actions through `.github/workflows/release-package.yml`.

## Keeping the repository clean

Compiled binaries are redirected to `.artifacts/` by `Directory.Build.props`. SDK intermediate folders may still be created locally for WPF markup compilation and are ignored by source control. To reset the working tree after local builds, run:

```powershell
.\scripts\clean-local-artifacts.cmd
.\scripts\verify-source-clean.cmd
```

Do not commit `.artifacts/`, `out/`, `evidence/`, captures, DLL/EXE/PDB files, or Visual Studio local state.

## Documentation

- [Quick Start](docs/QUICK_START.md)
- [Architecture](docs/ARCHITECTURE.md)
- [MMS Reporting Workflow](docs/REPORTING_WORKFLOW.md)
- [Process-Bus Binding Profile](docs/PROCESS_BUS_BINDING_PROFILE.md)
- [Sampled Values Diagnostics Profile](docs/SV_DIAGNOSTICS_PROFILE.md)
- [Full Stack Roadmap](docs/FULL_STACK_ROADMAP.md)
- [GOOSE Engine Audit](docs/GOOSE_ENGINE_AUDIT.md)
- [Release Packaging](docs/RELEASE_PACKAGING.md)
- [Validation](docs/VALIDATION.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Clean-room Policy](docs/CLEAN_ROOM_POLICY.md)
- [Public Release Checklist](docs/PUBLIC_RELEASE_CHECKLIST.md)

## License

Licensed under the [Apache License 2.0](LICENSE).

## Safety note

Active MMS writes, report enable operations, GOOSE publishing, and Sampled Values publishing must be used only in isolated labs or approved test networks. Do not publish process-bus traffic into an operational substation network without an approved test plan and isolation boundary.


## N5.25 — SCL Deep Engineering Profile

This milestone adds an offline SCL engineering profile engine. It extracts access points, server/logical-device/logical-node structure, expected report sessions, expected GOOSE/SV streams, subscriber ExtRef mapping, service declarations, and static findings. The profile is available through `scl-engineering-profile` and is designed as the expected-model input for future report, GOOSE, SV, simulator, and evidence engines.

## N5.26 — Expected-vs-Observed Process-Bus Binding

This milestone adds a read-only process-bus binding engine. It compares expected GOOSE/SV streams from SCL against observed PCAP traffic and produces typed Markdown/JSON evidence for missing streams, unexpected streams, APPID/MAC/VLAN/confRev mismatch, and sequence/timing anomalies.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- process-bus-binding-profile .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap --output .\.artifacts\out\process-bus-binding.md --json .\.artifacts\out\process-bus-binding.json
```

## N5.26 — Expected-vs-Observed Process-Bus Binding

This milestone adds a read-only process-bus binding engine. It compares expected GOOSE/SV streams from SCL against observed PCAP traffic and produces typed Markdown/JSON evidence for missing streams, unexpected streams, APPID/MAC/VLAN/confRev mismatch, and sequence/timing anomalies.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- process-bus-binding-profile .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap --output .\.artifacts\out\process-bus-binding.md --json .\.artifacts\out\process-bus-binding.json
```

## N5.27 — GOOSE Diagnostics Profile

This milestone adds a read-only GOOSE diagnostic engine. It turns SCL expected GOOSE streams and PCAP/live observed summaries into typed findings for missing/extra publishers, APPID/MAC/VLAN/confRev mismatch, DataSet value-count mismatch, `stNum`/`sqNum` anomalies, supervision timeout, test/needs-commissioning flags, and suspicious value changes without state-number increment.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\goose-diagnostic-demo.pcap --sv-frames 0 --goose-scenario diagnostic
dotnet run --project .\apps\AR.Iec61850.Cli -- goose-diagnostics-profile .\samples\scl\minimal-station.scd .\.artifacts\out\goose-diagnostic-demo.pcap --output .\.artifacts\out\goose-diagnostics.md --json .\.artifacts\out\goose-diagnostics.json
```


## N5.28 — Sampled Values Diagnostics Profile

This milestone adds a read-only SV diagnostic engine. It turns SCL expected SV streams and PCAP/live observed summaries into typed findings for missing/extra streams, APPID/MAC/VLAN/confRev mismatch, `nofASDU` mismatch, sample-rate/sample-mode mismatch, payload decode issues, `smpCnt` gaps/duplicates/out-of-order samples, wraps, and `smpSynch` issues.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\sv-diagnostic-demo.pcap --goose-frames 0 --sv-scenario diagnostic
dotnet run --project .\apps\AR.Iec61850.Cli -- sv-diagnostics-profile .\samples\scl\minimal-station.scd .\.artifacts\out\sv-diagnostic-demo.pcap --output .\.artifacts\out\sv-diagnostics.md --json .\.artifacts\out\sv-diagnostics.json
```

### MMS read-only virtual server profile

Generate the first server-side virtual IED evidence profile:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-server-readonly-profile --steps 5 --output .\.artifacts\out\mms-server-readonly.md --json .\.artifacts\out\mms-server-readonly.json
```

MMS listener skeleton self-probe:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-listener-skeleton-profile --port 0 --output .\.artifacts\out\mms-listener-skeleton.md --json .\.artifacts\out\mms-listener-skeleton.json
```

This is an offline alpha server model. It validates logical-device directory, logical-node directory, point reads, DataSet reads, RCB exposure, and read-only write rejection before a live TCP/MMS listener is added.

### N5.31 MMS handshake codec profile

N5.31 adds an offline handshake codec evidence path for the server-side roadmap. It validates TPKT framing, COTP CR/CC/Data TPDU handling, and ISO Session / ACSE / MMS association payload inspection before the listener skeleton is upgraded to real MMS PDU handling.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-handshake-codec-profile --output .\.artifacts\out\mms-handshake-codec.md --json .\.artifacts\out\mms-handshake-codec.json
```

### N5.32 MMS handshake listener profile

N5.32 moves the handshake foundation from offline codec proof into a loopback listener proof. It accepts a TCP client, receives TPKT/COTP CR, sends COTP CC, receives COTP Data TPDU, and inspects the ACSE/MMS association payload. It still does not claim a full MMS server response.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-handshake-listener-profile --port 0 --output .\.artifacts\out\mms-handshake-listener.md --json .\.artifacts\out\mms-handshake-listener.json
```

### MMS association response profile

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-association-response-profile --port 0 --output .\.artifacts\out\mms-association-response.md --json .\.artifacts\out\mms-association-response.json
```

This loopback probe verifies TPKT/COTP transport, ACSE AARE response generation, and MMS InitiateResponse marker inspection before live confirmed MMS request dispatch.


### MMS confirmed-request and read-only loopback profiles

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-confirmed-request-skeleton-profile --port 0 --output .\.artifacts\out\mms-confirmed-request-skeleton.md --json .\.artifacts\out\mms-confirmed-request-skeleton.json

dotnet run --project .\apps\AR.Iec61850.Cli -- mms-confirmed-request-ber-profile --port 0 --output .\.artifacts\out\mms-confirmed-request-ber.md --json .\.artifacts\out\mms-confirmed-request-ber.json

dotnet run --project .\apps\AR.Iec61850.Cli -- mms-readonly-loopback-profile --port 0 --output .\.artifacts\out\mms-readonly-loopback.md --json .\.artifacts\out\mms-readonly-loopback.json
```

The skeleton profile validates the request lifecycle with deterministic internal envelopes. The BER profile upgrades that path to native MMS BER confirmed-request payloads. The read-only loopback alpha profile unifies virtual model readiness, association response, native BER dispatch, and write guard evidence into one server-side readiness gate.


### Public alpha readiness gate

Run the aggregate engine readiness profile before tagging a developer-preview release:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- public-alpha-readiness-profile --output .\.artifacts\out\public-alpha-readiness.md --json .\.artifacts\out\public-alpha-readiness.json
```

This gate combines SCL engineering, expected-vs-observed process-bus diagnostics, GOOSE/SV healthy baselines, and the read-only MMS loopback alpha. It is an alpha readiness evidence artifact, not a conformance claim.

### WPF Engineering Workbench Alpha

The repository now includes a read-only WPF harness for exercising the engine profiles from one workflow:

```powershell
dotnet run --project .\apps\AR.Iec61850.EngineeringWorkbench
```

It opens an SCL file, optionally observes a PCAP, runs SCL engineering, process-bus binding, GOOSE/SV diagnostics, MMS read-only loopback, and exports a structured evidence pack. See `docs/ENGINEERING_WORKBENCH_ALPHA.md` and `docs/WORKBENCH_EVIDENCE_PACK.md`.

### Workbench evidence pack CLI

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- workbench-evidence-pack --scl .\samples\scl\minimal-station.scd --output .\.artifacts\workbench-pack
```

This command generates `README.md`, `manifest.json`, and per-profile Markdown/JSON artifacts for SCL engineering, process-bus binding, GOOSE diagnostics, SV diagnostics, MMS read-only loopback, and public-alpha readiness.


### N5.41.2 — IEC 61850 Value Binding Engine

Adds schema-driven DA value binding for the IED Discovery Workbench. Structured MMS values are now bound through IEC 61850 metadata, CDC templates, quality/timestamp decoders, and control-operation templates instead of positional index guesses.

## N5.41.3 display refinement

- IED Discovery detail view now follows a semantic child-row display policy: Quality and timestamp are rendered as IEC 61850 child rows, not noisy flat columns; common SAS logical nodes are prioritized in the explorer.



### Smart Value Reading Engine

The IED Discovery Workbench now uses an engine-owned value presentation layer for IED identity resolution, logical-device aliases, FC-aware read targets, collapsed DO summaries, and recursive semantic decoding of `q`, `t`, vector/analogue, and control structures. IED identity is derived from all live MMS domains with known logical-device suffixes, source, confidence, and candidate evidence; numerical parts such as `OLSF501` are preserved. The WPF app renders the engine output and does not guess MMS structure positions.

- N5.41.5: IED Discovery smart monitoring/report readiness adds Online state gating, live polling Activity Monitor, report-driven monitor updates, and static/dynamic/occupied RCB presentation.


## N5.41.6 IED Discovery session workflow fixes

- Smart RCB fallback in WPF report enable flow.
- Close IED clears explorer and panels.
- Save discovered model defaults to IID-oriented engine export.
- Open SCL projects offline SCL into the same explorer/detail visual shape as live discovery.


## N5.41.7 IED Discovery Reporting Repair

- Live RCB runtime refresh before detail display and enable planning.
- Static/dynamic report planning now uses refreshed RCB state.
- Explicit Pin and Unpin activity monitor workflow.
- Report group summaries expose locked/static/dynamic state.


## N5.42 Persistent Report Monitor

The IED Discovery Workbench now starts an interactive report monitor that keeps `RptEna=true` until Stop RCB or Close IED. Report-list rows can be opened by double-click, RCB state is refreshed after enable/stop, and received report values update the Activity Monitor.


## N5.42.1 Report Value Projector

The IED Discovery Workbench now projects report payload values through a report DataSet binding layer before updating the Activity Monitor. Static and dynamic reports can update readable engineering signals with value, quality, timestamp, reason, and source instead of showing raw `Struct(...)` values.
