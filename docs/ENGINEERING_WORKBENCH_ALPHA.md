# Engineering Workbench Alpha

The Engineering Workbench is a read-only WPF harness for validating the engine in a workflow that looks closer to real FAT/SAT engineering work. It is not a final product application and does not contain protocol logic. It orchestrates the engine profiles from `src` and renders their output in compact tables.

## Purpose

The workbench validates the public-alpha engine path from a user-facing workflow:

```text
SCL file
→ static engineering profile
→ expected GOOSE/SV/report model
→ optional PCAP observation
→ expected-vs-observed process-bus binding
→ GOOSE diagnostics
→ SV diagnostics
→ read-only MMS loopback alpha
→ structured evidence pack export
```

## Run

```powershell
dotnet run --project .\apps\AR.Iec61850.EngineeringWorkbench
```

The app defaults to `samples/scl/minimal-station.scd` when it can locate the repository root. A PCAP is optional. Without a PCAP, the workbench still runs static SCL analysis and MMS loopback; process-bus diagnostics will show missing expected streams when the SCL declares GOOSE/SV publishers.

## What it exposes

- SCL engineering profile: IED, LD, LN, DataSet, report, GOOSE, SV, and ExtRef summary.
- Process-bus binding profile: expected vs observed GOOSE/SV stream matching.
- GOOSE diagnostics profile: APPID/MAC/VLAN/confRev, stNum/sqNum, duplicates, gaps, and supervision issues.
- SV diagnostics profile: APPID/MAC/VLAN/confRev, smpCnt continuity, sample synchronization, payload and ASDU checks.
- MMS read-only loopback profile: server model, association path, BER dispatch, and write guard readiness.
- Evidence center: structured pack export with `README.md`, `manifest.json`, profile Markdown/JSON, file sizes, and SHA-256 hashes.

## Safety posture

The workbench is read-only. It does not perform live write/control operations against external IEDs. Live control, command, and write workflows remain future milestones and must stay behind explicit safety gates.

## Public-alpha boundary

This app is a harness for proving engine usability. Final product applications should remain separate repositories or separate app projects and consume stable engine APIs rather than duplicating protocol logic.


## Evidence pack milestone

N5.39 adds `EngineeringWorkbenchEvidencePackBuilder`, shared by CLI and WPF. The WPF `Export pack` action now writes a review folder instead of loose profile files. The same flow can be tested headlessly with `workbench-evidence-pack`, making the desktop workflow reproducible in CI or terminal validation.
