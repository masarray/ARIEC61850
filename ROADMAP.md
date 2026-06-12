# ARIEC61850 Roadmap

Last updated: 2026-06-13

This file is the technical source of truth for `ARIEC61850`. Keep public claims
honest: implemented, lab-validated, partial, experimental, planned, or
unsupported.

## 1. North Star

Build a clean-room, reusable, field-useful IEC 61850 native stack for .NET, then
build engineering tools on top of it.

The mature stack must support:

- MMS client discovery, read, write, reporting, control, file transfer, logs,
  and setting groups.
- MMS server and IED simulator.
- GOOSE publisher and subscriber.
- Sampled Values publisher and subscriber.
- SCL-driven station validation.
- PCAP capture/replay/evidence workflows.
- CLI, WPF, and future automation tools for FAT, SAT, lab, troubleshooting,
  protocol education, and commissioning support.

The product goal is not to clone any existing tool. The product goal is an
original clean-room engineering instrument: easier to use than raw IEC 61850
APIs, strict about evidence, and safe around live writes/control/publishing.

## 2. Product Doctrine

### 2.1 Smart stack first

The stack must hide avoidable IEC 61850 friction:

- discover the live IED directory before asking the user to guess references;
- resolve Functional Constraints from evidence instead of requiring manual
  `ST`, `MX`, `CO`, `RP`, or `BR` input;
- preserve DataSet order exactly;
- classify RCB readiness before enabling reports;
- expose raw diagnostics when the stack is uncertain;
- return ambiguity candidates instead of silently picking a risky path.

