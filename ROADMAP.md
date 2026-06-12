# ARIEC61850 Roadmap

Last updated: 2026-06-12

## North Star

Build a clean-room, reusable IEC 61850 native stack for .NET, then build tester
products on top of it.

The stack must become useful for:

- MMS client tester.
- MMS server and IED simulator.
- GOOSE publisher and subscriber.
- Sampled Values publisher and subscriber.
- SCL-driven station validation.
- WPF/CLI tester applications that behave like serious commissioning tools.

The long-term product direction is an IEC 61850 station testing workbench in the
same problem class as StationScout, IEDScout, and SVScout, but implemented as an
original clean-room system with our own architecture, UX, and validation suite.

## Research Anchors

These products and libraries are references for capability planning only. Do not
copy source code, private behavior, visual design, text, icons, layouts, or
licensed implementation details.

- OMICRON StationScout: SCL visualization, signal tracing, IED simulation,
  GOOSE/HMI/SCADA mapping tests, repeatable test cases, live comparison between
  configuration and real station behavior.
  https://www.omicronenergy.com/en/products/stationscout/
- OMICRON IEDScout: IEC 61850 IED exploration, multiple IED investigation,
  Activity Monitor for reports, GOOSE and data objects, traffic investigation,
  and IED simulation.
  https://www.omicronenergy.com/en/products/iedscout/
- OMICRON SVScout: multi-stream SV subscription, waveform and phasor display,
  80/256 samples-per-cycle support, COMTRADE recording, playback from capture,
  printable measurement reports, detailed stream inspection.
  https://www.omicronenergy.com/en/products/svscout/
- libIEC61850: feature coverage reference for MMS client/server, GOOSE, SV,
  reporting, discovery, data sets, logs, and files. GPLv3 code must not be
  copied, translated, or structurally ported.
  https://libiec61850.com/documentation/
  https://github.com/mz-automation/libiec61850
- DigSubAnalyzer: local proof that our receive-only raw SV/GOOSE/PTP decoder,
  SCL binding, target-aware diagnostics, and engineering UX direction can work.
  Keep it as a passive analyzer product and learning source.

## Product Split

The project family must be split into reusable stack, test harnesses, and tester
apps. Do not let app code become the protocol implementation.

```text
ARIEC61850/
  src/
    AR.Iec61850/                  reusable clean-room stack
    AR.Iec61850.Transports.Npcap/ raw Ethernet transport adapter
    AR.Iec61850.TestKit/          reusable protocol test helpers and fixtures
  apps/
    AR.Iec61850.Workbench.Wpf/    future station/MMS/GOOSE/SV tester UI
    AR.Iec61850.Cli/              future automation and smoke-test CLI
  tests/
    AR.Iec61850.Tests/            unit and protocol golden tests
    AR.Iec61850.InterOpTests/     optional hardware/network tests
  samples/
    scl/
    pcap/
    scripts/
  docs/
    architecture/
    validation/
    ux/
```

Current repository status: the stack projects exist with BER, Ethernet
process-bus frames, MMS data values, GOOSE frame builder and parser, SV frame
builder and parser, SCL parsing, SCL-backed GOOSE/SV publisher profiles,
in-memory publisher sessions, PCAP generation/inspection/replay, Npcap raw SV
live publishing, Npcap raw GOOSE live publishing, and native MMS discovery with
TPKT/COTP/ACSE/MMS association, `GetNameList`, DataSet inventory, RCB inventory,
and bounded RCB attribute probing.

## Non-Negotiable Boundaries

- `AR.Iec61850` is the reusable engine. It must not depend on WPF, Npcap,
  WinForms, app settings, UI view models, or product workflow state.
- Tester apps may depend on the stack. The stack must never depend on tester
  apps.
- Transport adapters are replaceable. Codecs produce and consume bytes.
- Every publisher must support an in-memory transport for tests before raw
  Ethernet output is introduced.
- Any real network publish action must require explicit adapter selection,
  destination identity, and visible operator confirmation in UI.
- DigSubAnalyzer remains receive-only. Do not move active publisher/control
  workflows into that product.

## Architecture Layers

### Layer 0 - Byte and Type Primitives

Purpose: deterministic protocol building blocks.

Includes:

- BER TLV reader and writer.
- MMS data value codec.
- Ethernet/VLAN/process-bus frame codec.
- MAC, APPID, VLAN, UTC time, quality, bit-string, object-reference types.

Rules:

- No network IO.
- No UI.
- No SCL assumptions.
- Every codec has golden byte tests and round-trip tests.

### Layer 1 - SCL Engineering Model

Purpose: convert SCD/CID/ICD/IID into a strongly typed engineering context.

Includes:

