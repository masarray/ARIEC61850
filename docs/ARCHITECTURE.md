# ARIEC61850 Architecture

ARIEC61850 is organized as a reusable protocol stack with tester applications on
top. Protocol logic must stay in libraries under `src/`.

## Project layers

```text
apps/
  AR.Iec61850.Cli/                  CLI tester and automation surface

src/
  AR.Iec61850/                      protocol primitives and engineering model
  AR.Iec61850.Transports.Npcap/     raw Ethernet transport adapter

tests/
  AR.Iec61850.Tests/                unit and round-trip validation
```

## Layer 0 - byte primitives

Purpose: deterministic protocol building blocks.

Includes:

- ASN.1 BER reader and writer.
- MMS common data values.
- Ethernet and VLAN frame codecs.
- MAC address, APPID, UTC time, and quality helpers.

Rules:

- No UI.
- No network IO.
- No SCL assumptions.
- Every codec needs byte-level tests.

## Layer 1 - SCL engineering model

Purpose: import SCL into a strongly typed engineering context.

Current model covers:

- IEDs.
- DataSets and FCDA order.
- GOOSE streams.
- Sampled Values streams.
- ReportControl blocks.
- Communication APPID, destination MAC, VLAN ID, and VLAN priority.
- Basic conflicts and warnings.

## Layer 2 - process bus services

Purpose: reusable GOOSE and Sampled Values publisher/subscriber logic.

Current implementation:

- GOOSE publisher profile from SCL.
- GOOSE publisher session with state and sequence behavior.
- SV publisher profile from SCL.
- SV publisher session with `smpCnt` wrap behavior.
- In-memory transport for deterministic tests.
- Npcap transport for live SV publishing.
- PCAP writer, reader, and monitor.

Next implementation:

- SCL-bound SV subscriber.
- SCL-bound GOOSE subscriber.
- GOOSE retransmission schedule.
- General typed SV payload packing.

## Layer 3 - MMS transport foundation

Planned build order:

1. TPKT.
2. COTP.
3. ISO Session.
4. ISO Presentation.
5. ACSE.
6. MMS initiate.

MMS discovery, report-control client behavior, and server simulation should be
built only after the lower transport layers are testable.

## Design rule

The same publisher session should run against memory, PCAP test workflows, or a
selected raw Ethernet adapter without changing protocol logic.