Target user behavior:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
```

The user should not need to know that this resolves to `MX` and a raw MMS item
such as `MMXU1$MX$PhV$phsA$cVal$mag$f`.

### 2.2 Live model first, SCL second

Online workflows should trust evidence in this order:

1. live MMS model directory from the IED;
2. live DataSet directory and DataSet member order;
3. live RCB attributes and runtime state;
4. SCL/CID/ICD/SCD engineering file;
5. cached successful resolutions;
6. explicit bounded heuristic fallback.

SCL is essential for engineering validation, GOOSE/SV semantics, and station
consistency checks. It must enrich and verify the live model, not replace live
evidence.

### 2.3 Reporting is a state machine

Reporting is not a button. A safe report workflow must model:

- URCB/RP versus BRCB/BR;
- static versus configurable DataSet binding;
- `RptEna`, `Resv`, `ResvTms`, `Owner`, `DatSet`, `RptID`, `ConfRev`;
- `OptFlds`, `TrgOps`, `BufTm`, `IntgPd`, `GI`, `PurgeBuf`;
- `EntryID`, `SqNum`, `TimeOfEntry`, `BufOvfl`;
- unconfirmed `InformationReport` arrival;
- DataSet-member-to-value mapping;
- reason-for-inclusion and optional field decode;
- disable, cleanup, recovery, and conflict diagnostics.

### 2.4 Better than raw protocol libraries means better workflow

Mature public IEC 61850 stacks already cover broad protocol scope: MMS
client/server, GOOSE, SV, reports, DataSets, logs, file services, and setting
groups. `ARIEC61850` must catch up on protocol coverage, but the differentiator
should be workflow quality:

- .NET-first typed APIs;
- smart FC resolution;
- live/SCL conflict evidence;
- guardrails around report writes and control;
- PCAP and validation artifacts as first-class outputs;
- CLI and future WPF tools that are usable by commissioning engineers, not only
  protocol developers.

Clean-room rule: public capability descriptions may guide planning; no
restrictive-license implementation code may be copied, translated, or
structurally ported.

## 3. Standard Scope

The stack direction follows the public IEC 61850 architecture:

- IEC 61850-6: SCL configuration language.
- IEC 61850-7-2: ACSI services such as client/server, reporting, logging,
  control, setting groups, and self-description.
- IEC 61850-7-3: common data classes and attribute typing.
- IEC 61850-7-4: logical nodes and data object semantics.
- IEC 61850-8-1: MMS and GOOSE mapping.
- IEC 61850-9-2 and related profiles: Sampled Values mapping.
- IEC 61850-10 path later: formal conformance evidence, not current status.

## 4. Current Repository Evidence

Latest verified local status:

- .NET solution, library, CLI, tests, sample SCL, docs, and GitHub Pages surface
  exist.
- ASN.1 BER reader/writer exists.
- MMS data value codec exists for common values.
- Ethernet, VLAN, GOOSE, SV, and PCAP primitives exist.
- SCL parser extracts the core objects needed by current publisher profiles.
- GOOSE/SV publisher profiles and live Npcap publish sessions exist.
- Native MMS client can establish TCP/TPKT/COTP/ACSE/MMS association.
- Native MMS discovery can call `GetNameList` for domains, named variables, and
  named variable lists.
- Live IED model directory builds FC-aware points from raw MMS names.
- Smart FC resolver and smart read are validated against a live lab IED.
- DataSet directory reads MMS named variable list attributes and preserves
  member order.
- Confirmed write foundation exists and is used by guarded RCB/DataSet flows.
- Report inventory, static planning, dynamic planning, and readiness
  classification exist.
- Guarded static report enable/GI/receive/disable is validated against a lab
  BRCB.
- Guarded dynamic report create DataSet, bind RCB.DatSet, enable/GI/receive,
  cleanup, and delete DataSet is validated against a lab BRCB.
- MMS PDU envelope classification and receive routing now queues
  invoke-matched confirmed responses/errors separately from unconfirmed
  InformationReports.
- Report frame mapping now preserves raw access-result count, inclusion
  bitstring position, and included DataSet member indexes.
- Latest automated validation: `dotnet test .\ARIEC61850.slnx -c Release
  --no-build` passed with 66 tests.

Latest live MMS evidence against lab IED `192.16.1.157:102`:

```text
Association=MmsInitiated
logicalDevices=4
logicalNodes=123
FC-points=9464
reportAttrs=3456
controlAttrs=457
DataSets=1
RCB=286
BRCB=8
URCB=278
static report=enable, GI, receive, map 2/2 values, disable OK
dynamic report=create DataSet, bind, enable, GI, receive, map 2/2 values, cleanup OK
```

## 5. Feature Status Matrix

| Area | Status | Notes |
| --- | --- | --- |
| BER/MMS value codec | Implemented | Needs broader type corpus over time. |
| Ethernet/VLAN/GOOSE/SV codecs | Implemented | Round-trip tests exist. |
| SCL core parser | Partial | Enough for current publishers; needs richer DataTypeTemplates and multi-file context. |
| PCAP writer/reader/monitor | Implemented | Useful for offline validation. |
| GOOSE publisher | Lab MVP | Needs subscriber, more semantics, and longer validation. |
| SV publisher | Lab MVP | Windows/Npcap timing is screening-level only. |
| GOOSE subscriber | Planned | Needs TTL, stNum/sqNum supervision, SCL binding. |
| SV subscriber | Planned | Needs sample counter supervision and channel mapping. |
| MMS association/discovery | Lab MVP | Works against current IED; needs multi-vendor matrix. |
| Live IED directory | Lab MVP | FC-aware index exists; variable access attributes still incomplete. |
| Smart FC resolver/read | Lab MVP | Validated on live point; needs more ambiguity tests and vendors. |
| DataSet directory | Lab MVP | Live member order mapping exists. |
| Confirmed write foundation | Partial | Used for guarded report/DataSet flows; generic write API remains guarded. |
| Static reporting | Guarded lab MVP | Enable, GI, receive, map, disable validated. |
| Dynamic reporting | Guarded lab MVP | Create/bind/enable/GI/receive/cleanup/delete validated. |
| MMS receive routing | Partial | PDU classifier and invoke-aware queue are implemented; background pump is next. |
| Full MMS receive pump | Planned next | Needs one reader loop, pending operation tasks, and long-running monitor. |
| BRCB recovery | Planned | Needs EntryID, PurgeBuf, reconnect, duplicate/loss diagnostics. |
| MMS file transfer | Planned | Browse/get/set/delete/rename file services. |
| MMS log service | Planned | Needed for full ACSI coverage. |
| Setting group service | Planned | Needed for full ACSI coverage. |
| Control service | Planned | Must not be implemented as generic write. |
| MMS server/IED simulator | Planned | Required for repeatable interop tests and demos. |
| TLS/IEC 62351 | Planned later | Not part of current MVP. |
| Formal conformance | Not claimed | No formal conformance route yet. |

## 6. Architecture Direction

Keep the stack layered:

```text
TCP
  -> TPKT
  -> COTP
  -> ISO Session
  -> ISO Presentation
  -> ACSE
  -> MMS

