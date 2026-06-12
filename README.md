# ARIEC61850

Clean-room IEC 61850 building blocks for reusable .NET projects.

This repository is intentionally separate from analyzer products. The first
milestone focuses on deterministic byte-level primitives that can be validated
with tests and later connected to project-specific transports:

- ASN.1 BER TLV reader/writer.
- MMS `Data` value codec for common GOOSE/report payload values.
- Ethernet/VLAN process-bus frame builder/parser.
- GOOSE publisher frame builder and decoder for test round-trips.
- Sampled Values publisher frame builder and decoder for test round-trips.

## Clean-Room Boundary

The implementation is original source for this repository. External IEC 61850
projects may be used only as behavioral references, documentation pointers, or
interoperability peers. Do not copy or translate GPL implementation code into
this repository.

## Current Scope

Implemented:

- GOOSE frame byte generation including APPID, VLAN, GOOSE APDU, state number,
  sequence number, configuration revision, and typed dataset values.
- GOOSE frame parsing for validation and future subscriber work.
- SV frame byte generation including APPID, VLAN, SavPdu, ASDU sequence,
  `smpCnt`, `confRev`, `smpSynch`, `smpRate`, `smpMod`, and raw sample payload.
- SV frame parsing for validation and future subscriber work.
- BER length/tag/value support with definite short and long-form lengths.
- MMS data types commonly needed by GOOSE and reporting payloads.

Next milestones:

- ISO-on-TCP, TPKT, COTP, ACSE, Presentation, MMS initiate.
- MMS discovery client.
- MMS report-control client and InformationReport parser.
- SCL-backed server data model and report generation.
