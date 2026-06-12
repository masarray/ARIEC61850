# ARIEC61850 Roadmap

Last updated: 2026-06-12

This file is the single source of truth for the technical direction of
`ARIEC61850`. If a feature, task, or product idea conflicts with this roadmap,
this roadmap wins until it is deliberately changed.

## 1. North Star

Build a clean-room, reusable, field-useful IEC 61850 native stack for .NET and
then build commissioning tools on top of it.

The stack must become useful for:

- MMS client discovery, read, write, reporting, control, file and log services.
- MMS server and IED simulation.
- GOOSE publisher and subscriber.
- Sampled Values publisher and subscriber.
- SCL-driven station validation.
- CLI, WPF, and future automation tools for FAT, SAT, lab, troubleshooting, and
  protocol education.

The product goal is not to clone an existing tool. The product goal is to build
an original, clean-room engineering instrument in the same problem class as
professional IEC 61850 tools: IED exploration, SCL validation, report testing,
GOOSE/SV analysis, simulation, and repeatable evidence generation.

## 2. Core Product Doctrine

### 2.1 The stack must be easier to use than raw IEC 61850 APIs

IEC 61850 is strongly typed and model driven. A raw client often requires the
caller to know the logical device, logical node, data object, data attribute,
functional constraint, MMS variable naming format, DataSet structure, RCB
ownership, and control model before a useful read or report can happen.

`ARIEC61850` must hide that friction behind a smart model layer.

Target user-facing behavior:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
```

The user should not have to provide `MX` manually. The stack must discover the
correct functional constraint from the live IED model and cache the result.

### 2.2 Live IED directory first, SCL second, heuristics last

The primary source of truth for online workflows is the live IED self-description
obtained through MMS services.

Priority order:

1. Live MMS model directory from the IED.
2. Live DataSet directory and DataSet member information.
3. Live RCB attributes and runtime state.
4. SCL/CID/ICD/SCD engineering file.
5. Cached previous successful resolution.
6. Explicit, bounded heuristic fallback.

SCL is critical for engineering validation, but a commissioning client must not
fail just because the user does not have a perfect CID file. The live IED model
must be enough for basic browse/read/report workflows.

### 2.3 Directory before polling, model before UI

The application workflow becomes lighter when the stack first builds a complete
IED directory. Do not make the UI repeatedly guess FC, raw MMS path, RCB state,
or DataSet order.

Correct workflow:

```text
Connect
  -> MMS association
  -> full domain/name directory
  -> FC-aware IED model index
  -> DataSet directory and member index
  -> RCB inventory and readiness classification
  -> smart read/report/control workflows
```

Wrong workflow:

```text
User clicks one item
  -> app tries random FC values
  -> app reads partial model repeatedly
  -> report workflow guesses DataSet order
  -> UI becomes slow and unreliable
