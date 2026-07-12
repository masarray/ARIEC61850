# Architecture

ARIEC61850 is organized as a reusable protocol stack plus thin user-facing tools.

```text
ARIEC61850
├─ src
│  ├─ AR.Iec61850
│  │  ├─ Asn1 / BER codec
│  │  ├─ Osi / TPKT + COTP
│  │  ├─ Acse / MMS association helpers
│  │  ├─ Mms / discovery, read, write, datasets, RCBs, reporting
│  │  ├─ Control / typed control discovery, exact MMS binding, sequence execution
│  │  ├─ Goose / GOOSE frame builder/parser/session helpers
│  │  ├─ SampledValues / SV frame builder/parser/payload model
│  │  ├─ Scl / SCL parser and publisher profiles
│  │  ├─ Capture / PCAP writer/reader
│  │  └─ Monitoring / stream diagnostics
│  ├─ AR.Iec61850.Simulation
│  │  └─ Offline IED profile, point runtime, DataSet, and RCB simulation foundation
│  └─ AR.Iec61850.Transports.Npcap
│     └─ Npcap-backed raw Ethernet transport
├─ apps
│  ├─ AR.Iec61850.Cli
│  ├─ AR.Iec61850.IedDiscovery
│  ├─ AR.Iec61850.IedSimulator
│  ├─ AR.Iec61850.EngineeringWorkbench
│  └─ AR.Iec61850.SvPublisher
├─ tests
│  └─ AR.Iec61850.Tests
├─ samples
│  └─ scl
├─ docs
└─ landing
```

## Design principles

- Keep the core library independent from UI concerns.
- Keep raw Ethernet transport optional and Windows-lab specific.
- Treat report enable, dynamic DataSet binding, and GI as guarded state-machine operations.
- Preserve raw protocol evidence when decoded shape is incomplete.
- Keep generated evidence outside source control.
- Keep public docs focused on users, not internal audit history.

## Main layers

### Core protocol layer

`src/AR.Iec61850` contains the reusable implementation: BER, MMS, GOOSE, SV, PCAP, SCL, and diagnostics.

The `Control` namespace sits above the generic MMS services. It discovers `ctlModel` and the exact live variable specification, owns Direct/SBO normal/enhanced state, and consumes InformationReport command termination through the single association receive pump. Public generic MMS write is blocked for `Oper`, `SBOw`, and `Cancel`.

### Simulation layer

`src/AR.Iec61850.Simulation` contains the offline IED simulator foundation. It models logical devices, logical nodes, points, DataSets, RCB profiles, deterministic value changes, and event snapshots without opening a network server.

### Transport layer

`src/AR.Iec61850.Transports.Npcap` contains live raw Ethernet integration. It is intentionally separate so the core library remains usable without installing Npcap.

### CLI layer

`apps/AR.Iec61850.Cli` exposes reproducible engineering commands for SCL, PCAP, MMS discovery, reads, report planning, and guarded report sessions.

### Desktop layer

`apps/AR.Iec61850.SvPublisher` is the WPF desktop workspace for Sampled Values publishing/injection.

`apps/AR.Iec61850.IedDiscovery` is the WPF workspace for live MMS model discovery, DataSet directory inspection, RCB inventory, and JSON evidence export.

`apps/AR.Iec61850.IedSimulator` is the WPF simulator workspace. It loads a default or SCL-derived model, runs the deterministic value engine, and exposes a read-only MMS server on TCP/102 for independent IEC 61850 clients. The server remains a thin application boundary over the reusable transport, ACSE, MMS dispatcher, and simulator model layers.

`apps/AR.Iec61850.EngineeringWorkbench` is a read-only WPF harness over the engine profiles. It orchestrates SCL engineering, process-bus binding, GOOSE/SV diagnostics, MMS read-only loopback, and structured evidence-pack export without embedding protocol logic in the UI.

### Test layer

`tests/AR.Iec61850.Tests` validates codecs, protocol shape, reporting planners, PCAP helpers, SCL parsing, stream diagnostics, and simulator runtime shape.