- Header, edition and namespace fingerprint.
- IED, AccessPoint, LDevice, LN/LN0.
- DataSet and FCDA order.
- GSEControl and SampledValueControl.
- ReportControl, LogControl, SettingControl where needed for MMS.
- Communication addresses for GSE and SMV.
- DataTypeTemplates resolution to CDC, bType, type id, enum type.

Rules:

- Read-only first. No SCL editor until parser and validation are mature.
- Preserve DataSet order exactly.
- Support multiple SCL files in one engineering context.
- Detect duplicate IED names, duplicate APPIDs, conflicting control blocks,
  conflicting `confRev`, and differing DataSet order.

### Layer 2 - Process Bus Services

Purpose: reusable GOOSE/SV publisher and subscriber logic.

Includes:

- GOOSE publisher state machine: `stNum`, `sqNum`, TAL, retransmission schedule,
  test flag, ndsCom, confRev, DataSet value mapping.
- GOOSE subscriber: stream identity, stale/lost states, TTL supervision, typed
  value decode, changed-value detection, SCL semantic labels.
- SV publisher: `smpCnt`, `smpSynch`, `smpRate`, `smpMod`, nofASDU, DataSet
  payload packing, pacing clock, frame generation.
- SV subscriber: stream identity, sequence supervision, DataSet payload decode,
  channel mapping, RMS/phasor helpers as optional analysis services.

Rules:

- Publisher state is testable without NIC access.
- The clock is injectable.
- The transport is injectable.
- No product-specific labels in the stack.

### Layer 3 - MMS Transport Stack

Purpose: native MMS client/server foundation.

Build in this order:

1. TPKT.
2. COTP connection and data TPDU.
3. ISO Session.
4. ISO Presentation.
5. ACSE associate/release/abort.
6. MMS initiate and confirmed request/response envelope.

Rules:

- Do not skip lower layers to make a demo work.
- Capture and expose negotiated parameters.
- Every handshake layer has byte-level tests.
- Connection code must support cancellation, timeout, reconnect, and diagnostic
  trace events.

### Layer 4 - MMS Client Services

Purpose: IEDScout-like client exploration and testing.

Includes:

- Connect/disconnect and association diagnostics.
- Self-description/model discovery.
- Logical device, logical node, data object and data attribute browsing.
- Read and write data attributes.
- DataSet browse, read, create/delete dynamic DataSet later.
- ReportControl discovery and configuration.
- Buffered and unbuffered report receive path.
- File service later.
- Control model later, guarded and explicit.

Rules:

- Read-only discovery comes before writes.
- Writes must require explicit API calls and UI confirmation in tester apps.
- Reports need an RCB state machine, owner/reservation tracking, `RptEna`,
  `GI`, `TrgOps`, `OptFlds`, `EntryID`, `SqNum`, `BufOvfl`, and reason parsing.

### Layer 5 - MMS Server and IED Simulation

Purpose: StationScout/IEDScout-like simulation for test environments.

Includes:

- SCL-backed server model.
- Logical devices/nodes/data attributes.
- Read/write service behavior.
- DataSet and ReportControl behavior.
- GOOSE/SV publisher integration from server data state.
- Scenario scripts for state changes, breaker positions, alarms, analog ramps.

Rules:

- Server data model is deterministic and inspectable.
- Simulation scripts are separate from protocol core.
- Any unsafe control behavior is disabled by default.

### Layer 6 - Tester Applications

Purpose: product UX that uses the stack.

Apps:

- WPF Workbench: primary engineering tester.
- CLI: automation, smoke tests, capture conversion, scripted publish/subscribe.
- Future headless service: lab automation if needed.

The WPF Workbench should have these workspaces:

- Station: SCL import, IED topology, expected communication, live differences,
  test cases, station validation.
- MMS Client: connect to IED, browse model, read/write, report monitor,
  connection trace.
- MMS Server: simulate IED from SCL, expose data model, publish reports, run
  scenarios.
- GOOSE: subscribe, inspect, publish, replay, SCL mapping, TTL supervision.
- SV: subscribe, waveform/phasor, publish from SCL/profile, sequence/timing
  diagnostics, capture playback.
- Capture: PCAP/PCAPNG scan/replay, export evidence.
- Reports: commissioning report, mismatch report, report-control status, timing
  confidence.

## UX Direction

This is not a marketing app. It is an engineering instrument.

Design read:

```text
Dense regulated engineering workbench for commissioning engineers, with a calm
industrial cockpit language, high evidence density, stable navigation, and no
decorative visual noise.
```

UX rules:

- First screen is the tool, not a landing page.
- Left rail: stable targets and workspaces.
- Center: decision table or primary instrument.
- Right panel: selected-target inspector and evidence.
- Header right: adapter/session/run controls.
- Never show a serious warning without the affected target.
- Never make live targets jump order during refresh.
- Do not hide primary evidence behind ellipsis unless the full value is visible
  in the same screen.