```

### 2.4 Reporting must be a state machine, not a button

A report workflow is not just `RptEna=true`. A usable client must understand:

- RCB type: URCB/RP or BRCB/BR.
- Static versus configurable/dynamic DataSet binding.
- `RptEna`, `Resv`, `ResvTms`, and owner behavior.
- `DatSet`, `RptID`, `ConfRev`, `OptFlds`, `TrgOps`, `BufTm`, `IntgPd`.
- `GI`, `PurgeBuf`, `EntryID`, `TimeOfEntry`, `SqNum`, `BufOvfl`.
- Report arrival as unconfirmed/unsolicited MMS PDU.
- Report item mapping by DataSet member order.
- Sequence, buffer, overflow, mismatch, and recovery diagnostics.

## 3. Standard Scope and Reference Anchors

This roadmap is based on public standard descriptions, public documentation,
black-box interoperability behavior, and original implementation work. Do not
copy restrictive-license or proprietary implementation code.

Main IEC 61850 areas that define the stack direction:

- IEC 61850-6: SCL configuration description language.
- IEC 61850-7-2: ACSI services such as client/server communication, reporting,
  logging, control, setting group, and self-description.
- IEC 61850-7-3: common data classes and attribute typing.
- IEC 61850-7-4: logical nodes and data object semantics.
- IEC 61850-8-1: mapping of ACSI to MMS and Ethernet frames.
- IEC 61850-9-2 / related sampled value profiles: Sampled Values payloads and
  stream behavior.
- UCA/IUG interoperability practice, TISSUE learnings, vendor variations, and
  black-box lab testing with real IEDs.

Public capability references may be used for planning:

- third-party IEC 61850 stack documentation: protocol capability reference only. restrictive-license code must
  not be copied, translated, or structurally ported.
- Vendor tools such as IEDScout, StationScout, SVScout, and relay engineering
  tools: workflow inspiration only, not UI or code templates.
- Wireshark decoded PCAPs: interoperability evidence and byte-level inspection.

## 4. Current Repository Evidence

Current status observed in this repository and the latest lab run:

- .NET stack scaffold exists.
- ASN.1 BER reader/writer exists.
- MMS data value codec exists for common values.
- Ethernet/VLAN/process-bus frame codecs exist.
- GOOSE frame builder/parser exists.
- Sampled Values frame builder/parser exists.
- SCL parser exists for core objects needed by current publisher profiles.
- GOOSE/SV publisher profiles and sessions exist.
- PCAP reader/writer/stream monitor exists.
- Npcap transport exists for live process-bus publishing.
- CLI exists.
- Native MMS client can establish TCP/TPKT/COTP/ACSE/MMS association with a lab
  IED.
- Native MMS discovery can call `GetNameList` for domains, named variables, and
  named variable lists.
- Latest lab result discovered 4 logical devices, 10,122 raw variables, 1
  DataSet, and 286 RCBs on a real IED.
- RCB inventory and bounded attribute probing exist for selected attributes.

Current MMS gaps:

- No full FC-aware IED directory model yet.
- No complete DataSet member directory yet.
- No robust `GetVariableAccessAttributes` / variable specification model yet.
- No `GetNamedVariableListAttributes` DataSet member read yet.
- No generic smart FC resolver yet.
- No confirmed-write service yet.
- No create/delete dynamic DataSet service yet.
- No RCB reservation/configuration state machine yet.
- No `RptEna` / `GI` write path yet.
- No asynchronous receive pump for unconfirmed `InformationReport` yet.
- No report decoder and DataSet-member mapping yet.
- No BRCB recovery using `EntryID` yet.
- No control model abstraction yet.
- No server simulator yet.

## 5. Final Project Structure

The current repository can continue with the existing projects while the stack is
still compact. Split projects only when module boundaries become stable. The
final mature structure should look like this:

```text
ARIEC61850/
  src/
    AR.Iec61850/                         current core package until split is needed
      Asn1/
      Ethernet/
      Goose/
      Mms/
      Model/
      Reporting/
      SampledValues/
      Scl/
      Transports/

    AR.Iec61850.Core/                    future split: primitives, BER, common types
    AR.Iec61850.Mms/                     future split: MMS/ACSE/client/server services
    AR.Iec61850.Model/                   future split: IED directory, FC resolver, SCL model
    AR.Iec61850.Reporting/               future split: RCB state machine and report decoder
    AR.Iec61850.ProcessBus/              future split: GOOSE and SV services
    AR.Iec61850.Server/                  future split: MMS server and simulator model
    AR.Iec61850.Transports.Npcap/         raw Ethernet adapter
    AR.Iec61850.Transports.Pcap/          PCAP replay/capture transport if needed
    AR.Iec61850.TestKit/                 fixtures, golden bytes, fake IED, interop harness

  apps/
    AR.Iec61850.Cli/                     automation, smoke tests, lab commands
    AR.Iec61850.Workbench.Wpf/           future engineering workbench
    AR.Iec61850.Simulator.Cli/           future headless simulator if needed

  tests/
    AR.Iec61850.Tests/                   unit and golden tests
    AR.Iec61850.InterOpTests/            opt-in tests against simulators/real IEDs
    AR.Iec61850.LongRunTests/            opt-in stability and report soak tests

  samples/
    scl/
    pcap/
    reports/
    scripts/

  docs/
    architecture/
    validation/
    interop/
    ux/
    protocol-notes/
