# GOOSE Engine Audit

Status date: 2026-06-14

## Why this exists

GOOSE support must become more than a frame encoder/decoder. For a commissioning tool, the useful behavior is SCL-aware discovery, stream supervision, event evidence, and safe publishing with protocol state that can be inspected.

This audit is based on public product/API material and local validation:

- libiec61850 GOOSE subscriber API: https://support.mz-automation.de/doc/libiec61850/c/latest/group__goose__api__group.html
- DigSubAnalyzer: https://github.com/masarray/DigSubAnalyzer
- OMICRON IEDScout: https://www.omicronenergy.com/en/products/iedscout/
- OMICRON StationScout: https://www.omicronenergy.com/en/products/stationscout/

No third-party IEC 61850 stack is vendored as a runtime dependency.

## Capability target

The target is a smart GOOSE stack that can support products in the same problem space as IEDScout and StationScout:

- import SCL and build publish/subscribe profiles;
- discover streams from passive traffic;
- bind traffic to SCL by APPID, destination MAC, GoCB reference, DataSet reference, GoID, and ConfRev;
- decode `allData` and preserve semantic order from the DataSet;
- supervise `stNum`, `sqNum`, TimeAllowedToLive, `test`, `ndsCom`, ConfRev, VLAN, source, and destination;
- explain why a frame is trusted, weakly matched, mismatched, or anonymous;
- publish deterministic GOOSE frames with correct state/retransmission behavior;
- keep active publishing guarded behind explicit lab confirmation.

## N5.19 implemented

Core monitor:

- `ProcessBusStreamMonitor` now accepts SCL-derived GOOSE publisher profiles.
- GOOSE frames are bound to SCL profiles by exact APPID, destination MAC, GoCB reference, and ConfRev, with weaker fallbacks by GoCB reference, DataSet reference, and GoID.
- Decoded `allData` values are exposed as `GooseDecodedValue` entries with index, SCL signal reference, FC, CDC, bType, display value, previous value, and changed flag.
- `ProcessBusStreamSummary` tracks GOOSE state changes, retransmissions, duplicates, jumps, regressions, TAL expiry, arrival gaps, and value changes.
- Diagnostics now flag `test`, `ndsCom`, zero TAL, ConfRev mismatch, destination MAC mismatch, DataSet count mismatch, TAL expiry, value changes without `stNum` increment, and `stNum` changes without decoded value changes.

Publisher:

- Demo PCAP and live publisher payload generation now keep payload values stable during retransmission.
- Payload values change only when a state change is requested, so `stNum` and changed values stay aligned.
- `sqNum` can represent retransmission evidence instead of hiding publisher payload churn.

CLI:

- `inspect-pcap` GOOSE summary now shows TAL, state-change count, retransmission count, jumps, duplicates, regressions, timeouts, value-change count, and changed-value summary.
- `stream-pcap` now shows per-frame GOOSE sequence status, TAL, SCL binding, value count, changed count, changed summary, and diagnostics.

Tests:

- SCL-bound GOOSE retransmission.
- Valid GOOSE state change with changed values.
- Invalid value change without state-number increment.
- TAL expiry.

## Comparison with libiec61850

libiec61850 provides a strong low-level C API for GOOSE subscription. Its public API exposes filtering by destination MAC and APPID, validity and parse-error state, `stNum`, `sqNum`, `test`, `confRev`, `ndsCom`, TimeAllowedToLive, timestamp, VLAN, and DataSet values. It also documents the expected `stNum` and `sqNum` relationship: `sqNum` increases for consecutive messages without state change and resets when `stNum` increases.

ARIEC61850 is still behind libiec61850 in these areas:

- no live GOOSE receiver loop exposed as a ready CLI command yet;
- no R-GOOSE receiver/publisher;
- no hardened multi-threaded live subscriber lifecycle;
- no formal conformance evidence;
- no broad multi-vendor traffic corpus.

