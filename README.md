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
- SCL parsing for expected GOOSE, SV, ReportControl, DataSet entries, transport
  addresses, and basic conflicts.
- SCL-backed GOOSE/SV publisher profiles and in-memory publisher sessions.
- Npcap-backed raw Ethernet transport for live SV publish smoke tests.

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
- SCL import from SCD/CID/ICD/IID-style XML files.
- Publish profiles that convert SCL `GSEControl` and `SampledValueControl` into
  frame builders.
- In-memory publisher sessions for deterministic tests and future UI/CLI use.
- Raw Ethernet adapter discovery and live SV publish through
  `AR.Iec61850.Transports.Npcap`.
- BER length/tag/value support with definite short and long-form lengths.
- MMS data types commonly needed by GOOSE and reporting payloads.

## Minimal Usage

```csharp
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;
using AR.Iec61850.Transports;

var scl = new SclParser().Load("station.scd");
var profile = SampledValuesPublisherProfile.FromScl(scl);
var transport = new InMemoryProcessBusTransport();
var session = new SampledValuesPublisherSession(
    profile,
    MacAddress.Parse("02:00:00:00:20:01"),
    transport);

await session.PublishNextAsync(Convert.FromHexString("0000006400000001"));
var frameBytes = transport.Frames[0];
```

## Try It Locally

Inspect the sample SCL:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
```

Generate a PCAP containing SCL-backed SV and GOOSE frames:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap --sv-frames 32 --goose-frames 4
```

Inspect that PCAP with the stack itself:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap
```

Stream decoded SV/GOOSE events to the console:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\out\processbus-demo.pcap --delay-ms 50 --limit 20
```

You can also open `out/processbus-demo.pcap` in Wireshark.

List Npcap adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Dry-run an SCL-backed SV publisher without sending to the NIC:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --dry-run
```

Publish one second of 9-2LE-style SV traffic to a selected isolated Ethernet
adapter:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --yes
```

Publish for a longer duration:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --duration-sec 60 --yes
```

Publish continuously until `Ctrl+C`:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --continuous --yes
```

Use `--adapter` from `list-adapters`. Active publishing sends raw multicast
Ethernet frames and should be used on an isolated test NIC/TAP, not an office
network. This software-paced publisher is for lab validation, not protection
grade timing claims.

Next milestones:

- GOOSE and SV subscribers with SCL binding.
- ISO-on-TCP, TPKT, COTP, ACSE, Presentation, MMS initiate.
- MMS discovery client and reports.
- MMS report-control client and InformationReport parser.
- SCL-backed server data model and report generation.