```

Split rule:

```text
Do not split early just to look professional.
Split when a boundary has stable public types, tests, and no circular dependency.
```

## 6. Layered Architecture

### Layer 0 - Byte and Type Primitives

Purpose: deterministic building blocks.

Includes:

- BER TLV reader/writer.
- MMS data value codec.
- Ethernet/VLAN/process-bus frame codec.
- MAC, APPID, VLAN, quality, timestamp, object reference, bit-string helpers.

Rules:

- No network IO.
- No UI.
- No SCL assumptions.
- Every codec needs golden byte tests and malformed input tests.

### Layer 1 - OSI/MMS Transport Foundation

Purpose: reliable native MMS association.

Includes:

- TCP socket handling.
- TPKT.
- COTP connection and data TPDU.
- ISO session.
- ISO presentation.
- ACSE associate/release/abort.
- MMS initiate.
- confirmed request/response envelope.
- invoke ID handling.

Rules:

- Do not skip layers to make a demo work.
- Expose negotiated parameters and trace events.
- Support timeout, cancellation, reconnect, release, and abort diagnostics.
- Confirmed responses must be matched by invoke ID.

### Layer 2 - Live IED Directory Engine

Purpose: make the IED self-describing and easy to use.

Includes:

- Domain discovery.
- Named variable discovery.
- Named variable list discovery.
- FC extraction from MMS names such as `LN$ST$DO$DA`.
- Logical device/logical node/data object/data attribute tree.
- `GetVariableAccessAttributes` / variable specification when implemented.
- `GetNamedVariableListAttributes` for DataSet member directory.
- Data type hints and value kind mapping.
- confidence/source tracking: LiveMms, DataSet, Scl, Cached, Heuristic.

Output type direction:

```csharp
public sealed class IedModelIndex
{
    public IReadOnlyDictionary<string, LogicalDeviceIndex> LogicalDevices { get; init; }
    public IReadOnlyDictionary<string, FcResolvedPoint> PointsByUserReference { get; init; }
    public IReadOnlyDictionary<string, FcResolvedPoint> PointsByMmsReference { get; init; }
    public IReadOnlyDictionary<string, DataSetDirectory> DataSets { get; init; }
    public IReadOnlyDictionary<string, ReportControlDirectory> ReportControls { get; init; }
}
```

Done means:

```text
After connect, the stack can show a stable FC-aware IED tree without asking the
user to know ST/MX/CO/CF/RP/BR.
```

### Layer 3 - Smart FC Resolver

Purpose: remove lib-style FC friction from user workflows.

Includes:

- exact live MMS match.
- normalized user reference match.
- SCL match.
- DataSet member match.
- cached successful read match.
- bounded heuristic fallback.
- ambiguity reporting.
- controlled trial read only as last resort.

Target API:

```csharp
await client.ReadSmartAsync("OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f");
await client.ResolveAsync("OCR7SR12CTRL/XCBR1.Pos");
```

Rules:

- Never brute-force control/write FC values.
- Never hide ambiguity.
- Cache proven resolutions.
- Show the source and confidence of every resolved FC.

### Layer 4 - MMS Client Service Surface

Purpose: expose ACSI-like client functions with friendly diagnostics.

Includes:

- connect/release/abort.
- model directory.
- smart read.
- explicit read by resolved MMS reference.
- confirmed write.
- DataSet directory.
- create/delete dynamic DataSet.
- RCB read/configure.
- log/file/setting group later.
- control services later.

Rules:

- Read-only services first.
- Write services must be explicit and traceable.
- CLI and UI must show exact target and risk before writes.

### Layer 5 - Reporting Engine

Purpose: full buffered/unbuffered report operation.

Includes:

- RCB directory.
- RCB readiness classification.
- reservation state machine.
- configurable/static/dynamic DataSet handling.
- RCB write sequence.
- enable/disable.
- GI trigger.
- async receive pump.
- unconfirmed `InformationReport` decode.
- DataSet member mapping.
- reason-for-inclusion mapping.
- sequence and buffer diagnostics.
- BRCB `EntryID` recovery.

RCB readiness states:

```text
Unknown
EmptyDynamicSlot
StaticBoundAvailable
EnabledByOtherClient
ReservedByOtherClient
AvailableForReservation
ReservedByMe
EnabledByMe
AccessDenied
ConfigurationRejected
Unsupported
```

Done means:

```text
The stack can select a free usable RCB, configure it safely, enable it, trigger
GI, receive reports, map values to DataSet members, and explain state/sequence
problems.
```

### Layer 6 - Control Model

Purpose: safe operation of controllable objects.

Includes:

- `ctlModel` discovery.
- direct operate.
- select-before-operate.
- enhanced security control flow.
- `Oper`, `SBO`, `Cancel` path handling.
- origin, check, test, interlock/synchrocheck fields.
- command termination/report correlation when possible.

Rules:

- Never treat control as a generic write.
- Control must require explicit API and UI confirmation.
- Control defaults to disabled in generic tools unless user enables lab mode.

### Layer 7 - Process Bus Engine

Purpose: mature GOOSE/SV publisher/subscriber capabilities.

Includes:

- GOOSE publisher and subscriber.
- SV publisher and subscriber.
- SCL semantic binding.
- TTL/sequence supervision.
- stream health.
- PCAP replay and evidence export.
- waveform/phasor helper services as optional analysis modules.

Rules:

- In-memory first.
- PCAP replay before live NIC when practical.
- Npcap timing is lab/screening grade unless proven otherwise.

### Layer 8 - MMS Server and IED Simulator

Purpose: repeatable station testing and development.

Includes:

- SCL-backed IED model.
- read/write behavior.
- DataSet service.
- RCB behavior.
- reporting behavior.
- control behavior in safe simulation mode.
- GOOSE/SV integration.
- scenario scripts.

Rules:

- Simulation state must be deterministic and inspectable.
- Unsafe control behavior is disabled by default.
- Simulator must be useful for automated tests before UI polish.

### Layer 9 - Tester Applications

Purpose: product workflows built on stack APIs.

Applications:

- CLI: smoke tests, automation, protocol diagnostics.
- WPF Workbench: station/MMS/GOOSE/SV/capture/report workbench.
- Future headless simulator service if needed.

WPF workspaces:

- Station: SCL import, live-vs-SCL comparison, topology, validation.
- MMS Client: connect, browse, smart read/write, report wizard, report monitor.
- MMS Server: simulate IED and scenarios.
- GOOSE: subscribe, inspect, publish, replay.
- Sampled Values: subscribe, waveform/phasor, publish, replay.
- Capture: PCAP scan/replay/evidence.
- Reports: commissioning evidence and mismatch reports.

## 7. Milestone Roadmap

### M0 - Clean Stack Seed

Status: implemented.

Done:

- .NET solution and library scaffold.
- BER reader/writer.
- MMS common data value codec.
- Ethernet/VLAN/process-bus codec.
- GOOSE frame builder/parser.
- SV frame builder/parser.
- Unit tests for BER, MMS data, GOOSE, and SV round-trips.

### M1 - SCL Core

Status: first usable pass implemented.

Done:

- SCL load support for sample engineering files.
- IED, DataSet, GSEControl, SampledValueControl, ReportControl extraction.
- DataSet order preservation.
- Basic conflict/warning model.
- SCL-backed GOOSE/SV publisher profiles.

Remaining:

- more vendor fixtures.
- fuller DataTypeTemplates resolution.
- multi-file engineering context.
- richer SCL-vs-live validation.

### M2 - Process-Bus Publish MVP

Status: first usable pass implemented.

Done:

- in-memory publisher sessions.
- PCAP generation and inspection.
- Npcap live SV publish smoke.
- Npcap live GOOSE publish smoke.
- GOOSE retransmission behavior started.

Remaining:

- GOOSE subscriber.
- SV subscriber.
- typed SV engineering-value packing.
- long-run sequence/timing validation.

### M3 - MMS Association and Discovery MVP

Status: first usable pass implemented.

Done:

- TCP/TPKT/COTP/ACSE/MMS association.
- MMS initiate handling.
- `GetNameList` for domain/named variable/named variable list.
- DataSet inventory by named variable list.
- RCB inventory by `RP`/`BR` names.
- bounded RCB attribute probing.
- CLI command: `mms-discover`.

Done evidence:

```text
mms-discover against lab IED reached MMS initiated state and discovered 4 LDs,
10,122 raw variables, 1 DataSet, and 286 RCBs.
```

### M4 - Full Live IED Directory and Smart FC Resolver

Status: next priority.

Goal: make the IED browse/read workflow light and user-friendly.

Deliverables:

- `IedModelIndex`.
- `FunctionalConstraint` enum and parser.
- MMS variable name parser: `LN$FC$DO$DA$BDA`.
- user reference normalizer: `LD/LN.DO.da.bda`.
- live FC-aware tree builder.
- `mms-model` CLI command.
- `mms-resolve` CLI command.
- `mms-read` CLI command that does not require user-provided FC.
- confidence/source labels for LiveMms, Scl, DataSet, Cached, Heuristic.
- ambiguity diagnostics.
- cache proven resolutions during session.

Done means:

```text
A user can browse and read values without manually entering ST/MX/CO/CF/RP/BR,
and the stack can explain how it resolved each reference.
```

Validation:

- unit tests for MMS variable name parsing.
- unit tests for reference normalization.
- tests for ambiguous DO references.
- tests for ST/MX/CO/CF/RP/BR extraction.
- lab validation against at least one real IED and one simulator.

### M5 - DataSet Directory and Dynamic DataSet Services

Goal: know exactly what every DataSet contains and prepare report binding.

Deliverables:

- `GetNamedVariableListAttributes` request/response.
- DataSet member directory with order preserved.
- member mapping to FC-resolved points.
- DataSet value read.
- capability detection for create/delete DataSet.
- `CreateNamedVariableList` and `DeleteNamedVariableList` later.
- CLI commands:
  - `mms-datasets`
  - `mms-dataset-members`
  - `mms-create-dataset` guarded and lab-mode only at first
  - `mms-delete-dataset` guarded and lab-mode only at first

Done means:

```text
The stack can list DataSet members with FC and read/report mapping order without
requiring SCL.
```

### M6 - Confirmed Write Foundation

Goal: enable safe configuration of MMS objects and RCBs.

Deliverables:

- confirmed-write PDU builder.
- write response decoder.
- type-aware value encoder for booleans, integers, unsigned, bit strings,
  visible strings, octet strings, UTC time, structures, and arrays where needed.
- write diagnostics with access result.
- explicit `WritePlan` object before executing writes.
- CLI command `mms-write` in guarded mode.

Rules:

- No hidden writes during discovery.
- No trial writes.
- No control writes through generic write workflow.
- Every write must identify target, resolved FC, raw MMS path, value type, and
  expected risk.

### M7 - RCB Readiness and Report Configuration

Goal: classify RCBs and configure only safe candidates.

Deliverables:

- full RCB attribute read.
- URCB/BRCB model.
- static-bound versus empty dynamic slot classification.
- owner/reservation detection.
- `Resv` and `ResvTms` handling.
- `RptEna=false` precondition checks.
- write sequence for RCB settings.
- `OptFlds`, `TrgOps`, `BufTm`, `IntgPd`, `RptID`, `DatSet` handling.
- CLI command `mms-rcb-list`.
- CLI command `mms-rcb-plan`.
- CLI command `mms-report-enable` guarded.
- CLI command `mms-report-disable` guarded.

Done means:

```text
The stack can tell which RCB is safe to use, which is occupied by another
client, which is an empty dynamic slot, and why.
```

### M8 - Async Receive Pump and InformationReport Decoder

Goal: receive reports correctly while other confirmed requests are in flight.

Deliverables:

- one network reader loop per MMS association.
- confirmed response router by invoke ID.
- unconfirmed PDU dispatcher.
- `InformationReport` decoder.
- report object model.
- DataSet member-to-value mapping.
- reason-for-inclusion decode.
- report handler subscription API.
- CLI command `mms-report-monitor`.

Done means:

```text
The client can keep a report subscription alive and decode unsolicited reports
without corrupting confirmed request/response handling.
```

### M9 - Buffered Report Recovery

Goal: make BRCB useful for real commissioning.

Deliverables:

- `EntryID` read/write support where applicable.
- `PurgeBuf` support.
- `BufOvfl` diagnostics.
- `SqNum` jump detection.
- reconnect/re-enable strategy.
- duplicate report handling.
- last-seen report state persistence in session.

Done means:

```text
The stack can reconnect to a BRCB and explain what was recovered, duplicated,
or lost.
```

### M10 - Control Model

Goal: safe control testing.

Deliverables:

- discover controllable objects.
- read `ctlModel` and related attributes.
- direct operate.
- SBO.
- enhanced security flow.
- command termination/report correlation.
- guardrails and confirmation UX.

Done means:

```text
The stack can perform controlled operations in a lab with explicit safety,
traceability, and diagnostics.
```

### M11 - GOOSE/SV Subscriber Maturity

Goal: complete process-bus receive side.

Deliverables:

- GOOSE subscriber with TTL, stNum/sqNum supervision, DataSet decode.
- SV subscriber with stream identity, sample counter supervision, channel mapping.
- SCL binding and live-vs-SCL mismatch diagnostics.
- PCAP replay fixtures.
- CLI monitor commands.

Done means:

```text
The stack can subscribe to GOOSE/SV traffic and produce station-useful evidence,
not only raw frame dumps.
```

### M12 - MMS Server and IED Simulator

Goal: enable repeatable testing without hardware.

Deliverables:

- SCL-backed server model.
- read/write support.
- DataSet service.
- report service.
- control simulation.
- GOOSE/SV integration.
- scenario scripts.
- interop tests against the stack client.

Done means:

```text
The stack can run a deterministic IED simulator useful for automated tests and
engineering demonstrations.
```

### M13 - WPF Engineering Workbench

Goal: productize the stack.

Deliverables:

- connection/session manager.
- station/SCL import.
- MMS model browser.
- smart read/write panel.
- report wizard and monitor.
- GOOSE/SV monitor.
- PCAP replay/evidence.
- exportable test report.

Done means:

```text
A commissioning engineer can use the app without reading code or understanding
raw MMS naming rules.
```

### M14 - Interoperability and Release Hardening

Goal: make releases credible.

Deliverables:

- hardware matrix.
- simulator matrix.
- PCAP golden corpus.
- long-run report soak test.
- malformed packet tests.
- documented limitations.
- public release notes.

Done means:

```text
The repository can publish tagged releases with a defensible validation story.
```

## 8. Current Next Patch Order

Do these in order. Do not jump to WPF before these stack APIs exist.

### Patch 1 - Live MMS Model Index

Implement:

- `FunctionalConstraint` enum.
- `MmsVariableName` parser.
- `Iec61850UserReference` normalizer.
- `IedModelIndexBuilder` from current `GetNameList` results.
- CLI `mms-model` to print LD/LN/FC tree summary.

Acceptance:

```text
The lab IED result can be converted from 10,122 raw variable names into a
logical FC-aware tree.
```

### Patch 2 - Smart FC Resolver and Smart Read

Implement:

- `FcResolver`.
- `ResolveAsync`.
- `ReadSmartAsync`.
- CLI `mms-resolve`.
- CLI `mms-read`.

Acceptance:

```text
User can read ST/MX/DC/RP/BR attributes without manually entering FC.
Ambiguous references return candidates, not random failure.
```

### Patch 3 - DataSet Member Directory

Implement:

- `GetNamedVariableListAttributes` request/response.
- `DataSetDirectory` model.
- member-to-FC-resolved point mapping.
- CLI `mms-dataset-members`.

Acceptance:

```text
The stack can list members of OCR7SR12PROT/LLN0.DataSet with order and resolved
FC source.
```

### Patch 4 - Full RCB Probe and Readiness Classification

Implement:

- full RCB attribute read.
- RCB readiness classifier.
- CLI `mms-rcb-list --classify`.

Acceptance:

```text
The 286 discovered RCBs are grouped into usable static-bound RCBs, empty dynamic
slots, occupied RCBs, access-denied RCBs, and unknown/partial RCBs.
```

### Patch 5 - Confirmed Write Foundation

Implement:

- write PDU builder/decoder.
- type-aware value encoder.
- guarded CLI `mms-write`.

Acceptance:

```text
A harmless writable test attribute or lab simulator attribute can be written and
verified by read-back.
```

### Patch 6 - Report Enable/GI MVP

Implement:

- RCB write sequence.
- report handler registration API placeholder.
- guarded `mms-report-enable`.
- `GI=true` trigger.

Acceptance:

```text
The stack can enable a safe free RCB in lab and trigger GI without taking over
an RCB already enabled/reserved by another client.
```

### Patch 7 - Async Report Receive

Implement:

- receive pump.
- confirmed response router.
- unconfirmed InformationReport decoder.
- CLI `mms-report-monitor`.

Acceptance:

```text
Report values appear with RptID, DataSet, SqNum, optional fields, and mapped
DataSet member names.
```

## 9. Validation Requirements

A feature is not done until it has:

- deterministic unit tests,
- at least one malformed input test,
- documented limitations,
- CLI or test harness usage,
- validation note under `docs/validation/` when hardware or interop is involved,
- clear evidence of what was tested and what remains unproven.

Validation levels:

1. Unit tests.
2. Golden byte tests.
3. Round-trip tests.
4. Negative/malformed tests.
5. PCAP replay tests.
6. Simulator interop tests.
7. Real IED lab tests.
8. Long-run stability tests.
9. Formal conformance path later.

Do not use the word `conformant` unless a formal conformance route exists. Use
`interop-tested`, `lab-validated`, or `screening-level` when that is the honest
status.

## 10. Release Definition

### Alpha

- Build and tests pass.
- CLI can inspect SCL, generate/inspect PCAP, publish bounded GOOSE/SV smoke,
  connect MMS, discover IED model, smart-read values.
- Known limitations documented.

### Beta

- Full live IED directory.
- Smart FC resolver.
- DataSet directory.
- RCB classification.
- Report enable/GI/monitor MVP.
- At least two simulator profiles and one real IED lab validation.

### Release Candidate

- Buffered and unbuffered reporting stable.
- Subscriber side GOOSE/SV stable.
- WPF workbench usable for real workflows.
- Interop matrix documented.
- Long-run report/subscriber tests completed.

### Public 1.0

- Clean-room license hygiene complete.
- API surface stable enough for users.
- Tagged release.
- User documentation and validation evidence complete.
- No marketing overclaim beyond proven capabilities.

## 11. Non-Negotiable Boundaries

- No restrictive-license implementation code may enter this repository.
- No UI logic in protocol stack.
- No network publishing without explicit adapter selection and confirmation.
- No write/control operation hidden inside discovery.
- No report enable on an occupied/reserved RCB unless the user explicitly forces
  it in lab mode.
- No brute-force control/write attempts.
- No claiming timing precision from normal Windows/Npcap behavior.
- No making a WPF screen before the stack API it needs exists.
- No deleting tests to make a build pass.
- No silently choosing one interpretation when SCL/live model conflicts.

## Current implementation checkpoint: report static/dynamic planning

The stack now validates the complete pre-reporting chain without writing to the IED:

1. Live MMS directory discovery builds FC-aware points from the IED model.
2. DataSet directory reads `GetNamedVariableListAttributes` and maps members to `LD/LN.DO.da [FC]`.
3. Report readiness classifies RCBs into static-ready, dynamic empty slot, occupied, reserved, or incomplete.
4. Static report planner selects a safe static RCB and binds it to a verified DataSet value map.
5. Dynamic report planner resolves user-selected points and selects a free RCB slot for a future dynamic DataSet.
6. MMS write and DefineNamedVariableList codec foundations exist, but live write workflows remain gated until the receive pump and cleanup state machine are implemented.

Next phase must implement the asynchronous MMS receive pump and InformationReport decoder before exposing any live `RptEna=true` command.
