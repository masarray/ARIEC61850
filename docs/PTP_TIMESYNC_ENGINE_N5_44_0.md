# N5.44.0 - PTP TimeSync Engine Foundation

This patch adds the first reusable PTP/TimeSync foundation into `AR.Iec61850`.

## Scope

Added core stack modules:

- `AR.Iec61850.TimeSync.Ptp`
  - PTP constants
  - PTP message types
  - clock identity and port identity
  - PTP timestamp
  - Announce / Sync / Follow_Up / Pdelay serializer helpers
  - PTPv2 message parser
  - Ethernet frame parser for EtherType `0x88F7`
  - VLAN and QinQ-aware frame inspection
- `AR.Iec61850.TimeSync.Monitoring`
  - passive PTP monitor
  - recent observed messages
  - per-source message counters
  - sequence anomaly detection
- `AR.Iec61850.TimeSync.Health`
  - PTP health validator
  - domain/liveness/message presence checks
  - Sync/Follow_Up/Pdelay readiness
  - `smpSynch` recommendation policy

## Product direction

The reusable engine belongs in `ARIEC61850`.
ARSVIN should consume it for:

- PTP status bar
- process-bus sync diagnostics
- relay compatibility warnings
- `smpSynch` recommendation
- future lab-only PTP publisher UI

## Safety boundary

This patch does not claim relay-grade grandmaster timing. It adds parser, serializer, passive monitor, and health policy foundation. Hardware timestamping, BMCA completeness, PHC/servo discipline, and certified timing behavior remain out of scope for this phase.
