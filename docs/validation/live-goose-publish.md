# Live GOOSE Publish Validation Note

Date: 2026-06-12

## Scope

This note covers the first live GOOSE publish path:

```text
SCL file
-> SclParser
-> GoosePublisherProfile
-> GoosePublisherSession
-> GooseRetransmissionSchedule
-> NpcapProcessBusTransport
-> selected Ethernet adapter
```

This is a lab smoke path for proving raw Ethernet GOOSE output. It is not a
conformance claim.

## Commands

List adapters:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- list-adapters
```

Dry-run without NIC transmit:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- publish-goose-live samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --frames 8 --dry-run --toggle-every-sec 0.02
```

Publish a bounded live GOOSE stream:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- publish-goose-live samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --duration-sec 5 --yes --toggle-every-sec 2
```

Publish continuously until `Ctrl+C`:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- publish-goose-live samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --continuous --yes --toggle-every-sec 2
```

## Evidence

The imported SCL produced one GOOSE stream:

```text
Control block: MU01LD0/LLN0$GO$GCB01
goID: trip-goose
APPID: 0x1001
Destination MAC: 01:0C:CD:01:00:01
VLAN: 100/prio 4
DataSet entries: 3
minTime: 4 ms
maxTime: 1000 ms
```

Live run:

```text
duration=5 seconds
frames=26
stNum=1..3
sqNum reset on state change
values=3
```

## Behavior

- `sqNum` increments on retransmission.
- `stNum` increments when `--toggle-every-sec` triggers a simulated DataSet
  state change.
- Retransmission delay starts at SCL `minTime`, doubles toward SCL `maxTime`,
  and resets after a state change.
- `--test` sets the GOOSE test flag.
- `--nds-com` sets the needs-commissioning flag.

## Safety Contract

- Active publishing requires `--yes`.
- Use `--dry-run` before live output.
- Use `list-adapters`; do not guess adapter indexes.
- Send live traffic only on an isolated test NIC/TAP or lab switch.
- Do not use an office network or production substation network.

## Limitations

- Dataset value generation is a deterministic demo binder, not a complete
  engineering value binding system.
- Current live command sends one selected GOOSE stream per process.
- There is no live GOOSE subscriber command yet.
- Timing is software-based and subject to OS scheduling jitter.
