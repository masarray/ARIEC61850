# Roadmap

## Current milestone

### N5.19 - Smart GOOSE sniffer diagnostics and publisher state consistency

- SCL-bound GOOSE monitor profiles now map frame `allData` values back to DataSet order.
- GOOSE stream diagnostics now classify first, retransmission, state change, duplicate, jump, and regression cases.
- TimeAllowedToLive supervision, timeout counters, and changed-value summaries are visible in PCAP inspection and stream playback.
- Demo PCAP and live publisher payloads now stay stable during retransmission and only change on a state change.
- Unit tests cover SCL binding, retransmission, valid state change, invalid value change without `stNum`, and TAL expiry.

Remaining before claiming live subscriber usability:

- Add adapter receive loops for GOOSE/SV using the Npcap transport.
- Add live GoCB MMS discovery/readback for `GoEna`, `GoID`, `DatSet`, `ConfRev`, `NdsCom`, `MinTime`, `MaxTime`, and `DstAddress`.
- Add long-running capture evidence from real relays and simulator interop.

## Near term

- Make the MMS reporting flow available as a guided desktop wizard.
- Keep RCB/DataSet selection as setup-time configuration, not an always-editable runtime control.
- Add a reporting runtime workspace with report timeline, sequence diagnostics, GI indicator, and evidence export.
- Expand multi-vendor reporting soak tests.
- Improve WPF SV Publisher release packaging and UX polish.
- Add live GOOSE/SV subscriber CLI loops over Npcap receive.

## Mid term

- Add richer SCL validation and profile export.
- Add MMS file/log/setting-group coverage.
- Add an IED simulator mode for training and demos.

## Long term

- Add IEC 62351 profile support where practical.
- Prepare formal validation evidence for selected protocol areas.