- Use status states explicitly: `PASS`, `WARNING`, `FAIL`, `UNKNOWN`,
  `MATCHED`, `WEAK`, `MISSING`, `UNEXPECTED`, `MISMATCH`, `CONFLICT`.
- Use cards only for repeated target items, not for nested page structure.
- Main tables must be readable at 1600x900 and 1920x1080.
- Raw hex belongs in advanced/evidence panels, not as the primary value when
  typed decode exists.
- Avoid UI copy that overclaims timing precision or conformance.

## Validation Strategy

Validation is part of the product, not a phase at the end.

Test levels:

- Unit tests: byte codecs, length handling, integer encoding, field mapping.
- Golden tests: known GOOSE/SV/MMS byte arrays, SCL snippets, parse trees.
- Round-trip tests: encode -> parse -> compare semantic object.
- Negative tests: malformed BER, length mismatch, invalid PDU ordering, missing
  required fields, unsupported tags.
- PCAP tests: replay known captures into subscribers and validators.
- Interop tests: run against known tools and real IEDs in an isolated lab.
- Hardware tests: physical NIC/TAP timing and raw Ethernet transmit behavior.

Do not mark a feature production-ready until it has:

- deterministic unit tests,
- at least one malformed input test,
- documented limitations,
- sample usage,
- and a validation note in `docs/validation/`.

## Milestones

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

Done means:

```text
The stack can generate parseable GOOSE and SV Ethernet frames from explicit
programmatic inputs without UI or external IEC 61850 libraries.
```

### M1 - SCL Core

Status: first usable pass implemented.

Goal: make SCL the engineering source of truth.

Deliverables:

- `AR.Iec61850.Scl` namespace or project.
- Multi-file SCL engineering context.
- SCD/CID/ICD/IID parse support.
- DataSet order and type resolution.
- GSEControl and SampledValueControl extraction.
- ReportControl extraction.
- Conflict model.
- Golden SCL fixtures.

Done means:

```text
Given an SCL file, the stack can list expected GOOSE, SV, reports, DataSets,
transport addresses, control block references, and payload entry order.
```

Current limitations:

- SCL parsing covers the core objects needed for first publish profiles.
- Type resolution supports common DO/SDO/DA/BDA chains, but needs more vendor
  fixtures before it should be called mature.
- Conflict detection is basic and will expand with multi-file engineering
  context.

### M2 - SCL -> GOOSE/SV Publish Profiles

Status: first usable pass implemented for in-memory sessions, PCAP smoke
generation, offline PCAP inspection, decoded console stream output, live SV
publish smoke through the Npcap transport, and live GOOSE publish smoke through
the Npcap transport.

Goal: publish process-bus frames from SCL, not hand-coded fields.

Deliverables:

- `GoosePublisherProfile.FromScl(...)`.
- `SampledValuesPublisherProfile.FromScl(...)`.
- Value binding model for DataSet entries.
- Strong validation errors for missing APPID/MAC/VLAN/DataSet/confRev.
- In-memory publisher sessions with deterministic clocks.
- CLI smoke command draft: `publish-goose` and `publish-sv` using fake transport.

Done means:

```text
Given SCL plus values, the stack can create a deterministic GOOSE or SV publish
session and produce the exact frame sequence expected by tests.
```

Current limitations:

- SV still accepts raw sample payload bytes. Typed engineering-value-to-SV
  payload packing is next.
- GOOSE accepts typed MMS values and validates value count against DataSet order.
- Generated PCAP output, offline PCAP inspection, decoded stream output, adapter
  listing, dry-run publish, live SV publish, and live GOOSE publish are available through
  `apps/AR.Iec61850.Cli`.
- Live SV pacing exists as a software smoke-test clock. It is not yet a
  hard-real-time publisher.

### M3 - Raw Ethernet Transport

Status: first usable pass implemented for SV and GOOSE publish.

Goal: safely send and receive process-bus frames through replaceable transports.

Deliverables:

- `IProcessBusTransport` implemented.
- In-memory transport implemented.
- PCAP generation/inspection/replay implemented through CLI helpers.
- Npcap raw Ethernet transport adapter implemented.
- Adapter discovery implemented.
- Explicit CLI confirmation contract for active publishing implemented with
  `--yes`; safe validation path available with `--dry-run`.
- Bounded and long-running SV publish controls implemented with `--frames`,
  `--duration-sec`, and `--continuous`.
- Bounded and long-running GOOSE publish controls implemented with `--frames`,
  `--duration-sec`, and `--continuous`, plus optional `--toggle-every-sec`
  state changes.

