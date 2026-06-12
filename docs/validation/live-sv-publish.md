# Live SV Publish Validation Note

Date: 2026-06-12

## Scope

This note covers the first live Sampled Values publish path:

```text
SCL file
-> SclParser
-> SampledValuesPublisherProfile
-> demo 4I+4V sample payload
-> SampledValuesPublisherSession
-> NpcapProcessBusTransport
-> selected Ethernet adapter
```

This is a lab smoke path for proving raw Ethernet output. It is not a
conformance claim and it is not protection-grade timing evidence.

## Commands

List adapters:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- list-adapters
```

Dry-run the 9-2LE sample stream without NIC transmit:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- publish-sv-live "samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 16 --dry-run
```

Publish one second of SV traffic to the selected adapter:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- publish-sv-live "samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --yes
```

Publish for a fixed duration:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- publish-sv-live "samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --duration-sec 60 --yes
```

Publish continuously until `Ctrl+C`:

```powershell
dotnet run --project apps\AR.Iec61850.Cli -- publish-sv-live "samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --continuous --yes
```

## Evidence

Local adapter enumeration found 11 Npcap devices. The selected Ethernet adapter
for the smoke run was:

```text
[5] MAC=68:E4:3B:30:92:CA
Corechip SR9900 USB2.0 to Fast Ethernet Adapter
```

The imported SCL produced three SV streams. Stream #1 resolved as:

```text
Control block: MU01_4I_4V_1MU01/LLN0$SV$MSVCB01
svID: OMICRON_CMC_SV1
APPID: 0x4000
Destination MAC: 01:0C:CD:04:00:01
VLAN: 0/prio 4
DataSet entries: 16
Payload bytes: 64
```

Live run:

```text
frames=4000
target rate=4000 Hz
elapsed=1.003s
effectiveRate=3987.267 fps
smpCnt=0..3999
```

Fixed-duration live run after adding long-running publish controls:

```text
duration=5 seconds
frames=20000
target rate=4000 Hz
elapsed=5.005s
effectiveRate=3995.682 fps
smpCnt=0..19999
```

## Safety Contract

- Active publishing requires `--yes`.
- Use `--dry-run` before live output.
- Use `--duration-sec` for bounded soak tests.
- Use `--continuous` only when you intend to stop manually with `Ctrl+C`.
- Use `list-adapters`; do not guess adapter indexes.
- Send live traffic only on an isolated test NIC/TAP or lab switch.
- Do not use an office network or production substation network.

## Limitations

- The demo sample payload is generated from SCL DataSet order, but it is not yet
  a general engineering-value binding system.
- The current live publisher sends one selected SV stream per command.
- The pacing clock is software-based and subject to OS scheduling jitter.
- There is no live subscriber verification command yet.
- APPID conflicts are reported by SCL inspection but are not automatically
  resolved; select the intended stream explicitly with `--stream-index`.