ARIEC61850 is moving beyond a raw subscriber API in these areas:

- SCL-bound semantic diagnostics are part of the monitor result;
- value-change summaries are compared against the GOOSE state machine;
- ambiguous or anonymous traffic remains visible instead of being silently dropped;
- the same monitor model supports PCAP replay now and can be reused by the future live receive loop;
- publisher dry-run and active publish share the same state-machine path.

## DigSubAnalyzer learning

DigSubAnalyzer is receive-only and raw-passive. Its useful product lessons for ARIEC61850 are:

- show adapter, APPID, stream ID, source MAC, sequence continuity, and timing confidence as evidence;
- avoid claiming certification-grade timing from ordinary software timestamps;
- make passive discovery useful even without SCL, but mark semantics as anonymous when SCL binding is missing;
- keep active publishing and control behavior out of passive analyzer workflows.

ARIEC61850 now adopts the same evidence-first posture for GOOSE, while remaining a full stack that can also publish in isolated labs.

## IEDScout and StationScout learning

Public OMICRON material emphasizes workflows that visualize SCL, trace signals, compare configuration with live traffic, inspect activity, and simulate IEDs/GOOSE for testing. The stack direction should therefore prioritize:

- SCL and live traffic comparison;
- clear PASS/WARNING/FAIL/UNKNOWN diagnostics per stream;
- signal tracing from GoCB/DataSet member to received `allData` value;
- simulation/publishing that is deterministic and bounded;
- evidence export for commissioning review.

## Current validation

Commands run for N5.19:

```powershell
dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' build .\apps\AR.Iec61850.Cli\AR.Iec61850.Cli.csproj -c Release --no-restore --no-incremental
dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' build .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-restore --no-incremental
dotnet 'C:\Program Files\dotnet\sdk\10.0.301\dotnet.dll' test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-build
dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll generate-pcap .\samples\scl\minimal-station.scd .\out\n5-19-goose-demo.pcap
dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll inspect-pcap .\out\n5-19-goose-demo.pcap --scl .\samples\scl\minimal-station.scd
dotnet .\.artifacts\bin\AR.Iec61850.Cli\Release\net8.0\AR.Iec61850.Cli.dll stream-pcap .\out\n5-19-goose-demo.pcap --scl .\samples\scl\minimal-station.scd --delay-ms 0 --limit 20
```

Observed PCAP evidence:

- 20 decoded process-bus frames.
- 1 GOOSE stream, 4 frames.
- `TAL=1000ms`.
- `stateChanges=1`.
- `retrans=2`.
- `timeouts=0`.
- retransmission frames show `changed=0`.
- final state-change frame shows `seq=StateChange` and changed Boolean/timestamp values.

## Remaining roadmap

Next safe patches:

1. Add `goose-subscribe-live` CLI using Npcap receive and `ProcessBusStreamMonitor`.
2. Add bounded capture evidence export: JSON summary, event log, and optional CSV values.
3. Add live MMS GoCB discovery/readback for `GoEna`, `GoID`, `DatSet`, `ConfRev`, `NdsCom`, `MinTime`, `MaxTime`, and `DstAddress`.
4. Add quality bit decoding for common GOOSE quality values.
5. Add replay tests with malformed GOOSE frames, unknown tags, length mismatch, sequence jumps, duplicate frames, and ConfRev mismatch.
6. Add anonymous stream registry for traffic without SCL and later bind it when SCL is loaded.
7. Add live adapter soak tests with real relay or simulator captures.
8. Add R-GOOSE only after normal Ethernet GOOSE receive/publish is stable.

## Claim boundary

Current claim: SCL-backed GOOSE frame generation, PCAP decode, passive stream monitoring, state/retransmission diagnostics, TAL supervision, and bounded lab publishing are implemented and unit tested.

Do not claim yet: formal IEC 61850 conformance, production-grade timing proof, live adapter subscriber CLI readiness, R-GOOSE support, or broad vendor interoperability.
