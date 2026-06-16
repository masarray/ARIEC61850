# N5.44.2 — Lab PTP Publisher Runtime

This patch adds a lab-only PTP publisher runtime that can run beside the SV publisher on the same selected Npcap adapter.

## What changed

- Added `AR.Iec61850.TimeSync.PtpRuntime`:
  - `PtpPublisherOptions`
  - `PtpPublisherRuntime`
  - `PtpPublisherStatus`
  - `PtpSequenceCounters`
- Added periodic PTP Layer-2 transmission:
  - Announce
  - Sync
  - Follow_Up
  - optional Pdelay_Resp / Pdelay_Resp_Follow_Up response
- Extended `PtpMessageSerializer.BuildEthernetFrame` with VLAN priority support.
- Extended SV Publisher Stream Config with:
  - `PTP Publisher` mode
  - PTP clock identity
  - announce interval
  - sync interval
  - Pdelay response toggle
- Integrated Lab PTP Publisher into the live SV publishing session.
- Status bar now shows PTP RX, PTP TX, and `smpSynch` state.

## Runtime topology

```text
Live ARSVIN session
  ├─ SV Publisher Loop
  ├─ PTP Passive Monitor
  └─ Lab PTP Publisher Loop
       ├─ Announce
       ├─ Sync
       ├─ Follow_Up
       └─ Pdelay response when requested
```

The same `NpcapProcessBusDuplexTransport` is used for transmit and capture so the selected NIC is the single source of truth for SV/PTP behavior.

## Safety boundary

Lab PTP Publisher is a software timestamp, Npcap-based traffic generator. It is not a certified grandmaster and does not provide hardware timestamp guarantees.

Use external GPS/PTP grandmaster hardware for relay-grade, conformance, FAT/SAT, or protection acceptance tests.

## smpSynch behavior

When `PTP Publisher = LabPublisher` and `Sync policy = AutoPtp`, ARSVIN publishes SV with `smpSynch=2` because ARSVIN is intentionally acting as the lab timing source.

Operators can still override with:

- `ForceUnsynchronized` → `smpSynch=0`
- `ForceLocal` → `smpSynch=1`
- `ForceGlobal` → `smpSynch=2`

## Recommended first test

1. Select a dedicated NIC.
2. Enable VLAN if the relay expects it.
3. Set `PTP Publisher = LabPublisher`.
4. Keep domain `0` unless the relay is configured otherwise.
5. Start live injection.
6. Verify in Wireshark:
   - EtherType `0x88F7` PTP Announce/Sync/Follow_Up
   - EtherType `0x88BA` SV
   - same VLAN/domain as relay subscription
   - SV `smpSynch=2`