Ethernet
  -> VLAN
  -> GOOSE / Sampled Values

SCL
  -> engineering model
  -> DataSet order
  -> report/control/process-bus validation
```

Boundaries:

- Codecs do not depend on transports.
- Stack projects do not depend on CLI, WPF, app settings, or UI state.
- Apps call stack services; apps must not parse protocol bytes directly.
- Transport projects depend on stack abstractions only.
- TestKit/interoperability helpers may be split later when boundaries stabilize.

Future structure, only when justified:

```text
src/AR.Iec61850.Core/
src/AR.Iec61850.Mms/
src/AR.Iec61850.Model/
src/AR.Iec61850.Reporting/
src/AR.Iec61850.ProcessBus/
src/AR.Iec61850.Server/
src/AR.Iec61850.Transports.Npcap/
src/AR.Iec61850.TestKit/
apps/AR.Iec61850.Cli/
apps/AR.Iec61850.Workbench.Wpf/
tests/AR.Iec61850.InterOpTests/
tests/AR.Iec61850.LongRunTests/
```

Do not split projects just to look mature. Split only when public types and
dependencies are stable.

## 7. Next Phase Roadmap

### Phase 1 - Full MMS Receive Pump and Report Monitor

Goal: make reporting robust while arbitrary confirmed requests are in flight.

Progress:

- done: MMS PDU envelope classifier for confirmed response, confirmed error,
  reject, and unconfirmed InformationReport;
- done: in-memory receive router that queues confirmed results by invoke ID and
  queues InformationReports separately;
- done: guarded report receive path uses the router and preserves inclusion-bit
  diagnostics;
- remaining: background association reader loop and task-based pending operation
  registry.

Deliverables:

- one reader loop per MMS association;
- invoke-ID router for confirmed responses;
- routing for confirmed errors, rejects, aborts, and releases;
- unconfirmed `InformationReport` dispatcher;
- non-blocking report handler pipeline;
- cancellation, timeout, and release handling;
- CLI `mms-report-monitor` for longer report sessions.

Acceptance:

```text
The client can keep a report subscription alive while reads/writes occur, and
unsolicited reports cannot corrupt pending confirmed requests.
```

### Phase 2 - Report Object Model and BRCB Recovery

Goal: move from short smoke reporting to useful commissioning reporting.

Deliverables:

- typed optional-field model: RptID, OptFlds, SqNum, TimeOfEntry, DatSet,
  BufOvfl, EntryID, ConfRev, inclusion bits, and reason-for-inclusion;
- sequence diagnostics;
- duplicate report detection;
- `EntryID` persistence in session;
- `PurgeBuf` support;
- reconnect/re-enable strategy;
- long-run report soak command and validation note.

Acceptance:

```text
The stack can explain what report was received, why each value was included,
what sequence state changed, and what was recovered or lost after reconnect.
```

### Phase 3 - MMS File Transfer

Goal: support common relay file workflows.

Deliverables:

- file directory browse;
- get file;
- set file where safe and supported;
- delete/rename where safe and supported;
- chunked transfer handling;
- progress and retry diagnostics;
- guarded CLI commands:
  - `mms-file-list`
  - `mms-file-get`
  - `mms-file-put`
  - `mms-file-delete`
  - `mms-file-rename`

Acceptance:

```text
The stack can list and download files from at least one lab IED with clear
errors for access denied, missing file, timeout, and unsupported service.
```

### Phase 4 - Control Model

Goal: implement IEC 61850 control as a safe workflow, not as generic write.

Deliverables:

- controllable object discovery;
- `ctlModel` discovery;
- direct operate;
- select-before-operate;
- enhanced security flow;
- `Oper`, `SBO`, and `Cancel` path handling;
- origin/check/test/interlock/synchrocheck handling where applicable;
- command lifecycle diagnostics;
- lab-mode-only CLI at first.

Acceptance:

```text
The stack can perform a controlled operation in a lab with explicit safety,
traceability, and no fallback to brute-force writes.
```

### Phase 5 - GOOSE Subscriber

Goal: decode and supervise live GOOSE streams.

Deliverables:

- raw Ethernet receive through transport abstraction;
- APPID, source, destination, VLAN filtering;
- TTL supervision;
- `stNum`/`sqNum` state tracking;
- SCL DataSet binding;
- value rendering by member order;
- mismatch diagnostics;
- PCAP replay and live CLI monitor.

Acceptance:

```text
The stack can subscribe to GOOSE traffic, bind it to SCL when available, and
report stream health instead of only dumping frames.
```

### Phase 6 - Sampled Values Subscriber

Goal: decode and supervise live SV streams.

Deliverables:

- APPID, source, destination, VLAN filtering;
- ASDU decode and stream identity;
- `smpCnt` wrap handling;
- sample loss/jump diagnostics;
- SCL channel binding;
- raw sample payload and typed engineering value mapping;
- PCAP replay and live CLI monitor.

Acceptance:

```text
The stack can subscribe to SV traffic, detect counter anomalies, and map channel
order from SCL or label data as semantically anonymous when SCL is missing.
```

### Phase 7 - MMS Server and IED Simulator

Goal: make development and demos repeatable without hardware.

Deliverables:

- SCL-backed model;
- read/write behavior;
- DataSet services;
- report services;
- control simulation in safe mode;
- file/log/setting group stubs or implementations as capability grows;
- deterministic scenario scripts;
- interop tests against the stack client.

Acceptance:

```text
The repository can run a deterministic IED simulator useful for automated tests,
examples, and engineering demonstrations.
```

### Phase 8 - WPF Engineering Workbench

Goal: productize proven stack workflows.

Build only after stack APIs are stable.

Workspaces:

- Station: SCL import, live-vs-SCL comparison, topology, validation.
- MMS Client: connect, browse, smart read/write, report wizard, report monitor.
- MMS Server: simulate IED and scenarios.
- GOOSE: subscribe, inspect, publish, replay.
- Sampled Values: subscribe, inspect, publish, replay.
- Capture: PCAP scan/replay/evidence.
- Reports: exportable commissioning evidence.

Acceptance:

```text
A commissioning engineer can use the app without reading source code or knowing
raw MMS naming rules.
```

## 8. Milestone History

### M0 - Clean Stack Seed

Status: implemented.

- .NET solution and library scaffold.
- BER reader/writer.
- MMS common data value codec.
- Ethernet/VLAN/process-bus codec.
- GOOSE frame builder/parser.
- SV frame builder/parser.
- Initial unit tests.

### M1 - SCL Core

Status: first usable pass implemented.

- SCL load support for sample engineering files.
- IED, DataSet, GSEControl, SampledValueControl, and ReportControl extraction.
- DataSet order preservation.
- SCL-backed GOOSE/SV publisher profiles.

Remaining:

- more vendor fixtures;
- fuller DataTypeTemplates resolution;
- multi-file engineering context;
- richer SCL-vs-live validation.

### M2 - Process-Bus Publish MVP

Status: first usable pass implemented.

- in-memory publisher sessions;
- PCAP generation and inspection;
- Npcap live SV publish smoke;
- Npcap live GOOSE publish smoke;
- GOOSE retransmission behavior started.

Remaining:

- GOOSE subscriber;
- SV subscriber;
- typed SV engineering-value packing;
- long-run sequence/timing validation.

### M3 - MMS Association and Discovery

Status: lab MVP implemented.

- TCP/TPKT/COTP/ACSE/MMS association;
- MMS initiate handling;
- `GetNameList` for domain/named variable/named variable list;
- DataSet inventory;
- RCB inventory;
- bounded RCB attribute probing;
- CLI `mms-discover`.

### M4 - Live IED Directory and Smart FC Resolver

Status: lab MVP implemented.

- FC enum/parser;
- raw MMS variable name parsing;
- user reference normalization;
- FC-aware IED model directory;
- `mms-directory`, `mms-find`, `mms-resolve`, and `mms-read-smart`;
- live validation against lab IED.

Remaining:

- `GetVariableAccessAttributes` variable specification model;
- richer ambiguity scoring;
- more multi-vendor evidence.

### M5 - DataSet Directory and Dynamic DataSet Services

Status: lab MVP implemented.

- `GetNamedVariableListAttributes`;
- DataSet member directory with order preserved;
- member mapping to FC-resolved points;
- dynamic DataSet create/delete used by guarded reporting.

Remaining:

- public generic DataSet CLI commands beyond guarded workflows;
- broader vendor behavior matrix;
- more negative tests for service rejection and access denied.

### M6 - Confirmed Write Foundation

Status: partial implementation.

- confirmed write PDU builder/decoder;
- value encoding used for booleans, strings, bit strings, and dynamic DataSet
  references needed by reporting;
- write diagnostics for guarded report/DataSet flows.

Remaining:

- generic guarded `mms-write` public workflow;
- broader value encoder coverage;
- access-result decoding matrix;
- simulator-backed tests.

### M7 - RCB Readiness and Report Configuration

Status: lab MVP implemented.

- RCB inventory and readiness classification;
- static report planner;
- dynamic report planner;
- guarded static report live command;
- guarded dynamic report live command;
- cleanup checks.

Remaining:

- broader reservation/owner vendor variations;
- typed optional fields;
- full receive pump;
- long-run monitor.

## 9. Validation Rules

A feature is not done until it has:

- deterministic unit tests;
- malformed input or unsupported-case tests where practical;
- documented limitations;
- CLI or test harness usage;
- validation note under `docs/validation/` when hardware or interop is involved;
- clear evidence of what was tested and what remains unproven.

Validation levels:

1. unit tests;
2. golden byte tests;
3. round-trip tests;
4. negative/malformed tests;
5. PCAP replay tests;
6. simulator interop tests;
7. real IED lab tests;
8. long-run stability tests;
9. formal conformance path later.

Do not use `conformant` unless formal conformance evidence exists. Use
`interop-tested`, `lab-validated`, or `screening-level` when that is the honest
status.

## 10. Release Definition

### Alpha

- Build and tests pass.
- CLI can inspect SCL, generate/inspect PCAP, publish bounded GOOSE/SV smoke,
  connect MMS, discover IED model, smart-read values, and run guarded report
  smoke tests.
- Known limitations documented.

### Beta

- Full receive pump.
- Report monitor.
- BRCB recovery MVP.
- MMS file transfer.
- GOOSE/SV subscriber MVP.
- At least two simulator profiles and two real IED/vendor validation notes.

### Release Candidate

- Buffered and unbuffered reporting stable.
- Subscriber side GOOSE/SV stable.
- MMS server/simulator useful for repeatable tests.
- WPF workbench usable for real workflows.
- Interop matrix documented.
- Long-run report/subscriber tests completed.

### Public 1.0

- Clean-room license hygiene complete.
- API surface stable enough for external users.
- Tagged release.
- User documentation and validation evidence complete.
- No marketing overclaim beyond proven capability.

## 11. Non-Negotiable Boundaries

- No restrictive-license implementation code may enter this repository.
- No UI logic in protocol stack.
- No network publishing without explicit adapter selection and confirmation.
- No write/control operation hidden inside discovery.
- No report enable on occupied/reserved RCBs unless an explicit lab-force mode
  is implemented and confirmed.
- No brute-force control/write attempts.
- No claiming timing precision from normal Windows/Npcap behavior.
- No WPF screen before the stack API it needs exists.
- No deleting tests to make a build pass.
- No silently choosing one interpretation when SCL/live model conflicts.
