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
│  │  ├─ Goose / GOOSE frame builder/parser/session helpers
│  │  ├─ SampledValues / SV frame builder/parser/payload model
│  │  ├─ Scl / SCL parser and publisher profiles
│  │  ├─ Capture / PCAP writer/reader
│  │  └─ Monitoring / stream diagnostics
│  └─ AR.Iec61850.Transports.Npcap
│     └─ Npcap-backed raw Ethernet transport
├─ apps
│  ├─ AR.Iec61850.Cli
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

### Transport layer

`src/AR.Iec61850.Transports.Npcap` contains live raw Ethernet integration. It is intentionally separate so the core library remains usable without installing Npcap.

### CLI layer

`apps/AR.Iec61850.Cli` exposes reproducible engineering commands for SCL, PCAP, MMS discovery, reads, report planning, and guarded report sessions.

### Desktop layer

`apps/AR.Iec61850.SvPublisher` is the current WPF desktop workspace for Sampled Values publishing.

### Test layer

`tests/AR.Iec61850.Tests` validates codecs, protocol shape, reporting planners, PCAP helpers, SCL parsing, and stream diagnostics.
