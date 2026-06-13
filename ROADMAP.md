# ARIEC61850 Roadmap

## Current validated milestone

### N5.19 - Smart GOOSE sniffer diagnostics and publisher state consistency

- Added SCL-bound GOOSE stream binding in the process-bus monitor.
- Added GOOSE sequence classification: first frame, retransmission, state change, duplicate, sequence jump, sequence regression, and state regression.
- Added TimeAllowedToLive supervision, arrival-gap counters, timeout counters, and diagnostics.
- Added decoded `allData` value summaries mapped to SCL DataSet order when SCL is available.
- Added changed-value summaries and diagnostics for values changing without `stNum` increment, or `stNum` changing without decoded value changes.
- Fixed demo PCAP and live publisher payload generation so retransmissions keep stable payloads until a real state change.
- Exposed GOOSE `seq`, TAL, SCL binding, changed counts, and diagnostics in `inspect-pcap` and `stream-pcap`.
- Added unit tests for SCL-bound GOOSE retransmission, valid state change, invalid value change without state increment, and TAL expiry.

Validation:

- `dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' build .\apps\AR.Iec61850.Cli\AR.Iec61850.Cli.csproj -c Release --no-restore --no-incremental`
- `dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' build .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-restore --no-incremental`
- `dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-build`
- `dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll generate-pcap .\samples\scl\minimal-station.scd .\out\n5-19-goose-demo.pcap`
- `dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll inspect-pcap .\out\n5-19-goose-demo.pcap --scl .\samples\scl\minimal-station.scd`
- `dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll stream-pcap .\out\n5-19-goose-demo.pcap --scl .\samples\scl\minimal-station.scd --delay-ms 0 --limit 20`

Known remaining gap:

- Solution-wide build currently reaches core, Npcap transport, tests, and CLI, then fails in this local environment at the WPF `*_wpftmp` project because the temporary WPF assets file under `.artifacts\obj` is not generated. Treat this as a WPF build-layout issue, not a GOOSE stack regression.

## Near term

- Keep repository public-safe: source only, no generated evidence, no unrelated protocol project content.
- Promote MMS report setup into a guided wizard: connect, discover, select DataSet/RCB, validate, enable, monitor, cleanup.
- Add a runtime reporting workspace with active RCB, DataSet members, GI indicator, report timeline, sequence diagnostics, and evidence export.
- Improve WPF SV Publisher usability and release polish.
- Expand automated report planner and receive-pump tests.
- Add live GOOSE/SV subscriber CLI loops over Npcap receive so the same stream monitor can run against adapter traffic, not only PCAP playback.
- Add live GoCB discovery/readback over MMS: `GoEna`, `GoID`, `DatSet`, `ConfRev`, `NdsCom`, `MinTime`, `MaxTime`, and `DstAddress`.

## Mid term

- Improve multi-vendor SCL and reporting compatibility evidence.
- Add MMS file, log, setting-group, and selected control-model services.
- Add simulator/training mode for offline demos and protocol learning.

## Long term

- Prepare formal validation evidence for selected protocol areas.
- Add security-profile work where practical and safe.
- Publish stable release notes for each tagged public release.
