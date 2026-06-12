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

Client-side implementation now includes:

- TCP/TPKT connection.
- COTP connection and data TPDU.
- ACSE/MMS association profiles.
- ISO Presentation P-DATA wrapping.
- MMS `GetNameList`.
- MMS Confirmed-Read.
- DataSet inventory.
- RCB inventory and bounded RCB attribute probing.

Next implementation:

- ACSE release/abort state model.
- Report enable/disable and GI writes.
- InformationReport receive/decode.
- MMS server simulation.

## Design rule

The same publisher session should run against memory, PCAP test workflows, or a
selected raw Ethernet adapter without changing protocol logic.
