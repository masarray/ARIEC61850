# ARIEC61850 Quick Start

This quick start validates the current clean-room IEC 61850 stack from source.

## Requirements

- .NET 8 SDK.
- Windows for live Npcap publishing.
- Npcap installed when using raw Ethernet commands.
- An isolated Ethernet adapter, TAP, or lab switch for active SV publishing.

## Build and test

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

## Inspect the 9-2LE sample SCL

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd"
```

Expected high-level result:

- 3 IEDs.
- 3 Sampled Values streams.
- 16 DataSet entries per stream.
- SV destination MAC addresses `01:0C:CD:04:00:01` through
  `01:0C:CD:04:00:03`.

## Generate and inspect a PCAP

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap --sv-frames 32 --goose-frames 4
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\out\processbus-demo.pcap --delay-ms 50 --limit 20
```

## Publish SV to a lab adapter

List Npcap adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Dry-run first:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --dry-run
```

Publish for one minute:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --duration-sec 60 --yes
```

Publish continuously until `Ctrl+C`:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --continuous --yes
```

Replace `--adapter 5` with the adapter index from your machine.

## Safety boundary

Active publishing sends raw multicast Ethernet frames. Use only an isolated lab
NIC, TAP, or test switch. Do not use a production substation network.
