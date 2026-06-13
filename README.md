# ARIEC61850

Clean-room IEC 61850 stack for .NET: MMS, Reporting, GOOSE, Sampled Values,
SCL, PCAP, and future engineering tools.

[![.NET CI](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/dotnet-ci.yml)
[![Pages](https://github.com/masarray/ARIEC61850/actions/workflows/deploy-pages.yml/badge.svg)](https://github.com/masarray/ARIEC61850/actions/workflows/deploy-pages.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](#build-and-test)
[![Protocol](https://img.shields.io/badge/protocol-IEC--61850-0f766e)](#what-is-this)
[![Status](https://img.shields.io/badge/status-lab--validated--MVP-2563eb)](#current-status)

[Website](https://masarray.github.io/ARIEC61850/) | [Quick Start](docs/QUICK_START.md) | [Architecture](docs/ARCHITECTURE.md) | [Validation](docs/VALIDATION.md) | [Roadmap](ROADMAP.md)

## What is this?

`ARIEC61850` is a native C#/.NET IEC 61850 protocol stack and lab toolkit. It is
being built to help engineers create IEC 61850 client, server, simulator,
publisher, subscriber, validation, and commissioning software without depending
on a third-party IEC 61850 runtime stack.

The current repository contains:

- a reusable protocol library in `src/AR.Iec61850`;
- an Npcap raw Ethernet transport in `src/AR.Iec61850.Transports.Npcap`;
- a CLI tester in `apps/AR.Iec61850.Cli`;
- unit and golden-style tests in `tests/AR.Iec61850.Tests`;
- sample SCL files and validation notes.

The CLI is the first user-facing tool. The reusable stack is the main asset.

## Why it exists

IEC 61850 is powerful but easy to make painful for users. A basic client often
expects the user to know logical devices, logical nodes, data objects, data
attributes, Functional Constraints, MMS item names, DataSet order, RCB state,
and control model details before a simple read or report can work.

This project exists to make that easier:

- discover the live IED model before asking the user to guess paths;
- resolve Functional Constraints automatically when possible;
- keep DataSet member order visible and typed;
- make reporting a guarded state machine, not a blind `RptEna=true` shortcut;
- expose raw protocol evidence when something fails;
- support SCL, PCAP, GOOSE, SV, MMS client workflows, and future simulator/UI
  products from the same stack.

The long-term goal is a serious open .NET foundation for IEC 61850 engineering
software: lab testing, FAT/SAT support, commissioning diagnostics, protocol
education, and repeatable evidence generation.

## Current Status

This is a lab-validated MVP, not a formal conformance-certified stack.

Implemented and tested today:

- ASN.1 BER reader/writer.
- MMS data value codec for common primitive and structured values.
- TCP/TPKT/COTP/ACSE/MMS client association.
- MMS `GetNameList` discovery for domains, variables, and named variable lists.
- Live IED model directory with Functional Constraint extraction.
- Smart FC resolver and smart read CLI.
- DataSet directory using MMS named variable list attributes.
- Confirmed write foundation used by guarded report/DataSet workflows.
- MMS receive pump with pending invoke registry, invoke-matched confirmed
  responses/errors, and queued unconfirmed InformationReports.
- RCB discovery, report readiness planning, static report planning, and dynamic
  report planning.
- Guarded static report enable, GI, receive, value mapping, and cleanup.
- Guarded static `mms-report-monitor` command on top of the receive pump,
  including optional smart-read polling while reports are active.
- Guarded dynamic DataSet create, RCB bind, report enable, GI, receive, cleanup,
  and DataSet delete.
- Report frames now preserve raw access-result count, inclusion bitstring index,
  and included DataSet member indexes for diagnostics.
- Report frames decode typed report header evidence: `RptID`, `OptFlds`,
  `SqNum`, `TimeOfEntry`, `DatSet`, `BufOvfl`, `EntryID`, `ConfRev`, and
  per-value reason-for-inclusion when present.
- Report sessions produce reusable diagnostics for report counts, mapping
  failures, sequence gaps/regressions, duplicate report keys, EntryID
  gaps/regressions, reason counts, poll-read status, write failures, and buffer
  overflow evidence.
- Guarded report commands can export evidence artifacts with `--evidence`,
  including `summary.json`, `reports.json`, `poll-reads.json`,
  `write-steps.json`, `report-timeline.json`, and `summary.md`.
- MMS `binary-time` values are preserved as raw evidence and decoded to UTC/time-of-day when the encoding is supported.
- GOOSE frame builder/parser and SCL-backed live publisher.
- Sampled Values frame builder/parser and SCL-backed live publisher.
- PCAP writer, reader, inspector, and stream playback.
- Npcap raw Ethernet transport for live process-bus lab publishing.
- 71 automated tests passing in the latest local validation run.

Still experimental or not implemented yet:

- long multi-vendor receive-pump soak evidence while reports, reads, and writes
  interleave during a report session;
- multi-vendor report optional-field coverage for data-reference and
  segmentation variants;
- BRCB recovery with `EntryID`, `PurgeBuf`, duplicate handling, and reconnect
  diagnostics;
- MMS file transfer services;
- MMS log and setting group services;
- IEC 61850 control model services;
- MMS server and IED simulator;
- GOOSE subscriber and SV subscriber engines;
- TLS/IEC 62351 security profile;
- formal conformance test evidence.

## Install Requirements

- .NET 8 SDK.
- Windows when using the current Npcap live Ethernet transport.
- Npcap for live GOOSE/SV publishing.
- Isolated lab NIC, TAP, or test switch for active raw Ethernet traffic.
- A test IED or simulator for live MMS commands.

## Build and Test

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

## Quick Start

Inspect an SCL file:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
```

Generate and inspect a PCAP:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\out\processbus-demo.pcap --delay-ms 50 --limit 12
```

List live Ethernet adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Run an SV publish dry run without sending packets:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 1 --stream-index 1 --frames 4000 --dry-run
```

Publish bounded GOOSE traffic on an isolated lab adapter:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 1 --stream-index 1 --duration-sec 10 --toggle-every-sec 2 --yes
```

Discover a live IEC 61850 MMS server or IED:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.168.1.10 --port 102 --timeout-ms 30000 --max-report-probes 16
```

Build the live MMS directory and show FC-aware points:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.168.1.10 --port 102 --timeout-ms 30000 --show-points --raw-limit 40
```

Find, resolve, and read values without manually typing `ST`, `MX`, `CO`, `RP`,
or `BR`:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-find 192.168.1.10 MMXU --fc MX --raw-limit 40
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-resolve 192.168.1.10 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.168.1.10 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
```

Inspect a live DataSet directory:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-dataset-directory 192.168.1.10 OCR7SR12PROT/LLN0.DataSet --port 102 --timeout-ms 60000 --raw-limit 80
```

Plan report usage before writing to any RCB:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.168.1.10 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-static-plan 192.168.1.10 --port 102 --timeout-ms 120000 --read-values
```

Run a guarded static report smoke test:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-static-live 192.168.1.10 --port 102 --timeout-ms 120000 --duration-sec 15 --yes
```

Run a guarded static report monitor:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.168.1.10 --port 102 --timeout-ms 120000 --rcb OCR7SR12PROT/LLN0.BR.brcbA01 --duration-sec 60 --yes
```

Run a guarded report monitor and poll smart-read values while the report
subscription is active:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.168.1.10 --port 102 --timeout-ms 120000 --rcb OCR7SR12PROT/LLN0.BR.brcbA01 --duration-sec 60 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --poll-interval-ms 1000 --yes
```

Export report evidence artifacts for FAT/SAT notes:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.168.1.10 --port 102 --timeout-ms 120000 --rcb OCR7SR12PROT/LLN0.BR.brcbA01 --duration-sec 60 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --poll-interval-ms 1000 --evidence .\out\report-session01 --yes
```

Run a guarded dynamic report smoke test:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-dynamic-live 192.168.1.10 --port 102 --timeout-ms 120000 --points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f,OCR7SR12MEAS/MMXU1.A.phsA.cVal.mag.f --dataset-name AR_DYN_DS01 --duration-sec 5 --gi true --delete-dataset true --yes
```

Use your own IED IP, DataSet, RCB, and points. Do not run report-enable commands
against production equipment or RCBs used by another client.

## Safety Model

`ARIEC61850` is deliberately conservative with live operations.

- Discovery and planning commands are read-only.
- Active GOOSE/SV publishing requires an explicit adapter and `--yes` unless
  `--dry-run` is used.
- Report live commands are guarded and should be run only on isolated lab IEDs
  or confirmed unused RCBs.
- The stack must not write during discovery.
- Control services are not exposed as generic writes.
- Normal Windows/Npcap timing is lab/screening grade, not protection-grade
  timing evidence.

## Live Validation Snapshot

Latest local validation evidence:

- `dotnet build .\ARIEC61850.slnx -c Release` passed.
- `dotnet test .\ARIEC61850.slnx -c Release --no-build` passed with 71 tests.
- Live MMS association to lab IED `192.16.1.157:102` reached `MmsInitiated`.
- Live directory evidence: 4 logical devices, 123 logical nodes, 9,464
  FC-aware points, 3,456 report attributes, and 457 control attributes.
- Report inventory evidence: 286 RCBs, including 8 BRCBs and 278 URCBs.
- Static BRCB smoke test received InformationReport frames and mapped 2 of 2
  DataSet values.
- Static BRCB monitor kept the receive pump active while smart-read polling ran
  during the report session: 4 report frames received and 4/4 poll reads
  succeeded.
- Live report header evidence decoded `RptID`, `OptFlds`, `SqNum`,
  `TimeOfEntry`, `DatSet`, `BufOvfl`, `EntryID`, `ConfRev`, and
  reason-for-inclusion.
- Report diagnostics and evidence export generated sequence/EntryID/reason
  summaries plus JSON/Markdown artifacts.
- Dynamic report smoke test created a DataSet, bound an RCB, enabled reporting,
  triggered GI, received a report, cleared the RCB DataSet, and deleted the
  dynamic DataSet.
- Live SV publish smoke sent 20,000 frames over five seconds at roughly 4,000
  frames per second in a lab setup.
- Live GOOSE publish smoke validated bounded retransmission behavior, `stNum`
  changes, and `sqNum` reset behavior.

See [docs/VALIDATION.md](docs/VALIDATION.md) for the full validation notes and
remaining limitations.

## Architecture

```text
TCP
  -> TPKT
  -> COTP
  -> ISO Session / Presentation
  -> ACSE
  -> MMS

Ethernet
  -> VLAN
  -> GOOSE / Sampled Values

SCL
  -> IED model
  -> DataSet order
  -> GOOSE/SV publisher profiles
  -> future live-vs-engineering validation
```

Repository layout:

```text
src/AR.Iec61850/                      reusable protocol stack
src/AR.Iec61850.Transports.Npcap/     raw Ethernet adapter transport
apps/AR.Iec61850.Cli/                 lab and automation CLI
tests/AR.Iec61850.Tests/              unit and protocol tests
samples/scl/                          SCL fixtures
docs/                                 website, quick start, validation notes
ROADMAP.md                            technical direction and next phases
AGENTS.md                             engineering rules for contributors
```

## Roadmap Summary

Next engineering phases:

1. run receive-pump/report-monitor soak tests while reads/writes occur;
2. mature report evidence, post-write readback, and BRCB recovery;
3. implement MMS file transfer;
4. implement IEC 61850 control services safely;
5. add GOOSE and SV subscribers;
6. add MMS server and IED simulator;
7. build the WPF engineering workbench only after the stack APIs are stable.

The detailed plan is in [ROADMAP.md](ROADMAP.md).

## Good First Commands

For new users:

```powershell
dotnet test .\ARIEC61850.slnx -c Release
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap
```

For lab users with an IED:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.168.1.10 --port 102 --max-report-probes 16
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.168.1.10 --show-points --raw-limit 40
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.168.1.10 --max-report-probes 64 --only-safe
```

## GitHub Repository Metadata

Suggested description:

```text
Clean-room IEC 61850 native .NET stack for MMS, Reporting, GOOSE, Sampled Values, SCL, PCAP validation, and engineering tools.
```

Suggested topics:

```text
iec61850, iec-61850, mms, goose, sampled-values, sv, scl, substation-automation,
scada, process-bus, protection-relay, protocol-analyzer, commissioning,
fat-testing, sat-testing, dotnet, csharp, npcap, pcap, clean-room
```

## Documentation

- [Quick Start](docs/QUICK_START.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Validation](docs/VALIDATION.md)
- [Professional Use](docs/PROFESSIONAL_USE.md)
- [FAQ](docs/FAQ.md)
- [Repository Setup](docs/REPOSITORY_SETUP.md)
- [Roadmap](ROADMAP.md)

## License

No open-source license has been declared yet. Add a license before distributing
this stack as a public dependency or embedding it in downstream products.

## Evidence integrity milestone

Report live commands now perform post-write readback verification and export the verification artifacts when `--evidence <dir>` is used. The evidence folder includes `verification.json`, `rcb-snapshots.json`, and `dataset-snapshots.json` in addition to the report, poll, write-step, and summary files. The verification layer checks RCB state before enable, after enable, and after cleanup; dynamic sessions also verify dynamic DataSet creation, RCB.DatSet binding, DataSet restore/clear, and delete readback.

The evidence classifier now separates hard failures from relay-specific warning conditions. For example, a BRCB `ResvTms` lease timer that remains visible after `RptEna=false` is reported as `PASS_WITH_WARNING` when no explicit `Resv` flag is active. This captures relay ownership timeout behavior without mislabeling a successful cleanup as failed. Diagnostics also classify buffer overflow, sequence/EntryID heuristics, duplicate keys, and partial mappings as warning evidence instead of hiding them in raw counters.


### Report forensic timeline evidence

Guarded report evidence now includes `report-timeline.json` and a Report Timeline section in `summary.md`. The timeline flattens each report into received time, RptID, DataSet, ConfRev, SqNum, EntryID, BufOvfl, included indexes, mapped count, reason summary, and decoded TimeOfEntry. Sequence diagnostics now distinguish reset-to-zero events from true regressions, while EntryID numeric gaps remain heuristic warnings because EntryID is treated as opaque by default. MMS `binary-time` is decoded to UTC/time-of-day when possible while retaining the original raw hex.

### Long-run report soak evidence

`mms-report-monitor` supports longer runtime validation with periodic smart-read polling, optional periodic GI, and periodic soak snapshots:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.16.1.157 --port 102 --timeout-ms 900000 --duration-sec 600 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --poll-interval-ms 1000 --gi-interval-sec 120 --soak-snapshot-sec 60 --evidence out/report-soak-10m --yes
```

The evidence bundle includes `soak-snapshots.json` and a **Soak Snapshots** table in `summary.md`. This is intended to validate that the receive pump can keep routing unsolicited reports while confirmed smart-read polling remains active.

### Exact InformationReport decoder and report frame evidence

Report evidence now includes `report-frames.json`, `report-streams.json`, and `report-values.csv` in addition to `report-timeline.json`. The mapper first attempts an OptFlds-driven IEC 61850 report decode before falling back to the legacy inclusion-bitstring scan. Each report frame records `DecoderMode`, stream key (`RptID + DataSet + ConfRev`), parse warnings, optional-field bits/raw value, included indexes, reasons, and member-value mapping. The CSV is intended for quick FAT/SAT review in spreadsheet tools.


### Smart RCB Selection

Report commands now treat `--rcb` as a preferred RCB by default. If the preferred RCB is busy, reserved, or unsafe, ARIEC61850 can select a compatible fallback RCB instead of fighting another client.

Use `--strict-rcb` when the goal is to test one exact RCB and fail if it is not available.

Evidence output includes:

- `rcb-candidates.json`
- `rcb-selection.json`
- `rcb-claim-attempts.json`

This is intended to avoid unsafe RCB contention in real substations where SCADA, gateways, or other tools may already be using the first BRCB/URCB instance.

### Smart RCB claim fallback

The report monitor treats `--rcb` as a preferred candidate unless `--strict-rcb` is used. If a selected RCB looks free during readback but rejects the live claim (`RptEna=true` or dynamic `DatSet` bind), ARIEC61850 marks that RCB as a failed claim, excludes it from the current command, and tries the next safe candidate. Evidence is written to `rcb-claim-attempts.json` so the skipped/failed/selected chain is auditable.

### Smart RCB pre-claim contention guard

ARIEC61850 now treats RCBs as a pool of exclusive resources rather than a fixed `brcbA01` target. The monitor can probe the selected RCB several times before writing `RptEna` or binding a DataSet. If the RCB flips state or becomes busy/reserved during the probe window, the command records the condition in `rcb-contention-probes.json`, skips that candidate for the current command, and tries the next safe RCB.

Example:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.16.1.157 --rcb-probe-count 3 --rcb-probe-delay-ms 1000 --contention-cooldown-sec 60 --evidence out/report-smart-rcb-contention --yes
```

### Live IED model discovery and future SCL export

ARIEC61850 is moving toward a tool-class workflow: connect to a live IED, discover its IEC 61850 model, reconstruct a canonical model, export a generic IID/CID-style SCL snapshot, re-import that SCL for client connection, and later use the same SCL as an MMS server/simulator seed.

The first implementation phase is read-only:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-model-discover 192.16.1.157 --port 102 --timeout-ms 120000 --max-report-probes 286 --read-datasets true --ied-name OCR7SR12 --output out/ied-model-discovery
```

The command writes `ied-model.json`, `discovery-summary.md`, `type-confidence-report.json`, `datasets.json`, `rcb-inventory.json`, and `control-block-inventory.json`. FC is stored as observed evidence; CDC and future DataTypeTemplates are reconstructed with confidence scoring rather than claimed as vendor-original templates.


## N5.2 MMS VariableAccessAttributes Type Reader

Live IED model discovery now includes a bounded MMS `GetVariableAccessAttributes` pass. The generated discovery bundle records exact MMS type evidence where the IED supports it, while CDC and SCL `DataTypeTemplates` remain reconstructed with explicit confidence. This strengthens the Live-to-SCL path without pretending to recover vendor-original engineering templates.

Key outputs: `variable-access-attributes.json`, richer `ied-model.json`, and MMS type coverage in `discovery-summary.md`.


## N5.3/N5.5 Live-to-SCL Generic Exporter

ARIEC61850 can now convert live read-only MMS discovery into a generic IID/CID-style SCL connection snapshot. The generated SCL is not the original vendor ICD; it is a reconstructed, importable engineering snapshot containing Communication, IED tree, DataSet, ReportControl, and generic DataTypeTemplates based on FC evidence, MMS type discovery, and CDC inference.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-scl-export 192.16.1.157 --port 102 --timeout-ms 120000 --max-report-probes 286 --read-datasets true --read-types true --max-type-reads 512 --type-read-source both --ied-name OCR7SR12 --ap-name AP1 --profile connection --ld-name-mode auto --output out/scl/OCR7SR12.generated.iid
```

The command writes the SCL file plus `*.scl-export-report.json`, `*.scl-export-summary.md`, and an optional `discovery-evidence/` bundle. The CLI also runs a round-trip parser check so the generated SCL can immediately feed ARIEC61850 client workflows. Generated `DOType cdc` values are restricted to known IEC 61850 CDC names; internal labels such as `GEN`, `Status`, `Controllable`, `Setting`, and `Measurement` are rejected.

### Full SCL discovery inventory

`mms-model-discover` and `mms-scl-export` now include a structured control-block inventory for SCL-oriented discovery. GO/SV/SG/LG functional-constraint attributes, plus relay variants such as `LLN0.SP.SGCB`, are grouped into candidate `GSEControl`, `SampledValueControl`, `SettingControl`, and `LogControl` entries. The discovery bundle writes `control-block-inventory.json` and the SCL exporter can emit conservative control-block shells with warnings when exact DatSet/address/ID/timing values have not been read yet.

Edition 1 export is intentionally deferred. The current focus is a complete live discovery model that can feed Edition 2 / 2.1-ready IID/CID generation, connection reuse, and eventually SCL-backed simulation.

### IEDScout-clean SCL export profile

`mms-scl-export` now defaults to an IEDScout-friendly connection profile.  The generated SCL keeps LD/LN, DataSet, ReportControl, and safe DataTypeTemplates, while control service parameters and optional unproven configuration attributes are moved to `*.scl-excluded-attributes.json`.

```powershell
 dotnet run --project .\apps\AR.Iec61850.Cli -- mms-scl-export 192.16.1.157 --port 102 --ied-name OCR7SR12 --ap-name AP1 --scl-export-profile iedscout-connection --output out/scl/OCR7SR12.generated.iid
```

Use `--scl-export-profile full-model` for a broader audit model, or `--scl-export-profile simulator-seed` when preparing a future ARIEC61850 server/simulator seed.

### Standard-discovery SCL profile

For full online model-discovery progress, use the standard-discovery/full-model profile instead of the IEDScout connection-clean profile:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-scl-export 192.16.1.157 --port 102 --ied-name OCR7SR12 --ap-name AP1 --scl-export-profile standard-discovery --ld-name-mode auto --read-datasets true --read-types false --output out/scl/OCR7SR12.standard-discovery.iid
```

This profile intentionally keeps more live-discovered structure. It may be larger than an IEDScout-saved IID because ARIEC61850 preserves generated templates and evidence-oriented structure rather than minimizing the file. Ed2 enum CDCs such as `ENS` are exported with generated `EnumType` definitions instead of plain integer `stVal` leaves.


## N5.12 — Golden-reference diff and service discovery coverage

This version adds `scl-diff` for comparing ARIEC61850-generated IID/SCL files against a trusted golden export such as IEDScout, and `mms-service-discover` for producing an online IEC 61850 service coverage bundle. The goal is to measure structural gaps explicitly instead of guessing from IEDScout warning messages.


### N5.14 — Setting Group Deep Discovery + SG/SE Setting Map

- `mms-service-discover` now emits `setting-group-map.json` and `setting-group-map.md`.
- SGCB core readback is classified separately from SG/SE setting attribute mapping.
- Optional `--read-setting-values true` performs bounded, read-only SG/SE setting value reads with `--max-setting-reads` and `--setting-read-delay-ms`.
- The service coverage report can now distinguish `Core readback complete`, `SG/SE map`, and readback evidence instead of treating setting groups as a single placeholder.


### N5.15 — Safe Variable Specification Probe

The `mms-service-discover` command now records a dedicated safe variable specification probe evidence bundle (`safe-variable-spec-probe.json` and `.md`). The probe is dataset-first/leaf-only by default, skips control-service/optional structures that are risky on some IEDs, records skipped candidate reasons, and reports whether probing stopped early due to a suspected protocol/transport fault. This moves variable specification coverage from a binary attempted/not-attempted status into an evidence-grade, batch-expandable workflow.