Done means:

```text
The same publisher session can target memory, PCAP test harness, or selected raw
Ethernet adapter without changing protocol logic.
```

### M4 - GOOSE Subscriber and SV Subscriber

Goal: reuse DigSubAnalyzer learning inside stack-quality subscriber services.

Deliverables:

- GOOSE subscriber engine with TTL/stNum/sqNum supervision.
- SV subscriber engine with stream identity and sequence supervision.
- SCL semantic binding for values and sample channels.
- Typed quality/time/value helpers.
- PCAP replay tests.

Done means:

```text
The stack can subscribe to process-bus traffic, bind it to SCL, and produce
target-aware stream health evidence independent of any UI.
```

### M5 - MMS Transport Foundation

Goal: native MMS connection layers.

Status: first usable pass implemented for client-side TCP/TPKT/COTP, ACSE/MMS
association, ISO Presentation P-DATA wrapping, and confirmed request/response
envelopes. Release/abort diagnostics still need a formal state model.

Deliverables:

- TPKT codec.
- COTP codec and connection state.
- Session codec.
- Presentation codec.
- ACSE associate/release/abort.
- MMS initiate.
- Diagnostic trace model.

Done means:

```text
The stack can establish and release an IEC 61850 MMS association and expose a
diagnostic trace for every negotiated layer.
```

### M6 - MMS Client Discovery and Read

Goal: IEDScout-like read-only exploration.

Status: first usable pass implemented. `mms-discover` connected to lab IED
`192.16.1.157:102`, discovered 4 logical devices, 10,122 variables, 1 DataSet,
and 286 RCBs. Generic model browsing and broader typed read coverage still need
expansion.

Deliverables:

- Logical device discovery.
- Logical node/data browsing.
- Data attribute read.
- DataSet browse/read.
- Object-reference model.
- Typed MMS data conversion to IEC 61850 values.

Done means:

```text
The stack can connect to a vendor IED or simulator, browse its model, and read
selected values with typed results and traceable MMS evidence.
```

### M7 - MMS Reports

Goal: report-control testing.

Status: started. RCB inventory and bounded attribute probing are implemented.
Report activation and InformationReport receive/decode are next.

Deliverables:

- ReportControl discovery.
- URCB/BRCB model.
- RCB configuration.
- Report enable/disable.
- GI trigger.
- InformationReport parser.
- Buffered report sequence handling.
- Owner/reservation diagnostics.

Done means:

```text
The stack can configure and monitor IEC 61850 reports and explain report state,
trigger reason, sequence, buffer, and ownership evidence.
```

### M8 - MMS Server and IED Simulator

Goal: simulate IEDs for station testing.

Deliverables:

- SCL-backed server model.
- Read service.
- Write service.
- DataSet services.
- Report generation.
- GOOSE/SV publisher integration.
- Scenario engine.

Done means:

```text
A tester app can simulate one or more IEDs from SCL and drive repeatable state
changes for station validation.
```

### M9 - WPF Workbench MVP

Goal: first integrated tester product.

Deliverables:

- Project shell under `apps/`.
- Station workspace with SCL topology and validation matrix.
- MMS Client workspace.
- GOOSE workspace.
- SV workspace.
- Capture/replay workspace.
- Evidence export.

Done means:

```text
An engineer can import SCL, connect/capture/publish in controlled modes, inspect
MMS/GOOSE/SV evidence, and produce a compact test report.
```

### M10 - Interoperability Lab and Product Hardening

Goal: make claims defensible.

Deliverables:

- Hardware lab checklist.
- Real IED/simulator matrix.
- Vendor interoperability notes.
- Capture corpus.
- Performance tests for SV rates.
- Installer/package.
- Public documentation with cautious claims.

Done means:

```text
The stack and tester apps have repeatable evidence across fixtures, captures,
simulators, and at least one physical lab path.
```

## Immediate Patch Order

1. Add typed SV payload packing from SCL DataSet entries.
2. Add GOOSE retransmission schedule service with injectable clock.
3. Add GOOSE and SV subscriber services with SCL binding.
4. Add live capture/subscribe smoke commands for adapter validation.
5. Add CLI smoke tests around live publish dry-run and adapter selection.
6. Add WPF Workbench SV publisher/subscriber workspace only after CLI paths are
   stable.
7. Start MMS transport layers after process-bus profiles and subscribers are
   stable.

## What We Will Not Do

- No direct dependency on `libiec61850`.
- No copied code from GPL projects.
- No WPF protocol logic.
- No hidden active network publishing.
- No global SV channel order assumption.
- No "works on my PC" protocol claims without tests.
- No conformance claims before formal validation evidence.
- No UI that hides mismatch evidence or makes live targets reorder randomly.
