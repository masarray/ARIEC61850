# SCL Publish MVP Validation Note

Date: 2026-06-12

## Scope

This note covers the first usable SCL-driven publish path in `AR.Iec61850`.

Validated path:

```text
SCL file
-> SclParser
-> SclSampledValuesStream / SclGooseStream
-> SampledValuesPublisherProfile / GoosePublisherProfile
-> SampledValuesPublisherSession / GoosePublisherSession
-> InMemoryProcessBusTransport
-> frame parser round-trip
-> optional PCAP export through AR.Iec61850.Cli
```

## Evidence

Automated tests cover:

- SCL parsing of one sample station file.
- DataSet entry order.
- SV address extraction: APPID, destination MAC, VLAN ID, VLAN priority.
- GOOSE address extraction: APPID, destination MAC, VLAN ID, VLAN priority.
- ReportControl extraction.
- basic duplicate IED conflict detection.
- SCL-to-SV frame build and parse-back.
- SCL-to-GOOSE frame build and parse-back.
- SV `smpCnt` increment and wrap from `65535` to `0`.
- GOOSE `stNum` and `sqNum` behavior for retransmit and state change.
- CLI `inspect-scl` smoke run on `samples/scl/minimal-station.scd`.
- CLI `generate-pcap` smoke run producing 32 SV frames and 4 GOOSE frames.
- CLI `inspect-pcap` smoke run reading the generated PCAP back as 1 SV stream
  and 1 GOOSE stream with zero unknown frames.
- CLI `stream-pcap` smoke run producing decoded SV/GOOSE event output with
  visible `smpCnt`, `stNum`, and `sqNum`.
- CLI `list-adapters` smoke run through the Npcap transport project.
- CLI `publish-sv-live` dry-run and live SV smoke run against the 9-2LE sample
  SCL. See `docs/validation/live-sv-publish.md`.
- PCAP global header magic/version/linktype verified.

Run:

```powershell
dotnet test ARIEC61850.slnx
dotnet build ARIEC61850.slnx -c Release
dotnet run --project apps\AR.Iec61850.Cli -- inspect-scl samples\scl\minimal-station.scd
dotnet run --project apps\AR.Iec61850.Cli -- generate-pcap samples\scl\minimal-station.scd out\processbus-demo.pcap --sv-frames 32 --goose-frames 4
dotnet run --project apps\AR.Iec61850.Cli -- inspect-pcap out\processbus-demo.pcap
dotnet run --project apps\AR.Iec61850.Cli -- stream-pcap out\processbus-demo.pcap --delay-ms 50 --limit 20
dotnet run --project apps\AR.Iec61850.Cli -- list-adapters
dotnet run --project apps\AR.Iec61850.Cli -- publish-sv-live "samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --dry-run
```

## Limitations

- Live raw Ethernet output exists for SV publisher smoke testing through Npcap,
  but should be used only on isolated lab adapters.
- SV sample payload has a demo 4I+4V generator for the sample SCL; a general
  engineering-value binding and payload packer is still next.
- SV real-time pacing is software-based and not protection-grade.
- GOOSE retransmission timing schedule is not implemented.
- SCL parser has one minimal fixture; vendor fixture coverage is still thin.
- This is not conformance evidence.
- Hardware interoperability evidence is limited to local raw transmit smoke
  testing.

## Next Validation Targets

- Add typed SV payload packing tests from SCL DataSet entries.
- Add generated-frame PCAP export and inspect with a trusted analyzer.
- Add GOOSE retransmission schedule tests.
- Add more SCL fixtures with multiple IEDs, multiple AccessPoints, duplicate
  APPIDs, missing addresses, and vendor namespace variations.
- Validate Npcap raw publish in an isolated lab network before any product UI
  exposes live publishing.
