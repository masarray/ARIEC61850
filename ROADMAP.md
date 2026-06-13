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

## Current source patch

### N5.20 - Live GOOSE subscriber receive path

- Added core receive-side process-bus abstraction: `IProcessBusFrameSource`, `ProcessBusCapturedFrame`, and `ProcessBusCaptureOptions`.
- Added an Npcap receive implementation with bounded buffering, BPF filter support, cancellation, and adapter cleanup.
- Added `goose-subscribe-live --adapter <index|name>` CLI. It is read-only, defaults to `ether proto 0x88b8`, supports optional SCL binding, and reuses `ProcessBusStreamMonitor` so live output matches PCAP replay diagnostics.
- Added in-memory frame-source unit tests for deterministic capture ordering and cancellation behavior.
- Added repository hygiene work for public source release: local `NuGet.Config`, stronger ignore/cleanup/verification scripts, and staged removal of tracked build artifacts and private/generated samples.

Validation status:

- Compile/test for this patch is blocked in the current sandbox because .NET restore tries to read `C:\Users\me\AppData\Roaming\NuGet\NuGet.Config`, which is outside the workspace and not currently accessible.
- `--no-restore` is also blocked after source-clean cleanup because the required `project.assets.json` files were intentionally removed with build artifacts.
- Before promoting N5.20 to the validated milestone, run CLI build, test suite, `verify-source-clean`, and a live adapter smoke capture on a Windows lab PC.

## Near term

- Keep repository public-safe: source only, no generated evidence, no unrelated protocol project content.
- Promote MMS report setup into a guided wizard: connect, discover, select DataSet/RCB, validate, enable, monitor, cleanup.
- Add a runtime reporting workspace with active RCB, DataSet members, GI indicator, report timeline, sequence diagnostics, and evidence export.
- Improve WPF SV Publisher usability and release polish.
- Expand automated report planner and receive-pump tests.
- Validate live GOOSE subscriber over Npcap receive, then add SV subscriber loop over the same abstraction.
- Add live GoCB discovery/readback over MMS: `GoEna`, `GoID`, `DatSet`, `ConfRev`, `NdsCom`, `MinTime`, `MaxTime`, and `DstAddress`.

## Mid term

- Improve multi-vendor SCL and reporting compatibility evidence.
- Add MMS file, log, setting-group, and selected control-model services.
- Add simulator/training mode for offline demos and protocol learning.

## Long term

- Prepare formal validation evidence for selected protocol areas.
- Add security-profile work where practical and safe.
- Publish stable release notes for each tagged public release.
